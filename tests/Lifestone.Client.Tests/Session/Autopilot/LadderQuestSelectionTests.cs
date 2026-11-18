using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 guard task (the owner, 2026-11-13): the quest selection, <c>--autopilot-ladder-quests=tusks,tusker-guard-kill-task</c>. Proved: on and off are as before; a list of the quest catalog's keys runs only those quests (a ladder errand or a
/// trophy turn-in it does not name is off, a kill task runs only when named); the group <c>tusks</c> is the catalog's tusk turn-ins; a bad list fails the run by name; the options carry one kill task object per named kill task, refuse it
/// outside the level up job's own loop, and log the selection on their line.
/// </summary>
public sealed class LadderQuestSelectionTests
{
    private static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void On_runs_every_errand_and_turn_in_and_no_kill_task_and_off_runs_none()
    {
        var on = LadderQuestSelection.Parse("on", QuestCatalog.All, out var e1)!;
        var off = LadderQuestSelection.Parse("off", QuestCatalog.All, out var e2)!;
        Assert.Null(e1);
        Assert.Null(e2);
        Assert.Same(LadderQuestSelection.On, on);
        Assert.Same(LadderQuestSelection.Off, off);
        Assert.True(on.Allows("tusk-female") && on.Allows("atlan-wand") && on.Allows("tusker-guard-kill-task"));
        Assert.False(on.Names("tusker-guard-kill-task")); // the default runs no kill task, as before
        Assert.False(off.Allows("tusk-female") || off.Allows("atlan-wand") || off.Names("tusker-guard-kill-task"));
    }

    [Fact]
    public void A_selection_runs_only_the_catalogs_keys_it_names_and_tusks_are_the_tusk_turn_ins()
    {
        var chosen = LadderQuestSelection.Parse(" tusks , tusker-guard-kill-task ", QuestCatalog.All, out var error)!;
        Assert.Null(error);
        Assert.Equal("tusks,tusker-guard-kill-task", chosen.Text);
        var tusks = QuestCatalog.All.Select(q => q.Key).Where(k => k.StartsWith("tusk-", StringComparison.Ordinal)).ToList();
        Assert.Contains("tusk-guard", tusks);
        Assert.Contains("tusk-female", tusks);
        Assert.Equal([.. tusks, "tusker-guard-kill-task"], chosen.Keys);
        Assert.Equal(tusks, LadderQuestSelection.GroupKeys("tusks", QuestCatalog.All));
        Assert.Null(LadderQuestSelection.GroupKeys("tusker-guard-kill-task", QuestCatalog.All));
        Assert.True(chosen.Allows("tusk-guard") && chosen.Allows("tusk-female") && chosen.Names("tusker-guard-kill-task"));
        Assert.False(chosen.Allows("atlan-wand") || chosen.Allows("lubziklan-stolen-goods") || chosen.Allows("blood-shreth-kill-task")); // the rest stay off
        Assert.False(chosen.IsOn || chosen.IsOff);

        var one = LadderQuestSelection.Parse("tusk-guard", QuestCatalog.All, out _)!;
        Assert.Equal(["tusk-guard"], one.Keys);
        Assert.False(one.Allows("tusk-female"));
    }

    [Theory]
    [InlineData("tusks,nonesuch", "'nonesuch' is not on, off, a group (tusks) or a key of the quest catalog (")]
    [InlineData("tusks,tusks", "names 'tusks' twice")]
    [InlineData("tusks,off", "'off' stands alone")]
    [InlineData("tusks,,tusk-guard", "an empty word")]
    [InlineData("sometimes", "'sometimes' is not on, off, a group (tusks) or a key of the quest catalog (")]
    public void A_bad_selection_is_refused_by_name(string value, string why)
    {
        Assert.Null(LadderQuestSelection.Parse(value, QuestCatalog.All, out var error));
        Assert.Contains(why, error, StringComparison.Ordinal);
        var parsed = Parse("--autopilot-job=level-up", "--autopilot-ladder-quests=" + value);
        Assert.StartsWith("--autopilot-ladder-quests: ", Assert.Single(parsed.Errors), StringComparison.Ordinal);
        Assert.Empty(parsed.KillTasks);
    }

    [Fact]
    public void The_options_carry_the_selection_and_one_kill_task_object_and_log_it()
    {
        var o = Parse("--autopilot-job=level-up", "--autopilot-ladder-quests=tusks,tusker-guard-kill-task");
        Assert.Empty(o.Errors);
        Assert.True(o.LadderQuests);
        Assert.Equal("tusks,tusker-guard-kill-task", o.Quests.Text);
        var task = Assert.Single(o.KillTasks);
        Assert.Equal(("tusker-guard-kill-task", "tusker-barracks", 500), (task.Key, task.Ground, task.Count));
        Assert.Same(task, (o with { Minutes = 5 }).KillTasks[0]); // one object for the process: every run's copy shares what the server said of the counter
        Assert.EndsWith(" job=level-up ladder_quests=tusks,tusker-guard-kill-task", o.ToString(), StringComparison.Ordinal);

        Assert.Empty(Parse("--autopilot-job=level-up", "--autopilot-ladder-quests=tusks").KillTasks); // the trophies only: no kill task
        Assert.Same(LadderQuestSelection.On, Parse("--autopilot-ladder-quests=on").Quests);
        Assert.Same(LadderQuestSelection.Off, Parse("--autopilot-ladder-quests=off").Quests);
        Assert.Same(LadderQuestSelection.On, new AutopilotOptions().Quests);
        Assert.Same(LadderQuestSelection.Off, new AutopilotOptions { LadderQuests = false }.Quests);

        var den = Parse("--autopilot-ladder-quests=tusker-guard-kill-task"); // the den job (the default)
        Assert.StartsWith("--autopilot-ladder-quests: the kill task tusker-guard-kill-task is the level up job's own loop's", Assert.Single(den.Errors), StringComparison.Ordinal);
        var option = Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-ladder-quests");
        Assert.Contains("tusks,tusker-guard-kill-task", option.Help, StringComparison.Ordinal);
    }
}
