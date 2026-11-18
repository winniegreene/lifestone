using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AY (the owner's decision of 2026-11-06): she fetches her corpse for coin when it is safe to reach, and every corpse run asks the rung's owner. Proved: the coin is read from ACE's line; the safe cells are data, a list outside every keep-out; each condition stops the fetch by its own name, the server's
/// destroyed coin first, so nothing walks on today's server; a corpse behind a shut gate is no fetch, also past the rung's band; a corpse on no rung's ground is no rung's matter. Scripted: nothing here ran live.
/// </summary>
public sealed class CorpseCoinRuleTests
{
    private const uint LodgeFront = 0x57490104;
    private const uint LodgeDeep = 0x5749010D; // the ramp, past the front
    private const uint LodgeSlot = 0x5749017C; // where gate A's corpse lay

    private static Ladder L => LadderLoader.WarMage;

    private static LadderCorpseCoin Coin => new(true, 2000);

    private static CorpseRunAnswer Open => new(true, "gate-open:lodge", "lodge");

    private static HuntingGround Ground(string key) => GroundCatalog.Get(key);

    private static RungAsk Ask(int level, bool met, uint? cell = 0xA9B40019)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return new RungAsk(level, cell, q, (_, _) => true, new RungBuffRead(_ => met ? 8 : 0, _ => 4));
    }

    [Theory]
    [InlineData("You've lost 4,640 Pyreals!", 4640L)]
    [InlineData("You've lost 765 Pyreals, and your Lesser Mana Stone!", 765L)]
    [InlineData("You've lost 1,234,567 Pyreals!", 1234567L)]
    [InlineData("You've lost your Lesser Mana Stone!", null)]
    [InlineData(null, null)]
    public void The_coin_is_read_from_ACEs_line(string? line, long? coin) => Assert.Equal(coin, CorpseCoinRule.CoinLost(line));

    [Theory]
    [InlineData("holtburg-outskirts", 0xA9B30021u, true)] // outdoors: anywhere on the ground
    [InlineData("tusker-burrow", 0x57480210u, true)] // the arrival
    [InlineData("tusker-burrow", 0x57480106u, false)] // the south hall's keep-out
    [InlineData("tusker-burrow", 0x5748013Cu, false)] // deep, not on the list
    [InlineData("tusker-lodge", LodgeFront, true)]
    [InlineData("tusker-lodge", LodgeDeep, false)]
    [InlineData("tusker-lodge", LodgeSlot, false)]
    [InlineData("tusker-cave", 0x5B460100u, false)] // no list: nowhere
    public void Safe_cells_are_the_grounds_list_outside_every_keep_out(string ground, uint cell, bool safe) => Assert.Equal(safe, CorpseCoinRule.Safe(Ground(ground), cell));

    [Fact]
    public void No_safe_cell_lies_in_a_keep_out_or_a_cell_of_a_creature_slot()
    {
        foreach (var ground in GroundCatalog.All.Where(g => g.CorpseFetch is { Anywhere: false }))
        {
            Assert.All(ground.CorpseFetch!.Cells.Cells, c => Assert.DoesNotContain(ground.Respawn.Slots, s => s.Cell == c));
        }
    }

    [Theory]
    [InlineData(false, 4640L, LodgeFront, true, "coin-destroyed")] // the server's fact comes first: today's run
    [InlineData(true, null, LodgeFront, true, "coin-unread")]
    [InlineData(true, 1999L, LodgeFront, true, "coin-short")]
    [InlineData(true, 4640L, LodgeSlot, true, "cell-unsafe")]
    [InlineData(true, 4640L, LodgeFront, false, "gate-held:lodge")]
    [InlineData(true, 2000L, LodgeFront, true, "met")]
    public void Each_condition_stops_the_fetch_by_its_name_and_only_all_of_them_fetch(bool onCorpse, long? coin, uint cell, bool gateOpen, string reason)
    {
        var gate = gateOpen ? Open : new CorpseRunAnswer(false, "gate-held:lodge", "lodge");
        var ask = new CorpseCoinAsk(Coin with { OnCorpse = onCorpse }, coin, cell, Ground("tusker-lodge"), gate);

        var answer = CorpseCoinRule.Decide(in ask);

        Assert.Equal((reason == "met", reason), (answer.Fetch, answer.Reason));
        Assert.EndsWith((answer.Fetch ? "-> fetch:" : "-> no-fetch:") + reason, CorpseCoinRule.Line(in ask, answer), StringComparison.Ordinal);
    }

    private static CorpseTripAsk Trip(uint cell, bool gateOpen = true, int wornMissing = 0, bool casterKept = true, bool kitLost = false, long? itemValue = null, bool unread = false) =>
        new(Coin, wornMissing, casterKept, kitLost, false, itemValue, unread, cell, Ground("tusker-lodge"), gateOpen ? Open : new CorpseRunAnswer(false, "gate-held:lodge", "lodge"));

    [Theory]
    [InlineData(LodgeFront, 1, true, false, null, false, true, "worn-gear")] // gear lost and the front cell is safe: walk
    [InlineData(LodgeFront, 0, false, false, null, false, true, "caster")]
    [InlineData(LodgeFront, 0, true, true, null, false, true, "kit")]
    [InlineData(LodgeFront, 0, true, false, 20000L, false, true, "value")]
    [InlineData(LodgeFront, 0, true, false, 19999L, false, false, "nothing-needed")] // under the data line, nothing worn, no caster, no kit
    [InlineData(LodgeFront, 0, true, false, null, true, true, "unread")]
    [InlineData(LodgeSlot, 1, true, false, null, false, false, "cell-unsafe")] // the live death: a Leather Cuirass deep in the Lodge, not walked to
    [InlineData(LodgeDeep, 0, false, false, null, false, false, "cell-unsafe")]
    public void A_corpse_is_walked_to_only_for_what_she_needs_and_only_where_it_is_safe(uint cell, int worn, bool caster, bool kit, long? value, bool unread, bool fetch, string reason)
    {
        var ask = Trip(cell, wornMissing: worn, casterKept: caster, kitLost: kit, itemValue: value, unread: unread);

        var answer = CorpseCoinRule.DecideTrip(in ask);

        Assert.Equal((fetch, reason), (answer.Fetch, answer.Reason));
        Assert.EndsWith((fetch ? "-> walk:" : "-> leave-corpse:") + reason, CorpseCoinRule.TripLine(in ask, answer), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 2000L, LodgeFront, true, "coin")] // items dropped beside the coin, none needed: the coin is worth the walk
    [InlineData(true, 1999L, LodgeFront, false, "nothing-needed")]
    [InlineData(true, null, LodgeFront, false, "nothing-needed")]
    [InlineData(false, 4640L, LodgeFront, false, "nothing-needed")] // a server that destroys the coin: nothing to walk for
    [InlineData(true, 4640L, LodgeSlot, false, "cell-unsafe")] // the safe-spot rule holds for coin too
    public void The_coin_counts_as_a_need_only_where_the_server_keeps_it_and_the_cell_is_safe(bool onCorpse, long? coin, uint cell, bool fetch, string reason)
    {
        var ask = Trip(cell) with { Rule = new LadderCorpseCoin(onCorpse, 2000), Coin = coin };

        var answer = CorpseCoinRule.DecideTrip(in ask);

        Assert.Equal((fetch, reason), (answer.Fetch, answer.Reason));
        Assert.Contains("coin=" + (coin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unread") + " ", CorpseCoinRule.TripLine(in ask, answer), StringComparison.Ordinal);
    }

    [Fact]
    public void A_needed_corpse_behind_a_shut_gate_is_held_by_the_gates_name_and_the_item_value_line_is_data()
    {
        var ask = Trip(LodgeFront, gateOpen: false, wornMissing: 1);

        Assert.Equal(new CorpseCoinAnswer(false, "gate-held:lodge"), CorpseCoinRule.DecideTrip(in ask));
        Assert.Equal(20000, L.CorpseCoin.MinItemValue);
    }

    [Fact]
    public void The_ladder_says_the_coin_lies_on_the_corpse_and_names_the_min()
    {
        Assert.Equal(new LadderCorpseCoin(true, 2000, 20000, 90, 10), L.CorpseCoin);
        Assert.Contains("corpse_destroy_pyreals", L.Sources["corpse_coin"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(17, false, "lodge", false, "gate-held:lodge")] // her rung's gate is shut: no fetch
    [InlineData(17, true, "lodge", true, "gate-open:lodge")]
    [InlineData(21, false, "tusker-lodge-past", false, "")]
    public void A_corpse_run_on_a_gated_ground_reads_the_gate(int level, bool met, string rung, bool open, string reason)
    {
        if (rung == "tusker-lodge-past")
        {
            var past = RungOwner.CorpseRun(L, Ask(22, met), "tusker-lodge"); // past the Lodge's band no gate is due by the band: the corpse's own gate still stands
            Assert.False(past.Open);
            Assert.Equal("lodge", past.Rung);
            return;
        }

        var answer = RungOwner.CorpseRun(L, Ask(level, met), "tusker-" + rung);
        Assert.Equal((open, rung), (answer.Open, answer.Rung));
        Assert.StartsWith(reason, answer.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corpse_on_a_ground_below_a_shut_gate_is_open_and_one_on_no_rung_is_no_rungs_matter()
    {
        Assert.True(RungOwner.CorpseRun(L, Ask(17, met: false), "holtburg-outskirts").Open);
        Assert.Equal(new CorpseRunAnswer(true, "no-rung", null), RungOwner.CorpseRun(L, Ask(17, met: false), null));
        Assert.Equal(new CorpseRunAnswer(true, "no-rung", null), RungOwner.CorpseRun(L, Ask(17, met: false), "black-spawn-den-aluvian"));
    }

    [Fact]
    public void She_stands_on_the_ground_so_the_corpse_there_is_open_as_she_is_never_stranded_outside_it()
    {
        Assert.True(RungOwner.CorpseRun(L, Ask(17, met: false, cell: LodgeFront), "tusker-lodge").Open);
    }

    [Fact]
    public void Unknown_buffs_never_open_a_gate_for_a_corpse_run()
    {
        Assert.False(RungOwner.CorpseRun(L, Ask(17, met: true) with { Buffs = null }, "tusker-lodge").Open);
    }

    [Theory]
    [InlineData(0x5749017Cu, "tusker-lodge")]
    [InlineData(0xA9B30021u, "holtburg-outskirts")]
    [InlineData(0xF6820021u, null)] // the island road
    public void The_corpses_ground_is_the_rungs_that_holds_its_cell(uint cell, string? ground) => Assert.Equal(ground, RungOwner.GroundOf(L, cell)?.Key);
}
