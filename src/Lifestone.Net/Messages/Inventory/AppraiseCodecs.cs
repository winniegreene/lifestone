// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/AppraiseInfoParser.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal; TryParse
// over WireReader on the whole F7B0 message (OpenAC takes the payload only, accepts a longer one and catches FormatException)
// that reads every byte, never throws and reports the section and the offset where it stopped; the property tables are kept as
// raw dictionaries by property id in Lifestone records (OpenAC's PropertyBundle and AcDream.Core.Items are not carried over);
// the strings are read as the CP1252 String16L of WireReader (OpenAC's reader skipped the pad but decoded its own way); the
// armour levels stay unsigned as ACE writes them (OpenAC cast them to int); a flag bit ACE does not define is a refusal
// (TODO(m4-appraise-section-unknown-flag)) where OpenAC ignored it; the table and spell counts are checked against the bytes left
// with WireReader (OpenAC capped the spell book at 4096); the creature profile keeps its flag word and its optional parts as
// nullable fields; the three bitfield pairs and every profile are Lifestone records; XML docs with the ACE and retail cites added.

using System.Collections.Immutable;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>The sections of an IdentifyObjectResponse (ACE <c>IdentifyResponseFlags</c>, <c>ACE.Entity/Enum/IdentifyResponseFlags.cs:8-24</c>).</summary>
[Flags]
public enum IdentifyResponseFlags : uint
{
    /// <summary>No section.</summary>
    None = 0x0000,

    /// <summary>The PropertyInt table.</summary>
    IntStatsTable = 0x0001,

    /// <summary>The PropertyBool table.</summary>
    BoolStatsTable = 0x0002,

    /// <summary>The PropertyFloat table (f64 values).</summary>
    FloatStatsTable = 0x0004,

    /// <summary>The PropertyString table.</summary>
    StringStatsTable = 0x0008,

    /// <summary>The spell book (a u32 count and the ids; item enchantments or'ed 0x80000000).</summary>
    SpellBook = 0x0010,

    /// <summary>The weapon profile.</summary>
    WeaponProfile = 0x0020,

    /// <summary>The hook profile.</summary>
    HookProfile = 0x0040,

    /// <summary>The armour profile (eight f32 protections).</summary>
    ArmorProfile = 0x0080,

    /// <summary>The creature profile.</summary>
    CreatureProfile = 0x0100,

    /// <summary>The armour highlight and colour masks.</summary>
    ArmorEnchantmentBitfield = 0x0200,

    /// <summary>The resist highlight and colour masks.</summary>
    ResistEnchantmentBitfield = 0x0400,

    /// <summary>The weapon highlight and colour masks.</summary>
    WeaponEnchantmentBitfield = 0x0800,

    /// <summary>The PropertyDataId table.</summary>
    DidStatsTable = 0x1000,

    /// <summary>The PropertyInt64 table.</summary>
    Int64StatsTable = 0x2000,

    /// <summary>The nine armour levels (a creature's per-slot armour).</summary>
    ArmorLevels = 0x4000,
}

/// <summary>An appraised armour's eight protections (ACE <c>ArmorProfile</c>, written in this order by <c>ArmorProfileExtensions.Write</c>).</summary>
public readonly record struct AppraisalArmorProfile(
    float Slashing,
    float Piercing,
    float Bludgeoning,
    float Cold,
    float Fire,
    float Acid,
    float Nether,
    float Lightning);

/// <summary>
/// An appraised creature (ACE <c>CreatureProfile</c>, <c>CreatureProfileExtensions.Write</c>): the flag word, the current and
/// maximum health, then the attributes when flag 0x8 (ShowAttributes) is set and the buff masks when flag 0x1
/// (HasBuffsDebuffs) is set. ACE sets only those two flags; flags 0x2 and 0x4 carry no data in its writer and are kept in
/// <see cref="Flags"/>. Retail reads it with <c>CreatureAppraisalProfile::UnPack</c> (11.4186 0x005B6190, 11.6096 0x005B7240).
/// </summary>
public sealed record AppraisalCreatureProfile(
    uint Flags,
    uint Health,
    uint HealthMax,
    uint? Strength,
    uint? Endurance,
    uint? Quickness,
    uint? Coordination,
    uint? Focus,
    uint? Self,
    uint? Stamina,
    uint? Mana,
    uint? StaminaMax,
    uint? ManaMax,
    ushort? AttributeHighlights,
    ushort? AttributeColors)
{
    /// <summary>The flag that says the attributes are present.</summary>
    public const uint ShowAttributes = 0x8;

    /// <summary>The flag that says the highlight and colour masks are present.</summary>
    public const uint HasBuffsDebuffs = 0x1;
}

/// <summary>An appraised weapon (ACE <c>WeaponProfile</c>, <c>WeaponProfileExtensions.Write</c>); retail <c>WeaponProfile::UnPack</c> (11.4186 0x005B63C0, 11.6096 0x005B7470).</summary>
public sealed record AppraisalWeaponProfile(
    uint DamageType,
    uint WeaponTime,
    uint WeaponSkill,
    uint Damage,
    double DamageVariance,
    double DamageMod,
    double WeaponLength,
    double MaxVelocity,
    double WeaponOffense,
    uint MaxVelocityEstimated);

/// <summary>An appraised hook (ACE <c>HookProfile</c>, <c>HookProfileExtensions.Write</c>); retail <c>HookAppraisalProfile::UnPack</c> (11.6096 0x005B75E0).</summary>
public sealed record AppraisalHookProfile(uint Flags, uint ValidLocations, uint AmmoType);

/// <summary>A highlight mask and a colour mask (two u16 words).</summary>
public readonly record struct AppraisalBitfield(ushort Highlight, ushort Color);

/// <summary>A creature's nine armour levels (ACE <c>ArmorLevel</c>, <c>ArmorLevelExtensions.Write</c>, <c>ArmorLevel.cs:83-98</c>).</summary>
public readonly record struct AppraisalArmorLevels(
    uint Head,
    uint Chest,
    uint Abdomen,
    uint UpperArm,
    uint LowerArm,
    uint Hand,
    uint UpperLeg,
    uint LowerLeg,
    uint Foot);

/// <summary>
/// GameEvent 0x00C9 IdentifyObjectResponse (ACE <c>GameEventIdentifyObjectResponse</c>): the answer to an Identify. The tables are
/// raw by property id (the read model is built from them elsewhere); a section the flags did not announce is empty or null.
/// </summary>
/// <param name="Guid">The appraised object's guid.</param>
/// <param name="Flags">The sections the server wrote.</param>
/// <param name="Success">Whether the appraisal succeeded; a failed one (0) still carries tables, and it is a result, not an error.</param>
/// <param name="IntProperties">PropertyInt values by property id.</param>
/// <param name="Int64Properties">PropertyInt64 values by property id.</param>
/// <param name="BoolProperties">PropertyBool values by property id.</param>
/// <param name="FloatProperties">PropertyFloat values by property id.</param>
/// <param name="StringProperties">PropertyString values by property id.</param>
/// <param name="DataIdProperties">PropertyDataId values by property id.</param>
/// <param name="SpellBook">The spell ids exactly as written: an item enchantment keeps its 0x80000000 bit (<see cref="IdentifyResponseMsg.IsItemEnchantment"/>).</param>
/// <param name="Armor">The armour profile, or null.</param>
/// <param name="Creature">The creature profile, or null.</param>
/// <param name="Weapon">The weapon profile, or null.</param>
/// <param name="Hook">The hook profile, or null.</param>
/// <param name="ArmorEnchantments">The armour highlight and colour masks, or null.</param>
/// <param name="WeaponEnchantments">The weapon highlight and colour masks, or null.</param>
/// <param name="ResistEnchantments">The resist highlight and colour masks, or null.</param>
/// <param name="ArmorLevels">The nine armour levels, or null.</param>
public sealed record IdentifyResponseMsg(
    uint Guid,
    IdentifyResponseFlags Flags,
    bool Success,
    ImmutableDictionary<uint, int> IntProperties,
    ImmutableDictionary<uint, long> Int64Properties,
    ImmutableDictionary<uint, bool> BoolProperties,
    ImmutableDictionary<uint, double> FloatProperties,
    ImmutableDictionary<uint, string> StringProperties,
    ImmutableDictionary<uint, uint> DataIdProperties,
    ImmutableArray<uint> SpellBook,
    AppraisalArmorProfile? Armor,
    AppraisalCreatureProfile? Creature,
    AppraisalWeaponProfile? Weapon,
    AppraisalHookProfile? Hook,
    AppraisalBitfield? ArmorEnchantments,
    AppraisalBitfield? WeaponEnchantments,
    AppraisalBitfield? ResistEnchantments,
    AppraisalArmorLevels? ArmorLevels)
{
    /// <summary>The bit ACE or's onto a spell id that comes from an enchantment on the item (<c>AppraiseInfo.cs:21</c>).</summary>
    public const uint ItemEnchantmentBit = 0x80000000;

    /// <summary>True when the spell book entry is an enchantment on the item rather than a spell the item casts.</summary>
    public static bool IsItemEnchantment(uint spellBookEntry) => (spellBookEntry & ItemEnchantmentBit) != 0;

    /// <summary>The spell id of a spell book entry, without the enchantment bit.</summary>
    public static uint SpellIdOf(uint spellBookEntry) => spellBookEntry & ~ItemEnchantmentBit;
}

/// <summary>
/// The appraisal game event (M4 3.2 s0), laid out as ACE writes it (<c>GameEventIdentifyObjectResponse.cs:9-16</c>,
/// <c>AppraiseInfoExtensions.Write</c>, <c>AppraiseInfo.cs:743-786</c>, ACE 95ef426d): u32 guid, u32 flags, u32 success, then by
/// flag bit in this fixed order (not bit order) the Int table (0x1), Int64 (0x2000), Bool (0x2), Float (0x4), String (0x8) and
/// DataId (0x1000) tables, each a u16 count, a u16 bucket count and count x {u32 key, value} (values i32, i64, u32 0/1, f64,
/// String16L, u32); the SpellBook (0x10: u32 count, count x u32); the Armor (0x80: 8 x f32), Creature (0x100), Weapon (0x20) and
/// Hook (0x40) profiles; the armour (0x200), weapon (0x800) and resist (0x400) bitfields, each a u16 highlight and a u16 colour;
/// and the nine ArmorLevels (0x4000, u32 each). A flag with an unknown bit, a cut-off section or a byte left over refuses the
/// whole message with the section and the offset named: there is never a partial record. Retail reads the whole thing with
/// <c>AppraisalProfile::UnPack</c> (11.4186 0x005B3980, 11.6096 0x005B4A30), whose body was not read; its consumer is
/// <c>gmExaminationUI::RecvNotice_SetAppraiseInfo</c> (11.6096 0x004B6E20); the 0x00C9 handler that feeds it is unknown.
/// </summary>
internal static class AppraiseCodecs
{
    /// <summary>The union of the flag bits ACE defines.</summary>
    private const uint KnownFlags = 0x7FFF;

    /// <summary>The sections in ACE's write order, each with its flag bit and its name for a refusal.</summary>
    private enum Section
    {
        Int,
        Int64,
        Bool,
        Float,
        String,
        DataId,
        SpellBook,
        ArmorProfile,
        CreatureProfile,
        WeaponProfile,
        HookProfile,
        ArmorBitfield,
        WeaponBitfield,
        ResistBitfield,
        ArmorLevels,
    }

    /// <summary>0x00C9 IdentifyObjectResponse. On false <paramref name="reason"/> names the section, its flag and the offset.</summary>
    public static bool TryParseIdentifyResponse(ReadOnlySpan<byte> message, out IdentifyResponseMsg response, out string reason)
    {
        response = null!;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            reason = "shorter than the GameEvent envelope";
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var guid) || !reader.TryReadU32(out var rawFlags) || !reader.TryReadU32(out var success))
        {
            reason = $"the guid, flags and success words are cut short at offset {reader.Position}";
            return false;
        }

        if ((rawFlags & ~KnownFlags) != 0)
        {
            reason = $"flags 0x{rawFlags:X8} carry bits ACE does not define (0x{rawFlags & ~KnownFlags:X}); TODO(m4-appraise-section-unknown-flag): their sections cannot be skipped, so nothing is decoded";
            return false;
        }

        var flags = (IdentifyResponseFlags)rawFlags;
        var ints = ImmutableDictionary<uint, int>.Empty;
        var int64s = ImmutableDictionary<uint, long>.Empty;
        var bools = ImmutableDictionary<uint, bool>.Empty;
        var floats = ImmutableDictionary<uint, double>.Empty;
        var strings = ImmutableDictionary<uint, string>.Empty;
        var dids = ImmutableDictionary<uint, uint>.Empty;
        var spells = ImmutableArray<uint>.Empty;
        AppraisalArmorProfile? armor = null;
        AppraisalCreatureProfile? creature = null;
        AppraisalWeaponProfile? weapon = null;
        AppraisalHookProfile? hook = null;
        AppraisalBitfield? armorBits = null;
        AppraisalBitfield? weaponBits = null;
        AppraisalBitfield? resistBits = null;
        AppraisalArmorLevels? levels = null;

        var failed = default(Section?);
        if (flags.HasFlag(IdentifyResponseFlags.IntStatsTable) && !TryReadInts(ref reader, out ints))
        {
            failed = Section.Int;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.Int64StatsTable) && !TryReadInt64s(ref reader, out int64s))
        {
            failed = Section.Int64;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.BoolStatsTable) && !TryReadBools(ref reader, out bools))
        {
            failed = Section.Bool;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.FloatStatsTable) && !TryReadFloats(ref reader, out floats))
        {
            failed = Section.Float;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.StringStatsTable) && !TryReadStrings(ref reader, out strings))
        {
            failed = Section.String;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.DidStatsTable) && !TryReadDataIds(ref reader, out dids))
        {
            failed = Section.DataId;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.SpellBook) && !TryReadSpellBook(ref reader, out spells))
        {
            failed = Section.SpellBook;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.ArmorProfile) && !TryReadArmor(ref reader, out armor))
        {
            failed = Section.ArmorProfile;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.CreatureProfile) && !TryReadCreature(ref reader, out creature))
        {
            failed = Section.CreatureProfile;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.WeaponProfile) && !TryReadWeapon(ref reader, out weapon))
        {
            failed = Section.WeaponProfile;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.HookProfile) && !TryReadHook(ref reader, out hook))
        {
            failed = Section.HookProfile;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.ArmorEnchantmentBitfield) && !TryReadBitfield(ref reader, out armorBits))
        {
            failed = Section.ArmorBitfield;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.WeaponEnchantmentBitfield) && !TryReadBitfield(ref reader, out weaponBits))
        {
            failed = Section.WeaponBitfield;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.ResistEnchantmentBitfield) && !TryReadBitfield(ref reader, out resistBits))
        {
            failed = Section.ResistBitfield;
        }
        else if (flags.HasFlag(IdentifyResponseFlags.ArmorLevels) && !TryReadArmorLevels(ref reader, out levels))
        {
            failed = Section.ArmorLevels;
        }

        if (failed is { } section)
        {
            reason = $"the {section} section (flags 0x{rawFlags:X}) is cut short or malformed; the reader stopped at offset {reader.Position} of {message.Length}";
            return false;
        }

        if (!reader.IsAtEnd)
        {
            reason = $"{reader.Remaining} unread bytes after the last announced section (flags 0x{rawFlags:X}, offset {reader.Position} of {message.Length})";
            return false;
        }

        response = new IdentifyResponseMsg(
            guid, flags, success != 0, ints, int64s, bools, floats, strings, dids, spells, armor, creature, weapon, hook, armorBits, weaponBits, resistBits, levels);
        reason = string.Empty;
        return true;
    }

    // ---- The property tables (ACE PackableHashTable.WriteHeader: u16 count, u16 bucket count; then count x {u32 key, value}) ----

    private static bool TryReadTableHeader(ref WireReader reader, int minimumEntrySize, out int count)
    {
        // The count is a u16 and the bucket word follows it; the count must fit the bytes left before anything is built.
        return reader.TryReadCount16(minimumEntrySize, out count) && reader.TryReadU16(out _) && reader.Remaining >= count * minimumEntrySize;
    }

    private static bool TryReadInts(ref WireReader reader, out ImmutableDictionary<uint, int> table)
    {
        table = ImmutableDictionary<uint, int>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, int>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadI32(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadInt64s(ref WireReader reader, out ImmutableDictionary<uint, long> table)
    {
        table = ImmutableDictionary<uint, long>.Empty;
        if (!TryReadTableHeader(ref reader, 12, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, long>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadI64(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadBools(ref WireReader reader, out ImmutableDictionary<uint, bool> table)
    {
        table = ImmutableDictionary<uint, bool>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, bool>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadU32(out var value))
            {
                return false;
            }

            builder[key] = value != 0;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadFloats(ref WireReader reader, out ImmutableDictionary<uint, double> table)
    {
        table = ImmutableDictionary<uint, double>.Empty;
        if (!TryReadTableHeader(ref reader, 12, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, double>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadF64(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadStrings(ref WireReader reader, out ImmutableDictionary<uint, string> table)
    {
        table = ImmutableDictionary<uint, string>.Empty;

        // A key and the smallest String16L (a u16 length and its two pad bytes).
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, string>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadString16L(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    private static bool TryReadDataIds(ref WireReader reader, out ImmutableDictionary<uint, uint> table)
    {
        table = ImmutableDictionary<uint, uint>.Empty;
        if (!TryReadTableHeader(ref reader, 8, out var count))
        {
            return false;
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, uint>();
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var key) || !reader.TryReadU32(out var value))
            {
                return false;
            }

            builder[key] = value;
        }

        table = builder.ToImmutable();
        return true;
    }

    // ---- The spell book and the profiles ----

    private static bool TryReadSpellBook(ref WireReader reader, out ImmutableArray<uint> spells)
    {
        spells = ImmutableArray<uint>.Empty;
        if (!reader.TryReadCount(sizeof(uint), out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<uint>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var id))
            {
                return false;
            }

            builder.Add(id);
        }

        spells = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadArmor(ref WireReader reader, out AppraisalArmorProfile? profile)
    {
        profile = null;
        if (!reader.TryReadF32(out var slashing) || !reader.TryReadF32(out var piercing) || !reader.TryReadF32(out var bludgeoning)
            || !reader.TryReadF32(out var cold) || !reader.TryReadF32(out var fire) || !reader.TryReadF32(out var acid)
            || !reader.TryReadF32(out var nether) || !reader.TryReadF32(out var lightning))
        {
            return false;
        }

        profile = new AppraisalArmorProfile(slashing, piercing, bludgeoning, cold, fire, acid, nether, lightning);
        return true;
    }

    private static bool TryReadCreature(ref WireReader reader, out AppraisalCreatureProfile? profile)
    {
        profile = null;
        if (!reader.TryReadU32(out var flags) || !reader.TryReadU32(out var health) || !reader.TryReadU32(out var healthMax))
        {
            return false;
        }

        uint? strength = null, endurance = null, quickness = null, coordination = null, focus = null, self = null;
        uint? stamina = null, mana = null, staminaMax = null, manaMax = null;
        if ((flags & AppraisalCreatureProfile.ShowAttributes) != 0)
        {
            if (!reader.TryReadU32(out var str) || !reader.TryReadU32(out var end) || !reader.TryReadU32(out var quick)
                || !reader.TryReadU32(out var coord) || !reader.TryReadU32(out var foc) || !reader.TryReadU32(out var slf)
                || !reader.TryReadU32(out var sta) || !reader.TryReadU32(out var man) || !reader.TryReadU32(out var staMax)
                || !reader.TryReadU32(out var manMax))
            {
                return false;
            }

            (strength, endurance, quickness, coordination, focus, self) = (str, end, quick, coord, foc, slf);
            (stamina, mana, staminaMax, manaMax) = (sta, man, staMax, manMax);
        }

        ushort? highlights = null, colors = null;
        if ((flags & AppraisalCreatureProfile.HasBuffsDebuffs) != 0)
        {
            if (!reader.TryReadU16(out var high) || !reader.TryReadU16(out var color))
            {
                return false;
            }

            (highlights, colors) = (high, color);
        }

        profile = new AppraisalCreatureProfile(
            flags, health, healthMax, strength, endurance, quickness, coordination, focus, self, stamina, mana, staminaMax, manaMax, highlights, colors);
        return true;
    }

    private static bool TryReadWeapon(ref WireReader reader, out AppraisalWeaponProfile? profile)
    {
        profile = null;
        if (!reader.TryReadU32(out var damageType) || !reader.TryReadU32(out var weaponTime) || !reader.TryReadU32(out var weaponSkill)
            || !reader.TryReadU32(out var damage) || !reader.TryReadF64(out var variance) || !reader.TryReadF64(out var damageMod)
            || !reader.TryReadF64(out var length) || !reader.TryReadF64(out var maxVelocity) || !reader.TryReadF64(out var offense)
            || !reader.TryReadU32(out var estimated))
        {
            return false;
        }

        profile = new AppraisalWeaponProfile(damageType, weaponTime, weaponSkill, damage, variance, damageMod, length, maxVelocity, offense, estimated);
        return true;
    }

    private static bool TryReadHook(ref WireReader reader, out AppraisalHookProfile? profile)
    {
        profile = null;
        if (!reader.TryReadU32(out var flags) || !reader.TryReadU32(out var locations) || !reader.TryReadU32(out var ammo))
        {
            return false;
        }

        profile = new AppraisalHookProfile(flags, locations, ammo);
        return true;
    }

    private static bool TryReadBitfield(ref WireReader reader, out AppraisalBitfield? bitfield)
    {
        bitfield = null;
        if (!reader.TryReadU16(out var highlight) || !reader.TryReadU16(out var color))
        {
            return false;
        }

        bitfield = new AppraisalBitfield(highlight, color);
        return true;
    }

    private static bool TryReadArmorLevels(ref WireReader reader, out AppraisalArmorLevels? levels)
    {
        levels = null;
        if (!reader.TryReadU32(out var head) || !reader.TryReadU32(out var chest) || !reader.TryReadU32(out var abdomen)
            || !reader.TryReadU32(out var upperArm) || !reader.TryReadU32(out var lowerArm) || !reader.TryReadU32(out var hand)
            || !reader.TryReadU32(out var upperLeg) || !reader.TryReadU32(out var lowerLeg) || !reader.TryReadU32(out var foot))
        {
            return false;
        }

        levels = new AppraisalArmorLevels(head, chest, abdomen, upperArm, lowerArm, hand, upperLeg, lowerLeg, foot);
        return true;
    }
}
