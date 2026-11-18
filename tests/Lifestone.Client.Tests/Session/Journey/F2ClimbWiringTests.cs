using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 0 fix slice F2 (the climb's remaining wiring): the trip's way home for a character with no Lifestone Recall (a point goal on foot, check, act, check), the outdoor death's walk and targets, the mana lines read against the spellbook, the foci
/// audit of a ground with no worn kit, and an outdoor ground's arrival that is not an interior.
/// </summary>
public sealed class F2ClimbWiringTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Belt = "low-b-belt";
    private const string Outskirts = "holtburg-outskirts";

    private static TravelRequest Home() => new(JourneyGrounds.Holtburg, TravelPurpose.Home);

    private static void ReadBookWithout(J1JourneyRig rig, params uint[] spells)
    {
        var book = rig.R.H.State.Self.Spellbook;
        book.Clear();
        book.IsKnown = true;
        foreach (var s in spells)
        {
            book.Learn(s);
        }
    }

    private static JourneyTravel TravelOver(J1JourneyRig rig, string character, HuntingGround? ground, IJourneyRunnerHost? runner)
    {
        rig.Facts.TeleportBudget = 1;
        return new JourneyTravel(character, rig.Facts, rig.Log, runner, rig.Place, ground);
    }

    /// <summary>A runner whose run puts the player at the request's point on its first step (an arrival), as the host's walk does.</summary>
    private sealed class ArrivingRunner(J1JourneyRig rig) : IJourneyRunnerHost
    {
        public List<TravelRequest> Requests { get; } = [];

        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            return new Leg(rig, request);
        }

        private sealed class Leg(J1JourneyRig rig, TravelRequest request) : ITravelLeg
        {
            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => null;

            public string Fields => "route_m=10.0 run_s=5.0 rungs=0 blocked=0";

            public TravelStatus Step(IGameSessionView session, double now)
            {
                var to = request.Destination.Location.Position;
                rig.TeleportNow(to.CellId, to.Local);
                return TravelStatus.Arrived;
            }

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public void The_trips_way_home_without_the_recall_is_a_point_goal_to_the_lifestone_point_with_its_purpose_logged_and_the_arrival_checked()
    {
        var rig = new J1JourneyRig(J1JourneyRig.BeltCell);
        ReadBookWithout(rig);
        var belt = GroundCatalog.Get(Belt);
        var runner = new J1Runner();
        var travel = TravelOver(rig, "Winnie", belt, runner);

        var leg = travel.GoTo(rig.View, Home(), rig.Now);

        Assert.Equal(TravelMeans.Run, leg.Means);
        var request = Assert.Single(runner.Requests);
        Assert.Equal((TravelPurpose.Bind, 0u), (request.Purpose, request.ObjectGuid)); // a point goal on foot, never a recall or a place
        Assert.Equal(belt.Home.LifestoneCell, request.Destination.Location.Position.CellId);
        Assert.Equal(belt.Home.LifestonePoint, request.Destination.Location.Position.Local);
        Assert.Contains(rig.Log.Lines, l => l.Contains("home on foot: check before: purpose: the way home (Home) to Holtburg's lifestone point as a Bind point goal", StringComparison.Ordinal));
        // The scripted run ends without moving the player: the check after names the miss, and the leg fails by name (never Arrived).
        Assert.Equal(TravelStatus.Failed, rig.RunLeg(leg));
        Assert.StartsWith("travel:home:arrival-off-point", leg.Failure, StringComparison.Ordinal);
        Assert.Equal(TravelStatus.Failed, leg.Step(rig.View, rig.Now)); // a later step reads the same answer
        Assert.Empty(rig.Casts);
        Assert.Empty(rig.PlacesTo);
        Assert.Equal(0, rig.Facts.Teleports);
        Assert.Equal(0, rig.Facts.Recalls);
    }

    [Fact]
    public void The_walk_home_arrives_when_the_player_stands_at_the_point_after_it_and_is_none_when_already_there()
    {
        var belt = GroundCatalog.Get(Belt);
        var rig = new J1JourneyRig(J1JourneyRig.BeltCell);
        ReadBookWithout(rig);
        var arriving = new ArrivingRunner(rig);
        var travel = TravelOver(rig, "Winnie", belt, arriving);

        var leg = travel.GoTo(rig.View, Home(), rig.Now);
        Assert.Equal(TravelStatus.Arrived, rig.RunLeg(leg));
        Assert.Null(leg.Failure);
        Assert.Single(arriving.Requests);
        Assert.Equal(belt.Home.LifestoneCell >> 16, rig.Landblock);

        // Now at the point: the same request is none (check before), and no run is made.
        var again = travel.GoTo(rig.View, Home(), rig.Now);
        Assert.Equal(TravelMeans.None, again.Means);
        Assert.Equal(TravelStatus.Arrived, again.Step(rig.View, rig.Now));
        Assert.Single(arriving.Requests);
    }

    [Fact]
    public void A_book_with_the_recall_an_unread_book_and_a_journey_with_no_ground_still_cast_it_as_before_and_a_ground_off_foot_names_why()
    {
        var belt = GroundCatalog.Get(Belt);
        var den = GroundCatalog.Get(FightOptions.DefaultGround);

        var held = new J1JourneyRig(J1JourneyRig.BeltCell); // the rig's book holds 1635
        var heldLeg = TravelOver(held, "BSD Mage", belt, new J1Runner()).GoTo(held.View, Home(), held.Now);
        Assert.Equal(TravelMeans.Recall, heldLeg.Means);
        Assert.Equal(TravelStatus.Arrived, held.RunLeg(heldLeg));
        Assert.Equal([1635u], held.Casts);

        var unread = new J1JourneyRig(J1JourneyRig.BeltCell);
        unread.R.H.State.Self.Spellbook.IsKnown = false;
        Assert.Equal(TravelMeans.Recall, TravelOver(unread, "Winnie", belt, new J1Runner()).GoTo(unread.View, Home(), unread.Now).Means);

        var noGround = new J1JourneyRig(J1JourneyRig.BeltCell);
        ReadBookWithout(noGround);
        Assert.Equal(TravelMeans.Recall, TravelOver(noGround, "BSD Mage", null, new J1Runner()).GoTo(noGround.View, Home(), noGround.Now).Means);

        var dungeon = new J1JourneyRig(J1JourneyRig.DenCell);
        ReadBookWithout(dungeon); // no recall in the book and no walk home from a dungeon: the recall is tried and fails by its own name, with the rule's reason beside it
        var runner = new J1Runner();
        var off = TravelOver(dungeon, "Winnie", den, runner).GoTo(dungeon.View, Home(), dungeon.Now);
        Assert.Equal(TravelMeans.Recall, off.Means);
        Assert.Empty(runner.Requests);
        Assert.Contains(dungeon.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("TODO(m6-first-tie-walk)", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_0_F3_a_climbers_ground_travel_to_an_outdoor_ground_is_a_walk_never_a_place_and_a_bsd_mage_is_unchanged()
    {
        var belt = GroundCatalog.Get(Belt);
        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        rig.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint); // in Holtburg, away from the ground
        var arriving = new ArrivingRunner(rig);
        var request = new TravelRequest(belt.ArrivalDestination(), TravelPurpose.Ground);

        var leg = TravelOver(rig, "Winnie", belt, arriving).GoTo(rig.View, request, rig.Now);

        Assert.Equal(TravelMeans.Run, leg.Means);
        var walk = Assert.Single(arriving.Requests);
        Assert.Equal(TravelPurpose.Cell, walk.Purpose); // a point goal on foot
        Assert.Equal(request.Destination.Location.Position.CellId, walk.Destination.Location.Position.CellId);
        Assert.Contains(rig.Log.Lines, l => l.Contains("ground on foot: check before: purpose: Ground to", StringComparison.Ordinal));
        Assert.Equal(TravelStatus.Arrived, rig.RunLeg(leg)); // the check after: the player stands at the point
        Assert.Empty(rig.PlacesTo);
        Assert.Equal(0, rig.Facts.Teleports);

        // The same request for BSD Mage (no climber): today's place.
        var mage = new J1JourneyRig(J1JourneyRig.DenCell);
        mage.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint);
        var placed = TravelOver(mage, "BSD Mage", belt, new ArrivingRunner(mage)).GoTo(mage.View, request, mage.Now);
        Assert.Equal(TravelMeans.Place, placed.Means);
        Assert.Single(mage.PlacesTo);
    }

    /// <summary>M6 7 H: LowRungDeathLiveTests' Throwaway stands in for the climber on the outskirts: a journey with no place in its budget reaches an outdoor ground on foot; a budget still places, and a dungeon-less journey is still refused the place by name.</summary>
    [Fact]
    public void M6_7_H_a_journey_with_no_place_in_its_budget_walks_to_an_outdoor_ground()
    {
        var belt = GroundCatalog.Get(Belt);
        var request = new TravelRequest(belt.ArrivalDestination(), TravelPurpose.Ground);

        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        rig.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint);
        rig.Facts.TeleportBudget = 0;
        var arriving = new ArrivingRunner(rig);
        var leg = new JourneyTravel("Lifestone Throwaway", rig.Facts, rig.Log, arriving, rig.Place, belt).GoTo(rig.View, request, rig.Now);
        Assert.Equal(TravelMeans.Run, leg.Means);
        Assert.Equal(TravelPurpose.Cell, Assert.Single(arriving.Requests).Purpose);
        Assert.Equal(TravelStatus.Arrived, rig.RunLeg(leg));
        Assert.Empty(rig.PlacesTo);

        var budget = new J1JourneyRig(J1JourneyRig.DenCell);
        budget.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint);
        Assert.Equal(TravelMeans.Place, TravelOver(budget, "Lifestone Throwaway", belt, new ArrivingRunner(budget)).GoTo(budget.View, request, budget.Now).Means);

        var noGround = new J1JourneyRig(J1JourneyRig.DenCell);
        noGround.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint);
        noGround.Facts.TeleportBudget = 0;
        var refused = new JourneyTravel("Lifestone Throwaway", noGround.Facts, noGround.Log, new ArrivingRunner(noGround), noGround.Place, null).GoTo(noGround.View, request, noGround.Now);
        Assert.Equal(TravelStatus.Failed, refused.Step(noGround.View, noGround.Now));
        Assert.Equal("travel:place:over-budget", refused.Failure);
    }

    /// <summary>M6 7 H: the plan's buff profile reaches the journey's fights (and through them the death station's rebuff), the factory's seam; no profile leaves each cycle's own choice by name.</summary>
    [Fact]
    public void M6_7_H_a_plans_buff_profile_reaches_the_factorys_fight_options()
    {
        static FightOptions Fight(IStationFactory factory) => (FightOptions)factory.GetType().GetField("_fight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(factory)!;
        var plan = new JourneyPlan("t", "Lifestone Throwaway", "test-tools", StationWhere.Here, 0, []);
        Assert.Null(Fight(JourneyStations.Create(plan, [], null)).BuffProfile);

        var climbing = Fight(JourneyStations.Create(plan with { BuffProfile = BuffProfiles.Climber }, [], null));
        Assert.Same(BuffProfiles.Climber, climbing.BuffProfile);
        Assert.Same(BuffProfiles.Climber, JourneyStations.DenFightOptions(new StationSpec("den-fight", StationKinds.DenFight, null, 0), climbing, []).BuffProfile);
    }

    [Fact]
    public void M6_0_F3_a_climbers_ground_travel_that_would_be_a_place_is_a_loud_stop_by_name()
    {
        var belt = GroundCatalog.Get(Belt);
        var request = new TravelRequest(belt.ArrivalDestination(), TravelPurpose.Ground);
        foreach (var (ground, runner) in new (HuntingGround?, IJourneyRunnerHost?)[] { (null, new J1Runner()), (belt, null), (belt with { Outdoor = null }, new J1Runner()) })
        {
            var rig = new J1JourneyRig(J1JourneyRig.DenCell);
            rig.TeleportNow(belt.Home.LifestoneCell, belt.Home.LifestonePoint);

            var leg = TravelOver(rig, "Winnie", ground, runner).GoTo(rig.View, request, rig.Now);

            Assert.Equal(TravelStatus.Failed, leg.Step(rig.View, rig.Now));
            Assert.Equal("travel:ground:climber-no-place", leg.Failure);
            Assert.Empty(rig.PlacesTo);
            Assert.Equal(0, rig.Facts.Teleports);
            Assert.Contains(rig.Log.Lines, l => l.Contains("REFUSED", StringComparison.Ordinal) && l.Contains("the climber is never placed", StringComparison.Ordinal));
        }
    }

    private sealed class SpyWalks : IJourneyRunnerHost, IJourneyWalks
    {
        public int FightWalks { get; private set; }

        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => throw new NotSupportedException();

        public IFightWalk? FightWalk(JourneyContext context, HuntingGround ground)
        {
            FightWalks++;
            return null;
        }

        public IHuntFollow? HuntFollow(JourneyContext context) => null;
    }

    private (int Outdoor, int Dungeon) DeathStationFightWalks()
    {
        var counts = new List<int>();
        foreach (var key in new[] { Outskirts, FightOptions.DefaultGround })
        {
            var t = new J3bRig { Dats = fixture.Dats };
            var spy = new SpyWalks();
            t.RunnersOverride = spy;
            var factory = JourneyStations.Create(JourneyPlans.Mage, [$"--fight-ground={key}"], fixture.Dats);
            var station = factory.Create(new StationSpec("death", StationKinds.Death, new StationNeeds(StationWhere.Here), 280));
            var run = station.Start(t.Context(dungeon: GroundCatalog.Get(key)));
            try
            {
                run.Step(t.View, t.Now);
            }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException)
            {
                // The walk is chosen before the scenario's constructor builds the mage kit. For the den the rig's spell table lacks a fight extra. M6 0 F3 (closes TODO(m6-death-outdoor-kit)): an outdoor ground without a worn kit has the bare kit,
                // so the constructor no longer refuses it (ground-plan-gap); whatever else the rig's thin spell table stops it with, the walk was asked for (or not) first, which is what is counted.
                Assert.DoesNotContain("ground-plan-gap", e.Message, StringComparison.Ordinal);
                Assert.True(key == Outskirts || e is KeyNotFoundException, e.Message);
            }
            counts.Add(spy.FightWalks);
        }

        return (counts[0], counts[1]);
    }

    [RequiresDatsFact]
    public void The_death_station_walks_an_outdoor_ground_by_its_cells_and_a_dungeon_by_the_hosts_den_walk()
    {
        var (outdoor, dungeon) = DeathStationFightWalks();

        Assert.Equal(0, outdoor); // the host's dungeon walk is never asked for an outdoor ground: OutdoorFightWalk (a point goal a cell through the travel seam) is the death's walk
        Assert.Equal(1, dungeon); // the den: unchanged
    }

    [Fact]
    public void An_outdoor_grounds_death_targets_are_its_creatures_in_the_band_where_the_profile_lists_none()
    {
        var outskirts = GroundCatalog.Get(Outskirts);

        Assert.Empty(outskirts.TargetWcids()); // what the death handler's trip look-out read: nothing outdoors
        var targets = FightTargets.Wcids(outskirts);
        Assert.NotEmpty(targets);
        Assert.All(targets, wcid => Assert.Contains(outskirts.Outdoor!.Creatures, c => c.Wcid == wcid));
        var den = GroundCatalog.Get(FightOptions.DefaultGround);
        Assert.Equal(den.TargetWcids(), FightTargets.Wcids(den)); // a dungeon: the same list as before
    }

    [Fact]
    public void Only_a_dungeons_arrival_is_an_interior()
    {
        Assert.Equal(InteriorKind.Dungeon, GroundCatalog.Get(FightOptions.DefaultGround).ArrivalDestination().Interior);
        Assert.Null(GroundCatalog.Get(Outskirts).ArrivalDestination().Interior);
        Assert.Null(GroundCatalog.Get(Belt).ArrivalDestination().Interior);
    }

    private sealed class ManaRig
    {
        public ManaRig(IBuffProfile profile, params uint[] book)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            var spells = R.H.State.Self.Spellbook;
            spells.Clear();
            spells.IsKnown = true;
            foreach (var s in book)
            {
                spells.Learn(s);
            }

            Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, new BuffOptions(), R.H.Log, profile: profile);
            Profile = profile;
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffCycle Cycle { get; }

        public IBuffProfile Profile { get; }

        public int Pick(bool transfer)
        {
            var line = transfer ? Profile.ManaTransferLine : Profile.StaminaRestoreLine;
            var infos = (SpellInfo?[])Field(transfer ? "_transferInfos" : "_boostInfos");
            var payable = Enumerable.Repeat(true, line.Spells.Count).ToArray();
            return (int)typeof(BuffCycle).GetMethod("PickMana", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Cycle, [line, infos, payable, 600u, 100000u])!;
        }

        private object Field(string name) => typeof(BuffCycle).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(Cycle)!;
    }

    [Fact]
    public void A_mana_line_learned_by_play_is_picked_only_at_the_levels_the_spellbook_holds_and_a_missing_line_is_logged_once()
    {
        var climber = BuffProfiles.For("Winnie")!;
        var transfer = climber.ManaTransferLine;
        var boost = climber.StaminaRestoreLine;

        var none = new ManaRig(climber);
        Assert.Equal(-1, none.Pick(transfer: true));
        Assert.Equal(-1, none.Pick(transfer: true));
        Assert.Equal(-1, none.Pick(transfer: false));
        Assert.Single(none.R.H.Log.Entries, e => e.Message.StartsWith(transfer.Name + ": no level is in the spellbook", StringComparison.Ordinal)); // once, by name
        Assert.Single(none.R.H.Log.Entries, e => e.Message.StartsWith(boost.Name + ": no level is in the spellbook", StringComparison.Ordinal));

        var first = new ManaRig(climber, transfer.Spells[0].Id, boost.Spells[1].Id);
        Assert.Equal(0, first.Pick(transfer: true)); // only level I is held, however high the skill reaches
        Assert.Equal(1, first.Pick(transfer: false));
        Assert.DoesNotContain(first.R.H.Log.Entries, e => e.Message.Contains("no level is in the spellbook", StringComparison.Ordinal));

        var more = new ManaRig(climber, transfer.Spells[0].Id, transfer.Spells[2].Id);
        Assert.Equal(2, more.Pick(transfer: true)); // the highest held under the chance
    }

    [Fact]
    public void BSD_Mages_mana_lines_are_taught_by_the_build_so_the_spellbook_is_not_asked()
    {
        var arena = ArenaProfile.Buffs;
        var empty = new ManaRig(arena); // the book reads empty: the build line is still picked (it is granted at the build)

        Assert.False(arena.ManaTransferLine.ByPlay);
        Assert.True(empty.Pick(transfer: true) > 0);
        Assert.True(empty.Pick(transfer: false) > 0);
        Assert.DoesNotContain(empty.R.H.Log.Entries, e => e.Message.Contains("no level is in the spellbook", StringComparison.Ordinal));
    }

    private static GameState PackWith(string name, params (uint Guid, string Item, uint Wcid)[] items)
    {
        var rig = new VendorRig(0);
        foreach (var (guid, item, wcid) in items)
        {
            rig.Arrive(guid, item, wcid, 0x80, 0);
            rig.Put(guid);
        }

        rig.R.H.State.Self.Name = name;
        return rig.R.H.State;
    }

    [Fact]
    public void The_foci_audit_of_a_ground_with_no_worn_kit_names_the_foci_Winnies_casts_assume_and_audits_no_one_elses()
    {
        var belt = GroundCatalog.Get(Belt); // no worn kit and no stock lines: the counters read all well, so the reading is made
        Assert.Empty(belt.Supplies.Kit);
        var counters = new LogisticsCounters(belt);

        var reading = StartupAudit.ReadKit(PackWith("Winnie"), belt, counters);
        Assert.Equal(["the Foci of Verdancy (Life Magic)", "the Foci of Strife (War Magic)"], reading.FociMissing.Order(StringComparer.Ordinal).Reverse());
        Assert.Contains("focus 15271 0/1", reading.Missing);

        var held = PackWith("Winnie", (0x80000301, "Foci of Strife", 15271), (0x80000302, "Foci of Verdancy", 15270));
        Assert.Empty(StartupAudit.ReadKit(held, belt, counters).FociMissing);

        var half = PackWith("Winnie", (0x80000301, "Foci of Strife", 15271));
        Assert.Equal(["the Foci of Verdancy (Life Magic)"], StartupAudit.ReadKit(half, belt, counters).FociMissing);

        var other = PackWith("Holtburg Hunter");
        Assert.Empty(StartupAudit.ReadKit(other, belt, counters).FociMissing); // BSD Mage's foci are its kit's; the fighters cast no foci formula
        Assert.Empty(StartupAudit.AssumedFoci(other));
        Assert.Equal([15271u, 15270u], StartupAudit.AssumedFoci(held).OrderByDescending(f => f));
    }
}
