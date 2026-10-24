using Lifestone.Client.Session.Credentials;
using Lifestone.Data.Checkout;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The headless host's entry point (step ST15) without a backend: <c>--help</c> lists the options a headless run reads,
/// a bad command line exits 1 before anything runs, and a run
/// with no ready backend stops before the dats are opened and before any session exists (exit 2), naming the remedy; a
/// refused account (with the dats, on stand-in backend files) prints the verdict line with a fixed failure, names no
/// username and leaves no recording. Plain unit tests (they never reach the network); they live here because this project
/// references the headless host. They replace the wave-0 stub's tests.
/// </summary>
public sealed class HeadlessProgramTests : IDisposable
{
    private readonly string _empty = Directory.CreateTempSubdirectory("lifestone-headless-").FullName;

    [Fact]
    public void Help_lists_the_headless_options_and_exits_0()
    {
        var (code, output, error) = Run(["--help", "--laps=0"]);  // help wins over a bad option
        Assert.Equal(0, code);
        Assert.StartsWith("Lifestone headless host options.", output, StringComparison.Ordinal);
        foreach (var option in new[] { "  --help", "  --dat-dir=DIR", "  --log-level=", "  --quit-after=S", "  --workers=N", "  --scenario=", "  --account=", "  --laps=N", "  --create-character=NAME", "  --summary=PATH", "  --play", "  --server=IP:PORT", "  --record-session[=DIR]" })
        {
            Assert.Contains(option, output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--vsync", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--screenshot", output, StringComparison.Ordinal);
        Assert.Empty(error);
    }

    /// <summary>dod-1-2-12-13-1: refused on the command line, before the backend, the dats or any recording.</summary>
    [Theory]
    [InlineData("play")]
    [InlineData("alice")]
    public void A_non_test_account_is_refused_before_the_backend_is_looked_at(string account)
    {
        var (code, output, error) = Run(["--account=" + account], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Empty(output);
        Assert.Contains("uses only the test account", error, StringComparison.Ordinal);
        Assert.DoesNotContain("the backend is not running", error, StringComparison.Ordinal);
        Assert.DoesNotContain("--account=" + account, error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_empty, "cache")));
    }

    [Fact]
    public void The_test_account_given_explicitly_goes_on_to_the_backend_check()
    {
        var (code, _, error) = Run(["--account=test"], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Contains("the backend is not running", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_holtburg_laps_scenario_checks_the_backend_before_it_builds_anything()
    {
        var (code, output, error) = Run(["--scenario=holtburg-laps"], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Empty(output);
        Assert.Contains("the backend is not running", error, StringComparison.Ordinal);
        Assert.DoesNotContain("building the Holtburg collision", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_academy_exit_scenario_checks_the_backend_before_it_builds_anything()
    {
        var (code, output, error) = Run(["--scenario=academy-exit"], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Empty(output);
        Assert.Contains("the backend is not running", error, StringComparison.Ordinal);
        Assert.DoesNotContain("building the Training Academy collision", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_running_backend_nothing_starts()
    {
        // A runtime folder with no pids.json: the backend is not running.
        var (code, output, error) = Run([], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Empty(output);
        Assert.Contains("the backend is not running", error, StringComparison.Ordinal);
        Assert.Contains("start-server.ps1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_server_mod_nothing_starts()
    {
        File.WriteAllText(Path.Combine(_empty, ServerRuntimePaths.PidsFileName), """{ "ace": { "pid": 12 } }""");
        var (code, _, error) = Run([], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Contains("the backend is not ready", error, StringComparison.Ordinal);
        Assert.Contains("modReady", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Workers_is_accepted_but_said_to_have_no_effect()
    {
        var (code, _, error) = Run(["--workers=2"], serverRuntime: _empty);
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Contains("--workers has no effect in the headless host", error, StringComparison.Ordinal);
        Assert.Contains("WARN", error, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresLocalDatsFact]
    public void A_refused_account_prints_a_fixed_failure_and_leaves_no_recording()
    {
        // A stand-in checkout in the temporary folder: the marker, a backend whose mod is ready, and secrets.json with
        // sentinel accounts (no real credential) and no test account, so the account gate (the headless run is automated, so it asks for the test account
        // and no other) refuses before any network session exists.
        File.WriteAllText(Path.Combine(_empty, CheckoutLocator.MarkerFileName), string.Empty);
        var runtime = Directory.CreateDirectory(Path.Combine(_empty, "server", "runtime")).FullName;
        File.WriteAllText(
            Path.Combine(runtime, ServerRuntimePaths.PidsFileName),
            """{ "ace": { "pid": 12, "modReady": true, "modVersion": "0.1.0", "modWire": 1 } }""");
        File.WriteAllText(Path.Combine(runtime, CheckoutLocator.SecretsFileName), $$"""
            {
              "schema": 2,
              "defaultAccount": "play",
              "accounts": {
                "play": { "username": "Sentinel-PlayUser", "password": "{{Sentinel}}" },
                "alice": { "username": "Sentinel-OtherUser", "password": "{{Sentinel}}-other" }
              }
            }
            """);

        var (code, output, error) = Run(["--account=test", "--dat-dir=" + DatSet.ResolveDirectory()], serverRuntime: runtime, searchRoot: _empty);

        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Equal("headless done scenario=login ok=0 corrections=0 where_max_m=none unmatched=0 recording=none", output.Trim());
        Assert.Contains("the run could not start", error, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Sentinel-PlayUser", "Sentinel-OtherUser", Sentinel })
        {
            Assert.DoesNotContain(forbidden, output, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(Sentinel, error, StringComparison.Ordinal);
        var sessions = Path.Combine(_empty, "cache", "sessions");
        Assert.Empty(Directory.Exists(sessions) ? Directory.GetFiles(sessions) : []);
        Assert.Single(Directory.GetFiles(Path.Combine(_empty, "cache", "headless"), "*.log"));
    }

    [Fact]
    public void A_relative_runtime_folder_is_refused()
    {
        var (code, _, error) = Run([], serverRuntime: "server/runtime");
        Assert.Equal(Program.CouldNotStartExitCode, code);
        Assert.Contains(ServerRuntimePaths.EnvironmentVariable, error, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_empty, recursive: true);

    /// <summary>The stand-in password of the refused-account test (a value no real file holds).</summary>
    private const string Sentinel = "Sentinel-HEADLESS-pw-5c81e2";

    private static (int Code, string Output, string Error) Run(string[] args, string? serverRuntime = null, string? searchRoot = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environment = new HeadlessEnvironment(serverRuntime, [searchRoot ?? Path.GetTempPath()], TimeProvider.System);
        var code = Program.Run(args, output, error, environment);
        return (code, output.ToString(), error.ToString());
    }
}
