using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The game events M2 decodes (m2-plan §3.3 N7), each the payload of an F7B0 envelope (<see cref="GameEventEnvelope"/>),
/// laid out as ACE's <c>GameEvent/Events/*</c> writers lay them out. Every parser takes the whole F7B0 message (so
/// String16L padding is counted as on the wire) and accepts it only when every byte of the payload was read. Lifestone's
/// own: OpenAC's <c>GameEvents.cs</c> was not taken.
/// </summary>
/// <remarks>
/// <para>The layouts, and where retail reads them:</para>
/// <list type="bullet">
/// <item>0x02BD Tell: text, sender name, u32 sender, u32 target, u32 chat type, u32 trailing word ACE writes as 0
/// (<c>CM_Communication::DispatchUI_HearDirectSpeech</c>, 11.4186 0x006A49C0, 11.6096 0x006A5920, reads all four
/// words);</item>
/// <item>0x0004 PopupString: the text (<c>CM_Communication::DispatchUI_PopUpString</c>, 11.4186 0x006A4EA0, 11.6096
/// 0x006A5E00);</item>
/// <item>0x028A WeenieError: u32 code (<c>CM_Communication::DispatchUI_WeenieError</c>, 11.4186 0x006A3AA0, 11.6096
/// 0x006A48C0); 0x028B WeenieErrorWithString: u32 code, then the text
/// (<c>CM_Communication::DispatchUI_WeenieErrorWithString</c>, 11.4186 0x006A5070, 11.6096 0x006A5FD0);</item>
/// <item>0x02EB CommunicationTransientString: the text (<c>CM_Communication::DispatchUI_TransientString</c>, 11.4186
/// 0x006A4FD0, 11.6096 0x006A5F30);</item>
/// <item>0x01C7 UseDone: u32 WeenieError, 0 for success (<c>CM_Item::DispatchUI_UseDone</c>, 11.4186 0x006A8510, 11.6096
/// 0x006A9470);</item>
/// <item>0x01C0 UpdateHealth: u32 GUID, f32 health fraction (<c>CM_Combat::DispatchUI_QueryHealthResponse</c>, 11.4186
/// 0x006A99A0, 11.6096 0x006AA900).</item>
/// </list>
/// </remarks>
internal static class GameEventCodecs
{
    /// <summary>0x02BD Tell.</summary>
    public static bool TryParseTell(ReadOnlySpan<byte> message, out TellEvent tell)
    {
        tell = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadString16L(out var text)
            || !reader.TryReadString16L(out var sender)
            || !reader.TryReadU32(out var senderGuid)
            || !reader.TryReadU32(out var targetGuid)
            || !reader.TryReadU32(out var chatType)
            || !reader.TryReadU32(out var trailing)
            || !reader.IsAtEnd)
        {
            return false;
        }

        tell = new TellEvent(text, sender, senderGuid, targetGuid, chatType, trailing);
        return true;
    }

    /// <summary>0x0004 PopupString.</summary>
    public static bool TryParsePopup(ReadOnlySpan<byte> message, out PopupEvent popup)
    {
        popup = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        popup = new PopupEvent(text);
        return true;
    }

    /// <summary>0x028A WeenieError (<paramref name="withString"/> false) or 0x028B WeenieErrorWithString (true).</summary>
    public static bool TryParseWeenieError(ReadOnlySpan<byte> message, bool withString, out WeenieErrorEvent error)
    {
        error = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var code))
        {
            return false;
        }

        string? text = null;
        if (withString)
        {
            if (!reader.TryReadString16L(out var read))
            {
                return false;
            }

            text = read;
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        error = new WeenieErrorEvent(code, text);
        return true;
    }

    /// <summary>0x02EB CommunicationTransientString.</summary>
    public static bool TryParseTransientString(ReadOnlySpan<byte> message, out TransientStringEvent transient)
    {
        transient = default;
        if (!TryReadText(message, out var text))
        {
            return false;
        }

        transient = new TransientStringEvent(text);
        return true;
    }

    /// <summary>0x01C7 UseDone.</summary>
    public static bool TryParseUseDone(ReadOnlySpan<byte> message, out UseDoneEvent useDone)
    {
        useDone = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var code) || !reader.IsAtEnd)
        {
            return false;
        }

        useDone = new UseDoneEvent(code);
        return true;
    }

    /// <summary>0x01C0 UpdateHealth.</summary>
    public static bool TryParseUpdateHealth(ReadOnlySpan<byte> message, out UpdateHealthEvent health)
    {
        health = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var guid) || !reader.TryReadF32(out var fraction) || !reader.IsAtEnd)
        {
            return false;
        }

        health = new UpdateHealthEvent(guid, fraction);
        return true;
    }

    /// <summary>A payload that is one String16L and nothing else.</summary>
    private static bool TryReadText(ReadOnlySpan<byte> message, out string text)
    {
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        return reader.TryReadString16L(out text) && reader.IsAtEnd;
    }
}
