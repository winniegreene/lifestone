// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/PublicWeenieDescBody.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: PublicWeenieDescParser.Parse as an internal
// static WeenieDescCodec over N2's WireReader that returns false instead of keeping a partial result; every field keeps
// ACE's width, and its sign apart from Value, which ACE holds as an int and docs/protocol.md names a u32 (OpenAC widened
// several to int and read the capacities as signed); PScript is kept (OpenAC skipped it); the RestrictionDB is read as
// ACE writes it (u16 count, u16 bucket count) into the Lifestone record HouseRestrictions instead of
// AcDream.Core.Items.HouseRestrictionRecord; the closing Align must be present; the icons are read as retail's
// Unpack_AsDataIDOfKnownType reads them, so a packed 0 is 0x06000000 (OpenAC kept 0); the result is the N0 record
// WeenieDesc.

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The WeenieDesc block that closes F745/F7DB CreateObject (docs/protocol.md §5.3; ACE
/// <c>WorldObject_Networking.SerializeCreateObject</c>): u32 header flags, String16L name, PackedDword weenie class,
/// known-type icon, u32 ItemType, u32 ObjectDescriptionFlags, Align; the second header when ObjectDescriptionFlags has
/// 0x04000000; the optional fields in ACE's write order (not bit order); then Align. Retail reads the same order in
/// <c>PublicWeenieDesc::UnPack</c> (11.4186 0x005AD470, 11.6096 0x005AE520), the house restrictions with
/// <c>RestrictionDB::UnPack</c> (11.4186 0x005AE990, 11.6096 0x005AFA40).
/// </summary>
internal static class WeenieDescCodec
{
    /// <summary>The icon type prefix (0x06xxxxxx).</summary>
    public const uint IconType = 0x06000000;

    /// <summary>The size of one RestrictionDB guest: u32 GUID, u32 access.</summary>
    private const int GuestSize = 8;

    /// <summary>Reads one WeenieDesc block, its closing Align included.</summary>
    public static bool TryRead(ref WireReader reader, out WeenieDesc weenie)
    {
        weenie = null!;
        if (!reader.TryReadU32(out var rawHeader)
            || !reader.TryReadString16L(out var name)
            || !reader.TryReadPackedDword(out var weenieClass)
            || !reader.TryReadKnownTypePackedDword(IconType, out var icon)
            || !reader.TryReadU32(out var itemType)
            || !reader.TryReadU32(out var descriptionFlags)
            || !reader.TryAlign())
        {
            return false;
        }

        var header = (WeenieHeaderFlags)rawHeader;
        var header2 = WeenieHeaderFlags2.None;
        if ((descriptionFlags & WeenieDesc.IncludesSecondHeader) != 0)
        {
            if (!reader.TryReadU32(out var rawHeader2))
            {
                return false;
            }

            header2 = (WeenieHeaderFlags2)rawHeader2;
        }

        var fields = new Fields();
        if (!fields.TryRead(ref reader, header, header2) || !reader.TryAlign())
        {
            return false;
        }

        weenie = new WeenieDesc
        {
            Header = header,
            Header2 = header2,
            Name = name,
            WeenieClassId = weenieClass,
            IconId = icon,
            ItemType = itemType,
            ObjectDescriptionFlags = descriptionFlags,
            PluralName = fields.PluralName,
            ItemsCapacity = fields.ItemsCapacity,
            ContainersCapacity = fields.ContainersCapacity,
            AmmoType = fields.AmmoType,
            Value = fields.Value,
            Usable = fields.Usable,
            UseRadius = fields.UseRadius,
            TargetType = fields.TargetType,
            UiEffects = fields.UiEffects,
            CombatUse = fields.CombatUse,
            Structure = fields.Structure,
            MaxStructure = fields.MaxStructure,
            StackSize = fields.StackSize,
            MaxStackSize = fields.MaxStackSize,
            ContainerId = fields.ContainerId,
            WielderId = fields.WielderId,
            ValidLocations = fields.ValidLocations,
            CurrentlyWieldedLocation = fields.CurrentlyWieldedLocation,
            Priority = fields.Priority,
            RadarBlipColor = fields.RadarBlipColor,
            RadarBehavior = fields.RadarBehavior,
            PhysicsScript = fields.PhysicsScript,
            Workmanship = fields.Workmanship,
            Burden = fields.Burden,
            SpellId = fields.SpellId,
            HouseOwnerId = fields.HouseOwnerId,
            HouseRestrictions = fields.HouseRestrictions,
            HookItemTypes = fields.HookItemTypes,
            MonarchId = fields.MonarchId,
            HookType = fields.HookType,
            IconOverlayId = fields.IconOverlayId,
            IconUnderlayId = fields.IconUnderlayId,
            MaterialType = fields.MaterialType,
            CooldownId = fields.CooldownId,
            CooldownDuration = fields.CooldownDuration,
            PetOwnerId = fields.PetOwnerId,
        };
        return true;
    }

    /// <summary>RestrictionDB: u32 version, u32 open status, u32 monarch, u16 guest count, u16 bucket count, the guests.</summary>
    private static bool TryReadRestrictions(ref WireReader reader, out HouseRestrictions? restrictions)
    {
        restrictions = null;
        if (!reader.TryReadU32(out var version)
            || !reader.TryReadU32(out var open)
            || !reader.TryReadU32(out var monarch)
            || !reader.TryReadCount16(GuestSize, out var count)
            || !reader.TryReadU16(out var buckets))
        {
            return false;
        }

        // The guest count was checked against the bytes left before the bucket word; check again past it.
        if (reader.Remaining < count * GuestSize)
        {
            return false;
        }

        var guests = ImmutableArray.CreateBuilder<HouseGuest>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var guid) || !reader.TryReadU32(out var access))
            {
                return false;
            }

            guests.Add(new HouseGuest(guid, access));
        }

        restrictions = new HouseRestrictions(version, open, monarch, guests.MoveToImmutable()) { BucketCount = buckets };
        return true;
    }

    /// <summary>The optional fields, read in ACE's write order.</summary>
    private struct Fields
    {
        public string? PluralName;
        public byte? ItemsCapacity;
        public byte? ContainersCapacity;
        public ushort? AmmoType;
        public uint? Value;
        public uint? Usable;
        public float? UseRadius;
        public uint? TargetType;
        public uint? UiEffects;
        public sbyte? CombatUse;
        public ushort? Structure;
        public ushort? MaxStructure;
        public ushort? StackSize;
        public ushort? MaxStackSize;
        public uint? ContainerId;
        public uint? WielderId;
        public uint? ValidLocations;
        public uint? CurrentlyWieldedLocation;
        public uint? Priority;
        public byte? RadarBlipColor;
        public byte? RadarBehavior;
        public ushort? PhysicsScript;
        public float? Workmanship;
        public ushort? Burden;
        public ushort? SpellId;
        public uint? HouseOwnerId;
        public HouseRestrictions? HouseRestrictions;
        public uint? HookItemTypes;
        public uint? MonarchId;
        public ushort? HookType;
        public uint? IconOverlayId;
        public uint? IconUnderlayId;
        public uint? MaterialType;
        public int? CooldownId;
        public double? CooldownDuration;
        public uint? PetOwnerId;

        public bool TryRead(ref WireReader reader, WeenieHeaderFlags h, WeenieHeaderFlags2 h2)
        {
            if (Has(h, WeenieHeaderFlags.PluralName))
            {
                if (!reader.TryReadString16L(out var plural))
                {
                    return false;
                }

                PluralName = plural;
            }

            if (!U8(ref reader, Has(h, WeenieHeaderFlags.ItemsCapacity), ref ItemsCapacity)
                || !U8(ref reader, Has(h, WeenieHeaderFlags.ContainersCapacity), ref ContainersCapacity)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.AmmoType), ref AmmoType)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Value), ref Value)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Usable), ref Usable)
                || !F32(ref reader, Has(h, WeenieHeaderFlags.UseRadius), ref UseRadius)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.TargetType), ref TargetType)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.UiEffects), ref UiEffects))
            {
                return false;
            }

            if (Has(h, WeenieHeaderFlags.CombatUse))
            {
                if (!reader.TryReadU8(out var combatUse))
                {
                    return false;
                }

                CombatUse = unchecked((sbyte)combatUse);
            }

            if (!U16(ref reader, Has(h, WeenieHeaderFlags.Structure), ref Structure)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.MaxStructure), ref MaxStructure)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.StackSize), ref StackSize)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.MaxStackSize), ref MaxStackSize)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Container), ref ContainerId)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Wielder), ref WielderId)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.ValidLocations), ref ValidLocations)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.CurrentlyWieldedLocation), ref CurrentlyWieldedLocation)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Priority), ref Priority)
                || !U8(ref reader, Has(h, WeenieHeaderFlags.RadarBlipColor), ref RadarBlipColor)
                || !U8(ref reader, Has(h, WeenieHeaderFlags.RadarBehavior), ref RadarBehavior)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.PScript), ref PhysicsScript)
                || !F32(ref reader, Has(h, WeenieHeaderFlags.Workmanship), ref Workmanship)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.Burden), ref Burden)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.Spell), ref SpellId)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.HouseOwner), ref HouseOwnerId))
            {
                return false;
            }

            if (Has(h, WeenieHeaderFlags.HouseRestrictions))
            {
                if (!TryReadRestrictions(ref reader, out var restrictions))
                {
                    return false;
                }

                HouseRestrictions = restrictions;
            }

            if (!U32(ref reader, Has(h, WeenieHeaderFlags.HookItemTypes), ref HookItemTypes)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.Monarch), ref MonarchId)
                || !U16(ref reader, Has(h, WeenieHeaderFlags.HookType), ref HookType)
                || !Icon(ref reader, Has(h, WeenieHeaderFlags.IconOverlay), ref IconOverlayId)
                || !Icon(ref reader, (h2 & WeenieHeaderFlags2.IconUnderlay) != 0, ref IconUnderlayId)
                || !U32(ref reader, Has(h, WeenieHeaderFlags.MaterialType), ref MaterialType))
            {
                return false;
            }

            if ((h2 & WeenieHeaderFlags2.Cooldown) != 0)
            {
                if (!reader.TryReadI32(out var cooldown))
                {
                    return false;
                }

                CooldownId = cooldown;
            }

            if ((h2 & WeenieHeaderFlags2.CooldownDuration) != 0)
            {
                if (!reader.TryReadF64(out var duration))
                {
                    return false;
                }

                CooldownDuration = duration;
            }

            return U32(ref reader, (h2 & WeenieHeaderFlags2.PetOwner) != 0, ref PetOwnerId);
        }

        private static bool Has(WeenieHeaderFlags flags, WeenieHeaderFlags flag) => (flags & flag) != 0;

        private static bool U8(ref WireReader reader, bool present, ref byte? value)
        {
            if (!present)
            {
                return true;
            }

            if (!reader.TryReadU8(out var read))
            {
                return false;
            }

            value = read;
            return true;
        }

        private static bool U16(ref WireReader reader, bool present, ref ushort? value)
        {
            if (!present)
            {
                return true;
            }

            if (!reader.TryReadU16(out var read))
            {
                return false;
            }

            value = read;
            return true;
        }

        private static bool U32(ref WireReader reader, bool present, ref uint? value)
        {
            if (!present)
            {
                return true;
            }

            if (!reader.TryReadU32(out var read))
            {
                return false;
            }

            value = read;
            return true;
        }

        private static bool F32(ref WireReader reader, bool present, ref float? value)
        {
            if (!present)
            {
                return true;
            }

            if (!reader.TryReadF32(out var read))
            {
                return false;
            }

            value = read;
            return true;
        }

        private static bool Icon(ref WireReader reader, bool present, ref uint? value)
        {
            if (!present)
            {
                return true;
            }

            if (!reader.TryReadKnownTypePackedDword(IconType, out var read))
            {
                return false;
            }

            value = read;
            return true;
        }
    }
}
