using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M3 2.1's buff scenario (<c>--scenario=buff</c>, plan section 4.5 s1; BSD Mage and nobody else): enter the mage and have the gear on (<see cref="GearScenario"/> with <c>endSession: false, cast: false</c>), enter the magic
/// combat mode, run the buff cycle (<see cref="BuffCycle.Start"/>: the ordered pass and its upgrade pass), run the forced re-buff pass (decision D25 as narrowed: the one line closest to expiry is recast while still
/// active), return to non-combat, log off and quit. With <c>endSession: false</c> (the tests, and the game's buff leg of s3, which owns the session) it ends at <c>done</c> in the world.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>start</c>, <c>gear</c> (login, entering and the gear are the inner scenario's), <c>mode-magic</c>, <c>cycle</c>, <c>rebuff</c> (or <c>driver</c>, see below), <c>mode-peace</c>, <c>logging-off</c>,
/// <c>quitting</c>, <c>done</c>.</para>
/// <para><b>The fighters' buff leg</b> (M4 P5a C4; the constructor over a <see cref="FighterTemplate"/>: Holtburg Brawler and Holtburg Archer). The same cycle over the fighter's <see cref="FighterProfile"/> (twelve buff lines; the mana lines are the profile's
/// Stamina to Mana Self and Revitalize Self, so a pass whose next cast needs more mana than the fighter holds transfers stamina to mana, and waits for regeneration only as the cycle's bounded last resort, <see cref="BuffOptions.ManaWaitSeconds"/>).
/// Stages: <c>start</c>, <c>login</c> (skipped in the world; the character is the template's and nobody else), <c>swap-in</c> (<see cref="CasterSwap"/>: the weapon and ammunition to the pack, the caster wielded, the magic combat mode; there is <b>no gear step</b>, the fighter keeps its
/// kit and weapon), <c>cycle</c>, <c>rebuff</c> (or <c>driver</c>), <c>swap-out</c> (peace, the caster to the pack, the weapon and ammunition wielded again), <c>logging-off</c>, <c>quitting</c>, <c>done</c>. The swap runs once for the whole run, not once per pass. A failure after the swap in runs
/// the swap's Out half before the run logs off, so the weapon is in hand and the player in peace whatever failed. The verdict keeps the mage's fields (<c>gear=n/a worn=n/a</c>: no gear step ran) and ends with <c>swapped=yes|no swap_s=&lt;s&gt;</c> (and <c>swap_why=</c> after a failure).</para>
/// <para><b>With <c>--buff-auto on</c></b> (<see cref="BuffOptions.Auto"/>, M4 4.1 lane c; DoD 14) there is <b>no forced pass</b>: after the cycle the scenario sets the margin of a <see cref="RebuffDriver"/> from the least time left on an
/// active line (<see cref="BuffCycle.MinRemainingSeconds"/> less <see cref="DriverLeadSeconds"/>, never below <see cref="DriverMinMarginSeconds"/>), so the driver's own pass comes within about <see cref="DriverLeadSeconds"/> of session
/// time, and ticks the driver (stage <c>driver</c>) until one pass ends. The verdict then reads <c>rebuffed=</c> from the driver's pass, <c>forced=no</c>, and adds <c>driver=on rebuff_lines=&lt;lines the pass cast&gt;
/// rebuff_s=&lt;session seconds from the cycle's end to the pass's end&gt;</c>. The success rule is the same with the driver's pass in the forced pass's place (every line active, none missed, the pass recast a line while it was still active).
/// The driver's chat notices go to <see cref="Notice"/> (the session's chat when it is null).</para>
/// <para><b>Success</b> means the gear is on, the cycle and the forced pass ended without a failure, every wanted line is active, no line was missed and the forced pass recast a line while it was still active. Anything
/// else fails the run: a failure at any stage, in the world, logs off before it is reported, as the gear scenario does (M3 1.4 fix A2; the headless host's <c>headless-failed-logoff</c>), so ACE does not keep the character in the
/// world for the next run.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[buff] verdict ...</c>, printed by the headless host as <c>buff ...</c>): <c>gear=wielded|failed worn=&lt;n&gt; buffs=&lt;active&gt;/&lt;wanted&gt;
/// levels=&lt;line:level,..&gt; rebuffed=yes|no forced=yes|no min_remaining_s=&lt;s&gt; transfers=&lt;fired&gt;/&lt;tried&gt; revitalizes=&lt;n&gt; fizzles=&lt;n&gt; mana_min=&lt;n&gt; cycle_s=&lt;s&gt; misses=&lt;names|none&gt;</c>.
/// <c>buffs</c>, <c>levels</c> and <c>min_remaining_s</c> are what the read model holds when the last pass ended; the counters are the cycle's, cumulative; <c>forced=yes</c> means the forced pass landed a recast;
/// <c>cycle_s</c> is the ordered pass's session seconds; <c>misses</c> are the lines either pass missed. No account or character name is in it.</para>
/// <para><b>A second run.</b> <see cref="CycleCast"/> is what the ordered pass cast (the forced pass always casts one line, so it is counted apart). A cycle object that already ran (passed in as <c>cycle</c>) casts nothing
/// the second time; so does a new object on the same session, since the scenario hands every cycle it makes the session's <see cref="ArmourBuffLedger"/> (M4 4.1 lane b; closes <c>TODO(m3-buff-armour-memory)</c>), which also holds what the appraisal
/// of the worn pieces found after a login. <see cref="CycleArmourCast"/> counts the armour lines the ordered pass cast: none on a known ledger. Given an <see cref="IArmourLedgerStore"/> the scenario keeps the ledger's remaining seconds
/// at the end of the run (ACE's clock for an item enchantment stops while its owner is offline, <see cref="ArmourBuffLedger"/>); the buff leg passes the file store.</para>
/// </remarks>
public sealed class BuffScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=buff</c>).</summary>
    public const string ScenarioName = "buff";

    /// <summary>With the driver on, the seconds before the shortest line would end at which the driver's margin puts its pass (the margin is the least time left less this).</summary>
    public const double DriverLeadSeconds = 30;

    /// <summary>The least margin the driver run uses, in seconds.</summary>
    public const double DriverMinMarginSeconds = 1;

    /// <summary>The log category of the scenario's lines (<c>[buff] ...</c>, the cycle's category too).</summary>
    public const string LogCategory = BuffCycle.LogCategory;

    private readonly GearScenario _gear; // inert in the fighters' leg (never stepped)
    private readonly MageSpellBars? _bars;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly BuffOptions _options;
    private readonly bool _endSession;
    private readonly ILogSink _log;
    private readonly FighterTemplate? _fighter;
    private readonly IBuffProfile? _profile;
    private readonly LoginScenario? _login;
    private int _wanted;
    private readonly ArmourBuffLedger? _ledger;
    private readonly IArmourLedgerStore? _store;
    private BuffCycle? _cycle;
    private ActionHandle? _pending;
    private BuffResult? _first;
    private BuffResult? _final;
    private RebuffDriver? _driver;
    private RebuffPassNotices? _notices;
    private BuffResult? _driverResult;
    private double _cycleEndedAt = double.NaN;
    private double _driverEndedAt = double.NaN;
    private double _driverMargin = double.NaN;
    private bool _forcedLanded;
    private bool _failLogOff;
    private CasterSwap? _swap;
    private VitalsKeeper? _keeper;
    private readonly VitalsOptions _vitalsOptions = VitalsOptions.Default;
    private bool _needRestore;
    private bool _failRestore;
    private string _gearFields = "gear=failed worn=0";

    /// <summary>The buff scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components, the casts' costs, forms and durations.</param>
    /// <param name="account">The account name the kit is built for (hashed by the kit, never stored, logged or printed).</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the scenario and the cycle log.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="cycle">A cycle object that already ran on this session (a second run casts nothing), or null for a new one.</param>
    /// <param name="bars">The spell bars step run after the gear (M3 4.2 p1), or null to leave the bars alone.</param>
    /// <param name="ledger">The armour lines' memory handed to the cycle (M4 4.1 lane b), or null for the session's (<see cref="ArmourBuffLedger.For"/>).</param>
    /// <param name="store">Where the ledger's remaining seconds are kept across a log-off, or null (the conservative rule).</param>
    public BuffScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, BuffOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, BuffCycle? cycle = null, MageSpellBars? bars = null, ArmourBuffLedger? ledger = null, IArmourLedgerStore? store = null)
        : this(GearKit.Build(spells ?? throw new ArgumentNullException(nameof(spells)), account), spells, options, log, arrivalSeconds, endSession, cycle, bars, ledger, store)
    {
    }

    /// <summary>The buff scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the scenario and the cycle log.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="cycle">A cycle object that already ran on this session, or null for a new one.</param>
    /// <param name="bars">The spell bars step run after the gear (M3 4.2 p1: tab I the buffs, tab II the fight), or null to leave the bars alone (the tests').</param>
    /// <param name="ledger">The armour lines' memory handed to the cycle (M4 4.1 lane b), or null for the session's (<see cref="ArmourBuffLedger.For"/>).</param>
    /// <param name="store">Where the ledger's remaining seconds are kept across a log-off, or null (the conservative rule).</param>
    public BuffScenario(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, BuffCycle? cycle = null, MageSpellBars? bars = null, ArmourBuffLedger? ledger = null, IArmourLedgerStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _spells = spells;
        _options = options;
        _log = log;
        _endSession = endSession;
        _cycle = cycle;
        _bars = bars;
        _ledger = ledger;
        _store = store;
        _gear = new GearScenario(kit, log, arrivalSeconds, endSession: false, cast: false, spells);
        foreach (var line in ArenaProfile.Lines)
        {
            if (line.IsBuff)
            {
                _wanted++;
            }
        }
    }

    /// <summary>
    /// The buff scenario for a fighter (M4 P5a C4): the Brawler's or the Archer's buff leg over its <see cref="FighterProfile"/> and the <see cref="CasterSwap"/> (see the class remarks); no gear step.
    /// </summary>
    /// <param name="fighter">The fighter template (one with a caster in its kit and a buff profile: the Brawler or the Archer).</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="options">The cycle's options.</param>
    /// <param name="log">Where the scenario and the cycle log.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="cycle">A cycle object that already ran on this session, or null for a new one.</param>
    /// <param name="ledger">The armour lines' memory handed to the cycle, or null for the session's.</param>
    /// <param name="store">Where the ledger's remaining seconds are kept across a log-off, or null.</param>
    public BuffScenario(FighterTemplate fighter, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions options, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, BuffCycle? cycle = null, ArmourBuffLedger? ledger = null, IArmourLedgerStore? store = null)
        : this(GearKit.Of([]), spells, options, log, arrivalSeconds, endSession, cycle, null, ledger, store)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        if (!fighter.Kit.HasCaster || BuffProfiles.For(fighter.Name) is not { } profile)
        {
            throw new ArgumentException($"{fighter.Key}: the template has no caster and no buff profile (only the Brawler and the Archer buff themselves)", nameof(fighter));
        }

        _fighter = fighter;
        _profile = profile;
        _wanted = profile.Lines.Count(l => l.IsBuff);
        _login = new LoginScenario(fighter.Name, null, null, arrivalSeconds) { StayInWorld = true };
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <summary>The fighter the run is for (null for the mage's).</summary>
    public FighterTemplate? Fighter => _fighter;

    /// <summary>The caster swap of a fighter's run (null for the mage's, and before the run starts).</summary>
    public CasterSwap? Swap => _swap;

    /// <summary>Where the driver's "[Lifestone] re-buffing ..." and "re-buffed n lines" notices go (without the prefix; the session's chat when null). Set at construction.</summary>
    public Action<string>? Notice { get; init; }

    /// <summary>True when the run uses the re-buff driver in place of the forced pass (<c>--buff-auto on</c>).</summary>
    public bool DriverOn => _options.Auto;

    /// <summary>What the driver's pass left (null before it ended, and without the driver).</summary>
    public BuffResult? DriverResult => _driverResult;

    /// <summary>The margin the driver ran with (NaN before the cycle ended, and without the driver).</summary>
    public double DriverMarginSeconds => _driverMargin;

    /// <summary>The lines the driver's pass cast (0 before it ended).</summary>
    public int DriverLines => _driverResult?.Cast ?? 0;

    /// <summary>The session seconds from the cycle's end to the driver pass's end (NaN before it ended).</summary>
    public double DriverRebuffSeconds => double.IsNaN(_driverEndedAt) || double.IsNaN(_cycleEndedAt) ? double.NaN : _driverEndedAt - _cycleEndedAt;

    /// <summary>What the spell bars step ended with (<c>set:&lt;n&gt;</c>, <c>ok</c>), or <c>n/a</c> when it did not run.</summary>
    public string BarsNote => _bars?.Note ?? "n/a";

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The inner gear scenario (the tests read its steps).</summary>
    public GearScenario Gear => _gear;

    /// <summary>True (a shared session, M4 P4): a player already in the world must be BSD Mage, else the run fails at once and sends nothing (<see cref="GearScenario.RequireCharacter"/>).</summary>
    public bool RequireCharacter
    {
        get => _gear.RequireCharacter;
        init => _gear.RequireCharacter = value;
    }

    /// <summary>The cycle object (null before the gear is done).</summary>
    public BuffCycle? Cycle => _cycle;

    /// <summary>The buff lines the ordered pass cast and landed (its upgrade casts included), or -1 before it ended; the forced pass is not counted.</summary>
    public int CycleCast => _first?.Cast ?? -1;

    /// <summary>Of <see cref="CycleCast"/>, the armour lines.</summary>
    public int CycleArmourCast => _first?.ArmourCast ?? 0;

    /// <summary>What the ordered pass left (null before it ended).</summary>
    public BuffResult? CycleResult => _first;

    /// <summary>What the forced pass left (null before it ended).</summary>
    public BuffResult? ForcedResult => _final;

    /// <summary>The verdict fields (see the class remarks); a run that never reached the cycle reports zeros and <c>n/a</c>.</summary>
    public string VerdictFields()
    {
        var best = _driverResult ?? _final ?? _first ?? _cycle?.Result;
        var misses = new List<string>();
        if (_first is not null)
        {
            misses.AddRange(_first.Misses);
        }

        if (best is not null && !ReferenceEquals(best, _first))
        {
            foreach (var miss in best.Misses)
            {
                if (!misses.Contains(miss))
                {
                    misses.Add(miss);
                }
            }
        }

        var gear = _fighter is null ? _gear.VerdictFields() : "gear=n/a worn=n/a"; // the fighters' leg has no gear step
        var min = best is null || double.IsNaN(best.MinRemainingSeconds) ? "n/a" : Math.Floor(best.MinRemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
        var mana = best?.ManaMin is { } m ? m.ToString(CultureInfo.InvariantCulture) : "n/a";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Token(gear, "gear")} {Token(gear, "worn")} buffs={best?.Active ?? 0}/{best?.Wanted ?? _wanted} levels={(best is null ? "n/a" : best.Levels)} rebuffed={(best?.Rebuffed == true ? "yes" : "no")} forced={(_forcedLanded ? "yes" : "no")} min_remaining_s={min} transfers={best?.TransfersFired ?? 0}/{best?.TransfersTried ?? 0} revitalizes={best?.Revitalizes ?? 0} fizzles={best?.Fizzles ?? 0} mana_min={mana} cycle_s={Math.Floor(_first?.CycleSeconds ?? 0).ToString("0", CultureInfo.InvariantCulture)} misses={(misses.Count == 0 ? "none" : string.Join(';', misses))} surpassed={_first?.Surpassed ?? 0}{DriverFields()}{SwapVerdict()}");
    }

    private string SwapVerdict() => _fighter is null ? string.Empty : " " + (_swap?.Fields ?? "swapped=no swap_s=0.0");

    private string DriverFields() =>
        DriverOn
            ? string.Create(CultureInfo.InvariantCulture, $" driver=on rebuff_lines={DriverLines} rebuff_s={(double.IsNaN(DriverRebuffSeconds) ? "n/a" : Math.Floor(DriverRebuffSeconds).ToString("0", CultureInfo.InvariantCulture))}")
            : string.Empty;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null)
        {
            if (_failRestore)
            {
                // The fighters' leg: the swap's Out half runs first, so the weapon is in hand and the player in peace when the run logs off.
                switch (_swap!.Step(session.Clock.Now))
                {
                    case SwapStatus.Running:
                        return ScenarioStatus.Running;
                    case SwapStatus.Failed:
                        _log.Warn(LogCategory, "the caster swap back failed (" + _swap.Failure + "): " + _swap.Detail);
                        break;
                }

                _failRestore = false;
                return FinishFail(session);
            }

            // A failed run in the world logs off first (see Fail), so ACE does not keep the character in the world for the next run.
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
                if (_fighter is not null)
                {
                    return StartFighter(session);
                }

                Stage = "gear";
                break;

            case "login":
                switch (_login!.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "login: " + _login.Failure);
                    case ScenarioStatus.Succeeded:
                        return BeginVitals(session, clock);
                }

                break;

            case "vitals":
                switch (_keeper!.Step(clock))
                {
                    case VitalsKeeperState.Ready:
                        _log.Info(LogCategory, "precondition met: " + _keeper.VerdictFields());
                        return BeginSwapIn(session, clock);
                    case VitalsKeeperState.Blocked:
                        var vital = _keeper.Why is { } w && w.Contains("mana", StringComparison.Ordinal) ? "mana" : _keeper.Why is { } h && h.Contains("health", StringComparison.Ordinal) ? "health" : "stamina";
                        return Fail(session, $"precondition:{vital}: the vitals keeper could not restore the vitals the pass starts with ({_keeper.Why}; {_keeper.VerdictFields()})");
                }

                break;

            case "swap-in":
                switch (_swap!.Step(clock))
                {
                    case SwapStatus.Failed:
                        return Fail(session, "swap-in: " + _swap.Failure + (_swap.Detail is null ? string.Empty : " (" + _swap.Detail + ")")); // the failed In half restored the weapon itself
                    case SwapStatus.Done:
                        _needRestore = true;
                        if (StartCycle(session, clock) is { } notStarted)
                        {
                            return Fail(session, notStarted);
                        }

                        break;
                }

                break;

            case "swap-out":
                switch (_swap!.Step(clock))
                {
                    case SwapStatus.Failed:
                        return Fail(session, "swap-out: " + _swap.Failure + (_swap.Detail is null ? string.Empty : " (" + _swap.Detail + ")"));
                    case SwapStatus.Done:
                        return Conclude(session);
                }

                break;

            case "gear":
                switch (_gear.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "gear: " + _gear.Failure);
                    case ScenarioStatus.Succeeded:
                        _gearFields = _gear.VerdictFields();
                        _log.Info(LogCategory, "gear done (" + _gearFields + "): " + (_bars is null ? "magic combat mode next" : "the spell bars next"));
                        if (_bars is null)
                        {
                            _pending = actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
                            Stage = "mode-magic";
                        }
                        else
                        {
                            Stage = "bars";
                        }

                        break;
                }

                break;

            case "bars":
                switch (_bars!.Step(session, clock))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "bars: " + _bars.Failure);
                    case ScenarioStatus.Succeeded:
                        _pending = actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
                        Stage = "mode-magic";
                        break;
                }

                break;

            case "mode-magic":
                if (_pending is { Outcome: { } magic })
                {
                    _pending = null;
                    if (!magic.Succeeded)
                    {
                        return Fail(session, "mode-magic: the magic combat mode was not entered: " + Describe(magic));
                    }

                    if (StartCycle(session, clock) is { } notStarted)
                    {
                        return Fail(session, notStarted);
                    }
                }

                break;

            case "cycle":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        _first = _cycle.Result;
                        return Fail(session, "cycle: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        _first = _cycle.Result;
                        if (_options.Auto)
                        {
                            StartDriver(session, clock);
                            break;
                        }

                        _log.Info(LogCategory, "the ordered cycle is done (" + CycleCast + " casts); the forced re-buff pass next (D25)");
                        if (!_cycle.StartRebuff(clock, forced: true))
                        {
                            return Fail(session, "rebuff: it did not start");
                        }

                        Stage = "rebuff";
                        break;
                }

                break;

            case "rebuff":
                switch (_cycle!.Step(clock))
                {
                    case BuffStatus.Failed:
                        _final = _cycle.Result;
                        return Fail(session, "rebuff: " + _cycle.Result?.Failure);
                    case BuffStatus.Done:
                        _final = _cycle.Result;
                        _forcedLanded = _final is { Forced: true, Cast: >= 1, RecastWhileActive: >= 1 };
                        var judged = Judge();
                        if (judged is not null)
                        {
                            return Fail(session, judged);
                        }

                        return ToPeace(session, clock);
                }

                break;

            case "driver":
                _driver!.Tick(clock);
                _notices!.Observe(clock);
                if (_driver.Passes > 0 && _driver.State != RebuffDriverState.Running)
                {
                    _driverResult = _driver.LastResult;
                    _driverEndedAt = clock;
                    var driverFailure = _driver.LastFailure;
                    _driver.Dispose();
                    if (driverFailure is not null)
                    {
                        return Fail(session, "driver: the pass did not succeed: " + driverFailure);
                    }

                    var driverJudged = Judge();
                    if (driverJudged is not null)
                    {
                        return Fail(session, driverJudged);
                    }

                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"the driver's pass ended {DriverRebuffSeconds:0} s after the cycle: {DriverLines} lines cast"));
                    return ToPeace(session, clock);
                }

                break;

            case "mode-peace":
                if (_pending is { Outcome: { } peace })
                {
                    _pending = null;
                    if (!peace.Succeeded)
                    {
                        _log.Warn(LogCategory, "the player was left in the magic combat mode: " + Describe(peace)); // report-only, as the gear scenario's
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
                    Stage = "quitting";
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
                    return ScenarioStatus.Succeeded;
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown buff stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    /// <summary>
    /// After the ordered cycle, with the driver on: sets the driver's margin from the shortest line (its remaining time less <see cref="DriverLeadSeconds"/>, at least <see cref="DriverMinMarginSeconds"/>; the options' margin when no line is
    /// active) and makes the driver, enabled, for the <c>driver</c> stage to tick. No forced pass is made.
    /// </summary>
    private void StartDriver(IGameSessionView session, double clock)
    {
        _cycleEndedAt = clock;
        var left = _cycle!.MinRemainingSeconds(clock);
        _driverMargin = double.IsNaN(left) ? _options.RebuffMarginSeconds : Math.Max(DriverMinMarginSeconds, left - DriverLeadSeconds);
        var options = _options with { RebuffMarginSeconds = _driverMargin, Auto = true };
        // The cycle's re-buff pass uses the margin of the options it was made with (the scenario's, 300 s by default), so the driver gets a cycle of its own made with the driver's margin; it knows the buffs from the read model
        // and the armour lines from the session's ledger, the way any second cycle on a session does.
        var driverCycle = new BuffCycle(session, _spells, options, _log, _ledger ?? ArmourBuffLedger.For(session.State), _store, _profile);
        _driver = new RebuffDriver(driverCycle, session.Actions, session, options, _log, _swap); // a fighter is in the magic mode with the caster wielded: the driver's own swap finds nothing to do
        _notices = new RebuffPassNotices(_driver, driverCycle, _driverMargin, Notice ?? LifestoneNotices.ChatSink(session));
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the ordered cycle is done ({CycleCast} casts); no forced pass (--buff-auto on): the re-buff driver next, with a margin of {_driverMargin:0} s (the least time left is {(double.IsNaN(left) ? "n/a" : left.ToString("0", CultureInfo.InvariantCulture))} s, less {DriverLeadSeconds:0} s)"));
        Stage = "driver";
    }

    /// <summary>The fighters' start: the character in the world must be the template's (nothing is sent for another); not in the world: the login.</summary>
    private ScenarioStatus StartFighter(IGameSessionView session)
    {
        if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
        {
            var name = session.State.Self.Name;
            if (!string.Equals(name, _fighter!.Name, StringComparison.OrdinalIgnoreCase))
            {
                // Nothing was sent: the session belongs to the host (the gear scenario's RequireCharacter rule, for the fighter's name).
                Failure = $"the character in the world is '{name ?? "(name not known)"}', not {_fighter.Name}: nothing is sent for it";
                _log.Error(LogCategory, Failure);
                _log.Info(LogCategory, "verdict " + VerdictFields());
                Stage = "failed";
                return ScenarioStatus.Failed;
            }

            return BeginVitals(session, session.Clock.Now);
        }

        Stage = "login";
        return ScenarioStatus.Running;
    }

    /// <summary>The fighters' swap in: the weapon aside, the caster wielded, the magic mode (the cycle needs it at its start).</summary>
    /// <summary>The least stamina share a fighter's pass starts with (M4 P5a fix r4): its Stamina to Mana Self draws on the stamina. Not retail: Lifestone's own.</summary>
    public const int PreconditionStaminaPercent = 80;

    /// <summary>The least mana share a fighter's pass starts with (M4 P5a fix r4). Not retail: Lifestone's own.</summary>
    public const int PreconditionManaPercent = 30;

    /// <summary>The vitals a fighter's pass starts with (M4 P5a fix r4; <see cref="PreconditionStaminaPercent"/>, <see cref="PreconditionManaPercent"/>); null starts the pass as the vitals are (a unit test of the cycle's own mana loop).</summary>
    public VitalsNeeds? Precondition { get; init; } = new(MinStaminaPercent: PreconditionStaminaPercent, MinManaPercent: PreconditionManaPercent);

    /// <summary>The vitals keeper of the precondition (M4 P5a fix r4), or null before it starts.</summary>
    public VitalsKeeper? PreconditionKeeper => _keeper;

    /// <summary>
    /// M4 P5a fix r4: the fighter's pass starts with its stamina and mana restored in peace, before the swap (the weapon still in hand, so the keeper rests rather than casts; bounded by <see cref="VitalsOptions.RestCapSeconds"/>):
    /// round 4's FighterArcherBuffLiveTests ran after the heal leg left the Archer at 9 of 291 mana and a low stamina, and 64 transfers and Revitalizes traded the two back and forth. A need it cannot meet fails up front,
    /// <c>precondition:&lt;vital&gt;</c>, never the pass.
    /// </summary>
    private ScenarioStatus BeginVitals(IGameSessionView session, double clock)
    {
        if (Precondition is not { } needs)
        {
            return BeginSwapIn(session, clock);
        }

        _keeper = new VitalsKeeper(session, _spells, _fighter is null ? _vitalsOptions : _vitalsOptions.ForFighter(), _log); // M4 P5a C7: a fighter's mana order is a Mana Potion, then a rest
        if (_keeper.NeedsFor(needs))
        {
            _log.Info(LogCategory, "precondition met at once: stamina and mana are where the pass starts");
            return BeginSwapIn(session, clock);
        }

        _log.Info(LogCategory, $"precondition: the vitals keeper restores stamina to {PreconditionStaminaPercent}% and mana to {PreconditionManaPercent}% in peace before the pass");
        Stage = "vitals";
        return ScenarioStatus.Running;
    }

    private ScenarioStatus BeginSwapIn(IGameSessionView session, double clock)
    {
        _swap = CasterSwap.For(_fighter, session.Actions, session.State, _log);
        if (_swap is null || !_swap.Begin(SwapDirection.In, clock))
        {
            return Fail(session, "swap-in: it did not start");
        }

        _log.Info(LogCategory, $"{_fighter!.Name}: the caster swap first (the weapon to the pack, the caster wielded, the magic combat mode); no gear step");
        Stage = "swap-in";
        return ScenarioStatus.Running;
    }

    /// <summary>Makes the cycle if there is none (over the fighter's profile when there is one) and starts the ordered pass; null on success, else the failure.</summary>
    private string? StartCycle(IGameSessionView session, double clock)
    {
        _cycle ??= new BuffCycle(session, _spells, _options, _log, _ledger ?? ArmourBuffLedger.For(session.State), _store, _profile); // one ledger for every cycle this session makes: a second cycle object casts no known armour line
        if (!_cycle.Start(clock))
        {
            return "cycle: it did not start: " + (_cycle.Result?.Failure ?? "a pass is already running");
        }

        Stage = "cycle";
        return null;
    }

    /// <summary>After the passes: the mage returns to non-combat; a fighter runs the swap's Out half (peace, the caster to the pack, the weapon back).</summary>
    private ScenarioStatus ToPeace(IGameSessionView session, double clock)
    {
        if (_swap is null)
        {
            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            Stage = "mode-peace";
            return ScenarioStatus.Running;
        }

        _needRestore = false;
        if (!_swap.Begin(SwapDirection.Out, clock))
        {
            return Fail(session, "swap-out: it did not start");
        }

        Stage = "swap-out";
        return ScenarioStatus.Running;
    }

    /// <summary>The run's judgement after the forced pass (or the driver's): null when it holds, else the first thing that does not.</summary>
    private string? Judge()
    {
        var best = _driverResult ?? _final!;
        var missed = new List<string>();
        if (_first is not null)
        {
            missed.AddRange(_first.Misses);
        }

        missed.AddRange(best.Misses);
        if (missed.Count > 0)
        {
            return "buffs: lines were missed (" + string.Join(';', missed) + ")";
        }

        if (best.Active < best.Wanted)
        {
            return $"buffs: {best.Active} of {best.Wanted} lines are active ({best.Levels})";
        }

        if (_driverResult is not null)
        {
            return best.Rebuffed ? null : "driver: the driver's pass recast no line while it was still active";
        }

        return _forcedLanded ? null : "rebuff: the forced pass recast no line while it was still active";
    }

    /// <summary>Keeps the ledger's remaining seconds in the store, when there is one (the run ends: ACE stops the items' clocks while the character is logged out).</summary>
    private void SaveLedger(IGameSessionView session)
    {
        if (_store is null || _cycle is null)
        {
            return;
        }

        var guid = session.State.Self.Guid;
        _store.Save(guid, _cycle.Ledger.Export(guid, session.Clock.Now));
        _log.Info(LogCategory, "armour memory: the ledger's remaining seconds are kept for the next login");
    }

    private ScenarioStatus Conclude(IGameSessionView session)
    {
        SaveLedger(session);
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            Stage = "done";
            return ScenarioStatus.Succeeded;
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        Stage = "logging-off";
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure = why;
        _driver?.Dispose();
        _log.Error(LogCategory, why);
        if (_needRestore && _swap is not null && session.State.Phase == SessionPhase.InWorld)
        {
            // The fighters' leg: the weapon is in the pack and the caster in hand; put them back before the run logs off.
            _needRestore = false;
            if (_swap.Begin(SwapDirection.Out, session.Clock.Now))
            {
                _log.Info(LogCategory, "the caster swap back first (the weapon in hand and peace), then the run ends as failed");
                _failRestore = true;
                return ScenarioStatus.Running;
            }
        }

        return FinishFail(session);
    }

    private ScenarioStatus FinishFail(IGameSessionView session)
    {
        _log.Info(LogCategory, "verdict " + VerdictFields());
        if (session.State.Phase == SessionPhase.InWorld)
        {
            SaveLedger(session);
        }

        if (_endSession && session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // As the gear scenario's failure (M3 1.4 fix A2): leave the world before the run ends as failed, so the next login is not refused as CharacterInWorld; the verdict above stands whatever the log off does.
            _log.Info(LogCategory, "logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _failLogOff = true;
            return ScenarioStatus.Running;
        }

        return ScenarioStatus.Failed;
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
