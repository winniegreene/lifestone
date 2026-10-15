using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests.Support;

/// <summary>
/// Where a built server mod can be found for the installed-mod tests, read from non-secret files only: the main
/// checkout's install (server\runtime\ace-mods\Lifestone.ServerMod, put there by setup-server.ps1, with
/// setup-state.json and ACE's build stamp lifestone-build.json) and this checkout's compile check (cache\server-mod,
/// from server\build-mod.ps1).
/// </summary>
internal static class ModBuilds
{
    /// <summary>The main checkout's server\runtime (the backend runs only from there).</summary>
    public static string Runtime => Path.Combine(RepositoryFiles.MainCheckout, "server", "runtime");

    /// <summary>The installed mod folder.</summary>
    public static string InstallDir => Path.Combine(Runtime, "ace-mods", "Lifestone.ServerMod");

    /// <summary>This checkout's compile-check output.</summary>
    public static string CompileCheckDir => RepositoryFiles.PathOf("cache", "server-mod");

    /// <summary>The mod's DLL in a build folder.</summary>
    public static string Dll(string folder) => Path.Combine(folder, "Lifestone.ServerMod.dll");

    /// <summary>A top-level string of a JSON file, or null when the file or the key is missing.</summary>
    public static string? JsonString(string file, string key)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>The SHA-256 of ACE.Server.dll in the main checkout's ACE build stamp, or null.</summary>
    public static string? AceServerDllSha256 => JsonString(Path.Combine(Runtime, "ace", "lifestone-build.json"), "aceServerDllSha256");

    /// <summary>A setup-state.json value, or null.</summary>
    public static string? SetupState(string key) => JsonString(Path.Combine(Runtime, "setup-state.json"), key);

    /// <summary>Why the installed-mod tests cannot run, or null when they can.</summary>
    public static string? InstalledSkipReason()
    {
        if (!File.Exists(Dll(InstallDir)))
        {
            return $"no server mod is installed in {InstallDir} (server\\setup-server.ps1 installs it)";
        }

        var recorded = SetupState("modBuildKey");
        var aceSha = AceServerDllSha256;
        if (recorded is null || aceSha is null)
        {
            return "setup-state.json or lifestone-build.json says nothing about the installed mod (run server\\setup-server.ps1 -ModOnly)";
        }

        var source = SetupState("modSource") is { } other && Directory.Exists(other) ? other : Path.Combine(RepositoryFiles.MainCheckout, "server", "mod");
        return string.Equals(ServerModBuildKey.Compute(source, aceSha), recorded, StringComparison.Ordinal)
            ? null
            : "the installed server mod was built from other sources than the main checkout's server\\mod (stale; run server\\setup-server.ps1 -ModOnly)";
    }
}

/// <summary>A fact about the installed server mod; skipped, with the reason, when none is installed or it is stale.</summary>
internal sealed class RequiresInstalledModFactAttribute : FactAttribute
{
    public RequiresInstalledModFactAttribute()
    {
        if (ModBuilds.InstalledSkipReason() is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>A fact about this checkout's compile check (cache\server-mod); skipped when server\build-mod.ps1 has not run.</summary>
internal sealed class RequiresCompileCheckFactAttribute : FactAttribute
{
    public RequiresCompileCheckFactAttribute()
    {
        if (!File.Exists(ModBuilds.Dll(ModBuilds.CompileCheckDir)))
        {
            Skip = $"no compile check in {ModBuilds.CompileCheckDir} (run pwsh -NoProfile -File server\\build-mod.ps1)";
        }
    }
}

/// <summary>What a built Lifestone.ServerMod.dll declares, read from its metadata (System.Reflection.Metadata, in the shared framework).</summary>
internal sealed record ModAssembly(
    IReadOnlyList<string> AssemblyReferences,
    IReadOnlyDictionary<string, string> Metadata,
    string? InformationalVersion,
    bool ModTypeIsPublic,
    IReadOnlyList<string> ModInterfaces)
{
    /// <summary>Reads <paramref name="dll"/>.</summary>
    public static ModAssembly Read(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var references = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).Order(StringComparer.Ordinal).ToArray();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        string? informational = null;
        foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = md.GetCustomAttribute(handle);
            var type = AttributeTypeName(md, attribute);
            var blob = md.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1)
            {
                continue;
            }

            if (type == "System.Reflection.AssemblyMetadataAttribute")
            {
                var key = blob.ReadSerializedString() ?? string.Empty;
                metadata[key] = blob.ReadSerializedString() ?? string.Empty;
            }
            else if (type == "System.Reflection.AssemblyInformationalVersionAttribute")
            {
                informational = blob.ReadSerializedString();
            }
        }

        var isPublic = false;
        var interfaces = new List<string>();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            if (md.GetString(type.Namespace) != "Lifestone.ServerMod" || md.GetString(type.Name) != "Mod")
            {
                continue;
            }

            isPublic = (type.Attributes & System.Reflection.TypeAttributes.VisibilityMask) == System.Reflection.TypeAttributes.Public;
            foreach (var implementation in type.GetInterfaceImplementations())
            {
                var iface = md.GetInterfaceImplementation(implementation).Interface;
                if (iface.Kind == HandleKind.TypeReference)
                {
                    var reference = md.GetTypeReference((TypeReferenceHandle)iface);
                    interfaces.Add(md.GetString(reference.Namespace) + "." + md.GetString(reference.Name));
                }
            }
        }

        return new ModAssembly(references, metadata, informational, isPublic, interfaces);
    }

    private static string AttributeTypeName(MetadataReader md, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return string.Empty;
        }

        var parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference)
        {
            return string.Empty;
        }

        var type = md.GetTypeReference((TypeReferenceHandle)parent);
        return md.GetString(type.Namespace) + "." + md.GetString(type.Name);
    }
}
