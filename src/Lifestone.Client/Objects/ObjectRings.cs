using Lifestone.Data.Terrain;

namespace Lifestone.Client.Objects;

/// <summary>
/// Which landblocks have landscape objects (statics, buildings, scenery) at all: retail creates them only for landblocks
/// drawn at full terrain detail, which are the 3×3 around the viewer's landblock.
/// </summary>
/// <remarks>
/// <para>Retail chain (acclient 11.4186 decompile; <c>LScape::get_block_orient</c> is identical in 11.6096, 0x5059F0):</para>
/// <list type="number">
/// <item><c>LScape::update_block</c> lays out the <c>mid_width × mid_width</c> landblock grid centred on the load point
/// (<c>CellManager::UpdateLoadPoint</c>: the player's outdoor cell) and, for each block,
/// <c>LScape::get_block_orient</c> gives a polygon size from the block's Chebyshev ring around the centre: 1 for rings
/// 0–1, 2 for ring 2, 4 for rings 3–4, 8 beyond.</item>
/// <item><c>CLandBlockStruct::generate(block, poly_size)</c> sets <c>side_cell_count = 8 / poly_size</c>: only rings 0–1
/// keep all 8×8 cells.</item>
/// <item><c>LScape::grab_visible_cells</c> calls <c>CLandBlock::init_buildings</c> and <c>CLandBlock::init_static_objs</c>
/// (which also runs <c>get_land_scenes</c>, the scenery) for every block, and both return at once unless
/// <c>side_cell_count == 8</c>.</item>
/// <item>A block that leaves ring 1 is regenerated at a lower detail, and <c>CLandBlock::notify_change_size</c> destroys
/// its static objects and buildings (<c>destroy_static_objects</c>, <c>destroy_buildings</c>).</item>
/// </list>
/// <para>So in retail, objects pop in and out at landblock boundaries, between 192 m and 384 m away along an axis; each
/// object's own LOD table then hides it beyond its last level (trees about 250 m, Holtburg's buildings 440 m).
/// <see cref="RetailRadius"/> is that rule; a larger radius is a non-retail option.</para>
/// </remarks>
public static class ObjectRings
{
    /// <summary>Retail: landblocks within one ring of the viewer's landblock (Chebyshev distance ≤ 1) have objects.</summary>
    public const int RetailRadius = 1;

    /// <summary><c>LScape::get_block_orient</c>'s polygon size for a block at Chebyshev ring <paramref name="ring"/>.</summary>
    public static int RetailPolygonSize(int ring) => ring switch
    {
        <= 1 => 1,
        2 => 2,
        <= 4 => 4,
        _ => 8,
    };

    /// <summary><c>CLandBlockStruct::side_cell_count</c> for a block at ring <paramref name="ring"/>: <c>8 / poly_size</c>.</summary>
    public static int RetailSideCellCount(int ring) => 8 / RetailPolygonSize(ring);

    /// <summary>The Chebyshev ring of <paramref name="coord"/> around <paramref name="center"/>.</summary>
    public static int Ring(LandblockCoord coord, LandblockCoord center) =>
        Math.Max(Math.Abs(coord.X - center.X), Math.Abs(coord.Y - center.Y));

    /// <summary>True when retail would create objects for <paramref name="coord"/> with the viewer in <paramref name="center"/>.</summary>
    public static bool HasObjects(LandblockCoord coord, LandblockCoord center, int radius = RetailRadius) =>
        Ring(coord, center) <= radius;

    /// <summary>
    /// The landblock the object grid is centred on for a streaming focus, or null when no landscape object exists: in an
    /// EnvCell that is not seen_outside (a dungeon cell, <see cref="Streaming.StreamingFocusKind.Indoor"/>) retail's
    /// <c>CellManager::ChangePosition</c> calls <c>LScape::release_all</c>, and <c>CellManager::UpdateLoadPoint</c> only
    /// moves the grid from outdoor cells; an outdoor focus centres it on its landblock.
    /// </summary>
    public static LandblockCoord? CenterFor(Streaming.StreamingFocus focus) =>
        focus.Kind == Streaming.StreamingFocusKind.Indoor ? null : World.AcCoordinates.ClampedLandblockAt(focus.Position);
}
