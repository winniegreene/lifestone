using Godot;

namespace Lifestone.Game.UI;

/// <summary>
/// The look of Lifestone's M1 tool panels: dark translucent panels with a thin bronze border and parchment text, in
/// the spirit of the retail client's window chrome (no retail art is used).
/// </summary>
public static class UiStyle
{
    public static readonly Color Background = new(0.055f, 0.045f, 0.035f, 0.86f);
    public static readonly Color Border = new(0.60f, 0.48f, 0.27f, 0.95f);
    public static readonly Color Text = new(0.94f, 0.89f, 0.75f);
    public static readonly Color DimText = new(0.72f, 0.66f, 0.52f);
    public static readonly Color Accent = new(0.98f, 0.80f, 0.40f);
    public static readonly Color ButtonFill = new(0.16f, 0.12f, 0.08f, 0.95f);
    public static readonly Color ButtonHover = new(0.27f, 0.20f, 0.11f, 0.95f);
    public static readonly Color ButtonPressed = new(0.42f, 0.31f, 0.14f, 0.95f);

    /// <summary>A panel container with the Lifestone frame.</summary>
    public static PanelContainer Panel(string name)
    {
        var panel = new PanelContainer { Name = name, MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Box(Background, Border, 1, 8));
        return panel;
    }

    public static Label Label(string text = "", int size = 14, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        return label;
    }

    public static Button Button(string text, string tooltip = "", bool toggle = false)
    {
        var button = new Button { Text = text, TooltipText = tooltip, ToggleMode = toggle, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeStyleboxOverride("normal", Box(ButtonFill, Border, 1, 4));
        button.AddThemeStyleboxOverride("hover", Box(ButtonHover, Border, 1, 4));
        button.AddThemeStyleboxOverride("pressed", Box(ButtonPressed, Accent, 1, 4));
        button.AddThemeStyleboxOverride("hover_pressed", Box(ButtonPressed, Accent, 1, 4));
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Accent);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    public static LineEdit LineEdit(string placeholder)
    {
        var edit = new LineEdit { PlaceholderText = placeholder, ClearButtonEnabled = true };
        edit.AddThemeStyleboxOverride("normal", Box(new Color(0.02f, 0.02f, 0.015f, 0.9f), Border, 1, 5));
        edit.AddThemeStyleboxOverride("focus", Box(new Color(0.02f, 0.02f, 0.015f, 0.9f), Accent, 1, 5));
        edit.AddThemeColorOverride("font_color", Text);
        edit.AddThemeColorOverride("font_placeholder_color", DimText);
        edit.AddThemeFontSizeOverride("font_size", 14);
        return edit;
    }

    public static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int margin)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = margin,
            ContentMarginRight = margin,
            ContentMarginTop = Math.Max(2, margin / 2),
            ContentMarginBottom = Math.Max(2, margin / 2),
        };
        box.SetBorderWidthAll(borderWidth);
        return box;
    }
}
