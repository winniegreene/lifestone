using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the villa track knows across the runs of one session (the scenario is made again each run): the half pair the last hand-in left at the Agent, and whether this session asked the server for the house status.</summary>
public sealed class VillaMemory
{
    private static readonly ConditionalWeakTable<GameState, VillaMemory> PerSession = new();

    /// <summary>The memory of the session behind <paramref name="state"/>.</summary>
    /// <param name="state">The session's read model.</param>
    public static VillaMemory For(GameState state) => PerSession.GetOrCreateValue(state ?? throw new ArgumentNullException(nameof(state)));

    /// <summary>True when the last hand-in ended on the Agent's first-stone line (<c>half_pending=yes</c>). Lost with the session: a stone given then is only a new first stone, never a loss.</summary>
    public bool HalfPending { get; set; }

    /// <summary>True once this session sent its HouseQuery.</summary>
    public bool Queried { get; set; }
}

/// <summary>
/// M6 villa, the climb's wiring (the owner's ruling, 2026-11-15: packs first, and then ideally a villa): the level up job's villa schedule (<see cref="VillaSchedule"/>) at each town trip's boundary. The pieces are the villa session's stations
/// (<c>Session/Housing</c>); this file only calls them, by rule:
/// <list type="number">
/// <item>Each tick (<see cref="VillaWatch"/>): with the house status unread, one HouseQuery a session (the steps need it read before they weigh anything).</item>
/// <item>At the logistics trip's end, after a ladder errand that waited for the sale (<see cref="VillaErrand"/>): the schedule's one step, logged with every step it weighed; a station step runs in the Trip state (<c>_villaActive</c>), a stone fetch is
/// a ladder errand of the quest station on the cave's file (<see cref="StartErrand"/>: from home, ready and buffed as an errand starts). Each step at most once a boundary; after each, the schedule is asked again, so a fetch can be followed
/// by its hand-in and the lockpick by the purchase.</item>
/// <item>A step's end (<see cref="VillaStepEnded"/>): check after by the station's own verdict line; a failure is remembered for the run (never a loop) and she goes home by the recovery's way, as after a failed ladder errand.</item>
/// <item>The cycle line's <c>villa=</c> (<see cref="VillaMarker"/>): the switch, the phase, the writs against their goal, the stones, the half pair, the money against the price and its trend an hour over the run.</item>
/// </list>
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The seconds of the run the money's trend waits for before it is printed (Lifestone's own: under a quarter hour one sale swings it).</summary>
    public const double VillaTrendSeconds = 900;

    private readonly HashSet<VillaStep> _villaFailed = [];
    private readonly HashSet<VillaStep> _villaBoundary = [];
    private readonly List<uint> _villaScratch = [];
    private bool _villaActive; // the Trip state runs a villa station
    private VillaStep _villaStep;
    private string? _villaFetchKey; // the quest errand running is the villa's stone fetch
    private int _villaBoundaryFetches; // the fetches this boundary started (open or behind: one a walkable cave)
    private int _villaFetchesRun;
    private int _villaStepsRun;
    private double _villaMoneyAt = double.NaN;
    private long _villaMoney0;
    private bool _villaUnreadLogged;

    /// <summary>The time the villa track reads as now (null: the session's clock); a test sets it.</summary>
    internal DateTimeOffset? VillaNowForTest { get; set; }

    /// <summary>M6 villa: the villa steps that ended well this run (the stone fetches counted apart).</summary>
    public int VillaSteps => _villaStepsRun;

    /// <summary>M6 villa: the stone fetches that ended with the stone this run.</summary>
    public int VillaFetches => _villaFetchesRun;

    /// <summary>True when the villa track runs: the switch, the level up job's own loop (not a task, not the den run).</summary>
    private bool VillaOn => _options.Villa && _options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job;

    /// <summary>Each tick: with the track on and the house status unread, the session's one HouseQuery (the answer is read at the next boundary; an unanswered one is named there).</summary>
    private void VillaWatch(IGameSessionView session)
    {
        if (!VillaOn)
        {
            return;
        }

        var memory = VillaMemory.For(session.State);
        if (session.State.HouseStatus == HouseOwnership.Unknown && !memory.Queried)
        {
            memory.Queried = true;
            var query = session.Actions.QueryHouse(ActionSource.Automation);
            _log.Info(LogCategory, $"villa: check before: the house status is unread: one HouseQuery (action {query.Id}); the steps weigh nothing until it is answered");
        }

        if (double.IsNaN(_villaMoneyAt) && session.State.Self.Inventory.IsKnown)
        {
            _villaMoneyAt = _clock;
            _villaMoney0 = VillaReads.Price(session.State, _villaScratch).Money;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"villa: the run's money at its start: {_villaMoney0:N0} pyreals in coins and notes (the price is {VillaFacts.PricePyreals:N0}); the cycle line's per_h is read against it"));
        }
    }

    /// <summary>The schedule's inputs from the live state.</summary>
    private VillaInput VillaRead(IGameSessionView session)
    {
        var state = session.State;
        var owned = state.HouseStatus == HouseOwnership.Owned ? state.House : null;
        var held = VillaReads.Price(state, _villaScratch);
        var coins = PurseView.Coin(state); // M6 16 E5a1: the one coin read, -1 unread
        var notes = PurseView.NotesCounterCanChange(state);
        var merchantTakes = NoteMasks.Takes(LockpickFacts.Merchant.Wcid);
        return new VillaInput(
            VillaNowForTest ?? session.Clock.Time.GetUtcNow(),
            _options.VillaOpensAt,
            VillaFacts.Block1401,
            state.HouseStatus,
            owned?.IsRentPaid ?? false,
            owned?.RentDue ?? 0,
            owned?.PeriodSeconds ?? 0,
            VillaReads.Owed(owned),
            VillaReads.RentWrits(owned),
            state.Self.Qualities.Level,
            held,
            coins,
            VillaReads.Stones(state, _villaScratch),
            VillaMemory.For(state).HalfPending,
            _context.QuestTimersInUse,
            [.. WritFacts.Caves.Where(c => !_questFailed.ContainsKey(c.QuestKey))],
            VillaStoreList(state).Count,
            PackTally.FreeReview,
            StorageTripStation.HousePortalWalkEstablished,
            _villaFailed,
            _villaBoundary,
            _villaBoundaryFetches)
        {
            // M6 16 E5a2: the lockpick step is judged on what the Merchant's counter can pay with: every note held, unless his mask is known to take none
            LockpickNotes = merchantTakes == false ? 0 : notes,
            LockpickNotesSay = PurseView.CounterSays(notes, "the Merchant", NoteMasks.Of(LockpickFacts.Merchant.Wcid)),
        };
    }

    /// <summary>The storage trip's items: held, not worn, not a pack or a focus, not rotation gear; a great-cantrip piece or a kept-aside find (the owner, 2026-11-16: the review pack holds the great-cantrip pieces).</summary>
    private IReadOnlyList<uint> VillaStoreList(GameState state)
    {
        var held = new List<uint>();
        GearInventory.Collect(state, held);
        var rows = new List<(uint, bool, bool, bool)>();
        foreach (var guid in held)
        {
            if (!state.Objects.TryGet(guid, out var o) || (o.ItemType & StoreContainerType) != 0 || GearKit.FociWcids.Contains(o.Weenie.WeenieClassId) || PackMemory.Shared.Rotation.Contains(guid))
            {
                continue;
            }

            // TODO(villa-useful-cantrips): the owner, 2026-11-16, keeps only the useful cantrips; m6/14-useful-cantrips routes CantripRoom.IsGreatPiece to that rule, so this call takes it with no change here
            rows.Add((guid, state.Self.Inventory.TryGetEquipped(guid, out _), CantripRoom.IsGreatPiece(state, guid, _context.Spells), SaleMemory.Shared.KeptAside.Contains(guid)));
        }

        return VillaSchedule.StoreList(rows);
    }

    private const uint StoreContainerType = 0x200;

    /// <summary>
    /// A town trip's boundary (see the class): the schedule's step, or null when none runs (the caller goes on). Not with the track off, a stop due, or a station or errand already running; the first call of a boundary clears its
    /// set (<paramref name="boundary"/>).
    /// </summary>
    /// <param name="why">Who asks, for the log.</param>
    /// <param name="boundary">True at a new boundary (the logistics trip's end).</param>
    private ScenarioStatus? VillaErrand(string why, bool boundary)
    {
        if (!VillaOn || _session is not { } session || _villaActive || _questTripActive)
        {
            return null;
        }

        if (boundary)
        {
            _villaBoundary.Clear();
            _villaBoundaryFetches = 0;
        }

        if (StopDue() is not null)
        {
            _log.Info(LogCategory, $"villa: {why}: a stop rule is due, so no villa step starts; the stop's own way home goes first");
            return null;
        }

        var input = VillaRead(session);
        var call = VillaSchedule.Decide(input);
        if (call.Token == "status-unread")
        {
            if (!_villaUnreadLogged)
            {
                _villaUnreadLogged = true;
                _log.Warn(LogCategory, $"villa: {why}: {call.Why} (TODO(villa-status-unanswered) when no answer ever comes: the HouseQuery is asked once a session)");
            }

            return null;
        }

        _log.Info(LogCategory, $"villa: {why}: pace: {call.Pace.Token}: {call.Pace.Why}");
        foreach (var line in call.Lines)
        {
            _log.Info(LogCategory, line);
        }

        if (!call.Runs)
        {
            _log.Info(LogCategory, $"villa: {why}: no step: {call.Why}");
            return null;
        }

        _villaBoundary.Add(call.Step);
        if (call.Step == VillaStep.WritFetch)
        {
            var key = call.Cave!.QuestKey;
            _log.Info(LogCategory, $"villa: {why}: step {VillaSchedule.Name(call.Step)} ({call.Token}): the quest station on {key} from={JourneyPlans.FromHome}: {call.Why}");
            _villaFetchKey = key;
            _villaBoundaryFetches++;
            var fetch = StartErrand(QuestThen.Home, forced: false, key, JourneyPlans.FromHome, ladder: true, $"{why}: the villa's writ run: {key} ({call.Token})");
            if (fetch is null)
            {
                _villaFetchKey = null;
                _log.Info(LogCategory, $"villa: {why}: the fetch {key} did not start (its errand's own check before said why); the next boundary asks again");
            }

            return fetch;
        }

        var spec = VillaStations.Spec(call.Step, VillaMemory.For(session.State).HalfPending, call.Step == VillaStep.Storage ? VillaStoreList(session.State) : null, _options.VillaOpensAt);
        _log.Info(LogCategory, $"villa: {why}: step {VillaSchedule.Name(call.Step)} ({call.Token}): the {spec.Name} station: {call.Why}");
        _villaActive = true;
        _villaStep = call.Step;
        _tripPending = false;
        var started = BeginRun(AutopilotState.Trip, spec, $"{why}: the villa step {spec.Name} ({call.Token})");
        if (_spec is null || !ReferenceEquals(_spec, spec))
        {
            _villaActive = false; // a stop or the factory took the run instead
        }

        return started;
    }

    /// <summary>A villa station ended (the Trip state with <c>_villaActive</c>): check after by its line; well, the schedule is asked again; failed, the step is not called again this run and she goes home by the recovery's way.</summary>
    private ScenarioStatus VillaStepEnded(bool ok, string? why, string fields)
    {
        _villaActive = false;
        _tripPending = false;
        var name = VillaSchedule.Name(_villaStep);
        var reason = Token(fields, "reason") ?? "?";
        if (_villaStep == VillaStep.WritHandIn && Token(fields, "half_pending") is { } half && _session is { } s)
        {
            VillaMemory.For(s.State).HalfPending = half == "yes";
        }

        _log.Info(LogCategory, $"villa: ended step={name} ok={(ok ? "yes" : "no")} reason={reason}{(why is null ? string.Empty : " why=" + JourneyVerdict.Token(why))}: {fields}");
        if (_deathSeen)
        {
            _villaFailed.Add(_villaStep);
            _log.Warn(LogCategory, $"villa: step {name} ended in a death: not called again this run; the death station first");
            return UnscriptedDeath();
        }

        _recalledHome = false;
        if (!ok)
        {
            _villaFailed.Add(_villaStep);
            FoldInFlight();
            var stop = "failed:villa:" + name + ":" + JourneyVerdict.Token(reason != "?" && reason != "none" ? reason : why ?? "failed");
            _log.Warn(LogCategory, $"villa: step failed: {stop}: not called again this run; the climb goes on by the recovery's way home");
            return RecoverTo("recovery after the villa step " + name);
        }

        _villaStepsRun++;
        return VillaErrand("after the villa step " + name, boundary: false) ?? EnterKnown("after the villa step " + name);
    }

    /// <summary>A villa station was cut (a reconnect, a watchdog, a hand-back): it is not run again this boundary; the next boundary weighs it again.</summary>
    private ScenarioStatus VillaStepCut()
    {
        _villaActive = false;
        _log.Warn(LogCategory, $"villa: the step {VillaSchedule.Name(_villaStep)} was cut: not run again at this boundary (the next town trip weighs it again)");
        return EnterKnown("after the cut villa step " + VillaSchedule.Name(_villaStep));
    }

    /// <summary>The survival layer's leave or a death took the villa station: the step is not called again this run.</summary>
    private void VillaStepLeft(string what)
    {
        if (!_villaActive)
        {
            return;
        }

        _villaActive = false;
        _villaFailed.Add(_villaStep);
        _log.Warn(LogCategory, $"villa: step {VillaSchedule.Name(_villaStep)}: {what}: not called again this run");
    }

    /// <summary>
    /// The quest station of the villa's stone fetch ended well (<see cref="QuestEnded"/>): counted, the trip's errand call freed again (the fetch is the boundary's, not the next way home's: a held trophy is still turned in there, as after a kill
    /// task's talk), and the schedule asked again (its hand-in may follow at once); null when nothing more runs.
    /// </summary>
    private ScenarioStatus? VillaFetchEnded(string key, bool done)
    {
        _questTripTried = false;
        if (done)
        {
            _villaFetchesRun++;
        }

        _log.Info(LogCategory, $"villa: check after the fetch {key}: done={(done ? "yes" : "no")}; stones held {(_session is { } s ? VillaReads.Stones(s.State, _villaScratch) : -1)}");
        return VillaErrand("after the stone fetch " + key, boundary: false);
    }

    /// <summary>The cycle line's <c>villa=</c> (see <see cref="VillaSchedule.Marker"/>).</summary>
    private string VillaMarker()
    {
        if (!VillaOn || _session is not { } session || !session.State.Self.Inventory.IsKnown)
        {
            return VillaSchedule.Marker(VillaOn, null, null);
        }

        var input = VillaRead(session);
        double? perHour = !double.IsNaN(_villaMoneyAt) && _clock - _villaMoneyAt >= VillaTrendSeconds ? (input.Held.Money - _villaMoney0) * 3600.0 / (_clock - _villaMoneyAt) : null;
        return VillaSchedule.Marker(true, input, perHour);
    }

    /// <summary>M6 14 (the every-spell rule): the cycle line's <c>every_spell=</c>: <c>off</c>, or the switch and the last trip's numbers (<see cref="EverySpellMemory.Marker"/>), from the trips' session memory.</summary>
    private string EverySpellMarker() => EverySpellMemory.For(SaleMemory.Shared).Marker(_context.Facts.EverySpell);
}
