using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F23 (soak round 2's death: the autopilot stopped the den fight for a buff margin with a Tusker Guard on the mage, and the upkeep then stood still for 50 s). The buff refresh runs INSIDE the fight: the autopilot asks for it
/// (<see cref="IBuffRefreshFight.RequestBuffRefresh"/>) instead of stopping the fight, and the fight runs it between kills, at a <c>Choose</c> point after the emergency has had its say, when no hostile is in reach (no living
/// target creature within the reach and no hit taken within <see cref="RefreshQuietSeconds"/>). The step is the buff cycle's own re-buff pass (<see cref="BuffCycle.StartRebuffLine"/>, unforced) with the cycle's own mana order
/// (its stamina-to-mana dance first, D27, as D88 orders the fight's).
/// <para>M5 7 F24 (soak round 3's death: a Tusker Guard was on the mage for the whole run, the lull never came, and Impenetrability, Bludgeon Bane and the masteries ran out at 21:21; the biggest hit rose from 11 to 26; F23's "a line may run out
/// first: the lesser harm" was wrong). Two thresholds: a line under the SOFT margin (<see cref="BuffOptions.RebuffMarginSeconds"/>, F23's rule) is recast in a lull; a line under the HARD margin (<see cref="BuffOptions.HardMarginSeconds"/>, default 120 s)
/// is recast between the fight's casts whatever is in reach: the next decision casts it instead of an attack. The emergency heal (D91) and the health floors (D88) still come first, and a cast in flight is never interrupted. The lines go
/// one at a time (the buff cycle's one-line pass), in the order protections, regenerations, masteries and attributes, the wand's lines (<see cref="ArenaProfile.RefreshGroupOf"/>), the soonest to expire first within a group. A line that runs
/// out anyway is logged once at error level and counted (<see cref="BuffsExpired"/>, <c>buffs_expired=</c>, report-only).</para>
/// <para>M5 7 F25 (D93, the owner: rebuff on purpose after a fight, looking ahead). At the END of each fight (the lull: no hostile in reach after a kill, F23's rule) the fight recasts every line that would run out before the next lull is likely:
/// with less left than the fight's own mean length (<see cref="FightMeanSeconds"/>, an exponentially weighted mean of the last fights' seconds from the first hit taken to the lull, seeded by <see cref="BuffOptions.FightSeedSeconds"/>) plus
/// <see cref="BuffOptions.LookaheadMarginSeconds"/>; protections first. Under fire only the emergency case is left: a line under the hard floor (<see cref="BuffOptions.HardMarginSeconds"/>, now 30 s) or already lapsed. Once armed by the
/// autopilot's first ask the fight keeps the lines itself (<see cref="BuffsKeptAhead"/>): the lookahead runs at every lull, asked or not.</para>
/// <para>D126 (a) (soak round 12: 15 lines cast together by the start's pass came due together and lapsed together at 09:43 UTC, during a 2-minute walk that reached no decision point, and again in the 10:28 trip). Lines are renewed as a
/// BATCH: every pick reads the lines sorted by their time left and takes the k-th soonest as due when it has less left than the threshold plus k times <see cref="BatchLineSeconds"/> (the passes before it), so a batch starts early enough
/// for its last line; at a lull and before a trip the batch also takes every line due within <see cref="BatchGapSeconds"/> of the last one taken (the whole batch, not its first lines); a batch once started is finished (its end is
/// kept). Under the hard floor a walk is dropped for the pass (<see cref="RefreshDropsWalk"/>), and a stop for a trip waits until every line due within the trip's expected length is recast (<see cref="RequestLeaveRefresh"/>).</para>
/// A pass already started is not cut (a buff cycle cannot be aborted, <see cref="RebuffDriver"/>; <c>TODO(f23-refresh-abort)</c>), so the external stop waits for its end.
/// </summary>
public sealed partial class FightScenario : IBuffRefreshFight
{
    /// <summary>The seconds after a hit taken during which the soft refresh does not start (as the re-buff driver's <see cref="RebuffDriver.FightQuietSeconds"/>).</summary>
    public const double RefreshQuietSeconds = RebuffDriver.FightQuietSeconds;

    /// <summary>The seconds before a line whose recast was tried is tried again (the pass cast it, or failed).</summary>
    public const double RefreshRetrySeconds = 30;

    /// <summary>The seconds before a line that a pass could not cast at all (no level payable, no spell) is tried again.</summary>
    public const double RefreshFutileSeconds = 300;

    /// <summary>M5 7 F25: the weight of the newest fight in the fight's mean length (an exponentially weighted mean; Lifestone's own: about the last five fights count).</summary>
    public const double FightMeanWeight = 0.3;

    /// <summary>M5 7 F25: the refresh cycle's own margin: a one-line pass casts the line the fight names, whatever its time left (the fight decides what is due: the hard floor or the lookahead's horizon).</summary>
    private const double RefreshCycleMarginSeconds = 86_400;

    /// <summary>D126 (a): the seconds one line's pass takes, for the batch's schedule: soak round 12's hard floor recast 13 lines one after another in 19 s (07:30:39 to 07:30:58 UTC), 1.46 s a line.</summary>
    public const double BatchLineSeconds = 1.5;

    /// <summary>D126 (a), Lifestone's own: at a lull and before a trip, a line due within this many seconds of the last line taken joins the batch (round 12's batches spread over 25 s and 50 s).</summary>
    public const double BatchGapSeconds = 60;

    /// <summary>D126 (a): the longest a stop for a trip waits for the lines due within the trip's length to be recast (Lifestone's own; 24 lines at <see cref="BatchLineSeconds"/> take 36 s).</summary>
    public const double LeaveRefreshBoundSeconds = 120;

    /// <summary>D126 (a): how often a walk checks for a line under the hard floor (each check reads the lines).</summary>
    public const double WalkRefreshCheckSeconds = 1;

    private double _fightMean = double.NaN;
    private double _fightStartAt = double.NaN;

    private double _refreshMargin = double.NaN;
    private bool _refreshArmed;
    private double _refreshReach;
    private string _refreshDetail = string.Empty;
    private BuffCycle? _refreshCycle;
    private double _refreshLastHitAt = double.NegativeInfinity;
    private int _refreshHitsSeen;
    private bool _refreshWaitLogged;
    private readonly List<BuffCycle.LineState> _refreshStates = [];
    private readonly Dictionary<string, double> _refreshBlockedUntil = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double At, string Outcome)> _refreshLastTry = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refreshSeen = new(StringComparer.Ordinal);
    private string _refreshLine = string.Empty;
    private string _refreshBlock = string.Empty;
    private readonly List<double> _batchRemaining = [];
    private double _batchThreshold;
    private double _hardBatchEnd = double.NaN;
    private double _lullBatchEnd = double.NaN;
    private double _leaveBatchEnd = double.NaN;
    private double _leaveHorizon = double.NaN;
    private double _leaveAskedAt = double.NaN;
    private string _leaveDetail = string.Empty;
    private double _walkRefreshCheckAt = double.NegativeInfinity;

    /// <inheritdoc />
    public bool BuffRefreshAsked => !double.IsNaN(_refreshMargin);

    /// <inheritdoc />
    public int BuffRefreshes { get; private set; }

    /// <inheritdoc />
    public int BuffLinePasses { get; private set; }

    /// <inheritdoc />
    public int BuffLineCount => _refreshStates.Count;

    /// <summary>M5 7 F24: the lines that ran out while the refresh watched them (each counted once per expiry; the verdict's <c>buffs_expired=</c>, report-only).</summary>
    public int BuffsExpired { get; private set; }

    /// <summary>M5 7 F25: the fight's mean length in seconds, from the first hit taken to the lull (the seed until a fight is measured).</summary>
    public double FightMeanSeconds => double.IsNaN(_fightMean) ? _buffOptions.FightSeedSeconds : _fightMean;

    /// <summary>M5 7 F25: the fights measured (each from its first hit taken to its lull).</summary>
    public int FightsMeasured { get; private set; }

    /// <inheritdoc />
    public bool BuffsKeptAhead => _refreshArmed;

    /// <inheritdoc />
    public bool LeaveRefreshAsked => !double.IsNaN(_leaveHorizon);

    /// <summary>D126 (a): the refreshes before a trip that ended with every line inside the trip's horizon recast.</summary>
    public int LeaveRefreshes { get; private set; }

    /// <summary>D126 (a): the walks dropped for a line under the hard floor (or inside a pending trip's horizon).</summary>
    public int WalksDroppedForBuffs { get; private set; }

    /// <inheritdoc />
    public void RequestLeaveRefresh(double horizonSeconds, double reachMetres, string detail)
    {
        if (LeaveRefreshAsked)
        {
            return;
        }

        (_leaveHorizon, _leaveAskedAt, _leaveDetail, _leaveBatchEnd) = (Math.Max(0, horizonSeconds), double.NaN, detail ?? string.Empty, double.NaN);
        _refreshArmed = true; // a leave ask arms the fight's own upkeep too
        if (_refreshReach <= 0)
        {
            _refreshReach = Math.Max(0, reachMetres);
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave asked ({_leaveDetail}): before the fight ends, every line with less than {_leaveHorizon:0} s left (and the rest of its batch) is recast, between the casts whatever is in reach, for at most {LeaveRefreshBoundSeconds:0} s (D126)"));
    }

    /// <inheritdoc />
    public void RequestBuffRefresh(double marginSeconds, double reachMetres, string detail)
    {
        if (BuffRefreshAsked || _phase == Phase.Refresh)
        {
            return;
        }

        (_refreshMargin, _refreshReach, _refreshDetail, _refreshWaitLogged) = (Math.Max(1, marginSeconds), Math.Max(0, reachMetres), detail ?? string.Empty, false);
        if (!_refreshArmed)
        {
            _refreshArmed = true; // the first ask arms the hard refresh too: the fight is under an autopilot that keeps the buffs up
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: asked ({_refreshDetail}): the fight recasts at each lull (no hostile within {_refreshReach:0.#} m after a kill) looking ahead: every line with less left than the fight's mean {FightMeanSeconds:0} s plus {_buffOptions.LookaheadMarginSeconds:0} s; under fire only those under the {_buffOptions.HardMarginSeconds:0.#} s hard floor or lapsed (F25)"));
    }

    /// <summary>Notes why a decision did not refresh (the emergency came first), for the expiry line.</summary>
    private void NoteRefreshBlocked(string why)
    {
        if (_refreshArmed)
        {
            _refreshBlock = why;
        }
    }

    /// <summary>
    /// At a Choose point after the emergency: reads the lines, counts the ones that ran out, and starts one line's pass: the most urgent line under the HARD margin first (whatever is in reach, unless the health is under the drain or heal line),
    /// else, when the refresh is asked and no hostile is in reach, the most urgent line under the SOFT margin. True when a pass started (the phase is the refresh's).
    /// </summary>
    private bool RefreshStep(IGameSessionView session, double clock)
    {
        if (!_refreshArmed)
        {
            return false;
        }

        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State));
        var seeding = _refreshCycle.SeedPending;
        _refreshCycle.ReadLines(clock, _refreshStates);
        CountExpired(clock, seeding);

        var hostile = RefreshHostile(session, clock, out var what); // M5 7 F25: also notes a fight's first hit, for its length
        var hard = _buffOptions.HardMarginSeconds;
        if (hard > 0 && PickBatchLine(clock, hard, seeding, chain: false, ref _hardBatchEnd, out var urgent))
        {
            if (HealthUnderFloor(session, clock))
            {
                _refreshBlock = "the health was under the heal line and the heal came first";
            }
            else
            {
                var left = urgent.Active ? string.Create(CultureInfo.InvariantCulture, $"has {urgent.Remaining:0.#} s left, under the {hard:0.#} s hard floor{BatchNote(hard)}") : "has lapsed";
                var note = string.Create(CultureInfo.InvariantCulture, $"buff refresh: hard: {urgent.Line.Name} {left}: recast between the fight's casts{(what.Length > 0 ? " with " + what : " with no hostile in reach")} (F24, F25)");
                return StartLinePass(clock, urgent.Line.Name, note, hard: true);
            }
        }

        if (LeaveRefreshAsked && PickBatchLine(clock, _leaveHorizon, seeding, chain: true, ref _leaveBatchEnd, out var leaving))
        {
            if (HealthUnderFloor(session, clock))
            {
                _refreshBlock = "the health was under the heal line and the heal came first";
            }
            else
            {
                var left = leaving.Active ? string.Create(CultureInfo.InvariantCulture, $"has {leaving.Remaining:0} s left") : "has lapsed";
                return StartLinePass(clock, leaving.Line.Name, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: {leaving.Line.Name} {left}, inside the {_leaveHorizon:0} s before the fight ends ({_leaveDetail}){BatchNote(_leaveHorizon)}; recast first (D126)"), hard: true);
            }
        }

        if (hostile)
        {
            if (BuffRefreshAsked && !_refreshWaitLogged)
            {
                _refreshWaitLogged = true;
                _log.Info(LogCategory, $"buff refresh: waits: {what}; the fight goes on (the lookahead runs at the lull; a line under the {hard:0.#} s hard floor is recast between the casts anyway, F25)");
            }

            return false;
        }

        // M5 7 F25: the lull. The fight that ends here is measured, then every line that would run out before the next lull is recast, one line a pass, protections first.
        NoteLull(clock);
        var mean = FightMeanSeconds;
        var horizon = mean + _buffOptions.LookaheadMarginSeconds;
        if (PickBatchLine(clock, horizon, false, chain: true, ref _lullBatchEnd, out var due))
        {
            return StartLinePass(clock, due.Line.Name, string.Create(CultureInfo.InvariantCulture, $"buff refresh: lookahead: {due.Line.Name} {due.Remaining:0} s left < next lull {horizon:0} s (fight mean {mean:0} + margin {_buffOptions.LookaheadMarginSeconds:0}){BatchNote(horizon)}; no hostile within {_refreshReach:0.#} m"), hard: false);
        }

        if (BuffRefreshAsked)
        {
            BuffRefreshes++; // every line inside the lookahead's horizon is recast (or tried): the ask is answered
            (_refreshMargin, _refreshWaitLogged) = (double.NaN, false);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: done: no line runs out before the next lull ({horizon:0} s) that has not been tried; the fight goes on"));
        }

        return false;
    }

    /// <summary>M5 7 F25: a lull ends the fight that began at its first hit: its length joins the mean (an exponentially weighted mean, <see cref="FightMeanWeight"/>).</summary>
    private void NoteLull(double clock)
    {
        if (double.IsNaN(_fightStartAt))
        {
            return;
        }

        var length = Math.Max(0, clock - _fightStartAt);
        _fightStartAt = double.NaN;
        var before = FightMeanSeconds;
        _fightMean = (before * (1 - FightMeanWeight)) + (length * FightMeanWeight);
        FightsMeasured++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: lull: the fight lasted {length:0} s from its first hit; the fight mean is now {_fightMean:0} s over {FightsMeasured} fight(s)"));
    }

    /// <summary>
    /// D126 (a): the most urgent line of the batch due under <paramref name="margin"/> (<see cref="BatchMargin"/>). <paramref name="batchEnd"/> keeps a started batch's end on the clock, so the batch is finished even as its lines' time
    /// left moves past the threshold; it is cleared when nothing is due.
    /// </summary>
    private bool PickBatchLine(double clock, double margin, bool skipArmour, bool chain, ref double batchEnd, out BuffCycle.LineState best)
    {
        var threshold = BatchMargin(clock, margin, skipArmour, chain);
        if (!double.IsNaN(batchEnd))
        {
            threshold = Math.Max(threshold, batchEnd - clock);
        }

        if (PickLine(clock, threshold, skipArmour, out best))
        {
            batchEnd = double.IsNaN(batchEnd) ? clock + threshold : Math.Max(batchEnd, clock + threshold);
            _batchThreshold = threshold;
            return true;
        }

        batchEnd = double.NaN;
        return false;
    }

    /// <summary>The note a pick's line carries when its batch reached past the threshold.</summary>
    private string BatchNote(double margin) =>
        _batchThreshold > margin + 0.05 ? string.Create(CultureInfo.InvariantCulture, $" (batch: the lines under {_batchThreshold:0} s go together)") : string.Empty;

    /// <summary>
    /// D126 (a): the threshold that takes a batch whole. With the lines that can be picked (<see cref="Pickable"/>) sorted by their time left, the k-th (from 1) is due when it has less than <paramref name="margin"/> plus k times
    /// <see cref="BatchLineSeconds"/> left (the passes before it come first, so the last line of a batch is reached in time); with <paramref name="chain"/>, every line within <see cref="BatchGapSeconds"/> of the last line taken
    /// joins it. The threshold returned is just over the last line taken, never under <paramref name="margin"/>. Allocates nothing once its list has grown.
    /// </summary>
    private double BatchMargin(double clock, double margin, bool skipArmour, bool chain)
    {
        _batchRemaining.Clear();
        foreach (var state in _refreshStates)
        {
            if (Pickable(state, clock, skipArmour))
            {
                _batchRemaining.Add(state.Remaining);
            }
        }

        _batchRemaining.Sort();
        var taken = 0;
        for (var i = 0; i < _batchRemaining.Count; i++)
        {
            if (_batchRemaining[i] < margin + ((i + 1) * BatchLineSeconds))
            {
                taken = i + 1;
            }
        }

        if (taken == 0)
        {
            return margin;
        }

        if (chain)
        {
            while (taken < _batchRemaining.Count && _batchRemaining[taken] - _batchRemaining[taken - 1] <= BatchGapSeconds)
            {
                taken++;
            }
        }

        return Math.Max(margin, _batchRemaining[taken - 1] + 0.001);
    }

    /// <summary>True when <paramref name="state"/> may be picked now: not an armour line while the memory is unseeded, not in its retry wait.</summary>
    private bool Pickable(in BuffCycle.LineState state, double clock, bool skipArmour) =>
        !(skipArmour && state.Line.Role == ProfileRole.ItemBuffArmor) && !(_refreshBlockedUntil.TryGetValue(state.Line.Name, out var until) && clock < until);

    /// <summary>
    /// D126 (a) (round 12, 09:41 to 09:43 UTC: a walk replanned around creatures to 491 m reached no decision point for two minutes and 15 lines lapsed under it): in the Walk phase, checked every
    /// <see cref="WalkRefreshCheckSeconds"/>, a line under the hard floor (as a batch), or one a pending trip's refresh wants, drops the walk; the next decision recasts it. True when the walk was dropped.
    /// </summary>
    private bool RefreshDropsWalk(IGameSessionView session, double clock)
    {
        if (!_refreshArmed || clock < _walkRefreshCheckAt)
        {
            return false;
        }

        _walkRefreshCheckAt = clock + WalkRefreshCheckSeconds;
        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State));
        var seeding = _refreshCycle.SeedPending;
        _refreshCycle.ReadLines(clock, _refreshStates);
        var hard = _buffOptions.HardMarginSeconds;
        var margin = LeaveRefreshAsked ? Math.Max(_leaveHorizon, hard) : hard;
        var threshold = margin > 0 ? BatchMargin(clock, margin, seeding, chain: false) : 0;
        if (margin <= 0 || !PickLine(clock, threshold, seeding, out var due) || HealthUnderFloor(session, clock))
        {
            return false;
        }

        _walk?.Abandon();
        WalksDroppedForBuffs++;
        var left = due.Active ? string.Create(CultureInfo.InvariantCulture, $"has {due.Remaining:0.#} s left") : "has lapsed";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: the walk is dropped: {due.Line.Name} {left}, under the {margin:0.#} s {(LeaveRefreshAsked ? "trip horizon" : "hard floor")} (its batch: under {threshold:0.#} s); the next decision recasts it (D126)"));
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>
    /// D126 (a): while a stop waits for a trip's refresh (<see cref="RequestLeaveRefresh"/>): true while a line inside the horizon (or the rest of its batch) is still to be recast and the bound has not passed; the fight goes on deciding
    /// meanwhile and its next decision casts the line. False (the stop goes ahead) once none is, or after <see cref="LeaveRefreshBoundSeconds"/>, each logged once.
    /// </summary>
    private bool LeaveRefreshHolds(IGameSessionView session, double clock)
    {
        if (!LeaveRefreshAsked)
        {
            return false;
        }

        if (double.IsNaN(_leaveAskedAt))
        {
            _leaveAskedAt = clock;
        }

        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State));
        var seeding = _refreshCycle.SeedPending;
        _refreshCycle.ReadLines(clock, _refreshStates);
        var threshold = BatchMargin(clock, _leaveHorizon, seeding, chain: true);
        if (!double.IsNaN(_leaveBatchEnd))
        {
            threshold = Math.Max(threshold, _leaveBatchEnd - clock);
        }

        var pending = PickLine(clock, threshold, seeding, out var next);
        if (pending && clock - _leaveAskedAt <= LeaveRefreshBoundSeconds)
        {
            return true;
        }

        if (pending)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: {LeaveRefreshBoundSeconds:0} s passed with {next.Line.Name} still inside the {_leaveHorizon:0} s horizon; the fight ends anyway (TODO(m5-leave-refresh-bound))"));
        }
        else
        {
            LeaveRefreshes++;
            var waiting = 0;
            foreach (var state in _refreshStates)
            {
                waiting += state.Remaining < threshold && !(seeding && state.Line.Role == ProfileRole.ItemBuffArmor) ? 1 : 0; // a line in its retry wait after a failed pass
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: done in {clock - _leaveAskedAt:0.#} s: {(waiting == 0 ? $"no line runs out within {_leaveHorizon:0} s" : $"{waiting} line(s) inside {_leaveHorizon:0} s are in their retry wait after a failed pass")}; the fight ends for its stop"));
        }

        (_leaveHorizon, _leaveBatchEnd) = (double.NaN, double.NaN);
        return false;
    }

    /// <summary>The most urgent line under <paramref name="margin"/> (an armour line left out while its memory is unseeded) that is not in its retry wait: the group first (<see cref="ArenaProfile.RefreshGroupOf"/>), then the least time left.</summary>
    private bool PickLine(double clock, double margin, bool skipArmour, out BuffCycle.LineState best)
    {
        best = default;
        var found = false;
        var bestGroup = RefreshGroup.Wand;
        foreach (var state in _refreshStates)
        {
            if (state.Remaining >= margin || !Pickable(state, clock, skipArmour))
            {
                continue;
            }

            var group = ArenaProfile.RefreshGroupOf(state.Line);
            if (!found || group < bestGroup || (group == bestGroup && state.Remaining < best.Remaining))
            {
                (best, bestGroup, found) = (state, group, true);
            }
        }

        return found;
    }

    /// <summary>Starts the buff cycle's one-line pass for <paramref name="name"/>; true when the phase is the refresh's now.</summary>
    private bool StartLinePass(double clock, string name, string note, bool hard)
    {
        if (!_refreshCycle!.StartRebuffLine(clock, name))
        {
            _log.Warn(LogCategory, "buff refresh: the buff cycle did not start (" + (_refreshCycle.Result?.Failure ?? "a pass is already running") + "); the fight goes on and the autopilot asks again");
            _refreshBlockedUntil[name] = clock + RefreshRetrySeconds;
            _refreshLastTry[name] = (clock, "the cycle did not start: " + (_refreshCycle.Result?.Failure ?? "a pass is already running"));
            if (!hard)
            {
                (_refreshMargin, _refreshWaitLogged) = (double.NaN, false);
            }

            return false;
        }

        _log.Info(LogCategory, note);
        _refreshLine = name;
        _phase = Phase.Refresh;
        return true;
    }

    /// <summary>The refresh pass in flight: stepped to its end, then the fight chooses again.</summary>
    private ScenarioStatus RefreshPassStep(double clock)
    {
        var status = _refreshCycle!.Step(clock);
        if (status == BuffStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        var result = _refreshCycle.Result;
        if (status == BuffStatus.Failed)
        {
            var failure = result?.Failure ?? "?";
            _log.Warn(LogCategory, "buff refresh: the pass failed (" + failure + "); the fight goes on and the autopilot asks again at its next check");
            _refreshBlockedUntil[_refreshLine] = clock + RefreshRetrySeconds;
            _refreshLastTry[_refreshLine] = (clock, "the pass failed: " + failure);
            (_refreshMargin, _refreshWaitLogged) = (double.NaN, false);
        }
        else
        {
            var cast = result?.Cast ?? 0;
            if (cast > 0)
            {
                BuffLinePasses++; // M5 7 F29: a line recast counts toward the buff cycles the sell-trip schedule counts
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: done ({_refreshLine}: {cast} casts, {result?.Active ?? 0} of {result?.Wanted ?? 0} lines active); the fight goes on"));
            _refreshBlockedUntil[_refreshLine] = clock + (cast == 0 ? RefreshFutileSeconds : RefreshRetrySeconds);
            _refreshLastTry[_refreshLine] = (clock, cast == 0 ? "the pass cast nothing (" + (result?.Misses.Count > 0 ? string.Join(';', result.Misses) : "no miss named") + ")" : "the pass cast it");
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    /// <summary>Counts, once each, the lines seen active that are not active now: they ran out under fire (an error-level event naming the line and why).</summary>
    private void CountExpired(double clock, bool seeding)
    {
        foreach (var state in _refreshStates)
        {
            var name = state.Line.Name;
            if (seeding && state.Line.Role == ProfileRole.ItemBuffArmor)
            {
                continue; // the armour memory is not seeded yet: an armour line cannot be read as ended
            }

            if (state.Active)
            {
                _refreshSeen.Add(name);
            }
            else if (_refreshSeen.Remove(name))
            {
                BuffsExpired++;
                var why = _refreshLastTry.TryGetValue(name, out var tried)
                    ? string.Create(CultureInfo.InvariantCulture, $"the last recast try, {clock - tried.At:0} s ago, ended: {tried.Outcome}")
                    : _refreshBlock.Length > 0 ? "no recast was reached: " + _refreshBlock : "no recast was tried before it ran out";
                _log.Error(LogCategory, "buff refresh: " + name + " EXPIRED under fire: " + why);
            }
        }
    }

    /// <summary>True while the health is under D88's drain or heal line (the floors that come before a refresh): the same rule the drain and the heal read.</summary>
    private bool HealthUnderFloor(IGameSessionView session, double clock) =>
        _drain.UnderPercent > 0 && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) && _drain.Wanted(health, healthMax, clock);

    /// <summary>True when a hostile is in reach: a hit taken within <see cref="RefreshQuietSeconds"/>, or a living target creature of the fight within the reach.</summary>
    private bool RefreshHostile(IGameSessionView session, double clock, out string what)
    {
        if (_tally.HitsTaken != _refreshHitsSeen)
        {
            (_refreshHitsSeen, _refreshLastHitAt) = (_tally.HitsTaken, clock);
            if (double.IsNaN(_fightStartAt))
            {
                _fightStartAt = clock; // M5 7 F25: a fight's first hit taken
            }
        }

        if (clock - _refreshLastHitAt < RefreshQuietSeconds)
        {
            what = string.Create(CultureInfo.InvariantCulture, $"a hit was taken {clock - _refreshLastHitAt:0.#} s ago");
            return true;
        }

        what = string.Empty;
        if (session.State.Self.Position is not { } here || _refreshReach <= 0)
        {
            return false;
        }

        var origin = here.ToGlobal();
        foreach (var o in session.State.Objects.All)
        {
            if (o.Position is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
            {
                continue;
            }

            var listed = false;
            for (var i = 0; i < _targetWcids.Count && !listed; i++)
            {
                listed = _targetWcids[i] == o.Weenie.WeenieClassId;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (listed && d <= _refreshReach)
            {
                what = string.Create(CultureInfo.InvariantCulture, $"{o.Name ?? "creature"} 0x{o.Guid:X8} is {d:0.#} m away");
                return true;
            }
        }

        return false;
    }
}
