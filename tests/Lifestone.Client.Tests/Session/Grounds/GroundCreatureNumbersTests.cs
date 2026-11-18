using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Lifestone.Client.Tests.Repository;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 16 Q12 (docs/automation-engine.md stage E1, the creature facts of the ground files become references): a ground profile names a creature by its weenie class and takes its health, Magic Defense, experience, drain
/// resistance, element resistances, awareness and pace from the bestiary's tables (the profile's <c>from_bestiary</c>) instead of stating them. The golden file <c>ground-creature-numbers.txt</c> holds every number each ground read
/// for its creatures when the profiles still stated them (the profile's own fields and the bestiary's record of each creature, one block a ground); a ground that moved a creature onto the bestiary must read exactly those numbers
/// again, and so must every ground that did not.
/// </summary>
public sealed class GroundCreatureNumbersTests
{
    private const string GoldenPath = "tests/Lifestone.Client.Tests/Session/Grounds/ground-creature-numbers.txt";

    /// <summary>The grounds, for the theory's rows.</summary>
    public static TheoryData<string> Keys() => [.. GroundCatalog.All.Select(g => g.Key)];

    /// <summary>Every creature number of a ground, as one block of text: the profile's fields of its targets, approach hostiles and outdoor creatures, then the bestiary's record of each creature the ground names.</summary>
    /// <param name="ground">The ground.</param>
    internal static string Snapshot(HuntingGround ground)
    {
        var sb = new StringBuilder();
        string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        sb.Append(CultureInfo.InvariantCulture, $"[{ground.Key}]\n");
        var wcids = new SortedSet<uint>();
        foreach (var t in ground.Targets)
        {
            wcids.Add(t.Wcid);
            sb.Append(CultureInfo.InvariantCulture, $"target {t.Wcid} {t.Name} level {t.Level} role {t.Role} health {t.MaxHealth} defence {t.MagicDefence} xp {t.Xp?.ToString(CultureInfo.InvariantCulture) ?? "none"} drain {N(t.ResistHealthDrain)}\n");
        }

        foreach (var h in ground.Approach?.Hostiles ?? [])
        {
            wcids.Add(h.Wcid);
            var resist = h.Resist is { } r ? string.Join(",", r.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}={N(e.Value)}")) : "none";
            sb.Append(CultureInfo.InvariantCulture, $"hostile {h.Wcid} {h.Name} health {h.MaxHealth?.ToString(CultureInfo.InvariantCulture) ?? "none"} defence {h.MagicDefence?.ToString(CultureInfo.InvariantCulture) ?? "none"} xp {h.Xp?.ToString(CultureInfo.InvariantCulture) ?? "none"} awareness {(h.AwarenessMetres is { } a ? N(a) : "none")} resist {resist} run {h.RunSkill?.ToString(CultureInfo.InvariantCulture) ?? "none"} motion {h.MotionTable?.ToString("X8", CultureInfo.InvariantCulture) ?? "none"} scale {(h.RunSkill is null ? "none" : N(h.Scale))} drain {N(h.ResistHealthDrain)}\n");
        }

        foreach (var c in ground.Outdoor?.Creatures ?? [])
        {
            wcids.Add(c.Wcid);
            sb.Append(CultureInfo.InvariantCulture, $"outdoor {c.Wcid} {c.Name} level {c.Level} health {c.MaxHealth} drain {N(c.ResistHealthDrain)}\n");
        }

        foreach (var wcid in wcids)
        {
            var k = Bestiary.Find(wcid);
            sb.Append(CultureInfo.InvariantCulture, $"kind {wcid} ");
            if (k is null)
            {
                sb.Append("not in the bestiary\n");
                continue;
            }

            sb.Append(CultureInfo.InvariantCulture, $"health {k.Vitals.MaxHealth?.ToString(CultureInfo.InvariantCulture) ?? "none"} from {k.Vitals.MaxHealthSource} defence {k.Defence.MagicDefence?.ToString(CultureInfo.InvariantCulture) ?? "none"} drain {N(k.Defence.HealthDrainResist)} xp {k.Reward.Xp?.ToString(CultureInfo.InvariantCulture) ?? "none"} awareness {N(k.Awareness.AwarenessMetres)} pinned {k.Awareness.Pinned} run {k.Movement.RunSkill?.ToString(CultureInfo.InvariantCulture) ?? "none"} scale {(k.Movement.Scale is { } s ? N(s) : "none")}\n");
        }

        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void A_ground_reads_the_creature_numbers_it_always_read(string key)
    {
        var golden = CheckoutFiles.ReadText(GoldenPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var marker = $"[{key}]\n";
        var start = golden.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{GoldenPath} holds no block for {key}");
        var end = golden.IndexOf("\n[", start + 1, StringComparison.Ordinal);
        var expected = end < 0 ? golden[start..] : golden[start..(end + 1)];

        Assert.Equal(expected, Snapshot(GroundCatalog.Get(key)));
    }

    [Fact]
    public void The_golden_file_names_every_ground_once()
    {
        var golden = CheckoutFiles.ReadText(GoldenPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = golden.Split('\n').Where(l => l.StartsWith('[')).ToArray();
        Assert.Equal(GroundCatalog.All.Select(g => $"[{g.Key}]").Order(StringComparer.Ordinal), blocks.Order(StringComparer.Ordinal));
    }

    private const string ArmoryFile = "tusker-armory.json";

    private static JsonObject ArmoryCopy() => (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText(ArmoryFile)!)!;

    private static string RefusedAt(JsonObject profile) => Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(ArmoryFile, profile.ToJsonString())).JsonPath;

    [Fact]
    public void A_creature_named_by_reference_reads_what_it_read_when_the_profile_stated_it()
    {
        // an Armory target and a Holding approach hostile (with a pace), each written out as the profile once stated it (the numbers as the golden file holds them), against the shipped profile: the ground reads the same
        var armory = ArmoryCopy();
        var target = ((JsonArray)armory["targets"]!).Cast<JsonObject>().First(t => (uint)t["wcid"]! == 11540);
        target.Remove("from_bestiary");
        target["max_health"] = 435;
        target["magic_defence"] = 275;
        target["xp"] = 80000;
        target["resist_health_drain"] = 0;
        Assert.Equal(Snapshot(GroundCatalog.Get("tusker-armory")), Snapshot(GroundCatalog.Parse(ArmoryFile, armory.ToJsonString())));

        const string holdingFile = "tusker-holding.json";
        var holding = (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText(holdingFile)!)!;
        var hostile = ((JsonArray)holding["approach"]!["hostiles"]!).Cast<JsonObject>().First(h => (uint)h["wcid"]! == 22053);
        hostile.Remove("from_bestiary");
        foreach (var field in new[] { "health", "magic_defence", "resist", "awareness_m", "xp", "pace", "resist_health_drain" })
        {
            hostile.Remove(field);
        }

        hostile["health"] = 760;
        hostile["magic_defence"] = 310;
        hostile["resist"] = JsonNode.Parse("""{"slash": 0.5, "pierce": 1.0, "bludgeon": 0.75, "fire": 1.0, "cold": 0.75, "acid": 0.75, "electric": 0.25}""");
        hostile["awareness_m"] = 25;
        hostile["xp"] = 125000;
        hostile["pace"] = JsonNode.Parse("""{"run_skill": 270, "motion_table": "0x0900000C", "scale": 1.5, "source": "written out for the test"}""");
        hostile["resist_health_drain"] = 0.75;
        Assert.Equal(Snapshot(GroundCatalog.Get("tusker-holding")), Snapshot(GroundCatalog.Parse(holdingFile, holding.ToJsonString())));
    }

    [Fact]
    public void A_fact_has_one_source_and_the_bestiary_must_hold_the_creature()
    {
        var both = ArmoryCopy();
        var stated = (JsonObject)both["targets"]![0]!;
        stated["max_health"] = 435;
        stated["from_bestiary"] = new JsonArray("max_health"); // max_health is also stated
        Assert.Equal("$.targets[0].from_bestiary[0]", RefusedAt(both));

        var unknown = ArmoryCopy();
        var target = (JsonObject)unknown["targets"]![0]!;
        target.Remove("xp");
        target["from_bestiary"] = new JsonArray("level");
        Assert.Equal("$.targets[0].from_bestiary[0]", RefusedAt(unknown));

        var twice = ArmoryCopy();
        target = (JsonObject)twice["targets"]![0]!;
        target.Remove("xp");
        target["from_bestiary"] = new JsonArray("xp", "xp");
        Assert.Equal("$.targets[0].from_bestiary[1]", RefusedAt(twice));

        var nobody = ArmoryCopy();
        target = (JsonObject)nobody["targets"]![0]!;
        target.Remove("xp");
        target["wcid"] = 2_000_000_000;
        target["from_bestiary"] = new JsonArray("xp");
        Assert.Equal("$.targets[0].from_bestiary", RefusedAt(nobody));
    }
}
