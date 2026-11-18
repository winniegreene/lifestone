using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--live-raise-check</c> (M6 7 S): the owner's report was that a skill cannot be raised by clicking. After login, on <b>Lifestone Throwaway only</b> (any other character is refused with a verdict line), this opens the
/// character panel's skills tab, clicks a trained skill's row through the shell's mouse path, clicks the Raise button, waits for the server's reply and logs one line,
/// <c>raise_check skill=&lt;id&gt; ranks_before= ranks_after= ok=yes|no</c> (ranks read from the session's qualities), then quits (exit 0 on yes, 1 on no). Check, act, check: every step reads the state it needs first and a failed
/// step ends the run with its named reason in the line (<c>why=</c>). It starts no backend and spends only the experience the character already holds.
/// </summary>
public sealed partial class LiveRaiseCheck : Node
{
    private const string LogCategory = "live-raise";
    private const string Character = "Lifestone Throwaway";
    private const uint SkillPage = 11;
    private const uint SkillsTabButton = 0x10000229;

    private enum Stage { WaitWorld, OpenTab, ClickRow, ClickRaise, WaitReply, Quit }

    private SessionHost _host = null!;
    private RetailUiLayer? _ui;
    private Stage _stage = Stage.WaitWorld;
    private ulong _stageAt;
    private int _frames;
    private uint _skill;
    private int _before;
    private bool _done;

    /// <summary>Adds the check to the scene when <c>--live-raise-check</c> is on the command line.</summary>
    public static void InstallIfAsked(Main main, SessionHost host, RetailUiLayer? ui)
    {
        if (!CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Any(a => a.Name == "--live-raise-check"))
        {
            return;
        }

        main.AddChild(new LiveRaiseCheck { Name = "LiveRaiseCheck", ProcessPriority = 70, _host = host, _ui = ui, _stageAt = Time.GetTicksMsec() });
        Log.Info(LogCategory, "--live-raise-check on: it runs on " + Character + " only and quits when it has its verdict");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        try
        {
            Step();
        }
        catch (Exception e)
        {
            Finish(false, "exception " + e.GetType().Name + ": " + e.Message);
        }
    }

    private bool Waited(double seconds) => (Time.GetTicksMsec() - _stageAt) / 1000.0 >= seconds;

    private void To(Stage stage)
    {
        _stage = stage;
        _stageAt = Time.GetTicksMsec();
        _frames = 0;
    }

    private void Step()
    {
        var session = _host.Session;
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var tab = shell?.PanelControllers?.ControllersOf(SkillPage).OfType<GameSkillsTab>().FirstOrDefault();
        var manager = _ui?.Manager;
        var ready = session is not null && shell?.PanelHost is not null && tab?.List is not null && manager is not null
            && session.SkillTable is not null && session.XpTable is not null && session.State.Self.Qualities.IsKnown;
        if (_stage == Stage.WaitWorld)
        {
            if (!ready)
            {
                if (Waited(180))
                {
                    Finish(false, "the world, the skills tab or the tables were not ready in 180 s");
                }

                return;
            }

            if (!string.Equals(_host.Options.Character, Character, StringComparison.OrdinalIgnoreCase))
            {
                Finish(false, "refused: this check runs on " + Character + " only (the run asked for '" + _host.Options.Character + "')");
                return;
            }

            if (++_frames < 60)
            {
                return; // let the world settle
            }

            shell!.PanelHost!.Toggle(SkillPage);
            To(Stage.OpenTab);
            return;
        }

        if (!ready)
        {
            Finish(false, "the session or the panel went away in stage " + _stage);
            return;
        }

        var q = session!.State.Self.Qualities;
        var list = tab!.List!;
        switch (_stage)
        {
            case Stage.OpenTab:
                if (++_frames < 6)
                {
                    return;
                }

                if (shell!.PanelHost!.CurrentPanel != SkillPage || shell.PanelHost.ElementFor(SkillPage) is not UiPanel panel || !panel.OpenTab(SkillsTabButton) || !tab.IsBuilt)
                {
                    Finish(false, "the skill page did not open or the skills tab did not build");
                    return;
                }

                // Pick the cheapest trained or specialised skill the experience covers.
                uint best = 0, bestCost = uint.MaxValue;
                foreach (var line in tab.Lines)
                {
                    if (line.Section > 1 || !q.TryGetSkill(line.Skill, out _))
                    {
                        continue;
                    }

                    var cost = SkillRules.CostToRaise(session.XpTable!, q, line.Skill);
                    if (cost > 0 && cost < bestCost && q.AvailableExperience >= cost)
                    {
                        (best, bestCost) = (line.Skill, cost);
                    }
                }

                if (best == 0)
                {
                    Finish(false, "no trained skill whose next rank the " + q.AvailableExperience + " unassigned experience covers (add experience to the Throwaway first)");
                    return;
                }

                _skill = best;
                _before = q.TryGetSkill(_skill, out var d0) ? d0.Ranks : -1;
                Log.Info(LogCategory, $"raise_check picked skill={_skill} cost={bestCost} xp_available={q.AvailableExperience} ranks_before={_before}");
                var row = tab.RowOf(_skill);
                list.ScrollToShow(row is null ? -1 : list.Items.ToList().IndexOf(row));
                To(Stage.ClickRow);
                return;

            case Stage.ClickRow:
                if (++_frames < 3)
                {
                    return;
                }

                var rowIndex = tab.RowOf(_skill) is { } r ? list.Items.ToList().IndexOf(r) : -1;
                if (!PointOfRow(list, rowIndex, out var rx, out var ry))
                {
                    Finish(false, "the row of skill " + _skill + " is not under the list");
                    return;
                }

                Click(manager!, rx, ry);
                To(Stage.ClickRaise);
                return;

            case Stage.ClickRaise:
                if (++_frames < 4)
                {
                    return;
                }

                if (tab.SelectedSkill != _skill)
                {
                    Finish(false, "the row click did not select the skill (selected=" + tab.SelectedSkill + ")");
                    return;
                }

                if (tab.RaiseButton is not { } button || button.State == UiStateIds.Ghosted)
                {
                    Finish(false, "the Raise button is ghosted or missing after the row selected");
                    return;
                }

                var box = button.ScreenBox;
                Click(manager!, (box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
                To(Stage.WaitReply);
                return;

            case Stage.WaitReply:
                if (tab.LastAction is null)
                {
                    if (Waited(2))
                    {
                        Finish(false, "the Raise click sent no action");
                    }

                    return;
                }

                var after = q.TryGetSkill(_skill, out var d1) ? d1.Ranks : -1;
                if (after > _before)
                {
                    Finish(true, "action=" + tab.LastAction.Status, after);
                    return;
                }

                if (Waited(15))
                {
                    Finish(false, "no rank change in 15 s; action=" + tab.LastAction.Status, after);
                }

                return;
        }
    }

    private static bool PointOfRow(UiListBox list, int index, out int x, out int y)
    {
        (x, y) = (0, 0);
        var box = list.ScreenBox;
        var mid = (box.X1 - box.X0) / 2;
        int? first = null, last = null;
        for (var dy = 0; dy < box.Y1 - box.Y0; dy++)
        {
            if (list.TryGetItemIndexAt(mid, dy, out var at) && at == index)
            {
                first ??= dy;
                last = dy;
            }
        }

        if (first is not { } top || last is not { } bottom)
        {
            return false;
        }

        (x, y) = (box.X0 + mid, box.Y0 + ((top + bottom) / 2));
        return true;
    }

    private static void Click(UiElementManager manager, int x, int y)
    {
        manager.Input.MouseMove(x, y);
        manager.Input.MouseMove(x, y);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
    }

    private void Finish(bool ok, string why, int? after = null)
    {
        var q = _host.Session?.State.Self.Qualities;
        var ranksAfter = after ?? (q is not null && q.TryGetSkill(_skill, out var d) ? d.Ranks : -1);
        var line = $"raise_check skill={_skill} ranks_before={_before} ranks_after={ranksAfter} ok={(ok ? "yes" : "no")} why={why.Replace(' ', '_')}";
        if (ok)
        {
            Log.Info(LogCategory, line);
        }
        else
        {
            Log.Error(LogCategory, line);
        }

        _done = true;
        _stage = Stage.Quit;
        GetTree().Quit(ok ? 0 : 1);
    }
}
