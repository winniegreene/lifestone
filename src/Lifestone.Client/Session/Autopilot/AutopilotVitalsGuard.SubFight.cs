namespace Lifestone.Client.Session.Autopilot;

/// <summary>M6 16 E3.0: what the vitals guard holds now, for the one line of the guard over a quest's sub-fight (<c>AutopilotScenario.SubFightShadow.cs</c>, report-only). Reads only.</summary>
internal sealed partial class AutopilotVitalsGuard
{
    /// <summary>The guard's emergency line as it last worked it out, in health points (0 before the first).</summary>
    public double LineHealth => _emergency.LineHealth;

    /// <summary>What the guard is doing while it holds: its own cast in flight by name (and at whom), or its hold before a cast (the lanes it waits for, its pick, a retry's wait).</summary>
    public string HoldWords => CastRunning
        ? "casts " + _drain.LastName + (_drain.IsHeal ? string.Empty : $" at 0x{_drain.TargetGuid:X8}")
        : "holds the station for its heal pick";
}
