using System.Globalization;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Game.Sessions;

// Slice a1 (Attributes tab (skill page 11)), docs/milestones/m3-plan.md section 4.6 row "a1". This file is the slice's alone (M3 2.2 s0's seam): write the play-check asserts here and edit nothing else of PlayChecks.
// Open the page with OpenPanelAsync(shell, panelId), assert with Check(name, ok, detail), close it with ClosePanelAsync(shell, panelId); the page's frame is taken afterwards (PlayChecks.Panels.cs).
public sealed partial class PlayChecks
{
    /// <summary>
    /// M3 2.2 a1: the skill page opens on the attributes tab and its Strength row and Max Mana row show what the read model holds for the stand-in (the attribute with the registry applied, the mana current over
    /// the enchanted maximum). A read of the UI against the session: nothing is clicked, so the stand-in's experience is untouched for the checks that follow.
    /// </summary>
    private async partial Task PanelA1ChecksAsync(GamePlayScreen shell)
    {
        const string name = "the-attributes-tab-shows-the-stand-ins-strength-and-max-mana";
        if (_host.Session is not { } session || shell.PanelControllers is not { } panels || shell.PanelHost is not { } host)
        {
            Check(name, false, "no session or panel controllers");
            return;
        }

        var tab = panels.ControllersOf(GameAttributesTab.SkillPagePanelId).OfType<GameAttributesTab>().FirstOrDefault();
        if (tab is null)
        {
            Check(name, false, "the skill page has no attributes controller");
            return;
        }

        var opened = await OpenPanelAsync(shell, GameAttributesTab.SkillPagePanelId);
        var panel = host.ElementFor(GameAttributesTab.SkillPagePanelId) as UiPanel;
        var q = session.State.Self.Qualities;
        var stats = new StatValues(q);
        var haveStrength = stats.TryAttribute(1, out var strengthBase, out var strength);
        var haveMana = stats.TryVital(5, out var manaCurrent, out var manaBase, out var manaMax, out var manaVitae);
        var strengthRow = tab.LineOf(false, 1)?.ValueText ?? string.Empty;
        var manaRow = tab.LineOf(true, 5)?.ValueText ?? string.Empty;
        var wantStrength = strength.ToString(CultureInfo.InvariantCulture);
        var wantMana = string.Create(CultureInfo.InvariantCulture, $"{manaCurrent}/{manaMax}");

        // M3 2.2 fix B (2.2-R18): the value colours, against the rule of AttributeInfoRegion::Update (0x004F29F0) and Attribute2ndInfoRegion::Update (0x004F2AE0) on the values StatValues gives: 1 when the registry raised the value
        // over its base, 2 when it lowered it, 0 when it is the base; the vitae alone does not change a vital's colour.
        static int Colour(long raw, long modified) => raw < modified ? 1 : raw > modified ? 2 : 0;
        var strengthColour = tab.LineOf(false, 1)?.ValueColor ?? -1;
        var manaColour = tab.LineOf(true, 5)?.ValueColor ?? -1;
        var wantStrengthColour = Colour(strengthBase, strength);
        var wantManaColour = Colour(manaBase, (long)manaMax - manaVitae);
        Check(
            name,
            opened && haveStrength && haveMana && panel?.OpenTabId == GameAttributesTab.TabId && tab.IsTabShown && strengthRow == wantStrength && manaRow == wantMana && strengthColour == wantStrengthColour && manaColour == wantManaColour,
            $"opened={opened} open_tab=0x{panel?.OpenTabId ?? 0:X8} strength_row='{strengthRow}' want='{wantStrength}' mana_row='{manaRow}' want='{wantMana}' strength_colour={strengthColour} want={wantStrengthColour} (base {strengthBase}) mana_colour={manaColour} want={wantManaColour} (base max {manaBase}, vitae {manaVitae}) redraws={tab.Redraws}");
        // M6 7 S: the real mouse route on a row (the owner's report was a skill row that would not select): a left click on Strength selects it and the raise button reads its state.
        if (tab.LineOf(false, 1) is { } strength1 && tab.List is { } attrList && attrList.GetItem(tab.StatLines.ToList().IndexOf(strength1)) is { } strengthLine && attrList.Manager is { } manager)
        {
            attrList.ScrollToShow(tab.StatLines.ToList().IndexOf(strength1));
            await Frames(2);
            await ClickAsync(this, manager, strengthLine);
            var raise = tab.RaiseButton;
            Check("attributes-tab-row-click-selects-the-attribute", tab.SelectedIndex >= 0 && raise is not null,
                $"selected_index={tab.SelectedIndex} raise_state=0x{raise?.State ?? 0:X} xp_available={q.AvailableExperience}");
        }

        await ClosePanelAsync(shell, GameAttributesTab.SkillPagePanelId);
        Check("the-skill-page-closes-after-the-attributes-check", !host.IsOpen && host.CurrentPanel == 0, $"open={host.IsOpen} panel={host.CurrentPanel}");
    }
}
