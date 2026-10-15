using System.Numerics;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;

namespace Lifestone.Client.Play;

/// <summary>
/// What picking tests a drawn part against (M2 G4 g5a, docs/avatars.md §18): the GfxObj's drawing sphere and its drawn triangles, in the
/// GfxObj's own AC frame. It holds the mesh's arrays; nothing is copied.
/// </summary>
/// <remarks>
/// Retail tests <c>CGfxObj::drawing_sphere</c> and then every <c>CPolygon</c> of the part (<c>Render::GfxObjUnderSelectionRay</c>,
/// 0x0054D350). The polygons are the drawing polygons, which <see cref="MeshData"/> holds as counter-clockwise triangles (a polygon
/// that is drawn from behind also has a reversed copy there, so culling the back faces by winding is retail's <c>sides_type</c> rule).
/// </remarks>
public sealed class PickShape
{
    /// <summary>Makes a shape from a drawing sphere and the triangle lists of the mesh.</summary>
    /// <param name="centre">The drawing sphere's centre in the GfxObj's frame.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="subMeshes">The mesh's triangle lists (counter-clockwise front faces).</param>
    public PickShape(Vector3 centre, float radius, IReadOnlyList<SubMesh> subMeshes)
    {
        ArgumentNullException.ThrowIfNull(subMeshes);
        Centre = centre;
        Radius = radius;
        SubMeshes = subMeshes;
    }

    /// <summary>The drawing sphere's centre.</summary>
    public Vector3 Centre { get; }

    /// <summary>The drawing sphere's radius.</summary>
    public float Radius { get; }

    /// <summary>The triangle lists.</summary>
    public IReadOnlyList<SubMesh> SubMeshes { get; }

    /// <summary>The shape of <paramref name="mesh"/>, or null when it has no drawing sphere (a GfxObj without a drawing tree: nothing to pick).</summary>
    public static PickShape? FromMesh(GfxObjMesh? mesh) =>
        mesh?.DrawingSphere is { } sphere ? new PickShape(sphere.Center, sphere.Radius, mesh.Mesh.SubMeshes) : null;
}
