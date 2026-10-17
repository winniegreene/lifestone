using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The play checks of the panel pages (M3 2.2 s0, docs/milestones/m3-plan.md section 4.6): <see cref="PanelChecksAsync"/> runs once from <c>CheckAllAsync</c>, in the world after the tools teleport (M3 2.2 lead: before the server teleport the panel phase outlasted the stand-in's fixed teleport time). It calls
/// one hook per slice (<c>PlayChecks.PanelA1.cs</c> ... <c>PlayChecks.PanelC2.cs</c>, each a slice's own file: it writes its asserts there and nothing else), then takes the frame of every page that has
/// a controller. A slice opens its page with <see cref="OpenPanelAsync"/> (the path a toolbar click takes), asserts, and closes it with <see cref="ClosePanelAsync"/>.
/// </summary>
public sealed partial class PlayChecks
{
    // The hooks, one stub file each (an async implementation is fine: Task PanelA1ChecksAsync(GamePlayScreen shell)).
    private partial Task PanelA1ChecksAsync(GamePlayScreen shell);

    private partial Task PanelA2ChecksAsync(GamePlayScreen shell);

    private partial Task PanelA3ChecksAsync(GamePlayScreen shell);

    private partial Task PanelB1ChecksAsync(GamePlayScreen shell);

    private partial Task PanelB2ChecksAsync(GamePlayScreen shell);

    private partial Task PanelB3ChecksAsync(GamePlayScreen shell);

    private partial Task PanelC1ChecksAsync(GamePlayScreen shell);

    private partial Task PanelC2ChecksAsync(GamePlayScreen shell);

    private async Task PanelChecksAsync(GamePlayScreen? shell)
    {
        if (shell is not { PanelHost: { } host, PanelControllers: { } panels })
        {
            Check("the-panel-controllers-are-wired-after-the-shell", false, "the shell has no panel host or panel controllers");
            return;
        }

        var registered = panels.RegisteredPages.ToArray();
        Check(
            "a-page-with-a-controller-is-built-and-the-others-say-not-in-m3",
            GamePanelHost.Pages.All(p => host.KindOf(p.PanelId) == (registered.Contains(p.PanelId) || p.PanelId is 3 or 10 ? PanelPageKind.Built : PanelPageKind.NotInM3)),
            $"controllers={panels.ControllerCount} pages_with_controllers=[{string.Join(", ", registered.OrderBy(static id => id))}]");

        await PanelA1ChecksAsync(shell);
        await PanelA2ChecksAsync(shell);
        await PanelA3ChecksAsync(shell);
        await PanelB1ChecksAsync(shell);
        await PanelB2ChecksAsync(shell);
        await PanelB3ChecksAsync(shell);
        await PanelC1ChecksAsync(shell);
        await PanelC2ChecksAsync(shell);

        if (_framesDirectory is not { } directory)
        {
            return;
        }

        // One frame per (page, tab) pair under <frames dir>/panels/ (the shell's ten frames stay ten; PanelFramesPlan): a tabbed page has a frame per tab, 11 attributes and skills, 13 spellbook and components (M3 2.2 fix B, 2.2-R4).
        var folder = PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y));
        var captured = 0;
        var failures = 0;
        var frames = PanelFramesPlan.FramesFor(registered);
        foreach (var frame in frames)
        {
            var page = frame.Page;
            if (!await OpenPanelAsync(shell, page.PanelId, 12))
            {
                failures++;
                Log.Error(LogCategory, "panel frame failed: the page did not open", ("panel", page.PanelId));
                continue;
            }

            if (!PanelFramesPlan.OpenTab(frame, host.ElementFor(page.PanelId)))
            {
                failures++;
                Log.Error(LogCategory, "panel frame failed: the tab did not open", ("panel", page.PanelId), ("tab", frame.TabName));
                await ClosePanelAsync(shell, page.PanelId);
                continue;
            }

            await Frames(8);
            if (SaveCheckedFrame($"{folder}/{PanelFramesPlan.FileName(frame)}", $"panel-{page.PanelId}{(frame.TabButton == 0 ? string.Empty : frame.Letter.ToString())}"))
            {
                captured++;
            }
            else
            {
                failures++;
            }

            await ClosePanelAsync(shell, page.PanelId);
        }

        Check("every-registered-panel-page-has-its-frame", failures == 0 && captured == frames.Count, $"captured={captured}/{frames.Count} pages={registered.Length} failures={failures}");
        Log.Info(LogCategory, PanelFramesPlan.Verdict(captured, failures));
    }

    /// <summary>
    /// Opens the page <paramref name="panelId"/> as a toolbar click does (the page's toggle action, <see cref="GamePanelHost.Toggle"/>) unless it is the page shown, then waits <paramref name="frames"/> frames.
    /// True when the page is shown. A page with no controller opens the NotInM3 message instead (false).
    /// </summary>
    private async Task<bool> OpenPanelAsync(GamePlayScreen shell, uint panelId, int frames = 4)
    {
        if (shell.PanelHost is not { } host)
        {
            return false;
        }

        if (host.CurrentPanel != panelId)
        {
            host.Toggle(panelId);
        }

        await Frames(frames);
        return host.CurrentPanel == panelId && host.ElementFor(panelId)?.Visible == true;
    }

    /// <summary>Closes the page <paramref name="panelId"/> as its toolbar button does when it is the page shown, then waits two frames.</summary>
    private async Task ClosePanelAsync(GamePlayScreen shell, uint panelId)
    {
        if (shell.PanelHost is { } host && host.CurrentPanel == panelId)
        {
            host.Toggle(panelId);
            await Frames(2);
        }
    }
}
