using System.Globalization;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Recording;

/// <summary>The kinds of frame in a <c>.lsrec</c> recording.</summary>
public enum RecordingFrameKind : byte
{
    /// <summary>Not a frame (a default value).</summary>
    None = 0,

    /// <summary>One game message, in either direction (the raw tap).</summary>
    Message = 1,

    /// <summary>One event of the network session itself (phase changes, the data check, retries, failures).</summary>
    SessionEvent = 2,

    /// <summary>A local notice: a change the session made without a server message (for replay).</summary>
    Notice = 3,

    /// <summary>The trailer: frame count, stream hash and the final state digest. Always the last frame.</summary>
    Trailer = 0xFF,
}

/// <summary>
/// The <c>.lsrec</c> recording format, version 1 (docs/session.md §14): what a session recorder writes and the reader
/// reads. Everything is little-endian.
/// </summary>
/// <remarks>
/// <code>
/// file    = magic(8: "LSREC" 0D 0A 1A) version(u16 = 1) flags(u16 = 0) headerLength(u32) header(UTF-8 JSON) frame* trailer?
/// frame   = kind(u8) flags(u8) reserved(u16 = 0) payloadLength(u32) time(f64, session-clock seconds) payload
/// Message      payload = direction(u8) reserved(u8) queue(u16) fragmentSequence(u32) opcode(u32) length(u32) body
///                        (flags bit 0: the body is omitted; then no body bytes follow and length is the original's)
/// SessionEvent payload = kind(u8) phase(u8) previousPhase(u8) warning(u8) code(u32) attempt(i32) delayTicks(i64)
///                        serverTime(f64) timestamp(i64) hasFailure(u8) failureKind(u8) failurePhase(u8) reserved(u8)
///                        failureCode(u32) text failureMessage
/// Notice       payload = code(u16) reserved(u16) value(u32) text
/// Trailer      payload = frameCount(u64) streamHash(32: SHA-256 of every byte before the trailer) hasStateDigest(u8)
///                        stateDigest(32; zero when absent)
/// text    = length(u16; 0xFFFF for null) UTF-8 bytes
/// </code>
/// <para><b>No secret in a recording.</b> The raw tap never sees the LoginRequest (a packet-level section, not a
/// message). As defence in depth, a client-to-server message keeps its body only when its opcode is on
/// <see cref="KeepsClientBody"/>'s allowlist; any other is recorded as opcode and length only. The header holds no
/// account, user, machine or path (<see cref="RecordingHeader"/>).</para>
/// <para><b>Truncation.</b> A recording cut short after its header (a crash, a full disk) reads to its last whole frame:
/// the reader reports it as truncated and never throws for it. A file cut inside its 16-byte start or its header is not a
/// recording, and the reader refuses it with <see cref="InvalidDataException"/>; the recorder writes and flushes the start
/// and the header before anything else, so only a failure in that first write (or a host that opened the file and never
/// started a session) leaves one.</para>
/// </remarks>
public static class RecordingFormat
{
    /// <summary>The format version this code writes and reads.</summary>
    public const ushort Version = 1;

    /// <summary>The file extension.</summary>
    public const string FileExtension = ".lsrec";

    /// <summary>The bytes of a frame's header.</summary>
    public const int FrameHeaderSize = 16;

    /// <summary>The bytes of a message payload before its body.</summary>
    public const int MessageHeaderSize = 16;

    /// <summary>The bytes of the trailer's payload.</summary>
    public const int TrailerPayloadSize = 8 + DigestSize + 1 + DigestSize;

    /// <summary>The bytes of a SHA-256 digest (the stream hash and the state digest).</summary>
    public const int DigestSize = 32;

    /// <summary>The largest header accepted.</summary>
    public const int MaxHeaderBytes = 64 * 1024;

    /// <summary>The largest payload accepted (a message body of up to 16 MiB).</summary>
    public const int MaxPayloadBytes = (16 * 1024 * 1024) + MessageHeaderSize;

    /// <summary>The longest text field written, in UTF-8 bytes (longer text is cut at a character boundary).</summary>
    public const int MaxTextBytes = 4 * 1024;

    /// <summary>The text length that stands for null.</summary>
    public const ushort NullText = 0xFFFF;

    /// <summary>The message frame flag: the body was not recorded.</summary>
    public const byte BodyOmittedFlag = 0x01;

    /// <summary>The bytes of the file's fixed start: magic, version, flags, header length.</summary>
    public const int FileHeaderSize = 16;

    /// <summary>The magic at the start of every recording: <c>LSREC</c>, CR, LF, Ctrl-Z (catches text-mode mangling).</summary>
    public static ReadOnlySpan<byte> Magic => "LSREC\r\n\u001A"u8;

    /// <summary>
    /// True when a client-to-server message with <paramref name="opcode"/> keeps its body in a recording: the data
    /// check answer (F7E6), character create, delete and restore (F656, F655, F7D9), entering the world (F7C8, F657),
    /// logoff (F653) and game actions (F7B1). Everything else the client sends is recorded without its body.
    /// </summary>
    public static bool KeepsClientBody(uint opcode) => opcode switch
    {
        MessageOpcodes.DddInterrogationResponse or MessageOpcodes.CharacterCreate or MessageOpcodes.CharacterDelete
            or MessageOpcodes.CharacterRestore or MessageOpcodes.CharacterEnterWorldRequest or MessageOpcodes.CharacterEnterWorld
            or MessageOpcodes.CharacterLogOff or MessageOpcodes.GameAction => true,
        _ => false,
    };

    /// <summary>
    /// A recording's file name: <c>&lt;UTC&gt;-&lt;runner&gt;.lsrec</c>, for example
    /// <c>20261012T142233Z-headless.lsrec</c>. Hosts put it under <see cref="SessionOptions.DefaultRecordDirectory"/>
    /// (or <c>--record-session=DIR</c>), checked with <c>OutputPaths.TryResolve</c>.
    /// </summary>
    /// <param name="createdUtc">When the recording started.</param>
    /// <param name="runner">Who records (<see cref="RecordingHeader.Runner"/>'s rule).</param>
    public static string FileName(DateTimeOffset createdUtc, string runner)
    {
        RecordingHeader.RequireToken(runner, nameof(runner));
        return createdUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + runner + FileExtension;
    }
}
