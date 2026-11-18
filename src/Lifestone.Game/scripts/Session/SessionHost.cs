using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Checkout;
using Lifestone.Data.Magic;
using Lifestone.Game.Core;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The one place the game makes a session (m2-plan G6 W9; the architecture test pins the file): the live session of <c>--play</c>, signed in to the
/// bundled ACE on 127.0.0.1, and the offline stand-in of the retail UI's own runs (<see cref="CreateStandIn"/>). A process priority of
/// <see cref="Priority"/> ticks the live session before everything else each frame (docs/game-core.md §1.2), so every node after it sees the state of the
/// messages that arrived in this frame; <see cref="Current"/> is how they reach it.
/// </summary>
/// <remarks>
/// <para><b>Starting.</b> The host reads <c>server\runtime\pids.json</c> and <c>secrets.json</c> as bytes (the Client does no file I/O), hands them to
/// <see cref="SessionBootstrap.CreateLive"/> (its four gates: loopback, the mod ready, the language dat, the account) and zeroes its copy of the secrets at
/// once. A password never becomes a string, a log line or an argument. A refused gate or a missing backend leaves no session: <see cref="StartFailure"/> says
/// why in words that name the remedy (never a secret), the main scene shows it, and an automated run quits with exit code 2. A session that was created logs
/// in at once (<c>Actions.Login()</c>, as retail's launcher signed in): the retail data check shows during the DDD exchange.</para>
/// <para><b>Ticking.</b> <c>GameSession.Tick(now)</c> with the frame clock portal space and physics share (<see cref="PortalSpaceSetup.Now"/>), inside the
/// frame-budget share <c>"session"</c> (<c>--session-budget-ms</c>). A frame that throws is logged as an ERROR (the first three, then every 600th) and the
/// next frame runs.</para>
/// <para><b>Quitting.</b> A window close request (<c>NOTIFICATION_WM_CLOSE_REQUEST</c>) calls <c>Actions.Quit</c> and ticks until it is sent, for at most
/// <see cref="QuitBudgetSeconds"/>, so ACE drops the session at once (F653 in the world, then a Disconnect). Leaving the tree by any other way (the
/// epilogue, <c>--quit-after</c>) disposes the session, which quits the same way. A killed process (tools\godot.ps1's timeout) cannot: ACE times the session out.</para>
/// </remarks>
public sealed partial class SessionHost : Node
{
    /// <summary>The process priority: before the frame-budget share's consumers, the player (-20), the world (10) and the UI (25).</summary>
    public const int Priority = -900;

    /// <summary>How long a close request may wait for the quit to be sent, in seconds (m2-plan G6 W9).</summary>
    public const double QuitBudgetSeconds = 0.5;

    private const string LogCategory = "session";

    private GameSession? _session;
    private Stream? _recording;
    private bool _budgetTaken;
    private bool _quitStarted;
    private long _tickFailures;
    private Task<CastPlannerBundle>? _castPlannerWork;
    private StandInVisit[] _castPlannerVisits = [];

    /// <summary>The installed host, or null.</summary>
    public static SessionHost? Instance { get; private set; }

    /// <summary>The live session as the rest of the game sees it, or null (no <c>--play</c>, or the session could not be started).</summary>
    public static IGameSessionView? Current => Instance?._session;

    /// <summary>The live session, or null.</summary>
    public GameSession? Session => _session;

    /// <summary>The session options of this run.</summary>
    public SessionOptions Options { get; private set; } = new();

    /// <summary>Why there is no session (a refused gate, no backend, no dats), in words that name the remedy and never a secret; null when there is one.</summary>
    public string? StartFailure { get; private set; }

    /// <summary>How many times the stand-in session has run its script (the first login and each reconnect); 0 for a live session.</summary>
    public int StandInRuns { get; private set; }

    /// <summary>The stand-in server's levers (boot the player, go quiet) for the play-mode checks; null for a live session.</summary>
    public StandInServer? StandIn { get; private set; }

    /// <summary>Failed frames so far.</summary>
    public long TickFailures => _tickFailures;

    /// <summary>
    /// Makes the host, starts the live session (or records why not) and adds the host to <paramref name="parent"/>. Call it before anything that reads
    /// <see cref="Current"/> (the retail UI, the play mode).
    /// </summary>
    public static SessionHost Install(Node parent, GameServices services)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        var host = new SessionHost { Name = "SessionHost", ProcessPriority = Priority };
        host.Start(services);
        parent.AddChild(host);
        return host;
    }

    /// <summary>
    /// Play mode's checks (<c>--play-checks</c>): this host over an offline stand-in session that plays <paramref name="visit"/> (the character list, an entry,
    /// an arrival, teleports, a logoff), with no backend, no secrets and no network. The session is made here like every other.
    /// </summary>
    public static SessionHost InstallStandIn(Node parent, GameServices services, StandInVisit visit, StandInVisit? replay = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(visit);
        var host = new SessionHost { Name = "SessionHost", ProcessPriority = Priority, Options = services.Options.Session };
        host._session = GameSession.Offline(
            h =>
            {
                // The stand-in runs again after every reconnect (the disconnected screen's button): the first run plays visit, the later ones replay.
                var run = host.StandInRuns++ == 0 ? visit : replay ?? visit;
                host.StandIn = StandInSessions.Server(h);
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, run);
                StandInSessions.AnswerWorld(h, run);
            },
            TimeProvider.System,
            GodotLogSink.Instance);
        if (services.Dats is { } dats)
        {
            host._session.UseSpellsFrom(dats); // the cast tracker's SpellTable (read at the first cast)
            host.StartCastPlanner(dats, visit, replay);
        }

        Log.Info(LogCategory, "play-mode checks: the session is the scripted stand-in, not a server");
        parent.AddChild(host);
        return host;
    }

    /// <summary>The stand-in's cast planner, its SpellTable and how long the worker took (M3 2.1 s2b).</summary>
    private sealed record CastPlannerBundle(SpellTableData Table, StandInCastPlanner Planner, int Planned, double Milliseconds);

    /// <summary>
    /// M3 2.1 s2b: gives the stand-in its cast (<see cref="StandInVisit.CastPlanner"/>, so an offline cast plays its words, gestures and effect scripts outside the checks too). The dat reads (the SpellTable, the SpellComponentTable and the
    /// player's motion table under <see cref="StandInCastPlanner.FromDats"/>, then the plan of every profile spell the stand-in can cast: the animations' lengths) run on a worker thread at below-normal priority, so none runs on the
    /// game thread in the frames after Enter; the game thread only stores the finished planner, in <see cref="_Process"/>, on the frame after the worker is done. A cast before then, or of a spell outside the profile, is planned on the
    /// thread that casts it (the planner caches it): <c>TODO(m3-standin-cast-plan-lazy)</c>, named in the log line.
    /// </summary>
    private void StartCastPlanner(Lifestone.Data.Dat.DatSet dats, StandInVisit visit, StandInVisit? replay)
    {
        _castPlannerVisits = replay is null ? [visit] : [visit, replay];
        var source = new TaskCompletionSource<CastPlannerBundle>(TaskCreationOptions.RunContinuationsAsynchronously);
        _castPlannerWork = source.Task;
        var thread = new Thread(() =>
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                var table = visit.SpellTable ?? SpellTableData.Load(dats);
                var planner = StandInCastPlanner.FromDats(dats);
                var planned = 0;
                foreach (var id in ArenaProfile.SpellsToLearn)
                {
                    // Only what the stand-in can cast without a fizzle (its fixed skill): the others would never be planned.
                    if (table.Spells.TryGetValue(id, out var info) && info.Power <= StandInCastPlanner.CastablePower)
                    {
                        planner.Plan(info);
                        planned++;
                    }
                }

                source.SetResult(new CastPlannerBundle(table, planner, planned, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                source.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "StandInCastPlanner",
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
    }

    /// <summary>Stores the worker's planner into the visits (game thread, once).</summary>
    private void TakeCastPlanner()
    {
        var work = _castPlannerWork;
        if (work is null || !work.IsCompleted)
        {
            return;
        }

        _castPlannerWork = null;
        if (work.IsFaulted)
        {
            Log.Error(LogCategory, "TODO(m3-standin-cast-plan): the stand-in's cast planner could not be made, so an offline cast plays without gestures", ("problem", work.Exception?.GetBaseException().Message));
            return;
        }

        var bundle = work.Result;
        foreach (var visit in _castPlannerVisits)
        {
            visit.SpellTable ??= bundle.Table;
            visit.CastPlanner ??= bundle.Planner.Plan;
        }

        Log.Info(LogCategory, "the stand-in's cast planner is ready (planned on a worker)", ("profile_spells_planned", bundle.Planned), ("worker_ms", bundle.Milliseconds), ("TODO", "m3-standin-cast-plan-lazy: a spell outside the profile is planned when it is cast"));
    }

    /// <summary>
    /// The offline stand-in session the retail screens run on while no live session exists (<c>--retail-ui=standin</c>): a <see cref="GameSession"/> fed by
    /// <c>StandInSessions</c> (Lifestone-authored names, nothing from the dats or a server) through the same reducer a live session uses, with the stand-in
    /// responder on so delete, restore and create can be clicked through. A run that starts at the data check (the default) gets a timed one; a screen
    /// further on starts with the list already in.
    /// </summary>
    /// <param name="startMode">The first UI mode (<c>--retail-ui-start</c>), or null.</param>
    /// <param name="scripted">True for a scripted run: the session's clock follows the frames instead of real time.</param>
    public static RetailUiOfflineSession CreateStandIn(uint? startMode, bool scripted)
    {
        var frameTime = scripted ? new FrameTimeProvider() : null;
        var timed = startMode is null or Lifestone.Client.RetailUi.Flow.UiModes.DataPatch;
        var session = GameSession.Offline(
            h =>
            {
                if (timed)
                {
                    StandInSessions.DataCheckThenCharacterSelect(h, StandInSessions.DefaultDataCheckDuration);
                }
                else
                {
                    StandInSessions.CharacterSelect(h);
                }

                StandInSessions.AnswerLocally(h);
            },
            (TimeProvider?)frameTime ?? TimeProvider.System,
            GodotLogSink.Instance);
        return new RetailUiOfflineSession(session, frameTime);
    }

    /// <summary>
    /// The disconnected screen's button (D43): a new network session with the same credentials, then the login again. Returns false without a session.
    /// </summary>
    public bool Reconnect()
    {
        if (_session is null)
        {
            return false;
        }

        _session.Reconnect();
        if (!_session.State.IsOffline)
        {
            _session.Actions.Login(); // the stand-in of the play-mode checks starts its data check by itself
        }
        Log.Info(LogCategory, "reconnecting: a new login");
        return true;
    }

    /// <inheritdoc/>
    public override void _EnterTree() => Instance = this;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        using var framePhase = Lifestone.Game.Diagnostics.FramePhases.Session(); // M6 16 Eperf2: this call's share of a long frame
        if (_session is null || _quitStarted)
        {
            return;
        }

        if (!_budgetTaken)
        {
            // The frame-budget share node is installed later in Main._Ready, so the share is taken on the first frame.
            _budgetTaken = true;
            FrameBudgetShare.Take(FrameBudgetShare.SessionShareName, Options.BudgetMs);
        }

        if (_castPlannerWork is not null)
        {
            TakeCastPlanner();
        }

        try
        {
            _session.Tick(PortalSpaceSetup.Now());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _tickFailures++;
            if (_tickFailures <= 3 || _tickFailures % 600 == 0)
            {
                Log.Error(LogCategory, "a session frame threw; the next frame runs", ("failures", _tickFailures), ("type", ex.GetType().Name), ("problem", ex.Message));
            }
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            // M5 7 F18: the window close is handled here. During an autopilot run the dial is asked to recall home first (WindowCloseGate): the close is deferred, the tree does not quit by itself,
            // and AutopilotHost quits the game through QuitGame when the run is done (60 s at most) or on a second close. Otherwise the game's own log off, as before.
            if (CloseDeferral?.Invoke() == true)
            {
                GetTree().AutoAcceptQuit = false;
                return;
            }

            QuitGame("the window was closed");
        }
    }

    /// <summary>
    /// Asked on a close request of the window (M5 7 F18): returns true when the close is deferred (the autopilot recalls home first and the host quits later through <see cref="QuitGame"/>), false for an immediate quit. Set by the autopilot host.
    /// </summary>
    public Func<bool>? CloseDeferral { get; set; }

    /// <summary>The game's own log off (<see cref="QuitNow"/>: F653 when in the world, then the disconnect), then the tree quits (exit code 0).</summary>
    /// <param name="why">For the log.</param>
    public void QuitGame(string why)
    {
        QuitNow(why);
        GetTree().Quit();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        QuitNow("the scene is closing");
        if (_budgetTaken)
        {
            FrameBudgetShare.Release(FrameBudgetShare.SessionShareName);
        }

        _session?.Dispose();
        _session = null;
        _recording?.Dispose();
        _recording = null;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Sends the quit (F653 when in the world, then a Disconnect) and ticks until it is sent or <see cref="QuitBudgetSeconds"/> has passed. Once only;
    /// the dispose after it does nothing more.
    /// </summary>
    private void QuitNow(string why)
    {
        if (_session is null || _quitStarted)
        {
            return;
        }

        _quitStarted = true;
        var watch = Stopwatch.StartNew();
        ActionHandle handle;
        try
        {
            handle = _session.Actions.Quit(ActionSource.Host);
            while (!handle.IsCompleted && watch.Elapsed.TotalSeconds < QuitBudgetSeconds)
            {
                _session.Tick(PortalSpaceSetup.Now());
                Thread.Sleep(2);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error(LogCategory, "the quit threw; the session is disposed anyway", ("type", ex.GetType().Name), ("problem", ex.Message));
            return;
        }

        Log.Info(
            LogCategory,
            handle.Outcome?.Kind == ActionOutcomeKind.Refused
                ? "quit was already under way (a login check or a log off ended the session before the scene closed)"
                : handle.IsCompleted ? "quit sent" : "quit not confirmed within the budget; the session is disposed anyway",
            ("why", why),
            ("ms", watch.Elapsed.TotalMilliseconds),
            ("outcome", handle.Outcome?.Kind.ToString() ?? "none"));
    }

    private void Start(GameServices services)
    {
        // An automated run (a login check, or any run that ends by itself) signs in to one of the four test accounts (TestAccounts: test, test-melee, test-archer, test-tools) and nothing else: the owner's play account is never
        // used by automation, and --login-check=enter creates a character on its account (AutomatedAccountRule; tools\play.ps1 and the headless host agree).
        var automated = services.Options.IsAutomated || services.Options.Play.LoginCheck != LoginCheckKind.None;
        if (!AutomatedAccountRule.TryApply(services.Options.Session, automated, out var sessionOptions, out var accountRefusal))
        {
            Options = services.Options.Session;
            Fail(accountRefusal!);
            return;
        }

        if (!AutomatedAccountRule.TryLoginCheck(sessionOptions.Account, services.Options.Play.LoginCheck, out var characterRefusal))
        {
            Options = services.Options.Session;
            Fail(characterRefusal!); // M4 P4 rfix-b (R11): a login check that creates Smoke or Throwaway never runs on test-melee or test-archer
            return;
        }

        Options = sessionOptions;
        if (services.Dats is not { } dats)
        {
            Fail(services.StartupError ?? "the retail dats are not available");
            return;
        }

        var root = Directory.GetCurrentDirectory(); // the game makes the repository root the working directory at start-up
        var searchRoots = CheckoutLocator.DefaultSearchRoots();
        if (!ServerRuntimePaths.TryFind(System.Environment.GetEnvironmentVariable(ServerRuntimePaths.EnvironmentVariable), searchRoots, out var runtime, out var problem))
        {
            Fail(problem + "; start the backend with tools\\play.ps1");
            return;
        }

        byte[] pids;
        try
        {
            pids = File.ReadAllBytes(runtime.PidsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"the backend is not running: {runtime.PidsFile} cannot be read ({ex.GetType().Name}); start it with tools\\play.ps1 (or server\\start-server.ps1)");
            return;
        }

        byte[]? secrets = null;
        try
        {
            secrets = File.ReadAllBytes(runtime.SecretsFile);
            _recording = Options.RecordSession ? OpenRecording(root) : null;
            var header = new RecordingHeader
            {
                Runner = "godot",
                CreatedUtc = DateTimeOffset.UtcNow,
                Scenario = "play",
                LifestoneVersion = typeof(SessionHost).Assembly.GetName().Version?.ToString(),
            };
            _session = SessionBootstrap.CreateLive(Options, secrets, pids, dats, TimeProvider.System, GodotLogSink.Instance, _recording, header);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // The gates' messages name keys and files and the remedy, never a secret (BackendReadiness, PlayAccounts, LoopbackGate).
            _recording?.Dispose();
            _recording = null;
            Fail(ex.Message);
            return;
        }
        finally
        {
            if (secrets is not null)
            {
                CryptographicOperations.ZeroMemory(secrets);
            }
        }

        Log.Info(LogCategory, "play mode: the live session is created; logging in", ("account", Options.Account ?? "default"), ("character", Options.Character is null ? "none" : "named"), ("recording", _recording is not null));
        var login = _session.Actions.Login();
        login.Completed += h =>
        {
            if (h.Outcome is { Succeeded: false } outcome)
            {
                Log.Error(LogCategory, "the login did not succeed (the retail disconnected screen has the reason)", ("outcome", outcome.Kind), ("detail", outcome.Detail));
            }
        };
    }

    /// <summary>The recording file of <c>--record-session</c>: under the repository, never under src/ or tests/ (<see cref="OutputPaths"/>).</summary>
    private Stream OpenRecording(string root)
    {
        var relative = Path.Combine(Options.RecordDirectory ?? SessionOptions.DefaultRecordDirectory, RecordingFormat.FileName(DateTimeOffset.UtcNow, "godot"));
        if (!OutputPaths.TryResolve(relative, root, out var full, out var problem))
        {
            throw new IOException(problem ?? "the recording path is refused");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return new FileStream(full, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.Read);
    }

    private void Fail(string message)
    {
        StartFailure = message;
        Log.Error(LogCategory, "play mode cannot start: " + message);
    }
}
