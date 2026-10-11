using System.Globalization;
using Lifestone.Data.Terrain;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// Command-line options of scenes/TerrainPreview.tscn (user arguments after <c>--</c>; with tools\godot.ps1 pass them
/// through <c>-GameArgs:'...'</c>, the screenshot through <c>-Screenshot</c>/<c>-Frames</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--center=A9B4</c>: centre landblock (hex, <c>0x</c> optional). Default Holtburg.</item>
/// <item><c>--size=5</c>: N × N landblocks around the centre (odd; default 5).</item>
/// <item><c>--time=0.5</c>: day fraction (default 0.5, Midsong: retail's midday); <c>--day-group=0</c>.</item>
/// <item><c>--look-at=x,y</c>: camera target in AC metres from the centre landblock's south-west corner (default 96,96).</item>
/// <item><c>--distance=m</c>, <c>--pitch=deg</c> (down from horizontal), <c>--yaw=deg</c> (compass heading the camera
/// looks towards; 0 = north), <c>--fov=deg</c>.</item>
/// <item><c>--debug-view=N</c> (see <see cref="TerrainDebugView"/>), <c>--max-mip=F</c> (default 3, retail),
/// <c>--no-fog</c>, <c>--retail-normals</c> (retail per-block normals; seamless is the default, <c>--seamless-normals</c> still accepted).</item>
/// <item><c>--screenshot=PATH</c>, <c>--frames=N</c>: save the view after N frames and quit (godot.ps1 convention).</item>
/// <item><c>--perf</c>: log build timings and render statistics.</item>
/// <item><c>--golden[=DIR]</c>: run the golden comparison against the CPU reference compositor and quit (exit code 0 =
/// within tolerance, 2 = outside, 1 = error); <c>--golden-blocks=A9B4,7E62,7AC2,1FB1,1EB1</c>, <c>--golden-size=1000</c>.
/// DIR defaults to the repository's <c>docs/screenshots/m1</c>; a relative DIR is taken from the repository root (Godot
/// runs in <c>src/Lifestone.Game</c>), and it may be percent-encoded (<c>%20</c> for a space, <c>%25</c> for <c>%</c>)
/// because tools\godot.ps1 splits its game arguments on whitespace.</item>
/// <item><c>--dat-dir=DIR</c>: dat folder.</item>
/// </list>
/// </remarks>
public sealed class TerrainPreviewOptions
{
    public LandblockCoord Center { get; set; } = LandblockCoord.Holtburg;

    public int Size { get; set; } = 5;

    public float TimeOfDay { get; set; } = 0.5f;

    public int DayGroup { get; set; }

    public System.Numerics.Vector2? LookAt { get; set; }

    public float? Distance { get; set; }

    public float Pitch { get; set; } = 28f;

    public float Yaw { get; set; }

    public float Fov { get; set; } = 60f;

    public TerrainDebugView DebugView { get; set; } = TerrainDebugView.Retail;

    public float MaxMip { get; set; } = 3f;

    public bool Fog { get; set; } = true;

    public bool SeamlessNormals { get; set; } = TerrainRenderer.DefaultNormalMode == TerrainNormalMode.SeamlessAcrossBlocks;

    public string? ScreenshotPath { get; set; }

    public int Frames { get; set; } = 30;

    public bool Perf { get; set; }

    public string? GoldenDirectory { get; set; }

    public IReadOnlyList<LandblockCoord> GoldenBlocks { get; set; } =
        [LandblockCoord.Holtburg, new LandblockCoord(0x7E, 0x62), new LandblockCoord(0x7A, 0xC2), new LandblockCoord(0x1F, 0xB1), new LandblockCoord(0x1E, 0xB1)];

    public int GoldenSize { get; set; } = 1000;

    public string? DatDirectory { get; set; }

    public static TerrainPreviewOptions Parse(IEnumerable<string> args)
    {
        var o = new TerrainPreviewOptions();
        foreach (var raw in args)
        {
            var arg = raw.Trim();
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            var key = eq < 0 ? arg : arg[..eq];
            var value = eq < 0 ? string.Empty : arg[(eq + 1)..].Trim('"');
            switch (key)
            {
                case "--center":
                    o.Center = ParseCoord(value);
                    break;
                case "--size":
                    o.Size = Math.Clamp(int.Parse(value, CultureInfo.InvariantCulture) | 1, 1, 31);
                    break;
                case "--time":
                    o.TimeOfDay = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--day-group":
                    o.DayGroup = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--look-at":
                    var parts = value.Split(',');
                    o.LookAt = new System.Numerics.Vector2(
                        float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case "--distance":
                    o.Distance = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--pitch":
                    o.Pitch = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--yaw":
                    o.Yaw = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--fov":
                    o.Fov = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--debug-view":
                    o.DebugView = (TerrainDebugView)int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--max-mip":
                    o.MaxMip = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--no-fog":
                    o.Fog = false;
                    break;
                case "--seamless-normals":
                    o.SeamlessNormals = true;
                    break;
                case "--retail-normals":
                    o.SeamlessNormals = false;
                    break;
                case "--screenshot":
                    o.ScreenshotPath = value;
                    break;
                case "--frames":
                    o.Frames = Math.Max(1, int.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case "--perf":
                    o.Perf = true;
                    break;
                case "--golden":
                    o.GoldenDirectory = RepositoryPath(value.Length > 0 ? Uri.UnescapeDataString(value) : DefaultGoldenDirectory);
                    break;
                case "--golden-blocks":
                    o.GoldenBlocks = value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ParseCoord).ToArray();
                    break;
                case "--golden-size":
                    o.GoldenSize = Math.Clamp(int.Parse(value, CultureInfo.InvariantCulture), 64, 4096);
                    break;
                case "--dat-dir":
                    o.DatDirectory = value;
                    break;
                default:
                    // Core options (--workers, --log-level, ...) are read by GameServices; unknown arguments and other
                    // scenes' options are reported there once, from the option registry (Lifestone.Client OptionCatalog).
                    break;
            }
        }

        return o;
    }

    /// <summary>Where the golden test writes by default, relative to the repository root (gitignored).</summary>
    public const string DefaultGoldenDirectory = "docs/screenshots/m1";

    /// <summary>
    /// An absolute path for <paramref name="path"/>: rooted paths unchanged, relative ones from the repository root (two
    /// levels above <c>res://</c>, i.e. <c>src/Lifestone.Game/..\..</c>), not from Godot's working directory.
    /// </summary>
    public static string RepositoryPath(string path)
    {
        if (System.IO.Path.IsPathRooted(path))
        {
            return System.IO.Path.GetFullPath(path);
        }

        var projectDir = Godot.ProjectSettings.GlobalizePath("res://");
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "..", path));
    }

    public static LandblockCoord ParseCoord(string text)
    {
        var hex = text.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length <= 4 ? new LandblockCoord((byte)(value >> 8), (byte)value) : LandblockCoord.FromId(value);
    }
}
