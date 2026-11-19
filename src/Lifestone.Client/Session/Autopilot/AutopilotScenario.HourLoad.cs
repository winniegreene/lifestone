using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 hour load (the owner's ruling, 2026-11-16: when she is fighting somewhere she is comfortable, she should bring herself to 150% burden by the end of the hour by discarding loot that is less valuable per weight as time goes on.
/// She only keeps amazing items or major cantrips). The autopilot's half: the cycle's load begins at its first fight frame on the ground (<see cref="CycleLoad.Begin"/>), the
/// comfort rule is read on a cadence with its numbers logged (<see cref="ComfortRule"/>), the ramp's end is moved to the buff bot's trip when that comes first, and the hour's trip is called after every other trigger
/// (<see cref="LogisticsCause.Hour"/>). The loot step asks the line for each corpse (<see cref="CycleLoad.LineFor"/>); the swap of sell loot for a better value per burden is the loot rule's own, on every ground
/// (<see cref="MageLootRules.Plan"/>).
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The seconds between two reads of the comfort rule in a fight (Lifestone's own cadence: each read is one log line with every number).</summary>
    public const double ComfortReadSeconds = 60;

    /// <summary>The seconds between two samples of the incoming damage a second for the cycle's peak (the counters' own cadence).</summary>
    public const double IncomingSampleSeconds = CounterCheckSeconds;

    private double _comfortReadAt = double.NegativeInfinity;
    private double _incomingSampleAt = double.NegativeInfinity;
    private int _hourLeavesAtBegin;
    private bool _hourTrip;
    private bool _hourRaceLost; // set by the fight's losing-race read (FightRaceLost) when it found the race lost; read and cleared each fight frame
    private bool _hourRaceWas; // the trip asked is the hour's (its words; forced, as the station's own counters know nothing of the hour)

    /// <summary>The session's cycle load (<see cref="CycleLoad.For"/>), or null with no session.</summary>
    private CycleLoad? HourLoad => _session is { } s ? CycleLoad.For(s.State) : null;

    /// <summary>
    /// One fight frame of the hour load (the ground's own fight, not a keeper's fight-back): the cycle's load begins on its first frame (once the capacity is read), the incoming peak is sampled, and the comfort rule is read every
    /// <see cref="ComfortReadSeconds"/> (and at the beginning), each read logged.
    /// </summary>
    private void HourLoadFrame(IGameSessionView session)
    {
        var load = CycleLoad.For(session.State);
        if (!load.Begun)
        {
            var context = LootContext.Of(session.State);
            if (context.Capacity <= 0)
            {
                return; // not read yet: the next frame begins it
            }

            var settings = _options.HourLoad;
            var top = _counters.LineCount > 0 ? Math.Min(settings.TopPercent, _counters.BurdenTripPercent) : settings.TopPercent; // a trip is due strictly over the burden trip line: the top never calls it
            load.Begin(_ground.Key, _clock, context.Burden, context.Capacity, settings, top);
            _hourLeavesAtBegin = _survivalLeavesRun;
            _comfortReadAt = double.NegativeInfinity;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"hour load: cycle {_cycle} begins on {_ground.Key}: {settings}; burden {context.Burden} of {context.Capacity} ({load.StartPercent:0.#}%), base line {MageLootThresholds.LoadLineOf(_ground)}%, top {top}%{(top < settings.TopPercent ? string.Create(CultureInfo.InvariantCulture, $" (the settings' {settings.TopPercent}% held to the ground's burden trip line {_counters.BurdenTripPercent}%)") : string.Empty)} (m6-hour-load)"));
        }

        if (!load.Settings.On)
        {
            return;
        }

        if (session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax))
        {
            load.NoteHealth(health, healthMax); // each frame: the cycle's lowest health against the vitals policy's floor (the comfort rule's outcome)
        }

        var losing = _hourRaceLost || _guard is { RaceLost: true };
        if (losing && !_hourRaceWas)
        {
            load.NoteRaceLost(); // each losing race once, from the frame it is found lost
        }

        (_hourRaceWas, _hourRaceLost) = (losing, false);
        if (_clock - _incomingSampleAt >= IncomingSampleSeconds && _hostiles is { } watch)
        {
            _incomingSampleAt = _clock;
            load.NoteIncoming(watch.IncomingPerSecond(_clock));
        }

        if (_clock - _comfortReadAt < ComfortReadSeconds || load.Latched)
        {
            return; // latched: the read that latched it was logged, and nothing read later changes the cycle's line
        }

        _comfortReadAt = _clock;
        double? heal = _guard is { } guard && guard.Race(session, _clock, out var race, out _) ? race.HealPerSecond : null;
        var way = AutopilotTravelRule.Home(AutopilotTravelRule.Input(session.State, _ground, _context.Facts, _context.Routes, RungDirection.Home, _context.Grounds, _context.Spells), AutopilotTravelRule.BookRead(session.State.Self.Spellbook), _context.Routes);
        var trial = _options.Trial is { Phase: LadderTrialPhase.On } on && string.Equals(on.Ground, _ground.Key, StringComparison.Ordinal);
        var input = new ComfortInput(_ground.Key, _ground.Jumps.Count, way.WayName, trial, Approach: false, _tally.Retreats, _survivalLeavesRun - _hourLeavesAtBegin, _tally.Deaths, load.RacesLost, load.Health, load.IncomingPeak, load.IncomingMean, heal, MageLootThresholds.LoadLineOf(_ground), load.TopPercent);
        var verdict = ComfortRule.Decide(in input);
        var inForce = load.Apply(in verdict);
        if (BuffBotApplies() && _botTripArmed && double.IsFinite(_botTripLeast) && _botTripLeast > 0)
        {
            load.RampEnds(_clock, _botTripLeast - BuffBotRule.TripUnderSeconds); // the bot's trip is called when its least line has TripUnderSeconds left: the pack is at the top then
        }

        var line = load.LineAt(MageLootThresholds.LoadLineOf(_ground), _ground.Jumps.Count > 0, _clock);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{ComfortRule.Line(in input, in verdict, inForce)}; the line now {line}%, the ramp ends in {Math.Max(0, load.RampEndClock - _clock) / 60:0.#} min{(load.Latched ? " (latched for the cycle)" : string.Empty)} (m6-hour-load)"));
    }

    /// <summary>
    /// The hour's trip (after the counters' own triggers and the buff bot's, which win): due when the rising line was in force and the settings' minutes are fought (<see cref="CycleLoad.HourDue"/>), not while the trip loop guard holds the
    /// trips, and in a lull as every trip. Logged with the verdict's words.
    /// </summary>
    private bool HourLoadTripDue(IGameSessionView session, int kills)
    {
        if (_keeperBack is not null || HourLoad is not { } load || !load.HourDue(_clock) || _tripLoop.Holding(_tally.Kills + kills) || TripWaitsForLull("the hour's trip"))
        {
            return false;
        }

        var context = LootContext.Of(session.State);
        var percent = context.Capacity <= 0 ? 0 : (int)((((long)context.Burden * 100) + context.Capacity - 1) / context.Capacity);
        var verdict = LogisticsVerdict.HourUp((int)(load.Elapsed(_clock) / 60), (int)Math.Round(load.Settings.Minutes), percent, load.TopPercent);
        load.NoteHourCalled();
        _hourTrip = true;
        CallTrip(session, verdict); // M6 16 E5d: the verdict the trip carries
        _log.Info(LogCategory, $"autopilot fight: the counters say a trip is due: {verdict.Reason} (trigger={verdict.Trigger}; the highest line the loot was given {load.HighestLine}%, the line now {load.LineAt(MageLootThresholds.LoadLineOf(_ground), _ground.Jumps.Count > 0, _clock)}%)");
        return true;
    }

    /// <summary>
    /// M6 14 (the lead, 2026-11-16, on ladder42: no line she can cast lapses inside a round, whatever its length; a round that would outlast a line the fight cannot recast safely ends in its trip first): a line that ran out in the fight
    /// and is still down after the fight's own window to recast it under fire (the hard floor, <see cref="BuffOptions.HardMarginSeconds"/>: a lapsed line is recast between the fight's casts at once, F25) calls the round's trip, in a lull
    /// as every trip, not while the trip loop guard holds; the trip's leave refresh and the pass at home recast it. Logged once a line.
    /// </summary>
    private bool LapsedLineTripDue(IStoppableFight fight, int kills)
    {
        if (_keeperBack is not null || fight is not IBuffRefreshFight refresh || refresh.LapsedDown(_clock) is not { } down)
        {
            return false;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        var window = _buffOptions.HardMarginSeconds;
        if (down.DownSeconds < window || _tripLoop.Holding(_tally.Kills + kills) || TripWaitsForLull(string.Create(CultureInfo.InvariantCulture, $"the lapsed line's trip ({down.Line} down {down.DownSeconds:0} s)")))
        {
            return false;
        }

        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot fight: a trip is due: {down.Line} ran out {down.DownSeconds:0} s ago and the fight has not recast it within its hard floor's {window:0} s (F25): the round's trip comes first; the leave refresh and the pass at home recast it (m6-line-outlasts-round)"));
        return true;
    }

    /// <summary>The cycle line's <c>hour_load=</c> field, then the cycle's load ends (the next fight frame begins a new one).</summary>
    private string HourLoadEndCycle()
    {
        if (HourLoad is not { } load)
        {
            return _options.HourLoad.On ? "held" : "off";
        }

        var field = load.Begun ? load.Field : _options.HourLoad.On ? "held" : "off";
        load.End();
        return field;
    }
}
