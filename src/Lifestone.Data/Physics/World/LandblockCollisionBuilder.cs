// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/Landblock.cs (init_static_objs,
// init_buildings, PostInit) and Source/ACE.Server/Physics/PhysicsObj.cs (calc_cross_cells_static, add_shadows_to_cells)
// (AGPL-3.0)
// with changes: built from the cells, models and scenery tracks' decoders on any thread, into an immutable
// LandblockCollision; checked against retail CLandBlock::init_buildings, init_static_objs, get_land_scenes and
// CEnvCell::init_static_objects (cited below); shadows that fall into another landblock's cells are kept for the world
// to register when both are attached; objects with no collision shape at all are left out (they never stop anything).

using System.Collections.Concurrent;
using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// Builds a landblock's <see cref="LandblockCollision"/> from the dats: terrain cells, EnvCells, buildings and the static
/// objects (LandBlockInfo statics, procedural scenery, EnvCell statics) with their shadows.
/// </summary>
/// <remarks>
/// <para><b>Retail order.</b> Land cells and their water (<c>CLandBlockStruct::ConstructPolygons</c>, <c>CalcWater</c>);
/// buildings into the land cell under their origin (<c>CLandBlock::init_buildings</c>, 11.4186 0x0052FD80, 11.6096
/// 0x00530AC0); the restriction table onto the land cells; EnvCells; then each static object is made
/// (<c>CPhysicsObj::makeObject</c>: a part per Setup part, its level-0 GfxObj, the rest pose), placed in its cell
/// (<c>CLandBlock::init_static_objs</c>, 11.4186 0x00530A40, 11.6096 0x00531780; <c>get_land_scenes</c>, 0x00530460,
/// 0x005311A0; <c>CEnvCell::init_static_objects</c>, 0x0052C350, 0x0052CF60) and registered in every cell it reaches
/// (<c>CPhysicsObj::calc_cross_cells_static</c>, 0x00515160, 0x00515C60).</para>
/// <para>Thread-safe: build different landblocks on different threads. Shapes are cached for the builder's lifetime.</para>
/// </remarks>
public sealed class LandblockCollisionBuilder
{
    private readonly DatSet _dats;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly CellStructureLibrary _structures;
    private readonly SceneryGenerator? _scenery;
    private readonly ConcurrentDictionary<uint, ModelTemplate?> _models = new();
    private readonly ConcurrentDictionary<uint, PartTemplate?> _parts = new();

    /// <summary>A builder over <paramref name="dats"/>.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="geometry">Shared collision shapes; a new library when null.</param>
    /// <param name="structures">Shared room shapes; a new library when null.</param>
    /// <param name="scenery">The scenery generator; null leaves scenery out (a test option, not retail).</param>
    /// <param name="includeScenery">False leaves procedural scenery out even with a generator.</param>
    public LandblockCollisionBuilder(DatSet dats, PhysicsGeometryLibrary? geometry = null, CellStructureLibrary? structures = null, SceneryGenerator? scenery = null, bool includeScenery = true)
    {
        ArgumentNullException.ThrowIfNull(dats);
        _dats = dats;
        _geometry = geometry ?? new PhysicsGeometryLibrary(dats);
        _structures = structures ?? new CellStructureLibrary(dats);
        _scenery = includeScenery ? scenery ?? SceneryGenerator.FromDats(dats) : null;
    }

    /// <summary>The collision shapes the builder uses.</summary>
    public PhysicsGeometryLibrary Geometry => _geometry;

    /// <summary>
    /// Builds the landblock's collision (a landblock without a LandBlock record has no cells).
    /// </summary>
    /// <exception cref="InvalidDataException">A record of the landblock is malformed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public LandblockCollision Build(LandblockCoord coord, CancellationToken cancellationToken = default)
    {
        var terrain = _dats.GetLandblockTerrain(coord);
        if (terrain is null)
        {
            return new LandblockCollision(coord);
        }

        var assembly = new Assembly(coord, new TerrainSampler(terrain, _dats.TerrainRegion.LandHeightTable), terrain);
        var envCells = LandblockCellSet.Load(_structures, coord).Cells.OrderBy(c => c.Id).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var statics = LandblockStatics.Read(_dats, coord);
        if (statics is not null)
        {
            foreach (var building in statics.Buildings)
            {
                AddBuilding(assembly, building);
            }

            foreach (var cell in assembly.LandCells)
            {
                if (statics.RestrictionTable.TryGetValue(cell.Id, out var restriction) && restriction != 0)
                {
                    cell.RestrictionObject = restriction;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var data in envCells)
        {
            assembly.EnvCells.Add(data.Id, MakeEnvCell(data));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (statics is not null)
        {
            foreach (var placed in statics.Objects)
            {
                var cellId = coord.LandBlockId;
                var origin = placed.Frame.Origin;
                if (!LandDefs.AdjustToOutside(ref cellId, ref origin) || assembly.GetCell(cellId) is not PhysLandCell)
                {
                    continue;
                }

                assembly.AddStatic(MakeStatic(placed.ModelId, new CellFrame(cellId, ToFrame(placed.Frame)), 1.0f));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_scenery is not null)
        {
            foreach (var scene in _scenery.Generate(coord).Placements)
            {
                var frame = new AcFrame(scene.Origin, AcFrame.SetRotate(scene.Orientation, Quaternion.Identity));
                assembly.AddStatic(MakeStatic(scene.ObjectId, new CellFrame(scene.CellId(coord), frame), scene.Scale));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var data in envCells)
        {
            foreach (var placed in data.StaticObjects)
            {
                assembly.AddStatic(MakeStatic(placed.ModelId, new CellFrame(data.Id, ToFrame(placed.Frame)), 1.0f));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return assembly.Finish();
    }

    /// <summary>
    /// A landblock with terrain only (no LandBlockInfo, EnvCells or scenery): synthetic worlds in tests, and the terrain
    /// half of <see cref="Build"/>.
    /// </summary>
    public static LandblockCollision FromTerrain(LandblockTerrain terrain, IReadOnlyList<float> landHeightTable) =>
        FromParts(terrain, landHeightTable, [], []);

    /// <summary>
    /// A landblock from parts (synthetic worlds in tests): terrain, EnvCells, and static objects registered in the cells
    /// they reach as <see cref="Build"/> registers them.
    /// </summary>
    internal static LandblockCollision FromParts(LandblockTerrain terrain, IReadOnlyList<float> landHeightTable, IEnumerable<PhysEnvCell> envCells, IEnumerable<PhysStaticObject> statics)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var assembly = new Assembly(terrain.Coord, new TerrainSampler(terrain, landHeightTable), terrain);
        foreach (var cell in envCells)
        {
            assembly.EnvCells.Add(cell.Id, cell);
        }

        foreach (var obj in statics)
        {
            assembly.AddStatic(obj);
        }

        return assembly.Finish();
    }

    /// <summary>
    /// The physics object retail's <c>CPhysicsObj::makeObject</c> makes of <paramref name="modelId"/> as a static at
    /// <paramref name="position"/>, or null when it cannot be made (a part that does not load) or can stop nothing.
    /// </summary>
    internal PhysStaticObject? MakeStatic(uint modelId, in CellFrame position, float scale)
    {
        var model = GetModel(modelId);
        if (model is null)
        {
            return null;
        }

        var parts = new PhysPart[model.Parts.Length];
        var hasPhysicsBsp = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var template = model.Parts[i];
            var partFrame = AcFrame.Combine(position.Frame, new AcFrame(template.RestFrame.Origin * scale, template.RestFrame.Orientation));
            parts[i] = new PhysPart(new CellFrame(position.CellId, partFrame), template.Shape, template.DefaultScale * scale, template.Always2D);
            hasPhysicsBsp |= template.Shape.Physics is not null;
        }

        var state = PhysicsState.DefaultForNewObject | PhysicsState.Static;
        if (hasPhysicsBsp)
        {
            state |= PhysicsState.HasPhysicsBsp;
        }

        var result = new PhysStaticObject(modelId, position, state, scale, parts, model.CylSpheres, model.Spheres);
        return result.HasCollisionShape ? result : null;
    }

    private static AcFrame ToFrame(Placement placement) => new(placement.Origin, placement.Orientation);

    /// <summary>
    /// Retail <c>CLandBlock::init_buildings</c>: the building (<c>CBuildingObj::makeBuilding</c>, 11.4186 0x006B53A0, 11.6096
    /// 0x006B62E0: one part, the building GfxObj) placed when its origin is in one of this landblock's cells, and given to
    /// that cell unless it already has one (<c>CSortCell::add_building</c>).
    /// </summary>
    private void AddBuilding(Assembly assembly, BuildingData building)
    {
        var cellId = assembly.Coord.LandBlockId;
        var origin = building.Frame.Origin;
        if (!LandDefs.AdjustToOutside(ref cellId, ref origin) || assembly.GetCell(cellId) is not PhysLandCell cell)
        {
            return;
        }

        var part = GetPart(building.ModelId);
        if (part is null)
        {
            return;
        }

        var position = new CellFrame(cellId, ToFrame(building.Frame));
        var physBuilding = new PhysBuilding(
            building.ModelId,
            position,
            new PhysPart(position, part.Shape, Vector3.One, part.Always2D),
            building.Portals.ToArray());
        assembly.Buildings.Add(physBuilding);
        cell.Building ??= physBuilding;
    }

    private PhysEnvCell MakeEnvCell(EnvCellData data)
    {
        var structure = _structures.Get(data);
        var physics = _geometry.GetCellStruct(data.EnvironmentId, data.CellStructureIndex);
        var portals = new PhysCellPortal[data.Portals.Count];
        for (var i = 0; i < portals.Length; i++)
        {
            var portal = data.Portals[i];
            var polygon = structure.GetPortal(portal.PolygonId)
                ?? throw new InvalidDataException($"EnvCell 0x{data.Id:X8}: portal {i} names polygon {portal.PolygonId}, which is not one of its struct's portals.");
            portals[i] = new PhysCellPortal(portal.OtherCellId, portal.OtherPortalIndex, portal.PortalSide, PhysPolygon.MakePlane(polygon.Vertices), polygon.Vertices);
        }

        return new PhysEnvCell(data.Id, new CellFrame(data.Id, ToFrame(data.Frame)), structure.Bsp, physics, portals, data.VisibleCellIds.ToArray(), data.SeenOutside)
        {
            RestrictionObject = data.RestrictionObjectId,
        };
    }

    private ModelTemplate? GetModel(uint modelId) => _models.GetOrAdd(modelId, LoadModel);

    private ModelTemplate? LoadModel(uint modelId)
    {
        switch (modelId >> 24)
        {
            case 0x01:
            {
                var part = GetPart(modelId);
                return part is null
                    ? null
                    : new ModelTemplate([new PartTemplate(part.Shape, PartFrame.Identity, Vector3.One, part.Always2D)], [], []);
            }

            case 0x02:
            {
                if (!_dats.TryGetPortal<Setup>(modelId, out var setup))
                {
                    return null;
                }

                var assembly = ModelAssembly.FromSetup(modelId, setup);
                var placement = assembly.ResolvePlacement(PlacementIds.Resting);
                var parts = new PartTemplate[assembly.Parts.Count];
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = GetPart(assembly.Parts[i].GfxObjId);
                    if (part is null)
                    {
                        // CPartArray::InitParts: a part that does not load fails the whole object.
                        return null;
                    }

                    var frame = placement is not null && i < placement.Frames.Count ? placement.Frames[i] : PartFrame.Identity;
                    parts[i] = new PartTemplate(part.Shape, frame, assembly.Parts[i].DefaultScale, part.Always2D);
                }

                return new ModelTemplate(
                    parts,
                    assembly.CylSpheres.Select(c => new PhysCylSphere(c.LowPoint, c.Height, c.Radius)).ToArray(),
                    assembly.Spheres.Select(s => new PhysSphere(s.Center, s.Radius)).ToArray());
            }

            default:
                return null;
        }
    }

    /// <summary>A part's level-0 GfxObj (<c>CPhysicsPart::LoadGfxObjArray</c>), or null when it does not load.</summary>
    private PartTemplate? GetPart(uint gfxObjId) => _parts.GetOrAdd(gfxObjId, LoadPart);

    private PartTemplate? LoadPart(uint gfxObjId)
    {
        if (gfxObjId >> 24 != 0x01)
        {
            return null;
        }

        var chain = LodChain.Load(_dats, gfxObjId);
        if (!chain.IsLoadable)
        {
            return null;
        }

        var shape = _geometry.GetGfxObjShape(chain.NearestGfxObjId);
        return shape is null ? null : new PartTemplate(shape, PartFrame.Identity, Vector3.One, chain.Table?.Always2D ?? false);
    }

    private sealed record ModelTemplate(PartTemplate[] Parts, PhysCylSphere[] CylSpheres, PhysSphere[] Spheres);

    private sealed record PartTemplate(GfxObjShape Shape, PartFrame RestFrame, Vector3 DefaultScale, bool Always2D);

    /// <summary>
    /// One landblock while it is built: its cells (the only ones its static objects can find), its buildings and statics,
    /// and the shadows they cast.
    /// </summary>
    private sealed class Assembly : IPhysicsCellSource
    {
        private readonly LandblockTerrain _terrain;
        private readonly WaterType _waterType;
        private readonly List<PhysStaticObject> _statics = [];
        private readonly Dictionary<PhysCell, List<PhysStaticObject>> _shadows = [];
        private readonly List<ForeignShadow> _foreign = [];
        private readonly CellArray _cellArray = new() { DoNotLoadCells = true };

        public Assembly(LandblockCoord coord, TerrainSampler sampler, LandblockTerrain terrain)
        {
            Coord = coord;
            _terrain = terrain;
            var (cellWater, blockWater) = LandblockCollision.CalcWater(terrain.TerrainWords);
            _waterType = blockWater;
            LandCells = new PhysLandCell[TerrainConstants.CellCount];
            for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
            {
                for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
                {
                    var index = (cx * TerrainConstants.CellsPerSide) + cy;
                    LandCells[index] = new PhysLandCell(CellIds.Make(coord, (ushort)(index + 1)), sampler, cx, cy, cellWater[index]);
                }
            }
        }

        public LandblockCoord Coord { get; }

        public PhysLandCell[] LandCells { get; }

        public Dictionary<uint, PhysEnvCell> EnvCells { get; } = [];

        public List<PhysBuilding> Buildings { get; } = [];

        public PhysCell? GetCell(uint cellId)
        {
            if (LandblockCoord.FromId(cellId) != Coord)
            {
                return null;
            }

            var low = cellId & 0xFFFF;
            if (low is >= 1 and <= 0x40)
            {
                return LandCells[low - 1];
            }

            return EnvCells.TryGetValue(cellId, out var cell) ? cell : null;
        }

        public List<PhysStaticObject>? GetForeignShadows(uint cellId) => null;

        public List<Objects.PhysicsObj>? GetObjectShadows(uint cellId) => null;

        /// <summary>No player moves through a landblock while it is built.</summary>
        public void NoteUnenforcedRestriction(uint cellId)
        {
        }

        /// <summary>
        /// Retail <c>CPhysicsObj::add_obj_to_cell</c> (11.4186 0x005159E0, 11.6096 0x005164E0) and
        /// <c>calc_cross_cells_static</c>: the object joins every cell its cylinder-spheres (an object without a physics BSP)
        /// or its parts' boxes reach; cells of other landblocks are kept as foreign shadows.
        /// </summary>
        public void AddStatic(PhysStaticObject? obj)
        {
            if (obj is null)
            {
                return;
            }

            var ownCell = GetCell(obj.Position.CellId);
            if (ownCell is null)
            {
                return;
            }

            _statics.Add(obj);
            _cellArray.DoNotLoadCells = true;
            var byCylSpheres = (obj.State & PhysicsState.HasPhysicsBsp) == 0 && obj.CylSpheres.Count > 0;
            if (byCylSpheres)
            {
                var cylSpheres = new PhysCylSphere[obj.CylSpheres.Count];
                for (var i = 0; i < cylSpheres.Length; i++)
                {
                    cylSpheres[i] = obj.CylSpheres[i];
                }

                CellLists.FindCellList(this, obj.Position, cylSpheres, _cellArray);
            }
            else
            {
                CellLists.FindBboxCellList(this, obj.Position.CellId, ownCell, obj.PartSpan, _cellArray);
            }

            for (var i = 0; i < _cellArray.Count; i++)
            {
                var cell = _cellArray.CellAt(i);
                if (cell is not null)
                {
                    if (!_shadows.TryGetValue(cell, out var list))
                    {
                        _shadows.Add(cell, list = []);
                    }

                    list.Add(obj);
                }
                else if (LandblockCoord.FromId(_cellArray.IdAt(i)) != Coord)
                {
                    _foreign.Add(new ForeignShadow(_cellArray.IdAt(i), obj, FromCellBlock: !byCylSpheres));
                }
            }
        }

        public LandblockCollision Finish()
        {
            foreach (var (cell, list) in _shadows)
            {
                cell.SetShadows(list.ToArray());
            }

            return new LandblockCollision(
                Coord,
                LandCells,
                EnvCells,
                Buildings.ToArray(),
                _statics.ToArray(),
                _foreign.ToArray(),
                _terrain.TerrainWords.ToArray(),
                _waterType);
        }
    }
}
