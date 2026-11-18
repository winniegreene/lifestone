using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Movement.Wayfinding;

namespace Lifestone.Client.Session.Travel;

/// <summary>A portal placed in the world: its placement and where it sends a body.</summary>
/// <param name="Guid">The placement's guid (landblock_instance.guid).</param>
/// <param name="Wcid">The weenie class.</param>
/// <param name="Cell">The cell it stands in (an outdoor portal's is its landblock id, cell index 0).</param>
/// <param name="Origin">Its point in that cell's landblock.</param>
/// <param name="Name">Its name (PropertyString 1).</param>
/// <param name="DestCell">Its Destination's cell (PositionType 2), 0 for none.</param>
public sealed record WorldPortal(uint Guid, uint Wcid, uint Cell, Vector3 Origin, string Name, uint DestCell)
{
    /// <summary>True for a portal in an outdoor cell.</summary>
    public bool Outdoor => (Cell & 0xFFFF) < 0x100;

    /// <summary>Its point in global metres (<see cref="NavPoint.Global"/>).</summary>
    public Vector2 Global => new NavPoint(Cell & 0xFFFF0000u, Origin).Global;
}

/// <summary>
/// M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:29:41 UTC): every portal placed in the world, from <c>world-portals.json</c> (the pinned world SQL v0.9.296, ACE's open world database, not retail dats; the file's <c>sources</c> holds the
/// read-only SELECT). The walk to the Yinar Portal's front in Samsur's portal plaza (landblock 0x977E, nineteen portals) kept off the portals of the travel data alone, so only its own portal was near its line; she touched Sand's Edge Portal
/// (weenie 13124 at (28.88, 29.92)), which sent her to 0xA586003D, and the hop named its own portal. Two uses: the portals every walk keeps off (<c>RungPortal.AllKnown</c>), and the portal a teleport during a walk came from, by the destination
/// it moved the body to (<see cref="TakenBy"/>).
/// </summary>
public static class WorldPortals
{
    /// <summary>The embedded resource's name.</summary>
    public const string ResourceName = "Lifestone.Client.Session.Travel.world-portals.json";

    private static readonly Lazy<WorldPortal[]> Loaded = new(Load);

    /// <summary>Every placed portal of the file, in guid order.</summary>
    public static IReadOnlyList<WorldPortal> All => Loaded.Value;

    /// <summary>
    /// The placed portal that sent a body to <paramref name="arrivalCell"/> during a walk from <paramref name="from"/> to <paramref name="to"/> (global metres): of the portals whose Destination is in that landblock and that stand within
    /// <paramref name="reach"/> of the walk's straight line, the one nearest it; null when none does (a recall, a summoned portal, the server's own move, a portal the walk could not have touched).
    /// </summary>
    /// <param name="arrivalCell">The cell the server put the body in.</param>
    /// <param name="from">The walk's start, global metres.</param>
    /// <param name="to">Its end (the hop's portal), global metres.</param>
    /// <param name="reach">How far off the line a portal is a candidate, in metres (the walks' keep-off reach).</param>
    public static WorldPortal? TakenBy(uint arrivalCell, Vector2 from, Vector2 to, float reach)
    {
        WorldPortal? best = null;
        var bestMetres = reach;
        var ab = to - from;
        var length2 = ab.LengthSquared();
        foreach (var p in All)
        {
            if (p.DestCell == 0 || p.DestCell >> 16 != arrivalCell >> 16)
            {
                continue;
            }

            var g = p.Global;
            var metres = Vector2.Distance(g, from + (ab * (length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(g - from, ab) / length2, 0f, 1f)))) - (p.DestCell == arrivalCell ? 1e-3f : 0f); // the same cell wins a tie
            if (metres <= bestMetres)
            {
                best = p;
                bestMetres = metres;
            }
        }

        return best;
    }

    private static WorldPortal[] Load()
    {
        using var stream = typeof(WorldPortals).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"the embedded {ResourceName} is missing from the client assembly"));
        using var doc = JsonDocument.Parse(stream);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        string[] expected = ["guid", "wcid", "cell", "x", "y", "z", "name", "dest_cell"];
        if (!columns.SequenceEqual(expected))
        {
            throw new InvalidOperationException($"{ResourceName}: the columns are [{string.Join(", ", columns)}], not [{string.Join(", ", expected)}]");
        }

        var rows = new List<WorldPortal>();
        foreach (var r in doc.RootElement.GetProperty("portals").EnumerateArray())
        {
            rows.Add(new WorldPortal(r[0].GetUInt32(), r[1].GetUInt32(), r[2].GetUInt32(), new Vector3(r[3].GetSingle(), r[4].GetSingle(), r[5].GetSingle()), r[6].GetString() ?? string.Empty, r[7].GetUInt32()));
        }

        return [.. rows];
    }
}
