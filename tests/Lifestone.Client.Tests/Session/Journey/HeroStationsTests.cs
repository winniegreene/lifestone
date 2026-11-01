using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>The hero plans' stations (the M4 hero video, the owner's re-cut): the lower-right bar's picks.</summary>
public sealed class HeroStationsTests
{
    [Fact]
    public void The_bar_takes_potions_and_food_then_kits_mana_stones_ammunition_and_the_caster_one_per_weenie_in_the_packs_order()
    {
        (uint, uint, uint)[] pack =
        [
            (0x10, 300, 0x200),   // a side pack: never
            (0x11, 628, 0x80),    // Handy Healing Kit (Misc)
            (0x12, 377, 0x20),    // Potion of Healing (Food)
            (0x13, 377, 0x20),    // a second of the same weenie: once only
            (0x14, 9060, 0x80000), // Titan Mana Charge (ManaStone)
            (0x15, 691, 0x1000),  // a spell component: never
            (0x16, 5307, 0x100),  // Greater Frost Arrows (MissileWeapon)
            (0x17, 2366, 0x8000), // the caster kept in the pack
            (0x18, 378, 0x20),    // Potion of Stamina (Food)
            (0x19, 46, 0x2),      // armour in the pack: never
            (0x1A, 273, 0x40),    // money: never
        ];

        Assert.Equal([0x12u, 0x18, 0x11, 0x14, 0x16, 0x17], HotbarPicks.Choose(pack, [], 9));
        Assert.Equal([0x12u, 0x18, 0x11], HotbarPicks.Choose(pack, [], 3)); // only the empty slots
        Assert.Equal([0x18u, 0x11, 0x14], HotbarPicks.Choose(pack, [377u], 3)); // a weenie on the bar already is not picked again
        Assert.Empty(HotbarPicks.Choose(pack, [], 0));
        Assert.Empty(HotbarPicks.Choose([(0x20u, 1u, HotbarPicks.Never)], [], 9));
    }

    [Fact]
    public void The_hotbar_station_is_the_hero_plans_own_and_fills_the_nine_visible_slots()
    {
        Assert.Equal(StationKinds.Hotbar, new HotbarStation().Name);
        Assert.Equal(Lifestone.Client.RetailUi.Toolbar.GameHotbar.VisibleSlots, HotbarStation.VisibleSlots);
        Assert.All(JourneyPlans.Hero, p => Assert.Equal(StationKinds.Hotbar, p.Stations[1].Kind));
    }

    [Fact]
    public void A_filmed_hero_journey_opens_the_attributes_during_the_deaths_rebuff_and_the_inventory_while_a_hunt_loots()
    {
        Assert.Equal(HeroPanels.CharacterInfo, HeroPanels.Want(StationKinds.Death, DeathScenario.HandlerStagePrefix + "rebuff", false)); // M5 3 d4: the stage the live scenario reports is handler-rebuff
        Assert.Equal("handler-rebuff", DeathScenario.HandlerStagePrefix + "rebuff");
        Assert.Equal(0u, HeroPanels.Want(StationKinds.Death, "rebuff", false)); // the retired M4 name no longer opens it
        Assert.Equal(0u, HeroPanels.Want(StationKinds.Death, DeathScenario.HandlerStagePrefix + "relines", false));
        Assert.Equal(0u, HeroPanels.Want(StationKinds.Death, "death-wait", false));
        Assert.Equal(0u, HeroPanels.Want(StationKinds.Death, DeathScenario.HandlerStagePrefix + "corpse-loot", true)); // the death's own corpse trip is not the hunt's loot
        Assert.Equal(HeroPanels.Inventory, HeroPanels.Want(JourneyStationNames.Hunt, null, true));
        Assert.Equal(0u, HeroPanels.Want(JourneyStationNames.Hunt, null, false));
        Assert.Equal(0u, HeroPanels.Want(StationKinds.Vendor, null, true)); // the vendor window opens itself
        Assert.Equal(0u, HeroPanels.Want(null, null, true));
        Assert.Equal(Lifestone.Client.RetailUi.Panels.GameInventoryPage.Panel, HeroPanels.Inventory);
    }

    [Fact]
    public void The_corpses_station_follows_the_den_arrival_and_outwaits_aces_rot_of_the_dropped_contents()
    {
        Assert.Equal(StationKinds.Corpses, new CorpsesStation().Name);
        var names = JourneyPlans.HeroMage.Stations.Select(s => s.Name).ToList();
        Assert.Equal(names.IndexOf(StationKinds.DenTravel) + 1, names.IndexOf(StationKinds.Corpses));
        Assert.True(CorpsesStation.ItemRotSeconds >= 300 + 5, "ACE's default rot (300 s) and its 5 s decay heartbeat"); // WorldObject_Decay.cs:18 at 95ef426d
        Assert.True(CorpsesStation.CorpseGoneSeconds >= 15 + 5, "ACE's empty-corpse 15 s and the heartbeat");
    }
}
