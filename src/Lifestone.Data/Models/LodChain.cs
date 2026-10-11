using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Models;

/// <summary>
/// The GfxObjs a part draws at each LOD level, resolved the way retail <c>CPhysicsPart::LoadGfxObjArray</c> does.
/// </summary>
/// <remarks>
/// <para>
/// A part references one GfxObj (the Setup part id, or a static's id). If that GfxObj has a <c>DIDDegrade</c> whose
/// GfxObjDegradeInfo exists, the part's drawable array is <b>the degrade table's GfxObjs</b>, level by level, and the
/// referenced GfxObj itself is only used to find the table: in 229 of 4,131 end-of-retail tables
/// <c>Levels[0]</c> is a different (usually more detailed) GfxObj, e.g. the human head 0x0100004E draws 0x01001787
/// up close. Otherwise the array is just the referenced GfxObj.
/// </para>
/// <para>
/// Level GfxObj id 0 draws nothing. Every end-of-retail table ends with such a level (at FLT_MAX), so objects stop
/// being drawn beyond their last real level. 11 tables (e.g. 0x1100002E of the anchor GfxObj 0x010001EC) are just
/// a level 0 with all distances 0 plus the empty level, so <see cref="DegradeSelector.SelectLevel"/> never picks
/// level 0 at any bias: such parts are never drawn, except on the player's own character, whose parts retail
/// always draws at level 0 (<c>CPhysicsPart::UpdateViewerDistance</c>, <c>player_iid</c> check).
/// </para>
/// <para>
/// Retail refuses the part (and with it the whole object, <c>CPartArray::InitParts</c>) when the referenced GfxObj
/// is missing or when <c>Levels[0]</c>'s GfxObj is missing or 0; <see cref="IsLoadable"/> reports that.
/// </para>
/// <para>
/// <b>Every level is drawn with the level-0 surface array.</b> <c>CPhysicsPart::SetGfxObjArray</c> sets the part's
/// <c>surfaces</c> to <c>new_gfxobj[0]-&gt;m_rgSurfaces</c> (later edited by ObjDesc texture and palette changes), and
/// <c>CPhysicsPart::Draw</c> hands that array to <c>Render::SetSurfaceArray</c> before
/// <c>D3DPolyRender::DrawMesh(CGfxObj*)</c> draws the selected level's subsets against it. A level's own surface list
/// is never used for drawing, so a level's sub-meshes must be resolved by surface index
/// (<see cref="SurfaceIndexRange.SurfaceIndex"/>) in <see cref="NearestGfxObjId"/>'s list. On end-of-retail data every
/// non-empty level above 0 lists exactly the same surfaces as level 0 (the exhaustive degrade sweep asserts it), so
/// <see cref="Geometry.SubMesh.SurfaceId"/> of any level is already what retail draws.
/// </para>
/// </remarks>
public sealed class LodChain
{
    private LodChain(uint rootGfxObjId, DegradeTable? table, IReadOnlyList<uint> gfxObjIds, bool isLoadable)
    {
        RootGfxObjId = rootGfxObjId;
        Table = table;
        GfxObjIds = gfxObjIds;
        IsLoadable = isLoadable;
    }

    /// <summary>The GfxObj the part references.</summary>
    public uint RootGfxObjId { get; }

    /// <summary>The degrade table, or null when the root has none (single level, <see cref="DegradeMode.Normal"/>).</summary>
    public DegradeTable? Table { get; }

    /// <summary>GfxObj per level (0 = draw nothing). One entry (the root) when there is no table.</summary>
    public IReadOnlyList<uint> GfxObjIds { get; }

    /// <summary>The level-0 GfxObj: what retail draws up close, and whose sort centre feeds the LOD distance.</summary>
    public uint NearestGfxObjId => GfxObjIds[0];

    /// <summary>False when retail's <c>CPhysicsPart::SetPart</c> would fail for this part.</summary>
    public bool IsLoadable { get; }

    /// <summary>Retail <c>CPhysicsPart::GetMaxDegradeDistance</c>.</summary>
    public float MaxDegradeDistance => Table?.MaxDegradeDistance ?? DegradeSelector.DefaultMaxDegradeDistance;

    /// <summary>
    /// Level drawn at <paramref name="lodDistance"/> (see <see cref="DegradeSelector.SelectLevel"/>): its index, GfxObj id
    /// (0 = nothing) and orientation mode.
    /// </summary>
    public (int Level, uint GfxObjId, DegradeMode Mode) Select(
        float lodDistance,
        float bias = DegradeSelector.SteadyStateBias,
        float degradeDistance = DegradeSelector.DefaultDegradeDistance)
    {
        if (Table is null)
        {
            return (0, GfxObjIds[0], DegradeMode.Normal);
        }

        var level = DegradeSelector.SelectLevel(Table, lodDistance, bias, degradeDistance);

        // CPhysicsPart::Draw: a level index outside the array falls back to 0 (cannot happen with a table).
        return (level, GfxObjIds[level], Table.Levels[level].Mode);
    }

    /// <summary>Builds the chain from already-decoded records (no dat access).</summary>
    /// <param name="rootGfxObjId">Referenced GfxObj id.</param>
    /// <param name="root">The referenced GfxObj, or null when it is missing from the dat.</param>
    /// <param name="table">Its degrade table, or null when it has none or the record is missing.</param>
    /// <param name="gfxObjExists">Whether a GfxObj id exists in the dat (used for the level-0 check).</param>
    public static LodChain Create(uint rootGfxObjId, GfxObj? root, DegradeTable? table, Func<uint, bool> gfxObjExists)
    {
        ArgumentNullException.ThrowIfNull(gfxObjExists);
        if (root is null)
        {
            return new LodChain(rootGfxObjId, null, [rootGfxObjId], isLoadable: false);
        }

        if (table is null)
        {
            return new LodChain(rootGfxObjId, null, [rootGfxObjId], isLoadable: true);
        }

        var ids = table.Levels.Select(l => l.GfxObjId).ToArray();
        var loadable = ids[0] != 0 && gfxObjExists(ids[0]);
        return new LodChain(rootGfxObjId, table, ids, loadable);
    }

    /// <summary>Resolves the chain of <paramref name="gfxObjId"/> from the dats.</summary>
    public static LodChain Load(DatSet dats, uint gfxObjId)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (gfxObjId == 0 || !dats.TryGetPortal<GfxObj>(gfxObjId, out var root))
        {
            return Create(gfxObjId, null, null, _ => false);
        }

        DegradeTable? table = null;
        if (root.Flags.HasFlag(DatReaderWriter.Enums.GfxObjFlags.HasDIDDegrade) && root.DIDDegrade != 0)
        {
            table = DegradeTable.Load(dats, root.DIDDegrade);
        }

        return Create(gfxObjId, root, table, id => GfxObjExists(dats, id));
    }

    private static bool GfxObjExists(DatSet dats, uint id) =>
        dats.Read(d => d.Portal.Tree.TryGetFile(id, out _));
}
