using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 D121: the sort of the packs as a frame-stepped machine, for a caller with no script (the start-up audit; the resupply's gear phase sorts in its own script). The layout and the moves are planned once, at the first step
/// (<see cref="PackLayout.Sort"/>); each move is one <see cref="IActionApi.MoveToPack"/> waited for, logged with its outcome; a refused move is counted and the next one goes. Every send is <see cref="ActionSource.Automation"/>.
/// </summary>
/// <param name="rules">The ground's pack rules.</param>
/// <param name="keptAside">True for a guid the keep-aside rule kept.</param>
/// <param name="rotation">True for a guid the self-equip took off.</param>
/// <param name="log">Where the lines go.</param>
/// <param name="category">The log category.</param>
/// <param name="why">What began the sort, for the lines.</param>
public sealed class PackSorter(PackRules rules, Func<uint, bool> keptAside, Func<uint, bool> rotation, ILogSink log, string category, string why)
{
    private List<PackMove>? _moves;
    private int _next;
    private PackMove _current;
    private ActionHandle? _pending;

    /// <summary>The moves the server confirmed.</summary>
    public int Moved { get; private set; }

    /// <summary>The moves refused or timed out (the item stays where it is).</summary>
    public int Refused { get; private set; }

    /// <summary>The items that belong elsewhere but found their pack full.</summary>
    public int Stayed { get; private set; }

    /// <summary>The moves planned.</summary>
    public int Planned => _moves?.Count ?? 0;

    /// <summary>One frame: true while a move is in flight or due, false once every planned move settled.</summary>
    /// <param name="session">The session.</param>
    public bool Step(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_moves is null)
        {
            var layout = PackLayout.Of(session.State, rules, keptAside, rotation);
            layout.Publish();
            log.Info(category, $"packs: layout ({why}): {layout.Why}; {layout.SlotsLine()}");
            var stays = new List<string>();
            _moves = layout.Sort(PackLayout.Items(session.State), rules, keptAside, rotation, stays);
            Stayed = stays.Count;
            foreach (var line in stays)
            {
                log.Info(category, line);
            }

            log.Info(category, string.Create(CultureInfo.InvariantCulture, $"packs: {_moves.Count} item(s) to move ({why}), {Stayed} left for a full pack"));
        }

        if (_pending is { Outcome: null })
        {
            return true;
        }

        if (_pending is { Outcome: { } outcome })
        {
            if (outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                Moved++;
                log.Info(category, _current + ": done");
            }
            else
            {
                Refused++;
                log.Warn(category, _current + ": " + outcome.Kind + (outcome.Detail is null ? string.Empty : " " + outcome.Detail) + " (counted; it stays where it is)");
            }

            _pending = null;
        }

        while (_next < _moves.Count)
        {
            _current = _moves[_next++];
            if (!session.State.Self.Inventory.Contains(_current.Guid))
            {
                continue;
            }

            _pending = session.Actions.MoveToPack(_current.Guid, _current.Container, 0, ActionSource.Automation);
            return true;
        }

        return false;
    }

    /// <summary>The sort's finding for a summary: <c>&lt;moved&gt;/&lt;planned&gt;</c>, with the refused and the stayed when there are any.</summary>
    public string Finding() => string.Create(CultureInfo.InvariantCulture, $"{Moved}/{Planned}{(Refused > 0 ? $"+refused{Refused}" : string.Empty)}{(Stayed > 0 ? $"+full{Stayed}" : string.Empty)}");
}
