using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanels;

/// <summary>
/// The automation menu (M6 6 MC, the lead's L1; map-menu.md 6.4): a placeholder Lifestone window whose rows are the job menu's (open jobs with their experience and the
/// current task marked, the refused quests with their reason, a stop row), whose choices go to the host-wired delegates and come back in its status line, and which rebuilds on
/// open, on its source's change, and at most once a minute on the clock alone.
/// </summary>
public sealed class AutomationMenuTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly AutopilotTask Tusks = AutopilotTask.ForQuest("tusk-female");
    private static readonly AutopilotTask Wand = AutopilotTask.ForQuest("atlan-wand");

    /// <summary>A hand-built job list as <see cref="JobMenu.Build"/> lays it out: level up, two quests (experience descending), the errand, stop; two quests refused.</summary>
    private static JobList List(int level = 12) => new(
        level,
        [
            new JobEntry(AutopilotTask.Job, JobMenu.LevelUpLabel, null, null, "the ladder and the trophy errand"),
            new JobEntry(Tusks, "do a quest: Tusker Tusks", 12000, null, "Tusker Tusks, level 10-20"),
            new JobEntry(Wand, "do a quest: Atlan Wand", null, null, "Atlan Wand, level 10+"),
            new JobEntry(AutopilotTask.Errand, "errand", null, null, "one logistics trip"),
            new JobEntry(AutopilotTask.Job, JobMenu.StopLabel, null, null, "stop the run where it stands"),
        ],
        [
            new JobEntry(AutopilotTask.ForQuest("bone-dust"), "Bone Dust", 900, "not-due:2026-11-05T10:00:00Z", "not-due:2026-11-05T10:00:00Z"),
            new JobEntry(AutopilotTask.ForQuest("olthoi-nest"), "Olthoi Nest", null, "level-out-of-band", "level-out-of-band"),
        ]);

    private sealed class FakeJobs : IAutopilotJobSource
    {
        public JobList Jobs { get; set; } = List();

        public AutopilotTask Current { get; set; } = AutopilotTask.Job;

        public int Reads { get; private set; }

        JobList IAutopilotJobSource.Jobs
        {
            get
            {
                Reads++;
                return Jobs;
            }
        }

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }

    private (LoginRig Rig, GamePlayScreen Screen, AutomationMenu Menu) EnterGame()
    {
        var rig = new LoginRig(fixture, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        return (rig, screen, Assert.IsType<AutomationMenu>(screen.Menu));
    }

    // ------------------------------------------------------------------ no dats

    [Fact]
    public void The_rows_are_the_open_jobs_then_the_refused_quests_then_stop()
    {
        var rows = AutomationMenu.RowsOf(List());
        Assert.Equal(
            ["job", "quest:tusk-female", "quest:atlan-wand", "errand", "quest:bone-dust", "quest:olthoi-nest", "job"],
            rows.Select(r => r.Task.Name));
        Assert.Equal(JobMenu.StopLabel, rows[^1].Label);
        Assert.True(AutomationMenu.IsStop(rows[^1]));
        Assert.False(AutomationMenu.IsStop(rows[0])); // level up carries the same task; the label tells them apart
        Assert.False(AutomationMenu.IsStop(new JobEntry(AutopilotTask.Job, JobMenu.StopLabel, null, "refused", "refused"))); // only an open stop row stops
    }

    [Fact]
    public void A_row_shows_its_label_and_experience_the_current_task_is_marked_and_a_refused_quest_says_why()
    {
        var list = List();
        Assert.Equal("> xp ? | level up | current", AutomationMenu.FormatRow(list.Jobs[0], AutopilotTask.Job));
        Assert.Equal("xp ? | level up", AutomationMenu.FormatRow(list.Jobs[0], Tusks));
        Assert.Equal("xp 12,000 | do a quest: Tusker Tusks", AutomationMenu.FormatRow(list.Jobs[1], AutopilotTask.Job));
        Assert.Equal("> xp 12,000 | do a quest: Tusker Tusks | current", AutomationMenu.FormatRow(list.Jobs[1], Tusks));
        Assert.Equal("xp ? | errand", AutomationMenu.FormatRow(list.Jobs[3], AutopilotTask.Job));
        Assert.Equal("stop | stop the run where it stands", AutomationMenu.FormatRow(list.Jobs[4], AutopilotTask.Job)); // never marked current, though it carries the job task
        Assert.Equal("(refused) xp ? | Olthoi Nest | level-out-of-band", AutomationMenu.FormatRow(list.Refused[1], AutopilotTask.Job));
        var long1 = AutomationMenu.FormatRow(list.Refused[0], AutopilotTask.Job);
        Assert.True(long1.Length <= AutomationMenu.LineLimit);
        Assert.StartsWith("(refused) xp 900 | Bone Dust | not-due:", long1, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_quest_name_is_cut_but_never_its_experience_or_the_current_mark()
    {
        // M6 6 FXU (m6-menu-row-xp-clipped): the catalog's names are long; the row puts the xp first and cuts only the label.
        var task = AutopilotTask.ForQuest("blood-shreth");
        var entry = new JobEntry(task, "do a quest: Blood Shreth Kill Task, the long way round the Direlands and back again", 120000, null, "a kill task");

        var row = AutomationMenu.FormatRow(entry, AutopilotTask.Job);
        Assert.Equal(AutomationMenu.LineLimit, row.Length);
        Assert.StartsWith("xp 120,000 | do a quest: Blood Shreth Kill Task", row, StringComparison.Ordinal);
        Assert.EndsWith("...", row, StringComparison.Ordinal);

        var current = AutomationMenu.FormatRow(entry, task);
        Assert.Equal(AutomationMenu.LineLimit, current.Length);
        Assert.StartsWith("> xp 120,000 | do a quest: ", current, StringComparison.Ordinal);
        Assert.EndsWith("... | current", current, StringComparison.Ordinal);

        Assert.Equal("xp ?", AutomationMenu.XpText(null));
        Assert.Equal("xp 1,234,567", AutomationMenu.XpText(1234567));
    }

    [Fact]
    public void The_summary_names_the_level_the_open_and_refused_quests_and_the_current_task()
    {
        Assert.Equal("Level 12 | 2 quest(s) open, 2 refused | now: quest:tusk-female", AutomationMenu.FormatSummary(List(), Tusks));
        Assert.Equal("Level 3 | 2 quest(s) open, 2 refused | now: job", AutomationMenu.FormatSummary(List(3), AutopilotTask.Job));
        Assert.StartsWith("No job source in this host", AutomationMenu.FormatSummary(null, AutopilotTask.Job), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ over the user's dats

    [RequiresLocalDatsFact]
    public void The_shell_has_a_menu_that_is_closed_until_opened_and_says_it_is_a_placeholder()
    {
        var (rig, screen, menu) = EnterGame();
        using (rig)
        {
            Assert.Same(menu, screen.Menu);
            Assert.False(menu.IsOpen);
            Assert.Null(menu.Window);
            Assert.True(rig.Manager.Services.Descs.TryGetLayout(AutomationMenu.LayoutId, out _));

            menu.Open();
            Assert.True(menu.IsOpen);
            var window = Assert.IsAssignableFrom<UiElement>(menu.Window);
            Assert.Equal((80, 80, 640, 360), (window.Box.X0, window.Box.Y0, window.Box.Width, window.Box.Height)); // M6 7 C: 640 wide (was 480): a long quest name's row was cut at the window's right edge (frame 11)
            Assert.True(window.GetChildRecursive(AutomationMenu.ListId)!.Box.Width >= AutomationMenu.LineLimit * 8, "a row at the line limit fits the list at 8 px a character"); // the placeholder window's own estimate of the retail font
            foreach (var id in new[] { AutomationMenu.TitleId, AutomationMenu.SummaryId, AutomationMenu.StatusId, AutomationMenu.ListId, AutomationMenu.ScrollbarId, AutomationMenu.CloseId })
            {
                Assert.NotNull(window.GetChildRecursive(id));
            }

            Assert.Contains("placeholder", Assert.IsAssignableFrom<UiTextElement>(window.GetChildRecursive(AutomationMenu.TitleId)).Text, StringComparison.Ordinal);
            Assert.Equal(AutomationMenu.ReadyLine, menu.StatusText);
            Assert.Equal(0, menu.RowCount); // no source wired: nothing listed, and the summary says so
            Assert.StartsWith("No job source", menu.SummaryText, StringComparison.Ordinal);
            Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= LogSeverity.Error);

            window.GetChildRecursive(AutomationMenu.CloseId)!.BroadcastElementMessage(UiMessageIds.ButtonClick);
            Assert.False(menu.IsOpen);
        }
    }

    [RequiresLocalDatsFact]
    public void The_open_menu_lists_the_sources_rows_with_the_current_task_marked()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            menu.Source = new FakeJobs { Current = Tusks };
            menu.Open();
            Assert.Equal(7, menu.RowCount);
            Assert.Equal("xp ? | level up", menu.RowText(0));
            Assert.Equal("> xp 12,000 | do a quest: Tusker Tusks | current", menu.RowText(1));
            Assert.Equal("(refused) xp ? | Olthoi Nest | level-out-of-band", menu.RowText(5));
            Assert.Equal("stop | stop the run where it stands", menu.RowText(6));
            Assert.Equal("Level 12 | 2 quest(s) open, 2 refused | now: quest:tusk-female", menu.SummaryText);
            Assert.Null(menu.RowText(7));
            Assert.DoesNotContain(rig.Log.Entries, e => e.Severity >= LogSeverity.Error);
        }
    }

    [RequiresLocalDatsFact]
    public void Choosing_a_row_calls_the_hosts_delegate_and_shows_its_line_and_stop_calls_stop()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            var chosen = new List<AutopilotTask>();
            var stops = 0;
            menu.Source = new FakeJobs();
            menu.Choose = task =>
            {
                chosen.Add(task);
                return "Autopilot: " + task.Name + " chosen.";
            };
            menu.Stop = () =>
            {
                stops++;
                return "Autopilot: stopping.";
            };
            menu.Open();

            Assert.Equal("Autopilot: quest:tusk-female chosen.", menu.ChooseRow(1));
            Assert.Equal("Autopilot: quest:tusk-female chosen.", menu.StatusText);
            Assert.Equal("Autopilot: quest:tusk-female chosen.", Assert.IsAssignableFrom<UiTextElement>(menu.Window!.GetChildRecursive(AutomationMenu.StatusId)).Text);
            menu.ChooseRow(3);
            Assert.Equal([Tusks, AutopilotTask.Errand], chosen);

            Assert.Equal("Autopilot: stopping.", menu.ChooseRow(6));
            Assert.Equal((1, 2), (stops, chosen.Count)); // the stop row carries the job task but calls Stop, not Choose

            // A refused quest calls neither and says why; a row past the end says so.
            Assert.Equal("Bone Dust is not open: not-due:2026-11-05T10:00:00Z", menu.ChooseRow(4));
            Assert.StartsWith("No row 9", menu.ChooseRow(9), StringComparison.Ordinal);
            Assert.Equal((1, 2), (stops, chosen.Count));
            Assert.Contains(rig.Log.Entries, e => e.Message.Contains("automation menu: row 6 job=stop chosen -> Autopilot: stopping.", StringComparison.Ordinal));
        }
    }

    [RequiresLocalDatsFact]
    public void A_click_on_the_list_chooses_its_row_and_a_second_click_on_it_chooses_it_again()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            var chosen = new List<AutopilotTask>();
            menu.Source = new FakeJobs();
            menu.Choose = task =>
            {
                chosen.Add(task);
                return "ok " + task.Name;
            };
            menu.Open();
            var list = Assert.IsAssignableFrom<UiListBox>(menu.Window!.GetChildRecursive(AutomationMenu.ListId));
            list.SetSelectedIndex(2, broadcast: true);
            list.SetSelectedItem(list.GetItem(2), broadcast: true); // the same row again: retail's reselect
            Assert.Equal([Wand, Wand], chosen);
            Assert.Equal("ok quest:atlan-wand", menu.StatusText);
        }
    }

    [RequiresLocalDatsFact]
    public void A_mouse_click_on_a_row_chooses_it_after_a_click_on_the_shell()
    {
        // M6 6 FXU (m6-menu-row-click): the window is a child of the shell's root (as the tools panel's), so the shell's world viewport no longer lies over its rows for the mouse.
        var (rig, screen, menu) = EnterGame();
        using (rig)
        {
            var chosen = new List<AutopilotTask>();
            menu.Source = new FakeJobs();
            menu.Choose = task =>
            {
                chosen.Add(task);
                return "ok " + task.Name;
            };
            menu.Open();
            Assert.Same(screen.Root, menu.Window!.Parent);

            // A click on the shell first (outside the menu: the world viewport takes it), as a person's would be.
            rig.Manager.Input.MouseMove(20, 300);
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
            rig.Frame();

            var list = Assert.IsAssignableFrom<UiListBox>(menu.Window.GetChildRecursive(AutomationMenu.ListId));
            var box = list.ScreenBox;
            var x = (box.X1 - box.X0) / 2;
            var y = Enumerable.Range(0, box.Y1 - box.Y0).First(y => list.TryGetItemIndexAt(x, y, out var at) && at == 3);
            var (sx, sy) = (box.X0 + x, box.Y0 + y + 2);
            var chain = new List<UiElement>();
            for (var e = rig.Manager.HitTest(sx, sy); e is not null; e = e.Parent)
            {
                chain.Add(e);
            }

            Assert.True(chain.Contains(list), "the row's point hits " + string.Join("<", chain.Select(e => e.ElementId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))) + ", not the menu's list");

            rig.Manager.Input.MouseMove(sx, sy);
            rig.Frame();
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
            rig.Frame();
            Assert.Equal([AutopilotTask.Errand], chosen);
            Assert.Equal("ok errand", menu.StatusText);
        }
    }

    [RequiresLocalDatsFact]
    public void A_row_chosen_with_nothing_wired_says_so_and_warns()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            menu.Source = new FakeJobs();
            menu.Open();
            Assert.Equal(AutomationMenu.UnwiredLine, menu.ChooseRow(0));
            Assert.Equal(AutomationMenu.UnwiredLine, menu.ChooseRow(6));
            Assert.Equal(2, rig.Log.Entries.Count(e => e.Severity == LogSeverity.Warning && e.Message.Contains("nothing is wired", StringComparison.Ordinal)));
        }
    }

    [RequiresLocalDatsFact]
    public void A_change_rebuilds_the_open_menu_on_the_next_frame_and_a_closed_one_on_open()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            var jobs = new FakeJobs();
            menu.Source = jobs;
            menu.Open();
            var rebuilds = menu.Rebuilds;
            Assert.Equal("> xp ? | level up | current", menu.RowText(0));

            jobs.Current = Tusks;
            jobs.Raise();
            Assert.True(menu.IsStale);
            rig.Frame();
            Assert.Equal(rebuilds + 1, menu.Rebuilds);
            Assert.Equal("> xp 12,000 | do a quest: Tusker Tusks | current", menu.RowText(1));

            // Closed: nothing is rebuilt; opening catches up.
            menu.Close();
            jobs.Jobs = List(13) with { Refused = [] };
            jobs.Raise();
            rig.Frame();
            Assert.Equal(rebuilds + 1, menu.Rebuilds);
            menu.Toggle();
            Assert.Equal((rebuilds + 2, 5), (menu.Rebuilds, menu.RowCount));
            Assert.StartsWith("Level 13 | 2 quest(s) open, 0 refused", menu.SummaryText, StringComparison.Ordinal);

            // Unwired: the old source no longer reaches the window.
            menu.Source = null;
            rig.Frame();
            Assert.Equal(0, menu.RowCount);
            var after = menu.Rebuilds;
            jobs.Raise();
            rig.Frame();
            Assert.Equal(after, menu.Rebuilds);
        }
    }

    [RequiresLocalDatsFact]
    public void Without_a_change_the_open_menu_rebuilds_at_most_once_a_minute()
    {
        var (rig, _, menu) = EnterGame();
        using (rig)
        {
            var jobs = new FakeJobs();
            menu.Source = jobs;
            menu.Open();
            var rebuilds = menu.Rebuilds;
            var reads = jobs.Reads;
            for (var i = 0; i < 59; i++)
            {
                rig.Frame(1.0);
            }

            Assert.Equal((rebuilds, reads), (menu.Rebuilds, jobs.Reads)); // 59 s of frames: the rows are not rebuilt, the source not read

            rig.Frame(1.0);
            Assert.Equal(rebuilds + 1, menu.Rebuilds); // a minute on: one rebuild, for a quest timer come due
            for (var i = 0; i < 30; i++)
            {
                rig.Frame(1.0);
            }

            Assert.Equal(rebuilds + 1, menu.Rebuilds);
            rig.Run(31);
            Assert.Equal(rebuilds + 2, menu.Rebuilds);
        }
    }

    [RequiresLocalDatsFact]
    public void A_host_without_the_layout_gets_a_loud_todo_and_a_closed_menu()
    {
        var log = new RecordingLogSink();
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), Log = log });
        using var menu = new AutomationMenu(manager);

        menu.Open();
        menu.Open();

        Assert.False(menu.IsOpen);
        Assert.Null(menu.Window);
        var error = Assert.Single(log.Entries, e => e.Severity == LogSeverity.Error);
        Assert.Contains("TODO(m6-menu-layout)", error.Message, StringComparison.Ordinal);
    }
}
