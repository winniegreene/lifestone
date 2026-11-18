using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 AX (live, 2026-11-06: "Foci of Enchantment in the pack 0, paid 25000" at once, then the same focus bought again a run later): a focus is no ordinary pack item. ACE sends it with ContainerType 2 (<c>WorldObject.ContainerType</c>,
/// WorldObject.cs:453-462 @95ef426d), so the read model holds it beside the side packs, and the two readers that read only type-0 entries never saw it. The check after a buy waits for the ware by a named limit and reads the held count on every path;
/// the plan and the counters read a held focus; nothing is bought twice; a ware that never shows holds its line for one plan, by name.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint Enchantment = 15268;
    private const uint Artifice = 15269;
    private const uint FocusContainerType = 2;

    /// <summary>Winnie with the Burrow gate's conditions met (the gate's skills trained, its lines in the book), both enchantment schools trained.</summary>
    private static PlayerDescriptionMsg ClimberAtTheFoci()
    {
        var gate = LadderLoader.WarMage.GateOf(FociPlan.GateRung)!;
        var skills = gate.Skills.Concat([MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment]).Distinct();
        return new PlayerDescriptionMsg
        {
            Attributes = [new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
            Skills = [.. skills.Select(id => new SkillDesc(id, 10, 1, 2u, 0, 0u, 0, 0))],
            Spells = gate.Lines.Select(l => RungGateRule.LineOf(BuffProfiles.Climber, l.Line).Spells[l.Level - 1].Id).Distinct().ToImmutableDictionary(id => id, _ => 2f),
        };
    }

    private static Home FociHome(int coins = 120_000)
    {
        var home = new Home(Outskirts, coins, description: ClimberAtTheFoci());
        home.Vendor.State.Self.Name = MageTemplate.ClimberName;
        var row = 0x80008000u;
        foreach (var scrivener in home.Ground.Scriveners) // the catalog's scriveners join the ground's view, not Home's shops
        {
            home.Vendor.PlaceAt(scrivener.Guid, scrivener.Name, scrivener.Cell, scrivener.Point, 3f, scrivener.Wcid);
            var shop = home.Shops.Add(scrivener.Guid, scrivener.BuysItemTypes);
            foreach (var ware in scrivener.Sells)
            {
                shop.Rows.Add((-1, ++row, ware.Name, ware.Wcid, MiscType, ware.Price));
            }
        }

        foreach (var focus in new[] { Enchantment, Artifice })
        {
            home.ShopOf(focus).ContainerTypes[focus] = FocusContainerType;
        }

        return home;
    }

    private static int HeldOf(Home home, uint wcid) => GearInventory.CountOf(home.Vendor.State, [], wcid);

    [Fact]
    public void A_bought_focus_comes_as_container_type_2_and_the_check_after_finds_it()
    {
        var home = FociHome();

        var status = home.Run(home.Start(new ResupplyOptions(false, null, null, home.Memory)), 2_000);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, HeldOf(home, Enchantment));
        Assert.True(home.Logged("foci: check after: Foci of Enchantment held 0 -> 1, paid 25000"));
        Assert.False(home.Logged("bought but not in the pack"));
        Assert.Empty(home.Memory.FociRefused);
    }

    [Fact]
    public void A_held_focus_is_not_bought_again_by_the_next_trip_or_called_for_by_the_counters()
    {
        var home = FociHome(140_000);
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(home.Start(new ResupplyOptions(false, null, null, home.Memory)), 2_000));
        Assert.Equal(1, HeldOf(home, Enchantment));
        var counters = new LogisticsCounters(home.Ground);
        counters.Check(home.Vendor.State);
        Assert.NotEqual(Enchantment, counters.FociDue(home.Vendor.State)?.Focus.Wcid); // the fight's read: the 20:53 trip was called here

        var fresh = new SaleMemory(); // a new process: the session's memory is gone, the 20:57 buy
        home.Vendor.StandAt(home.Ground.Home.LifestoneCell, home.Ground.Home.LifestonePoint); // a trip starts at home
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(home.Start(new ResupplyOptions(false, null, null, fresh)), 2_000));

        Assert.Equal(1, HeldOf(home, Enchantment));
        Assert.Equal(2, home.Lines.Count(l => l.Contains("foci: check before:", StringComparison.Ordinal))); // the Enchantment's, then the Artifice's, never the Enchantment's twice
        Assert.Equal(1, HeldOf(home, Artifice));
    }

    [Fact]
    public void A_slow_update_is_waited_for_and_is_no_refusal()
    {
        var home = FociHome();
        home.ShopOf(Enchantment).ArriveAfterPumps = 24; // 6 s at the rig's quarter second, past the old 3 s

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(home.Start(new ResupplyOptions(false, null, null, home.Memory)), 2_000));

        Assert.Equal(1, home.Lines.Count(l => l.Contains("resupply: bought 1 Foci of Enchantment", StringComparison.Ordinal)));
        Assert.Equal(1, HeldOf(home, Enchantment));
        Assert.True(home.Logged("foci: check after: Foci of Enchantment held 0 -> 1, paid 25000"));
        Assert.Empty(home.Memory.FociRefused);
    }

    [Fact]
    public void A_ware_that_never_shows_is_named_at_the_limit_and_not_bought_again_by_the_next_plan()
    {
        var home = new Home(Den, 2_000_000, held: l => l.TripUnder - 1);
        var line = home.Ground.Supplies.Stock.First(l => !home.Ground.IsCarried(l.Wcid));
        home.ShopOf(line.Wcid).ArriveAfterPumps = int.MaxValue;
        var start = () => home.Run(home.Start(new ResupplyOptions(false, null, null, home.Memory)), 2_000);

        Assert.Equal(ScenarioStatus.Succeeded, start());

        var boughtFirst = home.Lines.Count(l => l.Contains($"resupply: bought {line.Keep - (line.TripUnder - 1)} {line.Name} at", StringComparison.Ordinal));
        Assert.Equal(1, boughtFirst);
        Assert.True(home.Logged($"TODO(m6-bought-unseen): {line.Name}"));

        Assert.Equal(ScenarioStatus.Succeeded, start());

        Assert.Equal(1, home.Lines.Count(l => l.Contains($"resupply: bought {line.Keep - (line.TripUnder - 1)} {line.Name} at", StringComparison.Ordinal))); // the plan after held it
        Assert.True(home.Logged($"resupply: {line.Name}: held out of this plan"));
    }

    [Fact]
    public void A_spare_focus_is_not_sold_or_dropped_and_no_third_is_bought()
    {
        var home = FociHome(140_000);
        foreach (var _ in new[] { 1, 2 })
        {
            var guid = home.Vendor.NextGuid();
            home.Vendor.Arrive(guid, "Foci of Enchantment", Enchantment, MiscType, 500);
            home.Vendor.Put(guid, containerType: FocusContainerType);
        }

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(home.Start(new ResupplyOptions(true, null, null, home.Memory)), 2_000));

        Assert.Equal(2, HeldOf(home, Enchantment));
        Assert.Equal(0, home.Shops.Sells);
        Assert.Equal(0, home.Shops.Drops);
    }
}
