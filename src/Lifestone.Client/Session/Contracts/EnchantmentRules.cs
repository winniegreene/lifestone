using System.Collections.Immutable;
using Lifestone.Data.Dat;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// How the enchantment registry changes an attribute or a secondary attribute (a vital's maximum), re-expressed from retail: <c>CEnchantmentRegistry::EnchantAttribute</c> (11.4186 0x00594570,
/// 11.6096 0x005953E0), <c>CEnchantmentRegistry::EnchantAttribute2nd</c> (0x00594670 / 0x005954E0), <c>CEnchantmentRegistry::CullEnchantmentsFromList</c> (0x00594470 / 0x005952E0),
/// <c>CEnchantmentRegistry::Duel</c> (0x005942B0 / 0x00595120), <c>Enchantment::Duel</c> (0x005CACE0 / 0x005CBC10) and <c>Enchantment::Enchant</c> (0x005CAD10 / 0x005CBC40).
/// </summary>
/// <remarks>
/// <para>The rule. Each list (multiplicative, then additive, in the order the registry holds them) is culled for the stat asked: an enchantment counts when its type has the kind's bit
/// (attribute 0x1, secondary attribute 0x2) and either names several stats (0x2000) or has the stat's key. The culled enchantments duel by spell category: for one category only
/// the strongest stays (higher power level; at equal power the one that started later; the first of the list on a tie of both), so the layers of one spell do not stack and two spells of one
/// category do not either. Then the survivors apply in order: an additive one (0x8000) adds its value, a multiplicative one (0x4000) multiplies by it. The result is kept at 1 (base below the
/// floor) or at the floor (attribute 10, secondary attribute 5), then rounded half up.</para>
/// <para>The quality filter (M3 2.2 a1; <c>TODO(g4-quality-filter)</c> is closed). <c>EnchantAttribute2nd</c> first asks the portal's quality filter (<c>DBObj::GetByEnum</c> of the enum 0x10000002 in enum group 3 for the
/// object type 0x1000000C, a portal file 0x0E01xxxx; <c>ACQualityFilter::QueryAttribute2nd</c>, 11.6096 0x005CCBC0) whether the secondary attribute may be enchanted: when it may not, the value is left as it is.
/// <see cref="Attribute2ndFilter"/> holds the table read from the dat (<c>QualityFilterData.LoadEnchantable</c>; the file keeps the secondary attributes 1, 3 and 5, the three maximums); until a session that
/// has the dats loads it (<c>GameSession.UseStatTablesFrom</c>, M3 2.2 a3: <c>TODO(g4-quality-filter-install)</c> is closed), every secondary attribute is taken as enchantable, which gives the same answers for the maximums.
/// The cull's two extra clauses (attack and defence skill groups, <c>Enchantment::AffectsAttackSkills</c> and <c>AffectsDefenseSkills</c>; M3 2.2 a2, <see cref="AffectsKey"/>) name skill keys
/// only, so they never hold for an attribute's or a vital's key.</para>
/// </remarks>
public static class EnchantmentRules
{
    private static volatile QualityFilterData? _attribute2ndFilter;

    /// <summary>
    /// The portal's quality filter for the secondary attributes (<c>QualityFilterData.LoadEnchantable</c>), or null until a session with the dats loads it (<c>GameSession.UseStatTablesFrom</c>; every secondary attribute then counts as enchantable,
    /// until then). Set once at start-up; the vitals' maximums read it.
    /// </summary>
    public static QualityFilterData? Attribute2ndFilter
    {
        get => _attribute2ndFilter;
        set => _attribute2ndFilter = value;
    }

    /// <summary>Type bit: the enchantment changes a primary attribute.</summary>
    public const uint AttributeKind = 0x1;

    /// <summary>Type bit: the enchantment changes a secondary attribute (a vital's maximum or current value).</summary>
    public const uint SecondaryAttributeKind = 0x2;

    /// <summary>Type bit: the enchantment names several stats, so its key is not compared (the second byte's 0x20 in the cull).</summary>
    public const uint MultipleStats = 0x2000;

    /// <summary>Type bit: the enchantment multiplies (the second byte's 0x40 in <c>Enchantment::Enchant</c>).</summary>
    public const uint Multiplicative = 0x4000;

    /// <summary>Type bit: the enchantment adds (the second byte's 0x80; tested first).</summary>
    public const uint Additive = 0x8000;

    /// <summary>Type bit: the enchantment is the vitae (the single enchantment <c>CEnchantmentRegistry::UpdateVitae</c> keeps outside the lists; ACE's <c>EnchantmentTypeFlags.Vitae</c>).</summary>
    public const uint VitaeKind = 0x800000;

    /// <summary>Type bit: the enchantment is a cooldown (it goes in the registry's cooldown list; ACE's <c>EnchantmentTypeFlags.Cooldown</c>).</summary>
    public const uint CooldownKind = 0x1000000;

    /// <summary>Type bit: the enchantment is beneficial (<c>CEnchantmentRegistry::PurgeBadEnchantmentList</c> keeps it; ACE's <c>EnchantmentTypeFlags.Beneficial</c>).</summary>
    public const uint BeneficialKind = 0x2000000;

    /// <summary>The vitae's spell id (666, 0x029A): retail's <c>CEnchantmentRegistry::IsEnchanted</c> tests it by number.</summary>
    public const ushort VitaeSpellId = 666;

    /// <summary>
    /// How long past its expiry an enchantment may stay before it counts as overdue (<c>TODO(m3-enchant-overdue)</c>): ACE removes expired enchantments in the player's heartbeat (5 s) and the 02C3
    /// follows in the next send, so two heartbeats. A Lifestone-chosen allowance, not a retail number (retail's client never times an enchantment out).
    /// </summary>
    public const double OverdueGraceSeconds = 10.0;

    /// <summary>
    /// A primary attribute with the registry applied (<c>CACQualities::InqAttribute</c> with <c>raw</c> false asks <c>EnchantAttribute</c>): <paramref name="id"/> is the attribute (1 Strength ... 6 Self),
    /// <paramref name="raw"/> its start plus ranks.
    /// </summary>
    public static uint EnchantAttribute(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw)
    {
        if (enchantments.IsDefaultOrEmpty)
        {
            return raw;
        }

        var value = (float)raw;
        var affecting = new List<EnchantmentDesc>(4);
        Cull(enchantments, EnchantmentBucket.Multiplicative, AttributeKind, id, affecting);
        Cull(enchantments, EnchantmentBucket.Additive, AttributeKind, id, affecting);
        foreach (var enchantment in affecting)
        {
            value = Enchant(enchantment, value);
        }

        var floor = raw < 10 ? 1.0f : 10.0f; // retail keeps a raised or lowered attribute at 1 (base below 10) or at 10
        return (uint)((double)Math.Max(value, floor) + 0.5);
    }

    /// <summary>
    /// A secondary attribute with the registry applied (<c>CACQualities::InqAttribute2nd</c> with <c>raw</c> false asks <c>EnchantAttribute2nd</c>): <paramref name="id"/> is the secondary attribute's id (1
    /// maximum health, 3 maximum stamina, 5 maximum mana), <paramref name="raw"/> the value before enchantments. The vitae applies first, whatever its key.
    /// </summary>
    public static uint EnchantSecondaryAttribute(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw) =>
        EnchantSecondaryAttributeFiltered(enchantments, id, raw, Attribute2ndFilter);

    /// <summary>
    /// <see cref="EnchantSecondaryAttribute"/> with the quality filter given: when <paramref name="filter"/> does not keep the secondary attribute <paramref name="id"/>
    /// (<c>ACQualityFilter::QueryAttribute2nd</c> answers 0), <c>EnchantAttribute2nd</c> does nothing and the value stays <paramref name="raw"/>. A null filter takes every secondary attribute as enchantable.
    /// </summary>
    public static uint EnchantSecondaryAttributeFiltered(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw, QualityFilterData? filter)
    {
        if (enchantments.IsDefaultOrEmpty || (filter is not null && !filter.QueryAttribute2nd(id)))
        {
            return raw;
        }

        var value = (float)raw;
        foreach (var enchantment in enchantments)
        {
            if (enchantment.Bucket == EnchantmentBucket.Vitae)
            {
                value = Enchant(enchantment, value);
            }
        }

        var affecting = new List<EnchantmentDesc>(4);
        Cull(enchantments, EnchantmentBucket.Multiplicative, SecondaryAttributeKind, id, affecting);
        Cull(enchantments, EnchantmentBucket.Additive, SecondaryAttributeKind, id, affecting);
        foreach (var enchantment in affecting)
        {
            value = Enchant(enchantment, value);
        }

        var floor = raw < 5 ? 1.0f : 5.0f; // retail keeps a secondary attribute at 1 (base below 5) or at 5
        return (uint)((double)Math.Max(value, floor) + 0.5);
    }

    /// <summary><c>Enchantment::Enchant</c>: adds when the type says additive, else multiplies when it says multiplicative, else leaves the value.</summary>
    public static float Enchant(in EnchantmentDesc enchantment, float value)
    {
        if ((enchantment.StatModType & Additive) != 0)
        {
            return enchantment.StatModValue + value;
        }

        return (enchantment.StatModType & Multiplicative) != 0 ? enchantment.StatModValue * value : value;
    }

    /// <summary><c>Enchantment::Duel</c>: true when <paramref name="existing"/> keeps its place against <paramref name="challenger"/> (a higher power level, or an equal one that started later).</summary>
    public static bool Holds(in EnchantmentDesc existing, in EnchantmentDesc challenger) =>
        existing.PowerLevel > challenger.PowerLevel || (existing.PowerLevel == challenger.PowerLevel && challenger.StartTime < existing.StartTime);

    /// <summary>Type bit: the enchantment affects the attack skill group (ACE <c>EnchantmentTypeFlags.AttackSkills</c>; retail's <c>Enchantment::AffectsAttackSkills</c> tests the third byte's 0x1).</summary>
    public const uint AttackSkillsKind = 0x10000;

    /// <summary>Type bit: the enchantment affects the defence skill group (ACE <c>EnchantmentTypeFlags.DefenseSkills</c>; retail's <c>Enchantment::AffectsDefenseSkills</c> tests 0x20000).</summary>
    public const uint DefenseSkillsKind = 0x20000;

    /// <summary>
    /// The key match of <c>CEnchantmentRegistry::CullEnchantmentsFromList</c> (11.4186 0x00594470, 11.6096 0x005952E0), after the kind bit: the enchantment names several stats (0x2000), or its key is
    /// <paramref name="key"/>, or <c>Enchantment::AffectsAttackSkills</c> (11.6096 0x005CBD10: the attack group bit and <paramref name="key"/> one of the skills 0x21, 0x22, 0x29, 0x2B to 0x2F, 0x31) or
    /// <c>Enchantment::AffectsDefenseSkills</c> (0x005CBD60: the defence group bit and <paramref name="key"/> one of the skills 6, 7, 0xF, 0x30) holds. Closes <c>TODO(g4-enchantment-affects-skills)</c>:
    /// a skill enchantment aimed at a group lifts every skill in it. (The numbers are the end-of-retail client's own skill ids.)
    /// </summary>
    /// <param name="enchantment">The enchantment.</param>
    /// <param name="key">The stat asked for: an attribute, a vital or a skill id.</param>
    public static bool AffectsKey(in EnchantmentDesc enchantment, uint key)
    {
        var type = enchantment.StatModType;
        if ((type & MultipleStats) != 0 || enchantment.StatModKey == key)
        {
            return true;
        }

        if ((type & AttackSkillsKind) != 0 && key is 0x21 or 0x22 or 0x29 or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x31)
        {
            return true;
        }

        return (type & DefenseSkillsKind) != 0 && key is 6 or 7 or 0xF or 0x30;
    }

    /// <summary><c>CEnchantmentRegistry::CullEnchantmentsFromList</c> over one list of the registry: the enchantments that affect <paramref name="key"/>, duelled into <paramref name="affecting"/>.</summary>
    private static void Cull(ImmutableArray<EnchantmentDesc> enchantments, EnchantmentBucket bucket, uint kind, uint key, List<EnchantmentDesc> affecting)
    {
        foreach (var enchantment in enchantments)
        {
            if (enchantment.Bucket == bucket && (enchantment.StatModType & kind) != 0 && AffectsKey(in enchantment, key))
            {
                Duel(enchantment, affecting);
            }
        }
    }

    /// <summary><c>CEnchantmentRegistry::Duel</c>: the challenger meets the one enchantment of its spell category in <paramref name="list"/>; it takes the place at the list's tail when it wins or there is none.</summary>
    private static void Duel(in EnchantmentDesc challenger, List<EnchantmentDesc> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].SpellCategory != challenger.SpellCategory)
            {
                continue;
            }

            if (Holds(list[i], challenger))
            {
                return;
            }

            list.RemoveAt(i);
            break;
        }

        list.Add(challenger);
    }

    // ------------------------------------------------------------------ the registry's edits (the messages 02C2 to 02C8 and 0312)

    /// <summary>
    /// The list of the registry an enchantment belongs to, by retail's rule (<c>CEnchantmentRegistry::UpdateEnchantment</c>, 11.4186 0x00593E70, 11.6096 0x00594CE0): the vitae bit (or the vitae's spell,
    /// 666) is the single vitae, the cooldown bit the cooldown list, the multiplicative bit the multiplicative list, else the additive one. Retail first drops an enchantment whose multiplicative,
    /// additive and cooldown bits do not xor to one (none of them, or two): then <c>false</c> (<c>TODO(m3-enchant-type-parity)</c>: the test is read from the decompile; no real ACE enchantment fails it).
    /// Closes <c>TODO(m3-enchant-bucket)</c>: net's codec derives the bucket from the spell id 0x029A and the category 0x8000, which agrees with these bits for every enchantment ACE sends (the vitae spell's
    /// type has the vitae bit, a cooldown's type is the cooldown bit alone) but is not retail's mechanism; the reducer applies this rule and keeps the result in <see cref="EnchantmentDesc.Bucket"/>.
    /// </summary>
    public static bool TryClassify(in EnchantmentDesc enchantment, out EnchantmentBucket bucket)
    {
        var type = enchantment.StatModType;
        bucket = default;
        if (((type & Multiplicative) != 0) ^ ((type & Additive) != 0) ^ ((type & CooldownKind) != 0))
        {
            bucket = (type & VitaeKind) != 0 || enchantment.SpellId == VitaeSpellId ? EnchantmentBucket.Vitae
                : (type & CooldownKind) != 0 ? EnchantmentBucket.Cooldown
                : (type & Multiplicative) != 0 ? EnchantmentBucket.Multiplicative
                : EnchantmentBucket.Additive;
            return true;
        }

        return false;
    }

    /// <summary>The position of a bucket in the registry's wire order (multiplicative, additive, cooldown, vitae), which the flat list keeps.</summary>
    private static int Rank(EnchantmentBucket bucket) => bucket switch
    {
        EnchantmentBucket.Multiplicative => 0,
        EnchantmentBucket.Additive => 1,
        EnchantmentBucket.Cooldown => 2,
        _ => 3,
    };

    /// <summary>What <see cref="Update"/> did.</summary>
    internal enum UpdateOutcome
    {
        /// <summary>The enchantment was inserted at the head of its list (or became the vitae).</summary>
        Added,

        /// <summary>The enchantment overwrote an existing one in place.</summary>
        Replaced,

        /// <summary>Retail's first test refused it (<see cref="TryClassify"/>); nothing changed.</summary>
        Rejected,
    }

    /// <summary>
    /// <c>CEnchantmentRegistry::UpdateEnchantment</c> over the flat registry (<paramref name="registry"/>: multiplicative, additive, cooldown, then the vitae). The vitae replaces the one it has
    /// (<c>UpdateVitae</c>, 11.4186 0x00593A20, 11.6096 0x00594890). Any other enchantment first overwrites, in place, every entry of its list with the same layered id
    /// (<c>ReplaceEnchantmentInList</c>, 0x00593BB0 / 0x00594A20); failing that, a timed one (duration above 0) overwrites the first timed entry of its list with the same spell whatever the layer
    /// (<c>AttemptToReplaceSpellInList</c>, 0x00593D00 / 0x00594B70, from <c>AddEnchantmentToList</c>, 0x00593DF0 / 0x00594C60); failing that it is inserted at the head of its list
    /// (<c>PackableList::InsertHead</c>), so a new buff is the first its list's duel sees on a tie.
    /// </summary>
    internal static UpdateOutcome Update(List<ActiveEnchantment> registry, ActiveEnchantment incoming)
    {
        if (!TryClassify(incoming.Enchantment, out var bucket))
        {
            return UpdateOutcome.Rejected;
        }

        incoming = incoming with { Enchantment = incoming.Enchantment with { Bucket = bucket } };
        if (bucket == EnchantmentBucket.Vitae)
        {
            var at = registry.FindIndex(e => e.Enchantment.Bucket == EnchantmentBucket.Vitae);
            if (at >= 0)
            {
                registry[at] = incoming;
                return UpdateOutcome.Replaced;
            }

            registry.Add(incoming);
            return UpdateOutcome.Added;
        }

        var replaced = false;
        for (var i = 0; i < registry.Count; i++)
        {
            var existing = registry[i].Enchantment;
            if (existing.Bucket == bucket && existing.SpellId == incoming.Enchantment.SpellId && existing.Layer == incoming.Enchantment.Layer)
            {
                registry[i] = incoming;
                replaced = true;
            }
        }

        if (replaced)
        {
            return UpdateOutcome.Replaced;
        }

        if (incoming.Enchantment.Duration > 0.0)
        {
            for (var i = 0; i < registry.Count; i++)
            {
                var existing = registry[i].Enchantment;
                if (existing.Bucket == bucket && existing.SpellId == incoming.Enchantment.SpellId && existing.Duration > 0.0)
                {
                    registry[i] = incoming;
                    return UpdateOutcome.Replaced;
                }
            }
        }

        var rank = Rank(bucket);
        var head = registry.FindIndex(e => Rank(e.Enchantment.Bucket) >= rank);
        registry.Insert(head < 0 ? registry.Count : head, incoming);
        return UpdateOutcome.Added;
    }

    /// <summary>
    /// <c>CEnchantmentRegistry::RemoveEnchantments</c> for one layered id (11.4186 0x00594B40, 11.6096 0x005959B0; <c>RemoveEnchantment</c> asks it for one): the vitae when its id matches, else the first
    /// match of the cooldown list, then of the multiplicative list, then of the additive list. Retail compares the whole layered id (spell in the low half, layer in the high half); ACE sends the vitae at
    /// layer 0 in the update and in the removal alike. Returns the entry removed, or null when there was none.
    /// </summary>
    internal static ActiveEnchantment? Remove(List<ActiveEnchantment> registry, ushort spellId, ushort layer)
    {
        foreach (var bucket in RemovalOrder)
        {
            var at = registry.FindIndex(e => e.Enchantment.Bucket == bucket && e.Enchantment.SpellId == spellId && e.Enchantment.Layer == layer);
            if (at >= 0)
            {
                var removed = registry[at];
                registry.RemoveAt(at);
                return removed;
            }
        }

        return null;
    }

    private static readonly EnchantmentBucket[] RemovalOrder =
    [
        EnchantmentBucket.Vitae,
        EnchantmentBucket.Cooldown,
        EnchantmentBucket.Multiplicative,
        EnchantmentBucket.Additive,
    ];

    /// <summary>
    /// <c>CEnchantmentRegistry::PurgeEnchantments</c> (it walks <c>PurgeEnchantmentList</c>, 11.4186 0x00594C50, 11.6096 0x00595AC0, over the multiplicative and the additive list): every enchantment of
    /// those lists whose duration is not -1 (an item's lasting spell stays). The vitae and the cooldown list are not touched. With <paramref name="badOnly"/> (<c>PurgeBadEnchantments</c> over
    /// <c>PurgeBadEnchantmentList</c>, 0x00594D10 / 0x00595B80) the beneficial ones (type bit 0x2000000) stay too. Returns what was removed, in registry order.
    /// </summary>
    internal static List<ActiveEnchantment> Purge(List<ActiveEnchantment> registry, bool badOnly)
    {
        var removed = new List<ActiveEnchantment>();
        registry.RemoveAll(e =>
        {
            var d = e.Enchantment;
            var kept = d.Bucket is not (EnchantmentBucket.Multiplicative or EnchantmentBucket.Additive)
                || d.Duration == -1.0
                || (badOnly && (d.StatModType & BeneficialKind) != 0);
            if (!kept)
            {
                removed.Add(e);
            }

            return !kept;
        });
        return removed;
    }
}

/// <summary>
/// One enchantment of the player's registry with the moment it arrived (<see cref="PlayerQualities.ActiveEnchantments"/>). The wire's start time is relative to the sender's clock when the message was
/// written (ACE: 0 at the cast, then minus the seconds elapsed); retail's <c>Enchantment::UnPack</c> (11.4186 0x005CB040, 11.6096 0x005CBF70) adds its own clock to it on arrival, so the entry keeps
/// the record as sent plus <see cref="ReceivedSeconds"/> and derives the start and the expiry from both, on the session clock.
/// </summary>
/// <param name="Enchantment">The record as the message carried it (its bucket is the registry list it sits in).</param>
/// <param name="ReceivedSeconds">When the message arrived, in <see cref="SessionClock"/> seconds.</param>
public readonly record struct ActiveEnchantment(EnchantmentDesc Enchantment, double ReceivedSeconds)
{
    /// <summary>The spell id.</summary>
    public ushort SpellId => Enchantment.SpellId;

    /// <summary>The layer.</summary>
    public ushort Layer => Enchantment.Layer;

    /// <summary>True for an enchantment with no end (duration below 0: an item's lasting spell, -1 on the wire).</summary>
    public bool IsPermanent => Enchantment.Duration < 0.0;

    /// <summary>When the enchantment started, in session-clock seconds (the received time plus the wire's start time).</summary>
    public double StartSeconds => ReceivedSeconds + Enchantment.StartTime;

    /// <summary>When the enchantment ends, in session-clock seconds; positive infinity when it is permanent.</summary>
    public double ExpirySeconds => IsPermanent ? double.PositiveInfinity : StartSeconds + Enchantment.Duration;

    /// <summary>The seconds left at <paramref name="nowSeconds"/> (session clock), never below 0; positive infinity when permanent.</summary>
    public double RemainingSeconds(double nowSeconds) => IsPermanent ? double.PositiveInfinity : Math.Max(0.0, ExpirySeconds - nowSeconds);

    /// <summary>
    /// When the enchantment ends on the server's clock (<see cref="ServerTimeState"/>: the server time estimated at arrival plus the wire's start time and the duration), or null while no server
    /// time is known or when permanent.
    /// </summary>
    /// <param name="time">The session's server time.</param>
    public double? ExpiryServerSeconds(ServerTimeState time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return IsPermanent || time.ServerSecondsAt(ReceivedSeconds) is not { } serverAtArrival ? null : serverAtArrival + Enchantment.StartTime + Enchantment.Duration;
    }

    /// <summary>True when the enchantment is past its expiry by more than <paramref name="graceSeconds"/> at <paramref name="nowSeconds"/> and no removal arrived (<c>TODO(m3-enchant-overdue)</c>).</summary>
    /// <param name="nowSeconds">Session-clock seconds.</param>
    /// <param name="graceSeconds">The allowance past the expiry.</param>
    public bool IsOverdue(double nowSeconds, double graceSeconds = EnchantmentRules.OverdueGraceSeconds) => !IsPermanent && nowSeconds - ExpirySeconds > graceSeconds;
}
