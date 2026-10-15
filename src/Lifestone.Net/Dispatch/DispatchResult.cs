namespace Lifestone.Net.Dispatch;

/// <summary>What <see cref="InboundDispatcher.Dispatch"/> did with one message.</summary>
/// <param name="Outcome">Decoded, unknown or malformed.</param>
/// <param name="Opcode">The message's opcode (0 when it was shorter than one).</param>
/// <param name="EventType">For a GameEvent envelope (F7B0) whose header was read, the event type; else 0.</param>
public readonly record struct DispatchResult(DispatchOutcome Outcome, uint Opcode, uint EventType = 0);

/// <summary>The outcome of dispatching one message.</summary>
public enum DispatchOutcome : byte
{
    /// <summary>Decoded and passed to its typed callback.</summary>
    Decoded,

    /// <summary>Not decoded: passed to <see cref="IInboundMessageHandler.OnUnknown"/> (or, for an undecoded game event,
    /// <see cref="IInboundMessageHandler.OnGameEvent"/>).</summary>
    Unknown,

    /// <summary>Could not be parsed: passed to <see cref="IInboundMessageHandler.OnMalformed"/>.</summary>
    Malformed,
}
