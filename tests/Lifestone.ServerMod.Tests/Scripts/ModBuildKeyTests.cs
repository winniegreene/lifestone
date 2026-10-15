using Lifestone.Client.Tests;
using Lifestone.ServerMod.Tests.Support;

namespace Lifestone.ServerMod.Tests.Scripts;

/// <summary>
/// The server mod's build key (m2-plan SM4): setup-server.ps1 rebuilds the mod when it changes and start-server.ps1
/// refuses a mod whose key no longer matches. PowerShell's Get-LsServerModBuildKey and this project's C# version
/// (<see cref="ServerModBuildKey"/>, used by the installed-mod tests) must agree, cover the sources and the linked
/// Lifestone.Net files, and ignore build output and documentation.
/// </summary>
public sealed class ModBuildKeyTests
{
    private const string AceSha = "9c510128f6ce0469ae1345265ba4d569023c0acdd13bd3a42f83ccd46ea94367";

    /// <summary>The shells the scripts must work in.</summary>
    public static TheoryData<string> Shells => PowerShellHost.Shells;

    [Theory]
    [MemberData(nameof(Shells))]
    public void PowerShell_and_CSharp_give_this_checkout_the_same_key(string shell)
    {
        var modRoot = RepositoryFiles.PathOf("server", "mod");
        var run = PowerShellHost.RunOk(shell, "Out-Result (Get-LsServerModBuildKey -ModRoot " + PowerShellHost.Quote(modRoot) + " -AceServerDllSha256 '" + AceSha + "')\n");
        Assert.Equal(ServerModBuildKey.Compute(modRoot, AceSha), run.Result?.GetString());
    }

    [Fact]
    public void The_key_covers_the_mod_s_sources_and_the_linked_loopback_gate()
    {
        var paths = ServerModBuildKey.Files(RepositoryFiles.PathOf("server", "mod")).Select(f => f.Path).ToArray();
        Assert.Contains("server/mod/.editorconfig", paths);
        Assert.Contains("server/mod/Lifestone.ServerMod/Lifestone.ServerMod.csproj", paths);
        Assert.Contains("server/mod/Lifestone.ServerMod/Meta.json", paths);
        Assert.Contains("server/mod/Lifestone.ServerMod/Verbs/PlaceVerb.cs", paths);
        Assert.Contains("server/mod/Shared/ToolsWire.cs", paths);
        Assert.Contains("src/Lifestone.Net/Sockets/LoopbackPolicy.cs", paths);
        Assert.Contains("src/Lifestone.Net/Sockets/LoopbackRequiredException.cs", paths);
        Assert.DoesNotContain("server/mod/README.md", paths);
        Assert.DoesNotContain(paths, p => p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal));
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_key_changes_with_a_source_or_ACE_and_not_with_build_output_or_docs(string shell)
    {
        using var folder = new TempFolder();
        var modRoot = CopyMod(folder);
        var original = ServerModBuildKey.Compute(modRoot, AceSha);

        // Build output and documentation do not count.
        Directory.CreateDirectory(Path.Combine(modRoot, "Lifestone.ServerMod", "obj"));
        File.WriteAllText(Path.Combine(modRoot, "Lifestone.ServerMod", "obj", "x.cs"), "// generated");
        Directory.CreateDirectory(Path.Combine(modRoot, "Shared", "bin"));
        File.WriteAllText(Path.Combine(modRoot, "Shared", "bin", "y.json"), "{}");
        File.AppendAllText(Path.Combine(modRoot, "README.md"), "\nmore words\n");
        var withNoise = PowerShellHost.RunOk(shell, Key(modRoot, AceSha)).Result?.GetString();
        Assert.Equal(original, withNoise);
        Assert.Equal(original, ServerModBuildKey.Compute(modRoot, AceSha));

        // A source, a linked file or another ACE.Server.dll does.
        File.AppendAllText(Path.Combine(modRoot, "Shared", "PlaceRules.cs"), "\n// changed\n");
        var changedSource = PowerShellHost.RunOk(shell, Key(modRoot, AceSha)).Result?.GetString();
        Assert.NotEqual(original, changedSource);
        Assert.Equal(ServerModBuildKey.Compute(modRoot, AceSha), changedSource);

        File.AppendAllText(Path.Combine(folder.Path, "src", "Lifestone.Net", "Sockets", "LoopbackPolicy.cs"), "\n// changed\n");
        Assert.NotEqual(changedSource, ServerModBuildKey.Compute(modRoot, AceSha));
        Assert.NotEqual(ServerModBuildKey.Compute(modRoot, AceSha), ServerModBuildKey.Compute(modRoot, new string('0', 64)));
    }

    private static string Key(string modRoot, string aceSha) =>
        "Out-Result (Get-LsServerModBuildKey -ModRoot " + PowerShellHost.Quote(modRoot) + " -AceServerDllSha256 '" + aceSha + "')\n";

    /// <summary>Copies server\mod and the two linked Lifestone.Net files into a checkout-shaped folder; returns its server\mod.</summary>
    private static string CopyMod(TempFolder folder)
    {
        var source = RepositoryFiles.PathOf("server", "mod");
        var target = folder.Combine("server", "mod");
        foreach (var file in ServerModBuildKey.Files(source))
        {
            var to = Path.Combine(folder.Path, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file.FullName, to);
        }

        File.Copy(Path.Combine(source, "README.md"), Path.Combine(target, "README.md"));
        return target;
    }
}
