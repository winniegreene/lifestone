using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// A landblock's LandBlockInfo (<c>0xXXYYFFFE</c>): static objects, buildings and the restriction table.
/// </summary>
/// <remarks>
/// <para>
/// Layout per retail <c>CLandBlockInfo::UnPack</c> (and ACE <c>DatLoader/FileTypes/LandblockInfo.cs</c>): id,
/// u32 num_cells, u32 num_objects, objects {u32 id, Frame}, u32 packed (low 16 = num_buildings, high 16 = pack_mask),
/// buildings {u32 model, Frame, u32 num_leaves, u32 num_portals, CBldPortal[]}, and when <c>pack_mask &amp; 1</c> a
/// PackableHashTable&lt;u32 cell id, u32 restriction iid&gt;. Every one of the 5,346 records in the end-of-retail cell
/// dat decodes byte-exact (cells tests).
/// </para>
/// <para>
/// A LandBlockInfo exists exactly when the LandBlock's <c>hasObjects</c> flag is set; landblocks without one have no
/// statics, buildings or EnvCells.
/// </para>
/// </remarks>
public sealed class LandblockStatics
{
    private LandblockStatics(
        LandblockCoord coord,
        uint numCells,
        IReadOnlyList<StaticObject> objects,
        IReadOnlyList<BuildingData> buildings,
        IReadOnlyDictionary<uint, uint> restrictionTable)
    {
        Coord = coord;
        NumCells = numCells;
        Objects = objects;
        Buildings = buildings;
        RestrictionTable = restrictionTable;
    }

    public LandblockCoord Coord { get; }

    /// <summary>
    /// The <c>num_cells</c> field as stored. <b>Not a reliable EnvCell count</b>: it disagrees with the EnvCells
    /// actually present in 38 of the 5,346 landblocks (e.g. Holtburg 0xA9B4 stores 123 but has 138; the Town Network
    /// 0x0007 stores 205 but has 498). Retail never uses it to load cells (cells come from portal and visible lists),
    /// so enumerate with <see cref="CellDiscovery.EnumerateEnvCellIds"/>.
    /// </summary>
    public uint NumCells { get; }

    /// <summary>Static objects (GfxObj 0x01 or Setup 0x02) with landblock-local frames.</summary>
    public IReadOnlyList<StaticObject> Objects { get; }

    /// <summary>Buildings (always a GfxObj 0x01 in end-of-retail data) with their portals into EnvCells.</summary>
    public IReadOnlyList<BuildingData> Buildings { get; }

    /// <summary>
    /// Restriction table as stored: full cell id to restriction object iid. Empty when the record has no table.
    /// </summary>
    /// <remarks>
    /// Retail only looks up the block's 64 land cells: <c>CLandBlock::init_static_objs</c> calls
    /// <c>CLandBlockInfo::GetRestrictionIID(landcell id)</c> for each and stores a non-zero result on that land cell
    /// (ACE <c>Landblock.init_static_objs</c> assigns each value to the land cell <c>gid_to_lcoord(key)</c>). The keys of the
    /// end-of-retail dats (1,293 tables) are 3,490 land-cell ids of the same block, which retail uses; 3,000 EnvCell ids
    /// and 650 zero keys, which retail never looks up (an EnvCell carries its own <see cref="EnvCellData.RestrictionObjectId"/>).
    /// </remarks>
    public IReadOnlyDictionary<uint, uint> RestrictionTable { get; }

    /// <summary>Reads the LandBlockInfo of a landblock, or null when the landblock has none.</summary>
    /// <exception cref="InvalidDataException">The record exists but is malformed.</exception>
    public static LandblockStatics? Read(DatSet dats, LandblockCoord coord)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var bytes = CellRecordReader.ReadCellBytes(dats, coord.LandBlockInfoId);
        return bytes is null ? null : Decode(coord, bytes);
    }

    /// <summary>Decodes raw LandBlockInfo bytes.</summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or the wrong id.</exception>
    public static LandblockStatics Decode(LandblockCoord coord, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var lbi = CellRecordReader.UnpackExact<LandBlockInfo>(bytes, coord.LandBlockInfoId);
        var blockMask = CellIds.Make(coord, 0);

        var objects = lbi.Objects.Select(o => new StaticObject(o.Id, Placement.FromDat(o.Frame))).ToArray();
        var buildings = lbi.Buildings.Select(b => new BuildingData(
            b.ModelId,
            Placement.FromDat(b.Frame),
            b.NumLeaves,
            b.Portals.Select(p => BuildingPortalData.FromDat(blockMask, p)).ToArray())).ToArray();

        return new LandblockStatics(
            coord,
            lbi.NumCells,
            objects,
            buildings,
            new Dictionary<uint, uint>(lbi.RestrictionTable));
    }
}

/// <summary>A static object placement: a GfxObj (0x01) or Setup (0x02) id and its landblock-local frame.</summary>
public readonly record struct StaticObject(uint ModelId, Placement Frame)
{
    public bool IsGfxObj => ModelId >> 24 == 0x01;

    public bool IsSetup => ModelId >> 24 == 0x02;
}

/// <summary>
/// A building from a LandBlockInfo (retail <c>BuildInfo</c>): its model, frame, number of leaf cells and its portals.
/// </summary>
/// <param name="ModelId">The building GfxObj (0x01). Its drawing BSP carries the portal polygons; see
/// <see cref="BuildingPortalGeometry"/>.</param>
/// <param name="Frame">Landblock-local frame. Retail <c>CLandBlock::init_buildings</c> places the building at exactly this
/// frame in the outdoor cell found by <c>LandDefs::adjust_to_outside</c>.</param>
/// <param name="NumLeaves">Number of BSP leaf cells (retail <c>CBuildingObj</c> leaf cells used for drawing leaves).</param>
/// <param name="Portals">Doorways into EnvCells, indexed by the <c>portal_index</c> of the building GfxObj's drawing-BSP
/// portal polygons.</param>
public sealed record BuildingData(uint ModelId, Placement Frame, uint NumLeaves, IReadOnlyList<BuildingPortalData> Portals);

/// <summary>
/// A building doorway (retail <c>CBldPortal</c>, unpacked by <c>CBldPortal::UnPack</c>).
/// </summary>
/// <param name="RawFlags">u16 flags as stored: bit 0 ExactMatch, bit 1 (clear = <see cref="PortalSide"/>). End-of-retail
/// data uses only 1 and 3.</param>
/// <param name="ExactMatch">Flag bit 0: the building portal polygon coincides with the cell's portal polygon.</param>
/// <param name="PortalSide"><c>!(flags &amp; 2)</c>. Names the side of the building GfxObj's portal polygon the viewer must
/// be on to look in (retail <c>PView::ConstructView(CBldPortal*, ...)</c>, see <see cref="BuildingDoorway.CanLookIn"/>):
/// the negative side when true, the positive side when false. The interior is on the other side: measured on all
/// 16,937 doorways, positive for the 5,729 with PortalSide (flags 1) and negative for the 11,208 without (flags 3).</param>
/// <param name="OtherCellId">Full id of the entry EnvCell.</param>
/// <param name="OtherPortalIndex">Index into the entry cell's <see cref="EnvCellData.Portals"/> of the matching outside
/// portal (verified: always a portal whose other side is the outside).</param>
/// <param name="StabList">Full ids of the cells visible through this doorway. Retail <c>CLandBlock::init_buildings</c>
/// merges these into the landblock stab list that is loaded with the landblock (<c>CBuildingObj::add_to_stablist</c>),
/// and <c>PView::DrawPortal</c> restricts the indoor view through this doorway to them (<c>add_views</c>).</param>
public sealed record BuildingPortalData(
    ushort RawFlags,
    bool ExactMatch,
    bool PortalSide,
    uint OtherCellId,
    int OtherPortalIndex,
    IReadOnlyList<uint> StabList)
{
    internal static BuildingPortalData FromDat(uint blockMask, DatReaderWriter.Types.BuildingPortal p)
    {
        var flags = (ushort)p.Flags;
        return new BuildingPortalData(
            flags,
            (flags & 1) != 0,
            (flags & 2) == 0,
            blockMask | p.OtherCellId,
            (short)p.OtherPortalId,
            p.StabList.Select(s => blockMask | s).ToArray());
    }
}
