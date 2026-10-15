namespace Lifestone.Net.Session;

/// <summary>Where a session is in the flow of docs/protocol.md §4.</summary>
public enum SessionPhase
{
    /// <summary>Created; <see cref="IAcSession.Login"/> not called yet. No socket exists.</summary>
    Idle,

    /// <summary>LoginRequest sent; waiting for ConnectRequest (retries on a lost one, D18).</summary>
    Connecting,

    /// <summary>ConnectResponse being sent (200 ms delay, then every 0.333 s) until the first encrypted server packet.</summary>
    Handshaking,

    /// <summary>The DDD data check (F7E5 answered with F7E6, waiting for F7EA).</summary>
    DataCheck,

    /// <summary>Character select: F7EA and F658 both received. Character operations and EnterWorld are allowed.</summary>
    CharacterSelect,

    /// <summary>EnterWorld sent (F7C8 → F7DF → F657); waiting for PlayerCreate.</summary>
    EnteringWorld,

    /// <summary>In the world: PlayerCreate received.</summary>
    InWorld,

    /// <summary>CharacterLogOff sent; waiting for the server's F653 and F658 (back to character select).</summary>
    LoggingOff,

    /// <summary>Quit: the final messages and the Disconnect are being sent.</summary>
    Closing,

    /// <summary>Closed normally (quit or disposed).</summary>
    Closed,

    /// <summary>Failed; <see cref="IAcSession.Failure"/> says why.</summary>
    Failed,
}
