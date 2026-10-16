using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Bus;

/// <summary>
/// The session's event bus (A0; docs/session.md §2 and §5): typed struct events from the reducer, dispatched
/// synchronously in subscription order, plus the raw message tap. Publishing allocates nothing.
/// </summary>
/// <remarks>
/// <para><b>Channels.</b> One channel per event type, found by a per-type index (<see cref="EventTypeId{T}"/>), so a
/// publish is an array lookup, never a dictionary or a boxing conversion. Handlers sit in copy-on-write arrays: a
/// dispatch walks the array it started with, and a subscription disposed during it is skipped from then on.</para>
/// <para><b>Re-entrancy.</b> An event published while a dispatch runs (by a handler, or by an action a handler issued) is
/// copied into its channel's pending ring and its channel recorded in one FIFO; the outermost publish drains that FIFO
/// after its own dispatch, so events reach handlers in publish order, and each event's handlers finish before the next
/// event starts.</para>
/// <para><b>Failures.</b> A handler that throws is caught: its first failure is logged as an ERROR (category
/// <c>session</c>, with the handler's method and the event type; the Godot smoke fails on ERROR lines), later ones of the
/// same subscription at DEBUG, and every one is counted (<see cref="HandlerFailures"/>). The other handlers still run,
/// and the exception never reaches the reducer. Tests may set <see cref="RethrowHandlerExceptions"/>.</para>
/// <para><b>Raw tap.</b> A separate handler list for message bytes, dispatched at once (the bytes are only valid during
/// the call, so they are never queued). With no raw subscriber <see cref="PublishRaw"/> returns at once, and the session
/// skips the tap work altogether (<see cref="HasRawSubscribers"/>).</para>
/// <para><b>Threads.</b> Game thread only, once the session is bound (<see cref="GameThreadBinding"/>): subscribing,
/// unsubscribing and publishing from another thread throw.</para>
/// </remarks>
internal sealed class EventBus : IEventBus
{
    private readonly ILogSink _log;
    private readonly GameThreadBinding? _thread;
    private Channel?[] _channels = new Channel?[64];
    private Channel[] _fifo = new Channel[16];
    private int _fifoHead;
    private int _fifoCount;
    private int _depth;
    private RawSubscription[] _raw = [];
    private readonly TimeProvider? _time;
    private readonly long _slowTicks;
    private readonly Delegate?[] _slowHandlers = new Delegate?[SlowHandlerCapacity];
    private readonly Type?[] _slowEvents = new Type?[SlowHandlerCapacity];
    private readonly long[] _slowElapsed = new long[SlowHandlerCapacity];
    private int _slowCount;

    /// <summary>How many handlers over <see cref="SlowHandlerMilliseconds"/> the bus remembers between two <see cref="ResetSlowHandlers"/>.</summary>
    private const int SlowHandlerCapacity = 8;

    /// <summary>A handler that takes this long (1 ms) is remembered for <see cref="DescribeSlowHandlers"/>.</summary>
    internal const double SlowHandlerMilliseconds = 1.0;

    /// <summary>A bus logging to <paramref name="log"/>, bound to <paramref name="thread"/> when given.</summary>
    /// <param name="log">Where handler failures are logged.</param>
    /// <param name="thread">The session's game-thread binding, or null for a bus without one (tests).</param>
    /// <param name="time">The clock that times the handlers (two reads per handler call, no allocation), or null for a bus that does not time them.</param>
    internal EventBus(ILogSink log, GameThreadBinding? thread = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _thread = thread;
        _time = time;
        _slowTicks = time is null ? long.MaxValue : (long)(time.TimestampFrequency * SlowHandlerMilliseconds / 1000.0);
    }

    /// <summary>Forgets the slow handlers noted so far (called at the start of each inbound message).</summary>
    internal void ResetSlowHandlers() => _slowCount = 0;

    /// <summary>
    /// The handlers that took 1 ms or more since the last <see cref="ResetSlowHandlers"/>, as <c>Type.Method(Event)=ms</c> separated by semicolons (the subscriber's type is its
    /// target's, or the declaring type of a static or a closure's method); empty when none did. Allocates only when called.
    /// </summary>
    internal string DescribeSlowHandlers()
    {
        if (_slowCount == 0)
        {
            return string.Empty;
        }

        var parts = new string[_slowCount];
        for (var i = 0; i < _slowCount; i++)
        {
            var handler = _slowHandlers[i]!;
            var owner = handler.Target?.GetType() ?? handler.Method.DeclaringType;
            var milliseconds = _time!.GetElapsedTime(0, _slowElapsed[i]).TotalMilliseconds;
            parts[i] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{owner?.Name}.{handler.Method.Name}({_slowEvents[i]?.Name})={milliseconds:0.0}");
        }

        return string.Join(';', parts);
    }

    /// <summary>Handler exceptions caught so far (typed and raw).</summary>
    internal long HandlerFailures { get; private set; }

    /// <summary>For tests: after logging and counting, rethrow a handler's exception out of the publish.</summary>
    internal bool RethrowHandlerExceptions { get; set; }

    /// <summary>True while some raw subscriber listens; the session's tap does nothing without one.</summary>
    internal bool HasRawSubscribers => _raw.Length > 0;

    /// <summary>How many typed handlers listen now, over every event type (a test reads it: a screen that ends leaves the count where it began, review 2.2-R2).</summary>
    internal int HandlerCount
    {
        get
        {
            var count = 0;
            foreach (var channel in _channels)
            {
                count += channel?.HandlerCount ?? 0;
            }

            return count;
        }
    }

    /// <summary>Events queued by publishes during a dispatch and not yet dispatched.</summary>
    internal int PendingCount => _fifoCount;

    /// <inheritdoc/>
    public IDisposable Subscribe<T>(BusHandler<T> handler)
        where T : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        _thread?.Check("IEventBus.Subscribe");
        var channel = ChannelFor<T>(create: true)!;
        var subscription = new Subscription<T>(this, channel, handler);
        channel.Handlers = [.. channel.Handlers, subscription];
        return subscription;
    }

    /// <inheritdoc/>
    public IDisposable SubscribeRaw(RawMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _thread?.Check("IEventBus.SubscribeRaw");
        var subscription = new RawSubscription(this, handler);
        _raw = [.. _raw, subscription];
        return subscription;
    }

    /// <inheritdoc/>
    public long Count<T>()
        where T : struct, IGameEvent =>
        ChannelFor<T>(create: false)?.Published ?? 0;

    /// <summary>
    /// Publishes <paramref name="e"/>: dispatched now, or, during a dispatch, queued to follow it (see the remarks).
    /// Allocation-free once the channel exists (its first publish or subscription creates it).
    /// </summary>
    internal void Publish<T>(in T e)
        where T : struct, IGameEvent
    {
        _thread?.Check("EventBus.Publish");
        var channel = ChannelFor<T>(create: true)!;
        channel.Published++;
        if (_depth > 0)
        {
            channel.Enqueue(in e);
            EnqueueChannel(channel);
            return;
        }

        Dispatch(channel, in e);
        DrainPending();
    }

    /// <summary>
    /// Hands one game message to the raw subscribers, in subscription order. Returns at once without subscribers.
    /// </summary>
    internal void PublishRaw(MessageDirection direction, double time, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body)
    {
        var subscriptions = _raw;
        if (subscriptions.Length == 0)
        {
            return;
        }

        _thread?.Check("EventBus.PublishRaw");
        foreach (var subscription in subscriptions)
        {
            if (!subscription.Active)
            {
                continue;
            }

            try
            {
                subscription.Handler(direction, time, fragmentSequence, queue, body);
            }
            catch (Exception ex)
            {
                if (OnHandlerFailed(subscription, subscription.Handler, "raw message", ex))
                {
                    throw;
                }
            }
        }
    }

    private void Dispatch<T>(Channel<T> channel, in T e)
        where T : struct, IGameEvent
    {
        var subscriptions = channel.Handlers;
        _depth++;
        try
        {
            foreach (var subscription in subscriptions)
            {
                if (!subscription.Active)
                {
                    continue;
                }

                var startedAt = _time is null ? 0L : _time.GetTimestamp();
                try
                {
                    subscription.Handler(in e);
                }
                catch (Exception ex)
                {
                    if (OnHandlerFailed(subscription, subscription.Handler, typeof(T).Name, ex))
                    {
                        throw;
                    }
                }

                if (_time is not null)
                {
                    var elapsed = _time.GetTimestamp() - startedAt;
                    if (elapsed >= _slowTicks && _slowCount < SlowHandlerCapacity)
                    {
                        _slowHandlers[_slowCount] = subscription.Handler;
                        _slowEvents[_slowCount] = typeof(T);
                        _slowElapsed[_slowCount] = elapsed;
                        _slowCount++;
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void DrainPending()
    {
        while (_fifoCount > 0)
        {
            var channel = _fifo[_fifoHead];
            _fifo[_fifoHead] = null!;
            _fifoHead = (_fifoHead + 1) % _fifo.Length;
            _fifoCount--;
            channel.DispatchNext();
        }
    }

    private void EnqueueChannel(Channel channel)
    {
        if (_fifoCount == _fifo.Length)
        {
            var grown = new Channel[_fifo.Length * 2];
            for (var i = 0; i < _fifoCount; i++)
            {
                grown[i] = _fifo[(_fifoHead + i) % _fifo.Length];
            }

            _fifo = grown;
            _fifoHead = 0;
        }

        _fifo[(_fifoHead + _fifoCount) % _fifo.Length] = channel;
        _fifoCount++;
    }

    private Channel<T>? ChannelFor<T>(bool create)
        where T : struct, IGameEvent
    {
        var id = EventTypeId<T>.Value;
        if ((uint)id < (uint)_channels.Length && _channels[id] is Channel<T> channel)
        {
            return channel;
        }

        if (!create)
        {
            return null;
        }

        if (id >= _channels.Length)
        {
            Array.Resize(ref _channels, Math.Max(id + 1, _channels.Length * 2));
        }

        var created = new Channel<T>(this);
        _channels[id] = created;
        return created;
    }

    /// <summary>Logs and counts a handler failure; true when the exception should be rethrown (tests).</summary>
    private bool OnHandlerFailed(SubscriptionBase subscription, Delegate handler, string eventName, Exception ex)
    {
        HandlerFailures++;
        subscription.Failures++;
        var method = $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}";
        if (subscription.Failures == 1)
        {
            _log.Error(
                SessionLog.Category,
                $"event handler {method} threw on {eventName}: {ex.GetType().Name}: {ex.Message} (logged once per handler; the other handlers ran)",
                new LogField("handler", method),
                new LogField("event", eventName));
        }
        else
        {
            _log.Debug(
                SessionLog.Category,
                $"event handler {method} threw again on {eventName}: {ex.GetType().Name}",
                new LogField("failures", subscription.Failures));
        }

        return RethrowHandlerExceptions;
    }

    /// <summary>A channel of any event type: its publish count and its pending events.</summary>
    private abstract class Channel
    {
        /// <summary>Events of this type published so far.</summary>
        public long Published { get; set; }

        /// <summary>How many typed handlers listen on this channel.</summary>
        public abstract int HandlerCount { get; }

        /// <summary>Dispatches the oldest pending event of this channel.</summary>
        public abstract void DispatchNext();
    }

    /// <summary>One event type's handlers and pending ring.</summary>
    private sealed class Channel<T>(EventBus bus) : Channel
        where T : struct, IGameEvent
    {
        private T[] _pending = new T[4];
        private int _head;
        private int _count;

        /// <summary>The handlers, replaced (never changed in place) by subscribing and unsubscribing.</summary>
        public Subscription<T>[] Handlers { get; set; } = [];

        public override int HandlerCount => Handlers.Length;

        public void Enqueue(in T e)
        {
            if (_count == _pending.Length)
            {
                var grown = new T[_pending.Length * 2];
                for (var i = 0; i < _count; i++)
                {
                    grown[i] = _pending[(_head + i) % _pending.Length];
                }

                _pending = grown;
                _head = 0;
            }

            _pending[(_head + _count) % _pending.Length] = e;
            _count++;
        }

        public override void DispatchNext()
        {
            var e = _pending[_head];
            _pending[_head] = default;
            _head = (_head + 1) % _pending.Length;
            _count--;
            bus.Dispatch(this, in e);
        }

        public void Remove(Subscription<T> subscription)
        {
            var handlers = Handlers;
            var index = Array.IndexOf(handlers, subscription);
            if (index < 0)
            {
                return;
            }

            var remaining = new Subscription<T>[handlers.Length - 1];
            Array.Copy(handlers, 0, remaining, 0, index);
            Array.Copy(handlers, index + 1, remaining, index, handlers.Length - index - 1);
            Handlers = remaining;
        }
    }

    /// <summary>What every subscription has: whether it is active and how often its handler failed.</summary>
    private abstract class SubscriptionBase(EventBus bus) : IDisposable
    {
        public bool Active { get; private set; } = true;

        public int Failures { get; set; }

        protected EventBus Bus => bus;

        public void Dispose()
        {
            if (!Active)
            {
                return;
            }

            bus._thread?.Check("IEventBus subscription Dispose");
            Active = false;
            Detach();
        }

        protected abstract void Detach();
    }

    private sealed class Subscription<T>(EventBus bus, Channel<T> channel, BusHandler<T> handler) : SubscriptionBase(bus)
        where T : struct, IGameEvent
    {
        public BusHandler<T> Handler => handler;

        protected override void Detach() => channel.Remove(this);
    }

    private sealed class RawSubscription(EventBus bus, RawMessageHandler handler) : SubscriptionBase(bus)
    {
        public RawMessageHandler Handler => handler;

        protected override void Detach()
        {
            var raw = Bus._raw;
            var index = Array.IndexOf(raw, this);
            if (index >= 0)
            {
                Bus._raw = [.. raw.AsSpan(0, index), .. raw.AsSpan(index + 1)];
            }
        }
    }
}

/// <summary>
/// A small index per event type, assigned on first use, so the bus finds a type's channel with an array lookup. Shared by
/// every bus in the process; the values are dense, starting at 0.
/// </summary>
/// <typeparam name="T">The event type.</typeparam>
internal static class EventTypeId<T>
    where T : struct, IGameEvent
{
    /// <summary>This type's index.</summary>
    public static readonly int Value = EventTypeIds.Next();
}

/// <summary>The counter behind <see cref="EventTypeId{T}"/>.</summary>
internal static class EventTypeIds
{
    private static int _next = -1;

    /// <summary>The next unused index.</summary>
    public static int Next() => Interlocked.Increment(ref _next);
}
