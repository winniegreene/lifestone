using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;

namespace Lifestone.Client.Tests.Session.Quests;

/// <summary>
/// M6 14 the Atlan wand made runnable (the owner, 2026-11-16): the one roll's skill is data on its step and is trained and raised by a pure plan before the roll (ACE's chance, <see cref="RecipeChance"/>, <see cref="RecipeSkillPlan"/>); a
/// crafted chain cut after its hunt or its combines starts after what she holds (<see cref="QuestResume"/>); a stone already held skips its walk; a lost roll keeps the errand callable; the errand's station is sized for its hunt; the
/// credit order takes the job's skill (D136). What these do not prove: the live server's answers to the train and the raises, the walks and the fights; those are the lead's live run.
/// </summary>
public sealed class AtlanWandReadyTests
{
    private const uint Mote = 6353, Bar = 6329, QualityIngot = 6331, Glyph = 19410, Infused = 46141, Wand = 46122, Stone = 6320, StonedWand = 46128;

    private static Quest File => QuestCatalog.Get("atlan-wand");

    private static Func<uint, int> Held(params (uint Wcid, int Count)[] items) => w => items.Where(i => i.Wcid == w).Sum(i => i.Count);

    [Fact]
    public void The_roll_is_alchemy_against_150_on_its_step_and_aces_logistic()
    {
        var bars = File.Steps.Single(s => s.Kind == QuestStepKind.Combine && s.Item!.Wcid == Bar);
        Assert.Equal((38u, 150u), (bars.RecipeSkill!.Value, bars.RecipeDifficulty!.Value));
        Assert.All(File.Steps.Where(s => s.Index != bars.Index), s => Assert.Null(s.RecipeSkill)); // the other five recipes have no roll
        Assert.Equal(0.5, RecipeChance.Of(150, 150), 9);
        Assert.Equal(0.4551, RecipeChance.Of(144, 150), 4); // her trained Alchemy on her buffed Coordination 145 and Focus 288 (AtlanAlchemyDatTests)
        Assert.Equal(0.2479, RecipeChance.Of(113, 150), 4); // on her base 105 and 233
        Assert.Equal(249u, RecipeChance.SkillFor(150, 0.95));
        Assert.Equal(224u, RecipeChance.SkillFor(150, 0.90));
    }

    [Fact]
    public void The_skill_plan_trains_then_raises_to_the_plan_chance_and_rolls_when_the_experience_runs_out()
    {
        uint[] list = [0, 10, 30, 60, 100];
        Assert.Equal(RecipeSkillMoveKind.Train, RecipeSkillPlan.Next(false, 9, 6, 0, 0, list, 0, 150, 0.95).Kind); // her 9 credits, Alchemy's 6
        Assert.Equal(RecipeSkillMoveKind.Untrained, RecipeSkillPlan.Next(false, 4, 6, 0, 0, list, 0, 150, 0.95).Kind);
        var raise = RecipeSkillPlan.Next(true, 3, 6, 144, 1, list, 1_000, 150, 0.95);
        Assert.Equal((RecipeSkillMoveKind.Raise, 20L), (raise.Kind, raise.Price)); // rank 1 -> 2: 30 - 10
        var short_ = RecipeSkillPlan.Next(true, 3, 6, 144, 1, list, 19, 150, 0.95);
        Assert.Equal(RecipeSkillMoveKind.Roll, short_.Kind);
        Assert.Contains("TODO(m6-roll-under-plan)", short_.Why, StringComparison.Ordinal);
        Assert.Equal(RecipeSkillMoveKind.Roll, RecipeSkillPlan.Next(true, 3, 6, 249, 1, list, 0, 150, 0.95).Kind);
        Assert.Equal(RecipeSkillMoveKind.Roll, RecipeSkillPlan.Next(true, 3, 6, 144, 4, list, 1_000_000, 150, 0.95).Kind); // the last rank
    }

    [Theory]
    [InlineData(0, 0)] // nothing: the file's start
    [InlineData(1, 5)] // 16 motes: the recall after the hunt
    [InlineData(2, 9)] // two bars: the roll
    [InlineData(3, 10)] // the Quality Ingot: back to the village and up the tower
    [InlineData(4, 19)] // the ingot and a glyph: the apply on the roof
    [InlineData(5, 20)] // the Superb Infused Pyreal Ingot: the give
    [InlineData(6, 21)] // the Atlan Wand: the recall down from the roof
    [InlineData(7, 0)] // a stone alone (the stone's own errand): no forging is skipped
    [InlineData(8, 24)] // the wand and a stone: the apply
    [InlineData(9, 25)] // the stoned wand: the wear
    public void A_cut_chain_starts_after_what_she_holds(int held, int step)
    {
        (uint, int)[][] cases =
        [
            [],
            [(Mote, 16)],
            [(Bar, 2)],
            [(QualityIngot, 1)],
            [(QualityIngot, 1), (Glyph, 1)],
            [(Infused, 1)],
            [(Wand, 1)],
            [(Stone, 1)],
            [(Wand, 1), (Stone, 1)],
            [(StonedWand, 1)],
        ];
        Assert.Equal(step, QuestResume.From(File, Held(cases[held])).Step);
    }

    [Fact]
    public void Fifteen_motes_are_not_a_hunt_done()
    {
        Assert.Equal(0, QuestResume.From(File, Held((Mote, 15))).Step);
        Assert.Equal(0, QuestResume.From(File, Held((Bar, 1))).Step); // one bar: the roll needs two
    }

    [Fact]
    public void A_lost_roll_keeps_the_errand_callable_and_any_other_failure_does_not()
    {
        Assert.True(AutopilotScenario.RecipeRollLost("combine:failed-roll"));
        Assert.True(AutopilotScenario.RecipeRollLost("apply:failed-roll"));
        Assert.False(AutopilotScenario.RecipeRollLost("combine:untrained"));
        Assert.False(AutopilotScenario.RecipeRollLost(null));
    }

    [Fact]
    public void The_errands_station_is_sized_for_its_hunt_and_a_turn_in_keeps_its_own()
    {
        Assert.Equal(AutopilotScenario.QuestTripSeconds + QuestStation.FightExpectedSeconds, AutopilotScenario.QuestSeconds("atlan-wand"));
        Assert.Equal(AutopilotScenario.QuestTripSeconds, AutopilotScenario.QuestSeconds("minor-smoldering-stone"));
        var trophy = QuestCatalog.All.First(q => q.Ladder is null && q.Steps.Any(s => s.Kind == QuestStepKind.HuntUntil));
        Assert.Equal(AutopilotScenario.QuestTripSeconds, AutopilotScenario.QuestSeconds(trophy.Key));
    }

    [Fact]
    public void The_credit_order_takes_alchemy_only_on_the_jobs_need()
    {
        var plan = MageTemplate.PlanClimberCreation(52, id => id == MageTemplate.WarMagic ? 14 : id == MageTemplate.LifeMagic ? 12 : id == MageTemplate.Alchemy ? 6 : 8, id => id == MageTemplate.WarMagic ? 28 : 20, buffBot: true);
        Assert.DoesNotContain(SkillCreditPlan.OrderOf(plan), l => l.Skill == MageTemplate.Alchemy);
        var need = JobBolt.Skills([File]);
        Assert.Equal(new CreditLine(MageTemplate.Alchemy, 6), SkillCreditPlan.OrderOf(plan, need.Contains)[^1]);
    }

    [Fact]
    public void The_file_carries_no_todo_left()
    {
        Assert.All(File.Steps, s => Assert.Null(s.Todo));
        Assert.DoesNotContain(File.Sources, s => s.Contains("TODO(", StringComparison.Ordinal));
        Assert.All(QuestCatalog.Get("minor-smoldering-stone").Steps, s => Assert.Null(s.Todo));
    }

    /// <summary>The walk to the stone is not made when she holds one (the stone's own errand ran first): the go says held and the pick-up says held, nothing sent.</summary>
    [Fact]
    public void A_stone_held_skips_the_walk_to_it()
    {
        var go = File.Steps.Single(s => s.Kind == QuestStepKind.Go && s.Target?.Wcid == Stone);
        var pick = File.Steps.Single(s => s.Kind == QuestStepKind.PickUp);
        var quest = QuestRig.Quest("test-stone", new QuestDone(null, null, new QuestThing(Stone, "Minor Smoldering Stone")), null, go with { Index = 0 }, pick with { Index = 1 });
        var rig = new QuestRig();
        rig.PackItem(0x80000801, Stone, "Minor Smoldering Stone");

        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Contains(rig.Lines("go:"), l => l.Contains("is held already and the next step picks it up: no walk", StringComparison.Ordinal));
        Assert.Contains(rig.Lines("pick-up:"), l => l.Contains("is held already", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("pick-up:", StringComparison.Ordinal));
        Assert.NotEqual(ScenarioStatus.Running, status);
    }
}
