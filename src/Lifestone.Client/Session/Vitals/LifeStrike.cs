using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>The travel fight-back's life cast at an attacker its bolts cannot reach (<see cref="LifeStrike.Choose"/>).</summary>
/// <param name="Spell">The Harm Other level.</param>
/// <param name="LandChance">The chance one cast lands: her cast chance times the chance the target does not resist it.</param>
/// <param name="Takes">What one cast is expected to take: the land chance times the level's mean, times the target's health-drain resistance, cut to its health.</param>
/// <param name="Why">The choice in words with its numbers.</param>
public sealed record LifeStrikePick(SpellInfo Spell, double LandChance, double Takes, string Why);

/// <summary>
/// M6 14 the Zefir (the owner, 2026-11-16, after live ladder42 20:17:14-20:18:30 UTC in the Crater Pathway: 25 Ilservian's Flames at a Kilif Zefir 8.7 m away each struck the world 1.5 m out, no damage line, until the fight-back's timeout recalled
/// her: "she should have used life magic and harmed it"): the travel fight-back's answer to an attacker its bolts cannot reach, the hunting fight's <c>m6-life-through</c> rule (<see cref="LifeThroughRule"/>) on the road. Pure. Harm Other has
/// no projectile: ACE checks a targeted spell's range in two dimensions and nothing else (<c>Player.VerifySpellRange</c>, Player_Magic.cs:481-506 @95ef426d) and resolves it on its target at once, whatever stands between.
/// <para>The level: the highest Harm Other (<see cref="VitalsMeans.HarmOther"/>) the book holds, the leg has not had refused, whose cast chance on her current Life Magic reaches the fight-back's chance and whose range
/// (<see cref="FightScenario.CastRange"/>) reaches the target. Its worth: <see cref="LifeThroughRule.LandChance"/> (the cast's 0.07 curve times the resist's 0.03 curve against the creature's Magic Defense from the world database,
/// <see cref="CreatureTable"/>) times the level's mean take (<see cref="HarmOtherRows"/>) times the kind's health-drain resistance (<see cref="DrainResist"/>), cut to its health. A kind that resists health drains wholly, or no level known, is
/// no pick, named. The drain is not offered on the road (it takes a share of a target's health and gives it to her; the fight-back's job is the kill): <c>TODO(m6-travel-drain-through)</c>.</para>
/// <para>On the Kilif Zefir 2610 (health 50, Magic Defense 46, ResistHealthDrain 1; the creature table) at her Life Magic 390: Harm Other VI (33 to 63, mean 48) lands 0.999 of the time, two casts at most.</para>
/// </summary>
public static class LifeStrike
{
    /// <summary>The pick, or null with <paramref name="why"/> naming why none.</summary>
    /// <param name="table">The SpellTable.</param>
    /// <param name="known">True when the book holds the spell.</param>
    /// <param name="refused">The spells refused on this leg (never cast again on it).</param>
    /// <param name="lifeMagic">Her current Life Magic.</param>
    /// <param name="initPlusRanks">Her Life Magic's start level plus ranks (the range).</param>
    /// <param name="minChance">The fight-back's cast chance.</param>
    /// <param name="kind">
    /// The target's kind as the bestiary holds it (<see cref="KindNumbers.Of"/>). M6 16 E1e: where it holds no Magic Defense for the kind (a weenie class the bestiary does not hold) the roll is against 0, as before; the
    /// caller reads the kind through its named read and says so once a kind (a leg's <c>LegKindRead</c>), so the 0 is never taken without a word.
    /// </param>
    /// <param name="metres">Its distance.</param>
    /// <param name="health">Its health in points (the model's, else its maximum).</param>
    /// <param name="why">The choice, or why none, in words.</param>
    public static LifeStrikePick? Choose(IReadOnlyDictionary<uint, SpellInfo> table, Func<uint, bool> known, IReadOnlySet<uint> refused, uint lifeMagic, uint initPlusRanks, double minChance, in KindNumbers kind, double metres, uint health, out string why)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(refused);
        var c = CultureInfo.InvariantCulture;
        var resist = DrainResist.Of(kind.Wcid);
        if (resist <= 0)
        {
            why = LifeThroughRule.Resisted;
            return null;
        }

        var defence = (uint)Math.Max(0, kind.MagicDefence ?? 0);
        var skipped = new List<string>();
        for (var i = VitalsMeans.HarmOther.Count - 1; i >= 0; i--)
        {
            var id = VitalsMeans.HarmOther[i];
            if (!known(id) || !table.TryGetValue(id, out var info) || info.School != MagicSchool.LifeMagic || HarmOtherRows.RowOf(id) is not { } row)
            {
                continue;
            }

            var range = FightScenario.CastRange(info, initPlusRanks);
            if (refused.Contains(id))
            {
                skipped.Add($"{info.Name} was refused on this leg");
            }
            else if (GearCasts.Chance(lifeMagic, info.Power) < minChance)
            {
                skipped.Add(string.Create(c, $"{info.Name} casts at {GearCasts.Chance(lifeMagic, info.Power):0.00} on Life Magic {lifeMagic}"));
            }
            else if (metres > range)
            {
                skipped.Add(string.Create(c, $"{info.Name} reaches {range:0.#} m, the target stands {metres:0.#} m off"));
            }
            else
            {
                var land = LifeThroughRule.LandChance(lifeMagic, info.Power, defence);
                var takes = land * Math.Min(row.MeanHarm * resist, health);
                why = string.Create(c, $"{info.Name} ({info.Id}): lands {land:0.000} (Life Magic {lifeMagic} against Magic Defense {defence}), takes {Math.Abs(row.Boost)} to {row.MaxHarm} x resistance {resist:0.##}, {takes:0.#} expected of its {health} health; no projectile, so no wall stops it{(skipped.Count == 0 ? string.Empty : "; skipped: " + string.Join("; ", skipped))}");
                return new LifeStrikePick(info, land, takes, why);
            }
        }

        why = skipped.Count == 0 ? "her book holds no Harm Other" : "no Harm Other level can be cast at it: " + string.Join("; ", skipped);
        return null;
    }
}
