using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The session's read model (A0's GameState, automation-vision.md §2): what the server has confirmed, organised for the
/// screens, the world layers and automation (R3: conditions read server-confirmed state only). Only the session's
/// reducer changes it, inside <c>GameSession.Tick</c>, and every change is followed by an event on
/// <see cref="IGameSessionView.Events"/>. Built by steps ST7 (login phase) and ST8 (world phase).
/// </summary>
/// <remarks>Game thread only. The objects it hands out are the reducer's own: read them, never keep a reference across
/// a reconnect (<c>GameSession.Reconnect</c> resets the state).</remarks>
public sealed class GameState
{
    /// <summary>An empty state, before login. Only the session creates one.</summary>
    internal GameState()
    {
        Containers = new ContainerViews(Self.Inventory, Objects);
    }

    /// <summary>The session's phase (the network session's phases; an offline session reports the phase of its stand-in data).</summary>
    public SessionPhase Phase { get; internal set; } = SessionPhase.Idle;

    /// <summary>Why the session failed, once <see cref="Phase"/> is <see cref="SessionPhase.Failed"/>; else null.</summary>
    public SessionFailure? Failure { get; internal set; }

    /// <summary>
    /// True when the session ended in failure (<see cref="SessionPhase.Failed"/>: a timeout, a boot, a data mismatch, a lost server). The retail
    /// screens name that in their own terms, a lost connection (<c>gmUIFlow::RecvNotice_ServerDied</c>), so they need not name the network
    /// layer's phases; a clean close is not one.
    /// </summary>
    public bool ConnectionLost => Phase == SessionPhase.Failed;

    /// <summary>Why the session failed, as the network layer words it (for the log; never a secret), or null.</summary>
    public string? FailureMessage => Failure?.Message;

    /// <summary>True for an offline session fed by stand-in data (<c>GameSession.Offline</c>): every server action is refused.</summary>
    public bool IsOffline { get; internal set; }

    /// <summary>The DDD data check's progress, for the retail data-patch screen.</summary>
    public DataCheckState DataCheck { get; internal set; }

    /// <summary>The world's name from F7E1 ServerName, or null before it.</summary>
    public string? WorldName { get; internal set; }

    /// <summary>The account's characters, in server order.</summary>
    public CharacterListState Characters { get; } = new();

    /// <summary>The player's own state.</summary>
    public SelfState Self { get; } = new();

    /// <summary>Every object the server created and has not deleted, the player's own included.</summary>
    public WorldObjectTable Objects { get; } = new();

    /// <summary>The containers the player sees: the viewed ground container (a corpse or chest, at most one) and the side packs' contents (M4 2.1 s1).</summary>
    public ContainerViews Containers { get; }

    /// <summary>The chat lines received and the client's own notices.</summary>
    public ChatLog Chat { get; } = new();

    /// <summary>The server's clock.</summary>
    public ServerTimeState Time { get; } = new();

    /// <summary>Rises by one with every change the reducer applies: a cheap "did anything change" check.</summary>
    public long Version { get; internal set; }

    /// <summary>The latest appraisal of each object the player appraised (GameEvent 0x00C9; M4 3.2 s2), bounded by <see cref="AppraisalCache.Capacity"/>.</summary>
    public AppraisalCache Appraisals { get; } = new();

    /// <summary>The vendor the player has open (the last GameEvent 0x0062 ApproachVendor), or null (M4 3.2 s1). Closed by the rule in <see cref="VendorClosed"/>.</summary>
    public VendorStock? Vendor { get; internal set; }

    private readonly Dictionary<uint, uint> _vendorMasks = [];

    /// <summary>
    /// The item-type mask of the last 0062 read from a vendor of weenie class <paramref name="weenieClassId"/> this session, or null when none was (3.2-R16). Kept after the vendor closes, so the loot rule can read Renald the Elder's mask while another vendor is open or none is.
    /// </summary>
    /// <param name="weenieClassId">The vendor's weenie class id (Renald the Elder 718).</param>
    public uint? VendorMaskOf(uint weenieClassId) => _vendorMasks.TryGetValue(weenieClassId, out var mask) ? mask : null;

    /// <summary>Remembers the mask a vendor of <paramref name="weenieClassId"/> sent.</summary>
    internal void RememberVendorMask(uint weenieClassId, uint mask) => _vendorMasks[weenieClassId] = mask;

    private readonly List<PendingConfirmation> _confirmations = [];

    /// <summary>
    /// The server's open yes-or-no question (GameEvent 0x0274; M5 6 w2), the newest when more than one is open, or null. Closed by the server's
    /// GameEvent 0x0276, by the player's answer (<see cref="IActionApi.RespondToConfirmation"/>) or by the world exit; <see cref="ConfirmationRequested"/>
    /// and <see cref="ConfirmationClosed"/> follow each change.
    /// </summary>
    public PendingConfirmation? PendingConfirmation => _confirmations.Count == 0 ? null : _confirmations[^1];

    /// <summary>
    /// Every open question, oldest first. ACE keeps at most one per <see cref="Lifestone.Net.Messages.ConfirmationType"/> (its <c>ConfirmationManager</c> is
    /// a dictionary keyed by the type and refuses a second of the same type before sending it: @95ef426d
    /// <c>WorldObjects/Managers/ConfirmationManager.cs:16, 33-51</c>), so this holds at most one of each type.
    /// </summary>
    public IReadOnlyList<PendingConfirmation> PendingConfirmations => _confirmations;

    /// <summary>The reducer's list (game thread only).</summary>
    internal List<PendingConfirmation> MutableConfirmations => _confirmations;

    private readonly List<(uint Type, uint Context)> _closedAwaitingReply = [];

    /// <summary>
    /// The questions the server closed with its GameEvent 0x0276 that ACE still holds until the client answers (M5 6 fix, 6-R1): for the types in <see cref="ServerKeepsSlotAfterDone"/> ACE sends the 0x0276
    /// at its 30 s timeout and does not remove the question ("automatically trigger a response from client", <c>ConfirmationManager.cs:57-84</c> @95ef426d), so every later question of that type is refused
    /// ("confirmation in progress") until a 0x0275 for it arrives. Each is dropped when the answer is sent or at the world exit.
    /// </summary>
    public IReadOnlyList<(uint Type, uint Context)> ClosedAwaitingReply => _closedAwaitingReply;

    /// <summary>The reducer's list (game thread only).</summary>
    internal List<(uint Type, uint Context)> MutableClosedAwaitingReply => _closedAwaitingReply;

    /// <summary>
    /// True for the confirmation types whose server-side slot survives a 0x0276 until the client's 0x0275: AlterSkill (2), AlterAttribute (3), CraftInteraction (5), Augmentation (6) and Yes_No (7)
    /// (ACE <c>ConfirmationManager.EnqueueAbort</c>, @95ef426d <c>WorldObjects/Managers/ConfirmationManager.cs:57-84</c>); types 1 and 4 are removed by the timeout itself.
    /// </summary>
    public static bool ServerKeepsSlotAfterDone(uint type) => type is 2 or 3 or 5 or 6 or 7;
}

/// <summary>
/// One open server question (<see cref="GameState.PendingConfirmation"/>; GameEvent 0x0274 CharacterConfirmationRequest, retail
/// <c>CM_Character::DispatchUI_ConfirmationRequest</c>, 11.6096 0x006A3BB0).
/// </summary>
/// <param name="Type">The raw type word (ACE's <c>ConfirmationType</c>: 1 swear allegiance ... 7 yes or no).</param>
/// <param name="Context">The server's context word, sent back unchanged in the answer.</param>
/// <param name="Text">The question as the server sent it.</param>
/// <param name="Time">When it arrived (session-clock seconds).</param>
public sealed record PendingConfirmation(uint Type, uint Context, string Text, double Time)
{
    /// <summary>The type as ACE's enum (a value it does not name is cast as is).</summary>
    public Lifestone.Net.Messages.ConfirmationType Kind => (Lifestone.Net.Messages.ConfirmationType)Type;
}

/// <summary>Where the DDD data check is (<see cref="GameState.DataCheck"/>; docs/protocol.md §4.2).</summary>
public enum DataCheckState
{
    /// <summary>Not started: the handshake has not reached the data check.</summary>
    NotStarted,

    /// <summary>F7E5 DDD_Interrogation arrived and was answered with the client's iterations; waiting for the server.</summary>
    Checking,

    /// <summary>F7EA DDD_EndDDD: the dats match the server's.</summary>
    Complete,

    /// <summary>F7E7 DDD_BeginDDD: the dats do not match the server's, and the session fails (patching is off).</summary>
    Mismatch,
}
