using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The reducer's allocation budget (ST8): for known objects, applying position, motion and vector updates, publishing
/// their events and mirroring the player allocates nothing. Measured alone, outside the parallel test collections.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ReducerAllocationTests
{
    private const uint Npc = 0x80000100;
    private const int Updates = 1000;

    [Fact]
    public void Applying_1000_position_motion_and_vector_updates_allocates_nothing()
    {
        var h = new ReducerHarness(track: false);
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(0x860201AD)), false);
        long moved = 0;
        long motions = 0;
        long vectors = 0;
        h.Bus.Subscribe((in ObjectMoved e) => moved += e.Guid == Npc ? 1 : 0);
        h.Bus.Subscribe((in ObjectMotionChanged e) => motions++);
        h.Bus.Subscribe((in ObjectVectorChanged e) => vectors++);

        // Motion messages are net's classes: built before measuring, as the dispatcher would have built them.
        const int Rounds = 7;
        var motion = Motion();
        var npcMotions = new UpdateMotionMsg[Rounds * Updates];
        var playerMotions = new UpdateMotionMsg[Rounds * Updates];
        for (var i = 0; i < npcMotions.Length; i++)
        {
            npcMotions[i] = new UpdateMotionMsg(Npc, 1, (ushort)(i + 2), 1, false, motion);
            playerMotions[i] = new UpdateMotionMsg(PlayerGuid, 1, (ushort)(i + 2), 1, true, motion);
        }

        var round = 0;
        var context = h.Context;
        var reducer = h.Reducer;
        AssertAllocatesNothing(() =>
        {
            var start = round++ * Updates;
            for (var i = 0; i < Updates; i++)
            {
                var stamp = (ushort)(start + i + 2);
                reducer.OnUpdatePosition(context, new UpdatePositionMsg(Npc, PositionFlags.Grounded, new AcPosition(0x860201AD, new Vector3(i, 1, 2), Quaternion.Identity), null, null, 1, stamp, 1, 1));
                reducer.OnUpdatePosition(context, new UpdatePositionMsg(PlayerGuid, PositionFlags.Grounded, new AcPosition(0x860201AD, new Vector3(i, 1, 2), Quaternion.Identity), null, null, 1, stamp, 1, 1));
                reducer.OnUpdateMotion(context, npcMotions[start + i]);
                reducer.OnUpdateMotion(context, playerMotions[start + i]);
                reducer.OnVectorUpdate(context, new VectorUpdateMsg(Npc, Vector3.UnitX, Vector3.Zero, 1, stamp));
            }
        });

        Assert.True(moved >= 2 * Updates, $"moved {moved}");
        Assert.True(motions >= 4 * Updates);
        Assert.True(vectors >= 2 * Updates);
        Assert.Equal(0, h.Reducer.Counters.Dropped);
    }

    /// <summary>
    /// Runs <paramref name="work"/> once to warm up, then measures it until a window without a GC inside is seen (at most
    /// five tries), and requires that window to have allocated nothing.
    /// </summary>
    private static void AssertAllocatesNothing(Action work)
    {
        work();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var window = AllocationWindow.Open();
            work();
            var (bytes, gcInside) = window.Close();
            if (!gcInside)
            {
                Assert.Equal(0, bytes);
                return;
            }
        }

        Assert.Fail("every measured window had a GC inside");
    }
}
