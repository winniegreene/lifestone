using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Avatars;

/// <summary>
/// The animations CharGen's 3D view plays (retail <c>gmCG3DView</c>'s <c>m_didAnimation</c>, <c>m_didAnimationRest</c>
/// and <c>m_didAnimArray</c>), resolved through the UIASSET enum map (group 7).
/// </summary>
/// <param name="Animation">Played at 30 fps from frame 0 by <see cref="CreaturePreview.StartAnimation"/>.</param>
/// <param name="Rest">Held at frame 0 (0 fps) by <see cref="CreaturePreview.StopAnimation"/>.</param>
/// <param name="IdleEmotes">
/// The five idle emotes retail loads (wave, yawn, shrug, akimbo, tap foot; the twitch five times for the Olthoi). No
/// 11.4186 code plays them (<c>m_didAnimArray</c> is only written, and <c>m_dLastAnimTime</c>, which
/// <c>StartAnimation</c> sets, is never read), so the preview never plays them either (owner decision D35; m2-plan §1.6
/// item 6).
/// </param>
public sealed record PreviewAnimations(uint Animation, uint Rest, IReadOnlyList<uint> IdleEmotes)
{
    /// <summary>The Olthoi heritage whose preview plays the Olthoi animations.</summary>
    public const int OlthoiHeritage = 12;

    /// <summary>The acid Olthoi heritage.</summary>
    public const int OlthoiAcidHeritage = 13;

    /// <summary>
    /// Retail <c>gmCG3DView::Initialize</c> (0x004EE760 / 0x004EF3F0) and <c>Update</c> (0x004EE9D0 / 0x004EF660): for
    /// heritage 12 the Olthoi animation as both the animation and the rest, and its twitch as every idle emote; for 13 the
    /// acid Olthoi's; for any other the CharGen animation (0x10000006), the paper-doll rest (0x10000005) and the five
    /// idle emotes (0x10000007-0x1000000B).
    /// </summary>
    /// <exception cref="InvalidDataException">An enum the map does not resolve (<see cref="EnumIdMaps.Resolve"/>).</exception>
    public static PreviewAnimations Resolve(EnumIdMaps maps, int heritage)
    {
        ArgumentNullException.ThrowIfNull(maps);
        uint R(uint e) => maps.Resolve(EnumGroup.UiAsset, e);
        switch (heritage)
        {
            case OlthoiHeritage:
            {
                var twitch = R(UiAssetIds.CharGenOlthoiTwitchAnimation);
                var anim = R(UiAssetIds.CharGenOlthoiAnimation);
                return new PreviewAnimations(anim, anim, [twitch, twitch, twitch, twitch, twitch]);
            }

            case OlthoiAcidHeritage:
            {
                var twitch = R(UiAssetIds.CharGenOlthoiAcidTwitchAnimation);
                var anim = R(UiAssetIds.CharGenOlthoiAcidAnimation);
                return new PreviewAnimations(anim, anim, [twitch, twitch, twitch, twitch, twitch]);
            }

            default:
                return new PreviewAnimations(
                    R(UiAssetIds.CharGenAnimation),
                    R(UiAssetIds.PaperDollAnimation),
                    [
                        R(UiAssetIds.CharGenAnimationWave),
                        R(UiAssetIds.CharGenAnimationYawn),
                        R(UiAssetIds.CharGenAnimationShrug),
                        R(UiAssetIds.CharGenAnimationAkimbo),
                        R(UiAssetIds.CharGenAnimationTapFoot),
                    ]);
        }
    }
}

/// <summary>
/// CharGen's 3D view (AV12), engine-agnostic: the character a <see cref="CharGenComposition"/> describes, posed and
/// animated as retail's <c>gmCG3DView</c> (11.4186 gmCG3DView.c; names and addresses cited, nothing copied; addresses
/// are 11.4186 / 11.6096) does it in its <c>CreatureMode</c> viewport. The wave-2 <c>CreatureModeView</c> draws
/// <see cref="Draws"/> with <see cref="LightDirection"/> and <see cref="LightIntensity"/>, and the heritage's
/// background Setup (<see cref="BackgroundSetupId"/>) behind it.
/// </summary>
/// <remarks>
/// <para><b>The object</b> is <c>CPhysicsObj::makeObject(setup)</c>: the Setup's parts, resting placement, default
/// script, and its default motion table in its default state; an examination object, so every part draws level 0 and
/// degrades are off. <c>gmCG3DView::Update</c> makes it again only when the Setup changes (a new object plays its motion
/// table's default state until the page starts or stops the animation again), sets its heading
/// (<see cref="Heading"/>, 180° on the CharGen pages) and applies the ObjDesc with
/// <c>DoObjDescChangesFromDefault</c>.</para>
/// <para><b>Animation</b>: <see cref="StartAnimation"/> and <see cref="StopAnimation"/> clear the sequence and set one
/// animation (<c>CPhysicsObj::clear_sequence_anims</c>, 0x0050F760 / 0x00510230; <c>set_sequence_animation</c>,
/// 0x0050F6F0 / 0x005101C0). <see cref="Advance"/> is <c>CPhysicsObj::update_position</c> (0x00514520 / 0x00515020),
/// which <c>CreatureMode::Render</c> (0x004529D0 / 0x00452A10) calls for each of its objects: one step of the whole
/// elapsed time once at least 1/30 s has passed, none beyond 2 s; no root motion (the object touches nothing).</para>
/// <para>Not thread-safe; allocation-free per <see cref="Advance"/> once warm.</para>
/// </remarks>
public sealed class CreaturePreview
{
    /// <summary>The view's light direction (<c>UIElement_Viewport::SetLight(DISTANT_LIGHT, 2.0, (0.3, 1.9, 0.65))</c>;
    /// portal space's light points the other way in Y).</summary>
    public static readonly Vector3 LightDirection = new(0.3f, 1.9f, 0.65f);

    /// <summary>The view's distant light intensity (2.0).</summary>
    public const float LightIntensity = 2.0f;

    /// <summary>The heading the CharGen pages give the character (<c>gmCG3DView::SetPlayerHeading(180)</c>).</summary>
    public const float CharGenHeading = 180f;

    /// <summary>The framerate <see cref="StartAnimation"/> plays at.</summary>
    public const float AnimationFramerate = 30f;

    /// <summary><c>CPhysicsObj::update_position</c> waits until at least this many seconds passed (1/30).</summary>
    public const double MinQuantum = 1.0 / 30.0;

    /// <summary>A gap longer than this many seconds is skipped.</summary>
    public const double MaxElapsed = 2.0;

    private const double Epsilon = 0.00019999999;

    private readonly AvatarSources _sources;
    private PartDraw[] _draws = [];
    private AcFrame[] _partFrames = [];
    private ModelAssembly _setup;
    private PartArrayAppearance _appearance;
    private IPoseDriver _driver;
    private EffectObject _effects;
    private double _updateTime;
    private float _heading = CharGenHeading;

    private CreaturePreview(AvatarSources sources, CharGenComposition composition, PreviewAnimations animations, double now)
    {
        _sources = sources;
        Animations = animations;
        Composition = composition;
        _updateTime = now;
        (_setup, _appearance, _driver, _effects) = MakeObject(composition.SetupId, now);
        Result = _appearance.DoObjDescChangesFromDefault(composition.ObjDesc);
        Pose();
    }

    /// <summary>The animations.</summary>
    public PreviewAnimations Animations { get; }

    /// <summary>The composition shown.</summary>
    public CharGenComposition Composition { get; private set; }

    /// <summary>The ObjDesc result of the last appearance change (problems are loud, never fatal; retail ignores them).</summary>
    public AppearanceResult Result { get; private set; }

    /// <summary>The character's Setup.</summary>
    public ModelAssembly Setup => _setup;

    /// <summary>The parts' appearances.</summary>
    public IReadOnlyList<PartAppearance> Parts => _appearance.Parts;

    /// <summary>The part array's sequence.</summary>
    public AnimSequence Sequence => _driver is MotionPlayer m ? m.Sequence : ((DefaultAnimationPlayer)_driver).Sequence;

    /// <summary>The heritage's environment Setup drawn behind the character (0 for none).</summary>
    public uint BackgroundSetupId => Composition.BackgroundSetupId;

    /// <summary>The character's heading in degrees (<c>m_fHeading</c>).</summary>
    public float Heading => _heading;

    /// <summary>The object frame: the origin, turned to <see cref="Heading"/>.</summary>
    public AcFrame Frame => new(Vector3.Zero, PositionFrames.WithHeading(Quaternion.Identity, _heading));

    /// <summary>The character's effects (its default script; animation hooks).</summary>
    public EffectObject Effects => _effects;

    /// <summary>Changes whenever the parts are posed again.</summary>
    public int PoseVersion { get; private set; }

    /// <summary>The parts to draw: level 0 of each part's chain, in <see cref="Frame"/>'s space.</summary>
    public ReadOnlySpan<PartDraw> Draws => _draws;

    /// <summary>Makes the view of <paramref name="composition"/> (<c>gmCG3DView::Initialize</c> and <c>Update</c>).</summary>
    /// <exception cref="InvalidDataException">The Setup does not exist or a part does not load.</exception>
    public static CreaturePreview Create(AvatarSources sources, CharGenComposition composition, PreviewAnimations animations, double now)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(animations);
        return new CreaturePreview(sources, composition, animations, now);
    }

    /// <summary>
    /// Retail <c>gmCG3DView::Update</c>: a new Setup makes a new object (playing its default state); the ObjDesc is applied
    /// from the default (<c>CPhysicsObj::DoObjDescChangesFromDefault</c>), and the heading kept.
    /// </summary>
    public void SetAppearance(CharGenComposition composition, double now)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (composition.SetupId != _setup.Id)
        {
            (_setup, _appearance, _driver, _effects) = MakeObject(composition.SetupId, now);
            _updateTime = now;
        }

        Composition = composition;
        Result = _appearance.DoObjDescChangesFromDefault(composition.ObjDesc);
        Pose();
    }

    /// <summary>Retail <c>gmCG3DView::SetPlayerHeading</c> (0x004EE590 / 0x004EF220).</summary>
    public void SetHeading(float degrees)
    {
        _heading = degrees;
        Pose();
    }

    /// <summary>
    /// Retail <c>gmCG3DView::StartAnimation</c> (0x004EE600 / 0x004EF290): clears the sequence and plays
    /// <see cref="PreviewAnimations.Animation"/> from frame 0 at 30 fps. Retail stores the time in <c>m_dLastAnimTime</c>, which nothing reads;
    /// here it also marks the object as updated up to <paramref name="now"/>, so the first step after a start is the time since the start (retail
    /// updates the object every frame, so a held rest pose never leaves a gap that the animation would then jump).
    /// </summary>
    public void StartAnimation(double now)
    {
        _updateTime = now;
        Sequence.ClearAnimations();
        Sequence.SetSequenceAnimation(Animations.Animation, interrupt: true, startFrame: 0, AnimationFramerate);
        Pose();
    }

    /// <summary>
    /// Retail <c>gmCG3DView::StopAnimation</c> (0x004EE640 / 0x004EF2D0): clears the sequence and holds frame 0 of
    /// <see cref="PreviewAnimations.Rest"/> (0 fps).
    /// </summary>
    public void StopAnimation()
    {
        Sequence.ClearAnimations();
        Sequence.SetSequenceAnimation(Animations.Rest, interrupt: true, startFrame: 0, framerate: 0f);
        Pose();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::update_position</c> for a viewport object: nothing until at least 1/30 s has passed; then,
    /// up to 2 s, one step of the whole elapsed time (animation, hooks, particles, scripts); beyond 2 s the time is
    /// skipped.
    /// </summary>
    public void Advance(double now)
    {
        var elapsed = now - _updateTime;
        if (elapsed <= Epsilon)
        {
            _updateTime = now;
            return;
        }

        if (elapsed < MinQuantum)
        {
            return;
        }

        if (elapsed <= MaxElapsed)
        {
            var update = _driver.Update(elapsed);
            for (var i = 0; i < update.Hooks.Count; i++)
            {
                _effects.QueueAnimHook(update.Hooks[i]);
            }

            Pose();
            _effects.Update(now, Frame, _partFrames);
        }

        _updateTime = now;
    }

    private (ModelAssembly Setup, PartArrayAppearance Appearance, IPoseDriver Driver, EffectObject Effects) MakeObject(uint setupId, double now)
    {
        var setup = _sources.GetSetup(setupId) ?? throw new InvalidDataException($"CharGen Setup 0x{setupId:X8} does not exist");
        var appearance = PartArrayAppearance.Create(setup, _sources.Appearance);
        if (!appearance.IsLoadable)
        {
            throw new InvalidDataException($"CharGen Setup 0x{setupId:X8} has a part that does not load");
        }

        IPoseDriver driver;
        var placement = _sources.Placement(setup, PlacementIds.Resting);
        if (setup.DefaultMotionTableId != 0 && _sources.GetMotionTable(setup.DefaultMotionTableId) is { } table)
        {
            var player = new MotionPlayer(table, _sources.Animations);
            player.Sequence.SetPlacementFrame(placement, PlacementIds.Resting);
            driver = player;
        }
        else
        {
            var defaults = new DefaultAnimationPlayer(_sources.Animations, setup.DefaultAnimationId);
            defaults.Sequence.SetPlacementFrame(placement, PlacementIds.Resting);
            driver = defaults;
        }

        var effects = new EffectObject(_sources.Effects, new RetailRandom((int)setupId), setup.DefaultScriptTableId);
        _partFrames = new AcFrame[setup.Parts.Count];
        _draws = new PartDraw[setup.Parts.Count];
        effects.SetPlacement(now, Frame, _partFrames);
        if (setup.DefaultScriptId != 0)
        {
            effects.PlayScriptId(setup.DefaultScriptId);
        }

        return (setup, appearance, driver, effects);
    }

    /// <summary>Poses the parts and fills <see cref="Draws"/> (level 0 of every part, as an examination object).</summary>
    private void Pose()
    {
        var frame = Frame;
        var pose = _driver.Pose;
        if (pose is null)
        {
            Array.Fill(_partFrames, frame);
        }
        else
        {
            Data.Animation.PartPose.Update(frame, pose, Vector3.One, _partFrames);
            for (var i = pose.Count; i < _partFrames.Length; i++)
            {
                _partFrames[i] = frame;
            }
        }

        var parts = _appearance.Parts;
        for (var i = 0; i < _draws.Length; i++)
        {
            var part = _setup.Parts[i];
            var chain = parts[i].Chain;
            var gfxObj = part.IsGfxObj && chain.IsLoadable ? chain.NearestGfxObjId : 0;
            _draws[i] = new PartDraw(i, gfxObj, _partFrames[i], part.DefaultScale, DegradeMode.Normal);
        }

        PoseVersion++;
    }
}
