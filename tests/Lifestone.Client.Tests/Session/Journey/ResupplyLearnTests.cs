using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7j (D143, D141): the resupply's learn step on the den's profile against a scripted ACE: the scroll of Aura of Spirit Drinker Self IV bought at the Scrivener of Item Magic on the walk, read after it, the spellbook gaining
/// 3256 from the server's MagicUpdateSpell, and never bought again; a school too low refused before the buy with a typed reason; a refusal the server gives remembered, so no later trip tries again (no loop).
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint SpiritDrinkerIV = 3256;
    private const uint SpiritDrinkerScroll = 28005;
    private const uint Scrivener = 20214;

    /// <summary>A mage with Focus and Self 100 and Item Enchantment trained with <paramref name="itemRanks"/> ranks (current skill (100 + 100) / 4 + ranks), whose spellbook holds the build's spells.</summary>
    private static PlayerDescriptionMsg LearnMage(int itemRanks) => new()
    {
        Attributes = [new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
        Skills = [new SkillDesc(MageTemplate.ItemEnchantment, (ushort)itemRanks, 1, 2u, 0, 0u, 0, 0)],
        Spells = ArenaProfile.SpellsToLearn.Distinct().ToImmutableDictionary(id => id, _ => 2f),
    };

    /// <summary>ACE's answer to a Use of a scroll the pack holds (Scroll.ActOnUse, Scroll.cs 45-127 @95ef426d): with <paramref name="teach"/> the MagicUpdateSpell, the learn line, the scroll consumed and "The scroll is destroyed.", else the skill refusal; then the UseDone.</summary>
    private static Action ScrollServer(Home home, bool teach, List<uint> uses)
    {
        var seen = 0;
        uint sequence = 40;
        return () =>
        {
            home.Shops.Pump();
            var sends = home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                var send = sends[seen];
                if (send.Kind != FakeSendKind.Use || !home.Vendor.State.Objects.TryGet(send.Guid, out var item) || item.Weenie.WeenieClassId != SpiritDrinkerScroll)
                {
                    continue;
                }

                uses.Add(send.Guid);
                if (teach)
                {
                    home.Vendor.R.Raw(AceM3Writers.MagicUpdateSpell(VendorRig.Player, ++sequence, (ushort)SpiritDrinkerIV));
                    home.Vendor.H.ServerMessage("You learn the Aura of Spirit Drinker Self IV spell.\n");
                    home.Vendor.Remove(send.Guid);
                    home.Vendor.H.ServerMessage("The scroll is destroyed.");
                }
                else
                {
                    home.Vendor.H.ServerMessage("You are not skilled enough in Item Enchantment to learn this spell.");
                }

                home.Vendor.UseDone();
            }
        };
    }

    [Fact]
    public void The_scroll_the_spellbook_lacks_is_bought_at_the_scrivener_read_and_learned_and_never_bought_again()
    {
        var home = new Home(Den, 2_000_000, description: LearnMage(itemRanks: 200)); // Item Enchantment 250: IV's power 150 needs 100
        var uses = new List<uint>();
        Assert.False(home.Vendor.State.Self.Spellbook.Knows(SpiritDrinkerIV));
        var memory = new SaleMemory();

        var server = ScrollServer(home, teach: true, uses); // one scripted server for the home's trips: it answers each send once
        var run = home.Start(new ResupplyOptions(Memory: memory));
        var status = home.Stations.Run(run, 600, pump: server);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var scrivener = home.Ground.VendorOf(Scrivener)!;
        Assert.Contains(run.Visits, v => v.Vendor.Wcid == Scrivener && v.Buys.Single().Line.Wcid == SpiritDrinkerScroll); // she is a stop of the walk
        Assert.Contains(home.Travel.Requests, r => r.Destination.Name == scrivener.Name);
        Assert.Equal(1, run.ScrollsBought);
        Assert.Single(uses); // read once
        Assert.Equal(1, run.Learned);
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(SpiritDrinkerIV)); // the after-check: the server's update, in the state
        Assert.True(home.Logged("learn: Aura of Spirit Drinker Self IV (3256): not in the spellbook, ItemEnchantment skill 250, power 150 needs 100: buy its scroll (weenie 28005"));
        Assert.True(home.Logged("learn: learned Aura of Spirit Drinker Self IV (3256): the spellbook holds it (the server's update); ACE said: You learn the Aura of Spirit Drinker Self IV spell."));
        Assert.True(home.Logged("resupply: learn: learned=1 scrolls_bought=1 learn_refused=none"));
        Assert.DoesNotContain(home.Lines, l => l.Contains("TODO(m6-scroll-read)", StringComparison.Ordinal));

        // The next trip: the spellbook holds the spell, so the scroll is never bought again and the scrivener is no stop.
        var buysBefore = home.Shops.Buys;
        var again = home.Start(new ResupplyOptions(Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 400, pump: server));
        Assert.DoesNotContain(again.Visits, v => v.Vendor.Wcid == Scrivener);
        Assert.Equal(buysBefore, home.Shops.Buys);
        Assert.Single(uses);
        Assert.Equal((0, 0), (again.ScrollsBought, again.Learned));
        Assert.True(home.Logged("learn: Aura of Spirit Drinker Self IV (3256): known: the spellbook holds it: the scroll is never bought again"));
    }

    [Fact]
    public void A_school_short_of_ACEs_requirement_buys_no_scroll_and_says_skill_short()
    {
        var home = new Home(Den, 2_000_000, description: LearnMage(itemRanks: 0)); // Item Enchantment 50: IV's power 150 needs 100 (Player.CanReadScroll)
        var uses = new List<uint>();

        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        var status = home.Stations.Run(run, 400, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(LearnReason.SkillShort, ResupplyRun.CanRead(home.Vendor.State.Self.Qualities, DatReaderWriter.Enums.MagicSchool.ItemEnchantment, 150, out _));
        Assert.DoesNotContain(run.Visits, v => v.Vendor.Wcid == Scrivener); // no walk out of town for a scroll the mage cannot read
        Assert.Equal((0, 0), (run.ScrollsBought, run.Learned));
        Assert.Empty(uses);
        Assert.True(home.Logged("learn: Aura of Spirit Drinker Self IV (3256): skill-short: ItemEnchantment skill 50, power 150 needs 100"));
        Assert.False(home.Vendor.State.Self.Spellbook.Knows(SpiritDrinkerIV));
    }

    [Fact]
    public void A_learn_the_server_refuses_is_logged_with_its_reason_and_no_later_trip_tries_again()
    {
        var home = new Home(Den, 2_000_000, description: LearnMage(itemRanks: 200));
        var uses = new List<uint>();
        var memory = new SaleMemory();

        var server = ScrollServer(home, teach: false, uses);
        var run = home.Start(new ResupplyOptions(Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: server)); // ACE says the skill is short (its own reading of the skill)

        Assert.Single(uses);
        Assert.Equal((1, 0), (run.ScrollsBought, run.Learned));
        Assert.Equal(LearnReason.SkillShort, memory.LearnRefused[SpiritDrinkerIV]);
        Assert.Equal(["3256:skill-short"], run.LearnRefusals);
        Assert.Contains("learn", run.Refusals);
        Assert.True(home.Logged("refused: learn: Aura of Spirit Drinker Self IV (3256): skill-short: the spellbook does not hold it after the use (waited up to 15 s); ACE said: You are not skilled enough in Item Enchantment to learn this spell.; not tried again this session (no loop)"));

        // The next trip holds the scroll still, but the refusal is remembered: no use, no buy, no walk to her.
        var buysBefore = home.Shops.Buys;
        var again = home.Start(new ResupplyOptions(Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 400, pump: server));
        Assert.Single(uses);
        Assert.Equal(buysBefore, home.Shops.Buys);
        Assert.True(home.Logged("learn: Aura of Spirit Drinker Self IV (3256): refused-before: refused earlier this session (skill-short): not tried again"));
    }
    /// <summary>
    /// M5 7 P7l (D143): the walk's order when it starts outdoors. Live trip check 3 (2026-10-30 06:13 UTC) walked the shortest walk's order from the lifestone, Renald, Cindrue, Monyra, then the Scrivener of Item Magic outdoors,
    /// and the last leg stuck at the shut door of Archmage Cindrue's entry hall, so the scroll was never bought. From the lifestone (outdoors) the stop outdoors comes first and the stops in buildings keep their order;
    /// a walk that starts in a building keeps the shortest walk's order. Each decision names its reason.
    /// </summary>
    [Fact]
    public void A_walk_starting_outdoors_visits_the_Scrivener_outdoors_first_and_the_shops_keep_the_shortest_walks_order()
    {
        var ground = GroundCatalog.Get(Den);
        GroundVendor Named(string name) => ground.Vendors.First(v => v.Name == name);
        var scroll = new ResupplyBuy(new StockLine(SpiritDrinkerScroll, "Aura of Spirit Drinker Self IV", 1, 1, 1), Named("Scrivener of Item Magic"), 0, 1);
        var taper = new ResupplyBuy(new StockLine(20631, "Prismatic Taper", 1000, 200, 12), Named("Archmage Cindrue"), 437, 563);
        var sellers = new List<(GroundVendor, int)> { (Named("Shopkeeper Renald the Elder"), 37), (Named("Archmage Cindrue"), 10), (Named("Monyra the Jeweler"), 4) };
        var shortest = ResupplyPlanner.Order([scroll, taper], sellers, ResupplyPlanner.WorldMetres(ground.Home.LifestoneCell, ground.Home.LifestonePoint));
        Assert.Equal(["Shopkeeper Renald the Elder", "Archmage Cindrue", "Monyra the Jeweler", "Scrivener of Item Magic"], shortest.Select(v => v.Vendor.Name)); // live check 3's order

        var outdoors = ResupplyPlanner.OutdoorStopsFirst(shortest, ground.Home.LifestoneCell, out var why);

        Assert.Equal(["Scrivener of Item Magic", "Shopkeeper Renald the Elder", "Archmage Cindrue", "Monyra the Jeweler"], outdoors.Select(v => v.Vendor.Name));
        Assert.Equal(shortest.Where(v => v.Sell).Select(v => (v.Vendor.Name, v.SellCount)), outdoors.Where(v => v.Sell).Select(v => (v.Vendor.Name, v.SellCount))); // the loot buyers' sale order is unchanged
        Assert.Equal($"the walk starts outdoors (0x{ground.Home.LifestoneCell:X8}): the stops outdoors (Scrivener of Item Magic) first, so no buy or learn there waits on leaving a building; the stops in buildings keep the shortest walk's order", why);

        // From Cindrue's entry hall (a trip that starts in a building) the shortest walk's order stands, with its reason.
        Assert.Same(shortest, ResupplyPlanner.OutdoorStopsFirst(shortest, 0xA9B40117, out why));
        Assert.Equal("the walk starts indoors (0xA9B40117): the shortest walk's order", why);

        // No stop outdoors: the order stands.
        var shops = ResupplyPlanner.Order([taper], sellers, ResupplyPlanner.WorldMetres(ground.Home.LifestoneCell, ground.Home.LifestonePoint));
        Assert.Same(shops, ResupplyPlanner.OutdoorStopsFirst(shops, ground.Home.LifestoneCell, out why));
        Assert.Contains("no stop is outdoors", why, StringComparison.Ordinal);
    }

    /// <summary>M5 7 P7l (D143): the trip that learns the spell walks to the Scrivener first from the lifestone, and logs the order's decision.</summary>
    [Fact]
    public void The_learning_trip_from_the_lifestone_walks_to_the_Scrivener_first_and_logs_why()
    {
        var home = new Home(Den, 2_000_000, held: line => line.Wcid == 20631 ? 100 : line.Keep, description: LearnMage(itemRanks: 200)); // tapers under their trip line: Archmage Cindrue is a stop too
        var uses = new List<uint>();
        var server = ScrollServer(home, teach: true, uses);
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: server));

        Assert.True(run.Visits.Count > 1, string.Join(", ", run.Visits.Select(v => v.Vendor.Name)));
        Assert.Equal(Scrivener, run.Visits[0].Vendor.Wcid);
        Assert.True(home.Logged("resupply: the walk's order: the walk starts outdoors"), string.Join(" | ", home.Lines.Where(l => l.StartsWith("resupply: the walk", StringComparison.Ordinal))));
        Assert.True(home.Logged("the stops outdoors (Scrivener of Item Magic) first"));
        Assert.Equal(home.Ground.VendorOf(Scrivener)!.Name, home.Travel.Requests.First(r => r.Purpose == TravelPurpose.Vendor).Destination.Name); // the first vendor walk is hers
        Assert.True(run.Learned == 1, "the scroll bought first is read after the walk");
    }
}
