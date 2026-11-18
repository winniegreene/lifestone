using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 1 c (D41, D40, D42): the learn trip by the spell plan on Holtburg's outskirts against a scripted ACE. What these prove: a level-6 climber whose current War Magic (106) casts Flame Bolt II at 0.98 and III at 0.60 is due a learn
/// (the counters' trigger <c>learn:Flame Bolt II</c>), and the resupply buys the scroll 5998 at Holtburg's Apprentice Scrivener 20216 (the catalog's scrivener in the ground's vendor view, 250 pyreals), reads it with check, act, check and
/// the book gains 81, after which nothing is due; a V target (War Magic 250) is sold only at the Master Scriveners' towns, so it goes by the Town Network (M6 4 t4a): Dryreach chosen and named on the plan's line, the hop's failure in
/// this rig (no portal object) named, remembered for the session and not tried again; the trip rule's choices (V: Dryreach; VI: Stonehold past Fort Tethana's lever gate; the refusals by name) and the lever-door file.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const string Outskirts = "holtburg-outskirts";
    private const uint FlameBoltI = 27;
    private const uint FlameBoltII = 81;
    private const uint FlameBoltIIScroll = 5998;
    private const uint Apprentice = 20216;
    private const uint WritableType = 0x2000;

    /// <summary>A level-6 climber: Focus and Self 100, War Magic trained with <paramref name="warRanks"/> ranks (current skill (100 + 100) / 4 + ranks), the book holding Flame Bolt I only.</summary>
    private static PlayerDescriptionMsg Climber(int warRanks) => new()
    {
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 6),
        Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)], // Strength 100: the counters judge the load
        Skills = [new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 2u, 0, 0u, 0, 0)],
        Spells = ImmutableDictionary<uint, float>.Empty.Add(FlameBoltI, 2f),
    };

    /// <summary>The home on the outskirts with the catalog's Holtburg scriveners placed and their shops open (the rig places only the profile's vendors).</summary>
    private static Home OutskirtsHome(int warRanks, int coins = 2_000_000, bool notes = true)
    {
        var home = new Home(Outskirts, coins, notes: notes, description: Climber(warRanks));
        uint row = 0x80009000;
        foreach (var scrivener in home.Ground.Scriveners)
        {
            home.Vendor.PlaceAt(scrivener.Guid, scrivener.Name, scrivener.Cell, scrivener.Point, 3f, scrivener.Wcid);
            var shop = home.Shops.Add(scrivener.Guid, scrivener.BuysItemTypes);
            foreach (var ware in scrivener.Sells)
            {
                shop.Rows.Add((-1, ++row, ware.Name, ware.Wcid, WritableType, ware.Price));
            }
        }

        return home;
    }

    /// <summary>ACE's answer to a Use of the Flame Bolt II scroll (Scroll.ActOnUse, Scroll.cs 45-127 @95ef426d): the MagicUpdateSpell, the learn line, the scroll consumed, then the UseDone.</summary>
    private static Action FlameBoltServer(Home home, List<uint> uses)
    {
        var seen = 0;
        uint sequence = 60;
        return () =>
        {
            home.Shops.Pump();
            var sends = home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                var send = sends[seen];
                if (send.Kind != FakeSendKind.Use || !home.Vendor.State.Objects.TryGet(send.Guid, out var item) || item.Weenie.WeenieClassId != FlameBoltIIScroll)
                {
                    continue;
                }

                uses.Add(send.Guid);
                home.Vendor.R.Raw(AceM3Writers.MagicUpdateSpell(VendorRig.Player, ++sequence, (ushort)FlameBoltII));
                home.Vendor.H.ServerMessage("You learn the Flame Bolt II spell.\n");
                home.Vendor.Remove(send.Guid);
                home.Vendor.H.ServerMessage("The scroll is destroyed.");
                home.Vendor.UseDone();
            }
        };
    }

    [Fact]
    public void A_level_6_climber_whose_War_Magic_casts_II_at_95_percent_buys_Flame_Bolt_II_at_Holtburgs_Apprentice_Scrivener_and_reads_it()
    {
        var home = OutskirtsHome(warRanks: 56); // War Magic 106: II (power 50) at 0.981, III (power 100) at 0.60 (D42's numbers)
        var counters = new LogisticsCounters(home.Ground);
        var before = counters.Check(home.Vendor.State);
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Learn, "learn:Flame Bolt II"), (before.Kind, before.Cause, before.Trigger)); // the trip trigger: readable and sold at home
        Assert.Contains("sold at Holtburg", before.Reason, StringComparison.Ordinal);

        var uses = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: FlameBoltServer(home, uses)));

        Assert.Contains(run.Visits, v => v.Vendor.Wcid == Apprentice && v.Buys.Single().Line.Wcid == FlameBoltIIScroll);
        Assert.Equal((1, 1), (run.ScrollsBought, run.Learned));
        Assert.Single(uses);
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FlameBoltII)); // the after-check: the server's update
        Assert.True(home.Logged("learn: plan: rung outskirts level 6: Flame Bolt II (81): due: WarMagic skill 106 (standing), plan chance 0.95: level II at the chance, rung outskirts's floor I: target Flame Bolt II (power 50, chance 0.981)"), string.Join(" | ", home.Lines.Where(l => l.Contains("learn", StringComparison.Ordinal))));
        Assert.True(home.Logged("(create-list row 78068 of 20216)"));
        Assert.True(home.Logged("buy its scroll (weenie 5998, 250 pyreals) at Apprentice Scrivener of Item and War Magic (0x7A9B300D) on the walk, read it after; his row's cell 0xA9B30000 has the low word 0"));
        Assert.True(home.Logged("learn: learned Flame Bolt II (81): the spellbook holds it"));
        Assert.True(home.Logged("resupply: learn: learned=1 scrolls_bought=1 learn_refused=none"));
        Assert.DoesNotContain(home.Lines, l => l.Contains("TODO(m6-scroll-read)", StringComparison.Ordinal));

        Assert.Equal(LogisticsKind.None, counters.Check(home.Vendor.State).Kind); // the book holds II: nothing is due
    }

    /// <summary>r2b's climber, moved to the Lodge's rung in M6 7 AC (the Burrow rung is dropped, and the outskirts' creatures carry no damage types): level 15, Focus and Self 100, War and Life Magic 118, the book holding Flame Bolt II and Armor Self I.</summary>
    private static PlayerDescriptionMsg BurrowClimber(params uint[] known)
    {
        var spells = ImmutableDictionary<uint, float>.Empty.Add(FlameBoltII, 2f).Add(24, 2f).Add(82, 2f); // M6 7 AR: the Burrow is a rung again, so its bolt line is planned: Flame Bolt III (82) is held and the test reads the lines after it
        foreach (var spell in known)
        {
            spells = spells.SetItem(spell, 2f);
        }

        return new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 15),
            Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
            Skills = [new SkillDesc(MageTemplate.WarMagic, 68, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.LifeMagic, 65, 1, 2u, 0, 0u, 0, 0)],
            Spells = spells,
        };
    }

    /// <summary>The Lodge's home (M6 7 AC: the ground of the rung that learns the protection lines) with the catalog's Holtburg scriveners placed, the stock at its keeps.</summary>
    private static Home BurrowHome(int coins, PlayerDescriptionMsg description)
    {
        var home = new Home("tusker-lodge", coins, notes: false, description: description);
        uint row = 0x80009000;
        foreach (var scrivener in home.Ground.Scriveners)
        {
            home.Vendor.PlaceAt(scrivener.Guid, scrivener.Name, scrivener.Cell, scrivener.Point, 3f, scrivener.Wcid);
            var shop = home.Shops.Add(scrivener.Guid, scrivener.BuysItemTypes);
            foreach (var ware in scrivener.Sells)
            {
                shop.Rows.Add((-1, ++row, ware.Name, ware.Wcid, WritableType, ware.Price));
            }
        }

        return home;
    }

    /// <summary>ACE's answer to a Use of any catalog scroll: the MagicUpdateSpell, the learn line, the scroll consumed, the UseDone.</summary>
    private static Action AnyScrollServer(Home home, List<uint> learned)
    {
        var seen = 0;
        uint sequence = 60;
        return () =>
        {
            home.Shops.Pump();
            var sends = home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                var send = sends[seen];
                if (send.Kind != FakeSendKind.Use || !home.Vendor.State.Objects.TryGet(send.Guid, out var item) || ScrivenerCatalog.Default.Spells.FirstOrDefault(s => s.ScrollWcid == item.Weenie.WeenieClassId) is not { } spell)
                {
                    continue;
                }

                learned.Add(spell.SpellId);
                home.Vendor.R.Raw(AceM3Writers.MagicUpdateSpell(VendorRig.Player, ++sequence, (ushort)spell.SpellId));
                home.Vendor.H.ServerMessage($"You learn the {spell.Name} spell.\n");
                home.Vendor.Remove(send.Guid);
                home.Vendor.H.ServerMessage("The scroll is destroyed.");
                home.Vendor.UseDone();
            }
        };
    }

    [Fact]
    public void M6_7_X_the_learn_trip_at_level_15_buys_and_reads_Armor_II_and_the_protection_II_first_and_the_budget_leaves_the_rest_waiting()
    {
        var home = BurrowHome(coins: 900, BurrowClimber(1676)); // the budget is 540: two scrolls of 250; M6 7 AD: she holds the press, which comes first
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Armor Self II", counters.Check(home.Vendor.State).Trigger);

        var learned = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: AnyScrollServer(home, learned)));

        Assert.Equal([1019u, 1308u], learned.Order());
        Assert.Equal((2, 2), (run.ScrollsBought, run.Learned));
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(1308));
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(1019));
        Assert.True(home.Logged("learn: Fire Vulnerability Other I (")); // M6 7 AK: Life 115 here, under the 0.75 bar's 116 for III: the debuff stands at I, which the budget still pays
        Assert.True(home.Logged("learn: plan: rung lodge level 15: Armor Self II (1308): due: LifeMagic skill 115 (standing)"));
    }

    /// <summary>M6 7 AC: the outskirts now hold levels 10 to 14 (the Burrow rung is dropped): the plan there is Armor Self only, no protection line (TODO(m6-outskirts-damage-types)); the Lodge's Male Tuskers name bludgeon, so its plan holds the bludgeon line. 0 deaths on the outskirts at levels 5 to 9 is why Armor alone stands.</summary>
    [Fact]
    public void M6_7_AC_the_outskirts_rung_at_level_12_plans_Armor_only_and_the_Lodge_adds_the_bludgeon_protection()
    {
        var ladder = Lifestone.Client.Session.Grounds.LadderLoader.WarMage;
        var outskirts = ScrivenerLearnPlan.RungOf(ladder, "holtburg-outskirts", 12)!;
        Assert.Equal("outskirts", outskirts.Key);
        Assert.Empty(ScrivenerLearnPlan.IncomingElements(Lifestone.Client.Session.Grounds.GroundCatalog.Get("holtburg-outskirts")));
        Assert.DoesNotContain(ScrivenerLearnPlan.LinesFor(outskirts, ScrivenerCatalog.Default), l => l.Kind == ScrivenerLineKind.Protection);
        Assert.Contains(ScrivenerLearnPlan.LinesFor(outskirts, ScrivenerCatalog.Default), l => l.Kind == ScrivenerLineKind.Armor);

        var lodge = ScrivenerLearnPlan.RungOf(ladder, "tusker-lodge", 15)!;
        Assert.Contains(ScrivenerLearnPlan.LinesFor(lodge, ScrivenerCatalog.Default), l => l.Kind == ScrivenerLineKind.Protection && l.Element == "bludgeon");
        Assert.Equal("burrow", ScrivenerLearnPlan.RungOf(ladder, "tusker-burrow", 16)!.Key); // M6 7 AR: the Burrow is a rung again
    }

    [Fact]
    public void M6_7_AD_the_press_is_the_first_thing_a_trip_buys_after_the_bolt_and_it_calls_a_trip_the_budget_pays()
    {
        var poor = BurrowHome(coins: 83, BurrowClimber()); // 60% of 83 is 49, under the scroll's 50
        Assert.NotEqual("learn:Stamina to Mana Self I", new LogisticsCounters(poor.Ground, learnHolds: new LearnHolds()).Check(poor.Vendor.State).Trigger);

        var home = BurrowHome(coins: 900, BurrowClimber());
        var counters = new LogisticsCounters(home.Ground, learnHolds: new LearnHolds());
        Assert.Equal("learn:Stamina to Mana Self I", counters.Check(home.Vendor.State).Trigger);

        var learned = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: AnyScrollServer(home, learned)));

        Assert.Contains(1676u, learned);
        Assert.Contains(1308u, learned); // 50 + 250 + 250 is over the budget of 540: the plan's order stands
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(1676));
    }

    [Fact]
    public void M6_7_AT_a_learn_trip_waits_for_the_next_two_scrolls_or_the_coin_line_and_not_for_each_one()
    {
        // The Burrow's first due scroll is the press (50), the next Armor Self II (250): 60% of the purse pays both from 500 coin, 300 in all.
        string Trigger(int coins) => new LogisticsCounters(BurrowHome(coins, BurrowClimber()).Ground, learnHolds: new LearnHolds()).Check(BurrowHome(coins, BurrowClimber()).Vendor.State).Trigger;

        Assert.NotEqual("learn:Stamina to Mana Self I", Trigger(100)); // pays the first, not the two
        Assert.NotEqual("learn:Stamina to Mana Self I", Trigger(499)); // 60% of 499 is 299, a pyreal under the two
        Assert.Equal("learn:Stamina to Mana Self I", Trigger(500));
        Assert.Equal("learn:Stamina to Mana Self I", Trigger(900));

        var line = BurrowHome(900, BurrowClimber()).Ground.Supplies.Purse.LearnTripCoin;
        Assert.Equal("learn:Stamina to Mana Self I", Trigger(line)); // the data coin line pays the trip whatever the batch costs
    }

    [Fact]
    public void M6_7_X_a_protective_scroll_the_budget_cannot_pay_calls_no_trip_and_the_recovery_line_never_does()
    {
        var poor = BurrowHome(coins: 400, BurrowClimber(1676)); // 60% of 400 is 240, under Armor Self II's 250
        var counters = new LogisticsCounters(poor.Ground, learnHolds: new LearnHolds());
        Assert.NotEqual("learn:Armor Self II", counters.Check(poor.Vendor.State).Trigger);

        var paid = BurrowHome(coins: 417, BurrowClimber(1676)); // M6 7 AT: 60% of 417 is 250, which pays the one scroll and not the batch of the next two
        Assert.NotEqual("learn:Armor Self II", new LogisticsCounters(paid.Ground, learnHolds: new LearnHolds()).Check(paid.Vendor.State).Trigger);
        var batch = BurrowHome(coins: GroundPurse.DefaultLearnTripCoin, BurrowClimber(1676)); // the data coin line pays the trip
        Assert.Equal("learn:Armor Self II", new LogisticsCounters(batch.Ground, learnHolds: new LearnHolds()).Check(batch.Vendor.State).Trigger);

        var rich = BurrowHome(coins: 100_000, BurrowClimber([1676, 1308, 1019, 1104, 1323, 15, 280, .. new[] { "Fire Vulnerability Other", "Imperil Other", "Vulnerability Other", "Magic Yield Other" }.Select(l => ScrivenerCatalog.Default.LineSpells(l)[0].SpellId)])); // every line but Mana Renewal is known
        var renewal = new LogisticsCounters(rich.Ground, learnHolds: new LearnHolds());
        Assert.Equal(LogisticsKind.Trip, renewal.Check(rich.Vendor.State).Kind); // M6 7 AN (the owner): a regeneration line the purse pays calls a trip like the protective lines
        Assert.NotNull(renewal.LearnDue(rich.Vendor.State));
    }

    [Fact]
    public void A_V_target_sold_only_off_home_goes_by_the_Town_Network_to_Dryreach_and_a_failed_hop_is_named_and_not_tried_again_M6_4_t4a()
    {
        var home = OutskirtsHome(warRanks: 200); // War Magic 250: V (power 200) at 0.97, VI (power 250) at 0.50
        Assert.Equal(LogisticsKind.None, new LogisticsCounters(home.Ground).Check(home.Vendor.State).Kind); // the counters' learn trigger is the home's (unchanged): the trip goes on another trip

        var memory = new SaleMemory();
        var run = home.Start(new ResupplyOptions(Loot: true, Memory: memory));
        var status = home.Stations.Run(run, 600, pump: home.Shops.Pump);
        var again = home.Start(new ResupplyOptions(Memory: memory));
        var status2 = home.Stations.Run(again, 600, pump: home.Shops.Pump);
        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("learn", StringComparison.Ordinal) || l.Contains("hop", StringComparison.Ordinal) || l.Contains("FAIL", StringComparison.Ordinal)));
        Assert.True((ScenarioStatus.Succeeded, ScenarioStatus.Succeeded) == (status, status2), dump);

        Assert.True(home.Logged("learn: plan: rung outskirts level 6: Flame Bolt V (84): off-home (WarMagic skill 250 (standing)"), dump);
        Assert.True(home.Logged("by the Town Network to Dryreach (travel=town-network)"), dump);
        Assert.True(home.Logged("at Master Scrivener of War Magic (0x7DA75035); chose Master Scrivener of War Magic in Dryreach: hops holtburg-to-network,network-to-dryreach, the drop 0xDA75002B"), dump);
        Assert.True(home.Logged("home by portals:dryreach (the first admitted); Westwatch refused off-network"), dump); // the rig's book lacks Lifestone Recall: home by the town's way back
        Assert.True(home.Logged("also admitted: Plateau Village"), dump);
        var trip = Assert.Single(home.Lines, l => l.Contains("learn_trip: ", StringComparison.Ordinal)); // one trip a session: the second run does not try again
        Assert.Contains("learn_trip: town=Dryreach vendors=20223 spells=84 hops=holtburg-to-network,network-to-dryreach travel=town-network bought=0 learned=0 home=portals:dryreach ok=no reason=hop:", trip, StringComparison.Ordinal);
        Assert.Contains(home.Lines, l => l.Contains("learn_trip_hop: n=1 edge=holtburg-to-network", StringComparison.Ordinal) && l.EndsWith("ok=no", StringComparison.Ordinal));
        Assert.True(home.Logged("walking: purpose=home (the learn trip to Dryreach: the book lacks Lifestone Recall; the rung change direction=home by portals to holtburg)"), dump);
        Assert.Equal(LearnReason.TownTrip, memory.LearnRefused[84]);
        Assert.Contains("84:town-trip", run.LearnRefusals);
        Assert.True(home.Logged("Flame Bolt V (84): off-home: refused-before: refused earlier this session (town-trip): not tried again"), dump);
        Assert.Equal((0, 0), (run.ScrollsBought, run.Learned));
        Assert.DoesNotContain(run.Visits.Concat(again.Visits), v => ScrivenerCatalog.Default.ScrivenerOf(v.Vendor.Wcid) is not null); // no home scrivener sells it: none walked at home
        Assert.False(home.Vendor.State.Self.Spellbook.Knows(84));
    }

    [Fact]
    public void M6_14_at_her_shape_Revitalize_Self_VI_goes_by_the_Town_Network_to_Stonehold_s_Grand_Master_Scrivener_of_Life_Magic()
    {
        // Her live shape (ladder19, 2026-11-15): Life Magic 354 (ranks 304), Mana Conversion 264 (ranks 231), stamina 287, mana 391; the book holds Flame Bolt I and Stamina to Mana Self I to VI and no Revitalize Self. The home's scriveners
        // are not placed and the coin pays no 50,000 scroll, so the trip names Stonehold's scrolls and stops there; what this proves is the route: the plan's line, the town and the seller.
        var spells = ImmutableDictionary<uint, float>.Empty.Add(FlameBoltI, 2f);
        foreach (var id in Lifestone.Client.Session.Vitals.VitalsMeans.StaminaToManaSelf.Take(6))
        {
            spells = spells.Add(id, 2f);
        }

        var description = Climber(0) with
        {
            Skills = [new SkillDesc(MageTemplate.WarMagic, 0, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.LifeMagic, 304, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ManaConversion, 231, 1, 2u, 0, 0u, 0, 0)],
            Vitals = [new VitalDesc(MageTemplate.MaxStamina, 0, 287, 0, 287), new VitalDesc(MageTemplate.MaxMana, 0, 291, 0, 391)],
            Spells = spells,
        };
        var home = new Home(Outskirts, 25_000, notes: false, description: description);
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        var status = home.Stations.Run(run, 900, pump: home.Shops.Pump);
        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("Revitalize", StringComparison.Ordinal) || l.Contains("learn_trip:", StringComparison.Ordinal)));
        Assert.True(status == ScenarioStatus.Succeeded, dump);

        var plan = Assert.Single(home.Lines, l => l.Contains("learn: plan: rung outskirts level 6: Revitalize Self VI (1182): off-home (", StringComparison.Ordinal));
        Assert.Contains("a round on Mana Conversion 264 at stamina 287 (the standing maximum): Revitalize Self VI restores 120 stamina", plan, StringComparison.Ordinal);
        Assert.Contains("): by the Town Network to Stonehold (travel=town-network), 50000 pyreals at Grand Master Scrivener of Life Magic (0x", plan, StringComparison.Ordinal);
        Assert.Contains(home.Lines, l => l.Contains("learn_trip: town=Stonehold ", StringComparison.Ordinal) && l.Contains("20220", StringComparison.Ordinal) && l.Contains("1182", StringComparison.Ordinal));
    }

    /// <summary>The trip rule's candidates for a spell, as the station builds them: every catalog place of every scrivener who sells it, the towns by name.</summary>
    private static List<TownTripCandidate> LearnCandidates(uint spellId)
    {
        var catalog = ScrivenerCatalog.Default;
        return [.. catalog.Spell(spellId)!.Sellers.Select(s => catalog.ScrivenerOf(s.VendorWcid)).OfType<Scrivener>().SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).OrderBy(k => k.Town, StringComparer.Ordinal)];
    }

    private static TownTripVerdict VerdictOf(TownTripChoice choice, string town) => choice.Verdicts.First(v => v.Candidate.Town == town);

    [Fact]
    public void The_town_for_a_V_is_the_first_Master_Scrivener_town_on_the_Network_Dryreach_hopped_from_Holtburg_M6_4_t4a()
    {
        var routes = Lifestone.Client.Session.Travel.TravelRoutes.Default;
        var choice = TownNetworkTrip.Choose(LearnCandidates(84), new TownTripInput("holtburg", 30, KnowsLifestoneRecall: true, HomeIsTravelStart: true), routes, LeverDoors.Default);

        var plan = Assert.IsType<TownTripPlan>(choice.Chosen);
        Assert.Null(choice.Stop);
        Assert.Equal(("Dryreach", "Master Scrivener of War Magic", 0x7DA75035u), (plan.Candidate.Town, plan.Candidate.Name, plan.Candidate.Vendor.Guid));
        Assert.Equal(["holtburg-to-network", "network-to-dryreach"], plan.Hops.Select(h => h.Key));
        Assert.Equal([(ushort)0xA9B4, (ushort)0xABB1, (ushort)0xAAB1], plan.Hops[0].FromLandblocks); // the hop leg's place: Holtburg's (0xAAB1, M6 8 B2a: the Redoubt portal's block), then the Network's
        Assert.Equal([(ushort)0x0007], plan.Hops[1].FromLandblocks);
        Assert.Equal((0x0007u, 0xDA75u), (plan.Hops[0].DestLandblock, plan.Hops[1].DestLandblock));
        Assert.Equal((0xDA75002Bu, "dryreach"), (plan.DropCell, plan.Place.Key));
        Assert.InRange(plan.DropMetres, 30, 50); // 41 m from the drop (132, 60) to him (163.0, 87.1)
        Assert.True(plan.HomeByRecall);
        Assert.Equal("off-network", VerdictOf(choice, "Westwatch").Refusal);
        Assert.NotNull(VerdictOf(choice, "Plateau Village").Plan);
        Assert.NotNull(VerdictOf(choice, "Qalaba'r").Plan);

        // The way home without the recall: the town's own way back by portals (the rung change's direction=home), to the travel file's start.
        var noRecall = TownNetworkTrip.Choose(LearnCandidates(84), new TownTripInput("holtburg", 14, KnowsLifestoneRecall: false, HomeIsTravelStart: true), routes, LeverDoors.Default);
        Assert.Equal("portals:dryreach", noRecall.Chosen!.Home);
        Assert.False(noRecall.Chosen.HomeByRecall);
    }

    [Fact]
    public void The_town_for_a_VI_passes_Fort_Tethanas_lever_gate_for_Stonehold_M6_4_t4a()
    {
        var routes = Lifestone.Client.Session.Travel.TravelRoutes.Default;
        var input = new TownTripInput("holtburg", 43, KnowsLifestoneRecall: true, HomeIsTravelStart: true);
        var choice = TownNetworkTrip.Choose(LearnCandidates(85), input, routes, LeverDoors.Default); // Flame Bolt VI: the Grand Master Scriveners of War Magic

        Assert.Equal(("Stonehold", 0x764D503Cu), (choice.Chosen!.Candidate.Town, choice.Chosen.Candidate.Vendor.Guid));
        Assert.Equal(["holtburg-to-network", "network-to-stonehold"], choice.Chosen.Hops.Select(h => h.Key));
        var tethana = VerdictOf(choice, "Fort Tethana");
        Assert.Equal("lever-door:fort-tethana-gate", tethana.Refusal);
        Assert.Contains("is behind Fort Tethana's door 0x72581042, which opens only by its lever 0x72581044 (TODO(door-lever))", tethana.Why, StringComparison.Ordinal);
        Assert.Contains("Fort Tethana refused lever-door:fort-tethana-gate", choice.Why, StringComparison.Ordinal);
        Assert.All(new[] { "Ayan Baqur", "Candeth Keep", "Eastwatch", "Kara", "Wai Jhou" }, t => Assert.Equal("off-network", VerdictOf(choice, t).Refusal));

        // The door is what refuses it: without the lever-door file Fort Tethana is the first Grand Master town on the Network.
        Assert.Equal("Fort Tethana", TownNetworkTrip.Choose(LearnCandidates(85), input, routes, LeverDoors.None).Chosen!.Candidate.Town);
    }

    [Fact]
    public void A_trip_with_no_way_out_or_home_is_refused_by_name_M6_4_t4a()
    {
        var routes = Lifestone.Client.Session.Travel.TravelRoutes.Default;

        // From Fort Tethana (the Temple 40+ home): its own Portal to Town Network stands behind the lever gate, so every trip's first hop is refused.
        var fromTethana = TownNetworkTrip.Choose(LearnCandidates(85), new TownTripInput("fort-tethana", 45, KnowsLifestoneRecall: true, HomeIsTravelStart: false), routes, LeverDoors.Default);
        Assert.Null(fromTethana.Chosen);
        Assert.Equal("TODO(door-lever)", fromTethana.Stop);
        Assert.Equal("lever-door:fort-tethana-gate", VerdictOf(fromTethana, "Stonehold").Refusal);
        Assert.Contains("Fort Tethana's Portal to Town Network 0x7258104B is behind", VerdictOf(fromTethana, "Stonehold").Why, StringComparison.Ordinal);

        // Without Lifestone Recall a ground whose home is not the travel file's start has no way home: the portals lead to Holtburg.
        var noHome = TownNetworkTrip.Choose(LearnCandidates(85), new TownTripInput("holtburg", 45, KnowsLifestoneRecall: false, HomeIsTravelStart: false), routes, LeverDoors.Default);
        Assert.Equal("no-way-home", VerdictOf(noHome, "Stonehold").Refusal);
        Assert.Equal("town-network:no-town", TownNetworkTrip.Choose(LearnCandidates(84), new TownTripInput("holtburg", 45, KnowsLifestoneRecall: false, HomeIsTravelStart: false), routes, LeverDoors.Default).Stop);

        // A home lifestone in no travel place: no route.
        Assert.Equal("no-route", VerdictOf(TownNetworkTrip.Choose(LearnCandidates(84), new TownTripInput(null, 45, true, true), routes, LeverDoors.Default), "Dryreach").Refusal);
        Assert.Equal("holtburg", TownNetworkTrip.PlaceKeyOf(0xA9B4, routes));
        Assert.Null(TownNetworkTrip.PlaceKeyOf(0x1234, routes));
    }

    [Fact]
    public void The_lever_door_file_names_Fort_Tethanas_gate_its_lever_and_everything_behind_it_M6_4_t4a()
    {
        var door = Assert.Single(LeverDoors.Default.Doors);
        Assert.Equal(("fort-tethana-gate", "Fort Tethana", 0x72581042u, 2179u, 0x72581044u, 2609u, 298488), (door.Key, door.Town, door.DoorGuid, door.DoorWcid, door.LeverGuid, door.LeverWcid, door.Link));
        Assert.Equal(0x25810000u, door.DoorCell);
        var tethanaScriveners = ScrivenerCatalog.Default.Scriveners.SelectMany(s => s.Places).Where(p => p.Town == "Fort Tethana").Select(p => p.Guid);
        var portal = Lifestone.Client.Session.Travel.TravelRoutes.Default.Edge("fort-tethana-to-network").Guid;
        Assert.Equal(tethanaScriveners.Append(portal).Order(), door.Behind.Order()); // every Grand Master of the town and its Network portal (TownNetworkTripDatTests proves each out of reach from the drop)
        Assert.Null(LeverDoors.Default.Before(0x72481001)); // Zisaha's walk passes no lever door
        Assert.Contains("landblock_instance_link id 298488", door.Source, StringComparison.Ordinal);

        var bad = Assert.Throws<LeverDoorsException>(() => LeverDoors.Parse("""{"key":"x","name":"x","source":"x","doors":[],"extra":1}"""));
        Assert.Equal("$.extra", bad.JsonPath);
    }

    private const uint FlameBoltVIISpell = 2128;
    private const uint FlameBoltVIIScroll = 7001; // the looted scroll's weenie (a stand-in id: the scripted server answers by it)

    /// <summary>A looted scroll the pack holds (the F745 with its spell id and the pack list naming it).</summary>
    private static void OwnLooted(Home home, uint guid, string name, uint wcid, uint spellId)
    {
        var rig = home.Vendor;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: WritableType);
        var weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value, WeenieClassId = wcid, ContainerId = VendorRig.Player, Value = 100_000, SpellId = (ushort)spellId };
        rig.H.R.Reducer.OnCreateObject(rig.H.R.Context, basis with { Weenie = weenie }, isUpdate: false);
        rig.Put(guid);
    }

    /// <summary>ACE's answer to a Use of the looted scroll: with <paramref name="teach"/> the MagicUpdateSpell, the learn line and the scroll consumed, else the skill refusal; then the UseDone.</summary>
    private static Action LootedScrollServer(Home home, uint wcid, uint spellId, string spellName, bool teach, List<uint> uses)
    {
        var seen = 0;
        uint sequence = 90;
        return () =>
        {
            home.Shops.Pump();
            var sends = home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                var send = sends[seen];
                if (send.Kind != FakeSendKind.Use || !home.Vendor.State.Objects.TryGet(send.Guid, out var item) || item.Weenie.WeenieClassId != wcid)
                {
                    continue;
                }

                uses.Add(send.Guid);
                if (teach)
                {
                    home.Vendor.R.Raw(AceM3Writers.MagicUpdateSpell(VendorRig.Player, ++sequence, (ushort)spellId));
                    home.Vendor.H.ServerMessage($"You learn the {spellName} spell.\n");
                    home.Vendor.Remove(send.Guid);
                    home.Vendor.H.ServerMessage("The scroll is destroyed.");
                }
                else
                {
                    home.Vendor.H.ServerMessage("You are not skilled enough in War Magic to learn this spell.");
                }

                home.Vendor.UseDone();
            }
        };
    }

    [Fact]
    public void A_looted_VII_Flame_Bolt_in_the_pack_is_kept_by_the_rule_and_read_on_the_next_trip_with_the_book_proving_it()
    {
        var home = OutskirtsHome(warRanks: 0); // War Magic 50: the plan buys nothing (I is known); the VII needs no skill (power 300)
        OwnLooted(home, 0x80008001, "Scroll of Ilservian's Flame", FlameBoltVIIScroll, FlameBoltVIISpell);
        Assert.False(home.Vendor.State.Self.Spellbook.Knows(FlameBoltVIISpell));

        var uses = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: LootedScrollServer(home, FlameBoltVIIScroll, FlameBoltVIISpell, "Ilservian's Flame", teach: true, uses)));

        Assert.Single(uses);
        Assert.Equal((0, 1), (run.ScrollsBought, run.Learned)); // no buy; one read
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FlameBoltVIISpell)); // the after-check: the server's update
        Assert.True(home.Logged("learn: held scroll Scroll of Ilservian's Flame (0x80008001, weenie 7001): scroll of Ilservian's Flame"), string.Join(" | ", home.Lines.Where(l => l.Contains("held scroll", StringComparison.Ordinal))));
        Assert.True(home.Logged("level 7 of the plan's Flame Bolt line, a VII, kept always (D4)"));
        Assert.True(home.Logged("learn: learned Ilservian's Flame (2128): the spellbook holds it"));
        Assert.True(home.Logged("resupply: learn: learned=1 scrolls_bought=0 learn_refused=none"));

        // The next trip: the book holds it, so nothing is read again.
        var again = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 400, pump: home.Shops.Pump));
        Assert.Single(uses);
        Assert.Equal(0, again.Learned);
    }

    [Fact]
    public void A_kept_scroll_parked_in_a_side_pack_is_planned_and_read_M6_1_fix_1_R2()
    {
        var home = OutskirtsHome(warRanks: 0);
        var rig = home.Vendor;
        const uint pack = 0x80008100;
        rig.Arrive(pack, "Review Pack", 136, 0x200, 65);
        rig.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 9000, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
        var basis = ReducerHarness.Create(0x80008101, ReducerHarness.Stamps(), null, name: "Scroll of Ilservian's Flame", itemType: WritableType);
        var weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value, WeenieClassId = FlameBoltVIIScroll, ContainerId = pack, Value = 100_000, SpellId = (ushort)FlameBoltVIISpell };
        rig.H.R.Reducer.OnCreateObject(rig.H.R.Context, basis with { Weenie = weenie }, isUpdate: false);
        rig.Put(0x80008101, pack);
        Assert.Contains(home.Vendor.State.Self.Inventory.ItemsIn(pack), e => e.Guid == 0x80008101); // the scroll is in the side pack, not the main pack
        Assert.DoesNotContain(home.Vendor.State.Self.Inventory.Pack, e => e.Guid == 0x80008101);

        var uses = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: LootedScrollServer(home, FlameBoltVIIScroll, FlameBoltVIISpell, "Ilservian's Flame", teach: true, uses)));

        Assert.Single(uses);
        Assert.Equal((0, 1), (run.ScrollsBought, run.Learned));
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FlameBoltVIISpell));
        Assert.True(home.Logged("learn: held scroll Scroll of Ilservian's Flame (0x80008101, weenie 7001)"), string.Join(" | ", home.Lines.Where(l => l.Contains("held scroll", StringComparison.Ordinal))));
    }

    [Fact]
    public void A_looted_scroll_ACE_refuses_is_named_once_and_no_later_trip_tries_again()
    {
        var home = OutskirtsHome(warRanks: 0);
        OwnLooted(home, 0x80008002, "Scroll of Ilservian's Flame", FlameBoltVIIScroll, FlameBoltVIISpell);
        var uses = new List<uint>();
        var memory = new SaleMemory();
        var server = LootedScrollServer(home, FlameBoltVIIScroll, FlameBoltVIISpell, "Ilservian's Flame", teach: false, uses); // ACE's own reading of the skill disagrees

        var run = home.Start(new ResupplyOptions(Loot: true, Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: server));
        Assert.Single(uses);
        Assert.Equal(0, run.Learned);
        Assert.Equal(LearnReason.SkillShort, memory.LearnRefused[FlameBoltVIISpell]);
        Assert.Equal([$"{FlameBoltVIISpell}:skill-short"], run.LearnRefusals);

        var again = home.Start(new ResupplyOptions(Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 400, pump: server));
        Assert.Single(uses); // no loop
        Assert.True(home.Logged("was refused earlier this session (skill-short): not tried again"));
    }

    [Fact]
    public void A_held_scroll_the_rule_does_not_keep_is_left_in_the_pack_with_its_reason()
    {
        var home = OutskirtsHome(warRanks: 0); // War Magic 50: a VI (power 250 needs 200) is not readable
        OwnLooted(home, 0x80008003, "Scroll of Flame Bolt VI", 7002, 85);
        var uses = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: LootedScrollServer(home, 7002, 85, "Flame Bolt VI", teach: true, uses)));

        Assert.Empty(uses);
        Assert.Equal(0, run.Learned);
        Assert.True(home.Logged("learn: held scroll Scroll of Flame Bolt VI (0x80008003, spell 85): left in the pack, NotNext"));
    }

    [Fact]
    public void A_learn_trip_that_bought_nothing_holds_its_trigger_until_the_coin_covers_the_scroll_M6_1_d()
    {
        // M6 7 AT: the counters call a learn trip only on the coin of the batch, so a trip that finds the purse short is the coin spent in flight: its hold (the station's own line, covered by the coin-in-hand case below) is set here.
        var home = OutskirtsHome(warRanks: 56, coins: 2_000, notes: false); // Flame Bolt II is due; its scroll is 250 pyreals
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);
        home.Vendor.Coins(100); // the coin is spent before the trip reaches the scrivener
        home.Vendor.Tick();

        Assert.True(holds.Hold(FlameBoltII, 250, 6)); // the trip bought no scroll: purse short
        Assert.Equal(1, holds.Count);
        Assert.NotEqual(LogisticsCause.Learn, counters.Check(home.Vendor.State).Cause);

        home.Vendor.Coins(2_000); // the purse has grown past the scroll's price and the batch's
        home.Vendor.Tick();
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);
        Assert.Equal(0, holds.Count); // released

        Assert.True(holds.Hold(FlameBoltII, 250, 6));
        Assert.False(holds.Hold(FlameBoltII, 250, 6)); // already held: logged once
        Assert.False(holds.Holds(FlameBoltII, 100, 7)); // the next level-up releases it
    }

    [Fact]
    public void A_learn_trip_that_bought_nothing_with_the_coin_in_hand_holds_until_the_level_rises_M6_1_fix_1_R1()
    {
        var home = OutskirtsHome(warRanks: 56, coins: 9000, notes: false); // the purse covers the 250: the cause is not the purse
        var holds = new LearnHolds();
        var counters = new LogisticsCounters(home.Ground, learnHolds: holds);
        Assert.Equal("learn:Flame Bolt II", counters.Check(home.Vendor.State).Trigger);

        var context = new JourneyContext
        {
            Character = "BSD Mage",
            Account = "test",
            Log = home.Vendor.H.Log,
            Travel = home.Travel,
            Spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(),
            Dungeon = home.Ground,
        };
        var run = new LogisticsStation(resupply: new ResupplyOptions(Memory: new SaleMemory()), learnHolds: holds).Start(context);
        home.Stations.Run(run, 600, pump: () => { }); // no shop answers: the scrivener's list never arrives, so no scroll is bought

        Assert.Equal(1, holds.Count);
        Assert.Single(home.Lines, l => l.Contains("logistics: learn hold: the trip bought no scroll of Flame Bolt II (81; its price 250, the coin 9000, level 6; cause: ", StringComparison.Ordinal) && !l.Contains("cause: purse short", StringComparison.Ordinal) && l.Contains("waits until the level rises", StringComparison.Ordinal));
        Assert.NotEqual(LogisticsCause.Learn, counters.Check(home.Vendor.State).Cause); // held although the coin covers the price
        Assert.Equal(1, holds.Count);

        home.Vendor.Coins(900_000); // more coin does not release it: the purse was not the reason
        home.Vendor.Tick();
        Assert.NotEqual(LogisticsCause.Learn, counters.Check(home.Vendor.State).Cause);
        Assert.Equal(1, holds.Count);
    }

    [Fact]
    public void A_hold_by_a_cause_other_than_the_purse_releases_on_the_level_only_and_a_purse_hold_on_coin_too_M6_1_fix_1_R1()
    {
        var holds = new LearnHolds();
        Assert.True(holds.Hold(FlameBoltII, 250, 6, purse: false));
        Assert.True(holds.Holds(FlameBoltII, 2_000_000, 6)); // coin proves nothing
        Assert.NotEqual(0, holds.Key(2_000_000, 6));
        Assert.False(holds.Holds(FlameBoltII, 0, 7)); // the level rose
        Assert.Equal(0, holds.Count);

        Assert.True(holds.Hold(FlameBoltII, 250, 6, purse: true));
        Assert.True(holds.Holds(FlameBoltII, 100, 6));
        Assert.False(holds.Holds(FlameBoltII, 250, 6)); // the purse covers it
    }

    [Fact]
    public void M6_6_MG_a_restart_with_an_empty_ledger_still_keeps_a_scroll_the_rule_keeps_at_the_sale_and_offers_one_it_does_not()
    {
        var home = OutskirtsHome(warRanks: 0); // War Magic 50
        OwnLooted(home, 0x80008001, "Scroll of Ilservian's Flame", FlameBoltVIIScroll, FlameBoltVIISpell); // a VII: kept always (D4)
        OwnLooted(home, 0x80008002, "Scroll of Flame Bolt VI", 7002, 85); // not the plan's next level on War Magic 50
        var memory = new SaleMemory(); // a restart: the loot step's ledger is empty
        var run = home.Start(new ResupplyOptions(Loot: true, Memory: memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: LootedScrollServer(home, FlameBoltVIIScroll, FlameBoltVIISpell, "Ilservian's Flame", teach: false, new List<uint>())));
        var sold = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();

        Assert.Contains(home.Lines, l => l.Contains("0x80008001", StringComparison.Ordinal) && l.Contains("is kept aside (", StringComparison.Ordinal) && l.Contains("kept to be read", StringComparison.Ordinal));
        Assert.Contains(0x80008001u, memory.KeptAside);
        Assert.DoesNotContain(0x80008001u, sold);
        Assert.DoesNotContain(0x80008002u, memory.KeptAside); // the rule does not keep it: the sale offers it
        Assert.Contains(0x80008002u, sold);
    }

    [Fact]
    public void M6_6_MG_the_gear_judge_and_the_restore_read_the_war_element_by_one_rule()
    {
        foreach (var level in new[] { 4, 7 })
        {
            var f = new FloorHome(level: level);
            var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));
            Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));
            var element = Lifestone.Client.Session.Mage.WornRestorer.ElementFor(f.Home.Ground, level, out var source);
            Assert.True(f.Home.Logged(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gear: element 0x{element:X}: {source}")), source);
        }
    }
}
