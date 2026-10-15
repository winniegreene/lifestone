using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// How a live element follows its parent's size: <c>UIElement::UpdateForParentSizeChange</c> (11.4186 0x00462640,
/// 11.6096 0x00462720), re-expressed on the data track's <see cref="UiAnchor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The element's <b>original</b> box (its desc's position and size, never the current box) is anchored by its edges
/// against its parent's original box changing to the parent's current box, so repeated resizes never accumulate
/// rounding. A root (or an element with no parent) anchors from its layout's design size (800×600, or 800×500) to the
/// display (<see cref="UiElementManager.DisplaySize"/>).
/// </para>
/// <para>
/// An edge of mode 0 (<see cref="UiEdge.Keep"/>) keeps the element's <b>current</b> coordinate once the element has a
/// size or is initialized, so code that moved an element (a dragged window) is not undone by its parent's resize.
/// </para>
/// </remarks>
public static class UiRuntimeLayout
{
    /// <summary>Re-anchors <paramref name="element"/> to its parent's (or the display's) current size.</summary>
    public static void UpdateForParentSizeChange(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var original = element.Desc.BaseState.Box;
        UiBox originalParent;
        UiBox currentParent;
        var parent = element.Parent;
        if (parent is null || element.IsRoot)
        {
            var (displayWidth, displayHeight) = element.Manager.DisplaySize;
            originalParent = UiBox.FromPositionAndSize(0, 0, element.Layout.Width, element.Layout.Height);
            currentParent = UiBox.FromPositionAndSize(0, 0, displayWidth, displayHeight);
        }
        else
        {
            originalParent = parent.Desc.BaseState.Box;
            currentParent = parent.Box;
        }

        var edges = element.Desc.Edges;
        var anchored = UiAnchor.UpdateSizeAndPosition(original, originalParent, currentParent, edges);
        var current = element.Box;
        if (current.Width != 0 || current.Height != 0 || element.IsInitialized)
        {
            anchored = new UiBox(
                edges.Left == UiEdge.Keep ? current.X0 : anchored.X0,
                edges.Top == UiEdge.Keep ? current.Y0 : anchored.Y0,
                edges.Right == UiEdge.Keep ? current.X1 : anchored.X1,
                edges.Bottom == UiEdge.Keep ? current.Y1 : anchored.Y1);
        }

        element.MoveTo(anchored.X0, anchored.Y0);
        element.ResizeTo(anchored.X1 - anchored.X0 + 1, anchored.Y1 - anchored.Y0 + 1);
    }
}
