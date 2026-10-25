using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b lane J2a: the hunt on the travel seam, over the scripted rig (<see cref="HuntScriptRig"/>, D23: nothing is spawned in any world; the rig's creature is the tests' own script). The embedded hunt over a run travel
/// places nothing and asks with each travel's purpose; over the default <see cref="PlaceTravel"/> the requests are today's places; <see cref="HuntStages"/> masks the stages; the keeper's <see cref="VitalsKeeper.MayAttack"/>
/// delays an attack and its in-fight potion drinks with the target out of reach; the sell rule never sells the worn gear (D41).
/// </summary>
public sealed class J2aHuntSeamTests
{
    private const uint Creature = 0x80000D41;
    private const string BeltName = "Field Ursuin";
    private const uint PotionOfHealing = 377;

    private static HuntOptions Base => new() { Kills = 1, Seconds = 300, Sell = false, BuyKits = 0 };

    /// <summary>A rig whose first arrival outside Holtburg (a walk or a place) holds one creature <paramref name="north"/> metres north.</summary>
    private static HuntScriptRig J2aRig(HuntOptions options, J2aRecordingTravel travel, double north = 12, Func<uint, float, float, float?>? seek = null, Func<IGameSessionView, ILogSink, VitalsKeeper>? keeper = null, CombatModeWatch? watch = null)
    {
        HuntScriptRig? made = null;
        var rig = HuntScriptRig.Create(log =>
        {
            var view = new J2aLateView(() => made!.Session);
            return new HuntScenario(options with { Travel = travel, Keeper = keeper?.Invoke(view, log) }, log, _ => 40f, endSession: false) { SeekHeight = seek, ModeWatch = watch };
        });
        made = rig;
        travel.Rig = rig;
        rig.OnBelt = (r, n) =>
        {
            if (n == 1)
            {
                r.PlaceCreature(Creature, BeltName, north);
            }
        };
        return rig;
    }

    private static VitalsKeeper Keeper(IGameSessionView view, ILogSink log, VitalsOptions? options = null) => new(view, new Dictionary<uint, SpellInfo>(), options ?? VitalsOptions.Default, log);

    // ---------------------------------------------------------------------------------------------- 1: the seam

    [Fact]
    public void An_embedded_hunt_over_a_run_travel_sends_no_place_and_asks_with_the_cell_and_home_purposes_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base, travel);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(0, rig.J2aPlaces()); // no Tools.Place in the session's action log
        Assert.Equal(0, rig.BeltArrivals); // no place-loc teleport
        Assert.Equal(1, rig.J2aWalkArrivals);
        Assert.Equal([TravelPurpose.Cell, TravelPurpose.Home], travel.Requests.Select(r => r.Purpose));
        Assert.All(travel.LegMeans, m => Assert.Equal(TravelMeans.Run, m));
        Assert.All(travel.Requests, r => Assert.Equal(0u, r.ObjectGuid)); // the cell and home are point goals
        Assert.Equal(1, rig.Scenario.Tally.Kills);
        Assert.EndsWith(" returned=yes", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.True(rig.Logged("travel: at hunt cell 0x"), rig.Tail());
        Assert.Equal(TravelMeans.Run, rig.Scenario.LastTravelMeans);
    }

    [Fact]
    public void Over_the_default_place_travel_the_requests_are_places_as_before_J2a()
    {
        var travel = new J2aRecordingTravel { Inner = new PlaceTravel() };
        using var rig = J2aRig(Base, travel);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal([TravelPurpose.Cell, TravelPurpose.Home], travel.Requests.Select(r => r.Purpose));
        Assert.All(travel.LegMeans, m => Assert.Equal(TravelMeans.Place, m));
        Assert.Equal(2, rig.J2aPlaces()); // one place per request: the cell, then Holtburg
        Assert.Equal(1, rig.BeltArrivals);
        Assert.Equal(0, rig.J2aWalkArrivals);
        Assert.False(rig.Logged("travel: at "), "a place's arrival is today's check, not the seam's line");
        Assert.EndsWith(" returned=yes", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("infight_potions", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_run_leg_goes_down_the_place_failure_path_with_the_legs_failure_J2a()
    {
        var travel = new J2aRecordingTravel { FailWhen = r => r.Purpose == TravelPurpose.Cell };
        using var rig = J2aRig(Base, travel);

        var status = rig.Run(900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("placing: travel:run:test-refusal", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, rig.J2aPlaces());
        Assert.Equal(0, rig.Scenario.Tally.Kills);
        Assert.Equal(TravelPurpose.Home, travel.Requests[^1].Purpose); // the wind-down still goes home through the seam
    }

    [Fact]
    public void A_seek_on_foot_asks_for_the_creature_as_an_object_goal_of_10_m_with_sight_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base, travel, north: 80, seek: (_, _, _) => 40f);

        Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), rig.Scenario.Failure + rig.Tail());

        Assert.Equal([TravelPurpose.Cell, TravelPurpose.Seek], travel.Requests.Select(r => r.Purpose));
        var seek = travel.Requests[1];
        Assert.Equal(Creature, seek.ObjectGuid);
        Assert.Equal(HuntScenario.SeekGoalMetres, seek.Range);
        Assert.True(seek.NeedSight);
        Assert.Equal(0, rig.J2aPlaces());
        Assert.True(rig.Logged("closes m4-seek-terrain-only"), rig.Tail());
        Assert.False(rig.Logged("TODO(m4-seek-terrain-only)"), rig.Tail());
    }

    [Theory]
    [InlineData("travel:run:arrivedwithoutsight: x", true, true)]
    [InlineData("travel:run:reworded: x", true, true)] // fix FA (P5b-R5): the typed end decides, whatever the text says
    [InlineData("travel:run:arrivedwithoutsight: x", false, false)] // and the text alone no longer does
    [InlineData("travel:run:blocked", false, false)]
    [InlineData("travel:run:failed", false, false)]
    public void A_seeks_arrival_without_sight_skips_the_target_and_the_hunt_goes_on_but_a_blocked_leg_still_fails_it_F3(string failure, bool sightless, bool goesOn)
    {
        // Live (journey 3h archer): the seek 'journey-seek-5' ended ArrivedWithoutSight and the hunt died with "placing: travel:run:arrivedwithoutsight" at kills=0.
        var travel = new J2aRecordingTravel { FailWhen = r => r.Purpose == TravelPurpose.Seek, FailureText = failure, Sightless = sightless };
        using var rig = J2aRig(Base, travel, north: 80, seek: (_, _, _) => 40f);

        var status = rig.Run(300);

        Assert.Contains(travel.Requests, r => r.Purpose == TravelPurpose.Seek && r.NeedSight);
        if (goesOn)
        {
            Assert.True(rig.Logged("arrived without a clear line"), rig.Tail());
            Assert.True(rig.Logged("sightless=1"), rig.Tail());
            Assert.DoesNotContain("placing:", rig.Scenario.Failure ?? string.Empty, StringComparison.Ordinal); // it may end on "no creature", never on the seek
            Assert.Contains("sightless=1", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(ScenarioStatus.Failed, status);
            Assert.StartsWith("placing: " + failure, rig.Scenario.Failure, StringComparison.Ordinal);
            Assert.Contains("sightless=0", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_missile_pick_on_foot_is_an_object_goal_with_sight_before_the_first_shot_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base with { Weapon = HuntWeapon.Missile }, travel);

        Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), rig.Scenario.Failure + rig.Tail());

        Assert.Equal([TravelPurpose.Cell, TravelPurpose.Seek], travel.Requests.Select(r => r.Purpose));
        var sight = travel.Requests[1];
        Assert.Equal(Creature, sight.ObjectGuid);
        Assert.Equal(HuntScenario.MissileRange, sight.Range);
        Assert.True(sight.NeedSight);
        Assert.Equal(0, rig.J2aPlaces());
        Assert.True(rig.Logged("closes m4-missile-clear-line"), rig.Tail());
        Assert.False(rig.Logged("TODO(m4-missile-clear-line)"), rig.Tail());
    }

    // ---------------------------------------------------------------------------------------------- 2: the stages

    [Fact]
    public void The_journeys_ground_stages_skip_the_bind_the_first_recovery_home_and_the_shops_and_their_fields_read_n_a_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base with { Stages = HuntStages.Ground, Sell = true }, travel);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal([TravelPurpose.Cell], travel.Requests.Select(r => r.Purpose)); // no bind, no home
        Assert.False(rig.Logged("bind: using the lifestone"), rig.Tail());
        Assert.Equal(1, rig.LineCount("stages: running swap|hunt|restore; skipped bind|health|home|vendor (bound=n/a returned=n/a sell=n/a buy=n/a)"));
        Assert.EndsWith(" returned=n/a", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" bound=n/a ", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Contains(" restored=yes ", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Contains(" sell=n/a buy=n/a ", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(1, rig.Scenario.Tally.Kills);
        Assert.Equal(1, rig.LineCount("wind-down: returned=n/a (the journey's recall-home station returns)")); // M4 4.2 J1: never "NOT back in Holtburg" when the home stage is skipped
        Assert.False(rig.Logged("NOT back in Holtburg"), rig.Tail());
    }

    [Fact]
    public void The_journeys_hunt_runs_its_combat_mode_watch_from_its_first_step_and_ends_it_with_its_subscriptions_M4_4_2_J1()
    {
        var travel = new J2aRecordingTravel();
        var watch = new CombatModeWatch();
        using var rig = J2aRig(Base with { Stages = HuntStages.Ground }, travel, watch: watch);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.False(watch.Running); // disposed with the hunt's subscriptions
        Assert.Equal(0, watch.Violations); // the melee hunt in melee mode with its Mace: no disagreement
        Assert.Equal(0, HuntLegVerdict.FromScenario(rig.Scenario, HuntWeapon.Melee, lootEnabled: true, HuntGroundChoice.Belt).ModeMismatch);
        Assert.EndsWith(" mode_mismatch=0 attack_frame_max_ms=n/a loot_frame_max_ms=n/a", JourneyStations.HuntFields(rig.Scenario, null), StringComparison.Ordinal);
        Assert.Contains(" kills=1 ", JourneyStations.HuntFields(rig.Scenario, null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vendor_stage_alone_walks_from_the_lifestone_to_Renald_in_his_front_room_as_an_object_goal_and_attacks_nothing_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base with { Stages = HuntStages.Vendor, Sell = true }, travel);
        rig.Shops = true;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        var shop = Assert.Single(travel.Requests);
        Assert.Equal(TravelPurpose.Vendor, shop.Purpose);
        Assert.Equal(HuntScenario.RenaldFrontRoomCell, shop.Destination.Location.Position.CellId); // never 0xA9B40141 behind the counter
        Assert.Equal(HuntScriptRig.RenaldGuid, shop.ObjectGuid);
        Assert.Equal(6.5, shop.Range);
        Assert.Equal(0, rig.J2aPlaces());
        Assert.Equal(0, rig.Scenario.Tally.Kills);
        Assert.DoesNotContain(rig.Sends, s => s.Type == GameActionTypes.TargetedMeleeAttack);
        Assert.True(rig.Scenario.Sold >= 1, rig.Scenario.SellOutcome + rig.Tail());
        Assert.EndsWith(" returned=n/a", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vendor_stage_alone_fails_with_the_travels_failure_when_the_walk_to_Renald_fails_F2()
    {
        var travel = new J2aRecordingTravel { FailWhen = r => r.Purpose == TravelPurpose.Vendor };
        using var rig = J2aRig(Base with { Stages = HuntStages.Vendor, Sell = true }, travel);
        rig.Shops = true;

        var status = rig.Run(900);

        // M4 P5b F2-4: the live run's walk failed and the vendor station passed doing nothing (sold=0, returned=n/a).
        Assert.NotNull(rig.Scenario.Failure);
        Assert.Contains("test-refusal", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.StartsWith("vendors: Renald the Elder: ", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, rig.Scenario.Sold);
        Assert.StartsWith("failed:", rig.Scenario.SellOutcome, StringComparison.Ordinal);
        Assert.Contains("sold=0 ", rig.Scenario.VendorFields(), StringComparison.Ordinal);
        Assert.NotEqual(ScenarioStatus.Running, status);
    }

    [Theory]
    [InlineData(HuntStages.All, "bind|health|swap|hunt|restore|home|vendor")]
    [InlineData(HuntStages.Ground, "swap|hunt|restore")]
    [InlineData(HuntStages.Vendor, "vendor")]
    [InlineData(HuntStages.None, "none")]
    public void The_stage_masks_spell_their_names_in_todays_order_J2a(HuntStages stages, string text)
    {
        Assert.Equal(text, HuntOptions.StagesText(stages));
        Assert.Equal(stages == HuntStages.All, !(Base with { Stages = stages }).ToString().Contains(" stages=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_stages_are_all_of_them_and_the_default_travel_is_a_place_per_scenario_J2a()
    {
        Assert.Equal(HuntStages.All, new HuntOptions().Stages);
        Assert.Null(new HuntOptions().Travel);
        Assert.Null(new HuntOptions().Keeper);
        var one = new HuntScenario(new HuntOptions(), new RecordingLogSink(), _ => 40f);
        var two = new HuntScenario(new HuntOptions(), new RecordingLogSink(), _ => 40f);
        Assert.IsType<PlaceTravel>(one.Travel);
        Assert.NotSame(one.Travel, two.Travel); // PlaceTravel's Busy is its own leg's
    }

    // ---------------------------------------------------------------------------------------------- 3: the keeper and the gear

    [Fact]
    public void The_keepers_MayAttack_refusal_delays_the_attack_until_the_stamina_is_back_J2a()
    {
        var travel = new J2aRecordingTravel();
        using var rig = J2aRig(Base, travel, keeper: (view, log) => Keeper(view, log));
        rig.OnBelt = (r, n) =>
        {
            if (n != 1)
            {
                return;
            }

            r.PlaceCreature(Creature, BeltName, 12);
            r.Session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var max);
            r.J2aStamina(1); // under --vitals-attack-stamina 15%
            r.J2aStaminaLater(20, max);
        };

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(1, rig.Scenario.AttackWaits);
        var held = rig.LineAt("fight: the keeper holds the attack on");
        Assert.True(held >= 0 && held < rig.LineAt("fight: Attack 1 at"), rig.Tail());
        Assert.Contains("is under --vitals-attack-stamina", rig.Log.Lines[held], StringComparison.Ordinal);
        Assert.Equal(1, rig.Scenario.Tally.Kills);
        Assert.EndsWith(" returned=yes infight_potions=0", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Under_the_in_fight_threshold_with_the_target_out_of_reach_the_keeper_drinks_before_the_attack_J2a()
    {
        var travel = new J2aRecordingTravel();
        var options = Base with { Recovery = RecoveryOptions.Default with { Enabled = false } };
        using var rig = J2aRig(options, travel, keeper: (view, log) => Keeper(view, log, VitalsOptions.Default with { InFightPotion = true }));
        rig.StartHealth = 70; // 37% of 188: under the keeper's 40%, over the melee retreat's 30%

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(1, rig.Scenario.InFightPotions);
        var drink = rig.LineAt("fight: the keeper drinks an in-fight potion");
        Assert.True(drink >= 0 && drink < rig.LineAt("fight: Attack 1 at"), rig.Tail());
        Assert.Contains("12 m away", rig.Log.Lines[drink], StringComparison.Ordinal);
        Assert.EndsWith(" infight_potions=1", rig.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, rig.Scenario.HealTally.Potions); // the hunt's own recovery is off: the drink is the keeper's
    }

    [Fact]
    public void The_sell_rule_never_sells_worn_or_wielded_gear_a_side_pack_the_caster_components_or_potions_D41_J2a()
    {
        var rig = new VendorRig();
        var pack = new List<ContentDesc>();
        var worn = new List<EquippedDesc>();
        var next = 0x80000900u;
        WorldObject Hold(uint wcid, uint itemType, uint value, bool wear = false, bool sidePack = false, uint location = 0x200)
        {
            var guid = next++;
            rig.Arrive(guid, "item " + wcid, wcid, itemType, value);
            if (wear)
            {
                worn.Add(new EquippedDesc(guid, location, 1));
            }
            else
            {
                pack.Add(new ContentDesc(guid, sidePack ? 1u : 0u));
            }

            rig.R.Describe([.. pack], [.. worn]);
            return rig.State.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException("item " + wcid);
        }

        SellDecision Decide(WorldObject item) => SellRules.Default.Decide(item, rig.State.Self.Inventory, null);

        var gearWcid = FighterGearSet.AllWcids[0];
        var wornGear = Hold(gearWcid, 0x2, 500, wear: true);
        var wieldedStranger = Hold(99001, 0x1, 100, wear: true, location: 0x100000); // a weapon outside every kit, wielded
        var spareGear = Hold(gearWcid, 0x2, 500);
        var caster = Hold(GearKit.Wand.Wcid, 0x8000, 100);
        var manaPotion = Hold(FighterKit.ManaPotionWcid, 0x80, 50);
        var staminaPotion = Hold(FighterKit.StaminaPotionWcid, 0x80, 50);
        var healing = Hold(PotionOfHealing, 0x80, 50);
        var component = Hold(691, GearKit.ItemTypeSpellComponents, 10);
        var sidePackItem = Hold(139, 0x200, 10, sidePack: true);
        var junk = Hold(141, 0x80, 10);

        Assert.Equal(SellDecision.Equipped, Decide(wornGear));
        Assert.Equal(SellDecision.Equipped, Decide(wieldedStranger));
        Assert.Equal(SellDecision.Kept, Decide(spareGear));
        Assert.Equal(SellDecision.Kept, Decide(caster));
        Assert.Equal(SellDecision.Kept, Decide(manaPotion));
        Assert.Equal(SellDecision.Kept, Decide(staminaPotion));
        Assert.Equal(SellDecision.Kept, Decide(healing));
        Assert.Equal(SellDecision.Kept, Decide(component));
        Assert.Equal(SellDecision.Container, Decide(sidePackItem));
        Assert.Equal(SellDecision.Sell, Decide(junk)); // the control: loot the shop's mask takes is sold
        Assert.All(FighterKit.Melee.Worn.Concat(FighterKit.Archer.Worn), line => Assert.True(SellRules.IsKept(line.Wcid), "a kit's worn line " + line.Wcid + " is kept"));
    }
}
