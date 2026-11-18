using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Session.Grounds;

/// <summary>One creature weenie of the world database (<see cref="CreatureTable"/>).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level (0 when the weenie records none).</param>
/// <param name="MaxHealth">
/// ACE's maximum health of the creature with no enchantment, as the server builds it (<see cref="CreatureTable.MaxHealthOf"/> of its rows). M6 16 E1e: never absent and never under 1. A weenie that states no MaxHealth row or
/// no Endurance has the health ACE gives such a creature, and <see cref="MaxHealthNote"/> says which of ACE's rules stands in the number; before, such a weenie read null and one with a start, ranks and Endurance of 0 read 0.
/// </param>
/// <param name="ResistHealthDrain">ACE's ResistHealthDrain (float 125; 1 when the weenie records none).</param>
/// <param name="DamageTypes">The OR of its body parts' attack damage types (0: none recorded); M6 14 the losing race: with its wielded weapon's and its launcher's ammunition's.</param>
/// <param name="MagicDefence">
/// ACE's Magic Defense (<see cref="GroundDraft.MagicDefenseOf"/> of its rows: <c>CreatureSkill.Current</c>, its init_Level and ranks and round((Focus + Self) / 7)). M6 14 attack timing (the exactness check): an untrained or absent Magic
/// Defense is ACE's too, not 0: ACE gives a creature with no row an untrained one (<c>Creature_Skills.GetCreatureSkill</c>) and an untrained skill the formula's share when the SkillTable's min level is 1 (<c>CreatureSkill.IsUsable</c>,
/// CreatureSkill.cs:48-63 @95ef426d), which the dats' Magic Defense has (<see cref="CreatureTable.MagicDefenseMinLevel"/>, a dat test pins it).
/// </param>
public sealed record CreatureRow(uint Wcid, string Name, int Level, int MaxHealth, double ResistHealthDrain, uint DamageTypes, int MagicDefence)
{
    /// <summary>
    /// M6 16 E1e: which of ACE's rules for an unstated row stands in <see cref="MaxHealth"/>, in words (<see cref="CreatureTable.MaxHealthNoteOf"/>); null when the weenie states its MaxHealth row and an Endurance of 1 or
    /// more, so the number is its own rows' alone.
    /// </summary>
    public string? MaxHealthNote { get; init; }

    /// <summary>
    /// M6 14 the losing race (live 2026-11-15 04:13 UTC: "'Tuskie Gunner' (weenie 22521: no profile records its attack: the general fallback 50%)"): its hardest physical attack as ACE deals it (<see cref="CreatureAttack"/>), or null when the
    /// world database records none (no body part with a damage value, no melee or thrown weapon, no launcher with ammunition: a creature that only casts, or none that attacks).
    /// </summary>
    public CreatureAttack? Attack { get; init; }

    /// <summary>M6 14 attack timing: what ACE times its attacks by (<see cref="CreatureTimingFacts"/>, the <c>timing</c> SELECTs of the same file); the seconds themselves are read from the user's dats at run time (<see cref="Vitals.AttackTimings"/>).</summary>
    public CreatureTimingFacts? Timing { get; init; }
}

/// <summary>
/// M6 14 attack timing (the owner, 2026-11-15: the creature stats and the formulas are on hand, so every number is computed, never guessed): the world database's facts ACE's monster AI times a creature's attacks by (@95ef426d <c>Monster_Melee.MeleeAttack</c>,
/// <c>GetCombatManeuver</c>, <c>Creature_Combat.GetAnimSpeed</c>, <c>Monster_Combat.GetNextAttackType</c>, <c>Monster_Magic.TryRollSpell</c> and <c>MagicAttack</c>, <c>Monster_Missile.LaunchMissile</c>). The animation lengths and the
/// attack frames are the dats' and are read at run time (<see cref="Vitals.AttackTimings.Derive"/>).
/// </summary>
/// <param name="MotionTable">Its MotionTable (DID 2), 0 when none.</param>
/// <param name="CombatTable">Its CombatTable (DID 4), 0 when none.</param>
/// <param name="PowerupTime">Its PowerupTime (float 34, the most of the random delay after a swing), or null: ACE's 1 s.</param>
/// <param name="MagicDelay">Its AiUseMagicDelay (float 80, the least seconds from an attack's start to a cast's), or null: ACE's 3 s.</param>
/// <param name="Quickness">Its Quickness (init_Level + ranks; 0 when none), which sets its attack animations' speed.</param>
/// <param name="HumanMagic">Its AiUseHumanMagicAnimations (bool 7): it casts with the spell's gestures, not the CastSpell motion.</param>
/// <param name="WeaponCombatStyle">Its wielded melee or thrown weapon's DefaultCombatStyle (int 46), 0 when none.</param>
/// <param name="WeaponAttackType">That weapon's AttackType (int 47), 0 when none.</param>
/// <param name="LauncherCombatStyle">Its launcher's DefaultCombatStyle, 0 when none.</param>
/// <param name="MeleeWeapons">The melee weapons it wields (two: it dual wields).</param>
/// <param name="Shields">The shields it wields.</param>
/// <param name="WieldedTreasure">Its WieldedTreasureType (DID 32, a weapon rolled at spawn), 0 when none.</param>
/// <param name="Spells">Its spell book in ACE's iteration order.</param>
public sealed record CreatureTimingFacts(
    uint MotionTable,
    uint CombatTable,
    double? PowerupTime,
    double? MagicDelay,
    int Quickness,
    bool HumanMagic,
    int WeaponCombatStyle,
    int WeaponAttackType,
    int LauncherCombatStyle,
    int MeleeWeapons,
    int Shields,
    uint WieldedTreasure,
    IReadOnlyList<CreatureSpell> Spells)
{
    /// <summary>It throws its wielded weapon (a thrown weapon, WeenieType Missile).</summary>
    public bool Thrown { get; init; }

    /// <summary>It wields a missile launcher (ACE's <c>IsRanged</c> with <see cref="Thrown"/>).</summary>
    public bool Launcher { get; init; }

    /// <summary>It wields a melee weapon (its stance is the weapon's, not HandCombat).</summary>
    public bool MeleeWeapon { get; init; }

    /// <summary>M6 14 the derived incoming: its six attributes, the terms of its skills' formulas (<see cref="CreatureSkills.Current"/>).</summary>
    public CreatureAttributes Attributes { get; init; }

    /// <summary>M6 14 the derived incoming: the weenie class of the weapon its physical attack uses (its launcher's for a ranged attack, else its melee or thrown weapon's), 0 with none: its attack skill's (<see cref="CreatureSkills.AttackSkillId"/>).</summary>
    public uint AttackWeapon { get; init; }
}

/// <summary>
/// M6 14 attack timing: one spell of a creature's book (the <c>spells</c> table of <c>creatures.json</c>): its chance a decision (ACE's <c>TryRollSpell</c>: a probability over 2 is <c>probability - 2</c>, else <c>probability / 100</c>) and what it
/// deals (a war projectile's <c>base_Intensity</c> to <c>base_Intensity + variance</c> a projectile, <c>num_Projectiles</c> of them, of element <c>e_Type</c>; a life boost or transfer by its <c>boost</c> and <c>source</c>).
/// </summary>
/// <param name="Id">The spell.</param>
/// <param name="Name">Its name.</param>
/// <param name="Probability">The book's probability as stored.</param>
/// <param name="Element">A projectile's DamageType bits (<c>e_Type</c>), 0 when none.</param>
/// <param name="BaseIntensity">A projectile's least damage, -1 when none.</param>
/// <param name="Variance">A projectile's spread above it, -1 when none.</param>
/// <param name="Projectiles">The projectiles a cast, -1 when none.</param>
/// <param name="Boost">A boost's least change (negative: a harm), 0 when none.</param>
/// <param name="BoostVariance">A boost's spread.</param>
/// <param name="DamageType">A boost's or a life projectile's damage type (0x80 health), -1 when none.</param>
/// <param name="TransferSource">A transfer's source vital (2: health), -1 when none.</param>
public sealed record CreatureSpell(uint Id, string Name, double Probability, uint Element, int BaseIntensity, int Variance, int Projectiles, int Boost, int BoostVariance, int DamageType, int TransferSource)
{
    /// <summary>ACE's chance of the spell when the book is rolled (<c>Monster_Magic.TryRollSpell</c>).</summary>
    public double Chance => Probability > 2.0 ? Probability - 2.0 : Probability / 100.0;
}

/// <summary>
/// M6 14 the losing race: one creature's hardest physical attack, as ACE's monster damage reads it (@95ef426d <c>Entity/DamageEvent.cs</c> <c>DoCalculateDamage</c>): its base damage range from the body part
/// (<c>Monster_Melee.GetBaseDamage</c> and <c>GetAttackPart</c>), a wielded melee weapon (ACE uses its damage for every melee attack), a thrown weapon (<c>Monster_Missile.GetMissileDamage</c>: the missile itself) or a launcher's
/// ammunition times the launcher's DamageMod; the attribute modifier (<c>SkillFormula.GetAttributeMod</c>: Strength, Coordination for a launcher); the critical chance (the weapon's CriticalFrequency, else 0.1) and multiplier (the weapon's
/// CriticalMultiplier, else 1). The armour, the shield and her resistance are hers: the reader applies them (<c>Vitals.LiveFightLine</c>).
/// </summary>
/// <param name="Kind">Where the attack comes from, in words (a body part, or the weapon by weenie class).</param>
/// <param name="Hitter">The attack's terms: its DVal (ACE's <c>BaseDamageMod.MaxDamage</c>), DVar, attribute and critical.</param>
/// <param name="DamageType">The attack's DamageType bits.</param>
public sealed record CreatureAttack(string Kind, LadderHitter Hitter, uint DamageType);

/// <summary>
/// M6 14 walk drains (the owner, 2026-11-15: the target's health and kind are known data, and the server's database holds every creature): every creature weenie of the pinned world SQL, its name, level, maximum health, health-drain
/// resistance and damage types (<c>creatures.json</c>, embedded; its <c>sources</c> name the SELECT). Read after the ground profiles (<see cref="Vitals.CreatureFacts.MaxHealthOf"/>, <see cref="Vitals.DrainResist.Of"/>): the profiles'
/// facts first, this table second. The maximum health is the profiles' own formula (<see cref="GroundDraft.MaxHealthOf"/>) over the weenie's rows, with ACE's rules for a row the weenie does not state (M6 16 E1e, <see cref="MaxHealthOf"/>). M6 14 the losing race (the owner: "It's impossible for there to be an unknown creature -
/// we have the server database"): each weenie's attack too (<see cref="CreatureRow.Attack"/>, the <c>attacks</c> SELECT of the same file), so every attacker's hit is derived (<see cref="Vitals.CreatureFacts.Find"/>).
/// </summary>
public static class CreatureTable
{
    /// <summary>The embedded resource's logical name.</summary>
    public const string ResourceName = "Lifestone.Client.Session.Grounds.creatures.json";

    private const int MeleeWeapon = 6; // ACE WeenieType.MeleeWeapon
    private const int ThrownWeapon = 4; // ACE WeenieType.Missile: a thrown weapon is its own missile

    private static readonly Lazy<FrozenDictionary<uint, CreatureRow>> Rows = new(Load);

    /// <summary>How many creature weenies the table holds.</summary>
    public static int Count => Rows.Value.Count;

    /// <summary>The row of <paramref name="wcid"/>, or null when it is no creature weenie of the world database.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static CreatureRow? Find(uint wcid) => Rows.Value.TryGetValue(wcid, out var row) ? row : null;

    /// <summary>Every row (the facts' table fallback is built over them once).</summary>
    public static IEnumerable<CreatureRow> All => Rows.Value.Values;

    /// <summary>Parses the table's JSON (<c>columns</c> and <c>creatures</c> rows).</summary>
    /// <param name="json">The document.</param>
    internal static FrozenDictionary<uint, CreatureRow> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        string[] expected =
        [
            "wcid", "name", "level", "health_start", "health_ranks", "endurance", "resist_health_drain", "damage_types", "defence_start", "defence_ranks", "defence_sac", "focus", "self",
            "strength", "coordination", "part_dval", "part_dvar", "part_dtype", "weapon_wcid", "weapon_kind", "weapon_damage", "weapon_damage_mod", "weapon_variance", "weapon_dtype", "weapon_crit_multiplier", "weapon_crit_frequency",
            "launcher_wcid", "launcher_damage_mod", "launcher_crit_multiplier", "launcher_crit_frequency", "ammo_damage", "ammo_variance", "ammo_dtype",
            "motion_table", "combat_table", "powerup_time", "magic_delay", "quickness", "human_magic", "weapon_combat_style", "weapon_attack_type", "launcher_combat_style", "melee_weapons", "shields", "wielded_treasure", "spells",
        ];
        if (!columns.SequenceEqual(expected))
        {
            throw new InvalidDataException($"creatures.json: the columns are [{string.Join(", ", columns)}], not [{string.Join(", ", expected)}]");
        }

        var spells = SpellsOf(doc.RootElement);
        var rows = new Dictionary<uint, CreatureRow>();
        foreach (var r in doc.RootElement.GetProperty("creatures").EnumerateArray())
        {
            var wcid = r[0].GetUInt32();
            var name = r[1].GetString() ?? string.Empty;
            var (start, ranks, endurance) = (r[3].GetInt32(), r[4].GetInt32(), r[5].GetInt32());
            var health = MaxHealthOf(start, ranks, endurance);
            var (defenceStart, defenceRanks, sac, focus, self) = (r[8].GetInt32(), r[9].GetInt32(), r[10].GetInt32(), r[11].GetInt32(), r[12].GetInt32());
            var defence = MagicDefenceOf(defenceStart, defenceRanks, sac, focus, self);
            var attack = AttackOf(wcid, name, r, out var attackTypes);
            rows[wcid] = new CreatureRow(wcid, name, r[2].GetInt32(), health, r[6].GetDouble(), r[7].GetUInt32() | attackTypes, defence) { Attack = attack, Timing = TimingOf(r, spells), MaxHealthNote = MaxHealthNoteOf(start, ranks, endurance) };
        }

        return rows.ToFrozenDictionary();
    }

    /// <summary>
    /// M6 16 E1e: ACE's maximum health of a creature with no enchantment from its row's three numbers (-1 where the weenie states none), as the server builds it (@95ef426d <c>CreatureVital.GetMaxValue</c>,
    /// Entity/CreatureVital.cs:127-174): the vital's start plus its ranks plus Endurance / 2 rounded half up (<see cref="GroundDraft.MaxHealthOf"/>), with ACE's three rules for what a weenie does not state.
    /// <list type="bullet">
    /// <item>A weenie with no MaxHealth row gets an empty one, start 0 and ranks 0 (the vital's constructor, Entity/CreatureVital.cs:29-33; every creature is built with the three vitals, Creature.cs:98-100).</item>
    /// <item>The Endurance the formula reads is the attribute's current value, never under 1 (<c>CreatureAttribute.GetCurrent</c>, Entity/CreatureAttribute.cs:141-155; an attribute the weenie does not state is built
    /// empty, :23-33, Creature.cs:103-108), so no Endurance, or one of 0, adds round(1 / 2) = 1.</item>
    /// <item>The whole is never under 1 (Entity/CreatureVital.cs:167-171).</item>
    /// </list>
    /// The server's own objects say the same (<c>tools/fight-sim.ps1 creature</c>, 2026-11-17): weenie 32400 (no MaxHealth row, no Endurance) health 1; 31228 (start 0, ranks 0, Endurance 0) 1; 35932 (start 0, no Endurance) 1;
    /// 39009 (start 200, no Endurance) 201; 52289 (no MaxHealth row, Endurance 200) 100; 87221 (no MaxHealth row, Endurance 100) 50. The same floor is the behaviour reader's for stamina and mana
    /// (<see cref="CreatureBehaviourRow.AttributeOf"/>).
    /// </summary>
    /// <param name="start">The MaxHealth row's init_Level, -1 when the weenie states no row.</param>
    /// <param name="ranks">Its level_From_C_P, -1 when the weenie states no row.</param>
    /// <param name="endurance">Its Endurance (init_Level + level_From_C_P), -1 when the weenie states none.</param>
    internal static int MaxHealthOf(int start, int ranks, int endurance) =>
        Math.Max(1, GroundDraft.MaxHealthOf((uint)Math.Max(0, start), (uint)Math.Max(0, ranks), (uint)Math.Max(1, endurance)));

    /// <summary>M6 16 E1e: which of ACE's rules for an unstated row stands in <see cref="MaxHealthOf"/>'s number, in words for a source line; null when the weenie states the MaxHealth row and an Endurance of 1 or more.</summary>
    /// <param name="start">The MaxHealth row's init_Level, -1 when the weenie states no row.</param>
    /// <param name="ranks">Its level_From_C_P, -1 when the weenie states no row.</param>
    /// <param name="endurance">Its Endurance, -1 when the weenie states none.</param>
    internal static string? MaxHealthNoteOf(int start, int ranks, int endurance)
    {
        var noVital = start < 0 || ranks < 0;
        var noEndurance = endurance < 1;
        var attribute = endurance < 0 ? "it states no Endurance, and ACE reads an attribute never under 1" : "its Endurance is 0, and ACE reads an attribute never under 1";
        return (noVital, noEndurance) switch
        {
            (false, false) => null,
            (true, false) => "the weenie states no MaxHealth row: ACE builds an empty one, so the health is its Endurance's share alone",
            (false, true) => "its MaxHealth row stands; " + attribute,
            _ => "the weenie states no MaxHealth row: ACE builds an empty one; " + attribute,
        };
    }

    /// <summary>The dats' SkillTable <c>_min_level</c> of Magic Defense (skill 15): 1, usable untrained (<c>AttackTimingTests</c> reads it from the dats).</summary>
    public const int MagicDefenseMinLevel = 1;

    /// <summary>
    /// M6 14 attack timing (the exactness check): ACE's <c>CreatureSkill.Current</c> of a creature's Magic Defense from its row (-1 where absent): the formula's share (round((Focus + Self) / 7), <see cref="GroundDraft.MagicDefenseOf"/>)
    /// when the skill is usable (trained, specialized, or untrained or absent at <see cref="MagicDefenseMinLevel"/> 1; an inactive one, s_a_c 0, adds none), plus its init_Level and ranks (0 when absent).
    /// </summary>
    /// <param name="start">init_Level, -1 when absent.</param>
    /// <param name="ranks">level_From_P_P, -1 when absent.</param>
    /// <param name="sac">s_a_c, -1 when the row is absent (ACE adds it untrained).</param>
    /// <param name="focus">Focus, -1 when absent.</param>
    /// <param name="self">Self, -1 when absent.</param>
    internal static int MagicDefenceOf(int start, int ranks, int sac, int focus, int self)
    {
        var usable = sac >= GroundDraft.TrainedSkill || ((sac < 0 || sac == 1) && MagicDefenseMinLevel == 1);
        var (f, s) = ((uint)Math.Max(0, focus), (uint)Math.Max(0, self));
        return usable ? GroundDraft.MagicDefenseOf((uint)Math.Max(0, start), (uint)Math.Max(0, ranks), f, s) : Math.Max(0, start) + Math.Max(0, ranks);
    }

    /// <summary>M6 14 attack timing: the <c>spells</c> table (<c>spell_columns</c>), by id.</summary>
    private static Dictionary<uint, CreatureSpell> SpellsOf(JsonElement root)
    {
        var columns = root.GetProperty("spell_columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        string[] expected = ["id", "name", "element", "base_intensity", "variance", "projectiles", "boost", "boost_variance", "damage_type", "transfer_source", "transfer_cap"];
        if (!columns.SequenceEqual(expected))
        {
            throw new InvalidDataException($"creatures.json: the spell columns are [{string.Join(", ", columns)}], not [{string.Join(", ", expected)}]");
        }

        var spells = new Dictionary<uint, CreatureSpell>();
        foreach (var s in root.GetProperty("spells").EnumerateArray())
        {
            var id = s[0].GetUInt32();
            spells[id] = new CreatureSpell(id, s[1].GetString() ?? string.Empty, 0, (uint)Math.Max(0, s[2].GetInt32()), s[3].GetInt32(), s[4].GetInt32(), s[5].GetInt32(), s[6].GetInt32(), s[7].GetInt32(), s[8].GetInt32(), s[9].GetInt32());
        }

        return spells;
    }

    /// <summary>M6 14 attack timing: the row's timing facts (columns 33 on); a book entry whose spell the table does not hold keeps its id with no damage facts.</summary>
    private static CreatureTimingFacts TimingOf(JsonElement r, Dictionary<uint, CreatureSpell> spells)
    {
        var book = new List<CreatureSpell>();
        foreach (var e in r[45].EnumerateArray())
        {
            var id = e[0].GetUInt32();
            var probability = e[1].GetDouble();
            book.Add(spells.TryGetValue(id, out var s)
                ? s with { Probability = probability }
                : new CreatureSpell(id, string.Create(CultureInfo.InvariantCulture, $"spell {id}"), probability, 0, -1, -1, -1, 0, 0, -1, -1));
        }

        var weaponKind = r[19].GetInt32();
        return new CreatureTimingFacts(
            (uint)Math.Max(0, r[33].GetInt64()),
            (uint)Math.Max(0, r[34].GetInt64()),
            r[35].GetDouble() is var powerup and >= 0 ? powerup : null,
            r[36].GetDouble() is var delay and >= 0 ? delay : null,
            Math.Max(0, r[37].GetInt32()),
            r[38].GetInt32() != 0,
            Math.Max(0, r[39].GetInt32()),
            Math.Max(0, r[40].GetInt32()),
            Math.Max(0, r[41].GetInt32()),
            r[42].GetInt32(),
            r[43].GetInt32(),
            (uint)Math.Max(0, r[44].GetInt64()),
            book)
        {
            Thrown = r[18].GetInt32() > 0 && weaponKind == ThrownWeapon,
            MeleeWeapon = r[18].GetInt32() > 0 && weaponKind == MeleeWeapon,
            Launcher = r[26].GetInt32() > 0,
            Attributes = new CreatureAttributes(Math.Max(0, r[13].GetInt32()), Math.Max(0, r[5].GetInt32()), Math.Max(0, r[37].GetInt32()), Math.Max(0, r[14].GetInt32()), Math.Max(0, r[11].GetInt32()), Math.Max(0, r[12].GetInt32())),
            AttackWeapon = (uint)Math.Max(0, r[26].GetInt32() > 0 ? r[26].GetInt32() : r[18].GetInt32()),
        };
    }

    /// <summary>
    /// The hardest of the row's attacks by ACE's largest uncritical hit (<see cref="Vitals.FightLines.MaxHit"/>): the body part, the wielded melee or thrown weapon, the launcher with its ammunition. <paramref name="types"/> is the OR of
    /// the weapon's and the ammunition's damage types (the body parts' are the row's own column).
    /// </summary>
    private static CreatureAttack? AttackOf(uint wcid, string name, JsonElement r, out uint types)
    {
        types = 0;
        var (strength, coordination) = (Math.Max(0, r[13].GetInt32()), Math.Max(0, r[14].GetInt32()));
        CreatureAttack? best = null;
        if (r[15].GetInt32() is var partVal and > 0)
        {
            best = Keep(best, new CreatureAttack("its hardest body part", new LadderHitter(name, wcid, partVal, Math.Max(0, r[16].GetDouble()), strength), (uint)Math.Max(0, r[17].GetInt32())));
        }

        var (weaponWcid, weaponKind, weaponDamage) = (r[18].GetInt32(), r[19].GetInt32(), r[20].GetInt32());
        if (weaponWcid > 0 && weaponKind is MeleeWeapon or ThrownWeapon && weaponDamage > 0)
        {
            var dval = (int)Math.Round(weaponDamage * Or(r[21].GetDouble(), 1.0));
            var dtype = (uint)Math.Max(0, r[23].GetInt32());
            types |= dtype;
            var hitter = new LadderHitter(name, wcid, dval, Math.Max(0, r[22].GetDouble()), strength)
            {
                CriticalMultiplier = Or(r[24].GetDouble(), LadderHitter.DefaultCriticalMultiplier),
                CriticalChance = Or(r[25].GetDouble(), LadderHitter.DefaultCriticalChance),
            };
            best = Keep(best, new CreatureAttack(string.Create(CultureInfo.InvariantCulture, $"its {(weaponKind == MeleeWeapon ? "melee" : "thrown")} weapon {weaponWcid}"), hitter, dtype));
        }

        var (launcherWcid, ammoDamage) = (r[26].GetInt32(), r[30].GetInt32());
        if (launcherWcid > 0 && ammoDamage > 0)
        {
            var dval = (int)Math.Round(ammoDamage * Or(r[27].GetDouble(), 1.0));
            var dtype = (uint)Math.Max(0, r[32].GetInt32());
            types |= dtype;
            var hitter = new LadderHitter(name, wcid, dval, Math.Max(0, r[31].GetDouble()), coordination)
            {
                AttributeFactor = LadderHitter.BowAttributeFactor,
                CriticalMultiplier = Or(r[28].GetDouble(), LadderHitter.DefaultCriticalMultiplier),
                CriticalChance = Or(r[29].GetDouble(), LadderHitter.DefaultCriticalChance),
            };
            best = Keep(best, new CreatureAttack(string.Create(CultureInfo.InvariantCulture, $"its launcher {launcherWcid} with its ammunition"), hitter, dtype));
        }

        return best;
    }

    private static CreatureAttack Keep(CreatureAttack? had, CreatureAttack next) =>
        had is null || Vitals.FightLines.MaxHit(next.Hitter) > Vitals.FightLines.MaxHit(had.Hitter) ? next : had;

    /// <summary>A property the row records (0 or more), else <paramref name="absent"/> (the extraction writes -1 for a property the weenie does not hold).</summary>
    private static double Or(double value, double absent) => value < 0 ? absent : value;

    private static FrozenDictionary<uint, CreatureRow> Load()
    {
        using var stream = typeof(CreatureTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"the embedded {ResourceName} is missing from the client assembly"));
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
