using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Tools;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The Lifestone server mod's player tools (DoD 10; m2 §7 decision 3): admin-like conveniences for a normal Player
/// account, through the same action rules as everything else (<see cref="IGameSessionView.Tools"/>). Built by step ST12.
/// </summary>
/// <remarks>
/// <para>Each request is a Talk line <c>@lifestone &lt;verb&gt; ... #&lt;id&gt;</c> built with the mod's shared wire
/// contract (ToolsWire; the id is <c>a</c> and the handle's id), and completes on the mod's F7E0 reply with the same id:
/// <see cref="ActionOutcomeKind.Confirmed"/> with the typed <see cref="ToolHandle{T}.Result"/>, or
/// <see cref="ActionOutcomeKind.ServerError"/> with the mod's error code and sentence in <see cref="ActionOutcome.Detail"/>
/// (<c>not-found: ...</c>). The timeout is 5 s and the rate 2 a second. The human reply line goes to the chat; the JSON
/// never does.</para>
/// <para>Every use adds a chat line of its own when it is sent (for example "Lifestone: teleporting to Holtburg...") and
/// an action-log entry. Requests are refused (<see cref="ActionOutcomeKind.Refused"/>) unless <see cref="Availability"/>
/// allows them: against a non-loopback server the tools never work (one WARNING per refused request, naming it), without
/// the mod they are refused once the server has said so, and outside the world there is no character to run them
/// for. An offline session refuses them all.</para>
/// <para><b>The status probe.</b> When the player's CreateObject arrives (entering the world), the session asks the mod
/// for its status silently (<see cref="ActionSource.Client"/>; no chat line either way). An access level above Player, or
/// ACE's first-account auto-promotion on, is logged as an ERROR and engages the kill switch, which stops automation
/// (owner decision D11).</para>
/// <para>Game thread only.</para>
/// </remarks>
public sealed class PlayerTools
{
    /// <summary>The access level every Lifestone account has (ACE's <c>AccessLevel.Player</c>).</summary>
    public const string PlayerAccess = "Player";

    private readonly ActionApi _api;
    private bool _modMissing;
    private bool _modAnswered;
    private bool _notLoopbackPublished;
    private bool _probed;

    /// <summary>Only the session creates one.</summary>
    internal PlayerTools(ActionApi api)
    {
        _api = api;
    }

    /// <summary>
    /// Whether the tools can be used now, in this order: <see cref="ToolAvailability.NotLoopback"/> for a server that is
    /// not loopback; <see cref="ToolAvailability.NotInWorld"/> outside the world (always, for an offline session);
    /// <see cref="ToolAvailability.ModMissing"/> once the server said <c>Unknown command: lifestone</c>;
    /// <see cref="ToolAvailability.Ready"/> once the mod answered; <see cref="ToolAvailability.Unknown"/> before that.
    /// Requests go out while it is Unknown or Ready.
    /// </summary>
    public ToolAvailability Availability
    {
        get
        {
            if (!_api.ServerIsLoopback)
            {
                return ToolAvailability.NotLoopback;
            }

            var state = _api.State;
            if ((state.IsOffline && !_api.StandInAnswersWorld) || state.Phase != Lifestone.Net.Session.SessionPhase.InWorld || state.Self.Guid == 0)
            {
                return ToolAvailability.NotInWorld;
            }

            return _modMissing ? ToolAvailability.ModMissing : _modAnswered ? ToolAvailability.Ready : ToolAvailability.Unknown;
        }
    }

    /// <summary>The last status the mod reported (the probe's, or a later <see cref="Status"/>), or null.</summary>
    public ToolStatus? LastStatus { get; private set; }

    /// <summary>
    /// Asks the mod for its status (<c>@lifestone status</c>). The session probes it silently on entering the world;
    /// <paramref name="showInChat"/> also shows the request and the reply line in the chat.
    /// </summary>
    /// <param name="source">Who asked.</param>
    /// <param name="showInChat">True to show the request and the human reply line in the chat.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<ToolStatus> Status(ActionSource source, bool showInChat = false) => RequestStatus(source, showInChat, probe: false);

    /// <summary>
    /// Teleports the player to <paramref name="destination"/>: ACE's points of interest by their first ACE name
    /// (<c>place poi "&lt;name&gt;"</c>), every other destination by its location (<c>place loc &lt;cell&gt; &lt;x&gt;
    /// &lt;y&gt; &lt;z&gt; &lt;qw&gt; &lt;qx&gt; &lt;qy&gt; &lt;qz&gt;</c>, invariant culture; <see cref="PlaceRequests"/>).
    /// Confirmed when the teleport has started; portal space follows the F751 and the position update as for any teleport.
    /// </summary>
    /// <param name="destination">Where to go (from <see cref="TeleportCatalog"/>).</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<PlaceResult> Place(TeleportDestination destination, ActionSource source)
    {
        var label = destination?.Label ?? "nowhere";
        return Request(
            ActionKind.ToolPlace,
            source,
            id => destination is null ? throw new ArgumentException("no destination") : PlaceRequests.For(destination, id),
            "Lifestone: teleporting to " + label + "...",
            silent: false,
            ToolResultReader.Place,
            observe: null,
            "ToolPlace " + label);
    }

    /// <summary>Asks the server where the player is (<c>@lifestone where</c>).</summary>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<WhereResult> WhereAmI(ActionSource source) =>
        Request(
            ActionKind.ToolWhereAmI,
            source,
            id => new WhereRequest(id),
            "Lifestone: asking the server where you are...",
            silent: false,
            ToolResultReader.Where,
            observe: null,
            "ToolWhereAmI");

    /// <summary>Adds experience to the character (<c>@lifestone xp &lt;amount&gt;</c>, 1 to 999,999,999,999; ACE clamps).</summary>
    /// <param name="amount">The experience to add.</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<XpResult> AddXp(long amount, ActionSource source) =>
        Request(
            ActionKind.ToolAddXp,
            source,
            id => new XpRequest(amount, id),
            string.Create(CultureInfo.InvariantCulture, $"Lifestone: asking for {amount:N0} experience..."),
            silent: false,
            ToolResultReader.Xp,
            observe: null,
            "ToolAddXp amount=" + ActionText.Count(amount));

    /// <summary>
    /// Stamps the quest <paramref name="name"/> for the player's own character (<c>@lifestone quest stamp &lt;name&gt;</c>), as a
    /// quest script's StampQuest emote does: the registry row is added with one completion, or gains one, unless the quest's
    /// world definition has reached its maximum (the result then says it did not change). The result carries the
    /// completions before and after. Confirmed when ACE has recorded it.
    /// </summary>
    /// <param name="name">The quest's name as ACE spells it: 1 to 64 of letters, digits, <c>_</c>, <c>-</c>, <c>(</c> and <c>)</c>.</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<QuestResult> StampQuest(string name, ActionSource source) => Quest(QuestAction.Stamp, name, source);

    /// <summary>
    /// Erases the quest <paramref name="name"/> from the player's own character (<c>@lifestone quest erase &lt;name&gt;</c>). A
    /// quest the character did not hold is not an error: the result says it was not held.
    /// </summary>
    /// <param name="name">The quest's name (see <see cref="StampQuest"/>).</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<QuestResult> EraseQuest(string name, ActionSource source) => Quest(QuestAction.Erase, name, source);

    /// <summary>Asks whether the player's own character holds the quest <paramref name="name"/> (<c>@lifestone quest has &lt;name&gt;</c>), changing nothing.</summary>
    /// <param name="name">The quest's name (see <see cref="StampQuest"/>).</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<QuestResult> HasQuest(string name, ActionSource source) => Quest(QuestAction.Has, name, source);

    /// <summary>
    /// Purges the character <paramref name="name"/> from the server's database (<c>@lifestone char purge "&lt;name&gt;"</c>), for good.
    /// The mod does it only for a character on this player's own account that was already marked for deletion on the character
    /// screen, and refuses (<see cref="ActionOutcomeKind.ServerError"/>, <see cref="ActionOutcome.Detail"/> <c>&lt;code&gt;: &lt;sentence&gt;</c>)
    /// a graduated test character (<c>graduated</c>), the character being played (<c>own-character</c>), a name with no character on the
    /// account (<c>unknown-character</c>), an online character (<c>character-online</c>) and one not marked for deletion
    /// (<c>not-pending-deletion</c>). Confirmed once ACE has removed the rows, with the counts. The player must be in the world, as
    /// a different character from the one purged (a pending-deletion character cannot enter).
    /// </summary>
    /// <param name="name">The character's name: 1 to 32 of letters, digits, space, apostrophe and hyphen.</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<CharPurgeResult> PurgeCharacter(string name, ActionSource source) =>
        Request(
            ActionKind.ToolCharPurge,
            source,
            id => new CharPurgeRequest(name, id),
            "Lifestone: asking the server to purge character " + (name ?? "(none)") + "...",
            silent: false,
            ToolResultReader.CharPurge,
            observe: null,
            "ToolCharPurge " + (name ?? "(none)"));

    /// <summary>Why a request is refused for <paramref name="availability"/>.</summary>
    internal static string Refusal(ToolAvailability availability) => availability switch
    {
        ToolAvailability.NotLoopback => "the server is not on loopback: the player tools work only against the bundled ACE",
        ToolAvailability.ModMissing => "the Lifestone server mod is not loaded (the server answered 'Unknown command: lifestone')",
        ToolAvailability.NotInWorld => "the player is not in the world",
        _ => "the player tools are not available (" + availability + ")",
    };

    /// <summary>The player's CreateObject arrived: probe the mod's status, silently, once per world entry.</summary>
    internal void OnEnteringWorld()
    {
        if (_probed || _modMissing || _api.State.IsOffline || !_api.ServerIsLoopback)
        {
            return;
        }

        // The availability is checked when the request's turn comes, not now: the player's object may arrive before the
        // network session reports the world's phase.
        _probed = true;
        RequestStatus(ActionSource.Client, showInChat: false, probe: true);
    }

    /// <summary>The player left the world: the next entry probes again.</summary>
    internal void OnLeftWorld() => _probed = false;

    /// <summary>A tool reply arrived: whatever it says, the mod is loaded.</summary>
    internal void OnReply() => _modAnswered = true;

    /// <summary>The server said the mod is not loaded.</summary>
    internal void OnModMissing() => _modMissing = true;

    /// <summary>Forgets what the server said (a reconnect may reach a server with the mod).</summary>
    internal void Reset()
    {
        _modMissing = false;
        _modAnswered = false;
        _notLoopbackPublished = false;
        _probed = false;
        LastStatus = null;
    }

    private ToolHandle<QuestResult> Quest(QuestAction action, string name, ActionSource source)
    {
        var word = ToolRequestParser.QuestActionWord(action);
        return Request(
            ActionKind.ToolQuest,
            source,
            id => new QuestRequest(action, name, id),
            "Lifestone: asking the server to " + word + " quest " + (name ?? "(none)") + "...",
            silent: false,
            ToolResultReader.Quest,
            observe: null,
            "ToolQuest " + word + " " + (name ?? "(none)"));
    }

    private ToolHandle<ToolStatus> RequestStatus(ActionSource source, bool showInChat, bool probe) =>
        Request(
            ActionKind.ToolStatus,
            source,
            id => new StatusRequest(id),
            showInChat ? "Lifestone: asking the server mod for its status..." : null,
            silent: !showInChat,
            ToolResultReader.Status,
            ObserveStatus,
            probe ? "ToolStatus probe" : "ToolStatus" + (showInChat ? string.Empty : " silent"),
            availabilityNow: !probe);

    private ToolHandle<T> Request<T>(
        ActionKind kind,
        ActionSource source,
        Func<string, ToolRequest> build,
        string? chatLine,
        bool silent,
        Func<ToolReply, T> read,
        Action<T>? observe,
        string what,
        bool availabilityNow = true)
        where T : class
    {
        var runner = "PlayerTools." + kind.ToString()["Tool".Length..];
        _api.CheckThread(runner);
        var id = _api.NextId();
        var requestId = "a" + ActionText.Count(id);
        var handle = new ToolHandle<T>(id, kind, source, what + " #" + requestId, _api.Clock.Now);

        ActionOutcome? early = null;
        ToolRequest? request = null;
        string text = string.Empty;
        if (!_api.IsOffline && !_api.ServerIsLoopback)
        {
            // The gate logs one WARNING naming the request and the reason.
            _api.CheckLoopback(runner, out var refusal);
            early = new ActionOutcome(ActionOutcomeKind.Refused, 0, refusal);
            PublishNotLoopbackOnce(refusal);
        }
        else
        {
            try
            {
                request = build(requestId);
                text = ToolsWire.Request(request);
            }
            catch (ArgumentException e)
            {
                early = new ActionOutcome(ActionOutcomeKind.Refused, 0, e.Message);
            }

            if (early is null && availabilityNow && !_api.IsOffline && Availability is not (ToolAvailability.Unknown or ToolAvailability.Ready))
            {
                early = new ActionOutcome(ActionOutcomeKind.Refused, 0, Refusal(Availability));
            }
        }

        var tracker = new ToolTracker<T>(handle, request ?? new StatusRequest(requestId), text, chatLine, silent, read, observe);
        _api.Accept(tracker, early, loopbackChecked: true);
        return handle;
    }

    private void PublishNotLoopbackOnce(string? refusal)
    {
        if (_notLoopbackPublished)
        {
            return;
        }

        _notLoopbackPublished = true;
        _api.PublishToolsUnavailable(ToolAvailability.NotLoopback, "The player tools work only against the bundled ACE on loopback: " + refusal);
    }

    /// <summary>
    /// The status reply's checks (owner decision D11): the account must be a Player and ACE's first-account auto-promotion
    /// off; otherwise an ERROR, and the kill switch stops automation.
    /// </summary>
    private void ObserveStatus(ToolStatus status)
    {
        LastStatus = status;
        if (!string.Equals(status.Access, PlayerAccess, StringComparison.Ordinal))
        {
            _api.Log.Error(
                SessionLog.Category,
                "the account's access level is above Player: automation is stopped (a Lifestone account is always a Player; owner decision D11)",
                new LogField("access", status.Access));
            _api.RequestKillSwitch("the account's access level is " + status.Access + ", above Player");
        }

        if (status.AutoPromotion)
        {
            _api.Log.Error(
                SessionLog.Category,
                "ACE's first-account Admin auto-promotion is on: automation is stopped (the server mod turns it off; owner decision D11)");
            _api.RequestKillSwitch("ACE's first-account Admin auto-promotion is on");
        }
    }
}
