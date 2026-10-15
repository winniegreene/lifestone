using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>The server's positions for the player: <see cref="ServerPositionPolicy"/> carried out.</summary>
public sealed partial class PlayerMovementController
{
    /// <summary>
    /// Applies an accepted UpdatePosition as retail's <c>SmartBox::HandleReceivedPosition</c> (11.4186 0x00453FD0, 11.6096
    /// 0x00454070) does for the player, by the reducer's verdict (<see cref="ServerPositionPolicy"/>): a correction blips
    /// the player keeping its heading and sends its position at once; a teleport arrival places it, constrains it and
    /// stops it; an echo constrains it. Positions of other objects are ignored.
    /// </summary>
    /// <param name="selfMove">The session's <see cref="ObjectMoved"/> event.</param>
    /// <param name="now">The frame time in seconds (the controller's clock moves forward to it).</param>
    public ServerPositionOutcome Apply(in ObjectMoved selfMove, double now)
    {
        Advance(now);
        switch (selfMove.Verdict)
        {
            case PositionVerdict.ForceBlip:
                BlipPlayer(selfMove.Position);
                SendPositionEvent();
                Stats.Blips++;
                return ServerPositionOutcome.Blipped;
            case PositionVerdict.SelfTeleport:
                TeleportPlayer(selfMove.Position);
                ConstrainToServer(selfMove.Position);
                _player.SetVelocity(Vector3.Zero);
                Stats.Teleports++;
                return ServerPositionOutcome.Teleported;
            case PositionVerdict.SelfEcho:
                ConstrainToServer(selfMove.Position);
                Stats.Echoes++;
                return ServerPositionOutcome.Constrained;
            default:
                Stats.IgnoredPositions++;
                return ServerPositionOutcome.Ignored;
        }
    }

    /// <summary>
    /// Retail <c>SmartBox::BlipPlayer</c> (11.4186 0x00453940, 11.6096 0x004539E0) after <c>HandleReceivedPosition</c>
    /// turned the position to the player's heading (<c>CPhysicsObj::get_heading</c>, <c>Frame::set_heading</c>): a sliding
    /// <c>CPhysicsObj::SetPositionSimple</c> (11.4186 0x005162B0, 11.6096 0x00516DB0). The distance is kept for the camera
    /// (<c>SmartBox::PlayerPositionUpdated</c> moves the viewer only beyond the autonomy blip distance).
    /// </summary>
    private void BlipPlayer(in CellFrame position)
    {
        var heading = RetailFrames.GetHeading(_player.Position.Frame);
        var blip = new CellFrame(position.CellId, RetailFrames.SetHeading(position.Frame, heading));
        Stats.LastBlipDistance = _player.Position.Distance(blip);
        _player.SetPositionSimple(blip, sliding: true);
    }

    /// <summary>
    /// Retail <c>SmartBox::TeleportPlayer</c> (11.4186 0x00453910, 11.6096 0x004539B0): a sliding
    /// <c>SetPositionSimple</c>, then <c>SmartBox::PlayerPositionUpdated</c> (0x00453870 / 0x00453910) with teleporting set:
    /// <c>CPhysicsObj::teleport_hook</c> (11.4186 0x00514ED0, 11.6096 0x005159D0) and the command interpreter's teleport
    /// notice, read as <c>CommandInterpreter::PlayerTeleported</c> (0x006B32B0 / 0x006B41F0: autorun off, the held keys
    /// applied, a MoveToState).
    /// </summary>
    private void TeleportPlayer(in CellFrame position)
    {
        _player.SetPositionSimple(position, sliding: true);
        _player.TeleportHook();
        SetAutoRun(false, applyMovement: true);
        SendMovementEvent();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::ConstrainTo</c> (11.4186 0x00510520, 11.6096 0x00510FF0) with the start and maximum
    /// distances of the cell the player is in (<see cref="ServerPositionPolicy.StartConstraintDistance"/>,
    /// <see cref="ServerPositionPolicy.MaxConstraintDistance"/>). Only an autonomy level other than 2 would also
    /// interpolate towards the position (<c>CommandInterpreter::UsePositionFromServer</c>).
    /// </summary>
    private void ConstrainToServer(in CellFrame position)
    {
        var cellId = _player.Position.CellId;
        _player.ConstrainTo(position, ServerPositionPolicy.StartConstraintDistance(cellId), ServerPositionPolicy.MaxConstraintDistance(cellId));
    }
}
