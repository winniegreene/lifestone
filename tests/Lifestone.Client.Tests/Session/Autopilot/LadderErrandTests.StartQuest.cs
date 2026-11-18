using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 the start quest (the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once)): <c>--autopilot-start-quest=&lt;key&gt;</c> parses as a key of the quest catalog and only for the level up job's own loop; over the
/// scripted ACE the errand runs at the run's start with no trip from kills (the first station after the start is the quest, before any fight), the ladder resumes after it (a fight follows), it is asked once a run, and a level gate
/// refuses it by name. Not proved: the live server's answers (the lead's run).
/// </summary>
public sealed partial class LadderErrandTests
{
    [Fact]
    public void The_start_quest_option_parses_a_catalog_key_for_the_level_up_job_only()
    {
        var ok = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", "--autopilot-start-quest=atlan-wand"]));
        Assert.Empty(ok.Errors);
        Assert.Equal("atlan-wand", ok.StartQuest);

        var unknown = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", "--autopilot-start-quest=no-such-quest"]));
        Assert.Contains(unknown.Errors, e => e.StartsWith("--autopilot-start-quest: 'no-such-quest' is no quest of the catalog", StringComparison.Ordinal));

        var task = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", "--autopilot-task=errand", "--autopilot-start-quest=atlan-wand"]));
        Assert.Contains(task.Errors, e => e.Contains("is the level up job's own loop's", StringComparison.Ordinal));
        Assert.NotEqual(default, OptionCatalog.Registry.ScenesOf(AutopilotOptions.StartQuestOption)); // registered
    }

    [Fact]
    public void The_chats_level_up_quest_names_the_start_quest_and_an_unknown_key_starts_nothing()
    {
        Assert.Equal("job+quest:atlan-wand", new AutopilotTask(AutopilotTaskKind.Job, "atlan-wand").Name);
        Assert.Equal("job", AutopilotTask.Job.Name);
        Assert.Contains("level-up quest <key>", ChatCommands.AutoUsage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_start_quest_runs_at_the_runs_start_with_no_trip_from_kills_and_the_ladder_resumes_after()
    {
        var quest = Errand();
        var timers = new QuestTimers("Winnie");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, spec => Completes(spec, quest, timers), timers, options: new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key });
        autopilot.TrophyQuests = [quest];
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var kinds = Kinds(w);
        var at = Array.IndexOf(kinds, "quest");
        Assert.True(at >= 0, string.Join(",", kinds));
        Assert.DoesNotContain("den-fight", kinds.Take(at)); // no fight, so no trip from kills, before it
        Assert.Contains("den-fight", kinds.Skip(at + 1)); // the ladder resumes: the fight follows the errand
        Assert.Single(Quests(w));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: start-quest: key={Key} level=17: ", StringComparison.Ordinal) && l.Contains("no trip from kills", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.LadderErrands);
    }

    [Fact]
    public void A_start_quest_out_of_its_band_is_refused_by_name_and_the_ladder_goes_on()
    {
        var quest = Errand(from: 30, before: 40); // she is 17
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, _ => throw new InvalidOperationException("no errand runs"), options: new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key });
        autopilot.TrophyQuests = [quest];
        w.Run(autopilot, a => a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(Quests(w));
        Assert.Single(w.Lines, l => l.StartsWith($"autopilot quest: start-quest: refused: key={Key} level=17: level-out-of-band: its ladder band is 30 to before 40", StringComparison.Ordinal));
    }
    [Fact]
    public void The_start_quest_is_asked_once_a_process_run_a_move_on_restarts_hand_over_carries_it_and_the_quests_failed_M6_14_motes()
    {
        // Live 2026-11-17 00:08:52: the Atlan wand's start quest failed and was marked "not called again this run"; the move-on's restart one minute later asked it again from home ("the handed-over pass stands").
        var quest = Errand();
        var timers = new QuestTimers("Winnie");
        var options = new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key };
        var (w, first) = TrophyQuestTests.MakeClimber(output, tusk: false, spec => Completes(spec, quest, timers), timers, options: options);
        first.TrophyQuests = [quest];
        w.Run(first, a => a.LadderErrands > 0 && a.State == AutopilotState.Fight, 20000);
        first.RequestStop("test");
        w.Run(first, null, 20000);
        Assert.Single(Quests(w));
        var handOver = first.HandOver;
        Assert.True(handOver.StartQuestAsked);

        var second = new AutopilotScenario(options, w.Factory, w.Context(), handOver: handOver) { TrophyQuests = [quest], MinStintSeconds = first.MinStintSeconds, BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4) };
        w.Run(second, a => a.State == AutopilotState.Fight, 20000);
        second.RequestStop("test");
        w.Run(second, null, 20000);

        Assert.Single(Quests(w)); // not asked again by the restart
        Assert.Single(w.Lines, l => l.StartsWith($"autopilot quest: start-quest: key={Key} level=17: ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l == $"autopilot quest: start-quest: key={Key}: not asked again: a run before this one in the same chain of restarts asked it (the hand-over); the ladder's own ways call its errand by their rules");
        Assert.True(second.HandOver.StartQuestAsked); // and on down the chain

        // A quest the chain marked failed goes with the hand-over: the next run refuses it by the same bookkeeping, by name (a fresh climber, so the quest is not recorded done).
        var (w2, _) = TrophyQuestTests.MakeClimber(output, tusk: false, _ => throw new InvalidOperationException("no errand runs"), timers: new QuestTimers("Winnie"), options: options);
        var failed = new AutopilotHandOver(w2.Trip.Vendor.State.Self.Guid, handOver.Ground, "rung-moved:quarters", 0, QuestsFailed: new Dictionary<string, string> { [Key] = "failed:quest:" + Key + ":hunt-until:short:0/16" });
        var third = new AutopilotScenario(options, w2.Factory, w2.Context(), handOver: failed) { TrophyQuests = [quest], MinStintSeconds = first.MinStintSeconds, BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4) };
        w2.Run(third, a => a.State == AutopilotState.Fight, 20000);
        third.RequestStop("test");
        w2.Run(third, null, 20000);

        Assert.Empty(Quests(w2));
        Assert.Contains(w2.Lines, l => l == $"autopilot quest: key={Key}: failed earlier in this process run (failed:quest:{Key}:hunt-until:short:0/16), handed over: not called again this run");
        Assert.Contains(w2.Lines, l => l.StartsWith($"autopilot quest: start-quest: refused: key={Key} level=17: failed-this-run: failed:quest:{Key}:hunt-until:short:0/16", StringComparison.Ordinal));
        Assert.Equal(new[] { Key }, third.HandOver.QuestsFailed!.Keys);
    }
}
