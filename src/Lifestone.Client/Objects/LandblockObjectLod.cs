using System.Numerics;
using Lifestone.Data.Models;

namespace Lifestone.Client.Objects;

/// <summary>Retail LOD parameters (<c>Render::deg_mul</c>, <c>Render::s_rDegradeDistance</c>).</summary>
/// <param name="Bias">Degrade bias; 1 is retail's steady state above 25 fps (<see cref="DegradeSelector.SteadyStateBias"/>).</param>
/// <param name="DegradeDistance">The "Degrade Distance" preference, default 50 m.</param>
public sealed record ObjectLodSettings(float Bias = DegradeSelector.SteadyStateBias, float DegradeDistance = DegradeSelector.DefaultDegradeDistance)
{
    public static readonly ObjectLodSettings Retail = new();

    /// <summary>Horizontal distance beyond which an object's parts all use the object distance (50 m at bias 1).</summary>
    public float ObjectDistance => DegradeSelector.ObjectDegradeDistance(Bias);
}

/// <summary>
/// Retail per-frame LOD selection for one landblock's objects, feeding <see cref="InstanceBatches"/> (one batch per
/// mesh of <see cref="LandblockObjectSet.Meshes"/>).
/// </summary>
/// <remarks>
/// <para>Per land cell that holds objects, every frame (<c>RenderDeviceD3D::UpdateObjCell</c>, acclient.c:458101, called
/// from the landblock draw loop before the cell is drawn):</para>
/// <list type="bullet">
/// <item>d = horizontal distance from the viewer to the cell centre (<c>CLandCell::GetViewerDistance</c>: the offset to
/// <c>CLandCell::pos</c> with z zeroed).</item>
/// <item><b>d &gt; 50 m</b> (a literal, whatever the bias): every object in the cell gets CYpt = d and the horizontal heading
/// to the cell centre (<c>CPhysicsObj::UpdateViewerDistance(CYpt, heading)</c> → <c>CPartArray::UpdateViewerDistance</c>).
/// Far objects therefore switch level cell by cell.</item>
/// <item><b>d ≤ 50 m</b>: <c>CPhysicsObj::UpdateViewerDistance()</c> (acclient.c:317913): offset = object origin − viewer;
/// if its horizontal length² ≥ <c>Render::object_distance_2dsq</c> ((25 + 25·bias)², 50 m at bias 1) every part gets the
/// object's 3-D distance and heading; otherwise each part measures its own, to its scaled level-0 sort centre placed by
/// the part frame (<c>CPhysicsPart::UpdateViewerDistance()</c>, acclient.c:315098).</item>
/// </list>
/// <para>Per part: with a degrade table, level = <c>GfxObjDegradeInfo::get_degrade(CYpt / gfxobj_scale.z)</c>
/// (<see cref="DegradeSelector.SelectLevel"/>: bias and the 50 m degrade distance); without one, level 0 and mode 1. A level
/// whose GfxObj is 0 draws nothing; every end-of-retail table ends with one, so objects vanish past their last level +
/// 50 m. The heading (unit vector viewer → part, (0, 0, 1) within 0.0002 m) feeds the camera-facing modes
/// (<see cref="DegradeFrames"/>). The player's own parts would always use level 0; there is no player in M1.</para>
/// <para><b>EnvCell statics</b> (<see cref="WorldObjectKind.CellStatic"/>) take the same object and part rules: retail calls
/// <c>UpdateObjCell</c> for each drawn EnvCell too (<c>PView::DrawCells</c> → <c>DrawObjCellForDummies</c>), but an EnvCell
/// does not override <c>CObjCell::GetViewerDistance</c>, which returns 0, so the far-cell rule never applies.</para>
/// <para><b>Moving parts.</b> Objects with a default animation give their parts' current frames with
/// <see cref="SetPartFrame"/>; the per-part rule then measures to the moved sort centre (retail <c>CPhysicsPart::UpdateViewerDistance</c>
/// reads the part's current <c>pos</c>).</para>
/// <para><b>TODO(objects): objects in several cells.</b> Retail adds an object to every cell its cylinder-spheres or
/// bounding box touch (<c>CPhysicsObj::calc_cross_cells_static</c>), and <c>UpdateObjCell</c> runs for each of them in
/// the landblock's draw order, so the last such cell sets the distance. Lifestone uses the cell holding the object's origin
/// (always one of them); a large building can switch level up to about a cell (24 m) away from where retail switches it.</para>
/// </remarks>
public sealed class LandblockObjectLod
{
    /// <summary>The viewer must move at least this far (metres) before a landblock is re-evaluated.</summary>
    public const float MoveThreshold = 0.001f;

    /// <summary>
    /// <c>RenderDeviceD3D::UpdateObjCell</c>: beyond this horizontal distance to a land cell's centre its objects take the
    /// cell's distance (a literal 50.0, independent of the degrade bias).
    /// </summary>
    public const float CellDistance = 50f;

    private readonly LandblockObjectSet _set;
    private readonly ObjectLodSettings _settings;
    private readonly int[] _level;
    private readonly DegradeMode[] _mode;
    private readonly Vector3[] _heading;
    private Vector3 _lastViewer;
    private bool _evaluated;

    // Parts that move (default animations, docs/objects.md §1.7): their current frame replaces the stored placement.
    private bool[]? _dynamic;
    private Vector3[]? _dynamicOrigin;
    private Quaternion[]? _dynamicRotation;
    private Vector3[]? _dynamicSortCenter;
    private bool _framesChanged;

    public LandblockObjectLod(LandblockObjectSet set, ObjectLodSettings? settings = null)
    {
        _set = set ?? throw new ArgumentNullException(nameof(set));
        _settings = settings ?? ObjectLodSettings.Retail;
        var count = set.Parts.Count;
        _level = new int[count];
        _mode = new DegradeMode[count];
        _heading = new Vector3[count];
        Array.Fill(_level, -1);
        Array.Fill(_mode, DegradeMode.Normal);
        Batches = new InstanceBatches(set.Meshes.Count, count);
    }

    public LandblockObjectSet Set => _set;

    /// <summary>Current batch membership (mesh per part).</summary>
    public InstanceBatches Batches { get; }

    /// <summary>Parts drawn right now (in some batch).</summary>
    public int DrawnParts { get; private set; }

    /// <summary>Parts currently drawn camera-facing (mode ≠ 1).</summary>
    public int FacingParts { get; private set; }

    /// <summary>The current LOD level of a part (-1 before the first evaluation).</summary>
    public int LevelOf(int part) => _level[part];

    /// <summary>The current degrade mode of a part.</summary>
    public DegradeMode ModeOf(int part) => _mode[part];

    /// <summary>The rotation a part is drawn with now (its own, or the camera-facing one of its level's mode).</summary>
    public Quaternion DrawRotation(int part)
    {
        var rotation = _dynamic is { } dynamic && dynamic[part] ? _dynamicRotation![part] : _set.PartArray[part].Rotation;
        return _mode[part] == DegradeMode.Normal ? rotation : DegradeFrames.DrawRotation(_mode[part], rotation, _heading[part]);
    }

    /// <summary>The part's frame origin now: its stored placement, or its current animated frame.</summary>
    public Vector3 PartOrigin(int part) => _dynamic is { } dynamic && dynamic[part] ? _dynamicOrigin![part] : _set.PartArray[part].Origin;

    /// <summary>True when the part's frame comes from <see cref="SetPartFrame"/> (it moves) instead of its stored placement.</summary>
    public bool IsDynamic(int part) => _dynamic is { } dynamic && dynamic[part];

    /// <summary>
    /// The part's current frame (landblock-local AC), for objects that animate (retail <c>CPhysicsObj::animate_static_object</c>
    /// → <c>UpdatePartsInternal</c>): the LOD then measures to its moved sort centre, the batch holding it is rewritten,
    /// and the next <see cref="Evaluate"/> runs even if the viewer stood still.
    /// </summary>
    public void SetPartFrame(int part, Vector3 origin, Quaternion rotation)
    {
        if (_dynamic is null)
        {
            var count = _set.Parts.Count;
            _dynamic = new bool[count];
            _dynamicOrigin = new Vector3[count];
            _dynamicRotation = new Quaternion[count];
            _dynamicSortCenter = new Vector3[count];
        }

        ref readonly var placed = ref _set.PartArray[part];
        _dynamic[part] = true;
        _dynamicOrigin![part] = origin;
        _dynamicRotation![part] = rotation;
        _dynamicSortCenter![part] = origin + Vector3.Transform(placed.LocalSortCenter * placed.Scale, rotation);
        _framesChanged = true;
        if (Batches.BatchOf(part) is var batch and >= 0)
        {
            Batches.MarkDirty(batch);
        }
    }

    /// <summary>
    /// Selects every part's level for a viewer at <paramref name="viewer"/> (landblock-local AC coordinates) and moves
    /// parts between batches. Batches holding camera-facing parts are marked dirty whenever the viewer moved. Returns
    /// false when the viewer has not moved since the last evaluation and no part moved (nothing to do) unless
    /// <paramref name="force"/>.
    /// </summary>
    public bool Evaluate(Vector3 viewer, bool force = false)
    {
        if (_evaluated && !force && !_framesChanged && Vector3.DistanceSquared(viewer, _lastViewer) < MoveThreshold * MoveThreshold)
        {
            return false;
        }

        _evaluated = true;
        _framesChanged = false;
        _lastViewer = viewer;
        var drawn = 0;
        var facing = 0;
        for (var o = 0; o < _set.ObjectArray.Length; o++)
        {
            EvaluateObject(o, viewer, ref drawn, ref facing);
        }

        DrawnParts = drawn;
        FacingParts = facing;
        return true;
    }

    /// <summary>
    /// Selects the levels of some objects only (EnvCell statics: retail updates the viewer distance only for the cells it
    /// draws, <c>PView::DrawCells</c> → <c>DrawObjCellForDummies</c> → <c>UpdateObjCell</c>). Returns the parts drawn
    /// and drawn camera-facing among them.
    /// </summary>
    public (int Drawn, int Facing) EvaluateObjects(Vector3 viewer, ReadOnlySpan<int> objects)
    {
        var drawn = 0;
        var facing = 0;
        foreach (var o in objects)
        {
            EvaluateObject(o, viewer, ref drawn, ref facing);
        }

        _framesChanged = false;
        return (drawn, facing);
    }

    private void EvaluateObject(int objectIndex, Vector3 viewer, ref int drawn, ref int facing)
    {
        ref readonly var obj = ref _set.ObjectArray[objectIndex];
        var parts = _set.PartArray;
        var objectDistance = _settings.ObjectDistance;
        var useCell = false;
        var useObject = false;
        var objectCypt = 0f;
        var objectHeading = Vector3.UnitZ;
        if (!obj.InEnvCell)
        {
            // RenderDeviceD3D::UpdateObjCell: the land cell's horizontal distance decides first. (An EnvCell keeps
            // CObjCell::GetViewerDistance, which returns 0, so its objects always go on to the object rule.)
            var toCell = new Vector3(obj.CellCenter.X - viewer.X, obj.CellCenter.Y - viewer.Y, 0f);
            var cellDistance = toCell.Length();
            useCell = cellDistance > CellDistance;
            if (useCell)
            {
                objectCypt = cellDistance;
                objectHeading = toCell / cellDistance;
            }
        }

        if (!useCell)
        {
            var offset = obj.Origin - viewer;
            useObject = (offset.X * offset.X) + (offset.Y * offset.Y) >= objectDistance * objectDistance;
            if (useObject)
            {
                (objectCypt, objectHeading) = DistanceAndHeading(offset);
            }
        }

        var dynamic = _dynamic;
        for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
        {
            ref readonly var part = ref parts[p];
            var sortCenter = dynamic is not null && dynamic[p] ? _dynamicSortCenter![p] : part.SortCenter;
            var (cypt, heading) = useCell || useObject ? (objectCypt, objectHeading) : DistanceAndHeading(sortCenter - viewer);
            int level;
            DegradeMode mode;
            if (part.Chain.Table is { } table)
            {
                level = DegradeSelector.SelectLevel(table, cypt / part.Scale.Z, _settings.Bias, _settings.DegradeDistance);
                mode = table.Levels[level].Mode;
            }
            else
            {
                level = 0;
                mode = DegradeMode.Normal;
            }

            var mesh = _set.LevelMesh(p, level);
            _level[p] = level;
            _mode[p] = mode;
            _heading[p] = heading;
            Batches.Move(p, mesh);
            if (mesh >= 0)
            {
                drawn++;
                if (mode != DegradeMode.Normal)
                {
                    facing++;
                    Batches.MarkDirty(mesh);
                }
            }
        }
    }

    /// <summary>Retail's CYpt and viewer heading for an offset (viewer → point).</summary>
    private static (float Distance, Vector3 Heading) DistanceAndHeading(Vector3 offset)
    {
        var distance = offset.Length();
        return distance <= DegradeFrames.SmallLength ? (distance, Vector3.UnitZ) : (distance, offset / distance);
    }
}
