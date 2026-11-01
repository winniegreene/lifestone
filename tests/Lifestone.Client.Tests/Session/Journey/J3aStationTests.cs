using System.Text.RegularExpressions;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Client.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The heal and Holtburg stations of lane J3a (M4 P5b: kit-refused, harm-heal, cast-facts, chests, shortcuts, appraise-items, appraise-vendor, vendor-facts) offline: each station's run, built from the journey's context, runs against a present player on the scripted
/// rig to its first server wait (or to the end of its offline checks), never calls the <c>place</c> tool (the action log is counted) and asks for every walk through the context's travel with the purpose and the goal the catalog needs. The verdict fields keep the ported legs' names.
/// What the stations prove against the server is the loopback facts' (the lead runs them); this proves their order and their seams.
/// </summary>
public sealed partial class J3aStationTests
{
    [GeneratedRegex(@"^kit=n/a bow=n/a appraised=0 failed=0 cooldowns=0 not_found=0 problems=2 granted=0$")]
    private static partial Regex ItemsVerdictNothingHeld();

    [GeneratedRegex(@"^thelnoth=n/a appraised=0 failed=0 cooldowns=0 not_found=0 problems=1 granted=0$")]
    private static partial Regex VendorVerdictNobody();

    [GeneratedRegex(@"^buy=no sellback=no refused=no sold=no problems=0 granted=0 vendor_frame_max_ms=n/a door_opened=no$")]
    private static partial Regex VendorFactsVerdictStart();

    [GeneratedRegex(@"^added=0 listed=-1 used=none removed=-1 bar_empty=no problems=0 granted=0$")]
    private static partial Regex ShortcutsVerdictStart();

    [GeneratedRegex(@"^outcome='none' code=0x0000 pack_before=\d+ pack_after=-1 held_before=\d+ held_after=-1 kit_uses=n/a problems=0 skipped=precondition:handy-healing-kit$")]
    private static partial Regex KitRefusedVerdictSkipped();

    [GeneratedRegex(@"^chest_opened=(true|false) closed=(true|false) locked=(none|refused|unreached|not_refused) problems=\d+$")]
    private static partial Regex ChestsVerdictShape();

    [Fact]
    public void The_rigs_place_count_sees_a_place_the_server_was_asked_for_so_the_zero_of_the_stations_means_something()
    {
        var rig = new J3aStationRig();
        Assert.Equal(0, rig.Places);

        var destination = new TeleportDestination("somewhere", [], new WorldLocation("somewhere", new CellPosition(0xA9B40111, System.Numerics.Vector3.Zero), HasHeight: true, HeadingDegrees: 0), "test");
        rig.View.Tools.Place(destination, ActionSource.Host);
        rig.R.Tick(0.25);

        Assert.Equal(1, rig.Places);
    }

    [Fact]
    public void Every_station_has_its_catalog_name_and_the_needs_the_plans_assert()
    {
        (IStation Station, string Name, StationWhere Where, bool Peace, int Kits)[] table =
        [
            (new KitRefusedStation(), "kit-refused", StationWhere.Holtburg, true, 0),
            (new HarmHealStation(), "harm-heal", StationWhere.Here, true, 0),
            (new CastFactsStation("account"), "cast-facts", StationWhere.Holtburg, true, 0),
            (new ChestsStation(), "chests", StationWhere.Holtburg, true, 0),
            (new ShortcutsStation(), "shortcuts", StationWhere.Holtburg, true, 0),
            (new AppraiseItemsStation(), "appraise-items", StationWhere.Here, false, 1),
            (new AppraiseVendorStation(), "appraise-vendor", StationWhere.Holtburg, true, 0),
            (new VendorFactsStation(), "vendor-facts", StationWhere.Holtburg, true, 0),
        ];

        foreach (var (station, name, where, peace, kits) in table)
        {
            Assert.Equal(name, station.Name);
            Assert.Equal(where, station.Needs.Where);
            Assert.Equal(peace, station.Needs.Peace);
            Assert.Equal(kits, station.Needs.Kits);
        }
    }

    [Fact]
    public void The_chests_station_walks_to_each_chest_as_a_cell_goal_through_the_travel_and_places_nothing()
    {
        var rig = new J3aStationRig();
        var run = (ChestsRun)rig.Start(new ChestsStation());

        var status = rig.Run(run, 200);

        Assert.Equal(ScenarioStatus.Succeeded, status); // the offline checks fail (no chest object), recorded as problems; the run goes on to its end
        Assert.Equal(2, rig.Travel.Requests.Count);
        Assert.All(rig.Travel.Requests, r => Assert.Equal(TravelPurpose.Cell, r.Purpose)); // a chest is neither a shop (Vendor) nor the lifestone (Bind)
        Assert.Equal(0xA9B4016Au, rig.Travel.Requests[0].Destination.Location.Position.CellId);
        Assert.Equal(ChestsRun.ChestRange, rig.Travel.Requests[0].Range);
        Assert.Equal(0xA9B40022u, rig.Travel.Requests[1].Destination.Location.Position.CellId);
        Assert.Equal(0, rig.Places);
        Assert.False(run.Ok);
        Assert.Contains(run.Problems, p => p.Contains("no object named 'Chest'", StringComparison.Ordinal));
        Assert.Matches(ChestsVerdictShape(), run.VerdictFields());
        Assert.Equal(0, rig.Actions(ActionKind.OpenContainer)); // nothing to open was in reach
    }

    [Fact]
    public void The_kit_refused_station_asks_for_no_kit_with_the_item_verb_and_with_none_held_ends_skipped_by_name_M5_3_c1()
    {
        var rig = new J3aStationRig();
        var run = (KitRefusedRun)rig.Start(new KitRefusedStation(), "BSD Mage");

        var status = rig.Run(run, 8);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(run.Ok, string.Join(" ; ", run.Problems));
        Assert.Equal(0, rig.ToolRequests("item")); // play grants nothing (M5 DoD 6): the station uses a kit the pack holds
        Assert.Equal(0, rig.Places);
        Assert.Matches(KitRefusedVerdictSkipped(), run.VerdictFields());
        Assert.Equal(0, run.Granted);
    }

    [Fact]
    public void The_shortcuts_station_grants_the_kit_it_needs_with_the_item_verb_and_counts_it_then_waits_for_the_server()
    {
        var rig = new J3aStationRig();
        var run = (ShortcutsRun)rig.Start(new ShortcutsStation(), "Holtburg Archer");

        var status = rig.Run(run, 8);

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(1, rig.ToolRequests("item"));
        Assert.Equal(0, rig.Places);
        Assert.Equal(0, run.Granted); // counted when the grant lands, not when it is asked
        Assert.Matches(ShortcutsVerdictStart(), run.VerdictFields());
    }

    [Fact]
    public void The_appraise_items_station_with_nothing_held_records_both_misses_and_sends_nothing()
    {
        var rig = new J3aStationRig();
        var run = (AppraiseItemsRun)rig.Start(new AppraiseItemsStation(), "Holtburg Archer");

        var status = rig.Run(run, 40);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(0, rig.Actions(ActionKind.Identify));
        Assert.Equal(0, rig.Places);
        Assert.Empty(rig.Travel.Requests); // the items half never walks
        Assert.False(run.Ok);
        Assert.Matches(ItemsVerdictNothingHeld(), run.VerdictFields());
    }

    [Fact]
    public void The_appraise_vendor_station_walks_to_Thelnoths_cellar_as_a_vendor_goal_on_each_side_and_places_nothing()
    {
        var rig = new J3aStationRig();
        var run = (AppraiseVendorRun)rig.Start(new AppraiseVendorStation(), "Holtburg Archer");

        var status = rig.Run(run, 200);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, rig.Travel.Requests.Count); // the first side, then the other (TODO(m4-vendor-approach)): a second request, never a place
        Assert.All(rig.Travel.Requests, r =>
        {
            Assert.Equal(TravelPurpose.Vendor, r.Purpose);
            Assert.Equal(ShopLeg.ThelnothCell, r.Destination.Location.Position.CellId);
            Assert.Equal(3.0, r.Range);
        });
        Assert.NotEqual(rig.Travel.Requests[0].Destination.Location.Position.Local.X, rig.Travel.Requests[1].Destination.Location.Position.Local.X);
        Assert.Equal(0, rig.Places);
        Assert.Equal(0, rig.Actions(ActionKind.Identify));
        Assert.Matches(VendorVerdictNobody(), run.VerdictFields());
    }

    [Fact]
    public void The_vendor_facts_station_grants_the_pyreals_it_needs_first_and_the_verdict_starts_all_no()
    {
        var rig = new J3aStationRig();
        var run = (VendorFactsRun)rig.Start(new VendorFactsStation());

        var status = rig.Run(run, 8);

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(1, rig.ToolRequests("item"));
        Assert.Equal(0, rig.Places);
        Assert.Empty(rig.Travel.Requests); // the coins come before the walk
        Assert.Matches(VendorFactsVerdictStart(), run.VerdictFields());
    }

    [Fact]
    public void The_cast_facts_station_on_another_character_fails_at_once_and_sends_nothing()
    {
        var rig = new J3aStationRig();
        var run = rig.Start(new CastFactsStation("account"), "BSD Mage");

        var status = rig.Run(run, 10);

        Assert.Equal(ScenarioStatus.Failed, status); // the character in the world is nameless in the rig, not BSD Mage
        Assert.Equal(0, rig.Places);
        Assert.Equal(0, rig.ToolRequests("item"));
        Assert.Contains("not BSD Mage", run.Failure, StringComparison.Ordinal);
        Assert.StartsWith("creature=n/a problems=", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_harm_heal_station_on_the_mage_casts_Harm_Self_first_with_no_swap_and_no_place()
    {
        uint[] spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];
        var character = new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = spells };
        var vitals = new C2VitalsRig(character);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "BSD Mage");

        var status = rig.Run(run, 60, pump: vitals.Pump);

        Assert.True(status == ScenarioStatus.Running, string.Join(" ; ", run.Problems) + " | " + run.Failure);
        Assert.NotEmpty(vitals.Casts); // the harm phase's first cast reached the scripted server
        Assert.Contains(vitals.Casts, c => HarmSelfPlan.For(c.Spell) is not null);
        Assert.Equal(0, rig.Places);
        Assert.NotNull(run.Harm);
        Assert.False(run.Harm.FighterPath);
        Assert.Equal(0, run.Granted);
        Assert.StartsWith("ok=", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_harm_heal_station_on_a_fighter_swaps_the_caster_in_before_any_cast_and_places_nothing()
    {
        uint[] spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];
        var character = new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = spells };
        var vitals = new C2VitalsRig(character);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Brawler");

        var status = rig.Run(run, 18, pump: vitals.Pump); // M5 7 P7p: the pre-check listens 3 s for attacks first

        Assert.True(status == ScenarioStatus.Running, string.Join(" ; ", run.Problems) + " | " + run.Failure + " | " + run.VerdictFields());
        Assert.NotNull(run.Harm);
        Assert.True(run.Harm.FighterPath);
        Assert.Empty(vitals.Casts); // the caster is in the pack: nothing is cast before the swap in
        Assert.Contains(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.GetAndWieldItem); // the swap in began: the caster to the hand
        Assert.Equal(0, rig.ToolRequests("item")); // a kit and a potion are held: nothing to grant
        Assert.Equal(0, rig.Places);
        Assert.Equal(0, run.Granted);
    }
}
