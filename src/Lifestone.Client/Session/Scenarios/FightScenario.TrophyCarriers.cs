using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): while a wanted item with known carriers (<see cref="FightOptions.WantCarriers"/>) is not held yet, the tour goes to its carriers first
/// (<see cref="IGroundTour.Prefer"/>): a living one it knows, else a standing slot of one. The fight still fights whatever comes in reach on the way; once the item is held the tour goes back to its own order.
/// </summary>
public sealed partial class FightScenario
{
    private readonly List<uint> _carrierWcids = [];
    private readonly List<uint> _carrierTold = [];
    private readonly List<uint> _carrierScratch = [];
    private IGroundTour? _carrierTour;

    /// <summary>Before the tour is asked for its next waypoint (nobody in reach): the carriers of the wanted items not held yet; the tour is told, and one line logged, only when they change.</summary>
    private void CarrierPreference(IGameSessionView session)
    {
        if (_walk?.Tour is not { } tour || _options.WantCarriers.Count == 0)
        {
            return;
        }

        _carrierWcids.Clear();
        foreach (var carrier in _options.WantCarriers)
        {
            if (_options.Want.Contains(carrier.Item) && !_wantRefused.Contains(carrier.Item) && !_carrierWcids.Contains(carrier.Creature) && GearInventory.CountOf(session.State, _carrierScratch, carrier.Item) == 0)
            {
                _carrierWcids.Add(carrier.Creature);
            }
        }

        if (ReferenceEquals(tour, _carrierTour) && _carrierWcids.SequenceEqual(_carrierTold))
        {
            return;
        }

        _carrierTour = tour;
        _carrierTold.Clear();
        _carrierTold.AddRange(_carrierWcids);
        var what = string.Join(", ", _options.WantCarriers.Where(c => _carrierWcids.Contains(c.Creature)).Select(c => c.ItemName).Distinct(StringComparer.Ordinal));
        tour.Prefer(_carrierWcids, what);
        _log.Info(LogCategory, _carrierWcids.Count == 0
            ? "tour: no wanted item is left to carry: the tour goes by its own order (m6-audit-tusk-carriers)"
            : string.Create(CultureInfo.InvariantCulture, $"tour: the carriers of {what} first: {string.Join(",", _carrierWcids)} (the fight wants the item and does not hold it; m6-audit-tusk-carriers)"));
    }
}
