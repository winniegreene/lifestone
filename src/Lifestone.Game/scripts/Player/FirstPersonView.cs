using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Game.Core;

namespace Lifestone.Game.Player;

/// <summary>
/// The first-person view of the player (M2 G5): while the camera is in the head, or so close to it that retail fades the player out
/// (<c>CameraSet::UpdateCamera</c>, 11.4186 0x00458AE0, 11.6096 0x00458C00: <c>SetTranslucencyHierarchical(player, 1)</c> in the head, and a fade from 0.45 m to 0.2 m
/// from the pivot), the character's body is not drawn (<see cref="PlayerAvatar.FirstPerson"/>), and its held items are.
/// </summary>
/// <remarks>
/// The avatars track documented that how retail hides the body was not found (docs/avatars.md); it is this translucency of 1. The camera's translucency is not an effect on the
/// player's avatar: <c>SetTranslucencyHierarchical</c> sets it on every part of the player and of the items it holds, so the avatar set takes it as one camera-driven input
/// (<see cref="PlayerAvatar.CameraTranslucency"/>) and floors every part's translucency with it every frame (<see cref="FirstPersonFade"/>); the parts between 0 and 1 are
/// drawn by the blended twin (D45, D63) through <c>INSTANCE_CUSTOM.w</c>. Closes TODO(g5-first-person-fade). The held items fade with the player (the wand, in the avatar set's
/// frame, moved with the body by the player's interpolated offset in <c>ServerObjectsLayer</c>: TODO(g5-held-item-interpolation) closed in M3 4.1 fix A).
/// </remarks>
public sealed class FirstPersonView
{
    private readonly PlayerAvatar _player;

    /// <param name="player">The player's avatar.</param>
    public FirstPersonView(PlayerAvatar player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _player = player;
    }

    /// <summary>True while the body is hidden.</summary>
    public bool BodyHidden => _player.FirstPerson;

    /// <summary>Applies the frame's camera: feeds the camera's translucency to the avatar set and hides the body when retail draws the player fully translucent.</summary>
    public void Apply(in CameraPose pose)
    {
        _player.CameraTranslucency = pose.PlayerTranslucency;
        var hide = pose.PlayerTranslucency >= 1f;
        if (hide != _player.FirstPerson)
        {
            _player.FirstPerson = hide;
            Log.Info("play", hide ? (pose.InHead ? "first person: the body is hidden" : "the camera is inside the player: the body is hidden") : "the body is drawn");
        }
    }
}
