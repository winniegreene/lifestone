namespace Lifestone.Client.Session.Autopilot.Commands;

/// <summary>What a command does, as the runner acts on it (<see cref="CommandRunner"/>).</summary>
public enum CommandAction
{
    /// <summary>Not built: refused by name, <c>not-built: TODO(m6-cmd-&lt;name&gt;)</c>.</summary>
    Stub,

    /// <summary>Lists the commands, or one command's help. Answered at once.</summary>
    Help,

    /// <summary>The autopilot's state and the queue in one line. Answered at once.</summary>
    Status,

    /// <summary>The active order and the queue. Answered at once.</summary>
    Queue,

    /// <summary>Takes a queued order (or the active one before its safe point) off. Answered at once.</summary>
    Cancel,

    /// <summary>Takes every queued order off. Answered at once.</summary>
    Clear,

    /// <summary>The stop: a person's stops where she stands; an order's recalls home. Jumps the queue and empties it.</summary>
    Stop,

    /// <summary>Stops where she stands at the next safe point; an order may resume her. Jumps the queue.</summary>
    Pause,

    /// <summary>Hands the controls back after a pause (a person's is their /auto). Jumps the queue.</summary>
    Resume,

    /// <summary>Recalls home and holds: the run ends cleanly at its next safe point.</summary>
    Home,

    /// <summary>The ladder's own loop: from another task by a re-seat, from off or done by a start.</summary>
    Ladder,

    /// <summary>The den run, once (its own task), then the ladder (or idle).</summary>
    DenRun,

    /// <summary>One logistics trip at the next safe point.</summary>
    Trip,

    /// <summary>Her own buff pass (or the bot's stop) at the next safe point.</summary>
    Rebuff,

    /// <summary>One quest's errand by the start-quest path at the next safe point.</summary>
    Quest,
}

/// <summary>One command of the registry.</summary>
/// <param name="Name">What the owner types after the slash.</param>
/// <param name="Group">Its group in the help (logistics, buffs, vitals, travel, hunting, quests, gear, items, villa, control, desk).</param>
/// <param name="Usage">Its usage line.</param>
/// <param name="Help">What it does, in plain words.</param>
/// <param name="Before">Its preconditions, checked before it acts (each refused by name).</param>
/// <param name="After">Its check after, and what its report says.</param>
/// <param name="Action">What the runner does.</param>
/// <param name="MinArgs">The fewest arguments.</param>
/// <param name="MaxArgs">The most arguments.</param>
/// <param name="Todo">For a stub: its TODO key; null when built.</param>
/// <param name="Aliases">Other names it answers to.</param>
public sealed record CommandSpec(string Name, string Group, string Usage, string Help, string Before, string After, CommandAction Action, int MinArgs = 0, int MaxArgs = 0, string? Todo = null, IReadOnlyList<string>? Aliases = null)
{
    /// <summary>True when the command acts; false for a loud stub.</summary>
    public bool Built => Todo is null;

    /// <summary>True for the commands answered at once, which never queue and never move her.</summary>
    public bool Answered => Action is CommandAction.Help or CommandAction.Status or CommandAction.Queue or CommandAction.Cancel or CommandAction.Clear;

    /// <summary>True for the controls, which jump the queue.</summary>
    public bool Control => Action is CommandAction.Stop or CommandAction.Pause or CommandAction.Resume;

    /// <summary>The stub's refusal: <c>not-built: TODO(m6-cmd-&lt;name&gt;)</c>.</summary>
    public string StubRefusal => $"not-built: TODO({Todo})";
}

/// <summary>
/// The command registry (the owner's ruling of 2026-11-17: a robust command system for nearly every roadmap job on its own). Every command declares its name and arguments, help, preconditions, check after
/// and report; the runner (<see cref="CommandRunner"/>) acts on it by one path for the chat and the steer desk. A command whose work has no station yet is a loud stub: it is listed and refused with <c>not-built: TODO(m6-cmd-&lt;name&gt;)</c>.
/// </summary>
public sealed class CommandRegistry
{
    private const string Moves = "the autopilot is running (else refused not-running); no person's stop holds it (person-stopped); the kill switch is off (kill-switch)";
    private const string SafePoint = "it waits for the next safe point: the known state, or a fight's lull with no hostile in reach, after every survival and stop trigger (never mid-cast, never mid-portal, never during a trip or a death)";
    private const string StubBefore = "none: it is not built and is refused by name";
    private const string StubAfter = "the refusal names its TODO";

    private readonly Dictionary<string, CommandSpec> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A registry over <paramref name="specs"/>.</summary>
    /// <param name="specs">The commands.</param>
    public CommandRegistry(IEnumerable<CommandSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        All = [.. specs];
        foreach (var spec in All)
        {
            _byName.Add(spec.Name, spec);
            foreach (var alias in spec.Aliases ?? [])
            {
                _byName.Add(alias, spec);
            }
        }
    }

    /// <summary>Every command, in the help's order.</summary>
    public IReadOnlyList<CommandSpec> All { get; }

    /// <summary>The command <paramref name="name"/> answers to (a name or an alias, case-blind), or null.</summary>
    /// <param name="name">The name.</param>
    public CommandSpec? Find(string name) => _byName.GetValueOrDefault(name ?? string.Empty);

    /// <summary>The registry the game uses.</summary>
    public static CommandRegistry Default { get; } = new(Build());

    private static CommandSpec Stub(string name, string group, string usage, string help, int min = 0, int max = 0, IReadOnlyList<string>? aliases = null) =>
        new(name, group, usage, help, StubBefore, StubAfter, CommandAction.Stub, min, max, "m6-cmd-" + name, aliases);

    private static IEnumerable<CommandSpec> Build()
    {
        const string TripAfter = "done when the trip's station ended well (the run's trip count rose); failed:trip-given-up when it failed twice; the fight or errand it left resumes with its counts kept";

        // Logistics
        yield return new("trip", "logistics", "/trip", "One logistics trip now: recall home, sell by the pack rules, the buys with the scroll plan, the supplies, and back. An errand's hunt it cuts runs again from what she holds.", Moves + "; " + SafePoint + "; also an errand's fight in a lull", TripAfter, CommandAction.Trip);
        yield return new("sell", "logistics", "/sell", "A sell trip: the whole logistics trip (its sale is the first part; a sale-only visit is TODO(m6-cmd-sell-only)).", Moves + "; " + SafePoint, TripAfter, CommandAction.Trip);
        yield return new("restock", "logistics", "/restock", "A restock: the whole logistics trip (its supplies are its last part).", Moves + "; " + SafePoint, TripAfter, CommandAction.Trip);
        yield return new("learn", "logistics", "/learn [plan]", "With no argument or 'plan': a trip, whose buys follow the scroll plan (every spell she can learn and pay for). A single spell is TODO(m6-cmd-learn-spell).", Moves + "; " + SafePoint + "; a spell name is refused not-built", TripAfter, CommandAction.Trip, 0, 1);
        yield return Stub("buy", "logistics", "/buy <item> [n]", "Buys an item at home on the next trip (the contract's buy_item).", 1, 2);

        // Buffs
        yield return new("rebuff", "buffs", "/rebuff [self|bot]", "Her own pass now, from her spell bar: every line due within a buff cycle is renewed. 'bot' visits the buff bot at home instead (home first when she is away).", Moves + "; " + SafePoint + "; 'bot': the bot applies to this run (else bot-off)", "done when the buff station (or the bot's stop) ended well; failed:buff:<why> otherwise; the loop goes on from where she stands", CommandAction.Rebuff, 0, 1);
        yield return Stub("buff", "buffs", "/buff <line>", "Casts one buff line now from her spell bar.", 1, 3);

        // Vitals
        yield return Stub("heal", "vitals", "/heal", "Heals to full now (the vitals guard does this by itself under its lines).");
        yield return Stub("rest", "vitals", "/rest", "Rests to full mana and stamina where she stands.");
        yield return Stub("dance", "vitals", "/dance", "The stamina-to-mana dance now (Revitalize, then Stamina to Mana).");

        // Travel
        yield return new("home", "travel", "/home", "Recalls home and holds: the run ends cleanly at its next safe point and the game stays open (then /auto or /ladder).", Moves, "done when the run has ended at home (the stop's request name)", CommandAction.Home, Aliases: ["recall"]);
        yield return Stub("go", "travel", "/go <place>", "Travels to a named place, a town, a ground's arrival, or by the journey's portal hops.", 1, 4);
        yield return Stub("walk", "travel", "/walk <x y | cell>", "Walks to a point by the exact controller.", 1, 3);

        // Hunting
        yield return new("ladder", "hunting", "/ladder", "Back to the ladder's normal loop: from another task by a re-seat (counts kept), from off or done by a start.", "no person's stop holds it; the kill switch is off; a re-seat waits for its safe point and at most one a minute (seat-rate)", "done when the dial runs the ladder's loop", CommandAction.Ladder);
        yield return new("den", "hunting", "/den", "The den run, once: the trip home with its sale, the bot, her own pass, a Black Spawn Den's entrance, a minute inside, the tie and home; then the ladder (or idle).", "no person's stop holds it; the kill switch is off; the den run's own checks (its level, the bot's lines, the book) refuse by name", "done when the den run's task ends by its own name; then the ladder starts again unless 'then idle'", CommandAction.DenRun);
        yield return Stub("hunt", "hunting", "/hunt <ground> [minutes <n> | until <item> <n> | kills <n>]", "Hunts a named ground for a while, then back to the ladder.", 1, 4);

        // Quests
        yield return new("quest", "quests", "/quest <key>", "Starts the named quest's errand now by the start-quest path, from home (home first when she is away; a trip first when the pack is at the load line or short of slots).", Moves + "; the key is in the catalog (unknown-quest); the start-quest checks: its band, its level gate, a once-quest done, its timer, the run's selection, each refused by name; " + SafePoint, "done when the quest station ended with the quest done (task-done:quest:<key>); failed:quest:<key>:<why> otherwise; the ladder resumes", CommandAction.Quest, 1, 1);
        yield return new("quest-step", "quests", "/quest-step <key> <n>", "As /quest, starting at step n (1 is the first) and running to the end.", "as /quest; n is a step of the file (bad-args)", "as /quest", CommandAction.Quest, 2, 2);
        yield return Stub("combine", "quests", "/combine <item>", "Combines an item by its recipe now.", 1, 3);

        // Gear and character
        yield return Stub("equip", "gear", "/equip <item|best>", "Runs the gear pass, or wields one item.", 1, 3);
        yield return Stub("wear", "gear", "/wear <item>", "Wears one item from the pack.", 1, 3);
        yield return Stub("xp", "gear", "/xp [skill|attribute|lore] [amount]", "Spends experience by the existing rules.", 0, 2);
        yield return Stub("train", "gear", "/train <skill>", "Trains a skill with credits.", 1, 2);

        // Items and NPCs
        yield return Stub("use", "items", "/use <item>", "Uses an item.", 1, 3);
        yield return Stub("give", "items", "/give <item> <npc>", "Gives an item to an NPC.", 2, 6);
        yield return Stub("pickup", "items", "/pickup <item>", "Picks an item up.", 1, 3);

        // Villa
        yield return Stub("villa", "villa", "/villa [step]", "Runs the villa schedule's next step now (it runs at town trips by itself).", 0, 2);

        // Control
        yield return new("stop", "control", "/stop", "A person's /stop: she stops where she stands and the controls are theirs (only their /auto gives it back). From the desk: a clean stop that recalls home. Either way the queue is emptied.", "none: a stop always acts", "done at once; the queue is emptied and a waiting order cancelled", CommandAction.Stop);
        yield return new("pause", "control", "/pause", "Stops where she stands at the next safe point; /resume (or an order's resume) gives it back.", Moves + "; " + SafePoint, "done when the dial is interrupted by the order", CommandAction.Pause);
        yield return new("resume", "control", "/resume", "Gives the controls back after a pause (a person's /resume is their /auto).", "the dial is paused by an order (wrong-mode); a person's stop is given back only by a person", "done when the dial runs again", CommandAction.Resume);

        // Desk and queue
        yield return new("help", "desk", "/help [command]", "Lists the commands (built and not built), or one command's usage, preconditions and report.", "none", "the list, or the command's lines", CommandAction.Help, 0, 1);
        yield return new("status", "desk", "/status", "The autopilot's state, task, level, ground, the active order and the queue.", "none", "one line", CommandAction.Status);
        yield return new("queue", "desk", "/queue", "The active order and the orders queued behind it.", "none", "one line", CommandAction.Queue);
        yield return new("cancel", "desk", "/cancel [n|id]", "Takes a queued order off (the last one with no argument), or the active one before its safe point.", "the order is queued or still waiting for its safe point (too-late once taken)", "one line", CommandAction.Cancel, 0, 1);
        yield return new("clear", "desk", "/clear", "Takes every queued order off (the active one stays).", "none", "one line", CommandAction.Clear);
    }
}
