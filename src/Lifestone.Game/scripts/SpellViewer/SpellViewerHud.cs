using System.Globalization;
using Godot;
using Lifestone.Client.Effects;
using MagicSchool = DatReaderWriter.Enums.MagicSchool;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// The spell viewer's 2D interface, built in code: a searchable list of every spell in the SpellTable (name, id or school),
/// the showcase buttons, Cast / Replay / Fizzle, a speed slider (0.25×–2×), toggles, an info panel, a chat-style event log
/// and the caption with the spell's words.
/// </summary>
public partial class SpellViewerHud : CanvasLayer
{
    private static readonly Color Gold = new(0.93f, 0.80f, 0.45f);
    private static readonly Color Parchment = new(0.92f, 0.89f, 0.80f);

    private readonly List<(uint Id, string Name, MagicSchool School)> _all = new();
    private readonly List<uint> _filtered = new();
    private readonly Queue<string> _logLines = new();
    private Control _panel = null!;
    private LineEdit _search = null!;
    private ItemList _list = null!;
    private CheckBox _fizzle = null!;
    private CheckBox _pause = null!;
    private HSlider _speed = null!;
    private Label _speedLabel = null!;
    private Label _info = null!;
    private RichTextLabel _log = null!;
    private Label _captionTitle = null!;
    private Label _captionWords = null!;
    private uint? _selected;

    /// <summary>(spell id, fizzle).</summary>
    public event Action<uint, bool>? CastRequested;

    public event Action? ReplayRequested;

    public event Action<double>? SpeedChanged;

    public event Action<bool>? DrudgeToggled;

    public event Action<bool>? StaticsToggled;

    public event Action<bool>? ParticlesToggled;

    public event Action<bool>? PauseToggled;

    /// <summary>Builds the controls. <paramref name="showPanels"/> false keeps only the caption (captures).</summary>
    public void Build(IEnumerable<(uint Id, string Name, MagicSchool School)> spells, IReadOnlyList<ShowcaseEntry> showcase, SpellViewerOptions options, bool showPanels)
    {
        _all.AddRange(spells);
        Layer = 10;

        // Left panel: spells.
        _panel = new PanelContainer
        {
            Name = "SpellPanel",
            Position = new Vector2(10, 10),
            CustomMinimumSize = new Vector2(330, 0),
            Size = new Vector2(330, 700),
            Visible = showPanels,
        };
        _panel.AddThemeStyleboxOverride("panel", PanelStyle());
        AddChild(_panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(v);
        v.AddChild(Title("Lifestone spell viewer"));

        _search = new LineEdit { PlaceholderText = "Search spells: name, id or school", ClearButtonEnabled = true };
        _search.TextChanged += _ => Filter();
        v.AddChild(_search);
        _list = new ItemList { CustomMinimumSize = new Vector2(310, 250), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.ItemSelected += index => _selected = _filtered[(int)index];
        _list.ItemActivated += index =>
        {
            _selected = _filtered[(int)index];
            CastRequested?.Invoke(_selected.Value, _fizzle.ButtonPressed);
        };
        v.AddChild(_list);

        var buttons = new HBoxContainer();
        v.AddChild(buttons);
        var cast = new Button { Text = "Cast", CustomMinimumSize = new Vector2(80, 0) };
        cast.Pressed += () =>
        {
            if (_selected is { } id)
            {
                CastRequested?.Invoke(id, _fizzle.ButtonPressed);
            }
        };
        buttons.AddChild(cast);
        var replay = new Button { Text = "Replay", CustomMinimumSize = new Vector2(80, 0) };
        replay.Pressed += () => ReplayRequested?.Invoke();
        buttons.AddChild(replay);
        _fizzle = new CheckBox { Text = "Fizzle" };
        buttons.AddChild(_fizzle);

        var speedRow = new HBoxContainer();
        v.AddChild(speedRow);
        speedRow.AddChild(new Label { Text = "Speed" });
        _speed = new HSlider { MinValue = 0.25, MaxValue = 2.0, Step = 0.05, Value = options.Speed, CustomMinimumSize = new Vector2(180, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _speedLabel = new Label { Text = SpeedText(options.Speed), CustomMinimumSize = new Vector2(48, 0) };
        _speed.ValueChanged += value =>
        {
            _speedLabel.Text = SpeedText(value);
            SpeedChanged?.Invoke(value);
        };
        speedRow.AddChild(_speed);
        speedRow.AddChild(_speedLabel);

        var toggles = new GridContainer { Columns = 2 };
        v.AddChild(toggles);
        toggles.AddChild(Toggle("Drudge", options.ShowDrudge, b => DrudgeToggled?.Invoke(b)));
        toggles.AddChild(Toggle("Town statics", options.Statics, b => StaticsToggled?.Invoke(b)));
        toggles.AddChild(Toggle("Particles", true, b => ParticlesToggled?.Invoke(b)));
        _pause = Toggle("Pause (Space)", false, b => PauseToggled?.Invoke(b));
        toggles.AddChild(_pause);

        v.AddChild(Title("Showcase"));
        var grid = new GridContainer { Columns = 2 };
        v.AddChild(grid);
        foreach (var entry in showcase)
        {
            var e = entry;
            var b = new Button { Text = e.Label, CustomMinimumSize = new Vector2(150, 0), ClipText = true };
            b.Pressed += () =>
            {
                SelectSpell(e.SpellId);
                CastRequested?.Invoke(e.SpellId, e.Fizzle);
            };
            grid.AddChild(b);
        }

        v.AddChild(new Label
        {
            Text = "RMB drag: orbit   Wheel: zoom   MMB: raise/lower   Home: reset",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(310, 0),
        });

        // Right: info.
        var infoPanel = new PanelContainer
        {
            Name = "InfoPanel",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -390,
            OffsetRight = -10,
            OffsetTop = 10,
            Visible = showPanels,
        };
        infoPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        AddChild(infoPanel);
        _info = new Label { CustomMinimumSize = new Vector2(360, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _info.AddThemeFontSizeOverride("font_size", 12);
        infoPanel.AddChild(_info);

        // Bottom right: event log.
        var logPanel = new PanelContainer
        {
            Name = "LogPanel",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -470,
            OffsetRight = -10,
            OffsetTop = -160,
            OffsetBottom = -10,
            Visible = showPanels,
        };
        logPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        AddChild(logPanel);
        _log = new RichTextLabel { ScrollFollowing = true, BbcodeEnabled = false, FitContent = false };
        _log.AddThemeFontSizeOverride("normal_font_size", 13);
        _log.AddThemeColorOverride("default_color", Parchment);
        logPanel.AddChild(_log);

        // Caption (bottom left; also in captures).
        var caption = new VBoxContainer
        {
            Name = "Caption",
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = showPanels ? 360 : 24,
            OffsetTop = -96,
            OffsetBottom = -16,
            OffsetRight = 900,
        };
        AddChild(caption);
        _captionTitle = new Label();
        _captionTitle.AddThemeFontSizeOverride("font_size", 30);
        _captionTitle.AddThemeColorOverride("font_color", Gold);
        _captionTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        _captionTitle.AddThemeConstantOverride("outline_size", 6);
        caption.AddChild(_captionTitle);
        _captionWords = new Label();
        _captionWords.AddThemeFontSizeOverride("font_size", 20);
        _captionWords.AddThemeColorOverride("font_color", Parchment);
        _captionWords.AddThemeColorOverride("font_outline_color", Colors.Black);
        _captionWords.AddThemeConstantOverride("outline_size", 5);
        caption.AddChild(_captionWords);

        if (!showPanels)
        {
            var mark = new Label { Text = "Lifestone · spell viewer · Holtburg", Position = new Vector2(20, 14) };
            mark.AddThemeFontSizeOverride("font_size", 16);
            mark.AddThemeColorOverride("font_color", new Color(Parchment, 0.85f));
            mark.AddThemeColorOverride("font_outline_color", Colors.Black);
            mark.AddThemeConstantOverride("outline_size", 4);
            AddChild(mark);
        }

        Filter();
        if (options.Spell is { } spell)
        {
            SelectSpell(spell);
        }
    }

    public void SetInfo(string text) => _info.Text = text;

    /// <summary>Flips the Pause toggle (raises <see cref="PauseToggled"/>).</summary>
    public void TogglePause() => _pause.ButtonPressed = !_pause.ButtonPressed;

    public void SetCaption(string title, string words)
    {
        _captionTitle.Text = title;
        _captionWords.Text = words;
    }

    public void AddLog(string line)
    {
        _logLines.Enqueue(line);
        while (_logLines.Count > 60)
        {
            _logLines.Dequeue();
        }

        _log.Text = string.Join('\n', _logLines);
    }

    public void ClearLog()
    {
        _logLines.Clear();
        _log.Text = string.Empty;
    }

    /// <summary>Selects (and scrolls to) a spell in the list, clearing a search that hides it.</summary>
    public void SelectSpell(uint id)
    {
        _selected = id;
        var index = _filtered.IndexOf(id);
        if (index < 0)
        {
            _search.Text = string.Empty;
            Filter();
            index = _filtered.IndexOf(id);
        }

        if (index >= 0)
        {
            _list.Select(index);
            _list.EnsureCurrentIsVisible();
        }
    }

    private void Filter()
    {
        var query = _search.Text.Trim();
        _list.Clear();
        _filtered.Clear();
        foreach (var (id, name, school) in _all)
        {
            if (query.Length > 0
                && !name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !id.ToString(CultureInfo.InvariantCulture).StartsWith(query, StringComparison.Ordinal)
                && !school.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _filtered.Add(id);
            _list.AddItem($"{id,5}  {name}  ·  {SchoolName(school)}");
        }

        if (_selected is { } selected && _filtered.IndexOf(selected) is var i and >= 0)
        {
            _list.Select(i);
        }
    }

    /// <summary>Short school names for the list.</summary>
    public static string SchoolName(MagicSchool school) => school switch
    {
        MagicSchool.WarMagic => "War",
        MagicSchool.LifeMagic => "Life",
        MagicSchool.CreatureEnchantment => "Creature",
        MagicSchool.ItemEnchantment => "Item",
        MagicSchool.VoidMagic => "Void",
        _ => school.ToString(),
    };

    private static string SpeedText(double speed) => $"{speed:0.00}×";

    private static CheckBox Toggle(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Text = text, ButtonPressed = on };
        box.Toggled += pressed => changed(pressed);
        return box;
    }

    private static Label Title(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Gold);
        label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color(0.07f, 0.06f, 0.05f, 0.82f),
        BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f),
        BorderWidthBottom = 1,
        BorderWidthTop = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        ContentMarginLeft = 10,
        ContentMarginRight = 10,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };
}
