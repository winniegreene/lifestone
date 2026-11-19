using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 redoubt (the owner's decision of 2026-11-18: the rung <c>outskirts</c>, levels 5 to 9, hunts the Holtburg Redoubt, landblock 0x0163, which the spec named for it and no profile held): the ground and its travel, by the rules
/// alone. A climber of the rung's levels standing at home is sent there on foot (the run to the entry's front, the portal's level gate, the tie when she knows it, the Use); the portal's level limit 20 refuses by name before any
/// walk; the way home is Lifestone Recall, and without it the exit portal and the walk to the lifestone point (m6-redoubt-way-home, closed). The ladder's rung names the Redoubt and keeps its key and band; the outdoor ground is the rung's
/// outdoor fallback (<see cref="RungFallback"/>): a climber the portal does not admit hunts it, the refusal named on the choice, and a run that stands on it keeps the band's learn plan. It is never taken by an approach's verdict
/// (<c>TODO(m6-redoubt-alternate)</c>).
/// </summary>
public sealed class RedoubtRungTests
{
    private const string Redoubt = "holtburg-redoubt", Outskirts = "holtburg-outskirts";

    private static Ladder L => LadderLoader.WarMage;

    private static RungRouteInput In(int level, RungDirection direction, RungWhere where, string place, bool lifestoneRecall = false, bool portalTie = false) =>
        new(GroundCatalog.Get(Redoubt), direction, where, place, level, lifestoneRecall, false, portalTie, PortalTie.Unknown, false);

    [Fact]
    public void The_redoubt_is_a_ground_the_fight_takes_for_the_rungs_levels()
    {
        var ground = GroundCatalog.Get(Redoubt);
        Assert.Equal(GroundKind.Dungeon, ground.Kind);
        Assert.Null(AutopilotScenario.GroundRefusal(Redoubt)); // the fight takes the ground, and its walk reaches target slots
        var rung = L.Rungs.Single(r => r.Key == "outskirts");
        Assert.All(Enumerable.Range(rung.Band.From, rung.Band.To - rung.Band.From + 1), level => Assert.True(ground.Entry!.Admits(level))); // the portal admits the whole band (1 to 20)
        Assert.Equal(new LevelBand(8, 8), ground.LevelBand); // the rung's target margin 5 admits level 8 creatures from level 5
        Assert.True(rung.Band.From + rung.TargetMargin >= ground.LevelBand.Max);

        // the outdoor ground's home, vendors, stock and purse, carried: the same town at the same levels
        var outdoor = GroundCatalog.Get(Outskirts);
        Assert.Equal(outdoor.Home, ground.Home);
        Assert.Equal(outdoor.Vendors.Select(v => (v.Wcid, v.Guid, v.Cell, v.StopCell)), ground.Vendors.Select(v => (v.Wcid, v.Guid, v.Cell, v.StopCell)));
        Assert.Equal(outdoor.Supplies!.Stock, ground.Supplies!.Stock);
        Assert.Equal((outdoor.Supplies.Purse.Coin, outdoor.Supplies.Purse.MinNote, outdoor.Supplies.BurdenTripPercent), (ground.Supplies.Purse.Coin, ground.Supplies.Purse.MinNote, ground.Supplies.BurdenTripPercent));
    }

    private static RungAsk Ask(int level, string? run = null)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 100, 1, 3u, 0, 10u, 0, 0);
        return new RungAsk(level, null, q, static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = run };
    }

    [Fact]
    public void The_rung_outskirts_keeps_its_key_and_band_and_names_the_redoubt()
    {
        var rung = L.Rungs.Single(r => r.Key == "outskirts");
        Assert.Equal((5, 9, 10), (rung.Band.From, rung.Band.To, rung.MoveOn.Level));
        Assert.Equal((Redoubt, (ushort?)0x0163), (rung.GroundKey, rung.Landblock));
        var health = rung.Health!;
        Assert.Equal(("Drudge Prowler", 192u, 4), (health.Hitter.Name, health.Hitter.Wcid, health.Hitter.Hit)); // DVal 5 x (1 - 0.75 / 2) x 1.055, rounded up
        Assert.Equal(2, health.Pack);
        Assert.NotNull(health.RoadHitter); // the Burrow's road: unchanged

        // the outdoor ground is the rung's one alternate, its outdoor fallback, and stays a ground of the catalog
        var alternate = Assert.Single(rung.Alternates);
        Assert.Equal((Outskirts, (ushort?)null, false), (alternate.GroundKey, alternate.Landblock, alternate.Pending));
        Assert.Equal(Outskirts, RungFallback.Of(rung)!.GroundKey);
        Assert.Equal(GroundKind.Outdoor, GroundCatalog.Get(Outskirts).Kind);
        Assert.All(L.Rungs.Where(r => r.Key != "outskirts"), r => Assert.Null(RungFallback.Of(r))); // no other rung has one: their alternates are dungeons
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    public void A_climber_of_the_band_hunts_the_redoubt(int level)
    {
        var choice = RungOwner.Decide(L, Ask(level));
        Assert.Equal(LadderStepKind.Hunt, choice.Decision.Kind);
        Assert.Equal("outskirts", choice.Decision.Rung!.Key);
        Assert.Equal(Redoubt, choice.Ground!.Key);
        Assert.Null(choice.Alternate);

        // a run that stands on the rung's outdoor ground is a run of this rung and hunts on: the ladder sends a new run to the Redoubt and pulls none off the outdoor ground
        var stays = RungOwner.Decide(L, Ask(level, Outskirts));
        Assert.Equal((LadderStepKind.Hunt, "outskirts", Outskirts, (string?)null), (stays.Decision.Kind, stays.Decision.Rung!.Key, stays.Decision.Rung.GroundKey, stays.Decision.Stop));
        Assert.Equal(Outskirts, stays.Ground!.Key);
        Assert.Contains("a new run takes holtburg-redoubt", stays.Decision.Why, StringComparison.Ordinal);

        // a run on any other ground at her level is moved to the rung's own ground, the Redoubt
        var moved = RungOwner.Decide(L, Ask(level, "low-b-belt"));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:outskirts", true, Redoubt), (moved.Decision.Kind, moved.Decision.Stop, moved.Decision.Ok, moved.Decision.Rung!.GroundKey));
    }

    /// <summary>
    /// The entry fallback (<see cref="RungFallback"/>): a climber above the portal's limit of 20 whom the ladder reads on this rung (the vitae burn's hold of the rung above, or shut gates) is not sent to a portal that refuses her:
    /// she hunts the rung's outdoor ground, by name. At 20 and under the Redoubt stands.
    /// </summary>
    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    [InlineData(40, true)]
    public void A_climber_the_portal_does_not_admit_hunts_the_rungs_outdoor_ground_by_name(int live, bool fallsBack)
    {
        // the ladder reads the rung at a level of its band while her live level is the portal's to read: the hold's own ask (RungAsk.LiveLevel)
        var held = RungOwner.Decide(L, Ask(9) with { LiveLevel = live });
        Assert.Equal(LadderStepKind.Hunt, held.Decision.Kind);
        Assert.Equal("outskirts", held.Decision.Rung!.Key);
        if (!fallsBack)
        {
            Assert.Equal((Redoubt, (string?)null, (string?)null), (held.Ground!.Key, held.Alternate, held.EntryRefused));
            return;
        }

        Assert.Equal((Outskirts, Outskirts, $"gate:entry:holtburg-redoubt:{live}>20"), (held.Ground!.Key, held.Alternate, held.EntryRefused));
        Assert.Equal(RungOwner.Decide(L, Ask(9)).Reason, held.Reason); // the reason stays the ladder's own: the fallback says which ground, not why the rung
        Assert.Equal((Outskirts, AutopilotScenario.EntryFallbackReason), (held.Decision.Rung.GroundKey, held.Decision.Reason));
        Assert.Contains($"gate:entry:holtburg-redoubt:{live}>20", held.Why, StringComparison.Ordinal);
        Assert.Equal((5, 9), (held.Decision.Rung.Band.From, held.Decision.Rung.Band.To)); // the rung's own band, bolt plan and margin

        // a run that already stands on the outdoor ground hunts on, as at any level: no move to a portal that refuses her
        var stays = RungOwner.Decide(L, Ask(9, Outskirts) with { LiveLevel = live });
        Assert.Equal((LadderStepKind.Hunt, Outskirts), (stays.Decision.Kind, stays.Ground!.Key));

        // and one that stands in the Redoubt is moved out to it by name (ok: a rung change, not a failure)
        var leaves = RungOwner.Decide(L, Ask(9, Redoubt) with { LiveLevel = live });
        Assert.Equal((LadderStepKind.Moved, "rung-moved:outskirts", true, Outskirts), (leaves.Decision.Kind, leaves.Decision.Stop, leaves.Decision.Ok, leaves.Decision.Rung!.GroundKey));
    }

    [Fact]
    public void A_run_on_the_outdoor_ground_keeps_the_bands_learn_plan_and_the_dens_stay_no_ladder_grounds()
    {
        // the rung names the ground it plays and its outdoor fallback (ScrivenerLearnPlan.RungOf)
        Assert.All(new[] { 5, 9, 12, 30 }, level => Assert.Equal("outskirts", ScrivenerLearnPlan.RungOf(L, Outskirts, level)!.Key));
        Assert.All(new[] { 5, 9, 12 }, level => Assert.Equal("outskirts", ScrivenerLearnPlan.RungOf(L, Redoubt, level)!.Key));

        // a dungeon listed as a later alternate is still no ladder ground here: BSD Mage's den trips are unchanged
        Assert.Null(ScrivenerLearnPlan.RungOf(L, "black-spawn-den-aluvian", 80));
        Assert.Null(ScrivenerLearnPlan.RungOf(L, "tusker-armory", 65));
        Assert.Null(ScrivenerLearnPlan.RungOf(L, "low-b-belt", 9)); // an outdoor ground no rung lists
    }

    [Fact]
    public void A_level_six_climber_at_home_runs_to_the_entry_front_passes_its_gate_and_enters()
    {
        var ground = GroundCatalog.Get(Redoubt);
        var plan = RungRoute.Decide(In(6, RungDirection.Out, RungWhere.Home, "holtburg"), TravelRoutes.Default);
        Assert.Null(plan.Stop);
        Assert.Equal("on-foot-to-the-entry", plan.RouteKey);
        Assert.Equal([RungLegKind.Run, RungLegKind.Gate, RungLegKind.Enter], plan.Legs.Select(l => l.Kind));
        Assert.Equal((ground.Entry!.FrontCell, ground.Entry.Front), (plan.Legs[0].Cell, plan.Legs[0].At));
        var enter = plan.Legs[2].Portal!;
        Assert.Equal((4935u, 0x7AAB1000u, 0x0163u, (int?)1, (int?)20), (enter.Wcid, enter.Guid, enter.DestLandblock, enter.MinLevel, enter.MaxLevel));
        Assert.Equal(("no", "pass"), (plan.Tied, plan.Gate));

        // with Primary Portal Tie in the book the tie comes before the Use (the portal is tieable: PortalBitmask 1)
        var tying = RungRoute.Decide(In(6, RungDirection.Out, RungWhere.Home, "holtburg", portalTie: true), TravelRoutes.Default);
        Assert.Equal([RungLegKind.Run, RungLegKind.Gate, RungLegKind.Tie, RungLegKind.Enter], tying.Legs.Select(l => l.Kind));

        // the travel rule sends her by the rung change, not by the outdoor ground's walk
        Assert.Equal(TravelWay.RungChange, AutopilotTravelRule.Out(In(6, RungDirection.Out, RungWhere.Home, "holtburg"), bookRead: true, TravelRoutes.Default).Way);
    }

    [Fact]
    public void The_portals_level_limit_refuses_by_name_before_any_walk()
    {
        var plan = RungRoute.Decide(In(21, RungDirection.Out, RungWhere.Home, "holtburg"), TravelRoutes.Default);
        Assert.Empty(plan.Legs);
        Assert.Equal("gate:entry:holtburg-redoubt:21>20", plan.Stop);
        Assert.Equal("refused:20", plan.Gate);
        Assert.Null(RungRoute.Decide(In(20, RungDirection.Out, RungWhere.Home, "holtburg"), TravelRoutes.Default).Stop);
    }

    [Fact]
    public void Home_is_lifestone_recall_and_without_it_the_exit_portal_and_the_walk_to_the_lifestone()
    {
        var recall = RungRoute.Decide(In(6, RungDirection.Home, RungWhere.AtGround, "holtburg-redoubt", lifestoneRecall: true), TravelRoutes.Default);
        Assert.Null(recall.Stop);
        Assert.Equal([RungLegKind.LifestoneRecall], recall.Legs.Select(l => l.Kind));

        // m6-redoubt-way-home (closed): the Surface portal drops in landblock 0xAAB1, a landblock of the travel file's place holtburg, and the file holds no way home from that place to itself; the rest is the walk to the lifestone point
        var ground = GroundCatalog.Get(Redoubt);
        Assert.Equal("holtburg", TravelRoutes.Default.PlaceOf(RungRoute.Hex(ground.Exit!.Destination >> 16))!.Key);
        var walk = RungRoute.Decide(In(6, RungDirection.Home, RungWhere.AtGround, "holtburg-redoubt"), TravelRoutes.Default);
        Assert.Null(walk.Stop);
        Assert.Equal("exit-walk-home", walk.RouteKey);
        Assert.Equal([RungLegKind.Hop, RungLegKind.WalkHome], walk.Legs.Select(l => l.Kind));
        Assert.Equal((ground.Exit.Wcid, ground.Exit.Cell), (walk.Legs[0].Portal!.Wcid, walk.Legs[0].Portal!.Cell));
        Assert.Equal((ground.Home.LifestoneCell, ground.Home.LifestonePoint), (walk.Legs[1].Cell, walk.Legs[1].At));

        // The travel rule takes it for a climber whose read book holds no recall, and for one whose pack is short for the cast (the reason is carried).
        var rule = AutopilotTravelRule.Home(In(6, RungDirection.Home, RungWhere.AtGround, "holtburg-redoubt"), bookRead: true, TravelRoutes.Default);
        Assert.Equal((TravelWay.RungChange, "exit-walk-home"), (rule.Way, rule.Route));
    }

    [Fact]
    public void The_outdoor_ground_is_never_taken_by_an_approachs_verdict_because_the_alternate_rule_reads_an_approach_and_a_portal()
    {
        // TODO(m6-redoubt-alternate): an alternate is taken for a held rung by its approach's verdict and its entry portal; the outdoor ground has neither, so a hold of the Redoubt never puts her there
        var outdoor = GroundCatalog.Get(Outskirts);
        Assert.Null(outdoor.Approach);
        Assert.Null(outdoor.Entry);
        Assert.StartsWith("no-approach:", RungOwner.AlternateRefusal(Assert.Single(L.Rungs.Single(r => r.Key == "outskirts").Alternates), 6), StringComparison.Ordinal);
        Assert.Null(GroundCatalog.Get(Redoubt).Approach); // and the way out reads no hold on a ground with no approach recorded
    }
}
