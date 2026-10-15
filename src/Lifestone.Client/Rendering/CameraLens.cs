using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Rendering;

/// <summary>
/// The projection a view is drawn with, as the engine-agnostic code needs it to build frusta (the interiors' portal
/// walk) and keys: a symmetric perspective with a vertical field of view (the game camera), or an off-centre frustum
/// given by its size and offset at the near plane (the portal zoom, D31, and the SmartBox frame's centring, D40).
/// </summary>
/// <remarks>
/// <para><b>Godot's two modes.</b> A <c>Camera3D</c> in perspective mode keeps its height (<c>KeepHeight</c>):
/// <see cref="VerticalFovDegrees"/> is the vertical angle and the horizontal one follows the aspect. In frustum mode
/// (<c>Projection = Frustum</c>) <c>Size</c> is the frustum's height at the near plane and <c>FrustumOffset</c> shifts
/// the rectangle in the near plane's units, so <see cref="FrustumHeight"/>, <see cref="OffsetX"/> and
/// <see cref="OffsetY"/> are those three values and <see cref="Near"/> is the camera's near plane. Frustum mode is the
/// only way to show the portal zoom exactly: perspective mode stops at 179°, and the zoom reaches 179.885° (D31;
/// <see cref="PortalLens"/>).</para>
/// <para><b>One lens, every consumer.</b> The world camera's lens is set once (the portal presenter's <c>WorldLens</c>
/// in wave 2); the interiors' frustum and view key, the sky view and picking read the same values, so they cannot
/// disagree about what the camera sees. The offset is zero while the world fills the window (no SmartBox frame yet).</para>
/// </remarks>
/// <param name="IsFrustum">Frustum mode (otherwise perspective).</param>
/// <param name="VerticalFovDegrees">Perspective mode: the vertical field of view in degrees, in (0, 180).</param>
/// <param name="FrustumHeight">Frustum mode: the frustum's height at the near plane, in the same units as <paramref name="Near"/>.</param>
/// <param name="Near">The near plane in metres.</param>
/// <param name="OffsetX">Frustum mode: the rectangle's horizontal shift at the near plane (positive = right).</param>
/// <param name="OffsetY">Frustum mode: the rectangle's vertical shift at the near plane (positive = up).</param>
public readonly record struct CameraLens(bool IsFrustum, float VerticalFovDegrees, float FrustumHeight, float Near, float OffsetX, float OffsetY)
{
    /// <summary>A symmetric perspective lens (the game camera's: 60° by default).</summary>
    public static CameraLens Perspective(float verticalFovDegrees, float near) =>
        new(false, verticalFovDegrees, 0f, near, 0f, 0f);

    /// <summary>An off-centre frustum of <paramref name="heightAtNear"/> at <paramref name="near"/>, shifted by (<paramref name="offsetX"/>, <paramref name="offsetY"/>).</summary>
    public static CameraLens Frustum(float heightAtNear, float near, float offsetX = 0f, float offsetY = 0f) =>
        new(true, 0f, heightAtNear, near, offsetX, offsetY);

    /// <summary>
    /// The portal zoom's lens (D31): a frustum of <see cref="PortalLens.FrustumHeightAtNear"/> at <see cref="PortalLens.Near"/>,
    /// shifted by the offset that centres it on the SmartBox frame (zero while the world fills the window). Call it for an
    /// overridden lens; when <see cref="PortalLens.Overridden"/> is false the view keeps its own projection.
    /// </summary>
    public static CameraLens FromPortal(in PortalLens lens, float offsetX = 0f, float offsetY = 0f) =>
        Frustum(lens.FrustumHeightAtNear, lens.Near, offsetX, offsetY);

    /// <summary>
    /// The frustum offset that puts the view's <b>axis</b> (the direction the camera looks along, the point the portal zoom collapses
    /// to and expands from) at (<paramref name="centreX"/>, <paramref name="centreY"/>), given as fractions of the window (0 to 1
    /// from the left, from the top): the hook for centring the world's projection on the SmartBox frame
    /// (<c>RetailUiLayer.GameViewRect</c>, D40), so the zoom collapses and expands about the frame's centre. While the world fills
    /// the window (the in-game shell's default layout) the centre is (0.5, 0.5) and the offset is exactly zero.
    /// </summary>
    /// <remarks>
    /// The offset moves the frustum's rectangle, so the picture moves the other way: a positive offset shifts the rectangle right
    /// and up and the axis left and down. Hence the signs (right: <c>0.5 - centreX</c>; up: <c>centreY - 0.5</c>). An earlier version had
    /// them reversed (it centred the rectangle, which puts the axis on the wrong side of the window); nothing applied an offset
    /// then, and <c>--portal-space-checks</c> now pins the geometry through the real camera.
    /// </remarks>
    /// <param name="heightAtNear">The frustum's height at the near plane (<see cref="FrustumHeight"/>).</param>
    /// <param name="windowAspect">The window's width over its height.</param>
    /// <param name="centreX">The frame centre's horizontal fraction of the window (0.5 = the middle).</param>
    /// <param name="centreY">The frame centre's vertical fraction of the window (0.5 = the middle; 0 is the top).</param>
    public static (float X, float Y) OffsetForCentre(float heightAtNear, float windowAspect, float centreX = 0.5f, float centreY = 0.5f) =>
        ((0.5f - centreX) * heightAtNear * windowAspect, (centreY - 0.5f) * heightAtNear);

    /// <summary>The vertical angle the lens spans on the view axis, in degrees (the frustum's own for an on-axis frustum).</summary>
    public float EffectiveVerticalFovDegrees =>
        IsFrustum
            ? (float)(2.0 * Math.Atan(FrustumHeight / (2.0 * Near)) * (180.0 / Math.PI))
            : VerticalFovDegrees;

    /// <summary>True when the lens can be used: a finite, positive near plane and size or an angle strictly inside (0°, 180°).</summary>
    public bool IsValid =>
        float.IsFinite(Near) && Near > 0f
        && (IsFrustum
            ? float.IsFinite(FrustumHeight) && FrustumHeight > 0f && float.IsFinite(OffsetX) && float.IsFinite(OffsetY)
            : VerticalFovDegrees > 0f && VerticalFovDegrees < 180f);

    /// <summary>
    /// The tangents of the lens's four sides at unit distance in front of the eye, for a view <paramref name="aspect"/>
    /// (width / height; the height is the fixed side, Godot's <c>KeepHeight</c>): <c>Left</c> and <c>Bottom</c> are
    /// normally negative, <c>Right</c> and <c>Top</c> positive. An offset moves the rectangle without resizing it, so
    /// the sides need not be symmetric.
    /// </summary>
    /// <exception cref="ArgumentException">An invalid lens or aspect.</exception>
    public (float Left, float Right, float Bottom, float Top) Sides(float aspect)
    {
        if (!IsValid || !(aspect > 0f) || !float.IsFinite(aspect))
        {
            throw new ArgumentException($"Bad lens {this} or aspect {aspect}.");
        }

        if (!IsFrustum)
        {
            var halfHeight = MathF.Tan(VerticalFovDegrees * 0.5f * (MathF.PI / 180f));
            return (-aspect * halfHeight, aspect * halfHeight, -halfHeight, halfHeight);
        }

        var h = FrustumHeight * 0.5f / Near;
        var w = h * aspect;
        var ox = OffsetX / Near;
        var oy = OffsetY / Near;
        return (-w + ox, w + ox, -h + oy, h + oy);
    }
}
