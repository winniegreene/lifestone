using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Client.Streaming;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// A headless host that logs one character in ONCE and runs several scenarios over that login, one after the other (M4 P4, D31:
/// <see cref="HeadlessRunner"/> is one run per scenario and one login per run, about 30 s of login for every loopback test).
/// </summary>
/// <remarks>
/// <para><b>The game thread.</b> The session is game-thread only (docs/session.md §2), so the host owns one dedicated thread that
/// creates the session, installs a <see cref="GameThreadSynchronizationContext"/> for its whole life (an action handle's awaiters
/// resume there between two scenarios as within one), and runs the work the caller hands it from any thread: <see cref="Run"/>
/// and the other members block the caller until the game thread has done the work, so a test body (whatever thread xunit gives
/// it) just calls them one after another. One caller at a time.</para>
/// <para><b>Runs.</b> <see cref="Start"/> creates the session and runs the entry scenario (normally a
/// <see cref="LoginScenario"/> with <see cref="LoginScenario.StayInWorld"/>); <see cref="Run"/> arms the same
/// <see cref="HeadlessRunner"/> with the next scenario and its own time limit and returns that run's own
/// <see cref="HeadlessRunResult"/> (errors, warnings, seconds and corrections count from the run's start; the summary line is
/// the same). A scenario that fails or times out leaves the session usable as long as it is still <see cref="IsInWorld"/>; a
/// scenario that ends the session (it logs off, or the character dies and the server moves the session out of the world) is
/// reported by <see cref="IsInWorld"/> turning false: the next <see cref="Run"/> then throws, and the owner starts a new host.
/// A scenario run over a session must not log in itself (a scenario that finds the player already in the world, as
/// <see cref="HuntScenario"/> does, skips its login).</para>
/// <para><b>Ending.</b> <see cref="Dispose"/> logs the character off when the session is still in the world, then
/// disposes the runner (the network session and the recording), and stops the thread.</para>
/// </remarks>
public sealed class HeadlessSession : IDisposable
{
    /// <summary>The limit on the log off in <see cref="Dispose"/>, in seconds on the session clock.</summary>
    public const double LogOffSeconds = 60;

    private readonly BlockingCollection<Action> _jobs = [];
    private readonly Thread _thread;
    private readonly Action<GameSession>? _idle;
    private HeadlessRunner? _runner;
    private bool _disposed;
    private ITimer? _keepAliveTimer;
    private TimeProvider? _time;
    private int _keepAlivePending;
    private ILogSink? _log;
    private string? _keepAliveFailure;

    private HeadlessSession(Action<GameSession>? idle)
    {
        _idle = idle;
        _thread = new Thread(Loop) { IsBackground = true, Name = "lifestone-headless-session" };
        _thread.Start();
    }

    /// <summary>The result of the entry scenario (the login), set by <see cref="Start"/>.</summary>
    public HeadlessRunResult EntryResult { get; private set; } = null!;

    /// <summary>The runs made so far, the entry included.</summary>
    public int Runs { get; private set; }

    /// <summary>
    /// Opens the session and runs <paramref name="entry"/> (the login). The gates and the options are
    /// <see cref="HeadlessRunner.Start"/>'s; a refusal throws from here as it does there, after the thread has stopped.
    /// </summary>
    /// <param name="options">The session's options (its <see cref="HeadlessRunOptions.QuitAfterSeconds"/> limits the entry).</param>
    /// <param name="entry">The first scenario.</param>
    /// <param name="secretsJson">secrets.json's bytes (the caller zeroes them afterwards).</param>
    /// <param name="pidsJson">pids.json's bytes.</param>
    /// <param name="dats">The opened dats.</param>
    /// <param name="time">The session's time source.</param>
    /// <param name="log">Where everything logs.</param>
    /// <param name="recording">Where to record, or null.</param>
    public static HeadlessSession Start(
        HeadlessRunOptions options,
        IScenario entry,
        byte[] secretsJson,
        byte[] pidsJson,
        DatSet dats,
        TimeProvider time,
        ILogSink log,
        Stream? recording)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return StartWith(
            options,
            entry,
            secretsJson,
            pidsJson,
            () => Lifestone.Client.Session.SessionBootstrap.LanguageIterations(dats),
            () => PortalSpaceAssets.Resolve(dats.EnumMaps),
            counting => new DataOnlyAttachments(dats, counting),
            time,
            log,
            recording,
            socketFactory: null,
            networkFactory: null,
            idle: null,
            afterCreate: runner => runner.Session.UseSpellsFrom(dats));
    }

    /// <summary><see cref="Start"/> with the dat-backed parts as functions, the network session injectable and the idle between frames replaceable (tests).</summary>
    internal static HeadlessSession StartWith(
        HeadlessRunOptions options,
        IScenario entry,
        byte[] secretsJson,
        byte[] pidsJson,
        Func<DddIterations> iterations,
        Func<PortalSpaceAssets> assets,
        Func<ILogSink, IWorldAttachments> world,
        TimeProvider time,
        ILogSink log,
        Stream? recording,
        Func<System.Net.IPEndPoint, Lifestone.Net.Sockets.IDatagramSocket>? socketFactory,
        Func<AcSessionOptions, IAcSessionSink, Lifestone.Net.Taps.IRawMessageTap, IAcSession>? networkFactory,
        Action<GameSession>? idle,
        Action<HeadlessRunner>? afterCreate = null)
    {
        var host = new HeadlessSession(idle) { _time = time, _log = log };
        try
        {
            host.EntryResult = host.Invoke(() =>
            {
                var runner = HeadlessRunner.StartWith(options, entry, secretsJson, pidsJson, iterations, assets, world, time, log, recording, socketFactory, networkFactory);
                host._runner = runner;
                afterCreate?.Invoke(runner);
                host.Runs = 1;
                return host.Pump(CancellationToken.None);
            });
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>The session's phase now.</summary>
    public SessionPhase Phase => Invoke(() => _runner!.Session.State.Phase);

    /// <summary>True while the character is in the world (a scenario that logs off or ends the session turns it false).</summary>
    public bool IsInWorld => Phase == SessionPhase.InWorld;

    /// <summary>
    /// Runs <paramref name="scenario"/> over the open session with its own limit and returns that run's result (the same
    /// <see cref="HeadlessRunResult"/> and <see cref="HeadlessRunResult.SummaryLine"/> as a one-shot run). Blocks the caller.
    /// </summary>
    /// <param name="scenario">What to run; it must not log in (the player is in the world).</param>
    /// <param name="quitAfterSeconds">The limit on this run, in seconds on the session clock.</param>
    /// <param name="cancellationToken">Ends the run as a failure ("cancelled").</param>
    /// <exception cref="InvalidOperationException">The session is no longer in the world.</exception>
    public HeadlessRunResult Run(IScenario scenario, double quitAfterSeconds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quitAfterSeconds);
        return Invoke(() =>
        {
            var phase = _runner!.Session.State.Phase;
            if (phase != SessionPhase.InWorld)
            {
                throw new InvalidOperationException($"the session is not in the world (phase {phase}): an earlier scenario ended it; open a new session");
            }

            _runner.Arm(scenario, quitAfterSeconds);
            Runs++;
            return Pump(cancellationToken);
        });
    }

    /// <summary>The keep-alive frames <see cref="KeepAlive"/> has run between scenarios.</summary>
    public int KeepAlives { get; private set; }

    /// <summary>
    /// One network frame on the game thread while no scenario runs (M4 P4 sfix): the session is only pumped during a run, so a shared session that
    /// waits more than the 20 s silence watchdog between two tests failed with "Silence" in the next one. A host calls this every few seconds
    /// (<see cref="StartKeepAlive"/>); it does nothing when the session is not in the world. Blocks the caller until the game thread has done it.
    /// </summary>
    public void KeepAlive() => Invoke(() =>
    {
        if (_runner is { } runner && runner.Session.State.Phase == SessionPhase.InWorld && runner.IsDone)
        {
            runner.KeepAlive(runner.Session.Clock.Now);
            if (_idle is not null)
            {
                _idle(runner.Session);
            }
            else
            {
                runner.Session.WaitForInboundAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            runner.KeepAlive(runner.Session.Clock.Now);
            KeepAlives++;
        }

        return 0;
    });

    /// <summary>The exception type of the first keep-alive failure of this session, or null when none failed (M4 P4 rfix-b, review R13).</summary>
    public string? KeepAliveFailure => _keepAliveFailure;

    /// <summary>
    /// One keep-alive frame that never throws: a failure is logged once per session as a WARN naming the exception type (R13: it was swallowed, so a broken keep-alive showed nowhere
    /// and the next test met the 20 s silence watchdog); a dead session itself is still reported by <see cref="IsInWorld"/>.
    /// </summary>
    internal void KeepAliveGuarded()
    {
        try
        {
            KeepAlive();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_keepAliveFailure is null)
            {
                _keepAliveFailure = e.GetType().Name;
                _log?.Warn("headless-session", "keep-alive frame failed; later failures of this session are not logged again", new LogField("type", e.GetType().Name));
            }
        }
    }

    /// <summary>
    /// Starts a timer that calls <see cref="KeepAlive"/> every <paramref name="periodSeconds"/> (default 4) while the host lives: a queued frame is never
    /// doubled, and a long run holds the frames back since the run itself pumps. Stopped by <see cref="Dispose"/>.
    /// </summary>
    /// <param name="periodSeconds">The period, well under the 20 s silence watchdog.</param>
    public void StartKeepAlive(double periodSeconds = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodSeconds);
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = (_time ?? throw new InvalidOperationException("the host has no time source")).CreateTimer(
            _ =>
            {
                if (_disposed || Interlocked.Exchange(ref _keepAlivePending, 1) == 1)
                {
                    return;
                }

                try
                {
                    _jobs.Add(() =>
                    {
                        try
                        {
                            KeepAliveGuarded();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _keepAlivePending, 0);
                        }
                    });
                }
                catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
                {
                    Interlocked.Exchange(ref _keepAlivePending, 0); // the queue is closed: the host is ending
                }
            },
            null,
            TimeSpan.FromSeconds(periodSeconds),
            TimeSpan.FromSeconds(periodSeconds));
    }

    /// <summary>Runs <paramref name="work"/> on the game thread with the live session (the session is game-thread only) and returns what it returns. Blocks the caller.</summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="work">The work.</param>
    public T OnGameThread<T>(Func<GameSession, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Invoke(() => work(_runner!.Session));
    }

    /// <summary>Logs the character off when the session is still in the world, then disposes the runner and stops the thread. Never throws.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keepAliveTimer?.Dispose();
        try
        {
            if (_thread.IsAlive)
            {
                Invoke(() =>
                {
                    if (_runner is { } runner)
                    {
                        try
                        {
                            if (runner.Session.State.Phase == SessionPhase.InWorld)
                            {
                                runner.Arm(new LogOffScenario(), LogOffSeconds);
                                Pump(CancellationToken.None);
                            }
                        }
                        finally
                        {
                            runner.Dispose();
                        }
                    }

                    return 0;
                });
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Disposal never throws; the runner's log already holds what went wrong.
        }
        finally
        {
            _jobs.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(10));
            _jobs.Dispose();
        }
    }

    /// <summary>Frames until the current run ends (the runner's own loop, with the idle replaceable).</summary>
    private HeadlessRunResult Pump(CancellationToken cancellationToken)
    {
        var runner = _runner!;
        var context = SynchronizationContext.Current as GameThreadSynchronizationContext;
        var wait = TimeSpan.FromSeconds(HeadlessRunOptions.FrameWaitSeconds);
        while (!runner.IsDone)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // The runner has no cancel entry other than Run; a cancelled pump ends the run through its time limit path.
                return runner.Run(context, cancellationToken);
            }

            runner.Step(runner.Session.Clock.Now);
            context?.RunPending();
            if (!runner.IsDone)
            {
                if (_idle is not null)
                {
                    _idle(runner.Session);
                }
                else
                {
                    runner.Session.WaitForInboundAsync(wait, cancellationToken).AsTask().GetAwaiter().GetResult();
                }
            }
        }

        return runner.Result;
    }

    private void Loop()
    {
        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                job();
                context.RunPending();
            }
        }
    }

    private T Invoke<T>(Func<T> work)
    {
        ObjectDisposedException.ThrowIf(_disposed && _jobs.IsAddingCompleted, this);
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            return work();
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        try
        {
            return done.Task.GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is AggregateException { InnerExceptions.Count: 1 } a)
        {
            ExceptionDispatchInfo.Capture(a.InnerExceptions[0]).Throw();
            throw;
        }
    }

    /// <summary>The scenario <see cref="Dispose"/> runs: log off (disposing the runner then quits the network session, as after every one-shot run).</summary>
    private sealed class LogOffScenario : IScenario
    {
        private ActionHandle? _pending;

        public string Name => "logoff";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            switch (Stage)
            {
                case "start":
                    _pending = session.Actions.LogOff(ActionSource.Host);
                    Stage = "logging-off";
                    break;
                case "logging-off" when _pending is { Outcome: { } off }:
                    if (off.Kind != ActionOutcomeKind.Confirmed)
                    {
                        Failure = "logging off: " + off.Kind;
                        return ScenarioStatus.Failed;
                    }

                    Stage = "done";
                    return ScenarioStatus.Succeeded;
            }

            return ScenarioStatus.Running;
        }
    }
}
