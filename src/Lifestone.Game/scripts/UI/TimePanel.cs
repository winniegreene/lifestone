using System.Globalization;
using Godot;
using Lifestone.Client.Sky;
using Lifestone.Client.World;
using Lifestone.Game.World.Sky;

namespace Lifestone.Game.UI;

/// <summary>
/// The time of day panel (bottom right): the Dereth date and time as retail prints them
/// (<c>GameTime::GetDateTimeString</c>: "Morningthaw 14, 10 P.Y." and "Morntide-and-Half"), a slider over the current
/// day, speed buttons (pause, 1x retail, 60x, 600x), a "live" button back to the server clock, and the day group
/// (weather) with a debug override.
/// </summary>
public partial class TimePanel : PanelContainer
{
    private SkyController _sky = null!;
    private Label _date = null!;
    private Label _time = null!;
    private Label _details = null!;
    private HSlider _slider = null!;
    private OptionButton _weather = null!;
    private readonly List<(Button Button, double Speed)> _speedButtons = new();
    private bool _dragging;
    private bool _updating;
    private double _sinceText = double.MaxValue;

    public static TimePanel Create(SkyController sky)
    {
        var panel = new TimePanel();
        panel.Build(sky);
        return panel;
    }

    private void Build(SkyController sky)
    {
        _sky = sky;
        Name = "TimePanel";
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Background, UiStyle.Border, 1, 10));
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorTop = 1;
        AnchorBottom = 1;
        OffsetLeft = -392;
        OffsetRight = -12;
        OffsetTop = -156;
        OffsetBottom = -12;
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.Begin;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);

        var row1 = new HBoxContainer();
        _date = UiStyle.Label(size: 16, color: UiStyle.Accent);
        _date.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row1.AddChild(_date);
        _weather = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Day group (weather). Retail picks one per Dereth day (SkyDesc::CalcPresentDayGroup); a forced one is a viewer tool." };
        _weather.AddThemeFontSizeOverride("font_size", 12);
        _weather.AddItem("Weather: retail", 0);
        var groups = sky.Director.Sky.Sky.DayGroups;
        for (var g = 0; g < groups.Count; g++)
        {
            _weather.AddItem(string.Create(CultureInfo.InvariantCulture, $"{g}: {groups[g].Name}"), g + 1);
        }

        _weather.ItemSelected += index => _sky.SetDayGroup(index == 0 ? null : (int)index - 1);
        _weather.Selected = sky.Director.ForcedDayGroup is { } forced ? forced + 1 : 0;
        row1.AddChild(_weather);
        box.AddChild(row1);

        var row2 = new HBoxContainer();
        _time = UiStyle.Label(size: 15);
        _time.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row2.AddChild(_time);
        _details = UiStyle.Label(size: 11, color: UiStyle.DimText);
        _details.HorizontalAlignment = HorizontalAlignment.Right;
        row2.AddChild(_details);
        box.AddChild(row2);

        _slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.0005,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Time of day (the date, and so the retail day group, stays)",
            CustomMinimumSize = new Vector2(0, 18),
        };
        _slider.DragStarted += () => _dragging = true;
        _slider.DragEnded += _ =>
        {
            _dragging = false;
            SetFraction(_slider.Value);
        };
        _slider.ValueChanged += value =>
        {
            if (!_updating)
            {
                SetFraction(value);
            }
        };
        box.AddChild(_slider);

        var row3 = new HBoxContainer();
        row3.AddThemeConstantOverride("separation", 4);
        foreach (var speed in DerethTimeController.PresetSpeeds)
        {
            var label = speed == 0 ? "Pause" : string.Create(CultureInfo.InvariantCulture, $"{speed:0}x");
            var tip = speed switch
            {
                0 => "Stop the clock",
                1 => "Retail rate: one game second per second (a Dereth day lasts 2 h 7 min)",
                _ => string.Create(CultureInfo.InvariantCulture, $"Time-lapse: {speed:0} game seconds per second"),
            };
            var button = UiStyle.Button(label, tip, toggle: true);
            button.CustomMinimumSize = new Vector2(58, 0);
            var s = speed;
            button.Pressed += () => _sky.Time.SetSpeed(s);
            _speedButtons.Add((button, speed));
            row3.AddChild(button);
        }

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row3.AddChild(spacer);
        var live = UiStyle.Button("Live", "Back to the server clock (ACE time) at the retail rate");
        live.Pressed += () => _sky.Time.ResetToLive();
        row3.AddChild(live);
        box.AddChild(row3);
    }

    public override void _Process(double delta)
    {
        if (_sky?.Frame is not { } frame)
        {
            return;
        }

        if (!_dragging)
        {
            _updating = true;
            _slider.SetValueNoSignal(frame.TimeOfDay);
            _updating = false;
        }

        foreach (var (button, speed) in _speedButtons)
        {
            button.SetPressedNoSignal(_sky.Time.Speed == speed);
        }

        _sinceText += delta;
        if (_sinceText < 0.1)
        {
            return;
        }

        _sinceText = 0;
        var date = frame.Date; // formats retail's strings: ten times a second, not every frame
        _date.Text = date.DateString;
        _time.Text = string.Create(CultureInfo.InvariantCulture, $"{date.TimeString}{(date.IsNight ? "  (night)" : string.Empty)}");
        var sun = frame.Lighting.SunDirection;
        var elevation = Math.Asin(Math.Clamp(sun.Z, -1f, 1f)) * 180.0 / Math.PI;
        var heading = AcCoordinates.HeadingOf(sun.X, sun.Y);
        _details.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"day {date.DayFraction:0.000}  sun {elevation:0}° @ {heading:0}°\nfog {frame.Fog.Start:0}-{frame.Fog.End:0} m  {frame.DayGroup.Name}{(_sky.Director.ForcedDayGroup is null ? string.Empty : " (forced)")}");
    }

    private void SetFraction(double value) => _sky.Time.SetDayFraction(value);
}
