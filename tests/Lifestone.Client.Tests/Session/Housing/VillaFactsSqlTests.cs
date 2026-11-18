using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Housing;

/// <summary>
/// M6 villa (D28): every fact the villa's steps walk to or check, against the pinned world SQL v0.9.296: each placed thing's weenie, cell and point (landblock_instance), the basements' arrivals (house_portal), the chests as the
/// basement house's linked children, the guardian's price and rent rows and MinLevel, the chest's capacities, the lockpick's shop row and price inputs, the stones' Quest strings and their one-hour rows, and the Agent of the
/// Arcanum's two lines (the QuestSuccess and QuestFailure sets of <c>MinorStoneWrit@n</c>, with the writ's Give).
/// </summary>
public sealed class VillaFactsSqlTests
{
    private const long NameString = 1, QuestString = 33, MinLevel = 86, ValueInt = 19, ItemsCapacity = 6, ContainersCapacity = 7, SellPrice = 38;
    private const long EmoteGive = 3, EmoteTell = 10, EmoteUpdateQuest = 20, CategoryGive = 6, CategoryQuestSuccess = 12, CategoryQuestFailure = 13;

    [RequiresPinnedWorldSqlFact]
    public void Every_villa_fact_is_what_the_pinned_world_SQL_holds()
    {
        var things = VillaFacts.Block1401.Houses.SelectMany(h => new[] { h.House, h.Guardian, h.HousePortal }.Concat(h.Chests))
            .Append(LockpickFacts.Merchant).Append(WritFacts.HoltburgAgent)
            .Concat(WritFacts.Caves.Select(c => new PlacedThing(c.StoneGuid, c.StoneWcid, c.StoneName, c.StoneCell, c.StonePoint)))
            .ToList();
        var guids = things.Select(t => t.Guid).ToHashSet();
        var basements = VillaFacts.Block1401.Houses.Select(h => (long)h.House.Guid).ToHashSet();
        var houseIds = VillaFacts.Block1401.Houses.Select(h => (long)h.HouseId).ToHashSet();
        var stoneWcids = WritFacts.StoneWcids.Select(w => (long)w).ToHashSet();
        var agentSets = new HashSet<long>();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var tables = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => guids.Contains((uint)r.L("guid")) || (r.L("weenie_Class_Id") == 13049 || r.L("weenie_Class_Id") == 13050),
            ["landblock_instance_link"] = r => ((r.L("parent_GUID") >> 12) & 0xFFFF) is 0x9DAF or 0x038B or 0x038C,
            ["house_portal"] = r => houseIds.Contains(r.L("house_Id")),
            ["weenie_properties_create_list"] = r => r.L("object_Id") is VillaFacts.VillaGuardianWcid or 1387,
            ["weenie_properties_int"] = r => (r.L("object_Id") == VillaFacts.VillaGuardianWcid && r.L("type") == MinLevel) || (r.L("object_Id") == VillaFacts.StorageWcid && r.L("type") is ItemsCapacity or ContainersCapacity) || (r.L("object_Id") == VillaFacts.LockpickWcid && r.L("type") == ValueInt),
            ["weenie_properties_float"] = r => r.L("object_Id") == 1387 && r.L("type") == SellPrice,
            ["weenie_properties_string"] = r => (r.L("type") == NameString && (stoneWcids.Contains(r.L("object_Id")) || r.L("object_Id") is WritFacts.AgentWcid or 1387 or VillaFacts.LockpickWcid)) || (r.L("type") == QuestString && stoneWcids.Contains(r.L("object_Id"))),
            ["quest"] = _ => true,
            ["weenie_properties_emote"] = r => r.L("object_Id") == WritFacts.AgentWcid && ((r.L("category") == CategoryGive && stoneWcids.Contains(r.L("weenie_Class_Id"))) || (r.L("category") is CategoryQuestSuccess or CategoryQuestFailure && (r.S("quest") ?? string.Empty).StartsWith("MinorStoneWrit", StringComparison.Ordinal))) && agentSets.Add(r.L("id")),
            ["weenie_properties_emote_action"] = r => agentSets.Contains(r.L("emote_Id")),
        });
        var rows = tables["landblock_instance"].View().ToDictionary(r => (uint)r.L("guid"));
        foreach (var t in things)
        {
            var row = rows[t.Guid];
            Assert.True((uint)row.L("weenie_Class_Id") == t.Wcid && (uint)row.L("obj_Cell_Id") == t.Cell, $"0x{t.Guid:X8}: weenie {row.L("weenie_Class_Id")} cell 0x{row.L("obj_Cell_Id"):X8}");
            Assert.True(Vector3.Distance(new Vector3((float)row.F("origin_X"), (float)row.F("origin_Y"), (float)row.F("origin_Z")), t.Point) < 0.001f, $"0x{t.Guid:X8}: the point");
        }

        var links = tables["landblock_instance_link"].View().ToLookup(r => (uint)r.L("parent_GUID"), r => (uint)r.L("child_GUID"));
        var portals = tables["house_portal"].View().ToLookup(r => r.L("house_Id"));
        foreach (var h in VillaFacts.Block1401.Houses)
        {
            Assert.Contains(h.Guardian.Guid, links[h.House.Guid]);
            Assert.Contains(h.HousePortal.Guid, links[h.House.Guid]);
            Assert.Contains(portals[h.HouseId], r => (uint)r.L("obj_Cell_Id") == h.BasementArrival.Cell && Math.Abs(r.F("origin_X") - h.BasementArrival.Point.X) < 0.001 && Math.Abs(r.F("origin_Y") - h.BasementArrival.Point.Y) < 0.001);
            var basementHouse = tables["landblock_instance"].View().Single(r => (uint)r.L("obj_Cell_Id") >> 16 == h.BasementArrival.Cell >> 16 && r.L("weenie_Class_Id") == h.House.Wcid);
            Assert.Equal(h.Chests.Select(c => c.Guid).Order(), links[(uint)basementHouse.L("guid")].Where(g => h.Chests.Any(c => c.Guid == g)).Order());
        }

        var creates = tables["weenie_properties_create_list"].View().ToLookup(r => r.L("object_Id"));
        long Row(long dest, long wcid) => creates[VillaFacts.VillaGuardianWcid].Single(r => r.L("destination_Type") == dest && r.L("weenie_Class_Id") == wcid).L("stack_Size");
        var price = VillaFacts.Block1401.Price;
        Assert.Equal((price.Pyreals, (long)price.Writs, (long)price.Lockpicks), (Row(16, VillaFacts.PyrealWcid), Row(16, VillaFacts.WritWcid), Row(16, VillaFacts.LockpickWcid)));
        Assert.Equal((VillaFacts.RentPyreals, (long)VillaFacts.RentWrits), (Row(32, VillaFacts.PyrealWcid), Row(32, VillaFacts.WritWcid)));
        Assert.Contains(creates[1387], r => r.L("id") == LockpickFacts.CreateListRow && r.L("weenie_Class_Id") == VillaFacts.LockpickWcid && r.L("destination_Type") == 4);

        var ints = tables["weenie_properties_int"].View().ToDictionary(r => (r.L("object_Id"), r.L("type")), r => r.L("value"));
        Assert.Equal((long)VillaFacts.Block1401.MinLevel, ints[(VillaFacts.VillaGuardianWcid, MinLevel)]);
        Assert.Equal(((long)VillaFacts.ChestItems, (long)VillaFacts.ChestPacks), (ints[(VillaFacts.StorageWcid, ItemsCapacity)], ints[(VillaFacts.StorageWcid, ContainersCapacity)]));
        Assert.Equal((long)LockpickFacts.LockpickValue, ints[(VillaFacts.LockpickWcid, ValueInt)]);
        Assert.Equal(LockpickFacts.SellRate, Assert.Single(tables["weenie_properties_float"].View()).F("value"), 6);

        var strings = tables["weenie_properties_string"].View().ToLookup(r => (r.L("object_Id"), r.L("type")), r => r.S("value"));
        var quests = tables["quest"].View().ToDictionary(r => r.S("name")!.ToUpperInvariant());
        foreach (var cave in WritFacts.Caves)
        {
            Assert.Equal(cave.StoneName, strings[(cave.StoneWcid, NameString)].Single());
            Assert.Equal(cave.QuestRow, strings[(cave.StoneWcid, QuestString)].Single());
            Assert.Equal((3600L, -1L), (quests[cave.QuestRow.ToUpperInvariant()].L("min_Delta"), quests[cave.QuestRow.ToUpperInvariant()].L("max_Solves")));
        }

        Assert.Equal(WritFacts.HoltburgAgent.Name, strings[(WritFacts.AgentWcid, NameString)].Single());
        Assert.Equal(LockpickFacts.Merchant.Name, strings[(1387, NameString)].Single());
        Assert.Equal(1L, quests["MINORSTONEWRIT"].L("max_Solves"));
        var actions = tables["weenie_properties_emote_action"].View().ToLookup(r => r.L("emote_Id"));
        var sets = tables["weenie_properties_emote"].View().ToList();
        Assert.Equal(4, sets.Count(s => s.L("category") == CategoryGive));
        Assert.All(sets.Where(s => s.L("category") == CategoryGive), s => Assert.Contains(actions[s.L("id")], a => a.L("type") == EmoteUpdateQuest && a.S("message")!.StartsWith("MinorStoneWrit", StringComparison.Ordinal)));
        Assert.All(sets.Where(s => s.L("category") == CategoryQuestSuccess), s => Assert.Contains(actions[s.L("id")], a => a.L("type") == EmoteTell && a.S("message") == WritFacts.FirstStoneLine));
        Assert.All(sets.Where(s => s.L("category") == CategoryQuestFailure), s =>
        {
            Assert.Contains(actions[s.L("id")], a => a.L("type") == EmoteTell && a.S("message") == WritFacts.WritLine);
            Assert.Contains(actions[s.L("id")], a => a.L("type") == EmoteGive && a.L("weenie_Class_Id") == VillaFacts.WritWcid && a.L("stack_Size") == 1);
        });
    }
}
