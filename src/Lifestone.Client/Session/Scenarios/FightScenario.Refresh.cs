using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
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
/// <para>M6 14 (<c>m6-banes</c>): the fight is the field. Its cycles carry the damage types its ground's targets deal (<see cref="BuffOptions.FieldDamageTypes"/>, <see cref="ArmourBanes.FieldMask"/>), so of the climber's
/// eight armour lines the refresh reads, recasts and counts Impenetrability and Bludgeon Bane as before, and one of the six other banes only where a target deals its damage type. The others are the pass at home's: they are
/// not recast under the hard floor, at a lull or before a trip, and their running out in the ground is no <see cref="BuffsExpired"/>. The reason is the batch: eight lines cast together at home come due together, a level IV
/// armour line is 3 s a cast and up to 120 mana on ten worn pieces, and a hard batch of eight would be 25 s with no bolt cast every half hour (<see cref="ArmourBanes"/>, its remarks).</para>
/// <para>M6 14 gate lapse (live, 2026-11-13: in the Tusker Barracks a hostile was always in reach, so no lull came). Under fire, between kills (the first decision after a kill, held until the next attack), a line is recast now when it would
/// otherwise fall under the hard floor, or lapse, in the middle of the next kill: its time left is under the hard floor plus the fight's own seconds a kill plus the recast's expected seconds (the measured cast seconds over the live cast
/// chance, <see cref="KillGapPlan"/>). It is cast only when standing that long at the live incoming damage leaves her over the heal line (else the heal comes first and the line waits for the first safe moment), the health floors and
/// the way out's mana first, as the hard pass. Nothing in it is a new constant.</para>
/// <para>M6 14 upkeep mana (live, 2026-11-14 01:52 to 02:15 UTC, the Tusker Quarters at level 58: the mana sat at 150 to 200 of 377 and every recast the three rules reached was refused by the way out's guard, which wants the cast's
/// worst-case cost plus the way out's line; the armour lines lapsed under fire at 02:14:35 with no line saying why). The vitals policy now plans the upkeep (<see cref="VitalsPolicy.Upkeep"/>: it fills toward the guard's number before a
/// line falls due), and every rule's hold goes through one gate (<see cref="GateFor"/>): the heal first, else the way out's mana, each said once per line and reason with its numbers, the policy's fill started at once when it has one,
/// and the expiry line naming the hold (<see cref="ExpiredWhy"/>). A walk is not dropped for a recast the guard refuses when no fill is to be had.</para>
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

    /// <summary>
    /// D126 (a), re-measured by M6 item 1 g from the long run (the 10-hour fight at the Black Spawn Den, 2026-10-30 14:37 to 2026-10-31 00:45 UTC; <c>cache/m6/longrun-measures/SUMMARY.md</c>, "m5-batch-figures: measured"):
    /// the seconds one line's pass takes, for the batch's schedule. A whole batch spent a median 1.94 s a line (p90 2.13, max 2.40; 30 batches, the 25-line passes 2.06 and 2.40 s a line, lifestone.log:9450 and :48300).
    /// Soak round 12's 1.46 s a line (13 lines in 19 s) under-planned it: 25 lines planned at 37.5 s took 51 to 60 s. Closes <c>m5-batch-figures</c>.
    /// </summary>
    public const double BatchLineSeconds = 2.0;

    /// <summary>D126 (a), Lifestone's own: at a lull and before a trip, a line due within this many seconds of the last line taken joins the batch (round 12's batches spread over 25 s and 50 s).</summary>
    public const double BatchGapSeconds = 60;

    /// <summary>
    /// D126 (a), re-measured by M6 item 1 g from the long run (<c>cache/m6/longrun-measures/SUMMARY.md</c>, "m5-leave-refresh-bound: measured"): the longest a stop for a trip waits for the lines due within the trip's length to be recast.
    /// The pre-trip refresh stop took a median 0.02 s and at most 17.7 s over 13 leaves (the trips that did work recast 3, 2 and 5 lines in 7.9, 11.2 and 17.2 s), and the old 120 s bound was never hit; 45 s keeps a margin of
    /// two and a half times the worst. Closes <c>m5-leave-refresh-bound</c>.
    /// </summary>
    public const double LeaveRefreshBoundSeconds = 45;

    /// <summary>
    /// M6 1 fix (1-R6): the bound a stop for a trip waits for a refresh of <paramref name="lines"/> lines: the larger of <see cref="LeaveRefreshBoundSeconds"/> (the long run's measure stays the floor's source) and the batch's own plan,
    /// <paramref name="lines"/> x <see cref="BatchLineSeconds"/> plus the refresh's margin (<see cref="BuffOptions.LookaheadMarginSeconds"/>), so a full same-age batch of 25 lines (about 50 s at 2.0 s a line) is never cut.
    /// </summary>
    /// <param name="lines">The lines the leave refresh has to recast (the batch's size when it began).</param>
    /// <param name="marginSeconds">The refresh's margin.</param>
    public static double LeaveBoundFor(int lines, double marginSeconds) => Math.Max(LeaveRefreshBoundSeconds, (Math.Max(0, lines) * BatchLineSeconds) + Math.Max(0, marginSeconds));

    /// <summary>
    /// M6 item 1 g (<c>m6-buff-recovery-batch</c>, the long run's finding 1): the fewest lines that make a "same age" group worth a stagger. The long run's big batches (<c>measure-out.txt</c>: ten lines or more) were 22 lines an hour
    /// and, after a recovery or a hand-off pass, 25; a handful of lines is an ordinary batch (<see cref="BatchLineSeconds"/> plans it).
    /// </summary>
    public const int StaggerMinLines = 10;

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
    private readonly Dictionary<string, (double LastLeft, double Offset)> _refreshStagger = new(StringComparer.Ordinal);
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
    private double _leaveBound = double.NaN;
    private string _leaveDetail = string.Empty;
    private double _walkRefreshCheckAt = double.NegativeInfinity;
    private bool _refreshPassHard;
    private int _refreshPassHits;
    private bool _refreshReserveLogged;
    private int _refreshKillsSeen;
    private bool _killBoundary;
    private readonly Dictionary<string, string> _refreshWaitNoted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double At, string Why)> _refreshHeld = new(StringComparer.Ordinal);
    private double _refreshBlockAt = double.NaN;
    private double _upkeepAskedAt = double.NaN;
    private string _upkeepWhat = string.Empty;

    /// <summary>M6 7 AZ: the refresh passes ended by name because she was attacked or a hostile was in reach (the verdict's report-only count).</summary>
    public int RefreshAborts { get; private set; }

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

        (_leaveHorizon, _leaveAskedAt, _leaveDetail, _leaveBatchEnd, _leaveBound) = (Math.Max(0, horizonSeconds), double.NaN, detail ?? string.Empty, double.NaN, double.NaN);
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
            (_refreshBlock, _refreshBlockAt) = (why, _lastClock);
        }
    }

    /// <summary>M6 14 upkeep mana: what stands between a line the refresh picked and its recast.</summary>
    private enum RefreshGate : byte
    {
        /// <summary>Nothing: the recast goes.</summary>
        Go,

        /// <summary>The heal or the way out's mana holds it (named, once per line and reason).</summary>
        Held,

        /// <summary>The way out's mana holds it and the vitals policy's fill toward it began.</summary>
        Filling,
    }

    /// <summary>
    /// M6 14 upkeep mana (live 2026-11-14 01:52 to 02:15 UTC: every recast under fire was refused for the way out's mana, and only <c>_refreshBlock</c> was set, so no line said why): the gate of every rule that recasts under fire (the
    /// hard floor, the leave refresh, between kills), checked before the recast. Under the heal line the heal comes first; a recast the way out's guard refuses (its cost at the worst roll would take the mana under the line, with a
    /// hostile on her) is held, the vitals policy's fill toward it starts now when it has one (<see cref="UpkeepPlan"/>: the press, a stamina drain first, or a drink), and the next decision asks again. Each hold is one line per line
    /// and reason with its numbers (<see cref="Hold"/>), and the expiry line names it.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="line">The line picked.</param>
    /// <param name="hostile">The refresh's read: a hit just taken or a target in reach.</param>
    /// <param name="rule">The rule that picked it, for the line.</param>
    private RefreshGate GateFor(IGameSessionView session, double clock, ProfileLine line, bool hostile, string rule)
    {
        if (HealthUnderFloor(session, clock))
        {
            session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
            Hold(clock, line.Name, "heal", string.Create(CultureInfo.InvariantCulture, $"{line.Name} waits for the heal: health {health} of {healthMax} is under the heal line {healthMax * (double)_drain.UnderPercent / 100:0}; the heal comes first and the line is recast at the first moment it is safe ({rule})"));
            return RefreshGate.Held;
        }

        if (!WayOutRefusesBuff(session, clock, line, hostile, out var mana, out var cost, out var reserve))
        {
            return RefreshGate.Go;
        }

        var fill = UpkeepPlanOnce(session, clock, out var plan, out var facts, out var reads, out var what);
        var need = (ulong)cost + reserve;
        var over = session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var manaMax) && need > manaMax
            ? string.Create(CultureInfo.InvariantCulture, $"; the need is over her maximum {manaMax}: under fire it cannot be met, the line waits for a lull")
            : string.Empty;
        Hold(clock, line.Name, "mana", string.Create(CultureInfo.InvariantCulture, $"{line.Name} waits for mana: {mana} of the {need} the way out needs (cast up to {cost} + reserve {reserve}); {what}{over} ({rule}; m6-upkeep-mana)"));
        return fill && UpkeepStart(session, clock, in plan, in facts, in reads) ? RefreshGate.Filling : RefreshGate.Held;
    }

    /// <summary>M6 14 upkeep mana: <see cref="UpkeepPlan"/>, asked once a decision when it finds no fill (the rules after the first hold read its answer again).</summary>
    private bool UpkeepPlanOnce(IGameSessionView session, double clock, out VitalsPlan plan, out VitalsPolicyFacts facts, out PolicyReads reads, out string what)
    {
        if (_upkeepAskedAt == clock)
        {
            (plan, facts, reads, what) = (default, default, default, _upkeepWhat);
            return false;
        }

        if (UpkeepPlan(session, clock, out plan, out facts, out reads, out what))
        {
            return true;
        }

        (_upkeepAskedAt, _upkeepWhat) = (clock, what);
        return false;
    }

    /// <summary>
    /// M6 14 upkeep mana: notes a line's hold for its expiry line, and logs it once per line and reason (<paramref name="kind"/>: the heal, the mana, standing unsafe); the note is dropped when the line's pass starts.
    /// </summary>
    private void Hold(double clock, string name, string kind, string text)
    {
        _refreshHeld[name] = (clock, text);
        (_refreshBlock, _refreshBlockAt) = (text, clock);
        if (_refreshWaitNoted.TryGetValue(name, out var noted) && string.Equals(noted, kind, StringComparison.Ordinal))
        {
            return;
        }

        _refreshWaitNoted[name] = kind;
        _log.Info(LogCategory, "buff refresh: " + text);
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

        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State), profile: _options.BuffProfile);
        var seeding = _refreshCycle.SeedPending;
        ReadRefreshLines(clock);
        CountExpired(clock, seeding);

        var hostile = RefreshHostile(session, clock, out var what); // M5 7 F25: also notes a fight's first hit, for its length
        var hard = _buffOptions.HardMarginSeconds;
        _upkeepAskedAt = double.NaN; // M6 14 upkeep mana: a plan that found no fill is not asked again in this decision
        if (hard > 0 && PickBatchLine(clock, hard, seeding, chain: false, ref _hardBatchEnd, out var urgent))
        {
            var gate = GateFor(session, clock, urgent.Line, hostile, "the hard floor");
            if (gate == RefreshGate.Filling)
            {
                return true; // M6 14 upkeep mana: the policy's fill toward the recast began
            }

            if (gate == RefreshGate.Go)
            {
                var left = urgent.Active ? string.Create(CultureInfo.InvariantCulture, $"has {urgent.Remaining:0.#} s left, under the {hard:0.#} s hard floor{BatchNote(hard)}") : "has lapsed";
                var note = string.Create(CultureInfo.InvariantCulture, $"buff refresh: hard: {urgent.Line.Name} {left}: recast between the fight's casts{(what.Length > 0 ? " with " + what : " with no hostile in reach")} (F24, F25)");
                return StartLinePass(clock, urgent.Line.Name, note, hard: true, session);
            }
        }

        if (LeaveRefreshAsked && PickBatchLine(clock, _leaveHorizon, seeding, chain: true, ref _leaveBatchEnd, out var leaving))
        {
            var gate = GateFor(session, clock, leaving.Line, hostile, "the leave refresh");
            if (gate == RefreshGate.Filling)
            {
                return true;
            }

            if (gate == RefreshGate.Go)
            {
                var left = leaving.Active ? string.Create(CultureInfo.InvariantCulture, $"has {leaving.Remaining:0} s left") : "has lapsed";
                return StartLinePass(clock, leaving.Line.Name, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: {leaving.Line.Name} {left}, inside the {_leaveHorizon:0} s before the fight ends ({_leaveDetail}){BatchNote(_leaveHorizon)}; recast first (D126)"), hard: true, session);
            }
        }

        if (_tally.Kills != _refreshKillsSeen)
        {
            (_refreshKillsSeen, _killBoundary) = (_tally.Kills, true); // M6 14 gate lapse: a kill: the fight stands between kills until its next attack
        }

        if (hostile && _killBoundary)
        {
            // M6 14 gate lapse: under fire no lull comes; between kills, a line that would fall under the hard floor (or lapse) before the next kill is over is recast now, at the first moment it is safe to stand for it
            if (!PickBetweenKills(session, clock, seeding, hard, out var next, out var plan))
            {
                _killBoundary = false; // no line falls under the floor before the next kill and its recast: the attack goes on
            }
            else if (!plan.Safe)
            {
                // M6 14 gate lapse: the heal and the fight first; M6 14 upkeep mana: the policy's heal toward the standing health comes at once when it can (live 02:15:01 to 02:15:03 UTC: the heal came only because a drain's whole give fit)
                var fill = UpkeepPlanOnce(session, clock, out var upkeepPlan, out var facts, out var reads, out var planned) && upkeepPlan.Choice.Move == VitalsMove.DrainHealth; // only the heal: a press is a stand too
                var fillWhat = fill ? planned : "no heal toward the stand is to be had now: the heal line's own heal and the fight's drains come first";
                Hold(clock, next.Line.Name, "unsafe", string.Create(CultureInfo.InvariantCulture, $"between kills: {next.Line.Name} waits: standing {plan.LandSeconds:0.#} s for it at {plan.Incoming:0.##} damage a second would leave {plan.HealthAfter:0} health, not over the heal line {plan.HealLine:0}: the heal and the fight come first; it is recast at the first moment it is safe; {fillWhat} (M6 14 gate lapse)"));
                if (fill && UpkeepStart(session, clock, in upkeepPlan, in facts, in reads))
                {
                    return true;
                }
            }
            else
            {
                var gate = GateFor(session, clock, next.Line, hostile, "between kills");
                if (gate == RefreshGate.Filling)
                {
                    return true;
                }

                if (gate == RefreshGate.Go)
                {
                    var left = next.Active ? string.Create(CultureInfo.InvariantCulture, $"has {next.Remaining:0.#} s left") : "has lapsed";
                    var note = string.Create(CultureInfo.InvariantCulture, $"buff refresh: between kills: {next.Line.Name} {left}, under {plan.Horizon(hard):0.#} s: the {hard:0.#} s hard floor, the next kill's {plan.KillGap:0.#} s and its recast's {plan.LandSeconds:0.#} s ({plan.CastSeconds:0.##} s a cast at a chance of {plan.Chance:0.###}); standing for it at {plan.Incoming:0.##} damage a second leaves {plan.HealthAfter:0} health over the heal line {plan.HealLine:0}: recast now, with {what} (M6 14 gate lapse)");
                    return StartLinePass(clock, next.Line.Name, note, hard: true, session);
                }
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
        if (AwakeNear(session, out var awake))
        {
            if (BuffRefreshAsked && !_refreshWaitLogged)
            {
                _refreshWaitLogged = true;
                _log.Info(LogCategory, $"buff refresh: waits: {awake} is awake in reach; no lookahead pass chains while a creature is awake nearby (M6 7 AZ); a line under the {hard:0.#} s hard floor is still recast between the casts");
            }

            return false;
        }

        var mean = FightMeanSeconds;
        var horizon = _buffOptions.LookaheadMarginSeconds; // M6 7 AZ: only lines really about to lapse (the margin alone); the fight's mean no longer widens it: a full pass belongs at home before the trip (AW), not in a fight
        if (PickBatchLine(clock, horizon, false, chain: true, ref _lullBatchEnd, out var due))
        {
            var reserve = LadderLoader.WarMage.Vitals.RefreshManaReserve;
            if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) && mana < reserve)
            {
                if (!_refreshReserveLogged)
                {
                    _refreshReserveLogged = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: lookahead waits: mana {mana} of {manaMax} is under the reserve {reserve} (refresh_mana_reserve) that the mana loop and a drain keep; {due.Line.Name} and the lines behind it wait for the next lull with the mana, or for home (M6 7 AZ)"));
                }

                return false;
            }

            _refreshReserveLogged = false;
            return StartLinePass(clock, due.Line.Name, string.Create(CultureInfo.InvariantCulture, $"buff refresh: lookahead: {due.Line.Name} {due.Remaining:0} s left < next lull {horizon:0} s (margin {_buffOptions.LookaheadMarginSeconds:0}; the fight's mean {mean:0} s is read, not added: M6 7 AZ){BatchNote(horizon)}; no hostile within {_refreshReach:0.#} m"), hard: false, session);
        }

        if (BuffRefreshAsked)
        {
            BuffRefreshes++; // every line inside the lookahead's horizon is recast (or tried): the ask is answered
            (_refreshMargin, _refreshWaitLogged) = (double.NaN, false);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: done: no line runs out before the next lull ({horizon:0} s) that has not been tried; the fight goes on"));
        }

        return false;
    }

    /// <summary>
    /// M6 14 gate lapse: the numbers of one line's recast between kills, all read live. <see cref="KillGap"/>: the fight's own seconds a kill so far (the time to the next moment between kills); <see cref="CastSeconds"/>: the measured seconds
    /// of a self cast at the level the pass would cast (the ladder's <c>cast_seconds_self</c>, from the live logs); <see cref="Chance"/>: that level's cast chance on her live skill; <see cref="LandSeconds"/>: the expected seconds to land it,
    /// fizzles retried (cast seconds over chance); <see cref="Incoming"/>: the damage she takes a second now (the larger of the fight's mean and the recent hits); <see cref="HealthAfter"/>: her health after standing that long;
    /// <see cref="HealLine"/>: the drain and heal line's health.
    /// </summary>
    /// <param name="KillGap">The seconds a kill.</param>
    /// <param name="CastSeconds">The seconds of one cast.</param>
    /// <param name="Chance">The cast chance.</param>
    /// <param name="LandSeconds">The expected seconds to land the recast.</param>
    /// <param name="Incoming">The damage a second.</param>
    /// <param name="HealthAfter">The health after standing <paramref name="LandSeconds"/>.</param>
    /// <param name="HealLine">The heal line's health.</param>
    internal readonly record struct KillGapPlan(double KillGap, double CastSeconds, double Chance, double LandSeconds, double Incoming, double HealthAfter, double HealLine)
    {
        /// <summary>True when standing for the recast leaves her over the heal line: it is safe to stand and cast.</summary>
        public bool Safe => HealthAfter > HealLine;

        /// <summary>The time left under which a line is recast now: the hard floor (the line must not fall under it in the middle of the next kill), the next kill's seconds and the recast's own.</summary>
        /// <param name="hardFloor">The hard floor (<see cref="BuffOptions.HardMarginSeconds"/>).</param>
        public double Horizon(double hardFloor) => Math.Max(0, hardFloor) + KillGap + LandSeconds;
    }

    /// <summary>M6 14 gate lapse: the expected seconds to land a cast of <paramref name="castSeconds"/> at <paramref name="chance"/>, fizzles retried (a geometric count of tries); infinite at no chance.</summary>
    /// <param name="castSeconds">The seconds of one cast.</param>
    /// <param name="chance">The cast chance.</param>
    internal static double LandSeconds(double castSeconds, double chance) => chance > 0 ? Math.Max(0, castSeconds) / Math.Min(1, chance) : double.PositiveInfinity;

    /// <summary>M6 14 gate lapse: <see cref="KillGapPlan"/> from its live inputs.</summary>
    /// <param name="killGap">The fight's seconds a kill.</param>
    /// <param name="castSeconds">The seconds of one cast.</param>
    /// <param name="chance">The cast chance.</param>
    /// <param name="health">Her health now.</param>
    /// <param name="incoming">The damage a second.</param>
    /// <param name="healLine">The heal line's health.</param>
    internal static KillGapPlan BetweenKillsPlan(double killGap, double castSeconds, double chance, double health, double incoming, double healLine)
    {
        var land = LandSeconds(castSeconds, chance);
        return new KillGapPlan(killGap, castSeconds, chance, land, Math.Max(0, incoming), health - (Math.Max(0, incoming) * land), healLine);
    }

    /// <summary>
    /// M6 14 gate lapse: between kills under fire, the most urgent line (the group first, then the least time left; <see cref="Pickable"/>) whose time left is under its <see cref="KillGapPlan.Horizon"/>, with its plan. False when none is, or
    /// before the first kill (no seconds a kill to read).
    /// </summary>
    private bool PickBetweenKills(IGameSessionView session, double clock, bool seeding, double hard, out BuffCycle.LineState best, out KillGapPlan plan)
    {
        best = default;
        plan = default;
        if (_tally.Kills <= 0 || double.IsNaN(_fightStart) || _fightStart <= 0 || clock <= _fightStart)
        {
            return false;
        }

        var killGap = (clock - _fightStart) / _tally.Kills;
        var q = session.State.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax);
        var incoming = Math.Max(Picker.Rates.IncomingPerSecond, Picker.Rates.Read(clock).PerSecond);
        var healLine = _drain.UnderPercent > 0 ? healthMax * (double)_drain.UnderPercent / 100 : 0;
        var found = false;
        var bestGroup = RefreshGroup.Wand;
        foreach (var state in _refreshStates)
        {
            if (!Pickable(state, clock, seeding) || !TryLineCast(session, state.Line, out var castSeconds, out var chance))
            {
                continue;
            }

            var candidate = BetweenKillsPlan(killGap, castSeconds, chance, health, incoming, healLine);
            if (state.Remaining >= candidate.Horizon(hard))
            {
                continue;
            }

            var group = ArenaProfile.RefreshGroupOf(state.Line);
            if (!found || group < bestGroup || (group == bestGroup && state.Remaining < best.Remaining))
            {
                (best, bestGroup, plan, found) = (state, group, candidate, true);
            }
        }

        return found;
    }

    /// <summary>M6 14 gate lapse: the seconds of one cast of the line at the level the pass would cast, and that level's chance on her live skill; false when no level is in reach.</summary>
    private bool TryLineCast(IGameSessionView session, ProfileLine line, out double castSeconds, out double chance)
    {
        (castSeconds, chance) = (0, 0);
        var level = _refreshCycle!.ReachLevel(line.Name);
        if (level < 1 || level > line.Spells.Count || !GearCasts.TryCurrentSkill(session.State.Self.Qualities, GearCasts.SkillOf(line.School), out var skill, out _))
        {
            return false;
        }

        chance = GearCasts.Chance(skill, line.Spells[level - 1].Power);
        var policy = LadderLoader.WarMage.VitalsPolicy;
        castSeconds = policy.CastSecondsSelf.Count > 0 ? policy.SelfSeconds(level) : BatchLineSeconds; // the live logs' measure; else the measured line pass
        return chance > 0;
    }

    /// <summary>M6 item 1 g: reads the buff lines for the schedule (<see cref="BuffCycle.ReadLines"/>) and staggers a same-age group (<see cref="StaggerSameAge"/>), logging the group once.</summary>
    private void ReadRefreshLines(double clock)
    {
        _refreshCycle!.ReadLines(clock, _refreshStates);
        var staggered = StaggerSameAge(_refreshStates, _refreshStagger, BatchLineSeconds);
        if (staggered > 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: stagger: {staggered} lines read with the same time left (a recovery or hand-off pass; m6-buff-recovery-batch): each is scheduled {BatchLineSeconds:0.#} s behind the one recast before it, the first recast {(staggered - 1) * BatchLineSeconds:0.#} s early, so the batch is reached in recast order and not as {staggered} ties"));
        }
    }

    /// <summary>
    /// M6 item 1 g (<c>m6-buff-recovery-batch</c>; the long run, <c>cache/m6/longrun-measures/SUMMARY.md</c> finding 1): after a recovery or a hand-off pass the client reads every line with the same time left, so all of them fall
    /// due together and the batch is reached as <c>n</c> ties (the 22:17 and 23:43 batches took 51 and 60 s for 25 lines). When <paramref name="states"/> holds <see cref="StaggerMinLines"/> or more active lines whose time left
    /// is within <paramref name="step"/> of each other, in the recast order (<see cref="ArenaProfile.RefreshGroupOf"/>, then the buff cycle's own order) the k-th line (from 0) is scheduled <c>(n - 1 - k) * step</c> seconds earlier
    /// than it reads: the first line recast is the most urgent and the lines are <paramref name="step"/> apart (the batch's own spacing, <see cref="BatchLineSeconds"/>), so the pick order is the recast order. A line is renewed at
    /// most <c>(n - 1) * step</c> seconds early (48 s of a half-hour or longer buff for 25 lines at 2 s), which is the "renew the earliest lines early" cost; the offset is dropped when the line is recast or ends. The server's own
    /// durations are unchanged (ACE <c>Enchantment</c> start time and duration are the spell's: nothing is shortened on the server). Returns the number of lines newly staggered (0: none, or already held).
    /// </summary>
    /// <param name="states">The lines as read, edited in place (the time left of a staggered line is its scheduled time left).</param>
    /// <param name="memory">Per line: the time left seen at its last read and the offset held; kept between reads.</param>
    /// <param name="step">The seconds between two lines of the group (<see cref="BatchLineSeconds"/>).</param>
    internal static int StaggerSameAge(List<BuffCycle.LineState> states, Dictionary<string, (double LastLeft, double Offset)> memory, double step)
    {
        // A line that was recast (its time left rose) or ended drops its offset; a line still counting down keeps it.
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var name = state.Line.Name;
            if (!memory.TryGetValue(name, out var held))
            {
                continue;
            }

            if (!state.Active || state.Remaining > held.LastLeft + 0.5)
            {
                memory.Remove(name);
                continue;
            }

            memory[name] = (state.Remaining, held.Offset);
            states[i] = state with { Remaining = Math.Max(0, state.Remaining - held.Offset) };
        }

        if (memory.Count > 0)
        {
            foreach (var name in memory.Keys.Where(k => !states.Any(s => string.Equals(s.Line.Name, k, StringComparison.Ordinal))).ToList())
            {
                memory.Remove(name); // a line no longer read (an armour line with nothing worn)
            }

            return 0; // a group is being worked through: the next one is looked for when it is done
        }

        if (states.Count < StaggerMinLines)
        {
            return 0;
        }

        var live = new List<int>();
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i].Active && states[i].Remaining > 0)
            {
                live.Add(i);
                low = Math.Min(low, states[i].Remaining);
                high = Math.Max(high, states[i].Remaining);
            }
        }

        if (live.Count < StaggerMinLines || high - low > step)
        {
            return 0;
        }

        var ordered = live.OrderBy(i => ArenaProfile.RefreshGroupOf(states[i].Line)).ToList(); // stable: the cycle's own order within a group
        for (var k = 0; k < ordered.Count; k++)
        {
            var i = ordered[k];
            var offset = (ordered.Count - 1 - k) * step;
            memory[states[i].Line.Name] = (states[i].Remaining, offset);
            states[i] = states[i] with { Remaining = Math.Max(0, states[i].Remaining - offset) };
        }

        return ordered.Count;
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
        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State), profile: _options.BuffProfile);
        var seeding = _refreshCycle.SeedPending;
        ReadRefreshLines(clock);
        var hard = _buffOptions.HardMarginSeconds;
        var margin = LeaveRefreshAsked ? Math.Max(_leaveHorizon, hard) : hard;
        var threshold = margin > 0 ? BatchMargin(clock, margin, seeding, chain: false) : 0;
        if (margin <= 0 || !PickLine(clock, threshold, seeding, out var due) || HealthUnderFloor(session, clock))
        {
            return false;
        }

        if (BuffWayOutHolds(session, clock, due.Line, hostile: false, out var mana, out var cost, out var reserve, out _) && !UpkeepPlan(session, clock, out _, out _, out _, out var what))
        {
            // M6 14 upkeep mana (live 2026-11-14 02:16:48 to 02:17:50 UTC: the walk was dropped 56 times, about once a second, for a recast the way out's guard refused each time): with no fill to be had, dropping the walk gains nothing
            Hold(clock, due.Line.Name, "mana", string.Create(CultureInfo.InvariantCulture, $"{due.Line.Name} waits for mana: {mana} of the {(ulong)cost + reserve} the way out needs (cast up to {cost} + reserve {reserve}); {what}; the walk is not dropped for it (m6-upkeep-mana)"));
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
    /// meanwhile and its next decision casts the line. False (the stop goes ahead) once none is, or after the bound (<see cref="LeaveBoundFor"/>: at least <see cref="LeaveRefreshBoundSeconds"/>), each logged once.
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

        _refreshCycle ??= new BuffCycle(session, _spells, _buffOptions with { RebuffMarginSeconds = RefreshCycleMarginSeconds }, _log, ArmourBuffLedger.For(session.State), profile: _options.BuffProfile);
        var seeding = _refreshCycle.SeedPending;
        ReadRefreshLines(clock);
        var threshold = BatchMargin(clock, _leaveHorizon, seeding, chain: true);
        if (!double.IsNaN(_leaveBatchEnd))
        {
            threshold = Math.Max(threshold, _leaveBatchEnd - clock);
        }

        if (double.IsNaN(_leaveBound))
        {
            // M6 1 fix (1-R6): the bound is set once, from the batch's size when the leave began (the lines inside the threshold that can be picked).
            var batch = 0;
            foreach (var state in _refreshStates)
            {
                batch += state.Remaining < threshold && Pickable(state, clock, seeding) ? 1 : 0;
            }

            _leaveBound = LeaveBoundFor(batch, _buffOptions.LookaheadMarginSeconds);
            if (_leaveBound > LeaveRefreshBoundSeconds)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: {batch} line(s) in the batch: the bound is {_leaveBound:0} s ({batch} x {BatchLineSeconds:0.#} s + the {_buffOptions.LookaheadMarginSeconds:0} s margin), over the {LeaveRefreshBoundSeconds:0} s floor"));
            }
        }

        var pending = PickLine(clock, threshold, seeding, out var next);
        if (pending && clock - _leaveAskedAt <= _leaveBound)
        {
            return true;
        }

        if (pending)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: leave: {_leaveBound:0} s passed with {next.Line.Name} still inside the {_leaveHorizon:0} s horizon; the fight ends anyway (TODO(m5-leave-refresh-bound))"));
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
    private bool StartLinePass(double clock, string name, string note, bool hard, IGameSessionView session)
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
        _refreshWaitNoted.Remove(name); // M6 14 upkeep mana: its wait is over; a later hold is named again
        _refreshHeld.Remove(name);
        _refreshLine = name;
        (_refreshPassHard, _refreshPassHits) = (hard, _tally.HitsTaken + _tally.Evades);
        _phase = Phase.Refresh;
        return true;
    }

    /// <summary>The refresh pass in flight: stepped to its end, then the fight chooses again.</summary>
    private ScenarioStatus RefreshPassStep(IGameSessionView session, double clock)
    {
        RefreshPassWatch(session, clock);
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
            if (cast == 0 && _refreshCycle.Aborting)
            {
                // M6 14 (live ladder37, 2026-11-16 09:19:01 to 09:19:57 UTC): a pass the fight ended (RefreshPassWatch) finishes through here with nothing cast. Its abort's own retry and reason stand: the 300 s futile wait is for a pass that
                // could not cast at all, and it held Impenetrability (56 s left) out of the next hard pass until it lapsed, its expiry blaming "the pass cast nothing".
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff refresh: {_refreshLine}: the pass was ended by the fight before it cast; the line is tried again {Math.Max(0, (_refreshBlockedUntil.TryGetValue(_refreshLine, out var until) ? until : clock) - clock):0.#} s from now (the abort's retry)"));
            }
            else
            {
                _refreshBlockedUntil[_refreshLine] = clock + (cast == 0 ? RefreshFutileSeconds : RefreshRetrySeconds);
                _refreshLastTry[_refreshLine] = (clock, cast == 0 ? "the pass cast nothing (" + (result?.Misses.Count > 0 ? string.Join(';', result.Misses) : "no miss named") + ")" : PassCastIt);
            }
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M6 7 AZ (live, 2026-11-06 23:25 to 23:27 UTC, level 19: a refresh pass stood in its mana wait in peace mode while tuskers hit her to 0): check before each step of a pass in flight, act, check after (the cycle's own end line). A pass ends at once, by name,
    /// when she was attacked since it began (a hit taken or an evade), or, for a soft pass, when a hostile is in reach; a hard pass (a line under the floor) goes on casting whatever is in reach but ends when it has to wait for mana or change mode. The cycle
    /// finishes a cast in flight, comes back to the magic mode and ends; the line is held for <see cref="RefreshRetrySeconds"/> so the fight does not start it again at once, and the fight's order (the next Choose) takes over.
    /// </summary>
    private void RefreshPassWatch(IGameSessionView session, double clock)
    {
        var cycle = _refreshCycle!;
        if (cycle.Aborting || cycle.Status != BuffStatus.Running)
        {
            return;
        }

        string? why = null;
        var waiting = cycle.Stage is BuffCycle.StageManaWait or BuffCycle.StageMode; // a hard pass under fire casts (F24); it ends only when it has to stand
        if (!_refreshPassHard || waiting)
        {
            if (_tally.HitsTaken + _tally.Evades != _refreshPassHits)
            {
                why = "she was attacked (a hit or an evade since the pass began)";
            }
            else if (RefreshHostile(session, clock, out var what) || AwakeNear(session, out what))
            {
                why = what;
            }

            if (why is not null && _refreshPassHard)
            {
                why += ", and the hard pass has to wait";
            }
        }

        if (why is null)
        {
            return;
        }

        why = "the fight's order comes first: " + why;
        if (cycle.Abort(why))
        {
            RefreshAborts++;
            _refreshBlockedUntil[_refreshLine] = clock + RefreshRetrySeconds;
            _refreshLastTry[_refreshLine] = (clock, "the pass was ended: " + why);
            _log.Warn(LogCategory, $"buff refresh: the pass for {_refreshLine} is ended: {why}; she returns to the magic mode and the fight's order; the line is tried again in {RefreshRetrySeconds:0} s (M6 7 AZ)");
            if (LeaveRefreshAsked)
            {
                // M6 7 BD (live, 2026-11-07 03:34:19 to 03:34:49 UTC: thirteen lines of the leave refresh each started and ended on a hit, 30 s with no bolt): a leave that was fought is over; the stop goes on, and the state after the fight fights back (AutopilotScenario.KeeperStands)
                _log.Warn(LogCategory, $"buff refresh: leave: dropped: {why}: no more lines are started for the trip; they are due again at the next lull or at home");
                (_leaveHorizon, _leaveBatchEnd) = (double.NaN, double.NaN);
            }
        }
    }

    /// <summary>
    /// M6 7 AZ: true when a creature that can attack is awake within the refresh's reach (any kind, not only the fight's targets).
    /// M6 16 Q1: the session's threat board answers (its awake query, the reach as the single the old scan narrowed it to, the later of two at one distance).
    /// </summary>
    private bool AwakeNear(IGameSessionView session, out string what)
    {
        what = string.Empty;
        if (_refreshReach <= 0)
        {
            return false;
        }

        var query = ThreatQuery.AwakeNear((float)_refreshReach) with { TiesToLast = true };
        var answer = ThreatBoard.For(session).Ask(session, in query, session.Clock.Now);
        if (answer.Kind != ThreatAnswerKind.Standing)
        {
            return false;
        }

        what = string.Create(CultureInfo.InvariantCulture, $"0x{answer.Guid:X8} is awake within {_refreshReach:0.#} m");
        return true;
    }

    private double _expiryWatchAt = double.NegativeInfinity;

    /// <summary>
    /// M6 14 upkeep mana: a lapse is counted and named whatever the fight is doing. <see cref="CountExpired"/> ran only at a decision (<see cref="RefreshStep"/>), so a line that ran out during a mana wait, a drain or a walk was named
    /// only at the next decision, or never if the fight ended first. Every <see cref="WalkRefreshCheckSeconds"/> outside the decision the lines are read and counted.
    /// </summary>
    private void ExpiryWatch(double clock)
    {
        if (!_refreshArmed || _refreshCycle is null || _phase == Phase.Choose || clock < _expiryWatchAt)
        {
            return;
        }

        _expiryWatchAt = clock + WalkRefreshCheckSeconds;
        var seeding = _refreshCycle.SeedPending;
        ReadRefreshLines(clock);
        CountExpired(clock, seeding);
    }

    /// <summary>The outcome <see cref="RefreshPassStep"/> notes for a pass that cast its line.</summary>
    private const string PassCastIt = "the pass cast it";

    /// <summary>
    /// Counts, once each, the lines seen active that are not active now: they ran out under fire (an error-level event naming the line and why). M6 14 upkeep mana (live 2026-11-14 02:14:35 UTC: the five lines' error lines named
    /// "the last recast try, 2702 s ago, ended: the pass cast it", the cast that had just run out, while every recast of the last 50 s had been refused for the way out's mana): the reason is the newest of the line's own hold and its
    /// last try that did not cast it; a cast is no reason; a line that was never reached names the hold of the line ahead of it.
    /// </summary>
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
                _lapsedAt.Remove(name); // recast: up again
            }
            else if (_refreshSeen.Remove(name))
            {
                BuffsExpired++;
                _lapsedAt[name] = clock;
                _log.Error(LogCategory, "buff refresh: " + name + " EXPIRED under fire: " + ExpiredWhy(name, clock));
            }
        }
    }

    private readonly Dictionary<string, double> _lapsedAt = new(StringComparer.Ordinal); // M6 14: the lines that ran out and are still down, with the clock they were read down

    /// <inheritdoc/>
    public (string Line, double DownSeconds)? LapsedDown(double clock)
    {
        (string Line, double DownSeconds)? longest = null;
        foreach (var (name, at) in _lapsedAt)
        {
            if (longest is not { } l || clock - at > l.DownSeconds)
            {
                longest = (name, clock - at);
            }
        }

        return longest;
    }

    /// <summary>M6 14 upkeep mana: why a line ran out (<see cref="CountExpired"/>).</summary>
    private string ExpiredWhy(string name, double clock)
    {
        var held = _refreshHeld.TryGetValue(name, out var hold);
        var tried = _refreshLastTry.TryGetValue(name, out var last);
        var failed = tried && !string.Equals(last.Outcome, PassCastIt, StringComparison.Ordinal);
        if (held && (!failed || hold.At >= last.At))
        {
            return string.Create(CultureInfo.InvariantCulture, $"its recast was held, last {clock - hold.At:0.#} s ago: {hold.Why}");
        }

        if (failed)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the last recast try, {clock - last.At:0} s ago, ended: {last.Outcome}");
        }

        var since = tried ? string.Create(CultureInfo.InvariantCulture, $" (its last cast {clock - last.At:0} s ago)") : string.Empty;
        return _refreshBlock.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"no recast of it was reached{since}: the refresh was held {clock - _refreshBlockAt:0.#} s ago: {_refreshBlock}")
            : "no recast was tried before it ran out" + since;
    }

    /// <summary>True while the health is under D88's drain or heal line (the floors that come before a refresh): the same rule the drain and the heal read.</summary>
    private bool HealthUnderFloor(IGameSessionView session, double clock) =>
        _drain.UnderPercent > 0 && session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) && _drain.Wanted(health, healthMax, clock);

    /// <summary>True when a hostile is in reach: a hit taken within <see cref="RefreshQuietSeconds"/>, or a living target creature of the fight within the reach.</summary>
    private bool RefreshHostileOwn(IGameSessionView session, double clock, out string what)
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
            if (StandsAt(session, o) is not { } p || (o.ItemType & WarCreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _killed.Contains(o.Guid))
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
