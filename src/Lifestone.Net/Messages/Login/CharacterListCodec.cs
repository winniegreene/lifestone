// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterList.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterListCodec; Parse (which threw FormatException) became TryParse over WireReader, which never throws
// and requires every byte to be read; the result is the N0 record CharacterListMsg with every word kept raw (the status
// and the two flags are not turned into int and bools); TrySelectFirstAvailable and IsAvailableActiveIdentity are not
// taken (character choice belongs to the state track).

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F658 CharacterList (docs/protocol.md §5.2, §10 vector): u32 status; the active list; the deleted list; u32 slot
/// count; String16L account; u32 use-TurbineChat; u32 has-Throne-of-Destiny. Each list is a u32 count of
/// {u32 GUID, String16L name, u32 seconds greyed out}.
/// </summary>
/// <remarks>
/// Retail reads it in <c>CharacterSet::UnPack</c> (11.4186 0x004FE340, 11.6096 0x004FEDC0), each entry in
/// <c>CharacterIdentity::UnPack</c> (11.4186 0x004FE880, 11.6096 0x004FF300), for
/// <c>CPlayerSystem::Handle_Login__CharacterSet</c> (11.4186 0x0055F6D0, 11.6096 0x00560440). The deleted list has the
/// active list's layout there, so it is decoded, not refused; ACE always sends it empty
/// (<c>GameMessageCharacterList</c>). The account string is kept exactly: the character requests must echo it.
/// </remarks>
internal static class CharacterListCodec
{
    /// <summary>The smallest entry: a GUID, an empty String16L (2 + 2 pad) and the greyed-out word.</summary>
    private const int MinimumEntrySize = sizeof(uint) + 4 + sizeof(uint);

    /// <summary>Decodes <paramref name="message"/>; false (and null) when it is not a whole, well-formed F658.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, [NotNullWhen(true)] out CharacterListMsg? list)
    {
        list = null;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.CharacterList)
            || !reader.TryReadU32(out var status)
            || !TryReadEntries(ref reader, out var characters)
            || !TryReadEntries(ref reader, out var deleted)
            || !reader.TryReadU32(out var slotCount)
            || !reader.TryReadString16L(out var account)
            || !reader.TryReadU32(out var useTurbineChat)
            || !reader.TryReadU32(out var hasThroneOfDestiny)
            || !reader.IsAtEnd)
        {
            return false;
        }

        list = new CharacterListMsg(status, characters, (uint)deleted.Length, slotCount, account, useTurbineChat, hasThroneOfDestiny)
        {
            DeletedCharacters = deleted,
        };
        return true;
    }

    /// <summary>Reads one list: a u32 count (checked against the bytes left before anything is allocated), then the entries.</summary>
    private static bool TryReadEntries(ref WireReader reader, out ImmutableArray<CharacterListEntry> entries)
    {
        entries = [];
        if (!reader.TryReadCount(MinimumEntrySize, out var count))
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        var builder = ImmutableArray.CreateBuilder<CharacterListEntry>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU32(out var guid) || !reader.TryReadString16L(out var name) || !reader.TryReadU32(out var greyedOut))
            {
                return false;
            }

            builder.Add(new CharacterListEntry(guid, name, greyedOut));
        }

        entries = builder.MoveToImmutable();
        return true;
    }
}
