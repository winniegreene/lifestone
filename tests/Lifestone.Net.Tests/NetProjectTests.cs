using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Lifestone.Client.Tests;
using Lifestone.Net;

namespace Lifestone.Net.Tests;

/// <summary>Lifestone.Net's project rule: the base class library only, so the protocol layer drags in no engine, data or package code.</summary>
public sealed class NetProjectTests
{
    [Fact]
    public void Lifestone_net_references_only_the_base_class_library()
    {
        var assembly = typeof(NetAssembly).Assembly;
        Assert.Equal(NetAssembly.Name, assembly.GetName().Name);

        var frameworkDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        var outside = assembly.GetReferencedAssemblies()
            .Select(name => (Name: name.Name, Location: Assembly.Load(name).Location))
            .Where(r => !Path.GetFullPath(r.Location).StartsWith(frameworkDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{r.Name} ({r.Location})")
            .ToArray();
        Assert.True(outside.Length == 0, "Lifestone.Net may reference the base class library only:\n  " + string.Join("\n  ", outside));
    }

    /// <summary>
    /// The project file itself declares no reference. The compiler drops a reference no code uses, so the runtime check
    /// above alone would let one in until the first use.
    /// </summary>
    [Fact]
    public void Lifestone_net_project_declares_no_package_project_or_assembly_reference()
    {
        var project = XDocument.Load(RepositoryFiles.PathOf("src", "Lifestone.Net", "Lifestone.Net.csproj"));
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference" or "FrameworkReference")
            .Select(e => $"<{e.Name.LocalName} Include=\"{(string?)e.Attribute("Include")}\">")
            .ToArray();
        Assert.True(references.Length == 0, "Lifestone.Net.csproj may declare no references (BCL only):\n  " + string.Join("\n  ", references));
    }
}
