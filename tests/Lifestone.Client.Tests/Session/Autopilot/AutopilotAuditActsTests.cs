using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 6 MF, "the audit acts" (check, act, check, M5 D141) over the scripted ACE of <see cref="AutopilotRig"/>: the caster in the hands before the bind's Lifestone Tie (<c>m6-climber-start-weapon</c>); the settle's bound cancels the queued
/// automation actions and reports only the in-flight ones (<c>m6-chat-controls-robust</c>, the audit's half); the no-caster stop judged after the trip's sale (2-R12); the kit's caster and robe lines routed through the gear floor
/// (<c>m5-audit-kit-restock</c>); the kit's one re-ask before it is named unread (<c>m5-audit-kit-unread</c>).
/// </summary>
public sealed class AutopilotAuditActsTests(ITestOutputHelper output)
{
    private const uint CasterType = 0x8000;
    private const uint Held = 0x01000000;
    private const uint MeleeSlot = 0x00100000;
    private const uint Wand = 31336;
    private const uint GemType = 0x800;

    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    /// <summary>A den world at <paramref name="level"/> with <paramref name="coins"/> pyreals and, unless <paramref name="notes"/>, no trade notes (as <c>AutopilotDressLineTests</c>).</summary>
    private ApWorld World(int level, int coins = 300_000, bool atHome = true, bool notes = true)
    {
        var w = new ApWorld(Den, output, atHome: atHome) { Prime = c => Proven(c, Den) };
        w.Trip.Vendor.Coins(coins);
        w.Trip.Vendor.R.Tick();
        if (!notes)
        {
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

    private static int IndexOf(ApWorld w, string prefix) => w.Lines.ToList().FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));

    // ------------------------------------------------------------------------------------------------ m6-climber-start-weapon

    [Fact]
    public void With_a_weapon_wielded_and_the_bind_unproven_the_caster_is_wielded_first_and_checked_held_before_the_bind_station_casts_its_tie()
    {
        // item 2's live window: the level up job began with a weapon wielded, and the bind's Lifestone Tie (a cast) was refused once before the NoCasterHeld recovery wielded the Training Wand
        var w = new ApWorld(Den, output);
        var sword = w.Trip.Own("Training Sword", 90_100, 0x1, 50);
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, sword, (int)MeleeSlot));
        w.Trip.Vendor.R.Tick();
        var wand = w.Trip.Own("Training Wand", Wand, CasterType, 100); // in the pack
        Assert.Null(CombatModeGuard.Read(w.Trip.View.State).Caster);
        var autopilot = w.Autopilot(new AutopilotOptions());
        bool? casterAtBind = null;

        w.Run(autopilot, a => w.Factory.Made.Count > 0 && casterAtBind is not null, perFrame: _ =>
        {
            AnswerWieldsAndModes(w);
            if (casterAtBind is null && w.Factory.Made.Count > 0)
            {
                casterAtBind = CombatModeGuard.Read(w.Trip.View.State).Caster?.Guid == wand;
            }
        });

        Assert.Equal("bind", Kinds(w)[0]);
        Assert.True(casterAtBind, "the bind station began without the caster in the hands");
        Assert.Equal(wand, (uint)Assert.Single(w.Trip.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem)).Guid);
        var caster = IndexOf(w, "autopilot audit caster: the hands held no caster; fix=wield-caster: ");
        var bind = IndexOf(w, "autopilot audit bind: not proven this session");
        Assert.True(caster >= 0 && bind > caster, $"the caster line ({caster}) must come before the bind line ({bind})");
        Assert.True(IndexOf(w, "autopilot audit place: ") < caster);
    }

    // ------------------------------------------------------------------------------------------------ m6-chat-controls-robust: the audit's settle

    [Fact]
    public void At_the_settles_bound_the_queued_automation_actions_are_cancelled_and_checked_and_only_the_in_flight_one_is_reported()
    {
        var w = new ApWorld(Den, output, atHome: false) { Prime = c => Proven(c, Den) };
        var gems = Enumerable.Range(0, 12).Select(i => w.Trip.Own("Gem " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 90_200 + (uint)i, GemType, 10)).ToList();
        foreach (var gem in gems)
        {
            _ = w.Trip.View.Actions.Wield(gem, EquipMask.Held, ActionSource.Automation); // a stopped run's: the scripted ACE answers none, each waits out its time in the serialised lane
        }

        var api = (Lifestone.Client.Session.Actions.ActionApi)w.Trip.View.Actions;
        Assert.True(api.PendingOf(ActionSource.Automation).Queued > 6);
        var autopilot = w.Autopilot(new AutopilotOptions());

        w.Run(autopilot, a => w.Lines.Any(l => l.StartsWith("autopilot audit actions: ", StringComparison.Ordinal)));

        var line = Assert.Single(w.Lines, l => l.StartsWith("autopilot audit actions: ", StringComparison.Ordinal));
        Assert.Matches(@"^autopilot audit actions: [1-9]\d* queued and 1 in flight after 30 s; fix=cancelled [1-9]\d* queued \(the stand-down\); after: 0 queued, 1 in flight: sent to the server, they end by its answer or their own timeout", line);
        Assert.DoesNotContain("TODO(", line, StringComparison.Ordinal);
        Assert.Equal(0, api.PendingOf(ActionSource.Automation).Queued); // the check after, read again here
    }

    // ------------------------------------------------------------------------------------------------ 2-R12: the no-caster stop after the sale

    [Fact]
    public void M6_16_E5c2_a_purse_short_floor_does_not_wait_for_a_sale_that_would_sell_nothing()
    {
        // The pack holds only what the sale keeps: a quest file's item, a drink she uses and a Bread ACE refuses. Before this slice the gate's own list offered the first two (items with a Value that are no stock or
        // kit line), so the purse-short stop was put off past a trip whose sale could not change the purse; it left the Bread out, as now.
        var w = World(5, coins: 100, notes: false);
        w.Trip.Own("Wand", Wand, CasterType, 100);
        w.Trip.Own("Quest Thing", Lifestone.Client.Session.Scenarios.Journey.ResupplyRun.QuestItemWcids.First(), GemType, 2_000);
        w.Trip.Own("Health Tincture", Lifestone.Client.Session.Vitals.HealthDrinks.TinctureWcid, GemType, 500);
        w.Trip.Own("Bread", Lifestone.Client.Session.Contracts.VendorStock.BreadWcid, GemType, 5);
        var state = w.Trip.View.State;

        Assert.Empty(StartupAudit.SaleOffers(state, w.Ground));
        var floor = StartupAudit.ReadFloor(state, w.Ground, saleMade: false);
        Assert.Null(floor.Short);
        Assert.StartsWith("no-caster: ", floor.Stop, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purse_short_floor_with_something_to_sell_waits_for_the_trips_sale_and_with_nothing_to_sell_or_after_the_sale_stops()
    {
        var w = World(5, coins: 100, notes: false);
        w.Trip.Own("Wand", Wand, CasterType, 100); // one caster: the purse cannot pay the second
        var state = w.Trip.View.State;
        Assert.Empty(StartupAudit.SaleOffers(state, w.Ground));
        Assert.StartsWith("no-caster: ", StartupAudit.ReadFloor(state, w.Ground, saleMade: false).Stop, StringComparison.Ordinal); // nothing to sell: the sale cannot change the purse

        w.Trip.Own("Gold Ring", 90_300, GemType, 2_000); // loot the sale may offer
        Assert.Equal(["Gold Ring"], StartupAudit.SaleOffers(state, w.Ground));
        var before = StartupAudit.ReadFloor(state, w.Ground, saleMade: false);
        Assert.Null(before.Stop);
        Assert.Contains("the trip's sale comes first: the pack holds 1 item(s) it may offer (Gold Ring)", before.Short, StringComparison.Ordinal);
        Assert.EndsWith("the stop is judged after the sale (2-R12)", before.Short, StringComparison.Ordinal);
        Assert.True(StartupAudit.ReadKit(state, w.Ground, new LogisticsCounters(w.Ground, []), saleMade: false).TripDue);

        Assert.StartsWith("no-caster: ", StartupAudit.ReadFloor(state, w.Ground, saleMade: true).Stop, StringComparison.Ordinal); // after the sale: the stop
        Assert.StartsWith("no-caster: ", StartupAudit.ReadFloor(state, w.Ground, lootOn: false, saleMade: false).Stop, StringComparison.Ordinal); // loot off: the trip buys no floor
    }

    [Fact]
    public void The_audit_makes_the_trip_first_and_stops_no_caster_by_name_only_after_its_sale_left_the_purse_short()
    {
        var w = World(5, coins: 100, atHome: false, notes: false);
        w.Trip.Own("Wand", Wand, CasterType, 100);
        w.Trip.Own("Gold Ring", 90_300, GemType, 2_000);
        var autopilot = w.Autopilot(new AutopilotOptions());

        var status = w.Run(autopilot, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(GearFloor.NoCaster, autopilot.StopName);
        Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Logistics); // the trip (its sale) first
        var stop = Assert.Single(w.Lines, l => l.StartsWith("autopilot audit caster: 1 caster(s) held, 2 wanted: no-caster: ", StringComparison.Ordinal));
        Assert.EndsWith("(judged after the audit's trip and its sale, 2-R12); fix=stop: no-caster", stop, StringComparison.Ordinal);
        Assert.True(IndexOf(w, "autopilot audit floor: gear floor: 1 caster(s) held, 2 wanted: no-caster: ") < w.Lines.ToList().IndexOf(stop));
    }

    // ------------------------------------------------------------------------------------------------ m5-audit-kit-restock: the kit's caster and robe through the gear floor

    [Fact]
    public void The_kits_wand_and_robe_go_through_the_gear_floor_and_the_gloves_packs_and_foci_stay_named()
    {
        var w = World(5);
        var counters = new LogisticsCounters(w.Ground, []);
        var kit = StartupAudit.ReadKit(w.Trip.View.State, w.Ground, counters);

        Assert.Contains(kit.FloorRouted, l => l.StartsWith("Wand 0/1: the gear floor buys 2 x Wand at Archmage Cindrue for 270 on a loot trip", StringComparison.Ordinal));
        Assert.Contains(kit.FloorRouted, l => l.StartsWith("Faran Robe with Hood 0/1: the gear floor buys 1 x ", StringComparison.Ordinal) && l.EndsWith(" on a loot trip", StringComparison.Ordinal));
        Assert.Contains("Leather Gauntlets 0/1", kit.KitRest);
        Assert.Contains("Pack 0/2", kit.KitRest);
        Assert.Contains(kit.KitRest, l => l.StartsWith("focus ", StringComparison.Ordinal));
        Assert.DoesNotContain(kit.KitRest, l => l.StartsWith("Wand", StringComparison.Ordinal) || l.StartsWith("Faran", StringComparison.Ordinal));
        Assert.Contains("Wand 0/1", kit.Missing); // still missing: the floor buys it on the next loot trip

        w.Trip.Own("Fire Sceptre", 90_400, CasterType, 100);
        w.Trip.Own("Spare Sceptre", 90_401, CasterType, 100); // two casters: the floor covers the kit's wand
        kit = StartupAudit.ReadKit(w.Trip.View.State, w.Ground, counters, lootOn: false);
        Assert.Contains("Wand 0/1: covered by the gear floor's casters (2 held, 2 wanted)", kit.FloorRouted);
        Assert.DoesNotContain("Wand 0/1", kit.Missing);
        Assert.Contains(kit.FloorRouted, l => l.StartsWith("Faran Robe with Hood 0/1: ", StringComparison.Ordinal) && l.EndsWith("only on a loot trip, and loot is off (TODO(m6-floor-needs-loot))", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ m5-audit-kit-unread: one re-ask, then named

    [Fact]
    public void A_pack_that_does_not_read_is_re_asked_once_then_named_unread_and_one_that_reads_in_the_re_ask_is_judged()
    {
        var w = new ApWorld(Den, output) { Prime = c => Proven(c, Den) };
        w.OwnKit();
        w.Trip.Vendor.State.Self.Inventory.ListsComplete = false; // the description's lists could not be decoded: the counters say unread
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Audits >= 1);

        Assert.Single(w.Lines, l => l.StartsWith("autopilot audit waits: kit: ", StringComparison.Ordinal));
        Assert.Contains("re-asked once", Assert.Single(w.Lines, l => l.StartsWith("autopilot audit waits: kit-reask: the pack did not read within 15 s", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("nor in the re-ask's 15 s more", Assert.Single(w.Lines, l => l.StartsWith("autopilot audit kit: ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(" kit=unread ", autopilot.LastAudit, StringComparison.Ordinal);

        // the pack reads in the re-ask's window: the kit is judged on it
        var r = new ApWorld(Den, output) { Prime = c => Proven(c, Den) };
        r.OwnKit();
        r.Trip.Vendor.State.Self.Inventory.ListsComplete = false;
        var reread = r.Autopilot(new AutopilotOptions());
        r.Run(reread, a => a.Audits >= 1, perFrame: _ =>
        {
            if (r.Lines.Any(l => l.StartsWith("autopilot audit waits: kit-reask: ", StringComparison.Ordinal)))
            {
                r.Trip.Vendor.State.Self.Inventory.ListsComplete = true;
            }
        });

        Assert.Contains(r.Lines, l => l.StartsWith("autopilot audit kit: the pack read in the re-ask's window", StringComparison.Ordinal));
        Assert.Contains(" kit=ok ", reread.LastAudit, StringComparison.Ordinal);
    }
}
