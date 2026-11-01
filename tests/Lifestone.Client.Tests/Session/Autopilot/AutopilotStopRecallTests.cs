using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 F18 ("never log off on the hunting ground"): the autopilot casts the recall home and waits until home before it stops by a stop rule, a stop request or a failure that leaves the session usable; a stop at home recalls
/// nothing, the person's interrupt recalls nothing, and a failed recall is tried once more and then the stop goes on, named. Over the scripted ACE of <see cref="AutopilotTests"/>.
/// </summary>
public sealed class AutopilotStopRecallTests(ITestOutputHelper output)
{
    private static bool Home(ApWorld w) => AutopilotScenario.Classify(w.Trip.Vendor.H.State, w.Ground) == AutopilotPlace.Home;

    [Theory]
    [InlineData(Den, "level")]
    [InlineData(Den, "request")]
    [InlineData(Temple, "level")]
    [InlineData(Temple, "request")]
    public void A_stop_rule_or_request_on_the_ground_recalls_home_first_and_the_done_line_follows_the_recall(string key, string rule)
    {
        var w = new ApWorld(key, output, atHome: false);
        var arrows = new List<AutopilotTransition>();
        var autopilot = w.Autopilot(new AutopilotOptions { Level = rule == "level" ? 10 : 0 });
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 2 });
        Assert.False(Home(w));
        if (rule == "level")
        {
            w.Level(10);
        }
        else
        {
            autopilot.RequestStop("test");
        }

        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(autopilot.Ok);
        Assert.Equal(rule == "level" ? "level" : "request:test", autopilot.StopName);
        Assert.True(Home(w), "the mage is home when the run stops");
        Assert.Equal(["buff", "den-fight", "recall-home"], Kinds(w));
        Assert.Equal(AutopilotState.Fight, arrows[^3].To);
        Assert.Equal([AutopilotState.Travel, AutopilotState.Stopped], arrows.TakeLast(2).Select(a => a.To)); // the fight, the recall home, the stop
        var lines = w.Lines.ToList();
        var recall = lines.FindIndex(l => l.Contains("autopilot: recalling home before the stop", StringComparison.Ordinal));
        var settled = lines.FindIndex(l => l.Contains("autopilot: the mage is home; the stop goes on", StringComparison.Ordinal));
        var stopped = lines.FindIndex(l => l.Contains($"autopilot stop={autopilot.StopName}", StringComparison.Ordinal));
        Assert.True(recall >= 0 && settled > recall && stopped > settled, $"recall={recall} settled={settled} stop={stopped}");
        Assert.StartsWith("autopilot done ok=yes ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_minutes_rule_on_the_ground_recalls_home_first_too()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions { Minutes = 0.5 });
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Equal("minutes", autopilot.StopName);
        Assert.True(Home(w));
        Assert.Equal("recall-home", Kinds(w)[^1]);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_stop_at_home_does_not_recall(string key)
    {
        var w = new ApWorld(key, output);
        var autopilot = w.Autopilot(new AutopilotOptions());
        autopilot.RequestStop("test"); // due before the first station: the mage stands at the lifestone
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Equal("request:test", autopilot.StopName);
        Assert.Empty(Kinds(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("recalling home before the stop", StringComparison.Ordinal));
        Assert.True(Home(w));
    }

    [Fact]
    public void The_interrupt_does_not_recall_the_person_has_the_keys()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.Abandon("interrupt"); // what the dial does when a movement key is pressed
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, w.Run(autopilot, frames: 1));
        }

        Assert.DoesNotContain("recall-home", Kinds(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("recalling home before the stop", StringComparison.Ordinal));
        Assert.False(Home(w));
        Assert.Null(autopilot.StopName);
    }

    [Fact]
    public void A_failed_recall_is_tried_once_more_and_on_the_ground_every_30_s_for_5_minutes_before_the_stop_goes_on_named()
    {
        var w = new ApWorld(Den, output, atHome: false);
        w.Factory.FailWith = spec => spec.Kind == StationKinds.RecallHome ? "scripted: the cast fizzled" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("test");

        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("request:test", autopilot.StopName); // the stop is the one asked for; the failed recall is named beside it
        Assert.True(autopilot.Ok);
        var recalls = Kinds(w).Count(k => k == "recall-home");
        Assert.InRange(recalls, 6, 12); // the first try, the one more, then a retry every 30 s while the 5 minutes last (M5 7 G2)
        Assert.Contains(w.Lines, l => l.Contains("the recall home before the stop failed", StringComparison.Ordinal) && l.Contains("it is tried once more", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("failed twice", StringComparison.Ordinal) && l.Contains("so it does not log off here: the recall is tried again in 30 s", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the recall home before the stop failed twice", StringComparison.Ordinal) && l.Contains("the stop goes on with the mage away from home", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("TODO(m5-autopilot-stop-recall)", StringComparison.Ordinal)); // named, loud
        Assert.False(Home(w));
        Assert.StartsWith("autopilot done ok=yes ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_G2_a_retry_that_lands_after_the_two_tries_failed_stops_at_home_and_logs_nothing_off_in_the_ground()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.FailWith = spec => spec.Kind == StationKinds.RecallHome && ++made <= 3 ? "scripted: the cast fizzled" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("test");

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Equal(4, Kinds(w).Count(k => k == "recall-home"));
        Assert.True(Home(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("TODO(m5-autopilot-stop-recall)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the recall is tried again in 30 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recall_that_fails_once_and_lands_the_second_time_stops_at_home()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.FailWith = spec => spec.Kind == StationKinds.RecallHome && ++made == 1 ? "scripted: the cast fizzled" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("test");

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Equal(2, Kinds(w).Count(k => k == "recall-home"));
        Assert.True(Home(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("failed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failure_that_leaves_the_session_usable_recalls_home_before_it_stops_and_a_dropped_session_does_not()
    {
        // a failure on the ground (the counters' stop in a trip is the same shape): the stop is the failure's, the recall comes first
        var w = new ApWorld(Den, output, atHome: false);
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Logistics ? "logistics: stop: a line is under its stop line" : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });
        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.StartsWith("failed:trip:logistics:", autopilot.StopName, StringComparison.Ordinal);
        Assert.False(autopilot.Ok);
        Assert.Equal("recall-home", Kinds(w)[^1]);
        Assert.True(Home(w));
        Assert.StartsWith("autopilot done ok=no ", autopilot.Lines[^1], StringComparison.Ordinal);

        // a session that is gone: nothing to cast with
        var d = new ApWorld(Den, output, atHome: false);
        var dropped = d.Autopilot(new AutopilotOptions());
        d.Run(dropped, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        d.Drop();
        Assert.Equal(ScenarioStatus.Failed, d.Run(dropped));
        Assert.Equal(AutopilotScenario.NoReconnectStop, dropped.StopName);
        Assert.DoesNotContain("recall-home", Kinds(d));
    }
}
