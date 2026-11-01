using Lifestone.Client.Scheduling;

namespace Lifestone.Client.Tests;

/// <summary>
/// M5 8 build: the player's build runs as three steps on three frames. The steps' own logic needs Godot (the play-check <c>player-build-staged</c> reads the real build);
/// these tests hold what the split rests on: the steps run in order and each once, the union of one-per-frame runs equals the single call, a failed or cancelled build stops,
/// and the per-step milliseconds the login's <c>player built</c> line carries are the steps' own.
/// </summary>
public sealed class StagedWorkTests
{
    private const string First = "avatar_other_meshes";
    private const string Second = "drawn_meshes";
    private const string Third = "body_and_camera";

    /// <summary>A manual clock in milliseconds; each step advances it by its cost.</summary>
    private sealed class FakeClock
    {
        public double Now;
    }

    private static StagedWork Make(List<string> effects, FakeClock? clock = null, Action? onCancel = null, int failAt = -1, double[]? costs = null)
    {
        var names = new[] { First, Second, Third };
        var steps = names.Select((name, i) => new StagedStep(name, () =>
        {
            effects.Add(name);
            if (clock is not null && costs is not null)
            {
                clock.Now += costs[i];
            }

            return i != failAt;
        })).ToArray();
        return new StagedWork(steps, onCancel, clock is null ? null : () => clock.Now);
    }

    [Fact]
    public void One_step_per_call_in_order_each_once_and_the_union_equals_the_single_run()
    {
        var staged = new List<string>();
        var work = Make(staged);
        var frames = 0;
        while (work.HasNext)
        {
            Assert.Equal(frames == 0 ? First : frames == 1 ? Second : Third, work.NextName);
            Assert.True(work.RunNext());
            frames++;
            Assert.Equal(frames, work.Done);
            Assert.Equal(staged.Count, frames); // exactly one step's effects per call: no frame carries the whole build
        }

        var single = new List<string>();
        var all = Make(single);
        Assert.True(all.RunAll());

        Assert.Equal(3, frames);
        Assert.True(work.IsComplete);
        Assert.Equal([First, Second, Third], staged);
        Assert.Equal(single, staged);
        Assert.Equal(all.Names, work.Names);
        Assert.False(work.RunNext()); // nothing left, and nothing runs twice
        Assert.Equal(3, staged.Count);
    }

    [Fact]
    public void A_failed_step_stops_the_build_and_the_later_steps_never_run()
    {
        var effects = new List<string>();
        var work = Make(effects, failAt: 0);
        Assert.False(work.RunNext());
        Assert.True(work.IsFailed);
        Assert.False(work.IsComplete);
        Assert.False(work.HasNext);
        Assert.False(work.RunNext());
        Assert.False(work.RunAll());
        Assert.Equal([First], effects);
    }

    [Fact]
    public void A_step_that_throws_leaves_the_build_failed_and_rethrows()
    {
        var ran = new List<string>();
        var work = new StagedWork(
        [
            new StagedStep("a", () => { ran.Add("a"); return true; }),
            new StagedStep("b", () => throw new InvalidOperationException("boom")),
            new StagedStep("c", () => { ran.Add("c"); return true; }),
        ]);
        Assert.True(work.RunNext());
        Assert.Throws<InvalidOperationException>(() => work.RunNext());
        Assert.True(work.IsFailed);
        Assert.False(work.RunNext());
        Assert.Equal(["a"], ran);
    }

    [Fact]
    public void Cancelled_between_two_steps_runs_the_clean_up_once_and_no_further_step()
    {
        var effects = new List<string>();
        var cleanUps = 0;
        var work = Make(effects, onCancel: () => cleanUps++);
        Assert.True(work.RunNext());
        work.Cancel();
        work.Cancel();
        Assert.True(work.IsCancelled);
        Assert.False(work.HasNext);
        Assert.False(work.RunNext());
        Assert.False(work.IsComplete);
        Assert.Equal(1, cleanUps);
        Assert.Equal([First], effects);
    }

    [Fact]
    public void Cancelling_a_finished_build_changes_nothing()
    {
        var cleanUps = 0;
        var work = Make([], onCancel: () => cleanUps++);
        Assert.True(work.RunAll());
        work.Cancel();
        Assert.False(work.IsCancelled);
        Assert.True(work.IsComplete);
        Assert.Equal(0, cleanUps);
    }

    [Fact]
    public void Each_steps_milliseconds_are_its_own_and_reach_the_log_fields_in_order()
    {
        var clock = new FakeClock();
        var work = Make([], clock, costs: [9.5, 8.25, 3.0]);

        Assert.True(work.RunNext());
        var partial = work.TimingFields();
        Assert.Equal([("build_steps", (object?)"1/3"), ($"{First}_ms", 9.5)], partial);

        Assert.True(work.RunNext());
        Assert.True(work.RunNext());
        var fields = work.TimingFields();
        Assert.Equal(
            [("build_steps", (object?)"3/3"), ($"{First}_ms", 9.5), ($"{Second}_ms", 8.25), ($"{Third}_ms", 3.0)],
            fields);
        Assert.Equal([9.5, 8.25, 3.0], work.Timings);
        Assert.Equal(20.75, work.TotalMilliseconds);
    }

    [Fact]
    public void A_failed_steps_time_is_kept_and_a_build_needs_steps_with_unique_names()
    {
        var clock = new FakeClock();
        var work = Make([], clock, failAt: 1, costs: [1.0, 2.0, 4.0]);
        Assert.True(work.RunNext());
        Assert.False(work.RunNext());
        Assert.Equal([1.0, 2.0], work.Timings);
        Assert.Equal("2/3", work.TimingFields()[0].Value);

        Assert.Throws<ArgumentException>(() => new StagedWork([]));
        Assert.Throws<ArgumentException>(() => new StagedWork([new StagedStep("a", () => true), new StagedStep("a", () => true)]));
    }
}
