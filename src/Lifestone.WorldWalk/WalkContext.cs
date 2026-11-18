using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.WorldWalk;

/// <summary>What every walking thread shares: the dats and the caches over them.</summary>
internal sealed class WalkContext
{
    /// <summary>Finished landblocks kept: three rows of the map, as a run goes row by row, each 255 long. Not retail: the walk's own.</summary>
    public const int StoreCapacity = 800;

    public WalkContext(DatSet dats)
    {
        Dats = dats;
        Geometry = new PhysicsGeometryLibrary(dats);
        Structures = new CellStructureLibrary(dats);
        Animations = new DatAnimationSource(dats);
        Store = new CollisionStore(new LandblockCollisionBuilder(dats, Geometry, Structures), StoreCapacity);
    }

    public DatSet Dats { get; }

    public PhysicsGeometryLibrary Geometry { get; }

    public CellStructureLibrary Structures { get; }

    public DatAnimationSource Animations { get; }

    public CollisionStore Store { get; }

    /// <summary>Whether the room legs steer predictively (<c>--predictive</c>: <see cref="Lifestone.Client.Movement.Wayfinding.NavRunnerOptions.PredictiveSteering"/>).</summary>
    public bool PredictiveSteering { get; init; }

    /// <summary>The room leg traced step by step (<c>--trace-leg=FROM-TO</c>, the cells' low words), or null.</summary>
    public string? TraceLeg { get; init; }

    /// <summary>The folder a traced leg's file is written to (the run's).</summary>
    public string? TraceFolder { get; init; }

    /// <summary>The tile of <paramref name="coord"/> as a walk would see it: with its neighbours attached, so their shadows and sea walls are stamped.</summary>
    public NavTile Tile(LandblockCoord coord, out NavBuildStats stats)
    {
        var neighbours = new List<LandblockCollision>(8);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && coord.Offset(dx, dy) is { } near && LandDefs.IsOnMap(near))
                {
                    neighbours.Add(Store.Get(near));
                }
            }
        }

        return NavTileBuilder.Build(Store.Get(coord), new NavBuildOptions { Neighbours = neighbours }, out stats);
    }

    /// <summary>A rig for one thread.</summary>
    public OfflineWalkRig NewRig(ILogSink? log = null) => new(Dats, Geometry, Animations, Store.Get, log ?? NullLogSink.Instance);

    /// <summary>The walkable node nearest the middle of a tile, with room round it and dry; null when the tile has none.</summary>
    public static (int X, int Y)? MiddleNode(NavTile tile)
    {
        const int Middle = NavTile.Size / 2;
        for (var r = 0; r < Middle; r++)
        {
            for (var y = Middle - r; y <= Middle + r; y++)
            {
                for (var x = Middle - r; x <= Middle + r; x++)
                {
                    var onRing = Math.Max(Math.Abs(x - Middle), Math.Abs(y - Middle)) == r;
                    if (onRing && NavTile.InBounds(x, y) && tile.IsWalkable(x, y) && tile.Clearance[NavTile.Index(x, y)] >= 2 && (tile.Flags[NavTile.Index(x, y)] & NavFlags.Water) == 0)
                    {
                        return (x, y);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The land id of <paramref name="coord"/> as a <see cref="NavPoint"/> wants it.</summary>
    public static uint BlockId(LandblockCoord coord) => ((uint)coord.X << 24) | ((uint)coord.Y << 16);

    /// <summary>The middle point of a node on its tile, in that landblock's metres.</summary>
    public static NavPoint PointOf(NavTile tile, int x, int y)
    {
        var centre = NavTile.NodeCentre(x, y);
        return new NavPoint(tile.LandblockId, new Vector3(centre.X, centre.Y, tile.Z[NavTile.Index(x, y)]));
    }
}
