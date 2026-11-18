using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M6 6 MA: the job list as pure data (the task names, the option, the menu's filter, order and refusals).</summary>
public sealed class AutopilotJobsTests
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

    private static QuestExperience Fixed(long amount) => new(amount, QuestExperienceKind.Fixed, null, null, "test");

    private static JobList Build(IReadOnlyList<Quest> quests, QuestTimers? timers = null, AutopilotJob job = AutopilotJob.LevelUp, int level = 10, params string[] ladder) =>
        JobMenu.Build(new JobMenuInput(job, level, 1_000_000, quests, timers ?? new QuestTimers("Winnie"), Now, ladder));

    [Theory]
    [InlineData("job", AutopilotTaskKind.Job, null)]
    [InlineData("errand", AutopilotTaskKind.Errand, null)]
    [InlineData("quest:tusk-female", AutopilotTaskKind.Quest, "tusk-female")]
    public void Task_names_round_trip(string text, AutopilotTaskKind kind, string? key)
    {
        Assert.True(AutopilotTask.TryParse(text, out var task, out var error));
        Assert.Null(error);
        Assert.Equal(new AutopilotTask(kind, key), task);
        Assert.Equal(text, task.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("quest:")]
    [InlineData("quest:Tusk Female")]
    [InlineData("level-up")]
    [InlineData("stop")]
    public void Task_parse_refuses_with_a_named_error(string text)
    {
        Assert.False(AutopilotTask.TryParse(text, out var task, out var error));
        Assert.Equal(AutopilotTask.Job, task);
        Assert.Contains("'" + text + "'", error);
    }

    [Fact]
    public void Option_reads_the_task_and_defaults_to_job()
    {
        Assert.Equal(AutopilotTask.Job, AutopilotOptions.FromArguments(CommandLineArgument.ParseAll([])).Task);
        Assert.Equal(AutopilotTask.Errand, AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-task=errand"])).Task);
        Assert.Equal(AutopilotTask.ForQuest("tusk-female"), AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-task=quest:tusk-female"])).Task);
    }

    [Theory]
    [InlineData("--autopilot-task=dance")]
    [InlineData("--autopilot-task=quest:no-such-quest")]
    public void Option_refuses_an_unknown_value_loudly(string argument)
    {
        var options = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll([argument]));
        Assert.Contains(options.Errors, e => e.StartsWith("--autopilot-task:", StringComparison.Ordinal));
        Assert.Equal(AutopilotTask.Job, options.Task);
    }

    [Fact]
    public void Level_up_row_names_the_round_todo_and_the_den_row_reads_hunt_here()
    {
        var ladder = Build([]);
        Assert.Equal(["level up", "errand", "stop"], ladder.Jobs.Select(e => e.Label));
        Assert.Contains("TODO(m6-menu-round)", ladder.Jobs[0].Why);
        Assert.Equal(AutopilotTask.Job, ladder.Jobs[0].Task);

        var den = Build([], job: AutopilotJob.Den);
        Assert.Equal("hunt here", den.Jobs[0].Label);
        Assert.DoesNotContain("TODO", den.Jobs[0].Why);
        Assert.Equal(AutopilotTask.Errand, den.Jobs[1].Task);
        Assert.Equal("autopilot menu: job=stop xp=? why=stop the run where it stands", den.Jobs[^1].Line);
    }

    [Fact]
    public void Each_refusal_is_named_and_kept()
    {
        var timers = new QuestTimers("Winnie");
        var once = Make("once", new QuestLevel(1, null), Fixed(1));
        var daily = Make("daily", new QuestLevel(1, null), Fixed(1), timer: 72000);
        timers.Complete(once, Now.AddHours(-1), QuestTimers.HowReply);
        timers.Complete(daily, Now.AddHours(-1), QuestTimers.HowReply);
        var list = Build(
            [Make("nolevel", null, Fixed(1)), Make("band", new QuestLevel(40, null), Fixed(1)), once, daily, Make("ladderq", new QuestLevel(1, null), Fixed(1)), Make("ok", new QuestLevel(1, 20), Fixed(1))],
            timers,
            ladder: "ladderq");
        var refusals = list.Refused.ToDictionary(e => e.Task.QuestKey!, e => e.Refusal);
        Assert.Equal("no-level", refusals["nolevel"]);
        Assert.Equal("level-out-of-band", refusals["band"]);
        Assert.Equal("done-once", refusals["once"]);
        Assert.Equal("not-due:2026-11-05T07:00:00Z", refusals["daily"]);
        Assert.Equal("ladder-quest", refusals["ladderq"]);
        Assert.Equal(5, list.Refused.Count);
        Assert.All(list.Refused, e => Assert.False(e.Enabled));
        Assert.Equal(["quest:ok"], list.Jobs.Where(e => e.Task.Kind == AutopilotTaskKind.Quest).Select(e => e.Task.Name));
        Assert.True(list.Jobs.All(e => e.Enabled));
    }

    [Fact]
    public void A_quest_due_again_after_its_timer_is_listed()
    {
        var timers = new QuestTimers("Winnie");
        var daily = Make("daily", new QuestLevel(1, null), Fixed(1), timer: 3600);
        timers.Complete(daily, Now.AddHours(-2), QuestTimers.HowReply);
        Assert.Contains(Build([daily], timers).Jobs, e => e.Task.QuestKey == "daily");
    }

    [Fact]
    public void Quests_order_by_experience_descending_then_key_and_unknown_experience_last()
    {
        var level = new QuestLevel(1, null);
        var proportional = new QuestExperience(null, QuestExperienceKind.Proportional, null, 0.25, "test");
        var list = Build([Make("b", level, Fixed(500)), Make("a", level, Fixed(500)), Make("none", level, null), Make("big", level, Fixed(900)), Make("share", level, proportional)]);
        Assert.Equal(["share", "big", "a", "b", "none"], list.Jobs.Where(e => e.Task.Kind == AutopilotTaskKind.Quest).Select(e => e.Task.QuestKey));
        Assert.Equal(250_000, list.Jobs.Single(e => e.Task.QuestKey == "share").Experience);
        Assert.Equal("autopilot menu: job=quest:none xp=? why=NONE, level 1+", list.Jobs.Single(e => e.Task.QuestKey == "none").Line);
        Assert.Equal("autopilot menu: job=quest:big xp=900 why=BIG, level 1+", list.Jobs.Single(e => e.Task.QuestKey == "big").Line);
    }

    [Fact]
    public void The_real_catalog_is_judged_and_the_summary_names_every_refusal()
    {
        var catalog = QuestCatalog.All;
        var list = Build(catalog, level: 30);
        var summary = JobMenu.SummaryLine(list, 1_000_000, catalog.Count);
        var listed = list.Jobs.Count(e => e.Task.Kind == AutopilotTaskKind.Quest);
        Assert.StartsWith($"autopilot menu: level=30 next_xp=1000000 listed={listed}/{catalog.Count} refused=", summary);
        Assert.Equal(catalog.Count, listed + list.Refused.Count);
        foreach (var refused in list.Refused)
        {
            Assert.Contains($"{refused.Task.QuestKey}:{refused.Refusal}", summary);
        }

        Assert.Equal("autopilot menu: level=10 next_xp=7 listed=0/0 refused=none", JobMenu.SummaryLine(Build([]), 7, 0));
    }

    [Fact]
    public void Status_job_defaults_empty_and_a_source_can_be_implemented()
    {
        var status = new AutopilotStatus(AutopilotMode.Off, string.Empty, string.Empty, string.Empty, 0, 0, 0);
        Assert.Equal(string.Empty, status.Job);
        Assert.Equal("errand", (status with { Job = "errand" }).Job);
        var source = new FakeSource();
        var raised = 0;
        source.Changed += () => raised++;
        source.Raise();
        Assert.Equal(1, raised);
        Assert.Equal(AutopilotTask.Job, source.Current);
        Assert.Equal(3, source.Jobs.Jobs.Count);
    }

    private sealed class FakeSource : IAutopilotJobSource
    {
        public JobList Jobs { get; } = Build([]);

        public AutopilotTask Current => AutopilotTask.Job;

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }
}
