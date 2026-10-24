using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.World.Cells;

/// <summary>EnvCell flag bits as stored (u32 at offset 4).</summary>
/// <remarks>
/// Retail <c>CEnvCell::UnPack</c> reads bit 0 (seen_outside), bit 1 (static objects follow) and bit 3 (restriction
/// object follows); <c>CEnvCell::Pack</c> writes only those three. Bit 2 (the community name is "HasWeenieObjs") is
/// neither read nor written by the client and is never set in end-of-retail data (measured over all 734,976 EnvCells:
/// only the values 0x0-0x3 and 0x8-0xB occur). ACE <c>ACE.Entity/Enum/EnvCellFlags.cs</c> and DatReaderWriter agree on
/// 1, 2 and 8.
/// </remarks>
[Flags]
public enum EnvCellFlags : uint
{
    None = 0,

    /// <summary>The landscape can be seen from this cell (or this cell must keep it loaded); see <see cref="EnvCellData.SeenOutside"/>.</summary>
    SeenOutside = 0x1,

    /// <summary>A static-object list follows the visible-cell list.</summary>
    HasStaticObjs = 0x2,

    /// <summary>Never set in end-of-retail data and ignored by the retail client.</summary>
    HasWeenieObjs = 0x4,

    /// <summary>A u32 restriction object iid follows.</summary>
    HasRestrictionObj = 0x8,
}

/// <summary>
/// An EnvCell (<c>0xXXYY0100+</c> in client_cell_1.dat): one room of a building interior or dungeon.
/// </summary>
/// <remarks>
/// <para>
/// Layout per retail <c>CEnvCell::UnPack</c>: id; u32 flags; u32 id again; u8 num_surfaces; u8 num_portals;
/// u16 num_visible; u16 surfaces[] (each ORed with 0x08000000 and resolved as a Surface); u16 environment (ORed with
/// 0x0D000000); u16 cell-struct index (<c>CEnvironment::get_cellstruct</c> indexes the environment's array); Frame;
/// CCellPortal[] (<c>CCellPortal::UnPack</c>); u16 visible[] (ORed with the block mask); if flags &amp; 2: u32 count +
/// {u32 id, Frame}[]; if flags &amp; 8: u32 restriction object. All 734,976 end-of-retail EnvCells decode byte-exact.
/// </para>
/// </remarks>
public sealed class EnvCellData
{
    private EnvCellData(
        uint id,
        EnvCellFlags flags,
        IReadOnlyList<uint> surfaceIds,
        uint environmentId,
        ushort cellStructureIndex,
        Placement frame,
        IReadOnlyList<CellPortalData> portals,
        IReadOnlyList<uint> visibleCellIds,
        IReadOnlyList<StaticObject> staticObjects,
        uint restrictionObjectId)
    {
        Id = id;
        Flags = flags;
        SurfaceIds = surfaceIds;
        EnvironmentId = environmentId;
        CellStructureIndex = cellStructureIndex;
        Frame = frame;
        Portals = portals;
        VisibleCellIds = visibleCellIds;
        StaticObjects = staticObjects;
        RestrictionObjectId = restrictionObjectId;
    }

    /// <summary>Full cell id (0xXXYYCCCC).</summary>
    public uint Id { get; }

    public EnvCellFlags Flags { get; }

    /// <summary>
    /// The landscape is visible from this cell. Retail uses it to keep and draw the landscape while the viewer is
    /// indoors (<c>SmartBox::RenderNormalMode</c> updates the LScape viewpoint only when the viewer cell is outdoors or
    /// seen_outside; <c>CEnvCell::grab_visible_cells</c> calls <c>LScape::grab_visible_cells</c>), and to light the cell
    /// with sunlight (<c>CellManager</c> picks <c>LScape::sunlight</c> for seen_outside cells). Every cell with an outside
    /// portal has it; 46,620 cells have it without one (rooms that see out through another room).
    /// </summary>
    public bool SeenOutside => (Flags & EnvCellFlags.SeenOutside) != 0;

    /// <summary>Resolved Surface (0x08) ids; polygon <c>PosSurface</c>/<c>NegSurface</c> index this list.</summary>
    public IReadOnlyList<uint> SurfaceIds { get; }

    /// <summary>Environment (0x0D) id holding the room shape.</summary>
    public uint EnvironmentId { get; }

    /// <summary>Index of the CellStruct inside the Environment (array position; equals the stored key in all 772 records).</summary>
    public ushort CellStructureIndex { get; }

    /// <summary>Landblock-local frame of the cell geometry.</summary>
    public Placement Frame { get; }

    /// <summary>Portals in stored order; <see cref="CellPortalData.OtherPortalIndex"/> of a neighbour indexes this list.</summary>
    public IReadOnlyList<CellPortalData> Portals { get; }

    /// <summary>
    /// Precomputed potentially-visible set: full ids of the other cells (same landblock, never this cell) that can be
    /// seen from here. Retail loads them with the cell (<c>CEnvCell::grab_visible_cells</c>) and only these cells can be
    /// reached by the portal walk (<c>PView::DrawInside</c> pushes views for them via <c>PView::add_views</c>).
    /// </summary>
    public IReadOnlyList<uint> VisibleCellIds { get; }

    /// <summary>Static objects inside the cell, with <b>landblock-local</b> frames (not cell-local).</summary>
    public IReadOnlyList<StaticObject> StaticObjects { get; }

    /// <summary>Restriction object iid (flag 8), or 0.</summary>
    public uint RestrictionObjectId { get; }

    /// <summary>Reads an EnvCell, or null when the id does not exist in the cell dat.</summary>
    /// <exception cref="InvalidDataException">The record exists but is malformed.</exception>
    public static EnvCellData? Read(DatSet dats, uint cellId)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (!CellIds.IsEnvCell(cellId))
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), $"0x{cellId:X8} is not an EnvCell id.");
        }

        var bytes = CellRecordReader.ReadCellBytes(dats, cellId);
        return bytes is null ? null : Decode(cellId, bytes);
    }

    /// <summary>Assembles a cell from parts (synthetic unit tests; real data goes through <see cref="Decode"/>).</summary>
    internal static EnvCellData FromParts(
        uint id,
        uint environmentId,
        ushort cellStructureIndex,
        Placement frame,
        IReadOnlyList<CellPortalData> portals,
        EnvCellFlags flags = EnvCellFlags.None) =>
        new(id, flags, [], environmentId, cellStructureIndex, frame, portals, [], [], 0);

    /// <summary>Decodes raw EnvCell bytes.</summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or a wrong id.</exception>
    public static EnvCellData Decode(uint cellId, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var cell = CellRecordReader.UnpackExact<EnvCell>(bytes, cellId);

        // DatReaderWriter discards the second id; retail CEnvCell::UnPack reads it as the cell's own id.
        if (bytes.Length < 12 || BitConverter.ToUInt32(bytes, 8) != cellId)
        {
            throw new InvalidDataException($"EnvCell 0x{cellId:X8}: second id field does not match.");
        }

        var blockMask = CellIds.BlockMask(cellId);
        var flags = (EnvCellFlags)(uint)cell.Flags;
        return new EnvCellData(
            cellId,
            flags,
            cell.Surfaces.Select(s => 0x08000000u | s).ToArray(),
            0x0D000000u | cell.EnvironmentId,
            cell.CellStructure,
            Placement.FromDat(cell.Position),
            cell.CellPortals.Select(p => CellPortalData.FromDat(blockMask, p)).ToArray(),
            cell.VisibleCells.Select(v => blockMask | v).ToArray(),
            cell.StaticObjects.Select(s => new StaticObject(s.Id, Placement.FromDat(s.Frame))).ToArray(),
            (flags & EnvCellFlags.HasRestrictionObj) != 0 ? cell.RestrictionObj : 0u);
    }

    public override string ToString() => $"EnvCell 0x{Id:X8} env 0x{EnvironmentId:X8}[{CellStructureIndex}]";
}

/// <summary>
/// A portal of an EnvCell (retail <c>CCellPortal</c>, unpacked by <c>CCellPortal::UnPack</c>).
/// </summary>
/// <param name="RawFlags">u16 as stored: bit 0 ExactMatch, bit 1 (clear = PortalSide), bit 2 leads outside.
/// End-of-retail values: 0, 1, 2, 3, 5, 7.</param>
/// <param name="ExactMatch">Bit 0: the neighbour's portal polygon coincides with this one, so retail skips the extra
/// clip against it (<c>PView::ClipPortals</c> / <c>OtherPortalClip</c>). Measured: all 1,706,795 reciprocal ExactMatch
/// pairs coincide in landblock space.</param>
/// <param name="PortalSide"><c>!(flags &amp; 2)</c>. When true the cell interior lies on the <b>negative</b> side of the portal
/// polygon's plane, otherwise on the positive side (measured on all 1,867,699 portals with a decisive CellBSP probe).
/// A viewer looks out through the portal only from the interior side (retail <c>PView::InitCell</c>).</param>
/// <param name="PolygonId">Polygon id within the CellStruct (<c>CCellStruct::get_portal</c> matches it against the struct's
/// portal list; ids equal array positions in all end-of-retail Environments).</param>
/// <param name="OtherCellId">Full id of the neighbour, or <see cref="CellIds.Outside"/> when <see cref="LeadsOutside"/>.</param>
/// <param name="OtherPortalIndex">Index of the matching portal in the neighbour's <see cref="EnvCellData.Portals"/>, -1 for
/// outside portals. Not always reciprocal in the data (1,778 of the 1,850,702 inside portals point at a portal that leads
/// elsewhere, or past the end of the list; almost all of them in cells beyond the LandBlockInfo's num_cells).</param>
public readonly record struct CellPortalData(
    ushort RawFlags,
    bool ExactMatch,
    bool PortalSide,
    ushort PolygonId,
    uint OtherCellId,
    int OtherPortalIndex)
{
    /// <summary>Flag bit 2: the portal opens onto the landscape (retail stores other_cell_id = -1).</summary>
    public bool LeadsOutside => (RawFlags & 4) != 0;

    internal static CellPortalData FromDat(uint blockMask, DatReaderWriter.Types.CellPortal p)
    {
        var flags = (ushort)p.Flags;
        var outside = (flags & 4) != 0;
        return new CellPortalData(
            flags,
            (flags & 1) != 0,
            (flags & 2) == 0,
            p.PolygonId,
            outside ? CellIds.Outside : blockMask | p.OtherCellId,
            (short)p.OtherPortalId);
    }
}
