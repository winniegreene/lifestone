using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 0 fix 0-R2: <c>godot_errors</c> counts Godot's own errors only, as the smoke's does. Lifestone's echoed <c>Log.Error</c> lines (bracketed by <see cref="GodotErrorTally.Echo"/>) and warnings are skipped.</summary>
public sealed class GodotErrorTallyTests
{
    // One test class touches the static tally; the cases read deltas so they hold in any order.
    [Fact]
    public void A_godot_error_counts_a_warning_does_not()
    {
        var before = GodotErrorTally.Count;

        Assert.True(GodotErrorTally.Record(isWarning: false));
        Assert.False(GodotErrorTally.Record(isWarning: true));

        Assert.Equal(before + 1, GodotErrorTally.Count);
    }

    [Fact]
    public void An_error_inside_the_echo_scope_is_lifestones_own_and_is_not_counted_and_the_next_one_counts()
    {
        var before = GodotErrorTally.Count;

        using (GodotErrorTally.Echo())
        {
            Assert.True(GodotErrorTally.Echoing);
            Assert.False(GodotErrorTally.Record(isWarning: false)); // what Godot calls back while GD.PushError echoes a Log.Error line
        }

        Assert.False(GodotErrorTally.Echoing);
        Assert.Equal(before, GodotErrorTally.Count);
        Assert.True(GodotErrorTally.Record(isWarning: false));
        Assert.Equal(before + 1, GodotErrorTally.Count);
    }

    [Fact]
    public void Nested_echoes_leave_only_at_the_outer_end()
    {
        using (GodotErrorTally.Echo())
        {
            using (GodotErrorTally.Echo())
            {
                Assert.True(GodotErrorTally.Echoing);
            }

            Assert.True(GodotErrorTally.Echoing);
        }

        Assert.False(GodotErrorTally.Echoing);
    }

    [Fact]
    public void An_echo_on_one_thread_does_not_hide_a_real_error_on_another()
    {
        var before = GodotErrorTally.Count;
        var counted = false;
        using (GodotErrorTally.Echo())
        {
            var other = new Thread(() => counted = GodotErrorTally.Record(isWarning: false));
            other.Start();
            other.Join();
        }

        Assert.True(counted);
        Assert.Equal(before + 1, GodotErrorTally.Count);
    }

    [Fact]
    public void Entering_the_echo_scope_and_recording_allocate_nothing()
    {
        for (var i = 0; i < 10; i++)
        {
            using (GodotErrorTally.Echo())
            {
                GodotErrorTally.Record(isWarning: false);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            using (GodotErrorTally.Echo())
            {
                GodotErrorTally.Record(isWarning: false);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
