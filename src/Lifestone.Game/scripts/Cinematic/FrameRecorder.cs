using System.Collections.Concurrent;
using System.Text.Json;
using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Cinematic;

/// <summary>
/// Records exactly the frames a director asks for (<see cref="CaptureThisFrame"/>), as raw RGBA8 into one file per shot
/// (<c>DIR/&lt;shot&gt;.rgba</c>, with a <c>.json</c> sidecar giving the size, pixel format, frame rate and frame count)
/// for tools\hero-video.ps1 to encode with ffmpeg.
/// </summary>
/// <remarks>
/// <para><b>Why not Movie Maker's own files.</b> Godot 4.7's <c>--write-movie</c> writes every frame of the run, at the
/// project's viewport size (1280×720 here, whatever <c>--resolution</c> says), PNG-compressed on the main thread
/// (about 230 ms a frame measured). This recorder takes the window's own size (the film's 1920×1080), skips loading and
/// pre-roll frames entirely, and hands the pixels to a writer thread, so a frame costs one read-back. Determinism is the
/// same: the run is stepped by the engine's <c>--fixed-fps</c> (which tools\hero-video.ps1 always passes).</para>
/// <para>The image is read after the frame is drawn (<see cref="RenderingServer.FramePostDraw"/>), so a frame captured
/// is the frame the director posed in that same iteration.</para>
/// </remarks>
public sealed class FrameRecorder : IDisposable
{
    private const int QueueFrames = 8;
    private readonly Viewport _viewport;
    private readonly string _category;
    private readonly BlockingCollection<byte[]> _queue = new(QueueFrames);
    private readonly FileStream _file;
    private readonly Thread _writer;
    private readonly string _sidecar;
    private readonly int _fps;
    private Exception? _writeError;
    private bool _pending;
    private bool _finished;

    private FrameRecorder(Viewport viewport, string path, string sidecar, int fps, string category)
    {
        _viewport = viewport;
        _category = category;
        _fps = fps;
        Path = path;
        _sidecar = sidecar;
        _file = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read, 1 << 22);
        _writer = new Thread(WriteLoop) { Name = "FrameRecorder", IsBackground = true };
        _writer.Start();
        RenderingServer.FramePostDraw += OnFramePostDraw;
    }

    /// <summary>The raw frame file.</summary>
    public string Path { get; }

    /// <summary>Frames captured so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Frame size (set by the first frame).</summary>
    public Vector2I Size { get; private set; }

    /// <summary>True once a frame could not be captured or written (the shot must be re-recorded).</summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// Starts a recording of shot <paramref name="shotId"/> into <paramref name="directory"/> (relative to the repository
    /// root; never inside src/ or tests/). Returns null (logged) when the file cannot be created.
    /// </summary>
    public static FrameRecorder? Start(Viewport viewport, string directory, string shotId, int fps, string category)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (ScreenshotWriter.Resolve(System.IO.Path.Combine(directory, shotId + ".rgba"), category) is not { } path)
        {
            return null;
        }

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            return new FrameRecorder(viewport, path, System.IO.Path.ChangeExtension(path, ".json"), fps, category);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(category, "cannot record frames", ("path", path), ("error", ex.Message));
            return null;
        }
    }

    /// <summary>Captures the frame drawn at the end of this iteration.</summary>
    public void CaptureThisFrame() => _pending = true;

    /// <summary>Writes what is queued, closes the file, writes the sidecar; returns false (logged) when anything failed.</summary>
    public bool Finish()
    {
        if (_finished)
        {
            return !Failed;
        }

        _finished = true;
        RenderingServer.FramePostDraw -= OnFramePostDraw;
        _queue.CompleteAdding();
        _writer.Join();
        _file.Dispose();
        if (_writeError is not null)
        {
            Failed = true;
            Log.Error(_category, "frame file not written", ("path", Path), ("error", _writeError.Message));
        }

        try
        {
            var meta = new { file = System.IO.Path.GetFileName(Path), width = Size.X, height = Size.Y, pixelFormat = "rgba", fps = _fps, frames = Frames, failed = Failed };
            File.WriteAllText(_sidecar, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Failed = true;
            Log.Error(_category, "frame sidecar not written", ("path", _sidecar), ("error", ex.Message));
        }

        Log.Info(_category, "frames written", ("path", Path), ("frames", Frames), ("size", $"{Size.X}x{Size.Y}"), ("mb", new FileInfo(Path).Length / (1024.0 * 1024.0)), ("failed", Failed));
        return !Failed;
    }

    public void Dispose()
    {
        Finish();
        _queue.Dispose();
    }

    private void OnFramePostDraw()
    {
        if (!_pending || _finished)
        {
            return;
        }

        _pending = false;
        using var image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty())
        {
            Failed = true;
            Log.Error(_category, "frame capture failed: empty viewport image", ("frame", Frames));
            return;
        }

        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }

        var size = new Vector2I(image.GetWidth(), image.GetHeight());
        if (Frames == 0)
        {
            Size = size;
        }
        else if (size != Size)
        {
            Failed = true;
            Log.Error(_category, "the frame size changed during a shot; frame dropped", ("frame", Frames), ("size", size), ("expected", Size));
            return;
        }

        _queue.Add(image.GetData());
        Frames++;
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var frame in _queue.GetConsumingEnumerable())
            {
                _file.Write(frame, 0, frame.Length);
            }

            _file.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _writeError = ex;
            foreach (var _ in _queue.GetConsumingEnumerable())
            {
                // drain, so the main thread never blocks on a full queue
            }
        }
    }
}
