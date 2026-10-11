using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// One ObjectDesc of a Scene (0x12) record: a candidate scenery object and its placement rules.
/// Field meanings follow retail <c>ObjectDesc</c> (Scene.c) and ACE <c>ACE.DatLoader.Entity.ObjectDesc</c>.
/// </summary>
/// <param name="ObjectId">GfxObj (0x01) or Setup (0x02) to place.</param>
/// <param name="BaseOrigin">BaseLoc origin: offset from the spawning vertex before the quadrant rotation.</param>
/// <param name="BaseOrientation">BaseLoc orientation exactly as stored (retail does not normalise it).</param>
/// <param name="Frequency">The object is a candidate when the frequency noise is below this.</param>
/// <param name="DisplaceX">Random X displacement range (used when &gt; 0).</param>
/// <param name="DisplaceY">Random Y displacement range (used when &gt; 0).</param>
/// <param name="MinScale">Minimum scale.</param>
/// <param name="MaxScale">Maximum scale; scale = Min * (Max / Min)^noise.</param>
/// <param name="MaxRotation">Random heading range in degrees (used when &gt; 0 and <paramref name="Align"/> is 0).</param>
/// <param name="MinSlope">Minimum terrain normal z.</param>
/// <param name="MaxSlope">Maximum terrain normal z.</param>
/// <param name="Align">Non-zero: face down the terrain slope (ObjectDesc::ObjAlign).</param>
/// <param name="Orient">Stored but not read by retail scenery placement.</param>
/// <param name="WeenieObj">Non-zero: a server-spawned weenie, never placed as scenery.</param>
public readonly record struct SceneObjectDesc(
    uint ObjectId,
    Vector3 BaseOrigin,
    Quaternion BaseOrientation,
    float Frequency,
    float DisplaceX,
    float DisplaceY,
    float MinScale,
    float MaxScale,
    float MaxRotation,
    float MinSlope,
    float MaxSlope,
    int Align,
    int Orient,
    uint WeenieObj);

/// <summary>A Scene (0x12) record: the ordered ObjectDesc list (the order is part of the random hashes).</summary>
public sealed class SceneDescription(uint id, IReadOnlyList<SceneObjectDesc> objects)
{
    public uint Id { get; } = id;

    public IReadOnlyList<SceneObjectDesc> Objects { get; } = objects ?? throw new ArgumentNullException(nameof(objects));
}

/// <summary>A sphere in an object's local frame.</summary>
public readonly record struct ScenerySphere(Vector3 Center, float Radius);

/// <summary>A Setup cylinder-sphere: low point, radius, height (dat order low point, radius, height).</summary>
public readonly record struct SceneryCylSphere(Vector3 LowPoint, float Radius, float Height);

/// <summary>
/// What retail <c>CPhysicsObj::makeObject</c> builds for a scenery id, reduced to what
/// <c>CPhysicsObj::obj_within_block</c> reads.
/// </summary>
/// <param name="ObjectId">The GfxObj or Setup id.</param>
/// <param name="HasPhysicsBsp">
/// Retail HAS_PHYSICS_BSP state: some part's first GfxObj (degrade level 0) has a physics BSP
/// (<c>CPartArray::CacheHasPhysicsBSP</c>).
/// </param>
/// <param name="SortingSphere">
/// Setup.SortingSphere, or for a bare GfxObj its physics sphere, else its drawing sphere, else zero
/// (<c>CSetup::makeSimpleSetup</c>).
/// </param>
/// <param name="CylSpheres">Setup cylinder-spheres (none for a bare GfxObj).</param>
/// <param name="SphereCount">Number of Setup spheres (none for a bare GfxObj); only the count is read.</param>
public sealed record SceneryObjectBounds(
    uint ObjectId,
    bool HasPhysicsBsp,
    ScenerySphere SortingSphere,
    IReadOnlyList<SceneryCylSphere> CylSpheres,
    int SphereCount)
{
    /// <summary>Retail <c>LandDefs</c> block length (24 * 8).</summary>
    private const double BlockLength = 192.0;

    /// <summary>
    /// Retail <c>CPhysicsObj::obj_within_block</c> for an object placed at <paramref name="frame"/> (before scaling):
    /// the sorting sphere (physics-BSP objects), every cylinder-sphere, the sorting sphere (sphere objects) or the
    /// origin must lie inside the landblock's 0..192 square, shrunk by the radius. Z is not checked.
    /// </summary>
    internal bool IsWithinBlock(RetailFrame frame)
    {
        if (HasPhysicsBsp)
        {
            var center = frame.LocalToGlobal(SortingSphere.Center);
            double radius = SortingSphere.Radius;
            if (center.X >= radius && center.Y >= radius)
            {
                var limit = BlockLength - radius;
                return center.X < limit && center.Y < limit;
            }

            return false;
        }

        if (CylSpheres.Count > 0)
        {
            var m = frame.Matrix;
            var origin = frame.Origin;
            foreach (var cyl in CylSpheres)
            {
                // The low point in landblock coordinates: origin + R * low. Retail transforms it inline here instead
                // of through Frame::localtoglobal, and its x87 code (11.6096 0x511BF5) adds the rotated terms in a
                // different order: the y, z and x terms for X, whose sum stays in a register (double), and the y, x
                // and z terms for Y, whose sum is stored as float. The order can move the last double bit, so we
                // keep it.
                var low = cyl.LowPoint;
                var rotatedX = ((low.Y * (double)m[3]) + (low.Z * (double)m[6])) + (low.X * (double)m[0]);
                var rotatedY = ((low.Y * (double)m[4]) + (low.X * (double)m[1])) + (low.Z * (double)m[7]);
                var x = rotatedX + origin.X;
                var y = (float)(rotatedY + origin.Y);
                double radius = cyl.Radius;
                if (x < radius || y < radius)
                {
                    return false;
                }

                var limit = (float)(BlockLength - radius);
                if (x >= limit || y >= limit)
                {
                    return false;
                }
            }

            return true;
        }

        return SphereCount > 0
            ? WithinBlock(frame.LocalToGlobal(SortingSphere.Center), SortingSphere.Radius)
            : WithinBlock(frame.Origin, 0f);
    }

    /// <summary>Retail <c>LandDefs::within_block</c>.</summary>
    private static bool WithinBlock(Vector3 v, float radius)
    {
        if (v.X >= (double)radius && v.Y >= (double)radius)
        {
            var limit = BlockLength - radius;
            return limit > v.X && limit > v.Y;
        }

        return false;
    }
}

/// <summary>A building from LandBlockInfo, reduced to what scenery needs.</summary>
/// <param name="ModelId">The building's GfxObj/Setup.</param>
/// <param name="Origin">Landblock-local origin of the building frame.</param>
public readonly record struct SceneryBuilding(uint ModelId, Vector3 Origin);

/// <summary>The per-landblock inputs of scenery placement.</summary>
/// <param name="Terrain">Heights and terrain words.</param>
/// <param name="Buildings">LandBlockInfo buildings (empty when the landblock has no LandBlockInfo).</param>
public sealed record SceneryLandblock(LandblockTerrain Terrain, IReadOnlyList<SceneryBuilding> Buildings);

/// <summary>
/// The Region's procedural-scenery tables (retail <c>CTerrainDesc</c> / <c>CSceneDesc</c>) plus the LandHeightTable.
/// </summary>
public sealed class SceneryRegion
{
    /// <summary>Terrain-type scene slot value meaning "no scene type" (retail stores a null CSceneType).</summary>
    public const uint NoSceneType = 0xFFFFFFFFu;

    private readonly uint[][] _terrainSceneTypes;
    private readonly uint[][] _sceneTypeScenes;

    /// <param name="landHeightTable">256 heights.</param>
    /// <param name="terrainSceneTypes">Per terrain type, the scene-type index of each scene slot.</param>
    /// <param name="sceneTypeScenes">Per scene type, its Scene (0x12) ids.</param>
    public SceneryRegion(IReadOnlyList<float> landHeightTable, IReadOnlyList<IReadOnlyList<uint>> terrainSceneTypes, IReadOnlyList<IReadOnlyList<uint>> sceneTypeScenes)
    {
        ArgumentNullException.ThrowIfNull(landHeightTable);
        ArgumentNullException.ThrowIfNull(terrainSceneTypes);
        ArgumentNullException.ThrowIfNull(sceneTypeScenes);
        if (landHeightTable.Count != 256)
        {
            throw new ArgumentException("LandHeightTable must have 256 entries.", nameof(landHeightTable));
        }

        LandHeightTable = landHeightTable.ToArray();
        _terrainSceneTypes = terrainSceneTypes.Select(s => s.ToArray()).ToArray();
        _sceneTypeScenes = sceneTypeScenes.Select(s => s.ToArray()).ToArray();

        // Retail CRegionDesc::UnPack resolves each slot to scene_types[index] with no bounds check, so an out-of-range
        // index would be undefined behaviour there. Refuse it instead of guessing.
        for (var t = 0; t < _terrainSceneTypes.Length; t++)
        {
            foreach (var index in _terrainSceneTypes[t])
            {
                if (index != NoSceneType && index >= _sceneTypeScenes.Length)
                {
                    throw new InvalidDataException(
                        $"Terrain type {t} references scene type {index}, but the Region has only {_sceneTypeScenes.Length}.");
                }
            }
        }
    }

    public IReadOnlyList<float> LandHeightTable { get; }

    public int TerrainTypeCount => _terrainSceneTypes.Length;

    public int SceneTypeCount => _sceneTypeScenes.Length;

    /// <summary>Retail <c>CTerrainDesc::NumSceneType</c>: scene slots of a terrain type (0 if unknown).</summary>
    public int NumSceneTypes(int terrainType) =>
        (uint)terrainType < (uint)_terrainSceneTypes.Length ? _terrainSceneTypes[terrainType].Length : 0;

    /// <summary>The scene-type index of a slot, or <see cref="NoSceneType"/>.</summary>
    public uint SceneTypeOf(int terrainType, int slot) => _terrainSceneTypes[terrainType][slot];

    /// <summary>The scene ids of a scene type.</summary>
    public IReadOnlyList<uint> ScenesOf(uint sceneType) => _sceneTypeScenes[sceneType];

    /// <summary>Retail <c>CTerrainDesc::SceneCount</c>: scenes reachable from a terrain type's slot (0 if none).</summary>
    public int SceneCount(int terrainType, int slot)
    {
        if (slot < 0 || slot >= NumSceneTypes(terrainType))
        {
            return 0;
        }

        var sceneType = SceneTypeOf(terrainType, slot);
        return sceneType == NoSceneType ? 0 : _sceneTypeScenes[sceneType].Length;
    }
}

/// <summary>
/// Everything scenery placement reads from the dats. Implementations must be safe for concurrent use;
/// see <see cref="DatSceneryDataSource"/>.
/// </summary>
public interface ISceneryDataSource
{
    /// <summary>Region scenery tables and the LandHeightTable.</summary>
    SceneryRegion Region { get; }

    /// <summary>The landblock's terrain and buildings, or null when the LandBlock record does not exist.</summary>
    SceneryLandblock? GetLandblock(LandblockCoord coord);

    /// <summary>A Scene record, or null when it does not exist (retail then skips the vertex).</summary>
    SceneDescription? GetScene(uint sceneId);

    /// <summary>
    /// The object's physics bounds, or null when retail <c>CPhysicsObj::makeObject</c> would fail for this id
    /// (missing record, missing part, Setup without parts, unsupported id type).
    /// </summary>
    SceneryObjectBounds? GetObjectBounds(uint objectId);
}
