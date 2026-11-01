using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The autopilot's options (M5 item 4 a1, D57; registered in <c>OptionCatalog.Autopilot.cs</c>): the stop rules (<c>--autopilot-minutes</c> on the session clock, <c>--autopilot-level</c> on the character level the state holds), the forced trip
/// (<c>--autopilot-trip-after-kills</c>, D52's live half) and the gate's scripted death (<c>--autopilot-test-death</c>, M4 D37). A stop request (<see cref="AutopilotScenario.RequestStop"/>) is not an option: the key and the hosts send it.
/// No rule set means the run goes on until a stop request. Immutable; <see cref="FromArguments"/> is the parser the hosts call. The bounds are Lifestone's own.
/// </summary>
public sealed record AutopilotOptions
{
    /// <summary>The most minutes <c>--autopilot-minutes</c> accepts: a week.</summary>
    public const double MaxMinutes = 7 * 24 * 60;

    /// <summary>The highest level <c>--autopilot-level</c> accepts (ACE's level cap, 275).</summary>
    public const int MaxLevel = 275;

    /// <summary>The most kills <c>--autopilot-trip-after-kills</c> accepts.</summary>
    public const int MaxTripAfterKills = 100_000;

    /// <summary>The most reconnect tries <c>--autopilot-reconnect-waits</c> accepts.</summary>
    public const int MaxReconnectTries = 10;

    /// <summary>The longest single reconnect wait <c>--autopilot-reconnect-waits</c> accepts, in seconds.</summary>
    public const double MaxReconnectWaitSeconds = 600;

    /// <summary>The most recoveries <c>--autopilot-watchdog-recoveries</c> accepts.</summary>
    public const int MaxWatchdogRecoveries = 20;

    /// <summary>The watchdog scale's range (<c>--autopilot-watchdog-scale</c>).</summary>
    public const double MinWatchdogScale = 0.01;

    /// <summary>The watchdog scale's range (<c>--autopilot-watchdog-scale</c>).</summary>
    public const double MaxWatchdogScale = 100;

    /// <summary>
    /// The seconds waited before each reconnect try (M5 item 4 a2; Lifestone's own, no retail rule): 5, 15, 30, 60, 120. ACE drops a client silent for 60 s (<c>NetworkSession.cs:336-338</c> @95ef426d), so the first tries may meet the old session
    /// still held and the sum (230 s) outlasts it by a wide margin. The last failure stops the run (<c>stop=reconnect-failed</c>).
    /// </summary>
    public static IReadOnlyList<double> DefaultReconnectWaits { get; } = [5, 15, 30, 60, 120];

    /// <summary>The session-clock minutes after which the autopilot stops (<c>stop=minutes</c>) at its next safe point; 0 (the default) is no such rule.</summary>
    public double Minutes { get; init; }

    /// <summary>The character level at which the autopilot stops (<c>stop=level</c>), read from the state's PropertyInt 25; 0 (the default) is no such rule.</summary>
    public int Level { get; init; }

    /// <summary>The kills, counted from the cycle's start, after which the autopilot forces one logistics trip (<c>trigger=forced</c>, D52); once a run (the gate wants one trip forced early). 0 (the default) is none.</summary>
    public int TripAfterKills { get; init; }

    /// <summary>True: the gate's scripted death, once, at the first fight after its first kill (the journey's death station: M4 D37's undress and lie down among the creatures).</summary>
    public bool TestDeath { get; init; }

    /// <summary>The factor on every watchdog bound (<see cref="AutopilotWatchdogs.BaseSeconds"/>; <c>--autopilot-watchdog-scale</c>, default 1).</summary>
    public double WatchdogScale { get; init; } = 1;

    /// <summary>The recoveries a cycle's watchdog trips may make before the next one stops the run (<c>--autopilot-watchdog-recoveries</c>, default <see cref="AutopilotWatchdogs.DefaultMaxRecoveries"/>).</summary>
    public int WatchdogRecoveries { get; init; } = AutopilotWatchdogs.DefaultMaxRecoveries;

    /// <summary>The seconds waited before each reconnect try, one entry a try (<c>--autopilot-reconnect-waits</c>, default <see cref="DefaultReconnectWaits"/>).</summary>
    public IReadOnlyList<double> ReconnectWaits { get; init; } = DefaultReconnectWaits;

    /// <summary>
    /// The run's tag (M5 4 F1, 4-R1): the checkpoint stores it, and a file written under another tag is another run's, set aside and named on the <c>checkpoint:</c> line. Empty (the default): none. Not a command-line option: a host sets it
    /// (the gate's lane gives each round its own).
    /// </summary>
    public string RunTag { get; init; } = string.Empty;

    /// <summary>
    /// The parsed buff options the host passes (M5 4 F1, 4-R4's scenario half): the autopilot's upkeep reads the rebuff margin (<see cref="BuffOptions.RebuffMarginSeconds"/>) and the buff cycle's other settings from it, as the rebuff driver does,
    /// so <c>--buff-auto</c>'s margin and the upkeep's agree. Null (the default): the driver's defaults (<c>new BuffOptions()</c>).
    /// </summary>
    public BuffOptions? Buff { get; init; }

    /// <summary>The parse errors (empty when every option parsed).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// M5 7 F15 (7-F2): <c>--fight-drain-under</c> as the fight reads it (default <see cref="Scenarios.FightOptions.DefaultDrainUnderPercent"/>; 0 is the drain rule off). The autopilot reads it only to keep its own health stop under the
    /// fight's drain line (<see cref="HealthFloorPercent"/>); the fight itself parses and validates the option.
    /// </summary>
    public int DrainUnderPercent { get; init; } = Scenarios.FightOptions.DefaultDrainUnderPercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-floor</c> as the fight reads it: the vitals guard's emergency line in the states outside the fight is the fight's own (the fight parses and validates the option).</summary>
    public int EmergencyFloorPercent { get; init; } = Scenarios.FightOptions.DefaultEmergencyFloorPercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-safe</c> as the fight reads it (the guard's safe line).</summary>
    public int EmergencySafePercent { get; init; } = Scenarios.FightOptions.DefaultEmergencySafePercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-margin</c> as the fight reads it.</summary>
    public int EmergencyMarginPercent { get; init; } = Scenarios.FightOptions.DefaultEmergencyMarginPercent;

    /// <summary>M5 7 F23: <c>--fight-heal-damage-weight</c> as the fight reads it (the guard's heal pick is the fight's).</summary>
    public double HealDamageWeight { get; init; } = Vitals.HealPick.DefaultDamageWeight;

    /// <summary>M5 7 F23: <c>--death-trip-aggro-m</c> as the death handler reads it: the aggro reach of the hostile watch (a living target within it holds the buff upkeep and the in-fight refresh; F19's registered distance).</summary>
    public double AggroReachMetres { get; init; } = Scenarios.DeathOptions.DefaultTripAggroMetres;

    /// <summary>
    /// M5 7 F15 (7-F2): the health share, in percent, under which the upkeep stops the fight for the keeper's heal. With the drain rule off (<see cref="DrainUnderPercent"/> 0) it is
    /// <see cref="AutopilotScenario.HealthFloorPercent"/> (50). With it on, D88's drain starts at <see cref="DrainUnderPercent"/> and runs until the safe line, so a stop at the same line would cut every drain short: the stop is
    /// then derived from the option, 60% of <c>--fight-drain-under</c> (30 at its default 50) and at most <see cref="AutopilotScenario.HealthFloorWithDrainPercent"/> (30), Lifestone's own numbers; the drains get the band between.
    /// </summary>
    public double HealthFloorPercent => DrainUnderPercent <= 0
        ? AutopilotScenario.HealthFloorPercent
        : Math.Min(AutopilotScenario.HealthFloorWithDrainPercent, DrainUnderPercent * 0.6);

    /// <summary>True when any stop rule is set (a run without one goes on until a stop request).</summary>
    public bool HasStopRule => Minutes > 0 || Level > 0;

    /// <summary>Parses the <c>--autopilot-*</c> options; other arguments are ignored.</summary>
    /// <param name="args">The user arguments.</param>
    public static AutopilotOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var all = args as IReadOnlyList<CommandLineArgument> ?? [.. args];
        var fight = Scenarios.FightOptions.FromArguments(all); // M5 7 F23: the guard's line is the fight's; the fight reports a bad value
        var reach = Scenarios.DeathOptions.FromArguments(all).TripAggroMetres;
        var o = new AutopilotOptions
        {
            EmergencyFloorPercent = fight.EmergencyFloorPercent,
            EmergencySafePercent = fight.EmergencySafePercent,
            EmergencyMarginPercent = fight.EmergencyMarginPercent,
            HealDamageWeight = fight.HealDamageWeight,
            AggroReachMetres = reach,
        };
        var errors = new List<string>();
        foreach (var argument in all)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--autopilot-minutes":
                    var minutes = OptionValues.Double(argument, errors, o.Minutes);
                    if (errors.Count == known && minutes is >= 0 and <= MaxMinutes)
                    {
                        o = o with { Minutes = minutes };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxMinutes:0} minutes"));
                    }

                    break;
                case "--autopilot-level":
                    var level = OptionValues.Int(argument, errors, o.Level);
                    if (errors.Count == known && level is >= 0 and <= MaxLevel)
                    {
                        o = o with { Level = level };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxLevel}"));
                    }

                    break;
                case "--autopilot-trip-after-kills":
                    var kills = OptionValues.Int(argument, errors, o.TripAfterKills);
                    if (errors.Count == known && kills is >= 0 and <= MaxTripAfterKills)
                    {
                        o = o with { TripAfterKills = kills };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxTripAfterKills}"));
                    }

                    break;
                case "--autopilot-watchdog-scale":
                    var scale = OptionValues.Double(argument, errors, o.WatchdogScale);
                    if (errors.Count == known && scale is >= MinWatchdogScale and <= MaxWatchdogScale)
                    {
                        o = o with { WatchdogScale = scale };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not {MinWatchdogScale} to {MaxWatchdogScale}"));
                    }

                    break;
                case "--autopilot-watchdog-recoveries":
                    var recoveries = OptionValues.Int(argument, errors, o.WatchdogRecoveries);
                    if (errors.Count == known && recoveries is >= 0 and <= MaxWatchdogRecoveries)
                    {
                        o = o with { WatchdogRecoveries = recoveries };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 to {MaxWatchdogRecoveries}"));
                    }

                    break;
                case "--autopilot-reconnect-waits":
                    if (ParseWaits(argument.Value) is { } waits)
                    {
                        o = o with { ReconnectWaits = waits };
                    }
                    else
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 1 to {MaxReconnectTries} comma-separated waits of 0 to {MaxReconnectWaitSeconds:0} seconds"));
                    }

                    break;
                case "--fight-drain-under":
                    var drainUnder = OptionValues.Int(argument, new List<string>(), o.DrainUnderPercent); // the fight reports a bad value; here a bad one keeps the default
                    if (drainUnder is >= 0 and <= 100)
                    {
                        o = o with { DrainUnderPercent = drainUnder };
                    }

                    break;
                case "--autopilot-test-death":
                    o = o with { TestDeath = OptionValues.Switch(argument, errors, o.TestDeath) };
                    break;
            }
        }

        return o with { Errors = errors };
    }

    private static double[]? ParseWaits(string? text)
    {
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > MaxReconnectTries)
        {
            return null;
        }

        var waits = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.IsFinite(w) || w < 0 || w > MaxReconnectWaitSeconds)
            {
                return null;
            }

            waits[i] = w;
        }

        return waits;
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"minutes={(Minutes > 0 ? Minutes.ToString("0.#", CultureInfo.InvariantCulture) : "off")} level={(Level > 0 ? Level.ToString(CultureInfo.InvariantCulture) : "off")} trip_after_kills={(TripAfterKills > 0 ? TripAfterKills.ToString(CultureInfo.InvariantCulture) : "off")} test_death={(TestDeath ? "on" : "off")}");

    /// <summary>The watchdogs' and the reconnect's settings as the run logs them (after <see cref="ToString"/>), in invariant culture.</summary>
    public string Resilience => string.Create(
        CultureInfo.InvariantCulture,
        $"watchdog_scale={WatchdogScale:0.##} watchdog_recoveries={WatchdogRecoveries} reconnect_waits={string.Join(',', ReconnectWaits.Select(w => w.ToString("0.#", CultureInfo.InvariantCulture)))}");
}
