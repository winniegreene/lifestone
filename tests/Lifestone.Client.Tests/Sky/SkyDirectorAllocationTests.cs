using Lifestone.Client.Sky;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Sky;

/// <summary>
/// No per-frame garbage from the sky driver (docs/sky.md §7). Measured per frame on this thread, in the non-parallel
/// <see cref="AllocationMeasurementCollection"/>; frames with a GC inside are not judged (as the game's own counter does),
/// but must stay rare so the tests keep their teeth.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SkyDirectorAllocationTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    private double Noon(int group = 0) => Lifestone.Client.Navigation.GoldenViewpoints.GameTimeFor(new(0.5, group), fixture.Sky);

    [RequiresDatsFact]
    public void A_running_clock_allocates_nothing_per_frame()
    {
        // docs/sky.md §7: per-frame garbage (and the finalizable StringNames that came with it on the Godot side) made
        // GC stalls of 20-50 ms. At the retail rate a frame must allocate nothing at all.
        var director = new SkyDirector(fixture.Sky);
        var t = Noon();
        var clock = 0.0;
        for (var i = 0; i < 120; i++) // warm-up: slots, buffers, scroll registry, JIT
        {
            director.Update(t += 1 / 60.0, clock += 1 / 60.0);
        }

        const int Frames = 3_600; // one minute at 60 fps: fog ticks every 0.8 s, light ticks every 15 s
        var objectsChanged = 0;
        var allocated = 0L;
        var allocatingFrames = 0;
        var gcFrames = 0;
        for (var i = 0; i < Frames; i++)
        {
            var window = AllocationWindow.Open();
            var frame = director.Update(t += 1 / 60.0, clock += 1 / 60.0);
            _ = director.ScrollOffset(0x01004C36, wrapAddressing: true);
            var (bytes, gcInside) = window.Close();
            objectsChanged += frame.ObjectsChanged ? 1 : 0;
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
            allocatingFrames += bytes != 0 ? 1 : 0;
        }

        // Asserts after the loop: they allocate.
        Assert.Equal(0, objectsChanged);
        Assert.True(gcFrames <= Frames / 100, $"{gcFrames} of {Frames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocatingFrames} of {Frames - gcFrames} frames without a GC allocated {allocated} B in total");
    }

    [RequiresDatsFact]
    public void A_time_lapse_allocates_only_when_objects_or_the_day_change()
    {
        // 600x at 60 fps: 10 game seconds per frame, a little over two Dereth days. Only frames that create or delete
        // a sky object (the scroll registry's sync) or start a new day (one calendar lookup) may allocate.
        var director = new SkyDirector(fixture.Sky);
        var t = Noon();
        var clock = 0.0;
        for (var i = 0; i < 4; i++) // warm-up: slots, buffers, the scroll registry's first step
        {
            director.Update(t += 10.0, clock += 10.0);
        }

        const int Frames = 1_600;
        var lastDay = fixture.Sky.Calendar.ToDate(t).DayOfYear;
        var allowed = 0;
        var allocating = 0;
        var gcFrames = 0;
        string? firstUnexpected = null;
        for (var i = 0; i < Frames; i++)
        {
            t += 10.0;
            clock += 10.0;
            var date = fixture.Sky.Calendar.ToDate(t); // outside the measured window
            var day = date.DayOfYear;
            var atDayEdge = Math.Min(t - date.DayBeginTime, date.DayBeginTime + fixture.Sky.Calendar.DayLength - t) < 0.01;
            var window = AllocationWindow.Open();
            var frame = director.Update(t, clock);
            var (bytes, gcInside) = window.Close();
            var mayAllocate = frame.ObjectsChanged || day != lastDay || atDayEdge;
            lastDay = day;
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allowed += mayAllocate ? 1 : 0;
            allocating += bytes > 0 ? 1 : 0;
            if (bytes != 0 && !mayAllocate && firstUnexpected is null)
            {
                firstUnexpected = $"frame {i} (day fraction {frame.TimeOfDay}) allocated {bytes} B";
            }
        }

        Assert.True(firstUnexpected is null, firstUnexpected);
        Assert.True(gcFrames <= Frames / 100, $"{gcFrames} of {Frames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allowed > 0 && allocating <= allowed);
    }
}
