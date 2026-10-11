using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>
/// One visible object for the spell viewer: placement, optional animation (<see cref="IPoseDriver"/>), effects
/// (<see cref="EffectObject"/>) and simple ballistic physics, stepped like retail <c>CPhysicsObj::update_object</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdvanceTo"/> re-expresses <c>CPhysicsObj::update_object</c> (acclient 11.4186 PORTAL/cphysobj/CPhysicsObj.c):
/// elapsed times ≤ ε or &gt; 2 s are skipped (the clock jumps), longer gaps are split into quanta of at most
/// <see cref="MaxQuantum"/> (0.2 s), and a final remainder ≤ <see cref="MinQuantum"/> (1/30 s) is carried to the next call.
/// </para>
/// <para>
/// Each quantum (<c>UpdateObjectInternal</c> → <c>UpdatePositionInternal</c>): the animation sequence advances and
/// returns its root-motion offset, scaled by the object scale while <see cref="OnGround"/> (retail
/// <c>transient_state &amp; 2</c>) and zeroed otherwise; the offset is combined with the object frame; then
/// <c>UpdatePhysicsInternal</c> integrates <see cref="Velocity"/>/<see cref="Acceleration"/>/<see cref="Omega"/>
/// (speed capped at 50 m/s; speeds under 0.25 m/s are zeroed; ground friction is not modelled); with
/// <see cref="AlignPath"/> the heading follows the motion (<c>Frame::set_vector_heading</c>); then
/// <see cref="EffectObject.Update"/> runs hooks, particles and scripts.
/// </para>
/// </remarks>
public sealed class EffectActor
{
    public const double MaxQuantum = 1.0 / 5.0;
    public const double MinQuantum = 1.0 / 30.0;
    private const double Epsilon = 0.00019999999;

    private readonly AcFrame[] _partFrames;
    private double _updateTime;

    public EffectActor(EffectObject effects, IPoseDriver? motion, int partCount, AcFrame frame, float scale, double now)
    {
        Effects = effects;
        Motion = motion;
        Frame = frame;
        Scale = scale;
        _partFrames = new AcFrame[partCount];
        _updateTime = now;
        PoseParts();
        Effects.SetPlacement(now, Frame, _partFrames);
    }

    public EffectObject Effects { get; }

    /// <summary>Animation driver: a <see cref="MotionPlayer"/>, a <see cref="DefaultAnimationPlayer"/>, or null (static pose).</summary>
    public IPoseDriver? Motion { get; }

    /// <summary>Object world frame (AC coordinates).</summary>
    public AcFrame Frame { get; set; }

    /// <summary>Object scale (multiplies part offsets and root motion).</summary>
    public float Scale { get; set; }

    public bool OnGround { get; set; } = true;

    public Vector3 Velocity { get; set; }

    public Vector3 Acceleration { get; set; }

    /// <summary>Global angular velocity (retail <c>m_omegaVector</c>, applied with <c>Frame::grotate</c>).</summary>
    public Vector3 Omega { get; set; }

    /// <summary>Retail <c>ALIGNPATH_PS</c>: turn to face the direction of travel.</summary>
    public bool AlignPath { get; set; }

    /// <summary>The clock value of the last processed quantum.</summary>
    public double UpdateTime => _updateTime;

    /// <summary>Current world frames of the parts.</summary>
    public ReadOnlySpan<AcFrame> PartFrames => _partFrames;

    /// <summary>Advances the object to <paramref name="now"/> (seconds on the caller's clock).</summary>
    public void AdvanceTo(double now)
    {
        var dt = now - _updateTime;
        if (dt <= Epsilon || dt > 2.0)
        {
            _updateTime = now;
            return;
        }

        var time = _updateTime;
        if (dt > MaxQuantum)
        {
            while (dt > MaxQuantum)
            {
                time += MaxQuantum;
                UpdateObjectInternal(MaxQuantum, time);
                dt -= MaxQuantum;
            }

            if (dt > MinQuantum)
            {
                time += dt;
                UpdateObjectInternal(dt, time);
            }
        }
        else
        {
            time += dt;
            UpdateObjectInternal(dt, time);
        }

        _updateTime = time;
    }

    private void UpdateObjectInternal(double quantum, double time)
    {
        var offset = AcFrame.Identity;
        if (Motion is not null)
        {
            var update = Motion.Update(quantum);
            offset = update.RootMotion;
            offset = offset with { Origin = offset.Origin * (OnGround ? Scale : 0f) };
            foreach (var hook in update.Hooks)
            {
                Effects.QueueAnimHook(hook);
            }
        }

        var oldOrigin = Frame.Origin;
        var frame = AcFrame.Combine(Frame, offset);
        frame = UpdatePhysics((float)quantum, frame);
        if (AlignPath && frame.Origin != oldOrigin)
        {
            frame = SetVectorHeading(frame, frame.Origin - oldOrigin);
        }

        Frame = frame;
        PoseParts();
        Effects.Update(time, Frame, _partFrames);
    }

    /// <summary>Retail <c>CPhysicsObj::UpdatePhysicsInternal</c> without ground friction.</summary>
    private AcFrame UpdatePhysics(float quantum, AcFrame frame)
    {
        var v = Velocity;
        var speed2 = v.LengthSquared();
        if (speed2 > 0f)
        {
            if (speed2 > 50f * 50f)
            {
                v = Vector3.Normalize(v) * 50f;
                speed2 = 50f * 50f;
            }

            if (speed2 - 0.25 * 0.25 < Epsilon)
            {
                v = Vector3.Zero;
            }

            frame = frame with { Origin = frame.Origin + v * quantum + Acceleration * 0.5f * quantum * quantum };
        }

        Velocity = v + Acceleration * quantum;
        return frame.GRotate(Omega * quantum);
    }

    private void PoseParts()
    {
        if (Motion?.Pose is { } pose)
        {
            PartPose.Update(Frame, pose, new Vector3(Scale), _partFrames);
            for (var i = pose.Count; i < _partFrames.Length; i++)
            {
                _partFrames[i] = Frame;
            }
        }
        else
        {
            Array.Fill(_partFrames, Frame);
        }
    }

    /// <summary>
    /// Retail <c>Frame::set_vector_heading</c>: yaw from the horizontal direction, pitch = asin(z), no roll
    /// (Shoemake Euler order XYZ static, <c>Frame::euler_set_rotate(pitch, 0, −heading, 0)</c>).
    /// The decompiler prints <c>atan2(x, y)</c>; we use <c>atan2(y, x)</c> like ACE <c>AFrame.set_vector_heading</c>,
    /// which makes the identity orientation face +Y (north) as AC objects do.
    /// </summary>
    public static AcFrame SetVectorHeading(AcFrame frame, Vector3 heading)
    {
        if (Particle.NormalizeCheckSmall(heading, out var n))
        {
            return frame;
        }

        var yawDegrees = (450.0 - Math.Atan2(n.Y, n.X) * (180.0 / Math.PI)) % 360.0;
        var yaw = (float)(-yawDegrees * (Math.PI / 180.0));
        var pitch = (float)Math.Asin(Math.Clamp(n.Z, -1f, 1f));
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        return frame with { Orientation = AcFrame.SetRotate(AcFrame.Hamilton(qz, qx), frame.Orientation) };
    }
}
