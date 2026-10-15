using Lifestone.Client.Session;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The clock of a scripted retail-UI run: it reads the frame time the layer was last stepped to, so the offline session's data check, its
/// answers and the dialogs' countdowns advance with the frames instead of real time (<c>--retail-ui-checks</c>, the frames runner).
/// </summary>
public sealed class FrameTimeProvider : TimeProvider
{
    private const long Frequency = 1_000_000;
    private long _timestamp;

    /// <inheritdoc/>
    public override long TimestampFrequency => Frequency;

    /// <summary>The frame time in seconds.</summary>
    public double Seconds
    {
        get => _timestamp / (double)Frequency;
        set => _timestamp = (long)(value * Frequency);
    }

    /// <inheritdoc/>
    public override long GetTimestamp() => _timestamp;
}

/// <summary>
/// The offline stand-in session the retail screens run on while no live session exists (<c>--retail-ui=standin</c>; m2-plan G2 U3): a
/// <see cref="GameSession"/> fed by <c>StandInSessions</c> (Lifestone-authored character names, nothing from the dats or a server) through the
/// same reducer a live session uses, with the stand-in responder on so delete, restore and create can be clicked through. The screens see only
/// <c>IGameSessionView</c>; with <c>--play</c> the layer is given the live session of <c>SessionHost</c> instead, and this stand-in is not made (it is made by <c>SessionHost.CreateStandIn</c>).
/// </summary>
public sealed class RetailUiOfflineSession : IDisposable
{
    private readonly FrameTimeProvider? _frameTime;

    /// <summary>Wraps the stand-in session <c>SessionHost.CreateStandIn</c> made (the only place a session is created, m2-plan G6 W9) and its scripted clock, or null for real time.</summary>
    internal RetailUiOfflineSession(GameSession session, FrameTimeProvider? frameTime)
    {
        Session = session;
        _frameTime = frameTime;
    }

    /// <summary>The session.</summary>
    public GameSession Session { get; }

    /// <summary>Runs the session for the frame at <paramref name="now"/> seconds (the layer's frame clock).</summary>
    public void Step(double now)
    {
        if (_frameTime is not null)
        {
            _frameTime.Seconds = now;
        }

        Session.Tick(now);
    }

    /// <summary>Resets the session and runs its stand-in again, as a reconnect does (D43).</summary>
    public void Reconnect() => Session.Reconnect();

    /// <inheritdoc/>
    public void Dispose() => Session.Dispose();
}
