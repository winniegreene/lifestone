using System.Numerics;
using System.Text.Json.Nodes;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The profile's new fields (M6 item 3 a1, <c>m6-ground-fields</c>): the optional <c>entry</c> section (the entry portal's place, its MinLevel and MaxLevel gate, whether it can be tied), the optional <c>exit</c> section (portal,
/// recall or walk), the <c>kill-first</c> role (a target the pick orders first) and the target stats (health, Magic Defense, attributes, damage types, swing). Each field is read, each refusal names its JSON path, a profile
/// without the new sections loads as before, and the lookups allocate nothing. The values are the Temple 40+'s, drafted from the pinned world SQL (GroundDraftTownTests checks them against it).
/// </summary>
public sealed class GroundFieldsTests
{
    private const string TempleKey = "tusker-temple-fort-tethana";
    private const string TempleFile = TempleKey + ".json";

    private static JsonObject TempleCopy() => (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText(TempleFile)!)!;

    private static HuntingGround Parse(JsonObject profile) => GroundCatalog.Parse(TempleFile, profile.ToJsonString());

    private static string RefusedAt(JsonObject profile) => Assert.Throws<GroundProfileException>(() => Parse(profile)).JsonPath;

    private static JsonObject Entry(JsonObject profile) => (JsonObject)profile["entry"]!;

    private static JsonObject Target(JsonObject profile, int index) => (JsonObject)profile["targets"]![index]!;

    [Fact]
    public void The_temple_s_entry_exit_and_target_stats_load_as_drafted()
    {
        var temple = GroundCatalog.Get(TempleKey);
        var entry = Assert.IsType<GroundEntry>(temple.Entry);
        Assert.Equal((22696u, 0x72A80000u, 0x2A800013u, 0x2A800013u), (entry.Wcid, entry.Guid, entry.Cell, entry.FrontCell));
        Assert.Equal(new Vector3(57.7292f, 64.9681f, 70.005f), entry.Origin);
        Assert.Equal(new Vector3(57.8509f, 67.9656f, 70.01f), entry.Front);
        Assert.Equal(((int?)40, (int?)null, true), (entry.MinLevel, entry.MaxLevel, entry.Tieable));
        Assert.Equal((temple.Portal!.Wcid, temple.Portal.Guid, temple.Portal.Front), (entry.Wcid, entry.Guid, entry.Front)); // the tie record and the entry name one portal

        var exit = Assert.IsType<GroundExitSection>(temple.Exit);
        Assert.Equal(new GroundExitSection(GroundExitKind.Recall, 0, 0, null, 0, null), exit); // no exit portal in 0x5C4A: the statue's Use emote is the only other way out, and it is avoided

        var slave = temple.Targets.Single(t => t.Wcid == 1628);
        Assert.Equal((400, 219, 0x4u, (double?)null, false), (slave.MaxHealth, slave.MagicDefence, slave.DamageTypes, slave.SwingSeconds, slave.KillFirst));
        Assert.Equal(new GroundAttributes(210, 300, 200, 180, 70, 60), slave.Attributes);
        var statue = temple.Targets.Single(t => t.Wcid == 22641);
        Assert.Equal((GroundRole.Avoid, 370, 0, 0x48u), (statue.Role, statue.MaxHealth, statue.MagicDefence, statue.DamageTypes)); // no Magic Defense row: none written, never a default
        Assert.All(temple.Targets, t => Assert.NotNull(t.Attributes));
        Assert.All(temple.Targets, t => Assert.Null(t.SwingSeconds)); // TODO(f11-swing-seconds): not drafted
    }

    [Fact]
    public void A_profile_without_the_new_sections_loads_unchanged()
    {
        var bare = TempleCopy();
        bare.Remove("entry");
        bare.Remove("exit");
        foreach (var t in ((JsonArray)bare["targets"]!).Cast<JsonObject>())
        {
            foreach (var field in new[] { "max_health", "magic_defence", "from_bestiary", "attributes", "damage_types" })
            {
                t.Remove(field);
            }
        }

        var without = Parse(bare);
        var with = GroundCatalog.Get(TempleKey);
        Assert.Null(without.Entry);
        Assert.Null(without.Exit);
        Assert.True(without.AdmitsLevel(1));
        Assert.All(without.Targets, t => Assert.Equal((0, 0, 0u, (GroundAttributes?)null, (double?)null, false), (t.MaxHealth, t.MagicDefence, t.DamageTypes, t.Attributes, t.SwingSeconds, t.KillFirst)));
        Assert.Equal(with.Targets.Select(t => (t.Wcid, t.Name, t.Level, t.Role)), without.Targets.Select(t => (t.Wcid, t.Name, t.Level, t.Role)));
        Assert.Equal(with.Portal, without.Portal);
        Assert.Equal(with.Walkable.Cells, without.Walkable.Cells);

        // Every other embedded profile has neither section, and the den's two measures are still its own.
        var spine = new[] { "tusker-burrow", "tusker-lodge", "tusker-cave", "tusker-temple-20-40", "tusker-cavern", "tusker-habitat", "tusker-barracks", "tusker-abode", "tusker-quarters", "tusker-holding", "tusker-armory", "cave-of-alabree", "crater-caves", "black-spawn-den-sho", "holtburg-redoubt", "tusker-retreat" }; // M6 redoubt: the Holtburg Redoubt has an entry and an exit; M6 item 14: the Sho den carries both sections for the den run (its entrance 7246 and its Surface Portal 7247); M6 5 q6b: the Cave of Alabree has an entry and an exit too; M6 3 p1, p2 and p3: the spine and alternate tusker profiles carry both sections
        Assert.All(GroundCatalog.All.Where(g => g.Key != TempleKey && !spine.Contains(g.Key)), g => Assert.Equal((GroundEntry?)null, g.Entry));
        Assert.All(GroundCatalog.All.Where(g => g.Key != TempleKey && !spine.Contains(g.Key)), g => Assert.Equal((GroundExitSection?)null, g.Exit));
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.Equal((400, 219), (den.Targets.Single(t => t.Wcid == 1628).MaxHealth, den.Targets.Single(t => t.Wcid == 1628).MagicDefence));
    }

    [Fact]
    public void AdmitsLevel_reads_the_entry_s_min_and_max_level_and_admits_every_level_without_an_entry()
    {
        var temple = GroundCatalog.Get(TempleKey);
        Assert.False(temple.AdmitsLevel(39));
        Assert.True(temple.AdmitsLevel(40));
        Assert.True(temple.AdmitsLevel(275)); // no MaxLevel on 22696

        var band = TempleCopy();
        Entry(band)["min_level"] = 20;
        Entry(band)["max_level"] = 40; // the shape of the Temple 20-40's gate
        var twenty = Parse(band);
        Assert.Equal(new[] { false, true, true, false }, new[] { 19, 20, 40, 41 }.Select(twenty.AdmitsLevel));

        var open = TempleCopy();
        Entry(open)["min_level"] = null;
        Assert.True(Parse(open).AdmitsLevel(1));
        Assert.True(GroundCatalog.Get("black-spawn-den-aluvian").AdmitsLevel(1)); // no entry section
    }

    [Fact]
    public void An_untieable_entry_loads_without_a_tie_record_and_is_refused_beside_one()
    {
        var noTie = TempleCopy();
        Entry(noTie)["tieable"] = false;
        Assert.Equal("$.portal", RefusedAt(noTie)); // a NoRecall portal (ACE's NoTie) carries no tie record

        noTie["portal"] = null;
        var ground = Parse(noTie);
        Assert.Null(ground.Portal);
        Assert.False(ground.Entry!.Tieable);
    }

    [Theory]
    [InlineData("min_level", "41", "$.entry.min_level")] // above max_level 40 (set below)
    [InlineData("tieable", "\"yes\"", "$.entry.tieable")]
    [InlineData("guid", "\"0x72A80001\"", "$.entry.guid")] // not the tie record's portal
    [InlineData("wcid", "22697", "$.entry.guid")]
    [InlineData("cell", "\"2A800013\"", "$.entry.cell")]
    [InlineData("max_level", "\"none\"", "$.entry.max_level")]
    [InlineData("unknown", "1", "$.entry.unknown")]
    public void A_bad_entry_field_is_refused_by_its_json_path(string field, string value, string path)
    {
        var bad = TempleCopy();
        Entry(bad)["max_level"] = 40;
        Entry(bad)[field] = JsonNode.Parse(value);
        Assert.Equal(path, RefusedAt(bad));
    }

    [Theory]
    [InlineData("tieable")]
    [InlineData("source")]
    [InlineData("front_point")]
    [InlineData("min_level")]
    public void A_missing_entry_field_is_refused_by_its_json_path(string field)
    {
        var bad = TempleCopy();
        Entry(bad).Remove(field);
        Assert.Equal("$.entry." + field, RefusedAt(bad));
    }

    [Fact]
    public void An_exit_portal_loads_its_place_and_the_hint_and_each_kind_s_fields_are_checked()
    {
        var portal = TempleCopy();
        portal["exit"] = JsonNode.Parse("""{ "kind": "portal", "wcid": 22664, "cell": "0x5C4A0100", "origin": { "x": 119.843, "y": -134.228, "z": -29.995 }, "destination": "0xF9820039", "from_arrival_m": 137.8, "source": "a test" }""");
        Assert.Equal(new GroundExitSection(GroundExitKind.Portal, 22664, 0x5C4A0100, new Vector3(119.843f, -134.228f, -29.995f), 0xF9820039, 137.8), Parse(portal).Exit);

        ((JsonObject)portal["exit"]!).Remove("from_arrival_m");
        Assert.Null(Parse(portal).Exit!.FromArrivalMetres); // the hint is optional

        ((JsonObject)portal["exit"]!)["cell"] = "0x57480100";
        Assert.Equal("$.exit.cell", RefusedAt(portal)); // the exit portal stands in the ground

        ((JsonObject)portal["exit"]!)["cell"] = "0x5C4A0100";
        ((JsonObject)portal["exit"]!).Remove("destination");
        Assert.Equal("$.exit.destination", RefusedAt(portal));

        var recallWithPlace = TempleCopy();
        recallWithPlace["exit"] = JsonNode.Parse("""{ "kind": "recall", "wcid": 22664, "source": "a test" }""");
        Assert.Equal("$.exit.wcid", RefusedAt(recallWithPlace)); // only a portal exit has a place

        var walk = TempleCopy();
        walk["exit"] = JsonNode.Parse("""{ "kind": "walk", "source": "a test" }""");
        Assert.Equal("$.exit.kind", RefusedAt(walk)); // a dungeon is not left on foot

        var fly = TempleCopy();
        fly["exit"] = JsonNode.Parse("""{ "kind": "fly", "source": "a test" }""");
        Assert.Equal("$.exit.kind", RefusedAt(fly));

        var noSource = TempleCopy();
        noSource["exit"] = JsonNode.Parse("""{ "kind": "recall" }""");
        Assert.Equal("$.exit.source", RefusedAt(noSource));
    }

    [Theory]
    [InlineData("damage_types", "\"0x0\"", "$.targets[0].damage_types")] // a mask of 0 says nothing: leave the field out
    [InlineData("damage_types", "4", "$.targets[0].damage_types")] // a mask is a hex string
    [InlineData("swing_s", "0", "$.targets[0].swing_s")]
    [InlineData("max_health", "0", "$.targets[0].max_health")]
    [InlineData("attributes", "{ \"strength\": 1, \"endurance\": 1, \"coordination\": 1, \"quickness\": 1, \"focus\": 1 }", "$.targets[0].attributes.self")]
    [InlineData("attributes", "{ \"strength\": 1, \"endurance\": 1, \"coordination\": 1, \"quickness\": 1, \"focus\": 1, \"self\": 1, \"luck\": 1 }", "$.targets[0].attributes.luck")]
    [InlineData("role", "\"first\"", "$.targets[0].role")]
    public void A_bad_target_stat_is_refused_by_its_json_path(string field, string value, string path)
    {
        var bad = TempleCopy();
        Target(bad, 0).Remove("from_bestiary"); // the Temple takes its targets' health and Magic Defense from the bestiary (M6 16 Q12); a stat written here is stated, and read as strictly as ever
        Target(bad, 0)[field] = JsonNode.Parse(value);
        Assert.Equal(path, RefusedAt(bad));
    }

    [Fact]
    public void A_target_s_swing_seconds_load_when_given()
    {
        var swing = TempleCopy();
        Target(swing, 0)["swing_s"] = 1.75;
        Assert.Equal(1.75, Parse(swing).Targets[0].SwingSeconds);
    }

    [Fact]
    public void Kill_first_loads_as_a_target_that_IsKillFirst_names_and_an_avoided_creature_never_is()
    {
        var first = TempleCopy();
        Target(first, 3)["role"] = "kill-first"; // the casting Worshipper 22516
        Target(first, 3)["opener"] = "Fire Vulnerability Other"; // still a target: it may name an opener
        var ground = Parse(first);
        var worshipper = ground.Targets[3];
        Assert.Equal((22516u, GroundRole.Target, true), (worshipper.Wcid, worshipper.Role, worshipper.KillFirst));
        Assert.Equal(GroundRole.Target, ground.RoleOf(22516)); // every Role == Target check holds
        Assert.Equal("Fire Vulnerability Other", ground.OpenerOf(22516));
        Assert.True(ground.IsKillFirst(22516));
        Assert.False(ground.IsKillFirst(1628)); // a plain target
        Assert.False(ground.IsKillFirst(22641)); // avoided
        Assert.False(ground.IsKillFirst(999_999)); // not listed

        // Only a target is kill-first: the JSON role is one value, so an avoided creature cannot carry it, and a ground built by hand that sets it on an avoided creature is not read as kill-first.
        var avoided = GroundCatalog.Get(TempleKey) with { Targets = [new GroundTarget(22641, "Tusker Shrine Statue", 171, GroundRole.Avoid) { KillFirst = true }] };
        Assert.False(avoided.IsKillFirst(22641));
        Assert.Contains("target, kill-first or avoid", Assert.Throws<GroundProfileException>(() =>
        {
            var bad = TempleCopy();
            Target(bad, 4)["role"] = "avoid-kill-first";
            Parse(bad);
        }).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsKillFirst_and_AdmitsLevel_allocate_nothing()
    {
        var first = TempleCopy();
        Target(first, 3)["role"] = "kill-first";
        var ground = Parse(first);
        var hits = 0;
        hits += ground.IsKillFirst(22516) ? 1 : 0; // warm
        hits += ground.AdmitsLevel(40) ? 1 : 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            hits += ground.IsKillFirst(22516) ? 1 : 0;
            hits += ground.IsKillFirst(1628) ? 1 : 0;
            hits += ground.AdmitsLevel(i % 100) ? 1 : 0;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(2 + 1000 + 600, hits); // levels 40 to 99 of each hundred are admitted
    }
}
