using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Creatures;

/// <summary>
/// M6 16 E1a (docs/automation-engine.md section 2.1, m6-plan.md D139): what one creature kind can do, by weenie class, as the one reader gives it (<see cref="Bestiary"/>). The groups are the design's: identity, vitals, defence,
/// offence, timing facts, movement, awareness, reward, body. Each field holds what the sources hold and nothing else: a field no source holds is null; none is guessed. What only the
/// dats hold (the seconds of an attack, the pace of a run) is not here: it stays a run-time join (<see cref="AttackTimings"/>, <see cref="CreaturePace"/>). What is learned live (a drain measured at 0) is not here
/// either (<see cref="DrainResist.Of"/>).
/// <para>
/// M6 16 E1d: the fields the world database's behaviour table fills (<see cref="CreatureBehaviour"/>: the stamina and mana, the rates, the drain resistances, the temper, the experience, the Run skill, the body, and the table's own
/// visual and aural range) sit BESIDE the values the automation reads today (the pinned awareness, a ground profile's pace and experience); no reader of the automation reads them yet. Each is null only for a kind that table
/// does not hold.
/// </para>
/// </summary>
/// <param name="Identity">Which kind it is.</param>
/// <param name="Vitals">Its vitals.</param>
/// <param name="Defence">What it resists.</param>
/// <param name="Offence">What it attacks with.</param>
/// <param name="Timing">What ACE times its attacks by (the world database's table), or null for a kind the table does not hold.</param>
/// <param name="Movement">How it moves.</param>
/// <param name="Awareness">How near it notices her.</param>
/// <param name="Reward">What its kill gives.</param>
/// <param name="Body">Its kind of creature and its body.</param>
public sealed record CreatureCapabilities(
    CreatureIdentity Identity,
    CreatureVitals Vitals,
    CreatureDefence Defence,
    CreatureOffence Offence,
    CreatureTimingFacts? Timing,
    CreatureMovement Movement,
    CreatureSenses Awareness,
    CreatureReward Reward,
    CreatureBody Body)
{
    /// <summary>Its weenie class id (<see cref="CreatureIdentity.Wcid"/>).</summary>
    public uint Wcid => Identity.Wcid;
}

/// <summary>A creature kind's identity (<see cref="CreatureCapabilities"/>): a ground profile's facts first, then the world database's table (<see cref="CreatureFacts.Find"/>).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level (0: not given).</param>
/// <param name="Where">Where its facts are recorded, in words (<see cref="CreatureFact.Where"/>: a profile's targets or approach, or the world database's table).</param>
public readonly record struct CreatureIdentity(uint Wcid, string Name, int Level, string Where);

/// <summary>A creature kind's vitals (<see cref="CreatureCapabilities"/>).</summary>
/// <param name="MaxHealth">Its maximum health: a profile's <c>max_health</c> first, then the world database's table (<see cref="CreatureFacts.MaxHealthOf"/>); null when neither holds it.</param>
/// <param name="MaxHealthSource">Where <paramref name="MaxHealth"/> comes from, in words; the named TODO when it is null.</param>
public readonly record struct CreatureVitals(int? MaxHealth, string MaxHealthSource)
{
    /// <summary>Its maximum stamina as ACE computes it with no enchantment: the behaviour table's start and ranks plus its Endurance (<see cref="CreatureBehaviourRow.MaxStaminaOf"/>); null for a kind the tables do not hold.</summary>
    public int? MaxStamina { get; init; }

    /// <summary>Its maximum mana as ACE computes it with no enchantment: the behaviour table's start and ranks plus its Self (<see cref="CreatureBehaviourRow.MaxManaOf"/>); null for a kind the tables do not hold.</summary>
    public int? MaxMana { get; init; }

    /// <summary>
    /// Its health regeneration rate, health a heartbeat (<see cref="CreatureBehaviourRow.HealthRate"/>: ACE's 0, no regeneration, where the weenie states none); null for a kind the behaviour table does not hold. The
    /// heartbeat's seconds are <see cref="HeartbeatSeconds"/>, and the rate a second <see cref="HealthPerSecond"/>.
    /// </summary>
    public double? HealthRate { get; init; }

    /// <summary>Its stamina regeneration rate (<see cref="CreatureBehaviourRow.StaminaRate"/>); null for a kind the behaviour table does not hold.</summary>
    public double? StaminaRate { get; init; }

    /// <summary>Its mana regeneration rate (<see cref="CreatureBehaviourRow.ManaRate"/>); null for a kind the behaviour table does not hold.</summary>
    public double? ManaRate { get; init; }

    /// <summary>
    /// M6 16 E1e: the seconds between two of its heartbeats (<see cref="CreatureBehaviourRow.HeartbeatSeconds"/>: its HeartbeatInterval, ACE's 5 where the weenie states none); null for a kind the behaviour table does
    /// not hold.
    /// </summary>
    public double? HeartbeatSeconds { get; init; }

    /// <summary>
    /// M6 16 E1e: its health regeneration in points a second with nothing else applied (<see cref="CreatureBehaviourRow.HealthPerSecond"/>: the rate over the heartbeat's seconds, 0 for a creature with no
    /// heartbeat); null for a kind the behaviour table does not hold. No caller of the automation reads it yet.
    /// </summary>
    public double? HealthPerSecond { get; init; }

    /// <summary>M6 16 E1e: its stamina regeneration in points a second (<see cref="CreatureBehaviourRow.StaminaPerSecond"/>); null for a kind the behaviour table does not hold.</summary>
    public double? StaminaPerSecond { get; init; }

    /// <summary>M6 16 E1e: its mana regeneration in points a second (<see cref="CreatureBehaviourRow.ManaPerSecond"/>); null for a kind the behaviour table does not hold.</summary>
    public double? ManaPerSecond { get; init; }

    /// <summary>Whether a cast spends its mana, so that it stops casting when it has none (<see cref="CreatureBehaviourRow.UsesMana"/>: ACE's true where the weenie states nothing); null for a kind the behaviour table does not hold.</summary>
    public bool? UsesMana { get; init; }
}

/// <summary>What a creature kind resists (<see cref="CreatureCapabilities"/>).</summary>
/// <param name="MagicDefence">Its Magic Defense as ACE computes it (<see cref="CreatureRow.MagicDefence"/>, the world database's table), or null for a kind the table does not hold. A ground profile's own value is not read here.</param>
/// <param name="Resists">
/// Its resistance to the seven war elements in the order of <see cref="CreatureResists.Elements"/> (slash, pierce, bludgeon, fire, cold, acid, electric), or null when the world database records none: ACE then takes every element whole
/// (1). One element with that default: <see cref="Bestiary.ResistOf"/>.
/// </param>
/// <param name="HealthDrainResist">
/// The share of a health drain's take it lets through as recorded: a ground profile's value first, then the world database's, else ACE's 1 (<see cref="DrainResist.RecordedOf"/>). The override measured live is not in it
/// (<see cref="DrainResist.Of"/>).
/// </param>
public readonly record struct CreatureDefence(int? MagicDefence, IReadOnlyList<double>? Resists, double HealthDrainResist)
{
    /// <summary>The share of a stamina drain's take it lets through (<see cref="CreatureBehaviourRow.ResistStaminaDrain"/>: ACE's 1 where the weenie states none); null for a kind the behaviour table does not hold.</summary>
    public double? StaminaDrainResist { get; init; }

    /// <summary>The share of a mana drain's take it lets through (<see cref="CreatureBehaviourRow.ResistManaDrain"/>: ACE's 1 where the weenie states none); null for a kind the behaviour table does not hold.</summary>
    public double? ManaDrainResist { get; init; }
}

/// <summary>What a creature kind attacks with (<see cref="CreatureCapabilities"/>): today's attack and spell book.</summary>
/// <param name="Hitter">
/// Its hardest physical attack as the fight line reads it: a ground profile's first, then the world database's table (<see cref="CreatureFact.Hitter"/>); a kind with no physical attack recorded has a hit of 0 and
/// <see cref="CreatureIdentity.Where"/> says so (<see cref="CreatureFacts.NoAttackTodo"/>).
/// </param>
/// <param name="DamageTypes">The DamageType bits of its attacks (0: not given), from the same source as <paramref name="Hitter"/>.</param>
/// <param name="Pack">The most of it one camp holds at once (1 for a kind only the table holds).</param>
/// <param name="Ranged">Its ranged attack as a ground profile's approach records it, or null.</param>
/// <param name="TableAttack">The world database's table's own hardest attack (<see cref="CreatureRow.Attack"/>), or null when the table records none or does not hold the kind. It differs from <paramref name="Hitter"/> where a profile records the kind.</param>
/// <param name="Spells">Its spell book in ACE's iteration order (the table's, <see cref="CreatureTimingFacts.Spells"/>); empty when it has none or the table does not hold the kind.</param>
/// <param name="Skills">Its skill rows as the world database records them (the skills ACE's two defence rolls read); empty when it records none. A skill's value needs the dats' formula: <see cref="Bestiary.SkillOf"/>.</param>
public readonly record struct CreatureOffence(
    LadderHitter Hitter,
    uint DamageTypes,
    int Pack,
    GroundRanged? Ranged,
    CreatureAttack? TableAttack,
    IReadOnlyList<CreatureSpell> Spells,
    IReadOnlyList<CreatureSkillRow> Skills);

/// <summary>
/// How a creature kind moves (<see cref="CreatureCapabilities"/>). The three positional values are a ground profile's approach pace only (<see cref="CreatureFact.RunSkill"/>), which is what the run-past rule reads
/// (<see cref="CreaturePace"/>, through <see cref="CreatureFacts"/>); a kind no profile paces has none of the three. Its speed is derived from the dats at run time. M6 16 E1d: every kind's Run skill from the world
/// database is beside them (<see cref="TableRunSkill"/>; its scale is <see cref="CreatureBody.Scale"/>, its motion table <see cref="CreatureTimingFacts.MotionTable"/>). <c>TODO(engine-bestiary-run)</c>: the pace reader still
/// reads the profile's three; it moves onto the table's in a later slice of E1.
/// </summary>
/// <param name="RunSkill">Its Run skill as a ground profile's pace records it, or null.</param>
/// <param name="MotionTable">Its motion table as the profile's pace records it, or null.</param>
/// <param name="Scale">Its ObjScale as the profile's pace records it, or null when no pace is recorded.</param>
public readonly record struct CreatureMovement(int? RunSkill, uint? MotionTable, double? Scale)
{
    /// <summary>
    /// Its Run skill as ACE computes it with no enchantment, from the world database: the behaviour table's Run row plus its Quickness when the skill is usable (<see cref="CreatureBehaviourRow.RunSkillOf"/>); null for a kind
    /// the tables do not hold. Beside <see cref="RunSkill"/>, never in its place.
    /// </summary>
    public int? TableRunSkill { get; init; }
}

/// <summary>How near a creature kind notices her (<see cref="CreatureCapabilities"/>).</summary>
/// <param name="AwarenessMetres">Its visual awareness range in metres as the walker reads it: its pinned value, else ACE's default (<see cref="Bestiary.AwarenessMetres"/>).</param>
/// <param name="Pinned">
/// True when <paramref name="AwarenessMetres"/> is one of the pinned ranges; false when it is ACE's default for a creature without the property, whatever the kind's own range is (<see cref="TableVisualMetres"/>).
/// <c>TODO(engine-bestiary-awareness)</c>: the awareness reader still reads the pins. M6 16 E1f: the walker's creature feed reads the kind's own range beside it (<see cref="Bestiary.OwnAwarenessMetres"/>) and walks by it only
/// under <c>--walk-awareness=own</c>; by default it reports what would differ and the pins decide.
/// </param>
public readonly record struct CreatureSenses(float AwarenessMetres, bool Pinned)
{
    /// <summary>
    /// M6 16 E1d: how near it sees a player by the world database, metres (<see cref="CreatureBehaviourRow.VisualAwarenessMetres"/>: its VisualAwarenessRange, ACE's 18 where the weenie states none), before the server's
    /// <c>mob_awareness_range</c> (1 on the bundled shard); null for a kind the behaviour table does not hold. Beside <see cref="AwarenessMetres"/>, never in its place.
    /// </summary>
    public double? TableVisualMetres { get; init; }

    /// <summary>M6 16 E1d: how near it hears a creature of its kind call, metres (<see cref="CreatureBehaviourRow.AuralAwarenessMetres"/>: its own range, else its visual range); null for a kind the behaviour table does not hold.</summary>
    public double? TableAuralMetres { get; init; }

    /// <summary>Its Tolerance bits: when it attacks (<see cref="CreatureBehaviourRow.Tolerance"/>: 0 attacks what comes in range, 1 never, 64 only after it is attacked); null for a kind the behaviour table does not hold.</summary>
    public int? Tolerance { get; init; }

    /// <summary>Its TargetingTactic bits: whom it picks among those in reach (<see cref="CreatureBehaviourRow.TargetingTactic"/>); null for a kind the behaviour table does not hold.</summary>
    public int? TargetingTactic { get; init; }

    /// <summary>The CreatureType it wakes to its fight beside its own kind (<see cref="CreatureBehaviourRow.FriendType"/>), or null: it has none, or the behaviour table does not hold the kind.</summary>
    public int? FriendType { get; init; }

    /// <summary>The CreatureType it is a foe of (<see cref="CreatureBehaviourRow.FoeType"/>), or null: it has none, or the behaviour table does not hold the kind.</summary>
    public int? FoeType { get; init; }
}

/// <summary>What a creature kind's kill gives (<see cref="CreatureCapabilities"/>).</summary>
/// <param name="Xp">
/// The experience its kill gives the one who dealt all of its damage, by the world database (<see cref="CreatureBehaviourRow.Xp"/>: its XpOverride, ACE's 0 where the weenie states none); null for a kind the behaviour table
/// does not hold. The ground profiles' own <c>xp</c> is not read here: the approach cost reads it (<see cref="GroundApproachHostile.Xp"/>).
/// </param>
public readonly record struct CreatureReward(long? Xp);

/// <summary>M6 16 E1d: a creature kind's kind and body by the world database (<see cref="CreatureCapabilities"/>, <see cref="CreatureBehaviour"/>); each null for a kind the behaviour table does not hold.</summary>
/// <param name="CreatureType">Its CreatureType (ACE's enum: 3 a Drudge, 8 a Tusker), or null when the weenie states none too.</param>
/// <param name="Setup">Its Setup, the dats' model of its body (0: the weenie states none). What the model holds (its height, its shapes) is the dats' and is read at run time.</param>
/// <param name="Scale">Its scale (ACE's ObjScale: 1 where the weenie states none). Beside a ground profile's own <see cref="CreatureMovement.Scale"/>, never in its place.</param>
public readonly record struct CreatureBody(int? CreatureType, uint? Setup, double? Scale);

/// <summary>
/// M6 16 Q12: the numbers of one creature kind that a ground profile takes by reference (its <c>from_bestiary</c>), as plain values read from the tables (<see cref="Bestiary.TableFactsOf"/>): the profile's loader holds no table row.
/// </summary>
/// <param name="MaxHealth">Its maximum health (<see cref="CreatureRow.MaxHealth"/>).</param>
/// <param name="MagicDefence">Its Magic Defense (<see cref="CreatureRow.MagicDefence"/>).</param>
/// <param name="ResistHealthDrain">The share of a health drain's take it lets through (<see cref="CreatureRow.ResistHealthDrain"/>).</param>
/// <param name="Xp">The experience its kill gives (<see cref="CreatureBehaviourRow.Xp"/>).</param>
/// <param name="AwarenessMetres">Its own visual awareness range, as the profile's <c>awareness_m</c> states it (<see cref="CreatureBehaviourRow.VisualAwarenessMetres"/>).</param>
/// <param name="RunSkill">Its Run skill as ACE computes it, or null for a kind with no motion table.</param>
/// <param name="MotionTable">Its motion table, or null.</param>
/// <param name="Scale">Its ObjScale (1 where the weenie states none).</param>
public readonly record struct TableFacts(int MaxHealth, int MagicDefence, double ResistHealthDrain, long Xp, double AwarenessMetres, int? RunSkill, uint? MotionTable, double Scale);
