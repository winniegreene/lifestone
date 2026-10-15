using System.Globalization;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Tools;

/// <summary>
/// How far the server's view of the player (the tools' "where am I", <see cref="Contracts.WhereResult"/>) is from the
/// client's, for DoD 8's check that they agree within <see cref="ToleranceMetres"/> after a walk. Distances are global
/// (<see cref="CellFrameExtensions.ToGlobal"/>: landblock times 192 plus the local origin, for every cell kind), in
/// three dimensions, so two positions in neighbouring landblocks compare correctly.
/// </summary>
public static class PositionAgreement
{
    /// <summary>The largest distance, in metres, that still agrees (DoD 8: "agrees within 0.5 m").</summary>
    public const double ToleranceMetres = 0.5;

    /// <summary>The distance between <paramref name="a"/> and <paramref name="b"/>, in metres.</summary>
    public static double DistanceMetres(in CellFrame a, in CellFrame b) => a.ToGlobal().DistanceTo(b.ToGlobal());

    /// <summary>True when <paramref name="server"/> and <paramref name="client"/> are within <see cref="ToleranceMetres"/>.</summary>
    /// <param name="server">The server's view (where am I).</param>
    /// <param name="client">The client's position (physics' controller).</param>
    /// <param name="metres">The distance between them.</param>
    public static bool Agrees(in CellFrame server, in CellFrame client, out double metres)
    {
        metres = DistanceMetres(in server, in client);
        return metres <= ToleranceMetres;
    }

    /// <summary>
    /// One line for the tools panel and the logs: <c>server 0x... [...] ...; client 0x... [...] ...; 0.12 m apart (agrees
    /// within 0.5 m)</c>, positions in ACE's form (<see cref="AceLocText"/>), invariant culture.
    /// </summary>
    public static string Describe(in CellFrame server, in CellFrame client)
    {
        var verdict = Agrees(in server, in client, out var metres) ? "agrees" : "does not agree";
        var positions = "server " + AceLocText.Format(in server) + "; client " + AceLocText.Format(in client);
        return string.Create(CultureInfo.InvariantCulture, $"{positions}; {metres:0.00} m apart ({verdict} within {ToleranceMetres:0.0} m)");
    }
}
