using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Physics;

/// <summary>
/// Drives a server-object physics host from the session's object table and events (M5 item 5 slice h2, D70): the headless
/// body's way to the host, as the avatar set is the game's. A CreateObject makes the object (with its Setup, scale,
/// PhysicsState and the weenie description's <c>ObjectDescriptionFlags</c>, so a player is a player), a position update
/// moves it, a SetState changes its state (an opened door turns Ethereal), a parenting or a pickup takes it out of the
/// world, a delete destroys it. The player's own object is skipped (its physics object is the body's). Objects already
/// in the table when the feed is made are created at once.
/// </summary>
/// <remarks>
/// Headless there is no avatar step, so an object moves only by the server's positions (a creature jumps from update to
/// update) and runs no transitions of its own: the avatars' transitions (a creature stepping into a wall) exist only in the
/// game, where the avatar set feeds the same host. Headless walks therefore meet doors, lifestones, NPCs and creatures
/// as statics that move. <see cref="Tick"/> once per frame retries a door that turned solid with something in it. Game
/// thread only, like the bus.
/// </remarks>
public sealed class ObjectTableFeed : IDisposable
{
    private const uint CreatureItemType = 0x10; // ACE ItemType.Creature

    private readonly IServerObjectPhysics _host;
    private readonly WorldObjectTable _objects;
    private readonly uint _self;
    private readonly SubscriptionSet _subscriptions = new();

    /// <summary>A feed from <paramref name="events"/> and <paramref name="objects"/> into <paramref name="host"/>.</summary>
    /// <param name="host">The host.</param>
    /// <param name="objects">The session's object table.</param>
    /// <param name="events">The session's event bus.</param>
    /// <param name="self">The player's id (never fed).</param>
    public ObjectTableFeed(IServerObjectPhysics host, WorldObjectTable objects, IEventBus events, uint self)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        ArgumentNullException.ThrowIfNull(events);
        _self = self;
        _subscriptions.Add(events.Subscribe<ObjectCreated>((in ObjectCreated e) => Create(e.Guid)));
        _subscriptions.Add(events.Subscribe<ObjectUpdated>((in ObjectUpdated e) =>
        {
            if ((e.Changes & ObjectChanges.Physics) != 0)
            {
                Create(e.Guid); // its Setup or scale may have changed: made again (a recreate replaces the object)
            }
        }));
        _subscriptions.Add(events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (!e.IsSelf && e.Guid != _self)
            {
                _host.Move(e.Guid, e.Position.Normalized());
            }
        }));
        _subscriptions.Add(events.Subscribe<ObjectStateChanged>((in ObjectStateChanged e) =>
        {
            if (!e.IsSelf && e.Guid != _self)
            {
                _host.SetState(e.Guid, (uint)e.State);
            }
        }));
        _subscriptions.Add(events.Subscribe<ObjectParented>((in ObjectParented e) => _host.LeaveWorld(e.Child)));
        _subscriptions.Add(events.Subscribe<ObjectPickedUp>((in ObjectPickedUp e) => _host.LeaveWorld(e.Guid)));
        _subscriptions.Add(events.Subscribe<ObjectDeleted>((in ObjectDeleted e) => _host.Remove(e.Guid)));
        foreach (var o in _objects.All)
        {
            Create(o);
        }
    }

    /// <summary>Once per frame: the host's retry of a door that turned solid with something in it.</summary>
    public void Tick() => _host.Tick();

    /// <summary>Releases the subscriptions (the host and its objects stay).</summary>
    public void Dispose() => _subscriptions.Dispose();

    private void Create(uint guid)
    {
        if (_objects.TryGet(guid, out var o))
        {
            Create(o);
        }
    }

    private void Create(WorldObject o)
    {
        if (o.IsSelf || o.Guid == _self)
        {
            return;
        }

        var physics = o.Physics;
        var description = new ServerObjectDescription(
            o.Guid,
            physics.SetupId ?? 0,
            physics.Scale ?? 1f,
            (uint)o.State,
            IsCreature: (o.ItemType & CreatureItemType) != 0,
            IsSelf: false,
            DescriptionFlags: o.DescriptionFlags);
        _host.Create(description, o.Parent is null && o.Position is { CellId: not 0 } at ? at.Normalized() : null);
    }
}
