using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests.Support;

/// <summary>What one PowerShell run gave back.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StdOut">Everything written to standard output (Write-Host and Write-Output).</param>
/// <param name="StdErr">Everything written to standard error.</param>
/// <param name="Result">The JSON the script wrote with <c>Out-Result</c>, or null.</param>
internal sealed record PowerShellRun(int ExitCode, string StdOut, string StdErr, JsonElement? Result)
{
    /// <summary>Everything the run printed, for assertion messages and for the no-secrets checks.</summary>
    public string AllText => StdOut + "\n" + StdErr;
}

/// <summary>
/// Runs short PowerShell scripts against server\lib\LifestoneServer.psm1 for the script tests: Windows PowerShell 5.1
/// (the scripts must keep working in it, server/README.md) and PowerShell 7 when it is installed. Each run is its own
/// window-less process (-NoProfile -NonInteractive), works only on the temporary files its test made, and never starts
/// a server or touches the network.
/// </summary>
internal static class PowerShellHost
{
    /// <summary>The marker before the JSON line a script writes with <c>Out-Result</c>.</summary>
    private const string ResultMarker = "##lifestone-test-result ";

    private static readonly Lazy<string?> PwshPath = new(FindPwsh);

    /// <summary>The shells to run the script tests in: "powershell" (5.1, always on Windows) and "pwsh" when found.</summary>
    public static TheoryData<string> Shells
    {
        get
        {
            var data = new TheoryData<string> { "powershell" };
            if (PwshPath.Value is not null)
            {
                data.Add("pwsh");
            }

            return data;
        }
    }

    /// <summary>The module under test, in this checkout.</summary>
    public static string ModulePath => RepositoryFiles.PathOf("server", "lib", "LifestoneServer.psm1");

    /// <summary>
    /// Runs <paramref name="script"/> after importing the module with $ErrorActionPreference Stop. The script may call
    /// <c>Out-Result $value</c> once to hand a value back as JSON.
    /// </summary>
    public static PowerShellRun Run(string shell, string script, int timeoutSeconds = 120)
    {
        var header = string.Join(
            "\n",
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "Set-StrictMode -Version 2.0",
            "Import-Module " + Quote(ModulePath) + " -Force -DisableNameChecking",
            "function Out-Result($Value) { Write-Output ('" + ResultMarker + "' + (ConvertTo-Json -InputObject $Value -Depth 8 -Compress)) }",
            string.Empty);
        var start = new ProcessStartInfo(Executable(shell))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // A test run started from PowerShell 7 (tools\smoke.ps1) inherits its PSModulePath, which puts PowerShell 7's
        // modules first; Windows PowerShell 5.1 then fails to load its own Microsoft.PowerShell.Utility (Get-FileHash is
        // "not recognized"). Each shell builds its default module path when the variable is absent.
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-EncodedCommand" })
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + header + script)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("could not start " + shell);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{shell} did not finish within {timeoutSeconds} s");
        }

        process.WaitForExit();
        var output = stdout.Result;
        JsonElement? result = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith(ResultMarker, StringComparison.Ordinal))
            {
                result = JsonDocument.Parse(line[ResultMarker.Length..].Trim()).RootElement.Clone();
            }
        }

        return new PowerShellRun(process.ExitCode, output, stderr.Result, result);
    }

    /// <summary>Runs the script and asserts that it succeeded; returns the run.</summary>
    public static PowerShellRun RunOk(string shell, string script, int timeoutSeconds = 120)
    {
        var run = Run(shell, script, timeoutSeconds);
        Assert.True(run.ExitCode == 0, $"{shell} exited {run.ExitCode}:\n{run.AllText}");
        return run;
    }

    /// <summary>A PowerShell single-quoted string literal.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Executable(string shell) => shell switch
    {
        "powershell" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        "pwsh" => PwshPath.Value ?? throw new InvalidOperationException("pwsh is not installed"),
        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "powershell or pwsh"),
    };

    private static string? FindPwsh()
    {
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(dir, "pwsh.exe"))
            .FirstOrDefault(File.Exists);
        if (onPath is not null)
        {
            return onPath;
        }

        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        return File.Exists(installed) ? installed : null;
    }
}

/// <summary>A temporary folder for one test's files, deleted afterwards (secrets.json copies in it are test values only).</summary>
internal sealed class TempFolder : IDisposable
{
    /// <summary>Creates an empty folder under the system temporary folder.</summary>
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lifestone-servermod-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder.</summary>
    public string Path { get; }

    /// <summary>A path inside the folder.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Deletes the folder and everything in it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A process the test started may still hold a file for a moment; the system temporary folder is cleaned anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
