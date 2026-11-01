using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Shell;

/// <summary>
/// The toolbar's panel buttons as one radio group (M5 7 P7r, D148, <c>m5-8-toolbar-radio</c>): <c>gmToolbarUI::RecvNotice_SetPanelVisibility</c> (11.6096 0x004BDF90) puts the button of the page shown in Highlight and the button of the page
/// hidden in Normal, and the host shows one page at a time, so only the open page's button is lit, whichever path opened or closed it (a click, a key, the autopilot's <see cref="GamePanelHost.Toggle"/> and
/// <see cref="GamePanelHost.SetPanelVisibility"/>, a page that overlays another, a page with no controller).
/// </summary>
public sealed class ToolbarRadioTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Inventory = 7;
    private const uint Options = 10;
    private const uint Skills = 11;
    private const uint Spells = 13;

    private static GamePlayScreen EnterGame(LoginRig rig)
    {
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    private static UiButton Button(GamePlayScreen screen, uint panelId) => Assert.IsAssignableFrom<UiButton>(screen.ToolbarUi!.PanelButtonFor(panelId));

    private static void Press(LoginRig rig, GamePlayScreen screen, uint panelId)
    {
        Assert.True(rig.Click(Button(screen, panelId)));
        rig.Frame();
        rig.Frame();
    }

    [RequiresLocalDatsFact]
    public void Every_panel_button_is_a_toggle_button_with_a_highlight_state_in_the_dats()
    {
        // The layout's facts the radio rests on: a click flips a toggle button's own highlight (UIElement_Button::MouseUp, property 0x0B), so without the toolbar's notice two buttons stay lit (the owner's screenshot).
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        foreach (var (buttonId, panelId) in GameToolbar.PanelButtons)
        {
            var button = Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(buttonId));
            Assert.True(button.InqProperty(UiPropertyIds.ButtonBooleanButton, out _), $"0x{buttonId:X8} (panel {panelId}) has no toggle property 0x0B");
            Assert.True(button.Desc.States.ContainsKey(UiStateIds.Highlight), $"0x{buttonId:X8} has no Highlight state");
            Assert.False(GameToolbar.IsLit(button), $"0x{buttonId:X8} is lit at login");
        }
    }

    [RequiresLocalDatsFact]
    public void One_panel_button_is_lit_after_any_sequence_of_opens_and_closes_by_click_key_autopilot_and_overlay()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var host = screen.PanelHost!;
        var toolbar = screen.ToolbarUi!;
        Assert.Empty(toolbar.LitPanelButtons);

        // Clicks: each opens its page, and the page it replaces unlights (the owner's screenshot had the spell and skill buttons lit together).
        Press(rig, screen, Inventory);
        Assert.Equal(Inventory, host.CurrentPanel);
        Assert.Equal([Inventory], toolbar.LitPanelButtons);
        Press(rig, screen, Spells);
        Assert.Equal(Spells, host.CurrentPanel);
        Assert.Equal([Spells], toolbar.LitPanelButtons);
        Save(rig, "toolbar-spells-open"); // the review frame: the star alone lit, the arm and the rest unlit
        Press(rig, screen, Skills);
        Assert.Equal([Skills], toolbar.LitPanelButtons);
        Press(rig, screen, Options);
        Assert.Equal([Options], toolbar.LitPanelButtons);

        // The same button closes its page: nothing is lit.
        Press(rig, screen, Options);
        Assert.False(host.IsOpen);
        Assert.Empty(toolbar.LitPanelButtons);

        // The autopilot's open (host.Toggle, what its panel driver calls) and close (SetPanelVisibility false) light and unlight the button though no button was clicked.
        Assert.True(host.Toggle(Inventory));
        rig.Frame();
        Assert.Equal([Inventory], toolbar.LitPanelButtons);
        host.SetPanelVisibility(Spells, true);
        Assert.Equal([Spells], toolbar.LitPanelButtons);
        host.SetPanelVisibility(Spells, false);
        Assert.Empty(toolbar.LitPanelButtons);

        // A page with no toolbar button that overlays another (character information over the skills): no button is lit while it shows, and the page it brings back lights its own.
        Press(rig, screen, Skills);
        Assert.Equal([Skills], toolbar.LitPanelButtons);
        host.SetPanelVisibility(3, true);
        Assert.Equal(3u, host.CurrentPanel);
        Assert.Empty(toolbar.LitPanelButtons);
        host.SetPanelVisibility(3, false);
        Assert.Equal(Skills, host.CurrentPanel);
        Assert.Equal([Skills], toolbar.LitPanelButtons);

        // A page with no controller (the social page) opens the NotInM3 message and no page: its toggle button flipped on the click, and the group is re-lit from the page shown (the skills, still open).
        Press(rig, screen, 12);
        Assert.Single(rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(Skills, host.CurrentPanel);
        Assert.Equal([Skills], toolbar.LitPanelButtons);
        Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(rig.Manager.Dialogs.OpenDialogs.First().GetChildRecursive(UiMessageDialog.OkButton))));
        rig.Frame();

        // A key opens a page as its button does (DoVisibilityToggleAction on the page's action).
        Assert.True(rig.Manager.DoVisibilityToggleAction(host.ElementFor(Inventory)!.VisibilityToggleAction));
        rig.Frame();
        Assert.Equal([Inventory], toolbar.LitPanelButtons);
        Assert.Contains(rig.Log.Lines, l => l.Contains("toolbar: the panel button 0x100001B1 (panel 7) is lit", StringComparison.Ordinal));

        Save(rig, "toolbar-inventory-open");
    }

    private void Save(LoginRig rig, string name)
    {
        if (UiReviewFrames.Save(rig, fixture, name) is { } frame)
        {
            output.WriteLine($"wrote {frame}");
        }
    }
}
