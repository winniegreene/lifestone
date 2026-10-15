using Lifestone.Data.Motion;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// What physics asks of an object's weenie (retail <c>CWeenieObject</c>), beyond what the motion interpreter asks
/// (<see cref="IMotionWeenie"/>): the four flags <c>OBJECTINFO::init</c> (11.4186 0x0050CF30, 11.6096 0x0050DA00) copies
/// into a transition and <c>CPhysicsObj::FindObjCollisions</c> (0x0050F050, 0x0050FB20) compares, which decide whether
/// two players pass through each other.
/// </summary>
/// <remarks>
/// <para>The 11.4186 decompile reaches them through the weenie's vtable: slot 4 <see cref="IsPlayer"/>, slot 8
/// <see cref="IsPK"/>, slot 9 <see cref="IsPKLite"/>, slot 10 <see cref="IsImpenetrable"/> (slot 11 is
/// <see cref="IMotionWeenie.IsCreature"/>), each read where the flag it sets is tested.</para>
/// <para><b>Implementers:</b> the state track's player weenie (from the qualities), the server objects' weenies (from
/// CreateObject), and <c>OfflinePlayerWeenie</c> for explore mode. m2-plan §4.10 named only
/// <see cref="IsImpenetrable"/>; the other three are needed for retail's player rule (docs/physics.md §10).</para>
/// </remarks>
public interface IPhysicsWeenie : IMotionWeenie
{
    /// <summary>Retail <c>CWeenieObject::IsPlayer</c> (slot 4): any player character, the client's own or another.</summary>
    bool IsPlayer { get; }

    /// <summary>Retail <c>CWeenieObject::IsPK</c> (slot 8): a player killer.</summary>
    bool IsPK { get; }

    /// <summary>Retail <c>CWeenieObject::IsPKLite</c> (slot 9): a lite player killer.</summary>
    bool IsPKLite { get; }

    /// <summary>Retail <c>CWeenieObject::IsImpenetrable</c> (slot 10): nothing passes through the object, not even another player.</summary>
    bool IsImpenetrable { get; }
}
