using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// The villa purchase trip (M6 villa): to the block's first unowned dwelling, its guardian opened, the purchase pane filled from the packs (<see cref="GuardianWindowModel.FillFromPacks"/>), Buy pressed
/// (<see cref="GuardianWindowModel.PressBuy"/>), and the house checked as hers. Never a villa for a test character: the caller names the block (the lead's live test buys an apartment's block). Check, act, check:
/// <list type="number">
/// <item>Before: the house status read (a HouseQuery when unknown); <see cref="VillaPurchaseRule.Decide"/>: the account's opening (<c>too-early</c>), a house owned (<c>owns-house</c>), the level, and the whole price in the packs
/// (<c>price-short</c>, every short line named). A refusal is a named stop.</item>
/// <item>For each dwelling of the block in order: the walk to its guardian and the Use (the window opens); an owned dwelling is passed by (<c>recovery=next-house</c>), all owned is the stop
/// <c>block-owned</c> (<c>TODO(villa-more-houses)</c>: the file lists the block's nearest landblock only). The pane filled from the packs, its lines logged; Buy pressed.</item>
/// <item>After: the server's line ("Congratulations!  You now own this dwelling."), then within <see cref="HouseLeg.AfterSeconds"/> the HouseData (else one HouseQuery, <c>recovery=query-after-buy</c>): owned, a villa, at the
/// dwelling's landblock; the writs fell by the price's and the lockpick is gone. Then Lifestone Recall home unless <c>back</c> is false.</item>
/// </list>
/// The line: <c>villa_purchase: block= house= guardian= price_before=&lt;PriceHeld&gt; price_after= tried= ok= reason=</c>.
/// </summary>
/// <param name="block">The block to buy in (<see cref="VillaFacts.Block1401"/> for the climber).</param>
/// <param name="opensAt">The first instant the account may buy (<see cref="VillaFacts.ClimberAccountOpensVillas"/> for the climber's account).</param>
/// <param name="back">False to end the step at the guardian.</param>
public sealed class VillaPurchaseStation(VillaBlock block, DateTimeOffset opensAt, bool back = true) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "villa-purchase";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "villa_purchase:";

    /// <summary>The block.</summary>
    public VillaBlock Block { get; } = block ?? throw new ArgumentNullException(nameof(block));

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (a Use of the guardian), with the mana for the recall home.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Block, opensAt, back);
    }

    private sealed class Run(JourneyContext context, VillaBlock block, DateTimeOffset opensAt, bool back) : HouseLeg(StationName, context)
    {
        private PriceHeld? _before, _after;
        private VillaHouse? _house;
        private int _tried;
        private bool _lined;

        protected override string Prefix => "villa";

        protected override string Fields() => string.Create(
            CultureInfo.InvariantCulture,
            $"block={block.Key} house={_house?.HouseId ?? 0} guardian=0x{_house?.Guardian.Guid ?? 0:X8} price_before=\"{_before}\" price_after=\"{_after}\" tried={_tried} ok={Yes(Failure is null)} reason={Reason}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            try
            {
                Stage = "status";
                if (S.State.HouseStatus == HouseOwnership.Unknown)
                {
                    foreach (var h in QueryHouse()) { yield return h; }

                    if (Failure is not null)
                    {
                        yield break;
                    }
                }

                var now = S.Clock.Time.GetUtcNow();
                _before = ReadPrice();
                var level = S.State.Self.Qualities.Level;
                var decision = VillaPurchaseRule.Decide(now, opensAt, block, S.State.HouseStatus, level, _before.Value);
                Log($"villa: check before: {decision.Token}: {decision.Why}");
                if (!decision.May)
                {
                    Fail(decision.Token, decision.Why);
                    yield break;
                }

                Stage = "guardian";
                foreach (var house in block.Houses)
                {
                    _tried++;
                    foreach (var h in OpenGuardian(house.Guardian)) { yield return h; }

                    if (Failure is not null)
                    {
                        yield break;
                    }

                    var open = S.State.Guardian!;
                    if (open.IsOwned)
                    {
                        Log($"villa: recovery=next-house: house {house.HouseId} (guardian 0x{house.Guardian.Guid:X8}) is owned by {open.OwnerName}");
                        continue;
                    }

                    if (open.MinLevel > level)
                    {
                        Fail("below-level", string.Create(CultureInfo.InvariantCulture, $"house {house.HouseId} asks level {open.MinLevel}, the character is {level}"));
                        yield break;
                    }

                    _house = house;
                    break;
                }

                if (_house is not { } chosen)
                {
                    Fail("block-owned", $"every dwelling of {block.Key} this file lists ({block.Houses.Count}) is owned (TODO(villa-more-houses): the block's other landblocks are not listed)");
                    yield break;
                }

                Stage = "pane";
                var window = Window();
                window.Clear();
                var filled = window.FillFromPacks(GuardianPane.Buy);
                Log($"villa: the purchase pane: {PaneText(window.Lines(GuardianPane.Buy))}; {window.Placed(GuardianPane.Buy).Count} stacks placed");
                if (!filled)
                {
                    Fail("price-short", $"the purchase pane does not cover the price: {window.ErrorLine}");
                    yield break;
                }

                Stage = "buy";
                var writsBefore = Held(VillaFacts.WritWcid);
                var picksBefore = Held(VillaFacts.LockpickWcid);
                var revision = S.State.HouseInfoRevision;
                var buy = window.PressBuy(ActionSource.Automation);
                if (buy is null)
                {
                    Fail("buy-refused-by-window", window.ErrorLine ?? "the window refused the press");
                    yield break;
                }

                yield return buy;
                if (buy.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
                {
                    Fail(buy.Outcome?.Kind == ActionOutcomeKind.TimedOut ? "buy-no-answer" : "buy-refused", Describe(buy));
                    yield break;
                }

                Log($"villa: the server: {buy.Outcome?.Detail}");
                foreach (var h in Until(() => S.State.HouseInfoRevision > revision && S.State.HouseStatus == HouseOwnership.Owned, AfterSeconds)) { yield return h; }

                if (S.State.HouseStatus != HouseOwnership.Owned)
                {
                    Log("villa: recovery=query-after-buy: no HouseData after the purchase line; one HouseQuery");
                    foreach (var h in QueryHouse()) { yield return h; }
                }

                if (S.State.House is not { } owned || S.State.HouseStatus != HouseOwnership.Owned)
                {
                    Fail("after-check", $"after the purchase line the house status is {S.State.HouseStatus}, no HouseData");
                    yield break;
                }

                _after = ReadPrice();
                var writs = Held(VillaFacts.WritWcid);
                var picks = Held(VillaFacts.LockpickWcid);
                if (owned.Type != block.Type || owned.Position.CellId >> 16 != chosen.House.Cell >> 16 || writs > writsBefore - block.Price.Writs || picks > picksBefore - block.Price.Lockpicks)
                {
                    Fail("after-check", string.Create(CultureInfo.InvariantCulture, $"the HouseData says a {owned.Type} at 0x{owned.Position.CellId:X8} (want a {block.Type} in 0x{chosen.House.Cell >> 16:X4}); writs {writsBefore} -> {writs}, lockpicks {picksBefore} -> {picks}"));
                    yield break;
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"villa: check after the purchase: house {chosen.HouseId} is hers (a {owned.Type} at 0x{owned.Position.CellId:X8}, rent due {DateTimeOffset.FromUnixTimeSeconds(owned.RentDue):yyyy-MM-dd HH:mm}Z); {_before} -> {_after}"));
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
            _after ??= ReadPrice();
            Log(LinePrefix + " " + Fields());
        }
    }
}
