using System.Globalization;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// Command-line options of scenes/SpellViewer.tscn (user arguments after <c>--</c>; with tools\godot.ps1 use
/// <c>-GameArgs:'...'</c>). The shared options (<c>--dat-dir</c>, <c>--time-of-day</c>, <c>--max-mip</c>, ...) are
/// GameServices'; arguments neither parser knows are reported by both as warnings and otherwise ignored.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--scene=spellviewer</term><description>From the main scene: open the spell viewer (Main.cs).</description></item>
/// <item><term>--spell=ID</term><description>Cast this spell once the scene is ready (and select it).</description></item>
/// <item><term>--fizzle</term><description>Make --spell fizzle.</description></item>
/// <item><term>--speed=X</term><description>Playback speed, 0.25-2 (default 1).</description></item>
/// <item><term>--showcase</term><description>Cast every showcase spell in turn with a scripted camera, then quit (record it with Godot's --write-movie).</description></item>
/// <item><term>--showcase-only=a,b</term><description>Restrict --showcase to these showcase indices (0-based).</description></item>
/// <item><term>--screenshot=PATH</term><description>With --shots: save PNGs named PATH-&lt;seconds&gt;.png; without: one PNG after --frames frames.</description></item>
/// <item><term>--shots=T1,T2,...</term><description>Seconds after the --spell cast at which to save screenshots, then quit.</description></item>
/// <item><term>--frames=N</term><description>Frames before a plain --screenshot (default 30).</description></item>
/// <item><term>--fixed-step</term><description>Advance the stage by exactly 1/30 s per frame (deterministic captures; implied by --shots).</description></item>
/// <item><term>--camera=YAW,PITCH,DIST</term><description>Orbit camera: compass heading it looks along, pitch (negative = down), distance in metres.</description></item>
/// <item><term>--focus=caster|target|mid</term><description>What the camera orbits (default mid: between the two, 1.1 m up).</description></item>
/// <item><term>--no-drudge</term><description>Do not draw the Drudge Skulker (it is still simulated as the target).</description></item>
/// <item><term>--no-statics</term><description>Do not load Holtburg's LandBlockInfo statics and buildings.</description></item>
/// <item><term>--no-ui</term><description>Hide the panels (captions stay).</description></item>
/// <item><term>--seed=N</term><description>Random seed of the effects (default 1).</description></item>
/// </list>
/// </remarks>
public sealed record SpellViewerOptions
{
    /// <summary>The main-scene switch (<c>--scene=spellviewer</c>).</summary>
    public const string SceneArgument = "--scene=spellviewer";

    public uint? Spell { get; init; }

    public bool Fizzle { get; init; }

    public double Speed { get; init; } = 1.0;

    public bool Showcase { get; init; }

    public IReadOnlyList<int>? ShowcaseOnly { get; init; }

    public string? ScreenshotPath { get; init; }

    public IReadOnlyList<double> ShotTimes { get; init; } = [];

    public int Frames { get; init; } = 30;

    public bool FixedStep { get; init; }

    public (float Yaw, float Pitch, float Distance)? Camera { get; init; }

    /// <summary>What the camera orbits: <c>caster</c>, <c>target</c> or <c>mid</c> (default).</summary>
    public string Focus { get; init; } = "mid";

    public bool ShowDrudge { get; init; } = true;

    public bool Statics { get; init; } = true;

    public bool Ui { get; init; } = true;

    public int Seed { get; init; } = 1;

    /// <summary>True when the run ends by itself.</summary>
    public bool IsAutomated => Showcase || ScreenshotPath is not null;

    /// <summary>True when the main scene was asked to open the spell viewer.</summary>
    public static bool RequestedBy(IEnumerable<string> args) => args.Any(a => a.Trim() == SceneArgument);

    /// <summary>
    /// Parses the user arguments. Never throws for bad input: a value of one of the viewer's options that does not parse
    /// is reported in <paramref name="errors"/> and the option keeps its default (an automated run then fails fast,
    /// <c>SpellViewer._Ready</c>).
    /// </summary>
    public static SpellViewerOptions Parse(IEnumerable<string> args, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(errors);
        var o = new SpellViewerOptions();
        foreach (var raw in args)
        {
            var arg = raw.Trim();
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            var name = eq < 0 ? arg : arg[..eq];
            var value = eq < 0 ? string.Empty : arg[(eq + 1)..].Trim('"');
            switch (name)
            {
                case "--spell":
                    if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spell))
                    {
                        o = o with { Spell = spell };
                    }
                    else
                    {
                        errors.Add($"--spell: '{value}' is not a spell id");
                    }

                    break;
                case "--fizzle":
                    o = o with { Fizzle = true };
                    break;
                case "--speed":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && double.IsFinite(speed))
                    {
                        o = o with { Speed = Math.Clamp(speed, 0.25, 2.0) };
                    }
                    else
                    {
                        errors.Add($"--speed: '{value}' is not a number");
                    }

                    break;
                case "--showcase":
                    o = o with { Showcase = true };
                    break;
                case "--showcase-only":
                    if (TryParseList(value, s => (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i), i), out var indices))
                    {
                        o = o with { ShowcaseOnly = indices };
                    }
                    else
                    {
                        errors.Add($"--showcase-only: '{value}' is not a comma-separated list of showcase indices");
                    }

                    break;
                case "--screenshot":
                    o = o with { ScreenshotPath = value };
                    break;
                case "--shots":
                    if (TryParseList(value, s => (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && double.IsFinite(t), t), out var times))
                    {
                        o = o with { ShotTimes = times.Order().ToArray(), FixedStep = true };
                    }
                    else
                    {
                        errors.Add($"--shots: '{value}' is not a comma-separated list of seconds");
                    }

                    break;
                case "--frames":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
                    {
                        o = o with { Frames = Math.Max(1, frames) };
                    }
                    else
                    {
                        errors.Add($"--frames: '{value}' is not a frame count");
                    }

                    break;
                case "--fixed-step":
                    o = o with { FixedStep = true };
                    break;
                case "--camera":
                    var parts = value.Split(',');
                    if (parts.Length == 3
                        && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw)
                        && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
                    {
                        o = o with { Camera = (yaw, pitch, distance) };
                    }
                    else
                    {
                        errors.Add($"--camera: '{value}' is not YAW,PITCH,DIST");
                    }

                    break;
                case "--focus":
                    if (value is "caster" or "target" or "mid")
                    {
                        o = o with { Focus = value };
                    }
                    else
                    {
                        errors.Add($"--focus: '{value}' is not caster, target or mid");
                    }

                    break;
                case "--no-drudge":
                    o = o with { ShowDrudge = false };
                    break;
                case "--no-statics":
                    o = o with { Statics = false };
                    break;
                case "--no-ui":
                    o = o with { Ui = false };
                    break;
                case "--seed":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                    {
                        o = o with { Seed = seed };
                    }
                    else
                    {
                        errors.Add($"--seed: '{value}' is not an integer");
                    }

                    break;
                case "--scene":
                    break;
                default:
                    // GameServices' own options (--dat-dir, --time-of-day, ...) and anything else: not ours.
                    break;
            }
        }

        return o;
    }

    /// <summary>A non-empty comma-separated list whose every item parses.</summary>
    private static bool TryParseList<T>(string value, Func<string, (bool Ok, T Value)> parse, out T[] items)
    {
        var raw = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        items = new T[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var (ok, item) = parse(raw[i]);
            if (!ok)
            {
                items = [];
                return false;
            }

            items[i] = item;
        }

        return raw.Length > 0;
    }
}
