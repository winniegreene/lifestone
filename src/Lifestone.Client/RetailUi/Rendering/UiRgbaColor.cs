namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// Retail's <c>RGBAColor</c>: four float channels, 0-1. The UI keeps colours this way between the dat and the surface
/// (text, outline and caret colours): <see cref="FromColor32"/> multiplies each byte by the float nearest 1/255, which is
/// a little above it, and <see cref="ToColor32"/> truncates, so every byte comes back unchanged (UiRgbaColorTests checks
/// all 256). Colours made from floats in code do not always: retail's default graphic's 0.3 becomes 76.
/// </summary>
/// <param name="R">Red, 0-1.</param>
/// <param name="G">Green, 0-1.</param>
/// <param name="B">Blue, 0-1.</param>
/// <param name="A">Alpha, 0-1.</param>
public readonly record struct UiRgbaColor(float R, float G, float B, float A)
{
    /// <summary>Retail's single-precision 1/255 (0x3B808081).</summary>
    private const float OneOver255 = 0.0039215689f;

    /// <summary>Opaque white (<c>RGBAColor_White</c>): exactly 1, so it converts to 0xFFFFFFFF.</summary>
    public static UiRgbaColor White => new(1f, 1f, 1f, 1f);

    /// <summary>Opaque black (<c>RGBAColor_Black</c>).</summary>
    public static UiRgbaColor Black => new(0f, 0f, 0f, 1f);

    /// <summary>
    /// <c>RGBAColor::SetColor32</c> (11.4186 0x00430B30, 11.6096 0x00430CD0): each byte of 0xAARRGGBB times the float
    /// 1/255, the product rounded to a float.
    /// </summary>
    public static UiRgbaColor FromColor32(uint argb) => new(
        (float)(((argb >> 16) & 0xFF) * (double)OneOver255),
        (float)(((argb >> 8) & 0xFF) * (double)OneOver255),
        (float)((argb & 0xFF) * (double)OneOver255),
        (float)((argb >> 24) * (double)OneOver255));

    /// <summary>
    /// <c>RGBAColor::GetColor32</c> (11.4186 0x00423D00, 11.6096 0x00424000): each channel times 255 in double precision,
    /// truncated (out-of-range channels wrap as the 64-bit conversion's low byte, as retail).
    /// </summary>
    public uint ToColor32() =>
        (Channel(A) << 24) | (Channel(R) << 16) | (Channel(G) << 8) | Channel(B);

    private static uint Channel(float value) => unchecked((uint)(long)(value * 255.0) & 0xFF);
}
