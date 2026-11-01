using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Routes;

/// <summary>A solid server object a <see cref="RouteDriver"/> may have to walk around: a collision cylinder standing on the ground.</summary>
/// <param name="Name">The object's name (for the log).</param>
/// <param name="WeenieClassId">The object's weenie class id (for the log).</param>
/// <param name="Centre">Where the cylinder stands.</param>
/// <param name="Radius">The cylinder's radius in metres, scale applied.</param>
public readonly record struct RouteObstacle(string Name, uint WeenieClassId, CellFrame Centre, float Radius);

/// <summary>
/// What the route driver asks about the world around the walker when it stalls or is about to (M5 8 npc): which solid server objects stand near, and whether the
/// collision world says a body can stand at a place. Lifestone's own walker tool: retail has no automated walker.
/// </summary>
public interface IRouteObstacles
{
    /// <summary>The walking body's radius in metres (the player's), which a detour adds to the object's.</summary>
    float BodyRadius { get; }

    /// <summary>Appends the solid objects whose cylinders come within <paramref name="metres"/> of <paramref name="at"/> (horizontal distance to the cylinder's edge).</summary>
    /// <param name="at">Where the walker stands.</param>
    /// <param name="metres">How near.</param>
    /// <param name="into">The list to append to.</param>
    void Near(in CellFrame at, float metres, List<RouteObstacle> into);

    /// <summary>
    /// Probes the collision world for a place to stand <paramref name="offset"/> metres (horizontal, in the world's axes) from <paramref name="origin"/>.
    /// Never a guess: the answer comes from a placement of the walking body, which must not be moved.
    /// </summary>
    /// <param name="origin">The place the offset is measured from.</param>
    /// <param name="offset">The horizontal offset in metres.</param>
    /// <param name="standing">The place found, on the ground.</param>
    /// <returns>True when the body fits there.</returns>
    bool TryFindStand(in CellFrame origin, Vector2 offset, out CellFrame standing);
}

/// <summary>
/// <see cref="IRouteObstacles"/> over a physics world: the objects come from a caller's list (the session's object table in the game), the free-place probe is a
/// placement of a body-shaped physics object in <paramref name="world"/>, which holds the server objects (M5 D70).
/// </summary>
/// <param name="world">The player's physics world.</param>
/// <param name="body">The walking body's Setup (its first sphere is the foot).</param>
/// <param name="objects">The solid objects now, each time it is called.</param>
public sealed class WorldRouteObstacles(PhysicsWorld world, PhysicsSetup body, Func<IEnumerable<RouteObstacle>> objects) : IRouteObstacles
{
    private readonly PhysicsWorld _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly PhysicsSetup _body = body is { Spheres.Count: > 0 } ? body : throw new ArgumentException("the body has no spheres to stand on", nameof(body));
    private readonly Func<IEnumerable<RouteObstacle>> _objects = objects ?? throw new ArgumentNullException(nameof(objects));

    /// <inheritdoc/>
    public float BodyRadius => _body.Radius > 0f ? _body.Radius : _body.Spheres[0].Radius;

    /// <inheritdoc/>
    public void Near(in CellFrame at, float metres, List<RouteObstacle> into)
    {
        foreach (var o in _objects())
        {
            var offset = at.GetOffset(o.Centre);
            if (new Vector2(offset.X, offset.Y).Length() - o.Radius <= metres)
            {
                into.Add(o);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryFindStand(in CellFrame origin, Vector2 offset, out CellFrame standing)
    {
        standing = default;
        var cellId = origin.CellId;
        var at = origin.Frame.Origin + new Vector3(offset.X, offset.Y, 0f);
        if (!LandDefs.AdjustToOutside(ref cellId, ref at) || _world.GetCell(cellId) is not PhysLandCell land)
        {
            return false; // indoors, or no attached outdoor cell: the walker has no detour there
        }

        if (land.FindTerrainPoly(at) is not { } terrain || MathF.Abs(terrain.Plane.Normal.Z) < 1e-4f)
        {
            return false;
        }

        var plane = terrain.Plane;
        at.Z = -((plane.Normal.X * at.X) + (plane.Normal.Y * at.Y) + plane.D) / plane.Normal.Z;
        var ground = new CellFrame(cellId, new AcFrame(at, Quaternion.Identity));
        if (!_world.TryGroundZ(ground, _body.Spheres[0], out var z, out _))
        {
            return false;
        }

        var probe = new PhysicsObj(_world, _body, id: 0, new ProbeWeenie()); // not FixedWorldOnly: the server objects are what is being asked about
        try
        {
            var place = new CellFrame(cellId, new AcFrame(at with { Z = z }, Quaternion.Identity));
            if (probe.SetPosition(place, SetPositionFlags.Placement) != SetPositionError.OK || probe.Cell is null || !LandDefs.IsOutdoorCell(probe.Position.CellId))
            {
                return false;
            }

            standing = new CellFrame(probe.Position.CellId, new AcFrame(probe.Position.Frame.Origin, Quaternion.Identity));
            return true;
        }
        finally
        {
            probe.Destroy();
        }
    }

    /// <summary>The probe's body: a player, not a player killer, so other players do not stop it (as the ring's probe).</summary>
    private sealed class ProbeWeenie : IPhysicsWeenie
    {
        public bool IsCreature => true;

        public bool IsThePlayer => false;

        public bool IsPlayer => true;

        public bool IsPK => false;

        public bool IsPKLite => false;

        public bool IsImpenetrable => false;

        public bool TryGetRunRate(out float rate)
        {
            rate = 1f;
            return false;
        }

        public bool TryGetJumpVelocity(float extent, out float velocityZ)
        {
            velocityZ = 0f;
            return false;
        }

        public bool CanJump(float extent) => false;

        public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
        {
            staminaCost = 0;
            return false;
        }
    }
}

/// <summary>The collision cylinder of a Setup, for <see cref="RouteObstacle"/>s made from the server's objects.</summary>
public static class RouteObstacleShapes
{
    /// <summary>
    /// The radius of the cylinder (else the sphere) that stops a body, scale applied, or null for a Setup whose collision is a physics BSP only (a door, a building:
    /// the driver then does not walk around it, and says nothing about it) or that has none.
    /// </summary>
    /// <param name="setup">The object's Setup.</param>
    /// <param name="scale">The object's scale.</param>
    public static float? RadiusOf(PhysicsSetup setup, float scale)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var s = scale > 0f ? scale : 1f;
        float? radius = null;
        foreach (var c in setup.CylSpheres)
        {
            radius = Math.Max(radius ?? 0f, c.Radius * s);
        }

        if (radius is null)
        {
            foreach (var sphere in setup.Spheres)
            {
                radius = Math.Max(radius ?? 0f, sphere.Radius * s);
            }
        }

        return radius is > 0f ? radius : null;
    }

    /// <summary>Formats an obstacle for a log line.</summary>
    /// <param name="o">The obstacle.</param>
    public static string Describe(in RouteObstacle o) => string.Create(CultureInfo.InvariantCulture, $"{o.Name} (wcid {o.WeenieClassId}, radius {o.Radius:0.00} m)");
}
