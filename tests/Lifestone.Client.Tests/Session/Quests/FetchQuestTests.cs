using System.Text.Json.Nodes;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 8 B2a (D118): the shape of a fetch quest, on the three files: from Holtburg to the item, a go directly before each pick-up and each portal, the last give's reply the done, and back at Holtburg at the end (below level 16 she has no Lifestone Recall). The bands the loader reads, and the named stops of the two files that do not walk.
/// </summary>
public sealed class FetchQuestTests
{
    private const ushort Holtburg = 0xA9B4;

    private static readonly string[] Keys = ["worcer-heirlooms", "nen-ai-pet-drudge", "four-stones-of-jojii", "lubziklan-stolen-goods"];

    public static IEnumerable<object[]> All() => Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(All))]
    public void A_fetch_starts_and_ends_at_holtburg_and_is_due_within_the_portals_levels(string key)
    {
        var q = QuestCatalog.Get(key);
        Assert.Equal(Holtburg, (ushort)(q.Start.Cell >> 16));
        Assert.Equal(QuestStepKind.Go, q.Steps[^1].Kind);
        Assert.Equal(Holtburg, (ushort)(q.Steps[^1].Place!.Cell >> 16));
        Assert.Equal(new QuestLevel(1, 20), q.Level);
        Assert.Equal(72000, q.TimerSeconds);
    }

    /// <summary>M6 8 (the long round): the stair comes down at y 18.5 and the Stone Cathedral portal stands at y 26.32 (world SQL, guid 0x7D854002). A stop past it means walking through it, and the touch takes her in a step early.</summary>
    [Fact]
    public void The_go_before_the_cathedral_portal_stops_on_the_stairs_side_of_it()
    {
        const float portalY = 26.3172f;
        var q = QuestCatalog.Get("nen-ai-pet-drudge");
        var use = q.Steps.First(s => s.Kind == QuestStepKind.Use && s.Target?.Wcid == 4955);
        var stop = q.Steps[use.Index - 1].Place!.Point!.Value;
        var foot = q.Steps[use.Index - 2].Place!.Point!.Value;

        Assert.True(foot.Y < stop.Y && stop.Y < portalY, $"the stair's foot at y {foot.Y}, the stop at y {stop.Y}, the portal at y {portalY}");
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_pick_up_has_its_go_directly_before_it_and_every_portal_too(string key)
    {
        var q = QuestCatalog.Get(key);
        Assert.Contains(q.Steps, s => s.Kind == QuestStepKind.PickUp);
        foreach (var s in q.Steps.Where(s => s.Kind == QuestStepKind.PickUp))
        {
            var before = q.Steps[s.Index - 1];
            Assert.Equal(QuestStepKind.Go, before.Kind);
            Assert.Equal((s.Target ?? s.Item)!.Wcid, before.Target!.Wcid);
        }

        foreach (var s in q.Steps.Where(s => s.Kind == QuestStepKind.Use))
        {
            Assert.NotNull(s.Place);
            Assert.Equal(QuestStepKind.Go, q.Steps[s.Index - 1].Kind);
            Assert.Null(q.Steps[s.Index - 1].Target); // the go stops at the front, short of the portal's touch: the touch is the use step's
        }
    }

    [Theory]
    [MemberData(nameof(All))]
    public void The_gives_are_to_the_giver_after_the_last_portal_and_the_last_ones_reply_is_the_done(string key)
    {
        var q = QuestCatalog.Get(key);
        var gives = q.Steps.Where(s => s.Kind == QuestStepKind.Give).ToList();
        Assert.Equal(q.Steps.Count(s => s.Kind == QuestStepKind.PickUp), gives.Count);
        Assert.All(gives, g => Assert.Equal(q.Giver!.Wcid, g.Target!.Wcid));
        Assert.All(gives, g => Assert.NotNull(g.Expect.Reply));
        Assert.True(gives[0].Index > q.Steps.Last(s => s.Kind == QuestStepKind.Use).Index);
        Assert.Equal(q.Giver!.Wcid, q.Steps[gives[0].Index - 1].Target!.Wcid);
        Assert.Equal(gives[^1].Expect.Reply, q.Done.Reply);
        Assert.Equal(q.Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!.Wcid).Order(), gives.Select(g => g.Item!.Wcid).Order());
    }

    [Fact]
    public void Worcer_is_on_the_ladder_with_its_band_and_no_open_todo_and_a_way_out_of_the_redoubt()
    {
        var q = QuestCatalog.Get("worcer-heirlooms");
        Assert.Equal(new QuestLadder(10, 18, true), q.Ladder);
        Assert.Empty(q.Todos);
        var way = TravelRoutes.Default.WayHomeFrom("0x0163", 5);
        Assert.Equal(TravelOutcome.Admitted, way.Outcome);
        Assert.Equal(TravelRoutes.Default.StartPlace, way.WayHome!.Steps[^1].Edge.To);
    }

    /// <summary>M6 8 B2c: Nen Ai's and the Stolen Goods' bands, no open todo, and a way out of each dungeon for a level with no Lifestone Recall.</summary>
    [Theory]
    [InlineData("nen-ai-pet-drudge", 11, 18, "0x0154")]
    [InlineData("lubziklan-stolen-goods", 12, 18, "0x0156")]
    public void The_two_b2c_fetches_are_on_the_ladder_with_no_open_todo_and_a_way_out_of_their_dungeon(string key, int from, int before, string landblock)
    {
        var q = QuestCatalog.Get(key);
        Assert.Equal(new QuestLadder(from, before, true), q.Ladder);
        Assert.Empty(q.Todos);
        var way = TravelRoutes.Default.WayHomeFrom(landblock, from);
        Assert.Equal(TravelOutcome.Admitted, way.Outcome);
        Assert.Equal(TravelRoutes.Default.StartPlace, way.WayHome!.Steps[^1].Edge.To);
    }

    /// <summary>M6 8 B2c: a go step's authored way loads with its points, the last one the step's place, and the loader refuses one that does not end there or stands on another kind.</summary>
    [Fact]
    public void A_way_is_a_go_steps_and_ends_at_its_place()
    {
        var q = QuestCatalog.Get("lubziklan-stolen-goods");
        var step = q.Steps.First(s => s.Way is not null);
        Assert.Equal(QuestStepKind.Go, step.Kind);
        Assert.Equal(step.Place!.Cell, step.Way![^1].Cell);
        Assert.Equal(step.Place.Point, step.Way[^1].Point);
        var file = JsonNode.Parse(QuestCatalog.ResourceText("lubziklan-stolen-goods.json")!)!.AsObject();
        var way = file["steps"]![step.Index]!["way"]!.AsArray();
        way[way.Count - 1]!["point"]![0] = 170f; // the last point no longer the step's place
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("lubziklan-stolen-goods.json", file.ToJsonString()));
        file = JsonNode.Parse(QuestCatalog.ResourceText("lubziklan-stolen-goods.json")!)!.AsObject();
        file["steps"]![2]!.AsObject().Add("way", file["steps"]![step.Index]!["way"]!.DeepClone()); // a use step has no way
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("lubziklan-stolen-goods.json", file.ToJsonString()));
    }

    [Theory]
    [InlineData("four-stones-of-jojii", "TODO(m6-fetch-tower-stairs)", "TODO(m6-fetch-hive-drop)")]
    public void A_fetch_that_does_not_walk_is_off_the_ladder_with_its_named_todos(string key, string first, string second)
    {
        var q = QuestCatalog.Get(key);
        Assert.Null(q.Ladder);
        Assert.Contains(q.Todos, t => t.StartsWith(first, StringComparison.Ordinal));
        Assert.Contains(q.Todos, t => t.StartsWith(second, StringComparison.Ordinal));
    }

    /// <summary>M6 8 B2b: the Minor Smoldering Stone: nobody starts it, the go carries the frost TODO, the 1-hour pick-up timer, a go directly before the pick-up.</summary>
    [Fact]
    public void The_minor_smoldering_stone_is_a_two_step_errand_with_its_gouts_named_and_its_hour()
    {
        var q = QuestCatalog.Get("minor-smoldering-stone");
        Assert.Equal(new QuestLadder(75, 126, true), q.Ladder);
        Assert.Null(q.Giver);
        Assert.Null(q.Level);
        Assert.Equal(3600, q.TimerSeconds);
        Assert.Equal(6320u, q.Done.Item!.Wcid);
        Assert.Equal(["go", "pick-up"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
        Assert.Equal((0xA66C0100u, 6320u), (q.Steps[0].Place!.Cell, q.Steps[0].Target!.Wcid!.Value));
        Assert.Null(q.Steps[0].Todo); // M6 14 the element line: her fight-back casts the line that hits a Gout hardest (BoltElementTests)
        Assert.Contains("ResistCold 0.65", q.Steps[0].Source, StringComparison.Ordinal);
        Assert.Equal(q.Steps[0].Place, QuestCatalog.Get("atlan-wand").Steps.Single(s => s.Kind == QuestStepKind.Go && s.Target?.Wcid == 6320).Place); // one place, in the chain too
    }

    /// <summary>M6 villa (writ-caves.md): the three other minor stones are two-step errands like the Smoldering one: nobody starts them, the hour's pick-up timer, off the ladder; the two runs no walk establishes name their TODO on the go.</summary>
    [Theory]
    [InlineData("minor-shivering-stone", 6321u, 0xBDB40102u, "TODO(villa-shivering-igloo-walk)")]
    [InlineData("minor-stinging-stone", 6319u, 0xBA600100u, "TODO(villa-blackmire-portal-ground)")]
    [InlineData("minor-sparking-stone", 6318u, 0xDD340100u, null)]
    public void The_minor_stones_for_the_writs_are_two_step_errands_with_their_hour(string key, uint stone, uint cell, string? todo)
    {
        var q = QuestCatalog.Get(key);
        Assert.Null(q.Ladder);
        Assert.Null(q.Giver);
        Assert.Null(q.Level);
        Assert.Null(q.Experience);
        Assert.Equal(3600, q.TimerSeconds);
        Assert.Equal(stone, q.Done.Item!.Wcid);
        Assert.Equal(["go", "pick-up"], q.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
        Assert.Equal((cell, stone, stone), (q.Steps[0].Place!.Cell, q.Steps[0].Target!.Wcid!.Value, q.Steps[1].Item!.Wcid!.Value));
        if (todo is null)
        {
            Assert.Null(q.Steps[0].Todo);
        }
        else
        {
            Assert.StartsWith(todo, q.Steps[0].Todo, StringComparison.Ordinal);
        }
    }

    /// <summary>M6 8 B2b (facts-fetch2.md section 5): Burun History is three reports only, the two dungeons before the Tunnels; each given to Ydnew for its gift; home by the recall, then the road to Yanshi carries its name.</summary>
    [Fact]
    public void Burun_is_three_reports_only_with_its_band_and_its_named_roads()
    {
        var q = QuestCatalog.Get("burun-history");
        Assert.Equal(new QuestLadder(78, 126, true), q.Ladder);
        Assert.Equal(new QuestLevel(40, null), q.Level);
        Assert.Equal(72000, q.TimerSeconds);
        Assert.Equal(28094u, q.Giver!.Wcid);
        uint[] reports = [28120, 28121, 28122];
        Assert.Equal(reports, q.Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!.Wcid!.Value));
        Assert.Equal(reports, q.Steps.Where(s => s.Kind == QuestStepKind.Give).Select(s => s.Item!.Wcid!.Value));
        Assert.Equal([28068u, 28069u, 28070u], q.Steps.Where(s => s.Kind == QuestStepKind.Give).Select(s => s.Expect.Item!.Wcid!.Value));
        Assert.All(q.Steps.Where(s => s.Kind == QuestStepKind.Give), g => Assert.Equal(28094u, g.Target!.Wcid));
        Assert.Equal(28070u, q.Done.Item!.Wcid);
        var recall = Assert.Single(q.Steps, s => s.Kind == QuestStepKind.Recall);
        Assert.Equal(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell, recall.Spell);
        Assert.Equal(new QuestExperience(null, QuestExperienceKind.Proportional, 4_500_000, 0.175, q.Experience!.Source), q.Experience);
        Assert.Contains(q.Todos, t => t.StartsWith("TODO(m6-yanshi-route)", StringComparison.Ordinal));
        Assert.Equal(2, q.Steps.Count(s => s.Todo?.StartsWith("TODO(m6-burun-surface-walk)", StringComparison.Ordinal) == true));
    }

    /// <summary>M6 8 B2b (D118): the two fetches that stay offline load, have no ladder, carry the level of their portals and name what no walk established.</summary>
    [Theory]
    [InlineData("repaired-shadow-stone", 125, 32589u, 19713u, 161_000_000L)]
    [InlineData("shield-of-yanshi", 100, 32554u, 32510u, 58_000_000L)]
    public void The_offline_fetches_load_off_the_ladder_at_their_portals_level_and_name_their_walks(string key, int level, uint lastItem, uint giver, long xp)
    {
        var q = QuestCatalog.Get(key);
        Assert.Null(q.Ladder);
        Assert.Equal(new QuestLevel(level, null), q.Level);
        Assert.Equal(giver, q.Giver!.Wcid);
        Assert.Equal(lastItem, q.Steps[^1].Item!.Wcid);
        Assert.Equal(QuestStepKind.Give, q.Steps[^1].Kind);
        Assert.Equal((QuestExperienceKind.Fixed, xp), (q.Experience!.Kind, q.Experience.Amount!.Value));
        Assert.Contains(q.Todos, t => t.StartsWith("TODO(m6-offline-fetch-walks)", StringComparison.Ordinal));
        Assert.Contains(q.Steps, s => s.Kind == QuestStepKind.Use && s.Place is not null);
    }

    [Fact]
    public void The_repository_cliff_climb_is_a_named_todo_not_a_walk()
    {
        var climb = QuestCatalog.Get("repaired-shadow-stone").Steps[0];
        Assert.Equal(QuestStepKind.Go, climb.Kind);
        Assert.StartsWith("TODO(m6-repository-cliff-climb)", climb.Todo, StringComparison.Ordinal);
        Assert.Equal(0x0A0F0027u, climb.Place!.Cell);
    }
}
