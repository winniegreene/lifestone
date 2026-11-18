using System.Globalization;
using System.Text;
using System.Text.Json;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quests;

/// <summary>One quest's timer for one character: when it was last completed, when it is next due, and how the completion was learned.</summary>
/// <param name="Key">The quest's key.</param>
/// <param name="LastCompleted">When the client learned of the last completion (UTC).</param>
/// <param name="NextDue">When the quest may be done again (UTC), or null when it is done once per character.</param>
/// <param name="Completions">How many completions the client has recorded.</param>
/// <param name="How">How the last completion was learned: <see cref="QuestTimers.HowReply"/>, <see cref="QuestTimers.HowItem"/> or <see cref="QuestTimers.HowServerStamp"/>.</param>
public sealed record QuestTimerEntry(string Key, DateTimeOffset LastCompleted, DateTimeOffset? NextDue, int Completions, string How);

/// <summary>What <see cref="QuestTimers.BindTo"/> found when the timers held records and were not this character's.</summary>
/// <param name="Dropped">True: the records were another character's (a character re-created under the same name) and are dropped. False: the file named no character and its records are kept as this character's (<c>TODO(m6-quest-timers-unowned)</c>).</param>
/// <param name="FileGuid">The guid the timers carried before (0: none).</param>
/// <param name="Entries">The records dropped, or the records kept.</param>
public sealed record QuestTimersBind(bool Dropped, uint FileGuid, IReadOnlyList<QuestTimerEntry> Entries);

/// <summary>
/// The quest timers of one character (M6 DoD 7: "the 20-hour timer kept per character"), keyed by quest key: when each quest was last
/// completed and when it is next due (<see cref="Quest.TimerSeconds"/> after the completion; never again for a quest done once).
/// </summary>
/// <remarks>
/// <para><b>What the client can observe of the server's quest stamps.</b> The stamps live in ACE's quest registry (a row per quest with
/// its completions and its last completion time). This server has <c>quest_info_enabled</c> false, so <c>/myquests</c> is off and the
/// retail client is told nothing of the registry; the protocol carries no stamp. What the client sees is the NPC's reply, the item
/// arriving and the experience line (the spec section 4). So a completion is recorded by the runner from the quest's own
/// <see cref="Quest.Done"/>: the done reply read in the chat (<see cref="HowReply"/>) or the done item seen in the pack
/// (<see cref="HowItem"/>), at the time the client saw it. On the loopback server the tools' <c>@lifestone quest has &lt;name&gt;</c>
/// verb (<see cref="PlayerTools.HasQuest"/>) also reads the registry row, changing nothing: whether the stamp is held and its
/// completions, but not its time (<see cref="QuestResult"/>); <see cref="Observe"/> records a held done stamp the timers lack, with the
/// time it was seen as the completion (<see cref="HowServerStamp"/>: an upper bound, so the next due time is never early).
/// Reading the registry's last completion time needs a new mod verb (<c>TODO(m6-quest-stamp-time)</c>; its shape is in
/// docs/session.md, the quest loader).</para>
/// <para><b>Where it is kept.</b> The client does no file I/O (<c>ArchitectureTests</c>): the timers are text written through an
/// <see cref="IAutopilotCheckpointStore"/>, which each host implements over a path (the temporary file and the one-step move). The
/// file is <see cref="FileName"/> under the host's folder: in the game <c>user://quests/&lt;character&gt;.json</c>
/// (<c>user://quests/winnie.json</c>; the file name is the autopilot checkpoint's, <see cref="AutopilotHostOptions.CheckpointFileName"/>).
/// Times are UTC and come from the caller (the session reads no wall clock).</para>
/// <para><b>Whose records they are.</b> The file is named for the character's name, and a character deleted and created again
/// keeps its name (the climber at each gate, M4 D44). So the file also carries the character's guid (<see cref="CharacterGuid"/>,
/// as the autopilot's checkpoint does), and the run binds the timers to the character in the world once it is there
/// (<see cref="BindTo"/>): another character's records are dropped, by name. Long run 5 (2026-11-10) is why: a fresh climber read
/// the last one's finished errand as her own and never ran it.</para>
/// </remarks>
public sealed class QuestTimers
{
    /// <summary>The file's schema: 2 adds <c>character_guid</c>. A schema 1 file is read as naming no character.</summary>
    public const int CurrentSchema = 2;

    private const int SchemaWithoutGuid = 1;

    /// <summary>A completion learned from the quest's done reply.</summary>
    public const string HowReply = "done-reply";

    /// <summary>A completion learned from the quest's done item in the pack.</summary>
    public const string HowItem = "done-item";

    /// <summary>A completion learned from the server's registry (the tools' <c>quest has</c>), timed when it was seen.</summary>
    public const string HowServerStamp = "server-stamp";

    private static readonly string[] Hows = [HowReply, HowItem, HowServerStamp];

    private readonly SortedDictionary<string, QuestTimerEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Empty timers for <paramref name="character"/>.</summary>
    /// <param name="character">The character's name.</param>
    public QuestTimers(string character)
    {
        if (string.IsNullOrWhiteSpace(character))
        {
            throw new ArgumentException("no character name", nameof(character));
        }

        Character = character;
    }

    /// <summary>The character the timers belong to.</summary>
    public string Character { get; }

    /// <summary>The guid of the character the records belong to; 0 until <see cref="BindTo"/> (new timers, or a file written before the guid was kept).</summary>
    public uint CharacterGuid { get; private set; }

    /// <summary>
    /// Binds the timers to the character in the world. Nothing to say (null) when they are already this character's, when
    /// <paramref name="characterGuid"/> is 0 (unknown: nothing is changed), or when they hold no record (the guid is taken silently).
    /// Records under another guid are another character's, re-created under the same name: they are dropped and returned. Records
    /// under no guid are kept as this character's and returned (<c>TODO(m6-quest-timers-unowned)</c>: the file cannot say whose they are).
    /// The caller logs the answer and saves the timers.
    /// </summary>
    /// <param name="characterGuid">The guid of the character in the world.</param>
    public QuestTimersBind? BindTo(uint characterGuid)
    {
        if (characterGuid == 0 || characterGuid == CharacterGuid)
        {
            return null;
        }

        var was = CharacterGuid;
        CharacterGuid = characterGuid;
        if (_entries.Count == 0)
        {
            return null;
        }

        QuestTimerEntry[] entries = [.. _entries.Values];
        if (was == 0)
        {
            return new QuestTimersBind(Dropped: false, was, entries);
        }

        _entries.Clear();
        return new QuestTimersBind(Dropped: true, was, entries);
    }

    /// <summary>Every recorded quest, in key order.</summary>
    public IReadOnlyCollection<QuestTimerEntry> Entries => _entries.Values;

    /// <summary>The timers' file name under the host's folder: <c>quests/&lt;character&gt;.json</c> (<c>quests/winnie.json</c>).</summary>
    /// <param name="character">The character's name.</param>
    public static string FileName(string character) => "quests/" + AutopilotHostOptions.CheckpointFileName(character);

    /// <summary>The entry of <paramref name="key"/>, or null when the quest has never been recorded complete.</summary>
    /// <param name="key">The quest's key.</param>
    public QuestTimerEntry? Get(string key) => _entries.GetValueOrDefault(key);

    /// <summary>
    /// Records a completion of <paramref name="quest"/> at <paramref name="at"/>: the runner calls it when it reads the quest's done reply
    /// or sees its done item. The next due time is <see cref="Quest.TimerSeconds"/> after it, or none for a quest done once.
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="at">When the completion was seen (UTC).</param>
    /// <param name="how">How it was learned (<see cref="HowReply"/>, <see cref="HowItem"/> or <see cref="HowServerStamp"/>).</param>
    /// <returns>The new entry.</returns>
    public QuestTimerEntry Complete(Quest quest, DateTimeOffset at, string how)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (!Hows.Contains(how, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{how}' is not one of {string.Join(", ", Hows)}", nameof(how));
        }

        var utc = at.ToUniversalTime();
        var previous = Get(quest.Key);
        var entry = new QuestTimerEntry(quest.Key, utc, quest.TimerSeconds is { } t ? utc.AddSeconds(t) : null, (previous?.Completions ?? 0) + 1, how);
        _entries[quest.Key] = entry;
        return entry;
    }

    /// <summary>
    /// Takes the tools' <c>quest has</c> answer for the quest's done stamp (<see cref="PlayerTools.HasQuest"/>): a held stamp the timers
    /// lack is recorded as a completion at <paramref name="seenAt"/> (<see cref="HowServerStamp"/>); anything else changes nothing.
    /// </summary>
    /// <param name="quest">The quest; its <see cref="QuestDone.Stamp"/> must be the answer's name.</param>
    /// <param name="has">The answer of a <c>has</c> request.</param>
    /// <param name="seenAt">When the answer came (UTC).</param>
    /// <returns>True when a completion was recorded.</returns>
    public bool Observe(Quest quest, QuestResult has, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(has);
        if (quest.Done.Stamp is not { } stamp || !string.Equals(stamp, has.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"the answer is for '{has.Name}', not quest {quest.Key}'s done stamp ({quest.Done.Stamp ?? "none"})", nameof(has));
        }

        if (!has.Held || Get(quest.Key) is not null)
        {
            return false;
        }

        Complete(quest, seenAt, HowServerStamp);
        return true;
    }

    /// <summary>When <paramref name="quest"/> may next be done: null when it never may (done once and recorded), <see cref="DateTimeOffset.MinValue"/> when it has never been recorded.</summary>
    /// <param name="quest">The quest.</param>
    public DateTimeOffset? NextDue(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        return Get(quest.Key) is { } e ? e.NextDue : DateTimeOffset.MinValue;
    }

    /// <summary>True when <paramref name="quest"/> may be done at <paramref name="now"/>: never recorded, or its timer has run out.</summary>
    /// <param name="quest">The quest.</param>
    /// <param name="now">The time (UTC).</param>
    public bool IsDue(Quest quest, DateTimeOffset now) => NextDue(quest) is { } due && now >= due;

    /// <summary>The quest's timer as one log line: <c>quest key=pathwarden state=done-once last=2026-11-01T01:00:00Z next=never completions=1 how=done-item</c>.</summary>
    /// <param name="quest">The quest.</param>
    /// <param name="now">The time (UTC).</param>
    public string Line(Quest quest, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (Get(quest.Key) is not { } e)
        {
            return $"quest key={quest.Key} character=\"{Character}\" state=never last=none next=now completions=0";
        }

        var state = e.NextDue is null ? "done-once" : now >= e.NextDue ? "due" : "waiting";
        return $"quest key={quest.Key} character=\"{Character}\" state={state} last={Stamp(e.LastCompleted)} next={(e.NextDue is { } n ? Stamp(n) : "never")} completions={e.Completions.ToString(CultureInfo.InvariantCulture)} how={e.How}";
    }

    /// <summary>The timers as their file's text.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", CurrentSchema);
            w.WriteString("character", Character);
            w.WriteNumber("character_guid", CharacterGuid);
            w.WriteStartArray("quests");
            foreach (var e in _entries.Values)
            {
                w.WriteStartObject();
                w.WriteString("key", e.Key);
                w.WriteString("last_completed", Stamp(e.LastCompleted));
                if (e.NextDue is { } n)
                {
                    w.WriteString("next_due", Stamp(n));
                }
                else
                {
                    w.WriteNull("next_due");
                }

                w.WriteNumber("completions", e.Completions);
                w.WriteString("how", e.How);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Reads timers from their file's text; a missing, unknown or malformed field, or another character's file, throws <see cref="InvalidDataException"/> naming it.</summary>
    /// <param name="json">The text.</param>
    /// <param name="character">The character the file must belong to.</param>
    public static QuestTimers Parse(string json, string character)
    {
        ArgumentNullException.ThrowIfNull(json);
        var timers = new QuestTimers(character);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"quest timers of {character}: $ is not JSON: {e.Message}", e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schemaField))
            {
                throw Bad(character, "$.schema", "is missing (a required field)");
            }

            if (!schemaField.TryGetInt32(out var schema) || (schema != CurrentSchema && schema != SchemaWithoutGuid))
            {
                throw Bad(character, "$.schema", $"is not {CurrentSchema} (or {SchemaWithoutGuid}, the file before the guid was kept)");
            }

            if (schema == SchemaWithoutGuid)
            {
                Fields(root, "$", character, "schema", "character", "quests");
            }
            else
            {
                Fields(root, "$", character, "schema", "character", "character_guid", "quests");
                if (!root.GetProperty("character_guid").TryGetUInt32(out var guid))
                {
                    throw Bad(character, "$.character_guid", "is not a guid (a whole number from 0 to 4294967295)");
                }

                timers.CharacterGuid = guid;
            }

            if (!string.Equals(root.GetProperty("character").GetString(), character, StringComparison.Ordinal))
            {
                throw Bad(character, "$.character", $"is '{root.GetProperty("character")}', not this character");
            }

            if (root.GetProperty("quests").ValueKind != JsonValueKind.Array)
            {
                throw Bad(character, "$.quests", "is not an array");
            }

            var i = 0;
            foreach (var q in root.GetProperty("quests").EnumerateArray())
            {
                var path = $"$.quests[{i}]";
                Fields(q, path, character, "key", "last_completed", "next_due", "completions", "how");
                var key = q.GetProperty("key").GetString() ?? throw Bad(character, path + ".key", "is null");
                var last = Time(q.GetProperty("last_completed"), character, path + ".last_completed") ?? throw Bad(character, path + ".last_completed", "is null");
                var next = Time(q.GetProperty("next_due"), character, path + ".next_due");
                if (!q.GetProperty("completions").TryGetInt32(out var completions) || completions < 1)
                {
                    throw Bad(character, path + ".completions", "is not a whole number above 0");
                }

                var how = q.GetProperty("how").GetString();
                if (how is null || !Hows.Contains(how, StringComparer.Ordinal))
                {
                    throw Bad(character, path + ".how", $"is '{how}', not one of {string.Join(", ", Hows)}");
                }

                if (!timers._entries.TryAdd(key, new QuestTimerEntry(key, last, next, completions, how)))
                {
                    throw Bad(character, path + ".key", $"'{key}' appears twice");
                }

                i++;
            }
        }

        return timers;
    }

    /// <summary>Reads the timers of <paramref name="character"/> from <paramref name="store"/>; empty timers when it holds none.</summary>
    /// <param name="store">The host's store over <see cref="FileName"/>.</param>
    /// <param name="character">The character's name.</param>
    public static QuestTimers Load(IAutopilotCheckpointStore store, string character)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Read() is { } text ? Parse(text, character) : new QuestTimers(character);
    }

    /// <summary>Writes the timers through <paramref name="store"/> (atomically: the store's contract).</summary>
    /// <param name="store">The host's store over <see cref="FileName"/>.</param>
    public void Save(IAutopilotCheckpointStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.Write(ToJson());
    }

    private static string Stamp(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Time(JsonElement e, string character, string path)
    {
        if (e.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (e.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(e.GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
        {
            throw Bad(character, path, "is not a UTC time (yyyy-MM-ddTHH:mm:ssZ)");
        }

        return t;
    }

    private static void Fields(JsonElement e, string path, string character, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw Bad(character, path, "is not an object");
        }

        foreach (var name in names)
        {
            if (!e.TryGetProperty(name, out _))
            {
                throw Bad(character, $"{path}.{name}", "is missing (a required field)");
            }
        }

        foreach (var p in e.EnumerateObject())
        {
            if (!names.Contains(p.Name, StringComparer.Ordinal))
            {
                throw Bad(character, $"{path}.{p.Name}", "is not a field of the quest timers (unknown field)");
            }
        }
    }

    private static InvalidDataException Bad(string character, string path, string problem) => new($"quest timers of {character}: {path} {problem}");
}
