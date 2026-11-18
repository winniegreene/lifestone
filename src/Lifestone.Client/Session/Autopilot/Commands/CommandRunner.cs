using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot.Commands;

/// <summary>Where a command stands.</summary>
public enum CommandEntryState
{
    /// <summary>In the queue behind another.</summary>
    Queued,

    /// <summary>Asked of the dial; it waits for its safe point.</summary>
    Waiting,

    /// <summary>Taken; its work runs.</summary>
    Running,

    /// <summary>Its work ended well.</summary>
    Done,

    /// <summary>It acted and did not get what it asked for.</summary>
    Failed,

    /// <summary>It was never acted on; the name says why.</summary>
    Refused,

    /// <summary>Taken off before it acted.</summary>
    Cancelled,

    /// <summary>A command answered at once (help, status, queue, cancel, clear).</summary>
    Answered,

    /// <summary>A dry run: every check passed and nothing was done.</summary>
    Checked,
}

/// <summary>One command, from its line to its report.</summary>
public sealed class CommandEntry
{
    internal CommandEntry(int number, string id, string text, string source, bool person)
    {
        (Number, Id, Text, Source, Person) = (number, id, text, source, person);
    }

    /// <summary>Its number in this session (the queue's <c>#n</c>).</summary>
    public int Number { get; }

    /// <summary>Its id: the desk order's, or <c>chat-&lt;n&gt;</c>.</summary>
    public string Id { get; }

    /// <summary>The line as given.</summary>
    public string Text { get; }

    /// <summary>Where it came from: <c>chat</c> or <c>desk</c>.</summary>
    public string Source { get; }

    /// <summary>True when a person typed it (the chat).</summary>
    public bool Person { get; }

    /// <summary>The parsed line, or null when it could not be read.</summary>
    public CommandRequest? Request { get; internal set; }

    /// <summary>The command, or null when none answers to the name.</summary>
    public CommandSpec? Spec { get; internal set; }

    /// <summary>Where it stands.</summary>
    public CommandEntryState State { get; internal set; } = CommandEntryState.Queued;

    /// <summary>The outcome's token (<c>queued</c>, <c>waiting</c>, <c>trip-done</c>, <c>not-running</c>, ...).</summary>
    public string Name { get; internal set; } = "queued";

    /// <summary>The outcome in words.</summary>
    public string Detail { get; internal set; } = string.Empty;

    /// <summary>The lines an answered command gives.</summary>
    public IReadOnlyList<string> Lines { get; internal set; } = [];

    /// <summary>Bumped at every change (the desk re-reports on it).</summary>
    public int Version { get; internal set; }

    /// <summary>True once it will not change again.</summary>
    public bool Final => State is not (CommandEntryState.Queued or CommandEntryState.Waiting or CommandEntryState.Running);

    /// <summary>The scenario order it asked, or null.</summary>
    public ScenarioOrder? Order { get; internal set; }

    /// <summary>The short label the lines use: <c>#n text</c>.</summary>
    public string Label => "#" + Number.ToString(CultureInfo.InvariantCulture) + " " + Text;

    internal AutopilotScenario? AskedOf { get; set; }

    internal double Since { get; set; }

    internal int SeatsAt { get; set; } = -1;

    internal bool TaskBegun { get; set; }
}

/// <summary>
/// The command runner: one parser and one queue for both ways in (the chat and the steer desk). A command is checked before (its preconditions, each refused by name), acted on by a call the dial already has, watched each frame after the
/// dial's own tick, and reported (the chat line, and the desk's outbox through <see cref="Changed"/>). Orders run one after another, each at its safe point; the controls jump the queue; a person's stop empties it. Nothing here sends to
/// the server: every act is the dial's, so every cast is from her spell bar and every move one a player could make.
/// </summary>
public sealed class CommandRunner
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "autopilot order";

    /// <summary>The longest an order waits for its safe point before it fails by name (<c>no-safe-point</c>), in seconds.</summary>
    public const double MaxWaitSeconds = 1800;

    /// <summary>The longest a start waits for the dial to run (the play host reads its parts on the first press), in seconds.</summary>
    public const double MaxStartSeconds = 120;

    /// <summary>The prefix of every chat line.</summary>
    public const string Prefix = "Lifestone: ";

    private readonly CommandRegistry _registry;
    private readonly ICommandDial _dial;
    private readonly ILogSink _log;
    private readonly List<CommandEntry> _queue = [];
    private CommandEntry? _pause;
    private int _number;
    private bool _ladderOwed;

    /// <summary>A runner over <paramref name="dial"/>.</summary>
    /// <param name="registry">The commands.</param>
    /// <param name="dial">The dial.</param>
    /// <param name="log">The log.</param>
    public CommandRunner(CommandRegistry registry, ICommandDial dial, ILogSink log)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dial = dial ?? throw new ArgumentNullException(nameof(dial));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>A line for her chat window (every change of every command, wherever it came from).</summary>
    public event Action<string>? Line;

    /// <summary>A command changed (the desk writes its report).</summary>
    public event Action<CommandEntry>? Changed;

    /// <summary>The command whose work is under way, or null.</summary>
    public CommandEntry? Active { get; private set; }

    /// <summary>The commands queued behind it.</summary>
    public IReadOnlyList<CommandEntry> Queued => _queue;

    /// <summary>The registry.</summary>
    public CommandRegistry Registry => _registry;

    /// <summary>True when <paramref name="name"/> is a command of the registry.</summary>
    /// <param name="name">The name, without the slash.</param>
    public bool Knows(string name) => _registry.Find(name) is not null;

    /// <summary>Takes one command line.</summary>
    /// <param name="text">The line (a leading slash is dropped).</param>
    /// <param name="source"><c>chat</c> or <c>desk</c>.</param>
    /// <param name="id">The desk order's id; null makes one.</param>
    /// <param name="person">True when a person typed it.</param>
    /// <param name="dryRun">Every check, no act: the entry ends <see cref="CommandEntryState.Checked"/>.</param>
    /// <returns>The entry: final when refused, answered or checked; else queued, waiting or running.</returns>
    public CommandEntry Submit(string text, string source, string? id = null, bool person = false, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var number = ++_number;
        var entry = new CommandEntry(number, id ?? "chat-" + number.ToString(CultureInfo.InvariantCulture), text.Trim().TrimStart('/'), source, person) { Since = _dial.Now };
        _log.Info(LogCategory, $"autopilot order: {entry.Label} from the {source}{(dryRun ? " (dry run)" : string.Empty)}: received");
        if (!CommandLine.TryParse(text, out var request, out var error))
        {
            return Refuse(entry, "bad-line", $"the line could not be read ({error})");
        }

        entry.Request = request;
        if (_registry.Find(request!.Name) is not { } spec)
        {
            return Refuse(entry, "unknown-command", $"'{request.Name}' is no command; /help lists them");
        }

        entry.Spec = spec;
        if (request.Args.Count < spec.MinArgs || request.Args.Count > spec.MaxArgs)
        {
            return Refuse(entry, "bad-args", "usage: " + spec.Usage);
        }

        if (!spec.Built)
        {
            return Refuse(entry, "not-built", $"TODO({spec.Todo}): {spec.Help} This command is listed and not built yet; nothing was done");
        }

        if (ArgRefusal(spec, request) is { } bad)
        {
            return Refuse(entry, bad.Name, bad.Detail);
        }

        if (spec.Answered)
        {
            entry.Lines = Answer(spec, request);
            return End(entry, CommandEntryState.Answered, spec.Name, string.Join(" | ", entry.Lines), lines: entry.Lines);
        }

        if (dryRun)
        {
            return Before(entry) is { } refused ? Refuse(entry, refused.Name, refused.Detail) : End(entry, CommandEntryState.Checked, "dry-run", "every check passed; nothing was done");
        }

        switch (spec.Action)
        {
            case CommandAction.Stop:
                return StopNow(entry);
            case CommandAction.Resume:
                return ResumeNow(entry);
            case CommandAction.Pause:
                return PauseAsk(entry);
        }

        _queue.Add(entry);
        if (Active is null)
        {
            StartNext();
        }

        if (entry.State == CommandEntryState.Queued)
        {
            Say(entry, $"queued behind {Active!.Label} ({_queue.Count} in the queue)");
        }

        return entry;
    }

    /// <summary>A person's /stop: the queue is emptied, a waiting order is taken back, and the dial stops where she stands (their /stop).</summary>
    /// <param name="via">Who asked, for the log.</param>
    /// <returns>The dial's line.</returns>
    public string StopByPerson(string via)
    {
        DropAll("a person stopped the autopilot (" + via + ")");
        _ladderOwed = false;
        return _dial.StopHere(via);
    }

    /// <summary>Takes <paramref name="entry"/> off, when it is queued or still waiting for its safe point.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="why">Why.</param>
    /// <returns>Null when taken off; else the refusal's token (<c>too-late</c>).</returns>
    public string? Cancel(CommandEntry entry, string why)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Final)
        {
            return "too-late";
        }

        if (_queue.Remove(entry))
        {
            End(entry, CommandEntryState.Cancelled, "cancelled", why);
            return null;
        }

        if (ReferenceEquals(entry, _pause))
        {
            _pause = null;
            End(entry, CommandEntryState.Cancelled, "cancelled", why);
            return null;
        }

        if (!ReferenceEquals(entry, Active) || entry.State != CommandEntryState.Waiting)
        {
            return "too-late";
        }

        if (entry.Order is not null && entry.AskedOf?.CancelOrder(why) != true)
        {
            return "too-late";
        }

        if (entry.SeatsAt >= 0)
        {
            return "too-late"; // a re-seat asked cannot be taken back
        }

        Active = null;
        End(entry, CommandEntryState.Cancelled, "cancelled", why);
        StartNext();
        return null;
    }

    /// <summary>
    /// The dial's <see cref="AutopilotHostLogic.AfterRun"/>: the end of a task run an order started (the den run) is the order's: claimed, so the game does not quit and the keys stay; the order is reported, and the ladder starts again
    /// unless it said <c>then idle</c>. Every other end is not claimed.
    /// </summary>
    /// <param name="scenario">The run that ended.</param>
    public bool ClaimEnd(AutopilotScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (Active is not { Spec.Action: CommandAction.DenRun, TaskBegun: true } entry)
        {
            return false;
        }

        var stop = scenario.StopName ?? "none";
        Active = null;
        End(entry, scenario.Ok ? CommandEntryState.Done : CommandEntryState.Failed, scenario.Ok ? stop : "failed:" + stop, $"check after: the den run's task ended (stop={stop})" + (entry.Request!.Then == CommandThen.Idle ? "; then idle: she holds where it ended" : "; the ladder starts again"));
        _ladderOwed = entry.Request.Then == CommandThen.Resume;
        return true;
    }

    /// <summary>One frame, after the dial's: the pause, the active command's watch, the next in the queue.</summary>
    public void Tick()
    {
        TickPause();
        if (_ladderOwed && _dial.Mode is AutopilotMode.Done)
        {
            _ladderOwed = false;
            var refusal = _dial.UseTask(AutopilotTask.Job);
            var line = refusal is null ? _dial.Start(person: false) : refusal;
            _log.Info(LogCategory, $"autopilot order: after the den run: the ladder starts again: {line}");
            Line?.Invoke(Prefix + "after the den run, the ladder starts again: " + line);
        }

        if (Active is { } active)
        {
            Watch(active);
        }

        if (Active is null)
        {
            StartNext();
        }
    }

    // ---------------------------------------------------------------------------------------------- checks

    private static (string Name, string Detail)? ArgRefusal(CommandSpec spec, CommandRequest request)
    {
        var args = request.Args;
        switch (spec.Name)
        {
            case "rebuff" when args.Count == 1 && args[0].ToLowerInvariant() is not ("self" or "bot"):
                return ("bad-args", "usage: " + spec.Usage);
            case "learn" when args.Count == 1 && !args[0].Equals("plan", StringComparison.OrdinalIgnoreCase):
                return ("not-built", $"TODO(m6-cmd-learn-spell): one spell's scroll by name is not built; /learn (or /learn plan) runs a trip whose buys follow the scroll plan");
            case "quest" or "quest-step":
                if (QuestCatalog.All.FirstOrDefault(q => q.Key == args[0]) is not { } quest)
                {
                    return ("unknown-quest", $"'{args[0]}' is no quest of the catalog");
                }

                if (spec.Name == "quest-step" && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var step) || step < 1 || step > quest.Steps.Count))
                {
                    return ("bad-args", string.Create(CultureInfo.InvariantCulture, $"the step must be 1 to {quest.Steps.Count} for {quest.Key}"));
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>The checks before an act, read when the command's turn comes (the state moves while it waits).</summary>
    private (string Name, string Detail)? Before(CommandEntry entry)
    {
        var spec = entry.Spec!;
        var mode = _dial.Mode;
        var modeName = mode.ToString().ToLowerInvariant();
        if (_dial.KillSwitch)
        {
            return ("kill-switch", "the kill switch is on");
        }

        switch (spec.Action)
        {
            case CommandAction.Ladder or CommandAction.DenRun:
                if (_dial.PersonHold && !entry.Person)
                {
                    return ("person-stopped", "a person stopped the autopilot; only their own /auto (or their own order) gives it back");
                }

                if (mode is AutopilotMode.Stopping or AutopilotMode.Halted || (mode == AutopilotMode.Interrupted && !_dial.InterruptedByOrder && !entry.Person))
                {
                    return ("wrong-mode", $"the autopilot is {modeName}");
                }

                return null;
            case CommandAction.Resume:
                if (mode is not (AutopilotMode.Interrupted or AutopilotMode.Halted))
                {
                    return ("wrong-mode", $"the autopilot is {modeName}: nothing to resume");
                }

                return !entry.Person && (_dial.PersonHold || !_dial.InterruptedByOrder) ? ("person-stopped", "a person stopped the autopilot; only their own /auto gives it back") : null;
            default:
                if (mode != AutopilotMode.Running)
                {
                    return ("not-running", $"the autopilot is {modeName}{(_dial.PersonHold ? " (a person stopped it)" : string.Empty)}: /auto or /ladder first");
                }

                return null;
        }
    }

    // ---------------------------------------------------------------------------------------------- acts

    private void StartNext()
    {
        while (Active is null && _queue.Count > 0)
        {
            var entry = _queue[0];
            _queue.RemoveAt(0);
            if (Before(entry) is { } refused)
            {
                Refuse(entry, refused.Name, refused.Detail);
                continue;
            }

            Active = entry;
            entry.Since = _dial.Now;
            Act(entry);
        }
    }

    private void Act(CommandEntry entry)
    {
        var spec = entry.Spec!;
        var request = entry.Request!;
        switch (spec.Action)
        {
            case CommandAction.Trip:
                AskOrder(entry, new ScenarioOrder(entry.Id, ScenarioOrderKind.Trip));
                break;
            case CommandAction.Rebuff:
                AskOrder(entry, new ScenarioOrder(entry.Id, request.Args.Count == 1 && request.Args[0].Equals("bot", StringComparison.OrdinalIgnoreCase) ? ScenarioOrderKind.RebuffBot : ScenarioOrderKind.Rebuff));
                break;
            case CommandAction.Quest:
                AskOrder(entry, QuestOrder(entry));
                break;
            case CommandAction.Home:
                _dial.StopHome("order-" + entry.Id);
                Set(entry, CommandEntryState.Running, "recalling", "the run stops at its next safe point, recalls home and holds");
                break;
            case CommandAction.Ladder:
                AskTask(entry, AutopilotTask.Job);
                break;
            case CommandAction.DenRun:
                AskTask(entry, AutopilotTask.DenRun);
                break;
            default:
                Finish(entry, CommandEntryState.Failed, "no-act", $"the runner has no act for {spec.Action} (a fault in the registry)");
                break;
        }
    }

    private static ScenarioOrder QuestOrder(CommandEntry entry)
    {
        var args = entry.Request!.Args;
        if (entry.Spec!.Name != "quest-step")
        {
            return new ScenarioOrder(entry.Id, ScenarioOrderKind.Quest, args[0]);
        }

        var count = QuestCatalog.All.First(q => q.Key == args[0]).Steps.Count;
        var from = int.Parse(args[1], NumberStyles.None, CultureInfo.InvariantCulture) - 1;
        return new ScenarioOrder(entry.Id, ScenarioOrderKind.Quest, args[0], string.Create(CultureInfo.InvariantCulture, $"{from}-{count - 1}"));
    }

    private void AskOrder(CommandEntry entry, ScenarioOrder order)
    {
        if (_dial.Scenario is not { } scenario)
        {
            Finish(entry, CommandEntryState.Refused, "not-running", "no run is in hand");
            return;
        }

        if (scenario.AskOrder(order) is { } refusal)
        {
            Finish(entry, CommandEntryState.Refused, refusal, $"the run refused the order ({refusal})");
            return;
        }

        (entry.Order, entry.AskedOf) = (order, scenario);
        Set(entry, CommandEntryState.Waiting, "waiting", "check before passed: it waits for the next safe point");
    }

    private void AskTask(CommandEntry entry, AutopilotTask task)
    {
        var mode = _dial.Mode;
        if (mode == AutopilotMode.Running)
        {
            if (_dial.Task == task)
            {
                Finish(entry, CommandEntryState.Done, "already", $"the autopilot already runs {task.Name}");
                return;
            }

            if (_dial.AskSeat(task, "order " + entry.Id + ": " + task.Name) is { } refusal)
            {
                Finish(entry, CommandEntryState.Refused, refusal, $"the re-seat with {task.Name} was refused ({refusal})");
                return;
            }

            entry.SeatsAt = _dial.Seats;
            Set(entry, CommandEntryState.Waiting, "waiting", $"a re-seat with {task.Name} waits for the next safe point (its counts are carried)");
            return;
        }

        if (_dial.UseTask(task) is { } why)
        {
            Finish(entry, CommandEntryState.Refused, "task-refused", why);
            return;
        }

        var line = _dial.Start(entry.Person);
        Set(entry, CommandEntryState.Waiting, "starting", line);
    }

    private CommandEntry StopNow(CommandEntry entry)
    {
        if (entry.Person)
        {
            var line = StopByPerson("/" + entry.Text);
            return End(entry, CommandEntryState.Done, "stopped", line);
        }

        DropAll("an order stopped the autopilot (" + entry.Id + ")");
        _ladderOwed = false;
        if (_dial.Mode != AutopilotMode.Running)
        {
            return End(entry, CommandEntryState.Refused, "not-running", $"the autopilot is {_dial.Mode.ToString().ToLowerInvariant()}: nothing to stop");
        }

        _dial.StopHome("order-" + entry.Id);
        return End(entry, CommandEntryState.Done, "stopping", "the run stops at its next safe point and recalls home; the queue is empty");
    }

    private CommandEntry ResumeNow(CommandEntry entry)
    {
        if (Before(entry) is { } refused)
        {
            return Refuse(entry, refused.Name, refused.Detail);
        }

        var line = _dial.Start(entry.Person);
        return _dial.Mode == AutopilotMode.Running ? End(entry, CommandEntryState.Done, "running", line) : End(entry, CommandEntryState.Failed, "not-resumed", line);
    }

    private CommandEntry PauseAsk(CommandEntry entry)
    {
        if (Before(entry) is { } refused)
        {
            return Refuse(entry, refused.Name, refused.Detail);
        }

        if (_pause is not null)
        {
            return Refuse(entry, "busy", $"{_pause.Label} already waits for its safe point");
        }

        _pause = entry;
        Set(entry, CommandEntryState.Waiting, "waiting", "the pause waits for the next safe point");
        return entry;
    }

    private void TickPause()
    {
        if (_pause is not { } pause)
        {
            return;
        }

        if (_dial.Mode != AutopilotMode.Running)
        {
            _pause = null;
            Finish(pause, CommandEntryState.Failed, "not-running", $"the autopilot became {_dial.Mode.ToString().ToLowerInvariant()} before a safe point");
            return;
        }

        if (_dial.Scenario?.SafePointNow != true)
        {
            if (_dial.Now - pause.Since > MaxWaitSeconds)
            {
                _pause = null;
                Finish(pause, CommandEntryState.Failed, "no-safe-point", string.Create(CultureInfo.InvariantCulture, $"no safe point came in {MaxWaitSeconds:0} s"));
            }

            return;
        }

        _pause = null;
        var stage = _dial.Scenario.Stage;
        var paused = _dial.PauseByOrder("order-" + pause.Id);
        Finish(pause, paused ? CommandEntryState.Done : CommandEntryState.Failed, paused ? "paused" : "not-paused", paused ? $"check after: she stands where she was ({stage}); /resume gives it back" : "the dial did not stop");
    }

    // ---------------------------------------------------------------------------------------------- watch

    private void Watch(CommandEntry entry)
    {
        if (entry.Order is { } order)
        {
            WatchOrder(entry, order);
            return;
        }

        var spec = entry.Spec!;
        var mode = _dial.Mode;
        switch (spec.Action)
        {
            case CommandAction.Home:
                if (mode == AutopilotMode.Done)
                {
                    Finish(entry, CommandEntryState.Done, "home", "check after: the run ended at its safe point (" + _dial.LastDecision + ")");
                }
                else if (mode is AutopilotMode.Interrupted or AutopilotMode.Halted or AutopilotMode.Off)
                {
                    Finish(entry, CommandEntryState.Failed, mode.ToString().ToLowerInvariant(), "the dial left the stop before it ended (" + _dial.LastDecision + ")");
                }

                break;
            case CommandAction.Ladder or CommandAction.DenRun:
                WatchTask(entry, spec.Action == CommandAction.Ladder ? AutopilotTask.Job : AutopilotTask.DenRun);
                break;
        }
    }

    private void WatchTask(CommandEntry entry, AutopilotTask task)
    {
        var mode = _dial.Mode;
        var begun = entry.SeatsAt >= 0 ? _dial.Seats > entry.SeatsAt && _dial.Task == task : mode == AutopilotMode.Running && _dial.Task == task;
        if (!entry.TaskBegun && begun)
        {
            entry.TaskBegun = true;
            if (task.Kind == AutopilotTaskKind.Job)
            {
                Finish(entry, CommandEntryState.Done, "ladder", "check after: the dial runs the ladder's loop (" + _dial.LastDecision + ")");
                return;
            }

            Set(entry, CommandEntryState.Running, "running", $"check after: the dial runs {task.Name}");
            return;
        }

        if (entry.TaskBegun)
        {
            if (mode is AutopilotMode.Interrupted or AutopilotMode.Halted)
            {
                Finish(entry, CommandEntryState.Failed, "stopped", $"the autopilot was {mode.ToString().ToLowerInvariant()} during {task.Name}");
            }

            return; // the end is claimed (ClaimEnd)
        }

        if (entry.SeatsAt >= 0 && mode != AutopilotMode.Running)
        {
            Finish(entry, CommandEntryState.Failed, "not-seated", $"the autopilot became {mode.ToString().ToLowerInvariant()} before the re-seat");
        }
        else if (entry.SeatsAt < 0 && _dial.Now - entry.Since > MaxStartSeconds)
        {
            Finish(entry, CommandEntryState.Failed, "not-started", "the dial did not run within the start's wait (" + _dial.LastDecision + ")");
        }
        else if (entry.SeatsAt >= 0 && _dial.Now - entry.Since > MaxWaitSeconds)
        {
            Finish(entry, CommandEntryState.Failed, "no-safe-point", "the re-seat found no safe point (" + _dial.LastDecision + ")");
        }
    }

    private void WatchOrder(CommandEntry entry, ScenarioOrder order)
    {
        if (order.Ended)
        {
            var ok = order.State == ScenarioOrderState.Done;
            var detail = order.Detail;
            if (ok && entry.Request!.Then == CommandThen.Idle && _dial.Mode == AutopilotMode.Running)
            {
                _dial.StopHome("order-" + entry.Id + "-then-idle");
                detail += "; then idle: she recalls home and holds (/auto or /ladder to go on)";
            }

            Finish(entry, ok ? CommandEntryState.Done : CommandEntryState.Failed, order.Outcome ?? (ok ? "done" : "failed"), detail);
            return;
        }

        var mode = _dial.Mode;
        if (!ReferenceEquals(_dial.Scenario, entry.AskedOf))
        {
            if (order.State == ScenarioOrderState.Waiting && mode == AutopilotMode.Running && _dial.Scenario is { } fresh)
            {
                var again = new ScenarioOrder(order.Id, order.Kind, order.Arg, order.Steps);
                if (fresh.AskOrder(again) is { } refusal)
                {
                    Finish(entry, CommandEntryState.Failed, refusal, $"the run was replaced ({_dial.LastDecision}) and the fresh run refused the order");
                    return;
                }

                (entry.Order, entry.AskedOf) = (again, fresh);
                _log.Info(LogCategory, $"autopilot order: {entry.Label}: the run was replaced ({_dial.LastDecision}); the order is asked of the fresh run");
                return;
            }

            Finish(entry, CommandEntryState.Failed, "cut", $"the run was replaced or ended ({mode.ToString().ToLowerInvariant()}: {_dial.LastDecision}) {(order.State == ScenarioOrderState.Taken ? "after the order was taken" : "before its safe point")}");
            return;
        }

        if (mode != AutopilotMode.Running)
        {
            entry.AskedOf?.CancelOrder("the dial is " + mode.ToString().ToLowerInvariant());
            Finish(entry, CommandEntryState.Failed, mode == AutopilotMode.Stopping ? "stopping" : "stopped", $"the autopilot is {mode.ToString().ToLowerInvariant()} ({_dial.LastDecision})");
            return;
        }

        if (order.State == ScenarioOrderState.Taken && entry.State == CommandEntryState.Waiting)
        {
            Set(entry, CommandEntryState.Running, "taken", $"taken at the {order.TakenAt}: {order.Detail}");
        }
        else if (order.State == ScenarioOrderState.Waiting && entry.State == CommandEntryState.Running)
        {
            Set(entry, CommandEntryState.Waiting, "waiting", order.Detail); // the way home first, or a cut take: it is taken again
        }
        else if (order.State == ScenarioOrderState.Waiting && !order.WentHome && !order.TripFirst && _dial.Now - entry.Since > MaxWaitSeconds)
        {
            entry.AskedOf?.CancelOrder("no safe point");
            Finish(entry, CommandEntryState.Failed, "no-safe-point", string.Create(CultureInfo.InvariantCulture, $"no safe point came in {MaxWaitSeconds:0} s"));
        }
    }

    // ---------------------------------------------------------------------------------------------- answers

    private IReadOnlyList<string> Answer(CommandSpec spec, CommandRequest request) => spec.Action switch
    {
        CommandAction.Help => request.Args.Count == 1 ? HelpFor(request.Args[0]) : HelpAll(),
        CommandAction.Status => [Prefix + StatusLine()],
        CommandAction.Queue => [Prefix + QueueLine()],
        CommandAction.Cancel => [Prefix + CancelBy(request.Args.Count == 1 ? request.Args[0] : null)],
        _ => [Prefix + ClearQueue()],
    };

    private List<string> HelpAll()
    {
        var lines = new List<string> { Prefix + "orders (type /help <command> for one; a trailing 'then idle' holds her at home after it):" };
        foreach (var group in _registry.All.GroupBy(s => s.Group))
        {
            var built = group.Where(s => s.Built).Select(s => s.Name).ToList();
            var stubs = group.Where(s => !s.Built).Select(s => s.Name).ToList();
            lines.Add(Prefix + group.Key + ": " + (built.Count > 0 ? string.Join(", ", built) : "none built") + (stubs.Count > 0 ? "; not built yet: " + string.Join(", ", stubs) : string.Empty));
        }

        return lines;
    }

    private IReadOnlyList<string> HelpFor(string name)
    {
        if (_registry.Find(name.TrimStart('/')) is not { } spec)
        {
            return [Prefix + $"'{name}' is no command; /help lists them."];
        }

        return
        [
            Prefix + spec.Usage + (spec.Aliases is { Count: > 0 } a ? " (also /" + string.Join(", /", a) + ")" : string.Empty) + (spec.Built ? string.Empty : " - not built yet: TODO(" + spec.Todo + ")"),
            Prefix + spec.Help,
            Prefix + "checks before: " + spec.Before,
            Prefix + "after: " + spec.After,
        ];
    }

    /// <summary>The <c>/status</c> line (without the prefix).</summary>
    public string StatusLine()
    {
        var mode = _dial.Mode;
        var held = mode == AutopilotMode.Interrupted ? (_dial.PersonHold ? " (a person stopped it)" : _dial.InterruptedByOrder ? " (paused by an order)" : string.Empty) : string.Empty;
        var scenario = _dial.Scenario;
        var stage = scenario is null || mode == AutopilotMode.Off ? "-" : scenario.Stage;
        return string.Create(CultureInfo.InvariantCulture, $"autopilot {mode.ToString().ToLowerInvariant()}{held}; task {_dial.Task.Name}; stage {stage}; level {_dial.Level}; ground {scenario?.Ground.Key ?? "-"}; order {(Active is { } a ? a.Label + " (" + a.Name + ")" : "none")}{(_pause is { } p ? "; " + p.Label + " waits" : string.Empty)}; queued {_queue.Count}; last: {_dial.LastDecision}");
    }

    private string QueueLine()
    {
        var active = Active is { } a ? $"{a.Label} ({a.Name}: {a.Detail})" : "none";
        var queued = _queue.Count == 0 ? "nothing queued" : "queued: " + string.Join(", ", _queue.Select(e => e.Label));
        return $"active: {active}; {queued}{(_pause is { } p ? "; " + p.Label + " waits for a safe point" : string.Empty)}";
    }

    private string CancelBy(string? which)
    {
        CommandEntry? target;
        if (which is null)
        {
            target = _queue.Count > 0 ? _queue[^1] : _pause ?? Active;
        }
        else
        {
            var number = which.TrimStart('#');
            target = _queue.Concat(Active is null ? [] : [Active]).Concat(_pause is null ? [] : [_pause]).FirstOrDefault(e => e.Id == which || e.Number.ToString(CultureInfo.InvariantCulture) == number);
        }

        if (target is null)
        {
            return which is null ? "nothing to cancel." : $"no order {which} is queued or waiting.";
        }

        return Cancel(target, "cancelled by /cancel") is { } refusal ? $"{target.Label} cannot be cancelled ({refusal}: it was already taken)." : $"{target.Label} is cancelled.";
    }

    private string ClearQueue()
    {
        var n = _queue.Count;
        foreach (var entry in _queue.ToList())
        {
            _queue.Remove(entry);
            End(entry, CommandEntryState.Cancelled, "cancelled", "cleared by /clear");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{n} queued order(s) taken off{(Active is { } a ? "; " + a.Label + " stays" : string.Empty)}.");
    }

    // ---------------------------------------------------------------------------------------------- bookkeeping

    private void DropAll(string why)
    {
        foreach (var entry in _queue.ToList())
        {
            _queue.Remove(entry);
            End(entry, CommandEntryState.Cancelled, "cancelled", why);
        }

        if (_pause is { } pause)
        {
            _pause = null;
            End(pause, CommandEntryState.Cancelled, "cancelled", why);
        }

        if (Active is { } active)
        {
            active.AskedOf?.CancelOrder(why);
            Active = null;
            End(active, active.State == CommandEntryState.Running ? CommandEntryState.Failed : CommandEntryState.Cancelled, active.State == CommandEntryState.Running ? "stopped" : "cancelled", why);
        }
    }

    private void Finish(CommandEntry entry, CommandEntryState state, string name, string detail)
    {
        if (ReferenceEquals(Active, entry))
        {
            Active = null;
        }

        End(entry, state, name, detail);
    }

    private CommandEntry Refuse(CommandEntry entry, string name, string detail) => End(entry, CommandEntryState.Refused, name, detail);

    private void Set(CommandEntry entry, CommandEntryState state, string name, string detail)
    {
        (entry.State, entry.Name, entry.Detail) = (state, name, detail);
        entry.Version++;
        Say(entry, $"{name}: {detail}");
        Changed?.Invoke(entry);
    }

    private CommandEntry End(CommandEntry entry, CommandEntryState state, string name, string detail, IReadOnlyList<string>? lines = null)
    {
        (entry.State, entry.Name, entry.Detail) = (state, name, detail);
        entry.Version++;
        if (lines is not null)
        {
            _log.Info(LogCategory, $"autopilot order: {entry.Label}: answered: {detail}");
            foreach (var line in lines)
            {
                Line?.Invoke(line);
            }
        }
        else
        {
            var word = state.ToString().ToLowerInvariant();
            Say(entry, name == word ? $"{word}: {detail}" : $"{word}: {name}: {detail}", warn: state is CommandEntryState.Failed or CommandEntryState.Refused);
        }

        Changed?.Invoke(entry);
        return entry;
    }

    private void Say(CommandEntry entry, string text, bool warn = false)
    {
        var line = $"order {entry.Label}: {text}";
        if (warn)
        {
            _log.Warn(LogCategory, "autopilot " + line);
        }
        else
        {
            _log.Info(LogCategory, "autopilot " + line);
        }

        Line?.Invoke(Prefix + line);
    }
}
