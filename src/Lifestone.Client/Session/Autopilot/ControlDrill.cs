using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// What the control drill does with the game's own input (M5 7 D145; the play host's Godot hands, a fake in the tests). Nothing here is a server cheat and no key reaches the operating system: the keys go through the game's input
/// path and the lines through the chat window's own entry.
/// </summary>
public interface IControlDrillHands
{
    /// <summary>Presses (<paramref name="down"/> true) or releases Move Forward's key through the game's own input path. False when the key map has no key for it.</summary>
    bool Forward(bool down);

    /// <summary>Types <paramref name="line"/> into the main chat's entry and sends it as a person's Enter does. False when there is no chat window.</summary>
    bool Type(string line);

    /// <summary>Saves a frame of the window as <c>&lt;name&gt;.png</c> in the drill's folder (logged by the hands).</summary>
    void Frame(string name);

    /// <summary>The player's body now, or null when it is not placed.</summary>
    CellFrame? Position { get; }

    /// <summary>The main chat window's text so far (the drill reads the lines that appeared after a step).</summary>
    string ChatText { get; }
}

/// <summary>
/// The live proof of taking over the controls and giving them back (M5 7 D145, the owner: double-check the hand-over of control and the hand-back; <c>--autopilot-control-drill=DIR</c>, <c>tools/soak.ps1 -ControlDrill</c>).
/// Four steps, each logged with the <see cref="Prefix"/> prefix, each ending in one verdict line (<c>control drill: step=&lt;id&gt; verdict=pass|fail ...</c>) that reads the state at that moment and never trusts the step before, and
/// each saving a frame:
/// <list type="bullet">
/// <item><description><b>a</b>, mid-fight: Move Forward held 1 s does nothing (the lock swallowed it), the notice shows in the chat, the autopilot still runs with the keys locked.</description></item>
/// <item><description><b>b1</b>, mid-fight: <c>/stop</c>; he stops where he stands (no teleport: the body moved at most <see cref="InPlaceMetres"/>), nothing is held or queued, the keys are unlocked. <b>b2</b>: Move Forward held 1 s moves him
/// (at least <see cref="MovedMetres"/>).</description></item>
/// <item><description><b>c1</b>, stopped: a bad state made through the person's own paths: walked about <see cref="OffSpotMetres"/> off the spot, the stance set to peace and the wielded caster moved into a side pack (the actions a person's
/// key and drag send). <b>c2</b>: <c>/auto</c>, then at the next kill: running, the keys locked, the stance magic and a caster wielded; the chat lines that appeared are logged.</description></item>
/// <item><description><b>d1</b>, on the next town trip, mid-walk: <c>/stop</c> (as b1); <b>d2</b>: <c>/auto</c> after <see cref="TripPauseSeconds"/> s (running, keys locked); <b>d3</b>: the trip finishes.</description></item>
/// </list>
/// Then one <c>control drill: finished</c> line with every verdict. A step that times out logs its verdict as fail with the reason, and the drill ends there. Engine-free: the play host ticks it after the dial.
/// </summary>
public sealed class ControlDrill
{
    /// <summary>The prefix of every drill line.</summary>
    public const string Prefix = "control drill:";

    /// <summary>The log category (the autopilot's).</summary>
    public const string LogCategory = "autopilot";

    /// <summary>How long Move Forward is held in steps a and b2, in seconds (the owner's figure: held 1 s).</summary>
    public const double HoldSeconds = 1;

    /// <summary>How long the state is read after a press or a typed line, in seconds (the body settles, the server answers).</summary>
    public const double SettleSeconds = 1.5;

    /// <summary>Seconds the fight state must hold before step a (mid-fight, not its first frame).</summary>
    public const double FightSettleSeconds = 3;

    /// <summary>At most this far, in metres, the body moves after a /stop and still stopped "in place" (a step in flight finishes; a teleport is far more).</summary>
    public const double InPlaceMetres = 3;

    /// <summary>At least this far, in metres, Move Forward held 1 s must move him after a /stop.</summary>
    public const double MovedMetres = 1;

    /// <summary>How far step c walks him off the spot, in metres (the owner's figure: about 10 m).</summary>
    public const double OffSpotMetres = 10;

    /// <summary>The longest walk off the spot, in seconds (a wall may stop him: the distance reached is logged).</summary>
    public const double OffSpotWalkSeconds = 8;

    /// <summary>The pause between d's /stop and its /auto, in seconds (the owner's figure: about 5 s).</summary>
    public const double TripPauseSeconds = 5;

    /// <summary>The longest wait for a state a step needs (the first fight, a kill, the trip, its end), in seconds.</summary>
    public const double WaitSeconds = 3600;

    /// <summary>The longest wait for the stance or the caster's move to read back, in seconds.</summary>
    public const double ReadBackSeconds = 10;

    /// <summary>The trip must have run this long before d's /stop (mid-walk, not its first cast), in seconds.</summary>
    public const double TripSettleSeconds = 10;

    /// <summary>After this long in the trip d's /stop is typed even when no walk was seen, in seconds (logged).</summary>
    public const double TripWalkWaitSeconds = 25;

    private enum Step
    {
        WaitFight,
        AHold,
        ASettle,
        BStop,
        BSettle,
        BHold,
        BHoldSettle,
        CWalk,
        CPeace,
        CCaster,
        CAuto,
        CWaitKill,
        DWaitTrip,
        DStop,
        DPause,
        DAutoSettle,
        DWaitEnd,
        Done,
    }

    private readonly IControlDrillHands _hands;
    private readonly ILogSink _log;
    private readonly List<string> _verdicts = [];
    private Step _step = Step.WaitFight;
    private double _since = double.NaN;
    private double _fightSince = double.NaN;
    private CellFrame? _mark;
    private CellFrame? _spot;
    private int _swallowed;
    private int _notices;
    private int _chatMark;
    private int _kills;
    private uint _caster;
    private int _tripLines;
    private bool _tripSeen;
    private double _tripSince;
    private CellFrame? _walkSample;
    private double _walkSampleAt = double.NaN;
    private bool _forwardDown;

    /// <summary>A drill over <paramref name="hands"/>.</summary>
    public ControlDrill(IControlDrillHands hands, ILogSink log)
    {
        _hands = hands ?? throw new ArgumentNullException(nameof(hands));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>True once every step ran, or a step failed and ended the drill.</summary>
    public bool Finished => _step == Step.Done;

    /// <summary>The verdict lines so far (<c>step=&lt;id&gt; verdict=...</c>, without the prefix).</summary>
    public IReadOnlyList<string> Verdicts => _verdicts;

    /// <summary>The distance between two positions in metres (across landblocks by their world offsets).</summary>
    public static double Metres(CellFrame a, CellFrame b)
    {
        if ((a.CellId >> 16) == (b.CellId >> 16))
        {
            return Vector3.Distance(a.Origin, b.Origin);
        }

        var (ax, ay) = Grounds.GroundDraft.WorldMetres(a.CellId, a.Origin);
        var (bx, by) = Grounds.GroundDraft.WorldMetres(b.CellId, b.Origin);
        var dz = a.Origin.Z - b.Origin.Z;
        return Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)) + (dz * dz));
    }

    /// <summary>One frame, after the dial's tick.</summary>
    /// <param name="logic">The dial.</param>
    /// <param name="session">The session.</param>
    /// <param name="now">The host's clock in seconds.</param>
    public void Tick(AutopilotHostLogic logic, IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(logic);
        ArgumentNullException.ThrowIfNull(session);
        if (double.IsNaN(_since))
        {
            _since = now;
            Info("armed: it waits for the autopilot to run and fight for " + F(FightSettleSeconds) + " s, then runs steps a, b, c and d (D145)");
        }

        var held = now - _since;
        var keys = session.Actions.Move.KeyLock;
        switch (_step)
        {
            case Step.WaitFight:
                if (logic.Mode == AutopilotMode.Running && logic.Scenario is { State: AutopilotState.Fight })
                {
                    if (double.IsNaN(_fightSince))
                    {
                        _fightSince = now;
                    }
                    else if (now - _fightSince >= FightSettleSeconds)
                    {
                        (_swallowed, _notices, _chatMark, _mark) = (keys.Swallowed, keys.Notices, _hands.ChatText.Length, _hands.Position);
                        Info($"step=a begins: Move Forward held {F(HoldSeconds)} s mid-fight (mode {Mode(logic)}, keys {(keys.Locked ? "locked" : "unlocked")}, cycle {logic.Scenario?.Cycle}, kills {logic.RunKills}, at {Where(_mark)})");
                        Press(true);
                        Next(Step.AHold, now);
                    }
                }
                else
                {
                    _fightSince = double.NaN;
                    TimeOut(held, "a", "the autopilot never ran a fight for " + F(FightSettleSeconds) + " s");
                }

                break;
            case Step.AHold:
                if (held >= HoldSeconds)
                {
                    Press(false);
                    Next(Step.ASettle, now);
                }

                break;
            case Step.ASettle:
                if (held >= SettleSeconds)
                {
                    var swallowed = keys.Swallowed - _swallowed;
                    var shown = NewChat(_chatMark).Contains(HumanKeyLock.NoticeLine, StringComparison.Ordinal);
                    var running = logic.Mode == AutopilotMode.Running;
                    var moved = Distance(_mark, _hands.Position);
                    Verdict("a", swallowed >= 1 && shown && running && keys.Locked, $"swallowed={swallowed} notices={keys.Notices - _notices} notice_in_chat={Yes(shown)} mode={Mode(logic)} keys={(keys.Locked ? "locked" : "unlocked")} moved_m={moved} (the autopilot's own steps count in moved_m)");
                    _hands.Frame("control-drill-a-locked");
                    Next(Step.BStop, now);
                }

                break;
            case Step.BStop:
                if (logic.Mode != AutopilotMode.Running)
                {
                    Fail("b1", "precondition: the autopilot is not running (mode " + Mode(logic) + ")");
                    break;
                }

                if (logic.Scenario?.State != AutopilotState.Fight)
                {
                    TimeOut(held, "b1", "no fight to stop mid-way (state " + logic.Scenario?.Stage + ")");
                    break;
                }

                (_mark, _chatMark) = (_hands.Position, _hands.ChatText.Length);
                Info($"step=b begins: typing /stop mid-fight at {Where(_mark)} (state {logic.Scenario?.Stage})");
                if (!_hands.Type("/stop"))
                {
                    Fail("b1", "there is no chat window to type /stop into");
                    break;
                }

                Info("step=b: /stop typed; mode now " + Mode(logic) + ", keys " + (keys.Locked ? "locked" : "unlocked"));
                Next(Step.BSettle, now);
                break;
            case Step.BSettle:
                if (held >= SettleSeconds)
                {
                    var after = _hands.Position;
                    var moved = Distance(_mark, after);
                    var move = session.Actions.Move;
                    var stopped = logic.Mode == AutopilotMode.Interrupted;
                    var inPlace = _mark is { } a && after is { } b && Metres(a, b) <= InPlaceMetres;
                    var answered = NewChat(_chatMark).Contains(AutopilotHostLogic.StoppedHereLine, StringComparison.Ordinal);
                    Verdict("b1", stopped && inPlace && !keys.Locked && move.AutomationHeld == 0 && move.QueuedAutomation == 0 && answered, $"mode={Mode(logic)} before={Where(_mark)} after={Where(after)} moved_m={moved} in_place={Yes(inPlace)} keys={(keys.Locked ? "locked" : "unlocked")} held={move.AutomationHeld} queued={move.QueuedAutomation} answer_in_chat={Yes(answered)}");
                    _hands.Frame("control-drill-b-stopped");
                    if (!stopped || keys.Locked)
                    {
                        End();
                        break;
                    }

                    (_mark, _swallowed) = (after, keys.Swallowed);
                    Info($"step=b2 begins: Move Forward held {F(HoldSeconds)} s after /stop at {Where(_mark)}");
                    Press(true);
                    Next(Step.BHold, now);
                }

                break;
            case Step.BHold:
                if (held >= HoldSeconds)
                {
                    Press(false);
                    Next(Step.BHoldSettle, now);
                }

                break;
            case Step.BHoldSettle:
                if (held >= SettleSeconds)
                {
                    var after = _hands.Position;
                    var moved = _mark is { } a && after is { } b ? Metres(a, b) : -1;
                    Verdict("b2", moved >= MovedMetres && keys.Swallowed == _swallowed && logic.Mode == AutopilotMode.Interrupted, $"moved_m={F(moved)} from={Where(_mark)} to={Where(after)} swallowed_since={keys.Swallowed - _swallowed} mode={Mode(logic)}");
                    _hands.Frame("control-drill-b-moved");
                    _spot = after;
                    Info($"step=c begins: the bad state: walk about {F(OffSpotMetres)} m off the spot at {Where(_spot)}, peace, the caster into a side pack");
                    Press(true);
                    Next(Step.CWalk, now);
                }

                break;
            case Step.CWalk:
                {
                    var off = _spot is { } s && _hands.Position is { } p ? Metres(s, p) : -1;
                    if (off >= OffSpotMetres || held >= OffSpotWalkSeconds)
                    {
                        Press(false);
                        Info($"step=c: walked {F(off)} m off the spot in {F(held)} s" + (off < OffSpotMetres ? " (stopped short: a wall or a slope; the drill goes on)" : string.Empty));
                        if (session.State.Self.Qualities.CombatMode != (int)CombatMode.NonCombat)
                        {
                            var handle = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Ui);
                            Info($"step=c: peace asked as the person's stance key does (action {handle.Id}, stance was {session.State.Self.Qualities.CombatMode})");
                        }

                        Next(Step.CPeace, now);
                    }
                }

                break;
            case Step.CPeace:
                if (session.State.Self.Qualities.CombatMode == (int)CombatMode.NonCombat || held >= ReadBackSeconds)
                {
                    Info($"step=c: the stance reads {session.State.Self.Qualities.CombatMode} ({(session.State.Self.Qualities.CombatMode == (int)CombatMode.NonCombat ? "peace" : "NOT peace after " + F(held) + " s")})");
                    _caster = WieldedCaster(session);
                    var pack = SidePack(session);
                    if (_caster == 0 || pack == 0)
                    {
                        Info($"step=c: no caster to move (caster=0x{_caster:X8} side_pack=0x{pack:X8}): the caster stays where it is");
                        Next(Step.CAuto, now);
                        break;
                    }

                    var move = session.Actions.MoveToPack(_caster, pack, 0, ActionSource.Ui);
                    Info($"step=c: the wielded caster 0x{_caster:X8} is moved into the side pack 0x{pack:X8} as a person's drag sends it (action {move.Id})");
                    Next(Step.CCaster, now);
                }

                break;
            case Step.CCaster:
                if (session.State.Self.Inventory.Find(_caster).Kind == InventoryPlaceKind.InSidePack || held >= ReadBackSeconds)
                {
                    Next(Step.CAuto, now);
                }

                break;
            case Step.CAuto:
                {
                    var off = Distance(_spot, _hands.Position);
                    var stance = session.State.Self.Qualities.CombatMode;
                    var place = _caster == 0 ? "none" : session.State.Self.Inventory.Find(_caster).Kind.ToString();
                    var bad = stance == (int)CombatMode.NonCombat && place == nameof(InventoryPlaceKind.InSidePack);
                    Verdict("c1", bad && logic.Mode == AutopilotMode.Interrupted && !keys.Locked, $"off_spot_m={off} stance={stance} caster=0x{_caster:X8} caster_place={place} mode={Mode(logic)} keys={(keys.Locked ? "locked" : "unlocked")}");
                    _hands.Frame("control-drill-c-bad-state");
                    (_kills, _chatMark, _mark) = (logic.RunKills, _hands.ChatText.Length, _hands.Position);
                    if (!_hands.Type("/auto"))
                    {
                        Fail("c2", "there is no chat window to type /auto into");
                        break;
                    }

                    Info($"step=c: /auto typed (mode now {Mode(logic)}, keys {(keys.Locked ? "locked" : "unlocked")}, kills {_kills}); waiting for the next kill");
                    Next(Step.CWaitKill, now);
                }

                break;
            case Step.CWaitKill:
                if (logic.RunKills > _kills)
                {
                    var stance = session.State.Self.Qualities.CombatMode;
                    var caster = WieldedCaster(session);
                    var running = logic.Mode == AutopilotMode.Running;
                    LogChat(_chatMark);
                    Verdict("c2", running && keys.Locked && stance == (int)CombatMode.Magic && caster != 0, $"kills={_kills}->{logic.RunKills} after_s={F(held)} mode={Mode(logic)} keys={(keys.Locked ? "locked" : "unlocked")} stance={stance} caster_wielded=0x{caster:X8} from_spot_m={Distance(_spot, _hands.Position)} audits={logic.Scenario?.Audits} audit='{logic.Scenario?.LastAudit}'");
                    _hands.Frame("control-drill-c-resumed");
                    Next(Step.DWaitTrip, now);
                }
                else
                {
                    if (logic.Mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
                    {
                        LogChat(_chatMark);
                        Fail("c2", "the autopilot is not running after /auto (mode " + Mode(logic) + ": " + logic.Status.LastDecision + ")");
                        break;
                    }

                    TimeOut(held, "c2", "no kill after /auto");
                }

                break;
            case Step.DWaitTrip:
                if (logic.Mode == AutopilotMode.Running && logic.Scenario is { State: AutopilotState.Trip })
                {
                    var p = _hands.Position;
                    if (!_tripSeen)
                    {
                        _tripSeen = true;
                        _tripSince = now;
                        Info($"step=d: the trip began ({logic.Scenario.Stage}); /stop waits until it walks (at least {F(TripSettleSeconds)} s in)");
                    }

                    var walking = false;
                    if (double.IsNaN(_walkSampleAt) || now - _walkSampleAt >= 1)
                    {
                        walking = _walkSample is { } was && p is { } here && Metres(was, here) > 0.5; // moved over half a metre in the last second
                        (_walkSample, _walkSampleAt) = (p, now);
                    }

                    var inTrip = now - _tripSince;
                    if (inTrip >= TripSettleSeconds && (walking || inTrip >= TripWalkWaitSeconds))
                    {
                        (_mark, _chatMark) = (p, _hands.ChatText.Length);
                        _tripLines = TripLines(logic);
                        Info($"step=d begins: typing /stop on the trip ({logic.Scenario.Stage}, {F(inTrip)} s in, {(walking ? "walking" : "no walk seen")}) at {Where(_mark)}");
                        if (!_hands.Type("/stop"))
                        {
                            Fail("d1", "there is no chat window to type /stop into");
                            break;
                        }

                        Next(Step.DStop, now);
                    }
                }
                else
                {
                    _tripSeen = false;
                    if (logic.Mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
                    {
                        Fail("d1", "precondition: the autopilot is not running (mode " + Mode(logic) + ")");
                        break;
                    }

                    TimeOut(held, "d1", "no town trip began (the run's --autopilot-trip-after-kills or its stock starts one)");
                }

                break;
            case Step.DStop:
                if (held >= SettleSeconds)
                {
                    var after = _hands.Position;
                    var move = session.Actions.Move;
                    var stopped = logic.Mode == AutopilotMode.Interrupted;
                    var inPlace = _mark is { } a && after is { } b && Metres(a, b) <= InPlaceMetres;
                    Verdict("d1", stopped && inPlace && !keys.Locked && move.AutomationHeld == 0 && move.QueuedAutomation == 0, $"mode={Mode(logic)} moved_m={Distance(_mark, after)} in_place={Yes(inPlace)} keys={(keys.Locked ? "locked" : "unlocked")} held={move.AutomationHeld} queued={move.QueuedAutomation}");
                    _hands.Frame("control-drill-d-stopped");
                    if (!stopped)
                    {
                        End();
                        break;
                    }

                    Next(Step.DPause, now);
                }

                break;
            case Step.DPause:
                if (held >= TripPauseSeconds)
                {
                    if (!_hands.Type("/auto"))
                    {
                        Fail("d2", "there is no chat window to type /auto into");
                        break;
                    }

                    Info("step=d: /auto typed after " + F(held) + " s stopped");
                    _tripSeen = false; // d3 watches the resumed loop's own trip
                    Next(Step.DAutoSettle, now);
                }

                break;
            case Step.DAutoSettle:
                if (logic.Scenario is { State: AutopilotState.Trip })
                {
                    _tripSeen = true; // the resumed loop is on the trip again
                }

                if (held >= SettleSeconds)
                {
                    Verdict("d2", logic.Mode == AutopilotMode.Running && keys.Locked, $"mode={Mode(logic)} keys={(keys.Locked ? "locked" : "unlocked")} state={logic.Scenario?.Stage} decision='{logic.Status.LastDecision}'");
                    Next(Step.DWaitEnd, now);
                }

                break;
            case Step.DWaitEnd:
                if (logic.Scenario is { State: AutopilotState.Trip })
                {
                    _tripSeen = true; // the resumed loop is on the trip again (after its audit)
                }

                if (TripLines(logic) > _tripLines || (_tripSeen && logic.Scenario is { State: not AutopilotState.Trip }))
                {
                    // The trip's end: its cycle line, or the trip state left (a cut trip runs again by its counters, and may find none due: no cycle line then).
                    LogChat(_chatMark);
                    var tripLine = TripLines(logic) > _tripLines ? logic.Lines.LastOrDefault(static l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal) && l.Contains(" state=trip", StringComparison.Ordinal)) : null;
                    Verdict("d3", logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping, $"the trip finished after_s={F(held)} mode={Mode(logic)} state={logic.Scenario?.Stage} decision='{logic.Status.LastDecision}' cycle_line='{tripLine ?? "none"}'");
                    _hands.Frame("control-drill-d-trip-done");
                    End();
                }
                else if (logic.Mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
                {
                    LogChat(_chatMark);
                    Fail("d3", "the autopilot stopped before the trip finished (mode " + Mode(logic) + ": " + logic.Status.LastDecision + ")");
                }
                else
                {
                    TimeOut(held, "d3", "the trip did not finish");
                }

                break;
        }
    }

    /// <summary>The wielded caster's id (ItemType Caster 0x8000 among the equipped items), or 0.</summary>
    public static uint WieldedCaster(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var item in session.State.Self.Inventory.Equipped)
        {
            if (session.State.Objects.TryGet(item.Guid, out var obj) && (obj.ItemType & 0x8000) != 0)
            {
                return item.Guid;
            }
        }

        return 0;
    }

    /// <summary>The first side pack (a container, not a foci) in the main pack, or 0.</summary>
    public static uint SidePack(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var pack in session.State.Self.Inventory.SidePacks)
        {
            if (pack.ContainerType == 1)
            {
                return pack.Guid;
            }
        }

        return 0;
    }

    private static int TripLines(AutopilotHostLogic logic) => logic.Lines.Count(static l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal) && l.Contains(" state=trip", StringComparison.Ordinal));

    private static string Mode(AutopilotHostLogic logic) => logic.Mode.ToString().ToLowerInvariant();

    private static string Yes(bool b) => b ? "yes" : "no";

    private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Where(CellFrame? p) => p is { } f ? string.Create(CultureInfo.InvariantCulture, $"0x{f.CellId:X8} ({f.Origin.X:0.0}, {f.Origin.Y:0.0}, {f.Origin.Z:0.0})") : "unplaced";

    private static string Distance(CellFrame? a, CellFrame? b) => a is { } x && b is { } y ? F(Metres(x, y)) : "n/a";

    private string NewChat(int mark)
    {
        var text = _hands.ChatText;
        return mark < text.Length ? text[mark..] : string.Empty;
    }

    private void LogChat(int mark)
    {
        foreach (var line in NewChat(mark).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Info("chat: " + line);
        }
    }

    private void Press(bool down)
    {
        if (down == _forwardDown)
        {
            return;
        }

        _forwardDown = down;
        if (!_hands.Forward(down))
        {
            _log.Error(LogCategory, Prefix + " the key map has no keyboard key for Move Forward that the game's input can press; nothing was pressed (TODO(m5-autopilot-test-key))");
            return;
        }

        Info(down ? "Move Forward down" : "Move Forward up");
    }

    private void Next(Step step, double now)
    {
        _step = step;
        _since = now;
    }

    private void TimeOut(double held, string id, string why)
    {
        if (held >= WaitSeconds)
        {
            Fail(id, why + " within " + F(WaitSeconds) + " s");
        }
    }

    private void Fail(string id, string why)
    {
        Verdict(id, false, why);
        End();
    }

    private void Verdict(string id, bool pass, string fields)
    {
        var line = "step=" + id + " verdict=" + (pass ? "pass" : "fail") + " " + fields;
        _verdicts.Add(line);
        if (pass)
        {
            Info(line);
        }
        else
        {
            _log.Warn(LogCategory, Prefix + " " + line);
        }
    }

    private void End()
    {
        Press(false);
        _step = Step.Done;
        Info("finished: " + string.Join(' ', _verdicts.Select(static v => v[5..v.IndexOf(' ', StringComparison.Ordinal)] + "=" + (v.Contains("verdict=pass", StringComparison.Ordinal) ? "pass" : "fail"))));
    }

    private void Info(string text) => _log.Info(LogCategory, Prefix + " " + text);
}
