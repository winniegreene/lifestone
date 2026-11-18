using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>
/// <c>--video-chat</c> (video capture only, off by default; M6's hero video): once the window layout is read, the main chat window is <see cref="HeightOfWindow"/> of the window's height (400 at 1080, the
/// height the owner chose from the review frames of 2026-11-18; four times the layout's own 100) with its bottom edge kept, and as wide as it can be short of the player. The chase camera draws her at the middle of the window and her body's half-width scales with the window's height (a fixed vertical field of view), so the right
/// edge is half the window's width less <see cref="ClearanceOfHeight"/> of its height: 755 at 1920x1080, where her shoulder pads start near 790 in the default view (review frames of the live game,
/// 2026-11-18). Lifestone's own presentation for videos, not a retail behaviour. Set once at start-up by the main scene.
/// </summary>
public static class VideoChatPlace
{
    /// <summary>The share of the window's height kept clear between the chat's right edge and the middle of the window.</summary>
    public const double ClearanceOfHeight = 0.19;

    /// <summary>The chat's height as a share of the window's height: 400 at 1080.</summary>
    public const double HeightOfWindow = 0.37;

    /// <summary>The narrowest the chat is made, so a small window still has a usable chat.</summary>
    public const int MinWidth = 200;

    /// <summary>True when the run asked for it (<c>--video-chat</c>).</summary>
    public static bool Enabled { get; set; }

    /// <summary>The chat's new box: <see cref="HeightOfWindow"/> of the window's height (never shorter than the layout's) with the bottom kept (never above the window's top), the right edge short of the player, at least <see cref="MinWidth"/> wide.</summary>
    /// <param name="rootWidth">The window's width in UI pixels.</param>
    /// <param name="rootHeight">The window's height in UI pixels.</param>
    /// <param name="x">The chat's left edge.</param>
    /// <param name="y">The chat's top edge.</param>
    /// <param name="width">The chat's width.</param>
    /// <param name="height">The chat's height.</param>
    public static (int X, int Y, int Width, int Height) Target(int rootWidth, int rootHeight, int x, int y, int width, int height)
    {
        var bottom = y + height;
        var newHeight = Math.Min(Math.Max(height, (int)Math.Round(rootHeight * HeightOfWindow)), bottom);
        var right = (rootWidth / 2) - (int)Math.Round(rootHeight * ClearanceOfHeight);
        var newWidth = Math.Max(MinWidth, right - x);
        return (x, bottom - newHeight, newWidth, newHeight);
    }

    /// <summary>
    /// Resizes, then moves, the main chat when <see cref="Enabled"/>, from <paramref name="layoutBox"/>: the chat's place in the gameplay layout itself, read before any saved layout. A saved layout file keeps
    /// the doubled chat at log-off, so working from the box the file restored would double it again at each login (seen 2026-11-18: 200, 400, 800, then the window's full height). Logs where it was and where it is.
    /// </summary>
    /// <param name="root">The game screen's root.</param>
    /// <param name="chat">The main chat window, or null (nothing to do).</param>
    /// <param name="layoutBox">The chat's box in the gameplay layout (X, Y, width, height), before any saved layout was read.</param>
    /// <param name="log">The log.</param>
    public static void Apply(UiElement? root, UiElement? chat, (int X, int Y, int Width, int Height)? layoutBox, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!Enabled || root is null || chat is null || layoutBox is not { } layout)
        {
            return;
        }

        var before = chat.Box;
        var target = Target(root.Box.Width, root.Box.Height, layout.X, layout.Y, layout.Width, layout.Height);
        chat.ResizeTo(target.Width, target.Height);
        chat.MoveTo(target.X, target.Y);
        var after = chat.Box;
        log.Info(
            "retailui",
            string.Create(
                CultureInfo.InvariantCulture,
                $"--video-chat: the main chat went from ({before.X0}, {before.Y0}) {before.Width}x{before.Height} (the layout's own place: ({layout.X}, {layout.Y}) {layout.Width}x{layout.Height}) to ({after.X0}, {after.Y0}) {after.Width}x{after.Height} in a {root.Box.Width}x{root.Box.Height} window (asked {target.Width}x{target.Height})"));
    }
}
