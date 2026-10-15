using System.Text.Json;
using Lifestone.Client.Tests;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>A fact that needs the main checkout's set-up ACE build (read only); skipped when there is none.</summary>
internal sealed class RequiresAceBuildFactAttribute : FactAttribute
{
    public RequiresAceBuildFactAttribute()
    {
        if (!File.Exists(Path.Combine(ModBuilds.Runtime, "ace", "ACE.Server.dll")) || ModBuilds.AceServerDllSha256 is null)
        {
            Skip = $"no set-up ACE build in {Path.Combine(ModBuilds.Runtime, "ace")} (server\\setup-server.ps1 creates it)";
        }
    }
}

/// <summary>
/// setup-server.ps1's "Lifestone server mod" step (m2-plan SM4), run for real but into a temporary runtime: it
/// compiles this checkout's server\mod against the main checkout's ACE build (read only), installs it, records the
/// build in setup-state.json and skips the next time; start-server.ps1's check then finds the install good, and finds
/// a stale key or a changed DLL. The real server\runtime is never written.
/// </summary>
public sealed class SetupModStepTests
{
    [RequiresAceBuildFact]
    public void The_mod_step_builds_installs_records_and_then_skips()
    {
        using var folder = new TempFolder();
        var aceDir = Path.Combine(ModBuilds.Runtime, "ace");
        var script = "$cfg = Get-LsConfig\n"
            + "$t = " + PowerShellHost.Quote(folder.Path) + "\n"
            + "$cfg.Runtime = $t; $cfg.ModBuildDir = Join-Path $t 'mod-build'; $cfg.AceModsDir = Join-Path $t 'ace-mods'\n"
            + "$cfg.ModInstallDir = Join-Path $t 'ace-mods\\Lifestone.ServerMod'; $cfg.ModDll = Join-Path $cfg.ModInstallDir 'Lifestone.ServerMod.dll'\n"
            + "$cfg.ModMeta = Join-Path $cfg.ModInstallDir 'Meta.json'; $cfg.StateFile = Join-Path $t 'setup-state.json'\n"
            + "$cfg.AceDir = " + PowerShellHost.Quote(aceDir) + "; $cfg.AceBuildInfo = Join-Path $cfg.AceDir 'lifestone-build.json'\n"
            + "$state = Get-LsState $cfg\n"
            + "$first = Invoke-LsServerModSetup -Cfg $cfg -State $state -ModRoot $cfg.ModRoot\n"
            + "Save-LsState $cfg $state\n"
            + "$state = Get-LsState $cfg\n"
            + "$second = Invoke-LsServerModSetup -Cfg $cfg -State $state -ModRoot $cfg.ModRoot\n"
            + "$good = Get-LsServerModInstall $cfg\n"
            + "$state.modBuildKey = 'f' * 64; Save-LsState $cfg $state\n"
            + "$stale = Get-LsServerModInstall $cfg\n"
            + "[IO.File]::AppendAllText($cfg.ModDll, 'x')\n"
            + "$changed = Get-LsServerModInstall $cfg\n"
            + "Out-Result @{ First = $first; Second = $second; Good = $good; Stale = $stale; Changed = $changed }\n";
        var result = PowerShellHost.RunOk("powershell", script, timeoutSeconds: 600).Result!.Value;

        var first = result.GetProperty("First");
        Assert.True(first.GetProperty("Built").GetBoolean());
        Assert.Equal(ServerModBuildKey.Compute(RepositoryFiles.PathOf("server", "mod"), ModBuilds.AceServerDllSha256!), first.GetProperty("Key").GetString());
        Assert.False(result.GetProperty("Second").GetProperty("Built").GetBoolean());
        Assert.Equal(first.GetProperty("DllSha256").GetString(), result.GetProperty("Second").GetProperty("DllSha256").GetString());

        // Installed exactly as built, outside the staging folder, and recorded.
        var install = folder.Combine("ace-mods", "Lifestone.ServerMod");
        Assert.Equal(["Lifestone.ServerMod.dll", "Lifestone.ServerMod.pdb", "Meta.json"], Directory.EnumerateFiles(install).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var mod = ModAssembly.Read(Path.Combine(folder.Path, "mod-build", "Lifestone.ServerMod.dll"));

        // The build's restore and intermediate files went to its own sibling folder, not to the project's obj\, which a
        // concurrent setup or compile check would share.
        Assert.True(File.Exists(Path.Combine(folder.Path, "mod-build.obj", "project.assets.json")));
        Assert.True(Directory.Exists(Path.Combine(folder.Path, "mod-build.obj", "Release")));
        Assert.Equal(first.GetProperty("Key").GetString(), mod.Metadata["LifestoneModBuildKey"]);
        Assert.Contains("ACE.Server.Mods.IHarmonyMod", mod.ModInterfaces);

        var good = result.GetProperty("Good");
        Assert.True(good.GetProperty("Installed").GetBoolean());
        Assert.False(good.GetProperty("Stale").GetBoolean());
        Assert.Empty(Problems(good));
        Assert.Equal("0.1.0", good.GetProperty("Version").GetString());

        Assert.True(result.GetProperty("Stale").GetProperty("Stale").GetBoolean());
        Assert.Empty(Problems(result.GetProperty("Stale")));
        Assert.Contains(Problems(result.GetProperty("Changed")), p => p.Contains("is not the one setup installed", StringComparison.Ordinal));
    }

    private static string[] Problems(JsonElement install) =>
        install.GetProperty("Problems") is { ValueKind: JsonValueKind.Array } problems ? [.. problems.EnumerateArray().Select(p => p.GetString()!)]
        : install.GetProperty("Problems") is { ValueKind: JsonValueKind.String } one ? [one.GetString()!]
        : [];
}
