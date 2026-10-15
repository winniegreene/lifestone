using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// What the compositor sees of one UI region (retail's <c>UIRegion</c>): where it is, whether it shows, its image and
/// its children in draw order, and how it draws itself. <see cref="Core.UiElement"/> implements it; tests use synthetic
/// views. Read on the thread that composites, between element updates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coordinates.</b> <see cref="Box"/> is relative to the parent, as retail's <c>m_box</c>; the compositor adds the
/// parents' offsets to get surface coordinates. Every box is inclusive (<see cref="UiBox"/>).
/// </para>
/// <para>
/// <b>Draw order</b> is <see cref="ChildrenInDrawOrder"/>: first drawn is furthest back. Retail keeps children sorted by
/// z level, highest first, then read order, lowest first (<c>UIRegion::AddChild</c> with <c>UIElement::CompareZLevel</c>),
/// and draws them in that order (<c>UIRegion::DrawChildren</c>), so a lower z level is in front (docs/retail-ui.md §3).
/// </para>
/// </remarks>
public interface IUiRegionView
{
    /// <summary>The element's id (for logs and the viewport host).</summary>
    uint ElementId { get; }

    /// <summary>The region's box relative to its parent (<c>m_box</c>).</summary>
    UiBox Box { get; }

    /// <summary>The region's box in logical screen pixels (the UI's own pixels, before the integer UI scale).</summary>
    UiBox ScreenBox { get; }

    /// <summary>The region's own visibility flag (<c>UIRegion</c>'s flag bit 1); a hidden region draws nothing, children included.</summary>
    bool IsVisible { get; }

    /// <summary>True when the region and every ancestor are visible (a surface is presented only then).</summary>
    bool IsShown { get; }

    /// <summary>The region's image and how it is blitted (<c>m_image</c>, <c>m_eBlitMode</c>).</summary>
    UiImageRef Image { get; }

    /// <summary>The image's opacity, 0-1 (<c>m_alphaBlendMod</c>, property 0x4D).</summary>
    float AlphaBlendMod { get; }

    /// <summary>Where tiling starts within the image (<c>m_ptTilingOffset</c>, properties 0x54/0x55).</summary>
    (int X, int Y) TilingOffset { get; }

    /// <summary>The hit-test mask image's DID, or 0 (<c>m_alphaImage</c>; used for hit testing only, never drawn).</summary>
    uint AlphaImageDid { get; }

    /// <summary>True when the region clears its dirty area to transparent before drawing (property 0x51, <c>UIRegion::EraseSelf</c>).</summary>
    bool ShouldEraseBackground { get; }

    /// <summary>True when the region draws its children before itself (property 0x53; <c>UIRegion::DrawHere</c>).</summary>
    bool DrawAfterChildren { get; }

    /// <summary>
    /// True when the region has a surface of its own (retail's <c>UIObject</c>): every mode root and dialog does. Such a
    /// region is composited into its own surface and presented as a unit; its parent's drawing skips it.
    /// </summary>
    bool OwnsSurface { get; }

    /// <summary>The surface's opacity when presented (the <c>UIObject</c>'s alpha, driven by fade media); only meaningful when <see cref="OwnsSurface"/>.</summary>
    float SurfaceAlpha { get; }

    /// <summary>The children, back to front.</summary>
    ReadOnlySpan<IUiRegionView> ChildrenInDrawOrder { get; }

    /// <summary>
    /// The element id of the 3D viewport this region is (class <c>UIElement_Viewport</c>), or 0. The host draws the 3D view
    /// into the region's screen box; the UI around it is opaque (D40).
    /// </summary>
    uint ViewportElementId { get; }

    /// <summary>
    /// Draws the region itself, not its children (<c>UIRegion::DrawSelf</c> and the class overrides). Retail's draw
    /// passes three things, all in surface coordinates: the region's own box, the clip window (the bounding box of the
    /// dirty pieces), and the dirty pieces (each already clipped to the region).
    /// </summary>
    /// <param name="painter">Where to draw.</param>
    /// <param name="self">The region's box in surface coordinates.</param>
    /// <param name="clip">The bounding box of <paramref name="dirty"/> (text draws into this window).</param>
    /// <param name="dirty">The dirty pieces to repaint, each within the region (images draw piece by piece).</param>
    void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty);

    /// <summary>
    /// What a class draws after its children, over them (the radar's blips: <c>gmRadarUI::DrawChildren</c>, 11.6096 0x004DA380, calls <c>UIRegion::DrawChildren</c> and then draws
    /// the objects and the player's marker straight into the surface). The arguments are <see cref="Draw"/>'s. Most classes draw nothing here.
    /// </summary>
    void DrawOverChildren(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
    }

    /// <summary>
    /// The window <paramref name="child"/> draws into, given its box and the window the parent offers it, both in surface
    /// coordinates (<c>UIRegion::DrawChildren</c> clips each child to the parent's window; <c>UIElement_Meter::DrawChildren</c>,
    /// 11.4186 0x0046FBD0, 11.6096 0x0046FF90, clips its fill child further to the filled part). The default is the
    /// window as offered.
    /// </summary>
    UiBox ClipChild(IUiRegionView child, UiBox childSelf, UiBox childClip) => childClip;
}
