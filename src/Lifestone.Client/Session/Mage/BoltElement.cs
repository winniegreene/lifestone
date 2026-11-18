using System.Globalization;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>One element's best bolt for a target (<see cref="BoltElement.Lines"/>).</summary>
/// <param name="Element">The element (a key of <see cref="SpellPlan.BoltLineNames"/>).</param>
/// <param name="LineName">Its bolt line's name.</param>
/// <param name="Index">The level's index on the line, 0 for I.</param>
/// <param name="SpellId">The spell.</param>
/// <param name="SpellName">Its name in the SpellTable.</param>
/// <param name="Resist">The target's resistance to the element (<see cref="CreatureResists.Of"/>).</param>
/// <param name="Expected">The mean plain hit on the target: ACE's mean roll plus the skill bonus, times the resistance and the caster's factor (<see cref="BoltElement.MeanHit"/>).</param>
/// <param name="Caster">The factor of the caster in her hands on the element (<see cref="WarFactor"/>; 1 with a plain caster).</param>
public sealed record BoltPick(string Element, string LineName, int Index, uint SpellId, string SpellName, double Resist, double Expected, double Caster = 1.0)
{
    /// <summary>The pick for a log line.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{SpellName} ({SpellId}, {Element} x{Resist:0.###}{(Caster != 1.0 ? string.Create(CultureInfo.InvariantCulture, $", caster x{Caster:0.###}") : string.Empty)}: {Expected:0.#} a plain hit)");
}

/// <summary>
/// M6 14 the element line (the owner, 2026-11-16; closes <c>TODO(m6-fight-element-line)</c>): which bolt line hits a creature hardest from her book. Pure. ACE's war projectile (<c>SpellProjectile.CalculateDamage</c>, SpellProjectile.cs:490-560
/// @95ef426d) rolls uniformly from the spell's <c>MinDamage</c> to <c>MaxDamage</c>, adds <c>MinDamage x (War Magic - Power) / 1000</c> when her War Magic is over the spell's power, and multiplies by the target's resistance to the
/// spell's element (<c>Creature.GetResistanceMod</c>, Creature_Properties.cs:185-205: <c>ResistX ?? 1.0</c>). Every element's bolt line has the same damage level by level (<see cref="WarSpellData"/>, M6 1 f's read), so the line that hits a
/// target hardest is the one whose highest castable level, times the target's resistance to its element, is the largest. A level is castable when the book holds it, the fight has not dropped it and its cast chance on her current War Magic
/// reaches the fight's chance (<see cref="GearCasts.Chance"/>), under the fight's cap.
/// <para>Checked against <c>tools/fight-sim.ps1 matchup</c> (ACE's own code on Qwinnie at level 78, 2026-11-16): the order of the lines by mean plain hit is the simulator's on the Nubilous Golem (Lightning Bolt VI 143.7, Frost Bolt VI 122.5,
/// Acid Stream VI 115.3), the Cursed Wisp (Whirling Blade VI 143.8 over Force Bolt VI 141.2; frost 0), the Magma Golem, the Ember and the Gout (Frost Bolt VI first on all three; fire 0 on all three).</para>
/// <para>The caster in her hands (M6 16 Q4, closes <c>TODO(m6-bolt-element-caster-mod)</c>): each line's hit is times the caster's factor on its element (<see cref="WarFactor"/>: its elemental bonus, a quest weapon's resistance
/// cleaving, the mastery bonus), given by the caller (<see cref="CastElement.BestOn"/> reads the caster she holds, <see cref="CasterWar.Held"/>); none given is x1 on every line, as a plain caster. Checked with
/// <c>tools/fight-sim.ps1 matchup --wield</c> (2026-11-18, level 82, War Magic 394): the Atlan wand makes her Flame Bolt VI x2.25 of the same bolt through a plain Orb, so the Stinging Chittick (fire 0.65, acid and pierce 1)
/// takes the fire line hardest with the wand in hand and the acid line with the Orb; the Reaper (fire 0.38, cold and slash 1) takes the frost line hardest with either.</para>
/// <para>Not read: the target's own vulnerabilities and protections (enchantments on it), and a cleaving under a larger vulnerability (<c>TODO(m6-caster-cleave-under-vuln)</c>).</para>
/// </summary>
public static class BoltElement
{
    /// <summary>ACE's mean plain hit of a war projectile before the resistance: the mean of the uniform roll plus the skill bonus (SpellProjectile.cs:527-537 @95ef426d).</summary>
    /// <param name="row">The spell's projectile row.</param>
    /// <param name="skill">Her current War Magic.</param>
    /// <param name="power">The spell's power.</param>
    public static double MeanHit(WarSpellRow row, uint skill, uint power)
    {
        ArgumentNullException.ThrowIfNull(row);
        var bonus = skill > power ? row.BaseIntensity * ((skill - power) / 1000.0) : 0.0;
        return row.BaseIntensity + (row.Variance / 2.0) + bonus;
    }

    /// <summary>
    /// The highest castable level of each element's bolt line on <paramref name="wcid"/>, the hardest first (the first is <see cref="Best"/>'s): the element of <paramref name="current"/> wins a tie, then fire (the ladder's line), then the
    /// order of <see cref="WarSpellData.BoltLineIds"/>. An element with no castable level is left out.
    /// </summary>
    /// <param name="wcid">The target's weenie class.</param>
    /// <param name="spell">The SpellTable's spell of an id, or null.</param>
    /// <param name="usable">True when the fight may cast the spell (the book holds it and the fight has not dropped it).</param>
    /// <param name="skill">Her current War Magic.</param>
    /// <param name="chance">The fight's cast chance.</param>
    /// <param name="cap">The highest level the fight casts, 1 to 7.</param>
    /// <param name="current">The element the fight casts now, or null.</param>
    /// <param name="caster">The caster in her hands (<see cref="CasterWar"/>), or null for a plain caster: x1 on every line.</param>
    public static IReadOnlyList<BoltPick> Lines(uint wcid, Func<uint, SpellInfo?> spell, Func<uint, bool> usable, uint skill, double chance, int cap, string? current = null, CasterWar? caster = null)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(usable);
        var picks = new List<(BoltPick Pick, int Order)>();
        var order = 0;
        foreach (var (element, ids) in WarSpellData.BoltLineIds)
        {
            order++;
            var top = Math.Min(Math.Clamp(cap, 1, FightScenario.Levels), ids.Count) - 1;
            for (var i = top; i >= 0; i--)
            {
                if (spell(ids[i]) is not { } info || !usable(ids[i]) || GearCasts.Chance(skill, info.Power) < chance || !WarSpellData.TryGet(ids[i], out var row))
                {
                    continue;
                }

                var resist = Bestiary.ResistOf(wcid, element);
                var tie = string.Equals(element, current, StringComparison.Ordinal) ? 0 : element == "fire" ? 1 : 2 + order;
                var factor = caster?.On(element) ?? 1.0;
                picks.Add((new BoltPick(element, SpellPlan.BoltLineNames[element], i, ids[i], info.Name, resist, MeanHit(row, skill, info.Power) * resist * factor, factor), tie));
                break;
            }
        }

        return [.. picks.OrderByDescending(p => Math.Round(p.Pick.Expected, 6)).ThenBy(p => p.Order).Select(p => p.Pick)];
    }

    /// <summary>The line that hits <paramref name="wcid"/> hardest (see <see cref="Lines"/>), or null when no line has a castable level. Its <see cref="BoltPick.Expected"/> is 0 when every castable line is resisted whole.</summary>
    /// <param name="wcid">The target's weenie class.</param>
    /// <param name="spell">The SpellTable's spell of an id, or null.</param>
    /// <param name="usable">True when the fight may cast the spell.</param>
    /// <param name="skill">Her current War Magic.</param>
    /// <param name="chance">The fight's cast chance.</param>
    /// <param name="cap">The highest level the fight casts, 1 to 7.</param>
    /// <param name="current">The element the fight casts now, or null.</param>
    /// <param name="caster">The caster in her hands, or null for a plain caster.</param>
    public static BoltPick? Best(uint wcid, Func<uint, SpellInfo?> spell, Func<uint, bool> usable, uint skill, double chance, int cap, string? current = null, CasterWar? caster = null) =>
        Lines(wcid, spell, usable, skill, chance, cap, current, caster) is { Count: > 0 } lines ? lines[0] : null;

    /// <summary>The element whose bolt line holds the spell <paramref name="id"/> (<see cref="WarSpellData.BoltLineIds"/>), or null.</summary>
    /// <param name="id">The spell id.</param>
    public static string? ElementOfSpell(uint id) => WarSpellData.BoltLineIds.FirstOrDefault(kv => kv.Value.Contains(id)).Key;

    /// <summary>The element whose bolt line <paramref name="lineName"/> is (a value of <see cref="SpellPlan.BoltLineNames"/>), or null.</summary>
    /// <param name="lineName">The line's name.</param>
    public static string? ElementOf(string lineName) =>
        SpellPlan.BoltLineNames.FirstOrDefault(kv => string.Equals(kv.Value, lineName, StringComparison.Ordinal)).Key;
}
