using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>Box arithmetic the runtime and the compositor share (retail's <c>Box2D</c> helpers).</summary>
public static class UiBoxes
{
    /// <summary>
    /// <c>Box2D::Intersection</c> (11.4186 0x00466550, 11.6096 0x00466680): the overlap of two inclusive boxes; false (and an empty box) when they do not overlap.
    /// </summary>
    public static bool TryIntersect(UiBox a, UiBox b, out UiBox overlap)
    {
        var x0 = Math.Max(a.X0, b.X0);
        var y0 = Math.Max(a.Y0, b.Y0);
        var x1 = Math.Min(a.X1, b.X1);
        var y1 = Math.Min(a.Y1, b.Y1);
        if (x0 > x1 || y0 > y1)
        {
            overlap = default;
            return false;
        }

        overlap = new UiBox(x0, y0, x1, y1);
        return true;
    }

    /// <summary>True when the box holds at least one pixel.</summary>
    public static bool IsValid(UiBox box) => box.X1 >= box.X0 && box.Y1 >= box.Y0;

    /// <summary>The smallest box holding both (either may be invalid, meaning empty).</summary>
    public static UiBox Union(UiBox a, UiBox b)
    {
        if (!IsValid(a))
        {
            return b;
        }

        if (!IsValid(b))
        {
            return a;
        }

        return new UiBox(Math.Min(a.X0, b.X0), Math.Min(a.Y0, b.Y0), Math.Max(a.X1, b.X1), Math.Max(a.Y1, b.Y1));
    }

    /// <summary>The box moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public static UiBox Offset(UiBox box, int dx, int dy) => new(box.X0 + dx, box.Y0 + dy, box.X1 + dx, box.Y1 + dy);

    /// <summary>True when <paramref name="outer"/> holds every pixel of <paramref name="inner"/>.</summary>
    public static bool Contains(UiBox outer, UiBox inner) =>
        inner.X0 >= outer.X0 && inner.Y0 >= outer.Y0 && inner.X1 <= outer.X1 && inner.Y1 <= outer.Y1;
}
