using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14, slice S3: the sprint, <c>--autopilot-ladder-quests=off</c>. Proved over the scripted ACE: with the option off a due ladder errand and a held, due trophy are not run at any way home (no quest station is made), the trip the fight
/// stopped for is still made, and each quest skipped is named once a run; the fight wants no trophy. The option: on by default (every test of the file beside this one runs with it on), a switch, registered, and its text says the first quest
/// rungs stay. The ladder's quest rungs are decided by <c>DecideRung</c>, which the option does not reach.
/// </summary>
public sealed partial class LadderErrandTests
{
    private static AutopilotOptions Sprint => new() { Job = AutopilotJob.LevelUp, TripAfterKills = 1, LadderQuests = false };

    private static string[] Skipped(ApWorld w) => [.. w.Lines.Where(l => l.Contains(": skipped: --autopilot-ladder-quests=off ", StringComparison.Ordinal))];

    [Fact]
    public void The_sprint_runs_no_errand_and_no_turn_in_makes_the_trip_and_names_each_quest_skipped_once()
    {
        var quest = Errand();
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: true, _ => throw new InvalidOperationException("the sprint runs no quest station"), new QuestTimers("Winnie"), options: Sprint);
        autopilot.TrophyQuests = [TrophyQuestTests.TuskFemale(ground: Lodge), quest]; // a held, due trophy and a due ladder errand: with the option on the trophy runs first, the errand at a later way home
        w.Run(autopilot, a => Kinds(w).Count(k => k == "logistics") >= 2 && a.State == AutopilotState.Fight, 20000); // the start-up audit's trip, then the way home of the forced trip, and back in the fight
        autopilot.RequestStop("test"); // the stop's way home asks again
        w.Run(autopilot, null, 20000);

        Assert.Empty(Quests(w));
        Assert.Equal((0, 0), (autopilot.TurnIns, autopilot.LadderErrands));
        var kinds = Kinds(w);
        var trip = Enumerable.Range(0, kinds.Length).Where(i => kinds[i] == "logistics").ElementAt(1);
        Assert.Equal("den-fight", kinds[trip - 1]); // the trip the fight stopped for, with no errand before it
        var skipped = Skipped(w);
        Assert.Equal(2, skipped.Length); // once a run each, however many ways home asked
        Assert.Single(skipped, l => l.StartsWith("autopilot quest: key=tusk-female: skipped: ", StringComparison.Ordinal) && l.Contains("its trophy is held and due, and the turn-in from=", StringComparison.Ordinal));
        Assert.Single(skipped, l => l.StartsWith($"autopilot quest: key={Key}: skipped: ", StringComparison.Ordinal) && l.Contains("the ladder errand is due", StringComparison.Ordinal));
        Assert.Equal(["fetch-test", "tusk-female"], autopilot.QuestsSkipped.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("errand=ladder", StringComparison.Ordinal) || l.Contains("autopilot quest: ended", StringComparison.Ordinal));
        Assert.Equal("request:test", autopilot.StopName);
    }

    [Fact]
    public void The_sprints_fight_wants_no_trophy_and_says_so_once()
    {
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false, options: Sprint); // tusk-female is due on the Lodge and the tusk is not held: with the option on the fight wants it (TrophyQuestTests)
        w.Run(autopilot, a => a.State == AutopilotState.Fight, 6000);

        var fight = w.Factory.Made.Last(s => s.Kind == StationKinds.DenFight);
        Assert.False(fight.Arguments.ContainsKey(JourneyStations.WantArgument));
        Assert.DoesNotContain(w.Lines, l => l.Contains("ladder: the fight's want:", StringComparison.Ordinal));
        Assert.StartsWith("autopilot quest: key=tusk-female: skipped: --autopilot-ladder-quests=off (the sprint: no quest errand from home, no trophy want): ", Assert.Single(Skipped(w)), StringComparison.Ordinal);
        Assert.Equal(["tusk-female"], autopilot.QuestsSkipped);
    }

    [Fact]
    public void The_quests_option_is_on_by_default_a_switch_registered_and_says_the_first_quest_rungs_stay()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.True(new AutopilotOptions().LadderQuests);
        Assert.True(Parse().LadderQuests);
        Assert.True(Parse("--autopilot-ladder-quests=on").LadderQuests);
        Assert.False(Parse("--autopilot-ladder-quests=off").LadderQuests);
        Assert.Empty(Parse("--autopilot-ladder-quests=off").Errors);
        Assert.True(Parse("--autopilot-ladder-quests=off").BuffBot); // the two switches are apart: a sprint with the bot is the default bot and the quests off
        var bad = Parse("--autopilot-ladder-quests=sometimes");
        Assert.StartsWith("--autopilot-ladder-quests: 'sometimes' is not on, off, a group (tusks) or a key of the quest catalog (", Assert.Single(bad.Errors), StringComparison.Ordinal); // M6 14 guard task: a list of the catalog's keys is a selection
        Assert.True(bad.LadderQuests);

        Assert.DoesNotContain("ladder_quests", new AutopilotOptions { Job = AutopilotJob.LevelUp }.ToString(), StringComparison.Ordinal); // on: every earlier run's line is unchanged
        Assert.EndsWith(" job=level-up ladder_quests=off", new AutopilotOptions { Job = AutopilotJob.LevelUp, LadderQuests = false }.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(" job=level-up buff_bot=off ladder_quests=off", new AutopilotOptions { Job = AutopilotJob.LevelUp, BuffBot = false, LadderQuests = false }.ToString(), StringComparison.Ordinal);

        var option = Assert.Single(OptionCatalog.Autopilot().Options, o => o.Name == "--autopilot-ladder-quests");
        Assert.Contains("default on", option.Help, StringComparison.Ordinal);
        Assert.Contains("the Training Academy and the Pathwarden chest (levels 1 to 4: her first gear), stay as they are in every setting", option.Help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-ladder-quests", OptionCatalog.HelpText(), StringComparison.Ordinal);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-ladder-quests"));
    }
}
