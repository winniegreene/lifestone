using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>Where a <see cref="CorpseLooter"/> is: still working, or finished (see <see cref="CorpseLooter.StoppedBy"/>).</summary>
public enum LootStatus
{
    /// <summary>A move is in flight, or entries remain to be judged or waited for.</summary>
    Running,

    /// <summary>The loop ended: <see cref="CorpseLooter.StoppedBy"/> says why.</summary>
    Done,
}

/// <summary>
/// The shared loot loop (M4 2.2 s0): the hunt's kill (this slice) and the death loop's own corpse (s1) take the contents of a viewed container, one <see cref="IActionApi.PickUp"/> at a time, in the order of
/// <see cref="ContainerView.Entries"/>. A rule (<see cref="LootRules"/>, or <see cref="LootRules.TakeAll"/>) decides each resolved entry; an entry whose object has not arrived is left until it does.
/// </summary>
/// <remarks>
/// <para><b>One move at a time, no waits of its own.</b> A new move is sent only when the previous handle has an <see cref="ActionHandle.Outcome"/>; the Inventory lane holds each move until the previous one's chain ends
/// (the return stance and the pickup motion, 1.35 s from a corpse: <c>ItemMoveTracker</c>), so the loop adds no sleep. The live capture (M4 2.2 cap, 2026-10-21) had four moves from one corpse in a row all Confirmed, each 0022
/// 1.35 s after its send and the next send 1.46 to 1.48 s after the 0022: about 2.8 s per item.</para>
/// <para><b>Unresolved entries.</b> ACE sends the 0196 before the F745 of every item it names, and the contents may arrive before or after the open's UseDone (the capture: 49129 before 49136): an unresolved entry is
/// skipped for now and judged when its object arrives. The entries' F745s are in within 3 s of the open (<see cref="UnresolvedCapSeconds"/>, counted from the first <c>Step</c>); an entry still unresolved then is counted
/// <see cref="LootDecision.NotResolved"/> and left.</para>
/// <para><b>Outcomes.</b> <see cref="Looted"/> counts Confirmed moves; <see cref="Refused"/> a <see cref="ActionOutcomeKind.ServerError"/> or <see cref="ActionOutcomeKind.Refused"/> or any other ending that is not a confirmation
/// or a timeout; <see cref="TimedOut"/> the 25 s timeouts. A refusal that names the burden (WeenieError 0x002A, "You are too encumbered to carry that!", or the same text as a transient string) ends the loop
/// (<see cref="StoppedBy"/> is <c>burden</c>): every later item would be refused the same. Any other refusal is counted and the loop goes on with the next entry. ACE never joins a stack at pick-up
/// (the capture's arrows and coins; <c>Container.cs</c> 499-581 <c>TryAddToInventory</c> only adds the object, <c>Player_Inventory.cs</c> 1268ff <c>DoHandleActionPutItemInContainer</c> never looks for a stack), so a looted stack is its own
/// stack with a 0022 and would take a pack slot. <b>Merging (M5 7, closes <c>TODO(m4-loot-merge-stacks)</c>):</b> once a pick-up of a stackable is Confirmed, the loop sends <see cref="IActionApi.MergeStack"/> (GameAction 0x0054,
/// <c>HandleActionStackableMerge</c> <c>Player_Inventory.cs</c> 2849ff) from the new stack into the fullest other held stack of its weenie class that has room (pack or side pack, not an equipped one), for the smaller of the
/// new stack and the room, and waits for its outcome before the next pick-up. A partial merge looks for the next stack with room. A refused or timed-out merge is logged, counted in <see cref="MergesRefused"/>, not tried again for that
/// stack, and the loop goes on. <see cref="StacksBesideHeld"/> still counts the looted stacks that had a held stack of their class. The merge can be turned off with the constructor's <c>mergeStacks</c>.</para>
/// <para><b>Allocation.</b> A <c>Step</c> allocates nothing but log lines, and those only on an event (a move sent or settled, an entry skipped, the end). The handled set grows with the entries.</para>
/// <para><b>For the death loop (s1):</b> <c>new CorpseLooter(log, category, corpseGuid, LootRules.TakeAll)</c>, then <c>Step(session, session.Clock.Now)</c> each frame until <see cref="LootStatus.Done"/>.</para>
/// </remarks>
public sealed class CorpseLooter
{
    /// <summary>How long after the first step an entry's object may stay unresolved before it is left (the capture: the F745s were in within 3 s of the open).</summary>
    public const double UnresolvedCapSeconds = 3.0;

    /// <summary>ACE's WeenieError "You are too encumbered to carry that!" (0x002A), the refusal that ends the loop.</summary>
    public const uint TooEncumberedCode = 0x002A;

    /// <summary>
    /// ACE's text for a pick-up into a main pack with no free slot (<c>Player_Inventory.cs</c> 1320-1322, <c>Container.cs</c> 527 @95ef426d: ItemCapacity 102 in the main pack, WeenieError 0 with this text): <c>Unable to put X into container</c>.
    /// </summary>
    public const string PackFullText = "Unable to put";

    /// <summary>How many pack-full refusals in a row end the loop (<c>why=pack-full</c>): the next item would be refused the same, at about 2 s each (round 2 of the P5a gate lost 4 items to it).</summary>
    public const int PackFullRefusalsToStop = 2;

    /// <summary>
    /// How long an owner steps a draining loop (<see cref="IsDraining"/>) before it gives up on the pick-up in flight: a little over the Action API's own 25 s pick-up timeout (<c>ActionPolicy.PickUpSeconds</c>), after which the handle
    /// ends <see cref="ActionOutcomeKind.TimedOut"/> by itself, so a drain always ends.
    /// </summary>
    public const double DrainSeconds = 26.0;

    private static readonly int ReasonSlots = Enum.GetValues<LootDecision>().Length;

    private readonly ILogSink _log;
    private readonly string _category;
    private readonly uint _container;
    private readonly LootRules _rules;
    private readonly double _unresolvedCap;
    private readonly HashSet<uint> _handled = new(32);
    private readonly int[] _skipped = new int[ReasonSlots];
    private ActionHandle? _pending;
    private uint _pendingItem;
    private int _packFullRun;
    private int _takenBurden;
    private int _burdenAtSend;
    private double _startedAt = double.NaN;
    private readonly bool _mergeStacks;
    private readonly List<uint> _scratch = new(64);
    private readonly HashSet<uint> _mergeGaveUp = [];
    private readonly HashSet<uint> _mergedAway = [];
    private ActionHandle? _mergeHandle;
    private bool _mergeDue;
    private uint _mergeFrom;
    private uint _mergeTo;
    private int _mergeAmount;

    /// <summary>A loop over the container <paramref name="container"/>.</summary>
    /// <param name="log">Where the loop logs (one line per move, one per skip, one at the end).</param>
    /// <param name="category">The log category (the hunt's <c>hunt</c>).</param>
    /// <param name="container">The viewed container's object id (a corpse or a chest).</param>
    /// <param name="rules">The rule, or null for <see cref="LootRules.TakeAll"/>.</param>
    /// <param name="unresolvedCapSeconds">How long an unresolved entry is waited for.</param>
    /// <param name="mergeStacks">Whether a looted stackable is merged into a held stack of its class (the default; see the class remarks).</param>
    public CorpseLooter(ILogSink log, string category, uint container, LootRules? rules = null, double unresolvedCapSeconds = UnresolvedCapSeconds, bool mergeStacks = true)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentOutOfRangeException.ThrowIfNegative(unresolvedCapSeconds);
        _log = log;
        _category = category;
        _container = container;
        _rules = rules ?? LootRules.TakeAll;
        _unresolvedCap = unresolvedCapSeconds;
        _mergeStacks = mergeStacks;
    }

    /// <summary>The container being emptied.</summary>
    public uint Container => _container;

    /// <summary>The rule in force.</summary>
    public LootRules Rules => _rules;

    /// <summary>Whether the loop is still working.</summary>
    public bool IsDone { get; private set; }

    /// <summary>Why the loop ended: <c>end</c> (every entry judged), <c>burden</c> (the server's burden refusal) or <c>closed</c> (the container's view is gone); empty while running.</summary>
    public string StoppedBy { get; private set; } = string.Empty;

    /// <summary>The moves the server confirmed (a 0022 into the player's pack).</summary>
    public int Looted { get; private set; }

    /// <summary>The entries the rule left, summed over every reason.</summary>
    public int Skipped { get; private set; }

    /// <summary>The moves the server refused (or that were never sent), the burden refusal included.</summary>
    public int Refused { get; private set; }

    /// <summary>The corpse entries left unsent because the pack refused two pick-ups in a row (<c>why=pack-full</c>, <c>TODO(m4-hunt-pack-full)</c>); they are part of <see cref="Skipped"/> but of no <see cref="SkippedBy"/> reason.</summary>
    public int SkippedPackFull { get; private set; }

    /// <summary>The moves that got no answer within their timeout.</summary>
    public int TimedOut { get; private set; }

    /// <summary>The looted stackables that had a held stack of their own class when they landed (the merge into it is <see cref="StackMerges"/>).</summary>
    public int StacksBesideHeld { get; private set; }

    /// <summary>The weenie class id of the first looted stack that had a held stack of its class, or 0.</summary>
    public uint FirstStackBesideHeldWcid { get; private set; }

    /// <summary>The merges the server confirmed (a looted stack joined into a held stack of its class; a partial merge and the merge of its remainder count one each).</summary>
    public int StackMerges { get; private set; }

    /// <summary>The merges that were refused, never sent (the model said they cannot merge) or timed out; the stack stays where it landed.</summary>
    public int MergesRefused { get; private set; }

    /// <summary>True when the looted stack <paramref name="guid"/> was wholly merged into another and is gone from the pack (its items are held, in the other stack).</summary>
    public bool WasMergedAway(uint guid) => _mergedAway.Contains(guid);

    /// <summary>The entries left for <paramref name="reason"/>.</summary>
    public int SkippedBy(LootDecision reason) => _skipped[(int)reason];

    /// <summary>The counts as one line (no names): <c>looted=2 skipped=0 (value=0 type=0 burden=0 held=0 unresolved=0) refused=0 timed_out=0 stopped_by=end stack_merges=0 merges_refused=0</c>.</summary>
    public string Summary =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"looted={Looted} skipped={Skipped} (value={SkippedBy(LootDecision.Value)} type={SkippedBy(LootDecision.Type)} burden={SkippedBy(LootDecision.Burden)} held={SkippedBy(LootDecision.AlreadyHeld)} unresolved={SkippedBy(LootDecision.NotResolved)}) refused={Refused} timed_out={TimedOut} stopped_by={(StoppedBy.Length == 0 ? "running" : StoppedBy)} stack_merges={StackMerges} merges_refused={MergesRefused}{(Left == 0 ? string.Empty : $" left={Left}")}");

    /// <summary>One step of the loop on a session; <paramref name="now"/> is the session clock's seconds.</summary>
    public LootStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Step(session.State, session.Actions, now);
    }

    /// <summary>One step of the loop on a read model and an action API (what <see cref="Step(IGameSessionView, double)"/> passes).</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="actions">The Action API the moves go through.</param>
    /// <param name="now">The session clock's seconds.</param>
    public LootStatus Step(GameState state, IActionApi actions, double now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        if (IsDone)
        {
            // M4 2.2 fix R3: an abandoned loop with a pick-up in flight only settles it (the count); it sends nothing new.
            if (_pending is { Outcome: { } late })
            {
                Settle(state, late);
            }

            if (_mergeHandle is { Outcome: { } lateMerge })
            {
                SettleMerge(state, lateMerge);
            }

            return LootStatus.Done;
        }

        if (double.IsNaN(_startedAt))
        {
            _startedAt = now;
        }

        if (_mergeHandle is not null)
        {
            if (_mergeHandle.Outcome is not { } mergeOutcome)
            {
                return LootStatus.Running;
            }

            SettleMerge(state, mergeOutcome);
        }

        if (_pending is not null)
        {
            if (_pending.Outcome is not { } outcome)
            {
                return LootStatus.Running;
            }

            Settle(state, outcome);
            if (IsDone)
            {
                return LootStatus.Done;
            }
        }

        if (_mergeDue)
        {
            _mergeDue = false;
            _mergeHandle = actions.MergeStack(_mergeFrom, _mergeTo, _mergeAmount, ActionSource.Automation);
            return LootStatus.Running;
        }

        if (state.Containers.Viewed is not { } view || view.Guid != _container)
        {
            Finish("closed");
            return LootStatus.Done;
        }

        var context = LootContext.Of(state, _takenBurden);
        var waiting = 0;
        var entries = view.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (_handled.Contains(entry.Guid))
            {
                continue;
            }

            WorldObject? item = null;
            if (!entry.Resolved || !state.Objects.TryGet(entry.Guid, out item))
            {
                waiting++;
                continue;
            }

            var decision = _rules.Decide(item, in context);
            if (decision == LootDecision.Replaced)
            {
                Leave(entry.Guid, item);
                continue;
            }

            if (decision != LootDecision.Take)
            {
                Skip(entry.Guid, item, decision);
                continue;
            }

            _pendingItem = entry.Guid;
            _burdenAtSend = context.Burden;
            var into = _rules.DestinationOf(item, state); // M5 7 D121: a side pack of the item's job, or 0 for the main pack
            if (into != 0 && into != state.Self.Guid && !state.Self.Inventory.IsKnownContainer(into))
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: the side pack 0x{into:X8} of '{item.Name}' 0x{entry.Guid:X8} is not held: into the main pack"));
                into = 0;
            }

            _pending = actions.PickUp(entry.Guid, into == 0 ? state.Self.Guid : into, 0, ActionSource.Automation);
            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: taking '{item.Name}' 0x{entry.Guid:X8} (wcid {item.Weenie.WeenieClassId}, type 0x{item.ItemType:X}, value {item.Weenie.Value ?? 0}, burden {item.Weenie.Burden ?? 0}){(into == 0 || into == state.Self.Guid ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" into the side pack 0x{into:X8}"))}"));
            return LootStatus.Running;
        }

        if (waiting > 0 && now - _startedAt < _unresolvedCap)
        {
            return LootStatus.Running;
        }

        for (var i = 0; i < entries.Count && waiting > 0; i++)
        {
            var entry = entries[i];
            if (!_handled.Contains(entry.Guid))
            {
                Skip(entry.Guid, null, LootDecision.NotResolved);
            }
        }

        Finish("end");
        return LootStatus.Done;
    }

    /// <summary>
    /// Ends the loop from outside (the hunt's retreat rule or its time, the death loop's abort): nothing more is sent. A move in flight is kept and settled on later <see cref="Step(GameState, IActionApi, double)"/> calls (a draining
    /// loop, <see cref="IsDraining"/>): its outcome is counted in <see cref="Looted"/> (or <see cref="Refused"/> or <see cref="TimedOut"/>) as any other, so the owner reads the count once the drain is over (M4 2.2 fix R3).
    /// </summary>
    /// <param name="why">Why (the <see cref="StoppedBy"/> value).</param>
    public void Abandon(string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        if (!IsDone)
        {
            Finish(why);
            if (_pending is not null)
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: the pick-up of 0x{_pendingItem:X8} is in flight at the abandon; nothing new is sent and its outcome is counted when it settles (draining)"));
            }
        }
    }

    /// <summary>True when the loop was abandoned (or ended) with a pick-up still in flight: the owner steps it until this is false (or <see cref="DrainSeconds"/> pass) and then reads <see cref="Looted"/>.</summary>
    public bool IsDraining => IsDone && (_pending is not null || _mergeHandle is not null);

    private void Settle(GameState state, ActionOutcome outcome)
    {
        var late = IsDone;
        var guid = _pendingItem;
        _pending = null;
        _handled.Add(guid);
        var known = state.Objects.TryGet(guid, out var item);
        var name = known ? item!.Name : "?";
        switch (outcome.Kind)
        {
            case ActionOutcomeKind.Confirmed:
                Looted++;
                _packFullRun = 0;
                if (known)
                {
                    _takenBurden = _burdenAtSend + (item!.Weenie.Burden ?? 0); // the burden the move started from plus the item's: the server's property may or may not show it yet
                    NoteStack(state, item);
                    if (!late)
                    {
                        PlanMerge(state, guid);
                    }
                }

                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: took '{name}' 0x{guid:X8} ({Looted} so far{(late ? "; it settled after the loop was abandoned" : string.Empty)})"));
                break;
            case ActionOutcomeKind.TimedOut:
                TimedOut++;
                _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"loot: the pick-up of '{name}' 0x{guid:X8} got no answer within its timeout; counted{(late ? " (after the loop was abandoned)" : ", going on")}"));
                break;
            default:
                Refused++;
                var burden = !late && outcome.Kind == ActionOutcomeKind.ServerError && (outcome.Code == TooEncumberedCode || (outcome.Detail?.Contains("encumbered", StringComparison.OrdinalIgnoreCase) ?? false));
                _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"loot: the pick-up of '{name}' 0x{guid:X8} ended {outcome.Kind} ({outcome.Detail ?? "no detail"}); counted{(late ? " (after the loop was abandoned)" : burden ? ", the burden limit: stopping" : ", going on")}"));
                _packFullRun = outcome.Kind == ActionOutcomeKind.ServerError && (outcome.Detail?.Contains(PackFullText, StringComparison.Ordinal) ?? false) ? _packFullRun + 1 : 0;
                if (burden)
                {
                    Finish("burden");
                }
                else if (!late && _packFullRun >= PackFullRefusalsToStop)
                {
                    StopPackFull(state);
                }

                break;
        }
    }

    /// <summary><c>TODO(m4-loot-merge-stacks)</c>: counts a looted stackable that has another stack of its class in the player's holdings.</summary>
    private void NoteStack(GameState state, WorldObject item)
    {
        if (item.Weenie.MaxStackSize is not > 1)
        {
            return;
        }

        var wcid = item.Weenie.WeenieClassId;
        foreach (var other in state.Objects.All)
        {
            if (other.Guid != item.Guid && other.Weenie.WeenieClassId == wcid && state.Self.Inventory.Find(other.Guid).Held)
            {
                StacksBesideHeld++;
                if (FirstStackBesideHeldWcid == 0)
                {
                    FirstStackBesideHeldWcid = wcid;
                }

                return;
            }
        }
    }

    /// <summary>
    /// Plans the merge of the looted stack <paramref name="guid"/> into the fullest other held stack of its weenie class that has room (not an equipped one): <c>_mergeDue</c> is set and the next <c>Step</c> sends it. Nothing is planned for
    /// an item that does not stack, a stack that is gone, one whose merge was refused before, or when no other stack has room.
    /// </summary>
    private void PlanMerge(GameState state, uint guid)
    {
        if (!_mergeStacks || IsDone || _mergeGaveUp.Contains(guid) || !state.Objects.TryGet(guid, out var item) || item.Weenie.MaxStackSize is not > 1)
        {
            return;
        }

        if (!StackMerge.TryPlan(state, guid, _scratch, out var best, out var amount, out var bestSize, out var bestRoom))
        {
            return;
        }

        _mergeFrom = guid;
        _mergeTo = best;
        _mergeAmount = amount;
        _mergeDue = true;
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: merging '{item.Name}' 0x{guid:X8} (x{GearInventory.StackOf(state, item)}) into the held stack 0x{best:X8} (x{bestSize}, room {bestRoom}): x{amount}"));
    }

    private void SettleMerge(GameState state, ActionOutcome outcome)
    {
        var from = _mergeFrom;
        var late = IsDone;
        _mergeHandle = null;
        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            StackMerges++;
            var gone = !state.Self.Inventory.Find(from).Held;
            if (gone)
            {
                _mergedAway.Add(from);
            }

            _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: merged 0x{from:X8} into 0x{_mergeTo:X8} ({StackMerges} so far; {outcome.Detail}{(gone ? string.Empty : "; part of it is left, looking for another stack")}{(late ? "; it settled after the loop was abandoned" : string.Empty)})"));
            if (!gone)
            {
                PlanMerge(state, from);
            }

            return;
        }

        MergesRefused++;
        _mergeGaveUp.Add(from);
        _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"loot: the merge of 0x{from:X8} into 0x{_mergeTo:X8} ended {outcome.Kind} ({outcome.Detail ?? "no detail"}); counted, the stack stays where it landed, going on"));
    }

    /// <summary>M6 7 AH: the entries left on the corpse because the replacement the handler bought already fills their slot (<c>take-back: left '&lt;name&gt;': replaced at the vendor</c>); not skipped, not refused, not lost.</summary>
    public int Left => _left.Count;

    /// <summary>M6 7 AH: the guids of the entries counted in <see cref="Left"/>.</summary>
    public IReadOnlyCollection<uint> LeftGuids => _left;

    private readonly List<uint> _left = [];

    private void Leave(uint guid, WorldObject item)
    {
        _handled.Add(guid);
        _left.Add(guid);
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"take-back: left '{item.Name}' 0x{guid:X8}: replaced at the vendor"));
    }

    private void Skip(uint guid, WorldObject? item, LootDecision reason)
    {
        _handled.Add(guid);
        _skipped[(int)reason]++;
        Skipped++;
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"loot: skipped {(item is null ? "an unresolved entry" : "'" + item.Name + "'")} 0x{guid:X8} ({LootRules.ReasonName(reason)}{(item is null ? string.Empty : ", type 0x" + item.ItemType.ToString("X", CultureInfo.InvariantCulture) + ", value " + (item.Weenie.Value ?? 0).ToString(CultureInfo.InvariantCulture))})"));
    }

    /// <summary>
    /// <c>TODO(m4-hunt-pack-full)</c>: the pack refused two pick-ups in a row with ACE's "Unable to put X into container" (no free slot in the main pack): the rest of the corpse is left, named, instead of two seconds per refused item.
    /// The entries still to judge are counted in <see cref="Skipped"/> and <see cref="SkippedPackFull"/>.
    /// </summary>
    private void StopPackFull(GameState state)
    {
        var left = 0;
        if (state.Containers.Viewed is { } view && view.Guid == _container)
        {
            for (var i = 0; i < view.Entries.Count; i++)
            {
                if (!_handled.Contains(view.Entries[i].Guid))
                {
                    _handled.Add(view.Entries[i].Guid);
                    left++;
                }
            }
        }

        SkippedPackFull = left;
        Skipped += left;
        _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"loot: loot_skipped={left} why=pack-full ({_packFullRun} pick-ups in a row refused with '{PackFullText} ...': the main pack has no free slot; the rest of the corpse is left) TODO(m4-hunt-pack-full)"));
        Finish("pack-full");
    }

    private void Finish(string why)
    {
        IsDone = true;
        StoppedBy = why;
        _log.Info(_category, "loot: done " + Summary + " (rule: " + _rules.Describe() + ")");
    }
}
