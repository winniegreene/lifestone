using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>The autopilot's answer to a carried line under its stop line (<see cref="AutopilotBuyTripRule.Call"/>).</summary>
/// <param name="Queue">True when the buy-trip station is queued; false when the audit's stop stands.</param>
/// <param name="Via">How the seller's town is reached: <c>in-town</c>, <c>budgeted-teleport</c>, <c>town-network</c>, or <c>none</c>.</param>
/// <param name="Reason">The decision in one clause, with its inputs (one log line).</param>
public sealed record BuyTripCall(bool Queue, string Via, string Reason);

/// <summary>
/// M6 item 4 t4b (D44, closes <c>m6-buy-trip-autopilot</c>), pure: whether the autopilot queues <see cref="BuyTripStation"/> for a carried line under its stop line (the Pyreal Scarab from VI, the Platinum Scarab from VII, D4, D12), or leaves the
/// audit's stop. The station's own decision (<see cref="BuyTripRule.Decide"/>) is not repeated: it must be <see cref="BuyTripKind.Due"/>, and the seller's town must be reachable: the mage is already in it, or the journey has a teleport budget
/// (the built character's one budgeted teleport), or (M6 4 F1, 4-R4) the station's own choice by the Town Network admits a seller: <see cref="TownNetworkTrip.Choose"/> over the catalog's sellers of the scarab with the station's inputs
/// (<see cref="TownInput"/>: the body's travel place, the level, Lifestone Recall, the ground's home; the lever-only doors; the town's own way back), its refusals logged on the line. No town or place fact lives here (D26): each is the catalog's,
/// a profile's or the travel file's.
/// </summary>
public static class AutopilotBuyTripRule
{
    /// <summary>The buy-trip's call for <paramref name="decision"/>.</summary>
    /// <param name="decision">The station's rule read over the ground's counters.</param>
    /// <param name="facts">The journey's facts (the teleport budget).</param>
    /// <param name="landblock">The landblock the mage stands in, or null when unread.</param>
    /// <param name="catalog">The scrivener catalog (the town's landblocks).</param>
    /// <param name="routes">The travel file (the journey's, <see cref="JourneyContext.Routes"/>).</param>
    /// <param name="trip">The Town Network rule's input as the station builds it (<see cref="TownInput"/>).</param>
    /// <param name="doors">The lever-only doors (the station's: <see cref="LeverDoors.Default"/>).</param>
    public static BuyTripCall Call(BuyTripDecision decision, JourneyFacts facts, ushort? landblock, ScrivenerCatalog catalog, TravelRoutes routes, TownTripInput trip, LeverDoors doors)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(doors);
        if (decision.Kind != BuyTripKind.Due)
        {
            return new BuyTripCall(false, "none", $"the buy-trip is {decision.Kind.ToString().ToLowerInvariant()}: {decision.Why}");
        }

        var town = decision.Place!.Town;
        if (landblock is { } here && BuyTripStation.TownLandblocks(town, decision.Arrival!, catalog).Contains(here))
        {
            return new BuyTripCall(true, "in-town", $"the {decision.Line!.Name} is under its stop line and it stands in {town}");
        }

        if (!BuyTripStation.NoTeleportBudget(facts))
        {
            return new BuyTripCall(true, "budgeted-teleport", string.Create(CultureInfo.InvariantCulture, $"the {decision.Line!.Name} is under its stop line and the journey has {facts.TeleportBudget} teleport(s) to {town}"));
        }

        // M6 4 F1 (4-R4): the station's own choice (BuyTripStation's run asks the same rule with the same inputs), so a call never queues a trip the station would refuse.
        var candidates = catalog.ScarabSellersOf(decision.Line!.Wcid).SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).ToList();
        var choice = TownNetworkTrip.Choose(candidates, trip, routes, doors);
        var inputs = string.Create(CultureInfo.InvariantCulture, $"from {trip.StartPlace ?? "no travel place"}{(landblock is null ? " (the body unread: the home lifestone's place)" : string.Empty)}, level {trip.Level}, lifestone_recall={(trip.KnowsLifestoneRecall ? "yes" : "no")}");
        if (choice.Chosen is not { } chosen)
        {
            return new BuyTripCall(false, "none", $"no teleport budget, and the Town Network choice admits no seller of the {decision.Line.Name} ({choice.Stop}; {inputs}): {choice.Why}");
        }

        return new BuyTripCall(true, TownNetworkTrip.TravelWord, $"the {decision.Line.Name} is under its stop line, no teleport budget: the Town Network trip to {chosen.Candidate.Name} in {chosen.Candidate.Town} (hops {chosen.HopKeys}, home by {chosen.Home}; {inputs}): {choice.Why}");
    }

    /// <summary>
    /// M6 4 F1 (4-R4): the Town Network rule's input as <see cref="BuyTripStation"/> builds it at its run: the travel place the body stands in (else the home lifestone's), the level, Lifestone Recall in the book, and whether the ground's home is the
    /// travel file's start.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="ground">The run's ground.</param>
    /// <param name="routes">The travel file.</param>
    /// <param name="facts">M6, the recall's components: the journey's facts, so Lifestone Recall counts only when castable (<see cref="RecallReadiness"/>); null: the book alone.</param>
    /// <param name="spells">The spell table for that read, or null: the components are not judged.</param>
    public static TownTripInput TownInput(GameState state, HuntingGround ground, TravelRoutes routes, JourneyFacts? facts = null, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(routes);
        return new TownTripInput(
            (state.Self.Position is { } at ? TownNetworkTrip.PlaceKeyOf(at.CellId >> 16, routes) : null) ?? TownNetworkTrip.PlaceKeyOf(ground.Home.LifestoneCell >> 16, routes),
            state.Self.Qualities.Level,
            facts is null ? state.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell) : RecallReadiness.Of(state, facts, spells).Castable, // M6, the recall's components (RecallReadiness)
            routes.StartLifestone.Cell >> 16 == ground.Home.LifestoneCell >> 16);
    }
}
