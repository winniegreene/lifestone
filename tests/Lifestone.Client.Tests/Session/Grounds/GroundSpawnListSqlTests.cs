using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Mage;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 7: every creature that can stand on a ground is on that ground's list, read from the pinned world SQL v0.9.296 (a read-only parse of the zip, as
/// <c>TravelRoutesSqlTests</c>; skipped without it). A spawner holds one creature at a time and makes the next only when that one dies, so a creature the fight never shoots
/// fills its spot for good: the outskirts lost its spots this way to the Gnawer Shreth 19263, a 5% roll of generator 2007 that the profile listed as the level 2 Shrethlet
/// 26686, and gate A's kills fell from 36 to 8 an hour. An outdoor ground is read at its own cells' encounter rows; a dungeon at the placed objects of its landblocks. A
/// generator's rows are followed down through generators; a row that puts its creature in a container, shop or treasure is no standing creature
/// (<see cref="GroundDraft.NotStandingLocations"/>, a chest's trap rows), and an object that cannot be attacked is no target (PropertyBool Attackable false).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class GroundSpawnListSqlTests(ITestOutputHelper output)
{
    private const long Attackable = 19;

    [RequiresPinnedWorldSqlFact]
    public void Every_creature_a_grounds_spawners_can_make_is_on_its_list()
    {
        var landblocks = GroundCatalog.All.SelectMany(g => g.Landblocks).ToHashSet();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["weenie"] = _ => true,
            ["encounter"] = r => landblocks.Contains((ushort)r.L("landblock")),
            ["landblock_instance"] = r => landblocks.Contains((ushort)(r.L("obj_Cell_Id") >> 16)),
            ["weenie_properties_generator"] = r => (r.L("where_Create") & GroundDraft.NotStandingLocations) == 0,
            ["weenie_properties_bool"] = r => r.L("type") == Attackable,
        });
        var creatures = sql["weenie"].View().Where(r => r.L("type") == GroundDraft.CreatureType).Select(r => (uint)r.L("class_Id")).ToHashSet();
        var unattackable = sql["weenie_properties_bool"].View().Where(r => r.S("value") == "\0").Select(r => (uint)r.L("object_Id")).ToHashSet(); // a false bit is the one NUL character
        var rows = sql["weenie_properties_generator"].View().ToLookup(r => (uint)r.L("object_Id"), r => (uint)r.L("weenie_Class_Id"));
        var encounters = sql["encounter"].View().ToLookup(r => ((ushort)r.L("landblock"), (int)r.L("cell_X"), (int)r.L("cell_Y")), r => (uint)r.L("weenie_Class_Id"));
        var placed = sql["landblock_instance"].View().ToLookup(r => (ushort)(r.L("obj_Cell_Id") >> 16), r => (uint)r.L("weenie_Class_Id"));

        void Follow(uint weenie, HashSet<uint> seen, HashSet<uint> found)
        {
            if (!seen.Add(weenie))
            {
                return;
            }

            if (creatures.Contains(weenie) && !unattackable.Contains(weenie))
            {
                found.Add(weenie);
            }

            foreach (var child in rows[weenie])
            {
                Follow(child, seen, found);
            }
        }

        var missing = new List<string>();
        foreach (var ground in GroundCatalog.All)
        {
            var roots = ground.Outdoor is { Cells.Count: > 0 } outdoor
                ? outdoor.Cells.SelectMany(c => encounters[EncounterOf(c.Cell)])
                : ground.Landblocks.SelectMany(b => placed[b]);
            var seen = new HashSet<uint>();
            var found = new HashSet<uint>();
            foreach (var root in roots)
            {
                Follow(root, seen, found);
            }

            var listed = ground.Targets.Select(t => t.Wcid).Concat(ground.Outdoor?.Creatures.Select(c => c.Wcid) ?? []).ToHashSet();
            output.WriteLine($"{ground.Key}: {found.Count} creatures can stand here, {listed.Count} listed");
            if (ground.Key == "holtburg-outskirts")
            {
                Assert.True(found.Contains(19263) && found.Contains(19259), "the check found the outskirts' own creatures");
                Assert.True(found.Count >= 9, $"the check found {found.Count} creatures on the outskirts: it reads nothing if it reads less than generator 2007's eight and the Shreth's young");
            }

            missing.AddRange(found.Where(w => !listed.Contains(w)).Select(w => $"{ground.Key}: {w}"));
        }

        Assert.True(missing.Count == 0, "on no list, so never shot, and each holds a spawner's one creature for good: " + string.Join(", ", missing));
    }

    /// <summary>The encounter row's key of an outdoor cell: the cell id is landblock &lt;&lt; 16 | (cell_X * 8 + cell_Y + 1) (holtburg-outskirts.json sources.outdoor).</summary>
    private static (ushort Landblock, int X, int Y) EncounterOf(uint cell)
    {
        var low = (int)(cell & 0xFFFF) - 1;
        return ((ushort)(cell >> 16), low / 8, low % 8);
    }
}
