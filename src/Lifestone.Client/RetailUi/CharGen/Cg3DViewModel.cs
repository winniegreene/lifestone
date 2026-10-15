using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// Character creation's 3D character, engine-agnostic (retail's <c>gmCG3DView</c>, the object each of the appearance and summary pages makes for its own
/// viewport element): the character of a <see cref="CharGenState"/> composed (<see cref="CharGenAppearance.Compose"/>) and posed by a
/// <see cref="CreaturePreview"/>, the heritage's backdrop, the camera the page puts it at, the light, and the heading. The Godot layer draws it
/// (<c>CharGenViewportBinder</c>) into the viewport element's hole through the portal track's creature-mode view.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCG3DView::Update</c> (11.4186 0x004EE9D0, 11.6096 0x004EF660), <c>Initialize</c> (0x004EE760, 0x004EF3F0), <c>SetCamera</c>
/// (0x004EE5B0, 0x004EF240), <c>SetPlayerHeading</c> (0x004EE590, 0x004EF220), <c>StartAnimation</c> (0x004EE600, 0x004EF290) and <c>StopAnimation</c>
/// (0x004EE640, 0x004EF2D0). <see cref="Update"/> composes the character from the state's current choices and applies it to the preview (a new body
/// makes a new object); the view's light is one distant light of intensity 2 towards (0.3, 1.9, 0.65) and the ambient 0.3 of creature mode
/// (<see cref="LightDirection"/>, <see cref="CreaturePreview.LightIntensity"/>).
/// </para>
/// <para>
/// <b>The animation (owner decision D60, which amends D54).</b> <c>StartAnimation</c> clears the object's sequence and loops the view's animation (the
/// heritage's CharGen animation, 33 frames for the human heritages) from frame 0 at 30 fps; <c>StopAnimation</c> clears it and holds frame 0 of the
/// heritage's rest animation at 0 fps. The pages make the calls: the summary page when it is initialised (<c>gmCGSummaryPage::InitializePage</c>, 11.6096
/// 0x0047BFB0), the appearance page's <c>ZoomOut</c> (0x0047D400) and, when the page is shown or updated, its <c>Update</c> (0x0047ECA0: a start when
/// zoomed out, a stop when zoomed in); <c>ZoomIn</c> (0x0047D2B0) stops. The five idle emotes retail loads are never played, as in retail
/// (<see cref="PreviewAnimations.IdleEmotes"/>).
/// </para>
/// <para>
/// <b>The backdrop</b> is <c>m_pbgObject</c>: the heritage's environment Setup, a single static part at the origin (no heritage's has a default
/// animation or motion), drawn behind the character at its resting placement.
/// </para>
/// </remarks>
public sealed class Cg3DViewModel
{
    /// <summary>The light's direction as the view gives it (<c>UIElement_Viewport::SetLight(DISTANT_LIGHT, 2.0, (0.3, 1.9, 0.65))</c>; portal space's points the other way in Y).</summary>
    public static Vector3 LightDirection => CreaturePreview.LightDirection;

    /// <summary>Creature mode's ambient light (<c>CreatureMode::CreatureMode</c>: 0.3 white), the same as portal space's.</summary>
    public static readonly Vector3 Ambient = new(0.3f, 0.3f, 0.3f);

    private readonly CharGenState _state;
    private readonly Cg3DViewData _data;
    private readonly ILogSink _log;
    private CreaturePreview? _preview;
    private PartDraw[] _backdrop = [];
    private uint _backdropSetupId;
    private double _now;
    private bool _animating;
    private bool _pinned;
    private int _version;
    private bool _composeProblemLogged;

    /// <summary>The view of <paramref name="state"/>'s character, empty until <see cref="Update"/>; the camera starts at the origin, the heading at 180 degrees.</summary>
    /// <param name="state">The character.</param>
    /// <param name="data">What it reads from the dats.</param>
    /// <param name="log">Where problems go (an ObjDesc step that fails is loud, never fatal: retail ignores it).</param>
    public Cg3DViewModel(CharGenState state, Cg3DViewData data, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(log);
        _state = state;
        _data = data;
        _log = log;
        Heading = CreaturePreview.CharGenHeading;
    }

    /// <summary>The character being shown.</summary>
    public CharGenState State => _state;

    /// <summary>The posed character, or null until the first <see cref="Update"/> that had a heritage and a gender.</summary>
    public CreaturePreview? Preview => _preview;

    /// <summary>The camera's position, in AC space (<c>m_vectPosition</c>).</summary>
    public Vector3 CameraPosition { get; private set; }

    /// <summary>The camera's direction as retail's <c>CreatureMode::SetCameraDirection_Degrees</c> takes it (<c>m_vectDirection</c>).</summary>
    public Vector3 CameraDirection { get; private set; }

    /// <summary>The character's heading in degrees (<c>m_fHeading</c>).</summary>
    public float Heading { get; private set; }

    /// <summary>Changes whenever anything the host draws changed: the character, its pose, the camera, the heading.</summary>
    public int Version => _version + (_preview?.PoseVersion ?? 0);

    /// <summary>True while the view loops its animation (after <see cref="StartAnimation"/>, until <see cref="StopAnimation"/>); false when it holds the rest pose.</summary>
    public bool IsAnimating => _animating;

    /// <summary>True while a capture holds the animation at a chosen time (<see cref="PinAnimation"/>); the next start or stop releases it.</summary>
    public bool IsPinned => _pinned;

    /// <summary>The heritage's backdrop parts (<c>m_pbgObject</c>), posed at the origin; empty when it has none.</summary>
    public ReadOnlySpan<PartDraw> BackdropDraws => _backdrop;

    /// <summary>The heritage's environment Setup shown behind the character, or 0.</summary>
    public uint BackdropSetupId => _backdropSetupId;

    /// <summary>The last composition's ObjDesc result (every step that failed, loud and never fatal).</summary>
    public AppearanceResult LastResult { get; private set; } = AppearanceResult.Success;

    /// <summary><c>gmCG3DView::SetCamera</c>: the camera's position and direction (the host moves its camera to them).</summary>
    public void SetCamera(Vector3 position, Vector3 directionDegrees)
    {
        CameraPosition = position;
        CameraDirection = directionDegrees;
        _version++;
    }

    /// <summary><c>gmCG3DView::SetPlayerHeading</c>: the character's heading in degrees.</summary>
    public void SetPlayerHeading(float degrees)
    {
        Heading = degrees;
        _preview?.SetHeading(degrees);
        _version++;
    }

    /// <summary>
    /// <c>gmCG3DView::StartAnimation</c> (11.6096 0x004EF290): clears the sequence and loops the view's animation from frame 0 at 30 fps (restarting it
    /// when it already plays, as retail does).
    /// </summary>
    public void StartAnimation()
    {
        _animating = true;
        _pinned = false;
        ApplyAnimation();
    }

    /// <summary><c>gmCG3DView::StopAnimation</c> (11.6096 0x004EF2D0): clears the sequence and holds frame 0 of the rest animation at 0 fps.</summary>
    public void StopAnimation()
    {
        _animating = false;
        _pinned = false;
        ApplyAnimation();
    }

    /// <summary>
    /// A capture's hook (the frames runner; not in retail): restarts the animation and steps it exactly <paramref name="seconds"/> from frame 0, then holds that
    /// pose whatever the clock does, until the next <see cref="StartAnimation"/> or <see cref="StopAnimation"/>, so a review frame shows the same pose
    /// however long the script ran before it. Does nothing, and returns false, when the view holds the rest pose (zoomed in) or has no character.
    /// </summary>
    /// <param name="seconds">The time into the loop, above 1/30 s (the quantum below which the object does not step).</param>
    public bool PinAnimation(double seconds)
    {
        if (!_animating || _preview is null)
        {
            return false;
        }

        _preview.StartAnimation(_now);
        _preview.Advance(_now + seconds);
        _pinned = true;
        _version++;
        return true;
    }

    /// <summary>
    /// <c>gmCG3DView::Update</c>: composes the character from the state's choices (it does nothing before a heritage and a gender are set) and applies it;
    /// a new body or a heritage with other animations makes a new object, posed as the last animation call left it. The backdrop follows the heritage.
    /// </summary>
    /// <param name="now">The runtime's clock in seconds.</param>
    public void Update(double now)
    {
        _now = now;
        if (_state.Heritage == 0 || _state.Gender == 0)
        {
            return;
        }

        var composition = CharGenAppearance.Compose(_data.Record, _state.ToLook(), _data.Dats);
        LastResult = composition.Result;
        if (!composition.Result.AllApplied && !_composeProblemLogged)
        {
            _composeProblemLogged = true;
            Say(LogSeverity.Warning, $"character creation's 3D character did not apply every appearance change (retail ignores this too): {string.Join("; ", composition.Result.Problems)}");
        }

        var animations = PreviewAnimations.Resolve(_data.Maps, (int)_state.Heritage);
        try
        {
            if (_preview is null || _preview.Animations.Animation != animations.Animation || _preview.Animations.Rest != animations.Rest)
            {
                _preview = CreaturePreview.Create(_data.Sources, composition, animations, now);
                _preview.SetHeading(Heading);
                ApplyAnimation();
            }
            else
            {
                var setup = _preview.Setup.Id;
                _preview.SetAppearance(composition, now);
                if (_preview.Setup.Id != setup)
                {
                    ApplyAnimation();
                }
            }
        }
        catch (InvalidDataException ex)
        {
            Say(LogSeverity.Error, $"character creation's 3D character could not be made; its view shows nothing: {ex.Message}");
            _preview = null;
        }

        SetBackdrop(composition.BackgroundSetupId);
        _version++;
    }

    /// <summary>
    /// What <c>CreatureMode::Render</c> does for the character each frame: <c>CPhysicsObj::update_position</c> to <paramref name="now"/>. The rest pose
    /// has nothing to step, so it costs nothing; a pinned animation (<see cref="PinAnimation"/>) is not stepped either.
    /// </summary>
    public void Advance(double now)
    {
        _now = now;
        if (_animating && !_pinned)
        {
            _preview?.Advance(now);
        }
    }

    private void Say(LogSeverity severity, string message)
    {
        if (_log.IsEnabled(severity))
        {
            _log.Write(severity, "retailui", message, []);
        }
    }

    private void ApplyAnimation()
    {
        _pinned = false;
        if (_preview is null)
        {
            return;
        }

        if (_animating)
        {
            _preview.StartAnimation(_now);
        }
        else
        {
            _preview.StopAnimation();
        }
    }

    private void SetBackdrop(uint setupId)
    {
        if (setupId == _backdropSetupId)
        {
            return;
        }

        _backdropSetupId = setupId;
        _backdrop = [];
        if (setupId == 0)
        {
            return;
        }

        try
        {
            var setup = _data.Sources.GetSetup(setupId) ?? throw new InvalidDataException($"the backdrop Setup 0x{setupId:X8} does not exist");
            var poses = setup.ComputeRestPose();
            var draws = new List<PartDraw>(poses.Length);
            for (var i = 0; i < poses.Length; i++)
            {
                if (setup.Parts[i].IsGfxObj)
                {
                    draws.Add(new PartDraw(i, setup.Parts[i].GfxObjId, new AcFrame(poses[i].Origin, poses[i].Orientation), poses[i].Scale, DegradeMode.Normal));
                }
            }

            _backdrop = [.. draws];
        }
        catch (InvalidDataException ex)
        {
            Say(LogSeverity.Error, $"character creation's backdrop (Setup 0x{setupId:X8}) could not be made; the character shows against black: {ex.Message}");
        }
    }
}
