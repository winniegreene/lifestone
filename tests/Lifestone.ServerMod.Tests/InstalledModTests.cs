using System.Text.Json;
using System.Text.RegularExpressions;
using Lifestone.Client.Tests;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The built server mod (m2-plan SM4): what ACE will load is the mod type ACE looks for, implements ACE's own
/// IHarmonyMod, references only ACE's assemblies and the framework, carries the ACE.Server.dll hash it checks at load,
/// and sits alone with its pdb and Meta.json. Run against the main checkout's install (skipped when none is installed or
/// it is stale) and against this checkout's compile check (skipped until server\build-mod.ps1 has run). They read
/// only non-secret files: the DLL, Meta.json, setup-state.json and ACE's build stamp.
/// </summary>
public sealed partial class InstalledModTests
{
    /// <summary>ACE's assemblies the mod may reference (Private=false: ACE provides them at run time).</summary>
    private static readonly string[] AceAssemblies = ["ACE.Common", "ACE.Database", "ACE.DatLoader", "ACE.Entity", "ACE.Server"];

    [GeneratedRegex("^(System(\\..+)?|netstandard|mscorlib|Microsoft\\.EntityFrameworkCore(\\..+)?)$")]
    private static partial Regex FrameworkAssembly();

    [RequiresInstalledModFact]
    public void The_installed_mod_is_what_ACE_loads() => AssertModFolder(ModBuilds.InstallDir);

    [RequiresInstalledModFact]
    public void The_installed_mod_is_the_one_setup_recorded()
    {
        Assert.Equal(ModBuilds.SetupState("modDllSha256"), ServerModBuildKey.Sha256(ModBuilds.Dll(ModBuilds.InstallDir)));
        var mod = ModAssembly.Read(ModBuilds.Dll(ModBuilds.InstallDir));
        Assert.Equal(ModBuilds.SetupState("modBuildKey"), mod.Metadata["LifestoneModBuildKey"]);
        Assert.Equal(ModBuilds.SetupState("modVersion"), mod.InformationalVersion);
    }

    [RequiresCompileCheckFact]
    public void The_compile_check_is_what_ACE_would_load() => AssertModFolder(ModBuilds.CompileCheckDir);

    private static void AssertModFolder(string folder)
    {
        // Exactly the DLL, its pdb and Meta.json: no ACE assembly beside the mod (the type-identity trap, ModContainer.cs:201).
        Assert.Equal(
            ["Lifestone.ServerMod.dll", "Lifestone.ServerMod.pdb", "Meta.json"],
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).Order(StringComparer.Ordinal));

        using var meta = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "Meta.json")));
        Assert.Equal("Lifestone", meta.RootElement.GetProperty("Name").GetString());
        Assert.False(meta.RootElement.GetProperty("HotReload").GetBoolean());
        Assert.True(meta.RootElement.GetProperty("Enabled").GetBoolean());

        var mod = ModAssembly.Read(ModBuilds.Dll(folder));

        // ACE creates <assembly>.Mod (ModContainer.cs:40) and casts it to its own IHarmonyMod (:201).
        Assert.True(mod.ModTypeIsPublic, "Lifestone.ServerMod.Mod must be a public type");
        Assert.Contains("ACE.Server.Mods.IHarmonyMod", mod.ModInterfaces);

        var foreign = mod.AssemblyReferences.Where(r => !AceAssemblies.Contains(r) && !FrameworkAssembly().IsMatch(r)).ToArray();
        Assert.True(foreign.Length == 0, "the mod references assemblies that are neither ACE's nor the framework's: " + string.Join(", ", foreign));
        Assert.Contains("ACE.Server", mod.AssemblyReferences);

        // The load gate compares the running ACE.Server.dll with this hash (Mod.cs); it is the stamped one.
        Assert.Matches("^[0-9a-f]{64}$", mod.Metadata["AceServerDllSha256"]);
        if (ModBuilds.AceServerDllSha256 is { } stamped)
        {
            Assert.Equal(stamped, mod.Metadata["AceServerDllSha256"]);
        }

        using var versions = JsonDocument.Parse(File.ReadAllBytes(RepositoryFiles.PathOf("server", "versions.json")));
        Assert.Equal(versions.RootElement.GetProperty("ace").GetProperty("commit").GetString(), mod.Metadata["AceCommit"]);
        Assert.Equal(meta.RootElement.GetProperty("Version").GetString(), mod.InformationalVersion);
        Assert.False(string.IsNullOrEmpty(mod.Metadata["LifestoneModBuildKey"]));
    }
}
