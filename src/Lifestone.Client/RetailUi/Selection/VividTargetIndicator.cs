using Lifestone.Client.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Dat;

namespace Lifestone.Client.RetailUi.Selection;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: VividTargetIndicator constructor 0x004F6AE0, ::Initialized 0x004F6C60, ::OnDraw 0x004F62B0,
// ::CopyImage 0x004F5DD0, ::SetEnabled 0x004F60A0, ::SetOnScreenColor 0x004F61B0, ::SetOffScreenImage 0x004F6140.
/// <summary>What the vivid target indicator shows.</summary>
public enum VividTargetState
{
    /// <summary>Nothing: no target, the option or the portal cue is off, or the target is not drawn.</summary>
    Hidden,

    /// <summary>The four corners around the target box (<c>ObjectSelectStatus</c> 1).</summary>
    OnScreen,

    /// <summary>The arrow on the view border pointing to the target (<c>ObjectSelectStatus</c> 2).</summary>
    OffScreen,
}

/// <summary>
/// The vivid target indicator (<c>VividTargetIndicator</c>, an embedded member of <c>gmSmartBoxUI</c>; 11.6096 constructor 0x004F7720, <c>Initialized</c> 0x004F78A0, <c>OnDraw</c> 0x004F6EF0,
/// <c>CopyImage</c> 0x004F6A70; docs/retail-ui.md section 21.7). Around the selected object it draws four corners (the frame 0x10000038 with its corners 0x10000039-0x1000003C), or, when the
/// object is outside the view, one of eight arrows (0x10000045) on the view border. The pictures are the 13 images of the enum group 0x10000009 (indices 1-4 the corners, 5-12 the arrows),
/// tinted with the target radar colour (<see cref="VividColorize"/>).
/// </summary>
/// <remarks>
/// <para>Drawn only when it is <see cref="Enabled"/> (retail <c>SetEnabled</c>, driven by portal space: <c>PortalCues.SelectionIndicatorOff</c> hides it for the tunnel), the character option
/// <see cref="OptionOn"/> (<c>PlayerModule::VividTargetingIndicator</c>, option bit 15) is set, and there is a target. Retail is told the target box by the SmartBox every frame; the shell asks
/// the <see cref="ISelectionProjector"/> instead (the Setup selection sphere, as <c>SmartBox::GetObjectBoundingBox</c> does).</para>
/// <para>The pictures are fetched when first needed; while one is still decoding the indicator stays hidden and tries again on the next <see cref="Draw"/>.</para>
/// </remarks>
public sealed class VividTargetIndicator
{
    /// <summary>The on-screen frame (<c>m_pOnScreen</c>).</summary>
    public const uint OnScreenId = 0x10000038;

    /// <summary>The off-screen arrow (<c>m_pOffScreen</c>).</summary>
    public const uint OffScreenId = 0x10000045;

    /// <summary>The four corners (<c>m_rgOnScreenCorners</c>, indices 1 to 4): top left, top right, bottom left, bottom right.</summary>
    public static IReadOnlyList<uint> CornerIds { get; } = [0x10000039, 0x1000003A, 0x1000003B, 0x1000003C];

    private readonly UiElementManager _manager;
    private readonly UiElement? _onScreen;
    private readonly UiElement? _offScreen;
    private readonly UiElement?[] _corners = new UiElement?[4];
    private readonly UiImage?[] _sources = new UiImage?[VividTargetMath.SourceImageCount];
    private readonly bool[] _sourceFailed = new bool[VividTargetMath.SourceImageCount];
    private UiRgbaColor _onScreenColor;
    private bool _onScreenColored;
    private UiRgbaColor _offScreenColor;
    private int _offScreenImage = -1;
    private bool _imagesPending;

    /// <summary>Finds the indicator elements among the SmartBox children (<c>VividTargetIndicator::Initialized</c>, 11.6096 0x004F78A0).</summary>
    /// <param name="manager">The element manager (images, enum maps, log).</param>
    /// <param name="smartBox">The SmartBox element, or null (the indicator then never shows).</param>
    public VividTargetIndicator(UiElementManager manager, UiElement? smartBox)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        SmartBox = smartBox;
        _onScreen = smartBox?.GetChildRecursive(OnScreenId);
        _offScreen = smartBox?.GetChildRecursive(OffScreenId);
        for (var i = 0; i < CornerIds.Count; i++)
        {
            _corners[i] = _onScreen?.GetChildRecursive(CornerIds[i]);
        }

        // Initialized finds the elements and hides both; a draw shows one.
        _onScreen?.SetVisible(false);
        _offScreen?.SetVisible(false);
        if (_onScreen is null || _offScreen is null || _corners.Any(c => c is null))
        {
            manager.LogOnce(
                "vti:elements",
                LogSeverity.Error,
                "VividTargetIndicator::Initialized: the SmartBox lacks the on-screen frame 0x10000038, its four corners or the off-screen arrow 0x10000045; the vivid target indicator shows nothing");
        }
    }

    /// <summary>The SmartBox the indicator draws in.</summary>
    public UiElement? SmartBox { get; }

    /// <summary>The state last applied.</summary>
    public VividTargetState State { get; private set; }

    /// <summary>Retail <c>m_bEnabled</c>, set by <c>SetEnabled</c> (11.6096 0x004F6CE0): false while portal space shows its tunnel. True at the start.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Retail <c>m_bDisplayOn</c>: the character option bit 15 (<c>PlayerModule::VividTargetingIndicator</c>).</summary>
    public bool OptionOn { get; set; }

    /// <summary>The on-screen frame's box as placed (logical UI pixels), or null while not on screen.</summary>
    public (int X, int Y, int Width, int Height)? FrameBox { get; private set; }

    /// <summary>The arrow's position as placed, or null while it is not showing.</summary>
    public (int X, int Y)? ArrowPosition { get; private set; }

    /// <summary>The source image index of the arrow showing (5 to 12), or -1.</summary>
    public int ArrowImage => State == VividTargetState.OffScreen ? _offScreenImage : -1;

    /// <summary>The heading (degrees) the arrow points at, while it shows.</summary>
    public double Heading { get; private set; }

    /// <summary>The colour the pictures are tinted with.</summary>
    public UiRgbaColor Color { get; private set; }

    /// <summary>Counts the draws that placed something (a test hook).</summary>
    public int Draws { get; private set; }

    /// <summary>The on-screen frame element.</summary>
    public UiElement? OnScreenElement => _onScreen;

    /// <summary>The off-screen arrow element.</summary>
    public UiElement? OffScreenElement => _offScreen;

    /// <summary>The corner element at <paramref name="index"/> (0 to 3).</summary>
    public UiElement? Corner(int index) => _corners[index];

    /// <summary>Hides both parts (retail <c>OnDraw</c> with no target or the indicator off).</summary>
    public void Hide()
    {
        if (State == VividTargetState.Hidden && _onScreen?.Visible != true && _offScreen?.Visible != true)
        {
            return;
        }

        State = VividTargetState.Hidden;
        FrameBox = null;
        ArrowPosition = null;
        _onScreen?.SetVisible(false);
        _offScreen?.SetVisible(false);
    }

    /// <summary>
    /// <c>VividTargetIndicator::OnDraw</c> for a target. <paramref name="found"/> false is status 3 (no such object: hidden); otherwise the projected <paramref name="rect"/> and
    /// <paramref name="onScreen"/> say status 1 or 2.
    /// </summary>
    /// <param name="found">True when the projector knows the object.</param>
    /// <param name="rect">The projected box in logical UI pixels.</param>
    /// <param name="onScreen">True when the box is in the view.</param>
    /// <param name="color">The target radar colour.</param>
    public void Draw(bool found, in ScreenRect rect, bool onScreen, UiRgbaColor color)
    {
        if (!Enabled || !OptionOn || !found || _onScreen is null || _offScreen is null || SmartBox is not { } box)
        {
            Hide();
            return;
        }

        _imagesPending = false;
        var viewWidth = box.ScreenBox.Width;
        var viewHeight = box.ScreenBox.Height;
        Color = color;
        if (onScreen)
        {
            if (!TrySetOnScreenColor(color))
            {
                Hide();
                return;
            }

            var (cornerWidth, cornerHeight) = CornerSize();
            var frame = VividTargetMath.OnScreenFrame(rect, cornerWidth, cornerHeight, viewWidth, viewHeight);
            if (FrameBox != frame || State != VividTargetState.OnScreen)
            {
                _onScreen.MoveTo(frame.X, frame.Y);
                _onScreen.ResizeTo(frame.Width, frame.Height);
            }

            FrameBox = frame;
            ArrowPosition = null;
            if (State != VividTargetState.OnScreen)
            {
                _onScreen.SetVisible(true);
                _offScreen.SetVisible(false);
            }

            State = VividTargetState.OnScreen;
            Draws++;
            return;
        }

        var heading = VividTargetMath.HeadingOf(rect, viewWidth, viewHeight);
        Heading = heading;
        var image = VividTargetMath.OffScreenImage(heading);
        if (!TrySetOffScreenImage(image, color))
        {
            Hide();
            return;
        }

        var position = VividTargetMath.OffScreenPosition(heading, _offScreen.ScreenBox.Width, _offScreen.ScreenBox.Height, viewWidth, viewHeight);
        if (ArrowPosition != position || State != VividTargetState.OffScreen)
        {
            _offScreen.MoveTo(position.X, position.Y);
        }

        ArrowPosition = position;
        FrameBox = null;
        if (State != VividTargetState.OffScreen)
        {
            _offScreen.SetVisible(true);
            _onScreen.SetVisible(false);
        }

        State = VividTargetState.OffScreen;
        Draws++;
    }

    private (int Width, int Height) CornerSize()
    {
        var corner = _corners[0];
        return corner is null ? (12, 12) : (corner.ScreenBox.Width, corner.ScreenBox.Height);
    }

    // VividTargetIndicator::SetOnScreenColor (11.6096 0x004F61B0): the four corners get the tinted pictures 1 to 4 when the colour changed.
    private bool TrySetOnScreenColor(UiRgbaColor color)
    {
        if (_onScreenColored && _onScreenColor == color)
        {
            return true;
        }

        for (var i = 0; i < 4; i++)
        {
            if (!TryGetSource(i + 1, out _))
            {
                return false;
            }
        }

        for (var i = 0; i < 4; i++)
        {
            _corners[i]?.SetGeneratedImage(VividColorize.Apply(_sources[i + 1]!, color));
        }

        _onScreenColor = color;
        _onScreenColored = true;
        return true;
    }

    // VividTargetIndicator::SetOffScreenImage (11.6096 0x004F6140): a new picture or a new colour makes a new tinted copy.
    private bool TrySetOffScreenImage(int image, UiRgbaColor color)
    {
        if (image == _offScreenImage && _offScreenColor == color)
        {
            return true;
        }

        if (!TryGetSource(image, out var source))
        {
            return false;
        }

        _offScreen!.SetGeneratedImage(VividColorize.Apply(source!, color));
        _offScreenImage = image;
        _offScreenColor = color;
        return true;
    }

    // The source pictures: DBObj::GetByEnum(index, 0x10000009, RenderSurface) for indices 1 to 12 (the constructor, 11.6096 0x004F7720).
    private bool TryGetSource(int index, out UiImage? image)
    {
        image = _sources[index];
        if (image is not null)
        {
            return true;
        }

        if (_sourceFailed[index])
        {
            return false;
        }

        var services = _manager.Services;
        if (services.EnumMaps is null || services.Images is null)
        {
            return Fail(index, "the UI services have no enum maps or image source");
        }

        if (!services.EnumMaps.TryResolve(new EnumGroup(VividTargetMath.ImageGroup), (uint)index, out var did))
        {
            return Fail(index, $"enum 0x{VividTargetMath.ImageGroup:X8} index {index} names no image");
        }

        switch (services.Images.TryGetImage(did, out var found))
        {
            case UiImageLookup.Ready:
                _sources[index] = found;
                image = found;
                return true;
            case UiImageLookup.Pending:
                _imagesPending = true;
                return false;
            default:
                return Fail(index, $"image 0x{did:X8} does not decode");
        }
    }

    private bool Fail(int index, string why)
    {
        _sourceFailed[index] = true;
        _manager.LogOnce(
            $"vti:image:{index}",
            LogSeverity.Error,
            $"VividTargetIndicator: source image {index}: {why}; the indicator does not show (placeholder)");
        return false;
    }

    /// <summary>True while a source picture is still decoding (the indicator tries again on the next frame).</summary>
    public bool ImagesPending => _imagesPending;
}
