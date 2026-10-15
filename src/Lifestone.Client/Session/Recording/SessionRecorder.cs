using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Unicode;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// Writes a session to a <c>.lsrec</c> recording (<see cref="RecordingFormat"/>; owner decision D48): every game message
/// the raw tap sees (<see cref="Message"/> has the raw handler's shape, so it subscribes directly), the network session's
/// own events and the session's local notices, then a trailer with the final state digest.
/// </summary>
/// <remarks>
/// <para><b>Buffering.</b> The file's start and the header are written to the stream and flushed at once, by the
/// constructor, so a session that dies at any point leaves a readable (truncated) recording, never a file the reader
/// refuses. Frames go into one 64 KiB buffer, written to the host's stream when it is full and flushed by
/// <see cref="FlushIfDue"/> (the session calls it at the end of every tick) at most once a second on the session clock,
/// and by <see cref="Complete"/> or <see cref="Dispose"/>. Writing a frame allocates nothing.</para>
/// <para><b>Allowlist.</b> A client-to-server message keeps its body only when <see cref="RecordingFormat.KeepsClientBody"/>
/// allows its opcode; otherwise its opcode and length are recorded and the body is not. Server messages are recorded
/// whole.</para>
/// <para><b>Failures.</b> A stream that fails (a full disk, a closed file) does not stop the session: the first failure is
/// logged as an ERROR, the recorder stops writing (<see cref="IsFaulted"/>) and counts the frames it drops.</para>
/// <para>The host owns the stream: the recorder never closes it. Game thread only.</para>
/// </remarks>
internal sealed class SessionRecorder : IDisposable
{
    /// <summary>The buffer between the frames and the stream.</summary>
    internal const int BufferSize = 64 * 1024;

    /// <summary>The least time between two timed flushes, in session-clock seconds.</summary>
    internal const double FlushIntervalSeconds = 1.0;

    private readonly Stream _stream;
    private readonly SessionClock _clock;
    private readonly ILogSink _log;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _used;
    private double _lastFlush;

    /// <summary>
    /// Starts a recording on <paramref name="stream"/>: the magic, the version and <paramref name="header"/> (validated:
    /// no free text) are written to the stream and flushed at once (one write, one flush; a failing stream faults the
    /// recorder as a later failure would).
    /// </summary>
    /// <param name="stream">Where to write (the host opens and disposes it).</param>
    /// <param name="header">What produced the recording.</param>
    /// <param name="clock">The session clock, for the flush cadence.</param>
    /// <param name="log">Where a write failure is logged.</param>
    /// <exception cref="ArgumentException">The header has a field of the wrong shape.</exception>
    internal SessionRecorder(Stream stream, RecordingHeader header, SessionClock clock, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("the recording stream is not writable", nameof(stream));
        }

        _stream = stream;
        _clock = clock;
        _log = log;
        _lastFlush = clock.Now;

        var json = header.ToJson();
        var start = new byte[RecordingFormat.FileHeaderSize + json.Length];
        RecordingFormat.Magic.CopyTo(start);
        BinaryPrimitives.WriteUInt16LittleEndian(start.AsSpan(8), RecordingFormat.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(start.AsSpan(10), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(start.AsSpan(12), (uint)json.Length);
        json.CopyTo(start, RecordingFormat.FileHeaderSize);
        _hash.AppendData(start);
        WriteToStream(start);
        Flush();
    }

    /// <summary>Frames written (the trailer not counted).</summary>
    internal long Frames { get; private set; }

    /// <summary>Frames dropped because the stream had failed.</summary>
    internal long DroppedFrames { get; private set; }

    /// <summary>Client messages recorded without their body (off the allowlist).</summary>
    internal long OmittedBodies { get; private set; }

    /// <summary>Bytes handed to the stream so far.</summary>
    internal long BytesWritten { get; private set; }

    /// <summary>Stream flushes so far (the header's, timed, final).</summary>
    internal int Flushes { get; private set; }

    /// <summary>True once the stream failed; nothing more is written.</summary>
    internal bool IsFaulted { get; private set; }

    /// <summary>True once the trailer is written; nothing more is recorded.</summary>
    internal bool IsCompleted { get; private set; }

    /// <summary>
    /// Records one game message (the raw tap's shape: subscribe it with <c>IEventBus.SubscribeRaw</c>). A client message
    /// off the allowlist is recorded without its body.
    /// </summary>
    internal void Message(MessageDirection direction, double time, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body)
    {
        if (!CanRecord())
        {
            return;
        }

        var opcode = body.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0u;
        var omit = direction == MessageDirection.Outbound && !RecordingFormat.KeepsClientBody(opcode);
        var stored = omit ? ReadOnlySpan<byte>.Empty : body;
        if (omit)
        {
            OmittedBodies++;
        }

        Span<byte> head = stackalloc byte[RecordingFormat.FrameHeaderSize + RecordingFormat.MessageHeaderSize];
        WriteFrameHeader(head, RecordingFrameKind.Message, omit ? RecordingFormat.BodyOmittedFlag : (byte)0, RecordingFormat.MessageHeaderSize + stored.Length, time);
        var message = head[RecordingFormat.FrameHeaderSize..];
        message[0] = (byte)direction;
        message[1] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(message[2..], (ushort)queue);
        BinaryPrimitives.WriteUInt32LittleEndian(message[4..], fragmentSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(message[8..], opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(message[12..], (uint)body.Length);
        Append(head);
        Append(stored);
        Frames++;
    }

    /// <summary>Records one event of the network session.</summary>
    internal void SessionEvent(double time, in SessionEvent e)
    {
        if (!CanRecord())
        {
            return;
        }

        Span<byte> payload = stackalloc byte[44 + 2 + RecordingFormat.MaxTextBytes + 2 + RecordingFormat.MaxTextBytes];
        payload[0] = (byte)e.Kind;
        payload[1] = (byte)e.Phase;
        payload[2] = (byte)e.PreviousPhase;
        payload[3] = (byte)e.Warning;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], e.Code);
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], e.Attempt);
        BinaryPrimitives.WriteInt64LittleEndian(payload[12..], e.Delay.Ticks);
        BinaryPrimitives.WriteDoubleLittleEndian(payload[20..], e.ServerTime);
        BinaryPrimitives.WriteInt64LittleEndian(payload[28..], e.Timestamp);
        payload[36] = e.Failure is null ? (byte)0 : (byte)1;
        payload[37] = (byte)(e.Failure?.Kind ?? 0);
        payload[38] = (byte)(e.Failure?.Phase ?? 0);
        payload[39] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[40..], e.Failure?.Code ?? 0);
        var length = 44;
        length += WriteText(payload[length..], e.Text);
        length += WriteText(payload[length..], e.Failure?.Message);
        WriteFrame(RecordingFrameKind.SessionEvent, time, payload[..length]);
    }

    /// <summary>Records one local notice.</summary>
    internal void Notice(double time, in RecordingNotice notice)
    {
        if (!CanRecord())
        {
            return;
        }

        Span<byte> payload = stackalloc byte[8 + 2 + RecordingFormat.MaxTextBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, notice.Code);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[2..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], notice.Value);
        var length = 8 + WriteText(payload[8..], notice.Text);
        WriteFrame(RecordingFrameKind.Notice, time, payload[..length]);
    }

    /// <summary>
    /// The end-of-tick flush: hands the buffer to the stream and flushes it when at least
    /// <see cref="FlushIntervalSeconds"/> passed on the session clock since the last flush and something is waiting.
    /// True when it flushed.
    /// </summary>
    internal bool FlushIfDue()
    {
        if (IsFaulted || _used == 0 || _clock.Now - _lastFlush < FlushIntervalSeconds)
        {
            return false;
        }

        Flush();
        return true;
    }

    /// <summary>
    /// Ends the recording: writes the trailer (frame count, stream hash and, when given, the 32-byte state digest) and
    /// flushes. Nothing is recorded after it. Safe to call once; later calls do nothing.
    /// </summary>
    /// <param name="stateDigest">The session's final state digest (32 bytes), or empty for none.</param>
    internal void Complete(ReadOnlySpan<byte> stateDigest = default)
    {
        if (IsCompleted)
        {
            return;
        }

        if (!stateDigest.IsEmpty && stateDigest.Length != RecordingFormat.DigestSize)
        {
            throw new ArgumentException($"the state digest must be {RecordingFormat.DigestSize} bytes", nameof(stateDigest));
        }

        IsCompleted = true;
        if (IsFaulted)
        {
            return;
        }

        Span<byte> trailer = stackalloc byte[RecordingFormat.FrameHeaderSize + RecordingFormat.TrailerPayloadSize];
        trailer.Clear();
        WriteFrameHeader(trailer, RecordingFrameKind.Trailer, 0, RecordingFormat.TrailerPayloadSize, _clock.Now);
        var payload = trailer[RecordingFormat.FrameHeaderSize..];
        BinaryPrimitives.WriteInt64LittleEndian(payload, Frames);
        _hash.GetCurrentHash(payload.Slice(8, RecordingFormat.DigestSize));
        if (!stateDigest.IsEmpty)
        {
            payload[8 + RecordingFormat.DigestSize] = 1;
            stateDigest.CopyTo(payload[(8 + RecordingFormat.DigestSize + 1)..]);
        }

        AppendUnhashed(trailer);
        Flush();
    }

    /// <summary>Completes the recording without a state digest if it is not complete yet. Never closes the stream.</summary>
    public void Dispose()
    {
        Complete();
        _hash.Dispose();
    }

    private bool CanRecord()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("the recording is complete; nothing more can be recorded");
        }

        if (IsFaulted)
        {
            DroppedFrames++;
            return false;
        }

        return true;
    }

    private void WriteFrame(RecordingFrameKind kind, double time, ReadOnlySpan<byte> payload)
    {
        Span<byte> head = stackalloc byte[RecordingFormat.FrameHeaderSize];
        WriteFrameHeader(head, kind, 0, payload.Length, time);
        Append(head);
        Append(payload);
        Frames++;
    }

    private static void WriteFrameHeader(Span<byte> destination, RecordingFrameKind kind, byte flags, int payloadLength, double time)
    {
        destination[0] = (byte)kind;
        destination[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)payloadLength);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[8..], time);
    }

    /// <summary>Writes a text field (null as <see cref="RecordingFormat.NullText"/>; cut at a character boundary past the limit).</summary>
    private static int WriteText(Span<byte> destination, string? text)
    {
        if (text is null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, RecordingFormat.NullText);
            return 2;
        }

        Utf8.FromUtf16(text, destination.Slice(2, RecordingFormat.MaxTextBytes), out _, out var written, replaceInvalidSequences: true, isFinalBlock: true);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)written);
        return 2 + written;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        _hash.AppendData(bytes);
        AppendUnhashed(bytes);
    }

    private void AppendUnhashed(ReadOnlySpan<byte> bytes)
    {
        if (IsFaulted)
        {
            return;
        }

        if (_used + bytes.Length > _buffer.Length)
        {
            WriteBuffer();
            if (bytes.Length > _buffer.Length)
            {
                WriteToStream(bytes);
                return;
            }
        }

        bytes.CopyTo(_buffer.AsSpan(_used));
        _used += bytes.Length;
    }

    private void Flush()
    {
        WriteBuffer();
        if (IsFaulted)
        {
            return;
        }

        try
        {
            _stream.Flush();
            Flushes++;
            _lastFlush = _clock.Now;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException or UnauthorizedAccessException)
        {
            Fault(e);
        }
    }

    private void WriteBuffer()
    {
        if (_used == 0)
        {
            return;
        }

        var used = _used;
        _used = 0;
        WriteToStream(_buffer.AsSpan(0, used));
    }

    private void WriteToStream(ReadOnlySpan<byte> bytes)
    {
        if (IsFaulted)
        {
            return;
        }

        try
        {
            _stream.Write(bytes);
            BytesWritten += bytes.Length;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException or UnauthorizedAccessException)
        {
            Fault(e);
        }
    }

    private void Fault(Exception e)
    {
        IsFaulted = true;
        _used = 0;
        _log.Error(
            SessionLog.Category,
            $"session recording stopped: the stream failed ({e.GetType().Name}: {e.Message}); the session goes on unrecorded",
            new LogField("frames", Frames));
    }
}
