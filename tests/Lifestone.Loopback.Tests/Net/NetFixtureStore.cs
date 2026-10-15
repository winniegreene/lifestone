using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lifestone.Net.Session;
using Lifestone.Net.Taps;
using Lifestone.Net.Types;
using Lifestone.Client.Tests;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>One message the client's raw tap saw: its direction, the session clock's timestamp, its fragment sequence, its queue and its body (opcode included).</summary>
internal sealed record CapturedMessage(MessageDirection Direction, long Timestamp, uint FragmentSequence, MessageQueue Queue, byte[] Body);

/// <summary>The client's raw tap (docs/protocol.md section 0 item 3): keeps every reassembled game message of one session, in both directions, as bytes.</summary>
internal sealed class NetCapture : IRawMessageTap
{
    private readonly List<CapturedMessage> _messages = [];

    /// <summary>The messages so far, in the order the tap saw them.</summary>
    public IReadOnlyList<CapturedMessage> Messages => _messages;

    /// <inheritdoc/>
    public void OnMessage(MessageDirection direction, long timestamp, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body) =>
        _messages.Add(new CapturedMessage(direction, timestamp, fragmentSequence, queue, body.ToArray()));
}

/// <summary>A message body held the account's password; nothing was written.</summary>
internal sealed class PasswordInFixtureException(string what) : InvalidOperationException(what);

/// <summary>
/// The golden captures of the live net tests (m2-plan N11, D14): messages recorded client-side from Lifestone's own raw tap into
/// <c>cache/net-fixtures/&lt;CacheKey&gt;/</c>, never committed (the folder is gitignored) and never taken from ACE's Packets log, which prints the
/// LoginRequest password. A body that holds the password in any form the wire could carry it in (ASCII, UTF-16, a LoginRequest-style String32L) is
/// refused whole: <see cref="Save"/> throws before it opens a file. The tap cannot see the LoginRequest (a packet section, not a message), so a hit
/// means a codec put the password into a message, which is a defect to fix, not a fixture to keep.
/// </summary>
internal static class NetFixtureStore
{
    /// <summary>The fixture file extension.</summary>
    public const string Extension = ".lsnetfx";

    private static ReadOnlySpan<byte> Magic => "LSNETFX1"u8;

    /// <summary>
    /// The folder name under <c>cache/net-fixtures/</c>: the pinned ACE commit and the client version the wire speaks (<c>ace-95ef426d412c-client1802</c>), so a
    /// fixture is never reread against another server build. Null when server/versions.json cannot be read.
    /// </summary>
    public static string? CacheKey()
    {
        var file = BackendProbe.MainCheckoutFiles.VersionsFile;
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
            var commit = json.RootElement.GetProperty("ace").GetProperty("commit").GetString();
            return string.IsNullOrEmpty(commit) ? null : $"ace-{commit[..Math.Min(12, commit.Length)]}-client1802";
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The folder of this server build's fixtures (it may not exist), or null without a <see cref="CacheKey"/>.</summary>
    public static string? Directory() => CacheKey() is { } key ? RepositoryFiles.PathOf("cache", "net-fixtures", key) : null;

    /// <summary>The fixture files that exist for this server build.</summary>
    public static IReadOnlyList<string> Files() =>
        Directory() is { } dir && System.IO.Directory.Exists(dir)
            ? [.. System.IO.Directory.EnumerateFiles(dir, "*" + Extension).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>The byte forms the password could take in a message body: ASCII, UTF-16LE, and a String32L (u32 count, u8 length, bytes).</summary>
    public static IReadOnlyList<byte[]> PasswordNeedles(AcCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var field = typeof(AcCredentials).GetField("_password", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AcCredentials has no _password field any more; update the net fixture guard");
        var password = (char[])((char[])field.GetValue(credentials)!).Clone();
        try
        {
            var ascii = Encoding.ASCII.GetBytes(password);
            var string32 = new byte[5 + ascii.Length];
            BinaryPrimitives.WriteInt32LittleEndian(string32, ascii.Length + 1);
            string32[4] = (byte)ascii.Length;
            ascii.CopyTo(string32, 5);
            return [ascii, Encoding.Unicode.GetBytes(password), string32];
        }
        finally
        {
            Array.Clear(password);
        }
    }

    /// <summary>How many bodies of <paramref name="messages"/> hold any of <paramref name="needles"/> (the count is all that is ever reported).</summary>
    public static int CountPasswordHits(IEnumerable<CapturedMessage> messages, IReadOnlyList<byte[]> needles)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(needles);
        var hits = 0;
        foreach (var message in messages)
        {
            if (needles.Any(n => n.Length > 0 && message.Body.AsSpan().IndexOf(n) >= 0))
            {
                hits++;
            }
        }

        return hits;
    }

    /// <summary>
    /// Writes <paramref name="messages"/> to <paramref name="path"/> after checking every body against <paramref name="needles"/>.
    /// </summary>
    /// <exception cref="PasswordInFixtureException">A body holds the password; no file is written. The message names the count and never a body.</exception>
    public static void Save(string path, IReadOnlyList<CapturedMessage> messages, IReadOnlyList<byte[]> needles)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(messages);
        var hits = CountPasswordHits(messages, needles);
        if (hits > 0)
        {
            throw new PasswordInFixtureException($"{hits} message bod{(hits == 1 ? "y holds" : "ies hold")} the account's password bytes; the fixture was not written (a codec must never put the password into a message)");
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(Magic);
        Span<byte> head = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, messages.Count);
        stream.Write(head);
        Span<byte> record = stackalloc byte[19];
        foreach (var m in messages)
        {
            record[0] = (byte)m.Direction;
            BinaryPrimitives.WriteUInt16LittleEndian(record[1..], (ushort)m.Queue);
            BinaryPrimitives.WriteUInt32LittleEndian(record[3..], m.FragmentSequence);
            BinaryPrimitives.WriteInt64LittleEndian(record[7..], m.Timestamp);
            BinaryPrimitives.WriteInt32LittleEndian(record[15..], m.Body.Length);
            stream.Write(record);
            stream.Write(m.Body);
        }
    }

    /// <summary>Reads a file <see cref="Save"/> wrote.</summary>
    /// <exception cref="InvalidDataException">The file is not a net fixture or is cut short.</exception>
    public static IReadOnlyList<CapturedMessage> Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var span = bytes.AsSpan();
        if (span.Length < Magic.Length + 4 || !span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("not a net fixture (bad magic)");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(span[Magic.Length..]);
        var at = Magic.Length + 4;
        var result = new List<CapturedMessage>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            if (span.Length - at < 19)
            {
                throw new InvalidDataException("a net fixture is cut short");
            }

            var direction = (MessageDirection)span[at];
            var queue = (MessageQueue)BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 1)..]);
            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 3)..]);
            var timestamp = BinaryPrimitives.ReadInt64LittleEndian(span[(at + 7)..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(span[(at + 15)..]);
            at += 19;
            if (length < 0 || span.Length - at < length)
            {
                throw new InvalidDataException("a net fixture is cut short");
            }

            result.Add(new CapturedMessage(direction, timestamp, sequence, queue, span.Slice(at, length).ToArray()));
            at += length;
        }

        return result;
    }
}

/// <summary>A fact that runs only when the live net tests have captured fixtures for this server build (<see cref="NetFixtureStore.Files"/>).</summary>
public sealed class RequiresNetFixturesFactAttribute : FactAttribute
{
    /// <summary>Skips with the reason when there are no fixtures.</summary>
    public RequiresNetFixturesFactAttribute()
    {
        if (NetFixtureStore.CacheKey() is not { } key)
        {
            Skip = "server/versions.json cannot be read, so the net fixtures' cache key is unknown";
        }
        else if (NetFixtureStore.Files().Count == 0)
        {
            Skip = $"no net fixtures under cache/net-fixtures/{key} (they are captured by the live net tests: start the backend and run the Loopback tests)";
        }
    }
}
