using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Radar;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: gmRadarUI::DrawChildren 0x004D9720.
/// <summary>Draws the radar's blips and the player's marker into <paramref name="painter"/> (what <c>gmRadarUI::DrawObjects</c> and the marker code of <c>DrawChildren</c> do).</summary>
/// <param name="painter">Where to draw.</param>
/// <param name="self">The radar's box in surface coordinates.</param>
/// <param name="clip">The bounding box of <paramref name="dirty"/>.</param>
/// <param name="dirty">The dirty pieces being repainted.</param>
public delegate void RadarDrawHandler(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty);

/// <summary>
/// <c>gmRadarUI</c>'s element (class 0x10000010; the radar window 0x100006D2): a plain element whose children (the ring art, the compass tokens, the buttons) the compositor draws, and
/// that then draws the blips over them (<c>gmRadarUI::DrawChildren</c>, 11.6096 0x004DA380: <c>UIRegion::DrawChildren</c>, then <c>DrawObjects</c>, then the marker).
/// <see cref="GameRadar"/> owns the behaviour and sets <see cref="Painter"/>.
/// </summary>
public sealed class UiRadarElement : UiElement
{
    /// <summary>The radar element of <paramref name="init"/>.</summary>
    public UiRadarElement(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>What draws over the children; null until a <see cref="GameRadar"/> binds the element.</summary>
    public RadarDrawHandler? Painter { get; set; }

    /// <inheritdoc/>
    public override void DrawOverChildren(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty) => Painter?.Invoke(painter, self, clip, dirty);
}
