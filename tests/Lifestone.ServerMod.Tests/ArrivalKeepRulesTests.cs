using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// M6 7 AJ: the server's half of <c>m6-body-apart</c>. ACE can unload a landblock from under a player who has just arrived, leaving her on no landblock; <see cref="ArrivalKeepRules"/> says when the guard marks her block active. ACE's assemblies are
/// not in this project, so the rule runs here and the seam is pinned on the source; the guard's own run is the lead's live check (the ACE log's <c>Lifestone arrival guard:</c> lines).
/// </summary>
public sealed class ArrivalKeepRulesTests
{
    private const uint Winnie = 0x500000F0;
    private const uint Lodge = 0x5749FFFF;
    private const uint Holtburg = 0xA9B4FFFF;

    private static string Guard => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "ArrivalGuard.cs"));

    [Fact]
    public void The_guard_is_on_unless_the_flag_says_ace()
    {
        Assert.True(ArrivalKeepRules.IsOn(null));
        Assert.True(ArrivalKeepRules.IsOn(string.Empty));
        Assert.True(ArrivalKeepRules.IsOn("on"));
        Assert.True(ArrivalKeepRules.IsOn("off-by-a-typo"));
        Assert.False(ArrivalKeepRules.IsOn("ace"));
        Assert.False(ArrivalKeepRules.IsOn(" ACE "));
    }

    [Fact]
    public void An_arrival_counts_once_a_stay_so_ace_keeps_its_own_timing_after_it()
    {
        var rules = new ArrivalKeepRules();
        Assert.True(rules.Arrived(Winnie, Holtburg));
        Assert.False(rules.Arrived(Winnie, Holtburg));
        Assert.True(rules.Arrived(Winnie, Lodge));
        Assert.False(rules.Arrived(Winnie, Lodge));
        Assert.True(rules.Arrived(Winnie, Holtburg));
    }

    [Fact]
    public void A_player_on_no_landblock_is_reported_once_and_her_next_block_is_an_arrival()
    {
        var rules = new ArrivalKeepRules();
        Assert.False(rules.Lost(Winnie)); // a login before its first block: nothing lost
        rules.Arrived(Winnie, Lodge);

        Assert.True(rules.Lost(Winnie));
        Assert.False(rules.Lost(Winnie));
        Assert.True(rules.Arrived(Winnie, Lodge)); // the same block's id again, after a re-login
    }

    [Fact]
    public void A_player_gone_offline_is_forgotten_so_her_login_is_an_arrival()
    {
        var rules = new ArrivalKeepRules();
        rules.Arrived(Winnie, Lodge);
        rules.Keep([]);
        Assert.True(rules.Arrived(Winnie, Lodge));

        rules.Keep([Winnie]);
        Assert.False(rules.Arrived(Winnie, Lodge));
    }

    [Fact]
    public void The_guard_runs_in_the_delay_stage_and_marks_the_block_active_on_the_arrival()
    {
        // The order is the fix: ACE's world loop runs ActionQueue (the arrival), DelayManager (this), then the landblocks' tick and unloads. An ActionQueue guard could run before the arrival in the same pass.
        var text = Guard;
        Assert.Contains("WorldManager.DelayManager.EnqueueAction(new Pass(this))", text, StringComparison.Ordinal);
        Assert.Contains(": DelayAction(0)", text, StringComparison.Ordinal);
        Assert.Contains("if (rules.Arrived(guid, block.Id.Raw))", text, StringComparison.Ordinal);
        Assert.Contains("block.SetActive();", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_in_the_guard_cannot_reach_ace_s_world_loop()
    {
        var text = Guard;
        Assert.Contains("catch (Exception e)", text, StringComparison.Ordinal);
        Assert.Contains("stopped = true;", text, StringComparison.Ordinal);
    }
}
