using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The corpse stage of the death loop (M4 2.2 s1, D5; <see cref="DeathStages.Corpse"/>): the mage goes back for its own corpse, after the fight when the death loop fights (M4 3.1 d1: respawn, regear, rebuff, fight, then this), straight after the
/// respawn's vitals and before the re-gear when it does not (M4 2.2's order). Stages (each logged once as
/// <c>[death] stage &lt;name&gt;</c>): <c>corpse-teleporting</c> (<c>PlayerTools.Place</c> to the cell and position the corpse's F745 had, remembered by <see cref="DeathTally"/>), <c>corpse-settle</c> (the arrival, the den's floor rule),
/// <c>corpse-seek</c> (the corpse by its guid within <see cref="CorpseFindSeconds"/> of the arrival), <c>corpse-opening</c> (<c>OpenContainer</c>), <c>corpse-looting</c> (<see cref="CorpseLooter"/> with take-all),
/// <c>corpse-closing</c> (<c>CloseContainer</c>), <c>corpse-return</c> and <c>corpse-return-settle</c> (<c>Place</c> back to Holtburg, the bind's point). Through the Action API and <c>PlayerTools</c> only.
/// </summary>
/// <remarks>
/// <para><b>Preconditions</b> (each failing one is <c>corpse_looted=skipped</c> and a named Warn line, never a guess): the respawn was classified <c>lifestone</c>; a corpse was remembered; the seconds since its F745 are under
/// <see cref="PlayerCorpseMinRotSeconds"/>; the health share is at least <see cref="DeathOptions.RecoverMinHealthPercent"/>.</para>
/// <para><b>Aborts</b> (each named in the log and counted as <c>trip_abort=&lt;reason&gt;</c>; the abort still places back to Holtburg): <c>health-under-40</c>, <c>trip-over-90s</c>, <c>corpse-not-found</c>, <c>open-refused</c> (an open view
/// is closed fire-and-forget), <c>off-floor</c> (the arrival is not a floor cell of the den: the den stages' rule) and <c>place-refused</c> (the server did not take the teleport: the mage never left, so nothing is placed back).
/// A death on the trip (a 0x01AC, the Dead motion or a NumDeaths rise after the trip began) fails the leg loudly with the corpse's state named.</para>
/// <para><b>The lifestone protection does not cover the trip.</b> ACE dispels it on every teleport of the player's own (<c>Player_Location.cs</c> 702-703: "Your actions have dispelled the Lifestone's magic!"), so the arrival among the
/// tuskers is unprotected. What the trip has is speed (it starts at the end of the respawn's vitals wait, the settle is <see cref="CorpseSettleSeconds"/>) and the abort under <see cref="TripMinHealthPercent"/>% health.</para>
/// <para><b>M3's judged fields do not see the trip.</b> The tally-derived measures are taken once at the end of the respawn's vitals wait (<c>_atRespawn</c>, M4 3.1 d1: before the regear, the rebuff and the fight too) and the verdict keeps them, so the trip's blows,
/// its teleports' protection line and the old corpses its arrival re-creates never change <c>died=</c>, <c>deaths=</c>, <c>hits_taken=</c> or <c>protection=</c>; the trip's own figures are the four new fields.</para>
/// <para><b>The listing (M4 3.1 d1).</b> The opened corpse's own entries are classified by weenie class when the trip turns back (<c>decoys_dropped=</c> the listed decoys, <c>gear_dropped=</c> the listed items the loops need), and the entries that are in the pack after the trip are counted
/// (<c>decoys_recovered=</c>). A needed item that was listed and not taken is counted (<c>gear_left=</c>, report-only in its first gate, M4 3.1 fix review R2) and warned about, never a failure: with the death loop's fight on the re-gear already replaced it (what is left behind is a duplicate on the corpse), and in M4 2.2's order the re-gear after the trip spawns it.</para>
/// </remarks>
public sealed partial class DeathScenario
{
    /// <summary>
    /// ACE's least time a player corpse that holds items lives, in seconds (<c>W/Corpse.cs</c> 103-118, <c>RecalculateDecayTime</c>: <c>max(3600, level x 300)</c> seconds with items, 15 s empty; the floor is the safe bound for any level).
    /// A corpse older than this may be gone, so the trip is not made.
    /// </summary>
    public const double PlayerCorpseMinRotSeconds = 3600;

    /// <summary>The limit on the whole trip (from the teleport to the corpse until the way back is settled), in seconds; past it the trip aborts as <c>trip-over-90s</c>.</summary>
    public const double TripSeconds = 90;

    /// <summary>The health share, in percent of the maximum, under which the trip aborts as <c>health-under-40</c>.</summary>
    public const double TripMinHealthPercent = 40;

    /// <summary>How long after the arrival the corpse may take to be in the object table, in seconds (ACE re-creates it for the client at the arrival).</summary>
    public const double CorpseFindSeconds = 15;

    /// <summary>
    /// The settle after the trip's arrival, in seconds: shorter than the den stages' 3 s because the arrival is among the tuskers and unprotected. The Inventory lane waits for the post-teleport LoginComplete plus 0.5 s itself,
    /// so a move sent early is held by the lane, not refused.
    /// </summary>
    public const double CorpseSettleSeconds = 1;

    private int _tripEntries;
    private string _tripAbort = DeathLegVerdict.TripAbortNone;
    private double _tripStart = double.NaN;
    private double _tripEnd = double.NaN;
    private double _tripArrivedAt = double.NaN;
    private uint _tripCorpse;
    private int _tripNotices;
    private int _tripDeadMotions;
    private int _tripDeaths;
    private double _drainUntil = double.NaN;
    private CorpseLooter? _looter;
    private ActionHandle? _abandonedOpen;
    private DeathLegMeasures? _atRespawn;

    /// <summary>
    /// What the trip came to (<c>corpse_looted=</c>): skipped until a trip begins. M4 2.2 fix R2: <see cref="CorpseLoot.Yes"/> only when every entry of the opened view came back (<see cref="ItemsRecovered"/> equals the entries at
    /// the open, with no refusal, no timeout and no unresolved skip); <see cref="CorpseLoot.Partial"/> when some did (an abort included); <see cref="CorpseLoot.No"/> when none did. Read from the looter's counts, so a pick-up that
    /// settled while the loop drained after an abort (R3) is in it.
    /// </summary>
    public CorpseLoot CorpseLooted =>
        double.IsNaN(_tripStart) ? CorpseLoot.Skipped
        : ItemsRecovered <= 0 ? CorpseLoot.No
        : ItemsRecovered >= _tripEntries && _looter is { Refused: 0, TimedOut: 0, Skipped: 0 } ? CorpseLoot.Yes
        : CorpseLoot.Partial;

    /// <summary>The items the trip took out of the corpse (<c>items_recovered=</c>): the looter's confirmed pick-ups, those that settled after an abort included (R3).</summary>
    public int ItemsRecovered => _looter?.Looted ?? 0;

    /// <summary>The entries the corpse's opened view listed (0 before an open): what <c>items_recovered</c> is measured against.</summary>
    public int CorpseEntries => _tripEntries;

    /// <summary>Why the trip aborted (<c>trip_abort=</c>), or <see cref="DeathLegVerdict.TripAbortNone"/>.</summary>
    public string TripAbort => _tripAbort;

    /// <summary>The seconds the trip took from its teleport to the corpse (to now while it runs), or null when no trip began (<c>trip_s=</c>).</summary>
    public double? TripElapsedSeconds => double.IsNaN(_tripStart) ? null : (double.IsNaN(_tripEnd) ? _lastClock : _tripEnd) - _tripStart;

    /// <summary>
    /// True from the moment the corpse's open is confirmed and its contents are listed until its close is sent: the game's leg takes the <c>corpse</c> review frame then and holds the scenario (a held scenario sends no
    /// pick-up), so the container window shows the contents.
    /// </summary>
    public bool CorpseOpen => Stage == "corpse-looting";

    /// <summary>True while the trip is under way (from the teleport to the corpse until the way back is settled).</summary>
    public bool TripRunning => Stage.StartsWith("corpse-", StringComparison.Ordinal);

    // ------------------------------------------------------------------------------------------------ the preconditions

    /// <summary>
    /// The corpse stage's turn in the order after the respawn (<see cref="NextPost"/>): the trip begins (true, the stage is entered), or every precondition is named in the log and the stage is skipped (false; the order goes on).
    /// </summary>
    private bool TryBeginCorpse(IGameSessionView session, double clock)
    {
        if ((_stages & DeathStages.Corpse) == 0)
        {
            return false;
        }

        if (!_options.RecoverCorpse)
        {
            _log.Info(LogCategory, "corpse: --death-recover-corpse is off: the corpse is not looted and the regear spawns the gear (M3's run, D8); corpse_looted=skipped");
            return false;
        }

        var health = TryHealthShare(session, out var share) ? share : double.NaN;
        if (TryBeginPretripHeal(session, clock, health))
        {
            return true; // M4 3.1 d1 F1: the health share is the only thing in the way: heal first, the preconditions are judged again after (DeathScenario.Heal.cs)
        }

        var skip = CorpsePrecondition(session, health);
        if (skip is not null)
        {
            _log.Warn(LogCategory, "corpse: skipped (corpse_looted=skipped): " + skip + (FightOn ? string.Create(CultureInfo.InvariantCulture, $"; the trip comes after the fight and the pre-trip recovery ({_pretripHeals} heal casts, up to {PreTripRegenSeconds:0} s of regeneration) did not bring the health to the minimum: TODO(m4-death-trip-health)") : "; the regear spawns the gear (D5's gap)"));
            return false;
        }

        var position = _tally.LastCorpsePosition!.Value;
        (_tripCorpse, _tripNotices, _tripDeadMotions, _tripDeaths) = (_tally.LastCorpseGuid, _tally.VictimNotices, _tally.DeadMotions, _tally.Deaths);
        _tripStart = clock;
        var age = clock - _tally.LastCorpseAt;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"corpse: going back for 0x{_tripCorpse:X8}: it died {age:0} s ago (ACE's rot floor {PlayerCorpseMinRotSeconds:0} s, Corpse.cs 103-118) in cell 0x{position.CellId:X8}; the health share is {health:0}% (the trip needs {_options.RecoverMinHealthPercent:0}%, aborts under {TripMinHealthPercent:0}%); the lifestone's protection is dispelled by this teleport as by every one (Player_Location.cs 702-703), so the arrival is unprotected: the trip runs after the fight when the death loop fights (the tuskers are dead), straight after the respawn when it does not; TODO(m4-death-trip-live): the {CorpseSettleSeconds:0.#} s settle, the {TripMinHealthPercent:0}% abort and the {TripSeconds:0} s limit are first-run values until the lead's live run"));
        _teleportMark = _selfTeleports;
        _pending = null;
        _seam.Start(session, new TravelRequest(CorpseDestination(position), TravelPurpose.Corpse, _tripCorpse, CorpseTravelRangeMetres), clock); // M4 P5b: on foot over the room graph in a journey; today's place by default
        Enter("corpse-teleporting", clock);
        return true;
    }

    /// <summary>M4 P5b: the object range the corpse trip's travel asks for (a run stops within it of the corpse; the place ignores it). TODO(m4-corpse-run-range): a first value until the lead's live journey.</summary>
    public const double CorpseTravelRangeMetres = 2.0;

    private string? CorpsePrecondition(IGameSessionView session, double health)
    {
        if (_respawn != DeathRespawn.Lifestone)
        {
            return "the respawn was " + (_respawn is { } kind ? kind.ToString().ToLowerInvariant() : "not classified") + ", not lifestone";
        }

        if (_tally.LastCorpseGuid == 0 || _tally.LastCorpsePosition is null)
        {
            return "no corpse of the mage was remembered (the tally saw none between the death signal and the arrival)";
        }

        var age = session.Clock.Now - _tally.LastCorpseAt;
        if (double.IsNaN(age) || age >= PlayerCorpseMinRotSeconds)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the corpse is {age:0} s old, at or past ACE's rot floor of {PlayerCorpseMinRotSeconds:0} s (Corpse.cs 103-118)");
        }

        if (double.IsNaN(health))
        {
            return "the health share is unknown";
        }

        return health < _options.RecoverMinHealthPercent
            ? string.Create(CultureInfo.InvariantCulture, $"the health share is {health:0}%, under --death-recover-min-health {_options.RecoverMinHealthPercent:0.##}%")
            : null;
    }

    private static bool TryHealthShare(IGameSessionView session, out double percent)
    {
        percent = 0;
        if (!session.State.Self.Qualities.TryGetVitalLevels(1, out var current, out var max) || max == 0)
        {
            return false;
        }

        percent = current * 100.0 / max;
        return true;
    }

    private static TeleportDestination CorpseDestination(CellFrame at)
    {
        var heading = WorldLocations.HeadingFromQuaternion(at.Frame.Orientation.W, at.Frame.Orientation.Z);
        const string Name = "the mage's corpse";
        return new TeleportDestination(Name, [], new WorldLocation(Name, new CellPosition(at.CellId, at.Frame.Origin), true, heading), "the death loop's remembered corpse position (M4 2.2 s1)");
    }

    private DeathLegMeasures TallyMeasures(IGameSessionView session) =>
        DeathLegVerdict.Measure(_tally, session.State.Objects, _suicides, false, 0, double.IsNaN(_deathStart) ? null : DeathSeconds, null, null, BoundLifestone);

    // ------------------------------------------------------------------------------------------------ the guard

    /// <summary>A death on the trip fails the leg; the trip's limits abort it. Null when the trip goes on.</summary>
    private ScenarioStatus? TripGuard(IGameSessionView session, double clock)
    {
        if (_tally.VictimNotices > _tripNotices || _tally.DeadMotions > _tripDeadMotions || _tally.Deaths > _tripDeaths)
        {
            _tripAbort = "died";
            _looter?.Abandon("died"); // the leg fails and its measures freeze now: a pick-up still in flight is not drained (items_recovered is the settled count)
            return Fail(session, "corpse: the mage died on the trip (a victim notice, the Dead motion or a NumDeaths rise after the trip began); the corpse's state: " + CorpseState(session));
        }

        if (clock - _tripStart > TripSeconds)
        {
            return AbortTrip(session, clock, "trip-over-90s", string.Create(CultureInfo.InvariantCulture, $"the trip took over {TripSeconds:0} s"));
        }

        return TryHealthShare(session, out var share) && share < TripMinHealthPercent
            ? AbortTrip(session, clock, "health-under-40", string.Create(CultureInfo.InvariantCulture, $"the health share fell to {share:0}% (under {TripMinHealthPercent:0}%)"))
            : null;
    }

    private string CorpseState(IGameSessionView session)
    {
        var viewed = session.State.Containers.ViewedGuid;
        var inTable = session.State.Objects.TryGet(_tripCorpse, out _);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{_tripCorpse:X8} {(inTable ? "in the object table" : "not in the object table")}, {(viewed == _tripCorpse ? "open" : "not open")}, {_looter?.Looted ?? 0} item(s) taken, stage {Stage}");
    }

    private ScenarioStatus AbortTrip(IGameSessionView session, double clock, string reason, string why)
    {
        _tripAbort = reason;
        _log.Warn(LogCategory, $"corpse: trip aborted ({reason}): {why}; corpse_looted is decided when the trip ends (yes only if every entry came back, partial if some did, no if none); placing back to Holtburg");
        if (Stage == "corpse-opening" && _pending is not null)
        {
            _abandonedOpen = _pending; // the open may still land Confirmed: PollAbandonedOpen closes the view then
        }

        if (_looter is { } looter)
        {
            looter.Abandon("abandoned"); // M4 2.2 fix R3: a pick-up in flight is kept; the return stages step the looter until it settles (DrainLooter) and items_recovered counts it
            _drainUntil = clock + CorpseLooter.DrainSeconds;
        }

        CloseViewFireAndForget(session);
        _pending = null;
        if (reason == "place-refused")
        {
            return EndTrip(session, clock); // the mage never left Holtburg: nothing to place back from
        }

        return BeginReturn(session, clock);
    }

    private void CloseViewFireAndForget(IGameSessionView session)
    {
        var viewed = session.State.Containers.ViewedGuid;
        if (viewed != 0)
        {
            _ = session.Actions.CloseContainer(viewed, ActionSource.Host);
        }
    }

    private void PollAbandonedOpen(IGameSessionView session)
    {
        if (_abandonedOpen is not { Outcome: { } done })
        {
            return;
        }

        _abandonedOpen = null;
        if (done.Kind == ActionOutcomeKind.Confirmed)
        {
            _log.Info(LogCategory, $"corpse: the abandoned open of 0x{_tripCorpse:X8} landed Confirmed; closing it");
            CloseViewFireAndForget(session);
        }
    }

    // ------------------------------------------------------------------------------------------------ the steps

    private ScenarioStatus CorpseTeleportingStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        var travel = _seam.Step(session, clock);
        if (travel == TravelStatus.Failed)
        {
            // A place refused: the mage never left (nothing to place back from). A run or a recall that failed on the way: the trip is aborted and the way home goes through the seam as usual.
            return _seam.Means == TravelMeans.Place
                ? AbortTrip(session, clock, "place-refused", "the teleport to the corpse was not taken: " + _seam.Failure)
                : AbortTrip(session, clock, "travel-failed", "the travel to the corpse failed: " + _seam.Failure);
        }

        if (travel == TravelStatus.Arrived && (_seam.Authoritative || (_selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)))
        {
            _tripArrivedAt = clock;
            _settleUntil = clock + CorpseSettleSeconds;
            Enter("corpse-settle", clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseSettleStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        if (clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        var cell = session.State.Self.Position?.CellId ?? 0;
        if (!BlackSpawnDen.IsFloorCell(cell))
        {
            return AbortTrip(session, clock, "off-floor", $"the player settled in cell 0x{cell:X8}, which is not a floor cell of the den (the den stages' floor rule)");
        }

        _log.Info(LogCategory, $"corpse: arrived in cell 0x{cell:X8}; floor cell yes; looking for 0x{_tripCorpse:X8} (up to {CorpseFindSeconds:0} s from the arrival)");
        Enter("corpse-seek", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseSeekStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        if (session.State.Objects.TryGet(_tripCorpse, out _))
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: 0x{_tripCorpse:X8} is in the object table {clock - _tripArrivedAt:0.0} s after the arrival; opening it"));
            _pending = session.Actions.OpenContainer(_tripCorpse, ActionSource.Host);
            Enter("corpse-opening", clock);
            return ScenarioStatus.Running;
        }

        return clock - _tripArrivedAt > CorpseFindSeconds
            ? AbortTrip(session, clock, "corpse-not-found", $"0x{_tripCorpse:X8} was not in the object table within {CorpseFindSeconds:0} s of the arrival")
            : ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseOpeningStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        if (_pending is not { Outcome: { } opened })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (opened.Kind != ActionOutcomeKind.Confirmed)
        {
            return AbortTrip(session, clock, "open-refused", "the open of 0x" + _tripCorpse.ToString("X8", CultureInfo.InvariantCulture) + " was not confirmed: " + Describe(opened));
        }

        if (session.State.Containers.Viewed is not { } view || view.Guid != _tripCorpse)
        {
            return AbortTrip(session, clock, "open-refused", "the open was confirmed but the session shows no view of 0x" + _tripCorpse.ToString("X8", CultureInfo.InvariantCulture));
        }

        var list = new System.Text.StringBuilder();
        foreach (var entry in view.Entries)
        {
            list.Append(list.Length == 0 ? string.Empty : ", ");
            list.Append(session.State.Objects.TryGet(entry.Guid, out var item) ? $"'{item.Name}' wcid {item.Weenie.WeenieClassId}" : $"0x{entry.Guid:X8} (not yet resolved)");
        }

        _tripEntries = view.Entries.Count;
        CaptureListing(view);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: opened 0x{_tripCorpse:X8}: {view.Entries.Count} item(s): {(list.Length == 0 ? "none" : list.ToString())}; taking everything (take-all, so no type mask applies: TODO(m4-loot-rules-shop-mask) is the hunt's rule only)"));
        _looter = new CorpseLooter(_log, LogCategory, _tripCorpse, LootRules.TakeAll);
        Enter("corpse-looting", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseLootingStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        ResolveListing(session); // the entries' F745s come after the open: each entry's weenie class is read while its object is in the table
        var looter = _looter!;
        if (looter.Step(session, clock) == LootStatus.Running)
        {
            return ScenarioStatus.Running;
        }

        _log.Info(LogCategory, $"corpse: the loot ended ({looter.Summary}); items_recovered={ItemsRecovered} of {_tripEntries}{(CorpseLooted == CorpseLoot.Yes ? string.Empty : "; not a full recovery")}");
        _pending = session.Actions.CloseContainer(_tripCorpse, ActionSource.Host);
        Enter("corpse-closing", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseClosingStep(IGameSessionView session, double clock)
    {
        if (TripGuard(session, clock) is { } guarded)
        {
            return guarded;
        }

        if (_pending is not { Outcome: { } closed })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        _log.Info(LogCategory, closed.Kind == ActionOutcomeKind.Confirmed ? "corpse: closed" : "corpse: the close was not confirmed (" + Describe(closed) + "); not a failure");
        return BeginReturn(session, clock);
    }

    // ------------------------------------------------------------------------------------------------ the way back

    private ScenarioStatus BeginReturn(IGameSessionView session, double clock)
    {
        ResolveListing(session); // the table forgets the corpse's contents once the mage is away
        _teleportMark = _selfTeleports;
        _seam.Start(session, new TravelRequest(_holtburg, TravelPurpose.Home), clock); // M4 P5b: Lifestone Recall in a journey; today's place by default
        Enter("corpse-return", clock);
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M4 2.2 fix R3: an aborted trip's looter still has the pick-up that was in flight (<see cref="CorpseLooter.IsDraining"/>): it is stepped here (it sends nothing) until the outcome is counted, so
    /// <see cref="ItemsRecovered"/> does not read one low.
    /// </summary>
    private void DrainLooter(IGameSessionView session)
    {
        if (_looter is { IsDraining: true } looter)
        {
            _ = looter.Step(session, session.Clock.Now);
        }
    }

    private ScenarioStatus CorpseReturnStep(IGameSessionView session, double clock)
    {
        PollAbandonedOpen(session);
        DrainLooter(session);
        if (_tally.VictimNotices > _tripNotices || _tally.DeadMotions > _tripDeadMotions || _tally.Deaths > _tripDeaths)
        {
            return TripGuard(session, clock) ?? ScenarioStatus.Running; // a death on the way back is a death on the trip
        }

        var travel = _seam.Step(session, clock);
        if (travel == TravelStatus.Failed)
        {
            return Fail(session, "corpse: the way back to Holtburg was refused: " + _seam.Failure + "; the mage is still at the corpse");
        }

        if (travel == TravelStatus.Arrived && (_seam.Authoritative || (_selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)))
        {
            _settleUntil = clock + SettleSeconds;
            Enter("corpse-return-settle", clock);
            return ScenarioStatus.Running;
        }

        return _seam.Means == TravelMeans.Place && clock - _stageStart > TeleportSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"corpse: the player did not arrive back at Holtburg and become present within {TeleportSeconds:0.#} s (teleports {_selfTeleports - _teleportMark}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus CorpseReturnSettleStep(IGameSessionView session, double clock)
    {
        PollAbandonedOpen(session);
        DrainLooter(session);
        if (clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        if (_looter is { IsDraining: true })
        {
            if (clock < _drainUntil)
            {
                return ScenarioStatus.Running; // the abort's pick-up in flight is not counted yet
            }

            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"corpse: the pick-up in flight at the abort did not settle within {CorpseLooter.DrainSeconds:0} s; items_recovered counts what had settled"));
        }

        return EndTrip(session, clock);
    }

    /// <summary>
    /// M4 2.2 fix R5: called by the game's leg while it holds the scenario for a review frame (<see cref="CorpseOpen"/>'s <c>corpse-open</c> frame holds it for up to a few seconds among the tuskers). The scenario is not stepped then, so
    /// the trip's guard would not run; this runs only the guard (the health and time aborts, and a death), never a step of the trip. A pick-up is never sent from here.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>Null when the trip goes on (or none is under way); <see cref="ScenarioStatus.Failed"/> or <see cref="ScenarioStatus.Running"/> when the guard fired (a death fails the leg; an abort has started the way back: the caller ends its hold).</returns>
    public ScenarioStatus? GuardWhileHeld(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null || !TripRunning || Stage is "corpse-return" or "corpse-return-settle")
        {
            return null;
        }

        _lastClock = session.Clock.Now;
        return TripGuard(session, session.Clock.Now);
    }

    /// <summary>
    /// What the regear's log line says about the corpse (M4 2.2 fix R2): after a trip, how many of the opened corpse's entries came back (<c>n of m recovered</c>) and the <c>corpse_looted=</c> they make; with no trip (the option off,
    /// a precondition failed) M3's "the corpse is not looted (D8)". Never "not looted" after a trip that brought items back.
    /// </summary>
    internal string RegearCorpseNote() =>
        double.IsNaN(_tripStart)
            ? (FightOn && (_stages & DeathStages.Corpse) != 0 && _options.RecoverCorpse
                ? "the corpse trip comes after the rebuff and the fight (M4 3.1 d1); the regear spawns what the death destroyed, never a decoy"
                : "no corpse trip was made: the corpse is not looted (D8)")
            : string.Create(CultureInfo.InvariantCulture, $"the corpse trip recovered {ItemsRecovered} of {_tripEntries} (corpse_looted={DeathLegVerdict.CorpseText(CorpseLooted)}); the regear spawns only what the death destroyed and the trip did not bring back");

    private ScenarioStatus EndTrip(IGameSessionView session, double clock)
    {
        _tripEnd = clock;
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"corpse: the trip ended after {_tripEnd - _tripStart:0.0} s: corpse_looted={DeathLegVerdict.CorpseText(CorpseLooted)} items_recovered={ItemsRecovered} (of {_tripEntries} entries) trip_abort={_tripAbort}"));
        ClassifyListing(session); // gear_left= is report-only in its first gate (M4 3.1 fix, review R2): a Warn, never a failure
        return NextPost(session, clock);
    }
}
