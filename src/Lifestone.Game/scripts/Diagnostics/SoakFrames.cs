using System.Diagnostics;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The soak frames (M5 item 7, <c>--soak-frame-s</c>, <c>--soak-frame-dir</c>): every S seconds in play mode a frame of the window is saved as a PNG, so a run of
/// hours leaves a picture series to look at in the morning. Plain object, not a node: play mode calls <see cref="Step"/> once a frame. The clock rule is
/// <see cref="SoakFrameSchedule"/> (the first frame S seconds after entering the world); the read-back is the only work on the main thread, on the frame that
/// takes it, and the PNG is encoded and written on a worker (<see cref="ScreenshotWriter.SaveInBackgroundQuietly"/>). One INFO line per frame.
/// </summary>
public sealed class SoakFrames
{
    private const string LogCategory = "soak";

    private readonly SoakFrameSchedule _schedule;
    private readonly string? _directory;
    private object? _world;

    /// <summary>Creates the frame saver; with a period of 0, or a refused folder (logged once), it does nothing.</summary>
    public SoakFrames(double periodSeconds, string directory)
    {
        _schedule = new SoakFrameSchedule(periodSeconds);
        if (!_schedule.Enabled)
        {
            return;
        }

        _directory = ScreenshotWriter.Resolve(directory, LogCategory);
        if (_directory is not null)
        {
            Log.Info(LogCategory, "soak frames on", ("period_s", periodSeconds), ("dir", _directory));
        }
    }

    /// <summary>True when frames will be saved.</summary>
    public bool Active => _schedule.Enabled && _directory is not null;

    /// <summary>Frames taken so far.</summary>
    public int Taken => _schedule.Taken;

    /// <summary>
    /// Once a frame. <paramref name="world"/> is the identity of the player's placed body, or null when the player is not in the world (start-up, the character list,
    /// portal space before the first arrival): the countdown starts when it first appears and again when it is a different object (a new login), so the first
    /// frame is S seconds after entering the world, not after start-up.
    /// </summary>
    public void Step(Viewport viewport, object? world)
    {
        if (!Active || world is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _schedule.Arm(now);
            return;
        }

        if (!_schedule.TryTake(now, out var number))
        {
            return;
        }

        var name = SoakFrameSchedule.FileName(DateTime.UtcNow, number);
        var path = System.IO.Path.Combine(_directory!, name);
        var readbackMs = ScreenshotWriter.SaveInBackgroundQuietly(viewport, path, LogCategory, ok =>
        {
            if (ok)
            {
                Log.Info(LogCategory, "soak frame saved", ("n", number), ("path", name));
            }
        });
        if (readbackMs < 0)
        {
            Log.Warn(LogCategory, "soak frame skipped: the window could not be read back", ("n", number));
        }
    }
}
