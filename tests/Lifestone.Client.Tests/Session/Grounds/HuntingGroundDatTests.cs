using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The hunting-ground profiles against the retail dats (M5 item 2 g3; DoD 4, D42, D43, D46). One check per profile, whatever the ground: every dungeon's arrival is a place a human stands (inside its cell, over a floor);
/// every outdoor cell reads the terrain its profile says (water by the terrain type of the cell's four corner vertices, types 16 to 20, ACE's table <c>LandblockStruct.cs:62</c>; slope by the surface normal under
/// a 5 x 5 grid of points against <see cref="PhysicsGlobals.FloorZ"/>, as M4's <c>HuntGroundDatTests</c> read the same cells); every forest spot, the generator's corner the hunt now stops at, is dry and walkable
/// (<c>m5-hunt-band-empty</c>); and every dungeon's components cover the war line's formula at every spell level its spell plan names. Tests that need the dats skip when the dats are missing.
/// </summary>
public sealed class HuntingGroundDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FirstWater = 16, LastWater = 20, Grid = 5;

    /// <summary>How far under the arrival's z a floor may be and still be stood on (the origin is the player's feet; the dats' floors are polygons, the origin a figure of the world data), in metres.</summary>
    private const float FloorSlackMetres = 0.5f;

    /// <summary>The key of the 5-9 rung's own ground (M6 item 0, slice belt): Holtburg's outskirts, the cells of the Newbie Aluvian Generator round the town.</summary>
    public const string OutskirtsKey = "holtburg-outskirts";

    public static TheoryData<string> OutdoorKeys() => new() { HuntGround.BeltKey, HuntGround.ForestKey, OutskirtsKey };

    /// <summary>What the dats say of an outdoor cell: water at a corner vertex, else a normal under the physics floor, else dry (the rule of <c>HuntGroundDatTests.Read</c>).</summary>
    private GroundTerrain Read(OutdoorCell cell, out float lowestNormalZ)
    {
        var terrain = fixture.Dats.GetLandblockTerrain(LandblockCoord.FromId(cell.Cell)) ?? throw new InvalidOperationException("no terrain for " + LandblockCoord.FromId(cell.Cell));
        var index = (int)(cell.Cell & 0xFFFF) - 1;
        var cx = index / 8;
        var cy = index % 8;
        var water = false;
        for (var vx = cx; vx <= cx + 1; vx++)
        {
            for (var vy = cy; vy <= cy + 1; vy++)
            {
                water |= terrain.TerrainType(vx, vy) is >= FirstWater and <= LastWater;
            }
        }

        var sampler = new TerrainSampler(terrain, fixture.Dats.TerrainRegion.LandHeightTable);
        lowestNormalZ = 1f;
        for (var i = 0; i < Grid; i++)
        {
            for (var j = 0; j < Grid; j++)
            {
                lowestNormalZ = Math.Min(lowestNormalZ, sampler.GetNormal((cx * 24) + 1 + (i * 22f / (Grid - 1)), (cy * 24) + 1 + (j * 22f / (Grid - 1))).Z);
            }
        }

        return water ? GroundTerrain.Water : lowestNormalZ < PhysicsGlobals.FloorZ ? GroundTerrain.Steep : GroundTerrain.Dry;
    }

    /// <summary>The keys of every dungeon profile (D46: a new dungeon's JSON file joins the dungeon checks here with no edit).</summary>
    public static TheoryData<string> DungeonKeys() => new(GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));

    [RequiresDatsTheory]
    [MemberData(nameof(DungeonKeys))]
    public void A_dungeons_arrival_is_a_place_a_human_stands_inside_its_cell_over_a_floor_of_its_walkable_set(string key)
    {
        var ground = GroundCatalog.Get(key);
        var arrival = ground.Arrival;
        var coord = LandblockCoord.FromId(arrival.Cell);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), coord);

        var cell = cells.GetCell(arrival.Cell);
        Assert.NotNull(cell);
        Assert.True(cells.Contains(cell, arrival.Origin), $"the arrival point {arrival.Origin} is not inside cell 0x{arrival.Cell:X8} (CEnvCell::point_in_cell)");
        Assert.Equal(arrival.Cell, cells.FindCell(arrival.Origin, arrival.Cell)!.Id);

        // Over a floor: a floor polygon of the cell holds the point's footprint, and the feet are on it (within FloorSlackMetres above or below the plane).
        var local = cell.Frame.ParentToLocal(arrival.Origin);
        var floors = cells.GetStructure(cell).Floors.Where(f => FootprintHolds(f.Vertices, local.X, local.Y)).ToArray();
        Assert.NotEmpty(floors);
        var heights = floors.Select(f => local.Z - (-(f.Plane.D + (f.Plane.Normal.X * local.X) + (f.Plane.Normal.Y * local.Y)) / f.Plane.Normal.Z)).ToArray();
        output.WriteLine($"arrival 0x{arrival.Cell:X8} {arrival.Origin}: cell-local {local}, {floors.Length} floor polygon(s) under the footprint, feet above the plane by {string.Join(", ", heights.Select(h => h.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))} m");
        Assert.Contains(heights, h => h >= -FloorSlackMetres && h <= FloorSlackMetres);

        Assert.True(ground.IsWalkable(arrival.Cell), $"{key}: the arrival cell is not a walkable cell of the profile");
        Assert.True(ground.InLandblocks(arrival.Cell));
        Assert.NotNull(new RoomGraph(cells, ground.Walk.DoorwayLean).RoomFrame(arrival.Cell)); // the room graph at the profile's lean has a room to walk from
    }

    [RequiresDatsTheory]
    [MemberData(nameof(OutdoorKeys))]
    public void Every_cell_of_the_profile_reads_the_terrain_it_says(string key)
    {
        var ground = GroundCatalog.Get(key);
        var disagree = new List<string>();
        foreach (var cell in ground.Outdoor!.Cells)
        {
            var read = Read(cell, out var normalZ);
            output.WriteLine($"{key} 0x{cell.Cell:X8} ({cell.North:0.00}N {cell.East:0.00}E): read {read}, lowest normal z {normalZ:0.000} (floor {PhysicsGlobals.FloorZ:0.000}); the profile says {cell.Terrain}");
            if (read != cell.Terrain)
            {
                disagree.Add($"0x{cell.Cell:X8} reads {read} (lowest normal z {normalZ:0.000}), the profile says {cell.Terrain}");
            }
        }

        Assert.True(disagree.Count == 0, $"{key}: cells the dats do not read as the profile says: " + string.Join("; ", disagree));
        Assert.All(ground.Outdoor.Cells, c => Assert.NotEqual(GroundTerrain.Unread, c.Terrain));
    }

    [RequiresDatsFact]
    public void Every_forest_spot_is_dry_and_walkable_and_the_stop_point_reads_a_terrain_height()
    {
        var forest = GroundCatalog.Get(HuntGround.ForestKey);
        Assert.Equal(33, forest.Outdoor!.Cells.Count);
        var failures = new List<string>();
        foreach (var cell in forest.Outdoor.Cells)
        {
            var coord = LandblockCoord.FromId(cell.Cell);
            var terrain = fixture.Dats.GetLandblockTerrain(coord) ?? throw new InvalidOperationException("no terrain for " + coord);
            var sampler = new TerrainSampler(terrain, fixture.Dats.TerrainRegion.LandHeightTable);
            var index = (int)(cell.Cell & 0xFFFF) - 1;

            // The spot lies in its own cell (it is the cell's low corner) and in the landblock, and its vertex and the cell's corners are not water ...
            var inCell = (int)(cell.Spot.X / 24) == index / 8 && (int)(cell.Spot.Y / 24) == index % 8 && cell.Spot.X >= 0 && cell.Spot.Y >= 0 && cell.Spot.X < 192 && cell.Spot.Y < 192;
            // ... the hunt's own seek rule accepts it (HuntGround.SeekHeight: no water at the cell's corners, every normal over its 3 x 3 grid walkable) and the normal at the spot itself is walkable ...
            var height = inCell ? HuntGround.SeekHeight(fixture.Dats, cell.Cell, cell.Spot.X, cell.Spot.Y) : null;
            var normalZ = inCell ? sampler.GetNormal(cell.Spot.X, cell.Spot.Y).Z : float.NaN;
            output.WriteLine($"0x{cell.Cell:X8} spot ({cell.Spot.X:0}, {cell.Spot.Y:0}): in cell {inCell}, seek height {height?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, normal z at the spot {normalZ:0.000}");
            if (!inCell || height is null || !(normalZ >= PhysicsGlobals.FloorZ))
            {
                failures.Add($"0x{cell.Cell:X8} spot ({cell.Spot.X:0}, {cell.Spot.Y:0}): in cell {inCell}, height {height?.ToString() ?? "none (water or too steep)"}, normal z {normalZ:0.000}");
            }

            // ... and the hunt's stop point reads a height through the row, the one the hunt places the player at.
            var row = HuntGround.LowNorthForestMix.Cells.Single(c => c.CellId == cell.Cell);
            Assert.Equal((cell.Spot.X, cell.Spot.Y), (row.StopX, row.StopY));
            Assert.NotNull(HuntGround.TerrainHeight(fixture.Dats, row));
        }

        Assert.True(failures.Count == 0, "forest spots that are not dry and walkable: " + string.Join("; ", failures));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(DungeonKeys))]
    public void A_dungeons_components_cover_the_war_lines_formula_at_every_spell_level_its_plan_names(string key)
    {
        var ground = GroundCatalog.Get(key);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.NotEmpty(ground.SpellPlan);
        foreach (var row in ground.SpellPlan)
        {
            // M6 8 B1: a ground that plans another line than the fight's (the Crater Caves' Frost Bolt, TODO(m6-fight-element-line)) names it by the line and the numeral: the foci formula is the same for every war line of a level.
            SpellInfo info;
            if (ArenaProfile.Lines.SingleOrDefault(l => l.Name == row.Family) is { } line)
            {
                var spell = line.Spells.Single(s => s.Position == row.SpellLevel);
                info = spells[spell.Id];
                Assert.Equal(spell.Name, info.Name);
            }
            else
            {
                var name = $"{row.Family} {new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" }[row.SpellLevel - 1]}";
                info = spells.Values.Single(s => s.Name == name);
            }

            // The cast of a mage that holds its school's foci burns the foci formula (the kit carries it: GearKit.FociFormulaCounts): the level's scarab, then Prismatic Tapers by its power.
            var needed = GearKit.FociFormulaCounts(info.Formula);
            var have = ground.Components.Where(c => c.SpellLevel == row.SpellLevel).ToDictionary(c => c.Wcid, c => c.PerCast);
            var wanted = needed.ToDictionary(
                n => ComponentWeenies.TryGetByComponent(n.Key, out var w) ? w.Wcid : throw new InvalidOperationException($"component {n.Key} of {info.Name} is not in the component map"),
                n => n.Value);
            output.WriteLine($"{info.Name} (spell {info.Id}, character levels {row.MinLevel}-{row.MaxLevel}): the formula needs {string.Join(", ", wanted.Select(w => $"{w.Value} x wcid {w.Key}"))}; the profile holds {string.Join(", ", have.Select(h => $"{h.Value} x wcid {h.Key}"))}");

            Assert.NotEmpty(wanted);
            foreach (var (wcid, count) in wanted)
            {
                Assert.True(have.TryGetValue(wcid, out var perCast) && perCast >= count, $"{info.Name}: the formula burns {count} x wcid {wcid}, the profile's components for spell level {row.SpellLevel} hold {(have.TryGetValue(wcid, out var p) ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}");
            }

            // and nothing the formula does not burn: M6 3 F3 (closes TODO(m6-dungeon-account-formula)) widens "the formula" to the foci formula plus, on a climber's ground, the account test's lines
            // (the lines of a school whose focus Winnie does not hold: SpellFormulaRules.CustomizeForAccount; ClimberAccountKeys), which A_climbers_dungeon_carries_the_account_test_formula_beside_the_foci_formula pins in full.
            var allowed = new HashSet<uint>(wanted.Keys);
            if (ClimberAccountKeys.Contains(key))
            {
                foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, "test").Where(i => i != 0))
                {
                    allowed.Add(ComponentWeenies.TryGetByComponent(id, out var w) ? w.Wcid : throw new InvalidOperationException($"component {id} is not in the component map"));
                }
            }

            Assert.Empty(have.Keys.Except(allowed));
        }
    }

    /// <summary>The dungeons the climber (Winnie, account <c>test</c>) hunts (M6 3 F3); M6 14 holding safety: the Holding too, its kit now the climber's general one (no longer BSD Mage's den lines); M6 14 armory: the Armory, the Holding rung's alternate with its kit; M6 redoubt: the Holtburg Redoubt, built for the rung outskirts.</summary>
    public static readonly string[] ClimberAccountKeys =
    [
        "tusker-burrow", "tusker-lodge", "tusker-cavern", "tusker-cave", "tusker-temple-20-40", "tusker-abode", "tusker-habitat", "tusker-quarters", "tusker-barracks", "cave-of-alabree", "tusker-holding", "tusker-armory", "holtburg-redoubt", "tusker-retreat",
    ];

    public static TheoryData<string> ClimberKeys() => new(ClimberAccountKeys);

    /// <summary>M6 sprint (2026-11-18): Archmage Cindrue's weenie class.</summary>
    private const uint Cindrue = 2304;

    /// <summary>
    /// M6 sprint (2026-11-18): the five spell components the profiles' rows of Archmage Cindrue lacked, with the price of her rate (max(1, ceil(1.35 x Value - 0.1)), Vendor.cs 577-585 @95ef426d; Values 5, 10, 10, 5 and 5: her
    /// create-list rows 21033, 20980, 20982, 21003 and 21007, a read-only SELECT of 2026-11-18 on the world copy, named in each profile's sources.vendors).
    /// </summary>
    private static readonly (uint Wcid, string Name, int Price)[] CindrueComponents = [(746, "Hazel Talisman", 7), (767, "Comfrey", 14), (769, "Dragonsblood", 14), (788, "Powdered Malachite", 7), (792, "Powdered Turquoise", 7)];

    /// <summary>Every profile that lists Archmage Cindrue.</summary>
    public static TheoryData<string> CindrueKeys() => new(GroundCatalog.All.Where(g => g.Vendors.Any(v => v.Wcid == Cindrue)).Select(g => g.Key));

    /// <summary>Every profile that lists Archmage Cindrue, for each of the climbers' accounts (Winnie's <c>test</c>, Qwinnie's <c>qwinnie</c>: CLAUDE.md, the accounts).</summary>
    public static TheoryData<string, string> CindrueKeysByAccount()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in GroundCatalog.All.Where(g => g.Vendors.Any(v => v.Wcid == Cindrue)).Select(g => g.Key))
        {
            data.Add(key, "test");
            data.Add(key, "qwinnie");
        }

        return data;
    }

    /// <summary>
    /// M6 sprint (the live log of 2026-11-18, 20:25:31 UTC: the learn step named Comfrey, Powdered Malachite, Dragonsblood, Hazel Talisman and Powdered Turquoise short, "no vendor of Holtburg sells it", though the world's
    /// create list of Archmage Cindrue holds all five): every profile's row of her sells the five at her rate, so the learn step's shopping list finds a vendor for each (wares only: <c>TODO(m6-recall-components-stock)</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(CindrueKeys))]
    public void Every_profiles_row_of_Archmage_Cindrue_sells_the_five_components_the_live_log_named_short(string key)
    {
        var ground = GroundCatalog.Get(key);
        var cindrue = Assert.Single(ground.Vendors, v => v.Wcid == Cindrue);
        foreach (var (wcid, name, price) in CindrueComponents)
        {
            var ware = Assert.Single(cindrue.Sells, w => w.Wcid == wcid);
            Assert.Equal((name, price), (ware.Name, ware.Price));
            Assert.True(ComponentWeenies.TryGetByWcid(wcid, out _), $"{name} is a spell component: the loader takes it as a ware without a supply line");
        }
    }

    /// <summary>
    /// M6 sprint (the live log of 2026-11-18, 20:29:54 UTC: the server refused Lifestone Recall, YouDontHaveAllTheComponents): every component Lifestone Recall (spell 1635) burns on the full formula of a climber's account
    /// (<see cref="SpellFormulaRules.CustomizeForAccount"/>, the dats) is a ware of a vendor of every profile that lists Archmage Cindrue, so a town trip can buy the way home's components.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(CindrueKeysByAccount))]
    public void A_profile_with_Archmage_Cindrue_sells_every_component_Lifestone_Recall_burns_on_a_climbers_account_formula(string key, string account)
    {
        var ground = GroundCatalog.Get(key);
        var info = SpellTableData.Load(fixture.Dats).Spells[Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell];
        var missing = new List<string>();
        foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account).Where(i => i != 0).Distinct())
        {
            var weenie = ComponentWeenies.TryGetByComponent(id, out var w) ? w : throw new InvalidOperationException($"component {id} is not in the component map");
            var seller = ground.Vendors.FirstOrDefault(v => v.Sells.Any(s => s.Wcid == weenie.Wcid));
            output.WriteLine($"{key} {account}: {info.Name} burns wcid {weenie.Wcid}: {(seller is null ? "no vendor of the profile sells it" : "sold by " + seller.Name)}");
            if (seller is null)
            {
                missing.Add($"wcid {weenie.Wcid}");
            }
        }

        Assert.True(missing.Count == 0, $"{key}, account {account}: no vendor of the profile sells a component of {info.Name}: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// M6 7 X (spells are the armor): the climber's low grounds stock every component of the protective lines' levels the rung reaches, on the account test's formula (<see cref="SpellFormulaRules.CustomizeForAccount"/>: she holds no focus of Life
    /// Magic), a stock line of each, sold by a vendor of the profile, so a scroll the plan buys can be cast.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("holtburg-outskirts", "Armor Self", 1, 2)]
    [InlineData("holtburg-outskirts", "Mana Renewal Self", 1, 2)]
    [InlineData("tusker-burrow", "Armor Self", 2, 3)]
    [InlineData("tusker-burrow", "Bludgeoning Protection Self", 2, 3)]
    [InlineData("tusker-burrow", "Mana Renewal Self", 2, 3)]
    [InlineData("tusker-retreat", "Armor Self", 2, 3)] // M6 retreat: the 10-14 rung's ground, the Burrow's lines
    [InlineData("tusker-retreat", "Bludgeoning Protection Self", 2, 3)]
    [InlineData("tusker-retreat", "Mana Renewal Self", 2, 3)]
    [InlineData("tusker-lodge", "Armor Self", 2, 3)]
    [InlineData("tusker-lodge", "Bludgeoning Protection Self", 2, 3)]
    [InlineData("tusker-lodge", "Mana Renewal Self", 2, 3)]
    [InlineData("holtburg-outskirts", "Stamina to Mana Self", 1, 1)]
    [InlineData("tusker-burrow", "Stamina to Mana Self", 1, 1)]
    [InlineData("tusker-retreat", "Stamina to Mana Self", 1, 1)]
    [InlineData("tusker-lodge", "Stamina to Mana Self", 1, 1)]
    public void A_climbers_low_grounds_carry_the_protective_lines_formula(string key, string line, int from, int to)
    {
        var ground = GroundCatalog.Get(key);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var spell in ScrivenerCatalog.Default.LineSpells(line).Where(s => s.Level >= from && s.Level <= to))
        {
            foreach (var id in SpellFormulaRules.CustomizeForAccount(spells[spell.SpellId].Formula, spells[spell.SpellId].FormulaVersion, "test").Where(i => i != 0))
            {
                Assert.True(ComponentWeenies.TryGetByComponent(id, out var w), $"component {id}");
                Assert.True(ground.Supplies.StockOf(w.Wcid) is not null, $"{key} {spell.Name}: no stock line of {w.Name} {w.Wcid}");
                Assert.True(ground.VendorSelling(w.Wcid) is not null, $"{key} {spell.Name}: no vendor sells {w.Name} {w.Wcid}");
            }
        }
    }

    /// <summary>
    /// M6 7 AD: what a press costs a caster with no Mana Conversion is the SpellTable's base mana, and the plan's level turns on it (<see cref="BuffStatMods.StaminaToManaBaseMana"/>). Revitalize Self's base is pinned beside it
    /// (<see cref="BuffStatMods.RevitalizeBaseMana"/>): at level 10 the reason it stayed off the plan, and since M6 14 what the plan weighs a round on (<see cref="ScrivenerLearnPlan.RevitalizeTarget"/>). M6 14: the level floor pays a level by
    /// its foci formula's scarab, so Revitalize Self VI burns a Pyreal Scarab 690 and VII (Robustification) a Platinum Scarab 8897, both of which she carries.
    /// </summary>
    [RequiresDatsFact]
    public void The_stamina_to_mana_base_mana_is_the_spell_tables()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(BuffStatMods.StaminaToManaBaseMana, VitalsMeans.StaminaToManaSelf.Select(id => spells[id].BaseMana));
        Assert.Equal([15u, 30u, 40u, 50u, 60u, 70u], VitalsMeans.RevitalizeSelf.Take(6).Select(id => spells[id].BaseMana));
        Assert.Equal(BuffStatMods.RevitalizeBaseMana, VitalsMeans.RevitalizeSelf.Take(6).Select(id => spells[id].BaseMana));
        uint Scarab(uint id) => GearKit.FociFormulaCounts(spells[id].Formula).Keys.Select(c => ComponentWeenies.TryGetByComponent(c, out var w) ? w : default).Single(w => w.Name?.EndsWith(" Scarab", StringComparison.Ordinal) == true).Wcid;
        Assert.Equal((690u, 8897u), (Scarab(1182), Scarab(2083)));
    }

    /// <summary>
    /// M6 3 F3 (3-R10, closes <c>m6-dungeon-account-formula</c> for the ladder's dungeons): Winnie on the account <c>test</c> holds no focus once the Foci of Strife is lost, and then ACE casts the account's full formula
    /// (<see cref="SpellFormulaRules.CustomizeForAccount"/>). For each spell level of the profile's plan, every component of that formula is a component line of the profile at that level (at least the slots it burns), a stock line
    /// that stops the loop at a count that still casts, and either sold by a vendor the profile lists or a carried line the profile names (<c>TODO(m6-island-vendors)</c> in its components source). The foci formula stays pinned
    /// by <see cref="A_dungeons_components_cover_the_war_lines_formula_at_every_spell_level_its_plan_names"/>. M6 item 14 (m6-goldscarab): a component the profile buys in another town (its <c>town_stock</c>: the Gold Scarab of level V and the
    /// Pyreal Scarab of level VI at Uziz) is no stock line and has no stop line: its town stock line keeps and restocks at least a cast's worth, and the level floor casts the level under it while the pack holds none.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(ClimberKeys))]
    public void A_climbers_dungeon_carries_the_account_test_formula_beside_the_foci_formula(string key)
    {
        var ground = GroundCatalog.Get(key);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var missing = new List<string>();
        var carried = new List<string>();
        foreach (var row in ground.SpellPlan)
        {
            var info = spells[ArenaProfile.Lines.Single(l => l.Name == row.Family).Spells.Single(s => s.Position == row.SpellLevel).Id];
            var wanted = new Dictionary<uint, int>();
            foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, "test").Where(i => i != 0))
            {
                var wcid = ComponentWeenies.TryGetByComponent(id, out var w) ? w.Wcid : throw new InvalidOperationException($"component {id} is not in the component map");
                wanted[wcid] = wanted.GetValueOrDefault(wcid) + 1;
            }

            foreach (var (wcid, perCast) in wanted)
            {
                var have = ground.Components.Where(c => c.Wcid == wcid && c.SpellLevel == row.SpellLevel).Select(c => c.PerCast).DefaultIfEmpty(0).Max();
                var stock = ground.Supplies.StockOf(wcid);
                output.WriteLine($"{key} {info.Name}: wcid {wcid} x{perCast}: component {have}, stock {(stock is null ? "none" : stock.StopUnder.ToString(System.Globalization.CultureInfo.InvariantCulture))}, {(ground.IsCarried(wcid) ? "carried" : "sold by " + ground.VendorSelling(wcid)!.Name)}");
                if (have < perCast)
                {
                    missing.Add($"{info.Name}: component wcid {wcid} x{perCast} (profile {have})");
                }

                if (stock is null && ground.TownStockOf(wcid) is { } town)
                {
                    // M6 item 14 (m6-goldscarab): bought in another town on the way out; never a stop
                    Assert.True(town.Keep >= perCast && town.TripUnder >= perCast, $"{key} {info.Name}: the town stock line of wcid {wcid} keeps {town.Keep} and restocks under {town.TripUnder}, a cast burns {perCast}");
                    Assert.True(ComponentWeenies.TryGetByWcid(wcid, out _));
                    continue;
                }

                if (stock is null || stock.StopUnder < perCast)
                {
                    missing.Add($"{info.Name}: stock line wcid {wcid} stops under {(stock is null ? "none" : stock.StopUnder.ToString(System.Globalization.CultureInfo.InvariantCulture))}, a cast burns {perCast}");
                }

                if (ground.IsCarried(wcid))
                {
                    carried.Add($"wcid {wcid}");
                }
            }
        }

        Assert.True(missing.Count == 0, key + ": the account test's formula is not carried: " + string.Join("; ", missing));
        if (carried.Count > 0)
        {
            Assert.Contains("TODO(m6-island-vendors)", ground.Sources["components"], StringComparison.Ordinal); // a carried line is named, not silent
        }
    }

    /// <summary>
    /// M6 item 0 (slice belt): the outskirts profile's components are the formula the account <c>test</c> (Winnie's) casts, not the foci formula: for each spell level its plan reaches, the components are exactly the slots of
    /// <see cref="SpellFormulaRules.CustomizeForAccount"/> over the line's spell at that position, every one is a stock line a vendor of the profile sells, and a stack that stops the loop stops it at a count that still casts
    /// (stop_under at least the slots per cast). The foci formula's wares (the Prismatic Taper and the scarab, for a mage that keeps its starter Foci of Strife) and the level I self spells' (Armor Self I, Heal Self I) are stock lines too.
    /// </summary>
    [RequiresDatsFact]
    public void The_outskirts_components_are_the_full_formula_of_the_account_test()
    {
        const string account = "test";
        var ground = GroundCatalog.Get(OutskirtsKey);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.NotEmpty(ground.SpellPlan);

        static Dictionary<uint, int> WcidCounts(IEnumerable<uint> componentIds)
        {
            var counts = new Dictionary<uint, int>();
            foreach (var id in componentIds.Where(i => i != 0))
            {
                var wcid = ComponentWeenies.TryGetByComponent(id, out var w) ? w.Wcid : throw new InvalidOperationException($"component {id} is not in the component map");
                counts[wcid] = counts.GetValueOrDefault(wcid) + 1;
            }

            return counts;
        }

        void RequireStocked(string what, Dictionary<uint, int> wanted)
        {
            foreach (var (wcid, perCast) in wanted)
            {
                var line = ground.Supplies.StockOf(wcid);
                Assert.True(line is not null, $"{what}: wcid {wcid} is not a stock line");
                Assert.True(line!.StopUnder >= perCast, $"{what}: {line.Name} stops the loop under {line.StopUnder}, a cast burns {perCast}");
                Assert.True(ground.VendorSelling(wcid) is not null, $"{what}: no vendor of the profile sells {line.Name}");
            }
        }

        var foci = (SpellInfo info) => WcidCounts(GearKit.FociFormulaCounts(info.Formula).SelectMany(f => Enumerable.Repeat(f.Key, f.Value)));
        foreach (var row in ground.SpellPlan)
        {
            var line = ArenaProfile.Lines.Single(l => l.Name == row.Family);
            for (var level = 1; level <= row.SpellLevel; level++)
            {
                var atLevel = line.Spells.Single(sp => sp.Position == level);
                var info = spells[atLevel.Id];
                var wanted = WcidCounts(SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account));
                var have = ground.Components.Where(c => c.SpellLevel == level).ToDictionary(c => c.Wcid, c => c.PerCast);
                output.WriteLine($"{info.Name} (spell {atLevel.Id}) on account {account}: needs {string.Join(", ", wanted.Select(w => $"{w.Value} x wcid {w.Key}"))}; the profile holds {string.Join(", ", have.Select(h => $"{h.Value} x wcid {h.Key}"))}");
                Assert.Equal(wanted.OrderBy(w => w.Key), have.OrderBy(h => h.Key));
                RequireStocked(info.Name, wanted);
                RequireStocked(info.Name + " (foci formula)", foci(info));
            }

            Assert.Equal(ground.SpellPlan.Max(r => r.SpellLevel), ground.Components.Max(c => c.SpellLevel)); // M6 7 AR: two rows now
        }

        foreach (var id in new uint[] { 24, 6 }) // Armor Self I and Heal Self I: the level I self spells of the War Magic specialist who trains Life Magic
        {
            var info = spells[id];
            RequireStocked(info.Name, WcidCounts(SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account)));
            RequireStocked(info.Name + " (foci formula)", foci(info));
        }
    }

    /// <summary>
    /// M6 item 0 (slice belt): the outskirts profile as data (no dats): the cells stand in its landblocks and are dry, the creatures' levels and masses make the band and the yield it says, the hunt's band for a level 5 and a level 9 mage is
    /// 5-8, the Holtburg lifestone is the den's, every stock line has a vendor and a price, and one full restock at the keeps (less the starter pack's 10 Lead Scarabs and 50 Prismatic Tapers) fits the 10,000 pyreals a new character holds.
    /// </summary>
    [Fact]
    public void The_outskirts_profile_is_an_outdoor_ground_the_starting_purse_stocks()
    {
        var ground = GroundCatalog.Get(OutskirtsKey);
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.Equal(GroundKind.Outdoor, ground.Kind);
        Assert.Equal(den.Home, ground.Home); // Holtburg's lifestone, as the den profile names it
        Assert.Null(ground.Portal);
        Assert.Equal((4, 8), (ground.LevelBand.Min, ground.LevelBand.Max));
        Assert.Equal((4, 8), AutopilotScenario.TargetBand(ground, 5, 5));
        Assert.Equal((4, 8), AutopilotScenario.TargetBand(ground, 9, 5));

        var outdoor = ground.Outdoor!;
        Assert.Equal(10, outdoor.Cells.Count);
        Assert.All(outdoor.Cells, c => Assert.True(ground.InLandblocks(c.Cell), $"cell 0x{c.Cell:X8} is outside the landblocks"));
        Assert.All(outdoor.Cells, c => Assert.Equal(GroundTerrain.Dry, c.Terrain));
        Assert.Equal(outdoor.Cells[0].Cell, ground.Arrival.Cell);
        Assert.Equal(1.0, outdoor.Creatures.Sum(c => c.SpawnMass), 3); // one roll of the one generator spawns exactly one of the eight
        var inBand = outdoor.Creatures.Where(c => c.Level >= ground.LevelBand.Min && c.Level <= ground.LevelBand.Max).ToArray();
        Assert.Equal(outdoor.ExpectedYieldPerSpot, inBand.Sum(c => c.SpawnMass), 4);
        Assert.All(outdoor.Creatures.Where(c => c.Level >= 5), c => Assert.Equal(8, c.Level)); // the drudges, mites, banderlings and mosswarts: the rung's 'L8'
        Assert.Contains(outdoor.Creatures, c => c.Name == "Drudge Skulker");

        // The supplies: every line is bought at Holtburg (none carried), at a price; the kit is empty (the chest dresses the mage); the purse is the starting stack.
        var supplies = ground.Supplies;
        Assert.Empty(supplies.Kit);
        Assert.Equal((30_000, 1), (supplies.Purse.Coin, supplies.Purse.Notes.Count)); // M6 8 I: the floor 30,000 as the Burrow and the Lodge read (it was 4,500); // M6 2 d: the purse is the floor the coin is kept at (one trip's spend; the climber starts with 10,000, starterGear.json) and the one note the trips buy toward
        Assert.Equal(10_000, supplies.Purse.MinNote); // M6 7 AU's 250,000, set to 10,000 by M6 8 I (round 1: 89,190 coin carried, no note could be bought)
        long restock = 0;
        long restockBurden = 0;
        foreach (var line in supplies.Stock)
        {
            Assert.True(ground.VendorSelling(line.Wcid) is not null, $"{line.Name} has no vendor in the profile");
            var price = ground.PriceOf(line.Wcid)!.Value;
            var inPack = line.Wcid switch { 691 => 10, 20631 => 50, _ => 0 }; // ACE creation: 5 Lead Scarabs and 25 Prismatic Tapers per trained magic school (War, Life)
            restock += (long)price * Math.Max(0, line.Keep - inPack);
            var unit = line.Wcid switch { 20631 => GearKit.PrismaticTaperBurden, 379 => 15, _ => GearKit.ComponentBurden }; // world SQL v0.9.296 PropertyInt 5: a component 4, the Prismatic Taper 6, the Mana Potion 15
            restockBurden += (long)unit * Math.Max(0, line.Keep - inPack);
        }

        output.WriteLine($"first restock at the keeps: {restock} pyreals of {supplies.Purse.Coin}");
        Assert.True(restock <= supplies.Purse.Coin, $"the first restock costs {restock}, the purse holds {supplies.Purse.Coin}");

        // M6 0 F8 (the first climb probe: 129% of the capacity after one restock): a Strength 10 mage (capacity 150 x 10 = 1,500, the burden line 90%) read 872 before the trip; the restock adds 104 + 15 = 119 and the Pathwarden's trinket 60 and robe 200
        // (quests/pathwarden.json, whose carry gate holds the same arithmetic) leave her at 1,251, 83% of the capacity.
        var capacity = EncumbranceSystem.EncumbranceCapacity(10, 0);
        Assert.Equal(1_500, capacity);
        Assert.Equal(1_114, restockBurden); // M6 14 (m6-life-through): 8 for Mandrake and Hemlock Talisman at keep 1 (Harm Other's components on the account formula); M6 14 T: 96 for two more of each of the twelve full-formula lines (keep 4: a visit leaves them clear of their lines); M6 8 (the owner, 2026-11-11): 735 for forty-nine more Mana Potions at 15; M6 7 AR: 24 for the scarabs and tapers of the III lines; M6 7 AK: 84 for the 21 new component lines at keep 1; M6 7 lead: 119 plus four more Iron Scarabs (the line keeps 6 since Flame Bolt II burns them: the rehearsal stopped at level 7 on the keep of 2); M6 7 X: 16 for the four protective lines at keep 1; M6 7 AD: 8 for Saffron and Quicksilver at keep 1
        var line90 = capacity * supplies.BurdenTripPercent / 100;
        Assert.True(872 + restockBurden + 60 + 200 <= line90, $"the climber after one restock, the trinket and the robe: {872 + restockBurden + 60 + 200} of the line {line90}");
    }

    /// <summary>True when (x, y) lies inside the polygon's footprint (even-odd rule over the cell-local vertices' x and y).</summary>
    /// <summary>M6 5 q6b: a portal can land a body exactly on a floor polygon's edge (the Cave of Alabree's arrival is on the edge x = 0 of its cell), where the strict crossing test says outside: a point within 1 cm of the polygon counts as under the footprint.</summary>
    private static bool FootprintHolds(IReadOnlyList<Vector3> vertices, float x, float y)
    {
        const float Edge = 0.01f;
        return FootprintHoldsStrict(vertices, x, y) || FootprintHoldsStrict(vertices, x - Edge, y) || FootprintHoldsStrict(vertices, x + Edge, y)
            || FootprintHoldsStrict(vertices, x, y - Edge) || FootprintHoldsStrict(vertices, x, y + Edge);
    }

    private static bool FootprintHoldsStrict(IReadOnlyList<Vector3> vertices, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            var a = vertices[i];
            var b = vertices[j];
            if ((a.Y > y) != (b.Y > y) && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
