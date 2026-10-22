namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The player opened the vendor <see cref="Vendor"/> (M4 3.2 s1): the first GameEvent 0x0062 ApproachVendor for it since the last open (or since another vendor, a close or a logoff). Read <see cref="GameState.Vendor"/> for the
/// stock. A repeat Use on a vendor that is still open raises <see cref="VendorStockChanged"/> instead.
/// </summary>
/// <param name="Vendor">The vendor's object id.</param>
public readonly record struct VendorOpened(uint Vendor) : IGameEvent;

/// <summary>A later 0062 for the open vendor <see cref="Vendor"/> (M4 3.2 s1): ACE sends one after every buy and every sale. <see cref="GameState.Vendor"/> holds the new stock.</summary>
/// <param name="Vendor">The vendor's object id.</param>
public readonly record struct VendorStockChanged(uint Vendor) : IGameEvent;

/// <summary>
/// The open vendor <see cref="Vendor"/> closed in the model (M4 3.2 s1). Retail's close has no message (spec 5.8; ACE's <c>Vendor.CheckClose</c> only plays an emote), so the model closes it by a rule:
/// the player moved beyond the use radius, another vendor opened, the vendor's object was deleted, or the player left the world (<c>TODO(m4-vendor-close-rule)</c>).
/// </summary>
/// <param name="Vendor">The vendor's object id.</param>
public readonly record struct VendorClosed(uint Vendor) : IGameEvent;

/// <summary>
/// The player's pyreals (PropertyInt 20, <see cref="PlayerQualities.CoinValue"/>) changed (M4 3.2 s1): a 0x02CD private update with a different value. It comes before the trade's UseDone, so a tracker or a panel reads the
/// new total at the UseDone. <see cref="QualitiesChanged"/> follows as for every int.
/// </summary>
/// <param name="Previous">The pyreals before (0 when none were known).</param>
/// <param name="Value">The pyreals now.</param>
public readonly record struct CoinValueChanged(int Previous, int Value) : IGameEvent;

/// <summary>What a vendor trade did, as <see cref="VendorTradeHandle.Result"/> reports it (M4 3.2 s1).</summary>
/// <param name="IsBuy">True for <see cref="IActionApi.Buy"/>, false for <see cref="IActionApi.Sell"/>.</param>
/// <param name="Predicted">The pyreals the 0062's rates predict the trade moves (paid for a buy, received for a sale), or -1 when the model could not predict (no open stock for the vendor, an unknown guid).</param>
/// <param name="Actual">The pyreals that really moved, from the CoinValue before and after (paid for a buy, received for a sale).</param>
/// <param name="ObjectsExpected">The objects the server announced with a 0022 for the player (the bought items, the payout coins) that the model should hold.</param>
/// <param name="ObjectsArrived">How many of them had their F745 inside the wait.</param>
/// <param name="Warn">Null when everything matched; else what did not (a coin delta that differs from the prediction, objects that never arrived, items the vendor refused with a line).</param>
public sealed record VendorTradeResult(bool IsBuy, long Predicted, long Actual, int ObjectsExpected, int ObjectsArrived, string? Warn)
{
    /// <summary>True when the prediction held and every announced object arrived.</summary>
    public bool Matched => Warn is null;
}

/// <summary>
/// The handle of <see cref="IActionApi.Buy"/> and <see cref="IActionApi.Sell"/>: an <see cref="ActionHandle"/> plus what the trade moved. <see cref="Result"/> is set when the trade was confirmed (no 0x00A0 since the send),
/// before <see cref="ActionHandle.Completion"/> completes; it stays null for every other outcome.
/// </summary>
public sealed class VendorTradeHandle : ActionHandle
{
    /// <summary>A queued trade handle. Only the session creates handles.</summary>
    internal VendorTradeHandle(long id, ActionKind kind, ActionSource source, string summary, double queuedAt)
        : base(id, kind, source, summary, queuedAt)
    {
    }

    /// <summary>What the trade moved, or null (not confirmed).</summary>
    public VendorTradeResult? Result { get; private set; }

    /// <summary>Completes the handle with <paramref name="outcome"/>, setting <see cref="Result"/> first, unless it already completed (see <see cref="ActionHandle.TryComplete"/>).</summary>
    internal bool TryComplete(VendorTradeResult? result, ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError)
    {
        if (IsCompleted)
        {
            return false;
        }

        Result = result;
        return TryComplete(outcome, time, onHandlerError);
    }
}
