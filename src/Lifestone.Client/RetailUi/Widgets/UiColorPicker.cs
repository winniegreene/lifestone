using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The colour picker (class 0x10, retail's <c>UIElement_ColorPicker</c>; CharGen's colour swatches): its image is a
/// palette; a click picks the pixel under the mouse, and the picked pixel is outlined.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the constructor (11.4186 0x0046AE30, 11.6096 0x0046B0C0; it hears every mouse move),
/// <c>SetCurrentSelectedColor</c> (11.4186 0x0046AEE0, 11.6096 0x0046B170: the first pixel of the colour, by
/// <c>SurfaceWindow::SearchForColor</c>, 11.4186 0x004417B0, 11.6096 0x00441950), <c>IndexToRGBA</c> (11.4186 0x0046AF90,
/// 11.6096 0x0046B220), <c>HandleMouseClick</c> (11.4186 0x0046AFF0, 11.6096 0x0046B280), <c>DrawSelf</c> (11.4186
/// 0x0046B070, 11.6096 0x0046B320: a one-pixel white box five pixels across, <c>SurfaceWindow::DrawBox</c>, 11.4186
/// 0x004413F0, 11.6096 0x00441590), <c>OnSetAttribute</c> (11.4186 0x0046B190, 11.6096 0x0046B440) and
/// <c>ListenToElementMessage</c> (11.4186 0x0046B230, 11.6096 0x0046B4E0). A pick broadcasts
/// <see cref="UiMessageIds.ColorPicked"/> with the pixel's index (row × width + column).
/// </para>
/// <para>
/// The search compares the colour channels and ignores alpha (the 11.4186 decompile of the comparison is garbled; the
/// colour it builds has no alpha). As retail, the outline is drawn whenever there is a selection, whatever property 0xAE says.
/// </para>
/// </remarks>
public class UiColorPicker : UiElement
{
    private const uint White = 0xFFFFFFFF;

    /// <summary>A colour picker of <paramref name="init"/>: nothing selected, every mouse move heard.</summary>
    public UiColorPicker(in UiElementInit init)
        : base(init)
    {
        WantsMouseMoves = true;
    }

    /// <summary>The picked pixel's index (<c>m_nCurrentSelection</c>), or -1.</summary>
    public int CurrentSelection { get; private set; } = -1;

    /// <summary>The picked colour as 0xAARRGGBB (<c>m_rgbaCurrentSelected</c>).</summary>
    public uint CurrentColor { get; private set; }

    /// <summary>Property 0xAE (<c>m_bDisplaySelection</c>).</summary>
    public bool DisplaySelection { get; private set; }

    /// <summary>
    /// <c>UIElement_ColorPicker::SetCurrentSelectedColor</c>: picks the first pixel of the palette with <paramref name="argb"/>'s
    /// colour. False when the palette is not loaded or has no such pixel.
    /// </summary>
    public bool SetCurrentSelectedColor(uint argb)
    {
        if (!TryGetPalette(out var palette))
        {
            return false;
        }

        CurrentColor = argb;
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        var pixels = palette.Pixels;
        for (var i = 0; i < palette.Width * palette.Height; i++)
        {
            if (pixels[i * 4] == r && pixels[(i * 4) + 1] == g && pixels[(i * 4) + 2] == b)
            {
                CurrentSelection = i;
                BroadcastElementMessage(UiMessageIds.ColorPicked, (uint)i);
                MakeRootDirtyHere();
                return true;
            }
        }

        return false;
    }

    /// <summary><c>UIElement_ColorPicker::IndexToRGBA</c>: the colour of pixel <paramref name="index"/>; false outside the palette.</summary>
    public bool IndexToColor(int index, out uint argb)
    {
        argb = 0;
        if (!TryGetPalette(out var palette) || palette.Width == 0 || index < 0 || index >= palette.Width * palette.Height)
        {
            return false;
        }

        var p = index * 4;
        var pixels = palette.Pixels;
        argb = ((uint)pixels[p + 3] << 24) | ((uint)pixels[p] << 16) | ((uint)pixels[p + 1] << 8) | pixels[p + 2];
        return true;
    }

    /// <inheritdoc/>
    protected override bool GetShouldBeMouseVisible() => base.GetShouldBeMouseVisible() || State != UiStateIds.Ghosted;

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.ColorPickerShowSelection:
                DisplaySelection = GetBool(value);
                break;
            case UiPropertyIds.ColorPickerCurrentSelection:
                // An absent colour counts as opaque black.
                SetCurrentSelectedColor(value is UiColorProperty color ? color.Argb : 0xFF000000);
                break;
        }
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (!ReferenceEquals(message.Element, this) || message.MessageId != UiMessageIds.Click)
        {
            return base.ListenToElementMessage(message);
        }

        // UIElement_ColorPicker::HandleMouseClick: the pixel under the mouse, in the element's own coordinates.
        var (x, y) = message.ElementPoint;
        CurrentSelection = x + (y * Box.Width);
        if (IndexToColor(CurrentSelection, out var argb))
        {
            CurrentColor = argb;
        }

        BroadcastElementMessage(UiMessageIds.ColorPicked, unchecked((uint)CurrentSelection));
        MakeRootDirtyHere();
        return UiListenResult.Consumed;
    }

    /// <summary><c>UIElement_ColorPicker::DrawSelf</c>: the palette, then a white outline around the picked pixel, clipped to the element.</summary>
    public override void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
        base.Draw(painter, self, clip, dirty);
        if (CurrentSelection == -1)
        {
            return;
        }

        var width = self.X1 - self.X0 + 1;
        var row = CurrentSelection / width;
        var column = CurrentSelection % width;
        var x0 = Math.Max(self.X0 + column - 2, self.X0);
        var y0 = Math.Max(self.Y0 + row - 2, self.Y0);
        var x1 = Math.Min(self.X0 + column + 2, self.X1);
        var y1 = Math.Min(self.Y0 + row + 2, self.Y1);
        foreach (var edge in (ReadOnlySpan<UiBox>)[new UiBox(x0, y0, x1, y0), new UiBox(x0, y1, x1, y1), new UiBox(x0, y0, x0, y1), new UiBox(x1, y0, x1, y1)])
        {
            if (UiBoxes.TryIntersect(edge, clip, out var visible))
            {
                painter.Fill(visible, White);
            }
        }
    }

    private bool TryGetPalette(out UiImage palette)
    {
        palette = null!;
        if (!Image.HasImage || Manager.Services.Images?.TryGetImage(Image.Did, out var image) != UiImageLookup.Ready || image is null)
        {
            return false;
        }

        palette = image;
        return true;
    }
}
