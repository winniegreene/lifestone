using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// Where the avatars' sounds are heard from (M3 4.2 x2, item 4; closes <c>TODO(avatars-listener-camera)</c>). Retail's listener is the viewer: <c>SmartBox::set_viewer</c> (0x00452C80) hands the camera's
/// position to the sound manager (<c>SoundManager::SetPlayerPosition</c>), which <c>SoundManager::PlaySoundInternal</c> (11.6096 0x00550D80) measures each positioned sound against. Without a listener the set
/// uses the position of its <see cref="AvatarViewer"/>, which the offline host and the player controller fill with the player's body.
/// </summary>
public interface IAvatarListener
{
    /// <summary>The listener's cell and frame (heading in the frame's orientation), or false when there is none this frame (the viewer's position stands in).</summary>
    bool TryGetListener(out CellFrame frame);
}

/// <summary>The camera as a listener: its global position and compass heading in the cell it is in (an EnvCell, else the outdoor land cell under it).</summary>
public static class CameraListener
{
    /// <summary>
    /// The listener frame of a camera at <paramref name="position"/> looking along compass <paramref name="headingDegrees"/>; <paramref name="envCellId"/> is the EnvCell the camera is in, or 0 outdoors.
    /// False when it is outdoors and off the map.
    /// </summary>
    public static bool TryFrame(GlobalPosition position, double headingDegrees, uint envCellId, out CellFrame frame)
    {
        var orientation = PositionFrames.WithHeading(Quaternion.Identity, (float)headingDegrees);
        if (envCellId != 0)
        {
            var origin = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(envCellId));
            var local = new Vector3((float)(position.X - origin.X), (float)(position.Y - origin.Y), (float)(position.Z - origin.Z));
            frame = new CellFrame(envCellId, new AcFrame(local, orientation));
            return true;
        }

        if (!AcCoordinates.TryGetOutdoorCell(position, out var cell))
        {
            frame = default;
            return false;
        }

        frame = new CellFrame(cell.CellId, new AcFrame(cell.Local, orientation));
        return true;
    }
}
