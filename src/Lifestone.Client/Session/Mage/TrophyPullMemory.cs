using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AX: what this session knows of a trophy pull (<see cref="Grounds.PullStand"/>): the tries spent and a named failure. It lives on the session's state, so the restart a rung change makes keeps it and a failed pull cannot call the job again and again; a new process starts clean and asks once more.
/// </summary>
public sealed class TrophyPullMemory
{
    private static readonly ConditionalWeakTable<GameState, TrophyPullMemory> PerSession = new();
    private readonly Dictionary<string, int> _tries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);

    /// <summary>The memory of the session behind <paramref name="state"/>.</summary>
    /// <param name="state">The session's read model.</param>
    public static TrophyPullMemory For(GameState state) => PerSession.GetOrCreateValue(state ?? throw new ArgumentNullException(nameof(state)));

    /// <summary>The tries spent on a quest's pull: a shot sent counts, whatever came of it (a death with it).</summary>
    /// <param name="quest">The quest's key.</param>
    public int TriesOf(string quest) => _tries.GetValueOrDefault(quest);

    /// <summary>Counts a try.</summary>
    /// <param name="quest">The quest's key.</param>
    public void NoteTry(string quest) => _tries[quest] = TriesOf(quest) + 1;

    /// <summary>The named failure of a quest's pull, or null.</summary>
    /// <param name="quest">The quest's key.</param>
    public string? FailureOf(string quest) => _failed.GetValueOrDefault(quest);

    /// <summary>Records a named failure: the job is not called again this session.</summary>
    /// <param name="quest">The quest's key.</param>
    /// <param name="why">The failure's name.</param>
    public void Fail(string quest, string why) => _failed[quest] = why;

    /// <summary>True when the job may be called: no failure named and <paramref name="tries"/> not spent.</summary>
    /// <param name="quest">The quest's key.</param>
    /// <param name="tries">The profile's limit.</param>
    public bool MayTry(string quest, int tries) => FailureOf(quest) is null && TriesOf(quest) < tries;
}
