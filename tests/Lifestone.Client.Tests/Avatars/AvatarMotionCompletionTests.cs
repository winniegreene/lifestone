using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The part array's completion check in every object update (retail <c>CPhysicsObj::UpdateObjectInternal</c> →
/// <c>CPartArray::HandleMovement</c> → <c>MotionTableManager::UseTime</c>), on the human Setup 0x02000001 and motion table
/// 0x09000001: a queued motion without animations completes in the next update, so the interpreter's pending motions
/// drain, and the MoveTo's steering, which waits while motions are pending, runs.
/// </summary>
public sealed class AvatarMotionCompletionTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanSetup = 0x02000001;
    private const uint HumanTable = 0x09000001;
    private const double Frame = 1.0 / 60.0;

    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    private double _now;

    [RequiresDatsFact]
    public void A_server_human_has_no_motion_pending_a_frame_after_creation_or_after_a_turn()
    {
        var set = new AvatarSet(Sources());
        Assert.Equal(AvatarApply.Applied, set.Create(Human(2, new Vector3(50, 50, 0)), now: 0));
        var body = set.All[0].Body;
        var motion = body.Motion!;
        Assert.True(motion.MotionsPending); // enter_default_state's Ready, which has no animations
        Advance(set, frames: 1);
        Assert.False(motion.MotionsPending);
        Assert.Equal(0, body.Parts!.PendingAnimationCount);

        Assert.Equal(AvatarApply.Applied, set.SetMovement(2, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.Ready, turn: MotionCommands.TurnRight), []));
        var turning = FramesUntilSettled(set, motion);
        Assert.Equal(MotionCommands.TurnRight, motion.Interpreted.TurnCommand);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(2, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.Ready), []));
        var stopped = FramesUntilSettled(set, motion);
        Assert.Equal(0u, motion.Interpreted.TurnCommand);

        // Only the motions with link animations wait for them; every other one completes in the update after it.
        Assert.InRange(turning, 1, 60);
        Assert.InRange(stopped, 1, 60);
        Assert.Equal(0, body.Parts.PendingAnimationCount);
    }

    [RequiresDatsFact]
    public void A_moveto_object_steers_towards_a_target_that_moves_sideways()
    {
        var set = new AvatarSet(Sources());
        Assert.Equal(AvatarApply.Applied, set.Create(Human(2, new Vector3(50, 40, 0)), now: 0));
        Assert.Equal(AvatarApply.Applied, set.Create(Human(3, new Vector3(50, 90, 0)), now: 0));
        var chaser = set.All[0].Body;
        var target = set.All[1].Body;
        Advance(set, frames: 2);

        var moveTo = new WireMoveTo(3, target.Position, new MovementParameters(), RunRate: 1f);
        var movement = new WireMovement(WireMovementType.MoveToObject, MotionCommandTable.ToWire(MotionCommands.NonCombat), false, false, default, MoveTo: moveTo);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(2, isSelf: false, autonomous: false, movement, []));
        Advance(set, frames: 120);
        Assert.True(chaser.Movement!.IsMovingTo);
        Assert.True(chaser.Position.Origin.Y > 45f, $"the chaser did not set off: {chaser.Position.Origin}");
        Assert.InRange(Off(chaser, target), 0f, 20f);

        // The target steps 30 m east: the next report moves the MoveTo's goal, and the chaser turns towards it.
        Assert.Equal(AvatarApply.Applied, set.Move(3, AvatarRig.At(target.Position.Origin + new Vector3(30, 0, 0)), teleportAdvanced: true, hasContact: true, placement: null, now: _now));
        var before = Off(chaser, target);
        Assert.True(before > 30f, $"the target is only {before}° off");
        Advance(set, frames: 180);
        Assert.True(chaser.Movement.IsMovingTo);
        var after = Off(chaser, target);
        Assert.True(after <= 20f, $"the chaser's heading {chaser.Heading}° is still {after}° off the target (was {before}°)");
        Assert.True(chaser.Heading is > 5f and < 90f, $"heading {chaser.Heading}°");
    }

    /// <summary>How far the chaser's heading is off the heading to the target, in degrees (0-180).</summary>
    private static float Off(AvatarBody chaser, AvatarBody target)
    {
        var diff = MathF.Abs(PositionFrames.HeadingTo(chaser.Position, target.Position) - chaser.Heading) % 360f;
        return diff > 180f ? 360f - diff : diff;
    }

    private static AvatarCreate Human(uint guid, Vector3 origin) =>
        AvatarRig.HumanAt(guid, origin) with { SetupId = HumanSetup, MotionTableId = HumanTable, HasContact = true };

    private AvatarSources Sources()
    {
        var dats = fixture.Dats;
        return AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
    }

    /// <summary>Advances until no motion is pending; returns how many frames that took (at most 2 s).</summary>
    private int FramesUntilSettled(AvatarSet set, MotionInterpreter motion)
    {
        for (var frames = 1; frames <= 120; frames++)
        {
            Advance(set, frames: 1);
            if (!motion.MotionsPending)
            {
                return frames;
            }
        }

        Assert.Fail($"{motion.PendingMotionCount} motions still pending after 2 s: {string.Join(", ", motion.PendingMotions.Select(p => MotionCommandTable.NameOf(p.Motion) ?? $"0x{p.Motion:X8}"))}");
        return 0;
    }

    private void Advance(AvatarSet set, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            _now += Frame;
            set.Advance(_now, Viewer, long.MaxValue);
        }
    }
}
