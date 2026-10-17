using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which stages of the fight scenario run before the fight itself (the stages that need the server's tools or the character's own kit can be left to the caller).</summary>
[Flags]
public enum FightStages
{
    /// <summary>Only the fight: the player is in the world, in the den, in the magic combat mode (the tests', and a host that has done the rest).</summary>
    None = 0,

    /// <summary>The gear scenario (login, the wand and the armour).</summary>
    Gear = 1,

    /// <summary>The buff top-up (the buff cycle when a profile line is missing or has under <see cref="FightScenario.TopUpSeconds"/> left).</summary>
    Buffs = 2,

    /// <summary>The teleport to the den's arrival (<see cref="BlackSpawnDen.Arrival"/>) and the settle.</summary>
    Place = 4,

    /// <summary>The change to the magic combat mode.</summary>
    Mode = 8,

    /// <summary>Everything (the headless host's).</summary>
    All = Gear | Buffs | Place | Mode,
}

/// <summary>
/// M3 3.1's fight scenario (<c>--scenario=fight</c>, plan section 4.7 lane fa; BSD Mage and nobody else): the character has the gear on, is topped up with the profile's buffs, is sent to the Black Spawn Den's arrival
/// (<see cref="BlackSpawnDen"/>), enters the magic combat mode and casts Flame Bolt at the nearest tusker on the arrival's floor until <see cref="FightOptions.Kills"/> kills (the server's 0x01AD notices, counted by
/// <see cref="FightTally"/>) or <see cref="FightOptions.Seconds"/> pass, walking along the floor when nothing is in range or the casts miss, and returns to non-combat.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>, each logged once on entry as <c>[fight] stage &lt;name&gt;</c>): <c>start</c>, <c>gear</c> (login, entering and the gear are <see cref="GearScenario"/>'s), <c>buffs</c> (the top-up, or the
/// logged skip), <c>place</c> and <c>teleporting</c> and <c>settle</c> (<c>PlayerTools.Place</c> to the den's Places row through <see cref="TeleportCatalog"/>; settled as <see cref="RoutePlay"/> does: the player is present, then
/// <see cref="SettleSeconds"/>), <c>mode-magic</c>, <c>fight</c>, <c>mode-peace</c>, <c>logging-off</c>, <c>quitting</c>, <c>done</c>. With <c>endSession: false</c> (the game's leg, the tests) it ends at <c>done</c> in the world.</para>
/// <para><b>The buff top-up</b> runs <see cref="BuffCycle"/> itself (the scenario's engine) with a re-buff margin of <see cref="TopUpSeconds"/>, not <see cref="BuffScenario"/>: that would run the gear a second time and cast
/// its forced re-buff pass for nothing. It is skipped, and logged, when every one of the profile's 24 lines has at least <see cref="TopUpSeconds"/> left.</para>
/// <para><b>The fight</b> (D10, D12, D21): the Flame Bolt level is the highest of levels I-VII the spellbook holds whose ACE cast chance from the buffed War Magic skill (<see cref="GearCasts.TryCurrentSkill"/>,
/// <see cref="GearCasts.Chance"/>, the buff cycle's code) reaches <see cref="BuffOptions.CastChance"/>; a fizzle drops one level once. The target is
/// <c>WorldObjectTable.NearestCreature</c> over <see cref="ArenaProfile.TargetWcids"/> on <see cref="BlackSpawnDen.IsFloorCell"/>'s cells within the cast's range: ACE's
/// <c>min(BaseRangeConstant + (Init + Ranks) * BaseRangeMod, MaxRadarRange_Outdoors)</c> (<c>Player_Magic.cs</c> 495-505, which reads the skill's start level plus its ranks, not the buffed value; beyond it ACE answers UseDone
/// <c>MissileOutOfRange</c> 0x0550). Each cast is a miss unless it is Confirmed (the projectile exists) <i>and</i> a damage line or the kill follows within <see cref="HitWaitSeconds"/>; <see cref="MissesBeforeWalking"/> misses in
/// a row, an out-of-range answer, or no target in range send the player along the floor to the next waypoint (<see cref="IFightWalk"/>). ACE turns the caster to a targeted spell's target itself (<c>Player_Magic.cs</c> about 827,
/// <c>CreateTurnToChain2</c>), so the scenario never turns the player to cast. A cast that needs more mana than the player has waits for regeneration (it counts against <see cref="FightOptions.Seconds"/>).</para>
/// <para><b>Failure</b> is loud and logs off first in the world (as the buff scenario): the player's death, the seconds limit (with the counts), a walk that cannot be planned or fails, an unreadable skill or spellbook.</para>
/// <para>Per-step code allocates nothing but log lines on events: the cell filter is one cached delegate, the death and projectile handlers are subscribed once.</para>
/// </remarks>
public sealed class FightScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token (<c>--scenario=fight</c>).</summary>
    public const string ScenarioName = "fight";

    /// <summary>The log category of the scenario's lines.</summary>
    public const string LogCategory = "fight";

    /// <summary>A profile line with less than this many seconds left is topped up before the fight (the plan's ten minutes).</summary>
    public const double TopUpSeconds = 600;

    /// <summary>How long the player is present in the den before the fight starts, in seconds (RoutePlay's settle).</summary>
    public const double SettleSeconds = 3;

    /// <summary>The limit on the teleport, in seconds (portal space takes 5 s or more).</summary>
    public const double TeleportSeconds = RoutePlay.TeleportSeconds;

    /// <summary>The seconds a confirmed projectile may take to produce a damage line or a kill before the cast is a miss.</summary>
    public const double HitWaitSeconds = 3.0;

    /// <summary>The seconds after a target's death (its Dead motion or its deletion) the kill notice may take before the target is left.</summary>
    public const double KillNoticeSeconds = 2.0;

    /// <summary>The misses in a row that send the player to the next waypoint.</summary>
    public const int MissesBeforeWalking = 3;

    /// <summary>ACE's <c>Player.MaxRadarRange_Outdoors</c> (<c>Player.cs</c> 74), the cap of a spell's range.</summary>
    public const float MaxRange = 75f;

    /// <summary>The WeenieError ACE sends when the target is beyond the spell's range (<c>MissileOutOfRange</c>, 0x0550).</summary>
    public const uint MissileOutOfRange = 0x0550;

    /// <summary>The code of a fizzle (GameEvent 0x028A 0x0402).</summary>
    public const uint FizzleCode = 0x0402;

    /// <summary>The Flame Bolt levels in play (I-VII; the profile's eighth spell is Incantation, D10).</summary>
    public const int Levels = 7;

    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

    private enum Phase
    {
        Choose,
        Mana,
        Cast,
        Hit,
        DeadWait,
        Walk,
    }

    private readonly GearScenario? _gear;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly BuffOptions _buffOptions;
    private readonly FightOptions _options;
    private readonly IFightWalk? _walk;
    private readonly bool _endSession;
    private readonly FightStages _stages;
    private readonly ILogSink _log;
    private readonly FightTally _tally = new();
    private readonly Func<uint, bool>? _cellFilter;
    private readonly List<uint> _killed = [];
    private readonly uint[] _levelIds = new uint[Levels];
    private readonly TeleportDestination _destination = TeleportCatalog.FromInterior(InteriorLocations.BlackSpawnDenAluvian);
    private BuffCycle? _cycle;
    private ActionHandle? _pending;
    private ActionHandle? _health;
    private ToolHandle<PlaceResult>? _place;
    private IDisposable? _diedSubscription;
    private IDisposable? _createdSubscription;
    private IDisposable? _movedSubscription;
    private bool _failLogOff;
    private bool _subscribed;
    private bool _disposed;
    private string _gearFields = "gear=n/a worn=0";
    private string _buffsNote = "n/a";
    private Phase _phase;
    private int _level = -1;
    private bool _fizzleDropped;
    private float _range;
    private uint _manaNeeded;
    private bool _manaLogged;
    private uint _target;
    private bool _damageWaitLogged;
    private int _missesInARow;
    private int _walks;
    private int _selfTeleports;
    private int _teleportMark;
    private int _damageMark;
    private int _killMark;
    private int _targetKillMark;
    private int _projectiles;
    private double _stageStart;
    private double _fightStart;
    private double _awaitSince;
    private double _settleUntil = double.NaN;
    private double _deadSince;
    private bool _died;
    private string? _deathText;
    private double _fightEnd = double.NaN;

    /// <summary>The fight scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components, the buff casts and the Flame Bolt levels' ranges and costs.</param>
    /// <param name="account">The account name the kit is built for (hashed by the kit, never stored, logged or printed).</param>
    /// <param name="buff">The buff cycle's options (the top-up's cast chance and the rest).</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="walk">The walk along the floor (<see cref="DenWalk"/>), or null: a leg then fails the fight loudly.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run before the fight (<see cref="FightStages.All"/>).</param>
    public FightScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, FightStages stages = FightStages.All)
        : this(GearKit.Build(spells ?? throw new ArgumentNullException(nameof(spells)), account), spells, buff, fight, walk, log, arrivalSeconds, endSession, stages)
    {
    }

    /// <summary>The fight scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="buff">The buff cycle's options.</param>
    /// <param name="fight">The fight's options.</param>
    /// <param name="walk">The walk along the floor, or null.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run before the fight.</param>
    public FightScenario(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, FightStages stages = FightStages.All)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(buff);
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(log);
        // The arena here (the offline stand-in's tusker): the player is where it is, in the mode the check chose, so only the mode stage may run, and the floor rule is off (fd).
        stages = fight.ArenaHere ? stages & FightStages.Mode : stages;
        (_spells, _options, _log, _walk, _endSession, _stages) = (spells, fight, log, walk, endSession, stages);
        _cellFilter = fight.ArenaHere ? null : BlackSpawnDen.IsFloorCell;
        _buffOptions = buff with { RebuffMarginSeconds = TopUpSeconds };
        _gear = (stages & FightStages.Gear) != 0 ? new GearScenario(kit, log, arrivalSeconds, endSession: false, cast: false, spells) : null;
        var line = ArenaProfile.LineNamed(ArenaProfile.WarLineName) ?? throw new InvalidOperationException("the profile has no " + ArenaProfile.WarLineName + " line");
        for (var i = 0; i < Levels; i++)
        {
            _levelIds[i] = line.Spells[i].Id;
        }
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario's own walker sends movement, but its audit is <c>TODO(m3-fight-walk-audit)</c>.</remarks>
    public long Unmatched => 0;

    /// <summary>The fight's counters (casts, misses, damage dealt and taken, kills, experience).</summary>
    public FightTally Tally => _tally;

    /// <summary>The Flame Bolt level in use as a numeral (<c>I</c> to <c>VII</c>), or <c>n/a</c> before the fight chose one.</summary>
    public string Level => _level < 0 ? "n/a" : Numerals[_level];

    /// <summary>The Flame Bolt spell id in use, or 0.</summary>
    public uint SpellId => _level < 0 ? 0 : _levelIds[_level];

    /// <summary>The cast range in metres (<see cref="CastRange"/>), 0 before the fight chose a level.</summary>
    public float RangeMetres => _range;

    /// <summary>The guid of the creature the loop is aimed at (0 when it has none now: before the fight, between targets, while it walks).</summary>
    public uint Target => _target;

    /// <summary>True from the fight's stage to its end: the loop is casting, waiting for a hit or walking.</summary>
    public bool Fighting => Stage == "fight";

    /// <summary>The Missile objects the server created during the fight (the war casts' projectiles).</summary>
    public int Projectiles => _projectiles;

    /// <summary>The legs the loop walked.</summary>
    public int Walks => _walks;

    /// <summary>The seconds the fight took (to now while it runs), or NaN before it began.</summary>
    public double FightSeconds => double.IsNaN(_fightStart) || _fightStart == 0 ? double.NaN : (double.IsNaN(_fightEnd) ? _lastClock : _fightEnd) - _fightStart;

    private double _lastClock;

    /// <summary>The inner gear scenario (null when <see cref="FightStages.Gear"/> is off).</summary>
    public GearScenario? Gear => _gear;

    /// <summary>The cycle of the buff top-up (null until the top-up needed one).</summary>
    public BuffCycle? Cycle => _cycle;

    /// <summary>ACE's cast range for <paramref name="spell"/> at the skill's start level plus ranks <paramref name="initPlusRanks"/> (see the class remarks).</summary>
    /// <param name="spell">The spell.</param>
    /// <param name="initPlusRanks">The skill's start level plus its ranks.</param>
    public static float CastRange(SpellInfo spell, uint initPlusRanks)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return Math.Min(spell.BaseRangeConstant + (initPlusRanks * spell.BaseRangeMod), MaxRange);
    }

    /// <summary>
    /// D10's level: the highest of the first <see cref="Levels"/> spells of the war line that <paramref name="book"/> holds and whose ACE cast chance from the current War Magic skill (the buffed value) is at least
    /// <paramref name="castChance"/>; the buff cycle's chance code (<see cref="GearCasts.Chance"/>).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The player's spellbook.</param>
    /// <param name="castChance">The threshold (<see cref="BuffOptions.CastChance"/>).</param>
    /// <param name="skill">The War Magic skill the choice was made from.</param>
    /// <param name="why">Why there is none.</param>
    /// <returns>The level's index (0 for I), or -1.</returns>
    public static int ChooseLevel(PlayerQualities q, Spellbook book, double castChance, out uint skill, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        skill = 0;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out skill, out why))
        {
            return -1;
        }

        return ChooseLevel(skill, book, castChance, out why);
    }

    /// <summary>D10's level from a skill already known (the arena-here run's <see cref="FightOptions.ArenaSkill"/>); see the other overload.</summary>
    /// <param name="skill">The War Magic skill.</param>
    /// <param name="book">The player's spellbook, or null for none: the arena-here run (the offline stand-in does not send the spell-learn update, so its book never holds the levels the tool taught: <c>TODO(m3-standin-spellbook-updates)</c>) knows them all.</param>
    /// <param name="castChance">The threshold.</param>
    /// <param name="why">Why there is none.</param>
    /// <returns>The level's index (0 for I), or -1.</returns>
    public static int ChooseLevel(uint skill, Spellbook? book, double castChance, out string why)
    {
        if (ArenaProfile.LineNamed(ArenaProfile.WarLineName) is not { } line)
        {
            why = "the profile has no war line";
            return -1;
        }

        if (book is { IsKnown: false })
        {
            why = "the spellbook has not arrived (no PlayerDescription)";
            return -1;
        }

        for (var i = Math.Min(Levels, line.Spells.Count) - 1; i >= 0; i--)
        {
            if ((book is null || book.Knows(line.Spells[i].Id)) && GearCasts.Chance(skill, line.Spells[i].Power) >= castChance)
            {
                why = string.Empty;
                return i;
            }
        }

        why = string.Create(CultureInfo.InvariantCulture, $"no known {ArenaProfile.WarLineName} level reaches a chance of {castChance} at War Magic skill {skill}");
        return -1;
    }

    /// <summary>The verdict fields (see the headless line): the gear and buff stage notes, then the counters. No account or character name is in it.</summary>
    public string VerdictFields()
    {
        var seconds = FightSeconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Token(_gearFields, "gear")} {Token(_gearFields, "worn")} buffs={_buffsNote} kills={_tally.Kills} damage_dealt={_tally.DamageDealt} spells_cast={_tally.Casts} misses={_tally.Misses} xp_gained={_tally.XpGained} damage_taken={_tally.DamageTaken} hits_taken={_tally.HitsTaken} evades={_tally.Evades} level={Level} range_m={_range:0.#} projectiles={_projectiles} walks={_walks} fight_s={(double.IsNaN(seconds) ? "n/a" : seconds.ToString("0", CultureInfo.InvariantCulture))} target_kills={_tally.TargetKills}");
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _lastClock = session.Clock.Now;
        if (Failure is not null)
        {
            if (_failLogOff && _pending is { Outcome: { } left })
            {
                _pending = null;
                _failLogOff = false;
                _log.Info(LogCategory, "the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            }

            return _failLogOff ? ScenarioStatus.Running : ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        var clock = session.Clock.Now;
        switch (Stage)
        {
            case "start":
                Enter(_gear is not null ? "gear" : "buffs", clock);
                break;

            case "gear":
                switch (_gear!.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "gear: " + _gear.Failure);
                    case ScenarioStatus.Succeeded:
                        _gearFields = _gear.VerdictFields();
                        _log.Info(LogCategory, "gear done (" + _gearFields + ")");
                        Enter("buffs", clock);
                        break;
                }

                break;

            case "buffs":
                return BuffsCheck(session, clock);

            case "buffs-mode":
                if (_pending is { Outcome: { } magicForBuffs })
                {
                    _pending = null;
                    if (!magicForBuffs.Succeeded)
                    {
                        return Fail(session, "buffs: the magic combat mode was not entered: " + Describe(magicForBuffs));
                    }

                    if (!_cycle!.Start(clock))
                    {
                        return Fail(session, "buffs: the cycle did not start: " + (_cycle.Result?.Failure ?? "a pass is already running"));
                    }

                    Enter("buffs-cycle", clock);
                }

                break;

            case "buffs-cycle":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        return Fail(session, "buffs: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        var result = _cycle.Result;
                        _buffsNote = string.Create(CultureInfo.InvariantCulture, $"topped-up:{result?.Cast ?? 0}");
                        _log.Info(LogCategory, $"the buff top-up is done ({result?.Cast ?? 0} casts, {result?.Active ?? 0} of {result?.Wanted ?? 0} lines active)");
                        Enter("place", clock);
                        break;
                }

                break;

            case "place":
                return PlaceStep(session, clock);

            case "teleporting":
                return TeleportingStep(session, clock);

            case "settle":
                return SettleStep(session, clock);

            case "mode-magic":
                return ModeStep(session, clock);

            case "fight":
                return FightStep(session, now, clock);

            case "mode-peace":
                if (_pending is { Outcome: { } peace })
                {
                    _pending = null;
                    if (!peace.Succeeded)
                    {
                        _log.Warn(LogCategory, "the player was left in the magic combat mode: " + Describe(peace)); // report-only, as the buff scenario's
                    }

                    return Conclude(session);
                }

                break;

            case "logging-off":
                if (_pending is { Outcome: { } loggedOff })
                {
                    _pending = null;
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off: " + Describe(loggedOff));
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    Enter("quitting", clock);
                }

                break;

            case "quitting":
                if (_pending is { Outcome: { } quit })
                {
                    _pending = null;
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        return Fail(session, "quitting: " + Describe(quit));
                    }

                    _log.Info(LogCategory, "verdict " + VerdictFields());
                    Stage = "done";
                    Dispose();
                    return ScenarioStatus.Succeeded;
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown fight stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>Stops following the session (the tally, the handlers, the walk).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tally.Dispose();
        _diedSubscription?.Dispose();
        _createdSubscription?.Dispose();
        _movedSubscription?.Dispose();
        _walk?.Dispose();
    }

    private ScenarioStatus BuffsCheck(IGameSessionView session, double clock)
    {
        if ((_stages & FightStages.Buffs) == 0)
        {
            Enter("place", clock);
            return ScenarioStatus.Running;
        }

        _cycle ??= new BuffCycle(session, _spells, _buffOptions, _log);
        var due = _cycle.RebuffDue(clock, TopUpSeconds);
        if (due.Count == 0)
        {
            _buffsNote = "skipped";
            _log.Info(LogCategory, $"the buff top-up is skipped: all {_cycle.Wanted} profile lines have at least {TopUpSeconds:0} s left");
            Enter("place", clock);
            return ScenarioStatus.Running;
        }

        _log.Info(LogCategory, $"the buff top-up is needed: {due.Count} of {_cycle.Wanted} lines are missing or have under {TopUpSeconds:0} s left ({string.Join(';', due)})");
        _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        Enter("buffs-mode", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus PlaceStep(IGameSessionView session, double clock)
    {
        if ((_stages & FightStages.Place) == 0)
        {
            Enter("mode-magic", clock);
            return ScenarioStatus.Running;
        }

        Subscribe(session);
        _walk?.Prepare(session, clock);
        if (_walk?.Failure is { } walkFailure)
        {
            return Fail(session, "place: " + walkFailure);
        }

        _teleportMark = _selfTeleports;
        _place = session.Tools.Place(_destination, ActionSource.Host);
        _pending = _place;
        Enter("teleporting", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus TeleportingStep(IGameSessionView session, double clock)
    {
        if (_pending is { Outcome: { } placed })
        {
            _pending = null;
            if (placed.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, "place: sending the player to the den: " + Describe(placed));
            }
        }

        _walk?.Tick(session, clock);
        if (_pending is null && _selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)
        {
            _settleUntil = clock + SettleSeconds;
            Enter("settle", clock);
            return ScenarioStatus.Running;
        }

        return clock - _stageStart > TeleportSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"place: the player did not arrive in the den and become present within {TeleportSeconds:0.#} s (teleports {_selfTeleports - _teleportMark}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus SettleStep(IGameSessionView session, double clock)
    {
        if (_walk?.Tick(session, clock) == FightWalkStatus.Failed)
        {
            return Fail(session, "settle: the walk failed: " + _walk.Failure);
        }

        if (clock >= _settleUntil)
        {
            var cell = session.State.Self.Position?.CellId ?? 0;
            if (!BlackSpawnDen.IsFloorCell(cell))
            {
                // D12: the loop never fights from a cell off the floor; a settle that ends elsewhere is a loud failure, not a note (3.1-R4).
                return Fail(session, $"settle: the player settled in cell 0x{cell:X8}, which is not a floor cell of the den (floor cell no); the fight would start off the floor");
            }

            _log.Info(LogCategory, $"settled in cell 0x{cell:X8}; floor cell yes");
            Enter("mode-magic", clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus ModeStep(IGameSessionView session, double clock)
    {
        Subscribe(session);
        if (_pending is null)
        {
            if ((_stages & FightStages.Mode) == 0 || session.State.Self.Qualities.CombatMode == CombatModes.Magic)
            {
                _log.Info(LogCategory, "the player is in the magic combat mode");
                return BeginFight(session, clock);
            }

            _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        _walk?.Tick(session, clock);
        if (_pending.Outcome is { } mode)
        {
            _pending = null;
            if (!mode.Succeeded)
            {
                return Fail(session, "mode-magic: the magic combat mode was not entered: " + Describe(mode));
            }

            return BeginFight(session, clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus BeginFight(IGameSessionView session, double clock)
    {
        var self = session.State.Self;
        uint skill;
        string why;
        if (_options.ArenaHere)
        {
            skill = _options.ArenaSkill;
            _level = ChooseLevel(skill, null, _buffOptions.CastChance, out why); // the arena-here run does not read the spellbook
        }
        else
        {
            _level = ChooseLevel(self.Qualities, self.Spellbook, _buffOptions.CastChance, out skill, out why);
        }

        if (_level < 0)
        {
            return Fail(session, "fight: no Flame Bolt level to cast: " + why);
        }

        if (!_spells.TryGetValue(_levelIds[_level], out var info))
        {
            return Fail(session, $"fight: the SpellTable has no spell {_levelIds[_level]}");
        }

        uint initPlusRanks;
        if (_options.ArenaHere)
        {
            initPlusRanks = _options.ArenaSkill; // the stand-in's range is worked out from its fixed skill alone (StandInWorld.Combat.cs)
        }
        else if (self.Qualities.TryGetSkill(MageTemplate.WarMagic, out var war))
        {
            initPlusRanks = war.Init + war.Ranks;
        }
        else
        {
            return Fail(session, "fight: the read model has no War Magic skill");
        }

        _range = CastRange(info, initPlusRanks);
        _manaNeeded = GearCasts.ManaCeiling(info, 0);
        _tally.Start(session);
        _fightStart = clock;
        _target = 0;
        _missesInARow = 0;
        _phase = Phase.Choose;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the fight begins: {info.Name} ({info.Id}) at chance {GearCasts.Chance(skill, info.Power):0.0000} from War Magic {skill}, range {_range:0.#} m (init+ranks {initPlusRanks}, base {info.BaseRangeConstant} + {info.BaseRangeMod} per point), mana up to {_manaNeeded}; {_options.Kills} kill(s) within {_options.Seconds:0.#} s"));
        Enter("fight", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus FightStep(IGameSessionView session, double now, double clock)
    {
        if (_died)
        {
            return Fail(session, "fight: the player died (" + _deathText + ")");
        }

        var walkStatus = _walk?.Tick(session, now) ?? FightWalkStatus.Idle;
        if (walkStatus == FightWalkStatus.Failed)
        {
            // Any phase: the walker left the floor, or a leg failed (a leg's end is only read in the Walk phase, but the floor check runs every frame).
            return Fail(session, "fight: the walk failed: " + _walk!.Failure);
        }

        if (_tally.Kills >= _options.Kills)
        {
            // The judge wants damage_dealt > 0 and ACE writes no damage line for the killing blow: a first hit that kills leaves it 0, so the fight goes on (bounded by the seconds limit) until a damage line (3.1-R1).
            if (_tally.DamageDealt > 0)
            {
                return EndFight(session, clock);
            }

            if (!_damageWaitLogged)
            {
                _damageWaitLogged = true;
                _log.Info(LogCategory, $"kills={_tally.Kills} of {_options.Kills} reached but damage_dealt=0 (ACE writes no damage line for the killing blow); fighting on until a damage line, within the seconds limit");
            }
        }

        if (clock - _fightStart > _options.Seconds)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"fight: {_options.Seconds:0.#} s passed with kills={_tally.Kills} of {_options.Kills} and damage_dealt={_tally.DamageDealt} (casts={_tally.Casts} misses={_tally.Misses} walks={_walks} phase={_phase})"));
        }

        if (_health is { Outcome: not null })
        {
            LogHealth(session);
        }

        switch (_phase)
        {
            case Phase.Choose:
                return Choose(session, clock);

            case Phase.Mana:
                if (ManaShort(session))
                {
                    return ScenarioStatus.Running;
                }

                _phase = Phase.Choose;
                return ScenarioStatus.Running;

            case Phase.Cast:
                return CastAnswer(session, clock);

            case Phase.Hit:
                return HitWait(session, clock);

            case Phase.DeadWait:
                if (_tally.Kills > _killMark || clock - _deadSince > KillNoticeSeconds)
                {
                    if (_tally.Kills > _killMark)
                    {
                        NoteKill();
                    }
                    else
                    {
                        _log.Warn(LogCategory, $"the target 0x{_target:X8} died but no kill notice came within {KillNoticeSeconds:0.#} s; it is not counted");
                    }

                    _phase = Phase.Choose;
                }

                return ScenarioStatus.Running;

            case Phase.Walk:
                switch (walkStatus)
                {
                    case FightWalkStatus.Arrived:
                        _log.Info(LogCategory, "the leg is done");
                        _missesInARow = 0;
                        _phase = Phase.Choose;
                        break;
                    case FightWalkStatus.Idle:
                        return Fail(session, "fight: the walk stopped without arriving");
                }

                return ScenarioStatus.Running;

            default:
                return Fail(session, $"TODO(state): unknown fight phase {_phase}");
        }
    }

    /// <summary>
    /// Picks the nearest live tusker on the floor in range (<see cref="WorldObjectTable"/>'s nearest-creature query) and casts at it. Retail picks its attack target in <c>ClientCombatSystem::GetAttackTarget</c>
    /// (11.6096 0x0056B630, 11.4186 0x0056A8F0); the loop has no selection, so it stands in for it, and the server stays the judge.
    /// </summary>
    private ScenarioStatus Choose(IGameSessionView session, double clock)
    {
        var self = session.State.Self;
        if (self.Position is not { } here)
        {
            return ScenarioStatus.Running;
        }

        if (!session.State.Objects.NearestCreature(ArenaProfile.TargetWcids, in here, _range, _cellFilter, _killed, out var guid))
        {
            return StartWalk(session, clock, "no tusker is in range on the floor");
        }

        if (guid != _target)
        {
            _target = guid;
            _tally.SetTarget(session.State.Objects.TryGet(guid, out var chosen) ? chosen.Name : null);
            _log.Info(LogCategory, $"target 0x{guid:X8}");
        }

        if (ManaShort(session))
        {
            _phase = Phase.Mana;
            return ScenarioStatus.Running;
        }

        _damageMark = _tally.DamageLinesDealt;
        _killMark = _tally.Kills;
        _targetKillMark = _tally.TargetKills;
        _pending = session.Actions.Cast(_levelIds[_level], _target, ActionSource.Host);
        _tally.NoteCast();
        _phase = Phase.Cast;
        return ScenarioStatus.Running;
    }

    /// <summary>True (and logs once) while the read model's mana is under what the cast can cost.</summary>
    private bool ManaShort(IGameSessionView session)
    {
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || mana >= _manaNeeded)
        {
            _manaLogged = false;
            return false;
        }

        if (!_manaLogged)
        {
            _manaLogged = true;
            _log.Info(LogCategory, $"waiting for mana: the cast needs up to {_manaNeeded}, the player has {mana}");
        }

        return true;
    }

    private ScenarioStatus CastAnswer(IGameSessionView session, double clock)
    {
        if (_pending is not { Outcome: { } outcome })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            // The projectile exists. A hit shows as a damage line, a kill as the 0x01AD notice, within HitWaitSeconds (the marks were taken when the cast was sent: a close target can be hit in the frame the outcome arrives).
            _awaitSince = clock;
            _phase = Phase.Hit;
            return ScenarioStatus.Running;
        }

        _tally.NoteMiss();
        _missesInARow++;
        var reason = Describe(outcome);
        _log.Info(LogCategory, $"miss: the cast was not confirmed: {reason} (misses in a row {_missesInARow})");
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == FizzleCode && !_fizzleDropped)
        {
            DropOneLevel();
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == MissileOutOfRange)
        {
            return StartWalk(session, clock, "the server answered MissileOutOfRange (0x0550)");
        }

        if (_missesInARow >= MissesBeforeWalking)
        {
            return StartWalk(session, clock, $"{_missesInARow} misses in a row");
        }

        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus HitWait(IGameSessionView session, double clock)
    {
        if (_tally.Kills > _killMark)
        {
            _log.Info(LogCategory, $"kill {_tally.Kills}: {_tally.LastKillText}");
            NoteKill();
            _missesInARow = 0;
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        if (_tally.DamageLinesDealt > _damageMark)
        {
            _missesInARow = 0;
            _health = session.Actions.QueryHealth(_target, ActionSource.Host);
            _phase = Phase.Choose;
            return ScenarioStatus.Running;
        }

        if (TargetGone(session.State.Objects, _target))
        {
            _deadSince = clock;
            _log.Info(LogCategory, $"the target 0x{_target:X8} is dead or gone; waiting for the kill notice");
            _phase = Phase.DeadWait;
            return ScenarioStatus.Running;
        }

        if (clock - _awaitSince > HitWaitSeconds)
        {
            _tally.NoteMiss();
            _missesInARow++;
            _log.Info(LogCategory, $"miss: the projectile was confirmed but no damage line or kill came within {HitWaitSeconds:0.#} s (misses in a row {_missesInARow})");
            if (_missesInARow >= MissesBeforeWalking)
            {
                return StartWalk(session, clock, $"{_missesInARow} misses in a row");
            }

            _phase = Phase.Choose;
        }

        return ScenarioStatus.Running;
    }

    /// <summary>
    /// A kill notice arrived for the cast in flight. When its text names the target, the target is never picked again: ACE sends the 0x01AD before the Dead motion and keeps the body for its death animation, so the
    /// read model still holds a creature with a live-looking health and motion (3.1-R2). A notice that does not name the target (3.1-R7) still counts toward the total kills, is logged, and leaves the target pickable.
    /// </summary>
    private void NoteKill()
    {
        if (_tally.TargetKills > _targetKillMark)
        {
            if (!_killed.Contains(_target))
            {
                _killed.Add(_target);
            }

            return;
        }

        _log.Info(LogCategory, $"the kill notice does not name the target 0x{_target:X8}; it counts in kills but not in target_kills");
    }

    private ScenarioStatus StartWalk(IGameSessionView session, double clock, string why)
    {
        if (_walk is null)
        {
            return Fail(session, "fight: the loop must walk (" + why + ") but has no walker; TODO(m3-fight-no-walker)");
        }

        if (!_walk.BeginLeg(session, clock, out var leg))
        {
            return Fail(session, "fight: no walk could be planned (" + why + "): " + _walk.Failure);
        }

        _walks++;
        _missesInARow = 0;
        _target = 0;
        _log.Info(LogCategory, $"walking: {why}; {leg}");
        _phase = Phase.Walk;
        return ScenarioStatus.Running;
    }

    /// <summary>A fizzle drops one level once (D10, as the buff cycle's retry): the next lower level the spellbook holds.</summary>
    private void DropOneLevel()
    {
        _fizzleDropped = true;
        for (var i = _level - 1; i >= 0; i--)
        {
            if (_spells.TryGetValue(_levelIds[i], out var info))
            {
                _level = i;
                _manaNeeded = GearCasts.ManaCeiling(info, 0);
                _log.Info(LogCategory, $"a fizzle: one level down, to {info.Name} ({info.Id})");
                return;
            }
        }

        _log.Warn(LogCategory, "a fizzle on the lowest level; there is no level to drop to");
    }

    private void LogHealth(IGameSessionView session)
    {
        _health = null;
        var objects = session.State.Objects;
        _log.Info(LogCategory, objects.HealthOf(_target, out var fraction) ? string.Create(CultureInfo.InvariantCulture, $"target 0x{_target:X8} health {fraction:0.00}") : $"target 0x{_target:X8} health unknown");
    }

    private static bool TargetGone(WorldObjectTable objects, uint guid) =>
        !objects.TryGet(guid, out var o) || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord };

    private ScenarioStatus EndFight(IGameSessionView session, double clock)
    {
        _fightEnd = clock;
        _log.Info(LogCategory, $"the fight is won: {VerdictFields()}");
        _walk?.Abandon();
        _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        Enter("mode-peace", clock);
        return ScenarioStatus.Running;
    }

    private void Subscribe(IGameSessionView session)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _diedSubscription = session.Events.Subscribe<PlayerDied>(OnDied);
        _createdSubscription = session.Events.Subscribe<ObjectCreated>(OnCreated);
        _movedSubscription = session.Events.Subscribe<ObjectMoved>(OnMoved);
        _view = session;
    }

    private IGameSessionView? _view;

    private void OnDied(in PlayerDied e)
    {
        _died = true;
        _deathText = e.Text;
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (e.IsSelf && e.Verdict == PositionVerdict.SelfTeleport)
        {
            _selfTeleports++;
        }
    }

    private void OnCreated(in ObjectCreated e)
    {
        if (e.IsSelf || Stage != "fight" || _view is null || !_view.State.Objects.TryGet(e.Guid, out var o) || (o.State & PhysicsState.Missile) == 0)
        {
            return;
        }

        _projectiles++;
        _log.Info(
            LogCategory,
            "projectile created",
            new LogField("guid", $"0x{e.Guid:X8}"),
            new LogField("setup", $"0x{o.Physics.SetupId ?? 0:X8}"),
            new LogField("cell", o.Position is { } p ? $"0x{p.CellId:X8}" : "none"),
            new LogField("at", o.Position?.Frame.Origin),
            new LogField("velocity", o.Velocity));
    }

    private ScenarioStatus Conclude(IGameSessionView session)
    {
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            Stage = "done";
            Dispose();
            return ScenarioStatus.Succeeded;
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        Enter("logging-off", session.Clock.Now);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure = why;
        _fightEnd = double.IsNaN(_fightEnd) ? session.Clock.Now : _fightEnd;
        _walk?.Abandon();
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        if (_endSession && session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // As the buff scenario's failure: leave the world before the run ends as failed, so the next login is not refused as CharacterInWorld.
            _log.Info(LogCategory, "logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _failLogOff = true;
            return ScenarioStatus.Running;
        }

        Dispose();
        return ScenarioStatus.Failed;
    }

    private void Enter(string stage, double clock)
    {
        Stage = stage;
        _stageStart = clock;
        _log.Info(LogCategory, "stage " + stage);
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    /// <summary>The <c>name=value</c> token of a verdict line (the gear scenario's), or <c>name=?</c>.</summary>
    private static string Token(string fields, string name)
    {
        var key = name + "=";
        var at = fields.StartsWith(key, StringComparison.Ordinal) ? 0 : fields.IndexOf(" " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return key + "?";
        }

        if (at > 0)
        {
            at++;
        }

        var end = fields.IndexOf(' ', at);
        return end < 0 ? fields[at..] : fields[at..end];
    }
}
