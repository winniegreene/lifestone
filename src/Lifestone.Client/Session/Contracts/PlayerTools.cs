using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Mage;
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

    /// <summary>The error code of a <see cref="LearnSpells"/> that named an id the spell table does not hold (a client-side word; the mod's reply is <c>ok</c>).</summary>
    public const string UnknownSpellCode = "unknown-spell";

    /// <summary>The most ids one <see cref="LearnSpells"/> may name; a longer list is refused before it is sent, as the mod would refuse it.</summary>
    public const int MaxSpellIds = ToolRequestParser.MaxSpellIds;

    /// <summary>
    /// Teaches the character the spells <paramref name="spellIds"/> (<c>@lifestone spell learn &lt;id&gt;[,&lt;id&gt;...]</c>), over ACE's own learn path. Every id is
    /// carried out; the result lists each one as learned, known (the character had it) or unknown (not in the spell table). The handle is
    /// <see cref="ActionOutcomeKind.Confirmed"/> only when no id was unknown; an unknown id ends it as <see cref="ActionOutcomeKind.ServerError"/>
    /// with <see cref="UnknownSpellCode"/> and the ids in <see cref="ActionOutcome.Detail"/>, and <see cref="ToolHandle{T}.Result"/> still set. A list of
    /// no ids or of more than <see cref="MaxSpellIds"/> is <see cref="ActionOutcomeKind.Refused"/> at once and never sent.
    /// </summary>
    /// <param name="spellIds">The spell ids (a repeat is allowed).</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<SpellLearnResult> LearnSpells(IReadOnlyList<uint> spellIds, ActionSource source)
    {
        var ids = spellIds?.ToArray();
        return Request(
            ActionKind.ToolSpell,
            source,
            id => new SpellLearnRequest(ids ?? throw new ArgumentException("no spell ids"), id),
            string.Create(CultureInfo.InvariantCulture, $"Lifestone: asking the server to teach {ids?.Length ?? 0} spell{(ids?.Length == 1 ? string.Empty : "s")}..."),
            silent: false,
            ToolResultReader.SpellLearn,
            observe: null,
            "ToolSpell ids=" + ActionText.Count(ids?.Length ?? 0),
            verdict: UnknownSpellVerdict);
    }

    /// <summary>
    /// Creates an item in the character's pack (<c>@lifestone item &lt;wcid&gt; [&lt;stack&gt;]</c>), as ACE's <c>@ci</c> does. A stack above the item's maximum is clamped to
    /// it and a stack asked for an item that does not stack is ignored (the result says <see cref="ItemResult.Clamped"/>, never an error). The errors
    /// are <c>unknown-wcid</c>, <c>not-creatable</c> and <c>no-room</c> (<see cref="ItemOutcomes.Of"/> names them). A stack outside 1 to 65,535 is
    /// <see cref="ActionOutcomeKind.Refused"/> at once and never sent.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="stack">The stack size, or null for the item's default.</param>
    /// <param name="source">Who asked.</param>
    /// <returns>The request's handle.</returns>
    public ToolHandle<ItemResult> SpawnItem(uint wcid, int? stack, ActionSource source) =>
        Request(
            ActionKind.ToolItem,
            source,
            id => new ItemRequest(wcid, stack, id),
            string.Create(CultureInfo.InvariantCulture, $"Lifestone: asking the server for item {wcid}{(stack is { } n ? $" x {n:N0}" : string.Empty)}..."),
            silent: false,
            ToolResultReader.Item,
            observe: null,
            "ToolItem wcid=" + ActionText.Count(wcid) + (stack is { } count ? " stack=" + ActionText.Count(count) : string.Empty));

    /// <summary>The gear spawn still under way, if any: a second <see cref="SpawnGear"/> returns it instead of counting an inventory that its grants have not finished changing.</summary>
    private GearSpawn? _gear;

    /// <summary>
    /// Asks the server for the kit <paramref name="kit"/> over the <c>item</c> verb (<see cref="SpawnItem"/>), idempotently: it counts what the inventory read model already holds (the pack, the side packs, what is in
    /// them and what is equipped, by weenie class id, stacks summed) and requests only what is missing: a component's missing count (topped up to the kit's stack), and the wand and each armour piece only when none is held.
    /// A kit already held makes no request. The requests queue on the tools lane (2 a second, so a whole kit takes about half a minute), each ending as <see cref="SpawnItem"/>'s do; the result names every request and every
    /// refusal (<see cref="ItemOutcome.UnknownWcid"/>, <see cref="ItemOutcome.NotCreatable"/>, <see cref="ItemOutcome.NoRoom"/>, anything else as <see cref="ItemOutcome.Failed"/>), each refusal is logged as an ERROR when it is known, and
    /// <see cref="GearSpawn.Succeeded"/> is false when any request was refused. Nothing is requested, and <see cref="GearSpawn.NotStarted"/> says why, when the inventory cannot be counted yet (no description, or a pack entry whose world
    /// object has not arrived: the wire order puts a grant's 0022 before its CreateObject, so wait for <see cref="GearSpawn.Arrived"/> before asking again). While an earlier spawn is not done (<see cref="GearSpawn.IsDone"/>) it is returned instead.
    /// <para><b>A top-up is merged.</b> The mod's <c>item</c> verb makes every grant its own pack item (ACE's <c>TryAddToInventory</c> never merges), so once every grant has arrived the spawn merges each component line into its largest
    /// held stack with <see cref="IActionApi.MergeStack"/> (GameAction 0x0054), and <see cref="GearSpawn.IsDone"/> waits for it: a top-up leaves one stack per line, and a rerun never grows the pack. The session's tick drives it
    /// (<see cref="Update"/>).</para>
    /// <para><b>The slot guard refuses before the server could</b> (<see cref="GearSpawn.GuardRefusal"/>, logged as an ERROR, nothing requested): the main pack's items plus the new grants, which each take a slot until they are merged,
    /// would pass <see cref="GearKit.PackCapacity"/>. A held focus item is not a reason to refuse: the kit carries the foci formula's components (<see cref="GearKit.FociFormulaCounts"/>); an Info line names the focus and its school.</para>
    /// </summary>
    /// <param name="kit">The kit (<see cref="GearKit.Build"/>).</param>
    /// <param name="source">Who asked.</param>
    /// <returns>What was requested; poll <see cref="GearSpawn.IsDone"/> and read <see cref="GearSpawn.Succeeded"/>.</returns>
    public GearSpawn SpawnGear(GearKit kit, ActionSource source)
    {
        ArgumentNullException.ThrowIfNull(kit);
        _api.CheckThread("PlayerTools.SpawnGear");
        if (_gear is { IsDone: false } running)
        {
            return running;
        }

        var state = _api.State;
        if (!state.Self.Inventory.IsKnown)
        {
            return _gear = new GearSpawn(kit, state, [], 0, "inventory-unknown: no PlayerDescription has told the pack yet");
        }

        var held = GearInventory.CountAll(state, out var unsettled);
        if (unsettled > 0)
        {
            return _gear = new GearSpawn(kit, state, [], 0, "inventory-unsettled: " + ActionText.Count(unsettled) + " held items have no world object yet");
        }

        foreach (var focus in GearKit.FociWcids)
        {
            if (held.ContainsKey(focus))
            {
                _api.Log.Info(SessionLog.Category, "SpawnGear: the character holds " + GearKit.FociName(focus) + " (weenie " + ActionText.Count(focus) + "): ACE casts that school with the foci formula, whose components the kit carries");
            }
        }

        var wanted = new List<(GearItem Item, int Missing)>();
        var alreadyHeld = 0;
        foreach (var item in kit.Items)
        {
            var have = held.GetValueOrDefault(item.Wcid);
            var missing = item.Kind == GearKind.Component ? item.Stack - have : have > 0 ? 0 : 1;
            if (missing <= 0)
            {
                alreadyHeld++;
                continue;
            }

            wanted.Add((item, missing));
        }

        // Each grant is a new pack item until it is merged, so the pack must hold them all at once.
        var used = state.Self.Inventory.Pack.Count;
        if (used + wanted.Count > GearKit.PackCapacity)
        {
            return Refuse(
                kit,
                state,
                "slot-guard: the main pack holds " + ActionText.Count(used) + " of " + ActionText.Count(GearKit.PackCapacity) + " items and the kit needs " + ActionText.Count(wanted.Count)
                    + " new ones (each grant takes a slot until it is merged), so the server would refuse a grant as no-room");
        }

        var grants = new List<GearGrant>();
        foreach (var (item, missing) in wanted)
        {
            int? stack = item.Kind == GearKind.Component ? missing : null;
            var handle = SpawnItem(item.Wcid, stack, source);
            var grant = new GearGrant(item, stack, handle);
            grants.Add(grant);
            if (handle.IsCompleted)
            {
                LogGrant(grant);
            }
            else
            {
                handle.Completed += _ => LogGrant(grant);
            }
        }

        return _gear = new GearSpawn(kit, state, grants, alreadyHeld, null, null, _api, source);
    }

    private GearSpawn Refuse(GearKit kit, GameState state, string guard)
    {
        _api.Log.Error(SessionLog.Category, "SpawnGear: refused before asking the server: " + guard);
        return _gear = new GearSpawn(kit, state, [], 0, null, guard);
    }

    /// <summary>Called by the session's tick (<c>ActionApi.Update</c>): sends the next merge of the gear spawn under way, if one is due. Allocates nothing when there is none.</summary>
    internal void Update()
    {
        if (_gear is { MergePending: true } gear)
        {
            gear.Pump();
        }
    }

    private void LogGrant(GearGrant grant)
    {
        if (grant.Refusal is { } refusal)
        {
            _api.Log.Error(SessionLog.Category, "SpawnGear: the server refused a kit item: " + refusal);
        }
    }

    private static string? UnknownSpellVerdict(SpellLearnResult result) =>
        result.Ok
            ? null
            : UnknownSpellCode + ": not in the spell table: " + string.Join(", ", result.UnknownIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));

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
        _gear = null;
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
        bool availabilityNow = true,
        Func<T, string?>? verdict = null)
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

        var tracker = new ToolTracker<T>(handle, request ?? new StatusRequest(requestId), text, chatLine, silent, read, observe, verdict);
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
