// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/SkillFormula.cs (CalcArmorMod, lines 16 and 29-37: the armor level as a damage multiplier) (AGPL-3.0)
// [with changes: the armor level is the spell's alone, and the protection's multiplier is taken with it, as DamageEvent.DoCalculateDamage multiplies ArmorMod and ResistanceMod (Entity/DamageEvent.cs:365)]
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 X (spells are the armor; the spec section 5): what a protective spell takes off a creature's melee hit, and so the order the plan buys them in. Pure. ACE's hit on a player is
/// <c>DamageBeforeMitigation x ArmorMod x ShieldMod x ResistanceMod x DamageResistanceRatingMod</c> (<c>DamageEvent.cs:365</c> @95ef426d): <c>ArmorMod</c> is <c>66.67 / (AL + 66.67)</c> over the worn pieces' AL and the
/// body armor enchantment, Armor Self (<c>Monster_Melee.cs:425-458</c>, <c>SkillFormula.cs:29-37</c>), and <c>ResistanceMod</c> is the protection's multiplier against that damage type (<c>Creature_Properties.cs:100-154</c>,
/// the stronger of the protection and the natural resistance). The spells' values are ACE's spell table (<see cref="BuffStatMods"/>): Armor Self 20, 50, 75, 100, 150, 200; a protection 0.91, 0.8, 0.67, 0.57, 0.5, 0.4.
/// </summary>
/// <remarks>
/// The two multiply, so a step's value is the log of the hit before over the hit after, and two steps compare by that log per coin: <see cref="Gain"/> and <see cref="PerCoin"/>. A worn piece's AL shrinks an Armor Self step a
/// little; the plan counts the spell alone (the climber's robe has next to none: gearing is the spells, the spec section 5).
/// </remarks>
public static class ProtectiveValue
{
    /// <summary>ACE's <c>SkillFormula.ArmorMod</c>, 200 / 3.</summary>
    public const double ArmorScale = 200.0 / 3.0;

    /// <summary>ACE's <c>CalcArmorMod</c> for a positive armor level, 1 at none.</summary>
    /// <param name="armorLevel">The armor level.</param>
    public static double ArmorFactor(double armorLevel) => armorLevel > 0 ? ArmorScale / (armorLevel + ArmorScale) : 1.0;

    /// <summary>What the spell <paramref name="spellId"/> multiplies a hit by: the armor factor of a body armor spell, a protection's own multiplier, or 1 for any other spell.</summary>
    /// <param name="spellId">The spell.</param>
    public static double FactorOf(uint spellId)
    {
        if (!BuffStatMods.BySpell.TryGetValue(spellId, out var mod))
        {
            return 1.0;
        }

        return mod.IsAdditive ? ArmorFactor(mod.Value) : mod.Value < 1.0 ? mod.Value : 1.0;
    }

    /// <summary>A hit of <paramref name="hit"/> after <paramref name="armorSpell"/> and <paramref name="protectionSpell"/> (0: none).</summary>
    /// <param name="hit">The hit before mitigation.</param>
    /// <param name="armorSpell">The Armor Self level's spell, or 0.</param>
    /// <param name="protectionSpell">The protection level's spell, or 0.</param>
    public static double Hit(double hit, uint armorSpell, uint protectionSpell) => hit * (armorSpell == 0 ? 1.0 : FactorOf(armorSpell)) * (protectionSpell == 0 ? 1.0 : FactorOf(protectionSpell));

    /// <summary>The value of learning <paramref name="target"/>: the log of the hit under the highest level of its line below it that <paramref name="book"/> holds (or none) over the hit under the target; 0 when the book holds the target or a level above it.</summary>
    /// <param name="line">The line's spells, I first.</param>
    /// <param name="target">The level the plan would learn.</param>
    /// <param name="book">The spellbook.</param>
    public static double Gain(IReadOnlyList<ScrivenerSpell> line, ScrivenerSpell target, Spellbook book)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(book);
        var before = 1.0;
        foreach (var spell in line)
        {
            if (!book.Knows(spell.SpellId))
            {
                continue;
            }

            if (spell.Level >= target.Level)
            {
                return 0;
            }

            before = FactorOf(spell.SpellId); // the list runs up: the highest held below the target stands
        }

        return Math.Log(before / FactorOf(target.SpellId));
    }

    /// <summary>A gain per pyreal: the order of the plan's protective lines.</summary>
    /// <param name="gain">The step's <see cref="Gain"/>.</param>
    /// <param name="price">The scroll's price.</param>
    public static double PerCoin(double gain, int price) => price > 0 ? gain / price : 0;
}
