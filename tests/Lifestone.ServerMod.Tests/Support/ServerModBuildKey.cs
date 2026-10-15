using System.Security.Cryptography;
using System.Text;

namespace Lifestone.ServerMod.Tests.Support;

/// <summary>
/// The server mod's build key, computed the way server\lib\LifestoneServer.psm1's Get-LsServerModBuildKey does
/// (setup-server.ps1 records it as setup-state.json modBuildKey; start-server.ps1 refuses a mod whose key changed):
/// SHA-256 of the UTF-8 text <c>"lifestone-mod-build-key/1\n"</c>, then one <c>"&lt;path&gt;\t&lt;file SHA-256&gt;\n"</c> per
/// build file in ordinal path order, then <c>"ACE.Server.dll\t&lt;sha&gt;\n"</c>. The build files are those under server/mod
/// with the extensions .cs, .csproj, .json, .props and .targets, and .editorconfig, outside bin/ and obj/, plus the two
/// Lifestone.Net files the project links. A second implementation, so the script tests can hold the two to each other.
/// </summary>
internal static class ServerModBuildKey
{
    private static readonly string[] Extensions = [".cs", ".csproj", ".json", ".props", ".targets"];

    private static readonly string[] LinkedFiles =
    [
        "src/Lifestone.Net/Sockets/LoopbackPolicy.cs",
        "src/Lifestone.Net/Sockets/LoopbackRequiredException.cs",
    ];

    /// <summary>The build files of the server\mod folder <paramref name="modRoot"/>: (repository-relative path, full path).</summary>
    public static IReadOnlyList<(string Path, string FullName)> Files(string modRoot)
    {
        var root = Path.GetFullPath(modRoot).TrimEnd(Path.DirectorySeparatorChar);
        var repo = Path.GetDirectoryName(Path.GetDirectoryName(root))!;
        var files = new List<(string Path, string FullName)>();
        foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, full);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            var name = Path.GetFileName(full);
            if (parts.Any(p => p is "bin" or "obj")
                || !(name == ".editorconfig" || Extensions.Contains(Path.GetExtension(full).ToLowerInvariant())))
            {
                continue;
            }

            files.Add(("server/mod/" + relative.Replace('\\', '/'), full));
        }

        foreach (var linked in LinkedFiles)
        {
            files.Add((linked, Path.Combine(repo, linked.Replace('/', Path.DirectorySeparatorChar))));
        }

        return [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>The key of the server\mod folder <paramref name="modRoot"/> built against an ACE.Server.dll with SHA-256 <paramref name="aceServerDllSha256"/>.</summary>
    public static string Compute(string modRoot, string aceServerDllSha256)
    {
        var text = new StringBuilder("lifestone-mod-build-key/1\n");
        foreach (var (path, full) in Files(modRoot))
        {
            text.Append(path).Append('\t').Append(Sha256(full)).Append('\n');
        }

        text.Append("ACE.Server.dll\t").Append(aceServerDllSha256.ToLowerInvariant()).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>A file's SHA-256 in lower-case hex.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
