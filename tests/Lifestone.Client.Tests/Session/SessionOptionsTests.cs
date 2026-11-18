using System.Net;
using System.Text.Json;
using Lifestone.Client.Options;
using Lifestone.Client.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Session option group (ST5; m2-plan §5.4): its parser, its registration, and the rules that keep secrets off the
/// command line: no option takes one, and an account or character value is never repeated in an error.
/// </summary>
public sealed class SessionOptionsTests
{
    [Fact]
    public void Defaults_are_explore_mode_against_the_bundled_ace()
    {
        var o = SessionOptions.Parse([]);
        Assert.False(o.Play);
        Assert.Null(o.Account);
        Assert.Null(o.Character);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), o.Server);
        Assert.False(o.RecordSession);
        Assert.Null(o.RecordDirectory);
        Assert.Equal(2.0, o.BudgetMs);
        Assert.False(o.RetailNet);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void Every_option_parses()
    {
        var o = SessionOptions.Parse(
        [
            "--play", "--account=test", "--character=Lifestone Smoke", "--server=127.0.0.1:9100", "--record-session=cache/runs",
            "--session-budget-ms=3.5", "--retail-net", "--allow-climber-grant", "--vsync=off",
        ]);
        Assert.Empty(o.Errors);
        Assert.True(o.Play);
        Assert.Equal("test", o.Account);
        Assert.Equal("Lifestone Smoke", o.Character);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9100), o.Server);
        Assert.True(o.RecordSession);
        Assert.Equal("cache/runs", o.RecordDirectory);
        Assert.Equal(3.5, o.BudgetMs);
        Assert.True(o.RetailNet);
        Assert.True(o.AllowClimberGrant);
        Assert.False(SessionOptions.Parse([]).AllowClimberGrant); // M6 0 F5: off unless named
        Assert.True(o.PlayerRules); // M6 15: on unless turned off

        var plain = SessionOptions.Parse(["--record-session", "--server=[::1]:9000", "--play=off"]);
        Assert.Empty(plain.Errors);
        Assert.True(plain.RecordSession);
        Assert.Null(plain.RecordDirectory);
        Assert.Equal(new IPEndPoint(IPAddress.IPv6Loopback, 9000), plain.Server);
        Assert.False(plain.Play);
    }

    [Fact]
    public void Server_must_be_an_ip_literal_and_defaults_to_versions_json()
    {
        using var versions = JsonDocument.Parse(File.ReadAllBytes(RepositoryFiles.PathOf("server", "versions.json")));
        var network = versions.RootElement.GetProperty("network");
        Assert.Equal(network.GetProperty("host").GetString(), new SessionOptions().Server.Address.ToString());
        Assert.Equal(network.GetProperty("acePort").GetInt32(), new SessionOptions().Server.Port);
        Assert.Equal(SessionOptions.DefaultPort, new SessionOptions().Server.Port);
        Assert.NotSame(new SessionOptions().Server, new SessionOptions().Server);

        foreach (var (value, expected) in new[]
                 {
                     ("localhost:9000", "IP literal"),
                     ("127.1:9000", "IP literal"),
                     ("10.0.0.5:9000", "not a loopback address"),
                     ("127.0.0.1", "has no port"),
                     ("127.0.0.1:65535", "at most 65534"),
                     ("", "no server endpoint"),
                 })
        {
            var o = SessionOptions.Parse(["--server=" + value]);
            var error = Assert.Single(o.Errors);
            Assert.StartsWith("--server: ", error, StringComparison.Ordinal);
            Assert.Contains(expected, error, StringComparison.Ordinal);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), o.Server);
        }
    }

    [Theory]
    [InlineData("--account=", "--account: needs")]
    [InlineData("--account=hunter2 secret", "--account: the value is not an account key or username")]
    [InlineData("--account=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "--account: the value is not")]
    [InlineData("--character=BadéName", "--character: the value is not a character name")]
    [InlineData("--character", "--character: needs")]
    public void Account_and_character_values_are_never_repeated_in_errors(string argument, string expected)
    {
        var o = SessionOptions.Parse([argument]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith(expected, error, StringComparison.Ordinal);
        var value = CommandLineArgument.Parse(argument).Value;
        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, error, StringComparison.Ordinal);
        }

        Assert.Null(o.Account);
        Assert.Null(o.Character);
    }

    [Theory]
    [InlineData("--session-budget-ms=0", "not more than 0")]
    [InlineData("--session-budget-ms=-1", "not more than 0")]
    [InlineData("--session-budget-ms=1000", "at most 100")]
    [InlineData("--session-budget-ms=fast", "not a number")]
    [InlineData("--record-session=", "the folder is empty")]
    [InlineData("--retail-net=maybe", "not on or off")]
    public void Bad_values_are_reported_and_keep_the_default(string argument, string expected)
    {
        var o = SessionOptions.Parse([argument]);
        Assert.Contains(expected, Assert.Single(o.Errors), StringComparison.Ordinal);
        Assert.Equal(2.0, o.BudgetMs);
        Assert.False(o.RecordSession);
        Assert.False(o.RetailNet);
    }

    [Fact]
    public void The_session_group_is_registered_for_the_main_scene_and_the_headless_host()
    {
        var registry = OptionCatalog.Registry;
        string[] names = ["--play", "--account", "--character", "--server", "--record-session", "--session-budget-ms", "--retail-net", "--allow-climber-grant", "--live-raise-check"];
        foreach (var name in names)
        {
            Assert.Equal("Session", registry.FeatureOf(name));
            Assert.Equal(OptionScenes.Main | OptionScenes.Headless, registry.ScenesOf(name));
        }

        Assert.Equal(names.Order(StringComparer.Ordinal), OptionCatalog.Session().Options.Select(o => o.Name).Order(StringComparer.Ordinal));
        Assert.True(registry.Find("--record-session")!.ValueOptional);
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--play", "--account=test", "--server=127.0.0.1:9000"]), OptionScenes.Main));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--play", "--retail-net"]), OptionScenes.Headless));
        Assert.All(names, n => Assert.False(registry.EndsRun(CommandLineArgument.ParseAll([n]), OptionScenes.Main)));
    }

    [Fact]
    public void The_headless_group_is_registered_for_the_headless_host_only()
    {
        var registry = OptionCatalog.Registry;
        foreach (var name in new[] { "--scenario", "--laps", "--create-character", "--summary" })
        {
            Assert.Equal("Headless host", registry.FeatureOf(name));
            Assert.Equal(OptionScenes.Headless, registry.ScenesOf(name));
        }

        Assert.Equal(
            "'--scenario=(value hidden)' is an option of the Headless host (dotnet Lifestone.Headless.dll), not read in this run's main scene; ignored",
            Assert.Single(registry.Problems(CommandLineArgument.ParseAll(["--scenario=login"]), OptionScenes.Main)));
    }

    [Fact]
    public void No_registered_option_takes_a_secret()
    {
        string[] forbidden = ["pass", "secret", "token", "key", "pwd", "credential"];
        foreach (var option in OptionCatalog.Session().Options.Concat(OptionCatalog.Headless().Options))
        {
            Assert.DoesNotContain(forbidden, f => option.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(forbidden, f => (option.Value ?? string.Empty).Contains(f, StringComparison.OrdinalIgnoreCase));
        }
    }
}
