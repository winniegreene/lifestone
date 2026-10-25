using Lifestone.Client.Session.Scenarios;


namespace Lifestone.Game.Sessions;

/// <summary>What a station's frames want of the chase camera (M4 P5b J7).</summary>
public enum JourneyCameraKind
{
    /// <summary>Retail's own camera: nothing changed.</summary>
    Retail,

    /// <summary>The melee hunt's review framing: <see cref="HuntUiShotPlan.FramedScale"/> behind the player, straight behind until a frame's target is hidden (then the side turn).</summary>
    MeleeFramed,

    /// <summary>The missile hunt's: <see cref="HuntUiShotPlan.MissileFramedScale"/> and the viewer turned <see cref="HuntUiShotPlan.MissileSideDegrees"/> about the player, so the bow and the arrow's flight are seen from the side.</summary>
    MissileSide,

    /// <summary>Retail's default offsets turned <see cref="HuntUiShotPlan.SideOffsetDegrees"/> about the player (the hero fight shots: the caster seen from the side, the tuskers around it).</summary>
    SideTurn,
}

/// <summary>
/// The chase camera for a journey's frames (M4 P5b J7), lifted from the hunt leg runner's <c>FrameCamera</c>: the camera is retail's chase camera, its offset scale and its heading offset (retail's own camera rotation and zoom, as a player turns them)
/// set once when a station starts and put back when it ends. Never a free camera, a path or anything retail has not got.
/// </summary>
public sealed class JourneyShotCamera
{
    private bool _framed;
    private float _scaleBefore;

    /// <summary>The scale in force, or 0 when not framed.</summary>
    public float Scale { get; private set; }

    /// <summary>The heading offset in force, in degrees.</summary>
    public float SideDegrees { get; private set; }

    /// <summary>The run clock the heading offset was last set at.</summary>
    public double SideSetAt { get; private set; }

    /// <summary>True while the camera is framed.</summary>
    public bool Framed => _framed;

    /// <summary>Frames the camera for <paramref name="kind"/> (once; a second call is ignored until <see cref="Restore"/>).</summary>
    /// <param name="mode">The play mode.</param>
    /// <param name="kind">What the station wants.</param>
    /// <param name="now">The run clock.</param>
    /// <returns>True when the camera was framed by this call.</returns>
    public bool Frame(PlayMode mode, JourneyCameraKind kind, double now)
    {
        if (_framed || kind == JourneyCameraKind.Retail || mode.Rig is not { } rig)
        {
            return false;
        }

        _framed = true;
        _scaleBefore = rig.Chase.Model.Scale;
        switch (kind)
        {
            case JourneyCameraKind.MeleeFramed:
                Scale = HuntUiShotPlan.FramedScale;
                SideDegrees = 0f;
                break;
            case JourneyCameraKind.MissileSide:
                Scale = HuntUiShotPlan.MissileFramedScale;
                SideDegrees = HuntUiShotPlan.MissileSideDegrees;
                break;
            default:
                Scale = _scaleBefore;
                SideDegrees = HuntUiShotPlan.SideOffsetDegrees;
                break;
        }

        rig.Chase.Model.Scale = Scale;
        rig.Chase.Model.SetDefaultOffsets();
        if (SideDegrees != 0f)
        {
            rig.Chase.Model.SetHeadingOffset(SideDegrees);
        }

        SideSetAt = now;
        return true;
    }

    /// <summary>Turns to the next side (<see cref="HuntUiShotPlan.NextSideOffset"/>: straight, one side, the other).</summary>
    /// <param name="mode">The play mode.</param>
    /// <param name="now">The run clock.</param>
    public void TurnNext(PlayMode mode, double now)
    {
        if (!_framed || mode.Rig is not { } rig)
        {
            return;
        }

        SideDegrees = HuntUiShotPlan.NextSideOffset(SideDegrees);
        rig.Chase.Model.SetHeadingOffset(SideDegrees);
        SideSetAt = now;
    }

    /// <summary>Puts the camera back (retail's scale and default offsets).</summary>
    /// <param name="mode">The play mode.</param>
    public void Restore(PlayMode mode)
    {
        if (!_framed)
        {
            return;
        }

        _framed = false;
        if (mode.Rig is { } rig && Godot.GodotObject.IsInstanceValid(rig.Chase))
        {
            rig.Chase.Model.Scale = _scaleBefore;
            rig.Chase.Model.SetDefaultOffsets();
        }

        (Scale, SideDegrees) = (0f, 0f);
    }
}
