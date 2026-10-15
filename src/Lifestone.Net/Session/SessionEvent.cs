namespace Lifestone.Net.Session;

/// <summary>
/// One event of the session itself (not a server message), passed to <see cref="Dispatch.IAcSessionSink.OnSessionEvent"/>.
/// <see cref="Kind"/> says which fields are meaningful; build one with the factory methods. A struct, so raising one
/// allocates nothing.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Timestamp">When, in the session clock's timestamp units.</param>
/// <param name="Phase">The session's phase after the event.</param>
/// <param name="PreviousPhase">For <see cref="SessionEventKind.PhaseChanged"/>, the phase before.</param>
/// <param name="Code">A CharacterError code (<see cref="SessionEventKind.CharacterErrorReceived"/>,
/// <see cref="SessionEventKind.RetryScheduled"/> when an error caused it), else 0.</param>
/// <param name="Attempt">For <see cref="SessionEventKind.RetryScheduled"/>, the attempt about to start (2 for the first
/// retry).</param>
/// <param name="Delay">For <see cref="SessionEventKind.RetryScheduled"/>, the wait before it.</param>
/// <param name="ServerTime">For <see cref="SessionEventKind.ConnectRequestReceived"/> and
/// <see cref="SessionEventKind.ServerTime"/>, the server's time (the portal-year clock in seconds).</param>
/// <param name="Text">For <see cref="SessionEventKind.Booted"/>, the reason when the server gave one; for
/// <see cref="SessionEventKind.RetryScheduled"/> and <see cref="SessionEventKind.Warning"/>, a short description.</param>
/// <param name="Failure">For <see cref="SessionEventKind.Failed"/>, the failure.</param>
/// <param name="Warning">For <see cref="SessionEventKind.Warning"/>, which warning.</param>
public readonly record struct SessionEvent(
    SessionEventKind Kind,
    long Timestamp,
    SessionPhase Phase,
    SessionPhase PreviousPhase = SessionPhase.Idle,
    uint Code = 0,
    int Attempt = 0,
    TimeSpan Delay = default,
    double ServerTime = 0,
    string? Text = null,
    SessionFailure? Failure = null,
    SessionWarning Warning = SessionWarning.None)
{
    /// <summary>The phase changed from <paramref name="previous"/> to <paramref name="phase"/>.</summary>
    public static SessionEvent PhaseChanged(long timestamp, SessionPhase previous, SessionPhase phase) =>
        new(SessionEventKind.PhaseChanged, timestamp, phase, PreviousPhase: previous);

    /// <summary>ConnectRequest arrived, carrying the server's time.</summary>
    public static SessionEvent ConnectRequestReceived(long timestamp, SessionPhase phase, double serverTime) =>
        new(SessionEventKind.ConnectRequestReceived, timestamp, phase, ServerTime: serverTime);

    /// <summary>The DDD data check started (F7E5 answered).</summary>
    public static SessionEvent DataCheckStarted(long timestamp, SessionPhase phase) =>
        new(SessionEventKind.DataCheckStarted, timestamp, phase);

    /// <summary>The DDD data check completed (F7EA).</summary>
    public static SessionEvent DataCheckComplete(long timestamp, SessionPhase phase) =>
        new(SessionEventKind.DataCheckComplete, timestamp, phase);

    /// <summary>Another login attempt is scheduled.</summary>
    public static SessionEvent RetryScheduled(long timestamp, SessionPhase phase, int attempt, TimeSpan delay, uint code, string reason) =>
        new(SessionEventKind.RetryScheduled, timestamp, phase, Code: code, Attempt: attempt, Delay: delay, Text: reason);

    /// <summary>The server booted the session (F7DC), with its reason when it gave one.</summary>
    public static SessionEvent Booted(long timestamp, SessionPhase phase, string? reason) =>
        new(SessionEventKind.Booted, timestamp, phase, Text: reason);

    /// <summary>A CharacterError arrived in <paramref name="phase"/>.</summary>
    public static SessionEvent CharacterErrorReceived(long timestamp, SessionPhase phase, uint code) =>
        new(SessionEventKind.CharacterErrorReceived, timestamp, phase, Code: code);

    /// <summary>A TimeSync gave the server's time.</summary>
    public static SessionEvent ServerTimeUpdated(long timestamp, SessionPhase phase, double serverTime) =>
        new(SessionEventKind.ServerTime, timestamp, phase, ServerTime: serverTime);

    /// <summary>Logoff completed: the server's F653 and F658 arrived; the session is back at character select.</summary>
    public static SessionEvent LoggedOff(long timestamp, SessionPhase phase) =>
        new(SessionEventKind.LoggedOff, timestamp, phase);

    /// <summary>The session failed.</summary>
    public static SessionEvent Failed(long timestamp, SessionFailure failure) =>
        new(SessionEventKind.Failed, timestamp, SessionPhase.Failed, Failure: failure);

    /// <summary>Something unexpected that the session survived.</summary>
    public static SessionEvent Warned(long timestamp, SessionPhase phase, SessionWarning warning, string description) =>
        new(SessionEventKind.Warning, timestamp, phase, Text: description, Warning: warning);
}

/// <summary>The kinds of <see cref="SessionEvent"/>.</summary>
public enum SessionEventKind : byte
{
    /// <summary>The phase changed.</summary>
    PhaseChanged,

    /// <summary>ConnectRequest arrived.</summary>
    ConnectRequestReceived,

    /// <summary>The data check started.</summary>
    DataCheckStarted,

    /// <summary>The data check completed.</summary>
    DataCheckComplete,

    /// <summary>Another login attempt is scheduled.</summary>
    RetryScheduled,

    /// <summary>The server booted the session.</summary>
    Booted,

    /// <summary>A CharacterError arrived.</summary>
    CharacterErrorReceived,

    /// <summary>The server's time was updated.</summary>
    ServerTime,

    /// <summary>Logoff completed.</summary>
    LoggedOff,

    /// <summary>The session failed.</summary>
    Failed,

    /// <summary>A warning.</summary>
    Warning,
}

/// <summary>The warnings a <see cref="SessionEvent"/> can carry. More may be added.</summary>
public enum SessionWarning : byte
{
    /// <summary>Not a warning.</summary>
    None,

    /// <summary>A message could not be parsed and was skipped.</summary>
    MalformedMessage,

    /// <summary>A message arrived that the current phase does not expect; it was dispatched anyway.</summary>
    UnexpectedMessage,

    /// <summary>The inbound sequencer gave up on a missing message and skipped the gap.</summary>
    GapSkipped,

    /// <summary>A NameInUse answer to a create was a duplicate and was folded into the first.</summary>
    DuplicateAnswerFolded,

    /// <summary>A character create, delete or restore got no answer within <see cref="SessionTimeouts.CharacterOperation"/>;
    /// the session stopped waiting (N6).</summary>
    CharacterOperationTimedOut,

    /// <summary>LoginComplete was sent again with no PlayerCreate, self ObjectCreate or PlayerTeleport since the last one (N6).</summary>
    LoginCompleteRepeated,
}
