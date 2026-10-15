using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Play;

/// <summary>Where a play-mode run is, from the session's events (the placeholder status line shows it; <see cref="PlayPresentationFlow.StatusText"/>).</summary>
public enum PlayStage
{
    /// <summary>Not logged in yet: connecting, the handshake.</summary>
    Connecting,

    /// <summary>The retail data check (the DDD exchange).</summary>
    DataCheck,

    /// <summary>The character list (character management, character creation).</summary>
    CharacterList,

    /// <summary>EnterWorld was sent; the server has not created the player yet.</summary>
    Entering,

    /// <summary>The player's CreateObject arrived: the world is built around it and portal space plays, until the server unhides the player.</summary>
    Arriving,

    /// <summary>The player is in the world (the server cleared Hidden).</summary>
    InWorld,

    /// <summary>Retail's logoff sequence is running.</summary>
    LoggingOff,

    /// <summary>The session failed, or the server booted the player: the retail disconnected screen has the reason.</summary>
    Lost,
}

/// <summary>Why the player left the world.</summary>
public enum PlayLeaveReason
{
    /// <summary>A logoff, or the client's own quit: back to the character list, or the end of the session.</summary>
    LoggedOff,

    /// <summary>The session failed (a stopped server, a timeout, a protocol failure).</summary>
    ConnectionLost,

    /// <summary>The server booted the player (an account boot or a ban).</summary>
    Booted,
}

/// <summary>
/// What play mode does in the world, as the Godot host (<c>scripts/Session/PlayMode.cs</c>) implements it and the tests fake it. Every call is made
/// on the game thread from inside the session's tick, after the state already changed; a host that has heavy work (building the player) queues it
/// for its own frame.
/// </summary>
public interface IPlayWorld
{
    /// <summary>
    /// The player's own CreateObject arrived (or was re-created by F7DB: <paramref name="recreated"/>). Build the player, start the world around it, and
    /// bring the UI to the game screen. <paramref name="player"/> holds the server's description, position, state and look.
    /// </summary>
    void PlayerArriving(WorldObject player, bool recreated);

    /// <summary>The server cleared Hidden: the player is in the world (the portal sequence's end).</summary>
    void PlayerPresent();

    /// <summary>
    /// A server teleport put the player at <paramref name="move"/> (<see cref="PositionVerdict.SelfTeleport"/>, after F751): move the world, the
    /// interiors, the physics and the camera.
    /// </summary>
    void PlayerTeleported(in ObjectMoved move);

    /// <summary>The server's word on the player's position that is not a teleport (<see cref="PositionVerdict.ForceBlip"/> or <see cref="PositionVerdict.SelfEcho"/>): physics decides.</summary>
    void PlayerPositioned(in ObjectMoved move);

    /// <summary>The player's physics state changed (Hidden cleared, Frozen, ...).</summary>
    void PlayerStateChanged(in ObjectStateChanged change);

    /// <summary>The server moved the player's motion (an emote, an action, a MoveTo).</summary>
    void PlayerMotionChanged(in ObjectMotionChanged change);

    /// <summary>The player's look changed (F625).</summary>
    void PlayerDescChanged(in ObjectDescChanged change);

    /// <summary>The player left the world: tear the player down and pause the world until the next arrival.</summary>
    void PlayerLeft(PlayLeaveReason reason);
}

/// <summary>Options of <see cref="PlayPresentationFlow"/>.</summary>
/// <param name="Character">The character to enter the world with by itself (<c>--character</c>), or null to stop at the character list.</param>
public sealed record PlayFlowOptions(string? Character = null);

/// <summary>
/// Play mode's presentation rules (m2-plan G6 W9, <c>PlayPresentationFlow</c>): from the session's events alone it says when the world starts
/// (the player's own CreateObject), what a server teleport does to it, which of the server's words about the player reach the avatar and the
/// physics, when the player is gone, and what the placeholder status line says. It enters the world by itself when a character was asked for.
/// Engine-agnostic: the Godot host implements <see cref="IPlayWorld"/>; nothing here draws, loads or reads a file.
/// </summary>
/// <remarks>
/// <para><b>What it does not do.</b> Portal space is the <c>PortalSessionBridge</c>'s (it feeds <c>TeleportProgress</c> from the same events and
/// sends LoginComplete when the destination is ready), the retail screens are the screen host's (they read the session and act through
/// <see cref="IActionApi"/>), and the object table's other objects are the avatars track's (the player only here: m2-plan G5 W10).</para>
/// <para><b>Auto-enter</b> (<see cref="PlayFlowOptions.Character"/>): once the character list is in, the named character is selected and
/// entered once per connection (a name that matches no character, or one pending deletion, is a logged ERROR and the list stays; the name is not
/// repeated). The screens' own Enter button works as ever.</para>
/// </remarks>
public sealed class PlayPresentationFlow : IDisposable
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "play";

    private readonly IGameSessionView _session;
    private readonly IPlayWorld _world;
    private readonly PlayFlowOptions _options;
    private readonly ILogSink _log;
    private readonly List<IDisposable> _subscriptions = new(12);
    private bool _disposed;
    private bool _arrived;
    private bool _awaitingPosition;
    private bool _autoEnterDone;
    private bool _booted;
    private string? _bootReason;

    /// <summary>Starts following <paramref name="session"/> (subscribe before the first tick so no event is missed).</summary>
    public PlayPresentationFlow(IGameSessionView session, IPlayWorld world, PlayFlowOptions options, ILogSink log)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        var events = session.Events;
        _subscriptions.Add(events.Subscribe<SessionPhaseChanged>(OnPhaseChanged));
        _subscriptions.Add(events.Subscribe<CharacterListChanged>(OnCharacterListChanged));
        _subscriptions.Add(events.Subscribe<Booted>(OnBooted));
        _subscriptions.Add(events.Subscribe<ObjectCreated>(OnObjectCreated));
        _subscriptions.Add(events.Subscribe<ObjectMoved>(OnObjectMoved));
        _subscriptions.Add(events.Subscribe<ObjectStateChanged>(OnObjectStateChanged));
        _subscriptions.Add(events.Subscribe<ObjectMotionChanged>(OnObjectMotionChanged));
        _subscriptions.Add(events.Subscribe<ObjectDescChanged>(OnObjectDescChanged));
        _subscriptions.Add(events.Subscribe<SelfTravelChanged>(OnSelfTravelChanged));
    }

    /// <summary>Where the run is.</summary>
    public PlayStage Stage { get; private set; } = PlayStage.Connecting;

    /// <summary>True from the player's CreateObject until the player leaves the world.</summary>
    public bool PlayerArrived => _arrived;

    /// <summary>Arrivals so far (the first, and each after a logoff and a new entry).</summary>
    public int Arrivals { get; private set; }

    /// <summary>Teleports passed to the world so far.</summary>
    public int Teleports { get; private set; }

    /// <summary>
    /// The placeholder status line (owner decision: until the in-game shell exists, a labelled line stands in for it): the stage, the character and, in the
    /// world, the cell. It is Lifestone's own text, not a retail window.
    /// </summary>
    public string StatusText
    {
        get
        {
            var state = _session.State;
            var name = state.Self.Name;
            return Stage switch
            {
                PlayStage.Connecting => "connecting to the server...",
                PlayStage.DataCheck => "data check...",
                PlayStage.CharacterList => "character list",
                PlayStage.Entering => "entering the world...",
                PlayStage.Arriving => $"{name ?? "your character"}: arriving in the world...",
                PlayStage.InWorld => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name ?? "your character"}: in the world{(state.Self.Position is { } position ? $", cell 0x{position.CellId:X8}" : string.Empty)}"),
                PlayStage.LoggingOff => "logging off...",
                _ => _booted ? $"the server booted you{(_bootReason is { Length: > 0 } reason ? ": " + reason : string.Empty)}" : $"connection lost: {state.FailureMessage ?? "no reason given"}",
            };
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private static bool InWorld(SessionPhase phase) => phase is SessionPhase.EnteringWorld or SessionPhase.InWorld or SessionPhase.LoggingOff;

    private void OnPhaseChanged(in SessionPhaseChanged e)
    {
        Stage = e.Phase switch
        {
            SessionPhase.Idle or SessionPhase.Connecting or SessionPhase.Handshaking => PlayStage.Connecting,
            SessionPhase.DataCheck => PlayStage.DataCheck,
            SessionPhase.CharacterSelect => PlayStage.CharacterList,
            SessionPhase.EnteringWorld => PlayStage.Entering,
            SessionPhase.InWorld => _arrived ? (Stage == PlayStage.InWorld ? PlayStage.InWorld : PlayStage.Arriving) : PlayStage.Entering,
            SessionPhase.LoggingOff => PlayStage.LoggingOff,
            SessionPhase.Failed => PlayStage.Lost,
            _ => Stage,
        };

        if (e.Phase is SessionPhase.Idle or SessionPhase.Connecting)
        {
            _autoEnterDone = false; // a reconnect: the next list is entered again
            _booted = false;
            _bootReason = null;
        }
        else if (e.Phase == SessionPhase.CharacterSelect)
        {
            TryAutoEnter(); // the list (F658) usually arrives just before the phase changes
        }

        if (!InWorld(e.Previous) || InWorld(e.Phase))
        {
            if (e.Phase == SessionPhase.Failed && !_arrived)
            {
                _log.Warn(LogCategory, $"the session failed before the world: {e.Failure?.Message ?? _session.State.FailureMessage ?? "no reason given"}");
            }

            return;
        }

        // The player left the world.
        var reason = e.Phase == SessionPhase.Failed ? (_booted ? PlayLeaveReason.Booted : PlayLeaveReason.ConnectionLost) : PlayLeaveReason.LoggedOff;
        if (_arrived || _awaitingPosition)
        {
            _arrived = false;
            _awaitingPosition = false;
            _log.Info(LogCategory, $"the player left the world ({reason})");
            _world.PlayerLeft(reason);
        }
    }

    private void OnCharacterListChanged(in CharacterListChanged e) => TryAutoEnter();

    private void OnBooted(in Booted e)
    {
        _booted = true;
        _bootReason = e.Reason;
        _log.Warn(LogCategory, $"the server booted this session{(e.Banned ? " (banned)" : string.Empty)}{(string.IsNullOrEmpty(e.Reason) ? string.Empty : ": " + e.Reason)}");
    }

    private void OnObjectCreated(in ObjectCreated e)
    {
        if (!e.IsSelf)
        {
            return;
        }

        if (!_session.State.Objects.TryGet(e.Guid, out var player))
        {
            _log.Error(LogCategory, "the player's CreateObject was announced but the object table does not hold it (a reducer defect)");
            return;
        }

        if (!e.InWorld)
        {
            // Retail's player always has a position; the arrival waits for the first one.
            _awaitingPosition = true;
            _log.Warn(LogCategory, "TODO(g6-arrival-position): the player's CreateObject carried no position; the world waits for the first position update");
            return;
        }

        Arrive(player, e.Recreated);
    }

    private void Arrive(WorldObject player, bool recreated)
    {
        _awaitingPosition = false;
        _arrived = true;
        Arrivals++;
        Stage = PlayStage.Arriving;
        _log.Info(
            LogCategory,
            recreated ? "the player was re-created: the world is built again around it" : "the player arrives: the world is built around it",
            new LogField("cell", player.Position is { } position ? $"0x{position.CellId:X8}" : "none"),
            new LogField("arrivals", Arrivals));
        _world.PlayerArriving(player, recreated);
    }

    private void OnObjectMoved(in ObjectMoved e)
    {
        if (!e.IsSelf)
        {
            return;
        }

        if (_awaitingPosition && _session.State.Objects.TryGet(e.Guid, out var player))
        {
            Arrive(player, recreated: false);
            return;
        }

        if (!_arrived)
        {
            _log.Warn(LogCategory, $"a position for the player arrived before the player ({e.Verdict}); ignored");
            return;
        }

        if (e.Verdict == PositionVerdict.SelfTeleport)
        {
            Teleports++;
            _log.Info(LogCategory, "the server teleported the player", new LogField("cell", $"0x{e.Position.CellId:X8}"));
            _world.PlayerTeleported(e);
        }
        else
        {
            _world.PlayerPositioned(e);
        }
    }

    private void OnObjectStateChanged(in ObjectStateChanged e)
    {
        if (e.IsSelf && _arrived)
        {
            _world.PlayerStateChanged(e);
        }
    }

    private void OnObjectMotionChanged(in ObjectMotionChanged e)
    {
        if (e.IsSelf && _arrived)
        {
            _world.PlayerMotionChanged(e);
        }
    }

    private void OnObjectDescChanged(in ObjectDescChanged e)
    {
        if (_arrived && e.Guid == _session.State.Self.Guid)
        {
            _world.PlayerDescChanged(e);
        }
    }

    private void OnSelfTravelChanged(in SelfTravelChanged e)
    {
        if (e.Travel != SelfTravel.Present || !_arrived || Stage == PlayStage.InWorld)
        {
            return;
        }

        Stage = PlayStage.InWorld;
        _log.Info(LogCategory, "the player is in the world");
        _world.PlayerPresent();
    }

    private void TryAutoEnter()
    {
        if (_options.Character is not { Length: > 0 } wanted || _autoEnterDone)
        {
            return;
        }

        var state = _session.State;
        if (state.Phase != SessionPhase.CharacterSelect || state.Characters.Entries.Count == 0)
        {
            return;
        }

        _autoEnterDone = true;
        CharacterSlot? found = null;
        foreach (var slot in state.Characters.Entries)
        {
            if (string.Equals(slot.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                found = slot;
                break;
            }
        }

        if (found is not { } character)
        {
            _log.Error(LogCategory, "--character names no character on this account, so the character list stays (the name is not repeated here)");
            return;
        }

        if (character.PendingDeletion)
        {
            _log.Error(LogCategory, "--character names a character pending deletion, so the character list stays (restore it there; the name is not repeated here)");
            return;
        }

        _log.Info(LogCategory, "entering the world with the character --character named", new LogField("guid", $"0x{character.Guid:X8}"));
        _session.Actions.SelectCharacter(character.Guid, ActionSource.Host);
        var handle = _session.Actions.EnterWorld(character.Guid, ActionSource.Host);
        handle.Completed += h =>
        {
            if (h.Outcome is { Succeeded: false } outcome)
            {
                _log.Warn(LogCategory, $"entering the world did not succeed: {outcome.Kind}{(string.IsNullOrEmpty(outcome.Detail) ? string.Empty : " (" + outcome.Detail + ")")}");
            }
        };
    }
}
