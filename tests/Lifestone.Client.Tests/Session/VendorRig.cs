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

    public VendorRig(int coins = 10000)
    {
        R = new WorldActionRig();
        R.Describe([], []);
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
    public void Arrive(uint guid, string name, uint wcid, uint itemType, uint value, ushort? stackSize = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        var header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | (stackSize is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.StackSize);
        H.R.Reducer.OnCreateObject(
            H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ContainerId = Player, Value = value, StackSize = stackSize } },
            isUpdate: false);
    }

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
