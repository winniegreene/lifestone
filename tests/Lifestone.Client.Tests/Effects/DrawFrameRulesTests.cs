using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Models;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// Retail per-part viewer rules: <c>Frame::set_vector_heading</c> (mode 2 billboards), <c>Frame::rotate_around_axis_to_vector</c>
/// (modes 3–5), viewer distance/heading and LOD selection.
/// </summary>
public sealed class DrawFrameRulesTests
{
    private const float Tolerance = 1e-4f;

    public static TheoryData<float, float, float> Headings => new()
    {
        { 0f, 1f, 0f },     // north
        { 3f, -4f, 0f },
        { 1f, 2f, 3f },     // looking up
        { -2f, 0.5f, -1f }, // looking down
        { 0.0001f, 0f, 1f }, // almost straight up
    };

    [Theory]
    [MemberData(nameof(Headings))]
    public void Set_vector_heading_points_local_Y_along_the_heading_with_local_X_horizontal(float x, float y, float z)
    {
        var heading = Vector3.Normalize(new Vector3(x, y, z));
        foreach (var start in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(0.3f, 1.1f, -0.7f) })
        {
            var q = DrawFrameRules.SetVectorHeading(start, heading);
            AssertClose(heading, DrawFrameRules.LocalAxis(q, 1));
            Assert.InRange(DrawFrameRules.LocalAxis(q, 0).Z, -Tolerance, Tolerance); // no roll
            Assert.True(DrawFrameRules.LocalAxis(q, 2).Z >= -Tolerance);               // up stays up
        }
    }

    [Fact]
    public void Set_vector_heading_of_a_tiny_vector_keeps_the_orientation()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
        Assert.Equal(start, DrawFrameRules.SetVectorHeading(start, new Vector3(0.0001f, 0f, 0f)));
    }

    [Fact]
    public void A_billboarded_particle_quad_shows_its_front_face_to_the_viewer()
    {
        // Particle GfxObjs are quads in their local XZ plane whose front (positive-side) normal is local -Y
        // (e.g. 0x010010F2): mode 2 turns local +Y along the viewer->part heading, so the front faces the viewer.
        var viewer = new Vector3(10, -3, 2);
        var part = new AcFrame(new Vector3(14, 5, 1.2f), Quaternion.CreateFromYawPitchRoll(1, 2, 3));
        var relation = DrawFrameRules.PartRelation(viewer, part, new Vector3(0.6f), Vector3.Zero);
        var q = DrawFrameRules.DrawOrientation(part.Orientation, DegradeMode.Billboard, relation.Heading);
        var front = Vector3.Transform(-Vector3.UnitY, q);
        AssertClose(Vector3.Normalize(viewer - part.Origin), front);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rotate_around_axis_keeps_the_axis_and_turns_the_next_one_to_the_viewer(int axis)
    {
        var start = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.9f));
        var dir = Vector3.Normalize(new Vector3(0.3f, 0.8f, -0.5f));
        var q = DrawFrameRules.RotateAroundAxisToVector(start, axis, dir);
        var kept = DrawFrameRules.LocalAxis(start, axis);
        AssertClose(kept, DrawFrameRules.LocalAxis(q, axis));

        // The axis before it in retail's prev[] = {2, 0, 1} cycle follows the component of dir perpendicular to the kept axis.
        var turned = axis switch { 0 => 2, 1 => 0, _ => 1 };
        var expected = Vector3.Normalize(dir - (kept * Vector3.Dot(kept, dir)));
        AssertClose(expected, DrawFrameRules.LocalAxis(q, turned));

        // Still a proper rotation (right-handed axes).
        var x = DrawFrameRules.LocalAxis(q, 0);
        var y = DrawFrameRules.LocalAxis(q, 1);
        var z = DrawFrameRules.LocalAxis(q, 2);
        AssertClose(z, Vector3.Cross(x, y));
    }

    [Fact]
    public void Mode_5_turns_about_the_vertical_only()
    {
        // Upright part (identity): rotating about Z towards a viewer above keeps Z vertical and points local Y horizontally at the part.
        var q = DrawFrameRules.DrawOrientation(Quaternion.Identity, DegradeMode.AxisAlignedZ, Vector3.Normalize(new Vector3(-1, -1, -1)));
        AssertClose(Vector3.UnitZ, DrawFrameRules.LocalAxis(q, 2));
        AssertClose(Vector3.Normalize(new Vector3(-1, -1, 0)), DrawFrameRules.LocalAxis(q, 1));
    }

    [Fact]
    public void Rotate_around_axis_parallel_to_the_direction_keeps_the_orientation()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        var q = DrawFrameRules.RotateAroundAxisToVector(start, 2, DrawFrameRules.LocalAxis(start, 2));
        AssertClose(DrawFrameRules.LocalAxis(start, 0), DrawFrameRules.LocalAxis(q, 0));
        AssertClose(DrawFrameRules.LocalAxis(start, 1), DrawFrameRules.LocalAxis(q, 1));
    }

    [Fact]
    public void Modes_1_and_unknown_keep_the_part_orientation()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f);
        Assert.Equal(start, DrawFrameRules.DrawOrientation(start, DegradeMode.Normal, Vector3.UnitX));
        Assert.Equal(start, DrawFrameRules.DrawOrientation(start, (DegradeMode)7, Vector3.UnitX));
    }

    [Fact]
    public void Viewer_relation_uses_the_scaled_sort_centre_and_retail_zero_rule()
    {
        var frame = new AcFrame(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        var r = DrawFrameRules.PartRelation(new Vector3(1, 2, 0), frame, new Vector3(2), new Vector3(1, 0, 0));

        // Sort centre (1,0,0)·2 rotated 90° about Z → (0,2,0); point (1,4,3).
        Assert.Equal(MathF.Sqrt(4 + 9), r.Distance, 4);
        AssertClose(Vector3.Normalize(new Vector3(0, 2, 3)), r.Heading);
        Assert.Equal(new ViewerRelation(0f, Vector3.UnitZ), DrawFrameRules.Relate(Vector3.One, Vector3.One));
    }

    [Fact]
    public void Object_relation_threshold_is_horizontal()
    {
        Assert.False(DrawFrameRules.UsesObjectRelation(Vector3.Zero, new Vector3(0, 0, 1000), 25f));
        Assert.True(DrawFrameRules.UsesObjectRelation(Vector3.Zero, new Vector3(15, 20, 0), 25f));
        Assert.False(DrawFrameRules.UsesObjectRelation(Vector3.Zero, new Vector3(15, 19.99f, 0), 25f));
        Assert.Equal(25f, DegradeSelector.ParticleDegradeDistance());
    }

    [Fact]
    public void Level_selection_divides_by_the_scale_and_the_player_always_draws_level_0()
    {
        var chain = ParticleChain(maxDistance: 64f);

        // get_degrade: d = max(0, CYpt/scale - 50) < 64 → level 0 (billboard); else the empty level.
        Assert.Equal((0, 0x01000001u, DegradeMode.Billboard), DrawFrameRules.SelectLevel(chain, 113f, 1f));
        Assert.Equal(0u, DrawFrameRules.SelectLevel(chain, 115f, 1f).GfxObjId);
        Assert.Equal(0u, DrawFrameRules.SelectLevel(chain, 12f, 0.1f).GfxObjId); // a 0.1-scale particle vanishes at 11.4 m
        Assert.Equal((0, 0x01000001u, DegradeMode.Normal), DrawFrameRules.SelectLevel(chain, 5000f, 1f, isPlayer: true));
    }

    [Fact]
    public void The_degrade_bias_reaches_level_selection()
    {
        // get_degrade with deg_mul b ≥ 0: level 0 while d < ideal − (ideal − max)·b = 64·b; d = 100 − 50 = 50 m.
        var chain = ParticleChain(maxDistance: 64f);
        Assert.Equal(0x01000001u, DrawFrameRules.SelectLevel(chain, 100f, 1f).GfxObjId);
        Assert.Equal(0u, DrawFrameRules.SelectLevel(chain, 100f, 1f, bias: 0.5f).GfxObjId);

        // ParticleDrawList.DegradeBias applies to the level as well as to the 25 m object-relation threshold.
        var model = new ParticleModel(0x01000001, chain, Vector3.Zero);
        var particle = new AcFrame(new Vector3(0, 100, 0), Quaternion.Identity);
        var list = new ParticleDrawList();
        list.Add(particle, 1f, 0f, model, Vector3.Zero);
        Assert.Equal(1, list.Stats.Drawn);
        list.Clear();
        list.DegradeBias = 0.5f;
        list.Add(particle, 1f, 0f, model, Vector3.Zero);
        Assert.Equal(0, list.Stats.Drawn);
        Assert.Equal(1, list.Stats.OutOfRange);
    }

    /// <summary>A synthetic particle chain: level 0 = 0x01000001 as a billboard, then the empty level.</summary>
    internal static LodChain ParticleChain(float maxDistance) => LodChain.Create(
        0x01000001,
        new DatReaderWriter.DBObjs.GfxObj(),
        new DegradeTable(0x11000001, [
            new DegradeLevel(0x01000001, DegradeMode.Billboard, 0f, 0f, maxDistance),
            new DegradeLevel(0, DegradeMode.Normal, float.MaxValue, float.MaxValue, float.MaxValue),
        ]),
        _ => true);

    internal static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}
