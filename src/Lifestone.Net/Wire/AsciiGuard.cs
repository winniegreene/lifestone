using System.Text;

namespace Lifestone.Net.Wire;

/// <summary>
/// The ASCII rule for client strings that other fields follow (m2-plan §3.3 N2). ACE reads a client String16L with
/// <c>BinaryReader.ReadChars(length)</c> over UTF-8 (<c>ACE.Common/Extensions/BinaryReaderExtensions.cs</c>
/// <c>ReadString16L</c>), counting characters, not bytes. A CP1252 byte of 0x80 or above can start a multi-byte UTF-8
/// sequence that swallows the bytes after it, so every later field of the message would be read from the wrong offset.
/// Only ASCII round-trips byte for byte.
/// </summary>
/// <remarks>
/// The fields it guards: the LoginRequest's client version, account and password, the CharacterCreate name, and the Tell
/// target and text. A message's last string field (Talk's text) may carry CP1252, because nothing follows it. The
/// password is not followed by another field, but ACE decodes it the same way, so two different non-ASCII passwords can
/// decode to the same string; it is refused too, loudly, rather than weakened silently.
/// </remarks>
internal static class AsciiGuard
{
    /// <summary>True when every character is 0x00-0x7F.</summary>
    public static bool IsAscii(ReadOnlySpan<char> text) => Ascii.IsValid(text);

    /// <summary>Throws unless <paramref name="text"/> is ASCII.</summary>
    /// <exception cref="ArgumentException">A character is above 0x7F; the message names <paramref name="field"/> and
    /// the index, never the text (the field may be a password).</exception>
    public static void Require(ReadOnlySpan<char> text, string field)
    {
        if (Ascii.IsValid(text))
        {
            return;
        }

        var index = 0;
        while (index < text.Length && text[index] <= '\x7F')
        {
            index++;
        }

        throw new ArgumentException(
            $"{field} has a non-ASCII character at index {index}. ACE decodes this field as UTF-8 and counts characters, "
            + "so only ASCII keeps the fields after it in place (docs/protocol.md, Conventions).",
            field);
    }
}
