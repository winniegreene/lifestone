using Godot;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Hides the developer HUD for a review frame and puts it back (M5 4 h1: extracted from <c>JourneyLegRunner</c>, whose frames and the autopilot check's share it). It hides the nodes named by
/// <see cref="PlayMode.PlayShotDeveloperHudNames"/> and remembers whether each was visible; <see cref="Showing"/> says whether any is still drawn (the frame's <c>developer_hud=</c> field is read from it).
/// </summary>
/// <param name="owner">The node the tree is reached through.</param>
internal sealed class DeveloperHudHider(Node owner)
{
    private readonly List<(Node Node, bool WasVisible)> _hidden = [];

    /// <summary>True when a node this hider hid is visible again (or still): the frame was not clean.</summary>
    public bool Showing()
    {
        foreach (var (node, _) in _hidden)
        {
            if (GodotObject.IsInstanceValid(node) && node is CanvasItem { Visible: true } item && item.IsVisibleInTree())
            {
                return true;
            }

            if (GodotObject.IsInstanceValid(node) && node is CanvasLayer { Visible: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hides the developer HUD nodes (each once is remembered).</summary>
    public void Hide()
    {
        foreach (var name in PlayMode.PlayShotDeveloperHudNames)
        {
            switch (owner.GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    Note(layer, layer.Visible);
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    Note(item, item.Visible);
                    item.Visible = false;
                    break;
            }
        }
    }

    /// <summary>Puts every hidden node back as it was.</summary>
    public void Restore()
    {
        foreach (var (node, wasVisible) in _hidden)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            switch (node)
            {
                case CanvasLayer layer:
                    layer.Visible = wasVisible;
                    break;
                case CanvasItem item:
                    item.Visible = wasVisible;
                    break;
            }
        }

        _hidden.Clear();
    }

    private void Note(Node node, bool wasVisible)
    {
        foreach (var (known, _) in _hidden)
        {
            if (ReferenceEquals(known, node))
            {
                return;
            }
        }

        _hidden.Add((node, wasVisible));
    }
}
