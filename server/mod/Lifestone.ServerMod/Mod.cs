using System.Globalization;
using System.Security.Cryptography;
using ACE.Common;
using ACE.Server.Mods;
using Lifestone.Net.Sockets;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// The mod's entry point. ACE creates it by name (<c>&lt;assembly&gt;.Mod</c>, ModContainer.cs:40) and calls
/// <see cref="Initialize"/> from ModManager.Initialize, early in its boot (Program.cs:158): after the configuration is
/// read (:155), before the databases (:250), the sockets (:312) and the world (:315, opened at :342).
/// </summary>
/// <remarks>
/// <para><see cref="Initialize"/> is the load gate. It refuses (logs <see cref="ModLog.Refused"/> as an error, then
/// throws) when ACE is not configured exactly as Lifestone needs; ACE then logs "Error enabling Lifestone", leaves the
/// mod inactive and registers none of its commands (ModContainer.cs:107-121, ModManager.cs:218-222), and
/// start-server.ps1 never sees the ready line. The conditions:</para>
/// <list type="bullet">
/// <item>Server.Network.Host must be exactly 127.0.0.1 (<see cref="LoopbackPolicy.IsPinnedConfigHost"/>): ACE binds
/// every interface when it cannot parse its host (SocketManager.cs:24-35);</item>
/// <item>Server.Accounts.DefaultAccessLevel must be 0, so accounts ACE creates are Player accounts (AccountDefaults.cs:16;
/// owner decision D51);</item>
/// <item>the running ACE.Server.dll must hash to the SHA-256 this mod was compiled against: the mod relies on ACE's
/// internals and boot order at one pinned commit, and ACE's own build info cannot tell (it reports 1.77 at the 1.78.4816
/// tag, ServerBuildInfo_Dynamic.cs:6-10);</item>
/// <item>Meta.json's HotReload must be false: it defaults to true (ModMetadata.cs:26) and would let a changed DLL be
/// reloaded into a running server (ModContainer.cs:62).</item>
/// </list>
/// <para>Then <see cref="StartupWatch"/> turns off ACE's first-account Admin auto-promotion as soon as ACE has computed
/// it and logs the ready line once the world is open. The mod cannot do that here: the flag does not exist yet, and
/// the sentinel Admin account setup creates (owner decision D11) is what keeps ACE from setting it at all.</para>
/// </remarks>
public sealed class Mod : IHarmonyMod
{
    private StartupWatch? watch;

    /// <summary>Checks the load gate, logs the loaded line and starts the startup watch; throws if the gate refuses.</summary>
    public void Initialize()
    {
        var refusal = Refusal();
        if (refusal is not null)
        {
            ModManager.Log(ModLog.Refused + refusal, ModManager.LogLevel.Error);
            throw new InvalidOperationException(ModLog.Refused + refusal);
        }

        if (!ConfigManager.Config.Server.Accounts.AllowAutoAccountCreation)
        {
            ModManager.Log(
                "Lifestone: Server.Accounts.AllowAutoAccountCreation is false, so a first login with a new account will fail; server\\setup-server.ps1 renders it true.",
                ModManager.LogLevel.Warn);
        }

        ModManager.Log(string.Create(
            CultureInfo.InvariantCulture,
            $"{ModLog.Loaded}version {ModInfo.Version}, wire {ToolsWire.Version}, ACE {ModInfo.AceShort}"));
        watch = StartupWatch.Start();
    }

    /// <summary>Stops the startup watch (ACE calls this when it disables the mod or shuts down).</summary>
    public void Dispose()
    {
        watch?.Dispose();
        watch = null;
    }

    /// <summary>Why this ACE must not run the mod, or null when it may.</summary>
    private string? Refusal()
    {
        var server = ConfigManager.Config?.Server;
        if (server?.Network is null || server.Accounts is null)
        {
            return "ACE's configuration (Config.js) is not loaded";
        }

        if (!LoopbackPolicy.IsPinnedConfigHost(server.Network.Host))
        {
            return "Config.js Server.Network.Host is '" + Printable(server.Network.Host) + "', not exactly " + LoopbackPolicy.PinnedHost
                + "; Lifestone's ACE must listen on loopback only (server\\templates\\Config.template.js)";
        }

        if (server.Accounts.DefaultAccessLevel != 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Config.js Server.Accounts.DefaultAccessLevel is {server.Accounts.DefaultAccessLevel}, not 0; Lifestone's accounts must be Player accounts");
        }

        if (ModInfo.AceServerDllSha256.Length != 64)
        {
            return "this build of the mod carries no ACE.Server.dll hash; build it with server\\build-mod.ps1 or server\\setup-server.ps1";
        }

        var aceServer = typeof(ModManager).Assembly.Location;
        var actual = Sha256Of(aceServer);
        if (!string.Equals(actual, ModInfo.AceServerDllSha256, StringComparison.Ordinal))
        {
            return "the running ACE.Server.dll has SHA-256 " + actual + ", but the mod was built against " + ModInfo.AceServerDllSha256
                + " (ACE " + ModInfo.AceShort + "); rebuild the mod with server\\setup-server.ps1 -RebuildMod";
        }

        var container = this.GetModContainer();
        if (container?.Meta is null)
        {
            return "ACE lists no mod container for this instance, so Meta.json's HotReload cannot be checked";
        }

        if (container.Meta.HotReload)
        {
            return "Meta.json has HotReload on (ACE's default); Lifestone's Meta.json sets it false, so reinstall the mod with server\\setup-server.ps1";
        }

        return null;
    }

    private static string Sha256Of(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(ACE.Server has no file location)";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>A configuration value fit for one log line: printable ASCII, at most 64 characters.</summary>
    private static string Printable(string? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        var chars = value.Take(64).Select(c => c is < ' ' or > '~' ? '?' : c).ToArray();
        return new string(chars);
    }
}
