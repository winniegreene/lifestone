namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// A UI surface: RGBA8 pixels (R, G, B, A bytes, rows top to bottom, no padding), retail's <c>UISurface</c> in the UI
/// format A8R8G8B8. A root's surface starts fully transparent (0,0,0,0).
/// </summary>
public sealed class UiSurface
{
    /// <summary>A transparent surface of <paramref name="width"/>×<paramref name="height"/> pixels.</summary>
    public UiSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>The pixels, <c>Width * 4</c> bytes per row.</summary>
    public byte[] Pixels { get; private set; }

    /// <summary>Bytes per row.</summary>
    public int Stride => Width * 4;

    /// <summary>Resizes to <paramref name="width"/>×<paramref name="height"/>, cleared to transparent (the buffer is reused when big enough).</summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var bytes = width * height * 4;
        if (Pixels.Length < bytes)
        {
            Pixels = new byte[bytes];
        }
        else
        {
            Array.Clear(Pixels, 0, bytes);
        }

        Width = width;
        Height = height;
    }

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) as 0xAARRGGBB (tests and diagnostics).</summary>
    public uint GetArgb(int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return ((uint)Pixels[i + 3] << 24) | ((uint)Pixels[i] << 16) | ((uint)Pixels[i + 1] << 8) | Pixels[i + 2];
    }

    /// <summary>Sets the pixel at (<paramref name="x"/>, <paramref name="y"/>) from 0xAARRGGBB (tests and diagnostics).</summary>
    public void SetArgb(int x, int y, uint argb)
    {
        var i = ((y * Width) + x) * 4;
        Pixels[i] = (byte)(argb >> 16);
        Pixels[i + 1] = (byte)(argb >> 8);
        Pixels[i + 2] = (byte)argb;
        Pixels[i + 3] = (byte)(argb >> 24);
    }
}
