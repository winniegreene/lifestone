using Lifestone.Client.Movement.Wayfinding;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>The world routes file and its search on a made-up world of three landblocks: 0x0101 has one component, 0x0102 two (only the second joined to the first landblock, one way, as a drop is), 0x0103 one.</summary>
public sealed class WorldRoutesTests : IDisposable
{
    private readonly MemoryStream _file = new();

    public void Dispose() => _file.Dispose();

    private MemoryStream Rewound()
    {
        _file.Position = 0;
        return _file;
    }

    private WorldRoutes Load()
    {
        WorldRoutes.Write(
            _file,
            new ushort[] { 0x0101, 0x0102, 0x0103 },
            new[] { 0, 1, 3, 4 },
            new[] { 0, 1, 3, 4 },
            new ushort[] { 0, 0, 1, 0 },
            new byte[] { 0, 9, 9, 0 },
            new ushort[] { 0x0101, 0x0102, 0x0102, 0x0103 },
            new[] { 10, 20, 5, 10 },
            new[] { 96f, 200f, 300f, 500f },
            new[] { 96f, 96f, 96f, 96f },
            new[] { 0, 1, 1, 2, 3 },
            new[] { 2, 3, 2 });
        return WorldRoutes.Load(Rewound());
    }

    [Fact]
    public void A_region_finds_its_component_and_a_cell_its_largest()
    {
        var routes = Load();
        Assert.Equal(4, routes.Components);
        Assert.Equal(2, routes.RegionCount(0x01020000));
        Assert.Equal(0, routes.RegionCount(0x09090000));
        Assert.Equal(2, routes.ComponentOfRegion(0x01020000, 1));
        Assert.Equal(-1, routes.ComponentOfRegion(0x01020000, 2));
        Assert.Equal(1, routes.ComponentAtCell(0x01020000, 9));
        Assert.Equal(-1, routes.ComponentAtCell(0x01020000, 3));
        Assert.Equal(0x01030000u, routes.LandblockOf(3));
    }

    [Fact]
    public void A_route_goes_through_the_ground_that_joins_and_a_drop_goes_one_way()
    {
        var search = new WorldRouteSearch(Load());
        Assert.Equal(new[] { 0, 2, 3 }, search.Route(0, 3));
        Assert.Null(search.Route(3, 0));
        Assert.Null(search.Route(1, 3));
        Assert.Equal(new[] { 2 }, search.Route(2, 2));
    }

    /// <summary>Two landblocks that touch only at a corner, each one component; <paramref name="joined"/> links the first to the second.</summary>
    private (NavMap Map, WorldRouteSearch Search, uint From, uint To) Corner(bool joined)
    {
        var (from, to) = (Block(0x10, 0x10), Block(0x11, 0x11));
        var map = Map(Flat(from), Flat(to));
        var (a, b) = (map.RegionsOf(from), map.RegionsOf(to));
        var cells = a.Concat(b).Select(r => (byte)r.Cell).ToArray();
        WorldRoutes.Write(
            _file,
            new ushort[] { 0x1010, 0x1111 },
            new[] { 0, a.Count, cells.Length },
            new[] { 0, 1, 2 },
            new ushort[cells.Length],
            cells,
            new ushort[] { 0x1010, 0x1111 },
            new[] { 36864, 36864 },
            new[] { 3168f, 3360f },
            new[] { 3168f, 3360f },
            new[] { 0, joined ? 1 : 0, joined ? 1 : 0 },
            joined ? new[] { 1 } : Array.Empty<int>());
        return (map, new WorldRouteSearch(WorldRoutes.Load(Rewound())), from, to);
    }

    [Fact]
    public void A_goal_across_a_landblock_corner_walks_to_the_corner_for_the_side_tiles()
    {
        var (map, search, from, to) = Corner(joined: true);
        var planner = new PathPlanner(map) { WorldRoutes = search };
        var result = planner.Plan(P(100.5f, 100.5f, from), NavGoal.ToPoint(NavPoint.At(to, 90.5f, 90.5f)));
        Assert.Equal(NavStatus.Horizon, result.Status);
        Assert.Equal(new[] { to }, result.Corridor);
        Assert.Equal(from, result.Route!.End.LandblockId);
        Assert.True(result.Route.End.Local.X > 185f && result.Route.End.Local.Y > 185f, $"ended at {result.Route.End.Local}");
        Assert.Equal(1, planner.WorldRoutePlans);
    }

    [Fact]
    public void A_goal_the_land_does_not_join_stays_no_path_and_says_why()
    {
        var (map, search, from, to) = Corner(joined: false);
        var result = new PathPlanner(map) { WorldRoutes = search }.Plan(P(100.5f, 100.5f, from), NavGoal.ToPoint(NavPoint.At(to, 90.5f, 90.5f)));
        Assert.Equal(NavStatus.NoPath, result.Status);
        Assert.Equal("no-path", result.Reason);
        Assert.Contains("does not join", result.WorldRouteNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_format_is_refused_by_name()
    {
        _file.Write(new byte[32]);
        Assert.Throws<InvalidDataException>(() => WorldRoutes.Load(Rewound()));
    }
}
