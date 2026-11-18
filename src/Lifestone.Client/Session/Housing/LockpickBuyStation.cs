using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// The Crude Lockpick buy (M6 villa): one lockpick for the purchase, from the Merchant east-south-east of Holtburg (<see cref="LockpickFacts.Merchant"/>), by the vendor path a player takes. Check, act, check:
/// <list type="number">
/// <item>Before: <see cref="LockpickRule.Decide"/> (one held: <c>reason=not-due:held</c>, ok; the coin unread, or the coin and the trade notes held short: a named stop; M6 16 E5a2: a note inside a side pack counts, and is changed where it lies); the body in Holtburg's place (else Lifestone Recall home first); the Merchant met in reach.</item>
/// <item>The shop opened (<see cref="IActionApi.OpenVendor"/>), then the vendor counter's buy of one (M6 16 E5b, <see cref="VendorCounter.Buy"/>): judged (<see cref="CheckedBuy.Judge"/>: the row, the stock, the wire price, the purse, a free slot), a trade note changed at the counter when the coin is short and his mask takes one, one <see cref="IActionApi.Buy"/>.</item>
/// <item>After: the lockpicks risen by one and the coin fallen within <see cref="HouseLeg.AfterSeconds"/>. A refusal is a named stop, never retried. Then Lifestone Recall home unless <c>back</c> is false.</item>
/// </list>
/// The line: <c>lockpick_buy: held_before= held_after= price= purse_before= purse_after= ok= reason=</c>.
/// </summary>
/// <param name="back">False to end the step at the Merchant.</param>
public sealed class LockpickBuyStation(bool back = true) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "lockpick-buy";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "lockpick_buy:";

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (a shop needs it), with the mana for the recall home.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, back);
    }

    private sealed class Run(JourneyContext context, bool back) : HouseLeg(StationName, context)
    {
        private int _heldBefore = -1, _heldAfter = -1;
        private uint _price;
        private long _purseBefore = -1, _purseAfter = -1;
        private bool _lined;

        protected override string Prefix => "lockpick";

        protected override string Fields() => string.Create(
            CultureInfo.InvariantCulture,
            $"held_before={_heldBefore} held_after={_heldAfter} price={(_price > 0 ? _price : (uint)LockpickFacts.CatalogPrice)} purse_before={_purseBefore} purse_after={_purseAfter} ok={Yes(Failure is null)} reason={Reason}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            try
            {
                Stage = "count";
                _heldBefore = Held(VillaFacts.LockpickWcid);
                _purseBefore = Coins();
                var decision = LockpickRule.Decide(_heldBefore, _purseBefore, LockpickFacts.CatalogPrice);
                Log($"lockpick: check before: {decision.Token}: {decision.Why}");
                if (!decision.May)
                {
                    // M6 16 E5b: the buy is the vendor counter's, which changes a trade note she holds when the coin is short (M6 16 E5a2: a side pack's too): a short coin with notes that cover the price goes on, and the Merchant's counter decides.
                    // M6 16 E5a2: every note she holds counts (a side pack's is changed where it lies), unless the Merchant's mask is known to take none (NoteMasks: no ground profile lists it today, so the notes are counted).
                    var mask = NoteMasks.Of(LockpickFacts.Merchant.Wcid);
                    var held = decision.Token == "purse-short" ? PurseView.NotesCounterCanChange(S.State) : 0;
                    var notes = NoteMasks.TakesNote(mask) == false ? 0 : held;
                    if (decision.Token == "held")
                    {
                        NotNow(decision.Token, decision.Why);
                        yield break;
                    }

                    if (decision.Token != "purse-short" || _purseBefore + notes < LockpickFacts.CatalogPrice)
                    {
                        Fail(decision.Token, decision.Why + (notes > 0 ? string.Create(CultureInfo.InvariantCulture, $" (the trade notes held, {notes}, do not cover the rest)") : held > 0 ? "; " + PurseView.CounterSays(held, "the Merchant", mask) : string.Empty));
                        yield break;
                    }

                    Log(string.Create(CultureInfo.InvariantCulture, $"lockpick: the coin {_purseBefore} is short of {LockpickFacts.CatalogPrice}; the trade notes held ({notes}) cover the rest: a note is changed at the Merchant's counter before the buy (TODO(m6-lockpick-note-mask): his mask is not in the notes of the corpus, so whether his counter takes a note is read from the wire there; a mask that takes none is the stop purse-short at the counter)"));
                }

                Stage = "travel";
                var holtburg = Context.Routes.Places.First(p => p.Key == Context.Routes.StartPlace);
                var merchant = LockpickFacts.Merchant;
                if (S.State.Self.Position is not { } at || (!holtburg.Landblocks.Contains((ushort)(at.CellId >> 16)) && at.CellId >> 16 != merchant.Cell >> 16))
                {
                    foreach (var h in GoHome()) { yield return h; }

                    if (Failure is not null)
                    {
                        yield break;
                    }
                }

                var reached = false;
                foreach (var h in Meet(merchant, TravelPurpose.Vendor, ShopLeg.UnseenVendorReachMetres, ShopLeg.UnseenVendorReachMetres + 1, ok => reached = ok)) { yield return h; }

                if (!reached)
                {
                    Fail("merchant-unreached", $"the Merchant 0x{merchant.Guid:X8} was not met in reach: {LastTravelFailure ?? "not seen in reach"}");
                    yield break;
                }

                Stage = "shop";
                var open = S.Actions.OpenVendor(merchant.Guid, ActionSource.Automation);
                yield return open;
                // M6 16 E5b: the buy is the vendor counter's (the checks before, a trade note changed when the coin is short, the send, the wait, the check after); the step keeps its stops' names and its lines.
                var before = Held(VillaFacts.LockpickWcid);
                var counter = new VendorBuyResult();
                var host = new VendorCounterHost("lockpick", Log, Until, () => PackView.MainPackFree(S.State))
                {
                    CheckBefore = judged =>
                    {
                        if (judged.Refusal is null)
                        {
                            _price = judged.Price;
                            if (_price != (uint)LockpickFacts.CatalogPrice)
                            {
                                Log(string.Create(CultureInfo.InvariantCulture, $"lockpick: the wire price {_price} differs from the catalog's {LockpickFacts.CatalogPrice} (rate {LockpickFacts.SellRate}): the wire wins"));
                            }

                            Stage = "buy";
                            Log(string.Create(CultureInfo.InvariantCulture, $"lockpick: check before the buy: {judged.Why}; held {before}"));
                        }
                    },
                };
                foreach (var h in VendorCounter.Buy(S, new VendorBuyRequest(merchant.Guid, VillaFacts.LockpickWcid, 1) { VendorName = "the Merchant", WareName = "Crude Lockpick", ArrivalSeconds = AfterSeconds }, counter, host)) { yield return h; }

                if (counter.Refusal == VendorCounter.Refused)
                {
                    Fail("refused", $"the Merchant refused one Crude Lockpick: {counter.Outcome}; never retried");
                    yield break;
                }

                if (counter.Refusal is { } refused && refused != VendorCounter.AfterCheck)
                {
                    Fail(refused, counter.Why + (open.Outcome is { Kind: ActionOutcomeKind.Confirmed } ? string.Empty : "; the shop: " + Describe(open)));
                    yield break;
                }

                var coins = counter.CoinBefore; // the coin at the send, after any note was changed
                var after = Held(VillaFacts.LockpickWcid);
                if (after < before + 1 || Coins() >= coins)
                {
                    Fail("after-check", $"after the buy the lockpicks are {before} -> {after} and the coin {coins} -> {Coins()}");
                    yield break;
                }

                if (counter.NotesChanged > 0)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"lockpick: paid from the purse: {counter.NotesChanged} trade note(s) changed to coin at the Merchant before the buy (the coin {_purseBefore} was short of {counter.Cost})"));
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"lockpick: check after the buy: the lockpicks {before} -> {after}, the coin {coins} -> {Coins()}"));
                Reason = "bought";
                if (back)
                {
                    Stage = "back";
                    foreach (var h in GoHome()) { yield return h; }
                }
            }
            finally
            {
                Emit();
            }
        }

        private void Emit()
        {
            if (_lined)
            {
                return;
            }

            _lined = true;
            _heldAfter = Held(VillaFacts.LockpickWcid);
            _purseAfter = Coins();
            Log(LinePrefix + " " + Fields());
        }
    }
}
