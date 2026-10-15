namespace Lifestone.Data.Ui;

/// <summary>
/// Retail's string hash: the id a StringTable files a string under is the hash of its token (<c>Tokenize("ID_...")</c>),
/// and a StringInfo variable's id is the hash of its name.
/// </summary>
/// <remarks>
/// Re-expresses <c>compute_str_hash</c> (11.4186 0x00413110, 11.6096 0x004016B0; ENGINE/dataerror/DataError.c) and
/// <c>Tokenize</c> (11.4186 0x00401A10, 11.6096 0x00401A20; GAME/acclient/gmClient.c), by function name. It is the
/// PJW/ELF hash over the bytes of a C string: shift left four, add the byte, and fold the top nibble back into bits 4-7
/// before clearing it. Retail adds each byte as a <b>signed</b> <c>char</c>, so a byte of 0x80 or above subtracts;
/// the tokens are ASCII, where that never matters. The result is at most 28 bits, so retail's last step (a result of
/// 0xFFFFFFFF becomes 0xFFFFFFFE) never fires; it is kept for fidelity. OpenAC's <c>RetailStringHash</c> (MIT) was read
/// as a cross-check only; it adds bytes unsigned. Allocation-free.
/// </remarks>
public static class StringHash
{
    /// <summary><c>compute_str_hash</c> over raw bytes (no terminator).</summary>
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var result = 0u;
        if (bytes.IsEmpty)
        {
            return result;
        }

        foreach (var b in bytes)
        {
            result = unchecked((uint)(sbyte)b + (result << 4));
            if ((result & 0xF0000000u) != 0)
            {
                result = (result ^ ((result & 0xF0000000u) >> 24)) & 0x0FFFFFFFu;
            }
        }

        return result == uint.MaxValue ? uint.MaxValue - 1 : result;
    }

    /// <summary>
    /// <c>compute_str_hash</c> over a token's characters, each one byte (retail hashes a <c>char</c> string). Allocation-free.
    /// </summary>
    /// <exception cref="ArgumentException">A character is above U+00FF, which a retail <c>char</c> string cannot hold.</exception>
    public static uint Compute(ReadOnlySpan<char> token)
    {
        var result = 0u;
        foreach (var c in token)
        {
            if (c > 'ÿ')
            {
                throw new ArgumentException($"Retail tokens are byte strings; U+{(int)c:X4} does not fit in one byte.", nameof(token));
            }

            result = unchecked((uint)(sbyte)(byte)c + (result << 4));
            if ((result & 0xF0000000u) != 0)
            {
                result = (result ^ ((result & 0xF0000000u) >> 24)) & 0x0FFFFFFFu;
            }
        }

        return result == uint.MaxValue ? uint.MaxValue - 1 : result;
    }

    /// <summary><c>Tokenize</c>: the hash of <paramref name="token"/>, or 0 for null.</summary>
    public static uint Tokenize(string? token) => token is null ? 0 : Compute(token.AsSpan());
}
