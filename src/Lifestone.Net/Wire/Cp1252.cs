using System.Text;

namespace Lifestone.Net.Wire;

/// <summary>
/// Windows-1252, the code page of the protocol's strings (docs/protocol.md "Conventions": a String16L's bytes are
/// CP1252; ACE writes them with <c>Encoding.GetEncoding(1252)</c>, <c>ACE.Server/Network/Extensions.cs</c>). Taken from
/// <see cref="CodePagesEncodingProvider"/> directly, so nothing registers a provider process-wide and no package is needed.
/// </summary>
/// <remarks>
/// Encoding is strict: a character CP1252 cannot represent throws (an <see cref="ArgumentException"/>, never a silent
/// <c>?</c>), so a caller learns that its text cannot go on the wire. Decoding never fails: .NET maps every byte,
/// including the five CP1252 leaves undefined (0x81, 0x8D, 0x8F, 0x90, 0x9D), to a character.
/// </remarks>
internal static class Cp1252
{
    /// <summary>The Windows code page number.</summary>
    public const int CodePage = 1252;

    /// <summary>The encoding: exception fallback for encoding, replacement fallback for decoding.</summary>
    public static Encoding Encoding { get; } =
        CodePagesEncodingProvider.Instance.GetEncoding(CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback)
        ?? throw new InvalidOperationException("the runtime's CodePagesEncodingProvider has no code page 1252");

    /// <summary>Decodes CP1252 bytes; an empty span gives <see cref="string.Empty"/> without allocating.</summary>
    public static string GetString(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : Encoding.GetString(bytes);

    /// <summary>
    /// The byte count of <paramref name="text"/> in CP1252, which is its character count when every character is
    /// representable.
    /// </summary>
    /// <exception cref="ArgumentException">A character of <paramref name="text"/> has no CP1252 byte; the message names
    /// <paramref name="field"/> and the index, never the text.</exception>
    public static int GetByteCount(ReadOnlySpan<char> text, string field)
    {
        try
        {
            return Encoding.GetByteCount(text);
        }
        catch (EncoderFallbackException e)
        {
            throw Unrepresentable(field, e);
        }
    }

    /// <summary>Encodes <paramref name="text"/> into <paramref name="destination"/> and returns the bytes written.</summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte (named by index, never by value), or
    /// <paramref name="destination"/> is too short.</exception>
    public static int GetBytes(ReadOnlySpan<char> text, Span<byte> destination, string field)
    {
        try
        {
            return Encoding.GetBytes(text, destination);
        }
        catch (EncoderFallbackException e)
        {
            throw Unrepresentable(field, e);
        }
    }

    private static ArgumentException Unrepresentable(string field, EncoderFallbackException e) =>
        new($"{field} has a character at index {e.Index} that Windows-1252 cannot represent, so it cannot go on the wire.", field);
}
