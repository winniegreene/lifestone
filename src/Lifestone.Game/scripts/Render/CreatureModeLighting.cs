using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Render;

/// <summary>
/// Creature mode's light, as the <c>ac_cm_*</c> shader globals the <see cref="Lifestone.Client.Rendering.SurfaceLighting.CreatureMode"/>
/// variants read (<see cref="SurfaceLightingCode.CreatureAmbient"/>, <see cref="SurfaceLightingCode.CreatureLightDirection"/>,
/// <see cref="SurfaceLightingCode.CreatureLightColor"/>). Written by whichever creature-mode view is about to draw
/// (<c>CreatureModeView.SetLights</c>; the portal tunnel, the character-creation figure).
/// </summary>
/// <remarks>
/// <para><b>Conventions</b> (pinned by <c>--portal-space-checks</c>): the direction is the unit vector <i>towards</i> the
/// light in Godot world axes, the sense of <c>ac_sun_dir</c> (a surface facing the light has N.L = 1); the colour is the
/// light's diffuse colour already multiplied by its intensity (retail's <c>CreatureMode::AddLight</c> (11.4186 0x004555B0, 11.6096 0x00455650)
/// with <c>DISTANT_LIGHT</c>, 2.0 and colour white is (2, 2, 2), which saturates anything facing it); the result is clamped per vertex after adding the
/// ambient and the emissive, as the world's sun is.</para>
/// <para><b>TODO(portal): the sign of retail's light vector is unverified.</b> <c>gmSmartBoxUI::PostInit</c> passes
/// (0.3, -1.9, 0.65) to <c>CreatureMode::SetLightDirection</c> (11.4186 0x004544C0, 11.6096 0x00454560) and <c>LIGHTINFO::SetDirection</c> normalises it
/// (<see cref="PortalSpaceScene.LightDirectionUnit"/>). Lifestone reads it as pointing <i>towards</i> the light, the sense of
/// the world's <c>LScape::sunlight</c>: the light then stands behind and above the camera, whose own offset from the tunnel
/// is (0.24, -2.7, 0.88) (a head-light that lights the tunnel's far wall). If retail's D3D light takes it as the direction
/// the light travels, the lit side flips: negate the direction here, in <see cref="PortalSceneDirection"/>, and nowhere else.</para>
/// <para>Global shader uniforms are shared by every viewport, so only one creature-mode light is in force at a time; a
/// view writes its lights when it is shown. Main thread; allocates nothing beyond the engine variant boxing.</para>
/// </remarks>
public static class CreatureModeLighting
{
    /// <summary>
    /// The portal-space scene's light direction (<see cref="PortalSpaceScene.LightDirectionUnit"/>) as the shader global wants
    /// it: AC axes to Godot, through <see cref="AcToGodot"/>, pointing towards the light (see the remarks).
    /// </summary>
    public static Vector3 PortalSceneDirection => AcToGodot.Direction(PortalSpaceScene.LightDirectionUnit);

    /// <summary>
    /// Writes creature mode's lights.
    /// </summary>
    /// <param name="ambient">The scene's ambient, raw colour units (retail: 0.3 white).</param>
    /// <param name="towardLight">The direction towards the distant light, Godot world axes (normalised here).</param>
    /// <param name="lightColor">The light's diffuse colour times its intensity, raw colour units.</param>
    public static void Apply(Vector3 ambient, Vector3 towardLight, Vector3 lightColor)
    {
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.CreatureAmbient, ambient);
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.CreatureLightDirection, towardLight.Normalized());
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.CreatureLightColor, lightColor);
    }

    /// <summary>Writes the portal-space scene's lights: ambient 0.3, the distant light of intensity 2.</summary>
    public static void ApplyPortalScene()
    {
        if (!_signLogged)
        {
            _signLogged = true;
            Log.Info(
                "render",
                "creature-mode light read as pointing towards the light (TODO(portal): the sign of retail's CreatureMode::SetLightDirection vector is unverified)",
                ("direction", PortalSceneDirection));
        }

        Apply(
            new Vector3(PortalSpaceScene.AmbientColor.X, PortalSpaceScene.AmbientColor.Y, PortalSpaceScene.AmbientColor.Z),
            PortalSceneDirection,
            new Vector3(1f, 1f, 1f) * PortalSpaceScene.LightIntensity);
    }

    private static bool _signLogged;

    /// <summary>The same direction from an AC vector (a caller with its own light, such as the character-creation scene).</summary>
    public static Vector3 FromAc(AcVector3 towardLightAc) => AcToGodot.Direction(towardLightAc);
}
