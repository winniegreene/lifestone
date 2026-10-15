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
        Session(ref w, state);
        Characters(ref w, state.Characters);
        Self(ref w, state.Self);
        Qualities(ref w, state.Self.Qualities);
        Objects(ref w, state.Objects);
        Chat(ref w, state.Chat);
        hash.GetHashAndReset(destination);
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

    private static void Qualities(ref Writer w, PlayerQualities q)
    {
        w.U8(q.IsKnown ? (byte)1 : (byte)0);
        Table(ref w, q.Ints, static (ref Writer x, int v) => x.I64(v));
        Table(ref w, q.Int64s, static (ref Writer x, long v) => x.I64(v));
        Table(ref w, q.Bools, static (ref Writer x, bool v) => x.U8(v ? (byte)1 : (byte)0));
        Table(ref w, q.Floats, static (ref Writer x, double v) => x.I64(BitConverter.DoubleToInt64Bits(v)));
        Table(ref w, q.Strings, static (ref Writer x, string v) => x.Text(v));
        Table(ref w, q.DataIds, static (ref Writer x, uint v) => x.U32(v));
        Table(ref w, q.InstanceIds, static (ref Writer x, uint v) => x.U32(v));
        Table(ref w, q.Attributes, static (ref Writer x, Lifestone.Net.Messages.AttributeDesc a) =>
        {
            x.U32(a.Ranks);
            x.U32(a.Start);
            x.U32(a.Experience);
        });
        Table(ref w, q.Vitals, static (ref Writer x, Lifestone.Net.Messages.VitalDesc v) =>
        {
            x.U32(v.Ranks);
            x.U32(v.Start);
            x.U32(v.Experience);
            x.U32(v.Current);
        });
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
    }

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
