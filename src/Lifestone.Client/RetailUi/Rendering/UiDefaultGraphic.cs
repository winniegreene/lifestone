namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// What retail draws for an image that does not load: <c>Graphic::SetToDefaultGraphic</c> (11.4186 0x00693D70, 11.6096
/// 0x00694C90), a 32×32 surface in the UI format filled with RGBA (0.3, 0.3, 0.4, 1), with its right three columns and
/// bottom three rows filled with a second colour. It tiles like any image.
/// </summary>
/// <remarks>
/// The second colour is a global the decompile does not show (<c>::color</c>); Lifestone draws it opaque magenta, a
/// visible placeholder (TODO(retailui-runtime): read the global's value from 11.6096). The fill's bytes are
/// <c>RGBAColor::GetColor32</c> of (0.3f, 0.3f, 0.4f, 1): (76, 76, 102, 255).
/// </remarks>
public static class UiDefaultGraphic
{
    /// <summary>The fill colour as 0xAARRGGBB.</summary>
    public const uint FillArgb = 0xFF4C4C66;

    /// <summary>The border colour Lifestone uses for retail's unknown global (a placeholder).</summary>
    public const uint PlaceholderBorderArgb = 0xFFFF00FF;

    /// <summary>The graphic's size.</summary>
    public const int Size = 32;

    /// <summary>The graphic (shared, immutable).</summary>
    public static UiImage Image { get; } = Build();

    private static UiImage Build()
    {
        var rgba = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var argb = x >= Size - 3 || y >= Size - 3 ? PlaceholderBorderArgb : FillArgb;
                var i = ((y * Size) + x) * 4;
                rgba[i] = (byte)(argb >> 16);
                rgba[i + 1] = (byte)(argb >> 8);
                rgba[i + 2] = (byte)argb;
                rgba[i + 3] = (byte)(argb >> 24);
            }
        }

        return new UiImage(0, Size, Size, UiPixelFormat.Argb, rgba);
    }
}
