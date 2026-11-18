using System.Collections.Immutable;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// One row of an open vendor's stock (M4 3.2 s1): the 0062's row as the reducer keeps it. A <see cref="Stack"/> of <see cref="VendorEntry.Unlimited"/> (-1) is a shop row that never runs out (ACE's packed
/// 0xFFFFFF, OpenAC #218 as m4-plan D16 reads it); a listed 0 stays 0 and is not buyable. The guid is what <see cref="IActionApi.Buy"/> names.
/// </summary>
/// <param name="Stack">The stack the vendor lists, or -1 for unlimited.</param>
/// <param name="Guid">The stock object's guid.</param>
/// <param name="Desc">The row's WeenieDesc (the whole description, for a panel that wants more than the fields below).</param>
public sealed record VendorStockEntry(int Stack, uint Guid, WeenieDesc Desc)
{
    /// <summary>True when the vendor never runs out of this row.</summary>
    public bool IsUnlimited => Stack == VendorEntry.Unlimited;

    /// <summary>The item's name.</summary>
    public string Name => Desc.Name;

    /// <summary>The item's weenie class.</summary>
    public uint WeenieClassId => Desc.WeenieClassId;

    /// <summary>The Value of the stock object as the description carries it (0 when it sent none): the base of the price.</summary>
    public uint Value => Desc.Value ?? 0;

    /// <summary>The item's ItemType.</summary>
    public uint ItemType => Desc.ItemType;

    /// <summary>The item's icon (a dat id).</summary>
    public uint IconId => Desc.IconId;

    /// <summary>The stack size of the stock object (1 when the description sent none).</summary>
    public uint StackSize => Desc.StackSize is { } s and > 0 ? s : 1u;

    /// <summary>The most one created stack holds: 1 for an item that does not stack.</summary>
    public uint MaxStackSize => Desc.MaxStackSize is { } m and > 1 ? m : 1u;

    /// <summary>The Value of one unit: the description's Value over its stack size.</summary>
    public double UnitValue => (double)Value / StackSize;

    /// <summary>True when <paramref name="amount"/> can be bought from this row: at least 1, and an unlimited row at any amount, a listed stack up to its size (a listed 0 never).</summary>
    public bool IsBuyable(uint amount) => amount > 0 && (IsUnlimited || (Stack > 0 && amount <= (uint)Stack));
}

/// <summary>The clause of a vendor's sale test that refuses an item (<see cref="VendorStock.Refusal"/>), in the order the test reads them.</summary>
public enum SaleRefusal
{
    /// <summary>The vendor takes the item.</summary>
    None,

    /// <summary>The item's ItemType is outside the vendor's <see cref="VendorStock.Mask"/>.</summary>
    Mask,

    /// <summary>The item's Value is below 1.</summary>
    Value,

    /// <summary>The item carries ACE's Retained description bit (<see cref="VendorStock.RetainedFlag"/>).</summary>
    Retained,

    /// <summary>The item's weenie class is one ACE answers "is unsellable" (<see cref="VendorStock.BreadWcid"/>, <see cref="VendorStock.ArrowWcid"/>).</summary>
    UnsellableWeenie,
}

/// <summary>
/// The vendor the player has open (<see cref="GameState.Vendor"/>; M4 3.2 s1): the last GameEvent 0x0062 ApproachVendor, with ACE's price rules (<c>Vendor.GetSellCost</c> and <c>GetBuyCost</c>, W/Vendor.cs 573-609).
/// ACE sends a fresh 0062 after every trade, so a refresh replaces the object (a new <see cref="Revision"/>); the instance a caller holds never changes.
/// <para><b>Prices.</b> What the player pays is <see cref="PriceToBuy(VendorStockEntry, uint)"/>: per created stack <c>max(1, ceil(SellPrice x Value - 0.1))</c>, with the float arithmetic ACE uses (a Handy Healing Kit of Value 10 at 1.35 is 14,
/// a Potion of Healing of Value 170 is 230). What the vendor pays is <see cref="PayoutFor(uint, uint)"/>: <c>max(1, floor(BuyPrice x Value + 0.1))</c> on the item's whole stack Value, whatever amount the sale names (ACE sells and pays the whole stack,
/// W/Player_Commerce.cs 261, 296). A promissory note (ItemType 0x40000) is charged at 1.15 and paid at 1.0, ACE's own exception. Retail's price arithmetic is in <c>VendorBuyUI::UpdateTransactionValue</c> 11.6096 0x004C3D40, <c>VendorSellUI::UpdateTransactionValue</c> 0x004C41A0 and <c>gmVendorUI::UpdateTotalValue</c> 0x004C54D0 and is unread: <c>TODO(m4-retail-vendor-price)</c>.</para>
/// </summary>
public sealed class VendorStock
{
    /// <summary>ItemType.PromissoryNote: ACE prices it at fixed rates, whatever the vendor's.</summary>
    public const uint PromissoryNoteType = 0x00040000;

    /// <summary>ACE's fixed charge rate for a promissory note (<c>Vendor.GetSellCost</c>).</summary>
    public const float PromissoryNoteSellRate = 1.15f;

    /// <summary>ACE's fixed payout rate for a promissory note (<c>Vendor.GetBuyCost</c>).</summary>
    public const float PromissoryNoteBuyRate = 1.0f;

    /// <summary>The stock a reducer builds from a 0062. Only the session creates one.</summary>
    internal VendorStock(VendorApproachMsg message, long revision, double receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        Vendor = message.Vendor;
        Mask = message.MerchandiseItemTypes;
        MinValue = message.MerchandiseMinValue;
        MaxValue = message.MerchandiseMaxValue;
        DealMagicalItems = message.DealMagicalItems;
        BuyPrice = message.BuyPrice;
        SellPrice = message.SellPrice;
        AlternateCurrencyWcid = message.AlternateCurrencyWcid;
        AlternateCurrencyCount = message.AlternateCurrencyCount;
        AlternateCurrencyPluralName = message.AlternateCurrencyPluralName ?? string.Empty;
        var entries = ImmutableArray.CreateBuilder<VendorStockEntry>(message.Items.IsDefault ? 0 : message.Items.Length);
        if (!message.Items.IsDefault)
        {
            foreach (var item in message.Items)
            {
                entries.Add(new VendorStockEntry(item.StackSize, item.Guid, item.Desc));
            }
        }

        Entries = entries.MoveToImmutable();
        Revision = revision;
        ReceivedAt = receivedAt;
    }

    /// <summary>The vendor's guid.</summary>
    public uint Vendor { get; }

    /// <summary>
    /// The ItemType mask the vendor buys (<c>MerchandiseItemTypes</c>): what the hunt's sell rule reads (<c>m4-loot-rules-shop-mask</c>; capture C2 read 0x480467A7 off Renald's own 0062). An item is bought when
    /// <c>(ItemType &amp; Mask) != 0</c>, which is all ACE checks of the item on a sale (<c>Player.VerifySellItems</c>).
    /// </summary>
    public uint Mask { get; }

    /// <summary>The lowest Value the vendor buys (<c>MerchandiseMinValue</c>); ACE does not check it on a sale.</summary>
    public uint MinValue { get; }

    /// <summary>The highest Value the vendor buys (<c>MerchandiseMaxValue</c>); ACE does not check it on a sale.</summary>
    public uint MaxValue { get; }

    /// <summary>Whether the vendor deals in magical items.</summary>
    public bool DealMagicalItems { get; }

    /// <summary>The rate the vendor pays: a fraction of an item's Value.</summary>
    public float BuyPrice { get; }

    /// <summary>The rate the vendor charges: a multiple of an item's Value.</summary>
    public float SellPrice { get; }

    /// <summary>The weenie class of the alternate currency this vendor trades in, 0 for pyreals.</summary>
    public uint AlternateCurrencyWcid { get; }

    /// <summary>How many of the alternate currency the player holds (0 without one).</summary>
    public uint AlternateCurrencyCount { get; }

    /// <summary>The alternate currency's plural name (empty without one).</summary>
    public string AlternateCurrencyPluralName { get; }

    /// <summary>The stock, in the server's order (every row the 0062 carried, services included).</summary>
    public ImmutableArray<VendorStockEntry> Entries { get; }

    /// <summary>Rises by one with every 0062 the session receives, whichever vendor sent it: a tracker waiting for an open or a refresh compares it.</summary>
    public long Revision { get; }

    /// <summary>The session-clock time the 0062 arrived.</summary>
    public double ReceivedAt { get; }

    /// <summary>The row with this guid, or null.</summary>
    public VendorStockEntry? Find(uint guid)
    {
        foreach (var entry in Entries)
        {
            if (entry.Guid == guid)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>True when the vendor's mask takes items of <paramref name="itemType"/> (<c>(ItemType &amp; Mask) != 0</c>).</summary>
    public bool BuysType(uint itemType) => (Mask & itemType) != 0;

    /// <summary>True when the vendor takes the object: its ItemType is in the mask and its Value is at least 1 (ACE refuses a worthless item, "has no value and cannot be sold").</summary>
    public bool Buys(WorldObject item) => Refusal(item) == SaleRefusal.None;

    /// <summary>
    /// The first clause of <see cref="Buys"/> that refuses the object, in the order it tests them (the mask, the Value, then ACE's <see cref="UnsellableClause"/>), or <see cref="SaleRefusal.None"/> when the vendor takes it:
    /// a failure line names the clause rather than saying the item was not taken.
    /// </summary>
    /// <param name="item">The pack item.</param>
    public SaleRefusal Refusal(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!BuysType(item.ItemType))
        {
            return SaleRefusal.Mask;
        }

        if ((item.Weenie.Value ?? 0) < 1)
        {
            return SaleRefusal.Value;
        }

        return UnsellableClause(item);
    }

    /// <summary>The refusal as a failure line reads it: the clause, then the numbers it tested (the item's ItemType and Value, this vendor's mask, the Retained bit, the weenie class).</summary>
    /// <param name="item">The pack item.</param>
    public string DescribeRefusal(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var w = item.Weenie;
        return Refusal(item) switch
        {
            SaleRefusal.None => $"none: the mask 0x{Mask:X8} takes '{w.Name}' (ItemType 0x{item.ItemType:X}, Value {w.Value ?? 0})",
            SaleRefusal.Mask => $"mask: '{w.Name}' ItemType 0x{item.ItemType:X} & the mask 0x{Mask:X8} is 0",
            SaleRefusal.Value => $"value: '{w.Name}' has Value {w.Value ?? 0} (ACE refuses an item under 1)",
            SaleRefusal.Retained => $"retained: '{w.Name}' carries the Retained description bit (flags 0x{item.DescriptionFlags:X8})",
            _ => $"unsellable-list: '{w.Name}' is weenie class {w.WeenieClassId}, one ACE answers 'is unsellable' (IsSellable=0)",
        };
    }

    /// <summary>ACE's <c>ObjectDescriptionFlag.Retained</c> (0x01000000; ACE.Entity/Enum/ObjectDescriptionFlag.cs:32 @95ef426d): the weenie description bit for a retained item.</summary>
    public const uint RetainedFlag = 0x01000000;

    /// <summary>The Bread weenie class (wcid 259), refused live by Renald the Elder: "The Bread is unsellable." (M4 P5b journey 3h, a Bread of type 0x20 inside his mask 0x480467A7).</summary>
    public const uint BreadWcid = 259;

    /// <summary>The Arrow weenie class (wcid 31717), IsSellable=0 in the world database (research.md section 6's read-only SELECT); refused live: "The Arrows is unsellable." (M4 P5b live 3a, the archer's vendor station).</summary>
    public const uint ArrowWcid = 31717;

    /// <summary>
    /// True when ACE refuses the item whatever the vendor's mask says. F3-2: ACE's sale check (ACE.Server/WorldObjects/Player_Commerce.cs:268 @95ef426d, <c>VerifySellItems</c>) is
    /// <c>(acceptedItemTypes &amp; wo.ItemType) == 0 || !wo.IsSellable || wo.Retained</c>, answered with the 02EB "The X is unsellable." (:270) and the item skipped. The mask is <see cref="BuysType"/>; <c>Retained</c> is the weenie
    /// description bit <see cref="RetainedFlag"/>, read from the CreateObject; <c>IsSellable</c> (WorldObject_Properties.cs:1460) is a PropertyBool the wire does not carry for an unappraised item, so a weenie live-refused for it is listed
    /// by class (<see cref="BreadWcid"/>, <see cref="ArrowWcid"/>: both confirmed by a read-only SELECT of ace_world weenie_properties_bool type 69 (IsSellable) = 0 for wcids 259 and 31717, research.md section 6;
    /// <c>TODO(m4-vendor-issellable-wire)</c>: the wire still does not carry IsSellable, so add a class here per refusal the SELECT confirms).
    /// </summary>
    /// <param name="item">The item.</param>
    public static bool Unsellable(WorldObject item) => UnsellableClause(item) != SaleRefusal.None;

    /// <summary>The clause of <see cref="Unsellable"/> that refuses the item: <see cref="SaleRefusal.Retained"/> (the description bit), <see cref="SaleRefusal.UnsellableWeenie"/> (the class list), or <see cref="SaleRefusal.None"/>.</summary>
    /// <param name="item">The item.</param>
    public static SaleRefusal UnsellableClause(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if ((item.DescriptionFlags & RetainedFlag) != 0)
        {
            return SaleRefusal.Retained;
        }

        return UnsellableClass(item.Weenie.WeenieClassId) ? SaleRefusal.UnsellableWeenie : SaleRefusal.None;
    }

    /// <summary>
    /// True for a weenie class ACE answers "is unsellable" whatever the vendor's mask (<see cref="BreadWcid"/>, <see cref="ArrowWcid"/>: IsSellable 0 in the world database, see <see cref="Unsellable"/>). The class list's one
    /// statement: the rule book's guard <c>unsellable-class</c> reads it (M6 16 E5c2).
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool UnsellableClass(uint wcid) => wcid is BreadWcid or ArrowWcid;

    /// <summary>What one created stack of Value <paramref name="stackValue"/> and ItemType <paramref name="itemType"/> costs: <c>max(1, ceil(SellPrice x Value - 0.1))</c> in ACE's float arithmetic.</summary>
    public uint PriceOfStack(uint stackValue, uint itemType = 0)
    {
        var rate = itemType == PromissoryNoteType ? PromissoryNoteSellRate : SellPrice;
        var cost = Math.Ceiling((double)(rate * stackValue) - 0.1);
        return cost < 1 ? 1u : cost >= uint.MaxValue ? uint.MaxValue : (uint)cost;
    }

    /// <summary>
    /// What buying <paramref name="amount"/> of <paramref name="entry"/> costs: ACE creates the amount as stacks of at most the item's MaxStackSize (one object each for an item that does not stack) and charges
    /// <see cref="PriceOfStack"/> for each, so a stackable's price is the sum over its stacks, each stack at the unit Value times its size. 0 for an amount of 0.
    /// </summary>
    public uint PriceToBuy(VendorStockEntry entry, uint amount)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (amount == 0)
        {
            return 0;
        }

        ulong total = 0;
        var max = entry.MaxStackSize;
        var remaining = amount;
        while (remaining > 0)
        {
            var size = Math.Min(remaining, max);
            var stackValue = max > 1 ? (uint)Math.Round(entry.UnitValue * size) : entry.Value;
            total += PriceOfStack(stackValue, entry.ItemType);
            remaining -= size;
        }

        return total >= uint.MaxValue ? uint.MaxValue : (uint)total;
    }

    /// <summary>The price of <paramref name="amount"/> of the row with this guid, or null when the vendor has no such row (ACE skips an unknown guid silently).</summary>
    public uint? PriceToBuy(uint entryGuid, uint amount) => Find(entryGuid) is { } entry ? PriceToBuy(entry, amount) : null;

    /// <summary>What the vendor pays for an item of ItemType <paramref name="itemType"/> whose whole stack is worth <paramref name="stackValue"/>: <c>max(1, floor(BuyPrice x Value + 0.1))</c> in ACE's float arithmetic.</summary>
    public uint PayoutFor(uint itemType, uint stackValue)
    {
        var rate = itemType == PromissoryNoteType ? PromissoryNoteBuyRate : BuyPrice;
        var payout = Math.Floor((double)(rate * stackValue) + 0.1);
        return payout < 1 ? 1u : payout >= uint.MaxValue ? uint.MaxValue : (uint)payout;
    }

    /// <summary>What the vendor pays for the object, its whole stack at its current Value (a stack of five is paid and taken whole whatever the sale names).</summary>
    public uint PayoutFor(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return PayoutFor(item.ItemType, item.Weenie.Value ?? 0);
    }
}
