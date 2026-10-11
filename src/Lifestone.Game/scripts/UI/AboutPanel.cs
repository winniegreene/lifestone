using Godot;
using Lifestone.Client;

namespace Lifestone.Game.UI;

/// <summary>
/// The About panel (F1, or the "About [F1]" button): Lifestone's Appropriate Legal Notices (GNU AGPL-3.0 §5(d)): the
/// copyright and licence notice, that there is no warranty, where the licence and the source are, the works Lifestone
/// builds on and the note on Asheron's Call's own data. The text is <see cref="LegalNotices"/> (Lifestone.Client), the
/// one place it lives, so the panel and the README agree.
/// </summary>
/// <remarks>F1 or the button opens and closes it; Esc and the close button close it. It sits on its own canvas layer
/// above the other panels.</remarks>
public partial class AboutPanel : PanelContainer
{
    /// <summary>Canvas layer of the About panel and its button: above the sky/navigation panels (5).</summary>
    public const int Layer = 6;

    /// <summary>The button that opens the panel (shown while it is closed).</summary>
    public Button? OpenButton { get; private set; }

    /// <summary>
    /// Adds the panel (closed) and, when <paramref name="withButton"/>, its "About [F1]" button to a new canvas layer
    /// under <paramref name="parent"/>. The button sits at <paramref name="buttonPosition"/> (next to the other tool
    /// buttons at the top left); <paramref name="canvasLayer"/> puts the layer above a scene's other panels.
    /// </summary>
    public static AboutPanel Attach(Node parent, bool withButton, Vector2 buttonPosition, int canvasLayer = Layer)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var layer = new CanvasLayer { Name = "AboutUi", Layer = canvasLayer };
        parent.AddChild(layer);
        var panel = new AboutPanel();
        panel.Build(layer, withButton, buttonPosition);
        layer.AddChild(panel);
        return panel;
    }

    private void Build(CanvasLayer layer, bool withButton, Vector2 buttonPosition)
    {
        Name = "AboutPanel";
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Background with { A = 0.94f }, UiStyle.Border, 1, 14));
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -330;
        OffsetRight = 330;
        OffsetTop = -250;
        OffsetBottom = 250;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        Visible = false;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);

        var header = new HBoxContainer();
        var title = UiStyle.Label($"About {LegalNotices.ProductName}", 18, UiStyle.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = UiStyle.Button("x", "Close (F1 or Esc)");
        close.Pressed += () => Toggle(false);
        header.AddChild(close);
        box.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        var text = UiStyle.Label(LegalNotices.FullText(), 13);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.CustomMinimumSize = new Vector2(620, 0);
        scroll.AddChild(text);

        if (withButton)
        {
            OpenButton = UiStyle.Button("About [F1]", "Licence (AGPL-3.0), no warranty, source and credits");
            OpenButton.Position = buttonPosition;
            OpenButton.Pressed += () => Toggle(true);
            layer.AddChild(OpenButton);
        }
    }

    /// <summary>Opens or closes the panel.</summary>
    public void Toggle(bool open)
    {
        Visible = open;
        if (OpenButton is not null)
        {
            OpenButton.Visible = !open;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.PhysicalKeycode == Key.F1 || (key.PhysicalKeycode == Key.Escape && Visible))
        {
            Toggle(key.PhysicalKeycode == Key.F1 && !Visible);
            GetViewport().SetInputAsHandled();
        }
    }
}
