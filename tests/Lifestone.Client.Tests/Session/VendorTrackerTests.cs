using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s1: <c>OpenVendor</c>, <c>Buy</c> and <c>Sell</c> over the order ACE really sends (cache/m4/3-2/capture.md C1, C2, live, ACE 95ef426d). The open is Confirmed on the 0062 (1.04 s after the UseDone); a buy is the spend (0197,
/// 02CD CoinValue), the 0022 of the new kit, the refreshed 0062, UseDone 0, then the kit's F745 3 ms later; a sale is the 0022 to the vendor, the 0062, the payout's 0022, the CoinValue, UseDone 0 and the payout's F745; a refusal is a 02EB
/// line, a 00A0 with the player's own guid and error 0, and UseDone 0.
/// </summary>
public sealed class VendorTrackerTests
{
    private const uint Kit = 0x80001097;

    // --- OpenVendor -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_open_sends_Use_and_is_Confirmed_on_the_0062_1_04_s_after_the_UseDone_not_on_the_UseDone()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();

        var sent = Assert.Single(rig.H.Net.Of(FakeSendKind.Use));
        Assert.Equal(VendorRig.Thelnoth, sent.Guid);
        Assert.Equal(ActionKind.OpenVendor, handle.Kind);
        Assert.Equal(ActionStatus.Sent, handle.Status);

        rig.Tick(0.833); // C1: UseDone 0 at +833 ms
        rig.UseDone();
        rig.Tick(0.5);
        Assert.False(handle.IsCompleted); // the shop is not open yet

        rig.Tick(0.54);
        rig.ThelnothStock(); // +1876 ms: the 0062
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("4 rows", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(VendorRig.Thelnoth, rig.State.Vendor!.Vendor);
    }

    [Fact]
    public void A_UseDone_with_a_code_is_a_ServerError_naming_it()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();

        rig.UseDone(0x001D);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x001Du, outcome.Code);
        Assert.Contains("YoureTooBusy", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_UseDone_0_with_no_0062_within_the_margin_is_Refused_vendor_did_not_open()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();
        rig.Tick(0.833);
        rig.UseDone();

        rig.Tick(ActionPolicy.OpenVendorMarginSeconds - 0.1);
        Assert.False(handle.IsCompleted);
        rig.Tick(0.2);

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Refused, outcome.Kind);
        Assert.Contains("vendor did not open", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(rig.State.Vendor);
    }

    [Fact]
    public void The_refusal_names_the_line_the_server_said()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();
        rig.Transient("The shop is closed.");
        rig.UseDone();

        rig.Tick(ActionPolicy.OpenVendorMarginSeconds + 0.1);

        Assert.Contains("The shop is closed.", handle.Outcome!.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_0062_for_another_vendor_does_not_confirm_the_open()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();
        rig.UseDone();
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []);
        rig.Tick(ActionPolicy.OpenVendorMarginSeconds + 0.1);

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_second_open_of_an_open_vendor_is_sent_again_and_Confirmed_by_the_refresh()
    {
        var rig = new VendorRig();
        rig.Opened();

        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();
        Assert.False(handle.IsCompleted); // the old stock does not count: only a 0062 since the send does
        rig.UseDone();
        rig.Tick(0.7);
        rig.ThelnothStock();
        rig.Tick();

        Assert.Equal(2, rig.Sent(FakeSendKind.Use));
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_0062_before_the_UseDone_confirms_and_the_lane_holds_until_the_UseDone_is_taken()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();
        rig.ThelnothStock();
        rig.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);

        var use = rig.Api.Use(0x80000001, ActionSource.Ui); // a later Use must not take the open's UseDone
        rig.Tick();
        rig.UseDone();
        rig.Tick();
        Assert.False(use.IsCompleted);

        rig.UseDone();
        rig.Tick();
        Assert.True(use.IsCompleted);
    }

    [Fact]
    public void An_open_with_no_UseDone_times_out_after_20_s()
    {
        var rig = new VendorRig();
        var handle = rig.Api.OpenVendor(VendorRig.Thelnoth, ActionSource.Ui);
        rig.Tick();

        rig.Tick(ActionPolicy.OpenVendorSeconds + 0.1);

        Assert.Equal(ActionOutcomeKind.TimedOut, handle.Outcome!.Value.Kind);
    }

    [Fact]
    public void An_open_of_guid_0_is_Refused_and_sends_nothing()
    {
        var rig = new VendorRig();

        var handle = rig.Api.OpenVendor(0, ActionSource.Ui);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.Equal(0, rig.Sent(FakeSendKind.Use));
    }

    // --- Buy --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The player with 10,000 pyreals in a stack, Thelnoth open, and one Buy of the kit sent (C1: GA 005F at 19819).</summary>
    private static (VendorRig Rig, VendorTradeHandle Handle) BuyOneKit(int coins = 10000)
    {
        var rig = new VendorRig(coins);
        rig.OwnCoins((uint)coins);
        rig.Opened();
        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.KitRow, 1u)], ActionSource.Ui);
        rig.Tick();
        return (rig, handle);
    }

    /// <summary>C1's answer to the buy up to the UseDone: 0197, 02CD CoinValue, the kit's 0022, the refreshed 0062, UseDone 0.</summary>
    private static void KitBought(VendorRig rig, int coinsAfter = 9986)
    {
        rig.Tick(0.009);
        rig.Stack(VendorRig.CoinStack, (uint)coinsAfter, (uint)coinsAfter);
        rig.Coins(coinsAfter);
        rig.Put(Kit);
        rig.Tick(0.003);
        rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, VendorRig.ThelnothRows());
        rig.UseDone();
    }

    [Fact]
    public void A_buy_sends_0x005F_with_the_vendor_and_the_rows()
    {
        var (rig, handle) = BuyOneKit();

        var sent = Assert.Single(rig.H.Net.Of(FakeSendKind.Buy));
        Assert.Equal(VendorRig.Thelnoth, sent.Guid);
        Assert.Equal([(VendorRig.KitRow, 1u)], sent.Items);
        Assert.Equal(ActionKind.Buy, handle.Kind);
        Assert.Equal(ActionStatus.Sent, handle.Status);
    }

    [Fact]
    public void A_buy_is_Confirmed_after_the_kits_F745_with_the_coin_delta_equal_to_the_predicted_price_14()
    {
        var (rig, handle) = BuyOneKit();

        KitBought(rig);
        rig.Tick();
        Assert.False(handle.IsCompleted); // the kit's F745 follows the UseDone by about 3 ms (C1)

        rig.Tick(0.003);
        rig.Arrive(Kit, "Handy Healing Kit", 628, VendorRig.MiscType, 10);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("14 pyreals paid (as predicted)", outcome.Detail, StringComparison.Ordinal);
        var result = Assert.IsType<VendorTradeResult>(handle.Result);
        Assert.True(result.IsBuy);
        Assert.Equal(14, result.Predicted);
        Assert.Equal(14, result.Actual);
        Assert.Equal(1, result.ObjectsExpected);
        Assert.Equal(1, result.ObjectsArrived);
        Assert.True(result.Matched);
        Assert.Equal(9986, rig.State.Self.Qualities.CoinValue);
    }

    [Fact]
    public void A_coin_delta_that_differs_from_the_prediction_is_a_WARN_in_the_detail_and_the_log_not_a_failure()
    {
        var (rig, handle) = BuyOneKit();

        KitBought(rig, coinsAfter: 9987); // 13 paid, 14 predicted
        rig.Arrive(Kit, "Handy Healing Kit", 628, VendorRig.MiscType, 10);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.True(outcome.Succeeded);
        Assert.Contains("WARN", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("13", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("14", outcome.Detail, StringComparison.Ordinal);
        var result = handle.Result!;
        Assert.False(result.Matched);
        Assert.Equal(13, result.Actual);
        Assert.Contains(rig.H.R.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("predict", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kit_that_never_gets_an_F745_is_a_WARN_after_5_s_not_a_failure()
    {
        var (rig, handle) = BuyOneKit();

        KitBought(rig);
        rig.Tick(ActionPolicy.TradeObjectsSeconds - 0.1);
        Assert.False(handle.IsCompleted);
        rig.Tick(0.2);

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("no F745", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(0, handle.Result!.ObjectsArrived);
        Assert.Equal(1, handle.Result.ObjectsExpected);
    }

    [Fact]
    public void Too_few_pyreals_is_a_00A0_with_no_text_and_the_detail_names_the_shortfall()
    {
        var (rig, handle) = BuyOneKit(coins: 5);

        // ACE: Vendor.cs 546-552 sends no text; the refusal is 00A0 {the player's guid, 0} and UseDone 0.
        rig.Tick(0.005);
        rig.Refusal();
        rig.UseDone();
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0u, outcome.Code);
        Assert.Contains("too few pyreals", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("14", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
        Assert.Equal(5, rig.State.Self.Qualities.CoinValue);
    }

    [Fact]
    public void A_refusal_names_the_02EB_line_the_server_sent_first()
    {
        var (rig, handle) = BuyOneKit();

        rig.Tick(0.005);
        rig.Transient("You do not have enough pack space to buy that!");
        rig.Refusal();
        rig.UseDone();
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("You do not have enough pack space to buy that!", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("too few pyreals", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_028B_refusal_text_is_the_reason_and_the_channel_noise_is_not()
    {
        var (rig, handle) = BuyOneKit();

        rig.Tick(0.005);
        rig.ErrorText(0x051C, "General"); // the chat channels left at a logoff: noise
        rig.ErrorText(0x001E, "Thelnoth Cort the Healer"); // _IsTooBusyToAcceptGifts: a service row while the vendor is busy
        rig.Refusal();
        rig.UseDone();
        rig.Tick();

        var detail = handle.Outcome!.Value.Detail!;
        Assert.Contains("too busy", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("channel", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_busy_UseDone_is_a_ServerError_with_the_code_and_a_trade_in_progress_too()
    {
        var (rig, handle) = BuyOneKit();

        rig.Tick(0.002);
        rig.Refusal();
        rig.UseDone(0x001D);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x001Du, outcome.Code);
        Assert.Contains("YoureTooBusy", outcome.Detail, StringComparison.Ordinal);

        var trading = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.KitRow, 1u)], ActionSource.Ui);
        rig.Tick();
        rig.UseDone(0x0506);
        rig.Tick();
        Assert.Equal(0x0506u, trading.Outcome!.Value.Code);
        Assert.Contains("CantDoThatTradeInProgress", trading.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_potions_are_predicted_and_charged_per_object()
    {
        var rig = new VendorRig();
        rig.OwnCoins(10000);
        rig.Opened();
        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.PotionRow, 2u)], ActionSource.Ui);
        rig.Tick();
        var p1 = rig.NextGuid();
        var p2 = rig.NextGuid();

        rig.Tick(0.009);
        rig.Coins(10000 - 460);
        rig.Put(p1);
        rig.Put(p2);
        rig.UseDone();
        rig.Arrive(p1, "Potion of Healing", 377, VendorRig.MiscType, 170);
        rig.Arrive(p2, "Potion of Healing", 377, VendorRig.MiscType, 170);
        rig.Tick();

        var result = handle.Result!;
        Assert.Equal(460, result.Predicted);
        Assert.Equal(460, result.Actual);
        Assert.Equal(2, result.ObjectsArrived);
        Assert.True(result.Matched);
    }

    [Fact]
    public void An_unknown_guid_is_left_out_of_the_prediction_and_named_ACE_skips_it_silently()
    {
        var rig = new VendorRig();
        rig.OwnCoins(10000);
        rig.Opened();
        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(0x80009999u, 1u)], ActionSource.Ui);
        rig.Tick();

        rig.UseDone(); // nothing bought, no 00A0
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("not in the open stock", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(0, handle.Result!.Actual);
    }

    [Fact]
    public void A_buy_with_no_vendor_open_has_no_prediction_and_says_so()
    {
        var rig = new VendorRig();
        rig.OwnCoins(10000);
        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.KitRow, 1u)], ActionSource.Ui);
        rig.Tick();

        rig.Coins(9986);
        rig.UseDone();
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.Contains("no prediction", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(-1, handle.Result!.Predicted);
        Assert.Equal(14, handle.Result.Actual);
    }

    [Theory]
    [InlineData(0u, 1u, 1u)]
    [InlineData(VendorRig.Thelnoth, 0u, 1u)]
    [InlineData(VendorRig.Thelnoth, VendorRig.KitRow, 0u)]
    public void A_buy_that_cannot_make_sense_is_Refused_and_sends_nothing(uint vendor, uint item, uint amount)
    {
        var rig = new VendorRig();

        var handle = rig.Api.Buy(vendor, [(item, amount)], ActionSource.Ui);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.Equal(0, rig.Sent(FakeSendKind.Buy));
    }

    [Fact]
    public void A_buy_of_nothing_and_an_amount_above_Int32_are_Refused()
    {
        var rig = new VendorRig();

        var empty = rig.Api.Buy(VendorRig.Thelnoth, [], ActionSource.Ui);
        var huge = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.KitRow, (uint)int.MaxValue + 1)], ActionSource.Ui);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, empty.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, huge.Outcome!.Value.Kind);
        Assert.Equal(0, rig.Sent(FakeSendKind.Buy));
    }

    [Fact]
    public void Vendor_actions_go_one_at_a_time_in_call_order()
    {
        var (rig, first) = BuyOneKit();
        var second = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.PotionRow, 1u)], ActionSource.Ui);
        rig.Tick();
        Assert.Equal(1, rig.Sent(FakeSendKind.Buy)); // the second waits: ACE's one UseDone names no request

        KitBought(rig);
        rig.Arrive(Kit, "Handy Healing Kit", 628, VendorRig.MiscType, 10);
        rig.Tick();
        Assert.True(first.IsCompleted);
        rig.Tick();

        Assert.Equal(2, rig.Sent(FakeSendKind.Buy));
        Assert.Equal(ActionStatus.Sent, second.Status);
    }

    // --- Sell -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Thelnoth open, the player with 9,986 pyreals and the kit bought (C1), and a sale of the kit sent (GA 0060 at 23054).</summary>
    private static (VendorRig Rig, VendorTradeHandle Handle) SellKit()
    {
        var rig = new VendorRig(9986);
        rig.OwnCoins(9986);
        rig.Own(Kit, "Handy Healing Kit", VendorRig.MiscType, 10);
        rig.Opened();
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [Kit], ActionSource.Ui);
        rig.Tick();
        return (rig, handle);
    }

    [Fact]
    public void A_sale_sends_0x0060_with_the_whole_stack_as_the_amount()
    {
        var rig = new VendorRig(9986);
        rig.Opened();
        rig.Own(Kit, "Handy Healing Kit", VendorRig.MiscType, 50, stackSize: 5);

        var handle = rig.Api.Sell(VendorRig.Thelnoth, [Kit], ActionSource.Ui);
        rig.Tick();

        var sent = Assert.Single(rig.H.Net.Of(FakeSendKind.Sell));
        Assert.Equal(VendorRig.Thelnoth, sent.Guid);
        Assert.Equal([(Kit, 5u)], sent.Items);
        Assert.Equal(ActionKind.Sell, handle.Kind);
    }

    [Fact]
    public void A_sale_is_Confirmed_after_the_payouts_F745_with_the_payout_equal_to_the_predicted_9()
    {
        var (rig, handle) = SellKit();

        // C1: 02CD Enc, 0022 {kit, vendor}, the refreshed 0062, 0022 {payout stack, player}, 02CD Enc, 02CD CoinValue 9995, UseDone 0, then at +5 ms the payout's F745.
        rig.Tick(0.016);
        rig.Put(Kit, VendorRig.Thelnoth);
        rig.ThelnothStock();
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(9995);
        rig.UseDone();
        rig.Tick();
        Assert.False(handle.IsCompleted);

        rig.Tick(0.005);
        rig.Arrive(payout, "Pyreal", 273, 0x40, 9, stackSize: 9);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Contains("9 pyreals received (as predicted)", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("1 item(s) left the pack", outcome.Detail, StringComparison.Ordinal);
        var result = handle.Result!;
        Assert.False(result.IsBuy);
        Assert.Equal(9, result.Predicted);
        Assert.Equal(9, result.Actual);
        Assert.Equal(1, result.ObjectsExpected);
        Assert.True(result.Matched);
    }

    [Fact]
    public void A_00A0_naming_another_item_mid_sale_is_not_a_vendor_refusal_and_the_sale_is_still_Confirmed()
    {
        // 3.2-R1: a refused pickup, drop or merge on the Move lane (its 00A0 names that item) lands while a sale is in flight.
        var (rig, handle) = SellKit();

        rig.Tick(0.016);
        rig.ItemRefusal(0x80009999u);
        rig.Put(Kit, VendorRig.Thelnoth);
        rig.ThelnothStock();
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(9995);
        rig.UseDone();
        rig.Tick();
        rig.Tick(0.005);
        rig.Arrive(payout, "Pyreal", 273, 0x40, 9, stackSize: 9);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.Equal(9, handle.Result!.Actual);
    }

    [Fact]
    public void A_sale_does_not_wait_for_the_refreshed_0062_that_comes_after_the_UseDone()
    {
        // C2 (Renald, the scroll): 0022 {item, vendor}, 02BD, 0022 {payout, player}, 02CD CoinValue, UseDone 0, THEN the refreshed 0062 (payout 1, the max(1, ...) floor).
        var rig = new VendorRig(9995);
        rig.OwnCoins(9995);
        rig.Place(VendorRig.Renald, "Shopkeeper Renald the Elder", x: 14, y: 20, useRadius: 5);
        rig.Own(Kit, "Scroll of Leadership Ineptitude", VendorRig.ScrollType, 1);
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []);
        rig.Events.Clear();
        var handle = rig.Api.Sell(VendorRig.Renald, [Kit], ActionSource.Ui);
        rig.Tick();

        rig.Tick(0.005);
        rig.Put(Kit, VendorRig.Renald);
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(9996);
        rig.UseDone();
        rig.Arrive(payout, "Pyreal", 273, 0x40, 1, stackSize: 1);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind); // before the 0062
        Assert.Equal(1, handle.Result!.Predicted);
        Assert.Equal(1, handle.Result.Actual);
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []);
        Assert.Single(rig.Events.OfType<VendorStockChanged>());
    }

    [Fact]
    public void A_stack_is_paid_on_its_whole_value()
    {
        var rig = new VendorRig(1000);
        rig.OwnCoins(1000);
        rig.Opened();
        rig.Own(Kit, "Handy Healing Kit", VendorRig.MiscType, 5, stackSize: 5); // five of Value 1: the stack pays floor(4.5 + 0.1) = 4, not 5

        var handle = rig.Api.Sell(VendorRig.Thelnoth, [Kit], ActionSource.Ui);
        rig.Tick();
        rig.Put(Kit, VendorRig.Thelnoth);
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(1004);
        rig.UseDone();
        rig.Arrive(payout, "Pyreal", 273, 0x40, 4, stackSize: 4);
        rig.Tick();

        Assert.Equal(4, handle.Result!.Predicted);
        Assert.True(handle.Result.Matched);
    }

    [Fact]
    public void An_unsellable_item_is_the_02EB_then_the_00A0_then_UseDone_0_and_stays_in_the_pack()
    {
        // C2: Leather Bracers (ItemType 2, Value 2,329) offered to Thelnoth, whose mask is 0x44080: 02EB "The Leather Bracers is unsellable.", GE 00A0 {the player, 0}, UseDone 0. No F7E0; the item stays.
        var rig = new VendorRig(9996);
        rig.OwnCoins(9996);
        rig.Own(Kit, "Leather Bracers", VendorRig.ArmorType, 2329);
        rig.Opened();
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [Kit], ActionSource.Ui);
        rig.Tick();

        rig.Tick(0.005);
        rig.Transient("The Leather Bracers is unsellable.");
        rig.Refusal();
        rig.UseDone();
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0u, outcome.Code);
        Assert.Contains("The Leather Bracers is unsellable.", outcome.Detail, StringComparison.Ordinal);
        Assert.Null(handle.Result);
        Assert.True(rig.State.Objects.TryGet(Kit, out _));
        Assert.Equal(9996, rig.State.Self.Qualities.CoinValue);
        Assert.Equal(4, rig.State.Vendor!.Entries.Length); // the stock is untouched
    }

    [Fact]
    public void A_sale_of_two_with_one_unsellable_goes_on_for_the_other_and_names_the_line()
    {
        var rig = new VendorRig(9986);
        rig.OwnCoins(9986);
        rig.Own(Kit, "Handy Healing Kit", VendorRig.MiscType, 10);
        const uint bracers = 0x80001098;
        rig.Own(bracers, "Leather Bracers", VendorRig.ArmorType, 2329);
        rig.Opened();
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [bracers, Kit], ActionSource.Ui);
        rig.Tick();

        rig.Transient("The Leather Bracers is unsellable."); // VerifySellItems drops it and goes on
        rig.Put(Kit, VendorRig.Thelnoth);
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(9995);
        rig.UseDone();
        rig.Arrive(payout, "Pyreal", 273, 0x40, 9, stackSize: 9);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Equal(9, handle.Result!.Predicted); // the bracers are left out of the prediction: the mask does not take armour
        Assert.True(handle.Result.Matched);
        Assert.Contains("The Leather Bracers is unsellable.", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sale_of_five_with_two_refused_by_the_server_is_predicted_over_the_three_that_left_and_names_the_two()
    {
        // 3.2-L1 (live): ACE refuses an item inside the mask with "The Bread is unsellable." and pays for the rest; the prediction counts what left the pack only.
        var rig = new VendorRig(9986);
        rig.OwnCoins(9986);
        const uint a = 0x800010A1, b = 0x800010A2, c = 0x800010A3, bread = 0x800010A4, apple = 0x800010A5;
        rig.Own(a, "Handy Healing Kit", VendorRig.MiscType, 10);
        rig.Own(b, "Mana Charge", VendorRig.MiscType, 20);
        rig.Own(c, "Salvage Bag", VendorRig.MiscType, 30);
        rig.Own(bread, "Bread", VendorRig.MiscType, 100);
        rig.Own(apple, "Apple", VendorRig.MiscType, 200);
        rig.Opened();
        var handle = rig.Api.Sell(VendorRig.Thelnoth, [a, bread, b, apple, c], ActionSource.Ui);
        rig.Tick();

        rig.Transient("The Bread is unsellable.");
        rig.Transient("The Apple is unsellable.");
        rig.Put(a, VendorRig.Thelnoth);
        rig.Put(b, VendorRig.Thelnoth);
        rig.Put(c, VendorRig.Thelnoth);
        var payout = rig.NextGuid();
        rig.Put(payout);
        rig.Coins(9986 + 54);
        rig.UseDone();
        rig.Arrive(payout, "Pyreal", 273, 0x40, 54, stackSize: 54);
        rig.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        var result = handle.Result!;
        Assert.Equal(54, result.Predicted); // 9 + 18 + 27, not the 5 items' 9 + 18 + 27 + 90 + 180
        Assert.Equal(54, result.Actual);
        Assert.True(result.Matched);
        Assert.DoesNotContain("WARN", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("2 item(s) stayed in the pack", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("Bread", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("Apple", outcome.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(VendorRig.Thelnoth, 0u)]
    public void A_sale_that_cannot_make_sense_is_Refused_and_sends_nothing(uint vendor, uint item)
    {
        var rig = new VendorRig();

        var handle = rig.Api.Sell(vendor, [item], ActionSource.Ui);
        var empty = rig.Api.Sell(VendorRig.Thelnoth, [], ActionSource.Ui);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, empty.Outcome!.Value.Kind);
        Assert.Equal(0, rig.Sent(FakeSendKind.Sell));
    }

    [Fact]
    public void A_trade_with_no_UseDone_times_out()
    {
        var (rig, handle) = SellKit();

        rig.Tick(ActionPolicy.TradeSeconds + 0.1);

        Assert.Equal(ActionOutcomeKind.TimedOut, handle.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_trade_before_the_world_is_Refused()
    {
        var rig = new VendorRig();
        rig.H.R.Reducer.OnSessionEvent(Lifestone.Net.Session.SessionEvent.LoggedOff(rig.H.R.Time.GetTimestamp(), Lifestone.Net.Session.SessionPhase.CharacterSelect));

        var handle = rig.Api.Buy(VendorRig.Thelnoth, [(VendorRig.KitRow, 1u)], ActionSource.Ui);
        rig.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
    }
}
