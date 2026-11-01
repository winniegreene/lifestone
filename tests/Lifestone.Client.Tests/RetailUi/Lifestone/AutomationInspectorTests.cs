using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanels;

/// <summary>
/// The automation inspector (G7 lane B; docs/retail-ui.md section 22.2): the action log's rows newest first, the ring's cap of 1,024, the counters, the kill switch and dry run
/// shown read-only, a refresh driven by the log's change notification, and an idle inspector that allocates nothing.
/// </summary>
public sealed class AutomationInspectorTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static ActionLogEntry Entry(long id, ActionOutcomeKind kind = ActionOutcomeKind.Confirmed, ActionSource source = ActionSource.Ui, string summary = "Talk chars=5", string? detail = null, bool waited = false) =>
        new(id, ActionKind.Talk, source, summary, new ActionOutcome(kind, 0, detail), id, id + 0.25, waited);

    private (LoginRig Rig, GamePlayScreen Screen, AutomationInspector Inspector) EnterGame()
    {
        var rig = new LoginRig(fixture, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        return (rig, screen, Assert.IsType<AutomationInspector>(screen.Inspector));
    }

    // ------------------------------------------------------------------ no dats

    [Fact]
    public void A_row_names_the_time_the_source_the_action_and_how_it_ended()
    {
        Assert.Equal("5.25s Ui Talk chars=5 -> Confirmed (250 ms)", AutomationInspector.FormatRow(Entry(5)));
        Assert.Equal(
            "7.25s Automation Use 0x80000A03 -> Refused: the kill switch is on",
            AutomationInspector.FormatRow(Entry(7, ActionOutcomeKind.Refused, ActionSource.Automation, "Use 0x80000A03", "the kill switch is on")));
        Assert.Equal("1.25s Host Quit -> Sent (250 ms)", AutomationInspector.FormatRow(Entry(1, ActionOutcomeKind.Sent, ActionSource.Host, "Quit")));
        Assert.EndsWith("[waited for the rate limit]", AutomationInspector.FormatRow(Entry(2, waited: true)), StringComparison.Ordinal);
        Assert.Contains("-> Server error", AutomationInspector.FormatRow(Entry(3, ActionOutcomeKind.ServerError)), StringComparison.Ordinal);
        Assert.Contains("-> Not implemented", AutomationInspector.FormatRow(Entry(3, ActionOutcomeKind.NotImplemented)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_autopilot_lines_show_the_mode_the_state_the_last_decision_and_the_next_trigger_M5_4_h2()
    {
        Assert.Equal(
            "Autopilot: running | fight:den-fight | cycle 2 | interrupted 1, resumed 1\nLast decision: known-state -> fight: in the ground\nNext trigger: trip at 200 Tapers: 412 held",
            AutomationInspector.FormatAutopilot(new AutopilotStatus(AutopilotMode.Running, "fight:den-fight", "known-state -> fight: in the ground", "trip at 200 Tapers: 412 held", 2, 1, 1)));
        Assert.Equal(
            "Autopilot: off (type /auto to start it)\nLast decision: not started\nNext trigger: -",
            AutomationInspector.FormatAutopilot(new AutopilotStatus(AutopilotMode.Off, string.Empty, "not started", string.Empty, 0, 0, 0)));
        Assert.StartsWith("Autopilot: stopped (the controls are yours; /auto resumes)", AutomationInspector.FormatAutopilot(new AutopilotStatus(AutopilotMode.Interrupted, "fight:den-fight", "x", string.Empty, 1, 1, 0)), StringComparison.Ordinal);
        Assert.StartsWith("Autopilot: halted by the kill switch", AutomationInspector.FormatAutopilot(new AutopilotStatus(AutopilotMode.Halted, "fight:den-fight", "x", string.Empty, 1, 0, 0)), StringComparison.Ordinal);
        Assert.StartsWith("Autopilot: not available in this session", AutomationInspector.FormatAutopilot(null), StringComparison.Ordinal);

        // A long decision is cut to one line of the window.
        var long1 = AutomationInspector.FormatAutopilot(new AutopilotStatus(AutopilotMode.Running, "fight:den-fight", new string('x', 300), "stop at 30 min: 12 min left", 1, 0, 0)).Split('\n');
        Assert.Equal(3, long1.Length);
        Assert.Equal("Last decision: ".Length + AutomationInspector.AutopilotLineLimit, long1[1].Length);
        Assert.EndsWith("...", long1[1], StringComparison.Ordinal);
        Assert.Equal("Next trigger: stop at 30 min: 12 min left", long1[2]);
    }

    [Fact]
    public void The_log_counts_every_outcome_for_its_lifetime_and_announces_each_change()
    {
        var log = new ActionLog();
        var changes = 0;
        log.Changed += () => changes++;
        for (var i = 0; i < 1100; i++)
        {
            log.Add(Entry(i, i % 10 == 0 ? ActionOutcomeKind.Refused : ActionOutcomeKind.Confirmed, waited: i % 100 == 0));
        }

        Assert.Equal((1024, 1100L, 1100), (log.Count, log.Total, changes));
        Assert.Equal(110L, log.CountOf(ActionOutcomeKind.Refused)); // the ring dropped 76 entries; their outcomes are still counted
        Assert.Equal(990L, log.CountOf(ActionOutcomeKind.Confirmed));
        Assert.Equal(0L, log.CountOf(ActionOutcomeKind.Cancelled));
        Assert.Equal(11L, log.RateLimited);
        Assert.Equal(1100L, log.Version);
        log.Touch();
        Assert.Equal((1101, 1101L), (changes, log.Version));

        var text = AutomationInspector.FormatCounters(log);
        Assert.Contains("Logged 1,100 (1,024 kept, the ring holds 1,024)", text, StringComparison.Ordinal);
        Assert.Contains("Confirmed 990 |", text, StringComparison.Ordinal);
        Assert.Contains("Refusals 110 |", text, StringComparison.Ordinal);
        Assert.Contains("Rate-limited 11", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_safety_line_shows_both_switches()
    {
        Assert.Equal("Kill switch: off | Dry run: off | read-only here", AutomationInspector.FormatState(false, false));
        Assert.Equal("Kill switch: ON | Dry run: off | read-only here", AutomationInspector.FormatState(true, false));
        Assert.Equal("Kill switch: off | Dry run: ON | read-only here", AutomationInspector.FormatState(false, true));
    }

    [Fact]
    public void The_session_tells_the_log_when_the_switches_change_and_when_an_action_waits_for_its_rate_limit()
    {
        var rig = GameSession.Offline(_ => { }, new ManualTimeProvider(), new RecordingLogSink());
        var changes = 0;
        rig.ActionLog.Changed += () => changes++;
        rig.Actions.DryRun = true;
        rig.Actions.DryRun = true; // no change, no notice
        rig.Actions.KillSwitch = true;
        Assert.Equal(2, changes);
        rig.Actions.KillSwitch = false;
        rig.Actions.DryRun = false;
        Assert.Equal(4, changes);

        // An automation action under the kill switch is refused and logged: one entry, one notice, and the refusal is counted.
        rig.Actions.KillSwitch = true;
        var before = changes;
        var handle = rig.Actions.Talk("hello", ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome?.Kind);
        Assert.Equal(before + 1, changes);
        Assert.Equal(1L, rig.ActionLog.CountOf(ActionOutcomeKind.Refused));
    }

    // ------------------------------------------------------------------ over the user's dats

    [RequiresLocalDatsFact]
    public void The_shell_has_an_inspector_that_is_closed_until_opened_and_its_layout_reads_and_resolves()
    {
        var (rig, screen, inspector) = EnterGame();
        using (rig)
        {
            Assert.Same(inspector, screen.Inspector);
            Assert.False(inspector.IsOpen);
            Assert.Null(inspector.Window);
            Assert.True(rig.Manager.Services.Descs.TryGetLayout(AutomationInspector.LayoutId, out _)); // found lazily, in the Lifestone range, with no host registration
            Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= LogSeverity.Error);

            inspector.Open();
            Assert.True(inspector.IsOpen);
            var window = Assert.IsAssignableFrom<UiElement>(inspector.Window);
            Assert.Equal((120, 40, 560, 400), (window.Box.X0, window.Box.Y0, window.Box.Width, window.Box.Height));
            foreach (var id in new[] { AutomationInspector.CountersId, AutomationInspector.StateId, AutomationInspector.AutopilotId, AutomationInspector.ListId, AutomationInspector.ScrollbarId, AutomationInspector.CloseId })
            {
                Assert.NotNull(window.GetChildRecursive(id));
            }

            Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= LogSeverity.Error);
            Assert.Equal(window, Assert.Single(rig.Manager.Roots, r => ReferenceEquals(r, window)));
        }
    }

    private sealed class FakeAutopilot : IAutopilotStatusSource
    {
        public AutopilotStatus Status { get; set; } = new(AutopilotMode.Off, string.Empty, "not started", string.Empty, 0, 0, 0);

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }

    [RequiresLocalDatsFact]
    public void The_open_inspector_shows_the_autopilots_lines_and_follows_its_changes_on_the_next_frame_M5_4_h2()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            inspector.Open();
            Assert.StartsWith("Autopilot: not available", inspector.AutopilotText, StringComparison.Ordinal);

            var autopilot = new FakeAutopilot();
            inspector.Autopilot = autopilot;
            rig.Frame();
            Assert.StartsWith("Autopilot: off (type /auto to start it)", inspector.AutopilotText, StringComparison.Ordinal);

            autopilot.Status = new AutopilotStatus(AutopilotMode.Running, "fight:den-fight", "buff -> fight: buffed in the ground", "stop at 30 min: 12 min left", 1, 0, 0);
            var refreshes = inspector.Refreshes;
            autopilot.Raise();
            Assert.True(inspector.IsStale);
            rig.Frame();
            Assert.Equal(refreshes + 1, inspector.Refreshes);
            Assert.Contains("Next trigger: stop at 30 min: 12 min left", inspector.AutopilotText, StringComparison.Ordinal);
            Assert.Contains("Last decision: buff -> fight: buffed in the ground", inspector.AutopilotText, StringComparison.Ordinal);

            // Unwired again: the window no longer follows it.
            inspector.Autopilot = null;
            autopilot.Raise();
            rig.Frame();
            Assert.StartsWith("Autopilot: not available", inspector.AutopilotText, StringComparison.Ordinal);
            Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= LogSeverity.Error);
        }
    }

    /// <summary>Runs frames until the list holds the whole ring (the rows after the first batch come a few frames later).</summary>
    private static void Backfill(LoginRig rig, AutomationInspector inspector)
    {
        for (var frames = 0; inspector.IsBackfilling && frames < 40; frames++)
        {
            rig.Frame();
        }

        Assert.False(inspector.IsBackfilling);
    }

    [RequiresLocalDatsFact]
    public void Opening_with_a_full_ring_builds_only_the_newest_rows_and_the_rest_come_over_frames()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            var log = rig.Session.ActionLog;
            for (var i = 0; i < ActionLog.Capacity; i++)
            {
                log.Add(Entry(i, summary: $"Use 0x{i:X}"));
            }

            Assert.Equal(ActionLog.Capacity, log.Count);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            inspector.Open();
            var openMs = watch.Elapsed.TotalMilliseconds;
            Assert.Equal((AutomationInspector.FirstBatch, true), (inspector.RowCount, inspector.IsBackfilling));

            // Frame by frame: a batch at a time, never more than FrameBatch rows in a frame.
            var previous = inspector.RowCount;
            var worst = 0;
            var frames = 0;
            while (inspector.IsBackfilling && frames++ < 40)
            {
                rig.Frame();
                worst = Math.Max(worst, inspector.RowCount - previous);
                previous = inspector.RowCount;
            }

            Assert.Equal((ActionLog.Capacity, false), (inspector.RowCount, inspector.IsBackfilling));
            Assert.True(worst is > 0 and <= AutomationInspector.FrameBatch, $"worst batch {worst}");
            for (var k = 0; k < ActionLog.Capacity; k += 97)
            {
                Assert.Equal(AutomationInspector.FormatRow(log[log.Count - 1 - k]), inspector.RowText(k)); // newest first, every row in its place
            }

            // The opening frame's cost was the window and 32 rows, not 1,024 rows (about 30 ms without the window, about 40 ms to rebuild them all in one frame; Debug build).
            output.WriteLine($"first open with a full ring: {openMs:0.0} ms (window build and {AutomationInspector.FirstBatch} rows); {frames} frames to fill the list");

            // A burst bigger than the ring while the inspector is open also rebuilds only the first batch.
            for (var i = 0; i < 1100; i++)
            {
                log.Add(Entry(6000 + i));
            }

            watch.Restart();
            rig.Frame();
            output.WriteLine($"burst of 1100 while open: frame {watch.Elapsed.TotalMilliseconds:0.0} ms");
            Assert.True(inspector.IsBackfilling);
            Backfill(rig, inspector);
            Assert.Equal(ActionLog.Capacity, inspector.RowCount);
        }
    }

    [RequiresLocalDatsFact]
    public void The_rows_are_the_logs_actions_newest_first_with_their_outcomes()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            var baseline = rig.Session.ActionLog.Total;
            rig.Actions.SelectCharacter(0, ActionSource.Ui); // Local
            rig.Actions.Talk("Greetings", ActionSource.Ui); // refused: the stand-in is offline
            rig.Actions.Use(0x80000A03, ActionSource.Host); // refused
            rig.Frame();
            inspector.Open();

            var kept = (int)Math.Min(rig.Session.ActionLog.Total, ActionLog.Capacity);
            Assert.Equal(kept, inspector.RowCount);
            Assert.Equal(baseline + 3, rig.Session.ActionLog.Total);
            var newest = Assert.IsType<string>(inspector.RowText(0));
            var second = Assert.IsType<string>(inspector.RowText(1));
            var third = Assert.IsType<string>(inspector.RowText(2));
            Assert.Contains(" Host ", newest, StringComparison.Ordinal);
            Assert.Contains("Use", newest, StringComparison.Ordinal);
            Assert.Contains("-> Refused", newest, StringComparison.Ordinal);
            Assert.Contains(" Ui ", second, StringComparison.Ordinal);
            Assert.Contains("Talk", second, StringComparison.Ordinal);
            Assert.Contains(" Ui ", third, StringComparison.Ordinal);
            Assert.Contains("-> Local", third, StringComparison.Ordinal);
            Assert.Equal(AutomationInspector.FormatRow(rig.Session.ActionLog[^1]), newest);
            Assert.Contains("Logged ", inspector.CountersText, StringComparison.Ordinal);
            Assert.Contains("Refusals 2 |", inspector.CountersText, StringComparison.Ordinal);
            Assert.Contains($"Local {rig.Session.ActionLog.CountOf(ActionOutcomeKind.Local)} |", inspector.CountersText, StringComparison.Ordinal);
            Assert.True(rig.Session.ActionLog.CountOf(ActionOutcomeKind.Local) >= 1);
        }
    }

    [RequiresLocalDatsFact]
    public void A_new_action_refreshes_an_open_inspector_on_the_next_frame_and_only_then()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            inspector.Open();
            var rows = inspector.RowCount;
            var refreshes = inspector.Refreshes;
            var rebuilds = inspector.Rebuilds;
            Assert.False(inspector.IsStale);

            rig.Actions.Talk("one", ActionSource.Ui); // the log announces it; the window waits for the frame
            Assert.True(inspector.IsStale);
            Assert.Equal(rows, inspector.RowCount);
            rig.Frame();
            Assert.False(inspector.IsStale);
            Assert.Equal((rows + 1, refreshes + 1, rebuilds), (inspector.RowCount, inspector.Refreshes, inspector.Rebuilds));
            Assert.Contains("Talk", inspector.RowText(0), StringComparison.Ordinal);

            rig.Actions.Talk("two", ActionSource.Ui);
            rig.Actions.Talk("three", ActionSource.Ui); // two actions in one frame: one refresh, two rows, the newest on top
            rig.Frame();
            Assert.Equal((rows + 3, refreshes + 2, rebuilds), (inspector.RowCount, inspector.Refreshes, inspector.Rebuilds));
            Assert.Equal(AutomationInspector.FormatRow(rig.Session.ActionLog[^1]), inspector.RowText(0));
            Assert.Equal(AutomationInspector.FormatRow(rig.Session.ActionLog[^2]), inspector.RowText(1));
        }
    }

    [RequiresLocalDatsFact]
    public void The_ring_caps_at_1024_rows_and_the_counters_still_count_the_dropped_ones()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            var log = rig.Session.ActionLog;
            var startTotal = log.Total;
            for (var i = 0; i < 1000; i++)
            {
                log.Add(Entry(1_000_000 + i, source: ActionSource.Automation, summary: $"Talk chars={i}"));
            }

            var kept = Math.Min(ActionLog.Capacity, 1000 + (int)startTotal);
            inspector.Open();
            Assert.Equal((AutomationInspector.FirstBatch, 1, true), (inspector.RowCount, inspector.Rebuilds, inspector.IsBackfilling)); // the first build makes only the newest rows
            Assert.Contains("chars=999", inspector.RowText(0), StringComparison.Ordinal);
            Backfill(rig, inspector);
            Assert.Equal((kept, 1), (inspector.RowCount, inspector.Rebuilds)); // then every kept entry is listed, with no rebuild

            for (var i = 0; i < 30; i++)
            {
                log.Add(Entry(2_000_000 + i, ActionOutcomeKind.Refused, ActionSource.Automation, $"Use 0x{i:X}", "the kill switch is on"));
            }

            rig.Frame();
            Assert.Equal((ActionLog.Capacity, 1), (inspector.RowCount, inspector.Rebuilds)); // 1,100 + the earlier entries: the oldest were dropped by the ring and by the list
            Assert.Equal(ActionLog.Capacity, log.Count);
            Assert.Contains("Use 0x1D", inspector.RowText(0), StringComparison.Ordinal);
            Assert.Equal(AutomationInspector.FormatRow(log[0]), inspector.RowText(ActionLog.Capacity - 1)); // the oldest kept is at the bottom
            Assert.Contains($"Logged {log.Total:N0} (1,024 kept, the ring holds 1,024)", inspector.CountersText, StringComparison.Ordinal);
            Assert.Contains("Refusals 30 |", inspector.CountersText, StringComparison.Ordinal);

            // A burst bigger than the ring in one frame cannot be followed row by row: the window rebuilds from the log.
            for (var i = 0; i < 1500; i++)
            {
                log.Add(Entry(3_000_000 + i, summary: $"Talk chars={i}"));
            }

            rig.Frame();
            Assert.Equal((AutomationInspector.FirstBatch, 2), (inspector.RowCount, inspector.Rebuilds));
            Backfill(rig, inspector);
            Assert.Equal((ActionLog.Capacity, 2), (inspector.RowCount, inspector.Rebuilds));
            Assert.Contains("chars=1499", inspector.RowText(0), StringComparison.Ordinal);
            Assert.Contains("chars=476", inspector.RowText(ActionLog.Capacity - 1), StringComparison.Ordinal);
        }
    }

    [RequiresLocalDatsFact]
    public void The_kill_switch_and_dry_run_are_shown_and_the_window_has_no_way_to_change_them()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            inspector.Open();
            Assert.Equal("Kill switch: off | Dry run: off | read-only here", inspector.StateText);
            rig.Actions.DryRun = true;
            rig.Frame();
            Assert.Equal("Kill switch: off | Dry run: ON | read-only here", inspector.StateText);
            rig.Actions.KillSwitch = true;
            rig.Frame();
            Assert.Equal("Kill switch: ON | Dry run: ON | read-only here", inspector.StateText);

            // Pressing every control of the window changes neither switch (the window's only button hides it).
            var window = inspector.Window!;
            window.GetChildRecursive(AutomationInspector.StateId)!.BroadcastElementMessage(UiMessageIds.ButtonClick);
            window.GetChildRecursive(AutomationInspector.CountersId)!.BroadcastElementMessage(UiMessageIds.ButtonClick);
            Assert.True(rig.Actions.KillSwitch);
            Assert.True(rig.Actions.DryRun);
            rig.Actions.KillSwitch = false;
            rig.Actions.DryRun = false;
            rig.Frame();
            Assert.Equal("Kill switch: off | Dry run: off | read-only here", inspector.StateText);
        }
    }

    [RequiresLocalDatsFact]
    public void The_hide_button_closes_the_window_and_reopening_catches_up_with_what_happened_meanwhile()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            inspector.Open();
            var rows = inspector.RowCount;
            var button = inspector.Window!.GetChildRecursive(AutomationInspector.CloseId)!;
            button.BroadcastElementMessage(UiMessageIds.ButtonClick);
            Assert.False(inspector.IsOpen);

            var refreshes = inspector.Refreshes;
            rig.Actions.Talk("while closed", ActionSource.Ui);
            rig.Frame();
            rig.Frame();
            Assert.Equal((refreshes, rows), (inspector.Refreshes, inspector.RowCount)); // nothing is rebuilt while it is hidden
            Assert.True(inspector.IsStale);

            inspector.Toggle();
            Assert.True(inspector.IsOpen);
            Assert.Equal((rows + 1, refreshes + 1), (inspector.RowCount, inspector.Refreshes));
            Assert.Contains("Talk", inspector.RowText(0), StringComparison.Ordinal);
            inspector.Close();
            inspector.Close(); // closing twice is harmless
            Assert.False(inspector.IsOpen);
        }
    }

    [RequiresLocalDatsFact]
    public void An_idle_inspector_does_no_work_and_allocates_nothing()
    {
        var (rig, _, inspector) = EnterGame();
        using (rig)
        {
            rig.Actions.Talk("warm", ActionSource.Ui);
            inspector.Open();
            rig.Frame();
            var refreshes = inspector.Refreshes;
            var rebuilds = inspector.Rebuilds;

            for (var i = 0; i < 60; i++)
            {
                rig.Frame();
            }

            Assert.Equal((refreshes, rebuilds), (inspector.Refreshes, inspector.Rebuilds));

            // The inspector's own per-frame work (the frame message and an explicit refresh) allocates nothing when the log has not changed.
            inspector.ListenToGlobalMessage(UiGlobalMessageIds.Frame, 0);
            inspector.Refresh();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                inspector.ListenToGlobalMessage(UiGlobalMessageIds.Frame, 0);
                inspector.Refresh();
            }

            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    [RequiresLocalDatsFact]
    public void A_host_without_the_layout_gets_a_loud_todo_and_a_closed_inspector()
    {
        var log = new RecordingLogSink();
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), Log = log }); // a source that holds no layouts (the dats' own source registers Lifestone's lazily)
        var session = GameSession.Offline(_ => { }, new ManualTimeProvider(), log);
        using var inspector = new AutomationInspector(manager, session);

        inspector.Open();
        inspector.Open();

        Assert.False(inspector.IsOpen);
        Assert.Null(inspector.Window);
        var errors = log.Entries.Where(e => e.Severity == LogSeverity.Error).ToList();
        Assert.Single(errors); // once, however many times it is asked
        Assert.Contains("TODO(g7-inspector-layout)", errors[0].Message, StringComparison.Ordinal);
    }
}
