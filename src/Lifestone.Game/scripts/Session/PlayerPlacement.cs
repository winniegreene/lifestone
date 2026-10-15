using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Game.Player;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Where the server puts the player and what moves with it (m2-plan G6 W9, <c>PlayerPlacement</c>): the streamed world, the interiors' viewer, the physics
/// body and the camera. Portal space's <c>TeleportProgress</c> is not fed here: the <c>PortalSessionBridge</c> already feeds it from the same events
/// (<c>PlayerCreated</c>, <c>PlayerTeleportAnnounced</c>, the self-teleport position, the unhide), and feeding it twice would run retail's flags twice.
/// </summary>
public static class PlayerPlacement
{
    /// <summary>
    /// Starts the world around <paramref name="at"/>: the streamed world is teleported there (its streamer is reset and the floating origin is rebased onto
    /// the landblock, which tells the camera and the player to sync), and the interiors' viewer is put in the cell with the shown cells forgotten, as an
    /// explore-mode teleport does (<c>InteriorController.PlaceCamera</c>). The streaming focus must already be the player.
    /// </summary>
    public static void StartWorldAround(WorldRoot world, InteriorController? interiors, in CellFrame at)
    {
        ArgumentNullException.ThrowIfNull(world);
        var global = AcCoordinates.ToGlobal(new CellPosition(at.CellId, at.Origin));
        world.Teleport(global);
        interiors?.PlaceForPlayer(at.CellId, global with { Z = global.Z + FlyCamera.EyeHeight });
    }

    /// <summary>
    /// <see cref="StartWorldAround"/> for the arrival, in two frames (DoD 15): the world's teleport (which drops the landblocks the explore start streamed
    /// before play mode paused the world: the tens of milliseconds of the frame at about a second after Enter) now, the interiors' placement through
    /// <paramref name="later"/> (a later frame's work). The tunnel hides both.
    /// </summary>
    public static void StartWorldAroundSplit(WorldRoot world, InteriorController? interiors, in CellFrame at, Action<Action> later)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(later);
        var global = AcCoordinates.ToGlobal(new CellPosition(at.CellId, at.Origin));
        world.Teleport(global);
        if (interiors is not null)
        {
            var cell = at.CellId;
            later(() => interiors.PlaceForPlayer(cell, global with { Z = global.Z + FlyCamera.EyeHeight }));
        }
    }

    /// <summary>
    /// A server teleport (<c>SmartBox::TeleportPlayer</c>): the world and the interiors move to the destination, then the physics body is placed there
    /// (<c>PlayerMovementController.Apply</c> with a self-teleport verdict) and the camera snaps behind it. The server keeps echoing accepted positions, which
    /// keeps retail's constraint near the player, so the constraint is kept (offline it is taken off).
    /// </summary>
    public static void Teleport(WorldRoot world, InteriorController? interiors, PlayerRig rig, in CellFrame at)
    {
        ArgumentNullException.ThrowIfNull(rig);
        StartWorldAround(world, interiors, at);
        rig.Controller.Teleport(at, keepConstraint: true);
    }
}
