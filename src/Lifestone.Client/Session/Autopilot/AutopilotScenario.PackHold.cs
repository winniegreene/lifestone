using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>M6 7 AY: the big pack rule's seam in the autopilot (<see cref="BigPackRule"/>): the state is read back at the start, updated at each counted death and read before each ladder fight. It persists in the checkpoint (<see cref="AutopilotCheckpoint.PackHold"/>).</summary>
public sealed partial class AutopilotScenario
{
    private PackHold _packHold = PackHold.None;

    /// <summary>The big pack rule's state, for the reader and the tests.</summary>
    internal PackHold PackHoldNow
    {
        get => _packHold;
        set => _packHold = value ?? PackHold.None;
    }

    /// <summary>Read before the checkpoint's load, as the tie memory is: the rule's state is the character's, not the run's.</summary>
    private void RecallPackHold()
    {
        var hold = AutopilotCheckpoint.ReadPackHold(_checkpoints!, _context.Character, _characterGuid, out var note);
        if (note is not null)
        {
            _log.Info(LogCategory, "pack: " + note);
        }

        if (hold is not null)
        {
            _packHold = hold;
        }
    }

    /// <summary>A counted death: the creatures awake around her when she fell, read by the death tally at the victim notice, on the ground she died on.</summary>
    private void PackHoldAfterDeath()
    {
        if (_session is not { } session || _deathTally is not { } tally || _options.Job != AutopilotJob.LevelUp)
        {
            return;
        }

        var rule = (_ladder ??= LadderLoader.WarMage).BigPack;
        var ground = tally.LastCorpsePosition is { } at && _ground.InLandblocks(at.CellId) ? _ground : null;
        _packHold = BigPackRule.OnDeath(_packHold, rule, ground, tally.AwakeAtDeath(rule.ReachMetres), BigPackRule.BaseMana(session.State.Self.Qualities), out var line);
        if (line is not null)
        {
            _log.Info(LogCategory, line);
        }
    }

    /// <summary>Before a ladder fight: lifts the hold when her mana has grown, then says whether this fight holds to the first slots.</summary>
    private bool PackHoldOn(IGameSessionView session)
    {
        _packHold = BigPackRule.Lift(_packHold, (_ladder ??= LadderLoader.WarMage).BigPack, BigPackRule.BaseMana(session.State.Self.Qualities), out var line);
        if (line is not null)
        {
            _log.Info(LogCategory, line);
        }

        return _packHold.Held && string.Equals(_packHold.Ground, _ground.Key, StringComparison.Ordinal);
    }
}
