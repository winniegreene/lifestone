using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Casting;

/// <summary>
/// One cast a caller decided to make (<see cref="CastAttempt.Start"/>): the line's levels and the chosen one, the target, the <see cref="CastRules"/>, what it is for, and who is asking.
/// </summary>
public readonly record struct CastRequest
{
    /// <summary>The line's spell ids, lowest level first: the caller's own array, read and never copied or written. One id is a line of one level.</summary>
    public required IReadOnlyList<uint> Levels { get; init; }

    /// <summary>The index into <see cref="Levels"/> of the level to cast first.</summary>
    public int Index { get; init; }

    /// <summary>The target's guid: 0 casts untargeted (GameAction 0x0048); any other value, the player's own guid included, casts at it (0x004A).</summary>
    public uint Target { get; init; }

    /// <summary>The retry rules.</summary>
    public required CastRules Rules { get; init; }

    /// <summary>What the cast is for, in a few words, for the log ("buff Strength Self", "recall", "tie").</summary>
    public string Purpose { get; init; }

    /// <summary>Who is asking: <see cref="ActionSource.Automation"/> (M5 D13: the kill switch and dry run then reach every automated cast) unless the request says otherwise.</summary>
    public ActionSource Source { get; init; }

    /// <summary>
    /// The mana each level needs at most, in <see cref="Levels"/> order (the caller's array, not copied), or null: the attempt then computes <see cref="GearCasts.ManaCeiling"/> from the spell table with
    /// <see cref="WornItems"/>, and without a spell table no ceiling is known (no try is a mana try and <see cref="CastRules.CeilingBeforeRetry"/> sends the retry).
    /// </summary>
    public IReadOnlyList<uint>? ManaCeilings { get; init; }

    /// <summary>
    /// M5 7 F28: the caller's own test of whether a guid is dead or gone from the world, or null. A 0x042C (the target was not acquired) always ends <see cref="CastEnd.TargetDied"/> (M5 7 D124: ACE raises it only when the target object
    /// is no longer on the landblock, <c>Player_Magic.cs</c>:137-140, 173-179, 197-203, 751-757 @95ef426d); this test only words the end's line (dead or gone here, or still shown here while ACE no longer holds it).
    /// </summary>
    public Func<uint, bool>? TargetGone { get; init; }

    /// <summary>The items the caster has equipped, for <see cref="GearCasts.ManaCeiling"/>'s armour lines (0: none counted).</summary>
    public int WornItems { get; init; }

    /// <summary>The least mana at which a Mana Conversion try is sent (M5 D15's try floor, K5's to compute; 0: none). Under it a refusal or a retry under the ceiling ends <see cref="CastEnd.ManaShort"/>.</summary>
    public uint ManaTryFloor { get; init; }

    /// <summary>
    /// The caster's Mana Conversion (<c>ManaOrder.TryManaConversion</c>; 0: untrained), or null for no model gate (today's behaviour). When set, with the rules' Mana Conversion tries on, a try is sent with mana under the level's ceiling only when
    /// <see cref="ManaConversionModel.LandingChance"/> at the mana held reaches <see cref="ManaTryChance"/>; else the attempt ends <see cref="CastEnd.ManaShort"/> with nothing sent (M5 7, D15 gated on the model: no blind tries).
    /// </summary>
    public uint? ManaConversion { get; init; }

    /// <summary>The landing chance a gated try needs (<see cref="ManaConversion"/>; D15's <see cref="ManaConversionModel.DefaultTryChance"/>).</summary>
    public double ManaTryChance { get; init; } = ManaConversionModel.DefaultTryChance;

    /// <summary>
    /// Which lower levels a level down may use (the fight skips the levels the spellbook lacks), asked only on a level down; null: the level just below, which the spell table must hold when one is
    /// given (the buff cycle's rule: "the level below is not in the SpellTable" is a named miss). Cache the delegate: a lambda per request allocates.
    /// </summary>
    public Func<uint, bool>? Usable { get; init; }

    /// <summary>The proof asked when a try ends Sent (<see cref="CastChatProof"/>), or null: a Sent cast ends <see cref="CastEnd.Unproven"/>.</summary>
    public ICastProof? Proof { get; init; }

    /// <summary>Same-level fizzle retries already spent on this need (a resumed attempt, or a streak the caller keeps across attempts: the keeper's fizzles in a row, the mana loop's streak).</summary>
    public int FizzlesSpent { get; init; }

    /// <summary>Levels already stepped down on this need (the fight's once-per-scenario drop: 1 once it dropped).</summary>
    public int LevelsDownSpent { get; init; }

    /// <summary>A request with the defaults: <see cref="ActionSource.Automation"/>, the purpose "cast".</summary>
    public CastRequest()
    {
        Purpose = "cast";
        Source = ActionSource.Automation;
    }

    /// <summary>
    /// This request resumed after <paramref name="report"/> (an attempt that ended <see cref="CastEnd.ManaShort"/> before a retry, once the caller restored the mana): it starts at the report's
    /// <see cref="CastReport.NextLevelIndex"/> with the bounds it spent, so the line's rule counts across the two attempts.
    /// </summary>
    /// <param name="report">The ended attempt's report.</param>
    public CastRequest Resume(in CastReport report) => this with { Index = report.NextLevelIndex, FizzlesSpent = report.FizzleRetriesSpent, LevelsDownSpent = report.LevelsDownSpent };
}

/// <summary>
/// The outcome-aware cast policy (M5 item 1 lane K1, the P5b design's §2): one cast a caller decided to make, carried through the rules of its <see cref="CastRules"/> one try at a time.
/// <see cref="Start"/> sends the first try; <see cref="Step"/>, once a frame, reads the try's outcome and decides: the end, or the next try (the same level, the level below, after a spacing). One action
/// in flight at most; every try is logged and counted (<see cref="Report"/>). The Action API itself is unchanged: one action per call, no retries; the retries are this policy, above it ("no silent
/// retries", CLAUDE.md "Checks").
/// </summary>
/// <remarks>
/// <para><b>The rules, one row per outcome</b> (the design's §2.2): Confirmed: <see cref="CastEnd.Landed"/>. Sent: the request's proof decides <see cref="CastEnd.Landed"/> (counted <c>proved</c>),
/// a resist (retried while <see cref="CastRules.MaxResists"/> allows) or <see cref="CastEnd.Unproven"/>. 0x0402: the same level while <see cref="CastRules.MaxFizzles"/> allows, then the level below
/// while <see cref="CastRules.LevelsDown"/> allows, then <see cref="CastEnd.Fizzled"/>. 0x0401: again after <see cref="CastRules.ManaTrySpacingSeconds"/> while the refusals are under
/// <see cref="CastRules.MaxManaTries"/>, else <see cref="CastEnd.ManaShort"/>. 0x001D: again after <see cref="CastRules.BusyWaitSeconds"/> while <see cref="CastRules.MaxBusy"/> allows, else
/// <see cref="CastEnd.Busy"/>. 0x0400: <see cref="CastEnd.NoComponents"/>. 0x0550: <see cref="CastEnd.OutOfRange"/>; 0x042C: <see cref="CastEnd.TargetDied"/> (M5 7 F28, D124: never out of range). UseDone 0 without the effect: <see cref="CastEnd.NoEffect"/>. Refused,
/// Cancelled, DryRun, NotImplemented: their end, never retried. The 10 s timeout: <see cref="CastEnd.Timeout"/>, never resent (the cast may have happened). Any other code:
/// <see cref="CastEnd.ServerError"/>. <see cref="CastRules.MaxTries"/> bounds the tries in all and <see cref="CastRules.MaxSeconds"/> the attempt's time.</para>
/// <para><b>Safety.</b> Before every send, the first included, an <see cref="ActionSource.Automation"/> attempt reads <see cref="IActionApi.KillSwitch"/>: on, it ends <see cref="CastEnd.Cancelled"/>
/// with nothing sent (the kill switch applies to automation only, A0's R6). A try in flight cannot be unsent: it finishes, and the attempt then ends Cancelled instead of sending again.</para>
/// <para><b>What it never does.</b> It never changes the combat mode (the callers own it and their caster swaps) and never reads the mana before the first try: the caller decided to cast, and retail's
/// client never checks the mana either (<c>gmSpellcastingUI::Cast</c> 11.6096 0x004C6C40, <c>ClientMagicSystem::CastSpell</c> 0x00568DE0). The mana is read at each send, to count the mana tries, and
/// before a fizzle's retry when <see cref="CastRules.CeilingBeforeRetry"/> says so.</para>
/// <para><b>No allocation in <see cref="Step"/></b> while a try is in flight or waits: the strings are the log lines written on a try or an end, and the Action API's own handle per send.</para>
/// </remarks>
public sealed class CastAttempt
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "cast";

    /// <summary>ACE's <c>WeenieError.YourSpellFizzled</c>.</summary>
    public const uint FizzleCode = 0x0402;

    /// <summary>ACE's <c>WeenieError.YouDontHaveEnoughManaToCast</c> (a UseDone code).</summary>
    public const uint ManaCode = 0x0401;

    /// <summary>ACE's <c>WeenieError.YouDontHaveAllTheComponents</c> (a UseDone code).</summary>
    public const uint ComponentsCode = 0x0400;

    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> (a UseDone code).</summary>
    public const uint BusyCode = 0x001D;

    /// <summary>ACE's <c>WeenieError.MissileOutOfRange</c> (<c>WeenieError.cs</c>:1135 @95ef426d).</summary>
    public const uint OutOfRangeCode = 0x0550;

    /// <summary>ACE's <c>WeenieError.TargetNotAcquired</c> (<c>WeenieError.cs</c>:389 @95ef426d): a UseDone code, or a WeenieError before the cast's UseDone 0 (<c>Player_Magic.cs</c>:751-757, then <c>FinishCast</c> 935-985).</summary>
    public const uint NotAcquiredCode = 0x042C;

    /// <summary>The player's Max Mana vital id (<c>PropertyAttribute2nd</c> 5).</summary>
    private const uint MaxManaVital = 5;

    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly ILogSink _log;
    private readonly IReadOnlyDictionary<uint, SpellInfo>? _spells;

    private CastRequest _request;
    private CastRules _rules = CastRules.Once;
    private Phase _phase;
    private CastEnd _end;
    private ActionHandle? _pending;
    private ActionOutcome? _last;
    private double _start;
    private double _endedAt;
    private double _sendAt;
    private int _level;
    private int _nextLevel;
    private uint _code;
    private double _lastNow;
    private bool _manaTry;
    private long _chatMark;
    private int _tries;
    private int _fizzles;
    private int _fizzleRetries;
    private int _levelsDown;
    private int _manaTries;
    private int _manaRefusals;
    private int _manaLanded;
    private int _busy;
    private int _resists;
    private int _proved;
    private string? _why;

    private enum Phase : byte
    {
        Idle,
        Waiting,
        InFlight,
        Done,
    }

    /// <summary>A reusable attempt over an Action API and a read model.</summary>
    /// <param name="actions">The Action API the tries go through.</param>
    /// <param name="state">The read model (the mana held, the chat mark).</param>
    /// <param name="log">The log (one line a try and one an end).</param>
    /// <param name="spells">The SpellTable (the names in the log, the mana ceilings, the level-down check), or null.</param>
    public CastAttempt(IActionApi actions, GameState state, ILogSink log, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(log);
        _actions = actions;
        _state = state;
        _log = log;
        _spells = spells;
    }

    /// <summary>True from <see cref="Start"/> until the attempt ends.</summary>
    public bool IsRunning => _phase is Phase.Waiting or Phase.InFlight;

    /// <summary>How it stands: <see cref="CastEnd.Running"/>, or its end.</summary>
    public CastEnd End => _end;

    /// <summary>The try in flight, or the last one sent (its <see cref="ActionHandle.CompletedAt"/> and outcome's detail), or null before the first send.</summary>
    public ActionHandle? Handle => _pending;

    /// <summary>The last try's outcome, or null.</summary>
    public ActionOutcome? LastOutcome => _last;

    /// <summary>The chat log's <see cref="ChatLog.Total"/> when the last try was sent (for <see cref="GearCasts.Explain"/> and the callers' own chat reads).</summary>
    public long ChatMark => _chatMark;

    /// <summary>The request of the current or last attempt.</summary>
    public CastRequest Request => _request;

    /// <summary>The counts so far and, once ended, the end and why.</summary>
    public CastReport Report => new()
    {
        End = _end,
        Code = _code,
        Outcome = _last?.Kind,
        LevelIndex = _level,
        SpellId = _request.Levels is { } levels && _level >= 0 && _level < levels.Count ? levels[_level] : 0,
        NextLevelIndex = _nextLevel,
        Tries = _tries,
        Fizzles = _fizzles,
        FizzleRetriesSpent = _fizzleRetries,
        LevelsDownSpent = _levelsDown,
        ManaTries = _manaTries,
        ManaRefusals = _manaRefusals,
        ManaLanded = _manaLanded,
        Busy = _busy,
        Resists = _resists,
        Proved = _proved,
        Seconds = (_phase == Phase.Done ? _endedAt : _lastNow) - _start,
        Why = _why,
    };

    /// <summary>
    /// Starts an attempt (any earlier one is forgotten; a try of it still in flight is not unsent) and sends its first try now, with no mana check. Returns <see cref="Step"/>'s answer: Running, or an
    /// end at once (the kill switch, a refusal, dry run).
    /// </summary>
    /// <param name="request">The cast.</param>
    /// <param name="now">The session clock.</param>
    /// <exception cref="ArgumentException">No levels, an index outside them, or a spell id 0 at the index.</exception>
    public CastEnd Start(in CastRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(request.Levels);
        ArgumentNullException.ThrowIfNull(request.Rules);
        if (request.Index < 0 || request.Index >= request.Levels.Count)
        {
            throw new ArgumentException($"the index {request.Index} is outside the line's {request.Levels.Count} levels", nameof(request));
        }

        if (request.Levels[request.Index] == 0)
        {
            throw new ArgumentException("the chosen level's spell id is 0", nameof(request));
        }

        _request = request;
        _rules = request.Rules;
        _phase = Phase.Waiting;
        _end = CastEnd.Running;
        _pending = null;
        _last = null;
        _start = now;
        _endedAt = now;
        _sendAt = now;
        _level = request.Index;
        _nextLevel = request.Index;
        _code = 0;
        _lastNow = now;
        _manaTry = false;
        _chatMark = _state.Chat.Total;
        _tries = _fizzles = _manaTries = _manaRefusals = _manaLanded = _busy = _resists = _proved = 0;
        _fizzleRetries = Math.Max(0, request.FizzlesSpent);
        _levelsDown = Math.Max(0, request.LevelsDownSpent);
        _why = null;
        return Step(now);
    }

    /// <summary>
    /// Once a frame: reads the try in flight, or sends the next try once its spacing has passed. Allocates nothing while a try is in flight or waits.
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <returns><see cref="CastEnd.Running"/>, or the attempt's end (the same end on every later call).</returns>
    public CastEnd Step(double now)
    {
        if (_phase is Phase.Waiting or Phase.InFlight)
        {
            _lastNow = now;
        }

        switch (_phase)
        {
            case Phase.Idle:
            case Phase.Done:
                return _end;
            case Phase.InFlight:
                if (_pending!.Outcome is not { } outcome)
                {
                    return CastEnd.Running;
                }

                Settle(outcome, now);
                return _phase == Phase.Waiting ? Step(now) : _end;
            default:
                if (_request.Source == ActionSource.Automation && _actions.KillSwitch)
                {
                    return Finish(CastEnd.Cancelled, now, _tries == 0 ? "the kill switch is on: nothing was sent" : "the kill switch is on: the next try was not sent");
                }

                if (now < _sendAt)
                {
                    return now - _start >= _rules.MaxSeconds ? CapEnd(now) : CastEnd.Running;
                }

                if (_tries > 0 && now - _start >= _rules.MaxSeconds)
                {
                    return CapEnd(now);
                }

                if (ModelCannotRaise(now))
                {
                    return _end;
                }

                Send(now);
                if (_pending!.Outcome is { } early)
                {
                    Settle(early, now); // refused, cancelled or a dry run at once: the end (never a retry)
                    return _phase == Phase.Waiting ? Step(now) : _end;
                }

                return CastEnd.Running;
        }
    }

    /// <summary>Ends a running attempt <see cref="CastEnd.Cancelled"/> (the caller's own stop: a station aborted, the scenario ended). A try in flight is not unsent.</summary>
    /// <param name="why">Why, for the log and the report.</param>
    /// <param name="now">The session clock.</param>
    public void Stop(string why, double now)
    {
        if (IsRunning)
        {
            Finish(CastEnd.Cancelled, now, "stopped by the caller: " + why);
        }
    }

    private CastEnd CapEnd(double now) => Finish(
        CastEnd.TimeCap,
        now,
        string.Create(CultureInfo.InvariantCulture, $"the attempt's cap of {_rules.MaxSeconds:0.##} s ({_rules.Name}) passed after {_tries} tries"));

    // M5 7: the Mana Conversion tries gated on the model. A try below the level's ceiling is sent only when the model gives it the request's landing chance at the mana held; else the attempt ends ManaShort with nothing sent (the caller dances or waits).
    private bool ModelCannotRaise(double now)
    {
        if (_request.ManaConversion is not { } conversion || _rules.MaxManaTries == 0 || _spells is not { } spells || !spells.TryGetValue(_request.Levels[_level], out var info))
        {
            return false;
        }

        var ceiling = Ceiling(_level);
        if (ceiling == 0 || !_state.Self.Qualities.TryGetVitalLevels(MaxManaVital, out var mana, out _) || mana >= ceiling)
        {
            return false;
        }

        var chance = ManaConversionModel.LandingChance(ceiling, info.Power, conversion, mana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0);
        if (chance >= _request.ManaTryChance)
        {
            return false;
        }

        Finish(
            CastEnd.ManaShort,
            now,
            string.Create(CultureInfo.InvariantCulture, $"the Mana Conversion model gives a try {chance:P1} at {mana} mana of up to {ceiling} (needs {_request.ManaTryChance:P0}); nothing sent (level {_level + 1})"));
        return true;
    }

    private void Send(double now)
    {
        var spell = _request.Levels[_level];
        _chatMark = _state.Chat.Total;
        var ceiling = Ceiling(_level);
        var known = _state.Self.Qualities.TryGetVitalLevels(MaxManaVital, out var mana, out _);
        _manaTry = known && ceiling > 0 && mana < ceiling;
        if (_manaTry)
        {
            _manaTries++;
        }

        _tries++;
        _pending = _request.Target == 0 ? _actions.Cast(spell, _request.Source) : _actions.Cast(spell, _request.Target, _request.Source);
        _phase = Phase.InFlight;
        _sendAt = now;
        if (_log.IsEnabled(LogSeverity.Info))
        {
            _log.Info(
                LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_request.Purpose}: try {_tries} {NameOf(spell)} ({spell}){(_request.Target == 0 ? string.Empty : $" at 0x{_request.Target:X8}")}, level {_level + 1} of {_request.Levels.Count}, mana {(known ? mana.ToString(CultureInfo.InvariantCulture) : "unknown")}{(ceiling > 0 ? $" of up to {ceiling}" : string.Empty)}{(_manaTry ? " (a mana try)" : string.Empty)} ({_rules.Name}, {_request.Source})"));
        }
    }

    private void Settle(ActionOutcome outcome, double now)
    {
        _last = outcome;
        _code = outcome.Code;
        switch (outcome.Kind)
        {
            case ActionOutcomeKind.Confirmed:
            case ActionOutcomeKind.Local:
                Landed(now, "confirmed: " + outcome.Detail);
                return;
            case ActionOutcomeKind.Sent:
                SettleSent(now);
                return;
            case ActionOutcomeKind.TimedOut:
                Finish(CastEnd.Timeout, now, "no answer within the cast's timeout; never resent, the cast may have happened: " + outcome.Detail);
                return;
            case ActionOutcomeKind.Refused when outcome.Reason == ActionReason.NotEnoughMana:
                // M5 7 D141: the tracker's before-check knew ACE's 0x0401 before the send (a certain refusal: no Mana Conversion roll could cover it), so it ends as that answer would, with no try spent on it.
                _code = ManaCode;
                _manaRefusals++;
                Finish(CastEnd.ManaShort, now, "not enough mana, known before the send (0x0401 certain, nothing sent): " + outcome.Detail);
                return;
            case ActionOutcomeKind.Refused when outcome.Reason == ActionReason.TargetGone && _request.Target != 0:
                // M5 7 D141: the target left the object table while the cast waited its turn: ACE would answer 0x042C, so it ends as that answer does (the caller picks another).
                _code = NotAcquiredCode;
                Finish(CastEnd.TargetDied, now, string.Create(CultureInfo.InvariantCulture, $"the target 0x{_request.Target:X8} was gone before the send (nothing sent): {outcome.Detail}"));
                return;
            case ActionOutcomeKind.Refused:
                Finish(CastEnd.Refused, now, "refused: " + outcome.Detail);
                return;
            case ActionOutcomeKind.Cancelled:
                Finish(CastEnd.Cancelled, now, "cancelled: " + outcome.Detail);
                return;
            case ActionOutcomeKind.DryRun:
                Finish(CastEnd.DryRun, now, "dry run: " + outcome.Detail);
                return;
            case ActionOutcomeKind.NotImplemented:
                Finish(CastEnd.NotImplemented, now, "not implemented: " + outcome.Detail);
                return;
        }

        switch (outcome.Code)
        {
            case FizzleCode:
                SettleFizzle(now);
                return;
            case ManaCode:
                SettleMana(now);
                return;
            case BusyCode:
                _busy++;
                if (_busy > _rules.MaxBusy || TriesSpent())
                {
                    Finish(CastEnd.Busy, now, string.Create(CultureInfo.InvariantCulture, $"busy (0x001D) {_busy} times, over the {_rules.MaxBusy} the rules wait out ({_rules.Name})"));
                    return;
                }

                Again(_level, now + _rules.BusyWaitSeconds);
                return;
            case ComponentsCode:
                Finish(CastEnd.NoComponents, now, "a component is missing (UseDone 0x0400): a kit problem, never retried");
                return;
            case NotAcquiredCode when _request.Target != 0 && _request.TargetGone is { } gone && !gone(_request.Target):
                // M5 7 D124: ACE sends 0x042C only when the target object is not on its landblock (Player_Magic.cs:137-140, 173-179, 197-203, 751-757 @95ef426d); the client's table lags its deletion. Never out of range.
                Finish(CastEnd.TargetDied, now, string.Create(CultureInfo.InvariantCulture, $"the target 0x{_request.Target:X8} was not acquired at the release (0x{outcome.Code:X4} TargetNotAcquired: ACE no longer holds it, though it still shows here): the caller picks another, no walk"));
                return;
            case NotAcquiredCode:
                Finish(CastEnd.TargetDied, now, string.Create(CultureInfo.InvariantCulture, $"the target 0x{_request.Target:X8} died or left during the wind-up (0x{outcome.Code:X4} TargetNotAcquired): the caller picks another, no walk"));
                return;
            case OutOfRangeCode:
                Finish(CastEnd.OutOfRange, now, string.Create(CultureInfo.InvariantCulture, $"out of range (0x{outcome.Code:X4}): the caller walks or closes in"));
                return;
            case 0:
                Finish(CastEnd.NoEffect, now, "UseDone 0 without the cast's effect (the stance, an invalid target, a stronger spell held): " + outcome.Detail);
                return;
            default:
                Finish(CastEnd.ServerError, now, string.Create(CultureInfo.InvariantCulture, $"the server answered 0x{outcome.Code:X4}: {outcome.Detail}"));
                return;
        }
    }

    private void SettleSent(double now)
    {
        var proof = _request.Proof?.Check(new CastProofQuery(_request.Levels[_level], _request.Target, _chatMark)) ?? CastProofResult.None;
        switch (proof)
        {
            case CastProofResult.Proved:
                _proved++;
                Landed(now, "sent and proved by the proof");
                return;
            case CastProofResult.Resisted:
                _resists++;
                if (_resists > _rules.MaxResists || TriesSpent())
                {
                    Finish(CastEnd.Resisted, now, string.Create(CultureInfo.InvariantCulture, $"the target resisted it {_resists} times, over the {_rules.MaxResists} the rules retry ({_rules.Name})"));
                    return;
                }

                Again(_level, now);
                return;
            default:
                // M5 1 fix A, 1-R16: a Sent cast nothing proves is not a landing: mana_landed counts only a landing proved (the chat proof) or confirmed (the enchantment, the projectile); the Unproven end and its tally say the rest.
                Finish(CastEnd.Unproven, now, _request.Proof is null ? "sent (UseDone 0) with no proof asked: TODO(m3-cast-other-target-proof)" : "sent (UseDone 0) and the proof found nothing: TODO(m3-cast-other-target-proof)");
                return;
        }
    }

    private void SettleFizzle(double now)
    {
        _fizzles++;
        if (TriesSpent())
        {
            Finish(CastEnd.Fizzled, now, string.Create(CultureInfo.InvariantCulture, $"fizzled; the {_rules.MaxTries} tries are spent ({_rules.Name})"));
            return;
        }

        if (_fizzleRetries < _rules.MaxFizzles)
        {
            _fizzleRetries++;
            Retry(_level, now + _rules.FizzleSpacingSeconds, now);
            return;
        }

        if (_levelsDown < _rules.LevelsDown)
        {
            var lower = Lower(_level, out var why);
            if (lower < 0)
            {
                Finish(CastEnd.Fizzled, now, "fizzled; " + why + " (" + _rules.Name + ")");
                return;
            }

            _levelsDown++;
            Retry(lower, now + _rules.FizzleSpacingSeconds, now);
            return;
        }

        Finish(
            CastEnd.Fizzled,
            now,
            string.Create(CultureInfo.InvariantCulture, $"fizzled {_fizzles} times; the {_rules.MaxFizzles} same-level retries and {_rules.LevelsDown} level down of {_rules.Name} are spent"));
    }

    private void SettleMana(double now)
    {
        _manaRefusals++;
        if (_manaRefusals >= _rules.MaxManaTries || TriesSpent())
        {
            Finish(
                CastEnd.ManaShort,
                now,
                string.Create(CultureInfo.InvariantCulture, $"not enough mana (0x0401) {_manaRefusals} times{(_rules.MaxManaTries == 0 ? " with no Mana Conversion tries" : $", the {_rules.MaxManaTries} Mana Conversion tries are spent")} ({_rules.Name})"));
            return;
        }

        if (UnderFloor(out var mana))
        {
            Finish(CastEnd.ManaShort, now, string.Create(CultureInfo.InvariantCulture, $"not enough mana (0x0401) and {mana} is under the try floor {_request.ManaTryFloor}"));
            return;
        }

        Again(_level, now + _rules.ManaTrySpacingSeconds);
    }

    // A fizzle's retry: the mana is checked against the retry level's ceiling first when the rules say so.
    private void Retry(int level, double at, double now)
    {
        _nextLevel = level;
        if (_rules.CeilingBeforeRetry)
        {
            var ceiling = Ceiling(level);
            if (ceiling > 0 && _state.Self.Qualities.TryGetVitalLevels(MaxManaVital, out var mana, out _) && mana < ceiling)
            {
                if (_rules.MaxManaTries <= _manaRefusals || mana < _request.ManaTryFloor)
                {
                    Finish(
                        CastEnd.ManaShort,
                        now,
                        string.Create(CultureInfo.InvariantCulture, $"the retry at level {level + 1} needs up to {ceiling} mana and {mana} is held; nothing sent (resume at level {level + 1})"));
                    return;
                }
            }
        }

        Again(level, at);
    }

    private void Again(int level, double at)
    {
        _level = level;
        _nextLevel = level;
        _sendAt = at;
        _phase = Phase.Waiting;
    }

    private bool TriesSpent() => _rules.MaxTries > 0 && _tries >= _rules.MaxTries;

    private bool UnderFloor(out uint mana)
    {
        mana = 0;
        return _request.ManaTryFloor > 0 && _state.Self.Qualities.TryGetVitalLevels(MaxManaVital, out mana, out _) && mana < _request.ManaTryFloor;
    }

    // The level below for a level down, or -1 (with why).
    private int Lower(int from, out string why)
    {
        var levels = _request.Levels;
        if (_request.Usable is { } usable)
        {
            for (var i = from - 1; i >= 0; i--)
            {
                if (levels[i] != 0 && usable(levels[i]))
                {
                    why = string.Empty;
                    return i;
                }
            }

            why = "no usable level below";
            return -1;
        }

        if (from == 0 || levels[from - 1] == 0)
        {
            why = "there is no level below";
            return -1;
        }

        if (_spells is { } spells && !spells.ContainsKey(levels[from - 1]))
        {
            why = "the level below is not in the SpellTable";
            return -1;
        }

        why = string.Empty;
        return from - 1;
    }

    private uint Ceiling(int level)
    {
        if (_request.ManaCeilings is { } ceilings)
        {
            return level < ceilings.Count ? ceilings[level] : 0;
        }

        return _spells is { } spells && spells.TryGetValue(_request.Levels[level], out var info) ? GearCasts.ManaCeiling(info, _request.WornItems) : 0;
    }

    private string NameOf(uint spell) => _spells is { } spells && spells.TryGetValue(spell, out var info) ? info.Name : "spell";

    private void Landed(double now, string why)
    {
        if (_manaTry)
        {
            _manaLanded++;
        }

        Finish(CastEnd.Landed, now, why);
    }

    private CastEnd Finish(CastEnd end, double now, string why)
    {
        _end = end;
        _phase = Phase.Done;
        _endedAt = now;
        _why = why;
        var spell = _request.Levels[_level];
        var severity = end.Succeeded() ? LogSeverity.Info : LogSeverity.Warning;
        if (_log.IsEnabled(severity))
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{_request.Purpose}: {end.Name()} after {_tries} tries ({NameOf(spell)}, level {_level + 1}, {now - _start:0.0#} s; fizzles {_fizzles}, mana tries {_manaTries}, mana refusals {_manaRefusals}, busy {_busy}, resists {_resists}, proved {_proved}): {why}");
            if (severity == LogSeverity.Info)
            {
                _log.Info(LogCategory, line);
            }
            else
            {
                _log.Warn(LogCategory, line);
            }
        }

        return end;
    }
}
