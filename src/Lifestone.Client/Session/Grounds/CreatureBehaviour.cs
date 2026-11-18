using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// One creature weenie's row of <c>creature-behaviour.json</c> (<see cref="CreatureBehaviour"/>): what the world database states of its behaviour, a weenie without a property holding what ACE gives such a creature (the table's
/// <c>sources</c> name ACE's file and line for each). The three values ACE computes from several rows are computed here from the row's terms and the creature's attributes, which <c>creatures.json</c> holds
/// (<see cref="MaxStaminaOf"/>, <see cref="MaxManaOf"/>, <see cref="RunSkillOf"/>).
/// </summary>
/// <param name="Wcid">The weenie class id.</param>
public sealed record CreatureBehaviourRow(uint Wcid)
{
    /// <summary>ACE's SkillAdvancementClass: Untrained 1, Trained 2 (Inactive 0, Specialized 3; ACE.Entity/Enum/SkillAdvancementClass.cs:6-9 @95ef426d).</summary>
    internal const int Untrained = 1, Trained = 2;

    /// <summary>The dats' SkillTable <c>_min_level</c> of Run (skill 24): 1, usable untrained (<c>CreatureBehaviourTests</c> reads it from the dats).</summary>
    public const int RunMinLevel = 1;

    /// <summary>
    /// The dats' vital formulas of stamina and mana (the VitalTable, ACE's SecondaryAttributeTable, AttributeFormula.cs:34-49 @95ef426d): Endurance over 1 and Self over 1, the same as a player's
    /// (<see cref="Contracts.PlayerQualities.TryGetVitalLevels"/>, checked against a live server); <c>CreatureBehaviourTests</c> reads both from the dats.
    /// </summary>
    internal static readonly SkillTableFormula MaxStaminaFormula = new(0, 1, 0, 1, GroundDraft.EnduranceAttribute, 0), MaxManaFormula = new(0, 1, 0, 1, GroundDraft.SelfAttribute, 0);

    /// <summary>Its CreatureType (PropertyInt 2, ACE's enum of the same name: 3 a Drudge, 8 a Tusker), or null: ACE holds none for it.</summary>
    public int? CreatureType { get; init; }

    /// <summary>Its Setup (PropertyDataId 1), the dats' model of its body; 0 when the weenie has none.</summary>
    public uint Setup { get; init; }

    /// <summary>Its DefaultScale (PropertyFloat 39), ACE's ObjScale; 1 when the weenie has none (<c>Monster_Navigation.GetMovementSpeed</c>: <c>ObjScale ?? 1.0f</c>).</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// How near it sees a player, metres: its VisualAwarenessRange (PropertyFloat 31), or ACE's 18 for a weenie without it (<c>VisualAwarenessRange_Default</c>, Monster_Awareness.cs:377 @95ef426d). Before the server's
    /// <c>mob_awareness_range</c>, which ACE multiplies it by (1 on the bundled shard).
    /// </summary>
    public double VisualAwarenessMetres { get; init; } = 18.0;

    /// <summary>Its own AuralAwarenessRange (PropertyFloat 32), metres, or null when the weenie has none (<see cref="AuralAwarenessMetres"/> is then its visual range).</summary>
    public double? StatedAuralAwarenessMetres { get; init; }

    /// <summary>
    /// How near it hears a creature of its kind call (ACE's <c>AlertFriendly</c>), metres: its own range, else its visual range (<c>AuralAwarenessRange ?? VisualAwarenessRange ?? VisualAwarenessRange_Default</c>,
    /// Monster_Awareness.cs:421 @95ef426d). Before the server's <c>mob_awareness_range</c>.
    /// </summary>
    public double AuralAwarenessMetres => StatedAuralAwarenessMetres ?? VisualAwarenessMetres;

    /// <summary>Its Tolerance bits (PropertyInt 67, ACE's enum of the same name: 0 attacks what comes in range, 1 never attacks, 64 only after it is attacked); 0 when the weenie has none (Monster_Awareness.cs:66).</summary>
    public int Tolerance { get; init; }

    /// <summary>Its TargetingTactic bits (PropertyInt 68); 0 when the weenie has none, where ACE picks between a random target and the top damager (Monster_Awareness.cs:75, 99-101).</summary>
    public int TargetingTactic { get; init; }

    /// <summary>Its FriendType (PropertyInt 72): the CreatureType it wakes to its fight beside its own kind (ACE's <c>AlertFriendly</c>, Monster_Awareness.cs:463-464), or null.</summary>
    public int? FriendType { get; init; }

    /// <summary>Its FoeType (PropertyInt 73): the CreatureType it and whose creatures are foes of each other (ACE's <c>PotentialFoe</c>, Creature_Combat.cs:1344-1347), or null.</summary>
    public int? FoeType { get; init; }

    /// <summary>Its MaxStamina row's init_Level (PropertyAttribute2nd 3); 0 when the weenie has no row (ACE makes an empty one, CreatureVital.cs:29-33).</summary>
    public int StaminaStart { get; init; }

    /// <summary>Its MaxStamina row's level_From_C_P.</summary>
    public int StaminaRanks { get; init; }

    /// <summary>Its MaxMana row's init_Level (PropertyAttribute2nd 5); 0 when the weenie has no row.</summary>
    public int ManaStart { get; init; }

    /// <summary>Its MaxMana row's level_From_C_P.</summary>
    public int ManaRanks { get; init; }

    /// <summary>
    /// Its HealthRate (PropertyFloat 3): the health one heartbeat's vital tick gives it back, for a creature with nothing else applied (the attribute and stance factors are a player's alone, Creature_Vitals.cs:174-213; an
    /// enchantment multiplies it; the fraction is carried to the next tick, 134-141). A negative rate takes health away. 0 when the weenie has none, and a rate of 0 never ticks (CreatureVital.cs:38, Creature_Vitals.cs:117).
    /// The heartbeat's seconds are <see cref="HeartbeatSeconds"/>; the rate a second is <see cref="HealthPerSecond"/>.
    /// </summary>
    public double HealthRate { get; init; }

    /// <summary>Its StaminaRate (PropertyFloat 4), as <see cref="HealthRate"/>; 0 when the weenie has none (CreatureVital.cs:41).</summary>
    public double StaminaRate { get; init; }

    /// <summary>Its ManaRate (PropertyFloat 5), as <see cref="HealthRate"/>; 0 when the weenie has none (CreatureVital.cs:44).</summary>
    public double ManaRate { get; init; }

    /// <summary>
    /// M6 16 E1e: the seconds between two of its heartbeats: its HeartbeatInterval (PropertyFloat 1), or ACE's 5 for a weenie without it (<c>WorldObject.InitializeHeartbeats</c>, WorldObject_Tick.cs:47-48 @95ef426d).
    /// A creature's heartbeat runs its vital tick (<c>Creature.Heartbeat</c>, Creature_Tick.cs:14-49, calling <c>VitalHeartBeat</c>, Creature_Vitals.cs:81-95) and sets the next one this many seconds on
    /// (WorldObject_Tick.cs:98-108). With an interval of 0 or less ACE disables the heartbeat (56-68): its vitals never tick. Of the table's 7,848 kinds 7,709 beat every 5 s and 139 state another interval, from
    /// 1 s to 480 s (38 at 2 s, 36 at 10 s, 21 at 3 s; the table, 2026-11-17); one states 0 (weenie 32966, the Stone of Yanshi) and has no heartbeat. The server's own objects say the same
    /// (<c>tools/fight-sim.ps1 creature</c>: weenie 5595 "every 2 s", 24495 "every 10 s", 72277 "every 480 s", 32966 no heartbeat). ACE sets a game piece's to 1 whatever its weenie states (44-45), and the
    /// tables hold no game piece.
    /// </summary>
    public double HeartbeatSeconds { get; init; } = 5.0;

    /// <summary>M6 16 E1e: its health regeneration in points a second, with nothing else applied: <see cref="HealthRate"/> over <see cref="HeartbeatSeconds"/> (<see cref="PerSecond"/>). No caller of the automation reads it yet.</summary>
    public double HealthPerSecond => PerSecond(HealthRate);

    /// <summary>M6 16 E1e: its stamina regeneration in points a second, as <see cref="HealthPerSecond"/>.</summary>
    public double StaminaPerSecond => PerSecond(StaminaRate);

    /// <summary>M6 16 E1e: its mana regeneration in points a second, as <see cref="HealthPerSecond"/>.</summary>
    public double ManaPerSecond => PerSecond(ManaRate);

    /// <summary>
    /// A rate a heartbeat as a rate a second: over <see cref="HeartbeatSeconds"/>; 0 for a creature whose heartbeat ACE disables (an interval of 0 or less), whatever its rate. The mean over many heartbeats: ACE gives
    /// whole points, carrying the fraction to the next tick (Creature_Vitals.cs:134-141), so a rate of 0.1 a 5 s heartbeat is one point every 50 s.
    /// </summary>
    /// <param name="rate">The vital points one heartbeat gives.</param>
    public double PerSecond(double rate) => HeartbeatSeconds > 0 ? rate / HeartbeatSeconds : 0.0;

    /// <summary>Its AiUsesMana (PropertyBool 6): a cast spends its mana, and it does not cast without it (Monster_Magic.cs:150); true when the weenie has none (Monster_Magic.cs:23).</summary>
    public bool UsesMana { get; init; } = true;

    /// <summary>Its ResistStaminaDrain (PropertyFloat 72): the share of a stamina drain's take it lets through; 1 when the weenie has none (Creature_Properties.cs:212).</summary>
    public double ResistStaminaDrain { get; init; } = 1.0;

    /// <summary>Its ResistManaDrain (PropertyFloat 74): the share of a mana drain's take it lets through; 1 when the weenie has none (Creature_Properties.cs:216).</summary>
    public double ResistManaDrain { get; init; } = 1.0;

    /// <summary>Its Run row's init_Level (Skill 24); 0 when the weenie has no row (ACE adds an empty, untrained one, Creature_Skills.cs:27-35).</summary>
    public int RunStart { get; init; }

    /// <summary>Its Run row's level_From_P_P.</summary>
    public int RunRanks { get; init; }

    /// <summary>Its Run row's s_a_c (ACE's SkillAdvancementClass: Inactive 0, Untrained 1, Trained 2, Specialized 3); 1 when the weenie has no row.</summary>
    public int RunSac { get; init; } = Untrained;

    /// <summary>The experience its kill gives the one who dealt all of its damage: its XpOverride (PropertyInt 146); 0 when the weenie has none (Creature_Death.cs:193: <c>(XpOverride ?? 0) * damagePercent</c>).</summary>
    public long Xp { get; init; }

    /// <summary>
    /// An attribute as ACE's formulas read it of a creature with no enchantment (<c>CreatureAttribute.Current</c>, CreatureAttribute.cs:149-160 @95ef426d): its stated value, and never under 1 (an attribute the weenie
    /// does not state is 1, not 0: the server builds weenie 32400 with stamina 1, mana 1 and Run 1 from no attribute, vital or skill row; <c>fight-sim creature 32400</c>, 2026-11-17).
    /// </summary>
    /// <param name="attributes">The creature's attributes, an absent one 0.</param>
    /// <param name="id">ACE's PropertyAttribute (1 Strength to 6 Self).</param>
    internal static uint AttributeOf(CreatureAttributes attributes, uint id) => Math.Max(1u, attributes.Of(id));

    /// <summary>
    /// ACE's maximum stamina of the creature with no enchantment (<c>CreatureVital.MaxValue</c>, CreatureVital.cs:126-174 @95ef426d): its row's start and ranks plus Endurance (<see cref="MaxStaminaFormula"/>,
    /// <see cref="AttributeOf"/>), and never under 1 (168-171).
    /// </summary>
    /// <param name="attributes">Its attributes (<c>creatures.json</c>'s, an absent one 0).</param>
    public int MaxStaminaOf(CreatureAttributes attributes)
    {
        MaxStaminaFormula.TryCalculate(AttributeOf(attributes, MaxStaminaFormula.Attribute1), 0, out var part);
        return Math.Max(1, checked(StaminaStart + StaminaRanks + (int)part));
    }

    /// <summary>ACE's maximum mana of the creature with no enchantment, as <see cref="MaxStaminaOf"/>: its row's start and ranks plus Self (<see cref="MaxManaFormula"/>), and never under 1.</summary>
    /// <param name="attributes">Its attributes.</param>
    public int MaxManaOf(CreatureAttributes attributes)
    {
        MaxManaFormula.TryCalculate(AttributeOf(attributes, MaxManaFormula.Attribute1), 0, out var part);
        return Math.Max(1, checked(ManaStart + ManaRanks + (int)part));
    }

    /// <summary>
    /// ACE's Run skill of the creature with no enchantment (<c>CreatureSkill.Current</c>, CreatureSkill.cs:154-163 @95ef426d; what <c>Monster_Navigation.GetRunRate</c> reads, 346-368): its row's start and ranks, plus Quickness
    /// (<see cref="Vitals.CreaturePace.RunFormula"/>, <see cref="AttributeOf"/>) when the skill is usable: trained, specialized, or untrained, the dats' Run being usable untrained (<see cref="RunMinLevel"/>; <c>IsUsable</c>, 48-63). An inactive
    /// Run (s_a_c 0) is its start and ranks alone.
    /// </summary>
    /// <param name="attributes">Its attributes.</param>
    public int RunSkillOf(CreatureAttributes attributes)
    {
        var usable = RunSac >= Trained || (RunSac == Untrained && RunMinLevel == 1);
        var formula = Vitals.CreaturePace.RunFormula;
        var part = usable && formula.TryCalculate(AttributeOf(attributes, formula.Attribute1), 0, out var value) ? value : 0u;
        return checked(RunStart + RunRanks + (int)part);
    }
}

/// <summary>
/// M6 16 E1d (docs/automation-engine.md section 2.1; m6-plan.md D139): the bestiary's fourth table, every creature weenie's behaviour facts the first three do not hold (<c>creature-behaviour.json</c>, embedded; written by
/// <c>tools/fight-sim bestiary</c> from the world database, its <c>sources</c> naming each column's place in ACE's source): what kind of creature it is, its body's setup and scale, how near it sees and hears, whom it attacks
/// and whose call it answers, its stamina and mana, how fast its vitals come back (the rates a heartbeat and, M6 16 E1e, the heartbeat's seconds), what a stamina or a mana drain takes of it, its Run skill and the experience of its kill. Read by the bestiary's facade
/// (<see cref="Creatures.Bestiary"/>) alone. M6 16 E1f: one of its values has a reader of the automation, the visual range, which the walker's creature feed reads through the facade (<see cref="Creatures.Bestiary.OwnAwarenessMetres"/>)
/// to report what it would change and, under <c>--walk-awareness=own</c> only, to walk by; by default nothing the table holds changes what the walker, the run-past rule or the fight do.
/// </summary>
public static class CreatureBehaviour
{
    /// <summary>The embedded resource's logical name.</summary>
    public const string ResourceName = "Lifestone.Client.Session.Grounds.creature-behaviour.json";

    /// <summary>The table's columns, in the file's order (<c>tools/fight-sim/BestiaryTables.cs</c>, <c>Behaviour</c>).</summary>
    internal static IReadOnlyList<string> Columns { get; } =
    [
        "wcid", "creature_type", "setup", "scale", "visual_awareness", "aural_awareness", "tolerance", "targeting_tactic", "friend_type", "foe_type", "stamina_start", "stamina_ranks", "mana_start", "mana_ranks",
        "health_rate", "stamina_rate", "mana_rate", "heartbeat_interval", "uses_mana", "resist_stamina_drain", "resist_mana_drain", "run_start", "run_ranks", "run_sac", "xp",
    ];

    private static readonly Lazy<FrozenDictionary<uint, CreatureBehaviourRow>> Rows = new(Load);

    /// <summary>How many creature weenies the table holds.</summary>
    public static int Count => Rows.Value.Count;

    /// <summary>The row of <paramref name="wcid"/>, or null when it is no creature weenie of the world database. Allocates nothing after the first call.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static CreatureBehaviourRow? Find(uint wcid) => Rows.Value.TryGetValue(wcid, out var row) ? row : null;

    /// <summary>Every row.</summary>
    public static IEnumerable<CreatureBehaviourRow> All => Rows.Value.Values;

    /// <summary>Parses the table's JSON (<c>columns</c> and <c>behaviour</c> rows).</summary>
    /// <param name="json">The document.</param>
    internal static FrozenDictionary<uint, CreatureBehaviourRow> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        if (!columns.SequenceEqual(Columns))
        {
            throw new InvalidDataException($"creature-behaviour.json: the columns are [{string.Join(", ", columns)}], not [{string.Join(", ", Columns)}]");
        }

        var rows = new Dictionary<uint, CreatureBehaviourRow>();
        foreach (var r in doc.RootElement.GetProperty("behaviour").EnumerateArray())
        {
            if (r.GetArrayLength() != Columns.Count)
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"creature-behaviour.json: weenie {r[0]}'s row has {r.GetArrayLength()} values, not {Columns.Count}"));
            }

            var wcid = r[0].GetUInt32();
            var row = new CreatureBehaviourRow(wcid)
            {
                CreatureType = r[1].GetInt32() is var kind and >= 0 ? kind : null,
                Setup = (uint)Math.Max(0, r[2].GetInt64()),
                Scale = r[3].GetDouble(),
                VisualAwarenessMetres = r[4].GetDouble(),
                StatedAuralAwarenessMetres = r[5].GetDouble() is var aural and >= 0 ? aural : null,
                Tolerance = r[6].GetInt32(),
                TargetingTactic = r[7].GetInt32(),
                FriendType = r[8].GetInt32() is var friend and >= 0 ? friend : null,
                FoeType = r[9].GetInt32() is var foe and >= 0 ? foe : null,
                StaminaStart = r[10].GetInt32(),
                StaminaRanks = r[11].GetInt32(),
                ManaStart = r[12].GetInt32(),
                ManaRanks = r[13].GetInt32(),
                HealthRate = r[14].GetDouble(),
                StaminaRate = r[15].GetDouble(),
                ManaRate = r[16].GetDouble(),
                HeartbeatSeconds = r[17].GetDouble(),
                UsesMana = r[18].GetInt32() != 0,
                ResistStaminaDrain = r[19].GetDouble(),
                ResistManaDrain = r[20].GetDouble(),
                RunStart = r[21].GetInt32(),
                RunRanks = r[22].GetInt32(),
                RunSac = r[23].GetInt32(),
                Xp = r[24].GetInt64(),
            };
            if (row.StaminaStart < 0 || row.StaminaRanks < 0 || row.ManaStart < 0 || row.ManaRanks < 0 || row.RunStart < 0 || row.RunRanks < 0 || row.RunSac is < 0 or > 3 || row.Xp < 0
                || !(row.Scale > 0) || row.VisualAwarenessMetres < 0 || row.ResistStaminaDrain < 0 || row.ResistManaDrain < 0 || double.IsNaN(row.HeartbeatSeconds))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"creature-behaviour.json: weenie {wcid}'s row holds a value outside what the world database can state: {r.GetRawText()}"));
            }

            if (!rows.TryAdd(wcid, row))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"creature-behaviour.json: weenie {wcid} has two rows"));
            }
        }

        return rows.ToFrozenDictionary();
    }

    private static FrozenDictionary<uint, CreatureBehaviourRow> Load()
    {
        using var stream = typeof(CreatureBehaviour).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded {ResourceName} is missing from the client assembly");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
