using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// Where the autopilot's checkpoint lives (M5 item 4 a2): the hosts implement it over a path (the client does no file I/O: <c>ArchitectureTests</c>); <see cref="MemoryCheckpointStore"/> is the tests'. The autopilot calls it from the game thread only.
/// A file implementation writes <see cref="Write"/> to a temporary file beside the checkpoint and moves it over the checkpoint in one step (<c>File.Move(temp, path, overwrite: true)</c>), and <see cref="SetAside"/> renames the file
/// (<c>&lt;file&gt;.aside</c>, then <c>.aside2</c> and so on) and never deletes it.
/// </summary>
public interface IAutopilotCheckpointStore
{
    /// <summary>What the store is, for the log (a file name, or "memory").</summary>
    string Describe { get; }

    /// <summary>The checkpoint's text, or null when there is none. Throws <see cref="IOException"/> when it cannot be read.</summary>
    string? Read();

    /// <summary>Replaces the checkpoint with <paramref name="json"/> atomically: a reader sees the old one or the new one, never half of either. Throws <see cref="IOException"/> when it cannot.</summary>
    /// <param name="json">The checkpoint's text.</param>
    void Write(string json);

    /// <summary>Moves the checkpoint aside (never deletes it) so the next write starts a new one; returns where it went.</summary>
    string SetAside();
}

/// <summary>A checkpoint held in memory (the tests'; a host may use it to run without a file).</summary>
public sealed class MemoryCheckpointStore : IAutopilotCheckpointStore
{
    /// <summary>The current text, or null.</summary>
    public string? Json { get; set; }

    /// <summary>The texts moved aside, oldest first.</summary>
    public List<string> Aside { get; } = [];

    /// <summary>The writes made.</summary>
    public int Writes { get; private set; }

    /// <summary>True: the next writes throw (a disk that cannot be written).</summary>
    public bool FailWrites { get; set; }

    /// <inheritdoc />
    public string Describe => "memory";

    /// <inheritdoc />
    public string? Read() => Json;

    /// <inheritdoc />
    public void Write(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (FailWrites)
        {
            throw new IOException("the memory store was told to fail its writes");
        }

        Json = json;
        Writes++;
    }

    /// <inheritdoc />
    public string SetAside()
    {
        if (Json is { } text)
        {
            Aside.Add(text);
        }

        Json = null;
        return "memory aside #" + Aside.Count.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>The counts the checkpoint holds (the tally's fields, by name; a field a newer build adds is ignored by an older one and a missing one reads 0).</summary>
public sealed class CheckpointTally
{
    /// <summary>The kills.</summary>
    public int Kills { get; init; }

    /// <summary>The experience earned.</summary>
    public long Xp { get; init; }

    /// <summary>The deaths.</summary>
    public int Deaths { get; init; }

    /// <summary>The trips.</summary>
    public int Trips { get; init; }

    /// <summary>The items sold.</summary>
    public int Sold { get; init; }

    /// <summary>The items bought.</summary>
    public int Bought { get; init; }

    /// <summary>The experience spent.</summary>
    public long SpentXp { get; init; }

    /// <summary>The longest mana wait, in seconds.</summary>
    public double ManaWaitMaxSeconds { get; init; }

    /// <summary>The fizzles.</summary>
    public int Fizzles { get; init; }

    /// <summary>The mana tries.</summary>
    public int ManaTries { get; init; }

    /// <summary>The mana casts landed.</summary>
    public int ManaLanded { get; init; }

    /// <summary>The most storeys one fight visited.</summary>
    public int StoreysVisited { get; init; }

    /// <summary>The basement entries.</summary>
    public int BasementEntries { get; init; }

    /// <summary>The server mod's grants.</summary>
    public int Granted { get; init; }

    /// <summary>The server mod's places.</summary>
    public int Places { get; init; }

    /// <summary>The person's interruptions.</summary>
    public int Interrupted { get; init; }

    /// <summary>The hand-backs.</summary>
    public int Resumed { get; init; }

    /// <summary>The reconnects.</summary>
    public int Reconnects { get; init; }

    /// <summary>The seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>The checkpoint's copy of <paramref name="t"/>.</summary>
    /// <param name="t">A tally.</param>
    public static CheckpointTally From(AutopilotTally t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new CheckpointTally
        {
            Kills = t.Kills,
            Xp = t.Xp,
            Deaths = t.Deaths,
            Trips = t.Trips,
            Sold = t.Sold,
            Bought = t.Bought,
            SpentXp = t.SpentXp,
            ManaWaitMaxSeconds = t.ManaWaitMaxSeconds,
            Fizzles = t.Fizzles,
            ManaTries = t.ManaTries,
            ManaLanded = t.ManaLanded,
            StoreysVisited = t.StoreysVisited,
            BasementEntries = t.BasementEntries,
            Granted = t.Granted,
            Places = t.Places,
            Interrupted = t.Interrupted,
            Resumed = t.Resumed,
            Reconnects = t.Reconnects,
            Seconds = t.Seconds,
        };
    }

    /// <summary>The tally these counts make.</summary>
    public AutopilotTally ToTally() => new()
    {
        Kills = Kills,
        Xp = Xp,
        Deaths = Deaths,
        Trips = Trips,
        Sold = Sold,
        Bought = Bought,
        SpentXp = SpentXp,
        ManaWaitMaxSeconds = ManaWaitMaxSeconds,
        Fizzles = Fizzles,
        ManaTries = ManaTries,
        ManaLanded = ManaLanded,
        StoreysVisited = StoreysVisited,
        BasementEntries = BasementEntries,
        Granted = Granted,
        Places = Places,
        Interrupted = Interrupted,
        Resumed = Resumed,
        Reconnects = Reconnects,
        Seconds = Seconds,
    };
}

/// <summary>
/// The autopilot's checkpoint (M5 item 4 a2, D57; automation-vision section 1.4 K): JSON written on every state change and on exit, read at the next start. It holds the counts and the stop rule's budget, never the position:
/// at a start the totals and the budget resume and the state is classified from the world (D57), so <see cref="State"/> and <see cref="LastDecision"/> are for the person reading the file and the log, not for the loop. A file that is torn,
/// unreadable, of another character (by name, or by guid: M5 4 F1) or another run (by tag) or of a newer schema is named, moved aside and the run starts fresh (<see cref="Load"/>); a checkpoint of a run that ended by its stop rule or a request (<see cref="Completed"/>) is a finished run, and a new run starts fresh over it.
/// </summary>
public sealed class AutopilotCheckpoint
{
    /// <summary>The schema this build writes and reads (a file of a higher one is set aside).</summary>
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>The schema version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>The character the run plays (a checkpoint of another one is foreign).</summary>
    public string Character { get; init; } = string.Empty;

    /// <summary>
    /// The character's guid when written (M5 4 F1, 4-R1): a character re-created under the same name has another guid, and its run must not resume the old one's totals. 0: unknown (a file of an older build), which matches any guid.
    /// </summary>
    public uint CharacterGuid { get; init; }

    /// <summary>The run's tag (<see cref="AutopilotOptions.RunTag"/>; M5 4 F1, 4-R1): a file written under another tag is another run's. Empty: none (matches only a run with no tag).</summary>
    public string RunTag { get; init; } = string.Empty;

    /// <summary>The ground's key.</summary>
    public string Ground { get; init; } = string.Empty;

    /// <summary>The autopilot's state when written (<see cref="AutopilotScenario.StateName"/>): for the reader, never for the loop.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The cycle in progress (from 1).</summary>
    public int Cycle { get; init; } = 1;

    /// <summary>The cycles ended.</summary>
    public int Cycles { get; init; }

    /// <summary>The totals of the ended cycles.</summary>
    public CheckpointTally Totals { get; init; } = new();

    /// <summary>The counts of the cycle in progress when the file was written (the fight of the state in progress, not yet counted, is not in it).</summary>
    public CheckpointTally CycleTally { get; init; } = new();

    /// <summary>The stop rule's budget used: the minutes of the session clock the run has run, over all its starts.</summary>
    public double MinutesUsed { get; init; }

    /// <summary>The duration stop rule when written (<c>--autopilot-minutes</c>; 0: none).</summary>
    public double MinutesTarget { get; init; }

    /// <summary>The level stop rule when written (<c>--autopilot-level</c>; 0: none).</summary>
    public int LevelTarget { get; init; }

    /// <summary>The last decision: the arrow's reason as logged.</summary>
    public string LastDecision { get; init; } = string.Empty;

    /// <summary>The session clock when the last decision was taken.</summary>
    public double LastDecisionAt { get; init; }

    /// <summary>The stop's name once the run stopped (the done line's <c>stop=</c>), else null.</summary>
    public string? Stop { get; init; }

    /// <summary>M5 7 S2d: the worn set at the write (<see cref="Lifestone.Client.Session.Mage.WornMemory"/>: every worn piece that is no caster, by object id and location), or null in a file written before it.</summary>
    public List<Lifestone.Client.Session.Mage.WornEntry>? Worn { get; init; }

    /// <summary>
    /// True when the run ended by its own stop rule or by a request (<c>ok=yes</c>): a finished run, over which a new run starts fresh. A run that stopped by a failure (<c>ok=no</c>: a watchdog, a failed reconnect, a station)
    /// is not finished: the next start resumes its totals and its budget.
    /// </summary>
    public bool Completed { get; init; }

    /// <summary>The checkpoint as JSON (indented, camel-cased).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads <paramref name="json"/> for <paramref name="character"/>: the checkpoint, or null with <paramref name="why"/> naming what is wrong (<c>torn</c>, <c>unreadable</c>, <c>newer schema</c>, <c>foreign character</c>).
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="character">The character this run plays.</param>
    /// <param name="why">The reason it was refused, else null.</param>
    /// <param name="characterGuid">The character's guid now (0: unknown, no guid check).</param>
    /// <param name="runTag">The run's tag now (empty: none).</param>
    public static AutopilotCheckpoint? Parse(string json, string character, out string? why, uint characterGuid = 0, string runTag = "")
    {
        ArgumentNullException.ThrowIfNull(json);
        why = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version < 1)
            {
                why = "unreadable: no schema version";
                return null;
            }

            if (version > CurrentSchema)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"newer schema {version} (this build reads {CurrentSchema})");
                return null;
            }

            var checkpoint = root.Deserialize<AutopilotCheckpoint>(Options);
            if (checkpoint is null || checkpoint.Cycle < 1 || checkpoint.Cycles < 0 || checkpoint.MinutesUsed < 0 || !double.IsFinite(checkpoint.MinutesUsed))
            {
                why = "unreadable: the counts are out of range";
                return null;
            }

            if (!string.Equals(checkpoint.Character, character, StringComparison.OrdinalIgnoreCase))
            {
                why = "foreign character (the file is another character's)";
                return null;
            }

            if (checkpoint.CharacterGuid != 0 && characterGuid != 0 && checkpoint.CharacterGuid != characterGuid)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"another character (guid 0x{checkpoint.CharacterGuid:X8} in the file, 0x{characterGuid:X8} now: a character re-created under the same name)");
                return null;
            }

            if (!string.Equals(checkpoint.RunTag ?? string.Empty, runTag ?? string.Empty, StringComparison.Ordinal))
            {
                why = $"another run (tag '{checkpoint.RunTag}' in the file, '{runTag}' now)";
                return null;
            }

            return checkpoint;
        }
        catch (JsonException e)
        {
            why = "torn: " + e.Message.Split('\n')[0].Trim();
            return null;
        }
    }

    /// <summary>
    /// Reads the store: the checkpoint of <paramref name="character"/>, or null (no file, a finished run, or a file set aside; <paramref name="note"/> says which for the log). A refused file is moved aside by the store.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="character">The character this run plays.</param>
    /// <param name="note">One log phrase (the caller prefixes <c>checkpoint: </c>), or null when there was nothing to say.</param>
    /// <param name="warn">True when the note is a warning (a file was set aside or could not be read).</param>
    /// <param name="characterGuid">The character's guid now (0: unknown).</param>
    /// <param name="runTag">The run's tag now (empty: none).</param>
    public static AutopilotCheckpoint? Load(IAutopilotCheckpointStore store, string character, out string? note, out bool warn, uint characterGuid = 0, string runTag = "")
    {
        ArgumentNullException.ThrowIfNull(store);
        note = null;
        warn = false;
        string? text;
        try
        {
            text = store.Read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note = $"set aside: unreadable: {e.Message}";
            warn = true;
            return SetAside(store, ref note);
        }

        if (text is null)
        {
            return null;
        }

        var checkpoint = Parse(text, character, out var why, characterGuid, runTag);
        if (checkpoint is null)
        {
            note = "set aside: " + why;
            warn = true;
            return SetAside(store, ref note);
        }

        if (checkpoint.Completed)
        {
            note = $"the previous run finished (stop={checkpoint.Stop ?? "?"}): starting fresh";
            return null;
        }

        note = string.Create(CultureInfo.InvariantCulture, $"read: resuming cycle {checkpoint.Cycle} ({checkpoint.Cycles} ended) with {checkpoint.MinutesUsed:0.#} minutes used; the state is classified from the world, not from the file (it was {checkpoint.State}{(checkpoint.Stop is null ? string.Empty : ", stop=" + checkpoint.Stop)})");
        return checkpoint;
    }

    private static AutopilotCheckpoint? SetAside(IAutopilotCheckpointStore store, ref string? note)
    {
        try
        {
            note += "; moved to " + store.SetAside();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note += "; it could not be moved aside: " + e.Message;
        }

        return null;
    }
}
