using Godot;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Data.Ui;
using Lifestone.Game.Core;

namespace Lifestone.Game.PortalSpace;

/// <summary>
/// A <b>placeholder</b> for retail's spew box (D32), now only for a run without the in-game shell (explore mode): the one status line "In Portal Space - Please
/// Wait..." that the portal director posts (retail <c>ECM_UI::SendNotice_DisplayStringInfo</c>, 11.4186 0x006925B0, 11.6096 0x00693500, text type 26, shown by <c>gmSpewBoxUI</c>), drawn as a plain
/// label at the lower left of the window. While the shell's spew box is registered (<see cref="Shell"/>), the notice goes to it through the router
/// (G4 g2) and this line stays hidden. The label says it is a placeholder, so nothing on screen passes for retail's own box.
/// </summary>
/// <remarks>
/// <para>It shows from the first post until the presenter says the tunnel is gone (<see cref="SetActive"/>, driven by
/// <see cref="PortalFrame.TunnelVisible"/>), so the line is on screen for states 2 to 5 and gone when the world is back (m2-log-in §4.1
/// step 3, §4.4). Each re-post (the director posts again at every
/// new roll segment) replaces the text; identical text is not stacked, as the spew box does not stack it.</para>
/// <para>Draws on canvas layer <see cref="NoticeLayer"/>: above the tunnel (−1) and the world, below the retail UI's slot (10).</para>
/// </remarks>
public partial class PortalNoticeLine : CanvasLayer, IPortalNoticeSink
{
    /// <summary>The canvas layer: above the tunnel and the tool panels (5, 6), below the retail UI.</summary>
    public const int NoticeLayer = 8;

    /// <summary>The label under the line that says it stands in for the spew box.</summary>
    public const string PlaceholderLabel = "placeholder status line - the retail chat/spew box arrives with the in-game shell";

    private PanelContainer _panel = null!;
    private Label _line = null!;
    private bool _active;
    private bool _viaShell;

    /// <summary>The router of the in-game shell, or null: while its spew box is registered a notice goes there instead of to this line.</summary>
    public Func<UiNoticeRouter?>? Shell { get; set; }

    /// <summary>True when the last post went to the shell's spew box.</summary>
    public bool LastViaShell => _viaShell;

    /// <summary>The text currently shown (empty before the first post).</summary>
    public string Text => _line.Text;

    /// <summary>The text type of the last post (26 = the spew box's, 0x1A).</summary>
    public uint LastTextType { get; private set; }

    /// <summary>Posts received.</summary>
    public int Posted { get; private set; }

    /// <summary>True while the line is on screen.</summary>
    public bool Shown => _panel.Visible;

    /// <summary>Builds the (hidden) line.</summary>
    public static PortalNoticeLine Create()
    {
        var node = new PortalNoticeLine { Name = "PortalNoticeLine", Layer = NoticeLayer };
        node._panel = new PanelContainer { Name = "NoticePanel", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        node._panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.6f),
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 5f,
            ContentMarginBottom = 5f,
        });
        // Bottom left, 12 px in: both vertical anchors at the bottom, growing upwards as the text needs room.
        node._panel.AnchorLeft = 0f;
        node._panel.AnchorRight = 0f;
        node._panel.AnchorTop = 1f;
        node._panel.AnchorBottom = 1f;
        node._panel.OffsetLeft = 12f;
        node._panel.OffsetBottom = -12f;
        node._panel.GrowHorizontal = Control.GrowDirection.End;
        node._panel.GrowVertical = Control.GrowDirection.Begin;
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        node._line = new Label { Name = "NoticeText", MouseFilter = Control.MouseFilterEnum.Ignore };
        node._line.AddThemeFontSizeOverride("font_size", 18);
        node._line.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.55f));
        var label = new Label { Name = "PlaceholderLabel", Text = PlaceholderLabel, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        column.AddChild(node._line);
        column.AddChild(label);
        node._panel.AddChild(column);
        node.AddChild(node._panel);
        return node;
    }

    /// <inheritdoc/>
    public void PostNotice(string literal, uint textType)
    {
        ArgumentNullException.ThrowIfNull(literal);
        Posted++;
        LastTextType = textType;
        if (_line.Text != literal)
        {
            _line.Text = literal;
        }

        if (Shell?.Invoke() is { } router && (router.SpewBox is not null || router.ExpectWindows)) // the router holds the notice while the shell's windows are being made
        {
            _viaShell = true;
            router.DisplayStringInfo((int)textType, UiStringRef.FromLiteral(literal));
            Log.Debug("portal", "portal notice (shell spew box)", ("text", literal), ("type", textType), ("posts", Posted));
        }
        else
        {
            _viaShell = false;
            Log.Debug("portal", "portal notice (placeholder line)", ("text", literal), ("type", textType), ("posts", Posted));
        }

        Refresh();
    }

    /// <summary>The presenter's say-so: true while the tunnel shows, false once it is gone (the line then hides).</summary>
    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        Refresh();
    }

    private void Refresh() => _panel.Visible = _active && !_viaShell && _line.Text.Length > 0;
}
