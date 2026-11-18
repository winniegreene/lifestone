using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 guard task (the owner, 2026-11-13): which quests the level up job runs, <c>--autopilot-ladder-quests</c>. <see cref="On"/> (the default) is the ladder as it was: every ladder errand of the quest files' ladder bands and every
/// trophy turn-in, and no kill task (a kill task has never been run unprompted). <see cref="Off"/> is the sprint (M6 item 14 S3): none. A selection (<c>tusks,tusker-guard-kill-task</c>) runs only the quests it names, by the quest catalog's
/// keys, and the rest stay off: a ladder errand or a trophy turn-in not named is skipped by name, and a kill task runs only when named. The ladder's first rungs that are quests (the Training Academy and the Pathwarden chest, her first gear)
/// are no selection's: they stay in every setting.
/// </summary>
public sealed class LadderQuestSelection
{
    /// <summary>The word of the default.</summary>
    public const string OnWord = "on";

    /// <summary>The word of the sprint.</summary>
    public const string OffWord = "off";

    /// <summary>The group word for the trophy turn-ins at Brighteyes: the quest catalog's keys that begin <see cref="TuskPrefix"/> (<c>tusk-female</c>, <c>tusk-guard</c>, ...).</summary>
    public const string TusksGroup = "tusks";

    /// <summary>The key prefix of the catalog's tusk turn-ins (each file <c>tusk-&lt;kind&gt;.json</c>).</summary>
    public const string TuskPrefix = "tusk-";

    private readonly HashSet<string>? _keys;

    private LadderQuestSelection(string text, IReadOnlyList<string>? keys)
    {
        Text = text;
        Keys = keys;
        _keys = keys is null ? null : new HashSet<string>(keys, StringComparer.Ordinal);
    }

    /// <summary>The default: the ladder as it was (every ladder errand and trophy turn-in; no kill task).</summary>
    public static LadderQuestSelection On { get; } = new(OnWord, null);

    /// <summary>The sprint: no quest of the ladder's.</summary>
    public static LadderQuestSelection Off { get; } = new(OffWord, []);

    /// <summary>The words as given (<c>on</c>, <c>off</c>, or the list).</summary>
    public string Text { get; }

    /// <summary>The catalog keys the selection names, groups expanded, in the order given; null for <see cref="On"/>, empty for <see cref="Off"/>.</summary>
    public IReadOnlyList<string>? Keys { get; }

    /// <summary>True for the default.</summary>
    public bool IsOn => _keys is null;

    /// <summary>True for the sprint.</summary>
    public bool IsOff => _keys is { Count: 0 };

    /// <summary>True when a ladder errand or a trophy turn-in of <paramref name="key"/> runs: always under <see cref="On"/>, never under <see cref="Off"/>, and under a selection when it names the key.</summary>
    /// <param name="key">The quest's key.</param>
    public bool Allows(string key) => _keys is null || _keys.Contains(key);

    /// <summary>True when the selection names <paramref name="key"/> itself: a kill task runs only then (the default runs none, as before).</summary>
    /// <param name="key">The quest's key.</param>
    public bool Names(string key) => _keys is not null && _keys.Contains(key);

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <summary>The catalog keys a group word stands for (<see cref="TusksGroup"/>: every key that begins <see cref="TuskPrefix"/>), or null when the word is no group.</summary>
    /// <param name="word">The word.</param>
    /// <param name="quests">The quest catalog.</param>
    public static IReadOnlyList<string>? GroupKeys(string word, IReadOnlyList<Quest> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        return string.Equals(word, TusksGroup, StringComparison.Ordinal) ? [.. quests.Select(q => q.Key).Where(k => k.StartsWith(TuskPrefix, StringComparison.Ordinal))] : null;
    }

    /// <summary>
    /// Reads <paramref name="value"/>: <c>on</c>, <c>off</c>, or a comma-separated list of the quest catalog's keys and group words (<see cref="TusksGroup"/>), each named once. Null with <paramref name="error"/> naming what is wrong: an empty
    /// word, <c>on</c> or <c>off</c> inside a list, a word twice, or a word that is no key of the catalog and no group (the error lists the groups and the keys).
    /// </summary>
    /// <param name="value">The option's value.</param>
    /// <param name="quests">The quest catalog (<see cref="QuestCatalog.All"/>).</param>
    /// <param name="error">Why the value was refused, or null.</param>
    public static LadderQuestSelection? Parse(string value, IReadOnlyList<Quest> quests, out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(quests);
        error = null;
        var text = value.Trim();
        if (string.Equals(text, OnWord, StringComparison.Ordinal))
        {
            return On;
        }

        if (string.Equals(text, OffWord, StringComparison.Ordinal))
        {
            return Off;
        }

        var catalog = quests.Select(q => q.Key).ToHashSet(StringComparer.Ordinal);
        var keys = new List<string>();
        var words = new List<string>();
        foreach (var raw in text.Split(','))
        {
            var word = raw.Trim();
            if (word.Length == 0 || word is OnWord or OffWord)
            {
                error = $"'{value}' is not on, off or a list of the quest catalog's keys ({(word.Length == 0 ? "an empty word" : $"'{word}' stands alone")})";
                return null;
            }

            if (words.Contains(word, StringComparer.Ordinal))
            {
                error = $"'{value}' names '{word}' twice";
                return null;
            }

            words.Add(word);
            var expanded = GroupKeys(word, quests) ?? (catalog.Contains(word) ? [word] : null);
            if (expanded is not { Count: > 0 })
            {
                error = $"'{word}' is not on, off, a group ({TusksGroup}) or a key of the quest catalog ({string.Join(", ", catalog.Order(StringComparer.Ordinal))})";
                return null;
            }

            keys.AddRange(expanded.Where(k => !keys.Contains(k, StringComparer.Ordinal)));
        }

        return new LadderQuestSelection(string.Join(",", words), keys);
    }
}
