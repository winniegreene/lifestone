// Ported from ACEmulator/ACE@95ef426d (AGPL-3.0):
//   Source/ACE.Server/Physics/Common/Landblock.cs (get_land_scenes, OnRoad, GetRoad, init_buildings, get_landcell)
//   Source/ACE.Server/Physics/Common/LandblockStruct.cs (ConstructVertices, ConstructPolygons, AddPolygon)
//   Source/ACE.Server/Physics/Common/LandCell.cs (find_terrain_poly)
//   Source/ACE.Server/Physics/Common/LandDefs.cs (AdjustToOutside, get_outside_lcoord, InBlock)
//   Source/ACE.Server/Physics/Common/ObjectDesc.cs (Displace, ScaleObj, RotateObj, ObjAlign, CheckSlope)
//   Source/ACE.Server/Physics/Common/Vector.cs (NormalizeCheckSmall)
//   Source/ACE.Server/Physics/Polygon.cs (make_plane, point_in_poly2D)
//   Source/ACE.Server/Physics/PhysicsObj.cs (obj_within_block), PartArray.cs (CacheHasPhysicsBSP, GetSortingSphere),
//   Setup.cs (MakeSimpleSetup), Collision/GfxObj.cs (PhysicsSphere, DrawingSphere)
//   Source/ACE.Server/Physics/Animation/AFrame.cs (set_heading, set_vector_heading, LocalToGlobal)
//   Source/ACE.Server/Physics/Extensions/{PlaneExtensions.cs (set_height), Vector3Extensions.cs (get_heading),
//   FloatExtensions.cs (ToRadians, ToDegrees)}
// with changes: flattened into one test-only oracle that reads the dats through DatReaderWriter; physics-object
// creation reduced to what obj_within_block reads. ACE's float math and its double 2.3283064e-10 constant are kept
// on purpose; UseRetailUnitScale swaps in the retail 2^-32 so the two scales can be compared.

using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>One ACE scenery candidate (noise &lt; Freq and WeenieObj == 0) and where ACE stopped it.</summary>
internal readonly record struct AceScenery(
    int VertexX,
    int VertexY,
    uint SceneId,
    int ObjectIndex,
    uint ObjectId,
    SceneryStage Stage,
    Vector3 Origin,
    Quaternion Orientation,
    float Scale);

/// <summary>
/// Independent reference: ACE's <c>Landblock.get_land_scenes</c> and helpers, transcribed with ACE's own arithmetic.
/// </summary>
internal sealed class AceSceneryOracle(DatSet dats, bool useRetailUnitScale = false)
{
    private const float Epsilon = 0.0002f;      // PhysicsGlobals.EPSILON
    private const int CellLength = 24;          // LandDefs.CellLength
    private const int BlockLength = 192;        // LandDefs.BlockLength
    private const float RoadWidth = 5.0f;       // Landblock.RoadWidth
    private const float TileLength = 24.0f;     // Landblock.TileLength

    private readonly double _unit = useRetailUnitScale ? 1.0 / 4294967296.0 : 2.3283064e-10;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, Scene> _scenes = new(); // concurrent: the sweeps run the oracle in parallel (C2)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, AceObject> _objects = new();

    private sealed record AceObject(bool HasPhysicsBsp, Vector3 SortCenter, float SortRadius, (Vector3 Low, float Radius)[] CylSpheres, int NumSphere);

    private sealed class AcePolygon(Vector3[] vertices)
    {
        public Vector3[] Vertices { get; } = vertices;

        public Plane Plane { get; private set; }

        // Polygon.make_plane
        public void MakePlane()
        {
            var normal = Vector3.Zero;
            for (int i = Vertices.Length - 2, spreadIdx = 1; i > 0; i--)
            {
                var v1 = Vertices[spreadIdx++] - Vertices[0];
                var v2 = Vertices[spreadIdx] - Vertices[0];
                normal += Vector3.Cross(v1, v2);
            }

            normal = Vector3.Normalize(normal);
            var distSum = 0.0f;
            for (int i = Vertices.Length, spread = 0; i > 0; i--, spread++)
            {
                distSum += Vector3.Dot(normal, Vertices[spread]);
            }

            var dist = -(distSum / Vertices.Length);
            Plane = new Plane(normal, dist);
        }

        // Polygon.point_in_poly2D(point, Sidedness.Positive)
        public bool PointInPoly2D(Vector3 point)
        {
            var prevIdx = 0;
            for (var i = Vertices.Length - 1; i >= 0; i--)
            {
                var prevVertex = Vertices[prevIdx];
                var vertex = Vertices[i];
                var diff = vertex - prevVertex;
                var diffCross = (diff.Y * vertex.X) - (diff.X * vertex.Y) + (diff.X * point.Y) - (diff.Y * point.X);
                if (diffCross > 0.0f)
                {
                    return false;
                }

                prevIdx = i;
            }

            return true;
        }
    }

    /// <summary>ACE <c>Landblock.get_land_scenes</c> for one landblock; every candidate with its stage.</summary>
    public List<AceScenery> GetLandScenes(LandblockCoord coord)
    {
        var result = new List<AceScenery>();
        var landBlock = dats.GetLandBlock(coord);
        if (landBlock is null)
        {
            return result;
        }

        var region = dats.Region;
        var terrain = landBlock.Terrain.Select(t => (ushort)t).ToArray();
        var heights = landBlock.Height;
        var cellPolygons = ConstructPolygons(coord, heights, region.LandDefs.LandHeightTable);
        var buildingCells = InitBuildings(coord, landBlock);

        var id = coord.LandBlockId;
        var blockX = (id >> 24) * 8;
        var blockY = (id >> 16 & 0xFF) * 8;

        for (uint i = 0; i < terrain.Length; i++)
        {
            var t = terrain[(int)i];
            var terrainType = t >> 2 & 0x1F;
            var sceneType = t >> 11;
            var sceneInfo = (int)region.TerrainInfo.TerrainTypes[terrainType].SceneTypes[sceneType];
            var scenes = region.SceneInfo.SceneTypes[sceneInfo].Scenes;
            if (scenes.Count == 0)
            {
                continue;
            }

            var cellX = i / 9;
            var cellY = i % 9;
            var globalCellX = cellX + blockX;
            var globalCellY = cellY + blockY;

            var cellMat = globalCellY * (712977289 * globalCellX + 1813693831) - 1109124029 * globalCellX + 2139937281;
            var offset = cellMat * _unit;
            var sceneIdx = (int)(scenes.Count * offset);
            if (sceneIdx >= scenes.Count)
            {
                sceneIdx = 0;
            }

            var sceneId = scenes[sceneIdx].DataId;
            var scene = GetScene(sceneId);

            var cellXMat = -1109124029 * globalCellX;   // long, as in ACE
            var cellYMat = 1813693831 * globalCellY;
            cellMat = 1360117743 * globalCellX * globalCellY + 1888038839;

            for (uint j = 0; j < scene.Objects.Count; j++)
            {
                var obj = scene.Objects[(int)j];
                var noise = (uint)(cellXMat + cellYMat - cellMat * (23399 + j)) * _unit;
                if (!(noise < obj.Frequency && obj.WeenieObj == 0))
                {
                    continue;
                }

                AceScenery Reject(SceneryStage stage, Vector3 at) =>
                    new((int)cellX, (int)cellY, sceneId, (int)j, obj.ObjectId, stage, at, default, 0f);

                var position = Displace(obj, globalCellX, globalCellY, j);
                var lx = cellX * CellLength + position.X;
                var ly = cellY * CellLength + position.Y;
                var loc = new Vector3(lx, ly, position.Z);

                if (lx < 0 || ly < 0 || lx >= BlockLength || ly >= BlockLength)
                {
                    result.Add(Reject(SceneryStage.OutOfBlock, loc));
                    continue;
                }

                if (OnRoad(terrain, loc))
                {
                    result.Add(Reject(SceneryStage.OnRoad, loc));
                    continue;
                }

                // LandDefs.AdjustToOutside + get_landcell
                if (Math.Abs(loc.X) < Epsilon)
                {
                    loc.X = 0;
                }

                if (Math.Abs(loc.Y) < Epsilon)
                {
                    loc.Y = 0;
                }

                var cx = (int)(float)Math.Floor(loc.X / CellLength);
                var cy = (int)(float)Math.Floor(loc.Y / CellLength);
                var cellIndex = cx * 8 + cy;

                if (buildingCells.Contains(cellIndex))
                {
                    result.Add(Reject(SceneryStage.Building, loc));
                    continue;
                }

                AcePolygon? walkable = null;
                for (var p = 0; p < 2; p++)
                {
                    if (cellPolygons[cellIndex][p].PointInPoly2D(loc))
                    {
                        walkable = cellPolygons[cellIndex][p];
                        break;
                    }
                }

                if (walkable is null)
                {
                    result.Add(Reject(SceneryStage.NoTerrainPolygon, loc));
                    continue;
                }

                if (!(walkable.Plane.Normal.Z >= obj.MinSlope && walkable.Plane.Normal.Z <= obj.MaxSlope))
                {
                    result.Add(Reject(SceneryStage.Slope, loc));
                    continue;
                }

                // PlaneExtensions.set_height
                if (!(Math.Abs(walkable.Plane.Normal.Z) <= Epsilon))
                {
                    loc.Z = -((loc.Y * walkable.Plane.Normal.Y + loc.X * walkable.Plane.Normal.X + walkable.Plane.D) / walkable.Plane.Normal.Z);
                }

                var orientation = obj.Align != 0
                    ? ObjAlign(obj, walkable.Plane)
                    : RotateObj(obj, globalCellX, globalCellY, j);

                var physics = GetObject(obj.ObjectId);
                if (!ObjWithinBlock(physics, loc, orientation))
                {
                    result.Add(Reject(SceneryStage.NotWithinBlock, loc));
                    continue;
                }

                var scale = ScaleObj(obj, globalCellX, globalCellY, j);
                result.Add(new AceScenery((int)cellX, (int)cellY, sceneId, (int)j, obj.ObjectId, SceneryStage.Placed, loc, orientation, scale));
            }
        }

        return result;
    }

    // ObjectDesc.Displace
    private Vector3 Displace(DatReaderWriter.Types.ObjectDesc obj, uint ix, uint iy, uint iq)
    {
        float x;
        float y;
        var loc = obj.BaseLoc.Origin;

        if (obj.DisplaceX <= 0)
        {
            x = loc.X;
        }
        else
        {
            x = (float)((1813693831 * iy - (iq + 45773) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                * _unit * obj.DisplaceX + loc.X);
        }

        if (obj.DisplaceY <= 0)
        {
            y = loc.Y;
        }
        else
        {
            y = (float)((1813693831 * iy - (iq + 72719) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                * _unit * obj.DisplaceY + loc.Y);
        }

        var z = loc.Z;
        var quadrant = (1813693831 * iy - ix * (1870387557 * iy + 1109124029) - 402451965) * _unit;
        if (quadrant >= 0.75)
        {
            return new Vector3(y, -x, z);
        }

        if (quadrant >= 0.5)
        {
            return new Vector3(-x, -y, z);
        }

        return quadrant >= 0.25 ? new Vector3(-y, x, z) : new Vector3(x, y, z);
    }

    // ObjectDesc.ScaleObj
    private float ScaleObj(DatReaderWriter.Types.ObjectDesc obj, uint x, uint y, uint k)
    {
        var minScale = obj.MinScale;
        var maxScale = obj.MaxScale;
        return minScale == maxScale
            ? maxScale
            : (float)(Math.Pow(maxScale / minScale, (1813693831 * y - (k + 32593) * (1360117743 * y * x + 1888038839) - 1109124029 * x) * _unit) * minScale);
    }

    // ObjectDesc.RotateObj
    private Quaternion RotateObj(DatReaderWriter.Types.ObjectDesc obj, uint x, uint y, uint k)
    {
        var orientation = obj.BaseLoc.Orientation;
        if (obj.MaxRotation > 0.0f)
        {
            var degrees = (float)((1813693831 * y - (k + 63127) * (1360117743 * y * x + 1888038839) - 1109124029 * x) * _unit * obj.MaxRotation);
            orientation = SetHeading(orientation, degrees);
        }

        return orientation;
    }

    // ObjectDesc.ObjAlign
    private static Quaternion ObjAlign(DatReaderWriter.Types.ObjectDesc obj, Plane plane)
    {
        var negNormal = -plane.Normal;
        return SetHeading(obj.BaseLoc.Orientation, GetHeading(negNormal));
    }

    // AFrame.set_heading
    private static Quaternion SetHeading(Quaternion orientation, float degrees)
    {
        var rads = ToRadians(degrees);
        var matrix = Matrix4x4.CreateFromQuaternion(orientation);
        var heading = new Vector3((float)Math.Sin(rads), (float)Math.Cos(rads), matrix.M23 + matrix.M13);
        return SetVectorHeading(orientation, heading);
    }

    // AFrame.set_vector_heading
    private static Quaternion SetVectorHeading(Quaternion orientation, Vector3 heading)
    {
        var normal = heading;
        if (NormalizeCheckSmall(ref normal))
        {
            return orientation;
        }

        var zDeg = 450.0f - ToDegrees((float)Math.Atan2(normal.Y, normal.X));
        var zRot = -ToRadians(zDeg % 360.0f);
        var xRot = (float)Math.Asin(normal.Z);
        var rotate = Quaternion.CreateFromYawPitchRoll(xRot, 0, zRot);
        return Quaternion.Normalize(rotate);
    }

    // Vector3Extensions.get_heading
    private static float GetHeading(Vector3 v)
    {
        var normal = new Vector3(v.X, v.Y, 0);
        return NormalizeCheckSmall(ref normal) ? 0.0f : (450.0f - ToDegrees((float)Math.Atan2(normal.Y, normal.X))) % 360.0f;
    }

    // Vec.NormalizeCheckSmall
    private static bool NormalizeCheckSmall(ref Vector3 v)
    {
        var dist = v.Length();
        if (dist < Epsilon)
        {
            return true;
        }

        v *= 1.0f / dist;
        return false;
    }

    private static float ToRadians(float angle) => (float)(Math.PI / 180.0f * angle);

    private static float ToDegrees(float rads) => (float)(180.0f / Math.PI * rads);

    // PhysicsObj.obj_within_block (Position.Frame.LocalToGlobal = Origin + Vector3.Transform(p, Orientation))
    private static bool ObjWithinBlock(AceObject obj, Vector3 origin, Quaternion orientation)
    {
        var globCenter = origin + Vector3.Transform(obj.SortCenter, orientation);
        if (obj.HasPhysicsBsp)
        {
            if (globCenter.X >= obj.SortRadius && globCenter.Y >= obj.SortRadius)
            {
                var blockRadius = BlockLength - obj.SortRadius;
                if (globCenter.X < blockRadius)
                {
                    return !(globCenter.Y >= blockRadius);
                }
            }

            return false;
        }

        if (obj.CylSpheres.Length > 0)
        {
            foreach (var cylSphere in obj.CylSpheres)
            {
                globCenter = origin + Vector3.Transform(cylSphere.Low, orientation);
                if (globCenter.X < cylSphere.Radius || globCenter.Y < cylSphere.Radius)
                {
                    return false;
                }

                var blockRadius = BlockLength - cylSphere.Radius;
                if (globCenter.X >= blockRadius || globCenter.Y >= blockRadius)
                {
                    return false;
                }
            }

            return true;
        }

        return obj.NumSphere > 0 ? InBlock(globCenter, obj.SortRadius) : InBlock(origin, 0.0f);
    }

    // LandDefs.InBlock
    private static bool InBlock(Vector3 pos, float radius)
    {
        if (pos.X < radius || pos.Y < radius)
        {
            return false;
        }

        var blockRadius = BlockLength - radius;
        return pos.X < blockRadius && pos.Y < blockRadius;
    }

    // Landblock.OnRoad
    private static bool OnRoad(ushort[] terrain, Vector3 obj)
    {
        var x = (int)(obj.X / TileLength);
        var y = (int)(obj.Y / TileLength);
        var rMin = RoadWidth;
        var rMax = TileLength - RoadWidth;
        uint GetRoad(int gx, int gy) => (uint)(terrain[gx * 9 + gy] & 3);
        var r0 = GetRoad(x, y);
        var r1 = GetRoad(x, y + 1);
        var r2 = GetRoad(x + 1, y);
        var r3 = GetRoad(x + 1, y + 1);
        if (r0 == 0 && r1 == 0 && r2 == 0 && r3 == 0)
        {
            return false;
        }

        var dx = obj.X - x * TileLength;
        var dy = obj.Y - y * TileLength;
        if (r0 > 0)
        {
            if (r1 > 0)
            {
                if (r2 > 0)
                {
                    return r3 > 0 || dx < rMin || dy < rMin;
                }

                return r3 > 0 ? (dx < rMin || dy > rMax) : dx < rMin;
            }

            if (r2 > 0)
            {
                return r3 > 0 ? (dx > rMax || dy < rMin) : dy < rMin;
            }

            return r3 > 0 ? Math.Abs(dx - dy) < rMin : dx + dy < rMin;
        }

        if (r1 > 0)
        {
            if (r2 > 0)
            {
                return r3 > 0 ? (dx > rMax || dy > rMax) : Math.Abs(dx + dy - TileLength) < rMin;
            }

            return r3 > 0 ? dy > rMax : TileLength + dx - dy < rMin;
        }

        if (r2 > 0)
        {
            return r3 > 0 ? dx > rMax : TileLength - dx + dy < rMin;
        }

        return r3 > 0 && TileLength * 2f - dx - dy < rMin;
    }

    // LandblockStruct.ConstructVertices + ConstructPolygons + AddPolygon (VertexPerCell = 1); polygons per cell.
    private static AcePolygon[][] ConstructPolygons(LandblockCoord coord, byte[] heights, IReadOnlyList<float> landHeightTable)
    {
        var vertices = new Vector3[81];
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                vertices[x * 9 + y] = new Vector3(x * CellLength, y * CellLength, landHeightTable[heights[x * 9 + y]] * 1);
            }
        }

        AcePolygon Add(int v0, int v1, int v2)
        {
            var polygon = new AcePolygon([vertices[v0], vertices[v1], vertices[v2]]);
            polygon.MakePlane();
            return polygon;
        }

        var cells = new AcePolygon[64][];
        var lcoordX = (uint)(coord.X * 8);
        var lcoordY = coord.Y * 8;
        var seedA = lcoordX * 214614067u;
        var seedB = lcoordX * 1109124029u;
        for (var x = 0; x < 8; x++)
        {
            var idxJ = 0;
            for (var y = 0; y < 8; y++)
            {
                var magicB = seedB;
                var magicA = seedA + 1813693831u;
                var splitDir = (uint)((lcoordY + idxJ) * magicA - magicB - 1369149221u);
                var vertIdx = x * 9 + idxJ;
                var nextVertIdx = (x + 1) * 9 + idxJ;
                cells[x * 8 + y] = splitDir * 2.3283064e-10 < 0.5f
                    ? [Add(vertIdx, nextVertIdx, vertIdx + 1), Add(nextVertIdx + 1, vertIdx + 1, nextVertIdx)]
                    : [Add(vertIdx, nextVertIdx, nextVertIdx + 1), Add(vertIdx, nextVertIdx + 1, vertIdx + 1)];
                idxJ++;
            }

            seedB += 1109124029u;
            seedA += 214614067u;
        }

        return cells;
    }

    // Landblock.init_buildings: the cell (after AdjustToOutside) of every building frame inside this landblock.
    private HashSet<int> InitBuildings(LandblockCoord coord, LandBlock landBlock)
    {
        var cells = new HashSet<int>();
        if (!landBlock.HasObjects || !dats.TryGetCell<LandBlockInfo>(coord.LandBlockInfoId, out var info))
        {
            return cells;
        }

        foreach (var building in info.Buildings)
        {
            var origin = building.Frame.Origin;
            if (Math.Abs(origin.X) < Epsilon)
            {
                origin.X = 0;
            }

            if (Math.Abs(origin.Y) < Epsilon)
            {
                origin.Y = 0;
            }

            var cx = (int)(float)Math.Floor(origin.X / CellLength);
            var cy = (int)(float)Math.Floor(origin.Y / CellLength);
            if (cx is >= 0 and < 8 && cy is >= 0 and < 8)
            {
                cells.Add(cx * 8 + cy);
            }
        }

        return cells;
    }

    private Scene GetScene(uint id)
    {
        if (!_scenes.TryGetValue(id, out var scene))
        {
            scene = dats.TryGetPortal<Scene>(id, out var s) ? s : throw new InvalidDataException($"Scene 0x{id:X8} missing");
            _scenes[id] = scene;
        }

        return scene;
    }

    // PhysicsObj.makeObject -> PartArray.CreateMesh (Setup.MakeSimpleSetup) / CreateSetup; GfxObj physics from
    // Collision/GfxObj.cs (PhysicsBSP only when PhysicsPolygons.Count > 0; degrades omitted, as in ACE PhysicsPart).
    private AceObject GetObject(uint id)
    {
        if (_objects.TryGetValue(id, out var obj))
        {
            return obj;
        }

        if (id >> 24 == 0x01)
        {
            var gfx = dats.TryGetPortal<GfxObj>(id, out var g) ? g : throw new InvalidDataException($"GfxObj 0x{id:X8} missing");
            var hasPhysics = gfx.PhysicsPolygons.Count > 0;
            var sphere = hasPhysics ? gfx.PhysicsBSP!.Root!.BoundingSphere : gfx.DrawingBSP!.Root!.BoundingSphere;
            obj = new AceObject(hasPhysics, sphere.Origin, sphere.Radius, [], 0);
        }
        else
        {
            var setup = dats.TryGetPortal<Setup>(id, out var s) ? s : throw new InvalidDataException($"Setup 0x{id:X8} missing");
            var hasPhysics = setup.Parts.Any(p => dats.TryGetPortal<GfxObj>(p.DataId, out var part) && part.PhysicsPolygons.Count > 0);
            obj = new AceObject(
                hasPhysics,
                setup.SortingSphere.Origin,
                setup.SortingSphere.Radius,
                setup.CylSpheres.Select(c => (c.Origin, c.Radius)).ToArray(),
                setup.Spheres.Count);
        }

        _objects[id] = obj;
        return obj;
    }
}
