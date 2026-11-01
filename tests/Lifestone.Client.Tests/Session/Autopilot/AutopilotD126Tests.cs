using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// D126 (a), soak round 12's 10:27 UTC trip: 15 lines came due inside the trip and lapsed in it. The autopilot asks its fight, before the stop for a trip, to recast the lines due within the trip's expected length (its mean, seeded
/// 120 s from round 12's trips, plus the lookahead margin); the trip's length is measured and joins the mean.
/// </summary>
public sealed class AutopilotD126Tests(ITestOutputHelper output)
{
    /// <summary>A scripted fight that records the trip's refresh ask (as <see cref="FightScenario"/> takes it).</summary>
    private sealed class LeavingFight(ScriptedFight inner) : IStationRun, IStoppableFight, IBuffRefreshFight
    {
        public List<(double Horizon, double Reach, string Detail, bool Stopped)> Leaves { get; } = [];

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

        public void RequestLeaveRefresh(double horizonSeconds, double reachMetres, string detail) => Leaves.Add((horizonSeconds, reachMetres, detail, inner.StopReason is not null));

        public ScenarioStatus Step(IGameSessionView session, double now) => inner.Step(session, now);

        public string VerdictFields() => inner.VerdictFields();
    }

    [Fact]
    public void A_trip_asks_the_fight_to_recast_the_lines_due_within_the_trips_length_before_it_stops_and_the_trip_is_measured()
    {
        var w = new ApWorld(Den, output);
        var fights = new List<LeavingFight>();
        w.Factory.WrapFight = f =>
        {
            var wrapped = new LeavingFight(f);
            fights.Add(wrapped);
            return wrapped;
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        Assert.Equal(AutopilotScenario.TripSeedSeconds, autopilot.TripMeanSeconds);
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Contains(StationKinds.Logistics, w.Factory.Made.Select(s => s.Kind));
        var leave = Assert.Single(fights.SelectMany(f => f.Leaves));
        Assert.Equal(AutopilotScenario.TripSeedSeconds + new Lifestone.Client.Session.Mage.BuffOptions().LookaheadMarginSeconds, leave.Horizon); // 120 + 60
        Assert.False(leave.Stopped, "asked before the stop");
        Assert.StartsWith("forced-trip: the trip's mean 120 s + margin 60 s", leave.Detail, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("autopilot trip: it took ", StringComparison.Ordinal) && l.Contains("the trip's mean is now ", StringComparison.Ordinal));
        Assert.NotEqual(AutopilotScenario.TripSeedSeconds, autopilot.TripMeanSeconds);
    }
}
