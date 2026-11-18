using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 item 0 a5 (D33, the mage's outdoor fight): on an outdoor ground the pick's targets are the profile's <c>outdoor.creatures</c> within its <c>level_band</c> (<see cref="FightTargets.Of"/>), and the band follows the character:
/// a creature above the character's level + the rung's margin (<see cref="FightOptions.TargetMargin"/>, review 0-R5) is skipped by the pick, logged with its reason (the band once per level, each skipped creature in range once). An attacker is always fought back,
/// whatever its level (M5 D88: the self-defence names its attacker from the notices, not from the band). A dungeon's pick is today's (<see cref="_targetWcids"/>). Also the casts a kill on the kill line and the verdict's
/// <c>casts= walk_s=</c> (the <c>[ladder]</c> line's casts a kill, a4's field).
/// </summary>
public sealed partial class FightScenario
{
    private readonly IReadOnlyList<GroundTarget> _targets;
    private int _bandLevel = -1;
    private IReadOnlyList<uint> _bandWcids = [];
    private IReadOnlyList<GroundTarget> _bandSkipped = [];
    private readonly HashSet<uint> _bandNoted = [];
    private bool _bandLevelUnknownNoted;
    private bool _bandMarginUnknownNoted;
    private int _castsAtKill;

    /// <summary>The creatures the pick skips now (above the character's band), empty on a dungeon or before the first pick.</summary>
    public IReadOnlyList<GroundTarget> BandSkipped => _bandSkipped;

    /// <summary>The weenie class ids the pick takes from <paramref name="here"/>: a dungeon's targets as today; an outdoor ground's within the character's band.</summary>
    private IReadOnlyList<uint> PickWcids(IGameSessionView session, in CellFrame here)
    {
        if (!_ground.IsOutdoorFight())
        {
            return _targetWcids;
        }

        if (!session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) || level <= 0)
        {
            if (!_bandLevelUnknownNoted)
            {
                _bandLevelUnknownNoted = true;
                _log.Warn(LogCategory, $"targets: the character's level is not known, so the band cannot follow it (TODO(m6-outdoor-band-level)); the pick takes the ground's band {_ground.LevelBand.Min}-{_ground.LevelBand.Max}");
            }

            return _targetWcids;
        }

        if (_options.TargetMargin is not { } margin)
        {
            if (!_bandMarginUnknownNoted)
            {
                _bandMarginUnknownNoted = true;
                _log.Warn(LogCategory, $"targets: the fight was given no target margin (TODO(m6-fight-no-target-margin): only the level up job passes its rung's); the pick is not banded by the character's level and takes the ground's band {_ground.LevelBand.Min}-{_ground.LevelBand.Max}");
            }

            return _targetWcids;
        }

        if (level != _bandLevel)
        {
            _bandLevel = level;
            (_bandWcids, _bandSkipped) = FightTargets.Band(_targets, level, margin);
            _bandNoted.Clear();
            var top = Math.Min(_ground.LevelBand.Max, level + margin);
            _log.Info(LogCategory, string.Create(
                CultureInfo.InvariantCulture,
                $"targets: the band follows the character: levels {_ground.LevelBand.Min}-{top} of the ground's {_ground.LevelBand.Min}-{_ground.LevelBand.Max} (level {level} + {margin}); {_bandWcids.Count} of {_targets.Count} creatures kept{(_bandSkipped.Count == 0 ? string.Empty : "; skipped above the band: " + string.Join(", ", _bandSkipped.Select(t => t.Name + " (" + t.Level.ToString(CultureInfo.InvariantCulture) + ")")) + "; an attacker of theirs is still fought back (D88)")}"));
        }

        if (_bandSkipped.Count > 0)
        {
            NoteSkippedInRange(session, in here, level, margin);
        }

        return _bandWcids;
    }

    /// <summary>Logs, once per creature, a living creature of the skipped band within the cast range: why the pick passes it by.</summary>
    private void NoteSkippedInRange(IGameSessionView session, in CellFrame here, int level, int margin)
    {
        var centre = here.ToGlobal();
        foreach (var o in session.State.Objects.All)
        {
            if (StandsAt(session, o) is not { } p || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _bandNoted.Contains(o.Guid) || SkippedTarget(o.Weenie.WeenieClassId) is not { } skipped)
            {
                continue;
            }

            var metres = p.ToGlobal().DistanceTo(centre);
            if (metres > _range)
            {
                continue;
            }

            _bandNoted.Add(o.Guid);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"targets: skipped {skipped.Name} 0x{o.Guid:X8} (level {skipped.Level}) {metres:0.#} m away: above the band's top {level + margin} (level {level} + {margin}); fought back only if it attacks (D88)"));
        }
    }

    private GroundTarget? SkippedTarget(uint wcid)
    {
        for (var i = 0; i < _bandSkipped.Count; i++)
        {
            if (_bandSkipped[i].Wcid == wcid)
            {
                return _bandSkipped[i];
            }
        }

        return null;
    }

    /// <summary>The kill notice's line (every 0x01AD kill of the mage's, whichever phase it lands in): <c>kill n: casts=k (text)</c>, the casts sent since the kill before (or the fight's start).</summary>
    private void NoteKillCasts(string text)
    {
        var casts = _tally.Casts - _castsAtKill;
        _castsAtKill = _tally.Casts;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"kill {_tally.Kills}: casts={(casts < 0 ? _tally.Casts : casts)} ({text})"));
    }

    /// <summary>
    /// The verdict's <c>casts= walk_s=</c> (M6 item 0 a5): the casts the fight sent (the <c>[ladder]</c> line's casts a kill reads it with <c>kills=</c>) and the walk's walking seconds, a cancelled leg's included
    /// (<see cref="IFightWalk.WalkSecondsTotal"/>, <c>m6-walk-cancel-seconds</c>; <c>n/a</c> for a walk that does not count them).
    /// </summary>
    public string CastWalkFields() => string.Create(CultureInfo.InvariantCulture, $"casts={_tally.Casts} walk_s={(_walk?.WalkSecondsTotal is { } walked ? walked.ToString("0", CultureInfo.InvariantCulture) : "n/a")}");
}
