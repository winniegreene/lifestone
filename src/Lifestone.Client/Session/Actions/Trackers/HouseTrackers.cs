using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.BuyHouse"/> and <see cref="IActionApi.PayRent"/> (M6 villa): GameAction 0x021C BuyHouse or 0x0221 RentHouse
/// {guardian, the items the player put into the guardian window's pane}: the window's Buy and Pay buttons.
/// </summary>
/// <remarks>
/// <para><b>Buy</b> (ACE <c>Player_House.HandleActionBuyHouse</c>, @95ef426d lines 30-148). The refusals ACE says: the line "You already own a
/// house!", WeenieErrorWithString YouMustBeAboveLevel_ToBuyHouse 0x0488 and YouMustBeAboveAllegianceRank_ToBuyHouse 0x048B, WeenieError
/// YouMustBeMonarchToPurchaseDwelling 0x048A and YouMustWaitToPurchaseHouse 0x0532 (the 30-day rule), and the 15-day line. Two refusals
/// are SILENT: a guardian not on the landblock, and items that do not cover the price (<c>VerifyPurchase</c> logs and returns): they end
/// as a <see cref="ActionOutcomeKind.TimedOut"/> that names them. Success is the line "Congratulations!  You now own this dwelling."
/// (<c>SetHouseOwner</c>, two spaces after the stop), sent before the deed and before the HouseData that follows three seconds later.</para>
/// <para><b>Rent</b> (<c>HandleActionRentHouse</c>, 187-308). Refusals: "The maintenance has already been paid for this period. ..." and the
/// owner-of-several-houses line; silent: no guardian, or nothing sent matches a rent line. Success is "Maintenance paid." or
/// "Maintenance partially paid.", after the refreshed HouseProfile and the HouseQuery's answer.</para>
/// <para>Lane Use, <see cref="ActionPolicyHouse.TradeSeconds"/>. Never retried: a payment is never sent twice by the client.</para>
/// </remarks>
internal sealed class HousePaymentTracker(ActionHandle handle, uint guardian, IReadOnlyList<uint> items, bool buy)
    : ActionTracker(handle, ActionLane.Use, ActionPolicyHouse.TradeSeconds)
{
    /// <summary>ACE's purchase line (<c>Player_House.cs:652</c>; two spaces after the stop).</summary>
    internal const string BoughtLine = "Congratulations!  You now own this dwelling.";

    /// <summary>ACE's already-own line (<c>Player_House.cs:41</c>).</summary>
    internal const string AlreadyOwnLine = "You already own a house!";

    /// <summary>The start of ACE's 15-day line (<c>Player_House.cs:94</c>).</summary>
    internal const string AccountAgeLinePrefix = "Your account must be at least 15 days old to purchase this dwelling.";

    /// <summary>ACE's rent lines (<c>Player_House.cs:305</c>).</summary>
    internal const string RentPaidLine = "Maintenance paid.";

    /// <summary>ACE's partial rent line.</summary>
    internal const string RentPartiallyPaidLine = "Maintenance partially paid.";

    /// <summary>The start of ACE's already-paid line (<c>Player_House.cs:202</c>).</summary>
    internal const string RentAlreadyPaidPrefix = "The maintenance has already been paid for this period.";

    /// <summary>The start of ACE's several-houses line (<c>Player_House.cs:218</c>).</summary>
    internal const string SeveralHousesPrefix = "The owner of this house currently owns multiple houses.";

    /// <summary>The partial outcome's detail word, which callers test for.</summary>
    internal const string PartialDetail = "partially paid";

    /// <inheritdoc/>
    internal override string WouldSend =>
        (buy ? "GameAction 0x021C BuyHouse" : "GameAction 0x0221 RentHouse") + " guardian=" + ActionText.Hex(guardian) + " items=" + ActionText.Count(items.Count);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        if (WorldTrackers.NotInWorld(api) is { } notInWorld)
        {
            return notInWorld;
        }

        if (items.Count == 0)
        {
            return "the guardian window's pane is empty: nothing to pay with";
        }

        foreach (var item in items)
        {
            if (!api.State.Self.Inventory.Contains(item))
            {
                return "item " + ActionText.Hex(item) + " is not in the player's inventory (ACE skips it silently)";
            }
        }

        return null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (buy)
        {
            session!.SendBuyHouse(guardian, items);
        }
        else
        {
            session!.SendRentHouse(guardian, items);
        }

        return null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.WeenieError)
        {
            if (buy && BuyRefusalName(line.ChatType) is { } name)
            {
                Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, line.ChatType, "the server refused the purchase: " + name + " (" + ActionText.Hex(line.ChatType) + ")" + (line.Text is { Length: > 0 } t ? " " + t : string.Empty)));
                return true;
            }

            return false;
        }

        var text = line.Text;
        if (buy)
        {
            if (string.Equals(text, BoughtLine, StringComparison.Ordinal))
            {
                Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "bought: " + BoughtLine));
                return true;
            }

            if (string.Equals(text, AlreadyOwnLine, StringComparison.Ordinal) || text.StartsWith(AccountAgeLinePrefix, StringComparison.Ordinal))
            {
                Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the purchase: " + text));
                return true;
            }

            return false;
        }

        if (string.Equals(text, RentPaidLine, StringComparison.Ordinal))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "rent: " + RentPaidLine));
            return true;
        }

        if (string.Equals(text, RentPartiallyPaidLine, StringComparison.Ordinal))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "rent " + PartialDetail + ": " + RentPartiallyPaidLine));
            return true;
        }

        if (text.StartsWith(RentAlreadyPaidPrefix, StringComparison.Ordinal) || text.StartsWith(SeveralHousesPrefix, StringComparison.Ordinal))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the rent: " + text.Split('\n')[0]));
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    internal override void OnTimedOut(ActionApi api) =>
        api.Log.Warn(
            SessionLog.Category,
            buy
                ? "the purchase got no answer: ACE refuses silently when the guardian is not on the player's landblock or the placed items do not cover the price (VerifyPurchase)"
                : "the rent payment got no answer: ACE refuses silently when the guardian is not found or nothing placed matches a rent line",
            new LogField("guardian", guardian),
            new LogField("items", items.Count));

    /// <summary>The ACE name of a WeenieError (or WithString) that refuses a purchase, or null.</summary>
    internal static string? BuyRefusalName(uint code) => code switch
    {
        0x0488 => "YouMustBeAboveLevel_ToBuyHouse",
        0x048A => "YouMustBeMonarchToPurchaseDwelling",
        0x048B => "YouMustBeAboveAllegianceRank_ToBuyHouse",
        0x0532 => "YouMustWaitToPurchaseHouse",
        _ => null,
    };
}

/// <summary>
/// <see cref="IActionApi.RecallHouse"/> (M6 villa): GameAction 0x0262 TeleToHouse, <c>/house recall</c>. ACE (<c>Player_Location.HandleActionTeleToHouse</c>,
/// @95ef426d lines 59-123) refuses with WeenieError OlthoiCanOnlyRecallToLifestone 0x0593, YouHaveBeenInPKBattleTooRecently 0x04CC,
/// ExitTrainingAcademyToUseCommand 0x055D, YoureTooBusy 0x001D or YouMustOwnHouseToUseCommand 0x047F; otherwise it puts the player in peace mode,
/// broadcasts "&lt;name&gt; is recalling home.", plays HouseRecall (0x1000013A) and, after the motion's length, teleports the player to the
/// guardian's position unless they moved more than 8 m (YouHaveMovedTooFar 0x0498). Confirmed on the teleport. Lane Magic, as the
/// other recalls; the lifestone recall's limit (the motion's length is not read here).
/// </summary>
internal sealed class HouseRecallTracker(ActionHandle handle)
    : ActionTracker(handle, ActionLane.Magic, ActionPolicyRecall.LifestoneSeconds)
{
    private bool _recallingSeen;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0262 TeleToHouse";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendTeleToHouse();
        return null;
    }

    /// <inheritdoc/>
    internal override void OnTeleport(in PlayerTeleportAnnounced e) =>
        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "teleported to the house" + (_recallingSeen ? " after the recall line" : " (no recall line seen)")));

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind == ChatKind.WeenieError && RefusalName(line.ChatType) is { } name)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, line.ChatType, "the server refused the house recall: WeenieError " + name + " (" + ActionText.Hex(line.ChatType) + ")"));
            return true;
        }

        if (line.Kind == ChatKind.System && line.Text.EndsWith(" is recalling home.", StringComparison.Ordinal)
            && api.State.Self.Name is { Length: > 0 } self && line.Text.StartsWith(self, StringComparison.Ordinal))
        {
            _recallingSeen = true;
        }

        return false;
    }

    /// <summary>The ACE name of a WeenieError that refuses the house recall, or null.</summary>
    internal static string? RefusalName(uint code) => code switch
    {
        0x001D => "YoureTooBusy",
        0x047F => "YouMustOwnHouseToUseCommand",
        0x0498 => "YouHaveMovedTooFar",
        0x04CC => "YouHaveBeenInPKBattleTooRecently",
        0x055D => "ExitTrainingAcademyToUseCommand",
        0x0593 => "OlthoiCanOnlyRecallToLifestone",
        _ => null,
    };
}

/// <summary>
/// <see cref="IActionApi.QueryHouse"/> (M6 villa): GameAction 0x021E HouseQuery. ACE answers HouseData 0x0225 when the player or the account owns a
/// house, else HouseStatus 0x0226 (<c>Player_House.HandleActionQueryHouse</c>, 911-933). Confirmed when either arrives after the send
/// (<see cref="GameState.HouseInfoRevision"/> rises); lane Query, <see cref="ActionPolicyHouse.QuerySeconds"/>.
/// </summary>
internal sealed class HouseQueryTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Query, ActionPolicyHouse.QuerySeconds)
{
    private long _revisionAtSend = -1;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x021E HouseQuery";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _revisionAtSend = api.State.HouseInfoRevision;
        session!.SendHouseQuery();
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (_revisionAtSend >= 0 && api.State.HouseInfoRevision > _revisionAtSend && Resolution is null)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, api.State.HouseStatus == HouseOwnership.Owned ? "the house data arrived" : "the server says no house is owned"));
        }
    }
}

/// <summary>The housing actions' time limits (M6 villa).</summary>
internal static class ActionPolicyHouse
{
    /// <summary>A purchase or a rent payment: ACE answers inside the same action, so a short wait suffices.</summary>
    internal const double TradeSeconds = 10;

    /// <summary>A house query: one event back.</summary>
    internal const double QuerySeconds = 5;
}
