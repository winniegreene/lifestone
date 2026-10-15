using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Chat;

/// <summary>
/// <c>TODO(g4-spew-ghost)</c> (docs/retail-ui.md section 21.3): a transparent window repainted in place must leave no trace of what it showed before.
/// The spew box has no art of its own, so its rows are glyphs over whatever the surface held; a dirty rectangle repaints from the surface's erased state
/// (<c>UIRegion::EraseSelf</c>, 11.6096 0x0069FFE0) or the old glyphs show through the new ones.
/// </summary>
public sealed class GameSpewGhostTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void Spew_rows_that_move_leave_no_old_glyphs_behind_in_the_shells_surface()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);

        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), rig.Log);
        var live = new UiCompositor(images, rig.Log);
        live.Attach(rig.Manager);
        Settle(live);

        // Each new notice pushes the older rows down one place: the glyphs of a row land where another row's were.
        string[] lines = ["Cannot log off while in portal space.", "Inventory is full.", "You are not a member of a fellowship.", "In Portal Space - Please Wait..."];
        foreach (var line in lines)
        {
            rig.Screens.Notices.DisplayStringInfo(26, UiStringRef.FromLiteral(line));
            rig.Frame();
            Settle(live);
        }

        var surfaces = new List<IUiRegionView>();
        rig.Manager.CollectSurfaces(surfaces);
        var painted = surfaces.Select(s => live.GetSurface(s)!.Pixels.ToArray()).ToList();

        // The same scene painted whole, from nothing.
        var fresh = new UiCompositor(images, rig.Log);
        fresh.Attach(rig.Manager);
        Settle(fresh);

        for (var i = 0; i < surfaces.Count; i++)
        {
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

            Assert.True(bad < 0, $"surface {i} (root {surfaces[i]}): the repaint differs from a whole paint first at ({bad % width}, {bad / width})");
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
