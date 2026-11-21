using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 hour load (the owner's ruling, 2026-11-16: when she is fighting somewhere she is comfortable, she should bring herself to 150% burden by the end of the hour by discarding loot that is less valuable per weight as time goes on): the
/// run's settings, from <c>--autopilot-hour-load</c>, <c>--autopilot-hour-load-minutes</c> and <c>--autopilot-hour-load-top</c>.
/// </summary>
/// <param name="On">True when the hour's rising load line and the hour's trip are on (default on).</param>
/// <param name="Minutes">The cycle's target length in minutes of fighting on a comfortable ground (default <see cref="DefaultMinutes"/>).</param>
/// <param name="TopPercent">The load line at the hour, in percent of the capacity (default <see cref="DefaultTopPercent"/>).</param>
public sealed record CycleLoadSettings(bool On = true, double Minutes = CycleLoadSettings.DefaultMinutes, int TopPercent = CycleLoadSettings.DefaultTopPercent)
{
    /// <summary>The owner's hour (2026-11-16).</summary>
    public const double DefaultMinutes = 60;

    /// <summary>The owner's 150% (2026-11-16).</summary>
    public const int DefaultTopPercent = 150;

    /// <summary>The least <see cref="TopPercent"/>: the capacity itself (the load modifier is 1 up to it, <c>EncumbranceSystem.GetBurdenMod</c> @95ef426d).</summary>
    public const int MinTopPercent = 100;

    /// <summary>
    /// The most <see cref="TopPercent"/>: 199. ACE's load modifier is <c>2 - burden</c> from 100% and 0 from 200% (<c>EncumbranceSystem.GetBurdenMod</c>, Physics/Common/EncumbranceSystem.cs @95ef426d): at 200% her run skill counts for nothing in
    /// <c>MovementSystem.GetRunRate</c> and her jump for nothing in <c>GetJumpHeight</c>.
    /// </summary>
    public const int MaxTopPercent = 199;

    /// <summary>The most minutes <see cref="Minutes"/> accepts: a day.</summary>
    public const double MaxMinutes = 1440;

    /// <summary>The settings in one phrase, for the log.</summary>
    public override string ToString() => On ? string.Create(CultureInfo.InvariantCulture, $"on: {Minutes:0.#} min to {TopPercent}%") : "off";
}

/// <summary>
/// M6 14 hour load: the load line that rises across a cycle on a comfortable ground. Pure.
/// </summary>
/// <remarks>
/// <para><b>The line.</b> From the burden at the cycle's first fight frame (<c>start</c>, in percent of the capacity) to <c>top</c> at the end of the ramp, straight in time: <c>start + (top - start) x min(1, elapsed / ramp)</c>, never under the
/// base line (the loot's line today: the ground's, 100% by default, <see cref="MageLootThresholds.LoadLinePercent"/>). Under 100% ACE's load modifier is 1 (<c>EncumbranceSystem.GetBurdenMod</c> @95ef426d), so holding her under the base line
/// buys nothing: the rise begins to bite only where the base line ends.</para>
/// <para><b>The ramp's end</b> is the earlier of the hour (the settings' minutes) and the trip the buff bot's lines will call (<c>BuffBotRule.TripUnderSeconds</c> before the least line lapses), read live: the pack is at the top when the trip
/// leaves, whichever cause calls it.</para>
/// </remarks>
public static class CycleLoadLine
{
    /// <summary>The line, in whole percent of the capacity (rounded down, so the loot never passes the line it names).</summary>
    /// <param name="basePercent">The base line (the loot's line today; 0 is no limit, which stays no limit).</param>
    /// <param name="startPercent">The burden at the cycle's start, in percent of the capacity.</param>
    /// <param name="topPercent">The line at the end of the ramp.</param>
    /// <param name="elapsedSeconds">The seconds since the cycle's first fight frame.</param>
    /// <param name="rampSeconds">The ramp's length in seconds (more than 0).</param>
    public static int Percent(int basePercent, double startPercent, int topPercent, double elapsedSeconds, double rampSeconds)
    {
        if (basePercent <= 0)
        {
            return basePercent; // no limit stays no limit
        }

        var share = rampSeconds <= 0 ? 1.0 : Math.Clamp(elapsedSeconds / rampSeconds, 0.0, 1.0);
        var start = Math.Min(startPercent, topPercent);
        var line = (int)Math.Floor(start + ((topPercent - start) * share));
        return Math.Max(basePercent, Math.Min(topPercent, line));
    }
}

/// <summary>
/// M6 safe load (the owner, 2026-11-20: up to 150% burden where she is safe): the highest load at which her running jump still clears a ground's longest jump, from her Run and Jump skills, buffs counted, by
/// <see cref="Lifestone.Client.Movement.Routes.JumpReach.FlightMetres(int, int, float)"/> (retail's run rate and jump height under the load modifier, <c>EncumbranceSystem::LoadMod</c> 11.6096 0x004FD810: 1 up to the capacity, then
/// 2 - load). Pure. At Run 100 and Jump 178 she flies 11.43 m at 100%, and the pit of the Abode and the Holding (need 10.75 m, the profiles' <c>need_m</c>) is cleared up to 106%; at 150% she flies 6.15 m (her Run from
/// <c>tools\fight-sim.ps1 her --her Qwinnie</c>, 2026-11-20; Jump 178 the profiles' own reading of 2026-11-13; <c>JumpLoadCapTests</c> pins the numbers).
/// </summary>
public static class JumpLoadCap
{
    /// <summary>The highest whole percent of the capacity (100 to <see cref="CycleLoadSettings.MaxTopPercent"/>) at which the flight is at least <paramref name="needMetres"/>, or null when even 100% falls short.</summary>
    /// <param name="runSkill">The Run skill, buffs counted.</param>
    /// <param name="jumpSkill">The Jump skill, buffs counted.</param>
    /// <param name="needMetres">The longest jump's need.</param>
    public static int? Percent(int runSkill, int jumpSkill, float needMetres)
    {
        if (Lifestone.Client.Movement.Routes.JumpReach.FlightMetres(runSkill, jumpSkill, 1f) < needMetres)
        {
            return null;
        }

        var percent = CycleLoadSettings.MinTopPercent;
        while (percent < CycleLoadSettings.MaxTopPercent && Lifestone.Client.Movement.Routes.JumpReach.FlightMetres(runSkill, jumpSkill, (percent + 1) / 100f) >= needMetres)
        {
            percent++;
        }

        return percent;
    }
}

/// <summary>What <see cref="ComfortRule.Decide"/> answers.</summary>
public enum ComfortKind
{
    /// <summary>Comfortable: the rising line is in force.</summary>
    Comfortable,

    /// <summary>Not comfortable: the base line holds, and for the rest of the cycle (latched).</summary>
    Uncomfortable,

    /// <summary>A number the rule needs is not read yet: the last verdict stands (none yet: the base line).</summary>
    Unread,

    /// <summary>The settings are off: the base line, no hour trip.</summary>
    Off,
}

/// <summary>
/// M6 14 hour load: one read of the "comfortable" rule, every number live. The cycle is the autopilot's (a trip ends it).
/// </summary>
/// <param name="Ground">The ground's key.</param>
/// <param name="GroundJumps">The running jumps the ground's walk takes (<see cref="Grounds.HuntingGround.Jumps"/>): past 100% the load lowers her jump under the pit's need (<see cref="MageLootThresholds.JumpLoadLinePercent"/>).</param>
/// <param name="WayHome">The way home the travel rule takes from where she stands (<see cref="Autopilot.TravelDecision.WayName"/>: <c>recall-home</c>, <c>at-home</c>, the rung change's, <c>den-travel</c>, <c>gap</c>), or null when it is not read.</param>
/// <param name="Trial">True while a ladder trial runs on this ground.</param>
/// <param name="Approach">True when the fight stands in for a travel or recovery state (a keeper's fight-back on the approach).</param>
/// <param name="Retreats">The retreats this cycle (the retreat rule's leaves home).</param>
/// <param name="SurvivalLeaves">The survival layer's leaves this cycle (the losing race and its kin).</param>
/// <param name="Deaths">The deaths this cycle.</param>
/// <param name="RacesLost">The losing races this cycle (the vitals guard's read found the race lost, whether or not a leave followed: <see cref="CycleLoad.NoteRaceLost"/>).</param>
/// <param name="Health">Her health this cycle against the vitals policy's health floor (<see cref="CycleLoad.Health"/>): the judged outcome.</param>
/// <param name="IncomingPeak">The most damage a second the hostile watch measured on her this cycle (its window, <see cref="Autopilot.HostileWatch.IncomingPerSecond"/>); logged, not judged.</param>
/// <param name="IncomingMean">The cycle's mean damage a second (<see cref="CycleLoad.IncomingMean"/>); logged, not judged.</param>
/// <param name="HealPerSecond">The heal she sustains (the vitals guard's race read), or null when not read; logged, not judged: it swings with her mana now (live ladder40 2026-11-16: 0.97 a second at 18:14:24 UTC, 0.32 at 18:15:24).</param>
/// <param name="BasePercent">The loot's base line on this ground (<see cref="MageLootThresholds.LoadLineOf"/>).</param>
/// <param name="TopPercent">The line at the hour: the settings' top, held to the ground's burden trip line (a trip is due strictly over it, <see cref="LogisticsCounters.BurdenTripPercent"/>).</param>
/// <param name="Safe">M6 safe load (the owner, 2026-11-20): true when she is safe: in a known dungeon (a catalog ground of the dungeon kind) on the level-up job (the ladder's rung ground, no trial). Safe, the line rises whatever her deaths,
/// retreats, races and health this cycle; only what the body cannot do holds it (the way home, the jumps).</param>
/// <param name="JumpCapPercent">M6 safe load: on a ground with jumps, the highest load at which her flight clears the longest (<see cref="JumpLoadCap.Percent"/>), or null when it is not read or even 100% falls short.</param>
public readonly record struct ComfortInput(string Ground, int GroundJumps, string? WayHome, bool Trial, bool Approach, int Retreats, int SurvivalLeaves, int Deaths, int RacesLost, CycleHealth Health, double IncomingPeak, double IncomingMean, double? HealPerSecond, int BasePercent, int TopPercent, bool Safe = false, int? JumpCapPercent = null);

/// <summary>
/// M6 14 hour load: her health this cycle against the vitals policy's health floor (<see cref="Vitals.VitalsLines.HealthFloor"/>: the biggest hit and the burst over a bolt and a drain, worked out from the fight's measured hits at each of its
/// decisions; under it a health drain goes at once, and one more burst could end her).
/// </summary>
/// <param name="FloorRead">True once the fight's policy has worked the floor out this cycle.</param>
/// <param name="Lowest">Her lowest health sampled this cycle (0 before any sample).</param>
/// <param name="Max">Her maximum health at that sample.</param>
/// <param name="FloorAtLowest">The floor in force at that sample (0 when none was read yet).</param>
/// <param name="Floor">The floor last worked out.</param>
/// <param name="Breached">True when a sample was under the floor in force then.</param>
/// <param name="BreachHealth">The health at the first sample under the floor (0 with none).</param>
/// <param name="BreachFloor">The floor it was under (0 with none).</param>
public readonly record struct CycleHealth(bool FloorRead, uint Lowest, uint Max, uint FloorAtLowest, uint Floor, bool Breached, uint BreachHealth, uint BreachFloor);

/// <summary>The rule's answer: its kind and why, in words.</summary>
/// <param name="Kind">Comfortable, not, unread or off.</param>
/// <param name="Why">The first reason that decided it (the numbers in it).</param>
public readonly record struct ComfortVerdict(ComfortKind Kind, string Why);

/// <summary>
/// M6 14 hour load: "comfortable", a rule over what the fight already measures (the owner, 2026-11-16; the lead's brief: no retreat, losing race or survival leave on this ground this cycle, incoming damage covered by her measured recovery,
/// the ground not an approach or a trial; never past 100% on a ground or a leg with jumps). Pure. The first failing check names the verdict, in this order:
/// <list type="number">
/// <item>the ground has a jump (the pit-hold: past 100% her flight falls under the pit's need);</item>
/// <item>the top is at or under the base line (the ground's burden trip line holds it there: nothing to rise to);</item>
/// <item>the way home is not read (unread), or is not Lifestone Recall (the way home by portals walks legs whose jumps the travel file does not record: <c>TODO(m6-hour-load-route-jumps)</c>; she would walk it at the top's load);</item>
/// <item>a ladder trial runs here (a trial measures the ground at the ladder's own loot);</item>
/// <item>the fight stands on an approach (a keeper's fight-back): not this cycle's ground, the read is skipped (unread: the verdict in force stands; the loot is held in such a fight anyway);</item>
/// <item>a death, a retreat, a survival leave or a losing race this cycle;</item>
/// <item>her health fell under the vitals policy's health floor this cycle; or the floor is not read yet (unread).</item>
/// </list>
/// The outcome, not a rate (the lead, 2026-11-16, on live ladder40: the heal she sustains swings with her mana now, 0.97 then 0.32 a second a minute later, and latched the Tusker Quarters, where she has not died in 30 hours of play): the
/// incoming and the heal are logged for reference. Not the fight's emergency line either: it is a 60% floor she crosses and drains back from in a normal fight (live ladder37: 17 of 19 cycles on the Quarters had an emergency drain, at
/// 113 to 128 of 212 to 215 health, while the policy's health floor just before each was 32 to 68).
/// The way back to the ground after the trip is walked after the sale, at the burden the visit leaves (the rising line starts again from it), so it is never walked at the top's load.
/// </summary>
public static class ComfortRule
{
    /// <summary>The rule's answer.</summary>
    /// <param name="input">The read.</param>
    public static ComfortVerdict Decide(in ComfortInput input)
    {
        var c = CultureInfo.InvariantCulture;
        if (input.Safe)
        {
            return Safe(in input);
        }

        if (input.GroundJumps > 0)
        {
            return new(ComfortKind.Uncomfortable, string.Create(c, $"the ground's walk takes {input.GroundJumps} running jump(s): the jump hold keeps the line at {input.BasePercent}% (M6 14 pit-hold)"));
        }

        if (input.TopPercent <= input.BasePercent)
        {
            return new(ComfortKind.Uncomfortable, string.Create(c, $"the top {input.TopPercent}% is at or under the base line {input.BasePercent}% (the ground's burden trip line holds it): nothing to rise to"));
        }

        if (input.WayHome is null)
        {
            return new(ComfortKind.Unread, "the way home is not read yet");
        }

        if (input.WayHome is not ("recall-home" or "at-home"))
        {
            return new(ComfortKind.Uncomfortable, $"the way home is '{input.WayHome}', not Lifestone Recall: its legs would be walked at the top's load and the travel file records no jumps on them (TODO(m6-hour-load-route-jumps))");
        }

        if (input.Trial)
        {
            return new(ComfortKind.Uncomfortable, "a ladder trial runs on this ground: the trial measures it at the base line");
        }

        if (input.Approach)
        {
            return new(ComfortKind.Unread, "the fight stands in for a travel or recovery state (an approach): not the ground's fight, this read is skipped");
        }

        if (input.Deaths > 0 || input.Retreats > 0 || input.SurvivalLeaves > 0 || input.RacesLost > 0)
        {
            return new(ComfortKind.Uncomfortable, string.Create(c, $"this cycle: {input.Deaths} death(s), {input.Retreats} retreat(s), {input.SurvivalLeaves} survival leave(s), {input.RacesLost} losing race(s)"));
        }

        var h = input.Health;
        if (h.Breached)
        {
            return new(ComfortKind.Uncomfortable, string.Create(c, $"her health fell to {h.BreachHealth} of {h.Max}, under the vitals policy's health floor {h.BreachFloor} (the biggest hit and the burst over a bolt and a drain): one burst from the end"));
        }

        if (!h.FloorRead)
        {
            return new(ComfortKind.Unread, "the vitals policy's health floor is not read yet (the fight's policy works it out at each of its decisions)");
        }

        return new(ComfortKind.Comfortable, string.Create(c, $"no jump, home by Lifestone Recall, no trial, no death, retreat, leave or losing race this cycle, and her lowest health {h.Lowest} of {h.Max} stayed over the vitals policy's health floor ({h.FloorAtLowest} then, {h.Floor} now)"));
    }

    /// <summary>
    /// M6 safe load (the owner, 2026-11-20: up to 150% burden when she is safe, safe being inside a known dungeon where she is only levelling): the rule on a safe ground. Her deaths, retreats, leaves, losing races and health this cycle no
    /// longer hold the line; what the body cannot do still does, in this order: nothing to rise to; a ground with jumps whose cap is not read (unread) or is at or under the base line (her flight clears the longest jump only up to it);
    /// the way home not read (unread) or not Lifestone Recall; an approach (unread). Else comfortable: the line rises to the top, held to the jump's cap on a ground with jumps (<see cref="CycleLoad.LineAt"/>).
    /// </summary>
    /// <param name="input">The read.</param>
    private static ComfortVerdict Safe(in ComfortInput input)
    {
        var c = CultureInfo.InvariantCulture;
        if (input.TopPercent <= input.BasePercent)
        {
            return new(ComfortKind.Uncomfortable, string.Create(c, $"safe, but the top {input.TopPercent}% is at or under the base line {input.BasePercent}% (the ground's burden trip line holds it): nothing to rise to"));
        }

        if (input.GroundJumps > 0)
        {
            if (input.JumpCapPercent is not { } cap)
            {
                return new(ComfortKind.Unread, string.Create(c, $"safe, but the ground's walk takes {input.GroundJumps} running jump(s) and her Run and Jump are not read, or her flight falls short of the longest even at 100%"));
            }

            if (cap <= input.BasePercent)
            {
                return new(ComfortKind.Uncomfortable, string.Create(c, $"safe, but the ground's walk takes {input.GroundJumps} running jump(s) and her flight clears the longest only up to {cap}%, at or under the base line {input.BasePercent}%: the jump hold keeps the line there (M6 14 pit-hold)"));
            }
        }

        if (input.WayHome is null)
        {
            return new(ComfortKind.Unread, "the way home is not read yet");
        }

        if (input.WayHome is not ("recall-home" or "at-home"))
        {
            return new(ComfortKind.Uncomfortable, $"safe, but the way home is '{input.WayHome}', not Lifestone Recall: its legs would be walked at the top's load and the travel file records no jumps on them (TODO(m6-hour-load-route-jumps))");
        }

        if (input.Approach)
        {
            return new(ComfortKind.Unread, "the fight stands in for a travel or recovery state (an approach): not the ground's fight, this read is skipped");
        }

        var held = input.GroundJumps > 0 && input.JumpCapPercent is { } jumpCap && jumpCap < input.TopPercent
            ? string.Create(c, $", held to {jumpCap}% by her flight over the ground's longest jump")
            : string.Empty;
        return new(ComfortKind.Comfortable, string.Create(c, $"safe: a known dungeon on the level-up job (the owner, 2026-11-20): the line rises to {input.TopPercent}%{held}, home by Lifestone Recall"));
    }

    /// <summary>The read as the log prints it (every number).</summary>
    /// <param name="input">The read.</param>
    /// <param name="verdict">The rule's answer to it.</param>
    /// <param name="inForce">The verdict in force after the read (a latched one, or the last one for an unread read).</param>
    public static string Line(in ComfortInput input, in ComfortVerdict verdict, ComfortKind inForce) => string.Create(
        CultureInfo.InvariantCulture,
        $"hour load: comfort read: ground={input.Ground} verdict={Word(verdict.Kind)} in_force={Word(inForce)} jumps={input.GroundJumps} way_home={input.WayHome ?? "unread"} trial={(input.Trial ? "yes" : "no")} approach={(input.Approach ? "yes" : "no")} deaths={input.Deaths} retreats={input.Retreats} survival_leaves={input.SurvivalLeaves} races_lost={input.RacesLost} lowest_health={input.Health.Lowest}/{input.Health.Max} floor_at_lowest={input.Health.FloorAtLowest} health_floor={(input.Health.FloorRead ? input.Health.Floor.ToString(CultureInfo.InvariantCulture) : "unread")} incoming_peak={input.IncomingPeak:0.##}/s incoming_mean={input.IncomingMean:0.##}/s heal={(input.HealPerSecond is { } h ? h.ToString("0.##", CultureInfo.InvariantCulture) + "/s" : "unread")} base={input.BasePercent}% top={input.TopPercent}%: {verdict.Why}");

    /// <summary>A kind's word in the log.</summary>
    /// <param name="kind">The kind.</param>
    public static string Word(ComfortKind kind) => kind switch
    {
        ComfortKind.Comfortable => "comfortable",
        ComfortKind.Uncomfortable => "uncomfortable",
        ComfortKind.Off => "off",
        _ => "unread",
    };
}

/// <summary>
/// M6 14 hour load: what the session holds of the cycle's load, between the autopilot (which begins and ends the cycle, reads the comfort rule and calls the hour's trip) and the fight's loot step (which asks the line for each corpse). One
/// per session (<see cref="For"/>); game thread only. Without an autopilot nothing begins it, and the line asked is the base line.
/// </summary>
public sealed class CycleLoad
{
    private static readonly ConditionalWeakTable<GameState, CycleLoad> PerSession = new();

    /// <summary>The memory of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static CycleLoad For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static _ => new CycleLoad());
    }

    /// <summary>The settings of the cycle (set at <see cref="Begin"/>).</summary>
    public CycleLoadSettings Settings { get; private set; } = new(On: false);

    /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public bool Begun { get; private set; }

    /// <summary>The ground's key the cycle began on.</summary>
    public string Ground { get; private set; } = string.Empty;

    /// <summary>The clock at the cycle's first fight frame.</summary>
    public double StartClock { get; private set; } = double.NaN;

    /// <summary>The burden at the cycle's first fight frame.</summary>
    public int StartBurden { get; private set; }

    /// <summary>The capacity at the cycle's first fight frame.</summary>
    public int StartCapacity { get; private set; }

    /// <summary>The line at the end of the ramp (the settings' top held to the ground's burden trip line).</summary>
    public int TopPercent { get; private set; }

    /// <summary>The verdict in force (<see cref="ComfortKind.Unread"/> before the first decided read).</summary>
    public ComfortKind InForce { get; private set; } = ComfortKind.Unread;

    /// <summary>Why the verdict in force is what it is.</summary>
    public string InForceWhy { get; private set; } = "not read yet";

    /// <summary>True once the rule said uncomfortable this cycle: the base line for the rest of it.</summary>
    public bool Latched { get; private set; }

    /// <summary>The most damage a second measured on her this cycle (<see cref="NoteIncoming"/>); logged, not judged.</summary>
    public double IncomingPeak { get; private set; }

    /// <summary>
    /// The mean damage a second over the cycle's samples (fights and lulls alike, <see cref="NoteIncoming"/>): what the heal she sustains has to cover (live ladder39 2026-11-16 18:03 UTC: the
    /// peak 4.4 a second of a pack's first seconds against the sustained 1.61 latched the Tusker Quarters uncomfortable; a burst is her health's and the losing-race guard's, the mean is the heal's).
    /// </summary>
    public double IncomingMean => _incomingSamples == 0 ? 0 : _incomingSum / _incomingSamples;

    private double _incomingSum;
    private int _incomingSamples;
    private bool _floorRead;
    private uint _floor;
    private uint _lowest = uint.MaxValue;
    private uint _lowestMax;
    private uint _floorAtLowest;
    private bool _breached;
    private uint _breachHealth;
    private uint _breachFloor;

    /// <summary>The losing races seen this cycle (<see cref="NoteRaceLost"/>).</summary>
    public int RacesLost { get; private set; }

    /// <summary>Her health this cycle against the vitals policy's health floor (<see cref="NoteHealthFloor"/>, <see cref="NoteHealth"/>): the comfort rule's outcome.</summary>
    public CycleHealth Health => new(_floorRead, _lowest == uint.MaxValue ? 0 : _lowest, _lowestMax, _floorAtLowest, _floor, _breached, _breachHealth, _breachFloor);

    /// <summary>Notes the vitals policy's health floor the fight just worked out (<see cref="Vitals.VitalsLines.HealthFloor"/>, at each of its decisions).</summary>
    /// <param name="floor">The floor, in health points.</param>
    public void NoteHealthFloor(uint floor) => (_floorRead, _floor) = (true, floor);

    /// <summary>Notes her health now (each fight frame): the cycle's lowest with the floor in force then, and the first sample under the floor (a breach). Allocation-free.</summary>
    /// <param name="health">Her health.</param>
    /// <param name="max">Her maximum health.</param>
    public void NoteHealth(uint health, uint max)
    {
        if (max == 0)
        {
            return;
        }

        if (health < _lowest)
        {
            (_lowest, _lowestMax, _floorAtLowest) = (health, max, _floorRead ? _floor : 0);
        }

        if (_floorRead && health < _floor && !_breached)
        {
            (_breached, _breachHealth, _breachFloor) = (true, health, _floor);
        }
    }

    /// <summary>Notes a losing race (the vitals guard's read found the race lost).</summary>
    public void NoteRaceLost() => RacesLost++;

    /// <summary>The ramp's end: the earlier of the hour and the buff bot's trip (<see cref="RampEnds"/>), as a clock.</summary>
    public double RampEndClock { get; private set; } = double.NaN;

    /// <summary>True once the rising line was in force this cycle (a comfortable read).</summary>
    public bool Rose { get; private set; }

    /// <summary>True once the hour's trip was called this cycle.</summary>
    public bool HourCalled { get; private set; }

    /// <summary>The highest line the loot was given this cycle (<see cref="LineFor"/>), in percent.</summary>
    public int HighestLine { get; private set; }

    /// <summary>
    /// M6 safe load: the highest load at which her flight clears the ground's longest jump (<see cref="JumpLoadCap.Percent"/>), as the last comfort read found it; null when not read, or on a ground without jumps. On a ground with jumps
    /// the rising line is held to it, and without it the line stays at the base.
    /// </summary>
    public int? JumpCapPercent { get; private set; }

    /// <summary>M6 safe load: notes the jump's cap read now (<see cref="JumpCapPercent"/>).</summary>
    /// <param name="percent">The cap, or null.</param>
    public void NoteJumpCap(int? percent) => JumpCapPercent = percent;

    /// <summary>The cycle begins at its first fight frame on the ground: the burden and the capacity then, the settings and the top.</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="burden">The burden now.</param>
    /// <param name="capacity">The capacity now.</param>
    /// <param name="settings">The run's settings.</param>
    /// <param name="topPercent">The top: the settings' top held to the ground's burden trip line.</param>
    public void Begin(string ground, double clock, int burden, int capacity, CycleLoadSettings settings, int topPercent)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (Begun, Ground, StartClock, StartBurden, StartCapacity, Settings, TopPercent, JumpCapPercent) = (true, ground, clock, burden, capacity, settings, topPercent, null);
        (InForce, InForceWhy, Latched, IncomingPeak, Rose, HourCalled, HighestLine) = (settings.On ? ComfortKind.Unread : ComfortKind.Off, settings.On ? "not read yet" : "the settings are off", false, 0, false, false, 0);
        (_incomingSum, _incomingSamples) = (0, 0);
        (_floorRead, _floor, _lowest, _lowestMax, _floorAtLowest, _breached, _breachHealth, _breachFloor, RacesLost) = (false, 0u, uint.MaxValue, 0u, 0u, false, 0u, 0u, 0);
        RampEndClock = clock + (settings.Minutes * 60);
    }

    /// <summary>The cycle ends (a trip, a death, a stop): the next fight frame begins a new one.</summary>
    public void End() => Begun = false;

    /// <summary>The seconds since the cycle's first fight frame (0 before it).</summary>
    /// <param name="clock">The session clock.</param>
    public double Elapsed(double clock) => Begun ? Math.Max(0, clock - StartClock) : 0;

    /// <summary>The burden at the start, in percent of the capacity then.</summary>
    public double StartPercent => StartCapacity <= 0 ? 0 : StartBurden * 100.0 / StartCapacity;

    /// <summary>Notes a measured damage a second: the cycle's mean (<see cref="IncomingMean"/>, the comfort rule's) and its peak (logged).</summary>
    /// <param name="perSecond">The damage a second now.</param>
    public void NoteIncoming(double perSecond)
    {
        if (!double.IsFinite(perSecond) || perSecond < 0)
        {
            return;
        }

        (_incomingSum, _incomingSamples) = (_incomingSum + perSecond, _incomingSamples + 1);
        if (perSecond > IncomingPeak)
        {
            IncomingPeak = perSecond;
        }
    }

    /// <summary>
    /// The ramp ends at the earlier of the hour and the buff bot's trip (<paramref name="botTripInSeconds"/> from <paramref name="clock"/>; null when the bot's lines are not read or do not apply). Never later than the hour, never before now.
    /// </summary>
    /// <param name="clock">The session clock.</param>
    /// <param name="botTripInSeconds">The seconds until the bot's trip is called, or null.</param>
    public void RampEnds(double clock, double? botTripInSeconds)
    {
        if (!Begun)
        {
            return;
        }

        var hour = StartClock + (Settings.Minutes * 60);
        RampEndClock = botTripInSeconds is { } bot && double.IsFinite(bot) ? Math.Max(clock, Math.Min(hour, clock + Math.Max(0, bot))) : hour;
    }

    /// <summary>Applies a comfort read: an uncomfortable verdict latches for the cycle; an unread keeps the verdict in force; a comfortable one is in force unless latched. Returns the verdict in force.</summary>
    /// <param name="verdict">The rule's answer.</param>
    public ComfortKind Apply(in ComfortVerdict verdict)
    {
        if (!Settings.On)
        {
            (InForce, InForceWhy) = (ComfortKind.Off, "the settings are off");
            return InForce;
        }

        if (Latched)
        {
            return InForce;
        }

        switch (verdict.Kind)
        {
            case ComfortKind.Uncomfortable:
                (InForce, InForceWhy, Latched) = (ComfortKind.Uncomfortable, verdict.Why, true);
                break;
            case ComfortKind.Comfortable:
                (InForce, InForceWhy, Rose) = (ComfortKind.Comfortable, verdict.Why, true);
                break;
        }

        return InForce;
    }

    /// <summary>
    /// The line the loot keeps under for a corpse planned now: the rising line (<see cref="CycleLoadLine.Percent"/>) while the verdict in force is comfortable and the cycle has begun, held to the jump's cap on a ground with jumps
    /// (<see cref="JumpCapPercent"/>; M6 safe load: only a safe ground's verdict is comfortable there); else <paramref name="basePercent"/>.
    /// </summary>
    /// <param name="basePercent">The loot's own line (its thresholds').</param>
    /// <param name="hasJumps">True when the ground's walk takes a running jump (never past the base line there).</param>
    /// <param name="clock">The session clock.</param>
    public int LineFor(int basePercent, bool hasJumps, double clock)
    {
        var line = LineAt(basePercent, hasJumps, clock);
        if (line != basePercent)
        {
            HighestLine = Math.Max(HighestLine, line);
        }

        return line;
    }

    /// <summary>The line <see cref="LineFor"/> would give now, without noting it as given (the autopilot's log lines).</summary>
    /// <param name="basePercent">The loot's own line.</param>
    /// <param name="hasJumps">True when the ground's walk takes a running jump.</param>
    /// <param name="clock">The session clock.</param>
    public int LineAt(int basePercent, bool hasJumps, double clock)
    {
        if (!Begun || !Settings.On || InForce != ComfortKind.Comfortable || basePercent <= 0)
        {
            return basePercent;
        }

        var top = TopPercent;
        if (hasJumps)
        {
            if (JumpCapPercent is not { } cap || cap <= basePercent)
            {
                return basePercent; // M6 14 pit-hold: no line past what her flight clears
            }

            top = Math.Min(top, cap);
        }

        var ramp = Math.Max(1, RampEndClock - StartClock);
        return CycleLoadLine.Percent(basePercent, StartPercent, top, Elapsed(clock), ramp);
    }

    /// <summary>The words behind <see cref="LineFor"/>'s answer, for the plan's line.</summary>
    /// <param name="basePercent">The loot's own line.</param>
    /// <param name="line">The line given.</param>
    /// <param name="clock">The session clock.</param>
    public string LineWhy(int basePercent, int line, double clock) => !Begun
        ? "no cycle begun (no autopilot): the base line"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"hour load {Settings}: line {line}% (base {basePercent}%, start {StartPercent:0.#}% = {StartBurden} of {StartCapacity}, top {TopPercent}%, {Elapsed(clock) / 60:0.#} of {(RampEndClock - StartClock) / 60:0.#} min of the ramp; {ComfortRule.Word(InForce)}{(Latched ? " (latched)" : string.Empty)}: {InForceWhy})");

    /// <summary>True when the hour's trip is due: the rising line was in force, it still is, and the settings' minutes are fought since the cycle's first fight frame.</summary>
    /// <param name="clock">The session clock.</param>
    public bool HourDue(double clock) => Begun && Settings.On && !HourCalled && Rose && InForce == ComfortKind.Comfortable && Elapsed(clock) >= Settings.Minutes * 60;

    /// <summary>Notes that the hour's trip was called.</summary>
    public void NoteHourCalled() => HourCalled = true;

    /// <summary>
    /// The cycle line's <c>hour_load=</c> field: <c>off</c>, <c>held</c> (the loot was never given the rising line this cycle: never comfortable, no corpse planned while it was, or no cycle), <c>rose:&lt;line&gt;</c> (the highest line the
    /// loot was given) or <c>hour:&lt;line&gt;</c> (the hour's trip ended the cycle; the highest line given, 0 when no corpse was planned).
    /// </summary>
    public string Field => !Settings.On ? "off" : !Begun ? "held" : HourCalled ? string.Create(CultureInfo.InvariantCulture, $"hour:{HighestLine}") : !Rose || HighestLine <= 0 ? "held" : string.Create(CultureInfo.InvariantCulture, $"rose:{HighestLine}");
}
