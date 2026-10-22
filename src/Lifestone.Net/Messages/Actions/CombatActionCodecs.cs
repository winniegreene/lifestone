// Ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/AttackTargetRequest.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// the three Build* methods returning a fresh byte[] become Write* methods over PacketWriter and the GameAction envelope
// the session's other sends use (GameActionEnvelope.WriteHeader, which owns the F7B1 opcode, the sequence and the action
// type); the local opcode constants move to GameActionTypes; a non-finite power or accuracy is refused with
// ArgumentException before anything is written (OpenAC writes it as is); XML docs with the retail and ACE cites added.

using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The three combat game actions (M4 1.2 s0): 0x0008 TargetedMeleeAttack {u32 target, u32 height, f32 power}
/// (<c>CM_Combat::Event_TargetedMeleeAttack</c>, 11.4186 0x006A9C10, 11.6096 0x006AAB70), 0x000A TargetedMissileAttack
/// {u32 target, u32 height, f32 accuracy} (<c>CM_Combat::Event_TargetedMissileAttack</c>, 11.4186 0x006A9D60, 11.6096
/// 0x006AACC0) and 0x01B7 CancelAttack (no payload; <c>CM_Combat::Event_CancelAttack</c>, 11.4186 0x006A99E0, 11.6096
/// 0x006AA940). ACE reads the same fields in the same order (<c>GameActionTargetedMeleeAttack</c>,
/// <c>GameActionTargetedMissileAttack</c>, <c>GameActionCancelAttack</c>).
/// </summary>
/// <remarks>
/// The height is the attack height: 1 High, 2 Medium, 3 Low. ACE clamps the power and the accuracy to 0..1
/// (<c>Math.Clamp</c> in <c>HandleActionTargetedMeleeAttack</c> and <c>HandleActionTargetedMissileAttack</c>) and OpenAC
/// does not, so these writers send the value as given and the clamping is the caller's; only NaN and infinity are refused.
/// </remarks>
internal static class CombatActionCodecs
{
    /// <summary>The attack height of a High attack.</summary>
    public const uint HeightHigh = 1;

    /// <summary>The attack height of a Medium attack.</summary>
    public const uint HeightMedium = 2;

    /// <summary>The attack height of a Low attack.</summary>
    public const uint HeightLow = 3;

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x0008, target, height, power}.</summary>
    public static void WriteMeleeAttack(PacketWriter writer, uint sequence, uint target, uint height, float power)
    {
        RequireFinite(power, nameof(power));
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TargetedMeleeAttack);
        writer.WriteUInt32(target);
        writer.WriteUInt32(height);
        writer.WriteFloat(power);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x000A, target, height, accuracy}.</summary>
    public static void WriteMissileAttack(PacketWriter writer, uint sequence, uint target, uint height, float accuracy)
    {
        RequireFinite(accuracy, nameof(accuracy));
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.TargetedMissileAttack);
        writer.WriteUInt32(target);
        writer.WriteUInt32(height);
        writer.WriteFloat(accuracy);
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x01B7}.</summary>
    public static void WriteCancelAttack(PacketWriter writer, uint sequence) =>
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.CancelAttack);

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentException("The level must be a finite number (ACE clamps it to 0..1).", name);
        }
    }
}
