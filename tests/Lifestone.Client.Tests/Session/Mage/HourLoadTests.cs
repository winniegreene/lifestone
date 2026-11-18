using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 hour load (the owner's ruling, 2026-11-16: when she is fighting somewhere she is comfortable, she should bring herself to 150% burden by the end of the hour by discarding loot that is less valuable per weight as time goes on; and the
/// correction the same day: "we're not married to something just because we picked it up", the swap on every ground). Proved here: the rising line (<see cref="CycleLoadLine"/>, <see cref="CycleLoad.LineFor"/>), the comfort rule and its latch
/// (<see cref="ComfortRule"/>, <see cref="CycleLoad.Apply"/>), the jump hold, the ramp's end at the buff bot's trip, the hour's trip (<see cref="CycleLoad.HourDue"/>, <see cref="LogisticsCause.Hour"/>), the swap
/// (<see cref="MageLootRules.Plan"/>: sell loot of a lower value per burden makes way, never heavier) and the options. The live numbers are ladder37's (Qwinnie, level 78, the Tusker Quarters, 2026-11-16: capacity 18,300; burden 14,592 at the
/// start of cycle 2, 03:23 UTC; the Celdon Leggings kept at 02:40:16 at value 10,996 burden 1,953).
/// </summary>
public sealed class HourLoadTests
{
    private const uint Armor = 0x2, Gem = 0x800, Jewelry = 0x8, Money = 0x40, Misc = 0x80;
    private const int Capacity = 18_300;
    private const double Hour = 3600;

    private static readonly CarriedLoot Leggings = new(21, "Celdon Leggings", 10_996, 1953), Platter = new(22, "Gold Platter", 3000, 300, "trade-goods"), Diamond = new(23, "Diamond", 5000, 10, "trade-goods");

    private static MageLootRules Rules(int line = 100) => new(new MageLootThresholds { LoadLinePercent = line });

    // ------------------------------------------------------------------ the rising line

    [Fact]
    public void The_line_rises_straight_in_time_from_the_start_burden_to_the_top_and_is_never_under_the_base_line()
    {
        // start 80% (14,592 of 18,300 is 79.7%), base 100, top 150, the hour's ramp
        Assert.Equal(100, CycleLoadLine.Percent(100, 79.7, 150, 0, Hour)); // the base line until the ramp passes it (under 100% ACE's load modifier is 1: holding her lower buys nothing)
        Assert.Equal(100, CycleLoadLine.Percent(100, 79.7, 150, 0.25 * Hour, Hour)); // 79.7 + 70.3 x 0.25 = 97.3
        Assert.Equal(114, CycleLoadLine.Percent(100, 79.7, 150, 0.5 * Hour, Hour)); // 79.7 + 35.15 = 114.85, rounded down: the loot never passes the line it names
        Assert.Equal(150, CycleLoadLine.Percent(100, 79.7, 150, Hour, Hour));
        Assert.Equal(150, CycleLoadLine.Percent(100, 79.7, 150, 2 * Hour, Hour)); // held at the top past the ramp
        Assert.Equal(150, CycleLoadLine.Percent(100, 160, 150, 0, Hour)); // a start over the top: the top
        Assert.Equal(0, CycleLoadLine.Percent(0, 79.7, 150, Hour, Hour)); // no limit stays no limit
        Assert.Equal(120, CycleLoadLine.Percent(120, 79.7, 150, 0, Hour)); // a ground's own higher base line is kept
    }

    [Fact]
    public void The_loot_is_given_the_rising_line_only_while_comfortable_and_never_on_a_ground_with_jumps()
    {
        var load = new CycleLoad();
        Assert.Equal(100, load.LineFor(100, hasJumps: false, 1000)); // no cycle begun (no autopilot): the base line

        load.Begin("tusker-quarters", clock: 0, burden: 14_592, Capacity, new CycleLoadSettings(), topPercent: 150);
        Assert.Equal(100, load.LineFor(100, false, 0.5 * Hour)); // not read yet: the base line
        Assert.Equal(ComfortKind.Comfortable, load.Apply(new ComfortVerdict(ComfortKind.Comfortable, "test")));
        Assert.Equal(114, load.LineFor(100, false, 0.5 * Hour));
        Assert.Equal(150, load.LineFor(100, false, Hour));
        Assert.Equal(100, load.LineFor(100, hasJumps: true, Hour)); // the pit-hold: never past the base line on a ground with jumps
        Assert.Equal(150, load.HighestLine);
        Assert.Equal("rose:150", load.Field);

        var off = new CycleLoad();
        off.Begin("tusker-quarters", 0, 14_592, Capacity, new CycleLoadSettings(On: false), 150);
        Assert.Equal(ComfortKind.Off, off.Apply(new ComfortVerdict(ComfortKind.Comfortable, "test")));
        Assert.Equal(100, off.LineFor(100, false, Hour)); // the option off: today's line
        Assert.Equal("off", off.Field);
    }

    // ------------------------------------------------------------------ comfortable or not

    /// <summary>
    /// The live read of ladder40 at 18:15:24 UTC (2026-11-16, the Tusker Quarters), which the heal comparison latched uncomfortable: peak 3.51 a second, mean 1.23, the heal she sustains 0.32 (it was 0.97 a minute before); her health
    /// 156 of 217 at 18:15:00 with the vitals policy's health floor 33 ("floors mana 105 health 33"), her lowest of the cycle so far.
    /// </summary>
    private static ComfortInput Quarters(bool floorRead = true) => new("tusker-quarters", GroundJumps: 0, WayHome: "recall-home", Trial: false, Approach: false, Retreats: 0, SurvivalLeaves: 0, Deaths: 0, RacesLost: 0,
        Health: new CycleHealth(floorRead, Lowest: 156, Max: 217, FloorAtLowest: floorRead ? 33u : 0u, Floor: 33, Breached: false, BreachHealth: 0, BreachFloor: 0), IncomingPeak: 3.51, IncomingMean: 1.23, HealPerSecond: 0.32, BasePercent: 100, TopPercent: 150);

    [Fact]
    public void The_Quarters_read_comfortable_from_the_fights_own_numbers_and_every_failing_check_names_itself_first()
    {
        // fight-sim matchup 1628 (2026-11-16): a Tusker Slave lands 100% of its melee on her at Melee Defense 112, 0.92 health a second each; six on her are 5.52 a second; her fastest heal, Drain Health Other I, 38.6 a second
        var comfortable = ComfortRule.Decide(Quarters());
        Assert.Equal(ComfortKind.Comfortable, comfortable.Kind);

        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { GroundJumps = 1 }).Kind); // the Abode's pit
        Assert.Contains("jump hold", ComfortRule.Decide(Quarters() with { GroundJumps = 1, Deaths = 1 }).Why, StringComparison.Ordinal); // the first failing check names it
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { TopPercent = 85, BasePercent = 85 }).Kind); // the Holding's line 85 is its burden trip line: nothing to rise to
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Quarters() with { WayHome = null }).Kind);
        Assert.Contains("TODO(m6-hour-load-route-jumps)", ComfortRule.Decide(Quarters() with { WayHome = "rung-change" }).Why, StringComparison.Ordinal); // home by portals: legs walked at the top's load
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Quarters() with { WayHome = "at-home" }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { Trial = true }).Kind);
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Quarters() with { Approach = true }).Kind); // a keeper's fight-back: the read is skipped
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { Retreats = 1 }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { SurvivalLeaves = 1 }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { Deaths = 1 }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Quarters() with { RacesLost = 1 }).Kind); // the guard found the race lost, paid recall or not
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Quarters(floorRead: false)).Kind); // the fight's policy has not worked its floor out yet

        // the heal and the incoming are logged, never judged: ladder40 18:14:24 (mean 0, heal 0.97) and 18:15:24 (mean 1.23, heal 0.32) both read comfortable now; so does ladder39 18:03 (peak 4.4, heal 1.61)
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Quarters() with { IncomingPeak = 0, IncomingMean = 0, HealPerSecond = 0.97 }).Kind);
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Quarters() with { IncomingPeak = 4.4, HealPerSecond = 1.61 }).Kind);
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Quarters() with { HealPerSecond = null }).Kind);

        var input = Quarters();
        Assert.Equal(
            "hour load: comfort read: ground=tusker-quarters verdict=comfortable in_force=comfortable jumps=0 way_home=recall-home trial=no approach=no deaths=0 retreats=0 survival_leaves=0 races_lost=0 lowest_health=156/217 floor_at_lowest=33 health_floor=33 incoming_peak=3.51/s incoming_mean=1.23/s heal=0.32/s base=100% top=150%: no jump, home by Lifestone Recall, no trial, no death, retreat, leave or losing race this cycle, and her lowest health 156 of 217 stayed over the vitals policy's health floor (33 then, 33 now)",
            ComfortRule.Line(in input, in comfortable, ComfortKind.Comfortable));
    }

    [Fact]
    public void Her_health_under_the_vitals_policys_floor_latches_the_cycle_and_the_emergency_lines_dips_do_not()
    {
        // ladder37 (the Quarters, 2026-11-16): 17 of 19 cycles had an emergency drain at 113 to 128 of 212 to 215 health (under the fight's 60% emergency line), the policy's floor just before each 32 to 68: comfortable
        var load = new CycleLoad();
        load.Begin("tusker-quarters", 0, 14_592, Capacity, new CycleLoadSettings(), 150);
        load.NoteHealth(217, 217);
        Assert.False(load.Health.FloorRead); // no floor worked out yet: the read is unread
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Quarters() with { Health = load.Health }).Kind);

        load.NoteHealthFloor(39); // 05:30:15 "floors mana 105 health 39"
        load.NoteHealth(113, 214); // 05:30:19 "emergency: Drain Health Other II ... at health 113/214"
        load.NoteHealth(173, 214);
        Assert.Equal(new CycleHealth(true, 113, 214, 39, 39, false, 0, 0), load.Health);
        Assert.Equal(ComfortKind.Comfortable, load.Apply(ComfortRule.Decide(Quarters() with { Health = load.Health })));

        // the floor rises with a burst (ladder37's highest, 114) while her health is under it: one burst from the end
        load.NoteHealthFloor(114);
        load.NoteHealth(110, 214);
        load.NoteHealth(200, 214); // healed back: the breach stands for the cycle
        Assert.True(load.Health.Breached);
        var breach = ComfortRule.Decide(Quarters() with { Health = load.Health });
        Assert.Equal("her health fell to 110 of 214, under the vitals policy's health floor 114 (the biggest hit and the burst over a bolt and a drain): one burst from the end", breach.Why);
        Assert.Equal(ComfortKind.Uncomfortable, load.Apply(breach));
        Assert.True(load.Latched);
        Assert.Equal(100, load.LineFor(100, false, Hour));

        load.NoteRaceLost();
        Assert.Equal(1, load.RacesLost);
        load.End();
        load.Begin("tusker-quarters", Hour, 14_000, Capacity, new CycleLoadSettings(), 150);
        Assert.Equal((false, false, 0), (load.Health.FloorRead, load.Health.Breached, load.RacesLost)); // a new cycle reads afresh
    }

    [Fact]
    public void An_uncomfortable_read_latches_the_base_line_for_the_rest_of_the_cycle_and_an_unread_one_keeps_the_verdict_in_force()
    {
        var load = new CycleLoad();
        load.Begin("tusker-quarters", 0, 14_592, Capacity, new CycleLoadSettings(), 150);
        Assert.Equal(ComfortKind.Unread, load.Apply(ComfortRule.Decide(Quarters(floorRead: false)))); // nothing decided yet: the base line
        Assert.Equal(100, load.LineFor(100, false, Hour));
        Assert.Equal(ComfortKind.Comfortable, load.Apply(ComfortRule.Decide(Quarters())));
        Assert.Equal(ComfortKind.Comfortable, load.Apply(ComfortRule.Decide(Quarters(floorRead: false)))); // unread: the verdict in force stands
        Assert.Equal(ComfortKind.Uncomfortable, load.Apply(ComfortRule.Decide(Quarters() with { Retreats = 1 })));
        Assert.True(load.Latched);
        Assert.Equal(ComfortKind.Uncomfortable, load.Apply(ComfortRule.Decide(Quarters()))); // latched: a comfortable read later in the cycle changes nothing
        Assert.Equal(100, load.LineFor(100, false, Hour));
        Assert.False(load.HourDue(Hour)); // no hour's trip on an uncomfortable cycle: the other triggers end it
        Assert.Equal("held", load.Field); // comfortable a while, but no corpse was planned while it was: the loot was never given the rising line

        load.End();
        load.Begin("tusker-quarters", Hour, 14_000, Capacity, new CycleLoadSettings(), 150);
        Assert.False(load.Latched); // a new cycle reads afresh
        Assert.Equal("held", load.Field);
    }

    // ------------------------------------------------------------------ the ramp's end and the hour's trip

    [Fact]
    public void The_ramp_ends_at_the_buff_bots_trip_when_it_comes_before_the_hour_and_the_hours_trip_is_due_at_the_hour()
    {
        var load = new CycleLoad();
        load.Begin("tusker-quarters", 0, 14_592, Capacity, new CycleLoadSettings(), 150);
        load.Apply(ComfortRule.Decide(Quarters()));

        // ladder37 03:16:48: "a line of 'all' has 410 s left (under 420)" after 37 min of fighting: read at minute 10 with 2,700 s left, the bot's trip is 2,280 s away (2,700 - 420): the ramp ends at minute 48
        load.RampEnds(600, 2700 - 420);
        Assert.Equal(2880, load.RampEndClock);
        Assert.Equal(150, load.LineFor(100, false, 2880)); // the pack is at the top when the bot's trip leaves
        load.RampEnds(600, 9000);
        Assert.Equal(Hour, load.RampEndClock); // never later than the hour

        Assert.False(load.HourDue(Hour - 1));
        Assert.True(load.HourDue(Hour));
        load.NoteHourCalled();
        Assert.False(load.HourDue(Hour + 60)); // once a cycle
        Assert.Equal("hour:150", load.Field);

        var verdict = LogisticsVerdict.HourUp(60, 60, 149, 150);
        Assert.True(verdict.IsTrip);
        Assert.Equal(("hour", LogisticsCause.Hour), (verdict.Trigger, verdict.Cause));
        Assert.Equal("the hour: 60 minute(s) fought on a comfortable ground, the cycle's target is 60; burden 149% of the capacity under the hour's line 150%: a trip sells what she carries (m6-hour-load)", verdict.Reason);

        var never = new CycleLoad();
        never.Begin("tusker-quarters", 0, 14_592, Capacity, new CycleLoadSettings(), 150);
        Assert.False(never.HourDue(2 * Hour)); // never comfortable this cycle: no hour's trip
    }

    // ------------------------------------------------------------------ the swap

    [Fact]
    public void A_better_find_over_the_line_takes_the_place_of_the_lowest_value_per_burden_sell_loot_and_a_worse_one_is_left()
    {
        var rules = Rules();
        var pea = new MageLootItem(11, "Silver Pea", Gem, 12_500, 10); // v/b 1,250
        var cheap = new MageLootItem(12, "Iron Leggings", Armor, 500, 400); // v/b 1.25: under all she carries

        var plan = rules.Plan("Corpse of Tusker Slave", "Qwinnie", [pea, cheap], burden: Capacity, Capacity, freeSlots: 40, sellLoot: [Platter, Diamond, Leggings]);

        Assert.Equal([21u], plan.Drops.Select(d => d.Loot.Guid)); // the leggings' v/b 5.63 is the lowest: 1,953 freed for the pea's 10
        Assert.True(plan.Drops[0].Swap);
        Assert.Equal([11u], plan.Kept);
        Assert.Equal([11u], plan.Swaps);
        Assert.Equal(MageLootVerdict.Keep, plan.VerdictOf(11));

        // the iron leggings would fit in the 1,943 the drop freed (18,300 - 1,953 + 10 + 400 = 16,757), but the room freed by dropping v/b 5.63 is never filled with v/b 1.25
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(12));
        Assert.Equal("its value per burden 1.25 is under the 5.63 of the sell loot this corpse's room drops leave on the ground: the room they freed is not filled with worse", Assert.Single(plan.SwapShorts).Why);

        var worse = rules.Plan("Corpse of Tusker Slave", "Qwinnie", [cheap], burden: Capacity, Capacity, freeSlots: 40, sellLoot: [Platter, Diamond, Leggings]);
        Assert.Empty(worse.Drops); // nothing she carries is worth less a burden than v/b 1.25
        Assert.Equal(MageLootVerdict.Load, worse.VerdictOf(12));
        var shortfall = Assert.Single(worse.SwapShorts);
        Assert.Equal("dropping all 0 sell loot item(s) she carries of a value per burden under its 1.25 (burden 0, value 0) cannot make room: burden 18300 + 400 against the load line 100% = 18300, 40 free slot(s) (the piece goes to the main pack)", shortfall.Why);
        Assert.Contains("loot: no swap for 'Iron Leggings' 0x0000000C (value 500, burden 400, v/b 1.25): ", worse.RoomLines().Single(), StringComparison.Ordinal);
        Assert.Contains(", over the load line, no swap could make room)", worse.DecisionLines().Single(), StringComparison.Ordinal);
        Assert.Contains(" no_swap=1", worse.Summary(), StringComparison.Ordinal);

        Assert.Equal(
            "loot: swap: take 'Silver Pea' 0x0000000B (value 12500, burden 10, v/b 1250): burden 18300 of 18300 (load line 100% = 18300), 40 free slot(s): drop 1 sell loot item(s) of a lower value per burden, burden 1953, value 10996, the lowest first: 'Celdon Leggings' 0x00000015 value 10996 burden 1953 v/b 5.63",
            plan.RoomLines().First());
        Assert.Equal(
            "'Celdon Leggings' 0x00000015 value 10996 burden 1953 v/b 5.63 (pack rule gear-loot: sell loot, the lowest value per burden she carries, under the find's 1250) to swap for the better sell loot 'Silver Pea' 0x0000000B (m6-hour-load)",
            plan.Drops[0].Reason);
        Assert.Contains("loot: kept Silver Pea value 12500 burden 10 v/b 1250 (floor 1, a swap: room made by dropping 1 sell loot item(s) of a lower value per burden)", plan.DecisionLines());
        Assert.Contains(" swaps=1 swap_dropped_value=10996", plan.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wear_upgrade_over_the_line_makes_room_by_the_same_swap_and_a_keep_aside_piece_keeps_its_own_rule()
    {
        // ladder37 02:51:22: "left Slippers value 10061 burden 90 v/b 111.79 (floor 1, over the load line, a wear upgrade ranked first (replace with))"
        var slippers = new MageLootItem(61, "Slippers", Armor, 10_061, 90, Gear: new SelfEquipChoice(SelfEquipKind.Replace, 61, "Slippers", 0, 0, "replaces the worn shoes"));
        var wand = new MageLootItem(62, "Acid Baton", 0x8000, 10_000, 50, KeepAside: "War Magic bonus +3%", Look: true);

        var plan = Rules().Plan("Corpse of Tusker Slave", "Qwinnie", [slippers, wand], burden: Capacity, Capacity, freeSlots: 40, sellLoot: [Platter, Diamond, Leggings]);

        Assert.True(slippers.IsWearUpgrade);
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(62)); // the wand (v/b 200) is planned first: kept for its War Magic bonus alone, it keeps its present rule: no room is made for it
        Assert.Equal([21u], plan.Drops.Select(d => d.Loot.Guid)); // the leggings (v/b 5.63) make way for the slippers (v/b 111.79)
        Assert.Equal([61u], plan.Swaps);
        Assert.Equal(MageLootVerdict.Keep, plan.VerdictOf(61));
    }

    /// <summary>
    /// M6 14 (live ladder42 19:51:19 UTC): 0 of 102 slots free at 20,789 of 19,200 burden, the hour's line 136% (26,112): the burden had room, the slots did not, and the swap's value-per-burden test found nothing under a Frost Elemental
    /// Essence's 80. Where the slots bind and the burden does not, a slot is worth its item's value: the lowest value she carries under the find's makes way.
    /// </summary>
    [Fact]
    public void Where_the_slots_bind_and_the_burden_does_not_the_lowest_value_makes_way()
    {
        var essence = new MageLootItem(71, "Frost Elemental Essence (50)", Misc, 4000, 50); // v/b 80
        var scroll = new CarriedLoot(81, "Scroll of Dual Wield Mastery Self VI", 1000, 30, "scrolls"); // v/b 33.33: under 80 too, but the lowest value is what counts here
        var breastplate = new CarriedLoot(82, "Covenant Breastplate", 24_907, 1632); // v/b 15.26: the lowest v/b, the highest value
        var peridot = new CarriedLoot(83, "Peridot", 1288, 5, "trade-goods"); // v/b 257.6

        var plan = Rules(136).Plan("Corpse of Tusker Slave", "Qwinnie", [essence], burden: 20_789, 19_200, freeSlots: 0, sellLoot: [breastplate, peridot, scroll]);

        Assert.Equal([81u], plan.Drops.Select(d => d.Loot.Guid)); // the scroll's 1,000, not the breastplate's 24,907 (the burden had room: its weight is no cost)
        Assert.True(plan.Drops[0].BySlot);
        Assert.Equal([71u], plan.Kept);
        Assert.Equal(
            "'Scroll of Dual Wield Mastery Self VI' 0x00000051 value 1000 burden 30 v/b 33.33 (pack rule scrolls: sell loot, the lowest value she carries, under the find's 4000: the slots bind) to swap for the better sell loot 'Frost Elemental Essence (50)' 0x00000047 (m6-hour-load)",
            plan.Drops[0].Reason);
        Assert.Contains("of a lower value (the slots bind, the burden does not: a slot is worth its item's value)", plan.RoomLines().First(), StringComparison.Ordinal);

        // a find worth less than everything she carries is left
        var lamp = new MageLootItem(72, "Lockpick", Misc, 750, 50);
        var cheap = Rules(136).Plan("Corpse of Tusker Slave", "Qwinnie", [lamp], burden: 20_789, 19_200, freeSlots: 0, sellLoot: [breastplate, peridot, scroll]);
        Assert.Empty(cheap.Drops);
        Assert.Equal(MageLootVerdict.Slots, cheap.VerdictOf(72));
        Assert.Contains("of a value under its 750 (the slots bind: a slot is worth its item's value)", Assert.Single(cheap.SwapShorts).Why, StringComparison.Ordinal);

        // over the line as well as out of slots: the burden's rule (value per burden), whose drops free slots too
        var both = Rules(100).Plan("Corpse of Tusker Slave", "Qwinnie", [essence], burden: 20_789, 19_200, freeSlots: 0, sellLoot: [breastplate, peridot, scroll]);
        Assert.Equal([82u], both.Drops.Select(d => d.Loot.Guid));
        Assert.False(both.Drops[0].BySlot);
    }

    [Fact]
    public void The_swap_runs_at_every_load_and_under_the_jump_hold_but_never_makes_her_heavier_than_the_line_or_than_she_is()
    {
        // the owner's ruling (2026-11-16): even for the dungeons that have jumps, increasingly valuable and efficient loot can be picked up: the line stays at the jump hold, the pack's contents upgrade within it
        var jumpGround = Rules(MageLootThresholds.JumpCapped(150, hasJumps: true));
        Assert.Equal(100, jumpGround.Thresholds.LoadLinePercent);
        var ring = new MageLootItem(31, "Gold Ring", Jewelry, 6000, 30); // v/b 200
        var swapped = jumpGround.Plan("Corpse of Tusker Guard", "Qwinnie", [ring], burden: Capacity, Capacity, freeSlots: 40, sellLoot: [Platter, Leggings]);
        Assert.Equal([21u], swapped.Drops.Select(d => d.Loot.Guid)); // the lowest first: the leggings (5.63) before the platter (10)

        // over the line (the rising line fell back to the base line when the cycle latched uncomfortable at 120%): a swap only trades, it never sheds loot to get under the line
        var over = Rules().Plan("Corpse of Tusker Guard", "Qwinnie", [ring], burden: 21_960, Capacity, freeSlots: 40, sellLoot: [Platter, Leggings]);
        Assert.Equal([21u], over.Drops.Select(d => d.Loot.Guid)); // 21,960 - 1,953 + 30 = 20,037: lighter than she was, still over the line
        Assert.Equal([31u], over.Kept);
    }

    [Fact]
    public void Over_the_line_the_lowest_value_per_burden_still_goes_first_and_the_burden_after_is_no_more_than_before()
    {
        var heavyFind = new MageLootItem(41, "Silver Platter", Gem, 9000, 600); // v/b 15
        var cheapLight = new CarriedLoot(24, "Copper Bracelet", 50, 100); // v/b 0.5
        var cheapHeavy = new CarriedLoot(25, "Iron Breastplate", 1500, 1000); // v/b 1.5

        // at 21,960 (120%) with the base line 100%: freeing 100 is not enough for 600; freeing 1,100 is: 21,960 - 1,100 + 600 = 21,460, no heavier
        var plan = Rules().Plan("Corpse of Tusker Guard", "Qwinnie", [heavyFind], burden: 21_960, Capacity, freeSlots: 40, sellLoot: [cheapHeavy, cheapLight, Diamond]);
        Assert.Equal([24u, 25u], plan.Drops.Select(d => d.Loot.Guid));
        Assert.Equal([41u], plan.Kept);
        Assert.Empty(plan.Refit(burden: 21_960 - 1100, Capacity, freeSlots: 42, apply: false)); // the check after the drops holds it to the same bound

        // when the lower value per burden she carries cannot free the find's burden, nothing is dropped
        var tooHeavy = Rules().Plan("Corpse of Tusker Guard", "Qwinnie", [heavyFind with { Burden = 2000, Value = 30_000 }], burden: 21_960, Capacity, freeSlots: 40, sellLoot: [cheapHeavy, cheapLight, Diamond]);
        Assert.Empty(tooHeavy.Drops);
        Assert.Contains("(or the burden now, 21960: a swap never makes her heavier)", Assert.Single(tooHeavy.SwapShorts).Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rising_line_lets_the_plan_keep_what_the_base_line_would_leave_and_pyreals_and_great_cantrips_keep_their_rules()
    {
        var rules = Rules();
        var gem = new MageLootItem(51, "Black Opal", Gem, 2000, 5);
        var coins = new MageLootItem(52, "Pyreal", Money, 500, 0);
        var atTop = rules.Plan("Corpse of Tusker Slave", "Qwinnie", [gem, coins], burden: 20_000, Capacity, freeSlots: 40, loadLinePercent: 150);
        var atBase = rules.Plan("Corpse of Tusker Slave", "Qwinnie", [gem, coins], burden: 20_000, Capacity, freeSlots: 40);

        Assert.Equal([52u, 51u], atTop.Kept); // 20,000 + 5 is under 150% = 27,450
        Assert.Equal(150, atTop.LoadLinePercent);
        Assert.Contains("the hour's load line 150%", atTop.Describe(), StringComparison.Ordinal);
        Assert.Equal([52u], atBase.Kept); // the pyreals always; the gem over 100% with no sell loot to swap
        Assert.Equal(MageLootVerdict.Load, atBase.VerdictOf(51));
        Assert.Empty(atBase.SwapShorts); // no sell loot list given: no swap asked
    }

    // ------------------------------------------------------------------ the options

    [Fact]
    public void The_hour_load_options_default_on_60_minutes_and_150_percent_and_refuse_a_bad_value()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.Equal(new CycleLoadSettings(true, 60, 150), Parse().HourLoad);
        Assert.DoesNotContain("hour_load", Parse().ToString(), StringComparison.Ordinal);
        var set = Parse("--autopilot-hour-load-minutes=45", "--autopilot-hour-load-top=140");
        Assert.Equal(new CycleLoadSettings(true, 45, 140), set.HourLoad);
        Assert.Empty(set.Errors);
        Assert.Contains(" hour_load=45m/140%", set.ToString(), StringComparison.Ordinal);
        Assert.False(Parse("--autopilot-hour-load=off").HourLoad.On);
        Assert.Contains(" hour_load=off", Parse("--autopilot-hour-load=off").ToString(), StringComparison.Ordinal);
        Assert.Equal(3, Parse("--autopilot-hour-load-minutes=0", "--autopilot-hour-load-top=200", "--autopilot-hour-load=maybe").Errors.Count); // 200%: ACE's load modifier is 0 there
        Assert.Equal(2, OptionCatalog.Autopilot().Options.Count(o => o.Name.StartsWith("--autopilot-hour-load-", StringComparison.Ordinal)));
    }

    /// <summary>Live ladder39 2026-11-16 18:03 UTC: the cycle's incoming is judged by its mean over the samples (lulls count), the peak only logged.</summary>
    [Fact]
    public void The_cycles_incoming_mean_counts_every_sample_and_the_peak_is_kept_apart()
    {
        var load = new CycleLoad();
        load.Begin("tusker-quarters", 0, 9000, 18300, new CycleLoadSettings(), 150);
        foreach (var perSecond in new[] { 0.0, 4.4, 1.2, 0.0, double.NaN, -1.0, 1.3 })
        {
            load.NoteIncoming(perSecond);
        }

        Assert.Equal(4.4, load.IncomingPeak, 6);
        Assert.Equal((0.0 + 4.4 + 1.2 + 0.0 + 1.3) / 5, load.IncomingMean, 6);
        load.Begin("tusker-quarters", 100, 9000, 18300, new CycleLoadSettings(), 150);
        Assert.Equal(0, load.IncomingMean);
    }
}
