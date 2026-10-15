using System.Numerics;
using Lifestone.Client.Audio;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The portal-space scene (m2-log-in.md §4.3): retail's separate creature-mode viewport (UI element 0x10000436 of the
/// <c>classic_smartbox</c> layout) holding only the tunnel object at the origin, one distant light and a fixed camera.
/// All coordinates are AC coordinates (right-handed, Z up); convert only through <c>AcToGodot</c>.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name, the constants of <c>gmSmartBoxUI::PostInit</c> (11.4186 0x004D6B60, 11.6096
/// 0x004D77C0), which stores them as float bit patterns; <c>CreatureMode::CreatureMode</c> (11.4186 0x00454390, 11.6096
/// 0x00454430), <c>CreatureMode::AddLight</c> (11.4186 0x004555B0, 11.6096 0x00455650) and <c>CreatureMode::Render</c>
/// (11.4186 0x004529D0, 11.6096 0x00452A10).</para>
/// <para><b>Rendering rules of creature mode</b> (<c>CreatureMode::Render</c>): degrades are disabled while it draws
/// (<see cref="DegradesDisabled"/>); the sun, the static lights and the dynamic lights are all switched off, so the
/// scene is lit only by <see cref="AmbientColor"/> and its own light; its objects are updated inside the render, with
/// the camera as the sound listener; and it uses the SmartBox's FOV (<c>CreatureMode::UseSmartboxFOV</c>), so the
/// portal zoom (<see cref="PortalZoom"/>) applies to it.</para>
/// </remarks>
public static class PortalSpaceScene
{
    /// <summary>
    /// The camera's position: (0.24, −2.7, 0.88), inside the tube near its lowest point (<c>gmSmartBoxUI::PostInit</c>
    /// and again each time the tunnel is shown, <c>CreatureMode::SetCameraPosition</c>).
    /// </summary>
    public static readonly Vector3 CameraPosition = new(0.24f, -2.7f, 0.88f);

    /// <summary>
    /// The camera's view axis: +Y, AC's forward. Its frame starts as the identity (<c>CreatureMode::CreatureMode</c>), and
    /// the roll turns it about this axis (<c>CreatureMode::SetCameraDirection_Degrees</c> with the angle as y).
    /// </summary>
    public static readonly Vector3 ViewAxis = Vector3.UnitY;

    /// <summary>The tunnel object's position: the scene's origin, with the identity orientation.</summary>
    public static readonly Vector3 TunnelPosition = Vector3.Zero;

    /// <summary>
    /// The distant light's direction as <c>gmSmartBoxUI::PostInit</c> passes it to <c>CreatureMode::SetLightDirection</c>:
    /// (0.3, −1.9, 0.65), not unit length. Retail's <c>LIGHTINFO::SetDirection</c> normalises it and keeps only its
    /// heading (<see cref="LightDirectionUnit"/>).
    /// </summary>
    public static readonly Vector3 LightDirection = new(0.3f, -1.9f, 0.65f);

    /// <summary><see cref="LightDirection"/> normalised, as retail's light uses it.</summary>
    public static readonly Vector3 LightDirectionUnit = Vector3.Normalize(LightDirection);

    /// <summary>The distant light's intensity (<c>CreatureMode::AddLight(DISTANT_LIGHT, 2.0)</c>).</summary>
    public const float LightIntensity = 2f;

    /// <summary>The distant light's colour, opaque white (<c>CreatureMode::AddLight</c>).</summary>
    public const uint LightColorArgb = 0xFFFFFFFF;

    /// <summary>The light's falloff field, the largest float (<c>CreatureMode::AddLight</c>; no falloff).</summary>
    public const float LightFalloff = float.MaxValue;

    /// <summary>The light's cone-angle field (<c>CreatureMode::AddLight</c>; a distant light has no cone).</summary>
    public const float LightConeAngle = 360f;

    /// <summary>Creature mode's ambient light, (0.3, 0.3, 0.3) (<c>CreatureMode::CreatureMode</c>).</summary>
    public static readonly Vector3 AmbientColor = new(0.3f, 0.3f, 0.3f);

    /// <summary>The spin's start frame (<c>CPhysicsObj::set_sequence_animation(anim, 1, 1, 40)</c>).</summary>
    public const int SpinStartFrame = 1;

    /// <summary>The spin's frame rate in frames per second.</summary>
    public const float SpinFramerate = 40f;

    /// <summary>Degrades (lower levels of detail) are disabled while creature mode draws.</summary>
    public const bool DegradesDisabled = true;

    /// <summary>
    /// Where the tunnel's sounds (its hum) are heard from: the listener is the tunnel camera while creature mode updates
    /// its objects (<c>CreatureMode::Render</c> moves <c>SoundManager::SetPlayerPosition</c>, 11.4186 0x005503C0, 11.6096
    /// 0x00550FD0, to the creature-view frame for the update), and the tunnel object is at the origin, so the hum is
    /// |<see cref="CameraPosition"/>| ≈ 2.85 m away. That is under 5 m, so it is neither panned nor attenuated by
    /// distance, and its bearing is left at 0.
    /// </summary>
    public static readonly SoundOrigin TunnelHumOrigin = new(Vector3.Distance(CameraPosition, TunnelPosition), 0f);

    /// <summary>
    /// Creature mode's own FOV when it does not use the SmartBox's, π/4 as a float (<c>CreatureMode::CreatureMode</c>).
    /// Portal space always uses the SmartBox's (<c>UseSmartboxFOV</c>), so this is reference only.
    /// </summary>
    public const float OwnFovRadians = 0.7853982f;
}
