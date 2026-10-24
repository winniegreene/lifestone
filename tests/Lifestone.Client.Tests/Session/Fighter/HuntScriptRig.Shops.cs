// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Vendor.cs (ApproachVendor 284-298, BuyItems_ValidateTransaction 450-569, GetSellCost / GetBuyCost 571-597, ProcessItemsForPurchase 610-666),
// Player_Commerce.cs (HandleActionBuyItem 22-57, FinalizeBuyTransaction 59-112, HandleActionSellItem 124-236, VerifySellItems 242-320, SpendCurrency 337-393) and Player_Examine.cs (HandleActionIdentifyObject), with
// Network/GameEvent/Events/GameEventApproachVendor.cs (AGPL-3.0) [with changes: test code only; the order and the lengths are the live capture's (cache/m4/3-2/capture.md C1 to C3, as written in docs/session.md sections 35 and 37);
// no service rows, no weights or pack-space limits, no range check, no vendor emote (GE 0x02BD), no burden updates (02CD burden) and no pick-up sound; the stock is a labelled subset of the two shops].
using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 4.1 fix2c (D23, D26): the scripted vendors of <see cref="HuntScriptRig"/>, the part of ACE the hunt's shop trip meets, so the shop rows that 3.2 s4a proved over the stand-in's vendors run over the script again. Test code only: it draws
/// nothing, places nothing in any stand-in world and is reachable from no play-check or frame.
/// </summary>
/// <remarks>
/// <para><see cref="Shops"/> on, the two Holtburg vendors (Thelnoth Cort the Healer 715 and Shopkeeper Renald the Elder 718) stand at their real cells and positions (cache/m4/3-2/vendors.tsv) once the hunter is present, and the hunter holds
/// 10,000 pyreals (one stack of wcid 273, CoinValue 10,000) and a pair of Leather Bracers (wcid 36) for Renald. Off, no vendor is ever created (the "not in view" row).</para>
/// <para><b>The open</b> (capture C1 and C2): a Use of a vendor is busy (UseDone 0x001D) while an earlier open or heal holds the hunter, else the player's turn, the UseDone 0.825 s (Thelnoth) or 0.956 s (Renald) after the Use and the
/// 0062 1.043 s or 0.650 s after that. <b>A buy</b> (GA 0x005F): busy is 00A0 then UseDone 0x001D; too few pyreals is 00A0 then UseDone 0, no text; otherwise the coin stack's 0197 (or 0024 when it empties) and the CoinValue 02CD, the 0022 of each
/// new item, then 3 ms later the refreshed 0062 and the UseDone 0, and 3 ms after that the F745 of each new item. <b>A sale</b> (GA 0x0060): busy as above; an item outside the vendor's mask is the 02EB line "The X is unsellable."; nothing left to
/// sell is 00A0 then UseDone 0; otherwise the 0022 of each item into the vendor, the payout stack's 0022 and the CoinValue 02CD, the UseDone 0, the refreshed 0062 (Renald, capture C2: after the UseDone; Thelnoth, C1: before the payout)
/// and the payout stack's F745 5 ms later. The prices are ACE's: the vendor charges <c>max(1, ceil(1.35 x Value - 0.1))</c> per created stack and pays <c>max(1, floor(0.9 x Value + 0.1))</c> on the whole stack's Value.
/// <b>An Identify</b> (GA 0x00C8) is answered 5 ms later by a 0x00C9 with the Value table (a guid the script does not know: flags 0, success 0).</para>
/// </remarks>
internal sealed partial class HuntScriptRig
{
    /// <summary>The guids of the vendors (the live capture's).</summary>
    public const uint ThelnothGuid = 0x7A9B4027, RenaldGuid = 0x7A9B4021;

    /// <summary>The weenie class ids of the vendors, the pyreal coin, the Leather Bracers the hunter holds for Renald and the Handy Healing Kit.</summary>
    public const uint ThelnothWcid = 715, RenaldWcid = 718, PyrealWcid = 273, BracersWcid = 36, HandyKitWcid = 628;

    /// <summary>The guids of the hunter's coin stack and Leather Bracers.</summary>
    public const uint PyrealGuid = 0x80080000, BracersGuid = 0x80080002;

    /// <summary>The pyreals the hunter holds when the shops are on.</summary>
    public const int StartPyreals = 10_000;

    private const uint VendorFlag = 0x200, CreatureItemType = 0x10, PromissoryNote = 0x40000, CoinValueProperty = 20, PyrealMaxStack = 25_000;
    private const float VendorBuyRate = 0.9f, VendorSellRate = 1.35f;
    private const double ThelnothOpenDone = 0.825, RenaldOpenDone = 0.956, ThelnothApproachLag = 1.043, RenaldApproachLag = 0.650;
    private const double BuyApproachLag = 0.003, BuyObjectLag = 0.003, SellObjectLag = 0.005, IdentifyLagSeconds = 0.005;

    /// <summary>True: the two vendors are created and the hunter holds the pyreals and the Bracers once it is present (set before the run).</summary>
    public bool Shops { get; set; }

    /// <summary>The refusals the script gave because the hunter was busy (a Use of a vendor, a Buy or a Sell).</summary>
    public int BusyRefusals { get; private set; }

    /// <summary>Every 0062 the script sent, in order.</summary>
    public IReadOnlyList<VendorApproachMsg> Approaches => _approaches;

    /// <summary>The guids the script answered an Identify for, in order.</summary>
    public IReadOnlyList<uint> Identified => _identified;

    /// <summary>The pyreals the SERVER holds (the sum of its coin stacks).</summary>
    public int Coins => _coins.Sum(c => c.Size);

    /// <summary>The server's coin stacks, in the order ACE spends them.</summary>
    public IReadOnlyList<(uint Guid, int Size)> CoinStacks => _coins;

    /// <summary>The server's first coin stack holds exactly <paramref name="pyreals"/> from now on, and no message goes out: the client's model keeps what it was told (a refused purchase's set-up).</summary>
    public void SetCoins(int pyreals)
    {
        _serverPyreals = pyreals;
        if (_shopsSeeded)
        {
            _coins.Clear();
            _coins.Add((PyrealGuid, pyreals));
        }
    }

    /// <summary>The hunter holds exactly <paramref name="count"/> Handy Healing Kits (20 uses each), all in the pack (set before the run: the login announces them).</summary>
    public void HoldKits(int count)
    {
        Fighter.DropItems(FighterKit.HealingKitWcid);
        for (var i = 0; i < count; i++)
        {
            _kitUses[Fighter.AddCopy(FighterKit.HealingKitWcid)] = 20;
        }
    }

    private sealed record Wares(uint Wcid, string Name, uint ItemType, uint Value, ushort Burden, int? MaxStack = null, ushort? Uses = null);

    private sealed class Shop(uint guid, string name, uint wcid, uint cell, Vector3 at, Quaternion facing, uint mask, double openDone, double approachLag, bool approachAfterSale, uint firstRow, Wares[] stock)
    {
        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public uint Wcid { get; } = wcid;

        public uint Cell { get; } = cell;

        public Vector3 At { get; } = at;

        public Quaternion Facing { get; } = facing;

        public uint Mask { get; } = mask;

        public double OpenDone { get; } = openDone;

        public double ApproachLag { get; } = approachLag;

        /// <summary>True when the refreshed 0062 of a sale follows the UseDone (capture C2, the 53-row Renald) rather than comes before the payout (C1, the 38-row Thelnoth).</summary>
        public bool ApproachAfterSale { get; } = approachAfterSale;

        /// <summary>The default rows (unlimited supply) by row guid.</summary>
        public Dictionary<uint, Wares> Rows { get; } = stock.Select((w, i) => (Guid: firstRow + (uint)i, Wares: w)).ToDictionary(r => r.Guid, r => r.Wares);

        /// <summary>The rows in the order the 0062 lists them: the default rows, then the unique items the hunter sold.</summary>
        public List<uint> Order { get; } = [.. stock.Select((_, i) => firstRow + (uint)i)];

        /// <summary>The items the hunter sold that ACE resells (they neither stack nor have uses), by guid.</summary>
        public Dictionary<uint, WeenieDesc> Uniques { get; } = [];
    }

    private static readonly Wares Pyreal = new(PyrealWcid, "Pyreal", 0x40, 1, 0, (int)PyrealMaxStack);
    private static readonly Wares Bracers = new(BracersWcid, "Leather Bracers", 0x2, 1100, 270);
    private static readonly Wares HandyKit = new(HandyKitWcid, "Handy Healing Kit", 0x80, 10, 50, null, 20);

    private readonly Dictionary<uint, Shop> _shops = new()
    {
        [ThelnothGuid] = new Shop(
            ThelnothGuid, "Thelnoth Cort the Healer", ThelnothWcid, 0xA9B40111, new Vector3(30.0196f, 159.683f, 66.005f), new Quaternion(0f, 0f, 0.070627f, 0.997503f), 0x44080, ThelnothOpenDone, ThelnothApproachLag, false, 0x80090000,
            [new(377, "Potion of Healing", 0x80, 170, 15, 100), HandyKit, new(629, "Adept Healing Kit", 0x80, 50, 50, null, 25), new(2621, "Trade Note (100)", PromissoryNote, 100, 1, 250)]),
        [RenaldGuid] = new Shop(
            RenaldGuid, "Shopkeeper Renald the Elder", RenaldWcid, 0xA9B40141, new Vector3(139.234f, 18.3293f, 94.005f), new Quaternion(0f, 0f, -0.160311f, 0.987067f), 0x480467A7, RenaldOpenDone, RenaldApproachLag, true, 0x800A0000,
            [new(300, "Arrow", 0x100, 1, 5, 1000), new(309, "Club", 0x1, 260, 675), new(352, "Short Sword", 0x1, 160, 350), Bracers, new(258, "Apple", 0x20, 7, 50, 100)]),
    };

    private readonly List<(uint Guid, int Size)> _coins = [];
    private readonly List<VendorApproachMsg> _approaches = [];
    private readonly List<uint> _identified = [];
    private bool _shopsSeeded;
    private int? _serverPyreals;
    private int _tradesSeen;
    private uint _nextItemGuid = 0x80100000;
    private uint _slot = 40;

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the world

    /// <summary>The two vendors at their real cells, the hunter's coins (one stack, CoinValue) and its Leather Bracers in the main pack.</summary>
    private void SeedShops()
    {
        _shopsSeeded = true;
        foreach (var shop in _shops.Values)
        {
            Sink.OnCreateObject(Ctx, new CreateObjectMsg(shop.Guid, ModelData.Empty, new PhysicsDesc
            {
                Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
                State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
                Position = new AcPosition(shop.Cell, shop.At, shop.Facing),
                SetupId = 0x02000001,
                Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
            }, new WeenieDesc
            {
                Header = WeenieHeaderFlags.None,
                Name = shop.Name,
                WeenieClassId = shop.Wcid,
                IconId = 0x06000001,
                ItemType = CreatureItemType,
                ObjectDescriptionFlags = VendorFlag,
            }), isUpdate: false);
        }

        _coins.Add((PyrealGuid, _serverPyreals ?? StartPyreals));
        Put(PyrealGuid);
        Sink.OnCreateObject(Ctx, PackObject(PyrealGuid, Pyreal, StartPyreals), isUpdate: false);
        Put(BracersGuid);
        Sink.OnCreateObject(Ctx, PackObject(BracersGuid, Bracers, 1), isUpdate: false);
        Private(PrivateUpdateKind.PropertyInt, CoinValueProperty, StartPyreals);
    }

    /// <summary>The 0022 that puts <paramref name="item"/> in the hunter's main pack.</summary>
    private void Put(uint item) => Sink.OnInventoryPutObjInContainer(Ctx, Header(0x0022), new InventoryPutObjInContainerEvent(item, Self, _slot++, 0));

    /// <summary>The description of a ware as a stock row (<paramref name="stack"/> 0, the Value of one) or a pack object shows it: the Value and the burden of the whole stack, a stacking ware's limit and size, a kit's uses.</summary>
    private WeenieDesc Desc(Wares ware, int stack, uint? container)
    {
        var header = WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden;
        if (container is not null)
        {
            header |= WeenieHeaderFlags.Container;
        }

        if (ware.MaxStack is not null)
        {
            header |= WeenieHeaderFlags.MaxStackSize;
            if (stack > 0)
            {
                header |= WeenieHeaderFlags.StackSize;
            }
        }

        if (ware.Uses is not null)
        {
            header |= WeenieHeaderFlags.Structure | WeenieHeaderFlags.MaxStructure;
        }

        var count = Math.Max(stack, 1);
        return new WeenieDesc
        {
            Header = header,
            Name = ware.Name,
            WeenieClassId = ware.Wcid,
            IconId = 0x06001036,
            ItemType = ware.ItemType,
            ObjectDescriptionFlags = 0,
            ContainerId = container,
            Value = ware.Value * (uint)count,
            Burden = (ushort)Math.Min(ware.Burden * count, ushort.MaxValue),
            StackSize = ware.MaxStack is not null && stack > 0 ? (ushort)Math.Min(stack, ushort.MaxValue) : null,
            MaxStackSize = ware.MaxStack is { } max ? (ushort)Math.Min(max, ushort.MaxValue) : null,
            Structure = ware.Uses,
            MaxStructure = ware.Uses,
        };
    }

    private CreateObjectMsg PackObject(uint guid, Wares ware, int stack) =>
        new(guid, ModelData.Empty, new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup,
            State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal,
            SetupId = 0x02000B77,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        }, Desc(ware, stack, Self));

    private void CoinValueNow() => Private(PrivateUpdateKind.PropertyInt, CoinValueProperty, Coins);

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the open (capture C1 and C2)

    /// <summary>A Use of a vendor: busy (an earlier open or heal holds the hunter) is UseDone 0x001D; otherwise the turn, the UseDone and the 0062 in the capture's lengths. False when <paramref name="guid"/> is not a vendor.</summary>
    private bool UseVendor(uint guid)
    {
        if (!Shops || !_shops.TryGetValue(guid, out var shop))
        {
            return false;
        }

        var refused = Busy;
        if (!refused)
        {
            _busyUntil = Now + 0.05 + shop.OpenDone;
        }

        At(0.05, () => OpenShop(shop, refused));
        return true;
    }

    private void OpenShop(Shop shop, bool refused)
    {
        if (refused)
        {
            BusyRefusals++;
            UseDone(YoureTooBusy);
            return;
        }

        StanceMotion(StanceNow(), null);
        At(shop.OpenDone, () => UseDone());
        At(shop.OpenDone + shop.ApproachLag, () => SendApproach(shop));
    }

    private void SendApproach(Shop shop)
    {
        var rows = ImmutableArray.CreateBuilder<VendorEntry>(shop.Order.Count);
        foreach (var guid in shop.Order)
        {
            if (shop.Rows.TryGetValue(guid, out var ware))
            {
                rows.Add(new VendorEntry(VendorEntry.Unlimited, guid, Desc(ware, 0, null)));
            }
            else if (shop.Uniques.TryGetValue(guid, out var unique))
            {
                rows.Add(new VendorEntry(1, guid, unique));
            }
        }

        var message = new VendorApproachMsg(shop.Guid, shop.Mask, 0, 1_000_000, true, VendorBuyRate, VendorSellRate, 0, 0, string.Empty, rows.MoveToImmutable());
        _approaches.Add(message);
        Sink.OnApproachVendor(Ctx, Header(0x0062), message);
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- the prices (Vendor.cs 573-609)

    /// <summary>What a vendor charges for a created stack whose Value is <paramref name="value"/>: <c>max(1, ceil(SellPrice x Value - 0.1))</c>, a Promissory Note at 1.15.</summary>
    private static uint SellCost(uint value, uint itemType)
    {
        var rate = itemType == PromissoryNote ? 1.15f : VendorSellRate;
        return Math.Max(1u, (uint)Math.Ceiling((rate * value) - 0.1));
    }

    /// <summary>What a vendor pays for an item whose Value (the whole stack's) is <paramref name="value"/>: <c>max(1, floor(BuyPrice x Value + 0.1))</c>, a Promissory Note at 1.0.</summary>
    private static int BuyCost(uint value, uint itemType)
    {
        var rate = itemType == PromissoryNote ? 1.0f : VendorBuyRate;
        return Math.Max(1, (int)Math.Floor((rate * value) + 0.1));
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- a trade

    /// <summary>A Buy or a Sell has reached the server: busy (decided now) is 00A0 then UseDone 0x001D, else it is carried out 50 ms later.</summary>
    private void Trade(bool buy, uint vendor)
    {
        var items = Server.TradeSends[_tradesSeen++].Items;
        if (Busy)
        {
            BusyRefusals++;
            At(0.05, () => EndTrade(YoureTooBusy, null));
            return;
        }

        At(0.05, () =>
        {
            if (!Shops || !_shops.TryGetValue(vendor, out var shop))
            {
                EndTrade(0x0038, null); // WeenieError.NoObject: the vendor is not in the world
            }
            else if (buy)
            {
                BuyNow(shop, items);
            }
            else
            {
                SellNow(shop, items);
            }
        });
    }

    /// <summary>The refusal every trade failure ends with: the optional 02EB line, GameEvent 0x00A0 {the player}, then the UseDone.</summary>
    private void EndTrade(uint error, string? line)
    {
        if (line is not null)
        {
            Sink.OnTransientString(Ctx, Header(0x02EB), new Lifestone.Net.Messages.TransientStringEvent(line));
        }

        Sink.OnInventoryServerSaysFailed(Ctx, Header(0x00A0), new InventoryServerSaysFailedEvent(Self, 0));
        UseDone(error);
    }

    private void SpendCoins(int amount)
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
                Sink.OnInventoryRemoveObject(Ctx, new InventoryRemoveObjectMsg(guid));
            }
            else
            {
                _coins[i] = (guid, size - take);
                Sink.OnSetStackSize(Ctx, new SetStackSizeMsg(Next(0x40000 + guid, 0), guid, (uint)(size - take), (uint)(size - take)));
                i++;
            }

            CoinValueNow();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- a buy (Player_Commerce.cs 22-112)

    private void BuyNow(Shop shop, IReadOnlyList<(uint Item, uint Amount)> items)
    {
        var created = new List<(uint Guid, Wares Ware, int Stack, uint Cost)>();
        foreach (var (guid, amount) in items)
        {
            if (amount is 0 or > int.MaxValue)
            {
                EndTrade(0, "Invalid amount"); // Vendor.cs 458-463: the whole transaction is rejected
                return;
            }

            if (shop.Rows.TryGetValue(guid, out var ware))
            {
                for (var remaining = (int)amount; remaining > 0;)
                {
                    var stack = ware.MaxStack is { } max ? Math.Min(remaining, max) : 1;
                    created.Add((_nextItemGuid++, ware, stack, SellCost(ware.Value * (uint)stack, ware.ItemType)));
                    remaining -= stack;
                }
            }
        }

        var total = created.Sum(c => (long)c.Cost);
        if (created.Count == 0 || total > Coins)
        {
            EndTrade(0, null); // Vendor.cs 546-552: too few pyreals, no text
            return;
        }

        SpendCoins((int)total);
        var objects = new List<CreateObjectMsg>();
        foreach (var (guid, ware, stack, _) in created)
        {
            if (ware.Uses is { } uses)
            {
                _kitUses[guid] = uses;
            }

            Put(guid);
            objects.Add(PackObject(guid, ware, stack));
        }

        At(BuyApproachLag, () =>
        {
            SendApproach(shop);
            UseDone();
        });
        At(BuyApproachLag + BuyObjectLag, () =>
        {
            foreach (var created in objects)
            {
                Sink.OnCreateObject(Ctx, created, isUpdate: false);
            }
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- a sale (Player_Commerce.cs 124-320)

    private static string PluralOf(string name) => name.EndsWith('s') ? name : name + "s";

    private void SellNow(Shop shop, IReadOnlyList<(uint Item, uint Amount)> items)
    {
        var verified = new List<(uint Guid, WeenieDesc Weenie, uint Value, uint ItemType)>();
        foreach (var (guid, amount) in items)
        {
            if (!Session.State.Objects.TryGet(guid, out var held) || !Session.State.Self.Inventory.Find(guid).Held || verified.Any(v => v.Guid == guid) || amount is 0 or > int.MaxValue || amount > Math.Max((int)(held.Weenie.StackSize ?? 1), 1))
            {
                continue; // an item the hunter does not hold, a duplicate and an amount beyond the stack are skipped silently
            }

            var name = (held.Weenie.StackSize ?? 1) > 1 ? PluralOf(held.Name) : held.Name;
            var value = held.Weenie.Value ?? 0;
            if ((shop.Mask & held.Weenie.ItemType) == 0)
            {
                Sink.OnTransientString(Ctx, Header(0x02EB), new Lifestone.Net.Messages.TransientStringEvent("The " + name + " is unsellable."));
                continue;
            }

            if (value < 1)
            {
                Sink.OnTransientString(Ctx, Header(0x02EB), new Lifestone.Net.Messages.TransientStringEvent("The " + name + " has no value and cannot be sold."));
                continue;
            }

            verified.Add((guid, held.Weenie, value, held.Weenie.ItemType));
        }

        if (verified.Count == 0)
        {
            Sink.OnInventoryServerSaysFailed(Ctx, Header(0x00A0), new InventoryServerSaysFailedEvent(Self, 0));
            UseDone();
            return;
        }

        var payout = verified.Sum(v => BuyCost(v.Value, v.ItemType));
        foreach (var (guid, weenie, _, _) in verified)
        {
            Sink.OnInventoryPutObjInContainer(Ctx, Header(0x0022), new InventoryPutObjInContainerEvent(guid, shop.Guid, 0, 0));
            if (weenie.MaxStackSize is null && weenie.MaxStructure is null)
            {
                shop.Uniques[guid] = weenie with { ContainerId = shop.Guid, Header = weenie.Header | WeenieHeaderFlags.Container }; // ProcessItemsForPurchase 610-660: an item that neither stacks nor has uses is resold
                shop.Order.Add(guid);
            }
        }

        if (!shop.ApproachAfterSale)
        {
            SendApproach(shop);
        }

        var objects = new List<CreateObjectMsg>();
        for (var left = payout; left > 0; left -= (int)Math.Min((uint)left, PyrealMaxStack))
        {
            var size = (int)Math.Min((uint)left, PyrealMaxStack);
            var guid = _nextItemGuid++;
            _coins.Add((guid, size));
            Put(guid);
            CoinValueNow();
            objects.Add(PackObject(guid, Pyreal, size));
        }

        UseDone();
        if (shop.ApproachAfterSale)
        {
            SendApproach(shop);
        }

        At(SellObjectLag, () =>
        {
            foreach (var created in objects)
            {
                Sink.OnCreateObject(Ctx, created, isUpdate: false);
            }
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------- an Identify (Player_Examine.cs; capture C3)

    private void Identify(uint guid)
    {
        if (guid == 0)
        {
            return; // guid 0 clears the target and gets no reply
        }

        _identified.Add(guid);
        var known = Session.State.Objects.TryGet(guid, out var item);
        var empty = ImmutableDictionary<uint, int>.Empty;
        var ints = known ? empty.Add(19, (int)(item?.Weenie.Value ?? 0)) : empty; // PropertyInt 19: Value
        Sink.OnIdentifyResponse(Ctx, Header(0x00C9), new IdentifyResponseMsg(
            guid,
            known ? IdentifyResponseFlags.IntStatsTable : IdentifyResponseFlags.None,
            known,
            ints,
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
    }
}
