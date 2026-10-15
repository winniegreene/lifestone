namespace Lifestone.Data.Ui;

/// <summary>
/// Retail's anchoring: how a desc's box moves and resizes when its parent changes size, by its four edge modes
/// (<see cref="UiEdge"/>). An exact integer port of <c>StateDesc::UpdateSizeAndPosition</c> (11.4186 0x0069BF20,
/// 11.6096 0x0069CDB0) and <c>ElementDesc::UpdateSizeAndPosition</c> (11.4186 0x0069A830, 11.6096 0x0069B6A0),
/// re-expressed by behaviour; OpenAC's <c>UiLayoutPolicy</c> (MIT, 1ee5a08) was read only as a cross-check.
/// </summary>
/// <remarks>
/// <para>
/// Boxes are inclusive and relative to the parent's top-left corner; only the parents' sizes matter, never their
/// positions. With <c>dW</c>/<c>dH</c> the change in the parent's width and height and <c>newW</c>/<c>newH</c> its new
/// size, each edge moves by its mode (the others keep their coordinate):
/// </para>
/// <list type="table">
///   <listheader><term>Mode</term><description>Left / top edge; right / bottom edge</description></listheader>
///   <item><term>1 OwnSide</term><description>keeps; <c>+ dW</c> (<c>+ dH</c>)</description></item>
///   <item><term>2 OppositeSide</term><description><c>+ dW</c> (<c>+ dH</c>); keeps</description></item>
///   <item><term>3 Centre</term><description><c>newW / 2 - w / 2</c>; <c>newW / 2 + w / 2 - 1</c>, with C integer
///   division and <c>w</c> the desc's own width (so two odd sizes lose a pixel)</description></item>
///   <item><term>4 Proportional</term><description>the coordinate times <c>newW / oldW</c> (<c>newH / oldH</c>), truncated
///   toward zero</description></item>
/// </list>
/// <para>
/// <b>Precision.</b> The ratios are divided in double precision. The width ratio is then stored to a 4-byte stack slot,
/// so it is rounded to a float, while the height ratio stays in an x87 register as a double: the same "double registers,
/// float stores" treatment as M1's TerrainSampler. It changes pixels: from 800 to 1024 wide the ratio 1.28 rounds down
/// to 1.27999997f, so a Proportional left edge at 100 lands on 127, where a double ratio gives 128 (UiAnchorTests). A
/// ratio is 0 when the old parent is empty. The product is converted with the compiler's 64-bit conversion and its low
/// 32 bits kept, which truncates toward zero; Proportional edges with negative coordinates do not occur in the retail
/// layouts (the 11 elements that use the mode, with 8 left and 8 right edges, are checked), so that case rests on this
/// reading of the conversion.
/// </para>
/// <para>
/// Nothing is clamped: a parent smaller than the desc can give it a negative position or size, as in retail.
/// </para>
/// </remarks>
public static class UiAnchor
{
    /// <summary>
    /// <c>StateDesc::UpdateSizeAndPosition</c>: the new box of a desc whose parent changes from
    /// <paramref name="oldParent"/> to <paramref name="newParent"/>.
    /// </summary>
    /// <param name="self">The desc's box, relative to its parent.</param>
    /// <param name="oldParent">The parent's box before the change.</param>
    /// <param name="newParent">The parent's box after it.</param>
    /// <param name="edges">The desc's edge modes (an element's own; for a state, its element's).</param>
    public static UiBox UpdateSizeAndPosition(UiBox self, UiBox oldParent, UiBox newParent, UiEdges edges)
    {
        // Parent sizes as retail derives them; int arithmetic wraps as the 32-bit code does.
        var oldSpanX = oldParent.X1 - oldParent.X0;
        var oldSpanY = oldParent.Y1 - oldParent.Y0;
        var newWidth = newParent.X1 - newParent.X0 + 1;
        var newHeight = newParent.Y1 - newParent.Y0 + 1;
        var deltaWidth = (newParent.X1 - newParent.X0) - oldSpanX;
        var deltaHeight = (newParent.Y1 - newParent.Y0) - oldSpanY;

        // The width ratio goes through a float slot; the height ratio stays double (see remarks).
        var widthRatio = oldSpanX == -1 ? 0f : (float)((double)newWidth / (oldSpanX + 1));
        var heightRatio = oldSpanY == -1 ? 0.0 : (double)newHeight / (oldSpanY + 1);

        var width = self.X1 - self.X0 + 1;
        var height = self.Y1 - self.Y0 + 1;

        var left = edges.Left switch
        {
            UiEdge.OppositeSide => self.X0 + deltaWidth,
            UiEdge.Centre => (newWidth / 2) - (width / 2),
            UiEdge.Proportional => Truncate(self.X0 * (double)widthRatio),
            _ => self.X0,
        };
        var right = edges.Right switch
        {
            UiEdge.OwnSide => self.X1 + deltaWidth,
            UiEdge.Centre => (newWidth / 2) + (width / 2) - 1,
            UiEdge.Proportional => Truncate(self.X1 * (double)widthRatio),
            _ => self.X1,
        };
        var top = edges.Top switch
        {
            UiEdge.OppositeSide => self.Y0 + deltaHeight,
            UiEdge.Centre => (newHeight / 2) - (height / 2),
            UiEdge.Proportional => Truncate(self.Y0 * heightRatio),
            _ => self.Y0,
        };
        var bottom = edges.Bottom switch
        {
            UiEdge.OwnSide => self.Y1 + deltaHeight,
            UiEdge.Centre => (newHeight / 2) + (height / 2) - 1,
            UiEdge.Proportional => Truncate(self.Y1 * heightRatio),
            _ => self.Y1,
        };
        return new UiBox(left, top, right, bottom);
    }

    /// <summary>
    /// <c>StateDesc::UpdateSizeAndPosition</c> applied to a state: the state with its position and size re-anchored.
    /// </summary>
    public static UiStateDesc UpdateSizeAndPosition(UiStateDesc state, UiBox oldParent, UiBox newParent, UiEdges edges)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.WithBox(UpdateSizeAndPosition(state.Box, oldParent, newParent, edges));
    }

    /// <summary>
    /// <c>ElementDesc::UpdateSizeAndPosition</c> with the element's own edges: re-anchors its base state to the parent's
    /// change, then each child, by the child's own edges, to the element's own change.
    /// </summary>
    public static UiElementDesc UpdateSizeAndPosition(UiElementDesc element, UiBox oldParent, UiBox newParent)
    {
        ArgumentNullException.ThrowIfNull(element);
        return UpdateSizeAndPosition(element, oldParent, newParent, element.Edges);
    }

    /// <summary>
    /// <c>ElementDesc::UpdateSizeAndPosition</c>: re-anchors the element's base state by <paramref name="edges"/>, then
    /// each child (recursively, by the child's own edges) from the element's old box to its new one. The element's other
    /// states are not touched, as in retail; the children are re-anchored even when the element's size did not change.
    /// </summary>
    public static UiElementDesc UpdateSizeAndPosition(UiElementDesc element, UiBox oldParent, UiBox newParent, UiEdges edges)
    {
        ArgumentNullException.ThrowIfNull(element);
        var oldSelf = element.Box;
        var baseState = UpdateSizeAndPosition(element.BaseState, oldParent, newParent, edges);
        var newSelf = baseState.Box;
        var children = element.Children.Count == 0
            ? element.Children
            : UiOrdered.Dictionary(
                element.Children.Select(kv => (kv.Key, UpdateSizeAndPosition(kv.Value, oldSelf, newSelf, kv.Value.Edges))),
                key => new InvalidOperationException($"Duplicate child 0x{key:X8}."));
        return element with { BaseState = baseState, Children = children };
    }

    /// <summary>The 64-bit conversion of the product, low 32 bits kept: truncation toward zero (see remarks).</summary>
    private static int Truncate(double value) => unchecked((int)(long)value);
}
