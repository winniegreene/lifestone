using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 guard task, the scheduling over the scripted ACE and the dial (<see cref="AutopilotMoveOnLoopTests.Climb"/>, as the trial's tests): the climber at level 41 (the Abode's last level since D135) on the Abode's rung with
/// <c>--autopilot-ladder-quests=tusker-guard-kill-task</c>. Unread and due, the task's talk is made from home before she leaves (the quest station's steps 0-1), the giver's start arms the counter, and the next rung read moves her to the
/// Tusker Barracks by a normal rung change. Armed, the dial's start builds the run on the Barracks and she fights there with the ladder's own fight; the server's complete line ends the fight in a lull, the turn-in starts from there
/// (steps 3-4), and after it her rung's own ground, the Abode, by a normal rung change.
/// </summary>
public sealed class KillTaskLadderTests(ITestOutputHelper output)
{
    private const string Barracks = "tusker-barracks";
    private const string Abode = "tusker-abode";
    private const string Key = "tusker-guard-kill-task";
    private const string Start = "If you wish to do the same you must first go out and experience combat with these creatures. Return to me after you have killed 500 Tusker Guards and I will reward you.";
    private const string TurnIn = "Excellent, now go contemplate what you have learned from fighting the Tusker Guards. You may be able to apply these pratices in various different types of combat with a great variety of creatures.";

    /// <summary>The climber at level 41 with the task selected (the quest selection names it alone unless <paramref name="selected"/> says more: no other errand), every gate line up and the bot's hour standing, the Abode's and the Barracks' stock in the pack.</summary>
    private (AutopilotMoveOnLoopTests.Climb Climb, List<StationSpec> Quests) Climb(LadderKillTask task, Func<StationSpec, int, AutopilotMoveOnLoopTests.Climb, ScenarioStatus?> talk, string selected = Key, IReadOnlyList<Quest>? trophies = null, Func<string, int>? active = null, Func<AutopilotOptions, AutopilotOptions>? more = null)
    {
        var selection = LadderQuestSelection.Parse(selected, QuestCatalog.All, out _)!;
        var c = new AutopilotMoveOnLoopTests.Climb(output, tripFirst: false, level: 41, active: active, withOptions: o => (more ?? (static x => x))(o with { LadderQuests = true, QuestSelection = selection, KillTasks = [task] }), trophyQuests: trophies);
        foreach (var key in new[] { Barracks, Abode })
        {
            foreach (var line in GroundCatalog.Get(key).Supplies.Stock)
            {
                c.World.Trip.Own(line.Name, line.Wcid, VendorRig.MiscType, 1, (ushort)line.Keep);
            }
        }

        var quests = new List<StationSpec>();
        var prior = c.World.Factory.Script;
        c.World.Factory.Script = spec =>
        {
            if (spec.Kind != StationKinds.Quest)
            {
                return prior?.Invoke(spec);
            }

            quests.Add(spec);
            return new ScriptedRun(spec.Name, step => talk(spec, step, c), () => spec.Arguments[JourneyPlans.StepsArgument] == task.TurnInSteps
                ? $"key={Key} from=home steps=2/5 done=yes xp=+20000000 reward_line=20000000 timer=saved ok=yes fail=none steps=3-4"
                : $"key={Key} from=home steps=2/5 done=partial xp=+0 reward_line=0 timer=none ok=yes fail=none steps=0-1");
        };
        return (c, quests);
    }

    /// <summary>The server says <paramref name="text"/> (a system line: the counter, the giver's lines as the client's chat holds them).</summary>
    private static void Say(AutopilotMoveOnLoopTests.Climb c, string text) => c.World.Trip.Vendor.R.Raw(AceWireWriters.ServerMessage(text, 0));

    /// <summary>The scripted talk: on its first step the giver's lines, then two steps for the client to read them, standing at home as the station's way home leaves her.</summary>
    private static ScenarioStatus? Talk(AutopilotMoveOnLoopTests.Climb c, int step, params string[] lines)
    {
        if (step == 0)
        {
            foreach (var line in lines)
            {
                Say(c, line);
            }

            var home = c.Ground.Home;
            c.World.Trip.Vendor.StandAt(home.LifestoneCell, home.LifestonePoint);
        }

        return step >= 3 ? ScenarioStatus.Succeeded : null;
    }

    private static IReadOnlyList<string> TaskLines(AutopilotMoveOnLoopTests.Climb c) => [.. c.Lines.Where(l => l.StartsWith(LadderKillTask.LinePrefix, StringComparison.Ordinal))];

    [Fact]
    public void Unread_and_due_the_talk_is_made_from_home_its_start_arms_the_counter_and_the_rung_change_takes_her_to_the_Barracks()
    {
        var task = new LadderKillTask(QuestCatalog.Get(Key));
        var (c, quests) = Climb(task, (_, step, x) => Talk(x, step, Start));

        c.Press();
        c.Run(x => x.FightsOn(Barracks), frames: 6000);
        c.Print();

        var arm = Assert.Single(quests);
        Assert.Equal((Key, JourneyPlans.FromHome, "0-1"), (arm.Arguments[JourneyPlans.QuestArgument], arm.Arguments[JourneyPlans.FromArgument], arm.Arguments[JourneyPlans.StepsArgument]));
        Assert.Equal((KillTaskPhase.Armed, 0), (task.Phase, task.Counter));
        Assert.True(c.FightsOn(Barracks), "she fights on the Barracks after the arm");
        Assert.Equal([Abode, Barracks], c.Grounds); // the dial's start on her rung's ground; the talk before she left home; the rung change to the task's ground
        Assert.Empty(c.Stopped);
        var lines = TaskLines(c);
        Assert.Contains(lines, l => l.Contains(": errand: the arm (his reply reads the counter", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("phase=armed counter=0/500 read=talk", StringComparison.Ordinal) && l.Contains("the server armed the counter at 0", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(": armed: the next rung read takes tusker-barracks in place of her rung's ground", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: move-on: level 41: the kill task's rung change: ", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains("errand=ladder from=home", StringComparison.Ordinal)); // the selection names the task alone: no ladder errand runs
    }

    [Theory]
    [InlineData("tusks,tusker-guard-kill-task", true)]
    [InlineData("tusker-guard-kill-task", false)]
    public void On_the_Barracks_the_fight_keeps_the_Guard_Tusk_only_when_the_tusks_are_selected(string selected, bool wanted)
    {
        var task = new LadderKillTask(QuestCatalog.Get(Key));
        task.Read(new Lifestone.Client.Session.Contracts.ChatLine(0, Lifestone.Client.Session.Contracts.ChatKind.System, 0, null, 0, "You've killed 10 out of 500 Tusker Guards."), Lifestone.Client.Diagnostics.NullLogSink.Instance);
        var (c, _) = Climb(task, (_, step, x) => Talk(x, step), selected, trophies: [QuestCatalog.Get("tusk-guard")]);

        c.Press();
        c.Run(x => x.FightsOn(Barracks), frames: 4000);

        Assert.True(c.FightsOn(Barracks));
        var fight = c.World.Factory.Made.Last(s => s.Kind == StationKinds.DenFight);
        Assert.Equal(wanted, fight.Arguments.TryGetValue(JourneyStations.WantArgument, out var want) && want == "22425"); // the Guard Tusk 22425, carried by the Tusker Guard 22593: kept aside for Brighteyes
        if (wanted)
        {
            Assert.Contains(c.Lines, l => l.StartsWith("ladder: the fight's want: 22425 (", StringComparison.Ordinal) && l.Contains("the due trophy quests of tusker-barracks", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(c.Lines, l => l.StartsWith("autopilot quest: key=tusk-guard: skipped: --autopilot-ladder-quests=tusker-guard-kill-task does not name it", StringComparison.Ordinal));
        }

        // the turn-in, by the trophy rule: a Guard Tusk held on the Barracks at level 50 (Lifestone Recall in the book) is a trip of its own from home to Brighteyes, at the next way home
        var input = new TrophyQuestInput(Barracks, [QuestCatalog.Get("tusk-guard")], new QuestTimers("Qwinnie"), DateTimeOffset.UnixEpoch, w => w == 22425 ? 1 : 0, 50, true, GroundCatalog.Get(Barracks).Arrival.Cell, GroundCatalog.All, Lifestone.Client.Session.Travel.TravelRoutes.Default);
        var call = Assert.Single(AutopilotQuestRule.Calls(input));
        Assert.Equal(("tusk-guard", JourneyPlans.FromHome), (call.Quest.Key, call.From));
        Assert.Empty(AutopilotQuestRule.Want(input)); // held: no second one is wanted (the pickup's own 20 h gate)
    }

    [Fact]
    public void Armed_she_hunts_the_Barracks_until_the_complete_line_turns_it_in_from_there_and_takes_her_rungs_ground_again()
    {
        var task = new LadderKillTask(QuestCatalog.Get(Key));
        task.Read(new Lifestone.Client.Session.Contracts.ChatLine(0, Lifestone.Client.Session.Contracts.ChatKind.System, 0, null, 0, "You've killed 498 out of 500 Tusker Guards."), Lifestone.Client.Diagnostics.NullLogSink.Instance); // an earlier run's talk read it
        var (c, quests) = Climb(task, (_, step, x) => Talk(x, step, TurnIn, "You've earned 20,000,000 experience."));

        c.Press();
        c.Run(x => x.FightsOn(Barracks), frames: 4000);
        Assert.True(c.FightsOn(Barracks), "the dial's start builds the first run on the Barracks");
        Assert.Equal([Barracks], c.Grounds);

        Say(c, "You have killed 499 Tusker Guards! You must kill 500 to complete your task.");
        Say(c, "You have killed 500 Tusker Guards! Your task is complete!");
        c.Run(x => x.FightsOn(Abode), frames: 6000);
        c.Print();

        var turnIn = Assert.Single(quests);
        Assert.Equal((Key, JourneyPlans.FromHome, "3-4"), (turnIn.Arguments[JourneyPlans.QuestArgument], turnIn.Arguments[JourneyPlans.FromArgument], turnIn.Arguments[JourneyPlans.StepsArgument]));
        Assert.Equal(KillTaskPhase.TurnedIn, task.Phase);
        Assert.True(c.FightsOn(Abode), "she fights on the Abode after the turn-in");
        Assert.Equal([Barracks, Abode], c.Grounds);
        Assert.Empty(c.Stopped);
        var lines = TaskLines(c);
        Assert.Contains(lines, l => l.Contains("phase=complete counter=500/500 read=kill-line", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("the counter reads complete and no hostile is in reach: the fight ends; the turn-in follows", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(": errand: the turn-in (the counter reads complete)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(": turned in (the experience the station read: 20000000)", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: move-on: level 41: the kill task's hunt on tusker-barracks is over: back to her rung's own ground: ", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal) && l.Contains(" state=kill-task ", StringComparison.Ordinal)); // the Barracks' cycle closed by the task
    }

    [Fact]
    public void M6_14_gate_lapse_on_the_Barracks_a_bane_that_lapsed_before_a_trip_moves_no_rung_at_the_cycles_check_after_it()
    {
        // the live shape of 2026-11-13 23:39:35 to 23:49:28 UTC: armed, she hunts the Barracks; Bludgeon Bane runs out on her armour; a logistics trip; the cycle's check after it
        var task = new LadderKillTask(QuestCatalog.Get(Key));
        task.Read(new Lifestone.Client.Session.Contracts.ChatLine(0, Lifestone.Client.Session.Contracts.ChatKind.System, 0, null, 0, "You've killed 391 out of 500 Tusker Guards."), Lifestone.Client.Diagnostics.NullLogSink.Instance);
        var bane = 8;
        var (c, _) = Climb(task, (_, step, x) => Talk(x, step), active: name => name == "Bludgeon Bane" ? bane : 8, more: o => o with { TripAfterKills = 2 });

        c.Press();
        c.Run(x => x.FightsOn(Barracks), frames: 4000);
        Assert.True(c.FightsOn(Barracks), "the dial's start builds the first run on the Barracks");
        bane = 0; // it ran out in the fight
        c.Run(x => x.Lines.Any(l => l.StartsWith("ladder: check after (cycle 2)", StringComparison.Ordinal)), frames: 4000);
        c.Run(x => x.FightsOn(Barracks), frames: 2000);
        c.Print();

        var after = Assert.Single(c.Lines, l => l.StartsWith("ladder: check after (cycle 2)", StringComparison.Ordinal));
        Assert.Contains("level=41 rung=abode ", after, StringComparison.Ordinal); // her own rung, not the outskirts' five rungs down
        Assert.Contains(" ground=tusker-barracks ", after, StringComparison.Ordinal);
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: gate abode: not read mid-run (", StringComparison.Ordinal) && l.Contains("tusker-barracks, the ground of this run, is guarded by the quarters gate, read whole at the way out with every gate under it", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains("rung=outskirts", StringComparison.Ordinal) || l.Contains("stop asked (move-on-reached)", StringComparison.Ordinal));
        Assert.True(c.FightsOn(Barracks), "she fights on the Barracks again after the trip");
        Assert.Equal([Barracks], c.Grounds); // no restart
        Assert.Empty(c.Stopped);
    }
}
