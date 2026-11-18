using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3; m6-plan.md D119): what the fight hands the survival layer beside her bars and her attackers. No number here is the rule's own: each is the fight's line (one of a pair whose
/// other half is the ladder's, <see cref="LadderSurvival"/>), a means the fight priced with the shared rules of <c>Session/Vitals</c>, or the mana's numbers as the fight reads them. The caller prices the heal's drain on
/// the layer's own pick (<see cref="SurvivalRule.FullestHealth"/>) and the stamina drain on <see cref="SurvivalRule.FullestStamina"/>, so "on whom" is the layer's answer and not the fight's.
/// </summary>
public readonly record struct SurvivalFight
{
    /// <summary>The fight's emergency line now, in percent of her maximum health (its floor raised by the incoming rate, cut to its safe line); 0: the fight has none. The ladder's half of the pair is <see cref="LadderSurvival.WaitHealth"/>.</summary>
    public double EmergencyLinePercent { get; init; }

    /// <summary>The fight's emergency safe line, in percent: where its heal run ends. The ladder's half is <see cref="LadderSurvival.BackHealth"/>.</summary>
    public double EmergencySafePercent { get; init; }

    /// <summary>Her own heal spell (Heal Self) can be cast now: known, paid for by the pack, the mana held.</summary>
    public bool SelfHealPayable { get; init; }

    /// <summary>What her own heal gives her a second of casting now (<c>HealPick.ChooseNow</c>'s rate), or 0.</summary>
    public double SelfHealPerSecond { get; init; }

    /// <summary>The attacker the heal's drain was priced on: the layer's pick (<see cref="SurvivalRule.FullestHealth"/>), or 0 when it picked none.</summary>
    public uint DrainTarget { get; init; }

    /// <summary>A Drain Health Other level can be cast at <see cref="DrainTarget"/> now: in range, the mana held.</summary>
    public bool DrainPayable { get; init; }

    /// <summary>What the best such level gives her a second of casting now, or 0.</summary>
    public double DrainPerSecond { get; init; }

    /// <summary>The mana held, in points.</summary>
    public uint Mana { get; init; }

    /// <summary>What the next bolt is expected to cost, in mana points (its mean at her Mana Conversion where the caller reads it, else the most it costs); 0: no bolt is chosen yet.</summary>
    public double BoltMana { get; init; }

    /// <summary>The bolt's mean landed damage; 0: not read yet.</summary>
    public double BoltDamage { get; init; }

    /// <summary>The best Stamina to Mana press now, by the sum (D114): its net mana at her stamina and the mana on hand; 0 or less: none pays.</summary>
    public double PressNet { get; init; }

    /// <summary>The attacker the stamina drain was priced on: the layer's pick (<see cref="SurvivalRule.FullestStamina"/>), or 0.</summary>
    public uint StaminaTarget { get; init; }

    /// <summary>The net mana of one Drain Stamina Other on <see cref="StaminaTarget"/> and the press after it, the drain's mana taken off; 0 or less: none pays.</summary>
    public double StaminaPairNet { get; init; }

    /// <summary>
    /// M6 16 Q6: the fight's mana loop's own reads (<see cref="SurvivalManaLoop"/>); when handed (<see cref="SurvivalManaLoop.Rule"/> not <see cref="SurvivalManaRule.NotRead"/>) the mana question is answered by the character's loop
    /// (<see cref="SurvivalRule.DecideFightMana"/>) and not by E3.0's sum of the mana that finishes the attackers.
    /// </summary>
    public SurvivalManaLoop ManaLoop { get; init; }
}

/// <summary>
/// M6 16 E3.0: the layer's answer in a fight, pure and allocation-free (every <see cref="SurvivalDecision.Why"/> below is a fixed word; the numbers stay in the input, and whoever writes a line formats them).
/// <para>The order is the owner's (D119): about to die, heal or leave; cannot finish these attackers on the mana she has, take mana by the best sum or leave; else keep killing.</para>
/// <list type="number">
/// <item><b>Leave for a lost race</b>, as outside a fight, when the caller hands the vitals guard's race (<see cref="SurvivalInput.GuardRaceLost"/> where <see cref="SurvivalInput.MayLeaveRace"/>).</item>
/// <item><b>About to die.</b> Her health is under the ladder's wait line (the back line while a heal run stands, <see cref="SurvivalInput.Held"/>) or under the fight's emergency line (its safe line while a run stands). Two lines
/// exist for this one question and the rule takes both as inputs and says which one it stood under. A heal payable: <see cref="SurvivalVerdict.Heal"/>, by a drain on the layer's pick when the drain gives more a second than
/// her own spell (a tie goes to her own spell, as <c>HealPick</c>'s), else by her own spell. None payable, attacked, and health and mana under the ladder's leave lines: <see cref="SurvivalVerdict.Leave"/>. None payable and
/// no leave: the mana question below (mana is what the heal lacks).</item>
/// <item><b>Cannot finish these attackers on the mana she has.</b> The mana that finishes them is the bolts their known health needs at the bolt's mean landed damage, every bolt landing, times the bolt's expected mana:
/// no resist and no fizzle is counted, so it is a low count, and "cannot" is not said of a fight her mana covers on the means. Where a health or the bolt is not read, the ladder's wait line for mana stands in. Short: <see cref="SurvivalVerdict.TakeMana"/> by the
/// better sum of the press and of the stamina drain with the press after it (D114), the drain's target named when the drain wins; no sum that pays, attacked and mana under the ladder's leave line: leave; else she goes on
/// with what she has.</item>
/// <item><b>Else</b> <see cref="SurvivalVerdict.Go"/>.</item>
/// </list>
/// M6 16 Q6: when the fight hands its mana loop's reads (<see cref="SurvivalFight.ManaLoop"/>), the mana question is the character's loop (<see cref="DecideFightMana"/>: the presses' least worth, the bolts between two mana
/// casts, the stamina line, the dying target, the policy's floors), and E3.0's sum above answers only the leave.
/// What the rule does not hold, each the fight's own until its stage: the bolt that kills the last attacker before a heal, the way-out reserve (the recall's mana a heal may not spend), the kinds that let no drain through and
/// the line of sight (the caller leaves such attackers out of what it prices).
/// </summary>
public static partial class SurvivalRule
{
    /// <summary>Why: no attacker is read and no bar is under a line.</summary>
    public const string FightGoes = "nothing calls: she keeps killing";

    /// <summary>Why: the vitals guard's race is lost and the activity may be left for it.</summary>
    public const string FightRaceLost = "the race is lost";

    /// <summary>Why: under both lines of the heal.</summary>
    public const string DieBothLines = "about to die: under the ladder's line and the fight's emergency line";

    /// <summary>Why: under the fight's emergency line alone.</summary>
    public const string DieFightLine = "about to die: under the fight's emergency line alone (the ladder's line is lower)";

    /// <summary>Why: under the ladder's line alone.</summary>
    public const string DieLadderLine = "about to die: under the ladder's line alone (the fight's emergency line is lower or off)";

    /// <summary>Why: about to die, nothing payable, the ladder's leave lines hold.</summary>
    public const string DieLeaves = "about to die, no heal payable, attacked, health and mana under the ladder's leave lines";

    /// <summary>Why: about to die, nothing payable, no leave, and no mana to take: she goes on.</summary>
    public const string DieGoesOn = "about to die and no heal payable, but no leave (the ladder's leave lines do not hold, or none from here) and no mana short: she goes on";

    /// <summary>Why: the mana that finishes the attackers is more than she holds; a press pays.</summary>
    public const string ManaPress = "cannot finish these attackers on the mana she has: the press by the sum";

    /// <summary>Why: the mana that finishes the attackers is more than she holds; the stamina drain and the press after it pay more.</summary>
    public const string ManaDrain = "cannot finish these attackers on the mana she has: the stamina drain and the press after it, by the sum";

    /// <summary>Why: the sum is not read (a health or the bolt), the mana is under the ladder's wait line; a press pays.</summary>
    public const string ManaLinePress = "mana under the ladder's wait line (the mana that finishes them is not read): the press by the sum";

    /// <summary>Why: the sum is not read, the mana is under the ladder's wait line; the stamina drain and the press after it pay more.</summary>
    public const string ManaLineDrain = "mana under the ladder's wait line (the mana that finishes them is not read): the stamina drain and the press after it, by the sum";

    /// <summary>Why: short of mana, nothing to take, the ladder's leave line holds.</summary>
    public const string ManaLeaves = "cannot finish these attackers, no mana to be had, attacked and mana under the ladder's leave line";

    /// <summary>Why: short of mana, nothing to take, no leave: she goes on with what she has.</summary>
    public const string ManaGoesOn = "cannot finish these attackers and no mana to be had, but no leave (the ladder's leave line does not hold, or none from here): she goes on with what she has";

    /// <summary>
    /// The layer's pick of the attacker a Drain Health Other is cast at: the one fullest pick (<see cref="FullestRule.EmergencyDrain"/>: the most health left by share, the nearest among those within a hundredth of it) over
    /// the attackers within <paramref name="reachMetres"/> whose health the session's picture holds; an attacker of unread health is passed over, never read as full (D92). 0 when none. Allocation-free.
    /// </summary>
    /// <param name="attackers">Who is attacking her, or null.</param>
    /// <param name="reachMetres">The drain's reach.</param>
    public static uint FullestHealth(IReadOnlyList<SurvivalAttacker>? attackers, double reachMetres)
    {
        var pick = new FullestPick(FullestRule.EmergencyDrain);
        for (var i = 0; attackers is not null && i < attackers.Count; i++)
        {
            var a = attackers[i];
            if (a.HealthShare is { } share && a.Metres <= reachMetres)
            {
                pick.Offer(i, a.Guid, share, a.Metres);
            }
        }

        return pick.Guid;
    }

    /// <summary>
    /// The layer's pick of the attacker a Drain Stamina Other is cast at: the one fullest pick (<see cref="FullestRule.StaminaDrain"/>: the most stamina, more than none, the first met among equals) over the attackers
    /// within <paramref name="reachMetres"/> whose stamina an appraisal told; an unread one is passed over. 0 when none. Allocation-free.
    /// </summary>
    /// <param name="attackers">Who is attacking her, or null.</param>
    /// <param name="reachMetres">The drain's reach.</param>
    public static uint FullestStamina(IReadOnlyList<SurvivalAttacker>? attackers, double reachMetres)
    {
        var pick = new FullestPick(FullestRule.StaminaDrain);
        for (var i = 0; attackers is not null && i < attackers.Count; i++)
        {
            var a = attackers[i];
            if (a.Stamina is { } stamina && a.Metres <= reachMetres)
            {
                pick.Offer(i, a.Guid, stamina, a.Metres);
            }
        }

        return pick.Guid;
    }

    /// <summary>
    /// The mana that finishes <paramref name="attackers"/> on the means: for each, the bolts its health needs at <paramref name="boltDamage"/> a bolt (every bolt landing), times <paramref name="boltMana"/>. NaN when it cannot be
    /// said: no attacker, no bolt read, or an attacker whose health the picture does not hold. Allocation-free.
    /// </summary>
    /// <param name="attackers">Who is attacking her, or null.</param>
    /// <param name="boltDamage">The bolt's mean landed damage.</param>
    /// <param name="boltMana">The bolt's expected mana.</param>
    public static double ManaToFinish(IReadOnlyList<SurvivalAttacker>? attackers, double boltDamage, double boltMana)
    {
        if (attackers is null || attackers.Count == 0 || !(boltDamage > 0) || !(boltMana > 0))
        {
            return double.NaN;
        }

        var bolts = 0.0;
        for (var i = 0; i < attackers.Count; i++)
        {
            if (attackers[i].Health is not { } health)
            {
                return double.NaN;
            }

            bolts += Math.Ceiling(Math.Max(0, health) / boltDamage);
        }

        return bolts * boltMana;
    }

    /// <summary>The layer's decision in a fight for <paramref name="input"/> under <paramref name="lines"/> (the type's summary is the rule).</summary>
    /// <param name="input">The fight's reads (<see cref="SurvivalInput.Fight"/> with them).</param>
    /// <param name="lines">The ladder's lines.</param>
    public static SurvivalDecision DecideFight(in SurvivalInput input, LadderSurvival lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var f = input.Fight;
        if (input.MayLeaveRace && input.GuardRaceLost)
        {
            return new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.LosingRace, FightRaceLost);
        }

        var payable = f.SelfHealPayable || f.DrainPayable;
        var dying = false;
        var underLadder = input.HealthPercent < (input.Held ? lines.BackHealth : lines.WaitHealth);
        var underFight = input.HealthPercent < (input.Held ? f.EmergencySafePercent : f.EmergencyLinePercent);
        if (underLadder || underFight)
        {
            if (payable)
            {
                var drain = f.DrainPayable && f.DrainTarget != 0 && (!f.SelfHealPayable || f.DrainPerSecond > f.SelfHealPerSecond);
                return new SurvivalDecision(SurvivalVerdict.Heal, SurvivalReason.Heal, underLadder && underFight ? DieBothLines : underFight ? DieFightLine : DieLadderLine, drain ? f.DrainTarget : 0);
            }

            if (input.MayLeave && input.Attacked && input.ManaPercent < lines.LeaveMana && input.HealthPercent < lines.LeaveHealth)
            {
                return new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.NoMana, DieLeaves);
            }

            dying = true; // no heal and no leave: mana is what the heal lacks, so the mana question below
        }

        var loop = f.ManaLoop.Rule != SurvivalManaRule.NotRead;
        var mana = f.ManaLoop;
        if (loop && DecideFightMana(in mana).Decision is { Verdict: not SurvivalVerdict.Go } taken)
        {
            return taken; // M6 16 Q6: the mana is taken by the character's loop (a heal here is the policy's health drain, its fill of health)
        }

        var count = input.Attackers?.Count ?? 0;
        var need = ManaToFinish(input.Attackers, f.BoltDamage, f.BoltMana);
        var read = !double.IsNaN(need);
        var shortOf = read ? f.Mana < need : count > 0 && input.ManaPercent < lines.WaitMana;
        if (!shortOf)
        {
            return new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, dying ? DieGoesOn : FightGoes);
        }

        if (loop)
        {
            // M6 16 Q6: the loop takes nothing; short of the mana that finishes them, the leave below is still E3.0's (the way out's stage), never a second take of mana
            return input.MayLeave && input.Attacked && input.ManaPercent < lines.LeaveMana
                ? new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.NoMana, ManaLeaves)
                : new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, ManaGoesOn);
        }

        var drainPays = f.StaminaTarget != 0 && f.StaminaPairNet > 0 && f.StaminaPairNet > f.PressNet;
        if (drainPays)
        {
            return new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, read ? ManaDrain : ManaLineDrain, f.StaminaTarget);
        }

        if (f.PressNet > 0)
        {
            return new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, read ? ManaPress : ManaLinePress);
        }

        return input.MayLeave && input.Attacked && input.ManaPercent < lines.LeaveMana
            ? new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.NoMana, ManaLeaves)
            : new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, ManaGoesOn);
    }
}
