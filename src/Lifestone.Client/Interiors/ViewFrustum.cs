using System.Numerics;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// The side planes of a perspective camera's view pyramid, as inward normals through the eye, in AC axes (right-handed,
/// Z up). They do not depend on the frame's origin, so one frustum serves every landblock: <see cref="PlanesAt"/> places
/// the planes at the eye's landblock-local position for <see cref="PortalVisibility"/>.
/// </summary>
/// <remarks>
/// <para>Retail clips portals against the screen rectangle (<c>PView::ClipPortals</c>, in screen space); the four side
/// planes through the eye bound exactly the region that projects into that rectangle, so clipping a portal polygon
/// against them in 3D is the same test. No near or far plane is needed: four planes through one point with inward
/// normals bound a single cone (the one in front of the eye) when the field of view is below 180°.</para>
/// <para>A small angular margin widens the pyramid so that rounding never culls a cell whose portal touches the screen
/// edge (the walk can only show more than the exact one).</para>
/// </remarks>
/// <param name="Left">Inward normal of the left plane.</param>
/// <param name="Right">Inward normal of the right plane.</param>
/// <param name="Top">Inward normal of the top plane.</param>
/// <param name="Bottom">Inward normal of the bottom plane.</param>
/// <param name="Forward">The view direction (unit).</param>
public readonly record struct ViewFrustum(Vector3 Left, Vector3 Right, Vector3 Top, Vector3 Bottom, Vector3 Forward)
{
    /// <summary>Default widening of every side, in degrees.</summary>
    public const float DefaultMarginDegrees = 1f;

    /// <summary>
    /// The frustum of a camera looking along <paramref name="forward"/> with <paramref name="up"/> as its up vector
    /// (both AC axes; they need not be exactly orthogonal), a vertical field of view and an aspect ratio (width / height,
    /// the vertical angle is fixed as with Godot's <c>KEEP_HEIGHT</c>).
    /// </summary>
    /// <exception cref="ArgumentException">Degenerate directions or angles.</exception>
    public static ViewFrustum FromCamera(Vector3 forward, Vector3 up, float verticalFovDegrees, float aspect, float marginDegrees = DefaultMarginDegrees)
    {
        if (!(verticalFovDegrees > 0f && verticalFovDegrees < 179f) || !(aspect > 0f) || !float.IsFinite(aspect))
        {
            throw new ArgumentException($"Bad field of view {verticalFovDegrees} or aspect {aspect}.");
        }

        var f = Vector3.Normalize(forward);
        var r = Vector3.Cross(f, up);
        if (!(r.LengthSquared() > 1e-12f) || !float.IsFinite(f.X))
        {
            throw new ArgumentException("Forward and up are parallel or zero.");
        }

        r = Vector3.Normalize(r);
        var u = Vector3.Cross(r, f);
        var halfV = (verticalFovDegrees * 0.5f) * (MathF.PI / 180f);
        var halfH = MathF.Atan(aspect * MathF.Tan(halfV));
        var margin = marginDegrees * (MathF.PI / 180f);
        var tv = MathF.Tan(MathF.Min(halfV + margin, 1.55f));
        var th = MathF.Tan(MathF.Min(halfH + margin, 1.55f));

        // Each plane contains the eye and one edge direction of the pyramid; the normal faces the view axis.
        var right = Vector3.Normalize(Vector3.Cross(u, f + (r * th)));
        var left = Vector3.Normalize(Vector3.Cross(f - (r * th), u));
        var top = Vector3.Normalize(Vector3.Cross(f + (u * tv), r));
        var bottom = Vector3.Normalize(Vector3.Cross(r, f - (u * tv)));
        return new ViewFrustum(left, right, top, bottom, f);
    }

    /// <summary>The four planes through <paramref name="eye"/> (any frame with AC axes), positive inside.</summary>
    public CellPlane[] PlanesAt(Vector3 eye) =>
    [
        Through(Left, eye),
        Through(Right, eye),
        Through(Top, eye),
        Through(Bottom, eye),
    ];

    /// <summary>True when a direction from the eye lies inside the pyramid.</summary>
    public bool ContainsDirection(Vector3 direction) =>
        Vector3.Dot(Left, direction) >= 0f && Vector3.Dot(Right, direction) >= 0f
        && Vector3.Dot(Top, direction) >= 0f && Vector3.Dot(Bottom, direction) >= 0f;

    private static CellPlane Through(Vector3 normal, Vector3 eye) => new(normal, -Vector3.Dot(normal, eye));
}
