namespace Lifestone.Data.Ui;

/// <summary>
/// A rectangle of layout pixels with inclusive corners, retail's <c>Box2D</c>: (X0, Y0) is the first pixel and (X1, Y1)
/// the last, so <c>Width = X1 - X0 + 1</c>. An element's box is relative to its parent's top-left corner.
/// </summary>
/// <remarks>
/// Retail builds an element's box from its position and size as <c>(x, y, x + width - 1, y + height - 1)</c>
/// (<c>ElementDesc::UpdateSizeAndPosition</c>, <c>ElementDesc::Incorporate</c>), so a zero-sized box has
/// <c>X1 = X0 - 1</c>; <see cref="UiAnchor"/> relies on that, and so the type does not normalise or clamp.
/// </remarks>
/// <param name="X0">Left column (inclusive).</param>
/// <param name="Y0">Top row (inclusive).</param>
/// <param name="X1">Right column (inclusive).</param>
/// <param name="Y1">Bottom row (inclusive).</param>
public readonly record struct UiBox(int X0, int Y0, int X1, int Y1)
{
    /// <summary><c>X1 - X0 + 1</c>.</summary>
    public int Width => X1 - X0 + 1;

    /// <summary><c>Y1 - Y0 + 1</c>.</summary>
    public int Height => Y1 - Y0 + 1;

    /// <summary>The box at (<paramref name="x"/>, <paramref name="y"/>) of the given size, as retail builds it.</summary>
    public static UiBox FromPositionAndSize(int x, int y, int width, int height) =>
        new(x, y, x + width - 1, y + height - 1);

    /// <summary>For logs and test failures: <c>(x0,y0)-(x1,y1) WxH</c>.</summary>
    public override string ToString() => $"({X0},{Y0})-({X1},{Y1}) {Width}x{Height}";
}

/// <summary>
/// How one edge of an element follows its parent when the parent changes size: the values 0-4 of an ElementDesc's
/// <c>LeftEdge</c>, <c>TopEdge</c>, <c>RightEdge</c> and <c>BottomEdge</c>, as <c>StateDesc::UpdateSizeAndPosition</c>
/// (11.4186 0x0069BF20, 11.6096 0x0069CDB0) treats them (<see cref="UiAnchor"/>).
/// </summary>
/// <remarks>
/// Retail has no names for these values (the layout source wrote them as integers), so the names here describe what
/// the code does. Coordinates are relative to the parent's left and top, so an edge that "keeps its coordinate" stays a
/// fixed distance from the parent's left (or top) side. The census of the 101 retail layouts (UiLayoutCensusTests) uses
/// every value on every side except <see cref="Proportional"/> on the top and bottom edges.
/// </remarks>
public enum UiEdge : uint
{
    /// <summary>0: retail's switch has no case for it, so the edge keeps its coordinate.</summary>
    Keep = 0,

    /// <summary>
    /// 1: the edge stays a fixed distance from the parent's side of the same name. A left or top edge keeps its
    /// coordinate; a right or bottom edge moves by the parent's change in width or height.
    /// </summary>
    OwnSide = 1,

    /// <summary>
    /// 2: the edge stays a fixed distance from the parent's opposite side. A left or top edge moves by the parent's
    /// change in width or height; a right or bottom edge keeps its coordinate.
    /// </summary>
    OppositeSide = 2,

    /// <summary>3: the element is centred in the new parent, with its original size (C integer division).</summary>
    Centre = 3,

    /// <summary>4: the coordinate is scaled by the ratio of the new parent size to the old one, truncated toward zero.</summary>
    Proportional = 4,
}

/// <summary>An element's four edge modes, in retail's argument order (left, top, right, bottom).</summary>
/// <param name="Left">The left edge's mode.</param>
/// <param name="Top">The top edge's mode.</param>
/// <param name="Right">The right edge's mode.</param>
/// <param name="Bottom">The bottom edge's mode.</param>
public readonly record struct UiEdges(UiEdge Left, UiEdge Top, UiEdge Right, UiEdge Bottom)
{
    /// <summary>All four edges <see cref="UiEdge.Keep"/> (the default of a new ElementDesc).</summary>
    public static UiEdges None => default;

    /// <summary>For logs: the four values as digits, left-top-right-bottom (e.g. <c>1122</c>).</summary>
    public override string ToString() => $"{(uint)Left}{(uint)Top}{(uint)Right}{(uint)Bottom}";
}

/// <summary>
/// A StateDesc's incorporation flags (<c>m_uiIncorporationFlags</c>): which of its fields override a base desc's when
/// <c>StateDesc::Incorporate</c> merges them, and, on an ElementDesc, which position fields the dat stores
/// (<c>ElementDesc::Serialize</c>). The flags are ORed into the merged desc.
/// </summary>
[Flags]
public enum UiIncorporation : uint
{
    /// <summary>Nothing overrides.</summary>
    None = 0,

    /// <summary><c>m_bPassToChildren</c>.</summary>
    PassToChildren = 0x1,

    /// <summary><c>m_x</c>.</summary>
    X = 0x2,

    /// <summary><c>m_y</c>.</summary>
    Y = 0x4,

    /// <summary><c>m_width</c>.</summary>
    Width = 0x8,

    /// <summary><c>m_height</c>.</summary>
    Height = 0x10,

    /// <summary><c>m_zLevel</c>.</summary>
    ZLevel = 0x20,
}
