using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// <c>--retail-ui-only --screenshot=PATH</c>: with no world, <c>Main</c>'s own screenshot mode has nothing to wait for, so this node
/// waits until the retail UI has settled (every image decoded, nothing left to paint or upload, for <c>--frames</c> frames), saves the
/// window as PNG and quits with 0 (1 when the picture could not be saved). It gives up waiting after <see cref="GiveUpSeconds"/> and
/// captures anyway, logging it. The review-frames runner (<c>--retail-ui-frames</c>, m2-plan U9) supersedes it for the
/// scripted captures.
/// </summary>
public partial class RetailUiOnlyCapture : Node
{
    /// <summary>How long it waits for the UI to settle before capturing what is there.</summary>
    public const double GiveUpSeconds = 30.0;

    private RetailUiLayer _layer = null!;
    private string _path = string.Empty;
    private int _frames;
    private int _settled;
    private double _seconds;
    private bool _done;

    /// <summary>A capture of <paramref name="layer"/>'s window to <paramref name="path"/> after it has been idle for <paramref name="frames"/> frames.</summary>
    public static RetailUiOnlyCapture Create(RetailUiLayer layer, string path, int frames) =>
        new() { Name = "RetailUiOnlyCapture", ProcessPriority = RetailUiLayer.FrameProcessPriority + 1, _layer = layer, _path = path, _frames = Math.Max(1, frames) };

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        _seconds += delta;
        var idle = _layer.Images.PendingCount == 0 && !_layer.Compositor.HasPendingWork && !_layer.Compositor.HasChangesToCopy && _layer.Frames > 2;
        _settled = idle ? _settled + 1 : 0;
        var gaveUp = _seconds > GiveUpSeconds;
        if (_settled <= _frames && !gaveUp)
        {
            return;
        }

        _done = true;
        if (gaveUp)
        {
            Log.Warn("retailui", "the retail UI did not settle; capturing it as it is", ("seconds", _seconds));
        }

        var saved = ScreenshotWriter.Save(GetViewport(), _path, "retailui", ("scale", _layer.Scale), ("surfaces", _layer.Compositor.RootCount));
        GetTree().Quit(saved ? 0 : 1);
    }
}
