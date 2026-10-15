// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CharGenVerificationResponse.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Messages; internal;
// renamed CharGenResponseCodec; Parse (which threw) became TryParse over WireReader, which never throws and requires
// every byte to be read; the result is the N0 record CharGenResponseMsg (its result enum is Lifestone's CharGenResult).

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// F643 CharacterCreateResponse (docs/protocol.md §5.2): u32 result; when the result is 1 (Ok), the new or restored
/// character's identity follows: u32 GUID, String16L name, u32 seconds greyed out. The same message answers F656
/// CharacterCreate and F7D9 CharacterRestore (ACE <c>GameMessageCharacterCreateResponse</c> and
/// <c>GameMessageCharacterRestore</c>); the session correlates them.
/// </summary>
/// <remarks>
/// Retail reads it in <c>CPlayerSystem::Handle_CharGenVerificationResponse</c> (11.4186 0x0055E8B0, 11.6096
/// 0x0055F620): only code 1 carries the identity (<c>CharacterIdentity::UnPack</c>); every other code is the word alone.
/// </remarks>
internal static class CharGenResponseCodec
{
    /// <summary>Decodes <paramref name="message"/>; false when it is not a whole, well-formed F643.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out CharGenResponseMsg response)
    {
        response = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.CharacterCreateResponse) || !reader.TryReadU32(out var result))
        {
            return false;
        }

        if (result != (uint)CharGenResult.Ok)
        {
            if (!reader.IsAtEnd)
            {
                return false;
            }

            response = new CharGenResponseMsg((CharGenResult)result, 0, null, 0);
            return true;
        }

        if (!reader.TryReadU32(out var guid)
            || !reader.TryReadString16L(out var name)
            || !reader.TryReadU32(out var trailing)
            || !reader.IsAtEnd)
        {
            return false;
        }

        response = new CharGenResponseMsg(CharGenResult.Ok, guid, name, trailing);
        return true;
    }
}
