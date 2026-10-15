using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Avatars;

/// <summary>What the <see cref="SessionAvatarAdapter"/> has done (counters; reset only by making a new adapter).</summary>
public sealed class SessionAvatarAdapterStats
{
    /// <summary>Object events taken from the bus.</summary>
    public long Received { get; internal set; }

    /// <summary>Events the avatar set applied.</summary>
    public long Applied { get; internal set; }

    /// <summary>Events retail ignores (the player's echoes, positions left to the player's body).</summary>
    public long Ignored { get; internal set; }

    /// <summary>Events for an object the set does not have (the reducer should have parked them).</summary>
    public long Unknown { get; internal set; }

    /// <summary>Events the set could not apply (a Setup that is missing, a part that does not load).</summary>
    public long Failed { get; internal set; }

    /// <summary>Events dropped because the queue was full (a loud error; never in a normal run).</summary>
    public long Dropped { get; internal set; }

    /// <summary>Events discarded by <see cref="SessionAvatarAdapter.Clear"/>.</summary>
    public long Discarded { get; internal set; }

    /// <summary>The slowest single event so far, in milliseconds (a create is the slow kind).</summary>
    public double SlowestEventMilliseconds { get; internal set; }

    /// <summary>The longest the queue has been.</summary>
    public int MaxPending { get; internal set; }

    /// <summary>Pumps that stopped on the time budget with events left.</summary>
    public long BudgetStops { get; internal set; }

    /// <summary>The player's own CreateObject, applied, with the player's Hidden bit set (the server's arrival).</summary>
    public long PlayerCreatedHidden { get; internal set; }

    /// <summary>Times the player's Hidden bit was cleared (the UnHide script, 0x75, started: materialising).</summary>
    public long PlayerUnhides { get; internal set; }

    /// <summary>Times the player's Hidden bit was set after its create.</summary>
    public long PlayerHides { get; internal set; }
}

/// <summary>
/// AV13: the bridge from the session's classified object events to the <see cref="AvatarSet"/>. It subscribes to the bus, keeps the events in order
/// in a queue (a CreateObject burst of a landblock never costs one frame its whole cost), and applies them with <see cref="Pump"/> inside the
/// caller's time budget. Nothing is dropped, reordered or filtered here: the reducer already owns the sequence stamps and the parking
/// (m2-plan section 1.4), and the avatar set owns retail's object semantics (<c>SmartBox::HandleCreateObject</c> and the handlers after it,
/// docs/avatars.md section 9), so every event is already newer and in order.
/// </summary>
/// <remarks>
/// <para><b>What it maps.</b> CreateObject (<see cref="ObjectCreated"/>, with the description as the session holds it when the event is published: the
/// create's <see cref="AvatarCreate"/> is built at that moment, so an object that is deleted before the queue reaches it still has the form it had), UpdateObject
/// (<see cref="ObjectUpdated"/>), DeleteObject, UpdatePosition (<see cref="ObjectMoved"/>: remote positions move the object; the player's own are its
/// body's, and place it only while no body is bound), UpdateMotion, VectorUpdate, SetState (Hidden goes through the portal track's
/// <c>HiddenTransition</c>, which plays the Hidden and UnHide scripts: materialising), ParentEvent, PickupEvent and ObjDescEvent. Sounds and scripts
/// the server plays on an object (<see cref="ObjectSoundPlayed"/>, <see cref="ObjectScriptPlayed"/>) are not mapped yet
/// (<c>TODO(g5-w10-scripts)</c>, logged when the adapter is made).</para>
/// <para><b>Threads.</b> Game thread only: the bus delivers on the session's thread and the set is not thread safe. The queue allocates nothing per event
/// once grown (a CreateObject allocates its <see cref="AvatarCreate"/>, once per create).</para>
/// <para><b>Time.</b> Every apply is stamped with <c>now()</c> at the moment it is applied, the host's frame clock, which is the clock the set steps on.</para>
/// </remarks>
public sealed class SessionAvatarAdapter : IDisposable
{
    /// <summary>The queue's length at which events are dropped (loudly): far above any burst a landblock sends.</summary>
    public const int QueueLimit = 65536;

    private const string LogCategory = "avatars";
    private const double SlowEventMilliseconds = 4.0;

    private readonly AvatarSet _avatars;
    private readonly GameState _state;
    private readonly Func<double> _now;
    private readonly ILogSink _log;
    private readonly SubscriptionSet _subscriptions = new();
    private readonly Queue<Item> _queue = new();
    private bool _overflowLogged;
    private bool _disposed;

    /// <param name="events">The session's bus.</param>
    /// <param name="state">The session's read model (a create reads the object it names).</param>
    /// <param name="avatars">The set the events are applied to.</param>
    /// <param name="now">The host's frame clock, in seconds.</param>
    /// <param name="log">The log, or none.</param>
    /// <param name="immediate">True: every event is applied at once, inside the bus's dispatch (tests, replays and tools without a frame); false: queued for <see cref="Pump"/>.</param>
    public SessionAvatarAdapter(IEventBus events, GameState state, AvatarSet avatars, Func<double> now, ILogSink? log = null, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(avatars);
        ArgumentNullException.ThrowIfNull(now);
        _state = state;
        _avatars = avatars;
        _now = now;
        _log = log ?? NullLogSink.Instance;
        Immediate = immediate;
        _subscriptions.Add(events.Subscribe<ObjectCreated>(OnCreated));
        _subscriptions.Add(events.Subscribe<ObjectUpdated>(OnUpdated));
        _subscriptions.Add(events.Subscribe<ObjectDeleted>(OnDeleted));
        _subscriptions.Add(events.Subscribe<ObjectMoved>(OnMoved));
        _subscriptions.Add(events.Subscribe<ObjectMotionChanged>(OnMotion));
        _subscriptions.Add(events.Subscribe<ObjectVectorChanged>(OnVector));
        _subscriptions.Add(events.Subscribe<ObjectStateChanged>(OnState));
        _subscriptions.Add(events.Subscribe<ObjectParented>(OnParented));
        _subscriptions.Add(events.Subscribe<ObjectPickedUp>(OnPickedUp));
        _subscriptions.Add(events.Subscribe<ObjectDescChanged>(OnDesc));
        Write(LogSeverity.Warning, "TODO(g5-w10-scripts): scripts and sounds the server plays on an object (F755, F750) are not played yet; only the Hidden and UnHide scripts of the state bit are", null);
    }

    /// <summary>True when events are applied at once instead of queued.</summary>
    public bool Immediate { get; }

    /// <summary>Counters.</summary>
    public SessionAvatarAdapterStats Stats { get; } = new();

    /// <summary>Events waiting for <see cref="Pump"/>.</summary>
    public int Pending => _queue.Count;

    /// <summary>
    /// Applies the queued events in order until the queue is empty or <paramref name="deadlineTimestamp"/> (<see cref="Stopwatch.GetTimestamp"/> units) has
    /// passed. At least one event is applied per call, so the queue always drains. Returns the number applied.
    /// </summary>
    public int Pump(long deadlineTimestamp)
    {
        var applied = 0;
        while (_queue.Count > 0)
        {
            if (applied > 0 && Stopwatch.GetTimestamp() > deadlineTimestamp)
            {
                Stats.BudgetStops++;
                break;
            }

            Apply(_queue.Dequeue());
            applied++;
        }

        return applied;
    }

    /// <summary>Applies every queued event now.</summary>
    public int Drain() => Pump(long.MaxValue);

    /// <summary>Forgets the queued events (the world they belong to is gone: a log off).</summary>
    public void Clear()
    {
        Stats.Discarded += _queue.Count;
        _queue.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        Clear();
    }

    // ----- the bus handlers: queue (or apply) in order -----

    private void OnCreated(in ObjectCreated e)
    {
        if (!_state.Objects.TryGet(e.Guid, out var obj))
        {
            Write(LogSeverity.Warning, "an ObjectCreated names an object the session does not hold", e.Guid);
            return;
        }

        Enqueue(new Item { Kind = Kind.Created, Guid = e.Guid, Created = e, Create = AvatarWire.FromWorldObject(obj) });
    }

    private void OnUpdated(in ObjectUpdated e)
    {
        if (!_state.Objects.TryGet(e.Guid, out var obj))
        {
            Write(LogSeverity.Warning, "an ObjectUpdated names an object the session does not hold", e.Guid);
            return;
        }

        Enqueue(new Item { Kind = Kind.Updated, Guid = e.Guid, Updated = e, Create = AvatarWire.FromWorldObject(obj) });
    }

    private void OnDeleted(in ObjectDeleted e) => Enqueue(new Item { Kind = Kind.Deleted, Guid = e.Guid, Deleted = e });

    private void OnMoved(in ObjectMoved e) => Enqueue(new Item { Kind = Kind.Moved, Guid = e.Guid, Moved = e });

    private void OnMotion(in ObjectMotionChanged e) => Enqueue(new Item { Kind = Kind.Motion, Guid = e.Guid, Motion = e });

    private void OnVector(in ObjectVectorChanged e) => Enqueue(new Item { Kind = Kind.Vector, Guid = e.Guid, Vector = e });

    private void OnState(in ObjectStateChanged e) => Enqueue(new Item { Kind = Kind.State, Guid = e.Guid, State = e });

    private void OnParented(in ObjectParented e) => Enqueue(new Item { Kind = Kind.Parented, Guid = e.Child, Parented = e });

    private void OnPickedUp(in ObjectPickedUp e) => Enqueue(new Item { Kind = Kind.PickedUp, Guid = e.Guid, PickedUp = e });

    private void OnDesc(in ObjectDescChanged e) => Enqueue(new Item { Kind = Kind.Desc, Guid = e.Guid, Desc = e });

    private void Enqueue(in Item item)
    {
        Stats.Received++;
        if (Immediate)
        {
            Apply(item);
            return;
        }

        if (_queue.Count >= QueueLimit)
        {
            Stats.Dropped++;
            if (!_overflowLogged)
            {
                _overflowLogged = true;
                Write(LogSeverity.Error, string.Create(CultureInfo.InvariantCulture, $"the avatar adapter's queue is full ({QueueLimit} events); events are dropped and the avatars will disagree with the session"), item.Guid);
            }

            return;
        }

        _queue.Enqueue(item);
        if (_queue.Count > Stats.MaxPending)
        {
            Stats.MaxPending = _queue.Count;
        }
    }

    // ----- applying -----

    private void Apply(in Item item)
    {
        var started = Stopwatch.GetTimestamp();
        var now = _now();
        var result = item.Kind switch
        {
            Kind.Created => ApplyCreated(item, now),
            Kind.Updated => _avatars.Apply(item.Updated, item.Create!, now),
            Kind.Deleted => _avatars.Apply(item.Deleted),
            Kind.Moved => _avatars.Apply(item.Moved, now),
            Kind.Motion => _avatars.Apply(item.Motion, now),
            Kind.Vector => _avatars.Apply(item.Vector),
            Kind.State => ApplyState(item),
            Kind.Parented => _avatars.Apply(item.Parented),
            Kind.PickedUp => _avatars.Apply(item.PickedUp),
            Kind.Desc => _avatars.Apply(item.Desc),
            _ => AvatarApply.Failed,
        };
        switch (result)
        {
            case AvatarApply.Applied:
                Stats.Applied++;
                break;
            case AvatarApply.Ignored:
                Stats.Ignored++;
                break;
            case AvatarApply.UnknownObject:
                Stats.Unknown++;
                break;
            default:
                Stats.Failed++;
                break;
        }

        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (ms > Stats.SlowestEventMilliseconds)
        {
            Stats.SlowestEventMilliseconds = ms;
        }

        if (ms > SlowEventMilliseconds && _log.IsEnabled(LogSeverity.Debug))
        {
            LogField[] fields = [new LogField("kind", item.Kind.ToString()), new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{item.Guid:X8}")), new LogField("ms", ms.ToString("0.0", CultureInfo.InvariantCulture))];
            _log.Write(LogSeverity.Debug, LogCategory, "slow avatar event", fields);
        }
    }

    private AvatarApply ApplyCreated(in Item item, double now)
    {
        var result = _avatars.Apply(item.Created, item.Create!, now);
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            var create = item.Create!;
            var desc = create.ObjDesc;
            LogField[] fields =
            [
                new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{create.Guid:X8}")),
                new LogField("setup", string.Create(CultureInfo.InvariantCulture, $"0x{create.SetupId:X8}")),
                new LogField("self", item.Created.IsSelf.ToString()),
                new LogField("result", result.ToString()),
                new LogField("palette", string.Create(CultureInfo.InvariantCulture, $"0x{desc.PaletteId:X8}")),
                new LogField("sub_palettes", desc.SubPalettes.Count.ToString(CultureInfo.InvariantCulture)),
                new LogField("texture_changes", desc.TextureChanges.Count.ToString(CultureInfo.InvariantCulture)),
                new LogField("part_changes", desc.PartChanges.Count.ToString(CultureInfo.InvariantCulture)),
                new LogField("state", string.Create(CultureInfo.InvariantCulture, $"0x{create.PhysicsState:X8}")),
            ];
            _log.Write(LogSeverity.Debug, LogCategory, "avatar created", fields);
        }

        if (result == AvatarApply.Applied && item.Created.IsSelf && (item.Create!.PhysicsState & AvatarStateBits.Hidden) != 0)
        {
            Stats.PlayerCreatedHidden++;
        }

        return result;
    }

    private AvatarApply ApplyState(in Item item)
    {
        var result = _avatars.Apply(item.State);
        if (result == AvatarApply.Applied && item.State.IsSelf)
        {
            var was = ((uint)item.State.Previous & AvatarStateBits.Hidden) != 0;
            var now = ((uint)item.State.State & AvatarStateBits.Hidden) != 0;
            if (was && !now)
            {
                Stats.PlayerUnhides++;
            }
            else if (!was && now)
            {
                Stats.PlayerHides++;
            }
        }

        return result;
    }

    private void Write(LogSeverity severity, string message, uint? guid)
    {
        if (!_log.IsEnabled(severity))
        {
            return;
        }

        LogField[] fields = guid is { } g ? [new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{g:X8}"))] : [];
        _log.Write(severity, LogCategory, message, fields);
    }

    private enum Kind : byte
    {
        Created,
        Updated,
        Deleted,
        Moved,
        Motion,
        Vector,
        State,
        Parented,
        PickedUp,
        Desc,
    }

    /// <summary>One queued event: its kind and the one payload that kind uses (a struct, so queueing allocates nothing).</summary>
    private struct Item
    {
        public Kind Kind;
        public uint Guid;
        public AvatarCreate? Create;
        public ObjectCreated Created;
        public ObjectUpdated Updated;
        public ObjectDeleted Deleted;
        public ObjectMoved Moved;
        public ObjectMotionChanged Motion;
        public ObjectVectorChanged Vector;
        public ObjectStateChanged State;
        public ObjectParented Parented;
        public ObjectPickedUp PickedUp;
        public ObjectDescChanged Desc;
    }
}
