using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The answer to a retail feature M2 does not build (the Credits button, D38; the gameplay panels, G4): retail's own
/// message dialog with a Lifestone literal ("Not in Lifestone M2: ..."), logged as a placeholder so it is never mistaken
/// for retail behaviour.
/// </summary>
public static class NotInM2
{
    /// <summary>The text the dialog shows before the feature's name.</summary>
    public const string Prefix = "Not in Lifestone M2: ";

    /// <summary>
    /// Opens the message dialog for <paramref name="feature"/> (a short English name, e.g. "Credits") in
    /// <paramref name="dialogs"/> and logs it as a placeholder. Returns the dialog's context.
    /// </summary>
    public static uint Show(DialogFactory dialogs, ILogSink log, string feature)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        log.Warn("retailui", $"placeholder: '{feature}' is not in Lifestone M2; a retail message dialog says so");
        var data = UiDialogData.Of(UiDialogType.Message, UiStringRef.FromLiteral(Prefix + feature))
            .WithFlag(UiPropertyIds.DialogModal, true);
        return dialogs.MakeDialog(data);
    }
}
