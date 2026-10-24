using System.Diagnostics;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.World;
using Lifestone.Data.Effects;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.UI;
using Lifestone.Game.World;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Objects;
using Lifestone.Game.World.Sky;

namespace Lifestone.Game.PortalSpace;

/// <summary>What the portal view installed that play mode attaches to (docs/portal-space.md §11): the director the session bridge becomes the LoginComplete sink of.</summary>
/// <param name="Director">The director (the machine, the progress, the sinks).</param>
/// <param name="Progress">The teleport progress (<c>SmartBox</c>'s teleport flags), which the session bridge feeds.</param>
/// <param name="Attachments">The world adapter the readiness rule asks; play mode gives it the player's collision streamer.</param>
/// <param name="Presenter">The presenter (priority 15).</param>
public sealed record PortalSpaceHandles(PortalSpaceDirector Director, TeleportProgress Progress, WorldAttachments Attachments, PortalSpacePresenter Presenter, PortalTunnelView View);

/// <summary>
/// The main scene's one call into the portal view (<c>Main._Ready</c>, right after the cinematic attach): installs the frame-budget
/// share, the audio service, the portal tunnel, the presenter that plays portal space around the teleport panel's jumps, and with
/// <c>--portal-space</c> the hold or with <c>--portal-space-frames</c> the scripted capture (docs/portal-space.md §11, m2-plan G1 W1 to W5).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> The frame-budget share (always: the session pump will take its share of the queue), the audio service
/// (<see cref="GameServices.Audio"/>), then the tunnel: the real <see cref="TunnelObject"/> over the dats, its creature-mode view
/// with one warm render, the machine and the director with the retail sound player as the sound sink and the placeholder status
/// line as the notice sink, the world adapter, the presenter, and the offline transition set on the teleport panel's hook
/// (<c>--portal-transition</c>). A failure to build the tunnel (a dat without the UIASSET records) is an error line and no portal
/// view, never a crash; with <c>--portal-space</c> or <c>--portal-space-frames</c> it also quits with exit code 1, since the run
/// could not show what it was asked to.</para>
/// <para>Check suites, the spell viewer and the terrain preview never reach it (they are other scenes or other modes of
/// <c>Main</c>); the headless host has its own host.</para>
/// </remarks>
public static class PortalSpaceSetup
{
    /// <summary>
    /// The tunnel hum: the UIASSET tunnel animation's frame-2 SoundTweaked hook plays Wave 0x0A000316 (docs/portal-space.md §5). Only used
    /// to build its stream ahead of time; the hook itself names the wave when it plays.
    /// </summary>
    private const uint TunnelHumWave = 0x0A000316;

    /// <summary>Monotonic real seconds, the clock every portal-space machine runs on (advanced once per frame by its host).</summary>
    public static double Now() => Time.GetTicksUsec() * 1e-6;

    /// <summary>
    /// Installs the portal view; see the type's remarks. Returns what play mode attaches to (the director for the session bridge, the progress, the
    /// world adapter and the presenter), or null when there is no portal view (no dats, or the tunnel could not be built).
    /// </summary>
    /// <param name="main">The main scene.</param>
    /// <param name="services">The game services.</param>
    /// <param name="world">The streamed world.</param>
    /// <param name="camera">The world camera.</param>
    /// <param name="sky">The sky.</param>
    /// <param name="playerCell">
    /// Play mode (<c>--play</c>): the destination's cell for the readiness rule, the player's own (the session's player placed by the play mode). The
    /// explore scene's teleport hook and panel are then not used (play mode has neither). Null in explore mode.
    /// </param>
    public static PortalSpaceHandles? Install(Main main, GameServices services, WorldRoot world, FlyCamera camera, SkyController sky, Func<uint>? playerCell = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(sky);

        var args = OS.GetCmdlineUserArgs();
        var portal = PortalSpaceOptions.Parse(args);
        foreach (var problem in portal.Errors) // the audio options' own problems are logged by AudioService.Install
        {
            Log.Warn("portal", "bad argument", ("problem", problem));
        }

        // W1: the frame-budget share. Always installed: the session pump takes its share of the queue when it exists.
        main.AddChild(FrameBudgetShare.Create(services));

        var dats = services.Dats;
        var frames = portal.FramesDirectory is not null;
        if (dats is null)
        {
            if (frames)
            {
                main.GetTree().Quit(1); // Main has already shown the missing-dats message; a capture run has nothing to capture
            }

            return null;
        }

        if (frames && portal.Hold)
        {
            Log.Warn("portal", "--portal-space and --portal-space-frames together: the scripted capture wins; the hold is ignored");
        }

        var seed = frames || services.Options.IsAutomated ? 1 : (int)(Stopwatch.GetTimestamp() & 0x7FFFFFFF);

        // W2: the sound card, silent in an automated run unless --volume is given (D8): the one install path (--volume, --mute, --record-audio and the guard),
        // shared with the runs that have no portal view. Without the service, Main's deferred check reports a --record-audio.
        var timer = Stopwatch.StartNew();
        var audio = AudioService.Install(main, services, args, seed, portalView: true);

        // M3 1.3 s2b: retail's outdoor ambient sound (and the sound half of AdminEnvirons) over the service's sound player.
        if (audio is not null)
        {
            AmbientSoundNode.Install(main, services, audio, camera, seed);
        }

        // W3: the tunnel and its view. (Its sounds are built first, on the workers, so the first whoosh and the first hum do not
        // queue behind the world's streaming.)
        timer.Restart();
        try
        {
            var fixedStep = frames ? PortalSpaceFramesRunner.FixedStep : 0.0;
            var now = frames ? 0.0 : Now(); // the tunnel's clock starts when the object is made (retail InitObjectBegin)
            var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
            try
            {
                audio?.Prefetch(audio.WavesOf(assets.UiSoundTableId, PortalSpaceDirector.EnterPortalSoundType, PortalSpaceDirector.ExitPortalSoundType).Append(TunnelHumWave));
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                // A sound that cannot be prepared is silence at the first whoosh (logged), never a portal space that does not exist.
                Log.Warn("portal", "the portal sounds could not be prefetched; they are built when first played, or are silent", ("error", ex.Message));
            }

            var tunnel = TunnelObject.Create(dats, assets, seed, createdAt: now);
            var lens = new WorldLens(camera.Camera);
            var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom((uint)seed), () => lens.RestFocal, portal.LogOffPolicy);
            var progress = new TeleportProgress();
            var notice = PortalNoticeLine.Create(); // D32: a labelled placeholder until the retail spew box exists
            main.AddChild(notice);

            // The director plays the whooshes and the hum through the audio service's retail sound player; the presenter plays none.
            var director = new PortalSpaceDirector(machine, progress, assets, ReadinessPolicy.Default, audio?.Sound, notice, GodotLogSink.Instance);
            var view = PortalTunnelView.Create(services, assets.TunnelSetupId, tunnel, camera.Camera.Fov); // the tunnel starts from the world camera's own field of view (retail's in play mode, Main.ApplyRetailFov)
            main.AddChild(view);
            Log.Info("portal", "portal tunnel ready", ("setup", $"0x{assets.TunnelSetupId:X8}"), ("parts", view.Parts.Count), ("hold", portal.Hold && !frames), ("transition", portal.Transition), ("ms", timer.Elapsed.TotalMilliseconds));

            var interiors = main.GetNodeOrNull<InteriorController>("Interiors");
            // Explore mode's teleport panel plays portal space around its jumps (docs/portal-space.md §9). The hook starts the tunnel in
            // its own frame and moves the camera on the next one (the presenter arms it); until it has moved the destination cell is 0.
            var transition = new OfflinePortalTransition(new ExploreTransition(progress) { Enabled = portal.Transition && playerCell is null });
            var attachments = new WorldAttachments(world, world.GetNodeOrNull<ObjectsSystem>("ObjectsSystem"), interiors);
            var presenter = PortalSpacePresenter.Create(
                director,
                tunnel,
                view,
                lens,
                services.WorldVisibility,
                attachments,
                () => playerCell is not null ? playerCell() : transition.Pending ? 0u : PlayerCell(camera, interiors),
                notice,
                now,
                fixedStep,
                busy => camera.InputSuspended = busy, // retail is busy through the whole sequence; the hidden camera must not fly (G2's busy count is a TODO)
                transition.Arm);
            main.AddChild(presenter);
            if (portal.Hold && !frames)
            {
                presenter.StartHeld();
            }

            // A run without the tool panels (an automated one) has no panel, and the scripted capture jumps through the same hook itself.
            if (playerCell is null && main.GetNodeOrNull<TeleportPanel>("SkyUi/TeleportPanel") is { } panel)
            {
                panel.Teleporter = transition;
            }

            if (frames)
            {
                main.AddChild(PortalSpaceFramesRunner.Create(services, world, Navigator.Create(main, services, world, camera), transition, presenter, portal.FramesDirectory!));
            }

            return new PortalSpaceHandles(director, progress, attachments, presenter, view);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever went wrong in the portal view must not take the explore scene with it (and the run must say so).
            Log.Error("portal", "the portal view could not be built; portal space is not available", ("problem", ex.Message), ("type", ex.GetType().Name));
            if (portal.Hold || frames)
            {
                main.GetTree().Quit(1);
            }

            return null;
        }
    }

    /// <summary>
    /// The destination's cell for the readiness rule: the viewer's EnvCell when it is inside one, else the outdoor cell under the
    /// camera, else 0 (off the map or no position: never ready, and a WARNING every 10 s says so).
    /// </summary>
    private static uint PlayerCell(FlyCamera camera, InteriorController? interiors)
    {
        if (interiors?.Tracker is { } tracker && AcCoordinates.IsEnvCell(tracker.CellId))
        {
            return tracker.CellId;
        }

        return AcCoordinates.TryGetOutdoorCell(camera.WorldPosition, out var cell) ? cell.CellId : 0u;
    }
}
