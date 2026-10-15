using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// Reads a <c>.lsrec</c> recording (<see cref="RecordingFormat"/>) from a stream, frame by frame: the replayer (ST14),
/// the recording tests and tools use it. A recording cut short reads to its last whole frame and reports
/// <see cref="RecordingEnd.Truncated"/>; it is never an exception. Anything that is not a version-1 recording, or a
/// frame that cannot be one, throws <see cref="InvalidDataException"/>.
/// </summary>
/// <remarks>
/// Message bodies come from one reused buffer: a frame's <see cref="RecordedMessage.Body"/> is valid until the next
/// <see cref="TryRead"/>. The reader checks the trailer as it reads: the frame count and the SHA-256 of every byte before
/// it.
/// </remarks>
public sealed class SessionRecordingReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _frameHeader = new byte[RecordingFormat.FrameHeaderSize];
    private byte[] _payload = new byte[4096];
    private bool _disposed;

    private SessionRecordingReader(Stream stream, bool leaveOpen, RecordingHeader header)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        Header = header;
    }

    /// <summary>The recording's header.</summary>
    public RecordingHeader Header { get; }

    /// <summary>Frames read so far (the trailer not counted).</summary>
    public long FramesRead { get; private set; }

    /// <summary>How the recording ended (<see cref="RecordingEnd.NotReached"/> until <see cref="TryRead"/> returns false).</summary>
    public RecordingEnd End { get; private set; }

    /// <summary>The trailer, once read (<see cref="RecordingEnd.Trailer"/>), else null.</summary>
    public RecordingTrailer? Trailer { get; private set; }

    /// <summary>Opens a recording: reads and checks its magic, version and header.</summary>
    /// <param name="stream">The recording, positioned at its start.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when the reader is disposed.</param>
    /// <exception cref="InvalidDataException">Not a version-1 recording, or its header is malformed or cut short.</exception>
    public static SessionRecordingReader Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var start = new byte[RecordingFormat.FileHeaderSize];
        if (stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) < start.Length || !start.AsSpan(0, 8).SequenceEqual(RecordingFormat.Magic))
        {
            throw new InvalidDataException("not a Lifestone session recording (.lsrec): the magic is missing");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(start.AsSpan(8));
        if (version != RecordingFormat.Version)
        {
            throw new InvalidDataException($"lsrec version {version} is not supported (this Lifestone reads version {RecordingFormat.Version})");
        }

        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(12));
        if (headerLength is 0 or > RecordingFormat.MaxHeaderBytes)
        {
            throw new InvalidDataException($"the recording header length {headerLength} is out of range");
        }

        var headerBytes = new byte[headerLength];
        if (stream.ReadAtLeast(headerBytes, headerBytes.Length, throwOnEndOfStream: false) < headerBytes.Length)
        {
            throw new InvalidDataException("the recording ends inside its header");
        }

        var reader = new SessionRecordingReader(stream, leaveOpen, RecordingHeader.FromJson(headerBytes));
        reader._hash.AppendData(start);
        reader._hash.AppendData(headerBytes);
        return reader;
    }

    /// <summary>
    /// Reads the next frame. False at the end: then <see cref="End"/> says how it ended, and <see cref="Trailer"/> holds
    /// the trailer if there was one.
    /// </summary>
    /// <exception cref="InvalidDataException">A frame is malformed (not a truncation), or data follows the trailer.</exception>
    public bool TryRead(out RecordingFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = default;
        if (End != RecordingEnd.NotReached)
        {
            return false;
        }

        var got = _stream.ReadAtLeast(_frameHeader, _frameHeader.Length, throwOnEndOfStream: false);
        if (got < _frameHeader.Length)
        {
            End = got == 0 ? RecordingEnd.NoTrailer : RecordingEnd.Truncated;
            return false;
        }

        var kind = (RecordingFrameKind)_frameHeader[0];
        var flags = _frameHeader[1];
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(_frameHeader.AsSpan(4));
        var time = BinaryPrimitives.ReadDoubleLittleEndian(_frameHeader.AsSpan(8));
        if (payloadLength > RecordingFormat.MaxPayloadBytes)
        {
            throw Malformed($"a payload of {payloadLength} bytes");
        }

        if (_payload.Length < payloadLength)
        {
            _payload = new byte[Math.Max((int)payloadLength, _payload.Length * 2)];
        }

        var payload = _payload.AsMemory(0, (int)payloadLength);
        if (_stream.ReadAtLeast(payload.Span, payload.Length, throwOnEndOfStream: false) < payload.Length)
        {
            End = RecordingEnd.Truncated;
            return false;
        }

        if (kind == RecordingFrameKind.Trailer)
        {
            ReadTrailer(payload.Span);
            return false;
        }

        _hash.AppendData(_frameHeader);
        _hash.AppendData(payload.Span);
        frame = kind switch
        {
            RecordingFrameKind.Message => new RecordingFrame(kind, time, ReadMessage(payload, flags), default, default),
            RecordingFrameKind.SessionEvent => new RecordingFrame(kind, time, default, ReadSessionEvent(payload.Span), default),
            RecordingFrameKind.Notice => new RecordingFrame(kind, time, default, default, ReadNotice(payload.Span)),
            _ => throw Malformed($"an unknown frame kind {(byte)kind}"),
        };
        FramesRead++;
        return true;
    }

    /// <summary>Every remaining frame, bodies copied (for tests and tools; the replayer uses <see cref="TryRead"/>).</summary>
    public IEnumerable<RecordingFrame> ReadAll()
    {
        while (TryRead(out var frame))
        {
            yield return frame.Kind == RecordingFrameKind.Message
                ? frame with { Message = frame.Message with { Body = frame.Message.Body.ToArray() } }
                : frame;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hash.Dispose();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private RecordedMessage ReadMessage(ReadOnlyMemory<byte> payload, byte flags)
    {
        var span = payload.Span;
        if (span.Length < RecordingFormat.MessageHeaderSize)
        {
            throw Malformed("a message frame shorter than its header");
        }

        var direction = (MessageDirection)span[0];
        var queue = (MessageQueue)BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var opcode = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        var omitted = (flags & RecordingFormat.BodyOmittedFlag) != 0;
        var body = payload[RecordingFormat.MessageHeaderSize..];
        if (direction is not (MessageDirection.Inbound or MessageDirection.Outbound)
            || length > int.MaxValue
            || (omitted ? !body.IsEmpty : body.Length != length))
        {
            throw Malformed("a message frame whose lengths disagree");
        }

        return new RecordedMessage(direction, sequence, queue, opcode, (int)length, omitted, body);
    }

    private static SessionEvent ReadSessionEvent(ReadOnlySpan<byte> span)
    {
        const int Fixed = 44;
        if (span.Length < Fixed)
        {
            throw Malformed("a session-event frame shorter than its fields");
        }

        var kind = (SessionEventKind)span[0];
        var phase = (SessionPhase)span[1];
        var previous = (SessionPhase)span[2];
        var warning = (SessionWarning)span[3];
        var code = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var attempt = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        var delay = TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(span[12..]));
        var serverTime = BinaryPrimitives.ReadDoubleLittleEndian(span[20..]);
        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(span[28..]);
        var hasFailure = span[36] != 0;
        var failureKind = (SessionFailureKind)span[37];
        var failurePhase = (SessionPhase)span[38];
        var failureCode = BinaryPrimitives.ReadUInt32LittleEndian(span[40..]);
        var rest = span[Fixed..];
        var text = ReadText(ref rest);
        var failureMessage = ReadText(ref rest);
        if (!rest.IsEmpty)
        {
            throw Malformed("a session-event frame with bytes left over");
        }

        var failure = hasFailure ? new SessionFailure(failureKind, failurePhase, failureMessage ?? string.Empty, failureCode) : null;
        return new SessionEvent(kind, timestamp, phase, previous, code, attempt, delay, serverTime, text, failure, warning);
    }

    private static RecordingNotice ReadNotice(ReadOnlySpan<byte> span)
    {
        if (span.Length < 8)
        {
            throw Malformed("a notice frame shorter than its fields");
        }

        var code = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var rest = span[8..];
        var text = ReadText(ref rest);
        if (!rest.IsEmpty)
        {
            throw Malformed("a notice frame with bytes left over");
        }

        return new RecordingNotice(code, value, text);
    }

    private void ReadTrailer(ReadOnlySpan<byte> span)
    {
        if (span.Length != RecordingFormat.TrailerPayloadSize)
        {
            throw Malformed("a trailer of the wrong size");
        }

        Span<byte> actual = stackalloc byte[RecordingFormat.DigestSize];
        _hash.GetCurrentHash(actual);
        var count = BinaryPrimitives.ReadInt64LittleEndian(span);
        var hashMatches = span.Slice(8, RecordingFormat.DigestSize).SequenceEqual(actual);
        var digest = span[(8 + RecordingFormat.DigestSize)] != 0 ? span[(8 + RecordingFormat.DigestSize + 1)..].ToArray() : null;
        Trailer = new RecordingTrailer(count, count == FramesRead, hashMatches, digest);
        End = RecordingEnd.Trailer;

        Span<byte> probe = stackalloc byte[1];
        if (_stream.Read(probe) != 0)
        {
            throw Malformed("data after the trailer");
        }
    }

    private static string? ReadText(ref ReadOnlySpan<byte> span)
    {
        if (span.Length < 2)
        {
            throw Malformed("a text field cut short");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(span);
        span = span[2..];
        if (length == RecordingFormat.NullText)
        {
            return null;
        }

        if (span.Length < length)
        {
            throw Malformed("a text field longer than its frame");
        }

        var text = Encoding.UTF8.GetString(span[..length]);
        span = span[length..];
        return text;
    }

    private static InvalidDataException Malformed(string what) => new($"the recording is malformed: {what}");
}
