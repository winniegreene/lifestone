using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>A worn piece that blocks the slot and comes off first (<see cref="WieldRequest.TakeOff"/>, <see cref="Wardrobe.TakeOff"/>).</summary>
/// <param name="Guid">The worn piece.</param>
/// <param name="Name">Its name, for the reason.</param>
public sealed record WieldBlocker(uint Guid, string Name)
{
    /// <summary>The container it goes to (the player's guid for the main pack, a side pack's), sent as a move to that pack; 0 (the default): an unwield, which the server puts in the main pack.</summary>
    public uint Into { get; init; }

    /// <summary>The seconds it may take to leave the worn set after the server's answer (0, the default: read at once).</summary>
    public double OffSeconds { get; init; }

    /// <summary>Who sends it (the automation by default; a scripted leg sends as the host).</summary>
    public ActionSource Source { get; init; } = ActionSource.Automation;
}

/// <summary>What a caller asks of <see cref="Wardrobe.PutOn"/>: one piece she holds, put on.</summary>
/// <param name="Guid">The piece.</param>
public sealed record WieldRequest(uint Guid)
{
    /// <summary>Its name, for the reason.</summary>
    public string Name { get; init; } = "the piece";

    /// <summary>The slot to send, as the caller states it (a judge's location, the held slot, a remembered one). Read when <see cref="Valid"/> is 0.</summary>
    public uint Location { get; init; }

    /// <summary>When not 0: the piece's valid locations, and the slot is chosen at the send by the one rule (<see cref="WieldSlotChoice.Choose"/>: the free side of a ring or a bracelet).</summary>
    public uint Valid { get; init; }

    /// <summary>Who sends it (the automation by default; a scripted leg sends as the host).</summary>
    public ActionSource Source { get; init; } = ActionSource.Automation;

    /// <summary>What blocks the slot: each comes off first, in order, checked; one that stays on ends the routine with <see cref="Wardrobe.Blocked"/> and nothing more is sent.</summary>
    public IReadOnlyList<WieldBlocker> TakeOff { get; init; } = [];

    /// <summary>The seconds the piece may take to show worn after the server's answer (0, the default: read at once).</summary>
    public double WornSeconds { get; init; }

    /// <summary>The caller's own reading of "worn" (a weenie class worn, a caster in the hands); null: this piece is in the worn set.</summary>
    public Func<bool>? Worn { get; init; }
}

/// <summary>
/// What <see cref="Wardrobe.PutOn"/> did; the caller makes it and reads it when the coroutine ends. <see cref="Refusal"/> is one token: the check before's (<see cref="Wardrobe.NotHeld"/>, <see cref="Wardrobe.WornAlready"/>),
/// <see cref="Wardrobe.Blocked"/> when a piece in the way stayed on, <see cref="Wardrobe.Refused"/> when the server did not confirm the wield and the piece is not worn, <see cref="Wardrobe.NotWorn"/> when it confirmed and
/// the piece is not worn; null when the piece is worn after.
/// </summary>
public sealed class WieldResult
{
    /// <summary>The refusal token, or null when the piece is worn after.</summary>
    public string? Refusal { get; internal set; }

    /// <summary>The reason in words, with its numbers.</summary>
    public string Why { get; internal set; } = "not judged";

    /// <summary>True once the wield was sent.</summary>
    public bool Sent => Handle is not null;

    /// <summary>The slot sent (0 until then).</summary>
    public uint Location { get; internal set; }

    /// <summary>The wield's handle, or null when nothing was sent.</summary>
    public ActionHandle? Handle { get; internal set; }

    /// <summary>True when the server confirmed the wield.</summary>
    public bool Confirmed => Handle?.Outcome is { Kind: ActionOutcomeKind.Confirmed };

    /// <summary>The check after: true when the piece is worn (the caller's reading when it gave one).</summary>
    public bool Worn { get; internal set; }

    /// <summary>The pieces in the way that came off, in order.</summary>
    public List<WieldBlocker> TakenOff { get; } = [];

    /// <summary>The wield's outcome in the caller's words (<paramref name="describe"/> of the handle), or <c>not sent: why</c> when the check before refused it.</summary>
    /// <param name="describe">The caller's phrase for a handle's outcome.</param>
    public string Outcome(Func<ActionHandle, string> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        return Handle is { } handle ? describe(handle) : "not sent: " + Why;
    }

    internal void Refuse(string token, string why)
    {
        Refusal = token;
        Why = why;
    }
}

/// <summary>What <see cref="Wardrobe.TakeOff"/> did with one piece.</summary>
public sealed class TakeOffResult
{
    /// <summary>The handle of the take-off (null until it is sent).</summary>
    public ActionHandle? Handle { get; internal set; }

    /// <summary>The check after: true when the piece is not in the worn set.</summary>
    public bool Off { get; internal set; }
}

/// <summary>
/// The caller's side of <see cref="Wardrobe.PutOn"/>: its waits (a leg's are held with the leg, <c>ScriptLeg.Until</c>) and the moments it says its own lines at. A caller whose lines all come after the routine reads the
/// result and needs no hook.
/// </summary>
public sealed class WieldHost
{
    private readonly Func<Func<bool>, double, IEnumerable<ActionHandle?>> _until;

    /// <summary>Creates the host.</summary>
    /// <param name="until">The caller's wait: until the condition holds or the seconds passed.</param>
    public WieldHost(Func<Func<bool>, double, IEnumerable<ActionHandle?>> until)
    {
        ArgumentNullException.ThrowIfNull(until);
        _until = until;
    }

    /// <summary>A host whose waits run on the session clock alone, for a caller that is no leg.</summary>
    /// <param name="session">The session.</param>
    public static WieldHost OnClock(IGameSessionView session) => new(VendorCounterHost.ClockUntil(session));

    /// <summary>A piece in the way was sent off and read after: the blocker, the take-off's handle, whether it is off.</summary>
    public Action<WieldBlocker, ActionHandle, bool>? TookOff { get; init; }

    /// <summary>The slot is chosen and the wield goes out next: the slot. A caller marks its chat here.</summary>
    public Action<uint>? Sending { get; init; }

    /// <summary>The server answered the wield, before any wait for the worn state: the caller's line for the answer.</summary>
    public Action<WieldResult>? Answered { get; init; }

    internal IEnumerable<ActionHandle?> Until(Func<bool> done, double max) => _until(done, max);
}

/// <content>
/// M6 item 16, slice E5e1: the one wield routine. Putting a piece on was written once a caller (ratchet <c>WieldSends</c>, 40 lines in 16 files). This is that sequence as one coroutine a caller yields from, in the vendor
/// counter's shape (<see cref="VendorCounter"/>); the caller keeps what is its own: the pick, its counters, its refusal names and its lines.
/// <para><b>Check, act, check (M5 D141).</b></para>
/// <list type="number">
/// <item>Before: she holds the piece and it is not worn. Else <see cref="NotHeld"/> or <see cref="WornAlready"/>, and nothing is sent.</item>
/// <item>What blocks the slot comes off first (<see cref="WieldRequest.TakeOff"/>), each by <see cref="TakeOff"/>: sent, waited for, read. ACE never takes a piece off by itself: a wield into a taken slot or covered part
/// is refused (<c>Player_Inventory.cs</c> 1800-1810 @95ef426d). One that stays on is <see cref="Blocked"/>.</item>
/// <item>The slot: the caller's, or chosen by the one rule from the piece's valid locations (<see cref="WieldSlotChoice.Choose"/>).</item>
/// <item>The act: one <see cref="IActionApi.Wield"/>, the action a player's double click sends. Never retried here.</item>
/// <item>After: the worn state is read (waited for when the caller says how long): the piece in the worn set, or the caller's reading. Not worn is <see cref="Refused"/> or <see cref="NotWorn"/>.</item>
/// </list>
/// A tick-driven caller that cannot yield takes the first half alone (<see cref="Send"/>) and reads the state itself.
/// </content>
public sealed partial class Wardrobe
{
    /// <summary>The refusal token of a piece she does not hold.</summary>
    public const string NotHeld = "not-held";

    /// <summary>The refusal token of a piece that is worn already.</summary>
    public const string WornAlready = "worn-already";

    /// <summary>The refusal token of a wield not sent because a piece in the way stayed on.</summary>
    public const string Blocked = "blocked";

    /// <summary>The refusal token of a wield the server did not confirm, the piece not worn after.</summary>
    public const string Refused = "refused";

    /// <summary>The refusal token of a wield the server confirmed, the piece not worn after.</summary>
    public const string NotWorn = "not-worn";

    /// <summary>Puts <paramref name="request"/>'s piece on (see the class): yields the handle to wait for, or null to wait a frame; <paramref name="result"/> holds the answer when it ends.</summary>
    /// <param name="session">The session.</param>
    /// <param name="request">The piece and how.</param>
    /// <param name="result">Filled as the routine goes.</param>
    /// <param name="host">The caller's waits and line moments; null: waits on the session clock, no hook.</param>
    public static IEnumerable<ActionHandle?> PutOn(IGameSessionView session, WieldRequest request, WieldResult result, WieldHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        return Run(session, request, result, host ?? WieldHost.OnClock(session));
    }

    private static IEnumerable<ActionHandle?> Run(IGameSessionView session, WieldRequest request, WieldResult result, WieldHost host)
    {
        // Check before.
        if (!CheckBefore(session, request, result))
        {
            yield break;
        }

        // What blocks the slot comes off first.
        foreach (var blocker in request.TakeOff)
        {
            var off = new TakeOffResult();
            foreach (var h in TakingOff(session, blocker, off, host)) { yield return h; }

            host.TookOff?.Invoke(blocker, off.Handle!, off.Off);
            if (!off.Off)
            {
                result.Refuse(Blocked, Text($"'{blocker.Name}' 0x{blocker.Guid:X8} is still worn after the take-off ({Describe(off.Handle!)}): the wield of '{request.Name}' 0x{request.Guid:X8} is not sent"));
                yield break;
            }

            result.TakenOff.Add(blocker);
        }

        // The slot, the act.
        var handle = Sending(session, request, result, host);
        yield return handle;
        host.Answered?.Invoke(result);

        // Check after.
        if (request.WornSeconds > 0)
        {
            foreach (var h in host.Until(() => IsWorn(session, request), request.WornSeconds)) { yield return h; }
        }

        ReadAfter(session, request, result);
    }

    /// <summary>
    /// The routine's first half for a tick-driven caller that cannot yield: the check before, the slot, the one wield sent. Returns the handle, or null when the check before refused it (<paramref name="result"/> says why
    /// and nothing was sent). The caller waits for the handle in its own step and reads the worn state itself, or with <see cref="ReadAfter"/>. A request with pieces to take off first is not for this half.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="request">The piece and how.</param>
    /// <param name="result">Filled with the slot and the handle, or the refusal.</param>
    public static ActionHandle? Send(IGameSessionView session, WieldRequest request, WieldResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (request.TakeOff.Count > 0)
        {
            throw new ArgumentException("a tick-driven send takes nothing off first: send the take-offs, then this", nameof(request));
        }

        return CheckBefore(session, request, result) ? Sending(session, request, result, null) : null;
    }

    /// <summary>The check after, for a caller of <see cref="Send"/>: reads the worn state now and names a refusal.</summary>
    /// <param name="session">The session.</param>
    /// <param name="request">The request sent.</param>
    /// <param name="result">The result <see cref="Send"/> filled.</param>
    public static void ReadAfter(IGameSessionView session, WieldRequest request, WieldResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        result.Worn = IsWorn(session, request);
        if (result.Worn)
        {
            result.Refusal = null;
            result.Why = Text($"'{request.Name}' 0x{request.Guid:X8} is worn at 0x{result.Location:X}");
            return;
        }

        var answer = result.Handle is { } handle ? Describe(handle) : "nothing sent";
        result.Refuse(result.Confirmed ? NotWorn : Refused, Text($"'{request.Name}' 0x{request.Guid:X8} is not worn after the wield at 0x{result.Location:X} ({answer})"));
    }

    /// <summary>
    /// Takes one worn piece off: act (a move to <see cref="WieldBlocker.Into"/>, or an unwield), check (it left the worn set, waited for up to <see cref="WieldBlocker.OffSeconds"/>). The check before is the caller's: it
    /// names a piece it read in the worn set. Yields the handle to wait for; <paramref name="result"/> holds the handle and whether it is off.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="blocker">The worn piece.</param>
    /// <param name="result">Filled with the handle and the check after.</param>
    /// <param name="host">The caller's waits; null: the session clock.</param>
    public static IEnumerable<ActionHandle?> TakeOff(IGameSessionView session, WieldBlocker blocker, TakeOffResult result, WieldHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(blocker);
        ArgumentNullException.ThrowIfNull(result);
        return TakingOff(session, blocker, result, host ?? WieldHost.OnClock(session));
    }

    private static IEnumerable<ActionHandle?> TakingOff(IGameSessionView session, WieldBlocker blocker, TakeOffResult result, WieldHost host)
    {
        var handle = SendTakeOff(session, blocker);
        result.Handle = handle;
        yield return handle;
        if (blocker.OffSeconds > 0)
        {
            foreach (var h in host.Until(() => !IsEquipped(session, blocker.Guid), blocker.OffSeconds)) { yield return h; }
        }

        result.Off = !IsEquipped(session, blocker.Guid);
    }

    /// <summary>The take-off's one send, for a tick-driven caller that cannot yield (it waits for the handle and reads the worn set itself): a move to <see cref="WieldBlocker.Into"/>, or an unwield.</summary>
    /// <param name="session">The session.</param>
    /// <param name="blocker">The worn piece.</param>
    public static ActionHandle SendTakeOff(IGameSessionView session, WieldBlocker blocker)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(blocker);
        return blocker.Into != 0
            ? session.Actions.MoveToPack(blocker.Guid, blocker.Into, 0, blocker.Source)
            : session.Actions.Unwield(blocker.Guid, blocker.Source);
    }

    private static bool CheckBefore(IGameSessionView session, WieldRequest request, WieldResult result)
    {
        var inventory = session.State.Self.Inventory;
        if (request.Guid == 0 || !inventory.Contains(request.Guid))
        {
            result.Refuse(NotHeld, Text($"'{request.Name}' 0x{request.Guid:X8} is not held: nothing sent"));
            return false;
        }

        if (inventory.Find(request.Guid).Kind == InventoryPlaceKind.Equipped)
        {
            result.Refuse(WornAlready, Text($"'{request.Name}' 0x{request.Guid:X8} is worn already: nothing sent"));
            return false;
        }

        return true;
    }

    private static ActionHandle Sending(IGameSessionView session, WieldRequest request, WieldResult result, WieldHost? host)
    {
        var location = request.Valid != 0 ? WieldSlotChoice.Choose(session.State.Self.Inventory, request.Valid) : request.Location;
        result.Location = location;
        host?.Sending?.Invoke(location);
        var handle = session.Actions.Wield(request.Guid, (EquipMask)location, request.Source);
        result.Handle = handle;
        return handle;
    }

    private static bool IsWorn(IGameSessionView session, WieldRequest request) => request.Worn?.Invoke() ?? IsEquipped(session, request.Guid);

    private static bool IsEquipped(IGameSessionView session, uint guid) => session.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;

    private static string Describe(ActionHandle handle) => Scenarios.Journey.ScriptLeg.Describe(handle);

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
