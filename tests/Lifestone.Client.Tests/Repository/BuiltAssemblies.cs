using Lifestone.Data.Dat;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The built Lifestone assemblies that the metadata scans read: Lifestone.Data and Lifestone.Client, which this project
/// references, and Lifestone.Net once it exists (Lifestone.Client references it, so the build copies it next to the
/// tests; <see cref="ArchitectureTests"/> fails if the project exists and the assembly does not). Lifestone.Game and
/// Lifestone.Headless are not in this project's output, so only the source scans cover them.
/// </summary>
internal static class BuiltAssemblies
{
    /// <summary>The project whose assembly must be scanned once it exists.</summary>
    public const string NetProject = "src/Lifestone.Net/Lifestone.Net.csproj";

    private static readonly Lazy<IReadOnlyList<AssemblyMetadata>> Read = new(() =>
    {
        var net = Path.Combine(AppContext.BaseDirectory, "Lifestone.Net.dll");
        string[] paths = [typeof(DatSet).Assembly.Location, typeof(LegalNotices).Assembly.Location, .. File.Exists(net) ? [net] : Array.Empty<string>()];
        return [.. paths.Select(AssemblyMetadata.Read)];
    });

    /// <summary>Every built assembly found.</summary>
    public static IReadOnlyList<AssemblyMetadata> All => Read.Value;

    /// <summary>One built assembly, by simple name.</summary>
    public static AssemblyMetadata Named(string name) => All.Single(a => a.Name == name);
}
