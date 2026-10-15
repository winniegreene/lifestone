using Lifestone.Client.Play;

namespace Lifestone.Client.RetailUi.Selection;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: VividTargetIndicator::OnDraw 0x004F62B0 and constructor 0x004F6AE0;
// SmartBox::GetObjectBoundingBox is not located in 11.4186 here (11.6096 0x00452E60 only).
/// <summary>
/// The vivid target indicator placement, re-expressed from <c>VividTargetIndicator::OnDraw</c> (11.6096 0x004F6EF0; docs/retail-ui.md section 21.7). Pure arithmetic over integer
/// pixels of the game view (retail's <c>RenderDevice</c> viewport; the whole window in this shell).
/// </summary>
public static class VividTargetMath
{
    /// <summary>
    /// The inset from the view edge, 8 pixels: the global <c>min_dist</c> (a float at 0x007C61F4 in the 11.6096 exe, read as 8.0: docs/research/retail-addresses-m2.md section 1.8) and the
    /// literal 8 the on-screen clamps and the off-screen bottom and right edges use.
    /// </summary>
    public const int EdgeInset = 8;

    /// <summary>The number of source images the indicator loads (<c>VividTargetIndicator::VividTargetIndicator</c>, 11.6096 0x004F7720): indices 0 to 12 of the enum group 0x10000009.</summary>
    public const int SourceImageCount = 13;

    /// <summary>The enum group the source images come from.</summary>
    public const uint ImageGroup = 0x10000009;

    /// <summary>
    /// The off-screen arrow source image for a heading in degrees (0 ahead, clockwise): [338,23) 6, [23,68) 7, [68,113) 9, [113,158) 12, [158,203) 11, [203,248) 10, [248,293) 8,
    /// [293,338) 5 (the sector chain at the top of the off-screen branch of <c>OnDraw</c>).
    /// </summary>
    public static int OffScreenImage(double heading)
    {
        if (heading >= 338.0 || heading < 23.0)
        {
            return 6;
        }

        return heading switch
        {
            < 68.0 => 7,
            < 113.0 => 9,
            < 158.0 => 12,
            < 203.0 => 11,
            < 248.0 => 10,
            < 293.0 => 8,
            _ => 5,
        };
    }

    /// <summary>
    /// The on-screen frame (the element holding the four corners) for a target box: grown outward by one corner size, never closer than 8 pixels to the view edge, and forced to span at
    /// least the corners (<c>OnDraw</c> status 1). Returns the frame position and size.
    /// </summary>
    /// <param name="left">The box left, in view pixels.</param>
    /// <param name="top">The box top.</param>
    /// <param name="right">The box right.</param>
    /// <param name="bottom">The box bottom.</param>
    /// <param name="cornerWidth">The first corner width.</param>
    /// <param name="cornerHeight">The first corner height.</param>
    /// <param name="viewWidth">The view width.</param>
    /// <param name="viewHeight">The view height.</param>
    public static (int X, int Y, int Width, int Height) OnScreenFrame(int left, int top, int right, int bottom, int cornerWidth, int cornerHeight, int viewWidth, int viewHeight)
    {
        var maxX = viewWidth - cornerWidth - EdgeInset;
        var maxY = viewHeight - cornerHeight - EdgeInset;
        var x0 = left - cornerWidth;
        var y0 = top - cornerHeight;
        if (x0 > right)
        {
            x0 = right - 1;
        }

        if (y0 > bottom)
        {
            y0 = bottom - 1;
        }

        x0 = Math.Max(x0, EdgeInset);
        y0 = Math.Max(y0, EdgeInset);
        var x1 = Math.Max(right, cornerWidth + EdgeInset);
        var y1 = Math.Max(bottom, cornerHeight + EdgeInset);
        x0 = Math.Min(x0, maxX - cornerWidth);
        y0 = Math.Min(y0, maxY - cornerHeight);
        x1 = Math.Min(x1, maxX);
        y1 = Math.Min(y1, maxY);
        return (x0, y0, cornerWidth + x1 - x0, cornerHeight + y1 - y0);
    }

    /// <summary>The same for a projected rectangle: its edges rounded to the nearest pixel (retail box is a <c>tagRECT</c> of integers).</summary>
    public static (int X, int Y, int Width, int Height) OnScreenFrame(in ScreenRect box, int cornerWidth, int cornerHeight, int viewWidth, int viewHeight) =>
        OnScreenFrame((int)MathF.Round(box.X), (int)MathF.Round(box.Y), (int)MathF.Round(box.Right), (int)MathF.Round(box.Bottom), cornerWidth, cornerHeight, viewWidth, viewHeight);

    /// <summary>
    /// Where the off-screen arrow sits for a heading (degrees, 0 ahead, clockwise): on the view border at the position the heading tangent gives against the half view, then clamped
    /// to 8 pixels inside (<c>OnDraw</c> status 2). Returns the arrow top-left.
    /// </summary>
    public static (int X, int Y) OffScreenPosition(double heading, int width, int height, int viewWidth, int viewHeight)
    {
        const double ToRadians = 0.017453292;
        double x;
        double y;
        var halfW = viewWidth * 0.5;
        var halfH = viewHeight * 0.5;
        if (heading <= 45.0)
        {
            y = EdgeInset;
            x = (Math.Tan(ToRadians * heading) * halfH) + halfW - (width * 0.5);
        }
        else if (heading <= 90.0)
        {
            x = viewWidth - EdgeInset - width;
            y = halfH - (Math.Tan((90.0 - heading) * ToRadians) * halfW) - (height * 0.5);
        }
        else if (heading <= 135.0)
        {
            x = viewWidth - EdgeInset - width;
            y = (Math.Tan((heading - 90.0) * ToRadians) * halfW) + halfH - (height * 0.5);
        }
        else if (heading <= 180.0)
        {
            x = (Math.Tan((180.0 - heading) * ToRadians) * halfH) + halfW - (width * 0.5);
            y = viewHeight - EdgeInset - height;
        }
        else if (heading <= 225.0)
        {
            x = halfW - (Math.Tan((heading - 180.0) * ToRadians) * halfH) - (width * 0.5);
            y = viewHeight - EdgeInset - height;
        }
        else if (heading <= 270.0)
        {
            x = EdgeInset;
            y = (Math.Tan((270.0 - heading) * ToRadians) * halfW) + halfH - (height * 0.5);
        }
        else if (heading <= 315.0)
        {
            x = EdgeInset;
            y = halfH - (Math.Tan((heading - 270.0) * ToRadians) * halfW) - (height * 0.5);
        }
        else
        {
            y = EdgeInset;
            x = halfW - (Math.Tan((360.0 - heading) * ToRadians) * halfH) - (width * 0.5);
        }

        var maxY = viewHeight - EdgeInset - height;
        var maxX = viewWidth - EdgeInset - width;
        return ((int)Math.Clamp(x, EdgeInset, Math.Max(EdgeInset, maxX)), (int)Math.Clamp(y, EdgeInset, Math.Max(EdgeInset, maxY)));
    }

    /// <summary>
    /// The heading, in degrees clockwise from straight ahead (0 up the screen), of the centre of a rectangle that lies outside the view. Retail computes it in the viewer frame
    /// (<c>SmartBox::GetObjectBoundingBox</c>, 11.6096 0x00452E60, the sphere centre heading against the view cone); the shell has only the projected rectangle, so this is the angle of
    /// that rectangle on the screen (<c>TODO(g5b-heading)</c>). A rectangle with no area (the object is behind the camera) has no direction: it points straight back, 180.
    /// </summary>
    public static double HeadingOf(in ScreenRect rect, int viewWidth, int viewHeight)
    {
        if (!(rect.Width > 0f) && !(rect.Height > 0f))
        {
            return 180.0;
        }

        var dx = rect.X + (rect.Width * 0.5) - (viewWidth * 0.5);
        var dy = rect.Y + (rect.Height * 0.5) - (viewHeight * 0.5);
        if (dx == 0.0 && dy == 0.0)
        {
            return 0.0;
        }

        var degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }
}
