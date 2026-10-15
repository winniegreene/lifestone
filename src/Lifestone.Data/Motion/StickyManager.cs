using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>StickyManager</c>, re-expressed from the 11.4186 decompile (PORTAL/cpmanager/CStickyManager.c; names and
/// addresses cited, nothing copied): keeps an object pressed against the object it sticks to (a melee approach that
/// ended with the Sticky flag). Each update's offset is replaced by a step towards the target's cylinder, 0.3 m short of
/// touching, turning to face it. A stick lasts one second unless renewed, and ends when the target reports anything but
/// Ok. Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para>Retail reads <c>Timer::cur_time</c>; here the host's <see cref="ITargetable.Time"/>. The timeout is never
/// extended by a target update in the decompile, so a stick ends one second after <see cref="StickTo"/> unless the server
/// sends a new one.</para>
/// <para>Not thread-safe. Allocation-free.</para>
/// </remarks>
public sealed class StickyManager
{
    /// <summary>A stick ends this many seconds after it began (retail <c>sticky_timeout_time</c> = now + 1.0).</summary>
    public const double StickySeconds = 1.0;

    /// <summary>The gap the follower keeps to the target's cylinder (0.30000001).</summary>
    public const float StickyGap = 0.3f;

    /// <summary>The step speed is this multiple of the interpreter's maximum speed.</summary>
    public const float MaxSpeedFactor = 5f;

    /// <summary>The step speed without an interpreter (or with a zero maximum speed).</summary>
    public const float FallbackSpeed = 15f;

    /// <summary>The radius a voyeur report needs to move by before the target reports again (0.5 m).</summary>
    public const float TargetRadius = 0.5f;

    /// <summary>The prediction time asked of the target (0.5 s).</summary>
    public const double TargetQuantum = 0.5;

    private readonly IMoveToHost _host;

    /// <summary>Retail <c>StickyManager::Create</c> (0x00555800 / 0x00556420).</summary>
    public StickyManager(IMoveToHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Retail <c>target_id</c>: the object stuck to, or 0.</summary>
    public uint TargetId { get; private set; }

    /// <summary>Retail <c>target_radius</c>.</summary>
    public float TargetRadiusOfObject { get; private set; }

    /// <summary>Retail <c>initialized</c>: the target has reported Ok since <see cref="StickTo"/>.</summary>
    public bool Initialized { get; private set; }

    /// <summary>Retail <c>target_position</c>: the target's last reported position.</summary>
    public CellFrame TargetPosition { get; private set; } = new(0, AcFrame.Identity);

    /// <summary>Retail <c>sticky_timeout_time</c>, on the frame clock.</summary>
    public double TimeoutTime { get; private set; }

    /// <summary>
    /// Retail <c>StickTo</c> (0x00555710 / 0x00556330): ends any stick (clearing the target and cancelling the MoveTo),
    /// then sticks to <paramref name="targetId"/> for <see cref="StickySeconds"/> and asks it for reports (radius 0.5 m,
    /// quantum 0.5 s). <paramref name="targetHeight"/> is passed by retail and not kept.
    /// </summary>
    public void StickTo(uint targetId, float targetRadius, float targetHeight)
    {
        _ = targetHeight;
        Unstick();
        TargetRadiusOfObject = targetRadius;
        TargetId = targetId;
        Initialized = false;
        TimeoutTime = _host.Time + StickySeconds;
        _host.Targets.SetTarget(0, targetId, TargetRadius, TargetQuantum);
    }

    /// <summary>
    /// Retail <c>HandleExitWorld</c> (0x00555400; TODO(avatars): 11.6096), which <c>PositionManager::UnStick</c> calls:
    /// when stuck, forgets the target, clears it and cancels the MoveTo.
    /// </summary>
    public void Unstick()
    {
        if (TargetId == 0)
        {
            return;
        }

        TargetId = 0;
        Initialized = false;
        _host.Targets.ClearTarget();
        _host.CancelMoveTo();
    }

    /// <summary>
    /// Retail <c>UseTime</c> (0x00555610; TODO(avatars): 11.6096): after the timeout the stick ends
    /// (<see cref="Unstick"/>).
    /// </summary>
    public void UseTime()
    {
        if (TargetId != 0 && _host.Time > TimeoutTime)
        {
            Unstick();
        }
    }

    /// <summary>
    /// Retail <c>HandleUpdateTarget</c> (0x00555780 / 0x005563A0): an Ok report from the target initialises the stick and
    /// records the target's position; any other status ends it.
    /// </summary>
    public void HandleUpdateTarget(in TargetInfo info)
    {
        if (info.ObjectId != TargetId)
        {
            return;
        }

        if (info.Status == TargetStatus.Ok)
        {
            Initialized = true;
            TargetPosition = info.TargetPosition;
        }
        else
        {
            Unstick();
        }
    }

    /// <summary>
    /// Retail <c>adjust_offset</c> (0x00555430 / 0x00556050): once initialised, replaces the offset's origin with a
    /// horizontal step (in the object's local axes) towards the target, the target's live position when it is known here
    /// and its last reported one otherwise. The step's length is the cylinder gap less <see cref="StickyGap"/>, capped at
    /// <see cref="MaxSpeedFactor"/> × the interpreter's maximum speed (<see cref="FallbackSpeed"/> without one) ×
    /// <paramref name="quantum"/>; the cap applies to the vertical part too, which is zero. The offset's rotation is
    /// replaced by the turn that faces the target.
    /// </summary>
    public void AdjustOffset(ref AcFrame offset, double quantum, in PositionContext ctx)
    {
        if (TargetId == 0 || !Initialized)
        {
            return;
        }

        var position = ctx.Position;
        var target = _host.World.FindObject(TargetId) is { } live ? live.Position : TargetPosition;
        var global = PositionFrames.Offset(position, target);
        var local = position.Frame.GlobalToLocalVec(global);
        local.Z = 0f;
        var gap = PositionFrames.CylinderDistanceNoZ(_host.Radius, position, TargetRadiusOfObject, target) - StickyGap;
        var length = local.Length();
        if (length < MotionInterpreter.Epsilon)
        {
            local = default;
        }
        else
        {
            local /= length;
        }

        var speed = _host.Motion is { } motion ? motion.GetMaxSpeed() * (double)MaxSpeedFactor : 0.0;
        if (speed < MotionInterpreter.Epsilon)
        {
            speed = FallbackSpeed;
        }

        var step = speed * quantum;
        if (step >= Math.Abs(gap))
        {
            local.X *= gap;
            local.Y *= gap;
            step = gap;
        }
        else
        {
            local.X = (float)(local.X * step);
            local.Y = (float)(local.Y * step);
        }

        local.Z = (float)(local.Z * step);
        var soughtHeading = PositionFrames.HeadingTo(position, target) - PositionFrames.Heading(position.Orientation);
        if (Math.Abs(soughtHeading) < MotionInterpreter.Epsilon)
        {
            soughtHeading = 0f;
        }

        if (soughtHeading < -MotionInterpreter.Epsilon)
        {
            soughtHeading += 360f;
        }

        offset = new AcFrame(local, PositionFrames.WithHeading(offset.Orientation, soughtHeading));
    }
}
