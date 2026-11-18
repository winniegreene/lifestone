using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Hosting;

/// <summary>The parts a headless kit makes a scenario from: the station factory and the journey context of a run on one ground (the context's <see cref="JourneyContext.Dungeon"/> is that ground).</summary>
/// <param name="Factory">The station factory.</param>
/// <param name="Context">The journey context.</param>
public sealed record HeadlessKitParts(IStationFactory Factory, JourneyContext Context);

/// <summary>
/// The headless half of the level up job's kits (M6 item 16 E0b, D139; <see cref="IAutopilotKitFactory"/>): a kit with no window. Nothing is read on workers, so a kit is ready as soon as it is asked for
/// (<see cref="Prepare"/>), and a move-on's restart starts in the frame of the stop. Each scenario's parts are made fresh by the host's <c>parts</c> for the ground and plan in hand, as the play host's kit makes its
/// parts for each scenario; the carry is the client's one copy (<see cref="AutopilotRungKits.Scenario"/>).
/// </summary>
/// <remarks>Game thread only. The session is the one the host steps (<see cref="Session"/>, set by <see cref="HeadlessClimbScenario"/> each frame before the dial).</remarks>
public sealed class HeadlessAutopilotKits : IAutopilotKitFactory
{
    private readonly AutopilotOptions _options;
    private readonly Func<HuntingGround, JourneyPlan, IGameSessionView, HeadlessKitParts> _parts;
    private readonly IAutopilotCheckpointStore? _checkpoints;
    private readonly Func<IGameSessionView, JourneyPlan, IAutopilotSession?>? _reconnect;
    private readonly List<string> _madeOn = [];
    private HuntingGround? _ground;
    private JourneyPlan? _plan;

    /// <summary>Kits for the job <paramref name="options"/> describe.</summary>
    /// <param name="options">The run's options.</param>
    /// <param name="parts">Makes the parts of a run on a ground with a plan in a session (the real host: the keeper, the context over its runner host, the catalog's factory; a test: its scripted ones).</param>
    /// <param name="checkpoints">Where the run keeps its checkpoint, or null: none is kept.</param>
    /// <param name="reconnect">The host's reconnect for a session and plan, or null: a dropped session stops the run by name.</param>
    public HeadlessAutopilotKits(AutopilotOptions options, Func<HuntingGround, JourneyPlan, IGameSessionView, HeadlessKitParts> parts, IAutopilotCheckpointStore? checkpoints = null, Func<IGameSessionView, JourneyPlan, IAutopilotSession?>? reconnect = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        _checkpoints = checkpoints;
        _reconnect = reconnect;
    }

    /// <summary>The session the scenarios are made in (the host's; set before the dial is pressed or ticked).</summary>
    public IGameSessionView? Session { get; set; }

    /// <summary>Called for each scenario made, before the dial steps it (a host prints its lines; a test sets what its scripted world cannot read).</summary>
    public Action<AutopilotScenario>? Created { get; set; }

    /// <summary>The ground of the kit in hand, or null before the first.</summary>
    public HuntingGround? Ground => _ground;

    /// <summary>The grounds the scenarios were made on, in order (one a scenario).</summary>
    public IReadOnlyList<string> MadeOn => _madeOn;

    /// <inheritdoc/>
    public bool Ready => _ground is not null;

    /// <inheritdoc/>
    public void Prepare(HuntingGround ground, JourneyPlan plan)
    {
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <inheritdoc/>
    public void PressWhenReady(string? startedBy, string? reason) =>
        throw new InvalidOperationException("a headless kit is ready as soon as it is asked for (nothing is read on workers), so no press ever waits on it; a move-on that asks for one has no kit prepared");

    /// <inheritdoc/>
    public AutopilotScenario Create(AutopilotCarry carry)
    {
        ArgumentNullException.ThrowIfNull(carry);
        var session = Session ?? throw new InvalidOperationException("the headless kit has no session yet (the host sets it before the dial is pressed)");
        if (_ground is not { } ground || _plan is not { } plan)
        {
            throw new InvalidOperationException("the headless kit has no ground yet (the first press prepares the rung's)");
        }

        var parts = _parts(ground, plan, session);
        var scenario = AutopilotRungKits.Scenario(carry, _options, parts.Factory, parts.Context, _checkpoints, _reconnect?.Invoke(session, plan));
        _madeOn.Add(ground.Key);
        Created?.Invoke(scenario);
        return scenario;
    }
}

/// <summary>
/// The level up job with no window (M6 item 16 E0b, D139): the dial (<see cref="AutopilotHostLogic"/>) over headless kits (<see cref="HeadlessAutopilotKits"/>), started once as an unattended run and stepped until it ends. The
/// first step is the play host's first press: the rung's ground and kit (<see cref="AutopilotRungKits.PrepareRung"/>), then the press. A run that ends
/// <c>stop=rung-moved:&lt;rung&gt;</c> restarts by itself on the next rung's ground through the dial's own move-on (<see cref="AutopilotRungKits.MoveOnNext"/>), in the frame of the stop. The scenario never logs in or off: its
/// host does (<see cref="HeadlessAutopilotScenario"/>), or a test's scripted world stands in.
/// </summary>
/// <remarks>
/// <para>What it leaves out of the play host's wiring, each by name: the start rule (<see cref="AutopilotHostLogic.StartRefusal"/>: the play host refuses a character a person logged in that is not the job's; here the host's login
/// enters the plan's character, <see cref="AutopilotHostLogic.PlanFor"/>, so there is no other to refuse); the automation menu's judgement of a quest task (<c>TODO(engine-headless-menu-input)</c>: <see cref="AutopilotHostLogic.MenuInput"/> stays null, so the
/// scenario's own checks stand); the person's controls (/stop, /auto, the kill switch's resume: a run that leaves Running without ending fails by name, as nothing here could resume it); the person's reconnect
/// (<see cref="AutopilotHostLogic.ForeignReconnects"/> stays off: only the run's own reconnect exists).</para>
/// <para>Game thread only.</para>
/// </remarks>
public sealed class HeadlessClimbScenario : IScenario, IDisposable
{
    /// <summary>The token of the one start (the dial's <c>started by</c> line; the run is unattended and stays so across rungs).</summary>
    public const string StartedBy = "--scenario=autopilot";

    private readonly AutopilotOptions _options;
    private readonly HeadlessAutopilotKits _kits;
    private readonly Action<IGameSessionView, double>? _tick;
    private readonly List<string> _lines = [];
    private AutopilotScenario? _linesOf;
    private int _linesTaken;
    private bool _pressed;
    private string? _failure;

    /// <summary>A headless climb.</summary>
    /// <param name="options">The level up job's options.</param>
    /// <param name="kits">The headless kits (their <see cref="HeadlessAutopilotKits.Session"/> is set here each frame).</param>
    /// <param name="log">The log.</param>
    /// <param name="tick">Run each frame before the dial (the real host ticks its runner host: controller, runner, session), or null.</param>
    public HeadlessClimbScenario(AutopilotOptions options, HeadlessAutopilotKits kits, ILogSink log, Action<IGameSessionView, double>? tick = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kits = kits ?? throw new ArgumentNullException(nameof(kits));
        ArgumentNullException.ThrowIfNull(log);
        if (options.Job != AutopilotJob.LevelUp)
        {
            throw new ArgumentException("the headless climb runs the level up job only (the den job has no rung to move to)", nameof(options));
        }

        _tick = tick;
        Logic = new AutopilotHostLogic(_kits.Create, log, options); // the level up job's ground is the rung's, known once the character is in the world (the first step gives it)
        Rungs = new AutopilotRungKits(Logic, options, _kits, log);
        Logic.MoveOn = Rungs.MoveOnNext;
        var made = _kits.Created;
        _kits.Created = scenario =>
        {
            TakeLines(); // the ended run's last lines, before the dial's scenario is the next one
            (_linesOf, _linesTaken) = (scenario, 0);
            made?.Invoke(scenario);
        };
    }

    /// <summary>The dial.</summary>
    public AutopilotHostLogic Logic { get; }

    /// <summary>The kits by rung.</summary>
    public AutopilotRungKits Rungs { get; }

    /// <summary>The run in hand (null before the first step).</summary>
    public AutopilotScenario? Autopilot => Logic.Scenario;

    /// <summary>Every run's lines (each cycle's, each stop and done line), bare, in order across the rung moves.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            TakeLines();
            return _lines;
        }
    }

    /// <summary>True when the climb ended well: the last run's stop is ok and no move-on was refused.</summary>
    public bool Ok => _failure is null && Logic.Mode == AutopilotMode.Done && Logic.MoveOnRefusal is null && Logic.Scenario is { Ok: true };

    /// <inheritdoc />
    public string Name => AutopilotScenario.ScenarioName;

    /// <inheritdoc />
    public string Stage => Autopilot?.Stage ?? "start";

    /// <inheritdoc />
    public string? Failure => _failure ?? Logic.MoveOnRefusal ?? Autopilot?.Failure;

    /// <inheritdoc />
    public double? WhereMaxMetres => Autopilot?.WhereMaxMetres;

    /// <inheritdoc />
    public long Unmatched => Autopilot?.Unmatched ?? 0;

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_failure is not null)
        {
            return ScenarioStatus.Failed;
        }

        _kits.Session = session;
        if (!_pressed)
        {
            _pressed = true;
            if (Rungs.PrepareRung(session) is { } refusal)
            {
                return Fail(refusal);
            }

            Logic.Toggle(session, StartedBy);
            if (Logic.Mode != AutopilotMode.Running)
            {
                return Fail("the dial did not start (" + Logic.Status.LastDecision + ")");
            }
        }

        _tick?.Invoke(session, now);
        Logic.Tick(session, now);
        switch (Logic.Mode)
        {
            case AutopilotMode.Running or AutopilotMode.Stopping:
                return ScenarioStatus.Running;
            case AutopilotMode.Done:
                return Ok ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
            default:
                return Fail("the dial left the run in mode " + Logic.Mode.ToString().ToLowerInvariant() + " (" + Logic.Status.LastDecision + "), and a headless run has no person to hand the controls back");
        }
    }

    /// <summary>Releases the dial and its run.</summary>
    public void Dispose() => Logic.Dispose();

    private ScenarioStatus Fail(string why)
    {
        _failure = why;
        return ScenarioStatus.Failed;
    }

    private void TakeLines()
    {
        if (_linesOf is not { } scenario)
        {
            return;
        }

        for (; _linesTaken < scenario.Lines.Count; _linesTaken++)
        {
            _lines.Add(scenario.Lines[_linesTaken]);
        }
    }
}
