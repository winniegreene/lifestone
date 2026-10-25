using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Hosting;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 item 0 c2: the leg guard. An exception in a runner's frame is reported once and trips the guard, so the runner ends its leg through its own fail path and the
/// next frames run no step; the runners are Godot nodes with no offline stand-in (see <see cref="FightLegRunnerTests"/>), so their wiring is pinned by the source.
/// </summary>
public sealed class LegFaultGuardTests
{
    private static readonly string[] Runners =
    [
        "JourneyLegRunner", "FightLegRunner", "HuntLegRunner", "DeathLegRunner", "BuffLegRunner", "ThrowawayLegRunner", "LoginCheckRunner", "RebuffDriverHost",
    ];

    [Fact]
    public void The_first_fault_is_reported_once_with_the_runner_the_stage_the_type_and_the_message_then_the_stack()
    {
        var log = new RecordingLogSink();
        var guard = new LegFaultGuard("JourneyLegRunner", "logincheck", log);
        Assert.False(guard.Tripped);
        Assert.Null(guard.Reason);

        var fault = Thrown(() => throw new ArgumentOutOfRangeException("index", "too far"));
        Assert.True(guard.Catch("_Process (Journey)", fault));

        Assert.True(guard.Tripped);
        Assert.StartsWith("JourneyLegRunner: _Process (Journey) threw ArgumentOutOfRangeException: ", guard.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', guard.Reason!);
        var entries = log.Entries;
        Assert.Equal(2, entries.Count); // the reason, then the whole exception once
        Assert.All(entries, e => Assert.Equal((LogSeverity.Error, "logincheck"), (e.Severity, e.Category)));
        Assert.Equal(guard.Reason, entries[0].Message);
        Assert.Contains("at ", entries[1].Message, StringComparison.Ordinal); // the stack
        Assert.Contains("ArgumentOutOfRangeException", entries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_fault_is_counted_and_reports_nothing_and_the_reason_stays_the_first()
    {
        var log = new RecordingLogSink();
        var guard = new LegFaultGuard("FightLegRunner", "logincheck", log);
        Assert.True(guard.Catch("_Process (Fighting)", Thrown(() => throw new InvalidOperationException("first"))));
        var reason = guard.Reason;
        var written = log.Entries.Count;

        for (var frame = 0; frame < 5; frame++)
        {
            Assert.False(guard.Catch("_Process (LoggingOff)", Thrown(() => throw new NullReferenceException("again"))));
        }

        Assert.Equal(5, guard.Repeats);
        Assert.Equal(reason, guard.Reason);
        Assert.Equal("FightLegRunner: _Process (Fighting) threw InvalidOperationException: first", reason);
        Assert.Equal(written, log.Entries.Count); // no second report
    }

    [Fact]
    public void A_multi_line_message_is_one_line_in_the_reason_and_running_out_of_memory_is_not_guarded()
    {
        var guard = new LegFaultGuard("HuntLegRunner", "logincheck", new RecordingLogSink());
        guard.Catch("_Process (Hunting)", Thrown(() => throw new InvalidOperationException("a\r\nb\nc")));

        Assert.EndsWith("InvalidOperationException: a b c", guard.Reason, StringComparison.Ordinal);
        Assert.True(LegFaultGuard.Guards(new InvalidOperationException()));
        Assert.False(LegFaultGuard.Guards(new OutOfMemoryException()));
    }

    [Fact]
    public void A_runner_that_steps_through_the_guard_runs_no_step_after_the_fault_and_ends_with_the_reason()
    {
        // The wiring every runner has: try the frame; on the first fault start the fail path with the reason; on any later one finish; and the failed state steps no scenario.
        var log = new RecordingLogSink();
        var guard = new LegFaultGuard("FakeRunner", "logincheck", log);
        var steps = 0;
        string? failedWith = null;
        string? finishedWith = null;
        var failed = false;

        void Frame()
        {
            try
            {
                if (failed)
                {
                    return; // the recovery: no scenario step
                }

                steps++;
                throw new InvalidOperationException("boom");
            }
            catch (Exception e) when (LegFaultGuard.Guards(e))
            {
                if (guard.Catch("_Process (Stage)", e))
                {
                    failed = true;
                    failedWith = guard.Reason;
                }
                else
                {
                    finishedWith = guard.Reason;
                }
            }
        }

        for (var frame = 0; frame < 4; frame++)
        {
            Frame();
        }

        Assert.Equal(1, steps);
        Assert.Equal("FakeRunner: _Process (Stage) threw InvalidOperationException: boom", failedWith);
        Assert.Null(finishedWith);
        Assert.Equal(2, log.Entries.Count);
    }

    [Fact]
    public void A_fault_that_repeats_in_the_observers_still_lets_the_log_off_complete_because_the_fail_path_skips_them_0_r5()
    {
        // The contract (M5 0 fix 0-R5): the first fault starts the fail path; the frames after it run ONLY the fail path (no observer, no sampling), so the same fault
        // cannot throw again and end the run before the log off. The old shape (observers ahead of the gate) is run beside it to show what it did.
        static (string? Finished, bool LoggedOff, int Frames) Run(bool observersSkippedWhenFailed)
        {
            var guard = new LegFaultGuard("FakeRunner", "logincheck", new RecordingLogSink());
            var failed = false;
            var loggedOff = false;
            string? finishedWith = null;
            var frames = 0;
            while (finishedWith is null && frames < 20)
            {
                frames++;
                try
                {
                    if (!failed || !observersSkippedWhenFailed)
                    {
                        throw new NullReferenceException("observer"); // the sampler throws every frame it runs
                    }

                    // the fail path: the log off is sent, and a later frame sees it done
                    if (!loggedOff)
                    {
                        loggedOff = true;
                    }
                    else
                    {
                        finishedWith = "log off done";
                    }
                }
                catch (Exception e) when (LegFaultGuard.Guards(e))
                {
                    if (guard.Catch("_Process (Stage)", e))
                    {
                        failed = true;
                    }
                    else
                    {
                        finishedWith = "ended by the repeat fault";
                    }
                }
            }

            return (finishedWith, loggedOff, frames);
        }

        Assert.Equal(("ended by the repeat fault", false, 2), Run(observersSkippedWhenFailed: false)); // the old shape: no log off
        Assert.Equal(("log off done", true, 3), Run(observersSkippedWhenFailed: true)); // the fix: the log off ran and finished
    }

    [Fact]
    public void The_observers_and_samplers_that_ran_ahead_of_the_fail_path_are_gated_and_the_login_check_routes_a_tripped_guard_to_its_fail_path_only_0_r5()
    {
        string Read(string name) => File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", name + ".cs"));

        Assert.Contains("if (!_failed) // M5 0 fix 0-R5", Read("JourneyLegRunner"), StringComparison.Ordinal);
        Assert.Contains("if (!_failed) // M5 0 fix 0-R5", Read("HuntLegRunner"), StringComparison.Ordinal);
        var throwaway = Read("ThrowawayLegRunner");
        Assert.Matches(@"if \(!_aborted\)\s*\{\s*SampleFrame\(delta\);", throwaway);
        var check = Read("LoginCheckRunner");
        Assert.Matches(@"if \(_guard\.Tripped\)\s*\{\s*StepAfterFault\(delta\);\s*\}\s*else\s*\{\s*StepFrame\(delta\);", check);
        // StepAfterFault runs the recovery only: none of the sampling and observer calls of StepFrame.
        var afterFault = check[check.IndexOf("private void StepAfterFault", StringComparison.Ordinal)..];
        afterFault = afterFault[..afterFault.IndexOf("StepRecovery(session);", StringComparison.Ordinal)];
        foreach (var call in new[] { "Sample(", "_shell.Step", "TakeDueShots", "TrackLegFrames", "WatchModes" })
        {
            Assert.DoesNotContain(call, afterFault, StringComparison.Ordinal);
        }

        // A fault outside the playing stage with the character in the world logs it off first.
        Assert.Contains("_recoveryLogOff = session.Actions.LogOff(ActionSource.Host);", check[check.IndexOf("private void StartFailPath", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RunnerFiles))]
    public void Every_leg_runner_catches_its_frame_through_the_guard_and_ends_by_its_own_fail_path(string name)
    {
        var text = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", name + ".cs"));
        Assert.Contains("LegFaultGuard _guard = new(\"" + name + "\", LogCategory, GodotLogSink.Instance)", text, StringComparison.Ordinal);
        Assert.Contains("catch (Exception e) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(e))", text, StringComparison.Ordinal);
        Assert.Contains("_guard.Catch(", text, StringComparison.Ordinal);
        if (name != "RebuffDriverHost")
        {
            Assert.Contains("_guard.Reason", text, StringComparison.Ordinal); // handed to the runner's fail or finish path
        }
    }

    public static TheoryData<string> RunnerFiles()
    {
        var data = new TheoryData<string>();
        foreach (var name in Runners)
        {
            data.Add(name);
        }

        return data;
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("the action did not throw");
    }
}
