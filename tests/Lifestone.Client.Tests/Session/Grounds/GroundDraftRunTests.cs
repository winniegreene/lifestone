using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The draft command (M5 item 2 g2, D46): drafts the hunting-ground profile of any dungeon landblock from the dats and the pinned world SQL (read-only, offline, no backend) and writes it under
/// <c>cache/grounds/</c> of the running checkout (gitignored; never under <c>src/</c>). A lead-run tool, not a check: <c>[Trait("Tier", "Tool")]</c> (a tier of its own since M5 item 2 k1; it was Timing, the lead's quiet-machine timing run, which then picked it up) keeps it out of the
/// default filters of slice-check, smoke and <c>dotnet test</c> (<c>Tier!=Tool</c>) and of slice-check's exhaustive one (<c>Tier=Exhaustive</c>) and of the timing run (<c>Tier=Timing</c>), and without <c>LIFESTONE_GROUND_DRAFT</c> it skips. The command, from the checkout:
/// <code>
/// $env:LIFESTONE_GROUND_DRAFT = '0x5C4A,,0.3'   # landblock, arrival cell (empty: the first entry portal's destination), doorway lean[, key[, home landblock]]
/// dotnet test tests/Lifestone.Client.Tests --disable-build-servers --filter "FullyQualifiedName~GroundDraftRunTests"
/// </code>
/// It writes <c>cache/grounds/&lt;key&gt;.json</c> (the draft profile; the key defaults to <c>draft-&lt;landblock&gt;</c>) and <c>&lt;key&gt;.txt</c> (the summary and the draft's TODOs). The draft has the dungeon half (the
/// dats' storeys, the generators and slots, the exits by portal object and by Death or Use emote) and the town half (M5 item 2 g8: the entry portal's instance, the lifestones nearest it, the vendors near the nearest one
/// with what they buy and sell and the prices at their own multipliers). The arrival is taken from the entry portals whose Destination lies in the landblock (several are listed, the first named, <c>TODO(ground-draft-arrival)</c>);
/// name it to override. The home is the lifestone nearest the entry portal; name a landblock to take that landblock's lifestone instead (a ground whose characters live in another town, as the den's Holtburg).
/// </summary>
public sealed class GroundDraftRunTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The command's switch: <c>landblock,arrival,lean[,key]</c>, hex or decimal.</summary>
    public const string Variable = "LIFESTONE_GROUND_DRAFT";

    [GroundDraftFact]
    [Trait("Tier", "Tool")]
    public void Draft_the_profile_of_the_named_dungeon_into_cache()
    {
        var (landblock, arrival, lean, key, home) = Parse(Environment.GetEnvironmentVariable(Variable)!);
        var all = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
        DraftWorld world;
        using (var zip = File.OpenRead(PinnedWorldSql.Zip()!))
        {
            world = GroundDraft.World(zip, landblock, arrival, home);
        }

        var arrivalCell = arrival ?? world.Arrival?.Cell ?? throw new InvalidOperationException($"no portal leads into landblock 0x{landblock:X4}: name the arrival cell ({Variable}=landblock,arrival,lean): {string.Join("; ", world.Notes)}");
        var cells = GroundDraft.Cells(all, arrivalCell, lean);
        var dir = Path.Combine(RepositoryFiles.Root, "cache", "grounds");
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar, dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(dir);
        var summary = GroundDraft.Summary(cells, world);
        File.WriteAllText(Path.Combine(dir, key + ".json"), GroundDraft.Json(key, all, cells, world));
        File.WriteAllText(Path.Combine(dir, key + ".txt"), summary);
        output.WriteLine(summary);
        output.WriteLine($"[measure] the draft of landblock 0x{landblock:X4} is cache/grounds/{key}.json (and .txt)");
        Assert.NotEmpty(cells.Storeys);
    }

    /// <summary>Reads <c>landblock,arrival,lean[,key[,home]]</c>; the arrival may be empty (the first entry portal's destination), the home is a landblock.</summary>
    internal static (ushort Landblock, uint? Arrival, float Lean, string Key, ushort? Home) Parse(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 5)
        {
            throw new ArgumentException($"{Variable} is '{text}': it takes landblock,arrival,lean[,key[,home landblock]] (for example 0x0105,0x01050375,0.3 or 0x5C4A,,0.3)");
        }

        static uint Number(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(s, CultureInfo.InvariantCulture);
        var landblock = Number(parts[0]);
        uint? arrival = parts[1].Length == 0 ? null : Number(parts[1]);
        if (landblock is 0 or > 0xFFFF || (arrival is { } named && named >> 16 != landblock))
        {
            throw new ArgumentException($"{Variable}: the arrival 0x{arrival:X8} is not a cell of landblock 0x{landblock:X4}");
        }

        var lean = float.Parse(parts[2], CultureInfo.InvariantCulture);
        var key = parts.Length >= 4 && parts[3].Length > 0 ? parts[3] : $"draft-{landblock:x4}";
        ushort? home = parts.Length == 5 ? checked((ushort)Number(parts[4])) : null;
        return ((ushort)landblock, arrival, lean, key, home);
    }
}

/// <summary>The pinned world SQL dump in the main checkout, which the test host opens (the client reads Streams only).</summary>
internal static class PinnedWorldSql
{
    /// <summary>The zip's path, or null when the main checkout has none.</summary>
    internal static string? Zip()
    {
        var path = Path.Combine([RepositoryFiles.MainCheckout, .. WorldSqlDump.ZipParts]);
        return File.Exists(path) ? path : null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ushort, uint?, ushort?), Lazy<DraftWorld>> Worlds = new();

    /// <summary>
    /// The draft's SQL part of a landblock, read once per (landblock, arrival, home) for the whole run: the per-ground theories of several classes share it (each read is two streaming passes over the 155 MB dump).
    /// </summary>
    /// <param name="landblock">The dungeon's landblock.</param>
    /// <param name="arrival">The arrival cell, or null for the first entry portal's destination.</param>
    /// <param name="home">The home lifestone's landblock, or null for the lifestone nearest the entry portal.</param>
    internal static DraftWorld Draft(ushort landblock, uint? arrival, ushort? home = null) =>
        Worlds.GetOrAdd((landblock, arrival, home), k => new Lazy<DraftWorld>(() =>
        {
            using var zip = File.OpenRead(Zip()!);
            return GroundDraft.World(zip, k.Item1, k.Item2, k.Item3);
        })).Value;
}

/// <summary>The draft command's switch: skipped, with the reason, without <see cref="GroundDraftRunTests.Variable"/>, the dats or the pinned dump of the main checkout.</summary>
internal sealed class GroundDraftFactAttribute : FactAttribute
{
    public GroundDraftFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GroundDraftRunTests.Variable)))
        {
            Skip = $"the draft command: set {GroundDraftRunTests.Variable}=landblock,arrival,lean[,key] (a lead-run tool, Tier=Timing)";
        }
        else if (!Lifestone.Data.Dat.DatSet.Exists())
        {
            Skip = "the draft command needs the retail dats";
        }
        else if (PinnedWorldSql.Zip() is null)
        {
            Skip = "the draft command needs the pinned world SQL dump under server/runtime/downloads of the main checkout";
        }
    }
}

/// <summary>
/// <see cref="GroundDraft"/>'s SQL part on a synthetic mysqldump excerpt (no dump, no dats): a linkable generator's slots are its link children with its first profile row's delay and its own
/// tick; a generator without links makes its profile rows' creatures at the row's cell; a portal whose destination is outside the landblock is an exit; the creatures carry their names and
/// levels; an object that is not a creature (WeenieType 10) is no slot and a generator of none is left out, named; the arrival's destination is the entrance's; a generator without RegenerationInterval is a named TODO. The command's switch parses.
/// </summary>
public sealed class GroundDraftWorldTests
{
    private const string Excerpt = """
        CREATE TABLE `weenie` (
          `class_Id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `class_Name` varchar(100) NOT NULL,
          `type` int(5) NOT NULL DEFAULT '0',
          `last_Modified` datetime NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie` VALUES (7,'cavebeast',10,'2020-01-01 00:00:00'),(8,'pup',10,'2020-01-01 00:00:00'),(9,'chest',20,'2020-01-01 00:00:00'),(50,'portal',7,'2020-01-01 00:00:00'),(900,'gen',1,'2020-01-01 00:00:00'),(901,'gen',1,'2020-01-01 00:00:00'),(902,'gen',1,'2020-01-01 00:00:00');
        CREATE TABLE `landblock_instance` (
          `guid` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `landblock` int(5) GENERATED ALWAYS AS ((`obj_Cell_Id` >> 16)) VIRTUAL,
          `weenie_Class_Id` int(10) unsigned NOT NULL,
          `obj_Cell_Id` int(10) unsigned NOT NULL,
          `origin_X` float NOT NULL,
          `origin_Y` float NOT NULL,
          `origin_Z` float NOT NULL,
          `angles_W` float NOT NULL,
          `angles_X` float NOT NULL,
          `angles_Y` float NOT NULL,
          `angles_Z` float NOT NULL,
          `is_Link_Child` bit(1) NOT NULL,
          `last_Modified` datetime NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`) VALUES (1879113729,900,17105153,1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(1879113730,7,17105154,1,1,0,1,0,0,0,b'1','2020-01-01 00:00:00'),(1879113731,7,17105155,1,1,0,1,0,0,0,b'1','2020-01-01 00:00:00'),(1879113732,901,17105156,1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(1879113733,50,17105157,1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(1879113734,7,17170689,1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(1879113735,902,17105159,1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00');
        CREATE TABLE `landblock_instance_link` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `parent_GUID` int(10) unsigned NOT NULL,
          `child_GUID` int(10) unsigned NOT NULL,
          `last_Modified` datetime NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `landblock_instance_link` VALUES (1,1879113729,1879113730,'2020-01-01 00:00:00'),(2,1879113729,1879113731,'2020-01-01 00:00:00');
        CREATE TABLE `weenie_properties_generator` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `object_Id` int(10) unsigned NOT NULL,
          `probability` float NOT NULL DEFAULT '1',
          `weenie_Class_Id` int(10) unsigned NOT NULL,
          `delay` float DEFAULT NULL,
          `init_Create` int(10) NOT NULL DEFAULT '1',
          `max_Create` int(10) NOT NULL DEFAULT '1',
          `when_Create` int(10) unsigned NOT NULL DEFAULT '2',
          `where_Create` int(10) unsigned NOT NULL DEFAULT '4',
          `stack_Size` int(10) DEFAULT NULL,
          `palette_Id` int(10) unsigned DEFAULT NULL,
          `shade` float DEFAULT NULL,
          `obj_Cell_Id` int(10) unsigned DEFAULT NULL,
          `origin_X` float DEFAULT NULL,
          `origin_Y` float DEFAULT NULL,
          `origin_Z` float DEFAULT NULL,
          `angles_W` float DEFAULT NULL,
          `angles_X` float DEFAULT NULL,
          `angles_Y` float DEFAULT NULL,
          `angles_Z` float DEFAULT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_generator` VALUES (1,900,1,0,300,1,1,2,4,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL),(2,901,1,8,120,1,2,2,4,NULL,NULL,NULL,17105158,NULL,NULL,NULL,NULL,NULL,NULL,NULL),(3,902,1,9,0,1,1,2,4,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
        CREATE TABLE `weenie_properties_float` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` double NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_float` VALUES (1,900,41,30),(2,901,37,1);
        CREATE TABLE `weenie_properties_int` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` int(10) NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_int` VALUES (1,7,25,60),(2,8,25,12);
        CREATE TABLE `weenie_properties_string` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` text NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_string` VALUES (1,7,1,'Cave Beast'),(2,8,1,'Pup'),(3,50,1,'Way Out');
        CREATE TABLE `weenie_properties_position` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `object_Id` int(10) unsigned NOT NULL,
          `position_Type` smallint(5) unsigned NOT NULL,
          `obj_Cell_Id` int(10) unsigned NOT NULL,
          `origin_X` float NOT NULL,
          `origin_Y` float NOT NULL,
          `origin_Z` float NOT NULL,
          `angles_W` float NOT NULL,
          `angles_X` float NOT NULL,
          `angles_Y` float NOT NULL,
          `angles_Z` float NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_position` VALUES (1,50,2,2847146009,84,7.7,94,1,0,0,0),(2,49,2,17105153,10,-20,0,0.707107,0,0,-0.707107);
        """;

    private static DraftWorld World()
    {
        using var reader = new StringReader(Excerpt);
        return GroundDraft.World(WorldSqlDump.Read(reader, GroundDraft.WorldTables(0x0105)), 0x0105, 0x01050101);
    }

    [Fact]
    public void A_linkable_generator_s_slots_are_its_links_with_its_first_row_s_delay_and_its_own_tick()
    {
        var world = World();
        Assert.Equal(
            [new DraftGenerator(0x70010001, 900, 0x01050101, 300, 30), new DraftGenerator(0x70010004, 901, 0x01050104, 120, null)],
            world.Generators);
        Assert.Equal(
            [
                new DraftSlot(0x70010001, 0x70010002, 0x01050102, 7),
                new DraftSlot(0x70010001, 0x70010003, 0x01050103, 7),
                new DraftSlot(0x70010004, 0, 0x01050106, 8),
                new DraftSlot(0x70010004, 0, 0x01050106, 8),
            ],
            world.Slots);
        Assert.DoesNotContain(world.Slots, s => s.Cell >> 16 != 0x0105); // the instance of landblock 0x0106 is not read
    }

    [Fact]
    public void The_creatures_carry_names_and_levels_the_exit_is_named_and_the_arrival_is_the_entrance_s_destination()
    {
        var world = World();
        Assert.Equal([new DraftCreature(7, "Cave Beast", 60), new DraftCreature(8, "Pup", 12)], world.Creatures);
        Assert.Equal([new DraftPortal(0x70010005, 50, 0x01050105, 0xA9B40019)], world.Exits);
        Assert.Equal([0x01050105u], world.ExitCells);
        Assert.NotNull(world.Arrival);
        Assert.Equal((49u, 10f, -20f), (world.Arrival.Wcid, world.Arrival.Origin.X, world.Arrival.Origin.Y));
        Assert.Contains(world.Notes, n => n.StartsWith("TODO(ground-draft-tick): generator 0x70010004", StringComparison.Ordinal));
        Assert.Contains(world.Notes, n => n.StartsWith("generator 0x70010007 (wcid 902) in 0x01050107 makes no creature", StringComparison.Ordinal)); // a chest's generator is no hunting slot
    }

    [Fact]
    public void The_command_s_switch_parses_and_refuses_an_arrival_outside_the_landblock()
    {
        Assert.Equal(((ushort)0x0105, (uint?)0x01050375u, 0.3f, "draft-0105", (ushort?)null), GroundDraftRunTests.Parse("0x0105, 0x01050375, 0.3"));
        Assert.Equal("tusker-temple", GroundDraftRunTests.Parse("0x5C4A,0x5C4A0100,0.3,tusker-temple").Key);
        Assert.Equal(((ushort)0x5C4A, (uint?)null, 0.3f, "draft-5c4a", (ushort?)null), GroundDraftRunTests.Parse("0x5C4A,,0.3")); // no arrival: the first entry portal's
        Assert.Equal((ushort?)0xA9B4, GroundDraftRunTests.Parse("0x0105,,0.3,den,0xA9B4").Home);
        Assert.Throws<ArgumentException>(() => GroundDraftRunTests.Parse("0x0105,0x01060375,0.3"));
        Assert.Throws<ArgumentException>(() => GroundDraftRunTests.Parse("0x0105"));
        Assert.Throws<ArgumentException>(() => GroundDraftRunTests.Parse("0x0105,,0.3,k,0xA9B4,extra"));
    }
}
