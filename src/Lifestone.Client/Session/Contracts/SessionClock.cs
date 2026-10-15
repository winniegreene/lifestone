namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The session's clock (A0's R5): monotonic seconds since the clock was created, read from a <see cref="TimeProvider"/>
/// (<see cref="TimeProvider.System"/> in the hosts, a manual provider in tests and replays). The network session runs
/// on the same provider, so action timeouts, transport timers, handle times, chat times and recording times all read one
/// clock, and a test or a replay that moves the provider moves all of them.
/// </summary>
/// <remarks>
/// These are real seconds, not the speed-scaled world clock (<c>DerethTimeController.ClientClock</c>). They are not the
/// host's frame time either, which frame-stepped machines (portal, physics, avatars) take as <c>now</c> and which the
/// host passes to <c>GameSession.Tick</c>. This file is the session layer's only clock reader
/// (ArchitectureTests: no wall clock in Client/Session).
/// </remarks>
public sealed class SessionClock
{
    /// <summary>A clock starting at 0 now.</summary>
    /// <param name="time">The time source.</param>
    public SessionClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        Time = time;
        StartTimestamp = time.GetTimestamp();
    }

    /// <summary>The time source (handed to the network session as its <c>AcSessionOptions.Time</c>).</summary>
    public TimeProvider Time { get; }

    /// <summary>The provider's timestamp at which this clock reads 0.</summary>
    public long StartTimestamp { get; }

    /// <summary>Seconds since the clock was created.</summary>
    public double Now => ToSeconds(Time.GetTimestamp());

    /// <summary>
    /// A timestamp of <see cref="Time"/> (such as net's <c>InboundContext.ReceivedTimestamp</c> or a session event's
    /// <c>Timestamp</c>) as seconds on this clock.
    /// </summary>
    /// <param name="timestamp">A <see cref="TimeProvider.GetTimestamp"/> value of <see cref="Time"/>.</param>
    /// <returns>Seconds since the clock was created (negative for an earlier timestamp).</returns>
    public double ToSeconds(long timestamp) => (timestamp - StartTimestamp) / (double)Time.TimestampFrequency;
}
