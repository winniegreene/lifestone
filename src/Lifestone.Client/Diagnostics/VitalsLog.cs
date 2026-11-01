using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The vitals on a timer (M5 item 7 F17, <c>--log-vitals-s</c>): one Info line <c>vitals: health h/H stamina s/S mana m/M</c> (category <c>vitals</c>) every S seconds. The frame path is one call to
/// <see cref="Step"/>, which compares a clock and allocates nothing until a line is due; the text is built once per interval. The first line is S seconds after the vitals are first known, and a stall of
/// several periods owes one line, not a burst (<see cref="SoakFrameSchedule"/>'s rule). Engine-free.
/// </summary>
public sealed class VitalsLog
{
    /// <summary>The wire ids of the three vitals' maximums (health, stamina, mana), the keys <see cref="PlayerQualities.TryGetVitalLevels"/> takes.</summary>
    public const uint HealthId = 1, StaminaId = 3, ManaId = 5;

    private readonly ILogSink _log;
    private readonly SoakFrameSchedule _schedule;

    /// <summary>Creates the log; <paramref name="periodSeconds"/> of 0 or less is off (<see cref="Step"/> does nothing).</summary>
    public VitalsLog(double periodSeconds, ILogSink log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _schedule = new SoakFrameSchedule(periodSeconds);
    }

    /// <summary>True when the period is positive.</summary>
    public bool Enabled => _schedule.Enabled;

    /// <summary>How many lines were logged.</summary>
    public int Lines => _schedule.Taken;

    /// <summary>
    /// One frame's call. <paramref name="qualities"/> is the player's, or null before there is a player; the countdown starts the first time the vitals are known and goes on from there.
    /// <paramref name="nowSeconds"/> is any monotonic clock.
    /// </summary>
    public void Step(double nowSeconds, PlayerQualities? qualities)
    {
        if (!_schedule.Enabled || qualities is null || !qualities.IsKnown)
        {
            return;
        }

        if (!_schedule.Armed)
        {
            _schedule.Arm(nowSeconds);
            return;
        }

        if (!_schedule.TryTake(nowSeconds, out _) || !_log.IsEnabled(LogSeverity.Info))
        {
            return;
        }

        if (qualities.TryGetVitalLevels(HealthId, out var health, out var healthMax)
            && qualities.TryGetVitalLevels(StaminaId, out var stamina, out var staminaMax)
            && qualities.TryGetVitalLevels(ManaId, out var mana, out var manaMax))
        {
            _log.Info("vitals", Format(health, healthMax, stamina, staminaMax, mana, manaMax));
        }
    }

    /// <summary>The line's text.</summary>
    public static string Format(uint health, uint healthMax, uint stamina, uint staminaMax, uint mana, uint manaMax) =>
        string.Create(CultureInfo.InvariantCulture, $"vitals: health {health}/{healthMax} stamina {stamina}/{staminaMax} mana {mana}/{manaMax}");
}
