using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The drag bar (class 2, retail's <c>UIElement_Dragbar</c>): pressing it with the left button moves its parent (a
/// window) with the mouse until the button comes up. It takes every message about itself and its children.
/// </summary>
/// <remarks>
/// Re-expressed from <c>UIElement_Dragbar::ListenToElementMessage</c> (11.4186 0x0046C850, 11.6096 0x0046CC30),
/// <c>StartMouseMoving</c> (11.4186 0x0046C760, 11.6096 0x0046CB40) and <c>StopMouseMoving</c> (11.4186 0x0046C7C0,
/// 11.6096 0x0046CBA0), over <see cref="UiElement.StartMovement"/>. The bar's press is the pressed state (3), which the
/// release does not undo (as retail). Retail drag bars take the mouse, but no rule in the 11.4186 decompile read makes
/// them mouse-visible; Lifestone makes drag and resize bars mouse-visible (TODO(retailui-runtime): confirm in 11.6096).
/// </remarks>
public class UiDragbar : UiElement
{
    private bool _mousePressed;

    /// <summary>A drag bar of <paramref name="init"/>.</summary>
    public UiDragbar(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>True while the bar is held (<c>m_mousePressed</c>).</summary>
    public bool IsPressed => _mousePressed;

    /// <inheritdoc/>
    protected override bool GetShouldBeMouseVisible() => true;

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var parent = Parent;
        if (parent is null || !ReferenceEquals(message.Element, this))
        {
            return UiListenResult.Consumed;
        }

        var (x, y) = message.WindowPoint;
        switch (message.MessageId)
        {
            case UiMessageIds.MouseDown:
                if (message.Param1 == UiMouseActions.Left && !parent.IsResizing && !parent.IsMoving)
                {
                    _mousePressed = true;
                    parent.StartMovement(x, y);
                    SetState(UiStateIds.NormalPressed);
                    parent.MouseDown(x, y, message.Param1);
                }

                break;
            case UiMessageIds.MouseUp:
                if (message.Param1 == UiMouseActions.Left && _mousePressed)
                {
                    parent.MouseUp(x, y, UiMouseActions.Left);
                    _mousePressed = false;
                    parent.StopMovement();
                }

                break;
            case UiMessageIds.MouseMove:
                if (parent.IsMoving && _mousePressed)
                {
                    parent.MouseMove(x, y);
                }

                break;
        }

        return UiListenResult.Consumed;
    }
}

/// <summary>
/// The resize bar (class 9, retail's <c>UIElement_Resizebar</c>): pressing it resizes its parent from the border its
/// properties name (0x2A bottom, 0x2B left, 0x2C right, 0x2D top; two make a corner).
/// </summary>
/// <remarks>
/// Re-expressed from <c>UIElement_Resizebar::ListenToElementMessage</c> (11.4186 0x0046B930, 11.6096 0x0046BBE0),
/// <c>StartMouseResizing</c> (11.4186 0x0046B7E0, 11.6096 0x0046BA90) and <c>StopMouseResizing</c> (11.4186 0x0046B8C0,
/// 11.6096 0x0046BB70), over <see cref="UiElement.StartResizing"/>. Mouse visibility as <see cref="UiDragbar"/>.
/// </remarks>
public class UiResizeBar : UiElement
{
    private bool _mousePressed;

    /// <summary>A resize bar of <paramref name="init"/>.</summary>
    public UiResizeBar(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>True while the bar is held.</summary>
    public bool IsPressed => _mousePressed;

    /// <summary>The border the bar's properties name (<c>StartMouseResizing</c>'s choice), or none.</summary>
    public UiBorder Border
    {
        get
        {
            var right = Flag(UiPropertyIds.ResizebarBorderRight);
            var left = Flag(UiPropertyIds.ResizebarBorderLeft);
            var top = Flag(UiPropertyIds.ResizebarBorderTop);
            var bottom = Flag(UiPropertyIds.ResizebarBorderBottom);
            if (right)
            {
                return top ? UiBorder.TopRight : bottom ? UiBorder.BottomRight : UiBorder.Right;
            }

            if (left)
            {
                return top ? UiBorder.TopLeft : bottom ? UiBorder.BottomLeft : UiBorder.Left;
            }

            return top ? UiBorder.Top : bottom ? UiBorder.Bottom : UiBorder.None;
        }
    }

    /// <inheritdoc/>
    protected override bool GetShouldBeMouseVisible() => true;

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var parent = Parent;
        if (parent is null || !ReferenceEquals(message.Element, this))
        {
            return UiListenResult.Consumed;
        }

        var (x, y) = message.WindowPoint;
        switch (message.MessageId)
        {
            case UiMessageIds.MouseDown:
                if (message.Param1 == UiMouseActions.Left && !parent.IsResizing && !parent.IsMoving && Border is var border and not UiBorder.None)
                {
                    _mousePressed = true;
                    parent.StartResizing(border, x, y);
                    SetState(UiStateIds.NormalPressed);
                    parent.MouseDown(x, y, message.Param1);
                }

                break;
            case UiMessageIds.MouseUp:
                if (message.Param1 == UiMouseActions.Left && _mousePressed)
                {
                    parent.MouseUp(x, y, UiMouseActions.Left);
                    _mousePressed = false;
                    parent.StopResizing();
                }

                break;
            case UiMessageIds.MouseMove:
                if (parent.IsResizing && _mousePressed)
                {
                    parent.MouseMove(x, y);
                }

                break;
        }

        return UiListenResult.Consumed;
    }

    private bool Flag(uint id) => TryGetBoolProperty(id, out var value) && value;
}
