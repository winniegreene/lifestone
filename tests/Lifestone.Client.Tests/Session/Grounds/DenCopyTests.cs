using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 14 (the den run): the Black Spawn Den behind the entrance at 60.0S 57.6W (portal 7246, landblock 0x0103) is the Aluvian den's (portal 7242, landblock 0x0105) copy on the retail dats: the same cells, each with the same
/// environment, structure, placement, surfaces, portals and statics, the landblock's id apart. The profile <c>black-spawn-den-sho.json</c> is the Aluvian profile with the landblock read as 0x0103 on the strength of this test and of
/// <see cref="DenCopySqlTests"/>.
/// </summary>
public sealed class DenCopyDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const ushort Aluvian = 0x0105;
    private const ushort Sho = 0x0103;

    private static uint Low(uint cell) => cell & 0xFFFF;

    [RequiresDatsFact]
    public void The_two_landblocks_hold_the_same_cells_the_landblock_s_id_apart()
    {
        var structures = new CellStructureLibrary(fixture.Dats);
        var a = LandblockCellSet.Load(structures, new LandblockCoord((byte)(Aluvian >> 8), (byte)(Aluvian & 0xFF)));
        var s = LandblockCellSet.Load(structures, new LandblockCoord((byte)(Sho >> 8), (byte)(Sho & 0xFF)));
        Assert.Equal(653, a.Count);
        Assert.Equal(a.Ids.Select(Low), s.Ids.Select(Low));
        var differing = new List<string>();
        foreach (var id in a.Ids)
        {
            var x = a.GetCell(id)!;
            var y = s.GetCell(((uint)Sho << 16) | Low(id))!;
            var same = x.Flags == y.Flags
                && x.EnvironmentId == y.EnvironmentId
                && x.CellStructureIndex == y.CellStructureIndex
                && x.Frame == y.Frame
                && x.RestrictionObjectId == y.RestrictionObjectId
                && x.SurfaceIds.SequenceEqual(y.SurfaceIds)
                && x.StaticObjects.SequenceEqual(y.StaticObjects)
                && x.VisibleCellIds.Select(Low).SequenceEqual(y.VisibleCellIds.Select(Low))
                && x.Portals.Count == y.Portals.Count
                && x.Portals.Zip(y.Portals).All(p => p.First with { OtherCellId = Low(p.First.OtherCellId) } == p.Second with { OtherCellId = Low(p.Second.OtherCellId) });
            if (!same)
            {
                differing.Add(string.Create(CultureInfo.InvariantCulture, $"0x{Low(id):X4}"));
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"landblocks 0x{Aluvian:X4} and 0x{Sho:X4}: {a.Count} cells each, {differing.Count} differ{(differing.Count == 0 ? string.Empty : ": " + string.Join(" ", differing))}"));
        Assert.Empty(differing);
    }

    /// <summary>
    /// The den's first exit stands behind no-go cells (its room and the doorway the fight loop keeps out of), so a leg to the exit takes the fewest of them back (<see cref="GroundRooms.ExitWay"/>): the cells between the exit's cell
    /// 0x0341 and the arrival's rooms, and nothing else; every other ground's exit touches a room and takes none.
    /// </summary>
    [RequiresDatsFact]
    public void A_leg_to_the_den_s_exit_takes_back_only_the_no_go_cells_between_the_exit_and_the_rooms()
    {
        var structures = new CellStructureLibrary(fixture.Dats);
        var den = GroundCatalog.Get("black-spawn-den-sho");
        var all = LandblockCellSet.Load(structures, GroundRooms.CoordOf(den));
        var way = GroundRooms.ExitWay(all, den).Order().ToArray();
        output.WriteLine("the Sho den's exit way: " + string.Join(' ', way.Select(c => string.Create(CultureInfo.InvariantCulture, $"0x{c:X8}"))));
        Assert.NotEmpty(way);
        Assert.All(way, c => Assert.True(den.IsNoGo(c) && !GroundRooms.IsExitCell(den, c)));
        Assert.Equal([0x01030343u, 0x0103035Cu], way); // the exit's room and the doorway to the arrival's hall
        Assert.NotNull(GroundRooms.LegGraphOf(all, den).Planner);

        foreach (var ground in GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon && g.Key != den.Key && g.Exit is { Kind: GroundExitKind.Portal } && g.Landblocks.Count == 1 && g.InLandblocks(g.Exit.Cell)))
        {
            var other = GroundRooms.ExitWay(LandblockCellSet.Load(structures, GroundRooms.CoordOf(ground)), ground);
            output.WriteLine($"{ground.Key}: exit way of {other.Count} cell(s)");
            Assert.True(other.Count == 0, $"{ground.Key}: {string.Join(' ', other.Select(c => c.ToString("X8", CultureInfo.InvariantCulture)))}");
        }
    }

    /// <summary>The Sho profile is the Aluvian profile, the landblock apart: the cells of every section, and what is the Sho den's own (its portal, its entry and its exit).</summary>
    [Fact]
    public void The_sho_profile_is_the_aluvian_profile_with_the_landblock_read_as_0x0103()
    {
        var a = GroundCatalog.Get("black-spawn-den-aluvian");
        var s = GroundCatalog.Get("black-spawn-den-sho");
        static uint Moved(uint cell) => ((uint)Sho << 16) | (cell & 0xFFFF);
        Assert.Equal([Sho], s.Landblocks);
        Assert.Equal(Moved(a.Arrival.Cell), s.Arrival.Cell);
        Assert.Equal(a.Arrival.Origin, s.Arrival.Origin);
        Assert.Equal(a.Walk, s.Walk);
        Assert.Equal(a.Storeys.Select(x => (x.Name, x.ZMin, x.ZMax)), s.Storeys.Select(x => (x.Name, x.ZMin, x.ZMax)));
        Assert.Equal(a.Walkable.Cells.Select(Moved).Order(), s.Walkable.Cells.Order());
        Assert.Equal(a.NoGo.Cells.Select(Moved).Order(), s.NoGo.Cells.Order());
        Assert.Equal(a.Targets.Select(x => (x.Wcid, x.Name, x.Level, x.Role, x.MaxHealth, x.MagicDefence)), s.Targets.Select(x => (x.Wcid, x.Name, x.Level, x.Role, x.MaxHealth, x.MagicDefence)));
        Assert.Equal(a.LevelBand, s.LevelBand);
        Assert.Equal(a.Home, s.Home);
        Assert.Equal(a.Vendors.Select(v => (v.Guid, v.Wcid, v.Cell, v.StopCell, v.BuysItemTypes, v.Sells.Count)), s.Vendors.Select(v => (v.Guid, v.Wcid, v.Cell, v.StopCell, v.BuysItemTypes, v.Sells.Count)));
        Assert.Equal(a.SpellPlan, s.SpellPlan);
        Assert.Equal(a.Tour.Select(Moved), s.Tour);
        Assert.Equal(a.Respawn.Generators.Select(g => g with { Guid = g.Guid - 0x2000, Cell = Moved(g.Cell) }), s.Respawn.Generators);
        Assert.Equal(a.Respawn.Slots.Select(x => (Generator: x.Generator - 0x2000, Cell: Moved(x.Cell), x.Wcid)), s.Respawn.Slots.Select(x => (x.Generator, x.Cell, x.Wcid)));

        // Its own: the entrance 7246 (the tie record and the entry), and the Surface Portal 7247 nearest the arrival.
        Assert.Equal((7246u, 0x73734000u, 0x3734001Bu, 47u, 48u), (s.Portal!.Wcid, s.Portal.Guid, s.Portal.Cell, s.Portal.TieSpell, s.Portal.RecallSpell));
        Assert.Equal((s.Portal.Wcid, s.Portal.Guid, s.Portal.Cell, s.Portal.Origin, s.Portal.FrontCell, s.Portal.Front), (s.Entry!.Wcid, s.Entry.Guid, s.Entry.Cell, s.Entry.Origin, s.Entry.FrontCell, s.Entry.Front));
        Assert.Equal(((int?)20, (int?)null, true), (s.Entry.MinLevel, s.Entry.MaxLevel, s.Entry.Tieable));
        Assert.Equal((GroundExitKind.Portal, 7247u, 0x01030341u, 0x37340014u), (s.Exit!.Kind, s.Exit.Wcid, s.Exit.Cell, s.Exit.Destination));
        Assert.Null(a.Entry);
        Assert.Null(a.Exit);
    }
}

/// <summary>
/// M6 item 14 (the den run), on the pinned world SQL: the rows of landblock 0x0103 are those of 0x0105 (the same 126 rows by the guid's last three digits: weenie, cell, origin, rotation and link), but for the heritage chest
/// (3994 chestwarriorshohighlocked for 3985 chestwarrioraluhighlocked) and the two Surface Portals (7247 for 7243, one of them turned), and their 117 generator links are the same.
/// </summary>
public sealed class DenCopySqlTests(ITestOutputHelper output)
{
    [RequiresPinnedWorldSqlFact]
    public void The_two_landblocks_hold_the_same_rows_but_for_the_chest_and_the_surface_portals()
    {
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) is 0x0103 or 0x0105,
            ["landblock_instance_link"] = r => ((r.L("parent_GUID") >> 12) & 0xFFFF) is 0x0103 or 0x0105,
        });
        (long Wcid, long Cell, double X, double Y, double Z, double W, double Qx, double Qy, double Qz, bool Link) Row(SqlRow r) =>
            (r.L("weenie_Class_Id"), r.L("obj_Cell_Id") & 0xFFFF, r.F("origin_X"), r.F("origin_Y"), r.F("origin_Z"), r.F("angles_W"), r.F("angles_X"), r.F("angles_Y"), r.F("angles_Z"), r.L("is_Link_Child") != 0);
        var rows = sql["landblock_instance"].View().ToArray();
        var sho = rows.Where(r => (r.L("obj_Cell_Id") >> 16) == 0x0103).ToDictionary(r => r.L("guid") & 0xFFF, Row);
        var aluvian = rows.Where(r => (r.L("obj_Cell_Id") >> 16) == 0x0105).ToDictionary(r => r.L("guid") & 0xFFF, Row);
        Assert.Equal(126, sho.Count);
        Assert.Equal(aluvian.Keys.Order(), sho.Keys.Order());
        var differing = aluvian.Keys.Where(k => aluvian[k] != sho[k]).Order().ToArray();
        foreach (var k in differing)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"guid ...{k:X3}: 0x0105 {aluvian[k]} / 0x0103 {sho[k]}"));
        }

        Assert.Equal([0x055L, 0x069L, 0x077L], differing);
        Assert.Equal((7243L, 7247L), (aluvian[0x055].Wcid, sho[0x055].Wcid));
        Assert.Equal((3985L, 3994L), (aluvian[0x069].Wcid, sho[0x069].Wcid));
        Assert.Equal((7243L, 7247L), (aluvian[0x077].Wcid, sho[0x077].Wcid));
        Assert.Equal(aluvian[0x055] with { Wcid = 0 }, sho[0x055] with { Wcid = 0 });
        Assert.Equal(aluvian[0x069] with { Wcid = 0 }, sho[0x069] with { Wcid = 0 });
        Assert.Equal(aluvian[0x077] with { Wcid = 0, W = 0, Qz = 0 }, sho[0x077] with { Wcid = 0, W = 0, Qz = 0 }); // the Aluvian den's second Surface Portal is turned (w 0.696707 z -0.717356); the Sho den's is not (w 1)

        var links = sql["landblock_instance_link"].View().Select(r => (Parent: r.L("parent_GUID"), Child: r.L("child_GUID"))).ToArray();
        var shoLinks = links.Where(l => ((l.Parent >> 12) & 0xFFFF) == 0x0103).Select(l => (l.Parent & 0xFFF, l.Child & 0xFFF)).Order().ToArray();
        var aluvianLinks = links.Where(l => ((l.Parent >> 12) & 0xFFFF) == 0x0105).Select(l => (l.Parent & 0xFFF, l.Child & 0xFFF)).Order().ToArray();
        Assert.Equal(117, shoLinks.Length);
        Assert.Equal(aluvianLinks, shoLinks);
    }
}
