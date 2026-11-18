using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The predictive steering's hub bench (the owner's ruling, 2026-11-15: flawless-looking walking, because it is just math; docs/wayfinding.md "Predictive steering"): the Town Network's
/// arrival 0x00070143 to and from its eleven portal rooms, and the Facility Hub's arrival 0x8A020212 to and from nine of its rooms, 40 legs between the rooms' frames, walked by the
/// explore-mode walker at 30 steps a second with the physics confirming. Every leg arrives without a rung, and the totals beat the ordinary steering's on the same legs, measured
/// on 2026-11-15 (walk/w0 at a529012a): key changes 1,718, turn reversals 245, presses with forward up 208, forward pressed again within 0.3 s of a release 78, D134 corner
/// turns in place 176, 697.8 s of running. Exhaustive tier: it reads the dats and skips without them.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class HubWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int OrdinaryKeys = 1718;

    private const int OrdinaryReversals = 245;

    private const int OrdinaryInPlace = 208;

    private const int OrdinaryRepresses = 78;

    private const int OrdinaryCornerTurns = 176;

    private const double OrdinarySeconds = 697.8;

    private static readonly ushort[] TownRooms = [0x0124, 0x0126, 0x012A, 0x012D, 0x013D, 0x0150, 0x015F, 0x0160, 0x016C, 0x0176, 0x0117];

    private static readonly ushort[] FacilityRooms = [0x0118, 0x0119, 0x011A, 0x011E, 0x011F, 0x0121, 0x0122, 0x0123, 0x01F4];

    [RequiresDatsFact]
    public void The_hubs_forty_legs_beat_the_ordinary_steering()
    {
        var (keys, reversals, inPlace, represses, cornerTurns, seconds) = (0, 0, 0, 0, 0, 0.0);
        var failures = new List<string>();
        foreach (var (hub, arrival, rooms) in new[] { (new LandblockCoord(0x00, 0x07), (ushort)0x0143, TownRooms), (new LandblockCoord(0x8A, 0x02), (ushort)0x0212, FacilityRooms) })
        {
            foreach (var room in rooms)
            {
                foreach (var (from, to) in new[] { (arrival, room), (room, arrival) })
                {
                    var runner = Walk(hub, from, to, out var line);
                    output.WriteLine(line);
                    keys += runner.KeyChanges;
                    reversals += runner.TurnReversals;
                    inPlace += runner.InPlacePresses;
                    represses += runner.ForwardRepresses;
                    cornerTurns += runner.CornerTurns;
                    seconds += runner.RunSeconds;
                    if (runner.State != NavRunState.Arrived || runner.Rungs != 0)
                    {
                        failures.Add(line);
                    }
                }
            }
        }

        var totals = string.Create(CultureInfo.InvariantCulture, $"keys {keys} (ordinary {OrdinaryKeys}), reversals {reversals} ({OrdinaryReversals}), in place {inPlace} ({OrdinaryInPlace}), forward again {represses} ({OrdinaryRepresses}), corner turns in place {cornerTurns} ({OrdinaryCornerTurns}), run {seconds:0.0} s ({OrdinarySeconds:0.0})");
        output.WriteLine(totals);
        Assert.Empty(failures);
        Assert.True(keys < OrdinaryKeys && reversals < OrdinaryReversals && inPlace < OrdinaryInPlace && represses < OrdinaryRepresses && cornerTurns < OrdinaryCornerTurns && seconds < OrdinarySeconds, totals);
    }

    private NavRunner Walk(LandblockCoord coord, ushort fromLow, ushort toLow, out string line)
    {
        var lb = ((uint)coord.X << 24) | ((uint)coord.Y << 16);
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var structures = new CellStructureLibrary(fixture.Dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, geometry, structures).Build(coord));
        var graph = new RoomGraph(LandblockCellSet.Load(structures, coord), RoomGraph.BuildingDoorwayLean);
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, "hub-walk");
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), geometry, new DatAnimationSource(fixture.Dats), sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        var from = lb | fromLow;
        var to = lb | toLow;
        Assert.True(walker.Place(new CellFrame(from, new AcFrame(graph.RoomFrame(from)!.Value.Origin + new Vector3(0f, 0f, 0.05f), Quaternion.Identity))));
        using var check = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), check, audit, new NavRunnerOptions { Name = "hub", ScarClock = time, PredictiveSteering = true }, NullLogSink.Instance);
        runner.Start(NavGoal.ToPoint(new NavPoint(lb, graph.RoomFrame(to)!.Value.Origin), to), now);
        while (!runner.IsDone && now < 120)
        {
            now += 1.0 / 30;
            time.Advance(1.0 / 30);
            walker.Controller.Tick(now);
            runner.Step(now);
            session.Tick(now);
        }

        runner.Dispose();
        line = string.Create(CultureInfo.InvariantCulture, $"0x{lb | fromLow:X8} to 0x{lb | toLow:X8}: {runner.State} rungs={runner.Rungs} keys={runner.KeyChanges} {runner.SteeringFields()} run_s={runner.RunSeconds:0.0}");
        return runner;
    }
}
