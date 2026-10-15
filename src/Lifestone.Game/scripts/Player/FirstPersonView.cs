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
/// The avatars track documented that how retail hides the body was not found (docs/avatars.md); it is this translucency of 1. The blended twin that draws the materialise
/// fade (D45, D63) could draw this fade too, but the camera's translucency is not an effect on the player's avatar: <c>SetTranslucencyHierarchical</c> sets it on every part
/// outside the effects layer's hooks, and the avatar set has no way to take a camera-driven value (TODO(g5-first-person-fade), M3). Until then the body is drawn solid
/// until the camera is within 0.2 m, where it is not drawn. The camera sweep keeps the chase camera outside 0.3 m of everything, so that only happens at a wall.
/// </remarks>
public sealed class FirstPersonView
{
    private readonly PlayerAvatar _player;
    private bool _fadeLogged;

    /// <param name="player">The player's avatar.</param>
    public FirstPersonView(PlayerAvatar player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _player = player;
    }

    /// <summary>True while the body is hidden.</summary>
    public bool BodyHidden => _player.FirstPerson;

    /// <summary>Applies the frame's camera: hides the body when retail draws the player fully translucent.</summary>
    public void Apply(in CameraPose pose)
    {
        var hide = pose.PlayerTranslucency >= 1f;
        if (hide != _player.FirstPerson)
        {
            _player.FirstPerson = hide;
            Log.Info("play", hide ? (pose.InHead ? "first person: the body is hidden" : "the camera is inside the player: the body is hidden") : "the body is drawn");
        }

        if (!hide && pose.PlayerTranslucency > 0f && !_fadeLogged)
        {
            _fadeLogged = true;
            Log.Warn("play", "TODO(g5-first-person-fade): the camera is close enough to fade the player (retail's fade from 0.45 m to 0.2 m); the avatar set cannot take a camera-driven translucency yet, so the character is drawn solid until the camera is 0.2 m from the pivot", ("translucency", pose.PlayerTranslucency));
        }
    }
}
