using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M4 P5b fix FA: the sightless arrival is a typed end of the leg (<see cref="ITravelLeg.ArrivedWithoutSight"/>, P5b-R5), which the journey's travel forwards and counts; the done line carries
/// <c>rungs= blocked= sightless=</c> summed over the runs, report-only (P5b-R6).
/// </summary>
public sealed class FaJourneyFieldsTests
{
    [Fact]
    public void The_travel_forwards_the_typed_sightless_end_and_sums_rungs_blocked_and_sightless_for_the_done_line_FA()
    {
        var t = new J1JourneyRig();
        var runner = new FaRunner();
        t.Context(runners: runner);

        runner.Next = new FaLeg(TravelStatus.Arrived, "route_m=40.0 run_s=10.0 speed_mps=4.00 plans=1 replans=0 rungs=2 blocked=0 corrections=0 where_max_m=none", null, false);
        var arrived = t.Travel!.GoTo(t.View, Seek(), t.Now);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(arrived));
        Assert.False(arrived.ArrivedWithoutSight);

        runner.Next = new FaLeg(TravelStatus.Failed, "route_m=12.0 run_s=3.0 speed_mps=4.00 plans=1 replans=1 rungs=1 blocked=0 corrections=0 where_max_m=none", "travel:run:reworded: x", true);
        var sightless = t.Travel.GoTo(t.View, Seek(), t.Now);
        Assert.Equal(TravelStatus.Failed, t.RunLeg(sightless));
        Assert.True(sightless.ArrivedWithoutSight); // forwarded by the journey's run leg, whatever the failure's text

        runner.Next = new FaLeg(TravelStatus.Failed, "route_m=5.0 run_s=2.0 speed_mps=2.50 plans=2 replans=1 rungs=3 blocked=1 corrections=0 where_max_m=none", "travel:run:arrivedwithoutsight: but not typed", false);
        var blocked = t.Travel.GoTo(t.View, Seek(), t.Now);
        Assert.Equal(TravelStatus.Failed, t.RunLeg(blocked));
        Assert.False(blocked.ArrivedWithoutSight); // the text alone is not the end

        Assert.Equal((3, 6, 1, 1), (t.Travel.Runs, t.Travel.Rungs, t.Travel.Blocked, t.Travel.Sightless));
        Assert.Equal(57.0, t.Travel.RouteMetres, 3);
    }

    [Fact]
    public void The_done_line_prints_rungs_blocked_and_sightless_after_run_s_and_they_never_decide_ok_FA()
    {
        var verdict = new JourneyVerdict(1);
        verdict.Add(new JourneyStationResult("s1", true, 1.0, string.Empty));
        var facts = new JourneyFacts { TeleportBudget = 1 };

        var line = verdict.DoneLine("archer", facts, 57.04, 15.0, rungs: 6, blocked: 1, sightless: 1);

        Assert.Contains(" route_m=57.0 run_s=15.0 rungs=6 blocked=1 sightless=1 vitals_passes=", line, StringComparison.Ordinal);
        Assert.StartsWith("journey done ok=true ", line, StringComparison.Ordinal); // report-only: a sightless seek and a Blocked run fail nothing here
        Assert.Contains(" rungs=0 blocked=0 sightless=0 ", verdict.DoneLine("archer", facts, 0, 0), StringComparison.Ordinal);
    }

    private static TravelRequest Seek() => new(JourneyGrounds.Holtburg, TravelPurpose.Seek, ObjectGuid: 0x50000009, Range: 10, NeedSight: true);

    private sealed class FaRunner : IJourneyRunnerHost
    {
        public FaLeg? Next { get; set; }

        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => Next ?? throw new InvalidOperationException("no leg scripted");
    }

    private sealed class FaLeg(TravelStatus end, string fields, string? failure, bool sightless) : ITravelLeg
    {
        public TravelMeans Means => TravelMeans.Run;

        public string? Failure => end == TravelStatus.Failed ? failure : null;

        public string Fields => fields;

        public bool ArrivedWithoutSight => sightless;

        public TravelStatus Step(IGameSessionView session, double now) => end;

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose()
        {
        }
    }
}
