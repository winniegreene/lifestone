using System.Globalization;

namespace Lifestone.ServerMod;

/// <summary>A school of magic the bot casts in (ACE's <c>MagicSchool</c>, by name, so the core needs nothing from ACE).</summary>
internal enum BuffBotSchool
{
    /// <summary>Creature Enchantment.</summary>
    Creature,

    /// <summary>Item Enchantment.</summary>
    Item,

    /// <summary>Life Magic.</summary>
    Life,
}

/// <summary>One of the bot's own self buffs: the level VII Self spell, its name, the line, and the school whose casting it raises when it is a mastery (null: a line every caster of the bot's kind holds).</summary>
/// <param name="SpellId">The Self spell at level VII.</param>
/// <param name="Spell">Its name in the dat.</param>
/// <param name="Line">The line.</param>
/// <param name="MasteryOf">The school a mastery raises; null for the lines the bot holds whatever it casts.</param>
internal sealed record BuffBotSelfLine(uint SpellId, string Spell, string Line, BuffBotSchool? MasteryOf = null);

/// <summary>A self line as the bot stands now: known in its book, its cast chance, and the seconds left on it (0: not on the bot).</summary>
/// <param name="Line">The line.</param>
/// <param name="Known">The bot's book holds the spell.</param>
/// <param name="Chance">ACE's chance of the cast landing on the bot's skill (<see cref="BuffBotCasting.Chance"/>).</param>
/// <param name="SecondsLeft">ACE's time left on the enchantment (<c>Duration + StartTime</c>, EnchantmentManager.cs 188), 0 when none.</param>
internal readonly record struct BuffBotSelfStatus(BuffBotSelfLine Line, bool Known, double Chance, double SecondsLeft);

/// <summary>One dance spell as the bot holds it now: known, the component it lacks, its chance, ACE's cost of this cast, and the spell table's transfer or boost numbers.</summary>
/// <param name="Known">The bot's book holds it.</param>
/// <param name="Missing">The component its formula lacks in the pack (<see cref="BuffBotCasting.Missing"/>), or null.</param>
/// <param name="Chance">ACE's chance of the cast landing.</param>
/// <param name="Cost">ACE's mana cost of this cast (<c>CalculateManaUsage</c>, Mana Conversion's rolled saving).</param>
/// <param name="Proportion">A transfer's share of the source taken (the spell table's <c>proportion</c>).</param>
/// <param name="LossPercent">A transfer's loss (<c>loss_Percent</c>; negative: the destination gains more than the source gives).</param>
/// <param name="MinBoost">A boost's least gain (<c>Math.Min(Boost, MaxBoost)</c>, WorldObject_Magic.cs 485).</param>
internal sealed record BuffBotDanceSpell(bool Known, string? Missing, double Chance, uint Cost, float Proportion = 0, float LossPercent = 0, int MinBoost = 0);

/// <summary>The bot as one step of its dance reads it.</summary>
/// <param name="Mana">The mana now.</param>
/// <param name="MaxMana">The mana's maximum.</param>
/// <param name="Stamina">The stamina now.</param>
/// <param name="MaxStamina">The stamina's maximum.</param>
/// <param name="Need">The next cast's mana (ACE's <c>CalculateManaUsage</c> for it).</param>
/// <param name="Reserve">The mana kept for the dance's own cast: Stamina to Mana's base mana, its cost with no saving, so the bot never spends below the price of its own recovery.</param>
/// <param name="Trance">Stamina to Mana Self (null: not in the book's lines).</param>
/// <param name="Revitalize">Revitalize Self (null: likewise).</param>
/// <param name="ManaPerSecond">ACE's regeneration a second (the rate, the enchantments' multiplier, over the heartbeat).</param>
/// <param name="HeartbeatSeconds">The bot's heartbeat.</param>
internal sealed record BuffBotDanceState(uint Mana, uint MaxMana, uint Stamina, uint MaxStamina, uint Need, uint Reserve, BuffBotDanceSpell? Trance, BuffBotDanceSpell? Revitalize, double ManaPerSecond, double HeartbeatSeconds);

/// <summary>What one step of the dance does.</summary>
internal enum BuffBotDanceKind
{
    /// <summary>The mana pays the next cast: go on.</summary>
    Paid,

    /// <summary>Cast Stamina to Mana Self.</summary>
    StaminaToMana,

    /// <summary>Cast Revitalize Self.</summary>
    Revitalize,

    /// <summary>The dance cannot run: rest on ACE's regeneration for <see cref="BuffBotDanceStep.RestSeconds"/>.</summary>
    Rest,
}

/// <summary>One step of the dance and why, for the log; the rest's seconds and the payer's reason in Koji's voice.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Why">The step's reason, with its numbers, for the mod log.</param>
/// <param name="RestSeconds">The seconds to rest (<see cref="BuffBotDanceKind.Rest"/>).</param>
/// <param name="Told">The reason the payer hears with a rest (<see cref="Lifestone.ServerTools.BuffBotWire.Rest(int, string?)"/>), or null.</param>
internal sealed record BuffBotDanceStep(BuffBotDanceKind Kind, string Why, int RestSeconds = 0, string? Told = null);

/// <summary>
/// The owner (2026-11-17): the bot recovers its mana as a caster does, by the stamina-to-mana dance (M5 D88), and keeps itself buffed. Nothing from ACE here, so <c>BuffBotSelfTests</c> runs it; the ACE side
/// (<c>BuffBotBody</c>) reads the bot's book, vitals, skills, enchantments and costs and casts each step by the same rules as a line (<see cref="BuffBotCasting"/>).
/// </summary>
/// <remarks>
/// <para><b>The dance's spells</b> (the world copy's spell table, ACE-World-Database v0.9.296, and the dat): Meditative Trance (2345, Stamina to Mana Self VII): Life Magic, power 300, base mana 70, a Transfer
/// from the caster's stamina to its mana (<c>transfer_Bitfield</c> 5), <c>proportion</c> 0.5, <c>loss_Percent</c> -0.75, no cap. ACE takes <c>round(stamina x 0.5)</c> and gives <c>round(that x 1.75)</c>, scaled
/// down to the mana missing (<c>HandleCastSpell_Transfer</c>, WorldObject_Magic.cs 697-747 @95ef426d): at full stamina (396) 198 stamina buys 346 mana. Robustification (2083, Revitalize Self VII): Life Magic,
/// power 300, base mana 70, a Boost of stamina, 100 to 200 (<c>boost</c> 100, <c>boost_Variance</c> 100; <c>HandleCastSpell_Boost</c>, 474-525).</para>
/// <para><b>One step</b> (<see cref="Next"/>): the mana pays the next cast and keeps <see cref="BuffBotDanceState.Reserve"/> for the dance's own cast: go on. Else, with the stamina under half and Revitalize payable with a
/// Stamina to Mana after it, Revitalize (the owner's rule: stamina near full, mana to near empty); else a Stamina to Mana that nets mana; else a Revitalize when the stamina is spent; else the next cast is paid
/// without the reserve when the mana reaches it; else the dance cannot run and the bot rests, on ACE's regeneration, only as long as the next step needs (its Stamina to Mana when the stamina would pay one).
/// The step is chosen again after each cast, with ACE's costs rolled again, so a fizzle or a small gain is met as it comes.</para>
/// <para><b>Health is not read.</b> Neither spell spends or restores health, and the bot holds no Stamina to Health or Health to Stamina line; the rest is the fallback when the book lacks the spell, the pack lacks a
/// component, the cast chance is under <see cref="MinCastChance"/>, or the stamina is spent with no Revitalize payable.</para>
/// <para><b>Its own buffs</b> (<see cref="Lines"/>, <see cref="Wanted"/>, <see cref="Due"/>): the Self lines that raise what its casting reads, at level VII: Focus and Willpower (each +40: its magic skills read
/// <c>(Focus + Self) / 4</c>, Mana Conversion <c>/ 6</c>, AttributeFormula.cs 55-69; Willpower raises its mana too), the mastery of each school it casts (+40), Mana Mastery (+40 Mana Conversion, the saving of every
/// cost), Mana Renewal (x2.15 mana regeneration) and Rejuvenation (x2.15 stamina regeneration: the dance's source). Each lasts 3600 s; ACE refreshes a line the same caster casts again (StartTime 0,
/// EnchantmentManager.cs 186-195), so the bot renews each <see cref="RenewAheadSeconds"/> ahead of its lapse, the nearest lapse first, one cast at a time.</para>
/// </remarks>
internal static class BuffBotSelf
{
    /// <summary>Meditative Trance: Stamina to Mana Self VII.</summary>
    public const uint StaminaToManaSpell = 2345;

    /// <summary>Its name.</summary>
    public const string StaminaToManaName = "Meditative Trance";

    /// <summary>Robustification: Revitalize Self VII.</summary>
    public const uint RevitalizeSpell = 2083;

    /// <summary>Its name.</summary>
    public const string RevitalizeName = "Robustification";

    /// <summary>The seconds ahead of a self line's lapse at which the bot renews it (the client's own top-up line: a line under 600 s is cast again).</summary>
    public const double RenewAheadSeconds = 600;

    /// <summary>The seconds between two renewals between rounds, so lines cast together lapse apart and a renewal never holds the bot long.</summary>
    public const double SpreadSeconds = 20;

    /// <summary>The seconds before a renewal that could not be cast (no components, no spell) is tried again between rounds.</summary>
    public const double BlockedSeconds = 60;

    /// <summary>The least cast chance a self or dance spell must have for the bot to cast it (the client's <c>--vitals-cast-chance</c> default).</summary>
    public const double MinCastChance = 0.9;

    /// <summary>The dance casts one need may take before the bot falls back to the rest by name (each step nets mana or stamina, so more is a fault).</summary>
    public const int MaxDanceCasts = 8;

    /// <summary>
    /// The bot's self lines at level VII, in the order cast when several are due together (the owner's bootstrap, 2026-11-06: Focus and Willpower, then the masteries, then the rest). The ids and names are the
    /// world copy's spell table's and the dat's (<c>BuffBotSelfDatTests</c> proves each: self-targeted, beneficial, power 300, an hour).
    /// </summary>
    public static IReadOnlyList<BuffBotSelfLine> Lines { get; } =
    [
        new(2067, "Inner Calm", "Focus Self"),
        new(2091, "Mind Blossom", "Willpower Self"),
        new(2215, "Adja's Blessing", "Creature Enchantment Mastery Self", BuffBotSchool.Creature),
        new(2267, "Harlune's Blessing", "Life Magic Mastery Self", BuffBotSchool.Life),
        new(2249, "Celcynd's Blessing", "Item Enchantment Mastery Self", BuffBotSchool.Item),
        new(2287, "Nuhmudira's Blessing", "Mana Conversion Mastery Self"),
        new(2183, "Battlemage's Blessing", "Mana Renewal Self"),
        new(2187, "Unflinching Persistence", "Rejuvenation Self"),
    ];

    /// <summary>The bot's book: its self lines and its dance's two spells (the weenie's spell book; the bot reads it before each cast).</summary>
    public static IReadOnlyList<uint> Book { get; } = [.. Lines.Select(l => l.SpellId), StaminaToManaSpell, RevitalizeSpell];

    /// <summary>The self lines the bot keeps up when it casts in <paramref name="schools"/>: every line but the masteries of schools it never casts.</summary>
    /// <param name="schools">The schools of its lines and its dance.</param>
    public static IReadOnlyList<BuffBotSelfLine> Wanted(IEnumerable<BuffBotSchool> schools)
    {
        var cast = new HashSet<BuffBotSchool>(schools ?? throw new ArgumentNullException(nameof(schools)));
        return [.. Lines.Where(l => l.MasteryOf is not { } school || cast.Contains(school))];
    }

    /// <summary>The self line to renew now, or null: of the lines in the book the bot can cast, the one nearest its lapse with under <paramref name="aheadSeconds"/> left (a line not on it first, in <see cref="Lines"/>' order).</summary>
    /// <param name="status">The wanted lines as the bot stands.</param>
    /// <param name="aheadSeconds">The renewal's lead.</param>
    public static BuffBotSelfStatus? Due(IReadOnlyList<BuffBotSelfStatus> status, double aheadSeconds = RenewAheadSeconds)
    {
        ArgumentNullException.ThrowIfNull(status);
        BuffBotSelfStatus? due = null;
        foreach (var s in status)
        {
            if (s.Known && s.Chance >= MinCastChance && s.SecondsLeft < aheadSeconds && (due is null || s.SecondsLeft < due.Value.SecondsLeft))
            {
                due = s;
            }
        }

        return due;
    }

    /// <summary>A magic skill as ACE reads it: <paramref name="initLevel"/>, the attributes' share <c>round((focus + self) / divisor)</c> (AttributeFormula.cs 55-69), and the mastery's bonus.</summary>
    /// <param name="initLevel">The skill's InitLevel.</param>
    /// <param name="focus">The Focus now.</param>
    /// <param name="self">The Self now.</param>
    /// <param name="divisor">4 for the casting schools, 6 for Mana Conversion (the dat's skill formulae).</param>
    /// <param name="mastery">The mastery's additive bonus.</param>
    public static uint Skill(uint initLevel, uint focus, uint self, uint divisor, uint mastery) =>
        initLevel + (uint)Math.Round((float)(focus + self) / divisor, MidpointRounding.AwayFromZero) + mastery;

    /// <summary>ACE's Stamina to Mana on the caster: the stamina taken and the mana given, scaled to the mana missing (<c>HandleCastSpell_Transfer</c>, WorldObject_Magic.cs 721-747; no cap, no drain).</summary>
    /// <param name="stamina">The stamina now.</param>
    /// <param name="missingMana">The mana missing once the cast's cost is spent.</param>
    /// <param name="proportion">The spell's proportion.</param>
    /// <param name="lossPercent">The spell's loss.</param>
    public static (uint Stamina, uint Mana) Transfer(uint stamina, uint missingMana, float proportion, float lossPercent)
    {
        var src = (uint)Math.Round(stamina * proportion * 1.0f);
        var dest = (uint)Math.Round(src * (1.0f - lossPercent) * 1.0f);
        if (dest > missingMana)
        {
            var scalar = dest == 0 ? 0f : (float)missingMana / dest;
            src = (uint)Math.Round(src * scalar);
            dest = missingMana;
        }

        return (src, dest);
    }

    /// <summary>The next step of the dance (see the class).</summary>
    /// <param name="s">The bot now.</param>
    public static BuffBotDanceStep Next(BuffBotDanceState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var c = CultureInfo.InvariantCulture;
        var t = s.Trance;
        var r = s.Revitalize;
        var tranceOk = t is { Known: true, Missing: null } && t.Chance >= MinCastChance;
        var reviveOk = r is { Known: true, Missing: null } && r.Chance >= MinCastChance;
        if (!tranceOk)
        {
            if (s.Mana >= s.Need)
            {
                return new(BuffBotDanceKind.Paid, string.Create(c, $"the mana {s.Mana} pays {s.Need}; no dance: {Cannot(t)}"));
            }

            var told = t is null || !t.Known ? "Stamina to Mana is not in my book." : t.Missing is { } m ? $"I have no {m} for Stamina to Mana." : "I cannot cast Stamina to Mana surely enough.";
            return Rest(s, s.Need, $"no dance: {Cannot(t)}", told);
        }

        if (s.Mana >= (long)s.Need + s.Reserve)
        {
            return new(BuffBotDanceKind.Paid, string.Create(c, $"the mana {s.Mana} pays {s.Need} and keeps the dance's {s.Reserve}"));
        }

        var payTrance = s.Mana >= t!.Cost;
        var gain = Transfer(s.Stamina, payTrance ? s.MaxMana - (s.Mana - t.Cost) : s.MaxMana - s.Mana, t.Proportion, t.LossPercent);
        var net = (long)gain.Mana - t.Cost;
        var canRevive = reviveOk && s.Stamina < s.MaxStamina && s.Mana >= (long)r!.Cost + t.Cost;
        if (canRevive && s.Stamina * 2 < s.MaxStamina)
        {
            return new(BuffBotDanceKind.Revitalize, string.Create(c, $"stamina {s.Stamina} of {s.MaxStamina} under half: {RevitalizeName} for {r!.Cost} (at least +{r.MinBoost} stamina), then {StaminaToManaName} for {t.Cost}"));
        }

        if (payTrance && net > 0)
        {
            return new(BuffBotDanceKind.StaminaToMana, string.Create(c, $"mana {s.Mana} of {s.MaxMana} short of {s.Need} and the dance's {s.Reserve}: {StaminaToManaName} for {t.Cost} takes {gain.Stamina} of {s.Stamina} stamina for {gain.Mana} mana"));
        }

        if (canRevive)
        {
            return new(BuffBotDanceKind.Revitalize, string.Create(c, $"stamina {s.Stamina} of {s.MaxStamina} nets no mana ({gain.Mana} for {t.Cost}): {RevitalizeName} for {r!.Cost} first"));
        }

        if (s.Mana >= s.Need)
        {
            return new(BuffBotDanceKind.Paid, string.Create(c, $"the mana {s.Mana} pays {s.Need}; the dance cannot run now (stamina {s.Stamina} of {s.MaxStamina}, {StaminaToManaName} {t.Cost}, {RevitalizeName} {(r is null ? "none" : r.Cost.ToString(c))})"));
        }

        // The dance cannot run now: rest only as long as the next step needs.
        var tranceWouldNet = (long)Transfer(s.Stamina, s.MaxMana - Math.Min(s.MaxMana, t.Cost), t.Proportion, t.LossPercent).Mana > t.Cost;
        return tranceWouldNet
            ? Rest(s, Math.Min(s.Need, t.Cost), string.Create(c, $"the mana {s.Mana} pays neither {s.Need} nor {StaminaToManaName}'s {t.Cost}"), "I have too little mana to draw on my stamina.")
            : Rest(s, s.Need, string.Create(c, $"stamina {s.Stamina} of {s.MaxStamina} is spent and {(reviveOk ? $"{RevitalizeName}'s {r!.Cost} with {StaminaToManaName}'s {t.Cost} is not payable" : $"{RevitalizeName} cannot be cast ({Cannot(r)})")}"), "My stamina is spent.");
    }

    private static BuffBotDanceStep Rest(BuffBotDanceState s, uint target, string why, string told)
    {
        var seconds = BuffBotCasting.RestSeconds(target - Math.Min(target, s.Mana), s.ManaPerSecond, s.HeartbeatSeconds);
        return new(BuffBotDanceKind.Rest, string.Create(CultureInfo.InvariantCulture, $"{why}: rest {seconds} s for {target} at {s.ManaPerSecond:0.###} a second"), seconds, told);
    }

    private static string Cannot(BuffBotDanceSpell? spell) =>
        spell is null || !spell.Known ? "not in the book" : spell.Missing is { } m ? "no " + m : string.Create(CultureInfo.InvariantCulture, $"chance {spell.Chance:P1} under {MinCastChance:P0}");
}
