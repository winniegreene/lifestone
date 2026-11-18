using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M3 1.4 s0, the gear kit: the wand, the leather set and the spell components the BSD profile's spells burn, for a fixed made-up account name (the real account name is never in a test, a log or a file), with the customised
/// component list pinned, every taper colour in the kit, the stacks' rule, the burden and slot totals, and <see cref="PlayerTools.SpawnGear"/> against the offline stand-in: it requests only what is missing, tops a component up,
/// does nothing on a rerun, waits for the world object, and fails loudly on a refusal.
/// </summary>
public sealed class GearKitTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Account = "kit-test-account";

    private GearKit KitFor(string account) => GearKit.Build(SpellTableData.Load(fixture.Dats).Spells, account);

    [RequiresDatsFact]
    public void The_kit_for_a_fixed_account_is_the_vendor_kit_the_four_foci_and_the_stock_lines_and_burns_six_component_ids()
    {
        // M5 3 m1 (D8): the 38 customised ids and the 57 component lines are retired; the foci formula burns the Copper Scarab (the travel spells), the Pyreal Scarab (VI), the Platinum Scarab (VII) and the Prismatic Taper.
        var kit = KitFor(Account);
        output.WriteLine(kit.Describe());
        Assert.Equal<uint>([1, 3, 4, 6, 112, 188], kit.CustomisedComponentIds); // M5 7 F16: the Lead Scarab (1) of the emergency drain joins the four; M5 7 P7j: the Silver Scarab (4) of Spirit Drinker IV, the learn list's level
        Assert.Equal("20631:1000 379:30 690:400 8897:100 686:20 7299:6 691:100 688:20 687:20", string.Join(' ', kit.Components.Select(c => c.Wcid + ":" + c.Stack))); // M5 7 F25 (D93): the buff's Silver (IV) and Gold (V) Scarabs, carried
        Assert.Equal(["Wand", "Faran Robe with Hood", "Leather Gauntlets", "Pack", "Foci of Enchantment", "Foci of Artifice", "Foci of Verdancy", "Foci of Strife", "Prismatic Taper", "Mana Potion", "Pyreal Scarab", "Platinum Scarab", "Copper Scarab", "Diamond Scarab", "Lead Scarab", "Silver Scarab", "Gold Scarab"], kit.Items.Select(i => i.Name));
        Assert.Equal(19, kit.Slots); // M5 7 F25: the Silver and Gold Scarab lines are two more stacks; // M5 7 F16: the Lead Scarab line is one more stack; 13 lines (M5 7 F13, D87: the hooded robe and the gloves are two worn lines; M5 7 F10: the ring's Diamond Scarab), and the Pyreal Scarab's 400 are four stacks of 100 (M5 7 F3, D87; F10: 300 -> 400)
        Assert.Equal(17, kit.Items.Count); // M5 7 G3: the side-pack line; M5 7 F16: the Lead Scarab line; M5 7 F25: the Silver and Gold Scarab lines
        Assert.Equal(9, kit.Components.Count()); // M5 7 F10: and the Diamond Scarab line; M5 7 F25: the Silver and Gold Scarab lines
    }

    [RequiresDatsFact]
    public void The_account_no_longer_changes_the_kit_the_foci_formula_is_the_same_for_every_name()
    {
        // M5 3 m1: the customised taper hash (SpellFormulaRules.CustomizeForAccount) only applies to the full formula; the foci formula (SpellFormula.cs:338-381) does not read the account, so no account name changes a line.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var kit = GearKit.Build(spells, Account);
        Assert.Equal(kit.Items, GearKit.Build(spells, Account.ToUpperInvariant()).Items);
        Assert.Equal(kit.Items, GearKit.Build(spells, "another-account").Items);
        Assert.Throws<ArgumentException>(() => GearKit.Build(spells, string.Empty));
    }

    [RequiresDatsFact]
    public void No_coloured_taper_is_in_the_kit_and_the_foci_formulas_prismatic_taper_is()
    {
        var kit = KitFor(Account);
        var taperWcids = Enumerable.Range(63, 12).Select(id => ComponentWeenies.TryGetByComponent((uint)id, out var w) ? w.Wcid : 0).ToArray();
        Assert.DoesNotContain(0u, taperWcids);
        Assert.All(taperWcids, wcid => Assert.Null(kit.ItemOf(wcid))); // M5 3 m1: the twelve coloured tapers are retired with the full formulas
        Assert.Equal((GearKit.PrismaticTaperWcid, GearKit.PrismaticTaperBurden, 1000), (kit.ItemOf(20631)!.Wcid, kit.ItemOf(20631)!.UnitBurden, kit.ItemOf(20631)!.MaxStack!.Value)); // the foci formula's taper (SpellFormula.cs:378)
        Assert.Contains(188u, kit.CustomisedComponentIds);
    }

    [RequiresDatsFact]
    public void Every_component_any_profile_spell_uses_is_in_the_component_map_for_any_account()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var account in new[] { Account, "a", "Dereth", "zoë", "x y z 42", "accounts.test" })
        {
            // Build throws for a component the map lacks, so building is the assertion.
            var kit = GearKit.Build(spells, account);
            Assert.All(kit.CustomisedComponentIds, id => Assert.True(ComponentWeenies.TryGetByComponent(id, out _), $"component {id} for {account}"));
        }
    }

    [RequiresDatsFact]
    public void The_stacks_cover_what_one_cast_needs_and_stay_within_the_weenies_maximum()
    {
        var kit = KitFor(Account);
        foreach (var c in kit.Components)
        {
            Assert.All(GearKit.StackSizes(c.Stack, c.MaxStack), stack => Assert.InRange(stack, 1, c.MaxStack!.Value)); // M5 7 F3 (D87): a line past one stack is granted in several
            Assert.Equal(c.Wcid == GearKit.PrismaticTaperWcid ? GearKit.PrismaticTaperBurden : c.WeenieType == GearKit.WeenieTypeSpellComponent ? GearKit.ComponentBurden : 15, c.UnitBurden);
        }

        // A formula can hold a component twice (ACE needs as many in the pack as the slots, Player_Magic.cs:1234-1245): no stack is below the most any one cast of the ground needs.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var burn in GearKit.CastBurns(Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround), spells, heroRing: false))
        {
            Assert.True(kit.ItemOf(burn.Wcid)!.Stack >= burn.PerCast, $"spell {burn.SpellId} ({burn.Spell}) needs {burn.PerCast} of {burn.Name}");
        }
    }

    [RequiresDatsFact]
    public void The_kit_list_reads_the_account_formula_for_a_school_without_its_focus_and_the_foci_formula_when_it_is_held_M6_3_a2()
    {
        // m6-kit-account-formula closed: with a focus test, a school whose focus is not held burns AccountFormula's components; every focus held is the foci formula unchanged (the den's mage).
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var den = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        var today = GearKit.CastBurns(den, spells, heroRing: false);
        Assert.Equal(today, GearKit.CastBurns(den, spells, heroRing: false, Account, _ => true));
        Assert.Equal(today, GearKit.CastBurns(den, spells, heroRing: false, null, _ => false)); // no account name: the foci formula, as before

        // A focus-less character: the first school's cast is read on the account formula (AccountFormula.Counts); the den's stock holds no such component, so the kit fails by name (a profile gap, never a silent guess).
        var gap = Assert.Throws<InvalidOperationException>(() => GearKit.CastBurns(den, spells, heroRing: false, Account, _ => false));
        Assert.Contains("ground-plan-gap", gap.Message, StringComparison.Ordinal);
        Assert.Contains("the account's full formula (no focus of the school held)", gap.Message, StringComparison.Ordinal); // M6 6 MG (m6-kit-account-gap-text): the message names the formula that was read
        Assert.DoesNotContain("TODO(m6-kit-account-gap-text)", gap.Message, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void M6_6_MG_the_kit_build_passes_the_focus_test_through_and_the_gap_names_the_foci_formula_when_that_is_the_one_read()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var den = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        var held = GearKit.Build(spells, Account, ground: den, holdsFocus: _ => true); // every focus held: the foci formula, the kit as it is today
        Assert.Equal(KitFor(Account).Burden, held.Burden);
        var gap = Assert.Throws<InvalidOperationException>(() => GearKit.Build(spells, Account, ground: den, holdsFocus: _ => false));
        Assert.Contains("the account's full formula", gap.Message, StringComparison.Ordinal);

        var noFocus = den with { Supplies = den.Supplies with { Stock = [.. den.Supplies.Stock.Where(l => l.Wcid is 686)] } }; // a ground whose stock lacks the foci formula's lines: the foci formula's gap
        var fociGap = Assert.Throws<InvalidOperationException>(() => GearKit.CastBurns(noFocus, spells, heroRing: false));
        Assert.Contains("on the foci formula", fociGap.Message, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_kit_stays_well_under_the_burden_capacity_and_the_pack_slots_and_the_numbers_are_printed()
    {
        var kit = KitFor(Account);
        var capacity = GearKit.EncumbranceCapacity(GearKit.CapacityStrength);
        output.WriteLine($"kit: {kit.Describe()}; capacity at strength {GearKit.CapacityStrength} = {capacity}");
        Assert.Equal(19_650, capacity);
        // M5 3 m1: wand 50 + robe 200 (M5 7 F13, D87: the hooded robe, same burden) + gloves 270 + the four foci 1,600 + 1,000 tapers x 6 + 30 potions x 15 + 420 scarabs x 4 = 10,250 (M5 7 F3, D87: the Pyreal Scarab line keeps 300, was 100; the loot kit was 17,143 of the same capacity).
        Assert.Equal(2_150, kit.Items.Where(i => i.Kind != GearKind.Component).Sum(i => i.Burden)); // M5 7 G3: 2,120 + two side packs at 15 each (seven refused live: no room)
        Assert.Equal(11_264, kit.Burden); // M5 7 G3 (seven side packs of burden 15, 11,234 + 105); M5 7 F25 (D93): 20 Silver and 20 Gold Scarabs at 4 each, 11,074 + 160; M5 7 F16 (D91): 100 Lead Scarabs for the emergency drain at 4 each, 10,674 + 400; M5 7 F10 (the components rule): 100 more Pyreal Scarabs and 6 Diamond Scarabs at 4 each, 10,250 + 424
        Assert.True(kit.Burden * 100 <= capacity * 90, $"the kit's burden {kit.Burden} is over 90% of {capacity}");
        Assert.True(kit.Slots <= GearKit.PackCapacity / 4, $"{kit.Slots} slots of {GearKit.PackCapacity}");
        Assert.Equal(kit.Items.Sum(i => i.Burden), kit.Burden);
    }

    [RequiresDatsFact]
    public void A_mage_that_wore_the_leather_set_and_the_old_wand_still_fits_under_its_capacity_after_the_swap_as_the_character_panel_counts_it()
    {
        // The swap takes the leather and the old wand to the pack; they stay there (their burden counts). The character panel's capacity is EncumbranceSystem.EncumbranceCapacity(strength with the registry, carrying augmentations)
        // (GameCharacterInfo.LoadOf, CACQualities::InqLoad 0x0058FF60): BSD Mage's Strength is the creation 10 plus the 131 ranks of the plan = 141, no augmentations; the build's buffs only raise it.
        var kit = KitFor(Account);
        const int OldWand = 50;
        Assert.Equal(2_560, GearKit.LeatherSetBurden);
        var carried = kit.Burden + GearKit.LeatherSetBurden + OldWand;
        var real = EncumbranceSystem.EncumbranceCapacity((int)(MageTemplate.LowAttribute + GearKit.CapacityStrength), 0);
        var conservative = EncumbranceSystem.EncumbranceCapacity(GearKit.CapacityStrength, 0);
        output.WriteLine($"kit {kit.Burden} + leather {GearKit.LeatherSetBurden} + old wand {OldWand} = {carried}; capacity at strength 141 = {real} (load {(double)carried / real:P1}), at 131 = {conservative} (load {(double)carried / conservative:P1})");
        Assert.Equal(21_150, real);
        Assert.True(carried * 100 <= real * 95, $"carrying {carried} is over 95% of {real}: the load penalty would start");
        Assert.True(carried * 100 <= conservative * 101, $"carrying {carried} is over 101% of the conservative capacity {conservative}"); // M4 P5b J3b relaxed this to 102% for the recall's and the tie's components plus J7's ring; the ring is hero-only again (fix FB, P5b-R12) and the kit is 19,753 (100.5%), so 101% - 100% does not come back (the recall's and the tie's components stay), the real-capacity 95% rule holds
        Assert.True(EncumbranceSystem.Load(real, carried) < 1.0);
    }

    [Fact]
    public void The_held_caster_and_the_classic_armour_are_the_selects_weenies_with_their_palettes_and_the_kit_holds_items_only()
    {
        // D43 (2026-10-19): The Healer's Heart (8552, the only weenie of that name), the olthoi helm 3715, the pre-"new" Shadow Amuli rows 6600 and 6606 (armour level 190), and the plain Platemail Gauntlets 57 and Sollerets 107 in Gold.
        Assert.Equal((8552u, "The Healer's Heart", 35u, EquipMask.Held, 50), (GearKit.Wand.Wcid, GearKit.Wand.Name, GearKit.Wand.WeenieType, GearKit.Wand.ValidLocations, GearKit.Wand.Burden));
        Assert.Equal(
            [(3715u, 0x1u, 230, 600, (int?)null), (6600u, 0x1A00u, 190, 1600, null), (6606u, 0x6400u, 190, 2288, null), (57u, 0x20u, 150, 919, 21), (107u, 0x100u, 150, 540, 21)],
            GearKit.Armour.Select(a => (a.Wcid, (uint)a.ValidLocations, a.ArmorLevel, a.Burden, a.Palette)));
        Assert.Equal(["Olthoi Helm", "Greater Amuli Shadow Coat", "Greater Amuli Shadow Leggings", "Platemail Gauntlets", "Sollerets"], GearKit.Armour.Select(a => a.Name));
        Assert.Equal(21, GearKit.PaletteGold); // ACE's PaletteTemplate.Gold: Undef 0 ... Yellow 17, YellowBrown 18, Copper 19, Silver 20, Gold 21
        Assert.All(GearKit.Armour, a => Assert.Null(a.Shade)); // the weenie's own shade (57 and 107 default to 0.66; @ci needs none)
        Assert.Null(GearKit.Wand.Palette);
        Assert.Equal(5_947, GearKit.Armour.Sum(a => a.Burden)); // q7: 5,947 against the leather set's 2,560
        // Disjoint places: no piece covers another's slot, so the swap never needs a layering order.
        var taken = (uint)GearKit.Wand.ValidLocations;
        foreach (var piece in GearKit.Armour)
        {
            Assert.Equal(0u, taken & (uint)piece.ValidLocations);
            taken |= (uint)piece.ValidLocations;
        }

        // The orb is not a focus: ACE's HasFoci (Player_Spells.cs 458-488) does not apply, so the casts use the account-customised formulas, whose components the kit carries (the dats test below).
        Assert.DoesNotContain(GearKit.Wand.Wcid, GearKit.FociWcids);

        // TODO(item-creature) (the mod is not changed in wave 1): the kit asks the mod's item verb only for these WeenieTypes, never a Creature (10).
        Assert.All(GearKit.Armour, a => Assert.Equal(GearKit.WeenieTypeClothing, a.WeenieType));
        Assert.All(new[] { GearKit.Wand }.Concat(GearKit.Armour), w => Assert.Contains(w.WeenieType, new[] { GearKit.WeenieTypeCaster, GearKit.WeenieTypeClothing }));
        Assert.Equal(ComponentWeenies.All.Count, ComponentWeenies.All.Select(c => c.Wcid).Distinct().Count());
        Assert.DoesNotContain(GearKit.Wand.Wcid, ComponentWeenies.All.Select(c => c.Wcid));
        // The tuskers (creatures) are the loops' targets, not the kit's.
        Assert.Empty(new uint[] { 1628, 1629 }.Intersect(GearKit.Armour.Select(a => a.Wcid).Append(GearKit.Wand.Wcid).Concat(ComponentWeenies.All.Select(c => c.Wcid))));
    }

    [RequiresDatsFact]
    public void The_wand_is_the_vendors_and_not_a_focus_and_every_foci_formula_count_is_in_the_kit()
    {
        var kit = KitFor(Account);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(GearKind.Wand, kit.WandItem.Kind);
        Assert.Equal(2472u, kit.WandItem.Wcid); // the vendor wand, from the den's profile (the Healer's Heart 8552 is retired from the mage kit)
        Assert.DoesNotContain(kit.WandItem.Wcid, GearKit.FociWcids);
        Assert.All(GearKit.MageFociWcids, focus => Assert.NotNull(kit.ItemOf(focus)));
        foreach (var spell in ArenaProfile.Lines.Where(l => l.Spells.Count < 7 && !l.ByPlay).SelectMany(l => l.Spells).Concat(ArenaProfile.LineNamed("Flame Bolt")!.Spells.Skip(5).Take(2)).Append(ArenaProfile.Spell(3256)!)) // M5 7 P7j: of Spirit Drinker, the IV the den's learn list teaches
        {
            foreach (var (id, count) in GearKit.FociFormulaCounts(spells[spell.Id].Formula))
            {
                ComponentWeenies.TryGetByComponent(id, out var weenie);
                Assert.True(kit.ItemOf(weenie.Wcid)!.Stack >= count, $"spell {spell.Id}: the foci formula needs {count} of {weenie.Name}");
            }
        }
    }

    [RequiresDatsFact]
    public void Every_line_of_the_built_kit_is_an_item_type_the_select_names()
    {
        var kit = KitFor(Account);
        Assert.All(kit.Items, i => Assert.Contains(i.WeenieType, new[] { GearKit.WeenieTypeCaster, GearKit.WeenieTypeClothing, GearKit.WeenieTypeSpellComponent, 1u, 18u, 21u })); // the foci are Generic (1), the potion Food (18), the side pack Container (21, M5 7 G3)
        Assert.DoesNotContain(kit.Items, i => i.WeenieType == 10); // WeenieType.Creature
        Assert.Equal(1, kit.Items.Count(i => i.Kind == GearKind.Wand));
        Assert.Equal(2, kit.Items.Count(i => i.Kind == GearKind.Armour)); // the hooded robe covers the body and the head in one piece, the gauntlets the hands (M5 7 F13, D87)
        Assert.Equal(4, kit.Items.Count(i => i.Kind == GearKind.Focus));
    }

    [Fact]
    public void A_profile_spell_the_table_lacks_fails_the_build_by_name()
    {
        var e = Assert.Throws<KeyNotFoundException>(() => GearKit.Build(new Dictionary<uint, SpellInfo>(), Account));
        Assert.Contains("is not in the SpellTable", e.Message, StringComparison.Ordinal);
    }

    [RequiresDatsTheory]
    [InlineData("tusker-burrow")]
    [InlineData("cave-of-alabree")]
    public void A_ground_with_no_vendor_kit_fails_by_name_and_a_stock_only_plan_is_the_foci_and_the_stock_M6_5(string key)
    {
        // M6 5 round 1 (TODO(m6-loopback-burrow-kit) closed): the Throwaway loopbacks grant a kit-less ground's foci and stock; every play caller still fails by name.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(key);
        var gap = Assert.Throws<InvalidOperationException>(() => GearKit.KitPlan.For(ground, spells, Account));
        Assert.Contains("ground-plan-gap", gap.Message, StringComparison.Ordinal);
        var plan = GearKit.KitPlan.For(ground, spells, Account, stockOnly: true);
        output.WriteLine(plan.Kit.Describe() + " " + plan.LoadLine);
        Assert.Equal("Wand", Assert.Single(plan.Kit.Items, i => i.Kind == GearKind.Wand).Name); // round 2: ACE's magic mode needs a wielded caster
        Assert.DoesNotContain(plan.Kit.Items, i => i.Kind == GearKind.Armour);
        Assert.Equal(4, plan.Kit.Items.Count(i => i.Kind == GearKind.Focus));
        Assert.Equal(ground.Supplies.Stock.Count, plan.Kit.Items.Count(i => i.Kind == GearKind.Component));
        Assert.Equal("skipped", MageBuildScenario.TieIntent(plan, endSession: true)); // round 2: no build-time tie on a low rung (a level-10 mage fizzles Primary Portal Tie)
    }

    /// <summary>M6 7 K (round 1c): the level-32 Throwaway's kit on the Cave of Alabree carries the bolt its fight picks off the ground's plan: one line beyond the stock, the stock's lines unchanged, and only a stock-only plan may ask.</summary>
    [RequiresDatsFact]
    public void A_stock_only_plan_carries_the_off_plan_bolts_scarab_beside_the_stock_M6_7_K()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("cave-of-alabree");
        var boltIV = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells[3].Id;
        var plain = GearKit.KitPlan.For(ground, spells, Account, stockOnly: true);
        var plan = GearKit.KitPlan.For(ground, spells, Account, stockOnly: true, offPlan: new GearKit.OffPlanBolts([boltIV], 60));
        output.WriteLine(plan.Kit.Describe() + " " + plan.LoadLine);
        var added = Assert.Single(plan.Kit.Items.Where(i => i.Kind == GearKind.Component).ExceptBy(plain.Kit.Items.Select(i => i.Wcid), i => i.Wcid));
        Assert.Equal(ground.Supplies.Stock.Count + 1, plan.Kit.Items.Count(i => i.Kind == GearKind.Component));
        Assert.Equal(60, added.Stack);
        foreach (var (component, _) in GearKit.FociFormulaCounts(spells[boltIV].Formula))
        {
            Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie));
            Assert.Contains(plan.Kit.Items, i => i.Wcid == weenie.Wcid); // the pack the fight judges pays for the bolt
        }

        var den = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        var refused = Assert.Throws<InvalidOperationException>(() => GearKit.KitPlan.For(den, spells, Account, offPlan: new GearKit.OffPlanBolts([boltIV], 60)));
        Assert.Contains("play grants nothing", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 7 AV (live, 2026-11-06: Lifestone Recall refused 0x0400): what Lifestone Recall (1635) casts in an account's formula (the tapers' colours depend on the account; the rest does not): Copper Scarab, Comfrey, Powdered Malachite, Turpeth, Rowan Talisman and three tapers. Comfrey and Powdered Malachite
    /// are on no ground's stock line (the Holtburg outskirts' is read here), so a trip's restock cannot buy them: <c>TODO(m6-recall-components-stock)</c>, pinned so the day a line is added this test says so.
    /// </summary>
    [RequiresDatsFact]
    public void Lifestone_Recalls_components_and_the_two_the_outskirts_stock_lacks_M6_7_AV()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var recall = spells[1635];
        var formula = SpellFormulaRules.CustomizeForAccount(recall.Formula, recall.FormulaVersion, Account);
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("holtburg-outskirts");
        var lacking = new List<string>();
        var all = new List<string>();
        foreach (var id in formula.Where(c => c != 0).Distinct())
        {
            Assert.True(ComponentWeenies.TryGetByComponent(id, out var weenie), "component " + id);
            all.Add(weenie.Name);
            if (ground.Supplies.StockOf(weenie.Wcid) is null)
            {
                lacking.Add(weenie.Name);
            }
        }

        output.WriteLine("Lifestone Recall casts: " + string.Join(", ", all) + "; not stock lines: " + string.Join(", ", lacking));
        Assert.Contains("Comfrey", all);
        Assert.Equal(["Comfrey", "Powdered Malachite"], lacking.Order().ToArray());
    }

    /// <summary>M6 7 K: a component a stock line already holds adds no line; the slots of a cast multiply the casts; the stack caps the count; a spell the table lacks fails by name. No dats.</summary>
    [Fact]
    public void The_off_plan_lines_are_only_what_the_stock_lacks_M6_7_K()
    {
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("cave-of-alabree");
        SpellInfo Bolt(uint id, params uint[] formula) => WorldActionRig.Spell(id, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Bolt " + id, Formula = formula };
        var spells = new Dictionary<uint, SpellInfo> { [1] = Bolt(1, 2, 0, 0, 0, 0, 0, 0, 0), [2] = Bolt(2, 4, 0, 0, 0, 0, 0, 0, 0), [3] = Bolt(3, 4, 4, 0, 0, 0, 0, 0, 0) };

        var ok = new GearKit.OffPlanBolts([1, 2, 3], 10).Lines(spells, ground);
        var line = Assert.Single(ok, l => ground.Supplies.StockOf(l.Wcid) is null);
        Assert.Equal(ok.Count, ok.Count(l => ground.Supplies.StockOf(l.Wcid) is null));
        Assert.True(ComponentWeenies.TryGetByComponent(line.Component, out var weenie));
        Assert.Equal(weenie.Wcid, line.Wcid);
        Assert.Equal(Math.Min(20, weenie.MaxStack), line.Count); // bolt 3 has two slots of it

        Assert.Throws<KeyNotFoundException>(() => new GearKit.OffPlanBolts([9], 10).Lines(spells, ground));
    }

    [RequiresDatsFact]
    public void A_withheld_spell_burns_nothing_so_the_outskirts_stock_only_plan_builds_without_the_recall_scarab_M6_6()
    {
        // M6 6 live round 2: the low-rung death loopback withholds Lifestone Recall (1635). M6 7 AR: the Copper Scarab is an outskirts stock line now (the III lines), so the plan builds with or without the withheld spells.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("holtburg-outskirts");
        Assert.Equal(1, ground.Supplies.StockOf(686)!.StopUnder);
        var plan = GearKit.KitPlan.For(ground, spells, Account, stockOnly: true, withheld: [Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell, Lifestone.Client.Session.Scenarios.Journey.BindStation.LifestoneTieSpell]); // the tie is skipped without the recall (tied=skipped)
        output.WriteLine(plan.Kit.Describe() + " " + plan.LoadLine);
        Assert.Equal(4, plan.Kit.Items.Count(i => i.Kind == GearKind.Focus));
    }

    [RequiresDatsFact]
    public void Lifestone_Recall_on_the_foci_formula_burns_only_stocked_outskirts_lines_M6_7_BC()
    {
        // M6 7 BC (gate A climb 19): the climber holds the four foci, so ACE casts Lifestone Recall on the foci formula (Player.HasFoci, Player_Spells.cs:458-488 @95ef426d): the Copper Scarab and Prismatic Tapers, never the full
        // formula's Comfrey, Powdered Malachite, Turpeth and Rowan Talisman. Each is a stock line of the outskirts that holds a cast's worth. TODO(m6-recall-components-stock): the full formula's Comfrey 767 and Powdered Malachite 788
        // (Cindrue sells both, world SQL) have no stock line, which only a climber that lost a focus would need.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("holtburg-outskirts");
        var recall = spells[Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell];
        var counts = GearKit.FociFormulaCounts(recall.Formula);
        Assert.NotEmpty(counts);
        foreach (var (component, n) in counts)
        {
            Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie), $"component {component}");
            var stock = ground.Supplies.StockOf(weenie.Wcid);
            Assert.True(stock is not null && stock.StopUnder >= n, $"{weenie.Name} x{n} on the foci formula needs an outskirts stock line that holds it");
        }

        Assert.Contains(counts.Keys, c => ComponentWeenies.TryGetByComponent(c, out var w) && w.Wcid == 686);
    }

    // ---------------------------------------------------------------- PlayerTools.SpawnGear against the offline stand-in

    private static GearItem Comp(uint wcid, string name, int stack) =>
        new(wcid, name, GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, stack, 100, GearKit.ComponentBurden);

    private static GearItem Wand() =>
        new(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand.WeenieType, GearKit.Wand.ItemType, GearKit.Wand.ValidLocations, 1, null, GearKit.Wand.Burden);

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(Dictionary<uint, StandInItemKind>? items = null)
        {
            var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Items = items };
            Log = new RecordingLogSink();
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                Log);
            Run(2.0);
        }

        public GameSession Session { get; }

        public RecordingLogSink Log { get; }

        public PlayerInventory Inventory => Session.State.Self.Inventory;

        public StandInWorld World => Session.Reducer.LocalResponder!.World!;

        /// <summary>The unit count the model holds of a weenie, summed over its stacks.</summary>
        public int Held(uint wcid) => Inventory.Pack.Sum(e => Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid ? GearInventory.StackOf(Session.State, o) : 0);

        /// <summary>How many pack entries are stacks of a weenie.</summary>
        public int Stacks(uint wcid) => Inventory.Pack.Count(e => Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

        public void Run(double seconds, double step = 0.1)
        {
            for (var t = 0.0; t < seconds; t += step)
            {
                _time.Advance(TimeSpan.FromSeconds(step));
                Session.Tick(Session.Clock.Now);
            }
        }

        /// <summary>Runs until the spawn is done (the rate of 2 requests a second makes a kit take a while), or the limit.</summary>
        public GearSpawn Finish(GearSpawn spawn, double limit = 60)
        {
            for (var t = 0.0; t < limit && !spawn.IsDone; t += 0.1)
            {
                Run(0.1);
            }

            return spawn;
        }

        public void Dispose() => Session.Dispose();
    }

    [Theory]
    [InlineData(0, 100, new int[] { 0 })]
    [InlineData(1, 100, new[] { 1 })]
    [InlineData(100, 100, new[] { 100 })]
    [InlineData(101, 100, new[] { 100, 1 })]
    [InlineData(300, 100, new[] { 100, 100, 100 })]
    [InlineData(250, 100, new[] { 100, 100, 50 })]
    [InlineData(1000, 1000, new[] { 1000 })]
    public void A_line_is_granted_in_stacks_of_at_most_the_weenies_maximum_M5_7_F3(int total, int max, int[] expected)
    {
        Assert.Equal(expected, GearKit.StackSizes(total, max));
        Assert.Equal([total], GearKit.StackSizes(total, null)); // an item with no maximum is one stack
    }

    [Fact]
    public void SpawnGear_asks_for_a_line_of_more_than_one_stack_in_stacks_of_the_maximum_and_a_rerun_asks_for_nothing_M5_7_F3()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 250)]);
        Assert.Equal(1 + 3, kit.Slots);

        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));
        Assert.True(spawn.Succeeded);
        Assert.True(spawn.Arrived);
        Assert.Equal([(GearKit.Wand.Wcid, (int?)null), (691u, 100), (691u, 100), (691u, 50)], spawn.Grants.Select(g => (g.Item.Wcid, g.Requested)));
        Assert.Empty(spawn.Refusals);
        Assert.Equal(250, rig.Held(691));
        Assert.Equal(3, rig.Stacks(691)); // full stacks never merge past the maximum

        var again = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));
        Assert.Equal(0, again.Requested);
        Assert.True(again.Succeeded);

        // A top-up past a held stack asks for the stacks of the rest only: 100 held, 300 kept = two more of 100.
        using var topUp = new Rig();
        var held = topUp.Session.Tools.SpawnItem(691, 100, ActionSource.Host);
        topUp.Run(1.5);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(held));
        var more = topUp.Finish(topUp.Session.Tools.SpawnGear(GearKit.Of([Comp(691, "Lead Scarab", 300)]), ActionSource.Host));
        Assert.True(more.Succeeded);
        Assert.Equal([100, 100], more.Grants.Select(g => g.Requested!.Value));
        Assert.Equal(300, topUp.Held(691));
    }

    [Fact]
    public void SpawnGear_asks_only_for_what_is_missing_tops_a_component_up_and_a_rerun_asks_for_nothing()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 40), Comp(774, "Hyssop", 25)]);

        // The character already holds 15 Hyssop (a stack the kit tops up by 10) and no wand and no lead.
        var held = rig.Session.Tools.SpawnItem(774, 15, ActionSource.Host);
        rig.Run(1.5);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(held));

        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));
        Assert.True(spawn.Succeeded);
        Assert.True(spawn.Arrived);
        Assert.Equal(3, spawn.Requested);
        Assert.Equal(0, spawn.AlreadyHeld);
        Assert.Equal([(GearKit.Wand.Wcid, (int?)null), (691u, 40), (774u, 10)], spawn.Grants.Select(g => (g.Item.Wcid, g.Requested)));
        Assert.Empty(spawn.Refusals);
        Assert.Equal(3, rig.Inventory.Pack.Count); // the wand, the lead, and the 15 Hyssop with the 10 more merged into it (the server's item verb makes the 10 its own stack; SpawnGear merges it: 1.4-R2)
        Assert.Equal(25, rig.Held(774));
        Assert.Equal(1, rig.Stacks(774));
        Assert.Equal(1, spawn.Merges);

        // A rerun counts what the inventory holds (stacks summed) and requests nothing.
        var again = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));
        Assert.Equal(0, again.Requested);
        Assert.Equal(3, again.AlreadyHeld);
        Assert.True(again.Succeeded);
        Assert.True(again.Arrived);
        Assert.Equal(3, rig.Inventory.Pack.Count);
    }

    [Fact]
    public void SpawnGear_asks_for_a_piece_in_its_palette_and_a_piece_without_one_plainly()
    {
        using var rig = new Rig();
        var gold = GearKit.Armour.Single(a => a.Wcid == 57);
        var coat = GearKit.Armour.Single(a => a.Wcid == 6600);
        GearItem Line(GearWeenie w) => new(w.Wcid, w.Name, GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden, w.Palette, w.Shade);
        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(GearKit.Of([Line(coat), Line(gold)]), ActionSource.Host));
        Assert.True(spawn.Succeeded, spawn.Describe());
        var summaries = spawn.Grants.ToDictionary(g => g.Item.Wcid, g => g.Handle.Summary);
        Assert.Contains("palette=21", summaries[57], StringComparison.Ordinal);
        Assert.DoesNotContain("palette", summaries[6600], StringComparison.Ordinal);
    }

    [Fact]
    public void SpawnGear_counts_an_equipped_item_as_held_and_a_bigger_kit_tops_up_only_the_difference()
    {
        using var rig = new Rig();
        var small = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 10)]);
        Assert.True(rig.Finish(rig.Session.Tools.SpawnGear(small, ActionSource.Host)).Arrived);
        var wand = rig.Inventory.Pack.First(e => rig.Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == GearKit.Wand.Wcid).Guid;
        var wield = rig.Session.Actions.Wield(wand, EquipMask.Held, ActionSource.Host);
        rig.Run(1.5);
        Assert.Equal(ActionOutcomeKind.Confirmed, wield.Outcome!.Value.Kind);
        Assert.Equal(InventoryPlaceKind.Equipped, rig.Inventory.Find(wand).Kind);

        var bigger = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 25)]);
        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(bigger, ActionSource.Host));
        Assert.True(spawn.Arrived);
        Assert.Equal([(691u, 15)], spawn.Grants.Select(g => (g.Item.Wcid, g.Requested!.Value)));
        Assert.Equal(1, spawn.AlreadyHeld);
    }

    [Fact]
    public void SpawnGear_fails_loudly_on_a_refusal_naming_the_item_and_the_reason()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(691, "Lead Scarab", 5), Comp(99_999_999, "Not A Weenie", 5)]);

        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));

        Assert.True(spawn.IsCompleted);
        Assert.False(spawn.Succeeded);
        Assert.False(spawn.Arrived);
        Assert.Equal(ItemOutcome.Created, spawn.Grants[0].Outcome);
        Assert.Equal(ItemOutcome.UnknownWcid, spawn.Grants[1].Outcome);
        var refusal = Assert.Single(spawn.Refusals);
        Assert.StartsWith("99999999 Not A Weenie: UnknownWcid", refusal, StringComparison.Ordinal);
        Assert.Contains("refused=1", spawn.Describe(), StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("99999999 Not A Weenie", StringComparison.Ordinal));
    }

    [Fact]
    public void SpawnGear_refuses_to_count_an_inventory_whose_world_objects_have_not_arrived_and_waits_for_the_world_object()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(691, "Lead Scarab", 5)]);
        var item = rig.Session.Tools.SpawnItem(774, 3, ActionSource.Host);

        // ACE's order: the pack entry (0022) comes first and the item's CreateObject a bundle later; in that gap the entry has no weenie to count.
        GearSpawn? gap = null;
        for (var i = 0; i < 400 && gap is null; i++)
        {
            rig.Run(0.01, 0.01);
            if (rig.Inventory.Pack.Count == 1 && !rig.Session.State.Objects.Contains(rig.Inventory.Pack[0].Guid))
            {
                gap = rig.Session.Tools.SpawnGear(kit, ActionSource.Host);
            }
        }

        Assert.NotNull(gap);
        Assert.True(gap.IsCompleted);
        Assert.False(gap.Succeeded);
        Assert.StartsWith("inventory-unsettled", gap.NotStarted, StringComparison.Ordinal);
        Assert.Equal(0, gap.Requested);
        rig.Run(1.5);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(item));

        // Once the world object is there the same call counts and requests the kit; Arrived waits for the new item's world object too.
        var spawn = rig.Session.Tools.SpawnGear(kit, ActionSource.Host);
        Assert.Equal(1, spawn.Requested);
        Assert.False(spawn.Arrived);
        Assert.True(rig.Finish(spawn).Arrived);
    }

    [Fact]
    public void A_second_SpawnGear_while_the_first_is_not_done_returns_the_first_and_asks_for_nothing_more()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(691, "Lead Scarab", 5), Comp(774, "Hyssop", 5)]);
        var first = rig.Session.Tools.SpawnGear(kit, ActionSource.Host);
        var second = rig.Session.Tools.SpawnGear(kit, ActionSource.Host);
        Assert.Same(first, second);
        Assert.True(rig.Finish(first).Arrived);
        Assert.Equal(2, rig.Inventory.Pack.Count);
    }

    // ---------------------------------------------------------------- 1.4-R2: a top-up is merged; the slot guard; the focus guard (M3 1.4 fix A)

    [Fact]
    public void After_a_burn_the_top_up_is_merged_so_there_is_one_stack_per_line_cycle_after_cycle()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 40), Comp(774, "Hyssop", 25)]);
        var first = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));
        Assert.True(first.Arrived);
        Assert.Equal(0, first.Merges);
        Assert.Equal(3, rig.Inventory.Pack.Count);

        for (var cycle = 1; cycle <= 20; cycle++)
        {
            // A cast burns from the first stack of the weenie, as ACE does (Player_Magic.cs 1203-1213): the stand-in's hook for it.
            rig.World.Burn(691, 7);
            rig.World.Burn(774, 3);
            rig.Run(0.5);
            Assert.Equal((33, 22), (rig.Held(691), rig.Held(774)));

            var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));

            Assert.True(spawn.Succeeded, spawn.Describe());
            Assert.True(spawn.Arrived, spawn.Describe());
            Assert.Equal([(691u, 7), (774u, 3)], spawn.Grants.Select(g => (g.Item.Wcid, g.Requested!.Value)));
            Assert.Equal(2, spawn.Merges);
            Assert.Equal(3, rig.Inventory.Pack.Count); // one slot per line, never more: the wand, the lead, the hyssop
            Assert.Equal((40, 25), (rig.Held(691), rig.Held(774)));
            Assert.Equal((1, 1), (rig.Stacks(691), rig.Stacks(774)));
            Assert.Equal(3, rig.World.Pack.Count);
        }

        Assert.DoesNotContain(rig.Log.Entries, e => e.Severity == LogSeverity.Error);
    }

    [Fact]
    public void A_top_up_that_finds_old_fragments_merges_them_all_into_the_largest_stack()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(774, "Hyssop", 30)]);
        foreach (var n in new[] { 4, 11, 6 })
        {
            var item = rig.Session.Tools.SpawnItem(774, n, ActionSource.Host);
            rig.Run(1.0);
            Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(item));
        }

        Assert.Equal(3, rig.Stacks(774));
        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));

        Assert.True(spawn.Succeeded, spawn.Describe());
        Assert.Equal(9, Assert.Single(spawn.Grants).Requested); // 30 - 21 held
        Assert.Equal(1, rig.Stacks(774)); // the 11, the 4, the 6 and the new 9 are one stack of 30
        Assert.Equal(30, rig.Held(774));
        Assert.Equal(3, spawn.Merges);
    }

    [Fact]
    public void A_spawn_that_grants_nothing_still_merges_the_fragments_an_earlier_spawn_left()
    {
        // Live, 1.4 round 2: lines topped up before the merge existed kept two stacks, and a later spawn with nothing to grant left them so.
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(774, "Hyssop", 20)]);
        foreach (var n in new[] { 15, 5 })
        {
            var item = rig.Session.Tools.SpawnItem(774, n, ActionSource.Host);
            rig.Run(1.0);
            Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(item));
        }

        Assert.Equal(2, rig.Stacks(774));
        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));

        Assert.True(spawn.Succeeded, spawn.Describe());
        Assert.Equal(0, spawn.Requested);
        Assert.Equal(1, spawn.Merges);
        Assert.Equal(1, rig.Stacks(774));
        Assert.Equal(20, rig.Held(774));
    }

    [Fact]
    public void A_merge_the_server_refuses_fails_the_spawn_loudly_and_names_it()
    {
        using var rig = new Rig();
        var kit = GearKit.Of([Comp(774, "Hyssop", 25)]);
        Assert.True(rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host)).Arrived);
        rig.World.Burn(774, 10);
        rig.Run(0.5);
        rig.World.RefusedMergeWcids.Add(774);

        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(kit, ActionSource.Host));

        Assert.True(spawn.IsDone);
        Assert.False(spawn.Succeeded);
        Assert.NotNull(spawn.MergeFailure);
        Assert.Contains("ServerError", spawn.MergeFailure, StringComparison.Ordinal);
        Assert.Contains("merge of 10", spawn.MergeFailure, StringComparison.Ordinal);
        Assert.Contains("merged=0/1", spawn.Describe(), StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("SpawnGear: merge of 10", StringComparison.Ordinal));
    }

    [Fact]
    public void The_slot_guard_refuses_before_the_server_when_the_pack_and_the_new_grants_would_pass_the_capacity()
    {
        using var rig = new Rig();
        for (var i = 0; i < GearKit.PackCapacity - 1; i++)
        {
            rig.Session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host); // 101 wands: 101 of 102 slots used
        }

        rig.Run(60);
        Assert.Equal(GearKit.PackCapacity - 1, rig.Inventory.Pack.Count);
        var kit = GearKit.Of([Comp(691, "Lead Scarab", 40), Comp(774, "Hyssop", 25)]);

        var spawn = rig.Session.Tools.SpawnGear(kit, ActionSource.Host);

        Assert.StartsWith("slot-guard", spawn.GuardRefusal, StringComparison.Ordinal);
        Assert.Contains("101 of 102", spawn.GuardRefusal, StringComparison.Ordinal);
        Assert.Contains("needs 2 new", spawn.GuardRefusal, StringComparison.Ordinal);
        Assert.Equal(0, spawn.Requested);
        Assert.True(spawn.IsCompleted);
        Assert.False(spawn.Succeeded);
        Assert.True(spawn.IsDone);
        Assert.Contains("slot-guard", spawn.Describe(), StringComparison.Ordinal);
        Assert.Contains(spawn.Refusals, r => r.StartsWith("slot-guard", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("slot-guard", StringComparison.Ordinal));
        rig.Run(2);
        Assert.Equal(GearKit.PackCapacity - 1, rig.Inventory.Pack.Count); // nothing was asked of the server

        // One new grant still fits (101 + 1 = 102), and a merge frees the slot again.
        var one = GearKit.Of([Comp(774, "Hyssop", 25)]);
        var fits = rig.Finish(rig.Session.Tools.SpawnGear(one, ActionSource.Host));
        Assert.Null(fits.GuardRefusal);
        Assert.True(fits.Succeeded);
        Assert.Equal(GearKit.PackCapacity, rig.Inventory.Pack.Count);
    }

    [Fact]
    public void A_character_holding_a_focus_item_spawns_the_kit_and_the_log_names_the_focus()
    {
        using var rig = new Rig(new Dictionary<uint, StandInItemKind> { [15271] = new("Foci of Strife", null) });
        var focus = rig.Session.Tools.SpawnItem(15271, null, ActionSource.Host);
        rig.Run(1.5);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(focus));

        var spawn = rig.Finish(rig.Session.Tools.SpawnGear(GearKit.Of([Comp(774, "Hyssop", 25)]), ActionSource.Host));

        Assert.Null(spawn.GuardRefusal);
        Assert.True(spawn.Succeeded, spawn.Describe());
        Assert.Equal(1, spawn.Requested);
        Assert.Equal(25, rig.Held(774));
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Info && e.Message.Contains("Foci of Strife (War Magic)", StringComparison.Ordinal) && e.Message.Contains("15271", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Severity == LogSeverity.Error);
    }

    [RequiresDatsFact]
    public void The_kit_holds_the_foci_formulas_components_of_every_profile_spell()
    {
        var kit = KitFor(Account);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var cast = GearKit.CastBurns(Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround), spells, heroRing: false).Select(b => b.SpellId).ToHashSet(); // M5 3 m1: the spells the mage casts (the plan's levels), not every level of every line
        foreach (var spell in ArenaProfile.Lines.SelectMany(l => l.Spells).Where(sp => cast.Contains(sp.Id)))
        {
            var counts = GearKit.FociFormulaCounts(spells[spell.Id].Formula);
            Assert.NotEmpty(counts);
            foreach (var (id, n) in counts)
            {
                Assert.True(ComponentWeenies.TryGetByComponent(id, out var weenie), $"component {id} of the foci formula of {spell.Name}");
                Assert.True(kit.ItemOf(weenie.Wcid) is { } line && line.Stack >= n, $"{spell.Name}: the kit holds {n} of {weenie.Name}");
            }

            // ACE GetFociFormula: the scarabs and chorizite of the base formula, then 1-4 Prismatic Tapers by the power: at least one of them for every profile spell.
            Assert.True(counts.GetValueOrDefault(SpellFormulaRules.PrismaticTaperId) is >= 1 and <= 4, spell.Name);
        }
    }

    [Fact]
    public void The_foci_formula_is_the_scarabs_chorizite_and_the_tapers_by_power()
    {
        // Lead Scarab (1, power 1) with a coloured taper: one scarab and one Prismatic Taper. Platinum (4: power 4) and chorizite (111): scarab, chorizite, three tapers.
        Assert.Equal(new Dictionary<uint, int> { [1] = 1, [188] = 1 }, GearKit.FociFormulaCounts([1, 63]));
        Assert.Equal(new Dictionary<uint, int> { [4] = 1, [111] = 1, [188] = 3 }, GearKit.FociFormulaCounts([4, 111, 70]));
    }

    [Fact]
    public void The_five_focus_items_are_ACE_s_FociWCIDs()
    {
        // Player_Spells.cs 451-457: Enchantment, Artifice, Verdancy, Strife, Shadow.
        Assert.Equal([15268u, 15269u, 15270u, 15271u, 43173u], GearKit.FociWcids);
    }

    /// <summary>M6 2 b (the map's finding 4): the gear floor's lines (a Wand, a robe, a charge) are the resupply's, never <c>supplies.kit</c>: the outskirts stay kit-less, so the kit-less death keeps <c>rearm=corpse</c> or <c>none</c>.</summary>
    [Fact]
    public void M6_2_b_HasNoKit_stays_true_for_the_outskirts_with_floor_lines_planned_and_the_kit_is_Bare()
    {
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("holtburg-outskirts");
        var plan = GearFloor.Plan(ground, new GearFloorState(10, 0, false, null, 10_000), GearFloor.FloorLevel(Lifestone.Client.Session.Grounds.LadderLoader.WarMage));

        Assert.Equal(3, plan.Lines.Count); // two Wands and the hooded robe
        Assert.Empty(ground.Supplies.Kit);
        Assert.True(GearKit.HasNoKit(ground));
        Assert.Same(GearKit.Bare, GearKit.BuildFor(new Dictionary<uint, SpellInfo>(), "kit-test-account", ground));
        Assert.All(plan.Lines, l => Assert.Null(ground.Supplies.KitOf(l.Wcid)));
    }

    /// <summary>M6 3 F1 (review 3-R1): every ground a non-pending rung or live alternate of the war mage's ladder names builds a kit through <see cref="GearKit.BuildFor"/> (the bare kit for a profile with no kit, dungeon or outdoor); before the fix nine dungeons threw <c>ground-plan-gap</c>.</summary>
    [RequiresDatsFact]
    public void M6_3_F1_BuildFor_builds_a_kit_for_the_ground_of_every_live_rung_and_alternate()
    {
        var keys = new List<string>();
        foreach (var rung in Lifestone.Client.Session.Grounds.LadderLoader.WarMage.Rungs.Where(r => !r.Pending && r.GroundKey is not null))
        {
            keys.Add(rung.GroundKey!);
            keys.AddRange(rung.Alternates.Where(a => !a.Pending).Select(a => a.GroundKey));
        }

        Assert.Contains("tusker-lodge", keys); // M6 7 AC: the Burrow rung is dropped; its kit-less dungeon is still built below (the dungeon test) and by the catalog case
        Assert.Contains("tusker-holding", keys);
        foreach (var key in keys.Distinct())
        {
            var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(key);
            var kit = GearKit.BuildFor(SpellTableData.Load(fixture.Dats).Spells, "kit-test-account", ground);
            if (ground.Supplies.Kit.Count == 0)
            {
                Assert.Same(GearKit.Bare, kit);
                Assert.True(GearKit.HasNoKit(ground), key);
            }
            else
            {
                Assert.False(GearKit.HasNoKit(ground), key);
            }
        }
    }

    /// <summary>M6 3 F1 (3-R1): a dungeon whose profile lists no kit is kit-less like an outdoor ground, and the Build rules stand for a ground that has one.</summary>
    [Fact]
    public void M6_3_F1_HasNoKit_reads_the_profile_not_the_ground_kind()
    {
        var dungeon = Lifestone.Client.Session.Grounds.GroundCatalog.Get("tusker-burrow");
        Assert.Empty(dungeon.Supplies.Kit);
        Assert.False(Lifestone.Client.Session.Grounds.FightTargets.IsOutdoorFight(dungeon));
        Assert.True(GearKit.HasNoKit(dungeon));
        Assert.Throws<InvalidOperationException>(() => GearKit.Build(new Dictionary<uint, SpellInfo>(), "kit-test-account", ground: dungeon));
        Assert.False(GearKit.HasNoKit(Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround)));
    }

    /// <summary>M6 2 b: the floor's weenie lookup never throws: a supply weenie (the Wand, the robes) or null (a component, a charge, a note).</summary>
    [Fact]
    public void M6_2_b_FloorWeenieOf_reads_the_supply_weenies_and_is_null_for_anything_else()
    {
        Assert.Equal((GearKit.ItemTypeCaster, 200), (GearKit.FloorWeenieOf(2472)!.ItemType, GearKit.FloorWeenieOf(2472)!.Value));
        Assert.Equal((EquipMask)0x7F01, GearKit.FloorWeenieOf(5851)!.ValidLocations);
        Assert.Null(GearKit.FloorWeenieOf(20631));
        Assert.Null(GearKit.FloorWeenieOf(4615));
    }
}

/// <summary>The supply weenies' pinned slots and values, offline (review 3-R12 and 3-R17; the SQL-backed re-read is <c>SupplyWeenieSqlTests</c>).</summary>
public sealed class SupplyWeenieOfflineTests
{
    /// <summary>M5 7 F13 (D87, the kit): the hooded robe's wear locations include the head and the gauntlets' the hands (the pinned world SQL: 5851 ClothingPriority 0x17F00 and ValidLocations 0x7F01; 56 ClothingPriority 0x8000 and ValidLocations 0x20; the SQL-backed re-read of the coverage is <c>SupplyWeenieSqlTests</c>); the den's worn lines are exactly the two and nothing else covers the head or the hands.</summary>
    [Fact]
    public void The_dens_worn_kit_is_a_hooded_robe_and_gloves_the_robe_covers_the_head_and_the_gauntlets_the_hands()
    {
        var den = Lifestone.Client.Session.Grounds.GroundCatalog.Get("black-spawn-den-aluvian");
        var worn = den.Supplies.Kit.Where(k => k.Slot == Lifestone.Client.Session.Grounds.KitSlot.Worn).Select(k => GearKit.SupplyWeenieOf(k.Wcid, den, "kit")).ToList();
        Assert.Equal([5851u, 56u], worn.Select(w => w.Wcid));
        var robe = worn[0];
        var gloves = worn[1];
        Assert.True(robe.ValidLocations.HasFlag(Lifestone.Client.Session.Contracts.EquipMask.HeadWear), "the hooded robe covers the head");
        Assert.Equal(Lifestone.Client.Session.Contracts.EquipMask.HandWear, gloves.ValidLocations);
        Assert.Equal(GearKit.ItemTypeClothing, robe.ItemType);
        Assert.Equal(GearKit.ItemTypeArmor, gloves.ItemType);
        Assert.Equal(0, robe.ItemDifficulty + gloves.ItemDifficulty); // nothing to wield: no Arcane Lore, no skill
        Assert.Equal((Lifestone.Client.Session.Contracts.EquipMask)0x7F00, robe.ValidLocations & (Lifestone.Client.Session.Contracts.EquipMask)0x7F00); // and everything the old robe covered
        Assert.Equal(Lifestone.Client.Session.Contracts.EquipMask.None, robe.ValidLocations & gloves.ValidLocations); // the two do not take one slot: both are worn at once
    }

    [Fact]
    public void The_robes_and_the_wand_keep_their_pinned_slots_and_each_vendor_prices_them_at_one_sell_rate_over_their_value()
    {
        var robe = GearKit.SupplyWeenies.Single(w => w.Wcid == 6071);
        var wand = GearKit.SupplyWeenies.Single(w => w.Wcid == 2472);
        Assert.Equal((Lifestone.Client.Session.Contracts.EquipMask)0x7F00, robe.ValidLocations); // chest, abdomen, arms, legs, feet: the pinned world SQL's 32512
        Assert.Equal(Lifestone.Client.Session.Contracts.EquipMask.Held, wand.ValidLocations); // 16777216
        Assert.Equal(200, wand.Value); // TODO(m4-caster-value) closed for the vendor wand

        // The profiles' prices are an independent source: a vendor's SellPrice is one rate (ACE Vendor.GetSellCost, Vendor.cs 577-585 @95ef426d: max(1, ceil(rate x Value - 0.1)), in float), 1.35 for the den's
        // vendors and 1.8 for Fort Tethana's, so every supply weenie they sell must be priced so over the Value read from the SQL.
        foreach (var (key, rate) in new[] { ("black-spawn-den-aluvian", 1.35), ("tusker-temple-fort-tethana", 1.8) })
        {
            var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(key);
            var checkedWares = 0;
            foreach (var ware in ground.Vendors.SelectMany(v => v.Sells))
            {
                if (ground.Supplies.Purse.Notes.Any(n => n.Wcid == ware.Wcid) || GearKit.SupplyWeenies.FirstOrDefault(w => w.Wcid == ware.Wcid) is not { Value: > 1 } weenie) // M5 7 G1: a trade note is sold at 1.15 times its face, not the vendor's rate
                {
                    continue;
                }

                Assert.Equal(Math.Max(1, (int)Math.Ceiling(((float)rate * weenie.Value) - 0.1)), ware.Price);
                checkedWares++;
            }

            Assert.True(checkedWares >= 3, $"{key}: the robe, the wand and the Mana Potion are sold");
        }
    }
}

/// <summary>A fact that runs only when the pinned world SQL zip (server/runtime/downloads, ACE-World-16PY-Patches v0.9.296) is on disk in this checkout or one above it; it is reported skipped otherwise.</summary>
public sealed class RequiresPinnedWorldSqlFactAttribute : FactAttribute
{
    public const string ZipRelativePath = "server/runtime/downloads/ace-world-v0.9.296/ACE-World-Database-v0.9.296.sql.zip";

    public RequiresPinnedWorldSqlFactAttribute()
    {
        if (Find() is null)
        {
            Skip = "The pinned world SQL zip is not on disk (" + ZipRelativePath + "): run server/setup-server.ps1 in the main checkout.";
        }
    }

    public static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ZipRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// Review 3-R12 / 3-R17 (M5 item 3 fix F3): every hand-typed fact of <see cref="GearKit.SupplyWeenies"/> against the pinned world SQL (a read-only parse of the zip): the WeenieType, the ItemType (PropertyInt 1), the burden (5), the
/// ValidLocations (9: the robes' 0x7F00 and the wand's Held), the stack size (11), the Arcane Lore (109) and the Value (19: the vendor wand's 200). Exhaustive tier: it reads about 100 MB of text.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class SupplyWeenieSqlTests
{
    [RequiresPinnedWorldSqlFact]
    public void Every_supply_weenie_fact_is_what_the_pinned_world_SQL_holds()
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        using var reader = new StreamReader(zip.Entries.First(e => e.Name.EndsWith(".sql", StringComparison.Ordinal)).Open());
        var sql = reader.ReadToEnd();
        var ints = Block(sql, "INSERT INTO `weenie_properties_int` VALUES");
        var weenies = Block(sql, "INSERT INTO `weenie` VALUES");
        foreach (var w in GearKit.SupplyWeenies)
        {
            var props = System.Text.RegularExpressions.Regex.Matches(ints, @"\(\d+," + w.Wcid + @",(\d+),(-?\d+)\)")
                .ToDictionary(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            var row = System.Text.RegularExpressions.Regex.Match(weenies, @"\(" + w.Wcid + @",'[^']*',(\d+),");
            Assert.True(row.Success, $"weenie {w.Wcid} {w.Name} is not in the pinned world SQL");
            Assert.True(w.WeenieType == uint.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), $"{w.Wcid} {w.Name}: WeenieType {w.WeenieType} vs the SQL's {row.Groups[1].Value}");
            Assert.True(w.ItemType == (uint)props[1], $"{w.Wcid} {w.Name}: ItemType 0x{w.ItemType:X} vs the SQL's 0x{props[1]:X}");
            Assert.True(w.UnitBurden == props[5], $"{w.Wcid} {w.Name}: burden {w.UnitBurden} vs the SQL's {props[5]}");
            Assert.True((uint)w.ValidLocations == (props.TryGetValue(9, out var locations) ? (uint)locations : 0u), $"{w.Wcid} {w.Name}: ValidLocations 0x{(uint)w.ValidLocations:X} vs the SQL's 0x{(props.TryGetValue(9, out var l2) ? l2 : 0):X}");
            Assert.True(w.MaxStack == (props.TryGetValue(11, out var stack) ? (int?)stack : null), $"{w.Wcid} {w.Name}: stack {w.MaxStack} vs the SQL's {(props.TryGetValue(11, out var s2) ? (int?)s2 : null)}");
            Assert.True(w.ItemDifficulty == (props.TryGetValue(109, out var lore) ? lore : 0), $"{w.Wcid} {w.Name}: Arcane Lore {w.ItemDifficulty} vs the SQL's {(props.TryGetValue(109, out var l3) ? l3 : 0)}");
            Assert.True(w.Value == props[19], $"{w.Wcid} {w.Name}: Value {w.Value} vs the SQL's {props[19]}");
        }

        // M5 7 F13 (D87): the coverage itself, ClothingPriority (PropertyInt 4, ACE's CoverageMask): the hood 0x4000 and the hands 0x8000, read from the SQL and not from our own table.
        const int Head = 0x4000, Hands = 0x8000, BodyAndLegs = 0x3F00;
        Assert.True((props_of(ints, 5851)[4] & Head) != 0 && (props_of(ints, 5851)[4] & BodyAndLegs) == BodyAndLegs, "5851 covers the head and everything the old robe did");
        Assert.True((props_of(ints, 6071)[4] & Head) == 0, "the old War robe 6071 has no hood");
        Assert.True((props_of(ints, 56)[4] & Hands) != 0 && (props_of(ints, 56)[4] & Head) == 0, "56 covers the hands");
        Assert.Equal(130, props_of(ints, 56)[28]); // the gauntlets' ArmorLevel
        Assert.False(props_of(ints, 5851).ContainsKey(158) || props_of(ints, 56).ContainsKey(158), "neither piece has a WieldRequirements row: nothing the mage must meet");

        var robe = GearKit.SupplyWeenies.First(w => w.Wcid == 6071);
        var wand = GearKit.SupplyWeenies.First(w => w.Wcid == 2472);
        Assert.Equal((EquipMask)0x7F00, robe.ValidLocations); // the pinned pair, stated: chest, abdomen, arms, legs, feet
        Assert.Equal(EquipMask.Held, wand.ValidLocations);
        Assert.Equal(200, wand.Value); // TODO(m4-caster-value) closed for the vendor wand
    }

    private static Dictionary<int, int> props_of(string ints, uint wcid) =>
        System.Text.RegularExpressions.Regex.Matches(ints, @"\(\d+," + wcid + @",(\d+),(-?\d+)\)")
            .ToDictionary(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Every chunk of the dump's <paramref name="start"/> statement (the dump splits a table's rows over several INSERTs), joined.</summary>
    private static string Block(string sql, string start)
    {
        var all = new System.Text.StringBuilder();
        for (var at = sql.IndexOf(start, StringComparison.Ordinal); at >= 0; at = sql.IndexOf(start, at + start.Length, StringComparison.Ordinal))
        {
            var end = sql.IndexOf("\nINSERT INTO", at + start.Length, StringComparison.Ordinal);
            all.Append(sql, at, (end < 0 ? sql.Length : end) - at);
        }

        Assert.True(all.Length > 0, start + " is not in the pinned world SQL");
        return all.ToString();
    }
}
