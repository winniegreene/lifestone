using System.Collections.Immutable;

namespace Lifestone.Net.Types;

/// <summary>
/// The WeenieDesc block of CreateObject (F745/F7DB), field for field (docs/protocol.md §5.3): the fixed header, then
/// the optional fields in ACE's write order (not bit order). A field is null when its flag is absent. Widths follow the
/// C# types ACE writes (<c>WorldObject_Networking.cs</c> L75-205).
/// </summary>
public sealed record WeenieDesc
{
    /// <summary>The ObjectDescriptionFlags bit that announces <see cref="Header2"/>.</summary>
    public const uint IncludesSecondHeader = 0x04000000;

    /// <summary>The first header's flags.</summary>
    public required WeenieHeaderFlags Header { get; init; }

    /// <summary>The second header's flags (present when ObjectDescriptionFlags has <see cref="IncludesSecondHeader"/>).</summary>
    public WeenieHeaderFlags2 Header2 { get; init; }

    /// <summary>The object's name.</summary>
    public required string Name { get; init; }

    /// <summary>The weenie class id (PackedDword).</summary>
    public required uint WeenieClassId { get; init; }

    /// <summary>The icon (a full 0x06xxxxxx id; a packed 0 reads as the bare type id 0x06000000, as retail reads it).</summary>
    public required uint IconId { get; init; }

    /// <summary>The ItemType bits.</summary>
    public required uint ItemType { get; init; }

    /// <summary>The ObjectDescriptionFlags bits (player, attackable, stuck, ...).</summary>
    public required uint ObjectDescriptionFlags { get; init; }

    /// <summary>PluralName (0x1).</summary>
    public string? PluralName { get; init; }

    /// <summary>ItemsCapacity (0x2), u8.</summary>
    public byte? ItemsCapacity { get; init; }

    /// <summary>ContainersCapacity (0x4), u8.</summary>
    public byte? ContainersCapacity { get; init; }

    /// <summary>AmmoType (0x100), u16.</summary>
    public ushort? AmmoType { get; init; }

    /// <summary>Value (0x8), u32.</summary>
    public uint? Value { get; init; }

    /// <summary>Usable (0x10), u32.</summary>
    public uint? Usable { get; init; }

    /// <summary>UseRadius (0x20), f32.</summary>
    public float? UseRadius { get; init; }

    /// <summary>TargetType (0x80000), u32.</summary>
    public uint? TargetType { get; init; }

    /// <summary>UiEffects (0x80), u32.</summary>
    public uint? UiEffects { get; init; }

    /// <summary>CombatUse (0x200), i8.</summary>
    public sbyte? CombatUse { get; init; }

    /// <summary>Structure (0x400), u16.</summary>
    public ushort? Structure { get; init; }

    /// <summary>MaxStructure (0x800), u16.</summary>
    public ushort? MaxStructure { get; init; }

    /// <summary>StackSize (0x1000), u16.</summary>
    public ushort? StackSize { get; init; }

    /// <summary>MaxStackSize (0x2000), u16.</summary>
    public ushort? MaxStackSize { get; init; }

    /// <summary>Container (0x4000), u32 GUID.</summary>
    public uint? ContainerId { get; init; }

    /// <summary>Wielder (0x8000), u32 GUID.</summary>
    public uint? WielderId { get; init; }

    /// <summary>ValidLocations (0x10000), u32.</summary>
    public uint? ValidLocations { get; init; }

    /// <summary>CurrentlyWieldedLocation (0x20000), u32.</summary>
    public uint? CurrentlyWieldedLocation { get; init; }

    /// <summary>Priority (0x40000), u32.</summary>
    public uint? Priority { get; init; }

    /// <summary>RadarBlipColor (0x100000), u8.</summary>
    public byte? RadarBlipColor { get; init; }

    /// <summary>RadarBehavior (0x800000), u8.</summary>
    public byte? RadarBehavior { get; init; }

    /// <summary>PScript (0x8000000), u16.</summary>
    public ushort? PhysicsScript { get; init; }

    /// <summary>Workmanship (0x1000000), f32.</summary>
    public float? Workmanship { get; init; }

    /// <summary>Burden (0x200000), u16.</summary>
    public ushort? Burden { get; init; }

    /// <summary>Spell (0x400000), u16.</summary>
    public ushort? SpellId { get; init; }

    /// <summary>HouseOwner (0x2000000), u32 GUID.</summary>
    public uint? HouseOwnerId { get; init; }

    /// <summary>HouseRestrictions (0x4000000), a RestrictionDB.</summary>
    public HouseRestrictions? HouseRestrictions { get; init; }

    /// <summary>HookItemTypes (0x20000000), u32.</summary>
    public uint? HookItemTypes { get; init; }

    /// <summary>Monarch (0x40), u32 GUID.</summary>
    public uint? MonarchId { get; init; }

    /// <summary>HookType (0x10000000), u16.</summary>
    public ushort? HookType { get; init; }

    /// <summary>IconOverlay (0x40000000), a full 0x06xxxxxx id (a packed 0 reads as 0x06000000).</summary>
    public uint? IconOverlayId { get; init; }

    /// <summary>IconUnderlay (second header 0x1), a full 0x06xxxxxx id (a packed 0 reads as 0x06000000).</summary>
    public uint? IconUnderlayId { get; init; }

    /// <summary>MaterialType (0x80000000), u32.</summary>
    public uint? MaterialType { get; init; }

    /// <summary>Cooldown (second header 0x2), i32 cooldown id.</summary>
    public int? CooldownId { get; init; }

    /// <summary>CooldownDuration (second header 0x4), f64 seconds.</summary>
    public double? CooldownDuration { get; init; }

    /// <summary>PetOwner (second header 0x8), u32 GUID.</summary>
    public uint? PetOwnerId { get; init; }
}

/// <summary>
/// A house's RestrictionDB in a <see cref="WeenieDesc"/>: u32 version, u32 open status, u32 monarch, then a hash table
/// of guests and their access (u16 guest count, u16 bucket count, the guests).
/// </summary>
/// <param name="Version">The RestrictionDB version word.</param>
/// <param name="OpenStatus">The open status (non-zero = open to everyone).</param>
/// <param name="MonarchId">The monarch whose allegiance has access, or 0.</param>
/// <param name="Guests">The guests and their access words, in wire order.</param>
public sealed record HouseRestrictions(uint Version, uint OpenStatus, uint MonarchId, ImmutableArray<HouseGuest> Guests)
{
    /// <summary>
    /// The hash table's bucket-count word as sent (N7, additive): ACE writes 768, the value retail's packets carried;
    /// retail's <c>RestrictionDB::UnPack</c> (11.4186 0x005AE990, 11.6096 0x005AFA40) sizes its table from a constant
    /// instead.
    /// </summary>
    public ushort BucketCount { get; init; }
}

/// <summary>One guest of <see cref="HouseRestrictions"/>: a GUID and its access word (storage access when non-zero).</summary>
/// <param name="Guid">The guest.</param>
/// <param name="Access">The access word.</param>
public readonly record struct HouseGuest(uint Guid, uint Access);

/// <summary>The first WeenieDesc header's flags (docs/protocol.md §5.3).</summary>
[Flags]
public enum WeenieHeaderFlags : uint
{
    /// <summary>No optional fields.</summary>
    None = 0,

    /// <summary>String16L plural name.</summary>
    PluralName = 0x1,

    /// <summary>u8 items capacity.</summary>
    ItemsCapacity = 0x2,

    /// <summary>u8 containers capacity.</summary>
    ContainersCapacity = 0x4,

    /// <summary>u32 value.</summary>
    Value = 0x8,

    /// <summary>u32 usable.</summary>
    Usable = 0x10,

    /// <summary>f32 use radius.</summary>
    UseRadius = 0x20,

    /// <summary>u32 monarch.</summary>
    Monarch = 0x40,

    /// <summary>u32 UI effects.</summary>
    UiEffects = 0x80,

    /// <summary>u16 ammo type.</summary>
    AmmoType = 0x100,

    /// <summary>i8 combat use.</summary>
    CombatUse = 0x200,

    /// <summary>u16 structure.</summary>
    Structure = 0x400,

    /// <summary>u16 max structure.</summary>
    MaxStructure = 0x800,

    /// <summary>u16 stack size.</summary>
    StackSize = 0x1000,

    /// <summary>u16 max stack size.</summary>
    MaxStackSize = 0x2000,

    /// <summary>u32 container.</summary>
    Container = 0x4000,

    /// <summary>u32 wielder.</summary>
    Wielder = 0x8000,

    /// <summary>u32 valid locations.</summary>
    ValidLocations = 0x10000,

    /// <summary>u32 currently wielded location.</summary>
    CurrentlyWieldedLocation = 0x20000,

    /// <summary>u32 priority.</summary>
    Priority = 0x40000,

    /// <summary>u32 target type.</summary>
    TargetType = 0x80000,

    /// <summary>u8 radar blip colour.</summary>
    RadarBlipColor = 0x100000,

    /// <summary>u16 burden.</summary>
    Burden = 0x200000,

    /// <summary>u16 spell.</summary>
    Spell = 0x400000,

    /// <summary>u8 radar behaviour.</summary>
    RadarBehavior = 0x800000,

    /// <summary>f32 workmanship.</summary>
    Workmanship = 0x1000000,

    /// <summary>u32 house owner.</summary>
    HouseOwner = 0x2000000,

    /// <summary>RestrictionDB house restrictions.</summary>
    HouseRestrictions = 0x4000000,

    /// <summary>u16 physics script.</summary>
    PScript = 0x8000000,

    /// <summary>u16 hook type.</summary>
    HookType = 0x10000000,

    /// <summary>u32 hook item types.</summary>
    HookItemTypes = 0x20000000,

    /// <summary>PackedDword icon overlay.</summary>
    IconOverlay = 0x40000000,

    /// <summary>u32 material type.</summary>
    MaterialType = 0x80000000,
}

/// <summary>The second WeenieDesc header's flags (docs/protocol.md §5.3).</summary>
[Flags]
public enum WeenieHeaderFlags2 : uint
{
    /// <summary>No optional fields.</summary>
    None = 0,

    /// <summary>PackedDword icon underlay.</summary>
    IconUnderlay = 0x1,

    /// <summary>i32 cooldown id.</summary>
    Cooldown = 0x2,

    /// <summary>f64 cooldown duration.</summary>
    CooldownDuration = 0x4,

    /// <summary>u32 pet owner.</summary>
    PetOwner = 0x8,
}
