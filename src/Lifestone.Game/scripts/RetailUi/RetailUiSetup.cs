using System.Diagnostics;
using Godot;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.Sessions;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The main scene's calls into the retail UI (<c>Main._Ready</c>; m2-plan W2-pre(b), G2 step U2). <see cref="AttachExplore"/>
/// installs the UI over the explore scene right after the sky track; <see cref="AttachOnly"/> installs it alone for
/// <c>--retail-ui-only</c>. With <c>--retail-ui</c> off (the default) neither installs anything, parses nothing the run needs and
/// leaves M1's explore mode exactly as it was (<c>--engine-checks</c> still passes unchanged).
/// </summary>
/// <remarks>
/// <para>Check suites, the spell viewer and the terrain preview never reach it (they are other scenes or other modes of
/// <c>Main</c>). A failure to build the UI is an error line and no retail UI, never a crash of the explore scene; in an automated
/// run (<c>--retail-ui-frames</c>) it also quits with exit code 1, since the run could not show what it was asked to.</para>
/// <para><c>--retail-ui=standin</c> runs the retail login screens (the data check, character management, the disconnected screen) on the offline
/// stand-in session. The game screen is the in-game shell (<see cref="Lifestone.Client.RetailUi.Screens.GamePlayScreen"/>, docs/retail-ui.md section 21): the retail
/// layout's windows, those without content named as placeholders on screen (<see cref="IUiPlaceholderScreen"/>). Character creation is built in part: it says what is not
/// (<see cref="IUiPlaceholderScreen"/>), and the 3D character views of its appearance and summary pages wear a label inside their holes
/// (<see cref="ViewPlaceholderNote"/>) until a view is bound to them.</para>
/// </remarks>
public static class RetailUiSetup
{
    /// <summary>The line on screen that says the stand-in screens are placeholders.</summary>
    public const string PlaceholderNote = "placeholder: this screen is a stand-in - the layout's art with no screen logic yet (character creation: M2 G3)";

    /// <summary>The label inside the hole of a 3D character view (character creation's appearance and summary pages) that nothing draws into yet.</summary>
    public const string ViewPlaceholderNote = "placeholder:\nthe 3D character view\nis not bound yet (M2 G3)";

    /// <summary>The canvas layer of the placeholder note: just above the retail UI's slot.</summary>
    public const int NoteLayer = RetailUiLayer.LayerSlot + 1;

    /// <summary>
    /// True when the run asked for the retail UI alone (<c>--retail-ui-only</c>, or <c>--retail-ui-frames</c>, whose frames show the login
    /// screens over the black backdrop), so <c>Main</c> builds no world.
    /// </summary>
    public static bool RequestedAlone(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Parse(report: false) is { Enabled: true } options && (options.UiOnly || options.FramesDirectory is not null);
    }

    /// <summary>
    /// Installs the retail UI over the explore scene (right after <c>SkyTrack.Attach</c>): the layer, the placeholder note, and the
    /// explore camera's hold while a text field has the keyboard. Returns the layer, or null when the UI is off or could not be built.
    /// </summary>
    public static RetailUiLayer? AttachExplore(Main main, GameServices services, FlyCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var layer = Install(main, services);
        if (layer is not null)
        {
            layer.KeyboardCapturedChanged += captured => camera.InputHeldByRetailUi = captured; // its own hold: never writes InputEnabled, which the teleport panel and the runners own
        }

        return layer;
    }

    /// <summary>
    /// Installs the retail UI with no world behind it (<c>--retail-ui-only</c>). With <c>--screenshot</c> it also saves the settled window
    /// and quits (<see cref="RetailUiOnlyCapture"/>), since <c>Main</c>'s own screenshot mode has no world to wait for. Returns the layer, or null.
    /// </summary>
    public static RetailUiLayer? AttachOnly(Main main, GameServices services)
    {
        // No world and so no portal view to install the audio service: install it here, so the UI's sounds play (silently when automated, D8) and are counted, not dropped (M3 1.3 s2a).
        if (services.Dats is not null)
        {
            _ = AudioService.Install(main, services, OS.GetCmdlineUserArgs(), services.Options.IsAutomated ? 1 : (int)(Stopwatch.GetTimestamp() & 0x7FFFFFFF));
        }

        var layer = Install(main, services);
        if (layer is not null && layer.Options.FramesDirectory is null && services.Options.ScreenshotPath is { } path)
        {
            main.AddChild(RetailUiOnlyCapture.Create(layer, path, services.Options.ScreenshotFrames));
        }

        return layer;
    }

    private static RetailUiOptions Parse(bool report)
    {
        var options = RetailUiOptions.Parse(OS.GetCmdlineUserArgs());
        if (report)
        {
            foreach (var problem in options.Errors)
            {
                Log.Warn("retailui", "bad argument", ("problem", problem));
            }
        }

        return options;
    }

    private static RetailUiLayer? Install(Main main, GameServices services)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        var options = Parse(report: true);
        var host = SessionHost.Instance is { Session: not null } installed ? installed : null;
        if (host is not null)
        {
            // Play mode: the retail screens are the login flow (data check, character management and creation), on the live session.
            options = options with { Mode = RetailUiMode.Live };
        }

        if (!options.Enabled)
        {
            return null;
        }

        var automated = options.FramesDirectory is not null;
        if (services.Dats is null)
        {
            Log.Error("retailui", "the retail UI needs the dats; they are not available", ("problem", services.StartupError ?? "unknown"));
            if (automated)
            {
                main.GetTree().Quit(1);
            }

            return null;
        }

        try
        {
            var windowSize = main.GetViewport().GetVisibleRect().Size;
            var layer = RetailUiLayer.Create(
                services,
                options,
                new Vector2I((int)windowSize.X, (int)windowSize.Y),
                () => AudioService.Current?.Sound,
                fixedStep: automated ? RetailUiFramesRunner.FixedStep : 0.0,
                host: host,
                soundMuted: () => AudioService.Current?.IsSilent ?? true);
            main.AddChild(layer);
            var note = CreateNote();
            note.Visible = false;
            main.AddChild(note);
            var notes = new NoteController(note, layer.Flow!.Flow);
            layer.Flow.Flow.ModeChanged += _ => notes.OnModeChanged();
            AddViewPlaceholders(note, layer);
            if (automated)
            {
                // The review frames (U9): the runner steps the layer itself at a fixed step, saves the frames and quits with the verdict.
                main.AddChild(RetailUiFramesRunner.Create(layer, options.FramesDirectory!));
            }

            return layer;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("retailui", "the retail UI could not be built; the explore scene runs without it", ("problem", ex.Message), ("type", ex.GetType().Name));
            if (automated)
            {
                main.GetTree().Quit(1);
            }

            return null;
        }
    }

    /// <summary>Keeps the note's text and visibility to what the screen on show says is a placeholder (a stand-in, or a screen that is built in part).</summary>
    private sealed class NoteController(CanvasLayer note, UiFlow flow)
    {
        private IUiPlaceholderScreen? _watched;

        public void OnModeChanged()
        {
            if (_watched is not null)
            {
                _watched.PlaceholderNoteChanged -= Refresh;
            }

            _watched = flow.CurrentScreen as IUiPlaceholderScreen;
            if (_watched is not null)
            {
                _watched.PlaceholderNoteChanged += Refresh;
            }

            Refresh();
        }

        private void Refresh()
        {
            var text = _watched is not null ? _watched.PlaceholderNote : LoginScreens.IsStandIn(flow.CurrentScreen, flow.CurrentMode) ? PlaceholderNote : null;
            note.GetNode<Label>("Note").Text = text ?? string.Empty;
            note.Visible = text is not null;
        }
    }

    /// <summary>
    /// A label inside the hole of each 3D character view (character creation's appearance and summary pages) while no source draws there, so an
    /// empty hole is never mistaken for a bug.
    /// </summary>
    private static void AddViewPlaceholders(CanvasLayer parent, RetailUiLayer layer)
    {
        var labels = new Dictionary<uint, Label>();
        foreach (var id in new[] { CharGenMainScreen.AppearanceViewportId, CharGenMainScreen.SummaryViewportId })
        {
            var label = new Label
            {
                Name = $"ViewNote{id:X8}",
                Text = ViewPlaceholderNote,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visible = false,
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.55f, 0.95f));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 3);
            parent.AddChild(label);
            labels[id] = label;
        }

        layer.Viewports.HoleChanged += (id, rect, visible) =>
        {
            if (labels.TryGetValue(id, out var label))
            {
                label.Position = new Vector2(rect.Position.X, rect.Position.Y);
                label.Size = new Vector2(rect.Size.X, rect.Size.Y);
                label.Visible = visible && !layer.Viewports.HasSource(id);
            }
        };
    }

    private static CanvasLayer CreateNote()
    {
        var layer = new CanvasLayer { Name = "RetailUiPlaceholderNote", Layer = NoteLayer };
        var label = new Label { Name = "Note", Text = PlaceholderNote, MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(8f, 4f) };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.55f, 0.9f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        layer.AddChild(label);
        return layer;
    }
}
