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

    // The component ids the profile's 247 spells hold for Account (SpellFormulaRules.CustomizeForAccount): scarabs, herbs, powders, potions, talismans, all twelve coloured tapers, Platinum and Mana.
    private static readonly uint[] PinnedComponentIds =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 13, 15, 17, 18, 25, 26, 30, 33, 34, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 55, 56, 57, 60, 61, 62,
        63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 112, 193,
    ];

    // Weenie class id -> stack, in kit order, for Account (the rule in GearKit's remarks).
    private const string PinnedStacks =
        "691:5 689:5 686:6 688:9 687:12 690:18 774:87 775:5 778:38 776:9 780:6 772:23 779:31 777:31 782:17 783:84 626:9 789:17 790:41 792:6 753:5 754:6 755:49 756:11 757:9 758:6 759:41 760:6 761:16 762:33 763:5 764:9 741:5 748:5 740:5 750:5 751:9 743:5 "
        + "1650:100 1649:71 1648:81 1653:100 1645:100 1654:48 1643:100 1647:64 1651:100 1644:100 1652:100 1646:66 8897:5 20631:682 37155:5";

    private GearKit KitFor(string account) => GearKit.Build(SpellTableData.Load(fixture.Dats).Spells, account);

    [RequiresDatsFact]
    public void The_customised_component_list_for_a_fixed_account_is_pinned()
    {
        var kit = KitFor(Account);
        output.WriteLine(kit.Describe());
        Assert.Equal(PinnedComponentIds, kit.CustomisedComponentIds);
        Assert.Equal(PinnedStacks, string.Join(' ', kit.Components.Select(c => c.Wcid + ":" + c.Stack)));
        Assert.Equal(53, kit.Components.Count());
        Assert.Equal(59, kit.Slots);
        Assert.Equal(52, kit.CustomisedComponentIds.Count);
    }

    [RequiresDatsFact]
    public void The_account_changes_the_tapers_and_the_name_is_lower_cased_as_ACE_stores_it()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var kit = GearKit.Build(spells, Account);
        Assert.Equal(PinnedStacks, string.Join(' ', GearKit.Build(spells, Account.ToUpperInvariant()).Components.Select(c => c.Wcid + ":" + c.Stack)));

        // Another account hashes to other taper colours, so the taper stacks differ while everything but the tapers is the same.
        var other = GearKit.Build(spells, "another-account");
        var tapers = kit.Components.Where(c => ComponentWeenies.TryGetByWcid(c.Wcid, out var w) && ComponentWeenies.IsColouredTaper(w.ComponentId)).Select(c => c.Stack).ToArray();
        var otherTapers = other.Components.Where(c => ComponentWeenies.TryGetByWcid(c.Wcid, out var w) && ComponentWeenies.IsColouredTaper(w.ComponentId)).Select(c => c.Stack).ToArray();
        Assert.NotEqual(tapers, otherTapers);
        Assert.Equal(
            kit.Components.Where(c => !IsTaper(c)).Select(c => c.Wcid + ":" + c.Stack),
            other.Components.Where(c => !IsTaper(c)).Select(c => c.Wcid + ":" + c.Stack));
    }

    private static bool IsTaper(GearItem item) => ComponentWeenies.TryGetByWcid(item.Wcid, out var w) && w.ComponentId is >= 63 and <= 74;

    [RequiresDatsFact]
    public void Every_taper_colour_and_the_foci_formulas_prismatic_taper_are_in_the_kit()
    {
        var kit = KitFor(Account);
        var taperWcids = Enumerable.Range(63, 12).Select(id => ComponentWeenies.TryGetByComponent((uint)id, out var w) ? w.Wcid : 0).ToArray();
        Assert.DoesNotContain(0u, taperWcids);
        Assert.All(taperWcids, wcid => Assert.NotNull(kit.ItemOf(wcid)));
        Assert.Equal(12, taperWcids.Distinct().Count());
        Assert.Equal((GearKit.PrismaticTaperWcid, GearKit.PrismaticTaperBurden, 1000), (kit.ItemOf(20631)!.Wcid, kit.ItemOf(20631)!.UnitBurden, kit.ItemOf(20631)!.MaxStack!.Value)); // the foci formula's taper (SpellFormula.cs:378): BSD Mage holds a focus
        Assert.DoesNotContain(188u, kit.CustomisedComponentIds); // not part of any account's customised formula
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
            Assert.InRange(c.Stack, GearKit.MinimumStack, c.MaxStack!.Value);
            Assert.Equal(c.Wcid == GearKit.PrismaticTaperWcid ? GearKit.PrismaticTaperBurden : GearKit.ComponentBurden, c.UnitBurden);
        }

        // A formula can hold a component twice (ACE needs as many in the pack as the slots, Player_Magic.cs:1234-1245): no stack is below the most any one profile spell holds.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var spell in ArenaProfile.Lines.SelectMany(l => l.Spells))
        {
            var info = spells[spell.Id];
            foreach (var group in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, Account).Where(id => id != 0).GroupBy(id => id))
            {
                ComponentWeenies.TryGetByComponent(group.Key, out var weenie);
                Assert.True(kit.ItemOf(weenie.Wcid)!.Stack >= group.Count(), $"spell {spell.Id} needs {group.Count()} of {weenie.Name}");
            }
        }
    }

    [RequiresDatsFact]
    public void The_kit_stays_well_under_the_burden_capacity_and_the_pack_slots_and_the_numbers_are_printed()
    {
        var kit = KitFor(Account);
        var capacity = GearKit.EncumbranceCapacity(GearKit.CapacityStrength);
        output.WriteLine($"kit: {kit.Describe()}; capacity at strength {GearKit.CapacityStrength} = {capacity}; wand+armour burden = {kit.Items.Where(i => i.Kind != GearKind.Component).Sum(i => i.Burden)}; components = {kit.Components.Sum(c => c.Stack)} units");
        Assert.Equal(19_650, capacity);
        Assert.Equal(5_997, kit.Items.Where(i => i.Kind != GearKind.Component).Sum(i => i.Burden)); // M3 4.2 P7 (D43): the Healer's Heart 50 + helm 600 + coat 1,600 + leggings 2,288 + gauntlets 919 + sollerets 540
        // M3 2.1 s0a: the two mana lines (D24) put the components at 11,008 (the leather kit's 13,618 less its 2,610); with the classic armour the kit is 17,005, 86.5% of the conservative capacity.
        Assert.True(kit.Burden * 100 <= capacity * 90, $"the kit's burden {kit.Burden} is over 90% of {capacity}");
        Assert.Equal(17_005, kit.Burden);
        Assert.True(kit.Slots <= GearKit.PackCapacity * 6 / 10, $"{kit.Slots} slots of {GearKit.PackCapacity}");
        Assert.Equal(kit.Items.Sum(i => i.Burden), kit.Burden);
        var tapers = kit.ItemOf(GearKit.PrismaticTaperWcid)!.Stack;
        Assert.Equal(5_997 + (4 * (kit.Components.Sum(c => c.Stack) - tapers)) + (6 * tapers), kit.Burden); // exact: every component weenie weighs 4 but the Prismatic Taper's 6 (the SELECT; TODO(m3-kit-burdens) closed)
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
        Assert.True(carried <= conservative, $"carrying {carried} is over the conservative capacity {conservative}");
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
        Assert.Empty(ArenaProfile.TargetWcids.Intersect(GearKit.Armour.Select(a => a.Wcid).Append(GearKit.Wand.Wcid).Concat(ComponentWeenies.All.Select(c => c.Wcid))));
    }

    [RequiresDatsFact]
    public void With_the_orb_in_hand_the_kit_still_carries_every_component_the_customised_formulas_and_the_foci_formula_need()
    {
        // The earlier held item sat beside a focus (the live mage holds the Foci of Strife in its pack): a focus switches ACE's casts to the foci formula. The orb is not one, so the casts use the account's customised formulas.
        var kit = KitFor(Account);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(GearKind.Wand, kit.WandItem.Kind);
        Assert.Equal(GearKit.Wand.Wcid, kit.WandItem.Wcid);
        foreach (var id in kit.CustomisedComponentIds)
        {
            ComponentWeenies.TryGetByComponent(id, out var weenie);
            Assert.NotNull(kit.ItemOf(weenie.Wcid));
        }

        foreach (var spell in ArenaProfile.Lines.SelectMany(l => l.Spells))
        {
            var info = spells[spell.Id];
            foreach (var group in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, Account).Where(i => i != 0).GroupBy(i => i))
            {
                ComponentWeenies.TryGetByComponent(group.Key, out var weenie);
                Assert.True(kit.ItemOf(weenie.Wcid)!.Stack >= group.Count(), $"spell {spell.Id}: the customised formula needs {group.Count()} of {weenie.Name}");
            }

            foreach (var (id, count) in GearKit.FociFormulaCounts(info.Formula))
            {
                ComponentWeenies.TryGetByComponent(id, out var weenie);
                Assert.True(kit.ItemOf(weenie.Wcid)!.Stack >= count, $"spell {spell.Id}: the foci formula (a focus held later) needs {count} of {weenie.Name}");
            }
        }
    }

    [RequiresDatsFact]
    public void Every_line_of_the_built_kit_is_an_item_type_the_select_names()
    {
        var kit = KitFor(Account);
        Assert.All(kit.Items, i => Assert.Contains(i.WeenieType, new[] { GearKit.WeenieTypeCaster, GearKit.WeenieTypeClothing, GearKit.WeenieTypeSpellComponent }));
        Assert.DoesNotContain(kit.Items, i => i.WeenieType == 10); // WeenieType.Creature
        Assert.Equal(1, kit.Items.Count(i => i.Kind == GearKind.Wand));
        Assert.Equal(5, kit.Items.Count(i => i.Kind == GearKind.Armour));
    }

    [Fact]
    public void A_profile_spell_the_table_lacks_fails_the_build_by_name()
    {
        var e = Assert.Throws<KeyNotFoundException>(() => GearKit.Build(new Dictionary<uint, SpellInfo>(), Account));
        Assert.Contains("is not in the SpellTable", e.Message, StringComparison.Ordinal);
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
        foreach (var spell in ArenaProfile.Lines.SelectMany(l => l.Spells))
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


    // ---------------------------------------------------------------- M4 3.1 d0 (D20): the decoys join the death loop's kit only

    private static Dictionary<uint, StandInItemKind> DecoyWeenies() => DecoyKit.All.ToDictionary(d => d.Wcid, d => new StandInItemKind(d.Name, null));

    [RequiresDatsFact]
    public void A_plain_kit_is_what_it_was_and_the_decoy_kit_is_the_plain_kit_with_eight_decoy_lines_at_the_end()
    {
        var plain = KitFor(Account);

        // The plain kit (the buff, fight and login legs' kit) has no decoy and describes itself as it always did.
        Assert.False(plain.HasDecoys);
        Assert.Empty(plain.DecoyItems);
        Assert.Equal(59, plain.Items.Count);
        Assert.Equal(17_005, plain.Burden);
        Assert.Equal("items=59 components=53 customised_components=52 slots=59/102 burden=17005/19650", plain.Describe());
        Assert.Equal(PinnedStacks, string.Join(' ', plain.Components.Select(c => c.Wcid + ":" + c.Stack)));

        var decoyed = plain.WithDecoys(DecoyKit.All);
        output.WriteLine("decoy kit: " + decoyed.Describe());

        Assert.True(decoyed.HasDecoys);
        Assert.Equal(67, decoyed.Items.Count);
        Assert.Equal(plain.Items, decoyed.Items.Take(59)); // the plain lines, unchanged and in the same order
        Assert.Equal(DecoyKit.All.Select(d => d.Wcid), decoyed.Items.Skip(59).Select(i => i.Wcid));
        Assert.All(decoyed.Items.Skip(59), i => Assert.Equal(GearKind.Decoy, i.Kind));
        Assert.Equal(DecoyKit.All.Select(d => d.ToGearItem()), decoyed.DecoyItems);
        Assert.Equal(67, decoyed.Slots);
        Assert.Equal(17_005 + 452, decoyed.Burden);
        Assert.Equal(17_457, decoyed.Burden);
        Assert.Equal(plain.CustomisedComponentIds, decoyed.CustomisedComponentIds);
        Assert.Equal(53, decoyed.Components.Count());
        Assert.Equal(5, decoyed.ArmourItems.Count());
        Assert.Equal(plain.WandItem, decoyed.WandItem);
        Assert.Equal("items=67 components=53 customised_components=52 slots=67/102 burden=17457/19650 decoys=8", decoyed.Describe());

        // The plain kit was not changed by building the other, and the decoys left it by value.
        Assert.False(plain.HasDecoys);
        Assert.Equal(59, plain.Items.Count);
    }

    [RequiresDatsFact]
    public void The_decoy_kit_stays_under_the_capacity_and_the_pack_slots_and_a_mage_that_kept_the_leather_set_and_the_old_wand_still_fits_under_its_real_capacity()
    {
        var kit = KitFor(Account).WithDecoys(DecoyKit.All);
        var conservative = GearKit.EncumbranceCapacity(GearKit.CapacityStrength);
        var real = EncumbranceSystem.EncumbranceCapacity((int)(MageTemplate.LowAttribute + GearKit.CapacityStrength), 0);
        const int OldWand = 50;
        var carried = kit.Burden + GearKit.LeatherSetBurden + OldWand;
        output.WriteLine($"decoy kit {kit.Burden} of {conservative} (conservative); + leather {GearKit.LeatherSetBurden} + old wand {OldWand} = {carried} of {real} at strength 141 ({(double)carried / real:P1})");

        Assert.True(kit.Burden * 100 <= conservative * 90, $"the decoy kit's burden {kit.Burden} is over 90% of {conservative}");
        Assert.True(kit.Slots <= GearKit.PackCapacity * 3 / 4, $"{kit.Slots} slots of {GearKit.PackCapacity}");
        Assert.True(carried * 100 <= real * 95, $"carrying {carried} is over 95% of {real}");
        Assert.True(EncumbranceSystem.Load(real, carried) < 1.0);
    }

    [RequiresDatsFact]
    public void ForDeathLoop_is_the_decoy_kit_with_the_option_on_and_the_plain_kit_with_it_off_and_counts_the_decoys_for_the_level()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var plain = GearKit.Build(spells, Account);

        var on = GearKit.ForDeathLoop(spells, Account, new DeathOptions());
        Assert.Equal(plain.WithDecoys(DecoyKit.All).Items, on.Items); // BSD Mage's level 126: all eight

        var off = GearKit.ForDeathLoop(spells, Account, new DeathOptions { Decoys = false });
        Assert.False(off.HasDecoys);
        Assert.Equal(plain.Items, off.Items);
        Assert.Equal(plain.Describe(), off.Describe());

        Assert.Single(GearKit.ForDeathLoop(spells, Account, new DeathOptions(), level: 15).DecoyItems);
        Assert.Equal(3, GearKit.ForDeathLoop(spells, Account, new DeathOptions(), level: 21).DecoyItems.Count());
        Assert.False(GearKit.ForDeathLoop(spells, Account, new DeathOptions(), level: 5).HasDecoys);
        Assert.Equal(8, GearKit.ForDeathLoop(spells, Account, new DeathOptions(), level: 400).DecoyItems.Count());
        Assert.Throws<ArgumentNullException>(() => GearKit.ForDeathLoop(spells, Account, null!));
    }

    [Fact]
    public void WithDecoys_replaces_the_decoys_it_has_is_idempotent_and_leaves_the_kit_it_was_called_on_alone()
    {
        var plain = GearKit.Of([Wand(), Comp(691, "Lead Scarab", 40)]);

        var one = plain.WithDecoys(DecoyKit.All.Take(1).ToList());
        var eight = one.WithDecoys(DecoyKit.All);
        var again = eight.WithDecoys(DecoyKit.All);
        var none = eight.WithDecoys([]);

        Assert.Equal(2, plain.Items.Count);
        Assert.False(plain.HasDecoys);
        Assert.Equal(3, one.Items.Count);
        Assert.Equal(10, eight.Items.Count);
        Assert.Equal(eight.Items, again.Items);
        Assert.Equal(plain.Items, none.Items);
        Assert.False(none.HasDecoys);
        Assert.Equal(plain.Burden + DecoyKit.TotalBurden, eight.Burden);
        Assert.Equal("items=10 components=1 customised_components=0 slots=10/102 burden=" + eight.Burden + "/19650 decoys=8", eight.Describe());
        Assert.Equal(DecoyKit.For(126).Select(d => d.Wcid), plain.WithDecoys(126).DecoyItems.Select(i => i.Wcid));
        Assert.Throws<ArgumentNullException>(() => plain.WithDecoys((IReadOnlyList<DecoyItem>)null!));
    }

    [Fact]
    public void SpawnGear_asks_for_exactly_the_decoys_not_held_one_each_and_a_rerun_asks_for_none()
    {
        using var rig = new Rig(DecoyWeenies());
        var three = GearKit.Of([Wand()]).WithDecoys(DecoyKit.All.Take(3).ToList());
        var first = rig.Finish(rig.Session.Tools.SpawnGear(three, ActionSource.Host));
        Assert.True(first.Succeeded, first.Describe());
        Assert.Equal(4, first.Requested); // the wand and three decoys
        Assert.All(DecoyKit.All.Take(3), d => Assert.Equal(1, rig.Held(d.Wcid)));

        var all = GearKit.Of([Wand()]).WithDecoys(DecoyKit.All);
        var second = rig.Finish(rig.Session.Tools.SpawnGear(all, ActionSource.Host));

        Assert.True(second.Succeeded, second.Describe());
        Assert.True(second.Arrived);
        Assert.Equal(5, second.Requested); // the five decoys not held, none of the three that are, and not the wand
        Assert.Equal(4, second.AlreadyHeld);
        Assert.Equal(DecoyKit.All.Skip(3).Select(d => d.Wcid), second.Grants.Select(g => g.Item.Wcid));
        Assert.All(second.Grants, g => Assert.Null(g.Requested)); // an item, not a stack
        Assert.All(second.Grants, g => Assert.Equal(ItemOutcome.Created, g.Outcome));
        Assert.All(DecoyKit.All, d => Assert.Equal(1, rig.Held(d.Wcid)));
        Assert.All(DecoyKit.All, d => Assert.Equal(1, rig.Stacks(d.Wcid)));

        var third = rig.Finish(rig.Session.Tools.SpawnGear(all, ActionSource.Host));
        Assert.Equal(0, third.Requested);
        Assert.Equal(9, third.AlreadyHeld);
        Assert.Equal(9, rig.Inventory.Pack.Count); // the wand and the eight decoys, never a second copy
    }
}
