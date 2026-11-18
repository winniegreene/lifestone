using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>Where a <see cref="HarmHealScript"/> is.</summary>
public enum HarmHealState
{
    /// <summary>Not stepped yet.</summary>
    Idle,

    /// <summary>Under way.</summary>
    Running,

    /// <summary>Ended well: the health was harmed, healed by the means and restored.</summary>
    Done,

    /// <summary>Ended with <see cref="HarmHealScript.Why"/>.</summary>
    Failed,
}

/// <summary>The harm-heal script's options (M4 P5a lane C3). Every bound is Lifestone's own, not retail's.</summary>
public sealed record HarmHealOptions
{
    /// <summary>The health share the harm steps bring the health to (<see cref="HarmSelfPlan.DefaultTargetShare"/>, half).</summary>
    public double TargetShare { get; init; } = HarmSelfPlan.DefaultTargetShare;

    /// <summary>
    /// The longest one harm phase may go without a landed harm, in seconds of the session clock (the design's 60 s cap); past it the script ends <c>why=harm-cap</c>. Since P5a round 3 it is measured from the last landed
    /// harm, not the phase's start: the Archer's second round still harmed as planned (Harm Self I at 5 points a cast, its 266 mana spent by the first round and the stamina by the transfers, so each cast waited for the mana)
    /// and the cap from the start ended it at 70% after 14 harms. <see cref="HarmRoundMaxSeconds"/> stays the bound.
    /// </summary>
    public double HarmCapSeconds { get; init; } = 60;

    /// <summary>The longest one harm phase may last in all, however its harms land (<c>why=harm-cap</c>). Not retail: Lifestone's own bound (P5a round 3).</summary>
    public double HarmRoundMaxSeconds { get; init; } = 180;

    /// <summary>The longest the Heal Self phase of the spell path may last (<c>why=heal-cap</c>).</summary>
    public double HealCapSeconds { get; init; } = 60;

    /// <summary>The longest the vitals keeper's restoring pass may last (<c>why=keeper-cap</c>); the keeper's own rest cap is 600 s.</summary>
    public double KeeperCapSeconds { get; init; } = 900;

    /// <summary>The longest the whole script may last (<c>why=timeout:&lt;phase&gt;</c>).</summary>
    public double MaxSeconds { get; init; } = 1800;

    /// <summary>The longest the script waits, after an action settled, for the server's vital update to show the change (a harm drops the health, a kit, a potion or a Heal Self raises it).</summary>
    public double SettleSeconds { get; init; } = 2.0;

    /// <summary>The kit uses tried before the script gives up on a rise (a kit's roll can lose; each use is counted <c>kit_lost</c>).</summary>
    public int MaxKitTries { get; init; } = 3;

    /// <summary>The chance a Harm Self or Heal Self level must reach to be cast (the buff cycle's 0.9).</summary>
    public double CastChance { get; init; } = VitalsOptions.DefaultCastChance;

    /// <summary>The margin of the harm plan (<see cref="HarmSelfPlan.MarginPoints"/>).</summary>
    public int MarginPoints { get; init; } = HarmSelfPlan.MarginPoints;

    /// <summary>
    /// The Stamina to Mana Self casts one harm round may make when the mana pays for no Harm Self level (M4 P5a fix F-abil, P5a-R7); past it the levels the mana pays are cast, or the 60 s cap decides, as before. Not retail: Lifestone's own. A fighter's mana is
    /// small and regenerates 1 point a 5 s tick (research.md section 6), so waiting for it is what the cap used to end.
    /// </summary>
    public int MaxManaTopUps { get; init; } = 6;
}

/// <summary>
/// The <b>harm-heal</b> station script (M4 P5a lane C3, design section 3.3; P5b hosts it in a station <c>ScriptLeg</c>): in peace, wherever the character stands, it lowers its health to half with Harm Self
/// (<see cref="HarmSelfPlan"/>: never a cast that could reach 0; fizzles are retried and counted, a 60 s cap ends <c>why=harm-cap</c>), heals by the character's means and has the vitals keeper restore the health.
/// <list type="bullet">
/// <item><b>A fighter</b> (a <see cref="CasterSwap"/> is given: the Brawler and the Archer cast only with the caster wielded, handoff C1b): swap in once, harm, a Handy Healing Kit (the health must rise, judged by the server's
/// vital update), harm again, a Potion of Healing (the same), the keeper restores the health to its until share, swap out once. Never one swap per cast.</item>
/// <item><b>The mage</b> (no swap: it wields its wand): harm, then Heal Self (the highest known level at the chance: VII, <c>DeathScenario.Heal.cs:26</c>), then the keeper.</item>
/// </list>
/// A step machine like <see cref="VitalsKeeper"/> and <see cref="CasterSwap"/>: <see cref="Step"/> never blocks, sends at most one action at a time through <see cref="IActionApi"/>, and judges every claim by the read model
/// (the vital moved), never by the outcome alone. A failure after the swap in still tries the swap out so the character does not stay in the magic mode with the wrong item; it ends <see cref="HarmHealState.Failed"/> with the first reason.
/// <c>TODO(m4-harm-heal-resist)</c>: a drain-resistance enchantment above 1.0 would scale a harm past its table range; the margin covers a small one and a larger one is not modelled. It is logged where it bites (once per leg, WARN, when a landed
/// harm took more health than its level's <see cref="HarmLevel.MaxHarm"/>) and counted in <see cref="ResistExceeded"/> (<c>resist=</c>).
/// <para><b>Mana.</b> When the mana pays for no Harm Self level, the harm phase casts Stamina to Mana Self itself (at most <see cref="HarmHealOptions.MaxManaTopUps"/> times a harm round; the swap is already in, so the caster is in hand) instead of waiting for the 60 s cap
/// (M4 P5a fix F-abil, P5a-R7); with none castable it logs why once and the cap decides.</para>
/// <para><b>What the heals prove</b> (P5a-R6, the coverage the old Brawler fact had): each kit use's Healed amount is over 0 and at most the health missing, its Structure goes down by one; the potion's Drank amount is 1 to <see cref="RecoveryRules.PotionPoints"/>
/// and the stack goes down by one. A break is recorded in <see cref="Anomalies"/> (the facts assert it empty); the script does not stop for it.</para>
/// </summary>
public sealed class HarmHealScript
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "harm-heal";

    private enum Phase
    {
        Peace,
        SwapIn,
        Harm,
        Kit,
        Potion,
        HealSpell,
        Keeper,
        SwapOut,
        PeaceEnd,
        ManaRest,
    }

    private enum Pend
    {
        None,
        Mode,
        Cast,
        Kit,
        Potion,
        HealCast,
        Transfer,
    }

    private enum Await
    {
        None,
        Drop,
        Rise,
        Mana,
    }

    private readonly IGameSessionView _session;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly VitalsKeeper _keeper;
    private readonly CasterSwap? _swap;
    private readonly HarmHealOptions _options;
    private readonly ILogSink _log;
    private readonly List<uint> _scratch = [];
    private readonly List<uint> _known = [];
    private readonly List<uint> _affordable = [];
    private readonly uint[] _castIds = new uint[1];
    private CastAttempt? _attempt;
    private readonly CastRules _harmRules;
    private readonly CastRules _harmTriesRules;
    private readonly CastRules _healRules;
    private readonly CastRules[] _transferRules;

    private Phase _phase;
    private Pend _pend;
    private ActionHandle? _handle;
    private Await _await;
    private Pend _awaitFor;
    private uint _awaitFrom;

    // M5 7 P7p: the heal window, from a kit's, a potion's or a Heal Self's send to its judge: what it restored (the handle's HealResult, the "restores N points" line) and the hits the character took meanwhile.
    private IDisposable? _windowChat;
    private IDisposable? _windowCombat;
    private int _windowRestoredResult;
    private int _windowRestoredLine;
    private long _windowHits;
    private int _windowHitCount;
    private double _awaitUntil;
    private double _notBefore;
    private double _t0;
    private double _phaseStart;
    private double _now;
    private int _harmRound = 1;
    private double _lastHarmAt = double.NaN;
    private uint _afterHarm;
    private int _manaRestRound;
    private bool _manaRestStarted;
    private double _restStart;
    private int _restTransfers0;
    private double _rise;

    /// <summary>
    /// The health regained between harms (M4 P5a fix r4): the health now minus the health the last harm left, kept as the largest seen in the round (regeneration does not slow down), 0 before the round's first harm.
    /// <see cref="HarmSelfPlan.Choose"/> counts each level's mean harm less by it.
    /// </summary>
    private double Rise(uint health)
    {
        if (_afterHarm > 0 && health > _afterHarm)
        {
            _rise = Math.Max(_rise, health - _afterHarm);
        }

        return _rise;
    }

    /// <summary>The largest health rise measured between two harms (M4 P5a fix r4; the verdict reports it as <c>harm_rise=</c>).</summary>
    public double MeasuredRise => _rise;
    private int _kitTries;
    private int _busyRow;
    private bool _phaseEntered;
    private bool _swapInBegun;
    private bool _swapInDone;
    private bool _swapOutStarted;
    private bool _keeperStarted;
    private int _ownFizzles;
    private int _keeperFizzles0;
    private string? _failWhy;
    private HarmLevel? _cast;
    private int _keeperKits0;
    private int _keeperPotions0;
    private int _keeperCasts0;
    private double _end;
    private uint _awaitManaFrom;
    private int _topUps;
    private bool _resistLogged;
    private uint _kitGuid;
    private uint _kitMissing;
    private int? _kitUsesBefore;
    private int _potionHeldBefore = -1;
    private readonly List<string> _anomalies = [];
    private readonly HashSet<string> _noted = [];

    /// <summary>A script over a session, the SpellTable's spells and the vitals keeper.</summary>
    /// <param name="session">The session (its Action API and read model).</param>
    /// <param name="spells">The SpellTable's spells (power, school, cost).</param>
    /// <param name="keeper">The keeper that restores the health at the end; its recovery thresholds are the station's (below 75%, until 90% by default).</param>
    /// <param name="swap">The caster swap of a fighter (<see cref="CasterSwap.ForCharacter"/>), or null for the mage: with a swap the means are the kit and the potion, without it Heal Self.</param>
    /// <param name="options">The options, or null for the defaults.</param>
    /// <param name="log">Where the script logs.</param>
    public HarmHealScript(IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, VitalsKeeper keeper, CasterSwap? swap, HarmHealOptions? options, ILogSink log)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _keeper = keeper ?? throw new ArgumentNullException(nameof(keeper));
        _swap = swap;
        _options = options ?? new HarmHealOptions();
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // M5 D13: the script's casts go through the cast policy. The harm and the heal retry a fizzle with no count inside their cap (Timed); the transfer's fizzles count against the round's cap of top-ups
        // (Utility of the casts still allowed, the harm cap as its time), as each fizzled top-up did before; the rules are made once.
        _harmRules = CastRules.Timed(_options.HarmCapSeconds);
        _harmTriesRules = _keeper.ManaOptions.On ? _harmRules.WithManaTries(_keeper.ManaOptions.Tries, _keeper.ManaOptions.TrySpacingSeconds) : _harmRules; // made once: the forms build a name string
        _healRules = CastRules.Timed(_options.HealCapSeconds);
        _transferRules = new CastRules[Math.Max(0, _options.MaxManaTopUps) + 1];
        for (var casts = 1; casts < _transferRules.Length; casts++)
        {
            _transferRules[casts] = CastRules.Utility(casts).WithMaxSeconds(_options.HarmCapSeconds);
        }

    }

    /// <summary>Where the script is.</summary>
    public HarmHealState State { get; private set; }

    /// <summary>The phase the script is in, as the log says it (<c>peace</c>, <c>swap-in</c>, <c>harm</c>, <c>kit</c>, <c>potion</c>, <c>heal</c>, <c>keeper</c>, <c>swap-out</c>, <c>peace-end</c>).</summary>
    public string Stage => State == HarmHealState.Idle ? "start" : _phase.ToString().ToLowerInvariant();

    /// <summary>Why the script failed, or null.</summary>
    public string? Why => _failWhy;

    /// <summary>True when the fighter path runs (a swap was given): the kit and the potion are the means.</summary>
    public bool FighterPath => _swap is not null;

    /// <summary>The Harm Self casts that landed.</summary>
    public int Harms { get; private set; }

    /// <summary>The casts of the script's own (Harm Self and Heal Self) that fizzled.</summary>
    public int Fizzles => _ownFizzles + (_keeperStarted ? _keeper.Fizzles - _keeperFizzles0 : 0);

    /// <summary>The kit uses that raised the health (the script's own, then the keeper's).</summary>
    public int Kits => OwnKits + (_keeperStarted ? _keeper.Kits - _keeperKits0 : 0);

    /// <summary>The potions that raised the health (the script's own, then the keeper's).</summary>
    public int Potions => OwnPotions + (_keeperStarted ? _keeper.Potions - _keeperPotions0 : 0);

    /// <summary>The heals in all: kits, potions and Heal Self casts that raised the health, the script's and the keeper's.</summary>
    public int Heals => Kits + Potions + OwnHealCasts + (_keeperStarted ? _keeper.HealSelfCasts - _keeperCasts0 : 0);

    /// <summary>The script's own kit uses that raised the health.</summary>
    public int OwnKits { get; private set; }

    /// <summary>The script's own potions that raised the health.</summary>
    public int OwnPotions { get; private set; }

    /// <summary>The script's own Heal Self casts that raised the health (the spell path).</summary>
    public int OwnHealCasts { get; private set; }

    /// <summary>The kit uses that spent a use and healed nothing (a lost roll, counted and retried up to <see cref="HarmHealOptions.MaxKitTries"/>).</summary>
    public int KitLost { get; private set; }

    /// <summary>The Stamina to Mana Self casts of the harm phase that raised the mana (P5a-R7).</summary>
    public int Transfers { get; private set; }

    /// <summary>The harms that took more health than their level's table maximum (<c>TODO(m4-harm-heal-resist)</c>, P5a-R11).</summary>
    public int ResistExceeded { get; private set; }

    /// <summary>The health the script's own kit uses healed, summed (the Healed amounts).</summary>
    public int KitPoints { get; private set; }

    /// <summary>The health the script's own potions restored, summed (the Drank amounts).</summary>
    public int PotionPoints { get; private set; }

    /// <summary>The potion stack's size before the script's own potion (-1: none drunk).</summary>
    public int PotionStackBefore => _potionHeldBefore;

    /// <summary>The potion stack's size after the script's own potion (-1: none drunk; 0: the stack is gone).</summary>
    public int PotionStackAfter { get; private set; } = -1;

    /// <summary>The heal checks that broke (P5a-R6): a Healed amount over the health missing or not above 0, a kit Structure that did not go down by one, a Drank amount outside 1 to 25, a potion stack that did not go down by one. Empty when all held.</summary>
    public IReadOnlyList<string> Anomalies => _anomalies;

    /// <summary>The 0x001D busy refusals of a heal the script waited out.</summary>
    public int Busy { get; private set; }

    /// <summary>The lowest health share seen, in percent (NaN before the first look).</summary>
    public double HealthMinPercent { get; private set; } = double.NaN;

    /// <summary>The health share at the end, in percent (NaN before it ended).</summary>
    public double HealthEndPercent { get; private set; } = double.NaN;

    /// <summary>True when the swap in and the swap out both ran (<c>swapped=yes</c>).</summary>
    public bool Swapped => _swap?.Swapped ?? false;

    /// <summary>The session seconds the script ran.</summary>
    public double Seconds => State is HarmHealState.Done or HarmHealState.Failed ? _end - _t0 : State == HarmHealState.Idle ? 0 : _now - _t0;

    /// <summary>
    /// The verdict line: <c>harm-heal done ok= [why=] heals= kits= potions= harms= fizzles= health_min_pct= swapped= kit_lost= busy= health_end_pct= s= kit_points= potion_points= potion_stack= transfers= resist= checks=</c>. The first nine fields are the
    /// station's; the rest say what was retried; the last six (M4 P5a fix F-abil) are new: the facts print them and assert only <c>checks</c>, the restored heal checks (<c>ok</c> or the count of <see cref="Anomalies"/>).
    /// </summary>
    public string VerdictLine() => string.Create(
        CultureInfo.InvariantCulture,
        $"harm-heal done ok={(State == HarmHealState.Done ? "true" : "false")}{(_failWhy is null ? string.Empty : " why=" + _failWhy)} heals={Heals} kits={Kits} potions={Potions} harms={Harms} fizzles={Fizzles} health_min_pct={Pct(HealthMinPercent)} swapped={(Swapped ? "yes" : "no")} kit_lost={KitLost} busy={Busy} health_end_pct={Pct(HealthEndPercent)} s={Seconds:0.0} kit_points={KitPoints} potion_points={PotionPoints} potion_stack={(PotionStackBefore < 0 ? "n/a" : PotionStackBefore.ToString(CultureInfo.InvariantCulture) + "->" + PotionStackAfter.ToString(CultureInfo.InvariantCulture))} transfers={Transfers} resist={ResistExceeded} checks={(_anomalies.Count == 0 ? "ok" : _anomalies.Count.ToString(CultureInfo.InvariantCulture))}");

    /// <summary>
    /// One step at session time <paramref name="now"/>: settles the action in flight, waits for the vital to move, then decides and sends at most one action. Never blocks.
    /// </summary>
    /// <param name="now">The session clock's time.</param>
    /// <returns>The state after the step.</returns>
    public HarmHealState Step(double now)
    {
        if (State is HarmHealState.Done or HarmHealState.Failed)
        {
            return State;
        }

        _now = now;
        if (State == HarmHealState.Idle)
        {
            State = HarmHealState.Running;
            _t0 = now;
            _phase = _swap is null ? Phase.Harm : Phase.Peace;
            _log.Info(LogCategory, $"harm-heal: begins ({(FighterPath ? "fighter: swap in once, harm, kit, harm, potion, keeper, swap out" : "caster: harm, Heal Self, keeper")}); target {_options.TargetShare * 100:0}% health");
        }

        if (now - _t0 > _options.MaxSeconds)
        {
            return Fail("timeout:" + Stage);
        }

        var q = _session.State.Self.Qualities;
        if (q.IsKnown && q.TryGetVitalLevels(MageTemplate.MaxHealth, out var seen, out var seenMax) && seenMax > 0)
        {
            var share = 100.0 * seen / seenMax;
            if (double.IsNaN(HealthMinPercent) || share < HealthMinPercent)
            {
                HealthMinPercent = share;
            }
        }

        if ((_handle is not null || _pend is Pend.Cast or Pend.HealCast or Pend.Transfer) && !SettleHandle())
        {
            return State;
        }

        if (State != HarmHealState.Running)
        {
            return State;
        }

        if (_await != Await.None && !SettleAwait())
        {
            return State;
        }

        if (State != HarmHealState.Running || now < _notBefore)
        {
            return State;
        }

        if (!_phaseEntered)
        {
            _phaseEntered = true;
            _phaseStart = now;
        }

        switch (_phase)
        {
            case Phase.Peace:
                return StepPeace(CombatModes.NonCombat, Phase.SwapIn);
            case Phase.SwapIn:
                return StepSwap(SwapDirection.In);
            case Phase.Harm:
                return StepHarm();
            case Phase.ManaRest:
                return StepManaRest();
            case Phase.Kit:
                return StepKit();
            case Phase.Potion:
                return StepPotion();
            case Phase.HealSpell:
                return StepHealSpell();
            case Phase.Keeper:
                return StepKeeper();
            case Phase.SwapOut:
                return StepSwap(SwapDirection.Out);
            default:
                return StepPeace(CombatModes.NonCombat, null);
        }
    }

    // ------------------------------------------------------------------------------------------------ phases

    private HarmHealState StepPeace(int mode, Phase? next)
    {
        var current = _session.State.Self.Qualities.CombatMode;
        if (current == mode || (mode == CombatModes.NonCombat && current == 0))
        {
            if (next is { } to)
            {
                Enter(to);
                return State;
            }

            return Finish();
        }

        _log.Info(LogCategory, $"harm-heal: the combat mode is {current}, not peace: setting peace");
        return Send(Pend.Mode, _session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation));
    }

    private HarmHealState StepSwap(SwapDirection direction)
    {
        var swap = _swap!;
        if (direction == SwapDirection.Out && !_swapOutStarted)
        {
            _swapOutStarted = true;
            swap.Begin(SwapDirection.Out, _now);
            _log.Info(LogCategory, "harm-heal: swap out (the weapon back in hand, peace)");
        }
        else if (direction == SwapDirection.In && !_swapInBegun)
        {
            _swapInBegun = true;
            swap.Begin(SwapDirection.In, _now);
            _log.Info(LogCategory, "harm-heal: swap in (the caster in hand, the magic mode), once for every cast");
        }

        switch (swap.Step(_now))
        {
            case SwapStatus.Running:
                return State;
            case SwapStatus.Failed:
                _log.Warn(LogCategory, $"harm-heal: the swap {direction} failed: {swap.Failure} {swap.Detail}");
                if (direction == SwapDirection.Out)
                {
                    _failWhy ??= swap.Failure ?? "swap:out";
                    return Finish();
                }

                return Fail(swap.Failure ?? "swap:in");
            default:
                if (direction == SwapDirection.In)
                {
                    _swapInDone = true;
                    Enter(Phase.Harm);
                    return State;
                }

                Enter(Phase.PeaceEnd);
                return State;
        }
    }

    /// <summary>
    /// M4 P5a fix r4: before each harm round, the mana the round needs: the casts of the level the plan picks at the current health (with every castable level and the measured rise) to bring the health to the target, times
    /// its cost, at most the maximum mana. With less, the vitals keeper restores it (Stamina to Mana Self where the stamina allows, else a rest in peace; bounded by its rest cap) and a miss fails the leg
    /// <c>precondition:mana</c>. Round 4: the Brawler's round 1 spent about 250 of 287 mana, round 2 began at 37, and Harm Self I against a regeneration of 3 a harm ran into the round cap at 54%.
    /// </summary>
    private HarmHealState StepManaRest()
    {
        if (!TryVitals(out var health, out var maxHealth, out var mana))
        {
            return State;
        }

        if (!_manaRestStarted)
        {
            _manaRestStarted = true;
            ManaRestNeed = HarmManaNeed(health, maxHealth);
            ManaRestFloor = _restFloor; // set by HarmManaNeed: the same rounds' try floors (the ceiling's value with the tries off or no Mana Conversion)
            if (mana >= ManaRestNeed)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: harm round {_harmRound} needs {ManaRestNeed} mana; {mana} held"));
                _manaRestRound = _harmRound;
                Enter(Phase.Harm);
                return State;
            }

            ManaRests++;
            _restStart = _now;
            _restTransfers0 = _keeper.Transfers;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: harm round {_harmRound} needs {ManaRestNeed} mana and {mana} is held: the vitals keeper restores it first"));
            _keeper.NeedsFor(new VitalsNeeds(NextCastMana: ManaRestNeed, NextCastFloor: ManaRestFloor)); // G2: the keeper funds the round to the try floor, and the harm casts try from there (HarmFloor)
        }

        var state = _keeper.Step(_now);
        if (state == VitalsKeeperState.Blocked || (state == VitalsKeeperState.Ready && !_keeper.Busy))
        {
            // The keeper's Stamina to Mana casts of the rest count as the leg's transfers (transfers=); its time is ManaRestSeconds.
            Transfers += _keeper.Transfers - _restTransfers0;
            ManaRestSeconds += _now - _restStart;
        }

        if (state == VitalsKeeperState.Blocked)
        {
            return Fail("precondition:mana (" + (_keeper.Why ?? "blocked") + ")");
        }

        if (state == VitalsKeeperState.Ready && !_keeper.Busy)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: the mana for harm round {_harmRound} is restored: {mana} of {ManaRestNeed} ({_keeper.VerdictFields()})"));
            _manaRestRound = _harmRound;
            Enter(Phase.Harm);
        }

        return State;
    }

    /// <summary>The mana the harms of a round need from <paramref name="health"/> (see <see cref="StepManaRest"/>); 0 when no level is castable or the health is at the target.</summary>
    private uint HarmManaNeed(uint health, uint maxHealth)
    {
        _restFloor = 0;
        var q = _session.State.Self.Qualities;
        var book = _session.State.Self.Spellbook;
        var worn = _session.State.Self.Inventory.Equipped.Count;
        var castable = new List<uint>();
        var held = LevelFloor.HeldForAccount(_session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, Account);
        foreach (var id in VitalsMeans.HarmSelf)
        {
            if (book.Knows(id) && _spells.TryGetValue(id, out var info) && GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out var skill, out _) && GearCasts.Chance(skill, info.Power) >= _options.CastChance && !Unpayable(held, info))
            {
                castable.Add(id);
            }
        }

        // The plan's pick at the round's start, with the rise the last round measured (rounds are alike: the same character, the same gear).
        if (HarmSelfPlan.Choose(health, maxHealth, _options.TargetShare, castable, _options.MarginPoints, _rise).Level is not { } level || !_spells.TryGetValue(level.SpellId, out var picked))
        {
            return 0;
        }

        var net = Math.Max(1.0, level.MeanHarm - _rise);
        var casts = Math.Ceiling((health - _options.TargetShare * maxHealth) / net);
        var ceiling = GearCasts.ManaCeiling(picked, worn);
        var need = casts * ceiling;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var maxMana);
        _restFloor = (uint)Math.Min(casts * HarmFloor(picked, ceiling), Math.Min(need, maxMana));
        return (uint)Math.Min(need, maxMana);
    }

    private uint _restFloor;

    /// <summary>
    /// M6 1 h2 (closes <c>m6-account-formula-callers</c> for the heal side): the account's name as the server stores it (the login name, not the secrets key), or null (then the keeper's, <see cref="VitalsKeeper.AccountName"/>, or none).
    /// With it the level floor also judges Life Magic casts of a pack that holds no Life focus on the account's full formula (<see cref="AccountFormula"/>); it is hashed there, never stored in a record, logged or printed.
    /// </summary>
    public string? AccountName { get; init; }

    private string? Account => AccountName ?? _keeper.AccountName;

    /// <summary>
    /// True when the pack casts Life Magic on the foci formula (it holds the focus) and lacks a component of <paramref name="info"/>'s formula: the level floor (<see cref="LevelFloor"/>, as the buff cycle's). M5 3 gate round 3:
    /// Harm Self II needs an Iron Scarab the ground's kit does not carry, and ACE refused it with 0x0400. The skip is logged once per spell.
    /// </summary>
    private bool Unpayable(IReadOnlyDictionary<uint, int>? held, SpellInfo info)
    {
        if (held is null || !LevelFloor.ShortOf(held, info, out var need, Account))
        {
            return false;
        }

        if (_noted.Add("floor:" + info.Id.ToString(CultureInfo.InvariantCulture)))
        {
            _log.Warn(LogCategory, $"harm-heal: {info.Name} needs {need}; skipped (the level floor)");
        }

        return true;
    }

    /// <summary>
    /// The try floor of a Harm Self level (M5 1 fix B, G2; <see cref="ManaOrder.FloorOf"/>): the least mana at which a Mana Conversion try lands at the try chance, at most <paramref name="ceiling"/>; the ceiling itself with the tries
    /// off (<c>--cast-mana-tries 0</c>) or no Mana Conversion, so today's economy is unchanged there. Reads the skill's enchantments, so callers ask it only when the mana is under the ceiling.
    /// </summary>
    private uint HarmFloor(SpellInfo info, uint ceiling)
    {
        var mana = _keeper.ManaOptions;
        if (!mana.On)
        {
            return ceiling;
        }

        ManaOrder.TryManaConversion(_session.State.Self.Qualities, out var conversion);
        return ManaOrder.FloorOf(ceiling, info.Power, ceiling, conversion, mana, (info.Flags & SpellFlags.IgnoresManaConversion) != 0);
    }

    /// <summary>The mana of the last harm round's floor need (<see cref="ManaRestNeed"/>'s partner, handed to the keeper as <see cref="VitalsNeeds.NextCastFloor"/>).</summary>
    public uint ManaRestFloor { get; private set; }

    /// <summary>The mana a harm round asked for last (M4 P5a fix r4; <see cref="StepManaRest"/>).</summary>
    public uint ManaRestNeed { get; private set; }

    /// <summary>The seconds the mana rests took (M4 P5a fix r4).</summary>
    public double ManaRestSeconds { get; private set; }

    /// <summary>The harm rounds that waited for the vitals keeper to restore the mana first (M4 P5a fix r4).</summary>
    public int ManaRests { get; private set; }

    private HarmHealState StepHarm()
    {
        var progress = double.IsNaN(_lastHarmAt) || _lastHarmAt < _phaseStart ? _phaseStart : _lastHarmAt;
        if (_now - progress > _options.HarmCapSeconds || _now - _phaseStart > _options.HarmRoundMaxSeconds)
        {
            return Fail("harm-cap");
        }

        if (!TryVitals(out var health, out var maxHealth, out var mana))
        {
            return State;
        }

        var q = _session.State.Self.Qualities;
        var book = _session.State.Self.Spellbook;
        _known.Clear();
        _affordable.Clear();
        var worn = _session.State.Self.Inventory.Equipped.Count;
        var held = LevelFloor.HeldForAccount(_session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, Account);
        foreach (var id in VitalsMeans.HarmSelf)
        {
            if (!book.Knows(id) || !_spells.TryGetValue(id, out var info) || !GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out var skill, out _) || GearCasts.Chance(skill, info.Power) < _options.CastChance || Unpayable(held, info))
            {
                continue;
            }

            _known.Add(id);
            var harmCeiling = GearCasts.ManaCeiling(info, worn);
            if (mana >= harmCeiling || mana >= HarmFloor(info, harmCeiling)) // G2: a level is affordable from its try floor (the ceiling with the tries off)
            {
                _affordable.Add(id);
            }
        }

        if (_known.Count == 0)
        {
            return Fail("harm-nospell");
        }

        var choice = HarmSelfPlan.Choose(health, maxHealth, _options.TargetShare, _affordable, _options.MarginPoints, Rise(health));
        switch (choice.Stop)
        {
            case HarmStop.AtTarget:
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: harm round {_harmRound} done at {health} of {maxHealth} ({100.0 * health / maxHealth:0}%), {Harms} cast(s) landed, {Fizzles} fizzle(s)"));
                return EndHarmRound(health, maxHealth);
            case HarmStop.Margin when _known.Count == _affordable.Count:
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: harm round {_harmRound} stops at {health} of {maxHealth}: no known level keeps the margin of {_options.MarginPoints} (a cast could reach 0)"));
                return EndHarmRound(health, maxHealth);
            case HarmStop.Margin:
            case HarmStop.NoSpell:
                // The mana pays no level (P5a-R7): top it up with Stamina to Mana Self; with no transfer to cast the cap decides.
                return _affordable.Count == 0 && TryManaTopUp(mana, out var topUp) ? topUp : State;
        }

        // The level the plan wants, with every known level allowed, is not the one the mana pays for (P5a-R7): a cheaper level takes many casts for the same harm and the cap ends them, so top the mana up first when a transfer can be cast.
        if (HarmSelfPlan.Choose(health, maxHealth, _options.TargetShare, _known, _options.MarginPoints, Rise(health)).Level is { } wanted && !_affordable.Contains(wanted.SpellId) && TryManaTopUp(mana, out var better))
        {
            return better;
        }

        if (_session.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            _log.Info(LogCategory, "harm-heal: entering the magic mode for Harm Self");
            return Send(Pend.Mode, _session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation));
        }

        _cast = choice.Level;
        _awaitFrom = health;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: cast {HarmSelfPlan.Describe(choice.Level!.Value)} at {health} of {maxHealth} ({100.0 * health / maxHealth:0}%), mana {mana}"));
        var harmSpell = choice.Level!.Value.SpellId;
        var tryFloor = 0u;
        if (_spells.TryGetValue(harmSpell, out var harmInfo))
        {
            var ceilingNow = GearCasts.ManaCeiling(harmInfo, _session.State.Self.Inventory.Equipped.Count);
            if (mana < ceilingNow && HarmFloor(harmInfo, ceilingNow) is var floor && floor < ceilingNow)
            {
                tryFloor = floor; // under the ceiling the cast tries from its floor (a 0x0401 inside the tries is expected)
            }
        }

        return SendCast(Pend.Cast, harmSpell, tryFloor > 0 ? _harmTriesRules : _harmRules, "harm-heal harm", tryFloor);
    }

    /// <summary>
    /// Sends a Stamina to Mana Self cast (the highest known level at the chance) when one can be cast: returns true with the state after the send. Returns false, after logging once why, when the cap of top-ups is reached, no level is castable,
    /// the mana does not pay the transfer or it would give nothing; the caller then goes on with the levels the mana pays (or the 60 s cap decides).
    /// </summary>
    private bool TryManaTopUp(uint mana, out HarmHealState state)
    {
        state = State;
        var q = _session.State.Self.Qualities;
        if (_topUps >= _options.MaxManaTopUps)
        {
            Note("topup-cap", $"harm-heal: {_topUps} Stamina to Mana Self cast(s) did not make the mana pay for the Harm Self level wanted ({mana} mana); the levels the mana pays are cast, or the harm cap decides");
            return false;
        }

        _ = q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _);
        _ = q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var maxMana);
        var info = PickTransfer(q, mana, stamina, maxMana > mana ? maxMana - mana : 0, out var why);
        if (info is null)
        {
            Note("topup-nospell", $"harm-heal: the mana ({mana}) does not pay for the Harm Self level wanted and no Stamina to Mana Self can be cast ({why}); the levels the mana pays are cast, or the harm cap decides");
            return false;
        }

        if (q.CombatMode != CombatModes.Magic)
        {
            _log.Info(LogCategory, "harm-heal: entering the magic mode for Stamina to Mana Self");
            state = Send(Pend.Mode, _session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation));
            return true;
        }

        _topUps++;
        _awaitManaFrom = mana;
        _log.Info(LogCategory, $"harm-heal: the mana ({mana} of {maxMana}) does not pay for the Harm Self level wanted: cast {info.Name} ({info.Id}), top-up {_topUps} of {_options.MaxManaTopUps}");
        state = SendCast(Pend.Transfer, info.Id, _transferRules[_options.MaxManaTopUps - _topUps + 1], "harm-heal mana top-up");
        return true;
    }

    /// <summary>The highest known level of Stamina to Mana Self whose chance reaches the cast chance, whose cost the mana pays and that would give more mana than it costs (the owner's rule of picking by the sum, 2026-11-08: for the ladder's character, of those levels the one that nets the most); null with the reason in <paramref name="why"/>.</summary>
    private SpellInfo? PickTransfer(PlayerQualities q, uint mana, uint stamina, uint manaMissing, out string why)
    {
        why = "Stamina to Mana Self: no level is known";
        var book = _session.State.Self.Spellbook;
        var ids = VitalsMeans.StaminaToManaSelf;
        var held = LevelFloor.HeldForAccount(_session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, Account);
        var byNet = CharacterPolicy.For(_session.State.Self.Name).ManaBySum;
        var conversion = 0u;
        if (byNet)
        {
            ManaOrder.TryManaConversion(q, out conversion);
        }

        SpellInfo? bestNet = null;
        var bestYield = double.MinValue;
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (!book.Knows(ids[i]) || !_spells.TryGetValue(ids[i], out var info))
            {
                continue;
            }

            if (Unpayable(held, info))
            {
                why = "Stamina to Mana Self: no level the pack can pay for (the level floor)"; // M5 4 G1: named once by Unpayable
                continue;
            }

            if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out var skill, out var skillWhy))
            {
                why = "Stamina to Mana Self: " + skillWhy;
                return null;
            }

            if (GearCasts.Chance(skill, info.Power) < _options.CastChance)
            {
                why = $"Stamina to Mana Self: no known level reaches a chance of {_options.CastChance} at skill {skill}";
                continue;
            }

            var cost = GearCasts.ManaCeiling(info, _session.State.Self.Inventory.Equipped.Count);
            if (mana < cost)
            {
                why = $"the mana ({mana}) does not pay for {info.Name} ({cost})";
                continue;
            }

            var gain = VitalsMeans.TransferGain(info, stamina, manaMissing);
            if (gain <= cost)
            {
                why = $"{info.Name} would give {gain} mana for a cost of up to {cost} (stamina {stamina}, mana {mana}): no net gain";
                continue;
            }

            if (byNet && BuffStatMods.TransferOf(info.Id) is { } row)
            {
                var level = new DanceLevel(info.Id, GearCasts.ManaCeiling(info, 0), info.Power, row);
                var net = ManaOrder.NetYield(in level, stamina, conversion, mana);
                if (bestNet is null || net >= bestYield) // the walk is highest first: the lower on a tie
                {
                    (bestNet, bestYield) = (info, net);
                }

                continue;
            }

            why = string.Empty;
            return info;
        }

        if (bestNet is not null)
        {
            why = string.Empty;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"Stamina to Mana Self: by the sum: {bestNet.Name} nets {bestYield:0.#} at stamina {stamina}, mana {mana}"));
        }

        return bestNet;
    }

    private void Note(string key, string message)
    {
        if (_noted.Add(key))
        {
            _log.Warn(LogCategory, message);
        }
    }

    private HarmHealState EndHarmRound(uint health, uint maxHealth)
    {
        _afterHarm = 0; // the next round measures its own rise (the kit or the potion comes between)
        if (maxHealth - health < RecoveryRules.PotionMinMissing)
        {
            return Fail("harm-short");
        }

        if (!FighterPath)
        {
            Enter(Phase.HealSpell);
            return State;
        }

        Enter(_harmRound == 1 ? Phase.Kit : Phase.Potion);
        return State;
    }

    private HarmHealState StepKit()
    {
        if (!TryVitals(out var health, out var maxHealth, out _))
        {
            return State;
        }

        if (_kitTries >= _options.MaxKitTries)
        {
            return Fail("kit-no-rise");
        }

        if (!VitalsMeans.FindKit(_session.State, _scratch, out var kit, out var uses))
        {
            return Fail("no-kit");
        }

        _kitTries++;
        _awaitFrom = health;
        _kitGuid = kit;
        _kitMissing = maxHealth - health;
        _kitUsesBefore = _session.State.Objects.TryGet(kit, out var kitObject) ? kitObject.Weenie.Structure : null;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: kit 0x{kit:X8} ({uses} use(s) left) at {health} of {maxHealth} ({100.0 * health / maxHealth:0}%), try {_kitTries} of {_options.MaxKitTries}"));
        OpenHealWindow();
        return Send(Pend.Kit, _session.Actions.UseWithTarget(kit, _session.State.Self.Guid, ActionSource.Automation));
    }

    private HarmHealState StepPotion()
    {
        if (!TryVitals(out var health, out var maxHealth, out _))
        {
            return State;
        }

        if (!VitalsMeans.FindPotions(_session.State, _scratch, out var potion, out var held))
        {
            return Fail("no-potion");
        }

        _awaitFrom = health;
        _potionHeldBefore = held;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: potion 0x{potion:X8} ({held} held) at {health} of {maxHealth} ({100.0 * health / maxHealth:0}%)"));
        OpenHealWindow();
        return Send(Pend.Potion, _session.Actions.Drink(potion, ActionSource.Automation));
    }

    private HarmHealState StepHealSpell()
    {
        if (_now - _phaseStart > _options.HealCapSeconds)
        {
            return Fail("heal-cap");
        }

        if (!TryVitals(out var health, out var maxHealth, out var mana))
        {
            return State;
        }

        var q = _session.State.Self.Qualities;
        var held = LevelFloor.HeldForAccount(_session.State, DatReaderWriter.Enums.MagicSchool.LifeMagic, Account); // M5 4 G1: the level floor for Heal Self too (named once per level)
        if (held is not null)
        {
            var heals = VitalsMeans.HealSelf;
            for (var i = 0; i < heals.Count; i++)
            {
                if (_spells.TryGetValue(heals[i], out var healLevel))
                {
                    Unpayable(held, healLevel);
                }
            }
        }

        var info = VitalsMeans.Pick(q, _session.State.Self.Spellbook, _spells, VitalsLine.HealSelf, _options.CastChance, out var why, held, Account);
        if (info is null)
        {
            // P5a round 3: under a vitae penalty (BSD Mage at 40%: Life Magic 310) no known Heal Self level may reach the 0.9 rule. Not a failure: the best known level is cast, its fizzles counted, the heal cap still decides.
            info = VitalsMeans.PickBestChance(q, _session.State.Self.Spellbook, _spells, VitalsLine.HealSelf, out var best, held, Account);
            if (info is null)
            {
                return Fail("heal-nospell:" + why);
            }

            Note("heal-fallback", string.Create(CultureInfo.InvariantCulture, $"harm-heal: heal-fallback: {why}; casting the best known level, {info.Name} ({info.Id}) at a chance of {best:0.00}: fizzles are counted and the heal cap ({_options.HealCapSeconds:0} s) decides"));
        }

        if (mana < GearCasts.ManaCeiling(info, _session.State.Self.Inventory.Equipped.Count))
        {
            return State; // the cap decides
        }

        if (q.CombatMode != CombatModes.Magic)
        {
            _log.Info(LogCategory, "harm-heal: entering the magic mode for Heal Self");
            return Send(Pend.Mode, _session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation));
        }

        _awaitFrom = health;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: cast {info.Name} ({info.Id}) at {health} of {maxHealth} ({100.0 * health / maxHealth:0}%)"));
        OpenHealWindow();
        return SendCast(Pend.HealCast, info.Id, _healRules, "harm-heal heal");
    }

    private HarmHealState StepKeeper()
    {
        if (_now - _phaseStart > _options.KeeperCapSeconds)
        {
            return Fail("keeper-cap");
        }

        if (!_keeperStarted)
        {
            _keeperStarted = true;
            _keeperFizzles0 = _keeper.Fizzles;
            _keeperKits0 = _keeper.Kits;
            _keeperPotions0 = _keeper.Potions;
            _keeperCasts0 = _keeper.HealSelfCasts;
            _log.Info(LogCategory, "harm-heal: the vitals keeper restores the health");
            _keeper.NeedsFor(new VitalsNeeds());
        }

        var state = _keeper.Step(_now);
        if (state == VitalsKeeperState.Blocked)
        {
            return Fail("keeper:" + (_keeper.Why ?? "blocked"));
        }

        if (state == VitalsKeeperState.Ready && !_keeper.Busy)
        {
            _log.Info(LogCategory, $"harm-heal: the keeper is ready ({_keeper.VerdictFields()})");
            Enter(FighterPath ? Phase.SwapOut : Phase.PeaceEnd);
        }

        return State;
    }

    // ------------------------------------------------------------------------------------------------ settling

    private HarmHealState Send(Pend kind, ActionHandle handle)
    {
        _pend = kind;
        _handle = handle;
        return State;
    }

    /// <summary>Starts a cast attempt (the policy sends the first try now and the retries later) and waits for it in <see cref="SettleHandle"/>.</summary>
    private HarmHealState SendCast(Pend kind, uint spell, CastRules rules, string purpose, uint tryFloor = 0)
    {
        _castIds[0] = spell;
        _attempt ??= new CastAttempt(_session.Actions, _session.State, _log, _spells);
        _attempt.Start(new CastRequest { Levels = _castIds, Rules = rules, Purpose = purpose, WornItems = _session.State.Self.Inventory.Equipped.Count, ManaTryFloor = tryFloor }, _now);
        _pend = kind;
        _handle = null;
        return State;
    }

    /// <summary>Reads the outcome of the action in flight; false while it has none.</summary>
    private bool SettleHandle()
    {
        if (_pend is Pend.Cast or Pend.HealCast or Pend.Transfer)
        {
            if (_attempt!.Step(_now) == CastEnd.Running)
            {
                return false;
            }

            var cast = _pend;
            _pend = Pend.None;
            SettleCast(cast, _attempt.Report);
            return State == HarmHealState.Running && _await == Await.None;
        }

        if (_handle?.Outcome is not { } outcome)
        {
            return false;
        }

        var handle = _handle;
        var kind = _pend;
        _handle = null;
        _pend = Pend.None;
        switch (kind)
        {
            case Pend.Mode:
                if (!outcome.Succeeded)
                {
                    Fail($"mode:{outcome.Kind}");
                }

                return State == HarmHealState.Running;
            default:
                SettleHeal(kind, (HealHandle)handle, outcome);
                return State == HarmHealState.Running && _await == Await.None;
        }
    }

    private void SettleCast(Pend kind, in CastReport report)
    {
        var name = kind == Pend.Cast ? (_cast is { } c ? HarmSelfPlan.Describe(c) : "Harm Self") : kind == Pend.Transfer ? "Stamina to Mana Self" : "Heal Self";
        _ownFizzles += report.Fizzles;
        if (kind == Pend.Transfer)
        {
            _topUps += Math.Max(0, report.Tries - 1); // each fizzled top-up counted against the round's cap, as when every cast was its own look
        }

        if (report.End.Succeeded())
        {
            if (report.Fizzles > 0)
            {
                _log.Info(LogCategory, $"harm-heal: {name} fizzled {report.Fizzles} time(s) before it landed (fizzles {_ownFizzles})");
            }

            _await = kind == Pend.Cast ? Await.Drop : kind == Pend.Transfer ? Await.Mana : Await.Rise;
            _awaitFor = kind;
            _awaitUntil = _now + _options.SettleSeconds;
            return;
        }

        // A fizzle the attempt ran out of tries or time on, or the mana read under the retry's ceiling (nothing sent): the same as a fizzle was, counted and decided again inside the cap.
        if (report.Fizzles > 0 && (report.End is CastEnd.Fizzled or CastEnd.TimeCap || (report.End == CastEnd.ManaShort && report.ManaRefusals == 0)))
        {
            _log.Info(LogCategory, $"harm-heal: {name} fizzled (fizzle {_ownFizzles}); the next look decides again, inside the cap");
            return;
        }

        var what = kind == Pend.Cast ? "harm" : kind == Pend.Transfer ? "transfer" : "heal";
        if (report.End == CastEnd.Cancelled && _session.Actions.KillSwitch)
        {
            Fail("kill-switch");
            return;
        }

        Fail(_attempt!.LastOutcome is { } o ? $"{what}-refused:{o.Kind}:0x{o.Code:X4}" : $"{what}-refused:{report.End.Name()}:0x{report.Code:X4}");
    }

    private void SettleHeal(Pend kind, HealHandle handle, ActionOutcome outcome)
    {
        var what = kind == Pend.Kit ? "kit" : "potion";
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == HealLaneTracker.TooBusyCode)
        {
            Busy++;
            if (++_busyRow > VitalsKeeper.MaxBusyRetries)
            {
                Fail("busy");
                return;
            }

            _notBefore = _now + VitalsKeeper.BusyRetrySeconds;
            if (kind == Pend.Kit)
            {
                _kitTries--;
            }

            _log.Warn(LogCategory, $"harm-heal: the server was busy (0x001D) for the {what}; waiting {VitalsKeeper.BusyRetrySeconds:0} s (busy {_busyRow} of {VitalsKeeper.MaxBusyRetries})");
            return;
        }

        _busyRow = 0;
        if (!outcome.Succeeded)
        {
            Fail($"{what}-refused:{outcome.Kind}:0x{outcome.Code:X4}");
            return;
        }

        if (kind == Pend.Kit)
        {
            CheckKitUse(handle.Result);
        }
        else
        {
            CheckPotion(handle.Result);
        }

        _windowRestoredResult = handle.Result is { Kind: HealResultKind.Healed or HealResultKind.Drank, Amount: > 0 } restored ? restored.Amount : 0; // M5 7 P7p: Confirmed with restored > 0

        if (kind == Pend.Kit && handle.Result is { Kind: HealResultKind.FailedRoll })
        {
            KitLost++;
            _log.Info(LogCategory, $"harm-heal: the kit's roll lost (a use spent, nothing healed; lost {KitLost}); the next try is counted");
            return;
        }

        _await = Await.Rise;
        _awaitFor = kind;
        _awaitUntil = _now + _options.SettleSeconds;
    }

    /// <summary>Waits for the server's vital update to show the change of the last action; false while it is awaited.</summary>
    private bool SettleAwait()
    {
        if (!TryVitals(out var health, out var maxHealth, out var mana))
        {
            if (_now >= _awaitUntil)
            {
                _await = Await.None;
                Fail("vitals-unknown");
            }

            return false;
        }

        var heal = _await == Await.Rise;
        var rose = health > _awaitFrom;
        var moved = _await switch
        {
            Await.Drop => health < _awaitFrom,
            Await.Mana => mana > _awaitManaFrom,
            _ => HealLanded(rose, _windowRestoredResult, _windowRestoredLine),
        };
        if (!moved && _now < _awaitUntil)
        {
            return false;
        }

        var kind = _awaitFor;
        _await = Await.None;
        if (moved && kind == Pend.Transfer)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: the mana moved {_awaitManaFrom} -> {mana} after the {kind}"));
        }
        else if (moved && !heal)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: the vital moved {_awaitFrom} -> {health} of {maxHealth} ({100.0 * health / maxHealth:0}%) after the {kind}"));
        }

        if (heal)
        {
            // M5 7 P7p (D141): a heal is judged on what it restored, not on the health alone: a hit in the same window (the item-end gate's Gold Phyntos Wasp, 39 points while the potion restored 25) can leave it lower.
            _log.Info(LogCategory, HealWindowLine(kind, _awaitFrom, health, maxHealth, moved, rose));
            CloseHealWindow();
        }

        switch (kind)
        {
            case Pend.Transfer:
                if (moved)
                {
                    Transfers++;
                }
                else
                {
                    _log.Warn(LogCategory, $"harm-heal: Stamina to Mana Self settled but the mana did not rise ({_awaitManaFrom} -> {mana}); counted as a top-up, the next look decides");
                }

                break;
            case Pend.Cast:
                if (moved)
                {
                    Harms++;
                    _lastHarmAt = _now;
                    _afterHarm = health;
                    NoteHarmTaken(_awaitFrom - health);
                }
                else
                {
                    Fail("harm-no-effect");
                }

                break;
            case Pend.Kit:
                if (moved)
                {
                    OwnKits++;
                    Enter(Phase.Harm, round: 2);
                }
                else
                {
                    KitLost++;
                    _log.Warn(LogCategory, "harm-heal: the kit's use settled but the health did not rise: counted as a lost roll");
                }

                break;
            case Pend.Potion:
                if (moved)
                {
                    OwnPotions++;
                    Enter(Phase.Keeper);
                }
                else
                {
                    Fail("potion-no-rise");
                }

                break;
            default:
                if (moved)
                {
                    OwnHealCasts++;
                    Enter(Phase.Keeper);
                }
                else
                {
                    Fail("heal-no-rise");
                }

                break;
        }

        return State == HarmHealState.Running;
    }

    // ------------------------------------------------------------------------------------------------ the heal window

    /// <summary>
    /// M5 7 P7p (D141): true when a heal landed: the health rose, or the action was Confirmed with a restored amount over 0 (the kit's Healed, the potion's Drank: <paramref name="restoredResult"/>), or the server's
    /// "restores N points" line with N over 0 arrived in the window (<paramref name="restoredLine"/>; a Heal Self's line, or a potion's whose handle did not read it). A heal that restored nothing and raised nothing did not land.
    /// </summary>
    /// <param name="rose">True when the health rose over the window.</param>
    /// <param name="restoredResult">The handle's restored amount, or 0.</param>
    /// <param name="restoredLine">The restore line's amount, or 0.</param>
    public static bool HealLanded(bool rose, int restoredResult, int restoredLine) => rose || restoredResult > 0 || restoredLine > 0;

    /// <summary>
    /// The window's line, logged at each heal's judge: <c>harm-heal: the potion landed: health 98 -&gt; 84 of 200 (42%), net -14: potion +25, hits -39 (1 hit(s)); restored by the result 25, by the line 25</c>; or <c>did not land</c>.
    /// </summary>
    private string HealWindowLine(Pend kind, uint from, uint health, uint maxHealth, bool landed, bool rose)
    {
        var what = kind switch
        {
            Pend.Kit => "kit",
            Pend.Potion => "potion",
            _ => "heal",
        };
        var restored = Math.Max(_windowRestoredResult, _windowRestoredLine);
        var net = (long)health - from;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"harm-heal: the {what} {(landed ? "landed" : "did not land")}: health {from} -> {health} of {maxHealth} ({100.0 * health / Math.Max(1u, maxHealth):0}%), net {net:+0;-0;0}: {what} +{restored}, hits -{_windowHits} ({_windowHitCount} hit(s)); restored by the result {_windowRestoredResult}, by the line {_windowRestoredLine}; the health {(rose ? "rose" : "did not rise")}");
    }

    private void OpenHealWindow()
    {
        _windowRestoredResult = 0;
        _windowRestoredLine = 0;
        _windowHits = 0;
        _windowHitCount = 0;
        _windowChat ??= _session.Events.Subscribe<ChatReceived>(OnWindowChat);
        _windowCombat ??= _session.Events.Subscribe<CombatEvent>(OnWindowCombat);
    }

    private void CloseHealWindow()
    {
        _windowChat?.Dispose();
        _windowCombat?.Dispose();
        _windowChat = null;
        _windowCombat = null;
    }

    private void OnWindowChat(in ChatReceived e)
    {
        var text = e.Line.Text;
        if (TryRestoredHealth(text, out var restored))
        {
            _windowRestoredLine += restored;
            return;
        }

        if (e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System)
        {
            return;
        }

        var line = FightTally.Classify(text, out var amount, out _);
        if (line == MagicLineKind.DamageTaken || (line == MagicLineKind.DrainTaken && text.Contains(" points of your health", StringComparison.OrdinalIgnoreCase)))
        {
            _windowHits += amount;
            _windowHitCount++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: a hit in the heal window: {amount} point(s) ({text.Trim()})"));
        }
    }

    private void OnWindowCombat(in CombatEvent e)
    {
        if (e.Kind == CombatEventKind.DefenseHit)
        {
            _windowHits += e.Damage;
            _windowHitCount++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"harm-heal: a hit in the heal window: {e.Damage} point(s) from {e.Who}"));
        }
    }

    /// <summary>
    /// The health a server line says was restored: the potion's "The Potion of Healing restores 25 points of your Health." (capture C3) and a Heal Self's "You cast Heal Self VI and restore 52 points of your health." (ACE's
    /// <c>SpellMessages</c> for a boost on the caster); false for any other line, and for another vital's.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="points">The points restored.</param>
    public static bool TryRestoredHealth(string text, out int points)
    {
        ArgumentNullException.ThrowIfNull(text);
        points = 0;
        foreach (var verb in RestoreVerbs)
        {
            var at = text.IndexOf(verb, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var rest = text.AsSpan(at + verb.Length);
            var end = rest.IndexOf(" points of your ", StringComparison.Ordinal);
            if (end <= 0 || !int.TryParse(rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                continue;
            }

            if (rest[(end + " points of your ".Length)..].StartsWith("health", StringComparison.OrdinalIgnoreCase))
            {
                points = n;
                return true;
            }
        }

        return false;
    }

    private static readonly string[] RestoreVerbs = [" restores ", " restore "];

    // ------------------------------------------------------------------------------------------------ checks

    /// <summary>
    /// TODO(m4-harm-heal-resist), logged where it bites (P5a-R11): a landed harm took more health than its level's table maximum, so something scaled it (a drain-resistance enchantment above 1.0); the plan's margin covers a small one only. Once per leg.
    /// </summary>
    private void NoteHarmTaken(uint taken)
    {
        if (_cast is not { } level || taken <= level.MaxHarm)
        {
            return;
        }

        ResistExceeded++;
        if (!_resistLogged)
        {
            _resistLogged = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"TODO(m4-harm-heal-resist): {HarmSelfPlan.Describe(level)} took {taken} health, over the table's maximum of {level.MaxHarm}: a drain resistance scaled it; the margin of {_options.MarginPoints} points is all that covers it"));
        }
    }

    /// <summary>The kit use's checks (P5a-R6): a Healed amount over 0 and at most the health missing at the use; the kit's Structure down by one.</summary>
    private void CheckKitUse(HealResult? result)
    {
        if (result is { Kind: HealResultKind.Healed } healed)
        {
            KitPoints += healed.Amount;
            if (healed.Amount <= 0 || healed.Amount > _kitMissing)
            {
                Anomaly($"kit healed {healed.Amount} of {_kitMissing} missing");
            }
        }

        var before = _kitUsesBefore;
        var after = _session.State.Objects.TryGet(_kitGuid, out var kit) ? kit.Weenie.Structure : null;
        var gone = after is null && before is 1;
        if (before is { } b && !gone && after != b - 1)
        {
            Anomaly($"kit Structure went {b} -> {(after?.ToString(CultureInfo.InvariantCulture) ?? "?")}, not down by one");
        }
    }

    /// <summary>The potion's checks (P5a-R6): a Drank amount of 1 to <see cref="RecoveryRules.PotionPoints"/>; the stack down by one.</summary>
    private void CheckPotion(HealResult? result)
    {
        if (result is { Kind: HealResultKind.Drank } drank)
        {
            PotionPoints += drank.Amount;
            if (drank.Amount is < 1 or > RecoveryRules.PotionPoints)
            {
                Anomaly($"potion restored {drank.Amount}, not 1 to {RecoveryRules.PotionPoints}");
            }
        }

        PotionStackAfter = VitalsMeans.FindPotions(_session.State, _scratch, out _, out var held) ? held : 0;
        if (_potionHeldBefore >= 0 && PotionStackAfter != _potionHeldBefore - 1)
        {
            Anomaly($"potion stack went {_potionHeldBefore} -> {PotionStackAfter}, not down by one");
        }
    }

    private void Anomaly(string text)
    {
        _anomalies.Add(text);
        _log.Warn(LogCategory, "harm-heal: check broke: " + text);
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private void Enter(Phase phase, int round = 0)
    {
        _phase = phase;
        _phaseEntered = false;
        if (round > 0)
        {
            _harmRound = round;
        }

        if (phase == Phase.Kit)
        {
            _kitTries = 0;
        }

        if (phase == Phase.Harm && _manaRestRound != _harmRound)
        {
            // M4 P5a fix r4: every harm round starts with the mana its harms need (StepManaRest), so the round is not spent on the levels a drained pool pays for.
            _phase = Phase.ManaRest;
            _manaRestStarted = false;
            return;
        }

        if (phase == Phase.Harm)
        {
            _topUps = 0; // the cap of mana top-ups is per harm round
            _log.Info(LogCategory, $"harm-heal: harm round {_harmRound}");
        }
    }

    private bool TryVitals(out uint health, out uint maxHealth, out uint mana)
    {
        health = 0;
        maxHealth = 0;
        mana = 0;
        var q = _session.State.Self.Qualities;
        if (!q.IsKnown || !q.TryGetVitalLevels(MageTemplate.MaxHealth, out health, out maxHealth) || maxHealth == 0)
        {
            return false;
        }

        _ = q.TryGetVitalLevels(MageTemplate.MaxMana, out mana, out _);
        return true;
    }

    private HarmHealState Fail(string why)
    {
        CloseHealWindow();
        _failWhy ??= why;
        _log.Warn(LogCategory, $"harm-heal: failed: {why}");
        _attempt?.Stop("the script failed: " + why, _now);
        _handle = null;
        _pend = Pend.None;
        _await = Await.None;
        if (_swap is not null && _swapInDone && !_swapOutStarted && _phase != Phase.SwapOut)
        {
            _log.Info(LogCategory, "harm-heal: the swap out still runs so the weapon is back in hand");
            Enter(Phase.SwapOut);
            return State;
        }

        return Finish();
    }

    private HarmHealState Finish()
    {
        _end = _now;
        HealthEndPercent = _session.State.Self.Qualities.IsKnown && _session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hMax) && hMax > 0 ? 100.0 * h / hMax : double.NaN;
        State = _failWhy is null ? HarmHealState.Done : HarmHealState.Failed;
        _log.Info(LogCategory, VerdictLine());
        return State;
    }

    private static string Pct(double value) => double.IsNaN(value) ? "n/a" : value.ToString("0.0", CultureInfo.InvariantCulture);
}
