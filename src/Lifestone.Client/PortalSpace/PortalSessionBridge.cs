using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Connects a game session (A0's facade, <see cref="IGameSessionView"/>) to portal space: the session's events feed the
/// SmartBox's teleport flags (<see cref="TeleportProgress"/>), the director's LoginComplete goes out through the Action
/// API, and a logoff request starts the logoff transition. The online counterpart of <see cref="ExploreTransition"/>.
/// </summary>
/// <remarks>
/// <para><b>Events → flags</b>, as retail's SmartBox reacts to the same messages:</para>
/// <list type="bullet">
/// <item><see cref="ObjectCreated"/> for the player's own object (a create, a newer instance or an F7DB re-create) →
/// <see cref="TeleportProgress.OnPlayerCreated"/>: every creation of the player runs <c>SmartBox::HandleCreateObject</c>'s
/// player branch (11.4186 0x00454C80, 11.6096 0x00454D20; <c>SmartBox::HandleUpdateObject</c>, 11.4186 0x00455580, 11.6096
/// 0x00455620, is the same with a forced re-create), which applies the position as a teleport. A refresh of the same
/// instance (<see cref="ObjectUpdated"/>) does not.</item>
/// <item><see cref="PlayerTeleportAnnounced"/> (an accepted F751) → <see cref="TeleportProgress.OnPlayerTeleport"/>. The
/// session's reducer has already applied <c>SmartBox::HandlePlayerTeleport</c>'s not-older rule (it publishes only an
/// accepted F751), so the bridge passes the message's own sequence as the stamp and the flags do not judge it
/// again.</item>
/// <item><see cref="ObjectMoved"/> with <see cref="PositionVerdict.SelfTeleport"/> →
/// <see cref="TeleportProgress.OnPlayerPositionTeleported"/> (<c>SmartBox::HandleReceivedPosition</c>, 11.4186
/// 0x00453FD0, 11.6096 0x00454070, calls <c>SmartBox::TeleportPlayer</c> only for that verdict; a forced blip is not a
/// teleport).</item>
/// <item><see cref="ObjectStateChanged"/> for the player → <see cref="TeleportProgress.OnPlayerStateApplied"/>
/// (<c>SmartBox::DoSetState</c>, 11.4186 0x004520D0, 11.6096 0x00452110).</item>
/// <item><see cref="SessionPhaseChanged"/> out of the world (from <see cref="SessionPhase.EnteringWorld"/>,
/// <see cref="SessionPhase.InWorld"/> or <see cref="SessionPhase.LoggingOff"/> to any other phase: the character list
/// arrived, a boot, a failure, a quit) → <see cref="TeleportProgress.OnPlayerRemoved"/> (the flags of
/// <c>SmartBox::Reset</c>, 11.4186 0x00453BF0, 11.6096 0x00453C90) and <see cref="PortalSpaceDirector.Reset"/> (retail
/// tears the SmartBox UI down when the character list returns). Under the default logoff policy this is what ends a
/// logoff's tunnel (D28).</item>
/// </list>
/// <para><b>LoginComplete.</b> The bridge is the director's <see cref="ILoginCompleteSink"/>: a
/// <see cref="PortalCues.SendLoginComplete"/> calls <see cref="IActionApi.LoginComplete"/> with
/// <see cref="ActionSource.Client"/> (<c>CPlayerSystem::SendLoginCompleteNotification</c>, 11.4186 0x00562E90, 11.6096
/// 0x00563C30). The Action API refuses a second one per arrival, loudly. Unlike retail, the bridge sends at once, without
/// retail's wait for the player's inventory (the TODO on <see cref="SendLoginComplete"/>; docs/portal-space.md §14).</para>
/// <para><b>Logoff.</b> <see cref="LogOffRequested"/> → <see cref="PortalSpaceMachine.RequestLogOff"/> with the event's
/// frame time (<c>CPlayerSystem::RequestLogOff</c>, 11.4186 0x00562DD0, 11.6096 0x00563B70); the session has already
/// printed "Logging off..." and sent CharacterLogOff.</para>
/// <para><b>Clocks.</b> The director's <c>now</c> and the session's <c>Tick(now)</c> must be the same host frame time, so a
/// logoff's 3 s delay is measured on one clock.</para>
/// <para><b>Lifetime.</b> Create it with the session, before the player enters the world; it subscribes at once. Dispose
/// it with the session: it unsubscribes and stops being the director's LoginComplete sink. Game thread only; the
/// handlers allocate nothing.</para>
/// </remarks>
public sealed class PortalSessionBridge : IDisposable, ILoginCompleteSink
{
    private readonly List<IDisposable> _subscriptions = new(6);
    private bool _disposed;

    /// <summary>Connects <paramref name="session"/> to <paramref name="director"/> and becomes its LoginComplete sink.</summary>
    public PortalSessionBridge(IGameSessionView session, PortalSpaceDirector director)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Director = director ?? throw new ArgumentNullException(nameof(director));
        var events = session.Events;
        _subscriptions.Add(events.Subscribe<ObjectCreated>(OnObjectCreated));
        _subscriptions.Add(events.Subscribe<PlayerTeleportAnnounced>(OnPlayerTeleportAnnounced));
        _subscriptions.Add(events.Subscribe<ObjectMoved>(OnObjectMoved));
        _subscriptions.Add(events.Subscribe<ObjectStateChanged>(OnObjectStateChanged));
        _subscriptions.Add(events.Subscribe<SessionPhaseChanged>(OnSessionPhaseChanged));
        _subscriptions.Add(events.Subscribe<LogOffRequested>(OnLogOffRequested));
        director.LoginComplete = this;
    }

    /// <summary>The session.</summary>
    public IGameSessionView Session { get; }

    /// <summary>The director.</summary>
    public PortalSpaceDirector Director { get; }

    /// <summary>The handle of the last LoginComplete sent, or null.</summary>
    public ActionHandle? LastLoginComplete { get; private set; }

    /// <summary>Times the bridge reset portal space because the session left the world.</summary>
    public int WorldExits { get; private set; }

    /// <inheritdoc/>
    public void SendLoginComplete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // TODO(portal): retail's pending rule is not re-expressed. CPlayerSystem::SendLoginCompleteNotification (11.4186
        // 0x00562E90, 11.6096 0x00563C30) sends only when the player's weenie exists and
        // ACCWeenieObject::AllContainedObjectsExist (11.4186 0x0058C000, 11.6096 0x0058CE30) holds; otherwise it sets
        // sendLoginCompletePending, and CPlayerSystem::AttemptSendLoginCompleteNotification (11.4186 0x00562F20, 11.6096
        // 0x00563CC0) sends it later, once the player description has been received and every contained object exists
        // (it is called from CPlayerSystem::InitializePlayer, 11.4186 0x00563570, 11.6096 0x00564310, and
        // CPlayerSystem::RecvNotice_ServerSaysMoveItem, 11.4186 0x00563260, 11.6096 0x00564000). Lifestone sends at once:
        // the tunnel's 5 s or more normally covers the inventory's arrival. The place to re-express the rule is the state
        // track's IActionApi.LoginComplete, which knows the player description and the inventory.
        LastLoginComplete = Session.Actions.LoginComplete(ActionSource.Client);
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
        if (ReferenceEquals(Director.LoginComplete, this))
        {
            Director.LoginComplete = null;
        }
    }

    private static bool InWorld(SessionPhase phase) =>
        phase is SessionPhase.EnteringWorld or SessionPhase.InWorld or SessionPhase.LoggingOff;

    private void OnObjectCreated(in ObjectCreated e)
    {
        if (e.IsSelf)
        {
            Director.Progress.OnPlayerCreated();
        }
    }

    private void OnPlayerTeleportAnnounced(in PlayerTeleportAnnounced e) =>
        Director.Progress.OnPlayerTeleport(e.TeleportSequence, e.TeleportSequence);

    private void OnObjectMoved(in ObjectMoved e)
    {
        if (e.Verdict == PositionVerdict.SelfTeleport)
        {
            Director.Progress.OnPlayerPositionTeleported();
        }
    }

    private void OnObjectStateChanged(in ObjectStateChanged e)
    {
        if (e.IsSelf)
        {
            Director.Progress.OnPlayerStateApplied((uint)e.State);
        }
    }

    private void OnSessionPhaseChanged(in SessionPhaseChanged e)
    {
        if (InWorld(e.Previous) && !InWorld(e.Phase))
        {
            WorldExits++;
            Director.Progress.OnPlayerRemoved();
            Director.Reset();
        }
    }

    private void OnLogOffRequested(in LogOffRequested e) =>
        Director.Machine.RequestLogOff(e.Now, e.PlayerKillerDelay);
}
