using Godot;

namespace Lifestone.Game.Core;

/// <summary>
/// The frame cap (<see cref="Engine.MaxFps"/>) for a vsync mode (wave-3 performance pass, docs/game-core.md §7).
/// </summary>
/// <remarks>
/// <para>Vsync on (the default) and adaptive pace frames themselves and get no cap. With <c>--vsync=off</c> or
/// <c>--vsync=mailbox</c> nothing paces the frames: the game renders 350-2,500 frames per second, spends a whole CPU
/// core and the GPU on frames nobody sees, and is the most exposed to stalls from other processes. Those modes are
/// therefore capped at the screen's refresh rate by default.</para>
/// <para><c>--max-fps=N</c> (after <c>--</c>) sets the cap for any vsync mode, <c>--max-fps=0</c> removes it (uncapped
/// measurements); a malformed or negative value is reported as a bad argument and keeps the default. Godot's own
/// engine argument <c>--max-fps N</c> (before <c>--</c>) is respected when no game argument is given.</para>
/// </remarks>
public static class FramePacing
{
    /// <summary>The cap when the screen's refresh rate is unknown.</summary>
    public const int FallbackRefreshRate = 60;

    /// <summary>The frame cap to apply (0 = none).</summary>
    /// <param name="vsync">The vsync mode in use.</param>
    /// <param name="requested"><c>--max-fps</c>, or null when not given.</param>
    /// <param name="engineMaxFps"><see cref="Engine.MaxFps"/> as the engine started (its own <c>--max-fps</c>).</param>
    /// <param name="refreshRate">The screen's refresh rate in Hz, or a non-positive value when unknown.</param>
    public static int Cap(DisplayServer.VSyncMode vsync, int? requested, int engineMaxFps, double refreshRate)
    {
        if (requested is { } cap)
        {
            return Math.Max(0, cap);
        }

        if (engineMaxFps > 0)
        {
            return engineMaxFps;
        }

        return vsync is DisplayServer.VSyncMode.Disabled or DisplayServer.VSyncMode.Mailbox
            ? (refreshRate > 0 ? (int)Math.Round(refreshRate) : FallbackRefreshRate)
            : 0;
    }

    /// <summary>
    /// Main thread, once per process (GameServices, for every scene; a second call would take the first cap for the
    /// engine's own): applies the vsync mode and its cap, and logs both.
    /// </summary>
    public static void Apply(DisplayServer.VSyncMode vsync, int? requested)
    {
        DisplayServer.WindowSetVsyncMode(vsync);
        var refresh = DisplayServer.ScreenGetRefreshRate();
        Engine.MaxFps = Cap(vsync, requested, Engine.MaxFps, refresh);
        Log.Info("core", "frame pacing", ("vsync", vsync), ("max_fps", Engine.MaxFps == 0 ? "uncapped" : Engine.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("refresh_hz", refresh));
    }
}
