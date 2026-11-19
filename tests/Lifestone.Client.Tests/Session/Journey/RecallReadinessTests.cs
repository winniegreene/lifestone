using DatReaderWriter.Enums;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6, the recall's components (live 2026-11-18 20:29:54 UTC, the sprint's step3-levelup.log: a level 8 climber read the scroll of Lifestone Recall 1635 on a shopping trip, the way home from Dryreach was chosen because the spellbook held
/// it, and ACE refused the cast 0x0400, YouDontHaveAllTheComponents). What these prove: <see cref="RecallReadiness.Of"/> reads the book, the run's refusal and the pack's components for one cast (the foci formula with the Item Enchantment
/// focus held, the account's full formula without it when the account is known), names the first component the pack is short of, never the account, and reads a pack it cannot judge as castable, as before; and the travel rule
/// (<see cref="AutopilotTravelRule"/>) with that read sends a known recall the pack is short for home by portals from a town on the Town Network, with the reason on the line, and by the recall once the components are held.
/// </summary>
public sealed class RecallReadinessTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Account = "formula-test-account";
    private const uint Recall = RecallHome.LifestoneRecallSpell;
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    /// <summary>Lifestone Recall's row in the player-castable layout (scarab, accent taper, herb, accent taper, powder, potion, accent taper, talisman): the school is the dat's, Item Enchantment (pinned below against the SpellTable).</summary>
    private static SpellInfo Row() =>
        WorldActionRig.Spell(Recall, SpellType.Enchantment) with
        {
            Name = "Lifestone Recall",
            School = MagicSchool.ItemEnchantment,
            Formula = [6, 63, 7, 63, 25, 37, 63, 49],
            FormulaVersion = 1,
        };

    private static Dictionary<uint, SpellInfo> Table(SpellInfo? row = null) => new() { [Recall] = row ?? Row() };

    private static ComponentWeenie WeenieOf(uint component) => ComponentWeenies.TryGetByComponent(component, out var w) ? w : throw new InvalidOperationException("component " + component);

    /// <summary>A scripted session's pack: the description arrived (the pack is known), each item its own CreateObject and its place in the main pack.</summary>
    private sealed class Pack
    {
        private uint _guid = 0x80000E00;

        public Pack(bool knowsRecall = true)
        {
            R.Describe(); // the PlayerDescription: the pack is known (empty), so a component it lacks is absent, not unread
            State.Self.Spellbook.IsKnown = true;
            if (knowsRecall)
            {
                State.Self.Spellbook.Learn(Recall);
            }
        }

        public WorldActionRig R { get; } = new();

        public GameState State => R.H.State;

        public void Give(uint wcid, string name, int count)
        {
            var guid = _guid++;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
            R.H.R.Reducer.OnCreateObject(
                R.H.R.Context,
                basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = 0x1000, StackSize = (ushort)count } },
                isUpdate: false);
            R.Server((player, sequence) => AceM3Writers.PutObjInContainer(player, sequence, guid, WorldActionRig.Player, 0, 0));
        }

        /// <summary>Gives what one cast needs of each component of <paramref name="formula"/> (by component id), except <paramref name="without"/>.</summary>
        public void GiveFormula(IReadOnlyDictionary<uint, int> formula, uint? without = null)
        {
            foreach (var (component, n) in formula)
            {
                if (component != without)
                {
                    var w = WeenieOf(component);
                    Give(w.Wcid, w.Name, n);
                }
            }
        }

        public void GiveItemFocus() => Give(LevelFloor.FocusOf(MagicSchool.ItemEnchantment), "Foci of Artifice", 1);
    }

    // ------------------------------------------------------------------------------------------------ the read

    [Fact]
    public void A_book_without_Lifestone_Recall_is_not_castable_whatever_the_pack_holds()
    {
        var p = new Pack(knowsRecall: false);
        p.GiveItemFocus();
        p.GiveFormula(GearKit.FociFormulaCounts(Row().Formula));

        var read = RecallReadiness.Of(p.State, new JourneyFacts { FormulaAccount = Account }, Table());

        Assert.Equal((false, false, null, false), (read.Known, read.Unavailable, read.ShortOf, read.Judged));
        Assert.False(read.Castable);
        Assert.Equal("the spellbook lacks Lifestone Recall", read.WhyNot);
        Assert.Equal("lifestone_recall=no", read.Fields);
    }

    [Fact]
    public void With_the_Item_Enchantment_focus_held_the_foci_formulas_components_make_it_castable_and_the_account_changes_nothing()
    {
        var p = new Pack();
        Assert.True(p.State.Self.Inventory.IsKnown);
        p.GiveItemFocus();
        var foci = GearKit.FociFormulaCounts(Row().Formula);
        Assert.Equal([6u, SpellFormulaRules.PrismaticTaperId], foci.Keys.Order()); // the Pyreal Scarab and the Prismatic Tapers: no herb, powder, potion or talisman
        p.GiveFormula(foci);

        foreach (var facts in new[] { new JourneyFacts(), new JourneyFacts { FormulaAccount = Account } })
        {
            var read = RecallReadiness.Of(p.State, facts, Table());
            Assert.Equal((true, false, null, true), (read.Known, read.Unavailable, read.ShortOf, read.Judged));
            Assert.True(read.Castable);
            Assert.Null(read.WhyNot);
            Assert.Equal("lifestone_recall=yes components=held", read.Fields);
        }
    }

    [Fact]
    public void With_the_focus_held_a_pack_short_of_the_scarab_is_not_castable_and_the_component_is_named()
    {
        var p = new Pack();
        p.GiveItemFocus();
        p.GiveFormula(GearKit.FociFormulaCounts(Row().Formula), without: 6); // no Pyreal Scarab

        var read = RecallReadiness.Of(p.State, new JourneyFacts(), Table());

        Assert.True(read is { Known: true, Judged: true, Castable: false });
        var scarab = WeenieOf(6);
        Assert.Equal($"{scarab.Name} {scarab.Wcid}, which the pack does not hold", read.ShortOf);
        Assert.Equal($"Lifestone Recall is in the spellbook and the pack is short for one cast: {scarab.Name} {scarab.Wcid}, which the pack does not hold (recall-short:components)", read.WhyNot);
        Assert.Equal("lifestone_recall=yes components=short", read.Fields);
    }

    [Fact]
    public void Without_the_focus_the_accounts_full_formula_is_the_judge_from_the_facts_or_from_the_callers_own_account()
    {
        var full = AccountFormula.Counts(Row(), false, Account);
        var foci = GearKit.FociFormulaCounts(Row().Formula);
        Assert.Contains(full.Keys, c => !foci.ContainsKey(c)); // the full formula holds more than the foci formula's scarab and Prismatic Tapers
        var p = new Pack();
        p.GiveFormula(full);

        var byFacts = RecallReadiness.Of(p.State, new JourneyFacts { FormulaAccount = Account }, Table());
        var byCaller = RecallReadiness.Of(p.State, new JourneyFacts(), Table(), Account);
        Assert.Equal(byFacts, byCaller);
        Assert.Equal((true, false, null, true), (byFacts.Known, byFacts.Unavailable, byFacts.ShortOf, byFacts.Judged));
        Assert.True(byFacts.Castable);
        Assert.Equal("lifestone_recall=yes components=held", byFacts.Fields);
    }

    /// <summary>The live case's shape: no Item Enchantment focus, the account known, and the pack without the herb and the powder its formula names (hers: Comfrey and Powdered Malachite; this account's are read from the formula).</summary>
    [Fact]
    public void Without_the_focus_a_pack_short_of_the_accounts_herb_and_powder_is_not_castable_and_names_the_first_and_never_the_account()
    {
        var full = AccountFormula.Counts(Row(), false, Account);
        var foci = GearKit.FociFormulaCounts(Row().Formula);
        var missing = full.Keys.Where(c => !foci.ContainsKey(c) && c is >= 7 and <= 36).ToList(); // the herbs are components 7 to 24, the powders 25 to 36 (ComponentWeenies)
        Assert.NotEmpty(missing);
        var p = new Pack();
        foreach (var (component, n) in full)
        {
            if (!missing.Contains(component))
            {
                var w = WeenieOf(component);
                p.Give(w.Wcid, w.Name, n);
            }
        }

        var facts = new JourneyFacts { FormulaAccount = Account };
        var read = RecallReadiness.Of(p.State, facts, Table());

        Assert.True(read is { Known: true, Judged: true, Unavailable: false, Castable: false });
        Assert.Contains(missing.Select(c => $"{WeenieOf(c).Name} {WeenieOf(c).Wcid}, which the pack does not hold"), need => need == read.ShortOf);
        Assert.StartsWith("Lifestone Recall is in the spellbook and the pack is short for one cast: ", read.WhyNot, StringComparison.Ordinal);
        Assert.EndsWith(" (recall-short:components)", read.WhyNot, StringComparison.Ordinal);
        Assert.Equal("lifestone_recall=yes components=short", read.Fields);
        Assert.DoesNotContain(Account, read.WhyNot + read.Fields + read.ShortOf, StringComparison.OrdinalIgnoreCase); // hashed, never logged

        // The same pack with no account known is not judged (the foci rule cannot name a school without its focus): castable, as before; the cast's own refusal names the component.
        var unknown = RecallReadiness.Of(p.State, new JourneyFacts(), Table());
        Assert.Equal((true, null, false, true), (unknown.Known, unknown.ShortOf, unknown.Judged, unknown.Castable));
        Assert.Equal("lifestone_recall=yes components=not-judged", unknown.Fields);
    }

    /// <summary>The SpellTable's own row of 1635 on an account's formula: a pack that holds what the foci formula needs (the climber's War and Life foci buy scarabs and Prismatic Tapers) and none of the account's other components is short.</summary>
    [RequiresDatsFact]
    public void The_dats_row_of_Lifestone_Recall_is_Item_Enchantment_and_a_pack_of_scarabs_and_tapers_alone_is_short_on_an_accounts_formula()
    {
        var real = SpellTableData.Load(fixture.Dats).Spells;
        var info = real[Recall];
        Assert.Equal(MagicSchool.ItemEnchantment, info.School);
        var full = AccountFormula.Counts(info, false, Account);
        var foci = GearKit.FociFormulaCounts(info.Formula);
        var others = full.Keys.Where(c => !foci.ContainsKey(c) && ComponentWeenies.TryGetByComponent(c, out _)).ToList();
        Assert.NotEmpty(others);

        var p = new Pack();
        p.Give(LevelFloor.FocusOf(MagicSchool.WarMagic), "Foci of Strife", 1); // another school's focus is no focus of this one
        p.GiveFormula(foci);
        var facts = new JourneyFacts { FormulaAccount = Account };
        var read = RecallReadiness.Of(p.State, facts, real);

        Assert.True(read is { Known: true, Judged: true, Castable: false });
        Assert.Contains(others.Select(c => $"{WeenieOf(c).Name} {WeenieOf(c).Wcid}, which the pack does not hold"), need => need == read.ShortOf);

        p.GiveFormula(full.Where(c => others.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value));
        Assert.True(RecallReadiness.Of(p.State, facts, real).Castable); // the account's full formula held: castable
    }

    /// <summary>
    /// M6 redoubt (live 2026-11-18 22:29:09 UTC: components held, the cast refused 0x049E, the run stopped at the second refusal): a tie known false leaves the recall out; an unknown tie is tried, and a proven one is castable.
    /// </summary>
    [Fact]
    public void A_recall_with_no_lifestone_tied_is_not_castable_and_an_unknown_tie_is_tried()
    {
        var p = new Pack();
        p.GiveItemFocus();
        p.GiveFormula(GearKit.FociFormulaCounts(Row().Formula));

        var untied = RecallReadiness.Of(p.State, new JourneyFacts { LifestoneTied = false }, Table());
        Assert.True(untied.Untied);
        Assert.False(untied.Castable);
        Assert.Equal("Lifestone Recall is in the spellbook and no lifestone is tied (ACE refused it 0x049E this run; recall-untied)", untied.WhyNot);
        Assert.Equal("lifestone_recall=yes components=held tie=no", untied.Fields);
        Assert.False(RecallReadiness.Of(p.State, new JourneyFacts { LifestoneTied = false }, null).Castable); // the fact stands without a spell table too

        var unknown = RecallReadiness.Of(p.State, new JourneyFacts(), Table());
        Assert.False(unknown.Untied);
        Assert.True(unknown.Castable);
        Assert.True(RecallReadiness.Of(p.State, new JourneyFacts { LifestoneTied = true }, Table()).Castable);
    }

    /// <summary>M6 redoubt: the tie after a learn runs from Holtburg's landblock or one beside it (the scriveners' rooms are in 0xA9B3); a town of the Town Network and a dungeon wait.</summary>
    [Theory]
    [InlineData(0xA9B40019u, true)]  // the lifestone's cell
    [InlineData(0xA9B30100u, true)]  // the Apprentice Scrivener's room, the next landblock south
    [InlineData(0xAAB50001u, true)]  // a corner neighbour
    [InlineData(0xABB10001u, false)] // two landblocks east
    [InlineData(0xDA75002Bu, false)] // Dryreach's drop
    [InlineData(0x016301ACu, false)] // the Redoubt's arrival
    public void The_tie_after_a_learn_runs_beside_Holtburg_only(uint cell, bool beside) =>
        Assert.Equal(beside, ResupplyRun.BesideHoltburg(cell));
    [Fact]
    public void A_recall_ACE_refused_for_a_component_this_run_is_not_castable_even_with_the_components_held()
    {
        var p = new Pack();
        p.GiveItemFocus();
        p.GiveFormula(GearKit.FociFormulaCounts(Row().Formula));
        var facts = new JourneyFacts { LifestoneRecallUnavailable = true };

        var read = RecallReadiness.Of(p.State, facts, Table());

        Assert.Equal((true, true, null, true), (read.Known, read.Unavailable, read.ShortOf, read.Judged));
        Assert.False(read.Castable);
        Assert.Equal("Lifestone Recall is in the spellbook and ACE refused it for a component earlier this run (recall-unavailable:components)", read.WhyNot);
        Assert.Equal("lifestone_recall=yes components=refused-this-run", read.Fields);
        Assert.False(RecallReadiness.Of(p.State, facts, null).Castable); // the fact stands without a spell table too (M6 7 AV)
    }

    [Fact]
    public void A_pack_that_cannot_be_judged_reads_castable_as_before()
    {
        // No focus and no account name: the formula cannot be named.
        var empty = new Pack();
        var noAccount = RecallReadiness.Of(empty.State, new JourneyFacts(), Table());
        Assert.Equal((true, false, true), (noAccount.Known, noAccount.Judged, noAccount.Castable));
        Assert.Equal("lifestone_recall=yes components=not-judged", noAccount.Fields);
        Assert.Null(noAccount.WhyNot);

        // The pack not read yet (no PlayerDescription), even with the account known.
        var unread = new GameState();
        unread.Self.Spellbook.IsKnown = true;
        unread.Self.Spellbook.Learn(Recall);
        Assert.False(unread.Self.Inventory.IsKnown);
        var early = RecallReadiness.Of(unread, new JourneyFacts { FormulaAccount = Account }, Table());
        Assert.Equal((true, false, true), (early.Known, early.Judged, early.Castable));

        // No spell row (no table, or a table without 1635), with the account known and an empty pack.
        var facts = new JourneyFacts { FormulaAccount = Account };
        foreach (var spells in new IReadOnlyDictionary<uint, SpellInfo>?[] { null, new Dictionary<uint, SpellInfo>() })
        {
            var noRow = RecallReadiness.Of(empty.State, facts, spells);
            Assert.Equal((true, false, true), (noRow.Known, noRow.Judged, noRow.Castable));
        }

        // The same empty pack with the row and the account is judged, and short.
        Assert.True(RecallReadiness.Of(empty.State, facts, Table()) is { Judged: true, Castable: false });
    }

    // ------------------------------------------------------------------------------------------------ the travel rule

    /// <summary>The rule's input as the autopilot builds it, standing in a town of the Town Network at the live case's level 8.</summary>
    private static RungRouteInput HomeFrom(Pack p, JourneyFacts facts, string place, IReadOnlyDictionary<uint, SpellInfo>? spells) =>
        AutopilotTravelRule.Input(p.State, GroundCatalog.Get("holtburg-outskirts"), facts, Routes, RungDirection.Home, null, spells) with { Where = RungWhere.Elsewhere, Place = place, BodyGround = null, Level = 8 };

    [Fact]
    public void From_a_town_on_the_Network_a_known_recall_the_pack_is_short_for_goes_home_by_portals_and_says_why_and_by_the_recall_once_the_components_are_held()
    {
        var full = AccountFormula.Counts(Row(), false, Account);
        var herb = full.Keys.First(c => c is >= 7 and <= 24);
        var p = new Pack();
        p.GiveFormula(full, without: herb);
        var facts = new JourneyFacts { FormulaAccount = Account };

        var input = HomeFrom(p, facts, "dryreach", Table());
        Assert.False(input.KnowsLifestoneRecall);
        var home = AutopilotTravelRule.Home(input, bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, home.Way);
        Assert.Equal("dryreach", home.Route);
        var need = $"{WeenieOf(herb).Name} {WeenieOf(herb).Wcid}, which the pack does not hold";
        Assert.StartsWith($"Lifestone Recall is in the spellbook and the pack is short for one cast: {need} (recall-short:components) (D43) and dryreach has a way home: route dryreach, ", home.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("lacks Lifestone Recall", home.Reason, StringComparison.Ordinal);

        // The plan the rung change then runs has no recall leg, and its reason opens with the same words.
        var plan = RungRoute.Decide(input, Routes);
        Assert.Null(plan.Stop);
        Assert.NotEmpty(plan.Legs);
        Assert.All(plan.Legs, l => Assert.Equal(RungLegKind.Hop, l.Kind));
        Assert.StartsWith("Lifestone Recall is in the spellbook and the pack is short for one cast: " + need, plan.Why, StringComparison.Ordinal);

        // Without the spell table the rule reads the book and the run's refusal alone, as before the read.
        Assert.True(HomeFrom(p, facts, "dryreach", null).KnowsLifestoneRecall);

        // The herb bought: the recall, as before.
        p.Give(WeenieOf(herb).Wcid, WeenieOf(herb).Name, full[herb]);
        var held = HomeFrom(p, facts, "dryreach", Table());
        Assert.True(held.KnowsLifestoneRecall);
        Assert.Null(held.RecallWhyNot);
        var recall = AutopilotTravelRule.Home(held, bookRead: true, Routes);
        Assert.Equal((TravelWay.Recall, "the spellbook holds Lifestone Recall"), (recall.Way, recall.Reason));
    }

    [Fact]
    public void A_book_without_the_recall_keeps_its_own_words_and_a_refused_recall_names_the_refusal()
    {
        var facts = new JourneyFacts { FormulaAccount = Account };
        var lacks = AutopilotTravelRule.Home(HomeFrom(new Pack(knowsRecall: false), facts, "dryreach", Table()), bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, lacks.Way);
        Assert.StartsWith("the spellbook lacks Lifestone Recall (D43) and dryreach has a way home", lacks.Reason, StringComparison.Ordinal);

        var p = new Pack();
        p.GiveFormula(AccountFormula.Counts(Row(), false, Account));
        facts.LifestoneRecallUnavailable = true;
        var refused = AutopilotTravelRule.Home(HomeFrom(p, facts, "dryreach", Table()), bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, refused.Way);
        Assert.StartsWith("Lifestone Recall is in the spellbook and ACE refused it for a component earlier this run (recall-unavailable:components) (D43) and dryreach has a way home", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>Where no way home by portals is known, a recall the pack is short for is still tried, as a book without it is: the rule says so, and the cast's refusal is the named stop (no way home is invented).</summary>
    [Fact]
    public void From_a_place_with_no_way_home_by_portals_a_short_pack_still_tries_the_recall_and_the_reason_names_the_lack()
    {
        var p = new Pack(); // an empty pack: short of everything
        var home = AutopilotTravelRule.Home(HomeFrom(p, new JourneyFacts { FormulaAccount = Account }, "0x1234", Table()), bookRead: true, Routes);

        Assert.Equal(TravelWay.Recall, home.Way);
        Assert.StartsWith("Lifestone Recall is in the spellbook and the pack is short for one cast: ", home.Reason, StringComparison.Ordinal);
        Assert.Contains("0x1234 has no way home by portals (TODO(m6-way-home)", home.Reason, StringComparison.Ordinal);
        Assert.EndsWith("the recall as before, its refusal stops the run by name", home.Reason, StringComparison.Ordinal);
    }
}
