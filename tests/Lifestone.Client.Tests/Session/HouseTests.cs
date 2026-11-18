using System.Collections.Immutable;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M6 villa: the housing read model (GameEvent 0x021D, 0x0225, 0x0226), the house actions (BuyHouse 0x021C, RentHouse 0x0221, TeleToHouse 0x0262,
/// HouseQuery 0x021E) and how ACE's answers complete or refuse them (@95ef426d <c>Player_House.cs</c>, <c>Player_Location.cs:59-123</c>), and the guardian
/// window's panes and buttons (<see cref="GuardianWindowModel"/>).
/// </summary>
public sealed class HouseTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Guardian = 0x7A9B4F10;
    private const uint Coins1 = 0x80000201;
    private const uint Coins2 = 0x80000202;
    private const uint Coins3 = 0x80000203;
    private const uint Writ = 0x80000204;
    private const uint Lockpick = 0x80000205;
    private const uint Note = 0x80000206;
    private const uint WritWcid = 11710;
    private const uint LockpickWcid = 511;

    private static readonly ImmutableArray<HousePayment> ApartmentPrice = [new(100_000, 0, 273, "Pyreal", "Pyreals"), new(1, 0, WritWcid, "Writ of Refuge", "Writs of Refuge")];

    private static HouseProfileMsg Profile(uint owner = 0, ImmutableArray<HousePayment>? rent = null, uint guardian = Guardian, HouseType type = HouseType.Apartment) =>
        new(guardian, 1, owner, HouseProfileMsg.ActiveFlag, -1, -1, -1, -1, false, (uint)type, owner == 0 ? string.Empty : "Tester", ApartmentPrice, rent ?? ApartmentPrice);

    private static void Open(WorldActionRig r, HouseProfileMsg profile) =>
        r.H.R.Reducer.OnHouseProfile(r.H.R.Context, new GameEventHeader(Player, 3, GameEventTypes.HouseProfile), profile);

    private static void Data(WorldActionRig r, HouseType type, uint rentTime) =>
        r.H.R.Reducer.OnHouseData(r.H.R.Context, new GameEventHeader(Player, 4, GameEventTypes.HouseData), new HouseDataMsg(rentTime, rentTime, (uint)type, false, ApartmentPrice, ApartmentPrice, ReducerHarness.Position(0x9DAF0102)));

    /// <summary>A rig in the world holding three coin stacks (25,000, 25,000 and 60,000 pyreals are not one stack: 25,000 is the pyreal's max; the third is 25,000 too), a writ, a lockpick and a 100,000 trade note.</summary>
    private static WorldActionRig Holding(params (uint Guid, uint Wcid, ushort Stack, uint Value, uint ItemType)[] items)
    {
        var r = new WorldActionRig();
        r.Describe(pack: [.. items.Select(static i => new ContentDesc(i.Guid, 0))]);
        foreach (var (guid, wcid, stack, value, itemType) in items)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Item", itemType: itemType);
            r.H.R.Reducer.OnCreateObject(
                r.H.R.Context,
                basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.Value, ContainerId = Player, WeenieClassId = wcid, StackSize = stack, Value = value } },
                isUpdate: false);
        }

        return r;
    }

    private static WorldActionRig Rich() => Holding(
        (Coins1, 273, 25_000, 25_000, 0x40),
        (Coins2, 273, 25_000, 25_000, 0x40),
        (Coins3, 273, 20_000, 20_000, 0x40),
        (Writ, WritWcid, 3, 0, 0x10000000),
        (Lockpick, LockpickWcid, 1, 1, 0x100),
        (Note, 2627, 1, 100_000, VendorStock.PromissoryNoteType));

    [Fact]
    public void A_profile_opens_the_guardian_a_second_refreshes_it_and_another_guardian_closes_the_first()
    {
        var r = new WorldActionRig();
        Open(r, Profile());
        var first = r.H.State.Guardian!;
        Assert.Equal((Guardian, HouseType.Apartment, false, false), (first.Guardian, first.Type, first.IsOwned, first.IsRentPaid));
        Assert.Equal(100_000, HouseGuardian.Line(first.Buy, 273)!.Required);

        Open(r, Profile(owner: Player, rent: [new(100_000, 100_000, 273, "Pyreal", "Pyreals"), new(1, 1, WritWcid, "Writ of Refuge", "Writs of Refuge")]));
        Assert.True(r.H.State.Guardian!.IsRentPaid);
        Assert.True(r.H.State.Guardian.Revision > first.Revision);

        Open(r, Profile(guardian: Guardian + 1));
        Assert.Single(r.H.R.Of<GuardianOpened>(), e => e.Guardian == Guardian);
        Assert.Single(r.H.R.Of<GuardianProfileChanged>());
        Assert.Equal(new GuardianClosed(Guardian), Assert.Single(r.H.R.Of<GuardianClosed>()));
        Assert.Equal(Guardian + 1, r.H.State.Guardian!.Guardian);
    }

    [Fact]
    public void House_data_makes_the_house_owned_with_ACE_s_rent_period_and_a_status_says_none()
    {
        var r = new WorldActionRig();
        Assert.Equal(HouseOwnership.Unknown, r.H.State.HouseStatus);

        Data(r, HouseType.Apartment, 1_700_000_000);
        Assert.Equal(HouseOwnership.Owned, r.H.State.HouseStatus);
        Assert.Equal(1_700_000_000u + (90u * 24 * 3600), r.H.State.House!.RentDue);
        var revision = r.H.State.HouseInfoRevision;

        Data(r, HouseType.Villa, 1_700_000_000);
        Assert.Equal(1_700_000_000u + (30u * 24 * 3600), r.H.State.House!.RentDue);

        r.H.R.Reducer.OnHouseStatus(r.H.R.Context, new GameEventHeader(Player, 5, GameEventTypes.HouseStatus), new HouseStatusEvent(HouseStatusEvent.NoHouse));
        Assert.Equal(HouseOwnership.None, r.H.State.HouseStatus);
        Assert.Null(r.H.State.House);
        Assert.True(r.H.State.HouseInfoRevision > revision);
    }

    [Fact]
    public void Buy_sends_0x021C_with_the_placed_stacks_and_is_confirmed_by_ACE_s_line()
    {
        var r = Rich();
        Open(r, Profile());
        var buy = r.Api.BuyHouse(Guardian, [Coins1, Writ], ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.BuyHouse));
        Assert.Equal(Guardian, sent.Guid);
        Assert.Equal([Coins1, Writ], sent.Items!.Select(static i => i.item));
        r.H.ServerMessage(HousePaymentTracker.BoughtLine, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, buy.Outcome!.Value.Kind);
    }

    [Theory]
    [InlineData(0x0532u, "YouMustWaitToPurchaseHouse")]
    [InlineData(0x0488u, "YouMustBeAboveLevel_ToBuyHouse")]
    public void A_purchase_refused_by_a_WeenieError_fails_with_its_name(uint code, string name)
    {
        var r = Rich();
        var buy = r.Api.BuyHouse(Guardian, [Coins1], ActionSource.Ui);
        r.Tick();
        r.WeenieError(code);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, buy.Outcome!.Value.Kind);
        Assert.Contains(name, buy.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purchase_refused_by_ACE_s_lines_fails_and_a_silent_one_times_out()
    {
        var r = Rich();
        var owned = r.Api.BuyHouse(Guardian, [Coins1], ActionSource.Ui);
        r.Tick();
        r.H.ServerMessage(HousePaymentTracker.AlreadyOwnLine, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, owned.Outcome!.Value.Kind);

        var young = r.Api.BuyHouse(Guardian, [Coins1], ActionSource.Ui);
        r.Tick();
        r.H.ServerMessage(HousePaymentTracker.AccountAgeLinePrefix + " This applies to all housing except apartments.", 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, young.Outcome!.Value.Kind);

        var silent = r.Api.BuyHouse(Guardian, [Coins1], ActionSource.Ui);
        r.Tick();
        r.Tick(ActionPolicyHouse.TradeSeconds + 1);
        Assert.Equal(ActionOutcomeKind.TimedOut, silent.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_payment_naming_an_item_the_player_does_not_hold_or_nothing_is_refused_and_sends_nothing()
    {
        var r = Rich();
        var stranger = r.Api.BuyHouse(Guardian, [0x80009999], ActionSource.Ui);
        var empty = r.Api.PayRent(Guardian, [], ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, stranger.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, empty.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.BuyHouse));
        Assert.Empty(r.H.Net.Of(FakeSendKind.RentHouse));
    }

    [Fact]
    public void Rent_sends_0x0221_and_reads_a_partial_payment_and_the_already_paid_refusal()
    {
        var r = Rich();
        var rent = r.Api.PayRent(Guardian, [Coins3], ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.RentHouse));
        r.H.ServerMessage(HousePaymentTracker.RentPartiallyPaidLine, 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, rent.Outcome!.Value.Kind);
        Assert.Contains(HousePaymentTracker.PartialDetail, rent.Outcome.Value.Detail, StringComparison.Ordinal);

        var again = r.Api.PayRent(Guardian, [Coins1], ActionSource.Ui);
        r.Tick();
        r.H.ServerMessage(HousePaymentTracker.RentAlreadyPaidPrefix + "\nYou may not prepay next period's maintenance.", 0);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, again.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_house_recall_sends_0x0262_and_is_confirmed_by_the_teleport_or_refused_without_a_house()
    {
        var r = new WorldActionRig();
        var recall = r.Api.RecallHouse(ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.TeleToHouse));
        r.Raw(AceWireWriters.PlayerTeleport(2));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, recall.Outcome!.Value.Kind);

        var homeless = r.Api.RecallHouse(ActionSource.Ui);
        r.Tick();
        r.WeenieError(0x047F);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, homeless.Outcome!.Value.Kind);
        Assert.Contains("YouMustOwnHouseToUseCommand", homeless.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_house_query_is_confirmed_by_the_status_that_answers_it()
    {
        var r = new WorldActionRig();
        var query = r.Api.QueryHouse(ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.HouseQuery));
        Assert.False(query.IsCompleted);

        r.H.R.Reducer.OnHouseStatus(r.H.R.Context, new GameEventHeader(Player, 5, GameEventTypes.HouseStatus), new HouseStatusEvent(HouseStatusEvent.NoHouse));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, query.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_window_fills_the_purchase_pane_like_a_player_and_Buy_sends_exactly_those_stacks()
    {
        var r = Rich();
        var window = new GuardianWindowModel(r.H.State, r.Api);
        Open(r, Profile());

        Assert.True(window.FillFromPacks(GuardianPane.Buy), window.ErrorLine);
        Assert.True(window.Covers(GuardianPane.Buy));
        Assert.Equal([Coins1, Coins2, Coins3, Note, Writ], window.Placed(GuardianPane.Buy)); // the coins largest first (70,000), then the note covers the rest
        var press = window.PressBuy(ActionSource.Automation);
        r.Tick();

        Assert.NotNull(press);
        Assert.Equal([Coins1, Coins2, Coins3, Note, Writ], Assert.Single(r.H.Net.Of(FakeSendKind.BuyHouse)).Items!.Select(static i => i.item));
    }

    [Fact]
    public void The_window_refuses_what_the_guardian_does_not_ask_for_a_short_pane_and_a_second_house()
    {
        var r = Holding((Coins1, 273, 25_000, 25_000, 0x40), (Lockpick, LockpickWcid, 1, 1, 0x100));
        var window = new GuardianWindowModel(r.H.State, r.Api);
        Assert.False(window.Place(GuardianPane.Buy, Coins1));
        Assert.Equal("No guardian is open.", window.ErrorLine);

        Open(r, Profile());
        Assert.False(window.Place(GuardianPane.Buy, Lockpick));
        Assert.Equal("The guardian does not ask for that.", window.ErrorLine);
        Assert.False(window.FillFromPacks(GuardianPane.Buy));
        Assert.Equal("You need 75,000 more Pyreals.", window.ErrorLine);
        Assert.Null(window.PressBuy(ActionSource.Ui));
        Assert.Equal("The purchase pane does not cover the price.", window.ErrorLine);

        Data(r, HouseType.Apartment, 1_700_000_000);
        Assert.Null(window.PressBuy(ActionSource.Ui));
        Assert.Equal("You already own a house.", window.ErrorLine);
        Assert.Empty(r.H.Net.Of(FakeSendKind.BuyHouse));
    }

    [Fact]
    public void Pay_is_refused_for_a_dwelling_nobody_owns_and_sends_nothing()
    {
        var r = Rich();
        var window = new GuardianWindowModel(r.H.State, r.Api);
        Open(r, Profile());
        Assert.True(window.Place(GuardianPane.Rent, Coins3));

        Assert.Null(window.PressPay(ActionSource.Ui));
        Assert.Equal("This dwelling has no owner: there is no maintenance to pay.", window.ErrorLine);
        r.Tick();
        Assert.Empty(r.H.Net.Of(FakeSendKind.RentHouse));
    }

    [Fact]
    public void Closing_the_window_closes_the_model_s_guardian()
    {
        var r = Rich();
        Open(r, Profile());
        r.H.R.Reducer.CloseOpenGuardian("the player closed the guardian window");

        Assert.Null(r.H.State.Guardian);
        Assert.Equal(new GuardianClosed(Guardian), Assert.Single(r.H.R.Of<GuardianClosed>()));
    }

    [Fact]
    public void The_panes_belong_to_one_guardian()
    {
        var r = Rich();
        var window = new GuardianWindowModel(r.H.State, r.Api);
        Open(r, Profile());
        Assert.True(window.Place(GuardianPane.Buy, Coins1));

        Open(r, Profile(guardian: Guardian + 1));
        Assert.Empty(window.Placed(GuardianPane.Buy));
    }
}
