using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The property ids an examine panel reads from an appraisal's tables (ACE <c>PropertyInt</c>, <c>PropertyString</c>, <c>PropertyBool</c>, <c>PropertyFloat</c> and <c>PropertyDataId</c>, ACEmulator/ACE@95ef426d,
/// <c>ACE.Entity/Enum/Properties</c>; the ids are the wire's, shared with the player description's tables). Only ids marked <c>[AssessmentProperty]</c> are sent in an appraisal (<c>AppraiseInfo.cs:352-358</c>).
/// </summary>
public static class AppraisalIds
{
    /// <summary>PropertyInt 1 ItemType (a flag word).</summary>
    public const uint ItemType = 1;

    /// <summary>PropertyFloat 152 ElementalDamageMod (a caster's bonus to elemental damage: 1.03 is +3%; M5 7 F30).</summary>
    public const uint ElementalDamageMod = 152;

    /// <summary>PropertyFloat 157 ResistanceModifier (a quest weapon's fixed resistance cleaving: ACE strikes with 1 plus this as the target's vulnerability to <see cref="ResistanceModifierType"/>; M6 16 E5e3).</summary>
    public const uint ResistanceModifier = 157;

    /// <summary>PropertyInt 263 ResistanceModifierType (the DamageType bits <see cref="ResistanceModifier"/> cleaves).</summary>
    public const uint ResistanceModifierType = 263;

    /// <summary>PropertyInt 5 EncumbranceVal (the burden).</summary>
    public const uint Burden = 5;

    /// <summary>PropertyInt 12 StackSize.</summary>
    public const uint StackSize = 12;

    /// <summary>PropertyInt 11 MaxStackSize.</summary>
    public const uint MaxStackSize = 11;

    /// <summary>PropertyInt 19 Value (the pyreal value).</summary>
    public const uint Value = 19;

    /// <summary>PropertyInt 25 Level (a creature's level).</summary>
    public const uint Level = 25;

    /// <summary>PropertyInt 28 ArmorLevel.</summary>
    public const uint ArmorLevel = 28;

    /// <summary>PropertyInt 36 ResistMagic (an AssessmentProperty, so an appraisal carries it): 9999 or more and ACE never enchants the object (<c>IsEnchantable</c>, WorldObjects/WorldObject_Weapon.cs:532 @95ef426d).</summary>
    public const uint ResistMagic = 36;

    /// <summary>PropertyInt 44 Damage (a weapon's).</summary>
    public const uint Damage = 44;

    /// <summary>PropertyInt 45 DamageType.</summary>
    public const uint DamageType = 45;

    /// <summary>PropertyInt 48 WeaponSkill (the skill the weapon uses).</summary>
    public const uint WeaponSkill = 48;

    /// <summary>PropertyInt 49 WeaponTime (the weapon's speed).</summary>
    public const uint WeaponTime = 49;

    /// <summary>PropertyInt 89 BoosterEnum (the vital a kit or potion boosts).</summary>
    public const uint BoosterVital = 89;

    /// <summary>PropertyInt 90 BoostValue (the healing kit's boost).</summary>
    public const uint HealingBoost = 90;

    /// <summary>PropertyInt 91 MaxStructure (the uses a kit starts with).</summary>
    public const uint MaxStructure = 91;

    /// <summary>PropertyInt 92 Structure (the uses left).</summary>
    public const uint Structure = 92;

    /// <summary>PropertyInt 105 ItemWorkmanship.</summary>
    public const uint Workmanship = 105;

    /// <summary>PropertyInt 106 ItemSpellcraft.</summary>
    public const uint Spellcraft = 106;

    /// <summary>PropertyInt 107 ItemCurMana.</summary>
    public const uint ManaCurrent = 107;

    /// <summary>PropertyInt 108 ItemMaxMana.</summary>
    public const uint ManaMax = 108;

    /// <summary>PropertyInt 109 ItemDifficulty (the arcane lore needed).</summary>
    public const uint ArcaneLore = 109;

    /// <summary>PropertyInt 131 MaterialType.</summary>
    public const uint Material = 131;

    /// <summary>PropertyInt 158 WieldRequirements (ACE <c>WieldRequirement</c>: 1 Skill, 2 RawSkill, 3 Attrib, ... 7 Level, 8 Training).</summary>
    public const uint WieldType = 158;

    /// <summary>PropertyInt 159 WieldSkillType (the skill or attribute, by the type).</summary>
    public const uint WieldSkill = 159;

    /// <summary>PropertyInt 160 WieldDifficulty.</summary>
    public const uint WieldDifficulty = 160;

    /// <summary>PropertyInt 270 WieldRequirements2.</summary>
    public const uint WieldType2 = 270;

    /// <summary>PropertyInt 271 WieldSkillType2.</summary>
    public const uint WieldSkill2 = 271;

    /// <summary>PropertyInt 272 WieldDifficulty2.</summary>
    public const uint WieldDifficulty2 = 272;

    /// <summary>PropertyString 1 Name.</summary>
    public const uint Name = 1;

    /// <summary>PropertyString 14 Use (the use text).</summary>
    public const uint UseText = 14;

    /// <summary>PropertyString 15 ShortDesc.</summary>
    public const uint ShortDescription = 15;

    /// <summary>PropertyString 16 LongDesc (the long description).</summary>
    public const uint LongDescription = 16;
}

/// <summary>One wield requirement of an appraised item (PropertyInt 158 to 160, and the second set 270 to 272).</summary>
/// <param name="Type">ACE's <c>WieldRequirement</c> (1 Skill, 2 RawSkill, 3 Attrib, 4 RawAttrib, 5 SecondaryAttrib, 6 RawSecondaryAttrib, 7 Level, 8 Training, ...).</param>
/// <param name="Skill">The skill, attribute or stat the requirement names (0 for a level).</param>
/// <param name="Difficulty">The value needed.</param>
public readonly record struct WieldRequirement(int Type, int Skill, int Difficulty);

/// <summary>
/// What the server said about one object: GameEvent 0x00C9 IdentifyObjectResponse, kept by guid (M4 3.2 s2; the wire is s0's <see cref="IdentifyResponseMsg"/>, docs/handoff-m4-3-2.md). The tables are by property id with typed
/// readers for the ids an examine panel shows (<see cref="AppraisalIds"/>), a reader is null when the table lacks its id. <see cref="Success"/> false is a result, not an error: ACE still sends the tables in full, and for a creature
/// the creature profile with its health alone (<c>CreatureProfile(creature, false)</c>), withholding the item enchantments, the creature's attributes, other vitals, ratings and armour levels (spec 5.6, <c>AppraiseInfo.cs:334-345, 556-597</c>). An answer with no section at all (<see cref="IsNotFound"/>) is ACE's reply to a
/// guid it cannot find (flags 0, success 0; capture.md C3).
/// </summary>
public sealed class Appraisal
{
    internal Appraisal(IdentifyResponseMsg message, long sequence, double receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        Guid = message.Guid;
        Flags = message.Flags;
        Success = message.Success;
        Sequence = sequence;
        ReceivedAt = receivedAt;
        Ints = message.IntProperties;
        Int64s = message.Int64Properties;
        Bools = message.BoolProperties;
        Floats = message.FloatProperties;
        Strings = message.StringProperties;
        DataIds = message.DataIdProperties;
        SpellBook = message.SpellBook.IsDefault ? [] : message.SpellBook;
        var spells = ImmutableArray.CreateBuilder<uint>();
        var enchantments = ImmutableArray.CreateBuilder<uint>();
        foreach (var entry in SpellBook)
        {
            if (IdentifyResponseMsg.IsItemEnchantment(entry))
            {
                enchantments.Add(IdentifyResponseMsg.SpellIdOf(entry));
            }
            else
            {
                spells.Add(entry);
            }
        }

        Spells = spells.ToImmutable();
        ItemEnchantments = enchantments.ToImmutable();
        Armor = message.Armor;
        Creature = message.Creature;
        Weapon = message.Weapon;
        Hook = message.Hook;
        ArmorEnchantments = message.ArmorEnchantments;
        WeaponEnchantments = message.WeaponEnchantments;
        ResistEnchantments = message.ResistEnchantments;
        ArmorLevels = message.ArmorLevels;
    }

    /// <summary>The appraised object's guid.</summary>
    public uint Guid { get; }

    /// <summary>The sections the server wrote.</summary>
    public IdentifyResponseFlags Flags { get; }

    /// <summary>True when the appraisal succeeded; false (a creature's failed Assess roll) is a result: the tables are here, the profiles are withheld.</summary>
    public bool Success { get; }

    /// <summary>True when the server knew no such object: no section and no success (ACE's reply to a guid it cannot find).</summary>
    public bool IsNotFound => Flags == IdentifyResponseFlags.None && !Success;

    /// <summary>The order this answer arrived in, over the session (1 for the first): a later answer for a guid has a higher number.</summary>
    public long Sequence { get; }

    /// <summary>The session-clock second the answer was applied.</summary>
    public double ReceivedAt { get; }

    /// <summary>PropertyInt values by property id.</summary>
    public IReadOnlyDictionary<uint, int> Ints { get; }

    /// <summary>PropertyInt64 values by property id.</summary>
    public IReadOnlyDictionary<uint, long> Int64s { get; }

    /// <summary>PropertyBool values by property id.</summary>
    public IReadOnlyDictionary<uint, bool> Bools { get; }

    /// <summary>PropertyFloat values by property id.</summary>
    public IReadOnlyDictionary<uint, double> Floats { get; }

    /// <summary>PropertyString values by property id.</summary>
    public IReadOnlyDictionary<uint, string> Strings { get; }

    /// <summary>PropertyDataId values by property id.</summary>
    public IReadOnlyDictionary<uint, uint> DataIds { get; }

    /// <summary>The spell book exactly as written: an entry of an enchantment on the item carries the 0x80000000 bit (<see cref="IdentifyResponseMsg.IsItemEnchantment"/>).</summary>
    public ImmutableArray<uint> SpellBook { get; }

    /// <summary>The spells the item casts or teaches: the spell book's entries without the 0x80000000 bit.</summary>
    public ImmutableArray<uint> Spells { get; }

    /// <summary>The spell ids of the enchantments on the item: the spell book's entries with the 0x80000000 bit, the bit removed. Withheld by a failed roll.</summary>
    public ImmutableArray<uint> ItemEnchantments { get; }

    /// <summary>An armour's eight protections, or null.</summary>
    public AppraisalArmorProfile? Armor { get; }

    /// <summary>A creature's health and, when shown, its attributes, or null (withheld by a failed roll).</summary>
    public AppraisalCreatureProfile? Creature { get; }

    /// <summary>A weapon's profile, or null.</summary>
    public AppraisalWeaponProfile? Weapon { get; }

    /// <summary>A hook's profile, or null.</summary>
    public AppraisalHookProfile? Hook { get; }

    /// <summary>The armour highlight and colour masks, or null.</summary>
    public AppraisalBitfield? ArmorEnchantments { get; }

    /// <summary>The weapon highlight and colour masks, or null.</summary>
    public AppraisalBitfield? WeaponEnchantments { get; }

    /// <summary>The resist highlight and colour masks, or null.</summary>
    public AppraisalBitfield? ResistEnchantments { get; }

    /// <summary>A creature's nine armour levels, or null (withheld by a failed roll).</summary>
    public AppraisalArmorLevels? ArmorLevels { get; }

    /// <summary>True when the answer carries any profile (armour, creature, weapon or hook): false after a failed creature roll.</summary>
    public bool HasProfile => Armor is not null || Creature is not null || Weapon is not null || Hook is not null;

    /// <summary>The name (PropertyString 1), or null.</summary>
    public string? Name => String(AppraisalIds.Name);

    /// <summary>The use text (PropertyString 14), or null.</summary>
    public string? UseText => String(AppraisalIds.UseText);

    /// <summary>The short description (PropertyString 15), or null.</summary>
    public string? ShortDescription => String(AppraisalIds.ShortDescription);

    /// <summary>The long description (PropertyString 16), or null.</summary>
    public string? LongDescription => String(AppraisalIds.LongDescription);

    /// <summary>The pyreal value (PropertyInt 19), or null.</summary>
    public int? Value => Int(AppraisalIds.Value);

    /// <summary>The burden (PropertyInt 5), or null.</summary>
    public int? Burden => Int(AppraisalIds.Burden);

    /// <summary>The item type flags (PropertyInt 1), or null.</summary>
    public uint? ItemType => Int(AppraisalIds.ItemType) is { } v ? unchecked((uint)v) : null;

    /// <summary>The workmanship (PropertyInt 105), or null.</summary>
    public int? Workmanship => Int(AppraisalIds.Workmanship);

    /// <summary>The material type (PropertyInt 131), or null.</summary>
    public int? Material => Int(AppraisalIds.Material);

    /// <summary>The stack size (PropertyInt 12), or null.</summary>
    public int? StackSize => Int(AppraisalIds.StackSize);

    /// <summary>The uses left (Structure, PropertyInt 92), or null.</summary>
    public int? UsesLeft => Int(AppraisalIds.Structure);

    /// <summary>The uses it started with (MaxStructure, PropertyInt 91), or null.</summary>
    public int? MaxUses => Int(AppraisalIds.MaxStructure);

    /// <summary>An item's mana now (ItemCurMana, PropertyInt 107), or null: a magic item, or a charged mana stone (an empty stone has none). ACE sends no update while it burns, so an appraisal is how the client reads it.</summary>
    public int? ItemMana => Int(AppraisalIds.ManaCurrent);

    /// <summary>An item's mana capacity (ItemMaxMana, PropertyInt 108), or null.</summary>
    public int? ItemMaxMana => Int(AppraisalIds.ManaMax);

    /// <summary>A healing kit's boost (BoostValue, PropertyInt 90), or null.</summary>
    public int? HealingBoost => Int(AppraisalIds.HealingBoost);

    /// <summary>The vital a kit or potion boosts (BoosterEnum, PropertyInt 89), or null.</summary>
    public int? BoostedVital => Int(AppraisalIds.BoosterVital);

    /// <summary>The armour level (PropertyInt 28), or null.</summary>
    public int? ArmorLevel => Int(AppraisalIds.ArmorLevel);

    /// <summary>The weapon's damage: the weapon profile's, else PropertyInt 44; null for neither.</summary>
    public int? WeaponDamage => Weapon is { } w ? unchecked((int)w.Damage) : Int(AppraisalIds.Damage);

    /// <summary>The weapon profile's damage variance (the share below the greatest damage the weapon can roll: the lowest is <c>Damage x (1 - variance)</c>), or null without a weapon profile.</summary>
    public double? WeaponDamageVariance => Weapon?.DamageVariance;

    /// <summary>The weapon's speed (its time): the weapon profile's, else PropertyInt 49; null for neither.</summary>
    public int? WeaponSpeed => Weapon is { } w ? unchecked((int)w.WeaponTime) : Int(AppraisalIds.WeaponTime);

    /// <summary>The weapon's skill: the weapon profile's, else PropertyInt 48; null for neither.</summary>
    public int? WeaponSkill => Weapon is { } w ? unchecked((int)w.WeaponSkill) : Int(AppraisalIds.WeaponSkill);

    /// <summary>The weapon's damage type: the weapon profile's, else PropertyInt 45; null for neither.</summary>
    public int? WeaponDamageType => Weapon is { } w ? unchecked((int)w.DamageType) : Int(AppraisalIds.DamageType);

    /// <summary>A creature's level (PropertyInt 25), or null.</summary>
    public int? Level => Int(AppraisalIds.Level);

    /// <summary>A creature's current health from its profile, or null (no profile: an item, or a failed roll).</summary>
    public uint? CreatureHealth => Creature?.Health;

    /// <summary>The creature profile's flags (0x8 ShowAttributes: a successful roll; 0 for ACE's failed one), or null without a profile (M5 7 D137).</summary>
    public uint? CreatureProfileFlags => Creature?.Flags;

    /// <summary>True when the creature profile carries the attributes and the other vitals (a successful roll's ShowAttributes).</summary>
    public bool CreatureShowsAttributes => Creature?.Strength is not null;

    /// <summary>The hook profile's flags (0x1 Inscribable, 0x2 IsHealer, 0x4 IsFood, 0x8 IsLockpick; ACE <c>HookFlags</c>), or null without a hook profile (M5 7 D137).</summary>
    public uint? HookFlags => Hook?.Flags;

    /// <summary>A creature's maximum health from its profile, or null.</summary>
    public uint? CreatureHealthMax => Creature?.HealthMax;

    /// <summary>A creature's current stamina, when the profile shows its attributes, else null.</summary>
    public uint? CreatureStamina => Creature?.Stamina;

    /// <summary>A creature's maximum stamina, when the profile shows its attributes, else null.</summary>
    public uint? CreatureStaminaMax => Creature?.StaminaMax;

    /// <summary>A creature's current mana, when the profile shows its attributes, else null.</summary>
    public uint? CreatureMana => Creature?.Mana;

    /// <summary>A creature's maximum mana, when the profile shows its attributes, else null.</summary>
    public uint? CreatureManaMax => Creature?.ManaMax;

    /// <summary>The wield requirements the item names (PropertyInt 158 to 160, then 270 to 272), in that order; empty when it has none.</summary>
    public IReadOnlyList<WieldRequirement> WieldRequirements
    {
        get
        {
            var list = new List<WieldRequirement>(2);
            if (Int(AppraisalIds.WieldType) is { } first and not 0)
            {
                list.Add(new WieldRequirement(first, Int(AppraisalIds.WieldSkill) ?? 0, Int(AppraisalIds.WieldDifficulty) ?? 0));
            }

            if (Int(AppraisalIds.WieldType2) is { } second and not 0)
            {
                list.Add(new WieldRequirement(second, Int(AppraisalIds.WieldSkill2) ?? 0, Int(AppraisalIds.WieldDifficulty2) ?? 0));
            }

            return list;
        }
    }

    /// <summary>A PropertyInt by id, or null when the table lacks it.</summary>
    public int? Int(uint id) => Ints.TryGetValue(id, out var value) ? value : null;

    /// <summary>A PropertyString by id, or null when the table lacks it.</summary>
    public string? String(uint id) => Strings.TryGetValue(id, out var value) ? value : null;

    /// <summary>A PropertyFloat by id, or null when the table lacks it.</summary>
    public double? Float(uint id) => Floats.TryGetValue(id, out var value) ? value : null;

    /// <summary>A PropertyBool by id, or null when the table lacks it.</summary>
    public bool? Bool(uint id) => Bools.TryGetValue(id, out var value) ? value : null;

    /// <summary>A PropertyInt64 by id, or null when the table lacks it.</summary>
    public long? Int64(uint id) => Int64s.TryGetValue(id, out var value) ? value : null;

    /// <summary>A PropertyDataId by id, or null when the table lacks it.</summary>
    public uint? DataId(uint id) => DataIds.TryGetValue(id, out var value) ? value : null;

    /// <inheritdoc/>
    public override string ToString() =>
        "Appraisal " + Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + (IsNotFound ? " not found" : Success ? " success" : " failed") + " flags " + ((uint)Flags).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The latest appraisal per guid (<see cref="GameState.Appraisals"/>), bounded: at most <see cref="Capacity"/> guids are kept and the one answered longest ago is dropped for a new guid (an answer for a kept guid replaces
/// it and is the newest). 256 holds every object a hunt or an examine panel looks at in a session many times over (a few KB each, about a megabyte at the bound) and keeps a long-running host from growing. The counters
/// count every answer since the session began, whatever was dropped. Game thread only; the reducer writes it.
/// <para>M6 14 (live ladder42, 2026-11-16 19:51:19 UTC): an appraisal of a piece she wears is never the one dropped (<see cref="Keep"/>): the loot step appraised 257 other guids in the 41 minutes after the Chevron Cloak's
/// (worn, ResistMagic 9999), the 257th dropped it, the armour lines counted the cloak again, read it bare, and logged Impenetrability and Bludgeon Bane EXPIRED 207 s before they ran out.</para>
/// </summary>
public sealed class AppraisalCache
{
    /// <summary>The most guids kept.</summary>
    public const int Capacity = 256;

    /// <summary>M6 14: true for a guid whose appraisal is kept past the bound (a piece she wears: <see cref="GameState"/> sets it); null keeps none. The oldest answer of any other guid is dropped instead.</summary>
    public Func<uint, bool>? Keep { get; set; }

    private readonly Dictionary<uint, LinkedListNode<Appraisal>> _byGuid = [];
    private readonly LinkedList<Appraisal> _order = new(); // oldest answer first

    /// <summary>The guids kept now.</summary>
    public int Count => _byGuid.Count;

    /// <summary>The <see cref="Appraisal.Sequence"/> of the latest answer (0 before any).</summary>
    public long Sequence { get; private set; }

    /// <summary>Every 00C9 answer received.</summary>
    public int Received { get; private set; }

    /// <summary>Answers that succeeded (<see cref="Appraisal.Success"/>): objects appraised.</summary>
    public int Appraised { get; private set; }

    /// <summary>Answers with success false and tables (a creature's failed roll, or ACE's cooldown answer).</summary>
    public int Failed { get; private set; }

    /// <summary>Answers for a guid the server could not find (<see cref="Appraisal.IsNotFound"/>).</summary>
    public int NotFound { get; private set; }

    /// <summary>The latest appraisal of <paramref name="guid"/>.</summary>
    /// <param name="guid">The object's guid.</param>
    /// <param name="appraisal">The appraisal, or null.</param>
    public bool TryGet(uint guid, [NotNullWhen(true)] out Appraisal? appraisal)
    {
        if (_byGuid.TryGetValue(guid, out var node))
        {
            appraisal = node.Value;
            return true;
        }

        appraisal = null;
        return false;
    }

    internal Appraisal Add(IdentifyResponseMsg message, double now)
    {
        var appraisal = new Appraisal(message, ++Sequence, now);
        Received++;
        if (appraisal.IsNotFound)
        {
            NotFound++;
        }
        else if (appraisal.Success)
        {
            Appraised++;
        }
        else
        {
            Failed++;
        }

        if (_byGuid.Remove(appraisal.Guid, out var old))
        {
            _order.Remove(old);
        }
        else if (_byGuid.Count >= Capacity)
        {
            var oldest = _order.First;
            while (oldest is not null && Keep is { } keep && keep(oldest.Value.Guid))
            {
                oldest = oldest.Next; // a worn piece's appraisal stays
            }

            oldest ??= _order.First!; // every kept guid is worn (more than the bound): the oldest goes after all
            _order.Remove(oldest);
            _byGuid.Remove(oldest.Value.Guid);
        }

        _byGuid[appraisal.Guid] = _order.AddLast(appraisal);
        return appraisal;
    }
}

/// <summary>An appraisal answer was applied (GameEvent 0x00C9; M4 3.2 s2). The model holds it: <see cref="IGameSessionView.TryGetAppraisal"/>.</summary>
/// <param name="Guid">The appraised object.</param>
/// <param name="Success">True when the appraisal succeeded; false is a failed roll or an unknown object (<see cref="Flags"/> none).</param>
/// <param name="Flags">The sections the answer carried.</param>
/// <param name="Sequence">The answer's <see cref="Appraisal.Sequence"/>.</param>
public readonly record struct AppraisalReceived(uint Guid, bool Success, IdentifyResponseFlags Flags, long Sequence) : IGameEvent;

/// <summary>What an Identify came to (<see cref="IdentifyResult.Kind"/>).</summary>
public enum IdentifyResultKind
{
    /// <summary>The server appraised the object (success 1).</summary>
    Appraised,

    /// <summary>The roll failed (success 0 with tables): a creature's Assess against its Deception, or ACE's cooldown answer; the profiles are withheld.</summary>
    Failed,

    /// <summary>The server knows no such object (flags 0, success 0).</summary>
    NotFound,
}

/// <summary>What <see cref="IActionApi.Identify"/> found.</summary>
/// <param name="Kind">How it came out.</param>
/// <param name="Appraisal">The answer (also in <see cref="GameState.Appraisals"/>).</param>
/// <param name="Cooldown">True for a <see cref="IdentifyResultKind.Failed"/> answer to a repeat within 5 s of an earlier failed one on the same guid: ACE answers failed with no roll (<c>Player.cs:274-299</c>).</param>
public sealed record IdentifyResult(IdentifyResultKind Kind, Appraisal Appraisal, bool Cooldown = false);

/// <summary>
/// The handle of <see cref="IActionApi.Identify"/>: an <see cref="ActionHandle"/> plus what the answer said. <see cref="Result"/> is set when the action was confirmed (an answer arrived; a failed roll and an unknown
/// object are answers too), before <see cref="ActionHandle.Completion"/> completes; it stays null for every other outcome.
/// </summary>
public sealed class IdentifyHandle : ActionHandle
{
    internal IdentifyHandle(long id, ActionKind kind, ActionSource source, string summary, double queuedAt)
        : base(id, kind, source, summary, queuedAt)
    {
    }

    /// <summary>What the answer said, or null (not confirmed).</summary>
    public IdentifyResult? Result { get; private set; }

    internal bool TryComplete(IdentifyResult? result, ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError)
    {
        if (IsCompleted)
        {
            return false;
        }

        Result = result;
        return TryComplete(outcome, time, onHandlerError);
    }
}
