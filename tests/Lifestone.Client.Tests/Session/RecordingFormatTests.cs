using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Data.Dat;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The <c>.lsrec</c> recording (ST6): frames round-trip bit-exact, the header holds no account, user, machine or path,
/// client messages off the allowlist lose their bodies (so a LoginRequest could never be recorded), a recording cut
/// anywhere reads to its last whole frame, and the recorder flushes at most once a second on the session clock.
/// </summary>
public sealed class RecordingFormatTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 12, 14, 22, 33, TimeSpan.Zero);

    private static RecordingHeader Header => new()
    {
        Runner = "test",
        CreatedUtc = Created,
        LifestoneVersion = "0.2.0-m2",
        AceCommit = "95ef426d412459ff619a12cc18f82bcf72c1c2a8",
        Iterations = new DatIterations(2072, 982, 994, 497),
        Scenario = "login",
    };

    [Fact]
    public void Frames_round_trip_bit_exact()
    {
        var bytes = RecordSample(out var expected, out var digest);
        using var reader = SessionRecordingReader.Open(new MemoryStream(bytes));
        Assert.Equal(Header, reader.Header);

        var frames = reader.ReadAll().ToList();
        Assert.Equal(expected.Count, frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            AssertFrame(expected[i], frames[i]);
        }

        Assert.Equal(RecordingEnd.Trailer, reader.End);
        var trailer = Assert.IsType<RecordingTrailer>(reader.Trailer);
        Assert.True(trailer.IsIntact);
        Assert.Equal(expected.Count, trailer.FrameCount);
        Assert.Equal(digest, trailer.StateDigest);

        // The same session recorded again gives the same bytes.
        Assert.Equal(bytes, RecordSample(out _, out _));
    }

    [Fact]
    public void A_corrupted_byte_breaks_the_stream_hash()
    {
        var bytes = RecordSample(out _, out _);
        var index = IndexOf(bytes, Body(MessageOpcodes.DddInterrogation, 0x9A, 0x00, 0x01)) + 4; // inside the first message body
        Assert.Equal(0x9A, bytes[index]);
        bytes[index] ^= 0xFF;
        using var reader = SessionRecordingReader.Open(new MemoryStream(bytes));
        _ = reader.ReadAll().Count();
        Assert.False(reader.Trailer!.StreamHashMatches);
        Assert.False(reader.Trailer.IsIntact);
    }

    [Fact]
    public void Header_contains_no_forbidden_keys()
    {
        using var json = JsonDocument.Parse(Header.ToJson());
        string[] forbidden = ["account", "user", "username", "machine", "computer", "host", "path", "file", "dir", "directory", "password", "secret", "token", "key", "name"];
        var keys = new List<string>();
        Collect(json.RootElement, keys);
        Assert.Equal(["format", "formatVersion", "runner", "createdUtc", "lifestoneVersion", "clientVersion", "aceCommit", "iterations", "portal", "cell", "local", "highRes", "scenario"], keys);
        Assert.DoesNotContain(keys, k => forbidden.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(Header, RecordingHeader.FromJson(Header.ToJson()));

        // Free text cannot get in: every field has a fixed shape.
        Assert.Throws<ArgumentException>(() => (Header with { Runner = "D:\\captures\\run" }).ToJson());
        Assert.Throws<ArgumentException>(() => (Header with { Runner = "Headless" }).ToJson());
        Assert.Throws<ArgumentException>(() => (Header with { Scenario = "walk to the bank" }).ToJson());
        Assert.Throws<ArgumentException>(() => (Header with { LifestoneVersion = "1.0 built by someone" }).ToJson());
        Assert.Throws<ArgumentException>(() => (Header with { AceCommit = "../../secrets" }).ToJson());
        Assert.Throws<ArgumentException>(() => (Header with { ClientVersion = "1802x" }).ToJson());
        Assert.Equal("""{"format":"lsrec","formatVersion":1,"runner":"x","createdUtc":"2026-10-12T14:22:33.0000000Z","clientVersion":"1802"}""",
            Encoding.UTF8.GetString(new RecordingHeader { Runner = "x", CreatedUtc = Created }.ToJson()));
    }

    [Fact]
    public void Unlisted_C2S_opcodes_are_recorded_without_body()
    {
        uint[] kept =
        [
            MessageOpcodes.DddInterrogationResponse, MessageOpcodes.CharacterCreate, MessageOpcodes.CharacterDelete,
            MessageOpcodes.CharacterRestore, MessageOpcodes.CharacterEnterWorldRequest, MessageOpcodes.CharacterEnterWorld,
            MessageOpcodes.CharacterLogOff, MessageOpcodes.GameAction,
        ];
        Assert.All(kept, o => Assert.True(RecordingFormat.KeepsClientBody(o)));
        Assert.False(RecordingFormat.KeepsClientBody(MessageOpcodes.CharacterList));
        Assert.False(RecordingFormat.KeepsClientBody(0));

        // A LoginRequest is a packet section, never a message; were its bytes ever tapped, they would lose their body.
        var login = LoginRequestLike(SessionTestData.PlaySentinel);
        var stream = new MemoryStream();
        var time = new ManualTimeProvider();
        using (var recorder = new SessionRecorder(stream, Header, new SessionClock(time), NullLogSink.Instance))
        {
            recorder.Message(MessageDirection.Outbound, 0.5, 2, MessageQueue.Login, login);
            recorder.Message(MessageDirection.Outbound, 0.6, 3, MessageQueue.Ui, Body(MessageOpcodes.GameAction, 0x15, 0x00));
            recorder.Message(MessageDirection.Inbound, 0.7, 4, MessageQueue.Ui, Body(MessageOpcodes.CharacterList, 1, 2, 3));
            recorder.Message(MessageDirection.Outbound, 0.8, 5, MessageQueue.Ui, [0x01]);
            Assert.Equal(2, recorder.OmittedBodies);
        }

        var bytes = stream.ToArray();
        Assert.Equal(-1, IndexOf(bytes, Encoding.ASCII.GetBytes(SessionTestData.PlaySentinel)));
        Assert.Equal(-1, IndexOf(bytes, Encoding.Unicode.GetBytes(SessionTestData.PlaySentinel)));

        using var reader = SessionRecordingReader.Open(new MemoryStream(bytes));
        var frames = reader.ReadAll().Select(f => f.Message).ToList();
        Assert.True(frames[0].BodyOmitted);
        Assert.Equal(login.Length, frames[0].Length);
        Assert.True(frames[0].Body.IsEmpty);
        Assert.False(frames[1].BodyOmitted);
        Assert.Equal(MessageOpcodes.GameAction, frames[1].Opcode);
        Assert.False(frames[2].BodyOmitted);
        Assert.True(frames[3].BodyOmitted);
        Assert.Equal(0u, frames[3].Opcode);
        Assert.Equal(1, frames[3].Length);
    }

    [Fact]
    public void Truncated_file_reads_to_the_last_whole_frame()
    {
        var bytes = RecordSample(out var expected, out _, includeBigMessage: false);
        var headerEnd = RecordingFormat.FileHeaderSize + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        var frameEnds = FrameEnds(bytes, headerEnd);
        Assert.Equal(expected.Count + 1, frameEnds.Count); // the frames and the trailer

        for (var cut = headerEnd; cut < bytes.Length; cut++)
        {
            using var reader = SessionRecordingReader.Open(new MemoryStream(bytes, 0, cut));
            var read = reader.ReadAll().Count();
            var whole = frameEnds.Count(end => end <= cut);
            Assert.Equal(Math.Min(whole, expected.Count), read);
            Assert.Equal(frameEnds.Contains(cut) || cut == headerEnd ? RecordingEnd.NoTrailer : RecordingEnd.Truncated, reader.End);
            Assert.Null(reader.Trailer);
        }

        for (var cut = 0; cut < headerEnd; cut++)
        {
            Assert.Throws<InvalidDataException>(() => SessionRecordingReader.Open(new MemoryStream(bytes, 0, cut)));
        }
    }

    [Fact]
    public void A_recorder_disposed_without_a_digest_still_writes_a_trailer()
    {
        var stream = new MemoryStream();
        using (var recorder = new SessionRecorder(stream, Header, new SessionClock(new ManualTimeProvider()), NullLogSink.Instance))
        {
            recorder.Notice(1, new RecordingNotice(1, 2, null));
        }

        using var reader = SessionRecordingReader.Open(new MemoryStream(stream.ToArray()));
        Assert.Single(reader.ReadAll());
        Assert.True(reader.Trailer!.IsIntact);
        Assert.Null(reader.Trailer.StateDigest);
    }

    [Fact]
    public void Recorder_flushes_at_most_once_per_second_on_the_injected_clock()
    {
        var time = new ManualTimeProvider();
        var stream = new CountingStream();
        using var recorder = new SessionRecorder(stream, Header, new SessionClock(time), NullLogSink.Instance);
        Assert.Equal((1, 1), (stream.Writes, stream.Flushes)); // the start and the header, at once
        recorder.Message(MessageDirection.Inbound, 0, 1, MessageQueue.Ui, Body(MessageOpcodes.CharacterList, 1));
        Assert.Equal(1, stream.Writes);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(recorder.FlushIfDue());
        Assert.Equal(1, stream.Flushes);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(recorder.FlushIfDue());
        Assert.Equal((2, 2), (stream.Writes, stream.Flushes));
        Assert.False(recorder.FlushIfDue()); // nothing new

        recorder.Message(MessageDirection.Inbound, 1.2, 2, MessageQueue.Ui, Body(MessageOpcodes.CharacterList, 2));
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(recorder.FlushIfDue());
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(recorder.FlushIfDue());
        Assert.Equal((3, 3), (stream.Writes, stream.Flushes));
        Assert.Equal(3, recorder.Flushes);
    }

    [Fact]
    public void A_recording_whose_session_never_ran_still_reads()
    {
        // The recorder writes and flushes its start and header at once: a host that dies before the first timed flush
        // leaves a recording the reader opens (cut after its header), not a file it refuses.
        var stream = new MemoryStream();
        _ = new SessionRecorder(stream, Header, new SessionClock(new ManualTimeProvider()), NullLogSink.Instance);
        using var reader = SessionRecordingReader.Open(new MemoryStream(stream.ToArray()));
        Assert.Equal(Header.Runner, reader.Header.Runner);
        Assert.Empty(reader.ReadAll());
        Assert.Equal(RecordingEnd.NoTrailer, reader.End);
    }

    [Fact]
    public void A_full_buffer_goes_to_the_stream_without_a_flush()
    {
        var time = new ManualTimeProvider();
        var stream = new CountingStream();
        using var recorder = new SessionRecorder(stream, Header, new SessionClock(time), NullLogSink.Instance);
        var body = new byte[1000];
        BinaryPrimitives.WriteUInt32LittleEndian(body, MessageOpcodes.UpdatePosition);
        for (var i = 0; i < 70; i++)
        {
            recorder.Message(MessageDirection.Inbound, i * 0.001, (uint)i, MessageQueue.SmartBox, body);
        }

        Assert.Equal(2, stream.Writes); // the header's, then one full buffer
        Assert.Equal(1, stream.Flushes); // the header's only
        Assert.True(stream.Length <= SessionRecorder.BufferSize);
    }

    [Fact]
    public void A_failing_stream_stops_the_recording_loudly_without_stopping_the_session()
    {
        var log = new RecordingLogSink();
        var stream = new CountingStream { FailWrites = true };
        var recorder = new SessionRecorder(stream, Header, new SessionClock(new ManualTimeProvider()), log);
        var big = new byte[SessionRecorder.BufferSize + 10];
        recorder.Message(MessageDirection.Inbound, 0, 1, MessageQueue.SmartBox, big);
        recorder.Message(MessageDirection.Inbound, 0, 2, MessageQueue.SmartBox, big);
        recorder.Notice(0, new RecordingNotice(1, 0, "after the failure"));
        recorder.Dispose();

        Assert.True(recorder.IsFaulted);
        Assert.Equal(3, recorder.DroppedFrames); // the header's write failed already
        var error = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Error, error.Severity);
        Assert.Contains("session recording stopped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_can_be_recorded_after_completion()
    {
        using var recorder = new SessionRecorder(new MemoryStream(), Header, new SessionClock(new ManualTimeProvider()), NullLogSink.Instance);
        recorder.Complete();
        Assert.True(recorder.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => recorder.Notice(0, default));
        recorder.Complete();
        Assert.Throws<ArgumentException>(() => new SessionRecorder(new MemoryStream(), Header, new SessionClock(new ManualTimeProvider()), NullLogSink.Instance).Complete(new byte[5]));
    }

    [Fact]
    public void What_is_not_a_recording_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => SessionRecordingReader.Open(new MemoryStream("not a recording at all"u8.ToArray())));

        var bytes = RecordSample(out _, out _);
        var otherVersion = (byte[])bytes.Clone();
        otherVersion[8] = 2;
        Assert.Contains("version 2", Assert.Throws<InvalidDataException>(() => SessionRecordingReader.Open(new MemoryStream(otherVersion))).Message, StringComparison.Ordinal);

        var headerEnd = RecordingFormat.FileHeaderSize + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        var unknownKind = (byte[])bytes.Clone();
        unknownKind[headerEnd] = 9;
        using (var reader = SessionRecordingReader.Open(new MemoryStream(unknownKind)))
        {
            Assert.Throws<InvalidDataException>(() => reader.TryRead(out _));
        }

        using (var reader = SessionRecordingReader.Open(new MemoryStream([.. bytes, 0])))
        {
            Assert.Throws<InvalidDataException>(() => reader.ReadAll().Count());
        }
    }

    [Fact]
    public void Recording_file_names_carry_the_time_and_the_runner_only()
    {
        Assert.Equal("20261012T142233Z-headless.lsrec", RecordingFormat.FileName(Created, "headless"));
        Assert.Equal("20261012T142233Z-godot.lsrec", RecordingFormat.FileName(Created.ToOffset(TimeSpan.FromHours(-7)), "godot"));
        Assert.Throws<ArgumentException>(() => RecordingFormat.FileName(Created, "../x"));

        // Two processes started in the same second get different names (the log's and the recording's).
        Assert.Equal("20261012T142233Z-headless-4711.lsrec", RecordingFormat.FileName(Created, "headless", 4711));
        Assert.NotEqual(RecordingFormat.FileName(Created, "headless", 4711), RecordingFormat.FileName(Created, "headless", 4712));
        Assert.Throws<ArgumentException>(() => RecordingFormat.FileName(Created, "../x", 1));
    }

    /// <summary>Records a session touching every frame kind; returns its bytes, the frames as written and the digest.</summary>
    private static byte[] RecordSample(out List<RecordingFrame> frames, out byte[] digest, bool includeBigMessage = true)
    {
        frames = [];
        var time = new ManualTimeProvider();
        var clock = new SessionClock(time);
        var stream = new MemoryStream();
        digest = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
        using (var recorder = new SessionRecorder(stream, Header, clock, NullLogSink.Instance))
        {
            void Message(MessageDirection direction, double t, uint sequence, MessageQueue queue, byte[] body, List<RecordingFrame> into)
            {
                recorder.Message(direction, t, sequence, queue, body);
                var opcode = body.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0u;
                var omitted = direction == MessageDirection.Outbound && !RecordingFormat.KeepsClientBody(opcode);
                into.Add(new RecordingFrame(RecordingFrameKind.Message, t, new RecordedMessage(direction, sequence, queue, opcode, body.Length, omitted, omitted ? default : body), default, default));
            }

            void Event(double t, SessionEvent e, List<RecordingFrame> into)
            {
                recorder.SessionEvent(t, e);
                into.Add(new RecordingFrame(RecordingFrameKind.SessionEvent, t, default, e, default));
            }

            void Notice(double t, RecordingNotice n, List<RecordingFrame> into)
            {
                recorder.Notice(t, n);
                into.Add(new RecordingFrame(RecordingFrameKind.Notice, t, default, default, n));
            }

            var big = new byte[70_000];
            new Random(7).NextBytes(big);
            BinaryPrimitives.WriteUInt32LittleEndian(big, MessageOpcodes.GameEvent);

            Event(0.001, Net.Session.SessionEvent.PhaseChanged(10, SessionPhase.Idle, SessionPhase.Connecting), frames);
            Event(0.002, Net.Session.SessionEvent.RetryScheduled(20, SessionPhase.Connecting, 2, TimeSpan.FromSeconds(3), 0x15, "lost ConnectRequest"), frames);
            Event(0.25, Net.Session.SessionEvent.ConnectRequestReceived(30, SessionPhase.Handshaking, 123456.789), frames);
            Message(MessageDirection.Inbound, 0.5, 2, MessageQueue.Database, Body(MessageOpcodes.DddInterrogation, 0x9A, 0x00, 0x01), frames);
            Message(MessageDirection.Outbound, 0.51, 3, MessageQueue.Database, Body(MessageOpcodes.DddInterrogationResponse, 1, 2, 3, 4), frames);
            if (includeBigMessage)
            {
                Message(MessageDirection.Inbound, 0.75, 4, MessageQueue.Ui, big, frames);
            }

            Message(MessageDirection.Outbound, 0.8, 5, MessageQueue.Ui, Body(0x0000F7EAu, 9), frames);
            Message(MessageDirection.Inbound, 0.81, 6, MessageQueue.Ui, [0xAB, 0xCD], frames);
            Message(MessageDirection.Outbound, 0.82, 7, MessageQueue.Ui, [], frames);
            Notice(0.9, new RecordingNotice(7, 0x50000001, "Logging off..."), frames);
            Notice(0.91, new RecordingNotice(8, 0, null), frames);
            Notice(0.92, new RecordingNotice(9, 1, "Caf\u00e9 \u2014 \U0001F600"), frames);
            Event(1.5, Net.Session.SessionEvent.Warned(40, SessionPhase.InWorld, SessionWarning.GapSkipped, "skipped 1 message"), frames);
            Event(2, Net.Session.SessionEvent.Failed(50, new SessionFailure(SessionFailureKind.Silence, SessionPhase.InWorld, "nothing arrived for 20 s", 7)), frames);
            Event(2.1, Net.Session.SessionEvent.Booted(60, SessionPhase.Failed, null), frames);
            time.Advance(TimeSpan.FromSeconds(3));
            recorder.Complete(digest);
        }

        return stream.ToArray();
    }

    private static void AssertFrame(RecordingFrame expected, RecordingFrame actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Time), BitConverter.DoubleToInt64Bits(actual.Time));
        switch (expected.Kind)
        {
            case RecordingFrameKind.Message:
                Assert.Equal(expected.Message with { Body = default }, actual.Message with { Body = default });
                Assert.Equal(expected.Message.Body.ToArray(), actual.Message.Body.ToArray());
                break;
            case RecordingFrameKind.SessionEvent:
                Assert.Equal(expected.SessionEvent with { Failure = null }, actual.SessionEvent with { Failure = null });
                Assert.Equal(expected.SessionEvent.Failure, actual.SessionEvent.Failure);
                break;
            default:
                Assert.Equal(expected.Notice, actual.Notice);
                break;
        }
    }

    private static List<int> FrameEnds(byte[] bytes, int start)
    {
        var ends = new List<int>();
        for (var at = start; at < bytes.Length;)
        {
            at += RecordingFormat.FrameHeaderSize + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
            ends.Add(at);
        }

        return ends;
    }

    private static byte[] Body(uint opcode, params byte[] rest)
    {
        var body = new byte[4 + rest.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(body, opcode);
        rest.CopyTo(body, 4);
        return body;
    }

    /// <summary>Bytes shaped like a LoginRequest section: String16L client version, then the account and password.</summary>
    private static byte[] LoginRequestLike(string password)
    {
        var bytes = new List<byte> { 4, 0 };
        bytes.AddRange("1802"u8.ToArray());
        bytes.AddRange([0, 0, 2, 0, 0, 0]);
        bytes.AddRange(Encoding.ASCII.GetBytes("lifestone:" + password));
        return [.. bytes];
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    private static void Collect(JsonElement element, List<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            keys.Add(property.Name);
            Collect(property.Value, keys);
        }
    }

    /// <summary>A stream over memory that counts writes and flushes, and can fail its writes.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly MemoryStream _inner = new();

        public int Writes { get; private set; }

        public int Flushes { get; private set; }

        public bool FailWrites { get; init; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            if (FailWrites)
            {
                throw new IOException("the disk is full");
            }

            _inner.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Flush() => Flushes++;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>The recorder's allocation budget (ST6): recording a message allocates nothing.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class RecordingAllocationTests
{
    [Fact]
    public void Recording_a_message_allocates_nothing()
    {
        var stream = new MemoryStream(4 * 1024 * 1024);
        using var recorder = new SessionRecorder(
            stream, new RecordingHeader { Runner = "test", CreatedUtc = DateTimeOffset.UnixEpoch }, new SessionClock(new ManualTimeProvider()), NullLogSink.Instance);
        var body = new byte[120];
        BinaryPrimitives.WriteUInt32LittleEndian(body, MessageOpcodes.UpdatePosition);
        recorder.Message(MessageDirection.Inbound, 0, 0, MessageQueue.SmartBox, body);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var window = AllocationWindow.Open();
            for (var i = 0; i < 1000; i++)
            {
                recorder.Message(MessageDirection.Inbound, i, (uint)i, MessageQueue.SmartBox, body);
            }

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
