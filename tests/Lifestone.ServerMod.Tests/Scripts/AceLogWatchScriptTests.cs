using System.Security.Cryptography;
using System.Text.Json;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// How start-server.ps1 reads ACE's log while it waits for the mod (server\lib\LifestoneServer.psm1: New-LsAceLogWatch,
/// Read-LsAceLogChunk, Add-LsAceLogText, Get-LsStartedAceMod), without a server: a line cut by the end of a read is
/// judged only once it is whole, a character cut by a read survives, and -AllowNoMod always records modReady false,
/// whatever the log says. Every log here is a temporary file the test writes.
/// </summary>
public sealed class AceLogWatchScriptTests
{
    private const string ModManager = "2026-10-12 10:00:00,123 [7] INFO  (ACE.Server.Mods.ModManager) ";

    private const string ReadyLine = ModManager + "Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off";

    private const string FailureLine = "2026-10-12 10:00:00,123 [2] ERROR (ACE.Server.Mods.ModManager) Lifestone mod refused to load: Config.js Server.Network.Host is 'x'";

    private const string WorldOpenLine = "2026-10-12 10:00:00,100 [2] INFO  (ACE.Server.Managers.PlayerManager) [CHAT][AUDIT] [SYSTEM] says on the Audit channel, \"World is now open\"";

    private const string OtherLine = "2026-10-12 10:00:00,000 [2] INFO  (ACE.Server.Managers.WorldManager) World started";

    /// <summary>The shells the scripts must work in.</summary>
    public static TheoryData<string> Shells => PowerShellHost.Shells;

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_ready_line_cut_by_a_read_counts_only_once_it_is_whole(string shell)
    {
        // The cut is after the ready line's prefix, which alone matches the ready pattern but not the ready line's form.
        var cut = ReadyLine.IndexOf("; wire", StringComparison.Ordinal);
        var run = PowerShellHost.RunOk(shell, $$"""
            $w = New-LsAceLogWatch
            $first = @(Add-LsAceLogText $w ({{PowerShellHost.Quote(OtherLine)}} + "`r`n" + {{PowerShellHost.Quote(ReadyLine[..cut])}}))
            $afterFirst = [string]$w.ReadyLine
            $second = @(Add-LsAceLogText $w ({{PowerShellHost.Quote(ReadyLine[cut..])}} + "`r`n"))
            Out-Result @{
                First = @($first | ForEach-Object { $_.Line }); AfterFirst = $afterFirst
                Second = @($second | ForEach-Object { $_.Signal }); Ready = [string]$w.ReadyLine
                Info = (Get-LsModReadyInfo $w.ReadyLine); Pending = $w.Pending
            }
            """);
        var result = run.Result!.Value;
        Assert.Equal([OtherLine], result.GetProperty("First").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(string.Empty, result.GetProperty("AfterFirst").GetString());
        Assert.Equal(["ModReady"], result.GetProperty("Second").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(ReadyLine, result.GetProperty("Ready").GetString());
        Assert.Equal("0.1.0", result.GetProperty("Info").GetProperty("Version").GetString());
        Assert.Equal(string.Empty, result.GetProperty("Pending").GetString());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_failure_line_cut_by_a_read_is_still_seen(string shell)
    {
        // Cut inside 'refused': neither half matches a failure pattern on its own.
        var cut = FailureLine.IndexOf("refu", StringComparison.Ordinal) + 4;
        var run = PowerShellHost.RunOk(shell, $$"""
            $w = New-LsAceLogWatch
            [void](Add-LsAceLogText $w {{PowerShellHost.Quote(FailureLine[..cut])}})
            $afterFirst = [string]$w.FailureLine
            [void](Add-LsAceLogText $w ({{PowerShellHost.Quote(FailureLine[cut..])}} + "`n"))
            Out-Result @{ AfterFirst = $afterFirst; Failure = [string]$w.FailureLine }
            """);
        var result = run.Result!.Value;
        Assert.Equal(string.Empty, result.GetProperty("AfterFirst").GetString());
        Assert.Equal(FailureLine, result.GetProperty("Failure").GetString());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_last_read_judges_the_pending_text_as_it_is(string shell)
    {
        // When ACE has exited or the wait is over, nothing more will come: the text after the last line break counts.
        var run = PowerShellHost.RunOk(shell, $$"""
            $w = New-LsAceLogWatch $true
            [void](Add-LsAceLogText $w {{PowerShellHost.Quote(WorldOpenLine)}})
            $before = $w.WorldOpen
            $final = @(Add-LsAceLogText $w '' -Final)
            Out-Result @{ Before = $before; After = $w.WorldOpen; Final = @($final | ForEach-Object { $_.Signal }); Pending = $w.Pending }
            """);
        var result = run.Result!.Value;
        Assert.False(result.GetProperty("Before").GetBoolean());
        Assert.True(result.GetProperty("After").GetBoolean());
        Assert.Equal(["WorldOpen"], result.GetProperty("Final").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(string.Empty, result.GetProperty("Pending").GetString());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_log_file_is_read_in_pieces_without_losing_a_line_or_a_character(string shell)
    {
        // ACE appends to its log while start-server reads it: here a chat line's two-byte character and the ready line
        // are both cut by the end of the first read, and the log then rolls over to a new, shorter file.
        using var folder = new TempFolder();
        var log = folder.Combine("ACE_Log.txt");
        var run = PowerShellHost.RunOk(shell, $$"""
            $log = {{PowerShellHost.Quote(log)}}
            $chat = '2026-10-12 10:00:00,050 [7] INFO  (ACE.Server.Managers.PlayerManager) [CHAT] Ren' + [char]0x00E9 + 'e says, "hi"'
            $old = [System.Text.Encoding]::UTF8.GetBytes('2026-10-12 09:00:00,000 [1] INFO  (ACE.Server.Program) an earlier boot' + "`r`n")
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($chat + "`r`n" + {{PowerShellHost.Quote(ReadyLine)}} + "`r`n")
            $cutChar = [System.Text.Encoding]::UTF8.GetByteCount($chat.Substring(0, $chat.IndexOf([char]0x00E9))) + 1
            [System.IO.File]::WriteAllBytes($log, $old)
            $w = New-LsAceLogWatch $false ([long]$old.Length)
            $none = Read-LsAceLogChunk $w $log
            $fs = [System.IO.File]::Open($log, 'Append', 'Write', 'ReadWrite')
            try { $fs.Write($bytes, 0, $cutChar) } finally { $fs.Dispose() }
            $first = @(Add-LsAceLogText $w (Read-LsAceLogChunk $w $log))
            $fs = [System.IO.File]::Open($log, 'Append', 'Write', 'ReadWrite')
            try { $fs.Write($bytes, $cutChar, 40) } finally { $fs.Dispose() }
            $second = @(Add-LsAceLogText $w (Read-LsAceLogChunk $w $log))
            $fs = [System.IO.File]::Open($log, 'Append', 'Write', 'ReadWrite')
            try { $fs.Write($bytes, $cutChar + 40, $bytes.Length - $cutChar - 40) } finally { $fs.Dispose() }
            $third = @(Add-LsAceLogText $w (Read-LsAceLogChunk $w $log))
            $offset = $w.Offset
            [System.IO.File]::AppendAllText($log, 'the start of a line whose end goes to the rolled file')
            [void](Add-LsAceLogText $w (Read-LsAceLogChunk $w $log))
            $beforeRoll = $w.Pending
            [System.IO.File]::WriteAllText($log, ({{PowerShellHost.Quote(WorldOpenLine[..30])}}))
            [void](Add-LsAceLogText $w (Read-LsAceLogChunk $w $log))
            $rolled = $w.Pending
            Out-Result @{
                None = $none; First = $first.Count; Second = @($second | ForEach-Object { $_.Line }); Third = @($third | ForEach-Object { $_.Line })
                Ready = [string]$w.ReadyLine; Expected = $chat; Offset = $offset; Length = ($old.Length + $bytes.Length); BeforeRoll = $beforeRoll; Rolled = $rolled
            }
            """);
        var result = run.Result!.Value;
        Assert.Equal(string.Empty, result.GetProperty("None").GetString());
        Assert.Equal(0, result.GetProperty("First").GetInt32());
        Assert.Equal([result.GetProperty("Expected").GetString()], result.GetProperty("Second").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Renée says", result.GetProperty("Expected").GetString(), StringComparison.Ordinal);
        Assert.Equal([ReadyLine], result.GetProperty("Third").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(ReadyLine, result.GetProperty("Ready").GetString());
        Assert.Equal(result.GetProperty("Length").GetInt32(), result.GetProperty("Offset").GetInt32());

        // The rolled-over file is read from its start, with nothing of the old file's pending text in front.
        Assert.Equal("the start of a line whose end goes to the rolled file", result.GetProperty("BeforeRoll").GetString());
        Assert.Equal(WorldOpenLine[..30], result.GetProperty("Rolled").GetString());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_mod_that_never_reports_fails_the_start_soon_after_the_world_opens(string shell)
    {
        // ACE logs nothing about a mod it never found (Config.js ModsDirectory pointing elsewhere), so the only sign is a
        // world that opened with no ready or failure line after it: start-server stops 30 s later, not at its timeout.
        var run = PowerShellHost.RunOk(shell, $$"""
            $t0 = [DateTime]::new(2026, 10, 12, 10, 0, 0, [DateTimeKind]::Utc)
            function Silence($Watch, [double]$After) { return [string](Get-LsModSilenceProblem $Watch $t0.AddSeconds($After)) }

            $w = New-LsAceLogWatch
            $before = Silence $w 600
            [void](Add-LsAceLogText $w ({{PowerShellHost.Quote(OtherLine)}} + "`n" + {{PowerShellHost.Quote(WorldOpenLine)}} + "`n") -NowUtc $t0)
            [void](Add-LsAceLogText $w ({{PowerShellHost.Quote(WorldOpenLine)}} + "`n") -NowUtc $t0.AddSeconds(20))
            $opened = $w.WorldOpenUtc.ToString('o')

            $ready = New-LsAceLogWatch
            [void](Add-LsAceLogText $ready ({{PowerShellHost.Quote(WorldOpenLine)}} + "`n") -NowUtc $t0)
            [void](Add-LsAceLogText $ready ({{PowerShellHost.Quote(ReadyLine)}} + "`n") -NowUtc $t0.AddSeconds(40))
            $failed = New-LsAceLogWatch
            [void](Add-LsAceLogText $failed ({{PowerShellHost.Quote(WorldOpenLine)}} + "`n" + {{PowerShellHost.Quote(FailureLine)}} + "`n") -NowUtc $t0)
            $noMod = New-LsAceLogWatch $true
            [void](Add-LsAceLogText $noMod ({{PowerShellHost.Quote(WorldOpenLine)}} + "`n") -NowUtc $t0)

            $clock = New-LsAceLogWatch
            $start = [DateTime]::UtcNow
            [void](Add-LsAceLogText $clock ({{PowerShellHost.Quote(WorldOpenLine)}} + "`n"))
            $clockOk = $clock.WorldOpenUtc -ge $start -and $clock.WorldOpenUtc -le [DateTime]::UtcNow -and $clock.WorldOpenUtc.Kind -eq 'Utc'

            Out-Result @{
                Before = $before; Opened = $opened; Early = (Silence $w 29.9); Late = (Silence $w 30); Later = (Silence $w 95)
                Ready = (Silence $ready 600); Failed = (Silence $failed 600); NoMod = (Silence $noMod 600); Clock = $clockOk
            }
            """);
        var result = run.Result!.Value;
        Assert.Equal(string.Empty, result.GetProperty("Before").GetString());
        Assert.Equal("2026-10-12T10:00:00.0000000Z", result.GetProperty("Opened").GetString());
        Assert.Equal(string.Empty, result.GetProperty("Early").GetString());
        Assert.Equal(
            "ACE opened the world 30 s ago but the Lifestone mod never reported (no 'Lifestone mod ready:' or failure line); ACE probably did not load it: check Config.js ModsDirectory and runtime\\ace-mods\\Lifestone.ServerMod",
            result.GetProperty("Late").GetString());
        Assert.StartsWith("ACE opened the world 95 s ago", result.GetProperty("Later").GetString(), StringComparison.Ordinal);

        // A ready mod, a failed one (start-server reports its own line) and an -AllowNoMod start never get this.
        Assert.Equal(string.Empty, result.GetProperty("Ready").GetString());
        Assert.Equal(string.Empty, result.GetProperty("Failed").GetString());
        Assert.Equal(string.Empty, result.GetProperty("NoMod").GetString());
        Assert.True(result.GetProperty("Clock").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void AllowNoMod_records_modReady_false_even_when_the_mod_reports_ready(string shell)
    {
        // An -AllowNoMod start never checked the installed mod (the DLL setup installed, not stale), so neither its ready
        // line nor its install record makes pids.json say ready; a failing mod is noted, not fatal.
        var run = PowerShellHost.RunOk(shell, $$"""
            $cfg = Get-LsConfig
            $w = New-LsAceLogWatch $true
            $entries = @(Add-LsAceLogText $w ({{PowerShellHost.Quote(FailureLine)}} + "`n" + {{PowerShellHost.Quote(WorldOpenLine)}} + "`n" + {{PowerShellHost.Quote(ReadyLine)}} + "`n"))
            $me = Get-Process -Id $PID
            $noInstall = Get-LsStartedAceMod $cfg $w $null
            $withInstall = Get-LsStartedAceMod $cfg $w ([pscustomobject]@{ Version = '0.1.0' })
            Out-Result @{
                Signals = @($entries | ForEach-Object { $_.Signal }); Ready = [string]$w.ReadyLine; Failure = [string]$w.FailureLine
                Ignored = [string]$w.IgnoredReadyLine; WorldOpen = $w.WorldOpen
                NoInstall = (New-LsPidEntry $me 'x' $noInstall); WithInstall = (New-LsPidEntry $me 'x' $withInstall)
            }
            """);
        var result = run.Result!.Value;
        Assert.Equal(["ModFailed", "WorldOpen", "ModReady"], result.GetProperty("Signals").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(string.Empty, result.GetProperty("Ready").GetString());
        Assert.Equal(string.Empty, result.GetProperty("Failure").GetString());
        Assert.Equal(ReadyLine, result.GetProperty("Ignored").GetString());
        Assert.True(result.GetProperty("WorldOpen").GetBoolean());
        foreach (var name in new[] { "NoInstall", "WithInstall" })
        {
            var entry = result.GetProperty(name);
            Assert.Equal(["pid", "path", "startTime", "modReady"], entry.EnumerateObject().Select(p => p.Name));
            Assert.Equal(JsonValueKind.False, entry.GetProperty("modReady").ValueKind);
        }
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_ready_mod_is_recorded_only_from_a_ready_line_that_matches_the_install(string shell)
    {
        using var folder = new TempFolder();
        var dll = folder.Combine("Lifestone.ServerMod.dll");
        File.WriteAllBytes(dll, [1, 2, 3, 4]);
        var run = PowerShellHost.RunOk(shell, $$"""
            $cfg = Get-LsConfig
            $cfg.ModDll = {{PowerShellHost.Quote(dll)}}
            $install = [pscustomobject]@{ Version = '0.1.0' }
            function Get-Started([string]$Text, $Install) {
                $w = New-LsAceLogWatch
                [void](Add-LsAceLogText $w ($Text + "`n"))
                try { return Get-LsStartedAceMod $cfg $w $Install } catch { return "refused: $($_.Exception.Message)" }
            }
            Out-Result @{
                Good = (Get-Started {{PowerShellHost.Quote(ReadyLine)}} $install)
                OtherVersion = (Get-Started {{PowerShellHost.Quote(ReadyLine)}} ([pscustomobject]@{ Version = '0.2.0' }))
                OtherPort = (Get-Started {{PowerShellHost.Quote(ReadyLine.Replace("9000", "9002", StringComparison.Ordinal))}} $install)
                NoLine = (Get-Started {{PowerShellHost.Quote(OtherLine)}} $install)
            }
            """);
        var result = run.Result!.Value;
        var good = result.GetProperty("Good");
        Assert.True(good.GetProperty("Ready").GetBoolean());
        Assert.Equal("0.1.0", good.GetProperty("Version").GetString());
        Assert.Equal(1, good.GetProperty("Wire").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll))), good.GetProperty("DllSha256").GetString());
        Assert.StartsWith("refused: The running Lifestone mod is version 0.1.0, but the installed one is 0.2.0", result.GetProperty("OtherVersion").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("refused: The Lifestone mod reports 127.0.0.1:9002", result.GetProperty("OtherPort").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("refused: The Lifestone mod's ready line was not seen", result.GetProperty("NoLine").GetString(), StringComparison.Ordinal);
    }
}
