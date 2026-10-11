using System.Numerics;
using Lifestone.Data.Geometry;

namespace Lifestone.Client.Objects;

/// <summary>
/// Non-uniform part scales for instanced drawing: the part's <c>gfxobj_scale</c> is split into a uniform factor (drawn by
/// the instance transform) and a per-axis "shape" baked into the mesh, so normals come out as retail's do.
/// </summary>
/// <remarks>
/// <para><b>Retail.</b> <c>PrimD3DRender::CalcObjectMatrixInternal</c> (acclient.c:453636) builds the part's
/// model-to-world matrix with its per-axis scale, and <c>RenderDeviceD3D::SetDefaultD3DStates</c> (460762) turns on
/// <c>D3DRS_NORMALIZENORMALS</c> (state 143, line 460815). D3D9's fixed-function pipeline transforms normals by the inverse transpose
/// of the world-view matrix and then normalises them: for a part frame R and scale S the lit normal is
/// <c>normalize(R·S⁻¹·n)</c>, while positions go through <c>R·S·v</c>.</para>
/// <para><b>Godot.</b> A <c>MultiMesh</c> instance's normals are transformed by <c>mat3(instance)</c> = R·S, not its inverse
/// transpose (the engine only uses the inverse transpose for a node's own non-uniform scale). For a uniform S the two
/// agree after normalisation, so only non-uniformly scaled parts need this.</para>
/// <para><b>Split.</b> With S = DefaultScale × s (s the uniform object scale), shape σ = DefaultScale / DefaultScale.z and
/// uniform factor u = S.z, so S = u·σ. The mesh is baked with positions σ·v and normals normalize(σ⁻¹·n) (zero normals
/// stay zero); the instance draws it with R·u. Then positions are R·u·σ·v = R·S·v and normals
/// normalize(R·σ⁻¹·n) = normalize(R·S⁻¹·n), exactly retail's. σ depends only on the Setup part, so every instance of a
/// part shares one baked mesh. On end-of-retail data 30 Setups have such parts (66 parts, 77 distinct GfxObj and shape
/// pairs); none has a zero or negative scale component.</para>
/// </remarks>
public static class PartShapes
{
    /// <summary>Squared length at or below which a stored normal counts as zero (the render path's <c>MeshFactory</c> threshold).</summary>
    public const float ZeroNormalLengthSquared = 1e-12f;

    /// <summary>The shape of a part whose scale at object scale 1 is <paramref name="defaultScale"/>: (1, 1, 1) when uniform.</summary>
    public static Vector3 ShapeOf(Vector3 defaultScale) =>
        IsUniform(defaultScale) || defaultScale.Z == 0f ? Vector3.One : defaultScale / defaultScale.Z;

    /// <summary>True when every component is the same.</summary>
    public static bool IsUniform(Vector3 scale) => scale.X == scale.Y && scale.Y == scale.Z;

    /// <summary>
    /// The scale the instance transform applies to a part drawn with a mesh baked for <paramref name="shape"/>: the part's
    /// own scale when the shape is (1, 1, 1), else its z component on every axis.
    /// </summary>
    public static Vector3 InstanceScale(Vector3 partScale, Vector3 shape) => shape == Vector3.One ? partScale : new Vector3(partScale.Z);

    /// <summary>
    /// A copy of <paramref name="mesh"/> with the shape baked in: positions scaled by <paramref name="shape"/>, normals by
    /// its inverse and normalised (zero normals stay zero), everything else shared. Returns the mesh itself for (1, 1, 1).
    /// </summary>
    public static MeshData Apply(MeshData mesh, Vector3 shape)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (shape == Vector3.One)
        {
            return mesh;
        }

        if (shape.X <= 0f || shape.Y <= 0f || shape.Z <= 0f)
        {
            // Would mirror or flatten the mesh (and flip its winding); no end-of-retail Setup does this.
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "TODO(objects): a zero or negative part scale component.");
        }

        var inverse = Vector3.One / shape;
        var subs = new SubMesh[mesh.SubMeshes.Count];
        for (var i = 0; i < subs.Length; i++)
        {
            var sub = mesh.SubMeshes[i];
            var positions = new Vector3[sub.Positions.Length];
            for (var v = 0; v < positions.Length; v++)
            {
                positions[v] = sub.Positions[v] * shape;
            }

            var normals = new Vector3[sub.Normals.Length];
            for (var n = 0; n < normals.Length; n++)
            {
                // A zero normal stays zero (it is lit by ambient and emissive only, render-contract.md §2.1).
                normals[n] = sub.Normals[n].LengthSquared() <= ZeroNormalLengthSquared ? sub.Normals[n] : Vector3.Normalize(sub.Normals[n] * inverse);
            }

            subs[i] = sub with { Positions = positions, Normals = normals };
        }

        return new MeshData(subs);
    }
}
