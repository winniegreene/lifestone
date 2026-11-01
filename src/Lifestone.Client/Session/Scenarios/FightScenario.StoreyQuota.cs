using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7o (the item-end gate's <c>storeys_visited 1 under 2</c>, D44 c2b): with <c>--fight-storeys-min</c> 2 or more (the mage journey's den fight tours two storeys, <see cref="Journey.JourneyPlans.MageDenFightArguments"/>), the kill quota
/// alone does not win the fight while fewer storeys than the minimum are visited and the tour can reach another. D88's self-defence (F4) drops the tour's walk for every attacker, so three attackers on the arrival's storey had filled the quota
/// there. The fight goes on: it still fights back every attacker, the tour's walk is taken up after each kill (the pick prefers the next storey's targets, <see cref="GroundTour.WantsToLeave"/>), and it is won at the first kill after the
/// storeys are met; <c>--fight-seconds</c> bounds it as before, by name. With no other storey reachable the fight ends as before, with one named line. An open-ended fight (the autopilot's, no kill quota) and a fight with a minimum of 1
/// are not touched.
/// </summary>
public sealed partial class FightScenario
{
    private int _storeyHoldKills = -1; // the kills when the win was last held for the storeys (-1: never held)
    private int _storeyHoldLogged = -1; // the kill count the hold last logged
    private bool _storeysMetLogged;
    private bool _storeysUnreachableLogged;

    /// <summary>The line a fight that cannot reach another storey logs once when it ends with fewer storeys than the minimum.</summary>
    public const string StoreysUnmetLine = "storeys unmet: no other storey";

    /// <summary>The line a fight logs when its kill quota is met but its storeys are not (once per kill count).</summary>
    public const string StoreysHoldLine = "the fight is not won yet: the storeys are not met";

    /// <summary>
    /// D44 c2b: true while the kill quota's win waits for the storeys: fewer than <see cref="FightOptions.StoreysMin"/> storeys visited by the BODY (<c>storeys_visited=</c>, the field the gate judges, never the tour's count, which a kill made from range on another storey raises) and the tour reaches another storey; or the storeys met
    /// by the walk with no kill made since (the win comes with the first kill there). Each decision is logged.
    /// </summary>
    private bool StoreysHoldTheWin(IGameSessionView session, double clock)
    {
        if (_options.StoreysMin < 2 || _options.ArenaHere || _walk?.Tour is not GroundTour tour)
        {
            return false;
        }

        var visited = _storeysVisited; // the body's count, the one the gate judges (storeys_visited=); a kill on another storey made from range does not count (the tour's own count does)
        if (visited >= _options.StoreysMin)
        {
            if (_storeyHoldKills < 0 || _tally.Kills > _storeyHoldKills)
            {
                if (_storeyHoldKills >= 0)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"storeys met: {visited} of {_options.StoreysMin} storeys visited and kill {_tally.Kills} made after them: the fight is won"));
                }

                return false;
            }

            if (!_storeysMetLogged)
            {
                _storeysMetLogged = true;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"storeys met by the walk: the body visited {visited} of {_options.StoreysMin} storeys (tour {tour.StoreysVisited}); the fight is won at the next kill"));
            }

            return true;
        }

        var here = session.State.Self.Position?.CellId ?? 0;
        var next = tour.OtherStorey(here, clock);
        if (next < 0)
        {
            if (!_storeysUnreachableLogged)
            {
                _storeysUnreachableLogged = true;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{StoreysUnmetLine}: kills={_tally.Kills} of {_options.Kills} with {visited} of {_options.StoreysMin} storeys visited, and the tour reaches no other storey with a standing hunted slot: the fight ends as before (storeys_visited stays under the minimum)"));
            }

            return false;
        }

        _storeyHoldKills = _tally.Kills;
        if (_storeyHoldLogged != _tally.Kills)
        {
            _storeyHoldLogged = _tally.Kills;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{StoreysHoldLine}: kills={_tally.Kills} of {_options.Kills} with {visited} of {_options.StoreysMin} storeys visited by the body (tour {tour.StoreysVisited}); the tour's walk goes on to storey '{_ground.Storeys[next].Name}' and attackers are still fought back (D88), within {_options.FailSeconds:0.#} s"));
        }

        return true;
    }
}
