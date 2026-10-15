namespace Lifestone.Net;

/// <summary>
/// Names the Lifestone.Net assembly for tests and architecture scans (<c>typeof(NetAssembly).Assembly</c>). The project
/// belongs to the M2 net track (docs/milestones/m2-plan.md §3.3 "net" and §4.6): the session (<c>Session/</c>), the
/// inbound dispatcher (<c>Dispatch/</c>), the raw tap (<c>Taps/</c>), the loopback gate and socket seam
/// (<c>Sockets/</c>), the wire records (<c>Types/</c>, <c>Messages/</c>) and its diagnostics.
/// </summary>
public static class NetAssembly
{
    /// <summary>The assembly name, as project references and the architecture scans spell it.</summary>
    public const string Name = "Lifestone.Net";
}
