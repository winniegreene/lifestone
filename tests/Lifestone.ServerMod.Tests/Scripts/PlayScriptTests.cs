using System.Diagnostics;
using System.Text;
using Lifestone.Client.Tests;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// tools\play.ps1's decisions (m2-plan L18, owner decisions D10 and D13) without a backend, a game or a window:
/// tools\lib\Play.psm1's pure functions (what to do about the backend, what to stop afterwards, which game arguments are
/// allowed), godot.ps1's argument splitting, and play.ps1's refusals, which happen before it looks for the backend.
/// </summary>
public sealed class PlayScriptTests
{
    /// <summary>A stand-in for a value that must never be echoed back by a problem: a made-up "account" that looks like a password.</summary>
    private const string Secretish = "Zx9-not-a-real-secret-value";

    public static TheoryData<string> Shells => PowerShellHost.Shells;

    private static string Import =>
        "Import-Module " + PowerShellHost.Quote(RepositoryFiles.PathOf("tools", "lib", "Play.psm1")) + " -Force -DisableNameChecking\n";

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_backend_plan_uses_a_ready_ACE_starts_a_missing_one_and_refuses_one_without_its_mod(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + """
            function State($db, $ace, $mod) { [pscustomobject]@{ MariaDb = $db; Ace = $ace; ModReady = $mod; AcePid = $(if ($ace) { 1234 } else { 0 }) } }
            $out = [ordered]@{
                ready = Get-PlayBackendPlan (State $true $true $true)
                downAll = Get-PlayBackendPlan (State $false $false $false)
                dbOnly = Get-PlayBackendPlan (State $true $false $false)
                noMod = Get-PlayBackendPlan (State $true $true $false)
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        Assert.Equal("use", result.GetProperty("ready").GetProperty("Action").GetString());
        Assert.Equal("start", result.GetProperty("downAll").GetProperty("Action").GetString());
        Assert.False(result.GetProperty("downAll").GetProperty("MariaDbRunning").GetBoolean());
        Assert.Equal("start", result.GetProperty("dbOnly").GetProperty("Action").GetString());
        Assert.True(result.GetProperty("dbOnly").GetProperty("MariaDbRunning").GetBoolean());
        var refused = result.GetProperty("noMod");
        Assert.Equal("refuse", refused.GetProperty("Action").GetString());
        Assert.Contains("modReady", refused.GetProperty("Reason").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void It_owns_what_came_up_during_its_start_also_when_the_start_was_cut_short(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + """
            $down = [pscustomobject]@{ MariaDb = $false; Ace = $false }
            $dbUp = [pscustomobject]@{ MariaDb = $true; Ace = $false }
            $allUp = [pscustomobject]@{ MariaDb = $true; Ace = $true }
            $out = [ordered]@{
                both = Get-PlayStartedByUs -Before $down -After $allUp
                aceOnly = Get-PlayStartedByUs -Before $dbUp -After $allUp
                dbOnly = Get-PlayStartedByUs -Before $down -After $dbUp
                nothing = Get-PlayStartedByUs -Before $down -After $down
                already = Get-PlayStartedByUs -Before $allUp -After $allUp
                unreadable = Get-PlayStartedByUs -Before $down -After $null
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        (bool Db, bool Ace) Owned(string name) => (result.GetProperty(name).GetProperty("MariaDb").GetBoolean(), result.GetProperty(name).GetProperty("Ace").GetBoolean());
        Assert.Equal((true, true), Owned("both"));
        Assert.Equal((false, true), Owned("aceOnly"));
        Assert.Equal((true, false), Owned("dbOnly"));
        Assert.Equal((false, false), Owned("nothing"));
        Assert.Equal((false, false), Owned("already"));
        Assert.Equal((false, false), Owned("unreadable"));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void It_stops_only_what_it_started_and_keeps_it_with_KeepServer(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + """
            $out = [ordered]@{
                nothing = Get-PlayStopPlan -StartedMariaDb $false -StartedAce $false
                both = Get-PlayStopPlan -StartedMariaDb $true -StartedAce $true
                aceOnly = Get-PlayStopPlan -StartedMariaDb $false -StartedAce $true
                dbOnly = Get-PlayStopPlan -StartedMariaDb $true -StartedAce $false
                kept = Get-PlayStopPlan -StartedMariaDb $true -StartedAce $true -KeepServer
                keptNothing = Get-PlayStopPlan -StartedMariaDb $false -StartedAce $false -KeepServer
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        string Action(string name) => result.GetProperty(name).GetProperty("Action").GetString()!;
        Assert.Equal("none", Action("nothing"));
        Assert.Equal("all", Action("both"));
        Assert.Equal("aceonly", Action("aceOnly"));
        Assert.Equal("leave-mariadb", Action("dbOnly"));
        Assert.Equal("none", Action("kept"));
        Assert.Equal("none", Action("keptNothing"));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_game_gets_play_and_names_only(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + """
            $out = [ordered]@{
                plain = Get-PlayGameOptions
                named = Get-PlayGameOptions -Account 'alt-1' -Character 'Amy'
                auto = Get-PlayGameOptions -Offscreen
                autoTest = Get-PlayGameOptions -Offscreen -Account 'test'
                spaced = Get-PlayGameOptions -Offscreen -Character 'Lifestone Smoke' -GameArgs '--quit-after=50 --record-session'
                quoted = Get-PlayGameOptions -GameArgs '--start="Holtburg Town" --log-level=debug'
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        string[] Arguments(string name) => [.. result.GetProperty(name).GetProperty("Arguments").EnumerateArray().Select(e => e.GetString()!)];
        string[] Problems(string name) => [.. result.GetProperty(name).GetProperty("Problems").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Equal(["--play"], Arguments("plain"));
        Assert.Equal(["--play", "--account=alt-1", "--character=Amy"], Arguments("named"));
        Assert.Equal(["--play", "--account=test"], Arguments("auto"));
        Assert.Equal(["--play", "--account=test"], Arguments("autoTest"));
        Assert.Equal(["--play", "--account=test", "--character=\"Lifestone Smoke\"", "--quit-after=50", "--record-session"], Arguments("spaced"));
        Assert.Equal("--play --account=test --character=\"Lifestone Smoke\" --quit-after=50 --record-session", result.GetProperty("spaced").GetProperty("GameArgs").GetString());
        Assert.Equal(["--play", "--start=Holtburg Town", "--log-level=debug"], Arguments("quoted"));
        Assert.Equal("--play \"--start=Holtburg Town\" --log-level=debug", result.GetProperty("quoted").GetProperty("GameArgs").GetString());
        Assert.All(new[] { "plain", "named", "auto", "autoTest", "spaced", "quoted" }, name => Assert.Empty(Problems(name)));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void An_automated_run_never_uses_the_play_account_and_a_value_is_never_echoed(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + $$"""
            $out = [ordered]@{
                play = Get-PlayGameOptions -Offscreen -Account 'play'
                other = Get-PlayGameOptions -Offscreen -Account 'lifestone'
                spaces = Get-PlayGameOptions -Account 'two words'
                equals = Get-PlayGameOptions -Account 'a=b'
                quote = Get-PlayGameOptions -Account '"x"'
                long = Get-PlayGameOptions -Account ('a' * 51)
                secretish = Get-PlayGameOptions -Account '{{Secretish}}!'
                character = Get-PlayGameOptions -Character '--play'
                digits = Get-PlayGameOptions -Character '9Amy'
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        foreach (var name in new[] { "play", "other", "spaces", "equals", "quote", "long", "secretish", "character", "digits" })
        {
            var problems = result.GetProperty(name).GetProperty("Problems").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.True(problems.Length > 0, name + " was accepted");
            Assert.DoesNotContain(problems, p => p.Contains(Secretish, StringComparison.Ordinal));
            Assert.DoesNotContain(problems, p => p.Contains("two words", StringComparison.Ordinal));
            Assert.DoesNotContain(result.GetProperty(name).GetProperty("Arguments").EnumerateArray().Select(e => e.GetString()!), a => a.StartsWith("--account=", StringComparison.Ordinal) && name != "play" && name != "other");
        }

    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void GameArgs_cannot_carry_what_the_script_owns_or_a_password(string shell)
    {
        var run = PowerShellHost.RunOk(shell, Import + """
            $cases = [ordered]@{
                account = '--account=play'
                play = '--play'
                character = '--character=Bo'
                server = '--server=elsewhere:9000'
                password = '--password=hunter2'
                passwd = '--db-passwd hunter2'
                secret = '--secret=x'
                token = '--api-token=abc'
                unbalanced = '--start="Holtburg'
                fine = '--log-level=debug --quit-after=10'
            }
            $out = [ordered]@{}
            foreach ($key in $cases.Keys) { $o = Get-PlayGameOptions -Offscreen -GameArgs $cases[$key]; $out[$key] = @{ Problems = $o.Problems; Arguments = $o.Arguments } }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        foreach (var name in new[] { "account", "play", "character", "server", "password", "passwd", "secret", "token", "unbalanced" })
        {
            var entry = result.GetProperty(name);
            Assert.True(entry.GetProperty("Problems").GetArrayLength() > 0, name + " was accepted");
            var text = string.Join("\n", entry.GetProperty("Problems").EnumerateArray().Select(e => e.GetString()));
            Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        }

        Assert.Equal(0, result.GetProperty("fine").GetProperty("Problems").GetArrayLength());
        Assert.Equal(["--play", "--account=test", "--log-level=debug", "--quit-after=10"], result.GetProperty("fine").GetProperty("Arguments").EnumerateArray().Select(e => e.GetString()!));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void Only_a_loopback_backend_is_played_against(string shell)
    {
        var versions = RepositoryFiles.PathOf("server", "versions.json");
        var run = PowerShellHost.RunOk(shell, Import + $$"""
            $real = Get-Content -LiteralPath {{PowerShellHost.Quote(versions)}} -Raw | ConvertFrom-Json
            $threw = @()
            try { Assert-PlayLoopback $real; $okReal = $true } catch { $okReal = $false }
            foreach ($bad in @('any-address', 'localhost', 'lan-machine', '')) {
                $v = [pscustomobject]@{ network = [pscustomobject]@{ host = $bad } }
                try { Assert-PlayLoopback $v; $threw += $false } catch { $threw += $true }
            }
            try { Assert-PlayLoopback ([pscustomobject]@{}); $threw += $false } catch { $threw += $true }
            Out-Result @{ OkReal = $okReal; Threw = $threw }
            """);
        var result = run.Result!.Value;
        Assert.True(result.GetProperty("OkReal").GetBoolean());
        Assert.All(result.GetProperty("Threw").EnumerateArray(), e => Assert.True(e.GetBoolean()));
    }

    /// <summary>dod-1-2-12-13-5: the game is told the backend's port from versions.json, as --server=127.0.0.1:PORT.</summary>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_game_is_told_the_backends_port_from_versions_json(string shell)
    {
        var versions = RepositoryFiles.PathOf("server", "versions.json");
        var run = PowerShellHost.RunOk(shell, Import + $$"""
            $real = Get-Content -LiteralPath {{PowerShellHost.Quote(versions)}} -Raw | ConvertFrom-Json
            $threw = @()
            foreach ($bad in @(0, 70000, -1)) {
                try { $null = Get-PlayServerEndpoint ([pscustomobject]@{ network = [pscustomobject]@{ host = '127.0.0.1'; acePort = $bad } }); $threw += $false } catch { $threw += $true }
            }
            try { $null = Get-PlayServerEndpoint ([pscustomobject]@{}); $threw += $false } catch { $threw += $true }
            $changed = Get-PlayServerEndpoint ([pscustomobject]@{ network = [pscustomobject]@{ host = '127.0.0.1'; acePort = 9100 } })
            $endpoint = Get-PlayServerEndpoint $real
            Out-Result @{
                Port = [int]$real.network.acePort
                Endpoint = $endpoint
                Changed = $changed
                Threw = $threw
                Real = Get-PlayGameOptions -Offscreen -Character 'Amy' -Server $endpoint
                Bad = Get-PlayGameOptions -Server 'example.test:9000'
                Other = Get-PlayGameOptions -Server '192.0.2.1:9000'
            }
            """);
        var result = run.Result!.Value;
        var port = result.GetProperty("Port").GetInt32();
        Assert.Equal($"127.0.0.1:{port}", result.GetProperty("Endpoint").GetString());
        Assert.Equal("127.0.0.1:9100", result.GetProperty("Changed").GetString());
        Assert.All(result.GetProperty("Threw").EnumerateArray(), e => Assert.True(e.GetBoolean()));
        Assert.Equal(
            ["--play", "--account=test", $"--server=127.0.0.1:{port}", "--character=Amy"],
            result.GetProperty("Real").GetProperty("Arguments").EnumerateArray().Select(e => e.GetString()!));
        Assert.NotEmpty(result.GetProperty("Bad").GetProperty("Problems").EnumerateArray());
        Assert.NotEmpty(result.GetProperty("Other").GetProperty("Problems").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void Game_arguments_split_on_whitespace_except_inside_double_quotes(string shell)
    {
        var run = PowerShellHost.RunOk(shell, "Import-Module " + PowerShellHost.Quote(RepositoryFiles.PathOf("tools", "lib", "Toolchain.psm1")) + " -Force -DisableNameChecking\n" + """
            $out = [ordered]@{
                empty = @(Split-LifestoneArguments '')
                plain = @(Split-LifestoneArguments '--a=1  --b   --c=x')
                quoted = @(Split-LifestoneArguments '--character="Lifestone Smoke" --b')
                whole = @(Split-LifestoneArguments '"--x=a b" c')
            }
            try { Split-LifestoneArguments '--x="oops'; $out.odd = 'no error' } catch { $out.odd = 'error' }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        string[] Of(string name) => [.. result.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];
        Assert.Empty(Of("empty"));
        Assert.Equal(["--a=1", "--b", "--c=x"], Of("plain"));
        Assert.Equal(["--character=Lifestone Smoke", "--b"], Of("quoted"));
        Assert.Equal(["--x=a b", "c"], Of("whole"));
        Assert.Equal("error", result.GetProperty("odd").GetString());
    }

    [Fact]
    public void Play_ps1_refuses_the_play_account_in_an_automated_run_before_it_looks_for_a_backend()
    {
        var (code, text) = RunPlay("-PlanOnly", "-Offscreen", "-Account", "play");
        Assert.Equal(1, code);
        Assert.Contains("REFUSED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Play_ps1_refuses_a_password_option_and_an_account_in_GameArgs()
    {
        var (code, text) = RunPlay("-PlanOnly", "-GameArgs:--password=hunter2 --account=play");
        Assert.Equal(1, code);
        Assert.Contains("REFUSED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_has_the_parameters_the_plan_names_and_never_touches_secrets()
    {
        var script = File.ReadAllText(RepositoryFiles.PathOf("tools", "play.ps1"));
        var module = File.ReadAllText(RepositoryFiles.PathOf("tools", "lib", "Play.psm1"));
        foreach (var parameter in new[] { "$Account", "$Character", "$KeepServer", "$Offscreen", "$IsolateUserData", "$TimeoutSeconds", "$GameArgs", "$NoBuild", "$PlanOnly" })
        {
            Assert.Contains(parameter, script, StringComparison.Ordinal);
        }

        // L18: it reads the backend state from the main checkout's module and stops with -AceOnly when MariaDB ran before.
        Assert.Contains("Get-LsBackendState", script, StringComparison.Ordinal);
        Assert.Contains("'-AceOnly'", script, StringComparison.Ordinal);
        Assert.Contains("start-server.ps1", script, StringComparison.Ordinal);
        Assert.Contains("--play", module, StringComparison.Ordinal);

        // Never a password: neither file reads or names the secrets file's contents, and neither builds a password argument.
        foreach (var text in new[] { script, module })
        {
            Assert.DoesNotContain("Get-LsSecrets", text, StringComparison.Ordinal);
            Assert.DoesNotContain("$cfg.Secrets", text, StringComparison.Ordinal);
            Assert.DoesNotContain("--password=$", text, StringComparison.Ordinal);
            Assert.DoesNotContain("-Password", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Godot_ps1_splits_its_argument_strings_with_the_shared_splitter()
    {
        var text = File.ReadAllText(RepositoryFiles.PathOf("tools", "godot.ps1"));
        Assert.Contains("Split-LifestoneArguments $GameArgs", text, StringComparison.Ordinal);
        Assert.Contains("Split-LifestoneArguments $GodotArgs", text, StringComparison.Ordinal);
    }

    /// <summary>Runs play.ps1 in PowerShell 7 (it needs it) and returns its exit code and output; skips quietly into a failure message when pwsh is missing.</summary>
    private static (int Code, string Text) RunPlay(params string[] arguments)
    {
        var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (!File.Exists(pwsh))
        {
            pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Select(d => Path.Combine(d, "pwsh.exe")).FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("PowerShell 7 (pwsh) is needed to run tools\\play.ps1");
        }

        var start = new ProcessStartInfo(pwsh)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", RepositoryFiles.PathOf("tools", "play.ps1") }.Concat(arguments))
        {
            start.ArgumentList.Add(a);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("play.ps1 did not finish within 120 s");
        }

        process.WaitForExit();
        return (process.ExitCode, stdout.Result + "\n" + stderr.Result);
    }
}
