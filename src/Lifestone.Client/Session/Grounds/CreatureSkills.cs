using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Grounds;

/// <summary>A creature's six attributes as its weenie records them (init_Level + level_From_C_P; 0 when absent), the terms of ACE's skill formulas.</summary>
/// <param name="Strength">Strength (attribute 1).</param>
/// <param name="Endurance">Endurance (2).</param>
/// <param name="Quickness">Quickness (3).</param>
/// <param name="Coordination">Coordination (4).</param>
/// <param name="Focus">Focus (5).</param>
/// <param name="Self">Self (6).</param>
public readonly record struct CreatureAttributes(int Strength, int Endurance, int Quickness, int Coordination, int Focus, int Self)
{
    /// <summary>The attribute of id <paramref name="id"/> (ACE's PropertyAttribute: 1 Strength to 6 Self), 0 for another.</summary>
    /// <param name="id">The attribute id.</param>
    public uint Of(uint id) => (uint)Math.Max(0, id switch
    {
        1 => Strength,
        2 => Endurance,
        3 => Quickness,
        4 => Coordination,
        5 => Focus,
        6 => Self,
        _ => 0,
    });
}

/// <summary>One skill row of a creature weenie (<see cref="CreatureSkills"/>; weenie_properties_skill).</summary>
/// <param name="Skill">ACE's Skill id.</param>
/// <param name="Init">Its init_Level.</param>
/// <param name="Ranks">Its level_From_P_P.</param>
/// <param name="Sac">Its s_a_c (ACE's SkillAdvancementClass: Inactive 0, Untrained 1, Trained 2, Specialized 3).</param>
public readonly record struct CreatureSkillRow(uint Skill, int Init, int Ranks, int Sac);

/// <summary>
/// M6 14 the derived incoming (live ladder57 2026-11-17 10:42:05 UTC: five Nubilous Golems derived at 4.3 a second each, fight-sim's ACE at 0.8 to 1.5, her hits measured 3.6 for the pack): the creature skills ACE's two defence rolls read
/// (<c>creature-skills.json</c>, embedded; its <c>sources</c> name the two read-only SELECTs). ACE's <c>SkillCheck.GetSkillChance</c> (SkillCheck.cs:7-12 @95ef426d: 1 - 1 / (1 + e^(0.03 (skill - difficulty)))) decides both:
/// <list type="bullet">
/// <item>a creature's spell lands on her when its skill in the spell's school beats her Magic Defense (<c>TryResistSpell</c>, <c>MagicDefenseCheck</c>, WorldObject_Magic.cs:104-168);</item>
/// <item>its physical attack is evaded at 1 - the chance of its attack skill against her Melee (or Missile) Defense (<c>DamageEvent.GetEvadeChance</c>, DamageEvent.cs:386-397); its attack skill is its weapon's WeaponSkill, else Unarmed
/// Combat, converted to the post-MoA skill when the creature's init_Level of it is 0 (<c>Creature_Combat.GetCurrentWeaponSkill</c>, Creature_Combat.cs:426-457).</item>
/// </list>
/// A skill's value is ACE's <c>CreatureSkill.Current</c> base: init_Level + ranks + the dats' SkillTable formula over its attributes when the skill is usable (trained, specialized, or untrained where the SkillTable's min level is 1;
/// CreatureSkill.cs:48-63, 154-163), a skill with no row ACE's untrained default (<c>Creature_Skills.GetCreatureSkill</c>).
/// </summary>
public static class CreatureSkills
{
    /// <summary>The embedded resource's logical name.</summary>
    public const string ResourceName = "Lifestone.Client.Session.Grounds.creature-skills.json";

    /// <summary>ACE's Skill ids the rolls read.</summary>
    public const uint Sword = 11, Dagger = 4, UnarmedCombat = 13, MeleeDefense = 6, MissileDefense = 7, MagicDefense = 15, CreatureEnchantment = 31, ItemEnchantment = 32, LifeMagic = 33, WarMagic = 34, HeavyWeapons = 44, LightWeapons = 45, FinesseWeapons = 46, MissileWeapons = 47;

    /// <summary>ACE's SkillAdvancementClass: Untrained 1, Trained 2, Specialized 3 (Inactive 0).</summary>
    private const int Untrained = 1, Trained = 2;

    private static readonly Lazy<(FrozenDictionary<uint, CreatureSkillRow[]> Skills, FrozenDictionary<uint, uint> Weapons)> Table = new(Load);

    /// <summary>How many creature weenies record one of the skills read.</summary>
    public static int Count => Table.Value.Skills.Count;

    /// <summary>M6 16 E1a (the bestiary's facade): the skill rows of <paramref name="wcid"/> as the world database records them (empty when it records none of the skills read). The table's own rows, not a copy.</summary>
    /// <param name="wcid">The creature's weenie class.</param>
    internal static IReadOnlyList<CreatureSkillRow> RowsOf(uint wcid) => Table.Value.Skills.TryGetValue(wcid, out var rows) ? rows : [];

    /// <summary>M6 16 E1a: the weenie classes that record one of the skills read.</summary>
    internal static IEnumerable<uint> Wcids => Table.Value.Skills.Keys;

    /// <summary>The row of <paramref name="skill"/> of <paramref name="wcid"/>: its init_Level, ranks and advancement class; false when the weenie records none (ACE's untrained default).</summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="skill">The skill id.</param>
    /// <param name="init">Its init_Level.</param>
    /// <param name="ranks">Its level_From_P_P.</param>
    /// <param name="sac">Its s_a_c.</param>
    public static bool TryRow(uint wcid, uint skill, out int init, out int ranks, out int sac)
    {
        (init, ranks, sac) = (0, 0, Untrained);
        if (!Table.Value.Skills.TryGetValue(wcid, out var rows))
        {
            return false;
        }

        foreach (var r in rows)
        {
            if (r.Skill == skill)
            {
                (init, ranks, sac) = (r.Init, r.Ranks, r.Sac);
                return true;
            }
        }

        return false;
    }

    /// <summary>The WeaponSkill (int 48) of weapon <paramref name="weaponWcid"/>, or null when it records none.</summary>
    /// <param name="weaponWcid">The weapon's weenie class.</param>
    public static uint? WeaponSkillOf(uint weaponWcid) => Table.Value.Weapons.TryGetValue(weaponWcid, out var skill) ? skill : null;

    /// <summary>
    /// ACE's <c>CreatureSkill.Current</c> base of <paramref name="skill"/> of <paramref name="wcid"/>: init_Level + ranks + the SkillTable formula (<paramref name="entry"/>) over <paramref name="attributes"/> when usable; null when the
    /// SkillTable entry is not at hand (the dats are not read).
    /// </summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="skill">The skill id.</param>
    /// <param name="attributes">Its attributes.</param>
    /// <param name="entry">The dats' SkillTable entry of the skill.</param>
    public static uint? Current(uint wcid, uint skill, CreatureAttributes attributes, SkillTableEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        TryRow(wcid, skill, out var init, out var ranks, out var sac);
        var usable = sac >= Trained || (sac == Untrained && entry.MinLevel == 1);
        var formula = usable && entry.Formula.TryCalculate(attributes.Of(entry.Formula.Attribute1), attributes.Of(entry.Formula.Attribute2), out var part) ? part : 0u;
        return (uint)(Math.Max(0, init) + Math.Max(0, ranks)) + formula;
    }

    /// <summary>
    /// ACE's <c>Creature_Combat.GetCurrentWeaponSkill</c>: the skill of its weapon (<paramref name="weaponWcid"/>'s WeaponSkill), else Unarmed Combat; when the creature's init_Level of it is 0, the post-MoA skill: Missile Weapons for a
    /// ranged weapon, Heavy for a Sword, Finesse for a Dagger, else Light.
    /// </summary>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="weaponWcid">Its weapon's weenie class (its launcher's for a ranged attack), 0 when none.</param>
    /// <param name="ranged">The weapon is ranged (a launcher or a thrown weapon).</param>
    public static uint AttackSkillId(uint wcid, uint weaponWcid, bool ranged)
    {
        var skill = weaponWcid > 0 ? WeaponSkillOf(weaponWcid) ?? 0u : UnarmedCombat;
        TryRow(wcid, skill, out var init, out _, out _);
        if (init != 0)
        {
            return skill;
        }

        return weaponWcid > 0 && ranged ? MissileWeapons : skill == Sword ? HeavyWeapons : skill == Dagger ? FinesseWeapons : LightWeapons;
    }

    /// <summary>ACE's <c>SkillCheck.GetSkillChance</c> at its regular 0.03: the chance <paramref name="skill"/> beats <paramref name="difficulty"/>.</summary>
    /// <param name="skill">The attacker's skill.</param>
    /// <param name="difficulty">The defender's skill.</param>
    public static double Chance(double skill, double difficulty) => Math.Clamp(1.0 - (1.0 / (1.0 + Math.Exp(0.03 * (skill - difficulty)))), 0, 1);

    /// <summary>Parses the table's JSON (<c>skills</c> and <c>weapons</c> rows).</summary>
    /// <param name="json">The document.</param>
    internal static (FrozenDictionary<uint, CreatureSkillRow[]> Skills, FrozenDictionary<uint, uint> Weapons) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var skills = new Dictionary<uint, CreatureSkillRow[]>();
        foreach (var r in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            var length = r.GetArrayLength();
            if (length < 5 || (length - 1) % 4 != 0)
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"creature-skills.json: a row of {length} values is not a wcid and rows of [skill, init_Level, level_From_P_P, s_a_c]"));
            }

            var rows = new CreatureSkillRow[(length - 1) / 4];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new CreatureSkillRow(r[1 + (4 * i)].GetUInt32(), r[2 + (4 * i)].GetInt32(), r[3 + (4 * i)].GetInt32(), r[4 + (4 * i)].GetInt32());
            }

            skills[r[0].GetUInt32()] = rows;
        }

        var weapons = new Dictionary<uint, uint>();
        foreach (var w in doc.RootElement.GetProperty("weapons").EnumerateArray())
        {
            weapons[w[0].GetUInt32()] = w[1].GetUInt32();
        }

        return (skills.ToFrozenDictionary(), weapons.ToFrozenDictionary());
    }

    private static (FrozenDictionary<uint, CreatureSkillRow[]> Skills, FrozenDictionary<uint, uint> Weapons) Load()
    {
        using var stream = typeof(CreatureSkills).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded {ResourceName} is missing from the client assembly");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
