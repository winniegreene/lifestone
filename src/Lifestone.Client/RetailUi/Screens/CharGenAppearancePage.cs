using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>The two halves of the appearance page (<c>gmCGAppearancePage::EType</c>): the face and the clothes.</summary>
public enum CharGenAppearanceType
{
    /// <summary>1: hair, eyes, nose, mouth and skin, with the camera close to the head.</summary>
    Face = 1,

    /// <summary>2: headgear, shirt, trousers and footwear, with the whole figure in view.</summary>
    Clothes = 2,
}

/// <summary>The nine things the appearance page picks (<c>gmCGAppearancePage::EParts</c>), in retail's numbering.</summary>
public enum CharGenAppearancePart
{
    /// <summary>1: the hair style and colour.</summary>
    Hair = 1,

    /// <summary>2: the eye strip and colour.</summary>
    Eyes = 2,

    /// <summary>3: the nose strip.</summary>
    Nose = 3,

    /// <summary>4: the mouth strip.</summary>
    Mouth = 4,

    /// <summary>5: the skin shade.</summary>
    Skin = 5,

    /// <summary>6: the headgear.</summary>
    Headgear = 6,

    /// <summary>7: the shirt.</summary>
    Shirt = 7,

    /// <summary>8: the trousers.</summary>
    Trousers = 8,

    /// <summary>9: the footwear.</summary>
    Footwear = 9,
}

/// <summary>What the page holds for one part (<c>gmCGAppearancePage</c>'s <c>m_tChoices</c>): how many styles, the current one, how many colours, the current colour and the shade.</summary>
public sealed class CharGenPartChoice
{
    /// <summary>The number of styles the part has.</summary>
    public int NumChoices { get; internal set; }

    /// <summary>The style chosen (-1 for none, headgear only).</summary>
    public int CurrentChoice { get; internal set; }

    /// <summary>The number of colours the part's wheel shows.</summary>
    public int NumColors { get; internal set; }

    /// <summary>The colour chosen.</summary>
    public int CurrentColor { get; internal set; } = -1;

    /// <summary>The shade chosen, 0 to 1.</summary>
    public double Shade { get; internal set; }
}

/// <summary>
/// The appearance page (retail's <c>gmCGAppearancePage</c>, element 0x100003D4, class 0x1000003C): gender, a face half (hair, eyes, nose, mouth and
/// skin, with the camera close to the head) and a clothes half (headgear, shirt, trousers and footwear, with the whole figure in view), a spinner for
/// each part with arrows for its styles, a wheel of up to nine colours and a slider for the shade, buttons that zoom and turn the character, and the
/// 3D character in its viewport (0x100003BB). Every choice goes into the screen's <see cref="CharacterCreation.CharGenState"/>.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCGAppearancePage::InitializePage</c> (11.4186 0x0047FDD0, 11.6096 0x00480180), <c>Update</c> (0x0047E8F0, 0x0047ECA0),
/// <c>SetChoice</c> (0x0047D4B0, 0x0047D860), <c>SetSelection</c> (0x0047E260, 0x0047E610), <c>SetupParts</c> (0x0047E020, 0x0047E3D0), <c>SetColor</c>
/// (0x0047DD50, 0x0047E100), <c>SetShade</c> (0x0047C860, 0x0047CC10), <c>DoColorSpots</c> (0x0047D850, 0x0047DC00), <c>DoGradDisk</c> (0x0047DA90,
/// 0x0047DE40), <c>ZoomIn</c> (0x0047CF00, 0x0047D2B0), <c>ZoomOut</c> (0x0047D050, 0x0047D400), <c>DoZoomAnimation</c> (0x0047C960, 0x0047CD10),
/// <c>Rotate</c> (0x0047CB50, 0x0047CF00), <c>DoRotation</c> (0x0047CA80, 0x0047CE30), <c>ListenToGlobalMessage</c> (0x0047CED0, 0x0047D280) and
/// <c>ListenToElementMessage</c> (0x0047EF30, 0x0047F2E0).
/// </para>
/// <para>
/// <b>Face and clothes.</b> The Face tab selects the face half, hides the headgear (the state's style is held and set to none, so the head shows), and
/// zooms the camera in; the Clothes tab gives the headgear back and zooms out. Retail gives the headgear back only through the Clothes tab, so a character
/// whose page is left in the face half is created without it, a glitch: Lifestone gives it back when the page is left, and holds the state's headgear again when
/// the face half shows (<see cref="UiRetailGlitches.CharGenHeldHeadgear"/> restores retail's). The camera moves linearly over 0.6 s (a flag set by the zoom, a frame message
/// moving it). The character turns when a turn button is on, a whole turn in 5 s; the same button again stops it, and the other one takes over.
/// </para>
/// <para>
/// <b>The wheel.</b> Each part picks its colour from up to nine spots: the hair's from its PalSets, the eyes' from their palettes, the clothes' from the
/// PalSets of the garment's colours; the nose, mouth and skin have one (the skin's), whose shade the slider picks. A spot is the layout's blank bullet with its
/// black replaced by the colour sampled at a fixed entry of the palette (208 for hair, 176 for skin, 259 for eyes, 1312 for clothes), averaged over the
/// palettes of a set; spots past the part's colours show the empty bullet and do nothing. The disk under the slider is the gradient ring multiplied by the
/// colour (the eyes show the plug and have no slider).
/// </para>
/// <para>
/// <b>Heritages.</b> The Gear Knights and the Olthoi have no clothes, nose or mouth: those controls hide, the hair, eyes and skin buttons take other texts
/// (<c>ID_CharGen_GearText_*</c>, <c>ID_CharGen_OlthoiText_*</c>), and the skin spinner moves up to y 90 (180 otherwise). Entering the Gear Knight heritage
/// from another (and leaving it) draws the appearance and clothing at random again.
/// </para>
/// <para>
/// <b>Kept from retail, observed.</b> Opening the page sets the opposite gender first (the code flips it before its first update; the character is random
/// at that point).
/// </para>
/// <para>
/// <b>Glitch fixed by default: the camera.</b> Retail's <c>Update</c>, which runs when the page is shown again, on Random and on a gender button, snaps the
/// camera to the face whatever the half showing and leaves the zoom flag alone, so the clothes half shows over a close-up of the head and the zoom-out button
/// does nothing until zoom-in is pressed. Lifestone puts the camera by the zoom state (the face when zoomed in, the whole figure otherwise);
/// <see cref="UiRetailGlitches.CharGenAppearanceCamera"/> restores retail's.
/// </para>
/// </remarks>
public sealed class CharGenAppearancePage : CharGenPage, IUiGlobalListener
{
    /// <summary>The Female button.</summary>
    public const uint FemaleButtonId = 0x100003A7;

    /// <summary>The Male button.</summary>
    public const uint MaleButtonId = 0x100003A8;

    /// <summary>The Face tab.</summary>
    public const uint FaceButtonId = 0x100003A9;

    /// <summary>The Clothes tab.</summary>
    public const uint ClothesButtonId = 0x100003AA;

    /// <summary>The group of the face half's spinners.</summary>
    public const uint FaceChoicesId = 0x100003AE;

    /// <summary>The group of the clothes half's spinners.</summary>
    public const uint ClothesChoicesId = 0x100003B4;

    /// <summary>The spinner's left arrow (a child of every spinner).</summary>
    public const uint LeftArrowId = 0x1000030A;

    /// <summary>The spinner's right arrow (a child of every spinner).</summary>
    public const uint RightArrowId = 0x1000030B;

    /// <summary>The first of the nine colour spots; the others follow it by one.</summary>
    public const uint FirstBulletId = 0x1000030F;

    /// <summary>The first of the nine rings that mark the chosen spot; the others follow it by one.</summary>
    public const uint FirstPointerId = 0x10000318;

    /// <summary>The disk under the shade slider.</summary>
    public const uint GradCircleId = 0x1000030E;

    /// <summary>The shade slider.</summary>
    public const uint ShadeScrollId = 0x10000321;

    /// <summary>Turn clockwise.</summary>
    public const uint RotateClockId = 0x10000323;

    /// <summary>Turn counter-clockwise.</summary>
    public const uint RotateCounterClockId = 0x10000324;

    /// <summary>Zoom in.</summary>
    public const uint ZoomInId = 0x10000325;

    /// <summary>Zoom out.</summary>
    public const uint ZoomOutId = 0x10000326;

    /// <summary>The colour spots on the wheel.</summary>
    public const int SpotCount = 9;

    /// <summary>The seconds the camera takes to zoom (<c>m_dAnimDuration</c>, set by the first frame after a zoom).</summary>
    public const double ZoomSeconds = 0.6;

    /// <summary>The seconds a whole turn takes (<c>m_dRotationPerSec</c>).</summary>
    public const double TurnSeconds = 5.0;

    private static readonly uint[] SpinnerIds = [0x100003AF, 0x100003B0, 0x100003B1, 0x100003B2, 0x100003B3, 0x100003B5, 0x100003B6, 0x100003B7, 0x100003B8];

    /// <summary>The wheel entry each part's spots are sampled at (<c>SetSelection</c>'s <c>iSampleColor</c>); the eyes' is a palette entry, not a PalSet's.</summary>
    private static readonly int[] SampleEntries = [208, 259, 176, 176, 176, 1312, 1312, 1312, 1312];

    private readonly UiButton? _female;
    private readonly UiButton? _male;
    private readonly UiButton? _face;
    private readonly UiButton? _clothes;
    private readonly UiButton?[] _spinners = new UiButton?[SpinnerIds.Length];
    private readonly UiElement? _faceChoices;
    private readonly UiElement? _clothesChoices;
    private readonly UiButton?[] _bullets = new UiButton?[SpotCount];
    private readonly UiElement?[] _pointers = new UiElement?[SpotCount];
    private readonly (byte R, byte G, byte B)?[] _spotColors = new (byte, byte, byte)?[SpotCount];
    private readonly UiElement? _gradCircle;
    private readonly UiScrollbar? _shadeScroll;
    private readonly UiButton? _zoomIn;
    private readonly UiButton? _zoomOut;
    private readonly UiButton? _rotateClock;
    private readonly UiButton? _rotateCounterClock;
    private readonly CharGenPartChoice[] _choices = [.. Enumerable.Range(0, SpinnerIds.Length).Select(_ => new CharGenPartChoice())];
    private readonly Cg3DViewModel? _view;
    private readonly IDisposable? _attachment;
    private UiButton? _currentSelection;
    private CharGenAppearancePart _part = CharGenAppearancePart.Hair;
    private CharGenAppearanceType _type;
    private int _partIndex;
    private int _color = -1;
    private int _heldHeadgear = -999;
    private bool _headgearHeld;
    private uint _lastHeritage;
    private bool _zoomedIn;
    private bool _animating;
    private double _animDuration;
    private double _animStart;
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private Vector3 _currentPosition;
    private bool _rotating;
    private int _rotateDirection;
    private double _lastRotateTime = -1.0;
    private float _heading = CreaturePreview.CharGenHeading;
    private bool _imagesPending;

    /// <summary>The appearance page over <paramref name="root"/>: its elements found, the 3D view made, and the first update run (the page's <c>InitializePage</c>).</summary>
    public CharGenAppearancePage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGAppearancePage")
    {
        _female = Find<UiButton>(FemaleButtonId);
        _male = Find<UiButton>(MaleButtonId);
        _face = Find<UiButton>(FaceButtonId);
        _clothes = Find<UiButton>(ClothesButtonId);
        for (var i = 0; i < SpinnerIds.Length; i++)
        {
            _spinners[i] = Find<UiButton>(SpinnerIds[i]);
        }

        // The skin has no styles: its arrows never work.
        Arrow(CharGenAppearancePart.Skin, LeftArrowId)?.SetBoolProperty(UiPropertyIds.ButtonGhosted, true);
        Arrow(CharGenAppearancePart.Skin, RightArrowId)?.SetBoolProperty(UiPropertyIds.ButtonGhosted, true);
        _faceChoices = Find<UiElement>(FaceChoicesId);
        _clothesChoices = Find<UiElement>(ClothesChoicesId);
        for (var i = 0; i < SpotCount; i++)
        {
            _bullets[i] = Find<UiButton>(FirstBulletId + (uint)i);
            _pointers[i] = Find<UiElement>(FirstPointerId + (uint)i);
        }

        _gradCircle = Find<UiElement>(GradCircleId);
        _shadeScroll = Find<UiScrollbar>(ShadeScrollId);
        _zoomIn = Find<UiButton>(ZoomInId);
        _zoomOut = Find<UiButton>(ZoomOutId);
        _rotateClock = Find<UiButton>(RotateClockId);
        _rotateCounterClock = Find<UiButton>(RotateCounterClockId);
        if (Find<UiViewport>(CharGenMainScreen.AppearanceViewportId) is not null)
        {
            _view = Main.NewView3D();
            if (_view is not null)
            {
                _attachment = Main.AttachView(CharGenMainScreen.AppearanceViewportId, _view);
            }
        }

        _view?.SetPlayerHeading(CreaturePreview.CharGenHeading);
        _currentPosition = _targetPosition = CharGenRules.FaceCamera(Main.State.Heritage);
        _view?.SetCamera(_currentPosition, Vector3.Zero);
        _zoomedIn = false;
        SetChoice(CharGenAppearanceType.Face);
        var state = Main.State;
        if (state.Gender is 1 or 2)
        {
            // gmCGAppearancePage::InitializePage sets the opposite gender before its first update.
            state.SetGender(state.Gender == 1 ? 2u : 1u);
            Update();
        }

        if (Root.Visible)
        {
            _view?.Update(Manager.Now);
        }

        SetupParts();
        SetSelection(_part);
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        if (Main.View3DData is null)
        {
            Manager.LogOnce(
                "chargen:appearance-data",
                LogSeverity.Warning,
                "TODO(retailui-chargen-view): the host gave character creation no 3D view data (CharGenSetup.View3D); the appearance page has no colour swatches and no character (placeholder)");
        }
    }

    /// <summary>The page's 3D character, or null when the host gave none.</summary>
    public Cg3DViewModel? View => _view;

    /// <inheritdoc/>
    public override string? PlaceholderText => _view is null ? "placeholder: the 3D character view needs the CharGen view data, which this host did not give" : null;

    /// <summary>Which half is showing (<c>m_eCurType</c>).</summary>
    public CharGenAppearanceType CurrentType => _type;

    /// <summary>The part selected (<c>m_eCurPart</c>).</summary>
    public CharGenAppearancePart CurrentPart => _part;

    /// <summary>The colour spot chosen for the part (<c>m_iCurColor</c>), or -1.</summary>
    public int CurrentColor => _color;

    /// <summary>True while the camera is zoomed in on the face (<c>m_bZoomedIn</c>).</summary>
    public bool IsZoomedIn => _zoomedIn;

    /// <summary>True while the camera is moving to its target.</summary>
    public bool IsZooming => _animating;

    /// <summary>True while the character is turning.</summary>
    public bool IsRotating => _rotating;

    /// <summary>The character's heading as the page last set it, in degrees.</summary>
    public float Heading => _heading;

    /// <summary>The camera the page last gave the view, in AC space.</summary>
    public Vector3 CameraPosition => _currentPosition;

    /// <summary>The camera the zoom is moving to.</summary>
    public Vector3 CameraTarget => _targetPosition;

    /// <summary>True when a colour picture is still waiting for an image the host decodes on a worker (it is made on a later frame).</summary>
    public bool ImagesPending => _imagesPending;

    /// <summary>The Female button.</summary>
    public UiButton? FemaleButton => _female;

    /// <summary>The Male button.</summary>
    public UiButton? MaleButton => _male;

    /// <summary>The Face tab.</summary>
    public UiButton? FaceButton => _face;

    /// <summary>The Clothes tab.</summary>
    public UiButton? ClothesButton => _clothes;

    /// <summary>The shade slider.</summary>
    public UiScrollbar? ShadeScroll => _shadeScroll;

    /// <summary>The disk under the slider.</summary>
    public UiElement? GradCircle => _gradCircle;

    /// <summary>The zoom-in button.</summary>
    public UiButton? ZoomInButton => _zoomIn;

    /// <summary>The zoom-out button.</summary>
    public UiButton? ZoomOutButton => _zoomOut;

    /// <summary>The clockwise turn button.</summary>
    public UiButton? RotateClockButton => _rotateClock;

    /// <summary>The counter-clockwise turn button.</summary>
    public UiButton? RotateCounterClockButton => _rotateCounterClock;

    /// <summary>What the page holds for <paramref name="part"/>.</summary>
    public CharGenPartChoice Choice(CharGenAppearancePart part) => _choices[(int)part - 1];

    /// <summary>The spinner button of <paramref name="part"/>.</summary>
    public UiButton? Spinner(CharGenAppearancePart part) => _spinners[(int)part - 1];

    /// <summary>The left (<see cref="LeftArrowId"/>) or right (<see cref="RightArrowId"/>) arrow of <paramref name="part"/>'s spinner.</summary>
    public UiButton? Arrow(CharGenAppearancePart part, uint arrowId) => Spinner(part)?.GetChildRecursive(arrowId) as UiButton;

    /// <summary>Colour spot <paramref name="index"/> (0 to 8).</summary>
    public UiButton? Bullet(int index) => _bullets[index];

    /// <summary>The ring that marks colour spot <paramref name="index"/>.</summary>
    public UiElement? Pointer(int index) => _pointers[index];

    /// <summary>The colour spot <paramref name="index"/> shows (red, green, blue), or null when the page has no colour for it.</summary>
    public (byte R, byte G, byte B)? SpotColor(int index) => _spotColors[index];

    /// <summary>The group of spinners of <paramref name="type"/>'s half.</summary>
    public UiElement? Choices(CharGenAppearanceType type) => type == CharGenAppearanceType.Face ? _faceChoices : _clothesChoices;

    // ---------------------------------------------------------------- The state's parts

    private Lifestone.Data.CharacterCreation.CharGenGender? Gender() =>
        Main.Catalog.Data.TryGetHeritage(Main.State.Heritage, out var hg) && hg.TryGetGender(Main.State.Gender, out var sx) ? sx : null;

    /// <summary>
    /// <c>gmCGAppearancePage::SetupParts</c>: for each part the number of styles and colours the heritage and gender have, and the style, colour and shade
    /// the state holds.
    /// </summary>
    public void SetupParts()
    {
        var state = Main.State;
        var sx = Gender();
        Set(CharGenAppearancePart.Hair, sx?.HairStyles.Count ?? 0, state.HairStyle, sx?.HairColors.Count ?? 0, state.HairColor, state.HairShade);
        Set(CharGenAppearancePart.Eyes, sx?.EyeStrips.Count ?? 0, state.EyesStrip, sx?.EyeColors.Count ?? 0, state.EyeColor, 0.0);
        Set(CharGenAppearancePart.Nose, sx?.NoseStrips.Count ?? 0, state.NoseStrip, 0, -1, 0.0);
        Set(CharGenAppearancePart.Mouth, sx?.MouthStrips.Count ?? 0, state.MouthStrip, 0, -1, 0.0);
        Set(CharGenAppearancePart.Headgear, sx?.Headgears.Count ?? 0, state.HeadgearStyle, state.HeadgearColors.Count, state.HeadgearColor, state.HeadgearShade);
        Set(CharGenAppearancePart.Shirt, sx?.Shirts.Count ?? 0, state.ShirtStyle, state.ShirtColors.Count, state.ShirtColor, state.ShirtShade);
        Set(CharGenAppearancePart.Trousers, sx?.Pants.Count ?? 0, state.TrousersStyle, state.TrousersColors.Count, state.TrousersColor, state.TrousersShade);
        Set(CharGenAppearancePart.Footwear, sx?.Footwear.Count ?? 0, state.FootwearStyle, state.FootwearColors.Count, state.FootwearColor, state.FootwearShade);
    }

    private void Set(CharGenAppearancePart part, int styles, int current, int colors, int color, double shade)
    {
        var choice = Choice(part);
        choice.NumChoices = styles;
        choice.CurrentChoice = current;
        choice.NumColors = colors;
        choice.CurrentColor = color;
        choice.Shade = shade;
    }

    /// <summary>
    /// <c>gmCGAppearancePage::Update</c>: the gender buttons, the heritage's controls and texts (see the class remarks), the parts set up again and the
    /// current part selected; when the page shows, the character composed and the camera and animation put as the half calls for.
    /// </summary>
    public override void Update()
    {
        var state = Main.State;
        _female?.SetState(state.Gender == 2 ? CharGenRules.SelectedState : CharGenRules.UnselectedState);
        _male?.SetState(state.Gender == 1 ? CharGenRules.SelectedState : CharGenRules.UnselectedState);
        Vector3 camera;
        if (state.Heritage == 6)
        {
            if (_lastHeritage != 6)
            {
                state.RandomizeAppearance(false);
                state.RandomizeClothing(true);
            }

            _lastHeritage = state.Heritage;
            Restricted(gearKnight: true);
            SetChoice(CharGenAppearanceType.Face);
            SetSelection(CharGenAppearancePart.Hair);
            camera = CharGenRules.FaceCamera(6);
        }
        else if (CharGenRules.IsOlthoi(state.Heritage))
        {
            Restricted(gearKnight: false);
            SetChoice(CharGenAppearanceType.Face);
            SetSelection(CharGenAppearancePart.Hair);
            camera = CharGenRules.FaceCamera(state.Heritage);
        }
        else
        {
            if (_lastHeritage == 6)
            {
                state.RandomizeAppearance(false);
                state.RandomizeClothing(true);
            }

            _lastHeritage = state.Heritage;
            Spinner(CharGenAppearancePart.Hair)?.SetStringInfoWithFont(Pregame("ID_CharGen_HairStyle"), 0, 1);
            Spinner(CharGenAppearancePart.Eyes)?.SetStringInfoWithFont(Pregame("ID_CharGen_Eyes"), 0, 1);
            Ghost(CharGenAppearancePart.Eyes, false);
            Spinner(CharGenAppearancePart.Skin)?.MoveTo(0, 180);
            Spinner(CharGenAppearancePart.Skin)?.SetStringInfoWithFont(Pregame("ID_CharGen_Skin"), 0, 1);
            _clothes?.SetVisible(true);
            Spinner(CharGenAppearancePart.Nose)?.SetVisible(true);
            Spinner(CharGenAppearancePart.Mouth)?.SetVisible(true);
            if (_type == CharGenAppearanceType.Face && !RetailHeadgear)
            {
                HoldHeadgear(); // the page was shown again, or Random or a gender button changed the state's headgear
            }

            // Retail puts the camera at the face whatever the half showing; Lifestone keeps it where the zoom state says (UiRetailGlitches.CharGenAppearanceCamera).
            camera = RetailCamera || _zoomedIn ? CharGenRules.FaceCamera(_lastHeritage) : CharGenRules.BodyCamera(_lastHeritage);
        }

        _targetPosition = _currentPosition = camera;
        _view?.SetCamera(camera, Vector3.Zero);
        SetupParts();
        SetSelection(_part);
        if (Root.Visible)
        {
            _view?.Update(Manager.Now);
            _view?.SetCamera(_targetPosition, Vector3.Zero);
            if (_zoomedIn)
            {
                _view?.StopAnimation();
            }
            else
            {
                _view?.StartAnimation();
            }
        }
    }

    /// <summary>The Gear Knights' and the Olthoi's controls: no clothes, nose or mouth, other texts, the skin spinner moved up.</summary>
    private void Restricted(bool gearKnight)
    {
        _clothes?.SetVisible(false);
        Spinner(CharGenAppearancePart.Nose)?.SetVisible(false);
        Spinner(CharGenAppearancePart.Mouth)?.SetVisible(false);
        var stem = gearKnight ? "ID_CharGen_GearText_" : "ID_CharGen_OlthoiText_";
        Spinner(CharGenAppearancePart.Hair)?.SetStringInfoWithFont(Pregame(stem + "HairButton"), 0, 1);
        Spinner(CharGenAppearancePart.Eyes)?.SetStringInfoWithFont(Pregame(stem + "EyesButton"), 0, 1);
        Ghost(CharGenAppearancePart.Eyes, true);
        Spinner(CharGenAppearancePart.Skin)?.MoveTo(0, 90);
        Spinner(CharGenAppearancePart.Skin)?.SetStringInfoWithFont(Pregame(stem + "SkinButton"), 0, 1);
    }

    private void Ghost(CharGenAppearancePart part, bool ghosted)
    {
        Arrow(part, LeftArrowId)?.SetBoolProperty(UiPropertyIds.ButtonGhosted, ghosted);
        Arrow(part, RightArrowId)?.SetBoolProperty(UiPropertyIds.ButtonGhosted, ghosted);
    }

    /// <summary>
    /// The Random button on this page (<c>gmCharGenMainUI::DoRandom</c>, case 4): the clothes half draws the clothing again, the face half the whole
    /// appearance, then the page is updated.
    /// </summary>
    public void Random()
    {
        if (_type == CharGenAppearanceType.Clothes)
        {
            Main.State.RandomizeClothing(true);
        }
        else
        {
            Main.State.RandomizeAppearance(false);
        }

        Update();
    }

    // ---------------------------------------------------------------- Face and clothes

    /// <summary>
    /// <c>gmCGAppearancePage::SetChoice</c>: the half showing. The face half holds the headgear style and takes the headgear off, shows its spinners and
    /// zooms in; the clothes half gives the held headgear back (when it is a style the list has), shows its spinners and zooms out.
    /// </summary>
    public void SetChoice(CharGenAppearanceType type)
    {
        _type = type;
        _clothesChoices?.SetVisible(false);
        _faceChoices?.SetVisible(false);
        var state = Main.State;
        if (type == CharGenAppearanceType.Face)
        {
            _face?.SetState(CharGenRules.SelectedState);
            _clothes?.SetState(CharGenRules.UnselectedState);
            HoldHeadgear();
            _faceChoices?.SetVisible(true);
            ZoomIn();
        }
        else
        {
            _startPosition = _currentPosition;
            _face?.SetState(CharGenRules.UnselectedState);
            _clothes?.SetState(CharGenRules.SelectedState);
            GiveBackHeadgear();
            _clothesChoices?.SetVisible(true);
            ZoomOut();
        }
    }

    private bool RetailCamera => (Manager.Services.RetailGlitches & UiRetailGlitches.CharGenAppearanceCamera) != 0;

    private bool RetailHeadgear => (Manager.Services.RetailGlitches & UiRetailGlitches.CharGenHeldHeadgear) != 0;

    /// <summary>
    /// The face half takes the headgear off the character and holds its style. Lifestone keeps what it already holds while the state's headgear is none (the half
    /// is shown again) and holds the state's headgear when something else changed it (the summary page's Random); retail holds whatever the state has.
    /// </summary>
    private void HoldHeadgear()
    {
        var state = Main.State;
        if (!RetailHeadgear && _headgearHeld && state.HeadgearStyle == -1)
        {
            return;
        }

        _heldHeadgear = state.HeadgearStyle;
        state.SetHeadgearStyle(-1);
        _headgearHeld = true;
    }

    /// <summary>
    /// The page is being left (another page was asked for): in the face half the held headgear goes back on the character, so it reaches the other pages and the
    /// created character (Lifestone's fix; retail drops it, <see cref="UiRetailGlitches.CharGenHeldHeadgear"/>).
    /// </summary>
    public void PageLeft()
    {
        if (!RetailHeadgear)
        {
            GiveBackHeadgear();
        }
    }

    /// <summary>
    /// The held headgear goes back on the character when it is a style the list has: the clothes half shows (retail's only give-back) or, Lifestone's fix
    /// (<see cref="UiRetailGlitches.CharGenHeldHeadgear"/>), the page is left in the face half. Nothing is given back that was not held.
    /// </summary>
    public void GiveBackHeadgear()
    {
        if (!RetailHeadgear && !_headgearHeld)
        {
            return;
        }

        _headgearHeld = false;
        if (_heldHeadgear > -2 && _heldHeadgear < Choice(CharGenAppearancePart.Headgear).NumChoices)
        {
            Main.State.SetHeadgearStyle(_heldHeadgear);
        }
    }

    /// <summary><c>gmCGAppearancePage::ZoomIn</c>: the camera moves to the face (over 0.6 s), the zoom-in button shows pressed and the character holds its rest pose.</summary>
    public void ZoomIn()
    {
        if (_zoomedIn)
        {
            _zoomIn?.SetState(UiStateIds.Highlight);
            return;
        }

        _startPosition = _currentPosition;
        _targetPosition = CharGenRules.FaceCamera(Main.State.Heritage);
        _animating = true;
        _animDuration = -0.1;
        _zoomIn?.SetState(UiStateIds.Highlight);
        _zoomOut?.SetState(UiStateIds.Normal);
        _view?.StopAnimation();
        _zoomedIn = true;
    }

    /// <summary><c>gmCGAppearancePage::ZoomOut</c>: the camera moves to the whole figure, the zoom-out button shows pressed and the view starts retail's animation loop at once (<c>gmCG3DView::StartAnimation</c>).</summary>
    public void ZoomOut()
    {
        if (!_zoomedIn)
        {
            _zoomOut?.SetState(UiStateIds.Highlight);
            return;
        }

        _startPosition = _currentPosition;
        _targetPosition = CharGenRules.BodyCamera(Main.State.Heritage);
        _animating = true;
        _animDuration = -0.1;
        _zoomIn?.SetState(UiStateIds.Normal);
        _zoomOut?.SetState(UiStateIds.Highlight);
        _view?.StartAnimation();
        _zoomedIn = false;
    }

    /// <summary>
    /// <c>gmCGAppearancePage::DoZoomAnimation</c> at the clock <paramref name="now"/>: the first frame of a zoom starts its 0.6 s; the camera is put
    /// at the linear mix of its start and target, and the zoom ends at the target.
    /// </summary>
    public void DoZoomAnimation(double now)
    {
        if (_animDuration < 0.0)
        {
            _animDuration = ZoomSeconds;
            _animStart = now;
        }

        var elapsed = now - _animStart;
        if (elapsed >= _animDuration)
        {
            _animating = false;
            elapsed = _animDuration;
        }

        var t = (float)(elapsed / _animDuration);
        _currentPosition = _animating ? _startPosition + ((_targetPosition - _startPosition) * t) : _targetPosition; // the last frame lands on the target exactly
        _view?.SetCamera(_currentPosition, Vector3.Zero);
    }

    /// <summary>
    /// <c>gmCGAppearancePage::Rotate</c>: the turn button <paramref name="direction"/> (1 clockwise, 2 counter-clockwise) was pressed: the same
    /// direction again stops the turn; otherwise the other button is released and the character starts turning.
    /// </summary>
    public void Rotate(int direction)
    {
        if (_rotating && direction == _rotateDirection)
        {
            _rotating = false;
            return;
        }

        if (direction == 1)
        {
            _rotateCounterClock?.SetState(UiStateIds.Normal);
        }
        else if (direction == 2)
        {
            _rotateClock?.SetState(UiStateIds.Normal);
        }

        _rotateDirection = direction;
        _lastRotateTime = -1.0;
        _rotating = true;
    }

    /// <summary><c>gmCGAppearancePage::DoRotation</c> at the clock <paramref name="now"/>: the heading moves by the time since the last frame over 5 s of a whole turn, wrapped to 0-360.</summary>
    public void DoRotation(double now)
    {
        if (_lastRotateTime < 0.0)
        {
            _lastRotateTime = now;
        }

        var delta = (float)((now - _lastRotateTime) / TurnSeconds * 360.0);
        _heading = _rotateDirection == 1 ? _heading + delta : _heading - delta;
        if (_heading < 0f)
        {
            _heading += 360f;
        }

        if (_heading > 360f)
        {
            _heading -= 360f;
        }

        _lastRotateTime = now;
        _view?.SetPlayerHeading(_heading);
    }

    /// <summary>Each frame (global message 3): a zoom and a turn in progress move on (<c>gmCGAppearancePage::ListenToGlobalMessage</c>), and the character is stepped.</summary>
    void IUiGlobalListener.ListenToGlobalMessage(uint messageId, int data)
    {
        if (IsDisposed || messageId != UiGlobalMessageIds.Frame)
        {
            return;
        }

        if (_animating)
        {
            DoZoomAnimation(Manager.Now);
        }

        if (_rotating)
        {
            DoRotation(Manager.Now);
        }

        if (_imagesPending)
        {
            DoColorSpots(_partIndex);
            DoGradDisk(_part == CharGenAppearancePart.Eyes);
        }

        if (Root.Visible)
        {
            _view?.Advance(Manager.Now);
        }
    }

    // ---------------------------------------------------------------- Selection, colour and shade

    /// <summary>
    /// <c>gmCGAppearancePage::SetSelection</c>: <paramref name="part"/> becomes the selected part: its spinner takes the selected look, the wheel is
    /// filled with its colours (sampled from its palettes), the shade slider shows its shade (the eyes have none) and its current colour is set.
    /// </summary>
    public void SetSelection(CharGenAppearancePart part)
    {
        var state = Main.State;
        var data = Main.View3DData;
        var sx = Gender();
        _part = part;
        _lastHeritage = state.Heritage;
        for (var i = 0; i < SpotCount; i++)
        {
            _bullets[i]?.SetVisible(true);
            _pointers[i]?.SetVisible(false);
            _spotColors[i] = null;
        }

        _currentSelection?.SetState(UiStateIds.Normal);
        var palSets = new List<uint>();
        int numPalSets;
        var partIndex = (int)part - 1;
        int curColor;
        double shade;
        switch (part)
        {
            case CharGenAppearancePart.Hair:
                palSets.AddRange(sx?.HairColors ?? []);
                curColor = state.HairColor == -1 ? 0 : state.HairColor;
                shade = state.HairShade;
                break;
            case CharGenAppearancePart.Eyes:
                palSets.AddRange(sx?.EyeColors ?? []);
                curColor = state.EyeColor;
                shade = 0.5;
                break;
            case CharGenAppearancePart.Nose:
            case CharGenAppearancePart.Mouth:
            case CharGenAppearancePart.Skin:
                palSets.Add(sx?.SkinPalSetId ?? 0);
                curColor = 0;
                shade = state.SkinShade;
                break;
            case CharGenAppearancePart.Headgear:
                palSets.AddRange(state.HeadgearColors.Select(c => c.PalSetId));
                curColor = state.HeadgearColor;
                shade = state.HeadgearShade;
                break;
            case CharGenAppearancePart.Shirt:
                palSets.AddRange(state.ShirtColors.Select(c => c.PalSetId));
                curColor = state.ShirtColor;
                shade = state.ShirtShade;
                break;
            case CharGenAppearancePart.Trousers:
                palSets.AddRange(state.TrousersColors.Select(c => c.PalSetId));
                curColor = state.TrousersColor;
                shade = state.TrousersShade;
                break;
            default:
                palSets.AddRange(state.FootwearColors.Select(c => c.PalSetId));
                curColor = state.FootwearColor;
                shade = state.FootwearShade;
                break;
        }

        numPalSets = Math.Min(palSets.Count, SpotCount);
        _currentSelection = Spinner(part);
        if (data is not null)
        {
            for (var i = 0; i < numPalSets; i++)
            {
                _spotColors[i] = part == CharGenAppearancePart.Eyes
                    ? data.ColorFromPalette(palSets[i], SampleEntries[1])
                    : Average(data, palSets[i], SampleEntries[partIndex]);
            }
        }

        _color = curColor;
        _partIndex = partIndex;
        Choice(part).NumColors = numPalSets;
        _currentSelection?.SetState(UiStateIds.Highlight);
        DoColorSpots(partIndex);
        if (part == CharGenAppearancePart.Eyes)
        {
            DoGradDisk(true);
            _shadeScroll?.SetVisible(false);
        }
        else
        {
            DoGradDisk(false);
            _shadeScroll?.SetVisible(true);
            SetShade(shade);
        }

        SetColor(curColor);
    }

    /// <summary>The colour of palette entry <paramref name="entry"/> averaged over the palettes of PalSet <paramref name="palSetId"/> (integer division, as retail's), or null when the set has none.</summary>
    private static (byte R, byte G, byte B)? Average(Cg3DViewData data, uint palSetId, int entry)
    {
        var palettes = data.PalSetPalettes(palSetId);
        if (palettes.Count == 0)
        {
            return null;
        }

        int r = 0, g = 0, b = 0;
        foreach (var palette in palettes)
        {
            if (data.ColorFromPalette(palette, entry) is { } c)
            {
                r += c.R;
                g += c.G;
                b += c.B;
            }
        }

        return ((byte)(r / palettes.Count), (byte)(g / palettes.Count), (byte)(b / palettes.Count));
    }

    /// <summary>
    /// <c>gmCGAppearancePage::SetColor</c>: colour spot <paramref name="color"/> is chosen for the selected part: the old ring hides and the new one
    /// shows, the state takes the colour, the disk is made again and the character composed.
    /// </summary>
    public void SetColor(int color)
    {
        if (_color != -1)
        {
            _pointers[_color]?.SetVisible(false);
        }

        if (color != -1)
        {
            _pointers[color]?.SetVisible(true);
        }

        _color = color;
        var plug = false;
        var state = Main.State;
        switch (_part)
        {
            case CharGenAppearancePart.Hair:
                state.SetHairColor(color);
                break;
            case CharGenAppearancePart.Eyes:
                state.SetEyeColor(color);
                plug = true;
                break;
            case CharGenAppearancePart.Headgear:
                state.HeadgearColor = color;
                break;
            case CharGenAppearancePart.Shirt:
                state.ShirtColor = color;
                break;
            case CharGenAppearancePart.Trousers:
                state.TrousersColor = color;
                break;
            case CharGenAppearancePart.Footwear:
                state.FootwearColor = color;
                break;
        }

        DoGradDisk(plug);
        if (Root.Visible)
        {
            _view?.Update(Manager.Now);
        }
    }

    /// <summary>
    /// <c>gmCGAppearancePage::SetShade</c>: the slider shows <paramref name="shade"/> and the selected part takes it (hair: the hair shade; nose, mouth and
    /// skin: the skin shade; a garment: its own shade); the character is composed when the page shows.
    /// </summary>
    public void SetShade(double shade)
    {
        _shadeScroll?.SetFloatProperty(UiPropertyIds.ScrollbarPosition, (float)shade);
        var state = Main.State;
        switch (_part)
        {
            case CharGenAppearancePart.Hair:
                state.SetHairShade(shade);
                break;
            case CharGenAppearancePart.Nose:
            case CharGenAppearancePart.Mouth:
            case CharGenAppearancePart.Skin:
                state.SetSkinShade(shade);
                break;
            case CharGenAppearancePart.Headgear:
                state.HeadgearShade = shade;
                break;
            case CharGenAppearancePart.Shirt:
                state.ShirtShade = shade;
                break;
            case CharGenAppearancePart.Trousers:
                state.TrousersShade = shade;
                break;
            case CharGenAppearancePart.Footwear:
                state.FootwearShade = shade;
                break;
        }

        Choice(_part).Shade = shade;
        if (Root.Visible)
        {
            _view?.Update(Manager.Now);
        }
    }

    /// <summary>
    /// <c>gmCGAppearancePage::DoColorSpots</c>: each of the nine spots shows the blank bullet with its black replaced by the spot's colour, or the empty
    /// bullet when the part has no such colour. A bullet the host has not decoded yet is made on a later frame.
    /// </summary>
    public void DoColorSpots(int partIndex)
    {
        var data = Main.View3DData;
        var images = Manager.Services.Images;
        _imagesPending = false;
        if (data is null || images is null)
        {
            return;
        }

        if (!Fetch(images, data, UiAssetIds.ColorBullet, out var blank) || !Fetch(images, data, UiAssetIds.ColorEmpty, out var cap))
        {
            return;
        }

        for (var i = 0; i < SpotCount; i++)
        {
            if (_bullets[i] is not { } bullet)
            {
                continue;
            }

            var colored = i < _choices[partIndex].NumColors && _spotColors[i] is not null;
            if (colored)
            {
                var (r, g, b) = _spotColors[i]!.Value;
                bullet.SetGeneratedImage(CharGenSwatches.ReplaceBlack(blank!, r, g, b));
            }
            else
            {
                bullet.SetGeneratedImage(CharGenSwatches.Copy(cap!));
            }
        }
    }

    /// <summary>
    /// <c>gmCGAppearancePage::DoGradDisk</c>: the disk under the slider is the gradient ring multiplied by the current colour, or (<paramref name="usePlug"/>,
    /// the eyes) the plug picture.
    /// </summary>
    public void DoGradDisk(bool usePlug)
    {
        var data = Main.View3DData;
        var images = Manager.Services.Images;
        if (data is null || images is null || _gradCircle is null)
        {
            return;
        }

        if (!Fetch(images, data, usePlug ? UiAssetIds.GradientPlug : UiAssetIds.ColorRing, out var source))
        {
            return;
        }

        if (usePlug)
        {
            _gradCircle.SetGeneratedImage(CharGenSwatches.Copy(source!));
            return;
        }

        // Retail reads the wheel entry of the current colour even when there is none (-1); that reads outside the table, so the disk stays uncoloured.
        var (r, g, b) = _color is >= 0 and < SpotCount && _spotColors[_color] is { } c ? c : ((byte)255, (byte)255, (byte)255);
        if (_color is < 0 or >= SpotCount)
        {
            Manager.LogOnce("chargen:appearance-no-color", LogSeverity.Warning, "TODO(retailui-chargen-view): the shade disk is drawn for no colour; retail reads outside its wheel table there, so the disk is left uncoloured");
        }

        _gradCircle.SetGeneratedImage(CharGenSwatches.Multiply(source!, r, g, b));
    }

    private bool Fetch(IUiImageSource images, Cg3DViewData data, uint assetEnum, out UiImage? image)
    {
        image = null;
        var did = data.UiAssetDid(assetEnum);
        if (did == 0)
        {
            Manager.LogOnce($"chargen:appearance-asset:{assetEnum:X8}", LogSeverity.Error, $"the UIASSET enum 0x{assetEnum:X8} names no image; the colour wheel keeps the layout's art (placeholder)");
            return false;
        }

        switch (images.TryGetImage(did, out image))
        {
            case UiImageLookup.Ready:
                return true;
            case UiImageLookup.Pending:
                _imagesPending = true;
                return false;
            default:
                Manager.LogOnce($"chargen:appearance-asset:{assetEnum:X8}", LogSeverity.Error, $"the colour wheel's image 0x{did:X8} does not decode; the wheel keeps the layout's art (placeholder)");
                return false;
        }
    }

    // ---------------------------------------------------------------- Messages

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ScrollbarPosition && message.ElementId == ShadeScrollId)
        {
            SetShade(Math.Clamp(message.Param1 * 0.001, 0.0, 1.0));
            return UiListenResult.Default;
        }

        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return UiListenResult.Default;
        }

        var id = message.ElementId;
        switch (id)
        {
            case FemaleButtonId:
                Main.State.SetGender(2);
                Update();
                break;
            case MaleButtonId:
                Main.State.SetGender(1);
                Update();
                break;
            case FaceButtonId:
                SetChoice(CharGenAppearanceType.Face);
                SetSelection(CharGenAppearancePart.Hair);
                break;
            case ClothesButtonId:
                SetChoice(CharGenAppearanceType.Clothes);
                SetSelection(CharGenAppearancePart.Headgear);
                break;
            case ZoomInId:
                ZoomIn();
                break;
            case ZoomOutId:
                ZoomOut();
                break;
            case RotateClockId:
                Rotate(1);
                break;
            case RotateCounterClockId:
                Rotate(2);
                break;
            case LeftArrowId:
            case RightArrowId:
                if (PartOfSpinner(message.Element.Parent?.ElementId ?? 0) is { } arrowPart)
                {
                    Step(arrowPart, id == LeftArrowId ? -1 : 1);
                }

                break;
            default:
                if (PartOfSpinner(id) is { } spinnerPart)
                {
                    Step(spinnerPart, 0);
                }
                else if (id >= FirstBulletId && id < FirstBulletId + SpotCount)
                {
                    var spot = (int)(id - FirstBulletId);
                    if (_choices[_partIndex].NumColors > spot)
                    {
                        SetColor(spot);
                    }
                }

                break;
        }

        return UiListenResult.Default;
    }

    private static CharGenAppearancePart? PartOfSpinner(uint elementId)
    {
        var index = Array.IndexOf(SpinnerIds, elementId);
        return index < 0 ? null : (CharGenAppearancePart)(index + 1);
    }

    /// <summary>
    /// A spinner's own click (<paramref name="delta"/> 0: the style is kept in range) or an arrow's (one step back or on, wrapping; the headgear's range
    /// also holds -1, no headgear): the style goes to the state and the part is selected again, so its colours and shade show.
    /// </summary>
    public void Step(CharGenAppearancePart part, int delta)
    {
        var choice = Choice(part);
        var state = Main.State;
        var current = choice.CurrentChoice + delta;
        var count = choice.NumChoices;
        if (part == CharGenAppearancePart.Headgear)
        {
            if (current >= count)
            {
                current = -1;
            }
            else if (current < 0 && current != -1)
            {
                current = count - 1;
            }
        }
        else if (current >= count)
        {
            current = 0;
        }
        else if (current < 0)
        {
            current = count - 1;
        }

        choice.CurrentChoice = current;
        switch (part)
        {
            case CharGenAppearancePart.Hair:
                state.HairStyle = current;
                break;
            case CharGenAppearancePart.Eyes:
                state.EyesStrip = current;
                break;
            case CharGenAppearancePart.Nose:
                state.NoseStrip = current;
                break;
            case CharGenAppearancePart.Mouth:
                state.MouthStrip = current;
                break;
            case CharGenAppearancePart.Headgear:
                state.SetHeadgearStyle(current);
                TakeGarment(choice, state.HeadgearColor, state.HeadgearColors.Count, state.HeadgearShade);
                break;
            case CharGenAppearancePart.Shirt:
                state.SetShirtStyle(current);
                TakeGarment(choice, state.ShirtColor, state.ShirtColors.Count, state.ShirtShade);
                break;
            case CharGenAppearancePart.Trousers:
                state.SetTrousersStyle(current);
                TakeGarment(choice, state.TrousersColor, state.TrousersColors.Count, state.TrousersShade);
                break;
            case CharGenAppearancePart.Footwear:
                state.SetFootwearStyle(current);
                TakeGarment(choice, state.FootwearColor, state.FootwearColors.Count, state.FootwearShade);
                break;
        }

        SetSelection(part);
    }

    private static void TakeGarment(CharGenPartChoice choice, int color, int colors, double shade)
    {
        choice.CurrentColor = color;
        choice.NumColors = colors;
        choice.Shade = shade;
    }

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        _attachment?.Dispose();
    }
}
