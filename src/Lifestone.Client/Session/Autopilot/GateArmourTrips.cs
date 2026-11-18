using System.Collections.Concurrent;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 gate armour (live 2026-11-17 04:55:43, ladder52: every gate read shut for want of Armor Self and Bludgeon Protection, lines her book lacked): the sets of armour lines a way out sent her on a town trip to buy in this
/// process (<see cref="AutopilotOptions.GateArmourTrips"/>). A set is tried once: a trip that bought nothing (the purse, the room, a scroll no scrivener she reaches sells) is named by its own every-spell lines, and the next
/// way out holds her by the gate as before, never on a loop of trips.
/// </summary>
public sealed class GateArmourTrips
{
    private readonly ConcurrentDictionary<string, byte> _tried = new(StringComparer.Ordinal);

    /// <summary>The key of a set of lines (sorted, joined).</summary>
    /// <param name="lines">The lines.</param>
    public static string KeyOf(IEnumerable<string> lines) => string.Join("+", (lines ?? throw new ArgumentNullException(nameof(lines))).Order(StringComparer.Ordinal));

    /// <summary>Notes a trip for <paramref name="lines"/>; true the first time.</summary>
    /// <param name="lines">The lines the trip is to buy.</param>
    public bool TryNote(IEnumerable<string> lines) => _tried.TryAdd(KeyOf(lines), 0);

    /// <summary>True when a trip for <paramref name="lines"/> was taken this process.</summary>
    /// <param name="lines">The lines.</param>
    public bool Tried(IEnumerable<string> lines) => _tried.ContainsKey(KeyOf(lines));
}
