using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// The rent (M6 villa): when the house's rent is owed and its period ends within the caller's margin, <c>/house recall</c> to the guardian, its window opened, the rent pane filled from the packs
/// (<see cref="GuardianWindowModel.FillFromPacks"/> with <see cref="GuardianPane.Rent"/>), Pay pressed, and the payment checked. Check, act, check:
/// <list type="number">
/// <item>Before: a HouseQuery (the period and the paid lines fresh); <see cref="RentRule.Decide"/>: no house, paid, or not within the margin end the step well (<c>reason=not-due:&lt;token&gt;</c>); the rent not all in the packs
/// is the stop <c>rent-short</c>, every short line named.</item>
/// <item>The house recall (one named retry), the guardian opened, the pane filled (its lines logged) and Pay pressed.</item>
/// <item>After: the server's "Maintenance paid." (a partial payment is the stop <c>partial</c>), then within <see cref="HouseLeg.AfterSeconds"/> the refreshed HouseData with every rent line paid (else one HouseQuery,
/// <c>recovery=query-after-pay</c>); the writs fell by the rent's. Then Lifestone Recall home unless <c>back</c> is false.</item>
/// </list>
/// The line: <c>villa_rent: house= due= margin_h= price_before= price_after= ok= reason=</c>.
/// </summary>
/// <param name="margin">How long before the period's end the rent is paid (the caller's; a late rent is paid at once).</param>
/// <param name="back">False to end the step at the guardian.</param>
public sealed class RentStation(TimeSpan margin, bool back = true) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "villa-rent";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "villa_rent:";

    /// <summary>The margin.</summary>
    public TimeSpan Margin { get; } = margin;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace, with the mana for the recall home.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Margin, back);
    }

    private sealed class Run(JourneyContext context, TimeSpan margin, bool back) : HouseLeg(StationName, context)
    {
        private PriceHeld? _before, _after;
        private VillaHouse? _house;
        private long _due;
        private bool _lined;

        protected override string Prefix => "rent";

        protected override string Fields() => string.Create(
            CultureInfo.InvariantCulture,
            $"house={_house?.HouseId ?? 0} due={(_due > 0 ? DateTimeOffset.FromUnixTimeSeconds(_due).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "unread")} margin_h={margin.TotalHours:0.#} price_before=\"{_before}\" price_after=\"{_after}\" ok={Yes(Failure is null)} reason={Reason}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            try
            {
                Stage = "status";
                foreach (var h in QueryHouse()) { yield return h; }

                if (Failure is not null)
                {
                    yield break;
                }

                var owned = S.State.House;
                _due = owned?.RentDue ?? 0;
                _before = ReadPrice();
                var owed = owned?.Rent.Where(r => r.Remaining > 0).Select(r => (r.WeenieClassId, r.Required == 1 ? r.Name : r.PluralName, (long)r.Remaining)).ToList() ?? [];
                var decision = RentRule.Decide(S.State.HouseStatus, owned?.IsRentPaid ?? false, _due, S.Clock.Time.GetUtcNow().ToUnixTimeSeconds(), margin, owed, _before.Value);
                Log($"rent: check before: {decision.Token}: {decision.Why}");
                if (!decision.May)
                {
                    if (decision.Token is "no-house" or "paid" or "not-yet")
                    {
                        NotNow(decision.Token, decision.Why);
                    }
                    else
                    {
                        Fail(decision.Token, decision.Why);
                    }

                    yield break;
                }

                if (VillaFacts.HouseAt(owned!.Position.CellId >> 16) is not { } house)
                {
                    Fail("house-unknown", string.Create(CultureInfo.InvariantCulture, $"the owned house at 0x{owned.Position.CellId:X8} is in no block this file knows (TODO(villa-more-houses))"));
                    yield break;
                }

                _house = house;
                Stage = "recall";
                foreach (var h in RecallToHouse(house.Guardian.Cell >> 16)) { yield return h; }

                if (Failure is not null)
                {
                    yield break;
                }

                Stage = "guardian";
                foreach (var h in OpenGuardian(house.Guardian)) { yield return h; }

                if (Failure is not null)
                {
                    yield break;
                }

                Stage = "pane";
                var window = Window();
                window.Clear();
                var filled = window.FillFromPacks(GuardianPane.Rent);
                Log($"rent: the rent pane: {PaneText(window.Lines(GuardianPane.Rent))}; {window.Placed(GuardianPane.Rent).Count} stacks placed");
                if (!filled)
                {
                    Fail("rent-short", $"the rent pane does not cover the rent: {window.ErrorLine}");
                    yield break;
                }

                Stage = "pay";
                var writsBefore = Held(VillaFacts.WritWcid);
                var writsOwed = S.State.Guardian?.Rent.Where(r => r.WeenieClassId == VillaFacts.WritWcid).Sum(r => r.Remaining) ?? 0;
                var revision = S.State.HouseInfoRevision;
                var pay = window.PressPay(ActionSource.Automation);
                if (pay is null)
                {
                    Fail("pay-refused-by-window", window.ErrorLine ?? "the window refused the press");
                    yield break;
                }

                yield return pay;
                if (pay.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || pay.Outcome?.Detail?.Contains("partially", StringComparison.Ordinal) == true)
                {
                    Fail(pay.Outcome is { Kind: ActionOutcomeKind.Confirmed } ? "partial" : pay.Outcome?.Kind == ActionOutcomeKind.TimedOut ? "pay-no-answer" : "pay-refused", Describe(pay));
                    yield break;
                }

                Log($"rent: the server: {pay.Outcome?.Detail}");
                foreach (var h in Until(() => S.State.HouseInfoRevision > revision && S.State.House is { IsRentPaid: true }, AfterSeconds)) { yield return h; }

                if (S.State.House is not { IsRentPaid: true })
                {
                    Log("rent: recovery=query-after-pay: no paid HouseData after the payment line; one HouseQuery");
                    foreach (var h in QueryHouse()) { yield return h; }
                }

                _after = ReadPrice();
                var writs = Held(VillaFacts.WritWcid);
                if (S.State.House is not { IsRentPaid: true } paid || writs > writsBefore - writsOwed)
                {
                    Fail("after-check", $"after the payment line the rent reads {(S.State.House is { } h ? (h.IsRentPaid ? "paid" : "owed: " + Lines(h.Rent)) : "unread")}; writs {writsBefore} -> {writs} (owed {writsOwed})");
                    yield break;
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"rent: check after the payment: paid for the period to {DateTimeOffset.FromUnixTimeSeconds(paid.RentDue):yyyy-MM-dd HH:mm}Z; {_before} -> {_after}"));
                Reason = "paid";
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
