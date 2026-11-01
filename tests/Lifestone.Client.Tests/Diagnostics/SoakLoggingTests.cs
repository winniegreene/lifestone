using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 7 F17, log basically everything: the chat lines from the session's event, the vitals on a timer, and the object lines near the player.</summary>
public sealed class SoakLoggingTests
{
    private static PlayerQualities Vitals(uint health = 80, uint stamina = 55, uint mana = 40)
    {
        var q = new PlayerQualities();
        q.Vitals[1] = new VitalDesc(1, 0, 100, 0, Current: health);
        q.Vitals[3] = new VitalDesc(3, 0, 60, 0, Current: stamina);
        q.Vitals[5] = new VitalDesc(5, 0, 90, 0, Current: mana);
        q.IsKnown = true;
        return q;
    }

    [Fact]
    public void Every_chat_line_of_the_session_is_logged_once_at_info_with_its_kind_type_and_sender_and_no_credential_special_case()
    {
        var log = new RecordingLogSink();
        var bus = new EventBus(log);
        using var tap = new ChatLogTap(bus, log);
        var before = log.Entries.Count;

        bus.Publish(new ChatReceived(new ChatLine(1.0, ChatKind.System, 7, null, 0, "You hit the Tusker for 12 points of damage to the head.")));
        bus.Publish(new ChatReceived(new ChatLine(2.0, ChatKind.Tell, 3, "Some Player", 0x50000001, "my password is hunter2\r\nsecond line")));

        var chat = log.Entries.Skip(before).Where(e => e.Category == "chat").ToList();
        Assert.Equal(2, chat.Count);
        Assert.Equal(2, tap.Lines);
        Assert.All(chat, e => Assert.Equal(LogSeverity.Info, e.Severity));
        Assert.Contains("kind=System type=7 text=\"You hit the Tusker for 12 points of damage to the head.\"", chat[0].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("from=", chat[0].Line, StringComparison.Ordinal);
        Assert.Contains("kind=Tell type=3 from=\"Some Player\"", chat[1].Line, StringComparison.Ordinal);
        Assert.Contains("hunter2 second line", chat[1].Line, StringComparison.Ordinal); // not special-cased, and the line end became a space: one chat line is one log line
        Assert.DoesNotContain('\n', chat[1].Line);
    }

    [Fact]
    public void A_failure_line_and_a_channel_notice_are_logged_with_the_text_the_chat_shows_never_blank()
    {
        // M5 7 D124: round 12 logged "kind=WeenieError type=1068 text=" 31 times and the channel joins as WeenieError lines with the bare channel name.
        var log = new RecordingLogSink();
        var bus = new EventBus(log);
        using var tap = new ChatLogTap(bus, log);
        var before = log.Entries.Count;

        bus.Publish(new ChatReceived(new ChatLine(1.0, ChatKind.WeenieError, 0x0402, null, 0, string.Empty)));
        bus.Publish(new ChatReceived(new ChatLine(2.0, ChatKind.WeenieError, 0x042C, null, 0, string.Empty)));
        bus.Publish(new ChatReceived(new ChatLine(3.0, ChatKind.ChannelNotice, 0x051B, null, 0, "General")));

        var chat = log.Entries.Skip(before).Where(e => e.Category == "chat").ToList();
        Assert.Equal(3, chat.Count);
        Assert.Contains("kind=WeenieError type=1026 code=0x0402 text=\"Your spell fizzled.\"", chat[0].Line, StringComparison.Ordinal);
        Assert.Contains("kind=WeenieError type=1068 code=0x042C text=\"WeenieError 0x042C (TargetNotAcquired)\" on_screen=no", chat[1].Line, StringComparison.Ordinal); // M5 7 D124 (S1b): the client prints no line for it
        Assert.Contains("on_screen=yes", chat[0].Line, StringComparison.Ordinal);
        Assert.Contains("kind=ChannelNotice type=1307 code=0x051B text=\"You have entered the General channel.\"", chat[2].Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_tap_logs_nothing_more_and_a_long_line_is_cut()
    {
        var log = new RecordingLogSink();
        var bus = new EventBus(log);
        var tap = new ChatLogTap(bus, log);
        bus.Publish(new ChatReceived(new ChatLine(1.0, ChatKind.System, 0, null, 0, new string('x', 1000))));
        Assert.Equal(ChatLogTap.MaxTextChars, ChatLogTap.Clean(new string('x', 1000)).Length);
        tap.Dispose();
        bus.Publish(new ChatReceived(new ChatLine(2.0, ChatKind.System, 0, null, 0, "after")));

        Assert.Equal(1, log.Entries.Count(e => e.Category == "chat"));
    }

    [Fact]
    public void The_vitals_line_comes_every_period_after_the_vitals_are_known_and_a_stall_owes_one_line()
    {
        var log = new RecordingLogSink();
        var vitals = new VitalsLog(10, log);
        var q = Vitals();

        vitals.Step(0, null); // no player yet: the countdown has not begun
        vitals.Step(5, new PlayerQualities()); // the description is not here yet
        vitals.Step(100, q); // known: the countdown starts here
        vitals.Step(109.9, q);
        Assert.Equal(0, vitals.Lines);
        vitals.Step(110, q);
        vitals.Step(119.9, q);
        vitals.Step(120, q);
        Assert.Equal(2, vitals.Lines);
        vitals.Step(500, q); // a stall of many periods: one line, not a burst
        vitals.Step(500.1, q);
        Assert.Equal(3, vitals.Lines);
        vitals.Step(509.9, q);
        Assert.Equal(3, vitals.Lines);
        vitals.Step(510, q);
        Assert.Equal(4, vitals.Lines);

        var lines = log.Entries.Where(e => e.Category == "vitals").ToList();
        Assert.Equal(4, lines.Count);
        Assert.All(lines, e => Assert.Equal(LogSeverity.Info, e.Severity));
        Assert.Equal("vitals: health 80/100 stamina 55/60 mana 40/90", lines[0].Message);
    }

    [Fact]
    public void The_vitals_are_off_at_zero_and_the_frame_path_allocates_nothing_between_lines()
    {
        var log = new RecordingLogSink();
        var off = new VitalsLog(0, log);
        off.Step(0, Vitals());
        off.Step(1000, Vitals());
        Assert.False(off.Enabled);
        Assert.Empty(log.Entries);

        var on = new VitalsLog(10, log);
        var q = Vitals();
        on.Step(0, q);
        on.Step(1, q); // warm
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 5000; i++)
        {
            on.Step(1 + (i * 0.001), q); // 5 simulated seconds, none due
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, on.Lines);
    }

    [Fact]
    public void The_object_filter_takes_creates_and_deletes_with_a_known_distance_inside_the_radius_only()
    {
        Assert.True(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Created, 1, null, "Tusker", 59.9f), 60));
        Assert.True(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Deleted, 1, null, "Tusker", 60f), 60));
        Assert.True(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Recreated, 1, null, "Tusker", 0f), 60));
        Assert.False(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Created, 1, null, "Tusker", 60.1f), 60));
        Assert.False(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Created, 1), 60)); // unknown distance
        Assert.False(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.MotionFailed, 1, "x", "Tusker", 5f), 60));
        Assert.False(AvatarNoticeTally.IsNearObject(new AvatarNotice(AvatarNoticeKind.Created, 1, null, "Tusker", 5f), 0)); // radius 0 = off
    }

    [Fact]
    public void An_object_near_the_player_is_logged_with_name_guid_and_distance_and_a_far_one_is_not()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log, "avatars", 60);

        tally.Add([new AvatarNotice(AvatarNoticeKind.Created, 0x80001234, null, "Black Spawn Tusker", 34.26f), new AvatarNotice(AvatarNoticeKind.Created, 0x80001235, null, "Far Thing", 90f), new AvatarNotice(AvatarNoticeKind.Deleted, 0x80001234, null, "Black Spawn Tusker", 12.04f)], 0);

        var lines = log.Entries.Where(e => e.Category == "objects").ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("object created", lines[0].Message);
        Assert.Contains("name=\"Black Spawn Tusker\" guid=0x80001234 dist_m=34.3", lines[0].Line, StringComparison.Ordinal);
        Assert.Equal("object deleted", lines[1].Message);
        Assert.Contains("dist_m=12", lines[1].Line, StringComparison.Ordinal);

        var off = new RecordingLogSink();
        new AvatarNoticeTally(off, "avatars", 0).Add([new AvatarNotice(AvatarNoticeKind.Created, 1, null, "Tusker", 3f)], 0);
        Assert.Empty(off.Entries);
    }

    [Fact]
    public void The_object_lines_are_limited_per_window_and_the_next_window_names_what_was_left_out()
    {
        var log = new RecordingLogSink();
        var tally = new AvatarNoticeTally(log, "avatars", 60);
        var burst = Enumerable.Range(0, AvatarNoticeTally.ObjectLinesPerWindow + 25).Select(i => new AvatarNotice(AvatarNoticeKind.Created, 0x80000000u + (uint)i, null, "Grub", 10f)).ToList();

        tally.Add(burst, 100);
        Assert.Equal(AvatarNoticeTally.ObjectLinesPerWindow, log.Entries.Count(e => e.Category == "objects"));
        Assert.Equal(25, tally.ObjectLinesSuppressed);

        tally.Add([new AvatarNotice(AvatarNoticeKind.Deleted, 7, null, "Grub", 5f)], 100 + AvatarNoticeTally.ObjectWindowSeconds);

        var left = log.Entries.Single(e => e.Message.Contains("left out", StringComparison.Ordinal));
        Assert.Contains("count=25", left.Line, StringComparison.Ordinal);
        Assert.Equal(AvatarNoticeTally.ObjectLinesPerWindow + 1, log.Entries.Count(e => e.Message is "object created" or "object deleted"));
    }
}
