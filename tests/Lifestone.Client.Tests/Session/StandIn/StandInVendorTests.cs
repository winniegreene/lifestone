using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>A stand-in session with the vendors, the purse and the two sellable items announced (M4 3.2 s3), shared by the vendor, appraisal and shortcut tests.</summary>
internal sealed class StandInVendorRig : IDisposable
{
    public StandInVendorRig(RecordedDescription? recorded = null, bool vendors = true)
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Vendors = vendors,
            Recorded = recorded,
            Stats = new StandInPlayerStats { Endurance = 240, HealthRanks = 68, Health = 188, Stamina = 240, TotalExperience = 1000, AvailableExperience = 1000 },
        };
        Cast = new CastRig(visit);
        Cast.Session.Events.Subscribe((in UseDone e) => Done.Add((Cast.Session.Clock.Now, e.WeenieError)));
        Cast.Begin();
        Done.Clear();
    }

    public CastRig Cast { get; }

    public List<(double At, uint Error)> Done { get; } = [];

    public IAcSession Net => Cast.Session.Reducer.LocalResponder!.Session;

    public StandInWorld World => Cast.World;

    public uint Self => Cast.Session.State.Self.Guid;

    public string[] Tags => Cast.Tags;

    public double Now => Cast.Session.Clock.Now;

    public void Run(double seconds) => Cast.Run(seconds);

    public bool Held(uint item) => Cast.Session.State.Self.Inventory.Find(item).Held;

    public int? Stack(uint item) => Cast.Session.State.Objects.TryGet(item, out var o) ? o.Weenie.StackSize : null;

    /// <summary>Clears the tags and the UseDones so the next step's answers stand alone.</summary>
    public void Fresh()
    {
        Cast.Begin();
        Done.Clear();
    }

    /// <summary>Opens <paramref name="vendor"/> with a Use and lets the whole answer arrive; the tags are then cleared.</summary>
    public void Open(uint vendor)
    {
        Net.SendUse(vendor);
        Run(4.0);
        Fresh();
    }

    public void Dispose() => Cast.Dispose();
}

/// <summary>
/// M4 3.2 s3: the stand-in's two vendors in the order and with the lengths of the live capture (cache/m4/3-2/capture.md C1 and C2): the open (the turn, the UseDone, the 0062), a buy and a sale with the coin arithmetic and the price rules of
/// facts.md, the refusals the capture recorded and ACE's source gives. Everything goes through the responder's session, the face the Action API sends through. Nothing here is ACE: the offline stand-in is proved against the capture, the
/// live loops prove ACE.
/// </summary>
public sealed class StandInVendorTests
{
    private const double Tolerance = 0.015;
    private const uint Thelnoth = StandInWorld.ThelnothGuid;
    private const uint Renald = StandInWorld.RenaldGuid;
    private const uint KitRow = StandInWorld.ThelnothRowGuid + 6;
    private const uint ArrowRow = StandInWorld.RenaldRowGuid;
    private const uint ClubRow = StandInWorld.RenaldRowGuid + 1;
    private const uint ShirtRow = StandInWorld.RenaldRowGuid + 7;

    // --- the world --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_vendors_stand_at_their_real_cells_with_their_real_names_and_the_player_holds_a_purse_and_two_sellable_items()
    {
        using var rig = new StandInVendorRig();

        Assert.True(rig.Cast.Session.State.Objects.TryGet(Thelnoth, out var thelnoth));
        Assert.Equal(("Thelnoth Cort the Healer" + StandInWorld.VendorNameTag, 715u, 0x200u), (thelnoth.Name, thelnoth.Weenie.WeenieClassId, thelnoth.Weenie.ObjectDescriptionFlags & 0x200u));
        Assert.Equal(0xA9B40111u, thelnoth.Position?.CellId);
        Assert.Equal(30.0196f, thelnoth.Position!.Value.Frame.Origin.X, 3);
        Assert.True(rig.Cast.Session.State.Objects.TryGet(Renald, out var renald));
        Assert.Equal(("Shopkeeper Renald the Elder" + StandInWorld.VendorNameTag, 718u, 0xA9B40141u), (renald.Name, renald.Weenie.WeenieClassId, renald.Position?.CellId ?? 0));
        Assert.Equal(StandInWorld.PlaceholderPyreals, rig.World.Coins);
        Assert.Equal(StandInWorld.PlaceholderPyreals, rig.Stack(StandInWorld.PyrealGuid));
        Assert.True(rig.Held(StandInWorld.SellKitGuid));
        Assert.True(rig.Held(StandInWorld.SellBracersGuid));
        var log = string.Join('\n', rig.Cast.Log.Entries.Select(e => e.Line));
        Assert.Contains("TODO(m4-standin-vendor-model)", log, StringComparison.Ordinal);
        Assert.Contains("TODO(m4-standin-pyreals)", log, StringComparison.Ordinal);
        Assert.Contains("TODO(m4-standin-vendor-stock)", log, StringComparison.Ordinal);
    }

    [Fact]
    public void A_visit_that_does_not_ask_holds_no_vendor_and_no_purse_and_a_use_of_the_vendors_guid_is_a_bare_UseDone()
    {
        using var rig = new StandInVendorRig(vendors: false);

        Assert.False(rig.Cast.Session.State.Objects.Contains(Thelnoth));
        Assert.False(rig.Held(StandInWorld.PyrealGuid));
        rig.Net.SendUse(Thelnoth);
        rig.Run(2.0);
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
        Assert.Empty(rig.World.Approaches);
    }

    // --- the open ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_use_of_Thelnoth_answers_the_turn_the_UseDone_and_after_a_second_the_0062_with_his_rates_mask_and_stock()
    {
        using var rig = new StandInVendorRig();
        var sent = rig.Now;

        rig.Net.SendUse(Thelnoth);
        rig.Run(4.0);

        Assert.Equal(["F74C", "01C7", "0062"], rig.Tags);
        var delay = rig.World.DelaySeconds;
        Assert.Equal(sent + delay, rig.Cast.First("F74C"), Tolerance);
        Assert.Equal(sent + delay + StandInWorld.ThelnothOpenDoneSeconds, rig.Cast.First("01C7"), Tolerance);
        Assert.Equal(sent + delay + StandInWorld.ThelnothOpenDoneSeconds + StandInWorld.ThelnothApproachLagSeconds, rig.Cast.First("0062"), Tolerance);
        var approach = Assert.Single(rig.World.Approaches);
        Assert.Equal((Thelnoth, 0x44080u, 0u, 1_000_000u, true, 0.9f, 1.35f, 0u, 0u), (approach.Vendor, approach.MerchandiseItemTypes, approach.MerchandiseMinValue, approach.MerchandiseMaxValue, approach.DealMagicalItems, approach.BuyPrice, approach.SellPrice, approach.AlternateCurrencyWcid, approach.AlternateCurrencyCount));
        Assert.Equal([2457u, 377, 2460, 379, 378, 27326, 628, 629, 2621, 2622], approach.Items.Select(i => i.Desc.WeenieClassId));
        Assert.All(approach.Items, i => Assert.True(i.IsUnlimited));
        var kit = approach.Items[6];
        Assert.Equal((KitRow, "Handy Healing Kit", 10u, (ushort?)20), (kit.Guid, kit.Desc.Name, kit.Desc.Value ?? 0, kit.Desc.MaxStructure));
        Assert.Equal(170u, approach.Items[1].Desc.Value);
        Assert.Equal([0x80u, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x40000, 0x40000], approach.Items.Select(i => i.Desc.ItemType));
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
    }

    // --- the price rules --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_use_of_Renald_answers_with_his_mask_and_a_stock_with_a_shirt_arrows_and_a_club_and_the_capture_lengths()
    {
        using var rig = new StandInVendorRig();
        var sent = rig.Now;

        rig.Net.SendUse(Renald);
        rig.Run(4.0);

        Assert.Equal(["F74C", "01C7", "0062"], rig.Tags);
        Assert.Equal(sent + rig.World.DelaySeconds + StandInWorld.RenaldOpenDoneSeconds, rig.Cast.First("01C7"), Tolerance);
        Assert.Equal(sent + rig.World.DelaySeconds + StandInWorld.RenaldOpenDoneSeconds + StandInWorld.RenaldApproachLagSeconds, rig.Cast.First("0062"), Tolerance);
        var approach = Assert.Single(rig.World.Approaches);
        Assert.Equal((Renald, 0x480467A7u), (approach.Vendor, approach.MerchandiseItemTypes));
        var names = approach.Items.Select(i => i.Desc.Name).ToArray();
        Assert.Contains("Shirt", names);
        Assert.Contains("Arrow", names);
        Assert.Contains("Club", names);
        Assert.Equal(("Club", 260u, 1u), (approach.Items[1].Desc.Name, approach.Items[1].Desc.Value ?? 0, approach.Items[1].Desc.ItemType));
        Assert.Equal(("Arrow", (ushort?)1000, 1u), (approach.Items[0].Desc.Name, approach.Items[0].Desc.MaxStackSize, approach.Items[0].Desc.Value ?? 0));
    }

    [Theory]
    [InlineData(10u, 0x80u, 14u)]
    [InlineData(50u, 0x80u, 68u)]
    [InlineData(170u, 0x80u, 230u)]
    [InlineData(0u, 0x80u, 1u)]
    [InlineData(1000u, 0x100u, 1350u)]
    [InlineData(500u, 0x40000u, 575u)]
    public void A_vendor_charges_the_ceiling_of_1_35_times_the_Value_less_a_tenth_and_at_least_one_with_the_note_rate_for_a_note(uint value, uint itemType, uint expected) =>
        Assert.Equal(expected, StandInWorld.SellCost(value, itemType));

    [Theory]
    [InlineData(10u, 0x80u, 9)]
    [InlineData(1u, 0x2000u, 1)]
    [InlineData(1100u, 0x2u, 990)]
    [InlineData(2329u, 0x2u, 2096)]
    [InlineData(100u, 0x40000u, 100)]
    public void A_vendor_pays_the_floor_of_0_9_times_the_whole_stacks_Value_plus_a_tenth_and_at_least_one_with_the_note_rate_for_a_note(uint value, uint itemType, int expected) =>
        Assert.Equal(expected, StandInWorld.BuyCost(value, itemType));

    // --- a buy -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_buy_of_a_kit_is_the_spend_the_0022_the_refreshed_0062_the_UseDone_then_the_F745_and_the_sound_and_costs_14()
    {
        using var rig = new StandInVendorRig();
        rig.Open(Thelnoth);

        rig.Net.SendBuy(Thelnoth, [(KitRow, 1)]);
        rig.Run(2.0);

        Assert.Equal(["0197", "02CD", "02CD", "0022", "02CD", "0062", "01C7", "F745", "F750"], rig.Tags);
        Assert.Equal(StandInWorld.PlaceholderPyreals - 14, rig.World.Coins);
        Assert.Equal(StandInWorld.PlaceholderPyreals - 14, rig.Stack(StandInWorld.PyrealGuid));
        var bought = Assert.Single(rig.World.Pack, e => e.Wcid == 628);
        Assert.True(rig.Held(bought.Guid));
        Assert.True(rig.Cast.Session.State.Objects.TryGet(bought.Guid, out var kit));
        Assert.Equal((628u, "Handy Healing Kit", 10u, (ushort?)20, rig.Self), (kit.Weenie.WeenieClassId, kit.Name, kit.Weenie.Value ?? 0, kit.Weenie.Structure, kit.Weenie.ContainerId ?? 0));
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
        Assert.Equal(2, rig.World.Approaches.Count);
    }

    // --- a sale -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_sale_to_Thelnoth_is_the_burden_the_0022_to_the_vendor_the_0062_before_the_coins_and_a_new_stack_that_does_not_merge()
    {
        using var rig = new StandInVendorRig();
        rig.Open(Thelnoth);

        rig.Net.SendSell(Thelnoth, [(StandInWorld.SellKitGuid, 1)]);
        rig.Run(2.0);

        Assert.Equal(["02CD", "0022", "0062", "0022", "02CD", "02CD", "01C7", "F745", "F750"], rig.Tags);
        Assert.False(rig.Held(StandInWorld.SellKitGuid));
        Assert.Equal(StandInWorld.PlaceholderPyreals + 9, rig.World.Coins); // floor(0.9 x 10 + 0.1) = 9
        Assert.Equal(2, rig.World.CoinStacks.Count);
        Assert.Equal((StandInWorld.PyrealGuid, StandInWorld.PlaceholderPyreals), rig.World.CoinStacks[0]);
        Assert.Equal(9, rig.World.CoinStacks[1].Size);
        Assert.True(rig.Cast.Session.State.Objects.TryGet(rig.World.CoinStacks[1].Guid, out var payout));
        Assert.Equal((273u, (ushort?)9, 9u), (payout.Weenie.WeenieClassId, payout.Weenie.StackSize, payout.Weenie.Value ?? 0));
        Assert.Equal(StandInWorld.PlaceholderPyreals, rig.Stack(StandInWorld.PyrealGuid));
        Assert.Equal(10, rig.World.Approaches[^1].Items.Length); // a kit has uses: the vendor does not resell it
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
    }

    // --- the bytes ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_sale_to_Renald_sends_the_0062_after_the_UseDone_as_the_capture_shows_and_the_vendor_lists_the_item_for_resale()
    {
        using var rig = new StandInVendorRig();
        rig.Open(Renald);

        rig.Net.SendSell(Renald, [(StandInWorld.SellBracersGuid, 1)]);
        rig.Run(2.0);

        Assert.Equal(["02CD", "0022", "0022", "02CD", "02CD", "01C7", "0062", "F745", "F750"], rig.Tags);
        Assert.Equal(StandInWorld.PlaceholderPyreals + 990, rig.World.Coins); // floor(0.9 x 1100 + 0.1)
        var resale = rig.World.Approaches[^1];
        Assert.Equal(9, resale.Items.Length);
        var row = resale.Items[^1];
        Assert.Equal((1, StandInWorld.SellBracersGuid, "Leather Bracers"), (row.StackSize, row.Guid, row.Desc.Name));

        rig.Fresh();
        rig.Net.SendBuy(Renald, [(StandInWorld.SellBracersGuid, 1)]);
        rig.Run(2.0);

        Assert.Equal(["0197", "02CD", "02CD", "0022", "02CD", "0062", "01C7", "F745", "F750"], rig.Tags);
        Assert.Equal(StandInWorld.PlaceholderPyreals + 990 - 1485, rig.World.Coins); // ceil(1.35 x 1100 - 0.1)
        Assert.True(rig.Held(StandInWorld.SellBracersGuid));
        Assert.Equal(8, rig.World.Approaches[^1].Items.Length);
    }

    [Fact]
    public void The_stand_ins_0062_is_the_record_ACEs_bytes_decode_to()
    {
        using var rig = new StandInVendorRig();
        rig.Net.SendUse(Renald);
        rig.Run(4.0);
        var sent = rig.World.Approaches[^1];
        var rows = sent.Items.Select(i => new AceVendorRow(i.StackSize, new AceCreateObject
        {
            Guid = i.Guid,
            Name = i.Desc.Name,
            WeenieClassId = i.Desc.WeenieClassId,
            Icon = i.Desc.IconId,
            ItemType = i.Desc.ItemType,
            ObjectDescriptionFlags = i.Desc.ObjectDescriptionFlags,
            Value = (int?)i.Desc.Value,
            Burden = i.Desc.Burden,
            MaxStackSize = i.Desc.MaxStackSize,
            Structure = i.Desc.Structure,
            MaxStructure = i.Desc.MaxStructure,
        })).ToList();
        var bytes = AceVendorWriters.ApproachVendor(rig.Self, 1, sent.Vendor, sent.MerchandiseItemTypes, sent.MerchandiseMinValue, sent.MerchandiseMaxValue, sent.DealMagicalItems, sent.BuyPrice, sent.SellPrice, rows);
        var decoded = new Recorder();

        var result = InboundDispatcher.Dispatch(new InboundContext(1, MessageQueue.Ui, 1), bytes, decoded);

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        var back = Assert.Single(decoded.Approaches);
        Assert.Equal((sent.Vendor, sent.MerchandiseItemTypes, sent.BuyPrice, sent.SellPrice, sent.Items.Length), (back.Vendor, back.MerchandiseItemTypes, back.BuyPrice, back.SellPrice, back.Items.Length));
        Assert.Equal(sent.Items.Select(i => (i.StackSize, i.Guid, i.Desc.Name, i.Desc.WeenieClassId, i.Desc.Value, i.Desc.Burden, i.Desc.MaxStackSize)), back.Items.Select(i => (i.StackSize, i.Guid, i.Desc.Name, i.Desc.WeenieClassId, i.Desc.Value, i.Desc.Burden, i.Desc.MaxStackSize)));
    }

    internal sealed class Recorder : IInboundMessageHandler
    {
        public List<VendorApproachMsg> Approaches { get; } = [];

        public List<IdentifyResponseMsg> Identified { get; } = [];

        public void OnApproachVendor(in InboundContext context, in GameEventHeader header, VendorApproachMsg message) => Approaches.Add(message);

        public void OnIdentifyResponse(in InboundContext context, in GameEventHeader header, IdentifyResponseMsg message) => Identified.Add(message);
    }
}
