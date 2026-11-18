using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Client.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The housing model (M6 villa): the open guardian (GameEvent 0x021D HouseProfile), the player's own house (0x0225 HouseData,
/// 0x0226 HouseStatus), the houses' access lists (0x0248) and the available list (0x0271). UpdateRentTime (0x0227), UpdateRentPayment
/// (0x0228) and HouseTransaction (0x0259) are decoded and logged once by name: ACE never sends them (their writers have no caller), so
/// what retail does with them is a named TODO (<c>TODO(villa-unsent-house-events)</c>).
/// </summary>
/// <remarks>
/// <b>The guardian's close rule.</b> Retail's guardian window, like the vendor's, closes without a message; the model uses the vendor's
/// rule (<see cref="VendorCloseMarginMetres"/> beyond the guardian's UseRadius, another guardian, the object deleted, the world left), plus
/// the player closing the window (<see cref="CloseOpenGuardian"/>).
/// </remarks>
internal sealed partial class GameStateReducer
{
    private long _guardianRevision;
    private bool _unsentHouseEventLogged;

    /// <summary>GE 0x021D: the guardian a Use opened, or the refresh after a payment or a purchase.</summary>
    public void OnHouseProfile(in InboundContext context, in GameEventHeader header, HouseProfileMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var previous = _state.Guardian;
        if (previous is not null && previous.Guardian != message.Guardian)
        {
            CloseGuardian("another guardian opened");
            previous = null;
        }

        _state.Guardian = new HouseGuardian(message, ++_guardianRevision, _clock.Now);
        Changed();
        _log.Info(
            SessionLog.Category,
            previous is null ? "a house guardian opened" : "a house guardian's profile refreshed",
            new LogField("guardian", message.Guardian),
            new LogField("type", message.Kind.ToString()),
            new LogField("owner", message.OwnerId),
            new LogField("buy", Lines(message.Buy)),
            new LogField("rent", Lines(message.Rent)),
            new LogField("min_level", message.MinLevel),
            new LogField("flags", message.Flags));
        if ((message.Flags & ~(HouseProfileMsg.ActiveFlag | HouseProfileMsg.RequiresMonarchFlag)) != 0)
        {
            _log.Warn(SessionLog.Category, "TODO(villa-house-profile-flags): a HouseProfile bitmask bit ACE does not name", new LogField("flags", message.Flags));
        }

        if (previous is null)
        {
            Publish(new GuardianOpened(message.Guardian));
        }
        else
        {
            Publish(new GuardianProfileChanged(message.Guardian));
        }
    }

    /// <summary>GE 0x0225: the house the player or the account owns.</summary>
    public void OnHouseData(in InboundContext context, in GameEventHeader header, HouseDataMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _state.House = new OwnedHouse(message, _clock.Now);
        _state.HouseStatus = HouseOwnership.Owned;
        _state.HouseInfoRevision++;
        Changed();
        _log.Info(
            SessionLog.Category,
            "house data",
            new LogField("type", message.Kind.ToString()),
            new LogField("buy_time", message.BuyTime),
            new LogField("rent_time", message.RentTime),
            new LogField("rent_due", _state.House.RentDue),
            new LogField("rent", Lines(message.Rent)),
            new LogField("free", message.MaintenanceFree),
            new LogField("cell", message.Position.CellId));
        Publish(new HouseInfoChanged(HouseOwnership.Owned, 0));
    }

    /// <summary>GE 0x0226: no house (ACE's default BadParam) or an eviction notice.</summary>
    public void OnHouseStatus(in InboundContext context, in GameEventHeader header, in HouseStatusEvent message)
    {
        _state.House = null;
        _state.HouseStatus = HouseOwnership.None;
        _state.HouseInfoRevision++;
        Changed();
        _log.Info(SessionLog.Category, "house status: no house owned", new LogField("notice", message.Notice));
        if (message.Notice != HouseStatusEvent.NoHouse)
        {
            _log.Warn(SessionLog.Category, "TODO(villa-house-status-text): a HouseStatus notice other than BadParam (an eviction?); its retail text is unread", new LogField("notice", message.Notice));
        }

        Publish(new HouseInfoChanged(HouseOwnership.None, message.Notice));
    }

    /// <summary>GE 0x0248: a house's access list.</summary>
    public void OnHouseRestrictions(in InboundContext context, in GameEventHeader header, HouseRestrictionsMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _state.MutableHouseRestrictions[message.Object] = message;
        Changed();
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "house restrictions", new LogField("house", message.Object), new LogField("open", message.OpenStatus), new LogField("guests", message.Entries.Length));
        }

        Publish(new HouseRestrictionsChanged(message.Object));
    }

    /// <summary>GE 0x0271: the available houses of a type.</summary>
    public void OnAvailableHouses(in InboundContext context, in GameEventHeader header, AvailableHousesMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _state.AvailableHouses = message;
        Changed();
        _log.Info(SessionLog.Category, "available houses", new LogField("type", message.Type), new LogField("listed", message.Locations.Length), new LogField("total", message.Total));
        Publish(new AvailableHousesReceived(message.Type));
    }

    /// <summary>GE 0x0227 (ACE never sends it): logged by name.</summary>
    public void OnUpdateRentTime(in InboundContext context, in GameEventHeader header, in UpdateRentTimeEvent message) =>
        LogUnsentHouseEvent("UpdateRentTime 0x0227", message.RentTime);

    /// <summary>GE 0x0228 (ACE never sends it): logged by name.</summary>
    public void OnUpdateRentPayment(in InboundContext context, in GameEventHeader header, UpdateRentPaymentMsg message) =>
        LogUnsentHouseEvent("UpdateRentPayment 0x0228", (uint)message.Rent.Length);

    /// <summary>GE 0x0259 (ACE never sends it): logged by name.</summary>
    public void OnHouseTransaction(in InboundContext context, in GameEventHeader header, in HouseTransactionEvent message) =>
        LogUnsentHouseEvent("HouseTransaction 0x0259", message.Notice);

    /// <summary>The player closed the guardian window: closes the open guardian in the model and publishes <see cref="GuardianClosed"/>.</summary>
    internal void CloseOpenGuardian(string why) => CloseGuardian(why);

    /// <summary>The player's own position changed: an open guardian farther than its use radius closes (the vendor's rule).</summary>
    private void CheckGuardianRange()
    {
        if (_state.Guardian is not { } open || _state.Self.Position is not { } here || !_state.Objects.TryGet(open.Guardian, out var guardian) || guardian.Position is not { } there)
        {
            return;
        }

        var radius = guardian.Weenie.UseRadius ?? VendorDefaultUseRadius;
        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        var distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (distance > radius + VendorCloseMarginMetres)
        {
            CloseGuardian("the player is " + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m away");
        }
    }

    /// <summary>An F747 removed <paramref name="guid"/>: the open guardian closes if it was that object.</summary>
    private void ForgetGuardian(uint guid)
    {
        if (_state.Guardian is { } open && open.Guardian == guid)
        {
            CloseGuardian("the guardian's object was deleted");
        }
    }

    /// <summary>The world was left: forgets the guardian, the house and the access lists; returns the guardian's guid (0 when none) for the caller to publish.</summary>
    private uint DropHouseForLeave()
    {
        var guid = _state.Guardian?.Guardian ?? 0;
        _state.Guardian = null;
        _state.House = null;
        _state.HouseStatus = HouseOwnership.Unknown;
        _state.MutableHouseRestrictions.Clear();
        _state.AvailableHouses = null;
        return guid;
    }

    private void CloseGuardian(string why)
    {
        if (_state.Guardian is not { } open)
        {
            return;
        }

        _state.Guardian = null;
        Changed();
        _log.Info(SessionLog.Category, "the open house guardian closed in the model: " + why, new LogField("guardian", open.Guardian));
        Publish(new GuardianClosed(open.Guardian));
    }

    private void LogUnsentHouseEvent(string name, uint value)
    {
        if (_unsentHouseEventLogged)
        {
            return;
        }

        _unsentHouseEventLogged = true;
        _log.Warn(SessionLog.Category, "TODO(villa-unsent-house-events): " + name + " arrived although ACE has no caller for it; decoded, not applied", new LogField("value", value));
    }

    /// <summary>"Required/Paid name (wcid)" for each line, for the log.</summary>
    internal static string Lines(System.Collections.Immutable.ImmutableArray<HousePayment> lines) =>
        lines.IsDefaultOrEmpty ? "none" : string.Join("; ", lines.Select(static l => l.Paid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + l.Required.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + l.Name + " (" + l.WeenieClassId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"));
}
