using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 sprint (the owner's decision of 2026-11-18: the vitae burnt on a known-good ground first, the corpse after): the autopilot's half of the vitae burn (<see cref="VitaeBurn"/>).
/// <list type="number">
/// <item><b>Defer.</b> The death handler asks once, after the re-arm and the re-buff (<see cref="Scenarios.Journey.JourneyFacts.DeferCorpse"/>). On the ladder's own climb, with vitae on her, on a rung whose ground is the run's, with a
/// hunting ground on a rung below and the corpse's rot not yet near, the corpse is owed: the rung and the run's ground are held, and the handler ends where the body stands. Otherwise the trip goes on as before, by name.</item>
/// <item><b>Burn.</b> The cycle's check reads the hold (<c>vitae-held:&lt;rung&gt;</c>), so the run stops <c>rung-moved:&lt;rung below&gt;</c> and the host's restart hunts there.</item>
/// <item><b>Over.</b> The fight ends in a lull once the vitae is gone or the rot is near (<see cref="VitaeBurnt"/>); the cycle's check releases the hold and takes her rung again (<c>rung-moved</c>).</item>
/// <item><b>Fetch.</b> A run on the corpse's ground takes the death's tally and enters the death station again (the cut station's way, 4-R2): the corpse trip by its usual rules (the gate, the approach, the coin and the drop rules).</item>
/// </list>
/// Every step logs one <c>vitae burn:</c> line.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The fight's stop when the vitae burn is over (<see cref="VitaeBurnOver"/>): the cycle ends at once and its check takes her rung again.</summary>
    public static FightStopReason VitaeBurnt { get; } = new("vitae-burnt");

    private bool _vitaeDeferred; // the handler deferred this death's corpse: the tally is handed to the burn when the station ends
    private bool _vitaeOverLogged;
    private bool _vitaeFetch; // this death station is an owed corpse's fetch: no death is counted at its end

    private VitaeBurn Burn => _options.ApproachHolds.Vitae;

    /// <summary>True on the ladder's own climb (the level up job's job, not a quest or a den run).</summary>
    private bool LadderClimb => _options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job;

    /// <summary>The handler's question (see the class): the reason the corpse is owed, or null to fetch it now. Each answer is one line.</summary>
    private string? DeferCorpseRule(IGameSessionView session, DeathFacts facts, double now)
    {
        string? Fetch(string why)
        {
            _log.Info(LogCategory, "vitae burn: not deferred, the corpse is fetched now: " + why);
            return null;
        }

        if (!LadderClimb)
        {
            return Fetch("this run is not the ladder's own climb");
        }

        var q = session.State.Self.Qualities;
        var vitae = EffectRules.VitaeValue(q);
        if (vitae >= 1f)
        {
            return Fetch("she carries no vitae");
        }

        if (!q.TryGetInt(PlayerQualities.LevelProperty, out var level) || level < 1)
        {
            return Fetch("her level is not known");
        }

        if (facts.CorpseGuid == 0 || facts.CorpsePosition is not { } at || double.IsNaN(facts.DiedAt))
        {
            return Fetch("no corpse is remembered (its guid, place or time)");
        }

        var ladder = _ladder ??= LadderLoader.WarMage;
        if (_rung is not { } rung || !(string.Equals(rung.GroundKey, _ground.Key, StringComparison.Ordinal) || rung.Alternates.Any(a => string.Equals(a.GroundKey, _ground.Key, StringComparison.Ordinal))))
        {
            return Fetch($"the run's ground {_ground.Key} is not its rung's ({_rung?.Key ?? "none"}): a trial's or a kill task's ground");
        }

        var index = IndexOfRung(ladder, rung.Key);
        var below = index <= 0 || rung.Band.From <= 1 ? null : ladder.Rungs.Take(index).LastOrDefault(r => r.GroundKey is { } g && !string.Equals(g, _ground.Key, StringComparison.Ordinal) && GroundCatalog.TryGet(g) is not null);
        if (below is null)
        {
            return Fetch($"no hunting ground is on a rung below {rung.Key}");
        }

        var fetchBy = VitaeBurn.FetchBy(facts.DiedAt, level);
        if (now >= fetchBy)
        {
            return Fetch(string.Create(CultureInfo.InvariantCulture, $"the corpse's rot is near: {VitaeBurn.RotSeconds(level):0} s at level {level} (ACE Corpse.cs 110), {now - facts.DiedAt:0} s gone, the burn gives way {VitaeBurn.RotMarginSeconds:0} s before it"));
        }

        if (Burn.Owed is { } older)
        {
            _log.Warn(LogCategory, $"vitae burn: a newer death replaces the owed corpse 0x{older.Tally.LastCorpseGuid:X8} on {older.GroundKey}: it is not gone back for (TODO(m5-death-older-corpse))");
        }

        Burn.Defer(new VitaeOwed(_deathTally!, at.CellId, _ground.Key, rung.Key, level, facts.DiedAt));
        _vitaeDeferred = true;
        _vitaeOverLogged = false;
        var why = string.Create(CultureInfo.InvariantCulture, $"the vitae is {EffectRules.VitaePercentage(vitae)}% at level {level}: the rung {rung.Key} and {_ground.Key} are held and she burns it on the rung below ({below.Key}, {below.GroundKey}) with the gear she has; the corpse 0x{facts.CorpseGuid:X8} in cell 0x{at.CellId:X8} is fetched when the vitae is gone, or by {fetchBy:0} s on the session clock ({fetchBy - now:0} s from now: ACE's rot time {VitaeBurn.RotSeconds(level):0} s at level {level}, Corpse.cs 110, less {VitaeBurn.RotMarginSeconds:0} s)");
        _log.Info(LogCategory, "vitae burn: deferred: " + why);
        return why;
    }

    private static int IndexOfRung(Ladder ladder, string key)
    {
        for (var i = 0; i < ladder.Rungs.Count; i++)
        {
            if (string.Equals(ladder.Rungs[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>At the death station's end: a deferred corpse's tally goes to the burn (no longer listening), and the run keeps a new tally of its own.</summary>
    private void VitaeBurnAfterDeath()
    {
        if (!_vitaeDeferred)
        {
            return;
        }

        _vitaeDeferred = false;
        if (_session is not { } session || Burn.Owed is not { } owed)
        {
            return;
        }

        owed.Tally.Dispose(); // the corpse's facts stay; a later death is the new tally's
        _deathTally = new DeathTally();
        _deathTally.Start(session);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae burn: the death station ended with the corpse owed (0x{owed.Tally.LastCorpseGuid:X8} on {owed.GroundKey}); the cycle's check holds {owed.RungKey} and takes the rung below"));
    }

    /// <summary>True when the burn is over (the vitae gone, or the rot near) and no hostile is in reach: the fight ends with <see cref="VitaeBurnt"/>. The wait for a lull is logged once.</summary>
    private bool VitaeBurnOver(IGameSessionView session)
    {
        if (!Burn.Burning || Burn.DueWhy(EffectRules.VitaeValue(session.State.Self.Qualities), _clock) is not { } why)
        {
            return false;
        }

        if (_hostiles is { } watch && watch.InReach(_clock, out var what))
        {
            if (!_vitaeOverLogged)
            {
                _vitaeOverLogged = true;
                _log.Info(LogCategory, $"vitae burn: over ({why}): the fight ends in the next lull ({what})");
            }

            return false;
        }

        _log.Info(LogCategory, $"vitae burn: over ({why}) and no hostile is in reach: the fight ends and the cycle's check takes her rung again");
        return true;
    }

    /// <summary>
    /// At the cycle's check, before the rung is asked: an owed corpse past its rot is forgotten by name; a burn that is over is released (the check then takes her rung again); a released corpse on the run's ground is taken and the death
    /// station entered again for it. Null: the check goes on.
    /// </summary>
    private ScenarioStatus? VitaeBurnCheck(IGameSessionView session)
    {
        if (Burn.Owed is not { } owed)
        {
            return null;
        }

        if (_clock >= owed.RotAt)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae burn: the owed corpse 0x{owed.Tally.LastCorpseGuid:X8} on {owed.GroundKey} is past ACE's rot time ({VitaeBurn.RotSeconds(owed.LevelAtDeath):0} s at level {owed.LevelAtDeath}, Corpse.cs 110; {_clock - owed.DiedAt:0} s gone): corpse_recovered=rotted, it is not gone back for"));
            Burn.Forget();
            return null;
        }

        if (!owed.Released && Burn.DueWhy(EffectRules.VitaeValue(session.State.Self.Qualities), _clock) is { } why)
        {
            Burn.Release();
            _log.Info(LogCategory, $"vitae burn: released ({why}): {owed.RungKey} and {owed.GroundKey} are held no more; the corpse is fetched by the run on {owed.GroundKey}");
        }

        return VitaeBurnTake("the cycle's check") ? ResumeDeath() : null;
    }

    /// <summary>A released corpse whose ground is the run's: the tally is taken and the death is pending again, so the known state enters the death station for it (the cut station's way). False when none is taken.</summary>
    private bool VitaeBurnTake(string where)
    {
        if (Burn.Owed is not { Released: true } owed || Burn.TakeFor(cell => string.Equals(owed.GroundKey, _ground.Key, StringComparison.Ordinal) || _ground.InLandblocks(cell)) is not { } tally)
        {
            return false;
        }

        _deathTally?.Dispose();
        _deathTally = tally;
        _deathPending = true;
        _vitaeFetch = true; // the death was counted when the corpse was deferred: the fetch's station end does not count it again
        _context.Facts.CorpseRotLevel = owed.LevelAtDeath; // the rot's level, and the mark that the station enters again from where the body stands
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae burn: {where}: the run is on {_ground.Key}: the owed corpse 0x{tally.LastCorpseGuid:X8} in cell 0x{owed.CorpseCell:X8} is fetched now ({_clock - owed.DiedAt:0} s after the death; ACE's rot time {VitaeBurn.RotSeconds(owed.LevelAtDeath):0} s)"));
        return true;
    }
}
