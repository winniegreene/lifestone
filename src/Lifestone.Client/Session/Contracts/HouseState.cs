using System.Collections.Immutable;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The guardian the player has open (<see cref="GameState.Guardian"/>; M6 villa): the last GameEvent 0x021D HouseProfile, what retail's
/// <c>gmSlumlordUI</c> shows. ACE sends one on every Use of the guardian (<c>SlumLord.ActOnUse</c>), after a rent payment and three
/// seconds after a purchase (<c>Player_House.SetHouseOwner</c>), so each one replaces the object (a new <see cref="Revision"/>).
/// <para><b>Price lines.</b> ACE's buy and rent lists are create-list rows: the money line is wcid 273 with the total in
/// <see cref="HousePayment.Required"/>, which trade notes also pay (<c>HousePayment.GetConsumeItems</c>); every other line is an item and a
/// count (Writ of Refuge 11710, Crude Lockpick 511, ...). The buy list reads all paid once the dwelling has an owner; the rent list's
/// paid counts are what lies in the guardian's own inventory (<c>HouseProfile.SetPaidItems</c>).</para>
/// </summary>
public sealed class HouseGuardian
{
    /// <summary>The guardian a reducer builds from a 0x021D. Only the session creates one.</summary>
    internal HouseGuardian(HouseProfileMsg message, long revision, double receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        Profile = message;
        Revision = revision;
        ReceivedAt = receivedAt;
    }

    /// <summary>The decoded profile as it arrived.</summary>
    public HouseProfileMsg Profile { get; }

    /// <summary>The guardian's guid (what BuyHouse and RentHouse name).</summary>
    public uint Guardian => Profile.Guardian;

    /// <summary>The dwelling's kind.</summary>
    public HouseType Type => Profile.Kind;

    /// <summary>True when someone owns the dwelling.</summary>
    public bool IsOwned => Profile.IsOwned;

    /// <summary>The owner's guid (0 when unowned).</summary>
    public uint OwnerId => Profile.OwnerId;

    /// <summary>The owner's name (empty when unowned).</summary>
    public string OwnerName => Profile.OwnerName;

    /// <summary>The lowest level that may buy (-1 for none).</summary>
    public int MinLevel => Profile.MinLevel;

    /// <summary>The purchase price, line by line.</summary>
    public ImmutableArray<HousePayment> Buy => Profile.Buy;

    /// <summary>The rent, line by line, with what is paid this period.</summary>
    public ImmutableArray<HousePayment> Rent => Profile.Rent;

    /// <summary>
    /// True when no rent is owed this period: the dwelling is maintenance-free, or every rent line is paid. ACE's own test is
    /// <c>SlumLord.IsRentPaid</c> (an inactive house, or every line's Paid at least its Num).
    /// </summary>
    public bool IsRentPaid => Profile.MaintenanceFree || Rent.All(static r => r.Remaining == 0);

    /// <summary>Rises with every 0x021D the reducer applies.</summary>
    public long Revision { get; }

    /// <summary>When it arrived (session seconds).</summary>
    public double ReceivedAt { get; }

    /// <summary>The line of <paramref name="lines"/> for <paramref name="weenieClassId"/>, or null.</summary>
    public static HousePayment? Line(ImmutableArray<HousePayment> lines, uint weenieClassId)
    {
        foreach (var line in lines)
        {
            if (line.WeenieClassId == weenieClassId)
            {
                return line;
            }
        }

        return null;
    }
}

/// <summary>What the client knows of the player's own house (<see cref="GameState.HouseStatus"/>; M6 villa).</summary>
public enum HouseOwnership
{
    /// <summary>Nothing has arrived yet: no HouseData 0x0225 and no HouseStatus 0x0226 since the world was entered.</summary>
    Unknown,

    /// <summary>The server answered HouseStatus: the player (and the account) owns no house.</summary>
    None,

    /// <summary>The server sent HouseData: <see cref="GameState.House"/> holds it.</summary>
    Owned,
}

/// <summary>
/// The house the player or the account owns (<see cref="GameState.House"/>; M6 villa): the last GameEvent 0x0225 HouseData. ACE sends it
/// in answer to HouseQuery (0x021E), three seconds after a purchase and after a rent payment (<c>Player_House.HandleActionQueryHouse</c>).
/// <para><b>Rent due.</b> ACE's maintenance period is 30 days, 90 for an apartment (<c>House.RentInterval</c>, <c>GetRentTimestamp</c> and
/// <c>GetRentDue</c>, House.cs 179-215): the period runs from <see cref="RentTime"/> to <see cref="RentDue"/>; an owner who has not paid
/// every rent line by the end is evicted (<c>HouseManager.HandleEviction</c>).</para>
/// </summary>
public sealed class OwnedHouse
{
    /// <summary>ACE's rent interval for a cottage, villa or mansion (<c>House.RentInterval</c>).</summary>
    public const uint RentPeriodSeconds = 30u * 24 * 3600;

    /// <summary>ACE's rent interval for an apartment (three periods, House.cs 194-195 and 210-211).</summary>
    public const uint ApartmentRentPeriodSeconds = 3 * RentPeriodSeconds;

    /// <summary>The house a reducer builds from a 0x0225. Only the session creates one.</summary>
    internal OwnedHouse(HouseDataMsg message, double receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        Data = message;
        ReceivedAt = receivedAt;
    }

    /// <summary>The decoded data as it arrived.</summary>
    public HouseDataMsg Data { get; }

    /// <summary>The house's kind.</summary>
    public HouseType Type => Data.Kind;

    /// <summary>When it was bought (Unix seconds; ACE keeps 0 for an apartment).</summary>
    public uint BuyTime => Data.BuyTime;

    /// <summary>When the current maintenance period began (Unix seconds).</summary>
    public uint RentTime => Data.RentTime;

    /// <summary>The length of a maintenance period for this kind of house.</summary>
    public uint PeriodSeconds => Type == HouseType.Apartment ? ApartmentRentPeriodSeconds : RentPeriodSeconds;

    /// <summary>When the current maintenance period ends (Unix seconds): the rent must be paid before it.</summary>
    public uint RentDue => RentTime + PeriodSeconds;

    /// <summary>The rent lines, with what is paid this period.</summary>
    public ImmutableArray<HousePayment> Rent => Data.Rent;

    /// <summary>True when no rent is owed this period.</summary>
    public bool IsRentPaid => Data.MaintenanceFree || Rent.All(static r => r.Remaining == 0);

    /// <summary>The house object's position (the server's House.Location).</summary>
    public AcPosition Position => Data.Position;

    /// <summary>When it arrived (session seconds).</summary>
    public double ReceivedAt { get; }
}

/// <summary>The open guardian's profile arrived: a first 0x021D for a guardian that was not open (M6 villa). Read <see cref="GameState.Guardian"/>.</summary>
/// <param name="Guardian">The guardian's guid.</param>
public readonly record struct GuardianOpened(uint Guardian) : IGameEvent;

/// <summary>A later 0x021D for the open guardian (after a rent payment or a purchase; M6 villa).</summary>
/// <param name="Guardian">The guardian's guid.</param>
public readonly record struct GuardianProfileChanged(uint Guardian) : IGameEvent;

/// <summary>The open guardian closed in the model (the vendor's close rule: out of range, another guardian, the object deleted, the world left, or the window closed; M6 villa).</summary>
/// <param name="Guardian">The guardian's guid.</param>
public readonly record struct GuardianClosed(uint Guardian) : IGameEvent;

/// <summary>A HouseData (0x0225) or HouseStatus (0x0226) changed <see cref="GameState.HouseStatus"/> or <see cref="GameState.House"/> (M6 villa).</summary>
/// <param name="Ownership">What is known now.</param>
/// <param name="Notice">The HouseStatus notice word (0 for a HouseData).</param>
public readonly record struct HouseInfoChanged(HouseOwnership Ownership, uint Notice) : IGameEvent;

/// <summary>A house's access list arrived (0x0248; M6 villa). Read <see cref="GameState.HouseRestrictions"/>.</summary>
/// <param name="House">The house object.</param>
public readonly record struct HouseRestrictionsChanged(uint House) : IGameEvent;

/// <summary>An AvailableHouses list arrived (0x0271; M6 villa). Read <see cref="GameState.AvailableHouses"/>.</summary>
/// <param name="Type">The raw house type listed.</param>
public readonly record struct AvailableHousesReceived(uint Type) : IGameEvent;
