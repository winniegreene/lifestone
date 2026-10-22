using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The answer to a retail feature that a later item of Lifestone M4 builds (M4 2.2 s2: a right click on an item examines it in retail, and the examine panel is item 3.2's): retail's own message dialog with a Lifestone
/// literal ("&lt;feature&gt; is built in M4 &lt;item&gt;"), logged as a placeholder so it is never mistaken for retail behaviour. <see cref="NotInM3"/> and <see cref="NotInM2"/> are the earlier milestones' answers.
/// </summary>
public static class NotInM4
{
    /// <summary>
    /// The text the dialog shows for <paramref name="feature"/> (a short English name with its article, e.g. "The examine panel") built by <paramref name="item"/> (the plan's item number, e.g. "3.2"):
    /// "The examine panel is built in M4 3.2".
    /// </summary>
    public static string TextFor(string feature, string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        return feature + " is built in M4 " + item;
    }

    /// <summary>Opens the message dialog for <paramref name="feature"/> in <paramref name="dialogs"/> and logs it as a placeholder. Returns the dialog's context.</summary>
    public static uint Show(DialogFactory dialogs, ILogSink log, string feature, string item)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        var text = TextFor(feature, item);
        log.Warn("retailui", $"placeholder: '{text}'; a retail message dialog says so");
        var data = UiDialogData.Of(UiDialogType.Message, UiStringRef.FromLiteral(text))
            .WithFlag(UiPropertyIds.DialogModal, true);
        return dialogs.MakeDialog(data);
    }
}
