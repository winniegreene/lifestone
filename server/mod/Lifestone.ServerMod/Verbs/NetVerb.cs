using ACE.Server.Network;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// <c>@lifestone net</c>: ACE's server-wide packet counters since boot (NetworkStatistics.cs:19-69), for the client's
/// lossy-transport checks (DoD 12), which take the difference of two replies.
/// </summary>
internal static class NetVerb
{
    /// <summary>Answers a net request.</summary>
    public static void Run(ToolCall call, NetRequest request)
    {
        var data = new NetData(
            NetworkStatistics.C2S_Packets_Aggregate,
            NetworkStatistics.S2C_Packets_Aggregate,
            NetworkStatistics.C2S_CRCErrors_Aggregate,
            NetworkStatistics.C2S_RequestsForRetransmit_Aggregate,
            NetworkStatistics.S2C_RequestsForRetransmit_Aggregate);
        call.Succeed(request, data, data.Describe());
    }
}
