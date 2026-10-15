namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Retail's easing for UI animations (m2-log-in.md §4.6): a 100-entry table of levels 0 to 1024 that rises like
/// (1 − cos πt) / 2, in steps. Portal space eases its zoom and the tunnel roll with it; later UI animations reuse it.
/// </summary>
/// <remarks>
/// <para><b>Construction</b> re-expresses <c>UIGlobals::Init</c> (11.4186 0x004EE470, 11.6096 0x004EF100) by function
/// name, in integers as retail builds it:</para>
/// <list type="bullet">
/// <item>raw[i] is the sine of i · 3.141592 · 0.0101010101010101 (both retail's literals; the second is retail's
/// stand-in for 1/99, multiplied, not divided), times 1024, truncated to an unsigned 16-bit value;</item>
/// <item>the raw values are summed in a signed 32-bit integer (64,478);</item>
/// <item>level[i] is (running sum through i, shifted left by 10) divided by the total, in signed 32-bit integer
/// division.</item>
/// </list>
/// <para>The table does not depend on the x87 precision retail ran with: the raw products sit at least 0.005 from an
/// integer, far beyond single-precision error, so double, single and exact arithmetic give the same 100 entries (the
/// tests derive it independently in decimal arithmetic).</para>
/// <para><b>Lookup</b> re-expresses <c>UIGlobals::GetAnimLevel</c> (11.4186 0x004EE540, 11.6096 0x004EF1D0): the float
/// percentage is widened, clamped to [0, 1], and indexes the table at ⌊99 · p⌋ (truncation).</para>
/// <para>Static, immutable and allocation-free; safe from any thread.</para>
/// </remarks>
public static class AnimLevel
{
    /// <summary>Entries in the table (retail allocates 0xC8 bytes of 16-bit levels).</summary>
    public const int Count = 100;

    /// <summary>The last level: a finished animation.</summary>
    public const int Full = 1024;

    /// <summary>One level as a fraction, the 1/1024 retail multiplies levels by (<c>0.0009765625</c>).</summary>
    public const double Unit = 0.0009765625;

    /// <summary>Retail's π in <c>UIGlobals::Init</c>, six decimals.</summary>
    internal const double RetailPi = 3.141592;

    /// <summary>Retail's step factor in <c>UIGlobals::Init</c>: about 1/99, as a literal.</summary>
    internal const double RetailStep = 0.0101010101010101;

    /// <summary>The highest index (the table spans ⌊99 · p⌋ for p in [0, 1]).</summary>
    private const double LastIndex = 99.0;

    private static readonly ushort[] s_raw = BuildRaw();
    private static readonly ushort[] s_levels = BuildLevels(s_raw);

    /// <summary>The 100 levels, 0 at index 0 and 1024 at indices 98 and 99, never decreasing.</summary>
    public static ReadOnlySpan<ushort> Table => s_levels;

    /// <summary>The raw sine values the levels accumulate (retail's first pass), for the tests.</summary>
    internal static ReadOnlySpan<ushort> RawSine => s_raw;

    /// <summary>
    /// The level for <paramref name="percentage"/>, 0 to 1024 (<c>UIGlobals::GetAnimLevel</c>). Below 0 gives 0 and
    /// above 1 gives 1024. NaN gives 0 (retail would index outside its table).
    /// </summary>
    public static int Level(float percentage)
    {
        double p = percentage;
        if (p < 0.0 || double.IsNaN(p))
        {
            p = 0.0;
        }

        if (p > 1.0)
        {
            p = 1.0;
        }

        return s_levels[(int)(p * LastIndex)];
    }

    /// <summary><see cref="Level"/> as a fraction of 1: level × <see cref="Unit"/>, so 0.0 to 1.0.</summary>
    public static double Fraction(float percentage) => Level(percentage) * Unit;

    private static ushort[] BuildRaw()
    {
        var raw = new ushort[Count];
        for (var i = 0; i < Count; i++)
        {
            // Every value is in [0, 1024]: 3.141592 is just below π, so the last sine is a tiny positive number.
            raw[i] = (ushort)(ulong)(Math.Sin(i * RetailPi * RetailStep) * 1024.0);
        }

        return raw;
    }

    private static ushort[] BuildLevels(ushort[] raw)
    {
        var total = 0;
        foreach (var value in raw)
        {
            total += value;
        }

        var levels = new ushort[Count];
        var running = 0;
        for (var i = 0; i < Count; i++)
        {
            running += raw[i];
            levels[i] = (ushort)((running << 10) / total);
        }

        return levels;
    }
}
