using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using Lifestone.Client.Tests;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>What <see cref="BackendProbe"/> found.</summary>
/// <param name="IsReady">True when every readiness condition holds and loopback tests may log in.</param>
/// <param name="Reason">Why the backend is not ready (the loopback tests' skip reason), or a one-line description of the ready backend.</param>
/// <param name="AceEndPoint">ACE's login endpoint (127.0.0.1 and the pinned port), once server/versions.json has been read.</param>
public sealed record BackendProbeResult(bool IsReady, string Reason, IPEndPoint? AceEndPoint);

/// <summary>The files of one checkout's localhost backend that <see cref="BackendProbe"/> reads.</summary>
/// <param name="Checkout">The checkout whose <c>server\runtime</c> runs the backend (the main checkout, not a worktree).</param>
public sealed record BackendFiles(string Checkout)
{
    /// <summary><c>server\versions.json</c>: the pinned host and ACE port.</summary>
    public string VersionsFile => Path.Combine(Checkout, "server", "versions.json");

    /// <summary><c>server\runtime\pids.json</c>, written by start-server.ps1.</summary>
    public string PidsFile => Path.Combine(Checkout, "server", "runtime", "pids.json");

    /// <summary><c>server\runtime\secrets.json</c> (gitignored): opened only to check that the test account's key exists.</summary>
    public string SecretsFile => Path.Combine(Checkout, "server", "runtime", "secrets.json");
}

/// <summary>
/// Decides whether the localhost backend is ready for loopback tests, without talking to it: it never sends a packet,
/// never opens a connection and never reads a secret's value. <see cref="LoopbackFactAttribute"/> and
/// <see cref="LoopbackTheoryAttribute"/> skip with its <see cref="BackendProbeResult.Reason"/> when it is not ready.
/// </summary>
/// <remarks>
/// <para>Ready means all of these hold, checked in this order (the first that fails is the reason):</para>
/// <list type="number">
/// <item>server/versions.json pins ACE to 127.0.0.1 and names its port;</item>
/// <item>server/runtime/pids.json has an <c>ace</c> entry whose process is alive: same PID, same executable path and a
/// start time within 2 s of the recorded one, as LifestoneServer.psm1's Get-LsLiveProcess checks;</item>
/// <item><c>ace.modReady</c> is <c>true</c>: the Lifestone server mod has loaded and turned ACE's first-account Admin
/// auto-promotion off (ACE enables mods before DatabaseManager sets AutoPromoteNextAccountToAdmin). Until the mod
/// exists this key is absent, so every loopback test skips and no automated login can create the first account;</item>
/// <item>a UDP listener is bound to 127.0.0.1 on ACE's port;</item>
/// <item>server/runtime/secrets.json exists and has the key <c>accounts.test</c> (the loopback tests' account). Only
/// the property names are looked at, and the file is opened last, only when everything else says a backend is
/// ready; its bytes are zeroed after the check.</item>
/// </list>
/// <para>The backend is the main checkout's (<see cref="RepositoryFiles.MainCheckout"/>): worktrees share the one ACE.</para>
/// </remarks>
public static class BackendProbe
{
    /// <summary>The key under <c>accounts</c> in secrets.json (schema 2) that names the loopback tests' account.</summary>
    public const string TestAccountKey = "test";

    /// <summary>The only host the backend may use.</summary>
    public const string PinnedHost = "127.0.0.1";

    private static readonly Lazy<BackendProbeResult> FirstProbe = new(Probe);

    /// <summary>The main checkout's backend files.</summary>
    public static BackendFiles MainCheckoutFiles => new(RepositoryFiles.MainCheckout);

    /// <summary>The first probe of this process, kept for test discovery (the attributes' skip reasons).</summary>
    public static BackendProbeResult AtDiscovery => FirstProbe.Value;

    /// <summary>Probes the main checkout's backend now.</summary>
    public static BackendProbeResult Probe() => Probe(MainCheckoutFiles, IsUdpListening);

    /// <summary>Probes the backend described by <paramref name="files"/>.</summary>
    /// <param name="files">The backend's files.</param>
    /// <param name="isUdpListening">Whether a UDP listener is bound to an endpoint (<see cref="IsUdpListening"/>; tests pass their own).</param>
    public static BackendProbeResult Probe(BackendFiles files, Func<IPEndPoint, bool> isUdpListening)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(isUdpListening);

        if (ReadAceEndPoint(files, out var why) is not { } endPoint)
        {
            return NotReady(why, null);
        }

        if (!File.Exists(files.PidsFile))
        {
            return NotReady($"the localhost backend is not running ({files.PidsFile} does not exist; start it with server\\start-server.ps1)", endPoint);
        }

        AceEntry ace;
        try
        {
            using var pids = JsonDocument.Parse(File.ReadAllBytes(files.PidsFile));
            if (pids.RootElement.ValueKind != JsonValueKind.Object || !pids.RootElement.TryGetProperty("ace", out var entry) || entry.ValueKind != JsonValueKind.Object)
            {
                return NotReady("ACE is not running (pids.json has no 'ace' entry; start it with server\\start-server.ps1)", endPoint);
            }

            ace = AceEntry.From(entry);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return NotReady($"{files.PidsFile} cannot be read ({e.GetType().Name})", endPoint);
        }

        if (LivenessProblem(ace) is { } dead)
        {
            return NotReady(dead, endPoint);
        }

        if (!ace.ModReady)
        {
            return NotReady(
                "ACE is running without a ready Lifestone server mod (pids.json ace.modReady is not true): loopback tests never log in " +
                "before the mod has turned ACE's first-account Admin auto-promotion off",
                endPoint);
        }

        if (!isUdpListening(endPoint))
        {
            return NotReady($"nothing listens on UDP {endPoint} (ACE pid {ace.Pid} is alive but its world is not open)", endPoint);
        }

        if (TestAccountProblem(files.SecretsFile) is { } noAccount)
        {
            return NotReady(noAccount, endPoint);
        }

        return new BackendProbeResult(true, $"backend ready: ACE pid {ace.Pid} on UDP {endPoint} with the Lifestone server mod", endPoint);
    }

    /// <summary>True when some process has a UDP socket bound to exactly <paramref name="endPoint"/> (a local query; nothing is sent).</summary>
    public static bool IsUdpListening(IPEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(l => l.Port == endPoint.Port && l.Address.Equals(endPoint.Address));
    }

    /// <summary>
    /// Null when <paramref name="secretsFile"/> exists and holds an object at <c>accounts.test</c>, else the reason. Reads
    /// property names only (no value is decoded or copied), never puts file content into the reason, and zeroes the
    /// bytes it read.
    /// </summary>
    public static string? TestAccountProblem(string secretsFile)
    {
        ArgumentNullException.ThrowIfNull(secretsFile);
        if (!File.Exists(secretsFile))
        {
            return $"{secretsFile} does not exist (run server\\setup-server.ps1)";
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(secretsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{secretsFile} cannot be read ({e.GetType().Name})";
        }

        try
        {
            return HasTestAccount(bytes)
                ? null
                : $"{secretsFile} has no 'accounts.{TestAccountKey}' entry (the loopback tests' account; secrets schema 2, written by server\\setup-server.ps1)";
        }
        catch (JsonException)
        {
            // The exception's message can quote the offending character; never pass it on.
            return $"{secretsFile} is not valid JSON";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>The UTF-8 byte order mark (U+FEFF) that a secrets.json saved by some editors starts with.</summary>
    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private static bool HasTestAccount(ReadOnlySpan<byte> json)
    {
        if (json.StartsWith(Utf8ByteOrderMark))
        {
            json = json[Utf8ByteOrderMark.Length..];
        }

        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isAccounts = reader.ValueTextEquals("accounts"u8);
            reader.Read();
            if (isAccounts && reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var isTest = reader.ValueTextEquals(TestAccountKey);
                    reader.Read();
                    if (isTest)
                    {
                        return reader.TokenType == JsonTokenType.StartObject;
                    }

                    reader.Skip();
                }

                return false;
            }

            reader.Skip();
        }

        return false;
    }

    private static IPEndPoint? ReadAceEndPoint(BackendFiles files, out string why)
    {
        why = string.Empty;
        try
        {
            using var versions = JsonDocument.Parse(File.ReadAllBytes(files.VersionsFile));
            if (!versions.RootElement.TryGetProperty("network", out var network)
                || !network.TryGetProperty("host", out var host)
                || !network.TryGetProperty("acePort", out var port)
                || !port.TryGetInt32(out var acePort)
                || acePort is <= 0 or > 65535)
            {
                why = $"{files.VersionsFile} has no network.host and network.acePort";
                return null;
            }

            if (host.GetString() != PinnedHost)
            {
                why = $"{files.VersionsFile} network.host is not {PinnedHost}; loopback tests only talk to localhost";
                return null;
            }

            return new IPEndPoint(IPAddress.Loopback, acePort);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            why = $"{files.VersionsFile} cannot be read ({e.GetType().Name})";
            return null;
        }
    }

    /// <summary>Null when the pids.json entry's process is alive and is the one recorded (Get-LsLiveProcess), else the reason.</summary>
    private static string? LivenessProblem(AceEntry ace)
    {
        if (ace.Pid <= 0)
        {
            return "ACE is not running (pids.json ace.pid is missing)";
        }

        Process process;
        try
        {
            process = Process.GetProcessById(ace.Pid);
        }
        catch (ArgumentException)
        {
            return $"ACE is not running (pid {ace.Pid} from pids.json has exited; start it with server\\start-server.ps1)";
        }

        using (process)
        {
            try
            {
                if (process.HasExited)
                {
                    return $"ACE is not running (pid {ace.Pid} from pids.json has exited)";
                }

                var path = process.MainModule?.FileName;
                if (path is not null && ace.Path is not null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(ace.Path), StringComparison.OrdinalIgnoreCase))
                {
                    return $"ACE is not running (pid {ace.Pid} from pids.json now belongs to another program)";
                }

                if (ace.StartTimeUtc is { } recorded && Math.Abs((process.StartTime.ToUniversalTime() - recorded.UtcDateTime).TotalSeconds) > 2)
                {
                    return $"ACE is not running (pid {ace.Pid} from pids.json was reused by a later process)";
                }
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // As Get-LsLiveProcess: a process whose path or start time cannot be read still counts as the recorded one.
            }
        }

        return null;
    }

    private static BackendProbeResult NotReady(string reason, IPEndPoint? endPoint) => new(false, reason, endPoint);

    /// <summary>The fields of pids.json's <c>ace</c> entry the probe uses.</summary>
    private readonly record struct AceEntry(int Pid, string? Path, DateTimeOffset? StartTimeUtc, bool ModReady)
    {
        public static AceEntry From(JsonElement entry)
        {
            var pid = entry.TryGetProperty("pid", out var p) && p.TryGetInt32(out var value) ? value : 0;
            var path = entry.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString() : null;
            DateTimeOffset? start = entry.TryGetProperty("startTime", out var s) && s.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(s.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed
                    : null;
            var modReady = entry.TryGetProperty("modReady", out var ready) && ready.ValueKind == JsonValueKind.True;
            return new AceEntry(pid, path, start, modReady);
        }
    }
}
