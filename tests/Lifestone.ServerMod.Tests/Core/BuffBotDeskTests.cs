using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// D138 (m6-plan.md): the paid bot's day (<see cref="BuffBotDesk"/>), its casts by a player's rules (<see cref="BuffBotCasting"/>), its trade at the shop (<see cref="BuffBotShop"/>) and its pack and state file
/// (<see cref="BuffBotLedger"/>). Nothing here runs ACE: the numbers are the bot's (<see cref="BuffBotRules"/>) and the dats' (level VII: power 300, ComponentLoss 0.15; the taper's CDM 1.0, the Platinum
/// Scarab's 0.03). The live proof is the lead's: a give, the credit line, a paid round's casts, mana and burns in the server log, and one trip to Archmage Cindrue and back.
/// </summary>
public sealed class BuffBotDeskTests
{
    private const uint Qwinnie = 0x50000A01;
    private const uint Other = 0x50000A02;

    private static readonly BuffBotSlot Scarab = new(BuffBotWire.FeeWcid, BuffBotRules.ScarabName, 0.03f);
    private static readonly BuffBotSlot Taper = new(BuffBotRules.TaperWcid, BuffBotRules.TaperName, 1.0f);

    /// <summary>The level VII foci formula: the Platinum Scarab and four Prismatic Tapers (SpellFormula.cs 340-381 @95ef426d: power 8, four tapers).</summary>
    private static readonly BuffBotSlot[] Foci = [Scarab, Taper, Taper, Taper, Taper];

    private static BuffBotCaller At(int level, uint guid = Qwinnie) => new(guid, guid == Qwinnie ? "Qwinnie" : "Other", level);

    private static BuffBotHook Word(BuffBotProfile? profile = null) => new(BuffBotHookKind.Word, profile ?? BuffBotWire.All);

    private static string[] Tells(IEnumerable<BuffBotAct> acts) => [.. acts.Where(a => a.Kind == BuffBotActKind.Tell).Select(a => a.Text!)];

    private static Func<double> Rolls(params double[] rolls)
    {
        var queue = new Queue<double>(rolls);
        return () => queue.Count > 0 ? queue.Dequeue() : 0.99;
    }

    // ------------------------------------------------------------------------------------------------ credits and the fee

    [Fact]
    public void A_scarab_given_is_one_credit_and_one_scarab_in_the_pack_and_the_bot_tells_the_count_which_the_wire_reads_back()
    {
        var desk = new BuffBotDesk(BuffBotLedger.Outfit());
        var acts = desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
        Assert.Equal(["You have 1 credit."], Tells(acts));
        Assert.Contains(acts, a => a.Kind == BuffBotActKind.Save); // the state file is written at once
        Assert.Equal((1, BuffBotLedger.OutfitScarabs + 1), (desk.Ledger.CreditsOf(Qwinnie), desk.Ledger.Scarabs)); // the scarab feeds the bot's own casting

        Assert.Equal(["You have 2 credits."], Tells(desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 1)));
        Assert.Equal(["You have 2 credits."], Tells(desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Credits), 2))); // the credit word: the count, nothing spent
        Assert.Equal(["You have 0 credits."], Tells(desk.Hear(At(64, Other), new BuffBotHook(BuffBotHookKind.Credits), 3)));
        Assert.Equal(2, desk.Ledger.CreditsOf(Qwinnie));

        foreach (var n in new[] { 0, 1, 2, 17 })
        {
            Assert.True(BuffBotWire.TryReadCredits(BuffBotWire.Credits(n), out var read));
            Assert.Equal(n, read);
        }

        Assert.False(BuffBotWire.TryReadCredits("You have 1 credits.", out _)); // only the bot's own line
        Assert.False(BuffBotWire.TryReadCredits(BuffBotWire.Spent(1), out _));
        Assert.False(BuffBotWire.TryReadCredits("You have killed 3 Tuskers!", out _));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(64, true)]
    public void A_round_is_free_under_level_60_and_from_60_takes_one_credit_and_says_how_many_are_left(int level, bool paid)
    {
        var desk = new BuffBotDesk(BuffBotLedger.Outfit());
        desk.Hear(At(level), new BuffBotHook(BuffBotHookKind.Give), 0);
        desk.Hear(At(level), new BuffBotHook(BuffBotHookKind.Give), 0);
        var acts = desk.Hear(At(level), Word(), 1);

        Assert.Equal(BuffBotPhase.Round, desk.Phase);
        Assert.Equal(paid, desk.Round!.Charged);
        Assert.Equal(paid ? 1 : 2, desk.Ledger.CreditsOf(Qwinnie)); // one credit a round, from the fee's level
        Assert.Equal(paid ? ["One credit spent, 1 left.", BuffBotWire.Begins(BuffBotWire.All)] : [BuffBotWire.Begins(BuffBotWire.All)], Tells(acts));
        Assert.Equal(BuffBotActKind.Turn, acts[0].Kind);
        Assert.Equal(BuffBotActKind.Round, acts[^1].Kind);
        Assert.Same(BuffBotWire.All, acts[^1].Profile);
        Assert.Equal(60, BuffBotWire.FeeLevel);
    }

    [Fact]
    public void With_no_credit_from_the_fee_level_the_bot_says_by_name_what_is_missing_and_casts_nothing()
    {
        var desk = new BuffBotDesk(BuffBotLedger.Outfit());
        var acts = desk.Hear(At(64), Word(), 0);
        Assert.Equal(["You need a credit: hand me a Platinum Scarab."], Tells(acts));
        Assert.DoesNotContain(acts, a => a.Kind == BuffBotActKind.Round);
        Assert.Equal(BuffBotPhase.Idle, desk.Phase);
        Assert.Contains(acts, a => a.Kind == BuffBotActKind.Log && a.Text!.Contains("no credit: Qwinnie (level 64, the fee from 60)", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_round_takes_its_own_credit_and_a_round_stopped_short_gives_its_credit_back()
    {
        var desk = new BuffBotDesk(BuffBotLedger.Outfit());
        desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
        desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
        desk.Hear(At(64), Word(), 1);
        var done = desk.RoundEnded(BuffBotRoundEnd.Completed, null, 40);
        Assert.Equal([BuffBotWire.Done(BuffBotWire.All)], Tells(done));
        Assert.Equal(1, desk.Ledger.CreditsOf(Qwinnie)); // a whole round keeps its credit

        desk.Hear(At(64), Word(BuffBotWire.Tusker), 50);
        Assert.Equal(0, desk.Ledger.CreditsOf(Qwinnie));
        var stopped = desk.RoundEnded(BuffBotRoundEnd.OutOfComponents, BuffBotRules.TaperName, 60);
        Assert.Equal(["I have no more Prismatic Tapers: I stop here and your credit is returned."], Tells(stopped));
        Assert.Equal(1, desk.Ledger.CreditsOf(Qwinnie)); // given back

        desk.Hear(At(64), Word(), 70);
        Assert.Equal([BuffBotWire.OutOfReach], Tells(desk.RoundEnded(BuffBotRoundEnd.OutOfReach, "40 m", 71)));
        Assert.Equal(1, desk.Ledger.CreditsOf(Qwinnie));
    }

    [Fact]
    public void A_word_while_a_round_runs_is_answered_busy_and_another_tellers_is_kept_for_after_it()
    {
        var desk = new BuffBotDesk(BuffBotLedger.Outfit());
        desk.Hear(At(30), Word(), 0);
        Assert.Equal([BuffBotWire.Busy], Tells(desk.Hear(At(30), Word(), 5))); // her own second tell: nothing queued, nothing charged
        Assert.Equal([BuffBotWire.Busy], Tells(desk.Hear(At(30, Other), Word(BuffBotWire.Tusker), 6)));
        Assert.Single(desk.Waiting);

        var after = desk.RoundEnded(BuffBotRoundEnd.Completed, null, 40);
        Assert.Equal([BuffBotWire.Done(BuffBotWire.All), BuffBotWire.Begins(BuffBotWire.Tusker)], Tells(after)); // the next teller's round at once
        Assert.Equal(Other, desk.Round!.Caller.Guid);
        Assert.Empty(desk.Waiting);
    }

    // ------------------------------------------------------------------------------------------------ a cast by a player's rules

    [Fact]
    public void The_chance_and_the_burn_rates_are_ACEs_for_the_bots_numbers()
    {
        // GetMagicSkillChance (0.07): skill 380 against power 300 lands 99.63% of the time; none under power - 50.
        Assert.Equal(1 - (1 / (1 + Math.Exp(0.07 * 80))), BuffBotCasting.Chance(380, 300), 12);
        Assert.InRange(BuffBotCasting.Chance(380, 300), 0.9963, 0.9964);
        Assert.Equal(0.5, BuffBotCasting.Chance(300, 300), 12);
        Assert.Equal(0, BuffBotCasting.Chance(249, 300));
        Assert.True(BuffBotCasting.Chance(250, 300) > 0);

        // ComponentLoss x CDM x min(1, Power / skill): a taper slot 0.118, a scarab slot 0.00355; about 0.47 tapers a cast on four slots, 13 a round of 28.
        Assert.Equal(0.15f * 1.0f * (300f / 380f), BuffBotCasting.BurnRate(0.15f, 1.0f, 300, 380), 6);
        Assert.InRange(BuffBotCasting.BurnRate(0.15f, 0.03f, 300, 380), 0.00355f, 0.00356f);
        Assert.Equal(0.15f, BuffBotCasting.BurnRate(0.15f, 1.0f, 300, 200)); // a skill under the power burns at the full rate
        Assert.InRange(28 * 4 * BuffBotCasting.BurnRate(0.15f, 1.0f, 300, 380), 13.0, 13.5);

        // The mana: a whole round of 28 at about 18 a cast is about 500, which the bot's 900 pays once with room; the rest seconds are the deficit at ACE's rate, and one heartbeat more.
        Assert.Equal(900u, 290 + BuffBotRules.ManaInitLevel);
        Assert.Equal(380u, 145 + BuffBotRules.MagicInitLevel);
        Assert.Equal(300u, 96 + BuffBotRules.ManaConversionInitLevel);
        Assert.Equal(122, BuffBotCasting.RestSeconds(50, BuffBotRules.ManaRate * 2.15 / 5.0, 5.0)); // under its own Mana Renewal (x2.15, BuffBotSelf): 50 / 0.43 = 116.3 s, and a heartbeat
    }

    [Fact]
    public void A_cast_that_lands_spends_ACEs_mana_cost_and_burns_each_slot_by_its_roll()
    {
        var pack = BuffBotLedger.Outfit();
        // Rolls: the fizzle roll (lands), then one a slot: the scarab misses, the first and third tapers burn.
        var cast = BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 900, 18, 0.43, 5, Rolls(0.5, 0.9, 0.05, 0.5, 0.1, 0.9));
        Assert.Equal(BuffBotCastKind.Landed, cast.Kind);
        Assert.Equal(18u, cast.Mana);
        Assert.Equal([Taper, Taper], cast.Burned);
        Assert.Equal((500, 10), (pack.Tapers, pack.Scarabs)); // nothing taken until the wind-up ends
        BuffBotCasting.Apply(pack, cast);
        Assert.Equal((498, 10), (pack.Tapers, pack.Scarabs));
    }

    [Fact]
    public void A_fizzle_costs_five_mana_and_still_burns_as_a_players_does()
    {
        var pack = BuffBotLedger.Outfit();
        var cast = BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 900, 18, 0.43, 5, Rolls(0.9999, 0.001, 0.9, 0.9, 0.9, 0.9));
        Assert.Equal(BuffBotCastKind.Fizzled, cast.Kind);
        Assert.Equal(BuffBotCasting.FizzleMana, cast.Mana);
        Assert.Equal([Scarab], cast.Burned); // the burn comes before the outcome's switch (Player_Magic.cs 866-868)
        BuffBotCasting.Apply(pack, cast);
        Assert.Equal((500, 9), (pack.Tapers, pack.Scarabs));
        Assert.Equal(3, BuffBotCasting.MaxFizzlesPerLine);
    }

    [Fact]
    public void A_cast_the_mana_does_not_pay_rests_for_the_regeneration_and_spends_nothing()
    {
        var pack = BuffBotLedger.Outfit();
        var cast = BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 10, 60, 0.43, 5, Rolls(0.5));
        Assert.Equal(BuffBotCastKind.NoMana, cast.Kind);
        Assert.Equal(BuffBotCasting.RestSeconds(50, 0.43, 5), cast.RestSeconds);
        Assert.Empty(cast.Burned);
        Assert.Equal("My mana is low: I rest 122 seconds, then I go on.", BuffBotWire.Rest(122));
        Assert.True(BuffBotWire.TryReadRest(BuffBotWire.Rest(122), out var seconds));
        Assert.Equal(122, seconds);
        Assert.False(BuffBotWire.TryReadRest("My mana is low: I rest soon.", out _));

        // A fizzle needs only its 5.
        Assert.Equal(BuffBotCastKind.Fizzled, BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 10, 60, 0.43, 5, Rolls(0.9999)).Kind);
    }

    [Fact]
    public void A_cast_with_a_component_missing_names_it_and_burns_and_spends_nothing()
    {
        var pack = new BuffBotLedger { Tapers = 3, Scarabs = 5 };
        var cast = BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 900, 18, 0.43, 5, Rolls(0.5));
        Assert.Equal((BuffBotCastKind.NoComponent, BuffBotRules.TaperName), (cast.Kind, cast.Missing)); // four tapers are needed in the pack, as a player needs them (HasComponentsForSpell)
        Assert.Equal(BuffBotRules.ScarabName, BuffBotCasting.Missing(new BuffBotLedger { Tapers = 40 }, Foci));
        Assert.Null(BuffBotCasting.Missing(new BuffBotLedger { Tapers = 4, Scarabs = 1 }, Foci));
        Assert.Equal("Sunstone", BuffBotCasting.Missing(new BuffBotLedger { Tapers = 40, Scarabs = 4 }, [new BuffBotSlot(999, "Sunstone", 1f)])); // a ware the bot never carries
    }

    [Fact]
    public void Over_many_casts_the_burns_and_the_fizzles_average_ACEs_rates()
    {
        var random = new Random(138);
        var pack = new BuffBotLedger { Tapers = 1_000_000, Scarabs = 1_000_000 };
        const int Casts = 200_000;
        var fizzles = 0;
        for (var i = 0; i < Casts; i++)
        {
            var cast = BuffBotCasting.Attempt(pack, Foci, 380, 300, 0.15f, 900, 18, 0.43, 5, random.NextDouble);
            fizzles += cast.Kind == BuffBotCastKind.Fizzled ? 1 : 0;
            BuffBotCasting.Apply(pack, cast);
        }

        var tapers = (1_000_000 - pack.Tapers) / (double)Casts;
        var scarabs = (1_000_000 - pack.Scarabs) / (double)Casts;
        Assert.InRange(tapers, 0.465, 0.482); // 4 x 0.1184
        Assert.InRange(scarabs, 0.0030, 0.0041); // 0.00355
        Assert.InRange(fizzles / (double)Casts, 0.0028, 0.0047); // 0.37%
    }

    // ------------------------------------------------------------------------------------------------ the restock trip, in its order

    [Fact]
    public void After_a_round_under_the_restock_line_it_waits_ten_seconds_then_says_it_is_away_then_leaves()
    {
        var desk = new BuffBotDesk(new BuffBotLedger { Tapers = 120, Scarabs = 10, Pyreals = 50_000 });
        desk.Hear(At(30), Word(), 0);
        desk.Ledger.Tapers = 99; // the round's burns took it under the line

        // 1. The round finishes: its done line, and no leaving yet.
        var ended = desk.RoundEnded(BuffBotRoundEnd.Completed, null, 100);
        Assert.Equal([BuffBotWire.Done(BuffBotWire.All)], Tells(ended));
        Assert.DoesNotContain(ended, a => a.Kind is BuffBotActKind.Say or BuffBotActKind.Leave);
        Assert.Equal((BuffBotPhase.Leaving, 110.0), (desk.Phase, desk.LeaveAt));

        // 2. Ten seconds: nothing said, nothing walked.
        Assert.DoesNotContain(desk.Tick(100.5), a => a.Kind is BuffBotActKind.Say or BuffBotActKind.Leave);
        Assert.Empty(desk.Tick(109.9));

        // 3. Then the announcement in local chat, 4. then the walk, in that order.
        var leaving = desk.Tick(110);
        Assert.Equal([BuffBotActKind.Say, BuffBotActKind.Leave], leaving.Select(a => a.Kind));
        Assert.Equal("I am out of tapers. Back shortly.", leaving[0].Text);
        Assert.Equal(BuffBotPhase.Away, desk.Phase);
        Assert.Empty(desk.Tick(111)); // said once
        Assert.Equal(10.0, BuffBotRules.LeaveDelaySeconds);
        Assert.Equal(100, BuffBotRules.RestockUnder);
    }

    [Fact]
    public void It_never_leaves_in_the_middle_of_a_round_and_holds_its_post_over_the_line()
    {
        var desk = new BuffBotDesk(new BuffBotLedger { Tapers = 40, Scarabs = 10 });
        desk.Hear(At(30), Word(), 0);
        for (var t = 1.0; t < 600; t += 1)
        {
            Assert.Empty(desk.Tick(t)); // under the line all the while: the round in hand finishes first
            Assert.Equal(BuffBotPhase.Round, desk.Phase);
        }

        desk.RoundEnded(BuffBotRoundEnd.Completed, null, 600);
        Assert.Equal(BuffBotPhase.Leaving, desk.Phase);

        var full = new BuffBotDesk(new BuffBotLedger { Tapers = 100 });
        Assert.Empty(full.Tick(0)); // 100 is not under the line
        Assert.Equal(BuffBotPhase.Idle, full.Phase);
    }

    [Fact]
    public void A_word_or_a_credit_ask_while_it_is_leaving_or_away_is_answered_away_and_served_when_it_is_back()
    {
        var desk = new BuffBotDesk(new BuffBotLedger { Tapers = 50, Scarabs = 10 });
        desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
        desk.Tick(1);
        Assert.Equal(BuffBotPhase.Leaving, desk.Phase);
        Assert.Equal([BuffBotWire.AwayTell], Tells(desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Credits), 2)));
        desk.Tick(11);
        Assert.Equal(BuffBotPhase.Away, desk.Phase);
        Assert.Equal([BuffBotWire.AwayTell], Tells(desk.Hear(At(64), Word(), 30)));
        Assert.Equal([BuffBotWire.AwayTell], Tells(desk.Hear(At(64), Word(), 31))); // told again: one ask of each kind is kept
        Assert.Equal(2, desk.Waiting.Count);
        Assert.Equal(1, desk.Ledger.CreditsOf(Qwinnie)); // nothing charged while away
        Assert.Equal(["You have 2 credits."], Tells(desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 40))); // a give is taken at once (ACE has the scarab)

        desk.Ledger.Tapers = 950;
        var back = desk.TripEnded(true, "bought 900", 90);
        var said = back.FindIndex(a => a.Kind == BuffBotActKind.Say);
        Assert.Equal("I am back. Tell me your word.", back[said].Text);
        Assert.True(said < back.FindIndex(a => a.Kind == BuffBotActKind.Tell)); // the back line first, then the kept asks
        Assert.Equal(["You have 2 credits.", "One credit spent, 1 left.", BuffBotWire.Begins(BuffBotWire.All)], Tells(back)); // in the order asked
        Assert.Equal(BuffBotPhase.Round, desk.Phase);
        Assert.Empty(desk.Waiting);
    }

    [Fact]
    public void A_trip_that_failed_is_not_tried_again_for_ten_minutes_and_a_bot_placed_again_forgets_its_trip()
    {
        var desk = new BuffBotDesk(new BuffBotLedger { Tapers = 50 });
        desk.Tick(0);
        desk.Tick(10);
        Assert.Equal(BuffBotPhase.Away, desk.Phase);
        desk.TripEnded(false, "stuck", 100);
        Assert.Equal(BuffBotPhase.Idle, desk.Phase);
        Assert.Empty(desk.Tick(699));
        Assert.Equal(BuffBotPhase.Idle, desk.Phase);
        desk.Tick(700);
        Assert.Equal(BuffBotPhase.Leaving, desk.Phase);

        desk.Reset();
        Assert.Equal((BuffBotPhase.Idle, (BuffBotRound?)null, 0), (desk.Phase, desk.Round, desk.Waiting.Count));
        Assert.Equal(600.0, BuffBotRules.RetryTripSeconds);
    }

    // ------------------------------------------------------------------------------------------------ the trade and the purse

    [Fact]
    public void The_vendors_prices_are_ACEs_on_a_stacks_whole_value()
    {
        // Archmage Cindrue: SellPrice 1.35, BuyPrice 0.9 (the profiles' vendor row); the taper's Value 22, the Platinum Scarab's 10,000 (the pinned world SQL v0.9.296).
        Assert.Equal(30, BuffBotShop.SellCost(1.35, 22, 1)); // the climber's 30 pyreals a taper
        Assert.Equal(26_730, BuffBotShop.SellCost(1.35, 22, 900)); // a stack is priced whole: 29.7 a taper
        Assert.Equal(9_000, BuffBotShop.BuyCost(0.9, 10_000, 1));
        Assert.Equal(45_000, BuffBotShop.BuyCost(0.9, 10_000, 5));
        Assert.Equal(0, BuffBotShop.SellCost(1.35, 22, 0));
        Assert.Equal(1, BuffBotShop.BuyCost(0.9, 0, 1)); // ACE's floor of 1
    }

    [Fact]
    public void The_trade_sells_the_scarabs_over_the_keep_at_her_buy_price_and_buys_tapers_up_to_a_stack()
    {
        var pack = new BuffBotLedger { Tapers = 90, Scarabs = 14, Pyreals = 1_000 };
        var trade = BuffBotShop.Plan(pack, 1.35, 22, 0.9, 10_000, buysScarabs: true);
        Assert.Equal((4, 36_000L), (trade.ScarabsSold, trade.SaleTotal)); // 14 - the keep of 10
        Assert.Equal((910, BuffBotShop.SellCost(1.35, 22, 910)), (trade.TapersBought, trade.BuyTotal));
        Assert.False(trade.Short);
        BuffBotShop.Apply(pack, trade);
        Assert.Equal((1000, 10, 1_000 + 36_000 - trade.BuyTotal), (pack.Tapers, pack.Scarabs, pack.Pyreals));
        Assert.Equal(BuffBotRules.RestockTo, pack.Tapers);

        // A vendor who buys no scarab: none sold.
        Assert.Equal(0, BuffBotShop.Plan(new BuffBotLedger { Tapers = 90, Scarabs = 14, Pyreals = 50_000 }, 1.35, 22, 0.9, 10_000, buysScarabs: false).ScarabsSold);
    }

    [Fact]
    public void A_purse_that_cannot_pay_a_whole_stack_buys_what_it_can_and_says_it_was_short()
    {
        var pack = new BuffBotLedger { Tapers = 90, Scarabs = 10, Pyreals = 3_000 };
        var trade = BuffBotShop.Plan(pack, 1.35, 22, 0.9, 10_000, buysScarabs: true);
        Assert.Equal(0, trade.ScarabsSold); // the keep is not sold
        Assert.True(trade.Short);
        Assert.InRange(trade.BuyTotal, 2_970, 3_000);
        Assert.True(BuffBotShop.SellCost(1.35, 22, trade.TapersBought + 1) > 3_000); // the most the purse pays
        Assert.Equal(0, BuffBotShop.Plan(new BuffBotLedger { Tapers = 90, Scarabs = 10, Pyreals = 0 }, 1.35, 22, 0.9, 10_000, buysScarabs: true).TapersBought);
    }

    // ------------------------------------------------------------------------------------------------ the state file

    [Fact]
    public void The_state_file_keeps_the_pack_the_purse_and_every_payers_credits_across_a_restart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lifestone-buffbot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, BuffBotLedger.FileName);
            var first = BuffBotLedger.Load(path, out var outfit);
            Assert.True(outfit); // no file yet: the monarchy's outfit
            Assert.Equal((500, 10, 50_000L, 0), (first.Tapers, first.Scarabs, first.Pyreals, first.Payers.Count));

            var desk = new BuffBotDesk(first);
            desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
            desk.Hear(At(64), new BuffBotHook(BuffBotHookKind.Give), 0);
            desk.Hear(new BuffBotCaller(Other, "Lifestone \"Smoke\"", 70), new BuffBotHook(BuffBotHookKind.Give), 0);
            desk.Hear(At(64), Word(), 1);
            first.Tapers = 487;
            first.Save(path, new DateTime(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc));
            Assert.False(File.Exists(path + ".writing")); // written whole, then moved over

            var again = BuffBotLedger.Load(path, out outfit);
            Assert.False(outfit);
            Assert.Equal((487, 13, 50_000L), (again.Tapers, again.Scarabs, again.Pyreals));
            Assert.Equal((1, 1), (again.CreditsOf(Qwinnie), again.CreditsOf(Other)));
            Assert.Equal("Lifestone \"Smoke\"", again.Payers.Single(p => p.Guid == Other).Name);
            Assert.Contains("\"written_utc\": \"2026-11-14T18:00:00Z\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Contains("\"guid\": \"0x50000A01\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("not json", "not JSON")]
    [InlineData("{\"version\":2,\"tapers\":1,\"scarabs\":1,\"pyreals\":1,\"credits\":[]}", "version 2, not 1")]
    [InlineData("{\"version\":1,\"tapers\":-1,\"scarabs\":1,\"pyreals\":1,\"credits\":[]}", "tapers is not a whole number")]
    [InlineData("{\"version\":1,\"tapers\":1,\"scarabs\":1,\"pyreals\":1,\"credits\":[{\"guid\":\"50000A01\",\"name\":\"Q\",\"credits\":1}]}", "is not 0x and eight hex digits")]
    [InlineData("{\"version\":1,\"tapers\":1,\"scarabs\":1,\"pyreals\":1,\"credits\":[{\"guid\":\"0x1\",\"name\":\"Q\",\"credits\":1},{\"guid\":\"0x1\",\"name\":\"Q\",\"credits\":2}]}", "listed twice")]
    public void A_state_file_it_cannot_read_is_named_by_its_fault_and_never_taken_for_an_empty_ledger(string json, string fault)
    {
        var e = Assert.Throws<FormatException>(() => BuffBotLedger.FromJson(json));
        Assert.Contains(fault, e.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the walk

    [Fact]
    public void The_walk_to_the_shop_starts_at_the_bots_post_and_ends_in_Cindrues_entry_hall_under_her()
    {
        var route = BuffBotRules.ShopRoute;
        Assert.Equal(new BuffBotWaypoint(BuffBotWire.Cell, BuffBotWire.X, BuffBotWire.Y, 94.0f), route[0]);
        Assert.Equal(new BuffBotWaypoint(0xA9B40117, 152.3f, 132.5f, 66.0f), route[^1]); // her stop point, the climber's shop step's (the profiles' vendor row)
        Assert.Contains(route, p => p.Cell == 0xA9B40118); // through her front door's doorway room
        Assert.All(route, p => Assert.Equal(BuffBotWire.Landblock, (ushort)(p.Cell >> 16))); // one landblock: ACE's creature move-to and the recall blip stay on it
        var metres = route.Zip(route.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))).Sum();
        Assert.InRange(metres, 150, 162);
        Assert.Equal(0x7A9B4033u, BuffBotRules.CindrueGuid);
    }
}
