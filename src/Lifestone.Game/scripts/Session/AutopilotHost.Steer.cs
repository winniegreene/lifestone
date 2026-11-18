using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Godot;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Steer;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The steer desk's host (<c>--autopilot-steer-dir</c>): resolves the run folder by the output-path rule, owns the real files and the one background thread that does all the file work, and ticks the <see cref="OrderDesk"/> once a frame between the stop file and
/// the dial. With the option absent nothing here exists and every hook returns at once. The desk has its own <c>try</c>: a throw inside the host's disposes the dial, and steering must never take the autopilot down.
/// </summary>
public sealed partial class AutopilotHost
{
    /// <summary>How long the spool's thread waits between two turns, in milliseconds.</summary>
    private const int SteerTurnMilliseconds = 100;

    /// <summary>How long the game waits for the spool's last writes when it closes, in milliseconds.</summary>
    private const int SteerCloseMilliseconds = 1000;

    /// <summary>How long after the character is in the world the desk opens, in seconds.</summary>
    private const double SteerOpenDelaySeconds = 5;

    /// <summary>The seconds before the desk opens whose worst frame is logged at close, against the same after it.</summary>
    private const double SteerFrameWatchBeforeSeconds = 2;

    /// <summary>The seconds after the desk opens whose worst frame is logged at close.</summary>
    private const double SteerFrameWatchAfterSeconds = 3;

    private string? _steerFolder;
    private DateTimeOffset _steerClientStarted;
    private OrderDesk? _desk;
    private SteerSpool? _spool;
    private Thread? _steerThread;
    private volatile bool _steerStop;
    private double _steerInWorldAt = double.NaN;
    private double _steerOpenedAt = double.NaN;
    private double _steerWorstBeforeMs;
    private double _steerWorstAfterMs;
    private double _steerOpenMs;
    private double _steerWorstMs;
    private Func<Lifestone.Client.Session.Quests.QuestTimers?> _steerTimers = () => null;
    private bool _steerObserveOnly;
    private bool _steerOrders;
    private SteerCamera? _steerCamera;
    private double _steerPriorFrameMs;
    private long _steerLastTick;

    /// <summary>The steer desk, or null when the option is absent or the desk has not opened (the checks read it).</summary>
    internal OrderDesk? Desk => _desk;

    private void SetupSteer(AutopilotHostOptions hostOptions)
    {
        if (hostOptions.SteerDirectory is not { } path)
        {
            return;
        }

        if (!Lifestone.Client.Diagnostics.OutputPaths.TryResolve(path, Diagnostics.ScreenshotWriter.RepositoryRoot, out var full, out var problem))
        {
            Log.Error(LogCategory, $"autopilot: --autopilot-steer-dir {problem}; no desk is opened");
            return;
        }

        _steerFolder = full;
        _steerObserveOnly = hostOptions.SteerObserveOnly;
        _steerOrders = hostOptions.SteerOrders;
        _steerClientStarted = DateTimeOffset.UtcNow;
    }

    /// <summary>One frame of the desk: opens it once the character has been in the world for <see cref="SteerOpenDelaySeconds"/> (the server must be on this computer), then answers what the spool read.</summary>
    private void TickSteer(IGameSessionView session, double delta)
    {
        if (_steerFolder is null)
        {
            return;
        }

        // Measured, not the engine's delta: with delta smoothing that is snapped to the refresh rate and hides a slow frame.
        var tick = System.Diagnostics.Stopwatch.GetTimestamp();
        var frameMs = _steerLastTick == 0 ? delta * 1000 : System.Diagnostics.Stopwatch.GetElapsedTime(_steerLastTick, tick).TotalMilliseconds;
        _steerLastTick = tick;
        NoteSteerFrame(session.Clock.Now, frameMs);
        if (_desk is null && !SteerOpenDue(session))
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var opening = _desk is null;
        try
        {
            if (_desk is null)
            {
                OpenSteer(session);
            }

            _desk?.Tick(session, _logic);
            if (_desk is { Open: false })
            {
                StopSteerThread();
            }
        }
        catch (Exception e) when (LegFaultGuard.Guards(e))
        {
            Log.Error(LogCategory, $"autopilot steer: the desk faulted ({e.GetType().Name}: {e.Message}); the run carries on");
            _desk?.Close($"desk fault: {e.GetType().Name}", null);
            _steerFolder = null;
            StopSteerThread();
        }

        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (opening)
        {
            _steerOpenMs = ms;
        }
        else if (ms > _steerWorstMs)
        {
            _steerWorstMs = ms;
        }
    }

    /// <summary>The desk's first work (its first status, its thread, the first use of the JSON code) stays out of the login, where every frame is already tight.</summary>
    private void NoteSteerFrame(double now, double ms)
    {
        _steerCamera?.NoteFrame(ms);
        _steerPriorFrameMs = ms;
        if (_desk is null && !double.IsNaN(_steerInWorldAt) && now - _steerInWorldAt >= SteerOpenDelaySeconds - SteerFrameWatchBeforeSeconds)
        {
            _steerWorstBeforeMs = Math.Max(_steerWorstBeforeMs, ms);
        }
        else if (_desk is not null && now - _steerOpenedAt <= SteerFrameWatchAfterSeconds)
        {
            _steerWorstAfterMs = Math.Max(_steerWorstAfterMs, ms);
        }
    }

    private bool SteerOpenDue(IGameSessionView session)
    {
        if (!Lifestone.Client.Session.RetailChecks.KeepAcrossLogins.InWorld(session))
        {
            _steerInWorldAt = double.NaN;
            return false;
        }

        if (double.IsNaN(_steerInWorldAt))
        {
            _steerInWorldAt = session.Clock.Now;
        }

        return session.Clock.Now - _steerInWorldAt >= SteerOpenDelaySeconds;
    }

    private void OpenSteer(IGameSessionView session)
    {
        var folder = _steerFolder!;
        if (!OrderDesk.ServerIsLocal(session))
        {
            Log.Error(LogCategory, "autopilot steer: the session's server is not on this computer; no desk is opened");
            _steerFolder = null;
            return;
        }

        SteerDiskFiles disk;
        try
        {
            disk = new SteerDiskFiles(folder);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error(LogCategory, $"autopilot steer: the run folder cannot be made ({e.GetType().Name}); no desk is opened");
            _steerFolder = null;
            return;
        }

        var build = typeof(AutopilotHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var margin = _jobOptions?.Buff?.RebuffMarginSeconds ?? BuffOptions.DefaultRebuffMarginSeconds;
        var spool = new SteerSpool(disk, _steerClientStarted);
        _spool = spool;
        _steerCamera = new SteerCamera(this, System.IO.Path.Combine(folder, "shots"));
        _desk = new OrderDesk(spool, System.IO.Path.GetFileName(folder.TrimEnd('/', '\\')), build, margin, _steerTimers, GodotLogSink.Instance, startedBy => Press(startedBy), disk.ReadOverlay(), WithdrawSteerPress, _steerObserveOnly, _steerCamera, _orders, _steerOrders);
        _steerOpenedAt = session.Clock.Now;
        _steerThread = new Thread(() => SteerLoop(spool)) { IsBackground = true, Name = "autopilot-steer" };
        _steerThread.Start();
        Log.Info(LogCategory, "autopilot steer: the desk is open", ("folder", System.IO.Path.GetRelativePath(Diagnostics.ScreenshotWriter.RepositoryRoot, folder).Replace('\\', '/')), ("build", build), ("observe_only", _steerObserveOnly), ("orders", _steerOrders));
    }

    /// <summary>Takes back a press the desk made that is still waiting for the journey's parts.</summary>
    private void WithdrawSteerPress()
    {
        if (_pressPending && _pendingStartedBy == OrderDesk.StartedBy)
        {
            (_pressPending, _pendingStartedBy, _pendingReason) = (false, null, null);
            Log.Info(LogCategory, "autopilot steer: the press waiting for the parts is taken back");
        }
    }

    private void SteerLoop(SteerSpool spool)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (!_steerStop)
            {
                spool.Step(clock.Elapsed.TotalSeconds);
                _steerCamera?.SaveWaiting();
                Thread.Sleep(SteerTurnMilliseconds);
            }

            spool.Step(clock.Elapsed.TotalSeconds); // what the desk posted as it closed
            _steerCamera?.SaveWaiting();
        }
        catch (Exception e) when (LegFaultGuard.Guards(e))
        {
            spool.Fail($"the spool's thread ended on {e.GetType().Name}: {e.Message}"); // the desk reads it and closes; an exception leaving a thread would end the process
        }
    }

    private void StopSteerThread()
    {
        _steerStop = true;
        if (_steerThread is { } thread)
        {
            thread.Join(SteerCloseMilliseconds);
            _steerThread = null;
        }
    }

    private void CloseSteer()
    {
        try
        {
            if (_host?.Session is { } session)
            {
                _desk?.Close("the game is closing", session, fault: false);
            }
        }
        catch (Exception e) when (LegFaultGuard.Guards(e))
        {
            Log.Error(LogCategory, $"autopilot steer: closing the desk failed ({e.GetType().Name}: {e.Message})"); // the dial's own disposal comes next and must still run
        }

        if (_desk is not null)
        {
            Log.Info(LogCategory, "autopilot steer: the desk's cost on the game thread", ("open_ms", _steerOpenMs), ("worst_frame_ms", _steerWorstMs), ("whole_frame_worst_2s_before_open_ms", _steerWorstBeforeMs), ("whole_frame_worst_3s_after_open_ms", _steerWorstAfterMs));
        }

        _steerCamera?.LogCost();
        StopSteerThread();
    }

    /// <summary>The <c>capture</c> order's camera: the read-back on the game thread, the PNG on the spool's thread, and what each cost.</summary>
    private sealed class SteerCamera(AutopilotHost host, string folder) : ISteerCamera
    {
        private readonly ConcurrentQueue<(SteerShot Shot, Image Image, string Path)> _waiting = new();
        private SteerShot? _unmeasured;
        private int _count;
        private double _readbackTotal;
        private double _readbackWorst;
        private double _frameWorst;
        private double _priorWorst;

        public SteerShot? Take(string name)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Image? image;
            try
            {
                image = host.GetViewport().GetTexture()?.GetImage();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                Log.Warn(LogCategory, $"autopilot steer: no frame could be read ({e.GetType().Name})");
                return null;
            }

            if (image is null || image.IsEmpty())
            {
                image?.Dispose();
                return null;
            }

            var readback = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var shot = new SteerShot(readback, host._steerPriorFrameMs, image.GetWidth(), image.GetHeight());
            _waiting.Enqueue((shot, image, System.IO.Path.Combine(folder, name + ".png")));
            _unmeasured = shot;
            _count++;
            _readbackTotal += readback;
            _readbackWorst = Math.Max(_readbackWorst, readback);
            _priorWorst = Math.Max(_priorWorst, host._steerPriorFrameMs);
            return shot;
        }

        /// <summary>Game thread, at the start of a frame: the frame just ended is the one that paid for a read-back made in it.</summary>
        public void NoteFrame(double milliseconds)
        {
            if (_unmeasured is { } shot)
            {
                shot.FrameMilliseconds = milliseconds;
                _frameWorst = Math.Max(_frameWorst, milliseconds);
                _unmeasured = null;
            }
        }

        /// <summary>The spool's thread.</summary>
        public void SaveWaiting()
        {
            while (_waiting.TryDequeue(out var job))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    var error = job.Image.SavePng(job.Path);
                    if (error == Error.Ok)
                    {
                        job.Shot.MarkSaved();
                    }
                    else
                    {
                        job.Shot.MarkFailed($"saving the PNG answered {error}");
                    }
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
                {
                    job.Shot.MarkFailed(e.GetType().Name);
                }
                finally
                {
                    job.Image.Dispose();
                }
            }
        }

        public void LogCost()
        {
            if (_count > 0)
            {
                Log.Info(LogCategory, "autopilot steer: the screenshots' cost on the game thread", ("captures", _count), ("readback_mean_ms", _readbackTotal / _count), ("readback_worst_ms", _readbackWorst), ("whole_frame_worst_with_capture_ms", _frameWorst), ("whole_frame_worst_before_capture_ms", _priorWorst));
            }
        }
    }

    /// <summary>
    /// The steer folder's real files (the steer contract, section 2). Called only on the spool's thread, after the folders are made here. A replace is a temporary file, then a move over the old one, as <see cref="UserDataCheckpointStore"/> does it.
    /// </summary>
    private sealed class SteerDiskFiles : ISteerDisk
    {
        private readonly string _root;
        private readonly string _inbox;
        private readonly string _taken;
        private readonly string _outbox;

        public SteerDiskFiles(string root)
        {
            _root = root;
            _inbox = System.IO.Path.Combine(root, "inbox");
            _taken = System.IO.Path.Combine(root, "taken");
            _outbox = System.IO.Path.Combine(root, "outbox");
            System.IO.Directory.CreateDirectory(_inbox);
            System.IO.Directory.CreateDirectory(_taken);
            System.IO.Directory.CreateDirectory(_outbox);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "sheets"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "shots"));
        }

        public IReadOnlyList<string> InboxNames() => [.. System.IO.Directory.EnumerateFiles(_inbox, "*.json").Select(p => System.IO.Path.GetFileName(p)!)];

        public long InboxSize(string name) => new System.IO.FileInfo(System.IO.Path.Combine(_inbox, name)).Length;

        public DateTimeOffset InboxWritten(string name) => new(System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(_inbox, name)), TimeSpan.Zero);

        public byte[] ReadInbox(string name) => System.IO.File.ReadAllBytes(System.IO.Path.Combine(_inbox, name));

        public void TakeInbox(string name) => System.IO.File.Move(System.IO.Path.Combine(_inbox, name), System.IO.Path.Combine(_taken, name), overwrite: true);

        public bool HasReport(string id) => System.IO.File.Exists(System.IO.Path.Combine(_outbox, id + ".json"));

        public void Replace(string path, string text)
        {
            var target = System.IO.Path.Combine(_root, path);
            var temporary = target + ".tmp";
            System.IO.File.WriteAllText(temporary, text);
            System.IO.File.Move(temporary, target, overwrite: true);
        }

        public void Append(string path, string text) => System.IO.File.AppendAllText(System.IO.Path.Combine(_root, path), text);

        public void SetAside(string path)
        {
            var source = System.IO.Path.Combine(_root, path);
            if (!System.IO.File.Exists(source))
            {
                return;
            }

            var target = source + ".aside";
            for (var n = 2; System.IO.File.Exists(target); n++)
            {
                target = source + ".aside" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            System.IO.File.Move(source, target);
        }

        public IReadOnlyList<string> ReportIds() => [.. System.IO.Directory.EnumerateFiles(_outbox, "*.json").Select(p => System.IO.Path.GetFileNameWithoutExtension(p)!)];

        public string ReadReport(string id) => System.IO.File.ReadAllText(System.IO.Path.Combine(_outbox, id + ".json"));

        /// <summary>The overlay's text, read once on the game thread before the spool's thread starts; null when there is none or it cannot be read yet.</summary>
        public string? ReadOverlay()
        {
            try
            {
                var path = System.IO.Path.Combine(_root, SteerOverlay.FileName);
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
