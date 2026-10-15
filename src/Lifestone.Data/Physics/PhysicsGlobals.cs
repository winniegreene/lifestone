// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/PhysicsGlobals.cs (AGPL-3.0)
// with changes: only the constants the retail client was read to use (each cited below); the step quanta are retail's
// (MaxQuantum 1/5, ACE 0.1: docs/physics.md §2 row 1) and doubles, as retail keeps them; the default friction,
// elasticity and dummy-sphere radius are retail data constants, whose 11.4186 values equal ACE's (PH5).

namespace Lifestone.Data.Physics;

/// <summary>
/// The retail physics constants. Every value was read in the 11.4186 decompile (function cited) unless it says
/// otherwise; ACE's names are kept where they exist.
/// </summary>
public static class PhysicsGlobals
{
    /// <summary>
    /// Retail <c>F_EPSILON</c>, the float 0.0002 (0.00019999999 in the decompile): the tolerance of nearly every
    /// physics comparison.
    /// </summary>
    public const float Epsilon = 0.0002f;

    /// <summary><see cref="Epsilon"/> squared.</summary>
    public const float EpsilonSquared = Epsilon * Epsilon;

    /// <summary>
    /// <c>PhysicsGlobals::gravity</c>, read by <c>CPhysicsObj::calc_acceleration</c> (11.4186 0x00510950, 11.6096
    /// 0x00511420) for objects with the Gravity state: -9.8 as a float (-9.80000019; the physics plan's "-9.8000002").
    /// The variable is initialised in data, not by code, so the value is ACE's (PhysicsGlobals.cs), which matches every
    /// published trajectory.
    /// </summary>
    public const float Gravity = -9.8f;

    /// <summary>
    /// <c>PhysicsGlobals::floor_z</c>: the smallest normal Z of a walkable plane. Retail computes it as
    /// <c>cos(3437.746770784939)</c> (a cosine of 60 * 180 / pi radians) in a static initialiser of GPhysics.obj
    /// (11.4186 0x0070D920) and stores it as a float; that float is ACE's 0.66417414618662751f.
    /// <c>CPhysicsObj::is_valid_walkable</c> compares with &gt;=, <c>set_on_walkable</c> with &lt;.
    /// </summary>
    public const float FloorZ = 0.66417414618662751f;

    /// <summary>
    /// The walkable allowance while landing (retail <c>z_for_landing</c>, the float 0.0871557: <c>CSphere::land_on_sphere</c>
    /// and <c>CCylSphere::land_on_cylinder</c> set it, and <c>CTransition::transitional_insert</c>, 11.4186 0x0050B6F0, and
    /// <c>CTransition::find_placement_position</c>, 0x0050C170, pass the literal).
    /// </summary>
    public const float LandingZ = 0.0871557f;

    /// <summary>
    /// The step-down height a transition tries before the object's own (<c>CTransition::step_up</c>, 11.4186 0x0050B610,
    /// and <c>CTransition::transitional_insert</c>, 0x0050B6F0: the literal 0.039999999, the float 0.04).
    /// </summary>
    public const float DefaultStepDownHeight = 0.04f;

    /// <summary>Speed cap in m/s (<c>CPhysicsObj::UpdatePhysicsInternal</c>, 11.4186 0x00510700: 50 * 50 against |v|²).</summary>
    public const float MaxVelocity = 50.0f;

    /// <summary><see cref="MaxVelocity"/> squared.</summary>
    public const float MaxVelocitySquared = MaxVelocity * MaxVelocity;

    /// <summary>Speeds whose square is within <see cref="Epsilon"/> of this one's square are zeroed (<c>UpdatePhysicsInternal</c>: 0.25 * 0.25).</summary>
    public const float SmallVelocity = 0.25f;

    /// <summary><see cref="SmallVelocity"/> squared.</summary>
    public const float SmallVelocitySquared = SmallVelocity * SmallVelocity;

    /// <summary>
    /// <c>MIN_QUANTUM</c>, 1/30 s: <c>CPhysics::UseTime</c> runs physics only once this much time has passed
    /// (<see cref="PhysicsClock"/>), and <c>CPhysicsObj::update_object</c> carries a smaller remainder to the next frame.
    /// Set by the static initialisers of CPhysics.obj (11.4186 0x006FB2D0) and CPhysicsObj.obj (0x006FBC60).
    /// </summary>
    public const double MinQuantum = 1.0 / 30.0;

    /// <summary>
    /// <c>MAX_QUANTUM</c>, 1/5 s: the longest step <c>CPhysicsObj::update_object</c> takes (11.4186 0x006FBC80; owner
    /// decision D21). ACE uses 0.1 (<see cref="PhysicsCompat.MaxQuantum"/>).
    /// </summary>
    public const double MaxQuantum = 1.0 / 5.0;

    /// <summary>A gap longer than this many seconds is skipped, not stepped (the literal 2.0 in <c>update_object</c>; ACE <c>HugeQuantum</c>).</summary>
    public const double HugeQuantum = 2.0;

    /// <summary>
    /// <c>DEFAULT_FRICTION</c>, which the <c>CPhysicsObj</c> constructor (11.4186 0x005123F0, 11.6096 0x00512EF0) gives every
    /// object and <c>CPhysicsObj::calc_friction</c> (0x0050EE70, 0x0050F940) decays a walkable velocity with
    /// (<c>pow(1 - friction, quantum)</c>). A data constant: the 11.4186 decompile's data declarations give 0.94999999 (the
    /// float 0.95, also as a <c>_0</c> copy), ACE's value too. TODO(physics): confirm it in the 11.6096 data (a D4-style
    /// read by the lead).
    /// </summary>
    public const float DefaultFriction = 0.95f;

    /// <summary>
    /// <c>DEFAULT_ELASTICITY</c>, which the <c>CPhysicsObj</c> constructor gives every object and
    /// <c>CPhysicsObj::handle_all_collisions</c> (11.4186 0x00514780, 11.6096 0x00515280) bounces with. A data constant:
    /// the 11.4186 data declarations give 0.050000001 (the float 0.05, also as a <c>_0</c> copy), ACE's value too.
    /// TODO(physics): confirm it in the 11.6096 data.
    /// </summary>
    public const float DefaultElasticity = 0.05f;

    /// <summary>
    /// <c>DUMMY_SPHERE_RADIUS</c>: the radius of the sphere a transition of an object without spheres uses (centred that
    /// high above the origin; CPhysicsObj.obj's static initialiser, 11.4186 0x006FC370, sets <c>dummy_sphere</c> from it). A
    /// data constant: the 11.4186 data declarations give 0.1, ACE's value too. TODO(physics): confirm it in the 11.6096
    /// data.
    /// </summary>
    public const float DummySphereRadius = 0.1f;
}
