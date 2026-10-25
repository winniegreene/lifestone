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

    /// <summary>A held caster kept in the pack for the buff pass and the keeper's casts (M4 P5a C1b): the fighters have no caster wielded, and ACE refuses the magic combat mode without one.</summary>
    Caster,

    /// <summary>A spell component the profile's spells burn, a stack in the pack (M4 P5a fix F-abil; <see cref="FighterComponents"/>).</summary>
    Component,

    /// <summary>A piece of jewelry (a necklace, a ring or a bracelet), worn (M4 P5a C6, <see cref="FighterGearSet"/>).</summary>
    Jewelry,

    /// <summary>A Mana Potion or a Stamina Potion (Food, +25 of the vital), a stack kept in the side packs and drunk by the vitals keeper (M4 P5a C7: the fighters do not regain mana efficiently by spell; <see cref="FighterKit.ManaPotionWcid"/>, <see cref="FighterKit.StaminaPotionWcid"/>).</summary>
    VitalPotion,

    /// <summary>A side pack (a retail container weenie) the build grants and fills with the spell components, so the main pack keeps its slots free (M4 P5a C6, D41).</summary>
    SidePack,

    /// <summary>A charged mana stone (a retail Mana Charge) the gear's recharge uses on the player (<see cref="FighterKit.ManaChargeWcid"/>, <see cref="GearRecharge"/>; closes <c>TODO(m4-gear-mana)</c>).</summary>
    ManaCharge,
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
/// The fighter's gear (M4 item 1.1, plan D14, spec section 5.10): what the <c>item</c> verb gives a fighter, as the weenies ACE spawns. One instance per
/// <see cref="FighterTemplate"/> (M4 P4, plan D31): <see cref="Hunter"/> is "Holtburg Hunter"'s (every number below is its own), <see cref="Melee"/> is
/// "Holtburg Brawler"'s (the same, without the Shortbow and the Arrows) and <see cref="Archer"/> is "Holtburg Archer"'s (without the Mace, with
/// <see cref="ArcherArrowCount"/> Arrows). The constants (wcids, ids, counts) are shared by all three.
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
public sealed class FighterKit
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

    /// <summary>
    /// The Archer's launcher (M4 P4 arch): Longbow 31092, DamageMod 2.0 and no wield requirement (the lead's read-only SELECT of ACE's world data: PropertyFloat 63 DamageMod,
    /// PropertyInt 160 WieldDifficulty; the other Longbows 31093-31096 need 250-290, Longbow 306 is 1.5, the Shortbow 1.5). Missile Weapons (47).
    /// </summary>
    public const uint LongbowWcid = 31092;

    /// <summary>
    /// The Archer's ammunition (M4 P4 arch): Greater Frost Arrow 5307, Damage 14, cold (PropertyInt 44 Damage, 45 DamageType 8; the Arrow 300 is 9 pierce). The lead's SELECT.
    /// Its burden 5, Value 11 and stack limit 1,000, and the Longbow 31092's burden 100 and Value 0, are ACE's (the lead's read-only SELECT of PropertyInt 5 EncumbranceVal, 19 Value,
    /// 11 MaxStackSize, 2026-10-23; closes TODO(m4-frost-arrow-burden)).
    /// </summary>
    public const uint GreaterFrostArrowWcid = 5307;

    /// <summary>
    /// The side pack the kits carry (M4 P5a C6, D41): weenie 136 "Pack" (ace_world: Container, ItemType 0x200, ItemsCapacity 24, EncumbranceVal 15, Value 65, not bonded, no requirement; the same family as 137 Basket, 138 Belt Pouch, 139 Small Belt Pouch, 166 Sack).
    /// The live round 2 found the 40 component stacks filling the main pack (102 items: <c>Container.cs:527</c>, ACE 95ef426d), so loot, the vendor's bought kit and the caster swap's unwield of the weapon were refused; the build moves the component stacks into the side packs.
    /// A side pack is a container slot of the player (ContainersCapacity), not one of the main pack's 102 item slots, and holds <see cref="SidePackSlots"/> items.
    /// </summary>
    public const uint SidePackWcid = 136;

    /// <summary>The items a side pack holds (ace_world ItemsCapacity of weenie 136).</summary>
    public const int SidePackSlots = 24;

    /// <summary>
    /// The side packs a kit carries beyond the ones its component stacks fill (M4 P5a fix r4): one, for a top-up stack on its way to its line's stack (the item tool creates a new stack, which the build then merges) and anything ACE places in a side pack.
    /// Not retail: Lifestone's own.
    /// </summary>
    public const int SidePackSpare = 1;

    /// <summary>
    /// The side packs a kit with <paramref name="componentSlots"/> component stacks carries (M4 P5a fix r4): enough packs of <see cref="SidePackSlots"/> for one stack per line, plus <see cref="SidePackSpare"/>.
    /// Round 4 found the fixed two (and the live character's three) full once the utilities' lines (r3: 44 for the Brawler, 45 for the Archer) and every rerun's unmerged top-up stacks were in them: both fighters now carry 2 + 1 = 3.
    /// </summary>
    /// <param name="componentSlots">The component lines (<see cref="ComponentSlots"/>).</param>
    public static int SidePacksFor(int componentSlots) => (Math.Max(0, componentSlots) + SidePackSlots - 1) / SidePackSlots + SidePackSpare;

    /// <summary>The side packs of a kit without components (the base kits <see cref="Melee"/> and <see cref="Archer"/>): <see cref="SidePacksFor"/> of the two potion lines (M4 P5a C7), one pack and the spare; <see cref="WithComponents"/> sizes them from the account's lines.</summary>
    public const int SidePackCount = 1 + SidePackSpare;

    /// <summary>The free main-pack slots the build keeps after the kit (M4 P5a C6, D41): the loot, a bought kit and the caster swap's unwield of the weapon each need a slot. The build fails loudly naming the count when it cannot keep them.</summary>
    public const int MainPackHeadroom = 30;

    /// <summary>ACE WeenieType 21 Container and ItemType 0x200.</summary>
    public const uint WeenieTypeContainer = 21, ItemTypeContainer = 0x200;

    /// <summary>The Handy Healing Kit's wcid.</summary>
    public const uint HealingKitWcid = 628;

    /// <summary>The Potion of Healing's wcid.</summary>
    public const uint PotionWcid = 377;

    /// <summary>
    /// The Stamina Potion's and the Mana Potion's wcids (M4 P5a C7; ace_world v0.9.296, the pinned world SQL, read-only: 378 <c>staminapotion</c> and 379 <c>manapotion</c>, Food, ItemType 0x80, BoosterEnum 4 Stamina / 6 Mana, BoostValue 25, MaxStackSize 100,
    /// EncumbranceVal 15, Value 75 / 170, not bonded, no requirement; the same family as the Potion of Healing 377). The dearer tiers (the Elixirs 65, the Tonics 85 to 100) weigh 75 to 150 each against the Potion's 15 and give about 1.1 burden per point against its 0.6,
    /// the Draughts (10) need more drinks than a gate has time for; the plain Potion is the one the character's Potion of Healing already is.
    /// </summary>
    public const uint StaminaPotionWcid = 378, ManaPotionWcid = 379;

    /// <summary>
    /// The Mana Potions the Brawler's kit holds (M4 P5a C7): 100 (one full stack of the weenie's limit), 2,500 mana, 1,500 burden. A gate's need at round 5's numbers (the Archer: mana 291, Harm Self V's ceiling 250 a round): the harm-heal leg 5 potions to reach round 1's 250 from 135 held,
    /// then 4 to 10 for each later round (the keeper tops the pool up to the ceiling before a round, and a round really spends 110 to 250): 15 to 40 for the leg's two to four rounds; the buff pass 0 to 25 (twelve level IV lines at a ceiling of 50, real cost about 12 with Mana Conversion);
    /// the keeper's stations 3 to 5 each. 100 is more than two legs and two passes, so a gate never reaches the transfer fallback; the build tops it up. Not retail: Lifestone's own sizing.
    /// </summary>
    public const int ManaPotionCount = 100;

    /// <summary>The Stamina Potions the Brawler's kit holds (M4 P5a C7): 50, 1,250 stamina, 750 burden: a restore from the 30% trigger to the 80% share of 240 is 120 stamina, 5 potions; 50 is ten of them (the melee fights drain stamina). Not retail: Lifestone's own sizing.</summary>
    public const int StaminaPotionCount = 50;

    /// <summary>
    /// The Mana Potions and Stamina Potions the Archer's kit holds (M4 P5a C7): 60 (900 burden, 1,500 mana, five pools) and 10 (150 burden, 250 stamina, one pool). The Archer's capacity is Strength 97 x 150 = 14,550 against a kit with components of 11,412 and the 2,000 the loot needs
    /// (<c>FighterComponentsDatTests</c>), which leaves about 1,100: 60 + 10 is 1,050. Not retail: Lifestone's own sizing.
    /// </summary>
    public const int ArcherManaPotionCount = 60, ArcherStaminaPotionCount = 10;

    /// <summary>What one Mana or Stamina Potion restores (BoostValue, PropertyInt 90, 25).</summary>
    public const int VitalPotionBoost = 25;

    /// <summary>ACE WeenieType 37 ManaStone and ItemType 0x80000 ManaStone.</summary>
    public const uint WeenieTypeManaStone = 37, ItemTypeManaStone = 0x80000;

    /// <summary>
    /// The gear's mana stone (closes <c>TODO(m4-gear-mana)</c>): weenie 9060 "Titan Mana Charge" (<c>manachargetitan</c>; ace_world v0.9.296, read-only SELECT 2026-10-24: WeenieType 37 ManaStone, ItemType 0x80000, ItemCurMana 5,000, ItemEfficiency 1,
    /// ManaStoneDestroyChance 1 (a use always consumes it), EncumbranceVal 50, Value 9,000, no stack, TargetType 0x891F (Creature among its targets, so a use on the player is allowed: <c>Player_Use.cs:138-144</c>, ACE 95ef426d), not bonded; sold by 19 vendors).
    /// The retail charges are Tiny 100, Small 175, Moderate 350, High 500, Great 1,000, Superb 2,000, Titan 5,000 and Massive 10,000, all of burden 50. One Titan refills the gear when the recharge's trigger fires (<see cref="GearRecharge.RechargeBelow"/>):
    /// the Brawler's 13 pieces burn about 1,516 mana an hour and the first (Branith's Shirt, 500 at 0.025 a second) is at half after 2.8 hours, about 4,200 short in all; the Archer's 12 worn pieces burn about 1,354 an hour and its first (the Ancient Armored
    /// Vestment, 800 at 0.033) is at half after 3.4 hours, about 4,600 short. The Massive would double that for the same burden but wastes what the gear cannot take (a used charge is consumed whatever is left).
    /// </summary>
    public const uint ManaChargeWcid = 9060;

    /// <summary>The mana one <see cref="ManaChargeWcid"/> holds (PropertyInt 107).</summary>
    public const int ManaChargeMana = 5_000;

    /// <summary>
    /// The charges a kit holds: one. The recharge uses at most what the kit holds, and the next build tops a used one up (the kit's grant, as the potions), so one is a recharge per build-verify, about three hours of the gear's burn: a gate burns well under one.
    /// The Archer's burden has little headroom (<c>FighterComponentsDatTests</c>: the kit with its components and the 2,000 the loot needs against Strength 97's 14,550; its arrows gave 100 for this charge, <see cref="ArcherArrowCount"/>), so a second charge is not carried. Not retail: Lifestone's own sizing.
    /// </summary>
    public const int ManaChargeCount = 1;

    /// <summary>The arrows the Hunter's kit gives.</summary>
    public const int ArrowCount = 200;

    /// <summary>
    /// The Greater Frost Arrows the Archer's kit gives: 230 (one stack). A gate's missile hunts and the tests need a few dozen: the live Archer shot 17 arrows for 2 kills with the Arrows
    /// (9 damage), and the Greater Frost Arrow does 14 and the Longbow doubles instead of 1.5 times, so a kill is fewer shots. 230 is inside the weenie's stack limit of 1,000, and 230 x 5 burden = 1,150. It was 500 Arrows for the Shortbow, then 250 (P4 arch);
    /// the gear's mana charge (<see cref="ManaChargeWcid"/>, burden 50; closes <c>TODO(m4-gear-mana)</c>) took 20 of them: after P5b's Lifestone Tie components the Archer's kit with its components and the 2,000 the loot needs left 1 of Strength 97's 14,550
    /// (<c>FighterComponentsDatTests</c>), so the charge needed room, and 20 arrows (100) leave 51. The build tops the stack up every build-verify. Not retail: Lifestone's own sizing.
    /// </summary>
    public const int ArcherArrowCount = 230;

    /// <summary>The kits the kit gives (separate items).</summary>
    public const int HealingKitCount = 2;

    /// <summary>The potions the kit gives.</summary>
    public const int PotionCount = 5;

    /// <summary>The Hunter's kit's total burden (675 + 450 + 1,000 + 2,560 + 100 + 75), checked by the tests against the sum of the lines (<see cref="Hunter"/>).</summary>
    public const int Burden = 4_860;

    /// <summary>The Strength the capacity is taken at: the creation value (100), the least the fighter ever has.</summary>
    public const int CapacityStrength = 100;

    private FighterKit(string key, IReadOnlyList<FighterItem> items, FighterGearSet? gear = null)
    {
        Key = key;
        Items = items;
        Gear = gear;
    }

    private static FighterItem Mace { get; } = new(new GearItem(MaceWcid, "Mace", GearKind.Wand, WeenieTypeMelee, ItemTypeMelee, EquipMask.MeleeWeapon, 1, null, 675), FighterRole.Weapon, 260, true, MaceSkillId);

    private static FighterItem Shortbow { get; } = new(new GearItem(ShortbowWcid, "Shortbow", GearKind.Wand, WeenieTypeLauncher, ItemTypeMissile, EquipMask.MissileWeapon, 1, null, 450), FighterRole.Bow, 200, false, BowSkillId);

    private static FighterItem Longbow { get; } = new(new GearItem(LongbowWcid, "Longbow", GearKind.Wand, WeenieTypeLauncher, ItemTypeMissile, EquipMask.MissileWeapon, 1, null, 100), FighterRole.Bow, 0, false, BowSkillId);

    private static FighterItem HealingKit { get; } = new(new GearItem(HealingKitWcid, "Handy Healing Kit", GearKind.Component, WeenieTypeHealer, ItemTypeMisc, EquipMask.None, HealingKitCount, null, 50), FighterRole.HealingKit, 10, false);

    /// <summary>
    /// The fighters' caster (M4 P5a C1b): the mage's wand weenie, <see cref="GearKit.Wand"/> 8552 "The Healer's Heart" (a Caster, ItemType 0x8000, held in the hand, burden 50, no wield requirement), granted into the pack and never wielded by the build.
    /// ACE puts a player in the magic mode only with a wielded caster (<c>Player_Combat.cs:872-880</c>, <c>Creature_Equipment.cs:141-144</c>, ACE 95ef426d), and a Held item needs the main hand free (<c>CheckWeaponCollision</c>, <c>Player_Inventory.cs:1979-1997</c>, ACE 95ef426d), so
    /// <see cref="Lifestone.Client.Session.Fighter.CasterSwap"/> wields it for a pass and puts the weapon back. Its Value is not read: <c>TODO(m4-caster-value)</c> (0 here; nothing reads it).
    /// </summary>
    private static FighterItem Caster { get; } = new(new GearItem(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand.WeenieType, GearKit.Wand.ItemType, GearKit.Wand.ValidLocations, 1, null, GearKit.Wand.Burden), FighterRole.Caster, 0, false);

    private static FighterItem SidePack { get; } = new(new GearItem(SidePackWcid, "Pack", GearKind.Component, WeenieTypeContainer, ItemTypeContainer, EquipMask.None, SidePackCount, null, 15), FighterRole.SidePack, 65, false);

    private static FighterItem ManaPotions(int count) => new(new GearItem(ManaPotionWcid, "Mana Potion", GearKind.Component, WeenieTypeFood, ItemTypeMisc, EquipMask.None, count, 100, 15), FighterRole.VitalPotion, 170, false);

    private static FighterItem StaminaPotions(int count) => new(new GearItem(StaminaPotionWcid, "Stamina Potion", GearKind.Component, WeenieTypeFood, ItemTypeMisc, EquipMask.None, count, 100, 15), FighterRole.VitalPotion, 75, false);

    private static FighterItem Potion { get; } = new(new GearItem(PotionWcid, "Potion of Healing", GearKind.Component, WeenieTypeFood, ItemTypeMisc, EquipMask.None, PotionCount, 100, 15), FighterRole.Potion, 170, false);

    private static FighterItem ManaCharge { get; } = new(new GearItem(ManaChargeWcid, "Titan Mana Charge", GearKind.Component, WeenieTypeManaStone, ItemTypeManaStone, EquipMask.None, ManaChargeCount, null, 50), FighterRole.ManaCharge, 9_000, false);

    private static FighterItem[] Leather { get; } =
    [
        new(new GearItem(45, "Leather Cap", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.HeadWear, 1, null, 100), FighterRole.Armour, 1100, true),
        new(new GearItem(47, "Leather Coat", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.ChestArmor | EquipMask.AbdomenArmor | EquipMask.UpperArmArmor | EquipMask.LowerArmArmor, 1, null, 810), FighterRole.Armour, 150, true),
        new(new GearItem(81, "Leather Leggings", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.UpperLegArmor | EquipMask.LowerLegArmor, 1, null, 960), FighterRole.Armour, 2400, true),
        new(new GearItem(56, "Leather Gauntlets", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.HandWear, 1, null, 270), FighterRole.Armour, 1100, true),
        new(new GearItem(115, "Leather Boots", GearKind.Armour, WeenieTypeClothing, ItemTypeArmor, EquipMask.LowerLegWear | EquipMask.FootWear, 1, null, 420), FighterRole.Armour, 1100, true),
    ];

    private static FighterItem Arrows(int count) => new(new GearItem(ArrowWcid, "Arrow", GearKind.Component, WeenieTypeAmmunition, ItemTypeMissile, EquipMask.MissileAmmo, count, 1000, 5), FighterRole.Ammo, 1, false);

    private static FighterItem GreaterFrostArrows(int count) => new(new GearItem(GreaterFrostArrowWcid, "Greater Frost Arrow", GearKind.Component, WeenieTypeAmmunition, ItemTypeMissile, EquipMask.MissileAmmo, count, 1000, 5), FighterRole.Ammo, 11, false);

    /// <summary>"Holtburg Hunter"'s kit: the Mace, the leather set, the Shortbow, 200 Arrows, two kits, five potions.</summary>
    public static FighterKit Hunter { get; } = new("hunter", [Mace, .. Leather, Shortbow, Arrows(ArrowCount), HealingKit, Potion]);

    /// <summary>
    /// "Holtburg Brawler"'s kit: the Hunter's without the Shortbow and the Arrows (no missile weapon at all), with the caster kept in the pack (M4 P5a C1b). M4 P5a C6 (D40): the magic gear (<see cref="FighterGearSet.Brawler"/>: a magic melee weapon, mismatched magic armour and jewelry)
    /// replaces the plain Mace and the leather set, which are not in the kit any more (a character built before keeps them: the build takes a worn plain piece off when the gear needs its place, and the sale rule never sells the Mace).
    /// </summary>
    public static FighterKit Melee { get; } = new("melee", [.. FighterGearSet.Brawler.KitLines(), HealingKit, Potion, Caster, SidePack, ManaPotions(ManaPotionCount), StaminaPotions(StaminaPotionCount), ManaCharge], FighterGearSet.Brawler);

    /// <summary>
    /// "Holtburg Archer"'s kit (M4 P4 arch): the leather set, the <see cref="LongbowWcid"/> Longbow instead of the Shortbow, <see cref="ArcherArrowCount"/> Greater Frost Arrows
    /// (<see cref="GreaterFrostArrowWcid"/>) instead of the Arrows, the kits, the potions and the caster kept in the pack (M4 P5a C1b); no melee weapon at all. M4 P5a C6 (D40): the magic gear (<see cref="FighterGearSet.Archer"/>: a magic bow kept in the pack, mismatched
    /// magic armour and jewelry) replaces the plain Longbow and the leather set (an older Longbow in a built character's pack stays there as a spare; the hunt's swap wields the kit's bow by its wcid).
    /// </summary>
    public static FighterKit Archer { get; } = new("archer", [.. FighterGearSet.Archer.KitLines(), GreaterFrostArrows(ArcherArrowCount), HealingKit, Potion, Caster, SidePack, ManaPotions(ArcherManaPotionCount), StaminaPotions(ArcherStaminaPotionCount), ManaCharge], FighterGearSet.Archer);

    /// <summary>The template's key (<c>hunter</c>, <c>melee</c>, <c>archer</c>).</summary>
    public string Key { get; }

    /// <summary>The kit, in grant order: the weapon, the armour, then the pack's lines.</summary>
    public IReadOnlyList<FighterItem> Items { get; }

    /// <summary>The magic gear the kit carries (M4 P5a C6), or null (the Hunter's plain kit).</summary>
    public FighterGearSet? Gear { get; }

    /// <summary>The wcid of the kit's wielded melee weapon (the Hunter's and old Brawler's Mace 331, the Brawler's gear weapon), or 0 when the kit has none.</summary>
    public uint WeaponWcid => Items.FirstOrDefault(i => i.Role == FighterRole.Weapon)?.Wcid ?? 0;

    /// <summary>The kit's lines worn on the body: armour and jewelry (<see cref="FighterRole.Armour"/>, <see cref="FighterRole.Jewelry"/>).</summary>
    public IEnumerable<FighterItem> GearLines => Items.Where(i => i.Role is FighterRole.Armour or FighterRole.Jewelry);

    /// <summary>The line for <paramref name="wcid"/>, or null.</summary>
    public FighterItem? ItemOf(uint wcid) => Items.FirstOrDefault(i => i.Wcid == wcid);

    /// <summary>True when the kit holds a melee weapon (the Mace).</summary>
    public bool HasMelee => Items.Any(i => i.Role == FighterRole.Weapon);

    /// <summary>True when the kit holds a missile launcher (the Shortbow, the Archer's Longbow).</summary>
    public bool HasBow => Items.Any(i => i.Role == FighterRole.Bow);

    /// <summary>True when the kit holds a caster in the pack (the Brawler's and the Archer's; the Hunter has no magic and no caster).</summary>
    public bool HasCaster => Items.Any(i => i.Role == FighterRole.Caster);

    /// <summary>The wcid of the kit's caster, or 0 when the kit has none.</summary>
    public uint CasterWcid => Items.FirstOrDefault(i => i.Role == FighterRole.Caster)?.Wcid ?? 0;

    /// <summary>The wcid of the kit's launcher (the Hunter's Shortbow 307, the Archer's Longbow 31092), or 0 when the kit has none.</summary>
    public uint LauncherWcid => Items.FirstOrDefault(i => i.Role == FighterRole.Bow)?.Wcid ?? 0;

    /// <summary>The wcid of the kit's ammunition (the Hunter's Arrow 300, the Archer's Greater Frost Arrow 5307), or 0 when the kit has none.</summary>
    public uint AmmoWcid => Items.FirstOrDefault(i => i.Role == FighterRole.Ammo)?.Wcid ?? 0;

    /// <summary>The lines wielded or worn at once.</summary>
    public IEnumerable<FighterItem> Worn => Items.Where(i => i.Wield);

    /// <summary>
    /// How many of the kit's leather armour lines (<see cref="FighterRole.Armour"/>) are among <paramref name="wornWcids"/>, one per line (M4 2.1-R1).
    /// The one rule behind <c>worn=</c> in the build scenario and in the corpse test, so the two cannot drift: a worn item that is not an armour line of the kit (the Mace, anything else) is not counted.
    /// </summary>
    public int CountWornArmour(IEnumerable<uint> wornWcids)
    {
        var set = new HashSet<uint>(wornWcids);
        return Worn.Count(i => i.Role == FighterRole.Armour && set.Contains(i.Wcid));
    }

    /// <summary>The spell component lines (<see cref="FighterRole.Component"/>); none in <see cref="Hunter"/>, <see cref="Melee"/> and <see cref="Archer"/> themselves: <see cref="WithComponents"/> adds them for an account.</summary>
    public IEnumerable<FighterItem> Components => Items.Where(i => i.Role == FighterRole.Component);

    /// <summary>
    /// This kit with <paramref name="components"/> as its component lines (M4 P5a fix F-abil; <see cref="FighterComponents.For"/>), after the pack's other lines: any component line this kit already had is replaced, so the call is idempotent.
    /// This kit is not changed; the kits the hunts and the tests use (<see cref="Melee"/>, <see cref="Archer"/>, <see cref="Hunter"/>) stay what they were.
    /// </summary>
    /// <param name="components">The lines (<see cref="FighterRole.Component"/>).</param>
    public FighterKit WithComponents(IReadOnlyList<FighterItem> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var packs = SidePacksFor(components.Count + Items.Count(i => i.Role == FighterRole.VitalPotion));
        return new FighterKit(Key, [.. Items.Where(i => i.Role != FighterRole.Component).Select(i => i.Role == FighterRole.SidePack ? i with { Item = i.Item with { Stack = packs } } : i), .. components], Gear);
    }

    /// <summary>The side packs this kit carries (its <see cref="FighterRole.SidePack"/> line's count; 0 for the Hunter's kit).</summary>
    public int SidePacks => Items.FirstOrDefault(i => i.Role == FighterRole.SidePack)?.Item.Stack ?? 0;

    /// <summary>The main-pack slots the kit's own lines take once the build has worn the gear and put the components in the side packs: each held item is a slot (a stack is one, the healing kits two), the side packs and the components none.</summary>
    public int MainPackSlots => Items.Where(i => !i.Wield && i.Role is not (FighterRole.Component or FighterRole.SidePack or FighterRole.VitalPotion)).Sum(i => i.Item.MaxStack is null ? i.Item.Stack : 1);

    /// <summary>The kit's lines that live in the side packs, one stack each: the spell components and (M4 P5a C7) the Mana and Stamina Potions.</summary>
    public IEnumerable<FighterItem> SidePackLines => Items.Where(i => i.Role is FighterRole.Component or FighterRole.VitalPotion);

    /// <summary>The Mana and Stamina Potion lines (M4 P5a C7; none in the Hunter's kit).</summary>
    public IEnumerable<FighterItem> VitalPotions => Items.Where(i => i.Role == FighterRole.VitalPotion);

    /// <summary>The wcid of the kit's mana charge (<see cref="ManaChargeWcid"/>; closes <c>TODO(m4-gear-mana)</c>), or 0 when the kit has none (the Hunter's).</summary>
    public uint ChargeWcid => Items.FirstOrDefault(i => i.Role == FighterRole.ManaCharge)?.Wcid ?? 0;

    /// <summary>The charges the kit holds (its <see cref="FighterRole.ManaCharge"/> line's count; 0 for the Hunter's kit).</summary>
    public int ManaCharges => Items.FirstOrDefault(i => i.Role == FighterRole.ManaCharge)?.Item.Stack ?? 0;

    /// <summary>The side-pack slots the kit's side-pack lines need (one per line, as each line is a stack): the component lines and the two potion lines.</summary>
    public int ComponentSlots => SidePackLines.Count();

    /// <summary>The lines kept in the pack.</summary>
    public IEnumerable<FighterItem> Pack => Items.Where(i => !i.Wield);

    /// <summary>The burden of the whole kit (the sum of the lines; <see cref="Burden"/> is the Hunter's pinned value).</summary>
    public int TotalBurden => Items.Sum(i => i.Item.Burden);

    /// <summary>The capacity at <paramref name="strength"/> (ACE's 150 x Strength, no augmentations).</summary>
    public static int Capacity(int strength) => GearKit.EncumbranceCapacity(strength);
}
