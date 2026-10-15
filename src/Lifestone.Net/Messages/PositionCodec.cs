using System.Numerics;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The 32-byte position the world messages carry (docs/protocol.md "Conventions", §5.3 and §5.4): u32 cell, 3 × f32
/// origin, then the quaternion as W, X, Y, Z. Retail packs it with <c>Position::Pack</c> and reads it with
/// <c>Position::UnPack</c> (11.4186 0x005A9690, 11.6096 0x005AA740); the origin alone (a MoveTo target) with
/// <c>Position::UnPackOrigin</c> (11.4186 0x005A9790, 11.6096 0x005AA840). The rotation stays exactly as sent, never
/// normalised (m2-plan §3.3 N7: normalisation belongs to the client).
/// </summary>
internal static class PositionCodec
{
    /// <summary>The wire size of a position.</summary>
    public const int Size = 32;

    /// <summary>The wire size of an origin (cell and the three coordinates).</summary>
    public const int OriginSize = 16;

    /// <summary>Reads a whole position.</summary>
    public static bool TryRead(ref WireReader reader, out AcPosition position)
    {
        if (!TryReadOrigin(ref reader, out var cell, out var origin)
            || !reader.TryReadF32(out var w)
            || !reader.TryReadF32(out var x)
            || !reader.TryReadF32(out var y)
            || !reader.TryReadF32(out var z))
        {
            position = default;
            return false;
        }

        position = new AcPosition(cell, origin, new Quaternion(x, y, z, w));
        return true;
    }

    /// <summary>Reads a cell and its origin (no rotation).</summary>
    public static bool TryReadOrigin(ref WireReader reader, out uint cell, out Vector3 origin)
    {
        if (!reader.TryReadU32(out cell) || !TryReadVector3(ref reader, out origin))
        {
            origin = default;
            return false;
        }

        return true;
    }

    /// <summary>Reads three f32 (a velocity, acceleration or angular velocity).</summary>
    public static bool TryReadVector3(ref WireReader reader, out Vector3 value)
    {
        if (!reader.TryReadF32(out var x) || !reader.TryReadF32(out var y) || !reader.TryReadF32(out var z))
        {
            value = default;
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    /// <summary>Writes a whole position: cell, origin, then W, X, Y, Z.</summary>
    public static void Write(PacketWriter writer, in AcPosition position)
    {
        writer.WriteUInt32(position.CellId);
        WriteVector3(writer, position.Origin);
        writer.WriteFloat(position.Rotation.W);
        writer.WriteFloat(position.Rotation.X);
        writer.WriteFloat(position.Rotation.Y);
        writer.WriteFloat(position.Rotation.Z);
    }

    /// <summary>Writes three f32.</summary>
    public static void WriteVector3(PacketWriter writer, Vector3 value)
    {
        writer.WriteFloat(value.X);
        writer.WriteFloat(value.Y);
        writer.WriteFloat(value.Z);
    }

    /// <summary>Writes the four sequences the client echoes after its position (instance, server control, teleport,
    /// force position), as retail's three movement packs do (<see cref="MovementActionCodecs"/> cites them).</summary>
    public static void WriteSequences(PacketWriter writer, in ObjSeqs sequences)
    {
        writer.WriteUInt16(sequences.Instance);
        writer.WriteUInt16(sequences.ServerControl);
        writer.WriteUInt16(sequences.Teleport);
        writer.WriteUInt16(sequences.ForcePosition);
    }
}
