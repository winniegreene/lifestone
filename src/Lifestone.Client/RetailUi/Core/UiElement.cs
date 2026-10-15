using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// One live UI element: retail's <c>UIRegion</c> (box, z level, image, children, dirty rectangles) and
/// <c>UIElement</c> (desc, states, properties, media machine) in one class. Every retail class derives from it; a class
/// Lifestone has not implemented yet is this class itself (<see cref="IsStandIn"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle</b> (<see cref="UiElementManager"/>, re-expressing <c>UIElementManager::CreateElementRecursiveFromFullDesc</c>,
/// 11.4186 0x0045B0C0, 11.6096 0x0045B1A0): the constructor (<c>UIElement::UIElement</c>, 11.4186 0x004647D0, 11.6096
/// 0x00464900) places the element at its desc's box and z level; its children are created, initialized and added; then
/// the element is initialized (<see cref="Initialize"/>): its media machine starts on the desc's media, it enters its
/// default state, and every property of its desc goes through <see cref="OnSetAttribute"/>.
/// </para>
/// <para>
/// <b>Threading.</b> One thread (the host's main thread) creates, updates and draws elements.
/// </para>
/// </remarks>
public partial class UiElement : IUiRegionView
{
    private readonly List<UiElement> _children = [];
    private IUiRegionView[] _childViews = [];
    private UiBox _box;
    private int _zLevel;
    private bool _visible = true;
    private UiImageRef _image;
    private UiImage? _generatedImage;
    private float _alphaBlendMod = 1f;
    private (int X, int Y) _tilingOffset;
    private uint _alphaImageDid;
    private bool _shouldEraseBackground;
    private bool _drawAfterChildren;
    private bool _ownsSurface;
    private float _surfaceAlpha = 1f;

    /// <summary>
    /// Creates the element of <paramref name="init"/>'s desc at the desc's position, size and z level
    /// (<c>UIElement::UIElement</c>). Nothing else happens until <see cref="Initialize"/>.
    /// </summary>
    public UiElement(in UiElementInit init)
    {
        ArgumentNullException.ThrowIfNull(init.Manager);
        ArgumentNullException.ThrowIfNull(init.Layout);
        ArgumentNullException.ThrowIfNull(init.Desc);
        Manager = init.Manager;
        Layout = init.Layout;
        Desc = init.Desc;
        Media = new UiMediaMachine(this);

        // UIRegion::UIRegion makes an empty box at (0,0); the element constructor moves and sizes it from the desc.
        _box = UiBox.FromPositionAndSize(0, 0, 0, 0);
        MoveRegion(Desc.BaseState.X, Desc.BaseState.Y);
        ResizeCore(Desc.BaseState.Width, Desc.BaseState.Height, notifyChildren: false);
        _zLevel = unchecked((int)Desc.BaseState.ZLevel);
    }

    /// <summary>The manager that created the element.</summary>
    public UiElementManager Manager { get; }

    /// <summary>The layout the element was created from (<c>m_layout</c>); a root anchors from its design size.</summary>
    public UiLayout Layout { get; }

    /// <summary>The element's full desc (<c>m_desc</c>).</summary>
    public UiElementDesc Desc { get; }

    /// <summary>The element's id (<c>m_elementID</c>).</summary>
    public uint ElementId => Desc.ElementId;

    /// <summary>The element's UI class (<c>m_type</c>).</summary>
    public uint ClassId => Desc.Type;

    /// <summary>
    /// True when the element stands in for a retail class Lifestone has no runtime for yet: it lays out, takes states and
    /// draws its media, but has none of the class's behaviour (a labelled placeholder; <see cref="UiClassRegistry"/>).
    /// </summary>
    public bool IsStandIn { get; init; }

    /// <summary>The element's media machine (<c>m_mediaMachine</c>).</summary>
    public UiMediaMachine Media { get; }

    /// <summary>The parent, or null for a root not yet attached.</summary>
    public UiElement? Parent { get; private set; }

    /// <summary>The children, back to front (retail's child list order).</summary>
    public IReadOnlyList<UiElement> Children => _children;

    /// <summary>The element's box relative to its parent (<c>m_box</c>).</summary>
    public UiBox Box => _box;

    /// <summary>The z level (<c>m_zlevel</c>); within a parent, lower is in front.</summary>
    public int ZLevel => _zLevel;

    /// <summary>The element's own visibility flag; <see cref="IsShown"/> also needs every ancestor's.</summary>
    public bool Visible => _visible;

    /// <summary>The image (<c>m_image</c>, <c>m_eBlitMode</c>).</summary>
    public UiImageRef Image => _image;

    /// <summary>
    /// The picture a screen made in memory and gave the element (<see cref="SetGeneratedImage"/>), or null: it is drawn instead of
    /// <see cref="Image"/>, with the element's blit mode.
    /// </summary>
    public UiImage? GeneratedImage => _generatedImage;

    /// <summary>The hit-test mask's DID (<c>m_alphaImage</c>), or 0.</summary>
    public uint AlphaImageDid => _alphaImageDid;

    /// <summary>True when the element has a surface of its own (<c>UIElement::GetShouldOwnObject</c>): roots and dialogs.</summary>
    public bool OwnsSurface => _ownsSurface;

    /// <summary>True for a mode root (<c>UIElement::SetIsRootElement</c>; flag 0x200000): it anchors to the display.</summary>
    public bool IsRoot { get; private set; }

    /// <summary>The surface's presentation alpha (the <c>UIObject</c>'s, set by fade media).</summary>
    public float SurfaceAlpha => _surfaceAlpha;

    // ---------------------------------------------------------------- IUiRegionView

    /// <inheritdoc/>
    UiBox IUiRegionView.Box => _box;

    /// <inheritdoc/>
    bool IUiRegionView.IsVisible => _visible;

    /// <inheritdoc/>
    public float AlphaBlendMod => _alphaBlendMod;

    /// <inheritdoc/>
    public (int X, int Y) TilingOffset => _tilingOffset;

    /// <inheritdoc/>
    public bool ShouldEraseBackground => _shouldEraseBackground;

    /// <summary><c>UIRegion::SetShouldEraseBackground</c> (11.4186 0x00464950, 11.6096 0x00464A80): property 0x51 at run time; no repaint is asked (the next one erases).</summary>
    public void SetShouldEraseBackground(bool erase) => _shouldEraseBackground = erase;

    /// <inheritdoc/>
    public bool DrawAfterChildren => _drawAfterChildren;

    /// <inheritdoc/>
    public ReadOnlySpan<IUiRegionView> ChildrenInDrawOrder => _childViews.AsSpan(0, _children.Count);

    /// <inheritdoc/>
    public virtual uint ViewportElementId => ClassId == UiClassIds.Viewport ? ElementId : 0;

    /// <inheritdoc/>
    public virtual UiBox ClipChild(IUiRegionView child, UiBox childSelf, UiBox childClip) => childClip;

    /// <summary>
    /// The element's box in logical window pixels (<see cref="UiElementManager.DisplayOrigin"/> included): its parents'
    /// offsets, from the display origin. Mouse coordinates are in the same space.
    /// </summary>
    public UiBox ScreenBox
    {
        get
        {
            var (x, y) = ScreenOrigin();
            return new UiBox(x, y, x + _box.Width - 1, y + _box.Height - 1);
        }
    }

    /// <summary>
    /// The element's box in its surface's coordinates (the element that owns the surface is at (0,0)); retail keeps each
    /// region's box relative to its parent and adds offsets up to the object.
    /// </summary>
    public UiBox SurfaceBox
    {
        get
        {
            if (_ownsSurface || Parent is null)
            {
                return new UiBox(0, 0, _box.Width - 1, _box.Height - 1);
            }

            var parent = Parent.SurfaceBox;
            return new UiBox(parent.X0 + _box.X0, parent.Y0 + _box.Y0, parent.X0 + _box.X1, parent.Y0 + _box.Y1);
        }
    }

    /// <summary>The element that owns the surface this element draws into (itself, or its nearest ancestor that owns one), or null.</summary>
    public UiElement? SurfaceOwner
    {
        get
        {
            for (var e = this; e is not null; e = e.Parent)
            {
                if (e._ownsSurface)
                {
                    return e;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// True when the element and every ancestor up to the manager's root are visible (<c>UIElement::IsVisible</c>,
    /// 11.4186 0x004602C0, 11.6096 0x004603A0); an element not attached under the root is not shown.
    /// </summary>
    public bool IsShown
    {
        get
        {
            var e = this;
            while (e is not null)
            {
                if (!e._visible)
                {
                    return false;
                }

                if (ReferenceEquals(e, Manager.RootElement))
                {
                    return true;
                }

                e = e.Parent;
            }

            return false;
        }
    }

    /// <summary>
    /// <c>UIRegion::DrawSelf</c> (11.4186 0x0069F1A0, 11.6096 0x006A0020): the image, once per dirty piece, the last piece
    /// first. Classes that draw more (text) override and call this first.
    /// </summary>
    public virtual void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
        ArgumentNullException.ThrowIfNull(painter);
        if (_generatedImage is { } generated)
        {
            for (var i = dirty.Length - 1; i >= 0; i--)
            {
                painter.DrawImage(generated, _image.Mode, _alphaBlendMod, self, dirty[i], _tilingOffset);
            }

            return;
        }

        if (!_image.HasImage)
        {
            return;
        }

        for (var i = dirty.Length - 1; i >= 0; i--)
        {
            painter.DrawImage(_image, _alphaBlendMod, self, dirty[i], _tilingOffset);
        }
    }

    /// <summary>What a class draws over its children (<see cref="IUiRegionView.DrawOverChildren"/>); nothing by default.</summary>
    public virtual void DrawOverChildren(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
    }

    // ---------------------------------------------------------------- Tree

    /// <summary>The direct child with id <paramref name="elementId"/>, or null (<c>UIElement::GetChild</c>, 11.4186 0x00463A70, 11.6096 0x00463BA0).</summary>
    public UiElement? GetChild(uint elementId)
    {
        foreach (var child in _children)
        {
            if (child.ElementId == elementId)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// The descendant with id <paramref name="elementId"/>: a direct child first, then each child's subtree in child order
    /// (<c>UIElement::GetChildRecursive</c>, 11.4186 0x00463AD0, 11.6096 0x00463C00).
    /// </summary>
    public UiElement? GetChildRecursive(uint elementId)
    {
        var direct = GetChild(elementId);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var child in _children)
        {
            var found = child.GetChildRecursive(elementId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>UIElement::SetParent</c> (11.4186 0x00462A50, 11.6096 0x00462B30): leaves the old parent, joins the new one in
    /// draw order, and re-anchors to it (<see cref="UiRuntimeLayout.UpdateForParentSizeChange"/>).
    /// </summary>
    public void SetParent(UiElement? parent)
    {
        if (ReferenceEquals(Parent, parent))
        {
            return;
        }

        // UIRegion::SetParent: the old area is dirty, the region moves, the new area is dirty.
        MakeRootDirtyHere();
        Parent?.RemoveChild(this);
        Parent = parent;
        SetVisible(_visible);
        MakeRootDirtyHere();
        parent?.AddChild(this);
        UiRuntimeLayout.UpdateForParentSizeChange(this);
    }

    /// <summary>
    /// <c>UIRegion::AddChild</c> (11.4186 0x006A0760, 11.6096 0x006A1580): walking back from the last child, the new child
    /// goes after the first child that compares at or above it (<see cref="CompareZLevel"/>), else first; so children
    /// run from the highest z level to the lowest, and within a z level by rising read order.
    /// </summary>
    private void AddChild(UiElement child)
    {
        var index = 0;
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].CompareZLevel(child) >= 0)
            {
                index = i + 1;
                break;
            }
        }

        _children.Insert(index, child);
        RebuildChildViews();
        Manager.OnChildrenChanged(this);
    }

    /// <summary><c>UIRegion::RemoveChild</c> (11.4186 0x006A0330, 11.6096 0x006A1150).</summary>
    private void RemoveChild(UiElement child)
    {
        if (_children.Remove(child))
        {
            RebuildChildViews();
            Manager.OnChildrenChanged(this);
        }
    }

    /// <summary>
    /// <c>UIRegion::BringToFront</c> (11.4186 0x006A07F0, 11.6096 0x006A1610): moves <paramref name="child"/> to the front
    /// of the children that share its z level (it never passes a child of a lower z level); nothing happens when it is
    /// already the last of its z level.
    /// </summary>
    public void BringToFront(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var index = _children.IndexOf(child);
        if (index < 0)
        {
            return;
        }

        var hasNext = index + 1 < _children.Count;
        if (hasNext && _children[index + 1]._zLevel != child._zLevel)
        {
            return;
        }

        _children.RemoveAt(index);
        if (_children.Count > 0 && _children[^1]._zLevel < child._zLevel)
        {
            // Walk forward from the old position past every child at or above the z level; insert after the last one.
            var insertAfter = index - 1;
            for (var i = index; i < _children.Count; i++)
            {
                if (_children[i]._zLevel < child._zLevel)
                {
                    break;
                }

                insertAfter = i;
            }

            _children.Insert(insertAfter + 1, child);
        }
        else
        {
            _children.Add(child);
        }

        RebuildChildViews();
        Manager.OnChildrenChanged(this);
        child.MakeRootDirtyHere();
    }

    /// <summary>
    /// <c>UIElement::BringChildToTop</c> (11.4186 0x004654E0, 11.6096 0x00465610): moves <paramref name="child"/> to the end of
    /// the child list, in front of every sibling whatever their z levels (the tooltip and dragged items).
    /// </summary>
    public void BringChildToTop(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            _children.Add(child);
            RebuildChildViews();
            Manager.OnChildrenChanged(this);
            child.MakeRootDirtyHere();
        }
    }

    /// <summary>
    /// <c>UIElement::CompareZLevel</c> (11.4186 0x0045FC60, 11.6096 0x0045FD40) over <c>UIRegion::CompareZLevel</c>
    /// (11.4186 0x0069F000, 11.6096 0x0069FE80): positive when this element's z level is higher; at equal z levels,
    /// positive when its read order is lower; 0 when both match.
    /// </summary>
    public int CompareZLevel(UiElement other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_zLevel != other._zLevel)
        {
            return _zLevel > other._zLevel ? 1 : -1;
        }

        var mine = Desc.ReadOrder;
        var theirs = other.Desc.ReadOrder;
        return mine == theirs ? 0 : mine < theirs ? 1 : -1;
    }

    private void RebuildChildViews()
    {
        if (_childViews.Length < _children.Count)
        {
            _childViews = new IUiRegionView[Math.Max(4, _children.Count * 2)];
        }

        for (var i = 0; i < _children.Count; i++)
        {
            _childViews[i] = _children[i];
        }

        Array.Clear(_childViews, _children.Count, _childViews.Length - _children.Count);
    }

    // ---------------------------------------------------------------- Region

    /// <summary>
    /// <c>UIElement::MoveTo</c> (11.4186 0x004633E0, 11.6096 0x004634C0) over <c>UIRegion::MoveTo</c> (11.4186 0x0069F830,
    /// 11.6096 0x006A06B0): the old and the new area are dirty.
    /// </summary>
    public virtual void MoveTo(int x, int y) => MoveRegion(x, y);

    private void MoveRegion(int x, int y)
    {
        if (x == _box.X0 && y == _box.Y0)
        {
            return;
        }

        MakeRootDirtyHere();
        _box = new UiBox(x, y, _box.X1 + x - _box.X0, _box.Y1 + y - _box.Y0);
        MakeRootDirtyHere();
    }

    /// <summary>
    /// <c>UIElement::ResizeTo</c> (11.4186 0x00463C30, 11.6096 0x00463D60): the size is clamped by the element's
    /// minimum and maximum properties (0x3C-0x3F); when it changes, the region is resized (<c>UIRegion::ResizeTo</c>,
    /// 11.4186 0x0069F8C0, 11.6096 0x006A0740) and every child re-anchors (<see cref="UiRuntimeLayout"/>).
    /// </summary>
    public virtual void ResizeTo(int width, int height) => ResizeCore(width, height, notifyChildren: true);

    private void ResizeCore(int width, int height, bool notifyChildren)
    {
        if (TryGetIntegerProperty(UiPropertyIds.ElementMaxHeight, out var maxHeight) && height > maxHeight)
        {
            height = maxHeight;
        }

        if (TryGetIntegerProperty(UiPropertyIds.ElementMinHeight, out var minHeight) && height < minHeight)
        {
            height = minHeight;
        }

        if (TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out var maxWidth) && width > maxWidth)
        {
            width = maxWidth;
        }

        if (TryGetIntegerProperty(UiPropertyIds.ElementMinWidth, out var minWidth) && width < minWidth)
        {
            width = minWidth;
        }

        var oldWidth = _box.Width;
        var oldHeight = _box.Height;
        if (oldWidth == width && oldHeight == height)
        {
            return;
        }

        // UIRegion::ResizeTo dirties the old area only when the region shrinks, then the new area.
        if (width < oldWidth || height < oldHeight)
        {
            MakeRootDirtyHere();
        }

        _box = new UiBox(_box.X0, _box.Y0, _box.X0 + width - 1, _box.Y0 + height - 1);
        MakeRootDirtyHere();
        if (notifyChildren)
        {
            // Children re-anchor in child order (UIElement::UpdateForParentSizeChange each).
            for (var i = 0; i < _children.Count; i++)
            {
                UiRuntimeLayout.UpdateForParentSizeChange(_children[i]);
            }
        }

        OnResized();
    }

    /// <summary>Called after the size changed (text re-wraps).</summary>
    protected virtual void OnResized()
    {
    }

    /// <summary>
    /// <c>UIElement::SetVisible</c> (11.4186 0x004622B0, 11.6096 0x00462390) over <c>UIRegion::SetVisible</c> (11.4186
    /// 0x0069FED0, 11.6096 0x006A0D50): sets the flag, re-applies each child's own flag, and dirties the area when the flag
    /// changed; <see cref="OnVisibilityChanged"/> runs when whether the element shows changed.
    /// </summary>
    public virtual void SetVisible(bool visible)
    {
        var wasShown = IsShown;
        var changed = _visible != visible;
        _visible = visible;
        for (var i = 0; i < _children.Count; i++)
        {
            _children[i].SetVisible(_children[i]._visible);
        }

        if (changed)
        {
            MakeRootDirtyHere();

            // UIElement::SetVisible: a change of the element's own flag is announced.
            BroadcastElementMessage(UiMessageIds.VisibilityChanged, visible ? 1u : 0u);
        }

        var shown = IsShown;
        if (wasShown != shown)
        {
            OnVisibilityChanged(shown);
        }
    }

    /// <summary>
    /// Called when whether the element shows changed (<c>UIElement::OnVisibilityChanged</c>, 11.4186 0x00463700, 11.6096
    /// 0x00463830): shown, an element that activates on show (property 0x34) activates its window and one that takes the
    /// focus on show (0x35) takes it; hidden, it leaves the activation order and an active window deactivates (the next
    /// one activates). Either way the mouse is hit-tested again. Classes that override call the base.
    /// </summary>
    protected virtual void OnVisibilityChanged(bool shownNow)
    {
        if (shownNow)
        {
            if (ActivateOnShow)
            {
                Activate();
            }

            if (TryGetBoolProperty(UiPropertyIds.ElementFocusOnShow, out var focusOnShow) && focusOnShow)
            {
                TakeFocus();
            }
        }
        else
        {
            Manager.UnregisterActivatable(this);
            if (IsActivated)
            {
                Deactivate();
                if (IsRoot)
                {
                    Manager.ActivateNext();
                }
            }
        }

        Manager.Input.RequestMouseHitTest();
    }

    /// <summary>
    /// <c>UIRegion::SetImageByDID</c> (11.4186 0x0069F960, 11.6096 0x006A07E0): the blit mode follows the draw mode
    /// (<see cref="UiBlitModes.FromDrawMode"/>) and the new image replaces the old; the area is dirty. A DID of 0 clears
    /// the image (<c>MediaMachine::Update_Image</c>).
    /// </summary>
    public void SetImageByDid(uint did, UiDrawMode drawMode)
    {
        if (did == 0)
        {
            ClearImage();
            return;
        }

        _generatedImage = null;
        _image = new UiImageRef(did, UiBlitModes.FromDrawMode(drawMode));
        MakeRootDirtyHere();
    }

    /// <summary>
    /// <c>UIRegion::SetImage</c> (11.4186 0x0069F790, 11.6096 0x006A0610) with a <c>Graphic</c> a screen drew itself (character creation's colour spots and gradient disk,
    /// <c>gmCGAppearancePage::DoColorSpots</c> and <c>DoGradDisk</c>): the picture replaces the element's image, keeps its blit mode, and the
    /// area is dirty. The element does not copy it: the caller hands over a picture it will not change.
    /// </summary>
    public void SetGeneratedImage(UiImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _generatedImage = image;
        MakeRootDirtyHere();
    }

    /// <summary><c>UIRegion::ClearImage</c> (11.4186 0x0069F7E0, 11.6096 0x006A0660): the area is dirty when there was an image.</summary>
    public void ClearImage()
    {
        var had = _image.HasImage || _generatedImage is not null;
        _generatedImage = null;
        _image = new UiImageRef(0, _image.Mode);
        if (had)
        {
            MakeRootDirtyHere();
        }
    }

    /// <summary><c>UIRegion::SetBlitMode</c> (11.4186 0x0069F6F0, 11.6096 0x006A0570).</summary>
    public void SetBlitMode(UiBlitMode mode)
    {
        if (_image.Mode != mode)
        {
            _image = _image with { Mode = mode };
            MakeRootDirtyHere();
        }
    }

    /// <summary>
    /// <c>UIRegion::SetAlphaBlendMod</c> (11.4186 0x0069F5E0, 11.6096 0x006A0460): clamped to 0-1; the area is dirty when
    /// it changed and the blit mode is not a plain copy (a copy ignores it).
    /// </summary>
    public void SetAlphaBlendMod(float mod)
    {
        var clamped = mod >= 0f ? (mod <= 1f ? mod : 1f) : 0f;
        var old = _alphaBlendMod;
        _alphaBlendMod = clamped;
        if (clamped != old && _image.Mode != UiBlitMode.SrcCopy)
        {
            MakeRootDirtyHere();
        }
    }

    /// <summary><c>UIRegion::SetTilingOffset</c> (11.4186 0x0069F730, 11.6096 0x006A05B0).</summary>
    public void SetTilingOffset(int x, int y)
    {
        if (_tilingOffset != (x, y))
        {
            _tilingOffset = (x, y);
            MakeRootDirtyHere();
        }
    }

    /// <summary><c>UIRegion::SetAlphaImageByDID</c> (11.4186 0x0069F030, 11.6096 0x0069FEB0): the hit-test mask (nothing is redrawn).</summary>
    public void SetAlphaImageByDid(uint did) => _alphaImageDid = did;

    /// <summary><c>UIRegion::ClearAlphaImage</c>.</summary>
    public void ClearAlphaImage() => _alphaImageDid = 0;

    /// <summary>
    /// <c>UIElement::SetShouldOwnObject</c> (11.4186 0x00461BD0, 11.6096 0x00461CB0): whether the element has a surface of
    /// its own.
    /// </summary>
    public void SetShouldOwnObject(bool owns)
    {
        if (_ownsSurface == owns)
        {
            return;
        }

        MakeRootDirtyHere();
        _ownsSurface = owns;
        Manager.OnSurfaceOwnershipChanged(this);
        MakeRootDirtyHere();
    }

    /// <summary>
    /// <c>UIElement::SetIsRootElement</c> (11.4186 0x00460100, 11.6096 0x004601E0): a root becomes a child of the manager's
    /// root element and anchors from its layout's design size to the display.
    /// </summary>
    public void SetIsRootElement(bool isRoot)
    {
        IsRoot = isRoot;
        var managerRoot = Manager.RootElement;
        if (ReferenceEquals(Parent, managerRoot))
        {
            if (!isRoot)
            {
                SetParent(null);
            }
        }
        else if (isRoot)
        {
            SetParent(managerRoot);
        }
    }

    /// <summary>Sets the presentation alpha of the element's surface (fade media; the <c>UIObject</c>'s alpha).</summary>
    internal void SetSurfaceAlpha(float alpha) => _surfaceAlpha = alpha;

    /// <summary>
    /// <c>UIRegion::PointIsOverRegion</c> (11.4186 0x0069EE40, 11.6096 0x0069FCC0): the point (in the parent's coordinates)
    /// is inside the box and, when the element has a hit-test mask, the mask's pixel there is black
    /// (<see cref="UiImage.PointTestOverlay(int, int, bool)"/>, retail's A8 test with <see cref="UiRetailGlitches.A8PointTest"/>).
    /// </summary>
    public bool PointIsOverRegion(int xParent, int yParent)
    {
        if (xParent < _box.X0 || xParent > _box.X1 || yParent < _box.Y0 || yParent > _box.Y1)
        {
            return false;
        }

        if (_alphaImageDid == 0)
        {
            return true;
        }

        var mask = Manager.Services.Images?.TryGetImage(_alphaImageDid, out var image) == UiImageLookup.Ready ? image : null;
        var retail = (Manager.Services.RetailGlitches & UiRetailGlitches.A8PointTest) != 0;
        return mask is null || !mask.PointTestOverlay(xParent - _box.X0, yParent - _box.Y0, retail);
    }

    // ---------------------------------------------------------------- Dirty rectangles

    /// <summary>
    /// <c>UIRegion::AddDirtyRect</c> (11.4186 0x0069F080, 11.6096 0x0069FF00): marks <paramref name="surfaceRect"/> (in the
    /// element's surface coordinates) for repainting, clipped to the element and to each ancestor up to the surface's
    /// owner. Nothing happens for an element with no surface above it.
    /// </summary>
    public void AddDirtyRect(UiBox surfaceRect)
    {
        var rect = surfaceRect;
        for (var e = this; e is not null; e = e.Parent)
        {
            if (!UiBoxes.TryIntersect(rect, e.SurfaceBox, out rect))
            {
                return;
            }

            if (e._ownsSurface)
            {
                Manager.OnDirty(e, rect);
                return;
            }
        }
    }

    /// <summary><c>UIRegion::MakeRootDirtyHere</c> (11.4186 0x0069F5B0, 11.6096 0x006A0430): the element's whole area is dirty.</summary>
    public void MakeRootDirtyHere() => AddDirtyRect(SurfaceBox);

    private (int X, int Y) ScreenOrigin()
    {
        if (Parent is null)
        {
            (int X, int Y) origin = ReferenceEquals(this, Manager.RootElement) ? Manager.DisplayOrigin : (0, 0);
            return (origin.X + _box.X0, origin.Y + _box.Y0);
        }

        var (px, py) = Parent.ScreenOrigin();
        return (px + _box.X0, py + _box.Y0);
    }
}
