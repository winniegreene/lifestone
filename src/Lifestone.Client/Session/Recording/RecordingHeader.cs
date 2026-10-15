using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Lifestone.Data.Dat;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// The JSON header of a <c>.lsrec</c> recording: what produced it and with which data, so a replay can tell whether it
/// still applies. It never holds an account, a user, a machine or a path: every field is a fixed token, a version, a
/// commit or a number, and the writer refuses free text (<see cref="Validate"/>).
/// </summary>
public sealed record RecordingHeader
{
    /// <summary>The <c>format</c> value.</summary>
    public const string FormatName = "lsrec";

    /// <summary>The retail client version string ACE expects.</summary>
    public const string RetailClientVersion = "1802";

    /// <summary>Who recorded: a short token such as <c>headless</c>, <c>godot</c> or <c>test</c> (lower-case letters, digits, dashes).</summary>
    public required string Runner { get; init; }

    /// <summary>When the recording started (from the session's <see cref="TimeProvider"/>), UTC.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Lifestone's version (letters, digits, <c>.</c>, <c>+</c>, <c>-</c>), or null.</summary>
    public string? LifestoneVersion { get; init; }

    /// <summary>The client version the session reported (ACE's is <see cref="RetailClientVersion"/>).</summary>
    public string ClientVersion { get; init; } = RetailClientVersion;

    /// <summary>The ACE commit the backend is pinned to (hexadecimal, from server/versions.json), or null.</summary>
    public string? AceCommit { get; init; }

    /// <summary>The dats' iterations (the data check's), or null.</summary>
    public DatIterations? Iterations { get; init; }

    /// <summary>The scenario that ran (a token, e.g. <c>login</c>), or null.</summary>
    public string? Scenario { get; init; }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> unless every field is of its restricted shape, so no free text (a name, a
    /// path) can enter a recording through the header.
    /// </summary>
    public void Validate()
    {
        RequireToken(Runner, nameof(Runner));
        if (Scenario is not null)
        {
            RequireToken(Scenario, nameof(Scenario));
        }

        if (LifestoneVersion is not null && !IsShaped(LifestoneVersion, 64, c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-'))
        {
            throw new ArgumentException("the Lifestone version must be 1 to 64 letters, digits, '.', '+' or '-'", nameof(LifestoneVersion));
        }

        if (!IsShaped(ClientVersion, 16, char.IsAsciiDigit))
        {
            throw new ArgumentException("the client version must be 1 to 16 digits", nameof(ClientVersion));
        }

        if (AceCommit is not null && (AceCommit.Length < 7 || !IsShaped(AceCommit, 40, char.IsAsciiHexDigitLower)))
        {
            throw new ArgumentException("the ACE commit must be 7 to 40 lower-case hexadecimal digits", nameof(AceCommit));
        }
    }

    /// <summary>The header as UTF-8 JSON (validated first).</summary>
    public byte[] ToJson()
    {
        Validate();
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("formatVersion", RecordingFormat.Version);
            json.WriteString("runner", Runner);
            json.WriteString("createdUtc", CreatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            if (LifestoneVersion is not null)
            {
                json.WriteString("lifestoneVersion", LifestoneVersion);
            }

            json.WriteString("clientVersion", ClientVersion);
            if (AceCommit is not null)
            {
                json.WriteString("aceCommit", AceCommit);
            }

            if (Iterations is { } iterations)
            {
                json.WriteStartObject("iterations");
                json.WriteNumber("portal", iterations.Portal);
                json.WriteNumber("cell", iterations.Cell);
                WriteOptional(json, "local", iterations.Local);
                WriteOptional(json, "highRes", iterations.HighRes);
                json.WriteEndObject();
            }

            if (Scenario is not null)
            {
                json.WriteString("scenario", Scenario);
            }

            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads a header written by <see cref="ToJson"/>.</summary>
    /// <exception cref="InvalidDataException">The JSON is not a version-1 header.</exception>
    public static RecordingHeader FromJson(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != FormatName)
            {
                throw new InvalidDataException("the recording header is not an lsrec header");
            }

            var header = new RecordingHeader
            {
                Runner = root.GetProperty("runner").GetString() ?? string.Empty,
                CreatedUtc = DateTimeOffset.Parse(root.GetProperty("createdUtc").GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                LifestoneVersion = OptionalString(root, "lifestoneVersion"),
                ClientVersion = OptionalString(root, "clientVersion") ?? RetailClientVersion,
                AceCommit = OptionalString(root, "aceCommit"),
                Iterations = root.TryGetProperty("iterations", out var it) && it.ValueKind == JsonValueKind.Object
                    ? new DatIterations(it.GetProperty("portal").GetInt32(), it.GetProperty("cell").GetInt32(), OptionalInt(it, "local"), OptionalInt(it, "highRes"))
                    : null,
                Scenario = OptionalString(root, "scenario"),
            };
            header.Validate();
            return header;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidDataException($"the recording header is malformed ({e.GetType().Name})", e);
        }
    }

    /// <summary>Throws unless <paramref name="value"/> is 1 to 32 lower-case letters, digits or dashes.</summary>
    internal static void RequireToken(string? value, string name)
    {
        if (value is null || !IsShaped(value, 32, c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new ArgumentException($"{name} must be 1 to 32 lower-case letters, digits or dashes", name);
        }
    }

    private static bool IsShaped(string? value, int maxLength, Func<char, bool> allowed) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && value.All(allowed);

    private static void WriteOptional(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } v)
        {
            json.WriteNumber(name, v);
        }
        else
        {
            json.WriteNull(name);
        }
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
