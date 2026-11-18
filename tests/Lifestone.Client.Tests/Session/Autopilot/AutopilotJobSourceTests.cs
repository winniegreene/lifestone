using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M6 6 MD2: the job menu's source (the host's): what it reads, when it raises Changed, what it logs once per change.</summary>
public sealed class AutopilotJobSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static Quest Make(string key, QuestLevel? level, QuestExperience? xp, int? timer = null) => new()
    {
        Key = key,
        Name = key.ToUpperInvariant(),
        Giver = null,
        Start = new QuestPlace(0, null, "somewhere"),
        Steps = [new QuestStep { Index = 1, Kind = QuestStepKind.Go, What = "go", Place = null, Target = null, Item = null, Expect = new QuestExpect(null, null, null), Source = "test", Todo = null }],
        Done = new QuestDone(null, "done", null),
        TimerSeconds = timer,
        Sources = ["test"],
        Level = level,
        Experience = xp,
    };

    private sealed class Rig
    {
        public int Level { get; set; } = 10;

        public DateTimeOffset At { get; set; } = Now;

        public long? NextXp { get; set; } = 1_000_000;

        public AutopilotTask Task { get; set; } = AutopilotTask.Job;

        public QuestTimers Timers { get; } = new("Winnie");

        public List<string> Lines { get; } = [];

        public int Raised { get; private set; }

        public AutopilotJobSource Make(IReadOnlyList<Quest> quests, params string[] ladder)
        {
            var source = new AutopilotJobSource(AutopilotJob.LevelUp, () => Task, () => Level, _ => NextXp, quests, () => Timers, () => At, ladder, new Sink(Lines));
            source.Changed += () => Raised++;
            return source;
        }
    }

    private sealed class Sink(List<string> lines) : ILogSink
    {
        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => lines.Add(message);
    }

    private static readonly Quest Daily = Make("tusk-daily", new QuestLevel(5, 20), new QuestExperience(null, QuestExperienceKind.Proportional, null, 0.5, "test"), timer: 72_000);
    private static readonly Quest FixedXp = Make("fixed", new QuestLevel(1, null), new QuestExperience(2_000, QuestExperienceKind.Fixed, null, null, "test"), timer: 60);

    [Fact]
    public void Next_level_xp_is_the_tables_step_and_null_when_unknown()
    {
        ulong[] table = [0, 0, 1_000, 2_500, 5_000];
        Assert.Equal(1_000, AutopilotJobSource.NextLevelXp(table, 1));
        Assert.Equal(1_500, AutopilotJobSource.NextLevelXp(table, 2));
        Assert.Null(AutopilotJobSource.NextLevelXp(table, 4)); // the cap
        Assert.Null(AutopilotJobSource.NextLevelXp(table, 0)); // no level yet
        Assert.Null(AutopilotJobSource.NextLevelXp(null, 3)); // no XpTable
    }

    [Fact]
    public void The_first_refresh_logs_the_summary_and_each_row_once_and_raises_changed()
    {
        var rig = new Rig();
        var source = rig.Make([Daily, FixedXp]);
        Assert.True(source.Refresh());
        Assert.Equal(1, rig.Raised);
        Assert.Equal("autopilot menu: level=10 next_xp=1000000 listed=2/2 refused=none", rig.Lines[0]);
        Assert.Equal(["level up", "do a quest: TUSK-DAILY", "do a quest: FIXED", "errand", "stop"], source.Jobs.Jobs.Select(e => e.Label));
        Assert.Equal(source.Jobs.Jobs.Count + 1, rig.Lines.Count);
        Assert.Contains("autopilot menu: job=quest:tusk-daily xp=500000 why=", rig.Lines[2], StringComparison.Ordinal);

        Assert.False(source.Refresh()); // nothing moved: no line, no event
        Assert.Equal(1, rig.Raised);
        Assert.Equal(source.Jobs.Jobs.Count + 1, rig.Lines.Count);
        Assert.Equal(1, source.Changes);
    }

    [Fact]
    public void A_level_change_rebuilds_logs_and_raises()
    {
        var rig = new Rig();
        var source = rig.Make([Daily, FixedXp]);
        source.Refresh();
        rig.Level = 21;
        Assert.True(source.Refresh());
        Assert.Equal(2, rig.Raised);
        Assert.Equal(21, source.Jobs.Level);
        Assert.Contains("autopilot menu: level=21 next_xp=1000000 listed=1/2 refused=tusk-daily:level-out-of-band", rig.Lines);
    }

    [Fact]
    public void A_task_change_raises_without_a_new_summary()
    {
        var rig = new Rig();
        var source = rig.Make([Daily]);
        source.Refresh();
        var lines = rig.Lines.Count;
        rig.Task = AutopilotTask.Errand;
        Assert.True(source.Refresh());
        Assert.Equal(2, rig.Raised);
        Assert.Equal(AutopilotTask.Errand, source.Current);
        Assert.Equal(lines, rig.Lines.Count);
        Assert.Equal(1, source.Changes);
    }

    [Fact]
    public void A_timer_set_refuses_the_quest_not_due_and_raises()
    {
        var rig = new Rig();
        var source = rig.Make([Daily]);
        source.Refresh();
        rig.Timers.Complete(Daily, Now.AddHours(-1), QuestTimers.HowReply);
        Assert.True(source.Refresh());
        Assert.Equal(2, rig.Raised);
        var refused = Assert.Single(source.Jobs.Refused);
        Assert.StartsWith("not-due:", refused.Refusal, StringComparison.Ordinal);
        Assert.Contains(rig.Lines, l => l.StartsWith("autopilot menu: level=10 next_xp=1000000 listed=0/1 refused=tusk-daily:not-due:", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_read_of_the_rows_is_built_against_the_clock_so_a_quest_comes_due()
    {
        var rig = new Rig();
        var source = rig.Make([Daily]);
        rig.Timers.Complete(Daily, Now.AddHours(-1), QuestTimers.HowReply);
        Assert.Single(source.Jobs.Refused);
        rig.At = Now.AddDays(1); // the 20-hour timer ran out: no Refresh call, only a read
        Assert.Empty(source.Jobs.Refused);
        Assert.Contains(source.Jobs.Jobs, e => e.Task == AutopilotTask.ForQuest("tusk-daily"));
        Assert.Equal(2, rig.Raised);
        Assert.Equal(2, source.Changes);
    }

    [Fact]
    public void An_unknown_next_level_shows_proportional_xp_unknown_and_names_the_todo_once()
    {
        var rig = new Rig { NextXp = null };
        var source = rig.Make([Daily, FixedXp]);
        source.Refresh();
        Assert.Null(source.Jobs.Jobs.Single(e => e.Task.QuestKey == "tusk-daily").Experience);
        Assert.Equal(2_000, source.Jobs.Jobs.Single(e => e.Task.QuestKey == "fixed").Experience);
        rig.Level = 11;
        source.Refresh();
        Assert.Single(rig.Lines, l => l.Contains("TODO(" + AutopilotJobSource.XpUnknownTodo + ")", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ladder_keys_are_the_war_mage_quest_rungs_and_are_refused()
    {
        var keys = AutopilotJobSource.LadderQuestKeys(LadderLoader.WarMage);
        var expected = LadderLoader.WarMage.Rungs.Where(r => r.Kind == LadderRungKind.Quest).SelectMany(r => r.QuestKeys).Distinct().ToArray();
        Assert.NotEmpty(keys);
        Assert.Equal(expected.Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));

        var rig = new Rig();
        var source = rig.Make([Daily], "tusk-daily");
        Assert.Equal("ladder-quest", Assert.Single(source.Jobs.Refused).Refusal);
    }

    [Fact]
    public void The_input_is_the_rows_own_and_null_while_the_level_is_unread()
    {
        // M6 6 FXU (6-M9): the dial's quest judgement reads the same input the rows are built from.
        var rig = new Rig { Level = 0 };
        var source = rig.Make([Daily, FixedXp], "fixed");
        Assert.Null(source.BuildInput());

        rig.Level = 10;
        var input = Assert.IsType<JobMenuInput>(source.BuildInput());
        Assert.Equal((AutopilotJob.LevelUp, 10, 1_000_000L, Now), (input.Job, input.Level, input.NextLevelXp, input.Now));
        Assert.Same(rig.Timers, input.Timers);
        Assert.Equal(["tusk-daily", "fixed"], input.Quests.Select(q => q.Key));
        Assert.Contains("fixed", input.LadderQuestKeys);
        Assert.Equal(JobMenu.SummaryLine(JobMenu.Build(input), 1_000_000, 2), JobMenu.SummaryLine(source.Jobs, 1_000_000, 2));

        rig.NextXp = null;
        Assert.Equal(0, source.BuildInput()!.NextLevelXp); // unknown: a proportional award prices nothing
    }

    [Fact]
    public void A_dial_given_the_sources_input_refuses_a_ladder_quest_and_judges_nothing_before_the_level()
    {
        var lines = new List<string>();
        var level = 0;
        var source = new AutopilotJobSource(AutopilotJob.LevelUp, () => AutopilotTask.Job, () => level, _ => 1_000_000, QuestCatalog.All, () => new QuestTimers("Winnie"), () => Now, ["tusk-female"], new Sink(lines));
        var logic = new AutopilotHostLogic(_ => throw new InvalidOperationException("no run in this test"), new Sink(lines), new AutopilotOptions()) { MenuInput = source.BuildInput };
        var tusks = AutopilotTask.ForQuest("tusk-female");

        level = 15;
        Assert.Equal("ladder-quest", logic.UseTask(tusks, AutopilotHostLogic.TaskByChat));
        Assert.Equal(AutopilotTask.Job, logic.Task);

        level = 0; // the state has no level yet: nothing to judge by, the scenario's own checks stand
        Assert.Null(logic.UseTask(tusks, AutopilotHostLogic.TaskByChat));
        Assert.Equal(tusks, logic.Task);
    }
}
