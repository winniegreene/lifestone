namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The inventory locations a wielded or worn item occupies (retail's <c>INVENTORY_LOC</c>, ACE's <c>EquipMask</c>; the <c>loc</c> of GameEvent 0x0013's
/// equipped list and of GE 0x0023): the argument of <see cref="IActionApi.Wield"/> and the value of <see cref="InventoryPlace.Location"/>.
/// The single-slot values are the wire values; the combined ones below them are ACE's groupings, kept for callers that ask "any of these".
/// </summary>
[Flags]
public enum EquipMask : uint
{
    /// <summary>No location (never valid for <see cref="IActionApi.Wield"/>).</summary>
    None = 0,

    /// <summary>Head clothing.</summary>
    HeadWear = 0x00000001,

    /// <summary>Chest clothing.</summary>
    ChestWear = 0x00000002,

    /// <summary>Abdomen clothing.</summary>
    AbdomenWear = 0x00000004,

    /// <summary>Upper-arm clothing.</summary>
    UpperArmWear = 0x00000008,

    /// <summary>Lower-arm clothing.</summary>
    LowerArmWear = 0x00000010,

    /// <summary>Hand clothing.</summary>
    HandWear = 0x00000020,

    /// <summary>Upper-leg clothing.</summary>
    UpperLegWear = 0x00000040,

    /// <summary>Lower-leg clothing.</summary>
    LowerLegWear = 0x00000080,

    /// <summary>Foot clothing (also armour).</summary>
    FootWear = 0x00000100,

    /// <summary>Chest armour.</summary>
    ChestArmor = 0x00000200,

    /// <summary>Abdomen armour.</summary>
    AbdomenArmor = 0x00000400,

    /// <summary>Upper-arm armour.</summary>
    UpperArmArmor = 0x00000800,

    /// <summary>Lower-arm armour.</summary>
    LowerArmArmor = 0x00001000,

    /// <summary>Upper-leg armour.</summary>
    UpperLegArmor = 0x00002000,

    /// <summary>Lower-leg armour.</summary>
    LowerLegArmor = 0x00004000,

    /// <summary>Necklace.</summary>
    NeckWear = 0x00008000,

    /// <summary>Left bracelet.</summary>
    WristWearLeft = 0x00010000,

    /// <summary>Right bracelet.</summary>
    WristWearRight = 0x00020000,

    /// <summary>Left ring.</summary>
    FingerWearLeft = 0x00040000,

    /// <summary>Right ring.</summary>
    FingerWearRight = 0x00080000,

    /// <summary>A melee weapon.</summary>
    MeleeWeapon = 0x00100000,

    /// <summary>A shield.</summary>
    Shield = 0x00200000,

    /// <summary>A missile weapon (bow, crossbow, thrown).</summary>
    MissileWeapon = 0x00400000,

    /// <summary>Ammunition.</summary>
    MissileAmmo = 0x00800000,

    /// <summary>Held (a caster: wand, orb, staff).</summary>
    Held = 0x01000000,

    /// <summary>A two-handed weapon.</summary>
    TwoHanded = 0x02000000,

    /// <summary>The trinket slot.</summary>
    TrinketOne = 0x04000000,

    /// <summary>A cloak.</summary>
    Cloak = 0x08000000,

    /// <summary>The first sigil slot.</summary>
    SigilOne = 0x10000000,

    /// <summary>The second sigil slot.</summary>
    SigilTwo = 0x20000000,

    /// <summary>The third sigil slot.</summary>
    SigilThree = 0x40000000,

    /// <summary>The slots a player selects with the ready-slot keys: melee, shield, missile, held and two-handed (ACE's <c>Selectable</c>).</summary>
    Selectable = MeleeWeapon | Shield | MissileWeapon | Held | TwoHanded,

    /// <summary>Every location (ACE's <c>All</c>).</summary>
    All = 0x7FFFFFFF,
}
