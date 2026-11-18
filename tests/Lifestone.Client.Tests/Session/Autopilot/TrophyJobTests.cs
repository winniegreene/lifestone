using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 AX: the Female Tusk as a named job of <see cref="RungOwner.Decide"/> (the owner's decision: it counts as gate A's tusk, changing D76). Proved: from the Lodge's band a due tusk with the Burrow's gate open sends her to the Burrow by the rung's own name, she stays there while it is due, and once it is not she goes back to her band; a shut gate, a host's read, a level outside the data, a tusk not due: no job, and the climb goes on as before. The job is additive: an ask with no <see cref="RungAsk.TrophyDue"/> decides as it did.
/// </summary>
public sealed class TrophyJobTests
{
    private const string Burrow = "tusker-burrow";
    private const string Lodge = "tusker-lodge";

    private static Ladder L => LadderLoader.WarMage;

    private static PlayerQualities Trained()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static RungAsk Ask(int level, string run, bool due, bool up = true, uint? cell = 0xA9B40019) =>
        new(level, cell, Trained(), (_, _) => true, new RungBuffRead(_ => up ? 8 : 0, _ => 4)) { RunGround = run, TrophyDue = key => due && key == "tusk-female" };

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(21)]
    public void A_due_tusk_with_the_gate_open_sends_her_from_the_Lodge_to_the_Burrow_by_name(int level)
    {
        var choice = RungOwner.Decide(L, Ask(level, Lodge, due: true));

        Assert.Equal(LadderStepKind.Moved, choice.Decision.Kind);
        Assert.Equal("rung-moved:burrow", choice.Decision.Stop);
        Assert.Equal("job:tusk-female", choice.Reason);
        Assert.Equal("burrow", choice.Decision.Rung?.Key); // a move builds no ground: the host builds it from the stop's name
        Assert.True(choice.Opened); // a run's first check that reads this is a move-on, not a mismatch
    }

    [Fact]
    public void While_it_is_due_she_stays_on_the_Burrow_past_its_band_and_no_move_on_fires()
    {
        var choice = RungOwner.Decide(L, Ask(18, Burrow, due: true));

        Assert.Equal(LadderStepKind.Hunt, choice.Decision.Kind);
        Assert.Equal("burrow", choice.Decision.Rung?.Key);
        Assert.Equal(14, choice.HeldLevel); // M6 14 (D135): the Burrow's band ends at 14
        Assert.Null(RungOwner.MoveOnDue(L, choice.Decision.Rung!, choice, Burrow, null));
    }

    [Fact]
    public void When_it_is_no_longer_due_she_goes_back_to_her_band()
    {
        var choice = RungOwner.Decide(L, Ask(18, Burrow, due: false));

        Assert.Equal("rung-moved:lodge", choice.Decision.Stop);
    }

    [Theory]
    [InlineData(18, false, true)] // not due: the quest done, its 20 hours, the tusk held, or the tries spent
    [InlineData(18, true, false)] // the Burrow's gate shut: the buffs are down
    [InlineData(30, true, true)] // past the data's level: the hunt reaches the hall by itself
    public void No_job_leaves_her_rung_as_it_was(int level, bool due, bool up)
    {
        var plain = RungOwner.Decide(L, Ask(level, Lodge, due: false, up: up) with { TrophyDue = null });

        var choice = RungOwner.Decide(L, Ask(level, Lodge, due, up));

        Assert.Equal(plain.Decision.Stop, choice.Decision.Stop);
        Assert.Equal(plain.HeldLevel, choice.HeldLevel);
        Assert.False(choice.Reason.StartsWith("job:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_host_has_no_timers_so_it_asks_no_job()
    {
        var host = new RungAsk(18, 0xA9B40019, Trained(), (_, _) => true, null);

        Assert.False(RungOwner.Decide(L, host).Reason.StartsWith("job:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_stop_the_job_makes_restarts_her_on_the_Burrow_past_its_band()
    {
        var named = RungOwner.NamedRung(L, "rung-moved:burrow");

        var host = RungOwner.Decide(L, new RungAsk(18, 0xA9B40019, Trained(), (_, _) => true, null, named));

        Assert.Equal(Burrow, host.Ground?.Key);
    }

    [Fact]
    public void The_pull_is_data_the_5_pack_holder_from_the_stand_outside_the_keep_out()
    {
        var hunt = GroundCatalog.Get(Burrow).Hunt!;
        var stand = hunt.Pulls[0].Stand!;

        Assert.Equal("tusk-female", stand.Quest);
        Assert.Equal((16, 29), (stand.FromLevel, stand.ToLevel));
        Assert.Equal(22589u, stand.HolderWcid);
        Assert.Equal(0x57480106u, stand.HolderCell); // slot 6, a pack of 5
        Assert.Equal(0x5748013Du, stand.Cell);
        Assert.True(hunt.KeepOut.Contains(stand.HolderCell));
        Assert.False(hunt.KeepOut.Contains(stand.Cell));
        Assert.Equal(420, stand.WaitSeconds); // the generator's 300 + 60, and one tick
        Assert.Equal(2, stand.Tries);
        Assert.True(stand.ToLevel < hunt.UnderLevel); // past the keep-out's line the plain hunt reaches the hall
    }

    [Fact]
    public void The_memory_counts_tries_and_keeps_a_failure()
    {
        var m = new TrophyPullMemory();
        Assert.True(m.MayTry("q", 2));
        m.NoteTry("q");
        Assert.True(m.MayTry("q", 2));
        m.NoteTry("q");
        Assert.False(m.MayTry("q", 2));

        var n = new TrophyPullMemory();
        n.Fail("q", "holder-absent");
        Assert.False(n.MayTry("q", 2));
        Assert.Equal("holder-absent", n.FailureOf("q"));
    }
}
