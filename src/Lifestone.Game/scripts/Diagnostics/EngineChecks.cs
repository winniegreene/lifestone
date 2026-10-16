using System.Collections;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Rendering;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;
using Lifestone.Data.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.World;
using AcVector2 = System.Numerics.Vector2;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--engine-checks</c>: verifies, in the running engine, the Godot behaviours the render path relies on and that
/// unit tests cannot reach, then quits with 0 when every check holds and 1 otherwise. Run it windowed
/// (<c>tools\godot.ps1 -Run -GameArgs:'--engine-checks'</c>); headless runs skip the render checks.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Zero normals cannot be stored.</b> A zero vertex normal reads back as a unit vector (octahedral encoding), so
/// shaders cannot test the normal's length (docs/render-contract.md §2.1).</item>
/// <item><b>The zero-normal flag survives.</b> <see cref="MeshFactory"/>'s CUSTOM0 (R float) flags read back unchanged.</item>
/// <item><b>Rendered lighting.</b> Four quads under a sun pointing at AC north (where Godot's decoded zero normal points),
/// ambient 0.2 and sun 0.5: a flagged zero-normal quad shows ambient only (51); a quad facing the sun shows ambient +
/// sun (179); a zero-normal quad <i>without</i> the flag shows 179 (the bug the flag fixes, and proof that a mesh without
/// CUSTOM0 is lit normally).</item>
/// <item><b>DXT1 alpha.</b> A DXT1 texture whose texels are all punch-through transparent, drawn blended over green: Godot
/// shows opaque black (it uploads FORMAT_DXT1 as BC1 without alpha, the reason for TextureCache's RGBA8 twin), and the
/// RenderingDevice format is <c>BC1_RGB</c>.</item>
/// <item><b>Stepwise attach and cancellation.</b> A real <see cref="LandblockStreamer"/> with a probe layer whose attach
/// takes several steps, drained one step per call, then cancelled part-way by moving the focus away: every attached
/// layer is detached exactly once, no step runs after its Detach, every started sequence is stopped (its
/// <c>finally</c> runs) and every prepared object is disposed exactly once.</item>
/// </list>
/// </remarks>
public partial class EngineChecks : Node
{
    private const int Tolerance = 3;
    private static readonly Vector2I ViewportSize = new(128, 32);

    private readonly List<(string Name, Outcome Outcome, string Detail)> _results = new();

    private enum Outcome
    {
        Pass,
        Fail,
        Info,
        Skipped,
    }

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var exitCode = 1;
        try
        {
            CheckZeroNormalReadback();
            CheckZeroNormalFlagReadback();
            CheckAmbientSound();
            await CheckStepwiseAttachAsync();
            if (DisplayServer.GetName() == "headless")
            {
                Record("render checks", Outcome.Skipped, "headless: no renderer; run windowed");
            }
            else
            {
                await RenderChecksAsync();
            }

            exitCode = _results.Any(r => r.Outcome == Outcome.Fail) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Log.Exception("checks", "engine checks crashed", ex);
        }

        Log.Info(
            "checks",
            exitCode == 0 ? "all engine checks hold" : "ENGINE CHECKS FAILED",
            ("pass", _results.Count(r => r.Outcome == Outcome.Pass)),
            ("fail", _results.Count(r => r.Outcome == Outcome.Fail)),
            ("info", _results.Count(r => r.Outcome == Outcome.Info)),
            ("skipped", _results.Count(r => r.Outcome == Outcome.Skipped)));
        Log.FlushToGodot();
        GetTree().Quit(exitCode);
    }

    private void Record(string name, Outcome outcome, string detail)
    {
        _results.Add((name, outcome, detail));
        var fields = new (string, object?)[] { ("check", name), ("result", outcome.ToString().ToUpperInvariant()), ("detail", detail) };
        if (outcome == Outcome.Fail)
        {
            Log.Error("checks", "check", fields);
        }
        else
        {
            Log.Info("checks", "check", fields);
        }
    }

    private async Task CheckStepwiseAttachAsync()
    {
        var queue = new MainThreadQueue(TimeSpan.Zero); // zero budget: every Drain runs exactly one step
        using var workers = new WorkerPool(2);
        var layer = new ProbeLayer();
        var parent = new Node3D { Name = "StreamerProbe" };
        AddChild(parent);
        var streamer = new LandblockStreamer(
            new StreamingSettings { LoadRadius = 1, MaxInFlight = 16 },
            [layer],
            workers,
            queue,
            coord =>
            {
                var root = new Node3D { Name = $"LB_{coord}" };
                parent.AddChild(root);
                return root;
            });

        static StreamingFocus FocusOn(int x, int y) => new(new GlobalPosition((x * 192.0) + 96.0, (y * 192.0) + 96.0, 0));

        // Load 9 landblocks, run a few single steps so some attach part-way, then fly away (cancel and unload them).
        streamer.Update(FocusOn(0x80, 0x80));
        for (var frame = 0; frame < 300 && (layer.PreparedCount < 9 || queue.Pending == 0); frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // Single steps until the second landblock's attach sequence is part-way (started, not finished).
        for (var i = 0; i < 500 && !(layer.SequencesStarted >= 2 && layer.SequencesStarted > layer.SequencesStopped && layer.StepsRun % 6 == 3); i++)
        {
            queue.Drain();
        }

        var attachedBeforeMove = layer.Attaches;
        streamer.Update(FocusOn(0x90, 0x90));
        for (var frame = 0; frame < 600 && !streamer.IsIdle; frame++)
        {
            queue.Budget = TimeSpan.FromMilliseconds(2);
            queue.Drain();
            streamer.Update(FocusOn(0x90, 0x90));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var idle = streamer.IsIdle;
        streamer.Reset();
        for (var i = 0; i < 100 && queue.Pending > 0; i++)
        {
            queue.Drain();
        }

        parent.QueueFree();
        var ok = idle
            && streamer.Stats.AbandonedMidAttach > 0
            && layer.Attaches == layer.Detaches
            && layer.DetachWithoutAttach == 0
            && layer.StepsAfterDetach == 0
            && layer.SequencesStarted == layer.SequencesStopped
            && layer.PreparedCount == layer.Disposed
            && layer.DoubleDisposed == 0;
        Record(
            "stepwise attach: cancellation keeps attach/detach ordered and frees everything",
            ok ? Outcome.Pass : Outcome.Fail,
            $"idle={idle} attached-before-move={attachedBeforeMove} abandoned-mid-attach={streamer.Stats.AbandonedMidAttach} attaches={layer.Attaches} " +
            $"detaches={layer.Detaches} detach-without-attach={layer.DetachWithoutAttach} steps-after-detach={layer.StepsAfterDetach} " +
            $"sequences started/stopped={layer.SequencesStarted}/{layer.SequencesStopped} prepared/disposed={layer.PreparedCount}/{layer.Disposed} double-disposed={layer.DoubleDisposed}");
    }

    /// <summary>
    /// M3 1.3 s2b: the outdoor ambient sound hears a draw. A minute of frames at Holtburg through the real region tables, terrain and
    /// sound tables onto a counting output (nothing reaches the sound card): the draws must read at least 1 outdoors and 0 indoors.
    /// The counters print as <c>ambient_draws=</c>, <c>ambient_played=</c>, <c>ambient_skipped=</c> and <c>ambient_heard=</c> (report-only for smoke).
    /// </summary>
    private void CheckAmbientSound()
    {
        const string name = "ambient sound draws";
        if (GameServices.Instance.Dats is not { } dats)
        {
            Record(name, Outcome.Skipped, "no dats");
            return;
        }

        try
        {
            var region = RegionSoundData.FromRegion(dats.Region);
            var terrain = new AmbientTerrainCache(dats, background: false); // the check has no frames to wait over: its reads run inside Get
            var coord = LandblockCoord.Holtburg;
            var cell = ((uint)coord.X << 24) | ((uint)coord.Y << 16) | 0x0021u;
            var listener = new CellFrame(cell, new AcFrame(new AcVector3(96f, 96f, 70f), System.Numerics.Quaternion.Identity));

            AmbientSoundPlayer Make(CountingOutput output) => new(
                region,
                dats.TerrainRegion.LandHeightTable,
                terrain,
                new RetailSoundPlayer(output, new DatAudioSource(dats), new RetailRandom(1), new MsvcCrtRandom(1)),
                new RetailRandom(2));

            var heard = new CountingOutput();
            var outdoor = Make(heard);
            var indoorOutput = new CountingOutput();
            var indoor = Make(indoorOutput);
            for (var frame = 0; frame < 3600; frame++)
            {
                var now = frame / 60.0;
                outdoor.Update(listener, outdoors: true, now);
                indoor.Update(listener, outdoors: false, now);
            }

            var ok = outdoor.Draws >= 1 && outdoor.Draws == outdoor.Played + outdoor.Skipped && indoor.Draws == 0 && indoorOutput.Plays == 0;
            Record(
                name,
                ok ? Outcome.Pass : Outcome.Fail,
                $"ambient_draws={outdoor.Draws} ambient_scans={outdoor.Scans} ambient_played={outdoor.Played} ambient_skipped={outdoor.Skipped} ambient_heard={heard.Plays} sounds={outdoor.ActiveSounds} tables={region.Tables.Count} indoor_draws={indoor.Draws} (a minute of frames at Holtburg; the sink's own roll and attenuation decide what is heard)");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            Record(name, Outcome.Fail, ex.Message);
        }
    }

    private sealed class CountingOutput : IAudioOutput
    {
        public int Plays { get; private set; }

        public void Play(in VoicePlay play) => Plays++;

        public void Stop(int voice)
        {
        }

        public bool IsPlaying(int voice) => false;
    }

    private void CheckZeroNormalReadback()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { Vector3.Zero, Vector3.Right, Vector3.Up };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Zero, new Vector3(0, 0.5f, 0), Vector3.Zero };
        using var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var normals = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var zeroBack = normals[0];
        var unit = Math.Abs(zeroBack.LengthSquared() - 1f) < 1e-3f;
        Record(
            "zero normal is not storable",
            unit ? Outcome.Pass : Outcome.Info,
            unit
                ? $"(0,0,0) reads back as {zeroBack} (unit length, AC north): shaders cannot detect zero normals, hence the CUSTOM0 flag"
                : $"(0,0,0) reads back as {zeroBack}: Godot now keeps zero normals; the CUSTOM0 flag still works, update render-contract.md §2.1");
    }

    private void CheckZeroNormalFlagReadback()
    {
        var data = Quad(0f, AcVector3.Zero, zeroFirstTwo: true);
        using var mesh = MeshFactory.Build(MeshFactory.Prepare(data));
        var format = (long)mesh.SurfaceGetFormat(0);
        var custom0 = (format >> (int)Mesh.ArrayFormat.FormatCustom0Shift) & (long)Mesh.ArrayFormat.FormatCustomMask;
        var flags = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
        var expected = new[] { 1f, 1f, 0f, 0f };
        var ok = custom0 == (long)Mesh.ArrayCustomFormat.RFloat && flags.SequenceEqual(expected);
        Record(
            "zero-normal flag survives upload",
            ok ? Outcome.Pass : Outcome.Fail,
            $"CUSTOM0 format {custom0} (RFloat = {(long)Mesh.ArrayCustomFormat.RFloat}), flags [{string.Join(", ", flags)}], expected [{string.Join(", ", expected)}]");
    }

    private async Task RenderChecksAsync()
    {
        // Lighting chosen so that a decoded zero normal ((0, 0, -1) in Godot, AC north) faces the sun.
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunDirection, new Vector3(0, 0, -1));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.SunColor, new Vector3(0.5f, 0.5f, 0.5f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.Ambient, new Vector3(0.2f, 0.2f, 0.2f));
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);
        RenderingServer.GlobalShaderParameterSet(RenderGlobals.MaxMipLevel, 3f);

        var viewport = new SubViewport
        {
            Size = ViewportSize,
            OwnWorld3D = true,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 2f, // y in [-1, 1]; x in [-4, 4] at 4:1
            Near = 0.1f,
            Far = 20f,
            Position = new Vector3(0, 0, 5),
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0f, 0.5f, 0f),
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                TonemapExposure = 1f,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        });

        var shaders = new SurfaceShaders();
        var opaque = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.None, TextureAddressMode.Clamp, Textured: false, Fog: false);
        var blended = new SurfaceShaderVariant(SurfaceBlend.Mix, false, false, SurfaceCull.None, TextureAddressMode.Clamp, Textured: true, Fog: false);
        var north = new AcVector3(0, 1, 0);

        // A: zero normals with the flag (MeshFactory). B: facing the sun. C: DXT1 punch-through over green. D: zero normals, no flag.
        AddQuad(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(-3f, AcVector3.Zero))), Solid(shaders, opaque));
        AddQuad(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(-1f, north))), Solid(shaders, opaque));
        using var dxt1 = Dxt1PunchThroughTexture();
        var textured = new ShaderMaterial { Shader = shaders.Get(blended) };
        textured.SetShaderParameter(MaterialFactory.TextureParameter, dxt1);
        AddQuad(viewport, MeshFactory.Build(MeshFactory.Prepare(Quad(1f, north))), textured);
        AddQuad(viewport, RawQuadWithoutFlag(3f), Solid(shaders, opaque));

        for (var i = 0; i < 4; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        var image = viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty())
        {
            Record("render checks", Outcome.Fail, "the viewport image could not be read back");
            return;
        }

        Pixel(image, 32, "background between quads (the clear colour reaches the screen unchanged)", 0, 128, 0);
        Pixel(image, 16, "zero normal + flag: ambient only", 51, 51, 51);
        Pixel(image, 48, "normal facing the sun: ambient + sun", 179, 179, 179);
        var dxt1Pixel = Bytes(image.GetPixel(80, ViewportSize.Y / 2));
        Record(
            "DXT1 punch-through renders opaque black",
            Near(dxt1Pixel, 0, 0, 0) ? Outcome.Pass : Outcome.Fail,
            Near(dxt1Pixel, 0, 128, 0)
                ? $"got {dxt1Pixel}: Godot now keeps BC1 alpha; TextureCache's RGBA8 twin is unnecessary, update render-contract.md §3"
                : $"got {dxt1Pixel}, expected (0, 0, 0): transparent texels turn opaque black, so alpha surfaces need the RGBA8 twin");
        Pixel(image, 112, "zero normal without flag: lit as if facing north (the bug the flag fixes)", 179, 179, 179);

        CheckDxt1Format(dxt1);
        viewport.QueueFree();
    }

    private void CheckDxt1Format(ImageTexture texture)
    {
        var device = RenderingServer.GetRenderingDevice();
        if (device is null)
        {
            Record("DXT1 RenderingDevice format", Outcome.Skipped, $"no RenderingDevice ({RenderingServer.GetCurrentRenderingDriverName()})");
            return;
        }

        var format = device.TextureGetFormat(RenderingServer.TextureGetRdTexture(texture.GetRid())).Format;
        var outcome = format switch
        {
            RenderingDevice.DataFormat.Bc1RgbUnormBlock or RenderingDevice.DataFormat.Bc1RgbSrgbBlock => Outcome.Pass,
            RenderingDevice.DataFormat.Bc1RgbaUnormBlock or RenderingDevice.DataFormat.Bc1RgbaSrgbBlock => Outcome.Fail,
            _ => Outcome.Info,
        };
        Record("DXT1 RenderingDevice format", outcome, $"Image.FORMAT_DXT1 is uploaded as {format} ({RenderingServer.GetCurrentRenderingDriverName()}); documented: BC1 without alpha");
    }

    private void Pixel(Image image, int x, string name, int r, int g, int b)
    {
        var got = Bytes(image.GetPixel(x, ViewportSize.Y / 2));
        Record(name, Near(got, r, g, b) ? Outcome.Pass : Outcome.Fail, $"got {got}, expected ({r}, {g}, {b}) +-{Tolerance}");
    }

    private static (int R, int G, int B) Bytes(Color c) => (c.R8, c.G8, c.B8);

    private static bool Near((int R, int G, int B) got, int r, int g, int b) =>
        Math.Abs(got.R - r) <= Tolerance && Math.Abs(got.G - g) <= Tolerance && Math.Abs(got.B - b) <= Tolerance;

    private static ShaderMaterial Solid(SurfaceShaders shaders, SurfaceShaderVariant variant)
    {
        var material = new ShaderMaterial { Shader = shaders.Get(variant) };
        material.SetShaderParameter("solid_color", Colors.White);
        return material;
    }

    private static void AddQuad(SubViewport viewport, ArrayMesh mesh, Material material)
    {
        mesh.SurfaceSetMaterial(0, material);
        viewport.AddChild(new MeshInstance3D { Mesh = mesh });
    }

    /// <summary>
    /// A 1.6 m square quad in the AC x/z plane centred on x = <paramref name="centreX"/>, facing AC south (towards the
    /// test camera), counter-clockwise front faces. Normals: <paramref name="normal"/>, or zero for the first two vertices.
    /// </summary>
    private static MeshData Quad(float centreX, AcVector3 normal, bool zeroFirstTwo = false)
    {
        var positions = new[]
        {
            new AcVector3(centreX - 0.8f, 0, -0.8f),
            new AcVector3(centreX + 0.8f, 0, -0.8f),
            new AcVector3(centreX + 0.8f, 0, 0.8f),
            new AcVector3(centreX - 0.8f, 0, 0.8f),
        };
        var normals = Enumerable.Range(0, 4).Select(i => zeroFirstTwo ? (i < 2 ? AcVector3.Zero : new AcVector3(0, -1, 0)) : normal).ToArray();
        var uvs = new[] { new AcVector2(0, 1), new AcVector2(1, 1), new AcVector2(1, 0), new AcVector2(0, 0) };
        return new MeshData([new SubMesh(0x08000000, MeshSide.Positive, TextureAddressMode.Clamp, positions, normals, uvs, [0, 1, 2, 0, 2, 3])]);
    }

    /// <summary>The same quad with zero normals built without MeshFactory, so it has no CUSTOM0 flag.</summary>
    private static ArrayMesh RawQuadWithoutFlag(float centreX)
    {
        var prepared = MeshFactory.Prepare(Quad(centreX, AcVector3.Zero)).SubMeshes[0];
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = prepared.Positions;
        arrays[(int)Mesh.ArrayType.Normal] = prepared.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = prepared.Uvs;
        arrays[(int)Mesh.ArrayType.Index] = prepared.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A layer whose attach takes several steps and records every call (main thread except PrepareAsync).</summary>
    private sealed class ProbeLayer : ILandblockLayer
    {
        private readonly HashSet<LandblockCoord> _attached = new();
        private int _prepared;
        private int _disposed;
        private int _doubleDisposed;

        public string Name => "probe";

        public int PreparedCount => Volatile.Read(ref _prepared);

        public int Disposed => Volatile.Read(ref _disposed);

        public int DoubleDisposed => Volatile.Read(ref _doubleDisposed);

        public int Attaches { get; private set; }

        public int Detaches { get; private set; }

        public int DetachWithoutAttach { get; private set; }

        public int StepsAfterDetach { get; private set; }

        public int SequencesStarted { get; private set; }

        public int SequencesStopped { get; private set; }

        public int StepsRun { get; private set; }

        public Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _prepared);
            return Task.FromResult<object?>(new Prepared(this));
        }

        public IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot)
        {
            Attaches++;
            _attached.Add(coord);
            return Steps(coord, landblockRoot);
        }

        public void Detach(LandblockCoord coord)
        {
            Detaches++;
            if (!_attached.Remove(coord))
            {
                DetachWithoutAttach++;
            }
        }

        private IEnumerable Steps(LandblockCoord coord, Node3D root)
        {
            SequencesStarted++;
            try
            {
                for (var i = 0; i < 6; i++)
                {
                    if (!_attached.Contains(coord))
                    {
                        StepsAfterDetach++;
                    }

                    root.AddChild(new Node3D());
                    StepsRun++;
                    yield return null;
                }
            }
            finally
            {
                SequencesStopped++;
            }
        }

        private sealed class Prepared(ProbeLayer owner) : IDisposable
        {
            private int _done;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0)
                {
                    Interlocked.Increment(ref owner._disposed);
                }
                else
                {
                    Interlocked.Increment(ref owner._doubleDisposed);
                }
            }
        }
    }

    /// <summary>A 4x4 DXT1 texture of one block in 3-colour mode (colour0 &lt;= colour1) with every index 3: all texels transparent black.</summary>
    private static ImageTexture Dxt1PunchThroughTexture()
    {
        byte[] block = [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        using var image = Image.CreateFromData(4, 4, false, Image.Format.Dxt1, block);
        return ImageTexture.CreateFromImage(image);
    }
}
