namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The portal-space zoom maths (m2-log-in.md §4.6): focal distance to field of view and near plane, and the collapse
/// and expand curves. Pure and allocation-free.
/// </summary>
/// <remarks>
/// <para><b>Focal distance.</b> Retail's renderer works with f = 1 / tan(FOV / 2) (<c>vdst</c>). While portal space
/// overrides it (<c>SmartBox::SetOverrideFovDistance</c>, 11.4186 0x00451BC0, 11.6096 0x00451C00), both the world view
/// and the tunnel's creature mode project with <c>Render::set_vdst(f)</c> (11.4186 0x0054B240, 11.6096 0x0054BE50):</para>
/// <list type="bullet">
/// <item>FOV = 2 · atan2(1, f). The decompiler prints the arctangent's arguments swapped; the 11.6096 machine code
/// (<c>fld 1.0</c>, <c>fld vdst</c>, <c>fpatan</c>) gives atan(1/f), so f = 0 gives π;</item>
/// <item>the FOV is clamped to [0.001, π] and handed on as a float;</item>
/// <item>it is a vertical FOV: <c>Render::SetFOVInternal</c> (11.4186 0x0054B340, 11.6096 0x0054BF50) divides by the
/// viewport height;</item>
/// <item>the near plane is 0.1 when f ≥ 0.4 and f / 4 below it (the decompile prints the float 0.4 as
/// <c>0.40000001</c>).</item>
/// </list>
/// <para><b>The curves</b> are <c>gmSmartBoxUI::UseTime</c>'s (11.4186 0x004D6E30, 11.6096 0x004D7A90): t is the time in
/// the state over 1 s, capped at 1, eased with <see cref="AnimLevel"/>; collapse runs from f_game to 0.001 (states 1
/// and 5), expand from 0.001 back to f_game (states 2 and 6). At 0.001 the FOV is about 179.885°: the whole view sits in
/// a few pixels at the centre of the SmartBox frame.</para>
/// <para><b>f_game.</b> Retail's is <c>SmartBox::GetOverrideFovDistance</c> without an override (11.4186 0x00451BE0,
/// 11.6096 0x00451C20): 1 / tan(gameFOV / (aspect − 0.1) / 2), where gameFOV / (aspect − 0.1) is also the vertical FOV
/// its world view uses (<c>SmartBox::RenderNormalMode</c>, 11.4186 0x00453AA0, 11.6096 0x00453B40, and
/// <c>CreatureMode::Render</c>, both through <c>Render::SetFOVRad</c>), so the zoom ends on the normal view. Lifestone's
/// world camera has a vertical FOV of its own (<c>--fov</c>), so Lifestone's f_game is
/// <see cref="FocalFromVerticalFov"/> of that FOV: the same relation, without retail's horizontal-to-vertical
/// approximation (<see cref="RetailGameVerticalFov"/> keeps it for reference).</para>
/// </remarks>
public static class PortalZoom
{
    /// <summary>The fully collapsed focal distance, retail's literal <c>0.001</c> in the zoom lines.</summary>
    public const double CollapsedFocal = 0.001;

    /// <summary>Below this focal distance the near plane follows the zoom (<c>Render::set_vdst</c>'s float 0.4).</summary>
    public const float NearThreshold = 0.40000001f;

    /// <summary>The near plane at or above <see cref="NearThreshold"/> (retail <c>znear</c> 0.1, also <c>SetFOVRad</c>'s).</summary>
    public const float DefaultNear = 0.1f;

    /// <summary>Below <see cref="NearThreshold"/> the near plane is the focal distance times this.</summary>
    public const double NearFactor = 0.25;

    /// <summary>The smallest FOV retail hands the renderer, in radians.</summary>
    public const double MinFovRadians = 0.001;

    /// <summary>The largest FOV retail hands the renderer, in radians (its literal π).</summary>
    public const double MaxFovRadians = 3.141592653589793;

    /// <summary>
    /// Retail's starting game FOV, π/2 as a float (<c>SmartBox::SmartBox</c>, 11.4186 0x004545A0, 11.6096 0x00454640);
    /// <c>SmartBox::SetDefaultFov</c> replaces it with the player's setting in degrees.
    /// </summary>
    public const float RetailDefaultGameFov = 1.5707964f;

    /// <summary>
    /// The focal distance while the view collapses into the centre (states 1 and 5): f_game at t = 0, 0.001 at t ≥ 1.
    /// </summary>
    /// <param name="gameFocal">f_game, captured when the sequence began.</param>
    /// <param name="t">Seconds in the state over 1 s; capped at 1, and eased below 0 as 0.</param>
    public static float Collapse(float gameFocal, double t) =>
        (float)((Level(t) * AnimLevel.Unit * (CollapsedFocal - gameFocal)) + gameFocal);

    /// <summary>
    /// The focal distance while the view grows out of the centre (states 2 and 6): 0.001 at t = 0, f_game at t ≥ 1.
    /// </summary>
    /// <param name="gameFocal">f_game, captured when the sequence began.</param>
    /// <param name="t">Seconds in the state over 1 s; capped at 1, and eased below 0 as 0.</param>
    public static float Expand(float gameFocal, double t) =>
        (float)((Level(t) * AnimLevel.Unit * (gameFocal - CollapsedFocal)) + CollapsedFocal);

    /// <summary>
    /// The vertical field of view, in radians, that retail's renderer gets for focal distance <paramref name="focal"/>:
    /// 2 · atan2(1, f), clamped to [0.001, π] and rounded to a float (returned widened). NaN gives 0.001.
    /// </summary>
    public static double VerticalFovRadians(float focal)
    {
        var fov = 2.0 * Math.Atan2(1.0, focal);
        if (!(fov >= MinFovRadians))
        {
            return (float)MinFovRadians;
        }

        return (float)Math.Min(fov, MaxFovRadians);
    }

    /// <summary>The near plane retail sets for focal distance <paramref name="focal"/>: 0.1 from f = 0.4 up, else f / 4.</summary>
    public static float NearPlane(float focal) => focal >= NearThreshold ? DefaultNear : (float)(focal * NearFactor);

    /// <summary>
    /// The frustum's height at the near plane for focal distance <paramref name="focal"/>: 2 · near · tan(FOV / 2) with
    /// retail's float FOV. 0.5 (to about 1e-4) for every f below 0.4, and 0.2 / f above it.
    /// </summary>
    public static float FrustumHeightAtNear(float focal) =>
        (float)(2.0 * NearPlane(focal) * Math.Tan(VerticalFovRadians(focal) * 0.5));

    /// <summary>The focal distance 1 / tan(FOV / 2) of a camera whose vertical field of view is <paramref name="verticalFovRadians"/>.</summary>
    public static float FocalFromVerticalFov(double verticalFovRadians) => (float)(1.0 / Math.Tan(verticalFovRadians * 0.5));

    /// <summary>
    /// Retail's vertical FOV for its game FOV setting: gameFOV / (aspect − 0.1), in radians (<c>SmartBox::RenderNormalMode</c> and
    /// <c>CreatureMode::Render</c> pass it to <c>Render::SetFOVRad</c>). Reference only: Lifestone's camera has a vertical
    /// FOV of its own.
    /// </summary>
    /// <param name="gameFovRadians">Retail <c>m_fGameFOV</c> (<see cref="RetailDefaultGameFov"/> until the player sets one).</param>
    /// <param name="viewportAspectRatio">The viewport's width over its height (<c>m_ViewportAspectRatio</c>).</param>
    public static double RetailGameVerticalFov(double gameFovRadians, double viewportAspectRatio) =>
        gameFovRadians / (viewportAspectRatio - 0.1);

    /// <summary>
    /// Retail's f_game: <c>SmartBox::GetOverrideFovDistance</c> with no override, 1 / tan(gameFOV / (aspect − 0.1) · 0.5).
    /// Reference only (see the remarks).
    /// </summary>
    public static double RetailGameFocal(double gameFovRadians, double viewportAspectRatio) =>
        1.0 / Math.Tan(RetailGameVerticalFov(gameFovRadians, viewportAspectRatio) * 0.5);

    /// <summary>The eased level for <c>UseTime</c>'s t: capped at 1 in double, then handed to <c>GetAnimLevel</c> as a float.</summary>
    private static int Level(double t) => AnimLevel.Level((float)(t >= 1.0 ? 1.0 : t));
}
