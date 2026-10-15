using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// A local notice in a recording: a change the session made without a server message (an action's local effect, a chat
/// line the client added), so a replay can reapply it. The codes are assigned by the session steps that make such
/// changes (the reducer and the Action API, ST7 to ST10); a replay that meets a code it does not know fails loudly.
/// </summary>
/// <param name="Code">What happened.</param>
/// <param name="Value">A number that goes with it (a guid, a count), or 0.</param>
/// <param name="Text">Text that goes with it (never a secret), or null.</param>
public readonly record struct RecordingNotice(ushort Code, uint Value, string? Text);

/// <summary>One game message as recorded (<see cref="RecordingFrameKind.Message"/>).</summary>
/// <param name="Direction">Inbound (server to client) or outbound.</param>
/// <param name="FragmentSequence">The message's fragment sequence.</param>
/// <param name="Queue">Its queue.</param>
/// <param name="Opcode">Its opcode (the body's first four bytes; 0 for a body shorter than four bytes).</param>
/// <param name="Length">The message's length in bytes, recorded or not.</param>
/// <param name="BodyOmitted">True when the body was not recorded (a client message off the allowlist).</param>
/// <param name="Body">The body, opcode included, or empty when omitted. Read from a reused buffer: valid until the next
/// frame is read; copy it to keep it.</param>
public readonly record struct RecordedMessage(
    MessageDirection Direction,
    uint FragmentSequence,
    MessageQueue Queue,
    uint Opcode,
    int Length,
    bool BodyOmitted,
    ReadOnlyMemory<byte> Body);

/// <summary>One frame read from a recording: <see cref="Kind"/> says which of the other members holds it.</summary>
/// <param name="Kind">The frame's kind.</param>
/// <param name="Time">When, in session-clock seconds.</param>
/// <param name="Message">For <see cref="RecordingFrameKind.Message"/>.</param>
/// <param name="SessionEvent">For <see cref="RecordingFrameKind.SessionEvent"/> (its <c>Timestamp</c> is the recorded one,
/// on the recording session's provider).</param>
/// <param name="Notice">For <see cref="RecordingFrameKind.Notice"/>.</param>
public readonly record struct RecordingFrame(
    RecordingFrameKind Kind,
    double Time,
    RecordedMessage Message,
    SessionEvent SessionEvent,
    RecordingNotice Notice);

/// <summary>A recording's trailer, as read and checked.</summary>
/// <param name="FrameCount">The frames before the trailer, as the recorder counted them.</param>
/// <param name="FrameCountMatches">True when the reader read exactly that many.</param>
/// <param name="StreamHashMatches">True when the SHA-256 of every byte before the trailer is the recorded one.</param>
/// <param name="StateDigest">The session's final state digest (ST14's <c>GameStateDigest</c>), or null when the recorder
/// had none.</param>
public sealed record RecordingTrailer(long FrameCount, bool FrameCountMatches, bool StreamHashMatches, byte[]? StateDigest)
{
    /// <summary>True when the trailer agrees with what was read.</summary>
    public bool IsIntact => FrameCountMatches && StreamHashMatches;
}

/// <summary>How a recording ended, once the reader reached its end.</summary>
public enum RecordingEnd
{
    /// <summary>The end has not been reached.</summary>
    NotReached,

    /// <summary>A trailer closed it (see <see cref="SessionRecordingReader.Trailer"/>).</summary>
    Trailer,

    /// <summary>It ended cleanly after a whole frame, without a trailer (the recorder never finished).</summary>
    NoTrailer,

    /// <summary>It ended inside a frame (cut short): every whole frame before the cut was read.</summary>
    Truncated,
}
