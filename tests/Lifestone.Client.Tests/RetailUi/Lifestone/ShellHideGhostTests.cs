using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanelTests;

/// <summary>
/// A window parented to the in-game shell's root paints into the root's surface, which has no art (g1 cleared its backdrop) and does not erase its dirty areas by itself. Hiding such a window dirties its box,
/// and the repaint must leave nothing of what it showed (docs/retail-ui.md section 21.3): the incremental paint equals a whole paint, as <c>GameSpewGhostTests</c> checks for the spew box.
/// </summary>
public sealed class ShellHideGhostTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly StandInVisit Visit = new() { EnterAfter = 0.2, UnhideAfter = 1.0, LogOffDelay = 1.0 };

    private (LoginRig Rig, GamePlayScreen Screen) Enter()
    {
        var rig = new LoginRig(
            fixture,
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, Visit);
                StandInSessions.AnswerWorld(h, Visit, TimeSpan.FromSeconds(0.1));
            },
            startMode: UiModes.GamePlay,
            liveGame: true);
        rig.Run(3.0);
        return (rig, Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen));
    }

    [RequiresLocalDatsFact]
    public void Closing_the_tools_panel_leaves_no_trace_in_the_shells_surface()
    {
        var (rig, screen) = Enter();
        using var _ = rig;
        var panel = screen.ToolsUi!;
        HideLeavesNothing(rig, () => Assert.True(panel.Open()), panel.Close);
    }

    [RequiresLocalDatsFact]
    public void Closing_the_panel_host_and_a_floating_chat_leaves_no_trace_in_the_shells_surface()
    {
        var (rig, screen) = Enter();
        using var _ = rig;
        var host = screen.PanelHost!.Window!;
        HideLeavesNothing(rig, () => host.SetVisible(true), () => host.SetVisible(false), "panel host");

        foreach (var chat in screen.Chat!.Windows.Select(w => w.Window))
        {
            var was = chat.Visible;
            HideLeavesNothing(rig, () => chat.SetVisible(true), () => chat.SetVisible(false), $"chat {chat.ElementId:X8}");
            chat.SetVisible(was);
        }
    }

    private void HideLeavesNothing(LoginRig rig, Action show, Action hide, string label = "tools panel")
    {
        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), rig.Log);
        var live = new UiCompositor(images, rig.Log);
        live.Attach(rig.Manager);
        Settle(live);

        show();
        rig.Frame();
        Settle(live);
        hide();
        rig.Frame();
        Settle(live);

        var surfaces = new List<IUiRegionView>();
        rig.Manager.CollectSurfaces(surfaces);
        var painted = surfaces.Select(s => live.GetSurface(s)!.Pixels.ToArray()).ToList();

        var fresh = new UiCompositor(images, rig.Log);
        fresh.Attach(rig.Manager);
        Settle(fresh);

        for (var i = 0; i < surfaces.Count; i++)
        {
            if (!surfaces[i].IsVisible)
            {
                continue; // a hidden surface is never shown, and a whole paint does not paint it
            }

            var whole = fresh.GetSurface(surfaces[i])!;
            var width = whole.Width;
            var bad = -1;
            for (var p = 0; p < whole.Pixels.Length; p += 4)
            {
                if (whole.Pixels[p + 3] != painted[i][p + 3] || (whole.Pixels[p + 3] != 0 && !whole.Pixels.AsSpan(p, 3).SequenceEqual(painted[i].AsSpan(p, 3))))
                {
                    bad = p / 4;
                    break;
                }
            }

            Assert.True(bad < 0, $"{label}: surface {i} (root {surfaces[i]}): the repaint differs from a whole paint first at ({bad % width}, {bad / width})");
        }
    }

    private static void Settle(UiCompositor compositor)
    {
        for (var i = 0; i < 2000 && (compositor.HasPendingWork || i == 0); i++)
        {
            if (!compositor.Composite(long.MaxValue))
            {
                Thread.Sleep(2);
            }
        }

        Assert.False(compositor.HasPendingWork);
    }
}
