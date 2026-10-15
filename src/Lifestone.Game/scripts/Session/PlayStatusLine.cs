using Godot;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The placeholder status line of play mode: one line at the top left of the window, under the game screen's own placeholder note, with the stage and the
/// character under a fixed prefix that says it is a placeholder. The in-game shell's spew box and chat (G4 g2) replace it while the shell is up, so play mode
/// hides it then (an offline stand-in session keeps <see cref="StandInNote"/>); it still shows during the login screens and in a run without the shell. Lifestone's own text, not a retail window.
/// </summary>
public sealed partial class PlayStatusLine : CanvasLayer
{
    /// <summary>The canvas layer: above the player's text (5), below the portal notice line (8) and the retail UI (10).</summary>
    public const int LayerSlot = 7;

    /// <summary>What every line starts with.</summary>
    public const string Prefix = "PLACEHOLDER STATUS (Lifestone's own line, shown outside the in-game shell): ";

    /// <summary>
    /// The note that stays on screen in the offline stand-in session while the in-game shell is up (review R15): the shell's meters, radar objects and chat then show
    /// the stand-in's placeholder values, and the status line above is hidden. Short, in two lines, so it clears the spew box at 800 wide.
    /// </summary>
    public const string StandInNote = "STAND-IN SERVER (offline):\nplaceholder values";

    private Label _label = null!;
    private string _text = string.Empty;

    /// <summary>The line's text without the prefix.</summary>
    public string Text => _text;

    /// <summary>Makes the line (not yet in the tree).</summary>
    public static PlayStatusLine Create()
    {
        var layer = new PlayStatusLine { Name = "PlayStatusLine", Layer = LayerSlot };
        layer._label = new Label { Name = "Status", Text = Prefix, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer._label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        layer._label.OffsetLeft = 8;
        layer._label.OffsetTop = 22; // under the game screen's note (the retail UI's placeholder line, top left)
        layer._label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        layer._label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer._label.AddThemeConstantOverride("outline_size", 4);
        layer._label.AddThemeFontSizeOverride("font_size", 13);
        layer.AddChild(layer._label);
        return layer;
    }

    /// <summary>Shows <paramref name="note"/> as it is, without the placeholder-status prefix (only when it changed).</summary>
    public void SetNote(string note)
    {
        if (note == _text)
        {
            return;
        }

        _text = note;
        _label.Text = note;
    }

    /// <summary>Shows <paramref name="text"/> (only when it changed).</summary>
    public void SetText(string text)
    {
        if (text == _text)
        {
            return;
        }

        _text = text;
        _label.Text = Prefix + text;
    }
}
