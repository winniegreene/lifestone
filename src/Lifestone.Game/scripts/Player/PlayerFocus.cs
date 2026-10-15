using Lifestone.Client.Streaming;
using Lifestone.Game.World;

namespace Lifestone.Game.Player;

/// <summary>
/// The world's streaming focus while the player exists: where the player is (<see cref="PlayerController.StreamingFocus"/>, the
/// player's cell and the dungeon rule of <see cref="Lifestone.Client.Play.PlayerStreamingFocus"/>), not where the camera is. The
/// camera that was the focus before keeps its own re-sync when the floating origin moves.
/// </summary>
public sealed class PlayerFocus : IStreamingFocusSource
{
    private readonly IStreamingFocusSource _camera;
    private readonly PlayerController _player;

    /// <param name="camera">The focus source that was installed (the camera, with the interiors' correction): it still re-syncs on a rebase.</param>
    /// <param name="player">The player.</param>
    public PlayerFocus(IStreamingFocusSource camera, PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(player);
        _camera = camera;
        _player = player;
    }

    /// <inheritdoc/>
    public StreamingFocus StreamingFocus => _player.StreamingFocus;

    /// <inheritdoc/>
    public void SyncToOrigin()
    {
        _camera.SyncToOrigin();
        _player.SyncToOrigin();
    }
}
