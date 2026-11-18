using System.Collections.Concurrent;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 (live 2026-11-14, a move-on loop): the rungs a way out held by their approach in this session (<c>approach-held</c>, <c>approach-costly</c>, <c>approach-abandoned</c>), so the move-on ahead of a band
/// (<see cref="RungOwner.Ahead"/>, read through <see cref="RungAsk.AheadHeld"/>) does not take one again and loop between the host's restart and the way out. It lives in <see cref="AutopilotOptions.ApproachHolds"/>: the host's restarts
/// and the scenario's checks share it; a new process (or a test's own options) reads the approach afresh at its first way out.
/// </summary>
public sealed class ApproachHolds
{
    private readonly ConcurrentDictionary<string, string> _held = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _abandoned = new(StringComparer.Ordinal);

    /// <summary>M6 sprint (the owner, 2026-11-18): the vitae burn after a death and its owed corpse (<see cref="VitaeBurn"/>), one a process like the holds, so the ladder's restarts carry it.</summary>
    public VitaeBurn Vitae { get; } = new();

    /// <summary>
    /// M6 14 (live 2026-11-14 19:13, ladder14b: the run leg gave the Armory's crossing up, the way out held it and named the Quarters, the host's restart began a new run whose journey facts had forgotten the crossing, its way out priced
    /// the Armory open again and the host stopped on <c>failed:move-on-loop</c>): a crossing a run leg gave up (<see cref="Scenarios.Journey.JourneyFacts.AbandonApproach"/>) by its ground, for the rest of the process: a measured
    /// crossing outweighs a price, so no way out's price releases it.
    /// </summary>
    /// <param name="groundKey">The ground whose crossing was given up.</param>
    /// <param name="why">The run leg's measure.</param>
    public void NoteAbandoned(string groundKey, string why) => _abandoned.TryAdd(groundKey, why);

    /// <summary>The measure a crossing to <paramref name="groundKey"/> was given up by in this process, or null.</summary>
    /// <param name="groundKey">The ground.</param>
    public string? AbandonedOf(string groundKey) => _abandoned.TryGetValue(groundKey, out var why) ? why : null;

    /// <summary>
    /// True when <paramref name="key"/> was held by its approach this session (a rung's key, or an alternate's ground key), or is a ground whose crossing was given up this process (<see cref="NoteAbandoned"/>: a run leg's measure, or
    /// <c>--autopilot-hold-grounds</c> at launch). M6 14 launch holds (live 2026-11-17, ladder47 and ladder51: the grounds held at launch were read by the way out only, so the start picked the Holding and the way out moved her on):
    /// every check reads the same holds, so a held ground is never picked to be refused at the way out.
    /// </summary>
    /// <param name="key">The ladder rung's key, or a ground's key.</param>
    public bool IsHeld(string key) => _held.ContainsKey(key) || _abandoned.ContainsKey(key);

    /// <summary>Notes a rung the way out held by its approach (M6 14 armory: or an alternate's ground key, <see cref="RungChoice.HeldAlternates"/>); true the first time (the caller logs it once).</summary>
    /// <param name="rungKey">The ladder rung's key, or an alternate's ground key.</param>
    /// <param name="reason">The way out's reason (<c>approach-costly:&lt;rung&gt;</c> and the like).</param>
    public bool Note(string rungKey, string reason) => _held.TryAdd(rungKey, reason);

    /// <summary>
    /// M6 14 armory: forgets the hold of a rung or an alternate's ground that a way out judged open again (<see cref="RungChoice.ApproachOpened"/>), so the checks that read no approach take it again; true when one was held.
    /// </summary>
    /// <param name="key">The rung's key or the alternate's ground key.</param>
    public bool Release(string key) => _held.TryRemove(key, out _);

    /// <summary>The rung a way-out reason names when it is an approach hold (<c>approach-held:</c>, <c>approach-costly:</c>, <c>approach-abandoned:</c>), else null.</summary>
    /// <param name="reason">The rung choice's reason.</param>
    public static string? HeldRungOf(string? reason)
    {
        if (reason is null)
        {
            return null;
        }

        foreach (var prefix in (string[])["approach-held:", "approach-costly:", "approach-abandoned:"])
        {
            if (reason.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = reason[prefix.Length..];
                var end = rest.IndexOfAny([' ', ';', ',']);
                return end < 0 ? rest : rest[..end];
            }
        }

        return null;
    }

    /// <summary>Forgets every hold (the tests).</summary>
    internal void Clear()
    {
        _held.Clear();
        _abandoned.Clear();
    }
}
