// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/LandblockStruct.cs (CalcWater,
// CalcCellWater, calc_water_depth, the SurfChar table) (AGPL-3.0)
// with changes: an immutable per-landblock collision set instead of ACE's Landblock/LandblockStruct; the water rules
// checked against retail CLandBlockStruct (cited below); the rest (cells, buildings, statics) is assembled by
// LandblockCollisionBuilder from the cells, models and scenery tracks' decoders.

using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>A static object of one landblock whose shadow falls into a cell of another landblock.</summary>
/// <param name="CellId">The other landblock's cell.</param>
/// <param name="Object">The static object.</param>
/// <param name="FromCellBlock">True when the cell was found by the parts' box search (retail <c>CLandCell::add_cell_block</c>,
/// which ACE keeps to one landblock: decision table row 6); false when a cylinder-sphere reached it
/// (<c>add_all_outside_cells</c>, which crosses edges in ACE too).</param>
public readonly record struct ForeignShadow(uint CellId, PhysStaticObject Object, bool FromCellBlock);

/// <summary>
/// Everything one landblock contributes to collision: its 64 land cells (terrain triangles and water), its EnvCells with
/// their physics BSPs, its buildings, and the static objects (LandBlockInfo statics, scenery, EnvCell statics) registered
/// in the cells they reach. Immutable once built, so it is built on any thread and attached to a
/// <see cref="PhysicsWorld"/> on the main thread.
/// </summary>
/// <remarks>Built by <see cref="LandblockCollisionBuilder"/>; a landblock without a LandBlock record has no cells.</remarks>
public sealed class LandblockCollision
{
    /// <summary>
    /// The terrain types retail counts as water (<c>TERRAIN_SURF_CHAR</c> 1). The table is data the decompile does not
    /// show; this is ACE's (LandblockStruct.cs:62, types 16-20, the five water types), which OpenAC repeats.
    /// </summary>
    private const int FirstWaterType = 16;
    private const int LastWaterType = 20;

    private readonly PhysLandCell[] _landCells;
    private readonly Dictionary<uint, PhysEnvCell> _envCells;
    private readonly PhysBuilding[] _buildings;
    private readonly PhysStaticObject[] _statics;
    private readonly ForeignShadow[] _foreignShadows;
    private readonly ushort[] _terrainWords;

    /// <summary>An empty landblock: no LandBlock record, so no cells.</summary>
    internal LandblockCollision(LandblockCoord coord)
        : this(coord, [], new Dictionary<uint, PhysEnvCell>(), [], [], [], new ushort[TerrainConstants.VertexCount], WaterType.NotWater)
    {
    }

    internal LandblockCollision(
        LandblockCoord coord,
        PhysLandCell[] landCells,
        Dictionary<uint, PhysEnvCell> envCells,
        PhysBuilding[] buildings,
        PhysStaticObject[] statics,
        ForeignShadow[] foreignShadows,
        ushort[] terrainWords,
        WaterType waterType)
    {
        Coord = coord;
        _landCells = landCells;
        _envCells = envCells;
        _buildings = buildings;
        _statics = statics;
        _foreignShadows = foreignShadows;
        _terrainWords = terrainWords;
        WaterType = waterType;
        foreach (var cell in landCells)
        {
            cell.Landblock = this;
        }

        foreach (var cell in envCells.Values)
        {
            cell.Landblock = this;
        }
    }

    /// <summary>The landblock.</summary>
    public LandblockCoord Coord { get; }

    /// <summary>
    /// The landblock's water (retail <c>CLandBlockStruct::water_type</c> from <c>CalcWater</c>): entirely water when every
    /// cell is, partly when some is.
    /// </summary>
    public WaterType WaterType { get; }

    /// <summary>The 64 land cells, index <c>x * 8 + y</c> (empty for a landblock without a LandBlock record).</summary>
    public IReadOnlyList<PhysLandCell> LandCells => _landCells;

    /// <summary>The EnvCells, by full id.</summary>
    public IReadOnlyDictionary<uint, PhysEnvCell> EnvCells => _envCells;

    /// <summary>The buildings that were placed (a building outside its landblock is not).</summary>
    public IReadOnlyList<PhysBuilding> Buildings => _buildings;

    /// <summary>The static objects with a collision shape, in creation order: LandBlockInfo statics, scenery, EnvCell statics.</summary>
    public IReadOnlyList<PhysStaticObject> StaticObjects => _statics;

    /// <summary>The shadows this landblock's static objects cast into other landblocks' cells.</summary>
    public IReadOnlyList<ForeignShadow> ForeignShadows => _foreignShadows;

    /// <summary>
    /// The cell with this full id: a land cell (retail <c>CLandBlock::get_landcell</c>, 11.4186 0x0052FD20, 11.6096
    /// 0x00530A60) or an EnvCell; null for another landblock's id or a cell that does not exist.
    /// </summary>
    public PhysCell? GetCell(uint cellId)
    {
        if (LandblockCoord.FromId(cellId) != Coord)
        {
            return null;
        }

        var low = cellId & 0xFFFF;
        if (low is >= 1 and <= 0x40)
        {
            return _landCells.Length == 0 ? null : _landCells[low - 1];
        }

        return low >= 0x100 && _envCells.TryGetValue(cellId, out var cell) ? cell : null;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"LandblockCollision {Coord} ({_landCells.Length} land cells, {_envCells.Count} EnvCells, {_buildings.Length} buildings, {_statics.Length} statics)";

    /// <summary>
    /// Retail <c>CLandBlockStruct::calc_water_depth</c> (11.4186 0x005315F0, 11.6096 0x00532330): in a partly flooded cell,
    /// 0.45 m at a water corner and 0.1 m elsewhere, the corner being the one nearest <paramref name="point"/>. The
    /// decompile keeps only the flags of the two comparisons that pick the corner; ACE's reading (the far corner from
    /// half a cell on) is kept.
    /// </summary>
    internal float CalcWaterDepth(uint cellId, Vector3 point)
    {
        if (!LandDefs.IsOutdoorCell(cellId))
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), $"TODO(physics): calc_water_depth of 0x{cellId:X8}, which is not a land cell (retail reads past its arrays).");
        }

        var index = (int)(cellId & 0xFFFF) - 1;
        var x = index >> 3;
        var y = index & 7;
        if (point.X % (float)LandDefs.CellLength >= 12.0f)
        {
            x++;
        }

        if (point.Y % (float)LandDefs.CellLength >= 12.0f)
        {
            y++;
        }

        return IsWater(_terrainWords[TerrainConstants.VertexIndex(x, y)]) ? 0.44999999f : 0.1f;
    }

    /// <summary>
    /// Retail <c>CLandBlockStruct::CalcCellWater</c> (11.4186 0x00531550, 11.6096 0x00532290) and <c>CalcWater</c>
    /// (0x005327E0, 0x00533520): each cell's water from its four corners, and the landblock's from its cells.
    /// </summary>
    internal static (WaterType[] Cells, WaterType Block) CalcWater(IReadOnlyList<ushort> terrainWords)
    {
        var cells = new WaterType[TerrainConstants.CellCount];
        var blockHasWater = false;
        var blockAllWater = true;
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var hasWater = false;
                var allWater = true;
                for (var vx = cx; vx <= cx + 1; vx++)
                {
                    for (var vy = cy; vy <= cy + 1; vy++)
                    {
                        if (IsWater(terrainWords[TerrainConstants.VertexIndex(vx, vy)]))
                        {
                            hasWater = true;
                        }
                        else
                        {
                            allWater = false;
                        }
                    }
                }

                WaterType water;
                if (hasWater)
                {
                    blockHasWater = true;
                    water = allWater ? WaterType.EntirelyWater : WaterType.PartiallyWater;
                    if (!allWater)
                    {
                        blockAllWater = false;
                    }
                }
                else
                {
                    blockAllWater = false;
                    water = WaterType.NotWater;
                }

                cells[(cx * TerrainConstants.CellsPerSide) + cy] = water;
            }
        }

        var block = !blockHasWater ? WaterType.NotWater : blockAllWater ? WaterType.EntirelyWater : WaterType.PartiallyWater;
        return (cells, block);
    }

    /// <summary>True when the terrain word's type (bits 2-6 of its low byte) is water.</summary>
    internal static bool IsWater(ushort terrainWord) => ((terrainWord & 0xFF) >> 2 & 0x1F) is >= FirstWaterType and <= LastWaterType;
}
