using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Housing;

/// <summary>
/// M6 villa, the climb's wiring: the villa schedule's decisions at a town trip's boundary (<see cref="VillaSchedule"/>): the writ pace against the account's opening, the purchase's gating, the rent's due time, the storage trip's trigger,
/// the weigh order with its once-a-boundary and never-again-after-a-failure rules, the cycle line's marker, and the villa station kind's specs. The owner's 2026-11-16 decision (a house needs no account age) opens the purchase at once
/// (<see cref="VillaFacts.ClimberAccountOpensVillas"/>, the <c>Open*</c> tests); the rest read the retail flag's path (<see cref="VillaFacts.ClimberAccountOpensVillasRetail"/>, the server's <c>LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace</c>).
/// </summary>
public sealed class VillaScheduleTests
{
    /// <summary>The retail flag's opening: 2026-11-24 22:51:38 UTC.</summary>
    private static readonly DateTimeOffset Opens = VillaFacts.ClimberAccountOpensVillasRetail;

    /// <summary>The live start of the wiring (2026-11-16 18:00 UTC): 8 days 4.9 hours before the opening.</summary>
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);

    private static VillaInput Input(
        DateTimeOffset now,
        HouseOwnership status = HouseOwnership.None,
        PriceHeld? held = null,
        int stones = 0,
        bool half = false,
        QuestTimers? timers = null,
        int store = 0,
        long reviewFree = 5,
        bool walk = true,
        bool rentPaid = true,
        long rentDue = 0,
        IReadOnlyList<(uint, string, long)>? owed = null,
        IReadOnlySet<VillaStep>? failed = null,
        IReadOnlySet<VillaStep>? done = null,
        int level = 78,
        long coins = 11_000_000,
        DateTimeOffset? opens = null,
        int boundaryFetches = 0) =>
        new(now, opens ?? Opens, VillaFacts.Block1401, status, rentPaid, rentDue, status == HouseOwnership.Owned ? OwnedHouse.RentPeriodSeconds : 0, owed ?? [], VillaFacts.RentWrits, level, held ?? new PriceHeld(11_000_000, 0, 0, 1), coins, stones, half,
            timers ?? new QuestTimers("villa-schedule-test"), WritFacts.Caves, store, reviewFree, walk, failed ?? new HashSet<VillaStep>(), done ?? new HashSet<VillaStep>(), boundaryFetches);

    [Fact]
    public void Open_the_purchase_opens_at_once_and_the_writs_are_wanted_now_at_the_fastest_the_caves_allow()
    {
        Assert.Equal(DateTimeOffset.MinValue, VillaFacts.ClimberAccountOpensVillas); // the owner, 2026-11-16: the server mod turns ACE's house_15day_account off
        var timers = new QuestTimers("villa-open");
        var input = Input(Monday, timers: timers, opens: VillaFacts.ClimberAccountOpensVillas);
        Assert.Equal(("purchase+rent", 7, Monday), VillaSchedule.WritGoal(input)); // wanted now: no date to pace to

        var pace = VillaSchedule.Pace(input);
        Assert.Equal(("open", true, 14), (pace.Token, pace.Due, pace.StonesToFetch));
        Assert.Equal(7.0, pace.FastestHours, 3); // two walkable caves, one stone an hour each (the stone files' timer_seconds 3600): ceil(14 / 2) x 1 h
        Assert.Contains("the purchase is open now", pace.Why, StringComparison.Ordinal);

        timers.Complete(QuestCatalog.Get("minor-smoldering-stone"), Monday.AddMinutes(-5), QuestTimers.HowItem);
        var right = VillaSchedule.Pace(Input(Monday, stones: 1, timers: timers, opens: VillaFacts.ClimberAccountOpensVillas));
        Assert.Equal(("open", true), (right.Token, right.Due)); // no spacing: the next town trip fetches again, the caves' timers are the only limit
    }

    [Fact]
    public void Open_each_walkable_cave_due_is_fetched_at_one_town_trip_and_no_more()
    {
        var timers = new QuestTimers("villa-open-two");
        var open = VillaFacts.ClimberAccountOpensVillas;
        var first = VillaSchedule.Decide(Input(Monday, timers: timers, opens: open));
        Assert.Equal((VillaStep.WritFetch, "open", "minor-smoldering-stone"), (first.Step, first.Token, first.Cave!.QuestKey));

        timers.Complete(QuestCatalog.Get("minor-smoldering-stone"), Monday, QuestTimers.HowItem); // the fetch picked it up
        var second = VillaSchedule.Decide(Input(Monday.AddMinutes(20), stones: 1, timers: timers, opens: open, done: new HashSet<VillaStep> { VillaStep.WritFetch }, boundaryFetches: 1));
        Assert.Equal((VillaStep.WritFetch, "minor-sparking-stone"), (second.Step, second.Cave!.QuestKey)); // the other cave at the same town trip

        timers.Complete(QuestCatalog.Get("minor-sparking-stone"), Monday.AddMinutes(40), QuestTimers.HowItem);
        var handIn = VillaSchedule.Decide(Input(Monday.AddMinutes(40), stones: 2, timers: timers, opens: open, done: new HashSet<VillaStep> { VillaStep.WritFetch }, boundaryFetches: 2));
        Assert.Equal(VillaStep.WritHandIn, handIn.Step); // the pair to the Agent

        var spent = VillaSchedule.Decide(Input(Monday.AddMinutes(45), timers: timers, opens: open, done: new HashSet<VillaStep> { VillaStep.WritFetch, VillaStep.WritHandIn }, boundaryFetches: 2));
        Assert.Equal(VillaStep.None, spent.Step); // one fetch a walkable cave a town trip
        Assert.Contains(spent.Lines, l => l.StartsWith("villa: weigh writ-fetch: done-this-boundary:", StringComparison.Ordinal));

        var hour = VillaSchedule.Decide(Input(Monday.AddMinutes(65), timers: timers, opens: open));
        Assert.Equal(("minor-smoldering-stone", "open"), (hour.Cave!.QuestKey, hour.Token)); // the next town trip after the hour: its timer passed

        var held = VillaSchedule.Decide(Input(Monday.AddMinutes(50), timers: timers, opens: open));
        Assert.Equal(VillaStep.None, held.Step); // both caves on their hour
        Assert.Contains(held.Lines, l => l.StartsWith("villa: weigh writ-fetch: no-cave-due:", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_the_purchase_runs_as_soon_as_the_price_is_held_and_the_writs_stop_at_the_goal()
    {
        var open = VillaFacts.ClimberAccountOpensVillas;
        var buy = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(2_000_000, 0, 5, 1), opens: open));
        Assert.Equal((VillaStep.Purchase, "may"), (buy.Step, buy.Token));
        Assert.DoesNotContain(buy.Lines, l => l.Contains("too-early", StringComparison.Ordinal));

        var shortMoney = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(1_500_000, 0, 7, 1), opens: open));
        Assert.NotEqual(VillaStep.Purchase, shortMoney.Step); // the new climber's own 2 M first: her sales bring it
        Assert.Contains(shortMoney.Lines, l => l.StartsWith("villa: weigh villa-purchase: price-short:", StringComparison.Ordinal) && l.Contains("pyreals 500,000 short", StringComparison.Ordinal));
        Assert.Equal("enough", VillaSchedule.Pace(Input(Monday, held: new PriceHeld(1_500_000, 0, 7, 1), opens: open)).Token); // 7 writs held: no fetch

        Assert.StartsWith("on,gather,writs:7/7,", VillaSchedule.Marker(true, Input(Monday, held: new PriceHeld(1_500_000, 0, 7, 1), opens: open), null), StringComparison.Ordinal);
        Assert.StartsWith("on,buy,writs:5/7,", VillaSchedule.Marker(true, Input(Monday, held: new PriceHeld(2_000_000, 0, 5, 1), opens: open), null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_purchase_spec_carries_the_opening_it_was_weighed_with()
    {
        var open = Assert.IsType<VillaPurchaseStation>(VillaStations.Create(VillaStations.Spec(VillaStep.Purchase)));
        Assert.False(VillaStations.Spec(VillaStep.Purchase).Arguments.ContainsKey(VillaStations.OpensAtArgument)); // the default: at once
        var retail = VillaStations.Spec(VillaStep.Purchase, opensAt: VillaFacts.ClimberAccountOpensVillasRetail);
        Assert.Equal("2026-11-24T22:51:38.0000000+00:00", retail.Arguments[VillaStations.OpensAtArgument]);
        Assert.IsType<VillaPurchaseStation>(VillaStations.Create(retail));
        Assert.Same(VillaFacts.Block1401, open.Block);
    }

    [Fact]
    public void Before_the_purchase_the_writs_wanted_are_the_price_and_the_first_rent_by_the_opening()
    {
        var (goal, target, deadline) = VillaSchedule.WritGoal(Input(Monday));
        Assert.Equal(("purchase+rent", 7, Opens), (goal, target, deadline)); // 5 for the price + 2 for the first period (the guardian 13080's rows)

        var owed = VillaSchedule.WritGoal(Input(Monday, HouseOwnership.Owned, rentPaid: false, rentDue: Opens.AddDays(30).ToUnixTimeSeconds(), owed: [(VillaFacts.WritWcid, "Writs of Refuge", 1)]));
        Assert.Equal(("rent", 1, Opens.AddDays(30)), owed);

        var paid = VillaSchedule.WritGoal(Input(Monday, HouseOwnership.Owned, rentPaid: true, rentDue: Opens.AddDays(30).ToUnixTimeSeconds()));
        Assert.Equal(("next-rent", 2, Opens.AddDays(60)), paid); // the next period's writs, by its end
    }

    [Fact]
    public void The_pace_spreads_the_fourteen_stones_over_the_time_left_and_fetches_only_when_the_spacing_has_passed()
    {
        var timers = new QuestTimers("villa-pace");
        var first = VillaSchedule.Pace(Input(Monday, timers: timers));
        Assert.Equal(("first", true, 14), (first.Token, first.Due, first.StonesToFetch)); // no fetch recorded yet
        Assert.Equal(172.86, first.WindowHours, 2); // 11-16 18:00 to 11-24 22:51:38 less the day's margin
        Assert.Equal(7.0, first.FastestHours, 3); // two walkable caves, one stone an hour each: ceil(14 / 2) x 1 h
        Assert.Equal(172.86 / 14, first.IntervalHours, 2); // about 12.3 h between fetches

        timers.Complete(QuestCatalog.Get("minor-smoldering-stone"), Monday, QuestTimers.HowItem);
        var soon = VillaSchedule.Pace(Input(Monday.AddHours(2), stones: 1, timers: timers));
        Assert.Equal(("ahead", false, 13), (soon.Token, soon.Due, soon.StonesToFetch)); // the hunt keeps the hours between: 2 h since the last, the spacing 13.1 h
        Assert.Contains("the next in", soon.Why, StringComparison.Ordinal);

        var later = VillaSchedule.Pace(Input(Monday.AddHours(13), stones: 1, timers: timers));
        Assert.Equal(("on-pace", true), (later.Token, later.Due)); // 13 h since the last, the spacing 12.3 h
    }

    [Fact]
    public void Close_to_the_opening_the_pace_is_behind_and_every_boundary_fetches()
    {
        var timers = new QuestTimers("villa-behind");
        timers.Complete(QuestCatalog.Get("minor-sparking-stone"), Opens.AddHours(-31), QuestTimers.HowItem);
        var room = VillaSchedule.Pace(Input(Opens.AddHours(-29.5), held: new PriceHeld(11_000_000, 0, 3, 1), timers: timers));
        Assert.Equal(("on-pace", true, 8), (room.Token, room.Due, room.StonesToFetch)); // 4 writs short: 8 stones; 5.5 h of window over the fastest 4 h; the spacing 0.69 h, 1.5 h since the last
        Assert.Equal(4.0, room.FastestHours, 3);

        var pace = VillaSchedule.Pace(Input(Opens.AddHours(-27), held: new PriceHeld(11_000_000, 0, 3, 1), timers: timers));
        Assert.Equal(("behind", true), (pace.Token, pace.Due)); // 3 h of window, no more than the fastest 4 h
        Assert.Equal(3.0, pace.WindowHours, 3);

        var after = VillaSchedule.Pace(Input(Opens.AddHours(1), held: new PriceHeld(11_000_000, 0, 6, 1), timers: timers));
        Assert.Equal(("open", true, 2), (after.Token, after.Due, after.StonesToFetch)); // past the opening and short: the purchase is open, a fetch from each due cave at every boundary
        Assert.Equal(0, after.WindowHours); // no window: wanted now
    }

    [Fact]
    public void With_the_writs_held_no_fetch_is_due_and_without_a_walkable_cave_none_can_be()
    {
        var enough = VillaSchedule.Pace(Input(Monday, held: new PriceHeld(11_000_000, 0, 7, 1)));
        Assert.Equal(("enough", false, 0), (enough.Token, enough.Due, enough.StonesToFetch));

        var walled = Input(Monday) with { Caves = [.. WritFacts.Caves.Where(c => c.RouteTodo is not null)] };
        var none = VillaSchedule.Pace(walled);
        Assert.Equal(("no-cave", false), (none.Token, none.Due));
    }

    [Fact]
    public void The_fetch_goes_to_the_first_walkable_cave_due_by_its_timer()
    {
        var timers = new QuestTimers("villa-fetch");
        var call = VillaSchedule.Decide(Input(Monday, timers: timers));
        Assert.Equal(VillaStep.WritFetch, call.Step);
        Assert.Equal("minor-smoldering-stone", call.Cave!.QuestKey);
        Assert.Contains(call.Lines, l => l.StartsWith("villa: weigh villa-purchase: too-early:", StringComparison.Ordinal));
        Assert.Contains(call.Lines, l => l.StartsWith("villa: weigh lockpick-buy: held:", StringComparison.Ordinal));
        Assert.Contains(call.Lines, l => l.StartsWith("villa: weigh writ-hand-in: no-stones:", StringComparison.Ordinal));

        timers.Complete(QuestCatalog.Get("minor-smoldering-stone"), Monday.AddMinutes(-20), QuestTimers.HowItem);
        timers.Complete(QuestCatalog.Get("minor-sparking-stone"), Monday.AddMinutes(-15), QuestTimers.HowItem);
        var ahead = VillaSchedule.Decide(Input(Monday.AddMinutes(30), stones: 0, timers: timers));
        Assert.Equal(VillaStep.None, ahead.Step); // 0.75 h since the last fetch, the spacing about 12.4 h
        Assert.Contains(ahead.Lines, l => l.StartsWith("villa: weigh writ-fetch: ahead:", StringComparison.Ordinal));

        var late = new QuestTimers("villa-late");
        late.Complete(QuestCatalog.Get("minor-smoldering-stone"), Opens.AddMinutes(100), QuestTimers.HowItem);
        late.Complete(QuestCatalog.Get("minor-sparking-stone"), Opens.AddMinutes(110), QuestTimers.HowItem);
        var held = VillaSchedule.Decide(Input(Opens.AddHours(2), held: new PriceHeld(11_000_000, 0, 4, 1), timers: late));
        Assert.Equal(VillaStep.None, held.Step); // behind, but both caves' timers hold them for the hour
        Assert.Contains(held.Lines, l => l.StartsWith("villa: weigh writ-fetch: no-cave-due:", StringComparison.Ordinal) && l.Contains("minor-smoldering-stone due", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_stones_or_one_with_a_half_pair_go_to_the_Agent_before_any_fetch()
    {
        Assert.Equal(VillaStep.WritHandIn, VillaSchedule.Decide(Input(Monday, stones: 2)).Step);
        Assert.Equal(VillaStep.WritHandIn, VillaSchedule.Decide(Input(Monday, stones: 1, half: true)).Step);
        var lone = VillaSchedule.Decide(Input(Monday, stones: 1));
        Assert.Equal(VillaStep.WritFetch, lone.Step); // the lone stone is kept; the pace asks the next
        Assert.Contains(lone.Lines, l => l.StartsWith("villa: weigh writ-hand-in: one-stone-kept:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_lockpick_is_bought_once_while_no_house_is_owned_and_none_is_held()
    {
        var call = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(11_000_000, 0, 0, 0)));
        Assert.Equal(VillaStep.LockpickBuy, call.Step);
        Assert.Equal("due", call.Token);
        Assert.NotEqual(VillaStep.LockpickBuy, VillaSchedule.Decide(Input(Monday, held: new PriceHeld(11_000_000, 0, 0, 0), done: new HashSet<VillaStep> { VillaStep.LockpickBuy })).Step);
        Assert.NotEqual(VillaStep.LockpickBuy, VillaSchedule.Decide(Input(Monday, held: new PriceHeld(11_000_000, 0, 0, 0), failed: new HashSet<VillaStep> { VillaStep.LockpickBuy })).Step);
        Assert.NotEqual(VillaStep.LockpickBuy, VillaSchedule.Decide(Input(Monday, HouseOwnership.Owned, held: new PriceHeld(11_000_000, 0, 0, 0), rentDue: Opens.AddDays(30).ToUnixTimeSeconds())).Step);
        var poor = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(100, 0, 0, 0), coins: 100));
        Assert.Contains(poor.Lines, l => l.StartsWith("villa: weigh lockpick-buy: purse-short:", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_16_E5a2_the_lockpick_step_starts_on_a_note_when_the_coin_is_short()
    {
        // 100 coin and a 10,000 note the Merchant's counter can change: before the slice the step read the coin alone and never started (the test above), though the counter would pay.
        var noted = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(100, 10_000, 0, 0), coins: 100) with { LockpickNotes = 10_000, LockpickNotesSay = "the trade notes held (10000) are counted with the coin" });
        Assert.Equal((VillaStep.LockpickBuy, "due"), (noted.Step, noted.Token));
        Assert.Contains(noted.Lines, l => l.StartsWith("villa: weigh lockpick-buy: due: no Crude Lockpick held; one at 233: the coin 100 is short by 133 and the trade notes held (10000) cover it, so a note is changed at the Merchant's counter; the trade notes held (10000) are counted with the coin", StringComparison.Ordinal));

        // The same pack at a vendor known to take no note: the caller counts none, and the step waits for coin, said.
        var coinOnly = VillaSchedule.Decide(Input(Monday, held: new PriceHeld(100, 10_000, 0, 0), coins: 100) with { LockpickNotes = 0, LockpickNotesSay = "his mask takes no trade note" });
        Assert.NotEqual(VillaStep.LockpickBuy, coinOnly.Step);
        Assert.Contains(coinOnly.Lines, l => l.StartsWith("villa: weigh lockpick-buy: purse-short: the coin 100 does not cover one Crude Lockpick at 233; his mask takes no trade note", StringComparison.Ordinal));

        // An unread coin is unread whatever the notes.
        Assert.Contains(VillaSchedule.Decide(Input(Monday, held: new PriceHeld(0, 10_000, 0, 0), coins: -1) with { LockpickNotes = 10_000 }).Lines, l => l.StartsWith("villa: weigh lockpick-buy: purse-unread:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_purchase_waits_for_the_opening_the_price_and_no_house_then_runs_first()
    {
        var price = new PriceHeld(2_000_000, 0, 7, 1);
        var early = VillaSchedule.Decide(Input(Opens.AddSeconds(-1), held: price));
        Assert.NotEqual(VillaStep.Purchase, early.Step);
        Assert.Contains(early.Lines, l => l.StartsWith("villa: weigh villa-purchase: too-early:", StringComparison.Ordinal));

        var open = VillaSchedule.Decide(Input(Opens, held: price, stones: 2));
        Assert.Equal((VillaStep.Purchase, "may"), (open.Step, open.Token)); // before the hand-in the stones would make

        var notes = VillaSchedule.Decide(Input(Opens, held: new PriceHeld(150_000, 1_900_000, 5, 1)));
        Assert.Equal(VillaStep.Purchase, notes.Step); // trade notes pay the money line

        var shortWrits = VillaSchedule.Decide(Input(Opens, held: new PriceHeld(2_000_000, 0, 4, 1)));
        Assert.NotEqual(VillaStep.Purchase, shortWrits.Step);
        Assert.Contains(shortWrits.Lines, l => l.Contains("price-short", StringComparison.Ordinal) && l.Contains("Writ of Refuge 1 short (4 of 5)", StringComparison.Ordinal));

        var noPick = VillaSchedule.Decide(Input(Opens, held: new PriceHeld(2_000_000, 0, 5, 0)));
        Assert.Equal(VillaStep.LockpickBuy, noPick.Step); // the lockpick first; the purchase is weighed again after it
        Assert.Equal(VillaStep.Purchase, VillaSchedule.Decide(Input(Opens, held: new PriceHeld(2_000_000, 0, 5, 1), done: new HashSet<VillaStep> { VillaStep.LockpickBuy })).Step);

        var low = VillaSchedule.Decide(Input(Opens, held: price, level: 34));
        Assert.Contains(low.Lines, l => l.StartsWith("villa: weigh villa-purchase: below-level:", StringComparison.Ordinal));

        var owned = VillaSchedule.Decide(Input(Opens, HouseOwnership.Owned, held: price, rentDue: Opens.AddDays(30).ToUnixTimeSeconds()));
        Assert.Contains(owned.Lines, l => l.StartsWith("villa: weigh villa-purchase: owns-house:", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_weighed_while_the_house_status_is_unread()
    {
        var call = VillaSchedule.Decide(Input(Opens, HouseOwnership.Unknown, held: new PriceHeld(2_000_000, 0, 7, 1), stones: 4));
        Assert.Equal((VillaStep.None, "status-unread"), (call.Step, call.Token));
    }

    [Fact]
    public void The_rent_is_due_within_three_days_of_the_period_end_and_late_at_once()
    {
        var due = Opens.AddDays(30);
        var owed = new List<(uint, string, long)> { (VillaFacts.PyrealWcid, "Pyreals", 100_000), (VillaFacts.WritWcid, "Writs of Refuge", 2) };
        var held = new PriceHeld(5_000_000, 0, 2, 0);
        var early = VillaSchedule.Decide(Input(due.AddDays(-4), HouseOwnership.Owned, held: held, rentPaid: false, rentDue: due.ToUnixTimeSeconds(), owed: owed));
        Assert.NotEqual(VillaStep.Rent, early.Step);
        Assert.Contains(early.Lines, l => l.StartsWith("villa: weigh villa-rent: not-yet:", StringComparison.Ordinal));

        var within = VillaSchedule.Decide(Input(due.AddDays(-2), HouseOwnership.Owned, held: held, rentPaid: false, rentDue: due.ToUnixTimeSeconds(), owed: owed));
        Assert.Equal((VillaStep.Rent, "due"), (within.Step, within.Token));

        var late = VillaSchedule.Decide(Input(due.AddHours(1), HouseOwnership.Owned, held: held, rentPaid: false, rentDue: due.ToUnixTimeSeconds(), owed: owed));
        Assert.Equal((VillaStep.Rent, "late"), (late.Step, late.Token));

        var shortWrits = VillaSchedule.Decide(Input(due.AddDays(-2), HouseOwnership.Owned, held: new PriceHeld(5_000_000, 0, 1, 0), rentPaid: false, rentDue: due.ToUnixTimeSeconds(), owed: owed));
        Assert.Equal((VillaStep.WritFetch, "first"), (shortWrits.Step, shortWrits.Token)); // the rent waits for its writ (rent-short); one writ is two stones, and none was fetched yet
        Assert.Contains(shortWrits.Lines, l => l.StartsWith("villa: weigh villa-rent: rent-short:", StringComparison.Ordinal));
        Assert.Equal("behind", VillaSchedule.Decide(Input(due.AddHours(-24.5), HouseOwnership.Owned, held: new PriceHeld(5_000_000, 0, 1, 0), rentPaid: false, rentDue: due.ToUnixTimeSeconds(), owed: owed)).Token); // half an hour of window, the fastest 1 h

        var paid = VillaSchedule.Decide(Input(due.AddDays(-2), HouseOwnership.Owned, held: held, rentPaid: true, rentDue: due.ToUnixTimeSeconds()));
        Assert.Contains(paid.Lines, l => l.StartsWith("villa: weigh villa-rent: paid:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_storage_trip_runs_with_a_house_and_finds_to_store_and_waits_by_name_for_the_portal_walk()
    {
        var house = Opens.AddDays(30).ToUnixTimeSeconds();
        var held = new PriceHeld(5_000_000, 0, 2, 0);
        Assert.Equal((VillaStep.Storage, "review-full"), Pick(VillaSchedule.Decide(Input(Opens.AddDays(1), HouseOwnership.Owned, held: held, rentDue: house, store: 3, reviewFree: 0))));
        Assert.Equal((VillaStep.Storage, "town-trip"), Pick(VillaSchedule.Decide(Input(Opens.AddDays(1), HouseOwnership.Owned, held: held, rentDue: house, store: 1, reviewFree: 9))));

        var nothing = VillaSchedule.Decide(Input(Opens.AddDays(1), HouseOwnership.Owned, held: held, rentDue: house, store: 0));
        Assert.Equal(VillaStep.None, nothing.Step);
        Assert.Contains(nothing.Lines, l => l.StartsWith("villa: weigh villa-storage: nothing-to-store:", StringComparison.Ordinal));

        var walled = VillaSchedule.Decide(Input(Opens.AddDays(1), HouseOwnership.Owned, held: held, rentDue: house, store: 4, walk: false));
        Assert.Equal(VillaStep.None, walled.Step);
        Assert.Contains(walled.Lines, l => l.StartsWith("villa: weigh villa-storage: walk-missing:", StringComparison.Ordinal) && l.Contains("TODO(villa-house-portal-walk)", StringComparison.Ordinal));
        Assert.False(StorageTripStation.HousePortalWalkEstablished); // the live flag stands until the walk is established

        var noHouse = VillaSchedule.Decide(Input(Monday, store: 4, held: new PriceHeld(11_000_000, 0, 7, 1)));
        Assert.Contains(noHouse.Lines, l => l.StartsWith("villa: weigh villa-storage: no-house:", StringComparison.Ordinal));
        static (VillaStep, string) Pick(VillaCall c) => (c.Step, c.Token);
    }

    [Fact]
    public void The_store_list_takes_unworn_great_pieces_and_kept_aside_finds()
    {
        var list = VillaSchedule.StoreList([(1u, false, true, false), (2u, true, true, false), (3u, false, false, true), (4u, false, false, false), (1u, false, true, false)]);
        Assert.Equal([1u, 3u], list);
    }

    [Fact]
    public void The_cycle_marker_names_the_phase_the_writs_and_the_money_trend()
    {
        Assert.Equal("off", VillaSchedule.Marker(false, Input(Monday), 5));
        Assert.Equal("on,unread", VillaSchedule.Marker(true, null, null));
        Assert.Equal("on,gather,writs:3/7,stones:1,half:yes,pyreals:11800000/2000000,per_h:+250000", VillaSchedule.Marker(true, Input(Monday, held: new PriceHeld(11_000_000, 800_000, 3, 1), stones: 1, half: true), 250_000));
        Assert.Equal("on,buy,writs:7/7,stones:0,half:no,pyreals:11000000/2000000,per_h:n/a", VillaSchedule.Marker(true, Input(Opens, held: new PriceHeld(11_000_000, 0, 7, 1)), null));
        Assert.Equal("on,unread,writs:0/7,stones:0,half:no,pyreals:11000000/2000000,per_h:-10", VillaSchedule.Marker(true, Input(Monday, HouseOwnership.Unknown), -10));
        Assert.Contains("on,owned,", VillaSchedule.Marker(true, Input(Opens.AddDays(1), HouseOwnership.Owned, rentDue: Opens.AddDays(30).ToUnixTimeSeconds()), null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_villa_station_kind_makes_each_step_from_its_spec()
    {
        Assert.IsType<WritHandInStation>(VillaStations.Create(VillaStations.Spec(VillaStep.WritHandIn, halfPending: true)));
        Assert.Equal("yes", VillaStations.Spec(VillaStep.WritHandIn, halfPending: true).Arguments[VillaStations.HalfPendingArgument]);
        Assert.IsType<LockpickBuyStation>(VillaStations.Create(VillaStations.Spec(VillaStep.LockpickBuy)));
        var purchase = Assert.IsType<VillaPurchaseStation>(VillaStations.Create(VillaStations.Spec(VillaStep.Purchase)));
        Assert.Same(VillaFacts.Block1401, purchase.Block);
        var rent = Assert.IsType<RentStation>(VillaStations.Create(VillaStations.Spec(VillaStep.Rent)));
        Assert.Equal(VillaSchedule.RentMargin, rent.Margin);
        var storage = Assert.IsType<StorageTripStation>(VillaStations.Create(VillaStations.Spec(VillaStep.Storage, items: [0x80001234u, 0x8000ABCDu])));
        Assert.Equal([0x80001234u, 0x8000ABCDu], storage.Items);
        Assert.Equal(StationKinds.Villa, VillaStations.Spec(VillaStep.Rent).Kind);
        Assert.Throws<ArgumentException>(() => VillaStations.Spec(VillaStep.WritFetch));
        Assert.Contains("TODO(villa-station-step)", Assert.Throws<NotSupportedException>(() => VillaStations.Create(new StationSpec("x", StationKinds.Villa, null, 1, new Dictionary<string, string> { [VillaStations.StepArgument] = "nope" }))).Message, StringComparison.Ordinal);
        Assert.IsType<RentStation>(JourneyStations.Create(JourneyPlans.Mage, [], null).Create(VillaStations.Spec(VillaStep.Rent))); // the journey's factory knows the kind
    }

    [Fact]
    public void The_sale_keeps_the_writs_and_the_lockpick()
    {
        Assert.Contains(VillaFacts.WritWcid, ResupplyRun.QuestItemWcids);
        Assert.Contains(VillaFacts.LockpickWcid, ResupplyRun.QuestItemWcids);
        Assert.All(WritFacts.StoneWcids, w => Assert.Contains(w, ResupplyRun.QuestItemWcids)); // the stones are their files' done items
    }
}
