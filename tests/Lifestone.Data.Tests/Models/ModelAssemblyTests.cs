using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Models;

/// <summary>
/// Setup assembly checks. Ids, flags and counts for real Setups were read independently with the python research
/// reader (session scratch <c>models/dump_setup.py</c>, a full byte-exact Setup parse over <c>acparse.py</c>). Decoded
/// geometry (frames, spheres, sizes, light values) is never written into the tests: it is re-read from the user's
/// dats at test time by the independent <see cref="RawModelReader"/>.
/// </summary>
public sealed class ModelAssemblyTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const float Tolerance = 1e-5f;

    [RequiresDatsFact]
    public void Humanoid_setup_0x02000001_matches_independent_decode()
    {
        var assembly = ModelAssembly.Load(fixture.Dats, 0x02000001);
        Assert.NotNull(assembly);
        Assert.False(assembly.IsSimpleGfxObjSetup);
        Assert.Equal(ModelAssemblyFlags.HasParent | ModelAssemblyFlags.AllowFreeHeading, assembly.Flags);

        uint[] bodyParts =
        [
            0x0100004E, 0x0100004F, 0x0100004D, 0x0100004C, 0x0100004B, 0x01000053, 0x01000051, 0x01000050, 0x01000052,
            0x01000054, 0x01000055, 0x01000056, 0x01000058, 0x01000057, 0x01000059, 0x0100005B, 0x0100005A,
        ];
        Assert.Equal(34, assembly.Parts.Count);
        Assert.Equal(bodyParts, assembly.Parts.Take(17).Select(p => p.GfxObjId));
        Assert.All(assembly.Parts.Skip(17), p => Assert.Equal(0x010001ECu, p.GfxObjId));
        Assert.Equal(
            new[] { -1, -1, 1, 2, 3, -1, 5, 6, 7, 0, 9, 10, 11, 12, 13, 14, 15, 0, 17, 18, 19, 16, 16, 9, 9, 2, 6, 10, 13, 9, 29, 30, 31, 32 },
            assembly.Parts.Select(p => p.ParentIndex));
        Assert.All(assembly.Parts, p => Assert.Equal(Vector3.One, p.DefaultScale));

        var raw = RawSetup(0x02000001);
        AssertSizesAndSpheresMatch(raw, assembly);
        Assert.Empty(assembly.CylSpheres);
        Assert.Equal(2, assembly.Spheres.Count);
        Assert.Empty(assembly.Lights);
        Assert.Equal(0u, assembly.DefaultAnimationId | assembly.DefaultScriptId | assembly.DefaultMotionTableId | assembly.DefaultSoundTableId | assembly.DefaultScriptTableId);

        Assert.Equal(
            new Dictionary<int, int> { [0] = 9, [8] = 12, [4] = 0, [9] = 12, [5] = 9, [1] = 15, [6] = 9, [2] = 12, [3] = 11 },
            assembly.HoldingLocations.ToDictionary(kv => kv.Key, kv => kv.Value.PartIndex));
        Assert.Empty(assembly.ConnectionPoints);

        // Only a Default placement: Resting falls back to it (CPartArray::SetPlacementFrame).
        Assert.Equal(new uint[] { PlacementIds.Default }, assembly.PlacementFrames.Keys);
        var placement = assembly.ResolvePlacement();
        Assert.NotNull(placement);
        Assert.Equal(PlacementIds.Default, placement.PlacementId);

        // At unit scale the rest pose is the Default placement's frames, unchanged.
        var pose = assembly.ComputeRestPose();
        var frames = raw.Placements.Single(p => p.Key == PlacementIds.Default).Frames;
        Assert.Equal(frames.Select(f => new PartPose(f.Origin, f.Orientation, Vector3.One)), pose);

        // The root part faces backwards: its frame is a 180-degree turn about +Z.
        AssertVector(-Vector3.UnitX, Vector3.Transform(Vector3.UnitX, pose[0].Orientation));
        AssertVector(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, pose[0].Orientation));
    }

    [RequiresDatsFact]
    public void Flat_part_frames_stand_the_humanoid_and_the_tree_on_the_ground_at_their_setup_height()
    {
        // If part frames were relative to their ParentIndex part, the composed bodies would be scattered; composed
        // flat (retail Frame::combine with the object frame), their geometry spans z = 0 .. Setup.Height.
        var humanHeight = RawSetup(0x02000001).Height;
        var treeHeight = RawSetup(0x02000258).Height;
        var (humanMin, humanMax) = AssembledZRange(0x02000001, hierarchical: false);
        var (treeMin, treeMax) = AssembledZRange(0x02000258, hierarchical: false);
        var (chainedMin, chainedMax) = AssembledZRange(0x02000001, hierarchical: true);
        output.WriteLine($"human z {humanMin}..{humanMax} (height {humanHeight}), tree z {treeMin}..{treeMax} (height {treeHeight}), human chained by ParentIndex z {chainedMin}..{chainedMax}");

        // Chaining frames through ParentIndex instead does not produce a figure of the Setup's height standing on the ground.
        Assert.False(chainedMin > -0.12f && chainedMax < humanHeight + 0.12f && chainedMax > humanHeight - 0.12f);

        Assert.InRange(humanMin, -0.12f, 0.12f);
        Assert.InRange(humanMax, humanHeight - 0.12f, humanHeight + 0.12f);
        Assert.InRange(treeMin, -2f, 0f);
        Assert.InRange(treeMax, treeHeight - 2f, treeHeight + 0.5f);
    }

    [RequiresDatsFact]
    public void Tree_setup_0x02000258_matches_independent_decode()
    {
        var assembly = ModelAssembly.Load(fixture.Dats, 0x02000258)!;
        Assert.Equal(new uint[] { 0x010037A2, 0x010037A1, 0x0100379F }, assembly.Parts.Select(p => p.GfxObjId));
        Assert.Equal(ModelAssemblyFlags.HasParent | ModelAssemblyFlags.AllowFreeHeading, assembly.Flags);
        var raw = RawSetup(0x02000258);
        AssertSizesAndSpheresMatch(raw, assembly);
        var cyl = Assert.Single(assembly.CylSpheres);
        Assert.Equal(assembly.Height, cyl.Height, 1e-4f); // the trunk cylinder spans the whole tree
        Assert.All(assembly.ComputeRestPose(), p => Assert.Equal(new PartPose(Vector3.Zero, Quaternion.Identity, Vector3.One), p));
    }

    [RequiresDatsFact]
    public void Lit_setups_read_48_byte_lights_and_the_trailing_default_ids()
    {
        // 0x0200034D is one of the earlier prototype's misread examples: its DefaultScript was read 4 bytes early.
        var lamp = ModelAssembly.Load(fixture.Dats, 0x0200034D)!;
        Assert.Equal(ModelAssemblyFlags.AllowFreeHeading | ModelAssemblyFlags.HasPhysicsBsp, lamp.Flags);
        Assert.Equal(-1, Assert.Single(lamp.Parts).ParentIndex);
        Assert.Equal(0x01000E52u, lamp.Parts[0].GfxObjId);
        var light = Assert.Single(lamp.Lights);
        Assert.Equal(0, light.Type);
        AssertLightMatches(Assert.Single(RawSetup(0x0200034D).Lights), light);
        Assert.Equal(0xCDCDCDCDu, BitConverter.SingleToUInt32Bits(light.ConeAngle));
        Assert.Equal(0x33000240u, lamp.DefaultScriptId);
        Assert.Equal(0u, lamp.DefaultAnimationId);

        var brazier = ModelAssembly.Load(fixture.Dats, 0x02000081)!;
        var rawBrazier = RawSetup(0x02000081);
        Assert.Equal(0x330006E0u, brazier.DefaultScriptId);
        AssertLightMatches(Assert.Single(rawBrazier.Lights), Assert.Single(brazier.Lights));
        Assert.Equal(rawBrazier.Height, brazier.Height);

        // Placements 0x0, 0x79 and 0x65: the rest pose is Resting (0x65).
        Assert.Equal(new uint[] { 0x00, 0x65, 0x79 }, brazier.PlacementFrames.Keys.Order());
        Assert.Equal(PlacementIds.Resting, brazier.ResolvePlacement()!.PlacementId);
        Assert.Equal(0x79u, brazier.ResolvePlacement(0x79)!.PlacementId);
        Assert.Equal(PlacementIds.Default, brazier.ResolvePlacement(0x7A)!.PlacementId);
    }

    [RequiresDatsFact]
    public void Bare_gfxobj_becomes_a_simple_setup()
    {
        var assembly = ModelAssembly.Load(fixture.Dats, 0x01000C1E)!;
        var mesh = GfxObjMeshBuilder.Load(fixture.Dats, 0x01000C1E)!;
        Assert.True(assembly.IsSimpleGfxObjSetup);
        Assert.Equal(0x01000C1Eu, Assert.Single(assembly.Parts).GfxObjId);
        Assert.NotNull(mesh.PhysicsSphere);
        Assert.Equal(mesh.PhysicsSphere, assembly.SortingSphere);

        // Without a physics BSP the drawing sphere is used (0x010008AA has drawing data only).
        var drawingOnly = ModelAssembly.Load(fixture.Dats, 0x010008AA)!;
        Assert.Equal(GfxObjMeshBuilder.Load(fixture.Dats, 0x010008AA)!.DrawingSphere, drawingOnly.SortingSphere);
        Assert.Equal(ModelAssemblyFlags.AllowFreeHeading, assembly.Flags);
        Assert.Equal(new PartPose(Vector3.Zero, Quaternion.Identity, Vector3.One), Assert.Single(assembly.ComputeRestPose()));
        Assert.Empty(assembly.FindUnloadableParts(fixture.Dats));
    }

    [RequiresDatsFact]
    public void Setup_with_a_missing_part_cannot_be_created()
    {
        var assembly = ModelAssembly.Load(fixture.Dats, 0x02001C4F)!;
        Assert.Equal(22, assembly.Parts.Count);
        var failing = assembly.FindUnloadableParts(fixture.Dats);
        Assert.Equal(21, failing.Count);
        Assert.Equal(new uint[] { 0x00000000, 0x01004E29 }, failing.Select(p => p.GfxObjId).Distinct().Order());
        Assert.Empty(ModelAssembly.Load(fixture.Dats, 0x02000001)!.FindUnloadableParts(fixture.Dats));
        Assert.Null(ModelAssembly.Load(fixture.Dats, 0x0200FFFF));
    }

    [Fact]
    public void Pose_applies_object_scale_to_origins_and_default_scale_times_object_scale_to_meshes()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var setup = new Setup
        {
            Flags = SetupFlags.HasDefaultScale,
            Parts = [0x01000001, 0x01000002],
            DefaultScale = [new Vector3(1f, 2f, 3f), Vector3.One],
        };
        var placement = new AnimationFrame(2);
        placement.Frames.Add(new Frame { Origin = new Vector3(1f, 2f, 3f), Orientation = rotation });
        placement.Frames.Add(new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity });
        setup.PlacementFrames.Add(Placement.Default, placement);

        var assembly = ModelAssembly.FromSetup(0x02000001, setup);
        var pose = assembly.ComputeRestPose(new Vector3(2f));
        Assert.Equal(new Vector3(2f, 4f, 6f), pose[0].Origin);
        Assert.Equal(rotation, pose[0].Orientation);
        Assert.Equal(new Vector3(2f, 4f, 6f), pose[0].Scale);
        Assert.Equal(new Vector3(2f), pose[1].Scale);

        // Retail Frame::combine with an object frame: origin = obj.origin + R(obj.q)(f.origin * s), q = obj.q * f.q.
        var objectFrame = new PartFrame(new Vector3(100f, 200f, 5f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f));
        var local = new Vector3(0.5f, -0.25f, 1f);
        var viaMatrices = Vector3.Transform(
            local,
            pose[0].ToMatrix() * Matrix4x4.CreateFromQuaternion(objectFrame.Orientation) * Matrix4x4.CreateTranslation(objectFrame.Origin));
        var partOrigin = objectFrame.Origin + Vector3.Transform(new Vector3(1f, 2f, 3f) * 2f, objectFrame.Orientation);
        var partOrientation = objectFrame.Orientation * rotation;
        var retail = partOrigin + Vector3.Transform(local * new Vector3(2f, 4f, 6f), partOrientation);
        AssertVector(retail, viaMatrices, 1e-4f);
        AssertVector(retail, objectFrame.TransformPoint(pose[0].TransformPoint(local)), 1e-4f);
    }

    [Fact]
    public void Missing_placements_fail_loudly_and_unknown_parts_are_flagged()
    {
        var setup = new Setup { Parts = [0x02000005] };
        var assembly = ModelAssembly.FromSetup(0x02000009, setup);
        Assert.Null(assembly.ResolvePlacement());
        Assert.Throws<InvalidDataException>(() => assembly.ComputeRestPose());
        Assert.False(assembly.Parts[0].IsGfxObj);
    }

    private (float Min, float Max) AssembledZRange(uint setupId, bool hierarchical)
    {
        var assembly = ModelAssembly.Load(fixture.Dats, setupId)!;
        var pose = assembly.ComputeRestPose();
        if (hierarchical)
        {
            // The rejected alternative: each frame relative to its ParentIndex part (parents precede children here).
            for (var i = 0; i < pose.Length; i++)
            {
                var parent = assembly.Parts[i].ParentIndex;
                if (parent >= 0)
                {
                    Assert.True(parent < i);
                    pose[i] = new PartPose(
                        pose[parent].Origin + Vector3.Transform(pose[i].Origin, pose[parent].Orientation),
                        pose[parent].Orientation * pose[i].Orientation,
                        pose[i].Scale);
                }
            }
        }

        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (var i = 0; i < assembly.Parts.Count; i++)
        {
            // Draw what retail draws at distance 0: the anchor parts (0x010001EC) select their empty level.
            var (_, gfxObjId, _) = LodChain.Load(fixture.Dats, assembly.Parts[i].GfxObjId).Select(0f);
            if (gfxObjId == 0)
            {
                continue;
            }

            var mesh = GfxObjMeshBuilder.Load(fixture.Dats, gfxObjId)!;
            foreach (var p in mesh.Mesh.SubMeshes.SelectMany(s => s.Positions))
            {
                var z = pose[i].TransformPoint(p).Z;
                minZ = MathF.Min(minZ, z);
                maxZ = MathF.Max(maxZ, z);
            }
        }

        return (minZ, maxZ);
    }

    private RawModelReader.RawSetup RawSetup(uint id) => RawModelReader.ReadSetup(RawModelReader.PortalBytes(fixture.Dats, id));

    private static void AssertSizesAndSpheresMatch(RawModelReader.RawSetup raw, ModelAssembly assembly)
    {
        Assert.Equal((raw.Height, raw.Radius, raw.StepUp, raw.StepDown), (assembly.Height, assembly.Radius, assembly.StepUpHeight, assembly.StepDownHeight));
        Assert.Equal(ModelsExhaustiveTests.ToSphere(raw.Sorting), assembly.SortingSphere);
        Assert.Equal(ModelsExhaustiveTests.ToSphere(raw.Selection), assembly.SelectionSphere);
        Assert.Equal(raw.Spheres.Select(ModelsExhaustiveTests.ToSphere), assembly.Spheres);
        Assert.Equal(raw.CylSpheres.Select(c => new ModelCylSphere(c.LowPoint, c.Radius, c.Height)), assembly.CylSpheres);
    }

    private static void AssertLightMatches(RawModelReader.RawLight raw, ModelLight light)
    {
        Assert.Equal(new PartFrame(raw.Frame.Origin, raw.Frame.Orientation), light.Offset);
        Assert.Equal(raw.Color, light.ColorArgb);
        Assert.Equal(new Vector3(((raw.Color >> 16) & 0xFF) / 255f, ((raw.Color >> 8) & 0xFF) / 255f, (raw.Color & 0xFF) / 255f), light.ColorRgb);
        Assert.Equal((raw.Intensity, raw.Falloff), (light.Intensity, light.Falloff));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}
