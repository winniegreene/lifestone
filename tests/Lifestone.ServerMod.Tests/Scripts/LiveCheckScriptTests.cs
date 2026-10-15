using System.Globalization;
using System.Text.Json;
using Lifestone.ServerMod.Tests.Support;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// The pieces of the live check (Test-LsServerModLive, m2-plan SM8) and of stop-server.ps1 -AceOnly that need no
/// server: the console replies are read with the wire contract's own format (written here by <see cref="ToolsWire"/>),
/// a boot's log lines are counted, the account rows are parsed, the sentinel's SQL holds no usable password, and
/// pids.json keeps MariaDB when ACE alone stops. The whole check refuses a backend that is not running.
/// </summary>
public sealed class LiveCheckScriptTests
{
    private const string ModManager = "2026-10-12 10:00:01,000 [2] INFO  (ACE.Server.Mods.ModManager) ";

    /// <summary>The shells the scripts must work in.</summary>
    public static TheoryData<string> Shells => PowerShellHost.Shells;

    [Theory]
    [MemberData(nameof(Shells))]
    public void Console_replies_in_the_wire_format_pass_and_wrong_ones_fail(string shell)
    {
        var good = ConsoleLine(Status(autoPromotion: false)) + "\n" + ConsoleLine(Net()) + "\n";
        var promoted = ConsoleLine(Status(autoPromotion: true)) + "\n";
        var failed = ModManager + "Lifestone console: " + ToolsWire.Reply(ToolReply.Failure(null, ToolVerbs.Status, new ToolError(ToolErrors.Failed, "boom")), "boom").Replace("\n", " | ", StringComparison.Ordinal) + "\n";
        var spoof = "2026-10-12 10:00:01,000 [7] INFO  (ACE.Server.Managers.PlayerManager) [CHAT] Bob says, \"" + ConsoleLine(Status(autoPromotion: false)) + "\"\n";
        var script = "$results = [ordered]@{}\n"
            + "foreach ($case in @(@('good', " + PowerShellHost.Quote(good) + "), @('promoted', " + PowerShellHost.Quote(promoted) + "), @('failed', " + PowerShellHost.Quote(failed) + "), @('spoof', " + PowerShellHost.Quote(spoof) + "))) {\n"
            + "  $r = Get-LsConsoleReplies $case[1]\n"
            + "  $results[$case[0]] = @{ Verbs = @($r.Keys | Sort-Object); Status = (Test-LsConsoleReply $r['status'] 'status').Pass; Net = (Test-LsConsoleReply $r['net'] 'net').Pass }\n"
            + "}\n"
            + "Out-Result $results\n";
        var result = PowerShellHost.RunOk(shell, script).Result!.Value;

        Assert.Equal(["net", "status"], result.GetProperty("good").GetProperty("Verbs").EnumerateArray().Select(v => v.GetString()));
        Assert.True(result.GetProperty("good").GetProperty("Status").GetBoolean());
        Assert.True(result.GetProperty("good").GetProperty("Net").GetBoolean());
        Assert.False(result.GetProperty("promoted").GetProperty("Status").GetBoolean());
        Assert.False(result.GetProperty("failed").GetProperty("Status").GetBoolean());
        Assert.False(result.GetProperty("failed").GetProperty("Net").GetBoolean());
        Assert.Empty(result.GetProperty("spoof").GetProperty("Verbs").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void A_boot_log_is_counted_line_by_line(string shell)
    {
        string[] goodBoot =
        [
            ModManager + "Lifestone mod loaded: version 0.1.0, wire 1, ACE 95ef426d",
            "2026-10-12 10:00:01,100 [2] INFO  (ACE.Server.Mods.ModContainer) Enabled mod `Lifestone (v0.1.0)`.",
            ModManager + "Lifestone: ACE's first-account Admin auto-promotion is off; new accounts get DefaultAccessLevel 0 (Player).",
            "2026-10-12 10:00:02,000 [2] INFO  (ACE.Server.Command.CommandManager) Command created: lifestone",
            ModManager + "Displaying mods (1):",
            ModManager + "Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off",
        ];
        string[] badBoot =
        [
            "2026-10-12 10:00:01,000 [2] ERROR (ACE.Server.Mods.ModManager) Lifestone mod refused to load: Config.js Server.Network.Host is 'x'",
            ModManager + "No mods to display.",
            "2026-10-12 10:00:05,000 [9] WARN  (ACE.Server.Network.Handlers.AuthenticationHandler) Automatically setting account AccessLevel to Admin for account \"a\" because there are no admin accounts in the current database.",
        ];
        var script = "Out-Result @{ Good = @(Measure-LsModBootLog @(" + string.Join(", ", goodBoot.Select(PowerShellHost.Quote)) + ")); "
            + "Bad = @(Measure-LsModBootLog @(" + string.Join(", ", badBoot.Select(PowerShellHost.Quote)) + ")) }\n";
        var result = PowerShellHost.RunOk(shell, script).Result!.Value;

        var good = Checks(result.GetProperty("Good"));
        Assert.Equal(7, good.Count);
        Assert.True(good.Values.All(p => p), "a good boot passes every count: " + string.Join(", ", good.Where(c => !c.Value).Select(c => c.Key)));
        var bad = Checks(result.GetProperty("Bad"));
        Assert.Equal(
            ["auto-promotion off once", "command registered", "mod enabled once", "mods listed", "no Admin promotion", "no refusal", "ready once"],
            bad.Where(c => !c.Value).Select(c => c.Key).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void Only_this_boot_s_log_lines_are_read(string shell)
    {
        using var folder = new TempFolder();
        File.WriteAllLines(folder.Combine("ACE_Log.txt.1"), ["2026-10-12 09:00:00,000 [2] INFO  (X) old boot", "2026-10-12 10:00:00,500 [2] INFO  (X) this boot, rolled"]);
        File.WriteAllLines(folder.Combine("ACE_Log.txt"), ["2026-10-12 10:00:01,000 [2] ERROR (X) failed:", "   at Some.Stack.Frame()", "2026-10-12 10:00:02,000 [2] INFO  (X) later"]);
        File.SetLastWriteTimeUtc(folder.Combine("ACE_Log.txt.1"), DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllLines(folder.Combine("ACE_Log-WARN.txt"), ["2026-10-12 10:00:01,000 [2] ERROR (X) a warning copy"]);
        var since = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Local).ToString("o", CultureInfo.InvariantCulture);
        var run = PowerShellHost.RunOk(shell, "$cfg = Get-LsConfig\n$cfg.Logs = " + PowerShellHost.Quote(folder.Path) + "\nOut-Result @(Get-LsAceLogLinesSince $cfg ([DateTime]::Parse('" + since + "', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)))\n");
        Assert.Equal(
            ["2026-10-12 10:00:00,500 [2] INFO  (X) this boot, rolled", "2026-10-12 10:00:01,000 [2] ERROR (X) failed:", "   at Some.Stack.Frame()", "2026-10-12 10:00:02,000 [2] INFO  (X) later"],
            run.Result!.Value.EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void Account_rows_are_read_by_name_and_level(string shell)
    {
        var run = PowerShellHost.RunOk(shell, "Out-Result @(ConvertFrom-LsAccountRows \"lifestone-sentinel`t5`r`nbob`t3`n`nodd`t9\")\n");
        var rows = run.Result!.Value.EnumerateArray().Select(r => (r.GetProperty("Name").GetString(), r.GetProperty("Level").GetInt32(), r.GetProperty("LevelName").GetString())).ToArray();
        Assert.Equal([("lifestone-sentinel", 5, "Admin"), ("bob", 3, "Envoy"), ("odd", 9, "level 9")], rows);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_sentinel_Admin_is_created_with_a_discarded_password_and_a_ban(string shell)
    {
        var run = PowerShellHost.RunOk(shell, "Out-Result @((New-LsSentinelAdminSql 'lifestone-sentinel'), (New-LsSentinelAdminSql 'lifestone-sentinel'))\n");
        var sql = run.Result!.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^INSERT INTO ace_auth\.account \(accountName, passwordHash, passwordSalt, accessLevel, banned_Time, ban_Expire_Time, ban_Reason\) SELECT 'lifestone-sentinel', '(?<hash>[A-Za-z0-9+/]{86}==)', '(?<salt>[A-Za-z0-9+/]{86}==)', 5, UTC_TIMESTAMP\(\), '2099-12-31 00:00:00', '[^']+' FROM DUAL WHERE NOT EXISTS \(SELECT 1 FROM ace_auth\.account WHERE accountName = 'lifestone-sentinel'\);$");
        var first = pattern.Match(sql[0]);
        var second = pattern.Match(sql[1]);
        Assert.True(first.Success, sql[0]);
        Assert.True(second.Success, sql[1]);

        // ACE's SHA-512 form (passwordSalt is not "use bcrypt"); a fresh random password and salt every time, and the
        // password itself appears nowhere.
        Assert.NotEqual(first.Groups["hash"].Value, second.Groups["hash"].Value);
        Assert.NotEqual(first.Groups["salt"].Value, second.Groups["salt"].Value);
        Assert.Equal(64, Convert.FromBase64String(first.Groups["salt"].Value).Length);
        Assert.Equal(64, Convert.FromBase64String(first.Groups["hash"].Value).Length);

        var refused = PowerShellHost.Run(shell, "New-LsSentinelAdminSql \"x'; DROP TABLE account; --\" | Out-Null\n");
        Assert.NotEqual(0, refused.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void An_existing_sentinel_account_gets_a_fresh_discarded_password_and_the_ban_back(string shell)
    {
        var run = PowerShellHost.RunOk(shell, "Out-Result @((New-LsSentinelAdminResetSql 'lifestone-sentinel'), (New-LsSentinelAdminResetSql 'lifestone-sentinel'), (Get-LsSentinelAdminQuery 'lifestone-sentinel'))\n");
        var sql = run.Result!.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^UPDATE ace_auth\.account SET passwordHash = '(?<hash>[A-Za-z0-9+/]{86}==)', passwordSalt = '(?<salt>[A-Za-z0-9+/]{86}==)', accessLevel = 5, banned_Time = UTC_TIMESTAMP\(\), ban_Expire_Time = '2099-12-31 00:00:00', ban_Reason = '(?<reason>[^']+)' WHERE accountName = 'lifestone-sentinel';$");
        var first = pattern.Match(sql[0]);
        var second = pattern.Match(sql[1]);
        Assert.True(first.Success, sql[0]);
        Assert.True(second.Success, sql[1]);
        Assert.NotEqual(first.Groups["hash"].Value, second.Groups["hash"].Value);
        Assert.NotEqual(first.Groups["salt"].Value, second.Groups["salt"].Value);
        Assert.Equal(64, Convert.FromBase64String(first.Groups["salt"].Value).Length);

        // The state query checks the same ban the reset writes.
        Assert.Contains("ban_Expire_Time > UTC_TIMESTAMP()", sql[2], StringComparison.Ordinal);
        Assert.Contains("ban_Reason <=> '" + first.Groups["reason"].Value + "'", sql[2], StringComparison.Ordinal);
        Assert.EndsWith("FROM ace_auth.account WHERE accountName = 'lifestone-sentinel';", sql[2], StringComparison.Ordinal);

        Assert.NotEqual(0, PowerShellHost.Run(shell, "New-LsSentinelAdminResetSql \"x'; DROP TABLE account; --\" | Out-Null\n").ExitCode);
        Assert.NotEqual(0, PowerShellHost.Run(shell, "Get-LsSentinelAdminQuery \"x' OR '1'='1\" | Out-Null\n").ExitCode);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_sentinel_account_row_says_whether_it_is_a_banned_Admin(string shell)
    {
        var run = PowerShellHost.RunOk(shell, "Out-Result @((ConvertFrom-LsSentinelAdminRow \"5`t1`t1`r`n\"), (ConvertFrom-LsSentinelAdminRow \"5`t0`t0\"), (ConvertFrom-LsSentinelAdminRow \"0`t1`t1\"), (ConvertFrom-LsSentinelAdminRow ''))\n");
        var rows = run.Result!.Value.EnumerateArray().ToArray();
        Assert.True(rows[0].GetProperty("Sound").GetBoolean());
        Assert.True(rows[0].GetProperty("SetupBan").GetBoolean());
        Assert.False(rows[1].GetProperty("Sound").GetBoolean());
        Assert.False(rows[1].GetProperty("Banned").GetBoolean());
        Assert.False(rows[2].GetProperty("Sound").GetBoolean());
        Assert.Equal(0, rows[2].GetProperty("Level").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows[3].ValueKind);
        Assert.NotEqual(0, PowerShellHost.Run(shell, "ConvertFrom-LsSentinelAdminRow \"5`t1\" | Out-Null\n").ExitCode);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void Setup_resets_an_existing_sentinel_account_every_time(string shell)
    {
        // Invoke-LsSql is replaced inside the module by a fake ace_auth with one row (or none): no MariaDB runs. A row
        // ACE created at a login (a Player with its own password, no ban) must not become a loginable Admin.
        var run = PowerShellHost.RunOk(shell, """
            & (Get-Module LifestoneServer) {
                $script:FakeRow = ''
                $script:FakeSql = New-Object System.Collections.Generic.List[string]
                $script:FakeIgnoresWrites = $false
                function script:Invoke-LsSql {
                    param($Cfg, $Cnf, $Database, $Sql, $SourceFile, [switch]$AllowFailure, $TimeoutSec)
                    $script:FakeSql.Add($Sql)
                    if ($Sql -like 'SELECT accessLevel, IF(*') { return [pscustomobject]@{ ExitCode = 0; StdOut = $script:FakeRow; StdErr = '' } }
                    if (-not $script:FakeIgnoresWrites -and ($Sql -like 'INSERT INTO ace_auth.account *' -or $Sql -like 'UPDATE ace_auth.account SET *')) { $script:FakeRow = "5`t1`t1`n" }
                    return [pscustomobject]@{ ExitCode = 0; StdOut = ''; StdErr = '' }
                }
            }
            $cfg = Get-LsConfig
            $out = [ordered]@{}
            foreach ($case in @(@('missing', ''), @('autoCreated', "0`t0`t0"), @('levelOnly', "5`t0`t0"), @('otherBan', "5`t1`t0"), @('sound', "5`t1`t1"))) {
                & (Get-Module LifestoneServer) { param($Row) $script:FakeRow = $Row; $script:FakeSql.Clear() } $case[1]
                $outcome = Set-LsSentinelAdmin $cfg
                $writes = @(& (Get-Module LifestoneServer) { @($script:FakeSql | Where-Object { $_ -notlike 'SELECT *' }) })
                $out[$case[0]] = @{ Outcome = $outcome; Writes = $writes }
            }
            & (Get-Module LifestoneServer) { $script:FakeRow = "0`t0`t0"; $script:FakeIgnoresWrites = $true }
            try { Set-LsSentinelAdmin $cfg | Out-Null; $out.unfixed = 'no error' } catch { $out.unfixed = $_.Exception.Message }
            Out-Result $out
            """);
        var result = run.Result!.Value;
        var expected = new Dictionary<string, (string Outcome, string Write)>
        {
            ["missing"] = ("created", "INSERT INTO ace_auth.account "),
            ["autoCreated"] = ("repaired", "UPDATE ace_auth.account SET passwordHash = '"),
            ["levelOnly"] = ("repaired", "UPDATE ace_auth.account SET passwordHash = '"),
            ["otherBan"] = ("repaired", "UPDATE ace_auth.account SET passwordHash = '"),
            ["sound"] = ("present", "UPDATE ace_auth.account SET passwordHash = '"),
        };
        foreach (var (name, (outcome, write)) in expected)
        {
            var one = result.GetProperty(name);
            Assert.Equal(outcome, one.GetProperty("Outcome").GetString());
            var writes = one.GetProperty("Writes").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.True(writes.Length == 1 && writes[0].StartsWith(write, StringComparison.Ordinal), $"{name}: {string.Join(" / ", writes)}");
            Assert.Contains("ban_Expire_Time", writes[0], StringComparison.Ordinal);
        }

        Assert.Equal("The sentinel Admin account 'lifestone-sentinel' is not a banned Admin after setup.", result.GetProperty("unfixed").GetString());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void AceOnly_keeps_MariaDB_in_pids_json(string shell)
    {
        using var folder = new TempFolder();
        var script = "$cfg = Get-LsConfig\n$cfg.PidsFile = " + PowerShellHost.Quote(folder.Combine("pids.json")) + "\n" + """
            $me = Get-Process -Id $PID
            $cfg.MariaDbd = $me.Path
            $record = [ordered]@{ startedUtc = '2026-10-12T10:00:00.0000000Z'; host = '127.0.0.1'; acePorts = @(9000, 9001) }
            $record.mariadbd = New-LsPidEntry $me $me.Path
            $record.ace = New-LsPidEntry $me 'C:\nowhere\ACE.Server.exe' @{ Ready = $true; Version = '0.1.0'; Wire = 1; DllSha256 = 'ab'; ReadyUtc = 'x' }
            Write-LsUtf8File $cfg.PidsFile ($record | ConvertTo-Json -Depth 4)
            Save-LsPidsWithoutAce $cfg
            $kept = [IO.File]::ReadAllText($cfg.PidsFile)
            $record.mariadbd = @{ pid = $PID; path = 'C:\nowhere\mariadbd.exe'; startTime = '2001-01-01T00:00:00.0000000Z' }
            Write-LsUtf8File $cfg.PidsFile ($record | ConvertTo-Json -Depth 4)
            Save-LsPidsWithoutAce $cfg
            Out-Result @{ Kept = $kept; GoneWithoutMariaDb = -not (Test-Path -LiteralPath $cfg.PidsFile); Pid = $PID }
            """;
        var result = PowerShellHost.RunOk(shell, script).Result!.Value;
        using var kept = JsonDocument.Parse(result.GetProperty("Kept").GetString()!);
        Assert.Equal(["startedUtc", "host", "acePorts", "mariadbd"], kept.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-10-12T10:00:00.0000000Z", kept.RootElement.GetProperty("startedUtc").GetString());
        Assert.Equal(result.GetProperty("Pid").GetInt32(), kept.RootElement.GetProperty("mariadbd").GetProperty("pid").GetInt32());
        Assert.True(result.GetProperty("GoneWithoutMariaDb").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_live_check_fails_without_a_running_backend(string shell)
    {
        using var folder = new TempFolder();
        var run = PowerShellHost.RunOk(shell, "$cfg = Get-LsConfig\n$cfg.PidsFile = " + PowerShellHost.Quote(folder.Combine("pids.json")) + "\n"
            + "$cfg.AceExe = " + PowerShellHost.Quote(folder.Combine("ACE.Server.exe")) + "; $cfg.MariaDbd = " + PowerShellHost.Quote(folder.Combine("mariadbd.exe")) + "\n"
            + "Out-Result (Test-LsServerModLive $cfg)\n");
        var result = run.Result!.Value;
        Assert.False(result.GetProperty("Passed").GetBoolean());
        var only = Assert.Single(result.GetProperty("Checks").EnumerateArray());
        Assert.Equal("backend", only.GetProperty("Check").GetString());
        Assert.False(only.GetProperty("Pass").GetBoolean());
    }

    private static Dictionary<string, bool> Checks(JsonElement checks) =>
        checks.EnumerateArray().ToDictionary(c => c.GetProperty("Check").GetString()!, c => c.GetProperty("Pass").GetBoolean());

    private static ToolReply Status(bool autoPromotion) => ToolReply.Success(
        null,
        ToolVerbs.Status,
        new StatusData(1, "0.1.0", "95ef426d412459ff619a12cc18f82bcf72c1c2a8", new string('a', 64), "127.0.0.1", 9000, autoPromotion, "Console", null, null, "Open", ToolsWire.Verbs));

    private static ToolReply Net() => ToolReply.Success(null, ToolVerbs.Net, new NetData(10, 20, 0, 1, 2));

    /// <summary>The line the mod logs for a console command's reply (Reply.cs: the two lines joined by " | ").</summary>
    private static string ConsoleLine(ToolReply reply) =>
        ModManager + "Lifestone console: " + ToolsWire.Reply(reply, reply.Verb).Replace("\n", " | ", StringComparison.Ordinal);
}
