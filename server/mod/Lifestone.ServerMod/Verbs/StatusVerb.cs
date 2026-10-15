using ACE.Common;
using ACE.Database;
using ACE.Server.Managers;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary><c>@lifestone status</c>: what the mod is, which ACE it runs in, and who is asking (<see cref="StatusData"/>).</summary>
internal static class StatusVerb
{
    /// <summary>Answers a status request (from ACE's console when the call has no session).</summary>
    public static void Run(ToolCall call, StatusRequest request)
    {
        var session = call.Session;
        var network = ConfigManager.Config.Server.Network;
        var data = new StatusData(
            ToolsWire.Version,
            ModInfo.Version,
            ModInfo.AceCommit,
            ModInfo.AceServerDllSha256,
            network.Host,
            (int)network.Port,
            DatabaseManager.AutoPromoteNextAccountToAdmin,
            session is null ? "Console" : session.AccessLevel.ToString(),
            session?.Account,
            session?.Player?.Name,
            WorldManager.WorldStatus.ToString(),
            ToolRouting.Implemented);
        call.Succeed(request, data, data.Describe());
    }
}
