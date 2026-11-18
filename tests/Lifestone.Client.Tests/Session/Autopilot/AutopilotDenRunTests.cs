using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14 (the den run's order, <c>AutopilotScenario.DenRun.cs</c>) over the scripted ACE of <see cref="AutopilotRig"/>, the trip, the stop at the bot, her own pass and the den run's station scripted: the order's parse, the
/// sequence (the forced trip, the bot, the pass, the run, the stop <c>den-run:tied</c>), each refusal by name, a death that ends the run with no death station, a failure by the station's name, a cut that is not begun again, and
/// the run-only stand-down of the survival layer.
/// </summary>
public sealed class AutopilotDenRunTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";
    private const string TiedFields = "ground=black-spawn-den-sho level=40 route=den-run hops=6 ran_m=2900 attackers=3 drinks=1 straight=13/0 inside_s=60 tied=yes home=recall ok=yes fail=none";

    private static AutopilotOptions Order => new() { Job = AutopilotJob.LevelUp, Task = AutopilotTask.DenRun };

    private static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

    /// <summary>A character at Holtburg's lifestone with the kit, two casters and every spell of the scrivener catalog in the book (Primary Portal Tie, Primary Portal Recall and Lifestone Recall among them).</summary>
    private ApWorld AtHome(int level = 40, Func<uint, bool>? knows = null)
    {
        var w = new ApWorld(Outskirts, output, atHome: true);
        w.Level(level);
        w.OwnKit();
        for (var i = 0u; i < 2; i++)
        {
            w.Trip.Own("Wand " + i.ToString(CultureInfo.InvariantCulture), 90_000 + i, KeepAsideRule.CasterType, 100);
        }

        var state = w.Trip.Vendor.State;
        state.Self.Spellbook.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells.Where(s => knows?.Invoke(s.SpellId) != false))
        {
            state.Self.Spellbook.Learn(spell.SpellId);
        }

        return w;
    }

    private static void Enchant(ApWorld w, BuffBotLine line, double duration)
    {
        var category = (ushort)(1000 + BuffBotWire.All.Lines.ToList().IndexOf(line));
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)line.SpellId, 1, category, 0, 300, 0.0, duration, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
    }

    /// <summary>The scripted stations of the order; <see cref="Order"/> is the runs stepped, in order.</summary>
    private sealed class Script
    {
        public Script(ApWorld w)
        {
            w.Factory.Script = spec => spec.Kind switch
            {
                StationKinds.Logistics => Named(spec, () => ScenarioStatus.Succeeded, () => "sold=3 bought=2"),
                StationKinds.BuffBot => Named(
                    spec,
                    () =>
                    {
                        if (!BotLands)
                        {
                            return ScenarioStatus.Failed;
                        }

                        foreach (var line in BuffBotWire.All.Lines)
                        {
                            Enchant(w, line, BotSeconds);
                        }

                        return ScenarioStatus.Succeeded;
                    },
                    () => BotLands ? "word=all lines=28 landed=28 ok=yes" : "word=all lines=28 landed=0 ok=no",
                    BotLands ? null : "buffs: buff-bot: no-bot"),
                StationKinds.Buff => Named(spec, () => ScenarioStatus.Succeeded, () => "buffs=24/24"),
                StationKinds.DenRun => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        if (step == 0)
                        {
                            Order.Add(spec.Name);
                        }

                        return Run(step);
                    },
                    () => RunFields)
                { Failure = RunFailure },
                _ => null,
            };
        }

        private ScriptedRun Named(StationSpec spec, Func<ScenarioStatus?> end, Func<string> fields, string? failure = null) => new(
            spec.Name,
            step =>
            {
                if (step == 0)
                {
                    Order.Add(spec.Name);
                }

                return end();
            },
            fields)
        { Failure = failure };

        public List<string> Order { get; } = [];

        public bool BotLands { get; set; } = true;

        public double BotSeconds { get; set; } = 3600;

        /// <summary>The den run's scripted station: by default it ends well at its first step.</summary>
        public Func<int, ScenarioStatus?> Run { get; set; } = _ => ScenarioStatus.Succeeded;

        public string RunFields { get; set; } = TiedFields;

        public string? RunFailure { get; set; }
    }

    private static int Made(ApWorld w, string kind) => w.Factory.Made.Count(m => m.Kind == kind);

    // ------------------------------------------------------------------------------------------------ the order

    [Fact]
    public void The_switch_is_off_by_default_and_on_it_is_the_den_run_task_of_the_level_up_job()
    {
        Assert.Equal(AutopilotTaskKind.Job, new AutopilotOptions().Task.Kind);
        Assert.True(OptionCatalog.Registry.IsKnown(AutopilotOptions.DenRunOption));

        var on = Parse("--autopilot-job=level-up", AutopilotOptions.DenRunOption);
        Assert.Empty(on.Errors);
        Assert.Equal(AutopilotTask.DenRun, on.Task);
        Assert.Equal("den-run", on.Task.Name);
        Assert.Equal(AutopilotTask.DenRun, Parse("--autopilot-job=level-up", "--autopilot-task=den-run").Task);
        Assert.Equal(AutopilotTaskKind.Job, Parse("--autopilot-job=level-up", AutopilotOptions.DenRunOption + "=off").Task.Kind);

        // Another task beside it, or another job, fails the run by name.
        Assert.Contains(Parse("--autopilot-job=level-up", AutopilotOptions.DenRunOption, "--autopilot-task=errand").Errors, e => e.Contains("names another (errand)", StringComparison.Ordinal));
        Assert.Contains(Parse(AutopilotOptions.DenRunOption).Errors, e => e.Contains("the den run is the level up job's order", StringComparison.Ordinal));
        Assert.True(AutopilotTask.TryParse("den-run", out var task, out _) && task.Kind == AutopilotTaskKind.DenRun);
    }

    [Fact]
    public void The_order_makes_the_trip_the_bot_her_pass_and_the_run_in_order_then_stops_den_run_tied()
    {
        var w = AtHome();
        var script = new Script(w);
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal(AutopilotScenario.DenRunTiedStop, run.StopName);
        Assert.Equal(AutopilotScenario.DenRunTiedStop, run.DenRunEnd);
        Assert.True(run.Ok);
        Assert.Equal(["logistics", "buff-bot", "buff", "den-run"], script.Order.Where(n => n is "logistics" or "buff-bot" or "buff" or "den-run"));
        var trip = Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.Equal(JourneyPlans.TripForced, trip.Arguments[JourneyPlans.TripArgument]);
        Assert.Same(AutopilotScenario.DenRunSpec, Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.DenRun));
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind is StationKinds.DenFight or StationKinds.RungChange or StationKinds.DenTravel or StationKinds.Death or StationKinds.Quest);
        Assert.Contains(w.Lines, l => l.Contains("den run: ordered (--autopilot-den-run): 1 of 4: the trip home with its sale", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("den run: 3 of 4: check before: level 40 (at least 40); the bot's 28 of 28 lines of 'all' up", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("den run: 4 of 4: the tie linked", StringComparison.Ordinal));
        Assert.Contains(run.Lines, l => l.StartsWith("autopilot done ok=yes ", StringComparison.Ordinal) && l.Contains(" stop=den-run:tied ", StringComparison.Ordinal));
    }

    /// <summary>The order is given at the level it is for, so the level stop rule (the climb's target, 40) is not read while it stands; without the order the same options stop by the level at once.</summary>
    [Fact]
    public void The_level_stop_rule_is_not_read_while_the_order_stands()
    {
        var w = AtHome();
        var script = new Script(w);
        var run = w.Autopilot(Order with { Level = 40 });
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal(AutopilotScenario.DenRunTiedStop, run.StopName);
        Assert.Contains("den-run", script.Order);

        var plain = AtHome();
        _ = new Script(plain);
        var job = plain.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Task = AutopilotTask.Errand, Level = 40 });
        Assert.Equal(ScenarioStatus.Succeeded, plain.Run(job, null, 8000, _ => AnswerWieldsAndModes(plain)));
        Assert.Equal("level", job.StopName);
    }

    [Fact]
    public void While_the_order_stands_the_two_scrolls_are_first_in_the_learn_plan_and_the_trip_ends_at_home()
    {
        var w = AtHome();
        _ = new Script(w);
        JourneyContext? made = null;
        w.Prime = c => made = c;
        var run = w.Autopilot(Order);

        Assert.Equal(ScrivenerLearnPlan.DenRunLines, made!.Facts.LearnTravelLines);
        Assert.Equal(ScrivenerLearnPlan.DenRunLines, run.Counters.OrderLines);
        Assert.True(made.Facts.MinutesStopDue!()); // the trip's safe point: no way back is begun
        var rung = LadderLoader.WarMage.Rungs[0];
        var lines = ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default, null, ScrivenerLearnPlan.DenRunLines).Select(l => l.Name).ToList();
        Assert.Equal(["Primary Portal Tie", "Primary Portal Recall"], lines.Take(2));
        Assert.DoesNotContain("Primary Portal Tie", ScrivenerLearnPlan.LinesFor(rung, ScrivenerCatalog.Default).Select(l => l.Name));

        // Off the order nothing is added.
        var plain = AtHome();
        JourneyContext? other = null;
        plain.Prime = c => other = c;
        var job = plain.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp });
        Assert.Empty(other!.Facts.LearnTravelLines);
        Assert.Empty(job.Counters.OrderLines);
    }

    // ------------------------------------------------------------------------------------------------ the refusals

    [Fact]
    public void Under_level_40_the_order_refuses_by_name_and_nothing_is_begun()
    {
        var w = AtHome(level: 39);
        var script = new Script(w);
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:refused:level:39<40", run.StopName);
        Assert.False(run.Ok);
        Assert.DoesNotContain(script.Order, n => n is "logistics" or "den-run");
    }

    [Fact]
    public void With_the_bot_off_or_another_word_the_order_refuses_by_name()
    {
        var off = AtHome();
        _ = new Script(off);
        var noBot = off.Autopilot(Order with { BuffBot = false });
        Assert.Equal(ScenarioStatus.Failed, off.Run(noBot, null, 8000, _ => AnswerWieldsAndModes(off)));
        Assert.Equal("den-run:refused:bot-off", noBot.StopName);

        var tusker = AtHome();
        _ = new Script(tusker);
        var word = tusker.Autopilot(Order with { BuffBotProfile = BuffBotWire.Tusker });
        Assert.Equal(ScenarioStatus.Failed, tusker.Run(word, null, 8000, _ => AnswerWieldsAndModes(tusker)));
        Assert.Equal("den-run:refused:bot-word:tusker", word.StopName);
        Assert.Equal(0, Made(tusker, StationKinds.DenRun));
    }

    [Fact]
    public void Without_the_bots_lines_fresh_the_run_is_refused_by_name_after_the_trip_and_her_pass()
    {
        var w = AtHome();
        var script = new Script(w) { BotLands = false };
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:refused:bot-lines:0of28", run.StopName);
        Assert.Equal(0, Made(w, StationKinds.DenRun));
        Assert.Contains("logistics", script.Order);
        Assert.Contains("buff", script.Order);

        // Lines that are up but under 45 minutes are not fresh either.
        var late = AtHome();
        var shortLived = new Script(late) { BotSeconds = BuffBotRule.RenewUnderSeconds - 60 };
        var second = late.Autopilot(Order);
        Assert.Equal(ScenarioStatus.Failed, late.Run(second, null, 8000, _ => AnswerWieldsAndModes(late)));
        Assert.Equal("den-run:refused:bot-lines:28of28", second.StopName);
        Assert.DoesNotContain("den-run", shortLived.Order);
    }

    [Fact]
    public void A_book_short_of_primary_portal_tie_after_the_trip_refuses_by_name()
    {
        var w = AtHome(knows: id => id != DenTravelStation.PortalTieSpell);
        _ = new Script(w);
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:refused:no-spell:primary-portal-tie", run.StopName);
        Assert.Equal(0, Made(w, StationKinds.DenRun));
    }

    // ------------------------------------------------------------------------------------------------ one attempt

    [Fact]
    public void A_death_on_the_run_ends_it_by_name_with_no_death_station_and_no_second_run()
    {
        var w = AtHome();
        var script = new Script(w);
        script.Run = step =>
        {
            if (step == 3)
            {
                w.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been killed by a Sclavus Acolyte!", 0x80001944, DeathNoticeSource.VictimNotification));
            }

            return null; // the station never ends by itself: the death ends it
        };
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:died:run", run.StopName);
        Assert.Equal(1, Made(w, StationKinds.DenRun));
        Assert.Equal(0, Made(w, StationKinds.Death));
        Assert.Single(w.Lines, l => l.Contains("den run: died: stage=run where=cell 0x", StringComparison.Ordinal) && l.Contains("by=You have been killed by a Sclavus Acolyte!", StringComparison.Ordinal) && l.Contains("the corpse lies where she died and is not fetched", StringComparison.Ordinal));
    }

    [Fact]
    public void A_station_that_reports_a_death_on_a_walk_stops_den_run_died_by_its_leg()
    {
        var w = AtHome();
        _ = new Script(w) { Run = _ => ScenarioStatus.Failed, RunFields = "ground=black-spawn-den-sho level=40 tied=no home=no ok=no fail=died:hop:respawned=0xA9B40019", RunFailure = "died:hop:respawned=0xA9B40019: the player died on hop network-to-danbys" };
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:died:hop", run.StopName);
        Assert.Equal(0, Made(w, StationKinds.Death));
        Assert.Equal(1, Made(w, StationKinds.DenRun));
    }

    [Fact]
    public void A_failure_of_the_station_stops_by_its_name_under_den_run_and_the_run_is_not_begun_again()
    {
        var w = AtHome();
        _ = new Script(w) { Run = _ => ScenarioStatus.Failed, RunFields = "ground=black-spawn-den-sho level=40 tied=no home=no ok=no fail=hop:danbys-to-desert-march:no-teleport", RunFailure = "hop:danbys-to-desert-march:no-teleport: two Uses and no teleport" };
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:hop:danbys-to-desert-march:no-teleport", run.StopName);
        Assert.Equal(1, Made(w, StationKinds.DenRun));

        // The station's own den-run names are kept as they are.
        var refused = AtHome();
        _ = new Script(refused) { Run = _ => ScenarioStatus.Failed, RunFields = "tied=no ok=no fail=den-run:tie:not-linked", RunFailure = "den-run:tie:not-linked: it did not link" };
        var second = refused.Autopilot(Order);
        Assert.Equal(ScenarioStatus.Failed, refused.Run(second, null, 8000, _ => AnswerWieldsAndModes(refused)));
        Assert.Equal("den-run:tie:not-linked", second.StopName);
    }

    /// <summary>M6 item 14 (live 2026-11-13: a stop asked during the run was not acted on for 106 s): a stop request calls the den run off at once, away from home too, and the run is not begun again.</summary>
    [Fact]
    public void A_stop_request_calls_the_den_run_off_at_once_where_she_stands()
    {
        var w = AtHome();
        var script = new Script(w);
        AutopilotScenario? run = null;
        var asked = -1;
        var steps = 0;
        script.Run = step =>
        {
            steps = step;
            if (step == 3)
            {
                asked = step;
                run!.RequestStop("stop-file");
            }

            return null; // the station never ends by itself: the request cuts it
        };
        run = w.Autopilot(Order);

        w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w));

        Assert.Equal(3, asked);
        Assert.InRange(steps, 3, 4); // cut on the frame after the request, not at the station's own end
        Assert.Equal("request:stop-file", run.StopName);
        Assert.Equal("den-run:called-off", run.DenRunEnd);
        Assert.Equal(1, Made(w, StationKinds.DenRun));
        Assert.Single(w.Lines, l => l.Contains("the den run is called off: the den-run station is cut where she stands", StringComparison.Ordinal));
    }

    /// <summary>M6 item 14: the rehearsal's switch names the den run's order by itself, sets the rehearsal, and is off by default; the journey's fact follows it.</summary>
    [Fact]
    public void The_rehearsal_switch_is_the_den_runs_order_with_the_rehearsal_set_and_is_off_by_default()
    {
        Assert.True(OptionCatalog.Registry.IsKnown(AutopilotOptions.DenRunRehearseOption));
        var plain = Parse("--autopilot-job=level-up", AutopilotOptions.DenRunOption);
        Assert.Equal(AutopilotTaskKind.DenRun, plain.Task.Kind);
        Assert.False(plain.DenRunRehearse);

        var rehearse = Parse("--autopilot-job=level-up", AutopilotOptions.DenRunRehearseOption);
        Assert.Empty(rehearse.Errors);
        Assert.Equal(AutopilotTaskKind.DenRun, rehearse.Task.Kind);
        Assert.True(rehearse.DenRunRehearse);
        Assert.False(Parse("--autopilot-job=level-up", AutopilotOptions.DenRunRehearseOption + "=off").DenRunRehearse);
        Assert.Equal("danbys-to-desert-march", DenRunStation.RehearseBeforeEdge);
        Assert.Contains(Lifestone.Client.Session.Travel.TravelRoutes.Default.RoutesFor(DenRunStation.DefaultGround).Single(r => r.Key == DenRunStation.RouteKey).Steps, s => s.Edge.Key == DenRunStation.RehearseBeforeEdge);
    }

    [Fact]
    public void A_run_that_was_cut_is_not_begun_again()
    {
        var w = AtHome();
        _ = new Script(w) { Run = _ => null }; // the station never ends: the Travel state's watchdog cuts it
        var run = w.Autopilot(Order with { WatchdogScale = 0.1 });

        Assert.Equal(ScenarioStatus.Failed, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal("den-run:cut", run.StopName);
        Assert.Equal(1, Made(w, StationKinds.DenRun));
    }

    // ------------------------------------------------------------------------------------------------ the run-only road

    [Fact]
    public void While_the_road_is_run_only_the_survival_layer_holds_nothing_and_the_station_is_stepped_every_frame()
    {
        var w = AtHome();
        var script = new Script(w);
        JourneyContext? made = null;
        w.Prime = c => made = c;
        var steps = 0;
        script.Run = step =>
        {
            steps = step + 1;
            if (step == 0)
            {
                made!.Facts.RunOnly = "the den run to the test's den"; // as the station sets it for its road
            }

            if (step < 40)
            {
                return null;
            }

            made!.Facts.RunOnly = null;
            return ScenarioStatus.Succeeded;
        };
        var run = w.Autopilot(Order);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(run, null, 8000, _ => AnswerWieldsAndModes(w)));
        Assert.Equal(AutopilotScenario.DenRunTiedStop, run.StopName);
        Assert.Equal(41, steps);
        Assert.Single(w.Lines, l => l.Contains("survival: travel: stand-down: run-only: the den run to the test's den: the layer and the vitals guard hold nothing", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("survival: travel: wait:", StringComparison.Ordinal));
        Assert.Equal(0, run.SurvivalHolds);
    }
}
