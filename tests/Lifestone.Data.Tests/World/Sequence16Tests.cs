using Lifestone.Data.World;

namespace Lifestone.Data.Tests.World;

/// <summary>
/// <see cref="Sequence16"/>, retail <c>CPhysicsObj::is_newer</c> / <c>newer_event</c>: the wrap from 0xFFFF to 0 and
/// the 0x7FFF / 0x8000 boundary; and the 15-bit action stamp rule of <c>CMotionInterp::move_to_interpreted_state</c>:
/// the wrap from 0x7FFF to 0 and the 0x3FFF / 0x4000 boundary.
/// </summary>
public sealed class Sequence16Tests
{
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(5, 5, false)] // equal is not newer
    [InlineData(0xFFFF, 0, true)] // the counter wraps forward
    [InlineData(0, 0xFFFF, false)]
    [InlineData(0xFFF0, 0x0010, true)]
    [InlineData(0x0010, 0xFFF0, false)]
    [InlineData(0, 0x7FFF, true)] // the largest forward step without a wrap
    [InlineData(0x7FFF, 0, false)]
    [InlineData(0, 0x8000, false)] // a distance of 0x8000 counts as a wrap: 0x8000 is older than 0 ...
    [InlineData(0x8000, 0, true)] // ... and 0 is newer than 0x8000
    [InlineData(0x0001, 0x8001, false)]
    [InlineData(0x8001, 0x0001, true)]
    [InlineData(0x8000, 0xFFFF, true)]
    [InlineData(0xFFFF, 0x8000, false)]
    public void Is_newer_follows_the_retail_wrap_rule(int stored, int incoming, bool newer)
    {
        Assert.Equal(newer, Sequence16.IsNewer((ushort)stored, (ushort)incoming));
    }

    [Fact]
    public void Every_step_forward_of_up_to_0x7FFF_is_newer_and_never_the_reverse()
    {
        int[] steps = [1, 2, 0x100, 0x4000, 0x7FFE, 0x7FFF];
        for (var stored = 0; stored <= 0xFFFF; stored++)
        {
            foreach (var step in steps)
            {
                var incoming = (ushort)(stored + step);
                if (!Sequence16.IsNewer((ushort)stored, incoming) || Sequence16.IsNewer(incoming, (ushort)stored))
                {
                    Assert.Fail($"stored 0x{stored:X4}, step 0x{step:X4}");
                }
            }
        }
    }

    [Fact]
    public void Newer_event_stores_only_a_newer_stamp()
    {
        ushort stored = 0xFFFE;
        Assert.True(Sequence16.NewerEvent(ref stored, 0xFFFF));
        Assert.Equal(0xFFFF, stored);
        Assert.True(Sequence16.NewerEvent(ref stored, 0x0000)); // wraps
        Assert.Equal(0x0000, stored);
        Assert.False(Sequence16.NewerEvent(ref stored, 0x0000)); // a repeat
        Assert.False(Sequence16.NewerEvent(ref stored, 0xFFFF)); // older
        Assert.Equal(0x0000, stored);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(5, 6, true)]
    [InlineData(6, 5, false)]
    [InlineData(0xFFFF, 0, true)]
    [InlineData(0, 0x8000, false)]
    public void Not_older_accepts_a_repeat_of_the_current_stamp(int stored, int incoming, bool accepted)
    {
        Assert.Equal(accepted, Sequence16.NotOlder((ushort)stored, (ushort)incoming));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(5, 5, false)] // equal is not newer
    [InlineData(0x7FFF, 0, true)] // the 15-bit counter wraps forward (the 16-bit rule would say false)
    [InlineData(0, 0x7FFF, false)]
    [InlineData(0x7FF0, 0x0010, true)]
    [InlineData(0x0010, 0x7FF0, false)]
    [InlineData(0, 0x3FFF, true)] // the largest forward step without a wrap
    [InlineData(0x3FFF, 0, false)]
    [InlineData(0, 0x4000, false)] // a distance of 0x4000 counts as a wrap: 0x4000 is older than 0 ...
    [InlineData(0x4000, 0, true)] // ... and 0 is newer than 0x4000
    [InlineData(0x0001, 0x4001, false)]
    [InlineData(0x4001, 0x0001, true)]
    [InlineData(0x4000, 0x7FFF, true)]
    [InlineData(0x7FFF, 0x4000, false)]
    public void Is_newer_action_follows_the_retail_15_bit_wrap_rule(int stored, int incoming, bool newer)
    {
        Assert.Equal(newer, Sequence16.IsNewerAction((ushort)stored, (ushort)incoming));
    }

    [Theory]
    [InlineData(0x7FFF, 0x8000, true)] // 0 with the autonomous bit set wraps forward from 0x7FFF
    [InlineData(0x8005, 0x0006, true)] // the stored stamp's flag bit is ignored too
    [InlineData(0x0005, 0x8005, false)] // the same stamp, flagged autonomous, is a repeat
    [InlineData(0x8006, 0x0005, false)]
    public void Is_newer_action_ignores_the_autonomous_bit(int stored, int incoming, bool newer)
    {
        Assert.Equal(newer, Sequence16.IsNewerAction((ushort)stored, (ushort)incoming));
    }

    [Fact]
    public void Every_action_step_forward_of_up_to_0x3FFF_is_newer_and_never_the_reverse()
    {
        int[] steps = [1, 2, 0x100, 0x2000, 0x3FFE, 0x3FFF];
        for (var stored = 0; stored <= Sequence16.ActionStampMask; stored++)
        {
            foreach (var step in steps)
            {
                var incoming = (ushort)((stored + step) & Sequence16.ActionStampMask);
                if (!Sequence16.IsNewerAction((ushort)stored, incoming)
                    || Sequence16.IsNewerAction(incoming, (ushort)stored))
                {
                    Assert.Fail($"stored 0x{stored:X4}, step 0x{step:X4}");
                }
            }
        }
    }
}
