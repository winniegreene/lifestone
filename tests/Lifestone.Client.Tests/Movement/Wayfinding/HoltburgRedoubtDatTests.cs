using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 redoubt (the owner's decision of 2026-11-18: the rung <c>outskirts</c> hunts the Holtburg Redoubt, landblock 0x0163), on the retail dats: the profile's <c>galleries</c> section, fact by fact, and the walk it makes possible.
/// The Redoubt's entrance hall has a walkway over it, and the room 0x01630133 a landing: their floor polygons belong to cells of the rooms below, and a body on them stands in the floorless cell above. The ground's storeys leave
/// floorless cells out, so without the section the room graph reaches 8 cells and one slot from the arrival; with it every walkable cell, and the whole tour walks with the fight walks' steering, each leg on its first plan.
/// 17 of the 18 slots stand in storey cells; the eighteenth's row names a cell that holds the walkway's floor (<c>TODO(m6-redoubt-gallery-slot)</c>).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class HoltburgRedoubtDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FighterRunSkill = 230;

    private static HuntingGround Ground => GroundCatalog.Get("holtburg-redoubt");

    private LandblockCellSet All() => LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), GroundRooms.CoordOf(Ground));

    [RequiresDatsFact]
    public void A_gallery_cell_holds_no_floor_and_the_cells_under_it_hold_its_floor_at_its_own_height()
    {
        var all = All();
        var galleries = Ground.Galleries!;
        Assert.Equal(4, galleries.Cells.Count);
        Assert.Equal(8, galleries.Floors.Count);
        foreach (var id in galleries.Cells.Cells)
        {
            var cell = all.GetCell(id)!;
            Assert.Empty(all.GetStructure(cell).Floors);
            Assert.True(Ground.IsWalkable(id), $"0x{id:X8} is a storey cell");
            Assert.Contains(cell.Portals, p => !p.LeadsOutside && galleries.Floors.Contains(p.OtherCellId));
        }

        foreach (var id in galleries.Floors.Cells)
        {
            var cell = all.GetCell(id)!;
            var above = all.GetCell(Assert.Single(cell.Portals).OtherCellId)!; // its one portal is the opening overhead
            Assert.True(galleries.Cells.Contains(above.Id), $"0x{id:X8} opens into the gallery cell 0x{above.Id:X8}");
            Assert.False(Ground.IsWalkable(id));
            Assert.All(all.GetStructure(cell).Floors, f => Assert.All(f.Vertices, v => Assert.Equal(above.Frame.Origin.Z, cell.Frame.Origin.Z + v.Z, 2))); // its floor polygons lie at the gallery's height, a storey over its own origin
        }

        // the slot the tour does not count: the world row's cell is a floor cell of the hall's walkway (holtburg-redoubt.json sources.respawn)
        var offStorey = Ground.Respawn.Slots.Where(s => !Ground.IsWalkable(s.Cell)).ToArray();
        Assert.Equal([(0x01630178u, 7u)], offStorey.Select(s => (s.Cell, s.Wcid)));
        Assert.True(galleries.Floors.Contains(0x01630178));
        Assert.Equal(17, Ground.Respawn.Slots.Count(s => Ground.IsWalkable(s.Cell)));
        Assert.All(Ground.Respawn.Slots.Where(s => Ground.IsWalkable(s.Cell)), s => Assert.Contains(s.Cell, Ground.Tour));
    }

    [RequiresDatsFact]
    public void Without_the_galleries_the_arrival_reaches_eight_cells_and_with_them_every_walkable_cell()
    {
        var all = All();
        var ground = Ground;
        var bare = new RoomGraph(LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => ground.IsWalkable(c.Id) && !ground.Galleries!.Cells.Contains(c.Id))), ground.Walk.DoorwayLean);
        var reached = ground.Walkable.Cells.Where(c => !ground.Galleries!.Cells.Contains(c) && (c == ground.Arrival.Cell || bare.FindPath(ground.Arrival.Cell, c) is not null)).ToArray();
        output.WriteLine($"without the galleries: {reached.Length} cells reached: {string.Join(" ", reached.Select(c => (c & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture)))}");
        Assert.Equal(8, reached.Length);
        Assert.Equal([0x016301A3u], ground.Tour.Where(reached.Contains));

        var graph = GroundRooms.GraphOf(all, ground);
        Assert.All(ground.Walkable.Cells.Where(c => c != ground.Arrival.Cell), c => Assert.NotNull(graph.FindPath(ground.Arrival.Cell, c)));
    }

    /// <summary>The whole tour from the arrival with the predictive steering (the fight walks' since 2026-11-16): every leg arrives on its first plan with no rung, and the walk crosses both galleries with the body in their floorless cells.</summary>
    [RequiresDatsFact]
    public void The_redoubts_whole_tour_walks_and_crosses_the_galleries()
    {
        var dats = fixture.Dats;
        var ground = Ground;
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(GroundRooms.CoordOf(ground)));
        var graph = GroundRooms.GraphOf(All(), ground);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "redoubt-tour");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        world.UseTime(0.0);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var failures = new List<string>();
        var stoodIn = new HashSet<uint>();
        var tour = ground.Tour.Distinct().ToArray();
        foreach (var cellId in tour)
        {
            var spot = graph.RoomFrame(cellId);
            Assert.NotNull(spot);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "tour-" + (cellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture), PredictiveSteering = true });
            runner.Start(NavGoal.ToPoint(new NavPoint(cellId & 0xFFFF0000u, spot!.Value.Origin), cellId), now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / 60;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / 60);
                stoodIn.Add(walker.Physics.Position.CellId);
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"0x{cellId & 0xFFFF:X4} {runner.State} {runner.RouteMetres:0.0} m in {runner.RunSeconds:0.0} s plans={runner.Plans} rungs={runner.Rungs} {runner.FailureReason}");
            output.WriteLine(line);
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || runner.Replans != 0)
            {
                failures.Add(line);
            }
        }

        Assert.Equal(15, tour.Length);
        Assert.Empty(failures);
        Assert.All(ground.Galleries!.Cells.Cells, c => Assert.Contains(c, stoodIn)); // the body's cell on a gallery is the floorless cell
        Assert.DoesNotContain(stoodIn, ground.IsGalleryFloor); // and never a cell that holds the floor
    }
}
