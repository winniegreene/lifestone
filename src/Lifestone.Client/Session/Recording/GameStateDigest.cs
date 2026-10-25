using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// A SHA-256 fingerprint of a session's read model (step ST13; docs/session.md §18): what a recording's trailer keeps
/// (<see cref="RecordingTrailer.StateDigest"/>) and what a replay of that recording must reproduce. Two sessions fed the
/// same server messages, session events and local notices have the same digest.
/// </summary>
/// <remarks>
/// <para><b>What goes in</b>, in this order, after the tag <c>lsdigest/1</c>: the phase, the offline flag, the data check
/// and the failure (kind, phase, code, message); the world's name; the character list (slot count, selection, then each
/// character's guid, name and deletion field in server order); the player (guid, name, travel, position, the nine
/// sequences, the physics state); the player's qualities (each table sorted by key); every object sorted by guid (its
/// name, setup, self flag, state, position, parent, motion presence and autonomy, velocity, angular velocity, the nine
/// sequences, health); and the chat (the total count, then each kept line's kind, type, sender and text).</para>
/// <para><b>What stays out</b>: every time (chat line times, creation times, the server's clock), because a replay runs on
/// its own clock; the action log and the handles (actions are not state); counters.</para>
/// <para><b>Canonical form.</b> Integers little-endian; strings as a u32 UTF-8 byte count then the bytes (0xFFFFFFFF for
/// null); positions, velocities and the health fraction quantised to 1e-4 (metres, metres per second, radians per second,
/// quaternion components) and written as i64, so float noise below a tenth of a millimetre does not change the digest;
/// the qualities' floating-point values bit for bit (they come straight off the wire).</para>
/// </remarks>
public static class GameStateDigest
{
    /// <summary>The digest's size in bytes.</summary>
    public const int Size = RecordingFormat.DigestSize;

    /// <summary>The quantum of positions, velocities and fractions: 1e-4.</summary>
    public const double Quantum = 1e-4;

    /// <summary>The digest of <paramref name="state"/> as a new 32-byte array.</summary>
    /// <param name="state">The session's state.</param>
    public static byte[] Compute(GameState state)
    {
        var digest = new byte[Size];
        Compute(state, digest);
        return digest;
    }

    /// <summary>Writes the digest of <paramref name="state"/> into <paramref name="destination"/> (32 bytes).</summary>
    /// <param name="state">The session's state.</param>
    /// <param name="destination">Where to write it.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Size"/>.</exception>
    public static void Compute(GameState state, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (destination.Length < Size)
        {
            throw new ArgumentException($"the digest needs {Size} bytes", nameof(destination));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var w = new Writer(hash);
        w.Text("lsdigest/1");
        foreach (var part in PartWriters)
        {
            part.Write(ref w, state);
        }

        hash.GetHashAndReset(destination);
    }

    /// <summary>
    /// The digest's parts (M4 P5b F4, TODO(m4-replay-vitae-digest)): the same canonical bytes as <see cref="Compute(GameState)"/>, cut where the state's
    /// parts meet (the session, the character list, the player, each quality table, the objects, the chat), each with its own short hash and a few counts.
    /// The recorder keeps them beside the digest (<c>SessionNotices.DigestParts</c>), so a replay that misses the digest names the part it missed.
    /// </summary>
    /// <param name="state">The session's state.</param>
    public static IReadOnlyList<DigestPart> Parts(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var parts = new List<DigestPart>(PartWriters.Length);
        Span<byte> full = stackalloc byte[Size];
        foreach (var part in PartWriters)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var w = new Writer(hash);
            part.Write(ref w, state);
            hash.GetHashAndReset(full);
            parts.Add(new DigestPart(part.Name, Convert.ToHexStringLower(full[..DigestPart.HashBytes]), part.Detail(state)));
        }

        return parts;
    }

    /// <summary>The parts as one line, <c>name=hash(detail) ...</c> (what the recorder's notice carries; <see cref="ParseParts"/> reads it back).</summary>
    /// <param name="parts">The parts.</param>
    public static string FormatParts(IEnumerable<DigestPart> parts) => string.Join(' ', parts.Select(p => p.ToString()));

    /// <summary>Reads <see cref="FormatParts"/>'s line back (a malformed entry is kept with an empty hash, so it differs).</summary>
    /// <param name="text">The line.</param>
    public static IReadOnlyList<DigestPart> ParseParts(string? text)
    {
        var parts = new List<DigestPart>();
        foreach (var entry in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            var open = entry.IndexOf('(', StringComparison.Ordinal);
            if (eq <= 0 || open < eq || !entry.EndsWith(')'))
            {
                parts.Add(new DigestPart(entry, string.Empty, string.Empty));
                continue;
            }

            parts.Add(new DigestPart(entry[..eq], entry[(eq + 1)..open], entry[(open + 1)..^1]));
        }

        return parts;
    }

    /// <summary>
    /// The parts that differ between <paramref name="recorded"/> and <paramref name="replayed"/>, one sentence each (<c>chat: recorded 0123..(total=337,kept=337),
    /// replayed 4567..(total=336,kept=336)</c>); empty when every part matches.
    /// </summary>
    /// <param name="recorded">The recording's parts.</param>
    /// <param name="replayed">The replay's parts.</param>
    public static IReadOnlyList<string> CompareParts(IReadOnlyList<DigestPart> recorded, IReadOnlyList<DigestPart> replayed)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(replayed);
        var differ = new List<string>();
        foreach (var r in recorded)
        {
            var mine = replayed.FirstOrDefault(p => p.Name == r.Name);
            if (mine.Name is null)
            {
                differ.Add($"{r.Name}: recorded {r.Hash}({r.Detail}), not a part this build replays");
            }
            else if (mine.Hash != r.Hash)
            {
                differ.Add($"{r.Name}: recorded {r.Hash}({r.Detail}), replayed {mine.Hash}({mine.Detail})");
            }
        }

        foreach (var mine in replayed)
        {
            if (!recorded.Any(p => p.Name == mine.Name))
            {
                differ.Add($"{mine.Name}: replayed {mine.Hash}({mine.Detail}), not in the recording");
            }
        }

        return differ;
    }

    /// <summary>The digest as 64 lower-case hexadecimal digits (logs, summaries).</summary>
    /// <param name="digest">A digest.</param>
    public static string ToHex(ReadOnlySpan<byte> digest) => Convert.ToHexStringLower(digest);

    /// <summary>A value quantised to <see cref="Quantum"/> (non-finite values map to <see cref="long.MinValue"/>).</summary>
    internal static long Quantise(double value) =>
        double.IsFinite(value) ? (long)Math.Round(value / Quantum, MidpointRounding.ToEven) : long.MinValue;

    private static void Session(ref Writer w, GameState state)
    {
        w.U8((byte)state.Phase);
        w.U8(state.IsOffline ? (byte)1 : (byte)0);
        w.U8((byte)state.DataCheck);
        if (state.Failure is { } failure)
        {
            w.U8(1);
            w.U8((byte)failure.Kind);
            w.U8((byte)failure.Phase);
            w.U32(failure.Code);
            w.Text(failure.Message);
        }
        else
        {
            w.U8(0);
        }

        w.Text(state.WorldName);
    }

    private static void Characters(ref Writer w, CharacterListState list)
    {
        w.U32(list.SlotCount);
        w.U32(list.SelectedGuid);
        w.U32((uint)list.Entries.Count);
        foreach (var c in list.Entries)
        {
            w.U32(c.Guid);
            w.Text(c.Name);
            w.U32(c.DeleteTimeout);
        }
    }

    private static void Self(ref Writer w, SelfState self)
    {
        w.U32(self.Guid);
        w.Text(self.Name);
        w.U8((byte)self.Travel);
        Position(ref w, self.Position);
        Stamps(ref w, self.Stamps);
        w.U32((uint)self.PhysicsState);
    }

    private delegate void PartWriter(ref Writer w, GameState state);

    /// <summary>One part of the canonical form: its name, its writer and its counts.</summary>
    private sealed record Part(string Name, PartWriter Write, Func<GameState, string> Detail);

    /// <summary>The parts in digest order: their bytes, one after another after the tag, are the whole digest's input (unchanged by the cut).</summary>
    private static readonly Part[] PartWriters =
    [
        new("session", static (ref Writer w, GameState s) => Session(ref w, s), static s => Invariant($"phase={s.Phase},failure={(s.Failure is null ? 0 : 1)}")),
        new("characters", static (ref Writer w, GameState s) => Characters(ref w, s.Characters), static s => Invariant($"n={s.Characters.Entries.Count},selected={s.Characters.SelectedGuid:X8}")),
        new("self", static (ref Writer w, GameState s) => Self(ref w, s.Self), static s => Invariant($"guid={s.Self.Guid:X8},travel={s.Self.Travel},cell={(s.Self.Position is { } p ? p.CellId : 0):X8}")),
        new("qualities.ints", static (ref Writer w, GameState s) => QualityInts(ref w, s.Self.Qualities), static s => Invariant($"known={(s.Self.Qualities.IsKnown ? 1 : 0)},n={s.Self.Qualities.Ints.Count}")),
        new("qualities.int64s", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.Int64s, static (ref Writer x, long v) => x.I64(v)), static s => Count(s.Self.Qualities.Int64s.Count)),
        new("qualities.bools", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.Bools, static (ref Writer x, bool v) => x.U8(v ? (byte)1 : (byte)0)), static s => Count(s.Self.Qualities.Bools.Count)),
        new("qualities.floats", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.Floats, static (ref Writer x, double v) => x.I64(BitConverter.DoubleToInt64Bits(v))), static s => Count(s.Self.Qualities.Floats.Count)),
        new("qualities.strings", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.Strings, static (ref Writer x, string v) => x.Text(v)), static s => Count(s.Self.Qualities.Strings.Count)),
        new("qualities.dataIds", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.DataIds, static (ref Writer x, uint v) => x.U32(v)), static s => Count(s.Self.Qualities.DataIds.Count)),
        new("qualities.instanceIds", static (ref Writer w, GameState s) => Table(ref w, s.Self.Qualities.InstanceIds, static (ref Writer x, uint v) => x.U32(v)), static s => Count(s.Self.Qualities.InstanceIds.Count)),
        new("qualities.attributes", static (ref Writer w, GameState s) => QualityAttributes(ref w, s.Self.Qualities), static s => Count(s.Self.Qualities.Attributes.Count)),
        new("qualities.vitals", static (ref Writer w, GameState s) => QualityVitals(ref w, s.Self.Qualities), static s => Count(s.Self.Qualities.Vitals.Count)),
        new("qualities.skills", static (ref Writer w, GameState s) => QualitySkills(ref w, s.Self.Qualities), static s => Count(s.Self.Qualities.Skills.Count)),
        new("objects", static (ref Writer w, GameState s) => Objects(ref w, s.Objects), static s => Count(s.Objects.Count)),
        new("chat", static (ref Writer w, GameState s) => Chat(ref w, s.Chat), static s => Invariant($"total={s.Chat.Total},kept={s.Chat.Count}")),
    ];

    private static string Count(int n) => Invariant($"n={n}");

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static void QualityInts(ref Writer w, PlayerQualities q)
    {
        w.U8(q.IsKnown ? (byte)1 : (byte)0);
        Table(ref w, q.Ints, static (ref Writer x, int v) => x.I64(v));
    }

    private static void QualityAttributes(ref Writer w, PlayerQualities q) =>
        Table(ref w, q.Attributes, static (ref Writer x, Lifestone.Net.Messages.AttributeDesc a) =>
        {
            x.U32(a.Ranks);
            x.U32(a.Start);
            x.U32(a.Experience);
        });

    private static void QualityVitals(ref Writer w, PlayerQualities q) =>
        Table(ref w, q.Vitals, static (ref Writer x, Lifestone.Net.Messages.VitalDesc v) =>
        {
            x.U32(v.Ranks);
            x.U32(v.Start);
            x.U32(v.Experience);
            x.U32(v.Current);
        });

    private static void QualitySkills(ref Writer w, PlayerQualities q) =>
        Table(ref w, q.Skills, static (ref Writer x, Lifestone.Net.Messages.SkillDesc s) =>
        {
            x.U32(s.Ranks);
            x.U32(s.AdjustPp);
            x.U32(s.AdvancementClass);
            x.U32(s.Experience);
            x.U32(s.Init);
            x.U32(s.ResistanceAtLastCheck);
            x.I64(BitConverter.DoubleToInt64Bits(s.LastUsed));
        });

    private static void Objects(ref Writer w, WorldObjectTable objects)
    {
        var sorted = new List<WorldObject>(objects.Count);
        sorted.AddRange(objects.All);
        sorted.Sort(static (a, b) => a.Guid.CompareTo(b.Guid));
        w.U32((uint)sorted.Count);
        foreach (var o in sorted)
        {
            w.U32(o.Guid);
            w.Text(o.Name);
            w.U32(o.Physics.SetupId ?? 0);
            w.U8(o.IsSelf ? (byte)1 : (byte)0);
            w.U32((uint)o.State);
            Position(ref w, o.Position);
            if (o.Parent is { } parent)
            {
                w.U8(1);
                w.U32(parent.ParentGuid);
                w.I64(parent.Location);
                w.I64(parent.Placement);
            }
            else
            {
                w.U8(0);
            }

            w.U8((byte)((o.Motion is null ? 0 : 1) | (o.MotionAutonomous ? 2 : 0)));
            Vector(ref w, o.Velocity);
            Vector(ref w, o.Omega);
            Stamps(ref w, o.Stamps);
            if (o.Health is { } health)
            {
                w.U8(1);
                w.I64(Quantise(health));
            }
            else
            {
                w.U8(0);
            }
        }
    }

    private static void Chat(ref Writer w, ChatLog chat)
    {
        w.I64(chat.Total);
        w.U32((uint)chat.Count);
        for (var i = 0; i < chat.Count; i++)
        {
            var line = chat[i];
            w.U8((byte)line.Kind);
            w.U32(line.ChatType);
            w.Text(line.Sender);
            w.U32(line.SenderGuid);
            w.Text(line.Text);
        }
    }

    private static void Position(ref Writer w, CellFrame? position)
    {
        if (position is not { } p)
        {
            w.U8(0);
            return;
        }

        w.U8(1);
        w.U32(p.CellId);
        Vector(ref w, p.Origin);
        var q = p.Orientation;
        w.I64(Quantise(q.W));
        w.I64(Quantise(q.X));
        w.I64(Quantise(q.Y));
        w.I64(Quantise(q.Z));
    }

    private static void Vector(ref Writer w, Vector3 v)
    {
        w.I64(Quantise(v.X));
        w.I64(Quantise(v.Y));
        w.I64(Quantise(v.Z));
    }

    private static void Stamps(ref Writer w, PhysicsTimestamps s)
    {
        w.U16(s.Position);
        w.U16(s.Movement);
        w.U16(s.State);
        w.U16(s.Vector);
        w.U16(s.Teleport);
        w.U16(s.ServerControl);
        w.U16(s.ForcePosition);
        w.U16(s.ObjDesc);
        w.U16(s.Instance);
    }

    private delegate void ValueWriter<in T>(ref Writer w, T value);

    private static void Table<T>(ref Writer w, Dictionary<uint, T> table, ValueWriter<T> write)
    {
        var keys = new uint[table.Count];
        table.Keys.CopyTo(keys, 0);
        Array.Sort(keys);
        w.U32((uint)keys.Length);
        foreach (var key in keys)
        {
            w.U32(key);
            write(ref w, table[key]);
        }
    }

    /// <summary>Appends canonical little-endian values to the hash.</summary>
    private readonly ref struct Writer
    {
        private readonly IncrementalHash _hash;

        public Writer(IncrementalHash hash) => _hash = hash;

        public void U8(byte value) => _hash.AppendData([value]);

        public void U16(ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(b, value);
            _hash.AppendData(b);
        }

        public void U32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, value);
            _hash.AppendData(b);
        }

        public void I64(long value)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(b, value);
            _hash.AppendData(b);
        }

        public void Text(string? value)
        {
            if (value is null)
            {
                U32(uint.MaxValue);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            U32((uint)bytes.Length);
            _hash.AppendData(bytes);
        }
    }
}

/// <summary>One part of a <see cref="GameStateDigest"/> (<see cref="GameStateDigest.Parts"/>).</summary>
/// <param name="Name">The part (<c>session</c>, <c>chat</c>, <c>qualities.vitals</c> ...).</param>
/// <param name="Hash">The first <see cref="HashBytes"/> bytes of the SHA-256 of its canonical bytes, in lower-case hexadecimal.</param>
/// <param name="Detail">A few counts, for the reader (<c>total=336,kept=336</c>); not compared.</param>
public readonly record struct DigestPart(string Name, string Hash, string Detail)
{
    /// <summary>The bytes of a part's hash that are kept.</summary>
    public const int HashBytes = 8;

    /// <summary><c>name=hash(detail)</c>.</summary>
    /// <returns>The part as the recorder's notice writes it.</returns>
    public override string ToString() => Name + "=" + Hash + "(" + Detail + ")";
}
