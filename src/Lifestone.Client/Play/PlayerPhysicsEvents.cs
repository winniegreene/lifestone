using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Objects;

namespace Lifestone.Client.Play;

/// <summary>
/// What the player's physics object reports, as the avatars track wants it: the animation hooks of each step reach the player
/// avatar's effects (<see cref="PlayerAvatar.QueueHooks"/>: particles, scripts, sounds), as <c>CPhysicsObj::process_hooks</c>
/// (11.4186 0x00511550, 11.6096 0x00512020) executes them on the object that owns the part array. Collisions are reports for
/// the server (the weenie's <c>DoCollision</c>); offline there is nobody to tell, so they are dropped.
/// </summary>
public sealed class PlayerPhysicsEvents : IPhysicsEvents
{
    private readonly PlayerAvatar _avatar;

    /// <param name="avatar">The player's avatar, whose effects run the hooks.</param>
    public PlayerPhysicsEvents(PlayerAvatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        _avatar = avatar;
    }

    /// <summary>Hooks forwarded since the events were made (statistics).</summary>
    public long HooksForwarded { get; private set; }

    /// <inheritdoc/>
    public void OnAnimationHooks(PhysicsObj self, IReadOnlyList<AnimHook> hooks)
    {
        _avatar.QueueHooks(hooks);
        HooksForwarded += hooks.Count;
    }

    /// <inheritdoc/>
    public void OnEnvironmentCollision(PhysicsObj self, Vector3 velocity, bool hadContact)
    {
    }

    /// <inheritdoc/>
    public void OnObjectCollision(PhysicsObj self, in ObjectCollision collision)
    {
    }

    /// <inheritdoc/>
    public void OnObjectCollisionEnd(PhysicsObj self, uint otherId)
    {
    }
}
