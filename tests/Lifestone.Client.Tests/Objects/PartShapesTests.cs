using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// Non-uniform part scales: the shape baked into the mesh plus the uniform instance scale must reproduce retail's
/// positions (R·S·v) and its lit normals (D3D's inverse transpose, normalised: normalize(R·S⁻¹·n)).
/// </summary>
public sealed class PartShapesTests(ObjectsFixture fixture) : IClassFixture<ObjectsFixture>
{
    [Fact]
    public void Uniform_scales_have_no_shape_and_non_uniform_ones_are_divided_by_z()
    {
        Assert.Equal(Vector3.One, PartShapes.ShapeOf(Vector3.One));
        Assert.Equal(Vector3.One, PartShapes.ShapeOf(new Vector3(1.7f)));
        var shape = PartShapes.ShapeOf(new Vector3(0.208f, 0.18f, 0.35f));
        Assert.Equal(new Vector3(0.208f / 0.35f, 0.18f / 0.35f, 1f), shape);

        // The instance scale times the shape is the part scale (object scale 1.6 on top of the default scale).
        var partScale = new Vector3(0.208f, 0.18f, 0.35f) * 1.6f;
        var instance = PartShapes.InstanceScale(partScale, shape);
        Assert.Equal(new Vector3(partScale.Z), instance);
        AssertClose(partScale, instance * shape, 1e-6f);
        Assert.Equal(partScale, PartShapes.InstanceScale(partScale, Vector3.One));
    }

    [Fact]
    public void A_baked_shape_reproduces_retail_positions_and_normals_under_any_part_frame()
    {
        var random = new Random(4186);
        var positions = Enumerable.Range(0, 64).Select(_ => RandomVector(random) * 3f).ToArray();
        var normals = Enumerable.Range(0, 64).Select(i => i % 9 == 0 ? Vector3.Zero : Vector3.Normalize(RandomVector(random))).ToArray();
        var uvs = new Vector2[64];
        var indices = Enumerable.Range(0, 63).ToArray();
        var mesh = new MeshData([new SubMesh(0x08000001, MeshSide.Positive, TextureAddressMode.Wrap, positions, normals, uvs, indices)]);
        var defaultScale = new Vector3(0.208f, 0.18f, 0.35f);
        var shape = PartShapes.ShapeOf(defaultScale);
        var baked = PartShapes.Apply(mesh, shape);
        var sub = baked.SubMeshes[0];
        Assert.Same(uvs, sub.Uvs);
        Assert.Same(indices, sub.Indices);
        Assert.NotSame(positions, sub.Positions);

        for (var trial = 0; trial < 20; trial++)
        {
            var rotation = Quaternion.Normalize(new Quaternion(RandomVector(random), (float)random.NextDouble() - 0.5f));
            var objectScale = 0.5f + (float)random.NextDouble() * 2f;
            var partScale = defaultScale * objectScale;
            var instance = PartShapes.InstanceScale(partScale, shape);
            for (var v = 0; v < positions.Length; v++)
            {
                // Retail: CalcObjectMatrixInternal's matrix R·S on positions.
                var retailPosition = Vector3.Transform(positions[v] * partScale, rotation);
                var drawnPosition = Vector3.Transform(sub.Positions[v] * instance, rotation);
                AssertClose(retailPosition, drawnPosition, 1e-5f);

                if (normals[v] == Vector3.Zero)
                {
                    Assert.Equal(Vector3.Zero, sub.Normals[v]); // still flagged as a zero normal
                    continue;
                }

                // Retail: inverse transpose of R·S is R·S⁻¹, then D3DRS_NORMALIZENORMALS. Godot's MultiMesh: mat3(instance).
                var retailNormal = Vector3.Normalize(Vector3.Transform(normals[v] / partScale, rotation));
                var drawnNormal = Vector3.Normalize(Vector3.Transform(sub.Normals[v] * instance, rotation));
                AssertClose(retailNormal, drawnNormal, 1e-5f);

                // Without the bake (mat3(instance) on the original normal) the lighting would differ.
                var unbaked = Vector3.Normalize(Vector3.Transform(normals[v] * partScale, rotation));
                if (trial == 0 && v == 1)
                {
                    Assert.True(Vector3.Distance(retailNormal, unbaked) > 1e-3f);
                }
            }
        }

        Assert.Same(mesh, PartShapes.Apply(mesh, Vector3.One));
    }

    [RequiresDatsFact]
    public void Non_uniformly_scaled_parts_draw_shaped_meshes_with_a_uniform_instance_scale()
    {
        // Arwic's 3x3 holds trees whose trunks are scaled (0.208, 0.18, 0.35) by Setup 0x020007A3.
        var tree = fixture.Library.GetModel(0x020007A3);
        Assert.True(tree.IsLoadable);
        Assert.Contains(tree.Parts, p => Vector3.Distance(p.DefaultScale, new Vector3(0.208f, 0.18f, 0.35f)) < 1e-4f);

        var shaped = 0;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var set = fixture.Build(new LandblockCoord((byte)(0xC6 + dx), (byte)(0xA9 + dy)));
                for (var p = 0; p < set.Parts.Count; p++)
                {
                    var part = set.Parts[p];
                    var model = fixture.Library.GetModel(set.Objects[part.ObjectIndex].ModelId);
                    var defaultScale = model.Parts[p - set.Objects[part.ObjectIndex].FirstPart].DefaultScale;
                    Assert.Equal(PartShapes.ShapeOf(defaultScale), part.Shape);
                    AssertClose(part.Scale, part.InstanceScale * part.Shape, 1e-5f * Math.Max(1f, part.Scale.Length()));
                    foreach (var mesh in set.LevelMeshes(p))
                    {
                        if (mesh >= 0)
                        {
                            Assert.Equal(part.Shape, set.Meshes[mesh].Shape);
                        }
                    }

                    if (part.Shape != Vector3.One)
                    {
                        shaped++;
                        Assert.True(PartShapes.IsUniform(part.InstanceScale));
                        Assert.Contains(":shape(", set.Meshes[set.LevelMesh(p, 0)].CacheKey, StringComparison.Ordinal);
                    }
                }
            }
        }

        Assert.True(shaped > 0);
    }

    private static Vector3 RandomVector(Random random) =>
        new((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
}
