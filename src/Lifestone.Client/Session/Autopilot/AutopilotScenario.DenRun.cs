using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.World;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 item 14 (the owner's order, 2026-11-12): the den run, the task <see cref="AutopilotTaskKind.DenRun"/> inside the level up job (<c>--autopilot-den-run</c>, off by default). The known state hands it to <see cref="DenRunArrow"/> at home
/// or on the ground, after the audit, where the job would buff or fight; it does these in order and then stops by name:
/// <list type="number">
/// <item><b>The trip home with its sale</b>: one forced logistics trip (<see cref="TripSpec"/>, the errand's): home from where she stands, the sale by the sell rules, the stock, the scrolls (Primary Portal Tie and Primary Portal Recall
/// are first in the learn plan while the order stands: <see cref="ScrivenerLearnPlan.DenRunLines"/>), the experience, and the trip's own stop at the buff bot. The trip ends at home: no way back is begun
/// (<see cref="TripSafePointStop"/>).</item>
/// <item><b>The buff bot</b>, when a line of the profile is still due (<see cref="BuffBotDue"/>), then <b>her own pass</b> (<see cref="BuffSpec"/>), which ends in the known state here, not on the way to the rung.</item>
/// <item><b>The check before the run</b>, each miss a refusal by name and a stop (<c>den-run:refused:*</c>, never a start): the level under <see cref="DenRunStation.MinLevel"/>; the buff bot off, or told another word than
/// <see cref="BuffBotWire.All"/>; a line of the bot's profile down or under <see cref="BuffBotRule.RenewUnderSeconds"/> (the lines are not fresh); the book short of Primary Portal Tie, Primary Portal Recall or Lifestone Recall; the
/// burden at <see cref="DenRunBurdenPercent"/> percent of the capacity or over (ACE slows a body from 100%: <c>EncumbranceSystem.GetBurdenMod</c>, Physics/Common/EncumbranceSystem.cs:32-40 @95ef426d, is 1 under a burden of 1.0 and
/// 2 - burden above it).</item>
/// <item><b>The run</b>: the <see cref="DenRunStation"/> in the Travel state. While its road is run only (<see cref="JourneyFacts.RunOnly"/>) the survival layer and the vitals guard hold nothing (<see cref="SurvivalFrame"/>).</item>
/// <item><b>The stop</b>: <see cref="DenRunTiedStop"/> (ok) when the tie linked, else the station's failure by name under <c>den-run:</c>. The stop's own rule recalls her home first when she is not there.</item>
/// </list>
/// <b>One attempt an order.</b> A death while the station runs ends the run at once (<c>den-run:died:&lt;stage&gt;</c>): one line says where and by what, no death station is entered, the corpse is not fetched and nothing sets out again.
/// A run that was cut (a reconnect, a watchdog, a hand-back) is not begun again either (<c>den-run:cut</c>). A relaunch with the switch is a new order.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop of a den run whose tie linked (ok).</summary>
    public const string DenRunTiedStop = "den-run:tied";

    /// <summary>The prefix of every stop of the den run.</summary>
    public const string DenRunPrefix = "den-run:";

    /// <summary>The burden, in percent of the capacity, from which the run does not start (she runs light, by the order: under 100%).</summary>
    public const double DenRunBurdenPercent = 100;

    /// <summary>The den run's station: no need but peace; its expected time is the road's (about 250 s of hub, 1.4 km of run, the minute and the casts).</summary>
    public static StationSpec DenRunSpec { get; } = new(DenRunStation.StationName, StationKinds.DenRun, null, 1500);

    private bool _denTripBegun;
    private bool _denTripDone;
    private bool _denBotAsked;
    private bool _denPassAsked;
    private bool _denPassDone;
    private bool _denRunBegun;
    private bool _denRunOnlyLogged;
    private string? _denRunEnd;
    private CellFrame? _denRunLastAlive;

    /// <summary>True while the order stands (the task is the den run).</summary>
    private bool DenRunOrdered => _options.Task.Kind == AutopilotTaskKind.DenRun;

    /// <summary>True while the den run's own station runs (begun, not ended).</summary>
    private bool DenRunRunning => DenRunOrdered && _denRunBegun && _denRunEnd is null;

    /// <summary>The stop the den run ended by, or null (the hosts and the tests read it).</summary>
    public string? DenRunEnd => _denRunEnd;

    /// <summary>The order's arrow at home or on the ground (see the class).</summary>
    private ScenarioStatus? DenRunArrow(IGameSessionView session, AutopilotPlace place)
    {
        var c = CultureInfo.InvariantCulture;
        if (_denRunEnd is { } ended)
        {
            return TaskStop(ended, ok: ended == DenRunTiedStop, "the den run ended before this known state (the recall home before the stop was cut and is asked again)");
        }

        if (_taskDone)
        {
            _denRunEnd = DenRunTiedStop;
            return TaskStop(DenRunTiedStop, ok: true, "the den run's tie was made before the hand-back; only the stop was left");
        }

        if (_denRunBegun)
        {
            return DenRunStop(DenRunPrefix + "cut", "the den run's station was cut before it ended (a reconnect, a watchdog or a recovery): one attempt an order, so it is not begun again");
        }

        // check before anything is begun: the two refusals no trip can cure
        var q = session.State.Self.Qualities;
        if (q.Level < DenRunStation.MinLevel)
        {
            return DenRunStop(string.Create(c, $"{DenRunStation.RefusedPrefix}level:{q.Level}<{DenRunStation.MinLevel}"), string.Create(c, $"the den run is ordered for level {DenRunStation.MinLevel}; she is level {q.Level}"));
        }

        if (!_options.BuffBot)
        {
            return DenRunStop(DenRunStation.RefusedPrefix + "bot-off", "the den run needs the buff bot's lines and the bot is off (--autopilot-buff-bot=off)");
        }

        if (!ReferenceEquals(BotProfile, BuffBotWire.All))
        {
            return DenRunStop(DenRunStation.RefusedPrefix + "bot-word:" + BotProfile.Key, $"the den run needs every protection, the bot's word '{BuffBotWire.All.Key}'; the run's word is '{BotProfile.Key}' (--autopilot-buff-bot-word)");
        }

        // 1. the trip home with its sale
        var trips = _total.Trips + _tally.Trips;
        if (!_denTripBegun)
        {
            (_denTripBegun, _taskBegun, _taskTripsAt) = (true, true, trips);
            _log.Info(LogCategory, $"den run: ordered (--autopilot-den-run): 1 of 4: the trip home with its sale, begun at {PlaceName(place)} ({Cell(session)}): one forced logistics trip; the learn plan reads {string.Join(" and ", ScrivenerLearnPlan.DenRunLines)} first; the trip ends at home");
            return BeginRun(AutopilotState.Trip, TripSpec(forced: true), "the den run: the trip home with its sale (trip=forced)");
        }

        if (!_denTripDone)
        {
            if (trips > _taskTripsAt)
            {
                _denTripDone = true;
                _log.Info(LogCategory, string.Create(c, $"den run: the trip was made (trips {_taskTripsAt} -> {trips})"));
            }
            else if (_tripPending)
            {
                _taskAgain++;
                if (_taskAgain > TaskRunsAgain)
                {
                    return DenRunStop(DenRunPrefix + "trip:cut-again", string.Create(c, $"the den run's trip was cut or failed {_taskAgain} times (it is made again {TaskRunsAgain} time(s) at most)"));
                }

                _log.Warn(LogCategory, string.Create(c, $"den run: the trip was cut or failed before it ended: it runs again from its start at {PlaceName(place)} (run again {_taskAgain} of {TaskRunsAgain})"));
                return ResumeTrip();
            }
            else
            {
                return DenRunStop(DenRunPrefix + "trip:not-made", "the den run's trip ended with no trip counted (skipped or given up): the run is not begun on an unsold pack");
            }
        }

        if (place != AutopilotPlace.Home)
        {
            return DenRunStop(DenRunStation.RefusedPrefix + "not-home", $"after the trip she stands {PlaceName(place)} ({Cell(session)}), not at home: the run starts from {_ground.Home.Town}");
        }

        // 2. the buff bot, then her own pass
        if (!_denPassDone)
        {
            if (!_denBotAsked && BuffBotDue(session) is { } botWhy)
            {
                _denBotAsked = true;
                _log.Info(LogCategory, "den run: 2 of 4: the buff bot's stop, then her own pass: " + botWhy);
                return BeginRun(AutopilotState.Buff, BuffBotSpec, botWhy);
            }

            if (!_denPassAsked)
            {
                _denPassAsked = true;
                _log.Info(LogCategory, "den run: 2 of 4: her own pass (the bot's lines stand or its stop was made on the trip)");
                return BeginRun(AutopilotState.Buff, BuffSpec, "the den run: her own pass before the run");
            }

            return DenRunStop(DenRunPrefix + "pass:not-made", "her own buff pass was begun and did not end (cut): the run is not begun half buffed");
        }

        // 3. the check before the run
        BuffBotRule.Read(BotProfile, q, session.Clock.Now, _botReads);
        var up = BuffBotRule.CountWith(_botReads, 1);
        var least = BuffBotRule.MinRemaining(_botReads);
        if (BuffBotRule.Due(_botReads))
        {
            return DenRunStop(string.Create(c, $"{DenRunStation.RefusedPrefix}bot-lines:{up}of{_botReads.Count}"), string.Create(c, $"the bot's lines are not fresh: {up} of {_botReads.Count} lines of '{BotProfile.Key}' are up, the least {least:0} s left (each must be up with {BuffBotRule.RenewUnderSeconds:0} s or more)"));
        }

        var den = GroundCatalog.TryGet(DenRunStation.DefaultGround);
        var book = session.State.Self.Spellbook;
        var tieSpell = den?.Portal?.TieSpell ?? DenTravelStation.PortalTieSpell;
        var recallSpell = den?.Portal?.RecallSpell ?? DenTravelStation.PortalRecallSpell;
        if (DenRunStation.Refusal(q.Level, book.Knows(tieSpell), book.Knows(recallSpell), book.Knows(RecallHome.LifestoneRecallSpell)) is { } refused)
        {
            return DenRunStop(refused, string.Create(c, $"the book ({book.Count} spells) after the trip: Primary Portal Tie {tieSpell} {(book.Knows(tieSpell) ? "known" : "not known")}, Primary Portal Recall {recallSpell} {(book.Knows(recallSpell) ? "known" : "not known")}, Lifestone Recall {RecallHome.LifestoneRecallSpell} {(book.Knows(RecallHome.LifestoneRecallSpell) ? "known" : "not known")} (the trip's learn lines say why a scroll was not bought or read)"));
        }

        var burdenKnown = TryLoadShares(out var freeSlots, out var burden);
        if (burdenKnown && burden >= DenRunBurdenPercent)
        {
            return DenRunStop(string.Create(c, $"{DenRunStation.RefusedPrefix}burden:{burden:0}"), string.Create(c, $"the burden is {burden:0.#}% of the capacity after the sale (the run starts under {DenRunBurdenPercent:0}%: ACE slows a body from 100%)"));
        }

        // 4. the run
        _denRunBegun = true;
        _denRunLastAlive = session.State.Self.Position;
        _log.Info(LogCategory, string.Create(c, $"den run: 3 of 4: check before: level {q.Level} (at least {DenRunStation.MinLevel}); the bot's {up} of {_botReads.Count} lines of '{BotProfile.Key}' up, the least {least:0} s left; the book holds the tie, the portal recall and the lifestone recall; burden {(burdenKnown ? burden.ToString("0.#", c) + "%" : "unread")}, {freeSlots} free slots: the run begins ({DenRunStation.StationName} to {DenRunStation.DefaultGround}); one attempt"));
        return BeginRun(AutopilotState.Travel, DenRunSpec, "the den run: the run-only road to the den's entrance, the minute inside, the tie and home");
    }

    /// <summary>Her own pass of the den run ended well: the known state decides the run (never the way out to the rung).</summary>
    private ScenarioStatus DenRunAfterPass()
    {
        _denPassDone = true;
        _log.Info(LogCategory, "den run: her own pass ended: the check before the run");
        return EnterKnown("the den run: buffed");
    }

    /// <summary>One frame of the den run's station, before it is stepped: where she last stood alive (for a death's line), and the Travel state's progress while the station stands by design (the minute inside, a rest for a cast's mana, the casts).</summary>
    private void DenRunFrame(IGameSessionView session)
    {
        var self = session.State.Self;
        if (self.Travel == SelfTravel.Present && self.Position is { } at && (!self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) || health > 0))
        {
            _denRunLastAlive = at;
        }

        NoteTravelProgress(session);
        if ((_run as ScriptLeg)?.Stage is "inside" or "tie" or "home")
        {
            _watchdogs.Progress(_clock); // each of these waits has its own bound in the station
        }
    }

    /// <summary>The den run's station ended: the stop by name (see the class).</summary>
    private ScenarioStatus DenRunEnded(bool ok, string? why, string fields)
    {
        _context.Facts.RunOnly = null;
        var fail = Token(fields, "fail") ?? "failed";
        if (_deathSeen || fail.StartsWith("died:", StringComparison.Ordinal) || fail.StartsWith("dead:", StringComparison.Ordinal))
        {
            return DenRunDeath(fail.StartsWith("died:", StringComparison.Ordinal) ? fail.Split(':')[1] : Token(fields, "fail") ?? "run", why);
        }

        if (ok)
        {
            _taskDone = true;
            _portalProven = false; // the one tie slot now holds the den run's den, not this run's ground
            _log.Info(LogCategory, $"den run: 4 of 4: the tie linked ({fields}): stop={DenRunTiedStop}");
            return DenRunStop(DenRunTiedStop, "the den run's tie linked: the den is reached by Primary Portal Recall from now on", ok: true);
        }

        var name = fail.StartsWith(DenRunPrefix, StringComparison.Ordinal) ? fail : DenRunPrefix + fail;
        _log.Warn(LogCategory, $"den run: the run failed by name ({name}; {fields}): {why ?? "no failure text"}; one attempt an order: the run stops (the stop's rule recalls her home first)");
        return DenRunStop(name, why ?? "the den run's station failed");
    }

    /// <summary>
    /// A death while the den run's station runs: the run is over. One line with where and by what, no death station, no corpse trip, nothing set out again; the stop is <c>den-run:died:&lt;stage&gt;</c>. Null when the den run's station is
    /// not the one running (a death on its trip or its pass is the job's ordinary death).
    /// </summary>
    private ScenarioStatus? DenRunDied()
    {
        if (!DenRunRunning)
        {
            return null;
        }

        var stage = (_run as ScriptLeg)?.Stage ?? "run";
        return DenRunDeath(stage, null);
    }

    private ScenarioStatus DenRunDeath(string stage, string? why)
    {
        var where = _denRunLastAlive is { } at ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{at.CellId:X8} ({at.Frame.Origin.X:0.#}, {at.Frame.Origin.Y:0.#}, {at.Frame.Origin.Z:0.#})") : "an unread cell";
        var by = _deathText ?? why ?? "not named by the server";
        _deathSeen = false;
        _context.Facts.RunOnly = null;
        _tally.Deaths++;
        NoteWhy(UnscriptedDeathStop);
        var name = DenRunPrefix + "died:" + JourneyVerdict.Token(stage);
        _log.Error(LogCategory, $"den run: died: stage={stage} where={where} by={by}: the run is over; the corpse lies where she died and is not fetched; one attempt an order: nothing sets out again; stop={name}");
        DisposeRun();
        return DenRunStop(name, $"she died on the den run ({stage}) at {where}: {by}");
    }

    private ScenarioStatus DenRunStop(string name, string why, bool ok = false)
    {
        _denRunEnd = name;
        if (!ok)
        {
            _log.Warn(LogCategory, $"den run: stop={name}: {why}");
        }

        return TaskStop(name, ok, why);
    }

    /// <summary>The survival layer's frame while the road is run only: null (go) with the stand-down logged once, else false: the layer decides as ever.</summary>
    private bool DenRunStandsDown()
    {
        if (_context.Facts.RunOnly is not { } runOnly)
        {
            _denRunOnlyLogged = false;
            return false;
        }

        if (!_denRunOnlyLogged)
        {
            _denRunOnlyLogged = true;
            _log.Info(LogCategory, $"survival: {StateName(_state)}: stand-down: run-only: {runOnly}: the layer and the vitals guard hold nothing and no keeper's pass is begun; the station names each attacker it does not answer and drinks on the move");
        }

        SurvivalEnd("the road is run only");
        return true;
    }
}
