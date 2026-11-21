using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The Godot driver of <see cref="IThrowawayScreens"/> (<c>--login-check=throwaway</c>): it clicks the game's real retail screens through the shell's input router, as a person
/// would (the pointer moves, two frames pass, the button goes down and up), the way <c>RetailUiFramesRunner</c> clicks character creation, <c>RetailUiChecks</c> the character list
/// and <c>PlayChecks</c> the tools panel. Nothing here talks to the server except through those screens.
/// </summary>
internal sealed class ThrowawayScreens(RetailUiLayer ui, IGameSessionView session) : IThrowawayScreens
{
    /// <summary>The Aluvian heritage's number (the throwaway is the same character <c>HeadlessCharacter</c> makes: an Aluvian).</summary>
    private const uint AluvianHeritage = 1;

    private const double WaitSeconds = 20.0;

    private const double MaxVideoPaceSeconds = 10.0;

    /// <summary><c>--video-pace=S</c>: the seconds each character-creation page and typed name, and each step of the character delete (the empty confirmation box, the typed word, the list with the character marked
    /// for deletion), are held for a screen capture (0: no hold); the climber start's pages read the same value (<c>ClimberScreens</c>).</summary>
    internal static readonly double VideoPaceSeconds = ReadVideoPace();

    private static double ReadVideoPace()
    {
        var arguments = CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Where(a => a.Name == "--video-pace").ToList();
        if (arguments.Count == 0)
        {
            return 0;
        }

        var argument = arguments[^1];

        if (double.TryParse(argument.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and <= MaxVideoPaceSeconds)
        {
            Log.Info("logincheck", "--video-pace: character creation's pages and names, and the character delete's steps, are held for a screen capture", ("seconds", seconds));
            return seconds;
        }

        Log.Error("logincheck", "--video-pace needs seconds from 0 to 10; nothing is held", ("value", argument.Value));
        return 0;
    }

    private UiElementManager Manager => ui.Manager;

    private IUiScreen? Current => ui.Screens?.Flow.CurrentScreen;

    /// <inheritdoc/>
    public ScreenOp OpenCharGenSummary() => new ScreenFrameOp(OpenCharGenSummarySteps);

    /// <inheritdoc/>
    public ScreenOp Finish(string name) => new ScreenFrameOp(op => FinishSteps(op, name));

    /// <inheritdoc/>
    public ScreenOp DismissErrorDialog() => new ScreenFrameOp(DismissSteps);

    /// <inheritdoc/>
    public ScreenOp OpenToolsPanel() => new ScreenFrameOp(OpenToolsSteps);

    /// <inheritdoc/>
    public ScreenOp CloseToolsPanel() => new ScreenFrameOp(CloseToolsSteps);

    /// <inheritdoc/>
    public ScreenOp PressWhere() => new ScreenFrameOp(WhereSteps);

    /// <inheritdoc/>
    public ScreenOp PressAddXp(string expectedCharacter, long amount) => new ScreenFrameOp(op => AddXpSteps(op, expectedCharacter, amount));

    /// <inheritdoc/>
    public ScreenOp PressPlace(string query) => new ScreenFrameOp(op => PlaceSteps(op, query));

    /// <inheritdoc/>
    public ScreenOp SelectCharacter(string name) => new ScreenFrameOp(op => SelectSteps(op, name));

    /// <inheritdoc/>
    public ScreenOp DeleteSelected() => new ScreenFrameOp(DeleteSteps);

    /// <inheritdoc/>
    public ScreenOp RestoreSelected() => new ScreenFrameOp(RestoreSteps);

    // ------------------------------------------------------------------------------------------------ input

    private IEnumerable<object?> Click(UiElement element)
    {
        var box = element.ScreenBox;
        var x = (box.X0 + box.X1) / 2;
        var y = (box.Y0 + box.Y1) / 2;
        Manager.Input.MouseMove(x, y);
        yield return 2;
        Manager.Input.MouseMove(x, y);
        Manager.Input.MouseButton(UiMouseButton.Left, true);
        Manager.Input.MouseButton(UiMouseButton.Left, false);
        yield return 2;
    }

    private void TapKey(int dik)
    {
        Manager.Input.KeyDown(dik);
        Manager.Input.KeyUp(dik);
    }

    private void Type(UiTextElement field, string text)
    {
        field.TakeFocus();
        field.SelectAll();
        foreach (var c in text)
        {
            Manager.Input.Character(c);
        }
    }

    private static ScreenFrameOp.Until Wait(Func<bool> condition, string what, double seconds = WaitSeconds) => new(condition, seconds, what);

    /// <summary>Holds the screen for <c>--video-pace</c>'s seconds (nothing without it), with the pointer out of the window so no tooltip covers the page.</summary>
    private IEnumerable<object?> VideoHold()
    {
        if (VideoPaceSeconds <= 0)
        {
            yield break;
        }

        Manager.Input.MouseLeave();
        var start = Stopwatch.GetTimestamp();
        yield return Wait(() => Stopwatch.GetElapsedTime(start).TotalSeconds >= VideoPaceSeconds, "the --video-pace hold", VideoPaceSeconds + WaitSeconds);
    }

    private string DialogText(uint context) =>
        Manager.Dialogs.Find(context)?.GetChildRecursive(UiDialog.TextElement) is UiTextElement text ? text.Text : string.Empty;

    private IEnumerable<object?> PressDialogButton(uint context, uint buttonId, ScreenFrameOp op)
    {
        if (Manager.Dialogs.Find(context)?.GetChildRecursive(buttonId) is not { } button)
        {
            op.Abort($"dialog {context} has no button 0x{buttonId:X}");
            yield break;
        }

        foreach (var step in Click(button))
        {
            yield return step;
        }
    }

    // ------------------------------------------------------------------------------------------------ character creation

    private IEnumerable<object?> OpenCharGenSummarySteps(ScreenFrameOp op)
    {
        yield return Wait(() => Current is CharacterManagementScreen { CreateButton: { State: var s } } && s == UiStateIds.Normal, "the character list with Create enabled");
        foreach (var step in Click(((CharacterManagementScreen)Current!).CreateButton!))
        {
            yield return step;
        }

        yield return Wait(() => Current is CharGenMainScreen { Progress: CharGenPageId.Heritage, Heritage: not null }, "character creation on its heritage page");
        var chargen = (CharGenMainScreen)Current!;
        var aluvian = CharGenRules.HeritageEntry(AluvianHeritage);
        if (aluvian is null || chargen.Root?.GetChildRecursive(aluvian.ButtonId) is not { } heritageButton)
        {
            op.Abort("the heritage page has no Aluvian button");
            yield break;
        }

        foreach (var step in VideoHold())
        {
            yield return step;
        }

        foreach (var step in Click(heritageButton))
        {
            yield return step;
        }

        foreach (var (page, what) in new (CharGenPageId, string)[]
        {
            (CharGenPageId.Profession, "the profession page"),
            (CharGenPageId.Skills, "the skills page"),
            (CharGenPageId.Appearance, "the appearance page"),
            (CharGenPageId.Town, "the start town page"),
            (CharGenPageId.Summary, "the summary page"),
        })
        {
            if (page is CharGenPageId.Town)
            {
                // The appearance page zooms its camera in over a moment; Next works once the zoom is over.
                yield return Wait(() => chargen.Appearance is { IsZooming: false }, "the appearance page's zoom to end");
            }

            foreach (var step in VideoHold())
            {
                yield return step;
            }

            foreach (var step in Click(chargen.RightButton!))
            {
                yield return step;
            }

            yield return Wait(() => chargen.Progress == page, "Next to open " + what);
        }

        yield return Wait(() => chargen.Summary is { NameField.HasFocus: true }, "the summary page's name field to take the keyboard");
        op.Succeed();
    }

    private IEnumerable<object?> FinishSteps(ScreenFrameOp op, string name)
    {
        if (Current is not CharGenMainScreen { Progress: CharGenPageId.Summary, Summary.NameField: { } field } chargen)
        {
            op.Abort("character creation is not on its summary page");
            yield break;
        }

        Type(field, name);
        yield return 3;
        if (field.Text != name)
        {
            op.Abort($"the name field holds '{field.Text}', not '{name}'");
            yield break;
        }

        foreach (var step in VideoHold())
        {
            yield return step;
        }

        foreach (var step in Click(chargen.FinishButton!))
        {
            yield return step;
        }

        // The answer is an error dialog (a refused name), or the screen going away (the server accepted the character). A credit warning asks first when attribute credits are left: Yes.
        var creditAnswered = false;
        for (var waited = 0; waited < 60 * 20; waited++)
        {
            if (chargen.CreditDialog != 0 && !creditAnswered)
            {
                creditAnswered = true;
                foreach (var step in PressDialogButton(chargen.CreditDialog, UiConfirmationDialog.YesButton, op))
                {
                    yield return step;
                }

                continue;
            }

            if (chargen.ErrorDialog != 0)
            {
                op.Succeed(DialogText(chargen.ErrorDialog));
                yield break;
            }

            if (!ReferenceEquals(Current, chargen))
            {
                op.Succeed();
                yield break;
            }

            yield return 1;
        }

        op.Abort("neither an error dialog nor the end of character creation followed Finish within 20 s");
    }

    private IEnumerable<object?> DismissSteps(ScreenFrameOp op)
    {
        if (Current is not CharGenMainScreen { ErrorDialog: not 0 } chargen)
        {
            op.Abort("character creation shows no error dialog");
            yield break;
        }

        foreach (var step in PressDialogButton(chargen.ErrorDialog, UiMessageDialog.OkButton, op))
        {
            yield return step;
        }

        yield return Wait(() => chargen.ErrorDialog == 0, "the dialog to close"); // the next Finish takes the name field's keyboard itself
        yield return 2;
        op.Succeed();
    }

    // ------------------------------------------------------------------------------------------------ the tools panel

    private bool InShell(out GamePlayScreen shell, out ToolsPanel panel)
    {
        if (Current is GamePlayScreen { ToolsUi: { } tools, Hotkeys: not null } game)
        {
            shell = game;
            panel = tools;
            return true;
        }

        shell = null!;
        panel = null!;
        return false;
    }

    private IEnumerable<object?> OpenToolsSteps(ScreenFrameOp op)
    {
        yield return Wait(() => InShell(out _, out _), "the game screen with its tools panel and hotkeys");
        InShell(out _, out var panel);
        for (var attempt = 0; attempt < 3 && !panel.IsOpen; attempt++)
        {
            TapKey(LifestoneHotkeys.ToolsKey);
            yield return 4;
        }

        if (!panel.IsOpen)
        {
            op.Abort("F7 did not open the tools panel");
            yield break;
        }

        op.Succeed();
    }

    private IEnumerable<object?> CloseToolsSteps(ScreenFrameOp op)
    {
        if (!InShell(out _, out var panel))
        {
            op.Abort("the game screen is not showing");
            yield break;
        }

        if (panel.IsOpen)
        {
            TapKey(LifestoneHotkeys.ToolsKey);
            yield return 3;
        }

        if (panel.IsOpen)
        {
            op.Abort("F7 did not close the tools panel");
            yield break;
        }

        op.Succeed();
    }

    private IEnumerable<object?> WhereSteps(ScreenFrameOp op)
    {
        if (!InShell(out var shell, out var panel) || panel.Window?.GetChildRecursive(ToolsPanel.WhereButtonId) is not { } button || !panel.IsOpen)
        {
            op.Abort("the tools panel is not open");
            yield break;
        }

        var before = panel.Model.LastWhere;
        foreach (var step in Click(button))
        {
            yield return step;
        }

        yield return Wait(() => panel.Model.LastWhere is { IsCompleted: true } where && !ReferenceEquals(where, before), "the server's answer to Where am I", 12);
        var answer = panel.Model.LastWhere!;
        yield return 3;
        if (answer.Outcome?.Kind != ActionOutcomeKind.Confirmed)
        {
            op.Abort($"Where am I ended {answer.Outcome?.Kind}: {answer.Outcome?.Detail}");
            yield break;
        }

        op.Succeed(panel.Model.WhereText, panel.Model.LastAgreement?.Metres ?? double.NaN);
        _ = shell;
    }

    private IEnumerable<object?> AddXpSteps(ScreenFrameOp op, string expectedCharacter, long amount)
    {
        // The guard (owner decision D61): experience is added to the throwaway only. Checked here, before anything is pressed, and again by the scenario.
        var inWorld = session.State.Self.Name;
        if (!string.Equals(inWorld, expectedCharacter, StringComparison.OrdinalIgnoreCase))
        {
            op.Abort($"Add XP refused: the character in the world is '{inWorld}', not '{expectedCharacter}'");
            yield break;
        }

        if (!InShell(out var shell, out var panel) || panel.Window?.GetChildRecursive(ToolsPanel.AddXpButtonId) is not { } button || panel.AmountField is not { } field || !panel.IsOpen)
        {
            op.Abort("the tools panel is not open");
            yield break;
        }

        field.SetText(amount.ToString(CultureInfo.InvariantCulture));
        var before = panel.Model.LastXp;
        foreach (var step in Click(button))
        {
            yield return step;
        }

        yield return Wait(() => panel.Model.LastXp is { IsCompleted: true } xp && !ReferenceEquals(xp, before), "the server's answer to Add XP", 12);
        var answer = panel.Model.LastXp!;
        yield return 3;
        var reply = $"added {amount.ToString("N0", CultureInfo.InvariantCulture)} experience";
        var chat = shell.Chat?.Main?.LogText ?? string.Empty;
        if (answer.Outcome?.Kind != ActionOutcomeKind.Confirmed || answer.Result is not { } result || !chat.Contains(reply, StringComparison.Ordinal))
        {
            op.Abort($"Add XP ended {answer.Outcome?.Kind}: {answer.Outcome?.Detail} (the chat {(chat.Contains(reply, StringComparison.Ordinal) ? "has" : "lacks")} the server's reply)");
            yield break;
        }

        op.Succeed(panel.Model.Status, result.Added);
    }

    private IEnumerable<object?> PlaceSteps(ScreenFrameOp op, string query)
    {
        if (!InShell(out _, out var panel) || !panel.IsOpen || panel.SearchField is not { } search)
        {
            op.Abort("the tools panel is not open");
            yield break;
        }

        Type(search, query);
        yield return 4;
        if (panel.Model.Query != query || panel.Rows[0] is not { Visible: true } row)
        {
            op.Abort($"the search for '{query}' listed no first place (query '{panel.Model.Query}')");
            yield break;
        }

        var before = panel.Model.LastPlace;
        foreach (var step in Click(row))
        {
            yield return step;
        }

        var place = panel.Model.LastPlace;
        if (place is null || ReferenceEquals(place, before) || place.Kind != ActionKind.ToolPlace)
        {
            op.Abort("the click on the first place did not ask the server to teleport");
            yield break;
        }

        op.Succeed(row.Text);
    }

    // ------------------------------------------------------------------------------------------------ the character list

    private CharacterManagementScreen? Screen => Current as CharacterManagementScreen;

    private CharacterSlot? SlotNamed(string name)
    {
        foreach (var slot in session.State.Characters.Entries)
        {
            if (string.Equals(slot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }
        }

        return null;
    }

    private IEnumerable<object?> SelectSteps(ScreenFrameOp op, string name)
    {
        yield return Wait(() => Screen is { List: not null }, "the character list");
        var row = Screen!.List!.Items.FirstOrDefault(i => i is UiTextElement text && string.Equals(text.Text, name, StringComparison.OrdinalIgnoreCase));
        if (row is null || SlotNamed(name) is not { } slot)
        {
            op.Abort($"the character list has no row for '{name}'");
            yield break;
        }

        foreach (var step in Click(row))
        {
            yield return step;
        }

        yield return Wait(() => Screen is { } s && s.SelectedGuid == slot.Guid, "the row to become the selected character");
        op.Succeed();
    }

    private IEnumerable<object?> DeleteSteps(ScreenFrameOp op)
    {
        if (Screen is not { SelectedGuid: not 0, DeleteButton: { Visible: true } delete } screen)
        {
            op.Abort("the character list has no ready character selected (no Delete button)");
            yield break;
        }

        var guid = screen.SelectedGuid;
        foreach (var step in Click(delete))
        {
            yield return step;
        }

        yield return Wait(() => Screen is { DeleteDialog: not 0 }, "the delete confirmation dialog");
        var dialog = Manager.Dialogs.Find(Screen!.DeleteDialog);
        if (dialog?.GetChildRecursive(UiConfirmationTextInputDialog.Field) is not UiTextElement field || dialog.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton) is not { } done)
        {
            op.Abort("the delete dialog has no text field or Done button");
            yield break;
        }

        // M6 sprint (the run 6 recording, 2026-11-19 21:37:44 UTC): without holds the confirmation box was up for about a tenth of a second (the word typed and Done pressed in five frames), so the
        // delete read as a blink. Under --video-pace the box is held empty, then with the typed word, and the list with the character marked for deletion; without it nothing changes.
        foreach (var step in VideoHold())
        {
            yield return step;
        }

        Type(field, "delete");
        yield return 3;
        if (field.Text != "delete")
        {
            op.Abort($"the delete dialog's field holds '{field.Text}', not the word to type");
            yield break;
        }

        foreach (var step in VideoHold())
        {
            yield return step;
        }

        foreach (var step in Click(done))
        {
            yield return step;
        }

        yield return Wait(() => Screen is { PleaseWaitDialog: 0, DeleteDialog: 0 } && session.State.Characters.Entries.Any(e => e.Guid == guid && e.PendingDeletion), "the server to mark the character pending deletion and the Please Wait dialog to close");
        yield return 3;
        foreach (var step in VideoHold())
        {
            yield return step;
        }

        op.Succeed();
    }

    private IEnumerable<object?> RestoreSteps(ScreenFrameOp op)
    {
        if (Screen is not { SelectedGuid: not 0, RestoreButton: { Visible: true } restore } screen)
        {
            op.Abort("the character list has no pending character selected (no Restore button)");
            yield break;
        }

        var guid = screen.SelectedGuid;
        foreach (var step in Click(restore))
        {
            yield return step;
        }

        yield return Wait(() => Screen is { PleaseWaitDialog: 0 } && session.State.Characters.Entries.Any(e => e.Guid == guid && !e.PendingDeletion), "the server to restore the character and the Please Wait dialog to close");
        yield return 3;
        op.Succeed();
    }
}
