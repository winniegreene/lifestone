using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The session's read model (A0's GameState, automation-vision.md §2): what the server has confirmed, organised for the
/// screens, the world layers and automation (R3: conditions read server-confirmed state only). Only the session's
/// reducer changes it, inside <c>GameSession.Tick</c>, and every change is followed by an event on
/// <see cref="IGameSessionView.Events"/>. Built by steps ST7 (login phase) and ST8 (world phase).
/// </summary>
/// <remarks>Game thread only. The objects it hands out are the reducer's own: read them, never keep a reference across
/// a reconnect (<c>GameSession.Reconnect</c> resets the state).</remarks>
public sealed class GameState
{
    /// <summary>An empty state, before login. Only the session creates one.</summary>
    internal GameState()
    {
    }

    /// <summary>The session's phase (the network session's phases; an offline session reports the phase of its stand-in data).</summary>
    public SessionPhase Phase { get; internal set; } = SessionPhase.Idle;

    /// <summary>Why the session failed, once <see cref="Phase"/> is <see cref="SessionPhase.Failed"/>; else null.</summary>
    public SessionFailure? Failure { get; internal set; }

    /// <summary>
    /// True when the session ended in failure (<see cref="SessionPhase.Failed"/>: a timeout, a boot, a data mismatch, a lost server). The retail
    /// screens name that in their own terms, a lost connection (<c>gmUIFlow::RecvNotice_ServerDied</c>), so they need not name the network
    /// layer's phases; a clean close is not one.
    /// </summary>
    public bool ConnectionLost => Phase == SessionPhase.Failed;

    /// <summary>Why the session failed, as the network layer words it (for the log; never a secret), or null.</summary>
    public string? FailureMessage => Failure?.Message;

    /// <summary>True for an offline session fed by stand-in data (<c>GameSession.Offline</c>): every server action is refused.</summary>
    public bool IsOffline { get; internal set; }

    /// <summary>The DDD data check's progress, for the retail data-patch screen.</summary>
    public DataCheckState DataCheck { get; internal set; }

    /// <summary>The world's name from F7E1 ServerName, or null before it.</summary>
    public string? WorldName { get; internal set; }

    /// <summary>The account's characters, in server order.</summary>
    public CharacterListState Characters { get; } = new();

    /// <summary>The player's own state.</summary>
    public SelfState Self { get; } = new();

    /// <summary>Every object the server created and has not deleted, the player's own included.</summary>
    public WorldObjectTable Objects { get; } = new();

    /// <summary>The chat lines received and the client's own notices.</summary>
    public ChatLog Chat { get; } = new();

    /// <summary>The server's clock.</summary>
    public ServerTimeState Time { get; } = new();

    /// <summary>Rises by one with every change the reducer applies: a cheap "did anything change" check.</summary>
    public long Version { get; internal set; }
}

/// <summary>Where the DDD data check is (<see cref="GameState.DataCheck"/>; docs/protocol.md §4.2).</summary>
public enum DataCheckState
{
    /// <summary>Not started: the handshake has not reached the data check.</summary>
    NotStarted,

    /// <summary>F7E5 DDD_Interrogation arrived and was answered with the client's iterations; waiting for the server.</summary>
    Checking,

    /// <summary>F7EA DDD_EndDDD: the dats match the server's.</summary>
    Complete,

    /// <summary>F7E7 DDD_BeginDDD: the dats do not match the server's, and the session fails (patching is off).</summary>
    Mismatch,
}
