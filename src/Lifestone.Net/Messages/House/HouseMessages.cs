using System.Collections.Immutable;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// The kind of a dwelling, as ACE names it (<c>ACE.Entity/Enum/HouseType.cs</c>; the wire carries a u32). A value the enum lacks is
/// kept raw, never refused.
/// </summary>
public enum HouseType : uint
{
    /// <summary>0: no type (ACE's Undef).</summary>
    Undefined = 0,

    /// <summary>1: a cottage.</summary>
    Cottage = 1,

    /// <summary>2: a villa.</summary>
    Villa = 2,

    /// <summary>3: a mansion.</summary>
    Mansion = 3,

    /// <summary>4: an apartment (exempt from the 15-day account rule; rent every 90 days in ACE).</summary>
    Apartment = 4,
}

/// <summary>
/// One line of a dwelling's price or rent list (ACE <c>Network/Structure/HousePayment.cs</c>, written by
/// <c>HousePaymentExtensions.Write</c> lines 140-147: i32 Num, i32 Paid, u32 WeenieID, String16L Name, String16L PluralName).
/// </summary>
/// <param name="Required">How many are required (ACE's Num: the stack size of the create-list row; pyreals are one line of the total).</param>
/// <param name="Paid">How many are paid (the buy list reads all paid once the house has an owner).</param>
/// <param name="WeenieClassId">The weenie class of the item asked for (273 for pyreals, which trade notes also pay).</param>
/// <param name="Name">The item's name.</param>
/// <param name="PluralName">The item's plural name (ACE's <c>GetPluralName</c>).</param>
public sealed record HousePayment(int Required, int Paid, uint WeenieClassId, string Name, string PluralName)
{
    /// <summary>The pyreal's weenie class: the line trade notes pay too (ACE <c>HousePayment.GetConsumeItems</c>).</summary>
    public const uint PyrealWcid = 273;

    /// <summary>What is still to pay on this line (ACE's <c>Remaining</c>: never below 0).</summary>
    public int Remaining => Math.Max(0, Required - Paid);

    /// <summary>True when the line is the money line (pyreals or trade notes).</summary>
    public bool IsMoney => WeenieClassId == PyrealWcid;
}

/// <summary>
/// GameEvent 0x021D HouseProfile: the guardian (ACE's SlumLord, the "covenant crystal") a Use opened, with the dwelling's terms (ACE
/// <c>GameEventHouseProfile.cs</c>: u32 guardian, then <c>HouseProfileExtensions.Write</c>, <c>Network/Structure/HouseProfile.cs</c>:
/// u32 DwellingID, u32 OwnerID, u32 Bitmask, i32 MinLevel, i32 MaxLevel, i32 MinAllegRank, i32 MaxAllegRank, u32 MaintenanceFree,
/// u32 Type, String16L OwnerName, the buy list and the rent list (u32 count + payments)). ACE sends it from <c>SlumLord.ActOnUse</c>
/// and again after a rent payment and three seconds after a purchase. Retail opens <c>gmSlumlordUI</c> from it (the window handler
/// is unread: <c>TODO(villa-retail-slumlord-recv)</c>).
/// </summary>
/// <param name="Guardian">The guardian's guid (what BuyHouse and RentHouse name).</param>
/// <param name="DwellingId">ACE's HouseId of the dwelling.</param>
/// <param name="OwnerId">The owner's guid, 0 for an unowned dwelling.</param>
/// <param name="Flags">ACE's HouseBitfield: 0x1 Active, 0x2 RequiresMonarch.</param>
/// <param name="MinLevel">The lowest level that may buy, -1 for none.</param>
/// <param name="MaxLevel">The highest level that may buy, -1 for none.</param>
/// <param name="MinAllegianceRank">The lowest allegiance rank that may buy, -1 for none.</param>
/// <param name="MaxAllegianceRank">The highest allegiance rank that may buy, -1 for none.</param>
/// <param name="MaintenanceFree">True when no rent is due this period (ACE: an inactive house).</param>
/// <param name="Type">The raw u32 of <see cref="HouseType"/>.</param>
/// <param name="OwnerName">The owner's name (empty when unowned).</param>
/// <param name="Buy">The purchase price, line by line.</param>
/// <param name="Rent">The rent, line by line, with what is paid.</param>
public sealed record HouseProfileMsg(
    uint Guardian,
    uint DwellingId,
    uint OwnerId,
    uint Flags,
    int MinLevel,
    int MaxLevel,
    int MinAllegianceRank,
    int MaxAllegianceRank,
    bool MaintenanceFree,
    uint Type,
    string OwnerName,
    ImmutableArray<HousePayment> Buy,
    ImmutableArray<HousePayment> Rent)
{
    /// <summary>ACE's HouseBitfield.Active.</summary>
    public const uint ActiveFlag = 0x1;

    /// <summary>ACE's HouseBitfield.RequiresMonarch.</summary>
    public const uint RequiresMonarchFlag = 0x2;

    /// <summary>The type as ACE's enum.</summary>
    public HouseType Kind => (HouseType)Type;

    /// <summary>True when someone owns the dwelling.</summary>
    public bool IsOwned => OwnerId != 0;
}

/// <summary>
/// GameEvent 0x0225 HouseData: the house the player (or the account) owns, the answer to a HouseQuery (ACE
/// <c>GameEventHouseData.cs</c>, <c>HouseDataExtensions.Write</c>, <c>Network/Structure/HouseData.cs</c>: u32 BuyTime, u32 RentTime,
/// u32 Type, u32 MaintenanceFree, the buy list, the rent list, then the house's Position: u32 cell, three f32, then the rotation W,
/// X, Y, Z (<c>AllegianceHierarchy.cs:192-212</c>)).
/// </summary>
/// <param name="BuyTime">When the house was bought (Unix seconds; 0 for an apartment, whose purchase time ACE does not keep).</param>
/// <param name="RentTime">When the current maintenance period began (Unix seconds).</param>
/// <param name="Type">The raw u32 of <see cref="HouseType"/>.</param>
/// <param name="MaintenanceFree">True when no rent is due this period.</param>
/// <param name="Buy">The purchase price (all paid).</param>
/// <param name="Rent">The rent, with what is paid this period.</param>
/// <param name="Position">The house object's position (ACE's House.Location).</param>
public sealed record HouseDataMsg(
    uint BuyTime,
    uint RentTime,
    uint Type,
    bool MaintenanceFree,
    ImmutableArray<HousePayment> Buy,
    ImmutableArray<HousePayment> Rent,
    AcPosition Position)
{
    /// <summary>The type as ACE's enum.</summary>
    public HouseType Kind => (HouseType)Type;
}

/// <summary>
/// GameEvent 0x0226 HouseStatus {u32 notice} (ACE <c>GameEventHouseStatus.cs</c>): the answer to a HouseQuery when no house is owned
/// (WeenieError BadParam 0x0002 by default, <c>ACE.Entity/Enum/WeenieError.cs:24</c>) and the eviction notice at login (WeenieError HouseEvicted). Retail's handler reads the word
/// as a notice type to display (Chorizite's NoticeType); its texts are unread (<c>TODO(villa-house-status-text)</c>).
/// </summary>
/// <param name="Notice">The raw notice word (ACE writes a WeenieError).</param>
public readonly record struct HouseStatusEvent(uint Notice)
{
    /// <summary>ACE's default notice: WeenieError BadParam, "no house".</summary>
    public const uint NoHouse = 0x0002;
}

/// <summary>GameEvent 0x0227 UpdateRentTime {u32 rent time} (ACE <c>GameEventHouseUpdateRentTime.cs</c>; ACE never sends it and writes 0).</summary>
/// <param name="RentTime">When the current maintenance period began (Unix seconds).</param>
public readonly record struct UpdateRentTimeEvent(uint RentTime);

/// <summary>GameEvent 0x0228 UpdateRentPayment {u32 count, payments} (ACE <c>GameEventHouseUpdateRentPayment.cs</c>; ACE never sends it and writes an empty list).</summary>
/// <param name="Rent">The rent lines.</param>
public sealed record UpdateRentPaymentMsg(ImmutableArray<HousePayment> Rent);

/// <summary>
/// GameEvent 0x0248 HouseUpdateRestrictions (ACE <c>GameEventHouseUpdateRestrictions.cs</c>): u8 sequence (a <c>ByteSequence</c>,
/// <c>SequenceManager.GetSequence</c>'s default), u32 object, then the RestrictionDB (<c>RestrictionDB.cs:74-105</c>): u32 version
/// (0x10000002), u32 open status, u32 monarch, u16 count, u16 buckets (ACE writes 768), count x {u32 guid, u32 access}. The words after
/// the sequence byte are NOT aligned. ACE sends it to every player near a house when its owner or guests change.
/// </summary>
/// <param name="Sequence">The object's restriction sequence.</param>
/// <param name="Object">The house object whose access list this is.</param>
/// <param name="Version">The list's version word (0x10000002).</param>
/// <param name="OpenStatus">1 when the house is open to everyone.</param>
/// <param name="Monarch">The monarch granted access, 0 for none.</param>
/// <param name="Buckets">The bucket count the header names (unused by retail, ACE's comment).</param>
/// <param name="Entries">The guests: guid and access (0 the house only, 1 storage too).</param>
public sealed record HouseRestrictionsMsg(byte Sequence, uint Object, uint Version, uint OpenStatus, uint Monarch, ushort Buckets, ImmutableArray<(uint Guid, uint Access)> Entries);

/// <summary>GameEvent 0x0259 HouseTransaction {u32 notice} (ACE <c>GameEventHouseTransaction.cs</c>: ACE writes 2 and never sends it).</summary>
/// <param name="Notice">The notice type.</param>
public readonly record struct HouseTransactionEvent(uint Notice);

/// <summary>
/// GameEvent 0x0271 AvailableHouses (ACE <c>GameEventHouseAvailableHouses.cs</c>): u32 type, u32 count + count x u32 location (landcell
/// ids), i32 total available: the answer to <c>/house available</c> (ListAvailable 0x0270).
/// </summary>
/// <param name="Type">The raw u32 of <see cref="HouseType"/>.</param>
/// <param name="Locations">The landcells listed.</param>
/// <param name="Total">How many of the type are available in all.</param>
public sealed record AvailableHousesMsg(uint Type, ImmutableArray<uint> Locations, int Total);
