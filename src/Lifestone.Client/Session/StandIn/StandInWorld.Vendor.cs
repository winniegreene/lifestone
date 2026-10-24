// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Vendor.cs (ApproachVendor 284-298, BuyItems_ValidateTransaction 450-569, GetSellCost / GetBuyCost 571-597, ProcessItemsForPurchase 610-666),
// Source/ACE.Server/WorldObjects/Player_Commerce.cs (HandleActionBuyItem 22-57, FinalizeBuyTransaction 59-112, HandleActionSellItem 124-236, VerifySellItems 242-320, SpendCurrency / CollectCurrencyStacks 337-393),
// Source/ACE.Server/WorldObjects/Player_Inventory.cs (TryConsumeFromInventoryWithNetworking 117-166) and Source/ACE.Server/Network/GameEvent/Events/GameEventApproachVendor.cs (AGPL-3.0)
// [with changes: the order and the lengths are the ones the live capture records (cache/m4/3-2/capture.md C1 and C2, capture1.log); the stand-in keeps the stock, the coins and the sold items itself; there are no service rows,
// no weights, no pack-space limits, no range check and no vendor emote].
using Lifestone.Client.Diagnostics;
using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// One item a stand-in vendor lists or the player holds for the vendor checks, as the world database (v0.9.296) has it. The look is the placeholder item model and icon (<c>TODO(m4-standin-vendor-model)</c>).
/// </summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The item's name.</param>
/// <param name="ItemType">ACE's <c>ItemType</c> (<c>PropertyInt</c> 1).</param>
/// <param name="Value">The Value of one (<c>PropertyInt</c> 19).</param>
/// <param name="Burden">The burden of one (<c>EncumbranceVal</c>, 5).</param>
/// <param name="MaxStack">The stack limit (<c>MaxStackSize</c>, 11), or null for an item that does not stack.</param>
/// <param name="Uses">The uses of a healing kit (<c>MaxStructure</c>, 91), or null.</param>
internal sealed record StandInVendorItem(uint Wcid, string Name, uint ItemType, uint Value, ushort Burden, int? MaxStack = null, int? Uses = null)
{
    /// <summary>The ItemType of a Promissory Note (the trade notes): ACE prices them at 1.15 and 1.0 whatever the vendor's rates.</summary>
    internal const uint PromissoryNote = 0x40000;
}

// M4 3.2 s3: the two Holtburg vendors the stand-in answers for (TODO(m4-standin-vendor), closed): Thelnoth Cort the Healer and Shopkeeper Renald the Elder at their real cells and positions, the 0062 with their rates, their mask and a stock
// from the world database, a buy and a sell with the price rules and the coins, in the order of the live capture. They are the only NPCs the stand-in adds (D23); their look is a placeholder.
internal sealed partial class StandInWorld
{
    // --- the constants ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The guids of the vendors (the live capture's: Thelnoth 0x7A9B4027, Renald 0x7A9B4021) and the first guid of each one's stock rows (the stand-in's own range).</summary>
    internal const uint ThelnothGuid = 0x7A9B4027, RenaldGuid = 0x7A9B4021, ThelnothRowGuid = 0x80090000, RenaldRowGuid = 0x800A0000;

    /// <summary>The weenie class ids of the vendors (the world database).</summary>
    internal const uint ThelnothWcid = 715, RenaldWcid = 718;

    /// <summary>The object description bit of a vendor (ACE <c>ObjectDescriptionFlags.Vendor</c>).</summary>
    internal const uint VendorFlag = 0x200;

    /// <summary>The weenie class id of the pyreal coin stack, its stack limit and the guid of the stack the player is announced with.</summary>
    internal const uint PyrealWcid = 273, PyrealMaxStack = 25000, PyrealGuid = 0x80080000;

    /// <summary>The pyreals the player is announced with: <c>TODO(m4-standin-pyreals)</c>, a placeholder (the Holtburg Hunter holds the same 10,000).</summary>
    internal const int PlaceholderPyreals = 10_000;

    /// <summary>The guids of the two items the player is announced with for the vendors: a Handy Healing Kit (Thelnoth buys it) and Leather Bracers (Renald buys them, Thelnoth refuses them).</summary>
    internal const uint SellKitGuid = 0x80080001, SellBracersGuid = 0x80080002;

    /// <summary>ACE's <c>PropertyInt.CoinValue</c> (20): the pyreals the player holds, in the description and in every 02CD after a spend or a payout.</summary>
    internal const uint CoinValueProperty = 20;

    /// <summary>ACE's <c>WeenieError.NoObject</c> (0x0038): the UseDone of a trade with a vendor the world does not hold.</summary>
    internal const uint NoObject = 0x0038;

    /// <summary>The vendors' rates (<c>BuyPrice</c> what a vendor pays, <c>SellPrice</c> what it charges; the world database, floats 37 and 38) and the highest Value they buy (<c>MerchandiseMaxValue</c>).</summary>
    internal const float VendorBuyRate = 0.9f, VendorSellRate = 1.35f;

    /// <summary>The masks of Thelnoth (Misc, Key, PromissoryNote) and Renald (capture C2 confirmed the shard's value).</summary>
    internal const uint ThelnothMask = 0x44080, RenaldMask = 0x480467A7;

    /// <summary>The sound the buy and the sell play (<c>Sound.PickUpItem</c>, capture C1: F750 0x8F).</summary>
    private const uint SoundPickUpItem = 0x8F;

    /// <summary>How long after the Use's turn the UseDone comes (capture C1: 833 ms for Thelnoth, C2: 964 ms for Renald; the turn itself came 8 ms after the send).</summary>
    internal const double ThelnothOpenDoneSeconds = 0.825, RenaldOpenDoneSeconds = 0.956;

    /// <summary>How long after the UseDone the 0062 comes (capture C1: 1.04 s, C2: 0.65 s).</summary>
    internal const double ThelnothApproachLagSeconds = 1.043, RenaldApproachLagSeconds = 0.650;

    /// <summary>How long after a buy's first bundle the refreshed 0062 and the UseDone come (capture C1: +3 ms), and the new item's F745 and the sound after them (+3 ms).</summary>
    internal const double BuyApproachLagSeconds = 0.003, BuyObjectLagSeconds = 0.003;

    /// <summary>How long after a sale's bundle the payout stack's F745 and the sound come (capture C1: +5 ms).</summary>
    internal const double SellObjectLagSeconds = 0.005;

    // --- the catalogue (the world database, v0.9.296) -----------------------------------------------------------------------------------------------------------------------------------------------------------

    private const uint Misc = 0x80, Melee = 0x1, Armor = 0x2, Clothing = 0x4, Food = 0x20, Money = 0x40, Missile = 0x100;

    /// <summary>The items by class id: Thelnoth's potions, kits and notes, a subset of Renald's shop (<c>TODO(m4-standin-vendor-stock)</c>), the player's coin and Leather Bracers. Names, ItemTypes, Values, burdens, stacks and uses are the SELECTs'.</summary>
    internal static IReadOnlyDictionary<uint, StandInVendorItem> VendorCatalog { get; } = new StandInVendorItem[]
    {
        new(2457, "Health Draught", Misc, 85, 5, 100),
        new(377, "Potion of Healing", Misc, 170, 15, 100),
        new(2460, "Mana Draught", Misc, 85, 5, 100),
        new(379, "Mana Potion", Misc, 170, 15, 100),
        new(378, "Stamina Potion", Misc, 75, 15, 100),
        new(27326, "Stamina Tincture", Misc, 100, 50, 100),
        new(628, "Handy Healing Kit", Misc, 10, 50, null, 20),
        new(629, "Adept Healing Kit", Misc, 50, 50, null, 25),
        new(2621, "Trade Note (100)", StandInVendorItem.PromissoryNote, 100, 1, 250),
        new(2622, "Trade Note (500)", StandInVendorItem.PromissoryNote, 500, 1, 250),
        new(300, "Arrow", Missile, 1, 5, 1000),
        new(309, "Club", Melee, 260, 675),
        new(352, "Short Sword", Melee, 160, 350),
        new(307, "Shortbow", Missile, 200, 450),
        new(36, "Leather Bracers", Armor, 1100, 270),
        new(115, "Leather Boots", Armor, 1100, 420),
        new(130, "Shirt", Clothing, 15, 75),
        new(258, "Apple", Food, 7, 50, 100),
        new(PyrealWcid, "Pyreal", Money, 1, 0, (int)PyrealMaxStack),
    }.ToDictionary(i => i.Wcid);

    /// <summary>Thelnoth's stock in the order the capture's first rows show (C1), then the rest of his shop rows from the SELECT (<c>TODO(m4-standin-vendor-stock)</c>: his 31 service rows, a buff cast on the buyer, are left out).</summary>
    private static readonly uint[] ThelnothStock = [2457, 377, 2460, 379, 378, 27326, 628, 629, 2621, 2622];

    /// <summary>
    /// Renald's stock: a labelled subset of his shop rows (<c>TODO(m4-standin-vendor-stock)</c>: his real shop has 51 rows): arrows, a club (the Mace skill's weapon class), a sword, a bow, leather armour, an apple and a torch-free shortlist, and a Shirt. The
    /// Shirt is not one of his shop rows in the database (wcid 130 is worn by him, not sold); it is here so the stand-in has a piece of clothing to buy.
    /// </summary>
    private static readonly uint[] RenaldStock = [300, 309, 352, 307, 36, 115, 258, 130];

    private sealed class StandInVendor
    {
        public required uint Guid { get; init; }

        public required string Name { get; init; }

        public required uint Wcid { get; init; }

        public required uint Cell { get; init; }

        public required Vector3 At { get; init; }

        public required Quaternion Facing { get; init; }

        public required uint Mask { get; init; }

        public required double OpenDoneSeconds { get; init; }

        public required double ApproachLagSeconds { get; init; }

        /// <summary>True when the refreshed 0062 of a sale follows the UseDone (capture C2, the 53-row Renald) rather than comes before the payout (capture C1, the 38-row Thelnoth).</summary>
        public required bool ApproachAfterSale { get; init; }

        public required uint[] Stock { get; init; }

        public required uint FirstRow { get; init; }

        /// <summary>The default rows (unlimited) by guid.</summary>
        public Dictionary<uint, StandInVendorItem> Rows { get; } = [];

        /// <summary>The rows in the order the 0062 lists them: the default rows, then the unique items the player sold (guid to the item's description).</summary>
        public List<uint> Order { get; } = [];

        public Dictionary<uint, WeenieDesc> Uniques { get; } = [];

    }

    private Dictionary<uint, StandInVendor>? _vendors;

    private Dictionary<uint, StandInVendor> Vendors => _vendors ??= BuildVendors();

    private static Dictionary<uint, StandInVendor> BuildVendors()
    {
        var thelnoth = new StandInVendor
        {
            Guid = ThelnothGuid,
            Name = "Thelnoth Cort the Healer",
            Wcid = ThelnothWcid,
            Cell = 0xA9B40111,
            At = new Vector3(30.0196f, 159.683f, 66.005f),
            Facing = new Quaternion(0f, 0f, 0.070627f, 0.997503f),
            Mask = ThelnothMask,
            OpenDoneSeconds = ThelnothOpenDoneSeconds,
            ApproachLagSeconds = ThelnothApproachLagSeconds,
            ApproachAfterSale = false,
            Stock = ThelnothStock,
            FirstRow = ThelnothRowGuid,
        };
        var renald = new StandInVendor
        {
            Guid = RenaldGuid,
            Name = "Shopkeeper Renald the Elder",
            Wcid = RenaldWcid,
            Cell = 0xA9B40141,
            At = new Vector3(139.234f, 18.3293f, 94.005f),
            Facing = new Quaternion(0f, 0f, -0.160311f, 0.987067f),
            Mask = RenaldMask,
            OpenDoneSeconds = RenaldOpenDoneSeconds,
            ApproachLagSeconds = RenaldApproachLagSeconds,
            ApproachAfterSale = true,
            Stock = RenaldStock,
            FirstRow = RenaldRowGuid,
        };
        foreach (var vendor in new[] { thelnoth, renald })
        {
            var guid = vendor.FirstRow;
            foreach (var wcid in vendor.Stock)
            {
                vendor.Rows[guid] = VendorCatalog[wcid];
                vendor.Order.Add(guid);
                guid++;
            }
        }

        return new Dictionary<uint, StandInVendor> { [thelnoth.Guid] = thelnoth, [renald.Guid] = renald };
    }

    // --- the login burst: the vendors and what the player holds for them ---------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The tag appended to a stand-in vendor's name on screen: the body, the stock and the purse are placeholders, the name is the real vendor's.</summary>
    internal const string VendorNameTag = " (stand-in placeholder)";

    /// <summary>The text of the placeholders the vendors and the player's purse are (logged once at the login burst).</summary>
    internal const string VendorsPlaceholder = "the stand-in holds Thelnoth Cort the Healer and Shopkeeper Renald the Elder at their real cells and positions with the human placeholder model (TODO(m4-standin-vendor-model)), a short stock from the world database (TODO(m4-standin-vendor-stock): Renald's is a subset, no service rows) and a player purse of 10,000 pyreals with a Handy Healing Kit and Leather Bracers to sell (TODO(m4-standin-pyreals): placeholder amounts)";

    /// <summary>
    /// The CreateObjects of the two vendors for a login burst (a visit that asks, <see cref="StandInVisit.Vendors"/>): each at its real cell and position, a human placeholder, named by its real name, with the Vendor description bit
    /// and its weenie class id. The D23 rule: these are the only NPCs the stand-in adds, and they stand inside their shops.
    /// </summary>
    /// <param name="visit">The visit (its human look).</param>
    internal static IEnumerable<CreateObjectMsg> VendorObjects(StandInVisit visit)
    {
        foreach (var vendor in BuildVendors().Values)
        {
            // The object is named with the placeholder tag (3.2-R3), so the vendor window's title and the nameplate say it is the stand-in's; the look stays keyed by the real name.
            var kind = new StandInObject(vendor.Guid, vendor.Name + VendorNameTag, default, DescriptionFlags: VendorFlag, WeenieClassId: vendor.Wcid);
            yield return StandInSessions.ObjectMessage(kind, new AcPosition(vendor.Cell, vendor.At, vendor.Facing), visit.LookFor(vendor.Name));
        }
    }

    /// <summary>
    /// The items the player is announced with for the vendors (<c>TODO(m4-standin-pyreals)</c>): a pyreal stack of <see cref="PlaceholderPyreals"/>, a Handy Healing Kit and Leather Bracers in the main pack, listed in the description's
    /// pack and created right after the player. Merged after <paramref name="before"/>'s own.
    /// </summary>
    /// <param name="before">The items already announced (the heal items).</param>
    /// <param name="player">The stand-in player's guid.</param>
    internal static HealItemBurst WithVendorItems(HealItemBurst before, uint player)
    {
        var coin = Announce(PyrealGuid, VendorCatalog[PyrealWcid], player, PlaceholderPyreals);
        var kit = Announce(SellKitGuid, VendorCatalog[HandyKitWcid], player, 1);
        var bracers = Announce(SellBracersGuid, VendorCatalog[36], player, 1);
        return new HealItemBurst(
            before.Listed.Add(new ContentDesc(PyrealGuid, 0)).Add(new ContentDesc(SellKitGuid, 0)).Add(new ContentDesc(SellBracersGuid, 0)),
            before.Objects.Add(coin).Add(kit).Add(bracers));
    }

    private static CreateObjectMsg Announce(uint guid, StandInVendorItem item, uint player, int stack) =>
        new(
            guid,
            ModelData.Empty,
            new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal, SetupId = StandInWeenieArt.SetupOr(item.Wcid, PlaceholderItemSetup), Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) },
            ItemDesc(item, stack, player));

    /// <summary>
    /// The WeenieDesc of an item of the catalogue as a stock row or a pack object shows it: name, class id, the weenie's own icon (<see cref="StandInWeenieArt"/>), ItemType, the Value of the whole stack and the burden of it, the stack limit of a stacking item (and
    /// its size when <paramref name="stack"/> is above 0), a kit's uses; <paramref name="container"/> names where it lies (the player for a pack object, null for a row).
    /// </summary>
    private static WeenieDesc ItemDesc(StandInVendorItem item, int stack, uint? container, ILogSink? log = null)
    {
        var header = WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden;
        if (container is not null)
        {
            header |= WeenieHeaderFlags.Container;
        }

        if (item.MaxStack is not null)
        {
            header |= WeenieHeaderFlags.MaxStackSize;
            if (stack > 0)
            {
                header |= WeenieHeaderFlags.StackSize;
            }
        }

        if (item.Uses is not null)
        {
            header |= WeenieHeaderFlags.Structure | WeenieHeaderFlags.MaxStructure;
        }

        var count = Math.Max(stack, 1);
        return StandInWeenieArt.Apply(new WeenieDesc
        {
            Header = header,
            Name = item.Name,
            WeenieClassId = item.Wcid,
            IconId = StandInWeenieArt.PlaceholderIcon,
            ItemType = item.ItemType,
            ObjectDescriptionFlags = 0,
            ContainerId = container,
            Value = item.Value * (uint)count,
            Burden = (ushort)Math.Min(item.Burden * count, ushort.MaxValue),
            StackSize = item.MaxStack is not null && stack > 0 ? (ushort)Math.Min(stack, ushort.MaxValue) : null,
            MaxStackSize = item.MaxStack is { } max ? (ushort)Math.Min(max, ushort.MaxValue) : null,
            Structure = item.Uses is { } uses ? (ushort)uses : null,
            MaxStructure = item.Uses is { } maxUses ? (ushort)maxUses : null,
        }, log);
    }

    // --- the player's purse -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private readonly List<(uint Guid, int Size)> _coins = [];
    private bool _purseReady;
    private readonly StandInShortcutList _bar = new();

    /// <summary>The coin stacks the player holds, in the order ACE spends them (the stand-in's ledger; tests).</summary>
    internal IReadOnlyList<(uint Guid, int Size)> CoinStacks
    {
        get
        {
            EnsurePurse();
            return _coins;
        }
    }

    /// <summary>The pyreals the player holds (the sum of the stacks).</summary>
    internal int Coins
    {
        get
        {
            EnsurePurse();
            return _coins.Sum(c => c.Size);
        }
    }

    private void EnsurePurse()
    {
        if (_purseReady)
        {
            return;
        }

        _purseReady = true;
        if (_visit.Vendors)
        {
            _coins.Add((PyrealGuid, PlaceholderPyreals));
        }
    }

    /// <summary>Test lever: the player's first coin stack holds exactly <paramref name="pyreals"/> from now on, the other stacks are gone (no message goes out; the client's model keeps what it was told).</summary>
    internal void SetCoins(int pyreals)
    {
        EnsurePurse();
        _coins.Clear();
        _coins.Add((PyrealGuid, pyreals));
    }

    /// <summary>Every vendor message the stand-in has sent, in order (tests): the 0062 records.</summary>
    internal List<VendorApproachMsg> Approaches { get; } = [];

    /// <summary>
    /// The description as the stand-in sends it with what it holds: the shortcut list the player has added and removed since the login (a later description in the same visit, or a replayed login, carries it) and, for a visit with
    /// vendors, the pyreals the player holds (<c>CoinValue</c>, 20). Unchanged when the stand-in has nothing to add.
    /// </summary>
    /// <param name="description">The description about to be sent.</param>
    internal PlayerDescriptionMsg WithVendorState(PlayerDescriptionMsg description)
    {
        description = _bar.Apply(description);
        if (_visit.Vendors)
        {
            description = description with { IntProperties = description.IntProperties.SetItem(CoinValueProperty, Coins) };
        }

        return description;
    }

    // --- the open ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool TryVendor(uint guid, out StandInVendor vendor)
    {
        vendor = null!;
        return _visit.Vendors && Vendors.TryGetValue(guid, out vendor!);
    }

    /// <summary>
    /// A Use of a vendor (capture C1 and C2): busy is UseDone 0x001D; otherwise the player's turn (F74C, <see cref="DelaySeconds"/> after the send; the stand-in is busy until the UseDone), the UseDone 0.825 s (Thelnoth) or 0.956 s (Renald) after it
    /// and the 0062 1.04 s or 0.65 s after the UseDone. The range check, the vendor's own turn and its emote (the 02BD after every 0062, <c>TODO(m4-ge-02bd)</c>) are not modelled (<c>TODO(m4-vendor-approach)</c>).
    /// </summary>
    private OutboundTicket OpenVendor(StandInVendor vendor)
    {
        if (Busy)
        {
            return RefuseUse(YoureTooBusy);
        }

        Placeholder("m4-vendor-approach", "a Use of a vendor is answered wherever the player stands: the stand-in has no range check or walk to the vendor (a check places the player inside the shop)");
        Placeholder("m4-ge-02bd", "ACE sends GameEvent 0x02BD after every 0062 (the vendor's emote or open notice, unknown to Lifestone); the stand-in sends none");
        var start = _reducer.Clock.Now + DelaySeconds;
        var done = start + vendor.OpenDoneSeconds;
        _busyUntil = Math.Max(_busyUntil, done);
        var schedule = _reducer.StandIn!;
        schedule.At(start, sink =>
        {
            Placeholder("m4-standin-vendor-turn", "the player's turn toward a vendor is an UpdateMotion with the stance and no command: the stand-in has no motion table for TurnToObject");
            SendPlayerMotion(sink, StanceNow(), MotionCommands.Ready);
        });
        schedule.At(done, sink => UseDone(sink));
        schedule.At(done + vendor.ApproachLagSeconds, sink => SendApproach(sink, vendor));
        return Ticket();
    }

    private void SendApproach(IAcSessionSink sink, StandInVendor vendor)
    {
        var rows = ImmutableArray.CreateBuilder<VendorEntry>(vendor.Order.Count);
        foreach (var guid in vendor.Order)
        {
            if (vendor.Rows.TryGetValue(guid, out var item))
            {
                rows.Add(new VendorEntry(VendorEntry.Unlimited, guid, ItemDesc(item, 0, null, Log)));
            }
            else if (vendor.Uniques.TryGetValue(guid, out var unique))
            {
                rows.Add(new VendorEntry(1, guid, unique));
            }
        }

        var message = new VendorApproachMsg(vendor.Guid, vendor.Mask, 0, 1_000_000, true, VendorBuyRate, VendorSellRate, 0, 0, string.Empty, rows.MoveToImmutable());
        Approaches.Add(message);
        AnswerLog.Add("0062");
        sink.OnApproachVendor(Context(sink), Header(0x0062), message);
    }

    // --- the prices ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>What a vendor charges for a created stack whose Value is <paramref name="value"/> (<c>Vendor.GetSellCost</c> 576-585): <c>max(1, ceil(SellPrice x Value - 0.1))</c>, with a Promissory Note's 1.15 in place of the rate.</summary>
    internal static uint SellCost(uint value, uint itemType)
    {
        var rate = itemType == StandInVendorItem.PromissoryNote ? 1.15f : VendorSellRate;
        return Math.Max(1u, (uint)Math.Ceiling((rate * value) - 0.1));
    }

    /// <summary>What a vendor pays for an item whose Value (the whole stack's) is <paramref name="value"/> (<c>Vendor.GetBuyCost</c> 590-597): <c>max(1, floor(BuyPrice x Value + 0.1))</c>, with a Promissory Note's 1.0 in place of the rate.</summary>
    internal static int BuyCost(uint value, uint itemType)
    {
        var rate = itemType == StandInVendorItem.PromissoryNote ? 1.0f : VendorBuyRate;
        return Math.Max(1, (int)Math.Floor((rate * value) + 0.1));
    }

    // --- a buy ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A created item of a buy: the guid it gets, its description and its place.</summary>
    private sealed record Bought(uint Guid, StandInVendorItem Item, int Stack, WeenieDesc? Desc, uint Cost);

    /// <summary>
    /// A Buy (GA 0x005F; <c>Player_Commerce.HandleActionBuyItem</c> 22-57, <c>Vendor.BuyItems_ValidateTransaction</c> 450-569). Busy (decided when it is sent) is 00A0 {the player} then UseDone 0x001D; a vendor the world does not hold is 00A0 and
    /// UseDone 0x0038; an amount of 0 or less is the line "Invalid amount", 00A0 and UseDone 0; too few pyreals is 00A0 and UseDone 0, no text (<c>Vendor.cs</c> 546-552). Otherwise, in the order of capture C1 (one bundle): for each coin stack spent the 0197 (or
    /// 0024 when it empties), the burden 02CD and the CoinValue 02CD, for each created item the 0022 and the burden 02CD; 3 ms later the refreshed 0062 and the UseDone 0; 3 ms after that each new item's F745 and the sound.
    /// A stacking item is created in stacks of its limit, each priced on its own Value; a row the vendor does not list is left out.
    /// </summary>
    internal OutboundTicket Buy(uint vendorGuid, IReadOnlyList<(uint item, uint amount)> items)
    {
        if (Busy)
        {
            BusyRefusals++;
            return TradeRefused(YoureTooBusy, null);
        }

        Later(sink => BuyNow(sink, vendorGuid, items));
        return Ticket();
    }

    private OutboundTicket TradeRefused(uint error, string? line)
    {
        Later(sink => EndTrade(sink, error, line));
        return Ticket();
    }

    /// <summary>The refusal every trade failure ends with: the optional line, GameEvent 0x00A0 {the player} (<c>GameEventInventoryServerSaveFailed</c>), then the UseDone.</summary>
    private void EndTrade(IAcSessionSink sink, uint error, string? line)
    {
        if (line is not null)
        {
            TransientLine(sink, line);
        }

        Failed(sink, _reducer.State.Self.Guid);
        UseDone(sink, error);
    }

    private void TransientLine(IAcSessionSink sink, string text)
    {
        AnswerLog.Add("02EB");
        sink.OnTransientString(Context(sink), Header(0x02EB), new TransientStringEvent(text));
    }

    private void BuyNow(IAcSessionSink sink, uint vendorGuid, IReadOnlyList<(uint item, uint amount)> items)
    {
        if (!TryVendor(vendorGuid, out var vendor))
        {
            EndTrade(sink, NoObject, null);
            return;
        }

        var created = new List<Bought>();
        foreach (var (guid, amount) in items)
        {
            if (amount is 0 or > int.MaxValue)
            {
                EndTrade(sink, 0, "Invalid amount"); // Vendor.cs 458-463: the whole transaction is rejected
                return;
            }

            if (vendor.Rows.TryGetValue(guid, out var row))
            {
                var remaining = (int)amount;
                while (remaining > 0)
                {
                    var stack = row.MaxStack is { } max ? Math.Min(remaining, max) : 1;
                    var desc = ItemDesc(row, stack, null, Log);
                    created.Add(new Bought(_nextItemGuid++, row, stack, null, SellCost(desc.Value ?? 0, row.ItemType)));
                    remaining -= stack;
                }
            }
            else if (vendor.Uniques.TryGetValue(guid, out var unique))
            {
                var item = VendorCatalog.GetValueOrDefault(unique.WeenieClassId) ?? new StandInVendorItem(unique.WeenieClassId, unique.Name, unique.ItemType, unique.Value ?? 0, unique.Burden ?? 0);
                created.Add(new Bought(guid, item, 1, unique, SellCost(unique.Value ?? 0, unique.ItemType)));
            }
        }

        var total = created.Aggregate(0L, (sum, b) => sum + b.Cost);
        if (total > Coins)
        {
            EndTrade(sink, 0, null); // Vendor.cs 546-552: too few pyreals, no text
            return;
        }

        Placeholder("m4-standin-vendor-limits", "a buy checks the pyreals only: the stand-in has no weights and no pack-space limit, so ACE's 'too encumbered' and 'not enough pack space' refusals are not modelled");
        SpendCoins(sink, (int)total);
        var objects = new List<CreateObjectMsg>();
        foreach (var bought in created)
        {
            var shown = bought.Desc ?? ItemDesc(bought.Item, bought.Stack, null, Log);
            var desc = shown with { ContainerId = _reducer.State.Self.Guid, Header = shown.Header | WeenieHeaderFlags.Container };
            if (bought.Desc is not null)
            {
                vendor.Uniques.Remove(bought.Guid);
                vendor.Order.Remove(bought.Guid);
            }

            Receive(sink, bought.Guid, bought.Item, bought.Stack, desc, objects);
        }

        var schedule = _reducer.StandIn!;
        schedule.At(_reducer.Clock.Now + BuyApproachLagSeconds, later =>
        {
            SendApproach(later, vendor);
            UseDone(later);
        });
        schedule.At(_reducer.Clock.Now + BuyApproachLagSeconds + BuyObjectLagSeconds, later =>
        {
            foreach (var created in objects)
            {
                AnswerLog.Add("F745");
                later.OnCreateObject(Context(later), created, isUpdate: false);
            }

            PlaySound(later, _reducer.State.Self.Guid, SoundPickUpItem, 1f);
        });
    }

    /// <summary>
    /// Puts a created item in the player's main pack (<c>TryCreateInInventoryWithNetworking</c>): the stand-in's entry, the 0022 into the next slot and the burden 02CD; the CreateObject is held in <paramref name="objects"/> for the bundle that follows.
    /// A pyreal stack also tells the new CoinValue (<c>UpdateCoinValue</c>, in the same call).
    /// </summary>
    private void Receive(IAcSessionSink sink, uint guid, StandInVendorItem item, int stack, WeenieDesc desc, List<CreateObjectMsg> objects)
    {
        var owner = _reducer.State.Self.Guid;
        var placement = (uint)(_pack.Count + _stacks.Count);
        if (item.Wcid == PyrealWcid)
        {
            _coins.Add((guid, stack));
        }
        else
        {
            _pack.Add(new StandInPackEntry(guid, item.Wcid, item.Name, stack));
        }

        AnswerLog.Add("0022");
        sink.OnInventoryPutObjInContainer(Context(sink), Header(0x0022), new InventoryPutObjInContainerEvent(guid, owner, placement, 0));
        BurdenNow(sink, desc.Burden ?? 0);
        if (item.Wcid == PyrealWcid)
        {
            CoinValueNow(sink);
        }

        objects.Add(new CreateObjectMsg(
            guid,
            ModelData.Empty,
            new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal, SetupId = StandInWeenieArt.SetupOr(item.Wcid, PlaceholderItemSetup), Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) },
            desc));
    }

    private void CoinValueNow(IAcSessionSink sink)
    {
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CoinValueProperty, Coins);
    }

    /// <summary>
    /// Takes <paramref name="amount"/> pyreals out of the stacks in order (<c>SpendCurrency</c>, <c>TryConsumeFromInventoryWithNetworking</c> 149-166): a stack that is used up is removed (0024), another is reduced (0197 with the stack and its Value), then the burden 02CD and
    /// the CoinValue 02CD, per stack touched.
    /// </summary>
    private void SpendCoins(IAcSessionSink sink, int amount)
    {
        var left = amount;
        for (var i = 0; i < _coins.Count && left > 0;)
        {
            var (guid, size) = _coins[i];
            var take = Math.Min(left, size);
            left -= take;
            if (take == size)
            {
                _coins.RemoveAt(i);
                AnswerLog.Add("0024");
                sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(guid));
            }
            else
            {
                _coins[i] = (guid, size - take);
                AnswerLog.Add("0197");
                sink.OnSetStackSize(Context(sink), new SetStackSizeMsg(NextSequence(guid, StackSizeTag), guid, (uint)(size - take), (uint)(size - take)));
                i++;
            }

            BurdenNow(sink, 0);
            CoinValueNow(sink);
        }
    }

    // --- a sale --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The Value of an item the player holds, the whole stack's: the world object's, else the catalogue's times the stack, else 0.</summary>
    private uint ValueOf(StandInPackEntry entry) =>
        _reducer.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.Value is { } v ? v : VendorCatalog.TryGetValue(entry.Wcid, out var item) ? item.Value * (uint)Math.Max(entry.Stack, 1) : 0;

    private uint ItemTypeOf(StandInPackEntry entry) =>
        _reducer.State.Objects.TryGet(entry.Guid, out var o) ? o.Weenie.ItemType : VendorCatalog.TryGetValue(entry.Wcid, out var item) ? item.ItemType : Misc;

    private static string PluralOf(string name) => name.EndsWith('s') ? name : name + "s";

    /// <summary>
    /// A Sell (GA 0x0060; <c>Player_Commerce.HandleActionSellItem</c> 124-236, <c>VerifySellItems</c> 242-320). Busy (decided when it is sent) is 00A0 then UseDone 0x001D. Each item is checked, a bad one is skipped with its line: an item outside the vendor's
    /// mask is "The X is unsellable." (capture C2: 02EB), a Value below 1 is "The X has no value and cannot be sold."; an item the player does not hold, a duplicate and an amount beyond the stack are skipped silently. With nothing left the answer is 00A0 and
    /// UseDone 0. Otherwise, in the order of capture C1: per item the burden 02CD and the 0022 into the vendor; the refreshed 0062; per payout stack the 0022, the burden 02CD and the CoinValue 02CD; the UseDone 0; 5 ms later each payout stack's F745 and the sound.
    /// The 0062 of a vendor with <c>ApproachAfterSale</c> (Renald, capture C2) follows the UseDone instead. The vendor pays on the whole stack's Value whatever the amount (<c>Vendor.CalculatePayoutCoinAmount</c>), in stacks of 25,000.
    /// </summary>
    internal OutboundTicket Sell(uint vendorGuid, IReadOnlyList<(uint item, uint amount)> items)
    {
        if (Busy)
        {
            BusyRefusals++;
            return TradeRefused(YoureTooBusy, null);
        }

        Later(sink => SellNow(sink, vendorGuid, items));
        return Ticket();
    }

    private void SellNow(IAcSessionSink sink, uint vendorGuid, IReadOnlyList<(uint item, uint amount)> items)
    {
        if (!TryVendor(vendorGuid, out var vendor))
        {
            EndTrade(sink, NoObject, null);
            return;
        }

        var verified = new List<StandInPackEntry>();
        foreach (var (guid, amount) in items)
        {
            if (!InPack(guid, out var entry) || verified.Any(v => v.Guid == guid) || amount is 0 or > int.MaxValue || amount > Math.Max(entry.Stack, 1))
            {
                continue;
            }

            var name = entry.Stack > 1 ? PluralOf(entry.Name) : entry.Name;
            if ((vendor.Mask & ItemTypeOf(entry)) == 0)
            {
                TransientLine(sink, "The " + name + " is unsellable."); // Player_Commerce.cs 291-298: the retail line carried no name
                continue;
            }

            if (ValueOf(entry) < 1)
            {
                TransientLine(sink, "The " + name + " has no value and cannot be sold.");
                continue;
            }

            verified.Add(entry);
        }

        if (verified.Count == 0)
        {
            Failed(sink, _reducer.State.Self.Guid);
            UseDone(sink);
            return;
        }

        var payout = verified.Sum(e => BuyCost(ValueOf(e), ItemTypeOf(e)));
        var stacks = new List<int>();
        for (var left = payout; left > 0; left -= (int)Math.Min(left, PyrealMaxStack))
        {
            stacks.Add((int)Math.Min(left, PyrealMaxStack));
        }

        foreach (var entry in verified)
        {
            var weenie = _reducer.State.Objects.TryGet(entry.Guid, out var o) ? o.Weenie : null;
            var burden = weenie?.Burden ?? (VendorCatalog.TryGetValue(entry.Wcid, out var known) ? known.Burden * Math.Max(entry.Stack, 1) : 0);
            RemoveEntry(entry.Guid);
            BurdenNow(sink, -burden);
            AnswerLog.Add("0022");
            sink.OnInventoryPutObjInContainer(Context(sink), Header(0x0022), new InventoryPutObjInContainerEvent(entry.Guid, vendorGuid, 0, 0));
            if (weenie is not null && weenie.MaxStackSize is null && weenie.MaxStructure is null)
            {
                vendor.Uniques[entry.Guid] = weenie with { ContainerId = vendorGuid, Header = weenie.Header | WeenieHeaderFlags.Container }; // ProcessItemsForPurchase 610-660: an item that neither stacks nor has uses is resold
                vendor.Order.Add(entry.Guid);
            }
        }

        if (!vendor.ApproachAfterSale)
        {
            SendApproach(sink, vendor);
        }

        var objects = new List<CreateObjectMsg>();
        var coin = VendorCatalog[PyrealWcid];
        foreach (var size in stacks)
        {
            var guid = _nextItemGuid++;
            Receive(sink, guid, coin, size, ItemDesc(coin, size, _reducer.State.Self.Guid, Log), objects);
        }

        UseDone(sink);
        if (vendor.ApproachAfterSale)
        {
            SendApproach(sink, vendor);
        }

        _reducer.StandIn!.At(_reducer.Clock.Now + SellObjectLagSeconds, later =>
        {
            foreach (var created in objects)
            {
                AnswerLog.Add("F745");
                later.OnCreateObject(Context(later), created, isUpdate: false);
            }

            PlaySound(later, _reducer.State.Self.Guid, SoundPickUpItem, 1f);
        });
    }

    // --- the shortcut bar ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The shortcut list the stand-in holds now (index to object, spell 0); tests.</summary>
    internal IReadOnlyList<ShortcutDesc> Shortcuts => _bar.Items;

    /// <summary>
    /// An AddShortCut (GA 0x019C; <c>Player_Character.HandleActionAddShortcut</c> 252-258): the entry at the index is added or replaced, and nothing is answered (capture C4). The next description carries the list. The spell word is dropped, as ACE
    /// drops it (<c>TODO(m4-standin-shortcut-spell)</c>).
    /// </summary>
    internal OutboundTicket AddShortcut(uint index, uint objectId, uint spellId)
    {
        if (spellId != 0)
        {
            Placeholder("m4-standin-shortcut-spell", "an AddShortCut's spell word is dropped: ACE keeps only the object of a shortcut and lists every entry with spell 0 in the next description (capture C4)");
        }

        _bar.AddOrUpdate(index, objectId);
        return Ticket();
    }

    /// <summary>A RemoveShortCut (GA 0x019D; <c>HandleActionRemoveShortcut</c> 260-264): the entry at the index is removed if there is one, and nothing is answered (capture C4).</summary>
    internal OutboundTicket RemoveShortcut(uint index)
    {
        _bar.Remove(index);
        return Ticket();
    }
}
