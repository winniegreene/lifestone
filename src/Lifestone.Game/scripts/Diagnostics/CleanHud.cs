using Godot;
using Lifestone.Client.Options;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--clean-hud</c>, for videos: hides the diagnostic text over the world as each piece appears: the main scene's <c>Hud</c>
/// layer (the position line, the F3 overlay and the objects rows), the interiors line (<c>InteriorsHud</c>) and the player's key
/// hints (<c>KeysLabel</c>). The placeholder notices stay (the play status line, the player's stand-in label, portal space's
/// notice line), and so do the retail UI and the player's notice line.
/// </summary>
public sealed partial class CleanHud : Node
{
    private static readonly string[] HiddenNames = ["Hud", "InteriorsHud", "KeysLabel"];

    /// <summary>True when the run asks for it.</summary>
    public static bool Requested() => CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Any(a => a.Name == "--clean-hud");

    /// <summary>Adds the node to <paramref name="main"/>; it hides what is already there and watches the tree for the rest.</summary>
    public static void Attach(Node main)
    {
        ArgumentNullException.ThrowIfNull(main);
        main.AddChild(new CleanHud { Name = "CleanHud" });
    }

    public override void _Ready()
    {
        GetTree().NodeAdded += OnNodeAdded;
        foreach (var name in HiddenNames)
        {
            HideNode(GetTree().Root.FindChild(name, recursive: true, owned: false));
        }

        Log.Info("main", "--clean-hud: the diagnostic text is hidden as it appears (placeholder notices stay)", ("names", string.Join(",", HiddenNames)));
    }

    public override void _ExitTree() => GetTree().NodeAdded -= OnNodeAdded;

    private static void OnNodeAdded(Node node)
    {
        // Only layers and labels can be one of the names; the name is read (and allocated) for those alone.
        if (node is CanvasLayer or Label && Array.IndexOf(HiddenNames, node.Name.ToString()) >= 0)
        {
            HideNode(node);
        }
    }

    private static void HideNode(Node? node)
    {
        switch (node)
        {
            case CanvasLayer layer:
                layer.Visible = false;
                break;
            case CanvasItem item:
                item.Visible = false;
                break;
        }
    }
}
