using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The logistics station (M5 item 3 c2a; DoD 6, D38, D47, D48, D52): after the den fight, in the ground, it reads the counters (<see cref="LogisticsCounters.Check"/> over the journey's ground) and, when a trip is due or forced, makes the mage's whole
/// errand as a player would: Lifestone Recall home (the travel seam's <see cref="TravelPurpose.Home"/>), the resupply (<see cref="ResupplyStation"/>'s run: the sale of the fight's kept loot when looting is on, the buys back to the keep counts out of the
/// purse), the experience spent (<see cref="XpSpendRule"/> until it is done), then Primary Portal Recall back to the ground (the seam's <see cref="TravelPurpose.Ground"/>, which reads the tie). Every leg logs its purpose (D47). It sends no mod verb:
/// <c>granted=</c> and <c>places=</c> are the verb counter's delta over the station (<see cref="PlayerTools.Verbs"/>), and the gate expects 0 of both.
/// <para><b>The trigger.</b> In the gate the trip is forced (<see cref="JourneyPlans.TripArgument"/> = <see cref="JourneyPlans.TripForced"/>, D52: three kills cannot bring a stock line under its line), <c>trigger=forced</c>; without the force it runs only when the counters say trip
/// (<c>trigger=</c> the counter's: <c>stock:&lt;name&gt;</c> or <c>burden</c>), else it is skipped by name (<c>skipped=no-trip-due</c>). A stop from the counters (a line under its stop line) fails the station by name even when the trip is forced; for a carried line (no home vendor
/// sells it) the stop names the buy-trip (M6 item 1 d, D12, D44: the stop stays a named stop, <see cref="BuyTripRule.StopNote"/>, <c>buy-trip due: &lt;scarab&gt; x&lt;n&gt; at &lt;archmage&gt; in &lt;town&gt;</c>), which <see cref="BuyTripStation"/> makes: the
/// autopilot's next start audit queues it (M6 4 t4b, <c>AutopilotBuyTripRule</c>), the climber by the Town Network (M6 4 t4a, D40). Unread counters (the pack's list not yet known) are waited for; still unread, a forced trip goes on (the resupply fails by name if
/// it cannot count) and an unforced one is skipped (<c>skipped=counters-unread</c>).</para>
/// <para><b>The ways home and back</b> (M6 4 F1, 4-R1): both go through the travel seam, whose Home and Ground purposes ask the autopilot's travel rule first (<see cref="JourneyTravel.Decide"/>): a book without Lifestone Recall (D43) goes home by
/// portals and a routed ground (the island's, untieable, or a tie not known tied) is reached by the rung change; a tied ground by its Primary Portal Recall.</para>
/// <para><b>A failed errand.</b> A resupply that fails is named and the experience is still spent and the recall back still made (the mage is not left at home); a failed recall home ends the station there, named.</para>
/// </summary>
/// <param name="forced">True to make the trip whatever the counters say (D52).</param>
/// <param name="resupply">What the resupply is told: looting on and the guids the loot rule kept (the den fight's, <see cref="FightScenario.LootKeptGuids"/>); null for looting off.</param>
/// <param name="accountName">M6 1 h2: the account's name as the server stores it (the login name, never the secrets key), or null: the floor-only lines are then those of the foci formula for every school. With it a school whose focus the pack lacks is read on the account's full formula (<see cref="AccountFormula"/>); hashed, never logged or printed.</param>
/// <param name="learnHolds">M6 1 d: the learn triggers held after a learn trip that bought nothing (<see cref="LearnHolds"/>); null: the session's.</param>
/// <param name="trigger">M6 14 sell first: a forced trip's trigger name (<c>trigger=errand-room</c>: the sale before an errand from home, <see cref="JourneyPlans.TripErrandRoom"/>), or null for <c>trigger=forced</c>.</param>
public sealed class LogisticsStation(bool forced = false, ResupplyOptions? resupply = null, string? accountName = null, LearnHolds? learnHolds = null, string? trigger = null) : IStation
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

    /// <summary>M6 14 sell first: the forced trip's trigger name, or null for <c>forced</c>.</summary>
    public string? Trigger { get; } = trigger;

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, Forced, Resupply, accountName, learnHolds ?? LearnHolds.Shared, Trigger);
    }

    private sealed class Run(JourneyContext context, bool forced, ResupplyOptions options, string? accountName, LearnHolds holds, string? forcedTrigger) : StationLeg(StationName, context)
    {
        private ModVerbCounts _mark;
        private ModVerbCounts? _end;
        private string _trigger = "none";
        private string? _skipped;
        private ResupplyRun? _resupply;
        private long _spentXp;
        private int _raises;
        private string _spendWhy = "none";

        /// <inheritdoc />
        /// <remarks>Report-only (a new judge's first gate): <c>trigger= granted= places= loot= sold= bought= spent= notes_sold= spent_xp= raises= learned= scrolls_bought=</c> (M6 1 d: the resupply's learn step, <see cref="ResupplyRun.Learned"/>
        /// and <see cref="ResupplyRun.ScrollsBought"/>), then <c>spend_why=</c> (how the last experience call ended: <c>pool-covers-nothing</c> is why a <c>spent_xp=0</c> passes the mage's judge; <c>none</c> when no call ran) and <c>skipped=</c> when no trip was made and <c>why=</c> on a failure.</remarks>
        public override string VerdictFields()
        {
            var delta = (_end ?? S?.Tools.Verbs.Counts ?? _mark).Since(_mark);
            var r = _resupply;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"trigger={_trigger} granted={delta.Granted} places={delta.Place} loot={(options.Loot ? "on" : "off")} sold={r?.Sold ?? 0} bought={r?.Bought ?? 0} spent={r?.Spent ?? 0} notes_sold={r?.NotesSold ?? 0} spent_xp={_spentXp} raises={_raises} learned={r?.Learned ?? 0} scrolls_bought={r?.ScrollsBought ?? 0} spend_why={_spendWhy}")
                + (_skipped is null ? string.Empty : " skipped=" + _skipped) + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));
        }

        /// <summary>
        /// M6 1 h2 (closes <c>m6-account-formula-callers</c> for the logistics counters): the ground's floor-only lines (<see cref="ComponentFloor.FloorOnly"/>), with the account's full formula for a school whose focus the pack does not hold
        /// (<see cref="AccountFormula.HoldsFocus"/> over what the pack holds now); with no account name, the foci formula for every school (today's rule).
        /// </summary>
        private IReadOnlyDictionary<uint, IReadOnlyList<string>> FloorOnlyLines(HuntingGround ground)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                return ComponentFloor.FloorOnly(ground, Context.Spells);
            }

            var held = GearInventory.CountAll(S.State, out _);
            return ComponentFloor.FloorOnly(ground, Context.Spells, accountName, school => AccountFormula.HoldsFocus(held, school));
        }

        protected override IEnumerable<ActionHandle?> Script()
        {
            var ground = Context.Dungeon;
            _mark = S.Tools.Verbs.Counts;
            var desk = TripDesk.For(S.State);
            SaleDesk.For(S.State).StateIfNone(Context.Character, Context.Spells, Context.Facts); // M6 16 E5c2: a station run with no job states who she is and the SpellTable for the rule book's verdict (the sale estimate its count reads)
            var counters = desk.On(ground, FloorOnlyLines(ground).Keys, holds, Log); // M6 16 E5d: the session's one set of counters; M5 7 P7s (D148): a carried line only the war options need is no stop
            var order = desk.Take(ground); // M6 16 E5d: the verdict the trigger called this trip on, carried through the trip; null for a trip no trigger called (a forced one, a plan's)
            try
            {
                Stage = "count";
                foreach (var h in Until(() => counters.Check(S.State).Kind != LogisticsKind.Unread, NpcSeconds)) { yield return h; }

                var verdict = counters.Check(S.State);
                Log(string.Create(CultureInfo.InvariantCulture, $"logistics: the counters say {verdict.Kind}: {verdict.Reason}{(forced ? "; the trip is forced (D52)" : string.Empty)}"));
                if (order is not null)
                {
                    // M6 16 E5d: check before: the trigger's verdict against the count where she stands now (the fight ended, its last casts made), every difference by name
                    Log(string.Create(CultureInfo.InvariantCulture, $"logistics: the trip was called by the trigger {S.Clock.Now - order.CalledAt:0} s ago"));
                    Log(order.AtHome(counters, verdict, S.State, "at the fight's end"));
                }
                var restockableStop = verdict.IsStop && !verdict.Carried && counters.FirstCarriedShort() < 0; // M5 7 audit: a line a home vendor sells under its stop line is what the trip restocks
                if (verdict.IsStop && !restockableStop)
                {
                    _trigger = JourneyVerdict.Token(verdict.Trigger);
                    if (verdict.Carried || counters.FirstCarriedShort() >= 0)
                    {
                        // M6 1 d (D12, D44): no home vendor restocks a carried line; the buy-trip to the archmage who sells it does, and the stop names it: still a stop, TODO(m6-buy-trip-autopilot) (no plan calls the station until item 4's hop)
                        var plan = BuyTripRule.PlanLevel(S.State.Self.Qualities, LadderLoader.WarMage, out var planWhy);
                        var trip = BuyTripRule.Decide(ground, counters, plan, planWhy, ScrivenerCatalog.Default, GroundCatalog.All);
                        Log($"logistics: the carried line is the buy-trip's: {trip.Kind} {trip.Token}: {trip.Why}");
                        Fail($"logistics: stop: {verdict.Reason}; {BuyTripRule.StopNote(trip, BuyTripStation.NoTeleportBudget(Context.Facts))}");
                    }
                    else
                    {
                        Fail($"logistics: stop: {verdict.Reason}");
                    }

                    yield break;
                }

                if (restockableStop)
                {
                    _trigger = JourneyVerdict.Token(verdict.Trigger);
                    Log("logistics: the line under its stop line is sold at home: the trip restocks it (M5 7 audit)");
                }
                else if (forced)
                {
                    _trigger = forcedTrigger ?? "forced"; // M6 14 sell first: errand-room, the sale before an errand from home
                }
                else if (verdict.IsTrip)
                {
                    _trigger = JourneyVerdict.Token(order?.Trigger ?? verdict.Trigger); // M6 16 E5d: the reason the trigger called the trip for is the trip's; a different one counted here was named above
                }
                else if (order is not null && verdict.Kind != LogisticsKind.Unread)
                {
                    // M6 16 E5d: the trigger called this trip and the count at the fight's end finds nothing due (the pack changed since, or the trigger read what this count does not). The trip is the trigger's: it is
                    // made on the reason it was called for. Before, it was skipped here (skipped=no-trip-due), she went back to the fight and the trigger called it again.
                    _trigger = JourneyVerdict.Token(order.Trigger);
                    Log($"logistics: the trip is made on the verdict it was called on ({order.Trigger}): the count at the fight's end finds nothing due, and the trigger's reason is the trip's");
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
                Log($"walking: purpose=home (logistics trip: home to {ground.Home.Town}, by Lifestone Recall or, where the travel rule says so, the rung change home by portals)");
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Home), "home")) { yield return h; }

                // M6 14 T (the sprint of 2026-11-12: the run's first trip bought, then spent 22,710 experience; the raised skills made seven more scrolls due at once, the purse was down to 2,231, and two sale trips followed inside
                // nine minutes; then live at 05:23 UTC, a trip from the ground: its plan read Stamina to Mana Self II, the spend after the buys (69,280, the experience earned since the last level) lifted Life Magic and the stamina, and
                // the check after named level III as a trip the visit had not served): every trip spends the experience before the buys, so the learn plan reads the skills and the stamina the spend leaves and this visit buys their
                // scrolls. The Strength floor then reads the burden she walked in with, as the level-up's spend in the field does; the second look after the buys raises it if the buys made it short and experience is left.
                Stage = "experience-first";
                Log($"logistics: the experience is spent before the buys, so the learn plan reads the skills the spend leaves (the trip started {(startedInGround ? "in the ground" : "at home")}; M6 14 T)");
                foreach (var h in SpendExperience()) { yield return h; }

                var learnDue = counters.LearnDue(S.State); // M6 1 d: the plan's learn this trip would buy (held below when the trip buys no scroll)

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
                HoldLearn(ground, learnDue, run);
                if (status != ScenarioStatus.Succeeded || !run.Ok)
                {
                    Fail($"resupply: {run.Failure ?? (run.Problems.Count == 0 ? status.ToString() : run.Problems[0])}");
                }

                Stage = "experience";
                foreach (var h in SpendExperience()) { yield return h; }

                // M6 14 T (check after, no silent choices): what the counters say with the visit's buys and spend made. A trip due at once is named here, at home, where the lead can see what the visit left undone.
                var after = counters.Check(S.State);
                Log(after.IsTrip || after.IsStop
                    ? $"WARNING: logistics: check after the visit: the counters still say {after.Kind}: {after.Reason} (named: m6-visit-left-a-trip-due: the visit did not cure it)"
                    : $"logistics: check after the visit: the counters say {after.Kind}: {after.Reason}{(counters.LearnWaitNote(S.State) is { } waits ? "; " + waits : string.Empty)}");
                if (order is not null)
                {
                    Log(order.After(counters, after)); // M6 16 E5d: check after: whether the reason the trip was called for was served
                }

                Stage = "buff-bot";
                foreach (var h in BuffBotStop()) { yield return h; } // M6 item 14: the trip's own stop at the buff bot: a trip ends in the ground, so the autopilot's Home arrow never sees it

                if (startedInGround && Context.Facts.MinutesStopDue?.Invoke() == true)
                {
                    // M6 8 E (m6-minutes-stop-safe-point): the run's minutes are up while the mage is home with the errand done: this is the safe point (no portal in flight, no corpse owed, the pack sold and spent), so the way back is not made;
                    // the autopilot's next state change reads the same rule and stops here, at home, with no recall to pay for. Only the minutes rule ends a trip this way; the level rule is read where it always was.
                    Log("logistics: the minutes stop rule is due: the errand is done at home, so there is no way back; the run stops here (m6-minutes-stop-safe-point)");
                }
                else if (startedInGround && Context.Facts.PassAtHomeDue?.Invoke(S) is { } passDue)
                {
                    // M6 audit (finding 9): the visit left more buff lines to cast than a pass in the ground takes (new scrolls read, lines lapsed): the pass runs here, at home, and the autopilot's
                    // known state at home takes the buff state and then the way out, as after any recall home. No way back is made by the trip.
                    Log("logistics: the buff pass is due at home (" + passDue + "): there is no way back; she casts it here and the autopilot's way out follows (m6-audit-pass-at-home)");
                }
                else if (startedInGround)
                {
                    Stage = "back";
                    if (ground.Kind == GroundKind.Outdoor)
                    {
                        // M6 0 F3 (closes TODO(m6-trip-way-back)): an outdoor ground has no portal and no place; the climber walks back from the town to the ground's first cell (as the outdoor arrival does), never a place.
                        foreach (var h in WalkBack(ground)) { yield return h; }
                    }
                    else
                    {
                        Log($"walking: purpose=ground (logistics trip: back to {ground.Key}, by its Primary Portal Recall or, where the travel rule says so, the rung change)");
                        foreach (var h in TravelTo(new TravelRequest(ground.ArrivalDestination(), TravelPurpose.Ground), "the ground")) { yield return h; }
                    }
                }
                else
                {
                    Log("logistics: the trip did not start in the ground, so there is no recall back: the mage stays home");
                }
            }
            finally
            {
                desk.Close(); // M6 16 E5d: no trip is in hand
                _end = S.Tools.Verbs.Counts;
            }
        }

        /// <summary>
        /// M6 item 14 (live 2026-11-11: a trip goes from the shops straight back to the ground, so the bot was asked at a run's start alone and its hour would lapse in the field): at home, the errand done, when the run's stop at
        /// the bot applies (<see cref="JourneyFacts.BuffBotOn"/>) and a line of its profile is down or has under <see cref="BuffBotRule.RenewUnderSeconds"/> left, the <see cref="BuffBotStation"/> runs here. Check before: the lines read,
        /// one line either way. After: the station's verdict. A stop that fails never fails the trip: it is logged by name (<c>m6-buffbot-fallback</c>) and her own pass recasts her lines on their timers.
        /// </summary>
        private IEnumerable<ActionHandle?> BuffBotStop()
        {
            if (Context.Facts.BuffBotOn?.Invoke() != true)
            {
                yield break;
            }

            var reads = new List<BuffBotLineRead>();
            var profile = Context.Facts.BuffBotProfile; // the run's profile (the option's word, 'all' by default): the stop is due by its lines and tells its key
            BuffBotRule.Read(profile, S.State.Self.Qualities, S.Clock.Now, reads);
            var least = BuffBotRule.MinRemaining(reads);
            if (!BuffBotRule.Due(reads))
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"logistics: the buff bot is not due: every line of '{profile.Key}' has at least {BuffBotRule.RenewUnderSeconds:0} s left (the least {least:0})"));
                yield break;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"logistics: the buff bot before the way back: {BuffBotRule.CountWith(reads, 1)} of {reads.Count} lines of '{profile.Key}' are up, the least {least:0} s left (under {BuffBotRule.RenewUnderSeconds:0})"));
            for (var attempt = 1; attempt <= BuffBotRule.StopTries; attempt++)
            {
                var stop = new BuffBotStation().Start(Context);
                var status = stop.Step(S, S.Clock.Now);
                while (status == ScenarioStatus.Running)
                {
                    yield return null;
                    status = stop.Step(S, S.Clock.Now);
                }

                if (status == ScenarioStatus.Succeeded && stop.Ok)
                {
                    Log($"logistics: the buff bot's stop ended {status}: {stop.VerdictFields()}");
                    yield break;
                }

                if (attempt < BuffBotRule.StopTries)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"WARNING: logistics: the buff bot's stop failed ({stop.VerdictFields()}): try {attempt + 1} of {BuffBotRule.StopTries} now (m6-audit-bot-retry)")); // M6 audit (fix 1)
                    continue;
                }

                Log($"WARNING: logistics: the buff bot's stop failed ({stop.VerdictFields()}) after {BuffBotRule.StopTries} tries: the trip goes on, and her own pass recasts her lines on their timers (m6-buffbot-fallback)");
            }
        }

        /// <summary>
        /// The way back to an outdoor ground on foot (M6 0 F3): a point goal at the first cell's standing point (<see cref="WayHomeRule.FirstCell"/>, the terrain height from the dats) through the travel seam (<see cref="TravelPurpose.Cell"/>, never a place),
        /// check, act, check (M5 D141): the purpose and where the player stands are logged first; none when the player already stands within <see cref="JourneyTravel.ToleranceMetres"/> of the point; after, the player must stand in the point's landblock
        /// within the tolerance, else the station fails <c>logistics:way-back:arrival-off-point</c>. An outdoor ground that is not hunted from its home on foot, or whose first cell cannot be read, is a loud stop by name
        /// (<c>logistics:way-back:no-walk</c>, <c>TODO(m6-first-tie-walk)</c>): the trip is never a place for the climber.
        /// </summary>
        private IEnumerable<ActionHandle?> WalkBack(HuntingGround ground)
        {
            var first = WayHomeRule.FirstCell(ground);
            if (!WayHomeRule.HomeOnFoot(ground) || first is null)
            {
                Fail($"logistics:way-back:no-walk: the outdoor ground '{ground.Key}' is not one hunted from {ground.Home.Town} on foot (portal {(ground.Portal is null ? "none" : "yes")}, cells {ground.Outdoor?.Cells.Count ?? 0}) and a place is not the climber's way (TODO(m6-first-tie-walk))");
                yield break;
            }

            if (Context.Dats is not { } dats)
            {
                Fail($"logistics:way-back:no-dats: the dats are not available, so the terrain height at the first cell of '{ground.Key}' cannot be read");
                yield break;
            }

            var cell = WayHomeRule.HuntCellOf(first);
            var height = HuntGround.TerrainHeight(dats, cell);
            if (height is null)
            {
                Fail($"logistics:way-back:no-height: the first cell 0x{first.Cell:X8} of '{ground.Key}' has no terrain height");
                yield break;
            }

            var at = HuntGround.StopPoint(cell, height.Value);
            if (S.State.Self.Position is not { } standing || !WayHomeRule.StandsNear(ground, standing.CellId))
            {
                // M6 sprint (live 2026-11-18 20:30:09 UTC: the way home from a town trip to Dryreach had failed, and this walk was planned from there, about a hundred landblocks off, as if she stood in Holtburg, freezing the
                // game in four-second plans): check before: the walk starts only where the ground's facts say a walk starts (WayHomeRule.StandsNear). Elsewhere the way home comes first, by the travel rule (a recall she can
                // cast, else the portals); check after, and a body still away fails the station by name, no walk planned.
                Log(string.Create(CultureInfo.InvariantCulture, $"logistics: way back: check before: she stands at {(S.State.Self.Position is { } p ? $"cell 0x{p.CellId:X8}" : "an unknown place")}, not in {ground.Home.Town}'s landblock 0x{ground.Home.LifestoneCell >> 16:X4} or {ground.Key}'s: the way home first"));
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Home), "home", optional: true)) { yield return h; }

                if (S.State.Self.Position is not { } home || !WayHomeRule.StandsNear(ground, home.CellId))
                {
                    Fail(string.Create(CultureInfo.InvariantCulture, $"logistics:way-back:not-near-home: after the way home ({LastTravelFailure ?? "no failure named"}) she stands at {(S.State.Self.Position is { } q ? $"cell 0x{q.CellId:X8}" : "an unknown place")}, not in {ground.Home.Town}'s landblock or {ground.Key}'s: no walk to the ground is planned from there"));
                    yield break;
                }
            }

            var from = S.State.Self.Position;
            Log(string.Create(CultureInfo.InvariantCulture, $"walking: purpose=ground (logistics trip: on foot from {ground.Home.Town} to the first cell of {ground.Key}: an outdoor ground has no portal and no place); check before: the player stands at {(from is { } f ? $"cell 0x{f.CellId:X8} ({f.Frame.Origin.X:0.#}, {f.Frame.Origin.Y:0.#})" : "an unknown place")}, the point is cell 0x{first.Cell:X8} ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#})"));
            if (from is { } here && (here.CellId >> 16) == (first.Cell >> 16) && Planar(here.Frame.Origin, at) <= JourneyTravel.ToleranceMetres)
            {
                Log($"logistics: the player already stands within {JourneyTravel.ToleranceMetres:0.#} m of the first cell's point; no walk");
                yield break;
            }

            foreach (var h in TravelTo(new TravelRequest(Destination("ground-first-cell", first.Cell, at), TravelPurpose.Cell), "the ground's first cell")) { yield return h; }

            var after = S.State.Self.Position;
            if (after is not { } there || (there.CellId >> 16) != (first.Cell >> 16) || Planar(there.Frame.Origin, at) > JourneyTravel.ToleranceMetres)
            {
                Fail(string.Create(CultureInfo.InvariantCulture, $"logistics:way-back:arrival-off-point: the walk back ended at {(after is { } q ? $"cell 0x{q.CellId:X8} ({q.Frame.Origin.X:0.#}, {q.Frame.Origin.Y:0.#})" : "an unknown place")}, not within {JourneyTravel.ToleranceMetres:0.#} m of ({at.X:0.#}, {at.Y:0.#}) in cell 0x{first.Cell:X8}"));
                yield break;
            }

            Log("logistics: check after: at the first cell's point");
        }

        /// <summary>
        /// M6 1 d (slice c's loose end): a trip that was due a learn and bought no scroll (a short purse, a refused buy) holds that learn trigger (<see cref="LearnHolds"/>) until the coin covers the scroll's price or the level rises, so
        /// the counters do not call the same trip again at once; logged once, when the hold is new.
        /// </summary>
        private void HoldLearn(HuntingGround ground, LearnNeed? learn, ResupplyRun run)
        {
            if (learn?.Spell is not { } spell || run.ScrollsBought > 0)
            {
                return;
            }

            var q = S.State.Self.Qualities;
            var price = ground.PriceOf(spell.ScrollWcid) ?? (ScrivenerCatalog.Default.ScrivenerOf(spell.Sellers[0].VendorWcid) is { } s ? s.PriceOf(spell.ScrollValue) : 0);
            var coin = PurseView.CoinUnreadAsZero(S.State); // the coin alone, for the line
            var pay = (int)Math.Min(int.MaxValue, PurseView.CounterCanPayUnreadAsZero(S.State, TownTripPurse.ScrivenersTakeNotes)); // M6 16 E5a2: the hold is judged on what the scrivener's counter can pay with: the coin and the faces of the notes held
            var (purse, cause) = LearnHoldCause(run, pay, price);
            if (holds.Hold(spell.SpellId, price, q.Level, purse))
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"logistics: learn hold: the trip bought no scroll of {spell.Name} ({spell.SpellId}; its price {price}, the coin {coin}, level {q.Level}; cause: {cause}): the learn:{spell.Name} trigger waits until {(purse ? "the coin covers it or the level rises" : "the level rises (the coin proves nothing for this cause)")}"));
                if (pay != coin)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"logistics: learn hold: the trade notes held ({pay - coin}) count with the coin: the hold is judged on what the scrivener's counter can pay with, {pay}{(purse ? ", and is released when that covers the price" : string.Empty)}"));
                }
            }
        }

        /// <summary>
        /// M6 1 fix (1-R1): why the trip bought no scroll. The purse is the reason only when the coin is under the price; any other cause (the scrivener not reached or not found in reach, the scroll not listed in his live list, no scroll after
        /// the walk, a refused buy) leaves the coin covering it, and a hold released on coin would call the same trip again at once. Internal: the tests pin each shape.
        /// </summary>
        /// <param name="run">The trip's resupply run.</param>
        /// <param name="coin">What the scrivener's counter can pay with now (M6 16 E5a2: the coin and the faces of the trade notes held; until then the coin alone).</param>
        /// <param name="price">The scroll's price.</param>
        internal static (bool Purse, string Cause) LearnHoldCause(ResupplyRun run, int coin, int price)
        {
            if (coin < price)
            {
                return (true, "purse short");
            }

            if (run.VendorsSkipped > 0)
            {
                return (false, "the scrivener was not reached");
            }

            if (run.NotFound > 0)
            {
                return (false, "the scrivener or the scroll was not found in reach");
            }

            if (run.LearnRefusals.Any(r => r.EndsWith(":no-scroll", StringComparison.Ordinal)))
            {
                return (false, "no scroll in the pack after the walk");
            }

            return (false, "the scroll was not in his list or the buy was refused");
        }

        private static double Planar(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

        private bool _xpUnpriced;

        private IEnumerable<ActionHandle?> SpendExperience()
        {
            if (_xpUnpriced)
            {
                yield break; // said once a trip (the first call's failure stands)
            }

            var table = XpSpendRule.TableOf(S, Context.Dats); // M6 1 b: the level up job's spend reads the table the same way (AutopilotScenario.LadderSpend)
            if (table is null)
            {
                _xpUnpriced = true;
                Fail("experience: the session has no XpTable and the journey no dats, so the experience cannot be priced (TODO(m5-logistics-xptable))");
                yield break;
            }

            var before = _spentXp;
            var whyBefore = _spendWhy;

            // M5 7 F3 (D87): the soak mage at level 80 spends by the dance shares and leaves out the skill it never trained. M6 1 b (m6-climber-strength): Strength first until the capacity carries the measured burden (read after
            // the resupply) under the ground's burden_trip_percent (at most 100%); a no-op for a mage whose Strength already carries it (BSD Mage at level 80 runs near 68%).
            // M6 item 14: the ladder's character wears the chest's armour set, so her Strength floor counts the Strength that stands beside her ranks (StandingStrength: the worn pieces' own line, the bot's while its stop applies); any other mage as before.
            var climber = CharacterPolicy.For(Context.Character).WarLadder;
            Func<bool>? standingBot = climber ? Context.Facts.BuffBotOn ?? (static () => false) : null;
            // M6 14 (m6-caster-spend): the trip's spend buys a held caster's points first as the level-up spend does, so the trip does not spend the pool the points wait for on the shares
            // M6 14 (m6-lore-spend): and her Arcane Lore toward a held piece's ItemDifficulty, read the same way (the station's equip pass has just judged the packs)
            // M6 14 hour load (m6-hour-load): the spend runs before the sale, so the Strength floor leaves out the sell loot she carries (the hour's line takes it to 150% by design)
            var rule = new XpSpendRule(table, Context.Log, followLevel: true, burdenTripPercent: Context.Dungeon.Supplies.BurdenTripPercent, standingBot: standingBot, casterFacts: climber ? Context.Facts.CasterSpend : null, loreFacts: climber ? Context.Facts.LoreSpend : null, sellLootBurden: s => CantripRoom.SellLootBurden(s, Context.Dungeon, Context.Character, Context.Spells));
            for (var call = 1; call <= MaxXpCalls; call++)
            {
                rule.Begin(S);
                while (!rule.Tick())
                {
                    yield return null;
                }

                _spentXp += rule.SpentXp;
                _raises += rule.Raises;
                var why = rule.End switch { XpSpendEnd.PoolCoversNothing => "pool-covers-nothing", XpSpendEnd.PlanReached => "plan-reached", _ => rule.End.ToString().ToLowerInvariant() };
                _spendWhy = whyBefore != "none" && _spentXp == before && rule.End == XpSpendEnd.PoolCoversNothing ? whyBefore : why; // M6 14 T: the second look of a trip that spent first finds the pool empty: the first spend's word stands
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
