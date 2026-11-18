using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Tests;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (the den run): the <see cref="DenRunStation"/> over <see cref="RungRig"/>'s stand-in ACE (the travel file's portals as objects, the den's entry and exit, Primary Portal Tie that links, Lifestone Recall that lands): the
/// order of its steps, its refusals before any step, the run-only road (no attacker answered, no walk stopped or planned again), a death that ends it, and a recall home that fails with the tie standing. On the dats: the straight
/// run's pieces.
/// </summary>
public sealed class DenRunStationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint ExitGuid = 0x70103077;
    private const ushort Tie = 47, Recall = 48, LifestoneRecall = 1635;
    private static readonly string[] Chain = ["holtburg-to-network", "network-to-danbys", "danbys-to-desert-march", "desert-march-to-black-hill"];
    private static readonly Vector3 ExitDrop = new(56.3f, 91.1f, 87.3f);

    private static TravelRoutes Routes => TravelRoutes.Default;

    private static HuntingGround Den => GroundCatalog.Get(DenRunStation.DefaultGround);

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    private static RungRig Rig(int level = 40, params ushort[] spells)
    {
        var t = new RungRig(level, spells: spells.Length == 0 ? [Tie, Recall, LifestoneRecall] : spells);
        t.Portals(Routes, Chain);
        t.Entry(Den.Entry!, Den);
        t.Exit(Den, ExitGuid, ExitDrop);
        t.RecallTo[LifestoneRecall] = (0xA9B40019u, new Vector3(84, 7.7f, 94));
        return t;
    }

    /// <summary>The context of a level up run on another ground: the station takes the den from the catalog, never from the journey's dungeon.</summary>
    private static JourneyContext Context(RungRig t, IJourneyTravel? travel = null) =>
        new() { Character = "Winnie", Account = "test", Log = t.R.H.Log, Travel = travel ?? t.Travel, Spells = t.Caster.Table, Dungeon = GroundCatalog.Get("holtburg-outskirts") };

    private static string Field(IStationRun run, string name) => run.VerdictFields().Split(' ').First(f => f.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

    /// <summary>The travel seam with walks that take a few frames, so the walk's per-frame rule is read; <see cref="OnWalk"/> is called once at each request.</summary>
    private sealed class SlowTravel(IJourneyTravel inner, int frames) : IJourneyTravel
    {
        public Action<TravelRequest>? OnWalk { get; set; }

        public bool Busy => inner.Busy;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            OnWalk?.Invoke(request);
            return new Leg(inner.GoTo(session, request, now), frames);
        }

        private sealed class Leg(ITravelLeg inner, int frames) : ITravelLeg
        {
            private int _steps;

            public TravelMeans Means => inner.Means;

            public string? Failure => inner.Failure;

            public string Fields => inner.Fields;

            public TravelStatus Step(IGameSessionView session, double now) => _steps++ < frames ? TravelStatus.Running : inner.Step(session, now);

            public void Pause() => inner.Pause();

            public void Resume(double now) => inner.Resume(now);

            public void Dispose() => inner.Dispose();
        }
    }

    [Fact]
    public void The_run_hops_the_chain_runs_by_the_bend_enters_at_once_waits_a_minute_leaves_ties_and_recalls_home()
    {
        var t = Rig();
        var context = Context(t);
        var (run, status) = t.RunStation(new DenRunStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.True(run.Ok);
        Assert.Equal(("yes", "recall", "yes", "none", "6"), (Field(run, "tied"), Field(run, "home"), Field(run, "ok"), Field(run, "fail"), Field(run, "hops")));
        Assert.InRange(double.Parse(Field(run, "inside_s"), CultureInfo.InvariantCulture), DenRunStation.InsideSeconds, DenRunStation.InsideSeconds + 2);

        // The order: the four portals of the chain, the bend and the entry's front, the entry's Use with no cast before it, the exit's Use, the tie on the entrance, the recall.
        var entry = Den.Entry!;
        string[] wanted = [.. Chain.Select(k => "use:" + Hex(Routes.Edge(k).Guid)), "use:" + Hex(entry.Guid), "use:" + Hex(ExitGuid), $"cast:{Tie}@{Hex(entry.Guid)}", "cast:" + LifestoneRecall.ToString(CultureInfo.InvariantCulture)];
        Assert.Equal(wanted, t.Events.Where(e => e.StartsWith("use:", StringComparison.Ordinal) || e.StartsWith("cast:", StringComparison.Ordinal)));
        var walks = t.Travel.Requests.Select(r => r.Destination.Name).ToList();
        Assert.True(walks.IndexOf("via-1") >= 0 && walks.IndexOf("via-1") < walks.IndexOf("entry-front") && walks.IndexOf("entry-front") < walks.IndexOf("tie-point"), string.Join(",", walks));
        var via = Assert.Single(Routes.RoutesFor(Den.Key).Single(r => r.Key == DenRunStation.RouteKey).Run!.Via!.Points);
        Assert.Equal(via.Cell, t.Travel.Requests.Single(r => r.Destination.Name == "via-1").Destination.Location.Position.CellId);

        // The facts: the den is tied (the one slot), the run-only rule is off again, and she stands at home.
        Assert.Equal(PortalTie.Tied, context.Facts.PortalTieOf(Den.Key));
        Assert.Equal(Den.Key, context.Facts.PrimaryPortalSlot);
        Assert.Null(context.Facts.RunOnly);
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.Single(t.Lines("den_run plan: ground=black-spawn-den-sho level=40 route=den-run"));
        Assert.Equal(10, t.Lines("den_run_leg: ").Count); // four hops, the bend, the front, the enter, the exit, the tie and the way home
        Assert.Single(t.Lines("den_run: inside: check before"));
        Assert.Single(t.Lines("den_run: tie: check after: linked"));
    }

    [Fact]
    public void Under_level_40_the_station_refuses_by_name_before_any_step()
    {
        var t = Rig(level: 39);
        var (run, status) = t.RunStation(new DenRunStation(), Context(t));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("den-run:refused:level:39<40", Field(run, "fail"));
        Assert.Empty(t.Travel.Requests);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new ushort[] { Recall, LifestoneRecall }, "den-run:refused:no-spell:primary-portal-tie")]
    [InlineData(new ushort[] { Tie, LifestoneRecall }, "den-run:refused:no-spell:primary-portal-recall")]
    [InlineData(new ushort[] { Tie, Recall }, "den-run:refused:no-spell:lifestone-recall")]
    public void A_book_short_of_a_travel_spell_refuses_by_name_before_any_step(ushort[] spells, string refusal)
    {
        var t = Rig(40, spells);
        var (run, status) = t.RunStation(new DenRunStation(), Context(t));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(refusal, Field(run, "fail"));
        Assert.Empty(t.Travel.Requests);
    }

    [Fact]
    public void The_refusal_rule_names_the_level_first_then_each_spell()
    {
        Assert.Null(DenRunStation.Refusal(40, true, true, true));
        Assert.Equal("den-run:refused:level:39<40", DenRunStation.Refusal(39, false, false, false));
        Assert.Equal("den-run:refused:no-spell:primary-portal-tie", DenRunStation.Refusal(80, false, true, true));
        Assert.Equal("den-run:refused:no-spell:primary-portal-recall", DenRunStation.Refusal(80, true, false, true));
        Assert.Equal("den-run:refused:no-spell:lifestone-recall", DenRunStation.Refusal(80, true, true, false));
    }

    /// <summary>
    /// M6 item 14 (live 2026-11-13: 44 drinks in 137 s, each a stand, on the first den run): on the run-only road a drink is due only under 40% of the health; at 143 of 156, the health of those 44 drinks, none is.
    /// </summary>
    [Theory]
    [InlineData(143u, 156u, false)]
    [InlineData(130u, 156u, false)]
    [InlineData(98u, 156u, false)]
    [InlineData(63u, 156u, false)]
    [InlineData(62u, 156u, true)]
    [InlineData(10u, 156u, true)]
    [InlineData(0u, 156u, false)]
    [InlineData(5u, 0u, false)]
    public void On_the_run_only_road_a_drink_is_due_only_under_forty_percent_of_the_health(uint health, uint max, bool due)
    {
        Assert.Equal(due, TravelLeg.RunDrinkDue(health, max));
    }

    /// <summary>The run-only road: an attacker on the run is named once and not answered: no bolt is cast at it though the book holds the bolts, the walk is not stopped or planned again, and nothing retreats.</summary>
    [Fact]
    public void An_attacker_on_the_road_is_not_answered_and_the_walk_is_neither_stopped_nor_planned_again()
    {
        const uint tusker = 0x80001944;
        ushort[] bolts = [.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)];
        var t = Rig(40, [Tie, Recall, LifestoneRecall, .. bolts]);
        var attacked = false;
        var slow = new SlowTravel(t.Travel, 8)
        {
            OnWalk = r =>
            {
                if (r.Destination.Name == "entry-front" && !attacked)
                {
                    attacked = true;
                    t.Attacker(tusker, "Sclavus Acolyte", 3f);
                    t.Caster.SetHealth(10); // far under half: a drink would go, and none is held
                }
            },
        };
        var context = Context(t, slow);
        var (run, status) = t.RunStation(new DenRunStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.True(attacked);
        Assert.Single(t.Lines("run-only: 'Sclavus Acolyte' 0x80001944 attacked the player and stands 3.0 m away: not answered, no stop, no retreat: she runs on"));
        Assert.Single(t.Lines("run-only: drink: health 10 of "), l => l.Contains("no health drink is held: nothing is done for it on the road; she runs on", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@" + Hex(tusker), StringComparison.Ordinal));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "entry-front");
        Assert.Empty(t.Lines("fight-back 1 on"));
        Assert.Empty(t.Lines("the walk is stopped"));
        Assert.Empty(t.Lines("retreat due"));
        Assert.Empty(t.Lines("retreat on"));
        Assert.Equal("1", Field(run, "attackers"));
        Assert.Equal("yes", Field(run, "tied"));
        Assert.Null(context.Facts.RunOnly);
    }

    /// <summary>The same attacker without the run-only rule stops the walk (the rule is the fact, not the station): a rung change's run fights back as ever.</summary>
    [Fact]
    public void Without_the_run_only_fact_the_same_walk_is_stopped_for_the_attacker()
    {
        const uint tusker = 0x80001944;
        ushort[] bolts = [.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)];
        var t = new RungRig(10, spells: bolts);
        t.Portals(Routes, "holtburg-to-network");
        t.CastAt = (_, target) => t.Kill(target);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (!first || r.Destination.Name != "holtburg-to-network-front")
            {
                return false;
            }

            first = false;
            t.Attacker(tusker, "Sclavus Acolyte", 3f);
            return true;
        };
        var context = Context(t);
        var (_, status) = t.RunHop(new PortalHopLeg(context, RungPortal.Of(Routes.Edge("holtburg-to-network"), Routes), "the control"));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(t.Lines("the walk is stopped: 'Sclavus Acolyte' 0x80001944 attacked the player"));
        Assert.Contains(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@" + Hex(tusker), StringComparison.Ordinal));
    }

    /// <summary>A death on the run ends the station by the walk's own name; the entry is never used and the run-only rule is left off. One attempt: nothing is walked again.</summary>
    [Fact]
    public void A_death_on_the_run_ends_the_station_by_name_and_nothing_is_tried_again()
    {
        var t = Rig();
        t.Travel.TeleportOn = r => r.Destination.Name == "via-1" ? (0xA9B40019u, new Vector3(84, 7.7f, 94)) : null;
        t.Travel.DiesOn = r => r.Destination.Name == "via-1" ? "You have been killed by a Sclavus Acolyte!" : null;
        var context = Context(t);
        var (run, status) = t.RunStation(new DenRunStation(), context);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.StartsWith("died:run:respawned=0xA9B40019", Field(run, "fail"), StringComparison.Ordinal);
        Assert.Equal("no", Field(run, "tied"));
        Assert.Single(t.Lines("died on the run leg to via-1: death_cell=0x3E310004"));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "via-1");
        Assert.DoesNotContain(t.Travel.Requests, r => r.Destination.Name == "entry-front");
        Assert.DoesNotContain("use:" + Hex(Den.Entry!.Guid), t.Events);
        Assert.Null(context.Facts.RunOnly);
    }

    /// <summary>A hop that fails by name ends the station by that name: no other route, no gem and no second try.</summary>
    [Fact]
    public void A_portal_that_does_not_answer_ends_the_station_by_the_hops_name()
    {
        var t = Rig();
        t.Silent.Add(Routes.Edge("danbys-to-desert-march").Guid);
        var (run, status) = t.RunStation(new DenRunStation(), Context(t));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("hop:danbys-to-desert-march:no-teleport", Field(run, "fail"));
        Assert.Equal(2, t.Events.Count(e => e == "use:" + Hex(Routes.Edge("danbys-to-desert-march").Guid))); // the hop's own use-again, then the name
        Assert.DoesNotContain("use:" + Hex(Routes.Edge("desert-march-to-black-hill").Guid), t.Events);
    }

    /// <summary>The recall home refused after the tie: the tie stands, the station is ok on it and the verdict names the failure (the autopilot's stop recalls again by its own rule).</summary>
    [Fact]
    public void A_recall_home_that_is_refused_leaves_the_tie_standing_and_names_the_failure()
    {
        var t = Rig();
        t.RecallRefusal = 0x049E;
        var context = Context(t);
        var (run, status) = t.RunStation(new DenRunStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.True(run.Ok);
        Assert.Equal(("yes", "failed:den-run:home:refused", "yes"), (Field(run, "tied"), Field(run, "home"), Field(run, "ok")));
        Assert.Equal(PortalTie.Tied, context.Facts.PortalTieOf(Den.Key));
        Assert.Equal(0x3734u, t.Landblock); // she still stands at the entrance
    }

    /// <summary>On the dats the overland legs are run straight: pieces of about 96 m, each the file's way of one point on the straight line to the bend, then to the front.</summary>
    [RequiresDatsFact]
    public void On_the_dats_the_run_goes_straight_in_pieces_each_a_way_of_one_point_on_the_line()
    {
        var t = Rig();
        var context = new JourneyContext { Character = "Winnie", Account = "test", Log = t.R.H.Log, Travel = t.Travel, Spells = t.Caster.Table, Dungeon = GroundCatalog.Get("holtburg-outskirts"), Dats = fixture.Dats };
        var (run, status) = t.RunStation(new DenRunStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        static Vector2 G(CellPosition p) => new((((p.CellId >> 24) & 0xFF) * 192f) + p.Local.X, (((p.CellId >> 16) & 0xFF) * 192f) + p.Local.Y);
        var hop = Routes.Edge("desert-march-to-black-hill");
        var via = Routes.RoutesFor(Den.Key).Single(r => r.Key == DenRunStation.RouteKey).Run!.Via!.Points[0];
        var (drop, bend) = (G(new CellPosition(hop.DestCell, hop.DestOrigin)), G(new CellPosition(via.Cell, via.Origin)));
        var pieces = t.Travel.Requests.Where(r => r.Destination.Name.StartsWith("via-1-piece-", StringComparison.Ordinal)).ToList();
        Assert.InRange(pieces.Count, 8, 10); // 956.8 m in pieces of 96 m (a piece's end steps on when it lies by a landblock's edge)
        var at = drop;
        foreach (var piece in pieces)
        {
            var end = G(piece.Destination.Location.Position);
            Assert.InRange(Vector2.Distance(at, end), TravelLeg.StraightPieceMetres - 0.5f, TravelLeg.StraightPieceMetres + (TravelLeg.StraightEdgeMetres * 5) + 0.5f);
            var along = Vector2.Normalize(bend - at);
            Assert.InRange(Vector2.Dot(Vector2.Normalize(end - at), along), 0.9999f, 1.0001f); // on the straight line to the bend
            Assert.Equal(piece.Destination.Location.Position.CellId, Assert.Single(piece.Way!).CellId);
            var local = piece.Destination.Location.Position.Local;
            Assert.True(local.X >= TravelLeg.StraightEdgeMetres && local.Y >= TravelLeg.StraightEdgeMetres && local.X <= 192 - TravelLeg.StraightEdgeMetres && local.Y <= 192 - TravelLeg.StraightEdgeMetres, local.ToString());
            at = end;
        }

        Assert.NotNull(t.Travel.Requests.Single(r => r.Destination.Name == "via-1").Way);
        var all = t.Travel.Requests.Count(r => r.Destination.Name.Contains("-piece-", StringComparison.Ordinal));
        Assert.Equal(all.ToString(CultureInfo.InvariantCulture) + "/0", Field(run, "straight")); // every piece walked by the file's way, none handed to the planner
    }
}
