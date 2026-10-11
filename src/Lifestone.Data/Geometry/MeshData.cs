using System.Numerics;

namespace Lifestone.Data.Geometry;

/// <summary>
/// Engine-agnostic renderable geometry built from AC data (a GfxObj, an EnvCell's CellStruct, ...).
/// </summary>
/// <remarks>
/// <para>
/// Conventions, shared by every producer and consumer in Lifestone.Data:
/// <list type="bullet">
/// <item>AC coordinates: right-handed, Z up, metres. The AC to Godot conversion happens only in
/// <c>Lifestone.Game/scripts/Core/AcToGodot.cs</c>.</item>
/// <item>Front faces wind counter-clockwise (AC convention). Retail always draws a polygon's positive side (a
/// <c>NoPos</c> polygon merely has no positive UV indices and is drawn with UV index 0). Polygons that AC also draws
/// from behind (<c>sides_type</c> 1, the same surface reversed, and <c>sides_type</c> 2, a separate negative surface)
/// get extra triangles with reversed winding and negated normals in a sub-mesh whose <see cref="SubMesh.Side"/> is
/// <see cref="MeshSide.Negative"/>; consumers never need to disable back-face culling. For <c>sides_type</c> 2 this
/// is a documented deviation: retail <c>D3DPolyRender::ConstructMesh</c> writes that side unreversed, a coplanar
/// front face that is never visible from behind (11 polygons in 6 end-of-retail GfxObjs, none in cells; see
/// <c>GfxObjMeshBuilder</c>).</item>
/// <item>One <see cref="SubMesh"/> per (surface, side, texture addressing); the surface is the resolved
/// <c>0x08xxxxxx</c> Surface id, not an index into the owner's surface list. Usually that is one per (surface, side),
/// but an owner that lists the same Surface at several surface indices whose retail addressing differs gets one
/// sub-mesh per addressing mode. Key materials and samplers by (<see cref="SubMesh.SurfaceId"/>,
/// <see cref="SubMesh.Addressing"/>), never by the Surface id alone.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class MeshData
{
    public MeshData(IReadOnlyList<SubMesh> subMeshes)
    {
        SubMeshes = subMeshes;
        Bounds = BoundingBox.FromSubMeshes(subMeshes);
    }

    public IReadOnlyList<SubMesh> SubMeshes { get; }

    /// <summary>Axis-aligned bounds of every vertex, in the mesh's local AC frame.</summary>
    public BoundingBox Bounds { get; }

    public int TriangleCount => SubMeshes.Sum(s => s.Indices.Length / 3);
}

/// <summary>Which side of the source polygons a sub-mesh represents.</summary>
public enum MeshSide
{
    /// <summary>The polygon's positive (front) side, as authored.</summary>
    Positive,

    /// <summary>The polygon's negative side: winding reversed and normals negated relative to the source.</summary>
    Negative,
}

/// <summary>
/// Texture addressing (sampler wrap state) retail uses for a mesh subset: <c>TEXADDRESS_WRAP</c> or
/// <c>TEXADDRESS_CLAMP</c> on both U and V (<c>D3DPolyRender::SetSurface</c>, acclient.c:454437).
/// </summary>
/// <remarks>
/// Retail picks it per surface subset on the mesh path (GfxObjs and EnvCells): <c>D3DPolyRender::ConstructMesh</c> ORs
/// <c>(char)stippling &gt; 0</c> of every polygon into <c>isStippledOrAlphaedMask[pos_surface]</c>, and
/// <c>D3DPolyRender::RenderMeshSubset</c> passes <c>mask[subset] &amp; 1</c> to <c>SetSurface</c>, which selects WRAP
/// when it is set, else CLAMP. So a subset WRAPs when any polygon whose <i>positive</i> surface is that surface index
/// has a non-zero stippling byte (NoPos included); a surface index used only as a negative surface CLAMPs
/// (docs/textures.md §5). The per-polygon path (terrain, deferred poly list) uses the side's own bit instead.
/// </remarks>
public enum TextureAddressMode
{
    /// <summary>TEXADDRESS_WRAP: UVs repeat.</summary>
    Wrap,

    /// <summary>TEXADDRESS_CLAMP: UVs are clamped to the edge texels.</summary>
    Clamp,
}

/// <summary>
/// A triangle list sharing one Surface, side and texture addressing. Arrays are parallel; <see cref="Indices"/> holds
/// triangles.
/// </summary>
/// <param name="SurfaceId">Resolved Surface id (<c>0x08xxxxxx</c>).</param>
/// <param name="Side">Which side of the source polygons this is.</param>
/// <param name="Addressing">Retail sampler addressing for this sub-mesh (see <see cref="TextureAddressMode"/>).</param>
/// <param name="Positions">Vertex positions, local AC frame.</param>
/// <param name="Normals">Vertex normals as stored (unnormalised; exactly zero for 16,043 stored GfxObj vertices).</param>
/// <param name="Uvs">Texture coordinates, origin at the texture's top-left texel.</param>
/// <param name="Indices">Triangle list, counter-clockwise front faces.</param>
public sealed record SubMesh(
    uint SurfaceId,
    MeshSide Side,
    TextureAddressMode Addressing,
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uvs,
    int[] Indices);

/// <summary>Axis-aligned bounding box in AC coordinates.</summary>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public static readonly BoundingBox Empty = new(Vector3.Zero, Vector3.Zero);

    public Vector3 Center => (Min + Max) * 0.5f;

    public Vector3 Size => Max - Min;

    public static BoundingBox FromSubMeshes(IEnumerable<SubMesh> subMeshes)
    {
        var any = false;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var subMesh in subMeshes)
        {
            foreach (var p in subMesh.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }

        return any ? new BoundingBox(min, max) : Empty;
    }
}
