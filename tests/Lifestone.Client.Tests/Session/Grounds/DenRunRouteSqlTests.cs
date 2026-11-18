using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 14 (the den run), on the pinned world SQL: what stands beside the run's road, as the travel file's sentences say it (the rows of the three new edges and the run's entry are <c>TravelRoutesSqlTests</c>'s): the encounter rows
/// within 25, 40 and 60 m of the overland lines (ACE puts an encounter's generator at cell_X * 24, cell_Y * 24, clamped to 0.5..191.5: <c>Landblock.SpawnEncounters</c>, Landblock.cs:282-283 @95ef426d), and the creature rows in the rooms
/// of the way inside the Desert March dungeon.
/// </summary>
public sealed class DenRunRouteSqlTests(ITestOutputHelper output)
{
    private const long ObsidianOuterMix = 1982, MidNorthPlains = 23159, Creature = 10;

    private static Vector2 Global(uint cell, Vector3 at) => new((((cell >> 24) & 0xFF) * 192f) + at.X, (((cell >> 16) & 0xFF) * 192f) + at.Y);

    private static float Off(Vector2 p, Vector2 a, Vector2 b)
    {
        var d = b - a;
        var s = Math.Clamp(Vector2.Dot(p - a, d) / d.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + (d * s));
    }

    private static (int In25, int In40, int In60) Within(IEnumerable<Vector2> camps, params Vector2[] line)
    {
        var off = camps.Select(c => Enumerable.Range(0, line.Length - 1).Min(i => Off(c, line[i], line[i + 1]))).ToArray();
        return (off.Count(d => d <= 25), off.Count(d => d <= 40), off.Count(d => d <= 60));
    }

    [RequiresPinnedWorldSqlFact]
    public void The_camps_beside_the_den_run_s_lines_and_the_creatures_on_its_way_inside_are_the_file_s_numbers()
    {
        var routes = TravelRoutes.Default;
        var den = GroundCatalog.Get(DenRunStation.DefaultGround);
        var route = routes.RoutesFor(den.Key).Single(r => r.Key == DenRunStation.RouteKey);
        var rooms = TownNetworkWalkDatTests.DenRunHubRooms.Split(' ').Select(c => 0x02AA0000L | long.Parse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToHashSet();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["encounter"] = r => r.L("weenie_Class_Id") is ObsidianOuterMix or MidNorthPlains,
            ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) == 0x02AA,
            ["weenie"] = r => r.L("type") == Creature,
        });
        Vector2[] Camps(long generator) => sql["encounter"].View().Where(r => r.L("weenie_Class_Id") == generator)
            .Select(r => new Vector2(((r.L("landblock") >> 8) * 192f) + Math.Clamp(r.L("cell_X") * 24f, 0.5f, 191.5f), ((r.L("landblock") & 0xFF) * 192f) + Math.Clamp(r.L("cell_Y") * 24f, 0.5f, 191.5f))).ToArray();

        // The run: the straight line to the portal (the route's hint), and the two straight halves by the bend to the entry's front (what she runs).
        var hop = routes.Edge("desert-march-to-black-hill");
        var via = Assert.Single(route.Run!.Via!.Points);
        var (drop, portal, bend, front) = (Global(hop.DestCell, hop.DestOrigin), Global(route.Run.EntryCell, route.Run.EntryOrigin), Global(via.Cell, via.Origin), Global(den.Entry!.FrontCell, den.Entry.Front));
        var obsidian = Camps(ObsidianOuterMix);
        var straight = Within(obsidian, drop, portal);
        var byTheBend = Within(obsidian, drop, bend, front);
        output.WriteLine($"den-run camps (Obsidian Outer Mix Generator {ObsidianOuterMix}) within 25/40/60 m: the straight line {straight}, by the bend {byTheBend}");
        Assert.Equal((5, 10, 14), straight);
        Assert.Equal((6, 12, 22), byTheBend);

        // Danby's Outpost: the drop to the Desert March Portal.
        var danbys = routes.Edge("network-to-danbys");
        var march = routes.Edge("danbys-to-desert-march");
        var plains = Within(Camps(MidNorthPlains), Global(danbys.DestCell, danbys.DestOrigin), Global(march.Cell, march.Origin));
        output.WriteLine($"den-run camps (Mid North Plains Generator {MidNorthPlains}) within 25/40/60 m of the walk to the Desert March Portal: {plains}");
        Assert.Equal((3, 4, 4), plains);

        // Inside the Desert March dungeon: the creature rows of the landblock, and those standing in a room of the way.
        var creatures = sql["weenie"].View().Select(r => r.L("class_Id")).ToHashSet();
        var rows = sql["landblock_instance"].View().Where(r => creatures.Contains(r.L("weenie_Class_Id"))).ToArray();
        var onTheWay = rows.Count(r => rooms.Contains(r.L("obj_Cell_Id")));
        output.WriteLine($"den-run hub: {rows.Length} creature rows in landblock 0x02AA, {onTheWay} of them in the {rooms.Count} rooms of the way");
        Assert.Equal((61, 21), (rows.Length, onTheWay));
    }
}
