using System.Text.Json.Nodes;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// The quest profile (M6 item 0 a2; DoD 7, D26: a shape is code, a quest is a file): the two embedded files load with their steps in
/// order; a missing, unknown or malformed field fails naming the file and the JSON path; a step kind of the vocabulary that item 0 does
/// not implement fails loudly at load; <see cref="QuestTimers"/> round-trips a completion through a store and computes the next due time.
/// </summary>
public sealed class QuestProfileTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 1, 10, 0, TimeSpan.Zero);

    private static JsonObject Pathwarden() => JsonNode.Parse(QuestCatalog.ResourceText("pathwarden.json")!)!.AsObject();

    private static QuestFileException Refused(JsonNode file, string name = "pathwarden.json") =>
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse(name, file.ToJsonString()));

    /// <summary>A quest with a 20-hour timer, built from the Pathwarden file.</summary>
    private static Quest Daily()
    {
        var file = Pathwarden();
        file["key"] = "daily";
        file["timer_seconds"] = 72000;
        return QuestCatalog.Parse("daily.json", file.ToJsonString());
    }

    [Fact]
    public void Both_quest_files_load_and_an_unknown_key_is_named()
    {
        string[] itemZero = ["academy-skip", "academy", "pathwarden"];
        Assert.All(QuestCatalog.All.Where(q => itemZero.Contains(q.Key)), q => Assert.True(q.Level is null && q.Experience is null, q.Key)); // M6 5 Q1: the three item-0 files name neither optional quest field
        Assert.All(QuestCatalog.All.Where(q => itemZero.Contains(q.Key)), q => Assert.All(q.Steps, s => Assert.True(s.Count is null && s.Ground is null && s.Spell is null && s.Answer is null, s.ToString())));
        string[] noAward = ["atlan-wand", "minor-shivering-stone", "minor-smoldering-stone", "minor-sparking-stone", "minor-stinging-stone"]; // M6 8 B2b: two errands with no award and no level gate (the chain is once, the stone's timer an hour); M6 villa: the other three minor stones
        Assert.All(QuestCatalog.All.Where(q => !itemZero.Contains(q.Key) && !noAward.Contains(q.Key)), q => Assert.True(q.Level is not null && q.Experience is not null && q.TimerSeconds == 72000, q.Key)); // M6 5 Q6a: the kill tasks and the tusks carry a level, an award and the 20-hour timer
        Assert.Equal(
            ["academy-skip", "academy", "atlan-wand", "blood-shreth-kill-task", "burun-history", "four-stones-of-jojii", "lubziklan-stolen-goods", "minor-shivering-stone", "minor-smoldering-stone", "minor-sparking-stone", "minor-stinging-stone", "nen-ai-pet-drudge", "pathwarden", "repaired-shadow-stone", "shield-of-yanshi", "tusk-armored", "tusk-crimsonback", "tusk-female", "tusk-goldenback", "tusk-guard", "tusk-liberator", "tusk-male", "tusk-rampager", "tusk-redeemer", "tusk-slave", "tusker-guard-kill-task", "worcer-heirlooms"],
            QuestCatalog.All.Select(q => q.Key)); // M6 villa: the Shivering, Sparking and Stinging stones; M6 8 B2b: the wand, the stone, Burun and the two offline fetches; M6 8 B2a: the three low-level fetches; M6 8 B2c: the Stolen Goods; M6 0 F7: the skip's file sorts by its file name ('-' before '.'); M6 5 Q6a: the kill tasks and the nine profiled tusks (the five unprofiled have TODO(m6-tusk-unprofiled-grounds))
        Assert.Same(QuestCatalog.Get("pathwarden"), QuestCatalog.All[12]);
        Assert.All(QuestCatalog.All, q => Assert.All(q.Steps, s => Assert.True(QuestStepKinds.IsImplemented(s.Kind))));
        Assert.Contains("no-such-quest", Assert.Throws<KeyNotFoundException>(() => QuestCatalog.Get("no-such-quest")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Academy_runs_stone_leather_token_orb_exit_in_order()
    {
        var q = QuestCatalog.Get("academy");
        Assert.Null(q.Giver);
        Assert.Equal(0x860201ADu, q.Start.Cell);
        Assert.Equal(
            ["give", "go", "pick-up", "go", "pick-up", "pick-up", "go", "talk", "go", "talk", "go", "pick-up", "give", "go", "use", "go", "pick-up", "go", "give", "go", "use", "go", "talk", "go", "use", "go", "talk", "go", "pick-up", "go", "give", "go", "use"],
            q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
        Assert.Equal(Enumerable.Range(0, q.Steps.Count), q.Steps.Select(s => s.Index));
        Assert.Equal((5084u, 30991u, "CallingStoneGiven"), (q.Steps[0].Item!.Wcid!.Value, q.Steps[0].Target!.Wcid!.Value, q.Steps[0].Expect.Stamp));
        Assert.Equal([13240u, 13241u, 13239u], q.Steps.Where(s => s.Kind == QuestStepKind.PickUp && s.Target is null).Select(s => s.Item!.Wcid!.Value)); // M6 0 a3b: the gauntlets, leggings and cap by the placements (sel-academy-8602.tsv)
        Assert.Equal((12698u, 12709u), (q.Steps[11].Target!.Wcid!.Value, q.Steps[11].Item!.Wcid!.Value)); // the token from a Sparring Golem's corpse
        Assert.Equal((12709u, 29320u, "AcademyTokenGiven"), (q.Steps[12].Item!.Wcid!.Value, q.Steps[12].Target!.Wcid!.Value, q.Steps[12].Expect.Stamp));
        Assert.Equal((29333u, 12710u), (q.Steps[16].Target!.Wcid!.Value, q.Steps[16].Item!.Wcid!.Value)); // M6 0 F6: the Bellows from a Thieving Thrungus's corpse, for the Academy Blacksmith's library key
        Assert.Equal((12710u, 30996u, 30999u), (q.Steps[18].Item!.Wcid!.Value, q.Steps[18].Target!.Wcid!.Value, q.Steps[18].Expect.Item!.Wcid!.Value));
        Assert.Equal((30999u, 30998u), (q.Steps[20].Item!.Wcid!.Value, q.Steps[20].Target!.Wcid!.Value)); // the key on the Academy Library door
        Assert.Equal((29331u, 29336u), (q.Steps[28].Target!.Wcid!.Value, q.Steps[28].Item!.Wcid!.Value)); // the orb from an Adolescent Olthoi's corpse
        Assert.Equal(("SentryTaskComplete", 29336u), (q.Steps[30].Expect.Stamp, q.Steps[30].Item!.Wcid!.Value));
        var exit = q.Steps[^1];
        Assert.Equal((29338u, 0xA9B40019u), (exit.Target!.Wcid!.Value, exit.Place!.Cell));
        Assert.Equal(0x86020169u, q.Steps[^2].Place!.Cell);
        Assert.Null(q.Done.Stamp); // the exit's emote set erases the Academy's stamps (docs/session.md section 25)
        Assert.Equal(exit.Expect.Reply, q.Done.Reply);
        Assert.Null(q.TimerSeconds);
        Assert.DoesNotContain(q.Todos, t => t.StartsWith("TODO(m6-academy-courtyard-arrival)", StringComparison.Ordinal) || t.StartsWith("TODO(m6-academy-outer-courtyard)", StringComparison.Ordinal)); // closed by the portals' instance links (M6 0 F6)
        Assert.Contains(q.Todos, t => t.StartsWith("TODO(m6-academy-sentry-percent)", StringComparison.Ordinal));
        Assert.DoesNotContain(q.Todos, t => t.StartsWith("TODO(m6-academy-token-kill)", StringComparison.Ordinal) || t.StartsWith("TODO(m6-academy-spawns)", StringComparison.Ordinal) || t.StartsWith("TODO(m6-academy-hive-orb)", StringComparison.Ordinal)); // closed by the lead's SELECTs (a3b)
    }

    [Fact]
    public void The_Pathwarden_quest_gives_the_token_opens_the_chest_and_takes_four_pieces()
    {
        var q = QuestCatalog.Get("pathwarden");
        Assert.Equal(33596u, q.Giver!.Wcid);
        Assert.Equal(["go", "give", "use", "pick-up", "pick-up", "pick-up", "pick-up"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind))); // no talk: the give needs none (item 0 live round 1)
        var give = q.Steps[1];
        Assert.Equal((33613u, 33596u, 33608u, "PathwardenComplete"), (give.Item!.Wcid!.Value, give.Target!.Wcid!.Value, give.Expect.Item!.Wcid!.Value, give.Expect.Stamp));
        Assert.Equal((33608u, 33609u), (q.Steps[2].Item!.Wcid!.Value, q.Steps[2].Target!.Wcid!.Value));
        Assert.Equal([40439u, 33606u, 33605u, 41513u], q.Steps.Skip(3).Select(s => s.Item!.Wcid!.Value));
        Assert.All(q.Steps.Skip(3), s => Assert.Equal(33609u, s.Target!.Wcid));
        Assert.Equal(("PathwardenComplete", 41513u), (q.Done.Stamp, q.Done.Item!.Wcid!.Value));
        Assert.Equal("step 1 give Pathwarden Token (33613) to Pathwarden Thorolf (33596); expect stamp PathwardenComplete, item Pathwarden Supply Key (33608)", give.ToString());
    }

    [Fact]
    public void A_missing_field_fails_naming_the_file_and_the_path()
    {
        var file = Pathwarden();
        file["steps"]![2]!.AsObject().Remove("source");
        var e = Refused(file);
        Assert.Equal(("pathwarden.json", "$.steps[2].source"), (e.File, e.JsonPath));
        Assert.StartsWith("quest file pathwarden.json: $.steps[2].source is missing", e.Message, StringComparison.Ordinal);

        var noTimer = Pathwarden();
        noTimer.Remove("timer_seconds");
        Assert.Equal("$.timer_seconds", Refused(noTimer).JsonPath);
    }

    [Fact]
    public void An_unknown_field_fails_naming_the_file_and_the_path()
    {
        var file = Pathwarden();
        file["steps"]![0]!["target"]!["cell"] = "0xA9B40019";
        var e = Refused(file);
        Assert.Equal(("pathwarden.json", "$.steps[0].target.cell"), (e.File, e.JsonPath));
        Assert.Contains("unknown field", e.Message, StringComparison.Ordinal);

        var top = Pathwarden();
        top["levels"] = 1; // M6 5 Q1: "level" is now an optional field (a wrong type is refused by A_quest_level_and_experience_are_read_strictly)
        Assert.Equal("$.levels", Refused(top).JsonPath);
    }

    private static Quest Loaded(JsonNode file) => QuestCatalog.Parse("pathwarden.json", file.ToJsonString());

    private static JsonObject RecipeStep(string word)
    {
        var step = new JsonObject
        {
            ["kind"] = word,
            ["what"] = "two motes",
            ["todo"] = null,
            ["place"] = null,
            ["target"] = new JsonObject { ["wcid"] = 6353, ["name"] = "Pyreal Mote" },
            ["item"] = new JsonObject { ["wcid"] = 6353, ["name"] = "Pyreal Mote" },
            ["expect"] = new JsonObject { ["reply"] = "You combine the two motes into a larger sliver.", ["stamp"] = null, ["item"] = new JsonObject { ["wcid"] = 6355, ["name"] = "Pyreal Sliver" } },
            ["source"] = "the recipe row",
        };
        if (word == "apply")
        {
            step["fail_reply"] = "You fail.";
        }

        return step;
    }

    private static JsonObject WithRecipe(JsonObject step)
    {
        var file = Pathwarden();
        file["steps"]![1] = step;
        return file;
    }

    [Theory]
    [InlineData("combine")]
    [InlineData("apply")]
    public void A_recipe_step_loads_with_its_fields_and_the_count_repeats(string word)
    {
        var step = RecipeStep(word);
        step["count"] = 8;
        var loaded = Loaded(WithRecipe(step)).Steps[1];

        Assert.Equal(QuestStepKinds.Parse(word), loaded.Kind);
        Assert.Equal(8, loaded.Count);
        Assert.Equal(6353u, loaded.Item!.Wcid);
        Assert.Equal(6355u, loaded.Expect.Item!.Wcid);
        Assert.Contains($"{word} Pyreal Mote (6353) on Pyreal Mote (6353) x8", loaded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_combine_with_a_roll_names_its_fail_result_and_fail_line()
    {
        var step = RecipeStep("combine");
        step["fail_item"] = new JsonObject { ["wcid"] = 6330, ["name"] = "Pyreal Ingot" };
        step["fail_reply"] = "You combine the two bars into an ingot.";
        var loaded = Loaded(WithRecipe(step)).Steps[1];

        Assert.Equal(6330u, loaded.FailItem!.Wcid);
        Assert.Equal("You combine the two bars into an ingot.", loaded.FailReply);
    }

    [Theory]
    [InlineData("combine", "item", "$.steps[1].item")]
    [InlineData("apply", "item", "$.steps[1].item")]
    [InlineData("combine", "target", "$.steps[1].target")]
    [InlineData("apply", "target", "$.steps[1].target")]
    public void A_recipe_step_without_its_items_fails_loud_at_load(string word, string field, string path)
    {
        var step = RecipeStep(word);
        step[field] = null;
        Assert.Equal(path, Refused(WithRecipe(step)).JsonPath);
    }

    [Theory]
    [InlineData("combine")]
    [InlineData("apply")]
    public void A_recipe_step_needs_its_result_and_its_line_and_takes_no_stamp_or_place(string word)
    {
        var noResult = RecipeStep(word);
        noResult["expect"]!["item"] = null;
        Assert.Equal("$.steps[1].expect.item", Refused(WithRecipe(noResult)).JsonPath);

        var noLine = RecipeStep(word);
        noLine["expect"]!["reply"] = null;
        Assert.Equal("$.steps[1].expect.reply", Refused(WithRecipe(noLine)).JsonPath);

        var stamp = RecipeStep(word);
        stamp["expect"]!["stamp"] = "SomeStamp";
        Assert.Equal("$.steps[1].expect.stamp", Refused(WithRecipe(stamp)).JsonPath);

        var place = RecipeStep(word);
        place["place"] = new JsonObject { ["cell"] = "0xA9B40019", ["point"] = null, ["name"] = "here" };
        Assert.Equal("$.steps[1].place", Refused(WithRecipe(place)).JsonPath);
    }

    [Fact]
    public void An_apply_names_its_fail_line_and_a_fail_result_needs_one()
    {
        var apply = RecipeStep("apply");
        apply.Remove("fail_reply");
        Assert.Equal("$.steps[1].fail_reply", Refused(WithRecipe(apply)).JsonPath);

        var combine = RecipeStep("combine");
        combine["fail_item"] = new JsonObject { ["wcid"] = 6330, ["name"] = "Pyreal Ingot" };
        Assert.Equal("$.steps[1].fail_reply", Refused(WithRecipe(combine)).JsonPath);
    }

    [Theory]
    [InlineData("fail_reply")]
    [InlineData("fail_item")]
    public void The_fail_fields_are_refused_on_a_kind_that_does_not_use_them(string field)
    {
        var file = Pathwarden();
        file["steps"]![1]![field] = field == "fail_reply" ? "You fail." : new JsonObject { ["wcid"] = 6330, ["name"] = "Pyreal Ingot" };
        Assert.Equal($"$.steps[1].{field}", Refused(file).JsonPath);
    }

    [Fact]
    public void The_vocabulary_has_thirteen_words_all_implemented()
    {
        Assert.Equal(13, QuestStepKinds.Words.Count);
        Assert.All(QuestStepKinds.Words, w => Assert.Equal(w, QuestStepKinds.Word(QuestStepKinds.Parse(w)!.Value)));
        Assert.Equal(
            [QuestStepKind.Go, QuestStepKind.Talk, QuestStepKind.Use, QuestStepKind.Give, QuestStepKind.PickUp, QuestStepKind.HuntUntil, QuestStepKind.Kill, QuestStepKind.Buy, QuestStepKind.Combine, QuestStepKind.Apply, QuestStepKind.Recall, QuestStepKind.Confirm, QuestStepKind.Wear],
            QuestStepKinds.Implemented); // M6 5 Q1: item 5's six; M6 8 A: combine and apply
        Assert.True(QuestStepKinds.IsImplemented(QuestStepKind.Combine));
        Assert.True(QuestStepKinds.IsImplemented(QuestStepKind.Apply));
        var file = Pathwarden();
        file["steps"]![1]!["kind"] = "jump";
        Assert.Contains("not a word of the step vocabulary", Refused(file).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_without_its_kinds_fields_or_a_named_todo_fails()
    {
        var noItem = Pathwarden();
        noItem["steps"]![1]!["item"] = null;
        Assert.Equal("$.steps[1].item", Refused(noItem).JsonPath); // a give needs the item given

        var nullWcid = Pathwarden();
        nullWcid["steps"]![1]!["item"]!["wcid"] = null;
        Assert.Equal("$.steps[1].item.wcid", Refused(nullWcid).JsonPath); // an unknown wcid needs a todo

        var noCheck = Pathwarden();
        noCheck["steps"]![1]!["expect"] = new JsonObject { ["reply"] = null, ["stamp"] = null, ["item"] = null };
        Assert.Equal("$.steps[1].expect", Refused(noCheck).JsonPath); // a give needs a check

        var badTodo = Pathwarden();
        badTodo["steps"]![2]!["todo"] = "later";
        Assert.Equal("$.steps[2].todo", Refused(badTodo).JsonPath);

        var wrongKey = Pathwarden();
        Assert.Equal("$.key", Refused(wrongKey, "other.json").JsonPath);
    }

    [Fact]
    public void QuestTimers_round_trip_a_completion_through_the_store()
    {
        var store = new MemoryCheckpointStore();
        var timers = QuestTimers.Load(store, "Winnie");
        Assert.Empty(timers.Entries);
        var pathwarden = QuestCatalog.Get("pathwarden");
        Assert.True(timers.IsDue(pathwarden, At));
        timers.Complete(pathwarden, At, QuestTimers.HowItem);
        timers.Save(store);
        Assert.DoesNotContain('\r', store.Json!);

        var back = QuestTimers.Load(store, "Winnie");
        Assert.Equal(new QuestTimerEntry("pathwarden", At, null, 1, QuestTimers.HowItem), back.Get("pathwarden"));
        Assert.False(back.IsDue(pathwarden, At.AddYears(1))); // done once per character
        Assert.Equal("quests/winnie.json", QuestTimers.FileName("Winnie"));
        Assert.Contains("state=done-once", back.Line(pathwarden, At), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => QuestTimers.Load(store, "BSD Mage")); // another character's file
        Assert.Throws<InvalidDataException>(() => QuestTimers.Parse(store.Json!.Replace("\"how\"", "\"why\"", StringComparison.Ordinal), "Winnie"));
    }

    /// <summary>Long run 5 (2026-11-10): the file is named for the character's name, and a fresh climber read the last one's finished errand as her own.</summary>
    [Fact]
    public void QuestTimers_belong_to_one_guid_and_another_characters_records_are_dropped()
    {
        var pathwarden = QuestCatalog.Get("pathwarden");
        var store = new MemoryCheckpointStore();
        var first = new QuestTimers("Winnie");
        Assert.Equal(0u, first.CharacterGuid);
        Assert.Null(first.BindTo(0)); // an unknown guid changes nothing
        Assert.Null(first.BindTo(0x50000010)); // no record: the guid is taken silently
        Assert.Equal(0x50000010u, first.CharacterGuid);
        first.Complete(pathwarden, At, QuestTimers.HowItem);
        first.Save(store);
        Assert.Contains("\"schema\": 2", store.Json!, StringComparison.Ordinal);
        Assert.Contains("\"character_guid\": 1342177296", store.Json!, StringComparison.Ordinal);

        var same = QuestTimers.Load(store, "Winnie");
        Assert.Equal(0x50000010u, same.CharacterGuid);
        Assert.Null(same.BindTo(0x50000010)); // her own file
        Assert.False(same.IsDue(pathwarden, At.AddYears(1)));

        var recreated = QuestTimers.Load(store, "Winnie");
        var bind = recreated.BindTo(0x50000011);
        Assert.NotNull(bind);
        Assert.Equal((true, 0x50000010u, "pathwarden"), (bind.Dropped, bind.FileGuid, Assert.Single(bind.Entries).Key));
        Assert.Empty(recreated.Entries);
        Assert.Equal(0x50000011u, recreated.CharacterGuid);
        Assert.True(recreated.IsDue(pathwarden, At)); // never done on this character

        // A file written before the guid was kept (schema 1) names no character: its records are kept, and named.
        var old = "{\"schema\": 1, \"character\": \"Winnie\", \"quests\": [{\"key\": \"pathwarden\", \"last_completed\": \"2026-11-01T01:00:00Z\", \"next_due\": null, \"completions\": 1, \"how\": \"done-item\"}]}";
        var unowned = QuestTimers.Parse(old, "Winnie");
        Assert.Equal(0u, unowned.CharacterGuid);
        var kept = unowned.BindTo(0x50000011);
        Assert.NotNull(kept);
        Assert.Equal((false, 0u), (kept.Dropped, kept.FileGuid));
        Assert.Single(unowned.Entries);
        Assert.Equal(0x50000011u, unowned.CharacterGuid);

        Assert.Throws<InvalidDataException>(() => QuestTimers.Parse(old.Replace("\"schema\": 1", "\"schema\": 3", StringComparison.Ordinal), "Winnie"));
        Assert.Throws<InvalidDataException>(() => QuestTimers.Parse(store.Json!.Replace("1342177296", "-1", StringComparison.Ordinal), "Winnie"));
        Assert.Throws<InvalidDataException>(() => QuestTimers.Parse(old.Replace("\"schema\": 1,", "\"schema\": 1, \"character_guid\": 5,", StringComparison.Ordinal), "Winnie")); // schema 1 has no such field
    }

    [Fact]
    public void QuestTimers_compute_the_next_due_time_and_learn_a_held_stamp()
    {
        var daily = Daily();
        var timers = new QuestTimers("Winnie");
        var entry = timers.Complete(daily, At, QuestTimers.HowReply);
        Assert.Equal(At.AddHours(20), entry.NextDue);
        Assert.False(timers.IsDue(daily, At.AddHours(19.9)));
        Assert.True(timers.IsDue(daily, At.AddHours(20)));
        Assert.Contains("state=waiting", timers.Line(daily, At.AddHours(1)), StringComparison.Ordinal);
        Assert.Equal(2, timers.Complete(daily, At.AddHours(21), QuestTimers.HowReply).Completions);

        var fresh = new QuestTimers("Winnie");
        Assert.False(fresh.Observe(daily, new QuestResult("has", "PathwardenComplete", false, 0, false, 0), At));
        Assert.True(fresh.Observe(daily, new QuestResult("has", "PathwardenComplete", true, 1, true, 1), At));
        Assert.Equal((QuestTimers.HowServerStamp, At.AddHours(20)), (fresh.Get("daily")!.How, fresh.Get("daily")!.NextDue!.Value));
        Assert.False(fresh.Observe(daily, new QuestResult("has", "PathwardenComplete", true, 1, true, 1), At.AddHours(1))); // already recorded
        Assert.Throws<ArgumentException>(() => fresh.Observe(daily, new QuestResult("has", "PathwardenFound1111", true, 1, true, 1), At));
    }

    [Fact]
    public void The_Pathwarden_takes_the_robe_and_the_trinket_over_the_burden_line_and_gates_the_rest()
    {
        // M6 0 lead, live probe 3: a fresh climber stood at 164% before the chest; the quest's named reward is taken over the line, the other pieces stay gated.
        var carry = QuestCatalog.Get("pathwarden").Carry!;
        Assert.True(carry.TakesAlways(40439));
        Assert.True(carry.TakesAlways(41513));
        Assert.False(carry.TakesAlways(33605));
        Assert.False(carry.TakesAlways(33606));
        Assert.False(carry.Fits(2460, 1500, 60));
    }

    private static JsonObject Thing(uint wcid, string name) => new() { ["wcid"] = wcid, ["name"] = name };

    /// <summary>M6 5 Q1: a step of one of item 5's kinds with every field its kind needs (the values are test data, not a quest's).</summary>
    private static JsonObject NewStep(string kind)
    {
        var step = new JsonObject
        {
            ["kind"] = kind,
            ["what"] = "a test step",
            ["place"] = null,
            ["target"] = null,
            ["item"] = null,
            ["expect"] = new JsonObject { ["reply"] = null, ["stamp"] = null, ["item"] = null },
            ["source"] = "QuestProfileTests",
            ["todo"] = null,
        };
        switch (kind)
        {
            case "kill":
                step["target"] = Thing(4110, "Blood Shreth");
                step["count"] = 10;
                step["ground"] = "low-b-belt";
                step["expect"]!["reply"] = "Your task is complete!";
                break;
            case "hunt-until":
                step["item"] = Thing(22423, "Female Tusker Tusk");
                step["count"] = 1;
                step["ground"] = "tusker-burrow";
                step["target"] = Thing(22589, "Female Tusker");
                break;
            case "buy":
                step["target"] = Thing(7, "a vendor");
                step["item"] = Thing(8, "a ware");
                step["count"] = 5;
                break;
            case "recall":
                step["spell"] = 1635;
                step["place"] = new JsonObject { ["cell"] = "0xA9B40019", ["point"] = null, ["name"] = "Holtburg" };
                break;
            case "confirm":
                step["expect"]!["reply"] = "Do you wish";
                step["answer"] = "yes";
                break;
            case "wear":
                step["item"] = Thing(41513, "Pathwarden Trinket");
                break;
            default:
                throw new ArgumentException(kind);
        }

        return step;
    }

    /// <summary>The Pathwarden file with its step 1 replaced by <paramref name="step"/>.</summary>
    private static JsonObject WithStep(JsonObject step)
    {
        var file = Pathwarden();
        file["steps"]![1] = step;
        return file;
    }

    [Theory]
    [InlineData("kill", "step 1 kill 10 Blood Shreth (4110) on low-b-belt; expect reply \"Your task is complete!\"")]
    [InlineData("hunt-until", "step 1 hunt tusker-burrow until 1 Female Tusker Tusk (22423) arrived (from Female Tusker (22589)); expect nothing")]
    [InlineData("buy", "step 1 buy 5 a ware (8) from a vendor (7); expect nothing")]
    [InlineData("recall", "step 1 recall by spell 1635 to Holtburg (0xA9B40019); expect nothing")]
    [InlineData("confirm", "step 1 confirm yes; expect reply \"Do you wish\"")]
    [InlineData("wear", "step 1 wear Pathwarden Trinket (41513); expect nothing")]
    public void Each_item_5_kind_loads_with_its_fields(string word, string line)
    {
        var step = QuestCatalog.Parse("pathwarden.json", WithStep(NewStep(word)).ToJsonString()).Steps[1];
        Assert.Equal(word, QuestStepKinds.Word(step.Kind));
        Assert.Equal(line, step.ToString());
        switch (step.Kind)
        {
            case QuestStepKind.Kill:
                Assert.Equal((10, "low-b-belt", 4110u), (step.Count!.Value, step.Ground, step.Target!.Wcid!.Value));
                break;
            case QuestStepKind.HuntUntil:
                Assert.Equal((1, "tusker-burrow", 22423u), (step.Count!.Value, step.Ground, step.Item!.Wcid!.Value));
                break;
            case QuestStepKind.Buy:
                Assert.Equal(5, step.Count);
                break;
            case QuestStepKind.Recall:
                Assert.Equal((1635u, 0xA9B40019u), (step.Spell!.Value, step.Place!.Cell));
                break;
            case QuestStepKind.Confirm:
                Assert.True(step.Answer);
                break;
            case QuestStepKind.Wear:
                Assert.Null(step.Target);
                break;
        }

        var no = NewStep("confirm");
        no["answer"] = "no";
        Assert.False(QuestCatalog.Parse("pathwarden.json", WithStep(no).ToJsonString()).Steps[1].Answer);
    }

    [Theory]
    [InlineData("kill", "target", "$.steps[1].target")]
    [InlineData("kill", "count", "$.steps[1].count")]
    [InlineData("kill", "ground", "$.steps[1].ground")]
    [InlineData("kill", "expect.reply", "$.steps[1].expect.reply")]
    [InlineData("hunt-until", "item", "$.steps[1].item")]
    [InlineData("hunt-until", "count", "$.steps[1].count")]
    [InlineData("hunt-until", "ground", "$.steps[1].ground")]
    [InlineData("buy", "target", "$.steps[1].target")]
    [InlineData("buy", "item", "$.steps[1].item")]
    [InlineData("buy", "count", "$.steps[1].count")]
    [InlineData("recall", "spell", "$.steps[1].spell")]
    [InlineData("recall", "place", "$.steps[1].place")]
    [InlineData("confirm", "expect.reply", "$.steps[1].expect.reply")]
    [InlineData("confirm", "answer", "$.steps[1].answer")]
    [InlineData("wear", "item", "$.steps[1].item")]
    public void An_item_5_kind_without_its_field_fails_by_path(string word, string field, string path)
    {
        var step = NewStep(word);
        if (field == "expect.reply")
        {
            step["expect"]!["reply"] = null;
        }
        else if (field is "count" or "ground" or "spell" or "answer")
        {
            step.Remove(field); // the new fields are optional in the file: absent, not null
        }
        else
        {
            step[field] = null;
        }

        var e = Refused(WithStep(step));
        Assert.Equal(path, e.JsonPath);
        Assert.Contains($"a {word} step", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("kill", "item", "$.steps[1].item")]
    [InlineData("kill", "place", "$.steps[1].place")]
    [InlineData("hunt-until", "place", "$.steps[1].place")]
    [InlineData("buy", "place", "$.steps[1].place")]
    [InlineData("recall", "target", "$.steps[1].target")]
    [InlineData("recall", "item", "$.steps[1].item")]
    [InlineData("confirm", "target", "$.steps[1].target")]
    [InlineData("confirm", "item", "$.steps[1].item")]
    [InlineData("confirm", "place", "$.steps[1].place")]
    [InlineData("wear", "target", "$.steps[1].target")]
    [InlineData("wear", "place", "$.steps[1].place")]
    public void An_item_5_kind_with_a_field_it_does_not_use_fails_by_path(string word, string field, string path)
    {
        var step = NewStep(word);
        step[field] = field == "place" ? new JsonObject { ["cell"] = "0xA9B40019", ["point"] = null, ["name"] = "somewhere" } : Thing(9, "something");
        var e = Refused(WithStep(step));
        Assert.Equal(path, e.JsonPath);
        Assert.Contains("is set:", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "count", "talk")] // the Pathwarden's step 1 made a talk below; the item-0 kinds use none of the new fields
    [InlineData(1, "ground", "give")]
    [InlineData(0, "spell", "go")]
    [InlineData(2, "answer", "use")]
    [InlineData(3, "count", "pick-up")]
    public void A_new_field_on_a_kind_that_does_not_use_it_fails_by_path(int index, string field, string word)
    {
        var file = Pathwarden();
        var step = file["steps"]![index]!.AsObject();
        if (word == "talk")
        {
            step["kind"] = "talk";
            step["item"] = null;
        }

        step[field] = NewFieldValue(field);
        var e = Refused(file);
        Assert.Equal($"$.steps[{index}].{field}", e.JsonPath);
        Assert.Contains($"a {word} step has no {field}", e.Message, StringComparison.Ordinal);

        var other = NewStep("wear"); // and on an item-5 kind that does not use it
        other[field] = NewFieldValue(field);
        Assert.Equal($"$.steps[1].{field}", Refused(WithStep(other)).JsonPath);
    }

    private static JsonNode NewFieldValue(string field) => field switch
    {
        "count" => JsonValue.Create(3),
        "ground" => JsonValue.Create("tusker-burrow")!,
        "spell" => JsonValue.Create(1635),
        _ => JsonValue.Create("yes")!,
    };

    [Fact]
    public void The_new_step_fields_refuse_a_bad_value_by_path()
    {
        void Bad(string kind, string field, JsonNode? value, string expect)
        {
            var step = NewStep(kind);
            step[field] = value;
            var e = Refused(WithStep(step));
            Assert.Equal($"$.steps[1].{field}", e.JsonPath);
            Assert.Contains(expect, e.Message, StringComparison.Ordinal);
        }

        Bad("kill", "count", 0, "above 0");
        Bad("kill", "count", -3, "above 0");
        Bad("kill", "count", "10", "above 0");
        Bad("kill", "count", null, "above 0"); // present means a value: leave the field out instead
        Bad("kill", "ground", "no-such-ground", "not a ground profile key");
        Bad("hunt-until", "ground", 5, "not a string");
        Bad("recall", "spell", 0, "not a spell id");
        Bad("recall", "spell", "1635", "not a spell id");
        Bad("confirm", "answer", "maybe", "not \"yes\" or \"no\"");
        Bad("confirm", "answer", "Yes", "not \"yes\" or \"no\"");
        Bad("confirm", "answer", true, "not \"yes\" or \"no\"");

        var twice = WithStep(NewStep("kill")).ToJsonString().Replace("\"count\":10", "\"count\":10,\"count\":11", StringComparison.Ordinal);
        Assert.Equal("$.steps[1].count", Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("pathwarden.json", twice)).JsonPath);
    }

    [Fact]
    public void A_quest_level_and_experience_load_and_are_null_when_absent()
    {
        var file = Pathwarden();
        file["level"] = new JsonObject { ["min"] = 1, ["max"] = 40 };
        file["experience"] = new JsonObject { ["amount"] = 0, ["kind"] = "proportional", ["max"] = 120000, ["percent"] = 1.33, ["source"] = "San Ming's emote 51755 order 3 (facts.md section 3)" };
        var q = QuestCatalog.Parse("pathwarden.json", file.ToJsonString());
        Assert.Equal(new QuestLevel(1, 40), q.Level);
        Assert.Equal("1-40", q.Level!.ToString());
        Assert.True(q.Level.Fits(40));
        Assert.False(q.Level.Fits(41));
        Assert.Equal((0L, QuestExperienceKind.Proportional, 120000L, 1.33), (q.Experience!.Amount!.Value, q.Experience.Kind, q.Experience.Max!.Value, q.Experience.Percent!.Value));
        Assert.Equal(1330, q.Experience.At(1000)); // ACE GrantLevelProportionalXp: the share rounded
        Assert.Equal(120000, q.Experience.At(1_000_000)); // then capped
        Assert.Equal("proportional 133% max 120,000 min 0", q.Experience.ToString());

        var open = Pathwarden();
        open["level"] = new JsonObject { ["min"] = 40, ["max"] = null };
        open["experience"] = new JsonObject { ["amount"] = 20000000, ["kind"] = "fixed", ["max"] = null, ["percent"] = null, ["source"] = "Shoichi's emote 53992 order 1 (facts.md section 4)" };
        var o = QuestCatalog.Parse("pathwarden.json", open.ToJsonString());
        Assert.Equal("40+", o.Level!.ToString());
        Assert.True(o.Level.Fits(275));
        Assert.Equal(20_000_000, o.Experience!.At(1));
        Assert.Equal("fixed 20,000,000", o.Experience.ToString());

        var floored = new QuestExperience(500, QuestExperienceKind.Proportional, null, 0.1, "test");
        Assert.Equal(500, floored.At(1000)); // floored by a min above 0, no cap
        Assert.Equal(1000, floored.At(10000));

        var none = Pathwarden();
        none["level"] = null;
        none["experience"] = null;
        var n = QuestCatalog.Parse("pathwarden.json", none.ToJsonString());
        Assert.Null(n.Level);
        Assert.Null(n.Experience);
    }

    [Fact]
    public void A_quest_ladder_object_loads_and_is_null_when_absent()
    {
        var file = Pathwarden();
        file["ladder"] = new JsonObject { ["from_level"] = 1, ["before_level"] = 21, ["once"] = true };
        var q = QuestCatalog.Parse("pathwarden.json", file.ToJsonString());
        Assert.Equal(new QuestLadder(1, 21, true), q.Ladder);
        Assert.Equal("from 1 before 21 once", q.Ladder!.ToString());
        Assert.True(q.Ladder.Fits(20));
        Assert.False(q.Ladder.Fits(21));
        Assert.True(q.Ladder.Passed(21));
        Assert.False(q.Ladder.Passed(20));

        Assert.Null(QuestCatalog.Parse("pathwarden.json", Pathwarden().ToJsonString()).Ladder);
        Assert.All(QuestCatalog.All, s => Assert.True(s.Ladder is null || s.Ladder.BeforeLevel > s.Ladder.FromLevel, s.Key));
    }

    [Fact]
    public void A_quest_ladder_object_is_read_strictly()
    {
        void Bad(JsonNode value, string path)
        {
            var file = Pathwarden();
            file["ladder"] = value;
            Assert.Equal(path, Refused(file).JsonPath);
        }

        Bad(1, "$.ladder");
        Bad(new JsonObject { ["from_level"] = 0, ["before_level"] = 21, ["once"] = true }, "$.ladder.from_level");
        Bad(new JsonObject { ["from_level"] = 21, ["before_level"] = 21, ["once"] = true }, "$.ladder.before_level"); // equal is refused: the band would be empty
        Bad(new JsonObject { ["from_level"] = 21, ["before_level"] = 20, ["once"] = true }, "$.ladder.before_level");
        Bad(new JsonObject { ["from_level"] = 1, ["before_level"] = 21 }, "$.ladder.once");
        Bad(new JsonObject { ["from_level"] = 1, ["before_level"] = 21, ["once"] = "yes" }, "$.ladder.once");
        Bad(new JsonObject { ["from_level"] = 1, ["before_level"] = 21, ["once"] = true, ["band"] = 3 }, "$.ladder.band");
    }

    [Fact]
    public void A_quest_level_and_experience_are_read_strictly()
    {
        void Bad(string field, JsonNode value, string path)
        {
            var file = Pathwarden();
            file[field] = value;
            Assert.Equal(path, Refused(file).JsonPath);
        }

        Bad("level", 1, "$.level");
        Bad("level", new JsonObject { ["min"] = 0, ["max"] = null }, "$.level.min");
        Bad("level", new JsonObject { ["min"] = 20, ["max"] = 10 }, "$.level.max");
        Bad("level", new JsonObject { ["min"] = 1 }, "$.level.max"); // every field of the object, max null for no top
        Bad("level", new JsonObject { ["min"] = 1, ["max"] = null, ["band"] = 3 }, "$.level.band");

        static JsonObject Xp(string kind, long? amount, long? max, double? percent) =>
            new() { ["amount"] = amount, ["kind"] = kind, ["max"] = max, ["percent"] = percent, ["source"] = "a row" };

        Bad("experience", Xp("bonus", 5, null, null), "$.experience.kind");
        Bad("experience", Xp("fixed", null, null, null), "$.experience.amount");
        Bad("experience", Xp("fixed", 0, null, null), "$.experience.amount");
        Bad("experience", Xp("fixed", 5, 10, null), "$.experience.max");
        Bad("experience", Xp("fixed", 5, null, 1.0), "$.experience.percent");
        Bad("experience", Xp("proportional", 0, 100, null), "$.experience.percent");
        Bad("experience", Xp("proportional", 0, 100, -1), "$.experience.percent");
        Bad("experience", Xp("proportional", -1, 100, 1.33), "$.experience.amount");
        Bad("experience", Xp("proportional", 0, 0, 1.33), "$.experience.max");
        var noSource = Xp("fixed", 5, null, null);
        noSource.Remove("source");
        Bad("experience", noSource, "$.experience.source");
    }
}
