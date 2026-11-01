using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What the three vendor actions share (M4 3.2 s1): the Use lane (they end with ACE's UseDone 0x01C7, like a Use), the in-world precondition, one vendor action at a time, the UseDone latch and the refusal text.
/// <para><b>The lane.</b> ACE answers a Use, a buy and a sale with one UseDone and no request id, so a vendor action waits while another vendor action is in flight (<see cref="ActionTracker.WaitReason"/>), and the UseDone goes to the
/// oldest action that takes it, as a Use's does. A busy ACE (capture: none seen; ACE <c>Player.HandleActionBuyItem</c>, <c>HandleActionSellItem</c> and <c>Vendor.ActOnUse</c> check <c>IsBusy</c> first) answers 00A0 and UseDone 0x001D, which
/// ends the action as a <see cref="ActionOutcomeKind.ServerError"/> with that code; nothing is held after a vendor action (no animation follows a trade: capture C1, C2).</para>
/// <para><b>The refusal text.</b> The reason of a refusal is the last 02EB, 028A or 028B line (or F7E0 line) since the send (the chat channels' 0x051C noise at a logoff is not one). A Tell (the vendor's own emote, GE 02BD) is never one.</para>
/// </summary>
internal abstract class VendorLaneTracker : ActionTracker
{
    /// <summary>The 028B code ACE sends when a character leaves a chat channel: noise at a logoff, never a refusal (capture.md, the noise rule).</summary>
    internal const uint ChannelLeftCode = 0x051C;

    private bool _useDoneSeen;
    private uint _useDoneCode;
    private double _useDoneAt = double.NaN;
    private string? _text;

    /// <summary>A vendor action in the Use lane with <paramref name="timeoutSeconds"/> from its send.</summary>
    protected VendorLaneTracker(ActionHandle handle, double timeoutSeconds)
        : base(handle, ActionLane.Use, timeoutSeconds)
    {
    }

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <summary>True once this action's UseDone arrived.</summary>
    protected bool UseDoneSeen => _useDoneSeen;

    /// <summary>The code of this action's UseDone.</summary>
    protected uint UseDoneCode => _useDoneCode;

    /// <summary>The session-clock time of this action's UseDone, or NaN.</summary>
    protected double UseDoneAt => _useDoneAt;

    /// <summary>The last refusal-like line since the send (02EB, 028A, 028B or F7E0), or null.</summary>
    protected string? Text => _text;

    /// <summary>The Action API this action was sent through (set by <see cref="Begin"/>).</summary>
    protected ActionApi? Api { get; private set; }

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override string? WaitReason(ActionApi api) => api.VendorActionPending(this) ? "another vendor action is in flight" : null;

    /// <summary>Resets what a send starts with.</summary>
    protected void Begin(ActionApi api)
    {
        Api = api;
        _useDoneSeen = false;
        _useDoneCode = 0;
        _useDoneAt = double.NaN;
        _text = null;
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        if (_useDoneSeen || Api is null)
        {
            return false; // this action's UseDone came already: a later one is the next action's
        }

        _useDoneSeen = true;
        _useDoneCode = e.WeenieError;
        _useDoneAt = Api.Clock.Now;
        OnOwnUseDone(e.WeenieError);
        return true;
    }

    /// <summary>The action's UseDone arrived with <paramref name="code"/>.</summary>
    protected abstract void OnOwnUseDone(uint code);

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        switch (line.Kind)
        {
            case ChatKind.Transient or ChatKind.System when line.Text.Length > 0:
                _text = line.Text;
                break;
            case ChatKind.WeenieError when line.ChatType != ChannelLeftCode:
                _text = WeenieErrorText.TryFormat(line.ChatType, line.Text, out _, out var text) ? text : (line.Text.Length > 0 ? line.Text : text);
                break;
        }

        return false; // an observer: other trackers read the same line
    }

    /// <summary>A code as the detail names it: <c>WeenieError 0x001D YoureTooBusy: You're too busy!</c>.</summary>
    internal static string Describe(uint code)
    {
        var name = code switch
        {
            0x001D => "YoureTooBusy",
            0x0038 => "NoObject",
            0x0506 => "CantDoThatTradeInProgress",
            _ => null,
        };
        var known = WeenieErrorText.TryFormat(code, null, out _, out var text);
        return "WeenieError 0x" + code.ToString("X4", CultureInfo.InvariantCulture) + (name is null ? string.Empty : " " + name) + (known ? ": " + text : string.Empty);
    }
}

/// <summary>
/// <see cref="IActionApi.OpenVendor"/> (M4 3.2 s1): GameAction 0x0036 Use on a vendor. The order ACE answers in was captured live (capture.md C1, C2): F74C TurnToObject at +8 ms, <b>UseDone 0 at +0.83 to 0.96 s, then the 0062
/// 0.65 to 1.04 s after the UseDone</b>, then a GameEvent 0x02BD (the vendor's emote, a Tell; not a refusal). So the open is Confirmed on the 0062 for the vendor (the model's <see cref="VendorStock.Revision"/> rises past the send),
/// not on the UseDone. A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/> naming it (0x001D busy, 0x0038 no such object). A UseDone 0 with no 0062 for the vendor within
/// <see cref="ActionPolicy.OpenVendorMarginSeconds"/> is <see cref="ActionOutcomeKind.Refused"/> "vendor did not open" (a closed shop or a failed walk is a bare UseDone 0, spec 5.8), with the last refusal line if one came. 20 s overall.
/// <para>A Use on a vendor that is already open is sent again and Confirmed by the refresh ACE answers with (a second Use does not close it). If the 0062 arrives before the UseDone the lane stays held until the UseDone has been taken
/// (<see cref="HoldsLane"/>), so a later action does not take it.</para>
/// </summary>
internal sealed class OpenVendorTracker(ActionHandle handle, uint vendor) : VendorLaneTracker(handle, ActionPolicy.OpenVendorSeconds)
{
    private long _baseRevision;
    private bool _opened;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0036 Use (open vendor) " + ActionText.Hex(vendor);

    /// <inheritdoc/>
    internal override bool HoldsLane => _opened && !UseDoneSeen;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        Begin(api);
        _opened = false;
        _baseRevision = api.State.Vendor?.Revision ?? 0;
        session!.SendUse(vendor);
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Resolution is not null)
        {
            return;
        }

        if (api.State.Vendor is { } stock && stock.Vendor == vendor && stock.Revision > _baseRevision)
        {
            _opened = true;
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "opened with " + ActionText.Count(stock.Entries.Length) + " rows"));
            return;
        }

        if (UseDoneSeen && UseDoneCode == 0 && api.Clock.Now - UseDoneAt >= ActionPolicy.OpenVendorMarginSeconds)
        {
            Resolve(new ActionOutcome(
                ActionOutcomeKind.Refused,
                0,
                "vendor did not open: UseDone 0 but no 0062 ApproachVendor for " + ActionText.Hex(vendor) + " within " + ActionPolicy.OpenVendorMarginSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" +
                (Text is null ? string.Empty : " (the server said: " + Text + ")")));
        }
    }

    /// <inheritdoc/>
    protected override void OnOwnUseDone(uint code)
    {
        if (code != 0)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, code, "the server refused the use of the vendor " + ActionText.Hex(vendor) + ": " + Describe(code)));
        }
    }
}

/// <summary>
/// What <see cref="BuyTracker"/> and <see cref="SellTracker"/> share (M4 3.2 s1): a trade is complete at the UseDone, Confirmed iff no GameEvent 0x00A0 came since the send, then checked by the CoinValue delta against the price or payout
/// the 0062 predicted (a mismatch is a Warn detail in the outcome and the log, never a failure).
/// <para><b>The order</b> (capture C1, C2). A buy: 0197 (the pyreal stack), 02CD CoinValue, the 0022 of each new item, the refreshed 0062, UseDone 0, then <b>the F745 of each item about 3 ms later</b>. A sale: the 0022 of the item
/// (container = the vendor), the 0062 (before the coins in C1, after the UseDone in C2: the tracker never waits for it), the 0022 of the payout stack, 02CD CoinValue, UseDone 0, then the payout stack's F745. So after the UseDone the
/// action waits for the F745 of each item the server announced with a 0022 for the player (as <c>SpawnGear</c> waits), at most <see cref="ActionPolicy.TradeObjectsSeconds"/> (a miss is a Warn, not a failure).</para>
/// <para><b>The refusals.</b> Every vendor refusal is a 00A0 with the player's own guid and error 0, then the UseDone 0 (capture C2: "The Leather Bracers is unsellable." in a 02EB first); the reason is the last 02EB, 028A, 028B or F7E0 text
/// (<see cref="VendorLaneTracker"/>) and none for too few pyreals (ACE sends no text, <c>Vendor.cs</c> 546-552): the tracker then names the shortfall from the prediction. A UseDone with a code (0x001D busy, 0x0038 no vendor, 0x0506
/// trading) is a <see cref="ActionOutcomeKind.ServerError"/> with it; so is a 00A0, with code 0 and the reason.</para>
/// </summary>
internal abstract class TradeTracker : VendorLaneTracker
{
    private readonly HashSet<uint> _announced = [];
    private int _removed;
    private readonly List<uint> _removedGuids = [];
    private bool _failed;
    private int _coinsBefore;
    private long _predicted = -1;
    private string? _note;
    private VendorTradeResult? _result;

    /// <summary>A trade with <paramref name="vendor"/>.</summary>
    protected TradeTracker(VendorTradeHandle handle, uint vendor)
        : base(handle, ActionPolicy.TradeSeconds)
    {
        Vendor = vendor;
    }

    /// <summary>The vendor's guid.</summary>
    protected uint Vendor { get; }

    /// <summary>True for a buy, false for a sale.</summary>
    protected abstract bool IsBuy { get; }

    /// <summary>The verb for the detail.</summary>
    protected abstract string Verb { get; }

    /// <summary>The items the trade names, for the detail.</summary>
    protected abstract int ItemCount { get; }

    /// <summary>The pyreals the open stock predicts the trade moves (paid, or received), or -1 when it cannot say; <paramref name="note"/> carries a remark (an unknown guid ACE skips).</summary>
    protected abstract long Predict(ActionApi api, out string? note);

    /// <summary>Sends the trade's message.</summary>
    protected abstract void Transmit(IAcSession session);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        Begin(api);
        _announced.Clear();
        _removed = 0;
        _removedGuids.Clear();
        _failed = false;
        _result = null;
        _coinsBefore = api.State.Self.Qualities.CoinValue;
        _predicted = Predict(api, out _note);
        Transmit(session!);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        switch (e.Kind)
        {
            case InventoryChangeKind.Added:
                _announced.Add(e.Guid);
                break;
            case InventoryChangeKind.Removed when e.Container == Vendor:
                _removed++;
                _removedGuids.Add(e.Guid);
                break;
        }
    }

    /// <inheritdoc/>
    internal override void OnInventoryFailed(in InventoryFailed e)
    {
        // Every vendor refusal ACE sends names the player (capture C2, spec 5.8); a refusal of some other item (a pickup, a drop or a merge on the Move lane) is not this trade's.
        if (Api is not null && e.Item == Api.State.Self.Guid)
        {
            _failed = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnOwnUseDone(uint code)
    {
        if (code != 0)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, code, "the server ended the " + Verb + " with " + Describe(code)));
        }
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Resolution is not null || !UseDoneSeen)
        {
            return;
        }

        if (_failed)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the vendor refused the " + Verb + " (GameEvent 0x00A0): " + Reason(api), RefusedReason()));
            return;
        }

        var arrived = 0;
        foreach (var guid in _announced)
        {
            if (api.State.Objects.TryGet(guid, out _))
            {
                arrived++;
            }
        }

        if (arrived < _announced.Count && api.Clock.Now - UseDoneAt < ActionPolicy.TradeObjectsSeconds)
        {
            return; // the F745s follow the UseDone by a few ms (capture C1, C2)
        }

        Settle(api, arrived);
    }

    // M5 7 D141: the typed reason of a vendor's 00A0, from the shortfall the prediction names or ACE's words (Vendor.cs 492-499 @95ef426d); none when neither says.
    private ActionReason RefusedReason()
    {
        if (IsBuy && Text is null && _predicted >= 0 && _predicted > _coinsBefore)
        {
            return ActionReason.NotEnoughCoin;
        }

        return Text switch
        {
            "You do not have enough pack space to buy that!" or "You do not have enough container slots to buy that!" => ActionReason.NoFreeSlot,
            "You are too encumbered to buy that!" => ActionReason.Overloaded,
            _ => ActionReason.None,
        };
    }

    private string Reason(ActionApi api)
    {
        if (Text is { } text)
        {
            return text;
        }

        if (IsBuy && _predicted >= 0 && _predicted > _coinsBefore)
        {
            return "no reason given (too few pyreals: the price is " + ActionText.Count(_predicted) + " and the player holds " + ActionText.Count(_coinsBefore) + "; ACE sends no text for it)";
        }

        return "no reason given";
    }

    /// <summary>The prediction made at the send, or -1.</summary>
    protected long Predicted => _predicted;

    /// <summary>The items that left the pack for the vendor (the 0022s into his container), in arrival order.</summary>
    protected IReadOnlyList<uint> RemovedGuids => _removedGuids;

    /// <summary>
    /// The prediction the coin delta is compared with, and the note on what the server did not take. The default is the one made at the send; a sale narrows it to the items that really left the pack (3.2-L1).
    /// </summary>
    /// <param name="refusedNote">The note on refused items, or null.</param>
    protected virtual long SettledPrediction(out string? refusedNote)
    {
        refusedNote = null;
        return _predicted;
    }

    private void Settle(ActionApi api, int arrived)
    {
        var predicted = SettledPrediction(out var refusedNote);
        var after = api.State.Self.Qualities.CoinValue;
        long actual = IsBuy ? (long)_coinsBefore - after : (long)after - _coinsBefore;
        var warn = new List<string>();
        if (predicted < 0)
        {
            warn.Add("no prediction (the vendor's stock was not open in the model)");
        }
        else if (actual != predicted)
        {
            warn.Add("the pyreals " + (IsBuy ? "paid" : "received") + " are " + ActionText.Count(actual) + " but the 0062's rates predict " + ActionText.Count(predicted));
        }

        if (arrived < _announced.Count)
        {
            warn.Add(ActionText.Count(_announced.Count - arrived) + " of " + ActionText.Count(_announced.Count) + " announced objects had no F745 within " + ActionText.Count((long)ActionPolicy.TradeObjectsSeconds) + " s");
        }

        if (_announced.Count == 0 && _removed == 0 && actual == 0)
        {
            // M5 7 D141's after-check: a UseDone 0 with no 00A0, but no pyreal moved, no item arrived and none left the pack: the trade did not happen. It fails at once, typed, never Confirmed.
            var nothing = "the " + Verb + " of " + ActionText.Count(ItemCount) + " item(s) changed nothing: UseDone 0 and no 00A0, but no pyreal moved, no item arrived and none left the pack" +
                (predicted > 0 ? " (the 0062's rates predict " + ActionText.Count(predicted) + ")" : string.Empty) + (_note is null ? string.Empty : "; " + _note) +
                (Text is null ? string.Empty : "; the server said: " + Text);
            api.Log.Warn(SessionLog.Category, "vendor " + nothing);
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, nothing, ActionReason.NoEffect));
            return;
        }

        var warning = warn.Count == 0 ? null : string.Join("; ", warn);
        _result = new VendorTradeResult(IsBuy, predicted, actual, _announced.Count, arrived, warning);
        var detail = Verb + " of " + ActionText.Count(ItemCount) + " item(s): " + ActionText.Count(actual) + " pyreals " + (IsBuy ? "paid" : "received") +
            (warning is null ? " (as predicted)" : "; WARN: " + warning) +
            (_removed > 0 ? "; " + ActionText.Count(_removed) + " item(s) left the pack" : string.Empty) +
            (_note is null ? string.Empty : "; " + _note) +
            (refusedNote is null ? string.Empty : "; " + refusedNote) +
            (Text is null ? string.Empty : "; the server said: " + Text);
        if (warning is not null)
        {
            api.Log.Warn(SessionLog.Category, "vendor " + detail);
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail));
    }

    /// <inheritdoc/>
    internal override bool CompleteHandle(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError) =>
        ((VendorTradeHandle)Handle).TryComplete(outcome.Kind == ActionOutcomeKind.Confirmed ? _result : null, outcome, time, onHandlerError);
}

/// <summary>
/// <see cref="IActionApi.Buy"/> (M4 3.2 s1): GameAction 0x005F {vendor, count, count x {i32 amount, u32 item}} (retail <c>CM_Vendor::Event_Buy</c> 11.6096 0x006AB050 body unread, <c>TODO(m4-buy-trailer)</c>). ACE validates every row (an invalid amount rejects the
/// whole buy, an unknown guid is skipped silently), takes the price from the pyreals and creates the items in the pack, never merged into a held stack (W/Player_Commerce.cs 22-113, W/Vendor.cs 431-571). The predicted price is
/// <see cref="VendorStock.PriceToBuy(uint, uint)"/> over the open stock. See <see cref="TradeTracker"/>.
/// </summary>
internal sealed class BuyTracker(VendorTradeHandle handle, uint vendor, IReadOnlyList<(uint Item, uint Amount)> items) : TradeTracker(handle, vendor)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x005F Buy " + ActionText.Hex(Vendor) + " x" + ActionText.Count(items.Count);

    /// <inheritdoc/>
    protected override bool IsBuy => true;

    /// <summary>The ItemType bit of a container (ACE <c>ItemType.Container</c>): a pack bought takes a container slot, not a pack slot.</summary>
    internal const uint ContainerItemType = 0x00000200;

    /// <summary>ACE's main pack capacity for a player (<c>ItemCapacity</c> 102), when the player's own description does not carry it.</summary>
    internal const int PlayerPackCapacity = 102;

    /// <inheritdoc/>
    /// <remarks>
    /// M5 7 D141's before-check, with the vendor's stock open in the model (without it there is no price to compare and the server judges): the pyreals cover the price the 0062's rates predict (ACE refuses a
    /// shortfall with a bare 00A0, <c>Vendor.cs</c> 546-552), and the packs have the free slots the rows need (ACE's <c>ItemsToReceive</c>: a stack per max stack size, an item each for what does not stack; a
    /// pack bought is left out). An alternate currency is not checked.
    /// </remarks>
    internal override string? Refusal(ActionApi api) => base.Refusal(api) ?? Shortfall(api);

    private string? Shortfall(ActionApi api)
    {
        if (api.State.Vendor is not { } stock || stock.Vendor != Vendor)
        {
            return null;
        }

        long price = 0;
        var slots = 0;
        foreach (var (item, amount) in items)
        {
            if (stock.Find(item) is not { } entry)
            {
                continue; // ACE skips a guid it does not sell
            }

            price += stock.PriceToBuy(entry, amount);
            if ((entry.ItemType & ContainerItemType) == 0)
            {
                slots += SlotsFor(entry.Desc, amount);
            }
        }

        if (stock.AlternateCurrencyWcid == 0 && api.State.Self.Qualities.TryGetInt(PlayerQualities.CoinValueProperty, out var coins) && price > coins)
        {
            return Refuse(ActionReason.NotEnoughCoin, "not enough pyreals: the buy costs " + ActionText.Count(price) + " at the 0062's rates and the player holds " + ActionText.Count(coins));
        }

        if (slots > 0 && FreeSlots(api.State) is { } free && slots > free)
        {
            return Refuse(ActionReason.NoFreeSlot, "not enough pack space: the buy needs " + ActionText.Count(slots) + " slot(s) and the packs have " + ActionText.Count(free) + " free");
        }

        return null;
    }

    /// <summary>
    /// The pack slots <paramref name="amount"/> of an item takes at least (ACE <c>ItemsToReceive.GetItemSlotAndBurdenRequirements</c>): one per max stack size for a stackable item, one each for an item whose maximum
    /// stack is 1; a row whose 0062 gave no maximum counts one (a lower bound: the refusal must be certain).
    /// </summary>
    internal static int SlotsFor(Lifestone.Net.Types.WeenieDesc desc, uint amount) => desc.MaxStackSize switch
    {
        { } max and > 1 => (int)Math.Min(int.MaxValue, (amount + max - 1) / max),
        1 => (int)Math.Min(int.MaxValue, amount),
        _ => 1,
    };

    /// <summary>
    /// The packs' free slots as ACE counts them (<c>Container.GetFreeInventorySlots</c>, <c>Container.cs</c> 214-225 @95ef426d): the main pack's capacity less its items, plus each side pack's capacity less its items;
    /// a foci takes a main pack slot. An upper bound when the model misses an item; null when the inventory is not known or a side pack's capacity is not.
    /// </summary>
    internal static int? FreeSlots(GameState state)
    {
        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return null;
        }

        var capacity = state.Objects.TryGet(state.Self.Guid, out var self) && self.Weenie.ItemsCapacity is > 0 ? self.Weenie.ItemsCapacity.Value : PlayerPackCapacity;
        var free = capacity - inventory.Pack.Count;
        foreach (var pack in inventory.SidePacks)
        {
            if (pack.ContainerType != 1)
            {
                free--; // a foci sits in a main pack slot
                continue;
            }

            if (!state.Objects.TryGet(pack.Guid, out var side) || side.Weenie.ItemsCapacity is not > 0)
            {
                return null;
            }

            free += side.Weenie.ItemsCapacity.Value - inventory.ItemsIn(pack.Guid).Count;
        }

        return free;
    }

    /// <inheritdoc/>
    protected override string Verb => "buy";

    /// <inheritdoc/>
    protected override int ItemCount => items.Count;

    /// <inheritdoc/>
    protected override long Predict(ActionApi api, out string? note)
    {
        note = null;
        if (api.State.Vendor is not { } stock || stock.Vendor != Vendor)
        {
            return -1;
        }

        long total = 0;
        var unknown = 0;
        foreach (var (item, amount) in items)
        {
            if (stock.PriceToBuy(item, amount) is { } price)
            {
                total += price;
            }
            else
            {
                unknown++;
            }
        }

        if (unknown > 0)
        {
            note = ActionText.Count(unknown) + " guid(s) are not in the open stock (ACE skips them silently)";
        }

        return total;
    }

    /// <inheritdoc/>
    protected override void Transmit(IAcSession session) => session.SendBuy(Vendor, items);
}

/// <summary>
/// <see cref="IActionApi.Sell"/> (M4 3.2 s1): GameAction 0x0060 {vendor, count, count x {i32 amount, u32 item}} (the amount is the whole stack: ACE sells and pays the whole stack whatever the amount says, W/Player_Commerce.cs 261, 296). ACE drops
/// an item it will not take with a 02EB line ("The X is unsellable.") and goes on with the rest; a sale of nothing is the 00A0. The predicted payout is the sum of <see cref="VendorStock.PayoutFor(WorldObject)"/> over the items the open
/// stock's mask takes. See <see cref="TradeTracker"/>.
/// </summary>
internal sealed class SellTracker(VendorTradeHandle handle, uint vendor, IReadOnlyList<(uint Item, uint Amount)> items) : TradeTracker(handle, vendor)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0060 Sell " + ActionText.Hex(Vendor) + " x" + ActionText.Count(items.Count);

    /// <inheritdoc/>
    protected override bool IsBuy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// M5 7 D141's before-check: every item to sell is held by the player. ACE skips one it cannot find and pays for the rest; a guid the player no longer holds means the sale was planned on an old model, so it is
    /// refused, named, and the caller plans again.
    /// </remarks>
    internal override string? Refusal(ActionApi api)
    {
        if (base.Refusal(api) is { } refusal)
        {
            return refusal;
        }

        foreach (var (item, _) in items)
        {
            if (!api.State.Self.Inventory.Find(item).Held)
            {
                return Refuse(ActionReason.NotHeld, "the item " + ActionText.Hex(item) + " to sell is not held by the player");
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override string Verb => "sale";

    /// <inheritdoc/>
    protected override int ItemCount => items.Count;

    /// <inheritdoc/>
    protected override long Predict(ActionApi api, out string? note)
    {
        note = null;
        if (api.State.Vendor is not { } stock || stock.Vendor != Vendor)
        {
            return -1;
        }

        long total = 0;
        var refused = 0;
        _payouts.Clear();
        _names.Clear();
        foreach (var (item, _) in items)
        {
            if (api.State.Objects.TryGet(item, out var named))
            {
                _names[item] = named.Name;
            }

            if (!api.State.Objects.TryGet(item, out var obj) || !stock.Buys(obj))
            {
                refused++;
                continue;
            }

            var payout = stock.PayoutFor(obj);
            _payouts[item] = payout;
            total += payout;
        }

        if (refused > 0)
        {
            note = ActionText.Count(refused) + " item(s) are not held or not in the vendor's mask or have no value, so the payout leaves them out";
        }

        return total;
    }

    /// <summary>
    /// The payout of the items that left the pack only (3.2-L1): ACE skips an item it will not take with a 02EB "The X is unsellable." (a Food item inside Renald's mask) and pays for the rest, so the sent items' sum over-predicts a partial sale.
    /// The note names the items sent that did not leave.
    /// </summary>
    protected override long SettledPrediction(out string? refusedNote)
    {
        refusedNote = null;
        var all = Predicted;
        if (all < 0)
        {
            return all;
        }

        long total = 0;
        foreach (var guid in RemovedGuids)
        {
            if (_payouts.TryGetValue(guid, out var payout))
            {
                total += payout;
            }
        }

        List<string>? kept = null;
        foreach (var (item, _) in items)
        {
            if (!RemovedGuids.Contains(item))
            {
                (kept ??= []).Add(_names.TryGetValue(item, out var name) ? name : ActionText.Hex(item));
            }
        }

        if (kept is not null)
        {
            refusedNote = ActionText.Count(kept.Count) + " item(s) stayed in the pack, not taken by the vendor (" + string.Join(", ", kept) + "); the prediction counts the " + ActionText.Count(RemovedGuids.Count) + " that left";
        }

        return total;
    }

    private readonly Dictionary<uint, long> _payouts = [];
    private readonly Dictionary<uint, string> _names = [];

    /// <inheritdoc/>
    protected override void Transmit(IAcSession session) => session.SendSell(Vendor, items);
}
