using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 7: the soak frames' clock rule, their file names, and the three new options' parse and help.</summary>
public sealed class SoakFrameScheduleTests
{
    [Fact]
    public void Nothing_is_due_before_the_schedule_is_armed()
    {
        var schedule = new SoakFrameSchedule(60);

        Assert.False(schedule.TryTake(10_000, out _)); // start-up, the character list, portal space: not in the world yet
        Assert.False(schedule.Armed);
    }

    [Fact]
    public void The_first_frame_is_one_period_after_arming_then_one_every_period()
    {
        var schedule = new SoakFrameSchedule(60);
        schedule.Arm(1000); // entering the world, 1000 s after the start

        Assert.False(schedule.TryTake(1059.9, out _));
        Assert.True(schedule.TryTake(1060.0, out var first));
        Assert.Equal(1, first);
        Assert.False(schedule.TryTake(1100, out _));
        Assert.True(schedule.TryTake(1120.0, out var second));
        Assert.Equal(2, second);
        Assert.Equal(2, schedule.Taken);
    }

    [Fact]
    public void The_frames_keep_their_beat_when_a_frame_comes_a_little_late()
    {
        var schedule = new SoakFrameSchedule(60);
        schedule.Arm(0);

        Assert.True(schedule.TryTake(60.4, out _));
        Assert.True(schedule.TryTake(120.0, out _)); // due at 120, not at 120.4
    }

    [Fact]
    public void A_stall_of_many_periods_owes_one_frame_not_a_burst()
    {
        var schedule = new SoakFrameSchedule(60);
        schedule.Arm(0);

        Assert.True(schedule.TryTake(1000, out var number)); // a 16-minute stall
        Assert.Equal(1, number);
        Assert.False(schedule.TryTake(1000.1, out _));
        Assert.False(schedule.TryTake(1059.9, out _));
        Assert.True(schedule.TryTake(1060.0, out _));
    }

    [Fact]
    public void Arming_again_for_a_new_login_restarts_the_countdown_and_keeps_the_numbering()
    {
        var schedule = new SoakFrameSchedule(60);
        schedule.Arm(0);
        Assert.True(schedule.TryTake(60, out _));

        schedule.Arm(500);

        Assert.False(schedule.TryTake(559, out _));
        Assert.True(schedule.TryTake(560, out var number));
        Assert.Equal(2, number);
    }

    [Fact]
    public void Disarming_stops_the_frames()
    {
        var schedule = new SoakFrameSchedule(60);
        schedule.Arm(0);
        schedule.Disarm();

        Assert.False(schedule.TryTake(10_000, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_period_of_zero_or_less_is_off(double period)
    {
        var schedule = new SoakFrameSchedule(period);
        schedule.Arm(0);

        Assert.False(schedule.Enabled);
        Assert.False(schedule.Armed);
        Assert.False(schedule.TryTake(1_000_000, out _));
    }

    [Fact]
    public void The_file_name_is_soak_utc_stamp_and_the_number()
    {
        var name = SoakFrameSchedule.FileName(new DateTime(2026, 10, 29, 3, 4, 5, DateTimeKind.Utc), 12);

        Assert.Equal("soak-20261029T030405Z-12.png", name);
        Assert.Equal("soak-20261029T030405Z-1.png", SoakFrameSchedule.FileName(new DateTime(2026, 10, 29, 3, 4, 5, DateTimeKind.Utc), 1));
    }

    [Fact]
    public void The_defaults_are_a_256_mib_cap_and_no_frames_in_the_cache_folder()
    {
        var none = PlayOptions.Parse([]);

        Assert.Equal(256, none.LogMaxMb);
        Assert.Equal(0.0, none.SoakFrameSeconds);
        Assert.Equal("cache/soak/frames", none.SoakFrameDirectory);
        Assert.Empty(none.Errors);
    }

    [Theory]
    [InlineData("--log-max-mb=0", 0)]
    [InlineData("--log-max-mb=1", 1)]
    [InlineData("--log-max-mb=512", 512)]
    [InlineData("--log-max-mb=102400", 102400)]
    public void Log_max_mb_parses(string argument, int expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Equal(expected, o.LogMaxMb);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--log-max-mb=abc", "--log-max-mb: 'abc' is not an integer")]
    [InlineData("--log-max-mb=", "--log-max-mb: '' is not an integer")]
    [InlineData("--log-max-mb=-1", "--log-max-mb: '-1' is not 0 to 102400 (0 = no cap)")]
    [InlineData("--log-max-mb=102401", "--log-max-mb: '102401' is not 0 to 102400 (0 = no cap)")]
    public void A_bad_log_max_mb_is_reported_by_name_and_the_default_is_kept(string argument, string expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal(256, o.LogMaxMb);
    }

    [Theory]
    [InlineData("--soak-frame-s=0", 0.0)]
    [InlineData("--soak-frame-s=60", 60.0)]
    [InlineData("--soak-frame-s=300", 300.0)]
    [InlineData("--soak-frame-s=86400", 86400.0)]
    public void Soak_frame_s_parses(string argument, double expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Equal(expected, o.SoakFrameSeconds);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--soak-frame-s=abc", "--soak-frame-s: 'abc' is not a number")]
    [InlineData("--soak-frame-s=59", "--soak-frame-s: '59' is not 0 or 60 to 86400 (0 = no frames)")]
    [InlineData("--soak-frame-s=-1", "--soak-frame-s: '-1' is not 0 or 60 to 86400 (0 = no frames)")]
    [InlineData("--soak-frame-s=86401", "--soak-frame-s: '86401' is not 0 or 60 to 86400 (0 = no frames)")]
    public void A_bad_soak_frame_s_is_reported_by_name_and_no_frames_are_saved(string argument, string expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal(0.0, o.SoakFrameSeconds);
    }

    [Fact]
    public void The_frame_folder_is_taken_and_an_empty_one_is_reported()
    {
        Assert.Equal("cache/soak/run-a", PlayOptions.Parse(["--soak-frame-dir=cache/soak/run-a"]).SoakFrameDirectory);

        var o = PlayOptions.Parse(["--soak-frame-dir="]);

        Assert.StartsWith("--soak-frame-dir:", Assert.Single(o.Errors), StringComparison.Ordinal);
        Assert.Equal("cache/soak/frames", o.SoakFrameDirectory);
    }

    // M5 7 D129: the paper doll's live review frames, off by default.
    [Fact]
    public void The_doll_review_frames_folder_is_off_by_default_taken_when_given_and_an_empty_one_is_reported()
    {
        Assert.Null(PlayOptions.Parse([]).DollReviewFramesDirectory);
        Assert.Equal("cache/m5/7/doll", PlayOptions.Parse(["--doll-review-frames=cache/m5/7/doll"]).DollReviewFramesDirectory);

        var o = PlayOptions.Parse(["--doll-review-frames="]);

        Assert.StartsWith("--doll-review-frames:", Assert.Single(o.Errors), StringComparison.Ordinal);
        Assert.Null(o.DollReviewFramesDirectory);
        Assert.Contains(OptionCatalog.Play().Options, static e => e.Name == "--doll-review-frames" && e.Value == "DIR");
    }

    // M5 7 D142: the worn caster's and robe's examination frames, off by default.
    [Fact]
    public void The_examine_worn_frames_folder_is_off_by_default_taken_when_given_and_an_empty_one_is_reported()
    {
        Assert.Null(PlayOptions.Parse([]).ExamineWornFramesDirectory);
        Assert.Equal("cache/m5/7/worn", PlayOptions.Parse(["--examine-worn-frames=cache/m5/7/worn"]).ExamineWornFramesDirectory);

        var o = PlayOptions.Parse(["--examine-worn-frames="]);

        Assert.StartsWith("--examine-worn-frames:", Assert.Single(o.Errors), StringComparison.Ordinal);
        Assert.Null(o.ExamineWornFramesDirectory);
        Assert.Contains(OptionCatalog.Play().Options, static e => e.Name == "--examine-worn-frames" && e.Value == "DIR");
    }

    // M5 7 D137: the failed-roll live capture, off by default, 1 to 1000 answers.
    [Fact]
    public void The_creature_capture_is_off_by_default_takes_one_to_a_thousand_answers_and_reports_the_rest()
    {
        Assert.Null(PlayOptions.Parse([]).ExamineCaptureCreatures);
        Assert.Equal(6, PlayOptions.Parse(["--examine-capture-creatures=6"]).ExamineCaptureCreatures);
        foreach (var bad in new[] { "--examine-capture-creatures=0", "--examine-capture-creatures=1001", "--examine-capture-creatures=x", "--examine-capture-creatures=" })
        {
            var o = PlayOptions.Parse([bad]);
            Assert.Null(o.ExamineCaptureCreatures);
            Assert.StartsWith("--examine-capture-creatures:", Assert.Single(o.Errors), StringComparison.Ordinal);
        }

        Assert.Contains(OptionCatalog.Play().Options, static e => e.Name == "--examine-capture-creatures" && e.Value == "N");
    }

    // M5 7 S6 (D136): the examination's live review frames, off by default.
    [Fact]
    public void The_examine_review_frames_folder_is_off_by_default_taken_when_given_and_an_empty_one_is_reported()
    {
        Assert.Null(PlayOptions.Parse([]).ExamineReviewFramesDirectory);
        Assert.Equal("cache/m5/7/examine", PlayOptions.Parse(["--examine-review-frames=cache/m5/7/examine"]).ExamineReviewFramesDirectory);

        var o = PlayOptions.Parse(["--examine-review-frames="]);

        Assert.StartsWith("--examine-review-frames:", Assert.Single(o.Errors), StringComparison.Ordinal);
        Assert.Null(o.ExamineReviewFramesDirectory);
        Assert.Contains(OptionCatalog.Play().Options, static e => e.Name == "--examine-review-frames" && e.Value == "DIR");
    }

    [Fact]
    public void The_three_options_are_registered_with_help_in_the_play_group()
    {
        var names = OptionCatalog.Play().Options.ToDictionary(o => o.Name, o => o, StringComparer.Ordinal);

        Assert.Contains("--log-max-mb", names.Keys);
        Assert.Contains("--soak-frame-s", names.Keys);
        Assert.Contains("--soak-frame-dir", names.Keys);
        Assert.Contains("lifestone.1.log", names["--log-max-mb"].Help, StringComparison.Ordinal);
        Assert.Contains("256", names["--log-max-mb"].Help, StringComparison.Ordinal);
        Assert.Contains("soak-<UTC yyyyMMddTHHmmssZ>-<n>.png", names["--soak-frame-s"].Help, StringComparison.Ordinal);
        Assert.Contains("cache/soak/frames", names["--soak-frame-dir"].Help, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.PathOf(parts)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void The_game_wires_the_roller_the_drain_and_the_frames_where_the_slice_says()
    {
        var log = Source("src", "Lifestone.Game", "scripts", "Core", "Log.cs");
        Assert.Contains("new LogRoller(", log, StringComparison.Ordinal);
        Assert.Contains("roller.WriteLine(line);", log, StringComparison.Ordinal);
        Assert.Contains("PlayOptions.Parse(OS.GetCmdlineUserArgs()).LogMaxMb", log, StringComparison.Ordinal);

        var play = Source("src", "Lifestone.Game", "scripts", "Session", "PlayMode.cs");
        Assert.Contains("DrainAvatarNotices();", play, StringComparison.Ordinal);
        Assert.Contains("_avatars.DrainNotices(_frameNotices)", play, StringComparison.Ordinal);
        Assert.Contains("_soakFrames.Step(GetViewport()", play, StringComparison.Ordinal);

        // One drain owner: the look shots read the frame's list and never drain the set themselves.
        var look = Source("src", "Lifestone.Game", "scripts", "Session", "PlayMode.LookShots.cs");
        Assert.DoesNotContain("DrainNotices(", look, StringComparison.Ordinal);
        Assert.Contains("_frameNotices", look, StringComparison.Ordinal);

        var frames = Source("src", "Lifestone.Game", "scripts", "Diagnostics", "SoakFrames.cs");
        Assert.Contains("SaveInBackgroundQuietly", frames, StringComparison.Ordinal);
        Assert.Contains("\"soak frame saved\"", frames, StringComparison.Ordinal);
    }
}
