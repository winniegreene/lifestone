namespace Lifestone.Client.Session.Grounds;

/// <summary>A ladder file that cannot load: the file and the field's JSON path are named (M6 DoD 1, the profiles' D43 rule).</summary>
public sealed class LadderException : Exception
{
    /// <summary>A ladder error.</summary>
    /// <param name="file">The ladder's file name.</param>
    /// <param name="path">The field's JSON path (for example <c>$.rungs[2].move_on.level</c>).</param>
    /// <param name="problem">What is wrong.</param>
    public LadderException(string file, string path, string problem)
        : base($"ladder file {file}: {path} {problem}")
    {
        File = file;
        JsonPath = path;
    }

    /// <summary>The ladder's file name.</summary>
    public string File { get; }

    /// <summary>The field's JSON path.</summary>
    public string JsonPath { get; }
}

/// <summary>What a rung asks of the character: a hunting ground, or quests.</summary>
public enum LadderRungKind
{
    /// <summary>A hunt on one <see cref="HuntingGround"/> profile.</summary>
    Ground,

    /// <summary>Quests run in order (their keys are the quest catalog's).</summary>
    Quest,
}

/// <summary>A rung's character levels, both ends inclusive.</summary>
/// <param name="From">The first level of the rung.</param>
/// <param name="To">The last level of the rung.</param>
public sealed record LadderBand(int From, int To)
{
    /// <summary>True when <paramref name="level"/> is in the band.</summary>
    /// <param name="level">A character level.</param>
    public bool Holds(int level) => level >= From && level <= To;
}

/// <summary>
/// The move-on rule (M6 DoD 1; TODO(m6-ladder-move-on): the values are the optimizer seat's plan data, m6-plan.md §2.1). <see cref="Level"/> is the level at which the character leaves
/// the rung for the next one (the last rung's is the ladder's top level: the climb ends). The floor and the ceiling are the rung's health check: a rung that makes fewer kills an hour
/// than the floor, or spends more casts a kill than the ceiling, is not paying as the plan models it; what the job then does is the <c>level up</c> job's (slice a4) and the optimizer's.
/// A quest rung has neither (null).
/// </summary>
/// <param name="Level">The level that moves the character on.</param>
/// <param name="KillsAnHourFloor">The kills-an-hour floor, or null on a quest rung.</param>
/// <param name="CastsAKillCeiling">The casts-a-kill ceiling, or null on a quest rung.</param>
public sealed record LadderMoveOn(int Level, int? KillsAnHourFloor, double? CastsAKillCeiling);

/// <summary>
/// The war spell levels the plan expects on a rung. A plan value from the ladder file, never a constant (D26): M6 item 1 a computes it by the fizzle rule (D4, D41: the level at the ladder's
/// <see cref="Ladder.PlanCastChance"/> on the climber's projected base War Magic at the band's first level (min) and its last (max), <c>Mage.SpellPlan.PlanFor</c>), pinned against the computation by <c>SpellPlanTests</c>.
/// </summary>
/// <param name="Min">The lowest spell level of the rung.</param>
/// <param name="Max">The highest spell level of the rung.</param>
public sealed record LadderSpellLevel(int Min, int Max);

/// <summary>
/// M6 7 R: a creature whose hit the climber's health floor is measured by (<c>Mage.ClimberHealthRule</c>). The two inputs are the world's (D28): the body part's <c>DVal</c> (its largest, <c>weenie_properties_body_part</c>) and the creature's Strength
/// (<c>weenie_properties_attribute</c>, type 1), each with its SELECT in the rung's <c>sources.health</c>.
/// </summary>
/// <param name="Name">The creature's name.</param>
/// <param name="Wcid">Its weenie class id.</param>
/// <param name="DVal">The largest <c>DVal</c> of its attacking body parts: ACE's <c>BaseDamage.MaxDamage</c> (@95ef426d <c>WorldObjects/Monster_Melee.cs:385-404</c>).</param>
/// <param name="DVar">That part's <c>DVar</c>: ACE's <c>BaseDamage.Variance</c>: a hit is uniform between <c>DVal x (1 - DVar)</c> and <c>DVal</c> (<c>Entity/BaseDamage.cs</c>).</param>
/// <param name="Strength">Its Strength.</param>
public sealed record LadderHitter(string Name, uint Wcid, int DVal, double DVar, int Strength)
{
    /// <summary>ACE's attribute factor for a melee or thrown hit (<c>SkillFormula.DefaultMod</c>, @95ef426d <c>WorldObjects/SkillFormula.cs:11</c>).</summary>
    public const double MeleeAttributeFactor = 0.011;

    /// <summary>ACE's attribute factor for a launcher's shot, on Coordination (<c>SkillFormula.BowMod</c>, <c>SkillFormula.cs:14</c>).</summary>
    public const double BowAttributeFactor = 0.008;

    /// <summary>ACE's critical chance of a creature's physical attack with no CriticalFrequency on its weapon (<c>defaultPhysicalCritFrequency</c>, @95ef426d <c>WorldObjects/WorldObject_Weapon.cs:291</c>).</summary>
    public const double DefaultCriticalChance = 0.1;

    /// <summary>ACE's CriticalMultiplier with none on the weapon (<c>defaultCritDamageMultiplier</c>, <c>WorldObject_Weapon.cs:356</c>): a critical is the largest hit x (1 + it) (<c>DamageEvent.cs:276,287</c>).</summary>
    public const double DefaultCriticalMultiplier = 1.0;

    /// <summary>
    /// M6 14 the losing race: the attribute modifier's factor, <see cref="MeleeAttributeFactor"/> on Strength for a body part, a melee weapon or a thrown one, <see cref="BowAttributeFactor"/> on Coordination for a launcher's shot
    /// (<see cref="Strength"/> then holds the Coordination).
    /// </summary>
    public double AttributeFactor { get; init; } = MeleeAttributeFactor;

    /// <summary>M6 14 the losing race: the chance one of its hits is a critical (its weapon's CriticalFrequency, else <see cref="DefaultCriticalChance"/>).</summary>
    public double CriticalChance { get; init; } = DefaultCriticalChance;

    /// <summary>M6 14 the losing race: its weapon's CriticalMultiplier, else <see cref="DefaultCriticalMultiplier"/>: a critical is <see cref="CriticalFactor"/> times the largest hit.</summary>
    public double CriticalMultiplier { get; init; } = DefaultCriticalMultiplier;

    /// <summary>A critical's multiple of the largest uncritical hit: <c>1 + CriticalMultiplier</c> (ACE's <c>CriticalDamageMod</c> on <c>BaseDamageMod.MaxDamage</c>, <c>DamageEvent.cs:276,287</c>); 2 with no weapon.</summary>
    public double CriticalFactor => 1.0 + CriticalMultiplier;

    /// <summary>ACE's attribute modifier <c>max(1, 1 + (attribute - 55) x factor)</c> (<c>SkillFormula.GetAttributeMod</c>, <c>SkillFormula.cs:18-23</c>).</summary>
    public double AttributeMod => Math.Max(1.0, 1.0 + ((Strength - 55) * AttributeFactor));

    /// <summary>ACE's mean uncritical unarmoured hit, rounded up: <c>DVal x (1 - DVar / 2)</c> x the attribute modifier <c>max(1, 1 + (Strength - 55) x 0.011)</c> (@95ef426d <c>Entity/DamageEvent.cs</c> <c>DoCalculateDamage</c>, <c>WorldObjects/SkillFormula.cs:18-23</c>).</summary>
    public int Hit => (int)Math.Ceiling(DVal * (1 - (DVar / 2)) * AttributeMod);
}

/// <summary>M6 7 R: who a rung's health floor is measured by.</summary>
/// <param name="Hitter">The ground's hardest hitter.</param>
/// <param name="RoadHitter">The hardest hitter of the road to the ground, where the rung's route runs the island (below the hub's gate at 15), else null.</param>
/// <param name="Pack">M6 7 AY (the owner, 2026-11-06: the rule counts the whole pack): how many of <see cref="Hitter"/> the rung sends at her at once, data from the surveys (<c>health.pack</c>, its reason in the rung's <c>sources.health</c>). 1 is one attacker. A road hitter is met alone.</param>
public sealed record LadderHealth(LadderHitter Hitter, LadderHitter? RoadHitter, int Pack = 1);

/// <summary>An alternate ground of a rung (the spec §5.4's alternations): a profile key that resolves, or a pending one with its reason.</summary>
/// <param name="GroundKey">The <see cref="HuntingGround"/> key.</param>
/// <param name="Landblock">The ground's landblock as the spec names it, or null (an outdoor ground over several).</param>
/// <param name="Pending">True while the profile is not written.</param>
/// <param name="PendingReason">Which item owns the profile ("profile due in item 3"), or null when not pending.</param>
public sealed record LadderAlternate(string GroundKey, ushort? Landblock, bool Pending, string? PendingReason);

/// <summary>One rung of a <see cref="Ladder"/>.</summary>
/// <param name="Key">The rung's key (unique in the ladder).</param>
/// <param name="Name">The rung's name for logs and the panel.</param>
/// <param name="Kind">A ground or quests.</param>
/// <param name="Band">The character levels of the rung.</param>
/// <param name="GroundKey">The <see cref="HuntingGround"/> key (kind ground), else null.</param>
/// <param name="QuestKeys">The quest keys in order (kind quest), else empty; resolved against the quest catalog by a later slice.</param>
/// <param name="Landblock">The ground's landblock as the spec names it, or null.</param>
/// <param name="Alternates">The alternate grounds, possibly none.</param>
/// <param name="MoveOn">The move-on rule.</param>
/// <param name="SpellLevel">The plan's war spell levels.</param>
/// <param name="Element">The war element (a damage type: fire, cold, acid, electric, pierce, bludgeon, slash), or null on a quest rung.</param>
/// <param name="TargetMargin">The levels above the character's own that the fight's pick still takes on this rung (M6 0 F5, review 0-R5, D26: a rung's fact, never a constant); null on a quest rung and on a pending ground rung (the profile carries it). A dungeon's pick is not banded by this margin: the fight's pick takes every target of a dungeon at any character level (the margin reaches only the outdoor band, FightScenario.Outdoor.cs; TODO(m6-dungeon-band)).</param>
/// <param name="Route">How to get there: a route key (<see cref="Ladder.RouteKinds"/>); item 4 resolves it.</param>
/// <param name="Pending">True while the ground's profile is not written.</param>
/// <param name="PendingReason">Which item owns the profile, or null when not pending.</param>
/// <param name="Sources">Where every value of the rung comes from, by field (<see cref="Ladder.RungSourceNames"/>).</param>
/// <param name="Health">M6 7 R: who the climber's health floor is measured by on this rung, or null with the reason in <c>sources.health</c> (a quest rung, the Holding).</param>
public sealed record LadderRung(
    string Key,
    string Name,
    LadderRungKind Kind,
    LadderBand Band,
    string? GroundKey,
    IReadOnlyList<string> QuestKeys,
    ushort? Landblock,
    IReadOnlyList<LadderAlternate> Alternates,
    LadderMoveOn MoveOn,
    LadderSpellLevel SpellLevel,
    string? Element,
    int? TargetMargin,
    string Route,
    bool Pending,
    string? PendingReason,
    IReadOnlyDictionary<string, string> Sources,
    LadderHealth? Health = null);

/// <summary>M6 7 AK: one scroll of <see cref="Ladder.FirstScrolls"/>: a line of the scrivener catalog and its level.</summary>
/// <param name="Line">The catalog line's name.</param>
/// <param name="Level">The level.</param>
public sealed record ScrollGoal(string Line, int Level);

/// <summary>M6 7 AK: one line of a <see cref="RungGate"/>.</summary>
/// <param name="Line">The buff profile's line name.</param>
/// <param name="Level">The least level.</param>
/// <param name="Highest">True: the highest level she can reach, at least <paramref name="Level"/>.</param>
public sealed record GateLine(string Line, int Level, bool Highest);

/// <summary>M6 7 AK: what must hold before a rung.</summary>
/// <param name="Rung">The rung's key.</param>
/// <param name="Skills">The skills that must be trained.</param>
/// <param name="Lines">The lines that must stand on her.</param>
public sealed record RungGate(string Rung, IReadOnlyList<uint> Skills, IReadOnlyList<GateLine> Lines);

/// <summary>M6 7 AN: the fight's vitals rules that hold on every ground of the ladder (D26: data, each with its source sentence in the file).</summary>
/// <param name="DrainUnderPercent"><c>drain_under_percent</c>: at or below this health share she drains (the model's sweep at a pack of five: 30% 43, 40% 39, 50% 35, 60% 44 percent deaths).</param>
/// <param name="ManaLoopUnder"><c>mana_loop_under</c>: under this mana the fight's mana loop runs.</param>
/// <param name="ManaLoopStaminaPercent"><c>mana_loop_stamina_percent</c>: at or over this stamina share the loop presses; under it, it drains stamina first.</param>
/// <param name="StaminaDrainMinGive"><c>stamina_drain_min_give</c>: the least stamina one Drain Stamina Other must give to be cast.</param>
/// <param name="BuffPassWaitSeconds"><c>buff_pass_wait_seconds</c>: the named limit of one buff pass's mana: past it the pass goes on with what stands and names what is missing, and those lines are not due again for as long.</param>
/// <param name="PassHomeOverLines"><c>pass_home_over_lines</c>: a pass with more lines than this to cast, and a creature near her, walks home first (<see cref="Mage.PassPlaceRule"/>).</param>
/// <param name="RefreshManaReserve"><c>refresh_mana_reserve</c>: M6 7 AZ: the mana a refresh in a fight keeps for the mana loop and a drain: the fight starts no refresh pass for a line that is not under the hard floor below it, and a pass that has to wait for mana in a fight ends.</param>
/// <param name="ManaPressMinBolts"><c>mana_press_min_bolts</c>: M6 8 H: the share of the next bolt's mana a press must net to be sent before a bolt that can still be paid (the climber's mana loop by the sum).</param>
/// <param name="StaminaDrainMinBolts"><c>stamina_drain_min_bolts</c>: M6 8 H: the share of the next bolt's mana a drain and its press must net together.</param>
/// <param name="ManaLoopMaxAttackers"><c>mana_loop_max_attackers</c>: M6 8 H: at more than this many hitting her the bolts come first.</param>
/// <param name="ManaLoopBoltFirstHits"><c>mana_loop_bolt_first_hits</c>: M6 8 H: a target within this many mean bolts of death takes the bolt first.</param>
/// <param name="ManaLoopBoltsBetween"><c>mana_loop_bolts_between</c>: M6 8 H: the bolts sent after a press before the next mana cast (when the bolt can be paid).</param>
/// <param name="StartOnlyLines"><c>start_only_lines</c>: the lines cast only at a pass's start; every other line is kept up during a fight (the owner decides; default none: all lines kept up).</param>
/// <param name="StaminaKeepPercent"><c>stamina_keep_percent</c>: M6 14 (the owner, 2026-11-13: drain stamina to keep efficiency): under this stamina share the climber's loop drains the fullest target in reach, valued in stamina (the stamina line, <see cref="Vitals.ManaLoopRule.DecideBySum"/>).</param>
/// <param name="StaminaDrainMaxAttackers"><c>stamina_drain_max_attackers</c>: M6 14: at more than this many hitting her the stamina line does not drain.</param>
/// <param name="ManaPressFillPercent"><c>mana_press_fill_percent</c>: M6 14: a press is sent before a bolt she can pay only when the mana she is missing takes at least this share of its give (the press fills, it does not top up).</param>
public sealed record LadderVitals(int DrainUnderPercent, int ManaLoopUnder, int ManaLoopStaminaPercent, int StaminaDrainMinGive, double BuffPassWaitSeconds = 900, IReadOnlyList<string>? StartOnlyLines = null, int PassHomeOverLines = 4, int RefreshManaReserve = 60, double ManaPressMinBolts = 0.5, double StaminaDrainMinBolts = 1.0, int ManaLoopMaxAttackers = 2, double ManaLoopBoltFirstHits = 1, int ManaLoopBoltsBetween = 2, int StaminaKeepPercent = 80, int StaminaDrainMaxAttackers = 4, int ManaPressFillPercent = 90)
{
    /// <summary>True when <paramref name="line"/> is cast only at a start, not kept up during a fight.</summary>
    /// <param name="line">The line's name.</param>
    public bool StartOnly(string line) => StartOnlyLines is { } lines && lines.Contains(line, StringComparer.Ordinal);
}

/// <summary>M6 7 AY (the owner, 2026-11-06): the coin half of a corpse run (<see cref="Mage.CorpseCoinRule"/>): when a corpse is fetched for its coin alone.</summary>
/// <param name="OnCorpse"><c>coin_on_corpse</c>: true when a dead player's coin lies on the corpse (retail's rule, which the server mod sets; M6 7 finish). The server's <c>corpse_destroy_pyreals</c> on means it is destroyed (ACE @95ef426d <c>WorldObjects/Player_Death.cs:569-578</c>), and there is nothing to fetch.</param>
/// <param name="MinCoin"><c>min_coin</c>: the least coin worth the walk: a level's income.</param>
/// <param name="MinItemValue"><c>min_item_value</c> (optional, M6 7 BB): the least Value of an item that is worth a walk to the corpse when she needs nothing else; default 20,000.</param>
/// <param name="TripStallSeconds"><c>trip_stall_seconds</c> (M6 8 I): for a corpse she fetches, the trip is given up when the body has not moved <paramref name="TripStallMetres"/> in this many seconds (fights and heal holds do not count); 0: the trip keeps the elapsed-time limit (<c>--death-recover-abort-seconds</c>).</param>
/// <param name="TripStallMetres"><c>trip_stall_metres</c> (M6 8 I): the metres that count as progress (the watchdogs' 10).</param>
public sealed record LadderCorpseCoin(bool OnCorpse, int MinCoin, int MinItemValue = 20000, double TripStallSeconds = 0, double TripStallMetres = 10);

/// <summary>M6 8 K (the owner, 2026-11-09: one survival layer above every activity, D26: data): the lines of the layer (<see cref="Autopilot.SurvivalRule"/>), in percent of the maximum.</summary>
/// <param name="WaitHealth"><c>wait_health_percent</c>: under this health share, no hostile on her, the activity waits.</param>
/// <param name="BackHealth"><c>back_health_percent</c>: the health share a wait goes on to.</param>
/// <param name="WaitMana"><c>wait_mana_percent</c>: under this mana share, no hostile on her, the activity waits.</param>
/// <param name="BackMana"><c>back_mana_percent</c>: the mana share a wait goes on to.</param>
/// <param name="WaitStamina"><c>wait_stamina_percent</c>: under this stamina share, no hostile on her, the activity waits.</param>
/// <param name="BackStamina"><c>back_stamina_percent</c>: the stamina share a wait goes on to.</param>
/// <param name="LeaveHealth"><c>leave_health_percent</c>: under this health share (or the vitals guard's own line) with mana under <paramref name="LeaveMana"/>, attacked and no heal payable, she leaves.</param>
/// <param name="LeaveMana"><c>leave_mana_percent</c>: the mana share of the leave.</param>
/// <param name="StartHealth"><c>start_health_percent</c>: the health share a ladder errand starts at.</param>
/// <param name="StartMana"><c>start_mana_percent</c>: the mana share a ladder errand starts at.</param>
/// <param name="StartStamina"><c>start_stamina_percent</c>: the stamina share a ladder errand starts at.</param>
public sealed record LadderSurvival(int WaitHealth, int BackHealth, int WaitMana, int BackMana, int WaitStamina, int BackStamina, int LeaveHealth, int LeaveMana, int StartHealth, int StartMana, int StartStamina)
{
    /// <summary>No lines: the layer waits for nothing and leaves for nothing (a ladder built without the section).</summary>
    public static LadderSurvival Off { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>M6 7 AY (the owner, 2026-11-06): big packs stay allowed; two deaths to one drop her back to the first slots until her mana grows (<see cref="Mage.BigPackRule"/>).</summary>
/// <param name="Awake"><c>awake</c>: the creatures awake around her at her death that make it a big pack.</param>
/// <param name="ReachMetres"><c>reach_m</c>: how near a creature must stand to count as awake.</param>
/// <param name="Deaths"><c>deaths</c>: the deaths to a big pack that drop her back.</param>
/// <param name="ManaGrows"><c>mana_grows</c>: the rise of her base mana that lifts the drop-back.</param>
public sealed record LadderBigPack(int Awake, double ReachMetres, int Deaths, int ManaGrows);

/// <summary>
/// A ladder (M6 DoD 1): the ordered rungs a character climbs from level 1 to the ladder's top level, read from an embedded JSON file by <see cref="LadderLoader"/>. Immutable; every
/// field is checked at load (bands contiguous from 1 to the top, every non-pending ground resolved through <see cref="GroundCatalog"/>).
/// </summary>
public sealed class Ladder
{
    /// <summary>The spell ids of <see cref="FirstScrolls"/> are the catalog's; a goal names its line and level.</summary>
    internal const string FirstScrollsField = "first_scrolls";

    internal Ladder(string key, string name, int topLevel, double planCastChance, IReadOnlyList<LadderRung> rungs, IReadOnlyDictionary<string, string> sources, double healthHits = 0, int healthPayPercent = 0, double planBuffChance = 0, IReadOnlyList<ScrollGoal>? firstScrolls = null, IReadOnlyList<RungGate>? gates = null, LadderVitals? vitals = null, LadderCorpseCoin? corpseCoin = null, LadderBigPack? bigPack = null, Mage.StatCap? statCap = null, LadderSurvival? survival = null)
    {
        Survival = survival ?? LadderSurvival.Off;
        CorpseCoin = corpseCoin ?? new LadderCorpseCoin(false, 1);
        BigPack = bigPack ?? new LadderBigPack(int.MaxValue, 0, int.MaxValue, 1);
        Gates = gates ?? [];
        Vitals = vitals ?? new LadderVitals(50, 30, 50, 30);
        PlanBuffChance = planBuffChance > 0 ? planBuffChance : planCastChance;
        FirstScrolls = firstScrolls ?? [];
        HealthHits = healthHits;
        HealthPayPercent = healthPayPercent;
        StatCap = statCap;
        Key = key;
        Name = name;
        TopLevel = topLevel;
        PlanCastChance = planCastChance;
        Rungs = rungs;
        Sources = sources;
    }

    /// <summary>The route key's kinds (the text before the colon); item 4 resolves each: <c>start:&lt;place&gt;</c> (where the character is created), <c>walk:&lt;town&gt;</c> (a walk from
    /// the town), <c>town-network:&lt;town&gt;</c> (one Town Network hop, then a walk), <c>water-run:&lt;town&gt;</c> (a surface run with water from the town; every island run plans and arrives on the dats since M6 4 W3 and W5, TODO(m6-island-water-run) closed).</summary>
    public static IReadOnlyList<string> RouteKinds { get; } = Array.AsReadOnly(new[] { "start", "walk", "town-network", "water-run" });

    /// <summary>The war elements a rung may name (the damage types of the war bolt lines, ladder note §7).</summary>
    public static IReadOnlyList<string> Elements { get; } = Array.AsReadOnly(new[] { "fire", "cold", "acid", "electric", "pierce", "bludgeon", "slash" });

    /// <summary>The fields every rung names a source for (the keys of <see cref="LadderRung.Sources"/>).</summary>
    public static IReadOnlyList<string> RungSourceNames { get; } = Array.AsReadOnly(new[] { "band", "ground", "alternates", "move_on", "spell_level", "element", "target_margin", "route", "health" });

    /// <summary>The fields the ladder names a source for (the keys of <see cref="Sources"/>).</summary>
    public static IReadOnlyList<string> SourceNames { get; } = Array.AsReadOnly(new[] { "ladder", "top_level", "bands", "move_on_rule", "spell_level", "plan_cast_chance", "plan_buff_chance", "vitals", "first_scrolls", "rung_gate", "pending", "health_hits", "health_pay_percent", "stat_cap", "corpse_coin", "big_pack", "survival", "vitals_policy", "caster_score", "wear_score" });

    /// <summary>The ladder's key (its file name without <c>.json</c>).</summary>
    public string Key { get; }

    /// <summary>M6 7 AK (the owner, 2026-11-06; it changes D4 and D41 for buffs only): the bar a buff level must reach on the live skill, buffed and under vitae, read again after each landed step. <see cref="PlanCastChance"/> stays the bar of combat.</summary>
    public double PlanBuffChance { get; }

    /// <summary>M6 7 AY: the coin half of a corpse run (<see cref="LadderCorpseCoin"/>).</summary>
    public LadderCorpseCoin CorpseCoin { get; }

    /// <summary>M6 8 K: the survival layer's lines (<see cref="LadderSurvival"/>).</summary>
    public LadderSurvival Survival { get; }

    /// <summary>M6 14 (<c>m6-vitals-policy</c>): the climber's fills by the numbers (<see cref="LadderVitalsPolicy"/>; <see cref="LadderVitalsPolicy.Off"/> when the file has no section).</summary>
    public LadderVitalsPolicy VitalsPolicy { get; init; } = LadderVitalsPolicy.Off;

    /// <summary>M6 14 (<c>m6-caster</c>): the caster pick's weights (<see cref="LadderCasterScore"/>; <see cref="LadderCasterScore.Default"/> when the file has no section).</summary>
    public LadderCasterScore CasterScore { get; init; } = LadderCasterScore.Default;

    /// <summary>M6 14 (<c>m6-armour</c>): the armour and jewellery pick's weights (<see cref="LadderWearScore"/>; <see cref="LadderWearScore.Default"/> when the file has no section).</summary>
    public LadderWearScore WearScore { get; init; } = LadderWearScore.Default;

    /// <summary>A test's copy with another <see cref="CorpseCoin"/>: what the rule does on a server that keeps the coin on the corpse.</summary>
    /// <param name="coin">The values.</param>
    internal Ladder WithCorpseCoin(LadderCorpseCoin coin) => new(Key, Name, TopLevel, PlanCastChance, Rungs, Sources, HealthHits, HealthPayPercent, PlanBuffChance, FirstScrolls, Gates, Vitals, coin, BigPack, StatCap) { VitalsPolicy = VitalsPolicy, CasterScore = CasterScore, WearScore = WearScore };

    /// <summary>M6 7 AY: the big pack rule (<see cref="LadderBigPack"/>).</summary>
    public LadderBigPack BigPack { get; }

    /// <summary>M6 7 AN: the fight's vitals rules on every ground (<see cref="LadderVitals"/>).</summary>
    public LadderVitals Vitals { get; }

    /// <summary>M6 7 AK (the owner), AR (the Burrow's own): what must hold before each gated rung, in climbing order; the move-on level alone no longer sends her (<see cref="Mage.RungGateRule"/>).</summary>
    public IReadOnlyList<RungGate> Gates { get; }

    /// <summary>The gate of the rung <paramref name="rung"/>, or null when it has none.</summary>
    /// <param name="rung">The rung's key.</param>
    public RungGate? GateOf(string rung) => Gates.FirstOrDefault(g => string.Equals(g.Rung, rung, StringComparison.Ordinal));

    /// <summary>M6 7 AK (the owner): the scrolls the coin buys first, in order, and the smallest set that makes the last of them cast at <see cref="PlanBuffChance"/>; while one is missing and in reach, she saves for them.</summary>
    public IReadOnlyList<ScrollGoal> FirstScrolls { get; }

    /// <summary>The ladder's name.</summary>
    public string Name { get; }

    /// <summary>The last level of the climb (the last rung's band ends here).</summary>
    public int TopLevel { get; }

    /// <summary>
    /// M6 item 1 a (D4, D41): the plan's cast chance, in (0, 1]: a rung's <see cref="LadderRung.SpellLevel"/> is the highest level cast at this chance on the projected base War Magic, and the level up job caps its bolt and
    /// casts at this chance on the current skill. A ladder source value (<c>plan_cast_chance</c>), never a constant in code.
    /// </summary>
    public double PlanCastChance { get; }

    /// <summary>M6 7 R: N, the hits of the rung's hardest hitter the climber's base health must take (<c>health_hits</c>, a ladder value, its reason in <c>sources.health_hits</c>).</summary>
    public double HealthHits { get; }

    /// <summary>M6 7 R: the most of the experience earned the health floor may take, in percent (<c>health_pay_percent</c>, its reason in <c>sources.health_pay_percent</c>).</summary>
    public int HealthPayPercent { get; }

    /// <summary>M6 7 finish (the owner, 2026-11-08): the cap on each attribute and Max Health (<c>stat_cap</c>), or null on a ladder with none.</summary>
    public Mage.StatCap? StatCap { get; }

    /// <summary>The rungs in climbing order; their bands run without a gap from level 1 to <see cref="TopLevel"/>.</summary>
    public IReadOnlyList<LadderRung> Rungs { get; }

    /// <summary>Where the ladder-wide values and rules come from.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>The rung whose band holds <paramref name="level"/>.</summary>
    /// <param name="level">A character level, 1 to <see cref="TopLevel"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside the ladder.</exception>
    public LadderRung RungFor(int level)
    {
        if (level < 1 || level > TopLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"ladder '{Key}' covers levels 1 to {TopLevel}; level {level} is outside it");
        }

        foreach (var rung in Rungs)
        {
            if (rung.Band.Holds(level))
            {
                return rung;
            }
        }

        throw new InvalidOperationException($"ladder '{Key}' has no rung for level {level} (the loader checks the bands are contiguous; this is a bug)");
    }
}
