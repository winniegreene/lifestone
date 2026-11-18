using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>rung-change</c> (M6 item 4 t2, D50, D51): the climber's way to its rung's ground (the journey's <see cref="JourneyContext.Dungeon"/>), or home from it (<c>direction=home</c>), by play: <see cref="RungRoute.Decide"/> plans the legs from where the body
/// stands, the level, the spellbook, the ground's tie and the gem over the travel file (<see cref="JourneyContext.Routes"/>), and the station runs each with check, act, check (M5 D141):
/// <list type="bullet">
/// <item><c>lifestone-recall</c>: Lifestone Recall 1635, arrival in the travel file's start landblock (Holtburg's lifestone); 0x049E fails by name.</item>
/// <item><c>portal-recall</c>: the ground's Primary Portal Recall (the ground known tied), arrival in the ground's arrival landblock; ACE's 0x04A3 (not tied) sets the tie untied and plans again once (<c>recovery=route-after-untied</c>).</item>
/// <item><c>hop</c> and <c>enter</c>: a <see cref="PortalHopLeg"/> (walk to the front, find, seek, Use, arrive; one <c>recovery=use-again</c>); a hop of a route whose next place is the gem's place (the island) that fails by name while the Aphus Lassel Portal Gem is
/// held is replaced, with the hops up to the one into the island, by the gem (<c>gem=used</c>: Use it, arrive in 0xF682) and the run goes on from the gem's drop.</item>
/// <item><c>run</c> and <c>walk-home</c>: on foot to the entry's front point or the home lifestone's point (<see cref="TravelLeg.WalkToPoint"/>). M6 7 N: a run the entry portal ends by a teleport into its destination is the entry taken on touch, as a hop's walk (item 4's F5): the leg lines of
/// the run, the gate, the tie and the enter say <c>taken=touch</c>, no Use is sent and the arrival is checked as a Use's; a move anywhere else keeps <c>travel:run:moved</c>, a death stays a death. The way out of a ground is a <c>hop</c> (<c>exit:&lt;ground&gt;</c>) and has F5's rule itself.</item>
/// <item><c>gate</c>: the entry's gate read again at run time; <c>tie</c>: Primary Portal Tie 47 on the entry portal, "linked with the portal" read before the Use (a tie that does not link is logged and the trip goes on, <c>tied=no</c>).</item>
/// </list>
/// Lines: one <c>rung_change_leg: n= kind= edge= from= to= metres= ms= ok=</c> a leg, and the verdict <c>rung_change: ground= level= route= hops= walked_m= gem= tied= gate= ok= fail= direction=</c> (report-only in its first gate, D27); the
/// station's verdict fields are the same. The autopilot's Travel state runs the station (T3b), and the logistics station's buy-trip and learn-trip run it for their hops (T4b).
/// </summary>
public sealed partial class RungChangeStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "rung-change";

    /// <summary>The spec argument naming the direction (<c>out</c>, the default, or <c>home</c>).</summary>
    public const string DirectionArgument = "direction";

    /// <summary>M6 6 C2: the spec argument naming the travel place the rung change stops in (<c>stop_at=&lt;place&gt;</c>, <see cref="RungRouteInput.StopAtPlace"/>); absent: the whole plan.</summary>
    public const string StopAtArgument = "stop_at";

    /// <summary>A rung change in <paramref name="direction"/>.</summary>
    /// <param name="direction">Out to the journey's ground, or home.</param>
    /// <param name="gem">The portal gem a failed hop to its place may use (by default the travel file's gem that replaces hops into the island: the Aphus Lassel Portal Gem).</param>
    /// <param name="stopAt">M6 6 C2: the travel place the rung change stops in (the corpse's place of a death on a leg), or null: the whole plan.</param>
    public RungChangeStation(RungDirection direction = RungDirection.Out, TravelGem? gem = null, string? stopAt = null)
    {
        Direction = direction;
        Gem = gem;
        StopAt = string.IsNullOrEmpty(stopAt) ? null : stopAt;
    }

    /// <summary>M6 6 C2: the station of a spec's arguments: <see cref="DirectionArgument"/> (<see cref="ParseDirection"/>) and <see cref="StopAtArgument"/> (a travel place's key, checked when the plan is made).</summary>
    /// <param name="arguments">The spec's arguments.</param>
    public static RungChangeStation FromArguments(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new RungChangeStation(ParseDirection(arguments.TryGetValue(DirectionArgument, out var direction) ? direction : null), stopAt: arguments.TryGetValue(StopAtArgument, out var stopAt) ? stopAt : null);
    }

    /// <summary>M6 6 C2: the travel place the rung change stops in, or null.</summary>
    public string? StopAt { get; }

    /// <summary>Out or home.</summary>
    public RungDirection Direction { get; }

    /// <summary>The portal gem given to the station, or null: the travel file's (<see cref="JourneyContext.Routes"/>) is then used.</summary>
    public TravelGem? Gem { get; }

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs => NeedsOf(Direction);

    /// <summary>
    /// M6 8 I (the live round 1, 18:53 UTC, level 11: a retreat with no mana was stopped by <c>precondition:mana</c> and she stood while a Young Mosswart killed her). The mana need is real only where a leg casts: the way out (Primary Portal Recall, the Tie and
    /// the recharge at the door). The way home is a walk or portals: <c>AutopilotTravelRule.Home</c> sends the autopilot to this station only where the way home is on foot (the recall is no leg of it) or the book holds no Lifestone Recall, so no home plan casts and a
    /// mana need would only keep a body with no mana from walking out of danger. A recall home keeps its own need (<c>recall-home</c>, <c>RecallHomeStation.MinManaPercent</c>).
    /// </summary>
    /// <param name="direction">The station's direction.</param>
    public static StationNeeds NeedsOf(RungDirection direction) => direction == RungDirection.Out ? new StationNeeds(MinManaPercent: 20) : StationNeeds.None;

    /// <summary>The direction of a spec's <see cref="DirectionArgument"/> value; anything but <c>out</c> or <c>home</c> is refused by name.</summary>
    /// <param name="value">The value.</param>
    public static RungDirection ParseDirection(string? value) => value switch
    {
        null or "out" => RungDirection.Out,
        "home" => RungDirection.Home,
        _ => throw new ArgumentException($"journey: the rung-change station's {DirectionArgument}={value}: the direction is out or home"),
    };

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Direction, Gem ?? context.Routes.Gems.FirstOrDefault(), StopAt);
    }

    private sealed partial class Run(JourneyContext context, RungDirection direction, TravelGem? gem, string? stopAt) : TravelLeg(StationName, context)
    {
        private RungPlan? _plan;
        private int _level;
        private int _hops;
        private double _walked;
        private string _gem = "none";
        private string _tied = "untieable";
        private string _gate = "pass";
        private bool _replan;
        private bool _finished;
        private bool _fellBack;
        private FailedHop? _pendingFallback;
        private FailedHop? _failedOver;
        private string? _routeFallback;
        private string? _enterShort; // M6 7 AN: why the recharge at the door did not reach the bar
        private int? _takenMark; // M6 7 N: the travel mark of the run leg the entry portal took the body on: the gate, the tie and the enter have nothing to do

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"ground={Context.Dungeon.Key} level={_level} route={_plan?.RouteKey ?? RungRoute.NoRoute} hops={_hops} walked_m={_walked:0} gem={_gem} tied={_tied} gate={_gate} ok={(_finished && Failure is null ? "yes" : "no")} fail={FailOf()} direction={(direction == RungDirection.Home ? "home" : "out")}{(stopAt is null ? string.Empty : " stop_at=" + stopAt)}{(_routeFallback is null ? string.Empty : " route_fallback=" + _routeFallback)}");

        private string FailOf() => _finished && Failure is null ? "none" : (FailName ?? HopStep ?? Stage).Replace(' ', '_');

        protected override IEnumerable<ActionHandle?> Body()
        {
            try
            {
                foreach (var h in Plan())
                {
                    yield return h;
                }

                foreach (var h in Legs())
                {
                    yield return h;
                }

                if (_pendingFallback is not null)
                {
                    // M6 7 M: a hop failed by name and the ground has a next admitted route: once a station run, plan it from where the body stands.
                    Stage = "route-fallback";
                    _failedOver = _pendingFallback;
                    _pendingFallback = null;
                    _fellBack = true;
                    foreach (var h in Plan())
                    {
                        yield return h;
                    }

                    _failedOver = null;
                    foreach (var h in Legs())
                    {
                        yield return h;
                    }
                }

                if (_replan)
                {
                    Log("recovery=route-after-untied: Primary Portal Recall said the ground is not tied; planning again from where the body stands");
                    _replan = false;
                    foreach (var h in Plan())
                    {
                        yield return h;
                    }

                    foreach (var h in Legs())
                    {
                        yield return h;
                    }

                    if (_replan)
                    {
                        Fail("recall:portal:untied-twice", "the plan after the untied recall planned the recall again");
                    }
                }

                if (stopAt is not null)
                {
                    StopAtCheck(stopAt);
                }

                _finished = true;
            }
            finally
            {
                Log("rung_change: " + VerdictFields());
            }
        }

        private IEnumerable<ActionHandle?> Plan()
        {
            Stage = "plan";
            var ground = Context.Dungeon;
            var routes = Context.Routes;
            _level = CharacterLevel;
            foreach (var h in AwaitPresent())
            {
                yield return h;
            }

            if (S.State.Self.Position is not { } here || S.State.Self.Travel != SelfTravel.Present)
            {
                Fail("rung-change:not-present", string.Create(CultureInfo.InvariantCulture, $"the player is not present in the world {PresentSeconds:0} s into the station (travel {S.State.Self.Travel})"));
                yield break;
            }

            var landblock = here.CellId >> 16;
            var place = routes.PlaceOf(RungRoute.Hex(landblock));
            var where = ground.InLandblocks(here.CellId) ? RungWhere.AtGround : place?.Key == routes.StartPlace ? RungWhere.Home : RungWhere.Elsewhere;
            var book = S.State.Self.Spellbook;
            var recallSpell = ground.Portal?.RecallSpell ?? DenTravelStation.PortalRecallSpell;
            var tieSpell = ground.Portal?.TieSpell ?? DenTravelStation.PortalTieSpell;
            var holdsGem = gem is not null && HeldOf(gem.Wcid).Count > 0;
            var input = new RungRouteInput(
                ground,
                direction,
                where,
                place?.Key ?? RungRoute.Hex(landblock),
                _level,
                book.Knows(Fighter.RecallHome.LifestoneRecallSpell) && !Context.Facts.LifestoneRecallUnavailable, // M6 7 AV: a recall refused for components is not available this run
                book.Knows(recallSpell),
                book.Knows(tieSpell),
                Context.Facts.TieOf(ground), // M6 4 F1 (4-R2): the one slot holding another ground is untied here
                holdsGem,
                where == RungWhere.Elsewhere ? RungRoute.BodyGroundOf(here.CellId, ground, Context.Grounds) : null, // M6 4 F1 (4-R3): the way home from the ground that holds the body
                stopAt, // M6 6 C2: the plan cut at the corpse's place
                Context.Facts.FailedRoutesOf(ground.Key)); // M6 7 M: a route that failed on a hop this run is planned last
            _plan = RungRoute.Decide(input, routes);
            if (direction == RungDirection.Home && input.KnowsLifestoneRecall && RungRoute.Decide(input with { KnowsLifestoneRecall = false }, routes) is { Stop: null, Legs.Count: > 0, Hops: 0 } onFoot)
            {
                // M6 7 AV: the same rule as AutopilotTravelRule.Home: where the way home is a walk, the station walks (the recall is no leg of it).
                Log($"rung_change: the way home from {input.Place} is on foot (route {onFoot.RouteKey}), so the recall is not planned");
                _plan = onFoot;
            }

            _tied = _plan.Tied;
            _gem = _gem == "used" ? _gem : _plan.Gem;
            _gate = _plan.Gate;
            Log(string.Create(
                CultureInfo.InvariantCulture,
                $"rung_change plan: direction={(direction == RungDirection.Home ? "home" : "out")} ground={ground.Key} where={where.ToString().ToLowerInvariant()} place={input.Place} cell=0x{here.CellId:X8} level={_level} book: lifestone_recall={Yes(input.KnowsLifestoneRecall)} portal_recall={Yes(input.KnowsPortalRecall)} portal_tie={Yes(input.KnowsPortalTie)} tie={input.Tie.ToString().ToLowerInvariant()} gem={(holdsGem ? "held" : "none")} route={_plan.RouteKey} legs=[{string.Join(",", _plan.Legs.Select(l => l.KindName + ":" + l.Label))}] tied={_plan.Tied} gate={_plan.Gate}{(stopAt is null ? string.Empty : " stop_at=" + stopAt)}; why: {_plan.Why}"));
            if (direction == RungDirection.Out && input.Tie == PortalTie.Unknown && ground.Entry is { Tieable: true })
            {
                Log($"{ground.Key}'s tie is unknown this journey (the autopilot carries a tie across runs in its checkpoint, m6-tie-memory; a plain journey has no checkpoint, so a tie an earlier run made is not known here): the route is taken, not the recall");
            }

            if (_failedOver is { } over)
            {
                var plannedRoute = RouteOf(_plan);
                var why = _plan.Stop is { } blocked ? $"the next plan stops: {blocked}"
                    : plannedRoute == over.RouteKey ? $"no other route of {ground.Key} admits level {_level}: the plan is route {plannedRoute} again"
                    : _plan.Legs.Any(l => l.Kind is RungLegKind.Hop or RungLegKind.Enter && l.Label == over.Edge) ? $"route {plannedRoute} takes {over.Edge}, the portal that just failed"
                    : null;
                if (why is not null)
                {
                    Log($"route_fallback: none ({why}); the hop's failure {over.Name} stands");
                    Fail(over.Name, over.Detail);
                }

                _routeFallback = $"{over.RouteKey}:{plannedRoute}:{over.Name.Replace(' ', '_')}";
                Log($"route_fallback={_routeFallback}: check after: the body stands at cell 0x{here.CellId:X8}; route {plannedRoute} is planned from there, legs=[{string.Join(",", _plan.Legs.Select(l => l.KindName + ":" + l.Label))}]");
            }

            if (_plan.Stop is { } stop)
            {
                Fail(stop.StartsWith("TODO(", StringComparison.Ordinal) ? stop[..(stop.IndexOf(')', StringComparison.Ordinal) + 1)] : stop, stop);
            }
        }

        /// <summary>
        /// M6 7 H (the live round 1: a death's respawn teleport still settling, <c>rung-change:not-present ... (travel TeleportArrived)</c>, 0.1 s after the station before it ended): a station that starts mid-teleport waits for the arrival, one line before and
        /// one after, at most <see cref="TravelLeg.PresentSeconds"/>; the check after the wait in <see cref="Plan"/> names a player still not present.
        /// </summary>
        private IEnumerable<ActionHandle?> AwaitPresent()
        {
            var self = S.State.Self;
            if (self.Travel == SelfTravel.Present && self.Position is not null)
            {
                yield break;
            }

            var from = S.Clock.Now;
            Log(string.Create(CultureInfo.InvariantCulture, $"rung_change: the player is not present yet (travel {self.Travel}): waiting for the arrival, at most {PresentSeconds:0} s"));
            foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && S.State.Self.Position is not null, PresentSeconds))
            {
                yield return h;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"rung_change: after {S.Clock.Now - from:0.0} s the travel is {S.State.Self.Travel}"));
        }

        private static string Yes(bool b) => b ? "yes" : "no";

        /// <summary>M6 6 C2: the check after a rung change cut at a place: present, and in one of the place's landblocks, else <c>rung-change:not-at-place:&lt;key&gt;</c>.</summary>
        private void StopAtCheck(string key)
        {
            var at = S.State.Self.Position;
            var place = Context.Routes.PlaceOf(key);
            if (at is { } p && S.State.Self.Travel == SelfTravel.Present && place is not null && place.Landblocks.Contains((ushort)(p.CellId >> 16)))
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"rung_change: stop_at={key}: check after: the player stands in {key} (cell 0x{p.CellId:X8})"));
                return;
            }

            Fail("rung-change:not-at-place:" + key, string.Create(CultureInfo.InvariantCulture, $"the rung change cut at {key} ended with the player at {(at is { } q ? $"cell 0x{q.CellId:X8}" : "an unknown place")} (travel {S.State.Self.Travel}), not in {key}'s landblocks ({(place is null ? "unknown place" : string.Join(", ", place.Landblocks.Select(l => RungRoute.Hex(l))))})"));
        }

        private IEnumerable<ActionHandle?> Legs()
        {
            var plan = _plan!;
            for (var n = 0; n < plan.Legs.Count; n++)
            {
                var leg = plan.Legs[n];
                var number = n + 1;
                Stage = leg.KindName;
                var started = S.Clock.Now;
                var from = S.State.Self.Position;
                var ok = false;
                var metres = 0.0;
                var extra = string.Empty;
                try
                {
                    switch (leg.Kind)
                    {
                        case RungLegKind.Gate when _takenMark is not null:
                            Log($"gate: {leg.Portal!.Name} took the player on touch during the run: the server let the body through, nothing to read (taken=touch)");
                            extra = " taken=touch";
                            break;
                        case RungLegKind.Tie when _takenMark is not null:
                            _tied = "no";
                            Context.Log.Warn("journey", $"[live {StationName}] tie: {leg.Portal!.Name} took the player on touch during the run, before the tie: the trip goes on untied (tied=no)");
                            extra = " tied=no taken=touch";
                            break;
                        case RungLegKind.Enter when _takenMark is { } takenMark:
                            Log($"hop {leg.Portal!.Key}: {leg.Portal.Name} took the player on touch during the run (taken=touch): no Use is sent; the arrival is checked as a Use's");
                            extra = " taken=touch";
                            foreach (var h in ArriveNamed(leg.Portal.DestLandblock, takenMark, "hop:" + leg.Portal.Key))
                            {
                                yield return h;
                            }

                            _hops++;
                            break;
                        case RungLegKind.LifestoneRecall:
                            foreach (var h in LifestoneRecall(leg))
                            {
                                yield return h;
                            }

                            break;
                        case RungLegKind.PortalRecall:
                            foreach (var h in PortalRecall(leg))
                            {
                                yield return h;
                            }

                            if (_replan)
                            {
                                extra = " untied=yes";
                                ok = true;
                                yield break;
                            }

                            break;
                        case RungLegKind.Hop:
                        case RungLegKind.Enter:
                            var standFights = FoughtBack; // M6 14 entry recharge: the recharge's and the top-up's fight-backs go on the leg's line
                            if (leg.Kind == RungLegKind.Enter && direction == RungDirection.Out)
                            {
                                foreach (var h in Recharge(entry: true, leg, _runFrom))
                                {
                                    yield return h; // M6 7 AN (the owner): never enter short of the bar; M6 14: by the vitals policy, an attacker fought back (RungChangeStation.Recharge.cs)
                                }

                                if (_enterShort is { } shortOfBar)
                                {
                                    extra = FightWord(standFights);
                                    Fail(_rechargeFail ?? RechargeNotFull, shortOfBar);
                                    yield break;
                                }
                            }
                            else if (leg.Kind == RungLegKind.Hop)
                            {
                                foreach (var h in TopUpBeforeHop(leg))
                                {
                                    yield return h; // M6 14 entry recharge: a drop among a ground's hostiles is topped up for at the last safe place
                                }
                            }

                            var hop = new PortalHopLeg(Context, leg.Portal!, $"{plan.RouteKey} leg {n + 1} of {plan.Legs.Count} to {(direction == RungDirection.Home ? "home" : Context.Dungeon.Key)}");
                            var status = ScenarioStatus.Running;
                            while ((status = hop.Step(S, S.Clock.Now)) == ScenarioStatus.Running)
                            {
                                yield return null;
                            }

                            var seen = hop.Outcome;
                            metres = seen.WalkMetres;
                            extra = FightWord(standFights) + (seen.Taken ? " taken=yes" : string.Empty) + (seen.Recoveries > 0 ? " recovery=use-again" : string.Empty);
                            if (status == ScenarioStatus.Succeeded && hop.Failure is null)
                            {
                                _hops++;
                                foreach (var h in TownStockStops())
                                {
                                    yield return h; // M6 14 T: the stop in another town, there and back, where the hop dropped her at its door
                                }

                                break;
                            }

                            var name = hop.FailureName ?? leg.Label + ":failed";
                            if (name.StartsWith("gate:", StringComparison.Ordinal))
                            {
                                _gate = "refused:" + (leg.Portal!.MinLevel is { } min && _level < min ? min : leg.Portal.MaxLevel ?? 0).ToString(CultureInfo.InvariantCulture);
                            }

                            var intoIsland = direction == RungDirection.Out && gem is not null && _gem == "held" && !name.StartsWith("gate:", StringComparison.Ordinal) && !EndsTheRun(name) ? GemTarget(plan, n) : -1; // M6 4 F6: a death is never replaced by the gem
                            if (intoIsland >= 0)
                            {
                                var skipped = plan.Legs.Skip(n + 1).Take(intoIsland - n).Select(l => l.Label);
                                Log($"hop {leg.Label} failed by name ({name}: {hop.Failure}); the {gem!.Name} is held and the route's next place is {gem.ReplacesHopsInto}: it replaces the hops from here to {plan.Legs[intoIsland].Label} (gem=used; skipped=[{string.Join(",", skipped)}])");
                                extra += " gem=used";
                                foreach (var h in UseGem())
                                {
                                    yield return h;
                                }

                                n = intoIsland; // the legs on go on from the gem's drop (the hop into the island is the gem's)
                                break;
                            }

                            if (direction == RungDirection.Out && !_fellBack && !name.StartsWith("gate:", StringComparison.Ordinal) && !EndsTheRun(name))
                            {
                                // M6 7 M: check, act, check. Before: the hop failed by name. Act: remember the route as failed, plan the ground's next admitted route from where the body stands (once a station run). After: Plan() checks the new plan.
                                var failedRoute = RouteOf(plan);
                                Context.Facts.RouteFailed(Context.Dungeon.Key, failedRoute);
                                Log($"route_fallback: hop {leg.Label} of route {failedRoute} failed by name ({name}: {hop.Failure}); check before: the body stands at cell {(S.State.Self.Position is { } at ? "0x" + at.CellId.ToString("X8", CultureInfo.InvariantCulture) : "unknown")}, travel {S.State.Self.Travel}; act: the next admitted route is planned once from there");
                                _pendingFallback = new FailedHop(failedRoute, leg.Label, name, hop.Failure ?? "the hop failed");
                                extra += " route_fallback=pending";
                                yield break;
                            }

                            Fail(name, hop.Failure ?? "the hop failed");
                            break;
                        case RungLegKind.Run when leg.Via is { } viaWhy:
                            // M6 7 Z: a bend on the way, a cost and not a wall. Check: a via no nearer the entry's front than the body is behind it (a corpse trip's way back, a fight-back that carried the body on), skipped by name. Act: the guarded walk.
                            // After: a death or a retreat ends the leg by its own name (FailName); the walk's own failure skips the via by name, and the next leg plans from where the body stands.
                            HopStep = "run:" + leg.Label;
                            if (ViaBehind(plan, n, leg) is { } behind)
                            {
                                Log($"via_skipped: {leg.Label}: {behind}");
                                extra = " via=behind";
                                break;
                            }

                            var viaFights = FoughtBack;
                            string? viaFailed = null;
                            var viaKeepOff = plan.Legs.Skip(n + 1).FirstOrDefault(l => l.Kind == RungLegKind.Enter)?.Portal?.KeepOffTheOthers(); // M6 7 AI: a bend of the walk to the entry keeps clear as the walk does
                            try
                            {
                                foreach (var h in Catching(WalkGuarded(leg.KindName, "rung-change", leg.Label, leg.Cell, leg.At, TravelPurpose.Cell, $"{leg.Label} of the run to {Context.Dungeon.Name}'s entry portal ({plan.RouteKey}): {viaWhy}", keepOff: viaKeepOff), f => viaFailed = f))
                                {
                                    yield return h;
                                }
                            }
                            finally
                            {
                                extra = FightWord(viaFights);
                            }

                            if (viaFailed is not null)
                            {
                                if (FailName is not null)
                                {
                                    throw new InvalidOperationException(viaFailed);
                                }

                                Context.Log.Warn("journey", $"[live {StationName}] via_skipped: {leg.Label}: the walk failed by name and the run goes on from where the body stands: {viaFailed}");
                                extra += " via=skipped";
                            }

                            break;
                        case RungLegKind.Run:
                            HopStep = "run:" + leg.Label;
                            _runFrom = from; // M6 14 entry recharge: a retreat on foot from the entry's front walks back to where the run began
                            var fightsBefore = FoughtBack;
                            var runMark = TravelMark;
                            var entryPortal = plan.Legs.Skip(n + 1).FirstOrDefault(l => l.Kind == RungLegKind.Enter)?.Portal;
                            var entered = entryPortal?.DestLandblock; // M6 7 N: the ground this run is running to: a move into it is the entry taken on touch
                            if (direction == RungDirection.Out)
                            {
                                BeginCrossing(Context.Dungeon, leg.Cell, leg.At); // M6 14 approach cost (rule 2): the crossing of the ground's approach is measured on the way
                            }

                            try
                            {
                                foreach (var h in WalkGuarded(leg.KindName, "rung-change", "entry-front", leg.Cell, leg.At, TravelPurpose.Cell, $"to the front of {Context.Dungeon.Name}'s entry portal on foot ({plan.RouteKey})", entered, entryPortal?.KeepOffTheOthers(), door: true)) // M6 4 F6: life checks and the fight-back; M6 14 run past (D137): she runs past what she outpaces and fights at the door what is still on her
                                {
                                    yield return h;
                                }
                            }
                            finally
                            {
                                extra = FightWord(fightsBefore);
                                EndCrossing();
                            }

                            if (WalkTakenFrom is not null)
                            {
                                _takenMark = runMark;
                                extra += " taken=touch";
                            }

                            break;
                        case RungLegKind.WalkHome:
                            HopStep = "walk-home:" + leg.Label;
                            var homeFightsBefore = FoughtBack;
                            try
                            {
                                foreach (var h in WalkGuarded(leg.KindName, "rung-change", "home-lifestone-point", leg.Cell, leg.At, TravelPurpose.Bind, $"home to {Context.Dungeon.Home.Town}'s lifestone point on foot ({plan.Why})"))
                                {
                                    yield return h;
                                }
                            }
                            finally
                            {
                                extra = FightWord(homeFightsBefore);
                            }

                            break;
                        case RungLegKind.Gate:
                            var level = CharacterLevel;
                            if (leg.Portal!.Refusal(level) is { } refused)
                            {
                                _gate = "refused:" + (leg.Portal.MinLevel is { } lo && level < lo ? lo : leg.Portal.MaxLevel ?? 0).ToString(CultureInfo.InvariantCulture);
                                Fail(refused, $"level {level} does not pass {Context.Dungeon.Name}'s entry (read at the front, before the Use)");
                            }

                            Log(string.Create(CultureInfo.InvariantCulture, $"gate: level {level} passes {leg.Portal.Key} ({leg.Portal.MinLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"}..{leg.Portal.MaxLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"})"));
                            break;
                        case RungLegKind.Tie:
                            foreach (var h in Tie(leg))
                            {
                                yield return h;
                            }

                            extra = " tied=" + _tied;
                            break;
                        default:
                            Fail("rung-change:leg-kind", $"TODO(m6-rung-leg-kind): no runner for leg kind {leg.Kind}");
                            break;
                    }

                    ok = true;
                    HopStep = null;
                }
                finally
                {
                    var to = S.State.Self.Position;
                    var walkedTo = leg.Kind == RungLegKind.Run ? WalkTakenFrom ?? to : to; // M6 7 N: a run the portal took ends where the body stood before the teleport
                    if (leg.Kind is RungLegKind.Run or RungLegKind.WalkHome && from is { } a && walkedTo is { } b && MetresBetween(a, b) is var walked && !double.IsNaN(walked))
                    {
                        metres = walked;
                    }

                    _walked += metres;
                    Log(string.Create(
                        CultureInfo.InvariantCulture,
                        $"rung_change_leg: n={number} kind={leg.KindName} edge={leg.Label} from={Lb(from)} to={Lb(to)} metres={metres:0} ms={(S.Clock.Now - started) * 1000:0} ok={(ok ? "yes" : "no")}{extra}"));
                }
            }
        }

        /// <summary>
        /// M6 7 Z: the reason the via <paramref name="leg"/> is behind the body, or null: the plan's run to the front is nearer the body than to the via, so walking to it would turn back. Null too when the body or the front is not known in metres (the via is walked).
        /// </summary>
        private string? ViaBehind(RungPlan plan, int n, RungLeg leg)
        {
            if (S.State.Self.Position is not { } body || plan.Legs.Skip(n + 1).FirstOrDefault(l => l is { Kind: RungLegKind.Run, Via: null }) is not { } front)
            {
                return null;
            }

            var goal = new CellFrame(front.Cell, new AcFrame(front.At, System.Numerics.Quaternion.Identity));
            var bodyToFront = MetresBetween(body, goal);
            var viaToFront = MetresBetween(new CellFrame(leg.Cell, new AcFrame(leg.At, System.Numerics.Quaternion.Identity)), goal);
            return !double.IsNaN(bodyToFront) && !double.IsNaN(viaToFront) && bodyToFront <= viaToFront
                ? string.Create(CultureInfo.InvariantCulture, $"the body stands {bodyToFront:0} m from the front, the via {viaToFront:0} m: walking to it would turn back")
                : null;
        }

        /// <summary>M6 7 M: a hop that failed by name, kept for the one fallback of a station run.</summary>
        private sealed record FailedHop(string RouteKey, string Edge, string Name, string Detail);

        /// <summary>The plan's route key without a <c>stop_at</c> suffix.</summary>
        private static string RouteOf(RungPlan plan) => plan.RouteKey.Split(RungRoute.StopAtSeparator)[0];

        /// <summary>M6 4 F6: a failure name that is a death on a leg (<c>died:&lt;leg&gt;:respawned=&lt;cell&gt;</c>) or a dead body before one.</summary>
        internal static bool IsDeath(string name) => name.StartsWith("died:", StringComparison.Ordinal) || name.StartsWith("dead:", StringComparison.Ordinal);

        /// <summary>M6 8 G: true for a hop's failure no other route or gem answers: a death, or the walk's fight-back giving up (a retreat, a failed retreat, no bolt to fight with): she already left, or must not walk on.</summary>
        internal static bool EndsTheRun(string name) => IsDeath(name) || name.StartsWith("retreat:", StringComparison.Ordinal) || name.StartsWith("retreat-failed:", StringComparison.Ordinal) || name.StartsWith("fight-back:", StringComparison.Ordinal);

        /// <summary>The leg line's fight-back field (<c> fought_back=&lt;n&gt; kills=&lt;n&gt;</c>), empty with none.</summary>
        private string FightWord(int before)
        {
            var word = FoughtBack > before ? string.Create(CultureInfo.InvariantCulture, $" fought_back={FoughtBack - before} kills={FightBackKills}") : string.Empty;
            if (RanPast > _ranPastSaid)
            {
                word += string.Create(CultureInfo.InvariantCulture, $" ran_past={RanPast - _ranPastSaid}"); // M6 14 run past (D137): the attackers left behind or passed on the leg
                _ranPastSaid = RanPast;
            }

            return word;
        }

        private int _ranPastSaid;

        private static string Lb(CellFrame? at) => at is { } p ? RungRoute.Hex(p.CellId >> 16) : "none";

        /// <summary>
        /// The index of the first hop from leg <paramref name="from"/> on whose destination lies in the gem's place (the island), or -1: the route's next place is the island and the gem replaces the hops up to it
        /// (M6 4 F2, review 4-R6; the leg that failed itself when it is that hop). The gem is item 5's to obtain (Brighteyes's tusk turn-ins): nothing here acquires it.
        /// </summary>
        private int GemTarget(RungPlan plan, int from)
        {
            for (var m = from; m < plan.Legs.Count; m++)
            {
                if (plan.Legs[m] is { Kind: RungLegKind.Hop, Portal: { } portal } && GemPlaceHolds(portal.DestLandblock))
                {
                    return m;
                }
            }

            return -1;
        }

        private bool GemPlaceHolds(uint landblock) => gem is not null && Context.Routes.PlaceOf(gem.ReplacesHopsInto) is { } place && place.Landblocks.Contains((ushort)landblock);

        private IEnumerable<ActionHandle?> UseGem()
        {
            Stage = "gem";
            HopStep = "gem:use";
            var g = gem ?? throw new InvalidOperationException("the gem is used only when the travel file has one");
            var held = HeldOf(g.Wcid);
            if (held.Count == 0)
            {
                Fail("gem:not-held", $"the {g.Name} ({g.Wcid}) is no longer in the pack");
            }

            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var guid = held.Min();
            var travelMark = TravelMark;
            var use = S.Actions.Use(guid, ActionSource.Automation);
            Log($"gem: use {g.Name} 0x{guid:X8} (check after: arrival in landblock 0x{g.DestLandblock:X4})");
            foreach (var h in ArriveNamed(g.DestLandblock, travelMark, "gem"))
            {
                yield return h;
            }

            Log($"gem: arrived ({Describe(use)})");
            _gem = "used";
            HopStep = null;
        }

        private IEnumerable<ActionHandle?> LifestoneRecall(RungLeg leg)
        {
            var landblock = leg.Cell >> 16;
            HopStep = "recall:lifestone";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            foreach (var h in ArrivalBeforeCast("recall:lifestone"))
            {
                yield return h; // M6 14: never cast into a portal space (the route fallback's recall at 19:20:59 UTC)
            }

            var cast = new CastResult();
            foreach (var h in Cast(leg.Spell, 0, cast))
            {
                yield return h;
            }

            foreach (var h in Until(() => Refusal(this, cast) != 0 || TeleportSince(cast.TravelMark), TeleportStartSeconds))
            {
                yield return h;
            }

            var refusal = Refusal(this, cast);
            if (refusal != 0)
            {
                if (refusal == 0x049E)
                {
                    Context.Facts.LifestoneTied = false;
                }

                Fail("recall:lifestone:refused", "Lifestone Recall was refused: " + Named(refusal));
            }

            foreach (var h in ArriveNamed(landblock, cast.TravelMark, "recall:lifestone"))
            {
                yield return h;
            }

            Context.Facts.LifestoneTied = true;
            Context.Facts.Recalls++;
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            HopStep = null;
        }

        private IEnumerable<ActionHandle?> PortalRecall(RungLeg leg)
        {
            var key = Context.Dungeon.Key;
            HopStep = "recall:portal";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            foreach (var h in ArrivalBeforeCast("recall:portal"))
            {
                yield return h; // M6 14: never cast into a portal space
            }

            var cast = new CastResult();
            foreach (var h in Cast(leg.Spell, 0, cast))
            {
                yield return h;
            }

            foreach (var h in Until(() => Refusal(this, cast) != 0 || TeleportSince(cast.TravelMark), TeleportStartSeconds))
            {
                yield return h;
            }

            var refusal = Refusal(this, cast);
            if (refusal == 0x04A3)
            {
                Context.Facts.SetPortalTie(key, PortalTie.Untied);
                Log($"Primary Portal Recall refused: {Named(refusal)}; {key} is untied");
                _replan = true;
                foreach (var h in PeaceNow())
                {
                    yield return h;
                }

                yield break;
            }

            if (refusal != 0)
            {
                Fail("recall:portal:refused", "Primary Portal Recall was refused: " + Named(refusal));
            }

            Context.Facts.SetPortalTie(key, PortalTie.Unknown); // until the arrival says which portal the tie holds
            foreach (var h in Until(() => TeleportSince(cast.TravelMark) && S.State.Self.Travel == SelfTravel.Present, TeleportStartSeconds))
            {
                yield return h;
            }

            if (S.State.Self.Position is { } landed && landed.CellId >> 16 != leg.Cell >> 16 && JourneyFacts.SlotHolderAt(landed.CellId, Context.Dungeon, Context.Grounds) is { } holder)
            {
                // M6 4 F1 (4-R2): no 0x04A3: ACE teleported to the portal the one slot holds, another known ground's arrival; the facts say so, and the stop names it.
                Context.Facts.SetPrimaryPortalTie(holder.Key);
                Fail(JourneyFacts.SlotHoldsFailure(holder), string.Create(CultureInfo.InvariantCulture, $"Primary Portal Recall landed in cell 0x{landed.CellId:X8}, {holder.Key}'s arrival: the primary tie's one slot holds {holder.Key}, so {key} is untied"));
            }

            foreach (var h in ArriveNamed(leg.Cell >> 16, cast.TravelMark, "recall:portal"))
            {
                yield return h;
            }

            Context.Facts.SetPrimaryPortalTie(key);
            Context.Facts.Recalls++;
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            HopStep = null;
        }

        private IEnumerable<ActionHandle?> Tie(RungLeg leg)
        {
            var portal = leg.Portal!;
            var key = Context.Dungeon.Key;
            HopStep = "tie:find";
            var cell = CellAt(portal.Cell, portal.Origin);
            var found = 0u;
            foreach (var h in Until(() => (found = FindPortalObject(portal.Guid, portal.Wcid, cell, portal.Origin)) != 0, FindSeconds))
            {
                yield return h;
            }

            if (found == 0)
            {
                Fail(portal.Key + ":no-portal", $"no object of weenie {portal.Wcid} near the entry within {FindSeconds:0} s (the tie and the Use need it)");
            }

            HopStep = "tie:cast";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            var tie = new CastResult();
            foreach (var h in Cast(leg.Spell, found, tie))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(tie.Mark, "linked with the portal") || Refusal(this, tie) != 0, TextSeconds))
            {
                yield return h;
            }

            if (TextSince(tie.Mark, "linked with the portal"))
            {
                Context.Facts.SetPrimaryPortalTie(key);
                _tied = "yes";
                Log($"tie: linked with {portal.Name} 0x{found:X8}: {key} is tied (the primary tie is one slot: every other ground with a tie record is now known untied)");
            }
            else
            {
                _tied = "no";
                var refusal = Refusal(this, tie);
                Context.Log.Warn("journey", $"[live {StationName}] tie: Primary Portal Tie on {portal.Name} 0x{found:X8} did not link ({(refusal != 0 ? Named(refusal) : Describe(tie.Handle!))}); the trip goes on untied (tied=no)");
            }

            HopStep = null;
        }
    }
}
