using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 5 Q3: the quest station over a stand-in ACE (<see cref="QuestRig"/>): each step's check, act, check and its one named recovery, the <c>quest_step:</c> lines and the verdict line, the "arrives" rule for an expected item, the
/// hunt-until done at once when its count is held, a refusal by name, the confirm step (Q1's rule, answered), and the timers saved through the host's store.
/// </summary>
public sealed class QuestStationTests
{
    private const uint Npc = 0x7F68200D;
    private const string NpcName = "Brighteyes, the Tailor";
    private const uint Tusk = 0x80000501;
    private const uint TuskWcid = 22423;
    private const uint GemWcid = 22729;
    private const uint Gem = 0x80000502;
    private const string Intro = "Give me one of their tusks and I'll be sure to reward you well.";
    private const string Thanks = "Excellent work. Of course this is the lowliest of the tuskers";

    private static readonly Vector3 NpcAt = new(90, 12, 94);

    private static QuestPlace Room => new(QuestRig.HomeCell, new Vector3(89, 12, 94), "her room");

    private static QuestThing Brighteyes => new(22642, NpcName);

    private static Quest TuskQuest(params QuestStep[] steps) =>
        QuestRig.Quest("test-tusk", new QuestDone(null, Thanks, new QuestThing(GemWcid, "Aphus Lassel Portal Gem")), 72000, steps);

    private static QuestStep[] TurnIn() =>
    [
        QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes),
        QuestRig.Step(1, QuestStepKind.Talk, "her intro", target: Brighteyes, expect: new QuestExpect("reward you well", null, null)),
        QuestRig.Step(2, QuestStepKind.Give, "the tusk", target: Brighteyes, item: new QuestThing(TuskWcid, "Female Tusker Tusk"), expect: new QuestExpect(Thanks, null, new QuestThing(GemWcid, "Aphus Lassel Portal Gem"))),
    ];

    private static QuestRig Rig(bool gemHeldBefore = false, bool gives = true)
    {
        var rig = new QuestRig();
        rig.Npc(Npc, NpcName, 22642, QuestRig.HomeCell, NpcAt);
        rig.PackItem(Tusk, TuskWcid, "Female Tusker Tusk");
        if (gemHeldBefore)
        {
            rig.PackItem(0x80000503, GemWcid, "Aphus Lassel Portal Gem");
        }

        rig.OnUse[Npc] = () => rig.Tell(Npc, NpcName, Intro);
        rig.OnGive[Npc] = item =>
        {
            rig.Taken(item, Npc, NpcName, "Female Tusker Tusk");
            rig.Tell(Npc, NpcName, Thanks + ", but you did show your dedication to my cause.");
            rig.Award(100_000);
            if (gives)
            {
                rig.PackItem(Gem, GemWcid, "Aphus Lassel Portal Gem");
            }
        };
        return rig;
    }

    /// <summary>M6 8 M: a go step's file way reaches the leg: the points in order, in their cells, on the request the seam is asked (the leg's own walk of it: J4JourneyHostTests).</summary>
    [Fact]
    public void A_go_hands_the_files_way_to_the_walk()
    {
        var rig = new QuestRig();
        var foot = new QuestPlace(QuestRig.HomeCell, new Vector3(86, 12, 94), "the foot");
        var head = new QuestPlace(QuestRig.HomeCell, new Vector3(85, 9, 94), "the head");
        var step = QuestRig.Step(0, QuestStepKind.Go, "down the stair", place: foot) with { Way = [head, foot] };
        var quest = QuestRig.Quest("test-way", new QuestDone(null, "x", null), null, step);

        rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null, "0-0"));

        var way = Assert.Single(rig.Travel.Requests).Way;
        Assert.NotNull(way);
        Assert.Equal([(QuestRig.HomeCell, head.Point!.Value), (QuestRig.HomeCell, foot.Point!.Value)], way!.Select(w => (w.CellId, w.Frame.Origin)).ToArray());
    }

    [Fact]
    public void A_turn_in_runs_go_talk_give_and_records_the_timer_through_the_store()
    {
        var rig = Rig();
        var store = new MemoryCheckpointStore();
        var (run, status) = rig.Run(new QuestRun(rig.Context(store), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var verdict = Assert.Single(rig.Lines("quest: key="));
        Assert.Contains("quest: key=test-tusk from=home steps=3/3 done=yes xp=+100000 reward_line=100000 timer=saved ok=yes fail=none", verdict, StringComparison.Ordinal);
        Assert.Equal(3, rig.Lines("quest_step: n=").Count(l => l.Contains(" ok=yes ", StringComparison.Ordinal)));
        Assert.Contains(rig.Lines("quest_step: n=2 kind=give"), l => l.Contains("taken=yes give_line=given replied=yes reward_line=100000 xp=+100000", StringComparison.Ordinal));
        Assert.Equal("give: Aphus Lassel Portal Gem arrived: held 0 -> 1", QuestRun.GiftArrivedLine("Aphus Lassel Portal Gem", 0, 1)); // the string TrophyLiveTests asserts (review 5-R1)
        Assert.Single(rig.Lines("give: Aphus Lassel Portal Gem arrived: held 0 -> 1"));
        Assert.Contains("test-tusk", store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(rig.Lines("quest key=test-tusk character=\"Winnie\" state=waiting"), l => l.Contains("saved=yes", StringComparison.Ordinal));
        Assert.True(run.Ok);
    }

    /// <summary>M6 8, live 2026-11-11: the reward is paid at the give and the walk home after it fails: the completion is recorded and saved, the station still fails by the walk.</summary>
    [Fact]
    public void A_step_that_fails_after_the_reward_still_records_the_completion_M6_8()
    {
        var rig = Rig();
        var store = new MemoryCheckpointStore();
        var away = new QuestPlace(QuestRig.HomeCell, new Vector3(70, 12, 94), "the way home");
        QuestStep[] steps = [.. TurnIn(), QuestRig.Step(3, QuestStepKind.Go, "home", place: away)];
        var gives = 0;
        rig.Travel.Refuse = r => gives > 0 ? "front-wall" : null;
        var give = rig.OnGive[Npc];
        rig.OnGive[Npc] = item => { give(item); gives++; };
        var context = rig.Context(store);
        var (run, status) = rig.Run(new QuestRun(context, TuskQuest(steps), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains(rig.Lines("quest_step: n=3 kind=go"), l => l.Contains(" ok=no ", StringComparison.Ordinal));
        Assert.Single(rig.Lines("done: check after the failure"), l => l.Contains("recorded by done-reply", StringComparison.Ordinal) && l.Contains("timers saved", StringComparison.Ordinal) && l.Contains("m6-quest-done-before-fail", StringComparison.Ordinal));
        Assert.Contains("test-tusk", store.Json ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_that_skips_the_go_runs_the_give_and_completes_when_it_ends_at_the_last_step()
    {
        var rig = Rig();
        var store = new MemoryCheckpointStore();
        var (_, status) = rig.Run(new QuestRun(rig.Context(store), TuskQuest(TurnIn()), JourneyPlans.FromHome, null, "1-2"));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var verdict = Assert.Single(rig.Lines("quest: key="));
        Assert.Contains("steps=2/3 done=yes ", verdict, StringComparison.Ordinal);
        Assert.Contains(" steps=1-2 wasted_casts=0 shots_blocked=0 shot_disagreements=0", verdict, StringComparison.Ordinal); // M6 8 L: the shot fields follow the range
        Assert.Empty(rig.Lines("quest_step: n=0 "));
        Assert.Contains("test-tusk", store.Json ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_that_ends_before_the_give_records_nothing_and_says_done_partial()
    {
        var rig = Rig();
        var store = new MemoryCheckpointStore();
        var (_, status) = rig.Run(new QuestRun(rig.Context(store), TuskQuest(TurnIn()), JourneyPlans.FromHome, null, "0-1"));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var verdict = Assert.Single(rig.Lines("quest: key="));
        Assert.Contains("steps=2/3 done=partial ", verdict, StringComparison.Ordinal);
        Assert.Contains(" ok=yes fail=none steps=0-1", verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("test-tusk", store.Json ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("give:", StringComparison.Ordinal));
        Assert.Empty(rig.Lines("quest_step: n=2 "));
    }

    [Theory]
    [InlineData("1", "steps:malformed")]
    [InlineData("a-2", "steps:malformed")]
    [InlineData("0-3", "steps:out-of-range")]
    [InlineData("2-1", "steps:out-of-range")]
    public void A_bad_range_stops_by_name_at_the_start_before_any_step(string range, string name)
    {
        var rig = Rig();
        var (run, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null, range));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(name, ((QuestRun)run).FailName);
        Assert.Empty(rig.Lines("quest_step: n="));
        Assert.Contains("ok=no fail=" + name, Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_npc_busy_refusal_waits_and_gives_once_more_as_the_named_recovery()
    {
        var rig = Rig();
        var accept = rig.OnGive[Npc];
        var first = true;
        rig.OnGive[Npc] = item =>
        {
            if (first)
            {
                first = false;
                rig.Refused(item, QuestReads.BusyCode, NpcName);
                return;
            }

            accept(item);
        };

        var (_, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(rig.Lines("recovery=give-after-busy"));
        Assert.Equal(2, rig.Events.Count(e => e.StartsWith("give:", StringComparison.Ordinal)));
        Assert.Contains("ok=yes fail=none", Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal);
    }

    [Fact]
    public void After_a_busy_refusal_the_next_give_waits_until_the_npc_has_stopped_talking()
    {
        var rig = Rig();
        var accept = rig.OnGive[Npc];
        var gives = new List<double>();
        rig.OnGive[Npc] = item =>
        {
            gives.Add(rig.Now);
            if (gives.Count > 1)
            {
                accept(item);
                return;
            }

            rig.Refused(item, QuestReads.BusyCode, NpcName);
            foreach (var second in new[] { 1.0, 2.0, 3.0 })
            {
                rig.After(second, () => rig.Tell(Npc, NpcName, "And another thing."));
            }
        };

        var (_, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, gives.Count);
        Assert.True(gives[1] - gives[0] >= 3.0 + QuestRun.BusyQuietSeconds - 0.1, $"the second give came {gives[1] - gives[0]:0.00} s after the first, inside the speech");
    }

    [Fact]
    public void An_npc_that_stays_busy_stops_the_step_by_name_after_the_last_give()
    {
        var rig = Rig();
        rig.OnGive[Npc] = item => rig.Refused(item, QuestReads.BusyCode, NpcName);

        var (run, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("give:refused:busy", ((QuestRun)run).FailName);
        Assert.Equal(QuestRun.BusyGives - 1, rig.Lines("recovery=give-after-busy").Count());
        Assert.Equal(QuestRun.BusyGives, rig.Events.Count(e => e.StartsWith("give:", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_refusal_fails_the_step_by_name_and_keeps_the_item()
    {
        var rig = Rig();
        rig.OnGive[Npc] = item => rig.Refused(item, QuestReads.NotWantedCode, NpcName);

        var (run, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("steps=2/3 done=no", Assert.Single(rig.Lines("quest: key=")), StringComparison.Ordinal);
        Assert.Equal("give:refused:not-wanted", ((QuestRun)run).FailName);
        Assert.Contains(rig.Lines("quest_step: n=2 kind=give"), l => l.Contains("ok=no", StringComparison.Ordinal) && l.Contains("fail=give:refused:not-wanted", StringComparison.Ordinal));
        Assert.Single(rig.Events, e => e.StartsWith("give:", StringComparison.Ordinal)); // never given again
    }

    [Fact]
    public void An_expected_item_must_arrive_one_already_held_does_not_count()
    {
        var rig = Rig(gemHeldBefore: true, gives: false);

        var (run, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(TurnIn()), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("give:no-gift", ((QuestRun)run).FailName);
    }

    [Fact]
    public void A_hunt_until_whose_count_is_held_is_done_at_once_and_runs_no_fight()
    {
        var rig = Rig();
        var fights = 0;
        QuestStep[] steps =
        [
            QuestRig.Step(0, QuestStepKind.HuntUntil, "a female tusk", item: new QuestThing(TuskWcid, "Female Tusker Tusk"), count: 1, ground: FightOptions.DefaultGround),
            .. TurnIn().Select(s => s with { Index = s.Index + 1 }),
        ];

        var (_, status) = rig.Run(new QuestRun(rig.Context(), TuskQuest(steps), JourneyPlans.FromHome, (_, _) => { fights++; throw new InvalidOperationException("no fight"); }));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(0, fights);
        Assert.Single(rig.Lines("held already: done at once, no hunt"));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=hunt-until"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("held=1/1 hunted=no", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hunt_until_runs_the_fight_with_the_contract_arguments_and_checks_the_count_arrived()
    {
        var rig = Rig();
        rig.R.Raw(AceM3Writers.InventoryRemoveObject(Tusk)); // no tusk held: the hunt runs
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        rig.Put(ground.Arrival.Cell, ground.Arrival.Origin);
        StationSpec? seen = null;
        var steps = new[] { QuestRig.Step(0, QuestStepKind.HuntUntil, "a female tusk", item: new QuestThing(TuskWcid, "Female Tusker Tusk"), count: 1, ground: ground.Key) };
        var quest = QuestRig.Quest("test-hunt", new QuestDone(null, null, new QuestThing(TuskWcid, "Female Tusker Tusk")), null, steps);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, (spec, _) =>
        {
            seen = spec;
            FakeFight? fight = null;
            fight = new FakeFight(n =>
            {
                if (n < 3)
                {
                    return false;
                }

                rig.PackItem(0x80000504, TuskWcid, "Female Tusker Tusk");
                fight!.Met = true;
                return true;
            });
            return fight;
        }));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.NotNull(seen);
        Assert.Equal(StationKinds.DenFight, seen!.Kind);
        Assert.Equal(ground.Key, seen.Arguments[JourneyStations.GroundArgument]);
        Assert.Equal("22423", seen.Arguments[JourneyStations.WantArgument]);
        Assert.Equal("item:22423x1", seen.Arguments[JourneyStations.UntilArgument]);
        Assert.True(seen.Arguments.ContainsKey(JourneyStations.OpenEndedArgument));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=hunt-until"), l => l.Contains("held=1/1 hunted=yes arrived=1 until=met", StringComparison.Ordinal));
        Assert.Contains("done=yes", Assert.Single(rig.Lines("quest: key=test-hunt")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_confirm_step_answers_its_question_by_the_rule_and_no_to_an_unasked_one()
    {
        var rig = Rig();
        rig.OnUse[Npc] = () =>
        {
            rig.Tell(Npc, NpcName, Intro);
            rig.After(0.5, () =>
            {
                rig.Question(5, 0x22, "Combine these?");
                rig.Question(7, 0x11, "Do you wish to join the resistance?");
            });
        };
        var steps = new[]
        {
            QuestRig.Step(0, QuestStepKind.Talk, "her intro", target: Brighteyes, expect: new QuestExpect("reward you well", null, null)),
            QuestRig.Step(1, QuestStepKind.Confirm, "join", expect: new QuestExpect("join the resistance", null, null), answer: true),
        };
        var quest = QuestRig.Quest("test-confirm", new QuestDone(null, "reward you well", null), null, steps);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("confirm:7:yes", rig.Events);
        Assert.Contains("confirm:5:no", rig.Events);
        Assert.Contains(rig.Lines("quest_step: n=1 kind=confirm"), l => l.Contains("answered=yes", StringComparison.Ordinal));
        Assert.Empty(rig.R.H.State.PendingConfirmations);
    }

    // ---- M6 8 A: combine, apply, and the pick-up from a container

    private const uint MoteWcid = 6353;
    private const uint SliverWcid = 6355;
    private const string SliverLine = "You combine the two motes into a larger sliver.";
    private const uint Chest = 0x7F682100;
    private const uint ChestWcid = 40001;
    private const uint Loot = 0x80000701;
    private const uint LootWcid = 40002;

    private static QuestThing MoteThing => new(MoteWcid, "Pyreal Mote");

    private static QuestThing SliverThing => new(SliverWcid, "Pyreal Sliver");

    private static QuestThing ChestThing => new(ChestWcid, "a chest");

    private static QuestThing LootThing => new(LootWcid, "a prize");

    private static uint MoteGuid(int i) => 0x80000600u + (uint)i;

    private static QuestRig MoteRig(int motes)
    {
        var rig = new QuestRig();
        for (var i = 0; i < motes; i++)
        {
            rig.PackItem(MoteGuid(i), MoteWcid, "Pyreal Mote");
        }

        return rig;
    }

    private static void Combines(QuestRig rig)
    {
        var next = 0x80000680u;
        rig.OnRecipe = (source, target) =>
        {
            rig.Removed(target);
            rig.Removed(source);
            rig.PackItem(next++, SliverWcid, "Pyreal Sliver");
            rig.System(SliverLine);
            rig.R.UseDone();
        };
    }

    private static Quest MoteQuest(QuestStepKind kind = QuestStepKind.Combine, int? count = 1, QuestThing? failItem = null, string? failReply = null) =>
        QuestRig.Quest("test-motes", new QuestDone(null, null, SliverThing), null, QuestRig.Step(0, kind, "two motes", target: MoteThing, item: MoteThing, expect: new QuestExpect(SliverLine, null, SliverThing), count: count, failItem: failItem, failReply: failReply));

    private static (QuestRig Rig, ScenarioStatus Status) RunMotes(QuestRig rig, Quest quest)
    {
        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));
        return (rig, status);
    }

    [Fact]
    public void A_combine_repeats_its_count_on_two_different_guids_each_time()
    {
        var rig = MoteRig(4);
        Combines(rig);

        var (_, status) = RunMotes(rig, MoteQuest(count: 2));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var uses = rig.Events.Where(e => e.StartsWith("recipe:", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, uses.Count);
        Assert.All(uses, u => Assert.NotEqual(u["recipe:".Length..].Split('@')[0], u.Split('@')[1]));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=combine"), l => l.Contains("ok=yes", StringComparison.Ordinal) && l.Contains("outcome=success uses=2/2 consumed=4 created=2", StringComparison.Ordinal));
        Assert.Equal(2, rig.Lines("combine: check before: use ").Count);
        Assert.Equal(2, rig.Lines("combine: act: use ").Count);
        Assert.Contains(rig.Lines("combine: check after: use 2/2"), l => l.Contains("outcome=success", StringComparison.Ordinal) && l.Contains("result 1 -> 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_combine_short_of_two_objects_per_use_spends_nothing_and_stops_by_name()
    {
        var rig = MoteRig(3);
        Combines(rig);

        var (_, status) = RunMotes(rig, MoteQuest(count: 2));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.Contains("fail=combine:short", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_object_of_the_class_is_never_used_on_itself()
    {
        var rig = MoteRig(1);
        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
    }

    [Fact]
    public void Peace_mode_is_set_and_verified_before_the_first_use()
    {
        var rig = MoteRig(2);
        Combines(rig);
        rig.R.CombatModeIs(CombatModes.Magic);

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("mode:" + CombatModes.NonCombat, rig.Acts[0]);
        Assert.StartsWith("recipe:", rig.Acts[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_mode_that_stays_combat_stops_before_the_use_is_sent()
    {
        var rig = MoteRig(2);
        rig.R.CombatModeIs(CombatModes.Magic);
        rig.ModeIgnored = true;

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.Contains("fail=combine:combat-mode", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_use_still_in_flight_stops_by_busy_and_sends_no_second_one()
    {
        var rig = MoteRig(2);
        rig.R.Api.UseRecipe(MoteGuid(0), MoteGuid(1)); // an earlier use the server has not ended

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Single(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.Contains("fail=combine:busy", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_server_refusing_a_craft_in_combat_stops_by_that_name()
    {
        var rig = MoteRig(2);
        rig.OnRecipe = (_, _) => rig.R.UseDone(0x043A);

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=combine:combat-mode", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
        Assert.Contains(rig.Lines("quest_step: n=0 kind=combine"), l => l.Contains("outcome=combat-mode", StringComparison.Ordinal) && l.Contains("ok=no", StringComparison.Ordinal));
    }

    [Fact]
    public void An_untrained_refusal_stops_by_name_and_is_not_retried()
    {
        var rig = MoteRig(2);
        rig.OnRecipe = (_, _) =>
        {
            rig.R.WeenieError(0x043B);
            rig.R.UseDone();
        };

        var (_, status) = RunMotes(rig, MoteQuest(QuestStepKind.Apply, failReply: "You fail."));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Single(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.Contains("fail=apply:untrained", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_roll_stops_and_says_what_was_lost_and_never_continues()
    {
        const uint Dross = 6330;
        var rig = MoteRig(4);
        rig.OnRecipe = (source, target) =>
        {
            rig.Removed(target);
            rig.Removed(source);
            rig.PackItem(0x80000690, Dross, "Pyreal Ingot");
            rig.System("You fail.");
            rig.R.UseDone();
        };

        var (_, status) = RunMotes(rig, MoteQuest(QuestStepKind.Apply, count: 2, failItem: new QuestThing(Dross, "Pyreal Ingot"), failReply: "You fail."));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Single(rig.Events, e => e.StartsWith("recipe:", StringComparison.Ordinal));
        Assert.Contains("fail=apply:failed-roll", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
        Assert.Contains(rig.Lines("PROBLEM"), l => l.Contains("lost 1 Pyreal Mote (6353), 1 Pyreal Mote (6353)", StringComparison.Ordinal) && l.Contains("arrived 1 Pyreal Ingot (6330)", StringComparison.Ordinal));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=apply"), l => l.Contains("outcome=failed-roll uses=0/2 consumed=2 created=1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kept_source_reads_as_a_success_from_the_result_alone()
    {
        const uint StoneGuid = 0x80000610;
        const uint WandGuid = 0x80000611;
        var rig = new QuestRig();
        rig.PackItem(StoneGuid, 6320, "Minor Smoldering Stone");
        rig.PackItem(WandGuid, 46122, "Atlan Wand");
        const string Line = "The gem slips into the weapon altering its properties.";
        var stoned = new QuestThing(46128, "Minor Smoldering Atlan Wand");
        rig.OnRecipe = (_, target) =>
        {
            rig.Removed(target);
            rig.PackItem(0x80000612, 46128, "Minor Smoldering Atlan Wand");
            rig.System(Line);
            rig.R.UseDone();
        };
        var quest = QuestRig.Quest("test-stone", new QuestDone(null, null, stoned), null, QuestRig.Step(0, QuestStepKind.Apply, "the stone", target: new QuestThing(46122, "Atlan Wand"), item: new QuestThing(6320, "Minor Smoldering Stone"), expect: new QuestExpect(Line, null, stoned), failItem: stoned, failReply: Line));

        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(rig.R.H.State.Self.Inventory.Find(StoneGuid).Held);
        Assert.Contains(rig.Lines("apply: check after"), l => l.Contains("consumed=[1 Atlan Wand (46122)]", StringComparison.Ordinal));
    }

    [Fact]
    public void The_crafting_dialog_is_answered_yes_its_percent_is_logged_and_the_use_is_read_from_the_items()
    {
        var rig = MoteRig(2);
        rig.OnRecipe = (_, _) =>
        {
            rig.Question(5, 0x33, "You determine that you have a 73 percent chance to succeed.");
            rig.R.UseDone();
        };
        rig.OnConfirm = (type, yes) =>
        {
            Assert.Equal(5u, type);
            Assert.True(yes);
            rig.QuestionDone(5, 0x33);
            rig.Removed(MoteGuid(0));
            rig.Removed(MoteGuid(1));
            rig.PackItem(0x80000680, SliverWcid, "Pyreal Sliver");
            rig.System(SliverLine);
        };

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("confirm:5:yes", rig.Events);
        Assert.Single(rig.Lines("the crafting dialog: percent=73 "));
    }

    [Fact]
    public void An_item_gone_with_no_result_stops_no_answer_and_names_what_left()
    {
        var rig = MoteRig(2);
        rig.OnRecipe = (source, target) =>
        {
            rig.Removed(target);
            rig.Removed(source);
            rig.R.UseDone();
        };

        var (_, status) = RunMotes(rig, MoteQuest());

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=combine:no-answer", Assert.Single(rig.Lines("quest: key=test-motes")), StringComparison.Ordinal);
    }

    private static QuestRig ChestRig()
    {
        var rig = new QuestRig();
        rig.Npc(Chest, "a chest", ChestWcid, QuestRig.HomeCell, new Vector3(86, 8, 94));
        return rig;
    }

    private static Quest ChestQuest() =>
        QuestRig.Quest("test-chest", new QuestDone(null, null, LootThing), null, QuestRig.Step(0, QuestStepKind.PickUp, "the prize", target: ChestThing, item: LootThing));

    [Fact]
    public void A_pick_up_from_a_chest_opens_it_takes_the_item_and_closes_it()
    {
        var rig = ChestRig();
        rig.ChestItem(Loot, LootWcid, "a prize", Chest);
        rig.OnOpen[Chest] = () => rig.ChestOpens(Chest, Loot);
        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("use:" + Chest.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
        Assert.Contains("pick-up:" + Loot.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
        Assert.Contains("close:" + Chest.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
        Assert.Single(rig.Lines("pick-up: a prize arrived: held 0 -> 1"));
    }

    [Fact]
    public void A_pick_up_from_a_chest_refused_on_the_items_quest_timer_is_the_named_stop()
    {
        var rig = ChestRig();
        rig.ChestItem(Loot, LootWcid, "a prize", Chest);
        rig.OnOpen[Chest] = () => rig.ChestOpens(Chest, Loot);
        rig.RefusedPickUps.Add(Loot);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=pick-up:too-recent", Assert.Single(rig.Lines("quest: key=test-chest")), StringComparison.Ordinal);
        Assert.Contains("close:" + Chest.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
    }

    [Fact]
    public void A_floor_pick_up_refused_on_the_items_quest_timer_stops_by_name_without_a_second_try()
    {
        var rig = new QuestRig();
        rig.Npc(Loot, "a prize", LootWcid, QuestRig.HomeCell, new Vector3(86, 8, 94));
        rig.RefusedPickUps.Add(Loot);
        var quest = QuestRig.Quest("test-floor", new QuestDone(null, null, LootThing), null, QuestRig.Step(0, QuestStepKind.PickUp, "the prize", item: LootThing));

        var (_, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=pick-up:too-recent", Assert.Single(rig.Lines("quest: key=test-floor")), StringComparison.Ordinal);
        Assert.Single(rig.Events, e => e == "pick-up:" + Loot.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_locked_chest_is_a_named_stop_that_names_the_key_todo()
    {
        var rig = ChestRig();
        rig.ChestItem(Loot, LootWcid, "a prize", Chest);
        rig.OnOpen[Chest] = () => rig.ChestLocked(Chest);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=pick-up:locked", Assert.Single(rig.Lines("quest: key=test-chest")), StringComparison.Ordinal);
        Assert.Contains(rig.Lines("PROBLEM"), l => l.Contains("TODO(m6-quest-chest-key)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("pick-up:", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_chest_is_a_named_stop_and_is_closed_first()
    {
        var rig = ChestRig();
        rig.OnOpen[Chest] = () => rig.ChestOpens(Chest);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=pick-up:empty", Assert.Single(rig.Lines("quest: key=test-chest")), StringComparison.Ordinal);
        Assert.Contains("close:" + Chest.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
        Assert.Equal(0u, rig.R.H.State.Containers.ViewedGuid);
    }

    [Fact]
    public void A_chest_without_the_wanted_item_stops_by_name_and_is_closed_first()
    {
        var rig = ChestRig();
        rig.ChestItem(Loot, 40099, "something else", Chest);
        rig.OnOpen[Chest] = () => rig.ChestOpens(Chest, Loot);

        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=pick-up:not-in-container", Assert.Single(rig.Lines("quest: key=test-chest")), StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("pick-up:", StringComparison.Ordinal));
        Assert.Contains("close:" + Chest.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), rig.Events);
    }

    [Fact]
    public void An_item_held_already_leaves_the_chest_shut()
    {
        var rig = ChestRig();
        rig.PackItem(Loot, LootWcid, "a prize");

        var (_, status) = rig.Run(new QuestRun(rig.Context(), ChestQuest(), JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status); // the quest's done check wants it to arrive; the step itself did nothing
        Assert.DoesNotContain(rig.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
        Assert.Single(rig.Lines("is held already: the chest is left shut"));
        Assert.Contains(rig.Lines("quest_step: n=0 kind=pick-up"), l => l.Contains("ok=yes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_factory_line_makes_the_quest_station_and_refuses_a_spec_without_a_key()
    {
        var spec = new StationSpec("quest", StationKinds.Quest, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.QuestArgument] = "pathwarden", [JourneyPlans.FromArgument] = JourneyPlans.FromGround });
        var station = QuestStation.Of(spec, _ => throw new InvalidOperationException("not called"));
        Assert.Equal(("pathwarden", JourneyPlans.FromGround), (station.QuestKey, station.From));
        Assert.Throws<ArgumentException>(() => QuestStation.Of(spec with { Args = new Dictionary<string, string>(StringComparer.Ordinal) }, _ => throw new InvalidOperationException()));
        Assert.Throws<ArgumentException>(() => new QuestStation("pathwarden", "town"));
    }

    [Fact]
    public void A_talk_reads_its_gift_as_arrived_one_already_held_does_not_count()
    {
        var rig = Rig(gemHeldBefore: true, gives: false);
        var gemTalk = QuestRig.Step(1, QuestStepKind.Talk, "her gift", target: Brighteyes, expect: new QuestExpect("reward you well", null, new QuestThing(GemWcid, "Aphus Lassel Portal Gem")));
        var quest = TuskQuest(QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes), gemTalk);

        var (run, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("talk:no-gift", ((QuestRun)run).FailName);
    }

    [Fact]
    public void A_quest_its_timers_say_is_not_due_is_refused_by_name_before_any_walk()
    {
        var rig = Rig();
        var context = rig.Context(new MemoryCheckpointStore());
        var quest = TuskQuest(TurnIn());
        var (_, first) = rig.Run(new QuestRun(context, quest, JourneyPlans.FromHome, null));
        Assert.Equal(ScenarioStatus.Succeeded, first);
        var walks = rig.Travel.Requests.Count;

        var (second, status) = rig.Run(new QuestRun(context, quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("start:not-due", ((QuestRun)second).FailName);
        Assert.Equal(walks, rig.Travel.Requests.Count);
        Assert.Equal(1, rig.Lines("quest_step: n=").Count(l => l.Contains("n=0 ", StringComparison.Ordinal)) ); // only the first run's step 0
    }

    private static QuestStep ArmTalk() =>
        QuestRig.Step(1, QuestStepKind.Talk, "the task", target: Brighteyes, expect: new QuestExpect("Track down and slay", null, null)) with { ArmedReply = "haven't completed your task", WaitReply = "come back again" };

    [Fact]
    public void A_talk_accepts_the_already_armed_reply_as_the_arm()
    {
        var rig = Rig();
        rig.OnUse[Npc] = () => rig.Tell(Npc, NpcName, "I see you haven't completed your task.");
        var quest = QuestRig.Quest("test-armed", new QuestDone(null, "haven't completed", null), 72000, ArmTalk() with { Index = 0 });

        var (run, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(((QuestRun)run).FailName);
        Assert.Single(rig.Lines("says the task is already armed"));
    }

    [Fact]
    public void A_talk_that_hears_the_wait_reply_stops_on_timer_by_name_and_never_asks_again()
    {
        var rig = Rig();
        rig.OnUse[Npc] = () => rig.Tell(Npc, NpcName, "Ah, friend, come back again tomorrow.");
        var quest = QuestRig.Quest("test-wait", new QuestDone(null, "Track down", null), 72000, ArmTalk() with { Index = 0 });

        var (run, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("talk:on-timer", ((QuestRun)run).FailName);
        Assert.Single(rig.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hunt_until_whose_fight_ends_refused_stops_by_name_and_records_nothing()
    {
        var rig = Rig();
        rig.R.Raw(AceM3Writers.InventoryRemoveObject(Tusk)); // no tusk held: the hunt runs
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        rig.Put(ground.Arrival.Cell, ground.Arrival.Origin);
        var store = new MemoryCheckpointStore();
        var steps = new[] { QuestRig.Step(0, QuestStepKind.HuntUntil, "a female tusk", item: new QuestThing(TuskWcid, "Female Tusker Tusk"), count: 1, ground: ground.Key) };
        var quest = QuestRig.Quest("test-refused", new QuestDone(null, null, new QuestThing(TuskWcid, "Female Tusker Tusk")), 72000, steps);

        var (run, status) = rig.Run(new QuestRun(rig.Context(store), quest, JourneyPlans.FromHome, (_, _) => new FakeFight(n => n >= 2) { Refused = true }));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("hunt-until:refused:22423", ((QuestRun)run).FailName);
        Assert.Null(store.Json); // nothing recorded: the timer is untouched
        Assert.Contains("done=no", Assert.Single(rig.Lines("quest: key=test-refused")), StringComparison.Ordinal);
    }
}
