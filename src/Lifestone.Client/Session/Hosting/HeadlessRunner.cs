using System.Globalization;
using System.Net;
using System.Text;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Streaming;
using Lifestone.Data.Dat;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Net.Taps;

namespace Lifestone.Client.Session.Hosting;

/// <summary>What a headless run is set up with (the host's options, already parsed).</summary>
public sealed record HeadlessRunOptions
{
    /// <summary>The default limit on a whole run, in seconds on the session clock.</summary>
    public const double DefaultQuitAfterSeconds = 180;

    /// <summary>The longest wait between two frames, in seconds (the loop's idle).</summary>
    public const double FrameWaitSeconds = 0.016;

    /// <summary>The recording's runner token.</summary>
    public const string RunnerToken = "headless";

    /// <summary>The Session group's values (server, account, budget, retail net).</summary>
    public SessionOptions Session { get; init; } = new();

    /// <summary>The limit on the whole run, in seconds on the session clock (<c>--quit-after</c>).</summary>
    public double QuitAfterSeconds { get; init; } = DefaultQuitAfterSeconds;

    /// <summary>The seed of portal space's roll (fixed, so runs repeat).</summary>
    public uint Seed { get; init; } = MsvcCrtRandom.DefaultSeed;

    /// <summary>Lifestone's version for the recording header (letters, digits, '.', '+', '-'), or null.</summary>
    public string? LifestoneVersion { get; init; }

    /// <summary>The backend's ACE commit for the recording header (lower-case hexadecimal), or null.</summary>
    public string? AceCommit { get; init; }
}

/// <summary>
/// The headless host's loop (step ST15; DoD 13: a session host that logs in with no Godot): a live
/// <see cref="GameSession"/>, portal space as the game runs it but drawing nothing (a <see cref="TimedTunnel"/>, the
/// <see cref="PortalSessionBridge"/>, so LoginComplete goes out when the destination is ready, exactly as on screen), the
/// destination's readiness from the dats (<see cref="DataOnlyAttachments"/>), and a scenario.
/// </summary>
/// <remarks>
/// <para><b>One frame</b> (<see cref="Step"/>): the frame time is the session clock's now; the session ticks; the world is
/// focused on the player's cell; portal space steps (its director may ask the Action API for LoginComplete, which goes out
/// next frame); the scenario steps; the run's time limit is checked. <see cref="Run"/> repeats it, runs the callbacks
/// posted to the game thread's context, and between frames waits for a datagram for at most
/// <see cref="HeadlessRunOptions.FrameWaitSeconds"/> (net's sweeps never starve).</para>
/// <para><b>Ending.</b> The run ends when the scenario succeeds or fails, the time limit passes, a frame throws, or the
/// run is cancelled; <see cref="Result"/> says how. Disposing the runner disposes the session (quitting the network
/// session, completing the recording) and the bridge.</para>
/// <para><b>Gates</b> (<see cref="Start"/>): the server must be loopback (A0's S8: one WARNING naming
/// <c>HeadlessRunner</c>, and nothing else runs) and the server mod ready, before <see cref="SessionBootstrap"/> runs its
/// own gates and creates the session. Game thread only.</para>
/// </remarks>
public sealed class HeadlessRunner : IDisposable
{
    /// <summary>The runner name the gates log.</summary>
    public const string RunnerName = "HeadlessRunner";

    /// <summary>The game camera's vertical field of view portal space zooms from (Lifestone's 60°).</summary>
    public const double VerticalFovDegrees = 60;

    private readonly GameSession _session;
    private IScenario _scenario;
    private readonly IWorldAttachments _world;
    private readonly DataOnlyAttachments? _dataWorld;
    private readonly PortalSpaceDirector _director;
    private readonly PortalSessionBridge _bridge;
    private readonly CountingLogSink _log;
    private HeadlessRunOptions _options;
    private int _errors0;
    private int _warnings0;
    private int _loginCompletes0;
    private long _corrections0;
    private readonly List<SessionPhase> _phases = [];
    private readonly IDisposable _phaseSubscription;
    private bool _disposed;

    /// <summary>A runner over an existing session (tests; <see cref="Start"/> creates the session).</summary>
    /// <param name="session">The live session (the runner owns it from now on).</param>
    /// <param name="scenario">What to run.</param>
    /// <param name="assets">Portal space's UIASSET records (<see cref="PortalSpaceAssets.Resolve"/>).</param>
    /// <param name="world">The destination's readiness (a <see cref="DataOnlyAttachments"/> is focused each frame).</param>
    /// <param name="options">The run's options.</param>
    /// <param name="log">The counting log the session also writes to.</param>
    internal HeadlessRunner(GameSession session, IScenario scenario, PortalSpaceAssets assets, IWorldAttachments world, HeadlessRunOptions options, CountingLogSink log)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _dataWorld = world as DataOnlyAttachments;
        session.Cull.Cells = world; // M5 0 fix R1: the cull reads a dungeon cell's visible cells from the decoded cells (Focus decodes the player's cell and its visible cells)
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        ArgumentNullException.ThrowIfNull(assets);

        var focal = PortalZoom.FocalFromVerticalFov(VerticalFovDegrees * Math.PI / 180.0);
        var machine = new PortalSpaceMachine(new TimedTunnel(createdAt: session.Clock.Now), new MsvcCrtRandom(options.Seed), () => focal);
        _director = new PortalSpaceDirector(machine, new TeleportProgress(), assets, ReadinessPolicy.Default, sound: null, notices: null, log);
        _bridge = new PortalSessionBridge(session, _director);
        _phaseSubscription = session.Events.Subscribe<SessionPhaseChanged>(OnPhase);
        Started = session.Clock.Now;
        Result = Snapshot(done: false, ok: false, failure: null);
    }

    /// <summary>The session.</summary>
    public GameSession Session => _session;

    /// <summary>The scenario.</summary>
    public IScenario Scenario => _scenario;

    /// <summary>Portal space's director (its counters: LoginCompletes sent, stalls).</summary>
    public PortalSpaceDirector Director => _director;

    /// <summary>The session-clock time the run started.</summary>
    public double Started { get; private set; }

    /// <summary>True once the run ended.</summary>
    public bool IsDone => Result.Done;

    /// <summary>The run's result so far (final once <see cref="IsDone"/>).</summary>
    public HeadlessRunResult Result { get; private set; }

    /// <summary>
    /// Gates and starts a headless run: the loopback gate (<see cref="RunnerName"/>) and the mod-ready gate, then
    /// <see cref="SessionBootstrap.CreateLive"/> (its own gates: the language dat and the account) with a recording header
    /// naming the runner and the scenario, then the runner. The session logs through a <see cref="CountingLogSink"/>
    /// around <paramref name="log"/>. Refusals throw as <see cref="SessionBootstrap.CreateLive"/>'s do, after one WARNING. An account other than the test account is refused first
    /// (<see cref="AutomatedAccountRule"/>, a <see cref="PlayAccountException"/>); no account means the test account.
    /// </summary>
    /// <param name="options">The run's options.</param>
    /// <param name="scenario">What to run.</param>
    /// <param name="secretsJson">secrets.json's bytes (the host zeroes them afterwards).</param>
    /// <param name="pidsJson">pids.json's bytes.</param>
    /// <param name="dats">The opened dats (the language dat, portal space's assets, the destination's cells).</param>
    /// <param name="time">The session's time source (<see cref="TimeProvider.System"/> in the host).</param>
    /// <param name="log">Where everything logs.</param>
    /// <param name="recording">Where to record, or null (the host owns the stream).</param>
    /// <returns>The runner, its session idle.</returns>
    public static HeadlessRunner Start(
        HeadlessRunOptions options,
        IScenario scenario,
        ReadOnlySpan<byte> secretsJson,
        ReadOnlySpan<byte> pidsJson,
        DatSet dats,
        TimeProvider time,
        ILogSink log,
        Stream? recording)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var runner = StartWith(
            options,
            scenario,
            secretsJson,
            pidsJson,
            () => SessionBootstrap.LanguageIterations(dats),
            () => PortalSpaceAssets.Resolve(dats.EnumMaps),
            counting => new DataOnlyAttachments(dats, counting),
            time,
            log,
            recording,
            socketFactory: null,
            networkFactory: null);
        runner.Session.UseSpellsFrom(dats); // the cast tracker's SpellTable (read at the first cast)
        return runner;
    }

    /// <summary><see cref="Start"/> with the dat-backed parts as functions and the network session injectable (tests).</summary>
    internal static HeadlessRunner StartWith(
        HeadlessRunOptions options,
        IScenario scenario,
        ReadOnlySpan<byte> secretsJson,
        ReadOnlySpan<byte> pidsJson,
        Func<Lifestone.Net.Session.DddIterations> iterations,
        Func<PortalSpaceAssets> assets,
        Func<ILogSink, IWorldAttachments> world,
        TimeProvider time,
        ILogSink log,
        Stream? recording,
        Func<IPEndPoint, IDatagramSocket>? socketFactory,
        Func<AcSessionOptions, IAcSessionSink, IRawMessageTap, IAcSession>? networkFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(iterations);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);

        // D10 (M4 D31): an automated run uses only the four test accounts (TestAccounts). Applied here, the one place every caller passes (the host, the scenarios' tests),
        // before anything else; the refusal never repeats the account name (a mistyped option could be a password).
        if (!AutomatedAccountRule.TryApply(options.Session, automated: true, out var automatedSession, out var accountRefusal))
        {
            log.Warn(RunnerName, RunnerName + " refused the run: " + accountRefusal);
            throw new PlayAccountException(accountRefusal!);
        }

        options = options with { Session = automatedSession };

        // M4 P4 rfix-b (review R11): on test-melee and test-archer a run enters only that account's own fighter and creates no character; refused loudly, before any backend step.
        var (enterName, createName) = scenario switch
        {
            LoginScenario l => (l.Character, l.CreateCharacter),
            AcademyExitScenario a => (a.Login.Character, a.Login.CreateCharacter),
            HoltburgLapsScenario h => (h.Login.Character, h.Login.CreateCharacter),
            WayfindScenario w => (w.Login.Character, w.Login.CreateCharacter),
            Scenarios.Journey.HeadlessJourneyScenario j => ((string?)j.Character, (string?)null), // M4 P5b J4: the journey enters its plan's character, creates none
            LoginCheckScenario c => ((string?)c.Character, (string?)c.Character), // the check creates its character when it is missing
            _ => ((string?)null, (string?)null),
        };
        if (!AutomatedAccountRule.TryCharacters(automatedSession.Account, enterName, createName, out var characterRefusal))
        {
            log.Warn(RunnerName, RunnerName + " refused the run: " + characterRefusal);
            throw new PlayAccountException(characterRefusal!);
        }

        // A0's S8: nothing before the loopback gate; then the mod-ready gate (D11), both naming this runner.
        LoopbackGate.Require(options.Session.Server, RunnerName, log);
        BackendReadiness.Require(pidsJson, RunnerName, log);

        var counting = new CountingLogSink(log);
        var header = new RecordingHeader
        {
            Runner = HeadlessRunOptions.RunnerToken,
            CreatedUtc = time.GetUtcNow(),
            Scenario = scenario.Name,
            LifestoneVersion = options.LifestoneVersion,
            AceCommit = options.AceCommit,
        };
        var session = SessionBootstrap.CreateLiveWith(
            options.Session,
            secretsJson,
            pidsJson,
            iterations,
            time,
            counting,
            recording,
            socketFactory,
            header,
            networkFactory);
        try
        {
            return new HeadlessRunner(session, scenario, assets(), world(counting), options, counting);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Re-arms the runner with another scenario over the same live session (M4 P4: <see cref="HeadlessSession"/> runs several
    /// scenarios in one login). The previous run must have ended. The new run's clock, limit and counters start now: the
    /// result's errors, warnings, LoginCompletes and corrections count from this call on.
    /// </summary>
    /// <param name="scenario">What to run next.</param>
    /// <param name="quitAfterSeconds">The limit on this run, in seconds on the session clock.</param>
    internal void Arm(IScenario scenario, double quitAfterSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scenario);
        if (!Result.Done)
        {
            throw new InvalidOperationException("Arm: the previous run has not ended");
        }

        _scenario = scenario;
        _options = _options with { QuitAfterSeconds = quitAfterSeconds };
        _phases.Clear();
        _errors0 = _log.Errors;
        _warnings0 = _log.Warnings;
        _loginCompletes0 = _director.LoginCompletesSent;
        _corrections0 = _session.Corrections;
        Started = _session.Clock.Now;
        Result = Snapshot(done: false, ok: false, failure: null);
    }

    /// <summary>
    /// One network frame with no scenario (M4 P4 sfix): the session ticks so it receives and acknowledges what the server sent, which keeps the
    /// client's silence watchdog (20 s) and the server's own timeout quiet while a long-lived <see cref="HeadlessSession"/> waits between runs.
    /// </summary>
    /// <param name="now">The frame time.</param>
    internal void KeepAlive(double now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.Tick(now);
    }

    /// <summary>Runs one frame at <paramref name="now"/> (see the remarks). Returns the result so far.</summary>
    /// <param name="now">The frame time (the session clock's now in <see cref="Run"/>).</param>
    public HeadlessRunResult Step(double now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Result.Done)
        {
            return Result;
        }

        try
        {
            _session.Tick(now);
            var cell = _session.State.Self.Position?.CellId ?? 0;
            _dataWorld?.Focus(cell);
            _director.Step(now, cell, _world);
            switch (_scenario.Step(_session, now))
            {
                case ScenarioStatus.Succeeded:
                    return Finish(ok: true, failure: null);
                case ScenarioStatus.Failed:
                    return Finish(ok: false, failure: _scenario.Failure ?? "the scenario failed without saying why");
            }

            if (_session.Clock.Now - Started > _options.QuitAfterSeconds)
            {
                return Finish(ok: false, failure: string.Create(
                    CultureInfo.InvariantCulture,
                    $"the run did not finish within {_options.QuitAfterSeconds:0.#} s (stage {_scenario.Stage}, phase {_session.State.Phase}, portal {_director.Machine.State})"));
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error(SessionLog.Category, $"the headless run stopped: {e.GetType().Name}: {e.Message}");
            return Finish(ok: false, failure: $"{e.GetType().Name}: {e.Message}");
        }

        Result = Snapshot(done: false, ok: false, failure: null);
        return Result;
    }

    /// <summary>
    /// Runs frames until the run ends (see the remarks), on the calling thread, which becomes the game thread. Blocks
    /// between frames for at most <see cref="HeadlessRunOptions.FrameWaitSeconds"/>.
    /// </summary>
    /// <param name="context">The game thread's context, whose posted callbacks run after each frame, or null.</param>
    /// <param name="cancellationToken">Ends the run (as a failure: "cancelled").</param>
    /// <returns>The final result.</returns>
    public HeadlessRunResult Run(GameThreadSynchronizationContext? context, CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromSeconds(HeadlessRunOptions.FrameWaitSeconds);
        while (!Result.Done)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Finish(ok: false, failure: "cancelled");
            }

            Step(_session.Clock.Now);
            context?.RunPending();
            if (!Result.Done)
            {
                _session.WaitForInboundAsync(wait, cancellationToken).AsTask().GetAwaiter().GetResult();
            }
        }

        return Result;
    }

    /// <summary>Disposes the bridge and the session (which quits the network session and completes the recording).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _phaseSubscription.Dispose();
        _bridge.Dispose();
        _session.Dispose();
    }

    private HeadlessRunResult Finish(bool ok, string? failure)
    {
        if (!ok)
        {
            _log.Error(SessionLog.Category, $"headless run failed: {failure}", new LogField("scenario", _scenario.Name), new LogField("stage", _scenario.Stage));
        }
        else
        {
            _log.Info(SessionLog.Category, "headless run succeeded", new LogField("scenario", _scenario.Name));
        }

        Result = Snapshot(done: true, ok: ok, failure: failure);
        return Result;
    }

    private HeadlessRunResult Snapshot(bool done, bool ok, string? failure) => new(
        done,
        ok,
        _scenario.Name,
        _scenario.Stage,
        failure,
        [.. _phases],
        _log.Errors - _errors0,
        _log.Warnings - _warnings0,
        _session.Clock.Now - Started,
        _director.LoginCompletesSent - _loginCompletes0,
        _session.Recorder?.Frames ?? 0,
        _session.Corrections - _corrections0,
        _scenario.WhereMaxMetres,
        _scenario.Unmatched);

    private void OnPhase(in SessionPhaseChanged e) => _phases.Add(e.Phase);
}

/// <summary>How a headless run went (<see cref="HeadlessRunner.Result"/>).</summary>
/// <param name="Done">True once the run ended.</param>
/// <param name="Ok">True when the scenario succeeded.</param>
/// <param name="Scenario">The scenario's token.</param>
/// <param name="Stage">The scenario's stage when the result was taken.</param>
/// <param name="Failure">Why it failed, or null: the scenario's or the runner's words, or for a run that could not start a
/// fixed phrase (<see cref="CouldNotStart"/>). It names no account, character or secret.</param>
/// <param name="Phases">The session phases entered, in order.</param>
/// <param name="Errors">ERROR lines written (the failure's own included).</param>
/// <param name="Warnings">WARNING lines written.</param>
/// <param name="Seconds">How long it ran, in seconds on the session clock.</param>
/// <param name="LoginCompletes">LoginCompletes portal space sent.</param>
/// <param name="RecordedFrames">Frames recorded so far (0 without a recording).</param>
/// <param name="Corrections">The server's force-position corrections of the player (<see cref="GameSession.Corrections"/>).</param>
/// <param name="WhereMaxMetres">The largest server-to-client distance the scenario measured, or null when it measured
/// none (<see cref="IScenario.WhereMaxMetres"/>).</param>
/// <param name="Unmatched">Movement messages the server never matched (<see cref="IScenario.Unmatched"/>).</param>
public sealed record HeadlessRunResult(
    bool Done,
    bool Ok,
    string Scenario,
    string Stage,
    string? Failure,
    IReadOnlyList<SessionPhase> Phases,
    int Errors,
    int Warnings,
    double Seconds,
    int LoginCompletes,
    long RecordedFrames,
    long Corrections,
    double? WhereMaxMetres,
    long Unmatched)
{
    /// <summary>The process exit code of a finished run: 0 when it succeeded, 1 when it failed.</summary>
    public int ExitCode => Ok ? 0 : 1;

    /// <summary>
    /// The result of a run that could not start (<see cref="HeadlessRunner.Start"/> threw): failed at the scenario's
    /// first stage, with one ERROR (the host's) and a fixed failure phrase chosen by the exception's type
    /// (<see cref="StartFailure"/>). The exception's message, which may name account keys and usernames, belongs in the
    /// log only.
    /// </summary>
    /// <param name="scenario">The scenario that was to run.</param>
    /// <param name="refusal">What <see cref="HeadlessRunner.Start"/> threw.</param>
    public static HeadlessRunResult CouldNotStart(IScenario scenario, Exception refusal)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(refusal);
        return new HeadlessRunResult(true, false, scenario.Name, scenario.Stage, StartFailure(refusal), [], 1, 0, 0, 0, 0, 0, null, 0);
    }

    /// <summary>
    /// The fixed phrase for a run that could not start, by the exception's type: the loopback gate, the mod-ready gate,
    /// the account gate, the dats, a backend file, a refused setting, or the session's creation (the language dat among
    /// them). Each says to see the log, where the gate's own WARNING names the reason and the remedy.
    /// </summary>
    /// <param name="refusal">What <see cref="HeadlessRunner.Start"/> threw.</param>
    public static string StartFailure(Exception refusal) => refusal switch
    {
        LoopbackRequiredException => "the loopback gate refused the server (see the log)",
        BackendNotReadyException => "the backend is not ready (see the log)",
        PlayAccountException => "the account gate refused (see the log)",
        InvalidDataException => "the dats could not be read (see the log)",
        IOException or UnauthorizedAccessException => "a backend file could not be read (see the log)",
        ArgumentException => "a setting was refused (see the log)",
        _ => "the session could not be created (see the log)",
    };

    /// <summary>
    /// The headless verdict line, frozen in m2-plan §4.14 (the L19 smoke step and the login checks parse it):
    /// <c>headless done scenario=&lt;s&gt; ok=&lt;0|1&gt; corrections=&lt;n&gt; where_max_m=&lt;x&gt; unmatched=&lt;n&gt;
    /// recording=&lt;path&gt;</c>, keys in that order, <c>recording=</c> last; <c>where_max_m</c> has three decimals
    /// (invariant culture), or is <c>none</c> when the scenario measured nothing (only on a failed run: the login
    /// scenario cannot succeed without its check); <c>recording=none</c> without a recording. Nothing else is on the
    /// line, so it holds no account, character, secret or failure text: the failure, the stage, the phases and the
    /// counts are in the log and the <c>--summary</c> JSON.
    /// </summary>
    /// <param name="recording">The recording's path as the host shows it (no spaces), or null.</param>
    public string SummaryLine(string? recording)
    {
        var line = new StringBuilder("headless done");
        line.Append(" scenario=").Append(Scenario);
        line.Append(" ok=").Append(Ok ? '1' : '0');
        line.Append(" corrections=").Append(Corrections.ToString(CultureInfo.InvariantCulture));
        line.Append(" where_max_m=").Append(WhereMaxMetres is { } metres ? metres.ToString("0.000", CultureInfo.InvariantCulture) : "none");
        line.Append(" unmatched=").Append(Unmatched.ToString(CultureInfo.InvariantCulture));
        line.Append(" recording=").Append(recording ?? "none");
        return line.ToString();
    }
}
