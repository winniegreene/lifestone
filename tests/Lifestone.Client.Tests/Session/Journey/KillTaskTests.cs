using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 5 Q3 (D54): the kill task's shape end to end over a stand-in ACE and a fake ground fight (the station's sub-run seam): the talk arms the task, the fight's kills bring ACE's counter lines (QuestManager.HandleKillTask, @95ef426d
/// Managers/QuestManager.cs:583-626: silent while unarmed), "Your task is complete!" ends it, the collect talk awards the experience (the reward line and the property), the timer is saved. A fight that ends short fails by name
/// (<c>kill:not-complete:&lt;n&gt;/&lt;count&gt;</c>), and a kill before the arm counts nothing.
/// </summary>
public sealed class KillTaskTests
{
    private const uint SanMing = 0x7DA5504B;
    private const string Name = "San Ming";
    private const uint PlaqueWcid = 31617;
    private const string Start = "So, you wish to prove your hunting prowess?";
    private const string NotYet = "I see you haven't completed your task.";
    private const string Reward = "Excellent work, friend! You have slain many of the terrible beasts!";
    private const string Truly = "Congratulations! You are now truly a Blood Shreth Butcher!";

    private static QuestThing Npc => new(31641, Name);

    private static QuestStep[] Steps(bool killFirst = false)
    {
        var arm = QuestRig.Step(0, QuestStepKind.Talk, "the task", target: Npc, expect: new QuestExpect("hunting prowess", null, null));
        var kill = QuestRig.Step(1, QuestStepKind.Kill, "ten Blood Shreths", target: new QuestThing(4110, "Blood Shreth"), count: 10, ground: FightOptions.DefaultGround, expect: new QuestExpect(QuestReads.TaskComplete, null, null));
        var collect = QuestRig.Step(2, QuestStepKind.Talk, "the reward", target: Npc, expect: new QuestExpect("Excellent work, friend!", null, new QuestThing(PlaqueWcid, "Blood Shreth Butcher Plaque")));
        return killFirst ? [kill with { Index = 0 }, arm with { Index = 1 }, collect] : [arm, kill, collect];
    }

    private static Quest Task(bool killFirst = false) =>
        QuestRig.Quest("test-kill-task", new QuestDone(null, Truly, new QuestThing(PlaqueWcid, "Blood Shreth Butcher Plaque")), 72000, Steps(killFirst));

    /// <summary>The rig: San Ming on the ground's arrival landblock; his Use arms the task (when <paramref name="arms"/>) or, with 10 kills, rewards.</summary>
    private static (QuestRig Rig, Func<bool> Armed, Func<int> Killed, Action<int> Kill) Rig(bool arms = true)
    {
        var rig = new QuestRig();
        var ground = GroundCatalog.Get(FightOptions.DefaultGround);
        rig.Put(ground.Arrival.Cell, ground.Arrival.Origin);
        rig.Npc(SanMing, Name, 31641, ground.Arrival.Cell, ground.Arrival.Origin + new Vector3(3, 0, 0));
        var armed = false;
        var killed = 0;
        rig.OnUse[SanMing] = () =>
        {
            if (armed && killed >= 10)
            {
                rig.Tell(SanMing, Name, Reward);
                rig.Award(1_200);
                rig.PackItem(0x80000601, PlaqueWcid, "Blood Shreth Butcher Plaque");
                rig.After(1, () => rig.Tell(SanMing, Name, Truly));
                armed = false;
                return;
            }

            if (armed && killed > 0)
            {
                rig.Tell(SanMing, Name, NotYet);
                return;
            }

            rig.Tell(SanMing, Name, Start + " Well then. Track down and slay 10 of the terrible Blood Shreth for me.");
            armed = arms;
            killed = 0;
        };

        void Kill(int n)
        {
            if (!armed)
            {
                return; // ACE: if (!HasQuest(questName)) return; silent
            }

            killed = Math.Min(10, killed + n);
            rig.System(killed >= 10
                ? string.Create(CultureInfo.InvariantCulture, $"You have killed {killed} Blood Shreths! Your task is complete!")
                : string.Create(CultureInfo.InvariantCulture, $"You have killed {killed} Blood Shreths! You must kill 10 to complete your task."));
        }

        return (rig, () => armed, () => killed, Kill);
    }

    /// <summary>A fake fight killing one Blood Shreth a step, up to <paramref name="kills"/>, met when the counter said complete.</summary>
    private static Func<StationSpec, JourneyContext, IStationRun> Fight(Action<int> kill, Func<int> killed, int kills, List<StationSpec> specs)
    {
        return (spec, _) =>
        {
            specs.Add(spec);
            FakeFight? fight = null;
            fight = new FakeFight(n =>
            {
                if (n >= kills)
                {
                    return true;
                }

                kill(1);
                fight!.Met = killed() >= 10;
                return fight.Met;
            });
            return fight;
        };
    }

    [Fact]
    public void Arm_ten_counter_lines_complete_collect_reward_and_the_timer_saved()
    {
        var (rig, _, killed, kill) = Rig();
        var specs = new List<StationSpec>();
        var store = new MemoryCheckpointStore();

        var (run, status) = rig.Run(new QuestRun(rig.Context(store), Task(), JourneyPlans.FromHome, Fight(kill, killed, 30, specs)));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var spec = Assert.Single(specs);
        Assert.Equal("chat:" + QuestReads.TaskComplete, spec.Arguments[JourneyStations.UntilArgument]);
        Assert.False(spec.Arguments.ContainsKey(JourneyStations.WantArgument));
        Assert.Single(rig.Lines("is the kill task's arm"));
        Assert.Single(rig.Lines("kill: counter lines 10; the last: \"You have killed 10 Blood Shreths! Your task is complete!\" (n=10, complete=yes); complete=yes"));
        Assert.Contains(rig.Lines("quest_step: n=1 kind=kill"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("counter=10/10 complete=yes lines=10 until=met", StringComparison.Ordinal));
        Assert.Contains(rig.Lines("quest_step: n=2 kind=talk"), l => l.Contains("reward_line=1200", StringComparison.Ordinal));
        Assert.Contains("quest: key=test-kill-task from=home steps=3/3 done=yes xp=+1200 reward_line=1200 timer=saved ok=yes fail=none", Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal);
        Assert.Equal(1200, rig.R.H.State.Self.Qualities.TotalExperience);
        Assert.Contains("test-kill-task", store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.True(((QuestRun)run).QuestDone);
    }

    [Fact]
    public void A_fight_that_ends_short_of_the_complete_line_fails_by_name()
    {
        var (rig, _, killed, kill) = Rig();

        var (run, status) = rig.Run(new QuestRun(rig.Context(), Task(), JourneyPlans.FromHome, Fight(kill, killed, 3, [])));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("kill:not-complete:3/10", ((QuestRun)run).FailName);
        Assert.Contains("steps=1/3 done=no", Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal);
        Assert.Empty(rig.Lines("quest_step: n=2"));
    }

    [Fact]
    public void A_fight_with_no_castable_war_spell_ends_the_step_by_that_name_not_the_kill_count_M6_7_K()
    {
        var (rig, _, _, _) = Rig();
        const string why = FightScenario.NoCastableFailure + ": no-components: Flame Bolt IV needs Silver Scarab 688, which the pack does not hold; a logistics trip is due";

        var (run, status) = rig.Run(new QuestRun(rig.Context(), Task(), JourneyPlans.FromHome, (_, _) => new FakeFight(_ => true) { FailWith = why }));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("kill:" + FightScenario.NoCastableName, ((QuestRun)run).FailName);
        Assert.Contains(why, ((QuestRun)run).Failure, StringComparison.Ordinal);
        Assert.Empty(rig.Lines("quest_step: n=2"));
    }

    [Fact]
    public void Kills_the_server_did_not_arm_count_nothing()
    {
        var (rig, _, killed, kill) = Rig(arms: false);

        var (run, status) = rig.Run(new QuestRun(rig.Context(), Task(), JourneyPlans.FromHome, Fight(kill, killed, 12, [])));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("kill:not-complete:0/10", ((QuestRun)run).FailName);
        Assert.Single(rig.Lines("kill: counter lines 0; the last: none"));
    }

    [Fact]
    public void A_kill_step_with_no_talk_before_it_is_refused_before_any_fight()
    {
        var (rig, _, killed, kill) = Rig();
        var specs = new List<StationSpec>();

        var (run, status) = rig.Run(new QuestRun(rig.Context(), Task(killFirst: true), JourneyPlans.FromHome, Fight(kill, killed, 30, specs)));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("kill:unarmed", ((QuestRun)run).FailName);
        Assert.Empty(specs);
        Assert.Empty(rig.Events);
    }

    /// <summary>
    /// M6 14 guard task: the level up job runs a kill task's arm alone (steps 0-0 here: the talk; the ladder's own hunt is the kill). Unarmed, the talk arms it and the range ends partial, nothing recorded; when the server's counter was
    /// already complete (a new process that read nothing yet), the giver's answer to the arming talk is the turn-in itself: the talk ends well on the quest's done reply, the steps after it are done by it, the done check runs and the
    /// timer is saved.
    /// </summary>
    [Fact]
    public void An_arming_talk_whose_counter_was_complete_is_the_turn_in_and_the_done_check_records_it()
    {
        var (rig, _, killed, kill) = Rig();
        var (first, firstStatus) = rig.Run(new QuestRun(rig.Context(), Task(), JourneyPlans.FromHome, null, steps: "0-0"));
        Assert.Equal(ScenarioStatus.Succeeded, firstStatus);
        Assert.Contains("steps=1/3 done=partial", Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal); // the arm alone: nothing recorded
        Assert.False(((QuestRun)first).QuestDone);

        kill(10); // the ladder's hunt: ten counted kills, the counter complete
        Assert.Equal(10, killed());
        var store = new MemoryCheckpointStore();
        var (run, status) = rig.Run(new QuestRun(rig.Context(store), Task(), JourneyPlans.FromHome, null, steps: "0-0"));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(((QuestRun)run).QuestDone);
        Assert.Single(rig.Lines("answered the arming talk with the quest's done reply"));
        Assert.Contains("done=yes xp=+1200 reward_line=1200 timer=saved ok=yes fail=none steps=0-0", rig.Lines("quest: key=")[^1], StringComparison.Ordinal);
        Assert.Contains("test-kill-task", store.Json ?? string.Empty, StringComparison.Ordinal);
    }
}
