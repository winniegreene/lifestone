// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CreateObject.cs and
// src/AcDream.Core.Net/Messages/PhysicsSpawnData.cs (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors)
// with changes: the PhysicsDesc half of CreateObject.TryParse as an internal static PhysicsDescCodec over N2's WireReader
// that returns false instead of catching, into the N0 record PhysicsDesc (PhysicsSpawnData's fields); the rotation stays
// raw (OpenAC's AcDream.Core.Physics.PositionFrameValidation scaled it to unit length; normalisation belongs to the
// client); the movement buffer is parsed by MovementDataCodec (defect 2 fixed) and must fill exactly its length; the
// children count is checked against the bytes left instead of capped at 1024; the closing Align must be present.

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The PhysicsDesc block of F745/F7DB CreateObject (docs/protocol.md §5.3; ACE
/// <c>WorldObject_Networking.SerializePhysicsData</c>): u32 flags, u32 PhysicsState, the flagged fields in ACE's write
/// order (Movement or AnimationFrame, Position, MTable, STable, PeTable, CSetup, Parent, Children, ObjScale, Friction,
/// Elasticity, Translucency, Velocity, Acceleration, Omega, DefaultScript, DefaultScriptIntensity), the nine u16
/// sequences, then Align. Retail reads the same order in <c>PhysicsDesc::UnPack</c> (11.4186 0x0051DDD0, 11.6096
/// 0x0051E8E0).
/// </summary>
/// <remarks>
/// The Movement field is a u32 byte length and, when it is not 0, that many bytes of header-less MovementData and a u32
/// is-autonomous. Retail copies the bytes into their own buffer and unpacks them later
/// (<c>CPhysicsObj::unpack_movement</c>, 11.4186 0x00512040, 11.6096 0x00512B10); here they are parsed at once, over a
/// reader of exactly that length, so the body must fill it. AnimationFrame is read only when Movement is absent, as both
/// ACE and retail do; the raw flags keep both bits.
/// </remarks>
internal static class PhysicsDescCodec
{
    /// <summary>The size of one child link: u32 GUID, u32 location.</summary>
    private const int ChildSize = 8;

    /// <summary>Reads one PhysicsDesc block from <paramref name="message"/>, its closing Align included.</summary>
    public static bool TryRead(ReadOnlySpan<byte> message, ref WireReader reader, out PhysicsDesc physics)
    {
        physics = null!;
        if (!reader.TryReadU32(out var rawFlags) || !reader.TryReadU32(out var state))
        {
            return false;
        }

        var flags = (PhysicsDescFlags)rawFlags;
        ServerMotionState? movement = null;
        var movementIsAutonomous = false;
        uint? animationFrame = null;
        if (Has(flags, PhysicsDescFlags.Movement))
        {
            if (!reader.TryReadU32(out var length) || length > (uint)reader.Remaining)
            {
                return false;
            }

            if (length > 0)
            {
                var start = reader.Position;
                var buffer = new WireReader(message.Slice(start, (int)length));
                if (!MovementDataCodec.TryRead(ref buffer, out var motion) || !buffer.IsAtEnd
                    || !reader.TrySkip((int)length)
                    || !reader.TryReadU32(out var autonomous))
                {
                    return false;
                }

                movement = motion;
                movementIsAutonomous = autonomous != 0;
            }
        }
        else if (Has(flags, PhysicsDescFlags.AnimationFrame))
        {
            if (!reader.TryReadU32(out var frame))
            {
                return false;
            }

            animationFrame = frame;
        }

        AcPosition? position = null;
        if (Has(flags, PhysicsDescFlags.Position))
        {
            if (!PositionCodec.TryRead(ref reader, out var read))
            {
                return false;
            }

            position = read;
        }

        uint? motionTable = null, soundTable = null, effectTable = null, setup = null;
        if (!TryReadU32If(ref reader, flags, PhysicsDescFlags.MTable, ref motionTable)
            || !TryReadU32If(ref reader, flags, PhysicsDescFlags.STable, ref soundTable)
            || !TryReadU32If(ref reader, flags, PhysicsDescFlags.PeTable, ref effectTable)
            || !TryReadU32If(ref reader, flags, PhysicsDescFlags.CSetup, ref setup))
        {
            return false;
        }

        ObjectLink? parent = null;
        if (Has(flags, PhysicsDescFlags.Parent))
        {
            if (!reader.TryReadU32(out var parentGuid) || !reader.TryReadU32(out var location))
            {
                return false;
            }

            parent = new ObjectLink(parentGuid, location);
        }

        var children = ImmutableArray<ObjectLink>.Empty;
        if (Has(flags, PhysicsDescFlags.Children))
        {
            if (!reader.TryReadCount(ChildSize, out var count))
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<ObjectLink>(count);
            for (var i = 0; i < count; i++)
            {
                if (!reader.TryReadU32(out var childGuid) || !reader.TryReadU32(out var location))
                {
                    return false;
                }

                builder.Add(new ObjectLink(childGuid, location));
            }

            children = builder.MoveToImmutable();
        }

        float? scale = null, friction = null, elasticity = null, translucency = null;
        if (!TryReadF32If(ref reader, flags, PhysicsDescFlags.ObjScale, ref scale)
            || !TryReadF32If(ref reader, flags, PhysicsDescFlags.Friction, ref friction)
            || !TryReadF32If(ref reader, flags, PhysicsDescFlags.Elasticity, ref elasticity)
            || !TryReadF32If(ref reader, flags, PhysicsDescFlags.Translucency, ref translucency))
        {
            return false;
        }

        System.Numerics.Vector3? velocity = null, acceleration = null, omega = null;
        if (!TryReadVector3If(ref reader, flags, PhysicsDescFlags.Velocity, ref velocity)
            || !TryReadVector3If(ref reader, flags, PhysicsDescFlags.Acceleration, ref acceleration)
            || !TryReadVector3If(ref reader, flags, PhysicsDescFlags.Omega, ref omega))
        {
            return false;
        }

        uint? defaultScript = null;
        float? defaultScriptIntensity = null;
        if (!TryReadU32If(ref reader, flags, PhysicsDescFlags.DefaultScript, ref defaultScript)
            || !TryReadF32If(ref reader, flags, PhysicsDescFlags.DefaultScriptIntensity, ref defaultScriptIntensity))
        {
            return false;
        }

        if (!TryReadTimestamps(ref reader, out var timestamps) || !reader.TryAlign())
        {
            return false;
        }

        physics = new PhysicsDesc
        {
            Flags = flags,
            State = (PhysicsState)state,
            Movement = movement,
            MovementIsAutonomous = movementIsAutonomous,
            AnimationFrame = animationFrame,
            Position = position,
            MotionTableId = motionTable,
            SoundTableId = soundTable,
            PhysicsEffectTableId = effectTable,
            SetupId = setup,
            Parent = parent,
            Children = children,
            Scale = scale,
            Friction = friction,
            Elasticity = elasticity,
            Translucency = translucency,
            Velocity = velocity,
            Acceleration = acceleration,
            Omega = omega,
            DefaultScript = defaultScript,
            DefaultScriptIntensity = defaultScriptIntensity,
            Timestamps = timestamps,
        };
        return true;
    }

    /// <summary>The nine u16 sequences: position, movement, state, vector, teleport, server control, force position,
    /// visual description, instance.</summary>
    private static bool TryReadTimestamps(ref WireReader reader, out PhysicsTimestamps timestamps)
    {
        timestamps = default;
        if (!reader.TryReadU16(out var position)
            || !reader.TryReadU16(out var movement)
            || !reader.TryReadU16(out var state)
            || !reader.TryReadU16(out var vector)
            || !reader.TryReadU16(out var teleport)
            || !reader.TryReadU16(out var serverControl)
            || !reader.TryReadU16(out var forcePosition)
            || !reader.TryReadU16(out var objDesc)
            || !reader.TryReadU16(out var instance))
        {
            return false;
        }

        timestamps = new PhysicsTimestamps(position, movement, state, vector, teleport, serverControl, forcePosition, objDesc, instance);
        return true;
    }

    private static bool Has(PhysicsDescFlags flags, PhysicsDescFlags flag) => (flags & flag) != 0;

    private static bool TryReadU32If(ref WireReader reader, PhysicsDescFlags flags, PhysicsDescFlags flag, ref uint? value)
    {
        if (!Has(flags, flag))
        {
            return true;
        }

        if (!reader.TryReadU32(out var read))
        {
            return false;
        }

        value = read;
        return true;
    }

    private static bool TryReadF32If(ref WireReader reader, PhysicsDescFlags flags, PhysicsDescFlags flag, ref float? value)
    {
        if (!Has(flags, flag))
        {
            return true;
        }

        if (!reader.TryReadF32(out var read))
        {
            return false;
        }

        value = read;
        return true;
    }

    private static bool TryReadVector3If(ref WireReader reader, PhysicsDescFlags flags, PhysicsDescFlags flag, ref System.Numerics.Vector3? value)
    {
        if (!Has(flags, flag))
        {
            return true;
        }

        if (!PositionCodec.TryReadVector3(ref reader, out var read))
        {
            return false;
        }

        value = read;
        return true;
    }
}
