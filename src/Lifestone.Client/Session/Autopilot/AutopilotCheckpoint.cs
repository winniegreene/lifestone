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

    /// <summary>
    /// M6 6 live round 2: the text for the tie memory only (<see cref="AutopilotCheckpoint.ReadTieMemory"/>), which must not use up a hand-back's empty first read (<see cref="ColdReadOnceStore"/>); every other store: <see cref="Read"/>.
    /// </summary>
    string? ReadForTies() => Read();
}

/// <summary>
/// A checkpoint store whose first checkpoint read is empty (a hand-back carries the old scenario's counts in memory, so the file written at the interrupt must not be counted again) and which writes through; <see cref="SetAside"/> passes on.
/// M6 6 live round 2: the tie memory reads through <see cref="ReadForTies"/>, so it no longer uses up the empty read (since M6 4's tie memory read first, every hand-back counted the interrupt's file twice: <c>interrupted=2</c>
/// and the deaths and kills of the ended cycles doubled in the gate's autopilot line).
/// </summary>
/// <param name="inner">The real store.</param>
public sealed class ColdReadOnceStore(IAutopilotCheckpointStore inner) : IAutopilotCheckpointStore
{
    private bool _read;

    /// <inheritdoc/>
    public string Describe => inner.Describe;

    /// <inheritdoc/>
    public string? Read()
    {
        if (!_read)
        {
            _read = true;
            return null;
        }

        return inner.Read();
    }

    /// <inheritdoc/>
    public string? ReadForTies() => inner.ReadForTies();

    /// <inheritdoc/>
    public void Write(string json) => inner.Write(json);

    /// <inheritdoc/>
    public string SetAside() => inner.SetAside();
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

    /// <summary>M6 1 fix (1-R11): the learn scrolls the trips bought.</summary>
    public int ScrollsBought { get; init; }

    /// <summary>M6 1 fix (1-R11): the spells the trips learned.</summary>
    public int Learned { get; init; }

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

    /// <summary>M6 6 F3: the retreats taken (0 in a file written before it).</summary>
    public int Retreats { get; init; }

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
            ScrollsBought = t.ScrollsBought,
            Learned = t.Learned,
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
            Retreats = t.Retreats,
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
        ScrollsBought = ScrollsBought,
        Learned = Learned,
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
        Retreats = Retreats,
        Seconds = Seconds,
    };
}

/// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): a kill's corpse the stopped fight had found and not looted (the corpse's guid and the creature's name).</summary>
public sealed class CheckpointCorpse
{
    /// <summary>The corpse's guid.</summary>
    public uint Guid { get; init; }

    /// <summary>The creature's name (the corpse is "Corpse of name").</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>M6 item 14: the lifestone bind's proof as the checkpoint keeps it (<see cref="BindProof"/>): the lifestone's cell and point the server confirmed the Use at, whether the tie is confirmed too, and what confirmed it.</summary>
public sealed class CheckpointBind
{
    /// <summary>The lifestone's cell.</summary>
    public uint Cell { get; init; }

    /// <summary>The lifestone's point in the cell, x.</summary>
    public float X { get; init; }

    /// <summary>The lifestone's point in the cell, y.</summary>
    public float Y { get; init; }

    /// <summary>The lifestone's point in the cell, z.</summary>
    public float Z { get; init; }

    /// <summary>True when the server confirmed the Lifestone Tie there too, or a Lifestone Recall landed home.</summary>
    public bool Tied { get; init; }

    /// <summary>What confirmed it, for the log.</summary>
    public string How { get; init; } = string.Empty;

    /// <summary>The checkpoint's copy of <paramref name="proof"/>, or null for none.</summary>
    /// <param name="proof">The proof.</param>
    public static CheckpointBind? From(BindProof? proof) => proof is null ? null : new CheckpointBind { Cell = proof.LifestoneCell, X = proof.X, Y = proof.Y, Z = proof.Z, Tied = proof.Tied, How = proof.How };

    /// <summary>The proof this entry makes.</summary>
    public BindProof ToProof() => new(Cell, X, Y, Z, Tied, string.IsNullOrWhiteSpace(How) ? "an earlier run" : How);
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

    /// <summary>M6 1 fix (1-R11): the skill ids the level-up spend trained from the credits over the run (the <c>[ladder]</c> line's <c>trained=</c>), or null in a file written before it.</summary>
    public List<uint>? Trained { get; init; }

    /// <summary>The stop's name once the run stopped (the done line's <c>stop=</c>), else null.</summary>
    public string? Stop { get; init; }

    /// <summary>
    /// M6 4 t3b (<c>m6-tie-memory</c>): the grounds whose portal tie the journey knew tied at the write (<see cref="Scenarios.Journey.JourneyFacts.PortalTies"/>: a Primary Portal Tie the rung change made, or a recall to the ground that
    /// landed), or null in a file written before it. ACE sends no portal-tie property (LinkedPortalOne is not SendOnLogin), so this is the only memory of a tie across runs; it is read back by <see cref="ReadTieMemory"/> at the next start,
    /// so the next trip recalls instead of walking the route. M6 4 F1 (4-R2): the primary tie is one slot, so the list holds that one ground (<see cref="Scenarios.Journey.JourneyFacts.PrimaryPortalSlot"/>); carried, every other ground with a tie
    /// record is known untied. A stale entry costs one recall: ACE's 0x04A3 sets it untied, or a landing in another ground's arrival names <c>recall:portal:slot-holds:&lt;ground&gt;</c> and moves the slot.
    /// </summary>
    public List<string>? PortalTied { get; init; }

    /// <summary>M6 6 ME (<c>m6-trip-mean-resume</c>): the trip's mean length in seconds at the write (<see cref="AutopilotScenario.TripMeanSeconds"/>), or null before any trip was folded and in a file written before it.</summary>
    public double? TripMeanSeconds { get; init; }

    /// <summary>M6 6 ME: the trips folded into <see cref="TripMeanSeconds"/> (0 in a file written before it).</summary>
    public int TripMeanCount { get; init; }

    /// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): the last kills' corpses found and not yet looted at the write (the fight's <see cref="Scenarios.FightScenario.KillCorpses"/>), or null in a file written before it; the resumed fight queues those still in view.</summary>
    public List<CheckpointCorpse>? Corpses { get; init; }

    /// <summary>M5 7 S2d: the worn set at the write (<see cref="Lifestone.Client.Session.Mage.WornMemory"/>: every worn piece that is no caster, by object id and location), or null in a file written before it.</summary>
    public List<Lifestone.Client.Session.Mage.WornEntry>? Worn { get; init; }

    /// <summary>M6 7 AY: the big pack rule's state (<see cref="Mage.PackHold"/>: the deaths to big packs and the drop-back), or null when nothing is remembered and in a file written before it. Read back by <see cref="ReadPackHold"/> at each start, so a restart keeps it.</summary>
    public Lifestone.Client.Session.Mage.PackHold? PackHold { get; init; }

    /// <summary>
    /// M6 item 14: the lifestone bind's proof at the write (<see cref="BindProof"/>), or null when none stands and in a file written before it. It is the character's, not the run's: read back by <see cref="ReadBindProof"/> at each start,
    /// whatever <see cref="Load"/> decides of the run, and only for the same character guid, so a start does not walk to the lifestone again.
    /// </summary>
    public CheckpointBind? Bind { get; init; }

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

    /// <summary>
    /// M6 7 AY: the big pack rule's state in the store's checkpoint of <paramref name="character"/> (<see cref="PackHold"/>), read as <see cref="ReadTieMemory"/> reads the tie: before <see cref="Load"/> and whatever it decides of the run, because the state is the character's: a finished run's file or another run tag's
    /// still gives it. It is given only when the file names the character and both guids are known and equal (a character re-created under the same name starts clean). Null when there is nothing to carry, with <paramref name="note"/> naming a refusal. Never throws for a bad file.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="character">The character this run plays.</param>
    /// <param name="characterGuid">The character's guid now.</param>
    /// <param name="note">One log phrase, or null.</param>
    public static Lifestone.Client.Session.Mage.PackHold? ReadPackHold(IAutopilotCheckpointStore store, string character, uint characterGuid, out string? note)
    {
        ArgumentNullException.ThrowIfNull(store);
        note = null;
        AutopilotCheckpoint? file;
        try
        {
            if (store.ReadForTies() is not { } text)
            {
                return null;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var version) || version < 1 || version > CurrentSchema)
            {
                note = "pack hold: none (no readable schema)";
                return null;
            }

            file = document.RootElement.Deserialize<AutopilotCheckpoint>(Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            note = "pack hold: none (unreadable: " + e.Message.Split('\n')[0].Trim() + ")";
            return null;
        }

        if (file?.PackHold is not { } hold)
        {
            return null;
        }

        if (!string.Equals(file.Character, character, StringComparison.OrdinalIgnoreCase) || file.CharacterGuid == 0 || characterGuid == 0 || file.CharacterGuid != characterGuid)
        {
            note = string.Create(CultureInfo.InvariantCulture, $"pack hold: not this character's (the file's {file.Character} 0x{file.CharacterGuid:X8}, now 0x{characterGuid:X8}): nothing is carried");
            return null;
        }

        note = string.Create(CultureInfo.InvariantCulture, $"pack hold: carried from the checkpoint: ground={hold.Ground} deaths={hold.Deaths} held={(hold.Held ? "yes" : "no")} mana_at_drop={hold.ManaAtDrop}");
        return hold;
    }

    /// <summary>
    /// M6 item 14: the lifestone bind's proof in the store's checkpoint of <paramref name="character"/> (<see cref="Bind"/>), read as <see cref="ReadTieMemory"/> reads the tie: whatever <see cref="Load"/> decides of the run, because the bind is the
    /// character's: a finished run's file or another run tag's still gives it. It is given only when the file names the character and both guids are known and equal (a character re-created under the same name is bound nowhere). Null when
    /// nothing is carried; <paramref name="why"/> always says what was read (the reason no proof is carried, or where it came from). Never throws for a bad file.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="character">The character this run plays.</param>
    /// <param name="characterGuid">The character's guid now.</param>
    /// <param name="why">One log phrase.</param>
    public static BindProof? ReadBindProof(IAutopilotCheckpointStore store, string character, uint characterGuid, out string why)
    {
        ArgumentNullException.ThrowIfNull(store);
        AutopilotCheckpoint? file;
        try
        {
            if (store.ReadForTies() is not { } text)
            {
                why = "no checkpoint file is there (a first start, or a character never run)";
                return null;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var version) || version < 1 || version > CurrentSchema)
            {
                why = "the checkpoint has no readable schema";
                return null;
            }

            file = document.RootElement.Deserialize<AutopilotCheckpoint>(Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            why = "the checkpoint is unreadable (" + e.Message.Split('\n')[0].Trim() + ")";
            return null;
        }

        if (file?.Bind is not { } bind)
        {
            why = "the checkpoint holds no bind proof (no bind was proven before it was written, or an older build wrote it)";
            return null;
        }

        if (!string.Equals(file.Character, character, StringComparison.OrdinalIgnoreCase) || file.CharacterGuid == 0 || characterGuid == 0 || file.CharacterGuid != characterGuid)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the checkpoint's bind proof is another character's (the file's {file.Character} 0x{file.CharacterGuid:X8}, now 0x{characterGuid:X8}: a character re-created under the same name is bound nowhere)");
            return null;
        }

        if (bind.Cell == 0 || !float.IsFinite(bind.X) || !float.IsFinite(bind.Y) || !float.IsFinite(bind.Z))
        {
            why = "the checkpoint's bind proof names no lifestone cell or point";
            return null;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"the checkpoint of character 0x{characterGuid:X8} (written in the {file.State} state{(file.Stop is null ? string.Empty : ", stop=" + file.Stop)})");
        return bind.ToProof();
    }

    /// <summary>
    /// M6 4 t3b (<c>m6-tie-memory</c>): the grounds known tied in the store's checkpoint of <paramref name="character"/> (<see cref="PortalTied"/>), read before <see cref="Load"/> and whatever it decides of the run: a tie is the character's,
    /// not the run's, so a finished run's file or another run tag's still gives it. It is given only when the file names the character and both guids are known and equal (a character re-created under the same name has no tie); else
    /// empty, with <paramref name="note"/> saying why (null when there was nothing to read). Never throws for a bad file.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="character">The character this run plays.</param>
    /// <param name="characterGuid">The character's guid now.</param>
    /// <param name="note">One log phrase, or null.</param>
    public static IReadOnlyList<string> ReadTieMemory(IAutopilotCheckpointStore store, string character, uint characterGuid, out string? note)
    {
        ArgumentNullException.ThrowIfNull(store);
        note = null;
        string? text;
        try
        {
            text = store.ReadForTies();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note = "tie memory: unreadable: " + e.Message;
            return [];
        }

        if (text is null)
        {
            return [];
        }

        AutopilotCheckpoint? file;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var version) || version < 1 || version > CurrentSchema)
            {
                note = "tie memory: none (no readable schema)";
                return [];
            }

            file = document.RootElement.Deserialize<AutopilotCheckpoint>(Options);
        }
        catch (JsonException e)
        {
            note = "tie memory: none (torn: " + e.Message.Split('\n')[0].Trim() + ")";
            return [];
        }

        if (file?.PortalTied is not { Count: > 0 } tied)
        {
            return [];
        }

        if (!string.Equals(file.Character, character, StringComparison.OrdinalIgnoreCase) || file.CharacterGuid == 0 || characterGuid == 0 || file.CharacterGuid != characterGuid)
        {
            note = string.Create(CultureInfo.InvariantCulture, $"tie memory: not this character's (the file's {file.Character} 0x{file.CharacterGuid:X8}, now 0x{characterGuid:X8}): no tie is carried");
            return [];
        }

        var grounds = tied.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray();
        note = $"tie memory: {string.Join(",", grounds)} tied (the file's {file.State}{(file.Stop is null ? string.Empty : ", stop=" + file.Stop)})";
        return grounds;
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
