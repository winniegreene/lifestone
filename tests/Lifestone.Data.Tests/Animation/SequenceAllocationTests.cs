using Lifestone.Client.Tests;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// <see cref="AnimSequence"/> reuses the nodes it removes: a motion change that swaps links and cycles (what
/// <c>CMotionTable</c> does to the sequence on every movement) allocates nothing once the sequence has held as many
/// nodes, and the reused nodes play like new ones.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SequenceAllocationTests
{
    private const uint Link = 1;
    private const uint Cycle = 2;
    private const uint OtherCycle = 3;

    [Fact]
    public void Swapping_links_and_cycles_allocates_nothing_once_warm()
    {
        var seq = new AnimSequence(new SequencePlaybackTests.FakeAnimations().Add(Link, 3).Add(Cycle, 4).Add(OtherCycle, 5));
        var judged = 0;
        for (var i = 0; i < 400; i++)
        {
            var window = AllocationWindow.Open();
            Swap(seq, (i & 1) == 0 ? Cycle : OtherCycle);
            var (bytes, gcInside) = window.Close();
            if (i < 4 || gcInside)
            {
                continue;
            }

            Assert.Equal(0, bytes);
            judged++;
        }

        Assert.True(judged > 300);
    }

    [Fact]
    public void A_reused_node_takes_its_new_animation_range_and_framerate()
    {
        var seq = new AnimSequence(new SequencePlaybackTests.FakeAnimations().Add(Link, 3).Add(Cycle, 4).Add(OtherCycle, 5));
        seq.AppendAnimation(new AnimDataRef(Cycle, 1, 2, -15f));
        seq.MultiplyCyclicAnimationFramerate(-1f); // swaps the bounds and the sign of the old node
        var old = seq.CurrentNode!.Value;
        seq.ClearAnimations();

        seq.AppendAnimation(new AnimDataRef(OtherCycle, 0, -1, 30f));
        var node = Assert.Single(seq.Nodes);
        Assert.Same(old, node); // reused, not allocated
        Assert.Same(seq.FirstCyclic!.Value, node);
        Assert.Same(seq.CurrentNode!.Value, node);
        Assert.Equal((OtherCycle, 0, 4, 30f), (node.AnimId, node.LowFrame, node.HighFrame, node.Framerate));
        Assert.Equal(5, node.Anim!.NumFrames);
        Assert.Equal(0.0, seq.FrameNumber);
    }

    /// <summary>
    /// What a motion change does: the cyclic animations go, a link and a new cycle are appended, and the link plays
    /// out (apricot drops it).
    /// </summary>
    private static void Swap(AnimSequence seq, uint cycle)
    {
        seq.RemoveCyclicAnims();
        seq.AppendAnimation(new AnimDataRef(Link, 0, -1, 30f));
        seq.AppendAnimation(new AnimDataRef(cycle, 0, -1, 30f));
        for (var step = 0; step < 6; step++)
        {
            seq.Update(1.0 / 60.0);
        }
    }
}
