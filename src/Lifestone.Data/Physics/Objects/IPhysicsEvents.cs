using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// One object collision as retail reports it to a weenie (<c>CPhysicsObj::report_object_collision</c>, 11.4186
/// 0x00513060, 11.6096 0x00513B60): the object hit, the velocity of the reporter relative to it, and who was a missile
/// or in contact.
/// </summary>
/// <param name="Other">The object collided with.</param>
/// <param name="RelativeVelocity">The reporting object's velocity less <paramref name="Other"/>'s, in world axes.</param>
/// <param name="AsMissile">The reporting object is a missile: retail sends an attack profile (<c>AtkCollisionProfile</c>,
/// with the quadrant of <c>Position::determine_quadrant</c>) instead of an object profile; the quadrant is not computed
/// here (no client-simulated missile reaches physics in M2).</param>
/// <param name="OtherIsMissile">The other object is a missile.</param>
/// <param name="OtherHasContact">The other object touches a contact plane.</param>
/// <param name="SelfHadContact">The reporting object touched a contact plane before the step.</param>
public readonly record struct ObjectCollision(
    PhysicsObj Other,
    Vector3 RelativeVelocity,
    bool AsMissile,
    bool OtherIsMissile,
    bool OtherHasContact,
    bool SelfHadContact);

/// <summary>
/// Where a <see cref="PhysicsObj"/> reports what physics learned, in place of retail's calls into the weenie
/// (<c>CWeenieObject::DoCollision</c>, vtable slots 19-21, and <c>DoCollisionEnd</c>, slot 22) and of the animation
/// hooks <c>CPhysicsObj::process_hooks</c> runs. Reports only: game rules belong to the server, so nothing here may
/// change what physics does.
/// </summary>
/// <remarks>
/// <para>Collisions are reported, as in retail, only for an object with the ReportCollisions state (and here an
/// <see cref="PhysicsObj.Events"/> sink, standing in for retail's weenie): once when a collision starts, and an end when
/// the object has not touched the other for a second (an ethereal one at once), or when it leaves the world, teleports
/// or hides.</para>
/// <para>Called on the physics thread (the main thread or the headless tick) during <see cref="PhysicsObj.UpdateObject"/>
/// and <see cref="PhysicsObj.SetPosition"/>; an implementation must not move the object.</para>
/// </remarks>
public interface IPhysicsEvents
{
    /// <summary>
    /// Retail <c>DoCollision(EnvCollisionProfile)</c> (weenie slot 19, from <c>CPhysicsObj::report_environment_collision</c>,
    /// 11.4186 0x00512FC0, 11.6096 0x00513AC0): the object ran into the environment (terrain, a building, a static
    /// object, or landing).
    /// </summary>
    /// <param name="self">The object.</param>
    /// <param name="velocity">Its velocity in world axes when it collided.</param>
    /// <param name="hadContact">It touched a contact plane before the step.</param>
    void OnEnvironmentCollision(PhysicsObj self, Vector3 velocity, bool hadContact);

    /// <summary>Retail <c>DoCollision(ObjCollisionProfile)</c> or, for a missile, <c>DoCollision(AtkCollisionProfile)</c> (weenie slots 20, 21).</summary>
    /// <param name="self">The reporting object.</param>
    /// <param name="collision">The collision.</param>
    void OnObjectCollision(PhysicsObj self, in ObjectCollision collision);

    /// <summary>
    /// Retail <c>DoCollisionEnd</c> (weenie slot 22, from <c>CPhysicsObj::report_object_collision_end</c>, 11.4186
    /// 0x00510A90, 11.6096 0x00511560): the object stopped touching <paramref name="otherId"/>.
    /// </summary>
    void OnObjectCollisionEnd(PhysicsObj self, uint otherId);

    /// <summary>
    /// The animation hooks one physics step fired (retail <c>CPhysicsObj::process_hooks</c>, 11.4186 0x00511550, 11.6096
    /// 0x00512020, executes them: sounds, particles, attacks), animation-done hooks excepted (the motion table consumes
    /// those). <paramref name="hooks"/> is only valid during the call.
    /// </summary>
    void OnAnimationHooks(PhysicsObj self, IReadOnlyList<AnimHook> hooks);
}
