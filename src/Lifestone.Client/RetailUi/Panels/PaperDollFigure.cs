using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Effects;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Appearance;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The paperdoll's 3D figure, engine-agnostic (M3 2.2 slice b4, docs/milestones/m3-plan.md section 4.6): the player's own body in the viewport 0x100001D5 of page 7, as retail's <c>gmPaperDollUI</c> shows it. The
/// Godot layer draws it (<c>CharGenViewportBinder</c>, through <see cref="ICreatureView"/>, with creature mode's light: the 0.3 ambient and one distant light of intensity 2 towards (0.3, 1.9, 0.65), which
/// <c>gmPaperDollUI::PostInit</c> 0x004A56D0 sets as CharGen's view does).
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>RedressCreature</c> (11.6096 0x004A3F30) makes the viewport's object from the player's physics object (<c>CPhysicsObj::makeObject</c>: its Setup, motion and script tables), turns it to heading
/// 191.3679, holds the paper-doll animation (UIASSET 0x10000005, the human heritages' rest pose) at frame 0 and, for every redress, applies the player's visual description
/// (<c>SmartBox::get_player_visualdesc</c>) with <c>DoObjDescChangesFromDefault</c>; it runs at <c>PostInit</c> and on <c>RecvNotice_PlayerObjDescChanged</c> (0x004A46C0). <c>PostInit</c> puts the camera at
/// (0.12, -2.4, 0.88) looking along +Y (direction 0); <c>UpdateForRace</c> (0x004A4240, on <c>RecvNotice_PlayerDescReceived</c> 0x004A4750 and the player-description quality change 0x004A4800, with the player's
/// HeritageGroup, int property 0xBC) moves it by heritage: 6 and 7 to (0.12, -3.0, 0.88); 9 to (0.12, -3.4, 0.88); 8 to (0.12, -3.4, 1.0); the Olthoi, 12 and 13, to (0.12, -3.4, 0.88) and to their own animation
/// (UIASSET 0x10000011 and 0x10000013, which <see cref="PreviewAnimations.Resolve"/> gives as the rest). The other heritages (Aluvian, Gharu'ndim, Sho, Viamontian and the rest) keep <c>PostInit</c>'s camera.
/// </para>
/// <para>
/// <b>Lifestone.</b> The figure is rebuilt by <see cref="Update"/>, which the page calls when it is shown and something marked it stale (the player's ObjDescEvent, an item worn or taken off): never per frame, and
/// never while the page is hidden. A new Setup or heritage makes a new <see cref="CreaturePreview"/>; any other change re-applies the appearance to the one there is. The figure holds its pose (frame 0 of the
/// rest animation at 0 fps), so nothing steps it. The player's ObjScale is not applied (the preview draws at the Setup's own scale): <c>TODO(m3-paperdoll-scale)</c>, logged once.
/// </para>
/// </remarks>
public sealed class PaperDollFigure : ICreatureView
{
    /// <summary><c>RedressCreature</c> (0x004A3F30): <c>CPhysicsObj::set_heading(191.3679, 1)</c>, degrees.</summary>
    public const float FigureHeading = 191.3679f;

    /// <summary>The HeritageGroup int property (0xBC) <c>UpdateForRace</c> reads from the player's qualities.</summary>
    public const uint HeritageGroupProperty = 0xBC;

    /// <summary><c>gmPaperDollUI::PostInit</c> (0x004A56D0): the camera before any heritage moved it.</summary>
    public static readonly Vector3 DefaultCamera = new(0.12f, -2.4f, 0.88f);

    private readonly Cg3DViewData _data;
    private readonly IGameSessionView _session;
    private readonly ILogSink _log;
    private CreaturePreview? _preview;
    private int _version;
    private int _heritage = -1;
    private bool _scaleLogged;
    private bool _problemLogged;

    /// <summary>A figure of the session's player over the avatar data of <paramref name="data"/>; empty until <see cref="Update"/>.</summary>
    public PaperDollFigure(Cg3DViewData data, IGameSessionView session, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        _data = data;
        _session = session;
        _log = log;
        CameraPosition = DefaultCamera;
    }

    /// <summary>How many times the figure was made or re-dressed (a test counts rebuilds).</summary>
    public int Builds { get; private set; }

    /// <summary>The heritage the camera was set for (the player's HeritageGroup), or -1 before the first build.</summary>
    public int Heritage => _heritage;

    /// <inheritdoc/>
    public CreaturePreview? Preview => _preview;

    /// <inheritdoc/>
    public Vector3 CameraPosition { get; private set; }

    /// <inheritdoc/>
    public Vector3 CameraDirection => Vector3.Zero;

    /// <inheritdoc/>
    public int Version => _version + (_preview?.PoseVersion ?? 0);

    /// <inheritdoc/>
    public ReadOnlySpan<PartDraw> BackdropDraws => default;

    /// <inheritdoc/>
    public uint BackdropSetupId => 0;

    /// <summary>
    /// <c>UpdateForRace</c> (0x004A4240): the camera for <paramref name="heritage"/>; <see cref="DefaultCamera"/> (<c>PostInit</c>'s) for a heritage the function has no case for.
    /// </summary>
    public static Vector3 CameraFor(int heritage) => heritage switch
    {
        6 or 7 => new Vector3(0.12f, -3.0f, 0.88f),
        9 or 12 or 13 => new Vector3(0.12f, -3.4f, 0.88f),
        8 => new Vector3(0.12f, -3.4f, 1.0f),
        _ => DefaultCamera,
    };

    /// <summary>
    /// <c>RedressCreature</c> (0x004A3F30) and <c>UpdateForRace</c> (0x004A4240): makes the figure from the player's object as it is now, or re-dresses the one there is. Returns false (loud once, the
    /// figure then stays as it was) when the player has no object or no Setup yet.
    /// </summary>
    /// <param name="now">The runtime's clock in seconds.</param>
    public bool Update(double now)
    {
        var state = _session.State;
        if (!state.Objects.TryGet(state.Self.Guid, out var player))
        {
            return Problem("the player's object is not in the table yet");
        }

        var fields = player.FigureFields();
        if (fields.SetupId == 0)
        {
            return Problem("the player's CreateObject carried no Setup");
        }

        var heritage = state.Self.Qualities.TryGetInt(HeritageGroupProperty, out var value) ? value : 0;
        var composition = new CharGenComposition(fields.SetupId, fields.MotionTableId, fields.ScriptTableId, fields.ObjDesc, 0, AppearanceResult.Success);
        try
        {
            var animations = PreviewAnimations.Resolve(_data.Maps, heritage);
            if (_preview is null || _heritage != heritage || _preview.Animations.Animation != animations.Animation || _preview.Animations.Rest != animations.Rest)
            {
                _preview = CreaturePreview.Create(_data.Sources, composition, animations, now);
                _preview.SetHeading(FigureHeading);
                _preview.StopAnimation(); // set_sequence_animation(m_didAnimation, .., 0.0): the rest pose, frame 0, 0 fps
            }
            else
            {
                var setup = _preview.Setup.Id;
                _preview.SetAppearance(composition, now);
                if (_preview.Setup.Id != setup)
                {
                    _preview.SetHeading(FigureHeading);
                    _preview.StopAnimation();
                }
            }
        }
        catch (InvalidDataException ex)
        {
            _preview = null;
            _version++;
            return Problem($"the figure could not be made: {ex.Message}");
        }

        _heritage = heritage;
        CameraPosition = CameraFor(heritage);
        if (!_preview.Result.AllApplied && !_problemLogged)
        {
            _problemLogged = true;
            Say(LogSeverity.Warning, $"the paperdoll's figure did not apply every appearance change (retail ignores this too): {string.Join("; ", _preview.Result.Problems)}");
        }

        if (!_scaleLogged && fields.Scale is var scale && Math.Abs(scale - 1f) > 0.001f)
        {
            _scaleLogged = true;
            Say(LogSeverity.Warning, $"TODO(m3-paperdoll-scale): the player's ObjScale {scale} is not applied to the paperdoll's figure");
        }

        Builds++;
        _version++;
        return true;
    }

    private bool Problem(string message)
    {
        if (!_problemLogged)
        {
            _problemLogged = true;
            Say(LogSeverity.Warning, $"TODO(m3-paperdoll-figure-data): the paperdoll's figure shows nothing: {message}");
        }

        return false;
    }

    private void Say(LogSeverity severity, string message)
    {
        if (_log.IsEnabled(severity))
        {
            _log.Write(severity, "retailui", message, []);
        }
    }
}
