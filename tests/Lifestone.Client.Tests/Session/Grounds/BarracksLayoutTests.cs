using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 guard task: the Tusker Barracks (landblock 0x5A48, the Tusker Guard Kill Task's ground) as facts of the place, checked as the Abode's pits and the Holding's plates were. Proved: the layout has no pit, no jump and no unreached
/// half (every storey cell and every respawn slot joins the arrival on foot, and the tour holds every slot); the ten "Magic trap" objects stand in the profile's six avoid boxes with their reach to spare, and no leg of the tour as the walk
/// plans it comes within that reach; and, on ACE @95ef426d and the pinned world SQL, a trap is a Switch nothing activates (no link, not usable, and a Switch casts nothing on contact), whose spell would take every enchantment, the buff bot's
/// level VII lines included, so the study's "level VI and below, the bot's VII lines survive" is the dats' description text, not the server's row.
/// </summary>
public sealed class BarracksLayoutTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint TrapWcid = 7561;

    /// <summary>The ten traps of 0x5A48, landblock-local (landblock_instance, wcid 7561; the world dump test below checks them).</summary>
    private static readonly Vector3[] Traps =
    [
        new(52.824f, -123.252f, -5.995f), new(49.7554f, -123.252f, -5.995f), new(46.7372f, -123.252f, -5.995f), new(69.9467f, -109.777f, -5.995f), new(93.32f, -123.0f, -5.995f),
        new(89.94f, -123.0f, -5.995f), new(86.47f, -123.0f, -5.995f), new(60f, -140f, 0.005f), new(69.77f, -146f, 0.005f), new(80f, -140f, 0.005f),
    ];

    /// <summary>
    /// The reach kept round a trap: the trap's own physics radius (its Setup 0x020000ED, <see cref="The_trap_is_a_small_object_and_its_spell_reaches_every_line"/>) and her body's radius. The trap fires on nothing on this server; the reach
    /// is the margin the walk keeps all the same.
    /// </summary>
    private const float Reach = 0.104f + NavCreatures.PlayerRadiusMetres;

    private static HuntingGround Barracks => GroundCatalog.Get("tusker-barracks");

    private static bool Inside(GroundAvoidRegion r, Vector3 p, float margin) =>
        p.X - margin >= r.Min.X && p.X + margin <= r.Max.X && p.Y - margin >= r.Min.Y && p.Y + margin <= r.Max.Y && p.Z >= r.Min.Z && p.Z <= r.Max.Z;

    [Fact]
    public void The_Barracks_ten_traps_are_six_avoid_boxes_with_their_reach_to_spare()
    {
        var ground = Barracks;
        Assert.Equal(6, ground.AvoidRegions.Count);
        Assert.All(ground.AvoidRegions, r => Assert.Equal((ushort)0x5A48, r.Landblock));
        Assert.All(Traps, p => Assert.Contains(ground.AvoidRegions, r => Inside(r, p, Reach)));
        Assert.Empty(ground.Jumps);
        Assert.DoesNotContain("TODO(m6-barracks-trap-radius)", ground.Sources["no_go"], StringComparison.Ordinal); // closed: the trap fires on nothing (the world test below), and the box keeps the setup's radius and her body's
        Assert.Contains("closes m6-barracks-trap-radius", ground.Sources["no_go"], StringComparison.Ordinal);
        Assert.Contains("Switch", ground.Sources["no_go"], StringComparison.Ordinal);
    }

    [RequiresPinnedWorldSqlFact]
    public void Every_trap_is_an_unlinked_switch_in_a_box_and_its_spell_would_take_every_line()
    {
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) == 0x5A48,
            ["landblock_instance_link"] = r => (r.L("parent_GUID") >> 12) == 0x75A48 || (r.L("child_GUID") >> 12) == 0x75A48,
            ["weenie"] = r => r.L("class_Id") == TrapWcid,
            ["weenie_properties_int"] = r => r.L("object_Id") == TrapWcid,
            ["weenie_properties_d_i_d"] = r => r.L("object_Id") == TrapWcid,
            ["spell"] = r => r.L("id") == 1877,
        });
        var traps = sql["landblock_instance"].View().Where(r => r.L("weenie_Class_Id") == TrapWcid).ToList();
        Assert.Equal(10, traps.Count);
        var points = traps.Select(r => new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z"))).ToList();
        Assert.All(points, p => Assert.Contains(Barracks.AvoidRegions, r => Inside(r, p, Reach)));
        Assert.All(points, p => Assert.Contains(Traps, q => Vector3.Distance(p, q) < 0.01f));

        // ACE @95ef426d: WeenieType 26 is Switch (ACE.Entity/Enum/WeenieType.cs). A player's contact reaches only a Portal, a PressurePlate, a Hotspot or a projectile (Player.OnCollideObject, WorldObjects/Player.cs:368-385); a Switch casts
        // its spell only from OnActivate through ActOnUse (WorldObjects/Switch.cs), which a link (landblock_instance_link) or a Use reaches, and ItemUseable 1 is No: so on this server the trap never fires.
        Assert.Equal(26L, Assert.Single(sql["weenie"].View()).L("type"));
        var ints = sql["weenie_properties_int"].View().ToDictionary(r => r.L("type"), r => r.L("value"));
        Assert.Equal(1L, ints[16]); // ItemUseable: No
        Assert.Equal(0x1000L, ints[83]); // ActivationResponse: CastSpell
        var guids = traps.Select(r => r.L("guid")).ToHashSet();
        Assert.DoesNotContain(sql["landblock_instance_link"].View(), r => guids.Contains(r.L("parent_GUID")) || guids.Contains(r.L("child_GUID")));
        Assert.DoesNotContain(sql["landblock_instance"].View(), r => r.L("weenie_Class_Id") != TrapWcid && sql["landblock_instance_link"].View().Any(l => l.L("parent_GUID") == r.L("guid") && guids.Contains(l.L("child_GUID"))));
        var dids = sql["weenie_properties_d_i_d"].View().ToDictionary(r => r.L("type"), r => r.L("value"));
        Assert.Equal(1877L, dids[28]); // Spell
        Assert.Equal(0x020000EDL, dids[1]); // Setup

        // The spell the server would cast: every enchantment of power up to 999, of either sign, 99 of them (EnchantmentManager.SelectDispel, WorldObjects/Managers/EnchantmentManager.cs:526-601): the bot's level VII lines (power 300 and more) too.
        var nullify = Assert.Single(sql["spell"].View());
        Assert.Equal((0L, 999L, 0L, 99L, 0L), (nullify.L("min_Power"), nullify.L("max_Power"), nullify.L("align"), nullify.L("number"), nullify.L("dispel_School")));
        Assert.Equal(0.0, nullify.F("number_Variance"));
        output.WriteLine(string.Join(", ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.#} {p.Y:0.#} {p.Z:0.#})"))));
    }

    [RequiresDatsFact]
    public void The_trap_is_a_small_object_and_its_spell_reaches_every_line()
    {
        var setup = ObjectDimensions.FromSetup(fixture.Dats, 0x020000ED, 1f);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"trap setup 0x020000ED radius {setup.PhysicsRadius:0.###}"));
        Assert.Equal(0.104f, setup.PhysicsRadius, 3); // the reach the boxes keep: this radius and her body's
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal("Nullify All Magic Other", spells[1877].Name);
        Assert.True(spells[2052].Power <= 999 && spells[2152].Power <= 999, "the bot's Armor Self VII and Bludgeoning Protection VII are within the server row's power 999: a trap that fired would take them");
    }

    [RequiresDatsFact]
    public void The_Barracks_walk_reaches_every_storey_cell_and_slot_with_no_jump()
    {
        var ground = Barracks;
        var graph = GroundRooms.Load(fixture.Dats, ground);
        Assert.Empty(ground.Jumps);
        Assert.Equal([0x5A480124u], ground.NoGo.Cells);
        var storeyCells = ground.Storeys.SelectMany(s => s.Cells.Cells).Distinct().ToList();
        var unreached = storeyCells.Where(c => c != ground.Arrival.Cell && !ground.IsNoGo(c) && graph.FindPath(ground.Arrival.Cell, c) is null).ToList(); // the exit portal's cell 0x5A480124 is the one no_go: the walk never plans into the exit
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{storeyCells.Count} storey cells in {ground.Storeys.Count} storeys ({ground.NoGo.Count} no_go); {unreached.Count} unreached from the arrival"));
        Assert.True(unreached.Count == 0, "unreached: " + string.Join(' ', unreached.Select(c => c.ToString("X8", CultureInfo.InvariantCulture))));
        var slots = ground.Respawn.Slots.Select(s => s.Cell).Distinct().ToList();
        Assert.Equal(54, ground.Respawn.Slots.Count);
        Assert.All(slots, c => Assert.True(ground.IsWalkable(c), $"0x{c:X8} is walkable"));
        Assert.All(slots, c => Assert.True(graph.FindPath(c, ground.Arrival.Cell) is not null, $"the walk joins 0x{c:X8} and the arrival both ways"));
        Assert.All(slots.Where(c => !ground.IsNoGo(c)), c => Assert.Contains(c, ground.Tour));
    }

    [RequiresDatsFact]
    public void No_leg_of_the_Barracks_tour_comes_within_a_traps_reach()
    {
        var ground = Barracks;
        var graph = GroundRooms.Load(fixture.Dats, ground);
        var log = new RecordingLogSink(LogSeverity.Info);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, log); // seeds the six boxes
        Assert.Equal(6, graph.Patches.Count);
        var stops = new List<uint> { ground.Arrival.Cell };
        stops.AddRange(ground.Tour);
        stops.Add(ground.Tour[0]);
        var at = new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity));
        var near = new List<string>();
        var moved = 0;
        for (var i = 1; i < stops.Count; i++)
        {
            var goal = legs.GoalOutOfAvoid(stops[i], at.Origin);
            Assert.True(goal is not null, $"no goal in 0x{stops[i]:X8}");
            moved += graph.RoomFrame(stops[i])!.Value.Origin == goal!.Value.Origin ? 0 : 1;
            var route = graph.Plan(at.CellId, at.Origin, stops[i], goal.Value.Origin);
            Assert.True(route is not null, $"no plan from 0x{at.CellId:X8} to 0x{stops[i]:X8}");
            var points = new List<Vector3> { at.Origin };
            points.AddRange(route!.Waypoints.Select(w => w.Origin));
            for (var s = 1; s < points.Count; s++)
            {
                foreach (var trap in Traps)
                {
                    if (MathF.Abs(trap.Z - points[s].Z) < 3f && Distance(trap, points[s - 1], points[s]) < Reach)
                    {
                        near.Add(string.Create(CultureInfo.InvariantCulture, $"0x{at.CellId:X8} to 0x{stops[i]:X8} passes {Distance(trap, points[s - 1], points[s]):0.##} m from the trap at ({trap.X:0.#} {trap.Y:0.#})"));
                    }
                }
            }

            at = goal.Value;
        }

        output.WriteLine($"{stops.Count - 1} legs, {moved} goals moved out of a box; {near.Count} within a trap's reach");
        Assert.True(near.Count == 0, string.Join("; ", near));
        Assert.True(moved >= 2, "the goals of 0x5A4801C2 and 0x5A4801E2, whose floor centres are traps, are moved");
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        var (p2, a2, b2) = (new Vector2(p.X, p.Y), new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        var d = b2 - a2;
        var t = d.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p2 - a2, d) / d.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p2, a2 + (d * t));
    }
}
