using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The answer to a retail feature M3 does not build yet (a panel page with no controller, the combat panel and examination until item 3.2): retail's own message dialog with a Lifestone literal
/// ("Not in Lifestone yet: ..."), logged as a placeholder so it is never mistaken for retail behaviour. M2's features that stay out keep <see cref="NotInM2"/> (the chat focus, the options page's
/// unbuilt choices, the login screens); this one is the panel host's and the toolbar's.
/// </summary>
public static class NotInM3
{
    /// <summary>The text the dialog shows before the feature's name.</summary>
    public const string Prefix = "Not in Lifestone yet: ";

    /// <summary>
    /// Opens the message dialog for <paramref name="feature"/> (a short English name, e.g. "Map") in <paramref name="dialogs"/> and logs it as a placeholder. Returns the dialog's context.
    /// </summary>
    public static uint Show(DialogFactory dialogs, ILogSink log, string feature)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        log.Warn("retailui", $"placeholder: '{feature}' is not in Lifestone M3; a retail message dialog says so");
        var data = UiDialogData.Of(UiDialogType.Message, UiStringRef.FromLiteral(Prefix + feature))
            .WithFlag(UiPropertyIds.DialogModal, true);
        return dialogs.MakeDialog(data);
    }
}
