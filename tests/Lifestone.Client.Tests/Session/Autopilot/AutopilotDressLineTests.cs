using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 2 e, "the climber's dress line": <c>casters=</c> on the autopilot's cycle and stop lines (a state read of <see cref="GearFloor.CastersHeld"/>, not a summed tally), the audit's <c>no-caster</c> stop beside the caster audit, the audit's
/// trip when the gear floor is short (<see cref="StartupAudit.ReadFloor"/>, <see cref="KitReading.TripDue"/>), over the scripted ACE of <see cref="AutopilotRig"/> and the profiles' vendors. Report-only in the first gate (D27): no judge reads
/// <c>casters=</c> yet.
/// </summary>
public sealed class AutopilotDressLineTests(ITestOutputHelper output)
{
    private const uint CasterType = 0x8000;
    private const uint Held = 0x01000000;
    private const uint Wand = 31336;

    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    private static void Wield(ApWorld w, string name)
    {
        var caster = w.Trip.Own(name, Wand, CasterType, 100);
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, caster, (int)Held));
        w.Trip.Vendor.R.Tick();
        Assert.Equal(caster, w.Trip.Vendor.State.Self.Inventory.EquippedAt(Held));
    }

    [Fact]
    public void The_cycle_line_and_the_stop_line_read_casters_from_the_state_one_wielded_and_one_in_the_pack()
    {
        var w = new ApWorld(Den, output);
        Wield(w, "Fire Sceptre");
        w.Trip.Own("Spare Wand", Wand, CasterType, 100);
        Assert.Equal(2, GearFloor.CastersHeld(w.Trip.View.State));
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 3, TestDeath = true });

        w.Run(autopilot, a => a.Cycles == 2 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        var lines = autopilot.Lines;
        Assert.StartsWith("autopilot cycle n=1 state=death ", lines[0], StringComparison.Ordinal);
        Assert.Equal("2", Field(lines[0], "casters")); // the cycle line
        Assert.StartsWith("autopilot cycle n=3 state=stopped ", lines[2], StringComparison.Ordinal);
        Assert.Equal("2", Field(lines[2], "casters")); // the stop line
        Assert.Contains(" casters=2 s=", lines[0], StringComparison.Ordinal); // between the tally and s=
        Assert.DoesNotContain("casters=", lines[3], StringComparison.Ordinal); // the done line sums the tally: casters is a state read, not a field of it
        Assert.DoesNotContain("casters", AutopilotTally.FieldNames);
    }

    [Fact]
    public void The_cycle_line_reads_no_casters_as_zero_when_the_pack_is_read_and_holds_none()
    {
        var w = new ApWorld(Den, output);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 3, TestDeath = true });

        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Equal("0", Field(autopilot.Lines[0], "casters"));
    }

    /// <summary>A den world at <paramref name="level"/> with <paramref name="coins"/> pyreals (the coin first: the private-update sequence of the level is newer than the rig's coin update, which a later one would lose to).</summary>
    private ApWorld World(int level, int coins = 300_000, bool atHome = true, bool notes = true)
    {
        var w = new ApWorld(Den, output, atHome: atHome) { Prime = c => Proven(c, Den) };
        w.Trip.Vendor.Coins(coins);
        w.Trip.Vendor.R.Tick();
        if (!notes)
        {
            // the den's purse holds trade notes (the profile's): the face of a note counts in the floor's purse, so a short purse is one with the notes gone too
            var state = w.Trip.Vendor.State;
            foreach (var entry in state.Self.Inventory.Pack.ToList())
            {
                if (state.Objects.TryGet(entry.Guid, out var o) && (o.ItemType & 0x40000) != 0)
                {
                    w.Trip.Vendor.Remove(entry.Guid);
                }
            }

            w.Trip.Vendor.R.Tick();
        }

        w.Level(level);
        return w;
    }

    [Fact]
    public void The_floor_reading_is_short_and_buyable_with_the_purse_stopped_without_it_and_silent_under_the_floor_level_or_with_two_casters()
    {
        var w = World(5);
        var floor = StartupAudit.ReadFloor(w.Trip.View.State, w.Ground);
        Assert.Null(floor.Stop);
        Assert.Contains("0 caster(s) held, 2 wanted: 2 x Wand at Archmage Cindrue for 270", floor.Short, StringComparison.Ordinal);

        w = World(5, coins: 100, notes: false); // the purse cannot pay a 270 Wand
        floor = StartupAudit.ReadFloor(w.Trip.View.State, w.Ground);
        Assert.Null(floor.Short);
        Assert.StartsWith("no-caster: ", floor.Stop, StringComparison.Ordinal);

        w = World(4); // under the floor level (the ladder's start rung's move_on.level, 5)
        Assert.Equal((null, null, null), StartupAudit.ReadFloor(w.Trip.View.State, w.Ground));

        w = World(5);
        w.Trip.Own("Wand", Wand, CasterType, 100);
        w.Trip.Own("Spare Wand", Wand, CasterType, 100);
        Assert.Equal((null, null, null), StartupAudit.ReadFloor(w.Trip.View.State, w.Ground));
    }

    [Fact]
    public void A_ground_that_lists_no_vendor_is_not_audited_a_named_note_and_no_stop()
    {
        var w = World(5);
        var belt = GroundCatalog.Get("low-b-belt");
        Assert.Empty(belt.Vendors);

        var floor = StartupAudit.ReadFloor(w.Trip.View.State, belt);

        Assert.Null(floor.Stop);
        Assert.Null(floor.Short);
        Assert.Contains("TODO(m6-floor-vendorless-ground)", floor.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kit_check_calls_the_trip_when_only_the_floor_is_short_and_not_with_two_casters()
    {
        var w = World(5);
        var counters = new LogisticsCounters(w.Ground, []);

        var kit = StartupAudit.ReadKit(w.Trip.View.State, w.Ground, counters);
        Assert.False(kit.Unread);
        Assert.Empty(kit.Short);
        Assert.False(kit.Verdict.IsTrip);
        Assert.NotNull(kit.FloorShort);
        Assert.True(kit.TripDue);

        w.Trip.Own("Wand", Wand, CasterType, 100);
        w.Trip.Own("Spare Wand", Wand, CasterType, 100);
        kit = StartupAudit.ReadKit(w.Trip.View.State, w.Ground, counters);
        Assert.Null(kit.FloorShort);
        Assert.False(kit.TripDue);
    }

    [Fact]
    public void The_audit_stops_no_caster_when_under_two_casters_and_the_purse_cannot_buy_the_second()
    {
        var w = World(5, coins: 100, atHome: false, notes: false);
        w.Trip.Own("Wand", Wand, CasterType, 100); // one caster, in the pack
        var autopilot = w.Autopilot(new AutopilotOptions());

        var status = w.Run(autopilot, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(GearFloor.NoCaster, autopilot.StopName);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit caster: 1 caster(s) held, 2 wanted: no-caster: ", StringComparison.Ordinal) && l.EndsWith("fix=stop: no-caster", StringComparison.Ordinal));
        Assert.Contains(autopilot.Lines, l => l.StartsWith("autopilot done ok=no ", StringComparison.Ordinal) && l.Contains(" stop=no-caster ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind == StationKinds.Logistics); // a stop, not a trip
    }

    [Fact]
    public void The_audit_makes_a_forced_trip_for_a_short_floor_that_can_be_bought_and_says_what_it_leaves_with_loot_off()
    {
        var w = World(5, atHome: false);
        w.Trip.Own("Wand", Wand, CasterType, 100); // one caster in the pack: under two, the purse (300,000) pays the second
        var autopilot = w.Autopilot(new AutopilotOptions());

        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerWieldsAndModes(w));

        var trip = Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.Equal(JourneyPlans.TripForced, trip.Arguments[JourneyPlans.TripArgument]); // only the floor is short: the counters would skip an unforced trip
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit floor: gear floor: 1 caster(s) held, 2 wanted: 1 x Wand at Archmage Cindrue for 270; fix=a trip to ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit kit: short: the gear floor is short (", StringComparison.Ordinal) && l.Contains("forced: only the gear floor is short", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit kit: still short after the audit's trip: ", StringComparison.Ordinal) && l.Contains("TODO(m6-floor-needs-loot)", StringComparison.Ordinal)); // the rig's loot is off
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot audit caster: ", StringComparison.Ordinal) && l.Contains("fix=stop", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_2_f_the_loot_setting_reaches_the_autopilot_from_the_fight_options()
    {
        Assert.False(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll([])).LootOn); // --fight-loot defaults off (OptionCatalog.Fight)
        Assert.True(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-loot=on"])).LootOn);
        Assert.True(new AutopilotOptions().LootOn); // a hand-made options object assumes the floor is buyable unless told loot is off
    }

    [Fact]
    public void M6_2_f_with_loot_off_a_buyable_short_floor_is_a_named_note_and_no_trip_is_due()
    {
        var w = World(5);
        w.Trip.Own("Wand", Wand, CasterType, 100); // one caster: under two, and the purse pays the second
        var floor = StartupAudit.ReadFloor(w.Trip.View.State, w.Ground, lootOn: false);
        Assert.Null(floor.Short);
        Assert.Null(floor.Stop);
        Assert.Contains("floor short, loot off: no trip", floor.Note, StringComparison.Ordinal);

        var kit = StartupAudit.ReadKit(w.Trip.View.State, w.Ground, new LogisticsCounters(w.Ground, []), lootOn: false);
        Assert.Null(kit.FloorShort);
        Assert.False(kit.TripDue);
        Assert.Contains("floor short, loot off: no trip", kit.FloorNote, StringComparison.Ordinal);

        Assert.NotNull(StartupAudit.ReadFloor(w.Trip.View.State, w.Ground, lootOn: true).Short); // loot on: the trip is due as before
        w = World(5, coins: 100, atHome: false, notes: false);
        w.Trip.Own("Wand", Wand, CasterType, 100);
        Assert.StartsWith("no-caster: ", StartupAudit.ReadFloor(w.Trip.View.State, w.Ground, lootOn: false).Stop, StringComparison.Ordinal); // a floor that cannot be bought is still the stop
    }

    [Fact]
    public void M6_2_f_the_audit_with_loot_off_forces_no_trip_for_a_short_floor_and_says_so()
    {
        var w = World(5, atHome: false);
        w.Trip.Own("Wand", Wand, CasterType, 100);
        var autopilot = w.Autopilot(new AutopilotOptions { LootOn = false });

        w.Run(autopilot, a => a.Audits >= 1, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.DoesNotContain(w.Factory.Made, s => s.Kind == StationKinds.Logistics); // the BSD Mage lane: no wasted trip that cannot buy
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit floor: gear floor: 1 caster(s) held, 2 wanted: 1 x Wand at Archmage Cindrue for 270; floor short, loot off: no trip", StringComparison.Ordinal));
    }
}
