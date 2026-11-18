using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What the fight's mana loop does next (<see cref="ManaLoopRule.Decide"/>).</summary>
public enum ManaLoopMove : byte
{
    /// <summary>Nothing: the mana is over the line, or no cast pays: the mana order decides as before.</summary>
    None,

    /// <summary>Press Stamina to Mana.</summary>
    Press,

    /// <summary>Drain Stamina Other from the creature that holds the most, then press.</summary>
    DrainStamina,
}

/// <summary>A press by the sum (<see cref="ManaLoopRule.PressOption"/>): the level, what it nets, what it costs.</summary>
/// <param name="Level">The level in the levels given, or -1.</param>
/// <param name="Net">The net mana: the give cut to the mana missing, less the cost.</param>
/// <param name="Mana">The press's cost.</param>
/// <param name="Taken">M6 14: the stamina a landed press takes (the transfer cut to the mana she is missing, as ACE cuts it).</param>
/// <param name="Give">M6 14 (the press fills): the mana the level gives from the stamina when nothing is cut (<see cref="ManaOrder.Give"/>), the figure <c>mana_press_fill_percent</c> reads.</param>
public readonly record struct ManaLoopPress(int Level, double Net, uint Mana, uint Taken = 0, uint Give = 0)
{
    /// <summary>No press.</summary>
    public static ManaLoopPress None => new(-1, 0, 0);
}

/// <summary>What <see cref="ManaLoopRule.DecideBySum"/> reads.</summary>
public readonly record struct ManaLoopFacts
{
    /// <summary>The mana held.</summary>
    public uint Mana { get; init; }

    /// <summary>The mana's maximum.</summary>
    public uint ManaMax { get; init; }

    /// <summary>The stamina held.</summary>
    public uint Stamina { get; init; }

    /// <summary>The stamina's maximum.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>The next bolt's mana.</summary>
    public uint BoltMana { get; init; }

    /// <summary>The press now (<see cref="ManaLoopRule.PressOption"/>).</summary>
    public ManaLoopPress PressNow { get; init; }

    /// <summary>The press after the drain, read at the stamina the drain leaves and the mana it costs.</summary>
    public ManaLoopPress PressAfterDrain { get; init; }

    /// <summary>The stamina one drain gives her, cut to the room she has (0: none in reach, or no room).</summary>
    public int DrainGive { get; init; }

    /// <summary>The drain's mana.</summary>
    public uint DrainMana { get; init; }

    /// <summary>The target dies to its next bolt or bolts (<c>mana_loop_bolt_first_hits</c>).</summary>
    public bool TargetDying { get; init; }

    /// <summary>The creatures hitting her now.</summary>
    public int Attackers { get; init; }

    /// <summary>The bolts still owed since the last press (<c>mana_loop_bolts_between</c>).</summary>
    public int BoltsOwed { get; init; }

    /// <summary>M6 14: the stamina the ground's rest before a pull begins under (0: the ground has no such line): a press with ample mana held does not take her under it (<see cref="ManaLoopRule.AmpleBolts"/>).</summary>
    public uint StaminaKeep { get; init; }

    /// <summary>M6 14 (the press fills): the press before a bolt she can pay (<see cref="ManaLoopRule.FillOption"/>): the level that nets the most when nothing is cut, its net read at the mana she is missing.</summary>
    public ManaLoopPress PressFill { get; init; }

    /// <summary>M6 14 (the stamina line): the drain's pick is the bolt's own target.</summary>
    public bool DrainOnBoltTarget { get; init; }
}

/// <summary>The decision of <see cref="ManaLoopRule.DecideBySum"/>.</summary>
/// <param name="Move">What to do.</param>
/// <param name="Why">A fixed key of the reason (for the log's once-only note).</param>
/// <param name="Net">The net mana of the move (0 for none); for a drain of the stamina line, the stamina it gives.</param>
/// <param name="Level">M6 14: for a press, the level index pressed (the fill's or, under a bolt, the press by the sum's); -1 otherwise.</param>
/// <param name="StaminaLine">M6 14: a drain of the stamina line (valued in stamina; the bolts between are owed after it, as after a press).</param>
public readonly record struct ManaLoopPlan(ManaLoopMove Move, string Why, double Net, int Level = -1, bool StaminaLine = false);

/// <summary>
/// M6 7 AN: the mana loop's rule, pure. Numbers (spell rows 1676 and 1249, read by the coordinator; <see cref="LadderVitals"/> holds the lines): Stamina to Mana I takes half her stamina at 0.9, so a press nets 0.45 S - 10; Drain Stamina Other I takes 10% of the target's stamina and gives
/// twice that, cap 60, for 10 mana. A tusker with 290 gives 58, an outskirts creature with 80 gives 16 (ACE <c>HandleCastSpell_Transfer</c>, <c>WorldObject_Magic.cs</c> 697-760 @95ef426d).
/// </summary>
public static class ManaLoopRule
{
    /// <summary>
    /// M6 14 (the Cave's run of 2026-11-12: 140 presses in two and a half hours, every one with the mana over 70%, 20 of them with the stamina under 10%, and 15 of her 46 rests before a pull were for "stamina under 15%": about
    /// ten minutes lain down to win back stamina she had turned into mana she did not need; with the buff bot's Mana Conversion a bolt costs 5, so the least net a press must pay, a share of a bolt, lets every press through):
    /// the bolts' worth of mana held from which the mana is ample. With that much held, a press that would take her stamina under the ground's rest line (<see cref="ManaLoopFacts.StaminaKeep"/>) is not pressed; with less
    /// held the mana matters more than the rest and the press goes as before.
    /// </summary>
    public const uint AmpleBolts = 10;

    /// <summary>The mana a stamina drain needs: its own 10 and the press's 10 after it (spell rows 1249 and 1676).</summary>
    public const uint DrainFloor = 20;

    /// <summary>True when the stamina is under the share at which a press pays.</summary>
    /// <param name="stamina">The stamina now.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static bool ShortOfPress(uint stamina, uint staminaMax, LadderVitals rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return ManaOrder.StaminaUnder(stamina, staminaMax, rules.ManaLoopStaminaPercent);
    }

    /// <summary>The stamina one landed cast of <paramref name="drain"/> gives from a target holding <paramref name="targetStamina"/>: the share taken, the loss (negative: a gain), each cut at the cap.</summary>
    /// <param name="targetStamina">The target's stamina.</param>
    /// <param name="drain">The level.</param>
    public static int StaminaGive(uint targetStamina, in DebuffSpell drain)
    {
        var taken = Math.Round(targetStamina * drain.Proportion, MidpointRounding.AwayFromZero);
        var cap = drain.TransferCap > 0 ? drain.TransferCap : int.MaxValue;
        return (int)Math.Min(Math.Round(Math.Min(taken, cap) * (1 - drain.LossPercent), MidpointRounding.AwayFromZero), cap);
    }

    /// <summary>What one drain really gives her: <see cref="StaminaGive(uint, in DebuffSpell)"/> cut to the stamina she has room for (ACE scales the transfer down to what the caster is missing, <c>WorldObject_Magic.cs:737-746</c> @95ef426d).</summary>
    /// <param name="targetStamina">The target's stamina.</param>
    /// <param name="drain">The level.</param>
    /// <param name="room">The stamina she is missing.</param>
    public static int StaminaGive(uint targetStamina, in DebuffSpell drain, uint room) => (int)Math.Min(StaminaGive(targetStamina, in drain), Math.Min(room, int.MaxValue));

    /// <summary>
    /// M6 8 H: the level of Stamina to Mana by the sum, with the overfill counted (the transfer is cut to the mana she is missing and the press pays its whole cost anyway, <c>WorldObject_Magic.cs:737-746</c> @95ef426d): the level that nets the most of those that land at <paramref name="minChance"/>, the lower on a tie; <see cref="ManaLoopPress.None"/> when none lands.
    /// </summary>
    /// <param name="levels">The levels, lowest first.</param>
    /// <param name="stamina">The stamina the press reads.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="manaMax">The mana's maximum.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained).</param>
    /// <param name="minChance">The least landing chance.</param>
    public static ManaLoopPress PressOption(ReadOnlySpan<DanceLevel> levels, uint stamina, uint mana, uint manaMax, uint manaConversion, double minChance)
    {
        var missing = manaMax > mana ? manaMax - mana : 0;
        var best = ManaLoopPress.None;
        for (var i = 0; i < levels.Length; i++)
        {
            if (ManaConversionModel.LandingChance(levels[i].BaseMana, levels[i].Power, manaConversion, mana) < minChance)
            {
                continue;
            }

            var cost = Math.Min(ManaConversionModel.ExpectedCost(levels[i].BaseMana, levels[i].Power, manaConversion), mana);
            var give = ManaOrder.Give(levels[i].Row, stamina);
            var net = Math.Min(give, missing) - cost;
            if (best.Level < 0 || net > best.Net)
            {
                BuffStatMods.Transfer(levels[i].Row, stamina, missing, out var taken, out _);
                best = new ManaLoopPress(i, net, levels[i].BaseMana, taken, give);
            }
        }

        return best;
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-13: the press fills, it does not top up): the press before a bolt she can pay: of the levels that land at <paramref name="minChance"/>, the one that nets the most when nothing is cut (its whole give less its cost), the lower on a tie;
    /// its <see cref="ManaLoopPress.Net"/> and <see cref="ManaLoopPress.Taken"/> are read at the mana she is missing now, its <see cref="ManaLoopPress.Give"/> is the whole give. <see cref="DecideBySum"/> sends it once the missing mana takes <c>mana_press_fill_percent</c> of
    /// that give, so a cheap low level never tops up the bar a few points at a time while a dearer level waits to land whole (the live Abode of 2026-11-13: level I pressed at 9 to 14 missing of 332).
    /// </summary>
    /// <param name="levels">The levels, lowest first.</param>
    /// <param name="stamina">The stamina the press reads.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="manaMax">The mana's maximum.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained).</param>
    /// <param name="minChance">The least landing chance.</param>
    public static ManaLoopPress FillOption(ReadOnlySpan<DanceLevel> levels, uint stamina, uint mana, uint manaMax, uint manaConversion, double minChance)
    {
        var missing = manaMax > mana ? manaMax - mana : 0;
        var best = ManaLoopPress.None;
        var bestWhole = 0.0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (ManaConversionModel.LandingChance(levels[i].BaseMana, levels[i].Power, manaConversion, mana) < minChance)
            {
                continue;
            }

            var cost = Math.Min(ManaConversionModel.ExpectedCost(levels[i].BaseMana, levels[i].Power, manaConversion), mana);
            var give = ManaOrder.Give(levels[i].Row, stamina);
            var whole = give - cost;
            if (best.Level < 0 || whole > bestWhole)
            {
                BuffStatMods.Transfer(levels[i].Row, stamina, missing, out var taken, out _);
                best = new ManaLoopPress(i, Math.Min(give, missing) - cost, levels[i].BaseMana, taken, give);
                bestWhole = whole;
            }
        }

        return best;
    }

    /// <summary>M6 14: true when the stamina is under <c>stamina_keep_percent</c> of its maximum (the stamina line).</summary>
    /// <param name="stamina">The stamina now.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static bool UnderStaminaLine(uint stamina, uint staminaMax, LadderVitals rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return ManaOrder.StaminaUnder(stamina, staminaMax, rules.StaminaKeepPercent);
    }

    /// <summary>M6 14: true when a press of <paramref name="give"/> fills: the mana missing takes at least <c>mana_press_fill_percent</c> of it (a give of 0 fills nothing).</summary>
    /// <param name="give">The press's whole give.</param>
    /// <param name="missing">The mana she is missing.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static bool Fills(uint give, uint missing, LadderVitals rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return give > 0 && 100.0 * Math.Min(give, missing) >= (double)rules.ManaPressFillPercent * give;
    }

    /// <summary>
    /// M6 8 H (the owner, 2026-11-09: the enemy's stamina feeds her mana, fights end near full; <c>cache/m6/8/drain-sums.md</c>): the ladder climber's mana loop by the sum, replacing the fixed <c>mana_loop_under</c> and <c>stamina_drain_min_give</c> for her. Every line is a net in mana:
    /// the bolt first when the target would die to it, when more than <c>mana_loop_max_attackers</c> are hitting her, or until <c>mana_loop_bolts_between</c> bolts have gone since the last press, unless the bolt cannot be paid;
    /// at or over <c>mana_loop_stamina_percent</c> of her stamina, a press that nets at least <c>mana_press_min_bolts</c> of a bolt; under it, a drain if the drain and the press after it net at least <c>stamina_drain_min_bolts</c> of a bolt, more than the press alone, and she can pay both; else that press; else nothing.
    /// M6 14 (the owner, 2026-11-13: drain stamina to keep the loop efficient; the live Abode's loop spent her stamina on top-ups of 9 to 14 mana and never refilled it), the order now reads:
    /// <list type="number">
    /// <item><b>The stamina line</b>, first and whatever her mana (a full bar too): under <c>stamina_keep_percent</c>, a Drain Stamina Other on the fullest target in reach that gives at least <c>stamina_drain_min_give</c>, valued in stamina, when she can pay
    /// the drain and still a bolt; it waits while bolts are owed (<c>mana_loop_bolts_between</c>), while more than <c>stamina_drain_max_attackers</c> are hitting her, and while its target is the bolt's target dying to the next bolt.</item>
    /// <item>No mana missing: nothing.</item>
    /// <item>The bolt first while it can be paid (the target dies to it, more than <c>mana_loop_max_attackers</c>, bolts owed).</item>
    /// <item>The drain and the press after it, valued in mana (M6 8 H), under <c>mana_loop_stamina_percent</c>.</item>
    /// <item><b>The press fills</b>: before a bolt she can pay, the press is <see cref="ManaLoopFacts.PressFill"/>, sent only when the mana she is missing takes <c>mana_press_fill_percent</c> of its give (else "the press would overflow"); under a bolt, the press by the sum
    /// (<see cref="ManaLoopFacts.PressNow"/>) as before; either nets at least <c>mana_press_min_bolts</c> of a bolt, and the rest's line holds (<see cref="AmpleBolts"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="facts">The read.</param>
    /// <param name="rules">The ladder's lines.</param>
    public static ManaLoopPlan DecideBySum(in ManaLoopFacts facts, LadderVitals rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (facts.StaminaMax == 0)
        {
            return new ManaLoopPlan(ManaLoopMove.None, "no stamina is read", 0);
        }

        if (UnderStaminaLine(facts.Stamina, facts.StaminaMax, rules) && facts.DrainGive >= rules.StaminaDrainMinGive && facts.Mana >= facts.DrainMana + facts.BoltMana)
        {
            if (facts.Attackers > rules.StaminaDrainMaxAttackers)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the stamina line waits: too many are hitting her", 0);
            }

            if (facts.BoltsOwed > 0)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the stamina line waits: bolts between the mana casts", 0);
            }

            if (facts.TargetDying && facts.DrainOnBoltTarget)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the stamina line waits a bolt: its target dies to it", 0);
            }

            return new ManaLoopPlan(ManaLoopMove.DrainStamina, "the stamina line", facts.DrainGive, StaminaLine: true);
        }

        if (facts.Mana >= facts.ManaMax)
        {
            return new ManaLoopPlan(ManaLoopMove.None, "no mana is missing", 0);
        }

        if (facts.Mana >= facts.BoltMana)
        {
            if (facts.TargetDying)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the bolt first: the target dies to it", 0);
            }

            if (facts.Attackers > rules.ManaLoopMaxAttackers)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the bolt first: too many are hitting her", 0);
            }

            if (facts.BoltsOwed > 0)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "the bolt first: bolts between the mana casts", 0);
            }
        }

        if (ShortOfPress(facts.Stamina, facts.StaminaMax, rules) && facts.DrainGive > 0 && facts.PressAfterDrain.Level >= 0 && facts.Mana >= facts.DrainMana + facts.PressAfterDrain.Mana)
        {
            var pair = facts.PressAfterDrain.Net - facts.DrainMana;
            if (pair > 0 && pair >= rules.StaminaDrainMinBolts * facts.BoltMana && pair > facts.PressNow.Net)
            {
                return new ManaLoopPlan(ManaLoopMove.DrainStamina, "the drain and the press after it pay", pair);
            }
        }

        var boltPaid = facts.Mana >= facts.BoltMana;
        var press = boltPaid ? facts.PressFill : facts.PressNow; // M6 14: under a bolt the press by the sum, as before
        if (boltPaid && press.Level >= 0 && !Fills(press.Give, facts.ManaMax - facts.Mana, rules))
        {
            return new ManaLoopPlan(ManaLoopMove.None, "the press would overflow", 0);
        }

        if (press.Level >= 0 && press.Net > 0 && press.Net >= rules.ManaPressMinBolts * facts.BoltMana)
        {
            if (facts.StaminaKeep > 0 && facts.Mana >= AmpleBolts * facts.BoltMana && facts.Stamina < facts.StaminaKeep + press.Taken)
            {
                return new ManaLoopPlan(ManaLoopMove.None, "no press: the mana is ample and the press would take her stamina under the rest's line", 0);
            }

            return new ManaLoopPlan(ManaLoopMove.Press, boltPaid ? "the press fills" : "the press pays: the bolt cannot be paid", press.Net, press.Level);
        }

        return new ManaLoopPlan(ManaLoopMove.None, "no mana cast pays", 0);
    }

    /// <summary>The rule: under the mana line, at least the share of stamina presses; under the share a drain that gives at least the least goes first; else nothing.</summary>
    /// <param name="mana">The mana now.</param>
    /// <param name="stamina">The stamina now.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="rules">The ladder's lines.</param>
    /// <param name="bestGive">What one drain gives from the fullest target in reach (0: none read).</param>
    public static ManaLoopMove Decide(uint mana, uint stamina, uint staminaMax, LadderVitals rules, int bestGive)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (mana >= rules.ManaLoopUnder || staminaMax == 0)
        {
            return ManaLoopMove.None;
        }

        if (!ShortOfPress(stamina, staminaMax, rules))
        {
            return ManaLoopMove.Press;
        }

        // M6 7 lead (2026-11-06, live, 21:15 UTC): a drain at 13 mana left 3, 91 stamina and no press she could pay for, and she died by inches with every line up. The drain needs the mana for the press after it.
        return bestGive >= rules.StaminaDrainMinGive && mana >= DrainFloor ? ManaLoopMove.DrainStamina : ManaLoopMove.Press;
    }
}
