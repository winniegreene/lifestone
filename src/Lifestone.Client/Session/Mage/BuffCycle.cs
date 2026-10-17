using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where a <see cref="BuffCycle"/> is: running, finished (<see cref="BuffCycle.Result"/> is set) or failed (the result carries the reason).</summary>
public enum BuffStatus
{
    /// <summary>A pass is under way: step again.</summary>
    Running,

    /// <summary>The pass ended; <see cref="BuffCycle.Result"/> says what it cast.</summary>
    Done,

    /// <summary>The pass could not go on (a loud failure: the reason is in <see cref="BuffResult.Failure"/>); the result holds what was done before.</summary>
    Failed,
}

/// <summary>
/// What a buff pass left (M3 2.1 s0b): the lines active over the lines wanted, the level of each line, the re-buff flag, the least time left, the fizzles, the mana loop's counters and the lines it missed.
/// Built once at the end of a pass (<see cref="Fields"/> too), never per tick.
/// </summary>
public sealed record BuffResult
{
    /// <summary>The lines wanted (the profile's 24 buff lines).</summary>
    public int Wanted { get; init; }

    /// <summary>The lines whose buff is active at the end (<c>buffs=&lt;active&gt;/&lt;wanted&gt;</c>).</summary>
    public int Active { get; init; }

    /// <summary>The level of every line, short names, comma-separated, in the cycle's order (<c>Focus:VI,Will:VI,...</c>; <c>-</c> for a line with no active buff).</summary>
    public string Levels { get; init; } = string.Empty;

    /// <summary>True when this pass recast at least one line while it was still active (<see cref="RecastWhileActive"/> above 0; M3 2.1 fix B, review 2.1-R5: it counts the pass, not the cycle object's life).</summary>
    public bool Rebuffed => RecastWhileActive > 0;

    /// <summary>The casts of this pass that landed on a line whose buff was still active when the cast began (a re-buff pass's purpose; 0 for the ordered pass and for a re-buff pass that found every line expired).</summary>
    public int RecastWhileActive { get; init; }

    /// <summary>The seconds left on the buff that ends soonest, or NaN when no line is active.</summary>
    public double MinRemainingSeconds { get; init; } = double.NaN;

    /// <summary>The casts that fizzled (buff lines and mana lines; cumulative over the cycle object's life).</summary>
    public int Fizzles { get; init; }

    /// <summary>Stamina to Mana casts that fired (UseDone without a fizzle).</summary>
    public int TransfersFired { get; init; }

    /// <summary>Stamina to Mana casts sent, fired or not.</summary>
    public int TransfersTried { get; init; }

    /// <summary>Revitalize casts that fired.</summary>
    public int Revitalizes { get; init; }

    /// <summary>The lowest current mana the cycle saw, or null when it never read any.</summary>
    public uint? ManaMin { get; init; }

    /// <summary>One token per missed line, <c>Short:why</c> without spaces (the explanation is in <see cref="MissDetails"/>); empty when none.</summary>
    public IReadOnlyList<string> Misses { get; init; } = [];

    /// <summary>The full sentence for each miss, in the same order.</summary>
    public IReadOnlyList<string> MissDetails { get; init; } = [];

    /// <summary>True when the pass was a forced re-buff (decision D25 as narrowed: the one line closest to expiry was recast while still active, by a margin of its remaining time plus one second).</summary>
    public bool Forced { get; init; }

    /// <summary>"cycle" or "rebuff": the pass that ended.</summary>
    public string Pass { get; init; } = "cycle";

    /// <summary>The buff lines cast and landed in this pass (upgrades included).</summary>
    public int Cast { get; init; }

    /// <summary>Of <see cref="Cast"/>, the armour lines (Impenetrability, Bludgeon Bane) cast and landed: a new cycle object does not remember an earlier one's armour casts, so a second run recasts them (<c>TODO(m3-buff-armour-memory)</c>).</summary>
    public int ArmourCast { get; init; }

    /// <summary>The armour pieces whose cast was surpassed by a stronger version of the line they already held, counted active for the line (<c>surpassed=</c>; closes <c>TODO(m3-buff-armour-surpassed)</c>).</summary>
    public int Surpassed { get; init; }

    /// <summary>The lines of this pass cast again at a higher level than the one they held (the first cycle's upgrade pass; closes <c>TODO(m3-buff-upgrade-skip)</c>).</summary>
    public int Upgraded { get; init; }

    /// <summary>The session seconds from the pass's start to its end (<c>cycle_s</c>).</summary>
    public double CycleSeconds { get; init; }

    /// <summary>The combat-mode changes the cycle made itself, to NonCombat for a regeneration wait and back to Magic (cumulative over the object's life; <c>mode_changes</c>).</summary>
    public int ModeChanges { get; init; }

    /// <summary>The reason the pass failed, or null when it did not.</summary>
    public string? Failure { get; init; }

    /// <summary>The verdict fields: <c>buffs=a/w levels=... rebuffed=yes|no min_remaining_s=n fizzles=n transfers=f/t revitalizes=n mana_min=n cycle_s=n mode_changes=n misses=...</c> (and <c>forced=yes</c> for a forced pass).</summary>
    public string Fields { get; init; } = string.Empty;

    internal static string FormatFields(BuffResult r)
    {
        var misses = r.Misses.Count == 0 ? "none" : string.Join(';', r.Misses);
        var min = double.IsNaN(r.MinRemainingSeconds) ? "n/a" : Math.Floor(r.MinRemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
        var mana = r.ManaMin is { } m ? m.ToString(CultureInfo.InvariantCulture) : "n/a";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"buffs={r.Active}/{r.Wanted} levels={r.Levels} rebuffed={(r.Rebuffed ? "yes" : "no")} min_remaining_s={min} fizzles={r.Fizzles} transfers={r.TransfersFired}/{r.TransfersTried} revitalizes={r.Revitalizes} mana_min={mana} cycle_s={Math.Floor(r.CycleSeconds).ToString("0", CultureInfo.InvariantCulture)} mode_changes={r.ModeChanges} misses={misses}{(r.Forced ? " forced=yes" : string.Empty)} surpassed={r.Surpassed}");
    }
}

/// <summary>
/// The buff cycle (M3 2.1 s0b; plan section 4.5, decisions D10, D23, D24 and D25): a steppable machine that casts the profile's 24 buff lines on the mage in the owner's dependency order, keeps the mana to do it
/// with, and knows when each buff ends, over <see cref="IActionApi"/> and the read model only. It never ends the session (the buff scenario of s1 and the game's buff leg of s3 own that): <see cref="Start"/> or
/// <see cref="StartRebuff"/>, then <see cref="Step"/> every frame until it returns <see cref="BuffStatus.Done"/> or <see cref="BuffStatus.Failed"/>, then <see cref="Result"/>. The player must be in the magic combat mode
/// (ACE answers a cast made in another mode with a bare UseDone, <c>Player_Magic.cs</c> 84 and 275): the caller changes the mode, the cycle fails loudly when it is not Magic.
/// </summary>
/// <remarks>
/// <para><b>The order</b> is the owner's: Focus Self and Willpower Self, then Creature Enchantment Mastery Self, then the other masteries (War, Item, Life, Mana Conversion), then the rest in profile order.</para>
/// <para><b>The level</b> of a line is chosen just before its cast: the highest level whose <see cref="GearCasts.Chance"/> from <see cref="GearCasts.TryCurrentSkill"/> (the read model's current skill, which already
/// holds every enchantment the earlier casts added) is at least <see cref="BuffOptions.CastChance"/>. A fizzle (ServerError 0x0402) is retried once one level down, then the line is a miss: never a third try (no
/// silent retry). A line already active at a level at least the chosen one with more than the margin left is skipped by a cycle; a re-buff pass casts only the lines with less than the margin left. A cycle ends with an
/// <b>upgrade pass</b> (the lead's, M3 2.1 s1; closes <c>TODO(m3-buff-upgrade-skip)</c>): the casts of the ordered pass raised the skills, so each line holding a level below the one the rule gives now (Focus and Willpower, cast at VI from
/// skill 316, are chosen at VII from 373) is cast once more at that level, never retried; a second cycle on a buffed mage then casts nothing. A <b>forced</b> re-buff pass (D25 as narrowed) recasts only the one line closest to expiry.</para>
/// <para><b>Regeneration waits in NonCombat</b>: ACE halves stamina and health regeneration in any combat mode (<c>Creature_Vitals.cs</c> <c>GetStanceMod</c>); the mana wait sets NonCombat first and returns to Magic before the next cast, each change an awaited <see cref="IActionApi.SetCombatMode"/>.</para>
/// <para><b>The form</b> is <see cref="GearCasts.FormOf"/>, except the armour lines (Impenetrability, Bludgeon Bane: an Item Enchantment whose <c>NonComponentTargetType</c> is Vestements, not SelfTargeted), cast at the
/// caster's own guid (D23). ACE redirects such a cast to every equipped clothing piece and shield that is enchantable (<c>WorldObject_Magic.cs</c> 2067-2121, <c>TryCastItemEnchantment_WithRedirects</c>;
/// <c>Player_Magic.cs</c> 1391-1393 accepts a creature for Vestements), calls <c>HandleCastSpell(spell, item)</c> for each (the chat line "You cast &lt;spell&gt; on &lt;item&gt;", Magic chat, and no GameEvent 0x02C2 to the
/// player, <c>WorldObject_Magic.cs</c> 430-452) and ends the cast with UseDone 0 (<see cref="Actions.Trackers.CastTracker"/> ends such a cast as Sent). So the cycle takes the pieces from the chat lines the cast
/// produced: a worn armour piece (item type Armor or Clothing) with no line is a named miss.</para>
/// <para><b>Expiry.</b> A self cast's expiry is its registry entry's start plus its duration (<see cref="ActiveEnchantment.ExpirySeconds"/>, the spell id the line holds in
/// <see cref="PlayerQualities.ActiveEnchantments"/>); an armour line's is the cast's UseDone time plus the SpellTable duration, kept per worn piece (<c>TODO(m3-buff-armour-memory)</c>: the cycle object remembers it;
/// a new object does not know an earlier object's armour casts, since the server sends no registry for an item). A piece whose line says "surpassed" holds a stronger version of the line and is counted active (retail keeps the stronger enchantment; <see cref="BuffResult.Surpassed"/>, <c>surpassed=</c>; closes <c>TODO(m3-buff-armour-surpassed)</c>).</para>
/// <para><b>The mana loop</b> (D24, retail's way): before a cast whose cost (<see cref="GearCasts.ManaCeiling"/>: the base mana and ACE's armour surcharge, <c>Creature_Magic.cs</c> 14-60) is above the current mana,
/// the cycle casts Stamina to Mana Self at the highest level whose chance reaches the threshold and whose own cost is affordable, tried again until one fires (a try is a separate cast; <see cref="BuffOptions.TransferTries"/>
/// fizzles in a row fail the cycle); when the stamina is too low for a transfer to cover the shortfall (<see cref="BuffStatMods.Transfer"/> gives the yield) it casts Revitalize Self first by the same rule; when neither can
/// be cast (the mana is below the cheapest level that passes) it waits for regeneration, at most <see cref="BuffOptions.ManaWaitSeconds"/>, logged with the numbers, and fails the cycle when the wait runs out. A fizzle
/// costs 5 mana (<c>Player_Magic.cs</c> 566-572). The ceiling is a conservative <em>need</em>, not the price: ACE's Mana Conversion roll (<c>Creature_Magic.cs</c> <c>CalculateManaUsage</c> 14-56, <c>GetManaCost</c> 58-100) charges far less, so on the real server a whole cycle took 56 s of session time with 4 mana casts (M3 2.1 fix B, review 2.1-R2), not the 1,325 s the ceiling economy gives.</para>
/// <para><b>Per frame</b>: while a cast is in flight or the mana wait runs, a step reads a handle's outcome and one vital and allocates nothing; the sentences and strings are built per cast and at the end.</para>
/// </remarks>
public sealed class BuffCycle
{
    /// <summary>The log category of the cycle's lines (<c>[buff] ...</c>).</summary>
    public const string LogCategory = "buff";

    /// <summary>The stage between passes.</summary>
    public const string StageIdle = "idle";

    /// <summary>The stage while a buff cast is in flight.</summary>
    public const string StageCasting = "casting";

    /// <summary>The stage while the mana loop decides.</summary>
    public const string StageMana = "mana";

    /// <summary>The stage while a Stamina to Mana or Revitalize cast is in flight.</summary>
    public const string StageManaCasting = "mana-casting";

    /// <summary>The stage while the cycle waits for mana to regenerate.</summary>
    public const string StageManaWait = "mana-wait";

    /// <summary>The stage while a combat-mode change (NonCombat for a regeneration wait, Magic back) is in flight.</summary>
    public const string StageMode = "mode";

    /// <summary>The stage of a finished pass.</summary>
    public const string StageDone = "done";

    /// <summary>The stage of a failed pass.</summary>
    public const string StageFailed = "failed";

    /// <summary>How often the regeneration wait looks again at what can be cast, in seconds on the session clock.</summary>
    public const double ReevaluateSeconds = 1.0;

    /// <summary>How long a regeneration wait lasts before the cycle goes to NonCombat for it, in seconds on the session clock: a shorter wait is not worth two stance changes (each is an animation on ACE's side).</summary>
    public const double NonCombatAfterSeconds = 5.0;

    /// <summary>The most mana casts one buff line may need before the cycle fails (a loud bound against a loop that never reaches the cost).</summary>
    public const int ManaCastLimitPerLine = 64;

    private const int MaxPieces = 32;
    private const string TransferLineName = "Stamina to Mana Self";
    private const string BoostLineName = "Revitalize Self";

    private static readonly string[] Roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"];

    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["Strength Self"] = "Str",
        ["Endurance Self"] = "End",
        ["Coordination Self"] = "Coord",
        ["Quickness Self"] = "Quick",
        ["Focus Self"] = "Focus",
        ["Willpower Self"] = "Will",
        ["War Magic Mastery Self"] = "WarM",
        ["Creature Enchantment Mastery Self"] = "CreM",
        ["Item Enchantment Mastery Self"] = "ItemM",
        ["Life Magic Mastery Self"] = "LifeM",
        ["Mana Mastery Self"] = "ManaM",
        ["Arcane Enlightenment Self"] = "Arcane",
        ["Magic Resistance Self"] = "MagRes",
        ["Invulnerability Self"] = "Invuln",
        ["Impregnability Self"] = "Impreg",
        ["Armor Self"] = "Armor",
        ["Bludgeon Protection Self"] = "BludP",
        ["Regeneration Self"] = "Regen",
        ["Rejuvenation Self"] = "Rejuv",
        ["Mana Renewal Self"] = "ManaR",
        ["Impenetrability"] = "Impen",
        ["Bludgeon Bane"] = "BludB",
        ["Hermetic Link"] = "Herm",
        ["Defender"] = "Def",
    };

    private enum Phase : byte
    {
        Idle,
        NextLine,
        Mana,
        ManaWait,
        Casting,
        ManaCasting,
        ModeAway,
        ModeBack,
    }

    private enum ManaAction : byte
    {
        Wait,
        Transfer,
        Boost,
        Fail,
    }

    private enum ManaKind : byte
    {
        Transfer,
        Boost,
    }

    private sealed class WantedLine(int index, ProfileLine line, string shortName, SpellInfo?[] infos, bool armour)
    {
        public int Index { get; } = index;

        public ProfileLine Line { get; } = line;

        public string Short { get; } = shortName;

        public SpellInfo?[] Infos { get; } = infos;

        public bool Armour { get; } = armour;

        /// <summary>The index of the level of this armour line last cast (the memory below), or -1.</summary>
        public int CastIndex { get; set; } = -1;

        public uint[] PieceGuid { get; } = new uint[MaxPieces];

        public double[] PieceExpiry { get; } = new double[MaxPieces];

        public int PieceCount { get; set; }
    }

    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly BuffOptions _options;
    private readonly ILogSink _log;
    private readonly WantedLine[] _order;
    private readonly Dictionary<uint, (int Line, int Index)> _spellOf = [];
    private readonly ProfileLine _transferLine;
    private readonly ProfileLine _boostLine;
    private readonly SpellInfo?[] _transferInfos;
    private readonly SpellInfo?[] _boostInfos;
    private readonly TransferRow?[] _transferRows;
    private readonly List<string> _misses = [];
    private readonly List<string> _missDetails = [];
    private readonly uint[] _expectedGuid = new uint[MaxPieces];
    private readonly string[] _expectedName = new string[MaxPieces];
    private Phase _phase = Phase.Idle;
    private bool _rebuff;
    private bool _forced;
    private double _margin;
    private int _line;
    private int _castIndex;
    private int _lineFizzles;
    private int _manaCasts;
    private bool _wasActive;
    private ActionHandle? _pending;
    private ManaKind _manaKind;
    private long _chatMark;
    private uint _need;
    private int _transferStreak;
    private int _boostStreak;
    private double _waitStart = double.NaN;
    private double _nextEvaluate;
    private int _passCast;
    private int _passArmour;
    private int _passSurpassed;
    private int _fizzles;
    private int _fired;
    private int _tried;
    private int _revitalizes;
    private uint _manaMin = uint.MaxValue;
    private int _passRecast;
    private bool _upgrading;
    private int _upgraded;
    private int _forcedLine = -1;
    private bool _awayFromMagic;
    private int _modeChanges;
    private double _passStart;
    private int _expectedCount;
    private uint _lifeSkill;

    /// <summary>The buff cycle over a session's Action API and read model.</summary>
    /// <param name="session">The session (its <see cref="IGameSessionView.Actions"/> and <see cref="IGameSessionView.State"/>).</param>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): costs, forms and durations.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the cycle logs.</param>
    public BuffCycle(IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log)
        : this((session ?? throw new ArgumentNullException(nameof(session))).Actions, session.State, spells, options, log)
    {
    }

    /// <summary>The buff cycle over an Action API and a read model.</summary>
    /// <param name="actions">The Action API the casts go through (<see cref="IActionApi.Cast(uint, ActionSource)"/>).</param>
    /// <param name="state">The read model (the player's qualities, inventory and chat).</param>
    /// <param name="spells">The SpellTable's spells: costs, forms and durations.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the cycle logs.</param>
    public BuffCycle(IActionApi actions, GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _actions = actions;
        _state = state;
        _options = options;
        _log = log;

        var lines = new List<ProfileLine>();
        foreach (var name in new[] { "Focus Self", "Willpower Self", "Creature Enchantment Mastery Self" })
        {
            lines.Add(ArenaProfile.LineNamed(name) ?? throw new InvalidOperationException("the profile has no line " + name));
        }

        foreach (var line in ArenaProfile.Lines)
        {
            if (line.IsBuff && line.Name.EndsWith("Mastery Self", StringComparison.Ordinal) && !lines.Contains(line))
            {
                lines.Add(line);
            }
        }

        foreach (var line in ArenaProfile.Lines)
        {
            if (line.IsBuff && !lines.Contains(line))
            {
                lines.Add(line);
            }
        }

        _order = new WantedLine[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var infos = InfosOf(line, spells);
            _order[i] = new WantedLine(i, line, ShortNames.GetValueOrDefault(line.Name) ?? line.Name.Replace(" ", string.Empty, StringComparison.Ordinal), infos, line.Role == ProfileRole.ItemBuffArmor);
            for (var s = 0; s < line.Spells.Count; s++)
            {
                _spellOf[line.Spells[s].Id] = (i, s);
            }
        }

        _transferLine = ArenaProfile.LineNamed(TransferLineName) ?? throw new InvalidOperationException("the profile has no line " + TransferLineName);
        _boostLine = ArenaProfile.LineNamed(BoostLineName) ?? throw new InvalidOperationException("the profile has no line " + BoostLineName);
        _transferInfos = InfosOf(_transferLine, spells);
        _boostInfos = InfosOf(_boostLine, spells);
        _transferRows = new TransferRow?[_transferInfos.Length];
        for (var i = 0; i < _transferRows.Length; i++)
        {
            _transferRows[i] = _transferInfos[i] is { } info ? BuffStatMods.TransferOf(info.Id) : null; // looked up once: the lookup allocates
        }
    }

    /// <summary>Where the cycle is (one of the <c>Stage...</c> constants).</summary>
    public string Stage { get; private set; } = StageIdle;

    /// <summary>The status of the pass.</summary>
    public BuffStatus Status { get; private set; } = BuffStatus.Done;

    /// <summary>What the last pass left; null until a pass ended (or failed) and while the next runs.</summary>
    public BuffResult? Result { get; private set; }

    /// <summary>The number of lines the cycle wants (the profile's buff lines).</summary>
    public int Wanted => _order.Length;

    /// <summary>The wanted lines' names in the order the cycle casts them.</summary>
    public IReadOnlyList<string> LineOrder
    {
        get
        {
            var names = new string[_order.Length];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = _order[i].Line.Name;
            }

            return names;
        }
    }

    /// <summary>The short name of a wanted line as the result's <c>levels</c> shows it.</summary>
    /// <param name="lineName">The profile line's name.</param>
    public static string ShortNameOf(string lineName) => ShortNames.GetValueOrDefault(lineName) ?? lineName.Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>Starts a cycle: every line not already active at a level at least the one the rule gives, with more than the margin left, is cast (a second cycle on a buffed mage casts nothing new).</summary>
    /// <param name="now">The session clock's time.</param>
    /// <returns>False when a pass is already running.</returns>
    public bool Start(double now) => Begin(now, rebuff: false, forced: false);

    /// <summary>
    /// Starts a re-buff pass: only the lines with less than the margin left are cast, at the level the rule gives now. <paramref name="forced"/> (decision D25 as narrowed) recasts only the one line closest to expiry while it is still active
    /// (its margin is that line's remaining time plus one second; when no line is active every line is due), and the result says so (<see cref="BuffResult.RecastWhileActive"/>).
    /// <para><c>TODO(m3-buff-rebuff-driver)</c> (review 2.1-R10): nothing drives the timed re-buff yet. <c>StartRebuff(forced: false)</c> and <see cref="RebuffDue"/> have no caller outside the tests, which prove the schedule on a fake clock;
    /// the auto-buff goal will call them. A named TODO is enough for 2.1: no code path reaches them in a run, so nothing can silently skip a re-buff.</para>
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="forced">True for the forced pass (an explicit parameter, never a default).</param>
    /// <returns>False when a pass is already running.</returns>
    public bool StartRebuff(double now, bool forced) => Begin(now, rebuff: true, forced);

    /// <summary>The names of the wanted lines whose remaining time at <paramref name="now"/> is under <paramref name="margin"/> (a line with no active buff has none left: it is due). <c>TODO(m3-buff-rebuff-driver)</c>: no driver calls this outside the tests yet (see <see cref="StartRebuff"/>).</summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="margin">The margin in seconds (<see cref="double.PositiveInfinity"/> lists every line).</param>
    public IReadOnlyList<string> RebuffDue(double now, double margin)
    {
        var due = new List<string>();
        CollectExpected();
        foreach (var w in _order)
        {
            ActiveState(w, now, out _, out var remaining);
            if (remaining < margin)
            {
                due.Add(w.Line.Name);
            }
        }

        return due;
    }

    /// <summary>The seconds left on the buff that ends soonest over the wanted lines that are active at <paramref name="now"/>, or NaN when none is.</summary>
    /// <param name="now">The session clock's time.</param>
    public double MinRemainingSeconds(double now)
    {
        CollectExpected();
        var min = double.PositiveInfinity;
        foreach (var w in _order)
        {
            if (ActiveState(w, now, out _, out var remaining) && remaining < min)
            {
                min = remaining;
            }
        }

        return double.IsPositiveInfinity(min) ? double.NaN : min;
    }

    /// <summary>Advances the pass: reads a cast's outcome, decides the next cast or the mana step, sends it. Call every frame while <see cref="BuffStatus.Running"/>; it allocates nothing while it waits.</summary>
    /// <param name="now">The session clock's time.</param>
    public BuffStatus Step(double now)
    {
        if (Status != BuffStatus.Running)
        {
            return Status;
        }

        while (true)
        {
            bool again;
            switch (_phase)
            {
                case Phase.NextLine:
                    again = BeginLine(now);
                    break;
                case Phase.Mana:
                case Phase.ManaWait:
                    again = ManaStep(now);
                    break;
                case Phase.Casting:
                    again = CastOutcome(now);
                    break;
                case Phase.ManaCasting:
                    again = ManaOutcome(now);
                    break;
                case Phase.ModeAway:
                case Phase.ModeBack:
                    again = ModeOutcome(now);
                    break;
                default:
                    return Status;
            }

            if (!again || Status != BuffStatus.Running)
            {
                return Status;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ starting a pass

    private bool Begin(double now, bool rebuff, bool forced)
    {
        if (Status == BuffStatus.Running)
        {
            return false;
        }

        _rebuff = rebuff;
        _forced = forced;
        _margin = _options.RebuffMarginSeconds;
        _forcedLine = -1;
        _passRecast = 0;
        _upgrading = false;
        _upgraded = 0;
        _awayFromMagic = false;
        _passStart = now;
        _line = 0;
        _passCast = 0;
        _passArmour = 0;
        _passSurpassed = 0;
        _misses.Clear();
        _missDetails.Clear();
        _pending = null;
        _waitStart = double.NaN;
        _transferStreak = 0;
        _boostStreak = 0;
        Result = null;
        Status = BuffStatus.Running;
        if (_state.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            return Fail(now, "the player is not in the magic combat mode (ACE ends a cast made in another mode with a bare UseDone): the caller changes the mode before the cycle starts");
        }

        var forcedNote = string.Empty;
        if (forced)
        {
            // D25 as narrowed: only the line closest to expiry is recast, by a margin of its remaining time plus one second (one cast proves a recast while the buff is active).
            CollectExpected();
            var least = double.PositiveInfinity;
            foreach (var w in _order)
            {
                if (ActiveState(w, now, out _, out var left) && left < least)
                {
                    least = left;
                    _forcedLine = w.Index;
                }
            }

            if (_forcedLine >= 0)
            {
                _margin = least + 1;
                forcedNote = string.Create(CultureInfo.InvariantCulture, $" (forced: {_order[_forcedLine].Line.Name} has the least left, {least:0.#} s)");
            }
            else
            {
                _margin = 1;
                forcedNote = " (forced: no line is active, so every line is due)";
            }
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{(rebuff ? (forced ? "forced re-buff pass" : "re-buff pass") : "cycle")} starts: {_order.Length} lines, margin {_margin:0.#} s{forcedNote}, {_options}"));
        _phase = Phase.NextLine;
        Stage = StageMana;
        return true;
    }

    /// <summary>Picks the next line to cast; false when the pass ended, failed or waits.</summary>
    private bool BeginLine(double now)
    {
        CollectExpected();
        while (_line < _order.Length)
        {
            var w = _order[_line];
            var index = Choose(w, out var why);
            if (index == int.MinValue)
            {
                return Fail(now, why);
            }

            if (index < 0)
            {
                if (!_upgrading)
                {
                    Miss(w, "no-level", w.Line.Name + ": " + why);
                }

                _line++;
                continue;
            }

            var active = ActiveState(w, now, out var activeIndex, out var remaining);
            bool skip;
            if (_upgrading)
            {
                skip = !(active && activeIndex < index); // an upgrade only raises a line that holds a lower level; a line the pass missed is not tried again
            }
            else if (_forced && _forcedLine >= 0)
            {
                skip = w.Index != _forcedLine || remaining >= _margin;
            }
            else
            {
                skip = _rebuff ? remaining >= _margin : active && activeIndex >= index && remaining > _margin;
            }

            if (skip)
            {
                _line++;
                continue;
            }

            if (w.Infos[index] is null)
            {
                if (_upgrading)
                {
                    _line++;
                    continue;
                }

                Miss(w, "no-spell", w.Line.Name + ": the SpellTable given to the cycle has no spell " + w.Line.Spells[index].Id.ToString(CultureInfo.InvariantCulture));
                _line++;
                continue;
            }

            if (_upgrading)
            {
                _upgraded++;
                _log.Info(LogCategory, $"upgrade: {w.Line.Name} holds {w.Line.Spells[activeIndex].Name} and the rule now gives {w.Infos[index]!.Name} (D10: the highest level the current skill gives)");
            }

            _castIndex = index;
            _wasActive = active;
            _lineFizzles = 0;
            _manaCasts = 0;
            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        if (!_rebuff && !_upgrading)
        {
            // The ordered pass is done and the skills are higher than they were when its first lines were chosen: each line below the level the rule gives now is cast once more at that level (D10, not a retry).
            _upgrading = true;
            _line = 0;
            _log.Info(LogCategory, "the ordered pass is done: the upgrade pass looks at every line once");
            return true;
        }

        return Finish(now);
    }

    /// <summary>
    /// The highest level of <paramref name="w"/> whose chance from the current skill is at least the threshold: its index, -1 with the reason when none reaches it, or <see cref="int.MinValue"/> when the skill cannot be
    /// read (the cycle fails).
    /// </summary>
    private int Choose(WantedLine w, out string why)
    {
        if (!GearCasts.TryCurrentSkill(_state.Self.Qualities, GearCasts.SkillOf(w.Line.School), out var skill, out why))
        {
            why = w.Line.Name + ": " + why;
            return int.MinValue;
        }

        for (var i = w.Line.Spells.Count - 1; i >= 0; i--)
        {
            if (GearCasts.Chance(skill, w.Line.Spells[i].Power) >= _options.CastChance)
            {
                why = string.Empty;
                return i;
            }
        }

        why = string.Create(CultureInfo.InvariantCulture, $"no level reaches a chance of {_options.CastChance} at {w.Line.School} skill {skill}");
        return -1;
    }

    // ------------------------------------------------------------------------------------------------ the mana loop

    /// <summary>The mana step: cast the buff when the mana is there, else cast a transfer or a boost, else wait for regeneration.</summary>
    private bool ManaStep(double now)
    {
        var w = _order[_line];
        var info = w.Infos[_castIndex]!;
        var q = _state.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max))
        {
            return Fail(now, "the read model has no mana vital");
        }

        NoteMana(mana);
        var need = GearCasts.ManaCeiling(info, _state.Self.Inventory.Equipped.Count);
        if (mana >= need)
        {
            if (_phase == Phase.ManaWait)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait over after {now - _waitStart:0.#} s: {mana} of {max}, {need} needed for {info.Name}"));
            }

            _waitStart = double.NaN;
            if (_awayFromMagic)
            {
                return BeginMode(CombatMode.Magic, Phase.ModeBack);
            }

            SendBuff(w, info, need);
            return false;
        }

        if (max < need)
        {
            return Fail(now, string.Create(CultureInfo.InvariantCulture, $"{info.Name} costs up to {need} mana and the maximum is {max} (current {mana})"));
        }

        if (_phase == Phase.ManaWait)
        {
            if (now - _waitStart > _options.ManaWaitSeconds)
            {
                return Fail(now, string.Create(CultureInfo.InvariantCulture, $"the mana wait ran out: {mana} of {max} after {_options.ManaWaitSeconds:0.#} s, {need} needed for {info.Name} and neither Stamina to Mana nor Revitalize could be cast"));
            }

            if (now < _nextEvaluate)
            {
                return false;
            }
        }

        if (_manaCasts >= ManaCastLimitPerLine)
        {
            return Fail(now, string.Create(CultureInfo.InvariantCulture, $"{ManaCastLimitPerLine} mana casts did not bring the mana to the {need} {info.Name} needs ({mana} of {max})"));
        }

        var action = Decide(mana, max, need, out var index, out var failure);
        switch (action)
        {
            case ManaAction.Fail:
                return Fail(now, failure);
            case ManaAction.Wait:
                if (_phase != Phase.ManaWait)
                {
                    _phase = Phase.ManaWait;
                    Stage = StageManaWait;
                    _waitStart = now;
                    q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait: {mana} of {max}, stamina {stamina} of {staminaMax}, {need} needed for {info.Name}; neither Stamina to Mana nor Revitalize can be cast usefully; at most {_options.ManaWaitSeconds:0.#} s; NonCombat after {NonCombatAfterSeconds:0.#} s of it (ACE halves stamina and health regeneration in a combat mode, Creature_Vitals.cs GetStanceMod)"));
                }
                else if (!_awayFromMagic && now - _waitStart >= NonCombatAfterSeconds)
                {
                    // A wait this long is worth two stance changes: the stamina the transfer needs regenerates twice as fast out of a combat mode.
                    _nextEvaluate = now + ReevaluateSeconds;
                    return BeginMode(CombatMode.NonCombat, Phase.ModeAway);
                }

                _nextEvaluate = now + ReevaluateSeconds;
                return false;
            default:
                if (_phase == Phase.ManaWait)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait over after {now - _waitStart:0.#} s: {mana} of {max}"));
                    _waitStart = double.NaN;
                }

                if (_awayFromMagic)
                {
                    return BeginMode(CombatMode.Magic, Phase.ModeBack); // the next step decides again, in the magic mode
                }

                SendMana(action == ManaAction.Transfer ? ManaKind.Transfer : ManaKind.Boost, index, mana, max, need);
                return false;
        }
    }

    /// <summary>
    /// What to cast to reach <paramref name="need"/> mana (D24). A transfer if one is castable (a level that passes the chance and costs no more than the mana) and its yield (<see cref="BuffStatMods.Transfer"/>, ACE's
    /// <c>HandleCastSpell_Transfer</c>) covers the shortfall, or helps and the stamina cannot be raised; else Revitalize when the stamina is not full; else wait.
    /// </summary>
    private ManaAction Decide(uint mana, uint max, uint need, out int index, out string failure)
    {
        index = -1;
        failure = string.Empty;
        var q = _state.Self.Qualities;
        uint skill;
        if (_phase == Phase.ManaWait)
        {
            // The wait looks again every second and allocates nothing: GearCasts.TryCurrentSkill builds lists while the registry holds enchantments, so the wait reuses the skill its first look read (nothing is cast during
            // a wait, so nothing changes it but an expiry, which the next cast's own choice sees).
            skill = _lifeSkill;
        }
        else if (GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out skill, out var why))
        {
            _lifeSkill = skill;
        }
        else
        {
            failure = "the mana lines need Life Magic: " + why;
            return ManaAction.Fail;
        }

        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var transfer = PickMana(_transferLine, _transferInfos, skill, mana);
        var boost = PickMana(_boostLine, _boostInfos, skill, mana);
        if (transfer >= 0)
        {
            var info = _transferInfos[transfer]!;
            var row = _transferRows[transfer];
            if (row is not { } r)
            {
                failure = "BuffStatMods has no transfer row for spell " + info.Id.ToString(CultureInfo.InvariantCulture) + " (TODO(m3-buff-transfer-row))";
                return ManaAction.Fail;
            }

            var cost = GearCasts.ManaCeiling(info, 0);
            var after = mana - cost;
            BuffStatMods.Transfer(r, stamina, max - after, out _, out var given);
            var covered = after + given >= need;
            if (given > cost && (covered || boost < 0 || stamina >= staminaMax))
            {
                index = transfer;
                return ManaAction.Transfer;
            }
        }

        if (boost >= 0 && stamina < staminaMax)
        {
            index = boost;
            return ManaAction.Boost;
        }

        return ManaAction.Wait;
    }

    /// <summary>The highest level of a mana line whose chance reaches the threshold and whose cost is at most <paramref name="mana"/>, or -1.</summary>
    private int PickMana(ProfileLine line, SpellInfo?[] infos, uint skill, uint mana)
    {
        for (var i = line.Spells.Count - 1; i >= 0; i--)
        {
            if (infos[i] is { } info && GearCasts.Chance(skill, line.Spells[i].Power) >= _options.CastChance && GearCasts.ManaCeiling(info, 0) <= mana)
            {
                return i;
            }
        }

        return -1;
    }

    private void SendMana(ManaKind kind, int index, uint mana, uint max, uint need)
    {
        var line = kind == ManaKind.Transfer ? _transferLine : _boostLine;
        var info = (kind == ManaKind.Transfer ? _transferInfos : _boostInfos)[index]!;
        _manaKind = kind;
        _need = need;
        _chatMark = _state.Chat.Total;
        _manaCasts++;
        if (kind == ManaKind.Transfer)
        {
            _tried++;
        }

        _pending = _actions.Cast(info.Id);
        _phase = Phase.ManaCasting;
        Stage = StageManaCasting;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: {info.Name} ({line.Spells[index].Id}), mana {mana} of {max}, {need} needed for {_order[_line].Line.Name}"));
    }

    /// <summary>The outcome of a mana cast: a fire (UseDone without a fizzle) or a fizzle counts; anything else fails the cycle.</summary>
    private bool ManaOutcome(double now)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return false;
        }

        _pending = null;
        var kind = _manaKind;
        if (_state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _))
        {
            NoteMana(mana);
        }

        if (outcome.Succeeded)
        {
            if (kind == ManaKind.Transfer)
            {
                _fired++;
                _transferStreak = 0;
            }
            else
            {
                _revitalizes++;
                _boostStreak = 0;
            }
        }
        else if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == GearCasts.FizzleCode)
        {
            _fizzles++;
            var streak = kind == ManaKind.Transfer ? ++_transferStreak : ++_boostStreak;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: {(kind == ManaKind.Transfer ? "Stamina to Mana" : "Revitalize")} fizzled (try {streak} of {_options.TransferTries}; it costs 5 mana, Player_Magic.cs 566-572)"));
            if (streak >= _options.TransferTries)
            {
                return Fail(now, string.Create(CultureInfo.InvariantCulture, $"the mana loop: {streak} {(kind == ManaKind.Transfer ? "Stamina to Mana" : "Revitalize")} casts in a row fizzled (--buff-transfer-tries {_options.TransferTries})"));
            }
        }
        else
        {
            return Fail(now, "the mana loop: a " + (kind == ManaKind.Transfer ? "Stamina to Mana" : "Revitalize") + " cast ended: " + GearCasts.Explain(outcome, _state, _chatMark, _need));
        }

        _phase = Phase.Mana;
        Stage = StageMana;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ the combat mode

    private bool BeginMode(CombatMode mode, Phase phase)
    {
        _pending = _actions.SetCombatMode(mode, ActionSource.Host);
        _phase = phase;
        Stage = StageMode;
        _log.Info(LogCategory, $"combat mode to {mode} (awaited)");
        return false;
    }

    /// <summary>A combat-mode change ended: to NonCombat the regeneration wait begins, back to Magic the mana step decides again; a change that did not happen fails the cycle (no retry).</summary>
    private bool ModeOutcome(double now)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return false;
        }

        _pending = null;
        var toMagic = _phase == Phase.ModeBack;
        if (!outcome.Succeeded)
        {
            return Fail(now, "the combat mode change to " + (toMagic ? "Magic" : "NonCombat") + " did not happen: " + outcome.Kind + " " + outcome.Detail);
        }

        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            _modeChanges++;
        }

        if (toMagic)
        {
            _awayFromMagic = false;
            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        _awayFromMagic = true;
        _phase = Phase.ManaWait;
        Stage = StageManaWait;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ the buff casts

    private void SendBuff(WantedLine w, SpellInfo info, uint need)
    {
        _need = need;
        _chatMark = _state.Chat.Total;
        var atCaster = w.Armour && GearCasts.FormOf(info) == CastForm.AtItem;
        _pending = atCaster ? _actions.Cast(info.Id, _state.Self.Guid) : _actions.Cast(info.Id);
        _phase = Phase.Casting;
        Stage = StageCasting;
        var q = _state.Self.Qualities;
        GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(w.Line.School), out var skill, out _);
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"cast {info.Name} ({info.Id}) {(atCaster ? "at the caster (every worn piece, D23)" : "on the caster")}: {w.Line.School} skill {skill}, chance {GearCasts.Chance(skill, info.Power):0.0000}, up to {need} mana"));
    }

    private bool CastOutcome(double now)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return false;
        }

        var handle = _pending;
        _pending = null;
        var w = _order[_line];
        var info = w.Infos[_castIndex]!;
        if (_state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _))
        {
            NoteMana(mana);
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == GearCasts.FizzleCode)
        {
            _fizzles++;
            if (_lineFizzles == 0 && _castIndex > 0 && !_upgrading)
            {
                _lineFizzles = 1;
                var lower = w.Infos[_castIndex - 1];
                _log.Info(LogCategory, $"{info.Name} fizzled; one retry, one level down ({lower?.Name ?? "(no spell)"})");
                _castIndex--;
                if (lower is null)
                {
                    Miss(w, "fizzled", w.Line.Name + ": " + info.Name + " fizzled and the level below is not in the SpellTable");
                    return NextLine();
                }

                _phase = Phase.Mana;
                Stage = StageMana;
                return true;
            }

            if (_upgrading)
            {
                Miss(w, "upgrade-fizzled", w.Line.Name + ": the upgrade to " + info.Name + " fizzled; the line stays at the level it held (an upgrade is cast once, no retry)");
                return NextLine();
            }

            Miss(w, "fizzled", w.Line.Name + ": " + (_lineFizzles == 0 ? info.Name + " fizzled and there is no level below" : "fizzled twice, the second time at " + info.Name) + " (no third try)");
            return NextLine();
        }

        if (outcome.Kind is ActionOutcomeKind.TimedOut or ActionOutcomeKind.Refused or ActionOutcomeKind.Cancelled or ActionOutcomeKind.DryRun or ActionOutcomeKind.NotImplemented)
        {
            return Fail(now, w.Line.Name + ": the cast was not carried out: " + GearCasts.Explain(outcome, _state, _chatMark, _need));
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError)
        {
            Miss(w, "error-0x" + outcome.Code.ToString("X4", CultureInfo.InvariantCulture), w.Line.Name + ": " + GearCasts.Explain(outcome, _state, _chatMark, _need));
            return NextLine();
        }

        if (w.Armour)
        {
            ArmourLanded(w, info, handle?.CompletedAt ?? now, now);
            return NextLine();
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            Miss(w, "no-enchantment", w.Line.Name + ": the cast ended (" + outcome.Kind + ") with no enchantment on the player");
            return NextLine();
        }

        Landed(w, info);
        return NextLine();
    }

    private void Landed(WantedLine w, SpellInfo info)
    {
        _passCast++;
        if (w.Armour)
        {
            _passArmour++;
        }

        if (_rebuff && _wasActive)
        {
            _passRecast++;
        }

        _log.Info(LogCategory, $"cast {w.Line.Name}: {info.Name} landed ({_passCast} this pass)");
    }

    /// <summary>
    /// An armour cast at the caster ended with UseDone 0 (Sent): ACE says "You cast &lt;spell&gt; on &lt;item&gt;" once per piece it enchanted (<c>WorldObject_Magic.cs</c> 430-452), so the pieces come from the chat lines
    /// that arrived during the cast; each gets the UseDone time plus the SpellTable duration as its expiry. A worn armour piece with no line is a named miss.
    /// </summary>
    private void ArmourLanded(WantedLine w, SpellInfo info, double doneAt, double now)
    {
        var chat = _state.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - _chatMark);
        var count = CollectExpected();
        var prefix = "You cast " + info.Name + " on ";
        w.CastIndex = _castIndex;
        w.PieceCount = 0;
        string? missing = null;
        var missingCount = 0;
        for (var p = 0; p < count; p++)
        {
            var found = false;
            var surpassed = false;
            var line = prefix + _expectedName[p];
            for (var i = chat.Count - fresh; i < chat.Count && !found; i++)
            {
                var text = chat[i].Text;
                if (text.StartsWith(line, StringComparison.Ordinal) && (text.Length == line.Length || text[line.Length] == ','))
                {
                    found = true;
                    surpassed = text.Contains("but it is surpassed by", StringComparison.Ordinal);
                }
            }

            if (found && surpassed)
            {
                // The piece already holds a stronger enchantment of the line (ACE: "You cast <spell> on <item>, but it is surpassed by ..."); retail keeps the stronger one, so the line is active for this piece.
                // Its own expiry is not known to the client; the cast's is the conservative stand-in. Closes TODO(m3-buff-armour-surpassed).
                _passSurpassed++;
                _log.Info(LogCategory, $"{w.Line.Name}: {_expectedName[p]} holds a stronger version of the line (the cast was surpassed); counted active");
            }

            if (found)
            {
                w.PieceGuid[w.PieceCount] = _expectedGuid[p];
                w.PieceExpiry[w.PieceCount] = doneAt + info.Duration;
                w.PieceCount++;
            }
            else
            {
                missingCount++;
                missing = (missing is null ? string.Empty : missing + ";") + _expectedName[p].Replace(' ', '_');
            }
        }

        if (count == 0)
        {
            Miss(w, "no-worn-piece", w.Line.Name + ": no worn armour or clothing piece is known to the read model, so the cast at the caster had nothing to enchant");
        }
        else if (missingCount > 0)
        {
            Miss(w, "no-piece:" + missing, string.Create(CultureInfo.InvariantCulture, $"{w.Line.Name}: {missingCount} of {count} worn pieces got no \"You cast {info.Name} on <item>\" line during the cast ({missing})"));
        }
        else
        {
            Landed(w, info);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{w.Line.Name}: {w.PieceCount} pieces enchanted, each ends {info.Duration:0} s after the cast's UseDone (at {doneAt + info.Duration:0.#} on the session clock; now {now:0.#})"));
        }
    }

    private bool NextLine()
    {
        _line++;
        _phase = Phase.NextLine;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ what is active

    /// <summary>
    /// Whether <paramref name="w"/> has an active buff at <paramref name="now"/>: its level's index and the seconds left on it. A self line reads the registry (the entry of the highest level of the line that has not
    /// expired: its start plus its duration, <see cref="ActiveEnchantment.RemainingSeconds"/>); an armour line reads the cycle's memory (the cast's UseDone time plus the duration, per worn piece: every worn piece must
    /// hold an unexpired one, the least time left counts). <paramref name="remaining"/> is 0 when it is not active.
    /// </summary>
    private bool ActiveState(WantedLine w, double now, out int index, out double remaining)
    {
        index = -1;
        remaining = 0;
        if (w.Armour)
        {
            if (w.CastIndex < 0 || _expectedCount == 0)
            {
                return false;
            }

            var least = double.PositiveInfinity;
            for (var p = 0; p < _expectedCount; p++)
            {
                var left = -1.0;
                for (var m = 0; m < w.PieceCount; m++)
                {
                    if (w.PieceGuid[m] == _expectedGuid[p])
                    {
                        left = w.PieceExpiry[m] - now;
                        break;
                    }
                }

                if (left <= 0)
                {
                    return false;
                }

                least = Math.Min(least, left);
            }

            index = w.CastIndex;
            remaining = least;
            return true;
        }

        var active = _state.Self.Qualities.ActiveEnchantments;
        for (var i = 0; i < active.Length; i++)
        {
            var e = active[i];
            if (!_spellOf.TryGetValue(e.SpellId, out var at) || at.Line != w.Index)
            {
                continue;
            }

            var left = e.RemainingSeconds(now);
            if (left <= 0)
            {
                continue;
            }

            if (at.Index > index || (at.Index == index && left > remaining))
            {
                index = at.Index;
                remaining = left;
            }
        }

        return index >= 0;
    }

    /// <summary>The worn armour and clothing pieces the read model knows (ACE enchants Clothing and shields, <c>WorldObject_Magic.cs</c> 2096): their guids and names into the scratch arrays.</summary>
    private int CollectExpected()
    {
        var equipped = _state.Self.Inventory.Equipped;
        var count = 0;
        for (var i = 0; i < equipped.Count && count < MaxPieces; i++)
        {
            if (_state.Objects.TryGet(equipped[i].Guid, out var item) && (item.ItemType & ArmourOrClothing) != 0)
            {
                _expectedGuid[count] = equipped[i].Guid;
                _expectedName[count] = item.Name;
                count++;
            }
        }

        _expectedCount = count;
        return count;
    }

    private const uint ArmourOrClothing = 0x2 | 0x4; // ItemType Armor | Clothing

    // ------------------------------------------------------------------------------------------------ the end of a pass

    private bool Finish(double now)
    {
        Stage = StageDone;
        _phase = Phase.Idle;
        Status = BuffStatus.Done;
        Result = Build(now, null);
        _log.Info(LogCategory, "pass done: " + Result.Fields);
        return false;
    }

    private bool Fail(double now, string why)
    {
        Stage = StageFailed;
        _phase = Phase.Idle;
        Status = BuffStatus.Failed;
        _pending = null;
        Result = Build(now, why);
        _log.Warn(LogCategory, "FAILED: " + why + "; " + Result.Fields);
        return false;
    }

    private BuffResult Build(double now, string? failure)
    {
        CollectExpected();
        var levels = new StringBuilder();
        var active = 0;
        var min = double.PositiveInfinity;
        foreach (var w in _order)
        {
            var on = ActiveState(w, now, out var index, out var remaining);
            if (levels.Length > 0)
            {
                levels.Append(',');
            }

            levels.Append(w.Short).Append(':').Append(on ? Roman[Math.Clamp(w.Line.Spells[index].Position - 1, 0, Roman.Length - 1)] : "-");
            if (on)
            {
                active++;
                min = Math.Min(min, remaining);
            }
        }

        var result = new BuffResult
        {
            Wanted = _order.Length,
            Active = active,
            Levels = levels.ToString(),
            RecastWhileActive = _passRecast,
            MinRemainingSeconds = double.IsPositiveInfinity(min) ? double.NaN : min,
            Fizzles = _fizzles,
            TransfersFired = _fired,
            TransfersTried = _tried,
            Revitalizes = _revitalizes,
            ManaMin = _manaMin == uint.MaxValue ? null : _manaMin,
            Misses = [.. _misses],
            MissDetails = [.. _missDetails],
            Forced = _forced,
            Pass = _rebuff ? "rebuff" : "cycle",
            Cast = _passCast,
            ArmourCast = _passArmour,
            Surpassed = _passSurpassed,
            Upgraded = _upgraded,
            CycleSeconds = now - _passStart,
            ModeChanges = _modeChanges,
            Failure = failure,
        };
        return result with { Fields = BuffResult.FormatFields(result) };
    }

    private void Miss(WantedLine w, string token, string detail)
    {
        _misses.Add(w.Short + ":" + token);
        _missDetails.Add(detail);
        _log.Warn(LogCategory, "MISS " + detail);
    }

    private void NoteMana(uint mana)
    {
        if (mana < _manaMin)
        {
            _manaMin = mana;
        }
    }

    private static SpellInfo?[] InfosOf(ProfileLine line, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        var infos = new SpellInfo?[line.Spells.Count];
        for (var i = 0; i < infos.Length; i++)
        {
            infos[i] = spells.GetValueOrDefault(line.Spells[i].Id);
        }

        return infos;
    }
}
