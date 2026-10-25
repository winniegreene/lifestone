using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The script machinery of a scripted leg (M4 P5b 3.4; moved from the loopback tests' <c>LiveLeg</c>, which now derives
/// from it): a step-by-step <see cref="IScenario"/> whose <see cref="Script"/> yields the handle to wait for (or null to
/// wait a frame), with the follower that walks the client's body when ACE moves it, the waits, the checks and the grants.
/// Every action goes through <see cref="IActionApi"/> (the session's <c>Actions</c> and <c>Tools</c>), never a raw send.
/// A failed check is recorded in <see cref="Problems"/> and logged, and the leg goes on; a thrown
/// <see cref="InvalidOperationException"/> or <see cref="NotSupportedException"/> ends it Failed. In a journey the leg ends
/// Failed in the world (the journey recovers); the loopback tests' <c>LiveLeg</c> logs off instead
/// (<see cref="LogOffOnFailure"/>).
/// </summary>
/// <param name="name">The leg's name (the log's <c>[live name]</c>, the scenario's <see cref="Name"/>).</param>
/// <param name="log">Where the leg's lines go.</param>
public abstract class ScriptLeg(string name, Action<string> log) : IStationRun
{
    private const double BailSeconds = 30;
    private readonly List<string> _problems = [];
    private IEnumerator<ActionHandle?>? _script;
    private ActionHandle? _wait;
    private double _bailSince;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string Stage { get; protected set; } = "start";

    /// <inheritdoc />
    public string? Failure { get; protected set; }

    /// <inheritdoc />
    public double? WhereMaxMetres => null;

    /// <inheritdoc />
    public long Unmatched => 0;

    /// <summary>What the leg's checks found wrong, one line each (empty when every check held).</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The items <see cref="GrantIfMissing"/> granted (the journey's <c>granted=</c>).</summary>
    public int Granted { get; private set; }

    /// <summary>True when the leg passed: it ended without a failure and every check held.</summary>
    public virtual bool Ok => Stage == "done" && Failure is null && _problems.Count == 0;

    /// <summary>The leg's verdict fields (a station overrides it with its leg's names); the base reports the problems and the grants.</summary>
    public virtual string VerdictFields() => $"problems={_problems.Count} granted={Granted}" + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <summary>When true, a leg that fails in the world logs off before it ends (the loopback tests' rule); false in a journey, which recovers in the world.</summary>
    protected virtual bool LogOffOnFailure => false;

    /// <summary>The session of the current step.</summary>
    protected IGameSessionView S { get; private set; } = null!;

    /// <summary>The time of the current step.</summary>
    protected double Now { get; private set; }

    /// <summary>The follower, or null; the leg that gives it to the hunt sets <see cref="FollowIsTheHunts"/> while the hunt ticks it.</summary>
    protected IHuntFollow? Follow { get; set; }

    /// <summary>True while a hunt the leg runs ticks <see cref="Follow"/> itself.</summary>
    protected bool FollowIsTheHunts { get; set; }

    /// <summary>Writes a line with the leg's name and the session clock.</summary>
    protected void Log(string text) => log($"[live {name} t={S?.Clock.Now ?? 0:0.0}] {text}");

    /// <summary>Records a failed check (logged, the leg goes on) and returns whether it held.</summary>
    protected bool Check(bool ok, string what)
    {
        if (!ok)
        {
            _problems.Add(what);
            Log("CHECK FAILED: " + what);
        }

        return ok;
    }

    /// <inheritdoc />
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        S = session;
        Now = now;
        switch (Stage)
        {
            case "done":
                return ScenarioStatus.Succeeded;
            case "failed":
                return ScenarioStatus.Failed;
            case "bailing":
                if (_wait is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || session.Clock.Now - _bailSince > BailSeconds)
                {
                    Stage = "failed";
                    return ScenarioStatus.Failed;
                }

                return ScenarioStatus.Running;
        }

        if (Follow is { } follow && !FollowIsTheHunts)
        {
            follow.Prepare(session, now);
            follow.Tick(session, now);
        }

        if (_wait is { IsCompleted: false })
        {
            return ScenarioStatus.Running;
        }

        try
        {
            _script ??= Script().GetEnumerator();
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return ScenarioStatus.Running;
            }

            Stage = "done";
            Follow?.Dispose();
            return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            Failure ??= Stage + ": " + e.Message;
            _problems.Add("the leg failed: " + Failure);
            Log("FAILED: " + Failure);
            if (LogOffOnFailure && session.State.Phase == SessionPhase.InWorld)
            {
                _bailSince = session.Clock.Now;
                _wait = session.Actions.LogOff(ActionSource.Host);
                Stage = "bailing";
                return ScenarioStatus.Running;
            }

            Follow?.Dispose();
            Stage = "failed";
            return ScenarioStatus.Failed;
        }
    }

    /// <summary>The leg's script: yield the handle to wait for, or null to wait a frame.</summary>
    protected abstract IEnumerable<ActionHandle?> Script();

    // ---------------------------------------------------------------------------------------- helpers a script uses

    /// <summary>Waits <paramref name="seconds"/> of session time.</summary>
    protected IEnumerable<ActionHandle?> Pause(double seconds)
    {
        var until = S.Clock.Now + seconds;
        while (S.Clock.Now < until)
        {
            yield return null;
        }
    }

    /// <summary>Waits until <paramref name="done"/> holds or <paramref name="max"/> seconds passed (the caller checks which).</summary>
    protected IEnumerable<ActionHandle?> Until(Func<bool> done, double max)
    {
        var until = S.Clock.Now + max;
        while (!done() && S.Clock.Now < until)
        {
            yield return null;
        }
    }

    /// <summary>A handle's outcome in one phrase.</summary>
    protected static string Describe(ActionHandle handle) =>
        handle.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : $" (code 0x{o.Code:X})") + (o.Detail is null ? string.Empty : ": " + o.Detail) : "not settled";

    /// <summary>The handle settled as Confirmed; a failed check names <paramref name="what"/>. A 0x001D (busy) is also its own check: the Action API's lane rule says no move is sent into a busy chain.</summary>
    protected bool Confirmed(ActionHandle handle, string what)
    {
        Log(what + ": " + Describe(handle));
        var notBusy = Check(handle.Outcome is not { Code: 0x001D }, what + ": the server refused the move as busy (0x001D): the lane rule was broken");
        return Check(handle.Outcome is { Kind: ActionOutcomeKind.Confirmed }, what + ": " + Describe(handle)) && notBusy;
    }

    /// <summary>A destination for a place or a travel request.</summary>
    protected static TeleportDestination Destination(string label, uint cell, Vector3 at) =>
        new(label, [], new WorldLocation(label, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "live");

    /// <summary>The nearest object of that exact name within <paramref name="metres"/> of the player, or 0.</summary>
    protected uint Nearest(string objectName, double metres)
    {
        if (S.State.Self.Position is not { } here || !S.State.Objects.NearestNamed(objectName, in here, S.State.Self.Guid, out var guid))
        {
            return 0;
        }

        var found = S.State.Objects.TryGet(guid, out var o) && o.Position is { } p && p.CellId >> 16 == here.CellId >> 16 ? Vector3.Distance(p.Origin, here.Origin) : double.MaxValue;
        Log($"nearest '{objectName}' {guid:X8} is {found:0.0} m away");
        return found <= metres ? guid : 0;
    }

    /// <summary>Waits for the item's world object and its place in the inventory model (ACE sends the 0022 before the CreateObject).</summary>
    protected IEnumerable<ActionHandle?> WaitHeld(uint guid, string what)
    {
        foreach (var h in Until(() => S.State.Self.Inventory.Contains(guid) && S.State.Objects.TryGet(guid, out _), 10))
        {
            yield return h;
        }

        Log($"{what} {guid:X8}: in the model {S.State.Self.Inventory.Contains(guid)}, world object {S.State.Objects.TryGet(guid, out _)}");
    }

    /// <summary>
    /// M4 P4 sfix2: a consumable the test needs but an earlier test may have used up (the hunts drink the Archer's potions, the kits lose their uses). When <paramref name="find"/> returns 0 the mod's <c>item</c> verb
    /// grants one <paramref name="wcid"/> (an item, never a creature, D23) and the leg waits until it is in the pack; the Log says so and <see cref="Granted"/> counts it. Returns the guid, or 0 when the grant failed (a failed check).
    /// </summary>
    protected IEnumerable<ActionHandle?> GrantIfMissing(Func<uint> find, uint wcid, string what, Action<uint> found, int? stack = null)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(found);
        var held = find();
        if (held != 0)
        {
            found(held);
            yield break;
        }

        Log($"the pack holds no {what} (wcid {wcid}; an earlier test used it up): granting one with the item verb");
        var spawn = S.Tools.SpawnItem(wcid, stack, ActionSource.Host);
        yield return spawn;
        if (!Check(spawn.Result is not null, $"the {what} grant (wcid {wcid}) failed: " + Describe(spawn)))
        {
            yield break;
        }

        Granted++;
        foreach (var h in WaitHeld(spawn.Result!.Guid, what)) { yield return h; }

        foreach (var h in Pause(1)) { yield return h; } // the grant's own messages settle before the next action
        found(find());
    }

    /// <summary>
    /// A <c>place loc</c> (or poi), the end of the own teleport and a 3 s settle; a refusal is a failed check. The end of the teleport is what ACE waits for before it takes another place, a move or a give
    /// (2.1-R2: its teleporting flag clears when it handles the post-teleport LoginComplete, which portal space sends 5.6 s after the place): the travel reaches TeleportArrived, then Present, and the LoginComplete is sent.
    /// </summary>
    protected IEnumerable<ActionHandle?> PlaceAndSettle(TeleportDestination destination, string what)
    {
        var placeLogTotal = S.ActionLog.Total;
        var place = S.Tools.Place(destination, ActionSource.Host);
        foreach (var h in Until(() => place.IsCompleted, 30))
        {
            yield return h;
        }

        Log($"place {what}: {Describe(place)}");
        Check(place.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"place {what}: {Describe(place)}");
        foreach (var h in Until(() => S.State.Self.Travel is SelfTravel.TeleportArrived or SelfTravel.Arriving or SelfTravel.Materialising, 30))
        {
            yield return h;
        }

        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(placeLogTotal), 30))
        {
            yield return h;
        }

        Log($"place {what}: the teleport ended (travel {S.State.Self.Travel}, the post-teleport LoginComplete confirmed)");
        foreach (var h in Pause(3))
        {
            yield return h;
        }
    }

    /// <summary>True when the action log holds a Confirmed LoginComplete added after the log's <paramref name="total"/> entries.</summary>
    protected bool LoginCompleteConfirmedSince(long total)
    {
        var log = S.ActionLog;
        var fresh = (int)Math.Min(log.Count, log.Total - total);
        for (var i = log.Count - fresh; i < log.Count; i++)
        {
            if (log[i].Kind == ActionKind.LoginComplete && log[i].Outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The stack size the model knows for a held or world item, or -1.</summary>
    protected int StackOf(uint guid)
    {
        if (S.State.Self.Inventory.TryGetStack(guid, out var size, out _))
        {
            return (int)size;
        }

        return S.State.Objects.TryGet(guid, out var o) ? (int)(o.Weenie.StackSize ?? 1u) : -1;
    }

    /// <summary>The guids of the held items (pack, side packs, worn) of that weenie class.</summary>
    protected HashSet<uint> HeldOf(uint wcid)
    {
        var set = new HashSet<uint>();
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && S.State.Self.Inventory.Contains(o.Guid))
            {
                set.Add(o.Guid);
            }
        }

        return set;
    }

    /// <summary>The guids of the world objects of that weenie class the player does not hold and that no container holds.</summary>
    protected HashSet<uint> GroundOf(uint wcid)
    {
        var set = new HashSet<uint>();
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && !S.State.Self.Inventory.Contains(o.Guid) && o.ContainerId is null or 0)
            {
                set.Add(o.Guid);
            }
        }

        return set;
    }

    /// <summary>True when the session's entry already put the character in the world, arrived with its qualities known: a leg then skips its own login.</summary>
    protected bool AlreadyInWorld => S.State.Phase == SessionPhase.InWorld && S.State.Self.Guid != 0 && S.State.Self.Travel == SelfTravel.Present && S.State.Self.Qualities.IsKnown;

    /// <summary>Throws (the leg ends Failed) when <paramref name="ok"/> is false.</summary>
    protected static void Require(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException(what);
        }
    }
}
