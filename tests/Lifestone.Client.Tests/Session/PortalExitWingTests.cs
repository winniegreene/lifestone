using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Academy's topology that the exit scenario has to live with (ST16 part 2, measured on the dats): the rooms around the
/// Academy start and the wing that holds "Exit to Holtburg" share no walkable doorway, so the live scenario is sent to a room of
/// the portal's wing (<see cref="PortalExit.WingStart"/>) instead of walking there from the start.
/// </summary>
public sealed class PortalExitWingTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint AcademyStart = AcademyExitScenario.AcademyStartCell;

    private LandblockCellSet Cells() => LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), LandblockCoord.FromId(AcademyStart));

    [RequiresDatsFact]
    public void The_exit_portals_wing_is_not_reachable_from_the_Academy_start_by_walkable_doorways()
    {
        var cells = Cells();
        var planner = new CellRoutePlanner(cells);
        Assert.NotNull(cells.GetCell(PortalExit.ExitToHoltburgCell));
        Assert.Null(planner.FindPath(AcademyStart, PortalExit.ExitToHoltburgCell));
        Assert.NotNull(planner.FindPath(AcademyStart, AcademyStart));
    }

    [RequiresDatsFact]
    public void A_wing_start_is_a_room_a_few_doorways_from_the_portal_with_a_walkable_way_to_it()
    {
        var cells = Cells();
        var exit = new PortalExit(cells, PortalCell: PortalExit.ExitToHoltburgCell);
        var start = exit.WingStart();
        Assert.NotNull(start);
        var path = new CellRoutePlanner(cells).FindPath(start.Value.CellId, PortalExit.ExitToHoltburgCell);
        Assert.NotNull(path);
        output.WriteLine($"wing start 0x{start.Value.CellId:X8} at {start.Value.Frame.Origin}; {path.Count - 1} doorway(s): {string.Join(" > ", path.Select(c => $"0x{c:X8}"))}");
        Assert.InRange(path.Count - 1, 1, PortalExit.WingDoorways);
        Assert.NotEqual(AcademyStart, start.Value.CellId);
        Assert.Equal(PortalExit.ExitToHoltburgCell, path[^1]);
    }

    [RequiresDatsFact]
    public void Without_a_known_portal_cell_there_is_no_wing_start()
    {
        var cells = Cells();
        Assert.Null(new PortalExit(cells).WingStart());
        Assert.Null(new PortalExit(cells, PortalCell: 0x86020FFF).WingStart());
    }
}
