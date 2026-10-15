using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Messages;
using Lifestone.Data.World;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The adapter's allocation budget (m2-plan G5 W6: an allocation test of the layer path): once the queue has grown, taking events from the bus and applying them
/// (a server object's positions, vectors and state) allocates nothing. A CreateObject allocates its <see cref="AvatarCreate"/>, once per create, and is not in
/// this budget. Run in the non-parallel <see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SessionAvatarAdapterAllocationTests
{
    private const uint Npc = 0x80000100;
    private const uint Cell = AvatarRig.HoltburgCell;

    [Fact]
    public void Positions_vectors_and_states_of_a_standing_object_allocate_nothing_through_the_queue()
    {
        var h = new ReducerHarness(track: false);
        var avatars = new AvatarSet(AvatarRig.Sources());
        using var adapter = new SessionAvatarAdapter(h.Bus, h.State, avatars, () => 0.0);
        h.Reducer.OnPlayerCreate(h.Context, new PlayerCreateMsg(ReducerHarness.PlayerGuid)); // the SmartBox holds every object message until F746
        var msg = Create(Npc, Stamps(), Position(Cell, x: 10), name: "Guard", setup: AvatarRig.Human);
        h.Reducer.OnCreateObject(h.Context, msg with { Physics = msg.Physics with { MotionTableId = AvatarRig.Table, Flags = msg.Physics.Flags | PhysicsDescFlags.MTable, State = PhysicsState.Gravity | PhysicsState.ReportCollisions }, Weenie = msg.Weenie with { ItemType = 0x10 } }, false);
        adapter.Drain();
        Assert.True(avatars.TryGet(Npc, out _));

        var position = new CellFrame(Cell, new Lifestone.Data.Animation.AcFrame(new Vector3(10, 0, 0), Quaternion.Identity));
        var moved = new ObjectMoved(Npc, position, PositionVerdict.Remote, false, true, null, null);
        var vector = new ObjectVectorChanged(Npc, Vector3.Zero, Vector3.Zero);
        var state = new ObjectStateChanged(Npc, false, PhysicsState.Gravity, PhysicsState.Gravity | PhysicsState.ReportCollisions);

        void Work()
        {
            for (var i = 0; i < 1000; i++)
            {
                h.Bus.Publish(in moved);
                h.Bus.Publish(in vector);
                h.Bus.Publish(in state);
            }

            adapter.Drain();
        }

        // Warm: the queue's ring grows to the largest burst once.
        Work();
        AllocationAssert.Nothing(Work);
        Assert.Equal(0, adapter.Stats.Dropped);
        Assert.True(adapter.Stats.Failed + adapter.Stats.Unknown == 0, $"failed {adapter.Stats.Failed}, unknown {adapter.Stats.Unknown}");
    }
}
