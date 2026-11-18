using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>The four classes of order (the steer contract, section 6).</summary>
public enum SteerClass
{
    /// <summary>Reads something; no effect on the game; answered in a frame.</summary>
    Probe,

    /// <summary>Starts or stops the dial.</summary>
    Control,

    /// <summary>A standing rule, in force at the next seat.</summary>
    Policy,

    /// <summary>One piece of work, one at a time.</summary>
    Job,
}

/// <summary>Every order kind the steer contract names, and which are built. A kind that is not built yet is refused by name (<c>not-built</c>), never ignored.</summary>
public static class SteerKinds
{
    /// <summary>The desk's liveness probe.</summary>
    public const string Ping = "ping";

    /// <summary>One section of the character sheet, written to <c>sheets\&lt;section&gt;.json</c>.</summary>
    public const string Sheet = "sheet";

    /// <summary>A screenshot, written to <c>shots\&lt;name&gt;.png</c>, and what it cost the game thread.</summary>
    public const string Capture = "capture";

    /// <summary>Starts the dial from off or done.</summary>
    public const string Start = "start";

    /// <summary>Hands the controls back after an order stopped the dial.</summary>
    public const string Resume = "resume";

    /// <summary>A clean stop that recalls home; the game stays open.</summary>
    public const string StopHome = "stop_home";

    /// <summary>Stops where it stands, with no recall.</summary>
    public const string StopHere = "stop_here";

    /// <summary>Recalls home if running, logs off and quits: the way to yield to a gate.</summary>
    public const string Leave = "leave";

    /// <summary>Takes a waiting order off the desk.</summary>
    public const string Cancel = "cancel";

    /// <summary>One logistics trip (the command registry's <c>trip</c>).</summary>
    public const string Trip = "trip";

    /// <summary>One quest by its <c>key</c> (the command registry's <c>quest &lt;key&gt;</c>).</summary>
    public const string RunQuest = "run_quest";

    /// <summary>Any command of the registry by its <c>line</c>, as the chat types it (<c>Commands/CommandRegistry.cs</c>; 2026-11-17).</summary>
    public const string Command = "command";

    /// <summary>Every kind and its class (29: the contract's section 6, and <see cref="Command"/>).</summary>
    public static IReadOnlyDictionary<string, SteerClass> All { get; } = new Dictionary<string, SteerClass>(StringComparer.Ordinal)
    {
        [Ping] = SteerClass.Probe, [Sheet] = SteerClass.Probe, [Capture] = SteerClass.Probe,
        [Start] = SteerClass.Control, [Resume] = SteerClass.Control, [StopHome] = SteerClass.Control, [StopHere] = SteerClass.Control, [Leave] = SteerClass.Control, [Cancel] = SteerClass.Control,
        ["hold_rung"] = SteerClass.Policy, ["release_rung"] = SteerClass.Policy, ["use_ground"] = SteerClass.Policy, ["release_ground"] = SteerClass.Policy, ["deny_spell"] = SteerClass.Policy, ["allow_spell"] = SteerClass.Policy,
        ["learn_spell"] = SteerClass.Policy, ["set_stock"] = SteerClass.Policy, ["set_opener"] = SteerClass.Policy, ["raise_plan"] = SteerClass.Policy, ["set_tuning"] = SteerClass.Policy, ["protect_item"] = SteerClass.Policy,
        [Trip] = SteerClass.Job, ["errand"] = SteerClass.Job, [RunQuest] = SteerClass.Job, ["buy_item"] = SteerClass.Job, ["equip"] = SteerClass.Job, ["save_set"] = SteerClass.Job, ["wear_set"] = SteerClass.Job,
        [Command] = SteerClass.Job,
    };

    /// <summary>True for a kind this build answers.</summary>
    /// <param name="kind">The kind.</param>
    public static bool IsBuilt(string kind) => kind is Ping or Sheet or Capture or Start or Resume or StopHome or StopHere or Leave or Cancel or Trip or RunQuest or Command;

    /// <summary>True for a kind the command registry answers (<c>--autopilot-steer-orders=on</c>).</summary>
    /// <param name="kind">The kind.</param>
    public static bool IsCommand(string kind) => kind is Trip or RunQuest or Command;
}

/// <summary>An order's guard: what must still be true when the desk reaches it, or it is refused by name.</summary>
/// <param name="CharacterGuid">The character the order was written for (required on everything except <c>ping</c>).</param>
/// <param name="LevelMin">The lowest level it is meant for; null: any.</param>
/// <param name="LevelMax">The highest level it is meant for; null: any.</param>
/// <param name="Ground">The ground it is meant for; null: any.</param>
public sealed record SteerGuard(uint? CharacterGuid, int? LevelMin, int? LevelMax, string? Ground);

/// <summary>What <see cref="SteerOrder.Parse"/> made of a file: an order, or a refusal's name with whatever could be read of it.</summary>
/// <param name="Order">The order; null when refused.</param>
/// <param name="Refusal">The refusal's name; null when parsed.</param>
/// <param name="Detail">The reason in words.</param>
/// <param name="Kind">The order's kind when it could be read.</param>
public readonly record struct SteerParse(SteerOrder? Order, string? Refusal, string Detail, string? Kind);

/// <summary>
/// One order, as the harness wrote it (the steer contract, section 3). Parsed strictly: an unknown schema, a bad field or a kind the contract does not name is refused by name. Its text is data: <see cref="Why"/> is cleaned for the log and is never sent to
/// the server or shown in game chat.
/// </summary>
/// <param name="Id">Unique in a run; also a file name.</param>
/// <param name="Kind">One of <see cref="SteerKinds.All"/>.</param>
/// <param name="Args">The kind's arguments (an object; cloned, so safe to hand between threads).</param>
/// <param name="By">Who wrote it (a token).</param>
/// <param name="Why">Why, for the log.</param>
/// <param name="IssuedUtc">When it was written.</param>
/// <param name="ExpiresSeconds">How long after <paramref name="IssuedUtc"/> it may still act.</param>
/// <param name="Guard">What must still be true.</param>
/// <param name="DryRun">Run every check, act on nothing, answer <c>checked</c>.</param>
public sealed partial record SteerOrder(string Id, string Kind, JsonElement Args, string By, string Why, DateTimeOffset IssuedUtc, int ExpiresSeconds, SteerGuard Guard, bool DryRun)
{
    /// <summary>The one envelope version this build reads.</summary>
    public const int Version = 1;

    /// <summary>The shortest life an order may ask for, in seconds.</summary>
    public const int MinExpiresSeconds = 5;

    /// <summary>The longest life an order may ask for, in seconds (a day).</summary>
    public const int MaxExpiresSeconds = 86_400;

    /// <summary>The most characters of <see cref="Why"/> kept.</summary>
    public const int MaxWhyLength = 300;

    /// <summary>The moment the order stops being able to act.</summary>
    public DateTimeOffset ExpiresUtc => IssuedUtc.AddSeconds(ExpiresSeconds);

    /// <summary>A string argument, or null when absent or not a string.</summary>
    /// <param name="name">The argument's name.</param>
    public string? ArgText(string name) => Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>True when <paramref name="id"/> can name an order (and so a file: no separators, no device name).</summary>
    /// <param name="id">The id.</param>
    public static bool IsId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Reads one order from its file's bytes.</summary>
    /// <param name="utf8">The file's bytes.</param>
    public static SteerParse Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            _ = StrictUtf8.GetCharCount(utf8);
        }
        catch (DecoderFallbackException)
        {
            return Refused("bad-encoding", "the file is not valid UTF-8");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8);
        }
        catch (JsonException e)
        {
            return Refused("bad-json", "the file is not one JSON document: " + e.Message);
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static SteerParse Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Refused("bad-json", "the document is not an object");
        }

        if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != Version)
        {
            return Refused("bad-version", $"this build reads version {Version}");
        }

        var kind = Text(root, "kind");
        if (!IsId(Text(root, "id")))
        {
            return Refused("bad-id", "id must be 1 to 64 letters, digits, '.', '_' or '-', and not a device name", kind);
        }

        if (kind is null || !SteerKinds.All.ContainsKey(kind))
        {
            return Refused("unknown-kind", $"'{kind}' is not an order kind", kind);
        }

        var by = Text(root, "by");
        if (by is null || !TokenPattern().IsMatch(by))
        {
            return Refused("bad-by", "by must be a short token", kind);
        }

        var args = default(JsonElement);
        if (root.TryGetProperty("args", out var a))
        {
            if (a.ValueKind != JsonValueKind.Object)
            {
                return Refused("bad-args", "args must be an object", kind);
            }

            args = a.Clone();
        }

        if (Text(root, "issued_utc") is not { } issuedText || !DateTimeOffset.TryParse(issuedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issued))
        {
            return Refused("bad-issued", "issued_utc must be an ISO 8601 time", kind);
        }

        if (!root.TryGetProperty("expires_s", out var e) || e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var expires) || expires is < MinExpiresSeconds or > MaxExpiresSeconds)
        {
            return Refused("bad-expiry", $"expires_s must be {MinExpiresSeconds} to {MaxExpiresSeconds} seconds", kind);
        }

        var dry = false;
        if (root.TryGetProperty("dry_run", out var d))
        {
            if (d.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Refused("bad-dry-run", "dry_run must be true or false", kind);
            }

            dry = d.GetBoolean();
        }

        if (ReadGuard(root, kind, out var guard) is { } bad)
        {
            return Refused("bad-guard", bad, kind);
        }

        var order = new SteerOrder(Text(root, "id")!, kind, args, by, CleanWhy(Text(root, "why")), issued, expires, guard, dry);
        return new SteerParse(order, null, string.Empty, kind);
    }

    private static string? ReadGuard(JsonElement root, string kind, out SteerGuard guard)
    {
        guard = new SteerGuard(null, null, null, null);
        if (!root.TryGetProperty("guard", out var g))
        {
            return kind == SteerKinds.Ping ? null : "guard.character_guid is required on every order except ping";
        }

        if (g.ValueKind != JsonValueKind.Object)
        {
            return "guard must be an object";
        }

        uint? character = null;
        if (Text(g, "character_guid") is { } text)
        {
            if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            {
                return "guard.character_guid must be hex, 0x50000A12";
            }

            character = value;
        }
        else if (kind != SteerKinds.Ping)
        {
            return "guard.character_guid is required on every order except ping";
        }

        if (OptionalInt(g, "level_min", out var min) is { } badMin)
        {
            return badMin;
        }

        if (OptionalInt(g, "level_max", out var max) is { } badMax)
        {
            return badMax;
        }

        string? ground = null;
        if (g.TryGetProperty("ground", out var groundElement))
        {
            ground = groundElement.ValueKind == JsonValueKind.String ? groundElement.GetString() : null;
            if (ground is null)
            {
                return "guard.ground must be a string";
            }
        }

        guard = new SteerGuard(character, min, max, ground);
        return null;
    }

    private static string? OptionalInt(JsonElement guard, string name, out int? value)
    {
        value = null;
        if (!guard.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number))
        {
            return $"guard.{name} must be a whole number";
        }

        value = number;
        return null;
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static SteerParse Refused(string name, string detail, string? kind = null) => new(null, name, detail, kind);

    private static string CleanWhy(string? why)
    {
        if (string.IsNullOrWhiteSpace(why))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(why.Length, MaxWhyLength));
        foreach (var c in why)
        {
            sb.Append(char.IsControl(c) ? ' ' : c);
            if (sb.Length == MaxWhyLength)
            {
                break;
            }
        }

        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"^(?!(?i:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|$))[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

/// <summary>The inbox's file names: <c>&lt;seq&gt;-&lt;id&gt;.json</c>.</summary>
public static partial class SteerFileName
{
    /// <summary>Reads a name.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="sequence">The harness's sequence number.</param>
    /// <param name="id">The order's id.</param>
    /// <returns>False when the name is not of the form.</returns>
    public static bool TryParse(string name, out long sequence, out string id)
    {
        var match = Pattern().Match(name ?? string.Empty);
        sequence = 0;
        id = string.Empty;
        if (!match.Success || !long.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out sequence) || !SteerOrder.IsId(match.Groups[2].Value))
        {
            return false;
        }

        id = match.Groups[2].Value;
        return true;
    }

    [GeneratedRegex(@"^([0-9]{1,18})-(.+)\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>The states an order's report can carry (the steer contract's lifecycle). The waiting and running states come with the desk's slice.</summary>
public static class SteerState
{
    /// <summary>It acted; the name says how it ended.</summary>
    public const string Done = "done";

    /// <summary>It was never acted on; the name says why.</summary>
    public const string Refused = "refused";

    /// <summary>Its life ran out before the desk reached it.</summary>
    public const string Expired = "expired";

    /// <summary>A dry run passed every check and acted on nothing.</summary>
    public const string Checked = "checked";

    /// <summary>It acted and has not ended; a later report replaces this one.</summary>
    public const string Running = "running";

    /// <summary>It acted and did not get what it asked for; the name says what happened instead.</summary>
    public const string Failed = "failed";

    /// <summary>It was taken off before it acted (a <c>cancel</c>, a <c>/clear</c>, a stop that emptied the queue).</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>An order's latest report (the steer contract, section 3): what the harness reads from <c>outbox\&lt;id&gt;.json</c>.</summary>
/// <param name="Id">The order's id.</param>
/// <param name="Kind">The order's kind; empty when it could not be read.</param>
/// <param name="State">One of <see cref="SteerState"/>.</param>
/// <param name="Name">How it ended, as a token.</param>
/// <param name="Detail">The same in words.</param>
/// <param name="AtUtc">When the report was made.</param>
/// <param name="Level">The character's level then.</param>
/// <param name="Ground">The ground then; null when none is known.</param>
/// <param name="Cycle">The dial's cycle then.</param>
/// <param name="EvidenceJson">A JSON object of what proves it; null: an empty one.</param>
public sealed record SteerReport(string Id, string Kind, string State, string Name, string Detail, DateTimeOffset AtUtc, int Level, string? Ground, int Cycle, string? EvidenceJson = null)
{
    /// <summary>The report as one JSON object.</summary>
    public string ToJson() => SteerJson.Text(w =>
    {
        w.WriteStartObject();
        w.WriteNumber("v", SteerOrder.Version);
        w.WriteString("id", Id);
        w.WriteString("kind", Kind);
        w.WriteString("state", State);
        w.WriteString("name", Name);
        w.WriteString("detail", Detail);
        w.WriteString("at_utc", SteerJson.Utc(AtUtc));
        w.WriteNumber("level", Level);
        w.WriteString("ground", Ground);
        w.WriteNumber("cycle", Cycle);
        w.WritePropertyName("evidence");
        w.WriteRawValue(EvidenceJson ?? "{}");
        w.WriteEndObject();
    });
}

/// <summary>The JSON writing the steer files share.</summary>
public static class SteerJson
{
    /// <summary>What <paramref name="write"/> wrote, as text.</summary>
    /// <param name="write">Writes one JSON value.</param>
    public static string Text(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A time as the steer files write it: UTC, to the millisecond.</summary>
    /// <param name="at">The time.</param>
    public static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A guid as the steer files write it.</summary>
    /// <param name="guid">The guid.</param>
    public static string Guid(uint guid) => "0x" + guid.ToString("X8", CultureInfo.InvariantCulture);
}
