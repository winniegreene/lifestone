using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi.Panels;

// M4 2.2 s3 (the minimal drag, D4). This file is the slice's alone. The panels register their own drop targets (the inventory page, the container window, the paperdoll); the world view is the shell's, so it is wired here: an item released over
// the SmartBox's viewport (the 3D area) where no shown window of the shell covers the point, or over no element at all, is dropped to the ground by the Action API; over a panel's empty area the release cancels (M4 2.2 fix R4). The drag itself (UiDrag) is the UI runtime's: UiInputRouter.Drag.
public sealed partial class GamePanels
{
    /// <summary>The world view's drop target (the SmartBox viewport and the area outside every element), or null before the staged wiring has run.</summary>
    public GameWorldDrop? WorldDrop { get; private set; }

    // One Queue call; a unit of its own (a registration).
    private partial void QueueD2()
    {
        Queue(static p =>
        {
            p.WireWorldDrop();
            return null;
        });
    }

    private void WireWorldDrop()
    {
        var drag = Context.Manager.Input.Drag;
        WorldDrop = new GameWorldDrop(Context);
        drag.NoElementTarget = WorldDrop;
        if (Screen.SmartBoxViewport is { } viewport)
        {
            drag.RegisterTarget(viewport, WorldDrop);
            drag.WorldPoint = (x, y) => IsWorldPoint(viewport, drag, x, y); // M4 2.2 fix R4: the viewport fills the screen under the panels, so a release over a panel's empty area must not be the world
        }
        else
        {
            Context.Manager.Services.Log.Warn(UiElementManager.Category, "the gameplay layout has no SmartBox viewport 0x10000436; an item dragged out of a panel can only be dropped on the area outside every element (TODO(m4-drag-world-viewport))");
        }
    }

    /// <summary>
    /// True when the screen point is the world view: inside the SmartBox viewport's box and under no other shown window of the shell (the panel host, the floaty windows, the toolbar, the radar, the chat: the shell root's other
    /// children, the carried item's icon excepted). The windows' fields are not mouse-visible, so the hit test sees the full-screen viewport through them; their boxes decide instead (index loops: no allocation per pointer move).
    /// </summary>
    private bool IsWorldPoint(UiViewport viewport, UiDrag drag, int x, int y)
    {
        var box = viewport.ScreenBox;
        if (x < box.X0 || x > box.X1 || y < box.Y0 || y > box.Y1)
        {
            return false;
        }

        var windows = Screen.Root?.Children;
        for (var i = 0; windows is not null && i < windows.Count; i++)
        {
            var window = windows[i];
            if (ReferenceEquals(window, Screen.SmartBox) || ReferenceEquals(window, drag.Icon) || !window.IsShown)
            {
                continue;
            }

            var area = window.ScreenBox;
            if (x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1)
            {
                return false;
            }
        }

        return true;
    }

    private void DisposeD2()
    {
        var drag = Context.Manager.Input.Drag;
        drag.WorldPoint = null;
        if (Screen.SmartBoxViewport is { } viewport)
        {
            drag.UnregisterTarget(viewport);
        }

        if (ReferenceEquals(drag.NoElementTarget, WorldDrop))
        {
            drag.NoElementTarget = null;
        }

        WorldDrop = null;
    }
}
