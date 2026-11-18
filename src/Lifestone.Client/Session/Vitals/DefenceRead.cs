using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 the derived incoming (live ladder57 2026-11-17 10:42:05 UTC: five Nubilous Golems derived at 4.3 a second each, every strike and spell counted as landing): her three defences as ACE's rolls read them, the creature's skill against
/// each (<see cref="CreatureSkills.Chance"/>): Melee Defense against a melee strike, Missile Defense against a missile (<c>DamageEvent.GetEvadeChance</c>, DamageEvent.cs:386-397 @95ef426d), Magic Defense against a spell
/// (<c>TryResistSpell</c>, WorldObject_Magic.cs:119-168). Each is her <c>CreatureSkill.Current</c> from the read model (<see cref="SkillRules.TryCurrent"/>: her attributes, ranks and skill enchantments), usable untrained as the SkillTable's
/// min level 1 makes them; null when not read (every strike or spell then lands: the safe side).
/// <para>Not counted, a named TODO (<see cref="ModsTodo"/>): ACE's effective defence also multiplies her weapon's defence modifier, her burden's and her stance's, and adds defence imbues (<c>GetEffectiveDefenseSkill</c>,
/// Creature_Combat.cs:481-500; <c>GetEffectiveMagicDefense</c>, Creature_Magic.cs:172).</para>
/// </summary>
/// <param name="Melee">Her Melee Defense, or null.</param>
/// <param name="Missile">Her Missile Defense, or null.</param>
/// <param name="Magic">Her Magic Defense, or null.</param>
public readonly record struct DefenceRead(double? Melee, double? Missile, double? Magic)
{
    /// <summary>The named TODO of the modifiers not counted.</summary>
    public const string ModsTodo = "TODO(m6-defence-mods): her weapon's, burden's and stance's defence modifiers and her defence imbues are not counted";

    /// <summary>Her three defences now from <paramref name="q"/>.</summary>
    /// <param name="q">Her qualities.</param>
    public static DefenceRead Of(PlayerQualities q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return new DefenceRead(Read(q, MageTemplate.MeleeDefense), Read(q, CreatureSkills.MissileDefense), Read(q, MageTemplate.MagicDefense));
    }

    /// <summary>The three in words, for a line.</summary>
    public string Words() => string.Create(CultureInfo.InvariantCulture, $"her Melee Defense {Text(Melee)}, Missile Defense {Text(Missile)}, Magic Defense {Text(Magic)} ({ModsTodo})");

    private static string Text(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "not read";

    private static double? Read(PlayerQualities q, uint skill)
    {
        var formula = skill == MageTemplate.MagicDefense ? GroundDraft.MagicDefenseFormula : QualitiesSkills.Formula(skill)?.Formula;
        return formula is { } f && q.IsKnown && SkillRules.TryCurrent(q, skill, f, SkillRules.UntrainedClass, out var value) ? value : null;
    }
}
