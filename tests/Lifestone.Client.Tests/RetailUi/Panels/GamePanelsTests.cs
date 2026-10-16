using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The panel seam (M3 2.2 s0, <see cref="GamePanels"/>): a page with a registered controller opens as a page and calls the controller's OnShown, Step and OnHidden; a page without one shows the NotInM3 message;
/// the staged wiring builds one controller a unit. A fake controller stands in for the slices'.
/// </summary>
public sealed class GamePanelsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint InventoryButton = 0x100001B1;
    private const uint MapButton = 0x1000019A;
    private const uint InventoryPanel = 7;

    private sealed class Fake(uint panelId) : IGamePanel
    {
        public uint PanelId => panelId;

        public int Shown { get; private set; }

        public int Hidden { get; private set; }

        public int Steps { get; private set; }

        public double LastNow { get; private set; }

        public void OnShown() => Shown++;

        public void OnHidden() => Hidden++;

        public void Step(double now)
        {
            Steps++;
            LastNow = now;
        }
    }

    private static GamePlayScreen EnterGame(LoginRig rig)
    {
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    private static string TextOf(UiDialog dialog) => Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text;

    [RequiresLocalDatsFact]
    public void A_page_without_a_registered_controller_answers_NotInM3_and_the_g6_pages_are_always_built()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var panels = Assert.IsType<GamePanels>(screen.PanelControllers);
        Assert.Equal(panels.RegisteredPages.Sum(id => panels.ControllersOf(id).Count), panels.ControllerCount); // M3 2.2: the slices register their pages here, so the count is whatever they added
        foreach (var page in GamePanelHost.Pages)
        {
            var expected = page.PanelId is 3 or 10 || panels.RegisteredPages.Contains(page.PanelId) ? PanelPageKind.Built : PanelPageKind.NotInM3;
            Assert.Equal(expected, screen.PanelHost!.KindOf(page.PanelId));
        }
    }

    [RequiresLocalDatsFact]
    public void A_page_with_a_controller_opens_as_a_page_and_calls_OnShown_Step_and_OnHidden()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var panels = screen.PanelControllers!;
        var host = screen.PanelHost!;
        var fake = new Fake(InventoryPanel);
        var other = new Fake(InventoryPanel); // two controllers on one page: the tabs
        panels.RegisterPage(InventoryPanel, fake);
        panels.RegisterPage(InventoryPanel, other);
        Assert.Equal(PanelPageKind.Built, host.KindOf(InventoryPanel));
        Assert.Equal([fake, other], panels.ControllersOf(InventoryPanel).OfType<Fake>().ToArray()); // the inventory slice (b1) has its own controller on the page too
        var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        rig.Frame();
        Assert.Equal(0, fake.Steps); // hidden: no step

        Assert.True(rig.Click(button));
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs); // a page, not the message
        Assert.True(host.IsOpen);
        Assert.Equal(InventoryPanel, host.CurrentPanel);
        Assert.True(host.ElementFor(InventoryPanel)!.Visible);
        Assert.Equal((1, 0, 1, 0), (fake.Shown, fake.Hidden, other.Shown, other.Hidden));

        var steps = fake.Steps;
        rig.Frame();
        rig.Frame();
        Assert.Equal(steps + 2, fake.Steps); // once per shell frame while shown
        Assert.Equal(other.Steps, fake.Steps);
        Assert.Equal(rig.Manager.Now, fake.LastNow);

        Assert.True(rig.Click(button)); // the same button closes it
        rig.Frame();
        Assert.False(host.IsOpen);
        Assert.Equal((1, 1, 1, 1), (fake.Shown, fake.Hidden, other.Shown, other.Hidden));
        steps = fake.Steps;
        rig.Frame();
        Assert.Equal(steps, fake.Steps); // hidden again: no step
    }

    [RequiresLocalDatsFact]
    public void A_page_that_another_replaces_is_hidden_and_the_new_one_shown_and_a_page_without_a_controller_still_says_NotInM3()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var panels = screen.PanelControllers!;
        var host = screen.PanelHost!;
        var inventory = new Fake(InventoryPanel);
        var skills = new Fake(11);
        panels.RegisterPage(InventoryPanel, inventory);
        panels.RegisterPage(11, skills);

        host.Toggle(InventoryPanel);
        rig.Frame();
        host.Toggle(11);
        rig.Frame();
        Assert.Equal(11u, host.CurrentPanel);
        Assert.Equal((1, 1), (inventory.Shown, inventory.Hidden));
        Assert.Equal((1, 0), (skills.Shown, skills.Hidden));

        // The map has no controller: its button shows the message and the shown page stays.
        var map = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(MapButton));
        Assert.True(rig.Click(map));
        rig.Frame();
        var dialog = Assert.Single(rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(NotInM3.Prefix + "Map", TextOf(dialog));
        Assert.Equal(11u, host.CurrentPanel);
        Assert.Equal(PanelPageKind.NotInM3, host.KindOf(16));
    }

    [RequiresLocalDatsFact]
    public void The_staged_wiring_builds_one_controller_a_unit_and_registers_what_each_returns()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var ready = screen.PanelControllers!;

        var panels = new GamePanels(ready.Context, screen.PanelHost!, screen);
        var built = new List<int>();
        // Pages 12 and 14 belong to no slice of M3 2.2 (the slices' controllers are queued after these four by Build and are units of their own).
        panels.Queue(p => { built.Add(1); return new Fake(12); });
        panels.Queue(p => { built.Add(2); return new Fake(12); });
        panels.Queue(p => { built.Add(3); return null; }); // a page missing from the layout: nothing registered, the unit still yields
        panels.Queue(p => { built.Add(4); return new Fake(14); });

        var units = 0;
        var wiring = panels.Build();
        while (wiring.MoveNext())
        {
            units++;
            Assert.Equal(Math.Min(units, 4), built.Count); // exactly one construction between two yields
        }

        Assert.True(units >= 4);
        Assert.True(panels.ControllerCount >= 3);
        Assert.Equal(2, panels.ControllersOf(12).Count);
        Assert.Single(panels.ControllersOf(14));
        Assert.Equal(PanelPageKind.Built, screen.PanelHost!.KindOf(14));
    }

    [RequiresLocalDatsFact]
    public void The_staged_shell_runs_the_panel_wiring_inside_its_units()
    {
        using var rig = new LoginRig(fixture, liveGame: true, stageShell: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        for (var i = 0; i < 400 && !screen.IsBuilt; i++)
        {
            rig.Frame();
        }

        Assert.True(screen.IsBuilt);
        Assert.NotNull(screen.PanelControllers);
        Assert.NotNull(screen.CharacterInfo); // the controllers are built after the shell's own
        Assert.NotNull(screen.IndicatorsUi);
    }

    [Fact]
    public void The_panel_frames_name_each_page_by_its_id_and_name_and_go_under_panels()
    {
        Assert.Equal("11-skills-and-attributes.png", PanelFramesPlan.FileName(GamePanelHost.Pages.Single(p => p.PanelId == 11)));
        Assert.Equal("7-inventory-backpack-and-paperdoll.png", PanelFramesPlan.FileName(GamePanelHost.Pages.Single(p => p.PanelId == 7)));
        Assert.Equal("out/800x600/panels", PanelFramesPlan.FolderFor("out/800x600/"));
        Assert.Equal([11u, 7u], PanelFramesPlan.PagesWithFrames([11u, 7u, 99u]).Select(p => p.PanelId).Reverse().ToArray()); // in the host's page order, unknown ids dropped
        Assert.Empty(PanelFramesPlan.PagesWithFrames([]));
        Assert.Equal("panel frames done captured=3 failures=0", PanelFramesPlan.Verdict(3, 0));
    }

    [Fact]
    public void The_panel_frames_are_one_per_page_and_tab_and_the_file_names_say_the_tab()
    {
        // M3 2.2 fix B, 2.2-R4: 11 attributes and skills, 13 spellbook and components, then 7, 4, 5, 15 once each.
        var frames = PanelFramesPlan.FramesFor([7u, 11u, 13u, 4u, 5u, 15u, 99u]);
        Assert.Equal(
            ["7-inventory-backpack-and-paperdoll.png", "11a-attributes.png", "11b-skills.png", "13a-spellbook.png", "13b-components.png", "4-beneficial-effects.png", "5-harmful-effects.png", "15-vitae.png"],
            frames.Select(PanelFramesPlan.FileName).ToArray());
        Assert.Equal([0x10000228u, 0x10000229u], frames.Where(f => f.Page.PanelId == 11).Select(f => f.TabButton).ToArray());
        Assert.Equal([0x100002A9u, 0x100002AAu], frames.Where(f => f.Page.PanelId == 13).Select(f => f.TabButton).ToArray());
        Assert.All(frames.Where(f => f.Page.PanelId is not (11 or 13)), f => Assert.Equal(0u, f.TabButton));
        Assert.Empty(PanelFramesPlan.FramesFor([]));
    }

    [Fact]
    public void The_panel_shots_option_is_parsed_and_registered()
    {
        Assert.Equal("cache/x", PlayOptions.Parse(["--play-panel-shots=cache/x"]).PanelShotsDirectory);
        Assert.Null(PlayOptions.Parse([]).PanelShotsDirectory);
        Assert.NotEmpty(PlayOptions.Parse(["--play-panel-shots"]).Errors);
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Play().Options, o => o.Name == "--play-panel-shots");
    }
}
