namespace Lifestone.Client.PortalSpace;

/// <summary>
/// When a requested logoff's tunnel begins (m2-log-in.md §4.8): the timing half of retail
/// <c>CPlayerSystem::RequestLogOff</c> (11.4186 0x00562DD0, 11.6096 0x00563B70) and <c>CPlayerSystem::SetLogOffStarted</c>
/// (11.4186 0x0055D7D0, 11.6096 0x0055E4F0). The state machine owns one (<see cref="PortalSpaceMachine.LogOff"/>).
/// </summary>
/// <remarks>
/// <para><b>Timing only.</b> Retail's request also prints "Logging off..." to the chat and sends CharacterLogOff at once;
/// in Lifestone the session layer does both (it is the only sender of CharacterLogOff) and then tells the machine.</para>
/// <para><b>The time.</b> The logoff transition is due 3 s after the request (retail's <c>logOffRequestTime</c>), and
/// <see cref="PortalSpaceMachine.Update"/> begins it on the first frame strictly after that. Retail adds 20 s when a
/// virtual check on the player's weenie object holds (vtable slot 7), probably player-killer status. That check is
/// unverified and M2 has no player killers, so the extra delay is off (D33): a request that asks for it is recorded in
/// <see cref="PlayerKillerDelayIgnored"/> and logged by the machine as a TODO, and the 3 s stand.</para>
/// <para>Allocation-free; frame thread only.</para>
/// </remarks>
public sealed class LogOffSchedule
{
    /// <summary>Seconds from the request to the logoff transition (retail's literal 3.0).</summary>
    public const double Delay = 3.0;

    /// <summary>Retail's extra seconds when the weenie check holds (literal 20.0); not applied (D33).</summary>
    public const double PlayerKillerExtraDelay = 20.0;

    /// <summary>A logoff is requested and its transition has not begun (retail <c>CPlayerSystem::logOffRequested</c>).</summary>
    public bool Requested { get; private set; }

    /// <summary>When the transition is due (retail <c>logOffRequestTime</c>): request time + <see cref="Delay"/>.</summary>
    public double DueTime { get; private set; }

    /// <summary>The last request asked for the player-killer delay, which was not applied (D33).</summary>
    public bool PlayerKillerDelayIgnored { get; private set; }

    /// <summary>Schedules the transition <see cref="Delay"/> seconds after <paramref name="now"/>.</summary>
    /// <param name="now">The request time, in the machine's clock.</param>
    /// <param name="playerKillerDelay">Retail's weenie check held; recorded, not applied (D33).</param>
    public void Request(double now, bool playerKillerDelay)
    {
        Requested = true;
        DueTime = now + Delay;
        PlayerKillerDelayIgnored = playerKillerDelay;
    }

    /// <summary>The transition is due at <paramref name="now"/>: requested, and the due time strictly passed.</summary>
    public bool IsDue(double now) => Requested && DueTime < now;

    /// <summary>The transition began (<c>CPlayerSystem::SetLogOffStarted</c>): the request is spent.</summary>
    public void Start() => Requested = false;

    /// <summary>Forgets any request (the machine's reset).</summary>
    public void Clear()
    {
        Requested = false;
        DueTime = 0.0;
        PlayerKillerDelayIgnored = false;
    }
}
