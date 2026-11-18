using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 item 14 (<c>m6-life-through</c>): the numbers of Harm Other, the Life Magic spell that takes a fixed amount of a creature's health with no projectile. The client's SpellTable carries each level's power, cost and
/// range; what a landed cast takes exists only in the server's world data.
/// </summary>
/// <remarks>
/// <para><b>The rows.</b> The read-only world copy (ACE-World-Database v0.9.296, the database the bundled backend loads), <c>SELECT id, name, boost, boost_Variance, damage_Type FROM spell WHERE id IN (7, 1172, 1173, 1174, 1175,
/// 1176, 2070)</c>, read 2026-11-12: Harm Other I 7 (-4, -2), II 1172 (-7, -5), III 1173 (-10, -9), IV 1174 (-16, -17), V 1175 (-24, -23), VI 1176 (-33, -30), VII 2070 "Heart Rend" (-42, -37); every row's
/// <c>damage_Type</c> is 128, health. <see cref="FightDebuffs"/> holds the same numbers for the picker's scoring (<c>LifeThroughRuleTests</c> pins the two together).</para>
/// <para><b>What a landed cast takes</b> (ACE @95ef426d, <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs:474-601</c>): an integer rolled uniformly between <c>boost</c> and <c>boost + boost_Variance</c>, both ends
/// included (<c>:485-490</c>; <c>SpellProperties.MaxBoost</c>, <c>SpellProperties.cs:393</c>; <c>ThreadSafeRandom.Next(int, int)</c> adds one to the upper end, <c>ACE.Common/ThreadSafeRandom.cs:28-31</c>), times the target's
/// health-drain resistance, rounded (<c>:491</c>), cut to the health it has left (<c>UpdateVitalDelta</c>, <c>:525</c>). The resistance is the creature's <c>ResistHealthDrain</c> (float property 125) times its natural
/// resistance (1.0 for anything but a player, <c>Creature_Properties.cs:154-158</c>) times its life-resist rating (int property 351; <c>Creature_Properties.cs:207-208</c>, <c>Creature_Rating.cs:413-430</c>); no creature of the profiles has the rating, so it is
/// the kind's <c>ResistHealthDrain</c>, read by <see cref="DrainResist"/> (M6 14 drain resistance: an Armored Tusker's is 0, and Harm Other takes nothing from it; <see cref="LifeThroughRule.Choose"/>). Nothing of the
/// caster enters: no damage rating (ACE reads it for projectiles only, <c>WorldObject_Magic.cs:2004</c>), no critical hit, no elemental or slayer bonus, no skill. Her skill decides only whether it lands: the cast
/// (<c>GetMagicSkillChance</c>, the 0.07 factor: <see cref="GearCasts.Chance"/>) and the target's resist (<c>TryResistSpell</c>, <c>:119-223</c>, the 0.03 factor of her Life Magic against its Magic Defense:
/// <see cref="DrainPicker.PassChance"/>).</para>
/// <para><b>On a Tusker Crimsonback</b> (weenie 1627: health 110, <c>ResistHealthDrain</c> 1, no life-resist rating, so the resistance is 1.0; Magic Defense 94 as ACE computes it, the skill row's 88 plus (Focus 20 + Self 20) / 7;
/// read from the same world copy): Harm Other IV takes 16 to 33, 24.5 on average, whatever her skill. At Life Magic 267 it is resisted 0.6% of the time (0.5% against the bare 88), and the cast itself passes
/// 99.8% at the SpellTable's power 175: 24.3 expected a cast, four to seven landed casts for the 110, 4.5 on average.</para>
/// <para><b>No line is asked.</b> ACE checks a targeted spell's range in two dimensions and nothing else (<c>Player.VerifySpellRange</c>, <c>Player_Magic.cs:481-506</c>, called before and after the wind-up, <c>:785</c> and
/// <c>:1022</c>); only a projectile is tested against the world (<c>IsProjectileVisible</c>, <c>WorldObject_Magic.cs:1835</c>, and the flight itself, <c>SpellProjectile.cs</c>). A spell with no projectile is resolved at
/// once on its target (<c>CreatePlayerSpell</c>, <c>Player_Magic.cs:1098-1135</c>), whatever stands between the two.</para>
/// </remarks>
public static class HarmOtherRows
{
    /// <summary>Harm Other I to VII, in the order of <see cref="VitalsMeans.HarmOther"/>.</summary>
    public static IReadOnlyList<HarmLevel> Rows { get; } =
    [
        new(1, 7, -4, -2),
        new(2, 1172, -7, -5),
        new(3, 1173, -10, -9),
        new(4, 1174, -16, -17),
        new(5, 1175, -24, -23),
        new(6, 1176, -33, -30),
        new(7, 2070, -42, -37),
    ];

    /// <summary>The row of spell <paramref name="id"/>, or null when it is no level of Harm Other.</summary>
    /// <param name="id">The spell id.</param>
    public static HarmLevel? RowOf(uint id)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].SpellId == id)
            {
                return Rows[i];
            }
        }

        return null;
    }
}

/// <summary>What the rule casts through the obstacle.</summary>
public enum LifeThroughSpell
{
    /// <summary>Nothing: no drain is worth a cast and no Harm Other level is to be had.</summary>
    None,

    /// <summary>Drain Health Other, at the level the drain picker chose.</summary>
    Drain,

    /// <summary>Harm Other.</summary>
    Harm,
}

/// <summary>The rule's answer: the spell, what each candidate was expected to take from the target (0 for one that was not offered), and why.</summary>
/// <param name="Spell">The spell to cast.</param>
/// <param name="DrainTakes">The health the drain was expected to take from the target: its chance times what a landing takes.</param>
/// <param name="HarmTakes">The health Harm Other was expected to take: its chance times its mean, cut to the health the target has left.</param>
/// <param name="Why">The reason, a fixed text (never built per call).</param>
public readonly record struct LifeThroughChoice(LifeThroughSpell Spell, double DrainTakes, double HarmTakes, string Why);

/// <summary>
/// M6 item 14 (<c>m6-life-through</c>, the owner's rule of 2026-11-12: an attacker her bolt cannot reach is fought with Life Magic): which of the two spells that need no line goes at it. Pure and allocation-free; the fight
/// (<c>FightScenario.LifeThrough.cs</c>) reads the numbers live and casts.
/// </summary>
/// <remarks>
/// <para><b>The two spells.</b> Drain Health Other takes a share of the target's CURRENT health and gives it to her (ACE <c>HandleCastSpell_Transfer</c>, <c>WorldObject_Magic.cs:697-760</c>; <see cref="DrainPicker"/>), so each
/// cast takes less than the one before, a cast at a target with one point left takes none, and it takes nothing at all while she is unhurt (the amount is scaled to the health she is missing, <c>:735-747</c>): it can
/// never finish a creature. Harm Other takes a fixed amount (<see cref="HarmOtherRows"/>) and gives her nothing.</para>
/// <para><b>The rule.</b> A drain is a candidate when the drain picker picks a level (<see cref="DrainPicker.Pick"/>, the existing scoring: she is missing health, the mana pays the level's floor, the target is in its
/// range and the expected heal is at least <see cref="DrainPicker.MinWorthShare"/> of her maximum health). Harm Other is a candidate when a level is known, cast at the fight's chance, paid for and in range. With both, the
/// drain goes while it is expected to take at least as much from the target as Harm Other would (a tie goes to the drain: it heals her as well); from there Harm Other finishes it. With one, that one. With neither,
/// nothing: the fight's walk and sidestep stand as before.</para>
/// </remarks>
public static class LifeThroughRule
{
    /// <summary>The drain takes at least what Harm Other would.</summary>
    public const string DrainTakesMore = "the drain takes at least what Harm Other would, and heals her";

    /// <summary>Harm Other takes more than the drain would.</summary>
    public const string HarmTakesMore = "Harm Other takes more than the drain would";

    /// <summary>No drain is a candidate.</summary>
    public const string HarmAlone = "no drain is worth a cast";

    /// <summary>No Harm Other level is a candidate.</summary>
    public const string DrainAlone = "no Harm Other level is to be had: the drain while it pays";

    /// <summary>Neither is a candidate.</summary>
    public const string Neither = "no drain is worth a cast and no Harm Other level is to be had";

    /// <summary>M6 14 drain resistance: the target's kind lets neither spell take anything (its <c>ResistHealthDrain</c> is 0).</summary>
    public const string Resisted = "it resists health drains wholly (ResistHealthDrain 0): neither a drain nor Harm Other takes anything";

    /// <summary>The health a landed cast of <paramref name="level"/> takes from the target in <paramref name="s"/> (ACE's transfer arithmetic, scaled to the health she is missing: <see cref="BuffStatMods.Transfer"/>).</summary>
    /// <param name="level">The drain level.</param>
    /// <param name="s">The situation.</param>
    public static uint DrainTakes(in DrainLevel level, in DrainSituation s)
    {
        var missing = s.HealthMax > s.Health ? s.HealthMax - s.Health : 0u;
        var row = level.Row;
        BuffStatMods.Transfer(in row, s.TargetHealth, missing, out var taken, out _, (float)s.TargetDrainResist); // M6 14 drain resistance: the kind's share, before the cap
        return taken;
    }

    /// <summary>The chance one cast of a Life Magic spell of <paramref name="power"/> lands on a target of <paramref name="magicDefence"/>: the cast's own chance times the chance it is not resisted.</summary>
    /// <param name="lifeMagic">Her current Life Magic.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="magicDefence">The target's Magic Defense; 0 is a Magic Defense of 0, as ACE rolls it (<see cref="ResistRoll"/>).</param>
    public static double LandChance(uint lifeMagic, uint power, uint magicDefence) => GearCasts.Chance(lifeMagic, power) * DrainPicker.PassChance(lifeMagic, magicDefence);

    /// <summary>The rule (see the class remarks).</summary>
    /// <param name="drain">The drain picker's choice (<see cref="DrainChoice.Index"/> under 0: no drain).</param>
    /// <param name="drainTakesIfLanded">What a landing of that drain takes from the target (<see cref="DrainTakes"/>).</param>
    /// <param name="harm">The Harm Other level on offer, or null.</param>
    /// <param name="harmChance">The chance it lands (<see cref="LandChance"/>).</param>
    /// <param name="targetHealth">The target's modelled health in points.</param>
    /// <param name="resist">
    /// M6 14 drain resistance: the target kind's health-drain resistance (<see cref="DrainResist.Of"/>), which scales Harm Other's roll (<c>WorldObject_Magic.cs:491</c> @95ef426d) as it scales the drain's take (the drain's own is in
    /// <paramref name="drain"/> and <paramref name="drainTakesIfLanded"/>, through <see cref="DrainSituation.TargetDrainResist"/>); 0: nothing is cast (<see cref="Resisted"/>).
    /// </param>
    public static LifeThroughChoice Choose(in DrainChoice drain, uint drainTakesIfLanded, HarmLevel? harm, double harmChance, uint targetHealth, double resist = 1.0)
    {
        if (resist <= 0)
        {
            return new(LifeThroughSpell.None, 0, 0, Resisted);
        }

        var drainTakes = drain.Index >= 0 ? drain.Chance * drainTakesIfLanded : 0.0;
        var harmTakes = harm is { } level ? harmChance * Math.Min(level.MeanHarm * resist, targetHealth) : 0.0;
        if (drain.Index < 0)
        {
            return harm is null ? new(LifeThroughSpell.None, 0, 0, Neither) : new(LifeThroughSpell.Harm, 0, harmTakes, HarmAlone);
        }

        if (harm is null)
        {
            return new(LifeThroughSpell.Drain, drainTakes, 0, DrainAlone);
        }

        return drainTakes >= harmTakes ? new(LifeThroughSpell.Drain, drainTakes, harmTakes, DrainTakesMore) : new(LifeThroughSpell.Harm, drainTakes, harmTakes, HarmTakesMore);
    }
}
