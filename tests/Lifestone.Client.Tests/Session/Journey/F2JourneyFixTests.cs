using Lifestone.Net.Messages;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M4 P5b fix slice F2 (the first live journeys' findings): the known-state's indoor recovery place (F2-1), the station's needs from one source (F2-2), the buff station's ok minus the named
/// host-only judges (F2-3), the vendor station's sale (F2-4) and the vendor-facts sale item (F2-5).
/// </summary>
public sealed class F2JourneyFixTests
{
    private static StationSpec Spec(string name, StationNeeds? over = null) => new(name, StationKinds.HarmHeal, over, 30);

    private static JourneyPlan Plan(params StationSpec[] stations) => new("f2", "BSD Mage", "test", StationWhere.Belt, 1, stations);

    // ------------------------------------------------------------------------------------------------ F2-1

    [Theory]
    [InlineData(0xA9B40141u)] // Renald's back room, where the Brawler was left
    [InlineData(0xA9B40111u)] // Thelnoth's cellar, where the Archer was left
    public void A_player_indoors_in_Holtburgs_landblock_gets_the_unbudgeted_recovery_place_and_it_is_logged_with_the_cell_F2(uint cell)
    {
        var t = new J1JourneyRig(cell, peace: true);
        var journey = t.Journey(Plan(Spec("s1")), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Equal(["Holtburg"], t.PlacesTo);
        Assert.Contains(" teleports=0 recalls=0 budget=1 ", journey.Lines[^1], StringComparison.Ordinal);
        Assert.EndsWith(" recovery_place=1 recovery=none", journey.Lines[^1], StringComparison.Ordinal);
        Assert.Single(t.Log.Entries, e => e.Message.Contains("journey known-state: the player is indoors (", StringComparison.Ordinal) && e.Message.Contains("recovery_place=1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_player_outdoors_in_Holtburg_stays_where_it_is_and_the_indoor_test_reads_the_cell_index_F2()
    {
        var t = new J1JourneyRig();
        var journey = t.Journey(Plan(Spec("s1")), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Empty(t.PlacesTo);
        Assert.EndsWith(" recovery_place=0 recovery=none", journey.Lines[^1], StringComparison.Ordinal);
        Assert.False(JourneyGrounds.Indoors(null));
    }

    // ------------------------------------------------------------------------------------------------ F2-2

    /// <summary>A factory whose stations declare their own needs (the real catalog's way), scripted otherwise.</summary>
    private sealed class OwnNeedsFactory(StationNeeds own) : IStationFactory
    {
        public List<string> Started { get; } = [];

        public IStation Create(StationSpec spec) => new J1Station(spec.Name, own, new J1StationScript(), Started);
    }

    [Fact]
    public void Without_an_override_the_stations_own_needs_are_asserted_and_nothing_is_warned_and_an_override_that_differs_is_warned_and_wins_F2()
    {
        // No override: the station's own needs (mana 101: no keeper can restore it) are what the journey asserts -> skipped by precondition, and no "differ" warning.
        var t = new J1JourneyRig();
        var journey = t.Journey(Plan(Spec("s1")), new OwnNeedsFactory(new StationNeeds(MinManaPercent: 101)), t.Context());
        t.Run(journey);
        Assert.Equal("journey station=s1 ok=false s=0.0 skipped=precondition:mana", journey.Lines[0]);
        Assert.DoesNotContain(t.Log.Entries, e => e.Message.Contains("differ", StringComparison.Ordinal) || e.Message.Contains("overrides", StringComparison.Ordinal));

        // An override equal to the own needs warns nothing; one that differs warns once and is what is asserted (mana 0 holds, the station runs).
        var same = new J1JourneyRig();
        var sameJourney = same.Journey(Plan(Spec("s1", new StationNeeds(MinManaPercent: 101))), new OwnNeedsFactory(new StationNeeds(MinManaPercent: 101)), same.Context());
        same.Run(sameJourney);
        Assert.DoesNotContain(same.Log.Entries, e => e.Message.Contains("overrides", StringComparison.Ordinal));

        var over = new J1JourneyRig();
        var overFactory = new OwnNeedsFactory(new StationNeeds(MinManaPercent: 101));
        var overJourney = over.Journey(Plan(Spec("s1", new StationNeeds(MinManaPercent: 0))), overFactory, over.Context());
        over.Run(overJourney);
        Assert.Single(over.Log.Entries, e => e.Message.StartsWith("journey station s1: the plan overrides the station's own needs", StringComparison.Ordinal));
        Assert.Matches(@"^journey station=s1 ok=true ", overJourney.Lines[0]);
        Assert.Equal(["s1"], overFactory.Started);
    }

    // ------------------------------------------------------------------------------------------------ F2-3

    private static BuffResult Pass(int active = 12, bool rebuffed = true) => new()
    {
        Wanted = 12,
        Active = active,
        Levels = "Focus:VIII,Will:VIII,HeavyM:IV,Str:IV,End:IV,Coord:IV,Quick:IV,Invuln:IV,Impreg:IV,Armor:VI,Regen:VI,Rejuv:VIII",
        MinRemainingSeconds = 579,
        RecastWhileActive = rebuffed ? 1 : 0,
        Forced = true,
        Pass = "rebuff",
        Cast = 1,
        Misses = [],
    };

    [Fact]
    public void The_fighters_buff_station_is_ok_on_the_live_melee_fields_with_only_the_named_host_only_judges_left_out_F2()
    {
        // The melee journey's live fields: buffs=12/12 rebuffed=yes forced=yes, gear=n/a worn=n/a, sounds_server=n/a (the headless host has no counter).
        var cycle = new BuffResult { Wanted = 12, Active = 12, Levels = "Focus:VIII", MinRemainingSeconds = 1200, Misses = [], CycleSeconds = 0 };
        var fighter = new BuffLegScenarioResult(true, null, JourneyStations.FighterGearFields, cycle, Pass());
        IReadOnlyList<string> unjudged = [.. JourneyStations.HostOnlyProblems, "gear=n/a"];
        var log = new RecordingLogSink();

        Assert.True(JourneyStations.BuffOk(fighter, unjudged, "buff", log));
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station buff: not judged in this host: sounds_server=n/a", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station buff: not judged in this host: gear=n/a", StringComparison.Ordinal));

        // The live bug: the unused gear scenario's "gear=failed" made a problem the station does not name.
        Assert.False(JourneyStations.BuffOk(fighter with { GearFields = "gear=failed worn=0" }, unjudged, "buff", new RecordingLogSink()));

        // Real failures still fail: a line missing, no re-buff, the scenario not succeeded.
        Assert.False(JourneyStations.BuffOk(fighter with { Forced = Pass(active: 11) }, unjudged, "buff", new RecordingLogSink()));
        Assert.False(JourneyStations.BuffOk(fighter with { Forced = Pass(rebuffed: false) }, unjudged, "buff", new RecordingLogSink()));
        Assert.False(JourneyStations.BuffOk(fighter with { ScenarioSucceeded = false, Failure = "boom" }, unjudged, "buff", new RecordingLogSink()));

        // The mage keeps its gear judge: gear=wielded holds, gear=failed fails (only the sound counter is left out for it).
        var mage = new BuffLegScenarioResult(true, null, "gear=wielded worn=8", cycle, Pass());
        Assert.True(JourneyStations.BuffOk(mage, JourneyStations.HostOnlyProblems, "buff", new RecordingLogSink()));
        Assert.False(JourneyStations.BuffOk(mage with { GearFields = "gear=failed worn=0" }, JourneyStations.HostOnlyProblems, "buff", new RecordingLogSink()));
    }

    [Fact]
    public void Every_walk_to_Renald_uses_the_hunts_one_range_and_no_other_literal_FC_b2()
    {
        // Round 1: the vendor-facts walk asked 5.0 m where the hunt's 6.5 m (no standable floor within 5 m of him, F1) is the measured one: the physics refused the path.
        Assert.Equal(6.5, HuntScenario.RenaldUseMetres);
        Assert.Equal(3.0, HuntScenario.ThelnothUseMetres);
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lifestone.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var shop = File.ReadAllText(Path.Combine(dir!, "src", "Lifestone.Client", "Session", "Scenarios", "Journey", "ShopStations.cs"));
        Assert.Contains("RenaldAt, 2.0f, HuntScenario.RenaldUseMetres)", shop, StringComparison.Ordinal);
        Assert.DoesNotContain("RenaldAt, 2.0f, 5.0)", shop, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mage_buff_station_reports_sounds_server_zero_without_failing_and_names_the_failing_judge_FC_b1()
    {
        // Round 1: buffs=24/24 rebuffed=yes forced=no, the wand was re-seated in the gear station (before the buff window): sounds_server counted 0 -> why=verdict.
        var cycle = new BuffResult { Wanted = 24, Active = 24, Levels = "Focus:VII", MinRemainingSeconds = 3215, Misses = [], CycleSeconds = 7 };
        var mage = new BuffLegScenarioResult(true, null, "gear=wielded worn=5", cycle, Pass());
        var log = new RecordingLogSink();
        var judged = JourneyStations.BuffJudge(mage, JourneyStations.HostOnlyProblems, "buff", log, serverSounds: 0, soundsReportOnly: true);
        Assert.True(judged.Ok);
        Assert.Single(judged.Reported);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station buff: report-only in its first journey gate: sounds_server=0", StringComparison.Ordinal));

        // Still judged where not report-only (the retired leg's rule), and a real failure fails and is NAMED, never a bare verdict.
        Assert.False(JourneyStations.BuffJudge(mage, JourneyStations.HostOnlyProblems, "buff", new RecordingLogSink(), 0, soundsReportOnly: false).Ok);
        var broken = JourneyStations.BuffJudge(mage with { GearFields = "gear=failed worn=0", Forced = Pass(active: 11) }, JourneyStations.HostOnlyProblems, "buff", new RecordingLogSink(), 0, soundsReportOnly: true);
        Assert.False(broken.Ok);
        Assert.Equal("judge:gear+buffs", JourneyStations.JudgeWhy(broken.Failed));
    }

    [Fact]
    public void The_buff_station_judges_D27s_server_sounds_where_the_host_counts_them_and_sets_them_aside_where_it_does_not_FB_R4()
    {
        var cycle = new BuffResult { Wanted = 12, Active = 12, Levels = "Focus:VIII", MinRemainingSeconds = 1200, Misses = [], CycleSeconds = 0 };
        var result = new BuffLegScenarioResult(true, null, JourneyStations.FighterGearFields, cycle, Pass());
        IReadOnlyList<string> unjudged = [.. JourneyStations.HostOnlyProblems, "gear=n/a"];

        // The Godot host: the counter exists, so sounds_server >= 1 is judged (0 fails, 1 and more pass) and is not logged as set aside.
        var godotLog = new RecordingLogSink();
        Assert.True(JourneyStations.BuffOk(result, unjudged, "buff", godotLog, serverSounds: 3));
        Assert.DoesNotContain(godotLog.Entries, e => e.Message.Contains("sounds_server", StringComparison.Ordinal));
        Assert.False(JourneyStations.BuffOk(result, unjudged, "buff", new RecordingLogSink(), serverSounds: 0));
        Assert.True(JourneyStations.BuffOk(result, unjudged, "buff", new RecordingLogSink(), serverSounds: 1));

        // The headless host: no counter, the named host-only judge is set aside and logged.
        var headlessLog = new RecordingLogSink();
        Assert.True(JourneyStations.BuffOk(result, unjudged, "buff", headlessLog));
        Assert.Contains(headlessLog.Entries, e => e.Message.StartsWith("station buff: not judged in this host: sounds_server=n/a", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ F2-4

    [Theory]
    [InlineData(2, "ok", true)]
    [InlineData(1, "partial", true)]
    [InlineData(0, "none", true)] // nothing-to-sell, named
    [InlineData(0, "failed:placing:_travel:run:blocked", false)] // the live run's walk failure
    [InlineData(0, "failed:no_item_left_the_pack", false)]
    [InlineData(0, "off", false)]
    [InlineData(0, "not-run", false)]
    public void The_vendor_station_requires_its_sale_or_a_named_nothing_to_sell_F2(int sold, string outcome, bool ok)
    {
        var log = new RecordingLogSink();

        Assert.Equal(ok, JourneyStations.SaleHappened(sold, outcome, "vendor", log));

        Assert.Equal(sold == 0 && outcome == "none", log.Entries.Any(e => e.Message.Contains("nothing-to-sell", StringComparison.Ordinal)));
        Assert.Equal(sold == 0 && outcome != "none", log.Entries.Any(e => e.Message.Contains("the sale did not happen", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------------------------------------ F2-5

    [Fact]
    public void Food_is_never_the_vendor_facts_sale_item_even_when_the_mask_takes_it_F2()
    {
        var bread = LootRulesTests.Item(0x20, 5, name: "Bread", wcid: VendorStock.BreadWcid);
        var shirt = LootRulesTests.Item(0x4, 10, name: "Shirt");
        var bracers = LootRulesTests.Item(0x2, 30, name: "Leather Bracers");
        Func<WorldObject, bool> mask = o => (o.Weenie.ItemType & 0x480467A7u) != 0 && (o.Weenie.Value ?? 0) >= 1;

        Assert.True(mask(bread)); // the mask takes it ...
        Assert.False(VendorFactsRun.SaleCandidate(bread, mask)); // ... Renald refused it live ("The Bread is unsellable."): not a candidate
        Assert.True(VendorFactsRun.SaleCandidate(shirt, mask));
        Assert.True(VendorFactsRun.SaleCandidate(bracers, mask));
        Assert.False(VendorFactsRun.SaleCandidate(shirt, _ => false));
    }

    // ------------------------------------------------------------------------------------------------ F3

    [Fact]
    public void The_sale_rules_apply_aces_unsellable_clauses_so_the_hunt_never_offers_a_retained_item_or_the_Bread_F3()
    {
        // ACE Player_Commerce.cs:268 @95ef426d: (mask & ItemType) == 0 || !IsSellable || Retained -> "The X is unsellable." (live: Bread, type 0x20, inside Renald's mask).
        var bread = LootRulesTests.Item(0x20, 5, name: "Bread", wcid: VendorStock.BreadWcid, guid: 0x80000459);
        var apple = LootRulesTests.Item(0x20, 5, name: "Apple", wcid: 260, guid: 0x8000045A);
        var retained = LootRulesTests.Item(0x4, 10, name: "Retained Shirt", guid: 0x8000045B, descriptionFlags: VendorStock.RetainedFlag);
        var shirt = LootRulesTests.Item(0x4, 10, name: "Shirt", guid: 0x8000045C);

        Assert.True(VendorStock.Unsellable(bread));
        Assert.True(VendorStock.Unsellable(retained));
        var arrow = LootRulesTests.Item(0x100, 1, name: "Arrow", wcid: VendorStock.ArrowWcid, guid: 0x8000045D); // IsSellable=0 (research.md section 6's SELECT); live 3a "The Arrows is unsellable."
        Assert.Equal(31717u, VendorStock.ArrowWcid);
        Assert.True(VendorStock.Unsellable(arrow));
        Assert.False(VendorStock.Unsellable(apple)); // the refusal is not the Food type: other food is not guessed away
        Assert.False(VendorStock.Unsellable(shirt));

        var stock = new VendorStock(new VendorApproachMsg(0x7A9B4021, 0x480467A7, 0, 1_000_000, true, 0.9f, 1.35f, 0, 0, string.Empty, []), 1, 0);
        Assert.False(stock.Buys(bread));
        Assert.False(stock.Buys(retained));
        Assert.True(stock.Buys(apple));
        Assert.True(stock.Buys(shirt));
        Assert.Equal("unsellable", Lifestone.Client.Session.Fighter.SellRules.ReasonName(Lifestone.Client.Session.Fighter.SellDecision.Unsellable));
        Assert.False(VendorFactsRun.SaleCandidate(bread, stock.Buys));
    }

    [Fact]
    public void A_chest_use_that_ends_with_no_view_no_sound_and_no_text_is_ACEs_walk_timeout_not_the_lock_answer_F3()
    {
        // Live (journey 3h melee): UseDone 0 after 15.1 s, "no reason given": Player_Move.cs:119 timed out before Chest.CheckUseRequirements (Chest.cs:105) could play its sound.
        var timeout = new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server did not open 0x7A9B4072: UseDone 0x00000000 with no 0196 ViewContents; no reason given");
        var locked = new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server did not open 0x7A9B4072: UseDone 0x00000000 with no 0196 ViewContents; the container is locked (it played its sound 0x94; ACE sends no text)");
        Assert.True(ChestsRun.WalkTimedOut(timeout));
        Assert.False(ChestsRun.WalkTimedOut(locked));
        Assert.False(ChestsRun.WalkTimedOut(null));
        Assert.Equal(ChestsRun.ChestRange, ChestsRun.LockedRange); // the locked chest is approached as close as Chest 143 (3.0 m was the live miss)
    }
}
