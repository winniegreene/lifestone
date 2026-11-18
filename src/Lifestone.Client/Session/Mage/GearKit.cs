using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>What sort of thing a kit item is.</summary>
public enum GearKind
{
    /// <summary>The held caster the mage wields: the kit's wielded line (<see cref="KitSlot.Wielded"/>, the ground's vendor wand: weenie 2472 "Wand" in both dungeon profiles). <see cref="GearKit.Wand"/> (The Healer's Heart) is only the M3 loot kit's single-weenie property and the fighters' caster.</summary>
    Wand,

    /// <summary>A piece of the classic armour the mage wears (<see cref="GearKit.Armour"/>).</summary>
    Armour,

    /// <summary>A spell component a cast burns.</summary>
    Component,

    /// <summary>M5 item 3 m1 (D8): one of the four foci (<see cref="GearKit.MageFociWcids"/>), an item the mage only carries so that ACE casts a school with the foci formula; never worn or wielded.</summary>
    Focus,

    /// <summary>M5 7 G3: a side pack (weenie 136) the mage carries, <see cref="GearItem.Stack"/> of them, each its own item (<see cref="GearItem.MaxStack"/> 1); a container slot, not a main-pack item slot (<see cref="GearKit.Slots"/> leaves it out).</summary>
    Pack,
}

/// <summary>
/// A weenie of the kit as the world database holds it (a read-only SELECT of ACE's world database, 2026-10-16): the class id, name, WeenieType, ValidLocations, armour level and burden
/// of the wand and the five armour pieces. The kit holds items only: <see cref="GearKit"/> never holds a creature (<c>TODO(item-creature)</c>, the mod's <c>item</c> verb, stays open).
/// </summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The weenie's name.</param>
/// <param name="WeenieType">ACE's <c>WeenieType</c>: 35 Caster, 2 Clothing (the armour), 32 SpellComponent.</param>
/// <param name="ItemType">ACE's <c>ItemType</c>: 0x8000 Caster, 0x2 Armor, 0x1000 SpellComponents.</param>
/// <param name="ValidLocations">The <c>EquipMask</c> bits the item can occupy.</param>
/// <param name="ArmorLevel">The armour level (0 for the wand).</param>
/// <param name="Burden">The burden of one item.</param>
/// <param name="Palette">The <c>PaletteTemplate</c> the kit asks the <c>item</c> verb to give the piece (ACE's <c>@ci</c> palette argument), or null for the weenie's own.</param>
/// <param name="Shade">The shade (0 to 1) the kit asks for, or null for the weenie's own.</param>
public sealed record GearWeenie(uint Wcid, string Name, uint WeenieType, uint ItemType, EquipMask ValidLocations, int ArmorLevel, int Burden, int? Palette = null, double? Shade = null);

/// <summary>One line of the kit: a weenie, how many the kit holds (a component's stack; 1 for the wand and a piece) and where it goes.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The weenie's name.</param>
/// <param name="Kind">What it is.</param>
/// <param name="WeenieType">ACE's <c>WeenieType</c> (see <see cref="GearWeenie"/>).</param>
/// <param name="ItemType">ACE's <c>ItemType</c>.</param>
/// <param name="ValidLocations">The <c>EquipMask</c> bits the item can occupy (0 for a component).</param>
/// <param name="Stack">The count the kit wants (1 for the wand and a piece).</param>
/// <param name="MaxStack">The most one stack holds, or null for an item that does not stack.</param>
/// <param name="UnitBurden">The burden of one item (<see cref="GearKit.ComponentBurden"/> for a component).</param>
/// <param name="Palette">The palette the <c>item</c> request asks for (<see cref="GearWeenie.Palette"/>), or null.</param>
/// <param name="Shade">The shade the <c>item</c> request asks for, or null.</param>
public sealed record GearItem(uint Wcid, string Name, GearKind Kind, uint WeenieType, uint ItemType, EquipMask ValidLocations, int Stack, int? MaxStack, int UnitBurden, int? Palette = null, double? Shade = null)
{
    /// <summary>The burden of the whole stack.</summary>
    public int Burden => UnitBurden * Stack;

    /// <summary>True for a line the kit counts by units (a component, a pack): it is whole when the pack holds <see cref="Stack"/> of it; any other line is whole at one.</summary>
    public bool Counted => Kind is GearKind.Component or GearKind.Pack;
}

/// <summary>
/// The BSD mage's gear (M3 1.4 s0; docs/milestones/m3-lights-and-sound.md sections 5.2 and 5.3, plan D9): a held caster (The Healer's Heart), the five pieces of the classic armour and the spell components the profile's spells burn, as the
/// weenies ACE spawns for the <c>item</c> verb. Built once for an account from the SpellTable (<see cref="Build"/>).
/// </summary>
/// <remarks>
/// <para><b>M5 item 3 m1 (D8, D46, D48): the kit is the ground's supplies now.</b> <see cref="Build"/> builds the vendor kit lines, the four foci and the stock lines of a <see cref="HuntingGround"/> (<see cref="KitPlan"/> adds the purse and the load); the paragraphs below describe the M3 loot kit (the Healer's Heart, the classic armour, the account's customised formulas) it replaced, and stay for the constants and the rules the fighters' kit (<see cref="Fighter.FighterComponents"/>) still shares.</para>
/// <para><b>The components.</b> The union of every component of every spell of <see cref="ArenaProfile"/> in <em>the formula the account casts</em>: ACE's <c>SpellFormula.GetPlayerFormula</c> (Entity/SpellFormula.cs:224) asks
/// <c>SpellTable.GetSpellFormula(table, id, player.Session.Account)</c>, which rewrites taper slots 1, 3 and 6 from the hash of the account name (<see cref="SpellFormulaRules.CustomizeForAccount"/>). The hash is over the
/// name as <c>Session.Account</c> holds it, the stored account name, which ACE creates in lower case (<c>AuthenticationHandler.cs:83</c>, <c>loginRequest.Account.ToLower()</c>), so <see cref="Build"/> lower-cases the name it is given
/// (invariant culture) and the case a caller typed does not matter. Every one of the twelve coloured tapers (ids 63 to 74) is in the kit besides, so a wrong customisation shows as a test failure (the pinned list of
/// <c>GearKitTests</c>) and not as a live fizzle. The foci formula is in the kit too: ACE adds the Prismatic Taper (188) to the foci formula only (<c>SpellFormula.cs:378</c>, <c>GetFociFormula</c> 338-381), which a player uses when <c>HasFoci(school)</c>
/// (<c>Player_Spells.cs:458-488</c>) is true: the school's infusion augmentation is above 0, <em>or the school's focus item is in the inventory</em> (<c>FociWCIDs[school]</c>, 485-486: the five items of <see cref="FociWcids"/>; BSD Mage holds
/// the Foci of Strife, 15271, War Magic). The foci formula is the base formula's scarabs, chorizite (111) and 1 to 4 Prismatic Tapers by the spell's power (<see cref="FociFormulaCounts"/>), so the kit always carries those components for every
/// profile spell (the Prismatic Taper, wcid 20631, among them) and a held focus is never a reason to refuse (<c>TODO(m3-kit-foci)</c> is closed). The account name is an argument at run time and is never stored, logged or printed here.</para>
/// <para><b>The stacks.</b> A cast burns each slot of its formula with the chance <c>ComponentLoss × CDM × min(1, Power / skill)</c> (<c>Spell.TryBurnComponents</c>, Entity/Spell.cs:157-189), at most one of each slot per cast,
/// and needs as many of a component in the pack as the formula has slots of it (<c>Player_Magic.cs:1234-1245</c>). The rule, per component: the burn of a spell is its slots of the component times <c>ComponentLoss × CDM</c> (the
/// skill factor taken as 1, the worst case); a buff line costs its worst spell (the level is chosen by cast chance, D10) and the cycle casts every buff line once, so a cycle costs the sum over the buff lines, plus each mana line's worst spell <see cref="ManaCastsPerLine"/> times (<see cref="ManaCastsPerCycle"/>, D24); the war line costs
/// its worst spell per cast, and the loops fight with one war line, so the most of any war line counts. The stack is <see cref="Safety"/> × (<see cref="BuffCycles"/> × the cycle + <see cref="FightCasts"/> × the war cast), rounded up,
/// at least <see cref="MinimumStack"/> and the most slots of the component in one spell, and at most the weenie's maximum stack. A coloured taper no profile formula of the account holds is given <see cref="SpareTaperStack"/>.
/// <see cref="BuffCycles"/> is 3 (the first buff, a re-buff after the 30-minute enchantments expire, a spare) and <see cref="Safety"/> 3 (a fizzled cast burns its components too, and a retry casts one level down).
/// The loops top the kit up with <see cref="PlayerTools.SpawnGear"/> (idempotent) between cycles. The server's <c>item</c> verb makes each grant its own pack item (it never merges, <c>Container.TryAddToInventory</c>, Container.cs:499-560), so
/// a top-up takes a new slot; <see cref="PlayerTools.SpawnGear"/> merges each arrived grant into the held stack of its weenie afterwards (GameAction 0x0054, <see cref="IActionApi.MergeStack"/>), so a top-up leaves one stack per line.</para>
/// <para><b>The burden.</b> The caster and the pieces carry the SELECT's burdens (50; 600, 1,600, 2,288, 919, 540 since M3 4.2 P7; the leather set's 2,560 stays in the pack: <see cref="LeatherSetBurden"/>). A component's burden is 4 for every component weenie of the kit (a read-only SELECT of the world database, 2026-10-16, all 81 weenies; <c>TODO(m3-kit-burdens)</c> closed), so the kit's burden is exact, and the tests keep it well under the capacity of Strength 131
/// (<see cref="EncumbranceCapacity"/>, ACE <c>EncumbranceSystem.EncumbranceCapacity</c>: 150 × Strength with no augmentations).</para>
/// <para><b>The pack.</b> A player's main pack holds <see cref="PackCapacity"/> items (the human weenie's ItemCapacity, 102; each stack is one slot); the kit's slots are its lines, and the tests keep them well under it. A top-up is granted as new items
/// before it is merged, so <see cref="PlayerTools.SpawnGear"/> counts the main pack's items plus the new grants against the capacity first and refuses (<see cref="GearSpawn.GuardRefusal"/>) before the server would.</para>
/// </remarks>
public sealed class GearKit
{
    /// <summary>ACE's <c>WeenieType.Caster</c>.</summary>
    public const uint WeenieTypeCaster = 35;

    /// <summary>ACE's <c>WeenieType.Clothing</c> (the armour).</summary>
    public const uint WeenieTypeClothing = 2;

    /// <summary>ACE's <c>WeenieType.SpellComponent</c>.</summary>
    public const uint WeenieTypeSpellComponent = 32;

    /// <summary>ACE's <c>ItemType.Caster</c>.</summary>
    public const uint ItemTypeCaster = 0x8000;

    /// <summary>ACE's <c>ItemType.Armor</c>.</summary>
    public const uint ItemTypeArmor = 0x2;

    /// <summary>ACE's <c>ItemType.SpellComponents</c>.</summary>
    public const uint ItemTypeSpellComponents = 0x1000;

    /// <summary>How many buff cycles the stacks cover (the first buff, a re-buff, a spare).</summary>
    public const int BuffCycles = 3;

    /// <summary>The margin over the expected burns (fizzled casts burn too; a retry casts again).</summary>
    public const int Safety = 3;

    /// <summary>
    /// How many casts of the two mana lines one buff cycle makes (M3 2.1 s0a, D24): Stamina to Mana Self tries and Revitalize Self casts together, so each line is cast
    /// <see cref="ManaCastsPerLine"/> times. From <c>BuffChainDatTests</c>: the cycle's mana is 1,680 (24 casts of 70) against a maximum of 426, so the cycle needs 1,254 more; a Stamina to Mana Self VI
    /// from a full stamina (275) drains 138 and gives 207 (<see cref="BuffStatMods.Transfer"/>), so ceil(1,254 / 207) = 7 transfers and as many Revitalize casts to refill the stamina,
    /// doubled for the tries that fizzle: (7 + 7) x 2 = 28. This is the <em>worst case</em>: every cast charged the ceiling, no Mana Conversion (<c>BuffChainDatTests</c> and <c>BuffStatModsTests</c> recompute the 28 from that rule).
    /// On the real server ACE's Mana Conversion roll (<c>Creature_Magic.cs</c> <c>CalculateManaUsage</c> 14-56, <c>GetManaCost</c> 58-100) makes a cast cost far less than the ceiling: the live cycle (<c>BuffLoopLiveTests</c>, 2026-10-16, M3 2.1 fix B) made 4 mana casts
    /// (3 transfers, 1 revitalize), and the scripted ACE with the same roll makes 0 to 1, so the kit's 28 is a 7-fold margin kept on purpose (the burden stays under the capacity, which GearKitTests pins).
    /// </summary>
    public const int ManaCastsPerCycle = 28;

    /// <summary>How many casts of each mana line one buff cycle makes (<see cref="ManaCastsPerCycle"/> over the two lines).</summary>
    public const int ManaCastsPerLine = ManaCastsPerCycle / 2;

    /// <summary>How many casts of the war line one stack covers (a few fights).</summary>
    public const int FightCasts = 25;

    /// <summary>The least a component of the union is given.</summary>
    public const int MinimumStack = 5;

    /// <summary>What a coloured taper no profile formula of the account holds is given.</summary>
    public const int SpareTaperStack = 10;

    /// <summary>The least a travel-only component is given (M4 P5b J3b): a tie or a recall burns one slot of it with a small chance, so three cover a journey's cast and its retry.</summary>
    public const int TravelMinimumStack = 3;

    /// <summary>The casts of each travel spell a gate makes (M4 P5b J3b: a journey casts a recall or a tie once, and the kit keeps one retry; a small number on purpose: the mage's burden has little headroom, GearKitTests).</summary>
    public const int TravelCasts = 2;

    /// <summary>The travel spells whose components the kit carries (M4 P5b J3b, W0: the mage lacked 767 for Lifestone Recall and 769, 788, 746 for Lifestone Tie): Lifestone Recall 1635 and Lifestone Tie 2644.</summary>
    public static IReadOnlyList<uint> KitTravelSpells { get; } = [1635, 2644];

    /// <summary>The fight spells beyond the profile's lines whose components the kit carries (M4 P5b J7): Cassius' Ring of Fire 1785, the hero video's cast (the mage lacked 110, 19 and 58).</summary>
    public static IReadOnlyList<uint> KitFightSpells { get; } = [ArenaProfile.RingOfFireId];


    /// <summary>The burden of one component: the SELECT of the world database gives 4 to every component weenie in the kit (the Prismatic Taper, 20631, has 6, <see cref="PrismaticTaperBurden"/>).</summary>
    public const int ComponentBurden = 4;

    /// <summary>The Prismatic Taper's weenie class id (component 188; maximum stack 1,000): the foci formula's taper, in the kit.</summary>
    public const uint PrismaticTaperWcid = 20631;

    /// <summary>The Platinum Scarab (level VII's scarab), whose low stack is the named stop <c>m5-carried-stock</c> at the fight's start (D8); the autopilot's audit queues the buy-trip station for it first when the seller's town is reachable (M6 4 t4b, D44, closes <c>m6-buy-trip-autopilot</c>, once <c>m5-platinum-stock</c>).</summary>
    public const uint PlatinumScarabWcid = 8897;

    /// <summary>The burden of one Prismatic Taper (a read-only SELECT of the world database, 2026-10-16: 6, where every other component of the kit has <see cref="ComponentBurden"/>).</summary>
    public const int PrismaticTaperBurden = 6;

    /// <summary>The strength the capacity is taken at (<see cref="MageTemplate.Targets"/>: 131 ranks; Strength 141 with the creation value, so this is conservative).</summary>
    public const int CapacityStrength = 131;

    /// <summary>The items a player's main pack holds (the human weenie's ItemCapacity; the stand-in's <c>PackCapacity</c>).</summary>
    public const int PackCapacity = 102;

    /// <summary>The side packs a player can carry (the human weenie's ContainersCapacity, 7: ACE Container.cs @95ef426d refuses an eighth with a container-full error; the pack lines of a profile's kit are checked against it).</summary>
    public const int ContainerCapacity = 7;

    /// <summary>
    /// The weenie class ids of ACE's five focus items (<c>Player.FociWCIDs</c>, Player_Spells.cs:451-457: Enchantment 15268, Artifice 15269, Verdancy 15270, Strife 15271, Shadow 43173). A character that holds one casts that school with the foci
    /// formula (<c>HasFoci</c>, 485-486), whose components the kit carries (<see cref="FociFormulaCounts"/>).
    /// </summary>
    public static IReadOnlyList<uint> FociWcids { get; } = [15268, 15269, 15270, 15271, 43173];

    /// <summary>ACE's <c>PaletteTemplate.Gold</c> (ACE.Entity/Enum/PaletteTemplate.cs: the 22nd member, Undef being 0, so 21; the pinned enum, read 2026-10-19).</summary>
    public const int PaletteGold = 21;

    /// <summary>
    /// The held caster (the property keeps its M3 name; the weenie is the owner's D43 choice): weenie 8552, The Healer's Heart (<c>orbhealer</c>), a Caster (WeenieType 35, ItemType 0x8000) held in the hand (ValidLocations 0x01000000), burden 50, no
    /// WieldRequirements (a read-only parse of the pinned world SQL, ACE-World-16PY-Patches v0.9.296, 2026-10-19: the only weenie of that name). It carries three item spells (Regeneration Self VI 170, Healing Mastery Self VI 879, Life Giver 1997; item
    /// spellcraft 225, ItemDifficulty 105, which BSD Mage's Arcane Lore 144 meets) and is <b>not a focus</b>: it is not in <see cref="FociWcids"/>, so ACE's <c>HasFoci</c> does not apply and the casts use the account's customised formulas.
    /// </summary>
    public static GearWeenie Wand { get; } = new(8552, "The Healer's Heart", WeenieTypeCaster, ItemTypeCaster, EquipMask.Held, 0, 50);

    /// <summary>
    /// BSD Mage's classic armour (D43, 2026-10-19), a disjoint set with no wield requirement (the world SQL, 2026-10-19): the olthoi helm 3715, the Greater Amuli Shadow coat 6600 and leggings 6606 (armour level 190, the pre-"new" rows: 7658 and 7689 are 150 with 0.1
    /// elemental protection), and the Platemail Gauntlets 57 and Sollerets 107 in the <see cref="PaletteGold"/> palette (the shade stays the weenie's own). Total burden 5,947 (the leather set's 2,550 stays in the pack after the swap:
    /// <see cref="LeatherSetBurden"/>). The coat covers chest and both arms, the leggings abdomen and both legs.
    /// </summary>
    public static IReadOnlyList<GearWeenie> Armour { get; } =
    [
        new(3715, "Olthoi Helm", WeenieTypeClothing, ItemTypeArmor, EquipMask.HeadWear, 230, 600),
        new(6600, "Greater Amuli Shadow Coat", WeenieTypeClothing, ItemTypeArmor, EquipMask.ChestArmor | EquipMask.UpperArmArmor | EquipMask.LowerArmArmor, 190, 1600),
        new(6606, "Greater Amuli Shadow Leggings", WeenieTypeClothing, ItemTypeArmor, EquipMask.AbdomenArmor | EquipMask.UpperLegArmor | EquipMask.LowerLegArmor, 190, 2288),
        new(57, "Platemail Gauntlets", WeenieTypeClothing, ItemTypeArmor, EquipMask.HandWear, 150, 919, PaletteGold),
        new(107, "Sollerets", WeenieTypeClothing, ItemTypeArmor, EquipMask.FootWear, 150, 540, PaletteGold),
    ];

    /// <summary>The burden of the five leather pieces the kit used before M3 4.2 P7 (cap 100, coat 810, leggings 960, gauntlets 270, boots 420): a mage that wore them keeps them in the pack after the gear scenario swaps, so the burden check counts them.</summary>
    public const int LeatherSetBurden = 2_560;

    /// <summary>The capacity ACE gives <paramref name="strength"/> with no augmentations (<c>EncumbranceSystem.EncumbranceCapacity</c>: 150 × strength).</summary>
    public static int EncumbranceCapacity(int strength) => strength <= 0 ? 0 : 150 * strength;

    private GearKit(IReadOnlyList<GearItem> items, IReadOnlyList<uint> componentIds)
    {
        Items = items;
        CustomisedComponentIds = componentIds;
    }

    /// <summary>Every line: the wand, the armour, then the components in component-id order.</summary>
    public IReadOnlyList<GearItem> Items { get; }

    /// <summary>The component ids the account's formulas hold (the profile's spells, customised for the account), ascending. The twelve coloured tapers are in the kit besides.</summary>
    public IReadOnlyList<uint> CustomisedComponentIds { get; }

    /// <summary>The wand's line.</summary>
    public GearItem WandItem => Items.First(i => i.Kind == GearKind.Wand);

    /// <summary>The components' lines.</summary>
    public IEnumerable<GearItem> Components => Items.Where(i => i.Kind == GearKind.Component);

    /// <summary>The armour's lines.</summary>
    public IEnumerable<GearItem> ArmourItems => Items.Where(i => i.Kind == GearKind.Armour);

    /// <summary>The pack slots the kit takes: one for each line, and one more for each further stack a line holds past its weenie's maximum (M5 7 F3, D87: a stock line may keep more than one stack, <see cref="StackSizes"/>).</summary>
    public int Slots => Items.Where(i => i.Kind != GearKind.Pack).Sum(i => StackSizes(i.Stack, i.MaxStack).Count);

    /// <summary>
    /// M5 7 F3 (D87): how <paramref name="total"/> units are granted as stacks of at most <paramref name="max"/> (one stack when the weenie has no maximum): the full stacks first, then the rest. The server mod clamps one
    /// <c>item</c> request to the weenie's maximum stack (<c>ItemCreateRules.ResolveStack</c>), so a stock line that keeps more than a stack is asked for in several requests.
    /// </summary>
    /// <param name="total">The units wanted.</param>
    /// <param name="max">The weenie's maximum stack, or null.</param>
    internal static IReadOnlyList<int> StackSizes(int total, int? max)
    {
        var stacks = new List<int>();
        for (var left = total; left > 0;)
        {
            var one = Math.Min(left, max ?? left);
            stacks.Add(one);
            left -= one;
        }

        return stacks.Count == 0 ? [Math.Max(total, 0)] : stacks;
    }

    /// <summary>The burden of the whole kit (exact).</summary>
    public int Burden => Items.Sum(i => i.Burden);

    /// <summary>The line for <paramref name="wcid"/>, or null.</summary>
    public GearItem? ItemOf(uint wcid)
    {
        foreach (var item in Items)
        {
            if (item.Wcid == wcid)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// The mage's kit on a hunting ground (M5 item 3 m1, D8, D46, D48): the ground's <c>supplies</c> section as items. The vendor kit lines (the den's: a worn robe and a wielded wand, bought from the ground's home vendor), the four foci of
    /// <see cref="MageFociWcids"/> and one stack of every stock line to its <c>keep</c> (the tapers, the potions and the carried scarabs). Every school's cast then uses the foci formula, so the components of every spell the mage casts
    /// (<see cref="CastBurns"/>) are exactly stock lines: a component that is not one fails the build by name (<c>ground-plan-gap</c>, a profile gap and not a code fix). The M3 loot kit (the Healer's Heart, the classic armour and the 38
    /// component ids of the account's customised formulas) is retired; the foci formula is the same for every account, so the account name is no longer hashed.
    /// </summary>
    /// <param name="spells">The SpellTable's spells (<see cref="SpellTableData.Spells"/>).</param>
    /// <param name="accountName">The account's name; checked, never stored, logged or printed (kept for the callers: the foci formula does not depend on it).</param>
    /// <param name="heroRing">True only for the hero mage's gear station (M4 P5b fix FB, P5b-R12): the cast spells also hold <see cref="KitFightSpells"/> (Cassius' Ring of Fire).</param>
    /// <param name="ground">The ground whose supplies the kit is (the fight's <c>--fight-ground</c>); null: the default ground (<see cref="FightOptions.DefaultGround"/>).</param>
    /// <param name="holdsFocus">M6 3 a2, M6 6 MG (<c>m6-kit-account-formula</c>): whether the character holds the school's focus; given, a school without one burns the account's full formula (<see cref="AccountFormula"/>, hashed from <paramref name="accountName"/>), so a climber with no focus (Winnie on the account <c>test</c>) is kitted with those components. The Client callers that can see the character pass it (the heal station's gear, <c>HealStations.cs</c>; the journey's gear station, <c>JourneyStations.cs</c>, whose kit lists the four foci itself); the legs (<c>BuffLegRunner</c>, <c>FightLegRunner</c>, <c>DeathLegRunner</c>), <c>GearScenario</c>, <see cref="KitPlan"/> and the play-checks stay on the foci formula by design (the den's mage holds every focus; the ladder's dungeon profiles carry both formulas' lines). Null: the foci formula for every school.</param>
    /// <param name="stockOnly">M6 5 (<c>TODO(m6-loopback-burrow-kit)</c> closed): true for a Throwaway loopback's grant on a ground with no vendor kit (the Burrow, the Cave of Alabree): the kit is the default ground's vendor wand (ACE's magic mode needs a wielded caster), the four foci and the stock lines, with no robe; false (every play caller): a ground with no vendor kit fails by name, as before.</param>
    /// <returns>The kit: the wielded line first, then the worn lines, the four foci, the stock lines in the profile's order.</returns>
    /// <exception cref="KeyNotFoundException">A cast spell is not in <paramref name="spells"/>.</exception>
    /// <exception cref="InvalidOperationException">The ground has no kit (unless <paramref name="stockOnly"/>) or no stock; a component of a cast is not a stock line; a line names a weenie this table does not know (<c>TODO(m6-kit-weenie)</c>); a keep passes one stack.</exception>
    /// <param name="withheld">M6 6 live round 2: the profile spells the build does not teach (<see cref="CastBurns"/>); null: none.</param>
    /// <param name="offPlan">M6 7 K: what a Throwaway loopback's fight casts off the ground's plan (<see cref="OffPlanBolts"/>; only with <paramref name="stockOnly"/>); null: nothing.</param>
    public static GearKit Build(IReadOnlyDictionary<uint, SpellInfo> spells, string accountName, bool heroRing = false, HuntingGround? ground = null, Func<MagicSchool, bool>? holdsFocus = null, bool stockOnly = false, IReadOnlyCollection<uint>? withheld = null, OffPlanBolts? offPlan = null)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        var home = ground ?? GroundCatalog.Get(FightOptions.DefaultGround);
        var supplies = home.Supplies;
        if ((supplies.Kit.Count == 0 && !stockOnly) || supplies.Stock.Count == 0)
        {
            throw new InvalidOperationException($"ground-plan-gap: {home.Name} has no vendor kit or no stock lines in its supplies section, so there is no mage kit to build (an outdoor ground shops at the journey's own stations)");
        }

        var burns = CastBurns(home, spells, heroRing, holdsFocus is null ? null : accountName, holdsFocus, keepers: !stockOnly, withheld: withheld);
        var items = new List<GearItem>();
        // M6 5 round 2: ACE enters the magic mode only with a wielded caster (Player_Combat.cs 872-880 @95ef426d), so a stock-only plan carries the default ground's vendor wand (the climber buys its Wand by level 10, item 2).
        var wandHome = stockOnly && !supplies.Kit.Any(k => k.Slot == KitSlot.Wielded) ? GroundCatalog.Get(FightOptions.DefaultGround) : home;
        foreach (var slot in new[] { KitSlot.Wielded, KitSlot.Worn })
        {
            var source = slot == KitSlot.Wielded ? wandHome : home;
            foreach (var line in source.Supplies.Kit.Where(k => k.Slot == slot))
            {
                var w = SupplyWeenieOf(line.Wcid, source, "kit");
                if (slot == KitSlot.Wielded ? w.ItemType != ItemTypeCaster : w.ItemType is not (ItemTypeArmor or ItemTypeClothing))
                {
                    throw new InvalidOperationException($"ground-plan-gap: the kit line {line.Name} ({line.Wcid}) of {home.Name} is {slot} but the weenie's item type is 0x{w.ItemType:X}");
                }

                items.Add(new GearItem(w.Wcid, line.Name, slot == KitSlot.Wielded ? GearKind.Wand : GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.UnitBurden));
            }
        }

        foreach (var line in supplies.Kit.Where(k => k.Slot == KitSlot.Pack))
        {
            var w = SupplyWeenieOf(line.Wcid, home, "kit");
            if ((w.ItemType & Lifestone.Client.Session.Fighter.FighterKit.ItemTypeContainer) == 0)
            {
                throw new InvalidOperationException($"ground-plan-gap: the kit line {line.Name} ({line.Wcid}) of {home.Name} is a pack but the weenie's item type is 0x{w.ItemType:X}");
            }

            items.Add(new GearItem(w.Wcid, line.Name, GearKind.Pack, w.WeenieType, w.ItemType, EquipMask.None, line.Count, 1, w.UnitBurden));
        }

        foreach (var focus in MageFociWcids)
        {
            var w = SupplyWeenieOf(focus, home, "focus");
            items.Add(new GearItem(focus, w.Name, GearKind.Focus, w.WeenieType, w.ItemType, EquipMask.None, 1, null, w.UnitBurden));
        }

        foreach (var line in supplies.Stock)
        {
            var w = SupplyWeenieOf(line.Wcid, home, "stock");
            items.Add(new GearItem(w.Wcid, line.Name, GearKind.Component, w.WeenieType, w.ItemType, EquipMask.None, line.Keep, w.MaxStack, w.UnitBurden));
        }

        var components = burns.Select(b => b.ComponentId).ToList();
        if (offPlan is not null)
        {
            if (!stockOnly)
            {
                throw new InvalidOperationException($"the off-plan bolts are a Throwaway loopback's grant (a stock-only plan): {home.Name}'s mage casts its plan, and play grants nothing");
            }

            foreach (var (component, weenie, count) in offPlan.Lines(spells, home))
            {
                var w = SupplyWeenieOf(weenie, home, "off-plan bolt");
                items.Add(new GearItem(w.Wcid, w.Name, GearKind.Component, w.WeenieType, w.ItemType, EquipMask.None, count, w.MaxStack, w.UnitBurden));
                components.Add(component);
            }
        }

        return new GearKit(items, [.. components.Distinct().Order()]);
    }

    /// <summary>
    /// M6 7 K (round 1c: <c>KillTaskLiveTests</c> built a level-32 Throwaway on the Cave of Alabree, whose plan stops at level 20, so the fight chose Flame Bolt IV with no plan cap and the stock holds no scarab for it): the spells a Throwaway loopback's
    /// fight will cast off the ground's plan, and the casts' worth of each foci-formula component that no stock line holds. Every other kit stays the ground's: <see cref="CastBurns"/> and its <c>ground-plan-gap</c> are not touched, and the play of every
    /// other character grants nothing.
    /// </summary>
    /// <param name="Spells">The spell ids.</param>
    /// <param name="Casts">The casts each line is stocked for (the rig's; capped at the weenie's stack).</param>
    public sealed record OffPlanBolts(IReadOnlyCollection<uint> Spells, int Casts)
    {
        /// <summary>The lines beyond <paramref name="ground"/>'s stock, one for each component, by component id: the component, its weenie and the count.</summary>
        /// <param name="spells">The SpellTable's spells.</param>
        /// <param name="ground">The ground.</param>
        internal IReadOnlyList<(uint Component, uint Wcid, int Count)> Lines(IReadOnlyDictionary<uint, SpellInfo> spells, HuntingGround ground)
        {
            var slots = new SortedDictionary<uint, (uint Wcid, int MaxStack, int Slots)>();
            foreach (var id in Spells)
            {
                var info = spells.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"off-plan spell {id} is not in the SpellTable");
                foreach (var (component, n) in FociFormulaCounts(info.Formula))
                {
                    if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
                    {
                        throw new InvalidOperationException($"off-plan spell {id} ({info.Name}) needs component {component}, which the component map does not hold");
                    }

                    if (ground.Supplies.StockOf(weenie.Wcid) is null)
                    {
                        slots[component] = (weenie.Wcid, weenie.MaxStack, Math.Max(n, slots.TryGetValue(component, out var had) ? had.Slots : 0));
                    }
                }
            }

            return [.. slots.Select(s => (s.Key, s.Value.Wcid, Math.Min(s.Value.Slots * Casts, s.Value.MaxStack)))];
        }
    }

    /// <summary>
    /// The counts read at the fight's start (M5 item 3 m1, D8, D48): the stop, if any, before the first cast. A focus the mage does not hold is <c>m5-mage-foci</c> (its school would cast the full formula, which no stock line covers); every CARRIED stock line
    /// (<see cref="HuntingGround.IsCarried"/>: no home vendor sells it) the kit holds whose count is under its <c>stop_under</c> is <c>m5-carried-stock</c>, named with the vendors' town (the autopilot's audit queues the buy-trip station for it before the fight when the seller's town is reachable: M6 4 t4b, D44); a sold line under its
    /// <c>trip_under</c> is only reported through <paramref name="info"/> (the counters are m4's). Null when the kit has no foci (a test's hand-made kit) or every line holds.
    /// </summary>
    /// <param name="ground">The ground (its supplies, its home town).</param>
    /// <param name="kit">The kit the mage was built with.</param>
    /// <param name="held">What the pack holds, by weenie class id.</param>
    /// <param name="info">Where the trip-line notes go.</param>
    /// <param name="floorOnly">
    /// M6 14 (live 2026-11-17 00:07:58, the Atlan wand's mote hunt in the Crater Caves: "m5-carried-stock: the Diamond Scarab stack is 0, under its stop line 2" ended a level-79 climber's fight on its first frame, with Frost and Shock Wave VI paid
    /// for): the floor-only carried lines (<see cref="ComponentFloor.FloorOnly"/>, D148: only war options burn them) by weenie, each with the options that need it. Such a line under its stop line is no stop: it is named through
    /// <paramref name="info"/> and the fight leaves those options out at their floor (<c>FightScenario.ReadFloors</c>). Null or empty: every carried line stops, as before.
    /// </param>
    /// <param name="capped">
    /// M6 14 cap (live 2026-11-17 05:35:45, ladder53: "m5-carried-stock: the Platinum Scarab stack is 2, under its stop line 3" ended the climber's mote hunt, whose VI levels were paid for): the carried lines the fight has already met by leaving
    /// out the war spells that burn them (<see cref="ShortCarriedLines"/>, the fight's level cap, logged there). They stop nothing here. Null or empty: every short carried line that is not floor-only stops, as before.
    /// </param>
    internal static string? FightStartStop(HuntingGround ground, GearKit kit, IReadOnlyDictionary<uint, int> held, Action<string> info, IReadOnlyDictionary<uint, IReadOnlyList<string>>? floorOnly = null, IReadOnlySet<uint>? capped = null)
    {
        if (kit.ItemOf(MageFociWcids[0]) is null)
        {
            return null;
        }

        foreach (var focus in MageFociWcids)
        {
            if (!held.ContainsKey(focus))
            {
                return string.Create(CultureInfo.InvariantCulture, $"m5-mage-foci: the mage holds no {FociName(focus)} (weenie {focus}); its school would cast the full formula, which the stock does not hold (D8)");
            }
        }

        foreach (var line in ground.Supplies.Stock)
        {
            if (kit.ItemOf(line.Wcid) is null)
            {
                continue;
            }

            var count = held.GetValueOrDefault(line.Wcid);
            if (ground.IsCarried(line.Wcid) && count < line.StopUnder && floorOnly is not null && floorOnly.TryGetValue(line.Wcid, out var options))
            {
                info(string.Create(CultureInfo.InvariantCulture, $"stock: the {line.Name} stack is {count}, under its stop line {line.StopUnder}; {ground.Home.Town} sells none: no stop, only {string.Join(" and ", options)} burn it, so they are left out at its floor and the bolt fights on (D148); no trip buys it, only loot or a trade brings more (TODO(m6-floor-line-seller))"));
                continue;
            }

            if (ground.IsCarried(line.Wcid) && count < line.StopUnder && capped is not null && capped.Contains(line.Wcid))
            {
                continue; // M6 14 cap: the fight casts the levels it can pay above the line; its own line names it
            }

            if (ground.IsCarried(line.Wcid) && count < line.StopUnder)
            {
                const string tag = "m5-carried-stock"; // M6 4 t4b: m6-buy-trip-autopilot is closed (the audit calls the buy-trip); a stop here means it was not reachable or already tried
                return string.Create(CultureInfo.InvariantCulture, $"{tag}: the {line.Name} stack is {count}, under its stop line {line.StopUnder}; {ground.Home.Town} sells none (D8)");
            }

            if (count < line.TripUnder)
            {
                info(string.Create(CultureInfo.InvariantCulture, $"stock: the {line.Name} stack is {count}, under its trip line {line.TripUnder} (a trip is due; the counters are m4's)"));
            }
        }

        return null;
    }

    /// <summary>
    /// M6 14 cap: the carried stock lines (<see cref="HuntingGround.IsCarried"/>) of the kit that the pack holds under their stop lines and that are not floor-only (<paramref name="floorOnly"/>), each with the count held, in the profile's order.
    /// Empty when the kit has no foci or a focus is not held (<see cref="FightStartStop"/> names that stop).
    /// </summary>
    /// <param name="ground">The ground (its supplies).</param>
    /// <param name="kit">The kit the mage was built with.</param>
    /// <param name="held">What the pack holds, by weenie class id.</param>
    /// <param name="floorOnly">The floor-only lines, left out (they leave their options out at their floor, D148).</param>
    internal static IReadOnlyList<(StockLine Line, int Held)> ShortCarriedLines(HuntingGround ground, GearKit kit, IReadOnlyDictionary<uint, int> held, IReadOnlyDictionary<uint, IReadOnlyList<string>>? floorOnly)
    {
        if (kit.ItemOf(MageFociWcids[0]) is null || MageFociWcids.Any(f => !held.ContainsKey(f)))
        {
            return [];
        }

        var list = new List<(StockLine, int)>();
        foreach (var line in ground.Supplies.Stock)
        {
            var count = held.GetValueOrDefault(line.Wcid);
            if (kit.ItemOf(line.Wcid) is not null && ground.IsCarried(line.Wcid) && count < line.StopUnder && (floorOnly is null || !floorOnly.ContainsKey(line.Wcid)))
            {
                list.Add((line, count));
            }
        }

        return list;
    }

    /// <summary>The weenie class ids of the four foci the mage holds (D8): Enchantment 15268 (Creature), Artifice 15269 (Item), Verdancy 15270 (Life) and Strife 15271 (War). Shadow (Void) is not cast.</summary>
    public static IReadOnlyList<uint> MageFociWcids { get; } = [15268, 15269, 15270, 15271];

    /// <summary>The mage's keeper-cast spells outside the profile lines: Heal Self VII (Adja's Intervention, the last of <c>VitalsMeans.HealSelf</c>), which the vitals keeper casts for health.</summary>
    public static IReadOnlyList<uint> KitKeeperSpells { get; } = [2073];

    /// <summary>The coin weenie (pyreals) the purse is granted in (the pinned world SQL v0.9.296: WeenieType Coin, MaxStackSize 25,000).</summary>
    public const uint PyrealWcid = 273;

    /// <summary>ACE's <c>ItemType.Clothing</c> (0x4): a robe.</summary>
    public const uint ItemTypeClothing = 0x4;

    /// <summary>One component one spell's cast burns on the foci formula, as a stock line must hold it.</summary>
    /// <param name="SpellId">The spell.</param>
    /// <param name="Spell">The spell's name.</param>
    /// <param name="ComponentId">The component's id in the SpellComponentTable.</param>
    /// <param name="Wcid">The component's weenie class id.</param>
    /// <param name="Name">The component's name.</param>
    /// <param name="PerCast">How many of it one cast needs in the pack.</param>
    public sealed record CastBurn(uint SpellId, string Spell, uint ComponentId, uint Wcid, string Name, int PerCast);

    /// <summary>M5 7 F16 (D91, the owner's components rule): Drain Health Other I (id 1237), which the fight's emergency drain always casts, so its Lead Scarab and tapers are in the kit although the spell plan casts no level under VI.</summary>
    internal const uint EmergencyDrainSpellId = 1237;

    /// <summary>The Lead Scarab (wcid 691), the level I scarab: a ground whose supplies hold this line casts the emergency drain and the kit carries its components; a ground without it names the gap at the fight (TODO(f16-drain-one-supplies)).</summary>
    internal const uint EmergencyDrainScarabWcid = 691;

    /// <summary>
    /// The spells the mage casts on <paramref name="ground"/> and the components each burns on the foci formula (<see cref="FociFormulaCounts"/>): every profile line the cycle, the keeper and the fight cast (the buffs, the
    /// two mana lines, the war bolt) and the two travel lines (<see cref="KitTravelSpells"/>), the ring with <paramref name="heroRing"/> (<see cref="KitFightSpells"/>) and <see cref="KitKeeperSpells"/>; of a line of
    /// seven or more levels, the levels the ground's spell plan casts (D26, D41: the plan's lowest to its highest <c>spell_level</c>, the den's VI and VII; the Incantation and the levels below VI are cast by no plan and
    /// need scarabs no stock line holds), of a shorter line every spell. A component that is no stock line of the ground, or a stock line smaller than one cast needs, fails by name (<c>ground-plan-gap</c>).
    /// M6 item 14 (m6-goldscarab): a component the ground buys in another town (<see cref="HuntingGround.TownStockOf"/>: the Gold Scarab of a level V plan row, bought at Uziz on the way out) is no burn of the kit: no stock line holds it, a
    /// build grants none, and the level floor casts the level under it until the stop has bought some (<see cref="LevelFloor"/>).
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="heroRing">True: the Ring of Fire is cast.</param>
    /// <param name="accountName">M6 3 a2: the account whose full formula a school without its focus burns (with <paramref name="holdsFocus"/>); null: the foci formula for every school.</param>
    /// <param name="holdsFocus">M6 3 a2: whether the character holds the school's focus; null: the foci formula for every school.</param>
    /// <param name="keepers">False (M6 5, a stock-only plan of a low rung): the den's kit keepers (Adja's Intervention) are not cast, so their scarabs need no stock line.</param>
    /// <param name="withheld">M6 6 live round 2: the profile spells the build does not teach (a Throwaway loopback's <c>withheldSpells</c>, e.g. Lifestone Recall below 16): not cast, so their components need no stock line; null: none.</param>
    internal static IReadOnlyList<CastBurn> CastBurns(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, bool heroRing, string? accountName = null, Func<MagicSchool, bool>? holdsFocus = null, bool keepers = true, IReadOnlyCollection<uint>? withheld = null)
    {
        var low = ground.SpellPlan.Count == 0 ? 1 : ground.SpellPlan.Min(p => p.SpellLevel);
        var high = ground.SpellPlan.Count == 0 ? 7 : ground.SpellPlan.Max(p => p.SpellLevel);
        var cast = new List<(string Line, ProfileSpell Spell)>();
        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras))
        {
            var wanted = line.Role switch
            {
                ProfileRole.WarOption => heroRing && line.Spells.Any(s => KitFightSpells.Contains(s.Id)),
                ProfileRole.Travel => line.Spells.Any(s => KitTravelSpells.Contains(s.Id)),
                ProfileRole.HarmSelf or ProfileRole.HealSelf => false,
                _ => true,
            };
            if (!wanted)
            {
                continue;
            }

            for (var i = 0; i < line.Spells.Count; i++)
            {
                if (line.ByPlay)
                {
                    // M5 7 P7j (D143): of a line learned by play, only the levels the ground's learn list teaches are cast (the levels not learned need no component)
                    if (ground.Learn.Any(l => l.SpellId == line.Spells[i].Id))
                    {
                        cast.Add((line.Name, line.Spells[i]));
                    }

                    continue;
                }

                if (line.Spells.Count < 7 || (i + 1 >= low && i + 1 <= high) || (line.Role == ProfileRole.LifeDrain && line.Spells[i].Id == EmergencyDrainSpellId && ground.Supplies.StockOf(EmergencyDrainScarabWcid) is not null))
                {
                    cast.Add((line.Name, line.Spells[i]));
                }
            }
        }

        foreach (var id in keepers ? KitKeeperSpells : [])
        {
            cast.Add(("Heal Self", new ProfileSpell(7, id, "Adja's Intervention", 300, 0)));
        }

        var burns = new List<CastBurn>();
        foreach (var (line, spell) in cast)
        {
            if (withheld is not null && withheld.Contains(spell.Id))
            {
                continue; // M6 6 live round 2: a spell the build withholds is not cast, so it burns nothing
            }

            var info = spells.TryGetValue(spell.Id, out var found)
                ? found
                : throw new KeyNotFoundException($"profile spell {spell.Id} ({spell.Name}) is not in the SpellTable");
            // M6 3 a2, M6 6 MG: with a focus test and the account, a school whose focus is not held burns the account's full formula (AccountFormula.Counts); else the foci formula, as before.
            var useAccount = !string.IsNullOrEmpty(accountName) && holdsFocus is not null && !holdsFocus(info.School);
            var formulaName = useAccount ? "the account's full formula (no focus of the school held)" : "the foci formula";
            foreach (var (component, n) in useAccount ? AccountFormula.Counts(info, false, accountName) : FociFormulaCounts(info.Formula))
            {
                if (!ComponentWeenies.TryGetByComponent(component, out var weenie))
                {
                    throw new InvalidOperationException($"spell {spell.Id} ({spell.Name}) needs component {component} on {formulaName}, which the component map does not hold");
                }

                if (ground.Supplies.StockOf(weenie.Wcid) is null && ground.TownStockOf(weenie.Wcid) is not null)
                {
                    continue; // M6 item 14 (m6-goldscarab): bought in another town on the way out, never a stock line of the kit
                }

                var stock = ground.Supplies.StockOf(weenie.Wcid)
                    ?? throw new InvalidOperationException($"ground-plan-gap: {line} ({spell.Name}, spell {spell.Id}) burns {weenie.Name} (wcid {weenie.Wcid}) on {formulaName}, which is not a stock line of {ground.Name}; add the line to the profile's supplies");
                if (stock.StopUnder < n)
                {
                    throw new InvalidOperationException($"ground-plan-gap: {spell.Name} (spell {spell.Id}) needs {n} {weenie.Name} a cast but the stock line of {ground.Name} stops under {stock.StopUnder}");
                }

                burns.Add(new CastBurn(spell.Id, spell.Name, component, weenie.Wcid, weenie.Name, n));
            }
        }

        return burns;
    }

    /// <summary>A weenie of the supplies as the pinned world SQL v0.9.296 holds it (a read-only parse of the zip, 2026-10-27): what the build needs to grant, count and weigh it.</summary>
    /// <param name="Wcid">The weenie class id.</param>
    /// <param name="Name">The weenie's name.</param>
    /// <param name="WeenieType">ACE's WeenieType.</param>
    /// <param name="ItemType">ACE's ItemType.</param>
    /// <param name="ValidLocations">The EquipMask bits it can occupy (0: not worn).</param>
    /// <param name="UnitBurden">The burden of one (PropertyInt 5).</param>
    /// <param name="MaxStack">The most one stack holds (PropertyInt 11), or null.</param>
    /// <param name="ItemDifficulty">The Arcane Lore the weenie needs (PropertyInt 109), or 0.</param>
    /// <param name="Value">The weenie's Value (PropertyInt 19): what a vendor's rates multiply (review 3-R17: the vendor wand's value, read).</param>
    public sealed record SupplyWeenie(uint Wcid, string Name, uint WeenieType, uint ItemType, EquipMask ValidLocations, int UnitBurden, int? MaxStack, int ItemDifficulty = 0, int Value = 0);

    /// <summary>
    /// The weenies the profiles' supplies name beyond the spell components (<see cref="ComponentWeenies"/>): the vendor robes and wand, the four foci, the Mana Potion, the pyreal coin and the three trade notes; a profile naming another one fails
    /// by name (<c>TODO(m6-kit-weenie)</c>: read the weenie from the pinned world SQL and add it here). The robes cover chest, abdomen, arms, legs and feet (0x7F00).
    /// <para><b>Sources</b> (review 3-R12; the pinned world SQL ACE-World-16PY-Patches v0.9.296, read-only, 2026-10-27; <c>SupplyWeenieSqlTests</c> re-reads every field of every row against it when the zip is present): each row's
    /// weenie is the <c>weenie</c> table's <c>class_Id</c> (the WeenieType is its <c>type</c>), the ItemType is <c>weenie_properties_int</c> type 1, the burden type 5 (EncumbranceVal), the ValidLocations type 9 (absent: not worn), the
    /// MaxStack type 11 (MaxStackSize), the Arcane Lore type 109 (ItemDifficulty), the Value type 19. 6071 robesuckwaraluvian, 5910 robewargharundim (ValidLocations 32512 = 0x7F00), 2472 wand (ValidLocations 16777216 = 0x01000000,
    /// Held; Value 200, the vendor wand: Cindrue sells it at 270, a 1.35 sell rate, so <c>TODO(m4-caster-value)</c> is read for this wand), 15268-15271 packcreatureessence, packitemessence, packlifeessence, packwaressence (the foci,
    /// ValidLocations 0), 379 manapotion, 273 coinstack, 20630, 2625 and 2626 the 250,000, 10,000 and 50,000 trade notes.</para>
    /// </summary>
    public static IReadOnlyList<SupplyWeenie> SupplyWeenies { get; } =
    [
        new(6071, "Faran War Apprentice Robe", WeenieTypeClothing, ItemTypeClothing, (EquipMask)0x7F00, 200, null, 20, 1120),
        new(5910, "Dho War Master Robe", WeenieTypeClothing, ItemTypeClothing, (EquipMask)0x7F00, 200, null, 158, 8000),
        // M5 7 F13 (D87, the kit: a hooded robe and gloves): 5851 robealuvianhood sets ClothingPriority 98048 = 0x17F00 (upper and lower legs, chest, abdomen, upper and lower arms, HEAD 0x4000, feet) and ValidLocations 32513 = 0x7F01 (the robe's 0x7F00 and HeadWear); 56 gauntletsleather sets ClothingPriority 32768 = 0x8000 (hands), ValidLocations 32 (HandWear), ArmorLevel 130
        new(5851, "Faran Robe with Hood", WeenieTypeClothing, ItemTypeClothing, (EquipMask)0x7F01, 200, null, 0, 50),
        new(56, "Leather Gauntlets", WeenieTypeClothing, ItemTypeArmor, EquipMask.HandWear, 270, null, 0, 1100),
        new(2472, "Wand", WeenieTypeCaster, ItemTypeCaster, EquipMask.Held, 50, null, 0, 200),
        new(15268, "Foci of Enchantment", 1, 0x80, EquipMask.None, 400, null, 0, 500),
        new(15269, "Foci of Artifice", 1, 0x80, EquipMask.None, 400, null, 0, 500),
        new(15270, "Foci of Verdancy", 1, 0x80, EquipMask.None, 400, null, 0, 500),
        new(15271, "Foci of Strife", 1, 0x80, EquipMask.None, 400, null, 0, 500),
        // M5 7 G3: 136 pack (Container 21, ItemType 0x200, burden 15, Value 65, ItemsCapacity 24: the ace_world row FighterKit.SidePackWcid cites); Renald the Elder sells it
        new(136, "Pack", Lifestone.Client.Session.Fighter.FighterKit.WeenieTypeContainer, Lifestone.Client.Session.Fighter.FighterKit.ItemTypeContainer, EquipMask.None, 15, null, 0, 65),
        new(379, "Mana Potion", 18, 0x80, EquipMask.None, 15, 100, 0, 170),
        new(377, "Potion of Healing", 18, 0x80, EquipMask.None, 15, 100, 0, 170), // M6 14 T: weenie type 18 (Food), ItemType 0x80, burden 15, MaxStackSize 100, Value 170 (weenie_properties_int 1, 5, 11, 19: a SELECT on the world copy)
        new(PyrealWcid, "Pyreal", 9, 0x40, EquipMask.None, 0, 25_000, 0, 1),
        new(20630, "Trade Note (250,000)", 51, 0x40000, EquipMask.None, 1, 250, 0, 250_000),
        new(2625, "Trade Note (10,000)", 51, 0x40000, EquipMask.None, 1, 250, 0, 10_000),
        new(2626, "Trade Note (50,000)", 51, 0x40000, EquipMask.None, 1, 250, 0, 50_000),
    ];

    /// <summary>
    /// M6 2 b: the supply weenie of a vendor ware the gear floor reads (<see cref="GearFloor"/>: a caster, a robe), or null when the ware is not one of <see cref="SupplyWeenies"/> (a component, a mana charge, a note: never a caster or a
    /// robe line). Unlike <see cref="SupplyWeenieOf"/> it never throws: the floor looks over every ware of every profile vendor.
    /// </summary>
    /// <param name="wcid">The ware's weenie.</param>
    public static SupplyWeenie? FloorWeenieOf(uint wcid)
    {
        foreach (var w in SupplyWeenies)
        {
            if (w.Wcid == wcid)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>The supply weenie <paramref name="wcid"/>: <see cref="SupplyWeenies"/>, else a spell component; else a named failure.</summary>
    internal static SupplyWeenie SupplyWeenieOf(uint wcid, HuntingGround ground, string what)
    {
        foreach (var w in SupplyWeenies)
        {
            if (w.Wcid == wcid)
            {
                return w;
            }
        }

        if (ComponentWeenies.TryGetByWcid(wcid, out var c))
        {
            return new SupplyWeenie(c.Wcid, c.Name, WeenieTypeSpellComponent, ItemTypeSpellComponents, EquipMask.None, c.Wcid == PrismaticTaperWcid ? PrismaticTaperBurden : ComponentBurden, c.MaxStack);
        }

        throw new InvalidOperationException($"TODO(m6-kit-weenie): the {what} weenie {wcid} of {ground.Name} is in neither the component map nor GearKit.SupplyWeenies; read it from the pinned world SQL and add it");
    }

    /// <summary>
    /// The kit, the purse and the load of a ground's mage in one (M5 item 3 m1, D49): <see cref="For"/> builds the kit, checks that its load (the kit's burden plus the purse's) is under the supplies' burden line and the pack has room, and
    /// names the wielded or worn lines the mage cannot use yet. The build grants it; play grants nothing (the owner's cheat layer, the spec section 5.6).
    /// </summary>
    public sealed class KitPlan
    {
        private KitPlan(HuntingGround ground, GearKit kit, IReadOnlyList<PurseGrant> purse, int load, int capacity)
        {
            Ground = ground;
            Kit = kit;
            Purse = purse;
            Load = load;
            Capacity = capacity;
        }

        /// <summary>The ground whose supplies this is.</summary>
        public HuntingGround Ground { get; }

        /// <summary>M6 5 round 2: a Throwaway loopback's stock-only plan (<see cref="For"/>'s <c>stockOnly</c>): the build makes no tie (a low rung is reached by the Network and on foot, and a level-10 mage's Item Enchantment fizzles Primary Portal Tie).</summary>
        public bool StockOnly { get; private init; }

        /// <summary>The kit (<see cref="Build"/>).</summary>
        public GearKit Kit { get; }

        /// <summary>The purse as grants: the coin in stacks of at most the pyreal stack size, then the notes.</summary>
        public IReadOnlyList<PurseGrant> Purse { get; }

        /// <summary>The burden of the kit and the purse.</summary>
        public int Load { get; }

        /// <summary>The capacity the load is judged against (<see cref="EncumbranceCapacity"/> of <see cref="CapacityStrength"/>).</summary>
        public int Capacity { get; }

        /// <summary>The line <c>load: &lt;burden&gt; of &lt;capacity&gt;</c> the build logs.</summary>
        public string LoadLine => string.Create(CultureInfo.InvariantCulture, $"load: {Load} of {Capacity}");

        /// <summary>The purse's pack slots (a coin stack or a note stack each).</summary>
        public int PurseSlots => Purse.Sum(p => p.Stacks.Count);

        /// <summary>
        /// The plan of the ground: the kit (<see cref="Build"/>), the purse, and the load check: the load must be at most the supplies' <c>burden_trip_percent</c> of the capacity and the kit and purse must fit the pack's slots, else
        /// the build fails by name (<c>ground-plan-gap</c>).
        /// </summary>
        /// <param name="ground">The ground.</param>
        /// <param name="spells">The SpellTable's spells.</param>
        /// <param name="account">The account name (<see cref="Build"/>).</param>
        /// <param name="heroRing">True: the Ring of Fire is cast.</param>
        /// <param name="capacityStrength">M5 7 F3 (D87): the Strength the load is judged at, as <see cref="CapacityStrength"/> is (the plan's Strength ranks, conservative by the creation value): the default is level 126's 131; a build at another level (<c>--mage-level</c>) passes its plan's.</param>
        /// <param name="stockOnly">True: a ground with no vendor kit gives the foci and the stock only (a Throwaway loopback's grant, <see cref="Build"/>).</param>
        /// <param name="withheld">M6 6 live round 2: the profile spells the build withholds (<c>MageBuildScenario</c>'s <c>withheldSpells</c>): not cast, so no component for them (<see cref="CastBurns"/>); null: none.</param>
        /// <param name="offPlan">M6 7 K: what a Throwaway loopback's fight casts off the ground's plan (<see cref="OffPlanBolts"/>; only with <paramref name="stockOnly"/>); null: nothing.</param>
        public static KitPlan For(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, string account, bool heroRing = false, int capacityStrength = CapacityStrength, bool stockOnly = false, IReadOnlyCollection<uint>? withheld = null, OffPlanBolts? offPlan = null)
        {
            ArgumentNullException.ThrowIfNull(ground);
            var kit = GearKit.Build(spells, account, heroRing, ground, stockOnly: stockOnly, withheld: withheld, offPlan: offPlan);
            var purse = new List<PurseGrant>();
            var coin = ground.Supplies.Purse.Coin;
            if (coin > 0)
            {
                var coinWeenie = SupplyWeenieOf(PyrealWcid, ground, "purse coin");
                var stacks = new List<int>();
                for (var left = coin; left > 0;)
                {
                    var one = Math.Min(left, coinWeenie.MaxStack ?? left);
                    stacks.Add(one);
                    left -= one;
                }

                purse.Add(new PurseGrant(PyrealWcid, coinWeenie.Name, stacks, coinWeenie.UnitBurden));
            }

            foreach (var note in ground.Supplies.Purse.Notes)
            {
                var w = SupplyWeenieOf(note.Wcid, ground, "purse note");
                if (w.MaxStack is { } max && note.Count > max)
                {
                    throw new InvalidOperationException($"ground-plan-gap: the purse of {ground.Name} holds {note.Count} {note.Name}, more than one stack ({max})");
                }

                purse.Add(new PurseGrant(note.Wcid, note.Name, [note.Count], w.UnitBurden));
            }

            var capacity = EncumbranceCapacity(capacityStrength);
            var load = kit.Burden + purse.Sum(p => p.Burden);
            var line = (long)capacity * ground.Supplies.BurdenTripPercent / 100;
            var plan = new KitPlan(ground, kit, purse, load, capacity) { StockOnly = stockOnly };
            if (load > line)
            {
                throw new InvalidOperationException($"ground-plan-gap: {plan.LoadLine} is over the supplies' burden line of {ground.Supplies.BurdenTripPercent}% ({line}) for {ground.Name}; the kit and the purse leave no room for loot");
            }

            if (kit.Slots + plan.PurseSlots > PackCapacity)
            {
                throw new InvalidOperationException($"ground-plan-gap: the kit ({kit.Slots} slots) and the purse ({plan.PurseSlots}) of {ground.Name} do not fit the pack's {PackCapacity} slots");
            }

            return plan;
        }

        /// <summary>The Arcane Lore each worn or wielded kit line needs that <paramref name="arcaneLore"/> does not reach, as <c>TODO(m6-tethana-supplies)</c> lines (empty when the mage can use the whole kit).</summary>
        /// <param name="arcaneLore">The mage's Arcane Lore skill (BSD Mage's: 144).</param>
        public IReadOnlyList<string> WieldShortfalls(int arcaneLore)
        {
            var shorts = new List<string>();
            foreach (var line in Ground.Supplies.Kit)
            {
                var w = SupplyWeenieOf(line.Wcid, Ground, "kit");
                if (w.ItemDifficulty > arcaneLore)
                {
                    shorts.Add(string.Create(CultureInfo.InvariantCulture, $"TODO(m6-tethana-supplies): the kit line {line.Name} ({line.Wcid}) of {Ground.Name} needs Arcane Lore {w.ItemDifficulty}, the mage has {arcaneLore}; it cannot be worn or wielded until the mage trains it or the profile picks another"));
                }
            }

            return shorts;
        }
    }

    /// <summary>One line of the purse: <see cref="Stacks"/> items of weenie <see cref="Wcid"/>, each of the given stack size (the coin split by the pyreal stack size, a note line one stack of its count).</summary>
    /// <param name="Wcid">The weenie class id.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Stacks">The stack size of each item to grant.</param>
    /// <param name="UnitBurden">The burden of one unit.</param>
    public sealed record PurseGrant(uint Wcid, string Name, IReadOnlyList<int> Stacks, int UnitBurden)
    {
        /// <summary>The units in all the stacks.</summary>
        public int Total => Stacks.Sum();

        /// <summary>The burden of all the stacks.</summary>
        public int Burden => Total * UnitBurden;
    }

    /// <summary>The name and school of a focus item, for the log line.</summary>
    public static string FociName(uint wcid) => wcid switch
    {
        15268 => "the Foci of Enchantment (Creature Enchantment)",
        15269 => "the Foci of Artifice (Item Enchantment)",
        15270 => "the Foci of Verdancy (Life Magic)",
        15271 => "the Foci of Strife (War Magic)",
        43173 => "the Foci of Shadow (Void Magic)",
        _ => "an unknown focus item",
    };

    /// <summary>
    /// The components of the foci formula of a spell with the base formula <paramref name="baseFormula"/>, and how many of each: ACE <c>SpellFormula.GetFociFormula</c> (SpellFormula.cs:338-381, retail
    /// <c>CSpellBase::InqScarabOnlyFormula</c>): the scarabs and chorizite (111) of the base formula, then 1 to 4 Prismatic Tapers (188) by the spell's power. Retail takes the power of the strongest scarab
    /// (<see cref="SpellFormulaRules.ScarabOnlyFormula"/>); ACE of the first component (SpellFormula.cs:193-208). The two agree for the profile's spells; where they differ the larger count is kept, so the kit never falls short.
    /// </summary>
    internal static Dictionary<uint, int> FociFormulaCounts(IReadOnlyList<uint> baseFormula)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var id in SpellFormulaRules.ScarabOnlyFormula(baseFormula))
        {
            if (id != 0)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        var first = baseFormula.Count > 0 && SpellFormulaRules.IsScarabOnlyComponent(baseFormula[0]) ? SpellFormulaRules.PowerLevelOfComponent(baseFormula[0]) : 0;
        var aceTapers = first switch
        {
            1 => 1,
            2 => 2,
            3 or 4 or 7 => 3,
            5 or 6 or 8 or 9 or 10 => 4,
            _ => 0,
        };
        if (aceTapers > counts.GetValueOrDefault(SpellFormulaRules.PrismaticTaperId))
        {
            counts[SpellFormulaRules.PrismaticTaperId] = aceTapers;
        }

        return counts;
    }

    /// <summary>
    /// M6 item 0 F3 (closes <c>TODO(m6-death-outdoor-kit)</c>): the kit of a character whose profile has none (the climber on an outdoor ground: its gear is what the Academy gave and what it wears, not a vendor plan). No line, no component: a death
    /// scenario over it has no gear stage and no gear to count; what comes back after a death is the worn set's (<see cref="Mage.WornRestorer"/>).
    /// </summary>
    public static GearKit Bare { get; } = new([], []);

    /// <summary>True when <paramref name="ground"/>'s profile lists no vendor kit, outdoor or dungeon (M6 3 F1, 3-R1: the spine's dungeons have none; the character fights with what it holds, as on the outskirts): <see cref="Build"/> refuses it (<c>ground-plan-gap</c>) and <see cref="Bare"/> is its kit through <see cref="BuildFor"/>.</summary>
    /// <remarks>M6 2 b: the gear floor's lines (<see cref="GearFloor"/>: a caster, a robe, a mana charge) join the resupply's buy list beside the kit and never enter <c>supplies.kit</c>, so this stays true for the outskirts with a
    /// floor and the kit-less death keeps <c>rearm=corpse</c> or <c>none</c> (DeathHandler.BeginRearm reads the kit, not the floor; a bought spare in the pack is its <c>rearm=pack</c>).</remarks>
    /// <param name="ground">The ground, or null (the default ground: it has a kit).</param>
    public static bool HasNoKit(HuntingGround? ground) => ground is not null && ground.Supplies.Kit.Count == 0;

    /// <summary>The kit of <paramref name="ground"/>: <see cref="Bare"/> when it has none (<see cref="HasNoKit"/>), else <see cref="Build"/>'s (every other check of Build stands: a ground with a kit but no stock still fails by name).</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="accountName">The account's name.</param>
    /// <param name="ground">The ground, or null for the default ground.</param>
    public static GearKit BuildFor(IReadOnlyDictionary<uint, SpellInfo> spells, string accountName, HuntingGround? ground) =>
        HasNoKit(ground) ? Bare : Build(spells, accountName, ground: ground);

    /// <summary>A kit of the given lines (for tests of <see cref="PlayerTools.SpawnGear"/> that need no SpellTable).</summary>
    internal static GearKit Of(IReadOnlyList<GearItem> items) => new(items, []);

    /// <summary>One line of the kit's report: the numbers a test or a handoff prints.</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"items={Items.Count} components={Items.Count(i => i.Kind == GearKind.Component)} customised_components={CustomisedComponentIds.Count} slots={Slots}/{PackCapacity} burden={Burden}/{EncumbranceCapacity(CapacityStrength)}");

    private static GearItem ItemOf(GearWeenie w) =>
        new(w.Wcid, w.Name, w.Wcid == Wand.Wcid ? GearKind.Wand : GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden, w.Palette, w.Shade);
}

/// <summary>One request <see cref="PlayerTools.SpawnGear"/> made for a kit line: what was asked for, and the handle that tells how it ended.</summary>
/// <param name="Item">The kit line.</param>
/// <param name="Requested">The stack asked for (a component's missing count), or null for the wand and a piece (one item).</param>
/// <param name="Handle">The <c>item</c> request's handle.</param>
public sealed record GearGrant(GearItem Item, int? Requested, ToolHandle<ItemResult> Handle)
{
    /// <summary>How the request ended (<see cref="ItemOutcome.Pending"/> until it did).</summary>
    public ItemOutcome Outcome => ItemOutcomes.Of(Handle);

    /// <summary>The refusal in words (<c>wcid name: UnknownWcid</c>, with the mod's sentence), or null while the request is pending or when it created the item.</summary>
    public string? Refusal => Outcome is ItemOutcome.Pending or ItemOutcome.Created
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{Item.Wcid} {Item.Name}: {Outcome} ({Handle.Outcome?.Detail})");
}

/// <summary>
/// What <see cref="PlayerTools.SpawnGear"/> did: the requests it made for what the character did not yet hold, and how they ended. A kit the character already holds makes no request (<see cref="Grants"/> is empty and
/// <see cref="Arrived"/> is true at once). Polling <see cref="IsCompleted"/>, <see cref="Succeeded"/> and <see cref="Arrived"/> allocates nothing (<see cref="Refusals"/> and <see cref="Describe"/> do, and are for the end).
/// </summary>
/// <remarks>
/// <para><b>Wire order.</b> A grant's 0022 (and the burden's 02CD) reaches the client before the item's CreateObject (ACE's UIQueue before its SmartboxQueue, a bundle apart), and the mod's reply with them, so a completed
/// request does not yet mean the item is there. <see cref="Arrived"/> waits for the world object: for each grant the reply's guid must be a world object the inventory model holds, or (once the grant has been merged into a
/// stack the character held, so the guid is gone) the character must hold at least the kit's count of that weenie.</para>
/// <para><b>The merge.</b> The server's <c>item</c> verb never merges: a component top-up is a new item in a new pack slot (<c>Container.TryAddToInventory</c>, Container.cs:499-560). Once every grant has arrived, the spawn merges
/// each component line's other stacks (the new grant, and any fragment an earlier run left) into the largest held stack of that weenie with <see cref="IActionApi.MergeStack"/> (GameAction 0x0054), one at a time, and
/// <see cref="IsDone"/> waits for it. A merge the server refuses or does not answer fails the spawn (<see cref="Succeeded"/> is false and <see cref="MergeFailure"/> says why). The spawn is driven by the session's tick
/// (<see cref="PlayerTools"/> pumps it), so a caller only polls <see cref="IsDone"/>.</para>
/// </remarks>
public sealed class GearSpawn
{
    private enum MergePhase
    {
        /// <summary>The grants have not all arrived yet.</summary>
        Waiting,

        /// <summary>The merges are being sent, one at a time.</summary>
        Merging,

        /// <summary>No more merges to send or wait for.</summary>
        Finished,
    }

    private readonly record struct MergeStep(uint From, uint To, int Amount);

    private readonly List<uint> _held = [];
    private readonly GameState? _state;
    private readonly ActionApi? _api;
    private readonly ActionSource _source;
    private List<MergeStep>? _plan;
    private ActionHandle? _current;
    private MergePhase _phase;
    private int _next;
    private int _merged;
    private bool _arrived;

    internal GearSpawn(
        GearKit kit,
        GameState? state,
        IReadOnlyList<GearGrant> grants,
        int alreadyHeld,
        string? notStarted,
        string? guardRefusal = null,
        ActionApi? api = null,
        ActionSource source = ActionSource.Host)
    {
        Kit = kit;
        _state = state;
        Grants = grants;
        AlreadyHeld = alreadyHeld;
        NotStarted = notStarted;
        GuardRefusal = guardRefusal;
        _api = api;
        _source = source;
        // Merged even when nothing was granted: a line may hold fragments from an earlier spawn (live, 1.4 round 2: three lines kept two stacks from
        // top-ups made before the merge existed), so every run leaves one stack per component line.
        _phase = notStarted is null && guardRefusal is null && api is not null ? MergePhase.Waiting : MergePhase.Finished;
    }

    /// <summary>The kit asked for.</summary>
    public GearKit Kit { get; }

    /// <summary>The requests made, one per kit line that was missing something, in kit order.</summary>
    public IReadOnlyList<GearGrant> Grants { get; }

    /// <summary>The kit lines the character already held in full (no request).</summary>
    public int AlreadyHeld { get; }

    /// <summary>Why nothing was requested when the inventory could not be counted (a pack entry whose world object has not arrived, or no description yet), or null.</summary>
    public string? NotStarted { get; }

    /// <summary>
    /// Why the spawn refused to ask for anything though the inventory could be counted, or null: the slot guard (<c>slot-guard</c>: the main pack's items plus the new grants would pass <see cref="GearKit.PackCapacity"/>) . The spawn is then <see cref="IsCompleted"/> and not <see cref="Succeeded"/> at once, with no request made.
    /// </summary>
    public string? GuardRefusal { get; }

    /// <summary>Why a merge failed (a refusal by the server, a timeout, a refusal at once), or null.</summary>
    public string? MergeFailure { get; private set; }

    /// <summary>The number of merges the server confirmed.</summary>
    public int Merges => _merged;

    /// <summary>True while merges are still to be sent or waited for (the session's tick pumps them).</summary>
    internal bool MergePending => _phase != MergePhase.Finished;

    /// <summary>The number of requests made (0 when the kit was already held: <c>kit_topup=0</c>).</summary>
    public int Requested => Grants.Count;

    /// <summary>True when nothing was started, or every request has ended.</summary>
    public bool IsCompleted
    {
        get
        {
            if (NotStarted is not null || GuardRefusal is not null)
            {
                return true;
            }

            for (var i = 0; i < Grants.Count; i++)
            {
                if (!Grants[i].Handle.IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>True when it started, every request ended and each created its item, and no merge failed (no refusal of any kind).</summary>
    public bool Succeeded
    {
        get
        {
            if (NotStarted is not null || GuardRefusal is not null || MergeFailure is not null || !IsCompleted)
            {
                return false;
            }

            return AllCreated();
        }
    }

    private bool AllCreated()
    {
        for (var i = 0; i < Grants.Count; i++)
        {
            if (Grants[i].Outcome != ItemOutcome.Created)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when every grant's item is (or was, before it was merged) in the inventory with its world object (see the type's remarks).</summary>
    public bool Arrived
    {
        get
        {
            if (_arrived)
            {
                return true;
            }

            if (!Succeeded || _state is not { } state)
            {
                return false;
            }

            for (var i = 0; i < Grants.Count; i++)
            {
                var grant = Grants[i];
                if (grant.Handle.Result is { } result && state.Objects.Contains(result.Guid) && state.Self.Inventory.Contains(result.Guid))
                {
                    continue;
                }

                if (GearInventory.CountOf(state, _held, grant.Item.Wcid) < grant.Item.Stack)
                {
                    return false;
                }
            }

            return _arrived = true;
        }
    }

    /// <summary>True when nothing is left to wait for: it did not start, it failed, or it arrived and its merges are done.</summary>
    public bool IsDone => IsCompleted && (!Succeeded || (Arrived && _phase == MergePhase.Finished));

    /// <summary>Every refusal, in words (empty when none).</summary>
    public IReadOnlyList<string> Refusals
    {
        get
        {
            var all = new List<string>();
            if (GuardRefusal is { } guard)
            {
                all.Add(guard);
            }

            all.AddRange(Grants.Select(g => g.Refusal).OfType<string>());
            if (MergeFailure is { } merge)
            {
                all.Add(merge);
            }

            return all;
        }
    }

    /// <summary>One line: the requests, the lines already held, the merges and the refusals.</summary>
    public string Describe()
    {
        var refusals = Refusals;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"requested={Requested} held={AlreadyHeld} created={Grants.Count(g => g.Outcome == ItemOutcome.Created)} merged={_merged}/{_plan?.Count ?? 0} refused={refusals.Count}{(NotStarted is { } why ? " not_started=" + why : string.Empty)}{(refusals.Count > 0 ? " [" + string.Join("; ", refusals) + "]" : string.Empty)}");
    }

    /// <summary>
    /// One step of the merge: called by the session's tick (<see cref="PlayerTools.Update"/>) while <see cref="MergePending"/>. It waits for the grants, plans the merges once, then sends one merge at a time and waits for its
    /// outcome. Allocates only when it plans and when it sends.
    /// </summary>
    internal void Pump()
    {
        if (_phase == MergePhase.Finished || _api is null || _state is null)
        {
            return;
        }

        if (_phase == MergePhase.Waiting)
        {
            if (!IsCompleted)
            {
                return;
            }

            if (!AllCreated())
            {
                _phase = MergePhase.Finished; // a refused grant: the spawn failed, nothing is merged
                return;
            }

            if (!Arrived)
            {
                return;
            }

            _plan = PlanMerges(_state);
            _phase = _plan.Count > 0 ? MergePhase.Merging : MergePhase.Finished;
            if (_phase == MergePhase.Finished)
            {
                return;
            }
        }

        if (_current is { } handle)
        {
            if (handle.Outcome is not { } outcome)
            {
                return;
            }

            if (outcome.Kind != ActionOutcomeKind.Confirmed)
            {
                var step = _plan![_next - 1];
                MergeFailure = string.Create(
                    CultureInfo.InvariantCulture,
                    $"merge of {step.Amount} from 0x{step.From:X8} into 0x{step.To:X8} ended {outcome.Kind}: {outcome.Detail}");
                _api.Log.Error(SessionLog.Category, "SpawnGear: " + MergeFailure);
                _phase = MergePhase.Finished;
                return;
            }

            _merged++;
            _current = null;
        }

        if (_next >= _plan!.Count)
        {
            _phase = MergePhase.Finished;
            return;
        }

        var next = _plan[_next++];
        _current = _api.MergeStack(next.From, next.To, next.Amount, _source);
    }

    /// <summary>
    /// For each component line of the kit (granted this run or not: a fragment left by an earlier spawn is merged too): the largest held stack of the weenie is the target and every other held stack of it a source, moved whole (a source that would pass the target's maximum is moved in part,
    /// or skipped). A line with a single stack plans nothing.
    /// </summary>
    private List<MergeStep> PlanMerges(GameState state)
    {
        var plan = new List<MergeStep>();
        var done = new HashSet<uint>();
        var stacks = new List<(uint Guid, int Size, int Max)>();
        foreach (var line in Kit.Items)
        {
            if (line.Kind != GearKind.Component || !done.Add(line.Wcid))
            {
                continue;
            }

            stacks.Clear();
            GearInventory.Collect(state, _held);
            foreach (var guid in _held)
            {
                if (state.Objects.TryGet(guid, out var item) && item.Weenie.WeenieClassId == line.Wcid)
                {
                    stacks.Add((guid, GearInventory.StackOf(state, item), item.Weenie.MaxStackSize ?? line.MaxStack ?? int.MaxValue));
                }
            }

            if (stacks.Count < 2)
            {
                continue;
            }

            var target = 0;
            for (var i = 1; i < stacks.Count; i++)
            {
                if (stacks[i].Size > stacks[target].Size)
                {
                    target = i;
                }
            }

            var size = stacks[target].Size;
            for (var i = 0; i < stacks.Count; i++)
            {
                if (i == target)
                {
                    continue;
                }

                var amount = Math.Min(stacks[i].Size, stacks[target].Max - size);
                if (amount > 0)
                {
                    plan.Add(new MergeStep(stacks[i].Guid, stacks[target].Guid, amount));
                    size += amount;
                }
            }
        }

        return plan;
    }
}

/// <summary>Counts what the inventory read model holds, by weenie class id (pack, side packs, what is in them, equipped; stacks summed).</summary>
internal static class GearInventory
{
    /// <summary>Every guid the player holds, in <paramref name="into"/> (cleared first).</summary>
    internal static void Collect(GameState state, List<uint> into)
    {
        into.Clear();
        var inventory = state.Self.Inventory;
        var pack = inventory.Pack;
        for (var i = 0; i < pack.Count; i++)
        {
            into.Add(pack[i].Guid);
        }

        var side = inventory.SidePacks;
        for (var i = 0; i < side.Count; i++)
        {
            into.Add(side[i].Guid);
            var inside = inventory.ItemsIn(side[i].Guid);
            for (var j = 0; j < inside.Count; j++)
            {
                into.Add(inside[j].Guid);
            }
        }

        var equipped = inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            into.Add(equipped[i].Guid);
        }
    }

    /// <summary>
    /// A caster (ItemType's Caster bit: a wand, a staff or an orb) the player holds in the pack or a side pack and has not wielded, or 0. The one place for it (M5 3 F5): the portal tie wields it before the magic mode (a fresh build's mage
    /// holds its kit unwielded) and the death handler's rearm wields it when the wielded caster went to the corpse; ACE refuses the magic mode with no caster in the held slot (<c>Player_Combat.cs</c> 872-880, <c>GetEquippedWand</c>
    /// <c>Creature_Equipment.cs</c> 141-144 @95ef426d; <see cref="CombatModeGuard"/> answers <see cref="CombatModeProblem.MagicWithoutCaster"/>).
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A list to collect into (cleared first), so a caller that polls allocates nothing.</param>
    internal static uint PackCaster(GameState state, List<uint> scratch)
    {
        Collect(state, scratch);
        foreach (var guid in scratch)
        {
            if (state.Objects.TryGet(guid, out var item) && (item.ItemType & CombatModeGuard.ItemTypeCaster) != 0 && state.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.Equipped)
            {
                return guid;
            }
        }

        return 0;
    }

    /// <summary>The stack of a held item: the server's last SetStackSize, else the weenie description's, else 1.</summary>
    internal static int StackOf(GameState state, WorldObject item)
    {
        if (state.Self.Inventory.TryGetStack(item.Guid, out var size, out _))
        {
            return (int)size;
        }

        return item.Weenie.StackSize is { } stack ? stack : 1;
    }

    /// <summary>How many items of weenie <paramref name="wcid"/> the player holds (stacks summed), using <paramref name="scratch"/> for the guids.</summary>
    internal static int CountOf(GameState state, List<uint> scratch, uint wcid)
    {
        Collect(state, scratch);
        var n = 0;
        for (var i = 0; i < scratch.Count; i++)
        {
            if (state.Objects.TryGet(scratch[i], out var item) && item.Weenie.WeenieClassId == wcid)
            {
                n += StackOf(state, item);
            }
        }

        return n;
    }

    /// <summary>The count of every weenie held, and how many held guids have no world object yet (their weenie is unknown).</summary>
    internal static Dictionary<uint, int> CountAll(GameState state, out int unsettled)
    {
        var guids = new List<uint>();
        Collect(state, guids);
        var counts = new Dictionary<uint, int>();
        unsettled = 0;
        foreach (var guid in guids)
        {
            if (!state.Objects.TryGet(guid, out var item))
            {
                unsettled++;
                continue;
            }

            counts[item.Weenie.WeenieClassId] = counts.GetValueOrDefault(item.Weenie.WeenieClassId) + StackOf(state, item);
        }

        return counts;
    }
}
