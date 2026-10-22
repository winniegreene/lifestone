using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Field (class 3, retail's <c>UIElement_Field</c>): the plain container every mode root and most windows are, and a
/// drop target. Text entry is the Text class (0x0C), not this one.
/// </summary>
/// <remarks>
/// <para>
/// As a drop target (<c>UIElement_Field::MouseOverTop</c>, 11.4186 0x00472360, 11.6096 0x00472720, and
/// <c>CatchDroppedItem</c>, 11.4186 0x00472450, 11.6096 0x00472810), a container field (property 0x36) with drag rollover
/// (0x70) shows <see cref="UiStateIds.DragRolloverAccept"/> or <see cref="UiStateIds.DragRolloverReject"/> while an item is
/// dragged over it, and goes back to its old state when the item leaves or drops. Since M4 2.2 s3 the router has a dragged item
/// (<see cref="UiDrag"/>: <c>UiInputRouter.DragElement</c> is the pressed <see cref="UiItem"/> cell while a drag is on); the item cells show the states through
/// <see cref="EnterDragRollover"/> and <see cref="LeaveDragRollover"/>, which this field's <c>MouseOverTop</c> uses too.
/// </para>
/// </remarks>
public class UiField : UiElement
{
    private bool _rolloverStateChange;
    private uint _oldState;

    /// <summary>A field of <paramref name="init"/>.</summary>
    public UiField(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>
    /// Decides whether a dragged item may drop here (retail's <c>m_dragDropCallback</c>); null accepts nothing, as retail's
    /// default callback (which does nothing and answers no).
    /// </summary>
    public Func<UiElement, UiElement, bool>? DragDropCallback { get; set; }

    /// <summary><c>UIElement_Field::MouseOverTop</c>: the drag-rollover states while an item is dragged over the field.</summary>
    public override void MouseOverTop(bool overTop)
    {
        base.MouseOverTop(overTop);
        if (overTop)
        {
            if (Manager.Input.DragElement is not { } dragged
                || !(TryGetBoolProperty(UiPropertyIds.ElementContainer, out var container) && container)
                || !(TryGetBoolProperty(UiPropertyIds.FieldDragRollover, out var rollover) && rollover))
            {
                return;
            }

            var disallow = TryGetBoolProperty(UiPropertyIds.ElementDisallowDragIn, out var d) && d;
            EnterDragRollover(DragDropCallback?.Invoke(dragged, this) == true && !disallow);
        }
        else
        {
            LeaveDragRollover();
        }
    }

    /// <summary>The field takes the accept or reject drag-rollover state (a state the desc lacks becomes the base state, as <see cref="UiElement.SetState"/>), remembering the state it had.</summary>
    protected void EnterDragRollover(bool accept)
    {
        var state = accept ? UiStateIds.DragRolloverAccept : UiStateIds.DragRolloverReject;
        if (state != State)
        {
            if (!_rolloverStateChange)
            {
                _rolloverStateChange = true;
                _oldState = State;
            }

            SetState(state);
        }
    }

    /// <summary>The field goes back to the state it had before <see cref="EnterDragRollover"/>; true when it had changed.</summary>
    protected bool LeaveDragRollover()
    {
        if (!_rolloverStateChange)
        {
            return false;
        }

        SetState(_oldState);
        _rolloverStateChange = false;
        return true;
    }
}
