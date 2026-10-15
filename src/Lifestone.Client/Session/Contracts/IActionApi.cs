namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Every action a human or a bot can take in a session (A0's Action API, automation-vision.md §1.3): the retail
/// screens, the portal bridge, the tools panel, the scenarios and the automation engines all go through it, so the UI
/// and a bot take the same path (R2). Built by steps ST9 to ST11.
/// </summary>
/// <remarks>
/// <para><b>Handles.</b> Each call returns an <see cref="ActionHandle"/> at once and never blocks or throws for a
/// rejected request: a wrong phase, a bad argument or an offline session completes the handle with
/// <see cref="ActionOutcomeKind.Refused"/>, and an action that is not built yet with
/// <see cref="ActionOutcomeKind.NotImplemented"/> (a loud stub, logged as an ERROR). A handle completes on the server's
/// confirmation, the server's error or its timeout, inside <c>GameSession.Tick</c> (docs/session.md, "Actions").</para>
/// <para><b>Order and limits.</b> Session-phase actions (login, character operations, entering the world, logoff,
/// quit) run one at a time, in call order. Chat, use and tool requests are rate-limited, and wait in the queue rather
/// than being dropped. The defaults are in docs/session.md (owner decision D49).</para>
/// <para><b>Safety.</b> <see cref="KillSwitch"/> and <see cref="DryRun"/> apply to <see cref="ActionSource.Automation"/>
/// only. Automation actions also need a loopback server (R1); an offline session refuses every server action.</para>
/// <para>Game thread only.</para>
/// </remarks>
public interface IActionApi
{
    /// <summary>
    /// Logs in with the account the session was created for (the host chose it; no secret passes through here). Completes
    /// when the data check is complete and the character list is in; fails on a boot, a data mismatch, a character error
    /// after its retries, or a timeout.
    /// </summary>
    ActionHandle Login(ActionSource source = ActionSource.Host);

    /// <summary>
    /// Selects a character in the list, or none with 0 (<see cref="CharacterListState.SelectedGuid"/>). Local: no message is
    /// sent; completes at once with <see cref="ActionOutcomeKind.Local"/>.
    /// </summary>
    ActionHandle SelectCharacter(uint guid, ActionSource source);

    /// <summary>
    /// Creates a character (F656). Refused before sending unless the intent validates
    /// (<see cref="CharacterCreateIntent.TryValidate"/>: exactly 55 skills, a printable-ASCII name). Completes on F643
    /// <c>Ok</c>, or with <see cref="ActionOutcomeKind.ServerError"/> and the CharGenResult code.
    /// </summary>
    ActionHandle CreateCharacter(CharacterCreateIntent intent, ActionSource source);

    /// <summary>
    /// Deletes a character (F655 with its index in the server's list, which this finds and range-checks; ACE does not).
    /// Refused for an unknown character or one already pending deletion. Completes on the F655 acknowledgement and the
    /// list that follows it.
    /// </summary>
    ActionHandle DeleteCharacter(uint guid, ActionSource source);

    /// <summary>Restores a character pending deletion (F7D9). Completes on F643 for that character.</summary>
    ActionHandle RestoreCharacter(uint guid, ActionSource source);

    /// <summary>
    /// Enters the world with a character (F7C8, then F657 when F7DF arrives). Refused for a character pending deletion
    /// (retail ghosts Enter). Completes when F746 PlayerCreate and the player's CreateObject have arrived.
    /// </summary>
    ActionHandle EnterWorld(uint guid, ActionSource source);

    /// <summary>
    /// Sends LoginComplete (GameAction 0x00A1) when portal space ends, as retail's
    /// <c>CPlayerSystem::SendLoginCompleteNotification</c> does (11.4186 0x00562E90, 11.6096 0x00563C30). Allowed only while
    /// the player is <see cref="SelfTravel.Arriving"/> or <see cref="SelfTravel.TeleportArrived"/>, and once per arrival: a
    /// second call is refused loudly. Completes when the server clears Hidden (<see cref="SelfTravel.Present"/>).
    /// </summary>
    ActionHandle LoginComplete(ActionSource source = ActionSource.Client);

    /// <summary>Uses an object (GameAction 0x0036). Completes on GameEvent 0x01C7 UseDone: code 0 confirms, any other is a server error.</summary>
    ActionHandle Use(uint guid, ActionSource source);

    /// <summary>Asks for an object's health (GameAction 0x01BF). Completes on GameEvent 0x01C0 UpdateHealth for it.</summary>
    ActionHandle QueryHealth(uint guid, ActionSource source);

    /// <summary>Says <paramref name="text"/> aloud (GameAction 0x0015). Completes on the server's echo of the player's own speech.</summary>
    ActionHandle Talk(string text, ActionSource source);

    /// <summary>
    /// Tells <paramref name="text"/> to the character named <paramref name="target"/> (GameAction 0x005D). Completes on the
    /// server's confirmation line, or with a server error when the character is not available.
    /// </summary>
    ActionHandle Tell(string target, string text, ActionSource source);

    /// <summary>
    /// Logs the character off: adds "Logging off..." to the chat as retail does, sends F653 (the only sender), and
    /// publishes <see cref="LogOffRequested"/> for portal space. Completes when the server's F653 and the character list
    /// have arrived.
    /// </summary>
    ActionHandle LogOff(ActionSource source);

    /// <summary>
    /// Changes one of the character's option bits for this session only (the options panel; D42): the reducer applies it as a session-local override over the PlayerDescription's words
    /// (<see cref="PlayerQualities.CharacterOptions1"/> and <see cref="PlayerQualities.CharacterOptions2"/>), publishes <see cref="QualitiesChanged"/> (<see cref="QualityChange.Options"/>)
    /// and records it. Nothing goes to the server (<c>TODO(g6-options-save)</c>); it applies at once and is not a handle. Game thread only.
    /// </summary>
    /// <param name="word">1 or 2: which option word.</param>
    /// <param name="mask">The option's bit in that word.</param>
    /// <param name="value">Whether the option is on.</param>
    void SetSessionOption(int word, uint mask, bool value);

    /// <summary>
    /// Ends the session: in the world F653 then a Disconnect at once, otherwise the Disconnect only (DoD 11). Completes
    /// with <see cref="ActionOutcomeKind.Sent"/>, since ACE never answers a Disconnect.
    /// </summary>
    ActionHandle Quit(ActionSource source);

    /// <summary>
    /// Where the player's movement controller (physics) sends MoveToState, AutonomousPosition and Jump. The sink stamps
    /// each with the player's current sequences; it has no handles and allocates nothing per send.
    /// </summary>
    IMovementSink Movement { get; }

    /// <summary>The automation path into the same movement controller the keyboard drives (R2).</summary>
    MoveIntents Move { get; }

    /// <summary>
    /// True refuses every new <see cref="ActionSource.Automation"/> action and cancels the queued ones
    /// (<see cref="ActionOutcomeKind.Cancelled"/>). Other sources are unaffected.
    /// </summary>
    bool KillSwitch { get; set; }

    /// <summary>
    /// True logs <see cref="ActionSource.Automation"/> actions with what they would have sent and completes them with
    /// <see cref="ActionOutcomeKind.DryRun"/>; nothing is sent. Other sources are unaffected.
    /// </summary>
    bool DryRun { get; set; }
}
