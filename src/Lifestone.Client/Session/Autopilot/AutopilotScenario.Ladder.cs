using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the ladder asks of the run at a cycle's start (<see cref="AutopilotScenario.DecideRung"/>).</summary>
public enum LadderStepKind
{
    /// <summary>The rung's ground is the run's: hunt on.</summary>
    Hunt,

    /// <summary>The level has reached the rung's move-on level: the next rung's ground is another (stop <c>rung-moved:&lt;rung&gt;</c>, ok).</summary>
    Moved,

    /// <summary>The rung's profile is not written (stop <c>rung-pending:&lt;rung&gt;</c>).</summary>
    Pending,

    /// <summary>The rung is quests, which the job does not run yet (stop <c>rung-quest:&lt;rung&gt;</c>).</summary>
    Quest,

    /// <summary>The fight's own ground rule refuses the rung's ground (stop <c>rung-unfightable:&lt;rung&gt;</c>).</summary>
    Unfightable,

    /// <summary>The level is past the ladder's top (stop <c>ladder-top</c>, ok: the climb is over).</summary>
    Top,

    /// <summary>The character's level is not in the state (stop <c>failed:rung-level-unknown</c>).</summary>
    LevelUnknown,
}

/// <summary>The ladder's answer for one level and ground: what to do, the rung, and the stop's name and reason when the run stops.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Rung">The rung of the level, or null (top, level unknown).</param>
/// <param name="Level">The character level read.</param>
/// <param name="Stop">The stop's name, or null for <see cref="LadderStepKind.Hunt"/>.</param>
/// <param name="Ok">The stop's ok (a move-on and the top are ok; the rest are loud).</param>
/// <param name="Why">The reason, for the log.</param>
/// <param name="Reason">Set to <see cref="AutopilotScenario.QuestsDoneReason"/> when every quest of the level's band's quest rung is done and the next rung is taken; else null.</param>
public sealed record LadderDecision(LadderStepKind Kind, LadderRung? Rung, int Level, string? Stop, bool Ok, string Why, string? Reason = null);

/// <summary>
/// One <c>[ladder]</c> line (M6 the spec section 2, D27: report-only in its first gate): the level from the character's own property, the rung and ground the run is on, the counts, and the move-on rule's floor and ceiling beside
/// the rates they judge (only logged). A field a later item fills reads 0 until then, never omitted. M6 1 d: <c>scrolls_bought</c> and <c>learned</c> are the trips' learn step (the logistics line's fields, summed). M6 1 b: <c>trained</c> names the skills the level-up spend trained from the credits (the cycle's, or the run's on the done
/// line; <c>none</c> when it trained none) and <c>spent_xp</c> holds the level-up spend's raises with the trips' (report-only, D27). <c>quests_done</c> is the number of the ladder's quests (the quest rungs' keys) the job knows to be done (M6 0 F5, 0-R6: the same rule that decides <c>reason=quests-done</c>). <c>granted</c> is the cycle's or the run's tally of the
/// server mod's verbs (the session's verb counter, as the cycle line's own <c>granted=</c>): a fact, never assumed.
/// </summary>
/// <param name="At"><c>cycle</c> or <c>done</c>.</param>
/// <param name="Cycle">The cycle number (the done line: the cycles counted).</param>
/// <param name="Level">The character level, or null when the state has none.</param>
/// <param name="Rung">The rung key, or null before the first check.</param>
/// <param name="Ground">The run's ground key.</param>
/// <param name="Tally">The cycle's or the run's tally.</param>
/// <param name="WalkSeconds">The walking seconds measured from the state (<see cref="AutopilotScenario.WalkWindowSeconds"/>), cancelled legs included.</param>
/// <param name="MoveOn">The rung's move-on rule, or null before the first check.</param>
/// <param name="CastsAKill">The casts a kill, or null while the fight line gives no casts.</param>
/// <param name="Reason">The rung's reason (<see cref="AutopilotScenario.QuestsDoneReason"/>) when the rung is not the level's band's, else null; written as a last field <c>reason=</c> only then (M6 0 F4).</param>
/// <param name="QuestsDone">The ladder's quests the job knows to be done (M6 0 F5, 0-R6): only the keys of the ladder file's quest rungs, by the rule that decides <c>reason=quests-done</c>. A trophy turn-in (the Female Tusk) is not one of them, so a line with <c>turn_ins=1</c> and <c>quests_done=0</c> is true, not a miss (M6 8 E, <c>m6-ladder-quests-done</c>: the two fields count different things; this one is the rung rule's, never a sum of turn-ins).</param>
/// <param name="Trained">M6 1 b: the skills trained from the credits (<see cref="LevelUpSpend.TrainedField"/>: the names without spaces, commas between; <c>none</c>).</param>
/// <param name="TurnIns">M6 5 Q5 (D58, D27 report-only): the quest completions of this cycle or run whose quest is not a quest rung's (the trophy turn-ins, made inside a stretch or outside it, each counted once when its station ends well; the quest rungs' own quests count in <c>quests_done</c> instead: <c>turn_ins=</c>, before <c>health=</c>, after M6 7 U's <c>corpses_lost=</c>, the corpse trips given up).</param>
/// <param name="Health">M6 7 R (D27 report-only): the character's maximum health at this line (<c>health=</c>, the health the owner reads rung by rung), or null when the state has none.</param>
/// <param name="SurvivalHolds">M6 8 K (report-only): the holds the survival layer made in this cycle or run (<c>survival_holds=</c>), each counted once when it began.</param>
/// <param name="SurvivalLeaves">M6 8 K (report-only): the activities the survival layer ended and left (<c>survival_leaves=</c>).</param>
/// <param name="LifestoneRecalls">M6 8 N (D120, report-only): the Lifestone Recalls the survival layer's last resort took and that arrived (<c>lifestone_recalls=</c>, after <c>health=</c>); each marks a walk home that failed after a failed ladder errand.</param>
/// <param name="BuffBot">M6 item 14 (D125): the autopilot's stop at the buff bot is on (<c>buffbot=on</c>, the last field before <c>reason=</c>): a climb with the bot and one without are never read as one figure.</param>
/// <param name="Errands">M6 8 F (D118, report-only): the ladder errands completed in this cycle or run (<c>errands=</c>, just before <c>turn_ins=</c>): a quest the ladder's band sent her on, never a trophy, so the two fields never count the same completion.</param>
/// <param name="Trial">
/// M6 item 14 (the trial switch, <see cref="LadderTrial"/>): the trial's ground when this cycle (or, on the done line, this run) was fought under the trial (<c>trial=&lt;ground&gt;</c>, after <c>buffbot=</c> and before <c>reason=</c>), so
/// the trial's rate is read beside the rung's; null (the field left out, as <c>reason=</c> is) on every other line.
/// </param>
public readonly record struct LadderLine(string At, int Cycle, int? Level, string? Rung, string Ground, AutopilotTally Tally, double WalkSeconds, LadderMoveOn? MoveOn, double? CastsAKill, string? Reason = null, int QuestsDone = 0, string Trained = "none", int TurnIns = 0, uint? Health = null, int Errands = 0, int SurvivalHolds = 0, int SurvivalLeaves = 0, int LifestoneRecalls = 0, bool BuffBot = false, string? Trial = null)
{
    /// <summary>The line's prefix.</summary>
    public const string Prefix = "[ladder]";

    /// <summary>The field names, in order.</summary>
    public static IReadOnlyList<string> FieldNames { get; } =
        ["at", "n", "level", "rung", "ground", "kills", "xp_per_h", "deaths", "scrolls_bought", "learned", "trained", "spent_xp", "quests_done", "granted", "walk_s", "rest_s", "s", "move_on_level", "kills_per_h", "kills_per_h_floor", "under_floor", "casts_a_kill", "casts_a_kill_ceiling", "over_ceiling", "corpses_lost", "survival_holds", "survival_leaves", "errands", "turn_ins", "health", "lifestone_recalls", "buffbot"];

    /// <summary>The experience an hour over the tally's seconds (0 with no seconds).</summary>
    public double XpPerHour => Tally.Seconds > 0 ? Tally.Xp * 3600.0 / Tally.Seconds : 0;

    /// <summary>The kills an hour over the tally's seconds (0 with no seconds).</summary>
    public double KillsPerHour => Tally.Seconds > 0 ? Tally.Kills * 3600.0 / Tally.Seconds : 0;

    /// <summary>The line, <see cref="FieldNames"/> in order after <see cref="Prefix"/>.</summary>
    public string Text()
    {
        var c = CultureInfo.InvariantCulture;
        var floor = MoveOn?.KillsAnHourFloor;
        var ceiling = MoveOn?.CastsAKillCeiling;
        var under = floor is { } f && Tally.Seconds > 0 ? (KillsPerHour < f ? "yes" : "no") : "n/a";
        var over = ceiling is { } m && CastsAKill is { } k ? (k > m ? "yes" : "no") : "n/a";
        return string.Create(
            c,
            $"{Prefix} at={At} n={Cycle} level={(Level is { } l ? l.ToString(c) : "?")} rung={Rung ?? "none"} ground={Ground} kills={Tally.Kills} xp_per_h={XpPerHour:0} deaths={Tally.Deaths} scrolls_bought={Tally.ScrollsBought} learned={Tally.Learned} trained={Trained} spent_xp={Tally.SpentXp} quests_done={QuestsDone} granted={Tally.Granted} walk_s={WalkSeconds:0} rest_s={Tally.RestSeconds:0} s={Tally.Seconds:0} move_on_level={(MoveOn is { } r ? r.Level.ToString(c) : "n/a")} kills_per_h={KillsPerHour:0} kills_per_h_floor={(floor is { } f2 ? f2.ToString(c) : "n/a")} under_floor={under} casts_a_kill={(CastsAKill is { } k2 ? k2.ToString("0.0", c) : "n/a")} casts_a_kill_ceiling={(ceiling is { } m2 ? m2.ToString("0.0", c) : "n/a")} over_ceiling={over} corpses_lost={Tally.CorpsesLost} survival_holds={SurvivalHolds} survival_leaves={SurvivalLeaves} errands={Errands} turn_ins={TurnIns} health={(Health is { } h ? h.ToString(c) : "?")} lifestone_recalls={LifestoneRecalls} buffbot={(BuffBot ? "on" : "off")}{(Trial is { } trial ? " trial=" + trial : string.Empty)}{(Reason is { } why ? " reason=" + why : string.Empty)}");
    }
}

/// <summary>The <c>level up</c> job's first form (M6 item 0 a4): the rung per cycle, the move-on rule read, the bolt cap, the target band, the buff lines the spellbook lacks, the walking seconds and the <c>[ladder]</c> line.</summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The window of the walking clock, in seconds (Lifestone's own): the position is compared one window apart, so the server's stepwise position updates are read as a pace and not as jumps.</summary>
    public const double WalkWindowSeconds = 1.0;

    /// <summary>The slowest pace that counts as walking, in metres a second (Lifestone's own: under it the position's jitter, a turn or a step aside).</summary>
    public const double WalkMinMetresPerSecond = 0.5;

    /// <summary>The fastest pace that counts, in metres a second (Lifestone's own: above any run, whose rate tops near 4 x 3 = 12 m/s in ACE, so a larger move in one window is a teleport or a portal).</summary>
    public const double WalkMaxMetresPerSecond = 20.0;

    /// <summary>The stops the ladder makes before any station: no recall first (nothing was fought, and the next run's known state finds the character where it stands).</summary>
    internal static bool LadderStop(string stop) => stop.StartsWith("rung-", StringComparison.Ordinal) || stop.StartsWith("failed:rung-", StringComparison.Ordinal) || stop == "ladder-top";

    private Ladder? _ladder;
    private int _ladderCycle = -1;
    private bool _ladderChecked;
    private bool _ladderMidRun; // M6 14 gate lapse: the run is past its first check (RungAsk.MidRun)
    private LadderRung? _rung;
    private readonly HashSet<string> _ladderNoted = new(StringComparer.Ordinal);
    private GlobalPosition? _walkFrom;
    private double _walkFromClock = double.NaN;
    private double _walkCycleSeconds;
    private double _walkTotalSeconds;

    /// <summary>The walking seconds of the run so far (the cycles closed plus the open one), measured from the state: cancelled legs count, as every pace does.</summary>
    public double WalkSeconds => _walkTotalSeconds + _walkCycleSeconds;

    /// <summary>The rung the run is on (null before the first check, and in the den job).</summary>
    public LadderRung? Rung => _rung;

    /// <summary>
    /// The ladder's answer for <paramref name="level"/> on the run's ground <paramref name="groundKey"/>: a quest rung, a pending rung and a ground the fight refuses are loud stops naming the rung; a rung of another ground is the
    /// move-on (the level has reached the previous rung's move-on level, its top + 1); a level past the top ends the climb.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level (its own property); 0 or less when the state has none.</param>
    /// <param name="groundKey">The key of the ground the run is on.</param>
    /// <param name="groundError">The fight's ground rule (default <see cref="FightOptions.GroundError"/>): why the fight cannot walk a ground, or null.</param>
    /// <param name="questDone">M6 0 F4: whether the character has done the quest of a key (a quest rung's <see cref="LadderRung.QuestKeys"/>); a quest rung whose every quest is done takes the ladder's NEXT rung even while the level is inside the quest rung's band
    /// (the Academy gives level 3-4 on this server, TODO(m6-academy-sentry-percent)), with <see cref="LadderDecision.Reason"/> <see cref="QuestsDoneReason"/>; null reads every quest as not done.</param>
    internal static LadderDecision DecideRung(Ladder ladder, int level, string groundKey, Func<string, string?>? groundError = null, Func<string, bool>? questDone = null)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        if (level <= 0)
        {
            return new(LadderStepKind.LevelUnknown, null, level, "failed:rung-level-unknown", false, "the character's level (PropertyInt 25) is not in the state; the rung cannot be chosen");
        }

        if (level > ladder.TopLevel)
        {
            return new(LadderStepKind.Top, null, level, "ladder-top", true, string.Create(CultureInfo.InvariantCulture, $"level {level} is past the ladder's top ({ladder.TopLevel}): the climb is over"));
        }

        var rung = ladder.RungFor(level);
        string? reason = null;
        var band = string.Create(CultureInfo.InvariantCulture, $"rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) for level {level}");
        if (rung.Kind == LadderRungKind.Quest)
        {
            var next = NextRung(ladder, rung);
            if (questDone is null || rung.QuestKeys.Count == 0 || next is null || !rung.QuestKeys.All(questDone))
            {
                return new(LadderStepKind.Quest, rung, level, "rung-quest:" + rung.Key, false, $"{band} is quests ({string.Join(", ", rung.QuestKeys)}); the level up job does not run quests yet");
            }

            reason = QuestsDoneReason;
            band = string.Create(CultureInfo.InvariantCulture, $"rung {next.Key} ({next.Name}, levels {next.Band.From}-{next.Band.To}) for level {level}: every quest of {rung.Key} ({string.Join(", ", rung.QuestKeys)}) is done, so the ladder's next rung is taken inside {rung.Key}'s band {rung.Band.From}-{rung.Band.To}");
            rung = next;
        }

        return DecideOn(rung, level, groundKey, groundError, reason, band);
    }

    /// <summary>
    /// M6 14 (D135, the move-on ahead of the band): the decision for <paramref name="rung"/>, a ground rung named outright (<see cref="RungOwner.Ahead"/>'s rung after the band's, whose portal admits her and whose gate reads open), on the
    /// run's ground <paramref name="groundKey"/>: the same loud stops as <see cref="DecideRung"/> (pending, a ground the fight refuses, a dungeon whose walk reaches no slot), then the move or the hunt; the reason is
    /// <see cref="AheadReason"/>.
    /// </summary>
    /// <param name="rung">The rung.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="groundKey">The run's ground.</param>
    internal static LadderDecision DecideAhead(LadderRung rung, int level, string groundKey)
    {
        ArgumentNullException.ThrowIfNull(rung);
        var band = string.Create(CultureInfo.InvariantCulture, $"rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) for level {level}, ahead of its band (D135: the rung after the band's, whose portal admits her and whose gate reads open)");
        return DecideOn(rung, level, groundKey, null, AheadReason, band);
    }

    /// <summary>The <c>reason=</c> of a decision that took the rung after the band's before its band (M6 14, D135; <see cref="RungOwner.Ahead"/>).</summary>
    public const string AheadReason = "ahead";

    /// <summary>
    /// M6 14 armory (the owner, 2026-11-14: try the Tusker Armory as the climber's next ground): the decision for <paramref name="rung"/> on one of its alternates (<paramref name="rung"/> carries the alternate's ground and landblock), taken
    /// because the rung's own ground is held by its approach (<see cref="RungOwner"/>), on the run's ground <paramref name="groundKey"/>: the same loud stops as <see cref="DecideRung"/>, then the move or the hunt; the reason is
    /// <see cref="AlternateReason"/>. The band, the gate, the bolt plan and the target margin are the rung's own.
    /// </summary>
    /// <param name="rung">The rung with the alternate's ground.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="groundKey">The run's ground.</param>
    internal static LadderDecision DecideAlternate(LadderRung rung, int level, string groundKey)
    {
        ArgumentNullException.ThrowIfNull(rung);
        var band = string.Create(CultureInfo.InvariantCulture, $"rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) for level {level}, on its alternate {rung.GroundKey} (its own ground's approach holds her)");
        return DecideOn(rung, level, groundKey, null, AlternateReason, band);
    }

    /// <summary>The <c>reason=</c> of a decision that took a rung's alternate because the rung's own ground is held by its approach (M6 14 armory; <see cref="DecideAlternate"/>).</summary>
    public const string AlternateReason = "alternate";

    /// <summary>
    /// M6 redoubt: the decision for <paramref name="rung"/> on its outdoor fallback ground (<paramref name="rung"/> carries the fallback's ground, <see cref="RungFallback"/>), taken because the rung's own ground's entry portal does not admit
    /// <paramref name="liveLevel"/> (<paramref name="refusal"/>, the gate's own name), on the run's ground <paramref name="groundKey"/>: the same loud stops as <see cref="DecideRung"/>, then the move or the hunt; the reason is
    /// <see cref="EntryFallbackReason"/>. The band, the gate, the bolt plan and the target margin are the rung's own.
    /// </summary>
    /// <param name="rung">The rung with the fallback's ground.</param>
    /// <param name="level">The level the ladder read the rung at (a held level under a shut gate).</param>
    /// <param name="liveLevel">Her live level, the one the portal reads.</param>
    /// <param name="groundKey">The run's ground.</param>
    /// <param name="refusal">The entry's refusal by name (<c>gate:entry:&lt;ground&gt;:&lt;level&gt;&gt;&lt;max&gt;</c>).</param>
    internal static LadderDecision DecideEntryFallback(LadderRung rung, int level, int liveLevel, string groundKey, string refusal)
    {
        ArgumentNullException.ThrowIfNull(rung);
        var band = string.Create(CultureInfo.InvariantCulture, $"rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) for level {level}, on its outdoor ground {rung.GroundKey} (its own ground's portal does not admit level {liveLevel}: {refusal})");
        return DecideOn(rung, level, groundKey, null, EntryFallbackReason, band);
    }

    /// <summary>The <c>reason=</c> of a decision that took a rung's outdoor fallback ground because the own ground's entry portal does not admit her level (M6 redoubt; <see cref="DecideEntryFallback"/>).</summary>
    public const string EntryFallbackReason = "entry-fallback";

    /// <summary>
    /// M6 retreat (the owner, 2026-11-19: the 10-14 rung hunts the Tusker Retreat, the Burrow its alternate): the decision for <paramref name="rung"/> played on one of its live alternates (<paramref name="rung"/> carries the
    /// alternate's ground and landblock) for a trophy job whose pull that ground holds (<see cref="RungOwner"/>), on the run's ground <paramref name="groundKey"/>: the same loud stops as <see cref="DecideRung"/>, then the hunt, or the
    /// move, whose stop names the ground after the rung (<c>rung-moved:&lt;rung&gt;@&lt;ground&gt;</c>, <see cref="RungOwner.NamedGround"/>) so the host's restart, which has no quest timers, builds that ground. The band, the gate, the
    /// bolt plan and the target margin are the rung's own.
    /// </summary>
    /// <param name="rung">The rung with the alternate's ground.</param>
    /// <param name="level">The level the gates hold her at.</param>
    /// <param name="groundKey">The run's ground.</param>
    internal static LadderDecision DecideOnGround(LadderRung rung, int level, string groundKey)
    {
        ArgumentNullException.ThrowIfNull(rung);
        var band = string.Create(CultureInfo.InvariantCulture, $"rung {rung.Key} ({rung.Name}, levels {rung.Band.From}-{rung.Band.To}) for level {level}, on its alternate {rung.GroundKey} (the ground of its trophy job's pull)");
        var decision = DecideOn(rung, level, groundKey, null, null, band);
        return decision.Kind == LadderStepKind.Moved ? decision with { Stop = decision.Stop + RungOwner.GroundMark + rung.GroundKey } : decision;
    }

    private static LadderDecision DecideOn(LadderRung rung, int level, string groundKey, Func<string, string?>? groundError, string? reason, string band)
    {
        if (rung.Pending || rung.GroundKey is null)
        {
            return new(LadderStepKind.Pending, rung, level, "rung-pending:" + rung.Key, false, $"{band}: its ground '{rung.GroundKey}' has no profile yet ({rung.PendingReason ?? "no reason given"})", reason);
        }

        if ((groundError ?? FightOptions.GroundError)(rung.GroundKey) is { } refused)
        {
            return new(LadderStepKind.Unfightable, rung, level, "rung-unfightable:" + rung.Key, false, $"{band}: the fight refuses its ground: {refused}", reason);
        }

        if (GroundCatalog.TryGet(rung.GroundKey) is { Kind: GroundKind.Dungeon } dungeon && HuntedSlots(dungeon) == 0)
        {
            // M6 3 F1 (3-R3): a dungeon whose walk reaches no target slot would hold at its arrival until the run's time limit; the rung stops by name instead (the same refusal as the fight's ground rule).
            return new(LadderStepKind.Unfightable, rung, level, "rung-unfightable:" + rung.Key, false, $"{band}: its ground {rung.GroundKey} hunts 0 of {dungeon.Respawn.Slots.Count} respawn slot(s) (the walk reaches no target slot; see the profile's walk TODOs)", reason);
        }

        if (!string.Equals(rung.GroundKey, groundKey, StringComparison.Ordinal) && RungFallback.Of(rung) is { } fallback && string.Equals(fallback.GroundKey, groundKey, StringComparison.Ordinal))
        {
            // M6 redoubt: a run that stands on the rung's outdoor fallback ground (a climber its own ground's portal refused, a run named by hand) is a run of this rung and hunts on: the ladder sends a new run to the rung's own
            // ground and never pulls one off the outdoor ground. The decision's rung carries the run's ground, as an alternate's does.
            var own = rung.GroundKey;
            rung = rung with { GroundKey = fallback.GroundKey, Landblock = fallback.Landblock };
            return new(LadderStepKind.Hunt, rung, level, null, true, $"{band} on {groundKey}, the rung's outdoor ground (a run that stands on it hunts on; a new run takes {own})", reason);
        }

        if (!string.Equals(rung.GroundKey, groundKey, StringComparison.Ordinal))
        {
            return new(LadderStepKind.Moved, rung, level, "rung-moved:" + rung.Key, true, $"{band}: its ground is {rung.GroundKey}, the run's is {groundKey}: the next run is on {rung.GroundKey}", reason);
        }

        return new(LadderStepKind.Hunt, rung, level, null, true, $"{band} on {groundKey}", reason);
    }

    /// <summary>The respawn slots of <paramref name="ground"/> a hunt can use: a target's slot in a walkable cell (the count <c>RespawnSchedule.HuntedCount</c> logs).</summary>
    private static int HuntedSlots(HuntingGround ground) => ground.Respawn.Slots.Count(s => ground.RoleOf(s.Wcid) == GroundRole.Target && ground.IsWalkable(s.Cell));

    /// <summary>
    /// M6 item 14 (the trial switch): why the fight cannot hunt <paramref name="groundKey"/>, or null: the two refusals <see cref="DecideRung"/> makes of a rung's ground (the fight's ground rule, and a dungeon whose walk reaches no
    /// target slot), for a ground no rung of the level names (<see cref="RungOwner.TrialRefusal"/>).
    /// </summary>
    /// <param name="groundKey">The ground's key.</param>
    internal static string? GroundRefusal(string groundKey)
    {
        if (FightOptions.GroundError(groundKey) is { } refused)
        {
            return "the fight refuses its ground: " + refused;
        }

        return GroundCatalog.TryGet(groundKey) is { Kind: GroundKind.Dungeon } dungeon && HuntedSlots(dungeon) == 0
            ? string.Create(CultureInfo.InvariantCulture, $"its ground {groundKey} hunts 0 of {dungeon.Respawn.Slots.Count} respawn slot(s) (the walk reaches no target slot; see the profile's walk TODOs)")
            : null;
    }

    /// <summary>The <c>reason=</c> of a decision that took the rung after a quest rung (M6 0 F4): every quest the quest rung names is done for this character.</summary>
    public const string QuestsDoneReason = "quests-done";

    /// <summary>The rung after <paramref name="rung"/> in the ladder's order, or null for the last.</summary>
    private static LadderRung? NextRung(Ladder ladder, LadderRung rung)
    {
        for (var i = 0; i < ladder.Rungs.Count - 1; i++)
        {
            if (ladder.Rungs[i].Key == rung.Key)
            {
                return ladder.Rungs[i + 1];
            }
        }

        return null;
    }

    /// <summary>The ground a level's band names, or null with the reason when the ladder stops there. By the band alone: <see cref="RungOwner"/> is its caller.</summary>
    /// <param name="level">The character's level.</param>
    /// <param name="why">The rung, or the stop's reason.</param>
    /// <param name="questDone">M6 0 F4: whether the character has done the quest of a key (see <see cref="DecideRung"/>), or null.</param>
    internal static HuntingGround? LadderGroundFor(int level, out string why, Func<string, bool>? questDone = null)
    {
        var decision = LadderDecisionFor(LadderLoader.WarMage, level, questDone);
        why = decision.Why;
        return decision.Kind == LadderStepKind.Hunt && decision.Rung?.GroundKey is { } key ? GroundCatalog.TryGet(key) : null;
    }

    /// <summary><see cref="DecideRung"/> on the ground the level's own rung names, so a rung with a run reads Hunt.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The level.</param>
    /// <param name="questDone">Whether a quest is done, or null.</param>
    internal static LadderDecision LadderDecisionFor(Ladder ladder, int level, Func<string, bool>? questDone)
    {
        var inBand = level >= 1 && level <= ladder.TopLevel ? ladder.RungFor(level) : null;
        var asked = inBand is { Kind: LadderRungKind.Quest } && questDone is not null && inBand.QuestKeys.Count > 0 && inBand.QuestKeys.All(questDone) && NextRung(ladder, inBand) is { } next ? next : inBand;
        return DecideRung(ladder, level, asked?.GroundKey ?? string.Empty, null, questDone);
    }

    /// <summary>
    /// The bolt level the rung allows by the fizzle rule (M6 item 1 a, D41): <paramref name="live"/> is the highest level of <paramref name="line"/> whose ACE chance on the CURRENT War Magic <paramref name="skill"/> (the buffed value
    /// the fight reads, <see cref="GearCasts.TryCurrentSkill"/>) is at least <paramref name="planChance"/> (the ladder's <see cref="Ladder.PlanCastChance"/>, <see cref="SpellPlan.LevelAt(uint, double, IReadOnlyList{uint})"/>); the
    /// cap is that level, never under the rung's plan (<see cref="LadderSpellLevel.Min"/>, computed on the projected base skill), and the result is the highest level at or under the cap that the spellbook holds, or 0 with the
    /// reason when it holds none. With no current skill (null) the cap is the rung's plan <see cref="LadderSpellLevel.Max"/> and <paramref name="live"/> is 0. The fight's own <c>ChooseLevel</c> then casts at the same chance under it.
    /// </summary>
    /// <param name="rung">The rung.</param>
    /// <param name="line">The bolt line (<see cref="SpellPlan.BoltLine"/>), I first.</param>
    /// <param name="knows">Whether the spellbook holds a spell.</param>
    /// <param name="skill">The current War Magic skill, or null when it is not read.</param>
    /// <param name="planChance">The ladder's plan chance.</param>
    /// <param name="live">The level the rule gives on the current skill (0: none, or no skill).</param>
    /// <param name="why">What chose it.</param>
    public static int BoltLevel(LadderRung rung, IReadOnlyList<ProfileSpell> line, Func<uint, bool> knows, uint? skill, double planChance, out int live, out string why)
    {
        ArgumentNullException.ThrowIfNull(rung);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(knows);
        var c = CultureInfo.InvariantCulture;
        live = skill is { } current ? SpellPlan.LevelAt(current, planChance, [.. line.Select(s => s.Power)]) : 0;
        var rule = skill is null ? rung.SpellLevel.Max : Math.Max(live, rung.SpellLevel.Min);
        var reason = skill is { } s2
            ? string.Create(c, $"the rule's cap {rule}: live {live} (the highest level at {planChance} on the current War Magic {s2}), never under the rung's plan {rung.SpellLevel.Min}-{rung.SpellLevel.Max}")
            : string.Create(c, $"the rung's plan {rung.SpellLevel.Min}-{rung.SpellLevel.Max} (the current War Magic is not read)");
        var cap = Math.Min(rule, line.Count);
        for (var level = cap; level >= 1; level--)
        {
            if (knows(line[level - 1].Id))
            {
                why = string.Create(c, $"level {level}: the highest the spellbook holds within {reason}");
                return level;
            }
        }

        why = string.Create(c, $"the spellbook holds no level of the line within {reason}");
        return 0;
    }

    /// <summary>The creature levels the character hunts on <paramref name="ground"/>: the ground's band, its top lowered to the character's level + the rung's <paramref name="margin"/> (a band whose top is under its bottom is empty).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="margin">The rung's <c>target_margin</c> (M6 0 F5, 0-R5: a rung's fact, never a constant).</param>
    public static (int Min, int Max) TargetBand(HuntingGround ground, int level, int margin)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return (ground.LevelBand.Min, Math.Min(ground.LevelBand.Max, level + margin));
    }

    /// <summary>True when a creature of <paramref name="creatureLevel"/> is skipped by a character of <paramref name="level"/> (above the level + <paramref name="margin"/>).</summary>
    /// <param name="creatureLevel">The creature's level.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="margin">The rung's <c>target_margin</c>.</param>
    public static bool TargetSkipped(int creatureLevel, int level, int margin) => creatureLevel > level + margin;

    /// <summary>The names of the profile's lines of which the spellbook holds no level, in the profile's order.</summary>
    /// <param name="lines">The buff profile's lines.</param>
    /// <param name="knows">Whether the spellbook holds a spell.</param>
    public static IReadOnlyList<string> MissingLines(IEnumerable<ProfileLine> lines, Func<uint, bool> knows)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(knows);
        return lines.Where(l => !l.Spells.Any(s => knows(s.Id))).Select(l => l.Name).ToArray();
    }

    /// <summary>
    /// The cycle's start in the level up job (check, act, check, M5 D141): before: the level and the rung read; the act: the ladder's decision (hunt on, or the named stop: a move-on, a quest or pending rung, a ground the fight
    /// refuses); after: the rung held on the run's ground. A move-on's after-check is the next run's first check: its ground must be the new rung's, else <c>failed:rung-ground-mismatch:&lt;rung&gt;</c> (the recovery: the host builds the
    /// run on <see cref="RungOwner"/>'s ground).
    /// </summary>
    private ScenarioStatus? LadderCycleStart(IGameSessionView session)
    {
        if (VitaeBurnCheck(session) is { } corpse)
        {
            return corpse; // M6 sprint: the burn is over and the owed corpse lies on this run's ground: the death station fetches it before the check
        }

        var ladder = _ladder ??= LadderLoader.WarMage;
        var level = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        LadderJobNeeds(level); // M6 14: what the ladder errands of her band need (a roll's skill, a hunt's bolt line), before the spend reads the credit order
        if (LadderSpend(session, level) is { } spending)
        {
            return spending; // M6 1 b: train, then spend, at a level-up and at the run's first check, before the rung is decided (so a pending or quest rung's stop still trains and spends)
        }

        if (LadderQuestsPending(session, ladder, level))
        {
            return ScenarioStatus.Running; // M6 0 F4: the quest rung's `quest has` reads are in flight; the cycle's check is made when they are in
        }

        if (LadderErrandAtCycle(level) is { } errand)
        {
            return errand; // M6 sprint (m6-errand-at-cycle): a ladder errand due in her band is called at the cycle's check too, not only on a way home
        }

        if (GateRecastFirst(session, ladder, level) is { } recast)
        {
            return recast; // M6 14 gate lapse: a gate line down that she can recast herself is recast first; the pass's end comes back here and the gates are read again
        }

        _ladderCycle = _cycle;
        _ladderQuestAsked.Clear();
        var first = !_ladderChecked;
        _ladderChecked = true;
        var before = _rung;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: check before (cycle {_cycle}): level={level} rung={before?.Key ?? "none"} ground={_ground.Key}{(first ? " (the run's first check)" : string.Empty)}"));
        var choice = AskRung(session, ladder, level, "the cycle's check");
        _ladderMidRun = true; // M6 14 gate lapse: from here the run's ground's guard is not read again until a way out
        var decision = choice.Decision;
        _ladderReason = decision.Reason;
        _trialCycle = choice.Trial is { Steers: true } && decision.Kind == LadderStepKind.Hunt ? choice.Trial.Ground : null; // M6 item 14: this cycle is the trial's: its [ladder] line says trial=<ground>
        if (decision.Kind == LadderStepKind.Moved && first && !choice.Opened && choice.Trial is not { Steers: true } && choice.KillTask is not { Steers: true })
        {
            // M6 7 AW: a gate the host could not read (no buff is known before a run) that this check reads open is a move-on: buffs still up across a restart
            return Stop("failed:rung-ground-mismatch:" + decision.Rung!.Key, ok: false, decision.Why + "; the run was built on the wrong ground (recovery: the host builds the level up run on the rung's ground, RungOwner)");
        }

        if (decision.Stop is { } stop)
        {
            if (decision.Kind == LadderStepKind.Moved && (choice.Trial is { Steers: true } || _options.Trial is { Phase: LadderTrialPhase.Ended or LadderTrialPhase.Refused } && string.Equals(_ground.Key, _options.Trial.Ground, StringComparison.Ordinal)))
            {
                // M6 item 14 (the trial switch): the rung change to the trial's ground, or back to the ladder's own rung after it: no move-on level was reached
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level}: {(choice.Trial is { Steers: true } ? "the trial's rung change" : $"the trial of {_ground.Key} is over ({_options.Trial!.Why}): back to the ladder's own rung")}: {decision.Why}; the after-check is the next run's first check"));
            }
            else if (decision.Kind == LadderStepKind.Moved && (choice.KillTask is { Steers: true } || _options.KillTasks.Any(t => string.Equals(t.Ground, _ground.Key, StringComparison.Ordinal))))
            {
                // M6 14 guard task: the rung change to the kill task's ground, or back to her rung's own after it (turned in, on its timer, or held): no move-on level was reached
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level}: {(choice.KillTask is { Steers: true } ? "the kill task's rung change" : "the kill task's hunt on " + _ground.Key + " is over: back to her rung's own ground")}: {decision.Why}; the after-check is the next run's first check"));
            }
            else if (decision.Kind == LadderStepKind.Moved && decision.Reason == AheadReason)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level}, ahead of the band (D135: {RungOwner.AheadLine(ladder, level, session.State.Self.Qualities, PlanWarForTest ?? SpellPlan.PlanWarOf(session.XpTable))}): {decision.Why}; the after-check is the next run's first check"));
            }
            else if (decision.Kind == LadderStepKind.Moved)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level} reached {before?.Key ?? "the rung"}'s move-on level ({before?.MoveOn.Level.ToString(CultureInfo.InvariantCulture) ?? "?"}): {decision.Why}; the after-check is the next run's first check"));
            }

            return Stop(stop, decision.Ok, decision.Why);
        }

        _rung = decision.Rung;
        var rung = _rung!;
        _log.Info(LogCategory, string.Create(
            CultureInfo.InvariantCulture,
            $"ladder: check after (cycle {_cycle}): level={level} rung={rung.Key} band={rung.Band.From}-{rung.Band.To} ground={_ground.Key} move_on_level={rung.MoveOn.Level} ({rung.MoveOn.Level - level} to go){(first ? ": the run's ground is the rung's" : before is not null && before.Key != rung.Key ? ": the rung changed on the same ground" : string.Empty)}; the kills-an-hour floor {rung.MoveOn.KillsAnHourFloor?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} and the casts-a-kill ceiling {rung.MoveOn.CastsAKillCeiling?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a"} are only logged (D27, TODO(m6-ladder-move-on)); ahead of the band (D135): {(decision.Reason == AheadReason ? "on it, " : string.Empty)}{RungOwner.AheadLine(ladder, level, session.State.Self.Qualities, PlanWarForTest ?? SpellPlan.PlanWarOf(session.XpTable))}"));
        return LadderNotes(session, ladder, rung, level);
    }

    private readonly HashSet<string> _ladderQuestSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ladderQuestAsked = new(StringComparer.Ordinal);
    private ToolHandle<QuestResult>? _ladderQuestAsk;
    private string? _ladderQuestAskKey;
    private string? _ladderReason;

    /// <summary>Whether the character has done the quest of <paramref name="key"/>: a station saw it done this journey (<see cref="JourneyFacts.QuestDone"/>) or the server's <c>quest has</c> said its done stamp is held (M6 0 F4).</summary>
    private bool LadderQuestDone(string key) => _context.Facts.QuestDone(key) || _ladderQuestSeen.Contains(key) || EarlierQuestDoneByLater(key, LadderQuestDone);

    /// <summary>
    /// M6 0 lead (live round 4's sequel): a quest of a quest rung that keeps no done stamp (the Academy's exit erases its stamps, so a fresh process cannot read it) counts as done when a LATER quest of
    /// the same rung that keeps one is done: the rung's quests run in the file's order and the later one starts where the earlier one ends (pathwarden.json's start is Holtburg, "where the Academy's exit
    /// arrives"). A quest that keeps a stamp is never inferred.
    /// </summary>
    /// <param name="key">The quest's key.</param>
    /// <param name="done">Whether a quest is done (the caller's rule, applied to the later quests only).</param>
    public static bool EarlierQuestDoneByLater(string key, Func<string, bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        if (QuestCatalog.All.FirstOrDefault(q => q.Key == key) is not { } quest || quest.Done.Stamp is not null)
        {
            return false;
        }

        var rung = LadderLoader.WarMage.Rungs.FirstOrDefault(r => r.Kind == LadderRungKind.Quest && r.QuestKeys.Contains(key));
        if (rung is null)
        {
            return false;
        }

        var later = rung.QuestKeys.SkipWhile(k => k != key).Skip(1).Where(k => QuestCatalog.All.FirstOrDefault(q => q.Key == k)?.Done.Stamp is not null).ToList();
        return later.Count > 0 && later.Any(done);
    }

    /// <summary>
    /// M6 0 F4, check before the rung is chosen: while the level's band is a quest rung, each of its quests that no station saw done and that keeps a done stamp is asked once through the tools' <c>quest has</c> (a read: <see cref="ModVerbCounter"/> does not count it as a
    /// grant), one at a time; the answer, held or not, is logged. A quest with no done stamp (the Academy's exit erases its stamps) is only what a station saw; a read the server did not confirm counts as not done and says so. True while a read is in flight.
    /// </summary>
    private bool LadderQuestsPending(IGameSessionView session, Ladder ladder, int level)
    {
        if (level < 1 || level > ladder.TopLevel || ladder.RungFor(level) is not { Kind: LadderRungKind.Quest } rung)
        {
            return false;
        }

        if (_ladderQuestAsk is { } ask)
        {
            if (ask.Outcome is not { } outcome)
            {
                return true; // the tool's own limit (ActionPolicy.ToolSeconds) ends the wait
            }

            var asked = _ladderQuestAskKey!;
            _ladderQuestAsk = null;
            if (outcome.Kind == ActionOutcomeKind.Confirmed && ask.Result is { } found)
            {
                _log.Info(LogCategory, $"ladder: quest {asked}: `quest has` {found.Name}: held={(found.Held ? "yes" : "no")} completions={found.Completions.ToString(CultureInfo.InvariantCulture)}");
                if (found.Held)
                {
                    _ladderQuestSeen.Add(asked);
                }
            }
            else
            {
                _log.Warn(LogCategory, $"ladder: quest {asked}: `quest has` was not confirmed ({outcome.Kind}); the quest counts as not done (rung-quest:{rung.Key} stays the stop)");
            }
        }

        string? StampOf(string key) => QuestCatalog.All.FirstOrDefault(q => q.Key == key)?.Done.Stamp;
        bool LaterKeepsStamp(string key) => rung.QuestKeys.SkipWhile(k => k != key).Skip(1).Any(k => StampOf(k) is not null);
        foreach (var key in rung.QuestKeys.Where(k => !LadderQuestDone(k) && StampOf(k) is null && !LaterKeepsStamp(k)))
        {
            if (_ladderQuestAsked.Add("unreadable:" + key))
            {
                _log.Info(LogCategory, $"ladder: quest {key}: no done stamp to read (its exit erases the stamps, or it has no quest file) and no station saw it done this journey: not done, so rung-quest:{rung.Key} stays the stop and no `quest has` is asked for the others");
            }

            return false;
        }

        foreach (var key in rung.QuestKeys.Where(k => StampOf(k) is not null))
        {
            if (LadderQuestDone(key) || !_ladderQuestAsked.Add(key))
            {
                continue;
            }

            _ladderQuestAskKey = key;
            _ladderQuestAsk = session.Tools.HasQuest(StampOf(key)!, ActionSource.Automation);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The bolt cap, the target band and the buff lines the spellbook lacks for this cycle's rung; each missing line and each unknown is logged once by name. M6 item 1 a: the rung's element names its bolt line
    /// (<see cref="SpellPlan.BoltLine"/>; closes <c>m6-ladder-element-line</c>): a line the ladder cannot name stops the run (<c>failed:rung-bolt-line:&lt;rung&gt;</c>), with no recall (nothing was fought). M6 14: the fight casts every
    /// element's line (the stop <c>rung-element-line</c> is gone with <c>TODO(m6-fight-element-line)</c>). The cap is D41's rule on the current
    /// War Magic (<see cref="BoltLevel"/>), and the ladder's plan chance reaches the fight (<see cref="LadderFightSpec"/>).
    /// </summary>
    private ScenarioStatus? LadderNotes(IGameSessionView session, Ladder ladder, LadderRung rung, int level)
    {
        var book = session.State.Self.Spellbook;
        _ladderBolt = 0; // M6 0 a5: no cap until this cycle's rung gives one
        _ladderChance = null;
        if (SpellPlan.BoltLine(rung.Element, _context.Spells, out var lineWhy) is not { } line)
        {
            return Stop("failed:rung-bolt-line:" + rung.Key, ok: false, $"rung {rung.Key}'s element '{rung.Element ?? "none"}' names no bolt line: {lineWhy}");
        }

        var lineName = SpellPlan.BoltLineNames[rung.Element!]; // a line was found, so the element is one of the vocabulary's
        // M6 14 the element line (TODO(m6-fight-element-line) closed): the fight casts every element's line, at each target the one that hits it hardest (FightScenario.Element.cs), so a rung's element is no stop

        var c = CultureInfo.InvariantCulture;
        var q = session.State.Self.Qualities;
        uint? skill = GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var current, out var skillWhy) ? current : null; // the fight's own read (FightScenario.ChooseLevel)
        var bolt = BoltLevel(rung, line, book.Knows, skill, ladder.PlanCastChance, out var live, out var why);
        _ladderBolt = bolt; // M6 0 a5: the fight's cap (LadderFightSpec)
        _ladderChance = ladder.PlanCastChance; // M6 1 a (D41): the fight casts at the plan's chance on the current skill
        var chance = skill is { } s && bolt > 0 ? SpellPlan.Chance(s, line[bolt - 1].Power).ToString("0.0000", c) : "n/a";
        _log.Info(LogCategory, string.Create(
            c,
            $"ladder: bolt: {rung.Element} {lineName} plan={rung.SpellLevel.Min}-{rung.SpellLevel.Max} live={(skill is null ? "n/a" : live.ToString(c))} skill={(skill is { } s3 ? s3.ToString(c) : "n/a")} chance={chance} plan_chance={ladder.PlanCastChance.ToString(c)} cap={bolt}: {why}{(skill is null ? "; the War Magic skill is not read: " + skillWhy : string.Empty)}{(bolt == 0 ? " (the fight stops by its own rule when it has no level to cast)" : string.Empty)}"));

        _ladderMargin = rung.TargetMargin; // M6 0 F5 (0-R5): the rung's margin reaches the fight (LadderFightSpec) and this line
        if (rung.TargetMargin is { } margin)
        {
            var (min, max) = TargetBand(_ground, level, margin);
            var skipped = _ground.Outdoor is { } outdoor ? outdoor.Creatures.Where(c => c.Level >= _ground.LevelBand.Min && TargetSkipped(c.Level, level, margin)).Select(c => c.Name).Distinct().ToArray() : [];
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: targets: levels {min}-{max} of the ground's {_ground.LevelBand.Min}-{_ground.LevelBand.Max} (level {level} + {margin}, the rung's target_margin){(skipped.Length == 0 ? string.Empty : "; skipped: " + string.Join(", ", skipped))}"));
        }
        else if (_ladderNoted.Add("margin:" + rung.Key))
        {
            _log.Warn(LogCategory, $"ladder: targets: rung {rung.Key} carries no target_margin (TODO(m6-rung-target-margin)); the fight's pick is not banded by the character's level");
        }

        if (!book.IsKnown)
        {
            return null; // the missing lines are read once the spellbook is
        }

        if (BuffProfiles.For(_context.Character) is not { } profile)
        {
            if (_ladderNoted.Add("profile"))
            {
                _log.Warn(LogCategory, $"ladder: buff: {_context.Character} has no buff profile (TODO(m6-ladder-buff-profile)); the buff station has no lines to cast");
            }

            return null;
        }

        foreach (var name in MissingLines(profile.Lines, book.Knows))
        {
            if (_ladderNoted.Add("line:" + name))
            {
                _log.Info(LogCategory, $"ladder: buff: the spellbook holds no level of '{name}': not cast until a level is learned (logged once)");
            }
        }

        return null;
    }

    /// <summary>The walking clock (<c>m6-walk-cancel-seconds</c>): one window apart, a pace between <see cref="WalkMinMetresPerSecond"/> and <see cref="WalkMaxMetresPerSecond"/> counts the window, whoever moved the legs.</summary>
    private void SampleWalk(IGameSessionView session)
    {
        var self = session.State.Self;
        if (session.State.Phase != SessionPhase.InWorld || self.Travel != SelfTravel.Present || self.Position is not { } at)
        {
            _walkFrom = null;
            return;
        }

        if (_walkFrom is not { } from)
        {
            _walkFrom = at.ToGlobal();
            _walkFromClock = _clock;
            return;
        }

        var span = _clock - _walkFromClock;
        if (span < WalkWindowSeconds)
        {
            return;
        }

        var here = at.ToGlobal();
        var pace = here.DistanceTo(from) / span;
        if (span <= 3 * WalkWindowSeconds && pace >= WalkMinMetresPerSecond && pace <= WalkMaxMetresPerSecond)
        {
            _walkCycleSeconds += span;
        }

        _walkFrom = here;
        _walkFromClock = _clock;
    }

    /// <summary>The <c>[ladder]</c> line of the cycle that closes (<paramref name="tally"/>) or of the run (<c>done</c>); the cycle's walking seconds fold into the run's.</summary>
    private void EmitLadder(string at, AutopilotTally tally)
    {
        if (_options.Job != AutopilotJob.LevelUp)
        {
            return;
        }

        int? level = _session is { } s && s.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var l) ? l : null;
        uint? health = _session is { } h && h.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) && max > 0 ? max : null; // M6 7 R: report-only (D27)
        if (at == "cycle" && tally.Seconds > 0 && _rung is { } own && (string.Equals(_ground.Key, own.GroundKey, StringComparison.Ordinal) || own.Alternates.Any(a => string.Equals(a.GroundKey, _ground.Key, StringComparison.Ordinal))) && _trialCycle is null && !_options.KillTasks.Any(t => string.Equals(t.Ground, _ground.Key, StringComparison.Ordinal)))
        {
            (_rungXp, _rungSeconds) = (_rungXp + tally.Xp, _rungSeconds + tally.Seconds); // M6 audit (fix 3): the rate a later kill-task round is weighed against
        }

        if (at == "cycle")
        {
            Emit(new LadderLine(at, _cycle, level, _rung?.Key, _ground.Key, tally, _walkCycleSeconds, _rung?.MoveOn, CastsAKill(_castsCycle, _castsCycleKills), _ladderReason, LadderQuestsDone(), LevelUpSpend.TrainedField(_ladderTrainedCycle), _turnInsCycle, health, _errandsCycle, _survivalHoldsCycle, _survivalLeavesCycle, _lifestoneRecallsCycle, _options.BuffBot, _trialCycle).Text());
            _trialRun ??= _trialCycle; // M6 item 14: a run that fought a cycle of the trial says so on its done line
            _trialCycle = null;
            _turnInsCycle = 0;
            _errandsCycle = 0;
            (_survivalHoldsCycle, _survivalLeavesCycle, _lifestoneRecallsCycle) = (0, 0, 0);
            _walkTotalSeconds += _walkCycleSeconds;
            _walkCycleSeconds = 0;
            (_castsCycle, _castsCycleKills) = (null, 0);
            _ladderTrainedCycle.Clear();
            return;
        }

        Emit(new LadderLine(at, _cycles, level, _rung?.Key, _ground.Key, tally, WalkSeconds, _rung?.MoveOn, CastsAKill(_castsTotal, _castsTotalKills), _ladderReason, LadderQuestsDone(), LevelUpSpend.TrainedField(_ladderTrainedRun), _turnInsRun, health, _errandsRun, _survivalHoldsRun, _survivalLeavesRun, _lifestoneRecallsRun, _options.BuffBot, _trialRun ?? _trialCycle).Text());
    }

    private double _rungXp;
    private double _rungSeconds;

    /// <summary>M6 audit (fix 3): this run's experience an hour over its cycles on the rung's own ground (or its alternate; never the kill task's ground or a trial's), or null before one such cycle closed.</summary>
    private double? RungXpPerHour() => _rungSeconds > 0 ? _rungXp * 3600.0 / _rungSeconds : null;

    /// <summary>The ladder's quests (the keys of its quest rungs) the job knows to be done (<see cref="LadderQuestDone"/>), for the <c>[ladder]</c> line's <c>quests_done=</c> (M6 0 F5, 0-R6).</summary>
    private int LadderQuestsDone() => (_ladder ??= LadderLoader.WarMage).Rungs.Where(r => r.Kind == LadderRungKind.Quest).SelectMany(r => r.QuestKeys).Distinct(StringComparer.Ordinal).Count(LadderQuestDone);

    // ------------------------------------------------------------------------------------------------ M6 item 1 b: train, then spend, at a level-up

    private LevelUpSpend? _ladderSpend;
    private int _ladderSpentLevel;
    private bool _ladderOrderRead;
    private IReadOnlyList<CreditLine>? _ladderCreditOrder;
    private MageTemplate.ClimberCreationPlan? _ladderCreation; // M6 14 D136: the creation's plan, so each spend's order carries the jobs' needs
    private readonly List<uint> _ladderTrainedCycle = [];
    private readonly List<uint> _ladderTrainedRun = [];

    /// <summary>The <c>trained=</c> stop of a train that failed: <c>failed:rung-train:&lt;skill&gt;</c> (a ladder stop before any station: no recall first).</summary>
    /// <param name="skill">The skill whose train failed.</param>
    public static string TrainStop(uint skill) => "failed:rung-train:" + LevelUpSpend.TrainedField([skill]);

    /// <summary>The stop of a refused raise at the level-up spend.</summary>
    public const string SpendStop = "failed:rung-spend";

    /// <summary>
    /// M6 1 b: the level-up spend at the cycle's start (between fights: the previous station has ended, no cast is in flight). It runs at the run's first check, at each level-up the mid-run spend did not take (<see cref="LadderMidRunSpend"/>) and
    /// when a mid-run spend was left owed, before the rung is decided, so a climber that stands at a pending or a quest rung still trains and spends. Train, then spend (<see cref="LevelUpSpend"/>): the credit order from the creation's plan read
    /// from the dats (<see cref="ClimberCreateScenario.PlanFromDats"/>, <see cref="SkillCreditPlan.OrderOf"/>), the experience by <see cref="XpSpendRule"/> with the ground's <c>burden_trip_percent</c> as the Strength floor's line. The spend's
    /// experience joins the cycle's tally (<c>spent_xp=</c>) and its skills the <c>[ladder]</c> line's <c>trained=</c>. A failed train stops the run by name (<see cref="TrainStop"/>), a refused raise too (<see cref="SpendStop"/>);
    /// neither is retried. Running while the spend runs; null when there is nothing to do or it is done.
    /// </summary>
    private ScenarioStatus? LadderSpend(IGameSessionView session, int level)
    {
        if (_ladderSpendMidRun)
        {
            return ScenarioStatus.Running; // the fight ended under a mid-run spend: Step ticks it (its own bounds end it); the cycle's check waits for the character it leaves
        }

        if (_ladderSpend is { } spend)
        {
            if (!spend.Tick())
            {
                return ScenarioStatus.Running;
            }

            var failed = LadderSpendEnded(spend, level, "the cycle's start");
            if (failed is { } why)
            {
                return Stop(spend.FailedSkill is { } skill ? TrainStop(skill) : SpendStop, ok: false, why);
            }

            _ladderSpentLevel = level;
            return null;
        }

        if (level <= 0 || (level <= _ladderSpentLevel && !_ladderSpendOwed))
        {
            return null;
        }

        var from = _ladderSpentLevel;
        var owed = _ladderSpendOwed && level <= _ladderSpentLevel;
        _ladderSpentLevel = level; // one spend a level, whatever its end (a failure stops the run)
        if (!BeginLadderSpend(session, level, owed ? "owed by the mid-run spend" : from == 0 ? "the run's first check" : string.Create(CultureInfo.InvariantCulture, $"up from {from}")))
        {
            return null;
        }

        return LadderSpend(session, level);
    }

    /// <summary>Starts a <see cref="LevelUpSpend"/> into <see cref="_ladderSpend"/>; false (logged by name) when no XpTable prices the experience.</summary>
    private bool BeginLadderSpend(IGameSessionView session, int level, string why)
    {
        if (XpSpendRule.TableOf(session, _context.Dats) is not { } table)
        {
            _ladderSpendOwed = false;
            _log.Warn(LogCategory, $"ladder: level-up spend: TODO(m5-logistics-xptable): the session has no XpTable and the journey no dats, so the experience cannot be priced: nothing is trained or spent at level {level}");
            return false;
        }

        if (!_ladderOrderRead)
        {
            _ladderOrderRead = true;
            _log.Info(LogCategory, "ladder: " + MageTemplate.ClimberOrderLine(_options.BuffBot)); // M6 item 14 S3: once, at the run's first check: which order is in force and why
            if (_context.Dats is { } dats)
            {
                var creation = ClimberCreateScenario.PlanFromDats(dats, _options.BuffBot); // M6 item 14 S3: the order is the buff bot's option's (a character keeps what she trained: the plan skips it)
                _ladderCreation = creation;
                _ladderCreditOrder = SkillCreditPlan.OrderOf(creation, JobNeedsSkill);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: credits: the creation left {creation.Left} of {creation.Credits}; the order from its unpaid lines; after it the credits bank (M6 14 D136: Alchemy only when a job needs it: a ladder errand of her band whose roll names it, LadderJobNeeds): {string.Join(", ", _ladderCreditOrder.Select(l => $"{MageTemplate.SkillName(l.Skill)} {l.Cost}"))}"));
                foreach (var line in _ladderCreditOrder.Where(l => session.State.Self.Qualities.TryGetSkill(l.Skill, out var held) && held.AdvancementClass >= SkillRules.TrainedClass))
                {
                    _log.Info(LogCategory, $"ladder: credits: {MageTemplate.SkillName(line.Skill)} is trained already (a character keeps what she trained): the order skips it");
                }
            }
            else
            {
                _log.Warn(LogCategory, "ladder: credits: TODO(m6-credit-plan-dats): the journey has no dats, so the creation's credit order (the SkillTable's costs) cannot be read: nothing is trained this run, the experience is still spent");
            }
        }

        if (_ladderCreation is { } plan)
        {
            var order = SkillCreditPlan.OrderOf(plan, JobNeedsSkill); // M6 14 D136: a job's need joins the order at each spend (the jobs are read at the cycle's start, LadderJobNeeds)
            if (_ladderCreditOrder is null || !order.SequenceEqual(_ladderCreditOrder))
            {
                _log.Info(LogCategory, $"ladder: credits: the order now: {string.Join(", ", order.Select(l => $"{MageTemplate.SkillName(l.Skill)} {l.Cost}"))} (a job's need: {(_jobSkills.Count == 0 ? "none" : string.Join(", ", _jobSkills.Select(MageTemplate.SkillName)))})");
            }

            _ladderCreditOrder = order;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: level-up spend: level {level} ({why}): train, then spend, before the rung is decided; Strength floor line: the ground's burden_trip_percent {_ground.Supplies.BurdenTripPercent}"));
        var ground = _ground;
        var started = new LevelUpSpend(table, _ladderCreditOrder, _ground.Supplies.BurdenTripPercent, _log, BuffBotApplies, LadderCasterFacts, LadderLoreFacts, s => CantripRoom.SellLootBurden(s, ground, _context.Character, _context.Spells), LadderRecipeFacts, () => _rung); // M6 audit: the health floor reads the rung she hunts // M6 14 hour load: the Strength floor leaves out the sell loot she carries // M6 item 14: the Strength floor counts the worn armour's own line and the bot's (StandingStrength); M6 14 (m6-caster-spend): a held caster's points first; M6 14 (m6-lore-spend): her Arcane Lore toward a held piece's ItemDifficulty
        started.Begin(session);
        _ladderSpend = started;
        return true;
    }

    private readonly Dictionary<string, double> _loreAsked = new(StringComparer.Ordinal); // M6 14 own lore: each piece's ask, once in LoreAskSeconds
    private int? _loreBefore; // M6 14 own lore: her Arcane Lore when a lore-asked spend began (the check after reads it again)

    /// <summary>M6 14 own lore: the seconds before the same piece's wait on her Arcane Lore may start the spend again (Lifestone's own bound: a spend that bought nothing is not run each corpse).</summary>
    public const double LoreAskSeconds = 600;

    /// <summary>
    /// M6 14 own lore (the owner, 2026-11-17: raise lore to wear): a find or a worn piece the loot step judged waits on her Arcane Lore: the spend runs now, the fight running, as the mid-run spend of a level-up does, so its Arcane Lore rule
    /// (<see cref="LoreSpend"/>) raises the lore toward it from the experience she holds, not only at the next level-up. Check before: the job's own run, the first check made, no spend running, unassigned experience held, the same piece not
    /// asked within <see cref="LoreAskSeconds"/>; logged with her lore now and on her own. Check after: <see cref="LadderSpendEnded"/> reads her lore again.
    /// </summary>
    /// <param name="why">The piece and its need.</param>
    private void LadderLoreWanted(string why)
    {
        if (_session is not { } session || _options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job || !_ladderChecked || _ladderSpend is not null
            || (_loreAsked.TryGetValue(why, out var at) && _clock - at < LoreAskSeconds))
        {
            return;
        }

        _loreAsked[why] = _clock;
        var q = session.State.Self.Qualities;
        var level = q.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        var now = new QualitiesSkills(q).Current(MageTemplate.ArcaneLore);
        _ = StandingLore.OwnOf(q, session.State.Self.Spellbook.Knows, out var own);
        if (q.AvailableExperience <= 0 || level <= 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: lore: {why}: no unassigned experience to raise it (her pool {q.AvailableExperience:N0}); check: {own}"));
            return;
        }

        _loreBefore = now;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: lore: {why}: the spend runs now for her Arcane Lore, the fight running; check before: Arcane Lore {now?.ToString(CultureInfo.InvariantCulture) ?? "unread"} now; {own}; {q.AvailableExperience:N0} unassigned"));
        if (BeginLadderSpend(session, level, "a piece waits on her Arcane Lore: " + why))
        {
            _ladderSpendMidRun = true;
        }
    }

    /// <summary>Folds an ended spend into the cycle's tally and the trained lists and logs its fields; its failure's text, or null.</summary>
    private string? LadderSpendEnded(LevelUpSpend spend, int level, string where)
    {
        if (_loreBefore is { } loreBefore && _session is { } held)
        {
            _loreBefore = null;
            var after = new QualitiesSkills(held.State.Self.Qualities).Current(MageTemplate.ArcaneLore);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: lore: check after the spend: Arcane Lore {loreBefore} -> {after?.ToString(CultureInfo.InvariantCulture) ?? "unread"} (the loot step re-wields a worn piece it now lights, and judges a held one again at the next corpse)"));
        }

        _ladderSpend = null;
        _ladderSpendMidRun = false;
        _ladderSpendOwed = false;
        _tally.SpentXp += spend.SpentXp;
        _ladderTrainedCycle.AddRange(spend.Trained);
        _ladderTrainedRun.AddRange(spend.Trained);
        var health = _session is { } s && s.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) ? max.ToString(CultureInfo.InvariantCulture) : "?"; // M6 7 R: the climb's health_by_level= reads this line
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: level-up spend: level {level}: {spend.Fields} health={health} ({where}){(spend.Failure is { } failed ? "; stopped: " + failed : string.Empty)}"));
        CasterPickAfterSpend(level); // M6 14 (m6-caster): the pick read again on the skills the spend left (a caster whose wield requirement the raise met is owed to the next visit)
        return spend.Failure;
    }

    // ------------------------------------------------------------------------------------------------ M6 7 R: the spend soon after each level-up, the fight running

    /// <summary>
    /// The seconds the mid-run spend waits for a frame with nothing of the Magic, Heal, Use or Inventory lanes in flight (<see cref="IActionApi.BusyReason"/>) before it starts anyway. Lifestone's own bound, so a level-up spends within a known time
    /// in the closest fight: a raise cannot hurt a cast (the raise's lane is its own, the cast's chance is read at the fight's Choose and nowhere inside a cast, <c>FightScenario.Choose</c>, <c>CastAttempt</c>; ACE takes a raise at any time,
    /// @95ef426d <c>Player_Attributes.cs</c> <c>HandleActionRaiseAttribute</c>, <c>Player_Vitals.cs</c>, <c>Player_Skills.cs</c>: no busy check), so the wait is a courtesy between casts, never a condition.
    /// </summary>
    public const double LadderSpendCastWaitSeconds = 10;

    /// <summary>The fight's stop that takes a bolt cap the mid-run spend raised (<see cref="LadderCapRose"/>): the fight restarts where it stands at the new cap, no town and no station between.</summary>
    public static FightStopReason BoltCapRose { get; } = new("bolt-cap-rose");

    /// <summary>The fight's stop at the rung's move-on level (<see cref="LadderMoveOnReached"/>): the cycle ends at once and its check takes the next rung, no trip needed.</summary>
    public static FightStopReason MoveOnReached { get; } = new("move-on-reached");

    /// <summary>M6 7 lead (2026-11-06, live, stretch 15): the fight's stop in a lull when no whole pass stands on this book (<see cref="LadderPassOwed"/>): an audit's fight skipped the run's first pass, so the gates stayed shut on lines nobody cast.</summary>
    public static FightStopReason PassOwed { get; } = new("pass-owed");

    private double _ladderPassQuiet; // the clock before which the owed pass is not asked again

    /// <summary>True in a lull when a whole pass is owed: the fight ends, the known state runs the pass (<see cref="LearnedPassRule"/>); read once a minute at most.</summary>
    private bool LadderPassOwed(IGameSessionView session)
    {
        if (_ladderPassQuiet > _clock || _options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job || _rung is null
            || LearnedPassRule.Why(_passBook, session.State.Self.Spellbook.Count) is not { } why)
        {
            return false;
        }

        _ladderPassQuiet = _clock + 60;
        if (_hostiles is { } watch && watch.InReach(_clock, out _))
        {
            return false;
        }

        _log.Info(LogCategory, $"ladder: pass: owed: {why}: no hostile is in reach: the fight ends for it");
        return true;
    }

    private int _ladderMoveOnAsked; // the level the move-on was last asked at: one ask a level, never a loop
    private int _ladderMoveOnWait; // the level whose ask the lull is waiting for, logged once

    private bool _ladderSpendMidRun; // _ladderSpend was started inside the fight: Step ticks it, whatever state the run is in by then
    private bool _ladderSpendOwed; // a mid-run spend failed or was cut: the next cycle's start spends, and only there a refusal is a stop
    private double _ladderSpendSeen = double.NaN; // when the fight first saw a level above the last spent
    private int _ladderCapAsked; // the bolt cap a mid-run spend raised, waiting for a lull; 0: none

    /// <summary>
    /// M6 7 R (the owner's correction, D-record: a spend at each level-up stays the rule): each frame of the fight, a level above the last spent starts the spend at once, the fight running: nothing stops, no cast is cancelled, and the run stays on its
    /// ground, as a person raises a stat in the middle of a fight. Check before: the job's own run, the first check made, no spend running, and the first frame with no busy lane (at most <see cref="LadderSpendCastWaitSeconds"/>). The act:
    /// <see cref="BeginLadderSpend"/>; <see cref="LadderSpendTick"/> steps it. Check after: its outcome (<see cref="LadderSpendTick"/>), and the fight's Choose re-reads the raised skills (its signature holds the War Magic, Focus and Self ranks and the level,
    /// <c>FightScenario.SignatureChanged</c>); the bolt cap, which the fight holds as an option, by <see cref="LadderCapRose"/>.
    /// </summary>
    private void LadderMidRunSpend(IGameSessionView session)
    {
        if (_options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job || !_ladderChecked || _ladderSpend is not null || !session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) || level <= _ladderSpentLevel)
        {
            return;
        }

        if (double.IsNaN(_ladderSpendSeen))
        {
            _ladderSpendSeen = _clock;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: level-up: level {level} (spent to {_ladderSpentLevel}) in the fight: the spend runs now, the fight running; check before: busy={JourneyVerdict.Token(session.Actions.BusyReason ?? "none")}"));
        }

        if (session.Actions.BusyReason is not null && _clock - _ladderSpendSeen < LadderSpendCastWaitSeconds)
        {
            return;
        }

        var from = _ladderSpentLevel;
        var waited = _clock - _ladderSpendSeen;
        _ladderSpentLevel = level; // one spend a level, whatever its end
        _ladderSpendSeen = double.NaN;
        if (BeginLadderSpend(session, level, string.Create(CultureInfo.InvariantCulture, $"up from {from}, mid-run after {waited:0.0} s")))
        {
            _ladderSpendMidRun = true;
        }
    }

    /// <summary>
    /// Steps a mid-run spend each frame in the world, in any state (a fight that ended under it must not orphan a raise). A refusal here never stops the run (the rule of the slice: a transient refusal costs nothing but the wait): it is logged by name, the
    /// fight goes on, and the spend is owed to the next level-up or the next cycle's start, where <see cref="LadderSpend"/>'s named stops stand. Each spend is one try.
    /// </summary>
    private void LadderSpendTick(IGameSessionView session)
    {
        if (!_ladderSpendMidRun || _ladderSpend is not { } spend || !spend.Tick())
        {
            return;
        }

        var level = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        if (LadderSpendEnded(spend, level, "mid-run") is { } why)
        {
            _ladderSpendOwed = true;
            _log.Warn(LogCategory, $"ladder: level-up spend: the mid-run spend failed and the run goes on: it is owed to the next level-up or the next cycle's start, whose refusal is the named stop: {why}");
            return;
        }

        LadderCapAfterSpend(session);
    }

    /// <summary>A spend in flight is cut (a drop, a death, an abandon, a watchdog): its confirmed raises count, and the next cycle's start spends again.</summary>
    private void LadderSpendCut()
    {
        if (_ladderSpend is not { } spend)
        {
            return;
        }

        _ladderSpend = null;
        _ladderSpendMidRun = false;
        _ladderSpendOwed = true;
        _tally.SpentXp += spend.SpentXp;
        _log.Warn(LogCategory, $"ladder: level-up spend: cut: {spend.Fields}: the rest is owed to the next cycle's start");
    }

    /// <summary>
    /// The check after a mid-run spend: the fight's Choose reads the new skills by itself, but the cap <see cref="BoltLevel"/> gave it at the cycle's start is an option of the running fight. A cap the raised War Magic lifts (the book holds a higher
    /// level the old skill could not cast at the plan's chance) waits for a lull (<see cref="LadderCapRose"/>).
    /// </summary>
    private void LadderCapAfterSpend(IGameSessionView session)
    {
        if (_rung is not { } rung || _ladderBolt <= 0 || _ladderChance is not { } chance || SpellPlan.BoltLine(rung.Element, _context.Spells, out _) is not { } line
            || !GearCasts.TryCurrentSkill(session.State.Self.Qualities, MageTemplate.WarMagic, out var skill, out _))
        {
            return;
        }

        var bolt = BoltLevel(rung, line, session.State.Self.Spellbook.Knows, skill, chance, out var live, out var why);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: bolt after the spend: War Magic {skill} live={live} cap {_ladderBolt} -> {bolt}: {why}"));
        if (bolt > _ladderBolt)
        {
            _ladderCapAsked = bolt;
        }
    }

    /// <summary>
    /// The ladder's decision when <paramref name="level"/> has reached <paramref name="rung"/>'s move-on level and the cycle's check would change something (another rung, another ground, the top, a named stop), else null: the last rung's own move-on level
    /// (the top, 126, inside its band) hunts on, so it never ends a fight for nothing.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="rung">The rung the run is on.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="groundKey">The run's ground.</param>
    /// <param name="questDone">Whether a quest is done (see <see cref="DecideRung"/>), or null.</param>
    internal static LadderDecision? MoveOnDue(Ladder ladder, LadderRung rung, int level, string groundKey, Func<string, bool>? questDone = null)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(rung);
        if (level < rung.MoveOn.Level)
        {
            return null;
        }

        var decision = DecideRung(ladder, level, groundKey, null, questDone);
        return decision.Kind == LadderStepKind.Hunt && decision.Rung?.Key == rung.Key ? null : decision;
    }

    private double _ladderMoveOnQuiet;
    private bool _gateHeld;
    private readonly Dictionary<string, string> _gateLogged = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 7 AW: every rung read of a run asks <see cref="RungOwner"/> here, with its live buffs (M6 7 AK: each gate's conditions are logged by name, met or not, once and at every change).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="path">Which path asks, for the log.</param>
    /// <param name="leaving">True when she is leaving for the run's ground, so its gate is read.</param>
    /// <param name="probe">M6 14 gate lapse: true for the check before a gate recast (<see cref="GateRecastFirst"/>): the read is logged, but the trial and the kill task are not told of it and the fight's held flag is not set.</param>
    internal RungChoice AskRung(IGameSessionView session, Ladder ladder, int level, string path, bool leaving = false, bool probe = false)
    {
        var buffs = GateBuffs(session);
        var ask = new RungAsk(level, session.State.Self.Position?.CellId, session.State.Self.Qualities, GateKnows(session), buffs) { RunGround = _ground.Key, Leaving = leaving, MidRun = _ladderMidRun, QuestDone = LadderQuestDone, TrophyDue = key => TrophyJobDue(session, key), StandsByAnother = GateStandsNow(), Trial = _options.Trial, PlanWar = PlanWarForTest ?? SpellPlan.PlanWarOf(session.XpTable), KillTask = SteeringKillTask(), Approach = leaving ? ground => ApproachForRung(session, ground, probe) : null, AheadHeld = _options.ApproachHolds.IsHeld, VitaeHeld = _options.RungHoldsReportOnly ? null : _options.ApproachHolds.Vitae.HeldWhy, HoldsReportOnly = _options.RungHoldsReportOnly }; // M6 sprint: the vitae burn's hold; M6 holding at 60: --autopilot-rung-holds=report reads and reports, never holds
        var choice = RungOwner.Decide(ladder, ask);
        foreach (var opened in choice.ApproachOpened)
        {
            if (_options.ApproachHolds.Release(opened))
            {
                _log.Info(LogCategory, $"ladder: {opened}'s approach is judged open at the way out: its hold for this session is released (M6 14 armory)");
            }
        }

        if (ApproachHolds.HeldRungOf(choice.Reason) is { } heldRung && _options.ApproachHolds.Note(heldRung, choice.Reason))
        {
            _log.Info(LogCategory, $"ladder: {heldRung} is held by its approach for this session ({choice.Reason}): the move-on does not take it ahead of its band again, and a check that is not a way out hunts its alternate or her own ground in its place (M6 14, the move-on loop of 2026-11-14; M6 14 armory)");
        }

        foreach (var (ground, kind) in choice.HeldAlternates)
        {
            if (_options.ApproachHolds.Note(ground, kind + ":" + ground))
            {
                _log.Info(LogCategory, $"ladder: the alternate {ground} is held by its approach for this session ({kind}): a check that is not a way out does not take it in its rung's place (M6 14 armory)");
            }
        }

        if (!probe)
        {
            _options.Trial?.Note(choice.Trial, level, path, _log); // M6 item 14 (the trial switch): its start, its refusal, each return, by name
            KillTaskNoted(choice, level, path); // M6 14 guard task: its refusal by name, a shut gate's hold, its steer
            _gateHeld = choice.HeldLevel != level;
        }

        if (!probe && choice.RunGuardUnread is { } guarded && !string.Equals(_runGuardLogged, guarded + "@" + _ground.Key, StringComparison.Ordinal))
        {
            _runGuardLogged = guarded + "@" + _ground.Key;
            var guard = RungOwner.KillTaskGate(ladder, _ground.Key)?.Rung ?? guarded;
            _log.Info(LogCategory, $"ladder: gate {guarded}: not read mid-run ({path}): {_ground.Key}, the ground of this run, is guarded by the {guard} gate, read whole at the way out with every gate under it; a check that is not a way out never holds her below the ground she runs on (D93: a run's own gate is read each time she leaves for the ground; M6 14 gate lapse)");
        }

        foreach (var read in choice.Gates)
        {
            var text = string.Join("; ", read.Conditions.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Name}: {(c.Met ? "met" : "NOT MET")} ({c.Read})")));
            if (!_gateLogged.TryGetValue(read.Gate.Rung, out var logged) || text != logged)
            {
                _gateLogged[read.Gate.Rung] = text;
                _log.Info(LogCategory, $"ladder: gate {read.Gate.Rung}: level {level}: {text}");
            }
        }

        if (!probe && choice.Reason != _rungReasonLogged)
        {
            _rungReasonLogged = choice.Reason;
            _log.Info(LogCategory, choice.Line(path, ask));
            RungOwner.WarnLoud(choice, _log, LogCategory); // M6 14 far below: a rung far below her level is never chosen silently (report-only)
        }

        return choice;
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 6): the approach check at the way out for <paramref name="groundKey"/> on her live state (<see cref="Vitals.ApproachCheck.Live"/>: the approach's facts against her health, her armour and live resistance per
    /// damage type, and the time to recall), logged once per verdict change; null when the ground records no approach.
    /// </summary>
    private Vitals.ApproachVerdict? ApproachNow(IGameSessionView session, string groundKey, bool probe)
    {
        // M6 14 approach cost (rules 1 and 2): the verdict carries the crossing's cost on her live state, and a crossing the run leg gave up this run holds by name (never tried again in the same run)
        var abandoned = _context.Facts.ApproachAbandonedOf(groundKey);
        if (abandoned is not null)
        {
            _options.ApproachHolds.NoteAbandoned(groundKey, abandoned); // M6 14: kept for the host's restarts, whose runs begin with new journey facts
        }
        else
        {
            abandoned = _options.ApproachHolds.AbandonedOf(groundKey);
        }
        if (GroundCatalog.TryGet(groundKey) is not { } ground
            || ApproachCrossing.Judge(session.State, ground, _fightLine, _clock, _ladder ??= LadderLoader.WarMage, _context.Spells, _context.Routes, abandoned, out var costWhy, Vitals.CreaturePaces.For(_context.Dats)) is not { } verdict)
        {
            return null;
        }

        var said = verdict.Why + (verdict.Cost?.Why ?? costWhy) + verdict.Abandoned;
        if (!probe && (!_approachLogged.TryGetValue(groundKey, out var logged) || !string.Equals(logged, said, StringComparison.Ordinal)))
        {
            _approachLogged[groundKey] = said;
            var line = "ladder: approach check at the way out: " + verdict.Why + " (M6 14 retreat attackers)";
            if (verdict.Survivable)
            {
                _log.Info(LogCategory, line);
            }
            else
            {
                _log.Warn(LogCategory, line);
            }

            var cost = verdict switch
            {
                { Abandoned: { } why } => $"ladder: approach cost at the way out: {Vitals.ApproachCost.AbandonedPrefix}: the crossing to {groundKey} was given up earlier in this run and is not tried again: {why} (M6 14 approach cost, rule 2)",
                { Cost: { Costly: true } c } => $"ladder: approach cost at the way out: {Vitals.ApproachCost.CostlyPrefix} ({c.Reason}): {c.Why}; {c.Change} (M6 14 approach cost, rule 1)",
                { Cost: { } c } => $"ladder: approach cost at the way out: {c.Why} (M6 14 approach cost, rule 1)",
                _ => $"ladder: approach cost at the way out: not judged: {costWhy} (M6 14 approach cost, rule 1)",
            };
            if (verdict.Holds && verdict.Survivable)
            {
                _log.Warn(LogCategory, cost);
            }
            else
            {
                _log.Info(LogCategory, cost);
            }
        }

        return verdict;
    }

    private readonly Dictionary<string, string> _approachLogged = new(StringComparer.Ordinal);

    /// <summary>M6 holding at 60: the grounds and verdict kinds the report-only line has named (once each).</summary>
    private readonly HashSet<string> _approachReported = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 holding at 60 (the owner, 2026-11-20: the approach check does not seem correct; send her): the way out's approach verdict for the rung owner: <see cref="ApproachNow"/>'s; or, when the approach checks only report
    /// (<see cref="AutopilotOptions.RungHoldsReportOnly"/>), judged and logged as ever but handed on as none, so no approach holds a ground (a verdict that would have held is named once).
    /// </summary>
    private Vitals.ApproachVerdict? ApproachForRung(IGameSessionView session, string groundKey, bool probe)
    {
        var verdict = ApproachNow(session, groundKey, probe);
        if (!_options.RungHoldsReportOnly || verdict is null)
        {
            return verdict;
        }

        if (!probe && verdict.Holds && _approachReported.Add(groundKey + "|" + verdict.Kind))
        {
            _log.Info(LogCategory, $"ladder: approach check at the way out: report only ({AutopilotOptions.RungHoldsOption}=report): {groundKey} is not held, she goes: {verdict.Held}");
        }

        return null;
    }
    private string? _rungReasonLogged;
    private string? _runGuardLogged;
    private int _gateRecastCycle = -1;
    private List<string> _gateRecastLines = [];
    private string? _gateRecastNoted;

    /// <summary>The buffs the gates read: a scripted test's, else the live ones (the autopilot's own buff cycle: the level standing, the level the pass would cast).</summary>
    private RungBuffRead GateBuffs(IGameSessionView session)
    {
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session);
        return BuffsForTest ?? new RungBuffRead(name => _buffCycle.ActiveLevel(name, _clock), _buffCycle.ReachLevel);
    }

    /// <summary>
    /// M6 14 gate lapse (live, 2026-11-13 23:39:35 to 23:49:28 UTC: Bludgeon Bane VI ran out on her armour in a fight-back and stayed down through a logistics trip; at the cycle's check the gates read it NOT MET and held her five rungs
    /// down, though she recast it herself in 7 s once the next fight began). The cycle's check that is not a way out: check, act, check. Check: the gates as the check reads them (<see cref="AskRung"/>'s probe: the trial and the kill
    /// task are not told). Act: a gate line read down that she can recast herself (<see cref="RungGateRule.RecastHerself"/>: in her book, on her spell bar, the mana and the components to hand, at the gate's level) is recast by a pass
    /// where she stands, in the ground or at home, once a cycle; the pass's end comes back to the known state (<see cref="Ended"/>) and the check runs again. Check: only a line still down after that try holds the rung, by name. A line
    /// she cannot recast herself is named with its reason, and the gate is read as it stands. The way out's gate reads are unchanged (<see cref="LeavingHeld"/>): they protect the trip. Null: the check goes on.
    /// </summary>
    private ScenarioStatus? GateRecastFirst(IGameSessionView session, Ladder ladder, int level)
    {
        var tried = _gateRecastCycle == _cycle;
        var probe = AskRung(session, ladder, level, tried ? "the cycle's check, after the gate recast" : "the cycle's check, before the gate recast", probe: true);
        var down = new List<(string Gate, GateLine Line, string Read)>();
        foreach (var read in probe.Gates.Where(g => !g.Open))
        {
            foreach (var condition in read.Conditions.Where(c => !c.Met && c.Name.EndsWith(" up", StringComparison.Ordinal)))
            {
                var name = condition.Name[..^" up".Length];
                if (read.Gate.Lines.FirstOrDefault(l => string.Equals(l.Line, name, StringComparison.Ordinal)) is { } line && !down.Any(d => string.Equals(d.Line.Line, name, StringComparison.Ordinal)))
                {
                    down.Add((read.Gate.Rung, line, condition.Read));
                }
            }
        }

        if (tried)
        {
            var still = down.Where(d => _gateRecastLines.Contains(d.Line.Line)).Select(d => $"{d.Line.Line} up ({d.Read}, the {d.Gate} gate)").ToList();
            _log.Info(LogCategory, still.Count == 0
                ? string.Create(CultureInfo.InvariantCulture, $"ladder: gate recast: check after (cycle {_cycle}): {string.Join(", ", _gateRecastLines)} up again after the pass: the gates are read on what stands now")
                : string.Create(CultureInfo.InvariantCulture, $"ladder: gate recast: check after (cycle {_cycle}): still down after the pass: {string.Join("; ", still)}: the gate holds the rung by name (M6 14 gate lapse)"));
            return null;
        }

        if (down.Count == 0)
        {
            return null;
        }

        var self = session.State.Self;
        self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _);
        var buffs = GateBuffs(session);
        var can = new List<string>();
        var cannot = new List<string>();
        var names = new List<string>();
        foreach (var (gate, line, read) in down)
        {
            var reach = buffs.Reach(line.Line);
            var want = line.Highest ? Math.Max(line.Level, reach) : line.Level;
            var why = RungGateRule.RecastHerself(RungGateRule.LineOf(BuffProfiles.Climber, line.Line), reach, want, self.Spellbook, mana, self.Inventory.Equipped.Count, _context.Spells, out var yes);
            if (why is null)
            {
                can.Add($"{line.Line} up ({read}, the {gate} gate): {yes}");
                names.Add(line.Line);
            }
            else
            {
                cannot.Add($"{line.Line} up ({read}, the {gate} gate): {why}");
            }
        }

        var place = Classify(session.State, _ground);
        if (names.Count > 0 && place is not (AutopilotPlace.Ground or AutopilotPlace.Home))
        {
            cannot.AddRange(names.Select(n => $"{n} up: she stands neither in the ground nor at home ({place.ToString().ToLowerInvariant()}): no pass runs here"));
            names.Clear();
        }

        if (cannot.Count > 0)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"ladder: gate recast: check before (cycle {_cycle}): level {level}: not recast here: {string.Join("; ", cannot)}{(names.Count == 0 ? ": the gate is read as it stands" : string.Empty)}");
            if (!string.Equals(_gateRecastNoted, text, StringComparison.Ordinal))
            {
                _gateRecastNoted = text;
                _log.Info(LogCategory, text);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        _gateRecastCycle = _cycle;
        _gateRecastLines = names;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: gate recast: check before (cycle {_cycle}): level {level}: {string.Join("; ", can)}; act: {(place == AutopilotPlace.Ground ? "a pass where she stands (the rebuff margin, the field's lines)" : "her pass at home")} recasts it, then the cycle's check reads the gates again (check, act, check: only a line still down after the try holds the rung, by name; M6 14 gate lapse)"));
        return BeginRun(AutopilotState.Buff, place == AutopilotPlace.Ground ? GroundBuffSpec(start: false) : BuffSpec, "the cycle's check: a gate line down that she can recast herself: " + string.Join(", ", names));
    }

    /// <summary>M6 14 gate lapse: true when a pass that just ended was the cycle's gate recast and the cycle's check is still to be made: the pass's end goes back to the known state, where the check reads the gates again.</summary>
    private bool GateRecastEnded() => _gateRecastCycle == _cycle && _ladderCycle != _cycle;

    /// <summary>The buffs a scripted test holds standing; null reads the live ones.</summary>
    internal RungBuffRead? BuffsForTest { get; set; }

    /// <summary>M6 14 (D135): the plan's base War Magic a scripted test reads (the rig has no XpTable); null reads the session's (<see cref="SpellPlan.PlanWarOf"/>).</summary>
    internal Func<int, uint?>? PlanWarForTest { get; set; }

    /// <summary>What a scripted test holds standing in a learned line's place (the buff bot's spell); null reads the live ones (<see cref="GateStands"/>).</summary>
    internal Func<string, int, string?>? GateStandsForTest { get; set; }

    /// <summary>The read of what stands in a learned line's place that every rung ask of this run carries, and its hand-over at a move-on: a test's, else the live one while the stop at the bot is on, else none (the book alone).</summary>
    private Func<string, int, string?>? GateStandsNow() => GateStandsForTest ?? (_options.BuffBot && BuffsForTest is null ? GateStands : null);

    /// <summary>
    /// M6 item 14 (the gates' second form): what stands in a learned line's place: the line stands at <paramref name="level"/> or over by another's stronger spell (the buff bot's, <see cref="BuffCycle.TryStandsByAnother"/>).
    /// The read names the spell, the level it reads as and its seconds left. Null: nothing stands there, the book decides.
    /// </summary>
    private string? GateStands(string line, int level) =>
        _buffCycle is { } cycle && cycle.TryStandsByAnother(line, _clock, out var standing) && standing.Level >= level
            ? string.Create(CultureInfo.InvariantCulture, $"not in the book; stands by another's spell {standing.SpellId} at level {ScrivenerLearnPlan.Numeral(standing.Level)}, {standing.Remaining:0} s left: no scroll of her own is needed while it stands")
            : null;

    /// <summary>
    /// M6 7 AW, check, act, check: she leaves for the run's ground only on <see cref="RungOwner"/>'s yes, its gate read on the buffs that stand now: after a death, a trip or a pass that fell short the run's ground may no longer be open to her. A shut gate
    /// stops the run by the rung's own name (<c>rung-moved:&lt;rung&gt;</c>, ok), and the move-on builds the next run there; she has not left, so no recall. Null: the way out goes on.
    /// </summary>
    private ScenarioStatus? LeavingHeld(string why)
    {
        if (_options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job || _session is not { } session || !session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level))
        {
            return null;
        }

        var choice = AskRung(session, _ladder ??= LadderLoader.WarMage, level, "the way out", leaving: true);
        if (choice.Decision.Kind != LadderStepKind.Moved)
        {
            return null;
        }

        if (GateArmourTrip(choice, level, why) is { } trip)
        {
            return trip;
        }

        _log.Info(LogCategory, $"ladder: way out: level {level}, {why}: she does not leave for {_ground.Key}: {choice.Reason}: {choice.Decision.Why}");
        return Stop(choice.Decision.Stop!, choice.Decision.Ok, choice.Decision.Why);
    }

    /// <summary>
    /// M6 14 gate armour (live 2026-11-17 04:55:43, ladder52: every gate read shut for want of Armor Self and Bludgeon Protection, which her book lacked and the bot's short round never cast; she hunted the outskirts for 93 minutes):
    /// a way out whose gates read shut only for want of armour lines (<see cref="RungOwner.ArmourWant"/>) says so, by name; when a scrivener sells a line whose scroll she lacks, the run's next town trip is taken now (a forced
    /// logistics trip: the every-spell rule buys the gates' armour lines first, <see cref="EverySpellTier.Gate"/>) before the rung is chosen, once per set of lines in the process (<see cref="GateArmourTrips"/>); the way out after it
    /// reads the gates again (check, act, check). Null: no such trip; the way out holds her by the gate as before.
    /// </summary>
    private ScenarioStatus? GateArmourTrip(RungChoice choice, int level, string why)
    {
        if (!choice.Reason.StartsWith("gate-held:", StringComparison.Ordinal))
        {
            return null;
        }

        var want = RungOwner.ArmourWantOf(_ladder ??= LadderLoader.WarMage, choice);
        if (want is null)
        {
            return null;
        }

        var c = CultureInfo.InvariantCulture;
        _log.Info(LogCategory, string.Create(c, $"ladder: way out: level {level}: gate armour: {want.Why}"));
        if (want.Unlearned.Count == 0)
        {
            _log.Info(LogCategory, string.Create(c, $"ladder: way out: level {level}: gate armour: her book holds each line the gates ask; what is down is the bot's round or her own pass, not a scroll: no trip"));
            return null;
        }

        if (!_options.GateArmourTrips.TryNote(want.Unlearned))
        {
            _log.Info(LogCategory, string.Create(c, $"ladder: way out: level {level}: gate armour: check after: a town trip for {string.Join(", ", want.Unlearned)} was taken this process and the gates still read shut (its every_spell lines say what it bought or why each waits): she is held by the gate"));
            return null;
        }

        _log.Info(LogCategory, string.Create(c, $"ladder: way out: level {level}: gate armour: act: the run's next town trip is taken now, before the rung is chosen: its every-spell rule buys {string.Join(", ", want.Unlearned)} first (the gate tier) as her coin allows; check after: the way out reads the gates again"));
        return BeginRun(AutopilotState.Trip, TripSpec(forced: true), $"{why}: the gates' armour lines {string.Join(", ", want.Unlearned)} (M6 14 gate armour)");
    }

    private uint? _fieldMask;

    /// <summary>
    /// M6 14 (<c>m6-banes</c>): the autopilot's own buff cycle (its reads: what is due, what stands, where a pass runs), made once, and told before every read where she stands (<see cref="BuffCycle.FieldDamageTypes"/>):
    /// in the run's ground the six other armour banes are read only where its targets deal their damage type (<see cref="ArmourBanes.FieldMask"/>), so a bane that lapsed in the ground calls no refresh, no pass and no
    /// walk home; at home, and anywhere else, every armour line the book holds is read, so the pass before she leaves casts them all.
    /// </summary>
    /// <param name="session">The session view.</param>
    /// <param name="inFight">True for the read a running fight's upkeep makes: a fight is the field wherever it stands (its own cycles are the field's, <c>FightScenario</c>), so a bane it will not recast never calls its refresh or an upkeep pass.</param>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_buffCycle), nameof(_buffOptions))]
    private BuffCycle BuffReads(IGameSessionView session, bool inFight = false)
    {
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        _buffCycle ??= new BuffCycle(session, _context.Spells, _buffOptions, _log, ArmourBuffLedger.For(session.State));
        _buffCycle.FieldDamageTypes = inFight || JourneyGrounds.At(StationWhere.Den, session.State.Self.Position, _ground) ? _fieldMask ??= ArmourBanes.FieldMask(_ground) : null;
        return _buffCycle;
    }

    private bool _passHomeWalked;
    private readonly List<BuffCycle.LineState> _passLines = [];

    /// <summary>
    /// M6 7 AW (<see cref="PassPlaceRule"/>): the place of a pass in the ground: a line says what was read and where it runs. A walk is the way home by name (<see cref="HomeSpec"/>), and the pass that follows runs at the far end: home for a run of
    /// a ground away from town, the lifestone point on the outskirts. Null: it runs here.
    /// </summary>
    private ScenarioStatus? PassHomeFirst(IGameSessionView session, string why)
    {
        var self = session.State.Self;
        if (_rung is null || self.Position is not { } here)
        {
            return null;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session);
        _buffCycle.ReadLines(_clock, _passLines);
        var ledger = ArmourBuffLedger.For(session.State);
        var lines = _passLines.Count(l => !l.Active && !(_clock < ledger.PassLeftUntil && ledger.PassLeft.Contains(l.Line.Name))); // a line the pass's limit named stays out for as long (M6 7 AN)
        var block = Vitals.RestBlockerRule.Read(session.State.Objects, in here, self.Guid, Vitals.VitalsKeeper.QuietMetres, 0, out var near, session.Live); // M6 16 E2b: where the client draws it
        var overLines = (_ladder ??= LadderLoader.WarMage).Vitals.PassHomeOverLines;
        var place = PassPlaceRule.Decide(lines, overLines, block, PassPlaceRule.AtHome(in here, _ground.Home), _passHomeWalked, out var reason);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: pass: place: {why}: blocker=0x{near:X8} cell=0x{here.CellId:X8}: {reason}: {(place == PassPlace.WalkHome ? "she walks home" : "here")}"));
        if (place != PassPlace.WalkHome)
        {
            return null;
        }

        _passHomeWalked = true;
        return BeginRun(AutopilotState.Travel, HomeSpec(), "the pass runs at home: " + reason);
    }

    /// <summary>
    /// M6 audit (finding 9, <c>JourneyFacts.PassAtHomeDue</c>): read at the logistics trip's end at home: the lines the pass would cast (as <see cref="PassHomeFirst"/> counts them) over the ladder's
    /// <c>pass_home_over_lines</c> name the reason the pass runs at home before the way back; null when the pass is short enough for the ground (<see cref="PassPlaceRule.Decide"/>: a short pass stays).
    /// </summary>
    private string? PassAtHomeDue(IGameSessionView session)
    {
        if (_rung is null)
        {
            return null;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        BuffReads(session);
        var now = session.Clock.Now;
        _buffCycle.ReadLines(now, _passLines);
        var ledger = ArmourBuffLedger.For(session.State);
        var lines = _passLines.Count(l => !l.Active && !(now < ledger.PassLeftUntil && ledger.PassLeft.Contains(l.Line.Name))); // the same count as PassHomeFirst's
        var overLines = (_ladder ??= LadderLoader.WarMage).Vitals.PassHomeOverLines;
        var why = string.Create(CultureInfo.InvariantCulture, $"{lines} line(s) to cast, the line for a walk is over {overLines}");
        _log.Info(LogCategory, "ladder: pass: at the trip's end at home: " + why + (lines > overLines ? ": the pass runs here" : ": the way back goes on"));
        return lines > overLines ? why : null;
    }

    /// <summary>M6 7 AK, AO: whether the book holds a line of the climber's buff profile at a level or any higher one (<see cref="RungGateRule.BookKnows"/>).</summary>
    /// <param name="session">The session view.</param>
    internal static Func<string, int, bool> GateKnows(IGameSessionView session) => RungGateRule.BookKnows(BuffProfiles.Climber, session.State.Self.Spellbook.Knows);

    /// <summary>
    /// M6 7 R (r1f: Winnie reached level 10 mid-cycle and hunted the outskirts on until a trip was due): true when the level has reached the rung's move-on level (<see cref="MoveOnDue"/>) and no hostile is in reach, the upkeep's lull (F23): the fight ends with
    /// <see cref="MoveOnReached"/> and <see cref="FightEnded"/> ends the cycle, so the cycle's check (the level-up spend first, then <see cref="DecideRung"/>) runs at once. One ask a level; the wait for the lull is logged once.
    /// </summary>
    private bool LadderMoveOnReached(IGameSessionView session)
    {
        if (_ladderMoveOnQuiet > _clock)
        {
            return false; // M6 7 AK: the gate is read once every 30 s
        }

        _ladderMoveOnQuiet = _gateHeld ? _clock + 30 : 0;
        if (_options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job || _rung is not { } rung || !session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) || level <= _ladderMoveOnAsked
            || RungOwner.MoveOnDue(_ladder ??= LadderLoader.WarMage, rung, AskRung(session, _ladder, level, "the fight's read"), _ground.Key, LadderQuestDone) is not { } decision)
        {
            return false;
        }

        var reached = decision.Reason == AheadReason
            ? "takes " + decision.Rung?.Key + " ahead of the band (D135)"
            : string.Create(CultureInfo.InvariantCulture, $"reached {rung.Key}'s move-on level {rung.MoveOn.Level}");
        if (_hostiles is { } watch && watch.InReach(_clock, out var what))
        {
            if (_ladderMoveOnWait != level)
            {
                _ladderMoveOnWait = level;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level} {reached}: the fight ends in the next lull ({what})"));
            }

            return false;
        }

        _ladderMoveOnAsked = level;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on: level {level} {reached} and no hostile is in reach: the fight ends and the cycle's check takes it: {decision.Why}"));
        return true;
    }

    /// <summary>
    /// True when a raised bolt cap should be taken now: no hostile in reach (a lull: the restart keeps no creature waiting, the same rule as the upkeep's F23). The fight ends with <see cref="BoltCapRose"/> and starts again where it stands
    /// (<see cref="FightEnded"/>), with the new cap (<see cref="LadderFightSpec"/>).
    /// </summary>
    private bool LadderCapRose()
    {
        if (_ladderCapAsked <= _ladderBolt || (_hostiles is { } watch && watch.InReach(_clock, out _)))
        {
            return false;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: the bolt cap rose {_ladderBolt} -> {_ladderCapAsked} on the raised War Magic, and no hostile is in reach: the fight restarts where it stands at the new cap"));
        _ladderBolt = _ladderCapAsked;
        _ladderCapAsked = 0;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ M6 item 14: the trial switch (--autopilot-trial-ground, --autopilot-trial-minutes)

    /// <summary>The fight's stop when the trial's minutes are fought, in a lull (<see cref="LadderTrialOver"/>): the cycle ends at once and its check takes the ladder's own rung by a normal rung change.</summary>
    public static FightStopReason TrialOver { get; } = new("trial-over");

    private string? _trialCycle; // the trial's ground while this cycle is the trial's (its [ladder] line's trial=)
    private string? _trialRun; // the trial's ground once a cycle of this run was the trial's (the done line's trial=)
    private IStoppableFight? _trialFight; // the fight the clock below belongs to: a new fight starts it again, so no trip or wait between fights counts
    private double _trialFightClock = double.NaN;
    private int _trialFightKills;
    private bool _trialLullLogged;

    /// <summary>
    /// M6 item 14: each frame of a fight of the trial's run (the trial on, the run's ground the trial's, she in it): the kills it made since the last frame (the first starts the trial's minutes) and the seconds of
    /// fight since then (<see cref="LadderTrial.Fought"/>). Time between fights (a trip, the bot, a pass, a death) never counts: the clock starts again with each fight.
    /// </summary>
    private void TrialFightFrame(IStoppableFight fight, int kills)
    {
        if (!ReferenceEquals(fight, _trialFight))
        {
            (_trialFight, _trialFightClock, _trialFightKills) = (fight, double.NaN, 0);
        }

        if (_options.Trial is not { Phase: LadderTrialPhase.On } trial || _options.Job != AutopilotJob.LevelUp || _options.Task.Kind != AutopilotTaskKind.Job
            || !string.Equals(_ground.Key, trial.Ground, StringComparison.Ordinal) || !InGround())
        {
            (_trialFightClock, _trialFightKills) = (double.NaN, kills); // a frame out of the trial's ground counts neither its kills nor its seconds
            return;
        }

        _trialCycle ??= trial.Ground;
        trial.Kill(kills - _trialFightKills, _log);
        _trialFightKills = Math.Max(_trialFightKills, kills);
        if (!double.IsNaN(_trialFightClock))
        {
            trial.Fought(_clock - _trialFightClock);
        }

        _trialFightClock = _clock;
    }

    /// <summary>
    /// M6 item 14: true when the trial's minutes of fight time are fought and no hostile is in reach (the upkeep's lull, as the move-on's): the trial ends by name (<see cref="LadderTrial.End"/>) and the fight ends with
    /// <see cref="TrialOver"/>. The wait for the lull is logged once.
    /// </summary>
    private bool LadderTrialOver(IGameSessionView session)
    {
        if (_options.Trial is not { Phase: LadderTrialPhase.On, TimeUp: true } trial || !string.Equals(_ground.Key, trial.Ground, StringComparison.Ordinal))
        {
            return false;
        }

        var level = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var read) ? read : 0;
        if (_hostiles is { } watch && watch.InReach(_clock, out var what))
        {
            if (!_trialLullLogged)
            {
                _trialLullLogged = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LadderTrial.LinePrefix}minutes fought: ground={trial.Ground} fought_min={trial.FightSeconds / 60:0.0} of {trial.Minutes:0.#}: the fight ends in the next lull ({what})"));
            }

            return false;
        }

        trial.End(string.Create(CultureInfo.InvariantCulture, $"time: {trial.Minutes:0.#} minutes of fight time fought from her first kill there"), level, _log);
        return true;
    }

    /// <summary>M6 item 14: a death counted while the trial is on ends it, by name (<see cref="LadderTrial.EndByDeath"/>); the cycle's check after the recovery takes the ladder's own rung.</summary>
    private void TrialDeath(int deaths)
    {
        if (_options.Trial is { Phase: LadderTrialPhase.On } trial)
        {
            _trialCycle ??= string.Equals(_ground.Key, trial.Ground, StringComparison.Ordinal) ? trial.Ground : null;
            trial.EndByDeath(deaths, _session is { } s && s.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) ? level : 0, _log);
        }
    }

    // ------------------------------------------------------------------------------------------------ M6 item 0 a5: the fight's cap and its casts

    private int _ladderBolt;
    private double? _ladderChance;
    private int? _ladderMargin;
    private int? _castsCycle;
    private int _castsCycleKills;
    private int? _castsTotal;
    private int _castsTotalKills;

    /// <summary>
    /// The fight's spec with the level up job's bolt cap (<see cref="JourneyPlans.MaxLevelArgument"/>: <see cref="BoltLevel"/> of this cycle's rung, so the fight's <c>PlanCap</c> casts no level above it; M6 14, the owner's decision of 2026-11-13: the station makes it the fight's
    /// <see cref="FightOptions.LiveCap"/>, so the ground's <c>spell_plan</c> row, the base skill's projection, does not lower it) and, M6 item 1 a (D41), the
    /// ladder's plan chance (<see cref="JourneyStations.CastChanceArgument"/>: the fight's <c>ChooseLevel</c> casts at the same chance the cap was chosen by), or <paramref name="spec"/> itself for the den job (BSD Mage keeps
    /// <see cref="BuffOptions.DefaultCastChance"/>), a rung with no bolt line or a book that holds no level (the fight then stops by its own rule).
    /// </summary>
    /// <param name="spec">The fight's spec.</param>
    /// <remarks>M6 5 Q5 (D58): the fight also wants the due trophies of the rung's ground (<see cref="JourneyStations.WantArgument"/>, <see cref="AutopilotQuestRule.Want"/>), so the loot step keeps a tusk while its quest is due.</remarks>
    internal StationSpec LadderFightSpec(StationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (_options.Job != AutopilotJob.LevelUp || spec.Kind != StationKinds.DenFight)
        {
            return spec;
        }

        var want = TrophyInput() is { } trophies ? AutopilotQuestRule.Want(trophies) : [];
        if (want.Count > 0 && !_options.LadderQuests)
        {
            // M6 item 14 S3: the sprint wants no trophy: the fight hunts its ground plainly, no holder is pulled and no trophy is kept aside.
            foreach (var (quest, hunt) in AutopilotQuestRule.Trophies(_rung?.GroundKey, TrophyQuests ?? QuestCatalog.All).Where(t => want.Contains(t.Hunt.Item!.Wcid!.Value)))
            {
                QuestsOffSkips(quest.Key, $"the fight on {_rung?.GroundKey} does not want its trophy {hunt.Item!.Name} ({hunt.Item.Wcid})");
            }

            want = [];
        }
        else if (want.Count > 0 && !_options.Quests.IsOn)
        {
            // M6 14 guard task: a selection wants only the trophies of the quests it names (tusks: the Guard Tusk on the Barracks); the rest are hunted plainly
            var named = AutopilotQuestRule.Trophies(_rung?.GroundKey, TrophyQuests ?? QuestCatalog.All).Where(t => want.Contains(t.Hunt.Item!.Wcid!.Value)).ToList();
            foreach (var (quest, hunt) in named.Where(t => !_options.Quests.Allows(t.Quest.Key)))
            {
                QuestsOffSkips(quest.Key, $"the fight on {_rung?.GroundKey} does not want its trophy {hunt.Item!.Name} ({hunt.Item.Wcid})");
            }

            want = [.. want.Where(w => named.Any(t => t.Hunt.Item!.Wcid == w && _options.Quests.Allows(t.Quest.Key)))];
        }

        var packHold = _session is { } session && PackHoldOn(session); // M6 7 AY
        if (_ladderBolt <= 0 && _ladderMargin is null && _ladderChance is null && want.Count == 0 && !packHold)
        {
            return spec;
        }

        var args = new Dictionary<string, string>(spec.Arguments, StringComparer.Ordinal);
        if (packHold)
        {
            args[JourneyStations.PackHoldArgument] = "yes";
            _log.Info(LogCategory, $"ladder: the fight holds to the first slots of {_ground.Key} ({JourneyStations.PackHoldArgument}=yes: dropped back by two deaths to big packs, M6 7 AY; the tusk job goes ahead)");
        }

        if (want.Count > 0)
        {
            var text = string.Join(",", want.Select(w => w.ToString(CultureInfo.InvariantCulture)));
            args[JourneyStations.WantArgument] = text;
            _log.Info(LogCategory, $"ladder: the fight's want: {text} ({JourneyStations.WantArgument}={text}: the due trophy quests of {_rung?.GroundKey}, D58)");
            if (AutopilotQuestRule.Carriers(_rung?.GroundKey, TrophyQuests ?? QuestCatalog.All, want) is { Count: > 0 } carriers)
            {
                // M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): the quest files' carriers, so a carrier's corpse is walked to and the tour goes to its spawn slots first (FightScenario.TrophyCarriers.cs)
                var carriersText = WantCarrier.Format(carriers);
                args[JourneyStations.CarriersArgument] = carriersText;
                _log.Info(LogCategory, $"ladder: the fight's carriers: {carriersText} ({JourneyStations.CarriersArgument}=: the tusk's own creature, m6-audit-tusk-carriers)");
            }
        }

        if (_ladderBolt > 0)
        {
            args[JourneyPlans.MaxLevelArgument] = _ladderBolt.ToString(CultureInfo.InvariantCulture);
            _log.Info(LogCategory, $"ladder: the fight's bolt cap: level {_ladderBolt} ({JourneyPlans.MaxLevelArgument}={_ladderBolt})");
        }

        if (_ladderChance is { } chance)
        {
            var text = chance.ToString(CultureInfo.InvariantCulture);
            args[JourneyStations.CastChanceArgument] = text;
            _log.Info(LogCategory, $"ladder: the fight's cast chance: {text} ({JourneyStations.CastChanceArgument}={text}, the ladder's plan_cast_chance, D41)");
        }

        if (_ladderMargin is { } margin)
        {
            args[JourneyPlans.TargetMarginArgument] = margin.ToString(CultureInfo.InvariantCulture);
            _log.Info(LogCategory, $"ladder: the fight's target margin: {margin} ({JourneyPlans.TargetMarginArgument}={margin}, the rung's target_margin)");
        }

        return spec with { Args = args };
    }

    /// <summary>The casts a kill of <paramref name="casts"/> over <paramref name="kills"/>, or null while no fight line carried <c>casts=</c> or no kill was made.</summary>
    /// <param name="casts">The casts the fight lines gave, or null when none carried the field.</param>
    /// <param name="kills">The kills of the same lines.</param>
    public static double? CastsAKill(int? casts, int kills) => casts is { } c && kills > 0 ? (double)c / kills : null;

    /// <summary>Folds a fight line's <c>casts=</c> and <c>kills=</c> into the cycle's and the run's casts a kill (a line without <c>casts=</c> leaves them alone).</summary>
    private void NoteLadderCasts(string fields)
    {
        if (Token(fields, "casts") is not { } text || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var casts))
        {
            return;
        }

        var kills = Int(fields, "kills");
        (_castsCycle, _castsCycleKills) = ((_castsCycle ?? 0) + casts, _castsCycleKills + kills);
        (_castsTotal, _castsTotalKills) = ((_castsTotal ?? 0) + casts, _castsTotalKills + kills);
    }

    // ------------------------------------------------------------------------------------------------ M6 item 5 Q5 (D58): the trophy errand and the unasked Yes/No

    /// <summary>What the run does after the trophy errand: the trip the fight stopped for, the way home from elsewhere, or the recall home before a stop; M6 6 MB: or the quest task's end (the recall home, stop <c>task-done:quest:&lt;key&gt;</c>).</summary>
    private enum QuestThen
    {
        Trip,
        Home,
        Final,
        Task,
    }

    /// <summary>The quest station's expected seconds when the autopilot runs it (Lifestone's own: the island run and the turn-in; the Trip state's watchdog and its progress rule bound it).</summary>
    public const double QuestTripSeconds = 900;

    /// <summary>Seconds after an answer within which the question must have closed (the check after; Lifestone's own: the answer closes it locally when sent).</summary>
    public const double ConfirmCheckSeconds = 5;

    private bool _questTripTried; // the errand was called once this trip (reset when a logistics trip or a death ends): never a loop
    private bool _questTripActive; // the Trip state is running the quest station
    private QuestThen _questThen;
    private bool _questThenForced;
    private string _questKey = string.Empty;
    private readonly Dictionary<string, string> _questFailed = new(StringComparer.Ordinal); // key -> the failure's stop name: a turn-in that failed is not called again this run (never a loop)
    private int _turnInsCycle;
    private int _turnInsRun;
    private bool _questLadderErrand; // the quest station running is a ladder errand (D118), not a trophy turn-in
    private int _errandsCycle;
    private int _errandsRun;
    private readonly List<uint> _trophyScratch = [];
    private readonly Dictionary<(uint Type, uint Context), ConfirmSeen> _confirmSeen = [];

    /// <summary>An open question the tick decided: when, whether an answer was sent, and whether the check after it was made.</summary>
    private readonly record struct ConfirmSeen(double At, bool Answered, bool Checked);

    /// <summary>M6 7 AX: a trophy job is due: the level up job, the quest's timer (a done quest's 20 hours included, the existing read), the trophy not held to its count, and the session's tries not spent.</summary>
    private bool TrophyJobDue(IGameSessionView session, string key)
    {
        if (_options.Job != AutopilotJob.LevelUp || (TrophyQuests ?? QuestCatalog.All).FirstOrDefault(q => q.Key == key) is not { } quest
            || quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.HuntUntil && s.Item?.Wcid is not null) is not { } hunt
            || !_context.QuestTimersInUse.IsDue(quest, session.Clock.Time.GetUtcNow())
            || GearInventory.CountOf(session.State, _trophyScratch, hunt.Item!.Wcid!.Value) >= (hunt.Count ?? 1))
        {
            return false;
        }

        var tries = GroundCatalog.TryGet(hunt.Ground ?? string.Empty)?.Hunt?.Pulls.FirstOrDefault(p => p.Stand?.Quest == key)?.Stand?.Tries ?? 0;
        if (!TrophyPullMemory.For(session.State).MayTry(key, tries))
        {
            return false;
        }

        return !QuestsOffSkips(key, "its trophy job is due, and no rung is entered for it");
    }

    // ------------------------------------------------------------------------------------------------ M6 item 14 S3: the sprint, --autopilot-ladder-quests=off

    private readonly HashSet<string> _questsOffLogged = new(StringComparer.Ordinal);

    /// <summary>The quests the sprint skipped this run, by key, each logged once (<c>--autopilot-ladder-quests=off</c>).</summary>
    public IReadOnlyCollection<string> QuestsSkipped => _questsOffLogged;

    /// <summary>
    /// M6 item 14 S3 (the owner's sprint, 2026-11-11: quests are ignored when they slow her down): true when <c>--autopilot-ladder-quests</c> is off, so the caller runs no errand, wants no trophy and enters no rung for <paramref name="key"/>; each
    /// quest skipped is logged once a run, by name, with what was skipped first. False with the option on (the default): today's ladder. The ladder's quest rungs (the Training Academy and the Pathwarden chest) are not asked here: they stay in both settings.
    /// </summary>
    /// <remarks>M6 14 guard task: with a selection (<c>--autopilot-ladder-quests=tusks,tusker-guard-kill-task</c>, <see cref="AutopilotOptions.Quests"/>) a quest it does not name is skipped the same way, by name, once a run.</remarks>
    private bool QuestsOffSkips(string key, string what)
    {
        var quests = _options.Quests;
        if (quests.Allows(key))
        {
            return false;
        }

        if (_questsOffLogged.Add(key))
        {
            _log.Info(LogCategory, quests.IsOff
                ? $"autopilot quest: key={key}: skipped: --autopilot-ladder-quests=off (the sprint: no quest errand from home, no trophy want): {what}; logged once a run"
                : $"autopilot quest: key={key}: skipped: --autopilot-ladder-quests={quests.Text} does not name it (the selection runs only its own quests): {what}; logged once a run");
        }

        return true;
    }

    /// <summary>The quests the trophy errand reads (null: <see cref="QuestCatalog.All"/>); a test sets its own records.</summary>
    internal IReadOnlyList<Quest>? TrophyQuests { get; set; }

    /// <summary>M6 5 Q5: the trophy turn-ins (quest completions of non-rung quests) this run.</summary>
    public int TurnIns => _turnInsRun;

    /// <summary>M6 8 F (D118): the ladder errands completed this run.</summary>
    public int LadderErrands => _errandsRun;

    /// <summary>The quest station's spec for the trophy errand: <c>quest=&lt;key&gt;</c>, <c>from=ground|home</c> (the Trip state runs it, <c>_questTripActive</c>).</summary>
    /// <param name="key">The quest's key.</param>
    /// <param name="from"><see cref="JourneyPlans.FromGround"/> or <see cref="JourneyPlans.FromHome"/>.</param>
    /// <param name="steps">M6 14 guard task: the station's range of the file's steps (<see cref="JourneyPlans.StepsArgument"/>, <c>0-1</c>: a kill task's arm), or null for the whole file.</param>
    public static StationSpec QuestSpec(string key, string from, string? steps = null)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.QuestArgument] = key, [JourneyPlans.FromArgument] = from };
        if (steps is not null)
        {
            args[JourneyPlans.StepsArgument] = steps;
        }

        return new(QuestStation.StationName, StationKinds.Quest, null, QuestSeconds(key), args);
    }

    /// <summary>
    /// M6 14 the Atlan wand: the quest station's expected seconds: <see cref="QuestTripSeconds"/>, and for a ladder errand (D118, a whole trip of its own) each hunt-until step's sub-run fight (<see cref="QuestStation.FightExpectedSeconds"/>):
    /// the wand's 16 Pyreal Motes are about 400 Nubilous Golem kills (a 4% drop, the world database's create list row 46840), past the 900 s a turn-in is sized for. A trophy turn-in's hunt is the fight's own (its station only
    /// turns in), so it keeps <see cref="QuestTripSeconds"/>. The Trip state's watchdog (no progress for 900 s) still bounds a stall.
    /// </summary>
    /// <param name="key">The quest's key.</param>
    public static double QuestSeconds(string key) =>
        QuestCatalog.All.FirstOrDefault(q => q.Key == key) is { Ladder: not null } quest
            ? QuestTripSeconds + (quest.Steps.Count(s => s.Kind == QuestStepKind.HuntUntil) * QuestStation.FightExpectedSeconds)
            : QuestTripSeconds;

    /// <summary>The trophy rule's inputs from the session (the level up job on a ground rung only), else null.</summary>
    private TrophyQuestInput? TrophyInput()
    {
        if (_options.Job != AutopilotJob.LevelUp || _session is not { } s || _rung?.GroundKey is not { } ground)
        {
            return null;
        }

        var state = s.State;
        return new TrophyQuestInput(
            ground,
            TrophyQuests ?? QuestCatalog.All,
            _context.QuestTimersInUse,
            s.Clock.Time.GetUtcNow(),
            w => GearInventory.CountOf(state, _trophyScratch, w),
            state.Self.Qualities.Level,
            RecallReadiness.Of(state, _context.Facts, _context.Spells).Castable, // M6, the recall's components: known, not refused this run and the pack not short for one cast
            state.Self.Position?.CellId,
            _context.Grounds,
            _context.Routes);
    }

    private readonly HashSet<string> _tuskWaitLogged = new(StringComparer.Ordinal); // M6 audit: a held tusk's wait for the next town run, named once a run

    /// <summary>
    /// The errand's call at a way home (the fight's trip, the way home from elsewhere, the recall before an ok stop). First the trophies (D58): each trophy quest of the rung's ground decided by <see cref="AutopilotQuestRule"/> on one line. Then, when no trophy runs, the
    /// ladder errands (M6 8 F, D118; <see cref="LadderErrandCall"/>), each decided on one line. The first that runs starts the quest station in the Trip state. Never a loop: once a trip (a cut errand is called again only after the next logistics trip or death ends), and a
    /// quest that failed is not called again this run (logged by its stop's name). Null: nothing to run, so the caller goes on; a run with no ground rung has no input and asks nothing.
    /// </summary>
    /// <remarks>
    /// M6 audit (the owner's decision of 2026-11-19: tusks only on town runs, never a trip of their own): <paramref name="trophies"/> false (the call after a trip that ended back on her ground, <see cref="ErrandAfterTrip"/>) leaves the
    /// trophies out: a held due tusk waits, named once, for the next way home (the hourly trip, a rung change's stop, the way home from elsewhere), where its turn-in is a detour on a trip she makes anyway.
    /// </remarks>
    private ScenarioStatus? QuestErrand(QuestThen then, bool forced, string why, bool trophies = true)
    {
        if (_questTripTried || _questTripActive || TrophyInput() is not { } input)
        {
            return null;
        }

        var calls = AutopilotQuestRule.Calls(input);
        if (!trophies)
        {
            foreach (var waits in calls.Where(c => c.Runs && _options.Quests.Allows(c.Quest.Key)))
            {
                if (_tuskWaitLogged.Add(waits.Quest.Key))
                {
                    _log.Info(LogCategory, $"autopilot quest: key={waits.Quest.Key}: its trophy is held and due, and it waits for the next town run ({why}: no trip of its own from the ground; m6-audit-tusk-town-run); logged once a run");
                }
            }

            calls = [];
        }

        if (!_options.LadderQuests)
        {
            // M6 item 14 S3: the sprint runs no errand: every one that would run now is named once and the caller's way goes on.
            foreach (var skipped in calls.Where(c => c.Runs))
            {
                QuestsOffSkips(skipped.Quest.Key, $"its trophy is held and due, and the turn-in from={skipped.From} is not run (the trophy stays held)");
            }

            foreach (var skipped in AutopilotQuestRule.LadderCalls(input, _questFailed).Where(c => c.Runs))
            {
                QuestsOffSkips(skipped.Quest.Key, $"the ladder errand is due ({skipped.Quest.Ladder}) and is not run");
            }

            return null;
        }

        // M6 14 guard task: a selection runs only the quests it names; a turn-in it does not name is skipped by name and the trophy stays held
        foreach (var skipped in calls.Where(c => c.Runs && !_options.Quests.Allows(c.Quest.Key)))
        {
            QuestsOffSkips(skipped.Quest.Key, $"its trophy is held and due, and the turn-in from={skipped.From} is not run (the trophy stays held)");
        }

        calls = [.. calls.Where(c => _options.Quests.Allows(c.Quest.Key))];
        foreach (var decision in calls)
        {
            _log.Info(LogCategory, decision.Line);
        }

        if (AutopilotQuestRule.First(calls.Where(c => !_questFailed.ContainsKey(c.Quest.Key)).ToList()) is { } call)
        {
            return StartErrand(then, forced, call.Quest.Key, call.From!, ladder: false, $"{why}: the trophy {call.Quest.Key} is held and due: the quest station from={call.From} first (D58)");
        }

        foreach (var failed in calls.Where(c => c.Runs && _questFailed.ContainsKey(c.Quest.Key)))
        {
            _log.Warn(LogCategory, $"autopilot quest: key={failed.Quest.Key}: its turn-in failed this run ({_questFailed[failed.Quest.Key]}): not called again until the next run; the trophy stays held");
        }

        if (KillTaskErrand(then, why) is { } killTask)
        {
            return killTask; // M6 14 guard task: a selected kill task's talk (the arm, or the turn-in), after the trophies and before the ladder errands
        }

        if (LadderErrandCalled(input) is not { } errand)
        {
            return null;
        }

        if (then == QuestThen.Trip && ErrandWaitsForSale(errand.Quest.Key))
        {
            return null; // the caller starts the trip; TripEnded calls the errand again from where the trip ends
        }

        return StartErrand(then, forced, errand.Quest.Key, errand.From!, ladder: true, $"{why}: the ladder errand {errand.Quest.Key} is due ({errand.Quest.Ladder}): the quest station from={errand.From} first (D118)");
    }

    /// <summary>The load a scripted test holds, in percent of the capacity; null reads the live one (<see cref="TryLoadShares"/>).</summary>
    internal double? BurdenForTest { get; set; }

    private bool _errandAfterTrip; // a ladder errand waited for the sale (ErrandWaitsForSale): TripEnded calls it once the trip has ended well

    /// <summary>
    /// M6 8, the long round (2026-11-10), check before: a ladder errand called at the fight's trip waits for the sale when her load is over the ground's trip line (<see cref="LogisticsCounters.BurdenTripPercent"/>). The
    /// trip the fight stopped for goes first, and <see cref="TripEnded"/> calls the errand from where the trip ends. Two of the round's three errands began on a trip called for burden (138% and 141%) and both failed: one
    /// ran dry among the Cathedral's drudges, the other stuck at the hand-in with every jump refused as too loaded; the third began at a learn trip and ran clean. A load that cannot be read does not hold the errand.
    /// </summary>
    private bool ErrandWaitsForSale(string key)
    {
        var burden = BurdenForTest ?? (TryLoadShares(out _, out var live) ? live : (double?)null);
        if (burden is not { } percent || percent <= _counters.BurdenTripPercent)
        {
            return false;
        }

        _errandAfterTrip = true;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={key}: check before: burden {percent:0}% is over the trip line {_counters.BurdenTripPercent}%: the sale first, the errand from where the trip ends (m6-errand-after-sale)"));
        return true;
    }

    /// <summary>The trip a ladder errand waited for has ended well (<see cref="ErrandWaitsForSale"/>): the errand is asked again, now as a way of its own from where she stands; null when none waited or none runs now.</summary>
    private ScenarioStatus? ErrandAfterTrip()
    {
        if (!_errandAfterTrip)
        {
            return null;
        }

        _errandAfterTrip = false;
        var burden = BurdenForTest ?? (TryLoadShares(out _, out var live) ? live : (double?)null);
        return QuestErrand(QuestThen.Home, forced: false, string.Create(CultureInfo.InvariantCulture, $"after the sale trip (check after: burden {(burden is { } b ? b.ToString("0", CultureInfo.InvariantCulture) + "%" : "unread")})"), trophies: false); // M6 audit: the trip ended on her ground: a held tusk waits for the next town run
    }

    private int _errandCycleAsked = -1;
    private readonly HashSet<string> _errandAtCycleCalled = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 sprint (run 5, 2026-11-19 14:23 to 15:09 UTC): a ladder errand was called only on a way home, and with the trips an hour apart she made none from level 6 to 10, so Worcer's Missing Heirlooms (band from 5 before 10, 36,000
    /// experience; its eight heirlooms lie in the Holtburg Redoubt she hunted those levels) passed undone; the run's first check at level 6 never asked. The cycle's check asks the ladder errands too, once a cycle: only the ladder
    /// errands (a held tusk still waits for a town run, the owner's rule of 2026-11-19; the kill task's talk stays on the way home). Null: none is due, or one was asked this cycle, or the quests are off.
    /// </summary>
    private ScenarioStatus? LadderErrandAtCycle(int level)
    {
        if (_errandCycleAsked == _cycle || !_options.LadderQuests || _questTripTried || _questTripActive || TrophyInput() is not { } input)
        {
            return null;
        }

        _errandCycleAsked = _cycle;
        if (LadderErrandCalled(input) is not { } errand || _errandAtCycleCalled.Contains(errand.Quest.Key))
        {
            return null; // only an errand never started this run (by any call): a cut or failed one is called again only on a way home, by the rules there (never a loop)
        }

        return StartErrand(QuestThen.Home, forced: false, errand.Quest.Key, errand.From!, ladder: true, string.Create(CultureInfo.InvariantCulture, $"the cycle's check at level {level}: the ladder errand {errand.Quest.Key} is due ({errand.Quest.Ladder}): the quest station from={errand.From} first (D118; m6-errand-at-cycle)"));
    }

    /// <summary>The ladder errands decided, one line each, a band passed with its quest undone named once a session (<c>TODO(m6-ladder-errand-missed)</c>: never run, the band is what guards a level-capped portal); the first that runs, or null.</summary>
    private LadderErrandCall? LadderErrandCalled(TrophyQuestInput input)
    {
        var all = AutopilotQuestRule.LadderCalls(input, _questFailed);
        foreach (var skipped in all.Where(c => c.Runs && !_options.Quests.Allows(c.Quest.Key)))
        {
            QuestsOffSkips(skipped.Quest.Key, $"the ladder errand is due ({skipped.Quest.Ladder}) and is not run"); // M6 14 guard task: a selection runs only the errands it names
        }

        var calls = all.Where(c => _options.Quests.Allows(c.Quest.Key)).ToList();
        foreach (var decision in calls)
        {
            _log.Info(LogCategory, decision.Line);
            if (decision.Missed && _session is { } session && LadderErrandMemory.For(session.State).FirstMiss(decision.Quest.Key))
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: TODO(m6-ladder-errand-missed): key={decision.Quest.Key} level={input.Level}: its band ({decision.Quest.Ladder}) has passed with the quest undone: it is not run (a late attempt would walk into a refused portal)"));
            }
        }

        return AutopilotQuestRule.FirstLadder(calls);
    }

    /// <summary>Starts the quest station for an errand in the Trip state; null (logged) when a stop rule is due first.</summary>
    private ScenarioStatus? StartErrand(QuestThen then, bool forced, string key, string from, bool ladder, string why, string? steps = null)
    {
        if (then != QuestThen.Final && StopDue() is not null)
        {
            _log.Info(LogCategory, $"autopilot quest: key={key}: a stop rule is due, so the errand waits for the stop's own way home");
            return null;
        }

        if (then == QuestThen.Final && ladder)
        {
            _log.Info(LogCategory, $"autopilot quest: key={key}: the way home before the stop starts no ladder errand (m6-stop-runs-errand): a stop is a way home, not an errand; the errand waits for the next run");
            return null; // M6 8: only a held due trophy is turned in on the stop's way home (D58); a ladder errand is a whole trip of its own
        }

        if (ladder && ErrandWaitsForBuffs(key))
        {
            return null; // M6 8 K: start ready: no enchantment stands: the caller's way goes on, the run's own pass buffs her, and the next way home asks again
        }

        if (ladder && ErrandWaitsForRoll(key))
        {
            return null; // M6 audit (fix 10): the roll's chance is under its floor: the caller's way goes on, and the next way home asks again
        }

        if (ladder && ErrandWaitsForLearn(key, then == QuestThen.Trip))
        {
            return null; // M6 14 the element line: the job's bolt line first (one trip's learn), then the errand
        }

        if (ErrandSellsFirst(then, forced, key, from, ladder, why, steps, out var sale))
        {
            return sale; // M6 14 sell first (the owner, 2026-11-17): the sale trip before an errand from home whose steps gain items, when her pack has no room for them (AutopilotScenario.ErrandRoom.cs)
        }

        if (ladder)
        {
            _errandAtCycleCalled.Add(key); // M6 sprint (m6-errand-at-cycle): an errand started once, by any call, is never the cycle's check's again this run
        }

        _questTripTried = true;
        _questTripActive = true;
        _errandHuntHeld = HuntHeld(key); // M6 14: a cut errand whose hunt gained stays callable (QuestEnded)
        _questThen = then;
        _questThenForced = forced;
        _questKey = key;
        _questLadderErrand = ladder;
        var started = BeginRun(AutopilotState.Trip, ladder ? ReadyErrandSpec(key, from, steps) : QuestSpec(key, from, steps), why, resume: then == QuestThen.Final);
        if (then == QuestThen.Trip)
        {
            _tripForced = forced; // the trip the fight stopped for is still to be made after the errand (a cut or a failure runs it again: ResumeTrip)
        }

        return started;
    }

    /// <summary>
    /// The quest station ended (the Trip state with <c>_questTripActive</c>): well, the completion counts (<c>turn_ins=</c> for a trophy, <c>errands=</c> for a ladder errand; the station recorded and saved the timer, <see cref="JourneyContext.CompleteQuest"/>) and the run goes on with what it was doing;
    /// a failed turn-in stops the run by the station's own name <c>failed:quest:&lt;key&gt;:&lt;fail&gt;</c> through the station failure's recovery (the trip stays due); a failed ladder errand does not (M6 8 F, <see cref="LadderErrandFailed"/>); during the recall before a stop the recall goes on.
    /// </summary>
    private ScenarioStatus QuestEnded(bool ok, string? why, string fields)
    {
        _questTripActive = false;
        var errand = _questLadderErrand;
        _questLadderErrand = false;
        _tripPending = _questThen == QuestThen.Trip; // the trip the fight stopped for is still to be made
        var key = _questKey;
        var done = ok && Token(fields, "done") == "yes";
        var villaFetch = _villaFetchKey == key; // M6 villa: the villa's stone fetch, counted apart (VillaFetches), not as a ladder errand
        _villaFetchKey = null;

        if (done && errand && !villaFetch)
        {
            _errandsCycle++;
            _errandsRun++;
        }
        else if (done && !LadderQuestKeys().Contains(key))
        {
            _turnInsCycle++;
            _turnInsRun++;
        }

        _log.Info(LogCategory, $"autopilot quest: ended key={key} ok={(ok ? "yes" : "no")} done={(done ? "yes" : "no")} xp={Token(fields, "xp") ?? "?"} timer={Token(fields, "timer") ?? "?"} turn_ins={_turnInsRun} errands={_errandsRun}{(why is null ? string.Empty : " why=" + JourneyVerdict.Token(why))}");
        OrderQuestEnded(key, ok && done, why); // the command registry: a quest order's check after (AutopilotScenario.Orders.cs)
        if (_deathSeen)
        {
            if (errand)
            {
                LadderErrandDied(key);
            }

            return UnscriptedDeath();
        }

        if (KillTaskEnded(key, ok, done, why, fields) is { } killTask)
        {
            return killTask; // M6 14 guard task: a kill task's talk is judged by what the server said of the counter, not by the station's range alone
        }

        var missing = errand && ok && !done; // a station that ended well with the quest not done: no timer was saved, so calling it again would repeat the same walk
        var stop = "failed:quest:" + key + ":" + JourneyVerdict.Token(missing ? "not-done" : Token(fields, "fail") is { } fail && fail != "none" ? fail : why ?? "failed");
        var lostRoll = errand && !ok && (RecipeRollLost(Token(fields, "fail")) || RecipeRollHeld(Token(fields, "fail"))); // M6 audit (fix 10): a roll held under its floor keeps the errand callable too
        var huntGained = errand && !ok && !lostRoll && HuntHeld(key) is var heldNow && heldNow > _errandHuntHeld;
        var walkAgain = errand && !ok && !lostRoll && !huntGained && ErrandWalkFailed(Token(fields, "fail")) && ErrandWalkTry(key);
        if ((!ok || missing) && !lostRoll && !huntGained && !walkAgain)
        {
            _questFailed[key] = stop;
        }
        else if (walkAgain)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={key}: {stop}: a walk step failed ({_errandWalkFails[key]} of {LadderErrandWalkTries} this run): the errand stays callable at the next way home (M6 14, live ladder42 20:20:15 UTC: the hop to Crater Lake retreated); past the bound it fails for the run"));
        }
        else if (lostRoll)
        {
            _log.Warn(LogCategory, $"autopilot quest: key={key}: {stop}: the roll was lost (its sources are gone, ACE's fail result is in the pack): the errand stays callable, so the next way home starts the chain again from what she holds (M6 14, the Atlan wand's ingot)");
        }
        else if (huntGained)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={key}: {stop}: its hunt gained ({_errandHuntHeld} -> {HuntHeld(key)} of the hunted items held): the errand stays callable and the next way home goes on from what she holds (M6 14; a call that gains nothing is a failure of the run)"));
        }

        if (_finalStop is not null)
        {
            if (!ok)
            {
                _log.Error(LogCategory, $"autopilot quest: {stop}: the turn-in failed during the recall home before the stop; the recall goes on (the stop keeps its own name)");
            }

            return FinalAttempt(ok ? "first, after the turn-in" : "first, after the failed turn-in");
        }

        if (errand && (!ok || missing))
        {
            return LadderErrandFailed(key, stop, why);
        }

        if (!ok)
        {
            return StationFailed(stop, $"the quest station {key} failed", why);
        }

        if (villaFetch && VillaFetchEnded(key, done) is { } villa)
        {
            return villa; // M6 villa: the schedule is asked again from the cave (the hand-in goes home itself)
        }

        return QuestThenStep("after the turn-in of " + key);
    }

    /// <summary>
    /// M6 8 F (D118): a ladder errand failed: logged by name, remembered for the run (<see cref="_questFailed"/>), and she goes home by the recovery's named way (<see cref="RecoverTo"/>: nothing at home, on foot or by portals where the travel file has a way, else Lifestone Recall), and the
    /// climb goes on with what it was doing (the trip stays due). It is never <see cref="StationFailed"/>, whose budget and repeat rules end a run: one failure a key a run bounds it, so it costs no recovery. A way home that itself fails ends the run by that station's name, as any way home does.
    /// </summary>
    private ScenarioStatus LadderErrandFailed(string key, string stop, string? why)
    {
        FoldInFlight();
        _log.Warn(LogCategory, $"autopilot quest: ladder errand failed: key={key} stop={stop}{(why is null ? string.Empty : " why=" + JourneyVerdict.Token(why))}: {(_questFailed.ContainsKey(key) ? "not called again this run" : "callable again at the next way home (M6 14: a lost roll, a hunt that gained, or a walk within its tries)")}; the climb goes on by the recovery's way home");
        LastResortArm(key); // M6 8 N (D120): if the way home fails too, /lifestone is the last resort, once for this errand
        return RecoverTo("recovery after the ladder errand " + key);
    }

    /// <summary>M6 8 F: a death inside a ladder errand goes to the death path as any death; the errand is not walked again this run, so a place that kills her cannot spend the run's deaths.</summary>
    private void LadderErrandDied(string key)
    {
        _questFailed[key] = "failed:quest:" + key + ":death";
        _log.Warn(LogCategory, $"autopilot quest: ladder errand died: key={key}: the death station first; not called again this run");
    }

    /// <summary>The errand was cut (a reconnect, a watchdog, a hand-back): it is not run again this trip (the trophy stays held; the next trip's way home calls it again), and the run goes on with what it was doing.</summary>
    private ScenarioStatus QuestCut()
    {
        OrderQuestEnded(_questKey, done: false, "cut"); // the command registry: a quest order's errand that was cut ends the order by name
        _villaFetchKey = null; // M6 villa: a cut fetch is weighed again at the next boundary
        _questTripActive = false;
        _tripPending = _questThen == QuestThen.Trip;
        _questLadderErrand = false;
        _log.Warn(LogCategory, $"autopilot quest: the errand {_questKey} was cut: it is not run again this trip (the trophy stays held; the next way home calls it again)");
        return _questThen == QuestThen.Trip ? QuestThenStep("after the cut errand " + _questKey) : EnterKnown("after the cut errand " + _questKey);
    }

    private ScenarioStatus QuestThenStep(string why) => _questThen switch
    {
        QuestThen.Trip => BeginRun(AutopilotState.Trip, TripSpec(_questThenForced), $"{why}: the trip the fight stopped for"),
        QuestThen.Home => QuestThenHome(why),
        QuestThen.Task => TaskQuestDone(why), // M6 6 MB: the quest task's turn-in: the recall home, then stop task-done:quest:<key>
        _ => FinalAttempt(why),
    };

    /// <summary>The way home after an errand from elsewhere (M6 8 F: an errand can end in Holtburg, where a recall is refused, M6 7 BC): at home, no way is run and the known state classifies; else the rule's way.</summary>
    private ScenarioStatus QuestThenHome(string why)
    {
        var way = HomeDecision();
        if (way is { Way: TravelWay.AtHome })
        {
            _log.Info(LogCategory, $"autopilot quest: {why}: at home: {way.Reason}; no way home is run");
            return EnterKnown(why);
        }

        return BeginRun(AutopilotState.Travel, way is { Way: TravelWay.RungChange } ? RungHomeSpec : RecallHomeSpec, $"{why}: the way home");
    }

    /// <summary>True while the quest station runs for a way home that is not the fight's trip (no logistics trip is pending behind it).</summary>
    private bool QuestHoldsNoTrip => _questTripActive && _questThen != QuestThen.Trip;

    /// <summary>A logistics trip or a death ended: the trophy errand may be called again on the next way home.</summary>
    private void QuestTripReset() => _questTripTried = false;

    private HashSet<string> LadderQuestKeys() => [.. (_ladder ??= LadderLoader.WarMage).Rungs.Where(r => r.Kind == LadderRungKind.Quest).SelectMany(r => r.QuestKeys)];

    /// <summary>
    /// M6 5 Q5 (closes <c>m5-6-headless-confirm-reply</c>): the server's open questions decided each tick by <see cref="QuestConfirmRule"/> with no step (an unasked one of a type ACE keeps gets No; types 1 and 4 and the rest are left
    /// open), each decision logged once and each answer sent (<see cref="IActionApi.RespondToConfirmation"/>; check after: the question closed within <see cref="ConfirmCheckSeconds"/>, else a named warning, never a second answer). While
    /// the quest station runs, its own confirm step decides (the same rule with its step), so the tick leaves the questions to it.
    /// </summary>
    private void AnswerUnasked(IGameSessionView session)
    {
        var open = session.State.PendingConfirmations;
        foreach (var (key, seen) in _confirmSeen.ToList())
        {
            var still = open.Any(q => q.Type == key.Type && q.Context == key.Context);
            var checkedNow = seen.Checked;
            if (seen.Answered && !seen.Checked && (!still || _clock - seen.At >= ConfirmCheckSeconds))
            {
                checkedNow = true;
                _confirmSeen[key] = seen with { Checked = true };
                var line = string.Create(CultureInfo.InvariantCulture, $"autopilot confirm: the question (type {key.Type}, context {key.Context}) {(still ? $"is still open {ConfirmCheckSeconds:0} s after the answer (TODO(m6-confirm-unclosed)); it is not answered again" : "closed after the answer")}");
                if (still)
                {
                    _log.Warn(LogCategory, line);
                }
                else
                {
                    _log.Info(LogCategory, line);
                }
            }

            if (!still && (!seen.Answered || checkedNow))
            {
                _confirmSeen.Remove(key);
            }
        }

        if (open.Count == 0 || (_questTripActive && _phase == RunPhase.Running))
        {
            return;
        }

        foreach (var decision in QuestConfirmRule.Decide([.. open], null))
        {
            var key = (decision.Question.Type, decision.Question.Context);
            if (_confirmSeen.ContainsKey(key))
            {
                continue;
            }

            _confirmSeen[key] = new ConfirmSeen(_clock, decision.Answers, false);
            _log.Info(LogCategory, "autopilot " + decision.Line);
            if (decision.Answers)
            {
                var sent = session.Actions.RespondToConfirmation(decision.Question.Type, decision.Question.Context, decision.Accepted, ActionSource.Automation);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot confirm: answered {(decision.Accepted ? "Yes" : "No")} (type {decision.Question.Type}, context {decision.Question.Context}, action {sent.Id})"));
            }
        }
    }
}
