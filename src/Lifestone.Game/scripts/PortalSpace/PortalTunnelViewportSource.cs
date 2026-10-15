using Godot;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// The portal tunnel as the picture of the in-game shell's SmartBox viewport (0x10000436, docs/retail-ui.md section 21): the retail UI draws
/// <see cref="PortalTunnelView"/>'s texture in the viewport's hole while the tunnel shows, and the hole shows the world otherwise (no picture).
/// The SmartBox fills the screen, so the hole is the whole window and the view (which follows the window) fits it; a hole of another size is
/// scaled to (TODO(g4-tunnel-hole): the view is not resized to a hole that is not the window, which does not happen in this shell). While the hole
/// shows, the tunnel's own full-window picture is hidden (<see cref="PortalTunnelView.DrawnByViewport"/>).
/// </summary>
public sealed class PortalTunnelViewportSource(PortalTunnelView view) : IUiViewportSource
{
    /// <summary>The viewport the tunnel is drawn in: the SmartBox's.</summary>
    public const uint ViewportId = 0x10000436;

    /// <summary>The hole last reported, in window pixels.</summary>
    public Rect2I Hole { get; private set; }

    /// <summary>True while the hole shows.</summary>
    public bool HoleVisible { get; private set; }

    /// <inheritdoc/>
    public Texture2D? Texture => view.Shown && HoleVisible ? view.View.GetTexture() : null;

    /// <inheritdoc/>
    public void Bound(Rect2I windowRect, bool visible)
    {
        Hole = windowRect;
        HoleVisible = visible;
        view.DrawnByViewport = visible;
    }
}
