using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// One press reaches a list box twice (its row's bubbled message and its own MouseDown), so the list sends a Selection and at once a Reselect, or two Reselects on the selected row (UiWidgetTests pins that). A listener that toggles
/// on a reselect (the skills tab, the effects page: "the same row again clears it") must toggle once per press, not once per message (M6 7 S: a click on a skill row selected it and cleared it in the same click).
/// This keys on the press itself (<see cref="Input.UiInputRouter.MousePresses"/>), never on the clock: a Reselect is ignored only when a message of the same press was handled already.
/// </summary>
public sealed class ListPressGuard(UiElementManager manager)
{
    private long _press = manager.Input.MousePresses;
    private bool _firstHandled;

    /// <summary>
    /// True when this message is the second of a press whose first was handled. A message that comes with a new press is that press's first (handled); a Reselect that follows it in the same press is ignored once;
    /// anything else (a selection made in code, with no press) is handled.
    /// </summary>
    public bool IsRepeatOfPress(uint messageId)
    {
        var press = manager.Input.MousePresses;
        if (press != _press)
        {
            (_press, _firstHandled) = (press, true);
            return false;
        }

        if (_firstHandled && messageId == UiMessageIds.ListBoxReselect)
        {
            _firstHandled = false;
            return true;
        }

        _firstHandled = false;
        return false;
    }
}
