using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The event bus with its raw tap (ST3): synchronous dispatch in subscription order, FIFO for events published from a
/// handler, safe unsubscription during a dispatch, loud but contained handler failures, the per-type counters, the
/// game-thread rule, and allocation-free publishing.
/// </summary>
public sealed class EventBusTests
{
    [Fact]
    public void Events_dispatch_in_subscription_order()
    {
        var bus = new EventBus(new RecordingLogSink());
        var order = new List<string>();
        bus.Subscribe((in ObjectDeleted e) => order.Add($"a:{e.Guid:X}"));
        bus.Subscribe((in ObjectDeleted e) => order.Add($"b:{e.Guid:X}"));
        bus.Subscribe((in UseDone e) => order.Add($"use:{e.WeenieError}"));

        bus.Publish(new ObjectDeleted(0x80000001));
        bus.Publish(new UseDone(0));
        bus.Publish(new ObjectDeleted(0x80000002));

        Assert.Equal(["a:80000001", "b:80000001", "use:0", "a:80000002", "b:80000002"], order);
        Assert.Equal(2, bus.Count<ObjectDeleted>());
        Assert.Equal(1, bus.Count<UseDone>());
        Assert.Equal(0, bus.Count<HealthUpdated>());
    }

    [Fact]
    public void A_publish_inside_a_handler_runs_after_the_current_dispatch_FIFO()
    {
        var bus = new EventBus(new RecordingLogSink());
        var order = new List<string>();
        bus.Subscribe((in ObjectDeleted e) =>
        {
            order.Add($"first deleted {e.Guid}");
            if (e.Guid == 1)
            {
                bus.Publish(new UseDone(10));
                bus.Publish(new ObjectDeleted(2));
                bus.Publish(new UseDone(11));
                Assert.Equal(3, bus.PendingCount);
            }
        });
        bus.Subscribe((in ObjectDeleted e) => order.Add($"second deleted {e.Guid}"));
        bus.Subscribe((in UseDone e) =>
        {
            order.Add($"use {e.WeenieError}");
            if (e.WeenieError == 10)
            {
                bus.Publish(new UseDone(12));
            }
        });

        bus.Publish(new ObjectDeleted(1));

        Assert.Equal(
            ["first deleted 1", "second deleted 1", "use 10", "first deleted 2", "second deleted 2", "use 11", "use 12"],
            order);
        Assert.Equal(0, bus.PendingCount);
        Assert.Equal(3, bus.Count<UseDone>());
    }

    [Fact]
    public void Unsubscribing_during_dispatch_is_safe()
    {
        var bus = new EventBus(new RecordingLogSink());
        var order = new List<string>();
        IDisposable? second = null;
        var first = bus.Subscribe((in UseDone e) =>
        {
            order.Add("first");
            second!.Dispose();
        });
        second = bus.Subscribe((in UseDone e) => order.Add("second"));
        bus.Subscribe((in UseDone e) =>
        {
            order.Add("third");
            bus.Subscribe((in UseDone e2) => order.Add("late"));
        });

        bus.Publish(new UseDone(0));
        Assert.Equal(["first", "third"], order);

        order.Clear();
        first.Dispose();
        first.Dispose();
        bus.Publish(new UseDone(0));
        Assert.Equal(["third", "late"], order);
    }

    [Fact]
    public void A_throwing_handler_is_logged_and_others_still_run()
    {
        var log = new RecordingLogSink();
        var bus = new EventBus(log);
        var ran = new List<string>();
        bus.Subscribe((in UseDone e) => ran.Add("before"));
        bus.Subscribe<UseDone>(Throwing);
        bus.Subscribe((in UseDone e) => ran.Add("after"));

        bus.Publish(new UseDone(1));
        bus.Publish(new UseDone(2));

        Assert.Equal(["before", "after", "before", "after"], ran);
        Assert.Equal(2, bus.HandlerFailures);
        var error = Assert.Single(log.Entries, e => e.Severity == LogSeverity.Error);
        Assert.Equal("session", error.Category);
        Assert.Contains("EventBusTests.Throwing", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseDone", error.Message, StringComparison.Ordinal);
        Assert.Contains("handler failure 1", error.Message, StringComparison.Ordinal);
        Assert.Single(log.Entries, e => e.Severity == LogSeverity.Debug);
    }

    [Fact]
    public void Tests_can_have_handler_exceptions_rethrown_and_the_bus_stays_usable()
    {
        var bus = new EventBus(new RecordingLogSink()) { RethrowHandlerExceptions = true };
        var ran = 0;
        bus.Subscribe<UseDone>(Throwing);
        bus.Subscribe((in ObjectDeleted e) => ran++);

        var e = Assert.Throws<InvalidOperationException>(() => bus.Publish(new UseDone(1)));
        Assert.Equal("handler failure 1", e.Message);
        bus.Publish(new ObjectDeleted(5));
        Assert.Equal(1, ran);
        Assert.Equal(0, bus.PendingCount);
    }

    [Fact]
    public void Raw_subscribers_see_messages_in_order_and_can_leave()
    {
        var bus = new EventBus(new RecordingLogSink());
        Assert.False(bus.HasRawSubscribers);
        var seen = new List<string>();
        var a = bus.SubscribeRaw((direction, time, sequence, queue, body) => seen.Add($"a {direction} {time} {sequence} {queue} {body.Length}"));
        using var b = bus.SubscribeRaw((direction, _, _, _, body) => seen.Add($"b {direction} {body[0]:X2}"));
        Assert.True(bus.HasRawSubscribers);

        bus.PublishRaw(MessageDirection.Inbound, 1.5, 7, MessageQueue.SmartBox, [0x48, 0xF7, 0, 0]);
        a.Dispose();
        bus.PublishRaw(MessageDirection.Outbound, 2, 8, MessageQueue.Ui, [0xB1]);

        Assert.Equal(["a Inbound 1.5 7 SmartBox 4", "b Inbound 48", "b Outbound B1"], seen);
    }

    [Fact]
    public void A_throwing_raw_handler_is_contained()
    {
        var log = new RecordingLogSink();
        var bus = new EventBus(log);
        var ran = 0;
        bus.SubscribeRaw((_, _, _, _, _) => throw new InvalidOperationException("raw failure"));
        bus.SubscribeRaw((_, _, _, _, _) => ran++);
        bus.PublishRaw(MessageDirection.Inbound, 0, 1, MessageQueue.Ui, [1]);
        Assert.Equal(1, ran);
        Assert.Equal(1, bus.HandlerFailures);
        Assert.Contains("raw message", Assert.Single(log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Subscribing_or_publishing_from_another_thread_throws_once_bound()
    {
        var thread = new GameThreadBinding();
        var bus = new EventBus(new RecordingLogSink(), thread);
        Exception? early = null;
        RunOnOtherThread(() => early = Record.Exception(() => bus.Subscribe((in UseDone e) => { })));
        Assert.Null(early);

        thread.Bind("GameSession.Tick");
        Exception? subscribe = null;
        Exception? raw = null;
        Exception? publish = null;
        RunOnOtherThread(() =>
        {
            subscribe = Record.Exception(() => bus.Subscribe((in UseDone e) => { }));
            raw = Record.Exception(() => bus.SubscribeRaw((_, _, _, _, _) => { }));
            publish = Record.Exception(() => bus.Publish(new UseDone(0)));
        });
        Assert.IsType<InvalidOperationException>(subscribe);
        Assert.IsType<InvalidOperationException>(raw);
        Assert.IsType<InvalidOperationException>(publish);
    }

    [Fact]
    public void The_subscription_set_disposes_newest_first_and_keeps_going()
    {
        var order = new List<string>();
        var set = new SubscriptionSet();
        set.Add(new Disposer(() => order.Add("first")));
        set.Add(new Disposer(() => throw new InvalidOperationException("second failed")));
        set.Add(new Disposer(() => order.Add("third")));
        Assert.Equal(3, set.Count);

        var e = Assert.Throws<InvalidOperationException>(set.Dispose);
        Assert.Equal("second failed", e.Message);
        Assert.Equal(["third", "first"], order);
        Assert.Equal(0, set.Count);
        set.Dispose();

        set.Add(new Disposer(() => order.Add("after")));
        Assert.Equal(["third", "first", "after"], order);
    }

    [Fact]
    public void A_subscription_set_releases_bus_subscriptions()
    {
        var bus = new EventBus(new RecordingLogSink());
        var count = 0;
        using (var set = new SubscriptionSet())
        {
            set.Add(bus.Subscribe((in UseDone e) => count++));
            set.Add(bus.SubscribeRaw((_, _, _, _, _) => count++));
            bus.Publish(new UseDone(0));
            bus.PublishRaw(MessageDirection.Inbound, 0, 0, MessageQueue.Ui, [0]);
        }

        bus.Publish(new UseDone(0));
        bus.PublishRaw(MessageDirection.Inbound, 0, 0, MessageQueue.Ui, [0]);
        Assert.Equal(2, count);
        Assert.False(bus.HasRawSubscribers);
    }

    [Fact]
    public void Type_ids_are_stable_and_distinct()
    {
        Assert.Equal(EventTypeId<UseDone>.Value, EventTypeId<UseDone>.Value);
        Assert.NotEqual(EventTypeId<UseDone>.Value, EventTypeId<ObjectDeleted>.Value);
    }

    private static void Throwing(in UseDone e) => throw new InvalidOperationException($"handler failure {e.WeenieError}");

    private static void RunOnOtherThread(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }

    private sealed class Disposer(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>
/// The bus's allocation budget (ST3): once a channel exists, publishing an event to handlers, queueing one from a handler
/// and tapping a raw message allocate nothing. Measured alone, outside the parallel test collections.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class EventBusAllocationTests
{
    [Fact]
    public void Publishing_allocates_nothing()
    {
        var bus = new EventBus(NullLogSink.Instance);
        long sum = 0;
        bus.Subscribe((in ObjectMoved e) => sum += e.Guid);
        bus.Subscribe((in ObjectMoved e) =>
        {
            if (e.Guid % 2 == 0)
            {
                bus.Publish(new UseDone(e.Guid));
            }
        });
        bus.Subscribe((in UseDone e) => sum += e.WeenieError);
        var moved = new ObjectMoved(2, default, PositionVerdict.Remote, false, true, null, null);
        AssertAllocatesNothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                bus.Publish(moved with { Guid = (uint)i });
            }
        });
        Assert.True(sum > 0);
        Assert.Equal(0, bus.PendingCount);
    }

    [Fact]
    public void Raw_tap_without_subscribers_costs_nothing()
    {
        var bus = new EventBus(NullLogSink.Instance);
        byte[] body = [0x48, 0xF7, 0, 0, 1, 2, 3];
        AssertAllocatesNothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                bus.PublishRaw(MessageDirection.Inbound, i, (uint)i, MessageQueue.SmartBox, body);
            }
        });
        Assert.False(bus.HasRawSubscribers);
    }

    [Fact]
    public void Raw_tap_with_a_subscriber_allocates_nothing()
    {
        var bus = new EventBus(NullLogSink.Instance);
        long total = 0;
        bus.SubscribeRaw((_, _, _, _, b) => total += b.Length);
        byte[] body = [0x48, 0xF7, 0, 0, 1, 2, 3];
        AssertAllocatesNothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                bus.PublishRaw(MessageDirection.Inbound, i, (uint)i, MessageQueue.SmartBox, body);
            }
        });
        Assert.True(total >= 7 * 1000);
    }

    /// <summary>
    /// Runs <paramref name="work"/> once to warm up, then measures it until a window without a GC inside is seen (at most
    /// five tries), and requires that window to have allocated nothing.
    /// </summary>
    private static void AssertAllocatesNothing(Action work)
    {
        work();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var window = AllocationWindow.Open();
            work();
            var (bytes, gcInside) = window.Close();
            if (!gcInside)
            {
                Assert.Equal(0, bytes);
                return;
            }
        }

        Assert.Fail("every measured window had a GC inside");
    }
}
