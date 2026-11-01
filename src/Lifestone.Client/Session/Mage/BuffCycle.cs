using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
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

    /// <summary>Mana Potions drunk and confirmed (a fighter's pass; M4 P5a C7).</summary>
    public int ManaPotions { get; init; }

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

    /// <summary>Of <see cref="Cast"/>, the armour lines (Impenetrability, Bludgeon Bane) cast and landed. A second cycle object on the same session casts none that the shared <see cref="ArmourBuffLedger"/> knows (M4 4.1 lane b; closes <c>TODO(m3-buff-armour-memory)</c>).</summary>
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

    /// <summary>The cast policy's counts over this pass's attempts (M5 1 lane K3; <see cref="CastTally"/>, reset at the pass's start): the Mana Conversion tries (K5 fills them). Report-only.</summary>
    public int ManaTries { get; init; }

    /// <summary>The 0x0401 refusals of the pass's casts (<c>mana_refusals</c>; report-only).</summary>
    public int ManaRefusals { get; init; }

    /// <summary>The Mana Conversion tries of the pass that landed (<c>mana_landed</c>; report-only).</summary>
    public int ManaLanded { get; init; }

    /// <summary>The busy answers (0x001D) of the pass's casts (<c>busy</c>; report-only).</summary>
    public int Busy { get; init; }

    /// <summary>The resists the pass's casts met (<c>resists</c>; report-only).</summary>
    public int Resists { get; init; }

    /// <summary>The Sent casts the proof proved (<c>proved</c>; report-only).</summary>
    public int Proved { get; init; }

    /// <summary>The fighter's caster swap around the pass (M4 P5a C1b): <c>swapped=yes|no swap_s=n</c>, set by <see cref="RebuffDriver"/> once the swap is back; null for the mage, who never swaps (its <see cref="Fields"/> are unchanged).</summary>
    public string? SwapFields { get; init; }

    /// <summary>The verdict fields: <c>buffs=a/w levels=... rebuffed=yes|no min_remaining_s=n fizzles=n transfers=f/t revitalizes=n mana_min=n cycle_s=n mode_changes=n misses=... surpassed=n</c> (and <c>forced=yes</c> for a forced pass).</summary>
    public string Fields { get; init; } = string.Empty;

    /// <summary>The cast policy's fields, appended to the end of the cycle's report line (<c>pass done: ...</c>, <c>FAILED: ...</c>): <c>mana_tries=n mana_refusals=n mana_landed=n busy=n resists=n proved=n</c> (<see cref="CastTally.Fields"/>; M5 1 K3, report-only). <see cref="Fields"/> itself is unchanged.</summary>
    public string CastFields => string.Create(CultureInfo.InvariantCulture, $"mana_tries={ManaTries} mana_refusals={ManaRefusals} mana_landed={ManaLanded} busy={Busy} resists={Resists} proved={Proved}{(Dances > 0 ? $" mana_presses={DancePresses} dances={Dances} dances_landed={DancesLanded}" : string.Empty)}");

    /// <summary>The stamina-to-mana dances of the pass (M5 1 K5, D27; report-only, written in <see cref="CastFields"/> as <c>dances=</c> where one ran).</summary>
    public int Dances { get; init; }

    /// <summary>The dances that landed (<c>dances_landed=</c>).</summary>
    public int DancesLanded { get; init; }

    /// <summary>The presses of every dance, each one cast (<c>mana_presses=</c>).</summary>
    public int DancePresses { get; init; }

    internal static string FormatFields(BuffResult r)
    {
        var misses = r.Misses.Count == 0 ? "none" : string.Join(';', r.Misses);
        var min = double.IsNaN(r.MinRemainingSeconds) ? "n/a" : Math.Floor(r.MinRemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
        var mana = r.ManaMin is { } m ? m.ToString(CultureInfo.InvariantCulture) : "n/a";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"buffs={r.Active}/{r.Wanted} levels={r.Levels} rebuffed={(r.Rebuffed ? "yes" : "no")} min_remaining_s={min} fizzles={r.Fizzles} transfers={r.TransfersFired}/{r.TransfersTried} revitalizes={r.Revitalizes}{(r.ManaPotions > 0 ? " mana_potions=" + r.ManaPotions.ToString(CultureInfo.InvariantCulture) : string.Empty)} mana_min={mana} cycle_s={Math.Floor(r.CycleSeconds).ToString("0", CultureInfo.InvariantCulture)} mode_changes={r.ModeChanges} misses={misses}{(r.Forced ? " forced=yes" : string.Empty)} surpassed={r.Surpassed}");
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
/// <see cref="PlayerQualities.ActiveEnchantments"/>); an armour line's is the cast's UseDone time plus the SpellTable duration, kept per worn piece in the
/// <see cref="ArmourBuffLedger"/> (M4 4.1 lane b; closes <c>TODO(m3-buff-armour-memory)</c>): the ledger is the session's, so a new cycle object knows an earlier object's armour casts, a piece taken off loses its lines, and after a login
/// that left the ledger empty the worn pieces are appraised once (<see cref="ArmourLedgerSeeder"/>; the server sends no registry for an item, only an appraisal's enchantment ids, which carry no clock). A line found that way has an unknown time
/// left and is due at once (no saved time: the conservative rule; with the host's store the previous session's seconds left are restored, ACE's clock stopping while the owner is offline). A piece whose line says "surpassed" holds a stronger version of the line and is counted active (retail keeps the stronger enchantment; <see cref="BuffResult.Surpassed"/>, <c>surpassed=</c>; closes <c>TODO(m3-buff-armour-surpassed)</c>).</para>
/// <para><b>The level floor</b> (M5 item 3 w1): a pack that holds a school's focus casts that school on the foci formula, so a level whose components the pack does not hold (a school short of level VI would need a Gold Scarab no stock line carries) is never chosen
/// and never stepped down onto: it is skipped, logged once by name, and the line is reported down with the reason (miss <c>down</c> when the level below it that the pack can pay for is cast, <c>no-level</c> when there is none).</para>
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

    /// <summary>The stage while the worn pieces are appraised to seed an empty armour ledger (M4 4.1 lane b).</summary>
    public const string StageSeed = "seed";

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

    /// <summary>
    /// The attempts a buff line is sent at its ceiling after its Mana Conversion tries were spent, all refused (0x0401); the refusal of the last one fails the cycle (M5 3 m4, <c>m5-buffcycle-ceiling-refusal</c>, review 1-R14). A cast sent with mana at or over the
    /// line's ceiling cannot be short of mana; a refusal then means the read model's mana is higher than the server's, and a resend would repeat for as long as it stays so. One whole attempt at the ceiling (its tries counted) is sent; its
    /// refusal is a named failure. Lifestone's own bound.
    /// </summary>
    public const int CeilingAttemptsPerLine = 1;

    /// <summary>The Revitalize Self casts one pass makes after its pre-pass dance (M5 7, D88; Lifestone's own bound).</summary>
    public const int PassRevitalizeLimit = 6;

    /// <summary>
    /// The mana casts (transfers and Revitalizes) in a row that do not lift the line's highest mana (M4 P5a fix r4): after them the line casts no more of them and waits for regeneration (<c>mana-futile:</c>, logged by
    /// name). A Revitalize spends mana for the stamina the next transfer draws on, so a few in a row are an investment; eight that lift nothing are the round 4 trade. A fighter's profile only (<see cref="IBuffProfile.TransfersAhead"/>): the mage's pool and Mana Conversion make its loop another economy, which round 4 passed. Not retail: Lifestone's own.
    /// </summary>
    public const int FutileManaCasts = 8;

    /// <summary>The lines whose mana casts were stopped as futile (<see cref="FutileManaCasts"/>).</summary>
    public int FutileStops { get; private set; }

    /// <summary>
    /// The most Stamina to Mana casts a fighter's pass sends when it holds no Mana Potion (M4 P5a C7; the vitals keeper's <see cref="Vitals.VitalsKeeper.FighterTransferCap"/>), and only at a stamina of <see cref="Vitals.VitalsOptions.DefaultStaminaUntilPercent"/> or more.
    /// A fighter's Revitalize costs mana itself and the transfer gives back less than the pair took, so the fighter's pass drinks Mana Potions instead of chaining them and never casts Revitalize for the sake of mana. Not retail: Lifestone's own bound, named in the log when it stops a cast.
    /// </summary>
    public const int FighterTransferCap = Vitals.VitalsKeeper.FighterTransferCap;

    /// <summary>How long the cycle waits for the mana to change after a confirmed Mana Potion before it sets potions aside and names it.</summary>
    public const double PotionVitalWaitSeconds = Vitals.VitalsKeeper.PotionVitalWaitSeconds;

    /// <summary>Mana Potions this cycle drank and confirmed (a fighter's profile only; M4 P5a C7).</summary>
    public int ManaPotions => _manaPotions;

    private const int MaxPieces = 32;
    private const int MaxWorn = 64;

    private static readonly string[] Roman = LevelFloor.Roman;

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
        ["Heavy Weapon Mastery Self"] = "HeavyM",
        ["Missile Weapon Mastery Self"] = "MissileM",
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
        ["Spirit Drinker"] = "Spirit",
    };

    private enum Phase : byte
    {
        Idle,
        NextLine,
        Mana,
        ManaWait,
        Casting,
        Seed,
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
        Potion,
    }

    private sealed class WantedLine(int index, ProfileLine line, string shortName, SpellInfo?[] infos, bool armour)
    {
        public int Index { get; } = index;

        /// <summary>The line's spell ids, lowest level first: the <see cref="CastRequest.Levels"/> of its casts (built once).</summary>
        public uint[] Ids { get; } = IdsOf(line);

        /// <summary>The purpose of the line's casts in the cast log (built once).</summary>
        public string Purpose { get; } = "buff " + shortName;

        public ProfileLine Line { get; } = line;

        public string Short { get; } = shortName;

        public SpellInfo?[] Infos { get; } = infos;

        public bool Armour { get; } = armour;

    }

    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly BuffOptions _options;
    private readonly ILogSink _log;
    private readonly WantedLine[] _order;
    private readonly sbyte[] _byPlayNoted; // M5 7 P7j: per line learned by play, what was last logged of it (0 nothing, -1 not in the spellbook, 1 in it)
    private int _onlyNext = -1;
    private int _only = -1;
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
    private readonly uint[] _wornGuid = new uint[MaxWorn];
    private readonly ArmourBuffLedger _ledger;
    private readonly ArmourLedgerSeeder? _seeder;
    private readonly CastAttempt _cast;
    private readonly CastTally _tally = new();
    private readonly uint[] _transferIds;
    private readonly uint[] _boostIds;
    private readonly CastRules _manaRules;
    private readonly CastRules _lineTriesRules;
    private readonly CastRules _upgradeTriesRules;
    private readonly CastRules _danceRules;
    private readonly CastChatProof _danceProof;
    private readonly Vitals.DanceLevel[] _danceLevels;
    private readonly int[] _danceIndex;
    private uint _manaConversion;
    private readonly Func<uint, bool> _payable;
    private readonly HashSet<uint> _floorLogged = [];
    private bool _floorOn;
    private readonly bool[] _transferPayable;
    private readonly bool[] _boostPayable;
    private bool _payableKnown;
    private bool _noPayableDanceNoted;
    private bool _lineTriesSpent;
    private int _ceilingAttempts;
    private bool _dancing;
    private bool _danceOff;
    private bool _passDanceDue; // M5 7: the pass's first buff has not been funded yet; a mana under its cost dances before it (and Revitalizes after). A start's pass re-arms it at every line (P7c, the reserve)
    private bool _passDancing; // the dance in flight is the pre-pass one
    private bool _reserveDancing; // P7c: the dance in flight is a start pass's reserve dance (a later line's), not the pre-pass one
    private uint _passReserve; // P7c: the mana a start pass keeps for the next Stamina to Mana press (the dearest payable level's base; 0 when none or not a start's pass)
    private int _reserveDances;
    private bool _passLooked; // P7c: the pass's first look (the pre-pass dance's) is done
    private uint _passDanceMana;
    private string _passDanceFor = string.Empty;
    private bool _reviveDue;
    private bool _passRevive;
    private int _passRevives;
    private int _dances;
    private int _dancesLanded;
    private int _dancePresses;
    private Phase _phase = Phase.Idle;
    private bool _rebuff;
    private bool _forced;
    private double _margin;
    private int _line;
    private int _castIndex;
    private int _spentFizzles;
    private int _spentDown;
    private int _manaCasts;
    private int _castsSincePeak;
    private uint _linePeak;
    private bool _manaFutile;
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
    private int _manaPotions;
    private int _fighterTransfers;
    private bool _potionsOff;
    private bool _potionWaiting;
    private uint _potionBefore;
    private double _potionWaitUntil;
    private bool _capNoted;
    private readonly List<uint> _potionScratch = [];
    private uint _manaMin = uint.MaxValue;
    private int _passRecast;
    private bool _upgrading;
    private int _upgraded;
    private int _forcedLine = -1;
    private bool _awayFromMagic;
    private int _modeChanges;
    private double _passStart;
    private int _expectedCount;
    private bool _noArmourLogged;
    private uint _lifeSkill;

    /// <summary>The buff cycle over a session's Action API and read model.</summary>
    /// <param name="session">The session (its <see cref="IGameSessionView.Actions"/> and <see cref="IGameSessionView.State"/>).</param>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): costs, forms and durations.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the cycle logs.</param>
    /// <param name="ledger">The armour lines' memory of the session (M4 4.1 lane b; <see cref="ArmourBuffLedger.For"/>): shared by every cycle object on the session, and seeded by an appraisal of the worn pieces when it is empty after a login. Null: the cycle keeps a private ledger and seeds nothing.</param>
    /// <param name="store">Where the ledger's remaining seconds are kept across a log-off, or null (the conservative rule); read once, when the ledger is seeded.</param>
    /// <param name="profile">The character's buff profile (M4 P5a): the lines and the order they are cast in; null is the mage's <see cref="ArenaProfile.Buffs"/>.</param>
    public BuffCycle(IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log, ArmourBuffLedger? ledger = null, IArmourLedgerStore? store = null, IBuffProfile? profile = null)
        : this((session ?? throw new ArgumentNullException(nameof(session))).Actions, session.State, spells, options, log, ledger, store, profile)
    {
    }

    /// <summary>The buff cycle over an Action API and a read model.</summary>
    /// <param name="actions">The Action API the casts go through (<see cref="IActionApi.Cast(uint, ActionSource)"/>).</param>
    /// <param name="state">The read model (the player's qualities, inventory and chat).</param>
    /// <param name="spells">The SpellTable's spells: costs, forms and durations.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the cycle logs.</param>
    /// <param name="ledger">The armour lines' memory of the session (see the other constructor), or null for a private ledger that seeds nothing.</param>
    /// <param name="store">Where the ledger's remaining seconds are kept across a log-off, or null.</param>
    /// <param name="profile">The character's buff profile (M4 P5a): the lines and the order they are cast in; null is the mage's <see cref="ArenaProfile.Buffs"/>.</param>
    public BuffCycle(IActionApi actions, GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log, ArmourBuffLedger? ledger = null, IArmourLedgerStore? store = null, IBuffProfile? profile = null)
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
        _ledger = ledger ?? new ArmourBuffLedger();
        Profile = profile ?? ArenaProfile.Buffs;

        var lines = Profile.BuffOrder;
        _order = new WantedLine[lines.Count];
        _byPlayNoted = new sbyte[lines.Count];
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

        _transferLine = Profile.ManaTransferLine;
        _boostLine = Profile.StaminaRestoreLine;
        if (ledger is not null)
        {
            var armour = new List<ArmourLineSpells>();
            foreach (var w in _order)
            {
                if (w.Armour)
                {
                    armour.Add(new ArmourLineSpells(w.Line.Name, w.Infos));
                }
            }

            if (armour.Count > 0)
            {
                _seeder = new ArmourLedgerSeeder(actions, state, spells, armour, ledger, log, store); // a profile with no armour line (the fighters') has no armour memory to seed
            }
        }

        _transferIds = IdsOf(_transferLine);
        _boostIds = IdsOf(_boostLine);
        _manaRules = CastRules.Utility(Math.Max(1, options.TransferTries)); // the mana loop's rule: the 8th Stamina to Mana (or Revitalize) fizzle in a row fails the pass; made once
        _cast = new CastAttempt(actions, state, log, spells);
        _danceProof = new CastChatProof(state, spells); // M5 1 fix B (G5): a landed press of the dance is proved by ACE's transfer line ("You cast ... on yourself and lose ...")
        _transferInfos = InfosOf(_transferLine, spells);
        _boostInfos = InfosOf(_boostLine, spells);
        _transferRows = new TransferRow?[_transferInfos.Length];
        for (var i = 0; i < _transferRows.Length; i++)
        {
            _transferRows[i] = _transferInfos[i] is { } info ? BuffStatMods.TransferOf(info.Id) : null; // looked up once: the lookup allocates
        }

        // M5 1 K5 (D15, D27): the buff lines' Mana Conversion tries and the dance, made once (the forms build a name string).
        var mana = options.Mana;
        _lineTriesRules = mana.On ? CastRules.BuffLineD14.WithManaTries(mana.Tries, mana.TrySpacingSeconds) : CastRules.BuffLineD14;
        _upgradeTriesRules = mana.On ? CastRules.Once.WithManaTries(mana.Tries, mana.TrySpacingSeconds) : CastRules.Once;
        _danceRules = CastRules.ManaDance(mana.DancePresses, mana.TrySpacingSeconds);
        _danceLevels = new Vitals.DanceLevel[_transferInfos.Length];
        _danceIndex = new int[_transferInfos.Length];
        _transferPayable = new bool[_transferInfos.Length];
        _boostPayable = new bool[_boostInfos.Length];
        _payable = PayableBelow; // made once: a lambda per request allocates
    }

    /// <summary>The armour lines' memory this cycle reads and writes (a private one when none was passed in).</summary>
    public ArmourBuffLedger Ledger => _ledger;

    /// <summary>The buff profile this cycle casts (the character's lines and their order).</summary>
    public IBuffProfile Profile { get; }

    /// <summary>The mage's buff lines in the order the cycle casts them: Focus, Willpower and Creature Enchantment Mastery first, then the other Mastery lines, then the rest in the profile's order (the owner's dependency order; the spell bars use it too). Another profile's order is <see cref="IBuffProfile.BuffOrder"/>.</summary>
    public static IReadOnlyList<ProfileLine> OrderedLines => ArenaProfile.Buffs.BuffOrder;

    /// <summary>Where the cycle is (one of the <c>Stage...</c> constants).</summary>
    public string Stage { get; private set; } = StageIdle;

    /// <summary>The status of the pass.</summary>
    public BuffStatus Status { get; private set; } = BuffStatus.Done;

    /// <summary>What the last pass left; null until a pass ended (or failed) and while the next runs.</summary>
    public BuffResult? Result { get; private set; }

    /// <summary>The number of lines the cycle wants (the profile's buff lines; a line learned by play only once the spellbook holds a level of it, M5 7 P7j).</summary>
    public int Wanted
    {
        get
        {
            var n = 0;
            foreach (var w in _order)
            {
                n += Wants(w) ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>The wanted lines' names in the order the cycle casts them.</summary>
    public IReadOnlyList<string> LineOrder
    {
        get
        {
            var names = new List<string>(_order.Length);
            foreach (var w in _order)
            {
                if (Wants(w))
                {
                    names.Add(w.Line.Name);
                }
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
    /// <para>The caller is <see cref="RebuffDriver"/> (M4 4.1 lane a; closes <c>TODO(m3-buff-rebuff-driver)</c>, review 2.1-R10): while <c>--buff-auto on</c> it asks <see cref="RebuffDue"/> every <c>--buff-auto-check-s</c> and, when a line is due and no guard holds,
    /// starts <c>StartRebuff(forced: false)</c> and steps this cycle to the pass's end. It never calls the forced pass; that stays the buff scenario's and the check's.</para>
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="forced">True for the forced pass (an explicit parameter, never a default).</param>
    /// <returns>False when a pass is already running.</returns>
    public bool StartRebuff(double now, bool forced) => Begin(now, rebuff: true, forced);

    /// <summary>
    /// M5 7 F24: a re-buff pass over ONE line (<paramref name="lineName"/>) when it has less than the cycle's margin left (a line with no active buff is due). The fight's refresh casts the due lines one at a time, in the priority order it chooses,
    /// so a pass stays short and the fight decides again after each cast. False when a pass is running or the line is not one of the wanted lines.
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="lineName">The profile line's name.</param>
    public bool StartRebuffLine(double now, string lineName)
    {
        var at = -1;
        for (var i = 0; i < _order.Length && at < 0; i++)
        {
            at = string.Equals(_order[i].Line.Name, lineName, StringComparison.Ordinal) ? i : -1;
        }

        if (at < 0 || Status == BuffStatus.Running || !Wants(_order[at]))
        {
            return false;
        }

        _onlyNext = at;
        var started = Begin(now, rebuff: true, forced: false);
        _onlyNext = -1;
        return started;
    }

    /// <summary>One wanted line as <see cref="ReadLines"/> reads it: the profile line, whether a buff of it is active, and the seconds left on it (0 when none is).</summary>
    /// <param name="Line">The profile line.</param>
    /// <param name="Active">True when a buff of the line is active.</param>
    /// <param name="Remaining">The seconds left; the least over the worn pieces for an armour line.</param>
    public readonly record struct LineState(ProfileLine Line, bool Active, double Remaining);

    /// <summary>M5 7 F24: fills <paramref name="into"/> (cleared first) with every wanted line's state at <paramref name="now"/>; an armour line is left out while nothing is worn (as <see cref="RebuffDue"/>). Allocates nothing once the list has grown.</summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="into">The list to fill.</param>
    public void ReadLines(double now, List<LineState> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var worn = CollectExpected();
        foreach (var w in _order)
        {
            if ((w.Armour && worn == 0) || !Wants(w))
            {
                continue; // M5 7 P7j: a line learned by play is left out until the spellbook holds it
            }

            var active = ActiveState(w, now, out _, out var remaining);
            into.Add(new LineState(w.Line, active, active ? remaining : 0));
        }
    }

    /// <summary>The names of the wanted lines whose remaining time at <paramref name="now"/> is under <paramref name="margin"/> (a line with no active buff has none left: it is due). <see cref="RebuffDriver"/> calls this at each of its checks (closes <c>TODO(m3-buff-rebuff-driver)</c>).</summary>
    /// <param name="now">The session clock's time.</param>
    /// <param name="margin">The margin in seconds (<see cref="double.PositiveInfinity"/> lists every line).</param>
    public IReadOnlyList<string> RebuffDue(double now, double margin)
    {
        var due = new List<string>();
        var worn = CollectExpected();
        foreach (var w in _order)
        {
            if (w.Armour && worn == 0)
            {
                NoteNoArmour();
                continue; // nothing is worn to enchant: an armour line is not wanted (review R4)
            }

            if (!Wants(w))
            {
                continue; // M5 7 P7j: a line learned by play is not due while the spellbook lacks it (logged once by Wants)
            }

            ActiveState(w, now, out _, out var remaining);
            if (remaining < margin)
            {
                due.Add(w.Line.Name);
            }
        }

        return due;
    }

    /// <summary>True when the armour ledger still has to be seeded by the appraisal of the worn pieces and the restore of the saved store (<see cref="StartSeed"/>), so the armour lines cannot be counted due yet (review R2).</summary>
    public bool SeedPending => _seeder is not null && _ledger.NeedsSeed;

    /// <summary>
    /// Starts only the seed: the worn pieces are appraised and the saved seconds restored, with no combat mode change and no cast (review R2). The caller steps <see cref="StepSeed"/> until it is not <see cref="BuffStatus.Running"/>.
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <returns>False when a pass is running or there is nothing to seed.</returns>
    public bool StartSeed(double now)
    {
        if (Status == BuffStatus.Running || !SeedPending)
        {
            return false;
        }

        CollectExpected();
        _seeder!.Start(now, _expectedGuid.AsSpan(0, _expectedCount));
        return true;
    }

    /// <summary>Advances the seed <see cref="StartSeed"/> began; <see cref="BuffStatus.Done"/> when it has finished (the ledger is then seeded).</summary>
    public BuffStatus StepSeed() => _seeder?.Step() ?? BuffStatus.Done;

    /// <summary>Logs once that the armour lines are not wanted while nothing is worn (review R4); the flag is cleared when a piece is worn again.</summary>
    private void NoteNoArmour()
    {
        if (_noArmourLogged)
        {
            return;
        }

        _noArmourLogged = true;
        _log.Info(LogCategory, "no armour or clothing piece is worn, so the armour lines are not wanted (no cast, no re-buff for them) until one is");
    }

    /// <summary>The seconds left on the buff that ends soonest over the wanted lines that are active at <paramref name="now"/>, or NaN when none is.</summary>
    /// <param name="now">The session clock's time.</param>
    public double MinRemainingSeconds(double now)
    {
        CollectExpected();
        var min = double.PositiveInfinity;
        foreach (var w in _order)
        {
            if (Wants(w) && ActiveState(w, now, out _, out var remaining) && remaining < min)
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
                case Phase.Seed:
                    again = _seeder!.Step() == BuffStatus.Running ? false : Launch(now);
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

        _only = _onlyNext; // M5 7 F24: a one-line pass (StartRebuffLine), else -1
        _rebuff = rebuff;
        _forced = forced;
        _margin = rebuff ? _options.RebuffMarginSeconds : _options.StartMarginSeconds ?? _options.RebuffMarginSeconds; // D126 (a): a start's pass has its own margin
        _forcedLine = -1;
        _passRecast = 0;
        _upgrading = false;
        _upgraded = 0;
        _awayFromMagic = false;
        _passStart = now;
        _line = 0;
        _passCast = 0;
        _fighterTransfers = 0;
        _capNoted = false;
        _potionsOff = false;
        _potionWaiting = false;
        _passArmour = 0;
        _passSurpassed = 0;
        _misses.Clear();
        _missDetails.Clear();
        _pending = null;
        _waitStart = double.NaN;
        _transferStreak = 0;
        _boostStreak = 0;
        _spentFizzles = 0;
        _spentDown = 0;
        _tally.Clear();
        _dances = _dancesLanded = _dancePresses = 0;
        _dancing = false;
        _danceOff = false;
        _passDanceDue = true;
        _passDancing = false;
        _reserveDancing = false;
        _passReserve = 0;
        _reserveDances = 0;
        _passLooked = false;
        _reviveDue = false;
        _passRevive = false;
        _passRevives = 0;
        Result = null;
        Status = BuffStatus.Running;
        if (_state.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            return Fail(now, "the player is not in the magic combat mode (ACE ends a cast made in another mode with a bare UseDone): the caller changes the mode before the cycle starts");
        }

        if (_seeder is not null && _ledger.NeedsSeed)
        {
            // M4 4.1 lane b: a login left the ledger empty, so the worn pieces are appraised once before the first line is chosen (the appraisal tells which armour lines are already on).
            CollectExpected();
            _seeder.Start(now, _expectedGuid.AsSpan(0, _expectedCount));
            if (_seeder.Status == BuffStatus.Running)
            {
                _phase = Phase.Seed;
                Stage = StageSeed;
                return true;
            }
        }

        return Launch(now);
    }

    /// <summary>The pass proper, after the armour ledger is seeded (or needs nothing): the forced pass picks its line, the start is logged and the first line is next.</summary>
    private bool Launch(double now)
    {
        var forced = _forced;
        var rebuff = _rebuff;
        var forcedNote = string.Empty;
        if (forced)
        {
            // D25 as narrowed: only the line closest to expiry is recast, by a margin of its remaining time plus one second (one cast proves a recast while the buff is active).
            CollectExpected();
            var least = double.PositiveInfinity;
            foreach (var w in _order)
            {
                if (Wants(w) && ActiveState(w, now, out _, out var left) && left < least)
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

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{(rebuff ? (forced ? "forced re-buff pass" : "re-buff pass") : "cycle")} starts: {Wanted} lines, margin {_margin:0.#} s{forcedNote}, {_options}"));
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
            if (!Wants(w))
            {
                _line++;
                continue; // M5 7 P7j: a line learned by play the spellbook does not hold yet is not wanted: no cast, no miss (logged once by Wants)
            }

            if (_rebuff && w.Armour && _expectedCount == 0)
            {
                NoteNoArmour();
                _line++;
                continue; // a re-buff pass wants no armour line while nothing is worn (review R4); the ordered cycle still records the miss
            }

            var index = Choose(w, out var why);
            if (index == int.MinValue)
            {
                return Fail(now, why);
            }

            if (index >= 0 && why.Length > 0 && !_upgrading)
            {
                // M5 3 w1: the level the rule gave is one the pack cannot pay for; the level below it that the pack can pay for is cast, and the line is reported down with the reason (never a silent substitute).
                Miss(w, "down", w.Line.Name + ": " + why + "; cast " + w.Line.Spells[index].Name + " instead");
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
            else if (_only >= 0)
            {
                skip = w.Index != _only || remaining >= _margin; // M5 7 F24: the one line, when it is under the margin
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
            _spentFizzles = 0;
            _spentDown = 0;
            _manaCasts = 0;
            _castsSincePeak = 0;
            _linePeak = 0;
            _manaFutile = false;
            _lineTriesSpent = false;
            _ceilingAttempts = 0;
            if (!_rebuff)
            {
                _passDanceDue = true; // M5 7 P7c: a start's pass looks again before every line (PassDance's reserve), one look a line
            }
            if (_options.Mana.On)
            {
                Vitals.ManaOrder.TryManaConversion(_state.Self.Qualities, out _manaConversion); // read once a line (it reads enchantments): the try floor and the dance's levels use it
            }

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
    /// M5 7 P7j (D143, D141): whether the cycle wants <paramref name="w"/>: every line the build teaches, and a line learned by play (<see cref="ProfileLine.ByPlay"/>) only while the spellbook (the state, never an assumption) holds a level of
    /// it. The first answer and every change are logged once ("no level is in the spellbook ... not wanted", then "... is in the spellbook now ... wanted"), never each frame. Allocates nothing once logged.
    /// </summary>
    private bool Wants(WantedLine w)
    {
        if (!w.Line.ByPlay)
        {
            return true;
        }

        var book = _state.Self.Spellbook;
        ProfileSpell? known = null;
        if (book.IsKnown)
        {
            foreach (var spell in w.Line.Spells)
            {
                if (book.Knows(spell.Id))
                {
                    known = spell; // the highest held, as the list runs up
                }
            }
        }

        var seen = (sbyte)(known is null ? -1 : 1);
        if (_byPlayNoted[w.Index] != seen)
        {
            _byPlayNoted[w.Index] = seen;
            _log.Info(LogCategory, known is null
                ? $"{w.Line.Name}: no level is in the spellbook{(book.IsKnown ? string.Empty : " (the spellbook is not read yet)")}; a line learned by play (D143), so it is not wanted until a level is learned"
                : $"{w.Line.Name}: {known.Name} ({known.Id}) is in the spellbook now: the line is wanted and kept up as the other auras (D143)");
        }

        return known is not null;
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

        // M5 3 w1: a pack that holds the school's focus casts on the foci formula, whose components it must hold: a level whose components the pack lacks is skipped (logged once, by name), the level below it that the pack can
        // pay for is the next candidate, and the caller reports the line down with the reason (`why` is then the first skip's text and the index is >= 0). No level the pack can pay for: -1 with that reason.
        var held = LevelFloor.HeldFor(_state, w.Line.School);
        _floorOn = held is not null;
        string? skipped = null;
        for (var i = w.Line.Spells.Count - 1; i >= 0; i--)
        {
            if (GearCasts.Chance(skill, w.Line.Spells[i].Power) < _options.CastChance || (w.Line.ByPlay && !_state.Self.Spellbook.Knows(w.Line.Spells[i].Id)))
            {
                continue; // M5 7 P7j: of a line learned by play only the levels the spellbook holds are cast
            }

            if (held is not null && w.Infos[i] is { } info && LevelFloor.ShortOf(held, info, out var need))
            {
                var text = LevelFloor.SkipText(w.Line.Spells[i], need);
                skipped ??= text;
                if (_floorLogged.Add(info.Id))
                {
                    _log.Warn(LogCategory, w.Line.Name + ": " + text);
                }

                continue;
            }

            why = skipped ?? string.Empty;
            return i;
        }

        why = skipped is not null
            ? "no level the pack can pay for (" + skipped + ")"
            : string.Create(CultureInfo.InvariantCulture, $"no level reaches a chance of {_options.CastChance} at {w.Line.School} skill {skill}");
        return -1;
    }

    /// <summary>The cast policy's <see cref="CastRequest.Usable"/> for a level down while the floor applies (D14 steps one level down after two fizzles): a level the SpellTable lacks or whose foci components the pack does not hold is not stepped onto.</summary>
    private bool PayableBelow(uint spellId)
    {
        if (!_spellOf.TryGetValue(spellId, out var at))
        {
            return false;
        }

        var w = _order[at.Line];
        if (w.Infos[at.Index] is not { } info || (w.Line.ByPlay && !_state.Self.Spellbook.Knows(spellId)))
        {
            return false; // M5 7 P7j: a level of a line learned by play that the spellbook lacks is never stepped onto
        }

        return LevelFloor.HeldFor(_state, w.Line.School) is not { } held || !LevelFloor.ShortOf(held, info, out _);
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
        if (_potionWaiting)
        {
            // M4 P5a C7: a Mana Potion was confirmed; the mana is read again once the vital's update has come (bounded, named on failure).
            if (mana != _potionBefore)
            {
                _potionWaiting = false;
            }
            else if (now < _potionWaitUntil)
            {
                return false;
            }
            else
            {
                _potionWaiting = false;
                _potionsOff = true;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana potion: the mana did not change within {PotionVitalWaitSeconds:0} s of a confirmed Mana Potion (still {mana} of {max}); no more Mana Potions this pass"));
            }
        }

        var need = GearCasts.ManaCeiling(info, _state.Self.Inventory.Equipped.Count);
        if (_options.Mana.On)
        {
            return ManaOrderStep(now, w, info, mana, max, need); // M5 1 K5: the mana order (D15, D27); --cast-mana-tries 0 keeps the loop below, exactly as before
        }

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

            if (PreemptTransfer(mana, max, need, out var aheadIndex, out var aheadNeed))
            {
                SendMana(ManaKind.Transfer, aheadIndex, mana, max, aheadNeed, now);
                return false;
            }

            SendBuff(w, info, need, now);
            return false;
        }

        if (max < need)
        {
            return Fail(now, string.Create(CultureInfo.InvariantCulture, $"{info.Name} costs up to {need} mana and the maximum is {max} (current {mana})"));
        }

        if (Profile.TransfersAhead && !_potionsOff && VitalsMeansFind(out var potion, out var potionCount))
        {
            // M4 P5a C7: the fighter's pass drinks Mana Potions for its mana instead of chaining transfers and Revitalizes (round 5: three Revitalizes between two transfers cost 130 mana).
            if (_phase == Phase.ManaWait)
            {
                _waitStart = double.NaN;
            }

            _potionBefore = mana;
            _manaKind = ManaKind.Potion;
            _need = need;
            _pending = _actions.Drink(potion, ActionSource.Automation);
            _phase = Phase.ManaCasting;
            Stage = StageManaCasting;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Mana Potion 0x{potion:X8} ({potionCount} held), mana {mana} of {max}, {need} needed for {_order[_line].Line.Name}"));
            return false;
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

        if (mana > _linePeak)
        {
            _linePeak = mana;
            _castsSincePeak = 0;
        }

        if (!_manaFutile && Profile.TransfersAhead && _castsSincePeak >= FutileManaCasts)
        {
            // M4 P5a fix r4: round 4's Archer, drained, traded stamina and mana back and forth (Stamina to Mana, then Revitalize) for 64 casts with the mana never past 36: casts that do not lift the line's peak stop for the line.
            _manaFutile = true;
            FutileStops++;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana-futile: {_castsSincePeak} mana casts did not lift the mana past {_linePeak} (now {mana} of {max}); no more transfers or Revitalizes for {info.Name}: the mana wait regenerates the {need} (at most {_options.ManaWaitSeconds:0.#} s)"));
        }

        var index = -1;
        var failure = string.Empty;
        var action = _manaFutile ? ManaAction.Wait : Decide(mana, max, need, out index, out failure);
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

                SendMana(action == ManaAction.Transfer ? ManaKind.Transfer : ManaKind.Boost, index, mana, max, need, now);
                return false;
        }
    }

    /// <summary>
    /// The mana step through <see cref="Vitals.ManaOrder"/> (M5 1 K5, D15 and D27): at or over the line's try floor the buff is cast with the Mana Conversion tries (the pass no longer waits for the ceiling); under it the
    /// mage dances (Stamina to Mana Self pressed until it lands), drinks a Mana Potion only when the stamina is too low for a transfer, else rests in peace for stamina; the fighters drink, then dance at their stamina gate
    /// within <see cref="FighterTransferCap"/>, then wait. The wait and the stamina rest are the bounded mana wait (<see cref="BuffOptions.ManaWaitSeconds"/>); the stamina rest goes to NonCombat at once (ACE halves
    /// stamina regeneration in a combat mode), the mana wait after <see cref="NonCombatAfterSeconds"/> as before. Revitalize is not cast for mana in this order. Allocation-free while it waits.
    /// </summary>
    private bool ManaOrderStep(double now, WantedLine w, SpellInfo info, uint mana, uint max, uint need)
    {
        if (_passDanceDue && PassDance(now, info, mana, max, need, out var danced))
        {
            return danced;
        }

        if (_reviveDue && PassRevitalize(now, info, mana, max, need))
        {
            return false;
        }

        var floor = _lineTriesSpent ? need : Vitals.ManaOrder.FloorOf(need, info.Power, need, _manaConversion, _options.Mana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0);
        if (mana >= floor)
        {
            if (_phase == Phase.ManaWait)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait over after {now - _waitStart:0.#} s: {mana} of {max}, {floor} needed for {info.Name}{(floor < need ? $" (its try floor; up to {need})" : string.Empty)}"));
            }

            _waitStart = double.NaN;
            if (_awayFromMagic)
            {
                return BeginMode(CombatMode.Magic, Phase.ModeBack);
            }

            SendBuff(w, info, need, now, floor);
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
                return Fail(now, string.Create(CultureInfo.InvariantCulture, $"the mana wait ran out: {mana} of {max} after {_options.ManaWaitSeconds:0.#} s, {floor} needed for {info.Name} and the mana order had nothing to press or drink"));
            }

            if (now < _nextEvaluate)
            {
                return false;
            }
        }

        if (_manaCasts >= ManaCastLimitPerLine)
        {
            return Fail(now, string.Create(CultureInfo.InvariantCulture, $"{ManaCastLimitPerLine} mana casts did not bring the mana to the {floor} {info.Name} needs ({mana} of {max})"));
        }

        uint skill;
        if (_phase == Phase.ManaWait)
        {
            skill = _lifeSkill; // the wait allocates nothing: GearCasts.TryCurrentSkill builds lists while the registry holds enchantments (as Decide)
        }
        else if (GearCasts.TryCurrentSkill(_state.Self.Qualities, MageTemplate.LifeMagic, out skill, out var why))
        {
            _lifeSkill = skill;
        }
        else
        {
            return Fail(now, "the mana lines need Life Magic: " + why);
        }

        var q = _state.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var count = _danceOff ? 0 : FillDanceLevels(skill);
        uint potion = 0;
        var potionCount = 0;
        var potions = !_potionsOff && VitalsMeansFind(out potion, out potionCount) ? potionCount : 0;
        var input = new Vitals.ManaOrderInput
        {
            Mana = mana,
            ManaMax = max,
            Stamina = stamina,
            StaminaMax = staminaMax,
            ManaConversion = _manaConversion,
            Need = new Vitals.ManaNeed(need, floor),
            TriesSpent = _lineTriesSpent,
            ManaPotions = potions,
            Profile = Profile.TransfersAhead ? Vitals.ManaProfile.Fighter : Vitals.ManaProfile.Mage,
            FighterGatePercent = Vitals.VitalsOptions.DefaultStaminaUntilPercent,
            FighterTransfersLeft = FighterTransferCap - _fighterTransfers,
        };
        var decision = Vitals.ManaOrder.Decide(input, _danceLevels.AsSpan(0, count), _options.Mana);
        if (Profile.TransfersAhead && potions == 0 && !_capNoted)
        {
            _capNoted = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fighter mana order: no Mana Potion to drink; Stamina to Mana (the dance) only at stamina {Vitals.VitalsOptions.DefaultStaminaUntilPercent}% or more and at most {FighterTransferCap} casts a pass (now stamina {stamina} of {staminaMax}, {_fighterTransfers} cast(s)); else the mana wait regenerates"));
        }

        switch (decision.Step)
        {
            case Vitals.ManaStep.Potion:
                if (_phase == Phase.ManaWait)
                {
                    _waitStart = double.NaN;
                }

                _potionBefore = mana;
                _manaKind = ManaKind.Potion;
                _need = need;
                _pending = _actions.Drink(potion, ActionSource.Automation);
                _phase = Phase.ManaCasting;
                Stage = StageManaCasting;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Mana Potion 0x{potion:X8} ({potionCount} held), mana {mana} of {max}, {floor} needed for {w.Line.Name} ({decision.Reason})"));
                return false;
            case Vitals.ManaStep.Dance:
                if (_phase == Phase.ManaWait)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait over after {now - _waitStart:0.#} s: {mana} of {max}, stamina {stamina}"));
                    _waitStart = double.NaN;
                }

                if (_awayFromMagic)
                {
                    return BeginMode(CombatMode.Magic, Phase.ModeBack); // the next step decides again, in the magic mode
                }

                SendDance(_danceIndex[decision.DanceIndex], mana, max, floor, now);
                return false;
            default:
                // A rest in peace for stamina, or the wait for mana regeneration: the bounded mana wait.
                var forStamina = decision.Step == Vitals.ManaStep.RestStamina;
                if (_phase != Phase.ManaWait)
                {
                    _phase = Phase.ManaWait;
                    Stage = StageManaWait;
                    _waitStart = now;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana wait: {mana} of {max}, stamina {stamina} of {staminaMax}, {floor} needed for {info.Name}; {decision.Reason}{(forStamina ? $" (to stamina {decision.StaminaTarget}), NonCombat now" : $"; NonCombat after {NonCombatAfterSeconds:0.#} s of it")}; at most {_options.ManaWaitSeconds:0.#} s (ACE halves stamina and health regeneration in a combat mode, Creature_Vitals.cs GetStanceMod)"));
                }

                _nextEvaluate = now + ReevaluateSeconds;
                if (!_awayFromMagic && (forStamina || now - _waitStart >= NonCombatAfterSeconds))
                {
                    return BeginMode(CombatMode.NonCombat, Phase.ModeAway);
                }

                return false;
        }
    }

    /// <summary>
    /// M5 7 (the owner's D88 order, applied to the buff pass): before the pass's first buff, a mana under that buff's cost dances first (the existing dance, <see cref="SendDance"/>: Stamina to Mana Self pressed until it lands), and
    /// <see cref="PassRevitalize"/> refills the stamina after. Live, a fresh login's first pass started with 15 mana and its first cast was refused for mana. One look a pass: at or over the cost, or when no dance can be pressed
    /// (the order has no Dance for it, the pass dance is off, the maximum is under the cost), the pass goes on as before. Returns true when it handled the step (<paramref name="result"/> is the step's answer).
    /// <para>
    /// M5 7 P7c (D139 row a, <c>m5-start-pass-dance</c>): a start's pass (not a re-buff pass; a mage's profile with the mana order on) looks before every line, and its cost is the line's plus the reserve: the base mana of the
    /// dearest Stamina to Mana Self level the pack pays for at the cast chance, the most one press can cost (ACE refuses a dearer roll, <c>Player_Magic.cs:566-585</c> @95ef426d). Round 13's start pass spent the mana to 42 and
    /// the vitals keeper's first press after it, Meditative Trance at 42 of up to 70, was refused "not enough mana" (0x0401). With the reserve, the pass dances while a press is sure (the mana at or over the reserve), and a line is
    /// cast only with the reserve still in hand after it, so the pass ends with the next press paid. Each look that finds the mana short logs one line (the dance, or why not).
    /// </para>
    /// </summary>
    private bool PassDance(double now, SpellInfo info, uint mana, uint max, uint need, out bool result)
    {
        result = false;
        var first = !_passLooked;
        _passLooked = true;
        var reserving = !_rebuff && !Profile.TransfersAhead && _options.Mana.On && _options.StartReserve;
        if (_phase == Phase.ManaWait || (!reserving && mana >= need) || max < need || _danceOff || !GearCasts.TryCurrentSkill(_state.Self.Qualities, MageTemplate.LifeMagic, out var skill, out _))
        {
            _passDanceDue = false;
            return false;
        }

        _lifeSkill = skill;
        _state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var count = FillDanceLevels(skill);
        if (count == 0)
        {
            _passDanceDue = false;
            return false;
        }

        _passReserve = reserving ? _danceLevels[count - 1].BaseMana : 0; // lowest first: the last is the dearest press
        var want = need + _passReserve;
        if (mana >= want || max < want)
        {
            _passDanceDue = false;
            return false;
        }

        var potions = !_potionsOff && VitalsMeansFind(out _, out var potionCount) ? potionCount : 0;
        var input = new Vitals.ManaOrderInput
        {
            Mana = mana,
            ManaMax = max,
            Stamina = stamina,
            StaminaMax = staminaMax,
            ManaConversion = _manaConversion,
            Need = new Vitals.ManaNeed(want, want),
            TriesSpent = true,
            ManaPotions = potions,
            Profile = Profile.TransfersAhead ? Vitals.ManaProfile.Fighter : Vitals.ManaProfile.Mage,
            FighterGatePercent = Vitals.VitalsOptions.DefaultStaminaUntilPercent,
            FighterTransfersLeft = FighterTransferCap - _fighterTransfers,
        };
        var decision = Vitals.ManaOrder.Decide(input, _danceLevels.AsSpan(0, count), _options.Mana);
        if (decision.Step != Vitals.ManaStep.Dance)
        {
            _passDanceDue = false;
            if (_passReserve > 0)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reserve: mana {mana} of {max} is under {want} ({need} for {info.Name} and {_passReserve} kept for the next Stamina to Mana press), stamina {stamina} of {staminaMax}; no dance ({decision.Step}: {decision.Reason}): the line goes on with the mana it has"));
            }

            return false;
        }

        if (_awayFromMagic)
        {
            result = BeginMode(CombatMode.Magic, Phase.ModeBack); // the next step decides again, in the magic mode
            return true;
        }

        if (first)
        {
            _passDancing = true;
        }
        else
        {
            _reserveDancing = true;
            _reserveDances++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"reserve: mana {mana} of {max} is under {want} ({need} for {info.Name} and {_passReserve} kept for the next Stamina to Mana press): the dance first, while a press is {(mana >= _passReserve ? "sure" : "a try")}"));
        }

        _passDanceMana = mana;
        _passDanceFor = info.Name;
        SendDance(_danceIndex[decision.DanceIndex], mana, max, need, now);
        result = false;
        return true;
    }

    /// <summary>
    /// M5 7 (D88): after the pass's first dance landed, the mage's stamina under <see cref="Scenarios.FightOptions.DefaultStaminaFullPercent"/> is refilled with Revitalize Self, the highest payable level whose cost leaves the first buff's mana in hand, one cast
    /// a look until the stamina is back or the mana cannot pay another. A fighter's profile never does (it spends the stamina). True when a cast was sent.
    /// </summary>
    private bool PassRevitalize(double now, SpellInfo info, uint mana, uint max, uint need)
    {
        _reviveDue = false;
        var q = _state.Self.Qualities;
        var keep = need + _passReserve; // P7c: a start's pass keeps the next press's mana too
        if (Profile.TransfersAhead || _passRevives >= PassRevitalizeLimit || mana <= keep || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax)
            || !Vitals.ManaOrder.StaminaUnder(stamina, staminaMax, Scenarios.FightOptions.DefaultStaminaFullPercent) || !GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var skill, out _))
        {
            return false;
        }

        _lifeSkill = skill;
        EnsurePayable();
        var index = PickMana(_boostLine, _boostInfos, _boostPayable, skill, mana - keep);
        if (index < 0)
        {
            return false;
        }

        _passRevive = true;
        _passRevives++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: Revitalize after the {(_reserveDances > 0 ? "reserve" : "pre-pass")} dance: stamina {stamina} of {staminaMax} is under {Scenarios.FightOptions.DefaultStaminaFullPercent}%, mana {mana} of {max} keeps the {need} {info.Name} needs{(_passReserve > 0 ? $" and the {_passReserve} reserve" : string.Empty)}"));
        SendMana(ManaKind.Boost, index, mana, max, need, now);
        return true;
    }

    /// <summary>The Stamina to Mana Self levels of the profile's transfer line at the cast chance (from <paramref name="skill"/>), lowest first, into <c>_danceLevels</c> (their line index in <c>_danceIndex</c>); their count. No allocation.</summary>
    private int FillDanceLevels(uint skill)
    {
        EnsurePayable();
        var count = 0;
        var floored = 0;
        for (var i = 0; i < _transferInfos.Length; i++)
        {
            if (_transferInfos[i] is { } info && _transferRows[i] is { } row && GearCasts.Chance(skill, _transferLine.Spells[i].Power) >= _options.CastChance)
            {
                if (!_transferPayable[i])
                {
                    floored++; // M5 4 G1: a level whose foci-formula scarab the pack lacks is not pressed (ACE refuses it 0x0400)
                    continue;
                }

                _danceIndex[count] = i;
                _danceLevels[count++] = new Vitals.DanceLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power, row);
            }
        }

        if (count == 0 && floored > 0 && !_noPayableDanceNoted)
        {
            _noPayableDanceNoted = true;
            _log.Info(LogCategory, _transferLine.Name + ": no level the pack can pay for (" + floored.ToString(CultureInfo.InvariantCulture) + " skipped by the level floor): no dance; the mana wait regenerates instead");
        }

        return count;
    }

    /// <summary>
    /// M5 4 G1: the level floor for the mana lines (the Stamina to Mana Self and Revitalize Self levels): which levels the pack pays for on the foci formula (<see cref="LevelFloor"/>), read afresh at every look but those of the mana wait
    /// (the wait allocates nothing and nothing is cast during it); each level skipped is named once. Live, the dance at mana 1 of 415 fell to level V, whose Gold Scarab no stock line carries, and ACE refused it 0x0400.
    /// </summary>
    private void EnsurePayable()
    {
        if (_phase == Phase.ManaWait && _payableKnown)
        {
            return;
        }

        _payableKnown = true;
        var held = LevelFloor.HeldFor(_state, _transferLine.School);
        FillPayable(_transferLine, _transferInfos, _transferPayable, held);
        FillPayable(_boostLine, _boostInfos, _boostPayable, _boostLine.School == _transferLine.School ? held : LevelFloor.HeldFor(_state, _boostLine.School));
    }

    private void FillPayable(ProfileLine line, SpellInfo?[] infos, bool[] payable, Dictionary<uint, int>? held)
    {
        for (var i = 0; i < infos.Length; i++)
        {
            payable[i] = true;
            if (held is not null && infos[i] is { } info && LevelFloor.ShortOf(held, info, out var need))
            {
                payable[i] = false;
                if (_floorLogged.Add(info.Id))
                {
                    _log.Warn(LogCategory, line.Name + ": " + LevelFloor.SkipText(line.Spells[i], need));
                }
            }
        }
    }

    /// <summary>Sends the dance (D27): the transfer line's level <paramref name="index"/> pressed until it lands, at most <see cref="Vitals.ManaOrderOptions.DancePresses"/> presses, every press counted.</summary>
    private void SendDance(int index, uint mana, uint max, uint need, double now)
    {
        var info = _transferInfos[index]!;
        _manaKind = ManaKind.Transfer;
        _dancing = true;
        _dances++;
        _need = need;
        _castsSincePeak++;
        _chatMark = _state.Chat.Total;
        _manaCasts++;
        _tried++;
        if (Profile.TransfersAhead)
        {
            _fighterTransfers++;
        }

        _phase = Phase.ManaCasting;
        Stage = StageManaCasting;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance: {info.Name} ({info.Id}) pressed until it lands ({_danceRules.Name}), mana {mana} of {max}, {need} needed for {_order[_line].Line.Name}"));
        _cast.Start(new CastRequest { Levels = _transferIds, Index = index, Rules = _danceRules, Purpose = "mana dance Stamina to Mana", Proof = _danceProof }, now);
    }

    /// <summary>
    /// A profile that <see cref="IBuffProfile.TransfersAhead"/> (a fighter's, M4 P5a C4) casts the transfer before the buff when the mana left after the buff would be under what the strongest transfer castable at the chance costs: a pool of 225
    /// that pays 50 a buff would otherwise run to 5 or 25, below the cost of any good transfer, and wait for regeneration (a fighter regenerates 1 mana a 5 s tick). The transfer is the ordinary one (<see cref="Decide"/>: it must give back more than it costs and
    /// cover the shortfall to <paramref name="aheadNeed"/>, or help while the stamina cannot be raised). Only in the ordered pass, and not before its last line; <see cref="ManaCastLimitPerLine"/> bounds it.
    /// </summary>
    private bool PreemptTransfer(uint mana, uint max, uint need, out int index, out uint aheadNeed)
    {
        index = -1;
        aheadNeed = need;
        if (!Profile.TransfersAhead || _rebuff || _upgrading || _line >= _order.Length - 1 || _manaCasts >= ManaCastLimitPerLine)
        {
            return false;
        }

        if ((!_potionsOff && VitalsMeansFind(out _, out _)) || !FighterTransferAllowed())
        {
            return false; // M4 P5a C7: a held Mana Potion is drunk when the next line needs the mana; no potion: a transfer ahead only at a high stamina and within the cap
        }

        if (!GearCasts.TryCurrentSkill(_state.Self.Qualities, MageTemplate.LifeMagic, out var skill, out _))
        {
            return false; // the mana loop proper names the missing skill when it matters
        }

        _lifeSkill = skill;

        EnsurePayable();
        var reserve = 0u;
        for (var i = _transferInfos.Length - 1; i >= 0; i--)
        {
            if (_transferInfos[i] is { } info && GearCasts.Chance(skill, _transferLine.Spells[i].Power) >= _options.CastChance && _transferPayable[i])
            {
                reserve = GearCasts.ManaCeiling(info, 0);
                break;
            }
        }

        if (reserve == 0 || mana - need >= reserve || max < need + reserve)
        {
            return false;
        }

        aheadNeed = need + reserve;
        return Decide(mana, max, aheadNeed, out index, out _) == ManaAction.Transfer;
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
        EnsurePayable();
        var transfer = PickMana(_transferLine, _transferInfos, _transferPayable, skill, mana);
        var boost = PickMana(_boostLine, _boostInfos, _boostPayable, skill, mana);
        if (Profile.TransfersAhead)
        {
            // M4 P5a C7: the fighter's order. Revitalize is never cast for the sake of mana (it costs mana itself); Stamina to Mana only at a high stamina and within the cap, else the mana wait regenerates.
            boost = -1;
            if (transfer >= 0 && !FighterTransferAllowed())
            {
                if (!_capNoted)
                {
                    _capNoted = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fighter mana order: no Mana Potion to drink; Stamina to Mana only at stamina {Vitals.VitalsOptions.DefaultStaminaUntilPercent}% or more and at most {FighterTransferCap} casts a pass (now stamina {stamina} of {staminaMax}, {_fighterTransfers} cast(s)): the mana wait regenerates instead"));
                }

                transfer = -1;
            }
        }

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

    /// <summary>The highest level of a mana line whose chance reaches the threshold, that the pack can pay for (the level floor) and whose cost is at most <paramref name="mana"/>, or -1.</summary>
    private int PickMana(ProfileLine line, SpellInfo?[] infos, bool[] payable, uint skill, uint mana)
    {
        for (var i = line.Spells.Count - 1; i >= 0; i--)
        {
            if (infos[i] is { } info && payable[i] && GearCasts.Chance(skill, line.Spells[i].Power) >= _options.CastChance && GearCasts.ManaCeiling(info, 0) <= mana)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when a held Mana Potion exists (M4 P5a C7); no allocation.</summary>
    private bool VitalsMeansFind(out uint guid, out int count) => Vitals.VitalsMeans.FindManaPotions(_state, _potionScratch, out guid, out count);

    /// <summary>A fighter's Stamina to Mana is allowed at a stamina of <see cref="Vitals.VitalsOptions.DefaultStaminaUntilPercent"/> or more and within <see cref="FighterTransferCap"/> casts (M4 P5a C7); a mage's always.</summary>
    private bool FighterTransferAllowed()
    {
        if (!Profile.TransfersAhead)
        {
            return true;
        }

        return _fighterTransfers < FighterTransferCap && _state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax) && staminaMax != 0 && 100.0 * stamina / staminaMax >= Vitals.VitalsOptions.DefaultStaminaUntilPercent;
    }

    private void SendMana(ManaKind kind, int index, uint mana, uint max, uint need, double now)
    {
        var line = kind == ManaKind.Transfer ? _transferLine : _boostLine;
        var info = (kind == ManaKind.Transfer ? _transferInfos : _boostInfos)[index]!;
        _manaKind = kind;
        _need = need;
        _castsSincePeak++;
        _chatMark = _state.Chat.Total;
        _manaCasts++;
        if (kind == ManaKind.Transfer)
        {
            _tried++;
            if (Profile.TransfersAhead)
            {
                _fighterTransfers++;
            }
        }

        _phase = Phase.ManaCasting;
        Stage = StageManaCasting;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: {info.Name} ({line.Spells[index].Id}), mana {mana} of {max}, {need} needed for {_order[_line].Line.Name}"));
        // M5 1 K3: through the cast policy as Automation (D13). Utility(TransferTries) is today's rule: the n-th fizzle in a row fails the pass; the streak the loop kept across looks goes in FizzlesSpent.
        _cast.Start(
            new CastRequest { Levels = kind == ManaKind.Transfer ? _transferIds : _boostIds, Index = index, Rules = _manaRules, Purpose = kind == ManaKind.Transfer ? "mana Stamina to Mana" : "mana Revitalize", FizzlesSpent = kind == ManaKind.Transfer ? _transferStreak : _boostStreak },
            now);
    }

    /// <summary>The outcome of a mana cast: a fire (UseDone without a fizzle) or a fizzle counts; anything else fails the cycle.</summary>
    private bool ManaOutcome(double now)
    {
        var kind = _manaKind;
        if (kind != ManaKind.Potion)
        {
            return ManaCastOutcome(now, kind);
        }

        if (_pending?.Outcome is not { } outcome)
        {
            return false;
        }

        _pending = null;
        if (_state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _))
        {
            NoteMana(mana);
        }

        if (outcome.Succeeded)
        {
            _manaPotions++;
            _potionWaiting = true;
            _potionWaitUntil = now + PotionVitalWaitSeconds;
        }
        else
        {
            _potionsOff = true;
            _log.Warn(LogCategory, $"mana potion: the Mana Potion was not carried out ({outcome.Kind} 0x{outcome.Code:X4}: {outcome.Detail}); no more Mana Potions this pass");
        }

        _phase = Phase.Mana;
        Stage = StageMana;
        return true;
    }

    /// <summary>
    /// The end of a Stamina to Mana or Revitalize attempt (M5 1 K3): a landed or sent cast counts (a fire), the fizzles add to the streak the loop keeps across looks (the n-th in a row fails the pass, <see cref="BuffOptions.TransferTries"/>),
    /// a retry the mana cannot pay goes back to the mana step with the streak, and any other end fails the cycle.
    /// </summary>
    private bool ManaCastOutcome(double now, ManaKind kind)
    {
        if (_cast.Step(now) == CastEnd.Running)
        {
            return false;
        }

        var report = _cast.Report;
        _tally.Add(report);
        _fizzles += report.Fizzles;
        var extra = report.Tries - 1; // SendMana counted the first try; the attempt's retries (and a try never sent) correct it
        _manaCasts += _dancing ? 0 : extra; // a dance is one mana cast against ManaCastLimitPerLine; its presses are counted in mana_presses=
        _castsSincePeak += extra;
        if (kind == ManaKind.Transfer && !_dancing) // a dance is one transfer tried (transfers=f/t) and one against the fighters' cap, whatever its presses (mana_presses=)
        {
            _tried += extra;
            if (Profile.TransfersAhead) // a dance is one transfer against the fighters' cap, whatever its presses
            {
                _fighterTransfers += extra;
            }
        }

        _chatMark = _cast.ChatMark;
        if (_state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _))
        {
            NoteMana(mana);
        }

        var name = kind == ManaKind.Transfer ? "Stamina to Mana" : "Revitalize";
        if (_dancing)
        {
            // M5 1 K5 (D27): the dance's end; every press counted. Presses spent with none landed go back to the mana step (ManaCastLimitPerLine bounds it, each press counted there too).
            _dancing = false;
            _dancePresses += report.Tries;
            if (_passDancing || _reserveDancing)
            {
                // M5 7 (c): one line for the dance before a buff pass: the mana before and after, the presses, the spell it was for. P7c: a start pass's later line names it the reserve dance.
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{(_passDancing ? "pre-pass" : "reserve")} dance: mana {_passDanceMana} -> {mana}, {report.Tries} press(es) ({report.ManaRefusals} refused, {report.Fizzles} fizzled), {(report.End.Succeeded() ? "landed" : report.End.Name())}, for {_passDanceFor}"));
                _passDancing = false;
                _reserveDancing = false;
                _passDanceDue = false;
                _reviveDue = report.End.Succeeded();
            }

            if (report.End.Succeeded())
            {
                _fired++;
                _dancesLanded++;
                _transferStreak = 0;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance landed on press {report.Tries} ({report.ManaRefusals} refused, {report.Fizzles} fizzled); mana {mana} now"));
            }
            else if (report.End is CastEnd.ManaShort or CastEnd.TimeCap)
            {
                // Every press refused or fizzled: the server's costs are not the model's (the try chance promised a landing). Named, and no more dances this pass: the mana wait instead.
                _danceOff = true;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: the dance did not land in {report.Tries} presses ({report.ManaRefusals} refused, {report.Fizzles} fizzled); no more dances this pass, the mana wait instead"));
            }
            else
            {
                return Fail(now, "the mana loop: the dance ended: " + ExplainEnd(report));
            }

            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        if (report.End.Succeeded())
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
                _reviveDue = _passRevive; // M5 7: the pass's refill goes on until the stamina is back or the mana cannot pay another
            }

            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        if (report.Fizzles > 0 && (report.End == CastEnd.Fizzled || (report.End == CastEnd.ManaShort && report.Code == CastAttempt.FizzleCode)))
        {
            var streak = kind == ManaKind.Transfer ? _transferStreak += report.Fizzles : _boostStreak += report.Fizzles;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"mana: {name} fizzled (try {streak} of {_options.TransferTries}; it costs 5 mana, Player_Magic.cs 566-572)"));
            if (streak >= _options.TransferTries)
            {
                return Fail(now, string.Create(CultureInfo.InvariantCulture, $"the mana loop: {streak} {name} casts in a row fizzled (--buff-transfer-tries {_options.TransferTries})"));
            }

            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        return Fail(now, "the mana loop: a " + name + " cast ended: " + ExplainEnd(report));
    }

    /// <summary>The words for an attempt that did not carry the cast out: the Action API's own outcome when a try was sent, else the attempt's reason (the kill switch).</summary>
    private string ExplainEnd(in CastReport report) =>
        _cast.LastOutcome is { } outcome ? GearCasts.Explain(outcome, _state, _chatMark, _need) : report.End + ": " + report.Why;

    // ------------------------------------------------------------------------------------------------ the combat mode

    private bool BeginMode(CombatMode mode, Phase phase)
    {
        _pending = _actions.SetCombatMode(mode, ActionSource.Automation);
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

    private void SendBuff(WantedLine w, SpellInfo info, uint need, double now, uint tryFloor = 0)
    {
        _need = need;
        _chatMark = _state.Chat.Total;
        var atCaster = w.Armour && GearCasts.FormOf(info) == CastForm.AtItem;
        _phase = Phase.Casting;
        Stage = StageCasting;
        var q = _state.Self.Qualities;
        GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(w.Line.School), out var skill, out _);
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"cast {info.Name} ({info.Id}) {(atCaster ? "at the caster (every worn piece, D23)" : "on the caster")}: {w.Line.School} skill {skill}, chance {GearCasts.Chance(skill, info.Power):0.0000}, up to {need} mana{(tryFloor > 0 ? $" (from the try floor {tryFloor}: the Mana Conversion tries, D15)" : string.Empty)}"));
        // M5 1 K3: through the cast policy as Automation (D13). A buff line is BuffLineD14 (the owner's fizzle rule, D14: the same level twice more, then one level down once, then a named miss; the mana checked again before each retry); the upgrade pass is Once (cast once, never retried).
        _cast.Start(
            new CastRequest
            {
                Levels = w.Ids,
                Index = _castIndex,
                Target = atCaster ? _state.Self.Guid : 0,
                Rules = _upgrading ? _upgradeTriesRules : _lineTriesRules, // M5 1 K5: with the tries (D15) unless --cast-mana-tries 0, where these are Once and BuffLineD14 exactly
                Purpose = w.Purpose,
                WornItems = _state.Self.Inventory.Equipped.Count,
                FizzlesSpent = _spentFizzles,
                LevelsDownSpent = _spentDown,
                ManaTryFloor = tryFloor,
                ManaConversion = _options.Mana.On ? _manaConversion : null, // M5 7: a Mana Conversion try only where the model says it can raise the mana to the cost
                ManaTryChance = _options.Mana.TryChance,
                Usable = _floorOn ? _payable : null, // M5 3 w1: D14's level down never steps onto a level the pack cannot pay for
            },
            now);
    }

    private bool CastOutcome(double now)
    {
        if (_cast.Step(now) == CastEnd.Running)
        {
            return false;
        }

        var report = _cast.Report;
        _tally.Add(report);
        _fizzles += report.Fizzles;
        _chatMark = _cast.ChatMark;
        var w = _order[_line];
        if (_state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _))
        {
            NoteMana(mana);
        }

        if (report.End == CastEnd.ManaShort && report.Code == CastAttempt.FizzleCode)
        {
            // The retry after a fizzle needs more mana than is held: nothing was sent; back to the mana step for the level the attempt would have retried, its spent bounds kept.
            _castIndex = report.NextLevelIndex;
            _spentFizzles = report.FizzleRetriesSpent;
            _spentDown = report.LevelsDownSpent;
            _log.Info(LogCategory, $"{w.Infos[report.LevelIndex]!.Name} fizzled; the retry ({w.Infos[_castIndex]?.Name ?? "(no spell)"}) needs more mana: the mana step first");
            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        if (report.End == CastEnd.ManaShort && report.Tries == 0 && report.Code == 0 && _options.Mana.On)
        {
            // M5 7: the model gate sent nothing (it cannot raise the mana to the cost): no blind tries; the mana step funds the ceiling (dance, potion or rest), the spent bounds kept.
            _castIndex = report.LevelIndex;
            _spentFizzles = report.FizzleRetriesSpent;
            _spentDown = report.LevelsDownSpent;
            _lineTriesSpent = true;
            _log.Info(LogCategory, $"{w.Infos[_castIndex]?.Name ?? w.Line.Name}: {report.Why}; the mana step funds its ceiling");
            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        if (report.End == CastEnd.ManaShort && report.ManaRefusals > 0 && _options.Mana.On)
        {
            // M5 1 K5 (D15): a 0x0401 inside the tries is expected, never a failure; the tries spent, the mana step funds the ceiling (the order's step 3), the spent bounds kept.
            _castIndex = report.LevelIndex;
            _spentFizzles = report.FizzleRetriesSpent;
            _spentDown = report.LevelsDownSpent;
            if (_lineTriesSpent && ++_ceilingAttempts >= CeilingAttemptsPerLine)
            {
                // M5 3 m4 (m5-buffcycle-ceiling-refusal, 1-R14): the tries were spent before and this attempt was sent at the ceiling (the floor is the need once they are spent); the server still refused it, so the mana the read model shows is not the server's.
                return Fail(now, string.Create(CultureInfo.InvariantCulture, $"{w.Infos[_castIndex]?.Name ?? w.Line.Name}: refused for mana (0x0401) again at its ceiling of {_need} with {mana} shown: the read model's mana is not the server's, so the line is not sent again (the {CeilingAttemptsPerLine} attempt at the ceiling after the tries was refused)"));
            }

            _lineTriesSpent = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{w.Infos[_castIndex]?.Name ?? w.Line.Name}: {report.ManaRefusals} Mana Conversion tries refused (0x0401, nothing charged); the mana step funds its ceiling"));
            _phase = Phase.Mana;
            Stage = StageMana;
            return true;
        }

        _castIndex = report.LevelIndex;
        var info = w.Infos[_castIndex]!;
        if (report.End == CastEnd.Fizzled)
        {
            if (_upgrading)
            {
                Miss(w, "upgrade-fizzled", w.Line.Name + ": the upgrade to " + info.Name + " fizzled; the line stays at the level it held (an upgrade is cast once, no retry)");
                return NextLine();
            }

            Miss(w, "fizzled", w.Line.Name + ": " + FizzledText(report, info));
            return NextLine();
        }

        if (report.End is CastEnd.Refused or CastEnd.Cancelled or CastEnd.DryRun or CastEnd.NotImplemented or CastEnd.Timeout or CastEnd.TimeCap)
        {
            return Fail(now, w.Line.Name + ": the cast was not carried out: " + ExplainEnd(report));
        }

        if (!report.End.Succeeded())
        {
            // Any other answer (a refusal for mana the read model did not know of, busy, a missing component, out of range): a named miss, never retried.
            var code = _cast.LastOutcome?.Code ?? 0;
            Miss(w, "error-0x" + code.ToString("X4", CultureInfo.InvariantCulture), w.Line.Name + ": " + ExplainEnd(report));
            return NextLine();
        }

        var outcome = _cast.LastOutcome!.Value;
        if (w.Armour)
        {
            ArmourLanded(w, info, _cast.Handle?.CompletedAt ?? now, now);
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

    /// <summary>The miss text of a buff line that fizzled out of its retries (D14: the same level twice more, one level down once, no further try).</summary>
    private static string FizzledText(in CastReport report, SpellInfo info)
    {
        if (report.LevelsDownSpent > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fizzled {report.Fizzles} times ({report.FizzleRetriesSpent} more tries at the same level, then one level down), the last at {info.Name} (no further try)");
        }

        if (report.Why is { } usable && usable.Contains("no usable level below", StringComparison.Ordinal))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{info.Name} fizzled {report.Fizzles} times and no level below is one the pack can pay for (its foci components, M5 3 w1; no further try)");
        }

        return report.Why is { } why && why.Contains("not in the SpellTable", StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"{info.Name} fizzled {report.Fizzles} times and the level below is not in the SpellTable")
            : string.Create(CultureInfo.InvariantCulture, $"{info.Name} fizzled {report.Fizzles} times and there is no level below (no further try)");
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
        string? missing = null;
        var missingCount = 0;
        var recorded = 0;
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
                _ledger.Record(_expectedGuid[p], w.Line.Name, info.Id, doneAt, info.Duration);
                recorded++;
            }
            else
            {
                _ledger.Forget(_expectedGuid[p], w.Line.Name); // a cast that did not name the piece leaves nothing known about it
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
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{w.Line.Name}: {recorded} pieces enchanted, each ends {info.Duration:0} s after the cast's UseDone (at {doneAt + info.Duration:0.#} on the session clock; now {now:0.#})"));
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
            // The ledger's memory (M4 4.1 lane b; closes TODO(m3-buff-armour-memory)): every worn piece must hold the line, the least time left counts and the lowest level among the pieces is the line's level. A piece the
            // ledger holds with an unknown time left (an appraisal found the line and no saved time restored it) is present and due at once: 0 seconds left.
            if (_expectedCount == 0)
            {
                return false;
            }

            var least = double.PositiveInfinity;
            var lowest = int.MaxValue;
            for (var p = 0; p < _expectedCount; p++)
            {
                if (!_ledger.TryGet(_expectedGuid[p], w.Line.Name, out var entry) || !_spellOf.TryGetValue(entry.SpellId, out var at) || at.Line != w.Index)
                {
                    return false;
                }

                var left = 0.0;
                if (entry.TimeKnown)
                {
                    left = entry.ExpiresAt - now;
                    if (left <= 0)
                    {
                        return false;
                    }
                }

                least = Math.Min(least, left);
                lowest = Math.Min(lowest, at.Index);
            }

            index = lowest;
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
        var worn = Math.Min(equipped.Count, MaxWorn);
        for (var i = 0; i < worn; i++)
        {
            _wornGuid[i] = equipped[i].Guid;
        }

        _ledger.Prune(_wornGuid.AsSpan(0, worn)); // a piece taken off loses its lines
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
        if (count > 0)
        {
            _noArmourLogged = false;
        }

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
        _log.Info(LogCategory, "pass done: " + Result.Fields + " " + Result.CastFields);
        return false;
    }

    private bool Fail(double now, string why)
    {
        Stage = StageFailed;
        _phase = Phase.Idle;
        Status = BuffStatus.Failed;
        _pending = null;
        Result = Build(now, why);
        _log.Warn(LogCategory, "FAILED: " + why + "; " + Result.Fields + " " + Result.CastFields);
        return false;
    }

    private BuffResult Build(double now, string? failure)
    {
        CollectExpected();
        var levels = new StringBuilder();
        var active = 0;
        var min = double.PositiveInfinity;
        var wanted = 0;
        foreach (var w in _order)
        {
            if (!Wants(w))
            {
                continue; // M5 7 P7j: a line learned by play is in the result once the spellbook holds it
            }

            wanted++;
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
            Wanted = wanted,
            Active = active,
            Levels = levels.ToString(),
            RecastWhileActive = _passRecast,
            MinRemainingSeconds = double.IsPositiveInfinity(min) ? double.NaN : min,
            Fizzles = _fizzles,
            TransfersFired = _fired,
            TransfersTried = _tried,
            Revitalizes = _revitalizes,
            ManaPotions = _manaPotions,
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
            ManaTries = _tally.ManaTries,
            ManaRefusals = _tally.ManaRefusals,
            ManaLanded = _tally.ManaLanded,
            Busy = _tally.Busy,
            Resists = _tally.Resists,
            Proved = _tally.Proved,
            Dances = _dances,
            DancesLanded = _dancesLanded,
            DancePresses = _dancePresses,
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

    private static uint[] IdsOf(ProfileLine line)
    {
        var ids = new uint[line.Spells.Count];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = line.Spells[i].Id;
        }

        return ids;
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
