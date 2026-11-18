using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>
/// The steer desk's game-thread side (the steer contract): answers the probes and the Control orders, writes the live picture and the event stream, and refuses every kind not built yet by name, <c>not-built</c>. Ticked once a frame by the play host between
/// the stop file and the dial, only when <c>--autopilot-steer-dir</c> was given. A person wins: the kill switch and a person's stop refuse every order that would move the character.
/// </summary>
public sealed class OrderDesk
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "autopilot steer";

    /// <summary>The most orders answered in one frame (a backlog must not cost a visible hitch).</summary>
    public const int MaxOrdersPerFrame = 8;

    /// <summary>Seconds between two writes of the live picture when nothing changed.</summary>
    public const double StatusSeconds = 2;

    /// <summary>The fewest seconds between two writes of the live picture when the dial keeps changing.</summary>
    public const double StatusMinSeconds = 0.25;

    /// <summary>The fewest seconds between two screenshots: one asked for sooner is refused by name (<c>too-soon</c>) and nothing is read back.</summary>
    public const double MinCaptureSeconds = 10;

    /// <summary>What a <c>start</c> presses the dial as: the run stays unattended, with the soak's tested ends.</summary>
    public const string StartedBy = "steer";

    private readonly SteerSpool _spool;
    private readonly ILogSink _log;
    private readonly string _run;
    private readonly string _build;
    private readonly double _rebuffMarginSeconds;
    private readonly Func<QuestTimers?> _timers;
    private readonly Action<string> _press;
    private readonly Action? _withdrawPress;
    private readonly SteerHistory _history = new();
    private readonly bool _observeOnly;
    private readonly ISteerCamera? _camera;
    private (SteerOrder Order, SteerShot Shot)? _shot;
    private double _lastCapture = double.NegativeInfinity;
    private readonly List<SteerOrder> _running = [];
    private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
    private string? _overlayText;
    private SteerOverlay? _overlay;
    private int? _seatedOverlay;
    private AutopilotScenario? _seatedScenario;
    private readonly SteerEventStream _events;
    private AutopilotHostLogic? _logic;
    private bool _dirty = true;
    private double _lastStatus = double.NegativeInfinity;
    private long _sequence;
    private readonly CommandRunner? _orders;
    private readonly bool _ordersOn;
    private readonly List<(SteerOrder Order, CommandEntry Entry, int Version)> _commands = [];

    /// <summary>A desk over <paramref name="spool"/>.</summary>
    /// <param name="spool">The files.</param>
    /// <param name="run">The run folder's name, for the live picture.</param>
    /// <param name="build">This build's version, for the live picture.</param>
    /// <param name="rebuffMarginSeconds">The seconds left under which a buff counts as due.</param>
    /// <param name="timers">The quest timers in memory, or null when none are known.</param>
    /// <param name="log">The log.</param>
    /// <param name="press">The host's press of the dial, with who asked: it reads the journey's parts first when it must.</param>
    /// <param name="overlayText">The overlay file's text as the host read it before the spool started, or null when there is none.</param>
    /// <param name="withdrawPress">Takes back a press still waiting for the journey's parts, when its order's life has ended.</param>
    /// <param name="observeOnly">True: every order that is not a probe is refused by name (<c>observe-only</c>) and logged, whatever else would have allowed it.</param>
    /// <param name="camera">The host's camera for <c>capture</c>; null: refused by name (<c>no-camera</c>).</param>
    /// <param name="orders">The command runner the <c>command</c>, <c>trip</c> and <c>run_quest</c> orders go to (the chat's own); null: they are refused by name (<c>no-runner</c>).</param>
    /// <param name="ordersOn"><c>--autopilot-steer-orders=on</c>: the command orders are taken, observe-only or not; off: refused by name (<c>orders-off</c>, or <c>observe-only</c> under it).</param>
    public OrderDesk(SteerSpool spool, string run, string build, double rebuffMarginSeconds, Func<QuestTimers?> timers, ILogSink log, Action<string> press, string? overlayText = null, Action? withdrawPress = null, bool observeOnly = false, ISteerCamera? camera = null, CommandRunner? orders = null, bool ordersOn = false)
    {
        _orders = orders;
        _ordersOn = ordersOn;
        _observeOnly = observeOnly;
        _camera = camera;
        _withdrawPress = withdrawPress;
        _press = press ?? throw new ArgumentNullException(nameof(press));
        _overlayText = overlayText;
        _spool = spool ?? throw new ArgumentNullException(nameof(spool));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _timers = timers ?? throw new ArgumentNullException(nameof(timers));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _rebuffMarginSeconds = rebuffMarginSeconds;
        _events = new SteerEventStream(line => _spool.Post(new SteerWrite(SteerWriteKind.Append, SteerSpool.EventsFile, line + "\n")));
    }

    /// <summary>True while the desk answers orders.</summary>
    public bool Open { get; private set; } = true;

    /// <summary>Why the desk closed; null while it is open.</summary>
    public string? ClosedWhy { get; private set; }

    /// <summary>How many orders the desk has answered, for the checks.</summary>
    public int Answered { get; private set; }

    /// <summary>Rule 4 of the contract: the desk opens only against a server on this computer (or an offline session), by the check the automation already uses.</summary>
    /// <param name="session">The session.</param>
    public static bool ServerIsLocal(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.State.IsOffline || session.Actions is ActionApi { ServerIsLoopback: true };
    }

    /// <summary>One frame.</summary>
    /// <param name="session">The session.</param>
    /// <param name="logic">The dial.</param>
    public void Tick(IGameSessionView session, AutopilotHostLogic logic)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logic);
        if (!Open)
        {
            return;
        }

        Attach(logic);
        if (_spool.Fault is { } fault)
        {
            Close(fault, session);
            return;
        }

        var utc = session.Clock.Time.GetUtcNow();
        EnsureOverlay(session);
        NoteSeated(logic);
        for (var i = 0; i < MaxOrdersPerFrame && _spool.TryTake(out var inbound); i++)
        {
            Answer(inbound, session, logic, utc);
        }

        TickRunning(session, logic, utc);
        TickCommands(session, logic, utc);
        TickCapture(session, logic, utc);
        _history.Tick(session, logic, utc);
        _events.Tick(session, logic, utc);
        WriteStatusIfDue(session, logic, utc);
    }

    /// <summary>Closes the desk: it answers nothing more and the run carries on (rule 7). Says so in the log, the event stream and a last live picture.</summary>
    /// <param name="why">Why.</param>
    /// <param name="session">The session when one is known: the last picture's source.</param>
    /// <param name="fault">False for the desk's normal end when the game closes: an information line and a <c>notice</c>, not an error and a <c>fault</c>.</param>
    public void Close(string why, IGameSessionView? session, bool fault = true)
    {
        if (!Open)
        {
            return;
        }

        Open = false;
        ClosedWhy = why;
        var line = "autopilot steer: desk-closed: " + why;
        if (fault)
        {
            _log.Error(LogCategory, line + " (the run carries on)");
        }
        else
        {
            _log.Info(LogCategory, line);
        }

        if (session is not null && _logic is { } logic)
        {
            var utc = session.Clock.Time.GetUtcNow();
            _events.Add(fault ? "fault" : "notice", "desk-closed: " + why);
            _events.Tick(session, logic, utc);
            _spool.PostStatus(BuildStatus(session, logic, utc));
        }
    }

    private void Attach(AutopilotHostLogic logic)
    {
        if (ReferenceEquals(_logic, logic))
        {
            return;
        }

        _logic = logic;
        logic.Changed += () => _dirty = true;
        logic.Stopped += why => _events.Add("stop", why);
        logic.ChatNotice += line => _events.Add("notice", line);
    }

    private void EnsureOverlay(IGameSessionView session)
    {
        var guid = session.State.Self.Guid;
        if (_overlay is not null || guid == 0)
        {
            return;
        }

        _overlay = SteerOverlay.Empty(guid);
        if (_overlayText is { } text)
        {
            if (SteerOverlay.Parse(text, guid, out var problem) is { } read)
            {
                _overlay = read;
            }
            else
            {
                _log.Warn(LogCategory, $"autopilot steer: the overlay is set aside ({problem}); the standing rules start empty");
                _events.Add("notice", $"overlay set aside: {problem}");
                _spool.Post(new SteerWrite(SteerWriteKind.SetAside, SteerOverlay.FileName));
            }

            _overlayText = null;
        }
    }

    private void NoteSeated(AutopilotHostLogic logic)
    {
        if (!ReferenceEquals(logic.Scenario, _seatedScenario))
        {
            _seatedScenario = logic.Scenario;
            _seatedOverlay = _overlay?.Version ?? 0;
            _dirty = true;
        }
    }

    private void TickRunning(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        for (var i = _running.Count - 1; i >= 0; i--)
        {
            var order = _running[i];
            if (Outcome(order, logic, utc) is not { } end)
            {
                continue;
            }

            _running.RemoveAt(i);
            if (order.Kind == SteerKinds.Start && end.State == SteerState.Failed)
            {
                _withdrawPress?.Invoke(); // the order's life is over, so a press still waiting for the parts must not start a run later
            }

            var report = Make(order, end.State, end.Name, end.Detail, session, logic, utc);
            _spool.Post(new SteerWrite(SteerWriteKind.Replace, $"outbox/{order.Id}.json", report.ToJson()));
            _events.Add("order", $"{order.Id} {order.Kind}: {report.State}:{report.Name}");
            _dirty = true;
        }
    }

    /// <summary>How a Control order that has acted stands now; null while it has not ended.</summary>
    private static (string State, string Name, string Detail)? Outcome(SteerOrder order, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var mode = logic.Mode;
        var modeName = mode.ToString().ToLowerInvariant();
        switch (order.Kind)
        {
            case SteerKinds.Start or SteerKinds.Resume:
                if (mode is AutopilotMode.Running or AutopilotMode.Stopping)
                {
                    return (SteerState.Done, "running", logic.Status.LastDecision); // a stop still standing comes back with the hand-back: the dial is running, and stopping
                }

                return utc >= order.ExpiresUtc ? (SteerState.Failed, "not-started", logic.Status.LastDecision) : null;
            case SteerKinds.StopHome:
                if (mode == AutopilotMode.Done)
                {
                    var want = "request:" + Scenarios.Journey.JourneyVerdict.Token("steer-" + order.Id);
                    var stop = logic.Scenario?.StopName ?? "none";
                    return stop == want ? (SteerState.Done, want, "the run stopped at its next safe point") : (SteerState.Failed, stop, "the run ended another way first");
                }

                return mode is AutopilotMode.Running or AutopilotMode.Stopping ? null : (SteerState.Failed, modeName, "the dial left the stop before it ended");
            case SteerKinds.StopHere:
                return mode == AutopilotMode.Interrupted ? (SteerState.Done, "interrupted", "stopped where it stands") : (SteerState.Failed, modeName, "the dial did not stop");
            case SteerKinds.Leave:
                if (logic.UnattendedEnd is { } end)
                {
                    return (SteerState.Done, "leaving", end.Decision);
                }

                return mode is AutopilotMode.Running or AutopilotMode.Stopping ? null : (SteerState.Failed, modeName, "the dial left the way out before it ended");
            default:
                return null;
        }
    }

    private SteerReport Control(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        if (order.Kind == SteerKinds.Cancel)
        {
            return Cancel(order, session, logic, utc);
        }

        if (ControlRefusal(order, session, logic) is { } refusal)
        {
            return Make(order, SteerState.Refused, refusal.Name, refusal.Detail, session, logic, utc);
        }

        if (order.DryRun)
        {
            return Make(order, SteerState.Checked, "dry-run", "every check passed; nothing was done", session, logic, utc);
        }

        var via = "steer-" + order.Id;
        switch (order.Kind)
        {
            case SteerKinds.Start:
                _press(StartedBy);
                break;
            case SteerKinds.Resume:
                logic.HandBack(session);
                break;
            case SteerKinds.StopHome:
                logic.Stop(via);
                break;
            case SteerKinds.StopHere:
                logic.StopHereByOrder(session, via);
                break;
            default:
                logic.Leave(via);
                break;
        }

        if (Outcome(order, logic, utc) is { } end)
        {
            return Make(order, end.State, end.Name, end.Detail, session, logic, utc);
        }

        _running.Add(order);
        return Make(order, SteerState.Running, "asked", logic.Status.LastDecision, session, logic, utc);
    }

    /// <summary>Rule 3 of the contract: a person wins. Then each kind's own legal modes.</summary>
    private static (string Name, string Detail)? ControlRefusal(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic)
    {
        var mode = logic.Mode;
        if (logic.UnattendedEnd is not null)
        {
            return ("leaving", "the game is already on its way out");
        }

        if (session.Actions.KillSwitch || mode == AutopilotMode.Halted)
        {
            return ("kill-switch", "the kill switch is on");
        }

        if (logic.PersonHold || (mode == AutopilotMode.Interrupted && !logic.InterruptedByOrder))
        {
            return ("person-stopped", "a person stopped the autopilot; only their own /auto gives it back");
        }

        var legal = order.Kind switch
        {
            SteerKinds.Start => mode is AutopilotMode.Off or AutopilotMode.Done,
            SteerKinds.Resume => mode == AutopilotMode.Interrupted,
            SteerKinds.StopHome => mode == AutopilotMode.Running,
            SteerKinds.StopHere => mode is AutopilotMode.Running or AutopilotMode.Stopping,
            _ => mode is AutopilotMode.Running or AutopilotMode.Off or AutopilotMode.Done,
        };
        return legal ? null : ("wrong-mode", $"{order.Kind} is not legal while {mode.ToString().ToLowerInvariant()}");
    }

    private SteerReport Cancel(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        if (order.ArgText("target") is not { } target)
        {
            return Make(order, SteerState.Refused, "bad-args", "cancel needs a target", session, logic, utc);
        }

        if (_orders is not null && _commands.FindIndex(c => c.Order.Id == target) is >= 0 and var at)
        {
            var entry = _commands[at].Entry;
            return _orders.Cancel(entry, "cancelled by the desk's " + order.Id) is { } refusal
                ? Make(order, SteerState.Refused, refusal, $"{target} was already taken", session, logic, utc)
                : Make(order, SteerState.Done, "cancelled", $"{target} is taken off", session, logic, utc);
        }

        return _answered.Contains(target) || _running.Exists(r => r.Id == target)
            ? Make(order, SteerState.Refused, "too-late", $"{target} has already been answered", session, logic, utc)
            : Make(order, SteerState.Refused, "unknown-order", $"no order {target} is on the desk", session, logic, utc);
    }

    private void Answer(in SteerInbound inbound, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        Answered++;
        if (inbound.Id is { } answered)
        {
            _answered.Add(answered);
        }

        if (inbound.Duplicate)
        {
            _events.Add("order", $"{inbound.Id}: duplicate: the earlier report stands");
        }
        else if (inbound.Id is not { } id)
        {
            _log.Warn(LogCategory, $"autopilot steer: refused:{inbound.Refusal}: {inbound.FileName} ({inbound.Detail})");
            _events.Add("order", $"{inbound.FileName}: refused:{inbound.Refusal}: {inbound.Detail}");
        }
        else
        {
            var report = inbound.Order is { } order ? Judge(order, session, logic, utc) : Make(id, inbound.Kind ?? string.Empty, SteerState.Refused, inbound.Refusal!, inbound.Detail, session, logic, utc);
            _spool.Post(new SteerWrite(SteerWriteKind.Replace, $"outbox/{id}.json", report.ToJson()));
            _events.Add("order", $"{id} {report.Kind}: {report.State}:{report.Name}");
        }

        _spool.Post(new SteerWrite(SteerWriteKind.Take, inbound.FileName));
    }

    private SteerReport Judge(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        if (SteerKinds.IsCommand(order.Kind) && !_ordersOn)
        {
            var token = _observeOnly ? "observe-only" : "orders-off";
            _log.Warn(LogCategory, $"autopilot steer: refused:{token}: {order.Id} {order.Kind} by {order.By} (the command orders need --autopilot-steer-orders=on)");
            return Make(order, SteerState.Refused, token, "this run takes no command orders: start it with --autopilot-steer-orders=on", session, logic, utc);
        }

        if (_observeOnly && SteerKinds.All[order.Kind] != SteerClass.Probe && !SteerKinds.IsCommand(order.Kind))
        {
            _log.Warn(LogCategory, $"autopilot steer: refused:observe-only: {order.Id} {order.Kind} by {order.By}");
            return Make(order, SteerState.Refused, "observe-only", "this run only watches: nothing may change what the character does", session, logic, utc);
        }

        if (utc >= order.ExpiresUtc)
        {
            return Make(order, SteerState.Expired, "expired", $"its {order.ExpiresSeconds} s ended before the desk reached it", session, logic, utc);
        }

        if (GuardRefusal(order.Guard, session, logic) is { } guard)
        {
            return Make(order, SteerState.Refused, guard.Name, guard.Detail, session, logic, utc);
        }

        if (!SteerKinds.IsBuilt(order.Kind))
        {
            return Make(order, SteerState.Refused, "not-built", $"this build has no {order.Kind} yet", session, logic, utc);
        }

        if (SteerKinds.All[order.Kind] == SteerClass.Control)
        {
            return Control(order, session, logic, utc);
        }

        if (SteerKinds.IsCommand(order.Kind))
        {
            return Command(order, session, logic, utc);
        }

        if (order.Kind == SteerKinds.Capture)
        {
            return Capture(order, session, logic, utc);
        }

        string? section = null;
        if (order.Kind == SteerKinds.Sheet)
        {
            section = order.ArgText("section");
            if (section is null || !SteerPicture.Sections.Contains(section, StringComparer.Ordinal))
            {
                return Make(order, SteerState.Refused, "unknown-section", $"'{section}' is not one of {string.Join(", ", SteerPicture.Sections)}", session, logic, utc);
            }

            if (!session.State.Self.Qualities.IsKnown)
            {
                return Make(order, SteerState.Refused, "not-in-world", "the character's description has not arrived", session, logic, utc);
            }
        }

        if (order.DryRun)
        {
            return Make(order, SteerState.Checked, "dry-run", "every check passed; nothing was done", session, logic, utc);
        }

        if (section is null)
        {
            var age = (long)Math.Max(0, (utc - order.IssuedUtc).TotalMilliseconds);
            return Make(order, SteerState.Done, "pong", "the desk is open", session, logic, utc, string.Create(CultureInfo.InvariantCulture, $"{{\"desk_open\":true,\"age_ms\":{age}}}"));
        }

        var path = $"sheets/{section}.json";
        var sheet = section == SteerPicture.RecentSection ? _history.Json(utc) : SteerPicture.Sheet(section, session, _timers());
        _spool.Post(new SteerWrite(SteerWriteKind.Replace, path, sheet));
        return Make(order, SteerState.Done, "sheet:" + section, path, session, logic, utc, string.Create(CultureInfo.InvariantCulture, $"{{\"chars\":{sheet.Length}}}"));
    }

    /// <summary>A command order (the command registry, 2026-11-17): its line goes to the same runner the chat's does; the report follows the command until it ends.</summary>
    private SteerReport Command(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var line = order.Kind switch
        {
            SteerKinds.Trip => "trip",
            SteerKinds.RunQuest => order.ArgText("key") is { } key ? "quest " + key : null,
            _ => order.ArgText("line"),
        };
        if (string.IsNullOrWhiteSpace(line))
        {
            return Make(order, SteerState.Refused, "bad-args", order.Kind == SteerKinds.RunQuest ? "run_quest needs a key" : "command needs a line, as the chat types it (trip, quest <key>, rebuff, ...)", session, logic, utc);
        }

        if (_orders is null)
        {
            return Make(order, SteerState.Refused, "no-runner", "this host has no command runner", session, logic, utc);
        }

        var entry = _orders.Submit(line, "desk", order.Id, person: false, dryRun: order.DryRun);
        if (!entry.Final)
        {
            _commands.Add((order, entry, entry.Version));
        }

        return Report(order, entry, session, logic, utc);
    }

    /// <summary>Each frame: a command order's report is written again whenever its command moved, and an order still waiting past its expiry is taken off (<c>expired</c>).</summary>
    private void TickCommands(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            var (order, entry, version) = _commands[i];
            if (!entry.Final && entry.State is CommandEntryState.Queued or CommandEntryState.Waiting && utc >= order.ExpiresUtc && _orders!.Cancel(entry, "expired") is null)
            {
                _commands.RemoveAt(i);
                Post(order, Make(order, SteerState.Expired, "expired", $"its {order.ExpiresSeconds} s ended before its safe point", session, logic, utc));
                continue;
            }

            if (entry.Version == version)
            {
                continue;
            }

            if (entry.Final)
            {
                _commands.RemoveAt(i);
            }
            else
            {
                _commands[i] = (order, entry, entry.Version);
            }

            Post(order, Report(order, entry, session, logic, utc));
        }
    }

    private void Post(SteerOrder order, SteerReport report)
    {
        _spool.Post(new SteerWrite(SteerWriteKind.Replace, $"outbox/{order.Id}.json", report.ToJson()));
        _events.Add("order", $"{order.Id} {order.Kind}: {report.State}:{report.Name}");
        _dirty = true;
    }

    private static SteerReport Report(SteerOrder order, CommandEntry entry, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var state = entry.State switch
        {
            CommandEntryState.Done or CommandEntryState.Answered => SteerState.Done,
            CommandEntryState.Failed => SteerState.Failed,
            CommandEntryState.Refused => SteerState.Refused,
            CommandEntryState.Cancelled => SteerState.Cancelled,
            CommandEntryState.Checked => SteerState.Checked,
            _ => SteerState.Running,
        };
        var evidence = SteerJson.Text(w =>
        {
            w.WriteStartObject();
            w.WriteString("line", entry.Text);
            w.WriteNumber("number", entry.Number);
            w.WriteString("command_state", entry.State.ToString().ToLowerInvariant());
            w.WriteString("taken_at", entry.Order?.TakenAt);
            if (entry.Lines.Count > 0)
            {
                w.WriteStartArray("lines");
                foreach (var l in entry.Lines)
                {
                    w.WriteStringValue(l);
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        });
        return Make(order, state, entry.Name, entry.Detail, session, logic, utc, evidence);
    }

    private static string ShotPath(string name) => $"shots/{name}.png";

    private SteerReport Capture(SteerOrder order, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        if (order.ArgText("name") is not { } name || !SteerOrder.IsId(name))
        {
            return Make(order, SteerState.Refused, "bad-args", "capture needs a name that can be a file name", session, logic, utc);
        }

        if (_camera is null)
        {
            return Make(order, SteerState.Refused, "no-camera", "this host has no frame to read", session, logic, utc);
        }

        if (_shot is not null)
        {
            return Make(order, SteerState.Refused, "busy", "the last screenshot is still being saved", session, logic, utc);
        }

        var since = session.Clock.Now - _lastCapture;
        if (since < MinCaptureSeconds)
        {
            return Make(order, SteerState.Refused, "too-soon", string.Create(CultureInfo.InvariantCulture, $"the last screenshot was {since:0.#} s ago; at most one every {MinCaptureSeconds:0} s"), session, logic, utc);
        }

        if (order.DryRun)
        {
            return Make(order, SteerState.Checked, "dry-run", "every check passed; nothing was done", session, logic, utc);
        }

        if (_camera.Take(name) is not { } shot)
        {
            return Make(order, SteerState.Failed, "no-frame", "no frame could be read back", session, logic, utc);
        }

        _lastCapture = session.Clock.Now;
        _shot = (order, shot);
        return Make(order, SteerState.Running, "saving", ShotPath(name), session, logic, utc);
    }

    /// <summary>A capture's report waits for its file and for the frame that paid for it.</summary>
    private void TickCapture(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        if (_shot is not { } taking || taking.Shot.FrameMilliseconds is not { } frame || (!taking.Shot.Saved && taking.Shot.Failure is null))
        {
            return;
        }

        _shot = null;
        var (order, shot) = taking;
        var name = order.ArgText("name")!;
        var evidence = string.Create(CultureInfo.InvariantCulture, $"{{\"readback_ms\":{shot.ReadbackMilliseconds:0.###},\"frame_ms\":{frame:0.###},\"prior_frame_ms\":{shot.PriorFrameMilliseconds:0.###},\"width\":{shot.Width},\"height\":{shot.Height}}}");
        var report = shot.Failure is { } why
            ? Make(order, SteerState.Failed, "not-saved", why, session, logic, utc, evidence)
            : Make(order, SteerState.Done, "shot:" + name, ShotPath(name), session, logic, utc, evidence);
        _spool.Post(new SteerWrite(SteerWriteKind.Replace, $"outbox/{order.Id}.json", report.ToJson()));
        _events.Add("order", $"{order.Id} {order.Kind}: {report.State}:{report.Name}");
    }

    private static (string Name, string Detail)? GuardRefusal(SteerGuard guard, IGameSessionView session, AutopilotHostLogic logic)
    {
        var self = session.State.Self.Guid;
        if (guard.CharacterGuid is { } guid && guid != self)
        {
            return ("guard-character", $"written for {SteerJson.Guid(guid)}; this is {SteerJson.Guid(self)}");
        }

        var level = AutopilotHostLogic.LevelOf(session);
        if (level < (guard.LevelMin ?? int.MinValue) || level > (guard.LevelMax ?? int.MaxValue))
        {
            return ("guard-level", string.Create(CultureInfo.InvariantCulture, $"level {level} is outside {guard.LevelMin}-{guard.LevelMax}"));
        }

        var ground = logic.Scenario?.Ground.Key;
        if (guard.Ground is { } wanted && !string.Equals(wanted, ground, StringComparison.Ordinal))
        {
            return ("guard-ground", $"written for {wanted}; the ground is {ground ?? "not known"}");
        }

        return null;
    }

    private static SteerReport Make(SteerOrder order, string state, string name, string detail, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc, string? evidence = null) =>
        Make(order.Id, order.Kind, state, name, detail, session, logic, utc, evidence);

    private static SteerReport Make(string id, string kind, string state, string name, string detail, IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc, string? evidence = null) =>
        new(id, kind, state, name, detail, utc, AutopilotHostLogic.LevelOf(session), logic.Scenario?.Ground.Key, logic.Scenario?.Cycle ?? 0, evidence);

    private void WriteStatusIfDue(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var now = session.Clock.Now;
        var since = now - _lastStatus;
        if (since < StatusMinSeconds || (since < StatusSeconds && !_dirty))
        {
            return;
        }

        _lastStatus = now;
        _dirty = false;
        _spool.PostStatus(BuildStatus(session, logic, utc));
    }

    private string BuildStatus(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc) => SteerPicture.Status(new SteerStatusInput(session, logic, utc, ++_sequence, _run, _build, Open, _rebuffMarginSeconds, _overlay?.Version ?? 0, _seatedOverlay, _running.Count > 0 ? _running[0].Id : null, _observeOnly));
}

/// <summary>
/// The event stream (the steer contract, section 8): one JSON object a line, stamped with the time, level, ground and cycle. Read from seams the dial already has: the stage, the tallies and the lines are compared once a frame, and a stop or a chat notice is
/// held until the next frame stamps it. A new scenario re-reads its tallies without an event (a hand-back carries them; a rung move starts them again).
/// </summary>
/// <param name="write">Writes one line.</param>
public sealed class SteerEventStream(Action<string> write)
{
    private readonly Action<string> _write = write ?? throw new ArgumentNullException(nameof(write));
    private readonly List<(string Kind, string Text)> _held = [];
    private AutopilotScenario? _scenario;
    private (int Deaths, int Retreats, int Trips, int Reconnects, long Spent, int Learned) _tally;
    private AutopilotMode? _mode;
    private string _stage = string.Empty;
    private int _level = -1;
    private int _linesSeen;
    private long _sequence;

    /// <summary>Holds an event until the next tick stamps it.</summary>
    /// <param name="kind">The event's kind.</param>
    /// <param name="text">What happened.</param>
    public void Add(string kind, string text) => _held.Add((kind, text));

    /// <summary>One frame: what changed becomes events.</summary>
    /// <param name="session">The session.</param>
    /// <param name="logic">The dial.</param>
    /// <param name="utc">The time.</param>
    public void Tick(IGameSessionView session, AutopilotHostLogic logic, DateTimeOffset utc)
    {
        var scenario = logic.Scenario;
        var level = AutopilotHostLogic.LevelOf(session);
        var ground = scenario?.Ground.Key;
        var cycle = scenario?.Cycle ?? 0;
        foreach (var (kind, text) in _held)
        {
            Emit(kind, text, utc, level, ground, cycle);
        }

        _held.Clear();
        var stage = scenario?.Stage ?? string.Empty;
        if (logic.Mode != _mode || !string.Equals(stage, _stage, StringComparison.Ordinal))
        {
            _mode = logic.Mode;
            _stage = stage;
            Emit("state", $"{logic.Mode.ToString().ToLowerInvariant()} {stage}".TrimEnd(), utc, level, ground, cycle);
        }

        if (level != _level)
        {
            if (_level > 0 && level > 0)
            {
                Emit("level", string.Create(CultureInfo.InvariantCulture, $"{_level} to {level}"), utc, level, ground, cycle);
            }

            _level = level;
        }

        TickLines(logic, utc, level, ground, cycle);
        TickTallies(scenario, utc, level, ground, cycle);
    }

    private void TickLines(AutopilotHostLogic logic, DateTimeOffset utc, int level, string? ground, int cycle)
    {
        var lines = logic.Lines;
        for (var i = Math.Min(_linesSeen, lines.Count); i < lines.Count; i++)
        {
            Emit(lines[i].StartsWith(LadderPrefix, StringComparison.Ordinal) ? "ladder" : "cycle", lines[i], utc, level, ground, cycle);
        }

        _linesSeen = lines.Count;
    }

    private const string LadderPrefix = "[ladder]";

    private void TickTallies(AutopilotScenario? scenario, DateTimeOffset utc, int level, string? ground, int cycle)
    {
        if (scenario is null)
        {
            _scenario = null;
            return;
        }

        var (t, c) = (scenario.Totals, scenario.CycleTally);
        var now = (t.Deaths + c.Deaths, t.Retreats + c.Retreats, t.Trips + c.Trips, t.Reconnects + c.Reconnects, t.SpentXp + c.SpentXp, t.Learned + c.Learned);
        if (!ReferenceEquals(scenario, _scenario))
        {
            _scenario = scenario;
            _tally = now;
            return;
        }

        void Rise(string kind, long was, long is_)
        {
            if (is_ > was)
            {
                Emit(kind, string.Create(CultureInfo.InvariantCulture, $"{was} to {is_}"), utc, level, ground, cycle);
            }
        }

        Rise("death", _tally.Deaths, now.Item1);
        Rise("retreat", _tally.Retreats, now.Item2);
        Rise("trip", _tally.Trips, now.Item3);
        Rise("reconnect", _tally.Reconnects, now.Item4);
        Rise("spend", _tally.Spent, now.Item5);
        Rise("learn", _tally.Learned, now.Item6);
        _tally = now;
    }

    private void Emit(string kind, string text, DateTimeOffset utc, int level, string? ground, int cycle) => _write(SteerJson.Text(w =>
    {
        w.WriteStartObject();
        w.WriteNumber("seq", ++_sequence);
        w.WriteString("at_utc", SteerJson.Utc(utc));
        w.WriteString("kind", kind);
        w.WriteString("text", text);
        w.WriteNumber("level", level);
        w.WriteString("ground", ground);
        w.WriteNumber("cycle", cycle);
        w.WriteEndObject();
    }));
}
