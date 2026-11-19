using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What the item-move actions share (M4 2.1 s2): the <see cref="ActionLane.Inventory"/> lane, the in-world precondition, the refusal and the busy rule.
/// <para><b>The refusal.</b> ACE refuses a move with a WeenieError (GameEvent 0x028A, or 0x028B with a string) or a transient string (0x02EB) and then GameEvent 0x00A0 {item, code}, whose code is 0 for most refusals
/// (capture C2, C4; the spec's §5.5). The tracker latches the last such line seen since the send and ends on the 0x00A0 as a <see cref="ActionOutcomeKind.ServerError"/>: the code is the 028A's (0x001D busy, 0x0029, 0x046A)
/// or the 0x00A0's own (0x04CD), the detail carries the text the session already maps (<see cref="WeenieErrorText"/>) or the 02EB's. A busy refusal (0x001D) is a refusal like any other: the caller decides, nothing is retried.</para>
/// <para><b>The busy rule.</b> ACE refuses a move sent while the previous move's chain runs (the pickup motion, the 0022, the return stance and one more motion length: capture C2, C4). A move with a chain
/// (<see cref="ChainSeconds"/> above 0) holds the lane after its confirmation until the own return-stance F74C has arrived and one more motion length has passed (<see cref="ActionPolicy.BusyChainGroundSeconds"/>,
/// <see cref="ActionPolicy.BusyChainContainerSeconds"/>); a recording without the F74C frees it <see cref="ActionPolicy.ReturnStanceWaitSeconds"/> after the confirmation. A move or give also waits after an own teleport until the client's post-teleport LoginComplete has been sent, plus
/// <see cref="ActionPolicy.LoginCompleteBusyMarginSeconds"/> (<see cref="WaitReason"/>; the rule's source is <see cref="ActionPolicy.TeleportLoginCompleteCapSeconds"/>'s doc). The splits to a pack and to the wield slot play no animation and hold nothing; a give holds the lane for the turn's length after its confirmation, which was not
/// captured as a rule (<c>TODO(m4-give-chain)</c>).</para>
/// </summary>
internal abstract class ItemMoveTracker(ActionHandle handle, uint item, double timeoutSeconds) : ActionTracker(handle, ActionLane.Inventory, timeoutSeconds)
{
    private ActionApi? _api;
    private uint _reasonCode;
    private string? _reasonText;
    private double _reasonAt = double.NaN;
    private double _chainAt = double.NaN;
    private double _returnAt = double.NaN;

    /// <summary>The item (the stack, for a split) the action moves.</summary>
    protected uint Item { get; } = item;

    /// <summary>The motion length that follows the confirmation before ACE accepts the next move; 0 for a move with no chain.</summary>
    protected double ChainSeconds { get; set; }

    /// <summary>The player's guid.</summary>
    protected static uint SelfOf(ActionApi api) => api.State.Self.Guid;

    /// <summary>True when a 028B refusal of this action ends it by itself, without waiting for the 0x00A0 (a give's: <see cref="ActionPolicy.GiveRefusalGraceSeconds"/>).</summary>
    protected virtual bool EndsOnReasonAlone(uint code) => false;

    /// <summary>True when a 0x00A0 for <paramref name="failedItem"/> refuses this action (the split to the wield slot's names the new guid).</summary>
    protected virtual bool OwnsFailure(uint failedItem) => failedItem == Item;

    /// <summary>Why the action cannot go given the inventory model, or null.</summary>
    protected virtual string? Precondition(ActionApi api) => null;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api) ?? Precondition(api);

    /// <inheritdoc/>
    internal override string? WaitReason(ActionApi api) => api.TeleportWait();

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _api = api;
        _reasonCode = 0;
        _reasonText = null;
        _reasonAt = double.NaN;
        SendMessage(session!, api);
        return null;
    }

    /// <summary>Sends the action's message.</summary>
    protected abstract void SendMessage(IAcSession session, ActionApi api);

    /// <summary>
    /// Settles the action as Confirmed and starts the busy chain (the first call counts; later ones are ignored by <see cref="ActionTracker.Resolve"/>).
    /// </summary>
    protected void Confirm(ActionApi api, string detail)
    {
        MarkChain(api);
        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail));
    }

    /// <summary>Notes the moment the chain began (the confirmation, or the first sign of it).</summary>
    protected void MarkChain(ActionApi api)
    {
        if (double.IsNaN(_chainAt))
        {
            _chainAt = api.Clock.Now;
        }
    }

    /// <inheritdoc/>
    internal override bool HoldsLane
    {
        get
        {
            if (ChainSeconds <= 0 || double.IsNaN(_chainAt) || _api is null || Resolution is not { Kind: ActionOutcomeKind.Confirmed })
            {
                return false;
            }

            var returned = double.IsNaN(_returnAt) ? _chainAt + ActionPolicy.ReturnStanceWaitSeconds : _returnAt;
            return _api.Clock.Now < returned + ChainSeconds + ActionPolicy.BusyChainMarginSeconds;
        }
    }

    /// <inheritdoc/>
    internal override void OnMotion(in ObjectMotionChanged e, ActionApi api)
    {
        if (e.IsSelf && !double.IsNaN(_chainAt) && double.IsNaN(_returnAt))
        {
            _returnAt = api.Clock.Now; // the own F74C after the confirmation is the return stance (capture C2, C4)
        }
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.WeenieError)
        {
            _reasonCode = line.ChatType;
            _reasonText = WeenieErrorText.TryFormat(line.ChatType, line.Text, out _, out var text) ? text : (line.Text.Length > 0 ? line.Text : text);
            if (double.IsNaN(_reasonAt))
            {
                _reasonAt = api.Clock.Now;
            }
        }
        else if (line.Kind == ChatKind.Transient && line.Text.Length > 0)
        {
            _reasonCode = 0;
            _reasonText = line.Text;
            if (double.IsNaN(_reasonAt))
            {
                _reasonAt = api.Clock.Now;
            }
        }

        return false; // an observer: other trackers read the same line
    }

    /// <inheritdoc/>
    internal override void OnInventoryFailed(in InventoryFailed e)
    {
        if (OwnsFailure(e.Item) || (e.Item == 0 && OwnsUnnamedFailure(e.Error)))
        {
            Resolve(Refused(e.Error));
        }
    }

    /// <summary>True when a 0x00A0 that names no item (guid 0) with <paramref name="error"/> refuses this action; none by default.</summary>
    protected virtual bool OwnsUnnamedFailure(uint error) => false;

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Resolution is null && _reasonCode != 0 && EndsOnReasonAlone(_reasonCode) && !double.IsNaN(_reasonAt) && api.Clock.Now - _reasonAt >= ActionPolicy.GiveRefusalGraceSeconds)
        {
            Resolve(Refused(0));
        }
    }

    private ActionOutcome Refused(uint failureCode)
    {
        var code = _reasonCode != 0 ? _reasonCode : failureCode;
        string? text = _reasonText;
        if (text is null && code != 0)
        {
            WeenieErrorText.TryFormat(code, null, out _, out text);
        }

        return new ActionOutcome(
            ActionOutcomeKind.ServerError,
            code,
            "the server refused " + ActionText.Hex(Item) + " (WeenieError " + ActionText.Hex(code) + (text is null ? "; ACE gives none for most refusals" : ": " + text) + ")");
    }

    /// <summary>The item's own place in the model, for a precondition.</summary>
    protected InventoryPlace PlaceOf(ActionApi api) => api.State.Self.Inventory.Find(Item);
}

/// <summary>
/// <see cref="IActionApi.PickUp"/> (and <see cref="IActionApi.MoveToPack"/> of an item the player does not hold): GameAction 0x0019 {item, container, placement}, retail
/// <c>CM_Inventory::Event_PutItemInContainer</c> (11.6096 0x006ACC80). ACE walks the player to the item, plays the pickup motion (0.74 s from the ground, 1.35 s from an open container: capture C2, C4), then sends
/// 02DA, GameEvent 0x0022 {item, container, placement}, 02CD and the sound; the 0022 confirms, when its container is the requested one. Refusals: <see cref="ItemMoveTracker"/>.
/// <para><b>There is no merge case</b> (M4 2.2 cap, the lead's live run of 2026-10-21: arrows and coins, both captured). ACE never joins a stack at pick-up: a stack picked up beside a held stack of its own class arrives as a
/// separate stack with a normal 0022, so the tracker confirms on the 0022 alone and a pick-up without one ends as a plain timeout. Joining two stacks is <see cref="IActionApi.MergeStack"/> (GameAction 0x0054). The M4 2.1
/// speculation (<c>TODO(m4-pickup-merge)</c>: a 0197 of a held stack of the item's class instead of the 0022) is retired here.</para>
/// </summary>
internal sealed class PickUpTracker(ActionHandle handle, uint item, uint container, int placement) : ItemMoveTracker(handle, item, ActionPolicy.PickUpSeconds)
{
    private uint _target;
    private bool _seen;

    /// <inheritdoc/>
    internal override void Accepted(ActionApi api) => _seen = Visible(api);

    /// <summary>
    /// M5 7 D141 ("check before"): an item seen when the pick-up was asked (in the object table or the viewed container) and in neither now is gone (a corpse that rotted, an item another took): <see cref="ActionReason.TargetGone"/>,
    /// as ACE's NoObject would answer; an item never seen is left to the server. A pick-up of an item that is not a pack needs a free pack slot (ACE never joins a stack at pick-up, see the class remarks): none by the client's count
    /// (<see cref="Quartermaster.PackView.AllPacksLessFociFree"/>, ACE's <c>Container.GetFreeInventorySlots</c>) is <see cref="ActionReason.NoFreeSlot"/>.
    /// </summary>
    protected override string? Precondition(ActionApi api)
    {
        if (_seen && !Visible(api))
        {
            return Refuse(ActionReason.TargetGone, "the item " + ActionText.Hex(Item) + " is gone: it was in the object table or the viewed container when the pick-up was asked and is in neither now");
        }

        var isPack = api.State.Objects.TryGet(Item, out var o) && (o.ItemType & BuyTracker.ContainerItemType) != 0;
        return !isPack && Quartermaster.PackView.AllPacksLessFociFree(api.State) is { } free && free < 1
            ? Refuse(ActionReason.NoFreeSlot, "no free pack slot: the pick-up of " + ActionText.Hex(Item) + " needs one and the packs have " + ActionText.Count(free) + " free")
            : null;
    }

    private bool Visible(ActionApi api)
    {
        if (api.State.Objects.TryGet(Item, out _))
        {
            return true;
        }

        if (api.State.Containers.Viewed is { } view)
        {
            foreach (var entry in view.Entries)
            {
                if (entry.Guid == Item)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0019 PutItemInContainer (pick up) " + ActionText.Hex(Item) + " into " + ActionText.Hex(container) + " slot " + placement;

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api)
    {
        _target = container == 0 ? SelfOf(api) : container;
        var view = api.State.Containers.Viewed;
        var fromContainer = false;
        if (view is not null)
        {
            foreach (var entry in view.Entries)
            {
                if (entry.Guid == Item)
                {
                    fromContainer = true;
                    break;
                }
            }
        }

        ChainSeconds = fromContainer ? ActionPolicy.BusyChainContainerSeconds : ActionPolicy.BusyChainGroundSeconds;
        session.SendPutItemInContainer(Item, _target, (uint)placement);
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid == Item && e.Container == _target && e.Kind is InventoryChangeKind.Added or InventoryChangeKind.Moved or InventoryChangeKind.Unwielded)
        {
            Confirm(api, "in " + ActionText.Hex(e.Container) + " slot " + e.Placement);
        }
    }

    /// <summary>
    /// M6 sprint (run 5, cache/m6/sprint/run5/step3-levelup.log 15:58:51-15:59:16 UTC: a second Female Tusker Tusk's pick-up waited its whole timeout, 25 s, standing): ACE refuses a quest-gated pick-up with a 0x00A0 that names
    /// no item, guid 0 (<c>QuestManager.HandleSolveError</c>, QuestManager.cs:558-578 @95ef426d: 0x043E YouHaveSolvedThisQuestTooRecently or 0x043F YouHaveSolvedThisQuestTooManyTimes, after its two chat lines), called from the
    /// pick-up's <c>VerifyQuest</c> (Player_Inventory.cs:1260) and a quest chest's open (Chest.cs:160); a pick-up meets only the first, so either code ends it as refused.
    /// </summary>
    protected override bool OwnsUnnamedFailure(uint error) => error is 0x043E or 0x043F;
}

/// <summary>
/// <see cref="IActionApi.Drop"/>: GameAction 0x001B {item}. ACE plays the pickup motion (0.73 s), sends 02DA, 02CD, 02DA again and GameEvent 0x019A {item}, the item's F745 (container none), the sound and the return stance
/// (capture C4); the 019A confirms. Senders of 0x001B in retail: not located (<c>TODO(m4-item-move-retail-cites)</c>).
/// </summary>
internal sealed class DropTracker(ActionHandle handle, uint item) : ItemMoveTracker(handle, item, ActionPolicy.ItemMoveSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x001B DropItem " + ActionText.Hex(Item);

    /// <inheritdoc/>
    protected override string? Precondition(ActionApi api) => PlaceOf(api).Held ? null : "the item " + ActionText.Hex(Item) + " is not held by the player";

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api)
    {
        ChainSeconds = ActionPolicy.BusyChainGroundSeconds;
        session.SendDropItem(Item);
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid == Item && e.Kind == InventoryChangeKind.Dropped)
        {
            Confirm(api, "dropped");
        }
    }
}

/// <summary>
/// <see cref="IActionApi.SplitToContainer"/>: GameAction 0x0055 {stack, container, placement, amount}. ACE answers GameEvent 0x0022 for the NEW guid into the container first, then 0197 for the old stack, then the new
/// stack's F745 (capture C4; spec §5.5 corrected); no animation. The 0022 of a new guid into that container confirms. Refusals: <see cref="ItemMoveTracker"/>.
/// </summary>
internal sealed class SplitToContainerTracker(ActionHandle handle, uint stack, uint container, int placement, int amount) : ItemMoveTracker(handle, stack, ActionPolicy.ItemMoveSeconds)
{
    private uint _target;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0055 StackableSplitToContainer " + ActionText.Hex(Item) + " into " + ActionText.Hex(container) + " slot " + placement + " x" + ActionText.Count(amount);

    /// <inheritdoc/>
    protected override string? Precondition(ActionApi api) => PlaceOf(api).Held ? null : "the stack " + ActionText.Hex(Item) + " is not held by the player";

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api)
    {
        _target = container == 0 ? SelfOf(api) : container;
        session.SendSplitToContainer(Item, _target, placement, amount);
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid != Item && e.Container == _target && e.Kind == InventoryChangeKind.Added)
        {
            Confirm(api, "the new stack " + ActionText.Hex(e.Guid) + " is in " + ActionText.Hex(e.Container));
        }
    }
}

/// <summary>
/// <see cref="IActionApi.SplitTo3D"/>: GameAction 0x0056 {stack, amount}. ACE plays the pickup motion (0.74 s), then sends 0197 for the old stack, 02CD, the new stack's F745 (container none), the sound and the return
/// stance (capture C4); no 019A and no 0022. The old stack's 0197 confirms once the new stack's F745 has followed (or <see cref="ActionPolicy.LoginCompleteContentsCapSeconds"/> have passed, logged).
/// </summary>
internal sealed class SplitTo3DTracker(ActionHandle handle, uint stack, int amount) : ItemMoveTracker(handle, stack, ActionPolicy.ItemMoveSeconds)
{
    private bool _stackSeen;
    private bool _created;
    private double _stackAt = double.NaN;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0056 StackableSplitTo3D " + ActionText.Hex(Item) + " x" + ActionText.Count(amount);

    /// <inheritdoc/>
    protected override string? Precondition(ActionApi api) => PlaceOf(api).Held ? null : "the stack " + ActionText.Hex(Item) + " is not held by the player";

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api)
    {
        ChainSeconds = ActionPolicy.BusyChainGroundSeconds;
        session.SendSplitTo3D(Item, amount);
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid == Item && e.Kind == InventoryChangeKind.StackChanged && !_stackSeen)
        {
            _stackSeen = true;
            _stackAt = api.Clock.Now;
            MarkChain(api);
        }
    }

    /// <inheritdoc/>
    internal override void OnObjectCreated(in ObjectCreated e, ActionApi api)
    {
        if (_stackSeen && !e.IsSelf && e.Guid != Item)
        {
            _created = true;
        }
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        base.Poll(api);
        if (Resolution is not null || !_stackSeen)
        {
            return;
        }

        if (_created)
        {
            Confirm(api, "the old stack shrank and the new stack is on the ground");
        }
        else if (api.Clock.Now - _stackAt >= ActionPolicy.LoginCompleteContentsCapSeconds)
        {
            api.Log.Warn(SessionLog.Category, "the split of " + ActionText.Hex(Item) + " to the ground: the new stack's F745 did not arrive within " + ActionText.Count((long)ActionPolicy.LoginCompleteContentsCapSeconds) + " s of the old stack's 0197");
            Confirm(api, "the old stack shrank; the new stack's object did not arrive");
        }
    }
}

/// <summary>
/// <see cref="IActionApi.SplitToWield"/>: GameAction 0x019B {stack, equipMask, amount}. ACE sends 0197 for the old stack, 02DA, 02CE and GameEvent 0x0023 {new guid, mask}, F625, the sound, then the new stack's F745
/// (capture C4); no 0022 and no animation. The 0023 of a new guid at the mask confirms. A slot that already holds ammunition is refused with 02EB "You must remove your ... to wield ..." and a 0x00A0 that names the NEW
/// guid, the old stack's 0197 restored (capture C4 run 2), so any 0x00A0 refuses it.
/// </summary>
internal sealed class SplitToWieldTracker(ActionHandle handle, uint stack, EquipMask location, int amount) : ItemMoveTracker(handle, stack, ActionPolicy.ItemMoveSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x019B StackableSplitToWield " + ActionText.Hex(Item) + " at " + ActionText.Hex((uint)location) + " x" + ActionText.Count(amount);

    /// <inheritdoc/>
    protected override string? Precondition(ActionApi api) => PlaceOf(api).Held ? null : "the stack " + ActionText.Hex(Item) + " is not held by the player";

    /// <inheritdoc/>
    protected override bool OwnsFailure(uint failedItem) => true; // the lane holds one action: the 0x00A0 names the new guid, which the client has not seen

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api) => session.SendSplitToWield(Item, (uint)location, amount);

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid != Item && e.Kind == InventoryChangeKind.Wielded && (e.Location & (uint)location) != 0)
        {
            Confirm(api, "the new stack " + ActionText.Hex(e.Guid) + " is wielded at " + ActionText.Hex(e.Location));
        }
    }
}

/// <summary>
/// <see cref="IActionApi.Give"/>: GameAction 0x00CD {target, item, amount}. ACE turns the player to the receiver, then (0.70 s later) sends 0024 {item}, 02CD, GameEvent 0x0022 {item, container = the receiver}, the "You give"
/// line and the sound 0x8E (capture C4 run 4); the reducer reports the item's leaving as <see cref="InventoryChangeKind.Removed"/> once (the 0024 first), and a part stack as the 0197
/// (<see cref="InventoryChangeKind.StackChanged"/>). Refusals: 028B {0x046A, the NPC's name} and 0x00A0 for an NPC with no Give emote for the item (also for an NPC with other emotes: not 0x03EF), 0x03EF, 0x04CE, 0x00A0
/// with 0x04CD, and 028A 0x001D with 0x00A0 when busy. A 028B refusal that is not followed by its 0x00A0 within <see cref="ActionPolicy.GiveRefusalGraceSeconds"/> ends the give by itself.
/// </summary>
internal sealed class GiveTracker(ActionHandle handle, uint target, uint item, int amount) : ItemMoveTracker(handle, item, ActionPolicy.GiveSeconds)
{
    /// <summary>ACE's refusal of a give to an NPC that has no Give emote for the item (capture C4).</summary>
    internal const uint NpcDoesNotAcceptCode = 0x046A;

    /// <summary>ACE's refusal 0x03EF (the spec's "not accepting"; not seen live).</summary>
    internal const uint NotAcceptingCode = 0x03EF;

    /// <summary>ACE's refusal 0x04CE (not seen live).</summary>
    internal const uint RefusedCode = 0x04CE;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x00CD GiveObjectRequest " + ActionText.Hex(Item) + " to " + ActionText.Hex(target) + " x" + ActionText.Count(amount);

    /// <inheritdoc/>
    protected override string? Precondition(ActionApi api) => PlaceOf(api).Held ? null : "the item " + ActionText.Hex(Item) + " is not held by the player";

    /// <inheritdoc/>
    protected override bool EndsOnReasonAlone(uint code) => code is NpcDoesNotAcceptCode or NotAcceptingCode or RefusedCode;

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session, ActionApi api)
    {
        ChainSeconds = ActionPolicy.BusyChainGiveSeconds; // TODO(m4-give-chain): not captured; the cautious reading (logged once per session by ActionApi.NoteGiveChain)
        session.SendGiveObject(target, Item, amount);
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid == Item && e.Kind is InventoryChangeKind.Removed or InventoryChangeKind.StackChanged)
        {
            api.NoteGiveChain();
            Confirm(api, e.Kind == InventoryChangeKind.Removed ? "given" : "part of the stack given (the stack is now " + ActionText.Count(e.StackSize) + ")");
        }
    }
}
