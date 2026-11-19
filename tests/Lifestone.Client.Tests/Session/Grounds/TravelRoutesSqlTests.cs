using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 4 T1 (D28): every number of the travel routes against the pinned world SQL v0.9.296 (a read-only parse of the zip): each edge's wcid, guid, cell, origin, destination and gates (PropertyInt 86, 87 and 111; an absent row is no limit), the start lifestone's
/// row, each run's entry portal row and every walk and run hint (the straight line on global x/y) recomputed from the rows; and that the Facility Hub holds no portal into the Town Network (the reason its way home goes by Eastham). Exhaustive tier, as
/// <c>SupplyWeenieSqlTests</c>: it reads about 100 MB of text; skipped without the zip.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class TravelRoutesSqlTests
{
    private const long DestinationPosition = 2;
    private const long MinLevel = 86, MaxLevel = 87, PortalBitmask = 111;
    private const long SpellDid = 28, NameString = 1;

    /// <summary>
    /// M6 4 t4b: the nine island dungeons' "Surface" exit portals (type 7, named Surface, placed in the dungeon's own landblock: Burrow 0x5748 22664, Lodge 0x5749 22689, Cave 0x5B46 22666, Cavern 0x5C43 22668, Abode 0x5C45 22655, Habitat 0x5B47 22679,
    /// Quarters 0x5B4A 22693, Barracks 0x5A48 22662, Holding 0x5B48 22681; M6 14 armory: Armory 0x5948 22658; M6 retreat: Retreat 0x5C49 22695, to 0xF97E0035), whose destination landblocks are the island place's.
    /// </summary>
    private static readonly uint[] IslandExitWcids = [22664, 22689, 22666, 22668, 22655, 22679, 22693, 22662, 22681, 22658, 22695];

    /// <summary>M6 4 t4b: a town's lifestone (weenie 509, guid 0x72780000 in 0x2780) and Zisaha's room (weenie 2314, guid 0x72481001 in 0x2481) are Fort Tethana's.</summary>
    private static readonly uint[] FortTethanaGuids = [0x72780000, 0x72481001];

    /// <summary>M6 4 F2: the Refuge's Healer (weenie 22725, guid 0x7F681000, cell 0xF6810000) stands in the island place's landblock 0xF681.</summary>
    private const uint RefugeHealerGuid = 0x7F681000;

    /// <summary>
    /// M6 8 B1: the Minor Smoldering Stone's row (weenie 6320, guid 0x7A66C000, cell 0xA66C0100): the cave's landblock 0xA66C is the Yinar place's. M6 villa: the Shivering (6321, 0x7BDB4001, 0xBDB40102: the
    /// holtburg-wilderness place's), Stinging (6319, 0x7BA60011, 0xBA600100: blackmire's) and Sparking (6318, 0x7DD34002, 0xDD340100: west-mayoi-villas') stones.
    /// </summary>
    private static readonly uint[] StoneGuids = [0x7A66C000, 0x7BDB4001, 0x7BA60011, 0x7DD34002];

    /// <summary>M6 8 B2c: Nen Ai (Shoushi's 0xD853) and Lubziklan al-Luq (Yaraq's 0x7D63) stand in landblocks no portal row holds.</summary>
    private static readonly uint[] QuestGiverGuids = [0x7D853001, 0x77D6300C];

    private static (double X, double Y) Global(uint cell, System.Numerics.Vector3 p) => (((cell >> 24) & 0xFF) * 192.0 + p.X, ((cell >> 16) & 0xFF) * 192.0 + p.Y);

    private static double Metres(uint cellA, System.Numerics.Vector3 a, uint cellB, System.Numerics.Vector3 b)
    {
        var (ax, ay) = Global(cellA, a);
        var (bx, by) = Global(cellB, b);
        return Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
    }

    /// <summary>
    /// M6 7 Z (D28): the danger map against the pinned world SQL: each cell in the file is an encounter row of its generator, and the levels, the largest awareness and the scatter are recomputed from the generator's outcomes, the camp generators among them followed
    /// (<c>weenie_properties_generator</c>, PropertyInt 25, PropertyFloat 31 and 43; ACE's default awareness 18 for a creature without the property).
    /// </summary>
    /// <summary>
    /// M6 7 AI: every portal the travel data knows has the physics <see cref="Lifestone.Client.Movement.Wayfinding.PortalKeepOff"/> reads: a Setup of the five the dat test measures, a scale of at most 1 (PropertyFloat 39), and Ethereal with ReportCollisions (PropertyInt 93), so the touch is
    /// the sphere's: the body passes through and the server moves it.
    /// </summary>
    [RequiresPinnedWorldSqlFact]
    public void Every_portal_the_travel_data_knows_has_the_physics_the_clearance_is_worked_from()
    {
        const long Setup = 1, DefaultScale = 39, PhysicsState = 93;
        const long Ethereal = 0x4, ReportCollisions = 0x8;
        var routes = TravelRoutes.Default;
        var wcids = new HashSet<uint>(routes.Edges.Select(e => e.Wcid));
        foreach (var g in routes.GroundKeys)
        {
            wcids.UnionWith(routes.RoutesFor(g).Where(r => r.Run is not null).Select(r => r.Run!.EntryWcid));
        }

        foreach (var g in GroundCatalog.All)
        {
            if (g.Entry is { } entry)
            {
                wcids.Add(entry.Wcid);
            }

            if (g.Portal is { } portal)
            {
                wcids.Add(portal.Wcid);
            }

            if (g.Exit is { Kind: GroundExitKind.Portal } exit)
            {
                wcids.Add(exit.Wcid);
            }
        }

        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["weenie_properties_d_i_d"] = r => r.L("type") == Setup && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_float"] = r => r.L("type") == DefaultScale && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_int"] = r => r.L("type") == PhysicsState && wcids.Contains((uint)r.L("object_Id")),
        });
        var setups = sql["weenie_properties_d_i_d"].View().ToDictionary(r => (uint)r.L("object_Id"), r => (uint)r.L("value"));
        var states = sql["weenie_properties_int"].View().ToDictionary(r => (uint)r.L("object_Id"), r => r.L("value"));
        foreach (var wcid in wcids)
        {
            Assert.Contains(setups[wcid], Lifestone.Client.Tests.Session.Journey.HubPortalKeepOffDatTests.PortalSetups);
            Assert.Equal(Ethereal | ReportCollisions, states[wcid] & (Ethereal | ReportCollisions));
        }

        Assert.All(sql["weenie_properties_float"].View(), r => Assert.True(r.F("value") <= 1.0, $"weenie {r.L("object_Id")} scale {r.F("value")}"));
        Assert.True(wcids.Count > 30, wcids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [RequiresPinnedWorldSqlFact]
    public void Every_danger_table_is_what_the_pinned_world_SQL_holds()
    {
        const long Level = 25, VisualAwareness = 31, GeneratorRadius = 43;
        var routes = TravelRoutes.Default;
        var tables = routes.DangerCells.Select(t => t.Weenie).ToHashSet();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["encounter"] = r => tables.Contains((uint)r.L("weenie_Class_Id")),
            ["weenie_properties_generator"] = _ => true,
            ["weenie_properties_int"] = r => r.L("type") == Level,
            ["weenie_properties_float"] = r => r.L("type") is VisualAwareness or GeneratorRadius,
        });
        var encounters = sql["encounter"].View().ToArray();
        var outcomes = sql["weenie_properties_generator"].View().ToLookup(r => (uint)r.L("object_Id"), r => (uint)r.L("weenie_Class_Id"));
        var levels = sql["weenie_properties_int"].View().ToDictionary(r => (uint)r.L("object_Id"), r => (int)r.L("value"));
        var floats = sql["weenie_properties_float"].View().ToLookup(r => (uint)r.L("object_Id"));
        double? Float(uint weenie, long type) => floats[weenie].Where(r => r.L("type") == type).Select(r => (double?)r.F("value")).FirstOrDefault();
        IEnumerable<(uint Weenie, double Scatter)> Creatures(uint weenie, double scatter)
        {
            var kids = outcomes[weenie].ToArray();
            return kids.Length == 0 ? [(weenie, scatter)] : kids.SelectMany(k => Creatures(k, scatter + (Float(weenie, GeneratorRadius) ?? 0)));
        }

        foreach (var table in routes.DangerCells)
        {
            var rows = encounters.Where(r => (uint)r.L("weenie_Class_Id") == table.Weenie).Select(r => new TravelDangerCell((ushort)r.L("landblock"), (int)r.L("cell_X"), (int)r.L("cell_Y"))).ToHashSet();
            Assert.Subset(rows, table.Cells.ToHashSet());
            var creatures = Creatures(table.Weenie, 0).Where(c => levels.ContainsKey(c.Weenie)).ToArray();
            Assert.Equal(table.MinLevel, creatures.Min(c => levels[c.Weenie]));
            Assert.Equal(table.MaxLevel, creatures.Max(c => levels[c.Weenie]));
            Assert.Equal(table.AwarenessMetres, creatures.Max(c => Float(c.Weenie, VisualAwareness) ?? 18.0));
            Assert.Equal(table.ScatterMetres, creatures.Max(c => c.Scatter));
        }
    }

    [RequiresPinnedWorldSqlFact]
    public void Every_edge_start_and_run_is_what_the_pinned_world_SQL_holds()
    {
        var routes = TravelRoutes.Default;
        var guids = new HashSet<uint>(routes.Edges.Select(e => e.Guid)) { routes.StartLifestone.Guid };
        foreach (var g in routes.GroundKeys)
        {
            foreach (var r in routes.RoutesFor(g))
            {
                if (r.Run is { } run)
                {
                    guids.Add(run.EntryGuid);
                }
            }
        }

        guids.UnionWith(FortTethanaGuids);
        guids.Add(RefugeHealerGuid);
        guids.UnionWith(StoneGuids);
        guids.UnionWith(QuestGiverGuids);
        var wcids = new HashSet<uint>(routes.Edges.Select(e => e.Wcid));
        wcids.UnionWith(IslandExitWcids);
        var gemWcids = new HashSet<uint>(routes.Gems.Select(g => g.Wcid));
        var gemSpells = new HashSet<uint>(routes.Gems.Select(g => g.Spell));
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var tables = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => guids.Contains((uint)r.L("guid")) || (r.L("obj_Cell_Id") >> 16) == 0x8A02,
            ["weenie_properties_position"] = r => r.L("position_Type") == DestinationPosition && (wcids.Contains((uint)r.L("object_Id")) || (r.L("obj_Cell_Id") >> 16) == 0x0007),
            ["weenie_properties_int"] = r => r.L("type") is MinLevel or MaxLevel or PortalBitmask && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_d_i_d"] = r => r.L("type") == SpellDid && gemWcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_string"] = r => r.L("type") == NameString && gemWcids.Contains((uint)r.L("object_Id")),
            ["spell"] = r => gemSpells.Contains((uint)r.L("id")),
        });

        var instances = tables["landblock_instance"].View().ToLookup(r => (uint)r.L("guid"));
        var destinations = tables["weenie_properties_position"].View().ToLookup(r => (uint)r.L("object_Id"));
        var ints = tables["weenie_properties_int"].View().ToLookup(r => (uint)r.L("object_Id"));

        (uint Wcid, uint Cell, System.Numerics.Vector3 Origin) Row(uint guid)
        {
            var row = Assert.Single(instances[guid]);
            return ((uint)row.L("weenie_Class_Id"), (uint)row.L("obj_Cell_Id"), new System.Numerics.Vector3((float)row.F("origin_X"), (float)row.F("origin_Y"), (float)row.F("origin_Z")));
        }

        void SameFacing(PortalFacing? expected, uint guid, string what)
        {
            var row = Assert.Single(instances[guid]);
            Assert.True(expected is { } f && Math.Abs(f.W - row.F("angles_W")) < 1e-6 && Math.Abs(f.X - row.F("angles_X")) < 1e-6 && Math.Abs(f.Y - row.F("angles_Y")) < 1e-6 && Math.Abs(f.Z - row.F("angles_Z")) < 1e-6,
                $"{what}: facing {expected} vs the SQL's angles {row.F("angles_W")}, {row.F("angles_X")}, {row.F("angles_Y")}, {row.F("angles_Z")} (guid 0x{guid:X8})");
        }

        void Same(System.Numerics.Vector3 expected, System.Numerics.Vector3 actual, string what)
        {
            Assert.True(Math.Abs(expected.X - actual.X) < 0.001 && Math.Abs(expected.Y - actual.Y) < 0.001 && Math.Abs(expected.Z - actual.Z) < 0.001, $"{what}: {expected} vs the SQL's {actual}");
        }

        foreach (var e in routes.Edges)
        {
            var row = Row(e.Guid);
            Assert.True(row.Wcid == e.Wcid, $"{e.Key}: wcid {e.Wcid} vs the SQL's {row.Wcid}");
            Assert.True(row.Cell == e.Cell, $"{e.Key}: cell 0x{e.Cell:X8} vs the SQL's 0x{row.Cell:X8}");
            Same(e.Origin, row.Origin, e.Key + " origin");
            SameFacing(e.Facing, e.Guid, e.Key);
            var dest = Assert.Single(destinations[e.Wcid]);
            Assert.True((uint)dest.L("obj_Cell_Id") == e.DestCell, $"{e.Key}: destination cell 0x{e.DestCell:X8} vs the SQL's 0x{dest.L("obj_Cell_Id"):X8}");
            Same(e.DestOrigin, new System.Numerics.Vector3((float)dest.F("origin_X"), (float)dest.F("origin_Y"), (float)dest.F("origin_Z")), e.Key + " destination");
            int? Int(long type) => ints[e.Wcid].Where(r => r.L("type") == type).Select(r => (int?)r.L("value")).FirstOrDefault();
            Assert.True(Int(MinLevel) == e.MinLevel, $"{e.Key}: MinLevel {e.MinLevel} vs the SQL's {Int(MinLevel)}");
            Assert.True(Int(MaxLevel) == e.MaxLevel, $"{e.Key}: MaxLevel {e.MaxLevel} vs the SQL's {Int(MaxLevel)}");
            Assert.True(Int(PortalBitmask) == e.Bitmask, $"{e.Key}: PortalBitmask {e.Bitmask} vs the SQL's {Int(PortalBitmask)}");
        }

        var stone = Row(routes.StartLifestone.Guid);
        Assert.Equal((routes.StartLifestone.Wcid, routes.StartLifestone.Cell), (stone.Wcid, stone.Cell));
        Same(routes.StartLifestone.Origin, stone.Origin, "start lifestone origin");

        foreach (var ground in routes.GroundKeys)
        {
            foreach (var r in routes.RoutesFor(ground))
            {
                var (cell, at) = (routes.StartLifestone.Cell, routes.StartLifestone.Origin);
                if (r.Start != routes.StartPlace)
                {
                    // M6 8 B1: a route that starts in a place other than the file's start: its hints are measured from the drop of the edge into that place.
                    var into = routes.Edges.First(e => e.To == r.Start);
                    (cell, at) = (into.DestCell, into.DestOrigin);
                }

                foreach (var s in r.Steps)
                {
                    var walk = Metres(cell, at, s.Edge.Cell, s.Edge.Origin);
                    Assert.True(Math.Abs(walk - s.WalkMetres!.Value) < 0.15, $"{ground}/{r.Key}: walk to {s.Edge.Key} {s.WalkMetres} vs {walk:F1} m from the rows");
                    (cell, at) = (s.Edge.DestCell, s.Edge.DestOrigin);
                }

                if (r.Run is { } run)
                {
                    var entry = Row(run.EntryGuid);
                    Assert.True(entry.Wcid == run.EntryWcid && entry.Cell == run.EntryCell, $"{ground}/{r.Key}: entry portal {run.EntryWcid} 0x{run.EntryCell:X8} vs the SQL's {entry.Wcid} 0x{entry.Cell:X8}");
                    Same(run.EntryOrigin, entry.Origin, ground + " entry origin");
                    SameFacing(run.EntryFacing, run.EntryGuid, ground + "/" + r.Key + " entry");
                    var m = Metres(cell, at, entry.Cell, entry.Origin);
                    Assert.True(Math.Abs(m - run.Metres) < 0.15, $"{ground}/{r.Key}: run {run.Metres} vs {m:F1} m from the rows");
                }
            }
        }

        foreach (var way in routes.WaysHome)
        {
            var first = way.Steps[0].Edge;
            var (cell, at) = (first.DestCell, first.DestOrigin);
            foreach (var s in way.Steps.Skip(1))
            {
                var walk = Metres(cell, at, s.Edge.Cell, s.Edge.Origin);
                Assert.True(Math.Abs(walk - s.WalkMetres!.Value) < 0.15, $"way {way.Key}: walk to {s.Edge.Key} {s.WalkMetres} vs {walk:F1} m from the rows");
                (cell, at) = (s.Edge.DestCell, s.Edge.DestOrigin);
            }

            foreach (var h in way.FirstWalkHints)
            {
                var arrived = routes.Edge(h.ArrivedBy);
                var walk = Metres(arrived.DestCell, arrived.DestOrigin, first.Cell, first.Origin);
                Assert.True(Math.Abs(walk - h.WalkMetres) < 0.15, $"way {way.Key}: first walk after {h.ArrivedBy} {h.WalkMetres} vs {walk:F1} m from the rows");
            }
        }

        // M6 4 T3a (closes TODO(m6-gem-in-travel-file)): the gem's weenie, its spell (PropertyDataId 28) and the spell row's destination position.
        var gemSpellRows = tables["spell"].View().ToLookup(r => (uint)r.L("id"));
        foreach (var gem in routes.Gems)
        {
            Assert.Equal(gem.Name, Assert.Single(tables["weenie_properties_string"].View(), r => (uint)r.L("object_Id") == gem.Wcid).S("value"));
            Assert.Equal(gem.Spell, (uint)Assert.Single(tables["weenie_properties_d_i_d"].View(), r => (uint)r.L("object_Id") == gem.Wcid).L("value"));
            var spell = Assert.Single(gemSpellRows[gem.Spell]);
            Assert.Equal(gem.SpellName, spell.S("name"));
            Assert.True((uint)spell.L("position_Obj_Cell_ID") == gem.DestCell, $"{gem.Key}: destination cell 0x{gem.DestCell:X8} vs the SQL's 0x{spell.L("position_Obj_Cell_ID"):X8}");
            Same(gem.DestOrigin, new System.Numerics.Vector3((float)spell.F("position_Origin_X"), (float)spell.F("position_Origin_Y"), (float)spell.F("position_Origin_Z")), gem.Key + " destination");
        }

        // M6 4 t4b: every island dungeon's Surface exit drops into a landblock of the island place (the Burrow's 22664 drops at 0xF9820039, landblock 0xF982), and Fort Tethana's lifestone and Zisaha's room are in Fort Tethana.
        var island = routes.PlaceOf("island")!;
        foreach (var exit in IslandExitWcids)
        {
            var drop = (uint)Assert.Single(destinations[exit]).L("obj_Cell_Id");
            Assert.True(island.Landblocks.Contains((ushort)(drop >> 16)), $"exit {exit} drops at 0x{drop:X8}: landblock 0x{drop >> 16:X4} is not in the island place");
        }

        foreach (var guid in FortTethanaGuids)
        {
            var row = Row(guid);
            Assert.True(routes.PlaceOf("0x" + (row.Cell >> 16).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))?.Key == "fort-tethana", $"guid 0x{guid:X8} (weenie {row.Wcid}) stands in 0x{row.Cell >> 16:X4}, which is not a Fort Tethana landblock");
        }

        // M6 4 F2 (4-R11, D28): this test is the places' source. Every landblock of every place is a landblock of a row it already checks: an edge's cell or destination, the start lifestone, the gem's destination, an island exit's drop or a
        // Fort Tethana guid's cell (the file's places carry this as their `source`).
        var rowLandblocks = new HashSet<ushort>();
        foreach (var e in routes.Edges)
        {
            rowLandblocks.Add((ushort)(e.Cell >> 16));
            rowLandblocks.Add((ushort)(e.DestCell >> 16));
        }

        rowLandblocks.Add((ushort)(routes.StartLifestone.Cell >> 16));
        rowLandblocks.Add((ushort)(Row(RefugeHealerGuid).Cell >> 16));
        foreach (var guid in StoneGuids)
        {
            rowLandblocks.Add((ushort)(Row(guid).Cell >> 16));
        }

        foreach (var guid in QuestGiverGuids)
        {
            rowLandblocks.Add((ushort)(Row(guid).Cell >> 16));
        }

        foreach (var ground in routes.GroundKeys)
        {
            foreach (var route in routes.RoutesFor(ground))
            {
                if (route.Run is { } entryRun)
                {
                    rowLandblocks.Add((ushort)(entryRun.EntryCell >> 16)); // M6 8 B1: the Crater Caves portal stands in 0x8FD2, the crater-lake place's
                }
            }
        }
        foreach (var gem in routes.Gems)
        {
            rowLandblocks.Add((ushort)(gem.DestCell >> 16));
        }

        foreach (var exit in IslandExitWcids)
        {
            rowLandblocks.Add((ushort)(Assert.Single(destinations[exit]).L("obj_Cell_Id") >> 16));
        }

        foreach (var guid in FortTethanaGuids)
        {
            rowLandblocks.Add((ushort)(Row(guid).Cell >> 16));
        }

        foreach (var place in routes.Places)
        {
            Assert.False(string.IsNullOrWhiteSpace(place.Source), $"place {place.Key} names no source");
            foreach (var landblock in place.Landblocks)
            {
                Assert.True(rowLandblocks.Contains(landblock), $"place {place.Key}: landblock 0x{landblock:X4} is the landblock of no row the file checks");
            }
        }

        // The Town Network's way home: its first walk hints are recomputed above with the other ways; its portal is the one Holtburg's drop leads to.
        Assert.Contains(routes.WaysHome, w => w.From == "town-network" && w.Steps[0].Edge.To == routes.StartPlace);

        // The Facility Hub (landblock 0x8A02) holds no portal into the Town Network (landblock 0x0007): the reason its way home goes by the Eastham Portal.
        var intoNetwork = destinations.Where(g => g.Any(r => (r.L("obj_Cell_Id") >> 16) == 0x0007)).Select(g => g.Key).ToHashSet();
        Assert.DoesNotContain(tables["landblock_instance"].View().Where(r => (r.L("obj_Cell_Id") >> 16) == 0x8A02), r => intoNetwork.Contains((uint)r.L("weenie_Class_Id")));
    }
}
