using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 8 B2b (D118, D34, DoD 10): the Atlan wand's chain as its file: the steps in order, the six recipe rows against the file (the rows themselves against the pinned zip are <c>QuestCatalogSqlTests</c>, exhaustive), the counts that sum to 16 motes, the named stops of the one check with a roll, and its skill and difficulty (M6 14: the two TODOs that gated it are closed). No live proof here: that is items 10 and 11's.
/// </summary>
public sealed class AtlanChainTests
{
    private const uint Mote = 6353, Sliver = 6355, Nugget = 6354, Bar = 6329, QualityIngot = 6331, Dross = 6330, Glyph = 19410, Infused = 46141, Wand = 46122, Stone = 6320, StonedWand = 46128;

    /// <summary>The recipe rows (facts-fetch.md section 6; the lead's read-only SELECT on the live world database, cache/m6/8/notes.md): source, target, result, the two lines, the fail result, the count of uses.</summary>
    public static TheoryData<int, uint, uint, uint, string, string?, uint?, int> Recipes => new()
    {
        { 790, Mote, Mote, Sliver, "You combine the two motes into a larger sliver.", null, null, 8 },
        { 792, Sliver, Sliver, Nugget, "You combine the two slivers into a larger nugget.", null, null, 4 },
        { 791, Nugget, Nugget, Bar, "You combine the two nuggets into a larger bar.", null, null, 2 },
        { 789, Bar, Bar, QualityIngot, "You combine the two bars into an ingot of high quality.", "You combine the two bars into an ingot.", Dross, 1 },
        { 6027, Glyph, QualityIngot, Infused, "You strongly imbue the pyreal with the essence of war magic.", "You strongly imbue the pyreal with the essence of a wand.", Infused, 1 },
        { 6068, Stone, Wand, StonedWand, "The gem slips into the weapon altering its properties.", "The gem slips into the weapon altering its properties.", StonedWand, 1 },
    };

    private static Quest File => QuestCatalog.Get("atlan-wand");

    private static QuestStep Recipe(uint source) => File.Steps.Single(s => s.Kind is QuestStepKind.Combine or QuestStepKind.Apply && s.Item!.Wcid == source);

    [Fact]
    public void The_steps_run_in_the_chains_order()
    {
        string[] words =
        [
            "go", "go", "go", "use", "hunt-until", "recall", "combine", "combine", "combine", "combine", "go",
            "go", "go", "go", "go", "go", "go", "go", "buy", "apply", "give",
            "recall", "go", "pick-up", "apply", "wear",
        ];
        Assert.Equal(words, File.Steps.Select(s => QuestStepKinds.Word(s.Kind)));
    }

    [Fact]
    public void The_file_is_the_climbs_once_for_a_mage_of_base_war_290_and_is_done_when_the_stoned_wand_arrives()
    {
        var q = File;
        Assert.Null(q.Giver);
        Assert.Equal(new QuestLadder(75, 126, true), q.Ladder);
        Assert.Null(q.TimerSeconds);
        Assert.Null(q.Level);
        Assert.Equal(StonedWand, q.Done.Item!.Wcid);
        Assert.Equal(StonedWand, q.Steps[^1].Item!.Wcid);
        Assert.Equal(QuestStepKind.Wear, q.Steps[^1].Kind);
    }

    [Theory]
    [MemberData(nameof(Recipes))]
    public void Every_recipe_step_is_its_row(int recipe, uint source, uint target, uint result, string success, string? fail, uint? failItem, int count)
    {
        var s = Recipe(source);
        Assert.Equal((target, result, success, count), (s.Target!.Wcid!.Value, s.Expect.Item!.Wcid!.Value, s.Expect.Reply!, s.Count ?? 1));
        Assert.Equal(fail, s.FailReply);
        Assert.Equal(failItem, s.FailItem?.Wcid);
        Assert.Equal(recipe is 6027 or 6068 ? QuestStepKind.Apply : QuestStepKind.Combine, s.Kind);
        Assert.Contains($"recipe row {recipe}", s.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_counts_sum_from_sixteen_motes_to_one_ingot()
    {
        var hunt = File.Steps.Single(s => s.Kind == QuestStepKind.HuntUntil);
        Assert.Equal((Mote, 16, "crater-caves"), (hunt.Item!.Wcid!.Value, hunt.Count!.Value, hunt.Ground!));
        var counts = new[] { Mote, Sliver, Nugget, Bar }.Select(w => Recipe(w).Count!.Value).ToArray();
        Assert.Equal(new[] { 8, 4, 2, 1 }, counts);
        Assert.Equal(16, counts[0] * 2);
        Assert.Equal(new[] { counts[0], counts[1], counts[2] }, new[] { counts[1] * 2, counts[2] * 2, counts[3] * 2 });
        var order = new[] { Mote, Sliver, Nugget, Bar, Glyph, Stone }.Select(w => Recipe(w).Index).ToArray();
        Assert.Equal(order.Order(), order);
        Assert.True(hunt.Index < order[0]);
    }

    [Fact]
    public void Silencias_tower_is_seven_flights_up_with_the_buy_the_glyph_and_the_golem_on_the_roof_and_a_recall_down()
    {
        var q = File;
        var buy = q.Steps.Single(s => s.Kind == QuestStepKind.Buy);
        Assert.Equal((2498u, Glyph, 1), (buy.Target!.Wcid!.Value, buy.Item!.Wcid!.Value, buy.Count!.Value));
        var flights = q.Steps.Where(s => s.Kind == QuestStepKind.Go && s.Place!.Cell == 0x90D00100).ToList();
        Assert.Equal(7, flights.Count);
        Assert.Equal(Enumerable.Range(buy.Index - 7, 7), flights.Select(s => s.Index));
        Assert.Equal(new System.Numerics.Vector3(58.2f, 110.8f, 297.2f), flights[^1].Place!.Point);
        var give = q.Steps.Single(s => s.Kind == QuestStepKind.Give);
        Assert.Equal((6356u, Infused, Wand), (give.Target!.Wcid!.Value, give.Item!.Wcid!.Value, give.Expect.Item!.Wcid!.Value));
        Assert.Null(give.Expect.Reply); // the golem's emote 40757 says nothing: the wand arriving is the check
        Assert.Equal(Recipe(Glyph).Index + 1, give.Index);
        Assert.Equal(Recipe(Stone).Index - 1, q.Steps.Single(s => s.Kind == QuestStepKind.PickUp).Index);
        var recalls = q.Steps.Where(s => s.Kind == QuestStepKind.Recall).ToList();
        Assert.Equal([q.Steps.Single(s => s.Kind == QuestStepKind.HuntUntil).Index + 1, give.Index + 1], recalls.Select(s => s.Index));
        Assert.All(recalls, r => Assert.Equal(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell, r.Spell));
    }

    [Fact]
    public void The_one_check_with_a_roll_names_its_skill_and_the_motes_wait_on_no_todo()
    {
        var bars = Recipe(Bar);
        Assert.Null(bars.Todo); // M6 14 (D132, D136): Alchemy is trained on the job's need and raised before the roll (AtlanWandReadyTests)
        Assert.Equal((38u, 150u), (bars.RecipeSkill!.Value, bars.RecipeDifficulty!.Value));
        Assert.Null(File.Steps.Single(s => s.Kind == QuestStepKind.HuntUntil).Todo); // M6 14 the element line: the fight casts the line that hits each target hardest (BoltElementTests)
        Assert.Null(File.Steps.Single(s => s.Kind == QuestStepKind.Go && s.Target?.Wcid == Stone).Todo);
        Assert.Empty(File.Todos);
    }

    private static QuestRig BarRig()
    {
        var rig = new QuestRig();
        rig.PackItem(0x80000701, Bar, "Pyreal Bar");
        rig.PackItem(0x80000702, Bar, "Pyreal Bar");
        return rig;
    }

    private static Quest BarStepOnly() => QuestRig.Quest("test-ingot", new QuestDone(null, null, new QuestThing(QualityIngot, "Quality Pyreal Ingot")), null, Recipe(Bar) with { Index = 0 });

    /// <summary>D34: with no grant made the server refuses an untrained Alchemy (0x043B, nothing consumed), and the chain stops there by name, the two bars kept.</summary>
    [Fact]
    public void An_untrained_alchemy_stops_the_chain_by_name_and_keeps_the_bars()
    {
        var rig = BarRig();
        rig.OnRecipe = (_, _) =>
        {
            rig.R.WeenieError(0x043B);
            rig.R.UseDone();
        };

        var (_, status) = rig.Run(new QuestRun(rig.Context(), BarStepOnly(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=combine:untrained", Assert.Single(rig.Lines("quest: key=test-ingot")), StringComparison.Ordinal);
        Assert.Single(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.True(rig.R.H.State.Self.Inventory.Find(0x80000701).Held);
        Assert.True(rig.R.H.State.Self.Inventory.Find(0x80000702).Held);
    }

    /// <summary>The roll lost: both bars go, the dross arrives, and the run stops by name saying what was lost (the golem only hands 6330 back).</summary>
    [Fact]
    public void A_lost_roll_stops_the_chain_by_name_and_says_what_was_lost()
    {
        var rig = BarRig();
        rig.OnRecipe = (source, target) =>
        {
            rig.Removed(target);
            rig.Removed(source);
            rig.PackItem(0x80000703, Dross, "Pyreal Ingot");
            rig.System("You combine the two bars into an ingot.");
            rig.R.UseDone();
        };

        var (_, status) = rig.Run(new QuestRun(rig.Context(), BarStepOnly(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=combine:failed-roll", Assert.Single(rig.Lines("quest: key=test-ingot")), StringComparison.Ordinal);
        Assert.Contains(rig.Lines("PROBLEM"), l => l.Contains("lost 1 Pyreal Bar (6329), 1 Pyreal Bar (6329)", StringComparison.Ordinal) && l.Contains("arrived 1 Pyreal Ingot (6330)", StringComparison.Ordinal));
    }
}
