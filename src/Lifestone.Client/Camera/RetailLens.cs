namespace Lifestone.Client.Camera;

/// <summary>
/// The field of view retail draws the world with (M4 polish pass P2, the play camera against retail's). Retail keeps one preference, <c>Render.FieldOfView</c>, in degrees with the default 90 (the preference's data float, 90.0;
/// <c>SmartBox::SmartBox</c>, 11.6096 0x00454640, starts <c>m_fGameFOV</c> at the same pi / 2 radians) and sets it with <c>SmartBox::SetDefaultFov</c> (11.6096 0x00451EA0). Each frame <c>SmartBox::RenderNormalMode</c>
/// (11.6096 0x00453B40) gives the renderer a vertical angle of <c>m_fGameFOV / (aspect - 0.1)</c> (<c>Render::SetFOVRad</c>; <c>SmartBox::GetOverrideFovDistance</c>, 11.6096 0x00451C20, is the focal distance of the same angle),
/// so the 90 is a nominal width, and a 4:3 window (800x600) is drawn 73 degrees tall and about 89 wide. Lifestone's own default (<c>--fov</c>, 60 vertical) is narrower: the same camera shows less and the player sits lower in the window.
/// </summary>
/// <remarks>
/// <b>The aspect</b> (closes <c>TODO(cam-aspect-unverified)</c>, read from the retail function, not pasted): <c>RenderDevice::SetViewport</c> (11.6096 0x0054FDD0) stores <c>m_ViewportAspectRatio</c> from <c>RenderDevice::ComputeAspectForViewport</c> (11.6096 0x0054FD60),
/// which returns the viewport's width over its height; with its auto-aspect argument off it multiplies that by the display's aspect ratio and 0.75, which is 1 on a 4:3 display (11.4186 0x0054F150). That is the window's width over its height here.
/// The angle is the vertical one: <c>PrimD3DRender::SetFOVInternal</c> (11.6096 0x0059BB50) hands it with that aspect to the D3DX perspective-FOV builder as the vertical angle, and <c>Render::SetFOVInternal</c> (11.6096 0x0054BF50) divides by the half-height.
/// </remarks>
public static class RetailLens
{
    /// <summary>The default of the <c>Render.FieldOfView</c> preference, in degrees (the data float 90.0; 11.4186 acclient.c:45612).</summary>
    public const float DefaultFovDegrees = 90f;

    /// <summary>The 0.1 <c>SmartBox::RenderNormalMode</c> takes off the aspect ratio before dividing.</summary>
    public const float AspectBias = 0.1f;

    /// <summary>The widest vertical angle returned (Godot's cameras stop at 179 degrees).</summary>
    public const float MaxVerticalDegrees = 170f;

    /// <summary>The vertical field of view, in degrees, retail's <c>SmartBox::RenderNormalMode</c> draws a window of <paramref name="aspect"/> (width over height) with, for the preference <paramref name="fovDegrees"/>.</summary>
    /// <param name="fovDegrees">The <c>Render.FieldOfView</c> preference (<see cref="DefaultFovDegrees"/>).</param>
    /// <param name="aspect">The window's width over its height; a value at or under <see cref="AspectBias"/> gives the widest angle.</param>
    public static float VerticalFovDegrees(float fovDegrees, float aspect)
    {
        var divisor = aspect - AspectBias;
        if (!(divisor > 0f) || !float.IsFinite(fovDegrees))
        {
            return MaxVerticalDegrees;
        }

        return Math.Clamp(fovDegrees / divisor, 1f, MaxVerticalDegrees);
    }
}
