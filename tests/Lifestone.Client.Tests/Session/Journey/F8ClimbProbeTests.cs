using System.Text.Json.Nodes;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Autopilot;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 0 fix slice F8 (the first live climb probe, Winnie at level 6): the buff station judged the den's arena kit because the factory read the default ground while the run's ground was the rung's; a character with a Training Wand
/// in the pack and nothing in her hands kept the vitals guard asking for the magic mode for 90 s until the known-state watchdog fired; and a Strength 10 mage (capacity 1,500) was at 129% after one restock.
/// </summary>
public sealed class F8ClimbProbeTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Outskirts = "holtburg-outskirts";

    private IScenario? FirstStep(string kind, bool withRunGround)
    {
        var t = new J3bRig { Dats = fixture.Dats };
        var ground = GroundCatalog.Get(Outskirts);
        var factory = withRunGround ? JourneyStations.Create(JourneyPlans.Climber, [], fixture.Dats, "test", ground) : JourneyStations.Create(JourneyPlans.Climber, [], fixture.Dats, "test");
        var station = factory.Create(new StationSpec("probe", kind, new StationNeeds(StationWhere.Here), 280));
        var run = station.Start(t.Context(dungeon: ground));
        run.Step(t.View, t.Now); // the scenario is made on the first step; its gear stage holds the kit the factory chose
        return (run as IStationInner)?.Scenario;
    }

    private static GearKit KitOf(IScenario? scenario)
    {
        var buff = Assert.IsType<BuffScenario>(scenario);
        return (GearKit)typeof(GearScenario).GetField("_kit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(buff.Gear)!;
    }

    [RequiresDatsFact]
    public void The_buff_station_of_the_climber_judges_the_run_grounds_bare_kit_and_not_the_arena_kit_the_default_ground_gave()
    {
        Assert.True(GearKit.HasNoKit(GroundCatalog.Get(Outskirts)));
        Assert.Empty(KitOf(FirstStep(StationKinds.Buff, withRunGround: true)).Items); // GearKit.Bare: no vendor kit on holtburg-outskirts, so no gear to verify and nothing to ask the mod for

        // The probe's wiring: no --fight-ground on the command line, so the stations read the default ground (the den), whose kit is the BSD Mage's (the Wand, the robe, the gauntlets, the foci, the components).
        Assert.False(GearKit.HasNoKit(GroundCatalog.Get(FightOptions.DefaultGround)));
    }

    [RequiresDatsFact]
    public void The_stations_fight_options_take_the_run_ground_whatever_the_command_line_says()
    {
        var ground = GroundCatalog.Get(Outskirts);
        var factory = JourneyStations.Create(JourneyPlans.Climber, [$"--fight-ground={FightOptions.DefaultGround}"], fixture.Dats, "test", ground);
        var t = new J3bRig { Dats = fixture.Dats };
        var run = factory.Create(new StationSpec("probe", StationKinds.DenFight, new StationNeeds(StationWhere.Here), 280)).Start(t.Context(dungeon: ground));
        run.Step(t.View, t.Now);
        var fight = Assert.IsType<FightScenario>((run as IStationInner)?.Scenario);
        var options = (FightOptions)typeof(FightScenario).GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fight)!;
        Assert.Equal(Outskirts, options.Ground);
    }

    [Fact]
    public void The_vitals_guard_holds_nothing_when_the_hands_hold_no_caster_and_says_so_once_where_it_asked_for_the_magic_mode_every_second()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        AutopilotF30Tests.Settle(rig);
        rig.Caster.HealthPerCast = 20;
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        rig.Caster.WandToPack(); // Winnie after the Academy skip: the Training Wand is in the pack, the hands are empty
        rig.R.Tick();
        Assert.Null(CombatModeGuard.Read(rig.R.H.State).Caster);
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        var released = 0;
        for (var i = 0; i < 120; i++)
        {
            rig.R.Tick(0.1);
            released = guard.Step(rig.View, rig.Now, "known-state") ? 0 : released + 1; // the state goes on once the setup's own action in flight has ended: its caster step (the audit's wield-caster) runs
        }

        Assert.True(released >= 40, "released at the end: " + released + "; " + string.Join("; ", rig.Log.Lines));
        Assert.False(guard.Holding);
        Assert.Equal(0, guard.Casts);
        Assert.Single(rig.Log.Lines, l => l.Contains("the hands hold no caster", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("entering the magic mode", StringComparison.Ordinal)); // the old loop: once a second for 90 s
        Assert.Empty(rig.Caster.Answered);
    }

    [Fact]
    public void The_pathwarden_quest_carries_a_burden_gate_and_the_other_quests_none()
    {
        var carry = QuestCatalog.Get("pathwarden").Carry!;
        Assert.Equal((90, 120), (carry.LinePercent, carry.ReserveBurden));
        Assert.Equal(new uint[] { 40439, 41513 }, carry.Always); // M6 0 lead, live probe 3: the robe and the trinket over the line
        Assert.Null(QuestCatalog.Get("academy").Carry);
        Assert.Null(QuestCatalog.Get("academy-skip").Carry);
        var file = JsonNode.Parse(QuestCatalog.ResourceText("pathwarden.json")!)!.AsObject();
        file["carry"] = new JsonObject { ["line_percent"] = 101, ["reserve_burden"] = 1 };
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("pathwarden.json", file.ToJsonString()));
        file["carry"] = new JsonObject { ["line_percent"] = 90, ["reserve_burden"] = 1, ["extra"] = 1 };
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("pathwarden.json", file.ToJsonString()));
    }

    [Theory]
    [InlineData(10, 872, 60, true)] // the trinket
    [InlineData(10, 932, 200, true)] // the robe
    [InlineData(10, 1_132, 540, false)] // the sollerets: 1,792 > 1,350
    [InlineData(10, 1_132, 900, false)] // the gauntlets
    [InlineData(14, 1_132, 540, true)] // the sollerets from Strength 14: 1,792 <= 1,890
    [InlineData(13, 1_132, 540, false)]
    [InlineData(20, 1_672, 900, true)] // the gauntlets after the sollerets from Strength 20: 2,692 <= 2,700
    public void The_carry_gate_takes_a_piece_only_while_the_burden_the_piece_and_the_restock_reserve_stay_within_the_line(int strength, int burden, int piece, bool fits)
    {
        var carry = QuestCatalog.Get("pathwarden").Carry!;
        Assert.Equal(fits, carry.Fits(burden, EncumbranceSystem.EncumbranceCapacity(strength, 0), piece));
        Assert.False(carry.Fits(burden, 0, piece)); // no Strength read: the gate does not guess
    }

    [Fact]
    public void A_strength_10_mage_after_one_restock_the_trinket_and_the_robe_is_under_the_burden_line_of_the_outskirts_profile()
    {
        var ground = GroundCatalog.Get(Outskirts);
        var capacity = EncumbranceSystem.EncumbranceCapacity(10, 0);
        long restock = 0;
        foreach (var line in ground.Supplies.Stock)
        {
            var held = line.Wcid switch { 691 => 10, 20631 => 50, _ => 0 };
            var unit = line.Wcid switch { 20631 => GearKit.PrismaticTaperBurden, 379 => 15, _ => GearKit.ComponentBurden };
            restock += (long)unit * Math.Max(0, line.Keep - held);
        }

        Assert.Equal(1_114, restock); // M6 14 (m6-life-through): 8 for Mandrake and Hemlock Talisman at keep 1 (Harm Other's components on the account formula); M6 14 T: 96 for two more of each of the twelve full-formula lines (keep 4, so a visit leaves them clear of their lines); M6 8 (the owner, 2026-11-11: Mana Potions instead of rests): 735 for forty-nine more Mana Potions at 15 (live: one buff pass drank 17 of 20 in 66 s where it had rested 300 s); M6 7 AR: 24 for two more Copper and Silver Scarabs, one more Red and Indigo Taper; M6 7 AN: 8 for Alder Talisman and Damiana; M6 7 AK: 84 for the 21 new component lines at keep 1; M6 7 X: 16 for the four protective lines at keep 1; M6 7 AD: 8 for Saffron and Quicksilver at keep 1
        Assert.Equal(1_500, capacity);
        Assert.True(872 + restock + 60 + 200 <= capacity * ground.Supplies.BurdenTripPercent / 100); // 1,283 of 1,350: 85% of the capacity
    }
}
