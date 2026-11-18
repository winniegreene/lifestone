using Lifestone.Client.Session.Casting;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1, m6-plan.md D139): ACE's resist roll of a spell of hers on a creature, the one function a drain, a Harm Other and a bolt ask whether the target lets the spell through. Before this
/// slice two readers disagreed about a Magic Defense of 0: the drain pick took it as unknown and gave an even 0.5, the bolt's rate took it as untrained and gave 1.0. ACE has neither case.
/// </summary>
/// <remarks>
/// <para><b>The roll</b> (@95ef426d). A drain, a Harm Other and a bolt's projectile all go through <c>WorldObject.TryResistSpell</c> (WorldObject_Magic.cs:119-223): a spell with no projectile from
/// <c>TryCastSpell_Inner</c> (:62-74), a projectile when it strikes (<c>SpellProjectile.CalculateDamage</c>, SpellProjectile.cs:419, the only call with <c>projectileHit</c>). It takes the caster's current skill in the
/// spell's school (:141-145) and the target's effective Magic Defense (<c>Creature.GetEffectiveMagicDefense</c>, Creature_Magic.cs:172-183: its Magic Defense skill, times a wielded weapon's modifier, plus imbues), and
/// rolls <c>MagicDefenseCheck</c> (:104-113): the spell lands with the chance <c>SkillCheck.GetSkillChance(skill, defence)</c> at the regular factor 0.03 (SkillCheck.cs:7-12),
/// <c>1 - 1 / (1 + e^(0.03 x (skill - defence)))</c>. Nothing in it treats 0 apart: against a Magic Defense of 0 the chance is the curve of her skill alone, 0.99987 at a skill of 298 and 0.818 at 50; never an even
/// 0.5 and not exactly 1.</para>
/// <para><b>No row and a row of 0 are the same creature.</b> A creature whose weenie states no Magic Defense skill gets an empty, untrained one when the skill is first asked (<c>Creature.GetCreatureSkill</c>,
/// Creature_Skills.cs:19-48, the class set at :31), and an untrained Magic Defense is usable (the dats' SkillTable gives it min level 1; <c>CreatureSkill.IsUsable</c>, Entity/CreatureSkill.cs:48-63), so its value is
/// the formula's share, round((Focus + Self) / 7), plus a start and ranks of 0 (<c>CreatureSkill.Current</c>, :154-195). A row that states a start and ranks of 0 untrained, trained or specialized gives the same number.
/// Of the world database's creature weenies 3,285 state no row; 644 of those come to 0 (Focus and Self too small for the formula); no weenie with a row comes to 0 (the creature table, <c>creatures.json</c>, 2026-11-17).</para>
/// <para><b>Confirmed by the server's own objects</b> (<c>tools/fight-sim.ps1 matchup</c>, 2026-11-17, a character with War and Life Magic 298): the Straw Drudge (weenie 2442, no row, Magic Defense 0) "a resistable
/// spell of that school lands 100.0 % of the time"; the Guardian Golem (weenie 43247, no row, Focus 850 and Self 890, Magic Defense 249) 81.3 %; the Guardian Spirit (weenie 44473, no row, Magic Defense 286) 58.9 %:
/// each the curve's value.</para>
/// <para><b>What only a creature does.</b> ACE lets only a creature resist (:160-162): a target that is none never resists. A weenie class the bestiary does not hold is read with a Magic Defense of 0 by its callers, who
/// name it (<c>TODO(engine-bestiary-unknown-kind)</c>); for a skilled caster the curve at 0 and "never resists" differ by less than one cast in a thousand from a skill of 231 up.</para>
/// </remarks>
public static class ResistRoll
{
    /// <summary>The chance a resistable spell cast with <paramref name="skill"/> is not resisted by a creature of Magic Defense <paramref name="magicDefence"/> (see the class). Allocates nothing.</summary>
    /// <param name="skill">Her current (buffed) skill in the spell's school.</param>
    /// <param name="magicDefence">The creature's Magic Defense as ACE computes it; 0 is a Magic Defense of 0, as ACE rolls it.</param>
    public static double LandChance(uint skill, uint magicDefence) => ManaConversionModel.SkillChance(skill, magicDefence);
}
