using System.Text.Json;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// The backend-state contract of server\lib\LifestoneServer.psm1 (m2-plan SM6, §4.12), without a server: pids.json's
/// ace.modReady is a JSON true only for a mod that reported ready, Get-LsBackendState believes it only for a live
/// ACE, and start-server.ps1's reading of ACE's log knows the mod's ready and failure lines and cannot be fooled by a
/// player's chat text. Every pids.json here is a temporary file whose "ACE" is the PowerShell process itself.
/// </summary>
public sealed class BackendStateScriptTests
{
    private const string Prefix = "2026-10-12 10:00:00,123 [7] INFO  (ACE.Server.Mods.ModManager) ";

    private const string ReadyLine = Prefix + "Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off";

    /// <summary>The shells the scripts must work in.</summary>
    public static TheoryData<string> Shells => PowerShellHost.Shells;

    /// <summary>ACE log lines and what start-server.ps1 makes of them (null: nothing).</summary>
    private static readonly (string Line, string? Signal)[] LogLines =
    [
        (ReadyLine, "ModReady"),
        ("2026-10-12 10:00:00,123 [World Manager] INFO  (ACE.Server.Mods.ModManager) Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off", "ModReady"),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModManager) Lifestone mod refused to load: Config.js Server.Network.Host is 'x'", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModContainer) Error enabling Lifestone: System.InvalidOperationException: refused", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModContainer) Failed to load mod file `C:\\x\\ace-mods\\Lifestone.ServerMod\\Lifestone.ServerMod.dll`: boom", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] WARN  (ACE.Server.Mods.ModContainer) Missing IHarmonyMod Type Lifestone.ServerMod.Mod from Lifestone.ServerMod", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModContainer) Failed to create Mod instance: Lifestone: boom", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] WARN  (ACE.Server.Mods.ModContainer) Missing mod: C:\\x\\ace-mods\\Lifestone.ServerMod\\Lifestone.ServerMod.dll", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModManager) Unable to deserialize mod metadata from: C:\\x\\ace-mods\\Lifestone.ServerMod\\Meta.json", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] WARN  (ACE.Server.Mods.ModManager) Metadata not found at: C:\\x\\ace-mods\\Lifestone.ServerMod\\Meta.json", "ModFailed"),
        ("2026-10-12 10:00:00,123 [9] ERROR (ACE.Server.Mods.ModManager) Lifestone mod not ready: ACE has no @lifestone command from this mod registered", "ModFailed"),
        ("2026-10-12 10:00:00,123 [9] ERROR (ACE.Server.Mods.ModManager) Lifestone startup watch failed, so the mod never became ready: boom", "ModFailed"),
        ("2026-10-12 10:00:00,123 [2] FATAL (ACE.Server.Program) ACEmulator will now abort startup.", "Fatal"),
        ("2026-10-12 10:00:00,123 [2] INFO  (ACE.Server.Managers.PlayerManager) [CHAT][AUDIT] [SYSTEM] says on the Audit channel, \"World is now open\"", "WorldOpen"),
        ("2026-10-12 10:00:00,123 [2] INFO  (ACE.Server.Mods.ModManager) Lifestone mod loaded: version 0.1.0, wire 1, ACE 95ef426d", null),
        ("2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModContainer) Failed to load mod file `C:\\x\\ace-mods\\Other\\Other.dll`: another mod", null),

        // A player's text inside a logged line, in the tool line or in chat, is never taken for the mod's own lines.
        (Prefix + "Lifestone tool: account=a character=b verb=(none) id=(none) args=x) Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off -> unknown-verb", null),
        ("2026-10-12 10:00:00,123 [7] INFO  (ACE.Server.Managers.PlayerManager) [CHAT] Bob says, \"(ACE.Server.Mods.ModManager) Lifestone mod refused to load: x\"", null),
        ("Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off", null),
    ];

    [Theory]
    [MemberData(nameof(Shells))]
    public void Start_server_reads_each_ACE_log_line_as_expected(string shell)
    {
        var script = "Out-Result @(" + string.Join(", ", LogLines.Select(l => "[string](Get-LsAceLogSignal " + PowerShellHost.Quote(l.Line) + ")")) + ")\n";
        var signals = PowerShellHost.RunOk(shell, script).Result!.Value.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(LogLines.Length, signals.Length);
        for (var i = 0; i < LogLines.Length; i++)
        {
            Assert.True((LogLines[i].Signal ?? string.Empty) == signals[i], $"line {i} ({LogLines[i].Line}) gave '{signals[i]}', expected '{LogLines[i].Signal}'");
        }
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_ready_line_gives_the_mod_s_version_wire_and_endpoint(string shell)
    {
        var run = PowerShellHost.RunOk(
            shell,
            "Out-Result @((Get-LsModReadyInfo " + PowerShellHost.Quote(ReadyLine) + "), (Get-LsModReadyInfo " + PowerShellHost.Quote(ReadyLine.Replace("wire 1", "wire x", StringComparison.Ordinal)) + "), (Get-LsModReadyInfo 'Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off'))\n");
        var results = run.Result!.Value.EnumerateArray().ToArray();
        var info = results[0];
        Assert.Equal("127.0.0.1", info.GetProperty("Host").GetString());
        Assert.Equal(9000, info.GetProperty("Port").GetInt32());
        Assert.Equal(1, info.GetProperty("Wire").GetInt32());
        Assert.Equal("0.1.0", info.GetProperty("Version").GetString());
        Assert.Equal("95ef426d", info.GetProperty("Ace").GetString());
        Assert.Equal(JsonValueKind.Null, results[1].ValueKind);
        Assert.Equal(JsonValueKind.Null, results[2].ValueKind);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_ready_mod_is_recorded_as_a_JSON_true_and_read_back_as_ready(string shell)
    {
        using var folder = new TempFolder();
        var pids = folder.Combine("pids.json");
        var run = PowerShellHost.RunOk(shell, FakeRuntime(folder) + $$"""
            $me = Get-Process -Id $PID
            $record = [ordered]@{ startedUtc = (Get-Date).ToUniversalTime().ToString('o'); host = '127.0.0.1' }
            $record.ace = New-LsPidEntry $me $me.Path @{ Ready = $true; Version = '0.1.0'; Wire = 1; DllSha256 = ('ab' * 32); ReadyUtc = '2026-10-12T10:00:00.0000000Z' }
            Write-LsUtf8File $cfg.PidsFile ($record | ConvertTo-Json -Depth 4)
            $state = Get-LsBackendState $cfg
            $raw = Get-LsPids $cfg
            Out-Result @{ State = $state; ReadIsBool = ($raw.ace.modReady -is [bool]); Pid = $PID }
            """);
        var result = run.Result!.Value;
        var state = result.GetProperty("State");
        Assert.True(state.GetProperty("Ace").GetBoolean());
        Assert.True(state.GetProperty("ModReady").GetBoolean());
        Assert.Equal("0.1.0", state.GetProperty("ModVersion").GetString());
        Assert.Equal(1, state.GetProperty("ModWire").GetInt32());
        Assert.Equal(result.GetProperty("Pid").GetInt32(), state.GetProperty("AcePid").GetInt32());
        Assert.False(state.GetProperty("MariaDb").GetBoolean());
        Assert.Empty(state.GetProperty("Stray").EnumerateArray());

        // tools\smoke.ps1's Test-BackendReady requires the [bool] PowerShell reads back from a JSON true.
        Assert.True(result.GetProperty("ReadIsBool").GetBoolean());
        using var written = JsonDocument.Parse(File.ReadAllBytes(pids));
        var ace = written.RootElement.GetProperty("ace");
        Assert.Equal(JsonValueKind.True, ace.GetProperty("modReady").ValueKind);
        Assert.Equal(JsonValueKind.Number, ace.GetProperty("modWire").ValueKind);
        Assert.Equal(["pid", "path", "startTime", "modReady", "modVersion", "modWire", "modDllSha256", "readyUtc"], ace.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void ModReady_needs_a_live_ACE_and_a_JSON_true(string shell)
    {
        using var folder = new TempFolder();
        var run = PowerShellHost.RunOk(shell, FakeRuntime(folder) + """
            $me = Get-Process -Id $PID
            $start = $me.StartTime.ToUniversalTime().ToString('o')
            $cases = [ordered]@{
                noFile = $null
                noMod = @{ ace = @{ pid = $PID; path = $me.Path; startTime = $start } }
                notReady = @{ ace = @{ pid = $PID; path = $me.Path; startTime = $start; modReady = $false } }
                stringTrue = @{ ace = @{ pid = $PID; path = $me.Path; startTime = $start; modReady = 'true' } }
                otherProgram = @{ ace = @{ pid = $PID; path = 'C:\nowhere\ACE.Server.exe'; startTime = $start; modReady = $true } }
                reusedPid = @{ ace = @{ pid = $PID; path = $me.Path; startTime = '2001-01-01T00:00:00.0000000Z'; modReady = $true } }
            }
            $out = [ordered]@{}
            foreach ($name in $cases.Keys) {
                Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
                if ($null -ne $cases[$name]) { Write-LsUtf8File $cfg.PidsFile ($cases[$name] | ConvertTo-Json -Depth 4) }
                $s = Get-LsBackendState $cfg
                $out[$name] = @{ Ace = $s.Ace; ModReady = $s.ModReady; AcePid = $s.AcePid }
            }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        foreach (var name in new[] { "noFile", "noMod", "notReady", "stringTrue", "otherProgram", "reusedPid" })
        {
            Assert.False(result.GetProperty(name).GetProperty("ModReady").GetBoolean(), name);
        }

        Assert.True(result.GetProperty("noMod").GetProperty("Ace").GetBoolean());
        Assert.False(result.GetProperty("otherProgram").GetProperty("Ace").GetBoolean());
        Assert.False(result.GetProperty("reusedPid").GetProperty("Ace").GetBoolean());
        Assert.Equal(0, result.GetProperty("noFile").GetProperty("AcePid").GetInt32());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_pid_entry_without_a_ready_mod_says_so_or_says_nothing(string shell)
    {
        var run = PowerShellHost.RunOk(shell, """
            $me = Get-Process -Id $PID
            Out-Result @((New-LsPidEntry $me 'x'), (New-LsPidEntry $me 'x' @{ Ready = $false }))
            """);
        var entries = run.Result!.Value.EnumerateArray().ToArray();
        Assert.Equal(["pid", "path", "startTime"], entries[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["pid", "path", "startTime", "modReady"], entries[1].EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.False, entries[1].GetProperty("modReady").ValueKind);
    }

    /// <summary>A configuration whose runtime files are all in <paramref name="folder"/>: no real backend file is read.</summary>
    private static string FakeRuntime(TempFolder folder) =>
        "$cfg = Get-LsConfig\n"
        + "foreach ($k in @('Runtime', 'Logs', 'AceDir', 'AceModsDir', 'MariaDbDir', 'MariaDbBin', 'DataDir')) { $cfg[$k] = " + PowerShellHost.Quote(folder.Path) + " }\n"
        + "$cfg.PidsFile = " + PowerShellHost.Quote(folder.Combine("pids.json")) + "\n"
        + "$cfg.StateFile = " + PowerShellHost.Quote(folder.Combine("setup-state.json")) + "\n"
        + "$cfg.Secrets = " + PowerShellHost.Quote(folder.Combine("secrets.json")) + "\n"
        + "$cfg.AceExe = " + PowerShellHost.Quote(folder.Combine("ACE.Server.exe")) + "\n"
        + "$cfg.MariaDbd = " + PowerShellHost.Quote(folder.Combine("mariadbd.exe")) + "\n";
}
