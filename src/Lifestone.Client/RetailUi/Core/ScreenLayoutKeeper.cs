using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>One chat window's text-type mask, as the layout keeper reads and restores it.</summary>
/// <param name="ElementId">The chat window's element id (its tag in the file is the one <see cref="ScreenLayoutFile.Windows"/> gives that id).</param>
/// <param name="Get">The window's mask now.</param>
/// <param name="Set">Sets the window's mask.</param>
public sealed record ChatMaskBinding(uint ElementId, Func<ulong> Get, Action<ulong> Set);

/// <summary>What <see cref="ScreenLayoutKeeper.Load"/> found and did.</summary>
/// <param name="Found">True when the store had the file.</param>
/// <param name="WindowsApplied">Windows moved or resized to the file's places.</param>
/// <param name="MasksApplied">Chat masks restored.</param>
/// <param name="Clamped">Windows whose saved place was off the screen and was moved back in.</param>
/// <param name="BadLines">Lines of the file that were not understood.</param>
public readonly record struct ScreenLayoutLoad(bool Found, int WindowsApplied, int MasksApplied, int Clamped, int BadLines);

/// <summary>
/// Writes the in-game shell's window layout and restores it (M5 6 u1, D33; retail's <c>gmGamePlayUI::SaveScreenLayout</c> 0x004EB9E0 and <c>LoadScreenLayout</c> 0x004EB580 over
/// <see cref="ScreenLayoutFile"/>). Saving writes one line per window of <see cref="ScreenLayoutFile.Windows"/> the shell has (its box in the root: place and size) and the chat masks; loading finds
/// each window by its id under the root and resizes it, then moves it, as retail does. The shell calls <see cref="Save"/> at log-off and at the world exit and <see cref="Load"/> once at the world entry,
/// over the layout's own places, so a file that is missing, or lacks a window, leaves the defaults.
/// <para>
/// <b>Clamping.</b> Retail's <c>LoadScreenLayout</c> (read in 11.4186, 0x004EA8F0) applies the saved numbers as they are. The file name carries the window size, so a layout is only read at the size it
/// was saved at; a window can still lie off the screen when the file was edited by hand. Lifestone moves such a window back inside the root (Lifestone's own safeguard, logged, no flag: retail's
/// behaviour would leave a window nobody can drag back).
/// </para>
/// </summary>
public sealed class ScreenLayoutKeeper(IScreenLayoutStore? store, ILogSink log)
{
    /// <summary>The text a save would write now (the windows found under <paramref name="root"/> and the <paramref name="masks"/>).</summary>
    public static string Capture(UiElement root, IReadOnlyList<ChatMaskBinding> masks)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(masks);
        var text = new StringBuilder();
        foreach (var window in ScreenLayoutFile.Windows)
        {
            if (root.GetChildRecursive(window.ElementId) is { } element)
            {
                var box = element.Box;
                text.Append(ScreenLayoutFile.Line(window.Tag, box.X0, box.Y0, box.Width, box.Height)).Append('\n');
            }
        }

        foreach (var mask in masks)
        {
            if (TagOf(mask.ElementId) is { } tag)
            {
                text.Append(ScreenLayoutFile.MaskLine(tag, mask.Get())).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Writes the layout to <paramref name="name"/>. False, with nothing written, when the keeper has no store or the store failed (the failure is logged).</summary>
    public bool Save(string name, UiElement root, IReadOnlyList<ChatMaskBinding> masks)
    {
        if (store is null)
        {
            return false;
        }

        try
        {
            store.Write(name, Capture(root, masks));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // a store's failure (the host's file I/O) is a warning, never a crash of the shell
        {
            log.Warn("retailui", $"the window layout could not be saved to {name}: {ex.Message}");
            return false;
        }

        log.Info("retailui", $"the window layout was saved to {name}");
        return true;
    }

    /// <summary>
    /// Restores the layout from <paramref name="name"/>. A missing file is the first login at this size (an INFO line, the defaults stay); a file with lines that are not understood logs one WARNING with
    /// their count, applies what it can read and leaves the rest at the defaults.
    /// </summary>
    public ScreenLayoutLoad Load(string name, UiElement root, IReadOnlyList<ChatMaskBinding> masks)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(masks);
        string? text;
        try
        {
            text = store?.Read(name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // a store's failure (the host's file I/O) is a warning, never a crash of the shell
        {
            log.Warn("retailui", $"the window layout {name} could not be read, so the windows keep the layout's places: {ex.Message}");
            return default;
        }

        if (text is null)
        {
            log.Info("retailui", $"no saved window layout {name}: the windows keep the layout's places");
            return default;
        }

        var parsed = ScreenLayoutFile.Parse(text);
        var applied = 0;
        var clamped = 0;
        foreach (var window in ScreenLayoutFile.Windows)
        {
            if (!parsed.Windows.TryGetValue(window.Tag, out var rect) || root.GetChildRecursive(window.ElementId) is not { } element)
            {
                continue;
            }

            clamped += Place(root, element, rect);
            applied++;
        }

        var restored = 0;
        foreach (var mask in masks)
        {
            if (TagOf(mask.ElementId) is { } tag && parsed.ChatMasks.TryGetValue(tag, out var value))
            {
                mask.Set(value);
                restored++;
            }
        }

        if (parsed.BadLines > 0)
        {
            log.Warn("retailui", $"the window layout {name} has {parsed.BadLines} line(s) that are not understood; those windows keep the layout's places");
        }

        log.Info("retailui", $"the window layout {name} was loaded: {applied} window(s), {restored} chat mask(s), {clamped} moved back on screen");
        return new ScreenLayoutLoad(true, applied, restored, clamped, parsed.BadLines);
    }

    private static string? TagOf(uint elementId) =>
        ScreenLayoutFile.Windows.FirstOrDefault(w => w.ElementId == elementId) is { Tag: not null } found ? found.Tag : null;

    // LoadScreenLayout resizes, then moves. The size may be clamped by the element's own limits; the place is then kept inside the root.
    private int Place(UiElement root, UiElement element, (int X, int Y, int W, int H) rect)
    {
        if (rect.W > 0 && rect.H > 0 && (rect.W != element.Box.Width || rect.H != element.Box.Height))
        {
            element.ResizeTo(rect.W, rect.H);
        }

        var maxX = Math.Max(0, root.Box.Width - element.Box.Width);
        var maxY = Math.Max(0, root.Box.Height - element.Box.Height);
        var x = Math.Clamp(rect.X, 0, maxX);
        var y = Math.Clamp(rect.Y, 0, maxY);
        element.MoveTo(x, y);
        if (x == rect.X && y == rect.Y)
        {
            return 0;
        }

        log.Info("retailui", $"the saved place of window 0x{element.ElementId:X8} ({rect.X}, {rect.Y}) was off the screen; it is at ({x}, {y})");
        return 1;
    }
}
