namespace Lifestone.Net.Session;

/// <summary>
/// The session's timeouts, all measured on <see cref="AcSessionOptions.Time"/>. <see cref="Default"/> is Lifestone's
/// (owner decision D18); <see cref="Retail"/> restores retail's 140 s silence limit for <c>--retail-net</c>.
/// </summary>
public sealed record SessionTimeouts
{
    /// <summary>Lifestone's timeouts.</summary>
    public static SessionTimeouts Default { get; } = new();

    /// <summary>Retail's: the same, except a 140 s silence limit.</summary>
    public static SessionTimeouts Retail { get; } = new() { Silence = TimeSpan.FromSeconds(140) };

    /// <summary>How long to wait for ConnectRequest after LoginRequest before the attempt counts as lost (5 s).</summary>
    public TimeSpan ConnectRequest { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The delay before the first ConnectResponse (200 ms). ACE ignores one that arrives before it has finished its own
    /// login bookkeeping (docs/protocol.md §4.1, the handshake race).
    /// </summary>
    public TimeSpan ConnectResponseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The interval between ConnectResponse resends until the first encrypted server packet (0.333 s).</summary>
    public TimeSpan ConnectResponseInterval { get; init; } = TimeSpan.FromSeconds(0.333);

    /// <summary>The cap on the whole handshake, from ConnectRequest to the first encrypted server packet (10 s).</summary>
    public TimeSpan Handshake { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The cap on the DDD data check, from the first encrypted server packet until F7EA and F658 (15 s).</summary>
    public TimeSpan DataCheck { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The wait for the answer to a character create, delete or restore (15 s). When it runs out the session stops
    /// waiting, raises a <see cref="SessionWarning.CharacterOperationTimedOut"/> warning and stays in character select.
    /// </summary>
    public TimeSpan CharacterOperation { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The cap on entering the world, until PlayerCreate (30 s).</summary>
    public TimeSpan EnterWorld { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The cap on logging off, until the server's F653 and F658 (30 s; ACE sends them after about 6 s).</summary>
    public TimeSpan Logoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The silence watchdog: the session fails when nothing arrives for this long while <see cref="IAcSession.Pump"/> kept
    /// running (20 s; retail 140 s). A host that stops pumping (a debugger pause) does not trip it.
    /// </summary>
    public TimeSpan Silence { get; init; } = TimeSpan.FromSeconds(20);
}
