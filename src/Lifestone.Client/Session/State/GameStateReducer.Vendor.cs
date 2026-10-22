using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The vendor model (M4 3.2 s1): GameEvent 0x0062 ApproachVendor, the player's pyreals (PropertyInt 20) and the rule that closes an open vendor.
/// </summary>
/// <remarks>
/// <para><b>The order ACE really sends</b> (capture.md C1 to C3, live, ACE 95ef426d; where it and the spec disagree, the capture wins). A Use on a vendor answers F74C TurnToObject, UseDone 0 (+0.83 s), then the 0062 about 0.65 to 1.04 s
/// after the UseDone; a GameEvent 0x02BD (the vendor's emote, a Tell) follows every 0062. A buy sends the spend (0197, 02CD EncumbranceVal, 02CD CoinValue), the 0022 of each new item (before its F745), the refreshed 0062 and the UseDone;
/// a sale sends the 0022 of the item to the vendor, the 0062 (before the coins in the first run, after the UseDone in the second), the 0022 of the payout stack, the 02CD CoinValue and the UseDone. So every 0062 replaces the stock
/// (<see cref="VendorStock.Revision"/> rises), and the first one for a vendor that is not open raises <see cref="VendorOpened"/>, any other <see cref="VendorStockChanged"/>. Retail turns the 0062 into the window through <c>CM_Vendor::SendNotice_OpenVendor</c> (11.6096 0x006AB220) and <c>gmVendorUI::OpenVendor</c> (0x004C5790); their bodies are unread.</para>
/// <para><b>The close rule</b> (<c>TODO(m4-vendor-close-rule)</c>). Retail's client closes the vendor window without any message: the window (<c>gmVendorUI::OnObjectRangeExit</c> 11.6096 0x004C0F70) closes itself when its vendor leaves the player's object range, and the same close runs from <c>gmVendorUI::RecvNotice_CloseVendor</c> 0x004C0F40 (sent by <c>CM_Vendor::SendNotice_CloseVendor</c> 0x006AB1D0, which closes <c>VendorItemsUI::CloseVendor</c> 0x004C1210 too). The function is named; the distance of that object range is unread. ACE's <c>Vendor.CheckClose</c> (W/Vendor.cs 320-362) checks every 1.5 s
/// that the last player's cylinder distance to the vendor is within the vendor's UseRadius, else plays the Close emote and nothing else. The model closes the vendor (<see cref="VendorClosed"/>) when (1) the player's own position update
/// puts the centres more than the vendor's UseRadius (0.6 m when it sent none, <c>W/WorldObject_Use.cs</c> 47-61) plus <see cref="VendorCloseMarginMetres"/> apart (the margin stands for the two cylinder radii ACE subtracts;
/// retail's object-range distance is unread), (2) another vendor's 0062 arrives, (3) the vendor's object is deleted, or (4) the player leaves the world.</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>
    /// The metres added to the vendor's UseRadius before the model closes the vendor (the player's and the vendor's cylinder radii, which ACE subtracts from the centre distance; about 0.5 m each).
    /// A named placeholder: <c>TODO(m4-vendor-close-rule)</c> (the function is <c>gmVendorUI::OnObjectRangeExit</c> 11.6096 0x004C0F70; only its range distance is unread).
    /// </summary>
    internal const double VendorCloseMarginMetres = 1.0;

    /// <summary>The use radius of an object that sent none (<c>WorldObject.IsWithinUseRadiusOf</c>, W/WorldObject_Use.cs 47-61).</summary>
    internal const float VendorDefaultUseRadius = 0.6f;

    private long _vendorRevision;
    private bool _vendorRuleLogged;

    /// <summary>GE 0x0062: the vendor a Use opened, or the refresh ACE sends after a trade. See the class remarks.</summary>
    public void OnApproachVendor(in InboundContext context, in GameEventHeader header, VendorApproachMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var previous = _state.Vendor;
        if (previous is not null && previous.Vendor != message.Vendor)
        {
            // Another vendor opened: the first one's window closes (retail sends no close).
            CloseVendor("another vendor opened");
            previous = null;
        }

        var stock = new VendorStock(message, ++_vendorRevision, _clock.Now);
        _state.Vendor = stock;
        if (_state.Objects.TryGet(message.Vendor, out var vendorObject))
        {
            _state.RememberVendorMask(vendorObject.Weenie.WeenieClassId, message.MerchandiseItemTypes);
        }

        Changed();
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(
                SessionLog.Category,
                previous is null ? "a vendor opened" : "a vendor's stock refreshed",
                new LogField("vendor", message.Vendor),
                new LogField("rows", stock.Entries.Length),
                new LogField("mask", message.MerchandiseItemTypes));
        }

        if (!_vendorRuleLogged)
        {
            _vendorRuleLogged = true;
            _log.Info(SessionLog.Category, "TODO(m4-vendor-close-rule): retail's close has no message (gmVendorUI::OnObjectRangeExit 0x004C0F70 closes the window when the vendor leaves the object range) and that range's distance is unread; the model closes a vendor when the player moves more than its UseRadius + " + VendorCloseMarginMetres.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m away, another vendor opens, its object is deleted or the player leaves");
        }

        if (previous is null)
        {
            Publish(new VendorOpened(message.Vendor));
        }
        else
        {
            Publish(new VendorStockChanged(message.Vendor));
        }
    }

    /// <summary>A 0x02CD for PropertyInt 20 with a new value raises <see cref="CoinValueChanged"/> (called from the int update after the value is stored).</summary>
    private void NoteCoinValue(uint key, int before, int value)
    {
        if (key != PlayerQualities.CoinValueProperty || before == value)
        {
            return;
        }

        Changed();
        Publish(new CoinValueChanged(before, value));
    }

    /// <summary>The player's own position changed: an open vendor farther than its use radius closes.</summary>
    private void CheckVendorRange()
    {
        if (_state.Vendor is not { } stock || _state.Self.Position is not { } here || !_state.Objects.TryGet(stock.Vendor, out var vendor) || vendor.Position is not { } there)
        {
            return;
        }

        var radius = vendor.Weenie.UseRadius ?? VendorDefaultUseRadius;
        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        var distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (distance > radius + VendorCloseMarginMetres)
        {
            CloseVendor("the player is " + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m away, beyond the use radius " + radius.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture) + " + " + VendorCloseMarginMetres.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>An F747 removed <paramref name="guid"/>: the open vendor closes if it was that object.</summary>
    private void ForgetVendor(uint guid)
    {
        if (_state.Vendor is { } stock && stock.Vendor == guid)
        {
            CloseVendor("the vendor's object was deleted");
        }
    }

    /// <summary>Forgets the open vendor without a message and returns its guid (0 when none): the world is gone, so the caller publishes <see cref="VendorClosed"/> after the rest of the leave.</summary>
    private uint DropVendorForLeave()
    {
        var guid = _state.Vendor?.Vendor ?? 0;
        _state.Vendor = null;
        return guid;
    }

    /// <summary>The player closed the vendor window (3.2-R23): closes the open vendor in the model and publishes <see cref="VendorClosed"/>.</summary>
    internal void CloseOpenVendor(string why) => CloseVendor(why);

    private void CloseVendor(string why)
    {
        if (_state.Vendor is not { } stock)
        {
            return;
        }

        _state.Vendor = null;
        Changed();
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "the open vendor closed in the model: " + why, new LogField("vendor", stock.Vendor));
        }

        Publish(new VendorClosed(stock.Vendor));
    }
}
