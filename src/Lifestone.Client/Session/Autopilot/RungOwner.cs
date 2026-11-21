using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// What is known of the buffs standing on her: a run's live reads. A run's start, a restart and a host have none: the ask carries null, and each line "up" then counts as not met, by name (but for what a host's
/// start reads standing in the state itself, <see cref="RungAsk.StartRead"/>).
/// </summary>
/// <param name="Active">The level of a line standing on her (<see cref="BuffCycle.ActiveLevel"/>), 0 when none.</param>
/// <param name="Reach">The highest level of a line the rule gives on the live skill (<see cref="BuffCycle.ReachLevel"/>).</param>
public sealed record RungBuffRead(Func<string, int> Active, Func<string, int> Reach);

/// <summary>Everything the rung choice reads. Every path builds one of these and asks <see cref="RungOwner.Decide"/>.</summary>
/// <param name="Level">The character's level; 0 or less when the state has none.</param>
/// <param name="Cell">The cell the body stands in, or null when unplaced.</param>
/// <param name="Qualities">The player's qualities (the skills trained).</param>
/// <param name="Knows">Whether the book holds a line at a level (<see cref="RungGateRule.BookKnows"/>).</param>
/// <param name="Buffs">The buffs standing, or null when unknown: unknown never opens a gate.</param>
/// <param name="NamedRung">The rung a stop named (<see cref="RungOwner.NamedRung"/>), or null. The run that made the stop read the buffs live, so the named rung's own gate is read for the book and the skills only.</param>
public sealed record RungAsk(int Level, uint? Cell, PlayerQualities Qualities, Func<string, int, bool> Knows, RungBuffRead? Buffs, string? NamedRung = null)
{
    /// <summary>
    /// M6 retreat: the live alternate ground the stop named after <see cref="NamedRung"/> (<see cref="RungOwner.NamedGround"/>), or null: the named rung is played there (a trophy job's ground), not on its own ground. Read only with
    /// <see cref="NamedRung"/>.
    /// </summary>
    public string? NamedGround { get; init; }

    /// <summary>
    /// M6 item 14 (the gates' second form): what stands in a learned line's place (<see cref="RungGateRule.Standing"/>'s <c>stands</c>), or null: a line that stands by the buff bot's stronger spell needs no scroll of her own
    /// while it stands. Set by a run whose stop at the bot is on; a host that has no run sets none (the book alone).
    /// </summary>
    public Func<string, int, string?>? StandsByAnother { get; init; }

    /// <summary>
    /// M6 item 14 (the start's rung): what a host's start reads standing on her in the state itself (<see cref="GateStartRead"/>: the buff bot's spells in her enchantment registry), or null: nothing is read, and
    /// unknown never opens a gate. Read only while <see cref="Buffs"/> is null and for a gate no stop named: a run's live read is the whole one. A rung picked through it is the start's pick
    /// (<c>start-pick:&lt;rung&gt;</c>): the lines it could not read are named (<see cref="RungGateRead.WayOutReads"/>), and the run's way out reads the whole gate before she leaves home.
    /// </summary>
    public GateStartRead? StartRead { get; init; }

    /// <summary>The ground of the run asking, or null for a host (no run yet). A run's own ground's gate is not read at a cycle's check or in a fight: it was read when she left for it, and <see cref="Leaving"/> reads it again each time she does.</summary>
    public string? RunGround { get; init; }

    /// <summary>True on the way out: she is leaving for <see cref="RunGround"/>, so its gate is read.</summary>
    public bool Leaving { get; init; }

    /// <summary>
    /// M6 14 gate lapse: true when the run asking is past its first check, so the gate that guards <see cref="RunGround"/> was read whole at a way out (D93) and a check that is not a way out reads no gate at or under it
    /// (<see cref="RungOwner.RunGuard"/>). False for the run's first check (a host built the run: its gates are read, so a wrong ground is caught, M6 7 AW) and for every ask without a run.
    /// </summary>
    public bool MidRun { get; init; }

    /// <summary>Whether a quest is done. Null: the host's provisional read: every quest counts as done, and the run's first check confirms it.</summary>
    public Func<string, bool>? QuestDone { get; init; }

    /// <summary>M6 7 AX: whether a trophy job is due by the quest's key: the quest timers say due, the trophy is not held, the session may still try. Null: a host (no timers before a run): no job; the run's first check asks.</summary>
    public Func<string, bool>? TrophyDue { get; init; }

    /// <summary>
    /// M6 item 14 (the trial switch): the run's trial (<see cref="AutopilotOptions.Trial"/>), or null. While it steers, its ground is chosen in place of the ladder's own rung (<see cref="RungOwner.Decide"/>); the choice only
    /// reads it: <see cref="LadderTrial.Note"/> moves it by <see cref="RungChoice.Trial"/>.
    /// </summary>
    public LadderTrial? Trial { get; init; }

    /// <summary>
    /// M6 14 (D135, the move-on ahead of the band): the plan's base War Magic at a level (<see cref="SpellPlan.PlanWarOf"/>, the projection the rungs' spell levels are computed on), or null: the plan cannot be read (no dats),
    /// so no rung is taken ahead of its band (<see cref="RungOwner.Ahead"/>).
    /// </summary>
    public Func<int, uint?>? PlanWar { get; init; }

    /// <summary>
    /// M6 14 (live 2026-11-14, a move-on loop: the caster spend raised her base War Magic to the plan's 290, the move-on took the Holding ahead of its band at a cycle check, the way out held it by its approach, and the restart took it
    /// ahead again): a rung the way out held by its approach this session (<see cref="ApproachHolds"/>) is never taken ahead of its band; null: none is held.
    /// </summary>
    public Func<string, bool>? AheadHeld { get; init; }

    /// <summary>
    /// M6 sprint (the owner's decision of 2026-11-18: the vitae burnt first, the corpse after): why a rung's key or a ground's key is held for the vitae burn after a death (<see cref="VitaeBurn.HeldWhy"/>), or null: none. Every check reads it, a way out
    /// too: a held rung is never chosen, and she hunts the rung below it (<c>vitae-held:&lt;rung&gt;</c>), a ground she has climbed through.
    /// </summary>
    public Func<string, string?>? VitaeHeld { get; init; }

    /// <summary>
    /// M6 holding at 60 (the owner, 2026-11-20: the only thing that should stop her is her level; <c>--autopilot-rung-holds=report</c>): true when the rungs' gates are read and reported (<see cref="RungChoice.Gates"/>) but hold
    /// nothing: the band decides. The caller leaves <see cref="Approach"/> and <see cref="VitaeHeld"/> unread for the same switch.
    /// </summary>
    public bool HoldsReportOnly { get; init; }

    /// <summary>
    /// M6 14 guard task: the kill task whose counter is armed (<see cref="LadderKillTask.Steers"/>), or null. While it steers, its kill step's ground is chosen in place of her rung's (<see cref="RungOwner.Decide"/>, after a trial); the choice
    /// only reads it.
    /// </summary>
    public LadderKillTask? KillTask { get; init; }

    /// <summary>
    /// M6 14 retreat attackers (rule 6): the approach check on her live state for a ground's key (<see cref="ApproachCheck.Live"/>), or null when the ground records no approach; the ask carries none for a host or a check that is not a way out.
    /// Read at the way out only (<see cref="Leaving"/>): a rung or a trial whose approach she cannot survive is held by name, and she hunts her own ground instead.
    /// </summary>
    public Func<string, ApproachVerdict?>? Approach { get; init; }

    /// <summary>
    /// M6 14 far below (live 2026-11-17 04:55:43, ladder52: level 79, every gate read shut, the walk dropped her to the outskirts for 93 minutes): her live level when <see cref="Level"/> was lowered by the choice itself (an approach
    /// hold or a session hold reads the rung before the held one at that rung's top), or null: <see cref="Level"/> is her live level. The far-below line reads this (<see cref="RungOwner.LiveOf"/>).
    /// </summary>
    public int? LiveLevel { get; init; }
}

/// <summary>M6 14 guard task: the kill task's part of a rung choice (<see cref="RungChoice.KillTask"/>).</summary>
/// <param name="Key">The quest's key.</param>
/// <param name="Ground">The kill step's ground.</param>
/// <param name="GateRung">The rung whose gate guards the ground, or null.</param>
/// <param name="Refused">The rule that refused the hunt (<c>&lt;rule&gt;: &lt;why&gt;</c>), or null.</param>
/// <param name="Gate">The gate as a way out to the ground read it, or null when this read made none.</param>
/// <param name="Steers">True when the choice is the task's: its ground in place of her rung's.</param>
public sealed record KillTaskRead(string Key, string Ground, string? GateRung, string? Refused, RungGateRead? Gate, bool Steers);

/// <summary>One gate the choice read, with what it found.</summary>
/// <param name="Gate">The gate.</param>
/// <param name="Conditions">Its conditions.</param>
public sealed record RungGateRead(RungGate Gate, IReadOnlyList<GateCondition> Conditions)
{
    /// <summary>True when every condition is met.</summary>
    public bool Open => Conditions.All(c => c.Met);

    /// <summary>
    /// M6 item 14 (the start's rung): the gate's lines whose "up" this read did not make and are no condition of it (<see cref="RungGateRule.LeftToTheWayOut"/>): a start cannot read them, and the run's way out does
    /// before she leaves home. Empty for every read but a start's with lines standing.
    /// </summary>
    public IReadOnlyList<string> WayOutReads { get; init; } = [];

    /// <summary>M6 14 (D135): true for the read of the gate of the rung after the band's (<see cref="RungOwner.Ahead"/>): shut, it holds her on her band's rung, never under it, so it is no <c>gate-held</c>.</summary>
    public bool Ahead { get; init; }
}

/// <summary>The one answer to "which rung": the rung with its ground, and the named reason.</summary>
/// <param name="Decision">The ladder's decision on the level the gates left.</param>
/// <param name="Ground">The ground to build a run on, or null when the ladder stops here (<see cref="Why"/> names it).</param>
/// <param name="HeldLevel">The level the rung was read at: the character's, or lower while a gate holds her.</param>
/// <param name="Reason">
/// The named reason: <c>band</c>, <c>gate-held:&lt;rung&gt;</c>, <c>gate-open:&lt;rung&gt;</c>, <c>start-pick:&lt;rung&gt;</c> (M6 item 14: a start read the gate open on what stands on her now, with lines left to the run's way
/// out), <c>stands-on:&lt;ground&gt;</c>, <c>stop-named:&lt;rung&gt;</c>, <c>quests-done</c>, <c>trial:&lt;ground&gt;</c> (M6 item 14: the trial switch's ground in place of the ladder's own rung, <see cref="LadderTrial"/>),
/// <c>ahead:&lt;rung&gt;</c> (M6 14, D135: the rung after the band's, before its band, <see cref="RungOwner.Ahead"/>), <c>kill-task:&lt;key&gt;</c> (M6 14 guard task: the kill step's ground in place of her rung's, <see cref="LadderKillTask"/>),
/// <c>approach-held:&lt;rung&gt;</c>, <c>approach-costly:&lt;rung&gt;</c> or <c>approach-abandoned:&lt;rung&gt;</c> (M6 14: a way out held the rung by its approach; M6 14 armory: its alternate or her own ground in its place, <see cref="Alternate"/>)
/// or <c>approach-session:&lt;rung&gt;</c> (M6 14 armory: a check that is not a way out, the rung held at a way out this session).
/// </param>
/// <param name="Why">The rung, or the stop's reason, in words.</param>
/// <param name="Gates">The gates read, in the order they were asked.</param>
public sealed record RungChoice(LadderDecision Decision, HuntingGround? Ground, int HeldLevel, string Reason, string Why, IReadOnlyList<RungGateRead> Gates)
{
    /// <summary>True when the chosen rung's own gate was read and is open: a run built below it moves on, which is no mismatch.</summary>
    public bool Opened => Decision.Rung is { } rung && Gates.Any(g => g.Gate.Rung == rung.Key && g.Open);

    /// <summary>
    /// M6 item 14 (the start's rung): the lines of the chosen rung's own gate that the read left to the run's way out (<see cref="RungGateRead.WayOutReads"/>), when that gate was read open; else empty. Not empty: the
    /// choice is a start's pick, checked by the way out before she leaves home.
    /// </summary>
    public IReadOnlyList<string> WayOutReads => Decision.Rung is { } rung && Gates.FirstOrDefault(g => g.Gate.Rung == rung.Key && g.Open) is { } gate ? gate.WayOutReads : [];

    /// <summary>M6 item 14 (the trial switch): the trial's part of the choice (<see cref="TrialRead"/>), or null when the ask carried no trial that steers.</summary>
    public TrialRead? Trial { get; init; }

    /// <summary>M6 14 guard task: the kill task's part of the choice (<see cref="KillTaskRead"/>), or null when the ask carried no kill task that steers.</summary>
    public KillTaskRead? KillTask { get; init; }

    /// <summary>
    /// M6 14 gate lapse: the rung of the first gate a check that is not a way out left unread because it guards the run's ground (<see cref="RungOwner.RunGuard"/>) while that ground is not the gate's own rung's (an alternate, a kill task's or a
    /// trial's ground, or a rung ahead of the band), or null. Its lines were read whole at the way out; mid-run they never hold her below the ground she runs on.
    /// </summary>
    public string? RunGuardUnread { get; init; }

    /// <summary>M6 14 retreat attackers (rule 6): the approach verdict that held a rung or a trial at the way out (<see cref="RungAsk.Approach"/>), or null when none did.</summary>
    public ApproachVerdict? ApproachHeld { get; init; }

    /// <summary>
    /// M6 14 armory: the alternate ground the choice took in place of a rung whose own ground is held by its approach (<see cref="RungOwner"/>: at the way out on her live verdicts, elsewhere by the session's holds), or null.
    /// The decision's rung is that rung with the alternate's ground and landblock.
    /// </summary>
    public string? Alternate { get; init; }

    /// <summary>M6 14 armory: the alternates a way out judged held by their approach, ground key and the verdict's kind (<c>approach-costly</c> and the like): the session holds them too (<see cref="ApproachHolds"/>).</summary>
    public IReadOnlyList<(string Ground, string Kind)> HeldAlternates { get; init; } = [];

    /// <summary>
    /// M6 14 armory: the keys a way out judged open by their approach (a rung whose own ground's verdict does not hold, an alternate ground taken on its verdict): a hold of this session on one of them is released
    /// (<see cref="ApproachHolds.Release"/>), so a rung whose crossing passes again is taken again.
    /// </summary>
    public IReadOnlyList<string> ApproachOpened { get; init; } = [];

    /// <summary>
    /// M6 14 far below: the loud line for a chosen rung whose band's top is under half her live level (<see cref="RungOwner.FarBelow"/>), or null. Report-only: it changes no choice (a gate walk held down past every shut gate, a trophy job, a stop's named rung, a
    /// corpse run); the callers log this as a warning, so it is never chosen silently.
    /// </summary>
    public string? FarBelow { get; init; }

    /// <summary>
    /// M6 redoubt: the entry portal's refusal by name (<c>gate:entry:&lt;ground&gt;:&lt;level&gt;&gt;&lt;max&gt;</c>) when the choice is on the rung's outdoor fallback ground because the rung's own ground's portal does not admit her
    /// (<see cref="RungOwner.EntryFallback"/>; the ground is <see cref="Alternate"/>), or null. The reason stays the ladder's own (a gate's hold, the vitae burn's): this says which ground, not why the rung.
    /// </summary>
    public string? EntryRefused { get; init; }

    /// <summary>The choice on one log line.</summary>
    /// <param name="path">Which path asked.</param>
    /// <param name="ask">Its ask.</param>
    public string Line(string path, RungAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return string.Create(CultureInfo.InvariantCulture, $"ladder: rung: asked by {path}: level={ask.Level} read_at={HeldLevel} cell=0x{ask.Cell ?? 0:X8} run_ground={ask.RunGround ?? "none"} leaving={(ask.Leaving ? "yes" : "no")} buffs={(ask.Buffs is not null ? "live" : ask.StartRead is not null ? "standing-now" : "unknown")} named={ask.NamedRung ?? "none"} -> rung={Decision.Rung?.Key ?? "none"} ground={Ground?.Key ?? "none"} reason={Reason}");
    }
}

/// <summary>M6 14 gate armour: the armour lines the shut gates want (<see cref="RungOwner.ArmourWant"/>).</summary>
/// <param name="Lines">Every armour line a shut gate holds unmet, by its profile name.</param>
/// <param name="Unlearned">Those whose <c>learned</c> is unmet at a level a scrivener sells: a town trip's scrolls answer them.</param>
/// <param name="Why">The line for the log.</param>
public sealed record GateArmourWant(IReadOnlyList<string> Lines, IReadOnlyList<string> Unlearned, string Why);

/// <summary>The owner's answer to a corpse run (<see cref="RungOwner.CorpseRun"/>).</summary>
/// <param name="Open">True when she may go to the corpse's ground.</param>
/// <param name="Reason">The named reason: <c>no-rung</c> (the corpse lies on no rung's ground), <c>gate-held:&lt;rung&gt;</c>, or the choice's own reason.</param>
/// <param name="Rung">The rung of the corpse's ground, or null.</param>
public readonly record struct CorpseRunAnswer(bool Open, string Reason, string? Rung);

/// <summary>
/// M6 7 AW: the one place that decides the rung. The Lodge's gate was passed three times on 2026-11-06, each by a path with its own read: a start on the book alone, a move-on by the level band, a gate that read level I only. Every path now
/// builds a <see cref="RungAsk"/> and takes the <see cref="RungChoice"/>: the host's start and move-on, the run's check at a cycle's start, the fight's read, and the way out. The band, the gates and the quest rungs stay where they were
/// (<see cref="Ladder"/>, <see cref="RungGateRule"/>, <see cref="AutopilotScenario"/>); only this class calls them to choose a rung. Those calls are internal, so the Godot host cannot reach them; a repository test
/// (<c>RungOwnerRepositoryTests</c>) fails any other caller in the client. Unknown never opens a gate. Pure.
/// M6 item 14 (the start's rung): a host's start may carry what the state itself shows standing on her (<see cref="RungAsk.StartRead"/>); the lines no start can read are then named on the read and left to the
/// run's way out, which reads the whole gate before she leaves home: unknown still never sends her to a gated ground.
/// M6 item 14 (the trial switch): an ask may carry a trial of a later rung's ground (<see cref="RungAsk.Trial"/>); its rules are this class's (<see cref="TrialRefusal"/>, <see cref="TrialGate"/>), and while it steers its ground is
/// the choice, <c>trial:&lt;ground&gt;</c>.
/// M6 14 (D135, the move-on ahead of the band): a band is the latest she moves; the rung after her band's is chosen before its band when its portals admit her, its gate reads open on what is known and she is at least the plan's
/// climber of its first level (<see cref="Ahead"/>, <c>ahead:&lt;rung&gt;</c>); a run on it stays while that holds, and its own gate is read at each way out.
/// </summary>
public static class RungOwner
{
    private const string MovedPrefix = "rung-moved:";

    /// <summary>The rung key a stop names (<c>rung-moved:&lt;rung&gt;</c>), when the ladder has that rung, else null.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="stop">The run's stop name.</param>
    public static string? NamedRung(Ladder ladder, string? stop)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        if (stop is null || !stop.StartsWith(MovedPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var key = stop[MovedPrefix.Length..];
        var at = key.IndexOf(GroundMark, StringComparison.Ordinal);
        key = at < 0 ? key : key[..at]; // M6 retreat: rung-moved:<rung>@<ground>, a rung played on one of its alternates
        return ladder.Rungs.Any(r => r.Key == key) ? key : null;
    }

    /// <summary>M6 retreat: the mark between a move's rung and the alternate ground it is played on (<c>rung-moved:&lt;rung&gt;@&lt;ground&gt;</c>, <see cref="NamedGround"/>).</summary>
    public const char GroundMark = '@';

    /// <summary>
    /// M6 retreat (the owner, 2026-11-19: the 10-14 rung hunts the Tusker Retreat, the Burrow its alternate, whose Female Tusk carriers the trophy job still pulls): the ground a move-on stop names after its rung
    /// (<c>rung-moved:&lt;rung&gt;@&lt;ground&gt;</c>), when it is a live alternate of that rung, else null (the rung's own ground). A trophy job on an alternate ground stops so (<see cref="AutopilotScenario.DecideOnGround"/>), and the
    /// host's restart, which has no quest timers, reads it here, so the next run is on the job's ground and not the rung's own.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="stop">The run's stop name.</param>
    public static string? NamedGround(Ladder ladder, string? stop)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        if (NamedRung(ladder, stop) is not { } key)
        {
            return null;
        }

        var at = stop!.IndexOf(GroundMark, StringComparison.Ordinal);
        var ground = at < 0 ? null : stop[(at + 1)..];
        return ground is not null && ladder.Rungs.Single(r => r.Key == key).Alternates.Any(a => !a.Pending && string.Equals(a.GroundKey, ground, StringComparison.Ordinal)) ? ground : null;
    }

    /// <summary>The rung played on <paramref name="ground"/>: the rung itself when it is its own ground, the rung with that alternate's ground and landblock when it is a live alternate of it, else null.</summary>
    private static LadderRung? PlayedOn(LadderRung rung, string ground)
    {
        if (string.Equals(rung.GroundKey, ground, StringComparison.Ordinal))
        {
            return rung;
        }

        return rung.Alternates.FirstOrDefault(a => !a.Pending && string.Equals(a.GroundKey, ground, StringComparison.Ordinal)) is { } alternate
            ? rung with { GroundKey = alternate.GroundKey, Landblock = alternate.Landblock, Pending = false, PendingReason = null }
            : null;
    }

    /// <summary>The rung and its named reason. See the class.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ask">What is known.</param>
    public static RungChoice Decide(Ladder ladder, RungAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(ask);
        var own = ApproachHold(ladder, ask, SessionHold(ladder, ask, DecideOwn(ladder, ask)));
        if (ask.Trial is { Steers: true } trial)
        {
            return Loud(ask, EntryFallback(ask, VitaeHold(ladder, ask, Trial(ladder, ask, trial, own)))); // an explicit test of a later rung's ground comes first: the kill task waits while it lasts
        }

        return Loud(ask, EntryFallback(ask, VitaeHold(ladder, ask, ask.KillTask is { Steers: true } task ? KillTask(ladder, ask, task, own) : own)));
    }

    /// <summary>
    /// M6 redoubt (the rung <c>outskirts</c> hunts the Holtburg Redoubt, whose portal admits levels 1 to 20): the last word of every choice. A hunt or a move on a rung's own ground whose entry portal does not admit her live level
    /// (<see cref="LiveOf"/>: a climber above the portal's limit, held down to the rung by shut gates or by the vitae burn) is made on the rung's outdoor fallback ground instead (<see cref="RungFallback"/>, an alternate of the rung
    /// hunted from home on foot): the rung's band, gate, bolt plan and target margin, the fallback's ground (<see cref="RungChoice.Alternate"/>), the refusal by name on the choice (<see cref="RungChoice.EntryRefused"/>, logged loudly by
    /// <see cref="WarnLoud"/>); the choice's reason stays the ladder's own (<c>gate-held:&lt;rung&gt;</c>, <c>vitae-held:&lt;rung&gt;</c>). A rung with no such ground keeps its choice,
    /// and the travel names the gate before any walk, as before. A choice already on an alternate, a trial's or a kill task's ground whose portal admits her, and a rung with no entry are untouched.
    /// </summary>
    private static RungChoice EntryFallback(RungAsk ask, RungChoice choice)
    {
        if (choice.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || choice.Alternate is not null || choice.Decision.Rung is not { GroundKey: { } own } rung
            || GroundCatalog.TryGet(own) is not { Entry: { } entry } || entry.Admits(LiveOf(ask)) || RungFallback.Of(rung) is not { } fallback)
        {
            return choice;
        }

        var live = LiveOf(ask);
        var refusal = Scenarios.Journey.RungRoute.GateRefusal("entry:" + own, live, entry.MinLevel, entry.MaxLevel) ?? "entry:" + own;
        var onFallback = rung with { GroundKey = fallback.GroundKey, Landblock = fallback.Landblock, Pending = false, PendingReason = null };
        var decision = AutopilotScenario.DecideEntryFallback(onFallback, choice.Decision.Level, live, ask.RunGround ?? fallback.GroundKey, refusal);
        var built = decision.Kind == LadderStepKind.Hunt ? GroundCatalog.TryGet(fallback.GroundKey) : null;
        return choice with
        {
            Decision = decision,
            Ground = built,
            Alternate = fallback.GroundKey,
            EntryRefused = refusal,
            Why = $"the rung {rung.Key}'s own ground {own} is entered by a portal that does not admit level {live} ({refusal}), so she hunts its outdoor ground {fallback.GroundKey} in its place; {decision.Why}",
        };
    }

    /// <summary>
    /// M6 sprint (the owner, 2026-11-18): the vitae burn's hold (<see cref="RungAsk.VitaeHeld"/>), over every other answer. While the chosen rung, or the ground chosen for it, is held, the choice is the ladder's own at the level under that
    /// rung's band (no alternate of the same rung: the burn is on a ground she has climbed through), again while that one is held. The first rung (from level 1) is never held. Named <c>vitae-held:&lt;rung&gt;</c>.
    /// </summary>
    private static RungChoice VitaeHold(Ladder ladder, RungAsk ask, RungChoice choice)
    {
        if (ask.VitaeHeld is not { } held)
        {
            return choice;
        }

        LadderRung? heldRung = null;
        string? why = null;
        for (var i = 0; i < ladder.Rungs.Count; i++)
        {
            if (choice.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || choice.Decision.Rung is not { } rung || rung.Band.From <= 1)
            {
                break;
            }

            var reason = held(rung.Key) ?? (choice.Ground?.Key is { } ground ? held(ground) : null) ?? (rung.GroundKey is { } own ? held(own) : null);
            if (reason is null)
            {
                break;
            }

            (heldRung, why) = (rung, reason);
            choice = DecideOwn(ladder, ask with { Level = Math.Min(ask.Level, rung.Band.From - 1), LiveLevel = LiveOf(ask), NamedRung = null, PlanWar = null, Trial = null, KillTask = null });
        }

        return heldRung is null
            ? choice
            : choice with { Reason = "vitae-held:" + heldRung.Key, Why = $"the rung {heldRung.Key} is held ({why}): she hunts the rung below until the vitae is gone or the corpse's rot is near; {choice.Why}" };
    }

    /// <summary>M6 14 far below: her live level (<see cref="RungAsk.LiveLevel"/>, else <see cref="RungAsk.Level"/>).</summary>
    /// <param name="ask">The ask.</param>
    public static int LiveOf(RungAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return ask.LiveLevel ?? ask.Level;
    }

    /// <summary>
    /// M6 14 far below (the lead's rule of 2026-11-17: a rung whose band's top is under half her level is far below her): true when <paramref name="rung"/>'s band ends under half of <paramref name="level"/>. A rung so far below her pays
    /// a fraction of her band's experience (ladder52: 3 kills in 93 minutes on the outskirts at 79) and is never chosen without a loud, named line (<see cref="RungChoice.FarBelow"/>, read at her live level).
    /// </summary>
    /// <param name="rung">The rung.</param>
    /// <param name="level">The level it is measured against.</param>
    public static bool FarBelow(LadderRung rung, int level)
    {
        ArgumentNullException.ThrowIfNull(rung);
        return level > 0 && rung.Band.To * 2 < level;
    }

    /// <summary>
    /// M6 14 gate armour (live 2026-11-17 04:55:43, ladder52: every gate from the Holding's to the Burrow's read shut on Armor Self and Bludgeon Protection alone, lines her book lacked and the bot's short round never cast): what the
    /// gates the choice read shut want, when every condition they hold unmet is an armour line's (its <c>learned</c> or its <c>up</c>), else null (a skill untrained, a mastery down: no scroll or bot round of an armour line opens them).
    /// </summary>
    /// <param name="choice">The rung choice (its <see cref="RungChoice.Gates"/>; the gate ahead of the band is no hold and is not read).</param>
    /// <param name="isArmour">Whether a gate line (its profile name) is an armour line (<c>EverySpellPlan.GateArmourLines</c>).</param>
    /// <param name="sold">Whether a scrivener of the catalog sells a gate line at a level or higher (level VII is the loot's, D4: no trip buys it).</param>
    /// <param name="botCasts">Whether the buff bot casts a gate line.</param>
    public static GateArmourWant? ArmourWant(RungChoice choice, Func<string, bool> isArmour, Func<string, int, bool> sold, Func<string, bool> botCasts)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(isArmour);
        ArgumentNullException.ThrowIfNull(sold);
        ArgumentNullException.ThrowIfNull(botCasts);
        var shut = choice.Gates.Where(g => !g.Open && !g.Ahead).ToList();
        if (shut.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        var unlearned = new List<string>();
        foreach (var read in shut)
        {
            foreach (var condition in read.Conditions.Where(c => !c.Met))
            {
                var learned = read.Gate.Lines.FirstOrDefault(l => condition.Name == string.Create(CultureInfo.InvariantCulture, $"{l.Line} {ScrivenerLearnPlan.Numeral(l.Level)} learned"));
                var up = read.Gate.Lines.FirstOrDefault(l => condition.Name == l.Line + " up");
                if ((learned ?? up) is not { } line || !isArmour(line.Line))
                {
                    return null; // a condition no armour line answers
                }

                if (!lines.Contains(line.Line))
                {
                    lines.Add(line.Line);
                }

                if (learned is not null && sold(learned.Line, learned.Level) && !unlearned.Contains(learned.Line))
                {
                    unlearned.Add(learned.Line);
                }
            }
        }

        var how = string.Join("; ", lines.Select(l => $"{l}: {(sold(l, 1) ? "a scrivener sells it" : "no scrivener sells it")}{(botCasts(l) ? ", the buff bot casts it" : string.Empty)}"));
        return new GateArmourWant(lines, unlearned, $"the gates read shut ({string.Join(", ", shut.Select(g => g.Gate.Rung))}) only for want of armour lines she can buy or have the bot cast: {how}");
    }

    /// <summary>
    /// M6 14 gate armour: <see cref="ArmourWant"/> on the catalog's facts: an armour line is one the every-spell rule's gate tier holds (<see cref="EverySpellPlan.GateArmourLines"/>), sold when a scrivener of the catalog sells a level of
    /// it (<see cref="ScrivenerCatalog.Default"/>), the bot's when the bot's word casts it (<c>BuffBotWire.LineNamed</c>).
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="choice">The way out's choice.</param>
    public static GateArmourWant? ArmourWantOf(Ladder ladder, RungChoice choice)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        var catalog = ScrivenerCatalog.Default;
        var armour = EverySpellPlan.GateArmourLines(catalog, ladder).ToHashSet(StringComparer.Ordinal);
        return ArmourWant(
            choice,
            line => armour.Contains(EverySpellPlan.GateLineStockName(catalog, line)),
            (line, level) => catalog.Stock.LineSpells(EverySpellPlan.GateLineStockName(catalog, line)).Any(s => s.Level >= level),
            line => Lifestone.ServerTools.BuffBotWire.LineNamed(line) is not null);
    }

    /// <summary>M6 14 far below: logs the choice's far-below line (<see cref="RungChoice.FarBelow"/>) as a warning (report-only: it changes no choice); nothing when it has none.</summary>
    /// <param name="choice">The choice.</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="category">The log category.</param>
    public static void WarnLoud(RungChoice choice, ILogSink? log, string category)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (log is null)
        {
            return;
        }

        if (choice.FarBelow is { } far)
        {
            log.Warn(category, far);
        }

        if (choice.EntryRefused is { } refused && choice.Decision.Rung is { } rung)
        {
            log.Warn(category, $"ladder: rung: entry refused: {refused}: the rung {rung.Key}'s own ground is entered by a portal that does not admit her, so she hunts its outdoor ground {choice.Alternate} in its place (reason={choice.Reason}; M6 redoubt)");
        }
    }

    /// <summary>M6 14 far below: <paramref name="choice"/> with its far-below line (<see cref="RungChoice.FarBelow"/>) when its rung's band ends under half her live level.</summary>
    private static RungChoice Loud(RungAsk ask, RungChoice choice)
    {
        var live = LiveOf(ask);
        if (choice.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || choice.Decision.Rung is not { Kind: LadderRungKind.Ground } rung || !FarBelow(rung, live))
        {
            return choice;
        }

        return choice with
        {
            FarBelow = string.Create(
                CultureInfo.InvariantCulture,
                $"ladder: rung: far below her level: rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) at level {live}: its band's top {rung.Band.To} is under half her level; chosen by reason={choice.Reason} on {choice.Decision.Rung.GroundKey ?? "no ground"}: {choice.Why}"),
        };
    }

    /// <summary>
    /// M6 14 launch holds (live 2026-11-17, ladder52: launched with <c>--autopilot-hold-grounds=tusker-holding,tusker-armory</c>, the start picked the Holding and only the way out held it): whether <paramref name="rung"/> is held this
    /// session: by its key (a way out's approach hold) or by its ground's key (a crossing given up, or a ground held at launch, <see cref="ApproachHolds.IsHeld"/>).
    /// </summary>
    private static bool Held(RungAsk ask, LadderRung rung) => ask.AheadHeld is { } isHeld && (isHeld(rung.Key) || (rung.GroundKey is { } ground && isHeld(ground)));

    /// <summary>
    /// M6 14 guard task (the owner, 2026-11-13: when the task is open and she can enter its ground, it takes precedence over her rung's ground): the kill task's choice over the ladder's own (<paramref name="own"/>). The ladder's own stops
    /// stand (a level unread, the top, a quest or a pending rung): the task changes which ground, never whether there is one. Its rules are read first (<see cref="KillTaskRefusal"/>); a way out to its ground reads its gate whole
    /// (<see cref="KillTaskGate"/>), live as every way out reads a gate: shut, the choice is the ladder's own with that read added and the refusal named. Else the task's ground: hunt there, or move there from a run on another ground
    /// (<c>rung-moved:&lt;rung&gt;</c>, ok); the reason is <c>kill-task:&lt;key&gt;</c>. The decision's rung is her own rung (the band's: its bolt plan and target margin are hers) with the task's ground and landblock: only the ground changes.
    /// </summary>
    private static RungChoice KillTask(Ladder ladder, RungAsk ask, LadderKillTask task, RungChoice own)
    {
        if (own.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || own.Decision.Rung is not { } ownRung)
        {
            return own;
        }

        if (KillTaskRefusal(ladder, task.Ground, ask.Level, out var gate) is { } refused)
        {
            return own with { KillTask = new KillTaskRead(task.Key, task.Ground, gate?.Rung, refused, null, false) };
        }

        var gates = new List<RungGateRead>(own.Gates);
        RungGateRead? gateRead = null;
        if (ask.Leaving && string.Equals(ask.RunGround, task.Ground, StringComparison.Ordinal))
        {
            var conditions = ask.Buffs is { } buffs
                ? RungGateRule.Check(gate!, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother)
                : RungGateRule.AtStart(gate!, ask.Qualities, ask.Knows, ask.StandsByAnother);
            gateRead = new RungGateRead(gate!, conditions);
            gates.Add(gateRead);
            if (!gateRead.Open)
            {
                var shut = string.Join("; ", conditions.Where(c => !c.Met).Select(c => $"{c.Name} ({c.Read})"));
                return own with { Gates = gates, KillTask = new KillTaskRead(task.Key, task.Ground, gate!.Rung, $"gate: the {gate.Rung} gate's lines are read shut at the way out to {task.Ground}: {shut}", gateRead, false) };
            }
        }

        var landblock = ladder.Rungs.FirstOrDefault(r => string.Equals(r.GroundKey, task.Ground, StringComparison.Ordinal))?.Landblock
            ?? ladder.Rungs.SelectMany(r => r.Alternates).FirstOrDefault(a => string.Equals(a.GroundKey, task.Ground, StringComparison.Ordinal))?.Landblock;
        var taskRung = ownRung with { GroundKey = task.Ground, Landblock = landblock, Pending = false, PendingReason = null };
        var why = string.Create(
            CultureInfo.InvariantCulture,
            $"the kill task {task.Key} (counter {(task.Counter is { } n ? n.ToString(CultureInfo.InvariantCulture) : "?")}/{task.Count}, read from the {task.ReadFrom}) hunts {task.Ground} in place of her rung {ownRung.Key}'s ground {ownRung.GroundKey} at level {ask.Level}");
        var decision = ask.RunGround is { } run && !string.Equals(run, task.Ground, StringComparison.Ordinal)
            ? new LadderDecision(LadderStepKind.Moved, taskRung, ask.Level, "rung-moved:" + taskRung.Key, true, $"{why}: the run's is {run}: the next run is on {task.Ground}")
            : new LadderDecision(LadderStepKind.Hunt, taskRung, ask.Level, null, true, $"{why} on {task.Ground}");
        var built = decision.Kind == LadderStepKind.Hunt ? GroundCatalog.TryGet(task.Ground) : null;
        return new RungChoice(decision, built, ask.Level, "kill-task:" + task.Key, decision.Why, gates) { KillTask = new KillTaskRead(task.Key, task.Ground, gate!.Rung, null, gateRead, true), RunGuardUnread = own.RunGuardUnread };
    }

    /// <summary>
    /// M6 14 guard task: why the kill task's hunt on <paramref name="ground"/> at <paramref name="level"/> is refused, by its rule's name (<c>&lt;rule&gt;: &lt;why&gt;</c>), or null when she can enter it, read in order: <c>level</c>: the level is on
    /// the ladder; <c>unknown</c>: the ground has a profile; <c>portal</c>: its entry portal's level range admits her; <c>unfightable</c>: the fight can hunt it (<see cref="AutopilotScenario.GroundRefusal"/>); <c>gate</c>: a gate guards it
    /// (<see cref="KillTaskGate"/>, returned in <paramref name="gate"/>), read whole, live, at the way out to the ground, as every gate is.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ground">The kill step's ground key.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="gate">The gate of the ground, or null.</param>
    public static string? KillTaskRefusal(Ladder ladder, string ground, int level, out RungGate? gate)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        gate = KillTaskGate(ladder, ground);
        if (level < 1 || level > ladder.TopLevel)
        {
            return string.Create(CultureInfo.InvariantCulture, $"level: the level {level} is not on the ladder (1 to {ladder.TopLevel}): her rung cannot be read");
        }

        if (GroundCatalog.TryGet(ground) is not { } profile)
        {
            return $"unknown: {ground} has no ground profile";
        }

        if (profile.Entry is not { } entry)
        {
            return $"portal: {ground}'s profile has no entry portal, so the level range it admits cannot be read";
        }

        if (!entry.Admits(level))
        {
            var range = entry.MaxLevel is { } max ? string.Create(CultureInfo.InvariantCulture, $"{entry.MinLevel ?? 1} to {max}") : string.Create(CultureInfo.InvariantCulture, $"{entry.MinLevel ?? 1} and up");
            return string.Create(CultureInfo.InvariantCulture, $"portal: the entry portal {entry.Wcid} of {ground} admits levels {range}; level {level} is not admitted");
        }

        if (AutopilotScenario.GroundRefusal(ground) is { } unfightable)
        {
            return "unfightable: " + unfightable;
        }

        return gate is null ? $"gate: TODO(m6-kill-task-gate): no rung of the ladder names {ground} as its ground or an alternate, so no gate guards it and the hunt would go ungated" : null;
    }

    /// <summary>
    /// M6 14 guard task: the gate a kill task's ground must pass at the way out: the gate of the rung whose ground or alternate it is (the first in climbing order), or, for a rung with none, the last gate before it (<see cref="TrialGate"/>:
    /// the Tusker Barracks is an alternate of the Quarters, so the Quarters' gate since D135 gave every rung from the Burrow up one), for the buff bot's protection. Null when no rung names the ground.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ground">The ground's key.</param>
    public static RungGate? KillTaskGate(Ladder ladder, string ground)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        var rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.GroundKey, ground, StringComparison.Ordinal) || r.Alternates.Any(a => string.Equals(a.GroundKey, ground, StringComparison.Ordinal)));
        return rung is null ? null : TrialGate(ladder, rung);
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 6; live 2026-11-14: the climber died on the Holding's approach): the ladder's way out reads the approach of the ground it chose (<see cref="RungAsk.Approach"/>, her live state against the approach's facts).
    /// A ground whose approach she cannot survive is held by name: the choice is made again from the level before its rung's band, with no rung taken ahead (the plan is not read), until a rung's approach holds or none is recorded; the
    /// reason is <c>approach-held:&lt;rung&gt;</c>. Not a way out, or no reader: the choice stands. M6 14 approach cost: a crossing that would force a recall before the portal or cost more time than the rung's share of a cycle holds it the
    /// same way (<c>approach-costly:&lt;rung&gt;</c>, <see cref="ApproachCost.Judge"/>), and so does a crossing the run leg gave up earlier in the run (<c>approach-abandoned:&lt;rung&gt;</c>, <see cref="ApproachCost.Measured"/>): she hunts the
    /// previous rung's ground, as a gate-held rung does.
    /// M6 14 armory (the owner, 2026-11-14: try the Tusker Armory as the climber's next ground): a rung of her band (not one taken ahead of it) held this way first tries its alternates in their order (<see cref="TakeAlternate"/>): the first
    /// whose own approach is recorded, judged here on her live state and does not hold, and whose portal and route admit her, is hunted in the rung's place (the rung's band, gate, bolt plan and target margin, the alternate's ground,
    /// <see cref="RungChoice.Alternate"/>); an alternate with no approach recorded is never taken so (unknown never sends her). The alternates judged held are named (<see cref="RungChoice.HeldAlternates"/>) and so are the keys
    /// judged open (<see cref="RungChoice.ApproachOpened"/>), for the session's holds. With none open she hunts the previous rung's ground, as before.
    /// </summary>
    private static RungChoice ApproachHold(Ladder ladder, RungAsk ask, RungChoice own)
    {
        if (!ask.Leaving || ask.Approach is not { } judge)
        {
            return own;
        }

        var choice = own;
        ApproachVerdict? held = null;
        LadderRung? heldRung = null;
        var heldAlternates = new List<(string Ground, string Kind)>();
        var opened = new List<string>();
        var tried = new List<string>();
        string? alternate = null;
        for (var i = 0; i < ladder.Rungs.Count; i++)
        {
            if (choice.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || choice.Decision.Rung is not { } rung || rung.GroundKey is not { } key || rung.Band.From <= 1)
            {
                break;
            }

            var read = judge(key);
            if (read is not { Holds: true } verdict)
            {
                if (read is not null)
                {
                    opened.Add(rung.Key); // judged open: a hold of this session on the rung is released
                }

                break;
            }

            (held, heldRung) = (verdict, rung);
            if (rung.Band.From <= ask.Level && TakeAlternate(ask, choice, rung, judge, heldAlternates, tried) is { } taken)
            {
                choice = taken;
                alternate = taken.Alternate;
                opened.Add(alternate!);
                break;
            }

            choice = DecideOwn(ladder, ask with { Level = Math.Min(ask.Level, rung.Band.From - 1), LiveLevel = LiveOf(ask), NamedRung = null, PlanWar = null });
        }

        if (held is null)
        {
            return opened.Count == 0 ? own : own with { ApproachOpened = opened };
        }

        var alternates = tried.Count == 0 ? string.Empty : $"; its alternates: {string.Join("; ", tried)}";
        var instead = alternate is null ? "she hunts her own ground instead" : $"she hunts its alternate {alternate} in its place";
        return choice with
        {
            Reason = held.Kind + ":" + heldRung!.Key,
            Why = $"{held.Held}: the rung {heldRung.Key} is held and {instead}{alternates}; {choice.Why}",
            ApproachHeld = held,
            HeldAlternates = heldAlternates,
            ApproachOpened = opened,
        };
    }

    /// <summary>
    /// M6 14 armory (the loop the session's holds end): a check that is not a way out reads no approach, so a rung of her band that a way out held by its approach this session (<see cref="RungAsk.AheadHeld"/>) would be chosen again,
    /// the run stopped to move there and the way out held it again, between two grounds without end. Here the choice is the way out's own answer without reading the approach: the rung's first alternate that is not held this session
    /// and that <see cref="AlternateRefusal"/> passes (the reason <c>approach-session:&lt;rung&gt;</c>), else the previous rung's ground, as a gate-held rung. The next way out reads every verdict again on her live state.
    /// </summary>
    private static RungChoice SessionHold(Ladder ladder, RungAsk ask, RungChoice own)
    {
        if (ask.Leaving || ask.AheadHeld is not { } isHeld)
        {
            return own;
        }

        var choice = own;
        LadderRung? heldRung = null;
        var tried = new List<string>();
        for (var i = 0; i < ladder.Rungs.Count; i++)
        {
            if (choice.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved) || choice.Decision.Rung is not { } rung || rung.Band.From <= 1 || !Held(ask, rung) || choice.Alternate is not null)
            {
                break;
            }

            heldRung = rung;
            if (rung.Band.From <= ask.Level && TakeAlternate(ask, choice, rung, null, null, tried) is { } taken)
            {
                choice = taken;
                break;
            }

            choice = DecideOwn(ladder, ask with { Level = Math.Min(ask.Level, rung.Band.From - 1), LiveLevel = LiveOf(ask), NamedRung = null, PlanWar = null });
        }

        if (heldRung is null)
        {
            return own;
        }

        var instead = choice.Alternate is { } alternate ? $"its alternate {alternate} in its place" : "her own ground instead";
        var alternates = tried.Count == 0 ? string.Empty : $"; its alternates: {string.Join("; ", tried)}";
        return choice with { Reason = "approach-session:" + heldRung.Key, Why = $"the rung {heldRung.Key} was held by its approach at a way out this session, so she hunts {instead} until a way out judges it again{alternates}; {choice.Why}" };
    }

    /// <summary>
    /// M6 14 armory: the choice of the first alternate of <paramref name="rung"/> that may stand in its place, or null. Each is read in order: <see cref="AlternateRefusal"/>; then, with <paramref name="judge"/> (the way out), its approach
    /// on her live state: no verdict (no approach recorded) or one that holds refuses it, and a held one is added to <paramref name="heldAlternates"/>; without (a check that is not a way out), a ground held this session refuses it.
    /// Each refusal is added to <paramref name="tried"/>. The choice keeps <paramref name="own"/>'s gate reads and level: the gate is the rung's.
    /// </summary>
    private static RungChoice? TakeAlternate(RungAsk ask, RungChoice own, LadderRung rung, Func<string, ApproachVerdict?>? judge, List<(string Ground, string Kind)>? heldAlternates, List<string> tried)
    {
        foreach (var a in rung.Alternates)
        {
            if (AlternateRefusal(a, ask.Level) is { } refused)
            {
                tried.Add($"{a.GroundKey} {refused}");
                continue;
            }

            if (judge is not null)
            {
                if (judge(a.GroundKey) is not { } verdict)
                {
                    tried.Add($"{a.GroundKey} not judged (no approach read)");
                    continue;
                }

                if (verdict.Holds)
                {
                    tried.Add($"{a.GroundKey} {verdict.Held}");
                    heldAlternates?.Add((a.GroundKey, verdict.Kind));
                    continue;
                }
            }
            else if (ask.AheadHeld?.Invoke(a.GroundKey) == true)
            {
                tried.Add($"{a.GroundKey} held by its approach this session");
                continue;
            }

            var alternateRung = rung with { GroundKey = a.GroundKey, Landblock = a.Landblock, Pending = false, PendingReason = null };
            var decision = AutopilotScenario.DecideAlternate(alternateRung, ask.Level, ask.RunGround ?? a.GroundKey);
            var built = decision.Kind == LadderStepKind.Hunt ? GroundCatalog.TryGet(a.GroundKey) : null;
            return new RungChoice(decision, built, own.HeldLevel, own.Reason, decision.Why, own.Gates) { Alternate = a.GroundKey, RunGuardUnread = own.RunGuardUnread };
        }

        return null;
    }

    /// <summary>
    /// M6 14 armory: why <paramref name="alternate"/> may not stand in its held rung's place at <paramref name="level"/>, or null, read in order: <c>pending</c>; <c>unknown</c>: it has a profile; <c>no-approach</c>: the profile records
    /// its approach (a ground whose crossing is not recorded is never chosen by a crossing's verdict: unknown never sends her); <c>unfightable</c> (<see cref="AutopilotScenario.GroundRefusal"/>); <c>portal</c>: its entry portal and a
    /// route of the travel file admit her (<see cref="TravelRoutes.FirstAdmitted"/>).
    /// </summary>
    /// <param name="alternate">The alternate.</param>
    /// <param name="level">The character's level.</param>
    public static string? AlternateRefusal(LadderAlternate alternate, int level)
    {
        ArgumentNullException.ThrowIfNull(alternate);
        if (alternate.Pending)
        {
            return "pending: " + (alternate.PendingReason ?? "no profile yet");
        }

        if (GroundCatalog.TryGet(alternate.GroundKey) is not { } ground)
        {
            return "unknown: no ground profile";
        }

        if (ground.Approach is null)
        {
            return "no-approach: its profile records no approach, so no crossing's verdict can send her there";
        }

        if (AutopilotScenario.GroundRefusal(alternate.GroundKey) is { } unfightable)
        {
            return "unfightable: " + unfightable;
        }

        return ground.Entry is not { } entry || !entry.Admits(level) || TravelRoutes.Default.FirstAdmitted(alternate.GroundKey, level, entry.MinLevel, entry.MaxLevel) is not { Outcome: TravelOutcome.Admitted }
            ? string.Create(CultureInfo.InvariantCulture, $"portal: no entry portal and route admit level {level}")
            : null;
    }

    /// <summary>The ladder's own choice: the band, the gates, the quest rungs, the trophy jobs and the rung ahead of the band (everything but a trial).</summary>
    private static RungChoice DecideOwn(Ladder ladder, RungAsk ask)
    {
        var read = new List<RungGateRead>();
        string? standing = null;

        // M6 14 (D135): the rung after the band's that the move-on may take before its band, by the facts and her strength (its gate is read below)
        var ahead = Ahead(ladder, ask.Level, ask.Qualities, ask.PlanWar, out _);
        if (ahead is not null && Held(ask, ahead))
        {
            ahead = null; // M6 14: its approach held it at a way out this session (approach-held, approach-costly or approach-abandoned): not taken ahead of its band
        }

        // A rung above her level is no rung she can be sent to, whoever named it, but the rung ahead of her band
        var named = ask.NamedRung is { } key && ladder.Rungs.FirstOrDefault(r => r.Key == key) is { } rung && (rung.Band.From <= ask.Level || rung.Key == ahead?.Key) ? rung : null;
        if (named is not null && named.Key == ahead?.Key && named.Band.From > ask.Level && ladder.GateOf(named.Key) is { } namedGate)
        {
            // the stop that named it read its gate live (the move-on ahead): here it is read for the book and the skills, as every named rung's
            var conditions = RungGateRule.Standing(namedGate, ask.Qualities, ask.Knows, ask.StandsByAnother);
            read.Add(new RungGateRead(namedGate, conditions) { Ahead = true });
            if (conditions.All(c => c.Met))
            {
                var aheadNamed = AutopilotScenario.DecideAhead(named, ask.Level, ask.RunGround ?? named.GroundKey ?? string.Empty);
                var builtNamed = aheadNamed.Kind == LadderStepKind.Hunt && named.GroundKey is { } namedGround ? GroundCatalog.TryGet(namedGround) : null;
                return new RungChoice(aheadNamed, builtNamed, ask.Level, "stop-named:" + named.Key, aheadNamed.Why, read);
            }

            named = null; // its book or skills fall short: the band and its gates decide
        }

        var from = named is null ? ask.Level : Math.Min(ask.Level, named.Band.To);
        var job = named is null ? TrophyJob(ladder, ask) : null; // M6 7 AX: a due trophy is not skipped by level: the level the gates read is the job's rung, whose own gate must be open
        from = job?.Rung.Band.To ?? from;
        var guard = ask.RunGround is { } guarded && !ask.Leaving && ask.MidRun ? RunGuard(ladder, guarded) : -1;
        string? guardUnread = null;
        var held = RungGateRule.Walk(ladder, from, gate =>
        {
            var groundKey = ladder.Rungs.Single(r => r.Key == gate.Rung).GroundKey;
            if (ask.RunGround is { } run && !ask.Leaving && string.Equals(run, groundKey, StringComparison.Ordinal))
            {
                standing = run;
                return null;
            }

            if (guard >= 0 && IndexOf(ladder, gate.Rung) <= guard)
            {
                // M6 14 gate lapse (live, 2026-11-13 23:49:28 UTC: on the Tusker Barracks for the kill task, one lapsed Bludgeon Bane read the Quarters', Abode's, Cave's, Lodge's and Burrow's gates shut at a cycle's check and held her
                // on the outskirts' rung, five rungs down, on the ground she stood on): the gate that guards the run's ground, and every gate under it, was read whole at the way out (D93) and is not read again until she leaves home;
                // mid-run a gate holds her up, never below the ground she runs on. The run's own rung's ground keeps its own rule above (the same, by its key).
                guardUnread ??= gate.Rung;
                return null;
            }

            if (ask.Cell is { } cell && groundKey is not null && GroundCatalog.TryGet(groundKey) is { } ground && ground.InLandblocks(cell))
            {
                standing = groundKey;
                return null; // she stands on it: she is never stranded outside it
            }

            // M6 item 14: a start with lines standing in the state reads them (GateStartRead); the lines it cannot read are named on the read, never counted met
            var start = gate.Rung != named?.Key && ask.Buffs is null ? ask.StartRead : null;
            var conditions = gate.Rung == named?.Key ? RungGateRule.Standing(gate, ask.Qualities, ask.Knows, ask.StandsByAnother)
                : ask.Buffs is { } buffs ? RungGateRule.Check(gate, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother)
                : start is not null ? RungGateRule.AtStart(gate, ask.Qualities, ask.Knows, start)
                : RungGateRule.AtStart(gate, ask.Qualities, ask.Knows, ask.StandsByAnother);
            read.Add(new RungGateRead(gate, conditions) { WayOutReads = start is null ? [] : RungGateRule.LeftToTheWayOut(gate, start) });
            return ask.HoldsReportOnly ? null : conditions; // M6 holding at 60: read and reported, never a hold
        });

        var decision = ask.RunGround is { } ground2
            ? AutopilotScenario.DecideRung(ladder, held, ground2, null, ask.QuestDone)
            : AutopilotScenario.LadderDecisionFor(ladder, held, ask.QuestDone ?? (static _ => true));

        // M6 retreat: a rung played on a live alternate of its own: the trophy job's ground (a run's check) or the ground the stop named after the rung (the host's restart of that move)
        var playedOn = job is { } onJob && held == from ? onJob.Rung
            : named is not null && ask.NamedGround is { } askedGround ? PlayedOn(named, askedGround)
            : null;
        if (playedOn is not null && decision.Rung?.Key == playedOn.Key && !string.Equals(decision.Rung.GroundKey, playedOn.GroundKey, StringComparison.Ordinal))
        {
            decision = AutopilotScenario.DecideOnGround(playedOn, held, ask.RunGround ?? playedOn.GroundKey!);
        }

        if (ahead is not null && named is null && job is null && held == ask.Level && decision.Kind is LadderStepKind.Hunt or LadderStepKind.Moved
            && decision.Rung?.Key == ladder.RungFor(ask.Level).Key && AheadGate(ladder, ask, ahead, read, ref standing))
        {
            decision = AutopilotScenario.DecideAhead(ahead, ask.Level, ask.RunGround ?? ahead.GroundKey ?? string.Empty);
        }

        var built = decision.Kind == LadderStepKind.Hunt && decision.Rung?.GroundKey is { } groundKey2 ? GroundCatalog.TryGet(groundKey2) : null;
        var why = ask.RunGround is null ? BandWhy(ladder, held, decision, built) : decision.Why;
        var choice = new RungChoice(decision, built, held, string.Empty, why, read) { RunGuardUnread = guardUnread };
        return choice with { Reason = job is { } j && held == from ? "job:" + j.Stand.Quest : ReasonOf(choice, named, standing) };
    }

    /// <summary>
    /// M6 14 gate lapse: the ladder index of the rung whose gate guards <paramref name="ground"/> (<see cref="KillTaskGate"/>: the gate of the first rung naming it as its ground or an alternate, or the last gate before that rung), or -1 when
    /// no gate guards it. That gate was read whole at the way out to the ground; a check that is not a way out reads no gate at or under it.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ground">The run's ground.</param>
    public static int RunGuard(Ladder ladder, string ground)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return KillTaskGate(ladder, ground) is { } gate ? IndexOf(ladder, gate.Rung) : -1;
    }

    private static int IndexOf(Ladder ladder, string rung)
    {
        for (var i = 0; i < ladder.Rungs.Count; i++)
        {
            if (string.Equals(ladder.Rungs[i].Key, rung, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// M6 14 (D135; the lead's brief of 2026-11-13: a rung moves on when the next rung's portal admits her and its gate reads open, the bands staying the latest she moves): the rung after the band's that the move-on may take before its band, or
    /// null with the reason. Each of these holds, read in order, or there is none:
    /// <list type="bullet">
    /// <item>the level is on the ladder and its band's rung is a ground rung; the rung after it (one rung, never two: the band stays the plan) is a ground rung, not pending, whose profile resolves and which the fight can hunt
    /// (<see cref="AutopilotScenario.GroundRefusal"/>);</item>
    /// <item>she is at least the plan's climber of its first level: her base War Magic (<see cref="GearCasts.TryBaseSkill"/>: no buff) is at least the plan's at the rung's first level (<paramref name="planWar"/>, the projection the
    /// rungs' spell levels are computed on). The bands are the plan's climber's (cache/m6/optimal-climb/study.md section 2), so the plan's own climber moves on at the band; a character ahead of the plan moves on as soon as the portal and the
    /// gate let her. No plan read (no dats), no move ahead;</item>
    /// <item>it has a gate (<see cref="Ladder.GateOf"/>): a rung with none is never taken before its band (unknown never opens a gate);</item>
    /// <item>the portals admit her: its entry portal's level range and a route of the travel file (<see cref="TravelRoutes.FirstAdmitted"/>, the Town Network's and the Facility Hub's gates), facts of the world.</item>
    /// </list>
    /// The reason names the first rule that fails, in this order (the cheap reads first).
    /// The gate itself is read by the caller, on what is known (the run's live buffs, a start's standing lines; unknown never opens it).
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="planWar">The plan's base War Magic at a level (<see cref="RungAsk.PlanWar"/>), or null.</param>
    /// <param name="why">Why there is a rung or none, by its rule's name.</param>
    public static LadderRung? Ahead(Ladder ladder, int level, PlayerQualities q, Func<int, uint?>? planWar, out string why)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(q);
        var c = CultureInfo.InvariantCulture;
        if (level < 1 || level > ladder.TopLevel)
        {
            why = string.Create(c, $"level: {level} is not on the ladder");
            return null;
        }

        var band = ladder.RungFor(level);
        var index = ladder.Rungs.ToList().FindIndex(r => r.Key == band.Key);
        if (band.Kind != LadderRungKind.Ground || index + 1 >= ladder.Rungs.Count)
        {
            why = $"band: {band.Key} is {(band.Kind == LadderRungKind.Ground ? "the last rung" : "a quest rung")}";
            return null;
        }

        var next = ladder.Rungs[index + 1];
        if (next.Kind != LadderRungKind.Ground || next.Pending || next.GroundKey is not { } key || GroundCatalog.TryGet(key) is not { } ground || AutopilotScenario.GroundRefusal(key) is { })
        {
            why = $"rung: {next.Key} has no ground the fight can hunt";
            return null;
        }

        if (planWar?.Invoke(next.Band.From) is not { } want)
        {
            why = "plan: the plan's War Magic cannot be read (no XpTable), so no rung is taken ahead of its band";
            return null;
        }

        if (!GearCasts.TryBaseSkill(q, MageTemplate.WarMagic, out var war) || war < want)
        {
            why = string.Create(c, $"strength: her base War Magic {(war > 0 ? war.ToString(c) : "unread")} is under the plan's {want} at {next.Key}'s first level {next.Band.From}");
            return null;
        }

        if (ladder.GateOf(next.Key) is null)
        {
            why = $"gate: {next.Key} has no gate, so it is never taken before its band";
            return null;
        }

        if (ground.Entry is not { } entry || !entry.Admits(level) || TravelRoutes.Default.FirstAdmitted(key, level, entry.MinLevel, entry.MaxLevel) is not { Outcome: TravelOutcome.Admitted } route)
        {
            why = string.Create(c, $"portal: no entry portal and route of {key} admit level {level}");
            return null;
        }

        why = string.Create(c, $"{next.Key} (levels {next.Band.From}-{next.Band.To}) at level {level}: route {route.Route!.Key} and entry {entry.Wcid} admit her, her base War Magic {war} is the plan's {want} at level {next.Band.From} or more");
        return next;
    }

    /// <summary>M6 14 (D135): <see cref="Ahead"/>'s reason alone, for the log: the rung after the band's that may be taken before its band, or the rule that stops it.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="planWar">The plan's base War Magic at a level, or null.</param>
    public static string AheadLine(Ladder ladder, int level, PlayerQualities q, Func<int, uint?>? planWar)
    {
        Ahead(ladder, level, q, planWar, out var why);
        return why;
    }

    /// <summary>
    /// M6 14 (D135): the gate of <paramref name="ahead"/> as this ask reads it: not read where she runs or stands on its ground (as the band's gates, <see cref="DecideOwn"/>), else on the run's live buffs, a start's standing lines, or
    /// the book alone with every line "up" not met; the read is added to <paramref name="read"/>, marked <see cref="RungGateRead.Ahead"/>. True when open (or not read).
    /// </summary>
    private static bool AheadGate(Ladder ladder, RungAsk ask, LadderRung ahead, List<RungGateRead> read, ref string? standing)
    {
        if (ask.RunGround is { } run && !ask.Leaving && string.Equals(run, ahead.GroundKey, StringComparison.Ordinal))
        {
            standing = run; // the run's own ground: its gate was read when she left for it
            return true;
        }

        if (ask.Cell is { } cell && ahead.GroundKey is { } key && GroundCatalog.TryGet(key) is { } ground && ground.InLandblocks(cell))
        {
            standing = key; // she stands on it: she is never stranded outside it
            return true;
        }

        var gate = ladder.GateOf(ahead.Key)!;
        var start = ask.Buffs is null ? ask.StartRead : null;
        var conditions = ask.Buffs is { } buffs ? RungGateRule.Check(gate, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother)
            : start is not null ? RungGateRule.AtStart(gate, ask.Qualities, ask.Knows, start)
            : RungGateRule.AtStart(gate, ask.Qualities, ask.Knows, ask.StandsByAnother);
        var gateRead = new RungGateRead(gate, conditions) { WayOutReads = start is null ? [] : RungGateRule.LeftToTheWayOut(gate, start), Ahead = true };
        read.Add(gateRead);
        return gateRead.Open;
    }

    /// <summary>
    /// M6 item 14 (the trial switch): the trial's choice over the ladder's own (<paramref name="own"/>). The ladder's own stops (a level unread, the top, a quest or a pending rung) stand: a trial changes which ground,
    /// never whether there is one. A trial asked is read by its rules first (<see cref="TrialRefusal"/>), one on keeps its rung. A way out to its ground reads its gate whole (<see cref="TrialGate"/>), live as every way out
    /// reads a gate: shut, the choice is the ladder's own with that read added and the refusal named. Else the trial's ground: hunt there, or move there from a run on another ground (<c>rung-moved:&lt;rung&gt;</c>, ok); the
    /// reason is <c>trial:&lt;ground&gt;</c>. The decision's rung is the trial's rung with the trial's ground (an alternate's own ground and landblock), so the fight takes that rung's bolt plan and target margin.
    /// </summary>
    private static RungChoice Trial(Ladder ladder, RungAsk ask, LadderTrial trial, RungChoice own)
    {
        if (own.Decision.Kind is not (LadderStepKind.Hunt or LadderStepKind.Moved))
        {
            return own;
        }

        LadderRung? rung;
        if (trial.Phase == LadderTrialPhase.Asked)
        {
            if (TrialRefusal(ladder, trial.Ground, ask.Level, out rung) is { } refused)
            {
                return own with { Trial = new TrialRead(trial.Ground, rung?.Key, refused, null, false) };
            }
        }
        else
        {
            rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.Key, trial.Rung, StringComparison.Ordinal));
        }

        if (rung is null || TrialGate(ladder, rung) is not { } gate)
        {
            return own with { Trial = new TrialRead(trial.Ground, rung?.Key, $"gate: TODO(m6-trial-gate): no gate at or before the trial's rung '{rung?.Key ?? trial.Rung}' in the ladder: the trial would go ungated", null, false) };
        }

        var gates = new List<RungGateRead>(own.Gates);
        RungGateRead? gateRead = null;
        if (ask.Leaving && string.Equals(ask.RunGround, trial.Ground, StringComparison.Ordinal))
        {
            var conditions = ask.Buffs is { } buffs
                ? RungGateRule.Check(gate, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother)
                : RungGateRule.AtStart(gate, ask.Qualities, ask.Knows, ask.StandsByAnother);
            gateRead = new RungGateRead(gate, conditions);
            gates.Add(gateRead);
            if (!gateRead.Open)
            {
                var shut = string.Join("; ", conditions.Where(c => !c.Met).Select(c => $"{c.Name} ({c.Read})"));
                return own with { Gates = gates, Trial = new TrialRead(trial.Ground, rung.Key, $"gate: the {gate.Rung} gate's lines are read shut at the way out to {trial.Ground}: {shut}", gateRead, false) };
            }

            if (ask.Approach?.Invoke(trial.Ground) is { Holds: true } approach)
            {
                // M6 14 retreat attackers (rule 6): an approach she cannot survive at her live state holds the trial by name; the ladder's own choice stands and she hunts her own ground. M6 14 approach cost: so does a crossing that costs
                // more than it pays, or one she gave up this run (the verdict's Holds)
                return own with { Gates = gates, Trial = new TrialRead(trial.Ground, rung.Key, approach.Held, gateRead, false), ApproachHeld = approach };
            }
        }

        var trialRung = string.Equals(rung.GroundKey, trial.Ground, StringComparison.Ordinal)
            ? rung
            : rung with { GroundKey = trial.Ground, Landblock = rung.Alternates.FirstOrDefault(a => string.Equals(a.GroundKey, trial.Ground, StringComparison.Ordinal))?.Landblock, Pending = false, PendingReason = null };
        var why = string.Create(CultureInfo.InvariantCulture, $"the trial of {trial.Ground} (rung {rung.Key}, {rung.Name}, levels {rung.Band.From}-{rung.Band.To}) at level {ask.Level}, in place of the ladder's own rung {own.Decision.Rung?.Key ?? "none"}");
        var decision = ask.RunGround is { } run && !string.Equals(run, trial.Ground, StringComparison.Ordinal)
            ? new LadderDecision(LadderStepKind.Moved, trialRung, ask.Level, "rung-moved:" + trialRung.Key, true, $"{why}: its ground is {trial.Ground}, the run's is {run}: the next run is on {trial.Ground}")
            : new LadderDecision(LadderStepKind.Hunt, trialRung, ask.Level, null, true, $"{why} on {trial.Ground}");
        var built = decision.Kind == LadderStepKind.Hunt ? GroundCatalog.TryGet(trial.Ground) : null;
        return new RungChoice(decision, built, ask.Level, "trial:" + trial.Ground, decision.Why, gates) { Trial = new TrialRead(trial.Ground, rung.Key, null, gateRead, true), RunGuardUnread = own.RunGuardUnread };
    }

    /// <summary>
    /// M6 item 14 (the trial switch): why a trial of <paramref name="ground"/> at <paramref name="level"/> is refused, by its rule's name (<c>&lt;rule&gt;: &lt;why&gt;</c>), or null when its rules hold, read in order:
    /// <c>not-later</c>: the ground is a ground of a rung after the level's own, or one of that rung's alternates (war-mage.json; the first such rung in climbing order is <paramref name="rung"/>);
    /// <c>unknown</c>: it has a profile; <c>portal</c>: its entry portal's level range admits her (a profile with no entry portal cannot be read, so it is refused); <c>unfightable</c>: the fight can hunt it
    /// (<see cref="AutopilotScenario.GroundRefusal"/>). The gate is the last rule (<see cref="TrialGate"/>): it is read whole, live, at the way out to the ground, as every gate is.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ground">The trial's ground key.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="rung">The later rung whose ground or alternate it is, or null.</param>
    public static string? TrialRefusal(Ladder ladder, string ground, int level, out LadderRung? rung)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        rung = null;
        if (level < 1 || level > ladder.TopLevel)
        {
            return string.Create(CultureInfo.InvariantCulture, $"level: the level {level} is not on the ladder (1 to {ladder.TopLevel}): her rung cannot be read");
        }

        var own = ladder.RungFor(level);
        var index = ladder.Rungs.ToList().FindIndex(r => r.Key == own.Key);
        rung = ladder.Rungs.Skip(index + 1).FirstOrDefault(r => string.Equals(r.GroundKey, ground, StringComparison.Ordinal) || r.Alternates.Any(a => string.Equals(a.GroundKey, ground, StringComparison.Ordinal)));
        if (rung is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"not-later: {ground} is no ground of a rung after her own ({own.Key}, levels {own.Band.From}-{own.Band.To}) and no alternate of one (war-mage.json)");
        }

        if (GroundCatalog.TryGet(ground) is not { } profile)
        {
            var pending = rung.Alternates.FirstOrDefault(a => string.Equals(a.GroundKey, ground, StringComparison.Ordinal))?.PendingReason;
            return $"unknown: {ground} has no ground profile{(pending is null ? string.Empty : " (" + pending + ")")}";
        }

        if (profile.Entry is not { } entry)
        {
            return $"portal: {ground}'s profile has no entry portal, so the level range it admits cannot be read";
        }

        if (!entry.Admits(level))
        {
            var range = entry.MaxLevel is { } max ? string.Create(CultureInfo.InvariantCulture, $"{entry.MinLevel ?? 1} to {max}") : string.Create(CultureInfo.InvariantCulture, $"{entry.MinLevel ?? 1} and up");
            return string.Create(CultureInfo.InvariantCulture, $"portal: the entry portal {entry.Wcid} of {ground} admits levels {range}; level {level} is not admitted");
        }

        return AutopilotScenario.GroundRefusal(ground) is { } unfightable ? "unfightable: " + unfightable : null;
    }

    /// <summary>
    /// M6 item 14 (the trial switch): the gate a trial of <paramref name="rung"/> must pass: the rung's own, or, for a rung with none, the last gate before it in climbing order (past the Abode: the Abode's, the Cave's
    /// shape: the same lines at the same levels), for the bot's protection. Null when no gate stands at or before the rung.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="rung">The trial's rung.</param>
    public static RungGate? TrialGate(Ladder ladder, LadderRung rung)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(rung);
        var index = ladder.Rungs.ToList().FindIndex(r => r.Key == rung.Key);
        for (var i = index; i >= 0; i--)
        {
            if (ladder.GateOf(ladder.Rungs[i].Key) is { } gate)
            {
                return gate;
            }
        }

        return null;
    }

    /// <summary>M6 7 AX: the first trophy job due at the level whose rung's gate is open or whose ground she stands on, else null. A rung she has outgrown is only entered for this; the keep-out and the stand are the ground's data.</summary>
    private static (LadderRung Rung, PullStand Stand)? TrophyJob(Ladder ladder, RungAsk ask)
    {
        if (ask.TrophyDue is not { } due || ask.Buffs is not { } buffs)
        {
            return null;
        }

        foreach (var rung in ladder.Rungs)
        {
            if (rung.GroundKey is null || rung.Band.To >= ask.Level)
            {
                continue; // the job is for a rung below her band: on its own band the hunt is already there
            }

            // M6 retreat: the rung's own ground, then its live alternates (the 10-14 rung hunts the Retreat; the Female Tusk's carriers stand in its alternate, the Burrow): the job's rung is played on the ground that holds the pull
            foreach (var key in rung.Alternates.Where(a => !a.Pending).Select(a => a.GroundKey).Prepend(rung.GroundKey))
            {
                if (GroundCatalog.TryGet(key) is not { Hunt: { } hunt } ground)
                {
                    continue;
                }

                foreach (var pull in hunt.Pulls)
                {
                    if (pull.Stand is { } stand && ask.Level >= stand.FromLevel && ask.Level <= stand.ToLevel && due(stand.Quest)
                        && (ask.RunGround == key || (ask.Cell is { } cell && ground.InLandblocks(cell)) || ladder.GateOf(rung.Key) is not { } gate || RungGateRule.Check(gate, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother).All(c => c.Met)))
                    {
                        return (PlayedOn(rung, key)!, stand);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The rung ground that holds <paramref name="cell"/> (a rung's own ground, or, M6 redoubt, its outdoor fallback ground: <see cref="RungFallback"/>), or null: the corpse lies on no rung's ground (a road).</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="cell">The cell.</param>
    public static HuntingGround? GroundOf(Ladder ladder, uint cell)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return ladder.Rungs.Where(r => r.GroundKey is not null).Select(r => GroundCatalog.TryGet(r.GroundKey!)).FirstOrDefault(g => g is not null && g.InLandblocks(cell))
            ?? ladder.Rungs.Select(r => RungFallback.Of(r) is { } f ? GroundCatalog.TryGet(f.GroundKey) : null).FirstOrDefault(g => g is not null && g.InLandblocks(cell));
    }

    /// <summary>
    /// M6 7 AY (the owner's decision of 2026-11-06; closes <c>m6-corpse-run-gate</c>): a corpse run asks too, so it cannot return her behind a shut gate. The ask is the way out to the corpse's ground, read at most at that rung's last level (a shut gate past the band still stands, whatever the band says of her):
    /// she may go when the gates leave her at that rung or above. A corpse on no rung's ground is no rung's matter.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ask">What is known; its run ground and leaving are this method's.</param>
    /// <param name="corpseGround">The ground that holds the corpse (<see cref="GroundOf"/>), or null.</param>
    public static CorpseRunAnswer CorpseRun(Ladder ladder, RungAsk ask, string? corpseGround)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(ask);
        if (ask.Trial is { Phase: LadderTrialPhase.On or LadderTrialPhase.Ended, Rung: { } trialKey } trial && string.Equals(trial.Ground, corpseGround, StringComparison.Ordinal)
            && ladder.Rungs.FirstOrDefault(r => r.Key == trialKey) is { } trialRung && TrialGate(ladder, trialRung) is { } trialGate)
        {
            // M6 item 14 (the trial switch): a corpse on the trial's ground, where its rules let her in: the trial's gate decides the walk back, read whole on the buffs that stand after the rebuff, as at a way out
            var conditions = ask.Buffs is { } buffs
                ? RungGateRule.Check(trialGate, ask.Qualities, ask.Knows, buffs.Active, buffs.Reach, ask.StandsByAnother)
                : RungGateRule.AtStart(trialGate, ask.Qualities, ask.Knows, ask.StandsByAnother);
            var open = conditions.All(c => c.Met);
            return new CorpseRunAnswer(open, (open ? "trial:" : "trial-gate-held:") + trial.Ground, trialRung.Key);
        }

        if (corpseGround is null || ladder.Rungs.FirstOrDefault(r => string.Equals(r.GroundKey, corpseGround, StringComparison.Ordinal) || RungFallback.Names(r, corpseGround)) is not { } rung)
        {
            return new CorpseRunAnswer(true, "no-rung", null);
        }

        var choice = Decide(ladder, ask with { Level = Math.Min(ask.Level, rung.Band.To), RunGround = corpseGround, Leaving = true, NamedRung = null, Trial = null, KillTask = null });
        return new CorpseRunAnswer(rung.Band.From <= choice.HeldLevel || choice.Decision.Rung?.Key == rung.Key, choice.Reason, rung.Key); // M6 14 (D135): a corpse on the rung ahead of her band, whose gate reads open again
    }

    /// <summary>The ground of a level by the band alone, with no gate. For the tests of the band's own rule: no path chooses a rung by this.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The level.</param>
    /// <param name="why">The rung, or the stop's reason.</param>
    internal static HuntingGround? BandGround(Ladder ladder, int level, out string why)
    {
        var decision = AutopilotScenario.LadderDecisionFor(ladder, level, static _ => true);
        var ground = decision.Kind == LadderStepKind.Hunt && decision.Rung?.GroundKey is { } key ? GroundCatalog.TryGet(key) : null;
        why = BandWhy(ladder, level, decision, ground);
        return ground;
    }

    /// <summary>The rung's move-on, when its level is reached on the level the gates left and the cycle's check would change something (<see cref="AutopilotScenario.MoveOnDue"/>), else null.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="rung">The rung the run is on.</param>
    /// <param name="choice">This class's answer.</param>
    /// <param name="groundKey">The run's ground.</param>
    /// <param name="questDone">Whether a quest is done.</param>
    public static LadderDecision? MoveOnDue(Ladder ladder, LadderRung rung, RungChoice choice, string groundKey, Func<string, bool>? questDone)
    {
        ArgumentNullException.ThrowIfNull(rung);
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Decision is { Kind: LadderStepKind.Moved, Reason: AutopilotScenario.AheadReason, Rung: { } next } && next.Key != rung.Key)
        {
            return choice.Decision; // M6 14 (D135): the rung after the band's, before her level reaches the move-on level: its portal admits her and its gate reads open
        }

        return AutopilotScenario.MoveOnDue(ladder, rung, choice.HeldLevel, groundKey, questDone);
    }

    private static string BandWhy(Ladder ladder, int level, LadderDecision decision, HuntingGround? ground)
    {
        if (level <= 0)
        {
            return "failed:rung-level-unknown: " + decision.Why;
        }

        // M6 0 lead: the host cannot read the quests' stamps before the run exists, so a level inside a quest rung builds the run on the rung after it, provisionally; the run's first check confirms the rung's quests done or stops by name (rung-quest:<key>).
        return ground is not null && ladder.RungFor(Math.Clamp(level, 1, ladder.TopLevel)).Kind == LadderRungKind.Quest ? "provisional, the quest rung's end is read by the run's first check: " + decision.Why : decision.Why;
    }

    private static string ReasonOf(RungChoice choice, LadderRung? named, string? standing)
    {
        if (choice.Gates.FirstOrDefault(g => !g.Open && !g.Ahead) is { } shut)
        {
            return "gate-held:" + shut.Gate.Rung; // the gate of the rung ahead of the band holds her on her band's rung, never under it: no hold
        }

        return named is not null ? "stop-named:" + named.Key
            : choice.Decision.Reason == AutopilotScenario.AheadReason ? (choice.WayOutReads.Count > 0 ? "start-pick:" : "ahead:") + choice.Decision.Rung!.Key
            : choice.Opened ? (choice.WayOutReads.Count > 0 ? "start-pick:" : "gate-open:") + choice.Decision.Rung!.Key
            : standing is not null ? "stands-on:" + standing
            : choice.Decision.Reason ?? "band";
    }
}
