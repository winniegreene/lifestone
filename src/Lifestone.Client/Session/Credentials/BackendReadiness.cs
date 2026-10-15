using System.Text.Json;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Credentials;

/// <summary>What server\runtime\pids.json says about ACE and the Lifestone server mod.</summary>
/// <param name="ModReady">True only when <c>ace.modReady</c> is the JSON literal <c>true</c>.</param>
/// <param name="ModVersion">The mod's version (<c>ace.modVersion</c>), when given.</param>
/// <param name="ModWire">The mod's tool-wire version (<c>ace.modWire</c>), when given.</param>
/// <param name="Reason">Why the backend is not ready, one sentence with the remedy; or, when ready, a description.</param>
public readonly record struct BackendReadinessReport(bool ModReady, string? ModVersion, int? ModWire, string Reason);

/// <summary>Thrown when the backend is not ready for a login (<see cref="BackendReadiness.Require"/>).</summary>
public sealed class BackendNotReadyException : InvalidOperationException
{
    /// <summary>The exception for <paramref name="reason"/>.</summary>
    public BackendNotReadyException(string reason)
        : base("Refused to log in: " + reason + ".")
    {
        Reason = reason;
    }

    /// <summary>Why, one sentence with the remedy.</summary>
    public string Reason { get; }
}

/// <summary>
/// The mod-ready gate (owner decision D11; docs/session.md §4): no live session starts unless pids.json says
/// <c>ace.modReady</c> is <c>true</c>, which start-server.ps1 writes only after the Lifestone server mod has loaded and
/// turned off ACE's promotion of the first account to Admin. Without it, the first account a login creates would be an
/// Admin. Both hosts check it, with no escape hatch. The host reads pids.json as bytes; nothing here touches a file.
/// </summary>
/// <remarks>
/// The check reads the file as written; it does not prove the process is alive (Lifestone.Client does no process I/O).
/// A stale pids.json of a stopped ACE passes this gate and then fails the login handshake with a clear timeout.
/// </remarks>
public static class BackendReadiness
{
    /// <summary>The file, for messages.</summary>
    public const string FileName = "pids.json";

    private const string StartHint = "start the backend with tools\\play.ps1 or server\\start-server.ps1";

    /// <summary>True when <paramref name="pidsJson"/> says <c>ace.modReady</c> is <c>true</c>; otherwise false and why.</summary>
    /// <param name="pidsJson">The bytes of server\runtime\pids.json (empty when it does not exist).</param>
    /// <param name="reason">Why not (or, when ready, a description).</param>
    public static bool ModReady(ReadOnlySpan<byte> pidsJson, out string reason)
    {
        var report = Read(pidsJson);
        reason = report.Reason;
        return report.ModReady;
    }

    /// <summary>Reads pids.json's <c>ace</c> entry. Never throws for any input.</summary>
    /// <param name="pidsJson">The bytes of server\runtime\pids.json (empty when it does not exist).</param>
    public static BackendReadinessReport Read(ReadOnlySpan<byte> pidsJson)
    {
        if (SecretsJson.IsBlank(pidsJson))
        {
            return NotReady($"{FileName} is missing or empty, so ACE is not running ({StartHint})");
        }

        try
        {
            var reader = SecretsJson.Reader(pidsJson);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return NotReady($"{FileName} is not a JSON object ({StartHint})");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isAce = reader.ValueTextEquals("ace"u8);
                reader.Read();
                if (!isAce)
                {
                    reader.Skip();
                    continue;
                }

                return reader.TokenType == JsonTokenType.StartObject
                    ? ReadAce(ref reader)
                    : NotReady($"{FileName}'s 'ace' entry is not an object, so ACE is not running ({StartHint})");
            }

            return NotReady($"ACE is not running ({FileName} has no 'ace' entry; {StartHint})");
        }
        catch (JsonException e)
        {
            return NotReady($"{SecretsJson.NotValidJson(FileName, e)}; restart the backend (server\\stop-server.ps1, then {StartHint})");
        }
    }

    /// <summary>Throws <see cref="BackendNotReadyException"/>, after one WARNING naming <paramref name="runner"/>, unless the mod is ready.</summary>
    /// <param name="pidsJson">The bytes of server\runtime\pids.json.</param>
    /// <param name="runner">Who asks, for the log line.</param>
    /// <param name="log">Where the refusal is logged (category <c>session</c>).</param>
    /// <returns>The report of the ready backend.</returns>
    /// <exception cref="BackendNotReadyException">The mod is not ready.</exception>
    public static BackendReadinessReport Require(ReadOnlySpan<byte> pidsJson, string runner, ILogSink log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runner);
        ArgumentNullException.ThrowIfNull(log);
        var report = Read(pidsJson);
        if (!report.ModReady)
        {
            log.Warn(SessionLog.Category, $"mod-ready gate: {runner} refused: {report.Reason}", new LogField("runner", runner));
            throw new BackendNotReadyException(report.Reason);
        }

        return report;
    }

    private static BackendReadinessReport ReadAce(ref Utf8JsonReader reader)
    {
        var modReady = JsonTokenType.None;
        string? version = null;
        int? wire = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("modReady"u8))
            {
                reader.Read();
                modReady = reader.TokenType;
                reader.Skip();
            }
            else if (reader.ValueTextEquals("modVersion"u8))
            {
                reader.Read();
                version = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                reader.Skip();
            }
            else if (reader.ValueTextEquals("modWire"u8))
            {
                reader.Read();
                wire = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var w) ? w : null;
                reader.Skip();
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        const string Restart = "run server\\stop-server.ps1, then server\\start-server.ps1 (it waits for 'Lifestone mod ready')";
        return modReady switch
        {
            JsonTokenType.True => new BackendReadinessReport(
                true, version, wire, $"ACE is running with the Lifestone server mod ready (version {version ?? "unknown"}, wire {wire?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"})"),
            JsonTokenType.None => NotReady(
                $"ACE is running without a confirmed Lifestone server mod ({FileName} has no ace.modReady): a login could make the account an Admin; {Restart}"),
            _ => NotReady(
                $"ACE is running without a ready Lifestone server mod ({FileName} ace.modReady is not true): a login could make the account an Admin; {Restart}"),
        };
    }

    private static BackendReadinessReport NotReady(string reason) => new(false, null, null, reason);
}
