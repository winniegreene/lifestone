using System.Globalization;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
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
}
