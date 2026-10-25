using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>What sort of thing a kit item is.</summary>
public enum GearKind
{
    /// <summary>The held caster the mage wields (<see cref="GearKit.Wand"/>: The Healer's Heart since M3 4.2 P7).</summary>
    Wand,

    /// <summary>A piece of the classic armour the mage wears (<see cref="GearKit.Armour"/>).</summary>
    Armour,

    /// <summary>A spell component a cast burns.</summary>
    Component,

    /// <summary>
    /// M4 3.1 d0 (D20): a death-loop decoy (<see cref="DecoyItem"/>): an ordinary, light, expensive item the mage only carries, so a death drops it and not its gear. One item, no valid location, never wielded; the kit's <c>item</c> top-up spawns a missing one
    /// (counted by weenie class, one each). Only <see cref="GearKit.WithDecoys(IReadOnlyList{DecoyItem})"/> and <see cref="GearKit.ForDeathLoop"/> put one in a kit.
    /// </summary>
    Decoy,
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
}

/// <summary>
/// The BSD mage's gear (M3 1.4 s0; docs/milestones/m3-lights-and-sound.md sections 5.2 and 5.3, plan D9): a held caster (The Healer's Heart), the five pieces of the classic armour and the spell components the profile's spells burn, as the
/// weenies ACE spawns for the <c>item</c> verb. Built once for an account from the SpellTable (<see cref="Build"/>).
/// </summary>
/// <remarks>
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

    private static bool IsKitFightLine(ProfileLine line) => line.Spells.Any(s => KitFightSpells.Contains(s.Id)); // sized as a travel line: a few casts a hero journey, the mage's burden has little headroom

    /// <summary>The burden of one component: the SELECT of the world database gives 4 to every component weenie in the kit (the Prismatic Taper, 20631, has 6, <see cref="PrismaticTaperBurden"/>).</summary>
    public const int ComponentBurden = 4;

    /// <summary>The Prismatic Taper's weenie class id (component 188; maximum stack 1,000): the foci formula's taper, in the kit.</summary>
    public const uint PrismaticTaperWcid = 20631;

    /// <summary>The burden of one Prismatic Taper (a read-only SELECT of the world database, 2026-10-16: 6, where every other component of the kit has <see cref="ComponentBurden"/>).</summary>
    public const int PrismaticTaperBurden = 6;

    /// <summary>The strength the capacity is taken at (<see cref="MageTemplate.Targets"/>: 131 ranks; Strength 141 with the creation value, so this is conservative).</summary>
    public const int CapacityStrength = 131;

    /// <summary>The items a player's main pack holds (the human weenie's ItemCapacity; the stand-in's <c>PackCapacity</c>).</summary>
    public const int PackCapacity = 102;

    /// <summary>
    /// The weenie class ids of ACE's five focus items (<c>Player.FociWCIDs</c>, Player_Spells.cs:451-457: Enchantment 15268, Artifice 15269, Verdancy 15270, Strife 15271, Shadow 43173). A character that holds one casts that school with the foci
    /// formula (<c>HasFoci</c>, 485-486), whose components the kit carries (<see cref="FociFormulaCounts"/>).
    /// </summary>
    public static IReadOnlyList<uint> FociWcids { get; } = [15268, 15269, 15270, 15271, 43173];

    /// <summary>ACE's <c>PaletteTemplate.Gold</c> (ACE.Entity/Enum/PaletteTemplate.cs: the 22nd member, Undef being 0, so 21; the pinned enum, read 2026-10-19).</summary>
    public const int PaletteGold = 21;

    /// <summary>
    /// The held caster (the property keeps its M3 name; the weenie is the owner's D43 choice): weenie 8552 "The Healer's Heart" (<c>orbhealer</c>), a Caster (WeenieType 35, ItemType 0x8000) held in the hand (ValidLocations 0x01000000), burden 50, no
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
    public GearItem WandItem => Items[0];

    /// <summary>The components' lines.</summary>
    public IEnumerable<GearItem> Components => Items.Where(i => i.Kind == GearKind.Component);

    /// <summary>The armour's lines.</summary>
    public IEnumerable<GearItem> ArmourItems => Items.Where(i => i.Kind == GearKind.Armour);

    /// <summary>The decoys' lines (M4 3.1 d0; none in a plain kit).</summary>
    public IEnumerable<GearItem> DecoyItems => Items.Where(i => i.Kind == GearKind.Decoy);

    /// <summary>True when the kit carries decoys (<see cref="WithDecoys(IReadOnlyList{DecoyItem})"/>); a plain kit never does.</summary>
    public bool HasDecoys
    {
        get
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Kind == GearKind.Decoy)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The pack slots the kit takes (each line is one stack, so one slot; a stack never exceeds its weenie's maximum).</summary>
    public int Slots => Items.Count;

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
    /// The kit for an account: the wand, the armour and the components of <see cref="ArenaProfile"/>'s spells as <paramref name="accountName"/> casts them (see the type's remarks for the rule).
    /// </summary>
    /// <param name="spells">The SpellTable's spells (<see cref="SpellTableData.Spells"/>).</param>
    /// <param name="accountName">The account's name; hashed lower-cased, never stored.</param>
    /// <returns>The kit.</returns>
    /// <exception cref="KeyNotFoundException">A profile spell is not in <paramref name="spells"/>.</exception>
    /// <exception cref="InvalidOperationException">A formula holds a component the pinned map (<see cref="ComponentWeenies"/>) does not.</exception>
    /// <param name="heroRing">True only for the hero mage's gear station (M4 P5b fix FB, P5b-R12): the kit also carries <see cref="KitFightSpells"/>' components (Cassius' Ring of Fire). False (every other mage kit, the gate's): the ring's burden is left out.</param>
    public static GearKit Build(IReadOnlyDictionary<uint, SpellInfo> spells, string accountName, bool heroRing = false)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        var account = accountName.ToLowerInvariant();

        var union = new SortedSet<uint>();
        var used = new SortedSet<uint>(); // the account's formulas and the foci formula's components
        var otherThanTravel = new HashSet<uint>(); // the components some non-travel line casts (they keep the MinimumStack; a travel-only component is sized by its few casts: the mage's burden has little headroom)
        var buffBurn = new Dictionary<uint, double>();
        var manaBurn = new Dictionary<uint, double>();
        var warBurn = new Dictionary<uint, double>();
        var travelBurn = new Dictionary<uint, double>();
        var perSpell = new Dictionary<uint, int>();
        var counts = new Dictionary<uint, int>();
        foreach (var line in ArenaProfile.Lines.Concat(ArenaProfile.Extras.Where(l => (l.Role == ProfileRole.Travel && l.Spells.Any(s => KitTravelSpells.Contains(s.Id))) || (heroRing && l.Spells.Any(s => KitFightSpells.Contains(s.Id))))))
        {
            var lineBurn = new Dictionary<uint, double>();
            foreach (var profileSpell in line.Spells)
            {
                var info = spells.TryGetValue(profileSpell.Id, out var found)
                    ? found
                    : throw new KeyNotFoundException($"profile spell {profileSpell.Id} ({profileSpell.Name}) is not in the SpellTable");
                counts.Clear();
                foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account))
                {
                    if (id == 0)
                    {
                        continue;
                    }

                    if (!ComponentWeenies.TryGetByComponent(id, out _))
                    {
                        throw new InvalidOperationException($"spell {profileSpell.Id} ({profileSpell.Name}) needs component {id}, which the component map does not hold");
                    }

                    union.Add(id);
                    used.Add(id);
                    if (line.Role != ProfileRole.Travel && !IsKitFightLine(line))
                    {
                        otherThanTravel.Add(id);
                    }
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                }

                // The foci formula (ACE GetFociFormula): used instead when the school's focus item is held, so the kit carries its components too; a cast burns one formula or the other, so each id counts the larger of the two.
                foreach (var (id, n) in FociFormulaCounts(info.Formula))
                {
                    if (!ComponentWeenies.TryGetByComponent(id, out _))
                    {
                        throw new InvalidOperationException($"spell {profileSpell.Id} ({profileSpell.Name}) needs component {id} in the foci formula, which the component map does not hold");
                    }

                    used.Add(id);
                    counts[id] = Math.Max(counts.GetValueOrDefault(id), n);
                }

                foreach (var (id, n) in counts)
                {
                    ComponentWeenies.TryGetByComponent(id, out var weenie);
                    var burn = n * info.ComponentLoss * (double)weenie.Cdm;
                    lineBurn[id] = Math.Max(lineBurn.GetValueOrDefault(id), burn);
                    perSpell[id] = Math.Max(perSpell.GetValueOrDefault(id), n);
                }
            }

            foreach (var (id, burn) in lineBurn)
            {
                // Buff lines add up (a cycle casts every line); war lines are alternatives (the loops fight with one).
                if (line.IsBuff)
                {
                    buffBurn[id] = buffBurn.GetValueOrDefault(id) + burn;
                }
                else if (line.Role == ProfileRole.Travel || IsKitFightLine(line))
                {
                    // The travel lines (M4 P5b J3b): a recall or a tie is cast a few times a gate (a journey, its fizzles).
                    travelBurn[id] = travelBurn.GetValueOrDefault(id) + burn;
                }
                else if (line.IsManaLine)
                {
                    // The mana lines (D24) are cast ManaCastsPerLine times a cycle, each cast at its worst spell.
                    manaBurn[id] = manaBurn.GetValueOrDefault(id) + (burn * ManaCastsPerLine);
                }
                else
                {
                    warBurn[id] = Math.Max(warBurn.GetValueOrDefault(id), burn);
                }
            }
        }

        var items = new List<GearItem> { ItemOf(Wand) };
        foreach (var piece in Armour)
        {
            items.Add(ItemOf(piece));
        }

        var ids = new SortedSet<uint>(used);
        for (var taper = SpellFormulaRules.LowestTaperId; taper < SpellFormulaRules.LowestTaperId + 12; taper++)
        {
            ids.Add(taper);
        }

        foreach (var id in ids)
        {
            ComponentWeenies.TryGetByComponent(id, out var weenie);
            var wanted = SpareTaperStack;
            if (used.Contains(id))
            {
                var expected = (BuffCycles * (buffBurn.GetValueOrDefault(id) + manaBurn.GetValueOrDefault(id))) + (FightCasts * warBurn.GetValueOrDefault(id)) + (TravelCasts * travelBurn.GetValueOrDefault(id));
                wanted = Math.Max(Math.Max(otherThanTravel.Contains(id) ? MinimumStack : TravelMinimumStack, perSpell[id]), (int)Math.Ceiling(Safety * expected));
            }

            items.Add(new GearItem(
                weenie.Wcid, weenie.Name, GearKind.Component, WeenieTypeSpellComponent, ItemTypeSpellComponents, EquipMask.None, Math.Min(wanted, weenie.MaxStack), weenie.MaxStack, weenie.Wcid == PrismaticTaperWcid ? PrismaticTaperBurden : ComponentBurden));
        }

        return new GearKit(items, [.. union]);
    }

    /// <summary>
    /// The death loop's kit (M4 3.1 d0, D20): this kit's lines, then <paramref name="decoys"/> as <see cref="GearKind.Decoy"/> lines (stack 1, no valid location, never wielded). A kit that already has decoys has them replaced, so the call is idempotent. The burden
    /// and the slots include the decoys; this kit is not changed, and a plain kit (<see cref="Build"/>, <see cref="Of"/>) stays what it was: the buff, fight and login legs never see a decoy.
    /// </summary>
    /// <param name="decoys">The decoys (<see cref="DecoyKit.For"/> for a level, <see cref="DecoyKit.All"/> for all eight).</param>
    public GearKit WithDecoys(IReadOnlyList<DecoyItem> decoys)
    {
        ArgumentNullException.ThrowIfNull(decoys);
        var items = new List<GearItem>(Items.Count + decoys.Count);
        foreach (var item in Items)
        {
            if (item.Kind != GearKind.Decoy)
            {
                items.Add(item);
            }
        }

        foreach (var decoy in decoys)
        {
            items.Add(decoy.ToGearItem());
        }

        return new GearKit(items, CustomisedComponentIds);
    }

    /// <summary><see cref="WithDecoys(IReadOnlyList{DecoyItem})"/> with the decoys <see cref="DecoyKit.CountFor"/> asks for at <paramref name="level"/>.</summary>
    /// <param name="level">The character's level.</param>
    public GearKit WithDecoys(int level) => WithDecoys(DecoyKit.For(level));

    /// <summary>
    /// The kit the death loop spawns (M4 3.1 d0, D20): with <see cref="DeathOptions.Decoys"/> on (<c>--death-decoys</c>, the default) <see cref="Build"/>'s kit with the decoys of <see cref="DecoyKit.For"/> at <paramref name="level"/>; off, the plain kit (M3's loop exactly).
    /// The death scenario calls this; the buff, fight and login legs call <see cref="Build"/>.
    /// </summary>
    /// <param name="spells">The SpellTable's spells (<see cref="SpellTableData.Spells"/>).</param>
    /// <param name="accountName">The account's name (see <see cref="Build"/>).</param>
    /// <param name="options">The death loop's options.</param>
    /// <param name="level">The level the decoys are counted for (default <see cref="MageTemplate.TargetLevel"/>, BSD Mage's).</param>
    public static GearKit ForDeathLoop(IReadOnlyDictionary<uint, SpellInfo> spells, string accountName, DeathOptions options, int level = MageTemplate.TargetLevel)
    {
        ArgumentNullException.ThrowIfNull(options);
        var kit = Build(spells, accountName);
        return options.Decoys ? kit.WithDecoys(level) : kit;
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

    /// <summary>A kit of the given lines (for tests of <see cref="PlayerTools.SpawnGear"/> that need no SpellTable).</summary>
    internal static GearKit Of(IReadOnlyList<GearItem> items) => new(items, []);

    /// <summary>One line of the kit's report: the numbers a test or a handoff prints.</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"items={Items.Count} components={Items.Count(i => i.Kind == GearKind.Component)} customised_components={CustomisedComponentIds.Count} slots={Slots}/{PackCapacity} burden={Burden}/{EncumbranceCapacity(CapacityStrength)}{(HasDecoys ? " decoys=" + DecoyItems.Count() : string.Empty)}");

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
