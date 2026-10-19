using Lifestone.Client.Avatars;
using Lifestone.Game.Player;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// The camera as the avatars' sound listener (M3 4.2 x2, item 4; retail's <c>SmartBox::set_viewer</c> 0x00452C80 sets <c>SoundManager::SetPlayerPosition</c> from the viewer): the camera's global position and
/// compass heading, in the EnvCell the interiors' tracker has it in or the outdoor cell under it (<see cref="CameraListener.TryFrame"/>). The ambient sound's listener reads the same camera.
/// </summary>
public sealed class FlyCameraListener(FlyCamera camera) : IAvatarListener
{
    /// <inheritdoc />
    public bool TryGetListener(out Lifestone.Data.World.CellFrame frame) =>
        CameraListener.TryFrame(camera.WorldPosition, camera.Heading, camera.ViewerCell is { } cell && Lifestone.Client.World.AcCoordinates.IsEnvCell(cell.CellId) ? cell.CellId : 0u, out frame);
}
