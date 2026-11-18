using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 errand lapse (live ladder57, 2026-11-17 UTC: the Atlan wand's hunt-until ran past the hour of the buff bot's lines and nobody asked its fight to keep her lines): the quest station shows the fight its hunt-until steps
/// while it runs (<see cref="IStationSubFight"/>), and not before or after, so the autopilot can ask it to keep her lines. Over the stand-in ACE of <see cref="QuestRig"/>; the fight is scripted.
/// </summary>
public sealed class QuestStationLapseTests
{
    private const uint TuskWcid = 22423;

    /// <summary>A scripted den fight the outer loop can stop (<see cref="IStoppableFight"/>), as the factory's real one is.</summary>
    private sealed class StoppableFight(Func<int, bool> each) : IStationRun, IStoppableFight
    {
        private int _steps;
        private bool _done;

        public string Name => "den-fight";

        public string Stage => _done ? "done" : "fight";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _done;

        public int Kills => _steps;

        public FightStopReason? StopReason { get; private set; }

        public void RequestStop(FightStopReason reason) => StopReason ??= reason;

        public string VerdictFields() => $"kills={_steps} until={(_done ? "met" : "not-met")}";

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            _done = _done || each(_steps++);
            return _done ? ScenarioStatus.Succeeded : ScenarioStatus.Running;
        }
    }

    [Fact]
    public void A_hunt_until_shows_the_fight_it_steps_while_it_runs_and_not_before_or_after()
    {
        var rig = new QuestRig();
        var ground = GroundCatalog.Get(FightOptions.DefaultGround);
        rig.Put(ground.Arrival.Cell, ground.Arrival.Origin);
        var steps = new[] { QuestRig.Step(0, QuestStepKind.HuntUntil, "a tusk", item: new QuestThing(TuskWcid, "Female Tusker Tusk"), count: 1, ground: ground.Key) };
        var quest = QuestRig.Quest("test-shown", new QuestDone(null, null, new QuestThing(TuskWcid, "Female Tusker Tusk")), null, steps);
        QuestRun? questRun = null;
        StoppableFight? fight = null;
        var shown = new List<IStoppableFight?>();
        questRun = new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, (_, _) => fight = new StoppableFight(n =>
        {
            shown.Add(questRun!.SubFight);
            if (n < 3)
            {
                return false;
            }

            rig.PackItem(0x80000504, TuskWcid, "Female Tusker Tusk");
            return true;
        }));

        Assert.Null(questRun.SubFight); // no fight before the step's
        var (_, status) = rig.Run(questRun);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(4, shown.Count);
        Assert.All(shown, s => Assert.Same(fight, s)); // the fight was shown on every one of its frames
        Assert.Null(questRun.SubFight); // and not after it ended
        Assert.Contains(rig.Lines("quest_step: n=0 kind=hunt-until"), l => l.Contains("held=1/1 hunted=yes arrived=1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hunt_until_called_again_goes_on_from_what_she_holds()
    {
        // M6 16 E3.1: a hunt whose fight was left (for mana, on the retreat line) is called again that run; the step counts what she holds, so the hunt resumes with its count kept and its fight is asked for the rest.
        var rig = new QuestRig();
        var ground = GroundCatalog.Get(FightOptions.DefaultGround);
        rig.Put(ground.Arrival.Cell, ground.Arrival.Origin);
        rig.PackItem(0x80000504, TuskWcid, "Female Tusker Tusk"); // gained before the leave
        rig.PackItem(0x80000505, TuskWcid, "Female Tusker Tusk");
        var steps = new[] { QuestRig.Step(0, QuestStepKind.HuntUntil, "three tusks", item: new QuestThing(TuskWcid, "Female Tusker Tusk"), count: 3, ground: ground.Key) };
        var quest = QuestRig.Quest("test-again", new QuestDone(null, null, new QuestThing(TuskWcid, "Female Tusker Tusk")), null, steps);
        StationSpec? asked = null;
        var questRun = new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, (spec, _) =>
        {
            asked = spec;
            return new StoppableFight(n =>
            {
                if (n < 1)
                {
                    return false;
                }

                rig.PackItem(0x80000506, TuskWcid, "Female Tusker Tusk");
                return true;
            });
        });

        var (_, status) = rig.Run(questRun);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(rig.Lines("hunt-until: 2 of 3 Female Tusker Tusk (22423) held"));
        Assert.Contains(asked!.Arguments, a => a.Value.Contains("3", StringComparison.Ordinal) && a.Value.Contains("22423", StringComparison.Ordinal)); // the fight's until is the whole count: the two held count toward it
        Assert.Contains(rig.Lines("quest_step: n=0 kind=hunt-until"), l => l.Contains("held=3/3 hunted=yes arrived=1", StringComparison.Ordinal));
    }
}
