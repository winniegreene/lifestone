using System.Text.RegularExpressions;
using Lifestone.Client.Tests;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// The server scripts without running them (they must never run from a test: they start servers): every script
/// parses in Windows PowerShell 5.1 and PowerShell 7, stays ASCII, takes the switches its callers use (tools\smoke.ps1,
/// tools\play.ps1, the lead's merge procedure), runs dotnet without build servers, and no longer promises that the
/// first account becomes an Admin.
/// </summary>
public sealed partial class ServerScriptTests
{
    private static readonly string Server = RepositoryFiles.PathOf("server");

    /// <summary>The shells the scripts must work in.</summary>
    public static TheoryData<string> Shells => PowerShellHost.Shells;

    private static IEnumerable<string> Scripts() =>
        Directory.EnumerateFiles(Server, "*.ps*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".ps1", StringComparison.Ordinal) || p.EndsWith(".psm1", StringComparison.Ordinal))
            .Where(p => !Path.GetRelativePath(Server, p).StartsWith("runtime", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Shells))]
    public void Every_server_script_parses(string shell)
    {
        var files = Scripts().ToArray();
        Assert.Contains(files, f => f.EndsWith("setup-server.ps1", StringComparison.Ordinal));
        var script = "$problems = @()\n"
            + "foreach ($f in @(" + string.Join(", ", files.Select(PowerShellHost.Quote)) + ")) {\n"
            + "  $t = $null; $e = $null\n"
            + "  [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$t, [ref]$e)\n"
            + "  foreach ($x in $e) { $problems += \"$([IO.Path]::GetFileName($f)):$($x.Extent.StartLineNumber): $($x.Message)\" }\n"
            + "}\n"
            + "Out-Result @($problems)\n";
        var problems = PowerShellHost.RunOk(shell, script).Result!.Value.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.True(problems.Length == 0, "parse errors:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Every_server_script_is_ascii()
    {
        // Windows PowerShell 5.1 reads a script without a byte order mark in the ANSI code page (.gitattributes).
        var offences = Scripts()
            .SelectMany(p => File.ReadAllLines(p).Select((line, i) => (p, i, line)))
            .Where(x => x.line.Any(c => c > '~' || (c < ' ' && c != '\t')))
            .Select(x => $"{Path.GetRelativePath(Server, x.p)}:{x.i + 1}")
            .ToArray();
        Assert.True(offences.Length == 0, "non-ASCII characters in server scripts:\n" + string.Join('\n', offences));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_scripts_take_the_switches_their_callers_use(string shell)
    {
        // Get-Command reads a script's param block without running it.
        var script = "$out = [ordered]@{}\n"
            + "foreach ($s in @('setup-server', 'start-server', 'stop-server', 'build-mod')) {\n"
            + "  $out[$s] = @((Get-Command (Join-Path " + PowerShellHost.Quote(Server) + " \"$s.ps1\")).Parameters.Keys)\n"
            + "}\n"
            + "Out-Result $out\n";
        var result = PowerShellHost.RunOk(shell, script).Result!.Value;
        string[] Params(string name) => [.. result.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

        Assert.Superset(
            new HashSet<string>(["RebuildAce", "ReimportWorld", "ResetEmptyDatabases", "RebuildMod", "ModOnly", "ModSource", "AddAccount"]),
            new HashSet<string>(Params("setup-server")));

        // tools\smoke.ps1 -Backend start passes -TimeoutSec; tools\play.ps1 and smoke stop with -AceOnly when MariaDB ran before.
        Assert.Superset(
            new HashSet<string>(["TimeoutSec", "DatabaseOnly", "KeepOnFailure", "AllowNoMod", "AllowStaleMod"]),
            new HashSet<string>(Params("start-server")));
        Assert.Superset(new HashSet<string>(["TimeoutSec", "Force", "AceOnly"]), new HashSet<string>(Params("stop-server")));
        Assert.Superset(new HashSet<string>(["OutDir", "AceDir", "BuildKey"]), new HashSet<string>(Params("build-mod")));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_module_exports_the_backend_contract(string shell)
    {
        var names = PowerShellHost.RunOk(shell, "Out-Result @((Get-Command -Module LifestoneServer).Name)\n").Result!.Value
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
        string[] contract =
        [
            "Get-LsConfig", "Get-LsBackendState", "Find-LsSecretLeaks", "Test-LsServerModLive", "Get-LsServerModBuildKey",
            "Invoke-LsServerModBuild", "Install-LsServerMod", "Invoke-LsServerModSetup", "Get-LsServerModInstall",
            "Initialize-LsSecrets", "Add-LsAccount", "Write-LsSecrets", "Set-LsSentinelAdmin", "Write-LsAccountAudit",
            "Get-LsAceLogSignal", "Get-LsModReadyInfo", "Get-LsDotnetEnvironment", "Send-LsConsoleCommand",
            "New-LsAceLogWatch", "Read-LsAceLogChunk", "Add-LsAceLogText", "Get-LsStartedAceMod", "Get-LsSentinelAdmin",
            "Get-LsModSilenceProblem",
        ];
        Assert.All(contract, c => Assert.Contains(c, names));
    }

    [GeneratedRegex(@"Invoke-LsTool -FilePath \$dotnet -Arguments @\('(?<verb>restore|publish|build)'[^)]*\)")]
    private static partial Regex DotnetCall();

    [Fact]
    public void Every_dotnet_build_in_the_scripts_runs_without_build_servers()
    {
        // CLAUDE.md, ".NET": --disable-build-servers on every dotnet process that builds.
        var calls = Scripts()
            .SelectMany(p => DotnetCall().Matches(File.ReadAllText(p)).Select(m => (File: Path.GetFileName(p), Call: m.Value)))
            .ToArray();
        Assert.Contains(calls, c => c.File == "setup-server.ps1" && c.Call.Contains("'restore'", StringComparison.Ordinal));
        Assert.Contains(calls, c => c.File == "setup-server.ps1" && c.Call.Contains("'publish'", StringComparison.Ordinal));
        Assert.All(calls, c => Assert.Contains("'--disable-build-servers'", c.Call, StringComparison.Ordinal));

        foreach (var file in new[] { Path.Combine(Server, "build-mod.ps1"), Path.Combine(Server, "lib", "LifestoneServer.psm1") })
        {
            var text = File.ReadAllText(file);
            Assert.Contains("'--disable-build-servers', '-nodeReuse:false', '-p:UseSharedCompilation=false'", text, StringComparison.Ordinal);
            Assert.Contains("Get-LsDotnetEnvironment", text, StringComparison.Ordinal);

            // Each build folder has its own intermediates (<output>.obj), never the project's shared obj\ (SM2).
            Assert.Matches(@"""-p:BaseIntermediateOutputPath=\$\w+\\""", text);
        }

        var environment = File.ReadAllText(Path.Combine(Server, "lib", "LifestoneServer.psm1"));
        Assert.All(
            ["DOTNET_CLI_TELEMETRY_OPTOUT        = '1'", "DOTNET_NOLOGO                      = '1'", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH    = 'false'", "DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'"],
            line => Assert.Contains(line, environment, StringComparison.Ordinal));
    }

    [Fact]
    public void The_config_template_keeps_the_default_access_level_at_zero()
    {
        // Owner decision D11: the sentinel Admin account and the mod keep ACE's first-account promotion from ever happening.
        var template = File.ReadAllText(Path.Combine(Server, "templates", "Config.template.js"));
        Assert.Contains("\"DefaultAccessLevel\": 0,", template, StringComparison.Ordinal);
    }

    [Fact]
    public void Versions_json_names_the_play_test_and_sentinel_accounts()
    {
        var text = File.ReadAllText(Path.Combine(Server, "versions.json"));
        Assert.Contains("\"playAccountName\": \"lifestone\"", text, StringComparison.Ordinal);
        Assert.Contains("\"testAccountName\": \"lifestone-test\"", text, StringComparison.Ordinal);
        Assert.Contains("\"sentinelAdminName\": \"lifestone-sentinel\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_server_waits_for_the_mod_and_records_it()
    {
        var text = File.ReadAllText(Path.Combine(Server, "start-server.ps1"));

        // Readiness is the mod's ready line plus both UDP endpoints; the old 'Initializing CommandManager' gate is gone.
        Assert.Contains("if ($watch.ReadyLine -or ($AllowNoMod -and $watch.WorldOpen))", text, StringComparison.Ordinal);
        Assert.DoesNotContain("if ($all -match 'Initializing CommandManager')", text, StringComparison.Ordinal);
        Assert.Contains("'ModFailed'", text, StringComparison.Ordinal);
        Assert.Contains("New-LsPidEntry $ace $cfg.AceExe $aceMod", text, StringComparison.Ordinal);
        Assert.Contains("Get-LsServerModInstall $cfg", text, StringComparison.Ordinal);
        Assert.Contains(".ModReady", text, StringComparison.Ordinal);

        // The log is read in whole lines (AceLogWatchScriptTests), and what pids.json records comes from one function,
        // which gives modReady false for every -AllowNoMod start.
        Assert.Contains("$watch = New-LsAceLogWatch $AllowNoMod.IsPresent $logOffset", text, StringComparison.Ordinal);
        Assert.Contains("Add-LsAceLogText $watch (Read-LsAceLogChunk $watch $cfg.AceLog) -Final:$last", text, StringComparison.Ordinal);
        Assert.Contains("$aceMod = Get-LsStartedAceMod $cfg $watch $mod", text, StringComparison.Ordinal);

        // A mod ACE never loaded (and never logged about) fails the start soon after the world opens (AceLogWatchScriptTests).
        Assert.Contains("$silence = Get-LsModSilenceProblem $watch ([DateTime]::UtcNow)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$aceMod = @{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-split", text, StringComparison.Ordinal);

        // The sentinel must be a banned Admin, not just an account at level 5.
        Assert.Contains("$sentinel = Get-LsSentinelAdmin $cfg", text, StringComparison.Ordinal);
        Assert.Contains("-not $sentinel.Sound", text, StringComparison.Ordinal);
    }
}
