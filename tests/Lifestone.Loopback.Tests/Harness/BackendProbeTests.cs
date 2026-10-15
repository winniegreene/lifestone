using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Harness;

/// <summary>
/// <see cref="BackendProbe"/> over stand-in checkouts in a temporary folder: each readiness condition that fails gives
/// its own named reason, the secrets file is judged by its keys only, and no reason ever carries a secret. Nothing here
/// touches the real backend or the network (the UDP listener query is replaced).
/// </summary>
public sealed class BackendProbeTests : IDisposable
{
    private const int AcePort = 9123;
    private const string SentinelPassword = "Sentinel-Pa55word-7f3a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lifestone-probe-" + Guid.NewGuid().ToString("N"));
    private readonly BackendFiles _files;

    public BackendProbeTests()
    {
        _files = new BackendFiles(_root);
        Directory.CreateDirectory(Path.Combine(_root, "server", "runtime"));
        WriteJson(_files.VersionsFile, new { network = new { host = "127.0.0.1", acePort = AcePort, mariadbPort = 3316 } });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Without_pids_json_the_backend_is_not_running()
    {
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("not running", result.Reason, StringComparison.Ordinal);
        Assert.Contains("pids.json", result.Reason, StringComparison.Ordinal);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, AcePort), result.AceEndPoint);
    }

    [Fact]
    public void A_backend_not_pinned_to_127_0_0_1_is_never_ready()
    {
        WriteJson(_files.VersionsFile, new { network = new { host = "localhost", acePort = AcePort } });
        WriteReadyBackend();
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("network.host is not 127.0.0.1", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.AceEndPoint);
    }

    [Fact]
    public void Pids_json_without_ace_means_ace_is_not_running()
    {
        WriteJson(_files.PidsFile, new { mariadbd = new { pid = 1, path = "x", startTime = "2026-01-01T00:00:00.0000000Z" } });
        Assert.Contains("no 'ace' entry", Probe().Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exited_ace_is_not_running()
    {
        WriteJson(_files.PidsFile, new { ace = new { pid = int.MaxValue, path = "x", startTime = "2026-01-01T00:00:00.0000000Z", modReady = true } });
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("has exited", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pid_now_used_by_another_program_is_not_ace()
    {
        var (pid, _, start) = Self();
        WriteJson(_files.PidsFile, new { ace = new { pid, path = Path.Combine(_root, "ACE.Server.exe"), startTime = start, modReady = true } });
        Assert.Contains("another program", Probe().Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pid_reused_by_a_later_process_is_not_ace()
    {
        var (pid, path, _) = Self();
        using var self = Process.GetCurrentProcess();
        var earlier = self.StartTime.ToUniversalTime().AddHours(-1).ToString("o", CultureInfo.InvariantCulture);
        WriteJson(_files.PidsFile, new { ace = new { pid, path, startTime = earlier, modReady = true } });
        Assert.Contains("reused", Probe().Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void Ace_without_a_ready_server_mod_is_not_ready(bool? modReady)
    {
        var (pid, path, start) = Self();
        object ace = modReady is null ? new { pid, path, startTime = start } : new { pid, path, startTime = start, modReady };
        WriteJson(_files.PidsFile, new { ace });
        WriteSecrets(withTestAccount: true);
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("modReady", result.Reason, StringComparison.Ordinal);
        Assert.Contains("Admin auto-promotion", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_udp_listener_on_the_pinned_port_the_backend_is_not_ready()
    {
        WriteReadyBackend();
        IPEndPoint? asked = null;
        var result = BackendProbe.Probe(_files, ep =>
        {
            asked = ep;
            return false;
        });
        Assert.False(result.IsReady);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, AcePort), asked);
        Assert.Contains($"127.0.0.1:{AcePort}", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_secrets_json_the_backend_is_not_ready()
    {
        WriteReadyBackend();
        File.Delete(_files.SecretsFile);
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("secrets.json does not exist", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_without_the_test_account_are_not_ready_and_no_value_is_shown()
    {
        WriteReadyBackend();
        WriteSecrets(withTestAccount: false);
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("'accounts.test'", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"accounts\":{\"test\":\"" + SentinelPassword + "\"}}")]
    [InlineData("{\"accounts\":[{\"test\":{}}],\"test\":{}}")]
    [InlineData("{\"mariadb\":{\"accounts\":{\"test\":{}}}}")]
    public void Only_an_object_at_accounts_test_counts(string json)
    {
        WriteReadyBackend();
        File.WriteAllText(_files.SecretsFile, json);
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("'accounts.test'", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_secrets_are_not_ready_and_their_content_is_not_quoted()
    {
        WriteReadyBackend();
        File.WriteAllText(_files.SecretsFile, SentinelPassword + "{");
        var result = Probe();
        Assert.False(result.IsReady);
        Assert.Contains("not valid JSON", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword[..1] + "'", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_live_ace_with_a_ready_mod_a_listener_and_the_test_account_is_ready()
    {
        WriteReadyBackend();
        var result = Probe();
        Assert.True(result.IsReady, result.Reason);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, AcePort), result.AceEndPoint);
        Assert.StartsWith("backend ready", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, result.Reason, StringComparison.Ordinal);

        // A UTF-8 byte order mark (Windows PowerShell 5.1's default) does not hide the key.
        File.WriteAllBytes(_files.SecretsFile, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(_files.SecretsFile)]);
        Assert.True(Probe().IsReady);
    }

    private static (int Pid, string Path, string StartTime) Self()
    {
        using var self = Process.GetCurrentProcess();
        return (self.Id, self.MainModule!.FileName, self.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
    }

    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value));

    private BackendProbeResult Probe() => BackendProbe.Probe(_files, _ => true);

    /// <summary>Everything ready: this test process stands in for ACE, with modReady and a schema-2 secrets file.</summary>
    private void WriteReadyBackend()
    {
        var (pid, path, start) = Self();
        WriteJson(_files.PidsFile, new { startedUtc = start, host = "127.0.0.1", acePorts = new[] { AcePort, AcePort + 1 }, ace = new { pid, path, startTime = start, modReady = true, modVersion = "test" } });
        WriteSecrets(withTestAccount: true);
    }

    private void WriteSecrets(bool withTestAccount)
    {
        var play = new { username = "lifestone", password = SentinelPassword };
        object accounts = withTestAccount
            ? new { play, test = new { username = "lifestone-test", password = SentinelPassword } }
            : new { play };
        WriteJson(_files.SecretsFile, new { schema = 2, mariadb = new { rootPassword = SentinelPassword }, defaultAccount = "play", accounts });
    }
}
