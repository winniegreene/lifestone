using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>The movement channel's allocation budget (step ST11): stamping and handing a send to the network session allocates nothing.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class MovementAllocationTests
{
    [Fact]
    public void Movement_sends_allocate_nothing()
    {
        var h = new ActionHarness();
        h.Present();
        var at = new CellFrame(0x860201AD, new AcFrame(new Vector3(12.3f, -28.5f, 0.005f), Quaternion.Identity));
        var raw = new RawMotionState { ForwardCommand = MotionCommands.WalkForward };
        var sink = h.Api.Movement;
        void Work()
        {
            h.Net.Sends.Clear();
            for (var i = 0; i < 500; i++)
            {
                sink.MoveToState(raw, at, true, false);
                sink.AutonomousPosition(at, true);
                sink.Jump(0.25f, Vector3.UnitZ, at);
            }
        }

        AllocationAssert.Nothing(Work);
        Assert.Equal(1500, h.Net.Sends.Count);
        Assert.Equal(0, h.Api.Channel.SentWhileNotPresent);
    }
}
