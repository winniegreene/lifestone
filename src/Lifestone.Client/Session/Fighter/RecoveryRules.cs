using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// What the recovery rule decides at one look at the fighter's health (M4 3.1 s3, <see cref="RecoveryRules.Decide(in RecoveryInput, RecoveryOptions, out double)"/>).
/// </summary>
public enum RecoveryDecision
{
    /// <summary>Nothing to do: the health holds (the recovery is off, the vital is full, or the share is at or over the share the recovery stops at, or nothing can help and the share is over the start trigger).</summary>
    None,

    /// <summary>Use a Handy Healing Kit on the player (<c>UseWithTarget</c>): its estimated chance is at least <see cref="RecoveryOptions.Chance"/> and it has uses.</summary>
    Kit,

    /// <summary>Drink a Potion of Healing (25 points, no roll): the kit's chance is low or the kit is out, and the missing health is at least <see cref="RecoveryRules.PotionMinMissing"/>.</summary>
    Potion,

    /// <summary>
    /// Use the kit as the last resort (M4 3.1 fix, review R1): its estimated chance is under <see cref="RecoveryOptions.Chance"/> but at least <see cref="RecoveryOptions.FloorChance"/>, no potion can be drunk, and the health is under the start
    /// trigger: a kit that heals often enough beats a 300 s regeneration wait that ends in a failure (capture C1: regeneration gave about one point of share in 94 s).
    /// </summary>
    KitLastResort,

    /// <summary>Neither helps and the share is under the trigger but at or over the retreat share: wait for regeneration (up to <see cref="RecoveryRules.RegenSeconds"/>).</summary>
    Wait,

    /// <summary>Nothing can heal and the share is under the retreat share, or the regeneration wait ran out: the recovery fails loudly (the run returns to Holtburg as every failure does).</summary>
    Fail,
}

/// <summary>
/// What the rule reads (plain numbers, so the rule is pure and allocation-free per decision; the hunt's driver fills it from the read model).
/// </summary>
/// <param name="Health">The current health.</param>
/// <param name="MaxHealth">The maximum health (0 makes the decision <see cref="RecoveryDecision.None"/>).</param>
/// <param name="Active">True while a recovery is under way: it then stops at <see cref="RecoveryOptions.Until"/> instead of starting at <see cref="RecoveryOptions.Below"/>.</param>
/// <param name="KitUses">The uses left of the held kit with uses (0: no kit, or the kit is refused: Healing untrained, 0x04FC).</param>
/// <param name="Potions">The Potions of Healing held (0: none, or drinking is refused).</param>
/// <param name="LostRolls">Failed kit rolls in a row at the current health (the driver clears it when the health changes or a roll wins): at <see cref="RecoveryRules.MaxLostRolls"/> the kit is set aside.</param>
/// <param name="WaitedSeconds">The regeneration seconds already waited in this recovery.</param>
/// <param name="HealingSkill">The Healing skill's current value.</param>
/// <param name="Specialised">True when Healing is specialised (the formula's 1.5 against 1.1).</param>
/// <param name="CombatMode">True in a combat mode (the difficulty's 1.1 against 1.0).</param>
/// <param name="KitBoost">The kit's BoostValue (the Handy Healing Kit: 50).</param>
public readonly record struct RecoveryInput(
    uint Health,
    uint MaxHealth,
    bool Active,
    int KitUses,
    int Potions,
    int LostRolls,
    double WaitedSeconds,
    int HealingSkill,
    bool Specialised,
    bool CombatMode,
    int KitBoost = RecoveryRules.HandyKitBoost);

/// <summary>
/// ACE's chance that a Healer's use heals (M4 3.1 s3; <c>W/Healer.cs</c> <c>DoSkillCheck</c> 240-257 and <c>W/SkillCheck.cs</c> 7-12 at ACE 95ef426d, AGPL-3.0; the spec's section 5.7): <c>1 / (1 + e^(-0.03 (eff - diff)))</c> with
/// <c>eff = round((Healing + BoostValue) x 1.5 specialised else 1.1)</c> and <c>diff = round(missing x 2 x 1.1 in a combat mode, x 1.0 otherwise)</c>. Pinned against cache/m4/3-1/capture.md C1: eff 264, 145 missing in combat
/// gives 0.16 (5 of 6 uses failed), 72 missing gives 0.96 (every use won). Retail's own formula is not read (the retail heal handlers have no address in the repository); the stand-in uses the same arithmetic.
/// </summary>
public static class HealChance
{
    /// <summary>The slope of the logistic (ACE <c>SkillCheck.GetSkillChance</c> factor).</summary>
    public const double Slope = 0.03;

    /// <summary>ACE's effective skill (<c>Healer.DoSkillCheck</c> 250): the Healing plus the kit's BoostValue, times 1.5 for a specialised skill and 1.1 otherwise (single-precision factors, banker's rounding, as ACE).</summary>
    public static int Effective(int healingSkill, int boostValue, bool specialised) =>
        (int)Math.Round((healingSkill + boostValue) * (specialised ? 1.5f : 1.1f));

    /// <summary>ACE's difficulty (<c>Healer.DoSkillCheck</c> 251): twice the missing vital, times 1.1 in a combat mode and 1.0 out of one.</summary>
    public static int Difficulty(int missingHealth, bool inCombatMode) =>
        (int)Math.Round(missingHealth * 2 * (inCombatMode ? 1.1f : 1.0f));

    /// <summary>The logistic: 1 / (1 + e^(-0.03 (effective - difficulty))).</summary>
    public static double Chance(int effective, int difficulty) => 1.0 / (1.0 + Math.Exp(-Slope * (effective - difficulty)));

    /// <summary>The chance that a kit use heals the player now.</summary>
    /// <param name="healingSkill">The Healing skill's current value.</param>
    /// <param name="boostValue">The kit's BoostValue (the Handy Healing Kit: 50).</param>
    /// <param name="specialised">True when Healing is specialised.</param>
    /// <param name="missingHealth">The health missing (maximum less current).</param>
    /// <param name="inCombatMode">True in a combat mode.</param>
    public static double Estimate(int healingSkill, int boostValue, bool specialised, int missingHealth, bool inCombatMode) =>
        Chance(Effective(healingSkill, boostValue, specialised), Difficulty(missingHealth, inCombatMode));
}

/// <summary>Whether the Healing skill's value could be read from the read model (<see cref="RecoveryRules.ReadHealingSkill"/>).</summary>
public enum HealingSkillState
{
    /// <summary>The current value was computed from the player's qualities.</summary>
    Read,

    /// <summary>The skill is below trained: a kit use ends UseDone 0x04FC (capture C4), so the kit is never used.</summary>
    Untrained,

    /// <summary>The read model has no Healing entry or lacks an attribute of its formula: <c>TODO(m4-heal-skill-read)</c>, the driver falls back to <see cref="RecoveryRules.FallbackHealingSkill"/> loudly.</summary>
    Unknown,
}

/// <summary>
/// The hunt's recovery rule (M4 3.1 s3, spec DoD 9 and 10, section 5.7; closes <c>TODO(m4-hunt-recovery)</c>): pure, allocation-free per decision, from the health, the kit's uses, the potions held, the estimated chance
/// (<see cref="HealChance"/>) and the options (<see cref="RecoveryOptions"/>).
/// </summary>
/// <remarks>
/// <para><b>The order.</b> A recovery starts at a health share under <see cref="RecoveryOptions.Below"/> and, once started (<see cref="RecoveryInput.Active"/>), goes on to <see cref="RecoveryOptions.Until"/>. At each look: the
/// <b>kit</b> while it has uses, fewer than <see cref="MaxLostRolls"/> rolls were lost in a row at this health, and its estimated chance is at least <see cref="RecoveryOptions.Chance"/> (capture C1: a kit under about 45%
/// health mostly burns uses for nothing); else a <b>potion</b> (25 points, no roll) while the missing health is at least <see cref="PotionMinMissing"/> (capture C3: a potion drunk at a full vital is spent for nothing); else,
/// with the share at or over the start trigger, <b>nothing</b>; else (M4 3.1 fix, review R1) the kit as the <b>last resort</b> while it has uses, fewer than <see cref="MaxLostRolls"/> rolls were lost and its chance is at least
/// <see cref="RecoveryOptions.FloorChance"/> (<see cref="KitFloorChance"/>, <c>--heal-floor-chance</c>); under the retreat share <b>fail</b>; else <b>wait</b> for regeneration up to <see cref="RegenSeconds"/> (1.2's wait), then fail. A lost roll does not change the
/// health, so the chance is the same at the next look: two lost rolls in a row set the kit aside for that health (the driver clears the count when the health moves) and a potion follows.</para>
/// <para><b>Not built:</b> a heal during a fight (<c>TODO(m4-hunt-infight-heal)</c>): the retreat at <see cref="RetreatShare"/> stays the in-fight rule.</para>
/// </remarks>
public static class RecoveryRules
{
    /// <summary>The Handy Healing Kit's BoostValue (PropertyInt 90; the world database, cache/m4/3-1/facts.md).</summary>
    public const int HandyKitBoost = 50;

    /// <summary>The health a Potion of Healing restores (BoostValue; capture C3: exactly 25, no roll).</summary>
    public const int PotionPoints = 25;

    /// <summary>The least missing health at which a potion is drunk: a potion at a full vital is spent for nothing (capture C3).</summary>
    public const int PotionMinMissing = 15;

    /// <summary>Failed kit rolls in a row (at one health) after which the kit is set aside and a potion is used.</summary>
    public const int MaxLostRolls = 2;

    /// <summary>
    /// The default floor of the last-resort kit (<c>--heal-floor-chance</c>; review R1): with no potion to drink, a kit whose estimated chance is at least this is used before the regeneration wait. 0.3 is about the chance at 57 of 188 health
    /// (30%, 0.33) for the Holtburg Hunter in combat mode; under it more than two uses in three burn for nothing.
    /// </summary>
    public const double KitFloorChance = 0.3;

    /// <summary>The longest regeneration wait of one recovery, in seconds (1.2's wait, live round 2: regeneration alone reached 77% in 300 s).</summary>
    public const double RegenSeconds = 300;

    /// <summary>The health share under which a fight is abandoned (the hunt's in-fight rule); under it, with nothing to heal with, the recovery fails at once instead of waiting.</summary>
    public const double RetreatShare = 0.3;

    /// <summary>The Healing skill (ACE skill 21, the SkillTable's: <see cref="FighterTemplate.Healing"/>).</summary>
    public const uint HealingSkillId = FighterTemplate.Healing;

    /// <summary>
    /// The Healing value the chance falls back to when the read model's cannot be read (<c>TODO(m4-heal-skill-read)</c>): 190 is the Holtburg Hunter's number in the capture (effective 264 with the Handy Healing Kit: (190 + 50) x 1.1);
    /// it is also the stand-in's placeholder (<c>StandInWorld.PlaceholderHealingSkill</c>).
    /// </summary>
    public const int FallbackHealingSkill = 190;

    /// <summary>Healing's formula (the SkillTable's, pinned by <c>RecoveryRulesDatTests</c>): (Focus + Coordination) / 3.</summary>
    private static readonly SkillTableFormula HealingFormula = new(0, 1, 1, 3, FighterTemplate.Focus, FighterTemplate.Coordination);

    /// <summary>
    /// The Healing skill's current value from the read model (<see cref="SkillRules.TryCurrent"/>: attributes with their enchantments, the vitae and the skill enchantments), and whether it is specialised. May allocate when the
    /// enchantment registry is not empty, so a driver reads it once in a while, not on every frame.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="value">The current value (0 unless <see cref="HealingSkillState.Read"/>).</param>
    /// <param name="specialised">True when Healing is specialised.</param>
    public static HealingSkillState ReadHealingSkill(PlayerQualities q, out int value, out bool specialised)
    {
        ArgumentNullException.ThrowIfNull(q);
        value = 0;
        specialised = false;
        if (!q.IsKnown || !q.TryGetSkill(HealingSkillId, out var skill))
        {
            return HealingSkillState.Unknown;
        }

        if (skill.AdvancementClass < SkillRules.TrainedClass)
        {
            return HealingSkillState.Untrained;
        }

        if (!SkillRules.TryCurrent(q, HealingSkillId, in HealingFormula, SkillRules.TrainedClass, out var current))
        {
            return HealingSkillState.Unknown;
        }

        value = (int)Math.Min(current, int.MaxValue);
        specialised = skill.AdvancementClass >= SkillRules.SpecializedClass;
        return HealingSkillState.Read;
    }

    /// <summary>The decision at one look (see the class remarks).</summary>
    /// <param name="input">The health, the items, the skill and the recovery's own state.</param>
    /// <param name="options">The recovery options.</param>
    public static RecoveryDecision Decide(in RecoveryInput input, RecoveryOptions options) => Decide(in input, options, out _);

    /// <summary>The decision at one look (see the class remarks), with the kit's estimated chance at the current health.</summary>
    /// <param name="input">The health, the items, the skill and the recovery's own state.</param>
    /// <param name="options">The recovery options.</param>
    /// <param name="chance">The kit's estimated chance at the current health; NaN when the kit was not weighed (no uses left, or no recovery needed).</param>
    public static RecoveryDecision Decide(in RecoveryInput input, RecoveryOptions options, out double chance)
    {
        ArgumentNullException.ThrowIfNull(options);
        chance = double.NaN;
        if (!options.Enabled || input.MaxHealth == 0 || input.Health >= input.MaxHealth)
        {
            return RecoveryDecision.None;
        }

        var share = (double)input.Health / input.MaxHealth;
        if (share >= (input.Active ? options.Until : options.Below))
        {
            return RecoveryDecision.None;
        }

        var missing = (int)Math.Min(input.MaxHealth - input.Health, int.MaxValue);
        if (input.KitUses > 0)
        {
            chance = HealChance.Estimate(input.HealingSkill, input.KitBoost, input.Specialised, missing, input.CombatMode);
            if (input.LostRolls < MaxLostRolls && chance >= options.Chance)
            {
                return RecoveryDecision.Kit;
            }
        }

        if (input.Potions > 0 && missing >= PotionMinMissing)
        {
            return RecoveryDecision.Potion;
        }

        if (share >= options.Below)
        {
            return RecoveryDecision.None; // a started recovery's tail: nothing helps and the health already holds the start trigger
        }

        // Review R1: no potion to drink and the kit under the wanted chance is still better than waiting: regeneration is nearly nothing (capture C1) and the wait ends in a failure with the kit unused. The lost-roll rule holds (a kit set
        // aside at this health waits for the health to move). Under the retreat share too: a kit that can heal is something that can heal.
        if (input.KitUses > 0 && input.LostRolls < MaxLostRolls && chance >= options.FloorChance)
        {
            return RecoveryDecision.KitLastResort;
        }

        if (share < RetreatShare)
        {
            return RecoveryDecision.Fail;
        }

        return input.WaitedSeconds >= RegenSeconds ? RecoveryDecision.Fail : RecoveryDecision.Wait;
    }
}
