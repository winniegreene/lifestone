// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharacterError.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharacterErrorCodec; Parse (which threw) became TryParse over WireReader, which never throws and requires every
// byte to be read; the code enum is not taken (Lifestone's is CharacterErrorCode, from ACE) and the code stays raw.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F659 CharacterError (docs/protocol.md §5.2): one u32 code, kept raw. Retail hands it to
/// <c>CPlayerSystem::Handle_CharacterError</c> (11.4186 0x0055D5D0, 11.6096 0x0055E2F0), which clears the ready-to-enter
/// and awaiting-logon flags and posts the error notice.
/// </summary>
internal static class CharacterErrorCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole F659.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out CharacterErrorMsg error)
    {
        error = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.CharacterError) || !reader.TryReadU32(out var code) || !reader.IsAtEnd)
        {
            return false;
        }

        error = new CharacterErrorMsg(code);
        return true;
    }
}
