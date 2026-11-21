using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D124-D127): the autopilot's stop at the buff bot. At home, before her own buff pass, when the option is on (<see cref="AutopilotOptions.BuffBot"/>, the default, D125), the home
/// is the bot's town and a line of the bot's profile is down or has under <see cref="BuffBotRule.RenewUnderSeconds"/> left, the <see cref="BuffBotStation"/> runs; her own pass follows it either way and casts
/// what the bot does not cover (the buff cycle reads a line as up when a stronger spell of its category stands). A stop that fails never stops the run: it is logged by name, her own whole pass runs, and the stop is
/// tried again at her next time home.
/// <para>The profile (the owner, 2026-11-12) is the option's (<see cref="AutopilotOptions.BuffBotProfile"/>, <c>--autopilot-buff-bot-word</c>: <c>all</c> by default, <c>tusker</c> the profile of before), and every rule here
/// reads it (<see cref="BotProfile"/>): the stop is due and done by its lines, the trip home and its urgent form key on the least seconds any of its lines has left, and the cover is its lines. The stations of the
/// run read the same profile from the journey's facts (<see cref="JourneyFacts.BuffBotProfile"/>).</para>
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop's spec: no needs of its own (she stands at home; the bot needs none of her mana).</summary>
    public static StationSpec BuffBotSpec { get; } = new(BuffBotStation.StationName, StationKinds.BuffBot, StationNeeds.None, 180);

    private readonly List<BuffBotLineRead> _botReads = [];
    private bool _botWordSaid;
    private bool _botFailedThisVisit;
    private int _botStopTry; // M6 audit (fix 1): the failed tries of this time home's stop
    private string? _botNotedWhy;
    private bool? _botStationKnown;
    private string? _botStationMissing;
    private bool _botTripArmed; // true once a fight has read every line of the bot over the trip's line; false again from a trip asked (or a stop that failed): a bot that never buffed her asks no trip, and one lapse asks one
    private bool _botTrip; // the trip asked is the bot's
    private bool _botTripWanted;
    private double _botTripLeast;
    private double _botTripReadAt = double.NegativeInfinity;

    /// <summary>The seconds between two reads of the bot's lines in a fight.</summary>
    public const double BotTripReadSeconds = 15;

    /// <summary>The profile this run asks the bot for: the option's (<see cref="AutopilotOptions.BuffBotProfile"/>).</summary>
    private BuffBotProfile BotProfile => _options.BuffBotProfile;

    /// <summary>The trips home this run asked for the bot's buffs alone.</summary>
    public int BuffBotTrips { get; private set; }

    /// <summary>
    /// M6 item 14 (the climb's third death, 2026-11-11 18:52 UTC): true when the fight should end for a trip home to the bot: the stop applies (<see cref="BuffBotApplies"/>), the catalog makes the station, and a line of the
    /// bot's profile has under <see cref="BuffBotRule.TripUnderSeconds"/> left. Read each <see cref="BotTripReadSeconds"/>. The rule is armed by a read in a fight with every line over the trip's line, so a run the bot
    /// never buffed (a resume in the ground, a bot that is not there) asks no trip, and one lapse asks one trip: it is armed again only when the lines read fresh.
    /// </summary>
    private bool BuffBotTripWanted(IGameSessionView session)
    {
        if (!BuffBotApplies())
        {
            return false;
        }

        if (_clock >= _botTripReadAt)
        {
            _botTripReadAt = _clock + BotTripReadSeconds;
            BuffBotRule.Read(BotProfile, session.State.Self.Qualities, session.Clock.Now, _botReads);
            _botTripLeast = BuffBotRule.MinRemaining(_botReads);
            if (_botTripLeast >= BuffBotRule.TripUnderSeconds)
            {
                _botTripArmed = true;
            }

            _botTripWanted = _botTripArmed && _botTripLeast < BuffBotRule.TripUnderSeconds && BuffBotStationKnown(out _);
        }

        return _botTripWanted;
    }

    /// <summary>
    /// True when the bot's trip goes now: in a lull, as every trip; or, with no lull and a line under <see cref="BuffBotRule.TripUrgentUnderSeconds"/>, by the urgent recall (<see cref="TripUrgent"/>: Lifestone Recall with the
    /// hostiles on her, when it is payable; the trip then runs from home, forced, and its stop at the bot renews the hour). The wait and the urgent leaving are each logged by name.
    /// </summary>
    private bool BuffBotTripGoes(IGameSessionView session)
    {
        if (!TripWaitsForLull("the buff bot's trip"))
        {
            return true;
        }

        return _botTripLeast < BuffBotRule.TripUrgentUnderSeconds
            && TripUrgent(session, string.Create(CultureInfo.InvariantCulture, $"the buff bot's lines lapse in {_botTripLeast:0} s (under {BuffBotRule.TripUrgentUnderSeconds:0}) and no lull came (m6-buffbot-trip-urgent)"));
    }

    /// <summary>The trigger the bot's hour last held, so the hold is logged once a trigger (M6 audit, <see cref="BotHourHolds"/>).</summary>
    private string? _botHourHeld;

    /// <summary>The trigger the bot's hour last let go, so the reason is logged once a trigger until the trip is called (<see cref="SayCall"/> clears it).</summary>
    private string? _botHourWent;

    /// <summary>
    /// M6 audit (fix 2, the owner's decision of 2026-11-19: town trips once an hour, just in time for the bot's rebuff; about 75 minutes of consumables): a trip the counters call for a reason the hourly trip serves
    /// (a stock line under its trip line, a scroll or a focus due, a sale, the sale cadence, the main pack's slots) waits for the bot's trip while the bot's stop applies and its lines stand, and while no such stock line
    /// runs down to its stop line before the bot's trip is due (her use since the last visit, <see cref="PackRoomMemory.Measure"/>; an unmeasured line does not wait). The bot's trip is the town trip: the forced
    /// logistics trip with its stop at the bot (<see cref="BuffBotTripWanted"/>). A stop line, the burden line, a full pack of keepers and the hour's load trip never wait. Live 2026-11-19 (run 4, level 11, the Burrow):
    /// a Mana Potion trip at 04:02:21 UTC and a taper trip at 04:25:50, 23 minutes apart, each its own walk home.
    /// </summary>
    private bool BotHourHolds(IGameSessionView session, LogisticsVerdict verdict)
    {
        if (!BuffBotApplies() || !BuffBotCover.For(session.State).On || !_botTripArmed)
        {
            // no bot's hour stands: the trip goes as before. M6 sprint (run 6, 2026-11-19 23:30:37 UTC, a Vitriol trip 50 minutes after the last one): the line said nothing when the rule let a trip go, so it now names why.
            BotHourWent(verdict, !BuffBotApplies() ? "the buff bot does not apply to this run" : !BuffBotCover.For(session.State).On ? "the buff bot's lines are not on her" : "the bot's lines have not read fresh since its last trip (the hour is not armed)");
            return false;
        }

        var now = session.Clock.Now;
        BuffBotRule.Read(BotProfile, session.State.Self.Qualities, now, _botReads);
        var least = BuffBotRule.MinRemaining(_botReads);
        var (stopUnder, used, minutes, name) = (0, 0, 0.0, string.Empty);
        if (verdict.Cause == LogisticsCause.StockLow && verdict.Line is { } line)
        {
            var measure = PackRoomMemory.For(session.State).Measure(now, [line.Wcid], _ => verdict.Held);
            (stopUnder, used, minutes, name) = (line.StopUnder, measure?.UseOf(line.Wcid) ?? 0, measure?.Minutes ?? 0, line.Name + ": ");
        }

        var holds = TripHourRule.Holds(verdict.Cause, least, BuffBotRule.TripUnderSeconds, verdict.Held, stopUnder, used, minutes, out var why);
        if (holds && _botHourHeld != verdict.Trigger)
        {
            _botHourHeld = verdict.Trigger;
            _log.Info(LogCategory, $"autopilot fight: the trip waits for the bot's hour (m6-audit-trip-hour): {verdict.Reason}; {name}{why}");
        }
        else if (!holds && _botHourHeld == verdict.Trigger)
        {
            _botHourHeld = null;
            _botHourWent = verdict.Trigger;
            _log.Info(LogCategory, $"autopilot fight: the bot's hour holds the trip no more: {verdict.Reason}; {name}{why}");
        }
        else if (!holds)
        {
            BotHourWent(verdict, name + why);
        }

        return holds;
    }

    /// <summary>Logs, once a trigger until the next trip is called, why the bot's hour does not hold a trip the counters call (M6 sprint: the reason was silent).</summary>
    private void BotHourWent(LogisticsVerdict verdict, string why)
    {
        if (_botHourWent == verdict.Trigger)
        {
            return;
        }

        _botHourWent = verdict.Trigger;
        _log.Info(LogCategory, $"autopilot fight: the bot's hour does not hold the trip (m6-audit-trip-hour): {verdict.Reason}; {why}");
    }

    /// <summary>The fight is asked to stop for the bot's trip: by name, and the rule stands down until the lines read fresh again.</summary>
    private void BuffBotTripAsked()
    {
        (_botTripArmed, _botTripWanted, _botTrip) = (false, false, true);
        BuffBotTrips++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff bot: a trip home is due: a line of '{BotProfile.Key}' has {_botTripLeast:0} s left (under {BuffBotRule.TripUnderSeconds:0}): the fight ends in this lull, the trip stops at the bot and comes back (m6-buffbot-trip)"));
    }

    /// <summary>The stops at the bot this run that ended with every line up.</summary>
    public int BuffBotStops { get; private set; }

    /// <summary>The stops at the bot this run that failed (her own pass ran in their place).</summary>
    public int BuffBotFailures { get; private set; }

    /// <summary>True while the stop at the bot applies to this run, wherever it is asked (the Home arrow here, the logistics trip at home through <see cref="JourneyFacts.BuffBotOn"/>): the option on, the level up job, the home the bot's town. No rung is asked: the run's ground and its home are known from the first frame, and the start-up trip runs before the ladder's first decision.</summary>
    private bool BuffBotApplies() => _options.BuffBot && _options.Job == AutopilotJob.LevelUp && _ground.Home.LifestoneCell >> 16 == BuffBotWire.Landblock; // no rung asked: live 2026-11-12 00:12 UTC the start-up trip ran before the ladder's first decision, the bot "did not apply", and the strength floor bought ranks the bot's line already gave

    /// <summary>
    /// M6 item 14 S3 (the build that leans on the bot): keeps the session's <see cref="BuffBotCover"/> in step with the run, each frame in the world, and logs each change once. The cover is on while the stop applies to the run
    /// (<see cref="BuffBotApplies"/>'s option, job and home town), the host's catalog is not known to lack the station, and the bot has not failed without ever answering this run: a bot that never buffed her covers nothing, so her own scrolls are bought (check, act, check:
    /// the plan never rests on a bot nobody saw). On the den's job nothing is set: the plan of before.
    /// </summary>
    private void BuffBotCoverSync(IGameSessionView session)
    {
        if (_options.Job != AutopilotJob.LevelUp)
        {
            return; // the den's mage buys what his own profile casts: nothing is set, nothing is said
        }

        // The run's ground is known from its first frame (the rung is decided later), so the cover stands before the start-up audit reads the learn plan.
        var here = _options.BuffBot && _ground.Home.LifestoneCell >> 16 == BuffBotWire.Landblock;
        var unanswered = BuffBotFailures > 0 && BuffBotStops == 0;
        var on = here && _botStationKnown != false && !unanswered;
        var cover = BuffBotCover.For(session.State);
        if (!_botWordSaid && _options.BuffBot)
        {
            _botWordSaid = true; // once a run: which word she tells and how many lines it asks for
            _log.Info(LogCategory, $"bot word: '{BotProfile.Key}' ({BotProfile.Lines.Count} lines, for {BotProfile.Purpose}; --autopilot-buff-bot-word{(ReferenceEquals(BotProfile, BuffBotWire.Default) ? ", the default" : string.Empty)}): the stop is due and done by these lines, the trip home is called by the least of them, and the cover is these lines");
        }

        if (cover.IsSet && cover.On == on && ReferenceEquals(cover.Profile, BotProfile))
        {
            return;
        }

        var why = on ? $"the stop at {BuffBotWire.Name} applies to this run (home {_ground.Home.Town})"
            : !_options.BuffBot ? "--autopilot-buff-bot=off"
            : !here ? $"the home of {_ground.Key} is {_ground.Home.Town}, and the bot stands in Holtburg (TODO(m6-buffbot-towns))"
            : _botStationKnown == false ? $"the host's station catalog makes no {BuffBotStation.StationName} station"
            : string.Create(CultureInfo.InvariantCulture, $"the bot has not answered this run ({BuffBotFailures} stop(s) failed, none landed)");
        cover.Set(on, why, BotProfile);
        _log.Info(LogCategory, on
            ? $"bot cover: on: {why}: she buys, keeps and reads no Self scroll of the {BotProfile.Lines.Count} lines of '{BotProfile.Key}', and the experience push counts them in the bootstrap's place"
            : $"bot cover: off: {why}: her own scrolls are bought and her own whole pass is planned");
    }

    /// <summary>Why the stop at the bot is due now, or null: check before, each reason for no stop logged once until it changes.</summary>
    private string? BuffBotDue(IGameSessionView session)
    {
        string? no = null;
        if (!_options.BuffBot)
        {
            no = "off (--autopilot-buff-bot=off): her own pass alone";
        }
        else if (_options.Job != AutopilotJob.LevelUp)
        {
            no = "the job is not the level up job: the den's mage casts level VII on himself, and the bot's lines are the same power (TODO(m6-buffbot-den-job)): his own pass alone";
        }
        else if (_ground.Home.LifestoneCell >> 16 != BuffBotWire.Landblock)
        {
            no = $"the home of {_ground.Key} is {_ground.Home.Town}, and the bot stands in Holtburg (TODO(m6-buffbot-towns)): her own pass alone";
        }
        else if (_botFailedThisVisit)
        {
            no = "the stop failed this time home: her own pass; it is tried again at her next time home";
        }
        else if (!BuffBotStationKnown(out var missing))
        {
            no = $"the host's station catalog makes no {BuffBotStation.StationName} station ({missing}): her own pass alone";
        }

        if (no is null)
        {
            BuffBotRule.Read(BotProfile, session.State.Self.Qualities, session.Clock.Now, _botReads);
            var up = BuffBotRule.CountWith(_botReads, 1);
            var least = BuffBotRule.MinRemaining(_botReads);
            if (BuffBotRule.Due(_botReads))
            {
                _botNotedWhy = null;
                return string.Create(CultureInfo.InvariantCulture, $"home: the buff bot first: {up} of {_botReads.Count} lines of '{BotProfile.Key}' are up, the least {least:0} s left (under {BuffBotRule.RenewUnderSeconds:0})");
            }

            no = string.Create(CultureInfo.InvariantCulture, $"not due: every line of '{BotProfile.Key}' has at least {BuffBotRule.RenewUnderSeconds:0} s left (the least {least:0})");
        }

        if (!string.Equals(_botNotedWhy, no, StringComparison.Ordinal))
        {
            _botNotedWhy = no;
            _log.Info(LogCategory, "buff bot: " + no);
        }

        return null;
    }

    /// <summary>True when the host's station factory makes the stop's station (asked once a run: a scripted or an older catalog without it is no reason to stop a run that buffs itself).</summary>
    private bool BuffBotStationKnown(out string? why)
    {
        if (_botStationKnown is null)
        {
            try
            {
                _ = _factory.Create(BuffBotSpec);
                (_botStationKnown, _botStationMissing) = (true, null);
            }
            catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                (_botStationKnown, _botStationMissing) = (false, e.Message);
            }
        }

        why = _botStationMissing;
        return _botStationKnown == true;
    }

    /// <summary>The stop ended: check after, by name, then her own pass either way.</summary>
    private ScenarioStatus BuffBotEnded(bool ok, string? why, string fields)
    {
        if (ok)
        {
            BuffBotStops++;
            _botStopTry = 0;
            _log.Info(LogCategory, $"buff bot: check after: every line landed ({fields}): her own pass for what the bot does not cover");
            foreach (var task in _options.KillTasks)
            {
                task.BotVisited(_log); // M6 14 guard task: a kill task held by a shut gate asks the gate again
            }

            return BeginRun(AutopilotState.Buff, BuffSpec, "home: after the buff bot, her own pass for what it does not cover");
        }

        BuffBotFailures++;
        if (++_botStopTry < BuffBotRule.StopTries)
        {
            // M6 audit (fix 1, the owner's decision of 2026-11-19): a stop that failed is asked again at once, up to BuffBotRule.StopTries, before her own pass takes its place
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"buff bot: the stop failed ({JourneyVerdict.Token(why ?? "failed")}; {fields}): try {_botStopTry + 1} of {BuffBotRule.StopTries} now (m6-audit-bot-retry)"));
            return BeginRun(AutopilotState.Buff, BuffBotSpec, string.Create(CultureInfo.InvariantCulture, $"home: the buff bot's stop failed: try {_botStopTry + 1} of {BuffBotRule.StopTries}"));
        }

        _botStopTry = 0;
        _botFailedThisVisit = true;
        _botTripArmed = false; // no trip home for a bot that did not answer at home
        _log.Warn(LogCategory, $"buff bot: the stop failed ({JourneyVerdict.Token(why ?? "failed")}; {fields}): the run goes on, her own whole pass runs now, and the stop is tried again at her next time home (m6-buffbot-fallback)");
        return BeginRun(AutopilotState.Buff, BuffSpec, "home: the buff bot's stop failed: her own pass");
    }
}
