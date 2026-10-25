using System.Globalization;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What the hunt leg measured (<c>--login-check=hunt</c>, M4 1.2 s4b): the scenario's tally (<see cref="HuntTally"/>) and its facts. Every count is the scenario's own.
/// </summary>
/// <param name="Kills">The 0x01AD kills (<c>kills</c>, judged &gt;= 1).</param>
/// <param name="DamageDealt">The damage of the player's blows (<c>damage_dealt</c>, judged &gt; 0; the killing blow has no line).</param>
/// <param name="DamageTaken">The damage taken (<c>damage_taken</c>, report-only).</param>
/// <param name="HitsTaken">The blows taken (<c>hits_taken</c>, report-only).</param>
/// <param name="Evades">The blows evaded (<c>evades</c>, report-only).</param>
/// <param name="XpGained">The total-experience delta (<c>xp_gained</c>, report-only).</param>
/// <param name="Fighter"><c>ok</c> or <c>wrong</c> (<c>fighter</c>, report-only: a wrong character already fails the run through its failure).</param>
/// <param name="Swings">The player's swings (<c>swings</c>, judged &gt;= 1 for the melee hunt from 1.3).</param>
/// <param name="Shots">The arrows launched (<c>shots</c>, judged &gt;= 1 for the missile hunt from 1.3).</param>
/// <param name="Hits">The blows landed (<c>hits</c>, judged &gt;= 1 for the missile hunt from 1.3).</param>
/// <param name="Misses">The blows that missed (<c>misses</c>, report-only in 1.2).</param>
/// <param name="Returned">The player ended in Holtburg's landblock (<c>returned</c>, report-only in 1.2).</param>
/// <param name="Alive">No death notice (0x01AC or 0x019E) was seen during the run.</param>
/// <param name="Weapon">The weapon the hunt used (<c>--hunt-weapon</c>): it chooses which of <c>swings</c> and <c>shots hits</c> are judged.</param>
/// <param name="CorpsesOpened">The kills' corpses whose open was Confirmed (<c>corpses_opened</c>, M4 2.1 s4a; report-only here, the lead reads <c>&gt;= 1</c> at the item's gate).</param>
/// <param name="CorpseItems">The entries those corpses showed, summed (<c>corpse_items</c>, report-only).</param>
/// <param name="AttackFrameMaxMs">The slowest frame from an attack's send to its run's Done (<c>attack_frame_max_ms</c>, M4 1.3 s2; report-only in 1.3, judged from 2.1: over 75 ms fails, over 50 ms flags <c>frame_over_50=yes</c>), or null when no window was sampled.</param>
/// <param name="Looted">The items the hunt picked up from its kills' corpses, the PickUps Confirmed (<c>looted</c>, M4 2.2 s0; report-only: judged <c>&gt;= 1</c> from 3.1, plan section 5 item 3).</param>
/// <param name="LootFrameMaxMs">The slowest frame from a corpse's open being sent to its close being settled or the sequence abandoned (<c>loot_frame_max_ms</c>, <see cref="LootFrameSampler"/>; report-only, judged from 3.1), or null when no window was sampled.</param>
/// <param name="LootSkipped">The corpse entries the loot rule left (<c>loot_skipped</c>, report-only).</param>
/// <param name="Heals">The recoveries that raised the health: kit uses whose roll won plus potions that restored something (<c>heals</c>, M4 3.1 s3; report-only: <c>&gt;= 1</c> is judged at 3.2's gate).</param>
/// <param name="KitUses">The kit uses confirmed, won or lost (<c>kit_uses</c>, report-only).</param>
/// <param name="Potions">The Potions of Healing drunk (<c>potions</c>, report-only).</param>
/// <param name="LootEnabled">False when the hunt was asked not to loot (<c>--loot off</c>, or <c>--hunt-open-corpse off</c>): <c>looted &gt;= 1</c> is then not judged (a field reads what it names).</param>
/// <param name="Ground">The ground the hunt was asked for (<c>--hunt-ground</c>: <c>ground=belt|forest</c> on the line).</param>
/// <param name="Sold">The items the loop's end sold (<c>sold</c>, M4 3.2 s4a; not judged here: the journey's vendor station judges the sale, <c>JourneyStations.SaleHappened</c>).</param>
/// <param name="Bought">The Handy Healing Kits the loop's end bought (<c>bought</c>, report-only).</param>
/// <param name="Pyreals">The coin delta of the sale (<c>pyreals</c>, report-only).</param>
/// <param name="Appraised">The looted items identified with the tables (<c>appraised</c>, report-only).</param>
/// <param name="VendorFrameMaxMs">The slowest frame from a vendor's open being sent to the visit's end (<c>vendor_frame_max_ms</c>, <see cref="VendorFrameSampler"/>), or null when no window was sampled. Never judged here: since M4 4.2 J1 the journey's vendor and vendor-facts station lines print it and smoke judges it (over 75 ms fails, over 50 flags, only uncontended).</param>
/// <param name="HealthStartShare">The health share (0 to 1) the hunt read when it began (the scenario's first health read, <c>health_start_pct</c> on the detail line; <see cref="HuntScenario.HealthStartShare"/>), or null when none was read. With <paramref name="HealEnabled"/> and <paramref name="HealBelowPercent"/> it decides whether <c>heals &gt;= 1</c> is judged (M4 3.2 L2).</param>
/// <param name="HealEnabled"><c>--heal on</c> (the recovery rule was on for the leg).</param>
/// <param name="HealBelowPercent">The recovery trigger, <c>--heal-below</c>, in percent (default 75).</param>
/// <param name="Blocked">The arrows that hit the environment (<c>blocked</c>, M4 P4 bow; counted in <c>misses</c> too; report-only in its first gate, printed after <c>misses=</c>).</param>
/// <param name="Seeks">The times the hunt went to a creature of its band that the client already knew, beyond the acquire range (<c>seeks</c>, M4 P4 seek; report-only in its first gate, printed after <c>blocked=</c>).</param>
/// <param name="OneBlowKills">The kills whose target took no damage line before it died (<c>one_blow_kills</c>, P5a round 3: ACE writes no damage line for a lethal blow, <c>Player_Combat.cs</c> 157 @95ef426d); stands for <c>damage_dealt</c> (and, for the missile hunt, <c>hits</c>) when those are 0. Printed after <c>vendor_frame_max_ms=</c>.</param>
/// <param name="ModeMismatch">The moments the combat mode and the wielded weapon disagreed during the leg (<c>mode_mismatch</c>, M4 3.1 s4 from <see cref="CombatModeWatch"/>; the line prints it after <c>ground=</c>; judged <c>= 0</c> since M4 4.2 J1, D23's combat-mode rule, m4-plan.md section 4.9 row j).</param>
public readonly record struct HuntLegMeasures(
    int Kills,
    long DamageDealt,
    long DamageTaken,
    int HitsTaken,
    int Evades,
    long XpGained,
    string Fighter,
    int Swings,
    int Shots,
    int Hits,
    int Misses,
    bool Returned,
    bool Alive = true,
    HuntWeapon Weapon = HuntWeapon.Melee,
    double? AttackFrameMaxMs = null,
    int CorpsesOpened = 0,
    int CorpseItems = 0,
    int Looted = 0,
    double? LootFrameMaxMs = null,
    int LootSkipped = 0,
    int Heals = 0,
    int KitUses = 0,
    int Potions = 0,
    bool LootEnabled = true,
    HuntGroundChoice Ground = HuntGroundChoice.Belt,
    int ModeMismatch = 0,
    int Sold = 0,
    int Bought = 0,
    int Pyreals = 0,
    int Appraised = 0,
    double? VendorFrameMaxMs = null,
    double? HealthStartShare = null,
    bool HealEnabled = true,
    int HealBelowPercent = 75,
    int Blocked = 0,
    int Seeks = 0,
    int OneBlowKills = 0);

/// <summary>
/// The hunt leg's verdict (<c>--login-check=hunt</c>, M4 1.2 s4b): composed from the scenario's failure (or null) and the measures, so it is built and tested without Godot. The line is
/// <c>hunt done ok=&lt;true|false&gt; kills= damage_dealt= damage_taken= hits_taken= evades= xp_gained= fighter= swings= shots= hits= misses= blocked= seeks= returned=yes|no attack_frame_max_ms=&lt;x|n/a&gt;</c> (M3's six fields in M3's order, then the new
/// ones; no name, no account), logged under the <c>logincheck</c> category.
/// </summary>
/// <remarks>
/// <b>Judged</b> (m4-plan section 4.2, row s4, and DoD 3 and 4): no failure (the run reached done, which includes the wind-down), the player alive, <c>kills &gt;= 1</c> and <c>damage_dealt &gt; 0</c>, as the fight leg judges
/// its own; since M4 1.3 s2 (their first judged gate, after 1.2's report-only one; plan section 5, item 3) <c>swings &gt;= 1</c> for the melee hunt and <c>shots &gt;= 1</c> with <c>hits &gt;= 1</c> for the missile hunt
/// (<c>--hunt-weapon=missile</c>). <c>fighter</c>, <c>misses</c>, <c>returned</c> stay as they were (a wrong character and a failed return already fail the run through its failure), and <c>damage_taken</c>,
/// <c>hits_taken</c>, <c>evades</c> and <c>xp_gained</c> are report-only. Since M4 2.1 s4a <c>attack_frame_max_ms</c> is judged (over 75 ms fails, over 50 ms ends the line with <c>frame_over_50=yes</c>, D11) and
/// <c>corpses_opened</c> and <c>corpse_items</c> are reported (report-only in their first gate). Since M4 2.2 s0 the line ends <c>... corpse_items= looted= loot_frame_max_ms=&lt;x|n/a&gt; loot_skipped=</c> (and
/// <c>loot_frame_over_50=yes</c> after a loot window frame over 50 ms): all report-only in 2.2, a note and never a failure. <b>Since M4 3.1 s3</b> (their second gate, plan section 5 item 3, D11) <c>looted &gt;= 1</c> is judged over a
/// leg of two or more kills (<see cref="LootJudgeMinKills"/>; never on a one-kill leg, never when the hunt was asked not to loot) and <c>loot_frame_max_ms</c> over 75 ms fails; over 50 ms the line ends with <c>frame_over_50=yes</c>, the attack
/// frames' own flag, after <c>loot_frame_over_50=yes</c>. The line gains <c>heals= kit_uses= potions= ground=belt|forest</c> after <c>loot_skipped=</c>: <c>heals kit_uses potions</c> are report-only (<c>heals &gt;= 1</c> is judged from M4 3.2 L2, only over a leg that started under the <c>--heal-below</c> trigger with <c>--heal on</c>). <b>Since M4 3.1 s4</b> <c>mode_mismatch=&lt;n&gt;</c> follows <c>ground=</c> (the <see cref="CombatModeWatch"/> count; report-only in its first gate, a new judge is never judged at once). <b>Since M4 3.2 s4a</b> the line gains <c>sold= bought= pyreals=&lt;delta&gt; appraised= vendor_frame_max_ms=&lt;x|n/a&gt;</c> after
/// <c>mode_mismatch=</c> (before the <c>over_50</c> flags): all report-only here (the journey's vendor station judges the sale; <c>vendor_frame_max_ms</c> is printed on the journey's vendor station lines and judged by smoke). <b>Since M4 4.2 J1</b>
/// <c>mode_mismatch=0</c> is judged (D23's combat-mode rule; m4-plan.md section 4.9 row j).
/// </remarks>
public static class HuntLegVerdict
{
    /// <summary>
    /// The measures of a hunt from the scenario's own numbers alone (no frame is sampled: the frame fields are n/a), for a run without Godot (the headless loopback's <c>hunt_loop verdict</c>) and for the journey's hunt and vendor
    /// stations on both hosts (their window frame times are printed on the station line and judged by smoke, never by the station's own ok: M4 4.2 J1). <c>mode_mismatch</c> is the scenario's <see cref="HuntScenario.ModeWatch"/>
    /// count (0 when it has none). M4 P5a fix r4: round 4's
    /// <c>HuntLiveTests</c> built the measures field by field and left <see cref="HuntLegMeasures.OneBlowKills"/> at its default 0 while the scenario counted three, so the judge failed "damage_dealt=0 ... one_blow_kills=0";
    /// every scenario count the verdict judges is read here, once.
    /// </summary>
    /// <param name="scenario">The hunt that ran.</param>
    /// <param name="weapon">The hunt's weapon.</param>
    /// <param name="lootEnabled">True when the hunt was asked to loot (the <c>looted</c> judge).</param>
    /// <param name="ground">The hunt's ground.</param>
    public static HuntLegMeasures FromScenario(HuntScenario scenario, HuntWeapon weapon, bool lootEnabled, HuntGroundChoice ground)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var tally = scenario.Tally;
        return new HuntLegMeasures(
            Kills: tally.Kills,
            DamageDealt: tally.DamageDealt,
            DamageTaken: tally.DamageTaken,
            HitsTaken: tally.HitsTaken,
            Evades: tally.Evades,
            XpGained: tally.XpGained,
            Fighter: scenario.Fighter,
            Swings: tally.Swings,
            Shots: tally.Shots,
            Hits: tally.Hits,
            Misses: tally.Misses,
            Returned: scenario.Returned,
            Weapon: weapon,
            CorpsesOpened: scenario.CorpsesOpened,
            CorpseItems: scenario.CorpseItems,
            Looted: scenario.Looted,
            LootSkipped: scenario.LootSkipped,
            Heals: scenario.Heals,
            KitUses: scenario.KitUses,
            Potions: scenario.PotionsUsed,
            LootEnabled: lootEnabled,
            Ground: ground,
            Sold: tally.Sold,
            Bought: tally.Bought,
            Pyreals: tally.Pyreals,
            Appraised: tally.Appraised,
            Blocked: tally.Blocked,
            Seeks: tally.Seeks,
            OneBlowKills: scenario.OneBlowKills,
            ModeMismatch: scenario.ModeWatch?.Violations ?? 0);
    }

    /// <summary>A window frame time as the lines print it: one decimal (<c>61.0</c>), or <c>n/a</c> when no window was sampled.</summary>
    /// <param name="ms">The slowest frame, in milliseconds, or null.</param>
    public static string FrameMs(double? ms) => ms is { } value && !double.IsNaN(value) ? value.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";

    /// <summary>The name of the report-only note for <c>attack_frame_max_ms=n/a</c> with a swing or a shot (2.1-R13).</summary>
    public const string AttackFrameNoWindowNote = "NOTE(attack_frame_max_ms=n/a)";

    /// <summary>An attack window whose slowest frame is over this many milliseconds fails the leg (the fight leg's rule; judged from 2.1, D11).</summary>
    public const double FrameFailMs = 75.0;

    /// <summary>An attack window whose slowest frame is over this many milliseconds prints <c>frame_over_50=yes</c> and passes (the lead's worst of three runs reads it).</summary>
    public const double FrameWarnMs = 50.0;

    /// <summary>The kills from which <c>looted &gt;= 1</c> is judged (M4 3.1 s3, <c>m4-hunt-loot-judge</c>): never on a one-kill leg, whose corpse may hold only what the loot rule leaves.</summary>
    public const int LootJudgeMinKills = 2;

    private static bool Over(double? ms, double limit) => ms is { } value && !double.IsNaN(value) && value > limit;

    /// <summary>Composes the leg's verdict.</summary>
    /// <param name="failure">The scenario's or the runner's failure, or null when there was none.</param>
    /// <param name="measures">What the tally and the scenario hold.</param>
    public static HuntLegOutcome Compose(string? failure, in HuntLegMeasures measures)
    {
        var problems = new List<string>();
        if (!string.IsNullOrEmpty(failure))
        {
            problems.Add("the hunt failed: " + failure);
        }

        if (!measures.Alive)
        {
            problems.Add("the player died during the hunt");
        }

        if (measures.Kills < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"kills={measures.Kills} (>= 1 expected)"));
        }

        // P5a round 3: a kill whose target took no damage line before it died is one lethal blow, which ACE never reports as damage (Player_Combat.cs 157 @95ef426d); one_blow_kills >= 1 stands for the damage then.
        if (measures.DamageDealt <= 0 && measures.OneBlowKills < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"damage_dealt={measures.DamageDealt} (> 0 expected, or one_blow_kills >= 1; one_blow_kills={measures.OneBlowKills})"));
        }

        if (measures.Weapon == HuntWeapon.Missile)
        {
            if (measures.Shots < 1)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"shots={measures.Shots} (>= 1 expected for the missile hunt)"));
            }

            if (measures.Hits < 1 && measures.OneBlowKills < 1)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"hits={measures.Hits} (>= 1 expected for the missile hunt, or one_blow_kills >= 1: a lethal arrow has no damage line; one_blow_kills={measures.OneBlowKills})"));
            }
        }
        else if (measures.Swings < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"swings={measures.Swings} (>= 1 expected for the melee hunt)"));
        }

        if (measures.AttackFrameMaxMs is { } worst && !double.IsNaN(worst) && worst > FrameFailMs)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"attack_frame_max_ms={worst:0.0} (at most {FrameFailMs:0} expected)"));
        }

        // M4 3.1 s3 (m4-hunt-loot-judge, D11): judged from this item's gate. looted >= 1 only over a leg of two or more kills (one kill's corpse may hold only items outside the shop mask: 2.2's gate saw it one run in four) and only
        // when the hunt was asked to loot; the loot window's slowest frame over 75 ms fails (an unmeasured one is not judged, as the attack frame).
        if (measures.Kills >= LootJudgeMinKills && measures.LootEnabled && measures.Looted < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"looted={measures.Looted} (>= 1 expected over {measures.Kills} kills; corpses_opened={measures.CorpsesOpened} corpse_items={measures.CorpseItems} loot_skipped={measures.LootSkipped})"));
        }

        // M4 3.2 L2 (plan section 5 item 3: judged from this item's gate): a leg that STARTED under the heal trigger with the recovery on must heal at least once; one that started at or above it is not judged on heals,
        // and neither is a leg with --heal off or no start read (the field reads what it names: heals counts recoveries, so only a leg that needed one is asked for one).
        if (measures.HealEnabled && measures.HealthStartShare is { } start && !double.IsNaN(start) && start * 100.0 < measures.HealBelowPercent && measures.Heals < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"heals={measures.Heals} (>= 1 expected): no heal although the hunt started at {start * 100.0:0}% under the {measures.HealBelowPercent}% trigger"));
        }

        if (measures.LootFrameMaxMs is { } lootWorst && !double.IsNaN(lootWorst) && lootWorst > FrameFailMs)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"loot_frame_max_ms={lootWorst:0.0} (at most {FrameFailMs:0} expected)"));
        }

        // M4 4.2 J1 (m4-plan.md section 4.9 row j; D23's rule: never melee with a caster or a launcher, never magic without a caster, never missile without a launcher): the watch's count, whoever caused it.
        if (measures.ModeMismatch > 0)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{CombatModeWatch.FieldName}={measures.ModeMismatch} (0 expected: the combat mode and the wielded weapon disagreed)"));
        }

        // 2.1-R13, report-only (a new judge): a run that swung or shot and killed but sampled no attack window passes silently otherwise.
        var notes = new List<string>();
        if (measures.Kills >= 1 && (measures.Swings >= 1 || measures.Shots >= 1) && (measures.AttackFrameMaxMs is not { } sampled || double.IsNaN(sampled)))
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{AttackFrameNoWindowNote}: swings={measures.Swings} shots={measures.Shots} kills={measures.Kills}, but no attack window was sampled (report-only; the sampler skips the frame after a saved review frame)"));
        }

        var ok = problems.Count == 0;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"hunt done ok={(ok ? "true" : "false")} kills={measures.Kills} damage_dealt={measures.DamageDealt} damage_taken={measures.DamageTaken} hits_taken={measures.HitsTaken} evades={measures.Evades} xp_gained={measures.XpGained} fighter={measures.Fighter} swings={measures.Swings} shots={measures.Shots} hits={measures.Hits} misses={measures.Misses} blocked={measures.Blocked} seeks={measures.Seeks} returned={(measures.Returned ? "yes" : "no")} attack_frame_max_ms={FrameMs(measures.AttackFrameMaxMs)} corpses_opened={measures.CorpsesOpened} corpse_items={measures.CorpseItems} looted={measures.Looted} loot_frame_max_ms={FrameMs(measures.LootFrameMaxMs)} loot_skipped={measures.LootSkipped} heals={measures.Heals} kit_uses={measures.KitUses} potions={measures.Potions} ground={measures.Ground.ToString().ToLowerInvariant()} {CombatModeWatch.FieldName}={measures.ModeMismatch} sold={measures.Sold} bought={measures.Bought} pyreals={measures.Pyreals} appraised={measures.Appraised} vendor_frame_max_ms={FrameMs(measures.VendorFrameMaxMs)} one_blow_kills={measures.OneBlowKills}{(measures.LootFrameMaxMs is { } lflagged && !double.IsNaN(lflagged) && lflagged > FrameWarnMs ? " loot_frame_over_50=yes" : string.Empty)}{(Over(measures.AttackFrameMaxMs, FrameWarnMs) || Over(measures.LootFrameMaxMs, FrameWarnMs) ? " frame_over_50=yes" : string.Empty)}");
        return new HuntLegOutcome(ok, line, problems, notes);
    }
}

/// <summary>The hunt leg's verdict: whether it held, the line to log (after the <c>[logincheck]</c> category) and, when it did not hold, what did not.</summary>
/// <param name="Ok">True when the judged fields hold.</param>
/// <param name="Line">The verdict line, <c>hunt done ok=... </c>.</param>
/// <param name="Problems">What did not hold, one sentence each; empty when <paramref name="Ok"/>.</param>
/// <param name="Notes">Report-only remarks (never fail the leg), one sentence each; the runner logs them once (2.1-R13).</param>
public sealed record HuntLegOutcome(bool Ok, string Line, IReadOnlyList<string> Problems, IReadOnlyList<string>? Notes = null);

/// <summary>
/// The hunt leg's two review frames (<c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c>; M4 1.2 s4b): <c>swing</c> (melee: 0.3 s after the first own swing of a sequence, the target within
/// <see cref="SwingRangeMetres"/>) and <c>arrow</c> (missile: 0.1 s after the first arrow's CreateObject F745, while that arrow exists). The leg takes the frame its weapon gives; the other slot is reported as not
/// applicable. A trigger whose condition fails at the frame's moment is dropped and waits for the next one. The plan is pure (the caller reads the session and saves the frame) and allocates nothing per frame.
/// </summary>
public sealed class HuntShotPlan
{
    /// <summary>The slots' names, in the order of the files <c>&lt;stem&gt;-swing.png</c> and <c>-arrow.png</c>.</summary>
    public static readonly string[] Names = ["swing", "arrow"];

    /// <summary>The seconds after each trigger at which the frame is taken.</summary>
    public static readonly double[] DelaysSeconds = [0.3, 0.1];

    /// <summary>What triggers each slot, for the log.</summary>
    public static readonly string[] Triggers = ["the first own swing F74C of an attack sequence, the target within 6 m", "the first arrow's CreateObject F745, the arrow still in the world"];

    /// <summary>How near the target must be for the swing frame, in metres.</summary>
    public const double SwingRangeMetres = 6.0;

    /// <summary>The longest, in seconds, the scenario is held for a frame after its trigger.</summary>
    public const double HoldSeconds = 3.0;

    /// <summary>The frames the developer HUD stays hidden before a frame is saved.</summary>
    public const int HiddenFramesBeforeShot = 2;

    private bool _pending;
    private bool _taken;
    private double _triggeredAt;
    private int _dropped;
    private string? _failedWhy;

    /// <summary>The plan for <paramref name="weapon"/>.</summary>
    public HuntShotPlan(HuntWeapon weapon) => Slot = weapon == HuntWeapon.Missile ? 1 : 0;

    /// <summary>The slot (index into <see cref="Names"/>) this plan takes.</summary>
    public int Slot { get; }

    /// <summary>The frame's name.</summary>
    public string Name => Names[Slot];

    /// <summary>True while a trigger has come and the frame is not yet taken or dropped: the scenario is held.</summary>
    public bool Owed => _pending;

    /// <summary>True when the frame was taken.</summary>
    public bool Taken => _taken;

    /// <summary>True while a trigger may still be accepted.</summary>
    public bool Waiting => !_pending && !_taken && _failedWhy is null;

    /// <summary>A trigger came at <paramref name="now"/>; ignored unless the plan is waiting.</summary>
    public void Trigger(double now)
    {
        if (Waiting)
        {
            _pending = true;
            _triggeredAt = now;
        }
    }

    /// <summary>True when the frame is due: the slot's delay has passed since the trigger and the HUD has been hidden for <see cref="HiddenFramesBeforeShot"/> frames.</summary>
    public bool Due(double now, int hiddenFrames) => _pending && now - _triggeredAt >= DelaysSeconds[Slot] && hiddenFrames >= HiddenFramesBeforeShot;

    /// <summary>True when the frame has been owed for more than <see cref="HoldSeconds"/>.</summary>
    public bool Expired(double now) => _pending && now - _triggeredAt > HoldSeconds;

    /// <summary>The frame was saved.</summary>
    public void MarkTaken()
    {
        _pending = false;
        _taken = true;
    }

    /// <summary>The trigger's condition failed at the frame's moment (the target went out of 6 m, the arrow was gone): drop it and wait for the next trigger.</summary>
    public void Drop()
    {
        _pending = false;
        _dropped++;
    }

    /// <summary>The frame could not be saved: given up.</summary>
    public void MarkFailed(string why)
    {
        _pending = false;
        _failedWhy = why;
    }

    /// <summary>One sentence for the log: taken, given up with the reason, or never triggered (with the number of dropped triggers).</summary>
    public string Note() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Name}: {(_taken ? "taken" : _failedWhy is not null ? "given up (" + _failedWhy + ")" : "not taken (trigger '" + Triggers[Slot] + "' never held)")}{(_dropped > 0 ? ", " + _dropped + " trigger(s) dropped" : string.Empty)}; {Names[1 - Slot]}: n/a (the weapon is {(Slot == 0 ? "melee" : "missile")})");

    /// <summary>The path of a frame: <c>&lt;stem&gt;-&lt;name&gt;.png</c> (the stem is the path without its extension).</summary>
    public static string ShotPath(string path, int slot) => Path.ChangeExtension(path, null) + "-" + Names[slot] + ".png";
}
