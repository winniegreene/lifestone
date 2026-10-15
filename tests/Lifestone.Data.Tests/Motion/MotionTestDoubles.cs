using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>A weenie with settable answers (retail <c>CWeenieObject</c>'s motion virtuals).</summary>
internal sealed class FakeWeenie : IMotionWeenie
{
    public bool IsCreature { get; set; } = true;

    public bool IsThePlayer { get; set; }

    /// <summary>The run rate <c>InqRunRate</c> reports, or null when it cannot tell.</summary>
    public float? RunRate { get; set; }

    /// <summary>The jump velocity <c>InqJumpVelocity</c> reports for any extent, or null when it cannot tell.</summary>
    public float? JumpVelocity { get; set; }

    public bool CanJumpResult { get; set; } = true;

    /// <summary>The stamina a jump costs, or null when it cannot be paid.</summary>
    public int? StaminaCost { get; set; } = 0;

    public List<float> JumpVelocityQueries { get; } = [];

    public bool TryGetRunRate(out float rate)
    {
        rate = RunRate ?? 0f;
        return RunRate.HasValue;
    }

    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        JumpVelocityQueries.Add(extent);
        velocityZ = JumpVelocity ?? 0f;
        return JumpVelocity.HasValue;
    }

    public bool CanJump(float extent) => CanJumpResult;

    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        staminaCost = StaminaCost ?? 0;
        return StaminaCost.HasValue;
    }

    public static FakeWeenie RemoteCreature() => new() { IsCreature = true, IsThePlayer = false };

    public static FakeWeenie Player(float? runRate = null) => new() { IsCreature = true, IsThePlayer = true, RunRate = runRate };
}

/// <summary>
/// The object a motion interpreter moves (retail <c>CPhysicsObj</c>): settable flags, and
/// <see cref="SetOnWalkable"/> calls back into the interpreter as <c>CPhysicsObj::set_on_walkable</c> does.
/// </summary>
internal sealed class FakeMotionHost : IMotionInterpHost
{
    public bool HasContact { get; set; } = true;

    public bool OnWalkable { get; set; } = true;

    public bool HasGravity { get; set; } = true;

    public bool InCell { get; set; } = true;

    public bool MovementIsAutonomous { get; set; }

    public bool IsFullyConstrained { get; set; }

    public Vector3 Velocity { get; set; }

    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    public IMotionWeenie? Weenie { get; set; } = FakeWeenie.RemoteCreature();

    /// <summary>The interpreter to call back from <see cref="SetOnWalkable"/>.</summary>
    public MotionInterpreter? Interpreter { get; set; }

    public List<Vector3> LocalVelocities { get; } = [];

    public int CancelMoveToCount { get; private set; }

    public int UnstickCount { get; private set; }

    public void SetLocalVelocity(Vector3 localVelocity)
    {
        LocalVelocities.Add(localVelocity);
        Velocity = Vector3.Transform(localVelocity, Orientation);
    }

    public void SetOnWalkable(bool onWalkable)
    {
        var was = OnWalkable;
        OnWalkable = onWalkable;
        if (was && !onWalkable)
        {
            Interpreter?.LeaveGround();
        }
        else if (!was && onWalkable)
        {
            Interpreter?.HitGround();
        }
    }

    public void CancelMoveTo() => CancelMoveToCount++;

    public void UnstickFromObject() => UnstickCount++;
}

/// <summary>One part-array call the interpreter made.</summary>
internal readonly record struct PartsCall(string Kind, uint Motion, float Speed)
{
    public override string ToString() => Kind is "do" or "stop"
        ? string.Create(CultureInfo.InvariantCulture, $"{Kind} {MotionCommandTable.NameOf(Motion) ?? $"0x{Motion:X8}"} @{Speed:0.######}")
        : Kind;

    /// <summary>Equal kind and motion, and speeds within <paramref name="tolerance"/>.</summary>
    public bool Matches(PartsCall other, float tolerance = 1e-5f) =>
        Kind == other.Kind && Motion == other.Motion && MathF.Abs(Speed - other.Speed) <= tolerance;
}

/// <summary>
/// A part array that records the interpreter's calls instead of playing a motion table. Every motion succeeds unless
/// listed in <see cref="Refuse"/>. <see cref="RemoveLinkAnimations"/> completes every pending motion, as the motion
/// table's queue (one entry per queued interpreter motion) would. With <see cref="CompleteOnCheck"/>,
/// <see cref="CheckForCompletedMotions"/> completes them too, as a table whose every motion has no animations would.
/// </summary>
internal sealed class RecordingParts : IMotionParts
{
    public List<PartsCall> Calls { get; } = [];

    public HashSet<uint> Refuse { get; } = [];

    public MotionInterpreter? Interpreter { get; set; }

    /// <summary>
    /// When set, <see cref="CheckForCompletedMotions"/> completes every pending motion with success: the motion table's
    /// queue holds no animations, so <c>MotionTableManager::CheckForCompletedMotions</c> empties it.
    /// </summary>
    public bool CompleteOnCheck { get; set; }

    public WeenieError DoInterpretedMotion(uint motion, float speed)
    {
        Calls.Add(new PartsCall("do", motion, speed));
        return Refuse.Contains(motion) ? WeenieError.NoMtableData : WeenieError.None;
    }

    public WeenieError StopInterpretedMotion(uint motion, float speed)
    {
        Calls.Add(new PartsCall("stop", motion, speed));
        return Refuse.Contains(motion) ? WeenieError.NoMtableData : WeenieError.None;
    }

    public void StopCompletelyInternal() => Calls.Add(new PartsCall("stopall", 0, 0));

    public void InitializeMotionTables() => Calls.Add(new PartsCall("init", 0, 0));

    public void CheckForCompletedMotions()
    {
        Calls.Add(new PartsCall("check", 0, 0));
        while (CompleteOnCheck && Interpreter is { MotionsPending: true } interpreter)
        {
            interpreter.MotionDone(success: true);
        }
    }

    public void RemoveLinkAnimations()
    {
        Calls.Add(new PartsCall("unlink", 0, 0));
        while (Interpreter is { MotionsPending: true } interpreter)
        {
            interpreter.MotionDone(success: false);
        }
    }

    public string[] Describe() => Calls.Select(c => c.ToString()).ToArray();
}

/// <summary>Builds interpreters on recording parts.</summary>
internal static class MotionRig
{
    public static (MotionInterpreter Motion, FakeMotionHost Host, RecordingParts Parts) Create(
        IMotionWeenie? weenie, bool enterDefaultState = true, bool autonomous = false)
    {
        var host = new FakeMotionHost { Weenie = weenie, MovementIsAutonomous = autonomous };
        var parts = new RecordingParts();
        var motion = new MotionInterpreter(parts, host);
        host.Interpreter = motion;
        parts.Interpreter = motion;
        if (enterDefaultState)
        {
            motion.EnterDefaultState();
            parts.Calls.Clear();
        }

        return (motion, host, parts);
    }

    /// <summary>The player: its own weenie, autonomous movement (the raw state drives it).</summary>
    public static (MotionInterpreter Motion, FakeMotionHost Host, RecordingParts Parts, FakeWeenie Weenie) Player(float? runRate = null)
    {
        var weenie = FakeWeenie.Player(runRate);
        var (motion, host, parts) = Create(weenie, autonomous: true);
        return (motion, host, parts, weenie);
    }

    public static InterpretedMotionState State(
        uint forward = MotionCommands.Ready,
        float forwardSpeed = 1f,
        uint sidestep = 0,
        float sidestepSpeed = 1f,
        uint turn = 0,
        float turnSpeed = 1f,
        uint style = MotionCommands.NonCombat) =>
        new()
        {
            CurrentStyle = style,
            ForwardCommand = forward,
            ForwardSpeed = forwardSpeed,
            SideStepCommand = sidestep,
            SideStepSpeed = sidestepSpeed,
            TurnCommand = turn,
            TurnSpeed = turnSpeed,
        };
}
