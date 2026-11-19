using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Net.Messages;


namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 guard task: the Tusker Guard Kill Task in the level up job, by its parts. The task (<see cref="LadderKillTask"/>) moves only on the server's own lines: the giver's start arms the counter at 0, his count reads it, each kill line
/// reads it after a kill (a jump is named), the complete line ends the hunt, his turn-in erases it and his wait puts it on its timer; the errand it owes is the arm while unread and due, the turn-in when complete, none otherwise. The
/// rung choice (<see cref="RungOwner"/>): an armed task takes the Barracks in place of her rung's ground (a rung change from the Abode, the hunt on the Barracks), with her own rung's plan; the portal's level, the gate read at the way out
/// and a trial each come first, by name; a corpse run is not steered.
/// </summary>
public sealed class LadderKillTaskTests
{
    private const string Barracks = "tusker-barracks";
    private const string Abode = "tusker-abode";

    private static Ladder L => LadderLoader.WarMage;

    private static Quest Guard => QuestCatalog.Get("tusker-guard-kill-task");

    private static ChatLine Line(string text, ChatKind kind = ChatKind.System) => new(0, kind, 0, kind == ChatKind.Tell ? "Shoichi" : null, 0, text);

    private static ChatLine Counter(int n) => Line(n >= 500 ? $"You have killed {n} Tusker Guards! Your task is complete!" : $"You have killed {n} Tusker Guards! You must kill 500 to complete your task.");

    private const string Start = "If you wish to do the same you must first go out and experience combat with these creatures. Return to me after you have killed 500 Tusker Guards and I will reward you.";

    private const string NotYet = "Return to me after you have killed 500 Tusker Guards and I will reward you.";

    [Fact]
    public void The_task_reads_its_shape_from_the_quest_file_and_owes_the_arm_while_unread_and_due()
    {
        var task = new LadderKillTask(Guard);
        Assert.Equal((Barracks, 500, "0-1", "3-4"), (task.Ground, task.Count, task.ArmSteps, task.TurnInSteps));
        Assert.Equal((1, 2, 4), (task.ArmTalk.Index, task.Kill.Index, task.TurnInTalk.Index));
        Assert.Equal(KillTaskPhase.Unread, task.Phase);
        Assert.False(task.Steers);
        var timers = new QuestTimers("Qwinnie");
        var now = new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal("0-1", task.ErrandSteps(timers, now));
        timers.Complete(Guard, now.AddHours(-19), QuestTimers.HowReply);
        Assert.Null(task.ErrandSteps(timers, now)); // the 20 hour timer has an hour to run
        Assert.Equal("0-1", task.ErrandSteps(timers, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => new LadderKillTask(QuestCatalog.Get("tusk-guard"))); // no kill step: no kill task
    }

    [Fact]
    public void The_givers_start_arms_the_counter_at_zero_and_the_kill_lines_read_it_to_complete()
    {
        var log = new RecordingLogSink(LogSeverity.Info);
        var task = new LadderKillTask(Guard);
        Assert.False(task.Read(Line("Greetings, have you come to train?", ChatKind.Tell), log));
        Assert.True(task.Read(Line(Start, ChatKind.Tell), log));
        Assert.Equal((KillTaskPhase.Armed, 0, "talk", 1), (task.Phase, task.Counter, task.ReadFrom, task.Arms));
        Assert.True(task.Steers);

        Assert.True(task.Read(Counter(1), log));
        Assert.Equal((KillTaskPhase.Armed, 1, "kill-line"), (task.Phase, task.Counter, task.ReadFrom));
        Assert.False(task.Read(Line("You have killed 3 Blood Shreths! You must kill 10 to complete your task."), log)); // another task's counter
        Assert.True(task.Read(Counter(5), log)); // a jump: named, never corrected
        Assert.Equal(5, task.Counter);
        Assert.Contains(log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("(a jump: the last read was 1)", StringComparison.Ordinal));
        Assert.True(task.Read(Counter(499), log));
        Assert.Equal(KillTaskPhase.Armed, task.Phase);
        Assert.Null(task.ErrandSteps(new QuestTimers("Qwinnie"), DateTimeOffset.UnixEpoch)); // armed: no errand, the hunt

        Assert.True(task.Read(Counter(500), log));
        Assert.Equal((KillTaskPhase.Complete, 500), (task.Phase, task.Counter));
        Assert.False(task.Steers);
        Assert.Equal("3-4", task.ErrandSteps(new QuestTimers("Qwinnie"), DateTimeOffset.UnixEpoch)); // the turn-in, whatever the client's timers say
        Assert.Equal(4, task.KillLines);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("ladder: kill task: key=tusker-guard-kill-task phase=complete counter=500/500 read=kill-line ground=tusker-barracks: the kill line \"You have killed 500 Tusker Guards! Your task is complete!\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_givers_count_reads_the_counter_and_his_reply_after_it_is_no_new_start()
    {
        var log = new RecordingLogSink(LogSeverity.Info);
        var task = new LadderKillTask(Guard);
        Assert.True(task.Read(Line("You've killed 123 out of 500 Tusker Guards."), log)); // his DirectBroadcast of %tqc and %tqm
        Assert.True(task.Read(Line(NotYet, ChatKind.Tell), log)); // then his Tell, which holds the start's words too
        Assert.Equal((KillTaskPhase.Armed, 123, "talk", 0), (task.Phase, task.Counter, task.ReadFrom, task.Arms));

        var full = new LadderKillTask(Guard);
        Assert.True(full.Read(Line("You've killed 500 out of 500 Tusker Guards."), log));
        Assert.Equal(KillTaskPhase.Complete, full.Phase);
    }

    [Fact]
    public void The_turn_in_erases_the_counter_and_the_wait_puts_the_task_on_its_timer()
    {
        var log = new RecordingLogSink(LogSeverity.Info);
        var task = new LadderKillTask(Guard);
        task.Read(Counter(500), log);
        Assert.True(task.Read(Line("Excellent, now go contemplate what you have learned from fighting the Tusker Guards. You may be able to apply these pratices in various different types of combat with a great variety of creatures.", ChatKind.Tell), log));
        Assert.Equal((KillTaskPhase.TurnedIn, (int?)null, 1), (task.Phase, task.Counter, task.TurnIns));
        var timers = new QuestTimers("Qwinnie");
        timers.Complete(Guard, DateTimeOffset.UnixEpoch, QuestTimers.HowReply); // the turn-in's completion
        Assert.Null(task.ErrandSteps(timers, DateTimeOffset.UnixEpoch)); // turned in: nothing while the quest's timer runs
        Assert.Equal(task.ArmSteps, task.ErrandSteps(timers, DateTimeOffset.UnixEpoch.AddSeconds(Guard.TimerSeconds!.Value))); // M6 audit (fix 3): the next round's arm once the timer allows it, in the same process

        var waiting = new LadderKillTask(Guard);
        Assert.True(waiting.Read(Line("You have killed many Tusker Guards and have advanced in your skills. Part of our training is knowing when to stop and reflect on what you have done.", ChatKind.Tell), log));
        Assert.Equal(KillTaskPhase.OnTimer, waiting.Phase);
        Assert.False(waiting.Steers);
    }

    [Fact]
    public void A_gate_shut_at_the_way_out_holds_the_task_until_the_next_stop_at_the_bot()
    {
        var log = new RecordingLogSink(LogSeverity.Info);
        var task = new LadderKillTask(Guard);
        task.Read(Line(Start, ChatKind.Tell), log);
        Assert.True(task.GateHeld("gate: the abode gate's lines are read shut", DateTimeOffset.UnixEpoch, log));
        Assert.False(task.GateHeld("again", DateTimeOffset.UnixEpoch, log)); // one hold, one line
        Assert.False(task.Steers);
        Assert.Equal(KillTaskPhase.Armed, task.Phase);
        task.BotVisited(log);
        Assert.True(task.Steers);
        Assert.Contains(log.Entries, e => e.Message.Contains("held: gate: the abode gate's lines are read shut; her rung's ground until the next stop at the buff bot", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the rung choice

    private static PlayerQualities Trained()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static LadderKillTask Armed()
    {
        var task = new LadderKillTask(Guard);
        task.Read(Line("You've killed 40 out of 500 Tusker Guards."), NullLogSink.Instance);
        return task;
    }

    private static RungAsk Ask(int level, LadderKillTask? task, string? run, bool leaving = false, Func<string, int>? active = null) =>
        new(level, null, Trained(), static (_, _) => true, new RungBuffRead(active ?? (static _ => 8), static _ => 4)) { RunGround = run, Leaving = leaving, KillTask = task };

    [Fact]
    public void An_armed_task_takes_the_Barracks_in_place_of_her_rungs_ground_with_her_own_rungs_plan()
    {
        var task = Armed();
        var moved = RungOwner.Decide(L, Ask(41, task, Abode));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:abode", true, "kill-task:tusker-guard-kill-task"), (moved.Decision.Kind, moved.Decision.Stop, moved.Decision.Ok, moved.Reason));
        Assert.Equal(("abode", Barracks, (ushort?)0x5A48), (moved.Decision.Rung!.Key, moved.Decision.Rung.GroundKey, moved.Decision.Rung.Landblock));
        Assert.Equal(L.Rungs.Single(r => r.Key == "abode").TargetMargin, moved.Decision.Rung.TargetMargin); // her own rung's plan: only the ground changes
        Assert.True(moved.KillTask is { Steers: true, Refused: null, GateRung: "quarters", Gate: null });
        Assert.Contains("the kill task tusker-guard-kill-task (counter 40/500, read from the talk) hunts tusker-barracks in place of her rung abode's ground tusker-abode at level 41", moved.Decision.Why, StringComparison.Ordinal);

        var hunt = RungOwner.Decide(L, Ask(41, task, Barracks));
        Assert.Equal((LadderStepKind.Hunt, Barracks), (hunt.Decision.Kind, hunt.Ground?.Key));
        var host = RungOwner.Decide(L, Ask(41, task, run: null)); // the dial's start and move-on (no run yet): the run is built on the Barracks
        Assert.Equal((LadderStepKind.Hunt, Barracks), (host.Decision.Kind, host.Ground?.Key));

        var own = RungOwner.Decide(L, Ask(41, new LadderKillTask(Guard), Abode)); // unread: the ladder's own
        Assert.Equal((LadderStepKind.Hunt, Abode), (own.Decision.Kind, own.Decision.Rung!.GroundKey));
        Assert.Null(own.KillTask);
    }

    [Fact]
    public void The_portal_the_gate_and_a_trial_come_first_by_name()
    {
        var task = Armed();
        var young = RungOwner.Decide(L, Ask(35, task, Abode));
        Assert.Equal(Abode, young.Decision.Rung!.GroundKey);
        Assert.StartsWith("portal: the entry portal 22661 of tusker-barracks admits levels 40 and up; level 35 is not admitted", young.KillTask!.Refused, StringComparison.Ordinal);
        Assert.False(young.KillTask.Steers);

        Assert.Same(L.GateOf("quarters"), RungOwner.KillTaskGate(L, Barracks)); // an alternate of the Quarters: its gate (D135 gave every rung from the Burrow up one)
        Assert.Null(RungOwner.KillTaskGate(L, "black-spawn-den-sho"));

        var open = RungOwner.Decide(L, Ask(41, task, Barracks, leaving: true));
        Assert.Equal(LadderStepKind.Hunt, open.Decision.Kind);
        Assert.True(open.KillTask is { Steers: true, Gate.Open: true });
        var shut = RungOwner.Decide(L, Ask(41, task, Barracks, leaving: true, active: static name => name == "Impenetrability" ? 0 : 8));
        var ownShut = RungOwner.Decide(L, Ask(41, null, Barracks, leaving: true, active: static name => name == "Impenetrability" ? 0 : 8));
        Assert.Equal((ownShut.Decision, ownShut.Reason), (shut.Decision, shut.Reason)); // she does not leave for the Barracks: the ladder's own choice, a gate holding her lower
        Assert.StartsWith("gate-held:", shut.Reason, StringComparison.Ordinal);
        Assert.StartsWith("gate: the quarters gate's lines are read shut at the way out to tusker-barracks: Impenetrability up (level 0 of 3)", shut.KillTask!.Refused, StringComparison.Ordinal);
        Assert.False(shut.KillTask.Gate!.Open);

        var trial = RungOwner.Decide(L, Ask(41, task, Abode) with { Trial = new LadderTrial("tusker-quarters") });
        Assert.Equal("trial:tusker-quarters", trial.Reason); // an explicit test of a later ground first: the task waits
        Assert.Null(trial.KillTask);

        var corpse = RungOwner.CorpseRun(L, Ask(41, task, null), Barracks);
        Assert.Equal((true, "no-rung"), (corpse.Open, corpse.Reason)); // a corpse on the Barracks is walked back to: the task does not steer the corpse run
    }

    /// <summary>The live read of 2026-11-13 23:49:28 UTC: every line up but Bludgeon Bane, which ran out on her armour at 23:39:35.</summary>
    private static int BaneLapsed(string name) => name == "Bludgeon Bane" ? 0 : 8;

    [Fact]
    public void M6_14_gate_lapse_mid_run_on_the_Barracks_a_lapsed_bane_never_holds_her_below_the_ground_she_runs_on()
    {
        var task = Armed();

        // the live check: mid-run on the Barracks at level 54, her rung the Quarters (its gate guards the Barracks, an alternate of the Quarters)
        var mid = RungOwner.Decide(L, Ask(54, task, Barracks, active: BaneLapsed) with { MidRun = true });
        Assert.Equal((LadderStepKind.Hunt, Barracks, "quarters", Barracks), (mid.Decision.Kind, mid.Ground?.Key, mid.Decision.Rung!.Key, mid.Decision.Rung.GroundKey));
        Assert.Equal((54, "kill-task:tusker-guard-kill-task", "quarters"), (mid.HeldLevel, mid.Reason, mid.RunGuardUnread));
        Assert.Empty(mid.Gates); // the Quarters' gate guards her ground and was read at the way out; the gates under it are not read either

        // what the live check did: the gates read, each shut by the one line, five rungs down to the outskirts' plan on the same ground. The run's first check still reads them (a host built the run: a wrong ground is caught)
        var first = RungOwner.Decide(L, Ask(54, task, Barracks, active: BaneLapsed));
        Assert.Equal(("outskirts", Barracks), (first.Decision.Rung!.Key, first.Decision.Rung.GroundKey));
        Assert.Equal(["quarters", "abode", "cave", "lodge", "burrow"], first.Gates.Select(g => g.Gate.Rung));
        Assert.All(first.Gates, g => Assert.Contains(g.Conditions, c => c is { Name: "Bludgeon Bane up", Met: false, Read: "level 0 of 3" }));
        Assert.Null(first.RunGuardUnread);

        // the way out still reads the gate whole: she does not leave home for the Barracks with the bane down
        var leaving = RungOwner.Decide(L, Ask(54, task, Barracks, leaving: true, active: BaneLapsed) with { MidRun = true });
        Assert.Equal("gate-held:quarters", leaving.Reason);
        Assert.StartsWith("gate: the quarters gate's lines are read shut at the way out to tusker-barracks: Bludgeon Bane up (level 0 of 3)", leaving.KillTask!.Refused, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_gate_lapse_without_the_kill_task_her_own_rungs_gate_is_never_read_at_a_cycles_check_so_the_lapse_moves_nothing()
    {
        const string Quarters = "tusker-quarters";
        foreach (var midRun in new[] { false, true })
        {
            // her rung's own ground: its gate is not read at a cycle's check (it was read when she left for it, D93), first check or not
            var own = RungOwner.Decide(L, Ask(54, null, Quarters, active: BaneLapsed) with { MidRun = midRun });
            Assert.Equal((LadderStepKind.Hunt, Quarters, 54, "stands-on:" + Quarters), (own.Decision.Kind, own.Ground?.Key, own.HeldLevel, own.Reason));
            Assert.Empty(own.Gates);
        }

        // the way out reads it: a lapsed bane at home holds her before she leaves, by name
        Assert.Equal("gate-held:quarters", RungOwner.Decide(L, Ask(54, null, Quarters, leaving: true, active: BaneLapsed)).Reason);
    }
}
