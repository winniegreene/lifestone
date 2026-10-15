namespace Lifestone.Client.PortalSpace;

/// <summary>
/// One projection of the portal-space zoom, as retail's renderer derives it from a focal distance: retail
/// <c>Render::set_vdst</c> (11.4186 0x0054B240, 11.6096 0x0054BE50) turns the focal distance into a vertical field of view
/// and a near plane. See <see cref="PortalZoom"/> for the maths.
/// </summary>
/// <remarks>
/// <para><b>Applying it.</b> A camera that shows this lens uses <see cref="VerticalFovRadians"/> (vertical, keep height)
/// and <see cref="Near"/>. Godot's perspective camera stops at 179°, and the zoom reaches about 179.9°, so the wave-2
/// presenter uses a frustum projection whose height at the near plane is <see cref="FrustumHeightAtNear"/> (D31: exact,
/// no 179° cap).</para>
/// <para>When <see cref="Overridden"/> is false the view uses its normal projection (retail's override is off in state 0
/// and from <c>EndTeleportAnimation</c> through state 4; in state 3 it is still set, at f_game); <see cref="Focal"/> is
/// then the game camera's own focal distance, so the properties still describe that normal projection.</para>
/// </remarks>
/// <param name="Focal">The focal distance f = 1 / tan(FOV / 2) (retail <c>vdst</c>); 0.001 fully collapsed.</param>
/// <param name="Overridden">
/// Retail's override is set (<c>SmartBox::SetOverrideFovDistance(1, f)</c>): the zoom owns the projection this frame.
/// </param>
public readonly record struct PortalLens(float Focal, bool Overridden)
{
    /// <summary>The vertical field of view in radians, clamped to [0.001, π] and rounded to a float as retail passes it.</summary>
    public double VerticalFovRadians => PortalZoom.VerticalFovRadians(Focal);

    /// <summary><see cref="VerticalFovRadians"/> in degrees (about 179.885° at f = 0.001).</summary>
    public double VerticalFovDegrees => VerticalFovRadians * (180.0 / Math.PI);

    /// <summary>The near plane in metres: f / 4 below f = 0.4, else 0.1 (<see cref="PortalZoom.NearPlane"/>).</summary>
    public float Near => PortalZoom.NearPlane(Focal);

    /// <summary>
    /// The frustum's height at the near plane, 2 · near · tan(FOV / 2): 0.5 (to about 1e-4) for every f below 0.4,
    /// 0.2 / f above it. A Godot frustum projection with this size reproduces the lens exactly.
    /// </summary>
    public float FrustumHeightAtNear => PortalZoom.FrustumHeightAtNear(Focal);
}
