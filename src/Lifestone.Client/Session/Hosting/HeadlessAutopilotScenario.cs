using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// <c>--scenario=autopilot</c> in the headless host (M5 4 h1, D57): at its first step it builds the journey's keeper over the session (<see cref="JourneyStations.CreateKeeper"/>), the context with the headless runner host
/// (<see cref="JourneyScenario.NewContext"/>), the station catalog (<see cref="JourneyStations.Create(JourneyPlan, IReadOnlyList{string}, DatSet?, string?)"/>) and the <see cref="AutopilotScenario"/>, as
/// <see cref="HeadlessJourneyScenario"/> builds the journey's; then every step ticks the runner host before the autopilot (the frame order: controller, runner, session). The autopilot never logs in or off: a
/// <see cref="LoginScenario"/> (the plan's character, no creation) is the host's login and log off, and the autopilot is its <c>play</c> scenario, so the run ends when the autopilot's done line is written and the character logs off even after a failed state; the done line's <c>ok</c> is the run's.
/// </summary>
public sealed class HeadlessAutopilotScenario : IScenario, IDisposable
{
    private readonly JourneyPlan _plan;
    private readonly LoginScenario _login;
    private readonly InWorld _inWorld;

    /// <summary>A headless autopilot run.</summary>
    /// <param name="plan">The plan (the mage's: the autopilot is built on it).</param>
    /// <param name="options">The autopilot's options (the stop rules, the forced trip, the scripted death).</param>
    /// <param name="arguments">The host's command line (the stations read their scenario options from it).</param>
    /// <param name="dats">The dats.</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="log">The log.</param>
    /// <param name="kitAccount">The account's login name the gear kit is built for (hashed by the kit, never stored or printed), or null.</param>
    /// <param name="host">The runner host (the scenario disposes it).</param>
    /// <param name="checkpoints">Where the autopilot keeps its checkpoint (M5 4 h3: the headless host's file store; <c>--autopilot-checkpoint-dir</c>); null: none is kept.</param>
    public HeadlessAutopilotScenario(JourneyPlan plan, AutopilotOptions options, IReadOnlyList<string> arguments, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, string? kitAccount, HeadlessJourneyRunnerHost host, IAutopilotCheckpointStore? checkpoints = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _inWorld = new InWorld(plan, options, arguments, dats, spells, log, kitAccount, host, checkpoints);
        _login = new LoginScenario(plan.Character, play: _inWorld);
    }

    /// <summary>The plan.</summary>
    public JourneyPlan Plan => _plan;

    /// <summary>The character the autopilot enters (the account rule reads it).</summary>
    public string Character => _plan.Character;

    /// <summary>The runner host.</summary>
    public HeadlessJourneyRunnerHost Host { get; }

    /// <summary>The autopilot, once the login ended and the first step built it.</summary>
    public AutopilotScenario? Autopilot => _inWorld.Autopilot;

    /// <summary>The autopilot's lines (each cycle's, then the done line), bare.</summary>
    public IReadOnlyList<string> Lines => Autopilot?.Lines ?? [];

    /// <inheritdoc />
    public string Name => AutopilotScenario.ScenarioName;

    /// <inheritdoc />
    public string Stage => Autopilot is null ? _login.Stage : _login.Stage + ":" + Autopilot.Stage;

    /// <inheritdoc />
    public string? Failure => _login.Failure ?? (Autopilot is { Ok: false, Failure: { } why } ? why : null);

    /// <inheritdoc />
    public double? WhereMaxMetres => _login.WhereMaxMetres;

    /// <inheritdoc />
    public long Unmatched => _login.Unmatched;

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        var status = _login.Step(session, now);
        return status == ScenarioStatus.Succeeded && Autopilot is { Ok: false } ? ScenarioStatus.Failed : status; // the run is the done line's ok
    }

    /// <summary>Releases the autopilot and the runner host.</summary>
    public void Dispose()
    {
        _inWorld.Dispose();
        Host.Dispose();
    }

    /// <summary>The autopilot as the login's <c>play</c> scenario: built at its first step (the keeper needs the session), ticks the runner host before it, and ends well when the autopilot has written its done line (the login then logs off).</summary>
    private sealed class InWorld(JourneyPlan plan, AutopilotOptions options, IReadOnlyList<string> arguments, DatSet dats, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, string? kitAccount, HeadlessJourneyRunnerHost host, IAutopilotCheckpointStore? checkpoints) : IScenario, IDisposable
    {
        public AutopilotScenario? Autopilot { get; private set; }

        public string Name => AutopilotScenario.ScenarioName;

        public string Stage => Autopilot?.Stage ?? "start";

        public string? Failure => Autopilot?.Failure;

        public double? WhereMaxMetres => Autopilot?.WhereMaxMetres;

        public long Unmatched => Autopilot?.Unmatched ?? 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (Autopilot is null)
            {
                var keeper = JourneyStations.CreateKeeper(plan, session, spells, arguments, log);
                var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, host, dungeon: JourneyLimits.FightOf(arguments).GroundProfile); // --fight-ground: a bad key is the station factory's error and keeps the default
                // M5 4 h3: the checkpoint store and the reconnect (a dropped session re-enters the character; the session is the host's GameSession).
                Autopilot = new AutopilotScenario(options, JourneyStations.Create(plan, arguments, dats, kitAccount), context, checkpoints, session is GameSession game ? new GameSessionAutopilotSession(game, plan.Character) : null);
            }

            host.Tick(session, now);
            return Autopilot.Step(session, now) == ScenarioStatus.Running ? ScenarioStatus.Running : ScenarioStatus.Succeeded;
        }

        public void Dispose() => Autopilot?.Dispose();
    }
}
