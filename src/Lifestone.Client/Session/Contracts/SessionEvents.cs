using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The session's phase changed (<see cref="GameState.Phase"/>). The phases are the network session's
/// (<see cref="SessionPhase"/>); an offline stand-in session reports the phase its stand-in data implies.
/// </summary>
/// <param name="Previous">The phase before.</param>
/// <param name="Phase">The phase now.</param>
/// <param name="Failure">Why, when <paramref name="Phase"/> is <see cref="SessionPhase.Failed"/>; else null.</param>
public readonly record struct SessionPhaseChanged(SessionPhase Previous, SessionPhase Phase, SessionFailure? Failure) : IGameEvent;

/// <summary>How far a login has got, for the connecting and data-check screens.</summary>
/// <param name="Stage">The step reached.</param>
/// <param name="Attempt">The login attempt (1 for the first; a retry raises it).</param>
/// <param name="Delay">For <see cref="ConnectionStage.Retrying"/>, the wait before the next attempt.</param>
/// <param name="Code">For <see cref="ConnectionStage.Retrying"/> after a CharacterError, its code; else 0.</param>
/// <param name="Detail">A short description for the log and the screen, or null.</param>
public readonly record struct ConnectionProgress(ConnectionStage Stage, int Attempt, TimeSpan Delay, uint Code, string? Detail) : IGameEvent;

/// <summary>The steps a <see cref="ConnectionProgress"/> reports (net's session events, docs/protocol.md §4).</summary>
public enum ConnectionStage
{
    /// <summary>LoginRequest sent; waiting for ConnectRequest.</summary>
    Connecting,

    /// <summary>ConnectRequest arrived; the handshake is running.</summary>
    Handshaking,

    /// <summary>The attempt failed in a way the retry policy allows; another one is scheduled.</summary>
    Retrying,

    /// <summary>The DDD data check started (F7E5 answered with F7E6).</summary>
    DataCheckStarted,

    /// <summary>The data check completed (F7EA).</summary>
    DataCheckComplete,
}

/// <summary>The character list changed (<see cref="GameState.Characters"/>).</summary>
/// <param name="Version">The list's new <see cref="CharacterListState.Version"/>.</param>
/// <param name="Reason">What changed it.</param>
public readonly record struct CharacterListChanged(int Version, CharacterListReason Reason) : IGameEvent;

/// <summary>Why the character list changed.</summary>
public enum CharacterListReason
{
    /// <summary>An F658 CharacterList arrived: the list was replaced in server order.</summary>
    Received,

    /// <summary>An F643 Ok answered a create: the character was appended locally (ACE does not resend F658).</summary>
    Created,

    /// <summary>The F655 delete acknowledgement arrived; the next F658 shows the character pending deletion.</summary>
    DeleteAcknowledged,

    /// <summary>An F643 Ok answered a restore: the character is no longer pending deletion.</summary>
    Restored,
}

/// <summary>The selected character changed (a local action: <see cref="IActionApi.SelectCharacter"/>).</summary>
/// <param name="Guid">The selected character, or 0 for none.</param>
public readonly record struct CharacterSelected(uint Guid) : IGameEvent;

/// <summary>An F643 CharacterCreateResponse answered a create or a restore.</summary>
/// <param name="Result">The server's result (<see cref="CharGenResult.Ok"/> on success).</param>
/// <param name="Guid">The character, when the server gave one.</param>
/// <param name="Name">The character's name, when the server gave one.</param>
/// <param name="Restore">True when it answered a CharacterRestore (F7D9) rather than a create.</param>
public readonly record struct CharacterCreateResult(CharGenResult Result, uint Guid, string? Name, bool Restore) : IGameEvent
{
    /// <summary>The server's result as its raw code (<see cref="CharGenResult"/>: 1 is Ok), for the screens that name results by code.</summary>
    public uint ResultCode => (uint)Result;

    /// <summary>True when the server accepted the create or restore (<see cref="CharGenResult.Ok"/>).</summary>
    public bool Succeeded => Result == CharGenResult.Ok;
}

/// <summary>An F659 CharacterError arrived (the codes are listed by <see cref="CharacterErrorCode"/>).</summary>
/// <param name="Code">The code, raw.</param>
public readonly record struct CharacterErrorReceived(uint Code) : IGameEvent
{
    /// <summary>The code as a known <see cref="CharacterErrorCode"/>, or null for an unlisted one.</summary>
    public CharacterErrorCode? Known => new CharacterErrorMsg(Code).Known;
}

/// <summary>The server ended the account's session: F7DC AccountBoot, or F7C1 AccountBanned.</summary>
/// <param name="Reason">The server's reason when it gave one (it may start with a space), else null.</param>
/// <param name="Banned">True for F7C1 AccountBanned.</param>
public readonly record struct Booted(string? Reason, bool Banned) : IGameEvent;

/// <summary>F746 PlayerCreate: the player's object id is known (<see cref="SelfState.Guid"/>); its CreateObject follows.</summary>
/// <param name="Guid">The player's object id.</param>
public readonly record struct PlayerCreated(uint Guid) : IGameEvent;

/// <summary>The server's time was updated (ConnectRequest or TimeSync; <see cref="GameState.Time"/>).</summary>
/// <param name="ServerSeconds">The server's time: the portal-year clock, in seconds.</param>
/// <param name="ClientSeconds">When it was received, in <see cref="SessionClock"/> seconds.</param>
public readonly record struct ServerTimeUpdated(double ServerSeconds, double ClientSeconds) : IGameEvent;

/// <summary>
/// The player asked to log off and the F653 CharacterLogOff was sent (<see cref="IActionApi.LogOff"/>, the only sender):
/// portal space plays the logoff transition from here (the portal bridge forwards it to its machine's
/// <c>RequestLogOff</c>, retail <c>CPlayerSystem::RequestLogOff</c>, 11.4186 0x00562DD0, 11.6096 0x00563B70).
/// </summary>
/// <param name="Now">The frame time of the tick that sent it (the host's frame clock, as <c>GameSession.Tick</c> received it).</param>
/// <param name="PlayerKillerDelay">True when retail would add its player-killer delay; false in M2 (D33).</param>
public readonly record struct LogOffRequested(double Now, bool PlayerKillerDelay) : IGameEvent;

/// <summary>
/// A server message was not applied. Published for every drop (it costs nothing without subscribers), so the inspector
/// and the tests can count them; retail drops the same messages silently.
/// </summary>
/// <param name="Opcode">The message's opcode.</param>
/// <param name="Guid">The object it was for, or 0.</param>
/// <param name="Reason">Why it was dropped.</param>
public readonly record struct MessageDropped(uint Opcode, uint Guid, DropReason Reason) : IGameEvent;

/// <summary>Why a <see cref="MessageDropped"/> happened.</summary>
public enum DropReason
{
    /// <summary>The message is for an older instance of the object (its instance sequence is older).</summary>
    OlderInstance,

    /// <summary>
    /// Its sequence is not newer than the one already applied (for a position update whose teleport sequence is newer,
    /// retail also raises its error count; the reducer counts those).
    /// </summary>
    Stale,

    /// <summary>
    /// A position update with a newer position sequence but an older teleport sequence: retail puts the position
    /// sequence back and ignores the update (<c>SmartBox::HandleReceivedPosition</c>).
    /// </summary>
    StaleTeleport,

    /// <summary>A message for an unknown object was parked, and the parking bounds were full.</summary>
    ParkingFull,

    /// <summary>A parked message expired before its object was created.</summary>
    ParkingExpired,

    /// <summary>A DeleteObject for the player's own object, which retail ignores (<c>SmartBox::HandleDeleteObject</c>).</summary>
    SelfDelete,

    /// <summary>The body could not be parsed (net's dispatcher reported it malformed).</summary>
    Malformed,
}

/// <summary>A message (or a game event) that the network layer does not decode; nothing was applied.</summary>
/// <param name="Opcode">The message's opcode (0xF7B0 for a game event).</param>
/// <param name="EventType">For a game event, its event type; else 0.</param>
public readonly record struct UnknownMessage(uint Opcode, uint EventType) : IGameEvent;
