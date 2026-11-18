using System.Collections.Frozen;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Creatures;

/// <summary>
/// M6 16 E1a, the bestiary's facade (docs/automation-engine.md section 2.1, m6-plan.md D139): the one reader of what a creature kind can do, by weenie class. It reads the sources and adds nothing to them: the ground
/// profiles' facts first and then the world database's creature table, in <see cref="CreatureFacts"/>' own order; the table's rows (<see cref="CreatureTable"/>); the seven resistances (<see cref="CreatureResists"/>); the skill
/// rows (<see cref="CreatureSkills"/>); the pinned awareness ranges (<see cref="CreatureAwareness"/>); and, M6 16 E1d, the behaviour table (<see cref="CreatureBehaviour"/>), whose values fill the fields no other source
/// held and sit beside the ones the automation reads today, changing none of them. A caller asks here, never a source.
/// </summary>
/// <remarks>
/// <para><b>The record.</b> <see cref="Find"/> gives a kind's <see cref="CreatureCapabilities"/>: every weenie a ground profile records facts for and every creature weenie of the world database's table, built once on first use; a
/// lookup after that is one dictionary read and allocates nothing. M6 16 E1e: a creature weenie is one of a weenie type ACE builds as a creature a player can fight: WeenieType.Creature and WeenieType.Cow (the cattle);
/// the vendors, the pets and the game pieces, which ACE also builds as Creature subclasses, are not kinds of the bestiary (the generator's list and its reasons: <c>tools/fight-sim/BestiaryRows.cs</c>, <c>AceIds.LeftOut</c>).</para>
/// <para><b>The reads with ACE's default.</b> A kind that records no resistance takes every element whole, one that pins no awareness notices at ACE's default, and both hold for a weenie outside the record too. Those reads
/// (<see cref="ResistOf"/>, <see cref="ResistsText"/>, <see cref="AwarenessMetres"/>) and the two skill reads that join the dats' SkillTable (<see cref="AttackSkillId"/>, <see cref="SkillOf"/>) go straight to their one source:
/// they load that table only, so the walker's creature feed reads the pinned awareness without loading the ground profiles. M6 16 E1f: the kind's own awareness range (<see cref="OwnAwarenessMetres"/>) is the record's, so the feed's
/// first read of it loads the bestiary, as every other reader of the record does.</para>
/// <para><b>The name.</b> The files live in <c>Session/Bestiary</c>; the namespace is <c>Creatures</c> because a class cannot be named as its own namespace's last part and still be called by its bare name from the session's
/// other namespaces.</para>
/// </remarks>
public static class Bestiary
{
    private static readonly Lazy<FrozenDictionary<uint, CreatureCapabilities>> Kinds = new(Load);

    /// <summary>How many creature kinds the bestiary holds.</summary>
    public static int Count => Kinds.Value.Count;

    /// <summary>Every kind.</summary>
    public static IEnumerable<CreatureCapabilities> All => Kinds.Value.Values;

    /// <summary>
    /// What weenie class <paramref name="wcid"/> can do: a ground profile's facts first, then the world database's creature table, as <see cref="CreatureFacts.Find"/> orders them; null only for a weenie neither holds. Allocates nothing
    /// after the first call.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static CreatureCapabilities? Find(uint wcid) => Kinds.Value.TryGetValue(wcid, out var kind) ? kind : null;

    /// <summary>
    /// M6 16 E2a (the threat board): the bestiary's name read. The server's damage and evasion notices and its spell lines name an attacker by its name alone, so a reader that holds only a name asks here for its weenie class:
    /// the one kind that bears <paramref name="name"/>, or null when no kind does or several do (several weenies can share a name; a name is then no identity, and the caller says so). Built once on first use from the
    /// record's own identities; a lookup after that allocates nothing.
    /// </summary>
    /// <param name="name">The creature's name, exactly as the server writes it.</param>
    /// <param name="kinds">How many kinds bear the name.</param>
    public static uint? WcidNamed(string name, out int kinds)
    {
        kinds = 0;
        if (name is null || !Names.Value.TryGetValue(name, out var found))
        {
            return null;
        }

        kinds = found.Kinds;
        return found.Kinds == 1 ? found.Wcid : null;
    }

    private static readonly Lazy<FrozenDictionary<string, (uint Wcid, int Kinds)>> Names = new(static () =>
    {
        var names = new Dictionary<string, (uint Wcid, int Kinds)>(StringComparer.Ordinal);
        foreach (var kind in Kinds.Value.Values)
        {
            var name = kind.Identity.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            names[name] = names.TryGetValue(name, out var held) ? (Math.Min(held.Wcid, kind.Wcid), held.Kinds + 1) : (kind.Wcid, 1);
        }

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    });

    /// <summary>How near weenie class <paramref name="wcid"/> notices a player, in metres: its pinned value, else ACE's default, times the server's scale. Allocates nothing.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static float AwarenessMetres(uint wcid) => AwarenessOf(wcid, own: false);

    /// <summary>
    /// M6 16 E1f: how near weenie class <paramref name="wcid"/> notices a player by its own range, in metres: the world database's VisualAwarenessRange of the kind (<see cref="CreatureSenses.TableVisualMetres"/>, ACE's 18 where the
    /// weenie states none) times the server's scale; for a weenie class the tables do not hold (a weenie that is no creature; the cattle are rows from M6 16 E1e on) <see cref="AwarenessMetres"/>: its pin, else
    /// ACE's default. The walker's creature feed reads it beside <see cref="AwarenessMetres"/> (<c>--walk-awareness</c>, <see cref="WalkAwareness"/>); every other caller keeps <see cref="AwarenessMetres"/>. It reads the record
    /// (<see cref="Find"/>), so the first call loads the bestiary; after that it allocates nothing.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static float OwnAwarenessMetres(uint wcid) => AwarenessOf(wcid, own: true);

    /// <summary>The facade's one read of the awareness source for both ranges: the pinned range, or with <paramref name="own"/> the record's own range through the same scale.</summary>
    private static float AwarenessOf(uint wcid, bool own) => CreatureAwareness.OwnMetresOf(wcid, own ? Find(wcid)?.Awareness.TableVisualMetres : null);

    /// <summary>The resistance of weenie class <paramref name="wcid"/> to war element <paramref name="element"/> (fire, cold, acid, electric, pierce, bludgeon, slash): the world database's, or ACE's 1 when it records none.</summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="element">The element.</param>
    public static double ResistOf(uint wcid, string element) => CreatureResists.Of(wcid, element);

    /// <summary>The seven resistances of weenie class <paramref name="wcid"/> as one text, for a log line.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static string ResistsText(uint wcid) => CreatureResists.Text(wcid);

    /// <summary>The skill of a creature's physical attack as ACE picks it: its weapon's, else Unarmed Combat, converted to the later skills when the creature's own start of it is 0.</summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="weaponWcid">Its weapon's weenie class (its launcher's for a ranged attack), 0 when none.</param>
    /// <param name="ranged">The weapon is ranged (a launcher or a thrown weapon).</param>
    public static uint AttackSkillId(uint wcid, uint weaponWcid, bool ranged) => CreatureSkills.AttackSkillId(wcid, weaponWcid, ranged);

    /// <summary>
    /// The value of <paramref name="skill"/> of weenie class <paramref name="wcid"/> as ACE computes it: its rows plus the dats' SkillTable formula (<paramref name="entry"/>) over <paramref name="attributes"/> when the skill is
    /// usable; null when the SkillTable entry is not at hand. A run-time join: the formula is the dats'.
    /// </summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="skill">The skill id.</param>
    /// <param name="attributes">Its attributes.</param>
    /// <param name="entry">The dats' SkillTable entry of the skill.</param>
    public static uint? SkillOf(uint wcid, uint skill, CreatureAttributes attributes, SkillTableEntry? entry) => CreatureSkills.Current(wcid, skill, attributes, entry);

    /// <summary>
    /// M6 16 E1d: the chance a skill beats a difficulty, as ACE rolls a creature's spell against her Magic Defense and its attack against her Melee or Missile Defense (<c>SkillCheck.GetSkillChance</c> at its regular 0.03,
    /// SkillCheck.cs:7-12 @95ef426d). The attack times asked the skill table for it; they ask here, for the same number.
    /// </summary>
    /// <param name="skill">The attacker's skill.</param>
    /// <param name="difficulty">The defender's skill.</param>
    public static double SkillChance(double skill, double difficulty) => CreatureSkills.Chance(skill, difficulty);

    /// <summary>
    /// One kind's record from its sources' answers: the regrouping and nothing else. <paramref name="fact"/> carries the identity, the hitter, the damage types, the pack, the throw and the pace in <see cref="CreatureFacts"/>'
    /// order; the rest is the table's row and the other sources'.
    /// </summary>
    /// <param name="fact">The kind's facts, a profile's first and then the table's (<see cref="CreatureFacts.Find"/>).</param>
    /// <param name="maxHealth">Its maximum health in the same order (<see cref="CreatureFacts.MaxHealthOf"/>), or null.</param>
    /// <param name="maxHealthSource">Where that comes from, in words.</param>
    /// <param name="row">The world database's table's row, or null when the table does not hold the kind.</param>
    /// <param name="resists">Its seven resistances, or null when none is recorded.</param>
    /// <param name="skills">Its skill rows.</param>
    /// <param name="healthDrainResist">Its recorded health-drain resistance (<see cref="DrainResist.RecordedOf"/>).</param>
    /// <param name="awarenessMetres">Its awareness range.</param>
    /// <param name="awarenessPinned">True when that range is the kind's own.</param>
    /// <param name="behaviour">
    /// M6 16 E1d: the behaviour table's row, or null when that table does not hold the kind. Its values fill the fields no other source holds; the stamina, the mana and the Run skill are computed with the attributes of
    /// <paramref name="row"/> and are null without it. None of the values the other parameters give is replaced.
    /// </param>
    internal static CreatureCapabilities Of(
        CreatureFact fact,
        int? maxHealth,
        string maxHealthSource,
        CreatureRow? row,
        IReadOnlyList<double>? resists,
        IReadOnlyList<CreatureSkillRow> skills,
        double healthDrainResist,
        float awarenessMetres,
        bool awarenessPinned,
        CreatureBehaviourRow? behaviour = null)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(maxHealthSource);
        ArgumentNullException.ThrowIfNull(skills);
        // the terms of ACE's stamina, mana and Run formulas: the table's attributes, an absent one 0 there (the behaviour row reads an attribute as ACE's formulas do, never under 1)
        CreatureAttributes? attributes = behaviour is null ? null : row?.Timing?.Attributes;
        return new CreatureCapabilities(
            new CreatureIdentity(fact.Wcid, fact.Name, fact.Level, fact.Where),
            new CreatureVitals(maxHealth, maxHealthSource)
            {
                MaxStamina = attributes is { } stamina ? behaviour!.MaxStaminaOf(stamina) : null,
                MaxMana = attributes is { } mana ? behaviour!.MaxManaOf(mana) : null,
                HealthRate = behaviour?.HealthRate,
                StaminaRate = behaviour?.StaminaRate,
                ManaRate = behaviour?.ManaRate,
                HeartbeatSeconds = behaviour?.HeartbeatSeconds,
                HealthPerSecond = behaviour?.HealthPerSecond,
                StaminaPerSecond = behaviour?.StaminaPerSecond,
                ManaPerSecond = behaviour?.ManaPerSecond,
                UsesMana = behaviour?.UsesMana,
            },
            new CreatureDefence(row?.MagicDefence, resists, healthDrainResist) { StaminaDrainResist = behaviour?.ResistStaminaDrain, ManaDrainResist = behaviour?.ResistManaDrain },
            new CreatureOffence(fact.Hitter, fact.DamageTypes, fact.Pack, fact.Ranged, row?.Attack, row?.Timing?.Spells ?? [], skills),
            row?.Timing,
            new CreatureMovement(fact.RunSkill, fact.MotionTable, fact.Scale) { TableRunSkill = attributes is { } run ? behaviour!.RunSkillOf(run) : null },
            new CreatureSenses(awarenessMetres, awarenessPinned)
            {
                TableVisualMetres = behaviour?.VisualAwarenessMetres,
                TableAuralMetres = behaviour?.AuralAwarenessMetres,
                Tolerance = behaviour?.Tolerance,
                TargetingTactic = behaviour?.TargetingTactic,
                FriendType = behaviour?.FriendType,
                FoeType = behaviour?.FoeType,
            },
            new CreatureReward(behaviour?.Xp),
            new CreatureBody(behaviour?.CreatureType, behaviour?.Setup, behaviour?.Scale));
    }

    private static readonly Lazy<FrozenDictionary<uint, CreatureRow>> TableRows = new(static () => CreatureTable.All.ToFrozenDictionary(row => row.Wcid));

    private static CreatureBehaviourRow? BehaviourOf(uint wcid) => CreatureBehaviour.Find(wcid);

    /// <summary>
    /// M6 16 Q12: the numbers of weenie class <paramref name="wcid"/> for the ground profiles' loader, which names a creature's health, defence, experience, drain resistance, awareness and pace by reference
    /// (a profile's <c>from_bestiary</c>) instead of stating them: the creature table's row (health, Magic Defense, drain resistance, motion table, attributes) and the behaviour table's (experience, awareness range, scale, Run
    /// start) as plain numbers, so the loader holds no row. Null when either table does not hold the kind. The loader runs while <see cref="CreatureFacts"/> is still reading the profiles, so this read touches the two tables only, never the record (<see cref="Find"/>).
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    internal static TableFacts? TableFactsOf(uint wcid) =>
        TableRows.Value.TryGetValue(wcid, out var row) && BehaviourOf(wcid) is { } behaviour
            ? new TableFacts(row.MaxHealth, row.MagicDefence, row.ResistHealthDrain, behaviour.Xp, behaviour.VisualAwarenessMetres, row.Timing is { } timing ? behaviour.RunSkillOf(timing.Attributes) : null, row.Timing?.MotionTable, behaviour.Scale)
            : null;

    private static FrozenDictionary<uint, CreatureCapabilities> Load()
    {
        var facts = CreatureFacts.All;
        var kinds = new Dictionary<uint, CreatureCapabilities>();
        foreach (var row in TableRows.Value.Values)
        {
            kinds[row.Wcid] = Compose(facts, row.Wcid, row);
        }

        foreach (var wcid in facts.Wcids)
        {
            if (!kinds.ContainsKey(wcid))
            {
                kinds[wcid] = Compose(facts, wcid, null); // a kind a profile records and the table does not hold: none today (BestiaryTests pins it)
            }
        }

        return kinds.ToFrozenDictionary();
    }

    private static CreatureCapabilities Compose(CreatureFacts facts, uint wcid, CreatureRow? row)
    {
        var fact = facts.Find(wcid) ?? throw new InvalidOperationException($"weenie {wcid} has a row or a profile fact and no facts");
        var maxHealth = facts.MaxHealthOf(wcid, out var source);
        var resists = CreatureResists.RowOf(wcid);
        var skills = CreatureSkills.RowsOf(wcid);
        var behaviour = BehaviourOf(wcid);
        return Of(fact, maxHealth, source, row, resists, skills, DrainResist.RecordedOf(wcid), CreatureAwareness.MetresOf(wcid), CreatureAwareness.IsPinned(wcid), behaviour);
    }
}
