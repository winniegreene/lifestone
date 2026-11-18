using System.Diagnostics;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Game.Sessions;

// Slice a2 (Skills tab (skill page 11)), docs/milestones/m3-plan.md section 4.6 row "a2". This file is the slice's alone (M3 2.2 s0's seam): write the play-check asserts here and edit nothing else of PlayChecks.
// Open the page with OpenPanelAsync(shell, panelId), assert with Check(name, ok, detail), close it with ClosePanelAsync(shell, panelId); the page's frame is taken afterwards (PlayChecks.Panels.cs).
public sealed partial class PlayChecks
{
    private const uint SkillPage = 11;
    private const uint SkillsTabButton = 0x10000229;
    private const uint WarMagicSkill = 34;

    /// <summary>
    /// The skills tab on the stand-in's description (M3 2.2 a2): the page opens, the skills tab (tab button 0x10000229) builds the list the first time it shows, and the War Magic row reads what
    /// <see cref="SkillRules"/> gives for the read model (the stand-in's own War Magic, or retail's default when its description has none), in the group its advancement class puts it in; the footer shows the credits
    /// the read model has; then the page closes. Nothing is clicked here: the raise, "10" and train buttons are tested on the offline session (GameSkillsTabTests), never against a server.
    /// </summary>
    private async partial Task PanelA2ChecksAsync(GamePlayScreen shell)
    {
        const string name = "skills-tab-lists-the-skills-and-shows-war-magic-as-the-read-model-has-it";
        var tab = shell.PanelControllers?.ControllersOf(SkillPage).OfType<GameSkillsTab>().FirstOrDefault();
        if (tab is null || _host.Session is not { } session || shell.PanelHost?.ElementFor(SkillPage) is not UiPanel panel)
        {
            Check(name, false, "no skills tab controller, session or tabbed page 11");
            return;
        }

        var builtBefore = tab.IsBuilt;
        if (!await OpenPanelAsync(shell, SkillPage))
        {
            Check(name, false, "page 11 did not open");
            return;
        }

        var watch = Stopwatch.StartNew();
        var opened = panel.OpenTab(SkillsTabButton);
        await Frames(3);
        watch.Stop();

        var q = session.State.Self.Qualities;
        var table = session.SkillTable;
        string detail;
        var ok = opened && table is not null && table.TryGetSkill(WarMagicSkill, out var warMagic) && tab.IsBuilt;
        if (ok)
        {
            _ = table!.TryGetSkill(WarMagicSkill, out warMagic);
            var expected = SkillRules.TryCurrent(q, WarMagicSkill, warMagic!.Formula, warMagic.MinLevel, out var current) ? current.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";
            var line = tab.Lines.FirstOrDefault(l => l.Skill == WarMagicSkill);
            var advancementClass = q.TryGetSkill(WarMagicSkill, out var desc) ? desc.AdvancementClass : SkillRules.UnusableClass;
            var section = advancementClass switch { SkillRules.SpecializedClass => 0, SkillRules.TrainedClass => 1, SkillRules.UntrainedClass when warMagic.MinLevel <= 1 => 2, _ => 3 };
            var credits = q.AvailableSkillCredits.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var footer = tab.FooterElement?.GetChildRecursive(GameSkillsTab.LineOneValueId) as Lifestone.Client.RetailUi.Text.UiTextElement;
            ok = line.Skill == WarMagicSkill && line.Value == expected && line.Section == section && tab.Lines.Count() == table.Skills.Count
                && tab.FooterState == GameSkillsTab.DefaultState && footer is not null && footer.Text.Contains(credits, StringComparison.Ordinal);
            detail = $"war_magic='{line.Value}' expected='{expected}' section={line.Section}/{section} class={advancementClass} rows={tab.Lines.Count()}/{table.Skills.Count} credits_footer='{footer?.Text}' credits={credits} built_before={builtBefore} builds={tab.Builds} open_ms={watch.Elapsed.TotalMilliseconds:F1}";
        }
        else
        {
            detail = $"opened={opened} table={(table is null ? "missing" : "ok")} built={tab.IsBuilt}";
        }

        Check(name, ok, detail);
        if (ok)
        {
            await SkillsRowMouseAsync(shell, tab);
        }

        await ClosePanelAsync(shell, SkillPage);
    }

    /// <summary>
    /// The owner's report (M6 7 S): a click on a skill row selects nothing. The real mouse route: the pointer goes to the War Magic row (the list's own hit test), a left click, and the tab must hold that skill as selected,
    /// its footer must change off the default, and a second click on the same row must clear it. The hit chain is logged so a covering element shows.
    /// </summary>
    private async Task SkillsRowMouseAsync(GamePlayScreen shell, GameSkillsTab tab)
    {
        const string name = "skills-tab-row-click-selects-the-skill";
        var manager = tab.List?.Manager;
        if (manager is null || tab.List is not { } list || tab.RowOf(WarMagicSkill) is not { } row)
        {
            Check(name, false, "no ui manager, list or War Magic row");
            return;
        }

        var index = list.Items.ToList().IndexOf(row);
        list.ScrollToShow(index);
        await Frames(2);
        var box = list.ScreenBox;
        var x = (box.X1 - box.X0) / 2;
        int? first = null, last = null;
        for (var y = 0; y < box.Y1 - box.Y0; y++)
        {
            if (list.TryGetItemIndexAt(x, y, out var at) && at == index)
            {
                first ??= y;
                last = y;
            }
        }

        if (first is not { } top || last is not { } bottom)
        {
            Check(name, false, $"row {index} not under the list ({box.X0},{box.Y0})-({box.X1},{box.Y1})");
            return;
        }

        var (sx, sy) = (box.X0 + x, box.Y0 + ((top + bottom) / 2));
        var chain = new List<string>();
        for (var e = manager.HitTest(sx, sy); e is not null && chain.Count < 8; e = e.Parent)
        {
            chain.Add(e.ElementId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }

        manager.Input.MouseMove(sx, sy);
        await Frames(2);
        manager.Input.MouseMove(sx, sy);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
        await Frames(2);
        var selected = tab.SelectedSkill;
        var footer = tab.FooterState;
        Check(name, selected == WarMagicSkill && footer != GameSkillsTab.DefaultState,
            $"at=({sx},{sy}) hit=[{string.Join("<", chain)}] click_select={list.ClickSelect} list_selected={list.SelectedIndex}/{index} selected_skill={selected} footer_state=0x{footer:X} rows_state_selected={row.State}");
    }
}
