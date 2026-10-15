using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;
using Lifestone.Game.UI;

namespace Lifestone.Game.World.Sky;

/// <summary>
/// Wires the sky, time and navigation track into the main scene with one call from <c>Main._Ready</c>: the
/// <see cref="SkyController"/> (time of day, light, fog, sky objects), the time and teleport panels, and the golden
/// viewpoint harness when asked for.
/// </summary>
public static class SkyTrack
{
    /// <summary>Layer of the panels: above the core's HUD layer (1).</summary>
    public const int UiLayer = 5;

    private static SkyModels? s_prefetched;

    /// <summary>
    /// Main thread, before the start-up warm-ups (wave-3 performance pass): starts preparing the Region's sky models on
    /// the workers, so <see cref="Attach"/> no longer waits 110-140 ms for them (<see cref="SkyModels.Prefetch"/>).
    /// </summary>
    public static void Prefetch(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Dats is null || !SkyOptions.Parse(OS.GetCmdlineUserArgs()).SkyObjects)
        {
            return;
        }

        s_prefetched = new SkyModels(services);
        s_prefetched.Prefetch(Lifestone.Client.Sky.SkyDirector.ModelUses(services.Sky.Sky).Select(u => u.Id));
    }

    /// <summary>The models <see cref="Prefetch"/> started, once (null when it did not run).</summary>
    internal static SkyModels? TakePrefetched()
    {
        var models = s_prefetched;
        s_prefetched = null;
        return models;
    }

    /// <summary>Adds everything under <paramref name="main"/>. Call after the camera is placed.</summary>
    public static SkyController Attach(Main main, GameServices services, WorldRoot world, FlyCamera camera, bool playMode = false)
    {
        ArgumentNullException.ThrowIfNull(main);
        var options = SkyOptions.Parse(OS.GetCmdlineUserArgs());
        foreach (var error in options.Errors)
        {
            Log.Warn("sky", "bad argument", ("problem", error));
        }

        var sky = new SkyController { Name = "SkyController" };
        main.AddChild(sky);
        sky.Initialize(services, world, camera, options, main.GetNodeOrNull<WorldEnvironment>("WorldEnvironment"));

        // Navigation fix (M1 wave 3): the catalogue with the curated dungeons and interiors, indoor jumps through the
        // interiors, the camera told the viewer's cell (UI/NavigationUi.cs).
        var navigator = NavigationUi.CreateNavigator(main, services, world, camera);
        var automated = services.Options.IsAutomated || options.GoldenDirectory is not null || options.SkyChecks;
        var layer = new CanvasLayer { Name = "SkyUi", Layer = UiLayer };
        main.AddChild(layer);
        // Play mode (M2 G6, D41): M1's time and teleport panels (and their T key) are not made; F1 and F3 stay.
        if (!playMode && (options.Ui ?? !automated))
        {
            layer.AddChild(TimePanel.Create(sky));
            var teleport = TeleportPanel.Create(navigator, camera, layer);
            layer.AddChild(teleport);
            if (options.TeleportOpen)
            {
                teleport.CallDeferred(TeleportPanel.MethodName.Toggle, true);
            }
        }

        // Navigation fix: the About panel (F1, AGPL legal notices) beside the panels, and the --teleport-tour harness.
        NavigationUi.AttachExtras(main, services, world, camera, navigator, showUi: !playMode && (options.Ui ?? !automated), keyOnly: playMode);

        if (options.SkyChecks)
        {
            var checks = new SkyChecks { Name = "SkyChecks" };
            main.AddChild(checks);
            checks.Initialize(sky, world, camera, main.GetNodeOrNull<CanvasLayer>("Hud"), main.SaveScreenshot);
            return sky;
        }

        if (options.GoldenDirectory is not null)
        {
            var runner = new GoldenViewpointsRunner { Name = "GoldenViewpoints" };
            main.AddChild(runner);

            // Navigation fix: hide every HUD layer (the interiors' and objects' status lines too), not only Hud and SkyUi.
            runner.Initialize(services, world, camera, sky, navigator, main.SaveScreenshot, visible => NavigationUi.SetHudVisible(main, visible));
        }

        return sky;
    }
}
