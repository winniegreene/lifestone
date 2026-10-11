using System.Globalization;
using System.Text;
using Godot;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// Golden test of the terrain shader against the CPU reference (<see cref="TerrainCompositor"/>, docs/terrain-shader.md
/// §8). Each landblock is rendered top-down with an orthographic camera into an off-screen <see cref="SubViewport"/>
/// (no MSAA, Linear tonemap) twice:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><see cref="TerrainDebugView.RawNearest"/>: the merged colour with nearest samplers at level 0, no light, no fog.
/// Compared with <see cref="TerrainCompositor.ComposeFloat"/> (what the shader computes) and
/// <see cref="TerrainCompositor.Compose"/> (retail's integer merge).</item>
/// <item><see cref="TerrainDebugView.LitNearest"/>: the same colour × the vertex light. Compared with ComposeFloat ×
/// <see cref="TerrainCompositor.InterpolateVertexLight"/> of <see cref="TerrainVertexLighting.Light"/> (retail
/// calc_lighting bytes, Gouraud-interpolated over the retail split).</item>
/// </list>
/// <para>
/// Every pixel centre maps to an exact cell UV (pixel (i, j) of an N² image covers AC x = (i + ½)·192/N and
/// y = 192 − (j + ½)·192/N). Pixels within 1.5 pixels of a cell edge (flat per-cell data changes there) and pixels whose
/// sampling position lies within <see cref="TexelEdgeEpsilon"/> texel of a texel boundary of any array (nearest
/// sampling is ambiguous there) are not compared.
/// </para>
/// <para>
/// <b>Tolerance</b> (docs/terrain-shader.md §8): |GPU − ComposeFloat| ≤ <see cref="FloatTolerance"/> levels per channel
/// on every compared pixel, and |GPU − Compose| ≤ <see cref="IntegerTolerance"/>. Output: PNGs (GPU, CPU, diff ×32)
/// and terrain-golden-report.txt in the output folder; exit code 0 = pass, 2 = fail, 1 = error.
/// </para>
/// </remarks>
public partial class TerrainGoldenRunner : Node
{
    /// <summary>Allowed difference to the float reference (levels of 255).</summary>
    public const int FloatTolerance = 2;

    /// <summary>Allowed difference to retail's integer merge (levels of 255; up to 5 truncating steps).</summary>
    public const int IntegerTolerance = 6;

    /// <summary>Pixels whose sampling position is closer than this to a texel boundary are skipped (texels).</summary>
    public const double TexelEdgeEpsilon = 0.03;

    private readonly TerrainRenderer? _renderer;
    private readonly SkyState? _sky;
    private readonly TerrainPreviewOptions? _options;
    private readonly Dictionary<int, TerrainImage> _colorImages = new();
    private readonly Dictionary<int, TerrainAlphaImage> _alphaImages = new();

    public TerrainGoldenRunner()
    {
    }

    public TerrainGoldenRunner(TerrainRenderer renderer, SkyState sky, TerrainPreviewOptions options)
    {
        _renderer = renderer;
        _sky = sky;
        _options = options;
        Name = "TerrainGolden";
    }

    public override async void _Ready()
    {
        if (_renderer is null || _sky is null || _options is null)
        {
            return;
        }

        var exitCode = 1;
        try
        {
            exitCode = await RunAsync(_renderer, _sky, _options) ? 0 : 2;
        }
        catch (Exception ex)
        {
            GD.PushError($"[terrain-golden] failed: {ex}");
        }

        GetTree().Quit(exitCode);
    }

    private async Task<bool> RunAsync(TerrainRenderer renderer, SkyState sky, TerrainPreviewOptions options)
    {
        var size = options.GoldenSize;
        var outDir = System.IO.Path.GetFullPath(options.GoldenDirectory!);
        System.IO.Directory.CreateDirectory(outDir);

        var viewport = new SubViewport
        {
            Name = "GoldenViewport",
            Size = new Vector2I(size, size),
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                TonemapExposure = 1f,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        });
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = TerrainConstants.LandblockSize,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Current = true,
        };
        viewport.AddChild(camera);
        var root = new Node3D { Name = "Landblock" };
        viewport.AddChild(root);

        // The render contract's global uniforms (as in the game), with world fog off for the comparison.
        TerrainRenderer.ApplySky(sky, fog: false);
        var environment = TerrainRenderer.LightingEnvironment(sky);
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Lifestone terrain golden test, {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Godot {Engine.GetVersionInfo()["string"]}, {RenderingServer.GetCurrentRenderingDriverName()} on {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine(CultureInfo.InvariantCulture, $"{size}x{size} px per landblock ({TerrainConstants.LandblockSize / size:0.000} m/px); sky day group {options.DayGroup} t={sky.TimeOfDay:0.###}");
        report.AppendLine(CultureInfo.InvariantCulture, $"tolerance: |gpu - ComposeFloat| <= {FloatTolerance}, |gpu - Compose| <= {IntegerTolerance} (per channel, levels of 255)");
        report.AppendLine();

        var pass = true;
        foreach (var coord in options.GoldenBlocks)
        {
            if (renderer.PrepareBlock(coord) is not { } block)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"{coord}: landblock missing");
                pass = false;
                continue;
            }

            renderer.AttachBlock(block, root);
            var top = block.Source.MaxHeight + 50f;
            camera.Near = 1f;
            camera.Far = (block.Source.MaxHeight - block.Source.MinHeight) + 200f;
            camera.LookAtFromPosition(
                AcToGodot.Position(new AcVector3(96f, 96f, top)),
                AcToGodot.Position(new AcVector3(96f, 96f, block.Source.MinHeight - 10f)),
                AcToGodot.Direction(new AcVector3(0f, 1f, 0f))); // screen up = north

            var lights = TerrainVertexLighting.Light(block.Source.GridNormals, environment);
            foreach (var view in new[] { TerrainDebugView.RawNearest, TerrainDebugView.LitNearest })
            {
                renderer.DebugView = view;
                for (var i = 0; i < 4; i++)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }

                var image = viewport.GetTexture().GetImage();
                if (image.GetFormat() != Image.Format.Rgba8)
                {
                    image.Convert(Image.Format.Rgba8);
                }

                var stats = Compare(renderer, block.Source, lights, view == TerrainDebugView.LitNearest, image, size, outDir);
                pass &= stats.Pass;
                var line = $"{coord} {(view == TerrainDebugView.RawNearest ? "raw" : "lit")}: {stats}";
                report.AppendLine(line);
                GD.Print($"[terrain-golden] {line}");
            }

            renderer.DetachBlock(coord);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        renderer.DebugView = TerrainDebugView.Retail;
        report.AppendLine();
        report.AppendLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
        var reportPath = System.IO.Path.Combine(outDir, "terrain-golden-report.txt");
        await System.IO.File.WriteAllTextAsync(reportPath, report.ToString());
        GD.Print($"[terrain-golden] {(pass ? "PASS" : "FAIL")}; report {reportPath}");
        return pass;
    }

    private GoldenStats Compare(
        TerrainRenderer renderer,
        TerrainRenderMesh mesh,
        TerrainVertexLight[] lights,
        bool lit,
        Image image,
        int size,
        string outDir)
    {
        var atlas = Pixels(renderer);
        var manifest = atlas.Manifest;
        var gpu = image.GetData();
        var (texels, alphas) = TerrainCompositor.NearestSamplers(ColorImage, AlphaImage);

        // Every texel grid the shader samples with nearest: colour layers at uv * tiling * size, alpha maps at uv * size.
        var scales = atlas.LayerTiling.Select(t => (double)t * atlas.ColorSize).Append(atlas.AlphaSize).Distinct().ToArray();
        var metersPerPixel = (double)TerrainConstants.LandblockSize / size;
        var edgeMargin = 1.5 * metersPerPixel / TerrainConstants.CellSize;

        var cpu = new byte[size * size * 3];
        var diffImage = new byte[size * size * 3];
        var rows = new GoldenStats[size];
        Parallel.For(0, size, j =>
        {
            var row = new GoldenStats();
            for (var i = 0; i < size; i++)
            {
                var x = (i + 0.5) * metersPerPixel;
                var y = TerrainConstants.LandblockSize - ((j + 0.5) * metersPerPixel);
                var cx = Math.Min((int)(x / TerrainConstants.CellSize), 7);
                var cy = Math.Min((int)(y / TerrainConstants.CellSize), 7);
                var u = (x / TerrainConstants.CellSize) - cx;
                var v = 1.0 - ((y / TerrainConstants.CellSize) - cy);
                var blend = mesh.Blend[cx, cy];

                var reference = TerrainCompositor.ComposeFloat(blend, manifest, (float)u, (float)v, texels, alphas);
                var integer = TerrainCompositor.Compose(blend, manifest, (float)u, (float)v, texels, alphas);
                var refRgb = new double[] { reference.X, reference.Y, reference.Z };
                var intRgb = new double[] { integer.R / 255.0, integer.G / 255.0, integer.B / 255.0 };
                if (lit)
                {
                    var light = TerrainCompositor.InterpolateVertexLight(lights, cx, cy, mesh.Mesh.CellSplitSWtoNE[(cx * 8) + cy], (float)u, (float)v);
                    refRgb[0] *= light.X;
                    refRgb[1] *= light.Y;
                    refRgb[2] *= light.Z;
                    intRgb[0] *= light.X;
                    intRgb[1] *= light.Y;
                    intRgb[2] *= light.Z;
                }

                var p = (j * size) + i;
                var maxDiff = 0;
                for (var c = 0; c < 3; c++)
                {
                    cpu[(p * 3) + c] = (byte)Math.Clamp(Math.Round(refRgb[c] * 255.0), 0, 255);
                }

                var excluded = u < edgeMargin || u > 1 - edgeMargin || v < edgeMargin || v > 1 - edgeMargin
                    || NearTexelEdge(u, scales) || NearTexelEdge(v, scales);
                if (excluded)
                {
                    row.Excluded++;
                    diffImage[p * 3] = 0;
                    diffImage[(p * 3) + 1] = 0;
                    diffImage[(p * 3) + 2] = 64;
                    continue;
                }

                for (var c = 0; c < 3; c++)
                {
                    var g = gpu[(p * 4) + c];
                    var dFloat = Math.Abs(g - (refRgb[c] * 255.0));
                    var dInt = Math.Abs(g - (intRgb[c] * 255.0));
                    row.Add(dFloat, dInt);
                    maxDiff = Math.Max(maxDiff, (int)Math.Round(dFloat));
                }

                var shade = (byte)Math.Min(255, maxDiff * 32);
                diffImage[p * 3] = shade;
                diffImage[(p * 3) + 1] = shade;
                diffImage[(p * 3) + 2] = shade;
            }

            rows[j] = row;
        });

        var stats = new GoldenStats();
        foreach (var r in rows)
        {
            stats.Merge(r);
        }

        var tag = $"terrain-golden-{mesh.Coord.X:X2}{mesh.Coord.Y:X2}-{(lit ? "lit" : "raw")}";
        image.SavePng(System.IO.Path.Combine(outDir, $"{tag}-gpu.png"));
        Image.CreateFromData(size, size, false, Image.Format.Rgb8, cpu).SavePng(System.IO.Path.Combine(outDir, $"{tag}-cpu.png"));
        Image.CreateFromData(size, size, false, Image.Format.Rgb8, diffImage).SavePng(System.IO.Path.Combine(outDir, $"{tag}-diff.png"));
        return stats;
    }

    /// <summary>The decoded atlas the reference compositor samples (the renderer must have been asked to keep it).</summary>
    private static TerrainAtlasPixels Pixels(TerrainRenderer renderer) =>
        renderer.AtlasPixels ?? throw new InvalidOperationException(
            "The golden test needs the CPU atlas: construct the TerrainRenderer with keepAtlasPixels: true.");

    private static bool NearTexelEdge(double t, double[] scales)
    {
        foreach (var s in scales)
        {
            var texel = t * s;
            var frac = texel - Math.Floor(texel);
            if (frac < TexelEdgeEpsilon || frac > 1 - TexelEdgeEpsilon)
            {
                return true;
            }
        }

        return false;
    }

    private TerrainImage ColorImage(int layer)
    {
        lock (_colorImages)
        {
            if (!_colorImages.TryGetValue(layer, out var image))
            {
                image = Pixels(_renderer!).ColorImage(layer);
                _colorImages[layer] = image;
            }

            return image;
        }
    }

    private TerrainAlphaImage AlphaImage(int layer)
    {
        lock (_alphaImages)
        {
            if (!_alphaImages.TryGetValue(layer, out var image))
            {
                image = Pixels(_renderer!).AlphaImage(layer);
                _alphaImages[layer] = image;
            }

            return image;
        }
    }

    /// <summary>Per-channel error statistics of one golden image.</summary>
    private sealed class GoldenStats
    {
        public long Compared;
        public long Excluded;
        public double MaxFloat;
        public double MaxInteger;
        public double SumFloat;
        public double SumSquareFloat;
        public long OverOne;
        public long OverTolerance;
        public long OverIntegerTolerance;
        public readonly long[] Histogram = new long[8]; // |diff| rounded: 0..6, 7+

        public bool Pass => Compared > 0 && MaxFloat <= FloatTolerance + 0.5 && MaxInteger <= IntegerTolerance + 0.5;

        public void Add(double dFloat, double dInt)
        {
            Compared++;
            MaxFloat = Math.Max(MaxFloat, dFloat);
            MaxInteger = Math.Max(MaxInteger, dInt);
            SumFloat += dFloat;
            SumSquareFloat += dFloat * dFloat;
            var rounded = (int)Math.Round(dFloat);
            Histogram[Math.Min(rounded, Histogram.Length - 1)]++;
            if (rounded > 1)
            {
                OverOne++;
            }

            if (rounded > FloatTolerance)
            {
                OverTolerance++;
            }

            if ((int)Math.Round(dInt) > IntegerTolerance)
            {
                OverIntegerTolerance++;
            }
        }

        public void Merge(GoldenStats other)
        {
            Compared += other.Compared;
            Excluded += other.Excluded;
            MaxFloat = Math.Max(MaxFloat, other.MaxFloat);
            MaxInteger = Math.Max(MaxInteger, other.MaxInteger);
            SumFloat += other.SumFloat;
            SumSquareFloat += other.SumSquareFloat;
            OverOne += other.OverOne;
            OverTolerance += other.OverTolerance;
            OverIntegerTolerance += other.OverIntegerTolerance;
            for (var i = 0; i < Histogram.Length; i++)
            {
                Histogram[i] += other.Histogram[i];
            }
        }

        public override string ToString()
        {
            var n = Math.Max(1, Compared);
            return string.Create(CultureInfo.InvariantCulture,
                $"{(Pass ? "PASS" : "FAIL")} channels compared {Compared} (pixels skipped {Excluded}); vs ComposeFloat: max {MaxFloat:0.00}, " +
                $"mean {SumFloat / n:0.000}, rms {Math.Sqrt(SumSquareFloat / n):0.000}, >1: {OverOne}, >{FloatTolerance}: {OverTolerance}; " +
                $"vs Compose (integer): max {MaxInteger:0.00}, >{IntegerTolerance}: {OverIntegerTolerance}; " +
                $"|diff| histogram 0..6,7+: [{string.Join(", ", Histogram)}]");
        }
    }
}
