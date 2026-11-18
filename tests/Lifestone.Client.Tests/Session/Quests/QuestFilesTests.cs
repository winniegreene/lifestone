using System.Text.Json.Nodes;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 item 5 Q6a: the shape of the two kill-task files and the ten tusk files (M6 14 armory: the Armored Tusker Tusk the tenth) through the loader (the values against the world SQL are <c>QuestCatalogSqlTests</c>, exhaustive): the steps in order, the grounds the profiles know, the 20-hour
/// timers, the awards, the level bands and the todos the files name.
/// </summary>
public sealed class QuestFilesTests
{
    private static readonly (string Key, uint Tusk, uint Carrier, string Ground, long Xp, int MinLevel)[] Tusks =
    [
        ("female", 22423, 22589, "tusker-burrow", 100_000, 1), ("male", 22427, 22597, "tusker-lodge", 200_000, 5), ("crimsonback", 22421, 22585, "tusker-cave", 350_000, 12),
        ("goldenback", 22424, 22591, "tusker-cavern", 500_000, 15), ("redeemer", 22430, 22601, "tusker-abode", 750_000, 20), ("liberator", 22426, 22595, "tusker-habitat", 850_000, 25),
        ("slave", 22432, 22605, "tusker-quarters", 1_000_000, 30), ("guard", 22425, 22593, "tusker-barracks", 2_500_000, 40), ("rampager", 22429, 22599, "tusker-holding", 8_000_000, 60), ("armored", 22419, 22611, "tusker-armory", 3_500_000, 50), // M6 14 armory: the Armored Tusker Tusk
    ];

    [Fact]
    public void The_Blood_Shreth_task_goes_talks_kills_ten_goes_back_and_turns_in_for_the_plaque()
    {
        var q = QuestCatalog.Get("blood-shreth-kill-task");
        Assert.Equal(31641u, q.Giver!.Wcid);
        Assert.Equal(["go", "talk", "kill", "go", "talk"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
        var kill = q.Steps[2];
        Assert.Equal((4110u, 10, "cave-of-alabree", "Your task is complete!"), (kill.Target!.Wcid!.Value, kill.Count!.Value, kill.Ground, kill.Expect.Reply));
        Assert.Equal((0xDA55001Du, 31641u), (q.Steps[0].Place!.Cell, q.Steps[0].Target!.Wcid!.Value));
        Assert.Equal(q.Steps[0].Place, q.Steps[3].Place);
        Assert.Equal(31617u, q.Steps[4].Expect.Item!.Wcid);
        Assert.Equal(("Congratulations! You are now truly a Blood Shreth Butcher!", 31617u), (q.Done.Reply, q.Done.Item!.Wcid!.Value));
        Assert.Equal(72000, q.TimerSeconds);
        Assert.Equal(new QuestLevel(1, null), q.Level);
        Assert.Equal((QuestExperienceKind.Proportional, 120000L, 1.33), (q.Experience!.Kind, q.Experience.Max!.Value, q.Experience.Percent!.Value));
        Assert.DoesNotContain(q.Todos, t => t.StartsWith("TODO(m6-lin", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Tusker_Guard_task_is_500_kills_at_the_Barracks_for_20_million_and_carries_the_Lin_walk_todo()
    {
        var q = QuestCatalog.Get("tusker-guard-kill-task");
        Assert.Equal(33968u, q.Giver!.Wcid);
        Assert.Equal(["go", "talk", "kill", "go", "talk"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
        Assert.Equal((1629u, 500, "tusker-barracks"), (q.Steps[2].Target!.Wcid!.Value, q.Steps[2].Count!.Value, q.Steps[2].Ground));
        Assert.Equal(new QuestExperience(20_000_000, QuestExperienceKind.Fixed, null, null, q.Experience!.Source), q.Experience);
        Assert.Equal(72000, q.TimerSeconds);
        Assert.Equal(0xDA3B0116u, q.Steps[0].Place!.Cell);
        Assert.Empty(q.Todos); // M6 14 guard task: the walk to Shoichi and back is proved on the dats (TownNetworkWalkDatTests), so the go steps carry no TODO(m6-lin-shoichi-walk)
        Assert.All(q.Steps, s => Assert.Null(s.Todo));
        Assert.Null(q.Done.Item);
        Assert.NotNull(GroundCatalog.TryGet(q.Steps[2].Ground!));
    }

    [Fact]
    public void Each_tusk_file_hunts_the_tusk_at_its_ground_walks_to_Brighteyes_and_gives_it_for_the_gem()
    {
        foreach (var (key, tusk, carrier, ground, xp, minLevel) in Tusks)
        {
            var q = QuestCatalog.Get("tusk-" + key);
            Assert.Equal(22642u, q.Giver!.Wcid);
            Assert.Equal(["hunt-until", "go", "give"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
            var hunt = q.Steps[0];
            Assert.Equal((carrier, tusk, 1, ground), (hunt.Target!.Wcid!.Value, hunt.Item!.Wcid!.Value, hunt.Count!.Value, hunt.Ground));
            Assert.Contains(GroundCatalog.Get(ground).Targets, t => t.Wcid == carrier);
            Assert.Equal((0xF682011Cu, 22642u), (q.Steps[1].Place!.Cell, q.Steps[1].Target!.Wcid!.Value));
            var give = q.Steps[2];
            Assert.Equal((tusk, 22642u, 22729u), (give.Item!.Wcid!.Value, give.Target!.Wcid!.Value, give.Expect.Item!.Wcid!.Value));
            Assert.Equal(22729u, q.Done.Item!.Wcid);
            Assert.Equal(72000, q.TimerSeconds);
            Assert.Equal((QuestExperienceKind.Fixed, xp), (q.Experience!.Kind, q.Experience.Amount!.Value));
            Assert.Equal(minLevel, q.Level!.Min);
            Assert.Null(q.Level.Max);
            Assert.Contains(q.Sources, s => s.Contains("rung change's fallback", StringComparison.Ordinal));
            Assert.Empty(q.Todos);
        }
    }

    [Fact]
    public void The_item_zero_files_no_longer_carry_the_closed_todos()
    {
        Assert.DoesNotContain(QuestCatalog.Get("pathwarden").Todos, t => t.Contains("m6-pathwarden-chest-open-check", StringComparison.Ordinal));
        Assert.DoesNotContain(QuestCatalog.Get("academy").Todos, t => t.Contains("m6-academy-skip", StringComparison.Ordinal) || t.Contains("m6-academy-sentry-gift", StringComparison.Ordinal));
    }

    [Fact]
    public void The_kill_task_files_name_the_in_progress_and_the_wait_replies_on_their_talk_step()
    {
        var shreth = QuestCatalog.Get("blood-shreth-kill-task").Steps[1];
        Assert.Equal(("I see you haven't completed your task.", "Ah, friend, it is good to see you back again!"), (shreth.ArmedReply, shreth.WaitReply)); // facts.md section 3: sets 51756 and 51754
        var guard = QuestCatalog.Get("tusker-guard-kill-task").Steps[1];
        Assert.Equal(("out of 500 Tusker Guards", "Part of our training is knowing when to stop and reflect"), (guard.ArmedReply, guard.WaitReply)); // facts.md section 4: the in-progress broadcast, set 53990
        Assert.Null(QuestCatalog.Get("blood-shreth-kill-task").Steps[2].ArmedReply);
    }

    [Theory]
    [InlineData("armed_reply")]
    [InlineData("wait_reply")]
    public void An_armed_or_wait_reply_is_refused_on_a_step_that_is_not_a_talk(string field)
    {
        var file = JsonNode.Parse(QuestCatalog.ResourceText("blood-shreth-kill-task.json")!)!.AsObject();
        file["steps"]![0]![field] = "anything";

        var refused = Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("blood-shreth-kill-task.json", file.ToJsonString()));

        Assert.Equal("$.steps[0]." + field, refused.JsonPath);
    }
}
