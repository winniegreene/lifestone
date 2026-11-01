using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// What a run carries across a hand-back: the loop restarts from the state it finds (a fresh scenario, whose station runs held the player's keys and walks), but the cycles it had counted, its tallies and the time it
/// ran are kept. The factory applies it with <see cref="Apply"/> (and may seed more: slice a2's checkpoint does).
/// </summary>
/// <param name="Cycles">Cycles completed before.</param>
/// <param name="Totals">The totals of the cycles completed.</param>
/// <param name="CycleTally">The cycle in progress (its kills so far, <c>interrupted=</c> and <c>resumed=</c>).</param>
/// <param name="ElapsedSeconds">The seconds the run has used over all its starts (<see cref="AutopilotScenario.SecondsUsed"/>: prior plus elapsed, M5 4 F3, 4-R5): the minutes rule counts them.</param>
/// <param name="ForcedTripDone">True when the forced trip (<c>--autopilot-trip-after-kills</c>, once a run) was taken before.</param>
/// <param name="TestDeathDone">True when the scripted death (<c>--autopilot-test-death</c>, once a run) was taken before.</param>
/// <param name="Seed">The abandoned scenario's <see cref="AutopilotScenario.Seed"/> (M5 4 F3, 4-R9): the cycle in progress, the cycles ended, the seconds used, a cut death's tally and a cut trip. The factory hands it to the fresh scenario.</param>
public sealed record AutopilotCarry(int Cycles, AutopilotTally Totals, AutopilotTally CycleTally, double ElapsedSeconds, bool ForcedTripDone = false, bool TestDeathDone = false, AutopilotSeed? Seed = null)
{
    /// <summary>The first run: nothing carried.</summary>
    public static AutopilotCarry None => new(0, new AutopilotTally(), new AutopilotTally(), 0);

    /// <summary>True when something was carried.</summary>
    public bool IsEmpty => Cycles == 0 && ElapsedSeconds <= 0 && CycleTally.Interrupted == 0 && CycleTally.Resumed == 0 && CycleTally.Kills == 0;

    /// <summary>Adds the carried totals and the cycle in progress to <paramref name="scenario"/> (before its first step).</summary>
    /// <param name="scenario">The fresh scenario.</param>
    public void Apply(AutopilotScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        scenario.Totals.Add(Totals);
        scenario.CycleTally.Add(CycleTally);
    }

    /// <summary>
    /// The options for the fresh scenario: the once-a-run forced trip and scripted death are off once taken. The minutes stay the run's <b>total</b> target when the carry has a <see cref="Seed"/> (the fresh scenario starts at the seed's
    /// <see cref="AutopilotSeed.SecondsUsed"/>, so a hand-back after a resume at 240 of 480 minutes still ends at 480 in all, and its checkpoint writes the real minutes used: M5 4 F3, 4-R5); a carry with no seed (a factory that
    /// cannot seed) gets only what is left of the minutes, as before.
    /// </summary>
    /// <param name="options">The run's options.</param>
    public AutopilotOptions Remaining(AutopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var minutes = options.Minutes > 0 && Seed is null ? Math.Max(1.0 / 60, options.Minutes - (ElapsedSeconds / 60)) : options.Minutes;
        return options with { Minutes = minutes, TripAfterKills = ForcedTripDone ? 0 : options.TripAfterKills, TestDeath = options.TestDeath && !TestDeathDone };
    }
}

/// <summary>
/// The dial's logic (M5 item 4 h2, D20; spec DoD 9; the controls since M5 7 D145): owns the autopilot's scenario for a play session, engine-free. <b>/auto</b> (the chat's, through the host's press: <see cref="Toggle"/> from off, done,
/// stopped or halted) starts it from the state the loop finds, or hands the controls back: a fresh scenario reads the state it finds and goes on with what was carried (<see cref="AutopilotCarry"/>), <c>resumed=</c> counts. <b>/stop</b>
/// (<see cref="StopHere"/>) stops him where he stands: the run is dropped and the loop stops stepping, every queued automation action is cancelled and every key automation holds released, <c>interrupted=</c> counts. Both counts are in
/// the cycle and done lines (the scenario's tally fields). <b>The movement keys</b> (<see cref="MoveIntents.KeyLock"/>) are locked while it runs or stops and unlocked in every other mode, whatever ended the run (/stop, a stop rule, a
/// failure, the kill switch, a fault), with a chat line (<see cref="ChatNotice"/>) saying why: a stopped autopilot never leaves the person locked out. A person's movement key no longer takes the controls (D98's hand-off is retired:
/// while it runs the key does nothing). The <b>kill switch</b> halts it within one tick: the loop is not stepped (the Action API has already cancelled the automation actions it queued and released the keys it held), and nothing resumes
/// until the switch is off and /auto is typed. A clean stop at the next safe point (<see cref="Stop"/>, <see cref="AutopilotScenario.RequestStop"/>) is the stop file's, the window close's and the hosts'.
/// </summary>
/// <remarks>
/// <para>Why a fresh scenario on a hand-back: a station run that was in flight when the person took the keys (a fight's walk, a cast) is not resumable (a runner the person interrupted ends the leg, "the player's keys win"), so the loop
/// restarts at its known state, which is what the spec says ("resumes from the state the loop finds"). The kills of the fight that was running are not in the cycle (a fight adds its kills to the cycle when it ends).</para>
/// <para>Game thread only. A host ticks <see cref="Tick"/> once a frame after the session host, wires /auto to <see cref="Toggle"/> (through its press) and /stop to <see cref="StopHere"/>, shows <see cref="ChatNotice"/> in the chat, and
/// reads <see cref="Status"/> (the inspector's <see cref="IAutopilotStatusSource"/>).</para>
/// </remarks>
public sealed class AutopilotHostLogic : IAutopilotStatusSource, IDisposable
{
    /// <summary>The log category (the scenario's: <c>autopilot</c>).</summary>
    public const string LogCategory = AutopilotScenario.LogCategory;

    /// <summary>Seconds between two <see cref="Changed"/> raises while running (the next trigger's counts move without an arrow).</summary>
    public const double StatusRefreshSeconds = 2;

    /// <summary>The stop request's token when the key or the button asked.</summary>
    public const string KeyReason = "key";

    /// <summary>M5 7 D145: the abandon token of a /stop (<see cref="StopHere"/>).</summary>
    public const string ChatStopReason = "chat-stop";


    /// <summary>M5 7 D145: the chat line of a /stop that stopped him.</summary>
    public const string StoppedHereLine = "Lifestone: the autopilot stopped where he stands; the controls are yours. Type /auto to hand them back.";

    /// <summary>M5 7 D145: the start of the log line of the check one frame after a stand-down (nothing held, nothing queued, the keys unlocked).</summary>
    public const string StandDownCheckLine = "autopilot host: the stand-down check";

    /// <summary>The stop request's token when the soak's stop file asked (<c>--autopilot-stop-file</c>, M5 7 F18): with <c>--autopilot-start</c> the run ends as an unattended end does (recall home, log off, quit).</summary>
    public const string StopFileReason = "stop-file";

    /// <summary>The stop request's token when the game window was closed during a run (M5 7 F18): the autopilot recalls home first, then the game quits (<see cref="WindowCloseGate"/>).</summary>
    public const string WindowCloseReason = "window-close";

    /// <summary>The log line when the person's own Reconnect moved the session's counter under a running dial (4-R8).</summary>
    public const string ReconnectedLine = "autopilot: the person reconnected: handing over";

    /// <summary>The log line when a hand-back waits for a held movement key (4-R16).</summary>
    public const string WaitingForKeyLine = "hand-back: waiting for the movement key to be released";

    /// <summary>
    /// M5 7 P7n (D145): the start reason of <c>/auto</c> (<see cref="Start"/>'s or <see cref="Toggle"/>'s <c>startedBy</c>): a person's resume, not an unattended run; the fresh scenario's audit is a resume
    /// (<see cref="StartupAudit.ResumeWhy"/>). A hand-back is a resume too.
    /// </summary>
    public const string ResumeReason = StartupAudit.ResumeWhy;

    /// <summary>The log line of an unattended start (<c>--autopilot-start</c>; M5 item 7 s1, <c>m5-autopilot-start</c>).</summary>
    public const string StartedByOptionLine = "autopilot: started by --autopilot-start";

    /// <summary>The log line when the dial closed the vitae page after a death (once a death; M5 item 7 s1, <c>m5-autopilot-vitae-panel</c>).</summary>
    public const string VitaeClosedLine = "autopilot: closed the vitae page after the death";

    /// <summary>
    /// How long after the death station ends the dial still looks for the vitae page to close, in seconds (Lifestone's own): retail opens it on the worse vitae (<c>RecvNotice_VitaeChanged</c>, 11.6096 0x004A7AA0), which can
    /// reach the client a moment after the station's last arrow.
    /// </summary>
    public const double VitaeGraceSeconds = 10;

    private readonly Func<AutopilotCarry, AutopilotScenario> _create;
    private readonly ILogSink _log;
    private readonly AutopilotOptions _options;
    private readonly LogisticsCounters? _counters;
    private readonly List<string> _lines = [];
    private AutopilotScenario? _scenario;
    private MoveIntents? _moves;
    private IGameSessionView? _session;
    private AutopilotMode _mode = AutopilotMode.Off;
    private string _decision = "not started";
    private int _reconnectsBase;
    private bool _handBackWaiting;
    private string? _handBackWhy;
    private AutopilotSeed? _abandonedSeed;
    private double _lastRaise = double.NegativeInfinity;
    private bool _forcedTripDone;
    private bool _testDeathDone;
    private string? _stopAsked;
    private int _linesTaken;
    private bool _unattended;
    private AutopilotUnattendedEnd? _unattendedEnd;
    private double _vitaeWatchUntil = double.NegativeInfinity;
    private bool _standDownCheck;
    private string? _quietUnlock;

    /// <summary>A dial over scenarios made by <paramref name="create"/>.</summary>
    /// <param name="create">Makes a scenario for a start or a hand-back; it applies the carry (<see cref="AutopilotCarry.Apply"/>), and may give its options the carry's remaining minutes.</param>
    /// <param name="log">The log.</param>
    /// <param name="options">The run's options (the stop rules and the forced trip the next trigger reads).</param>
    /// <param name="ground">The ground, for the stock lines the next trigger reads; null shows none.</param>
    public AutopilotHostLogic(Func<AutopilotCarry, AutopilotScenario> create, ILogSink log, AutopilotOptions options, HuntingGround? ground = null)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _counters = ground is { Supplies.Stock.Count: > 0 } ? new LogisticsCounters(ground) : null;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>
    /// M5 7 P7n (D145): the line for the chat window, in plain words: every scenario's <see cref="AutopilotScenario.ChatNotice"/> (the audit's summary at each start, its notices, the stop with its reason) and the halt by the kill switch.
    /// The Godot host wires it to the chat window (P7m).
    /// </summary>
    public event Action<string>? ChatNotice;

    /// <summary>
    /// M5 7 P7n (D145): raised once when the run stops by itself, with the reason (the scenario's <see cref="AutopilotScenario.StopName"/>: a stop rule, a stop request, a failure, a give-up), or when the kill switch halts it
    /// (<see cref="HaltReason"/>): the dial is no longer running, so the host unlocks the movement keys (a stopped bot never locks the person out). Not raised by an interrupt (the person took the keys).
    /// </summary>
    public event Action<string>? Stopped;

    /// <summary>M5 7 P7n: the reason <see cref="Stopped"/> carries when the kill switch halts the loop.</summary>
    public const string HaltReason = "halt:kill-switch";

    /// <summary>Where the dial is.</summary>
    public AutopilotMode Mode => _mode;

    /// <summary>True while the current run was started by <c>--autopilot-start</c> (a start by the key or the button clears it).</summary>
    public bool StartedByOption => _unattended;

    /// <summary>
    /// What the host does at the end of an unattended run (M5 item 7 s1): set once, when a run started by <c>--autopilot-start</c> reaches Done by one of its own stops (a stop rule, a failure) and not by a person's request. Null while the run goes on,
    /// after a request (the game stays open) and for runs the key started.
    /// </summary>
    public AutopilotUnattendedEnd? UnattendedEnd => _unattendedEnd;

    /// <summary>
    /// Closes the vitae page if it shows and says whether it did (M5 item 7 s1, <c>m5-autopilot-vitae-panel</c>): the play host wires the shell's page host (panel 15). Retail opens the page itself on a worse vitae (<c>RecvNotice_VitaeChanged</c>,
    /// 11.6096 0x004A7AA0, kept in <c>GameVitaePage</c>); the dial closes it as a person would once the death station has ended. Null (the default): nothing to close.
    /// </summary>
    public Func<bool>? CloseVitaePage { get; set; }

    /// <summary>How many times the dial closed the vitae page this run.</summary>
    public int VitaeCloses { get; private set; }

    /// <summary>
    /// The reconnects the session made that were not the autopilot's own (the person's Reconnect button: <c>GameSession.Reconnects</c> less the autopilot's tries; M5 4 F3, 4-R8). The host sets it; null (the default) turns the
    /// check off. When the number moves under a running dial the dial yields: an interrupt, logged <see cref="ReconnectedLine"/>, instead of starting a try of its own against the person's login.
    /// </summary>
    public Func<int>? ForeignReconnects { get; set; }

    /// <summary>
    /// True while a person holds a movement key (M5 4 F3, 4-R16; the host reads its input): a hand-back is then deferred until the key is up, logged once (<see cref="WaitingForKeyLine"/>). Null (the default): never held.
    /// </summary>
    public Func<bool>? HumanKeyHeld { get; set; }

    /// <summary>M5 7 D145: what asked for the last start or hand-back (<see cref="ResumeReason"/> for /auto, the option for <c>--autopilot-start</c>, else <see cref="KeyReason"/>), or null before one.</summary>
    public string? StartReason { get; private set; }

    /// <summary>M5 7 D145: true while the person's movement keys are locked (the autopilot has the controls).</summary>
    public bool KeysLocked => _moves?.KeyLock.Locked ?? false;

    /// <summary>M5 7 D145: the run's kills so far: the cycles ended, the cycle in progress and the fight in progress (the drill waits for the next kill on it).</summary>
    public int RunKills => _scenario is { } s ? s.Totals.Kills + s.CycleTally.Kills + FightKills() : 0;

    /// <summary>The cycle in which the scripted death was entered (0 until it was): D58's scripted interrupt counts from the first fight after it (4-R10).</summary>
    public int TestDeathCycle { get; private set; }

    /// <summary>True when the run scripts a death (<c>--autopilot-test-death</c>).</summary>
    public bool TestDeathScripted => _options.TestDeath;

    /// <summary>True while a hand-back waits for the person's movement key to be released.</summary>
    public bool HandBackWaiting => _handBackWaiting;

    /// <summary>The current scenario (null before the first start); it is replaced by a hand-back.</summary>
    public AutopilotScenario? Scenario => _scenario;

    /// <summary>How many times a person took the controls this run.</summary>
    public int Interrupted { get; private set; }

    /// <summary>How many times the controls were handed back this run.</summary>
    public int Resumed { get; private set; }

    /// <summary>How many times the kill switch halted the loop this run.</summary>
    public int Halts { get; private set; }

    /// <summary>The cycle, stop and done lines of this run, in order, across hand-backs (the lines of dropped scenarios and the current one's).</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            if (_scenario is { } s)
            {
                for (var i = _linesTaken; i < s.Lines.Count; i++)
                {
                    _lines.Add(s.Lines[i]);
                }

                _linesTaken = s.Lines.Count;
            }

            return _lines;
        }
    }

    /// <inheritdoc/>
    public AutopilotStatus Status
    {
        get
        {
            var state = _scenario is { } s && _mode is not AutopilotMode.Off ? s.Stage : string.Empty;
            return new AutopilotStatus(_mode, state, _decision, _mode is AutopilotMode.Running or AutopilotMode.Stopping ? NextTrigger() : string.Empty, _scenario?.Cycle ?? 0, Interrupted, Resumed);
        }
    }

    /// <summary>
    /// The host's press (/auto through it, the unattended start): off or done starts a run, running asks it to stop, interrupted or halted hands the controls back. A press while it is stopping does nothing but a log line.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="startedBy">When a start is not a person's press: the option that asked for it (<c>--autopilot-start</c>); a stop or a hand-back ignores it.</param>
    /// <param name="reason">M5 7 D145: what asked (<see cref="ResumeReason"/> for /auto), kept as <see cref="StartReason"/> for a start or a hand-back; null: the option's name, else <see cref="KeyReason"/>.</param>
    public void Toggle(IGameSessionView session, string? startedBy = null, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            StartReason = reason ?? startedBy ?? KeyReason;
            _log.Info(LogCategory, "autopilot host: a start or hand-back is asked (reason " + StartReason + ", mode " + _mode.ToString().ToLowerInvariant() + ")");
        }

        switch (_mode)
        {
            case AutopilotMode.Off or AutopilotMode.Done:
                Start(session, startedBy ?? (reason == ResumeReason ? ResumeReason : null)); // /auto from off or done: a resume (P7n's audit says why=resume)
                break;
            case AutopilotMode.Running:
                Stop(KeyReason);
                break;
            case AutopilotMode.Interrupted or AutopilotMode.Halted:
                HandBack(session);
                break;
            default:
                _log.Info(LogCategory, "autopilot host: a stop is already asked; the loop ends at its next safe point");
                break;
        }
    }

    /// <summary>Starts a run from the state the loop finds. Does nothing unless the dial is off or done.</summary>
    /// <param name="session">The session.</param>
    /// <param name="startedBy">The option that asked for the start (<c>--autopilot-start</c>): the run is then unattended (<see cref="UnattendedEnd"/>) and the start is logged; null for a person's press.</param>
    /// <returns>True when a run started.</returns>
    public bool Start(IGameSessionView session, string? startedBy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Off or AutopilotMode.Done))
        {
            return false;
        }

        (Interrupted, Resumed, Halts, _forcedTripDone, _testDeathDone, _stopAsked, TestDeathCycle) = (0, 0, 0, false, false, null, 0);
        (_handBackWaiting, _handBackWhy, _abandonedSeed) = (false, null, null);
        _lines.Clear();
        _linesTaken = 0;
        var resume = startedBy == ResumeReason; // M5 7 P7n (D145): /auto is a person's resume, never an unattended run
        (_unattended, _unattendedEnd, VitaeCloses, _vitaeWatchUntil) = (startedBy is not null && !resume, null, 0, double.NegativeInfinity);
        Install(_create(AutopilotCarry.None), resume ? ResumeReason : "start");
        _mode = AutopilotMode.Running;
        _decision = startedBy is null ? "started: the loop reads the state it finds" : "started by " + startedBy + ": the loop reads the state it finds";
        _log.Info(LogCategory, "autopilot host: started (" + _options + ")" + (resume ? " by " + ResumeReason + ": the audit reads every state a fight needs" : string.Empty));
        if (startedBy is not null && !resume)
        {
            _log.Info(LogCategory, "autopilot: started by " + startedBy);
        }

        Raise();
        return true;
    }

    /// <summary>
    /// Asks the run to stop (a clean stop at its next safe point; <c>stop=request:&lt;why&gt;</c>). While interrupted the controls are handed back first and the stop asked of the fresh loop, so its lines are written; while halted by
    /// the switch the run ends at once (nothing can be sent), with a log line and no done line.
    /// </summary>
    /// <param name="why">Who asked (a token).</param>
    public void Stop(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        switch (_mode)
        {
            case AutopilotMode.Running:
                _scenario!.RequestStop(why);
                _stopAsked = why;
                _mode = AutopilotMode.Stopping;
                _decision = "stop asked (" + why + "): the loop ends at its next safe point";
                Raise();
                break;
            case AutopilotMode.Interrupted when _session is { } session:
                HandBack(session, why);
                break;
            case AutopilotMode.Halted:
                _mode = AutopilotMode.Done;
                _decision = "stopped while halted by the kill switch: nothing was running";
                _log.Warn(LogCategory, "autopilot host: stopped while the kill switch halted it; no station was running, so no done line is written");
                if (_unattended && why == StopFileReason)
                {
                    // M5 7 G2: the owner's stop file ends an unattended run even when the switch halted it: nothing more can be sent, so the game logs off (tried) and quits, exit code 1 (no done line).
                    _unattendedEnd = new AutopilotUnattendedEnd(1, "autopilot: the stop file ended a run halted by the kill switch; the game logs off and quits with exit code 1");
                    _log.Info(LogCategory, _unattendedEnd.Decision);
                }

                Raise();
                break;
        }
    }

    /// <summary>
    /// Hands the controls back: a fresh scenario reads the state it finds and goes on with the carried counts, <c>resumed=</c> +1. Refused while the kill switch is on.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="stopWhy">When set, the fresh loop is asked to stop at once (a stop while interrupted).</param>
    /// <returns>True when the loop resumed.</returns>
    public bool HandBack(IGameSessionView session, string? stopWhy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Interrupted or AutopilotMode.Halted))
        {
            return false;
        }

        if (session.Actions.KillSwitch)
        {
            _decision = "the kill switch is on: turn it off, then hand back";
            _log.Warn(LogCategory, "autopilot host: a hand-back was asked but the kill switch is on; it stays " + _mode.ToString().ToLowerInvariant());
            Raise();
            return false;
        }

        if (HumanKeyHeld?.Invoke() == true)
        {
            // 4-R16: the fresh run's walk would start under the person's held key. Wait for the release (Tick looks each frame), say so once.
            _handBackWhy = stopWhy ?? _handBackWhy;
            if (!_handBackWaiting)
            {
                _handBackWaiting = true;
                _decision = "a hand-back is waiting for the movement key to be released";
                _log.Info(LogCategory, WaitingForKeyLine);
                Raise();
            }

            return false;
        }

        _handBackWaiting = false;
        stopWhy ??= _handBackWhy;
        _handBackWhy = null;
        var afterPerson = _mode == AutopilotMode.Interrupted;
        var old = _scenario!;
        if (afterPerson)
        {
            old.CycleTally.Resumed++;
        }

        Capture(old);
        var carry = new AutopilotCarry(old.Cycles, old.Totals, old.CycleTally, _abandonedSeed?.SecondsUsed ?? old.SecondsUsed, _forcedTripDone, _testDeathDone, _abandonedSeed ?? old.Seed);
        _abandonedSeed = null;
        Install(_create(carry), ResumeReason); // M5 7 P7n (D145): a hand-back is a resume: the fresh scenario's audit reads every state a fight needs
        if (afterPerson)
        {
            Resumed++;
        }

        _mode = AutopilotMode.Running;
        _decision = (afterPerson ? "handed back (" + (StartReason ?? KeyReason) + ")" : "restarted after the kill switch") + ": the loop reads the state it finds (cycle " + _scenario!.Cycle + ")";
        _log.Info(LogCategory, $"autopilot host: {_decision}; interrupted={Interrupted} resumed={Resumed}");
        if ((stopWhy ?? _stopAsked) is { } asked)
        {
            Stop(asked); // a stop asked before the person took the controls still stands
        }

        Raise();
        return true;
    }

    /// <summary>
    /// M5 7 D145, <b>/stop</b>: while it runs or stops, he stops where he stands. No recall and no log off: the run in flight is dropped (its fight's kills folded in, 4-R9; no done line, the run is not over), every queued automation action is
    /// cancelled, every key and hold automation set is released, a swing in progress is called off, and the movement keys are unlocked; <c>interrupted=</c> counts and /auto hands the controls back. In any other mode it does nothing.
    /// Returns the chat line either way.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="via">Who asked, for the log (<c>/stop</c>, the panel button, the drill).</param>
    public string StopHere(IGameSessionView session, string via = "/stop")
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) || _scenario is not { } scenario)
        {
            _log.Info(LogCategory, $"autopilot host: {via} while {_mode.ToString().ToLowerInvariant()}: nothing to stop; nothing else is done");
            return "Lifestone: the autopilot is not running (" + _mode.ToString().ToLowerInvariant() + "); nothing to stop.";
        }

        _quietUnlock = via; // the command's own answer says it; no second chat line
        Interrupt(scenario, ChatStopReason, via);
        _quietUnlock = null;
        _log.Info(LogCategory, "autopilot chat: " + StoppedHereLine);
        return StoppedHereLine;
    }

    /// <summary>The person takes the controls (/stop, or their own Reconnect): the run abandoned with its fight folded in (4-R9), no done line; then the stand-down (<see cref="StandDown"/>).</summary>
    private void Interrupt(AutopilotScenario scenario, string abandonToken, string who)
    {
        scenario.CycleTally.Interrupted++;
        Interrupted++;
        Drop(scenario, abandonToken); // the run in flight is dropped (its fight stops casting and walking); no done line: the run is not over
        var (cancelled, released, attack) = StandDown(abandonToken);
        _mode = AutopilotMode.Interrupted;
        _decision = $"stopped by {who}: the controls are the person's (/auto hands them back)";
        _log.Info(LogCategory, $"autopilot host: stopped by {who} where he stands (no recall, no log off); cancelled {cancelled} queued, released {released} held{(attack ? ", called off the swing" : string.Empty)}; interrupted={Interrupted} resumed={Resumed}");
        Raise();
    }

    /// <summary>
    /// M5 7 D145: the autopilot lets go of everything (a /stop, a halt, any stop it made): the queued automation actions cancelled, then every command, hold and autorun automation holds released (that order: no queued intent presses a key
    /// after the release), and a swing in progress called off (ACE repeats a swing until it is cancelled). The keys' lock follows the mode in <see cref="Raise"/>; the check one frame later reads that nothing is held.
    /// </summary>
    private (int Cancelled, int Released, bool Attack) StandDown(string why)
    {
        if (_session is not { } session)
        {
            return (0, 0, false);
        }

        var (cancelled, released) = session.Actions.Move.StandDownAutomation("the autopilot stood down (" + why + ")");
        var attack = false;
        if (session.Attack.Active && !session.Actions.KillSwitch)
        {
            _ = session.Actions.CancelAttack(ActionSource.Host);
            attack = true;
        }

        _standDownCheck = true;
        return (cancelled, released, attack);
    }

    /// <summary>
    /// M5 7 D145: the check one frame after a stand-down: nothing automation holds on the controller and the keys unlocked; anything left is released again and named. The automation actions queued now are reported, not cancelled: one frame
    /// later they can be another driver's (the re-buff driver goes on once the autopilot stops owning the buffs), which are not the autopilot's to withdraw.
    /// </summary>
    private void CheckStandDown(IGameSessionView session)
    {
        _standDownCheck = false;
        var move = session.Actions.Move;
        var (held, queued, locked) = (move.AutomationHeld, move.QueuedAutomation, move.KeyLock.Locked);
        if (held == 0 && !locked)
        {
            _log.Info(LogCategory, $"{StandDownCheckLine}: ok (held=0 keys=unlocked queued_now={queued} person_held={move.KeyLock.HeldByPerson}; mode {_mode.ToString().ToLowerInvariant()})");
            return;
        }

        var released = move.ReleaseAutomation();
        if (locked)
        {
            move.KeyLock.Unlock("the stand-down check found the keys locked");
        }

        _log.Warn(LogCategory, $"{StandDownCheckLine}: NOT clean (held={held} keys={(locked ? "locked" : "unlocked")} queued_now={queued}); released {released} again, keys unlocked");
    }

    /// <summary>Abandons <paramref name="scenario"/> (the fight's kills folded into the cycle, a cut death's or trip's state kept, the checkpoint written), keeps its lines and seed, and releases the run.</summary>
    private void Drop(AutopilotScenario scenario, string why)
    {
        scenario.Abandon(why);
        _abandonedSeed = scenario.Seed;
        Capture(scenario);
        scenario.Dispose();
    }

    /// <summary>One frame: halts at once when the kill switch is on, else steps the loop. Allocation-free while running unless a status is read.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The host's clock, passed to the scenario.</param>
    public void Tick(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_standDownCheck && _mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            CheckStandDown(session);
        }

        if (_handBackWaiting && _mode is AutopilotMode.Interrupted or AutopilotMode.Halted)
        {
            HandBack(session); // 4-R16: the movement key is up now, or it waits another frame
            return;
        }

        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) || _scenario is not { } scenario)
        {
            return;
        }

        if (session.Actions.KillSwitch)
        {
            Halt(session, scenario);
            return;
        }

        if (ForeignReconnects is { } foreign && foreign() != _reconnectsBase)
        {
            // 4-R8: the person pressed the disconnected screen's Reconnect (the autopilot's own tries do not move this number). Two logins would tear each other down: the dial yields.
            _log.Warn(LogCategory, ReconnectedLine);
            Interrupt(scenario, "person-reconnect", "the person's reconnect");
            return;
        }

        var clock = session.Clock.Now;
        var status = scenario.Step(session, now);
        if (clock < _vitaeWatchUntil)
        {
            TryCloseVitae();
        }

        if (status != ScenarioStatus.Running)
        {
            Capture(scenario);
            var (cancelled, released, _) = StandDown("done"); // D145: a stop it made on its own lets go the same way as /stop
            _mode = AutopilotMode.Done;
            _decision = scenario.Ok ? $"finished: stop={scenario.StopName}" : $"failed: {scenario.Failure}";
            _log.Info(LogCategory, $"autopilot host: the run ended by itself ({_decision}); cancelled {cancelled} queued, released {released} held");
            DecideUnattendedEnd(scenario);
            _log.Info(LogCategory, $"autopilot host: the run stopped by itself (stop={scenario.StopName ?? "none"} ok={(scenario.Ok ? "yes" : "no")}): Stopped is raised (the keys unlock)");
            Stopped?.Invoke(scenario.StopName ?? "stopped");
            Raise();
        }
        else if (clock - _lastRaise >= StatusRefreshSeconds)
        {
            Raise();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_moves is not null)
        {
            // D145: a disposed dial (a host fault, the scene's exit) never leaves the person locked out.
            _moves.KeyLock.Notice -= OnKeyNotice;
            if (_moves.KeyLock.Locked)
            {
                if (_session is not null)
                {
                    _ = StandDown("disposed");
                }

                _moves.KeyLock.Unlock("the autopilot was disposed (" + _mode.ToString().ToLowerInvariant() + ")");
            }

            _moves = null;
        }

        _scenario?.Dispose();
    }

    /// <summary>
    /// The unattended run's end (M5 item 7 s1): a run started by <c>--autopilot-start</c> that stopped by itself (a stop rule, a failure) logs the game off and quits, exit code 0 for ok=yes and 1 otherwise, after the done line (already
    /// written by the scenario); a person's own stop request keeps the game open. The decision is one log line.
    /// </summary>
    private void DecideUnattendedEnd(AutopilotScenario scenario)
    {
        if (!_unattended)
        {
            return;
        }

        var stop = scenario.StopName ?? "none";
        if (stop.StartsWith("request:", StringComparison.Ordinal) && stop != "request:" + StopFileReason)
        {
            _log.Info(LogCategory, $"autopilot: the run was started by --autopilot-start and ended on a stop request (stop={stop}); the game stays open");
            return;
        }

        var exit = scenario.Ok ? 0 : 1;
        var decision = $"autopilot: the run was started by --autopilot-start and ended by itself (stop={stop} ok={(scenario.Ok ? "yes" : "no")}); the game logs off and quits with exit code {exit}";
        _unattendedEnd = new AutopilotUnattendedEnd(exit, decision);
        _log.Info(LogCategory, decision);
    }

    /// <summary>Closes the vitae page if it shows (once a death: logged), then stops looking.</summary>
    private void TryCloseVitae()
    {
        if (CloseVitaePage?.Invoke() == true)
        {
            VitaeCloses++;
            _vitaeWatchUntil = double.NegativeInfinity;
            _log.Info(LogCategory, VitaeClosedLine);
        }
    }

    private void Halt(IGameSessionView session, AutopilotScenario scenario)
    {
        // The Action API cancelled the queued automation actions and released the held keys when the switch went on; the loop is not stepped from this tick on.
        var released = session.Actions.Move.ReleaseAutomation();
        _standDownCheck = true; // D145: the check one frame later (nothing held, the keys unlocked)
        if (session.Attack.Active)
        {
            // ACE repeats a swing until it is cancelled, and the switch refuses the autopilot's own cancel: the host sends the stop itself (a stop is what the switch wants).
            _ = session.Actions.CancelAttack(ActionSource.Host);
        }

        Drop(scenario, "halt");
        Halts++;
        _mode = AutopilotMode.Halted;
        _decision = "the kill switch is on: the loop is paused and sends nothing (turn it off, then /auto restarts the loop)";
        _log.Warn(LogCategory, $"autopilot host: halted by the kill switch (released {released} held); /auto restarts the loop once the switch is off");
        Stopped?.Invoke(HaltReason); // M5 7 P7n (D145): raised for the host; the unlock's own chat line says it (P7m)
        Raise();
    }

    private void Install(AutopilotScenario scenario, string reason)
    {
        if (_scenario is { } old)
        {
            old.ChatNotice -= ForwardNotice;
        }

        scenario.StartReason = reason; // M5 7 P7n (D145): start, or resume (/auto, a hand-back): the audit's why
        scenario.ChatNotice += ForwardNotice;
        _scenario = scenario;
        _linesTaken = 0;
        _reconnectsBase = ForeignReconnects?.Invoke() ?? 0;
        scenario.StateChanged += OnStateChanged;
    }

    /// <summary>M5 7 P7n: a scenario's chat line, passed on to the host's chat window.</summary>
    private void ForwardNotice(string line) => ChatNotice?.Invoke(line);

    /// <summary>Keeps the lines of a scenario that is about to be replaced.</summary>
    private void Capture(AutopilotScenario scenario)
    {
        for (var i = _linesTaken; i < scenario.Lines.Count; i++)
        {
            _lines.Add(scenario.Lines[i]);
        }

        _linesTaken = scenario.Lines.Count;
    }

    private void OnStateChanged(AutopilotTransition t)
    {
        if (t.From == AutopilotState.Death && t.To != AutopilotState.Death)
        {
            // M5 item 7 s1 (m5-autopilot-vitae-panel): the death station ended (any exit), so close the page retail opened on the worse vitae, as a person would; keep looking for a short while in case the vitae message is late.
            _vitaeWatchUntil = (_session?.Clock.Now ?? 0) + VitaeGraceSeconds;
            TryCloseVitae();
        }

        if (t.To == AutopilotState.Trip && t.Why.Contains("forced trip", StringComparison.Ordinal))
        {
            _forcedTripDone = true;
        }
        else if (t.To == AutopilotState.Death && t.Why.Contains("scripted death", StringComparison.Ordinal))
        {
            _testDeathDone = true;
            TestDeathCycle = _scenario?.Cycle ?? 0;
        }

        _decision = $"{AutopilotScenario.StateName(t.From)} -> {AutopilotScenario.StateName(t.To)}: {t.Why}";
        Raise();
    }

    private void Attach(IGameSessionView session)
    {
        _session = session;
        var moves = session.Actions.Move;
        if (ReferenceEquals(moves, _moves))
        {
            return;
        }

        // D145: a person's movement key no longer takes the controls (D98's hand-off is retired); the session's key lock is the dial's to set. A new session's movement (a reconnect's) takes the lock's state over.
        var wasLocked = false;
        if (_moves is not null)
        {
            _moves.KeyLock.Notice -= OnKeyNotice;
            wasLocked = _moves.KeyLock.Locked;
            if (wasLocked)
            {
                _moves.KeyLock.Unlock("the session's movement was replaced");
            }
        }

        _moves = moves;
        moves.KeyLock.Notice += OnKeyNotice;
        if (wasLocked || _mode is AutopilotMode.Running or AutopilotMode.Stopping)
        {
            SyncKeys();
        }
    }

    /// <summary>The lock's rate-limited notice goes to the chat.</summary>
    private void OnKeyNotice(string line) => ChatNotice?.Invoke(line);

    private void Raise()
    {
        _lastRaise = _session?.Clock.Now ?? double.NegativeInfinity;
        SyncKeys();
        Changed?.Invoke();
    }

    /// <summary>
    /// M5 7 D145: the movement keys follow the mode: locked while it runs or stops, unlocked otherwise. An unlock the autopilot made on its own (a stop rule, a failure, the kill switch, a stop-file end) says why in the chat; /stop's own answer
    /// says it for a /stop. Allocation-free when nothing changes.
    /// </summary>
    private void SyncKeys()
    {
        if (_moves is not { } moves)
        {
            return;
        }

        var keys = moves.KeyLock;
        var want = _mode is AutopilotMode.Running or AutopilotMode.Stopping;
        if (want == keys.Locked)
        {
            return;
        }

        if (want)
        {
            keys.Lock("the autopilot " + (_mode == AutopilotMode.Running ? "runs" : "stops") + ", reason " + (StartReason ?? KeyReason));
            return;
        }

        keys.Unlock("the autopilot is " + _mode.ToString().ToLowerInvariant() + ": " + _decision);
        if (_quietUnlock is not null)
        {
            _quietUnlock = null;
            return;
        }

        var line = _mode == AutopilotMode.Halted
            ? "Lifestone: the kill switch halted the autopilot; the controls are yours. Turn the switch off, then type /auto."
            : "Lifestone: the autopilot stopped (" + _decision + "); the controls are yours. Type /auto to start it again.";
        _log.Info(LogCategory, "autopilot chat: " + line);
        ChatNotice?.Invoke(line);
    }

    /// <summary>The nearest things that will change the state: the stop rules, the forced trip, the stock line closest to its trip, the burden.</summary>
    private string NextTrigger()
    {
        var parts = new List<string>(3);
        var session = _session;
        if (_options.Minutes > 0)
        {
            var left = Math.Max(0, (_options.Minutes * 60) - (_scenario?.SecondsUsed ?? 0));
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"stop at {_options.Minutes:0.#} min: {FormatLeft(left)} left"));
        }

        if (_options.Level > 0)
        {
            var level = session is not null && session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var l) ? l.ToString(CultureInfo.InvariantCulture) : "?";
            parts.Add($"stop at level {_options.Level}: level {level}");
        }

        if (_options.TripAfterKills > 0 && !_forcedTripDone)
        {
            parts.Add($"trip at {_options.TripAfterKills} kills: {(_scenario?.CycleTally.Kills ?? 0) + FightKills()} so far");
        }

        if ((_scenario?.Counters ?? _counters) is { Active: true } counters && session is not null) // M5 7 P7s (D148): the scenario's counters know the floor-only lines
        {
            var verdict = counters.Check(session.State);
            if (verdict.Kind == LogisticsKind.Unread)
            {
                parts.Add("stock: not read yet");
            }
            else
            {
                var best = -1;
                var margin = int.MaxValue;
                for (var i = 0; i < counters.LineCount; i++)
                {
                    var line = counters.LineAt(i);
                    if (counters.IsFloorOnly(i))
                    {
                        if (counters.AtFloor(i))
                        {
                            parts.Add($"{line.Name} at its floor ({counters.HeldOf(i)} held): its spells are put away"); // M5 7 P7s (D148): no stop
                        }

                        continue;
                    }

                    var m = counters.HeldOf(i) - (counters.IsCarried(i) ? line.StopUnder : line.TripUnder);
                    if (m < margin)
                    {
                        (best, margin) = (i, m);
                    }
                }

                if (best >= 0)
                {
                    var line = counters.LineAt(best);
                    parts.Add(counters.IsCarried(best)
                        ? $"stop at {line.StopUnder} {line.Name}: {counters.HeldOf(best)} held"
                        : $"trip at {line.TripUnder} {line.Name}: {counters.HeldOf(best)} held");
                }
            }
        }

        return string.Join("; ", parts);
    }

    /// <summary>The kills of the fight in progress (they join the cycle when it ends).</summary>
    private int FightKills() => _scenario?.CurrentRun switch
    {
        Scenarios.Journey.IStationInner { Scenario: IStoppableFight inner } => inner.Kills,
        IStoppableFight fight => fight.Kills,
        _ => 0,
    };

    private static string FormatLeft(double seconds) =>
        seconds >= 90 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds / 60):0} min") : string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds):0} s");
}

/// <summary>What the play host does at the end of an unattended run (<c>--autopilot-start</c>): log off, quit, and exit with <paramref name="ExitCode"/>.</summary>
/// <param name="ExitCode">0 for ok=yes, 1 otherwise.</param>
/// <param name="Decision">The one log line that says why.</param>
public sealed record AutopilotUnattendedEnd(int ExitCode, string Decision);

/// <summary>
/// When D58's scripted interrupt starts counting (M5 4 F3, 4-R10; DoD 9 asks for "mid-fight"): not at the dial's start but at the first Fight state, and with the scripted death on (<c>--autopilot-test-death</c>) at the first Fight state
/// of the cycle <b>after</b> the death's, so the key never lands in the buff, the trip or the death station. Engine-free so the rule is tested; the game's scripted hand-off asks it each frame until it is armed.
/// </summary>
/// <param name="deathScripted">True when the run scripts a death (<see cref="AutopilotOptions.TestDeath"/>).</param>
public sealed class AutopilotInterruptArm(bool deathScripted)
{
    /// <summary>True once the first qualifying Fight state was seen (it stays true).</summary>
    public bool Armed { get; private set; }

    /// <summary>Looks at the dial; returns <see cref="Armed"/>.</summary>
    /// <param name="logic">The dial.</param>
    public bool Update(AutopilotHostLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (Armed)
        {
            return true;
        }

        if (logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping && logic.Scenario is { State: AutopilotState.Fight } scenario
            && (!deathScripted || (logic.TestDeathCycle > 0 && scenario.Cycle > logic.TestDeathCycle)))
        {
            Armed = true;
        }

        return Armed;
    }
}
