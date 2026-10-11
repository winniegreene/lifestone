using System.Collections.Concurrent;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// <see cref="ISceneryDataSource"/> over the user's dats. Scenes and object bounds are decoded once and cached
/// (there are 131 reachable Scenes and 167 scenery object ids at end of retail). Thread-safe: dat reads go through
/// <see cref="DatSet"/>'s lock and the caches are concurrent.
/// </summary>
public sealed class DatSceneryDataSource : ISceneryDataSource
{
    private readonly DatSet _dats;
    private readonly Lazy<SceneryRegion> _region;
    private readonly ConcurrentDictionary<uint, SceneDescription?> _scenes = new();
    private readonly ConcurrentDictionary<uint, SceneryObjectBounds?> _bounds = new();

    public DatSceneryDataSource(DatSet dats)
    {
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _region = new Lazy<SceneryRegion>(() => BuildRegion(_dats.Region), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SceneryRegion Region => _region.Value;

    public SceneryLandblock? GetLandblock(LandblockCoord coord)
    {
        var landBlock = _dats.GetLandBlock(coord);
        if (landBlock is null)
        {
            return null;
        }

        var terrain = LandblockTerrain.FromLandBlock(coord, landBlock);
        var buildings = Array.Empty<SceneryBuilding>();

        // Retail CLandBlock::InitLoad reads the LandBlockInfo only when the LandBlock's lbi_exists (HasObjects) flag is
        // set, and fails to load the landblock when that record is missing.
        if (landBlock.HasObjects)
        {
            if (!_dats.TryGetCell<LandBlockInfo>(coord.LandBlockInfoId, out var info))
            {
                throw new InvalidDataException(
                    $"LandBlock {coord} has HasObjects set but LandBlockInfo 0x{coord.LandBlockInfoId:X8} is missing " +
                    "(retail CLandBlock::InitLoad fails the whole landblock).");
            }

            buildings = info.Buildings.Select(b => new SceneryBuilding(b.ModelId, b.Frame.Origin)).ToArray();
        }

        return new SceneryLandblock(terrain, buildings);
    }

    public SceneDescription? GetScene(uint sceneId) => _scenes.GetOrAdd(sceneId, LoadScene);

    public SceneryObjectBounds? GetObjectBounds(uint objectId) => _bounds.GetOrAdd(objectId, LoadBounds);

    /// <summary>Copies the Region's scene tables (retail CRegionDesc::UnPack resolves the same indices).</summary>
    public static SceneryRegion BuildRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var heights = region.LandDefs?.LandHeightTable?.ToArray()
            ?? throw new InvalidDataException("Region has no LandDefs.LandHeightTable.");
        var terrainTypes = region.TerrainInfo?.TerrainTypes
            ?? throw new InvalidDataException("Region has no TerrainInfo.");
        var sceneTypes = region.SceneInfo?.SceneTypes ?? [];

        return new SceneryRegion(
            heights,
            terrainTypes.Select(t => (IReadOnlyList<uint>)t.SceneTypes.ToArray()).ToArray(),
            sceneTypes.Select(s => (IReadOnlyList<uint>)s.Scenes.Select(q => q.DataId).ToArray()).ToArray());
    }

    private SceneDescription? LoadScene(uint sceneId)
    {
        if (!_dats.TryGetPortal<Scene>(sceneId, out var scene))
        {
            return null;
        }

        var objects = new SceneObjectDesc[scene.Objects.Count];
        for (var i = 0; i < objects.Length; i++)
        {
            var o = scene.Objects[i];

            // Retail ObjectDesc::UnPack -> Frame::UnPack rejects frames that fail Frame::IsValid, which fails the
            // whole Scene. That never happens at end of retail; refuse loudly rather than guess how it would degrade.
            if (!RetailFrame.IsValidQuaternion(o.BaseLoc.Orientation))
            {
                throw new InvalidDataException($"Scene 0x{sceneId:X8} object {i} has an invalid BaseLoc orientation {o.BaseLoc.Orientation}.");
            }

            objects[i] = new SceneObjectDesc(
                o.ObjectId,
                o.BaseLoc.Origin,
                o.BaseLoc.Orientation,
                o.Frequency,
                o.DisplaceX,
                o.DisplaceY,
                o.MinScale,
                o.MaxScale,
                o.MaxRotation,
                o.MinSlope,
                o.MaxSlope,
                o.Align,
                o.Orient,
                o.WeenieObj);
        }

        return new SceneDescription(sceneId, objects);
    }

    /// <summary>
    /// Mirrors retail <c>CPhysicsObj::InitPartArrayObject</c>: 0x01 ids go through <c>CPartArray::CreateMesh</c>
    /// (<c>CSetup::makeSimpleSetup</c>), 0x02 ids through <c>CPartArray::CreateSetup</c>; any other non-zero type byte
    /// fails. Each part loads through <c>CPhysicsPart::LoadGfxObjArray</c>.
    /// </summary>
    private SceneryObjectBounds? LoadBounds(uint objectId)
    {
        switch (objectId >> 24)
        {
            case 0x01:
            {
                // CSetup::makeSimpleSetup: sorting sphere = physics sphere, else drawing sphere, of the root GfxObj.
                if (!_dats.TryGetPortal<GfxObj>(objectId, out var root))
                {
                    return null;
                }

                var part = LoadFirstGfxObj(objectId);
                if (part is null)
                {
                    return null;
                }

                var sphere = PhysicsSphere(root) ?? DrawingSphere(root) ?? default;
                return new SceneryObjectBounds(objectId, HasPhysicsBsp(part), sphere, [], 0);
            }

            case 0x02:
            {
                if (!_dats.TryGetPortal<Setup>(objectId, out var setup))
                {
                    return null;
                }

                // CPartArray::InitParts fails for a Setup with no parts, and when any part fails to load.
                if (setup.Parts.Count == 0)
                {
                    return null;
                }

                var hasPhysicsBsp = false;
                foreach (var partId in setup.Parts)
                {
                    var part = LoadFirstGfxObj(partId.DataId);
                    if (part is null)
                    {
                        return null;
                    }

                    hasPhysicsBsp |= HasPhysicsBsp(part);
                }

                return new SceneryObjectBounds(
                    objectId,
                    hasPhysicsBsp,
                    new ScenerySphere(setup.SortingSphere?.Origin ?? default, setup.SortingSphere?.Radius ?? 0f),
                    setup.CylSpheres.Select(c => new SceneryCylSphere(c.Origin, c.Radius, c.Height)).ToArray(),
                    setup.Spheres.Count);
            }

            case 0x00:
                throw new NotSupportedException(
                    $"TODO(scenery): object id 0x{objectId:X8} would take retail's CPhysicsObj::makeAnimObject path " +
                    "(id | 0x02000000, translucent, ethereal), which is not implemented. No end-of-retail Scene uses it.");

            default:
                return null;
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsPart::LoadGfxObjArray</c>: the part's level-0 GfxObj is the first GfxObjDegradeInfo entry when
    /// the root has a degrade table, else the root itself; null when that GfxObj is missing.
    /// </summary>
    private GfxObj? LoadFirstGfxObj(uint gfxObjId)
    {
        if (gfxObjId == 0 || !_dats.TryGetPortal<GfxObj>(gfxObjId, out var root))
        {
            return null;
        }

        if (root.DIDDegrade != 0 && _dats.TryGetPortal<GfxObjDegradeInfo>(root.DIDDegrade, out var degrades))
        {
            if (degrades.Degrades.Count == 0)
            {
                // Retail would read an uninitialised gfxobj[0] here (operator new[](0)); refuse instead of guessing.
                throw new InvalidDataException(
                    $"GfxObj 0x{gfxObjId:X8} has an empty GfxObjDegradeInfo 0x{root.DIDDegrade:X8}.");
            }

            var first = degrades.Degrades[0].Id.DataId;
            return first != 0 && _dats.TryGetPortal<GfxObj>(first, out var level0) ? level0 : null;
        }

        return root;
    }

    private static bool HasPhysicsBsp(GfxObj gfxObj) =>
        gfxObj.Flags.HasFlag(GfxObjFlags.HasPhysics) && gfxObj.PhysicsBSP?.Root is not null;

    /// <summary>CGfxObj physics_sphere = BSPTREE::GetSphere(physics_bsp), the root node's bounding sphere.</summary>
    private static ScenerySphere? PhysicsSphere(GfxObj gfxObj) =>
        gfxObj.Flags.HasFlag(GfxObjFlags.HasPhysics) && gfxObj.PhysicsBSP?.Root is { } root
            ? ToSphere(root.BoundingSphere)
            : null;

    /// <summary>CGfxObj drawing_sphere = BSPTREE::GetSphere(drawing_bsp).</summary>
    private static ScenerySphere? DrawingSphere(GfxObj gfxObj) =>
        gfxObj.Flags.HasFlag(GfxObjFlags.HasDrawing) && gfxObj.DrawingBSP?.Root is { } root
            ? ToSphere(root.BoundingSphere)
            : null;

    private static ScenerySphere? ToSphere(Sphere? sphere) =>
        sphere is null ? null : new ScenerySphere(sphere.Origin, sphere.Radius);
}
