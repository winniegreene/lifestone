using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>Which travel station the autopilot's Travel state runs (M6 item 4 t3b).</summary>
public enum TravelWay
{
    /// <summary><c>den-travel</c>: a tied ground's Primary Portal Recall, an unknown tie's recall tried, an outdoor ground's walk, a route with no portal and no run (the ground's own station).</summary>
    DenTravel,

    /// <summary><c>rung-change</c> (<see cref="RungChangeStation"/>): out by the travel file's route, or home by portals.</summary>
    RungChange,

    /// <summary><c>recall-home</c>: Lifestone Recall.</summary>
    Recall,

    /// <summary>The named stop <see cref="StartupAudit.PortalGapStop"/>: the ground's portal is known untied and no route of the travel file reaches it (<c>TODO(m5-portal-retie-route)</c>, narrowed to such a ground).</summary>
    Gap,

    /// <summary>M6 7 BC: the body already stands at home (<see cref="RungWhere.Home"/>): no way home is run, the recovery goes to the known state (a recall from home spends mana and a component for nothing).</summary>
    AtHome,
}

/// <summary>The Travel state's choice and its reason (one log line: the purpose, M5 D47).</summary>
/// <param name="Way">The station.</param>
/// <param name="Reason">The decision in one clause.</param>
/// <param name="Route">The rung change's route key when it planned one (<see cref="RungPlan.RouteKey"/>), else null.</param>
public sealed record TravelDecision(TravelWay Way, string Reason, string? Route = null)
{
    /// <summary>The way as the log writes it (<c>den-travel</c>, <c>rung-change</c>, <c>recall-home</c>, <c>gap</c>).</summary>
    public string WayName => Way switch
    {
        TravelWay.DenTravel => "den-travel",
        TravelWay.RungChange => RungChangeStation.StationName,
        TravelWay.Recall => "recall-home",
        TravelWay.AtHome => "at-home",
        _ => "gap",
    };
}

/// <summary>
/// The autopilot's travel rule (M6 item 4 t3b, D50, D51, D43), pure: which station takes the climber to its rung's ground, and which takes it home.
/// <para><b>Out</b> (<see cref="Out"/>), in order: an outdoor ground walks (<c>den-travel</c>); a ground with a tie record (<see cref="HuntingGround.Portal"/>) known tied recalls by <c>den-travel</c>'s Primary Portal Recall; a tie record whose tie is
/// unknown, with a tieable entry (or none) and the recall spell in the book (or the book unread), tries the recall by <c>den-travel</c> (M5's rule: unknown is a recall tried, ACE's answer decides; its 0x04A3 sets the tie untied and the rule is read
/// again); otherwise <see cref="RungRoute.Decide"/> plans: a plan with legs (the travel file's route has steps or a run: the island grounds, the Temples untied) or with a named stop (a gate, <c>TODO(m6-ground-route)</c>) runs <c>rung-change</c>,
/// which names its own failure (<c>fail=</c>); a plan with neither is the ground's own station (<c>den-travel</c>), unless the tie is known untied: then no route reaches the portal and the run stops by the named gap.</para>
/// <para><b>Home</b> (<see cref="Home"/>): at home already (M6 7 BC), <see cref="TravelWay.AtHome"/>, no station; else Lifestone Recall when the book holds it (or is unread: no absence is guessed); else, when <see cref="RungRoute.Decide"/> finds a way home by portals from where the body stands (the island, a hub, a stop
/// town, a routed ground's exit), <c>rung-change</c> direction=home; else the recall as before (its refusal names itself).</para>
/// No ground, town or gate fact lives here (D26): each comes from the profile, the travel file or the spellbook.
/// </summary>
public static class AutopilotTravelRule
{
    /// <summary>True when <paramref name="book"/> was read from the server: ACE's player description arrived and carries spells (a mage's book is never empty), so a spell it lacks is absent, not unread.</summary>
    /// <param name="book">The spellbook.</param>
    public static bool BookRead(Spellbook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.IsKnown && book.Count > 0;
    }

    /// <summary>The rung route's input from the session's state, as <see cref="RungChangeStation"/> builds it at its start (the gem is never planned, so it reads as not held).</summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The rung's ground.</param>
    /// <param name="facts">The journey's facts (the ground's tie).</param>
    /// <param name="routes">The travel file.</param>
    /// <param name="direction">Out or home.</param>
    /// <param name="grounds">M6 4 F1 (4-R3): the grounds whose landblocks may hold the body (<see cref="RungRoute.BodyGroundOf"/>; <see cref="JourneyContext.Grounds"/>), or null for the catalog's.</param>
    /// <param name="spells">M6, the recall's components: the spell table, so Lifestone Recall counts as the way home only when the pack holds one cast's components (<see cref="RecallReadiness"/>); null: the book and the run's refusal alone.</param>
    public static RungRouteInput Input(GameState state, HuntingGround ground, JourneyFacts facts, TravelRoutes routes, RungDirection direction, IReadOnlyList<HuntingGround>? grounds = null, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(routes);
        var self = state.Self;
        var where = RungWhere.Elsewhere;
        var place = "none";
        HuntingGround? body = null;
        if (self.Position is { } here)
        {
            var landblock = RungRoute.Hex(here.CellId >> 16);
            var known = routes.PlaceOf(landblock);
            place = known?.Key ?? landblock;
            where = ground.InLandblocks(here.CellId) ? RungWhere.AtGround : known?.Key == routes.StartPlace ? RungWhere.Home : RungWhere.Elsewhere;
            body = where == RungWhere.Elsewhere ? RungRoute.BodyGroundOf(here.CellId, ground, grounds ?? GroundCatalog.All) : null;
        }

        var book = self.Spellbook;
        var recall = RecallReadiness.Of(state, facts, spells); // M6 7 AV: a recall refused for components is not available this run; M6, the recall's components: nor is one the pack is short for (the check before the first cast)
        return new RungRouteInput(
            ground,
            direction,
            where,
            place,
            self.Qualities.Level,
            recall.Castable,
            book.Knows(ground.Portal?.RecallSpell ?? DenTravelStation.PortalRecallSpell),
            book.Knows(ground.Portal?.TieSpell ?? DenTravelStation.PortalTieSpell),
            facts.TieOf(ground),
            false,
            body,
            RecallWhyNot: recall.WhyNot);
    }

    /// <summary>The way out to the ground (see the class).</summary>
    /// <param name="input">Where, who and what the book holds (<see cref="Input"/>).</param>
    /// <param name="bookRead">The book was read (<see cref="BookRead"/>).</param>
    /// <param name="routes">The travel file.</param>
    public static TravelDecision Out(RungRouteInput input, bool bookRead, TravelRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(routes);
        var g = input.Ground;
        if (g.Kind == GroundKind.Outdoor)
        {
            return new(TravelWay.DenTravel, $"{g.Key} is an outdoor ground: den-travel walks to its first cell");
        }

        var recall = g.Portal?.RecallSpell ?? DenTravelStation.PortalRecallSpell;
        if (g.Portal is not null && input.Tie == PortalTie.Tied)
        {
            return new(TravelWay.DenTravel, string.Create(CultureInfo.InvariantCulture, $"{g.Key} is known tied: den-travel's Primary Portal Recall {recall}"));
        }

        var untieable = g.Entry is { Tieable: false };
        if (g.Portal is not null && input.Tie == PortalTie.Unknown && !untieable && (!bookRead || input.KnowsPortalRecall))
        {
            return new(TravelWay.DenTravel, string.Create(CultureInfo.InvariantCulture, $"{g.Key}'s tie is unknown and the spellbook {(bookRead ? $"holds Primary Portal Recall {recall}" : "is unread")}: den-travel tries the recall (ACE's 0x04A3 turns the travel to the rung change)"));
        }

        var plan = RungRoute.Decide(input with { Direction = RungDirection.Out }, routes);
        var tie = input.Tie.ToString().ToLowerInvariant();
        if (plan.Legs.Count > 0 || plan.Stop is not null)
        {
            var why = plan.Stop is { } stop ? $"its plan stops by name ({stop}): the station names the failure" : string.Create(CultureInfo.InvariantCulture, $"{plan.Legs.Count} legs, {plan.Hops} portals");
            return new(TravelWay.RungChange, $"{g.Key} (tie {tie}, entry {(g.Entry is null ? "unknown" : untieable ? "untieable" : "tieable")}): route {plan.RouteKey}, {why}", plan.RouteKey);
        }

        if (input.Tie == PortalTie.Untied)
        {
            return new(TravelWay.Gap, $"{g.Key}'s portal is known untied and its route {plan.RouteKey} has no portal and no run in {TravelRoutes.FileName}: no route reaches the portal without a place (TODO(m5-portal-retie-route))", plan.RouteKey);
        }

        return new(TravelWay.DenTravel, $"{g.Key}'s route {plan.RouteKey} has no portal and no run (tie {tie}): the ground's own station", plan.RouteKey);
    }

    /// <summary>The way home (see the class).</summary>
    /// <param name="input">Where, who and what the book holds (<see cref="Input"/>).</param>
    /// <param name="bookRead">The book was read (<see cref="BookRead"/>).</param>
    /// <param name="routes">The travel file.</param>
    public static TravelDecision Home(RungRouteInput input, bool bookRead, TravelRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(routes);
        if (input.Where == RungWhere.Home)
        {
            // M6 7 BC (live, gate A climb 19: a bind that had taken, standing at Holtburg's lifestone, recovered by a recall home that was refused): a body at home needs no way home, and never recalls from it.
            return new(TravelWay.AtHome, string.Create(CultureInfo.InvariantCulture, $"the body stands at home ({input.Place}): no way home is run, the known state classifies where she stands"));
        }

        // M6 7 lead (2026-11-06, live): a body where the way home is a walk (an outdoor ground hunted from home, the outskirts) goes on foot: the recall spends mana and a component for nothing, and a missing component stopped the run 180 m from the town.
        // M6 7 AV: read from the plan's legs, never the run's ground kind (a run on the Lodge, standing in the outskirts, took the recall): the plan without the recall has legs, no portal and no stop.
        var walk = RungRoute.Decide(input with { Direction = RungDirection.Home, KnowsLifestoneRecall = false }, routes);
        if (walk.Stop is null && walk.Legs.Count > 0 && walk.Hops == 0)
        {
            return new(TravelWay.RungChange, string.Create(CultureInfo.InvariantCulture, $"{(input.Ground.Kind == GroundKind.Outdoor ? $"{input.Ground.Key} is an outdoor ground: " : string.Empty)}{input.Place} has a way home on foot (no portal on its route), whatever the run's ground ({input.Ground.Key}): route {walk.RouteKey}"), walk.RouteKey);
        }

        if (input.KnowsLifestoneRecall)
        {
            return new(TravelWay.Recall, "the spellbook holds Lifestone Recall");
        }

        if (!bookRead)
        {
            return new(TravelWay.Recall, "the spellbook is unread: the recall as before (a refusal names itself)");
        }

        var plan = RungRoute.Decide(input with { Direction = RungDirection.Home }, routes);
        if (plan.Stop is null && plan.Legs.Count > 0)
        {
            return new(TravelWay.RungChange, string.Create(CultureInfo.InvariantCulture, $"{input.RecallWhyNot ?? "the spellbook lacks Lifestone Recall"} (D43) and {input.Place} has a way home: route {plan.RouteKey}, {plan.Hops} portals"), plan.RouteKey);
        }

        return new(TravelWay.Recall, $"{input.RecallWhyNot ?? "the spellbook lacks Lifestone Recall"} and {input.Place} has no way home by portals ({plan.Stop ?? plan.Why}): the recall as before, its refusal stops the run by name");
    }
}
