using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 7 AX, on the dats (skips without them): the tusk pull's stand point is real. The stand in 0x5748013D is reachable by the room planner from the Burrow's arrival, and it stands about 35 m from the holder of slot 6 (102.50, -149.50, -23.99, the survey's SQL read), outside the 24 m sight of the hall's nearest tuskers, slots 5 and 7. A wrong cell or point in the profile fails here, not in a live run.
/// The clear line itself is not pinned: the sweep is noisy on these floors (no point of 546 clear on all 18 jittered samples, even at 15 m), so only a live shot proves it: TODO(m6-tusk-pull-live).
/// </summary>
public sealed class TuskPullDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly Vector3 Holder = new(102.50f, -149.50f, -23.99f);
    private static readonly Vector3 Slot5 = new(102.96f, -139.25f, -23.99f);
    private static readonly Vector3 Slot7 = new(107.21f, -139.14f, -23.99f);

    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    private static PullStand Stand => Burrow.Hunt!.Pulls[0].Stand!;

    [Fact]
    public void The_stand_stands_outside_the_sight_of_slots_5_and_7()
    {
        foreach (var slot in new[] { Slot5, Slot7 })
        {
            Assert.True(Vector3.Distance(Stand.Point, slot) > 24f, slot.ToString()); // in three dimensions: the stand is 8.7 m above them
            Assert.InRange(Vector3.Distance(Stand.Point, Holder), 34.5f, 36f);
        }
    }

    [RequiresDatsFact]
    public void The_room_planner_reaches_the_stand_from_the_arrival()
    {
        using var walk = GroundWalk.Create(fixture.Dats, Burrow, NullLogSink.Instance);

        var route = walk.Planner.Plan(Burrow.Arrival.Cell, Burrow.Arrival.Origin, Stand.Cell, Stand.Point);

        Assert.NotNull(route);
    }
}
