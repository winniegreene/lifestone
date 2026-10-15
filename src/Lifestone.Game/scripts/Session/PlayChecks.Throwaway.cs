using Godot;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The offline run of <c>--login-check=throwaway</c>'s screen driver (<see cref="ThrowawayScreens"/>; m2-plan section 6.9) on the stand-in: the tools-panel buttons in the world, and character
/// creation (the taken name, the banned name, a free name), Delete, Restore and Delete on the character list. The stand-in cannot run the whole leg: it enters only its first character
/// (so entering the new character and the graduated one, and the purge's effect on a log off list, cannot be served), but every screen the leg clicks is the real one with the real stand-in answers.
/// The scenario's own script is tested against a scripted account in <c>ThrowawayLegScenarioTests</c>. The named gap is <c>TODO(throwaway-standin)</c>.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>Runs <paramref name="op"/> a frame at a time until it ends or <paramref name="seconds"/> pass.</summary>
    private async Task<bool> RunOp(IGameSessionView session, ScreenOp op, double seconds)
    {
        return await WaitFor(
            () =>
            {
                op.Advance(session.Clock.Now);
                return op.Done;
            },
            seconds);
    }

    /// <summary>The driver's tools-panel buttons: open with F7, Where am I, Add XP refused for the wrong character and pressed for the right one, and the place click (done by <see cref="ToolsPanelPlaceAsync"/>).</summary>
    private async Task ThrowawayToolOpsAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        Log.Warn(LogCategory, "TODO(throwaway-standin): the stand-in runs only the throwaway leg's screen driver here, not the whole leg (it enters only its first character, so creating, entering and purging the throwaway run live only: --login-check=throwaway)");
        if (ui is null)
        {
            Check("throwaway-driver-tools-ops", false, "no retail UI");
            return;
        }

        var screens = new ThrowawayScreens(ui, session);
        var opened = screens.OpenToolsPanel();
        Check("throwaway-driver-opens-the-tools-panel-with-f7", await RunOp(session, opened, 10) && opened.Succeeded, $"failure={opened.Failure}");

        var where = screens.PressWhere();
        var whereDone = await RunOp(session, where, 15);
        Check("throwaway-driver-where-am-i-returns-the-metres-between-the-server-and-the-client", whereDone && where.Succeeded && where.Number is >= 0 and <= ThrowawayLegScenario.WhereToleranceMetres, $"failure={where.Failure} metres={where.Number:0.00}");

        // The guard (D61): Add XP is refused, with nothing pressed, unless the character in the world is the named one.
        var lastXp = (ui.Screens?.Flow.CurrentScreen as GamePlayScreen)?.ToolsUi?.Model.LastXp;
        var refused = screens.PressAddXp("Lifestone Throwaway", ThrowawayLegScenario.XpAmount);
        var refusedDone = await RunOp(session, refused, 5);
        var untouched = ReferenceEquals((ui.Screens?.Flow.CurrentScreen as GamePlayScreen)?.ToolsUi?.Model.LastXp, lastXp);
        Check("throwaway-driver-refuses-add-xp-for-a-character-that-is-not-the-throwaway-and-presses-nothing", refusedDone && refused.Failure is { } why && why.Contains("refused", StringComparison.Ordinal) && untouched, $"failure={refused.Failure} untouched={untouched}");

        var xp = screens.PressAddXp(session.State.Self.Name ?? string.Empty, ThrowawayLegScenario.XpAmount);
        var xpDone = await RunOp(session, xp, 15);
        Check("throwaway-driver-add-xp-is-confirmed-in-the-chat-for-the-named-character", xpDone && xp.Succeeded && xp.Number == ThrowawayLegScenario.XpAmount, $"failure={xp.Failure} added={xp.Number}");

        var closed = screens.CloseToolsPanel();
        Check("throwaway-driver-closes-the-tools-panel-with-f7", await RunOp(session, closed, 5) && closed.Succeeded && (ui.Screens?.Flow.CurrentScreen as GamePlayScreen)?.ToolsUi?.IsOpen == false, $"failure={closed.Failure}");
    }

    /// <summary>Character creation to the summary, the taken name, the banned name and a free name; then Delete, Restore, Delete on the character list.</summary>
    private async Task ThrowawayListOpsAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (ui?.Flow is not { } flow)
        {
            Check("throwaway-driver-list-ops", false, "no retail UI");
            return;
        }

        var screens = new ThrowawayScreens(ui, session);
        var open = screens.OpenCharGenSummary();
        var opened = await RunOp(session, open, 30) && open.Succeeded;
        Check("throwaway-driver-reaches-the-summary-page-by-clicks-from-the-character-list", opened, $"failure={open.Failure}");
        if (!opened)
        {
            return;
        }

        var taken = screens.Finish("Stand-in Alpha");
        var takenDone = await RunOp(session, taken, 15);
        Check("throwaway-driver-finish-with-a-listed-name-shows-the-name-refused-dialog", takenDone && taken.Succeeded && !string.IsNullOrEmpty(taken.Text), $"failure={taken.Failure} text='{taken.Text}'");
        var dismissed = screens.DismissErrorDialog();
        Check("throwaway-driver-dismisses-the-dialog", await RunOp(session, dismissed, 10) && dismissed.Succeeded, $"failure={dismissed.Failure}");

        var banned = screens.Finish(ThrowawayLegScenario.InvalidName);
        var bannedDone = await RunOp(session, banned, 15);
        Check("throwaway-driver-finish-with-the-banned-name-shows-the-name-refused-dialog", bannedDone && banned.Succeeded && !string.IsNullOrEmpty(banned.Text) && banned.Text != taken.Text, $"failure={banned.Failure} text='{banned.Text}' (the taken name's was '{taken.Text}')");
        var dismissedAgain = screens.DismissErrorDialog();
        Check("throwaway-driver-dismisses-the-second-dialog", await RunOp(session, dismissedAgain, 10) && dismissedAgain.Succeeded, $"failure={dismissedAgain.Failure}");

        var created = screens.Finish(ThrowawayLegScenario.Throwaway);
        var createdDone = await RunOp(session, created, 15);
        Check("throwaway-driver-finish-with-a-free-name-is-accepted-and-character-creation-closes", createdDone && created.Succeeded && created.Text is null, $"failure={created.Failure} text='{created.Text}'");

        // The stand-in cannot enter the new character: character management comes back with the placeholder message that says so (a person presses OK).
        var back = await WaitFor(() => ui.Screens?.Flow.CurrentScreen is CharacterManagementScreen { ErrorDialog: not 0, List.Items.Count: > 3 }, 15);
        Check("the-new-character-is-listed-on-character-management", back, $"mode={(ui.Screens?.Flow.CurrentScreen?.GetType().Name ?? "none")}");
        if (ui.Screens?.Flow.CurrentScreen is CharacterManagementScreen management && management.ErrorDialog != 0
            && ui.Manager.Dialogs.Find(management.ErrorDialog)?.GetChildRecursive(UiMessageDialog.OkButton) is { } ok)
        {
            await ClickAsync(this, ui.Manager, ok);
        }

        foreach (var (step, make) in new (string, Func<ScreenOp>)[]
        {
            ("select", () => screens.SelectCharacter(ThrowawayLegScenario.Throwaway)),
            ("delete", screens.DeleteSelected),
            ("select", () => screens.SelectCharacter(ThrowawayLegScenario.Throwaway)),
            ("restore", screens.RestoreSelected),
            ("select", () => screens.SelectCharacter(ThrowawayLegScenario.Throwaway)),
            ("delete", screens.DeleteSelected),
        })
        {
            var op = make();
            var done = await RunOp(session, op, 20) && op.Succeeded;
            var slot = session.State.Characters.Entries.Cast<CharacterSlot?>().FirstOrDefault(c => c!.Value.Name == ThrowawayLegScenario.Throwaway);
            var expected = step switch
            {
                "delete" => slot is { PendingDeletion: true },
                "restore" => slot is { PendingDeletion: false },
                _ => session.State.Characters.SelectedGuid == slot?.Guid,
            };
            Check($"throwaway-driver-{step}-on-the-character-list-{(step == "select" ? "selects-the-row" : step == "delete" ? "marks-it-pending-deletion" : "makes-it-ready-again")}", done && expected, $"failure={op.Failure} pending={slot?.PendingDeletion}");
            if (!done)
            {
                break;
            }
        }
    }
}
