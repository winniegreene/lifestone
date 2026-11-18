using System.Text.Json;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Repository;
using Xunit;

namespace Lifestone.Client.Tests.Session.Creatures;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1; m6-plan.md D139): a creature's maximum health is ACE's, for a weenie that does not state it whole too (<see cref="CreatureTable.MaxHealthOf"/>). Before, 570 creature
/// weenies with no MaxHealth row or no Endurance read no maximum health and 53 with a start, ranks and Endurance of 0 read 0, where the server builds each with a real one: an empty vital for a missing row
/// (Entity/CreatureVital.cs:29-33 @95ef426d), an attribute never under 1 (Entity/CreatureAttribute.cs:141-155), the whole never under 1 (Entity/CreatureVital.cs:167-171). The numbers pinned are the server's own
/// objects' (<c>tools/fight-sim.ps1 creature</c>, 2026-11-17). No kind a ground profile or a quest file names changes. Nothing here needs the dats or a server.
/// </summary>
public sealed class CreatureMaxHealthTests
{
    private const string NoRowNoEndurance = "the weenie states no MaxHealth row: ACE builds an empty one; it states no Endurance, and ACE reads an attribute never under 1";
    private const string NoRow = "the weenie states no MaxHealth row: ACE builds an empty one, so the health is its Endurance's share alone";
    private const string NoEndurance = "its MaxHealth row stands; it states no Endurance, and ACE reads an attribute never under 1";
    private const string EnduranceZero = "its MaxHealth row stands; its Endurance is 0, and ACE reads an attribute never under 1";

    [Theory]
    [InlineData(32400u, 1, NoRowNoEndurance)] // Run Warden of Forgetfulness: "health 1  stamina 1  mana 1"
    [InlineData(42020u, 1, NoRowNoEndurance)] // Guardian of Mhoire: "health 1"
    [InlineData(31228u, 1, EnduranceZero)] // Runic Door Guardian (start 0, ranks 0, Endurance 0): "health 1"
    [InlineData(35932u, 1, NoEndurance)] // Amelia (start 0, ranks 0): "health 1"
    [InlineData(39009u, 201, NoEndurance)] // Blighted Disgusting Moarsman (start 200): "health 201"
    [InlineData(52289u, 100, NoRow)] // Sir Unell bin Rakke (Endurance 200): "health 100"
    [InlineData(87221u, 50, NoRow)] // Ruschk Ice Totem (Endurance 100): "health 50"
    public void A_weenie_that_does_not_state_its_health_whole_has_the_health_the_server_builds_it_with(uint wcid, int health, string note)
    {
        var row = CreatureTable.Find(wcid);
        Assert.NotNull(row);
        Assert.Equal(health, row.MaxHealth);
        Assert.Equal(note, row.MaxHealthNote);

        // through the facade, with the rule named in the source
        var kind = Bestiary.Find(wcid);
        Assert.NotNull(kind);
        Assert.Equal(health, kind.Vitals.MaxHealth);
        Assert.Equal("the world database's creature table; " + note, kind.Vitals.MaxHealthSource);
        Assert.Equal(health, CreatureFacts.All.MaxHealthOf(wcid, out var source));
        Assert.Equal(kind.Vitals.MaxHealthSource, source);
        Assert.Equal(health, KindNumbers.Of(wcid).MaxHealth);
    }

    [Fact]
    public void The_rule_is_ACE_s_an_empty_vital_an_attribute_never_under_1_and_a_health_never_under_1()
    {
        Assert.Equal(1, CreatureTable.MaxHealthOf(-1, -1, -1));
        Assert.Equal(1, CreatureTable.MaxHealthOf(0, 0, 0));
        Assert.Equal(1, CreatureTable.MaxHealthOf(0, 0, -1));
        Assert.Equal(1, CreatureTable.MaxHealthOf(0, 0, 1)); // round(1 / 2) is 1: half up, as ACE rounds (ACE.Common/Extensions/FloatExtensions.cs:7-15)
        Assert.Equal(201, CreatureTable.MaxHealthOf(200, 0, -1));
        Assert.Equal(201, CreatureTable.MaxHealthOf(200, 0, 0));
        Assert.Equal(100, CreatureTable.MaxHealthOf(-1, -1, 200));
        Assert.Equal(51, CreatureTable.MaxHealthOf(-1, -1, 101));

        // a weenie that states all three is as before: the profiles' own formula
        Assert.Equal(80 + 0 + 70, CreatureTable.MaxHealthOf(80, 0, 140));
        Assert.Equal(GroundDraft.MaxHealthOf(20, 3, 131), CreatureTable.MaxHealthOf(20, 3, 131));
        Assert.Null(CreatureTable.MaxHealthNoteOf(80, 0, 140));
        Assert.Null(CreatureTable.MaxHealthNoteOf(0, 0, 1));
        Assert.Equal(NoRowNoEndurance, CreatureTable.MaxHealthNoteOf(-1, -1, -1));
        Assert.Equal(NoRow, CreatureTable.MaxHealthNoteOf(-1, -1, 200));
        Assert.Equal(NoEndurance, CreatureTable.MaxHealthNoteOf(200, 0, -1));
        Assert.Equal(EnduranceZero, CreatureTable.MaxHealthNoteOf(0, 0, 0));
    }

    [Fact]
    public void Every_kind_of_the_table_has_a_maximum_health_and_the_623_that_changed_are_counted_by_rule()
    {
        Assert.All(CreatureTable.All, row => Assert.True(row.MaxHealth >= 1, $"weenie {row.Wcid} {row.Name} has a maximum health of {row.MaxHealth}"));
        Assert.All(Bestiary.All, kind => Assert.True(kind.Vitals.MaxHealth >= 1, $"weenie {kind.Wcid} has no maximum health in the bestiary"));

        // what stood before this slice, among the creature weenies of weenie.type 10: 570 with no maximum health (552 + 2 + 16) and 53 at 0
        var typed = CreatureTable.All.Where(row => row.MaxHealthNote is not null).GroupBy(row => row.MaxHealthNote!).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(552, typed[NoRowNoEndurance].Count); // props and unfinished weenies (bells, portals, levers, gift boxes, the Wardens of Forgetfulness): 468 of them state no level
        Assert.Equal(2, typed[NoRow].Count);
        Assert.Equal(16, typed[NoEndurance].Count);
        Assert.Equal(53, typed[EnduranceZero].Count); // the Wardens of Enlightenment and their like
        Assert.Equal(4, typed.Count);
        Assert.Equal([52289u, 87221u], typed[NoRow].Select(row => row.Wcid).Order());
        Assert.All(typed[NoRowNoEndurance], row => Assert.Equal(1, row.MaxHealth));
        Assert.All(typed[EnduranceZero], row => Assert.Equal(1, row.MaxHealth));
        Assert.Equal(15, typed.Values.SelectMany(rows => rows).Count(row => row.MaxHealth > 1)); // the two with an Endurance alone and thirteen of the sixteen with a start above 0
    }

    [Fact]
    public void No_creature_a_ground_profile_or_a_quest_file_names_changed()
    {
        // the ground profiles: every target, approach hostile and outdoor creature the table holds states its health whole, so its number is what it was
        var profiles = GroundCatalog.All.SelectMany(g => g.Targets.Select(t => t.Wcid).Concat((g.Approach?.Hostiles ?? []).Select(h => h.Wcid)).Concat((g.Outdoor?.Creatures ?? []).Select(c => c.Wcid))).Distinct().ToList();
        Assert.True(profiles.Count >= 100, $"the profiles name {profiles.Count} creature classes");
        Assert.All(profiles, wcid => Assert.Null(CreatureTable.Find(wcid)?.MaxHealthNote));

        // the quest files: every weenie class under a "wcid" member that is a creature of the table (a carrier, a kill's kind, an NPC)
        var files = CheckoutFiles.Under("src/Lifestone.Client/Session/Quests/quests/").Where(f => f.EndsWith(".json", StringComparison.Ordinal)).ToList();
        Assert.True(files.Count >= 20, $"{files.Count} quest files");
        var named = new HashSet<uint>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(CheckoutFiles.ReadText(file));
            Collect(doc.RootElement, null, named);
        }

        Assert.True(QuestCatalog.All.Count >= 20);
        var creatures = named.Where(wcid => CreatureTable.Find(wcid) is not null).ToList();
        Assert.True(creatures.Count >= 20, $"the quest files name {creatures.Count} creature classes");
        Assert.All(creatures, wcid => Assert.Null(CreatureTable.Find(wcid)!.MaxHealthNote));
    }

    private static void Collect(JsonElement element, string? member, HashSet<uint> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, property.Name, into);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, member, into);
                }

                break;
            case JsonValueKind.Number when member is not null && member.Contains("wcid", StringComparison.OrdinalIgnoreCase) && element.TryGetUInt32(out var wcid):
                into.Add(wcid);
                break;
        }
    }
}
