using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 combine (the owner's ruling, 2026-11-17: she can also combine the motes into nuggets and bars to save pack space): the no-roll chain is read from the quest file's combine steps (the Atlan wand's 790, 792 and 791; never the roll of 789), the
/// hunt counts along it in mote-equivalents, and a later combine step whose result the hunt already made passes as done, logged; the roll step runs as before.
/// </summary>
public sealed class QuestCombineChainTests
{
    private const uint Mote = 6353, Sliver = 6355, Nugget = 6354, Bar = 6329, Ingot = 6331, PlainIngot = 6330;

    [Fact]
    public void The_Atlan_wands_chain_is_read_from_its_no_roll_combine_steps_and_counts_in_mote_equivalents()
    {
        var quest = QuestCatalog.Get("atlan-wand");
        var chain = QuestCombineChain.Of(quest, Mote);

        Assert.Equal([Mote, Sliver, Nugget, Bar], chain); // the ingot's step (Alchemy against 150, a fail result) is no link
        Assert.Equal(3, quest.Steps.Count(QuestCombineChain.IsNoRollLink));
        var counts = new Dictionary<uint, int> { [Mote] = 0, [Sliver] = 2, [Nugget] = 0, [Bar] = 0 };
        Assert.Equal(4, QuestCombineChain.Units(chain, w => counts.GetValueOrDefault(w))); // 3 motes and a fourth looted, combined: 2 slivers are 4 motes, 12 more wanted
        Assert.Equal(16, QuestCombineChain.Units(chain, w => w == Bar ? 2 : 0));
        Assert.Equal(16, QuestCombineChain.Units(chain, w => w switch { Bar => 1, Nugget => 1, Sliver => 1, Mote => 2, _ => 0 }));
        Assert.Equal(2, QuestCombineChain.Units(QuestCombineChain.Of(quest, Bar), w => w == Bar ? 2 : 0)); // in bar units from the bar
        var huntSteps = quest.Steps.Where(QuestCombineChain.IsNoRollLink).Select(s => s.Count).ToArray();
        Assert.Equal([8, 4, 2], huntSteps); // each link halves the count, as the chain's weights say
    }

    private static QuestThing Thing(uint wcid, string name) => new(wcid, name);

    /// <summary>The Atlan wand's four combines as a test file: three no-roll links, then the ingot's step with its fail result and line (a roll).</summary>
    private static Quest Combines() => QuestRig.Quest(
        "test-ingot",
        new QuestDone(null, null, Thing(Ingot, "Quality Pyreal Ingot")),
        null,
        QuestRig.Step(0, QuestStepKind.Combine, "16 motes to 8 slivers", target: Thing(Mote, "Pyreal Mote"), item: Thing(Mote, "Pyreal Mote"), expect: new QuestExpect("You combine the two motes into a larger sliver.", null, Thing(Sliver, "Pyreal Sliver")), count: 8),
        QuestRig.Step(1, QuestStepKind.Combine, "8 slivers to 4 nuggets", target: Thing(Sliver, "Pyreal Sliver"), item: Thing(Sliver, "Pyreal Sliver"), expect: new QuestExpect("You combine the two slivers into a larger nugget.", null, Thing(Nugget, "Pyreal Nugget")), count: 4),
        QuestRig.Step(2, QuestStepKind.Combine, "4 nuggets to 2 bars", target: Thing(Nugget, "Pyreal Nugget"), item: Thing(Nugget, "Pyreal Nugget"), expect: new QuestExpect("You combine the two nuggets into a larger bar.", null, Thing(Bar, "Pyreal Bar")), count: 2),
        QuestRig.Step(3, QuestStepKind.Combine, "2 bars to the ingot", target: Thing(Bar, "Pyreal Bar"), item: Thing(Bar, "Pyreal Bar"), expect: new QuestExpect("You combine the two bars into an ingot of high quality.", null, Thing(Ingot, "Quality Pyreal Ingot")), count: 1, failItem: Thing(PlainIngot, "Pyreal Ingot"), failReply: "You combine the two bars into an ingot."));

    /// <summary>The scripted ACE's answer to the four recipes: both consumed, the next item of the chain made (the roll's success for the bars), and the recipe's line.</summary>
    private static void Answers(QuestRig rig)
    {
        var next = 0x80000780u;
        rig.OnRecipe = (source, target) =>
        {
            var wcid = rig.R.H.State.Objects.TryGet(source, out var o) ? o.Weenie.WeenieClassId : 0;
            var (made, name, line) = wcid switch
            {
                Mote => (Sliver, "Pyreal Sliver", "You combine the two motes into a larger sliver."),
                Sliver => (Nugget, "Pyreal Nugget", "You combine the two slivers into a larger nugget."),
                Nugget => (Bar, "Pyreal Bar", "You combine the two nuggets into a larger bar."),
                _ => (Ingot, "Quality Pyreal Ingot", "You combine the two bars into an ingot of high quality."),
            };
            rig.Removed(target);
            rig.Removed(source);
            rig.PackItem(next++, made, name);
            rig.System(line);
            rig.R.UseDone();
        };
    }

    [Fact]
    public void Sixteen_motes_reached_as_two_bars_pass_the_three_no_roll_steps_and_run_the_ingots_roll()
    {
        var rig = new QuestRig();
        rig.PackItem(0x80000701, Bar, "Pyreal Bar");
        rig.PackItem(0x80000702, Bar, "Pyreal Bar");
        Answers(rig);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), Combines(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal)); // only the roll's use: the bars on each other
        Assert.Single(rig.Lines("quest: resume: the run starts at step 3 of 0-3 (combine: 2 bars to the ingot): step 2 (combine: 4 nuggets to 2 bars) is made: 6329 x2 held"));
        Assert.Empty(rig.Lines("quest_step: n=0 "));
        Assert.Contains(rig.Lines("quest_step: n=3 kind=combine"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("outcome=success uses=1/1", StringComparison.Ordinal));
    }

    [Fact]
    public void Sixteen_motes_held_as_a_bar_a_nugget_a_sliver_and_two_motes_run_only_the_uses_each_step_still_needs_then_the_roll()
    {
        // The hunt combined as it went and stopped at 16: 8 + 4 + 2 + 2. No step's product is held to its count, so the run starts at the first combine; each no-roll step counts what is held of its result and above and runs the uses left.
        var rig = new QuestRig();
        rig.PackItem(0x80000701, Bar, "Pyreal Bar");
        rig.PackItem(0x80000702, Nugget, "Pyreal Nugget");
        rig.PackItem(0x80000703, Sliver, "Pyreal Sliver");
        rig.PackItem(0x80000704, Mote, "Pyreal Mote");
        rig.PackItem(0x80000705, Mote, "Pyreal Mote");
        Answers(rig);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), Combines(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(4, rig.Events.Count(e => e.StartsWith("recipe:", StringComparison.Ordinal))); // one use of each step: 2 motes, 2 slivers, 2 nuggets, then the bars' roll
        Assert.Single(rig.Lines("combine: 16 motes to 8 slivers: check before: 6355 x1, 6354 x1, 6329 x1 is 7 Pyreal Sliver or their worth, of the 8 the step makes; 1 use(s) left of 2 Pyreal Mote held"));
        Assert.Single(rig.Lines("combine: 8 slivers to 4 nuggets: check before: 6354 x1, 6329 x1 is 3 Pyreal Nugget or their worth, of the 4 the step makes; 1 use(s) left of 2 Pyreal Sliver held"));
        Assert.Single(rig.Lines("combine: 4 nuggets to 2 bars: check before: 6329 x1 is 1 Pyreal Bar or their worth, of the 2 the step makes; 1 use(s) left of 2 Pyreal Nugget held"));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=combine"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("outcome=success uses=1/1", StringComparison.Ordinal));
        Assert.Contains(rig.Lines("quest_step: n=3 kind=combine"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("outcome=success uses=1/1", StringComparison.Ordinal));
    }
}
