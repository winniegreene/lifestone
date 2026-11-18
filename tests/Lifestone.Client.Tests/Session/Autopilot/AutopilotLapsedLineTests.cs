using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 (the lead, 2026-11-16, on live ladder42: no line she can cast lapses inside a round, whatever its length; a round that would outlast a line the fight cannot recast safely ends in its trip first). The fight recasts a lapsed line
/// between its casts at once (the hard floor, F25: live 19:51:21 UTC, 2 s after the read); a line still down after the hard floor's window calls the round's trip, forced, in a lull.
/// </summary>
public sealed class AutopilotLapsedLineTests(ITestOutputHelper output)
{
    /// <summary>A scripted fight whose line runs out at <paramref name="lapseAtKill"/> kills and is recast after <paramref name="recastAfterSeconds"/> (infinity: never).</summary>
    private sealed class LapsingFight(ScriptedFight inner, int lapseAtKill, double recastAfterSeconds) : IStationRun, IStoppableFight, IBuffRefreshFight
    {
        private double _lapsedAt = double.NaN;

        public string Name => inner.Name;

        public string Stage => inner.Stage;

        public string? Failure => inner.Failure;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => inner.Ok;

        public int Kills => inner.Kills;

        public FightStopReason? StopReason => inner.StopReason;

        public bool BuffRefreshAsked => false;

        public int BuffRefreshes => 0;

        public bool BuffsKeptAhead => true;

        public void RequestStop(FightStopReason reason) => inner.RequestStop(reason);

        public void RequestBuffRefresh(double marginSeconds, double reachMetres, string detail)
        {
        }

        public (string Line, double DownSeconds)? LapsedDown(double clock)
        {
            if (double.IsNaN(_lapsedAt) || clock - _lapsedAt >= recastAfterSeconds)
            {
                return null;
            }

            return ("Impenetrability", clock - _lapsedAt);
        }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (double.IsNaN(_lapsedAt) && inner.Kills >= lapseAtKill)
            {
                _lapsedAt = now;
            }

            return inner.Step(session, now);
        }

        public string VerdictFields() => inner.VerdictFields();
    }

    private static (ApWorld World, AutopilotScenario Autopilot) Run(ITestOutputHelper output, double recastAfterSeconds, double seconds)
    {
        var w = new ApWorld(Den, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded();
        w.Factory.WrapFight = f => new LapsingFight(f, lapseAtKill: 3, recastAfterSeconds);
        var autopilot = w.Autopilot(new AutopilotOptions { HourLoad = new CycleLoadSettings(On: false) });
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var until = w.Now + seconds;
        w.Run(autopilot, a => w.Now >= until || a.Cycles >= 1);
        return (w, autopilot);
    }

    [Fact]
    public void A_line_still_down_past_the_hard_floors_window_calls_the_rounds_trip_first()
    {
        var (w, autopilot) = Run(output, double.PositiveInfinity, 120);

        var why = new BuffOptions().HardMarginSeconds;
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot fight: a trip is due: Impenetrability ran out ", StringComparison.Ordinal) && l.Contains($"within its hard floor's {why:0} s (F25): the round's trip comes first", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("autopilot fight: stop asked (logistics-trip)", StringComparison.Ordinal));
        Assert.Contains(StationKinds.Logistics, w.Factory.Made.Select(s => s.Kind));
        Assert.Contains(w.Lines, l => l.Contains("trigger=forced", StringComparison.Ordinal)); // forced: the station's counters know nothing of the line
        Assert.True(autopilot.Cycles >= 1);
    }

    [Fact]
    public void A_line_the_fight_recasts_within_the_window_calls_no_trip()
    {
        // live ladder42 19:51:19 -> 19:51:21 UTC: read lapsed, recast between the fight's casts 2 s later
        var (w, _) = Run(output, recastAfterSeconds: 2, seconds: 120);

        Assert.DoesNotContain(w.Lines, l => l.Contains("the round's trip comes first", StringComparison.Ordinal));
        Assert.DoesNotContain(StationKinds.Logistics, w.Factory.Made.Select(s => s.Kind));
    }
}
