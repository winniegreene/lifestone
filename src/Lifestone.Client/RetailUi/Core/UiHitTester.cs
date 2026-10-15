namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// Which element the mouse is over: <c>MouseHitTester&lt;MouseOverCriteria&gt;::Recurse</c> (11.4186 0x00459370, 11.6096
/// 0x00459490), as <c>UIElementManager::UpdateMouseOver</c> (11.4186 0x0045C2B0, 11.6096 0x0045C390) runs it from the
/// manager's root.
/// </summary>
/// <remarks>
/// A hidden element hides its whole subtree. Otherwise the children are tried front to back (the reverse of draw order),
/// each only where the point is over it (<see cref="UiElement.PointIsOverRegion"/>, which honours a hit-test mask),
/// and the first that answers wins; when none does, the element itself answers if the mouse can hit it
/// (<see cref="UiElement.IsMouseVisible"/>) or it blocks clicks. So an element that is not mouse-visible lets the point
/// through to what is behind it.
/// </remarks>
public static class UiHitTester
{
    /// <summary>The element under (<paramref name="x"/>, <paramref name="y"/>), in <paramref name="element"/>'s own coordinates, or null.</summary>
    public static UiElement? HitTest(UiElement element, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Recurse(element, x, y, out var hit) ? hit : null;
    }

    private static bool Recurse(UiElement element, int x, int y, out UiElement? hit)
    {
        hit = null;
        if (!element.Visible)
        {
            return false;
        }

        var children = element.Children;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child.PointIsOverRegion(x, y) && Recurse(child, x - child.Box.X0, y - child.Box.Y0, out hit))
            {
                return true;
            }
        }

        if (element.IsMouseVisible || element.BlocksClicks)
        {
            hit = element;
            return true;
        }

        return false;
    }
}
