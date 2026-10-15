using System.Numerics;
using Lifestone.Client.Tests;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The per-frame and per-message paths of the motion code allocate nothing once warm: the position managers' offset
/// adjustment (every frame of every interpolating object) and the interpreter's handling of a server motion state. On a
/// real motion player the motions a state queues still allocate in M1's motion-table code, measured per queued motion.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class MotionAllocationTests
{
    [Fact]
    public void Adjusting_offsets_allocates_nothing()
    {
        var pm = new PositionManager();
        var position = new CellFrame(0xA9B40019, new AcFrame(new Vector3(50, 10, 0), Quaternion.Identity));
        var ctx = new PositionContext(position, HasContact: true, IsThePlayer: false, MaxSpeed: 4f, AdjustedMaxSpeed: 4f);
        pm.InterpolateTo(new CellFrame(0xA9B40019, new AcFrame(new Vector3(50, 180, 0), Quaternion.Identity)), keepHeading: true, ctx);
        pm.ConstrainTo(position, 10f, 1000f, ctx); // slows the steps a little, never stops them

        var judged = 0;
        for (var frame = 0; frame < 400; frame++)
        {
            ctx = ctx with { Position = position };
            var offset = AcFrame.Identity;
            var window = AllocationWindow.Open();
            pm.AdjustOffset(ref offset, 1.0 / 30.0, ctx);
            _ = pm.UseTime(ctx);
            var (bytes, gcInside) = window.Close();
            position = position with { Frame = AcFrame.Combine(position.Frame, offset) };
            if (frame < 10 || gcInside)
            {
                continue;
            }

            Assert.Equal(0, bytes);
            judged++;
        }

        Assert.True(pm.IsInterpolating);
        Assert.True(judged > 300);
    }

    [Fact]
    public void Applying_server_motion_states_allocates_nothing()
    {
        var host = new FakeMotionHost();
        var motion = new MotionInterpreter(new SilentParts(), host);
        motion.EnterDefaultState();
        var walk = MotionRig.State(forward: MotionCommands.WalkForward);
        var run = MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 1.5f, turn: MotionCommands.TurnRight);
        run.AddAction(MotionCommands.MagicPowerUp01, 1f, 1, false);

        var judged = 0;
        for (var i = 0; i < 400; i++)
        {
            run.Actions[0] = run.Actions[0] with { Stamp = (ushort)((i + 1) & 0x7FFF) };
            var window = AllocationWindow.Open();
            motion.MoveToInterpretedState((i & 1) == 0 ? walk : run);
            motion.HandleExitWorld();
            var (bytes, gcInside) = window.Close();
            if (i < 10 || gcInside)
            {
                continue;
            }

            Assert.Equal(0, bytes);
            judged++;
        }

        Assert.True(judged > 300);
    }

    /// <summary>
    /// The same server motion states on a real <see cref="MotionPlayer"/>. The interpreter, the player and the sequence
    /// allocate nothing; each motion the states queue in the motion table allocates in M1 code that is not yet
    /// allocation-free (see <see cref="MotionTableBytesPerQueuedMotion"/>), so the bytes are measured per queued motion.
    /// </summary>
    [Fact]
    public void Applying_server_motion_states_on_a_motion_player_allocates_only_for_the_motions_it_queues()
    {
        var (table, animations) = SyntheticMotionTable.Create();
        var player = new MotionPlayer(table, animations);
        var host = new FakeMotionHost { Weenie = FakeWeenie.RemoteCreature() };
        var motion = new MotionInterpreter(player, host);
        host.Interpreter = motion;
        motion.EnterDefaultState();
        var walk = MotionRig.State(forward: MotionCommands.WalkForward);
        var run = MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 1.5f);
        var ready = MotionRig.State();

        long bytes = 0;
        long queued = 0;
        var judged = 0;
        for (var i = 0; i < 600; i++)
        {
            var state = (i % 3) switch { 0 => walk, 1 => run, _ => ready };
            var before = player.MotionsQueued;
            var window = AllocationWindow.Open();
            motion.MoveToInterpretedState(state);
            for (var step = 0; step < 6; step++)
            {
                player.Update(1.0 / 60.0);
            }

            var (b, gcInside) = window.Close();
            if (i < 30 || gcInside)
            {
                continue;
            }

            bytes += b;
            queued += player.MotionsQueued - before;
            judged++;
        }

        Assert.True(judged > 500);
        Assert.True(queued >= judged, $"{queued} motions queued by {judged} changes of state");
        Assert.True(
            bytes <= MotionTableBytesPerQueuedMotion * queued,
            $"{bytes} B for {queued} queued motions ({(double)bytes / queued:0.#} B each); the motion table's share is at most {MotionTableBytesPerQueuedMotion} B each");
    }

    /// <summary>
    /// A bound on what M1's motion-table code allocates per motion it queues, on average: <c>MotionTableManager</c>'s
    /// pending entry (a <c>PendingMotion</c> and its <c>LinkedListNode</c>, 72 B), the boxed enumerator of
    /// <c>MotionTableLogic.AddMotion</c>'s <c>foreach</c> for each motion data it appends (32 B; a substate change
    /// appends a link and a cycle), and <c>ReModify</c>'s copy of the state while modifiers are on. The crowd measures
    /// about 94 B. TODO(avatars): 0 once the lead makes them allocation-free (requested in the M2 avatars fix pass);
    /// this test and the avatar crowd's then pin 0 bytes.
    /// </summary>
    private const long MotionTableBytesPerQueuedMotion = 200;

    /// <summary>A part array that accepts every motion and records nothing.</summary>
    private sealed class SilentParts : IMotionParts
    {
        public WeenieError DoInterpretedMotion(uint motion, float speed) => WeenieError.None;

        public WeenieError StopInterpretedMotion(uint motion, float speed) => WeenieError.None;

        public void StopCompletelyInternal()
        {
        }

        public void InitializeMotionTables()
        {
        }

        public void CheckForCompletedMotions()
        {
        }

        public void RemoveLinkAnimations()
        {
        }
    }
}
