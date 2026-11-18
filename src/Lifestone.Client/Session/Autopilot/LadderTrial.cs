using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>Where a ladder trial stands (<see cref="LadderTrial"/>).</summary>
public enum LadderTrialPhase
{
    /// <summary>Asked by the options; no rung was read yet. The first read checks the trial's rules (<see cref="RungOwner.TrialRefusal"/>).</summary>
    Asked,

    /// <summary>Its rules held: the level up job hunts the trial's ground in place of the ladder's own rung.</summary>
    On,

    /// <summary>A rule failed at the start (by name, <see cref="LadderTrial.Why"/>): the ladder went on as usual.</summary>
    Refused,

    /// <summary>The trial is over (its minutes fought, a death, its gate shut at a return; <see cref="LadderTrial.Why"/>): the ladder's own rung again, through a normal rung change.</summary>
    Ended,
}

/// <summary>
/// The trial's part of a rung choice (<see cref="RungChoice.Trial"/>): whether the choice is the trial's, or the rule that refused it by name. Read only by <see cref="LadderTrial.Note"/>, which moves the trial.
/// </summary>
/// <param name="Ground">The trial's ground.</param>
/// <param name="Rung">The later rung whose ground (or alternate) it is, or null when no rung is.</param>
/// <param name="Refused">The rule that failed (<c>&lt;rule&gt;: &lt;why&gt;</c>), or null.</param>
/// <param name="Gate">The trial's gate as a way out to its ground read it (live, before she leaves home), or null when this read made none.</param>
/// <param name="Steers">True when the choice is the trial's: its ground in place of the ladder's own rung.</param>
public sealed record TrialRead(string Ground, string? Rung, string? Refused, RungGateRead? Gate, bool Steers);

/// <summary>
/// M6 item 14 (the owner, 2026-11-13: the climber at level 49, on the Abode's rung, tries the Tusker Quarters early, as a test, then goes back): a trial of a later rung's ground, <c>--autopilot-trial-ground</c> with
/// <c>--autopilot-trial-minutes</c>. While it is on, the level up job hunts the trial's ground in place of the ladder's own rung (the rung choice is <see cref="RungOwner"/>'s: its <c>trial:&lt;ground&gt;</c>), for
/// <see cref="Minutes"/> of fight time counted from her first kill there; then it ends and the ladder's rung is taken again by a normal rung change (<c>rung-moved:&lt;rung&gt;</c>).
/// </summary>
/// <remarks>
/// <para>The rules (<see cref="RungOwner.TrialRefusal"/>), read at the trial's first rung read: the ground is a later rung's ground or one of that rung's alternates (war-mage.json); its entry portal's level range
/// admits her; its rung's gate holds, or, for a rung with none, the last gate before it in climbing order (the Abode's, the Cave's shape), for the bot's protection. The gate is read where every gate is read whole:
/// live, at the way out, before she leaves home for the ground. A rule that fails refuses the trial by name and the ladder goes on as usual; a gate shut at a later return ends it.</para>
/// <para>One object a process: the options are parsed once and every run's options are copies of them (<c>with</c>), so every run of the climb (the start, each move-on, a hand-back) shares it. A new process starts the
/// trial again. A death during the trial ends it (<see cref="EndByDeath"/>, when the death is counted): the recovery's corpse run is the trial's while it lasts, and the ladder resumes its own rung after it.</para>
/// <para>Every line begins <c>ladder: trial: </c> (<see cref="LinePrefix"/>): start, refused, every return (each way out to its ground), first-kill and end. A cycle fought under the trial carries <c>trial=&lt;ground&gt;</c>
/// on its <c>[ladder]</c> line.</para>
/// </remarks>
public sealed class LadderTrial
{
    /// <summary>The minutes of fight time when <c>--autopilot-trial-minutes</c> is not given.</summary>
    public const double DefaultMinutes = 30;

    /// <summary>The most minutes <c>--autopilot-trial-minutes</c> accepts (Lifestone's own: a day).</summary>
    public const double MaxMinutes = 24 * 60;

    /// <summary>The prefix of every trial line.</summary>
    public const string LinePrefix = "ladder: trial: ";

    /// <summary>A trial of <paramref name="ground"/> for <paramref name="minutes"/> of fight time.</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="minutes">The minutes of fight time from her first kill there.</param>
    public LadderTrial(string ground, double minutes = DefaultMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ground);
        if (!double.IsFinite(minutes) || minutes <= 0 || minutes > MaxMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, $"a trial runs more than 0 and at most {MaxMinutes:0} minutes");
        }

        Ground = ground;
        Minutes = minutes;
    }

    /// <summary>The trial's ground.</summary>
    public string Ground { get; }

    /// <summary>The minutes of fight time it runs, from her first kill there.</summary>
    public double Minutes { get; }

    /// <summary>Where it stands.</summary>
    public LadderTrialPhase Phase { get; private set; }

    /// <summary>The rung whose ground (or alternate) the trial's ground is, once it is on.</summary>
    public string? Rung { get; private set; }

    /// <summary>Why it was refused or why it ended, by name; null while it is asked or on.</summary>
    public string? Why { get; private set; }

    /// <summary>The fight seconds counted on its ground since her first kill there.</summary>
    public double FightSeconds { get; private set; }

    /// <summary>True once she has made a kill on its ground.</summary>
    public bool Killed { get; private set; }

    /// <summary>The kills of the fights on its ground counted so far (each fight's own count when it is read).</summary>
    public int Kills { get; private set; }

    /// <summary>The ways out to its ground made (the first is the way there, each later one a return after a trip or the bot).</summary>
    public int Returns { get; private set; }

    /// <summary>True while the trial is the rung choice's: asked (its rules are read at the next rung read) or on.</summary>
    public bool Steers => Phase is LadderTrialPhase.Asked or LadderTrialPhase.On;

    /// <summary>True when its minutes of fight time are fought.</summary>
    public bool TimeUp => Killed && FightSeconds >= Minutes * 60;

    /// <summary>The trial as the options log it.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"trial={Ground} trial_minutes={Minutes:0.#}");

    /// <summary>
    /// Moves the trial by a rung read's <paramref name="read"/> and logs the move by name: a refusal at its start (asked, or on before her first kill there), a start, a return (a way out that read its gate open); a
    /// gate shut at a return after a kill there ends it. Null: nothing to do.
    /// </summary>
    /// <param name="read">The choice's trial part.</param>
    /// <param name="level">The level read.</param>
    /// <param name="path">Which path asked (the log).</param>
    /// <param name="log">The log.</param>
    public void Note(TrialRead? read, int level, string path, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (read is null || !Steers)
        {
            return;
        }

        var c = CultureInfo.InvariantCulture;
        if (read.Refused is { } refused)
        {
            if (Phase == LadderTrialPhase.On && Killed)
            {
                End("gate-shut: " + refused, level, log);
                return;
            }

            Phase = LadderTrialPhase.Refused;
            Why = refused;
            log.Warn(AutopilotScenario.LogCategory, string.Create(c, $"{LinePrefix}refused: ground={Ground} rung={read.Rung ?? "none"} level={level} ({path}): {refused}; the ladder goes on as usual"));
            return;
        }

        if (!read.Steers)
        {
            return;
        }

        if (Phase == LadderTrialPhase.Asked)
        {
            Phase = LadderTrialPhase.On;
            Rung = read.Rung;
            log.Info(AutopilotScenario.LogCategory, string.Create(c, $"{LinePrefix}start: ground={Ground} rung={Rung} level={level} minutes={Minutes:0.#} ({path}): the level up job hunts {Ground} in place of the ladder's own rung for {Minutes:0.#} minutes of fight time from her first kill there; its gate is read whole at each way out, before she leaves home"));
        }

        if (read.Gate is { Open: true } gate)
        {
            Returns++;
            log.Info(AutopilotScenario.LogCategory, string.Create(c, $"{LinePrefix}return n={Returns}: ground={Ground} level={level} ({path}): the gate {gate.Gate.Rung}'s lines are read open; {(Killed ? $"{FightSeconds / 60:0.#} of {Minutes:0.#} minutes fought, kills={Kills}" : "no kill there yet: the minutes start at the first")}"));
        }
    }

    /// <summary>The fight on its ground made a kill: the first starts the minutes (logged once).</summary>
    /// <param name="kills">The kills the fight's own count gained since it was last read.</param>
    /// <param name="log">The log.</param>
    public void Kill(int kills, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (Phase != LadderTrialPhase.On || kills <= 0)
        {
            return;
        }

        Kills += kills;
        if (!Killed)
        {
            Killed = true;
            log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LinePrefix}first kill: ground={Ground}: the {Minutes:0.#} minutes of fight time run from now"));
        }
    }

    /// <summary>Adds <paramref name="seconds"/> of fight on its ground after her first kill there.</summary>
    /// <param name="seconds">The seconds.</param>
    public void Fought(double seconds)
    {
        if (Phase == LadderTrialPhase.On && Killed && double.IsFinite(seconds) && seconds > 0)
        {
            FightSeconds += seconds;
        }
    }

    /// <summary>Ends the trial (its minutes fought, a gate shut at a return): logged by name; the ladder's own rung follows by a normal rung change.</summary>
    /// <param name="why">Why it ends.</param>
    /// <param name="level">The level.</param>
    /// <param name="log">The log.</param>
    public void End(string why, int level, ILogSink log) => Close(why, level, log, "the ladder's own rung next, through a normal rung change");

    /// <summary>A death during the trial ends it, by name, when the death is counted: the ladder resumes its own rung after the recovery.</summary>
    /// <param name="deaths">The deaths counted.</param>
    /// <param name="level">The level.</param>
    /// <param name="log">The log.</param>
    public void EndByDeath(int deaths, int level, ILogSink log) =>
        Close(string.Create(CultureInfo.InvariantCulture, $"death: a death during the trial (deaths={deaths})"), level, log, "the ladder resumes its own rung after the recovery, through a normal rung change");

    private void Close(string why, int level, ILogSink log, string next)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (Phase != LadderTrialPhase.On)
        {
            return;
        }

        Phase = LadderTrialPhase.Ended;
        Why = why;
        log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LinePrefix}end: ground={Ground} rung={Rung} level={level} fought_min={FightSeconds / 60:0.0} minutes={Minutes:0.#} kills={Kills} returns={Returns}: {why}; {next}"));
    }
}
