// Partly ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/GameEvents.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only ParseAttackDone and
// ParseCombatCommenceAttack; namespace Lifestone.Net.Messages; internal; TryParse over WireReader on the whole F7B0 message
// (OpenAC takes the payload only, and accepts a longer one) that reads every byte and never throws; the record carries only
// the WeenieError (OpenAC's unused AttackSequence field, always 0, is dropped); the empty 01B8 is a record of its own; XML
// docs with the ACE cites added.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x01A7 AttackDone (ACE <c>GameEventAttackDone</c>): the attack is over; 0 for a finished swing, 0x0036
/// (ACE's <c>WeenieError.ActionCancelled</c> at @95ef426d; YoureTooBusy is 0x001D, a different error) when the attack could not start or repeat.</summary>
/// <param name="WeenieError">The WeenieError, 0 for none.</param>
public readonly record struct AttackDoneEvent(uint WeenieError);

/// <summary>GameEvent 0x01B8 CombatCommenceAttack (ACE <c>GameEventCombatCommenceAttack</c>): the envelope alone.</summary>
public readonly record struct CommenceAttackEvent;

/// <summary>
/// The attack game events (M4 1.2 s0), laid out as ACE writes them: 0x01A7 {u32 WeenieError} and 0x01B8 (empty). Each
/// parser takes the whole F7B0 message and accepts it only when every byte was read.
/// </summary>
internal static class AttackEventCodecs
{
    /// <summary>0x01A7 AttackDone.</summary>
    public static bool TryParseAttackDone(ReadOnlySpan<byte> message, out AttackDoneEvent attackDone)
    {
        attackDone = default;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var error) || !reader.IsAtEnd)
        {
            return false;
        }

        attackDone = new AttackDoneEvent(error);
        return true;
    }

    /// <summary>0x01B8 CombatCommenceAttack: the envelope alone.</summary>
    public static bool TryParseCommenceAttack(ReadOnlySpan<byte> message) => message.Length == GameEventEnvelope.HeaderSize;
}
