using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The scenario behind <c>--login-check=enter</c> (G6 W12; DoD 2, 13 and 15): the whole client path of a first login, as the
/// game runs it (a host that has already started the login, portal space and the retail screens around it), checked from
/// the session's own state. It acts only through the Action API (<see cref="ActionSource.Host"/>) and never blocks.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>):</para>
/// <list type="number">
/// <item><c>login</c>: the session reaches the character list (the host owns the login and has started it; a session that stays idle for
/// <see cref="HostLoginSeconds"/> fails the check). The data check must have completed.</item>
/// <item><c>choosing</c>: the character named by <see cref="Character"/> (case-insensitive), created once when missing
/// (<c>creating</c>, <see cref="HeadlessCharacter.Intent"/>: Aluvian, 55 skills) and selected.</item>
/// <item><c>entering</c>: <see cref="IActionApi.EnterWorld"/>, confirmed when the player's own object arrived.</item>
/// <item><c>arriving</c>: portal space plays the arrival and its director sends LoginComplete through the session bridge;
/// the step ends when the server has unhidden the player (<see cref="SelfTravel.Present"/>), and fails after
/// <see cref="ArrivalSeconds"/> on the session clock. The player must stand in the Training Academy's landblock
/// (<see cref="AcademyLandblock"/>).</item>
/// <item><c>settling</c>: the player stays present for <see cref="SettleSeconds"/> (in the world, not hidden, never
/// teleported, LoginComplete sent exactly once); the objects the server sent are counted (<see cref="AcademyObjects"/>).</item>
/// <item><c>playing</c> (only with <c>legs</c>, <c>--login-check=academy-exit|holtburg-laps|all</c>): each leg is a movement scenario (<see cref="RoutePlay"/>
/// driving the game's own player through a <see cref="RoutePlayHost"/>) stepped until it succeeds; the legs' audits give the verdict's corrections and where-am-I.</item>
/// <item><c>logging-off</c>: <see cref="IActionApi.LogOff"/>, confirmed back at the character list with the list intact.</item>
/// <item><c>quitting</c>: <see cref="IActionApi.Quit"/> (<see cref="ActionOutcomeKind.Sent"/>); then <c>done</c>.</item>
/// </list>
/// <para>Any other outcome fails the scenario with the step and the outcome (<see cref="Failure"/>). Character names appear
/// only in the log, never in the summary.</para>
/// </remarks>
public sealed class LoginCheckScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "login-check";

    /// <summary>The character the check enters, created when missing (a test character on the test account).</summary>
    public const string DefaultCharacter = "Lifestone Smoke";

    /// <summary>The Training Academy's landblock (the high word of its cells): the arrival must be here.</summary>
    public const uint AcademyLandblock = 0x8602;

    /// <summary>How long the session may stay idle before the check gives up: the host owns the login and starts it when it starts, so an idle session is a host fault.</summary>
    public const double HostLoginSeconds = 5;

    /// <summary>The default limit on reaching the character list, in seconds on the session clock.</summary>
    public const double DefaultListSeconds = 60;

    /// <summary>The default limit on the arrival, in seconds on the session clock (portal space takes 5 s or more).</summary>
    public const double DefaultArrivalSeconds = 90;

    /// <summary>The default time the player stays present before the log off, in seconds.</summary>
    public const double DefaultSettleSeconds = 5;

    /// <summary>The default limit on the log off, in seconds (the server answers about 6 s after the request).</summary>
    public const double DefaultLogOffSeconds = 30;

    private readonly IReadOnlyList<uint> _skillClasses;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<uint> _characterErrors = [];
    private ActionHandle? _pending;
    private bool _subscribed;
    private bool _createTried;
    private double _stageStart = double.NaN;
    private double _presentAt = double.NaN;
    private int _loginCompletes;
    private int _teleports;
    private readonly Func<ActionHandle?>? _requestLogOff;
    private readonly IReadOnlyList<RoutePlay> _legs;
    private int _leg;
    private int _teleportsAtSettle;
    private int _teleportEpisodes;
    private int _episodesAtSettle;
    private bool _teleportInFlight;

    /// <summary>How the log off is asked for: <c>key</c> (the host's log-off key) or <c>action</c> (<see cref="IActionApi.LogOff"/> directly).</summary>
    public string LogOffVia => _requestLogOff is null ? "action" : "key";

    /// <summary>A login check.</summary>
    /// <param name="skillClasses">The skill advancement classes of a created character (<see cref="HeadlessCharacter.RetailSkillClasses"/>).</param>
    /// <param name="character">The character to enter (<c>--character</c>), else <see cref="DefaultCharacter"/>.</param>
    /// <param name="settleSeconds">How long the player stays present before the log off.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="legs">The movement scenarios to play between the settle and the log off, in order (each leaves the character at its start), or none (<c>enter</c>).</param>
    /// <param name="requestLogOff">
    /// How the host asks for the log off, or null to call <see cref="IActionApi.LogOff"/> here. The game passes retail's key (Shift+Escape, End Character Session:
    /// <c>EndSessionKey</c>), so the check proves the key's whole path; it returns the handle of the log off the key asked for, or null when the key did nothing.
    /// </param>
    public LoginCheckScenario(IReadOnlyList<uint> skillClasses, string? character = null, double settleSeconds = DefaultSettleSeconds, double arrivalSeconds = DefaultArrivalSeconds, Func<ActionHandle?>? requestLogOff = null, IReadOnlyList<RoutePlay>? legs = null)
    {
        _requestLogOff = requestLogOff;
        _legs = legs is null ? [] : [.. legs];
        ArgumentNullException.ThrowIfNull(skillClasses);
        ArgumentOutOfRangeException.ThrowIfNegative(settleSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        _skillClasses = [.. skillClasses];
        Character = string.IsNullOrWhiteSpace(character) ? DefaultCharacter : character.Trim();
        SettleSeconds = settleSeconds;
        ArrivalSeconds = arrivalSeconds;
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The character the check enters.</summary>
    public string Character { get; }

    /// <summary>How long the player stays present before the log off.</summary>
    public double SettleSeconds { get; }

    /// <summary>The limit on the arrival, in seconds.</summary>
    public double ArrivalSeconds { get; }

    /// <summary>The character entered (its guid), or 0.</summary>
    public uint EnteredGuid { get; private set; }

    /// <summary>True when the check had to create the character.</summary>
    public bool Created { get; private set; }

    /// <summary>The session clock's time when <see cref="IActionApi.EnterWorld"/> was issued, or NaN.</summary>
    public double EnterAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the server unhid the player, or NaN.</summary>
    public double PresentAt => _presentAt;

    /// <summary>The session clock's time when the settle ended (the player could walk from <see cref="PresentAt"/>), or NaN.</summary>
    public double SettledAt { get; private set; } = double.NaN;

    /// <summary>The cell the player arrived in, or 0.</summary>
    public uint ArrivalCell { get; private set; }

    /// <summary>Where the player arrived (where the legs return it, and where a failed leg is recovered to), or null before the arrival.</summary>
    public CellFrame? ArrivalPosition { get; private set; }

    /// <summary>Every server teleport the player was sent through since the login, the legs' included.</summary>
    public int TeleportsTotal => _teleports;

    /// <summary>
    /// The objects the server had placed in the world, other than the player, by the end of the settle (the census's total,
    /// <see cref="WorldContentCensus"/>), or -1 before. The contents of the player's pack are not in it.
    /// </summary>
    public int AcademyObjects { get; private set; } = -1;

    /// <summary>The census at the end of the settle (what kinds of object the server placed), or null before.</summary>
    public WorldContent? Census { get; private set; }

    /// <summary>Every object the session held other than the player at the end of the settle (placed or not), or -1 before.</summary>
    public int ObjectsHeld { get; private set; } = -1;

    /// <summary>How many times the session sent LoginComplete (<see cref="SelfTravel.Materialising"/> entered); the check wants 1.</summary>
    public int LoginCompletes => _loginCompletes;

    /// <summary>How many server teleports (F751) the player was sent through by the end of the settle; the check wants 0 (the arrival is not one). The legs' own teleports are <see cref="LegTeleports"/>.</summary>
    public int Teleports => double.IsNaN(SettledAt) ? _teleports : _teleportsAtSettle;

    /// <summary>How many server teleports the legs sent the player through (to the route's start, through the portal, home).</summary>
    public int LegTeleports => double.IsNaN(SettledAt) ? 0 : _teleports - _teleportsAtSettle;

    /// <summary>
    /// How many portal-space visits the legs' teleports made: a teleport (F751) that ACE announces while an earlier one is still in flight (the player's
    /// travel is not <see cref="SelfTravel.Present"/> yet) belongs to that visit and shares its LoginComplete (docs/portal-space.md, "LoginComplete").
    /// ACE does announce two in a row for a portal Use: the Use's MoveTo and the contact with the portal each activate it. The portal space sends one
    /// LoginComplete per visit, so this, not <see cref="LegTeleports"/>, is what the check counts them against.
    /// </summary>
    public int LegTeleportEpisodes => double.IsNaN(SettledAt) ? 0 : _teleportEpisodes - _episodesAtSettle;

    /// <summary>The movement scenarios the check plays between the settle and the log off.</summary>
    public IReadOnlyList<RoutePlay> Legs => _legs;

    /// <summary>The leg being played, or null outside <c>playing</c>.</summary>
    public RoutePlay? CurrentLeg => Stage == "playing" && _leg < _legs.Count ? _legs[_leg] : null;

    /// <summary>The legs' force-position corrections (the server pulling the player back), or null before any leg walked.</summary>
    public int? Corrections => _legs.Any(l => l.Audit is not null) ? _legs.Sum(l => l.Audit?.Corrections ?? 0) : null;

    /// <summary>True when a leg left the Academy through the portal's teleport (F751).</summary>
    public bool ExitTeleported => _legs.Any(l => l.ExitArrival is not null);

    /// <summary>
    /// The current vitals as <c>h/s/m</c> from the PlayerDescription when the wait ended (<see cref="LoginCheckShell.Vitals"/>), <see cref="LoginCheckShell.Empty"/> when a vital was
    /// missing (the check fails then), and <see cref="LoginCheckShell.NotApplicable"/> before the wait ended.
    /// </summary>
    public string Vitals { get; private set; } = LoginCheckShell.NotApplicable;

    /// <summary>The vitals window the character's options chose when the wait ended: <c>stacked</c> or <c>side</c> (<see cref="LoginCheckShell.VitalsWindow"/>), else <c>n/a</c>.</summary>
    public string VitalsWindow { get; private set; } = LoginCheckShell.NotApplicable;

    /// <summary>What became of the exit leg's quest registry (<see cref="RoutePlay.QuestVerdict"/>: <c>put-back</c>, <c>unchanged</c> or <c>unverified</c>), or <c>n/a</c> when no exit leg is in the check; the verdict line's <c>quest=</c>.</summary>
    public string ExitQuestVerdict => _legs.FirstOrDefault(l => l.Name == RouteLibrary.AcademyExitName)?.QuestVerdict ?? "n/a";

    /// <summary>True when the log off came back to the character list.</summary>
    public bool LogOffOk { get; private set; }

    /// <summary>The character errors the server sent (a retried 0x01 among them), in order.</summary>
    public IReadOnlyList<uint> CharacterErrors => _characterErrors;

    /// <inheritdoc/>
    public double? WhereMaxMetres => _legs.Select(l => l.WhereMaxMetres).Where(w => w is not null).DefaultIfEmpty(null).Max();

    /// <inheritdoc/>
    public long Unmatched => _legs.Sum(l => l.Unmatched);

    /// <summary>
    /// The check's verdict: it ran to the end and nothing failed, and the Academy exit (when it is a leg) ended in the portal's teleport: a leg that took
    /// ACE's quest refusal as its outcome (<see cref="ExitQuestMode.Erase"/>) is not a pass of the check.
    /// </summary>
    public bool Ok => Failure is null && Stage == "done" && (!_legs.Any(l => l.Name == RouteLibrary.AcademyExitName) || ExitTeleported);

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        Subscribe(session);
        if (Failure is not null)
        {
            return ScenarioStatus.Failed;
        }

        var clock = session.Clock.Now;
        var state = session.State;
        if (double.IsNaN(_stageStart))
        {
            _stageStart = clock;
        }

        switch (Stage)
        {
            case "start":
                return Go("login", clock);

            case "login":
                return Login(state, clock);

            case "choosing":
                return Choose(session, clock);

            case "creating":
                if (Settled(out var created))
                {
                    return created.Kind == ActionOutcomeKind.Confirmed ? Go("choosing", clock) : Fail("creating the character", created);
                }

                break;

            case "entering":
                if (Settled(out var entered))
                {
                    if (entered.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail("entering the world", entered);
                    }

                    return Go("arriving", clock);
                }

                break;

            case "arriving":
                return Arriving(state, clock);

            case "settling":
                return Settling(state, clock, session.Actions);

            case "playing":
                return Playing(session, clock, now);

            case "logging-off":
                return LoggingOff(session, clock);

            case "quitting":
                if (Settled(out var quit))
                {
                    return quit.Kind == ActionOutcomeKind.Sent ? Go("done", clock, ScenarioStatus.Succeeded) : Fail("quitting", quit);
                }

                break;

            case "done":
                return ScenarioStatus.Succeeded;

            default:
                return Fail($"TODO(g6-logincheck): unknown login-check stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>Drops the subscriptions on the session's bus.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Subscribe(IGameSessionView session)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _subscriptions.Add(session.Events.Subscribe<SelfTravelChanged>((in e) =>
        {
            _teleportInFlight = e.Travel != SelfTravel.Present;
            if (e.Travel == SelfTravel.Materialising)
            {
                _loginCompletes++;
            }
        }));
        _subscriptions.Add(session.Events.Subscribe<PlayerTeleportAnnounced>((in _) =>
        {
            // Published before the travel change it causes: the flag still says whether an earlier teleport was in flight.
            _teleports++;
            if (!_teleportInFlight)
            {
                _teleportEpisodes++;
            }
        }));
        _subscriptions.Add(session.Events.Subscribe<CharacterErrorReceived>((in e) => _characterErrors.Add(e.Code)));
    }

    private ScenarioStatus Login(GameState state, double clock)
    {
        if (state.Phase == SessionPhase.Idle && clock - _stageStart > HostLoginSeconds)
        {
            return Fail($"login: the host never started the login ({HostLoginSeconds:0} s on the session clock; the session host logs in when it starts)");
        }

        if (state.Phase == SessionPhase.Failed)
        {
            return Fail($"login: the connection failed ({state.FailureMessage ?? "no reason given"})");
        }

        if (state.Phase == SessionPhase.CharacterSelect)
        {
            if (state.DataCheck != DataCheckState.Complete)
            {
                return Fail($"login: the character list came without a completed data check ({state.DataCheck})");
            }

            return Go("choosing", clock);
        }

        return clock - _stageStart > DefaultListSeconds
            ? Fail($"login: the character list did not come within {DefaultListSeconds:0} s (phase {state.Phase}, data check {state.DataCheck})")
            : ScenarioStatus.Running;
    }

    private ScenarioStatus Choose(IGameSessionView session, double clock)
    {
        foreach (var c in session.State.Characters.Entries)
        {
            if (string.Equals(c.Name, Character, StringComparison.OrdinalIgnoreCase))
            {
                if (c.PendingDeletion)
                {
                    return Fail("choosing: the check's character is pending deletion (restore it first)");
                }

                EnteredGuid = c.Guid;
                session.Actions.SelectCharacter(c.Guid, ActionSource.Host);
                _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
                EnterAt = clock;
                return Go("entering", clock);
            }
        }

        if (_createTried)
        {
            return Fail("choosing: the character the server confirmed is not in the character list");
        }

        _createTried = true;
        Created = true;
        _pending = session.Actions.CreateCharacter(HeadlessCharacter.Intent(Character, _skillClasses), ActionSource.Host);
        return Go("creating", clock);
    }

    private ScenarioStatus Arriving(GameState state, double clock)
    {
        if (state.Self.Travel == SelfTravel.Present)
        {
            ArrivalCell = state.Self.Position?.CellId ?? 0;
            ArrivalPosition = state.Self.Position;
            if (ArrivalCell >> 16 != AcademyLandblock)
            {
                return Fail(string.Create(CultureInfo.InvariantCulture, $"arriving: the player arrived in cell 0x{ArrivalCell:X8}, not in the Training Academy's landblock 0x{AcademyLandblock:X4}"));
            }

            if (_loginCompletes != 1)
            {
                return Fail($"arriving: LoginComplete was sent {_loginCompletes} times before the unhide, not once");
            }

            _presentAt = clock;
            return Go("settling", clock);
        }

        if (state.Phase is SessionPhase.Failed or SessionPhase.CharacterSelect)
        {
            return Fail($"arriving: the session left the world before the player was unhidden (phase {state.Phase}{(state.FailureMessage is null ? string.Empty : ", " + state.FailureMessage)})");
        }

        return clock - _stageStart > ArrivalSeconds
            ? Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"arriving: the player was not unhidden within {ArrivalSeconds:0.#} s (travel {state.Self.Travel}, LoginCompletes {_loginCompletes}); portal space sends LoginComplete when the destination is ready"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus Settling(GameState state, double clock, IActionApi actions)
    {
        if (state.Phase != SessionPhase.InWorld || state.Self.Travel != SelfTravel.Present || state.Self.Hidden)
        {
            return Fail($"settling: the player did not stay in the world (phase {state.Phase}, travel {state.Self.Travel}, hidden {state.Self.Hidden})");
        }

        if (_teleports != 0 || _loginCompletes != 1)
        {
            return Fail($"settling: teleports {_teleports} (want 0), LoginCompletes {_loginCompletes} (want 1)");
        }

        if (clock - _presentAt < SettleSeconds)
        {
            return ScenarioStatus.Running;
        }

        Vitals = LoginCheckShell.Vitals(state.Self.Qualities);
        VitalsWindow = LoginCheckShell.VitalsWindow(state.Self.Qualities);
        if (!LoginCheckShell.VitalsFilled(Vitals))
        {
            return Fail($"settling: the PlayerDescription left a vital empty (vitals {Vitals}; the shell's meters would be empty)");
        }

        SettledAt = clock;
        var held = 0;
        foreach (var o in state.Objects.All)
        {
            if (o.Guid != state.Self.Guid)
            {
                held++;
            }
        }

        ObjectsHeld = held;
        Census = WorldContentCensus.Count(state);
        AcademyObjects = Census.Value.Total;
        _teleportsAtSettle = _teleports;
        _episodesAtSettle = _teleportEpisodes;
        return _legs.Count > 0 ? Go("playing", clock) : BeginLogOff(actions, clock);
    }

    private ScenarioStatus BeginLogOff(IActionApi actions, double clock)
    {
        _pending = _requestLogOff is null ? actions.LogOff(ActionSource.Host) : _requestLogOff();
        return _pending is null ? Fail("logging off: the host's log-off key did not ask for a log off") : Go("logging-off", clock);
    }

    private ScenarioStatus Playing(IGameSessionView session, double clock, double now)
    {
        if (session.State.Phase != SessionPhase.InWorld)
        {
            return Fail($"playing {_legs[_leg].Name}: the session left the world (phase {session.State.Phase})");
        }

        var leg = _legs[_leg];
        switch (leg.Step(session, now))
        {
            case ScenarioStatus.Failed:
                return Fail($"playing {leg.Name} ({leg.Stage}): {leg.Failure ?? "the scenario failed without saying why"}");
            case ScenarioStatus.Succeeded:
                _leg++;
                return _leg < _legs.Count ? Go("playing", clock) : BeginLogOff(session.Actions, clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus LoggingOff(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (Settled(out var loggedOff))
        {
            if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail("logging off", loggedOff);
            }

            if (state.Phase != SessionPhase.CharacterSelect || state.Characters.Entries.Count == 0)
            {
                return Fail($"logging off: the log off was confirmed but the session is at {state.Phase} with {state.Characters.Entries.Count} characters, not at a character list");
            }

            LogOffOk = true;
            _pending = session.Actions.Quit(ActionSource.Host);
            return Go("quitting", clock);
        }

        return clock - _stageStart > DefaultLogOffSeconds
            ? Fail($"logging off: the server did not return the character list within {DefaultLogOffSeconds:0} s (phase {state.Phase})")
            : ScenarioStatus.Running;
    }

    private bool Settled(out ActionOutcome outcome)
    {
        if (_pending is { Outcome: { } done })
        {
            _pending = null;
            outcome = done;
            return true;
        }

        outcome = default;
        return false;
    }

    private ScenarioStatus Go(string stage, double clock, ScenarioStatus status = ScenarioStatus.Running)
    {
        Stage = stage;
        _stageStart = clock;
        return status;
    }

    private ScenarioStatus Fail(string step, in ActionOutcome outcome) =>
        Fail(step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) +
             (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    private ScenarioStatus Fail(string why)
    {
        Failure = why + (_characterErrors.Count == 0 ? string.Empty : " [character errors: " + string.Join(", ", _characterErrors.Select(c => "0x" + c.ToString("X2", CultureInfo.InvariantCulture))) + "]");
        return ScenarioStatus.Failed;
    }
}
