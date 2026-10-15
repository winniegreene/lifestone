using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// The player's own character (AV10): an avatar like any other for its looks (its own CreateObject's body, clothing and
/// colours), its children, Hidden and its scripts, and its draw output; but its position and animation are the player
/// body's (<see cref="IPlayerBody"/>, the physics track's player object, which hosts the motion interpreter on
/// <see cref="Parts"/> as retail's <c>CPhysicsObj</c> hosts <c>CMotionInterp</c>).
/// </summary>
/// <remarks>
/// <para><b>Binding.</b> The physics track builds its <c>MotionInterpreter</c> on <see cref="Parts"/> (the part array
/// the avatar made from the player's motion table), steps it in its own update and forwards each update's hooks with
/// <see cref="QueueHooks"/>. Each quantum, after its movement manager's <c>UseTime</c> and before its position
/// manager's, it calls <see cref="MotionPlayer.HandleMovement"/> on <see cref="Parts"/> (retail
/// <c>CPhysicsObj::UpdateObjectInternal</c> → <c>CPartArray::HandleMovement</c>); without it a queued motion with no
/// animations stays pending, and the jump gate and MoveTo's steering wait on it. Once bound, every frame takes the body's position and contact and draws the body's pose.
/// Until a body is bound the avatar stays where the player's accepted positions put it, and its own sequence plays the
/// motion table's default state.</para>
/// <para><b>Drawing.</b> The player always draws LOD level 0 (<c>CPhysicsPart::UpdateViewerDistance</c>'s player
/// check). <see cref="FirstPerson"/> hides the body; how retail hides it in first person is not pinned (the camera
/// code does not set NoDraw; TODO(avatars): find the viewer test), and held items stay drawn.</para>
/// </remarks>
public sealed class PlayerAvatar
{
    internal PlayerAvatar(Avatar avatar)
    {
        Avatar = avatar;
    }

    /// <summary>The avatar.</summary>
    public Avatar Avatar { get; }

    /// <summary>The avatar's body.</summary>
    public AvatarBody Body => Avatar.Body;

    /// <summary>The player's guid.</summary>
    public uint Guid => Avatar.Guid;

    /// <summary>
    /// The part array made from the player's motion table, for the player body to host its interpreter on; null when the
    /// player's CreateObject named no motion table.
    /// </summary>
    public MotionPlayer? Parts => Body.Parts;

    /// <summary>The bound player body, or null.</summary>
    public IPlayerBody? BoundBody { get; private set; }

    /// <summary>The first-person camera is on: the body is not drawn (its held items are).</summary>
    public bool FirstPerson { get; set; }

    /// <summary>The player's position: the body's when bound, else the avatar's.</summary>
    public CellFrame Position => BoundBody?.Position ?? Body.Position;

    /// <summary>Binds (or, with null, unbinds) the player body; the avatar then draws the pose of the body's part array.</summary>
    public void Bind(IPlayerBody? body)
    {
        BoundBody = body;
        if (body?.Motion.Parts is { } parts)
        {
            Body.UseExternalDriver(parts);
        }
    }

    /// <summary>
    /// The hooks of one update of the player's part array (<see cref="MotionUpdate.Hooks"/>), which the player body
    /// steps: they run on the avatar's effects (particles, scripts, sounds) at its next update.
    /// </summary>
    public void QueueHooks(IReadOnlyList<AnimHook> hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        for (var i = 0; i < hooks.Count; i++)
        {
            Body.Effects.QueueAnimHook(hooks[i]);
        }
    }

    /// <summary>One frame: follow the bound body, or step the avatar as a server object when none is bound.</summary>
    internal void Advance(double now, in AvatarStep step)
    {
        if (BoundBody is { } body)
        {
            Body.Follow(now, body.Position, body.HasContact);
            return;
        }

        Body.Advance(now, step with { PlayerPosition = null });
    }
}
