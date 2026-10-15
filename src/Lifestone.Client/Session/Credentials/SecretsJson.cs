using System.Text.Json;

namespace Lifestone.Client.Session.Credentials;

/// <summary>
/// The JSON reading rules shared by <see cref="PlayAccounts"/> and <see cref="BackendReadiness"/>: the files are read
/// by the host as bytes, may start with a UTF-8 byte order mark (some editors write one), and may hold comments and
/// trailing commas. A parse error is reported by line only: the reader's own message can quote the offending
/// character, which must never reach a log from a file that holds passwords.
/// </summary>
internal static class SecretsJson
{
    /// <summary>The reader options for the backend's JSON files.</summary>
    internal static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>The UTF-8 byte order mark (U+FEFF).</summary>
    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary><paramref name="json"/> without a leading UTF-8 byte order mark.</summary>
    internal static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> json) =>
        json.StartsWith(Utf8ByteOrderMark) ? json[Utf8ByteOrderMark.Length..] : json;

    /// <summary>True when <paramref name="json"/> holds nothing but a byte order mark and white space (a missing file reads as empty).</summary>
    internal static bool IsBlank(ReadOnlySpan<byte> json) => WithoutByteOrderMark(json).Trim(" \t\r\n"u8).IsEmpty;

    /// <summary>A reader over <paramref name="json"/> (byte order mark skipped) with <see cref="ReaderOptions"/>.</summary>
    internal static Utf8JsonReader Reader(ReadOnlySpan<byte> json) => new(WithoutByteOrderMark(json), ReaderOptions);

    /// <summary>
    /// A parse error of <paramref name="file"/>, by line and byte only (never the reader's message, which can quote the
    /// file's content).
    /// </summary>
    internal static string NotValidJson(string file, JsonException e) =>
        e.LineNumber is { } line
            ? $"{file} is not valid JSON (line {line + 1}, byte {(e.BytePositionInLine ?? 0) + 1})"
            : $"{file} is not valid JSON";
}
