using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 5 Q6a (D28, D54, D57): every number of item 5's quest files (the two kill tasks and the ten tusks, M6 14 armory's Armored Tusker Tusk the tenth: the files that carry an <c>experience</c>) against the pinned world SQL v0.9.296 (a read-only parse of the zip): each
/// wcid exists with its name; each NPC stands in the step's cell at its point; each Give emote of the tusk turn-ins and each turn-in set of the kill tasks exists, carries the reply, gives what <c>expect.item</c> says and awards the XP the file's
/// <c>experience</c> says; the kill target carries the KillQuest string, the NPC arms that quest and the quest row's max solves equals the step's <c>count</c>; the timer equals the quest row's min delta (the kill task's Wait stamp, the tusk's pickup
/// quest); the hunt-until carrier's create list holds the item at destination Contain and the ground's profile lists the carrier (or the kill target); the level is the entry portal's MinLevel and MaxLevel. A step of a shape the test does not
/// know fails (an unchecked value is a failure). Exhaustive tier, as <see cref="TravelRoutesSqlTests"/>: it reads about 100 MB of text; skipped without the zip.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class QuestCatalogSqlTests
{
    private const long NameString = 1, QuestString = 33, KillQuestString = 45;
    private const long MinLevel = 86, MaxLevel = 87, AllowGive = 8;
    private const long EmoteAwardXp = 2, EmoteGive = 3, EmoteTell = 10, EmoteStampQuest = 22, EmoteEraseQuest = 31, EmoteAwardLevelProportionalXp = 49, EmoteAwardNoShareXp = 62, EmoteSetQuestCompletions = 70;
    private const long CategoryGive = 6;

    /// <summary>ACE's kill counter ends with this at max solves (<c>QuestManager.cs:622</c> @95ef426d): ACE's own text, not an NPC's.</summary>
    private const string KillComplete = "Your task is complete!";

    /// <summary>The Tusker Guard task's four KillQuest bearers (cache/m6/5/facts.md section 4): the target 1629, the Barracks's tusk carrier 22593, 22592 and the unplaced 21525.</summary>
    private static readonly uint[] TuskerGuardKillQuestBearers = [1629, 22593, 22592, 21525];

    /// <summary>M6 8 B2a: the three low-level fetches, checked by <see cref="Every_value_of_the_fetch_files_is_what_the_pinned_world_SQL_holds"/>.</summary>
    private static readonly string[] FetchKeys = ["worcer-heirlooms", "nen-ai-pet-drudge", "four-stones-of-jojii", "lubziklan-stolen-goods"];

    /// <summary>M6 8 B2b: the wand's chain, the stone, Burun and the two offline fetches, checked by <see cref="Every_value_of_the_chain_and_offline_files_is_what_the_pinned_world_SQL_holds"/>.</summary>
    private static readonly string[] ChainKeys = ["atlan-wand", "minor-smoldering-stone", "burun-history", "repaired-shadow-stone", "shield-of-yanshi", "minor-shivering-stone", "minor-sparking-stone", "minor-stinging-stone"]; // M6 villa: the three other minor stones

    private static IReadOnlyList<Quest> ItemFive() => QuestCatalog.All.Where(q => q.Experience is not null && !FetchKeys.Contains(q.Key) && !ChainKeys.Contains(q.Key)).ToList();

    private static IEnumerable<QuestThing> Things(Quest q)
    {
        if (q.Giver is { } g)
        {
            yield return g;
        }

        foreach (var s in q.Steps)
        {
            if (s.Target is { } t)
            {
                yield return t;
            }

            if (s.Item is { } i)
            {
                yield return i;
            }

            if (s.Expect.Item is { } e)
            {
                yield return e;
            }
        }

        if (q.Done.Item is { } d)
        {
            yield return d;
        }
    }

    private static string Quiet(string questName) => questName.Contains('@', StringComparison.Ordinal) ? questName[..questName.IndexOf('@', StringComparison.Ordinal)] : questName;

    private sealed record EmoteSet(long Id, long Category, uint GiveWcid, List<SqlRow> Actions)
    {
        public IEnumerable<string> Tells => Actions.Where(a => a.L("type") == EmoteTell).Select(a => a.S("message") ?? string.Empty);
    }

    [Fact]
    public void The_item_five_quest_files_are_the_two_kill_tasks_and_the_ten_tusks()
    {
        Assert.Equal(
            ["blood-shreth-kill-task", "tusk-armored", "tusk-crimsonback", "tusk-female", "tusk-goldenback", "tusk-guard", "tusk-liberator", "tusk-male", "tusk-rampager", "tusk-redeemer", "tusk-slave", "tusker-guard-kill-task"],
            ItemFive().Select(q => q.Key));
    }

    /// <summary>
    /// M6 8 B2a (D28, D118): every number of the three fetch files against the pinned world SQL: each wcid with its name; a go to an item, an NPC or a chest stands where the row says (a chest within the use reach); a go with no target is the front of the use step's portal; each use's arrival is its portal's destination; each portal's levels are the file's level band; each give is the NPC's Give emote of that item, with the file's reply and any gift, and the awards sum to the experience;
    /// each picked item's Quest string names a quest row of the file's timer. The chests' contents have no table in the pinned world copy (a generator row, cache/m6/8/facts-fetch2.md section 0).
    /// </summary>
    [RequiresPinnedWorldSqlFact]
    public void Every_value_of_the_fetch_files_is_what_the_pinned_world_SQL_holds()
    {
        var quests = FetchKeys.Select(QuestCatalog.Get).ToList();
        var wcids = new HashSet<uint>(quests.SelectMany(Things).Where(t => t.Wcid is not null).Select(t => t.Wcid!.Value));
        var npcs = new HashSet<uint>(quests.Select(q => q.Giver!.Wcid!.Value));
        var emoteIds = new HashSet<long>();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var tables = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["weenie_properties_string"] = r => r.L("type") is NameString or QuestString && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_bool"] = r => r.L("type") == AllowGive && npcs.Contains((uint)r.L("object_Id")),
            ["weenie_properties_int"] = r => r.L("type") is MinLevel or MaxLevel && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_position"] = r => r.L("position_Type") == 2 && wcids.Contains((uint)r.L("object_Id")),
            ["landblock_instance"] = r => wcids.Contains((uint)r.L("weenie_Class_Id")),
            ["quest"] = _ => true,
            ["weenie_properties_emote"] = r => npcs.Contains((uint)r.L("object_Id")) && r.L("category") == CategoryGive && emoteIds.Add(r.L("id")),
            ["weenie_properties_emote_action"] = r => emoteIds.Contains(r.L("emote_Id")),
        });
        var strings = tables["weenie_properties_string"].View().ToLookup(r => (uint)r.L("object_Id"));
        var ints = tables["weenie_properties_int"].View().ToLookup(r => (uint)r.L("object_Id"));
        var positions = tables["weenie_properties_position"].View().ToLookup(r => (uint)r.L("object_Id"));
        var placements = tables["landblock_instance"].View().ToLookup(r => (uint)r.L("weenie_Class_Id"));
        var questRows = tables["quest"].View().ToLookup(r => r.S("name")!.ToUpperInvariant());
        var actions = tables["weenie_properties_emote_action"].View().ToLookup(r => r.L("emote_Id"));
        var gives = tables["weenie_properties_emote"].View().ToLookup(r => ((uint)r.L("object_Id"), (uint)r.L("weenie_Class_Id")), r => actions[r.L("id")].OrderBy(a => a.L("order")).ToList());
        string? Str(uint wcid, long type) => strings[wcid].Where(r => r.L("type") == type).Select(r => r.S("value")).FirstOrDefault();
        int? Int(uint wcid, long type) => ints[wcid].Where(r => r.L("type") == type).Select(r => (int?)r.L("value")).FirstOrDefault();
        static double Metres(SqlRow row, System.Numerics.Vector3 at) => Math.Sqrt(Math.Pow(row.F("origin_X") - at.X, 2) + Math.Pow(row.F("origin_Y") - at.Y, 2));

        foreach (var q in quests)
        {
            var giver = q.Giver!.Wcid!.Value;
            Assert.Contains(tables["weenie_properties_bool"].View(), b => (uint)b.L("object_Id") == giver && b.L("type") == AllowGive);
            foreach (var t in Things(q))
            {
                Assert.True(Str(t.Wcid!.Value, NameString) == t.Name, $"{q.Key}: wcid {t.Wcid} is named '{t.Name}' in the file and '{Str(t.Wcid!.Value, NameString)}' in the SQL");
            }

            long awards = 0;
            for (var i = 0; i < q.Steps.Count; i++)
            {
                var s = q.Steps[i];
                switch (s.Kind)
                {
                    case QuestStepKind.Go when s.Target is { Wcid: { } w } && placements[w].Any():
                        var point = s.Place!.Point!.Value;
                        Assert.True(placements[w].Any(p => (p.L("obj_Cell_Id") >> 16) == s.Place.Cell >> 16 && Metres(p, point) <= Lifestone.Client.Session.Scenarios.Journey.QuestStepLeg.UseReachMetres), $"{q.Key}: step {i}: no placement of {w} within the use reach of {s.Place}");
                        break;
                    case QuestStepKind.Go when s.Target is null && i + 1 < q.Steps.Count && q.Steps[i + 1].Kind == QuestStepKind.Use:
                        var portal = q.Steps[i + 1].Target!.Wcid!.Value;
                        Assert.True(placements[portal].Any(p => (p.L("obj_Cell_Id") >> 16) == s.Place!.Cell >> 16 && Metres(p, s.Place.Point!.Value) <= 3.1), $"{q.Key}: step {i}: {s.Place} is not at the front of the portal {portal}");
                        break;
                    case QuestStepKind.Use:
                        var dest = Assert.Single(positions[s.Target!.Wcid!.Value]);
                        Assert.True((uint)dest.L("obj_Cell_Id") == s.Place!.Cell, $"{q.Key}: step {i}: arrival {s.Place} vs the SQL's 0x{dest.L("obj_Cell_Id"):X8}");
                        Assert.True(Math.Abs(dest.F("origin_X") - s.Place.Point!.Value.X) < 0.001 && Math.Abs(dest.F("origin_Y") - s.Place.Point.Value.Y) < 0.001 && Math.Abs(dest.F("origin_Z") - s.Place.Point.Value.Z) < 0.001, $"{q.Key}: step {i}: arrival point");
                        if (Int(s.Target.Wcid.Value, MaxLevel) is { } max)
                        {
                            Assert.Equal(new QuestLevel(Int(s.Target.Wcid.Value, MinLevel) ?? 1, max), q.Level);
                        }

                        break;
                    case QuestStepKind.PickUp:
                        var pickup = Str(s.Item!.Wcid!.Value, QuestString);
                        Assert.True(pickup is not null, $"{q.Key}: {s.Item} has no Quest string");
                        var row = Assert.Single(questRows[pickup!.ToUpperInvariant()]);
                        Assert.True(row.L("min_Delta") == q.TimerSeconds && row.L("max_Solves") == -1, $"{q.Key}: {pickup}: min_Delta {row.L("min_Delta")}, max_Solves {row.L("max_Solves")}");
                        break;
                    case QuestStepKind.Give:
                        var set = Assert.Single(gives[(giver, s.Item!.Wcid!.Value)]);
                        Assert.Contains(set, a => a.L("type") == EmoteTell && a.S("message") == s.Expect.Reply);
                        awards += Assert.Single(set, a => a.L("type") == EmoteAwardNoShareXp).L("amount_64");
                        Assert.Equal(s.Expect.Item?.Wcid, set.Where(a => a.L("type") == EmoteGive).Select(a => (uint?)a.L("weenie_Class_Id")).FirstOrDefault());
                        break;
                }
            }

            Assert.Equal(q.Experience!.Amount, awards);
        }
    }

    /// <summary>
    /// M6 8 B2b (D28, D118): every number of the five chain and offline files against the pinned world SQL: each wcid (and each fail item) with its name; a go to a named thing stands in reach of a placement of it; a go directly before a use is at that portal's front; each use's arrival is its portal's destination row and the highest MinLevel met is the file's level; each picked item's Quest string names a row of the file's timer (the chain's stone, one hour);
    /// each give is the NPC's Give emote with the file's reply, stamp and gift; the awards are the experience; the combine and apply steps are their recipe rows (cook_book, recipe: result, whole lines, fail result, the destroy rows) and the file names a fail only for a row that has one; the mote's two droppers and the glyph's shop row.
    /// </summary>
    [RequiresPinnedWorldSqlFact]
    public void Every_value_of_the_chain_and_offline_files_is_what_the_pinned_world_SQL_holds()
    {
        var quests = ChainKeys.Select(QuestCatalog.Get).ToList();
        var things = quests.SelectMany(Things).Concat(quests.SelectMany(q => q.Steps).Where(s => s.FailItem is not null).Select(s => s.FailItem!)).ToList();
        var wcids = new HashSet<uint>(things.Select(t => t.Wcid!.Value));
        var npcs = new HashSet<uint>(quests.Where(q => q.Giver is not null).Select(q => q.Giver!.Wcid!.Value)) { 6356 };
        const long BurunAwardSet = 49535;
        var emoteIds = new HashSet<long>();
        uint[] sellers = [2498, 23082, 199];
        long[] recipes = [789, 790, 791, 792, 6027, 6068];
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var tables = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["cook_book"] = r => recipes.Contains(r.L("recipe_Id")),
            ["recipe"] = r => recipes.Contains(r.L("id")),
            ["weenie_properties_string"] = r => r.L("type") is NameString or QuestString && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_bool"] = r => r.L("type") == AllowGive && npcs.Contains((uint)r.L("object_Id")),
            ["weenie_properties_int"] = r => r.L("type") is MinLevel or MaxLevel && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_position"] = r => r.L("position_Type") == 2 && wcids.Contains((uint)r.L("object_Id")),
            ["landblock_instance"] = r => wcids.Contains((uint)r.L("weenie_Class_Id")),
            ["quest"] = _ => true,
            ["weenie_properties_create_list"] = r => sellers.Contains((uint)r.L("object_Id")),
            ["weenie_properties_emote"] = r => (r.L("id") == BurunAwardSet || (npcs.Contains((uint)r.L("object_Id")) && r.L("category") == CategoryGive)) && emoteIds.Add(r.L("id")),
            ["weenie_properties_emote_action"] = r => emoteIds.Contains(r.L("emote_Id")),
        });
        var strings = tables["weenie_properties_string"].View().ToLookup(r => (uint)r.L("object_Id"));
        var ints = tables["weenie_properties_int"].View().ToLookup(r => (uint)r.L("object_Id"));
        var positions = tables["weenie_properties_position"].View().ToLookup(r => (uint)r.L("object_Id"));
        var placements = tables["landblock_instance"].View().ToLookup(r => (uint)r.L("weenie_Class_Id"));
        var questRows = tables["quest"].View().ToLookup(r => r.S("name")!.ToUpperInvariant());
        var actions = tables["weenie_properties_emote_action"].View().ToLookup(r => r.L("emote_Id"));
        var gives = tables["weenie_properties_emote"].View().ToLookup(r => ((uint)r.L("object_Id"), (uint)r.L("weenie_Class_Id")), r => actions[r.L("id")].OrderBy(a => a.L("order")).ToList());
        var creates = tables["weenie_properties_create_list"].View().ToLookup(r => (uint)r.L("object_Id"));
        var cook = tables["cook_book"].View().ToDictionary(r => ((uint)r.L("source_W_C_I_D"), (uint)r.L("target_W_C_I_D")), r => r.L("recipe_Id"));
        var rows = tables["recipe"].View().ToDictionary(r => r.L("id"));
        Assert.Equal(recipes.Length, rows.Count);
        string? Str(uint wcid, long type) => strings[wcid].Where(r => r.L("type") == type).Select(r => r.S("value")).FirstOrDefault();
        int? Int(uint wcid, long type) => ints[wcid].Where(r => r.L("type") == type).Select(r => (int?)r.L("value")).FirstOrDefault();
        static double Metres(SqlRow row, System.Numerics.Vector3 at) => Math.Sqrt(Math.Pow(row.F("origin_X") - at.X, 2) + Math.Pow(row.F("origin_Y") - at.Y, 2));

        foreach (var t in things)
        {
            Assert.True(Str(t.Wcid!.Value, NameString) == t.Name, $"wcid {t.Wcid} is named '{t.Name}' in the file and '{Str(t.Wcid!.Value, NameString)}' in the SQL");
        }

        Assert.Contains(creates[23082], r => (uint)r.L("weenie_Class_Id") == 6353 && r.L("destination_Type") == 9 && Math.Abs(r.F("shade") - 0.04) < 1e-6);
        Assert.Contains(creates[199], r => (uint)r.L("weenie_Class_Id") == 6353 && r.L("destination_Type") == 9 && Math.Abs(r.F("shade") - 0.01) < 1e-6);
        foreach (var q in quests)
        {
            if (q.Giver is { } giverThing)
            {
                Assert.Contains(tables["weenie_properties_bool"].View(), b => (uint)b.L("object_Id") == giverThing.Wcid && b.L("type") == AllowGive);
            }

            var highest = 0;
            long awards = 0;
            for (var i = 0; i < q.Steps.Count; i++)
            {
                var s = q.Steps[i];
                switch (s.Kind)
                {
                    case QuestStepKind.Go when s.Target is { Wcid: { } w }:
                        var point = s.Place!.Point!.Value;
                        Assert.True(placements[w].Any(p => (p.L("obj_Cell_Id") >> 16) == s.Place.Cell >> 16 && Metres(p, point) <= Lifestone.Client.Session.Scenarios.Journey.QuestStepLeg.UseReachMetres), $"{q.Key}: step {i}: no placement of {w} within the use reach of {s.Place}");
                        break;
                    case QuestStepKind.Go when i + 1 < q.Steps.Count && q.Steps[i + 1].Kind == QuestStepKind.Use:
                        var portal = q.Steps[i + 1].Target!.Wcid!.Value;
                        Assert.True(placements[portal].Any(p => (p.L("obj_Cell_Id") >> 16) == s.Place!.Cell >> 16 && Metres(p, s.Place.Point!.Value) <= 3.1), $"{q.Key}: step {i}: {s.Place} is not at the front of the portal {portal}");
                        break;
                    case QuestStepKind.Use:
                        var dest = Assert.Single(positions[s.Target!.Wcid!.Value]);
                        Assert.True((uint)dest.L("obj_Cell_Id") == s.Place!.Cell, $"{q.Key}: step {i}: arrival {s.Place} vs the SQL's 0x{dest.L("obj_Cell_Id"):X8}");
                        Assert.True(Math.Abs(dest.F("origin_X") - s.Place.Point!.Value.X) < 0.001 && Math.Abs(dest.F("origin_Y") - s.Place.Point.Value.Y) < 0.001 && Math.Abs(dest.F("origin_Z") - s.Place.Point.Value.Z) < 0.001, $"{q.Key}: step {i}: arrival point");
                        highest = Math.Max(highest, Int(s.Target.Wcid.Value, MinLevel) ?? 0);
                        break;
                    case QuestStepKind.PickUp:
                        var pickup = Str(s.Item!.Wcid!.Value, QuestString);
                        Assert.True(pickup is not null, $"{q.Key}: {s.Item} has no Quest string");
                        var row = Assert.Single(questRows[pickup!.ToUpperInvariant()]);
                        Assert.Equal(-1L, row.L("max_Solves"));
                        Assert.Equal(q.TimerSeconds ?? 3600L, row.L("min_Delta"));
                        break;
                    case QuestStepKind.Give:
                        var set = Assert.Single(gives[(s.Target!.Wcid!.Value, s.Item!.Wcid!.Value)]);
                        if (s.Expect.Reply is { } reply)
                        {
                            Assert.Contains(set, a => a.L("type") == EmoteTell && a.S("message") == reply);
                        }
                        else
                        {
                            Assert.DoesNotContain(set, a => a.L("type") == EmoteTell);
                        }

                        Assert.Equal(s.Expect.Item?.Wcid, set.Where(a => a.L("type") == EmoteGive).Select(a => (uint?)a.L("weenie_Class_Id")).FirstOrDefault());
                        Assert.Equal(s.Expect.Stamp, set.Where(a => a.L("type") == EmoteStampQuest).Select(a => a.S("message")).FirstOrDefault());
                        awards += set.Where(a => a.L("type") == EmoteAwardXp).Sum(a => a.L("amount_64"));
                        break;
                    case QuestStepKind.Combine or QuestStepKind.Apply:
                        var id = cook[(s.Item!.Wcid!.Value, s.Target!.Wcid!.Value)];
                        var r = rows[id];
                        Assert.Contains($"recipe row {id}", s.Source, StringComparison.Ordinal);
                        Assert.Equal(((long)s.Expect.Item!.Wcid!.Value, s.Expect.Reply), (r.L("success_W_C_I_D"), r.S("success_Message")));
                        Assert.Equal(id == 6068 ? 0L : 1L, r.L("success_Destroy_Source_Chance"));
                        if (s.FailReply is { } failLine)
                        {
                            Assert.Equal(failLine, r.S("fail_Message"));
                            Assert.Equal((long)s.FailItem!.Wcid!.Value, r.L("fail_W_C_I_D"));
                        }
                        else
                        {
                            Assert.Equal((0L, 0L), (r.L("difficulty"), r.L("fail_W_C_I_D"))); // a combine with no roll names no fail
                        }

                        if (id == 789)
                        {
                            Assert.Equal((38L, 150L), (r.L("skill"), r.L("difficulty")));
                        }

                        break;
                    case QuestStepKind.Buy:
                        Assert.Contains(creates[s.Target!.Wcid!.Value], c => (uint)c.L("weenie_Class_Id") == s.Item!.Wcid && c.L("destination_Type") == 4);
                        break;
                }
            }

            Assert.Equal(q.Level?.Min ?? 0, highest);
            if (q.Experience is { Kind: QuestExperienceKind.Fixed } xp)
            {
                Assert.Equal(xp.Amount, awards);
            }
            else if (q.Experience is { } proportional)
            {
                var award = Assert.Single(actions[BurunAwardSet], a => a.L("type") == EmoteAwardLevelProportionalXp);
                Assert.True(award.L("max_64") == proportional.Max && Math.Abs(award.F("percent") - proportional.Percent!.Value) < 1e-9, $"{q.Key}: proportional {award.L("max_64")} at {award.F("percent")}");
            }
        }
    }

    [RequiresPinnedWorldSqlFact]
    public void Every_value_of_the_kill_task_and_tusk_files_is_what_the_pinned_world_SQL_holds()
    {
        var quests = ItemFive();
        var wcids = new HashSet<uint>(quests.SelectMany(Things).Where(t => t.Wcid is not null).Select(t => t.Wcid!.Value));
        var npcs = new HashSet<uint>(quests.SelectMany(q => q.Steps).Where(s => s.Kind is QuestStepKind.Go or QuestStepKind.Talk or QuestStepKind.Give && s.Target?.Wcid is not null).Select(s => s.Target!.Wcid!.Value));
        npcs.UnionWith(quests.Select(q => q.Giver!.Wcid!.Value));
        var carriers = new HashSet<uint>(quests.SelectMany(q => q.Steps).Where(s => s.Kind is QuestStepKind.HuntUntil).Select(s => s.Target!.Wcid!.Value));
        wcids.UnionWith(TuskerGuardKillQuestBearers); // the file's sources say all four count
        var entryWcids = new HashSet<uint>(quests.SelectMany(q => q.Steps).Where(s => s.Ground is not null).Select(s => GroundCatalog.Get(s.Ground!).Entry?.Wcid).Where(w => w is not null).Select(w => w!.Value));
        var emoteIds = new HashSet<long>();
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var tables = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["weenie"] = r => wcids.Contains((uint)r.L("class_Id")),
            ["weenie_properties_string"] = r => r.L("type") is NameString or QuestString or KillQuestString && wcids.Contains((uint)r.L("object_Id")),
            ["weenie_properties_bool"] = r => r.L("type") == AllowGive && npcs.Contains((uint)r.L("object_Id")),
            ["weenie_properties_int"] = r => r.L("type") is MinLevel or MaxLevel && entryWcids.Contains((uint)r.L("object_Id")),
            ["landblock_instance"] = r => npcs.Contains((uint)r.L("weenie_Class_Id")),
            ["weenie_properties_create_list"] = r => carriers.Contains((uint)r.L("object_Id")),
            ["quest"] = _ => true,
            ["weenie_properties_emote"] = r => npcs.Contains((uint)r.L("object_Id")) && emoteIds.Add(r.L("id")),
            ["weenie_properties_emote_action"] = r => emoteIds.Contains(r.L("emote_Id")),
        });

        Assert.True(tables["weenie_properties_emote_action"].Rows.Count > 0, "the emote actions were read after the emote rows (mysqldump writes the tables alphabetically): none kept");
        var weenies = tables["weenie"].View().Select(r => (uint)r.L("class_Id")).ToHashSet();
        var strings = tables["weenie_properties_string"].View().ToLookup(r => (uint)r.L("object_Id"));
        var bools = tables["weenie_properties_bool"].View().ToLookup(r => (uint)r.L("object_Id"));
        var ints = tables["weenie_properties_int"].View().ToLookup(r => (uint)r.L("object_Id"));
        var placements = tables["landblock_instance"].View().ToLookup(r => (uint)r.L("weenie_Class_Id"));
        var createLists = tables["weenie_properties_create_list"].View().ToLookup(r => (uint)r.L("object_Id"));
        var questRows = tables["quest"].View().ToLookup(r => r.S("name")!.ToUpperInvariant());
        var actions = tables["weenie_properties_emote_action"].View().ToLookup(r => r.L("emote_Id"));
        var sets = tables["weenie_properties_emote"].View().ToLookup(r => (uint)r.L("object_Id"), r => new EmoteSet(r.L("id"), r.L("category"), (uint)r.L("weenie_Class_Id"), actions[r.L("id")].OrderBy(a => a.L("order")).ToList()));

        string? Str(uint wcid, long type) => strings[wcid].Where(r => r.L("type") == type).Select(r => r.S("value")).FirstOrDefault();
        SqlRow QuestRow(string name) => Assert.Single(questRows[name.ToUpperInvariant()]);

        EmoteSet SetWithReply(uint npc, string reply, string why)
        {
            var found = sets[npc].Where(s => s.Tells.Any(t => t.Contains(reply, StringComparison.Ordinal))).ToList();
            Assert.True(found.Count >= 1, $"{why}: no emote set of {npc} has a Tell containing '{reply}'");
            return found[0];
        }

        foreach (var q in quests)
        {
            // each wcid exists with its name
            foreach (var t in Things(q))
            {
                var w = t.Wcid!.Value;
                Assert.True(weenies.Contains(w), $"{q.Key}: wcid {w} ({t.Name}) is not in the weenie table");
                Assert.True(Str(w, NameString) == t.Name, $"{q.Key}: wcid {w} is named '{t.Name}' in the file and '{Str(w, NameString)}' in the SQL");
            }

            var giver = q.Giver!.Wcid!.Value;
            Assert.All(q.Steps.Where(s => s.Kind is QuestStepKind.Talk or QuestStepKind.Give), s => Assert.Equal(giver, s.Target!.Wcid!.Value));

            // each NPC stands where the go step says
            foreach (var s in q.Steps.Where(s => s.Kind == QuestStepKind.Go))
            {
                Assert.Equal(giver, s.Target!.Wcid!.Value);
                var place = s.Place!;
                var point = place.Point!.Value;
                Assert.True(placements[giver].Any(p => (uint)p.L("obj_Cell_Id") == place.Cell && Math.Abs(p.F("origin_X") - point.X) < 0.001 && Math.Abs(p.F("origin_Y") - point.Y) < 0.001 && Math.Abs(p.F("origin_Z") - point.Z) < 0.001),
                    $"{q.Key}: step {s.Index}: no placement of {giver} in 0x{place.Cell:X8} at {point}");
            }

            var kill = q.Steps.SingleOrDefault(s => s.Kind == QuestStepKind.Kill);
            var hunt = q.Steps.SingleOrDefault(s => s.Kind == QuestStepKind.HuntUntil);
            var turnIn = q.Steps[^1];
            Assert.True((kill is null) != (hunt is null), $"{q.Key}: a quest file of item 5 has one kill or one hunt-until step");
            Assert.All(q.Steps, s => Assert.True(s.Kind is QuestStepKind.Go or QuestStepKind.Talk or QuestStepKind.Give or QuestStepKind.Kill or QuestStepKind.HuntUntil, $"{q.Key}: step {s.Index} is a {s.Kind}: a shape this test does not check"));
            Assert.True(turnIn.Kind is QuestStepKind.Talk or QuestStepKind.Give, $"{q.Key}: the last step is the turn-in");
            Assert.Equal(q.Done.Item?.Wcid, turnIn.Expect.Item?.Wcid);

            // the turn-in set: the reply, what it gives, the award
            var set = SetWithReply(giver, turnIn.Expect.Reply!, q.Key + " turn-in");
            Assert.True(set.Tells.Any(t => t.Contains(q.Done.Reply!, StringComparison.Ordinal)), $"{q.Key}: done.reply '{q.Done.Reply}' is in no Tell of the turn-in set {set.Id}");
            if (turnIn.Expect.Item is { } reward)
            {
                Assert.Contains(set.Actions, a => a.L("type") == EmoteGive && (uint)a.L("weenie_Class_Id") == reward.Wcid && a.L("stack_Size") == 1);
            }

            var ex = q.Experience!;
            if (ex.Kind == QuestExperienceKind.Fixed)
            {
                var award = set.Actions.Where(a => a.L("type") is EmoteAwardXp or EmoteAwardNoShareXp).ToList();
                Assert.True(award.Count == 1, $"{q.Key}: the turn-in set {set.Id} has {award.Count} fixed awards");
                Assert.True(award[0].L("amount_64") == ex.Amount, $"{q.Key}: experience {ex.Amount} vs the SQL's {award[0].L("amount_64")} (emote {set.Id})");
            }
            else
            {
                var award = Assert.Single(set.Actions, a => a.L("type") == EmoteAwardLevelProportionalXp);
                Assert.True(award.L("max_64") == ex.Max && Math.Abs(award.F("percent") - ex.Percent!.Value) < 1e-9, $"{q.Key}: proportional max {ex.Max} at {ex.Percent} vs the SQL's {award.L("max_64")} at {award.F("percent")}");
            }

            if (kill is not null)
            {
                // the kill target carries the KillQuest string; the giver arms that quest; the row's max solves is the count; the Wait stamp is the timer
                Assert.Equal(KillComplete, kill.Expect.Reply);
                var killQuest = Str(kill.Target!.Wcid!.Value, KillQuestString);
                Assert.True(killQuest is not null, $"{q.Key}: the kill target {kill.Target} has no KillQuest string");
                Assert.True(QuestRow(killQuest!).L("max_Solves") == kill.Count, $"{q.Key}: kill count {kill.Count} vs quest {killQuest} max_Solves {QuestRow(killQuest!).L("max_Solves")}");
                var arm = q.Steps.Single(s => s.Kind == QuestStepKind.Talk && s.Index < kill.Index);
                Assert.Contains(SetWithReply(giver, arm.Expect.Reply!, q.Key + " arm").Actions, a => a.L("type") == EmoteSetQuestCompletions && a.S("message") == killQuest && a.L("amount") == 0);
                Assert.Contains(set.Actions, a => a.L("type") == EmoteEraseQuest && a.S("message") == killQuest);
                var stamp = Assert.Single(set.Actions, a => a.L("type") == EmoteStampQuest);
                var wait = QuestRow(stamp.S("message")!);
                Assert.True(wait.L("min_Delta") == q.TimerSeconds && wait.L("max_Solves") == -1, $"{q.Key}: timer {q.TimerSeconds} vs {stamp.S("message")} min_Delta {wait.L("min_Delta")}, max_Solves {wait.L("max_Solves")}");

                // the ground's profile lists the target; every KillQuest bearer named in the file's sources is a wcid of the profile or a weenie the SQL gives that string
                var profile = GroundCatalog.Get(kill.Ground!);
                Assert.Contains(profile.Targets, t => t.Wcid == kill.Target.Wcid);
                Assert.Equal(new QuestLevel(1, null), q.Level);
                Assert.Equal(q.Giver!.Wcid, turnIn.Target!.Wcid);
                var giverPlacement = Assert.Single(placements[giver]);
                Assert.Equal(giverPlacement.L("obj_Cell_Id"), (long)q.Start.Cell);
            }
            else
            {
                // the hunt-until carrier's create list holds the tusk (Contain); the tusk's pickup quest is the timer; the ground lists the carrier; the give's emote is the tusk's own
                var carrier = hunt!.Target!.Wcid!.Value;
                var tusk = hunt.Item!.Wcid!.Value;
                var row = Assert.Single(createLists[carrier], r => (uint)r.L("weenie_Class_Id") == tusk);
                Assert.True(row.L("destination_Type") == 1 && row.L("stack_Size") == hunt.Count, $"{q.Key}: create list of {carrier}: destination {row.L("destination_Type")}, stack {row.L("stack_Size")} vs count {hunt.Count}");
                var profile = GroundCatalog.Get(hunt.Ground!);
                Assert.Contains(profile.Targets, t => t.Wcid == carrier);
                var give = q.Steps.Single(s => s.Kind == QuestStepKind.Give);
                Assert.Equal(tusk, give.Item!.Wcid!.Value);
                Assert.True(set.Category == CategoryGive && set.GiveWcid == tusk, $"{q.Key}: the turn-in set {set.Id} is category {set.Category} of {set.GiveWcid}, not a Give of {tusk}");
                Assert.True(bools[giver].Any(b => b.L("type") == AllowGive && b.S("value") == ""), $"{q.Key}: {giver} does not allow gives");
                var pickup = Str(tusk, QuestString);
                Assert.True(pickup is not null, $"{q.Key}: the tusk {tusk} has no Quest string");
                var pickupRow = QuestRow(pickup!);
                Assert.True(pickupRow.L("min_Delta") == q.TimerSeconds && pickupRow.L("max_Solves") == -1, $"{q.Key}: timer {q.TimerSeconds} vs {pickup} min_Delta {pickupRow.L("min_Delta")}, max_Solves {pickupRow.L("max_Solves")}");

                // the level is the entry portal's
                var entry = profile.Entry!.Wcid;
                int? Int(long type) => ints[entry].Where(r => r.L("type") == type).Select(r => (int?)r.L("value")).FirstOrDefault();
                Assert.Equal(new QuestLevel(Int(MinLevel) ?? 1, Int(MaxLevel)), q.Level);

                // the quest starts at the Aphus Lassel gem's arrival (the Refuge)
                var gem = Assert.Single(TravelRoutes.Default.Gems, g => g.Wcid == q.Done.Item!.Wcid);
                Assert.True(q.Start.Cell == gem.DestCell && q.Start.Point is { } p && Math.Abs(p.X - gem.DestOrigin.X) < 0.001 && Math.Abs(p.Y - gem.DestOrigin.Y) < 0.001 && Math.Abs(p.Z - gem.DestOrigin.Z) < 0.001, $"{q.Key}: start {q.Start} vs the gem's arrival 0x{gem.DestCell:X8} {gem.DestOrigin}");
            }
        }

        // the Tusker Guard task's four bearers all carry its KillQuest string (the file says 22593 and 22592 count too)
        foreach (var w in TuskerGuardKillQuestBearers)
        {
            Assert.Equal("TuskerGkillcount", Str(w, KillQuestString));
        }
    }
}
