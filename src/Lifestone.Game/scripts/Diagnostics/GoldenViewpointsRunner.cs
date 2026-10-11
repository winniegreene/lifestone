using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.UI;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Sky;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--golden-viewpoints[=DIR]</c>: renders the fixed <see cref="GoldenViewpoints"/> list to PNGs plus a manifest
/// (position, heading, pitch, field of view, game time, calendar, day group, light and fog, ACE teleport command) so the
/// same views can be captured in the retail client and compared (M1 definition of done item 11). Quits when done
/// (0 = every view captured).
/// </summary>
/// <remarks>
/// <para>Per viewpoint: stop the clock at the viewpoint's game time (the date is chosen so retail's own day group is the
/// one wanted), jump the camera there, hide every HUD and panel (<see cref="NavigationUi.SetHudVisible"/>), wait until
/// the place is complete (<see cref="CaptureReadiness"/>: streaming, objects and the interiors' cells) and the sky models
/// are built, render <c>--golden-frames</c> more frames (the retail fog and light cadence has refreshed by then) and at
/// least <see cref="CaptureReadiness.SettleSeconds"/> (living statics' flames and smoke reach their steady state; the
/// same settle as <c>--screenshot</c>), save.</para>
/// <para>Indoor views (<see cref="GoldenViewpoints.IsIndoor"/>) go through the interiors as a teleport-panel jump does
/// (<see cref="Navigator.PlaceIndoors"/>: on the floor point at eye height, the cell known at once), then turn to the
/// view's heading and pitch; their manifest entry gives the EnvCell, its landblock-local eye position and an
/// <c>@teleloc</c> to the floor point, and no map coordinates (retail shows none indoors).</para>
/// </remarks>
public partial class GoldenViewpointsRunner : Node
{
    private const double ViewTimeoutSeconds = 120.0;

    private readonly List<ManifestEntry> _manifest = new();
    private GameServices _services = null!;
    private WorldRoot _world = null!;
    private FlyCamera _camera = null!;
    private SkyController _sky = null!;
    private Navigator _navigator = null!;
    private CaptureReadiness _readiness = null!;
    private Func<string, bool> _save = null!;
    private Action<bool> _setUiVisible = null!;
    private IReadOnlyList<GoldenViewpoint> _views = [];
    private WorldLocation? _location;
    private string _directory = string.Empty;
    private int _index = -1;
    private int _framesReady;
    private double _readySeconds;
    private double _viewSeconds;
    private int _failures;
    private bool _done;

    public void Initialize(
        GameServices services,
        WorldRoot world,
        FlyCamera camera,
        SkyController sky,
        Navigator navigator,
        Func<string, bool> saveScreenshot,
        Action<bool> setUiVisible)
    {
        _services = services;
        _world = world;
        _camera = camera;
        _sky = sky;
        _navigator = navigator;
        _readiness = new CaptureReadiness(services, world);
        _save = saveScreenshot;
        _setUiVisible = setUiVisible;
        var options = sky.Options;
        // A relative folder is relative to the repository (the Godot process runs in src/Lifestone.Game).
        var directory = options.GoldenDirectory ?? SkyOptions.DefaultGoldenDirectory;
        var repository = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
        _directory = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(directory) ? directory : System.IO.Path.Combine(repository, directory));
        _views = options.GoldenOnly.Count == 0
            ? GoldenViewpoints.All
            : options.GoldenOnly.Select(id => GoldenViewpoints.Find(id) ?? throw new ArgumentException($"--golden-only: no viewpoint '{id}'")).ToArray();
        ProcessPriority = 30;
        _camera.InputEnabled = false;
        Log.Info("golden", "golden viewpoints", ("count", _views.Count), ("dir", _directory), ("frames", options.GoldenFrames));
        Next();
    }

    public override void _Process(double delta)
    {
        if (_done || _index < 0 || _index >= _views.Count)
        {
            return;
        }

        _viewSeconds += delta;
        var ready = _readiness.IsComplete && _sky.IsReady;
        if (!ready && _viewSeconds < ViewTimeoutSeconds)
        {
            _framesReady = 0;
            _readySeconds = 0;
            return;
        }

        if (!ready && _framesReady == 0)
        {
            Log.Warn("golden", "the place or the sky is not complete; capturing anyway", ("view", _views[_index].Id), ("seconds", _viewSeconds),
                ("state", _readiness.Describe()), ("sky_ready", _sky.IsReady));
        }

        _readySeconds += delta;
        if (++_framesReady < _sky.Options.GoldenFrames || _readySeconds < CaptureReadiness.SettleSeconds)
        {
            return;
        }

        Capture(_views[_index]);
        Next();
    }

    private void Next()
    {
        _index++;
        _framesReady = 0;
        _readySeconds = 0;
        _viewSeconds = 0;
        if (_index >= _views.Count)
        {
            Finish();
            return;
        }

        var view = _views[_index];
        _setUiVisible(false);
        _sky.SetDayGroup(null);
        _sky.Time.SetSpeed(0.0);
        _sky.SetGameTime(GoldenViewpoints.GameTimeFor(view.Time, _services.Sky));
        if (!GoldenViewpoints.TryResolveTarget(view.Target, _navigator.Catalog, out var location, out var error))
        {
            Log.Error("golden", "bad viewpoint target", ("view", view.Id), ("error", error));
            _failures++;
            Next();
            return;
        }

        _location = location;
        if (GoldenViewpoints.IsIndoor(location))
        {
            // As the teleport panel: on the floor point at eye height, in the cell; then the view's facing.
            var heading = view.HeadingDegrees ?? location.HeadingDegrees;
            _navigator.PlaceIndoors(location with { HeadingDegrees = heading }, view.Id);
            _camera.PlaceAt(_camera.WorldPosition, heading, view.PitchDegrees ?? 0.0);
            return;
        }

        var pose = GoldenViewpoints.PoseFor(view, location, _navigator.GroundAt);
        _navigator.Place(pose, view.Id);
    }

    private void Capture(GoldenViewpoint view)
    {
        var file = view.Id + ".png";
        var ok = _save(System.IO.Path.Combine(_directory, file));
        if (!ok)
        {
            _failures++;
        }

        var frame = _sky.Frame!;
        var date = frame.Date;
        var eye = _camera.WorldPosition;
        CellPosition cell;
        string map;
        string teleloc;
        var viewerCell = _navigator.ViewerCellId;
        if (AcCoordinates.IsEnvCell(viewerCell))
        {
            // Indoors: the EnvCell's own landblock frame (dungeon cells lie outside their square), no map coordinates.
            var origin = AcCoordinates.LandblockOrigin(Lifestone.Data.Terrain.LandblockCoord.FromId(viewerCell));
            cell = new CellPosition(viewerCell, new System.Numerics.Vector3((float)(eye.X - origin.X), (float)(eye.Y - origin.Y), (float)(eye.Z - origin.Z)));
            map = "-";
            teleloc = _location is { } floor && floor.Position.CellId == viewerCell
                ? GoldenViewpoints.AceTeleloc(floor.Position, _camera.Heading)
                : GoldenViewpoints.AceTeleloc(cell, _camera.Heading);
        }
        else
        {
            AcCoordinates.TryGetOutdoorCell(eye, out cell);
            map = AcCoordinates.TryGetMapCoordinates(eye, out var coordinates) ? coordinates.ToString() : "-";
            teleloc = GoldenViewpoints.AceTeleloc(eye, _camera.Heading);
        }

        // A dungeon cell (not seen_outside) is lit by retail's indoor ambient and the cells' burned-in static lights; the
        // sky's sun and fog do not reach it (CellManager::ChangePosition; docs/interiors.md). Building interiors
        // (seen_outside) keep the outdoor light.
        var dungeonCell = _navigator.Interiors?.Tracker?.Cell is { SeenOutside: false };
        var lighting = dungeonCell
            ? string.Create(CultureInfo.InvariantCulture, $"indoor: ambient {InteriorController.IndoorAmbientLevel:0.0#}, static lights; time of day does not apply")
            : null;
        var sun = frame.Lighting.SunDirection;
        _manifest.Add(new ManifestEntry
        {
            Id = view.Id,
            Title = view.Title,
            File = file,
            Captured = ok,
            Target = view.Target,
            Notes = view.Notes,
            CellId = $"0x{cell.CellId:X8}",
            Local = [Round(cell.Local.X), Round(cell.Local.Y), Round(cell.Local.Z)],
            Global = [Round(eye.X), Round(eye.Y), Round(eye.Z)],
            MapCoordinates = map,
            HeadingDegrees = Round(_camera.Heading),
            PitchDegrees = Round(_camera.Pitch),
            VerticalFovDegrees = Round(_camera.Camera.Fov),
            AceTeleloc = teleloc,
            GameTime = Round(_sky.Time.GameTime),
            Date = date.DateString,
            TimeOfDay = date.TimeString,
            DayFraction = Round(date.DayFractionExact),
            DayGroup = frame.DayGroup.Index,
            DayGroupName = frame.DayGroup.Name,
            SunElevationDegrees = Round(Math.Asin(Math.Clamp(sun.Z, -1f, 1f)) * 180.0 / Math.PI),
            SunHeadingDegrees = Round(AcCoordinates.HeadingOf(sun.X, sun.Y)),
            SunColor = frame.Lighting.SunColor.ToString(),
            SunBrightness = Round(frame.Lighting.SunIntensity),
            AmbientColor = frame.Lighting.AmbientColor.ToString(),
            AmbientLevel = Round(frame.Lighting.AmbientLevel),
            Fog = string.Create(CultureInfo.InvariantCulture, $"{frame.Fog.Start:0}-{frame.Fog.End:0} m {frame.Fog.Color}"),
            IndoorLighting = lighting,
            Resolution = $"{GetViewport().GetVisibleRect().Size.X:0}x{GetViewport().GetVisibleRect().Size.Y:0}",
        });
        Log.Info("golden", "captured", ("view", view.Id), ("ok", ok), ("cell", $"0x{cell.CellId:X8}"), ("map", map), ("time", $"{date.DateString} {date.TimeString}"), ("seconds", _viewSeconds));
    }

    private void Finish()
    {
        _done = true;
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(
                new Manifest { Generator = "Lifestone --golden-viewpoints (docs/navigation.md)", Viewpoints = _manifest },
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "manifest.json"), json, new UTF8Encoding(false));
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "manifest.md"), Markdown(), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("golden", "manifest not written", ("error", ex.Message));
            _failures++;
        }

        Log.Info("golden", "golden viewpoints done", ("captured", _manifest.Count(m => m.Captured)), ("failures", _failures), ("dir", _directory));
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Markdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Golden viewpoints");
        sb.AppendLine();
        sb.AppendLine("Rendered by `--golden-viewpoints`. To compare with retail: stand at the position (ACE admin `@teleloc` below), face the heading, set the same game time (the date's day group matches retail's), and use the same vertical field of view.");
        sb.AppendLine();
        sb.AppendLine("M1 draws what the dats hold. Server-spawned objects (lifestones, portals, doors, signs, wells, NPCs) arrive from ACE's CreateObject in M2, so they are missing here but present in retail; that is not a regression. Indoor views give the EnvCell and its landblock-local eye position, and their `@teleloc` names the floor point (retail shows no map coordinates indoors).");
        sb.AppendLine();
        sb.AppendLine("| View | Position | Heading / pitch | Time | Day group | Light and fog |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var m in _manifest)
        {
            var light = m.IndoorLighting ?? string.Create(
                CultureInfo.InvariantCulture,
                $"sun {m.SunElevationDegrees}° @ {m.SunHeadingDegrees}° {m.SunColor} x{m.SunBrightness}; ambient {m.AmbientColor} x{m.AmbientLevel}; fog {m.Fog}");
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| `{m.File}`<br>{m.Title} | {m.CellId} [{m.Local[0]} {m.Local[1]} {m.Local[2]}]<br>{m.MapCoordinates}<br>`{m.AceTeleloc}` | {m.HeadingDegrees}° / {m.PitchDegrees}°, fov {m.VerticalFovDegrees}° | {m.Date}, {m.TimeOfDay} (day {m.DayFraction}; game time {m.GameTime}) | {m.DayGroup} {m.DayGroupName} | {light} |"));
        }

        return sb.ToString();
    }

    private static double Round(double value) => Math.Round(value, 4);

    private sealed class Manifest
    {
        public string Generator { get; init; } = string.Empty;

        public List<ManifestEntry> Viewpoints { get; init; } = new();
    }

    private sealed class ManifestEntry
    {
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string File { get; init; } = string.Empty;

        public bool Captured { get; init; }

        public string Target { get; init; } = string.Empty;

        public string Notes { get; init; } = string.Empty;

        public string CellId { get; init; } = string.Empty;

        public double[] Local { get; init; } = [];

        public double[] Global { get; init; } = [];

        public string MapCoordinates { get; init; } = string.Empty;

        public double HeadingDegrees { get; init; }

        public double PitchDegrees { get; init; }

        public double VerticalFovDegrees { get; init; }

        public string AceTeleloc { get; init; } = string.Empty;

        public double GameTime { get; init; }

        public string Date { get; init; } = string.Empty;

        public string TimeOfDay { get; init; } = string.Empty;

        public double DayFraction { get; init; }

        public int DayGroup { get; init; }

        public string DayGroupName { get; init; } = string.Empty;

        public double SunElevationDegrees { get; init; }

        public double SunHeadingDegrees { get; init; }

        public string SunColor { get; init; } = string.Empty;

        public double SunBrightness { get; init; }

        public string AmbientColor { get; init; } = string.Empty;

        public double AmbientLevel { get; init; }

        public string Fog { get; init; } = string.Empty;

        /// <summary>For a dungeon cell: how it is lit instead of the sky's sun and fog (which the JSON still records); else null.</summary>
        public string? IndoorLighting { get; init; }

        public string Resolution { get; init; } = string.Empty;
    }
}
