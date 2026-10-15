namespace Lifestone.Client.PortalSpace;

/// <summary>
/// When the hold in the tunnel (state 4) may end (m2-log-in.md §4.5): after <see cref="MinHoldSeconds"/>, on a spin
/// frame the window admits, or at <see cref="MaxHoldSeconds"/> regardless. Retail's window is frames 68 to 76 of the
/// 1-to-119 loop (D27, settled by the 11.6096 machine code: docs/research/retail-addresses-m2.md §1.1).
/// </summary>
/// <remarks>
/// <para>Re-expresses the state-4 test of <c>gmSmartBoxUI::UseTime</c> (11.4186 0x004D6E30, 11.6096 0x004D7A90), by
/// function name. The time left in the loop is (120 − frame) / 40, with the subtraction done on the unsigned frame
/// number (<c>CPhysicsObj::get_curr_frame_number</c>) and the quotient stored as a float; it must lie strictly between
/// <see cref="LowerBound"/> and <see cref="UpperBound"/>, which retail computes in double from the float constants 0.1
/// and 0.3 (the x87 runs at 53 bits: <c>ClientMain::ConfigureFPU</c>). The float rounding admits frame 76
/// (float(1.1) = 1.10000002) and frame 68 (float(1.3) = 1.29999995), so the window is nine frames, 68 to 76, about
/// 1.675 s to 1.9 s into each 2.975 s loop. Exact arithmetic ("strictly between 1.1 and 1.3 s") would give 69 to 75.</para>
/// <para>This class is the one place the window is decided: <see cref="Admits"/> is what the machine calls, and
/// <see cref="FirstFrame"/> and <see cref="LastFrame"/> name its result (a test pins them against the arithmetic).</para>
/// </remarks>
public static class ExitWindow
{
    /// <summary>The least time in state 4 before the window is consulted (retail's double 2.0).</summary>
    public const double MinHoldSeconds = 2.0;

    /// <summary>The time in state 4 after which the hold ends regardless of the frame (retail's double 5.0).</summary>
    public const double MaxHoldSeconds = 5.0;

    /// <summary>Retail's frame count in the time-left formula (an immediate 120).</summary>
    public const uint LoopFrames = 120;

    /// <summary>Retail's frame rate in the time-left formula (the float 40.0 the spin also uses).</summary>
    public const double FramesPerSecond = PortalSpaceScene.SpinFramerate;

    /// <summary>The window's lower bound, exclusive: 1.0 + (double)0.1f = 1.1000000014901161.</summary>
    public const double LowerBound = 1.0 + (double)0.1f;

    /// <summary>The window's upper bound, exclusive: 1.0 + (double)0.3f = 1.3000000119209290.</summary>
    public const double UpperBound = 1.0 + (double)0.3f;

    /// <summary>The first frame <see cref="Admits"/> accepts.</summary>
    public const int FirstFrame = 68;

    /// <summary>The last frame <see cref="Admits"/> accepts.</summary>
    public const int LastFrame = 76;

    /// <summary>Retail's time left in the loop at <paramref name="frame"/>: (120 − frame) / 40 on the unsigned frame, as a float.</summary>
    public static float TimeLeftInLoop(int frame) => (float)(unchecked(LoopFrames - (uint)frame) / FramesPerSecond);

    /// <summary>True when the hold may end on <paramref name="frame"/> (once <see cref="MinHoldSeconds"/> have passed).</summary>
    public static bool Admits(int frame)
    {
        double timeLeft = TimeLeftInLoop(frame);
        return LowerBound < timeLeft && UpperBound > timeLeft;
    }

    /// <summary>
    /// The whole state-4 test: true when the hold ends after <paramref name="elapsed"/> seconds in state 4 with the spin on
    /// <paramref name="frame"/>.
    /// </summary>
    public static bool HoldEnds(double elapsed, int frame) =>
        elapsed >= MinHoldSeconds && (elapsed >= MaxHoldSeconds || Admits(frame));
}
