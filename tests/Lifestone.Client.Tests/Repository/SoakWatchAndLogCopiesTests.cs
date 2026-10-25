using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M5 item 0 c3: tools/soak-watch.ps1 (the outside loop) parses, takes one sample with <c>-Once</c> and changes nothing but its CSV; the
/// one-log-file wiring (project.godot, tools/godot.ps1, <c>Log</c>) is where the plan puts it. Text tests over the scripts and sources, plus one
/// real PowerShell run of the script; no Godot, no backend, no dats.
/// </summary>
public sealed class SoakWatchAndLogCopiesTests
{
    private static string Script(params string[] parts) => File.ReadAllText(RepositoryFiles.PathOf(parts)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Soak_watch_parses_and_has_its_parameters()
    {
        var path = RepositoryFiles.PathOf("tools", "soak-watch.ps1");
        var result = RunPowerShell(
            "$tokens = $null; $errors = $null; "
            + $"$ast = [System.Management.Automation.Language.Parser]::ParseFile('{path.Replace("'", "''", StringComparison.Ordinal)}', [ref]$tokens, [ref]$errors); "
            + "if ($errors.Count -gt 0) { $errors | ForEach-Object { $_.Message }; exit 1 }; "
            + "($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath }) -join ','");

        foreach (var parameter in new[] { "IntervalSeconds", "DurationSeconds", "Once", "OutFile", "GodotLogsDirectory", "ServerLogsDirectory", "DatabaseDirectory" })
        {
            Assert.Contains(parameter, result.Split(','));
        }

        Assert.Contains("[ValidateRange(1, 86400)] [int] $IntervalSeconds = 300", Script("tools", "soak-watch.ps1"), StringComparison.Ordinal);
    }

    [Fact]
    public void Soak_watch_once_writes_a_header_and_one_row_of_the_documented_columns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lifestone-soak-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var csv = Path.Combine(folder, "watch.csv");
            var empty = Path.Combine(folder, "absent");
            var path = RepositoryFiles.PathOf("tools", "soak-watch.ps1");
            var output = RunPowerShell($"& '{path.Replace("'", "''", StringComparison.Ordinal)}' -Once -OutFile '{csv}' -GodotLogsDirectory '{empty}' -ServerLogsDirectory '{empty}' -DatabaseDirectory '{empty}'");

            var lines = File.ReadAllLines(csv);
            Assert.Equal(2, lines.Length);
            var columns = lines[0].Split(',');
            var values = lines[1].Split(',');
            Assert.Equal(columns.Length, values.Length);
            Assert.Equal(
                ["time_utc", "godot_ws_mb", "godot_private_mb", "godot_handles", "godot_threads", "ace_ws_mb", "ace_private_mb", "ace_handles", "ace_threads", "mariadb_ws_mb", "mariadb_private_mb", "mariadb_handles", "mariadb_threads", "godot_logs_mb", "ace_logs_mb", "db_mb", "free_gb"],
                columns);
            Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", values[0]);
            Assert.Equal(["n/a", "n/a", "n/a"], values[13..16]); // the three folders are absent: n/a, not zero
            Assert.True(double.Parse(values[16], System.Globalization.CultureInfo.InvariantCulture) > 0, "the free disk of the repository's drive"); // always readable
            Assert.Single(Regex.Matches(output, @"\[soak-watch\] "));
            Assert.Contains("db_mb=n/a", output, StringComparison.Ordinal);

            // A second run appends one row and no second header.
            RunPowerShell($"& '{path.Replace("'", "''", StringComparison.Ordinal)}' -Once -OutFile '{csv}' -GodotLogsDirectory '{empty}' -ServerLogsDirectory '{empty}' -DatabaseDirectory '{empty}'");
            Assert.Equal(3, File.ReadAllLines(csv).Length);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Soak_watch_only_reads()
    {
        var script = Script("tools", "soak-watch.ps1");

        // It measures; it never starts, stops, kills, writes outside its CSV, or reads a file's contents.
        foreach (var verb in new[] { "Start-Process", "Stop-Process", "Kill(", "Remove-Item", "Set-Content -LiteralPath $Godot", "Get-Content", "Invoke-Expression", "Start-Service", "Stop-Service", "Register-ScheduledTask", "Set-ItemProperty", "New-NetFirewallRule" })
        {
            Assert.DoesNotContain(verb, script, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(script, @"Add-Content")); // the CSV row
        Assert.Contains("Get-Process -Name $Pattern", script, StringComparison.Ordinal);
        Assert.Contains("'Godot*'", script, StringComparison.Ordinal);
        Assert.Contains("'ACE.Server'", script, StringComparison.Ordinal);
        Assert.Contains("'mariadbd'", script, StringComparison.Ordinal);
        Assert.Contains("[int] $IntervalSeconds = 300", script, StringComparison.Ordinal);
        Assert.Contains("cache\\godot\\roaming\\Godot\\app_userdata\\Lifestone\\logs", script, StringComparison.Ordinal); // the isolated folder tools\godot.ps1 uses
    }

    [Fact]
    public void Godots_own_file_log_is_off_in_the_project_and_back_on_for_all_copies_through_godot_ps1()
    {
        var project = Script("src", "Lifestone.Game", "project.godot");
        Assert.Matches(@"(?m)^\[debug\]\n(?:.*\n)*?file_logging/enable_file_logging=false$", project);
        Assert.Contains("file_logging/enable_file_logging.pc=false", project, StringComparison.Ordinal); // the platform-suffixed variant that would otherwise turn it on

        var godot = Script("tools", "godot.ps1");
        Assert.Contains("--log-copies(=|\\s+)all", godot, StringComparison.Ordinal);
        Assert.Contains("$arguments.Add('--log-file')", godot, StringComparison.Ordinal);
        Assert.Contains("app_userdata\\Lifestone\\logs\\godot.log", godot, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_keeps_every_line_in_the_file_and_the_console_except_for_the_file_only_mode()
    {
        var log = Script("src", "Lifestone.Game", "scripts", "Core", "Log.cs");

        Assert.Contains("_file?.WriteLine(line);", log, StringComparison.Ordinal); // the file has every line in every mode
        Assert.Contains("else if (Copies != Lifestone.Client.Diagnostics.LogCopies.File)", log, StringComparison.Ordinal);
        Assert.Contains("GD.PushError(line);", log, StringComparison.Ordinal); // an error reaches the console in every mode
        Assert.Contains("Copies = Lifestone.Client.Play.PlayOptions.Parse(OS.GetCmdlineUserArgs()).LogCopies;", log, StringComparison.Ordinal);
        Assert.Contains("GodotErrorLogger.Install();", log, StringComparison.Ordinal);
    }

    [Fact]
    public void The_soak_line_node_sits_beside_the_objects_system_and_play_mode_binds_its_sources()
    {
        var layer = Script("src", "Lifestone.Game", "scripts", "World", "Objects", "ObjectsLayer.cs");
        Assert.Contains("new Lifestone.Game.Diagnostics.SoakLine { Name = \"SoakLine\" }", layer, StringComparison.Ordinal);
        Assert.Contains("soak.Configure(options.Play.SoakLineSeconds);", layer, StringComparison.Ordinal);
        Assert.True(layer.IndexOf("world.AddChild(system);", StringComparison.Ordinal) < layer.IndexOf("world.AddChild(soak);", StringComparison.Ordinal));

        var play = Script("src", "Lifestone.Game", "scripts", "Session", "PlayMode.cs");
        Assert.Contains("GetNodeOrNull<Lifestone.Game.Diagnostics.SoakLine>(\"SoakLine\")?.Bind(session, () => mode._avatars, mode._lightTable);", play, StringComparison.Ordinal);

        var soak = Script("src", "Lifestone.Game", "scripts", "Diagnostics", "SoakLine.cs");
        Assert.Contains("catch (Exception ex)", soak, StringComparison.Ordinal); // an exception never reaches the frame
        Assert.Contains("if (!_failureLogged)", soak, StringComparison.Ordinal); // logged once
        Assert.Contains("_builder.Clear();", soak, StringComparison.Ordinal); // one reused builder
    }

    private static string RunPowerShell(string command)
    {
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            var info = new ProcessStartInfo(shell) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(command);
            try
            {
                using var process = Process.Start(info)!;
                var errorTask = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(120_000), "PowerShell did not finish");
                var error = errorTask.Result;
                Assert.True(process.ExitCode == 0, "PowerShell failed: " + error + output);
                return output.Trim();
            }
            catch (Win32Exception)
            {
                // that shell is not installed: try the next
            }
        }

        throw new InvalidOperationException("neither pwsh nor powershell is installed");
    }
}
