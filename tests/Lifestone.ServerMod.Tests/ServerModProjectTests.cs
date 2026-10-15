using System.Xml.Linq;
using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// Where the server mod's code may live: the mod project stays outside the solution (it builds against the pinned ACE
/// DLLs, through server\setup-server.ps1), and its shared wire contract reaches the client only by the compile link in
/// Lifestone.Client, the single copy this project tests.
/// </summary>
public sealed class ServerModProjectTests
{
    [Fact]
    public void The_server_mod_stays_outside_the_solution()
    {
        var projects = XDocument.Load(RepositoryFiles.PathOf("Lifestone.slnx"))
            .Descendants("Project")
            .Select(p => (string?)p.Attribute("Path") ?? string.Empty)
            .ToArray();
        Assert.Contains("tests/Lifestone.ServerMod.Tests/Lifestone.ServerMod.Tests.csproj", projects);
        Assert.DoesNotContain(projects, p => p.StartsWith("server/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_client_compiles_the_mod_s_shared_wire_contract()
    {
        var includes = XDocument.Load(RepositoryFiles.PathOf("src", "Lifestone.Client", "Lifestone.Client.csproj"))
            .Descendants("Compile")
            .Select(c => (Include: (string?)c.Attribute("Include") ?? string.Empty, Link: (string?)c.Attribute("Link") ?? string.Empty))
            .ToArray();
        Assert.Contains((@"..\..\server\mod\Shared\**\*.cs", @"ServerTools\%(RecursiveDir)%(Filename)%(Extension)"), includes);

        // No test project links the shared files itself: a second copy would collide with Lifestone.Client's (CS0436).
        // The only server\mod link in a test project is this project's link of the mod's ACE-free Core folder.
        var links = Directory.EnumerateFiles(RepositoryFiles.PathOf("tests"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(csproj => XDocument.Load(csproj).Descendants("Compile").Select(c => (
                Project: Path.GetFileName(csproj),
                Include: (string?)c.Attribute("Include") ?? string.Empty)))
            .Where(l => l.Include.Contains(@"server\mod", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal([("Lifestone.ServerMod.Tests.csproj", @"..\..\server\mod\Lifestone.ServerMod\Core\**\*.cs")], links);
    }

    [Fact]
    public void The_linked_mod_core_needs_nothing_from_ACE()
    {
        // Core/ is compiled into this project, which has no ACE assembly, so an ACE type there would not build; this says
        // why, and keeps the mod's own files that do use ACE out of the folder.
        var core = RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "Core");
        var files = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.Equal(
            ["CharPurgeRules.cs", "OneAnswer.cs", "PlacesBuilder.cs", "QuestChange.cs", "ToolRouting.cs", "XpGrant.cs"],
            files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(files, f => Assert.DoesNotMatch(@"\busing\s+ACE\b|\bACE\.\w", File.ReadAllText(f)));
    }
}
