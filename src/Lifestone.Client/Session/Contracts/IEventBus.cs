using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Marks a typed session event: a <c>readonly record struct</c> the reducer publishes on <see cref="IEventBus"/> after it
/// has updated <see cref="GameState"/>. Events are structs so publishing allocates nothing; a reference they carry (a
/// string, an immutable net record) already existed.
/// </summary>
public interface IGameEvent
{
}

/// <summary>A typed event handler. The event is passed by reference and is valid only during the call (copy it to keep it).</summary>
/// <typeparam name="T">The event type.</typeparam>
/// <param name="e">The event.</param>
public delegate void BusHandler<T>(in T e)
    where T : struct, IGameEvent;

/// <summary>
/// A raw-tap handler: one reassembled game message, in either direction, as bytes (A0's raw tap; the recorder and the
/// golden-fixture capture use it). LoginRequest and ConnectResponse are packet-level sections, not messages, so the
/// password never reaches a raw handler.
/// </summary>
/// <param name="direction">Inbound (server to client) or outbound.</param>
/// <param name="time">When, in <see cref="SessionClock"/> seconds.</param>
/// <param name="fragmentSequence">The message's fragment sequence (outbound: the one the transport assigned).</param>
/// <param name="queue">The fragment queue (message group).</param>
/// <param name="body">The message, opcode included; valid only during the call.</param>
public delegate void RawMessageHandler(MessageDirection direction, double time, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body);

/// <summary>
/// The session's event bus: typed events from the reducer, plus the raw message tap. Built by step ST3; publishing is
/// internal to the session.
/// </summary>
/// <remarks>
/// <para><b>Dispatch</b> is synchronous, on the game thread, inside <c>GameSession.Tick</c>, in subscription order, and
/// allocates nothing. <see cref="GameState"/> already reflects the whole message when a handler runs.</para>
/// <para><b>Re-entrancy.</b> An event published while a handler runs (by an action the handler issued, for example) is
/// queued and dispatched after the current dispatch, first in, first out. Unsubscribing during a dispatch is safe: the
/// handler is not called again, even later in the same dispatch.</para>
/// <para><b>Failures.</b> A handler that throws is logged as an ERROR (with the handler's method name) and counted;
/// the other handlers still run, and the exception does not reach the reducer.</para>
/// </remarks>
public interface IEventBus
{
    /// <summary>Subscribes <paramref name="handler"/> to events of type <typeparamref name="T"/>; dispose the result to unsubscribe.</summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>The subscription.</returns>
    IDisposable Subscribe<T>(BusHandler<T> handler)
        where T : struct, IGameEvent;

    /// <summary>
    /// Subscribes to the raw message tap; dispose the result to unsubscribe. With no raw subscriber the session's tap
    /// costs nothing.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <returns>The subscription.</returns>
    IDisposable SubscribeRaw(RawMessageHandler handler);

    /// <summary>How many events of type <typeparamref name="T"/> were published so far (the inspector's counters).</summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <returns>The count since the session was created.</returns>
    long Count<T>()
        where T : struct, IGameEvent;
}
