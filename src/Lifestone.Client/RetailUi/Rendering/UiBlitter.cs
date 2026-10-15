namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// Retail's software blitter onto an A8R8G8B8 UI surface, pixel for pixel: <c>SoftwareBlitAndColor</c> (11.4186
/// 0x00660300, 11.6096 0x006612A0) and the blit and colouring loops it dispatches to, as a modern (SSE) machine runs
/// them. Every operation re-expresses one loop; the arithmetic, the rounding and the special cases are retail's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The opacity modifier.</b> A blit's opacity becomes a byte, <c>(int)(opacity × 255.0)</c>; 0 draws nothing. With
/// <c>m</c> that byte, a source alpha <c>a</c> counts as <c>(m + 1) × a / 256</c> unless <c>m</c> is 255.
/// </para>
/// <list type="bullet">
/// <item><b>SrcCopy</b> (the <c>BlitLoopNoScale</c> loops of <c>BlitOp_SrcCopy</c>: &lt;ARGB,ARGB&gt; 11.4186
/// 0x00660420, 11.6096 0x006613C0; &lt;XRGB,ARGB&gt; 0x00660510, 0x00661430; &lt;RGB,ARGB&gt; 0x00660590,
/// 0x00661530): the source replaces the destination; an opaque source writes alpha 255.</item>
/// <item><b>ThreeChannelAlpha</b> (&lt;ARGB,ARGB&gt; 0x006606C0, 0x00661660; &lt;XRGB,ARGB&gt; 0x006609B0, 0x00661950;
/// &lt;RGB,ARGB&gt; 0x00660CB0, 0x00661C50): with <c>k</c> the source alpha plus one, each colour channel becomes
/// <c>d − (d·k &gt;&gt; 8) + (s·k &gt;&gt; 8)</c> (an MMX lane, saturated); at <c>k</c> = 256 the colour is copied. The
/// destination keeps its alpha.</item>
/// <item><b>FourChannelAlpha</b> (the SSE variants: &lt;ARGB,ARGB,1&gt; 0x0066BD70, 0x0066CD10; &lt;XRGB,ARGB,1&gt;
/// 0x0066C480, 0x0066D420; &lt;RGB,ARGB,1&gt; 0x0066CBA0, 0x0066DB40, chosen by <c>IsSSEAvailable</c>, 11.4186
/// 0x00660220, 11.6096 0x006611C0): at <c>k</c> = 256, or over a fully transparent destination, the source is copied
/// (its own alpha, unmodulated); over an opaque destination the three-channel formula applies; otherwise, with
/// <c>A' = k − k·(da + 1) / 256 + da + 1</c>, each colour channel becomes <c>d − (d − s)·k / A'</c> in single precision,
/// rounded to nearest even, and the alpha <c>A' − 1</c>.</item>
/// <item><b>Colouring</b> through an alpha mask (<c>ColoringLoopNoScale_FromA8&lt;BlitOp_FourChannelAlpha&lt;AXXX,ARGB,1&gt;&gt;</c>,
/// 11.4186 0x0066D310, 11.6096 0x0066E2B0): the source is the colour with the mask as its alpha, and the colour's own
/// alpha is the modifier (0 draws nothing, 255 counts as 256); then as FourChannelAlpha.</item>
/// </list>
/// </remarks>
internal static class UiBlitter
{
    /// <summary>
    /// The opacity as the blitter's modifier byte, as <c>SoftwareBlitAndColor</c> forms it: the opacity times 255.0 in
    /// double precision, truncated toward zero to a 64-bit integer, of which the low byte is kept.
    /// </summary>
    public static int Modifier(float opacity) => unchecked((int)((long)(opacity * 255.0) & 0xFF));

    /// <summary>Blits <paramref name="width"/>×<paramref name="height"/> pixels of <paramref name="src"/> at (<paramref name="sx"/>, <paramref name="sy"/>) onto <paramref name="dst"/> at (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public static void Blit(UiImage src, int sx, int sy, UiSurface dst, int dx, int dy, int width, int height, UiBlitMode mode, float opacity)
    {
        var modifier = Modifier(opacity);
        if (modifier == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var opaque = src.Format != UiPixelFormat.Argb;
        var sp = src.Pixels;
        var dp = dst.Pixels;
        var am = modifier + 1;
        for (var row = 0; row < height; row++)
        {
            var si = (((sy + row) * src.Width) + sx) * 4;
            var di = (((dy + row) * dst.Width) + dx) * 4;
            for (var col = 0; col < width; col++, si += 4, di += 4)
            {
                var sr = sp[si];
                var sg = sp[si + 1];
                var sb = sp[si + 2];
                var sa = opaque ? 255 : sp[si + 3];
                switch (mode)
                {
                    case UiBlitMode.SrcCopy:
                        dp[di] = sr;
                        dp[di + 1] = sg;
                        dp[di + 2] = sb;
                        dp[di + 3] = (byte)sa;
                        break;

                    case UiBlitMode.ThreeChannelAlpha:
                    {
                        var a = am != 256 ? am * sa / 256 : sa;
                        if (a != 0)
                        {
                            ThreeChannel(dp, di, sr, sg, sb, a + 1);
                        }

                        break;
                    }

                    default:
                    {
                        var a = am != 256 ? am * sa / 256 : sa;
                        if (a != 0)
                        {
                            FourChannel(dp, di, sr, sg, sb, sa, a + 1);
                        }

                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Colours <paramref name="width"/>×<paramref name="height"/> pixels of <paramref name="dst"/> with <paramref name="argb"/>
    /// through the alpha of <paramref name="mask"/> (an A8 font atlas, or the alpha of an ARGB one), FourChannelAlpha.
    /// </summary>
    public static void Colorize(UiImage mask, int sx, int sy, UiSurface dst, int dx, int dy, int width, int height, uint argb)
    {
        var strength = (int)(argb >> 24);
        var modifier = (strength != 0 ? 1 : 0) + strength;
        if (modifier == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var cr = (int)((argb >> 16) & 0xFF);
        var cg = (int)((argb >> 8) & 0xFF);
        var cb = (int)(argb & 0xFF);
        var mp = mask.Pixels;
        var dp = dst.Pixels;
        for (var row = 0; row < height; row++)
        {
            var mi = (((sy + row) * mask.Width) + sx) * 4;
            var di = (((dy + row) * dst.Width) + dx) * 4;
            for (var col = 0; col < width; col++, mi += 4, di += 4)
            {
                int alpha = mp[mi + 3];
                var a = modifier != 256 ? modifier * alpha / 256 : alpha;
                if (a != 0)
                {
                    FourChannel(dp, di, cr, cg, cb, alpha, a + 1);
                }
            }
        }
    }

    /// <summary><c>SurfaceWindow::CopyAlpha</c>: the destination's alpha becomes the source's alpha.</summary>
    public static void CopyAlpha(UiImage src, int sx, int sy, UiSurface dst, int dx, int dy, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var si = (((sy + row) * src.Width) + sx) * 4;
            var di = (((dy + row) * dst.Width) + dx) * 4;
            for (var col = 0; col < width; col++, si += 4, di += 4)
            {
                dst.Pixels[di + 3] = src.Format == UiPixelFormat.Argb || src.Format == UiPixelFormat.A8 ? src.Pixels[si + 3] : (byte)255;
            }
        }
    }

    /// <summary>The three-channel blend of one pixel with <paramref name="k"/> = alpha + 1 (1-256); the destination keeps its alpha.</summary>
    private static void ThreeChannel(byte[] dp, int di, int sr, int sg, int sb, int k)
    {
        if (k == 256)
        {
            dp[di] = (byte)sr;
            dp[di + 1] = (byte)sg;
            dp[di + 2] = (byte)sb;
            return;
        }

        dp[di] = Lane(dp[di], sr, k);
        dp[di + 1] = Lane(dp[di + 1], sg, k);
        dp[di + 2] = Lane(dp[di + 2], sb, k);
    }

    /// <summary>The four-channel blend of one pixel: source colour, source alpha as given (written when copied) and <paramref name="k"/>.</summary>
    private static void FourChannel(byte[] dp, int di, int sr, int sg, int sb, int sa, int k)
    {
        if (k == 256)
        {
            Write(dp, di, sr, sg, sb, sa);
            return;
        }

        int da = dp[di + 3];
        if (da == 0)
        {
            Write(dp, di, sr, sg, sb, sa);
            return;
        }

        if (da == 255)
        {
            dp[di] = Lane(dp[di], sr, k);
            dp[di + 1] = Lane(dp[di + 1], sg, k);
            dp[di + 2] = Lane(dp[di + 2], sb, k);
            return;
        }

        var outAlpha = k - (k * (da + 1) / 256) + da + 1;
        float kf = k;
        float af = outAlpha;
        dp[di] = FloatLane(dp[di], sr, kf, af);
        dp[di + 1] = FloatLane(dp[di + 1], sg, kf, af);
        dp[di + 2] = FloatLane(dp[di + 2], sb, kf, af);
        dp[di + 3] = (byte)(outAlpha - 1);
    }

    /// <summary>One MMX lane: <c>d − ((d·k &gt;&gt; 8) − (s·k &gt;&gt; 8))</c>, saturated to a byte.</summary>
    private static byte Lane(int d, int s, int k)
    {
        var value = d - (((d * k) >> 8) - ((s * k) >> 8));
        return (byte)Math.Clamp(value, 0, 255);
    }

    /// <summary>One SSE lane: <c>d − (d − s)·k / A'</c> in single precision, converted round-to-nearest-even and saturated.</summary>
    private static byte FloatLane(int d, int s, float k, float a)
    {
        float df = d;
        var product = (df - s) * k;
        var quotient = product / a;
        var value = df - quotient;
        var rounded = (int)MathF.Round(value, MidpointRounding.ToEven);
        return (byte)Math.Clamp(rounded, 0, 255);
    }

    private static void Write(byte[] dp, int di, int r, int g, int b, int a)
    {
        dp[di] = (byte)r;
        dp[di + 1] = (byte)g;
        dp[di + 2] = (byte)b;
        dp[di + 3] = (byte)a;
    }
}
