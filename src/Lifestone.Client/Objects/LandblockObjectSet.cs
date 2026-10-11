using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Objects;

/// <summary>Where a landscape object comes from.</summary>
public enum WorldObjectKind
{
    /// <summary>A LandBlockInfo static object (<c>CLandBlock::init_static_objs</c>).</summary>
    Static,

    /// <summary>A LandBlockInfo building (<c>CLandBlock::init_buildings</c>), drawn with the building draw rules.</summary>
    Building,

    /// <summary>Procedural scenery (<c>CLandBlock::get_land_scenes</c>).</summary>
    Scenery,

    /// <summary>
    /// A static object inside an EnvCell (<c>CEnvCell::init_static_objects</c>): building interiors and dungeons. Built by
    /// <see cref="LandblockObjectSet.BuildCellObjects"/>, never by <see cref="LandblockObjectSet.Build"/>.
    /// </summary>
    CellStatic,
}

/// <summary>Which kinds of object a layer creates (diagnostics: <c>--objects=</c>).</summary>
[Flags]
public enum WorldObjectKinds
{
    None = 0,
    Statics = 1,
    Buildings = 2,
    Scenery = 4,
    All = Statics | Buildings | Scenery,
}

/// <summary>A mesh an object part can draw: a GfxObj under the draw rules of its context, with the part's shape baked in.</summary>
/// <param name="GfxObjId">The level's GfxObj.</param>
/// <param name="Context">Object or Building (<see cref="SurfaceDrawRules"/>).</param>
/// <param name="Shape">The non-uniform part scale baked into the mesh (<see cref="PartShapes"/>); (1, 1, 1) for almost every part.</param>
public readonly record struct ObjectMeshKey(uint GfxObjId, MeshDrawContext Context, Vector3 Shape)
{
    /// <summary>A mesh without a baked shape.</summary>
    public ObjectMeshKey(uint gfxObjId, MeshDrawContext context)
        : this(gfxObjId, context, Vector3.One)
    {
    }

    /// <summary>
    /// The shared-mesh cache key (the same form the render contract's examples use, e.g. <c>gfxobj:0x01000C1E:Building</c>;
    /// a baked shape adds <c>:shape(x,y,z)</c>).
    /// </summary>
    public string CacheKey => Shape == Vector3.One
        ? $"gfxobj:0x{GfxObjId:X8}:{Context}"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gfxobj:0x{GfxObjId:X8}:{Context}:shape({Shape.X:R},{Shape.Y:R},{Shape.Z:R})");

    public override string ToString() => CacheKey;
}

/// <summary>One created landscape object: its frame in landblock-local AC coordinates and its parts.</summary>
/// <param name="Kind">Static, building or scenery.</param>
/// <param name="ModelId">Setup or GfxObj id.</param>
/// <param name="Origin">Object origin, landblock-local (AC axes; the frame retail stores in <c>m_position</c>).</param>
/// <param name="Orientation">Object rotation (normalised).</param>
/// <param name="Scale">Uniform object scale (1 for statics and buildings).</param>
/// <param name="FirstPart">Index of the first part in <see cref="LandblockObjectSet.Parts"/>.</param>
/// <param name="PartCount">Number of parts.</param>
/// <param name="CellCenter">
/// Centre of the land cell holding the origin, at height 0 (<c>CLandCell::pos</c>: <c>((2i + 1)·12, (2j + 1)·12, 0)</c>,
/// <c>CLandBlockStruct::ConstructPolygons</c>); far objects take their LOD distance from it
/// (<see cref="LandblockObjectLod"/>). Unused for <see cref="WorldObjectKind.CellStatic"/>.
/// </param>
/// <param name="CellId">The EnvCell holding a <see cref="WorldObjectKind.CellStatic"/> object; 0 for landscape objects.</param>
public readonly record struct WorldObject(
    WorldObjectKind Kind,
    uint ModelId,
    Vector3 Origin,
    Quaternion Orientation,
    float Scale,
    int FirstPart,
    int PartCount,
    Vector3 CellCenter,
    uint CellId = 0)
{
    /// <summary>
    /// The object sits in an EnvCell. EnvCells keep <c>CObjCell::GetViewerDistance</c> (it returns 0), so
    /// <c>RenderDeviceD3D::UpdateObjCell</c> never applies its far-cell rule to them (<see cref="LandblockObjectLod"/>).
    /// </summary>
    public bool InEnvCell => Kind == WorldObjectKind.CellStatic;
}

/// <summary>One part of a created object, placed in landblock-local AC coordinates.</summary>
/// <param name="ObjectIndex">Owner in <see cref="LandblockObjectSet.Objects"/>.</param>
/// <param name="Origin">Part frame origin (landblock-local): <c>obj.origin + R(obj)·(pose.origin × s)</c>.</param>
/// <param name="Rotation">Part frame rotation: <c>obj.q ⊗ pose.q</c>.</param>
/// <param name="Scale">The part's <c>gfxobj_scale</c>: <c>DefaultScale × s</c>.</param>
/// <param name="Shape">
/// The non-uniform part of the scale, baked into the part's meshes (<see cref="PartShapes"/>); (1, 1, 1) when
/// <c>DefaultScale</c> is uniform. The instance transform applies <see cref="InstanceScale"/>.
/// </param>
/// <param name="SortCenter">The scaled level-0 sort centre in landblock-local space (per-part LOD distance point).</param>
/// <param name="Chain">The part's LOD chain.</param>
/// <param name="FirstLevelMesh">
/// Offset of the part's per-level mesh indices in the set's level table (<see cref="LandblockObjectSet.LevelMeshes"/>).
/// </param>
/// <param name="LocalSortCenter">
/// The level-0 GfxObj's sort centre in part space, unscaled (<see cref="ObjectModelPart.SortCenter"/>): the per-part LOD
/// point of a part that moves (default animations, <see cref="LandblockObjectLod.SetPartFrame"/>).
/// </param>
/// <param name="PartIndex">The part's index in its model (<see cref="ObjectModelPart.Index"/>).</param>
/// <remarks>A struct, and the level table is one flat array per set: a landblock holds thousands of parts and its sets
/// live for seconds, so per-part heap objects would lengthen every garbage collection while they survive.</remarks>
public readonly record struct ObjectPartPlacement(
    int ObjectIndex,
    Vector3 Origin,
    Quaternion Rotation,
    Vector3 Scale,
    Vector3 Shape,
    Vector3 SortCenter,
    LodChain Chain,
    int FirstLevelMesh,
    Vector3 LocalSortCenter = default,
    int PartIndex = 0)
{
    /// <summary>Number of LOD levels (entries in the level table).</summary>
    public int LevelCount => Chain.GfxObjIds.Count;

    /// <summary>The scale the instance transform applies: <see cref="Scale"/> = <see cref="InstanceScale"/> × <see cref="Shape"/>.</summary>
    public Vector3 InstanceScale => PartShapes.InstanceScale(Scale, Shape);
}

/// <summary>
/// The landscape objects retail creates for one landblock (statics, buildings and scenery), expanded into placed parts
/// with their LOD chains, plus the distinct meshes those parts can draw. Engine-agnostic, immutable, built on a worker.
/// </summary>
/// <remarks>
/// <para><b>Creation rules</b> (<see cref="Build"/>):</para>
/// <list type="bullet">
/// <item><b>Statics and buildings</b> (<c>CLandBlock::init_static_objs</c>, <c>CLandBlock::init_buildings</c>): the model
/// must load (<c>CPhysicsObj::makeObject</c> / <c>CBuildingObj::makeBuilding</c>), and its stored frame — landblock-local,
/// used unchanged as the object's frame (<c>add_obj_to_cell</c> / <c>set_initial_frame</c>) — must have its origin in this
/// landblock: <c>LandDefs::adjust_to_outside</c> on (landblock id, origin), then <c>CLandBlock::get_landcell</c>, which
/// only returns a cell whose id matches one of this block's 64 cells. Objects that fail are destroyed.</item>
/// <item><b>Scenery</b> comes from the scenery track's <see cref="SceneryGenerator"/> (retail filters, terrain z, scale,
/// orientation); the stored orientation is normalised here (retail accepts |q|² within 0.001 of 1).</item>
/// <item><b>Buildings</b> draw with <see cref="MeshDrawContext.Building"/> (untextured sub-meshes skipped,
/// <c>D3DPolyRender::DrawMesh</c>); statics and scenery with <see cref="MeshDrawContext.Object"/>.</item>
/// </list>
/// <para><b>Which landblocks</b> get objects at all is decided by the caller: retail creates them only for landblocks
/// at full terrain detail, the 3×3 around the viewer (<see cref="ObjectRings"/>).</para>
/// </remarks>
public sealed class LandblockObjectSet
{
    private readonly int[] _kindCounts;
    private readonly int[] _levelMeshes;

    private LandblockObjectSet(
        LandblockCoord coord,
        WorldObject[] objects,
        ObjectPartPlacement[] parts,
        int[] levelMeshes,
        ObjectMeshKey[] meshes,
        IReadOnlyDictionary<string, int> rejected)
    {
        Coord = coord;
        ObjectArray = objects;
        PartArray = parts;
        _levelMeshes = levelMeshes;
        Meshes = meshes;
        Rejected = rejected;
        var capacity = new int[meshes.Length];
        for (var p = 0; p < parts.Length; p++)
        {
            var levels = LevelMeshes(p);
            for (var l = 0; l < levels.Length; l++)
            {
                // Count each mesh once per part (a chain may repeat a GfxObj at several levels).
                if (levels[l] >= 0 && !levels[..l].Contains(levels[l]))
                {
                    capacity[levels[l]]++;
                }
            }
        }

        MeshCapacity = capacity;
        _kindCounts = new int[Enum.GetValues<WorldObjectKind>().Length];
        foreach (var obj in objects)
        {
            _kindCounts[(int)obj.Kind]++;
        }
    }

    public LandblockCoord Coord { get; }

    /// <summary>Created objects: statics first, then buildings, then scenery (retail creation order within each).</summary>
    public IReadOnlyList<WorldObject> Objects => ObjectArray;

    /// <summary>All parts of all objects, grouped by object.</summary>
    public IReadOnlyList<ObjectPartPlacement> Parts => PartArray;

    /// <summary>Distinct meshes any LOD level of any part can draw.</summary>
    public IReadOnlyList<ObjectMeshKey> Meshes { get; }

    /// <summary>Per mesh: how many parts can draw it (an upper bound on its instances at any time).</summary>
    public IReadOnlyList<int> MeshCapacity { get; }

    /// <summary>Objects retail would not create, by reason (diagnostics).</summary>
    public IReadOnlyDictionary<string, int> Rejected { get; }

    internal WorldObject[] ObjectArray { get; }

    internal ObjectPartPlacement[] PartArray { get; }

    /// <summary>Number of created objects of a kind.</summary>
    public int Count(WorldObjectKind kind) => _kindCounts[(int)kind];

    /// <summary>Per LOD level of part <paramref name="part"/>: index into <see cref="Meshes"/>, or -1 when the level draws nothing.</summary>
    public ReadOnlySpan<int> LevelMeshes(int part)
    {
        var p = PartArray[part];
        return _levelMeshes.AsSpan(p.FirstLevelMesh, p.LevelCount);
    }

    /// <summary>The mesh part <paramref name="part"/> draws at <paramref name="level"/>, or -1.</summary>
    public int LevelMesh(int part, int level) => _levelMeshes[PartArray[part].FirstLevelMesh + level];

    /// <summary>
    /// Builds the object set of a landblock from its LandBlockInfo and its generated scenery.
    /// </summary>
    /// <param name="coord">The landblock.</param>
    /// <param name="statics">Its LandBlockInfo, or null when it has none.</param>
    /// <param name="scenery">Its scenery placements (retail creation order), or null for none.</param>
    /// <param name="library">Resolved models and LOD chains.</param>
    /// <param name="kinds">Which kinds to create.</param>
    public static LandblockObjectSet Build(
        LandblockCoord coord,
        LandblockStatics? statics,
        IReadOnlyList<ScenePlacement>? scenery,
        ObjectModelLibrary library,
        WorldObjectKinds kinds = WorldObjectKinds.All)
    {
        ArgumentNullException.ThrowIfNull(library);
        var builder = new Builder(coord, library);

        // Size every array once (models and chains are cached, so this pre-pass is cheap): a landblock holds up to a few
        // thousand parts, and growing lists would allocate several times that on the workers.
        if (statics is not null)
        {
            if (kinds.HasFlag(WorldObjectKinds.Statics))
            {
                foreach (var obj in statics.Objects)
                {
                    builder.Reserve(obj.ModelId);
                }
            }

            if (kinds.HasFlag(WorldObjectKinds.Buildings))
            {
                foreach (var building in statics.Buildings)
                {
                    builder.Reserve(building.ModelId);
                }
            }
        }

        if (scenery is not null && kinds.HasFlag(WorldObjectKinds.Scenery))
        {
            foreach (var placement in scenery)
            {
                builder.Reserve(placement.ObjectId);
            }
        }

        builder.Allocate();
        if (statics is not null)
        {
            if (kinds.HasFlag(WorldObjectKinds.Statics))
            {
                foreach (var obj in statics.Objects)
                {
                    builder.AddLandBlockInfoObject(WorldObjectKind.Static, obj.ModelId, obj.Frame);
                }
            }

            if (kinds.HasFlag(WorldObjectKinds.Buildings))
            {
                foreach (var building in statics.Buildings)
                {
                    builder.AddLandBlockInfoObject(WorldObjectKind.Building, building.ModelId, building.Frame);
                }
            }
        }

        if (scenery is not null && kinds.HasFlag(WorldObjectKinds.Scenery))
        {
            foreach (var placement in scenery)
            {
                builder.Add(WorldObjectKind.Scenery, placement.ObjectId, placement.Origin, placement.Orientation, placement.Scale);
            }
        }

        return builder.Finish();
    }

    /// <summary>
    /// Builds the set of the static objects inside some EnvCells of a landblock (<c>CEnvCell::init_static_objects</c>):
    /// every object whose model loads (<c>CPhysicsObj::makeObject</c>) is created at its stored frame
    /// (<c>CPhysicsObj::add_obj_to_cell</c>; landblock-local, like LandBlockInfo frames, and with no inside-the-landblock
    /// test: dungeon cells lie far outside their 192 m square). Parts, LOD chains and meshes are those of every other
    /// object (<see cref="ObjectPlacement"/>, draw context Object).
    /// </summary>
    /// <param name="coord">The landblock.</param>
    /// <param name="objects">The cells' statics in cell order (cell id, then the cell's list in stored order).</param>
    /// <param name="library">Resolved models and LOD chains.</param>
    public static LandblockObjectSet BuildCellObjects(LandblockCoord coord, IReadOnlyList<(uint CellId, StaticObject Object)> objects, ObjectModelLibrary library)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(library);
        var builder = new Builder(coord, library);
        foreach (var (_, obj) in objects)
        {
            builder.Reserve(obj.ModelId);
        }

        builder.Allocate();
        foreach (var (cellId, obj) in objects)
        {
            builder.Add(WorldObjectKind.CellStatic, obj.ModelId, obj.Frame.Origin, obj.Frame.Orientation, 1f, cellId);
        }

        return builder.Finish();
    }

    /// <summary>
    /// Retail's test for a LandBlockInfo object's frame: <c>LandDefs::adjust_to_outside</c> of (landblock id, origin)
    /// succeeds and lands in one of this landblock's own cells (<c>CLandBlock::get_landcell</c>).
    /// </summary>
    public static bool IsInOwnLandblock(LandblockCoord coord, Vector3 origin)
    {
        var cellId = coord.LandBlockId;
        var local = origin;
        return AcCoordinates.AdjustToOutside(ref cellId, ref local) && LandblockCoord.FromId(cellId) == coord;
    }

    private sealed class Builder(LandblockCoord coord, ObjectModelLibrary library)
    {
        private readonly Dictionary<ObjectMeshKey, int> _meshIndex = [];
        private readonly List<ObjectMeshKey> _meshes = [];
        private readonly Dictionary<string, int> _rejected = [];
        private WorldObject[] _objects = [];
        private ObjectPartPlacement[] _parts = [];
        private int[] _levelMeshes = [];
        private int _objectCapacity;
        private int _partCapacity;
        private int _levelCapacity;
        private int _objectCount;
        private int _partCount;
        private int _levelCount;

        /// <summary>Counts an object that may be added (upper bounds for <see cref="Allocate"/>).</summary>
        public void Reserve(uint modelId)
        {
            var model = library.GetModel(modelId);
            _objectCapacity++;
            _partCapacity += model.Parts.Count;
            foreach (var part in model.Parts)
            {
                _levelCapacity += part.Chain.GfxObjIds.Count;
            }
        }

        public void Allocate()
        {
            _objects = new WorldObject[_objectCapacity];
            _parts = new ObjectPartPlacement[_partCapacity];
            _levelMeshes = new int[_levelCapacity];
        }

        public void AddLandBlockInfoObject(WorldObjectKind kind, uint modelId, Placement frame)
        {
            if (!IsInOwnLandblock(coord, frame.Origin))
            {
                Reject($"{kind}: origin outside its landblock (get_landcell fails)");
                return;
            }

            Add(kind, modelId, frame.Origin, frame.Orientation, 1f);
        }

        public void Add(WorldObjectKind kind, uint modelId, Vector3 origin, Quaternion orientation, float scale, uint cellId = 0)
        {
            var model = library.GetModel(modelId);
            if (!model.IsLoadable)
            {
                Reject($"{kind}: model does not load");
                return;
            }

            var rotation = Quaternion.Normalize(orientation);
            var context = kind == WorldObjectKind.Building ? MeshDrawContext.Building : MeshDrawContext.Object;
            var objectIndex = _objectCount;
            var firstPart = _partCount;
            foreach (var part in model.Parts)
            {
                // The one posing rule (CPartArray::UpdateParts / Frame::combine with the object scale).
                var placed = ObjectPlacement.Place(part, origin, rotation, scale);
                var levels = part.Chain.GfxObjIds;
                var firstLevel = _levelCount;
                for (var l = 0; l < levels.Count; l++)
                {
                    _levelMeshes[_levelCount++] = levels[l] == 0 ? -1 : MeshIndex(new ObjectMeshKey(levels[l], context, placed.Shape));
                }

                _parts[_partCount++] = new ObjectPartPlacement(
                    objectIndex, placed.Origin, placed.Rotation, placed.Scale, placed.Shape, placed.SortCenter, part.Chain, firstLevel, part.SortCenter, part.Index);
            }

            var cellCenter = kind == WorldObjectKind.CellStatic ? Vector3.Zero : CellCenterOf(origin);
            _objects[_objectCount++] = new WorldObject(kind, modelId, origin, rotation, scale, firstPart, model.Parts.Count, cellCenter, cellId);
        }

        /// <summary>
        /// The centre of the land cell that holds <paramref name="origin"/> (after <c>LandDefs::adjust_to_outside</c>'s
        /// snapping, the cell <c>CPhysicsObj::add_obj_to_cell</c> enters).
        /// </summary>
        private static Vector3 CellCenterOf(Vector3 origin)
        {
            const float cell = (float)AcCoordinates.CellLength;
            var x = MathF.Abs(origin.X) < AcCoordinates.AdjustToOutsideEpsilon ? 0f : origin.X;
            var y = MathF.Abs(origin.Y) < AcCoordinates.AdjustToOutsideEpsilon ? 0f : origin.Y;
            var cx = Math.Clamp((int)MathF.Floor(x / cell), 0, AcCoordinates.CellsPerLandblock - 1);
            var cy = Math.Clamp((int)MathF.Floor(y / cell), 0, AcCoordinates.CellsPerLandblock - 1);
            return new Vector3((cx + 0.5f) * cell, (cy + 0.5f) * cell, 0f);
        }

        public LandblockObjectSet Finish()
        {
            // Trimmed only when retail rejected something (never on end-of-retail LandBlockInfos).
            if (_objectCount < _objects.Length)
            {
                Array.Resize(ref _objects, _objectCount);
                Array.Resize(ref _parts, _partCount);
                Array.Resize(ref _levelMeshes, _levelCount);
            }

            return new(coord, _objects, _parts, _levelMeshes, [.. _meshes], _rejected);
        }

        private int MeshIndex(ObjectMeshKey key)
        {
            if (!_meshIndex.TryGetValue(key, out var index))
            {
                index = _meshes.Count;
                _meshes.Add(key);
                _meshIndex.Add(key, index);
            }

            return index;
        }

        private void Reject(string reason) => _rejected[reason] = _rejected.GetValueOrDefault(reason) + 1;
    }
}
