using System.Numerics;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// A <see cref="PhysicsObj"/> as the avatars track's MoveTo, sticky and target managers see an object
/// (<see cref="ITargetable"/>, <see cref="IMoveToHost"/>): its cylinder, heading, measured velocity and target manager,
/// and the objects target ids name.
/// </summary>
public sealed partial class PhysicsObj
{
    private TargetManager? _targets;
    private TargetWorldView? _targetWorldView;

    /// <summary>
    /// The objects a target id names (retail <c>CObjectMaint::GetObjectA</c> through the object maintainer): the session's
    /// objects, which the host sets (the avatar set, so that a server MoveTo of the player reaches a server object and a
    /// creature chasing the player finds it), or null for the objects of <see cref="World"/>. It can change at any time.
    /// </summary>
    public ITargetWorld? TargetWorld { get; set; }

    /// <summary>
    /// The object's target manager (retail <c>target_manager</c>, made by the first <c>set_target</c> or
    /// <c>add_voyeur</c>, 11.4186 0x0050ED30 and 0x0050EE00, 11.6096 0x0050F800 and 0x0050F8D0): what it targets and the
    /// objects that target it. Made on first use; <see cref="UpdateObject"/> runs its reports
    /// (<see cref="TargetManager.HandleTargetting"/>) only once it exists.
    /// </summary>
    public TargetManager Targets => _targets ??= new TargetManager(this, TargetWorldNow);

    /// <summary>
    /// Retail <c>CPhysicsObj::GetRadius</c> (11.4186 0x0050E9C0, 11.6096 0x0050F490): the Setup's radius times the scale
    /// (<c>CPartArray::GetRadius</c>), 0 without parts. Other objects measure their MoveTos and sticks to this cylinder.
    /// </summary>
    public float Radius => Setup is null ? 0f : Setup.Radius * Scale;

    /// <summary>
    /// Retail <c>CPhysicsObj::GetHeight</c> (11.4186 0x0050E9E0, 11.6096 0x0050F4B0): the Setup's height times the scale
    /// (<c>CPartArray::GetHeight</c>), 0 without parts.
    /// </summary>
    public float Height => Setup is null ? 0f : Setup.Height * Scale;

    /// <summary>
    /// Retail <c>CPhysicsObj::get_heading</c> (11.4186 0x00511540, 11.6096 0x00512010): the compass heading the object
    /// faces, in degrees (0 north, clockwise; <see cref="RetailFrames.GetHeading"/>).
    /// </summary>
    public float Heading => RetailFrames.GetHeading(Position.Frame);

    /// <inheritdoc/>
    /// <remarks>Physics has no parenting: the object is its own top-level object.</remarks>
    uint ITargetable.TopLevelId => Id;

    /// <inheritdoc/>
    /// <remarks>The world's <see cref="PhysicsWorld.CurrentTime"/> (retail <c>Timer::cur_time</c>).</remarks>
    double ITargetable.Time => World.CurrentTime;

    /// <inheritdoc/>
    /// <remarks>
    /// Retail <c>CPhysicsObj::get_velocity</c> (11.4186 0x005113C0, 11.6096 0x00511E90) answers
    /// <see cref="CachedVelocity"/>, what the last step moved the object at (walking included), not the physics
    /// <see cref="Velocity"/> the interpreter reads.
    /// </remarks>
    Vector3 ITargetable.Velocity => CachedVelocity;

    /// <inheritdoc/>
    ITargetWorld IMoveToHost.World => TargetWorldNow;

    /// <summary>The lookup the target, MoveTo and sticky managers use: it asks <see cref="TargetWorld"/> on each call.</summary>
    private TargetWorldView TargetWorldNow => _targetWorldView ??= new TargetWorldView(this);

    /// <summary>
    /// Retail <c>CPhysicsObj::HandleUpdateTarget</c> (11.4186 0x00512BC0, 11.6096 0x005136C0): a report of context 0 from
    /// the object's target goes to the movement manager (its MoveTo) and the position manager (its sticky follow).
    /// </summary>
    public void HandleUpdateTarget(in TargetInfo info)
    {
        if (info.ContextId != 0)
        {
            return;
        }

        Movement?.HandleUpdateTarget(info);
        Positions?.HandleUpdateTarget(info);
    }

    /// <summary>
    /// <see cref="TargetWorld"/> as the target managers hold it: read on each lookup, so the host may set it after the
    /// managers exist; without one, the objects of the object's own world.
    /// </summary>
    private sealed class TargetWorldView(PhysicsObj owner) : ITargetWorld
    {
        public ITargetable? FindObject(uint objectId) =>
            owner.TargetWorld is { } world ? world.FindObject(objectId) : owner.World.GetObject(objectId);
    }
}
