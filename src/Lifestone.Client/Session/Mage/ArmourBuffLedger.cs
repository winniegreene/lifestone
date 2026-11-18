using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One armour line on one worn piece: the spell that is on it and when it ends on the session clock (M4 4.1 lane b).
/// </summary>
/// <param name="SpellId">The spell id the ledger holds for the line on the piece: the cast's, or, for a line found by an appraisal, the highest level of the line the piece's enchantments cover.</param>
/// <param name="ExpiresAt">The session-clock second the enchantment ends (the cast's UseDone time plus the SpellTable duration), or NaN when the line is on the piece but the time left is not known (an appraisal carries no clock).</param>
public readonly record struct ArmourEntry(uint SpellId, double ExpiresAt)
{
    /// <summary>True when the time left is known.</summary>
    public bool TimeKnown => !double.IsNaN(ExpiresAt);
}

/// <summary>Where a ledger's remaining seconds are kept across a log-off, keyed by the character's guid (the host's per-character user data: <c>ArmourLedgerFileStore</c> over a file per character guid; the snapshot is one-shot, see <see cref="ArmourLedgerSeeder"/>).</summary>
public interface IArmourLedgerStore
{
    /// <summary>The text <see cref="Save"/> kept for the character, or null.</summary>
    /// <param name="characterGuid">The character's guid.</param>
    string? Load(uint characterGuid);

    /// <summary>Keeps the text for the character, replacing an earlier one.</summary>
    /// <param name="characterGuid">The character's guid.</param>
    /// <param name="json">The ledger's snapshot (<see cref="ArmourBuffLedger.Export"/>).</param>
    void Save(uint characterGuid, string json);
}

/// <summary>
/// The armour lines' memory (M4 4.1 lane b; plan section 4.8, closes <c>TODO(m3-buff-armour-memory)</c>): for each worn piece and armour line (Impenetrability, Bludgeon Bane) the spell on it and when it ends, kept
/// outside the <see cref="BuffCycle"/> so that a second cycle object on the same session knows what the first cast. The server sends no enchantment registry for an item, so the client has to remember (ACE sends the
/// registry of the player only; an item's enchantment ids come in an appraisal and carry no clock, <c>AppraiseInfo.cs</c> 494-503).
/// </summary>
/// <remarks>
/// <para><b>The clock across a log-off.</b> ACE counts an item enchantment down by its owner's heartbeat, not by the wall clock: <c>EnchantmentManager.HeartBeat</c> (<c>WorldObjects/Managers/EnchantmentManager.cs</c> 1217-1233) calls
/// <c>HeartBeatEnchantmentsAndReturnExpired</c> (<c>ACE.Entity/Models/PropertiesEnchantmentRegistryExtensions.cs</c> 239-259), which takes the heartbeat interval off each entry's <c>StartTime</c> until it reaches minus the duration
/// (line 251 and 254). The worn pieces are ticked from <c>Creature_Tick.cs</c> 15-27, <c>Container_Tick.cs</c> 38-51 and <c>WorldObject_Tick.cs</c> 98-101, all of them driven by the owner being in the world; nothing
/// in the player's login path (<c>Player_Networking.cs</c>, <c>Player_Database.cs</c>) advances <c>StartTime</c> by the time spent logged out. So the clock <b>stops while the character is offline</b>: the seconds left at log-off are
/// the seconds left at the next login. <see cref="Export"/> keeps them, <see cref="ArmourLedgerSeeder"/> restores them after the appraisal has proved the line is still on the piece.</para>
/// <para><b>Reading is allocation free</b> (the cycle reads it on every line it considers); writing allocates.</para>
/// </remarks>
public sealed class ArmourBuffLedger
{
    /// <summary>The snapshot's format version (<see cref="Export"/>).</summary>
    public const int SnapshotVersion = 1;

    private static readonly ConditionalWeakTable<GameState, ArmourBuffLedger> PerSession = new();

    /// <summary>M6 7 AN: the lines a climber's pass left missing at its mana limit: not counted due until <see cref="PassLeftUntil"/>, so the upkeep does not stop the fight for them at once.</summary>
    public HashSet<string> PassLeft { get; } = [];

    /// <summary>M6 7 AN: the session clock at which <see cref="PassLeft"/> counts as due again.</summary>
    public double PassLeftUntil { get; set; } = double.NegativeInfinity;

    private readonly Dictionary<uint, Dictionary<string, ArmourEntry>> _pieces = [];
    private readonly List<uint> _scratch = [];

    /// <summary>The ledger of a session: one per <see cref="GameState"/> (a login makes a new state, so a new login starts with an empty ledger), made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static ArmourBuffLedger For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static _ => new ArmourBuffLedger());
    }

    /// <summary>The number of (piece, line) entries held.</summary>
    public int Count
    {
        get
        {
            var n = 0;
            foreach (var lines in _pieces.Values)
            {
                n += lines.Count;
            }

            return n;
        }
    }

    /// <summary>True once <see cref="ArmourLedgerSeeder"/> has appraised the worn pieces for this session (it does so once, whatever it found).</summary>
    public bool Seeded { get; private set; }

    /// <summary>True when the ledger has nothing and no appraisal has been tried: the next pass should seed it.</summary>
    public bool NeedsSeed => !Seeded && Count == 0;

    /// <summary>
    /// The session-clock time of this login, or NaN when the host did not say (M4 4.1 fix, review R3). The seeder restores a saved line as the seconds it had at log-off counted from this time, not from the time of the appraisal:
    /// ACE's clock for an item enchantment runs while the character is online, so seconds played before the seed (the driver switched on late) are not kept.
    /// </summary>
    public double LoggedInAt { get; set; } = double.NaN;

    /// <summary>Marks the appraisal as done for this session.</summary>
    public void MarkSeeded() => Seeded = true;

    /// <summary>Records a cast that landed on a piece: the spell, the session time of the cast's UseDone and the SpellTable duration (the line then ends at their sum).</summary>
    /// <param name="piece">The worn piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    /// <param name="spellId">The spell cast.</param>
    /// <param name="doneAt">The session-clock time of the cast's UseDone.</param>
    /// <param name="duration">The SpellTable duration in seconds.</param>
    public void Record(uint piece, string line, uint spellId, double doneAt, double duration) => Set(piece, line, new ArmourEntry(spellId, doneAt + duration));

    /// <summary>Records a line an appraisal found on a piece, with no known time left (the conservative rule: <see cref="Remaining"/> says NaN and the cycle treats it as due).</summary>
    /// <param name="piece">The worn piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    /// <param name="spellId">The spell of the line the piece's enchantments cover.</param>
    public void RecordPresent(uint piece, string line, uint spellId) => Set(piece, line, new ArmourEntry(spellId, double.NaN));

    /// <summary>Records a line with the seconds left it had when the character logged off (the restore).</summary>
    /// <param name="piece">The worn piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    /// <param name="spellId">The spell on the piece.</param>
    /// <param name="remaining">The seconds left at the session time <paramref name="now"/>.</param>
    /// <param name="now">The session-clock time.</param>
    public void RecordRemaining(uint piece, string line, uint spellId, double remaining, double now) => Set(piece, line, new ArmourEntry(spellId, now + remaining));

    /// <summary>The entry of a piece and line, or false.</summary>
    /// <param name="piece">The worn piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    /// <param name="entry">The entry.</param>
    public bool TryGet(uint piece, string line, out ArmourEntry entry)
    {
        if (_pieces.TryGetValue(piece, out var lines) && lines.TryGetValue(line, out entry))
        {
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>
    /// The seconds left on the line on the piece at <paramref name="now"/>: 0 when the ledger has none or it ended, NaN when the line is on the piece with an unknown time left (an appraisal found it).
    /// </summary>
    /// <param name="piece">The worn piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    /// <param name="now">The session-clock time.</param>
    public double Remaining(uint piece, string line, double now)
    {
        if (!TryGet(piece, line, out var entry))
        {
            return 0;
        }

        if (!entry.TimeKnown)
        {
            return double.NaN;
        }

        var left = entry.ExpiresAt - now;
        return left > 0 ? left : 0;
    }

    /// <summary>Forgets one line on a piece (a cast that did not name the piece).</summary>
    /// <param name="piece">The piece's guid.</param>
    /// <param name="line">The armour line's profile name.</param>
    public void Forget(uint piece, string line)
    {
        if (_pieces.TryGetValue(piece, out var lines) && lines.Remove(line) && lines.Count == 0)
        {
            _pieces.Remove(piece);
        }
    }

    /// <summary>Forgets everything about a piece (it was taken off or is gone).</summary>
    /// <param name="piece">The piece's guid.</param>
    public void Forget(uint piece) => _pieces.Remove(piece);

    /// <summary>
    /// M6 14 (live 2026-11-16 00:35-01:28 UTC): the worn pieces a self-cast armour line passed over while it named the others, learned not enchantable by ACE's rule (IsEnchantable, ResistMagic 9999: every cloak) from
    /// ACE's own cast lines (<see cref="BuffCycle"/>), shared by every buff cycle of the session (the fight's refresh learned a Chevron Cloak while the ladder's gate read, another cycle, still counted it). A piece taken
    /// off is forgotten (<see cref="Prune"/>).
    /// </summary>
    public HashSet<uint> NotEnchanted { get; } = [];

    /// <summary>The self-cast armour casts that passed over each worn piece while they named others (see <see cref="NotEnchanted"/>).</summary>
    public Dictionary<uint, int> SilentCasts { get; } = [];

    private void ForgetLearnedTakenOff(ReadOnlySpan<uint> worn)
    {
        _scratch.Clear();
        foreach (var piece in NotEnchanted)
        {
            if (!worn.Contains(piece))
            {
                _scratch.Add(piece);
            }
        }

        foreach (var piece in SilentCasts.Keys)
        {
            if (!worn.Contains(piece))
            {
                _scratch.Add(piece);
            }
        }

        foreach (var piece in _scratch)
        {
            NotEnchanted.Remove(piece);
            SilentCasts.Remove(piece);
        }

        _scratch.Clear();
    }

    /// <summary>Forgets every piece that is not in <paramref name="worn"/> (what the player still wears), so a piece taken off loses its lines.</summary>
    /// <param name="worn">The guids of everything equipped.</param>
    /// <returns>The number of pieces forgotten.</returns>
    public int Prune(ReadOnlySpan<uint> worn)
    {
        if (NotEnchanted.Count > 0 || SilentCasts.Count > 0)
        {
            ForgetLearnedTakenOff(worn);
        }

        if (_pieces.Count == 0)
        {
            return 0;
        }

        _scratch.Clear();
        foreach (var piece in _pieces.Keys)
        {
            if (!worn.Contains(piece))
            {
                _scratch.Add(piece);
            }
        }

        foreach (var piece in _scratch)
        {
            Forget(piece);
        }

        return _scratch.Count;
    }

    /// <summary>The text of a snapshot with no entries: what the seeder writes over a snapshot it has read, so the saved seconds are used by one login only (M4 4.1 fix, review R3).</summary>
    /// <param name="characterGuid">The character's guid.</param>
    public static string EmptySnapshot(uint characterGuid) => JsonSerializer.Serialize(new Snapshot { Version = SnapshotVersion, Character = characterGuid });

    /// <summary>
    /// The ledger's remaining seconds as the text a store keeps across a log-off: the character's guid and, for each piece and line with a known time left, the spell and the seconds left at <paramref name="now"/>.
    /// A line with an unknown time or none left is not written (nothing is known to restore).
    /// </summary>
    /// <param name="characterGuid">The character's guid.</param>
    /// <param name="now">The session-clock time of the log-off.</param>
    public string Export(uint characterGuid, double now)
    {
        var snapshot = new Snapshot { Version = SnapshotVersion, Character = characterGuid };
        foreach (var (piece, lines) in _pieces)
        {
            foreach (var (line, entry) in lines)
            {
                var left = entry.ExpiresAt - now;
                if (entry.TimeKnown && left > 0)
                {
                    snapshot.Entries.Add(new SnapshotEntry { Piece = piece, Line = line, Spell = entry.SpellId, Remaining = Math.Round(left, 1) });
                }
            }
        }

        snapshot.Entries.Sort(static (a, b) => a.Piece != b.Piece ? a.Piece.CompareTo(b.Piece) : string.CompareOrdinal(a.Line, b.Line));
        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>
    /// Reads a snapshot <see cref="Export"/> wrote. The result is only what the character had at log-off: <see cref="ArmourLedgerSeeder"/> restores an entry into the ledger when the appraisal shows the same spell still on the same piece.
    /// </summary>
    /// <param name="json">The text.</param>
    /// <param name="characterGuid">The character the ledger is for; a snapshot of another character is refused.</param>
    /// <param name="why">Why the text was refused, when it was.</param>
    /// <returns>The saved entries (empty and false when the text is unusable).</returns>
    public static IReadOnlyList<(uint Piece, string Line, uint Spell, double Remaining)> ReadSnapshot(string? json, uint characterGuid, out string? why)
    {
        why = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            why = "nothing was saved";
            return [];
        }

        Snapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(json);
        }
        catch (JsonException e)
        {
            why = "the saved text is not valid JSON: " + e.Message;
            return [];
        }

        if (snapshot is null || snapshot.Version != SnapshotVersion)
        {
            why = "the saved text has the version " + (snapshot?.Version.ToString(CultureInfo.InvariantCulture) ?? "none") + ", not " + SnapshotVersion.ToString(CultureInfo.InvariantCulture);
            return [];
        }

        if (snapshot.Character != characterGuid)
        {
            why = "the saved text is of another character";
            return [];
        }

        var entries = new List<(uint, string, uint, double)>(snapshot.Entries.Count);
        foreach (var e in snapshot.Entries)
        {
            if (e.Line is { Length: > 0 } && e.Remaining > 0 && !double.IsNaN(e.Remaining))
            {
                entries.Add((e.Piece, e.Line, e.Spell, e.Remaining));
            }
        }

        return entries;
    }

    private void Set(uint piece, string line, ArmourEntry entry)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_pieces.TryGetValue(piece, out var lines))
        {
            lines = new Dictionary<string, ArmourEntry>(StringComparer.Ordinal);
            _pieces[piece] = lines;
        }

        lines[line] = entry;
    }

    private sealed class Snapshot
    {
        public int Version { get; set; }

        public uint Character { get; set; }

        public List<SnapshotEntry> Entries { get; set; } = [];
    }

    private sealed class SnapshotEntry
    {
        public uint Piece { get; set; }

        public string Line { get; set; } = string.Empty;

        public uint Spell { get; set; }

        public double Remaining { get; set; }
    }
}

/// <summary>One armour line as the seeder needs it: its profile name and its levels' SpellTable rows in ascending order (null where the table lacks the spell).</summary>
/// <param name="Name">The profile line's name (the ledger's key).</param>
/// <param name="Levels">The levels' rows, lowest first.</param>
public sealed record ArmourLineSpells(string Name, IReadOnlyList<SpellInfo?> Levels);

/// <summary>
/// The appraisal that seeds an empty <see cref="ArmourBuffLedger"/> after a login (M4 4.1 lane b; plan section 4.8 b, items 2 and 3): each worn piece appraised once through <see cref="IActionApi.Identify"/>, and a line whose spell,
/// or a stronger spell of the same category (the SpellTable's <see cref="SpellInfo.Category"/>, and <see cref="SpellInfo.Power"/> as its level), is among the piece's <see cref="Appraisal.ItemEnchantments"/> is recorded
/// present on that piece. A stepped machine like the cycle: <see cref="Start"/>, then <see cref="Step"/> every frame until it is <see cref="BuffStatus.Done"/>; it never fails (an appraisal that did not answer leaves its piece
/// out and is logged), so a missing answer costs a recast, never a stuck cycle.
/// </summary>
/// <remarks>
/// <para><b>The time left.</b> The appraisal carries no clock. If the store has the seconds left the previous session saved (<see cref="ArmourBuffLedger.Export"/>, ACE's clock stops while the owner is offline: see
/// <see cref="ArmourBuffLedger"/>), a saved entry is restored for a (piece, line) only when the appraisal shows the very same spell id still on the piece; otherwise the line is present with an unknown time, which the cycle treats as due
/// at once. The host's store (<c>ArmourLedgerFileStore</c>, a file per character guid) holds the previous session's snapshot; the restore is <b>one-shot</b> (the seeder writes an empty snapshot right after reading it, so only the login that follows a save restores,
/// and time played without the driver is never counted as kept) and counts the seconds played since the login (<see cref="ArmourBuffLedger.LoggedInAt"/>), because ACE's clock runs while the character is online. With no saved time the conservative rule runs.</para>
/// <para>Allocation: a pass that seeds allocates once per login, not per frame; a step that waits allocates nothing.</para>
/// </remarks>
public sealed class ArmourLedgerSeeder
{
    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly IReadOnlyList<ArmourLineSpells> _lines;
    private readonly ArmourBuffLedger _ledger;
    private readonly IArmourLedgerStore? _store;
    private readonly ILogSink _log;
    private readonly List<(uint Piece, IdentifyHandle Handle)> _pending = [];
    private readonly List<SpellInfo> _enchantments = [];
    private IReadOnlyList<(uint Piece, string Line, uint Spell, double Remaining)> _saved = [];
    private double _savedAt;
    private double _now;
    private int _found;
    private int _restored;

    /// <summary>The seeder over a session's Action API and read model.</summary>
    /// <param name="actions">The Action API (<see cref="IActionApi.Identify"/>).</param>
    /// <param name="state">The read model (the character's guid, the appraisals).</param>
    /// <param name="spells">The SpellTable's spells: the category and level of the enchantments found.</param>
    /// <param name="lines">The armour lines and their levels.</param>
    /// <param name="ledger">The ledger to seed.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="store">Where the previous session's remaining seconds are kept, or null (the conservative rule).</param>
    public ArmourLedgerSeeder(IActionApi actions, GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, IReadOnlyList<ArmourLineSpells> lines, ArmourBuffLedger ledger, ILogSink log, IArmourLedgerStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(log);
        _actions = actions;
        _state = state;
        _spells = spells;
        _lines = lines;
        _ledger = ledger;
        _log = log;
        _store = store;
    }

    /// <summary>The pieces whose appraisal found at least one line.</summary>
    public int PiecesWithLines { get; private set; }

    /// <summary>The lines recorded present (a restored one included).</summary>
    public int LinesFound => _found;

    /// <summary>Of <see cref="LinesFound"/>, the lines restored with a known time left.</summary>
    public int LinesRestored => _restored;

    /// <summary>Where the seeder is.</summary>
    public BuffStatus Status { get; private set; } = BuffStatus.Done;

    /// <summary>Sends one <see cref="IActionApi.Identify"/> for each piece and reads the saved seconds, if a store has them.</summary>
    /// <param name="now">The session-clock time.</param>
    /// <param name="pieces">The worn armour and clothing pieces' guids.</param>
    public void Start(double now, ReadOnlySpan<uint> pieces)
    {
        _pending.Clear();
        _found = 0;
        _restored = 0;
        PiecesWithLines = 0;
        _saved = [];
        _now = now;
        _savedAt = double.IsNaN(_ledger.LoggedInAt) ? now : Math.Min(_ledger.LoggedInAt, now);
        if (_store is not null)
        {
            var guid = _state.Self.Guid;
            var text = _store.Load(guid);
            _saved = ArmourBuffLedger.ReadSnapshot(text, guid, out var why);
            if (!string.IsNullOrWhiteSpace(text))
            {
                // One-shot (review R3): what was saved is used by this login only. It is overwritten with an empty snapshot now and written again only by a log-off that follows a session which ran the ledger.
                _store.Save(guid, ArmourBuffLedger.EmptySnapshot(guid));
            }

            if (why is not null)
            {
                _log.Info(BuffCycle.LogCategory, "armour memory: no saved time left (" + why + ")");
            }
        }

        foreach (var piece in pieces)
        {
            _pending.Add((piece, _actions.Identify(piece, ActionSource.Host)));
        }

        _log.Info(BuffCycle.LogCategory, string.Create(CultureInfo.InvariantCulture, $"armour memory: the ledger is empty after the login, so {_pending.Count} worn pieces are appraised once"));
        Status = BuffStatus.Running;
        if (_pending.Count == 0)
        {
            Finish();
        }
    }

    /// <summary>Reads the answers once all of them are in (an appraisal that timed out counts as answered, with nothing in it); <see cref="BuffStatus.Done"/> when the seeder has finished.</summary>
    public BuffStatus Step()
    {
        if (Status != BuffStatus.Running)
        {
            return Status;
        }

        foreach (var (_, handle) in _pending)
        {
            if (!handle.IsCompleted)
            {
                return Status;
            }
        }

        foreach (var (piece, handle) in _pending)
        {
            Read(piece, handle);
        }

        Finish();
        return Status;
    }

    private void Finish()
    {
        _ledger.MarkSeeded();
        Status = BuffStatus.Done;
        _log.Info(
            BuffCycle.LogCategory,
            string.Create(CultureInfo.InvariantCulture, $"armour memory: appraised {_pending.Count} pieces, {_found} lines present on {PiecesWithLines} of them, {_restored} with a restored time left; a present line with no saved time is due at once (the conservative rule)"));
    }

    private void Read(uint piece, IdentifyHandle handle)
    {
        if (handle.Result is not { Kind: IdentifyResultKind.Appraised } result)
        {
            _log.Warn(BuffCycle.LogCategory, string.Create(CultureInfo.InvariantCulture, $"armour memory: the appraisal of piece 0x{piece:X8} gave no tables ({handle.Outcome?.Kind.ToString() ?? "no outcome"}{(handle.Result is { } r ? ", " + r.Kind : string.Empty)}); its lines are unknown and will be cast"));
            return;
        }

        _enchantments.Clear();
        var ids = result.Appraisal.ItemEnchantments;
        foreach (var id in ids)
        {
            if (_spells.TryGetValue(id, out var info))
            {
                _enchantments.Add(info);
            }
        }

        var any = false;
        foreach (var line in _lines)
        {
            if (TryRestore(piece, line, ids))
            {
                any = true;
                continue;
            }

            for (var level = line.Levels.Count - 1; level >= 0; level--)
            {
                if (line.Levels[level] is { } wanted && Covers(wanted))
                {
                    _ledger.RecordPresent(piece, line.Name, wanted.Id);
                    _found++;
                    any = true;
                    break;
                }
            }
        }

        if (any)
        {
            PiecesWithLines++;
        }
    }

    /// <summary>The saved seconds of a (piece, line) are restored when the appraisal shows the very same spell on the piece.</summary>
    private bool TryRestore(uint piece, ArmourLineSpells line, System.Collections.Immutable.ImmutableArray<uint> ids)
    {
        foreach (var (savedPiece, savedLine, spell, remaining) in _saved)
        {
            if (savedPiece == piece && string.Equals(savedLine, line.Name, StringComparison.Ordinal) && ids.Contains(spell))
            {
                if (_savedAt + remaining <= _now)
                {
                    continue; // the seconds played since the login have used it up: the line is on the piece with no time known, so it is due at once
                }

                _ledger.RecordRemaining(piece, line.Name, spell, remaining, _savedAt);
                _found++;
                _restored++;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when one of the piece's enchantments is the spell, or a stronger one of its category.</summary>
    private bool Covers(SpellInfo wanted)
    {
        foreach (var held in _enchantments)
        {
            if (held.Id == wanted.Id || (held.Category == wanted.Category && held.Power >= wanted.Power))
            {
                return true;
            }
        }

        return false;
    }
}
