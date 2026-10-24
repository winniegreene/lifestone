using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Animation;
using Lifestone.Data.Geometry;

namespace Lifestone.Client.Tests.Effects;

/// <summary>Instance packing, translucency rules and draw batching of particles (engine-agnostic, synthetic data).</summary>
public sealed class ParticlePackingTests
{
    [Fact]
    public void Instance_layout_is_row_major_3x4_then_custom()
    {
        var buffer = new InstanceBuffer();
        Assert.True(buffer.Reserve(1));
        var rows = AffineRows.FromColumns(new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12));
        buffer.Add(rows, InstanceBuffer.ParticleCustom(0.25f, partMaterial: true));
        Assert.Equal(
            new float[] { 1, 4, 7, 10, 2, 5, 8, 11, 3, 6, 9, 12, 0.25f, 1, 0, 0 },
            buffer.Data.AsSpan(0, InstanceBuffer.Stride).ToArray());
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void A_tag_rides_with_every_draw_of_its_object_into_the_instance_custom_z()
    {
        // The world's EnvCell statics tag their particles with their cell's draw-state slot (up to 16,384, exact as a float).
        var model = new ParticleModel(0x01000001, DrawFrameRulesTests.ParticleChain(64f), Vector3.Zero);
        var viewer = new Vector3(0, -10, 1.5f);
        var list = new ParticleDrawList();
        list.Add(new AcFrame(new Vector3(0, 0, 1), Quaternion.Identity), 1f, 0f, model, viewer, tag: 16383);
        list.Add(new AcFrame(new Vector3(1, 0, 1), Quaternion.Identity), 1f, 0.5f, model, viewer, tag: 9);
        list.Add(new AcFrame(new Vector3(2, 0, 1), Quaternion.Identity), 1f, 0f, model, viewer);
        Assert.Equal([16383, 0], list.Batches[0].Draws.Select(d => d.Tag));
        Assert.Equal([9], list.Batches[1].Draws.Select(d => d.Tag));
        Assert.Equal(new Vector4(0.5f, 1f, 16383f, 0f), InstanceBuffer.ParticleCustom(0.5f, partMaterial: true, tag: 16383));
        Assert.Equal(new Vector4(1f, 0f, 0f, 0f), InstanceBuffer.ParticleCustom(1f, partMaterial: false));
    }

    [Fact]
    public void Affine_rows_transform_points_like_the_columns_say()
    {
        var q = Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f);
        var scale = 2.5f;
        var origin = new Vector3(5, -1, 3);
        var rows = AffineRows.FromColumns(
            Vector3.Transform(Vector3.UnitX, q) * scale,
            Vector3.Transform(Vector3.UnitY, q) * scale,
            Vector3.Transform(Vector3.UnitZ, q) * scale,
            origin);
        var p = new Vector3(0.3f, -0.7f, 1.1f);
        DrawFrameRulesTests.AssertClose(origin + Vector3.Transform(p * scale, q), rows.TransformPoint(p));
        Assert.Equal(new Vector3(1, 2, 3), AffineRows.Identity.TransformPoint(new Vector3(1, 2, 3)));
    }

    [Fact]
    public void Capacity_grows_by_powers_of_two_and_never_shrinks()
    {
        Assert.Equal(16, InstanceBuffer.CapacityFor(0));
        Assert.Equal(16, InstanceBuffer.CapacityFor(16));
        Assert.Equal(32, InstanceBuffer.CapacityFor(17));
        Assert.Equal(1024, InstanceBuffer.CapacityFor(1000));

        var buffer = new InstanceBuffer();
        Assert.True(buffer.Reserve(10));
        Assert.Equal(16 * InstanceBuffer.Stride, buffer.Data.Length);
        Assert.False(buffer.Reserve(16));
        Assert.True(buffer.Reserve(40));
        Assert.Equal(64, buffer.Capacity);
        Assert.False(buffer.Reserve(3));
        Assert.Equal(64, buffer.Capacity);
        for (var i = 0; i < 64; i++)
        {
            buffer.Add(AffineRows.Identity, Vector4.Zero);
        }

        Assert.Throws<InvalidOperationException>(() => buffer.Add(AffineRows.Identity, Vector4.Zero));
    }

    [Theory]
    [InlineData(0f, false, false)]
    [InlineData(0.999f, false, true)]
    [InlineData(1f, true, false)]
    public void Translucency_hides_at_one_and_carries_a_part_material_in_between(float t, bool hidden, bool partMaterial)
    {
        Assert.Equal(hidden, PartTranslucencyRules.IsHidden(t));
        Assert.Equal(partMaterial, PartTranslucencyRules.HasPartMaterial(t));
        Assert.Equal(1f - t, PartTranslucencyRules.MaterialAlpha(t));
    }

    [Fact]
    public void A_part_material_keeps_blends_without_a_test_and_turns_the_rest_into_alpha_blending()
    {
        static SurfaceShaderVariant V(SurfaceBlend blend, bool test, bool z, bool fog) =>
            new(blend, test, z, SurfaceCull.Back, TextureAddressMode.Clamp, true, fog);

        // Additive, Alpha and Translucent surfaces (blending without a test): unchanged (D3DPolyRender::SetSurface).
        foreach (var keep in new[] { V(SurfaceBlend.AddSourceAlpha, false, false, false), V(SurfaceBlend.AddOne, false, false, false), V(SurfaceBlend.Mix, false, false, true) })
        {
            Assert.Equal(keep, PartTranslucencyRules.WithPartMaterial(keep));
        }

        // Opaque, cut-outs and alpha-tested blends: SRCALPHA/INVSRCALPHA, no test, no depth write; fog unchanged.
        Assert.Equal(V(SurfaceBlend.Mix, false, false, true), PartTranslucencyRules.WithPartMaterial(V(SurfaceBlend.Opaque, false, true, true)));
        Assert.Equal(V(SurfaceBlend.Mix, false, false, true), PartTranslucencyRules.WithPartMaterial(V(SurfaceBlend.Opaque, true, true, true)));
        Assert.Equal(V(SurfaceBlend.Mix, false, false, true), PartTranslucencyRules.WithPartMaterial(V(SurfaceBlend.Mix, true, true, true)));
        Assert.Equal(V(SurfaceBlend.Mix, false, false, false), PartTranslucencyRules.WithPartMaterial(V(SurfaceBlend.AddOne, true, true, false)));
    }

    [Fact]
    public void Draw_list_batches_by_gfxobj_and_material_state_and_billboards_each_particle()
    {
        var model = new ParticleModel(0x01000001, DrawFrameRulesTests.ParticleChain(64f), Vector3.Zero);
        var viewer = new Vector3(0, -10, 1.5f);
        var list = new ParticleDrawList();
        list.Add(new AcFrame(new Vector3(0, 0, 1), Quaternion.Identity), 1f, 0f, model, viewer);       // default material
        list.Add(new AcFrame(new Vector3(1, 0, 1), Quaternion.Identity), 0.5f, 0.25f, model, viewer);  // part material
        list.Add(new AcFrame(new Vector3(2, 0, 1), Quaternion.Identity), 1f, 1f, model, viewer);       // hidden
        list.Add(new AcFrame(new Vector3(3, 0, 1), Quaternion.Identity), 0.1f, 0f, model, new Vector3(0, -200, 0)); // out of LOD range
        list.Add(new AcFrame(new Vector3(-1, 0, 1), Quaternion.Identity), 2f, 0.5f, model, viewer);    // part material

        Assert.Equal(new ParticleDrawStats(0, 3, 1, 1, 0, 0), list.Stats);
        Assert.Equal(2, list.Batches.Count);
        var plain = list.Batches[0];
        var translucent = list.Batches[1];
        Assert.Equal(new ParticleBatchKey(0x01000001, false), plain.Key);
        Assert.Equal(new ParticleBatchKey(0x01000001, true), translucent.Key);
        Assert.Equal(1f, Assert.Single(plain.Draws).Alpha);
        Assert.Equal([0.75f, 0.5f], translucent.Draws.Select(d => d.Alpha));
        Assert.Equal([0.5f, 2f], translucent.Draws.Select(d => d.Scale));

        foreach (var d in plain.Draws.Concat(translucent.Draws))
        {
            DrawFrameRulesTests.AssertClose(Vector3.Normalize(viewer - d.Origin), Vector3.Transform(-Vector3.UnitY, d.Orientation));
        }

        // A new frame reuses the batches.
        list.Clear();
        Assert.Empty(list.Batches);
        list.Add(new AcFrame(Vector3.Zero, Quaternion.Identity), 1f, 0.5f, model, viewer);
        Assert.Same(translucent, Assert.Single(list.Batches));
    }

    [Fact]
    public void Translucent_particles_share_the_batch_when_no_surface_switches_blend()
    {
        // Additive particle GfxObjs (almost all of them): the part material only replaces the alpha, per instance.
        var model = new ParticleModel(0x01000001, DrawFrameRulesTests.ParticleChain(64f), Vector3.Zero, PartMaterialSwitchesBlend: false);
        var list = new ParticleDrawList();
        list.Add(new AcFrame(new Vector3(0, 0, 1), Quaternion.Identity), 1f, 0f, model, Vector3.Zero);
        list.Add(new AcFrame(new Vector3(1, 0, 1), Quaternion.Identity), 1f, 0.4f, model, Vector3.Zero);
        var batch = Assert.Single(list.Batches);
        Assert.Equal(new ParticleBatchKey(0x01000001, false), batch.Key);
        Assert.Equal([false, true], batch.Draws.Select(d => d.PartMaterial));
        Assert.Equal([1f, 0.6f], batch.Draws.Select(d => d.Alpha));
    }

    [Fact]
    public void Far_particle_objects_share_the_object_heading()
    {
        var model = new ParticleModel(0x01000001, DrawFrameRulesTests.ParticleChain(128f), Vector3.Zero);
        var viewer = Vector3.Zero;
        var emitter = new Vector3(0, 40, 0);
        var shared = DrawFrameRules.UsesObjectRelation(viewer, emitter, 25f) ? DrawFrameRules.Relate(viewer, emitter) : (ViewerRelation?)null;
        Assert.NotNull(shared);
        var list = new ParticleDrawList();
        list.Add(new AcFrame(new Vector3(5, 40, 3), Quaternion.Identity), 1f, 0f, model, viewer, shared);
        var d = Assert.Single(list.Batches[0].Draws);
        DrawFrameRulesTests.AssertClose(-Vector3.UnitY, Vector3.Transform(-Vector3.UnitY, d.Orientation)); // faces back along the object heading
        Assert.Equal(40f, d.ViewerDistance, 4);
    }
}
