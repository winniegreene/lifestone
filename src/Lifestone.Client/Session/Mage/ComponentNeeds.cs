namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AT (live, 2026-11-06: "level III needs Blue Taper 1643, which the pack does not hold", four of nine lines lapsed for 70 minutes, <c>pass: casts=0</c> every minute, and nothing sent her home): the components the buff passes found
/// the pack short of, by weenie class. A pass that skips a level for want of a component notes it here (<see cref="Note"/>: the line, the count one cast burns); the logistics counters raise that stock line's trip and keep lines to
/// <see cref="PassesKept"/> passes of the lines that burn it (<see cref="WantOf"/>), so the next check names the trip (<c>stock:&lt;component&gt;</c>) once and the trip buys it. One per session (<see cref="Shared"/>), game thread only; the tests give
/// each their own. A need is never forgotten: a ground's profile keep stays the floor and this is the higher line the book needs.
/// </summary>
public sealed class ComponentNeeds
{
    /// <summary>How many passes of the lines in the book a trip's keep pays for (the brief's "at least two").</summary>
    public const int PassesKept = 2;

    private readonly Dictionary<uint, Need> _needs = [];

    /// <summary>The session's needs.</summary>
    public static ComponentNeeds Shared { get; } = new();

    /// <summary>A number that rises each time a note changes a line's want (for the counters' cache).</summary>
    public int Version { get; private set; }

    /// <summary>The components noted.</summary>
    public int Count => _needs.Count;

    /// <summary>Notes that <paramref name="line"/> was skipped for want of <paramref name="name"/> (<paramref name="wcid"/>), one cast burning <paramref name="perCast"/>; true when the want rose (the caller logs it, once).</summary>
    /// <param name="wcid">The component's weenie class id.</param>
    /// <param name="name">The component's name.</param>
    /// <param name="line">The buff line's name.</param>
    /// <param name="perCast">The count one cast of the skipped level burns.</param>
    public bool Note(uint wcid, string name, string line, int perCast)
    {
        ArgumentException.ThrowIfNullOrEmpty(line);
        if (perCast <= 0)
        {
            return false;
        }

        if (!_needs.TryGetValue(wcid, out var need))
        {
            _needs[wcid] = need = new Need(name, []);
        }

        if (need.Lines.TryGetValue(line, out var known) && known >= perCast)
        {
            return false;
        }

        need.Lines[line] = perCast;
        Version++;
        return true;
    }

    /// <summary>The count of <paramref name="wcid"/> that <see cref="PassesKept"/> passes of the noted lines burn, or 0 when it was never noted.</summary>
    /// <param name="wcid">The component's weenie class id.</param>
    public int WantOf(uint wcid)
    {
        if (!_needs.TryGetValue(wcid, out var need))
        {
            return 0;
        }

        var perPass = 0;
        foreach (var n in need.Lines.Values)
        {
            perPass += n;
        }

        return PassesKept * perPass;
    }

    /// <summary>The weenie class ids noted.</summary>
    public IEnumerable<uint> Wcids => _needs.Keys;

    /// <summary>The name of a noted component, or empty.</summary>
    /// <param name="wcid">The component's weenie class id.</param>
    public string NameOf(uint wcid) => _needs.TryGetValue(wcid, out var need) ? need.Name : string.Empty;

    private sealed record Need(string Name, Dictionary<string, int> Lines);
}
