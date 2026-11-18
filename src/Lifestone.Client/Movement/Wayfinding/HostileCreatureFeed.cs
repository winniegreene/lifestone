using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The creature layer's feed from the client's object table (nav-creature-avoid): every hostile creature the client sees, as the radar paints
/// one red (<c>RadarRules</c>: the creature item type, the attackable bit, not a player) and the census counts one (not a vendor): its CreateObject's
/// ItemType has the creature bit (0x10), its ObjectDescriptionFlags the attackable bit (0x10) and neither the player (0x8) nor the vendor (0x200)
/// bit, it has a position, and it is not known to be dead (a health fraction above zero or none yet; its forward motion is not Dead). Each is a
/// disc of <see cref="NavCreatures.AggroRadius"/> over its <see cref="CreatureAwareness"/> and its own radius: its Setup's radius (retail
/// <c>CSetup::radius</c>, through the injected <c>setupRadius</c> lookup, <see cref="SetupRadii"/> over the dats) times its scale (the physics
/// description's ObjScale), as the physics object's own radius is (<c>PhysicsObj.Radius</c>; ACE @95ef426d measures the awareness between the two
/// cylinders, PhysicsObj.cs:2519); <see cref="NavCreatures.UnknownCreatureRadiusMetres"/> when the Setup cannot be read (logged once). ACE alerts only an idle creature without a
/// move-exclude tolerance (<c>AlertMonster</c>, Creature_Combat.cs:1121-1151 @95ef426d); the client sees neither, so every hostile creature counts.
/// M6 16 E1f: the awareness is the pinned range (the default, as before) or, under <c>--walk-awareness=own</c>, the kind's own range by the world database (<see cref="Bestiary.OwnAwarenessMetres"/>); the session's
/// <see cref="WalkAwareness"/> on the object table says which and, in both modes, reports what the other would have changed (<c>awareness-shadow:</c>). <see cref="Fill"/>'s <c>from</c> is the body's place: the shadow measures
/// each creature from it. Allocates nothing once the list has grown.
/// </summary>
/// <param name="objects">The session's object table.</param>
/// <param name="self">The player's id (never on the layer).</param>
/// <param name="log">The log (the first creature whose radius falls back to 1 m), or null.</param>
/// <param name="setupRadius">A Setup's radius by its id (<see cref="SetupRadii"/>), null when it is not in the dats; null for no lookup (every disc then
/// assumes 1 m, logged once as an unread Setup radius: every host passes <see cref="SetupRadii"/> since M5 item 2 g5, so a feed without one is a host that was not wired).</param>
public sealed class HostileCreatureFeed(WorldObjectTable objects, uint self, ILogSink? log = null, Func<uint, float?>? setupRadius = null) : INavCreatureFeed
{
    /// <summary>The ItemType bit of a creature (ACE <c>ItemType.Creature</c>).</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>The ObjectDescriptionFlags bit of an attackable object (<c>BF_ATTACKABLE</c>).</summary>
    public const uint AttackableFlag = 0x10;

    /// <summary>The ObjectDescriptionFlags bit of a vendor (<c>BF_VENDOR</c>).</summary>
    public const uint VendorFlag = 0x200;

    private readonly WorldObjectTable _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private bool _radiusLogged;

    /// <summary>Whether <paramref name="o"/> is a hostile creature as the feed counts one (alive, placed, not the player).</summary>
    /// <param name="o">The object.</param>
    /// <param name="self">The player's id.</param>
    public static bool IsHostile(WorldObject o, uint self)
    {
        ArgumentNullException.ThrowIfNull(o);
        var flags = o.DescriptionFlags;
        return o.Guid != self && !o.IsSelf && o.Position is not null
            && (o.ItemType & CreatureItemType) != 0 && (flags & AttackableFlag) != 0 && (flags & (WorldObjectTable.PlayerFlag | VendorFlag)) == 0
            && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };
    }

    /// <inheritdoc/>
    public void Fill(Vector2 from, float maxMetres, List<NavCreature> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        float max2 = maxMetres * maxMetres;
        foreach (var o in _objects.All)
        {
            if (!IsHostile(o, self))
            {
                continue;
            }

            var centre = NavMap.FromCellFrame(o.Position!.Value).Global;
            if (Vector2.DistanceSquared(centre, from) > max2)
            {
                continue;
            }

            // M6 16 E1f: the range the walk keeps is the session's choice (--walk-awareness: the pinned range by default, as before, or the kind's own); the shadow names a kind whose two ranges differ and a leg's passes between the two discs
            var wcid = o.Weenie.WeenieClassId;
            var body = RadiusOf(o);
            var pinned = Bestiary.AwarenessMetres(wcid);
            var own = Bestiary.OwnAwarenessMetres(wcid);
            var walk = _objects.WalkAwareness;
            var kept = walk.Kept(wcid, o.Name, pinned, own, log);
            walk.Seen(o.Guid, wcid, o.Name, Vector2.Distance(centre, from), NavCreatures.AggroRadius(pinned, body), NavCreatures.AggroRadius(own, body));
            into.Add(new NavCreature(o.Guid, centre, NavCreatures.AggroRadius(kept, body), o.Position!.Value.Origin.Z)); // its height too, for a room route's storey band (M5 2 fix F2)
        }
    }

    /// <summary>
    /// A Setup's radius by its id from <paramref name="dats"/> (retail <c>CSetup::radius</c>, <see cref="ModelAssembly.Radius"/>), or null for a Setup not in
    /// the dats; cached per id, so a warm feed allocates nothing. For <see cref="HostileCreatureFeed"/>'s <c>setupRadius</c>. Game thread only.
    /// </summary>
    /// <param name="dats">The dats.</param>
    public static Func<uint, float?> SetupRadii(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return new SetupShapes(dats).Radius; // the same numbers as before; the lookup's owner also reads a Setup's collision shape for the NPC bodies (SetupShapes.Of)
    }

    /// <summary>The creature's own radius: its Setup's radius times its scale, or <see cref="NavCreatures.UnknownCreatureRadiusMetres"/> (logged once) when it cannot be read.</summary>
    private float RadiusOf(WorldObject o)
    {
        var physics = o.Created.Physics;
        if (setupRadius is not null && physics.SetupId is { } id and not 0 && setupRadius(id) is { } radius and > 0f)
        {
            return radius * (physics.Scale is { } scale and > 0f ? scale : 1f);
        }

        if (!_radiusLogged)
        {
            _radiusLogged = true;
            if (setupRadius is null)
            {
                log?.Warn("wayfinding", "unread Setup radius: the host gave the creature feed no Setup lookup (HostileCreatureFeed.SetupRadii), so every aggro disc assumes a 1 m creature", new LogField("creature", o.Name), new LogField("assumed_m", NavCreatures.UnknownCreatureRadiusMetres));
            }
            else
            {
                log?.Warn("wayfinding", "a creature's Setup radius could not be read (no Setup id, or not in the dats); its aggro disc assumes 1 m", new LogField("creature", o.Name), new LogField("setup", physics.SetupId ?? 0), new LogField("assumed_m", NavCreatures.UnknownCreatureRadiusMetres));
            }
        }

        return NavCreatures.UnknownCreatureRadiusMetres;
    }
}

/// <summary>
/// A Setup's two sizes by its id, over the dats, cached per id (a warm lookup allocates nothing; game thread only): its radius (retail <c>CSetup::radius</c>, the model's bounds, which the aggro discs and a creature's own
/// radius read) and the radius of its collision shape at walking height (<see cref="ShapeRadius"/>: what the physics makes a walker stop at). <see cref="HostileCreatureFeed.SetupRadii"/> hands out <see cref="Radius"/>;
/// <see cref="NpcBodyFeed"/> finds the shape through the same lookup (<see cref="Of"/>), so no host passes a second one.
/// </summary>
/// <param name="dats">The dats.</param>
public sealed class SetupShapes(DatSet dats)
{
    /// <summary>The height a walker's body reaches, in metres: a shape wholly above it (a sign over a door, a lamp on a pole) blocks no one on the ground. The human Setup's height is 1.84 at scale 1; Lifestone's own round figure.</summary>
    public const float WalkingHeightMetres = 2f;

    private readonly DatSet _dats = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly Dictionary<uint, (float? Radius, float? Shape)> _cache = [];

    /// <summary>The Setup's radius (<c>CSetup::radius</c>), or null for a Setup not in the dats.</summary>
    /// <param name="id">The Setup id.</param>
    public float? Radius(uint id) => Read(id).Radius;

    /// <summary>
    /// The radius of the Setup's collision shape at walking height, unscaled: the farthest any of its cylinders or spheres that stands between the ground and <see cref="WalkingHeightMetres"/> reaches from the Setup's own
    /// axis (the shapes retail's <c>CPartArray</c> collides a moving body with: <c>CSetup::cylsphere</c> and <c>CSetup::sphere</c>). Null when the Setup is not in the dats or has no such shape (its parts' own geometry is its
    /// collision, which this lookup does not measure).
    /// </summary>
    /// <param name="id">The Setup id.</param>
    public float? ShapeRadius(uint id) => Read(id).Shape;

    /// <summary>The shapes behind a radius lookup that <see cref="HostileCreatureFeed.SetupRadii"/> made, or null for any other lookup (a test's lambda, no lookup).</summary>
    /// <param name="setupRadius">The lookup.</param>
    public static SetupShapes? Of(Func<uint, float?>? setupRadius) => setupRadius?.Target as SetupShapes;

    private (float? Radius, float? Shape) Read(uint id)
    {
        if (_cache.TryGetValue(id, out var read))
        {
            return read;
        }

        if (!_dats.TryGetPortal<Setup>(id, out var setup))
        {
            return _cache[id] = (null, null);
        }

        var model = ModelAssembly.FromSetup(id, setup);
        return _cache[id] = (model.Radius, ShapeRadiusOf(model.CylSpheres, model.Spheres));
    }

    /// <summary>The farthest reach from the axis of the cylinders and spheres that stand in the walking height, or null when none does.</summary>
    /// <param name="cylinders">The Setup's cylinder-spheres.</param>
    /// <param name="spheres">The Setup's spheres.</param>
    public static float? ShapeRadiusOf(IReadOnlyList<ModelCylSphere> cylinders, IReadOnlyList<ModelSphere> spheres)
    {
        ArgumentNullException.ThrowIfNull(cylinders);
        ArgumentNullException.ThrowIfNull(spheres);
        float? reach = null;
        foreach (var c in cylinders)
        {
            if (c.Radius > 0f && c.LowPoint.Z < WalkingHeightMetres && c.LowPoint.Z + c.Height > 0f)
            {
                reach = MathF.Max(reach ?? 0f, new Vector2(c.LowPoint.X, c.LowPoint.Y).Length() + c.Radius);
            }
        }

        foreach (var s in spheres)
        {
            if (s.Radius > 0f && s.Center.Z - s.Radius < WalkingHeightMetres && s.Center.Z + s.Radius > 0f)
            {
                reach = MathF.Max(reach ?? 0f, new Vector2(s.Center.X, s.Center.Y).Length() + s.Radius);
            }
        }

        return reach;
    }
}
