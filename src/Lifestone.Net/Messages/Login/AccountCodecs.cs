using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F7DC AccountBoot (docs/protocol.md §5.2): an optional String16L reason; an empty body means none. ACE writes the
/// reason only when it has one (<c>GameMessageBootAccount</c>), usually starting with a space (" because ...").
/// </summary>
/// <remarks>
/// Retail reads it in <c>CPlayerSystem::Handle_AccountBooted</c> (11.4186 0x00562A70, 11.6096 0x00563810), which shows
/// "You have been booted from Asheron's Call" followed by the reason, or by " for Code of Conduct Violations" when the
/// reason is absent or empty.
/// </remarks>
internal static class AccountBootCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole F7DC.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out AccountBootMsg boot)
    {
        boot = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.AccountBoot))
        {
            return false;
        }

        if (reader.IsAtEnd)
        {
            boot = new AccountBootMsg(null);
            return true;
        }

        if (!reader.TryReadString16L(out var reason) || !reader.IsAtEnd)
        {
            return false;
        }

        boot = new AccountBootMsg(reason);
        return true;
    }
}

/// <summary>
/// F7C1 AccountBanned: u32 seconds until the ban expires, then an optional String16L reason (ACE
/// <c>GameMessageAccountBanned</c>, which writes the reason as " - reason").
/// </summary>
/// <remarks>
/// Retail reads it in <c>CPlayerSystem::Handle_AccountBanned</c> (11.4186 0x00562BD0, 11.6096 0x00563970). It takes the
/// seconds as a signed value: above 0 is a timed ban ("You have been banned until ..."), anything else a permanent one
/// ("You have been banned from Asheron's Call"); either text ends with the reason.
/// </remarks>
internal static class AccountBannedCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F7C1.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out AccountBannedMsg banned)
    {
        banned = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.AccountBanned) || !reader.TryReadU32(out var seconds))
        {
            return false;
        }

        if (reader.IsAtEnd)
        {
            banned = new AccountBannedMsg(seconds, null);
            return true;
        }

        if (!reader.TryReadString16L(out var reason) || !reader.IsAtEnd)
        {
            return false;
        }

        banned = new AccountBannedMsg(seconds, reason);
        return true;
    }
}

/// <summary>F746 PlayerCreate: the player's own GUID, sent on entering the world (ACE <c>GameMessagePlayerCreate</c>).</summary>
internal static class PlayerCreateCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole F746.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out PlayerCreateMsg playerCreate)
    {
        playerCreate = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PlayerCreate) || !reader.TryReadU32(out var guid) || !reader.IsAtEnd)
        {
            return false;
        }

        playerCreate = new PlayerCreateMsg(guid);
        return true;
    }
}
