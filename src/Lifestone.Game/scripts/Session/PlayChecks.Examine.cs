using System.Globalization;
using System.Numerics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

// M4 3.2 s4c: the examine panel's rows. The only hook is one call in CheckAllAsync after the heal rows (the replay visit's player holds the Handy Healing Kit). Each row judges the session's model and the panel's own state (what the window shows is read back from the window), never the stand-in's
// counters. No creature is examined (D23); the kit is the pack's own Handy Healing Kit. The offline rows prove the client's side (the Identify sent with the Ui source, the answer applied, the window filled) and the stand-in's labelled
// subset of ACE's tables (TODO(m4-standin-appraise-tables)), not ACE's acceptance (the lead's AppraiseLiveTests).
public sealed partial class PlayChecks
{
    /// <summary>
    /// A Healing Kit of the pack is examined through <see cref="GameExaminePanel.Open"/> (what the item list's right click and the paperdoll figure's click call): the Identify is Confirmed (<c>examine-identify-confirmed</c>), the model holds the kit's appraisal
    /// and the window shows it (<c>examine-kit-shown</c>: the title is the kit's name, the Value line is the appraisal's value and the uses line is its uses left), and with --play-frames the offline review frame is saved.
    /// </summary>
    private async Task ExamineChecksAsync(IGameSessionView session)
    {
        const string identifyName = "examine-identify-confirmed";
        const string shownName = "examine-kit-shown";
        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { PanelHost: { Examine: { } examine } })
        {
            Check(identifyName, false, "the shell has no examine panel");
            return;
        }

        var kit = PanelFramesPlan.KitInPack(session.State);
        if (kit == 0)
        {
            Check(identifyName, false, "setup: no Healing Kit in the pack");
            return;
        }

        var fillsBefore = examine.Fills;
        examine.Open(kit);
        var handle = examine.LastIdentify;
        var confirmed = handle is not null && await WaitFor(() => handle.IsCompleted, 10.0) && handle.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var filled = await WaitFor(() => examine.Fills > fillsBefore && !examine.Pending, 10.0);
        Check(
            identifyName,
            confirmed && handle!.Source == ActionSource.Ui && handle.Kind == ActionKind.Identify && examine.ShownGuid == kit && session.TryGetAppraisal(kit, out _),
            $"kit=0x{kit:X8} outcome={handle?.Outcome?.Kind.ToString() ?? "pending"} source={handle?.Source} kind={handle?.Kind} model_has_appraisal={session.TryGetAppraisal(kit, out _)}");

        var appraised = session.TryGetAppraisal(kit, out var appraisal) ? appraisal : null;
        var name = session.State.Objects.TryGet(kit, out var item) ? item.Name : "?";
        var valueLine = appraisal?.Value is { } value ? "Value: " + value.ToString("N0", CultureInfo.InvariantCulture) : "Value: ???";
        var usesLine = appraisal?.UsesLeft is { } left ? "Number of uses remaining: " + left.ToString(CultureInfo.InvariantCulture) : null;
        var lines = examine.Lines;
        var shown = filled && examine.IsOpen && examine.Kind == ExamineKind.Item && examine.Title == (appraised?.Name ?? name) && lines.Contains(valueLine) && (usesLine is null || lines.Contains(usesLine));
        Check(shownName, shown, $"open={examine.IsOpen} kind={examine.Kind} title='{examine.Title}' expected_title='{appraised?.Name ?? name}' value_line='{valueLine}' uses_line='{usesLine}' lines=[{string.Join(" | ", lines)}] fills={examine.Fills - fillsBefore}");

        if (_framesDirectory is not null && shown)
        {
            HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
            await Frames(4);
            SaveExtraFrame(PanelFramesPlan.ExamineStandInFrame);
            RestoreLightsHud();
        }

        examine.Close();
    }

    /// <summary>The stand-in chest the S6 rows place (beside the rows' other chests).</summary>
    public const uint ExamineChestGuid = 0x80000B0E;

    /// <summary>The stand-in magic wand's tables (M5 7 S6; a placeholder for ACE's loot roll): two real spell ids (their names come from the user's dats), Arcane Lore 120, spellcraft 160, 600 of 600 mana, workmanship 6.</summary>
    private static readonly StandInMagicItem ExamineWandMagic = new([1033u, 165u], ArcaneLore: 120, Spellcraft: 160, Mana: 600, MaxMana: 600, Workmanship: 6, Value: 4200)
    {
        // M5 7 D137: three wield requirement kinds (the level, a specialised skill, a race named from the dats) and an activation heritage, so the offline frames show the new lines.
        ExtraInts = System.Collections.Immutable.ImmutableDictionary<uint, int>.Empty
            .Add(158, 7).Add(159, 1).Add(160, 50)
            .Add(270, 8).Add(271, 34).Add(272, 3)
            .Add(273, 12).Add(274, 0).Add(275, 1)
            .Add(188, 3)
            .Add(265, 13).Add(370, 3).Add(372, 2), // D137 L2: the set ("Set: Soldier's") and two gear ratings
    };

    /// <summary>
    /// M5 7 S6 (D136): a stand-in chest holding a stand-in magic wand (weenie 2472's art) is opened. With <c>--examine-review-frames</c> the live capture's own object runs over it and must finish (<c>examine-review-frames-saved</c>);
    /// then a right click on the wand in the chest's window through the input router examines it with its spells, activation line, mana and workmanship (<c>container-row-right-click-examines-it</c>, the offline frame
    /// <see cref="PanelFramesPlan.ExamineChestItemFrame"/>); then the toolbar's Examine with nothing selected enters the examine target mode with UICURSOR 10, and a left click on the wand examines it without selecting it and
    /// ends the mode (<c>toolbar-examine-target-mode</c>). The pointer is the system's, so no frame shows it.
    /// </summary>
    private async Task ExamineEverywhereChecksAsync(IGameSessionView session)
    {
        const string capturedName = "examine-review-frames-saved";
        const string rightClickName = "container-row-right-click-examines-it";
        const string targetName = "toolbar-examine-target-mode";
        if (_host.StandIn is not { } server || _ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { PanelHost.Examine: { } examine } shell || _ui.Manager is not { } manager)
        {
            Check(rightClickName, false, "setup: no stand-in server levers, no shell or no examine panel");
            return;
        }

        await CloseViewedAsync(session);
        var placed = server.PlaceChest(ExamineChestGuid, new Vector3(-1.5f, 2f, 0f), ("Smock", StandInSmockWcid, 1));
        var wand = placed is null ? 0 : server.StockMagicItem(ExamineChestGuid, "Stand-in Magic Wand", 2472, ExamineWandMagic);
        var present = placed is not null && await WaitFor(() => session.State.Objects.Contains(ExamineChestGuid), 10.0);
        var open = present ? session.Actions.OpenContainer(ExamineChestGuid, ActionSource.Host) : null;
        var opened = open is not null && await WaitFor(() => open.IsCompleted, 15.0) && open.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var window = shell.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
        var drawn = opened && window is not null && await WaitFor(() => window.DrawState == ContainerWindowDraw.Drawn && window.ItemRow?.FindItem(wand) is not null, 10.0);
        if (!drawn)
        {
            Check(rightClickName, false, $"setup: chest_placed={placed is not null} present={present} open={open?.Outcome?.Kind.ToString() ?? "none"} drawn={drawn} wand=0x{wand:X8}");
            return;
        }

        // --- --examine-review-frames (when given): the capture runs over this window on its own; it reads, examines through the list and saves, and the rows wait for it to finish ---
        if (_mode.ExamineFrames is { Active: true } frames)
        {
            var finished = await WaitFor(() => !frames.Active, 15.0);
            Check(capturedName, finished && !examine.IsOpen, $"finished={finished} examine_open_after={examine.IsOpen} (the capture closes the window it opened)");
        }

        // --- a right click on the wand in the chest's window, through the router ---
        var fillsBefore = examine.Fills;
        var cell = window!.ItemRow!.FindItem(wand)!;
        var box = cell.ScreenBox;
        manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        manager.Input.MouseButton(UiMouseButton.Right, down: true);
        manager.Input.MouseButton(UiMouseButton.Right, down: false);
        var filled = await WaitFor(() => examine.Fills > fillsBefore && !examine.Pending, 10.0);
        var missing = GameExaminePanel.MissingReviewLines(examine.Lines);
        Check(
            rightClickName,
            filled && examine.IsOpen && examine.ShownGuid == wand && missing is null && window.ItemRow.SelectedItemId == wand,
            $"wand=0x{wand:X8} open={examine.IsOpen} title='{examine.Title}' filled={filled} missing={missing ?? "none"} selected=0x{window.ItemRow.SelectedItemId:X8} lines=[{string.Join(" | ", examine.Lines)}]");
        if (_framesDirectory is not null && filled)
        {
            HideLightsHud(); // the developer HUD would cover the windows; the shell's own windows stay
            await Frames(4);
            SaveExtraFrame(PanelFramesPlan.ExamineChestItemFrame);
            RestoreLightsHud();
        }

        examine.Close();
        await Frames(1);

        // --- the toolbar's Examine with nothing selected: the target mode, its pointer, then a left click on the wand ---
        var target = manager.TargetMode;
        _mode.Selection.Select(0);
        await Frames(2);
        var button = shell.Root?.GetChildRecursive(GameToolbar.ExamineButtonId);
        var clicks = shell.ToolbarUi?.ExamineClicks ?? 0;
        var pressed = button is not null && ClickVendorElement(manager, button);
        await Frames(1);
        var modeOn = target.Mode == UiTargetModeKind.Examine;
        var pointer = manager.Cursor.DefaultCursorDid;
        var listSelected = window.ItemRow.SelectedItemId;
        fillsBefore = examine.Fills;
        box = cell.ScreenBox;
        manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        manager.Input.MouseButton(UiMouseButton.Left, down: true);
        manager.Input.MouseButton(UiMouseButton.Left, down: false);
        var examined = await WaitFor(() => examine.Fills > fillsBefore && !examine.Pending && examine.ShownGuid == wand, 10.0);
        await Frames(1);
        Check(
            targetName,
            pressed && shell.ToolbarUi?.ExamineClicks == clicks + 1 && modeOn && pointer != 0 && examined && !target.Active && manager.Cursor.DefaultCursorDid != pointer && target.LastTarget == wand,
            $"pressed={pressed} mode_on={modeOn} pointer=0x{pointer:X8} examined={examined} mode_after={target.Mode} pointer_after=0x{manager.Cursor.DefaultCursorDid:X8} last_target=0x{target.LastTarget:X8} list_selection_kept={window.ItemRow.SelectedItemId == listSelected}");

        examine.Close();
        await InscriptionRequirementAndFailedRollChecksAsync(session, server, manager, shell, examine, wand);
        await CloseViewedAsync(session);
        server.Remove(ExamineChestGuid);
    }

    /// <summary>
    /// M5 7 D137: the wand is taken into the pack and examined again: its wield requirements read in retail's words, the race and the activation heritage named from the dats (<c>examine-requirement-kinds-named</c>); its box
    /// says "&lt;Inscribe here&gt;" and can be written; the box is given the focus, written and left, which sends the inscription, and the next appraisal shows it signed with this character (<c>examine-inscription-written-and-shown</c>);
    /// then a failed roll on the stand-in vendor shows its health as a percentage and stamina and mana unknown (<c>examine-failed-roll-text</c>). A frame of each state (with --play-frames).
    /// </summary>
    private async Task InscriptionRequirementAndFailedRollChecksAsync(IGameSessionView session, StandInServer server, UiElementManager manager, GamePlayScreen shell, GameExaminePanel examine, uint wand)
    {
        const string requirementName = "examine-requirement-kinds-named";
        const string inscriptionName = "examine-inscription-written-and-shown";
        const string failedName = "examine-failed-roll-text";
        var pick = session.Actions.PickUp(wand, 0, 0, ActionSource.Host);
        var picked = await WaitFor(() => pick.IsCompleted, 15.0) && pick.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var fills = examine.Fills;
        examine.Open(wand);
        var filled = await WaitFor(() => examine.Fills > fills && !examine.Pending, 10.0);
        string[] wanted = ["Wield requires level 50", "Wield requires specialized War Magic", "Wield requires Aluvian race", "Set: Soldier's", "Ratings: Dam 3, Crit 2."];
        var activation = examine.Lines.FirstOrDefault(static l => l.StartsWith("Activation requires", StringComparison.Ordinal)) ?? string.Empty;
        Check(
            requirementName,
            picked && filled && wanted.All(examine.Lines.Contains) && activation.Contains(", Sho", StringComparison.Ordinal),
            $"picked={picked} ({pick.Outcome?.Kind}) filled={filled} activation='{activation}' lines=[{string.Join(" | ", examine.Lines)}]");

        var inscribeHere = examine.InscriptionShown && examine.InscriptionText == GameExaminePanel.InscribeHere && examine.InscriptionEditable;
        await SaveExamineFrameAsync(PanelFramesPlan.ExamineInscribeHereFrame, filled);
        var box = shell.Root?.GetChildRecursive(GameExaminePanel.InscriptionId) as Lifestone.Client.RetailUi.Text.UiTextElement;
        ActionHandle? write = null;
        if (box is not null && inscribeHere)
        {
            manager.SetFocusElement(box);
            box.SetText("For the Den");
            manager.SetFocusElement(null);
            write = examine.LastInscription;
        }

        var written = write is not null && await WaitFor(() => write.IsCompleted, 5.0) && write.Outcome?.Kind == ActionOutcomeKind.Sent;
        fills = examine.Fills;
        examine.Open(wand);
        var refilled = await WaitFor(() => examine.Fills > fills && !examine.Pending, 10.0);
        var name = session.State.Self.Name ?? string.Empty;
        Check(
            inscriptionName,
            inscribeHere && written && refilled && examine.InscriptionText == "For the Den" && examine.SignatureText == "--" + name && examine.InscriptionEditable,
            $"inscribe_here={inscribeHere} written={written} ({write?.Outcome?.Kind}) refilled={refilled} text='{examine.InscriptionText}' signature='{examine.SignatureText}' editable={examine.InscriptionEditable}");
        await SaveExamineFrameAsync(PanelFramesPlan.ExamineInscribedFrame, refilled);
        examine.Close();

        server.FailNextCreatureAppraisal();
        fills = examine.Fills;
        examine.Open(VendorThelnothGuid);
        var answered = await WaitFor(() => examine.Fills > fills && !examine.Pending, 10.0);
        var vitals = examine.VitalLines;
        Check(
            failedName,
            answered && examine.Kind == ExamineKind.Creature && vitals.Count == 3 && System.Text.RegularExpressions.Regex.IsMatch(vitals[0], "^Health: [0-9]+ %$") && vitals[1] == "Stamina: ???" && vitals[2] == "Mana: ???",
            $"answered={answered} kind={examine.Kind} level='{examine.LevelText}' vitals=[{string.Join(" | ", vitals)}]");
        await SaveExamineFrameAsync(PanelFramesPlan.ExamineFailedRollFrame, answered);
        examine.Close();
        await CaptureFramesChecksAsync(session, server, examine, wand);
    }

    /// <summary>
    /// M5 7 D138 (with <c>--examine-capture-creatures</c>): the capture's three proof frames offline. The stand-in vendor is aimed at (a refused cast still names its target, <see cref="IActionApi.LastAttackTarget"/>) with its first roll
    /// failing: the capture inspects it (the failed-roll frame), asks again after ACE's 5 s (the full page's frame); the wand, with its set and ratings, held open gives the item frame (<c>examine-capture-frames-saved</c>).
    /// </summary>
    private async Task CaptureFramesChecksAsync(IGameSessionView session, StandInServer server, GameExaminePanel examine, uint wand)
    {
        if (_mode.CaptureFrames is not { Active: true } frames)
        {
            return;
        }

        server.FailNextCreatureAppraisal();
        session.Actions.Cast(0, VendorThelnothGuid, ActionSource.Host); // refused at once (no spell), but it names the target as the fight's casts do
        var failed = await WaitFor(() => frames.Saved(GameExaminePanel.CaptureFailedRoll), 10.0);
        var full = await WaitFor(() => frames.Saved(GameExaminePanel.CaptureCreature), 15.0);
        examine.Open(wand);
        var item = await WaitFor(() => frames.Saved(GameExaminePanel.CaptureItemExtras), 10.0);
        Check("examine-capture-frames-saved", failed && full && item, $"failed_roll={failed} creature={full} item_extras={item} last_target=0x{session.Actions.LastAttackTarget:X8}");
        examine.Close();
    }

    private async Task SaveExamineFrameAsync(string frame, bool ready)
    {
        if (_framesDirectory is null || !ready)
        {
            return;
        }

        HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
        await Frames(4);
        SaveExtraFrame(frame);
        RestoreLightsHud();
    }
}
