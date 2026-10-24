using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Animation;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.Tests.Textures;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS2: the portal-space records in the user's dats are what m2-log-in.md §4.2 describes, so wave 2 can draw the
/// tunnel with M1's object path and no guessing. Every record is reached through the UIASSET chain
/// (<see cref="PortalSpaceAssets"/>), not a hard-coded id; only the parts below the Setup are named.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class PortalSpaceAssetConformanceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HalfTorus = 0x0100080B;
    private const uint TranslucentSurface = 0x08000C31;
    private const uint OpaqueSurface = 0x08000C32;
    private const uint TunnelHum = 0x0A000316;

    private PortalSpaceAssets Assets => PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps);

    [RequiresDatsFact]
    public void The_tunnel_setup_has_two_half_torus_parts_and_no_defaults()
    {
        Assert.True(fixture.Dats.TryGetPortal<Setup>(Assets.TunnelSetupId, out var setup));

        Assert.Equal([HalfTorus, HalfTorus], setup.Parts.Select(p => p.DataId));
        Assert.Equal(0u, setup.DefaultAnimation.DataId);
        Assert.Equal(0u, setup.DefaultScript.DataId);
        Assert.Equal(0u, setup.DefaultMotionTable.DataId);
        Assert.Equal(0u, setup.DefaultSoundTable.DataId);
        Assert.Equal(0u, setup.DefaultScriptTable.DataId);
        Assert.Empty(setup.Lights);

        // M1's object path assembles it as it is.
        var assembly = ModelAssembly.Load(fixture.Dats, Assets.TunnelSetupId);
        Assert.NotNull(assembly);
        Assert.Equal([HalfTorus, HalfTorus], assembly.Parts.Select(p => p.GfxObjId));
    }

    [RequiresDatsFact]
    public void The_half_torus_has_352_vertices_360_polygons_and_two_surfaces()
    {
        var gfx = HalfTorusRecord();

        Assert.Equal(352, gfx.VertexArray.Vertices.Count);
        Assert.Equal(360, gfx.Polygons.Count);
        Assert.Equal([TranslucentSurface, OpaqueSurface], gfx.Surfaces.Select(s => s.DataId));
    }

    [RequiresDatsFact]
    public void The_half_torus_is_50_9_metres_round_and_its_tube_5_6_metres_wide()
    {
        // In the part's own coordinates the ring lies in the XY plane about the Z axis, on the y >= 0 side.
        var (ringRadius, tubeRadius) = TorusRadii(HalfTorusRecord());

        Assert.InRange(ringRadius, 50.8, 51.0);
        Assert.InRange(tubeRadius, 5.5, 5.7);
        Assert.All(HalfTorusRecord().VertexArray.Vertices.Values, v => Assert.True(v.Origin.Y >= -1e-3f, $"vertex {v.Origin} is outside the half"));
    }

    [RequiresDatsFact]
    public void The_translucent_surface_chain_matches_the_spec()
    {
        Assert.True(fixture.Dats.TryGetPortal<Surface>(TranslucentSurface, out var surface));
        Assert.Equal(SurfaceType.Base1Image | SurfaceType.Translucent, surface.Type);
        Assert.Equal(0.25f, surface.Translucency);
        Assert.Equal(1f, surface.Luminosity);
        Assert.Equal(0x05000E0Fu, surface.OrigTextureId.DataId);

        Assert.True(fixture.Dats.TryGetPortal<SurfaceTexture>(0x05000E0F, out var texture));
        Assert.Equal([0x06003F29u, 0x06003F2Au], texture.Textures.Select(t => t.DataId));

        // The portal's own copy is the 32x32 fallback; the 64x64 one lives in the high-res dat.
        Assert.True(fixture.Dats.TryGetPortal<RenderSurface>(0x06003F2A, out var low));
        Assert.Equal((32, 32, PixelFormat.PFID_INDEX16), (low.Width, low.Height, low.Format));
    }

    [RequiresHighResDatsFact]
    public void The_translucent_surface_s_high_res_image_is_64_by_64_index16()
    {
        Assert.True(fixture.Dats.TryGetHighResOrPortal<RenderSurface>(0x06003F29, out var image));
        Assert.Equal((64, 64, PixelFormat.PFID_INDEX16), (image.Width, image.Height, image.Format));
    }

    [RequiresDatsFact]
    public void The_opaque_surface_chain_matches_the_spec()
    {
        Assert.True(fixture.Dats.TryGetPortal<Surface>(OpaqueSurface, out var surface));
        Assert.Equal(SurfaceType.Base1Image, surface.Type);
        Assert.Equal(0f, surface.Translucency);
        Assert.Equal(0x050001B2u, surface.OrigTextureId.DataId);

        Assert.True(fixture.Dats.TryGetPortal<SurfaceTexture>(0x050001B2, out var texture));
        Assert.Equal([0x06004D8Cu], texture.Textures.Select(t => t.DataId));
        Assert.True(fixture.Dats.TryGetPortal<RenderSurface>(0x06004D8C, out var image));
        Assert.Equal((128, 128, PixelFormat.PFID_INDEX16), (image.Width, image.Height, image.Format));
    }

    [RequiresDatsFact]
    public void The_spin_has_120_frames_of_two_parts_and_no_root_motion()
    {
        var spin = Spin();

        Assert.Equal(Assets.TunnelAnimationId, spin.Id);
        Assert.Equal(120, spin.NumFrames);
        Assert.Equal(2, spin.NumParts);
        Assert.Null(spin.PosFrames);
    }

    [RequiresDatsFact]
    public void Both_parts_sit_at_0_0_51_and_turn_1_5_degrees_a_frame_about_minus_x()
    {
        var spin = Spin();
        for (var f = 0; f < spin.NumFrames; f++)
        {
            for (var p = 0; p < 2; p++)
            {
                var frame = spin.PartFrames[f].Frames[p];
                Assert.True(Vector3.Distance(new Vector3(0f, 0f, 51f), frame.Origin) < 1e-4f, $"frame {f} part {p} sits at {frame.Origin}");
                if (f + 1 < spin.NumFrames)
                {
                    var (degrees, axis) = Delta(frame.Orientation, spin.PartFrames[f + 1].Frames[p].Orientation);
                    Assert.True(Math.Abs(degrees - 1.5) < 0.005, $"frame {f} part {p} turns {degrees:F4} degrees");
                    Assert.True(Vector3.Dot(axis, -Vector3.UnitX) > 0.9999f, $"frame {f} part {p} turns about {axis}");
                }
            }
        }
    }

    [RequiresDatsFact]
    public void The_loop_turns_180_degrees_so_the_halves_swap_seamlessly()
    {
        // One more 1.5 degree step after the last frame puts each half where the other one starts.
        var spin = Spin();
        for (var p = 0; p < 2; p++)
        {
            var next = Hamilton(Quaternion.CreateFromAxisAngle(-Vector3.UnitX, 1.5f * MathF.PI / 180f), spin.PartFrames[^1].Frames[p].Orientation);
            var other = spin.PartFrames[0].Frames[1 - p].Orientation;
            Assert.True(Math.Abs(Quaternion.Dot(next, other)) > 0.99999f, $"part {p} after the last frame: {next}, part {1 - p} at frame 0: {other}");
        }
    }

    [RequiresDatsFact]
    public void The_only_hook_is_the_hum_on_frame_2()
    {
        var spin = Spin();
        var hooks = spin.PartFrames.SelectMany((frame, index) => frame.Hooks.Select(hook => (index, hook))).ToArray();

        var (frameIndex, hook) = Assert.Single(hooks);
        Assert.Equal(2, frameIndex);
        var hum = Assert.IsType<SoundTweakedHook>(hook);
        Assert.Equal(new SoundTweakedHook(AnimHookDirection.Both, TunnelHum, Priority: 1f, Probability: 1f, Volume: 0.3f), hum);
    }

    [RequiresDatsFact]
    public void The_tunnel_camera_is_inside_the_tube_on_every_frame()
    {
        var (ringRadius, tubeRadius) = TorusRadii(HalfTorusRecord());
        var spin = Spin();
        for (var f = 0; f < spin.NumFrames; f++)
        {
            var inside = false;
            for (var p = 0; p < 2; p++)
            {
                var frame = spin.PartFrames[f].Frames[p];
                var local = frame.GlobalToLocalVec(PortalSpaceScene.CameraPosition - frame.Origin);
                var fromCentreLine = Math.Sqrt(Math.Pow(Math.Sqrt((local.X * local.X) + (local.Y * local.Y)) - ringRadius, 2) + (local.Z * local.Z));
                inside |= local.Y >= 0f && fromCentreLine < tubeRadius;
            }

            Assert.True(inside, $"frame {f}: the camera at {PortalSpaceScene.CameraPosition} is not inside either half of the tube");
        }
    }

    private GfxObj HalfTorusRecord()
    {
        Assert.True(fixture.Dats.TryGetPortal<GfxObj>(HalfTorus, out var gfx));
        return gfx;
    }

    private AnimationData Spin()
    {
        var spin = new DatAnimationSource(fixture.Dats).GetAnimation(Assets.TunnelAnimationId);
        Assert.NotNull(spin);
        return spin;
    }

    /// <summary>The ring's centre-line radius and the tube's radius, from the vertices' distances to the local Z axis.</summary>
    private static (double Ring, double Tube) TorusRadii(GfxObj gfx)
    {
        var radial = gfx.VertexArray.Vertices.Values.Select(v => Math.Sqrt((v.Origin.X * v.Origin.X) + (v.Origin.Y * v.Origin.Y))).ToArray();
        var inner = radial.Min();
        var outer = radial.Max();
        return ((inner + outer) / 2.0, (outer - inner) / 2.0);
    }

    /// <summary>The rotation from <paramref name="from"/> to <paramref name="to"/> applied in the parent's space: to ⊗ from⁻¹.</summary>
    private static (double Degrees, Vector3 Axis) Delta(Quaternion from, Quaternion to)
    {
        var delta = Hamilton(to, Quaternion.Conjugate(from));
        if (delta.W < 0f)
        {
            delta = -delta;
        }

        var vector = new Vector3(delta.X, delta.Y, delta.Z);
        var half = Math.Atan2(vector.Length(), delta.W);
        return (2.0 * half * 180.0 / Math.PI, Vector3.Normalize(vector));
    }

    /// <summary>The Hamilton product a ⊗ b (retail's quaternion order, docs/research/rendering.md §1).</summary>
    private static Quaternion Hamilton(Quaternion a, Quaternion b) => new(
        (a.W * b.X) + (a.X * b.W) + (a.Y * b.Z) - (a.Z * b.Y),
        (a.W * b.Y) - (a.X * b.Z) + (a.Y * b.W) + (a.Z * b.X),
        (a.W * b.Z) + (a.X * b.Y) - (a.Y * b.X) + (a.Z * b.W),
        (a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z));
}
