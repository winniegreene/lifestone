using Godot;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// Draws one canvas item of the kind the arrival's first frames need and the login screens never drew, so the render server compiles its pipeline at start-up and not in
/// the frame the portal tunnel comes up (DoD 15: no frame over 50 ms between Enter and walking; docs/session.md section 27, <c>TODO(arrival-pipelines)</c>).
/// </summary>
/// <remarks>
/// <para>The pipeline is the one a rounded-corner <see cref="StyleBoxFlat"/> needs (found by drawing candidates one by one while the stand-in login ran: with this item drawn
/// first, the tunnel-start compile is gone; the notice panel, the tunnel screen's texture rect, the labels and the shell's tiles do not account for it). Which item of the
/// tunnel's frame asks for the same variant is not known (<c>TODO(arrival-canvas-source)</c>); the compile counters of <c>--play-checks</c> and <c>--login-check</c> say
/// whether the arrival still compiles one.</para>
/// <para>The item is 4 x 4 pixels at the window's corner on a canvas layer below every other, nearly transparent, and goes after <see cref="Frames"/> frames: the login screens' backdrop
/// covers it anyway.</para>
/// </remarks>
public static class CanvasWarmUp
{
    /// <summary>Frames the item lives (the engine builds the pipeline on the first draw).</summary>
    public const int Frames = 3;

    /// <summary>The canvas layer: below the tunnel's (-1) and every interface layer.</summary>
    public const int Layer = -100;

    /// <summary>Adds the item under <paramref name="parent"/> and frees it after <see cref="Frames"/> frames.</summary>
    public static void Run(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var layer = new CanvasLayer { Name = "CanvasWarmUp", Layer = Layer };
        var panel = new Panel { Name = "RoundedPanel", MouseFilter = Control.MouseFilterEnum.Ignore, Position = Vector2.Zero, Size = new Vector2(4f, 4f) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.02f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        layer.AddChild(panel);
        parent.AddChild(layer);

        var tree = parent.GetTree();
        var frames = 0;
        tree.ProcessFrame += Free;
        Log.Info("play", "canvas warm-up drawn (the rounded panel's pipeline)", ("frames", Frames));

        void Free()
        {
            if (++frames < Frames)
            {
                return;
            }

            tree.ProcessFrame -= Free;
            Log.Debug("play", "canvas warm-up done", ("pipelines_since_start", PipelineCompilations.Read().Describe()));
            layer.QueueFree();
        }
    }
}
