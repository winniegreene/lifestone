using System.Globalization;
using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Game.Player;

namespace Lifestone.Game.UI;

/// <summary>
/// The teleport panel (T): search ACE's towns and points of interest and the curated dungeons and building interiors,
/// jump to one, or type retail map coordinates.
/// </summary>
/// <remarks>
/// While a text field has focus the fly camera ignores the keyboard (it polls WASD), and Esc gives the focus back.
/// Enter in the search box jumps to the best match; double-click or Enter on a row jumps to that row. Typing a kind of
/// place ("dungeon", "interior") lists every place of that kind. Indoor places go through the interiors
/// (<see cref="Navigator.PlaceIndoors"/>).
/// </remarks>
public partial class TeleportPanel : PanelContainer
{
    private Navigator _navigator = null!;
    private FlyCamera _camera = null!;
    private LineEdit _search = null!;
    private ItemList _results = null!;
    private LineEdit _coordinates = null!;
    private Label _status = null!;
    private IReadOnlyList<TeleportDestination> _shown = [];
    private bool _cameraBlocked;

    /// <summary>Toggle button shown when the panel is closed.</summary>
    public Button? OpenButton { get; private set; }

    public static TeleportPanel Create(Navigator navigator, FlyCamera camera, CanvasLayer layer)
    {
        var panel = new TeleportPanel();
        panel.Build(navigator, camera, layer);
        return panel;
    }

    private void Build(Navigator navigator, FlyCamera camera, CanvasLayer layer)
    {
        _navigator = navigator;
        _camera = camera;
        Name = "TeleportPanel";
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Background, UiStyle.Border, 1, 10));
        MouseFilter = MouseFilterEnum.Stop;
        Position = new Vector2(12, 150);
        CustomMinimumSize = new Vector2(330, 0);
        Visible = false;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        AddChild(box);

        var header = new HBoxContainer();
        var title = UiStyle.Label("Teleport", 16, UiStyle.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = UiStyle.Button("x", "Close (T)");
        close.Pressed += () => Toggle(false);
        header.AddChild(close);
        box.AddChild(header);

        _search = UiStyle.LineEdit("Search towns, places, dungeons");
        _search.TextChanged += Refresh;
        _search.TextSubmitted += _ => JumpTo(_shown.FirstOrDefault());
        box.AddChild(_search);

        _results = new ItemList
        {
            CustomMinimumSize = new Vector2(306, 300),
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
        };
        _results.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0.02f, 0.02f, 0.015f, 0.85f), UiStyle.Border, 1, 4));
        _results.AddThemeColorOverride("font_color", UiStyle.Text);
        _results.AddThemeColorOverride("font_selected_color", UiStyle.Accent);
        _results.AddThemeFontSizeOverride("font_size", 13);
        _results.ItemActivated += index => JumpTo(index < _shown.Count ? _shown[(int)index] : null);
        box.AddChild(_results);

        box.AddChild(UiStyle.Label("Map coordinates", 12, UiStyle.DimText));
        var row = new HBoxContainer();
        _coordinates = UiStyle.LineEdit("42.1N 33.6E");
        _coordinates.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _coordinates.TextSubmitted += _ => JumpToCoordinates();
        row.AddChild(_coordinates);
        var go = UiStyle.Button("Go", "Jump to the 24 m cell retail shows these coordinates for");
        go.Pressed += JumpToCoordinates;
        row.AddChild(go);
        box.AddChild(row);

        _status = UiStyle.Label(string.Empty, 12, UiStyle.DimText);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(306, 0);
        box.AddChild(_status);
        box.AddChild(UiStyle.Label("Sources: ACE points of interest; curated interiors", 10, UiStyle.DimText));

        OpenButton = UiStyle.Button("Teleport [T]", "Search towns, places and dungeons, or type map coordinates");
        OpenButton.Position = new Vector2(12, 120);
        OpenButton.Pressed += () => Toggle(true);
        layer.AddChild(OpenButton);
        Refresh(string.Empty);
    }

    /// <summary>Opens or closes the panel.</summary>
    public void Toggle(bool open)
    {
        Visible = open;
        if (OpenButton is not null)
        {
            OpenButton.Visible = !open;
        }

        if (open)
        {
            _search.GrabFocus();
            _search.SelectAll();
        }
        else
        {
            GetViewport()?.GuiReleaseFocus();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.T })
        {
            Toggle(!Visible);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } && GetViewport().GuiGetFocusOwner() is LineEdit)
        {
            GetViewport().GuiReleaseFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // The fly camera polls WASD/QE; keep it still while someone types.
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        if (typing != _cameraBlocked)
        {
            _cameraBlocked = typing;
            _camera.InputEnabled = !typing;
        }
    }

    private void Refresh(string query)
    {
        _shown = _navigator.Catalog.Search(query);
        _results.Clear();
        foreach (var d in _shown)
        {
            // Outdoors: retail map coordinates; indoors (no coordinates in retail): dungeon, building interior or indoors.
            var index = _results.AddItem(string.Create(CultureInfo.InvariantCulture, $"{d.Name}    {d.Where}"));
            _results.SetItemTooltip(index, string.Create(
                CultureInfo.InvariantCulture,
                $"{d.Location.Position}  heading {d.Location.HeadingDegrees:0}°\n{(d.Aliases.Count > 0 ? "also: " + string.Join(", ", d.Aliases) + "\n" : string.Empty)}{d.Source}"));
        }

        _status.Text = _shown.Count == 0 ? "No match." : string.Create(CultureInfo.InvariantCulture, $"{_shown.Count} places. Enter jumps to the first.");
    }

    private void JumpTo(TeleportDestination? destination)
    {
        if (destination is null)
        {
            _status.Text = "No match.";
            return;
        }

        _navigator.Teleport(destination);
        _status.Text = $"Jumped to {destination.Label}.";
        GetViewport().GuiReleaseFocus();
    }

    private void JumpToCoordinates()
    {
        if (!MapCoordinateParser.TryParse(_coordinates.Text, out var map, out var error))
        {
            _status.Text = error ?? "Bad coordinates.";
            return;
        }

        _status.Text = _navigator.TeleportToMap(map) ? $"Jumped to {map}." : $"{map} is off the map.";
        GetViewport().GuiReleaseFocus();
    }
}
