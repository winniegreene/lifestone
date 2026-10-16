using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// <c>--retail-ui-frames[=DIR]</c> (M2 G2 step U9): plays the login screens on the offline stand-in session at a fixed step of 1/60 s,
/// saves the review frames of the window and quits. It runs the real flow: the screens, the dialogs and the session are the ones
/// <c>--retail-ui=standin</c> shows; only the clicks and the typed words are the runner's, sent through the same input router the mouse and the keyboard feed. The frames
/// are the window as it draws, so a run at 800×600 and one at 1920×1080 show the scale and the placement of each.
/// </summary>
/// <remarks>
/// <para><b>The frames</b> (<see cref="RetailUiFramesPlan.FrameNames"/>): the data check mid-progress (the real check, then the queue place and a half-done
/// download, which no session raises because Lifestone does not patch, so the runner raises them through the screen's own event
/// entry), character management with a character selected, with a character pending deletion selected, after a delete, each dialog the
/// login screens open (the typed-word delete confirmation, Please Wait, the exit confirmation, the Credits "not in M2" message, the offline
/// refusal of Enter, and a character error from the server), character creation's six pages (Create and Next clicked for real, on a character drawn
/// with the run's seed: the appearance page's face and clothes halves with the 3D character, the start towns, the summary with the name typed through the
/// keyboard path, the name-in-use message the stand-in answers a taken name with, and character management after a successful create), the appearance and
/// summary pages again for an Olthoi character, and the disconnected screen. Five frames are logged as synthetic (<c>synthetic=True</c>) because no
/// session raises what they show offline: the queue place and the half-done download of the data check, the Entering World dialog (the stand-in
/// refuses Enter and the screen closes the dialog at once, so the runner opens it through the dialog factory with the screen's own data), the
/// character error (the runner calls <c>RecvNoticeCharacterError(13)</c>) and the disconnected screen (it calls <c>RecvNoticeServerDied</c>).</para>
/// <para><b>Output.</b> <c>DIR/NN-name.png</c>; without a DIR the default (<see cref="RetailUiOptions.DefaultFramesDirectory"/>, gitignored)
/// gets a <c>&lt;W&gt;x&lt;H&gt;</c> sub-folder. Nothing is committed: the frames are drawn from the user's dats.</para>
/// <para><b>Verdict</b> (frozen, built by <see cref="RetailUiFramesPlan.Verdict"/>, read by <c>tools\smoke.ps1</c>): <c>retail ui frames done size=&lt;WxH&gt; scale=&lt;s&gt; captured=&lt;n&gt;
/// failures=&lt;f&gt; missing_images=&lt;k&gt;</c>; the exit code is 0 only when <see cref="RetailUiFramesPlan.FrameNames"/>'s frames were all taken and nothing failed.
/// A failure is: a window that is not the size requested (<c>--resolution</c>, else the project's own; the run then captures nothing), a screen or a
/// dialog that is not where the script expects it, a layer that did not settle, a frame that could not be saved, that is not the window's size
/// or that is blank, an image missing that <see cref="RetailUiFramesPlan.KnownMissingImages"/> does not name, or a run over <see cref="RunTimeoutSeconds"/>. The
/// images the painter drew as retail's default graphic are logged by id (<c>missing image 0x...</c>); the one image known not to resolve, DID 0 (it clears an
/// element's image and is never drawn), is named in a log line of every run, so <c>missing_images=0</c> is the expected count.</para>
/// </remarks>
public partial class RetailUiFramesRunner : Node
{
    /// <summary>The fixed step of the session's and the UI's clock, in seconds.</summary>
    public const double FixedStep = 1.0 / 60.0;

    /// <summary>The longest the script may take, in real seconds (the outer timeout of the smoke step is longer).</summary>
    public const double RunTimeoutSeconds = 180.0;

    private const double SettleTimeoutSeconds = 30.0;

    /// <summary>
    /// How far into its loop a 3D character's animation is held for a review frame: half a second, which is frame 15 of the 33-frame CharGen animation at
    /// 30 fps (<see cref="Cg3DViewModel.PinAnimation"/>), so the frames show the same pose whatever the script did before them. A character that holds
    /// the rest pose (the face half, zoomed in) has nothing to pin.
    /// </summary>
    public const double CharGenAnimationPinSeconds = 0.5;
    private const int DrawFrames = 3;
    private const int BlankGridX = 64;
    private const int BlankGridY = 36;
    private const int BlankMinimumColours = 8;
    private const string Category = "retailui";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private RetailUiLayer _layer = null!;
    private string _directory = string.Empty;
    private Vector2I _requested;
    private double _time;
    private int _captured;
    private int _failures;
    private bool _aborted;

    /// <summary>A runner over <paramref name="layer"/> (built with the fixed step and not processing by itself) that writes under <paramref name="directory"/>.</summary>
    public static RetailUiFramesRunner Create(RetailUiLayer layer, string directory) =>
        new() { Name = "RetailUiFramesRunner", _layer = layer, _directory = directory };

    /// <inheritdoc/>
    public override void _Ready()
    {
        _layer.SetProcess(false); // the runner steps the layer itself, one fixed step at a time
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await FramesAsync(2); // the window is up and the layer has seen its size
            var project = new Vector2I(
                ProjectSettings.GetSetting("display/window/size/viewport_width", 1152).AsInt32(),
                ProjectSettings.GetSetting("display/window/size/viewport_height", 648).AsInt32());
            if (RetailUiFramesPlan.RequestedWindowSize(System.Environment.GetCommandLineArgs(), project.X, project.Y) is not { } size)
            {
                Fail("--resolution is not WxH, so the size to check against is unknown");
                _aborted = true;
            }
            else
            {
                _requested = new Vector2I(size.Width, size.Height);
                await FitWindowAsync();
                _aborted = !VerifyWindow();
            }

            if (!_aborted)
            {
                _directory = RetailUiFramesPlan.FolderFor(_directory, _requested.X, _requested.Y);
                Log.Info(Category, "review frames start", ("dir", _directory), ("size", $"{_requested.X}x{_requested.Y}"), ("scale", _layer.Scale), ("frames", RetailUiFramesPlan.FrameNames.Count));
                await PlayAsync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Exception(Category, "the review frames run crashed", ex);
            _failures++;
        }

        Finish();
    }

    /// <summary>
    /// Makes the window the size asked for whatever the desktop is. A decorated window is kept inside the desktop's work area by the window manager (a 1920x1080
    /// request on a 1920x1080 display with a taskbar opens 1920x1061; 2560x1440 on a 2560x1440 one opens 2560x1421), so the frames' size would depend on the
    /// machine. A borderless window is not limited that way: when the window is not the size asked for, the decoration is dropped and the size is asked again. A
    /// window that already is the size asked for (every smaller request) is left alone, so those frames are unchanged.
    /// </summary>
    private async Task FitWindowAsync()
    {
        var before = DisplayServer.WindowGetSize();
        if (before == _requested)
        {
            return;
        }

        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        DisplayServer.WindowSetSize(_requested);
        await FramesAsync(3); // the window manager has resized it and the layer has seen the new size
        var now = DisplayServer.WindowGetSize();
        Log.Info(
            Category,
            "the decorated window did not fit the desktop's work area; made it borderless so the frames are the size requested",
            ("requested", $"{_requested.X}x{_requested.Y}"),
            ("was", $"{before.X}x{before.Y}"),
            ("now", $"{now.X}x{now.Y}"));
    }

    private bool VerifyWindow()
    {
        var window = DisplayServer.WindowGetSize();
        var visible = GetViewport().GetVisibleRect().Size;
        var visibleSize = new Vector2I((int)visible.X, (int)visible.Y);
        var ok = window == _requested && visibleSize == _requested && _layer.WindowSize == _requested;
        if (!ok)
        {
            Fail($"the window is not the size requested: requested {_requested.X}x{_requested.Y}, window {window.X}x{window.Y}, viewport {visibleSize.X}x{visibleSize.Y}, UI layer {_layer.WindowSize.X}x{_layer.WindowSize.Y}; nothing is captured");
        }

        return ok;
    }

    private void Finish()
    {
        // The one image the layouts name that no dat holds is DID 0, which clears an element's image and is never drawn; any image the painter
        // drew as retail's default graphic is a missing one, named here (and a failure unless it is a known one).
        Log.Info(Category, "the known unresolved UI image is DID 0x00000000: it clears an element's image and nothing is drawn for it (docs/retail-ui-data.md section 7)");
        var missing = _layer.Compositor.Painter.MissingImages.OrderBy(d => d).ToArray();
        foreach (var did in missing)
        {
            var known = RetailUiFramesPlan.KnownMissingImages.Contains(did);
            Log.Warn(Category, $"missing image 0x{did:X8}: retail's default graphic stands in (placeholder{(known ? "; a known one" : "; NOT a known one")})");
            if (!known)
            {
                Fail($"image 0x{did:X8} is missing and is not a known one");
            }
        }

        if (!_aborted && _captured != RetailUiFramesPlan.FrameNames.Count)
        {
            Fail($"{_captured} of {RetailUiFramesPlan.FrameNames.Count} frames were taken");
        }

        // Report-only (M3 1.3 s2a): the UI's sounds, handed to the sound player whatever the master volume (D8). A dropped one means this run had no audio service.
        var sounds = _layer.SoundStats;
        Log.Info(Category, "ui sounds", ("sounds_ui", sounds.Played), ("requested", sounds.Requested), ("dropped", sounds.Dropped), ("silenced", sounds.Silenced), ("muted", sounds.Muted));
        Log.Info(Category, RetailUiFramesPlan.Verdict(_requested.X, _requested.Y, _layer.Scale, _captured, _failures, missing.Length));
        Log.FlushToGodot();
        GetTree().Quit(_failures == 0 && !_aborted ? 0 : 1);
    }

    private void Fail(string problem)
    {
        _failures++;
        Log.Error(Category, $"review frames: {problem}");
    }

    /// <summary>Notes a failure when <paramref name="condition"/> does not hold and says whether it did.</summary>
    private bool Expect(bool condition, string what)
    {
        if (!condition)
        {
            Fail($"expected {what}");
        }

        return condition;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The script

    private async Task PlayAsync()
    {
        var flow = _layer.Flow!.Flow;

        // The data check: the real check under way, then the queue place and a half-done download, which Lifestone never has to raise (it
        // does not patch), so the runner raises them through the screen's own event entry (synthetic).
        Run(0.5);
        var check = flow.CurrentScreen as DataPatchScreen;
        if (!Expect(check is { ConnectLevel: 1f, PatchLevel: 0f } && check.StatusText.StartsWith("Looking for data", StringComparison.Ordinal), $"the data check under way (connect full, patch empty), got mode {UiModes.NameOf(flow.CurrentMode)} '{check?.StatusText}'"))
        {
            return;
        }

        await CaptureAsync("datapatch-checking", "the real check, two seconds long on the session's clock");
        check!.OnDataEvent(DataPatchEvent.Waiting, 3);
        Run(0.1);
        Expect(check.StatusText.Contains('3', StringComparison.Ordinal), $"the queue place in the status text, got '{check.StatusText}'");
        await CaptureAsync("datapatch-queue", "synthetic: the data check's queue event, #3 in line", synthetic: true);
        check.OnDataEvent(DataPatchEvent.Patching, 8u << 20);
        check.OnDataEvent(DataPatchEvent.Progress, 4u << 20);
        Run(0.6);
        Expect(Math.Abs(check.PatchLevel - 0.5f) < 0.001f, $"the patch meter at one half, got {check.PatchLevel}");
        await CaptureAsync("datapatch-patching-half", "synthetic: a download half done (4 of 8 MiB)", synthetic: true);

        // Character management, once the check ends and the list is in.
        if (!RunUntil(() => flow.CurrentMode == UiModes.CharacterManagement && flow.CurrentScreen is CharacterManagementScreen { List.Items.Count: > 0 }, 6.0))
        {
            Fail($"the data check did not end in character management (mode {UiModes.NameOf(flow.CurrentMode)})");
            return;
        }

        Run(0.3);
        var screen = (CharacterManagementScreen)flow.CurrentScreen!;
        var alpha = StandInGuid(0);
        if (!Expect(screen.SelectedGuid == alpha && screen.DeleteButton is { Visible: true } && screen.RestoreButton is { Visible: false } && screen.EnterButton is { State: var enterState } && enterState == UiStateIds.Normal, "the first character selected, Delete shown, Restore hidden, Enter enabled"))
        {
            return;
        }

        await CaptureAsync("charmanage-selected", "three characters, the first selected, the third pending deletion (red, last)");

        Click(screen.List!.Items.First(i => TextOf(i) == "Stand-in Gamma"));
        Run(0.2);
        Expect(screen.RestoreButton!.Visible && !screen.DeleteButton!.Visible && screen.EnterButton!.State == UiStateIds.Ghosted, "Restore in Delete's place and Enter ghosted for the character pending deletion");
        await CaptureAsync("charmanage-pending-selected", "the character pending deletion selected: Restore, Enter ghosted");

        // Delete the second character: the typed-word confirmation, Please Wait, the list again.
        Click(screen.List.Items.First(i => TextOf(i) == "Stand-in Beta"));
        Run(0.2);
        Click(screen.DeleteButton!);
        Run(0.2);
        var confirm = _layer.Manager.Dialogs.Find(screen.DeleteDialog);
        if (!Expect(confirm is not null, "the delete confirmation dialog"))
        {
            return;
        }

        await CaptureAsync("dialog-delete-confirm", "the typed-word confirmation naming the character");
        if (confirm!.GetChildRecursive(UiConfirmationTextInputDialog.Field) is UiTextElement field && confirm.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton) is { } done)
        {
            field.TakeFocus();
            field.SelectAll();
            foreach (var c in "delete")
            {
                _layer.Manager.Input.Character(c); // the keyboard path, so the text mode is exercised as well
            }

            Expect(field.Text == "delete", $"the confirmation field holding 'delete', got '{field.Text}'");
            Click(done);
        }
        else
        {
            Fail("the delete confirmation has no text field or Done button");
            return;
        }

        Run(0.2);
        Expect(screen.PleaseWaitDialog != 0, "the Please Wait dialog after the word was typed");
        await CaptureAsync("dialog-please-wait", "Please Wait while the stand-in answers the delete");
        Run(1.0);
        var beta = _layer.Session!.State.Characters.Entries.FirstOrDefault(e => e.Name == "Stand-in Beta");
        Expect(beta is { PendingDeletion: true } && screen.PleaseWaitDialog == 0, "the deleted character listed pending deletion and Please Wait closed");
        await CaptureAsync("charmanage-after-delete", "after the delete: two characters pending deletion (red, last)");

        // The dialogs of the buttons.
        Click(screen.Root!.GetChildRecursive(CharacterManagementScreen.ExitButtonId)!);
        Run(0.2);
        if (Expect(screen.ConfirmExitDialog != 0, "the exit confirmation dialog"))
        {
            await CaptureAsync("dialog-exit-confirm", "Exit asks to confirm");
            ClickDialogButton(screen.ConfirmExitDialog, UiConfirmationDialog.NoButton);
            Run(0.2);
            Expect(screen.ConfirmExitDialog == 0 && flow.CurrentMode == UiModes.CharacterManagement, "No keeps character management");
        }

        Click(screen.Root.GetChildRecursive(CharacterManagementScreen.CreditsButtonId)!);
        Run(0.2);
        var credits = _layer.Manager.Dialogs.OpenDialogs.FirstOrDefault();
        if (Expect(credits is not null, "the Credits message dialog"))
        {
            await CaptureAsync("dialog-credits-not-in-m2", "placeholder: Credits answers with a 'not in Lifestone M2' message (D38)");
            ClickDialogButton(credits!.Context, UiMessageDialog.OkButton);
            Run(0.2);
        }

        // Entering World: only seen through the factory offline (see the class remarks).
        var wait = _layer.Manager.Dialogs.MakeDialog(
            UiDialogData.Of(UiDialogType.Wait, new UiStringRef(StringHash.Tokenize("ID_Character_EnteringWorld"), GmUiFlow.PregameTableId)).WithFlag(UiPropertyIds.DialogModal, true));
        Run(0.2);
        await CaptureAsync("dialog-entering-world", "synthetic: the Entering World dialog, opened with the screen's own data (offline the screen closes it at once)", synthetic: true);
        _layer.Manager.Dialogs.CloseDialog(wait);
        Run(0.2);

        // Enter, offline: the stand-in refuses, and the screen says so.
        Click(screen.List.Items.First(i => TextOf(i) == "Stand-in Alpha"));
        Run(0.2);
        Click(screen.EnterButton!);
        Run(0.2);
        if (Expect(screen.ErrorDialog != 0 && screen.EnteringWorldDialog == 0, "the offline refusal of Enter said in a message dialog, Entering World closed"))
        {
            await CaptureAsync("dialog-enter-refused-offline", "placeholder: offline there is no world to enter, and the dialog says so");
            ClickDialogButton(screen.ErrorDialog, UiMessageDialog.OkButton);
            Run(0.2);
        }

        // A character error from the server that the player can retry: character management again, with the message.
        if (Expect(_layer.Flow!.RecvNoticeCharacterError(13), "character error 13 (the character is in the world) routed"))
        {
            Run(0.4);
            screen = (flow.CurrentScreen as CharacterManagementScreen)!;
            if (Expect(screen is not null && screen.ErrorDialog != 0, "character management again, with the error message"))
            {
                await CaptureAsync("dialog-character-error", "synthetic: a retryable character error (the runner calls RecvNoticeCharacterError(13); no session event raises it offline): character management with the message", synthetic: true);
                ClickDialogButton(screen!.ErrorDialog, UiMessageDialog.OkButton);
                Run(0.2);
            }
        }

        // Character creation: Create on character management opens it on the heritage page (a random character, drawn with the scripted run's seed),
        // and Next walks through the pages. Every click is a real one through the input router, the name is typed through the keyboard path, and the
        // answers come from the stand-in responder: nothing here is synthetic.
        if (flow.CurrentScreen is CharacterManagementScreen management && Expect(management.CreateButton is { State: var createState } && createState == UiStateIds.Normal, "character management with Create enabled"))
        {
            Click(management.CreateButton!);
            Run(0.6);
            if (Expect(flow.CurrentScreen is CharGenMainScreen { Progress: CharGenPageId.Heritage }, $"character creation on its heritage page, got mode {UiModes.NameOf(flow.CurrentMode)}"))
            {
                var chargen = (CharGenMainScreen)flow.CurrentScreen!;
                await CaptureAsync("chargen-heritage", "Create opens character creation on a random character (seeded): the heritage page");
                Click(chargen.RightButton!);
                Run(0.4);
                if (Expect(chargen.Progress == CharGenPageId.Profession, "Next opens the profession page"))
                {
                    await CaptureAsync("chargen-profession", "Next: the profession page for that character (attribute credits, profession, locks)");
                    Click(chargen.RightButton!);
                    Run(0.4);
                    if (Expect(chargen.Progress == CharGenPageId.Skills, "Next opens the skills page"))
                    {
                        await CaptureAsync("chargen-skills", "Next: the skills page for that character (skill credits, the four groups, the skill box)");
                        await PlayCharacterAsync(chargen, management);
                    }
                }

                if (flow.CurrentScreen is CharGenMainScreen second)
                {
                    await PlayOlthoiAsync(second);
                }
            }
        }

        // A lost connection: the disconnected screen with its reason.
        _layer.Flow!.RecvNoticeServerDied();
        Run(0.3);
        var disconnected = flow.CurrentScreen as DisconnectedScreen;
        if (Expect(disconnected is not null && disconnected.ReasonText.StartsWith("The connection to the server has been lost", StringComparison.Ordinal), $"the disconnected screen with the lost-connection reason, got mode {UiModes.NameOf(flow.CurrentMode)}"))
        {
            await CaptureAsync("disconnected", "synthetic: the lost connection (the runner calls RecvNoticeServerDied; no session failure raises it offline; its button reconnects, D43)", synthetic: true);
        }
    }

    /// <summary>
    /// The appearance page's two halves, the start towns and the summary for the seeded character; the name of a character the list already holds is typed
    /// and refused, then a new name is typed and the create succeeds: character management again with the new character selected.
    /// </summary>
    private async Task PlayCharacterAsync(CharGenMainScreen chargen, CharacterManagementScreen management)
    {
        var flow = _layer.Flow!.Flow;
        Click(chargen.RightButton!);
        Run(1.4); // the camera zooms in over 0.6 s and the character is drawn
        if (!Expect(chargen.Progress == CharGenPageId.Appearance && chargen.Appearance is { CurrentType: CharGenAppearanceType.Face, IsZoomedIn: true, IsZooming: false }, "Next opens the appearance page on its face half, zoomed in"))
        {
            return;
        }

        await CaptureAsync("chargen-appearance-face", "Next: the appearance page, face half: the camera on the head, hair, eyes, nose, mouth and skin, the colour wheel", CharGenMainScreen.AppearanceViewportId);
        Click(chargen.Appearance!.ClothesButton!);
        Run(1.4);
        Expect(chargen.Appearance.CurrentType == CharGenAppearanceType.Clothes && !chargen.Appearance.IsZoomedIn && !chargen.Appearance.IsZooming, "the Clothes tab shows the clothes half, zoomed out");
        await CaptureAsync("chargen-appearance-clothes", "the Clothes tab: the whole figure, headgear, shirt, trousers and footwear", CharGenMainScreen.AppearanceViewportId);

        Click(chargen.RightButton!);
        Run(0.4);
        if (Expect(chargen.Progress == CharGenPageId.Town, "Next opens the town page"))
        {
            await CaptureAsync("chargen-town", "Next: the start town page (the character's own town highlighted)");
        }

        Click(chargen.RightButton!);
        Run(0.6);
        if (!Expect(chargen.Progress == CharGenPageId.Summary && chargen.Summary is { NameField.HasFocus: true }, "Next opens the summary page with the name field focused"))
        {
            return;
        }

        await CaptureAsync("chargen-summary", "Next: the summary page: the list, the name prompt, the sample names and the character", CharGenMainScreen.SummaryViewportId);

        // A taken name: typed through the keyboard path, Finish clicked; the stand-in answers name in use and the screen says so.
        TypeName(chargen, "Stand-in Alpha");
        Click(chargen.FinishButton!);
        Run(0.2);
        if (chargen.CreditDialog != 0)
        {
            ClickDialogButton(chargen.CreditDialog, UiConfirmationDialog.YesButton);
            Run(0.2);
        }

        if (!Expect(RunUntil(() => chargen.ErrorDialog != 0, 3.0) && !chargen.IsCreatePending, "the name-in-use message after Finish with a taken name"))
        {
            return;
        }

        await CaptureAsync("chargen-dialog-name-in-use", "the stand-in answers a name the list holds with name in use; the message is retail's");
        ClickDialogButton(chargen.ErrorDialog, UiMessageDialog.OkButton);
        Run(0.2);

        // A free name: the create succeeds and character management opens with the new character selected.
        TypeName(chargen, "Runner Frame");
        Click(chargen.FinishButton!);
        Run(0.2);
        if (chargen.CreditDialog != 0)
        {
            ClickDialogButton(chargen.CreditDialog, UiConfirmationDialog.YesButton);
            Run(0.2);
        }

        if (!Expect(RunUntil(() => flow.CurrentScreen is CharacterManagementScreen { List.Items.Count: > 0 }, 4.0), "character management again after the create"))
        {
            return;
        }

        Run(0.4);
        var after = (CharacterManagementScreen)flow.CurrentScreen!;
        var created = _layer.Session!.State.Characters.Entries.Where(e => e.Name == "Runner Frame").Select(e => (uint?)e.Guid).FirstOrDefault();
        Expect(created is not null && after.SelectedGuid == created, "the new character in the list and selected");
        await CaptureAsync("charmanage-after-create", "after the create: the new character is logged in at once (CPlayerSystem::LogOnCharacter); the offline stand-in has no server, so character management shows the new character in the list, selected, under the placeholder message that says so");
        Expect(after.ErrorDialog != 0, "the offline stand-in's Enter for the new character is said on character management");
        if (after.ErrorDialog != 0)
        {
            ClickDialogButton(after.ErrorDialog, UiMessageDialog.OkButton);
            Run(0.2);
        }

        Click(after.CreateButton!);
        Run(0.6);
    }

    /// <summary>A second heritage: the Olthoi, whose appearance page has no clothes, nose or mouth and whose figure is far bigger.</summary>
    private async Task PlayOlthoiAsync(CharGenMainScreen chargen)
    {
        if (!Expect(chargen.Progress == CharGenPageId.Heritage && chargen.Heritage is not null, "character creation open again on its heritage page"))
        {
            return;
        }

        var olthoi = CharGenRules.Heritages.First(h => h.Heritage == 12);
        Click(chargen.Root!.GetChildRecursive(olthoi.ButtonId)!);
        Run(0.2);
        Click(chargen.TabOf(CharGenPageId.Appearance)!);
        Run(1.4);
        if (Expect(chargen.State.Heritage == 12 && chargen.Progress == CharGenPageId.Appearance, "the Olthoi heritage chosen and its appearance page open"))
        {
            await CaptureAsync("chargen-appearance-olthoi", "an Olthoi character on the appearance page: no clothes, nose or mouth, the Olthoi's own texts", CharGenMainScreen.AppearanceViewportId);
        }

        Click(chargen.TabOf(CharGenPageId.Summary)!);
        Run(0.6);
        if (Expect(chargen.Progress == CharGenPageId.Summary, "the Olthoi's summary page open"))
        {
            await CaptureAsync("chargen-summary-olthoi", "an Olthoi character on the summary page", CharGenMainScreen.SummaryViewportId);
        }

        // Back out through the exit warning to character management, so the lost-connection frame below follows what the player could do.
        chargen.ElementManager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionStart, (int)CharGenMainScreen.ExitAction);
        Run(0.2);
        ClickDialogButton(chargen.ExitDialog, UiConfirmationDialog.YesButton);
        Run(0.4);
        Expect(_layer.Flow!.Flow.CurrentMode == UiModes.CharacterManagement, "the exit warning's Yes returns to character management");
    }

    /// <summary>Types <paramref name="name"/> into the summary page's name field through the keyboard path (the field has the keyboard), replacing what it holds.</summary>
    private void TypeName(CharGenMainScreen chargen, string name)
    {
        var field = chargen.Summary!.NameField!;
        field.TakeFocus();
        field.SelectAll();
        foreach (var c in name)
        {
            _layer.Manager.Input.Character(c);
        }

        Run(0.1);
        Expect(field.Text == name, $"the name field holding '{name}', got '{field.Text}'");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Stepping, input and capture

    private static uint StandInGuid(int index) => Lifestone.Client.Session.StandIn.StandInSessions.FirstCharacterGuid + (uint)index;

    private static string TextOf(UiElement item) => item is UiTextElement text ? text.Text : string.Empty;

    private void Run(double seconds)
    {
        var frames = Math.Max(1, (int)Math.Ceiling(seconds / FixedStep));
        for (var i = 0; i < frames; i++)
        {
            _layer.Step(_time);
            _time += FixedStep;
        }
    }

    private bool RunUntil(Func<bool> done, double seconds)
    {
        var frames = (int)Math.Ceiling(seconds / FixedStep);
        for (var i = 0; i < frames; i++)
        {
            if (done())
            {
                return true;
            }

            _layer.Step(_time);
            _time += FixedStep;
        }

        return done();
    }

    /// <summary>Clicks the middle of <paramref name="element"/> with the mouse, through the layer's input router.</summary>
    private void Click(UiElement element)
    {
        var box = element.ScreenBox;
        var input = _layer.Manager.Input;
        input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        input.MouseButton(UiMouseButton.Left, down: true);
        input.MouseButton(UiMouseButton.Left, down: false);
    }

    private void ClickDialogButton(uint context, uint buttonId)
    {
        if (_layer.Manager.Dialogs.Find(context)?.GetChildRecursive(buttonId) is { } button)
        {
            Click(button);
        }
        else
        {
            Fail($"dialog {context} has no button 0x{buttonId:X}");
        }
    }

    private async Task FramesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private async Task CaptureAsync(string name, string what, uint viewportElementId, bool synthetic = false)
    {
        await CaptureAsync(name, what, synthetic, viewportElementId);
    }

    private async Task CaptureAsync(string name, string what, bool synthetic = false, uint viewportElementId = 0)
    {
        if (_aborted)
        {
            return;
        }

        var index = _captured + 1;
        if (index > RetailUiFramesPlan.FrameNames.Count || !string.Equals(RetailUiFramesPlan.FrameNames[_captured], name, StringComparison.Ordinal))
        {
            Fail($"frame {index} is '{name}', which is not where the plan's list has it");
            _aborted = true;
            return;
        }

        var file = RetailUiFramesPlan.FileName(index);

        if (_clock.Elapsed.TotalSeconds > RunTimeoutSeconds)
        {
            Fail($"the run took more than {RunTimeoutSeconds:0} s; frame {file} not taken");
            _aborted = true;
            return;
        }

        // The pointer rests off the screens, so no button shows a hover the picture is not about.
        _layer.Manager.Input.MouseMove(1, 1);
        Run(0.1);
        var settled = _layer.Flush(SettleTimeoutSeconds);
        await FramesAsync(DrawFrames);
        if (!settled)
        {
            Fail($"{file}: the UI did not settle within {SettleTimeoutSeconds:0} s (images still decoding or surfaces still painting)");
        }

        var pose = viewportElementId != 0 ? PinCharGenAnimation(viewportElementId) : "none";
        await FramesAsync(1); // the binder poses the character in its next frame
        var path = $"{_directory}/{file}";
        if (!ScreenshotWriter.Save(GetViewport(), path, Category, ("frame", name), ("scale", _layer.Scale), ("synthetic", synthetic)))
        {
            Fail($"{file} could not be saved");
            return;
        }

        using var image = GetViewport().GetTexture().GetImage();
        var size = new Vector2I(image.GetWidth(), image.GetHeight());
        if (size != _requested)
        {
            Fail($"{file} is {size.X}x{size.Y}, not the {_requested.X}x{_requested.Y} requested");
            return;
        }

        if (DistinctColours(image) < BlankMinimumColours)
        {
            Fail($"{file} is blank");
            return;
        }

        if (viewportElementId != 0)
        {
            CheckHole(image, file, viewportElementId);
        }

        _captured++;
        Log.Info(
            Category,
            "review frame",
            ("n", _captured),
            ("file", file),
            ("what", what),
            ("mode", UiModes.NameOf(_layer.Flow!.Flow.CurrentMode)),
            ("t", _time.ToString("0.00", CultureInfo.InvariantCulture)),
            ("pose", pose),
            ("synthetic", synthetic));
    }

    /// <summary>
    /// Holds the 3D character of the viewport at <see cref="CharGenAnimationPinSeconds"/> into its animation, or says it holds the rest pose; returns what
    /// the frame shows, for the log.
    /// </summary>
    private string PinCharGenAnimation(uint viewportElementId)
    {
        var chargen = _layer.Flow?.Flow.CurrentScreen as CharGenMainScreen;
        var view = viewportElementId == CharGenMainScreen.AppearanceViewportId ? chargen?.Appearance?.View
            : viewportElementId == CharGenMainScreen.SummaryViewportId ? chargen?.Summary?.View
            : null;
        if (view is null)
        {
            return "no view";
        }

        if (view.PinAnimation(CharGenAnimationPinSeconds))
        {
            return $"animation held at {CharGenAnimationPinSeconds:0.0} s (frame {view.Preview!.Sequence.FrameNumber:0.#})";
        }

        return view.IsAnimating ? "animation not pinned" : "rest pose";
    }

    /// <summary>
    /// A 3D character's hole has something drawn in it: a source is registered and its own picture (the creature-mode view's texture, read back whole,
    /// so the UI's frame around the hole cannot fool the count) holds more than a flat colour or two.
    /// </summary>
    private void CheckHole(Image window, string file, uint viewportElementId)
    {
        if (!_layer.Viewports.HasSource(viewportElementId) || !_layer.Viewports.TryGetHole(viewportElementId, out var rect, out var visible) || !visible)
        {
            Fail($"{file}: viewport 0x{viewportElementId:X8} has no source or no visible hole");
            return;
        }

        if (_layer.Viewports.PictureOf(viewportElementId)?.Texture is not { } texture)
        {
            Fail($"{file}: viewport 0x{viewportElementId:X8} shows no picture");
            return;
        }

        using var picture = texture.GetImage();
        var seen = new HashSet<uint>();
        const int grid = 32;
        for (var gy = 0; gy < grid; gy++)
        {
            for (var gx = 0; gx < grid; gx++)
            {
                var x = ((gx * 2) + 1) * picture.GetWidth() / (grid * 2);
                var y = ((gy * 2) + 1) * picture.GetHeight() / (grid * 2);
                seen.Add(picture.GetPixel(x, y).ToRgba32() >> 8);
            }
        }

        const int minimum = 8;
        if (seen.Count < minimum)
        {
            Fail($"{file}: the 3D character's picture (0x{viewportElementId:X8}, {picture.GetWidth()}x{picture.GetHeight()}) holds {seen.Count} colours over a {grid}x{grid} grid, fewer than the {minimum} a drawn character gives");
            return;
        }

        // The window shows that picture in the hole (not the page's panel over it): where the hole and the picture are the same size, the window's pixels
        // are the picture's.
        var matched = 0;
        var compared = 0;
        if (picture.GetSize() == rect.Size)
        {
            for (var gy = 0; gy < 8; gy++)
            {
                for (var gx = 0; gx < 8; gx++)
                {
                    var x = ((gx * 2) + 1) * picture.GetWidth() / 16;
                    var y = ((gy * 2) + 1) * picture.GetHeight() / 16;
                    var a = picture.GetPixel(x, y);
                    var b = window.GetPixel(rect.Position.X + x, rect.Position.Y + y);
                    compared++;
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 0.03f)
                    {
                        matched++;
                    }
                }
            }

            if (matched < compared * 3 / 4)
            {
                Fail($"{file}: the window shows the 3D character's picture at only {matched} of {compared} sampled pixels of the hole (something is drawn over it)");
                return;
            }
        }

        Log.Info(Category, "3D character drawn", ("file", file), ("hole", $"{rect.Position.X},{rect.Position.Y} {rect.Size.X}x{rect.Size.Y}"), ("picture", $"{picture.GetWidth()}x{picture.GetHeight()}"), ("colours", seen.Count), ("window_matches", $"{matched}/{compared}"));
    }

    private static int DistinctColours(Image image)
    {
        var seen = new HashSet<uint>();
        for (var gy = 0; gy < BlankGridY; gy++)
        {
            for (var gx = 0; gx < BlankGridX; gx++)
            {
                var x = (gx * image.GetWidth() + image.GetWidth() / 2) / BlankGridX;
                var y = (gy * image.GetHeight() + image.GetHeight() / 2) / BlankGridY;
                seen.Add(image.GetPixel(x, y).ToRgba32() >> 8);
            }
        }

        return seen.Count;
    }
}
