using System.Numerics;
using DatReaderWriter.Types;

namespace Lifestone.Data.World.Sky;

/// <summary>
/// A retail 32-bit colour (<c>RGBAUnion</c>), stored as 0xAARRGGBB exactly as the dat stores it.
/// </summary>
/// <remarks>
/// Retail lerps these per byte with truncation (see <see cref="Lerp"/>) and converts them to floats with
/// <c>RGBColor::SetColor32</c> (acclient.c:142984, 11.4186): <c>channel * 0.0039215689f</c> (the float nearest 1/255).
/// </remarks>
/// <param name="Argb">Packed colour, 0xAARRGGBB.</param>
public readonly record struct AcColor(uint Argb)
{
    /// <summary>The float constant retail multiplies a byte channel by (<c>RGBColor::SetColor32</c>).</summary>
    public const float ByteToUnit = 0.0039215689f;

    /// <summary>Opaque white, retail's fallback colour when a day group has no keyframes.</summary>
    public static AcColor White => new(0xFFFFFFFFu);

    public byte A => (byte)(Argb >> 24);

    public byte R => (byte)(Argb >> 16);

    public byte G => (byte)(Argb >> 8);

    public byte B => (byte)Argb;

    public static AcColor FromBytes(byte a, byte r, byte g, byte b) =>
        new(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    public static AcColor FromDat(ColorARGB? color) =>
        color is null ? White : FromBytes(color.Alpha, color.Red, color.Green, color.Blue);

    /// <summary>RGB as 0..1 floats, exactly as <c>RGBColor::SetColor32</c> computes them (alpha is ignored there).</summary>
    public Vector3 ToVector3() => new(ToUnit(R), ToUnit(G), ToUnit(B));

    /// <summary>
    /// Retail per-byte colour lerp used by <c>SkyDesc::GetLighting</c> and <c>SkyDesc::GetWorldFog</c>
    /// (CSkyDesc.c, acclient.c:301484 and 301601): for R, G and B,
    /// <c>(byte)(uint64)((double)to - (double)from) * ratio + (double)from)</c>, i.e. truncation, not rounding.
    /// Alpha is forced to 0xFF.
    /// </summary>
    public static AcColor Lerp(AcColor from, AcColor to, float ratio) =>
        FromBytes(0xFF, LerpByte(from.R, to.R, ratio), LerpByte(from.G, to.G, ratio), LerpByte(from.B, to.B, ratio));

    /// <summary>One channel of <see cref="Lerp"/>. The <c>(unsigned __int64)</c> cast truncates toward zero and the
    /// store keeps the low byte, so an extrapolated ratio wraps exactly as retail does.</summary>
    public static byte LerpByte(byte from, byte to, float ratio) =>
        unchecked((byte)(long)((((double)to - from) * ratio) + from));

    public override string ToString() => $"#{Argb:X8}";

    private static float ToUnit(byte channel) => (float)(channel * (double)ByteToUnit);
}
