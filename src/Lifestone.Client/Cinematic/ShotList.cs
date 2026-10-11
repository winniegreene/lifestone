using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Cinematic;

/// <summary>Which scene records a shot.</summary>
public enum ShotScene
{
    /// <summary>The main scene: the streamed world, flown by <c>CinematicDirector</c>.</summary>
    World,

    /// <summary>The spell viewer: a cast on its Holtburg stage, filmed by <c>SpellCinematic</c>.</summary>
    Spell,
}

/// <summary>
/// One key of a shot as written in the shot list. World shots use <see cref="Position"/> with either
/// <see cref="Yaw"/>/<see cref="Pitch"/> or <see cref="Look"/>; spell shots use <see cref="Yaw"/>, <see cref="Pitch"/>,
/// <see cref="Distance"/>, <see cref="Focus"/> and <see cref="Offset"/>.
/// </summary>
public sealed record ShotKey
{
    /// <summary><c>t</c>: seconds from the shot start, or null.</summary>
    public double? Time { get; init; }

    /// <summary><c>pos</c>: [east, north, up] metres from the shot's anchor.</summary>
    public Vec3d? Position { get; init; }

    /// <summary><c>ground</c>: <see cref="Position"/>'s up is metres above the ground under it (else above the anchor).</summary>
    public bool PositionAboveGround { get; init; }

    /// <summary><c>look</c>: [east, north, up] metres from the anchor to look at.</summary>
    public Vec3d? Look { get; init; }

    /// <summary><c>lookGround</c>: <see cref="Look"/>'s up is metres above the ground under it.</summary>
    public bool LookAboveGround { get; init; }

    /// <summary><c>yaw</c>: compass heading in degrees (0 north, 90 east).</summary>
    public double? Yaw { get; init; }

    /// <summary><c>pitch</c>: degrees, positive looks up.</summary>
    public double? Pitch { get; init; }

    /// <summary><c>fov</c>: vertical field of view in degrees.</summary>
    public double? Fov { get; init; }

    /// <summary><c>focus</c> (spell shots): caster, target, mid, hands or projectile.</summary>
    public string? Focus { get; init; }

    /// <summary><c>offset</c> (spell shots): metres added to the focus point (AC axes).</summary>
    public Vec3d? Offset { get; init; }

    /// <summary><c>distance</c> (spell shots): metres from the focus.</summary>
    public double? Distance { get; init; }
}

/// <summary>One shot of a shot list (see <see cref="ShotList"/> for the file format).</summary>
public sealed record Shot
{
    public string Id { get; init; } = string.Empty;

    public ShotScene Scene { get; init; } = ShotScene.World;

    /// <summary>The lower-third caption (tools\hero-video.ps1 draws it), or null.</summary>
    public string? Caption { get; init; }

    /// <summary>Where the shot is (world shots): a teleport name, an interior key, a landblock, a cell (optionally @x,y,z) or map coordinates.</summary>
    public string? Start { get; init; }

    public double Duration { get; init; }

    /// <summary><c>time</c>: day fraction at the start (0.5 = Midsong), or null for the run's default.</summary>
    public double? DayFraction { get; init; }

    /// <summary><c>timeEnd</c>: day fraction at the end (a time-lapse), or null.</summary>
    public double? DayFractionEnd { get; init; }

    /// <summary><c>timeSpeed</c>: game seconds per second (default 1, retail's rate).</summary>
    public double? TimeSpeed { get; init; }

    /// <summary><c>dayGroup</c>: the Region day group (weather) to show (default 0, Sunny).</summary>
    public int DayGroup { get; init; }

    public double Fov { get; init; } = ShotList.DefaultFov;

    public Easing Ease { get; init; } = Easing.Linear;

    public PathTiming Timing { get; init; } = PathTiming.Auto;

    /// <summary>Seconds rendered (and not recorded) after the place is complete, so living statics are under way.</summary>
    public double Preroll { get; init; } = ShotList.DefaultPreroll;

    /// <summary>Outdoors: the eye is kept at least this many metres above the ground (and a closer pass is logged).</summary>
    public double Clearance { get; init; } = ShotList.DefaultClearance;

    public IReadOnlyList<ShotKey> Keys { get; init; } = [];

    /// <summary>Spell shots: the spell id.</summary>
    public uint? Spell { get; init; }

    /// <summary>Spell shots: make the cast fizzle.</summary>
    public bool Fizzle { get; init; }

    /// <summary>
    /// Spell shots: seconds into the shot at which the spell is cast; negative casts that long before the first frame
    /// (during the pre-roll), so the film can open part-way through a long wind-up.
    /// </summary>
    public double CastAt { get; init; }

    /// <summary>Spell shots: the follow filter's time constant in seconds (0 = none).</summary>
    public double Smooth { get; init; }

    /// <summary>Spell shots: show the spell words over the caster's head (a viewer aid; off in films).</summary>
    public bool Words { get; init; }

    /// <summary>Extra game arguments for this shot's run (tools\hero-video.ps1), after the list's own.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>The time speed in force: <see cref="TimeSpeed"/>, or the time-lapse rate for <see cref="DayFractionEnd"/>.</summary>
    public double EffectiveTimeSpeed(double dayLengthSeconds) =>
        DayFractionEnd is { } end
            ? TimeLapse.Speed(DayFraction ?? ShotList.DefaultDayFraction, end, Duration, dayLengthSeconds)
            : TimeSpeed ?? 1.0;

    /// <summary>Recorded frames at <paramref name="fps"/> (the duration rounded to whole frames, at least one).</summary>
    public int FrameCount(int fps) => Math.Max(1, (int)Math.Round(Duration * fps, MidpointRounding.AwayFromZero));
}

/// <summary>
/// A shot list: the shots of a film and how to render them. A JSON file (comments and trailing commas allowed), read
/// by the cinematic directors (<c>--cinematic=FILE</c>) and by tools\hero-video.ps1, which also reads the editing
/// fields (cards, captions, crossfade) and passes over the ones meant for it only.
/// </summary>
/// <remarks>
/// <para><b>Top level</b>: <c>title</c>, <c>fps</c> (60), <c>width</c>/<c>height</c> (1920×1080), <c>render</c>
/// {<c>msaa</c> 0|2|4|8, <c>screenSpaceAA</c> off|fxaa|smaa, <c>scale3d</c>}, <c>preroll</c>, <c>clearance</c>,
/// <c>args</c>, <c>shots</c>; for the script: <c>crossfade</c>, <c>font</c>, <c>titleCard</c>, <c>endCard</c>,
/// <c>poster</c>, <c>captionStyle</c>, <c>note</c>.</para>
/// <para><b>A shot</b>: <c>id</c>, <c>scene</c> world|spell, <c>caption</c>, <c>start</c>, <c>duration</c>,
/// <c>time</c>, <c>timeEnd</c> or <c>timeSpeed</c>, <c>dayGroup</c>, <c>fov</c>, <c>ease</c>, <c>timing</c>
/// auto|arc|uniform|keys, <c>preroll</c>, <c>clearance</c>, <c>keys</c>, <c>args</c>, <c>note</c>; spell shots
/// add <c>spell</c>, <c>fizzle</c>, <c>castAt</c>, <c>smooth</c>, <c>words</c>; for the script: <c>captionIn</c>,
/// <c>captionOut</c>, <c>captionPosition</c> (bottom or top), <c>transition</c> (fade, fadeblack or cut into the shot).</para>
/// <para><b>A key</b>: <c>t</c>, <c>pos</c> [east, north, up] from the anchor (the start location's spot, or with
/// <c>"ground": true</c> up above the ground there), <c>yaw</c>/<c>pitch</c> or <c>look</c> [east, north, up]
/// (<c>lookGround</c>), <c>fov</c>; spell keys: <c>yaw</c>, <c>pitch</c>, <c>distance</c>, <c>focus</c>,
/// <c>offset</c> [x, y, z].</para>
/// </remarks>
public sealed record ShotList
{
    public const int DefaultFps = 60;
    public const double DefaultFov = 60.0;
    public const double DefaultPreroll = 1.5;
    public const double DefaultClearance = 2.0;
    public const double DefaultDayFraction = 0.5;

    public string Title { get; init; } = string.Empty;

    public int Fps { get; init; } = DefaultFps;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>MSAA sample count for the 3D view (0 = off).</summary>
    public int Msaa { get; init; } = 4;

    /// <summary>Screen-space anti-aliasing: off, fxaa or smaa.</summary>
    public string ScreenSpaceAA { get; init; } = "off";

    /// <summary>3D render scale (above 1 supersamples).</summary>
    public double Scale3D { get; init; } = 1.0;

    public IReadOnlyList<string> Args { get; init; } = [];

    public IReadOnlyList<Shot> Shots { get; init; } = [];

    /// <summary>The shot with this id, or null.</summary>
    public Shot? Find(string id) => Shots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> TopKeys = ["title", "fps", "width", "height", "render", "preroll", "clearance", "args", "shots", "crossfade", "font", "titleCard", "endCard", "poster", "captionStyle", "note"];
    private static readonly HashSet<string> RenderKeys = ["msaa", "screenSpaceAA", "scale3d"];
    private static readonly HashSet<string> ShotKeys = ["id", "scene", "caption", "captionIn", "captionOut", "captionPosition", "transition", "note", "start", "duration", "time", "timeEnd", "timeSpeed", "dayGroup", "fov", "ease", "timing", "preroll", "clearance", "keys", "args", "spell", "fizzle", "castAt", "smooth", "words"];
    private static readonly HashSet<string> KeyKeys = ["t", "pos", "ground", "look", "lookGround", "yaw", "pitch", "fov", "focus", "offset", "distance", "note"];

    /// <summary>The stage points a spell shot's keys may orbit.</summary>
    public static readonly IReadOnlyList<string> SpellFocusNames = ["caster", "target", "mid", "hands", "projectile"];

    /// <summary>
    /// Parses and checks a shot list. Never throws for bad input: every problem is listed in <paramref name="errors"/>
    /// (with its JSON path) and the result is null when there is any.
    /// </summary>
    public static ShotList? Parse(string json, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors.Add($"not valid JSON: {ex.Message}");
            return null;
        }

        using (document)
        {
            var before = errors.Count;
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("the shot list must be a JSON object");
                return null;
            }

            var r = new Reader(errors);
            r.Unknown(root, TopKeys, "$");
            var preroll = r.Number(root, "preroll", "$", DefaultPreroll, min: 0, max: 60);
            var clearance = r.Number(root, "clearance", "$", DefaultClearance, min: 0, max: 1000);
            var list = new ShotList
            {
                Title = r.String(root, "title", "$") ?? string.Empty,
                Fps = (int)r.Number(root, "fps", "$", DefaultFps, min: 1, max: 240, integer: true),
                Width = (int)r.Number(root, "width", "$", 1920, min: 16, max: 16384, integer: true),
                Height = (int)r.Number(root, "height", "$", 1080, min: 16, max: 16384, integer: true),
                Args = r.Strings(root, "args", "$"),
            };
            if (root.TryGetProperty("render", out var render))
            {
                if (render.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("$.render: expected an object");
                }
                else
                {
                    r.Unknown(render, RenderKeys, "$.render");
                    var msaa = (int)r.Number(render, "msaa", "$.render", 4, min: 0, max: 8, integer: true);
                    if (msaa is not (0 or 2 or 4 or 8))
                    {
                        errors.Add($"$.render.msaa: {msaa} is not 0, 2, 4 or 8");
                    }

                    var ssaa = r.String(render, "screenSpaceAA", "$.render") ?? "off";
                    if (ssaa is not ("off" or "fxaa" or "smaa"))
                    {
                        errors.Add($"$.render.screenSpaceAA: '{ssaa}' is not off, fxaa or smaa");
                    }

                    list = list with { Msaa = msaa, ScreenSpaceAA = ssaa, Scale3D = r.Number(render, "scale3d", "$.render", 1.0, min: 0.25, max: 4.0) };
                }
            }

            var shots = new List<Shot>();
            if (!root.TryGetProperty("shots", out var shotsElement) || shotsElement.ValueKind != JsonValueKind.Array || shotsElement.GetArrayLength() == 0)
            {
                errors.Add("$.shots: expected a non-empty array of shots");
            }
            else
            {
                var index = 0;
                foreach (var element in shotsElement.EnumerateArray())
                {
                    if (ParseShot(element, $"$.shots[{index}]", r, preroll, clearance) is { } shot)
                    {
                        shots.Add(shot);
                    }

                    index++;
                }

                foreach (var duplicate in shots.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                {
                    errors.Add($"$.shots: the id '{duplicate.Key}' is used {duplicate.Count()} times");
                }
            }

            return errors.Count == before ? list with { Shots = shots } : null;
        }
    }

    private static Shot? ParseShot(JsonElement e, string path, Reader r, double defaultPreroll, double defaultClearance)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            r.Errors.Add($"{path}: expected an object");
            return null;
        }

        var before = r.Errors.Count;
        r.Unknown(e, ShotKeys, path);
        var id = r.String(e, "id", path);
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            r.Errors.Add($"{path}.id: expected a file-safe id (letters, digits, '-' and '_')");
            id = "?";
        }

        path = $"{path} ('{id}')";
        var sceneText = r.String(e, "scene", path) ?? "world";
        var scene = sceneText switch
        {
            "world" => ShotScene.World,
            "spell" => ShotScene.Spell,
            _ => (ShotScene?)null,
        };
        if (scene is null)
        {
            r.Errors.Add($"{path}.scene: '{sceneText}' is not world or spell");
            scene = ShotScene.World;
        }

        var easeText = r.String(e, "ease", path);
        if (!Easing.TryParse(easeText, out var ease, out var easeError))
        {
            r.Errors.Add($"{path}.ease: {easeError}");
        }

        var timingText = r.String(e, "timing", path) ?? "auto";
        var timing = timingText switch
        {
            "auto" => PathTiming.Auto,
            "arc" => PathTiming.Arc,
            "uniform" => PathTiming.Uniform,
            "keys" => PathTiming.Keys,
            _ => (PathTiming?)null,
        };
        if (timing is null)
        {
            r.Errors.Add($"{path}.timing: '{timingText}' is not auto, arc, uniform or keys");
            timing = PathTiming.Auto;
        }

        var shot = new Shot
        {
            Id = id!,
            Scene = scene.Value,
            Caption = r.String(e, "caption", path),
            Start = r.String(e, "start", path),
            Duration = r.Number(e, "duration", path, double.NaN, min: 0.05, max: 600),
            DayFraction = r.OptionalNumber(e, "time", path, 0, 1),
            DayFractionEnd = r.OptionalNumber(e, "timeEnd", path, 0, 1),
            TimeSpeed = r.OptionalNumber(e, "timeSpeed", path, 0, 4096),
            DayGroup = (int)r.Number(e, "dayGroup", path, 0, min: 0, max: 63, integer: true),
            Fov = r.Number(e, "fov", path, DefaultFov, min: 5, max: 150),
            Ease = ease,
            Timing = timing.Value,
            Preroll = r.Number(e, "preroll", path, defaultPreroll, min: 0, max: 60),
            Clearance = r.Number(e, "clearance", path, defaultClearance, min: 0, max: 1000),
            Args = r.Strings(e, "args", path),
            Spell = r.OptionalNumber(e, "spell", path, 1, 1_000_000, integer: true) is { } spell ? (uint)spell : null,
            Fizzle = r.Bool(e, "fizzle", path),
            CastAt = r.Number(e, "castAt", path, 0, min: -30, max: 600),
            Smooth = r.Number(e, "smooth", path, 0, min: 0, max: 10),
            Words = r.Bool(e, "words", path),
        };
        _ = r.OptionalNumber(e, "captionIn", path, 0, 600);
        _ = r.OptionalNumber(e, "captionOut", path, 0, 600);
        _ = r.String(e, "note", path);
        if (r.String(e, "captionPosition", path) is { } position && position is not ("bottom" or "top"))
        {
            r.Errors.Add($"{path}.captionPosition: '{position}' is not bottom or top");
        }

        if (r.String(e, "transition", path) is { } transition && transition is not ("fade" or "fadeblack" or "cut"))
        {
            r.Errors.Add($"{path}.transition: '{transition}' is not fade, fadeblack or cut");
        }

        if (double.IsNaN(shot.Duration))
        {
            r.Errors.Add($"{path}.duration: required (seconds)");
        }

        if (shot.DayFractionEnd is not null && shot.TimeSpeed is not null)
        {
            r.Errors.Add($"{path}: give timeEnd (a time-lapse to a day fraction) or timeSpeed, not both");
        }

        var keys = new List<ShotKey>();
        if (!e.TryGetProperty("keys", out var keysElement) || keysElement.ValueKind != JsonValueKind.Array || keysElement.GetArrayLength() == 0)
        {
            r.Errors.Add($"{path}.keys: expected a non-empty array of camera keys");
        }
        else
        {
            var k = 0;
            foreach (var key in keysElement.EnumerateArray())
            {
                if (ParseKey(key, $"{path}.keys[{k}]", r, shot.Scene) is { } parsed)
                {
                    keys.Add(parsed);
                }

                k++;
            }
        }

        shot = shot with { Keys = keys };
        if (shot.Scene == ShotScene.World && string.IsNullOrWhiteSpace(shot.Start))
        {
            r.Errors.Add($"{path}.start: a world shot needs a start location");
        }

        if (shot.Scene == ShotScene.Spell && shot.Spell is null)
        {
            r.Errors.Add($"{path}.spell: a spell shot needs a spell id");
        }

        if (keys.Count > 0 && keys.Any(k => k.Time is not null))
        {
            if (keys.Any(k => k.Time is null))
            {
                r.Errors.Add($"{path}.keys: give every key a time 't', or none");
            }
            else
            {
                for (var i = 1; i < keys.Count; i++)
                {
                    if (!(keys[i].Time > keys[i - 1].Time))
                    {
                        r.Errors.Add($"{path}.keys[{i}].t: key times must increase");
                    }
                }

                if (keys[0].Time is not 0.0)
                {
                    r.Errors.Add($"{path}.keys[0].t: the first key must be at t = 0");
                }

                if (!double.IsNaN(shot.Duration) && keys[^1].Time > shot.Duration + 1e-9)
                {
                    r.Errors.Add($"{path}.keys: the last key (t = {keys[^1].Time}) is after the end of the shot ({shot.Duration} s)");
                }
            }
        }
        else if (shot.Timing == PathTiming.Keys)
        {
            r.Errors.Add($"{path}.timing: 'keys' needs a time 't' on every key");
        }

        return r.Errors.Count == before ? shot : null;
    }

    private static ShotKey? ParseKey(JsonElement e, string path, Reader r, ShotScene scene)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            r.Errors.Add($"{path}: expected an object");
            return null;
        }

        var before = r.Errors.Count;
        r.Unknown(e, KeyKeys, path);
        _ = r.String(e, "note", path);
        var key = new ShotKey
        {
            Time = r.OptionalNumber(e, "t", path, 0, 600),
            Position = r.Vector(e, "pos", path),
            PositionAboveGround = r.Bool(e, "ground", path),
            Look = r.Vector(e, "look", path),
            LookAboveGround = r.Bool(e, "lookGround", path),
            Yaw = r.OptionalNumber(e, "yaw", path, -3600, 3600),
            Pitch = r.OptionalNumber(e, "pitch", path, -89, 89),
            Fov = r.OptionalNumber(e, "fov", path, 5, 150),
            Focus = r.String(e, "focus", path),
            Offset = r.Vector(e, "offset", path),
            Distance = r.OptionalNumber(e, "distance", path, 0.05, 500),
        };
        if (scene == ShotScene.World)
        {
            if (key.Position is null)
            {
                r.Errors.Add($"{path}.pos: a world key needs a position [east, north, up]");
            }

            if (key.Yaw is null && key.Look is null)
            {
                r.Errors.Add($"{path}: a world key needs a yaw (with an optional pitch) or a look-at point");
            }

            if (key.Focus is not null || key.Distance is not null || key.Offset is not null)
            {
                r.Errors.Add($"{path}: focus, distance and offset are for spell shots");
            }
        }
        else
        {
            if (key.Yaw is null || key.Pitch is null || key.Distance is null)
            {
                r.Errors.Add($"{path}: a spell key needs yaw, pitch and distance");
            }

            var focus = key.Focus ?? "mid";
            if (!SpellFocusNames.Contains(focus))
            {
                r.Errors.Add($"{path}.focus: '{focus}' is not one of {string.Join(", ", SpellFocusNames)}");
            }

            if (key.Position is not null || key.Look is not null)
            {
                r.Errors.Add($"{path}: pos and look are for world shots (spell keys orbit a focus)");
            }

            key = key with { Focus = focus };
        }

        return r.Errors.Count == before ? key : null;
    }

    /// <summary>Typed property readers that report problems with their JSON path.</summary>
    private sealed class Reader(List<string> errors)
    {
        public List<string> Errors { get; } = errors;

        public void Unknown(JsonElement e, HashSet<string> known, string path)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (!known.Contains(p.Name))
                {
                    Errors.Add($"{path}.{p.Name}: unknown property (known: {string.Join(", ", known)})");
                }
            }
        }

        public string? String(JsonElement e, string name, string path)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (v.ValueKind != JsonValueKind.String)
            {
                Errors.Add($"{path}.{name}: expected a string");
                return null;
            }

            return v.GetString();
        }

        public bool Bool(JsonElement e, string name, string path)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return v.GetBoolean();
            }

            Errors.Add($"{path}.{name}: expected true or false");
            return false;
        }

        public double Number(JsonElement e, string name, string path, double fallback, double min, double max, bool integer = false) =>
            OptionalNumber(e, name, path, min, max, integer) ?? fallback;

        public double? OptionalNumber(JsonElement e, string name, string path, double min, double max, bool integer = false)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))
            {
                Errors.Add($"{path}.{name}: expected a number");
                return null;
            }

            if (d < min || d > max)
            {
                Errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}.{name}: {d} is outside [{min}, {max}]"));
                return null;
            }

            if (integer && Math.Abs(d - Math.Round(d)) > 1e-9)
            {
                Errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}.{name}: {d} is not a whole number"));
                return null;
            }

            return d;
        }

        public Vec3d? Vector(JsonElement e, string name, string path)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3 || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
            {
                Errors.Add($"{path}.{name}: expected three numbers [x, y, z]");
                return null;
            }

            var a = v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            if (a.Any(x => !double.IsFinite(x) || Math.Abs(x) > 100_000))
            {
                Errors.Add($"{path}.{name}: coordinates must be finite and within 100 km");
                return null;
            }

            return new Vec3d(a[0], a[1], a[2]);
        }

        public IReadOnlyList<string> Strings(JsonElement e, string name, string path)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            {
                return [];
            }

            if (v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            {
                Errors.Add($"{path}.{name}: expected an array of strings");
                return [];
            }

            return v.EnumerateArray().Select(x => x.GetString()!).ToArray();
        }
    }
}

/// <summary>Time-lapse arithmetic for shots that run the Dereth clock from one day fraction to another.</summary>
public static class TimeLapse
{
    /// <summary>
    /// Game seconds per second that take the clock from day fraction <paramref name="start"/> to
    /// <paramref name="end"/> in <paramref name="duration"/> seconds, forwards (an end at or before the start crosses
    /// midnight into the next day).
    /// </summary>
    public static double Speed(double start, double end, double duration, double dayLengthSeconds)
    {
        if (!(duration > 0) || !(dayLengthSeconds > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration and day length must be positive.");
        }

        return Span(start, end) * dayLengthSeconds / duration;
    }

    /// <summary>The forward span in days from day fraction <paramref name="start"/> to <paramref name="end"/>, in (0, 1].</summary>
    public static double Span(double start, double end)
    {
        var span = end - start;
        return span <= 0 ? span + 1.0 : span;
    }
}
