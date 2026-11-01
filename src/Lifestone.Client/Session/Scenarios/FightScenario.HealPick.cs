using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F21 (D92, the owner: a drain is cast only when it would help more than Heal Self; otherwise Heal Self). The fight's one comparison, <see cref="HealPick"/>, read at the three places a heal is chosen: the emergency under its line,
/// D88's drain floor and the picker's drain as an attack. Everything is read now: the mage's missing health, mana and Life Magic, the target's known health (F20's model), the measured cast seconds (F19) and the dance's seconds a mana point (F10).
/// </summary>
public sealed partial class FightScenario
{
    private readonly HealCandidate[] _healCandidates = new HealCandidate[8];

    /// <summary>The level (1 to 7) of the Heal Self the fight casts, or 1 when it has none.</summary>
    private int HealSelfLevel() => FightHealPick.HealSelfLevel(_drain.HealId);

    /// <summary>
    /// Heal Self as a candidate: the heal the spell rows give at its level (ACE rolls the boost between its rows, 55 to 120 at level VI, mean 87.5; <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 481-491 @95ef426d) or the
    /// measured mean of its casts, cut to the missing health; its cast chance and no resist (a self cast never resists); its mana and seconds. False when it cannot be cast now (not known, not paid for, the mana short).
    /// </summary>
    private bool HealSelfCandidate(uint missing, uint mana, uint life, out HealCandidate self) => FightHealPick.SelfCandidate(_drain, _heals, _emergencyHealSeconds, missing, mana, life, out self);

    /// <summary>
    /// One drain level on a target as a candidate: ACE's formula for the target's known health (<see cref="BuffStatMods.Transfer"/>), scaled by the spell's measured ratio and cut to the missing health; the damage it does to the target is
    /// the source change scaled the same; the chance is the cast chance times the chance the target does not resist; its mana and its seconds (the measured seconds of the level, else the seed). <paramref name="formula"/> is the formula's heal before the ratio.
    /// </summary>
    private HealCandidate DrainCandidate(in DrainLevel level, in DrainSituation s, uint guid, out double formula) => FightHealPick.DrainCandidate(in level, in s, guid, _heals, out formula);

    /// <summary>Logs the comparison for one place a heal is chosen.</summary>
    private void LogHealPick(string place, int choice, in HealCandidate self, double selfRate, ReadOnlySpan<HealCandidate> drains, int bestDrain, double bestRate) =>
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{place}: {HealPick.Line(choice, in self, selfRate, drains, bestDrain, bestRate)}"));

    /// <summary>
    /// The drain floor's and the picker's check (one drain level on one target against Heal Self): true when the drain scores more than Heal Self, so it may be cast. Logs the comparison; a tie, or a drain under Heal Self, is false and
    /// the caller casts Heal Self (the floor) or no drain (the picker).
    /// </summary>
    private bool DrainBeatsHealSelf(string place, in DrainLevel level, in DrainSituation s, uint guid, out HealCandidate self, out bool canSelf)
    {
        var missing = s.HealthMax > s.Health ? s.HealthMax - s.Health : 0u;
        canSelf = HealSelfCandidate(missing, s.Mana, s.LifeMagic, out self);
        _healCandidates[0] = DrainCandidate(in level, in s, guid, out _);
        var choice = HealPick.Choose(in self, canSelf, _healCandidates.AsSpan(0, 1), Picker.Rates.SecondsPerMana, _options.HealDamageWeight, out var selfRate, out var best, out var bestRate);
        LogHealPick(place, choice, in self, selfRate, _healCandidates.AsSpan(0, 1), best, bestRate);
        return choice == 0;
    }
}

/// <summary>
/// M5 7 F21's candidates of the one heal pick (<see cref="HealPick"/>), as static functions so the fight and the autopilot's vitals guard (M5 7 F23, <c>AutopilotVitalsGuard</c>) build them the same way: Heal Self by its level's
/// boost or the measured mean, cut to the missing health, and each drain level by ACE's transfer formula at the target's known health, scaled by the spell's measured ratio. Allocation-free.
/// </summary>
internal static class FightHealPick
{
    /// <summary>The level (1 to 7) of the Heal Self <paramref name="healId"/>, or 1 when it is none of them.</summary>
    /// <param name="healId">The Heal Self spell id.</param>
    public static int HealSelfLevel(uint healId)
    {
        var level = 1;
        for (var i = 0; i < VitalsMeans.HealSelf.Count; i++)
        {
            level = VitalsMeans.HealSelf[i] == healId ? i + 1 : level;
        }

        return level;
    }

    /// <summary>
    /// Heal Self as a candidate: the heal the spell rows give at its level (ACE rolls the boost between its rows, 55 to 120 at level VI, mean 87.5; <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 481-491 @95ef426d) or the
    /// measured mean of its casts, cut to the missing health; its cast chance and no resist (a self cast never resists); its mana and seconds (<paramref name="measuredSeconds"/>, else the seed of its level). False when it cannot be cast now.
    /// </summary>
    /// <param name="drain">The drain rule's levels (its Heal Self level).</param>
    /// <param name="heals">The measured heals.</param>
    /// <param name="measuredSeconds">Heal Self's measured cast seconds, or NaN.</param>
    /// <param name="missing">The health missing.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="life">The Life Magic now.</param>
    /// <param name="self">The candidate.</param>
    public static bool SelfCandidate(FightDrain drain, MeasuredHeals heals, double measuredSeconds, uint missing, uint mana, uint life, out HealCandidate self)
    {
        self = new HealCandidate(drain.HealName, 0, 0, 0, 0, 0, 0);
        if (!drain.HasHeal || mana < drain.HealCeiling)
        {
            return false;
        }

        var level = HealSelfLevel(drain.HealId);
        var heal = Math.Min(missing, heals.Expected(drain.HealName, null, MeasuredHeals.SelfSeed(level)));
        var seconds = double.IsNaN(measuredSeconds) ? MeasuredHeals.DrainSeed(level) : measuredSeconds;
        self = new HealCandidate(drain.HealName, 0, heal, 0, GearCasts.Chance(life, drain.HealPower), drain.HealCeiling, seconds);
        return true;
    }

    /// <summary>
    /// One drain level on a target as a candidate: ACE's formula for the target's known health (<see cref="BuffStatMods.Transfer"/>), scaled by the spell's measured ratio and cut to the missing health; the damage it does to the target is
    /// the source change scaled the same; the chance is the cast chance times the chance the target does not resist; its mana and its seconds (the measured seconds of the level, else the seed). <paramref name="formula"/> is the formula's heal before the ratio.
    /// </summary>
    /// <param name="level">The drain level.</param>
    /// <param name="s">The situation (the mage's and the target's).</param>
    /// <param name="guid">The target.</param>
    /// <param name="heals">The measured heals.</param>
    /// <param name="formula">The formula's heal before the ratio.</param>
    public static HealCandidate DrainCandidate(in DrainLevel level, in DrainSituation s, uint guid, MeasuredHeals heals, out double formula)
    {
        var missing = s.HealthMax > s.Health ? s.HealthMax - s.Health : 0u;
        var row = level.Row;
        BuffStatMods.Transfer(in row, s.TargetHealth, missing, out var taken, out var given);
        formula = given;
        var heal = Math.Min(missing, heals.ExpectedFromRatio(level.Info.Name, given));
        var scale = given > 0 ? heal / given : 0;
        var chance = GearCasts.Chance(s.LifeMagic, level.Info.Power) * DrainPicker.PassChance(s.LifeMagic, s.TargetMagicDefence);
        var seconds = heals.Seconds(level.Info.Name, MeasuredHeals.DrainSeed(FightScenario.DrainLevelOf(level.Info.Id)));
        return new HealCandidate(level.Info.Name, guid, heal, taken * scale, chance, GearCasts.ManaCeiling(level.Info, 0), seconds);
    }
}
