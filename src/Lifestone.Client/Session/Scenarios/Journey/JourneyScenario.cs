using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// A journey (M4 P5b 3.4): one character's stations in one session, from Holtburg's lifestone back to it. Each stage is logged once:
/// <list type="bullet">
/// <item><c>login</c>: a <see cref="LoginScenario"/> that stays in the world; skipped when the session is already in the world (as the hunt does).</item>
/// <item><c>known-state</c>: present, standing in Holtburg (else the pre-journey recovery place, <c>recovery_place=1</c>, never budgeted), in peace.</item>
/// <item>per station: <c>travel:&lt;name&gt;</c> (to the station's <see cref="StationNeeds.Where"/> through the context's travel seam: Holtburg is
/// <see cref="TravelPurpose.Home"/>, the belt and the forest <see cref="TravelPurpose.Ground"/>; the den is reached by the den-travel station itself; Here
/// travels nowhere), <c>vitals:&lt;name&gt;</c> (the keeper's pass to the station's needs, bounded by <see cref="VitalsTimeoutSeconds"/>; peace when it needs
/// peace), <c>station:&lt;name&gt;</c> (the station's run, bounded by its expected seconds times <see cref="StationLimitFactor"/> plus <see cref="StationLimitMarginSeconds"/>).</item>
/// <item><c>recover:&lt;name&gt;</c> after a failed station: the keeper, then home to Holtburg when the next station (or the journey's end) needs it; when that
/// way home fails, the mage in the den uses the suicide tool (<c>recovery=suicide</c>, its respawn is the lifestone) and anyone else one recovery place
/// (<c>recovery_places=</c>).</item>
/// <item><c>logoff</c>: the done line, then the log off and the quit (unless <c>endSession:false</c>).</item>
/// </list>
/// A station whose needs cannot be restored (the travel failed, the keeper is blocked, a need is still missing) is <c>skipped=precondition:&lt;x&gt;</c>.
/// The verdict lines are <see cref="JourneyVerdict"/>'s. The constants are Lifestone's own.
/// </summary>
public sealed class JourneyScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "journey";

    /// <summary>Seconds a place (budgeted or a recovery) has, once Confirmed, for the player to be present at the destination.</summary>
    public const double PlaceArrivalSeconds = 60;

    /// <summary>Seconds the player stands after a place's arrival before the journey goes on.</summary>
    public const double SettleSeconds = 1.0;

    /// <summary>Seconds one vitals pass may take (a rest for mana is the long one).</summary>
    public const double VitalsTimeoutSeconds = 420;

    /// <summary>Seconds the peace mode has to show after it was asked for.</summary>
    public const double PeaceSeconds = 15;

    /// <summary>Seconds the suicide recovery has to bring the player back present in Holtburg (death, the respawn, portal space).</summary>
    public const double SuicideSeconds = 120;

    /// <summary>A station may run this many times its expected seconds (plus <see cref="StationLimitMarginSeconds"/>) before it is ended <c>why=timeout:&lt;stage&gt;</c>.</summary>
    public const double StationLimitFactor = 3;

    /// <summary>The margin on a station's limit.</summary>
    public const double StationLimitMarginSeconds = 300;

    private readonly JourneyPlan _plan;
    private readonly IStationFactory _factory;
    private readonly JourneyContext _context;
    private readonly bool _endSession;
    private readonly double _arrivalSeconds;
    private readonly JourneyPlace _place;
    private readonly Func<uint, float, float, float?>? _height;
    private readonly JourneyVerdict _verdict;
    private readonly ILogSink _log;
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
    private readonly List<string> _lines = [];

    private Phase _phase;
    private double _clock;
    private double _phaseStart;
    private LoginScenario? _login;
    private int _index;
    private StationSpec? _spec;
    private IStation? _station;
    private readonly Dictionary<int, IStation> _made = [];
    private StationNeeds _needs = StationNeeds.None;
    private ITravelLeg? _leg;
    private StationWhere _legWhere;
    private ActionHandle? _pending;
    private IStationRun? _run;
    private double _runStart;
    private StationWhere _recoverTarget;
    private int _recoverNext;
    private bool _recoverBySuicide;
    private double? _whereMax;
    private long _unmatched;
    private string? _peaceWhy;

    private enum Phase
    {
        Start,
        Login,
        KnownPresent,
        KnownPlace,
        KnownArrive,
        KnownPeace,
        Travel,
        TravelArrive,
        Vitals,
        Peace,
        Station,
        RecoverVitals,
        RecoverHome,
        RecoverPlacing,
        RecoverArrive,
        LogOff,
        Quit,
        Done,
    }

    /// <summary>A journey over <paramref name="plan"/>.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="factory">Makes each station from its spec.</param>
    /// <param name="context">The journey's context (its <see cref="JourneyContext.Facts"/>' budget is set from the plan here).</param>
    /// <param name="endSession">False for a host that logs off itself: the journey ends in the world after the done line.</param>
    /// <param name="arrivalSeconds">The login's arrival limit.</param>
    /// <param name="place">The place verb for the recovery places, or null for <see cref="JourneyTravel.ToolPlace"/>.</param>
    /// <param name="groundHeight">The terrain height at (cell, x, y) for the belt's and the forest's place, or null to read it from the context's dats.</param>
    public JourneyScenario(JourneyPlan plan, IStationFactory factory, JourneyContext context, bool endSession = true, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, JourneyPlace? place = null, Func<uint, float, float, float?>? groundHeight = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _endSession = endSession;
        _arrivalSeconds = arrivalSeconds;
        _place = place ?? JourneyTravel.ToolPlace;
        _height = groundHeight ?? (context.Dats is { } dats ? (cell, x, y) => HuntGround.SeekHeight(dats, cell, x, y) : null);
        _log = context.Log;
        _verdict = new JourneyVerdict(plan.Stations.Count);
        context.Facts.TeleportBudget = plan.TeleportBudget;
    }

    /// <summary>
    /// A journey's context with its travel (<see cref="JourneyTravel"/>) over one shared <see cref="JourneyFacts"/> whose budget is the plan's: what the hosts (J4, J5) build.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="log">The log.</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="keeper">The vitals keeper, or null.</param>
    /// <param name="dats">The dats, or null.</param>
    /// <param name="runners">The host's runner, or null.</param>
    /// <param name="observer">The host's observer, or null for none.</param>
    /// <param name="place">The place verb, or null for <see cref="JourneyTravel.ToolPlace"/>.</param>
    /// <param name="windowFrames">The host's window frame times (<see cref="JourneyWindowSamplers"/>), or null for none (the station lines print <c>n/a</c>).</param>
    public static JourneyContext NewContext(JourneyPlan plan, ILogSink log, IReadOnlyDictionary<uint, SpellInfo> spells, VitalsKeeper? keeper = null, DatSet? dats = null, IJourneyRunnerHost? runners = null, IJourneyObserver? observer = null, JourneyPlace? place = null, IJourneyWindowFrames? windowFrames = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var facts = new JourneyFacts { TeleportBudget = plan.TeleportBudget };
        return new JourneyContext
        {
            Character = plan.Character,
            Account = plan.Account,
            Log = log,
            Travel = new JourneyTravel(plan.Character, facts, log, runners, place),
            Spells = spells,
            Keeper = keeper,
            Dats = dats,
            Runners = runners,
            Observer = observer ?? NoJourneyObserver.Instance,
            Facts = facts,
            WindowFrames = windowFrames,
        };
    }

    /// <inheritdoc />
    public string Name => ScenarioName;

    /// <inheritdoc />
    public string Stage { get; private set; } = "start";

    /// <inheritdoc />
    public string? Failure { get; private set; }

    /// <inheritdoc />
    public double? WhereMaxMetres => _whereMax;

    /// <inheritdoc />
    public long Unmatched => _unmatched;

    /// <summary>The station that is running now, or null between stations (the Godot host reads the wrapped scenario for its in-station review frames).</summary>
    public IStationRun? CurrentRun => _run;

    /// <summary>The plan.</summary>
    public JourneyPlan Plan => _plan;

    /// <summary>The context.</summary>
    public JourneyContext Context => _context;

    /// <summary>The verdict so far.</summary>
    public JourneyVerdict Verdict => _verdict;

    /// <summary>The verdict lines written so far (each station's, then the done line), bare (the Godot host prefixes <c>[logincheck]</c>).</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _clock = session.Clock.Now;
        switch (_phase)
        {
            case Phase.Start:
                return Begin(session);
            case Phase.Login:
                return LoginStep(session, now);
            case Phase.KnownPresent:
            case Phase.KnownPlace:
            case Phase.KnownArrive:
            case Phase.KnownPeace:
                return KnownState(session);
            case Phase.Travel:
            case Phase.TravelArrive:
                return TravelStep(session, now);
            case Phase.Vitals:
                return VitalsStep(session, now);
            case Phase.Peace:
                return PeaceStep(session, now);
            case Phase.Station:
                return StationStep(session, now);
            case Phase.RecoverVitals:
            case Phase.RecoverHome:
            case Phase.RecoverPlacing:
            case Phase.RecoverArrive:
                return RecoverStep(session, now);
            case Phase.LogOff:
            case Phase.Quit:
                return LeaveStep(session);
            default:
                return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
        }
    }

    /// <summary>Releases a travel leg or a station run still held.</summary>
    public void Dispose()
    {
        DisposeLeg();
        DisposeRun();
    }

    // ------------------------------------------------------------------------------------------------ login and the known state

    private ScenarioStatus Begin(IGameSessionView session)
    {
        if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
        {
            _logged.Add("login");
            _log.Info(JourneyVerdict.LogCategory, $"journey stage login skipped=in-world ({_plan.Name}: {_plan.Character})");
            return Enter("known-state", Phase.KnownPresent);
        }

        _login = new LoginScenario(_plan.Character, arrivalSeconds: _arrivalSeconds) { StayInWorld = true };
        return Enter("login", Phase.Login);
    }

    private ScenarioStatus LoginStep(IGameSessionView session, double now)
    {
        switch (_login!.Step(session, now))
        {
            case ScenarioStatus.Succeeded:
                _whereMax = Max(_whereMax, _login.WhereMaxMetres);
                return Enter("known-state", Phase.KnownPresent);
            case ScenarioStatus.Failed:
                return Abort("login: " + (_login.Failure ?? "the login failed without saying why"));
            default:
                return ScenarioStatus.Running;
        }
    }

    private ScenarioStatus KnownState(IGameSessionView session)
    {
        var self = session.State.Self;
        switch (_phase)
        {
            case Phase.KnownPresent:
                if (self.Travel != SelfTravel.Present)
                {
                    return Elapsed > _arrivalSeconds ? Abort($"known-state: the player was not present within {_arrivalSeconds:0} s (travel {self.Travel})") : ScenarioStatus.Running;
                }

                if (JourneyGrounds.At(StationWhere.Holtburg, self.Position) && !JourneyGrounds.Indoors(self.Position))
                {
                    return Go(Phase.KnownPeace);
                }

                // Not in Holtburg, or indoors in its landblock (an EnvCell, cell index >= 0x100: a shop's back room, a cellar an earlier test left the character in): outdoors at the lifestone is the known state.
                _verdict.RecoveryPlace = 1;
                _log.Warn(JourneyVerdict.LogCategory, $"journey known-state: the player is {(JourneyGrounds.Indoors(self.Position) ? "indoors" : "not in Holtburg")} ({Cell(session)}): the pre-journey recovery place (recovery_place=1, never budgeted)");
                _pending = _place(session, JourneyGrounds.Holtburg);
                return Go(Phase.KnownPlace);

            case Phase.KnownPlace:
                if (_pending?.Outcome is not { } placed)
                {
                    return ScenarioStatus.Running;
                }

                _pending = null;
                return placed.Kind == ActionOutcomeKind.Confirmed ? Go(Phase.KnownArrive) : Abort("known-state: the recovery place to Holtburg: " + Describe(placed));

            case Phase.KnownArrive:
                if (self.Travel == SelfTravel.Present && JourneyGrounds.At(StationWhere.Holtburg, self.Position) && !JourneyGrounds.Indoors(self.Position) && Elapsed >= SettleSeconds)
                {
                    return Go(Phase.KnownPeace);
                }

                return Elapsed > PlaceArrivalSeconds ? Abort($"known-state: the recovery place did not arrive in Holtburg within {PlaceArrivalSeconds:0} s ({Cell(session)}, travel {self.Travel})") : ScenarioStatus.Running;

            default:
                switch (Peace(session))
                {
                    case true:
                        _log.Info(JourneyVerdict.LogCategory, $"journey known-state peace=yes holtburg=yes recovery_place={_verdict.RecoveryPlace}");
                        return BeginStation(session, 0);
                    case false:
                        return Abort("known-state: peace: " + (_peaceWhy ?? "the peace mode did not show"));
                    default:
                        return ScenarioStatus.Running;
                }
        }
    }

    /// <summary>Asks for peace when the player is not in it: true in peace, false on a refusal or after <see cref="PeaceSeconds"/>, null while waiting.</summary>
    private bool? Peace(IGameSessionView session)
    {
        if (session.State.Self.Qualities.CombatMode == CombatModes.NonCombat)
        {
            _pending = null;
            return true;
        }

        if (_pending is null)
        {
            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat);
            return null;
        }

        if (_pending.Outcome is { } outcome && !outcome.Succeeded)
        {
            _peaceWhy = Describe(outcome);
            _pending = null;
            return false;
        }

        if (Elapsed > PeaceSeconds)
        {
            _peaceWhy = $"the combat mode is still {session.State.Self.Qualities.CombatMode} after {PeaceSeconds:0} s";
            _pending = null;
            return false;
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ the stations

    /// <summary>The station for plan index <paramref name="index"/>: made once by the factory (the next station's place is read before it begins).</summary>
    private IStation StationAt(int index)
    {
        if (!_made.TryGetValue(index, out var station))
        {
            station = _factory.Create(_plan.Stations[index]);
            _made[index] = station;
        }

        return station;
    }

    /// <summary>The needs of plan index <paramref name="index"/>: the plan's override, else the station's own (none when the factory has no station for it).</summary>
    private StationNeeds NeedsOf(int index)
    {
        if (_plan.Stations[index].NeedsOverride is { } over)
        {
            return over;
        }

        try
        {
            return StationAt(index).Needs;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return StationNeeds.None; // BeginStation reports the factory's refusal when the station is reached
        }
    }

    private ScenarioStatus BeginStation(IGameSessionView session, int index)
    {
        if (index >= _plan.Stations.Count)
        {
            return Finish(session);
        }

        _index = index;
        _spec = _plan.Stations[index];
        _station = null;
        try
        {
            _station = StationAt(index);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            _log.Error(JourneyVerdict.LogCategory, $"journey station {_spec.Name}: the factory made no station for kind '{_spec.Kind}': {e.Message}");
            return EndStation(session, ok: false, why: "factory:" + e.Message, fields: "kind=" + _spec.Kind, seconds: 0);
        }

        _needs = _spec.NeedsOverride ?? _station.Needs;
        if (_spec.NeedsOverride is { } over && over != _station.Needs)
        {
            _log.Warn(JourneyVerdict.LogCategory, $"journey station {_spec.Name}: the plan overrides the station's own needs ({_station.Needs}) with ({over}); the override is restored and checked");
        }

        Enter("travel:" + _spec.Name, Phase.Travel);
        var where = _needs.Where;
        if (where is StationWhere.Here or StationWhere.Den || JourneyGrounds.At(where, session.State.Self.Position))
        {
            if (where == StationWhere.Den && !JourneyGrounds.At(where, session.State.Self.Position))
            {
                _log.Info(JourneyVerdict.LogCategory, $"journey travel:{_spec.Name}: the den is reached by the den-travel station itself; nothing travels here");
            }

            return BeginVitals();
        }

        TravelRequest request;
        if (where == StationWhere.Holtburg)
        {
            request = new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Home);
        }
        else if (JourneyGrounds.For(where, _height) is { } ground)
        {
            request = new TravelRequest(ground, TravelPurpose.Ground);
        }
        else
        {
            _log.Warn(JourneyVerdict.LogCategory, $"TODO(m4-journey-ground-height): no terrain height for the {where} place (the journey has no dats): station {_spec.Name} is skipped");
            return Skip(session, "where", "travel_why=no-terrain-height");
        }

        _legWhere = where;
        _leg = _context.Travel.GoTo(session, request, _clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus TravelStep(IGameSessionView session, double now)
    {
        if (_phase == Phase.TravelArrive)
        {
            var self = session.State.Self;
            if (self.Travel == SelfTravel.Present && JourneyGrounds.At(_legWhere, self.Position) && Elapsed >= SettleSeconds)
            {
                return BeginVitals();
            }

            return Elapsed > PlaceArrivalSeconds ? Skip(session, "where", $"travel_why=place-arrival:{Cell(session)}") : ScenarioStatus.Running;
        }

        switch (_leg!.Step(session, now))
        {
            case TravelStatus.Arrived:
                var placed = _leg.Means == TravelMeans.Place;
                DisposeLeg();
                return placed ? Go(Phase.TravelArrive) : BeginVitals();
            case TravelStatus.Failed:
                var why = _leg.Failure ?? "travel:failed";
                DisposeLeg();
                _log.Warn(JourneyVerdict.LogCategory, $"journey travel:{_spec!.Name}: {why}: the station is skipped");
                return Skip(session, "where", "travel_why=" + JourneyVerdict.Token(why));
            default:
                return ScenarioStatus.Running;
        }
    }

    private ScenarioStatus BeginVitals()
    {
        Enter("vitals:" + _spec!.Name, Phase.Vitals);
        if (_context.Keeper is not { } keeper)
        {
            if (_logged.Add("vitals:no-keeper"))
            {
                _log.Info(JourneyVerdict.LogCategory, "journey vitals: the journey runs no keeper; the stations' needs are only checked");
            }

            return Go(Phase.Peace);
        }

        var n = _needs;
        if (!keeper.NeedsFor(new VitalsNeeds((int)Math.Ceiling(n.MinHealthPercent), (int)Math.Ceiling(n.MinStaminaPercent), (int)Math.Ceiling(n.MinManaPercent))))
        {
            _verdict.VitalsPasses++;
            return ScenarioStatus.Running;
        }

        return Go(Phase.Peace);
    }

    private ScenarioStatus VitalsStep(IGameSessionView session, double now)
    {
        var keeper = _context.Keeper!;
        switch (keeper.Step(now))
        {
            case VitalsKeeperState.Ready:
                return Go(Phase.Peace);
            case VitalsKeeperState.Blocked:
                return Skip(session, "vitals:" + (keeper.Why ?? "blocked"), string.Empty);
            default:
                return Elapsed > VitalsTimeoutSeconds ? Skip(session, "vitals:timeout", string.Empty) : ScenarioStatus.Running;
        }
    }

    private ScenarioStatus PeaceStep(IGameSessionView session, double now)
    {
        if (_needs.Peace)
        {
            switch (Peace(session))
            {
                case false:
                    _log.Warn(JourneyVerdict.LogCategory, $"journey vitals:{_spec!.Name}: peace: {_peaceWhy}");
                    return Skip(session, "peace", string.Empty);
                case null:
                    return ScenarioStatus.Running;
            }
        }

        if (StationPreconditions.Miss(_needs, session.State, _log) is { } miss)
        {
            return Skip(session, miss, string.Empty);
        }

        Enter("station:" + _spec!.Name, Phase.Station);
        _run = _station!.Start(_context);
        _runStart = _clock;
        _context.Observer.StationStarting(_spec.Name, now);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus StationStep(IGameSessionView session, double now)
    {
        var run = _run!;
        var status = run.Step(session, now);
        var seconds = _clock - _runStart;
        if (status == ScenarioStatus.Running)
        {
            var limit = (_spec!.ExpectedSeconds * StationLimitFactor) + StationLimitMarginSeconds;
            return seconds > limit ? EndRun(session, now, ok: false, why: string.Create(CultureInfo.InvariantCulture, $"timeout:{run.Stage} (over {limit:0} s)")) : ScenarioStatus.Running;
        }

        var ok = status == ScenarioStatus.Succeeded && run.Ok;
        return EndRun(session, now, ok, ok ? null : run.Failure ?? (status == ScenarioStatus.Succeeded ? "verdict" : "failed"));
    }

    private ScenarioStatus EndRun(IGameSessionView session, double now, bool ok, string? why)
    {
        var run = _run!;
        _whereMax = Max(_whereMax, run.WhereMaxMetres);
        _unmatched += run.Unmatched;
        string fields;
        try
        {
            fields = run.VerdictFields();
        }
        catch (InvalidOperationException e)
        {
            fields = "fields_why=" + JourneyVerdict.Token(e.Message);
        }

        if (run is ScriptLeg leg)
        {
            _context.Facts.Granted += leg.Granted; // the journey adds a scripted station's grants once (stations do not add them themselves)
        }

        var seconds = _clock - _runStart;
        DisposeRun();
        return EndStation(session, ok, why, fields, seconds, now);
    }

    private ScenarioStatus EndStation(IGameSessionView session, bool ok, string? why, string fields, double seconds, double? now = null)
    {
        var name = _spec!.Name;
        Emit(_verdict.Add(new JourneyStationResult(name, ok, seconds, fields, ok ? null : why)));
        _context.Observer.StationEnded(name, ok, now ?? _clock);
        if (ok)
        {
            return BeginStation(session, _index + 1);
        }

        var next = _index + 1;
        return BeginRecover(next < _plan.Stations.Count ? NeedsOf(next).Where : StationWhere.Holtburg, next);
    }

    private ScenarioStatus Skip(IGameSessionView session, string miss, string fields)
    {
        DisposeLeg();
        var name = _spec!.Name;
        var cause = _verdict.FirstFailed;
        _log.Warn(JourneyVerdict.LogCategory, $"journey station {name}: skipped=precondition:{miss}" + (cause is null ? string.Empty : $" (after {cause} failed)"));
        Emit(_verdict.Add(new JourneyStationResult(name, false, 0, fields, null, "precondition:" + miss, cause)));
        _context.Observer.StationEnded(name, false, _clock);
        return BeginStation(session, _index + 1);
    }

    // ------------------------------------------------------------------------------------------------ recover

    private ScenarioStatus BeginRecover(StationWhere target, int next)
    {
        _recoverTarget = target;
        _recoverNext = next;
        _recoverBySuicide = false;
        Enter("recover:" + _spec!.Name, Phase.RecoverVitals);
        if (_context.Keeper is { } keeper && !keeper.NeedsFor(default))
        {
            _verdict.VitalsPasses++;
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus RecoverStep(IGameSessionView session, double now)
    {
        var self = session.State.Self;
        switch (_phase)
        {
            case Phase.RecoverVitals:
                if (_context.Keeper is { } keeper && !keeper.Ready)
                {
                    var state = keeper.Step(now);
                    if (state is VitalsKeeperState.Working or VitalsKeeperState.Resting && Elapsed <= VitalsTimeoutSeconds)
                    {
                        return ScenarioStatus.Running;
                    }

                    if (state != VitalsKeeperState.Ready)
                    {
                        _log.Warn(JourneyVerdict.LogCategory, $"journey recover: the keeper is {state} ({keeper.Why ?? "timeout"}); going on");
                    }
                }

                if (_recoverTarget != StationWhere.Holtburg || JourneyGrounds.At(StationWhere.Holtburg, self.Position))
                {
                    return BeginStation(session, _recoverNext);
                }

                _leg = _context.Travel.GoTo(session, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Home), now);
                _legWhere = StationWhere.Holtburg;
                return Go(Phase.RecoverHome);

            case Phase.RecoverHome:
                switch (_leg!.Step(session, now))
                {
                    case TravelStatus.Arrived:
                        var placed = _leg.Means == TravelMeans.Place;
                        DisposeLeg();
                        return placed ? Go(Phase.RecoverArrive) : BeginStation(session, _recoverNext);
                    case TravelStatus.Failed:
                        var why = _leg.Failure ?? "travel:failed";
                        DisposeLeg();
                        return RecoverFallback(session, why);
                    default:
                        return ScenarioStatus.Running;
                }

            case Phase.RecoverPlacing:
                if (_pending?.Outcome is not { } outcome)
                {
                    return ScenarioStatus.Running;
                }

                _pending = null;
                if (!outcome.Succeeded)
                {
                    _log.Error(JourneyVerdict.LogCategory, $"journey recover: the {(_recoverBySuicide ? "suicide" : "recovery place")} was refused: {Describe(outcome)}");
                    return BeginStation(session, _recoverNext);
                }

                return Go(Phase.RecoverArrive);

            default:
                if (self.Travel == SelfTravel.Present && JourneyGrounds.At(StationWhere.Holtburg, self.Position) && Elapsed >= SettleSeconds)
                {
                    _log.Info(JourneyVerdict.LogCategory, $"journey recover: back in Holtburg ({(_recoverBySuicide ? "recovery=suicide" : "recovery_places=" + _verdict.RecoveryPlaces.ToString(CultureInfo.InvariantCulture))})");
                    return BeginStation(session, _recoverNext);
                }

                var limit = _recoverBySuicide ? SuicideSeconds : PlaceArrivalSeconds;
                if (Elapsed > limit)
                {
                    _log.Error(JourneyVerdict.LogCategory, $"journey recover: not back in Holtburg within {limit:0} s ({Cell(session)}, travel {self.Travel})");
                    return BeginStation(session, _recoverNext);
                }

                return ScenarioStatus.Running;
        }
    }

    private ScenarioStatus RecoverFallback(IGameSessionView session, string why)
    {
        if ((string.Equals(_plan.Name, JourneyPlans.Mage.Name, StringComparison.Ordinal) || string.Equals(_plan.Name, JourneyPlans.HeroMage.Name, StringComparison.Ordinal)) && JourneyGrounds.At(StationWhere.Den, session.State.Self.Position))
        {
            // The mage in the den with its hop spent: the suicide tool; the respawn is Holtburg's lifestone (its bind), the vitae stage pays for it.
            _log.Warn(JourneyVerdict.LogCategory, $"journey recover: the way home failed in the den ({why}): recovery=suicide");
            _verdict.Recovery = "suicide";
            _recoverBySuicide = true;
            _pending = session.Actions.Suicide(ActionSource.Host);
            return Go(Phase.RecoverPlacing);
        }

        _verdict.RecoveryPlaces++;
        _log.Warn(JourneyVerdict.LogCategory, $"journey recover: the way home failed ({why}): one recovery place to Holtburg (recovery_places={_verdict.RecoveryPlaces}, outside the budget, judged 0 from the second gate)");
        _pending = _place(session, JourneyGrounds.Holtburg);
        return Go(Phase.RecoverPlacing);
    }

    // ------------------------------------------------------------------------------------------------ the end

    private ScenarioStatus Finish(IGameSessionView session)
    {
        Enter("logoff", Phase.LogOff);
        WriteDone();
        if (!_endSession)
        {
            return Go(Phase.Done, Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed);
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus LeaveStep(IGameSessionView session)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (_phase == Phase.LogOff)
        {
            if (outcome.Kind != ActionOutcomeKind.Confirmed)
            {
                _log.Warn(JourneyVerdict.LogCategory, "journey logoff: " + Describe(outcome));
            }

            _pending = session.Actions.Quit(ActionSource.Host);
            return Go(Phase.Quit);
        }

        if (outcome.Kind != ActionOutcomeKind.Sent)
        {
            _log.Warn(JourneyVerdict.LogCategory, "journey quit: " + Describe(outcome));
        }

        return Go(Phase.Done, Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed);
    }

    private void WriteDone()
    {
        var facts = _context.Facts;
        var travel = _context.Travel as JourneyTravel;
        Emit(_verdict.DoneLine(_plan.Name, facts, travel?.RouteMetres ?? 0, travel?.RunSeconds ?? 0, travel?.Rungs ?? 0, travel?.Blocked ?? 0, travel?.Sightless ?? 0));
        if (!_verdict.Ok(facts))
        {
            Failure ??= string.Create(CultureInfo.InvariantCulture, $"journey {_plan.Name} ok=false: failed={_verdict.FirstFailed ?? "none"} teleports={facts.Teleports}/{facts.TeleportBudget} stations={_verdict.Stations.Count(r => r.Ok)}/{_verdict.Planned}");
        }
    }

    private ScenarioStatus Abort(string why)
    {
        Failure = why;
        _log.Error(JourneyVerdict.LogCategory, "journey: " + why);
        DisposeLeg();
        DisposeRun();
        WriteDone();
        return Go(Phase.Done, ScenarioStatus.Failed);
    }

    // ------------------------------------------------------------------------------------------------ plumbing

    private double Elapsed => _clock - _phaseStart;

    private ScenarioStatus Enter(string stage, Phase phase)
    {
        Stage = stage;
        if (_logged.Add(stage))
        {
            _log.Info(JourneyVerdict.LogCategory, "journey stage " + stage);
        }

        return Go(phase);
    }

    private ScenarioStatus Go(Phase phase, ScenarioStatus status = ScenarioStatus.Running)
    {
        _phase = phase;
        _phaseStart = _clock;
        return status;
    }

    private void Emit(string line)
    {
        _lines.Add(line);
        _log.Info(JourneyVerdict.LogCategory, line);
    }

    private void DisposeLeg()
    {
        _leg?.Dispose();
        _leg = null;
    }

    private void DisposeRun()
    {
        (_run as IDisposable)?.Dispose();
        _run = null;
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static string Cell(IGameSessionView session) =>
        session.State.Self.Position is { } at ? "cell 0x" + at.CellId.ToString("X8", CultureInfo.InvariantCulture) : "no position";

    private static string Describe(ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
}
