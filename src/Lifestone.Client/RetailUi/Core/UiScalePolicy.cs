namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// The UI's integer scale for a window: each UI pixel is drawn as <see cref="Scale"/>×<see cref="Scale"/> window pixels
/// (nearest-neighbour), so the UI works in <see cref="LogicalWidth"/>×<see cref="LogicalHeight"/> pixels of its own.
/// </summary>
/// <param name="Scale">Window pixels per UI pixel, 1 or more.</param>
/// <param name="WindowWidth">The window's width in pixels.</param>
/// <param name="WindowHeight">The window's height in pixels.</param>
public readonly record struct UiScale(int Scale, int WindowWidth, int WindowHeight)
{
    /// <summary>The UI's width in its own pixels: <c>floor(WindowWidth / Scale)</c>.</summary>
    public int LogicalWidth => WindowWidth / Scale;

    /// <summary>The UI's height in its own pixels: <c>floor(WindowHeight / Scale)</c>.</summary>
    public int LogicalHeight => WindowHeight / Scale;

    /// <summary>
    /// True when the UI has fewer than retail's 800×600 pixels: the screens are laid out for 800×600 at least, so part of
    /// them is cut off (a labelled placeholder; <see cref="UiElementManager.SetScale"/> logs it).
    /// </summary>
    public bool BelowMinimum => LogicalWidth < UiScalePolicy.MinimumWidth || LogicalHeight < UiScalePolicy.MinimumHeight;

    /// <summary>For logs: <c>s=2 window=2560x1440 ui=1280x720</c>.</summary>
    public override string ToString() => $"s={Scale} window={WindowWidth}x{WindowHeight} ui={LogicalWidth}x{LogicalHeight}";
}

/// <summary>
/// Lifestone's UI scale rule (m2-log-in.md §5.4, decision D37): the largest integer scale that still leaves at least
/// 800×600 UI pixels, drawn nearest-neighbour. Retail drew the UI pixel for pixel (scale 1), which <c>--ui-scale=1</c>
/// restores; any other override is honoured as given.
/// </summary>
/// <remarks>
/// 800×600, 1280×720 and 1920×1080 give 1; 1600×1200 and 2560×1440 give 2 (1280×720 UI pixels); 3840×2160 gives 3. A
/// window below 800×600 gets scale 1 and <see cref="UiScale.BelowMinimum"/>.
/// </remarks>
public static class UiScalePolicy
{
    /// <summary>Retail's smallest display width: every retail layout is 800 wide.</summary>
    public const int MinimumWidth = 800;

    /// <summary>Retail's smallest display height (three layouts are 500 high; the screens are 600).</summary>
    public const int MinimumHeight = 600;

    /// <summary>The scale for a <paramref name="windowWidth"/>×<paramref name="windowHeight"/> window.</summary>
    /// <param name="windowWidth">The window's width in pixels (1 or more).</param>
    /// <param name="windowHeight">The window's height in pixels (1 or more).</param>
    /// <param name="overrideScale">The <c>--ui-scale</c> value, or null for the rule.</param>
    public static UiScale Choose(int windowWidth, int windowHeight, int? overrideScale = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        if (overrideScale is { } forced)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(forced, nameof(overrideScale));
            return new UiScale(forced, windowWidth, windowHeight);
        }

        var scale = Math.Max(1, Math.Min(windowWidth / MinimumWidth, windowHeight / MinimumHeight));
        return new UiScale(scale, windowWidth, windowHeight);
    }
}
