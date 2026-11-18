using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>A line the command parser wants shown in the chat: its chat type (<see cref="ChatTextTypes"/>) and text.</summary>
/// <param name="Type">The chat text type; retail's usage lines use 26 (<see cref="ChatTextTypes.SpewBox"/>), a message of the shell's own <see cref="ChatTextTypes.System"/>.</param>
/// <param name="Text">The text.</param>
public readonly record struct ChatCommandLine(int Type, string Text);

/// <summary>What one entry line came to: the action it asked for, the lines to show now, and the named TODO to log.</summary>
public sealed class ChatCommandResult
{
    /// <summary>A line that asked for nothing and showed nothing (an empty emote: retail's <c>DoEmote</c> does nothing for one).</summary>
    public static ChatCommandResult Nothing { get; } = new();

    /// <summary>The action handle, or null when the line only showed text.</summary>
    public ActionHandle? Handle { get; init; }

    /// <summary>The start of the chat line reporting a refused, failed or timed-out <see cref="Handle"/> ("your message was not sent").</summary>
    public string FailurePrefix { get; init; } = "the action did not happen";

    /// <summary>The chat line when <see cref="Handle"/> is confirmed, or null for none.</summary>
    public string? SuccessLine { get; init; }

    /// <summary>The lines to show now.</summary>
    public IReadOnlyList<ChatCommandLine> Lines { get; init; } = [];

    /// <summary>The key of the named TODO to log once (with <see cref="TodoMessage"/>), or null.</summary>
    public string? TodoKey { get; init; }

    /// <summary>The log text of <see cref="TodoKey"/>. It never holds what the player typed.</summary>
    public string? TodoMessage { get; init; }

    /// <summary>M5 7 D145: true for a command the client carried out itself (<c>/stop</c>, <c>/auto</c>): its answer is a line alone, but the command was not declined.</summary>
    public bool ClientHandled { get; init; }

    /// <summary>True when the line was a Talk (plain speech, <c>/say</c>, or a command sent on to the server).</summary>
    public bool IsTalk => Handle?.Kind == ActionKind.Talk;
}

/// <summary>
/// The autopilot's two typed controls (M5 7 D145): <c>/stop</c> and <c>/auto</c>, answered by the client and never sent to the server. The play host's autopilot implements it; each call does what its command asks (or nothing, when the
/// autopilot is already where the command would put it) and returns the plain line the chat window shows.
/// </summary>
public interface IAutopilotChatControls
{
    /// <summary><c>/stop</c>: the autopilot stops where it stands and the movement keys are the person's again. Returns the chat line.</summary>
    string Stop();

    /// <summary><c>/auto</c>: the autopilot checks every state a fight needs and resumes. Returns the chat line.</summary>
    string Auto();

    /// <summary>
    /// M6 6 MD1, <c>/auto &lt;task&gt;</c>: chooses the task the autopilot runs and then does what <see cref="Auto"/> does. The task type is in the session contract (<see cref="AutopilotTask"/>). A refusal (the dial is running, no such quest file) is the returned line and nothing starts.
    /// The default answers that this host cannot choose a task, so a host that has not built it fails in plain words, never silently.
    /// </summary>
    /// <param name="task">The parsed task.</param>
    /// <returns>The chat line.</returns>
    string Run(AutopilotTask task) => "Lifestone: this host cannot choose a task (TODO(m6-chat-task-host)); nothing was started.";

    /// <summary>
    /// The autopilot's orders (the command registry, 2026-11-17): <c>/trip</c>, <c>/rebuff</c>, <c>/quest &lt;key&gt;</c>, <c>/ladder</c>, <c>/help</c>, <c>/status</c> and the rest. Returns the lines for the chat window (the host's runner may send
    /// them through its own line instead and return none), or null when <paramref name="name"/> is no order, so the line goes its usual way. The default knows no order.
    /// </summary>
    /// <param name="name">The command's name, lower case, without the slash.</param>
    /// <param name="line">The whole line as typed, trimmed.</param>
    IReadOnlyList<string>? Command(string name, string line) => null;
}

/// <summary>
/// The entry's <c>/</c> commands (docs/retail-ui.md section 21.2). Retail's <c>ChatInterface::ProcessCommand</c> (11.6096 0x004F5D60) hands the line to
/// <c>ClientCommunicationSystem::OnChatCommand</c> (11.6096 0x005821A0), which turns a leading <c>/</c> into <c>@</c> and calls <c>DoCommand</c> (11.6096 0x0057EFF0):
/// the line is split on spaces and tabs, the first word (without its <c>@</c> and a trailing comma) is looked up, case-blind, in the table <c>InitializeCommands</c>
/// (11.6096 0x005827F0) fills, and the rest are the arguments. A name that is not in the table is tried as a channel (<c>DoChannelCommand</c>, 11.6096 0x00578270) and,
/// failing that, the whole line goes to the server as Talk (<c>CM_Communication::Event_Talk</c>), which is how ACE's own <c>@</c> commands are reached.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built here</b> (names and aliases are the table's): tell (<c>tell</c> <c>t</c> <c>send</c> <c>whisper</c> <c>w</c>; <c>DoTell</c>, 11.4186 0x00577E40, no 11.6096
/// address in the symbol table), reply (<c>reply</c> <c>r</c> <c>rp</c>; <c>DoReply</c>, 11.4186 0x00577910), retell (<c>retell</c> <c>rt</c>; <c>DoReTell</c>, 11.6096
/// 0x005789A0), say (<c>say</c> <c>s</c>), emote (<c>e</c> <c>em</c> <c>emote</c> <c>me</c>; <c>DoEmote</c>, 11.6096 0x005798A0), AFK (<c>afk</c>; <c>DoAFK</c>, 11.6096
/// 0x0057C1C0), the lifestone recall (<c>lifestone</c> <c>lif</c> <c>ls</c>; <c>DoLifestone</c>, 11.4186 0x0056FC70) and the marketplace recall (<c>marketplace</c>
/// <c>mar</c> <c>mp</c>; <c>DoMarketplace</c>, 11.6096 0x00570AB0). The arguments are joined with single spaces (<c>JoinArgs</c>, 11.6096 0x00576890), so runs of spaces in
/// a message collapse, as retail's do.
/// </para>
/// <para>
/// <b>Lifestone's own (M5 7 D145):</b> <c>/stop</c> and <c>/auto</c>, the autopilot's controls (<see cref="Autopilot"/>), handled here and never sent: neither name is in retail's table (built, channel or other, below) nor one of
/// ACE's commands (ACE has <c>stop-now</c>, a different name; its command table is matched by the whole name). Each answers with a local system line; <c>/stop</c> reads no arguments, <c>/auto</c> takes an optional task (M6 6 MD1).
/// </para>
/// <para>
/// <b>Answered by name, not built:</b> the channels (<c>TODO(g4-chat-channels)</c>, M6) and the other client commands in retail's table
/// (<c>TODO(g4-chat-commands-rest)</c>: help, friends, allegiance, house and the rest). <b>Not built either:</b> the <c>:</c> and <c>;</c> emote prefixes
/// (<c>OnChatCommand</c> rewrites them to <c>@emote</c>; <c>TODO(g4-chat-emote-prefix)</c>) and the <c>@</c> prefix as a client command: an <c>@</c> line goes to the server
/// as Talk as it did in M2 (<c>TODO(g4-chat-at-commands)</c>).
/// </para>
/// <para>
/// <b>Two differences from retail, both because the Action API takes names.</b> Retail replies to the last teller by id (<c>Event_TalkDirect</c>) and to the selected
/// object by id; here both are a <see cref="IActionApi.Tell"/> to the name. A tell's name loses the trailing space a space before the comma leaves (retail sends it
/// with the space, which names no one).
/// </para>
/// </remarks>
public sealed class ChatCommands
{
    /// <summary>Retail's AFK message limit: <c>DoAFK</c> cuts a longer one at 0xC0 characters.</summary>
    public const int AfkMessageLimit = 0xC0;

    private const string UseComma = "Use comma after the name for targeted chat.";
    private const string NeedText = "You must specify the text you wish to say!";
    private const string NeedTell = "Someone must @tell you first!";
    private const string NeedTellee = "You must first provide a name using @tell";

    // InitializeCommands (11.6096 0x005827F0): the channel names (DoStupidChannelHack's aliases and the Turbine channels).
    private static readonly HashSet<string> ChannelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "allegiance", "c", "co-vassals", "covassals", "covassal", "f", "fellowship", "fellows", "fellow", "g", "group", "party", "m", "monarch", "p", "patron",
        "vassals", "vassal", "general", "trade", "lfg", "roleplay", "olthoi", "society",
    };

    // InitializeCommands: the other names of the client's table that this slice does not build. (A name from the table that has no entry in either set is built.)
    private static readonly HashSet<string> UnbuiltNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "?", "help", "commands", "all", "ab", "alh", "ah", "motd", "speaker", "channels", "age", "status", "birth", "clear", "text", "consent", "death", "cor", "corpse",
        "day", "die", "emotes", "endurance", "fillcomps", "filter", "unfilter", "framerate", "friends", "friends_add", "friends_remove", "hslist", "index",
        "title", "join", "leave", "loadautoui", "loadfile", "loadui", "saveautoui", "saveui", "lockui", "loc", "log", "hor", "message_types", "messagetypes", "msg_types",
        "msgtypes", "permit", "pka", "pkarena", "pkl", "pklite", "pla", "pklarena", "render", "version",
    };

    private readonly IActionApi _actions;
    private readonly Func<string?> _lastTeller;
    private readonly Func<bool> _isAfk;

    /// <summary>Makes the parser over the Action API.</summary>
    /// <param name="actions">The actions the commands ask for (all with <see cref="ActionSource.Ui"/>).</param>
    /// <param name="lastTeller">The name of the last player who sent a tell to this player (retail's <c>GetLastTeller</c>), or null when none has.</param>
    /// <param name="isAfk">True while the server's AFK flag is set (<see cref="PlayerQualities.Afk"/>): <c>DoAFK</c> sends the mode only when it would change.</param>
    public ChatCommands(IActionApi actions, Func<string?> lastTeller, Func<bool> isAfk)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(lastTeller);
        ArgumentNullException.ThrowIfNull(isAfk);
        _actions = actions;
        _lastTeller = lastTeller;
        _isAfk = isAfk;
    }

    /// <summary>Lifestone's <c>/stop</c> command (M5 7 D145).</summary>
    public const string StopCommand = "stop";

    /// <summary>Lifestone's <c>/auto</c> command (M5 7 D145).</summary>
    public const string AutoCommand = "auto";

    /// <summary>The line <c>/stop</c> and <c>/auto</c> answer when no autopilot is wired to the chat (another host, or before the play host wired it).</summary>
    public const string NoAutopilotLine = "Lifestone: there is no autopilot in this session; nothing was sent.";

    /// <summary>The autopilot's controls for <c>/stop</c> and <c>/auto</c> (M5 7 D145; the play host wires them), or null for none.</summary>
    public IAutopilotChatControls? Autopilot { get; set; }

    /// <summary>The name of the last tell this player sent (retail's <c>SetLastTelleeName</c>), or null.</summary>
    public string? LastTellee { get; private set; }

    /// <summary>True when <paramref name="line"/> is a command line this parser takes (it starts with '/').</summary>
    public static bool IsCommand(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.TrimStart().StartsWith('/');
    }

    /// <summary>
    /// Speaks <paramref name="text"/> to what the talk focus names (<c>OnChatCommand</c>'s switch on <c>GetTalkFocus</c>): All is local speech, Selected a tell to the
    /// last creature selected, the rest are channels (<c>TODO(g4-chat-channels)</c>).
    /// </summary>
    /// <param name="focus">The talk focus.</param>
    /// <param name="text">The text.</param>
    /// <param name="selectedName">The name of the last speakable (creature) target, or null for none.</param>
    public ChatCommandResult Say(TalkFocus focus, string text, string? selectedName)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (focus)
        {
            case TalkFocus.All:
                return Talk(text);
            case TalkFocus.Selected:
                if (string.IsNullOrWhiteSpace(selectedName))
                {
                    // Retail sends nothing (its Selected row is disabled until a creature is selected); the shell says so rather than drop the text.
                    return Lines(ChatTextTypes.System, "Lifestone: you must select a creature to tell first (nothing was sent).");
                }

                return TellTo(selectedName, text);
            default:
                return Channel(focus.ToString());
        }
    }

    /// <summary>Runs one '/' line (see the class remarks). The line is trimmed first, as <c>OnChatCommand</c> does.</summary>
    public ChatCommandResult Run(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return ChatCommandResult.Nothing;
        }

        // DoCommand: split on spaces and tabs, the command is the first word without its '@' and a trailing comma.
        var words = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var name = words[0][1..].TrimEnd(',');
        var args = words.Length > 1 ? words[1..] : [];
        switch (name.ToLowerInvariant())
        {
            case "tell" or "t" or "send" or "whisper" or "w":
                return Tell(args);
            case "reply" or "r" or "rp":
                return Reply(args);
            case "retell" or "rt":
                return Retell(args);
            case "say" or "s":
                return Talk(Join(args));
            case "e" or "em" or "emote" or "me":
                return Emote(args);
            case "afk":
                return Afk(args);
            case "lifestone" or "lif" or "ls":
                return args.Length > 0
                    ? Lines(ChatTextTypes.SpewBox, "Please see @help lifestone for more information on how to use this command.")
                    : Recall(_actions.RecallLifestone(ActionSource.Ui), "the lifestone recall did not happen", "Lifestone: you are at your lifestone.");
            case "marketplace" or "mar" or "mp":
                return args.Length > 0
                    ? Lines(ChatTextTypes.SpewBox, "Please see @help marketplace for more information on how to use this command.")
                    : Recall(_actions.RecallMarketplace(ActionSource.Ui), "the marketplace recall did not happen", "Lifestone: you are in the marketplace.");
            case "house" or "hou":
                return House(args);
            case StopCommand:
                return AutopilotLine(static c => c.Stop());
            case AutoCommand:
                return args.Length == 0 ? AutopilotLine(static c => c.Auto()) : AutoTask(args);
        }

        // The autopilot's orders (the command registry): after the client's own commands, before the channels and retail's unbuilt names (help, status and clear answer the autopilot while one is wired).
        if (Autopilot?.Command(name.ToLowerInvariant(), trimmed) is { } orderLines)
        {
            return new ChatCommandResult { Lines = [.. orderLines.Select(l => new ChatCommandLine(ChatTextTypes.System, l))], ClientHandled = true };
        }

        if (ChannelNames.Contains(name))
        {
            return Channel("/" + name.ToLowerInvariant());
        }

        if (UnbuiltNames.Contains(name))
        {
            return new ChatCommandResult
            {
                Lines = [new ChatCommandLine(ChatTextTypes.System, "Lifestone: the /" + name.ToLowerInvariant() + " command is not built yet (TODO(g4-chat-commands-rest)).")],
                TodoKey = "chat:cmd:" + name.ToLowerInvariant(),
                TodoMessage = "TODO(g4-chat-commands-rest): /" + name.ToLowerInvariant() + " is a client command of retail's table that is not built; nothing was sent",
            };
        }

        // Not in the table: retail sends the whole line on as Talk with the '/' turned into '@'; ACE answers it (its own commands, or "unknown command").
        return Talk("@" + trimmed[1..]);
    }

    /// <summary>The usage line <c>/auto</c> answers for an argument it does not know (M6 6 MD1).</summary>
    public const string AutoUsage = "Lifestone: /auto [level-up | level-up quest <key> | quest <key> | errand]: level-up climbs the ladder, level-up quest <key> starts the climb with that quest's errand, quest <key> runs one quest, errand runs the errand; nothing was started.";

    // M6 6 MD1: /auto <task>. level-up is the Job task; "quest <key>" and "quest:<key>" are the quest task; errand is the errand. Anything else answers the usage line and sends nothing.
    private ChatCommandResult AutoTask(string[] args)
    {
        var word = args[0].ToLowerInvariant();
        string text;
        switch (word)
        {
            case "level-up" or "levelup" or "job" when args.Length == 1:
                text = "job";
                break;
            case "level-up" or "levelup" or "job" when args.Length == 3 && args[1].Equals("quest", StringComparison.OrdinalIgnoreCase):
                // M6 14 the start quest (the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once)): the level up job, its first errand the quest named (--autopilot-start-quest's twin)
                if (!Session.Quests.QuestCatalog.All.Any(q => q.Key == args[2]))
                {
                    return Lines(ChatTextTypes.System, $"Lifestone: '{args[2]}' is no quest of the catalog; nothing was started.", clientHandled: true);
                }

                return AutopilotLine(c => c.Run(new AutopilotTask(AutopilotTaskKind.Job, args[2])));
            case "errand" when args.Length == 1:
                text = "errand";
                break;
            case "quest" when args.Length == 2:
                text = "quest:" + args[1];
                break;
            case var w when w.StartsWith("quest:", StringComparison.Ordinal) && args.Length == 1:
                text = "quest:" + args[0]["quest:".Length..];
                break;
            default:
                return Lines(ChatTextTypes.System, AutoUsage, clientHandled: true);
        }

        if (!AutopilotTask.TryParse(text, out var task, out var error))
        {
            return Lines(ChatTextTypes.System, "Lifestone: " + error + "; nothing was started.", clientHandled: true);
        }

        return AutopilotLine(c => c.Run(task));
    }

    private static string Join(string[] args) => string.Join(' ', args).Trim();

    // M5 7 D145: /stop and /auto are the client's; nothing goes to the server (no handle), and the answer is a local system line.
    private ChatCommandResult AutopilotLine(Func<IAutopilotChatControls, string> run) => Autopilot is { } controls
        ? new ChatCommandResult { Lines = [new ChatCommandLine(ChatTextTypes.System, run(controls))], ClientHandled = true }
        : Lines(ChatTextTypes.System, NoAutopilotLine);

    private static ChatCommandResult Lines(int type, string text, bool clientHandled = false) => new() { Lines = [new ChatCommandLine(type, text)], ClientHandled = clientHandled };

    private static ChatCommandResult Channel(string what) => new()
    {
        Lines = [new ChatCommandLine(ChatTextTypes.System, "Lifestone: chat to " + what + " is not built yet (TODO(g4-chat-channels)); nothing was sent.")],
        TodoKey = "chat:channels",
        TodoMessage = "TODO(g4-chat-channels): the channels and allegiance chat need the gameplay milestone (M6); the line was not sent",
    };

    private ChatCommandResult Talk(string text) => new()
    {
        Handle = _actions.Talk(text, ActionSource.Ui),
        FailurePrefix = "your message was not sent",
    };

    private ChatCommandResult TellTo(string name, string text) => new()
    {
        Handle = _actions.Tell(name, text, ActionSource.Ui),
        FailurePrefix = "your tell to " + name + " was not sent",
    };

    /// <summary>
    /// <c>/house</c> (M6 villa): <c>/house recall</c> sends TeleToHouse (<see cref="IActionApi.RecallHouse"/>); the other subcommands of retail's table (guest, storage, open,
    /// boot, available, ...) are not built (<c>TODO(villa-house-subcommands)</c>) and say so.
    /// </summary>
    private ChatCommandResult House(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "recall", StringComparison.OrdinalIgnoreCase))
        {
            return Recall(_actions.RecallHouse(ActionSource.Ui), "the house recall did not happen", "Lifestone: you are at your house.");
        }

        return new ChatCommandResult
        {
            Lines = [new ChatCommandLine(ChatTextTypes.System, "Lifestone: only /house recall is built (TODO(villa-house-subcommands)).")],
            TodoKey = "chat:cmd:house",
            TodoMessage = "TODO(villa-house-subcommands): /house " + string.Join(' ', args) + " is not built; nothing was sent",
        };
    }

    private static ChatCommandResult Recall(ActionHandle handle, string failure, string done) => new() { Handle = handle, FailurePrefix = failure, SuccessLine = done };

    // DoTell: JoinArgsAsName joins the words, trims the blanks and then leading '+'; the name is what comes before the first comma (which must not be the first
    // character), the text what follows, trimmed. No comma: retail's usage line.
    private ChatCommandResult Tell(string[] args)
    {
        var joined = Join(args).TrimStart('+');
        var comma = joined.IndexOf(',', StringComparison.Ordinal);
        if (comma < 1)
        {
            return Lines(ChatTextTypes.SpewBox, UseComma);
        }

        var name = joined[..comma].TrimEnd();
        LastTellee = name;
        return TellTo(name, joined[(comma + 1)..].Trim());
    }

    // DoReply: the text first ("You must specify..."), then the last teller ("Someone must @tell you first!"); both are type 26 lines.
    private ChatCommandResult Reply(string[] args)
    {
        var text = Join(args);
        if (text.Length == 0)
        {
            return Lines(ChatTextTypes.SpewBox, NeedText);
        }

        return _lastTeller() is { Length: > 0 } teller ? TellTo(teller, text) : Lines(ChatTextTypes.SpewBox, NeedTell);
    }

    // DoReTell: the text to the last tellee. Retail's text for an empty message is not read: it is the reply's (TODO(m5-6-retell-empty-text)).
    private ChatCommandResult Retell(string[] args)
    {
        var text = Join(args);
        if (LastTellee is not { Length: > 0 } tellee)
        {
            return Lines(ChatTextTypes.SpewBox, NeedTellee);
        }

        return text.Length == 0 ? Lines(ChatTextTypes.SpewBox, NeedText) : TellTo(tellee, text);
    }

    // DoEmote: an empty emote does nothing; else Event_Emote.
    private ChatCommandResult Emote(string[] args)
    {
        var text = Join(args);
        return text.Length == 0
            ? ChatCommandResult.Nothing
            : new ChatCommandResult { Handle = _actions.Emote(text, ActionSource.Ui), FailurePrefix = "your emote was not sent" };
    }

    // DoAFK: "/afk" and "/afk on" turn the mode on when it is off; "/afk off" turns it off when it is on; "/afk msg <text>" sets the message (cut at 0xC0
    // characters) and says so. Anything else gets retail's help, whose text is not copied (TODO(g4-chat-help)).
    private ChatCommandResult Afk(string[] args)
    {
        var sub = args.Length == 0 ? string.Empty : args[0].ToLowerInvariant();
        switch (sub)
        {
            case "" or "on":
                return _isAfk()
                    ? ChatCommandResult.Nothing
                    : new ChatCommandResult { Handle = _actions.SetAfk(true, null, ActionSource.Ui), FailurePrefix = "AFK was not set", SuccessLine = "Lifestone: you are away from the keyboard." };
            case "off":
                return _isAfk()
                    ? new ChatCommandResult { Handle = _actions.SetAfk(false, null, ActionSource.Ui), FailurePrefix = "AFK was not cleared", SuccessLine = "Lifestone: you are back at the keyboard." }
                    : ChatCommandResult.Nothing;
            case "msg":
                var message = Join(args[1..]);
                if (message.Length > AfkMessageLimit)
                {
                    message = message[..AfkMessageLimit];
                }

                // The Action API sets the message only together with the mode (SetAfk(true, text)); retail's "/afk msg" leaves the mode as it is.
                return new ChatCommandResult
                {
                    Handle = _actions.SetAfk(true, message.Length == 0 ? null : message, ActionSource.Ui),
                    FailurePrefix = "AFK was not set",
                    Lines = [new ChatCommandLine(0, "New AFK message set: " + (message.Length == 0 ? "I am currently away from the keyboard." : message))],
                    TodoKey = "chat:afk-message-only",
                    TodoMessage = "TODO(m5-6-afk-message-only): '/afk msg' sets the message and turns AFK on (the Action API has no message-only call); retail leaves the mode as it is",
                };
            default:
                return new ChatCommandResult
                {
                    Lines = [new ChatCommandLine(ChatTextTypes.SpewBox, "Usage: /afk, /afk on, /afk off, /afk msg <text>.")],
                    TodoKey = "chat:afk-help",
                    TodoMessage = "TODO(g4-chat-help): retail's /afk help text (HelpAFK, 11.6096 not in the symbol table) is not copied; a short usage line is shown",
                };
        }
    }

    /// <summary>Formats the failure line of a handle: "Lifestone: &lt;prefix&gt; (&lt;detail&gt;)."</summary>
    internal static string FailureLine(string prefix, ActionOutcome outcome)
    {
        return "Lifestone: " + prefix + " (" + (outcome.Detail ?? outcome.Kind.ToString()) + ").";
    }
}
