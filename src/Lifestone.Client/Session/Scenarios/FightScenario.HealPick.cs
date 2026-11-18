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
    /// <summary>
    /// M6 7 AA: the share of the blows falling due that are taken to spare the mage while a heal winds up, in <see cref="SaveChance"/> (Lifestone's own, not a retail or ACE figure: nothing here measures a creature's misses; it only orders a slow cast under a quick one).
    /// </summary>
    public const double BlowSpares = 0.5;

    /// <summary>The blows that fall due within <paramref name="seconds"/> at the recent hits' interval.</summary>
    /// <param name="read">The recent hits (they go on).</param>
    /// <param name="seconds">The cast's seconds.</param>
    public static int BlowsDuring(in IncomingRead read, double seconds)
    {
        var due = Math.Max(0, read.IntervalSeconds - read.SinceLastHit);
        return seconds < due ? 0 : 1 + (int)Math.Floor((seconds - due) / read.IntervalSeconds);
    }

    /// <summary>
    /// M6 7 AA: with the blows outrunning her, the chance one cast saves the mage: 0 when its heal does not lift her over the biggest hit, else the chance it lands (a target's resist is in a drain's) times <see cref="BlowSpares"/> for each blow due before it lands past those
    /// her health takes at the biggest hit.
    /// </summary>
    /// <param name="c">The candidate.</param>
    /// <param name="read">The recent hits.</param>
    /// <param name="health">The health left.</param>
    public static double SaveChance(in HealCandidate c, in IncomingRead read, double health)
    {
        var taken = (int)Math.Ceiling(health / Math.Max(1, read.BiggestHit)) - 1; // the blows she lives through
        return health + c.Heal <= read.BiggestHit ? 0 : Math.Clamp(c.Chance, 0, 1) * Math.Pow(BlowSpares, Math.Max(0, BlowsDuring(in read, c.CastSeconds) - taken));
    }

    /// <summary>
    /// M6 7 AA (r3c: at 5 health of 44, biggest hit 9, a hit every 0.8 s, Drain Health Other I on a full tusker scored 14.5 health a second to Heal Self I's 5.9 and she died in its wind-up): <see cref="HealPick.PerSecond"/> is what a heal is worth over a fight; when the mage is not expected to live through the drain's cast
    /// (<see cref="IncomingRead.Survives"/>; the next hit killing is its sharpest case) that is the wrong question, as a big heal that is resisted or lands too late is worth nothing. True when Heal Self saves her at all and at least as surely as the drain (a tie goes to Heal Self, as
    /// in <see cref="HealPick.Choose"/>). The caller asks this only then: at any other health the drain's value stands, and no level enters here: a den drain is scored on its own chance, seconds and heal.
    /// </summary>
    /// <param name="self">Heal Self.</param>
    /// <param name="drain">The drain the pick chose.</param>
    /// <param name="read">The recent hits.</param>
    /// <param name="health">The health left.</param>
    /// <param name="selfSave">Heal Self's chance.</param>
    /// <param name="drainSave">The drain's chance.</param>
    public static bool HealSelfSavesSurer(in HealCandidate self, in HealCandidate drain, in IncomingRead read, double health, out double selfSave, out double drainSave)
    {
        selfSave = SaveChance(in self, in read, health);
        drainSave = SaveChance(in drain, in read, health);
        return selfSave > 0 && selfSave >= drainSave;
    }

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
        var seconds = double.IsNaN(measuredSeconds) ? heals.Seconds(drain.HealName, MeasuredHeals.SelfSecondsSeedOf(level)) : measuredSeconds; // M6 14 tactics: its own measured seconds, else ACE's for its level (not the drain's seed)
        self = new HealCandidate(drain.HealName, 0, heal, 0, GearCasts.Chance(life, drain.HealPower), drain.HealCeiling, seconds);
        return true;
    }

    /// <summary>
    /// One drain level on a target as a candidate: ACE's formula for the target's known health (<see cref="BuffStatMods.Transfer"/>) at its kind's drain resistance
    /// (<see cref="DrainSituation.TargetDrainResist"/>: 0 expects nothing, so Heal Self wins), scaled by the spell's measured ratio and cut to the missing health; the damage it does to the target is
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
        BuffStatMods.Transfer(in row, s.TargetHealth, missing, out var taken, out var given, (float)s.TargetDrainResist); // M6 14 drain resistance: the kind's share, before the cap
        formula = given;
        var heal = Math.Min(missing, heals.ExpectedFromRatio(level.Info.Name, given));
        var scale = given > 0 ? heal / given : 0;
        var chance = GearCasts.Chance(s.LifeMagic, level.Info.Power) * DrainPicker.PassChance(s.LifeMagic, s.TargetMagicDefence);
        var seconds = heals.Seconds(level.Info.Name, MeasuredHeals.DrainSeed(FightScenario.DrainLevelOf(level.Info.Id)));
        return new HealCandidate(level.Info.Name, guid, heal, taken * scale, chance, GearCasts.ManaCeiling(level.Info, 0), seconds);
    }
}
