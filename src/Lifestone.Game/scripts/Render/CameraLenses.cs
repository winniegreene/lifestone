using Godot;
using Lifestone.Client.Rendering;

namespace Lifestone.Game.Render;

/// <summary>
/// Reads and writes a <see cref="Camera3D"/>'s projection as a <see cref="CameraLens"/>: the one place the engine's
/// two projection modes meet the engine-agnostic code (docs/portal-space.md §6).
/// </summary>
/// <remarks>
/// <para>The world camera is a perspective at the game's field of view (<c>--fov</c>, 60° by default) until the portal
/// presenter's <c>WorldLens</c> sets the zoom, an off-centre frustum (D31: Godot's perspective mode stops at 179°, the zoom
/// reaches 179.885°). Whoever draws from, or reasons about, the camera reads <see cref="Of"/> instead of
/// <c>Camera3D.Fov</c>, which means nothing in frustum mode: the interiors' view frustum and view key, the sky view
/// (<c>SkyView.Follow</c>, which copies the projection mode, near plane, size and offset) and picking.</para>
/// <para>Main thread. <see cref="Of"/> reads three engine properties (five in frustum mode) and allocates nothing.</para>
/// </remarks>
public static class CameraLenses
{
    /// <summary>The lens <paramref name="camera"/> is drawn with now.</summary>
    public static CameraLens Of(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (camera.Projection == Camera3D.ProjectionType.Frustum)
        {
            var offset = camera.FrustumOffset;
            return CameraLens.Frustum(camera.Size, camera.Near, offset.X, offset.Y);
        }

        return CameraLens.Perspective(camera.Fov, camera.Near);
    }

    /// <summary>
    /// Sets <paramref name="camera"/>'s projection to <paramref name="lens"/>, keeping its far plane and aspect rule
    /// (<c>KeepHeight</c>, which the frustum's <c>Size</c> and the lens's vertical angle assume).
    /// </summary>
    /// <exception cref="ArgumentException">An invalid lens.</exception>
    public static void Apply(Camera3D camera, in CameraLens lens)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (!lens.IsValid)
        {
            throw new ArgumentException($"Bad lens {lens}.", nameof(lens));
        }

        camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        if (lens.IsFrustum)
        {
            camera.SetFrustum(lens.FrustumHeight, new Vector2(lens.OffsetX, lens.OffsetY), lens.Near, camera.Far);
        }
        else
        {
            camera.SetPerspective(lens.VerticalFovDegrees, lens.Near, camera.Far);
        }
    }
}
