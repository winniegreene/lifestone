using System.Numerics;
using Lifestone.Client.Rendering;
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
/// normals bound a single cone (the one in front of the eye) when every side's angle from the view axis is below 90°,
/// i.e. when the field of view is below 180°.</para>
/// <para>A small angular margin widens the pyramid so that rounding never culls a cell whose portal touches the screen
/// edge (the walk can only show more than the exact one). It is capped just short of 90° per side
/// (<see cref="MaxSideAngleRadians"/>), so the portal zoom's widest lens (179.885°, D31) still gives a valid cone.</para>
/// <para>The pyramid of a <see cref="CameraLens"/> may be off-centre (the portal zoom about the SmartBox frame's centre,
/// D40): each side has its own angle from the view axis (<see cref="FromLens"/>).</para>
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
    /// The widest angle a side (margin included) may make with the view axis: 90° less 0.0001 rad (0.0057°). A side at 90°
    /// or more would no longer bound a cone in front of the eye; the portal zoom's widest lens has half-angles of 89.94°
    /// (vertical) and 89.97° (16:9), so this cap costs it nothing.
    /// </summary>
    public const float MaxSideAngleRadians = (MathF.PI / 2f) - 0.0001f;

    /// <summary>
    /// The frustum of a camera looking along <paramref name="forward"/> with <paramref name="up"/> as its up vector
    /// (both AC axes; they need not be exactly orthogonal), a vertical field of view and an aspect ratio (width / height,
    /// the vertical angle is fixed as with Godot's <c>KEEP_HEIGHT</c>). The vertical field of view is strictly between 0° and 180°:
    /// the portal zoom reaches 179.885°.
    /// </summary>
    /// <exception cref="ArgumentException">Degenerate directions or angles.</exception>
    public static ViewFrustum FromCamera(Vector3 forward, Vector3 up, float verticalFovDegrees, float aspect, float marginDegrees = DefaultMarginDegrees)
    {
        if (!(verticalFovDegrees > 0f && verticalFovDegrees < 180f) || !(aspect > 0f) || !float.IsFinite(aspect))
        {
            throw new ArgumentException($"Bad field of view {verticalFovDegrees} or aspect {aspect}.");
        }

        var halfV = (verticalFovDegrees * 0.5f) * (MathF.PI / 180f);
        var halfH = MathF.Atan(aspect * MathF.Tan(halfV));
        return FromAngles(forward, up, halfH, halfH, halfV, halfV, marginDegrees);
    }

    /// <summary>
    /// The frustum of a camera with <paramref name="lens"/> (a perspective, or the portal zoom's off-centre frustum) looking
    /// along <paramref name="forward"/> with <paramref name="up"/> as its up vector, for a view of <paramref name="aspect"/>.
    /// For a symmetric perspective lens it equals <see cref="FromCamera"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Degenerate directions, an invalid lens or aspect.</exception>
    public static ViewFrustum FromLens(Vector3 forward, Vector3 up, in CameraLens lens, float aspect, float marginDegrees = DefaultMarginDegrees)
    {
        if (!lens.IsFrustum)
        {
            return FromCamera(forward, up, lens.VerticalFovDegrees, aspect, marginDegrees);
        }

        var (left, right, bottom, top) = lens.Sides(aspect);
        return FromAngles(forward, up, MathF.Atan(-left), MathF.Atan(right), MathF.Atan(-bottom), MathF.Atan(top), marginDegrees);
    }

    /// <summary>
    /// The frustum whose sides make the given angles with the view axis (radians, positive outwards; negative when the
    /// axis lies outside the rectangle), each widened by the margin and capped at <see cref="MaxSideAngleRadians"/>.
    /// </summary>
    private static ViewFrustum FromAngles(Vector3 forward, Vector3 up, float leftAngle, float rightAngle, float bottomAngle, float topAngle, float marginDegrees)
    {
        var f = Vector3.Normalize(forward);
        var r = Vector3.Cross(f, up);
        if (!(r.LengthSquared() > 1e-12f) || !float.IsFinite(f.X))
        {
            throw new ArgumentException("Forward and up are parallel or zero.");
        }

        r = Vector3.Normalize(r);
        var u = Vector3.Cross(r, f);
        var margin = marginDegrees * (MathF.PI / 180f);
        var tl = MathF.Tan(MathF.Min(leftAngle + margin, MaxSideAngleRadians));
        var tr = MathF.Tan(MathF.Min(rightAngle + margin, MaxSideAngleRadians));
        var tb = MathF.Tan(MathF.Min(bottomAngle + margin, MaxSideAngleRadians));
        var tt = MathF.Tan(MathF.Min(topAngle + margin, MaxSideAngleRadians));

        // Each plane contains the eye and one edge direction of the pyramid; the normal faces the view axis.
        var right = Vector3.Normalize(Vector3.Cross(u, f + (r * tr)));
        var left = Vector3.Normalize(Vector3.Cross(f - (r * tl), u));
        var top = Vector3.Normalize(Vector3.Cross(f + (u * tt), r));
        var bottom = Vector3.Normalize(Vector3.Cross(r, f - (u * tb)));
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
