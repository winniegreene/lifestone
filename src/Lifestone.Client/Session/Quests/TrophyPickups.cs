using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quests;

/// <summary>
/// M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): when each trophy item whose quest's timer starts at the pickup (<see cref="QuestTimerFrom.Pickup"/>) was first seen in the pack this session.
/// <para>ACE stamps a tusk's own Quest string as it is picked up from a corpse (Player_Inventory.cs:1249-1262 and :1093-1094, EmoteManager.cs:2021-2041 @95ef426d), so the next tusk of that kind may be picked up 20 h after
/// the pickup, not after the turn-in. The client counted from the turn-in: live, run 4 of the sprint (cache/m6/sprint/run4/step3-levelup.log) took its Female Tusker Tusk at 03:57:11 UTC and turned it in at 04:35:54 UTC, so
/// the next one waited 38 min 43 s longer than the server asked.</para>
/// <para>The moment noted is the first time the item is seen held (<see cref="Watch"/>), never before the real pickup, so a timer counted from it is never early; with no moment noted (a new session, the tusk already held) the
/// turn-in's moment stands, as before. Game thread only.</para>
/// </summary>
public sealed class TrophyPickups
{
    private static readonly ConditionalWeakTable<GameState, TrophyPickups> PerSession = new();
    private readonly Dictionary<uint, DateTimeOffset> _seen = [];

    /// <summary>The memory of the session behind <paramref name="state"/>.</summary>
    /// <param name="state">The session's read model.</param>
    public static TrophyPickups For(GameState state) => PerSession.GetOrCreateValue(state ?? throw new ArgumentNullException(nameof(state)));

    /// <summary>Takes the count of <paramref name="item"/> held now: the first time it is held notes <paramref name="now"/>; none held forgets it (given away, so the next one is a new pickup).</summary>
    /// <param name="item">The trophy's weenie class.</param>
    /// <param name="held">The count held in the pack now.</param>
    /// <param name="now">The time (UTC).</param>
    /// <returns>True when the item is first seen held now (the caller logs it).</returns>
    public bool Watch(uint item, int held, DateTimeOffset now)
    {
        if (held <= 0)
        {
            _seen.Remove(item);
            return false;
        }

        return _seen.TryAdd(item, now.ToUniversalTime());
    }

    /// <summary>When <paramref name="item"/> was first seen held this session, or null.</summary>
    /// <param name="item">The trophy's weenie class.</param>
    public DateTimeOffset? PickedAt(uint item) => _seen.TryGetValue(item, out var at) ? at : null;

    /// <summary>Forgets <paramref name="item"/> (its turn-in gave it away).</summary>
    /// <param name="item">The trophy's weenie class.</param>
    public void Forget(uint item) => _seen.Remove(item);

    /// <summary>The hunt-until item whose pickup starts <paramref name="quest"/>'s timer, or null when its timer starts at the turn-in.</summary>
    /// <param name="quest">The quest.</param>
    public static QuestThing? PickupItem(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        return quest.TimerFrom == QuestTimerFrom.Pickup ? quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.HuntUntil && s.Item?.Wcid is not null)?.Item : null;
    }

    /// <summary>
    /// The moment <paramref name="quest"/>'s completion is recorded at, its turn-in being <paramref name="now"/>: the item's noted pickup when the quest's timer starts there and a moment is noted no later than now, else now.
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="now">The turn-in's moment (UTC).</param>
    /// <param name="note">Where the timer starts, for the station's line.</param>
    public DateTimeOffset StartOf(Quest quest, DateTimeOffset now, out string note)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var c = CultureInfo.InvariantCulture;
        if (PickupItem(quest) is not { Wcid: { } item } thing)
        {
            note = "the turn-in";
            return now;
        }

        if (PickedAt(item) is { } at && at <= now)
        {
            note = string.Create(c, $"the pickup: {thing.Name} ({item}) first seen in the pack at {at:yyyy-MM-ddTHH:mm:ssZ}, {(now - at).TotalMinutes:0.0} min before the turn-in (timer_from pickup, m6-audit-tusk-pickup)");
            return at;
        }

        note = string.Create(c, $"the turn-in: the pickup of {thing.Name} ({item}) was not seen this session (timer_from pickup; the turn-in's moment is never early)");
        return now;
    }
}
