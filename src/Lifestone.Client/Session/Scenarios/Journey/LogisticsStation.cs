using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The logistics station (M5 item 3 c2a; DoD 6, D38, D47, D48, D52): after the den fight, in the ground, it reads the counters (<see cref="LogisticsCounters.Check"/> over the journey's ground) and, when a trip is due or forced, makes the mage's whole
/// errand as a player would: Lifestone Recall home (the travel seam's <see cref="TravelPurpose.Home"/>), the resupply (<see cref="ResupplyStation"/>'s run: the sale of the fight's kept loot when looting is on, the buys back to the keep counts out of the
/// purse), the experience spent (<see cref="XpSpendRule"/> until it is done), then Primary Portal Recall back to the ground (the seam's <see cref="TravelPurpose.Ground"/>, which reads the tie). Every leg logs its purpose (D47). It sends no mod verb:
/// <c>granted=</c> and <c>places=</c> are the verb counter's delta over the station (<see cref="PlayerTools.Verbs"/>), and the gate expects 0 of both.
/// <para><b>The trigger.</b> In the gate the trip is forced (<see cref="JourneyPlans.TripArgument"/> = <see cref="JourneyPlans.TripForced"/>, D52: three kills cannot bring a stock line under its line), <c>trigger=forced</c>; without the force it runs only when the counters say trip
/// (<c>trigger=</c> the counter's: <c>stock:&lt;name&gt;</c> or <c>burden</c>), else it is skipped by name (<c>skipped=no-trip-due</c>). A stop from the counters (a line under its stop line: the carried Platinum Scarab, <c>m5-platinum-stock</c>) fails the station by name
/// even when the trip is forced. Unread counters (the pack's list not yet known) are waited for; still unread, a forced trip goes on (the resupply fails by name if it cannot count) and an unforced one is skipped (<c>skipped=counters-unread</c>).</para>
/// <para><b>A failed errand.</b> A resupply that fails is named and the experience is still spent and the recall back still made (the mage is not left at home); a failed recall home ends the station there, named.</para>
/// </summary>
/// <param name="forced">True to make the trip whatever the counters say (D52).</param>
/// <param name="resupply">What the resupply is told: looting on and the guids the loot rule kept (the den fight's, <see cref="FightScenario.LootKeptGuids"/>); null for looting off.</param>
public sealed class LogisticsStation(bool forced = false, ResupplyOptions? resupply = null) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "logistics";

    /// <summary>
    /// The experience calls one trip makes at most (Lifestone's own): <see cref="XpSpendRule"/> raises at most <see cref="XpSpendRule.MaxRaisesPerCall"/> ranks a call and ends <see cref="XpSpendEnd.RaiseCap"/> with more to spend; the station calls again, three times at most
    /// (180 raises), so a long-neglected pool does not hold the mage at home for the whole station limit.
    /// </summary>
    public const int MaxXpCalls = 3;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (the resupply needs it), with the mana for the two recalls (as <see cref="RecallHomeStation"/>); where the mage stands is the previous station's (the ground, after the den fight).</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <summary>True when the trip is forced whatever the counters say.</summary>
    public bool Forced { get; } = forced;

    /// <summary>What the resupply is told.</summary>
    public ResupplyOptions Resupply { get; } = resupply ?? new ResupplyOptions();

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Forced, Resupply);
    }

    private sealed class Run(JourneyContext context, bool forced, ResupplyOptions options) : StationLeg(StationName, context)
    {
        private ModVerbCounts _mark;
        private ModVerbCounts? _end;
        private string _trigger = "none";
        private string? _skipped;
        private ResupplyRun? _resupply;
        private long _spentXp;
        private int _raises;

        /// <inheritdoc />
        /// <remarks>Report-only (a new judge's first gate): <c>trigger= granted= places= loot= sold= bought= spent= notes_sold= spent_xp= raises=</c>, then <c>skipped=</c> when no trip was made and <c>why=</c> on a failure.</remarks>
        public override string VerdictFields()
        {
            var delta = (_end ?? S?.Tools.Verbs.Counts ?? _mark).Since(_mark);
            var r = _resupply;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"trigger={_trigger} granted={delta.Granted} places={delta.Place} loot={(options.Loot ? "on" : "off")} sold={r?.Sold ?? 0} bought={r?.Bought ?? 0} spent={r?.Spent ?? 0} notes_sold={r?.NotesSold ?? 0} spent_xp={_spentXp} raises={_raises}")
                + (_skipped is null ? string.Empty : " skipped=" + _skipped) + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));
        }

        protected override IEnumerable<ActionHandle?> Script()
        {
            var ground = Context.Dungeon;
            _mark = S.Tools.Verbs.Counts;
            try
            {
                Stage = "count";
                var counters = new LogisticsCounters(ground, ComponentFloor.FloorOnly(ground, Context.Spells).Keys); // M5 7 P7s (D148): a carried line only the war options need is no stop
                foreach (var h in Until(() => counters.Check(S.State).Kind != LogisticsKind.Unread, NpcSeconds)) { yield return h; }

                var verdict = counters.Check(S.State);
                Log(string.Create(CultureInfo.InvariantCulture, $"logistics: the counters say {verdict.Kind}: {verdict.Reason}{(forced ? "; the trip is forced (D52)" : string.Empty)}"));
                var restockableStop = verdict.IsStop && !verdict.Carried && counters.FirstCarriedShort() < 0; // M5 7 audit: a line a home vendor sells under its stop line is what the trip restocks
                if (verdict.IsStop && !restockableStop)
                {
                    _trigger = JourneyVerdict.Token(verdict.Trigger);
                    Fail($"logistics: stop: {verdict.Reason}{(verdict.Carried || counters.FirstCarriedShort() >= 0 ? " (TODO(m5-platinum-stock): no vendor restocks a carried line)" : string.Empty)}");
                    yield break;
                }

                if (restockableStop)
                {
                    _trigger = JourneyVerdict.Token(verdict.Trigger);
                    Log("logistics: the line under its stop line is sold at home: the trip restocks it (M5 7 audit)");
                }
                else if (forced)
                {
                    _trigger = "forced";
                }
                else if (verdict.IsTrip)
                {
                    _trigger = JourneyVerdict.Token(verdict.Trigger);
                }
                else
                {
                    _trigger = verdict.Kind == LogisticsKind.Unread ? "unread" : "none";
                    _skipped = verdict.Kind == LogisticsKind.Unread ? "counters-unread" : "no-trip-due";
                    Log($"logistics: no trip: {_skipped}");
                    yield break;
                }

                var startedInGround = JourneyGrounds.At(StationWhere.Den, S.State.Self.Position, ground);
                Log($"logistics: the trip, trigger={_trigger}, from {(startedInGround ? "the ground" : "outside the ground")}");

                Stage = "home";
                Log($"walking: purpose=home (logistics trip: Lifestone Recall to {ground.Home.Town})");
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Home), "home")) { yield return h; }

                Stage = "resupply";
                var run = (ResupplyRun)new ResupplyStation(options).Start(Context);
                _resupply = run;
                FollowIsTheHunts = true; // the resupply run ticks the follower itself (a host with no runner gives each leg one)
                var status = run.Step(S, S.Clock.Now);
                while (status == ScenarioStatus.Running)
                {
                    yield return null;
                    status = run.Step(S, S.Clock.Now);
                }

                FollowIsTheHunts = false;

                Log($"logistics: the resupply ended {status}: {run.VerdictFields()}");
                if (status != ScenarioStatus.Succeeded || !run.Ok)
                {
                    Fail($"resupply: {run.Failure ?? (run.Problems.Count == 0 ? status.ToString() : run.Problems[0])}");
                }

                Stage = "experience";
                foreach (var h in SpendExperience()) { yield return h; }

                if (startedInGround)
                {
                    Stage = "back";
                    Log($"walking: purpose=ground (logistics trip: Primary Portal Recall to {ground.Key})");
                    foreach (var h in TravelTo(new TravelRequest(ground.ArrivalDestination(), TravelPurpose.Ground), "the ground")) { yield return h; }
                }
                else
                {
                    Log("logistics: the trip did not start in the ground, so there is no recall back: the mage stays home");
                }
            }
            finally
            {
                _end = S.Tools.Verbs.Counts;
            }
        }

        private IEnumerable<ActionHandle?> SpendExperience()
        {
            var table = S.XpTable ?? (Context.Dats is { } dats ? XpTableData.Load(dats) : null);
            if (table is null)
            {
                Fail("experience: the session has no XpTable and the journey no dats, so the experience cannot be priced (TODO(m5-logistics-xptable))");
                yield break;
            }

            var rule = new XpSpendRule(table, Context.Log, followLevel: true); // M5 7 F3 (D87): the soak mage at level 80 spends by the dance shares and leaves out the skill it never trained
            for (var call = 1; call <= MaxXpCalls; call++)
            {
                rule.Begin(S);
                while (!rule.Tick())
                {
                    yield return null;
                }

                _spentXp += rule.SpentXp;
                _raises += rule.Raises;
                Log($"logistics: experience call {call}: ended {rule.End} ({rule.Fields}){(rule.Skipped > 0 ? $", {rule.Skipped} skipped: {rule.FirstSkip}" : string.Empty)}");
                if (rule.Failure is not null)
                {
                    Fail($"experience: {rule.Failure}");
                    yield break;
                }

                if (rule.End != XpSpendEnd.RaiseCap)
                {
                    yield break;
                }
            }

            Log($"logistics: the experience calls reached their cap of {MaxXpCalls}: more may be left for the next trip");
        }

        private void Fail(string why)
        {
            Check(false, why);
            Failure ??= why;
        }
    }
}
