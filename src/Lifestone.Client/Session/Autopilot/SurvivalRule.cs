using System.Globalization;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the survival layer says about the activity this frame.</summary>
public enum SurvivalVerdict
{
    /// <summary>The activity goes on (in a fight: keep killing).</summary>
    Go,

    /// <summary>The activity is held and she rests, heals or restores a bar until she is over its line.</summary>
    Wait,

    /// <summary>In a fight (<see cref="SurvivalRule.DecideFight"/>): heal now, the activity's bolts wait for it; <see cref="SurvivalDecision.DrainTarget"/> names the attacker to drain, or 0 for her own spell.</summary>
    Heal,

    /// <summary>In a fight (<see cref="SurvivalRule.DecideFight"/>): take mana by the best sum; <see cref="SurvivalDecision.DrainTarget"/> names an attacker to drain when the sum says a drain.</summary>
    TakeMana,

    /// <summary>The activity ends by name and she goes home.</summary>
    Leave,
}

/// <summary>Why the layer decided what it did.</summary>
public enum SurvivalReason
{
    /// <summary>No reason: the activity goes on.</summary>
    None,

    /// <summary>Her health.</summary>
    Heal,

    /// <summary>The keeper is resting for a bar (a wait's later name for itself in the log).</summary>
    Rest,

    /// <summary>Her mana.</summary>
    Mana,

    /// <summary>Her stamina.</summary>
    Stamina,

    /// <summary>Her buffs (an errand's start).</summary>
    Buff,

    /// <summary>Health and mana under their lines, attacked, and no heal payable.</summary>
    NoMana,

    /// <summary>M6 14 the losing race: the heal she can sustain is under the incoming, and one more heal would leave too little health for the way out (the vitals guard's <see cref="AutopilotVitalsGuard.RaceLost"/>).</summary>
    LosingRace,
}

/// <summary>One creature attacking her, as the session has read it.</summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Name">Its name.</param>
/// <param name="Metres">How far from her it stands (a drain needs it within the spell's range).</param>
/// <param name="HealthShare">Its health as the server last gave it, 0 to 1, or null when it never did (never read as full, D92).</param>
/// <param name="Stamina">Its stamina from its appraisal, or null when no appraisal told it (<c>TODO(m6-target-stamina-unread)</c>).</param>
public readonly record struct SurvivalAttacker(uint Guid, string Name, double Metres, float? HealthShare, uint? Stamina)
{
    /// <summary>M6 16 E3.0: its health in points as the session's one health picture has it, or null when no source holds its maximum (the fight's ask fills it; the mana that finishes it is counted from it).</summary>
    public double? Health { get; init; }
}

/// <summary>
/// One frame's inputs of <see cref="SurvivalRule.Decide"/>. <see cref="InFight"/>, <see cref="Attackers"/> and <see cref="Fight"/> are the shape the fight comes under (the owner, 2026-11-09, in stages: about to die, heal or leave; cannot finish these attackers on the mana she has, take mana by the best sum or leave; else keep killing). M6 16 E3.0: the fight asks the rule beside each of its own deciders and compares (<c>FightScenario.SurvivalShadow.cs</c>, report-only); it still decides by its own.
/// </summary>
public readonly record struct SurvivalInput
{
    /// <summary>True when a hold already stands: it runs on to the back lines, not the wait lines.</summary>
    public bool Held { get; init; }

    /// <summary>Her health, in percent of the maximum.</summary>
    public double HealthPercent { get; init; }

    /// <summary>Her mana, in percent of the maximum.</summary>
    public double ManaPercent { get; init; }

    /// <summary>Her stamina, in percent of the maximum.</summary>
    public double StaminaPercent { get; init; }

    /// <summary>A hostile is on her or in reach (<see cref="HostileWatch.InReach"/>): no wait starts or goes on: the activity's own fight-back answers.</summary>
    public bool HostileNear { get; init; }

    /// <summary>A creature attacked her within <see cref="HostileWatch.AttackQuietSeconds"/>.</summary>
    public bool Attacked { get; init; }

    /// <summary>True when a heal or a drain can be paid for now (an unread is payable, never "nothing payable").</summary>
    public bool HealPayable { get; init; }

    /// <summary>The vitals guard is under its own line (raised by the incoming rate) and found nothing payable.</summary>
    public bool GuardNothingPaid { get; init; }

    /// <summary>M6 14 the losing race: the vitals guard found the race lost with Lifestone Recall paid and handed it to the leave (<see cref="AutopilotVitalsGuard.RaceLost"/>).</summary>
    public bool GuardRaceLost { get; init; }

    /// <summary>M6 14 the losing race: the race in words with its numbers (<see cref="AutopilotVitalsGuard.RaceWhy"/>), or null.</summary>
    public string? RaceWhy { get; init; }

    /// <summary>M6 14 the losing race: the layer may end this activity for a lost race (a trip, or a travel that is not her way home: the rung change's way out).</summary>
    public bool MayLeaveRace { get; init; }

    /// <summary>The layer may hold this activity (not a state that keeps its own bars, not standing down).</summary>
    public bool MayWait { get; init; }

    /// <summary>The layer may end this activity.</summary>
    public bool MayLeave { get; init; }

    /// <summary>True when the caller is the fight: the answer is <see cref="SurvivalRule.DecideFight"/>'s.</summary>
    public bool InFight { get; init; }

    /// <summary>M6 16 E3.0: what the fight hands over with <see cref="InFight"/>: its own lines, the means it priced and the mana's numbers.</summary>
    public SurvivalFight Fight { get; init; }

    /// <summary>Who is attacking her, as read now; empty when none is read.</summary>
    public IReadOnlyList<SurvivalAttacker> Attackers { get; init; }
}

/// <summary>What the rule decided.</summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="Reason">Why.</param>
/// <param name="Why">The reason and the numbers, for the one log line.</param>
/// <param name="DrainTarget">With <see cref="SurvivalVerdict.TakeMana"/> or <see cref="SurvivalVerdict.Heal"/>: the attacker to drain, or 0 for her own spells.</param>
public readonly record struct SurvivalDecision(SurvivalVerdict Verdict, SurvivalReason Reason, string Why, uint DrainTarget = 0);

/// <summary>M6 16 E3.0: an activity's ask of the survival layer (<see cref="SurvivalRule.Decide"/> under the ladder's lines, bound where the activity is made). The fight's is <c>FightScenario.Survival</c>.</summary>
/// <param name="input">The frame's reads.</param>
public delegate SurvivalDecision SurvivalAsk(in SurvivalInput input);

/// <summary>
/// M6 8 K (the owner's ruling, 2026-11-09: the code that keeps her alive on mana and health runs above the quest code, not as part of it): the survival layer's one decision a frame, pure: <see cref="SurvivalVerdict.Go"/>, <see cref="SurvivalVerdict.Wait"/> or <see cref="SurvivalVerdict.Leave"/>. The lines are ladder data (D26, <see cref="LadderSurvival"/>).
/// The order: leave for a lost race (M6 14 the losing race: the vitals guard found the heal she can sustain under the incoming and the way out at stake, <see cref="SurvivalInput.GuardRaceLost"/>, where the activity may be left for it); leave (attacked, health and mana under their lines, no heal payable); a hostile near: go on, the activity fights back (M5 D88), never a rest among them; a bar under its line: wait, health first, then stamina, then mana (a press needs stamina); else go on. A hold runs on to the back lines, so it does not flap at the wait lines.
/// </summary>
public static partial class SurvivalRule
{
    /// <summary>True for a state whose station keeps its own bars (the fight; the buff pass and the upkeep, which spend mana on purpose and run their own keeper's pass): the layer's wait is not for them; its emergency heal is. Every other state, and any added later, gets the wait with no code of its own.</summary>
    /// <param name="state">The state.</param>
    public static bool KeepsItsOwnBars(AutopilotState state) => state is AutopilotState.Fight or AutopilotState.Buff or AutopilotState.Upkeep;

    /// <summary>The decision for <paramref name="input"/> under <paramref name="lines"/>.</summary>
    /// <param name="input">The frame's reads.</param>
    /// <param name="lines">The ladder's lines.</param>
    public static SurvivalDecision Decide(in SurvivalInput input, LadderSurvival lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (input.InFight)
        {
            return DecideFight(in input, lines); // M6 16 E3.0: the layer's own answer in a fight (SurvivalRule.Fight.cs); report-only until the stages of D119 flip
        }

        var numbers = string.Create(CultureInfo.InvariantCulture, $"health {input.HealthPercent:0}%, mana {input.ManaPercent:0}%, stamina {input.StaminaPercent:0}%");
        if (input.MayLeaveRace && input.GuardRaceLost)
        {
            // M6 14 the losing race (live 2026-11-15 04:13:27 UTC: the guard drained a losing race on the Holding's approach until she died): the leave comes before any hold, while Lifestone Recall can still land
            return new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.LosingRace, string.Create(CultureInfo.InvariantCulture, $"{numbers}; the race is lost: {input.RaceWhy ?? "the guard's numbers"}"));
        }

        var healthUnder = input.GuardNothingPaid || input.HealthPercent < lines.LeaveHealth;
        if (input.MayLeave && input.Attacked && !input.HealPayable && input.ManaPercent < lines.LeaveMana && healthUnder)
        {
            var count = input.Attackers?.Count ?? 0;
            return new SurvivalDecision(SurvivalVerdict.Leave, SurvivalReason.NoMana, string.Create(CultureInfo.InvariantCulture, $"{numbers}; health under {(input.GuardNothingPaid ? "the guard's line" : lines.LeaveHealth + "%")}, mana under {lines.LeaveMana}%, no heal payable, {count} attacker(s) read"));
        }

        if (input.HostileNear || !input.MayWait)
        {
            return new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, numbers);
        }

        var (health, mana, stamina) = input.Held ? (lines.BackHealth, lines.BackMana, lines.BackStamina) : (lines.WaitHealth, lines.WaitMana, lines.WaitStamina);
        var word = input.Held ? "still under" : "under";
        if (input.HealthPercent < health)
        {
            return new SurvivalDecision(SurvivalVerdict.Wait, SurvivalReason.Heal, string.Create(CultureInfo.InvariantCulture, $"{numbers}; health {word} {health}%, on to {lines.BackHealth}%"));
        }

        if (input.StaminaPercent < stamina)
        {
            return new SurvivalDecision(SurvivalVerdict.Wait, SurvivalReason.Stamina, string.Create(CultureInfo.InvariantCulture, $"{numbers}; stamina {word} {stamina}%, on to {lines.BackStamina}%"));
        }

        if (input.ManaPercent < mana)
        {
            return new SurvivalDecision(SurvivalVerdict.Wait, SurvivalReason.Mana, string.Create(CultureInfo.InvariantCulture, $"{numbers}; mana {word} {mana}%, on to {lines.BackMana}%"));
        }

        return new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, numbers);
    }
}
