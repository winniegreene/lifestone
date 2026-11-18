using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.WorldWalk;

/// <summary>What one landblock is, and what its tile says (W1): the row every later phase hangs its verdicts on.</summary>
internal static class Census
{
    /// <summary>The id a run files a landblock under.</summary>
    public static string IdOf(LandblockCoord coord) => string.Create(CultureInfo.InvariantCulture, $"{coord.X:X2}{coord.Y:X2}");

    /// <summary>Scans one landblock.</summary>
    /// <returns>The row, and what the scan cost.</returns>
    public static (LandblockRow Row, NavTile? Tile, double CollisionMs, double TileMs) Scan(WalkContext context, LandblockCoord coord)
    {
        var id = IdOf(coord);
        if (context.Dats.GetLandblockTerrain(coord) is null)
        {
            return (new LandblockRow { Id = id, Water = "no-record" }, null, 0, 0);
        }

        var clock = Stopwatch.StartNew();
        var block = context.Store.Get(coord);
        var collisionMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var tile = context.Tile(coord, out var stats);
        var tileMs = clock.Elapsed.TotalMilliseconds;

        var (open, water, slope, statics, building, lane) = (0, 0, 0, 0, 0, 0);
        foreach (var flags in tile.Flags)
        {
            open += (flags & NavFlags.Blocked) == 0 ? 1 : 0;
            water += (flags & NavFlags.Water) != 0 ? 1 : 0;
            slope += (flags & NavFlags.Slope) != 0 ? 1 : 0;
            statics += (flags & NavFlags.Static) != 0 ? 1 : 0;
            building += (flags & NavFlags.Building) != 0 ? 1 : 0;
            lane += (flags & NavFlags.DoorwayLane) != 0 ? 1 : 0;
        }

        var cells = block.EnvCells.Count == 0 ? LandblockCellKind.NoCells : CellDiscovery.Summarize(context.Structures, coord).Kind;
        return (new LandblockRow
        {
            Id = id,
            Water = block.WaterType switch { WaterType.NotWater => "land", WaterType.PartiallyWater => "coast", WaterType.EntirelyWater => "sea", _ => block.WaterType.ToString() },
            Cells = cells.ToString(),
            EnvCells = block.EnvCells.Count,
            Buildings = block.Buildings.Count,
            Statics = block.StaticObjects.Count,
            OpenNodes = open,
            WaterNodes = water,
            SlopeNodes = slope,
            StaticNodes = statics,
            BuildingNodes = building,
            LaneNodes = lane,
            Lanes = stats.LanesBuilt,
        }, tile, collisionMs, tileMs);
    }
}

/// <summary>One landblock's census row. Repeats exactly for the same dats.</summary>
internal sealed record LandblockRow
{
    public required string Id { get; init; }

    public required string Water { get; init; }

    public string Cells { get; init; } = nameof(LandblockCellKind.NoCells);

    public int EnvCells { get; init; }

    public int Buildings { get; init; }

    public int Statics { get; init; }

    public int OpenNodes { get; init; }

    public int WaterNodes { get; init; }

    public int SlopeNodes { get; init; }

    public int StaticNodes { get; init; }

    public int BuildingNodes { get; init; }

    public int LaneNodes { get; init; }

    public int Lanes { get; init; }
}
