using System.Numerics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The world with a player holding 10,000 pyreals and the server's side of a vendor scripted from the orders the lead captured live (M4 3.2: cache/m4/3-2/capture.md C1, C2; ACE 95ef426d). Thelnoth Cort the Healer stands 2 m from the
/// player (UseRadius 3 m, rates 0.9 and 1.35, mask 0x44080, a Handy Healing Kit row of Value 10 and a Potion of Healing row of Value 170, an arrow row of Value 5 that stacks to 25, a zero row); Renald the Elder stands 40 m away.
/// Every feed goes through ACE's own writers and the real decoder.
/// </summary>
internal sealed class VendorRig
{
    public const uint Player = WorldActionRig.Player;
    public const uint Thelnoth = 0x7A9B4027;
    public const uint Renald = 0x7A9B4030;
    public const uint KitRow = 0x8000033F;
    public const uint PotionRow = 0x80000340;
    public const uint ArrowRow = 0x80000341;
    public const uint ZeroRow = 0x80000342;
    public const uint Cell = 0x860201AD;
    public const uint ThelnothMask = 0x00044080;
    public const uint RenaldMask = 0x480467A7;
    public const uint MiscType = 0x80;
    public const uint ArmorType = 0x2;
    public const uint ScrollType = 0x2000;
    public const uint CoinStack = 0x80000100;

    private uint _event = 500;
    private uint _guid = 0x8000035F;
    private byte _seq;
    private ushort _position = 100;
    private ushort _placed = 1;

    public VendorRig(int coins = 10000, Lifestone.Net.Messages.PlayerDescriptionMsg? description = null)
    {
        R = new WorldActionRig();
        if (description is null)
        {
            R.Describe([], []);
        }
        else
        {
            H.Describe(description); // M5 7 P7j: a described player (skills, spellbook)
        }

        Coins(coins);
        H.R.Bus.Subscribe((in VendorOpened e) => Events.Add(e));
        H.R.Bus.Subscribe((in VendorStockChanged e) => Events.Add(e));
        H.R.Bus.Subscribe((in VendorClosed e) => Events.Add(e));
        H.R.Bus.Subscribe((in CoinValueChanged e) => Events.Add(e));
        Place(Thelnoth, "Thelnoth Cort the Healer", x: 12, y: 20);
    }

    public WorldActionRig R { get; }

    public ActionHarness H => R.H;

    public ActionApi Api => R.Api;

    public List<object> Events { get; } = [];

    public GameState State => H.State;

    /// <summary>Advances the clock, then runs the Action API's update.</summary>
    public void Tick(double seconds = 0) => R.Tick(seconds);

    /// <summary>The next guid for a thing the server creates.</summary>
    public uint NextGuid() => ++_guid;

    /// <summary>GameMessage 0x02CD CoinValue (PropertyInt 20).</summary>
    public void Coins(int value) => R.PrivateInt(PlayerQualities.CoinValueProperty, value);

    /// <summary>A vendor's object in the world at (x, y) of the player's cell, with its UseRadius.</summary>
    public void Place(uint guid, string name, float x, float y, float useRadius = 3f, uint wcid = 0)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, y), name: name);
        H.R.Reducer.OnCreateObject(H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.UseRadius, UseRadius = useRadius, WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>A vendor's object at a cell and point of the world (a ground's vendor at his own place), with its UseRadius and weenie class.</summary>
    public void PlaceAt(uint guid, string name, uint cell, Vector3 at, float useRadius = 3f, uint wcid = 0)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(++_placed), new AcPosition(cell, at, Quaternion.Identity), name: name); // newer stamps each time: a re-placed vendor is not a stale create
        H.R.Reducer.OnCreateObject(H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.UseRadius, UseRadius = useRadius, WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>The player stands at <paramref name="at"/> in <paramref name="cell"/>: an own position update with the next position sequence.</summary>
    public void StandAt(uint cell, Vector3 at)
    {
        _position++;
        H.R.Reducer.OnUpdatePosition(H.R.Context, new UpdatePositionMsg(Player, PositionFlags.Grounded, new AcPosition(cell, at, Quaternion.Identity), null, null, 1, _position, 1, 1));
    }

    /// <summary>The player moves to x metres (in the player's cell, y 20): an own position update.</summary>
    public void WalkTo(float x, ushort sequence)
    {
        H.R.Reducer.OnUpdatePosition(H.R.Context, ReducerHarness.Move(Player, 1, sequence, 1, 1, Cell, x));
    }

    /// <summary>One shop row.</summary>
    public static AceVendorRow Row(int stack, uint guid, string name, uint wcid, uint itemType, int value, ushort? stackSize = null, ushort? maxStack = null) =>
        new(stack, new AceCreateObject { Guid = guid, Name = name, WeenieClassId = wcid, Icon = 0x06001234, ItemType = itemType, Value = value, StackSize = stackSize, MaxStackSize = maxStack });

    /// <summary>Thelnoth's rows as the capture's first rows read.</summary>
    public static AceVendorRow[] ThelnothRows() =>
    [
        Row(-1, KitRow, "Handy Healing Kit", 628, MiscType, 10),
        Row(-1, PotionRow, "Potion of Healing", 377, MiscType, 170),
        Row(-1, ArrowRow, "Arrow", 300, 0x100, 5, stackSize: 1, maxStack: 25),
        Row(0, ZeroRow, "Out of Stock", 999, MiscType, 7),
    ];

    /// <summary>GameEvent 0x0062 for <paramref name="vendor"/> with <paramref name="rows"/>.</summary>
    public void Stock(uint vendor, uint mask, IReadOnlyList<AceVendorRow> rows, float buy = 0.9f, float sell = 1.35f, uint min = 0, uint max = 1_000_000, bool magic = true) =>
        R.Raw(AceVendorWriters.ApproachVendor(Player, ++_event, vendor, mask, min, max, magic, buy, sell, rows));

    /// <summary>Thelnoth's 0062.</summary>
    public void ThelnothStock() => Stock(Thelnoth, ThelnothMask, ThelnothRows());

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    public void UseDone(uint code = 0) => R.UseDone(code);

    /// <summary>GameEvent 0x00A0 InventoryServerSaveFailed with the player's own guid and error 0 (every vendor refusal, capture C2).</summary>
    public void Refusal() => R.Raw(AceM3Writers.InventoryServerSaysFailed(Player, ++_event, Player, 0));

    /// <summary>GameEvent 0x00A0 naming <paramref name="item"/>, as a refused pickup, drop or merge does (not a vendor refusal).</summary>
    public void ItemRefusal(uint item) => R.Raw(AceM3Writers.InventoryServerSaysFailed(Player, ++_event, item, 0));

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    public void Transient(string text) => R.Raw(AceWireWriters.TransientString(Player, ++_event, text));

    /// <summary>GameEvent 0x028B WeenieErrorWithString.</summary>
    public void ErrorText(uint code, string text) => R.Raw(AceWireWriters.WeenieErrorWithString(Player, ++_event, code, text));

    /// <summary>GameEvent 0x0022: the item goes into <paramref name="container"/> (the player's main pack by default).</summary>
    public void Put(uint item, uint? container = null) => R.Raw(AceM3Writers.PutObjInContainer(Player, ++_event, item, container ?? Player, 0, 0));

    /// <summary>GameMessage 0x0197: a stack's size and value.</summary>
    public void Stack(uint item, uint size, uint value) => R.Raw(AceM3Writers.SetStackSize(++_seq, item, size, value));

    /// <summary>An F745 for something the player holds (the way a bought item or a payout stack arrives).</summary>
    public void Arrive(uint guid, string name, uint wcid, uint itemType, uint value, ushort? stackSize = null, ushort? maxStack = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        var header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | (stackSize is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.StackSize) | (maxStack is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.MaxStackSize);
        H.R.Reducer.OnCreateObject(
            H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ContainerId = Player, Value = value, StackSize = stackSize, MaxStackSize = maxStack } },
            isUpdate: false);
    }

    /// <summary>GameMessage 0x0024: the item leaves the player's inventory (ACE's whole merge sends it for the source, <c>Player_Inventory.cs</c> 3096).</summary>
    public void Remove(uint item) => R.Raw(AceM3Writers.InventoryRemoveObject(item));

    /// <summary>An item the player holds already (in the pack), with its Value and ItemType.</summary>
    public void Own(uint guid, string name, uint itemType, uint value, ushort? stackSize = null)
    {
        Arrive(guid, name, 1, itemType, value, stackSize);
        Put(guid); // the pack list names it (the description would have)
    }

    /// <summary>The player's pyreal stack: the item the coins live in (the description would name it).</summary>
    public void OwnCoins(uint value) => Own(CoinStack, "Pyreal", 0x40, value);

    /// <summary>A UseDone-less vendor open as the capture C1 shows it: the Use sent, the turn, UseDone 0, the stock 1.04 s later.</summary>
    public ActionHandle Open(uint vendor = Thelnoth, double stockAfterUseDone = 1.04)
    {
        var handle = Api.OpenVendor(vendor, ActionSource.Ui);
        Tick();
        Tick(0.833);
        UseDone();
        Tick(stockAfterUseDone);
        ThelnothStock();
        Tick();
        return handle;
    }

    /// <summary>The 0062 the last Open leaves, then the log of events cleared.</summary>
    public void Opened()
    {
        var handle = Open();
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Events.Clear();
    }

    public int Sent(FakeSendKind kind) => H.Net.Of(kind).Count;
}

/// <summary>
/// The server's side of a ground's home vendors, scripted from the orders <see cref="VendorRig"/> holds (M5 item 3 m3; capture C1, C2; ACE 95ef426d): <see cref="Pump"/> answers each new Use, Buy and Sell the Action API sent. A Use of a
/// shop is its UseDone and its 0062; a Buy is the coin spent, the new stack's F745 and 0022, the refreshed 0062 and the UseDone, or a refusal (00A0 and UseDone, with the shop's text first when it has one; no text for too few pyreals, as
/// ACE); a Sell is each item into the vendor's container and the payout (the open stock's <see cref="VendorStock.PayoutFor(WorldObject)"/>), an item of an unsellable class answered with ACE's 02EB "The X is unsellable.", and a sale of
/// nothing the 00A0.
/// </summary>
internal sealed class ScriptedShop(VendorRig rig)
{
    private readonly Dictionary<uint, Shop> _shops = [];
    private int _seen;

    /// <summary>The Use, Buy and Sell sends answered.</summary>
    public int Uses { get; private set; }

    public int Buys { get; private set; }

    public int Sells { get; private set; }

    /// <summary>The DropItem sends answered (M5 7 D99).</summary>
    public int Drops { get; private set; }

    private const uint GroundStandIn = 0x7FFF0001;

    /// <summary>The sales refused whole because the payout's coin stacks did not fit the free slots (M5 7 G1).</summary>
    public int SlotRefusals { get; private set; }

    /// <summary>A shop: the vendor, its mask, its rows (stack, guid, name, wcid, ItemType, Value) and how it answers.</summary>
    internal sealed class Shop
    {
        public required uint Vendor { get; init; }

        public required uint Mask { get; init; }

        public List<(int Stack, uint Guid, string Name, uint Wcid, uint ItemType, int Value)> Rows { get; } = [];

        /// <summary>A text every buy is refused with (a 02EB before the 00A0), or null.</summary>
        public string? BuyRefusal { get; set; }

        /// <summary>The weenie classes this vendor answers "is unsellable" for.</summary>
        public HashSet<uint> Unsellable { get; } = [];

        /// <summary>M5 7 D136: the MaxStackSize of a row's weenie class (on the 0062 and on the bought stack's F745), as the world data gives it; a class not named has none.</summary>
        public Dictionary<uint, ushort> MaxStacks { get; } = [];

        /// <summary>
        /// M5 7 D136 follow-up: a vendor that lists a trade note lists all ten, as Archmage Cindrue's CreateList does (ACE-World-Database v0.9.296, rows 21060-21069): the missing ones are added as promissory notes (ItemType 0x40000, the
        /// face as the Value, so the wire price is ACE's 1.15 times it), every note stacking to 250.
        /// </summary>
        public void AddTradeNotes(Func<uint> nextRow)
        {
            var notes = Lifestone.Client.Session.Scenarios.Journey.PurseNotes.All;
            if (!Rows.Any(r => notes.Any(n => n.Wcid == r.Wcid)))
            {
                return;
            }

            foreach (var note in notes)
            {
                MaxStacks[note.Wcid] = 250;
                if (!Rows.Any(r => r.Wcid == note.Wcid))
                {
                    Rows.Add((-1, nextRow(), note.Name, note.Wcid, VendorStock.PromissoryNoteType, note.Value));
                }
            }
        }

        public AceVendorRow[] Wire() => [.. Rows.Select(r => VendorRig.Row(r.Stack, r.Guid, r.Name, r.Wcid, r.ItemType, r.Value, maxStack: MaxStacks.TryGetValue(r.Wcid, out var m) ? m : null))];
    }

    /// <summary>The StackableMerge (0x0054) and StackableSplitToContainer (0x0055) sends answered (M5 7 D136).</summary>
    public int Merges { get; private set; }

    public int Splits { get; private set; }

    /// <summary>Adds a shop at the sell rate 1.0 (so a row's Value is its price) and the buy rate 0.9.</summary>
    public Shop Add(uint vendor, uint mask)
    {
        var shop = new Shop { Vendor = vendor, Mask = mask };
        _shops[vendor] = shop;
        return shop;
    }

    /// <summary>The shop of <paramref name="vendor"/>.</summary>
    public Shop Get(uint vendor) => _shops[vendor];

    /// <summary>Answers every send since the last pump.</summary>
    public void Pump()
    {
        var sends = rig.H.Net.Sends;
        for (; _seen < sends.Count; _seen++)
        {
            var send = sends[_seen];
            if (send.Kind == FakeSendKind.DropItem)
            {
                // M5 7 D99: a drop leaves the pack (into a stand-in for the ground's landblock; ACE confirms with the same move)
                Drops++;
                rig.Put(send.Guid, GroundStandIn);
                continue;
            }

            if (send.Kind == FakeSendKind.StackableMerge)
            {
                Merges++;
                Merge(send.Guid, uint.Parse(send.Target!, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture), send.Index);
                continue;
            }

            if (send.Kind == FakeSendKind.SplitToContainer)
            {
                Splits++;
                Split(send.Guid, uint.Parse(send.Target!, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture), (int)send.Amount);
                continue;
            }

            if (!_shops.TryGetValue(send.Guid, out var shop))
            {
                continue;
            }

            switch (send.Kind)
            {
                case FakeSendKind.Use:
                    Uses++;
                    rig.UseDone();
                    Stock(shop);
                    break;
                case FakeSendKind.Buy:
                    Buys++;
                    Buy(shop, send.Items ?? []);
                    break;
                case FakeSendKind.Sell:
                    Sells++;
                    Sell(shop, send.Items ?? []);
                    break;
            }
        }
    }

    private void Stock(Shop shop) => rig.Stock(shop.Vendor, shop.Mask, shop.Wire(), buy: 0.9f, sell: 1.0f);

    private (int Size, uint Unit, WorldObject Item) StackOf(uint guid)
    {
        var o = rig.State.Objects.TryGet(guid, out var item) ? item : throw new InvalidOperationException($"no object 0x{guid:X8}");
        var size = rig.State.Self.Inventory.TryGetStack(guid, out var s, out _) ? (int)s : o.Weenie.StackSize ?? 1;
        return (size, (o.Weenie.Value ?? 0) / (uint)Math.Max(1, size), o);
    }

    /// <summary>ACE's merge inside the pack (<c>Player_Inventory.cs</c> 3072ff): a whole source is removed (0x0024, 3096) and the target's 0x0197 follows (3121-3123); a partial one sends the source's 0x0197, then the target's (3151-3161).</summary>
    private void Merge(uint from, uint to, int amount)
    {
        var (fromSize, unit, _) = StackOf(from);
        var (toSize, toUnit, _) = StackOf(to);
        if (amount == fromSize)
        {
            rig.Remove(from);
        }
        else
        {
            rig.Stack(from, (uint)(fromSize - amount), (uint)(fromSize - amount) * unit);
        }

        rig.Stack(to, (uint)(toSize + amount), (uint)(toSize + amount) * toUnit);
    }

    /// <summary>ACE's split into a pack (<c>Player_Inventory.cs</c> 2243ff): the new stack's 0022 into the container, its F745, then the old stack's 0x0197 (the order the tracker's note gives).</summary>
    private void Split(uint stack, uint container, int amount)
    {
        var (size, unit, item) = StackOf(stack);
        var guid = rig.NextGuid();
        rig.Put(guid, container);
        rig.Arrive(guid, item.Name, item.Weenie.WeenieClassId, item.ItemType, unit * (uint)amount, (ushort)amount, item.Weenie.MaxStackSize is { } m ? (ushort)m : null);
        rig.Stack(stack, (uint)(size - amount), (uint)(size - amount) * unit);
    }

    private void Buy(Shop shop, IReadOnlyList<(uint item, uint amount)> items)
    {
        var stock = rig.State.Vendor;
        long price = 0;
        foreach (var (item, amount) in items)
        {
            price += stock?.PriceToBuy(item, amount) ?? 0;
        }

        var coins = (long)rig.State.Self.Qualities.CoinValue;
        if (shop.BuyRefusal is { } text || price > coins)
        {
            if (shop.BuyRefusal is { } t)
            {
                rig.Transient(t);
            }

            rig.Refusal();
            rig.UseDone();
            return;
        }

        rig.Coins((int)(coins - price));
        foreach (var (item, amount) in items)
        {
            var row = shop.Rows.First(r => r.Guid == item);
            var guid = rig.NextGuid();
            rig.Arrive(guid, row.Name, row.Wcid, row.ItemType, (uint)(row.Value * amount), stackSize: (ushort)amount, maxStack: shop.MaxStacks.TryGetValue(row.Wcid, out var max) ? max : null);
            rig.Put(guid);
        }

        Stock(shop);
        rig.UseDone();
    }

    private void Sell(Shop shop, IReadOnlyList<(uint item, uint amount)> items)
    {
        var stock = rig.State.Vendor;
        long payout = 0;
        var taken = 0;
        foreach (var (item, _) in items)
        {
            if (!rig.State.Objects.TryGet(item, out var o))
            {
                continue;
            }

            if (shop.Unsellable.Contains(o.Weenie.WeenieClassId))
            {
                rig.Transient($"The {o.Name} is unsellable.");
                continue;
            }

            payout += stock?.PayoutFor(o) ?? 0;
            taken++;
        }

        // M5 7 G1: ACE refuses the whole sale when the payout's coin stacks (25,000 each) exceed the free slots, counted before the items leave (Player_Commerce.cs 176-191 @95ef426d)
        if (taken > 0 && (payout + 24_999) / 25_000 > Lifestone.Client.Session.Mage.MageLootRules.FreeSlots(rig.State))
        {
            SlotRefusals++;
            rig.Refusal();
            rig.UseDone();
            return;
        }

        if (taken > 0)
        {
            foreach (var (item, _) in items)
            {
                if (rig.State.Objects.TryGet(item, out var o) && !shop.Unsellable.Contains(o.Weenie.WeenieClassId))
                {
                    rig.Put(item, shop.Vendor);
                }
            }
        }

        if (taken == 0)
        {
            rig.Refusal();
            rig.UseDone();
            return;
        }

        rig.Coins((int)(rig.State.Self.Qualities.CoinValue + payout));
        rig.UseDone();
    }
}
