using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Physics;

/// <summary>Moving objects for the <see cref="PhysicsObj"/> tests: shapes, weenies, an event recorder and a clock.</summary>
internal static class PhysicsObjTestKit
{
    /// <summary>The human Setup 0x02000001's collision (checked against the dats in <see cref="LandscapeCollisionTests"/>).</summary>
    public static PhysicsSetup HumanSetup { get; } = PhysicsSetup.FromParts(
        [new PhysSphere(new Vector3(0, 0, 0.475f), 0.48f), new PhysSphere(new Vector3(0, 0, 1.35f), 0.48f)],
        stepUpHeight: 0.6f,
        stepDownHeight: 1.5f,
        height: 1.8f,
        radius: 0.48f,
        sortingSphere: new PhysSphere(new Vector3(0, 0, 0.9f), 1.0f),
        allowsFreeHeading: false);

    /// <summary>A one-sphere creature shape, 0.5 m round.</summary>
    public static PhysicsSetup CreatureSetup { get; } = PhysicsSetup.FromParts(
        [new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f)],
        stepUpHeight: 0.4f,
        stepDownHeight: 0.4f,
        sortingSphere: new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f));

    /// <summary>A human in <paramref name="world"/> (no motion table unless given).</summary>
    public static PhysicsObj HumanObj(PhysicsWorld world, uint id = 0, IPhysicsWeenie? weenie = null, MotionPlayer? motion = null) =>
        new(world, HumanSetup, id, weenie, motion);

    /// <summary>A motion player over the synthetic NonCombat table (walk 3.12 m/s, run 4 m/s, Falling).</summary>
    public static MotionPlayer SyntheticMotion()
    {
        var (table, animations) = SyntheticMotionTable.Create();
        return new MotionPlayer(table, animations);
    }

    /// <summary>
    /// Steps <paramref name="obj"/> for <paramref name="seconds"/> at <paramref name="fps"/> frames a second through the
    /// world's 30 Hz gate, calling <paramref name="perFrame"/> before each frame.
    /// </summary>
    public static void Run(PhysicsObj obj, double seconds, double fps = 60.0, Action<PhysicsObj>? perFrame = null)
    {
        var world = obj.World;
        var frames = (int)Math.Round(seconds * fps);
        for (var i = 0; i < frames; i++)
        {
            perFrame?.Invoke(obj);
            if (world.UseTime(world.CurrentTime + (1.0 / fps)))
            {
                obj.UpdateObject();
            }
        }
    }

    /// <summary>A flat world around <see cref="PhysicsTestKit.Block"/>, its clock started at 0.</summary>
    public static PhysicsWorld FlatWorld(params PhysStaticObject[] statics)
    {
        var world = PhysicsTestKit.WorldOf(PhysicsTestKit.Flat(PhysicsTestKit.Block, statics));
        world.UseTime(0.0);
        return world;
    }
}

/// <summary>A weenie whose answers the test sets.</summary>
internal sealed class FakePhysicsWeenie : IPhysicsWeenie
{
    public bool IsCreature { get; set; } = true;

    public bool IsThePlayer { get; set; }

    public bool IsPlayer { get; set; }

    public bool IsPK { get; set; }

    public bool IsPKLite { get; set; }

    public bool IsImpenetrable { get; set; }

    /// <summary>The run rate it reports, or null when it cannot tell.</summary>
    public float? RunRate { get; set; }

    /// <summary>The jump velocity it reports for any extent, or null when it cannot tell.</summary>
    public float? JumpVelocity { get; set; }

    public bool TryGetRunRate(out float rate)
    {
        rate = RunRate ?? 0f;
        return RunRate.HasValue;
    }

    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        velocityZ = JumpVelocity ?? 0f;
        return JumpVelocity.HasValue;
    }

    public bool CanJump(float extent) => true;

    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        staminaCost = 0;
        return true;
    }

    public static FakePhysicsWeenie Player() => new() { IsThePlayer = true, IsPlayer = true };

    public static FakePhysicsWeenie OtherPlayer() => new() { IsPlayer = true };

    public static FakePhysicsWeenie Creature() => new();
}

/// <summary>Records what physics reports.</summary>
internal sealed class RecordingPhysicsEvents : IPhysicsEvents
{
    public List<(PhysicsObj Self, Vector3 Velocity, bool HadContact)> Environment { get; } = [];

    public List<(PhysicsObj Self, ObjectCollision Collision)> Objects { get; } = [];

    public List<(PhysicsObj Self, uint OtherId)> Ends { get; } = [];

    public int HookBatches { get; private set; }

    public void OnEnvironmentCollision(PhysicsObj self, Vector3 velocity, bool hadContact) => Environment.Add((self, velocity, hadContact));

    public void OnObjectCollision(PhysicsObj self, in ObjectCollision collision) => Objects.Add((self, collision));

    public void OnObjectCollisionEnd(PhysicsObj self, uint otherId) => Ends.Add((self, otherId));

    public void OnAnimationHooks(PhysicsObj self, IReadOnlyList<AnimHook> hooks) => HookBatches++;
}
