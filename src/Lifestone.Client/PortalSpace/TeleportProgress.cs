using Lifestone.Data.World;

namespace Lifestone.Client.PortalSpace;

/// <summary>What <see cref="PortalSpaceMachine.Update"/> reads of the teleport flags each frame.</summary>
public interface ITeleportProgress
{
    /// <summary>
    /// Retail <c>SmartBox::teleport_in_progress</c> (11.4186 0x00451C20, 11.6096 0x00451C60): there is a player and its
    /// position update is not complete.
    /// </summary>
    bool InProgress { get; }

    /// <summary>
    /// Retail <c>SmartBox::teleport_occured</c> (11.4186 0x00451C40, 11.6096 0x00451C80): returns the "has been
    /// teleported" flag and clears it, so each completed update is reported once.
    /// </summary>
    bool ConsumeTeleportOccurred();
}

/// <summary>
/// The SmartBox's teleport flags (m2-log-in.md §4.7): whether the player's position update is complete, whether a
/// teleport's new position is awaited, and whether a completed update is still to be reported. The session layer feeds
/// it the player's events; the director ticks it once per frame with the destination's readiness.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name (step PS6):</para>
/// <list type="bullet">
/// <item><see cref="InProgress"/>: <c>SmartBox::teleport_in_progress</c> (11.4186 0x00451C20, 11.6096 0x00451C60);
/// <see cref="ConsumeTeleportOccurred"/>: <c>SmartBox::teleport_occured</c> (11.4186 0x00451C40, 11.6096
/// 0x00451C80).</item>
/// <item><see cref="OnPlayerCreated"/>: <c>SmartBox::HandleCreateObject</c> (11.4186 0x00454C80, 11.6096 0x00454D20) for
/// the player's own object, which applies its position through <c>SmartBox::PlayerPositionUpdated</c> with teleporting
/// set (11.4186 0x00453870, 11.6096 0x00453910): "complete", "waiting" and "has been teleported" all clear.</item>
/// <item><see cref="OnPlayerTeleport"/>: <c>SmartBox::HandlePlayerTeleport</c> (11.4186 0x00452150, 11.6096
/// 0x00452190).</item>
/// <item><see cref="OnPlayerPositionTeleported"/>: <c>SmartBox::TeleportPlayer</c> (11.4186 0x00453910, 11.6096
/// 0x004539B0) → <c>PlayerPositionUpdated</c> with teleporting set.</item>
/// <item><see cref="OnPlayerStateApplied"/>: the player branch of <c>SmartBox::DoSetState</c> (11.4186 0x004520D0,
/// 11.6096 0x00452110).</item>
/// <item><see cref="OnPlayerRemoved"/>: the flag part of <c>SmartBox::Reset</c> (11.4186 0x00453BF0, 11.6096
/// 0x00453C90): no player, not complete, nothing to report.</item>
/// <item><see cref="Tick"/>: the completion in <c>SmartBox::UseTime</c> (11.4186 0x00455410, 11.6096 0x004554B0).</item>
/// </list>
/// <para>Retail's "not blocking for cells" is a data-prefetch state; Lifestone's stand-in is the destination's attach
/// readiness (<see cref="DestinationReadiness"/>), passed to <see cref="Tick"/>.</para>
/// <para><b>Lifestone's additions.</b> <see cref="BeginLocal"/> stands a local camera jump in for the server's player
/// object (offline explore mode), and <see cref="Hold"/> keeps an update from completing (<c>--portal-space</c>). Neither
/// is retail.</para>
/// <para>Single-threaded: the frame thread calls everything, the session's event handlers included. Allocation-free.</para>
/// </remarks>
public sealed class TeleportProgress : ITeleportProgress
{
    /// <summary>The PhysicsState bit Hidden (0x4000): a SetState without it ends a teleport's wait.</summary>
    public const uint HiddenState = 0x4000;

    private bool _player;
    private bool _complete;
    private bool _waiting;
    private bool _teleported;
    private bool _hold;

    /// <inheritdoc/>
    public bool InProgress => _player && !_complete;

    /// <summary>A player object exists (retail <c>SmartBox::player</c> is set), or a local jump stands in for one.</summary>
    public bool HasPlayer => _player;

    /// <summary>The player is a local stand-in (<see cref="BeginLocal"/>), not a server object.</summary>
    public bool IsLocal { get; private set; }

    /// <summary>A teleport's new position is awaited (retail <c>SmartBox::waiting_for_teleport</c>).</summary>
    public bool WaitingForTeleport => _waiting;

    /// <summary>The position update is complete (retail <c>SmartBox::position_update_complete</c>).</summary>
    public bool PositionUpdateComplete => _complete;

    /// <summary>A completed update is still to be reported (retail <c>SmartBox::has_been_teleported</c>).</summary>
    public bool HasBeenTeleported => _teleported;

    /// <summary><see cref="Hold"/> is keeping the update from completing.</summary>
    public bool IsHeld => _hold;

    /// <summary>
    /// The server created the player's own object: its position is applied as a teleport
    /// (<c>HandleCreateObject</c> → <c>PlayerPositionUpdated(teleporting)</c>), so the update is no longer complete.
    /// </summary>
    public void OnPlayerCreated()
    {
        _player = true;
        IsLocal = false;
        PlayerPositionUpdated();
    }

    /// <summary>
    /// F751 PlayerTeleport (<c>HandlePlayerTeleport</c>): accepted when <paramref name="sequence"/> is not older than the
    /// player's teleport stamp (equal counts; a wrap-safe u16 compare), which clears "complete" and "has been teleported"
    /// and starts waiting for the new position.
    /// </summary>
    /// <param name="sequence">The message's teleport sequence.</param>
    /// <param name="playerTeleportStamp">The player object's stored teleport stamp (<c>update_times[TELEPORT_TS]</c>).</param>
    /// <returns>True when the message was accepted; false when it is older, or when there is no player (retail ignores
    /// it then).</returns>
    public bool OnPlayerTeleport(ushort sequence, ushort playerTeleportStamp)
    {
        if (!_player || !Sequence16.NotOlder(playerTeleportStamp, sequence))
        {
            return false;
        }

        _complete = false;
        _teleported = false;
        _waiting = true;
        return true;
    }

    /// <summary>
    /// The player's new position was applied as a teleport (<c>TeleportPlayer</c> → <c>PlayerPositionUpdated(teleporting)</c>),
    /// which ends the wait. Ignored without a player, as retail does.
    /// </summary>
    public void OnPlayerPositionTeleported()
    {
        if (_player)
        {
            PlayerPositionUpdated();
        }
    }

    /// <summary>
    /// A newer physics state was applied to the player (<c>DoSetState</c>; the caller has already applied the newer-stamp
    /// rule). A state without Hidden (0x4000) ends a wait.
    /// </summary>
    public void OnPlayerStateApplied(uint newPhysicsState)
    {
        if (_player && _waiting && (newPhysicsState & HiddenState) == 0)
        {
            _waiting = false;
        }
    }

    /// <summary>
    /// The player object is gone (logoff, a forced recreate, teardown): nothing is in progress any more. As in retail
    /// <c>SmartBox::Reset</c>, "waiting" is left as it was; the next player's creation clears it.
    /// </summary>
    public void OnPlayerRemoved()
    {
        _player = false;
        IsLocal = false;
        _complete = false;
        _teleported = false;
    }

    /// <summary>
    /// Offline explore mode: a local camera jump stands in for <c>TeleportPlayer</c>, with no player object (m2-log-in.md
    /// §4.11). The update restarts as if the server had created the player there.
    /// </summary>
    public void BeginLocal()
    {
        if (!_player)
        {
            _player = true;
            IsLocal = true;
        }

        PlayerPositionUpdated();
    }

    /// <summary>Keeps the update from completing while <paramref name="hold"/> is true (<c>--portal-space</c>).</summary>
    public void Hold(bool hold) => _hold = hold;

    /// <summary>
    /// The per-frame completion of <c>SmartBox::UseTime</c>: when <paramref name="notBlockingForCells"/> holds, there is a
    /// player, nothing is awaited and the update is not complete, the update completes and "has been teleported" is set.
    /// </summary>
    public void Tick(bool notBlockingForCells)
    {
        if (notBlockingForCells && !_hold && _player && !_waiting && !_complete)
        {
            _complete = true;
            _teleported = true;
        }
    }

    /// <inheritdoc/>
    public bool ConsumeTeleportOccurred()
    {
        var teleported = _teleported;
        _teleported = false;
        return teleported;
    }

    /// <summary>The teleporting branch of <c>SmartBox::PlayerPositionUpdated</c>: the flags only.</summary>
    private void PlayerPositionUpdated()
    {
        _complete = false;
        _waiting = false;
        _teleported = false;
    }
}
