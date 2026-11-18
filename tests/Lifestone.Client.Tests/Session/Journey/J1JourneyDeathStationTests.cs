using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>M6 6 D (map-death 4): a plain journey's station that died is followed by one death station before the recovery goes on to the next station.</summary>
public sealed class J1JourneyDeathStationTests
{
    private static JourneyPlan Plan(params StationSpec[] stations) => new("j1", "BSD Mage", "test", StationWhere.Belt, 1, stations);

    private static StationSpec Spec(string name) => new(name, StationKinds.HarmHeal, StationNeeds.None, 30);

    [Fact]
    public void A_leg_that_fails_died_is_followed_by_the_death_station_with_the_observed_death_set()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory(new() { ["s2"] = new(Succeed: false, Failure: "died:run:respawned=0xA9B40019", Fields: "kills=0") });
        var context = t.Context();
        var journey = t.Journey(Plan(Spec("s1"), Spec("s2"), Spec("s3")), factory, context);

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal(["s1", "s2", "death", "s3"], factory.Started);
        Assert.NotNull(context.Facts.ObservedDeath); // the station here does not take it; the real death station does
        Assert.Contains(t.Log.Entries, e => e.Message.StartsWith("journey station=s2 died: the death station follows (corpse 0x", StringComparison.Ordinal));
        Assert.Contains(t.Log.Entries, e => e.Message == "journey stage recover:s2");
    }

    [Fact]
    public void A_failure_that_is_not_a_death_goes_to_the_recovery_as_before()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory(new() { ["s2"] = new(Succeed: false, Failure: "boom here", Fields: "kills=0") });
        var context = t.Context();
        var journey = t.Journey(Plan(Spec("s1"), Spec("s2"), Spec("s3")), factory, context);

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal(["s1", "s2", "s3"], factory.Started);
        Assert.Null(context.Facts.ObservedDeath);
        Assert.DoesNotContain(t.Log.Entries, e => e.Message.Contains("the death station follows", StringComparison.Ordinal));
    }

    [Fact]
    public void The_death_stations_own_failure_goes_to_the_recovery_once_and_makes_no_second_death_station()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory(new()
        {
            ["s2"] = new(Succeed: false, Failure: "died:run:respawned=0xA9B40019", Fields: "kills=0"),
            ["death"] = new(Succeed: false, Failure: "died:death:respawned=0xA9B40019", Fields: "deaths=2"),
        });
        var journey = t.Journey(Plan(Spec("s1"), Spec("s2"), Spec("s3")), factory, t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal(["s1", "s2", "death", "s3"], factory.Started);
        Assert.Single(t.Log.Entries, e => e.Message.Contains("the death station follows", StringComparison.Ordinal));
        Assert.Contains(t.Log.Entries, e => e.Message == "journey stage recover:death");
    }

    [Fact]
    public void A_planned_death_stations_failure_is_its_own_stop_and_makes_no_second_death_station()
    {
        var t = new J1JourneyRig();
        var planned = new StationSpec("death", StationKinds.Death, StationNeeds.None, 30);
        var factory = new J1Factory(new() { ["death"] = new(Succeed: false, Failure: "died:death:respawned=0xA9B40019", Fields: "deaths=1") });
        var journey = t.Journey(Plan(Spec("s1"), planned, Spec("s3")), factory, t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal(["s1", "death", "s3"], factory.Started);
        Assert.DoesNotContain(t.Log.Entries, e => e.Message.Contains("the death station follows", StringComparison.Ordinal));
        Assert.Contains(t.Log.Entries, e => e.Message == "journey stage recover:death");
    }

    [Fact]
    public void An_inserted_station_counts_in_the_verdicts_total_and_the_done_line_names_it()
    {
        var verdict = new JourneyVerdict(3);
        verdict.NoteInserted();

        Assert.Equal(4, verdict.Planned);
        Assert.Equal(1, verdict.Inserted);
        Assert.Contains(" stations=0/4 inserted=1 ", verdict.DoneLine("j", new JourneyFacts(), 0, 0), StringComparison.Ordinal);
    }
}
