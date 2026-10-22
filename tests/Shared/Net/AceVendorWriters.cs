namespace Lifestone.Tests.Shared.Net;

/// <summary>One row of <see cref="AceVendorWriters.ApproachVendor"/>: the stack ACE lists (-1 is unlimited) and the object.</summary>
/// <param name="StackSize">ACE's <c>VendorShopCreateListStackSize ?? StackSize ?? 1</c>; -1 means unlimited supply.</param>
/// <param name="Item">The stock object: its GUID and its WeenieDesc fields.</param>
public sealed record AceVendorRow(int StackSize, AceCreateObject Item);

/// <summary>
/// The sections of an IdentifyObjectResponse (<c>AppraiseInfo</c>): ACE derives the flags from what is non-empty
/// (<c>AppraiseInfo.BuildFlags</c>, <c>AppraiseInfo.cs:700-735</c>), and so does <see cref="AceVendorWriters.IdentifyResponse"/>.
/// </summary>
public sealed record AceAppraisal
{
    /// <summary>Whether the appraisal succeeded (<c>Convert.ToUInt32(Success)</c>).</summary>
    public bool Success { get; init; }

    /// <summary>PropertyInt by property id (flag 0x1).</summary>
    public IReadOnlyDictionary<uint, int> Ints { get; init; } = new Dictionary<uint, int>();

    /// <summary>PropertyInt64 (flag 0x2000).</summary>
    public IReadOnlyDictionary<uint, long> Int64s { get; init; } = new Dictionary<uint, long>();

    /// <summary>PropertyBool (flag 0x2).</summary>
    public IReadOnlyDictionary<uint, bool> Bools { get; init; } = new Dictionary<uint, bool>();

    /// <summary>PropertyFloat, a double on the wire (flag 0x4).</summary>
    public IReadOnlyDictionary<uint, double> Floats { get; init; } = new Dictionary<uint, double>();

    /// <summary>PropertyString (flag 0x8).</summary>
    public IReadOnlyDictionary<uint, string> Strings { get; init; } = new Dictionary<uint, string>();

    /// <summary>PropertyDataId (flag 0x1000).</summary>
    public IReadOnlyDictionary<uint, uint> DataIds { get; init; } = new Dictionary<uint, uint>();

    /// <summary>The spell book as written, enchantments or'ed with 0x80000000 (flag 0x10).</summary>
    public IReadOnlyList<uint> SpellBook { get; init; } = [];

    /// <summary>The eight armour protections (flag 0x80).</summary>
    public float[]? Armor { get; init; }

    /// <summary>The creature profile: health, max health, and the optional attributes and masks (flag 0x100).</summary>
    public AceCreatureProfile? Creature { get; init; }

    /// <summary>The weapon profile (flag 0x20).</summary>
    public AceWeaponProfile? Weapon { get; init; }

    /// <summary>The hook profile {flags, valid locations, ammo type} (flag 0x40).</summary>
    public (uint Flags, uint ValidLocations, uint AmmoType)? Hook { get; init; }

    /// <summary>The armour highlight and colour (flag 0x200).</summary>
    public (ushort Highlight, ushort Color)? ArmorBits { get; init; }

    /// <summary>The weapon highlight and colour (flag 0x800).</summary>
    public (ushort Highlight, ushort Color)? WeaponBits { get; init; }

    /// <summary>The resist highlight and colour (flag 0x400).</summary>
    public (ushort Highlight, ushort Color)? ResistBits { get; init; }

    /// <summary>The nine armour levels: head, chest, abdomen, upper arm, lower arm, hand, upper leg, lower leg, foot (flag 0x4000).</summary>
    public uint[]? ArmorLevels { get; init; }
}

/// <summary>ACE's <c>CreatureProfile</c> as it is written (<c>CreatureProfile.cs:77-111</c>).</summary>
public sealed record AceCreatureProfile(uint Health, uint HealthMax)
{
    /// <summary>Ten words (Strength, Endurance, Quickness, Coordination, Focus, Self, Stamina, Mana, StaminaMax, ManaMax); sets flag 0x8.</summary>
    public uint[]? Attributes { get; init; }

    /// <summary>The attribute highlight and colour masks; sets flag 0x1.</summary>
    public (ushort Highlights, ushort Colors)? Buffs { get; init; }

    /// <summary>Extra flag bits ACE leaves unknown (0x2, 0x4); they carry no data.</summary>
    public uint ExtraFlags { get; init; }
}

/// <summary>ACE's <c>WeaponProfile</c> as it is written (<c>WeaponProfile.cs:148-165</c>).</summary>
public sealed record AceWeaponProfile(
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

/// <summary>
/// The M4 3.2 server-to-client vendor and appraisal events as the bundled ACE writes them, for tests in every project:
/// GameEvent 0x0062 ApproachVendor (<c>GameEventApproachVendor.cs:14-63</c>) and GameEvent 0x00C9 IdentifyObjectResponse
/// (<c>GameEventIdentifyObjectResponse.cs:9-16</c>, <c>AppraiseInfoExtensions.Write</c> <c>AppraiseInfo.cs:743-786</c> and the profile
/// writers). Built with a plain <see cref="BinaryWriter"/> from the cited ACE source (ACEmulator/ACE @95ef426d, read, not copied),
/// independently of Lifestone.Net's codecs.
/// </summary>
public static class AceVendorWriters
{
    private const uint GameEventOpcode = 0xF7B0;

    /// <summary>The bucket counts ACE passes to <c>PackableHashTable.WriteHeader</c> (<c>AppraiseInfo.cs:791-796</c>): 16 for ints, 8 for the rest.</summary>
    private const ushort IntBuckets = 16;

    private const ushort OtherBuckets = 8;

    /// <summary>The whole 0062 message: the GameEvent envelope, the profile, the alternate currency, the rows and ACE's closing Align.</summary>
    public static byte[] ApproachVendor(
        uint playerGuid,
        uint sequence,
        uint vendorGuid,
        uint merchandiseItemTypes,
        uint merchandiseMinValue,
        uint merchandiseMaxValue,
        bool dealMagicalItems,
        float buyPrice,
        float sellPrice,
        IReadOnlyList<AceVendorRow> rows,
        uint alternateCurrencyWcid = 0,
        uint alternateCurrencyCount = 0,
        string alternateCurrencyPluralName = "")
    {
        ArgumentNullException.ThrowIfNull(rows);
        return AceWireWriters.Message(GameEventOpcode, w =>
        {
            w.Write(playerGuid);
            w.Write(sequence);
            w.Write(0x0062u);

            w.Write(vendorGuid);
            w.Write(merchandiseItemTypes);
            w.Write(merchandiseMinValue);
            w.Write(merchandiseMaxValue);
            w.Write(Convert.ToUInt32(dealMagicalItems));
            w.Write(buyPrice);
            w.Write(sellPrice);
            w.Write(alternateCurrencyWcid);
            w.Write(alternateCurrencyCount);
            AceWireWriters.WriteString16L(w, alternateCurrencyPluralName);
            w.Write(rows.Count);
            foreach (var row in rows)
            {
                // Writer.Write(stackSize & 0xFFFFFF | -1 << 24): the high byte is the pwdType, always 0xFF.
                w.Write((row.StackSize & 0xFFFFFF) | (-1 << 24));

                // SerializeGameDataOnly: WriteGuid, then the WeenieDesc block (no model data, no physics data).
                w.Write(row.Item.Guid);
                AceWireWriters.WriteWeenieDesc(w, row.Item);
            }

            AceWireWriters.Align(w);
        });
    }

    /// <summary>The whole 00C9 message: the GameEvent envelope, the object's guid and <c>Write(AppraiseInfo)</c>.</summary>
    public static byte[] IdentifyResponse(uint playerGuid, uint sequence, uint objectGuid, AceAppraisal info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return AceWireWriters.Message(GameEventOpcode, w =>
        {
            w.Write(playerGuid);
            w.Write(sequence);
            w.Write(0x00C9u);
            w.Write(objectGuid);
            WriteAppraiseInfo(w, info);
        });
    }

    /// <summary>The flags ACE derives from the non-empty sections (<c>BuildFlags</c>).</summary>
    public static uint FlagsOf(AceAppraisal info)
    {
        ArgumentNullException.ThrowIfNull(info);
        uint flags = 0;
        if (info.Ints.Count > 0) flags |= 0x1;
        if (info.Int64s.Count > 0) flags |= 0x2000;
        if (info.Bools.Count > 0) flags |= 0x2;
        if (info.Floats.Count > 0) flags |= 0x4;
        if (info.Strings.Count > 0) flags |= 0x8;
        if (info.DataIds.Count > 0) flags |= 0x1000;
        if (info.SpellBook.Count > 0) flags |= 0x10;
        if (info.ResistBits is not null) flags |= 0x400;
        if (info.Armor is not null) flags |= 0x80;
        if (info.Creature is not null) flags |= 0x100;
        if (info.Weapon is not null) flags |= 0x20;
        if (info.Hook is not null) flags |= 0x40;
        if (info.ArmorBits is not null) flags |= 0x200;
        if (info.WeaponBits is not null) flags |= 0x800;
        if (info.ArmorLevels is not null) flags |= 0x4000;
        return flags;
    }

    /// <summary><c>AppraiseInfoExtensions.Write</c>: flags, success, then the sections in ACE's order (not bit order).</summary>
    public static void WriteAppraiseInfo(BinaryWriter w, AceAppraisal info, uint? flagsOverride = null)
    {
        w.Write(flagsOverride ?? FlagsOf(info));
        w.Write(Convert.ToUInt32(info.Success));
        var flags = flagsOverride ?? FlagsOf(info);

        if ((flags & 0x1) != 0)
        {
            WriteTable(w, info.Ints, IntBuckets, (ww, v) => ww.Write(v));
        }

        if ((flags & 0x2000) != 0)
        {
            WriteTable(w, info.Int64s, OtherBuckets, (ww, v) => ww.Write(v));
        }

        if ((flags & 0x2) != 0)
        {
            WriteTable(w, info.Bools, OtherBuckets, (ww, v) => ww.Write(Convert.ToUInt32(v)));
        }

        if ((flags & 0x4) != 0)
        {
            WriteTable(w, info.Floats, OtherBuckets, (ww, v) => ww.Write(v));
        }

        if ((flags & 0x8) != 0)
        {
            WriteTable(w, info.Strings, OtherBuckets, AceWireWriters.WriteString16L);
        }

        if ((flags & 0x1000) != 0)
        {
            WriteTable(w, info.DataIds, OtherBuckets, (ww, v) => ww.Write(v));
        }

        if ((flags & 0x10) != 0)
        {
            // PackableList: Write(list.Count) then each id.
            w.Write(info.SpellBook.Count);
            foreach (var id in info.SpellBook)
            {
                w.Write(id);
            }
        }

        if ((flags & 0x80) != 0)
        {
            foreach (var protection in info.Armor!)
            {
                w.Write(protection);
            }
        }

        if ((flags & 0x100) != 0)
        {
            var creature = info.Creature!;
            var creatureFlags = creature.ExtraFlags | (creature.Attributes is null ? 0u : 0x8u) | (creature.Buffs is null ? 0u : 0x1u);
            w.Write(creatureFlags);
            w.Write(creature.Health);
            w.Write(creature.HealthMax);
            if (creature.Attributes is { } attributes)
            {
                foreach (var word in attributes)
                {
                    w.Write(word);
                }
            }

            if (creature.Buffs is { } buffs)
            {
                w.Write(buffs.Highlights);
                w.Write(buffs.Colors);
            }
        }

        if ((flags & 0x20) != 0)
        {
            var weapon = info.Weapon!;
            w.Write(weapon.DamageType);
            w.Write(weapon.WeaponTime);
            w.Write(weapon.WeaponSkill);
            w.Write(weapon.Damage);
            w.Write(weapon.DamageVariance);
            w.Write(weapon.DamageMod);
            w.Write(weapon.WeaponLength);
            w.Write(weapon.MaxVelocity);
            w.Write(weapon.WeaponOffense);
            w.Write(weapon.MaxVelocityEstimated);
        }

        if ((flags & 0x40) != 0)
        {
            w.Write(info.Hook!.Value.Flags);
            w.Write(info.Hook.Value.ValidLocations);
            w.Write(info.Hook.Value.AmmoType);
        }

        if ((flags & 0x200) != 0)
        {
            w.Write(info.ArmorBits!.Value.Highlight);
            w.Write(info.ArmorBits.Value.Color);
        }

        if ((flags & 0x800) != 0)
        {
            w.Write(info.WeaponBits!.Value.Highlight);
            w.Write(info.WeaponBits.Value.Color);
        }

        if ((flags & 0x400) != 0)
        {
            w.Write(info.ResistBits!.Value.Highlight);
            w.Write(info.ResistBits.Value.Color);
        }

        if ((flags & 0x4000) != 0)
        {
            foreach (var level in info.ArmorLevels!)
            {
                w.Write(level);
            }
        }
    }

    /// <summary>A packable hash table: the u16 count, the u16 bucket count, then the rows in key order (ACE sorts by bucket; a reader does not care).</summary>
    private static void WriteTable<T>(BinaryWriter w, IReadOnlyDictionary<uint, T> table, ushort buckets, Action<BinaryWriter, T> writeValue)
    {
        w.Write((ushort)table.Count);
        w.Write(buckets);
        foreach (var key in table.Keys.OrderBy(k => k % buckets).ThenBy(k => k))
        {
            w.Write(key);
            writeValue(w, table[key]);
        }
    }
}
