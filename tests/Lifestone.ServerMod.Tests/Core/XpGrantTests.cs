using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The <c>xp</c> verb's count of what ACE added (server/mod/Lifestone.ServerMod/Core/XpGrant.cs, compiled here by link;
/// m2-plan SM3), run on a stand-in for the player's action queue that keeps ACE's order: the read of the experience,
/// ACE's UpdateXpAndLevel (which adds at most what is left to the cap, Player_Xp.cs:99-101) and the reply, queued
/// together as XpVerb does. Other experience queued before or after is never counted.
/// </summary>
public sealed class XpGrantTests
{
    [Fact]
    public void Added_is_what_ACE_s_update_added_for_this_request()
    {
        var player = new QueuedPlayer(total: 5_000);
        var reply = player.Xp(1_000);
        Assert.Null(reply.Data);
        player.RunQueue();

        Assert.Equal(new XpData(1_000, 1_000, 6_000, 6_000, 1), reply.Data);
    }

    [Fact]
    public void Experience_queued_around_the_request_in_the_same_tick_is_not_counted()
    {
        // A kill's update before, a second xp request and another kill after, all in the queue before it runs.
        var player = new QueuedPlayer(total: 0);
        player.Kill(250);
        var first = player.Xp(1_000);
        var second = player.Xp(40);
        player.Kill(7);
        player.RunQueue();

        Assert.Equal(1_000, first.Data!.Added);
        Assert.Equal(1_250, first.Data.Total);
        Assert.Equal(40, second.Data!.Added);
        Assert.Equal(1_290, second.Data.Total);
        Assert.Equal(1_297, player.Total);
    }

    [Fact]
    public void At_the_level_cap_added_is_less_than_requested()
    {
        var player = new QueuedPlayer(total: QueuedPlayer.Cap - 300);
        var reply = player.Xp(1_000);
        player.RunQueue();

        Assert.Equal(1_000, reply.Data!.Requested);
        Assert.Equal(300, reply.Data.Added);
        Assert.Equal(QueuedPlayer.Cap, reply.Data.Total);
    }

    [Fact]
    public void An_Olthoi_player_gets_nothing_added()
    {
        // GrantXP skips an Olthoi player (Player_Xp.cs:55-61) and queues no update at all.
        var player = new QueuedPlayer(total: 10, olthoi: true);
        var reply = player.Xp(1_000);
        player.RunQueue();

        Assert.Equal(0, reply.Data!.Added);
        Assert.Equal(10, reply.Data.Total);
    }

    [Fact]
    public void A_reply_that_runs_before_the_read_fails_loudly()
    {
        var grant = new XpGrant(new XpRequest(5));
        var e = Assert.Throws<InvalidOperationException>(() => grant.After(100, 100, 1));
        Assert.Contains("did not run the request's actions in order", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_data_carries_ACE_s_numbers_after_the_update()
    {
        var grant = new XpGrant(new XpRequest(999_999_999_999, "x1"));
        grant.Before(1);
        Assert.Equal(new XpData(999_999_999_999, 999_999_999_999, 1_000_000_000_000, 77, 126), grant.After(1_000_000_000_000, 77, 126));
        Assert.Equal("x1", grant.Request.Id);
    }

    [Fact]
    public void A_grant_to_level_126_is_counted_in_64_bits()
    {
        // XP to level 126 is 4,286,609,098 (XpTableData, handoff-m3-1-1): above int.MaxValue. The request, the parse, the count and the
        // reply (XpData) are all 64-bit, so the grant that builds the mage is one request and reads back exactly.
        const long ToLevel126 = 4_286_609_098;
        Assert.True(ToLevel126 > int.MaxValue);
        var request = ToolRequestParser.Parse("xp " + ToLevel126).Request;
        Assert.Equal(new XpRequest(ToLevel126), request);

        var grant = new XpGrant((XpRequest)request!);
        grant.Before(5_000);
        Assert.Equal(new XpData(ToLevel126, ToLevel126, ToLevel126 + 5_000, ToLevel126 + 5_000, 126), grant.After(ToLevel126 + 5_000, ToLevel126 + 5_000, 126));

        // A second grant on top of a total already above int.MaxValue still counts exactly.
        var again = new XpGrant(new XpRequest(10));
        again.Before(ToLevel126);
        Assert.Equal(10, again.After(ToLevel126 + 10, 10, 126).Added);
    }

    /// <summary>The answer a queued xp request gave, once the queue has run.</summary>
    private sealed class Answer
    {
        public XpData? Data { get; set; }
    }

    /// <summary>A player's total experience and action queue, run in order as ACE's ActionQueue does.</summary>
    private sealed class QueuedPlayer(long total, bool olthoi = false)
    {
        /// <summary>A stand-in for the experience at the maximum level.</summary>
        public const long Cap = 1_000_000;

        private readonly Queue<Action> queue = new();

        public long Total { get; private set; } = total;

        /// <summary>What XpVerb does on the world thread: queue the read, grant (which queues ACE's update), queue the reply.</summary>
        public Answer Xp(long amount)
        {
            var answer = new Answer();
            var grant = new XpGrant(new XpRequest(amount));
            queue.Enqueue(() => grant.Before(Total));
            GrantXp(amount);
            queue.Enqueue(() => answer.Data = grant.After(Total, Total, 1));
            return answer;
        }

        /// <summary>Experience from elsewhere (a kill), queued as ACE's own update.</summary>
        public void Kill(long amount) => GrantXp(amount);

        public void RunQueue()
        {
            while (queue.TryDequeue(out var action))
            {
                action();
            }
        }

        private void GrantXp(long amount)
        {
            if (!olthoi)
            {
                queue.Enqueue(() => Total += Math.Min(amount, Cap - Total));
            }
        }
    }
}
