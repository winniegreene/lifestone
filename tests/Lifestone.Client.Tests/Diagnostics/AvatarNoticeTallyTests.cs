using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Avatars;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 7, <c>m5-avatar-notices-full</c>: a host that drains the avatar set every frame never fills its 1,024-notice list, and the tally counts by kind and logs a problem kind once a minute.</summary>
public sealed class AvatarNoticeTallyTests
{
    [Fact]
    public void A_set_drained_every_frame_never_overflows_in_a_run_of_hours()
    {
        var log = new RecordingLogSink();
        var set = new AvatarSet(AvatarRig.Sources(), log);
        var tally = new AvatarNoticeTally(log);
        var frame = new List<AvatarNotice>();

        // Six notices a frame for 30 simulated minutes at 30 frames a second (the 1,024 list filled in 30 minutes at far lower rates).
        var uid = 1000u;
        for (var i = 0; i < 30 * 60 * 30; i++)
        {
            for (var n = 0; n < 6; n++)
            {
                set.Apply(new ObjectDeleted(uid++));
            }

            frame.Clear();
            Assert.Equal(6, set.DrainNotices(frame));
            tally.Add(frame, i / 30.0);
        }

        Assert.Equal(0, set.Stats.NoticesDropped);
        Assert.DoesNotContain(log.Lines, line => line.Contains("notice list is full", StringComparison.Ordinal));
        Assert.Equal(6L * 30 * 60 * 30, tally.Count(AvatarNoticeKind.UnknownObject));
        Assert.Equal(tally.Total, tally.Count(AvatarNoticeKind.UnknownObject));
    }

    [Fact]
    public void Counts_every_kind_and_logs_only_the_problem_kinds()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log);
        var kinds = Enum.GetValues<AvatarNoticeKind>();
        var frame = kinds.Select(k => new AvatarNotice(k, 0x50000001u, "why")).ToList();

        tally.Add(frame, 0);

        foreach (var kind in kinds)
        {
            Assert.Equal(1, tally.Count(kind));
        }

        Assert.Equal(kinds.Length, tally.Total);
        var problems = kinds.Where(AvatarNoticeTally.IsProblem).ToList();
        Assert.Equal(
            ["AppearanceProblem", "CreateFailed", "MotionFailed", "ParentFailed", "UnknownObject"],
            problems.Select(k => k.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal(problems.Count, log.Entries.Count(e => e.Severity == LogSeverity.Info));
        Assert.All(log.Entries, e => Assert.Equal(LogSeverity.Info, e.Severity));
        Assert.DoesNotContain(log.Lines, line => line.Contains("kind=Created", StringComparison.Ordinal) || line.Contains("kind=Deleted", StringComparison.Ordinal));
    }

    [Fact]
    public void A_problem_kind_logs_at_most_once_a_minute_with_the_running_count()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log);
        var one = new List<AvatarNotice> { new(AvatarNoticeKind.MotionFailed, 7, "bad") };

        for (var second = 0; second < 180; second++)
        {
            tally.Add(one, second); // 180 notices over three minutes
        }

        tally.Add(one, 180.0); // the fourth minute opens

        Assert.Equal(181, tally.Count(AvatarNoticeKind.MotionFailed));
        var lines = log.Entries;
        Assert.Equal(4, lines.Count); // at 0 s, 60 s, 120 s and 180 s
        Assert.Equal(
            [1L, 61L, 121L, 181L],
            lines.Select(e => (long)e.Fields.Single(f => f.Key == "count").Value!).ToArray());
        Assert.Equal(
            [1L, 60L, 60L, 60L],
            lines.Select(e => (long)e.Fields.Single(f => f.Key == "since_last_line").Value!).ToArray());
    }

    [Fact]
    public void Each_kind_has_its_own_minute()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log);

        tally.Add([new(AvatarNoticeKind.MotionFailed, 1), new(AvatarNoticeKind.UnknownObject, 2)], 0);
        tally.Add([new(AvatarNoticeKind.MotionFailed, 1), new(AvatarNoticeKind.UnknownObject, 2), new(AvatarNoticeKind.CreateFailed, 3, "x")], 30);

        Assert.Equal(3, log.Entries.Count); // motion and unknown at 0 s, create-failed at 30 s; the repeats inside the minute are only counted
        Assert.Equal(2, tally.Count(AvatarNoticeKind.MotionFailed));
    }

    [Fact]
    public void A_long_detail_is_cut_and_a_missing_one_is_empty()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log);

        tally.Add([new(AvatarNoticeKind.AppearanceProblem, 1, new string('x', 5000)), new(AvatarNoticeKind.ParentFailed, 2)], 0);

        Assert.All(log.Lines, line => Assert.True(line.Length < 400, line));
    }

    [Fact]
    public void Counting_and_a_rate_limited_repeat_allocate_nothing_a_frame()
    {
        var tally = new AvatarNoticeTally(new RecordingLogSink());
        var frame = new List<AvatarNotice> { new(AvatarNoticeKind.Created, 1), new(AvatarNoticeKind.MotionFailed, 2, "d"), new(AvatarNoticeKind.Deleted, 3) };
        tally.Add(frame, 0); // the first problem line is logged here
        tally.Add([], 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            tally.Add(frame, 1 + (i * 0.01)); // inside the minute: counted, not logged
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Every_notice_kind_is_inside_the_tally()
    {
        var tally = new AvatarNoticeTally(new RecordingLogSink());

        tally.Add([.. Enum.GetValues<AvatarNoticeKind>().Select(k => new AvatarNotice(k, 1))], 0); // an out-of-range kind would throw

        Assert.Equal((int)Enum.GetValues<AvatarNoticeKind>().Max() + 1, Enum.GetValues<AvatarNoticeKind>().Length);
    }
}
