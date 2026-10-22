using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What a line of the fighter's kit is for (<see cref="GearKind"/> has only the mage's three, and GearKit stays as it is).</summary>
public enum FighterRole
{
    /// <summary>The one-handed melee weapon, wielded.</summary>
    Weapon,

    /// <summary>The missile launcher, carried in the pack.</summary>
    Bow,

    /// <summary>Ammunition, a stack in the pack.</summary>
    Ammo,

    /// <summary>A piece of armour, worn.</summary>
    Armour,

    /// <summary>A healing kit (a Healer, used on the player).</summary>
    HealingKit,

    /// <summary>A potion (Food).</summary>
    Potion,
}

/// <summary>One line of the fighter's kit: a <see cref="GearItem"/> (GearKit's record) with its role, its Value and where it goes.</summary>
/// <param name="Item">The line, in GearKit's shape. <see cref="GearItem.Kind"/> is the nearest of GearKit's three kinds (Wand for the two weapons, Armour, Component for stacks and consumables); <paramref name="Role"/> is authoritative.</param>
/// <param name="Role">What it is for.</param>
/// <param name="Value">The weenie's Value (int 19) of one item.</param>
/// <param name="Wield">True when the kit wields or wears it at once; false for the pack.</param>
/// <param name="SkillId">The skill the item uses (the weapon's WeaponSkill, int 48), or null.</param>
public sealed record FighterItem(GearItem Item, FighterRole Role, int Value, bool Wield, uint? SkillId = null)
{
    /// <summary>The weenie class id.</summary>
    public uint Wcid => Item.Wcid;
}

/// <summary>
/// The fighter's gear (M4 item 1.1, plan D14, spec section 5.10): what the <c>item</c> verb gives "Holtburg Hunter", as the weenies ACE spawns.
/// The wcids, burdens and Values are the lead's read-only SELECTs of the pinned world database (v0.9.296, 2026-10-19; cache research.md section 1).
/// </summary>
/// <remarks>
/// <para><b>Wielded and worn.</b> The Mace 331 (one-handed MeleeWeapon, Heavy Weapons 44, Bludgeon: golems resist slashing, not it) and the
/// leather set 45, 47, 81, 56, 115 (armour level 130, 90, 90, 130, 130). <b>In the pack.</b> The Shortbow 307 (Missile Weapons 47, AmmoType Arrow),
/// Arrow 300 x 200 (stack limit 1,000), Handy Healing Kit 628 x 2 (Healer, ItemType 0x80, 20 uses each, BoostValue 50, BoosterEnum Health) and Potion of Healing 377 x 5 (Food, ItemType 0x80, stack limit 100, BoostValue 25).
/// Two kits are two separate items (a Healer does not stack: <see cref="GearItem.MaxStack"/> is null).</para>
/// <para><b>Burden.</b> 675 + 450 + 1,000 + 2,560 + 100 + 75 = <see cref="Burden"/> 4,860, against ACE's capacity of 150 x Strength
/// (<see cref="GearKit.EncumbranceCapacity"/>; retail <c>EncumbranceSystem::EncumbranceCapacity</c>, 0x004FD7A0 in 11.6096, without augmentations):
/// <see cref="CapacityStrength"/> is Strength 100, the creation value and the floor, so the margin holds before any rank is bought.</para>
/// <para>Closed in M4 3.1 s0: <c>TODO(m4-kit-itemtype)</c>. The lead's read-only SELECT of the pinned database gave the healing kit's and the potion's ItemType as 0x80 (Misc), and the kit's BoostValue (PropertyInt 90) as 50, not the 16 this comment once said.</para>
/// </remarks>
public static class FighterKit
{
    /// <summary>ACE WeenieType: 6 MeleeWeapon, 3 MissileLauncher, 5 Ammunition, 2 Clothing, 28 Healer, 18 Food.</summary>
    public const uint WeenieTypeMelee = 6, WeenieTypeLauncher = 3, WeenieTypeAmmunition = 5, WeenieTypeClothing = 2, WeenieTypeHealer = 28, WeenieTypeFood = 18;

    /// <summary>ACE ItemType: 1 MeleeWeapon, 0x100 MissileWeapon, 2 Armor, 0x80 Misc (the healing kit and the potion).</summary>
    public const uint ItemTypeMelee = 0x1, ItemTypeMissile = 0x100, ItemTypeArmor = 0x2, ItemTypeMisc = 0x80;

    /// <summary>The weapon skill the Mace uses (Heavy Weapons) and the skill the Shortbow uses (Missile Weapons).</summary>
    public const uint MaceSkillId = 44, BowSkillId = 47;

    /// <summary>The Mace's wcid (wielded).</summary>
    public const uint MaceWcid = 331;

    /// <summary>The Shortbow's wcid (in the pack).</summary>
    public const uint ShortbowWcid = 307;

    /// <summary>The Arrow's wcid.</summary>
    public const uint ArrowWcid = 300;

    /// <summary>The Handy Healing Kit's wcid.</summary>
    public const uint HealingKitWcid = 628;

    /// <summary>The Potion of Healing's wcid.</summary>
    public const uint PotionWcid = 377;

    /// <summary>The arrows the kit gives.</summary>
    public const int ArrowCount = 200;

    /// <summary>The kits the kit gives (separate items).</summary>
    public const int HealingKitCount = 2;

    /// <summary>The potions the kit gives.</summary>
    public const int PotionCount = 5;

    /// <summary>The kit's total burden (675 + 450 + 1,000 + 2,560 + 100 + 75), checked by the tests against the sum of the lines.</summary>
    public const int Burden = 4_860;

    /// <summary>The Strength the capacity is taken at: the creation value (100), the least the fighter ever has.</summary>
    public const int CapacityStrength = 100;

    /// <summary>The kit, in grant order: the weapon, the armour, then the pack's lines.</summary>
    public static IReadOnlyList<FighterItem> Items { get; } =
    [
        new(new GearItem(MaceWcid, "Mace", GearKind.Wand, WeenieTypeMelee, ItemTypeMelee, EquipMask.MeleeWeapon, 1, null, 675), FighterRole.Weapon, 260, true, MaceSkillId),
        new(new GearItem(45, "Leather Cap", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.HeadWear, 1, null, 100), FighterRole.Armour, 1100, true),
        new(new GearItem(47, "Leather Coat", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.ChestArmor | EquipMask.AbdomenArmor | EquipMask.UpperArmArmor | EquipMask.LowerArmArmor, 1, null, 810), FighterRole.Armour, 150, true),
        new(new GearItem(81, "Leather Leggings", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.UpperLegArmor | EquipMask.LowerLegArmor, 1, null, 960), FighterRole.Armour, 2400, true),
        new(new GearItem(56, "Leather Gauntlets", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.HandWear, 1, null, 270), FighterRole.Armour, 1100, true),
        new(new GearItem(115, "Leather Boots", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.LowerLegWear | EquipMask.FootWear, 1, null, 420), FighterRole.Armour, 1100, true),
        new(new GearItem(ShortbowWcid, "Shortbow", GearKind.Wand, WeenieTypeLauncher, ItemTypeMissile, EquipMask.MissileWeapon, 1, null, 450), FighterRole.Bow, 200, false, BowSkillId),
        new(new GearItem(ArrowWcid, "Arrow", GearKind.Component, WeenieTypeAmmunition, ItemTypeMissile, EquipMask.MissileAmmo, ArrowCount, 1000, 5), FighterRole.Ammo, 1, false),
        new(new GearItem(HealingKitWcid, "Handy Healing Kit", GearKind.Component, WeenieTypeHealer, ItemTypeMisc, EquipMask.None, HealingKitCount, null, 50), FighterRole.HealingKit, 10, false),
        new(new GearItem(PotionWcid, "Potion of Healing", GearKind.Component, WeenieTypeFood, ItemTypeMisc, EquipMask.None, PotionCount, 100, 15), FighterRole.Potion, 170, false),
    ];

    /// <summary>The line for <paramref name="wcid"/>, or null.</summary>
    public static FighterItem? ItemOf(uint wcid) => Items.FirstOrDefault(i => i.Wcid == wcid);

    /// <summary>The lines wielded or worn at once.</summary>
    public static IEnumerable<FighterItem> Worn => Items.Where(i => i.Wield);

    /// <summary>
    /// How many of the kit's leather armour lines (<see cref="FighterRole.Armour"/>) are among <paramref name="wornWcids"/>, one per line (M4 2.1-R1).
    /// The one rule behind <c>worn=</c> in the build scenario and in the corpse test, so the two cannot drift: a worn item that is not an armour line of the kit (the Mace, anything else) is not counted.
    /// </summary>
    public static int CountWornArmour(IEnumerable<uint> wornWcids)
    {
        var set = new HashSet<uint>(wornWcids);
        return Worn.Count(i => i.Role == FighterRole.Armour && set.Contains(i.Wcid));
    }

    /// <summary>The lines kept in the pack.</summary>
    public static IEnumerable<FighterItem> Pack => Items.Where(i => !i.Wield);

    /// <summary>The burden of the whole kit (the sum of the lines; <see cref="Burden"/> is its pinned value).</summary>
    public static int TotalBurden => Items.Sum(i => i.Item.Burden);

    /// <summary>The capacity at <paramref name="strength"/> (ACE's 150 x Strength, no augmentations).</summary>
    public static int Capacity(int strength) => GearKit.EncumbranceCapacity(strength);
}
