using System.Globalization;
using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Game.Core;

namespace Lifestone.Game.Player;

/// <summary>
/// Top-left position readout: landblock, cell, landblock-local coordinates, heading, retail map coordinates outdoors,
/// Dereth game time (calendar from the Region; the clock is the local real-time clock until M2's server time) and the
/// camera mode.
/// </summary>
/// <remarks>
/// Inside an EnvCell (the camera's <see cref="FlyCamera.ViewerCell"/>, from the interiors' cell tracker) the landblock,
/// cell and local coordinates are the cell's own and no map coordinates are shown, as retail's radar shows none there
/// (<see cref="PositionReadout"/>).
/// </remarks>
public partial class PositionHud : Label
{
    private FlyCamera? _camera;
    private double _sinceUpdate = double.MaxValue;

    public void Initialize(FlyCamera camera) => _camera = camera;

    public override void _Ready()
    {
        AddThemeColorOverride("font_color", Colors.White);
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 4);
        AddThemeFontSizeOverride("font_size", 15);
    }

    public override void _Process(double delta)
    {
        _sinceUpdate += delta;
        if (_camera is null || _sinceUpdate < 0.1)
        {
            return;
        }

        _sinceUpdate = 0;
        Text = Describe(_camera);
    }

    /// <summary>The readout text for a camera.</summary>
    public static string Describe(FlyCamera camera)
    {
        var cell = camera.ViewerCell;
        var lines = new List<string>(6);
        lines.AddRange(PositionReadout.Describe(camera.WorldPosition, cell?.CellId ?? 0u, cell?.SeenOutside));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Heading {camera.Heading:0.0}°  pitch {camera.Pitch:0.0}°   {(camera.WalkMode ? "WALK" : "FLY")} {(camera.WalkMode ? FlyCamera.WalkSpeed : camera.BaseSpeed):0} m/s"));

        var services = GameServices.Instance;
        if (services.Dats is not null)
        {
            var date = services.Sky.Calendar.ToDate(services.Clock.Now);
            lines.Add($"{date.DateString}, {date.TimeString}");
        }

        return string.Join('\n', lines);
    }
}
