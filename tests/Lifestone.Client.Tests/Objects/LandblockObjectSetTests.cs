using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Objects;

/// <summary>Which objects retail creates for a landblock and where their parts go, on the user's dats.</summary>
public sealed class LandblockObjectSetTests(ObjectsFixture fixture) : IClassFixture<ObjectsFixture>
{
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_creates_every_LandBlockInfo_object_at_its_stored_landblock_local_frame()
    {
        var coord = LandblockCoord.Holtburg;
        var statics = LandblockStatics.Read(fixture.Dats, coord)!;
        var set = fixture.Build(coord);

        Assert.Equal(114, statics.Objects.Count);
        Assert.Equal(12, statics.Buildings.Count);
        Assert.Equal(statics.Objects.Count, set.Count(WorldObjectKind.Static));
        Assert.Equal(statics.Buildings.Count, set.Count(WorldObjectKind.Building));
        Assert.Equal(0, set.Count(WorldObjectKind.Scenery)); // the town's land cells hold buildings or roads
        Assert.Empty(set.Rejected);

        // Statics first, then buildings, each in LandBlockInfo order, at their stored frames.
        var stored = statics.Objects.Select(o => (o.ModelId, o.Frame)).Concat(statics.Buildings.Select(b => (b.ModelId, b.Frame))).ToArray();
        for (var i = 0; i < stored.Length; i++)
        {
            var obj = set.Objects[i];
            Assert.Equal(stored[i].ModelId, obj.ModelId);
            Assert.Equal(stored[i].Frame.Origin, obj.Origin);
            Assert.True(Math.Abs(Quaternion.Dot(Quaternion.Normalize(stored[i].Frame.Orientation), obj.Orientation)) > 0.99999f);
            Assert.Equal(1f, obj.Scale);
        }

        // The frames are landblock-local, not cell-local: at its (x, y) every static is above the exact terrain by at most
        // a roof's height (lamps on walls, chimney-smoke emitters at 8-12.5 m), never buried, and many stand exactly on
        // it; their x and y span the whole landblock.
        var sampler = new TerrainSampler(fixture.Dats.GetLandblockTerrain(coord)!, fixture.Dats.TerrainRegion.LandHeightTable);
        var heights = statics.Objects.Select(o => o.Frame.Origin.Z - sampler.GetHeight(o.Frame.Origin.X, o.Frame.Origin.Y)).ToArray();
        Assert.All(heights, dz => Assert.InRange(dz, -0.05f, 13f));
        Assert.True(heights.Count(dz => MathF.Abs(dz) < 0.05f) >= 20, string.Join(" ", heights));
        Assert.Contains(statics.Objects, o => o.Frame.Origin.X > 150f);
        Assert.Contains(statics.Objects, o => o.Frame.Origin.Y > 150f);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Buildings_draw_with_the_building_rules_and_everything_else_as_objects()
    {
        var set = fixture.Build(LandblockCoord.Holtburg);
        foreach (var obj in set.Objects)
        {
            var expected = obj.Kind == WorldObjectKind.Building ? MeshDrawContext.Building : MeshDrawContext.Object;
            for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
            {
                Assert.All(set.LevelMeshes(p).ToArray().Where(m => m >= 0), m => Assert.Equal(expected, set.Meshes[m].Context));
            }
        }

        // Holtburg's 0x01000C1E: four levels, the last draws nothing.
        var building = set.Objects.Single(o => o.ModelId == 0x01000C1Eu);
        var part = set.Parts[building.FirstPart];
        Assert.Equal(new uint[] { 0x01000C1E, 0x01000C26, 0x01000C25, 0 }, part.Chain.GfxObjIds);
        Assert.Equal(-1, set.LevelMesh(building.FirstPart, 3));
        Assert.Equal("gfxobj:0x01000C1E:Building", set.Meshes[set.LevelMesh(building.FirstPart, 0)].CacheKey);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Part_frames_compose_the_object_frame_the_rest_pose_and_the_object_scale()
    {
        // Glenden Wood: multi-part tree Setups with scenery scales and a few statics.
        var set = fixture.Build(new LandblockCoord(0xA1, 0xA4));
        Assert.True(set.Count(WorldObjectKind.Scenery) > 20);
        var probe = new Vector3(0.7f, -1.3f, 2.1f);
        foreach (var obj in set.Objects)
        {
            var assembly = ModelAssembly.Load(fixture.Dats, obj.ModelId)!;
            var poses = assembly.ComputeRestPose(new Vector3(obj.Scale));
            Assert.Equal(poses.Length, obj.PartCount);
            var objectMatrix = Matrix4x4.CreateFromQuaternion(obj.Orientation) * Matrix4x4.CreateTranslation(obj.Origin);
            for (var i = 0; i < obj.PartCount; i++)
            {
                var part = set.Parts[obj.FirstPart + i];
                var expected = Vector3.Transform(probe, poses[i].ToMatrix() * objectMatrix);
                var actual = part.Origin + Vector3.Transform(probe * part.Scale, part.Rotation);
                Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"0x{obj.ModelId:X8} part {i}: {expected} vs {actual}");
                Assert.Equal(poses[i].Scale, part.Scale);
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Scenery_is_the_generator_output_with_normalised_orientations()
    {
        var coord = new LandblockCoord(0xA9, 0xB3);
        var placements = fixture.Scenery.Generate(coord).Placements;
        var set = fixture.Build(coord);
        var scenery = set.Objects.Where(o => o.Kind == WorldObjectKind.Scenery).ToArray();
        Assert.Equal(placements.Count, scenery.Length);
        for (var i = 0; i < scenery.Length; i++)
        {
            Assert.Equal(placements[i].ObjectId, scenery[i].ModelId);
            Assert.Equal(placements[i].Origin, scenery[i].Origin);
            Assert.Equal(placements[i].Scale, scenery[i].Scale);
            Assert.InRange(scenery[i].Orientation.Length(), 0.99999f, 1.00001f);
        }
    }

    [Fact]
    public void Origins_outside_the_landblock_fail_get_landcell()
    {
        var coord = LandblockCoord.Holtburg;
        Assert.True(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(0f, 0f, 10f)));
        Assert.True(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(-0.0001f, 191.9f, 10f))); // snapped to 0 by adjust_to_outside
        Assert.True(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(191.99f, 100f, -50f)));
        Assert.False(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(192f, 100f, 10f)));
        Assert.False(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(-0.01f, 100f, 10f)));
        Assert.False(LandblockObjectSet.IsInOwnLandblock(coord, new Vector3(10f, 200f, 10f)));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_LandBlockInfo_object_in_the_world_is_created_with_loadable_parts()
    {
        var landblocks = CellDiscovery.LandblocksWithInfo(fixture.Dats);
        long statics = 0, buildings = 0, created = 0, parts = 0;
        foreach (var coord in landblocks)
        {
            var info = LandblockStatics.Read(fixture.Dats, coord)!;
            statics += info.Objects.Count;
            buildings += info.Buildings.Count;
            var set = LandblockObjectSet.Build(coord, info, null, fixture.Library, WorldObjectKinds.Statics | WorldObjectKinds.Buildings);
            Assert.Empty(set.Rejected);
            created += set.Objects.Count;
            parts += set.Parts.Count;
            var capacity = new int[set.Meshes.Count];
            for (var p = 0; p < set.Parts.Count; p++)
            {
                var part = set.Parts[p];
                var levels = set.LevelMeshes(p).ToArray();
                Assert.True(part.Chain.IsLoadable);
                Assert.Equal(part.Chain.GfxObjIds.Count, levels.Length);
                for (var l = 0; l < levels.Length; l++)
                {
                    Assert.Equal(part.Chain.GfxObjIds[l] == 0, levels[l] < 0);
                    if (levels[l] >= 0)
                    {
                        Assert.Equal(part.Chain.GfxObjIds[l], set.Meshes[levels[l]].GfxObjId);
                    }
                }

                foreach (var mesh in levels.Where(m => m >= 0).Distinct())
                {
                    capacity[mesh]++;
                }
            }

            Assert.Equal(capacity, set.MeshCapacity);
        }

        // End-of-retail: no LandBlockInfo object sits outside its landblock or fails to load.
        Assert.Equal(5_346, landblocks.Count);
        Assert.Equal(42_942, statics);
        Assert.Equal(6_979, buildings);
        Assert.Equal(statics + buildings, created);
        Assert.Equal(77_069, parts);
    }
}
