using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The combat and death notifications (M3 1.2 s0), laid out as ACE writes them (<c>ACE.Server/Network/GameEvent/Events/
/// GameEvent*Notification.cs</c>, <c>GameMessages/Messages/GameMessagePlayerKilled.cs</c>). Every parser takes the whole
/// message (for a game event, the whole F7B0 message, so String16L padding counts as on the wire) and accepts it only when
/// every byte was read. The texts are decoded as fields; composing the chat lines is lane s3's job.
/// </summary>
/// <remarks>
/// <para>The layouts, and where retail handles each (<c>ClientCombatSystem</c>, 11.6096, with 11.4186 where known):</para>
/// <list type="bullet">
/// <item>GE 0x01AC VictimNotification: String16L (<c>GameEventVictimNotification</c>;
/// <c>ClientCombatSystem::HandleVictimNotificationEvent</c> 0x0056D160, 11.4186 0x0056C410).</item>
/// <item>GE 0x01AD KillerNotification: String16L (<c>GameEventKillerNotification</c>; <c>Creature_Death.cs:76, 189-195</c>).
/// TODO(retail-handler-01AD): the retail handler is not in the symbol table.</item>
/// <item>GE 0x01B1 AttackerNotification: String16L defender name, u32 damage type, f64 percent, u32 damage, u32 critical,
/// u64 attack conditions (<c>GameEventAttackerNotification</c>, <c>Player_Combat.cs:151, 163</c>;
/// <c>ClientCombatSystem::HandleAttackerNotificationEvent</c> 0x0056C160, 11.4186 0x0056B420, a predicted row).</item>
/// <item>GE 0x01B2 DefenderNotification: String16L attacker name, u32 damage type, f64 percent, u32 damage, u32 damage
/// location, u32 critical, u64 attack conditions, then ACE's <c>Align</c> (a no-op: the body is already a multiple of 4)
/// (<c>GameEventDefenderNotification</c>, <c>Player_Combat.cs:542-546</c>;
/// <c>ClientCombatSystem::HandleDefenderNotificationEvent</c> 0x0056D670, 11.4186 0x0056C920, a predicted row).</item>
/// <item>GE 0x01B3 EvasionAttackerNotification: String16L defender name (<c>GameEventEvasionAttackerNotification</c>;
/// <c>HandleEvasionAttackerNotificationEvent</c> 0x0056D4F0, 11.4186 0x0056C7A0).</item>
/// <item>GE 0x01B4 EvasionDefenderNotification: String16L attacker name (<c>GameEventEvasionDefenderNotification</c>;
/// <c>HandleEvasionDefenderNotificationEvent</c> 0x0056D370, 11.4186 0x0056C620).</item>
/// <item>GM 0x019E PlayerKilled: String16L text, u32 victim, u32 killer (<c>GameMessagePlayerKilled</c>,
/// <c>Player_Death.cs</c>; by name <c>ClientCombatSystem::HandlePlayerDeathEvent</c> 0x0056D070, 11.4186 0x0056C320).</item>
/// </list>
/// </remarks>
internal static class CombatCodecs
{
    /// <summary>GE 0x01AC VictimNotification.</summary>
    public static bool TryParseVictim(ReadOnlySpan<byte> message, out VictimNotificationEvent result)
    {
        result = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        result = new VictimNotificationEvent(text);
        return true;
    }

    /// <summary>GE 0x01AD KillerNotification.</summary>
    public static bool TryParseKiller(ReadOnlySpan<byte> message, out KillerNotificationEvent result)
    {
        result = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        result = new KillerNotificationEvent(text);
        return true;
    }

    /// <summary>GE 0x01B3 EvasionAttackerNotification.</summary>
    public static bool TryParseEvasionAttacker(ReadOnlySpan<byte> message, out EvasionAttackerNotificationEvent result)
    {
        result = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        result = new EvasionAttackerNotificationEvent(text);
        return true;
    }

    /// <summary>GE 0x01B4 EvasionDefenderNotification.</summary>
    public static bool TryParseEvasionDefender(ReadOnlySpan<byte> message, out EvasionDefenderNotificationEvent result)
    {
        result = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        result = new EvasionDefenderNotificationEvent(text);
        return true;
    }

    /// <summary>GE 0x01B1 AttackerNotification.</summary>
    public static bool TryParseAttacker(ReadOnlySpan<byte> message, out AttackerNotificationEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadString16L(out var name)
            || !reader.TryReadU32(out var damageType)
            || !reader.TryReadF64(out var percent)
            || !reader.TryReadU32(out var damage)
            || !reader.TryReadU32(out var critical)
            || !reader.TryReadU64(out var conditions)
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new AttackerNotificationEvent(name, damageType, percent, damage, critical != 0, conditions);
        return true;
    }

    /// <summary>GE 0x01B2 DefenderNotification.</summary>
    public static bool TryParseDefender(ReadOnlySpan<byte> message, out DefenderNotificationEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadString16L(out var name)
            || !reader.TryReadU32(out var damageType)
            || !reader.TryReadF64(out var percent)
            || !reader.TryReadU32(out var damage)
            || !reader.TryReadU32(out var location)
            || !reader.TryReadU32(out var critical)
            || !reader.TryReadU64(out var conditions)
            || !reader.TryAlign()
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new DefenderNotificationEvent(name, damageType, percent, damage, location, critical != 0, conditions);
        return true;
    }

    /// <summary>GM 0x019E PlayerKilled.</summary>
    public static bool TryParsePlayerKilled(ReadOnlySpan<byte> message, out PlayerKilledMsg result)
    {
        result = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PlayerKilled)
            || !reader.TryReadString16L(out var text)
            || !reader.TryReadU32(out var victim)
            || !reader.TryReadU32(out var killer)
            || !reader.IsAtEnd)
        {
            return false;
        }

        result = new PlayerKilledMsg(text, victim, killer);
        return true;
    }

    private static bool TryReadText(ReadOnlySpan<byte> message, out string text)
    {
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        text = string.Empty;
        return reader.TryReadString16L(out text) && reader.IsAtEnd;
    }
}
