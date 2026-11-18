using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 AX (the owner's decision: the Female Tusk counts as gate A's tusk, which changes D76): the pull of a wanted trophy's holder from a stand point (<see cref="PullStand"/>), one bounded stage on the plain hunt. It runs only when the pick finds no one in reach, so a tusker on the way is fought first and the fullness bar (<see cref="PullBarHolds"/>) has passed.
/// Check, act, check: walk to the stand (limited tries), read the holder by weenie and cell, else wait for its respawn by name up to the profile's limit; one try counted, the keep-out lifted for the fight, the shot at the named holder; the pack is fought at the stand by the ordinary fight; the tusk comes from the corpse by the ordinary loot with the item wanted. Every end is named: <c>pull: done</c>, or <c>pull: failed:&lt;name&gt;</c> with the keep-out back and the job not called again (<see cref="TrophyPullMemory"/>).
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The walk legs to the stand before the pull fails by name: a leg a tusker cuts counts, so three lets two fights pass on the way. Lifestone's own.</summary>
    private const int PullWalkTries = 3;

    private int _pullKillsSeen; // M6 7 lead (live 22:56 UTC: five kills on the way and three legs spent): a kill on the way starts the count again: she was fighting, not lost

    /// <summary>How near the stand she must stand: a metre, the walker's own stop; the free shot's margin is 1.2 m (the survey), so more would wake slots 5 and 7 first.</summary>
    private const double PullStandMetres = 1.0;

    /// <summary>The seconds with no attacker and no holder in view after the shot before the pull fails by name: a corpse appears within about 2 s of a kill (M5 7 D139 i). Lifestone's own.</summary>
    private const double PullLootSeconds = 30;

    private enum PullStage
    {
        None,
        Walk,
        Holder,
        Fight,
    }

    private GroundPull? _pull;
    private PullStand? _pullStand;
    private PullStage _pullStage;

    /// <summary>True while a trophy pull is under way (its walk, its wait for the holder, its fight): the steer desk's lull must not cut it.</summary>
    public bool PullRunning => _pull is not null && _pullStage != PullStage.None;
    private int _pullWalks;
    private double _pullSince = double.NaN;
    private readonly uint[] _pullWcid = new uint[1];
    private bool _pullWaitLogged;
    private Func<uint, bool>? _pullHolderCell;

    /// <summary>The ground's pull when its item is wanted and the job may still try it; called where the keep-out is set.</summary>
    private void BeginPull(IGameSessionView session, GroundPull pull)
    {
        var memory = TrophyPullMemory.For(session.State);
        var stand = pull.Stand!;
        if (!memory.MayTry(stand.Quest, stand.Tries))
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pull: {pull.Name}: not tried: {memory.FailureOf(stand.Quest) ?? "tries spent"} ({memory.TriesOf(stand.Quest)} of {stand.Tries})"));
            return;
        }

        (_pull, _pullStand, _pullStage, _pullWalks, _pullSince, _pullWaitLogged) = (pull, stand, PullStage.Walk, 0, double.NaN, false);
        _pullKillsSeen = _tally.Kills;
        _pullWcid[0] = stand.HolderWcid;
        _pullHolderCell = c => c == stand.HolderCell;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pull: {pull.Name}: begins: stand 0x{stand.Cell:X8} ({stand.Point.X:0.00}, {stand.Point.Y:0.00}), holder {stand.HolderWcid} in 0x{stand.HolderCell:X8}, try {memory.TriesOf(stand.Quest) + 1} of {stand.Tries}"));
    }

    /// <summary>One step of the pull, or null when no pull runs (the tour walks on). Called with nobody in reach.</summary>
    private ScenarioStatus? PullStep(IGameSessionView session, double clock, in CellFrame here, DefenceMove defence)
    {
        if (_pull is not { } pull || _pullStand is not { } stand)
        {
            return null;
        }

        if (GearInventory.CountOf(session.State, _pullScratch, pull.Item) > 0)
        {
            EndPull(session, null, "the tusk is held");
            return null;
        }

        return _pullStage switch
        {
            PullStage.Walk => PullWalk(session, clock, in here, stand),
            PullStage.Holder => PullHolder(session, clock, in here, stand, defence),
            PullStage.Fight => PullFight(session, clock, in here, defence),
            _ => null,
        };
    }

    private readonly List<uint> _pullScratch = [];

    private ScenarioStatus? PullWalk(IGameSessionView session, double clock, in CellFrame here, PullStand stand)
    {
        if (here.CellId == stand.Cell && Vector2.Distance(new Vector2(here.Origin.X, here.Origin.Y), new Vector2(stand.Point.X, stand.Point.Y)) <= PullStandMetres)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pull: check after the walk: at the stand 0x{here.CellId:X8} ({here.Origin.X:0.00}, {here.Origin.Y:0.00}) after {_pullWalks} leg(s)"));
            _pullStage = PullStage.Holder;
            _pullSince = clock;
            return ScenarioStatus.Running;
        }

        if (_tally.Kills != _pullKillsSeen)
        {
            _pullKillsSeen = _tally.Kills;
            _pullWalks = 0;
        }

        if (_pullWalks >= PullWalkTries)
        {
            EndPull(session, "stand-unreachable", string.Create(CultureInfo.InvariantCulture, $"{_pullWalks} legs ended at 0x{here.CellId:X8} ({here.Origin.X:0.00}, {here.Origin.Y:0.00})"));
            return null;
        }

        var goal = new CellFrame(stand.Cell, new AcFrame(stand.Point, here.Frame.Orientation));
        if (_walk is null || !_walk.BeginLegTo(session, clock, in goal, out var leg))
        {
            EndPull(session, "stand-no-walk", _walk?.Failure ?? "the fight has no walk");
            return null;
        }

        _pullWalks++;
        _walks++;
        NoteLegFrom(session);
        _missesInARow = 0;
        _target = 0;
        _holdingLogged = false;
        (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, "the pull's stand", 0);
        _log.Info(LogCategory, $"walking: purpose=pull (the stand for {_pull!.Name}); {leg}");
        _phase = Phase.Walk;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus? PullHolder(IGameSessionView session, double clock, in CellFrame here, PullStand stand, DefenceMove defence)
    {
        var c = CultureInfo.InvariantCulture;
        if (_killFirstPick.Nearest(session.State.Objects, _pullWcid, in here, _range, _pullHolderCell, _pickExcluded, out var holder, session.Live))
        {
            var memory = TrophyPullMemory.For(session.State);
            memory.NoteTry(stand.Quest);
            _keptOut = CellSet.Empty; // the pack's cells are fought now; the tour keeps its own keep-out, and she holds the stand
            _log.Info(LogCategory, string.Create(c, $"pull: check before the shot: holder 0x{holder:X8} in 0x{stand.HolderCell:X8}, the sweep's line {(LineIsClear(session, in here, holder) ? "clear" : "blocked")} (logged only: the sweep is noisy on these floors, TODO(m6-tusk-pull-live)), try {memory.TriesOf(stand.Quest)} of {stand.Tries}: the keep-out is lifted for the fight"));
            _pullStage = PullStage.Fight;
            _pullSince = clock;
            return BeginTarget(session, clock, in here, holder, defence, leaving: false);
        }

        var waited = clock - _pullSince;
        if (waited >= stand.WaitSeconds)
        {
            EndPull(session, "holder-absent", string.Create(c, $"no {stand.HolderWcid} in 0x{stand.HolderCell:X8} after {waited:0} s (the profile's limit {stand.WaitSeconds:0} s)"));
            return null;
        }

        if (!_pullWaitLogged)
        {
            _pullWaitLogged = true;
            _log.Info(LogCategory, string.Create(c, $"pull: the holder {stand.HolderWcid} is not in 0x{stand.HolderCell:X8}: waiting for its respawn at the stand, at most {stand.WaitSeconds:0} s"));
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus? PullFight(IGameSessionView session, double clock, in CellFrame here, DefenceMove defence)
    {
        if (_killFirstPick.Nearest(session.State.Objects, _pullWcid, in here, _range, _cellFilter, _pickExcluded, out var holder, session.Live))
        {
            _pullSince = clock;
            return BeginTarget(session, clock, in here, holder, defence, leaving: false); // a resisted shot left it asleep: the named holder again, no other
        }

        if (clock - _pullSince >= PullLootSeconds)
        {
            EndPull(session, "no-tusk", string.Create(CultureInfo.InvariantCulture, $"no attacker, no holder in view and no tusk {PullLootSeconds:0} s after the last"));
            return null;
        }

        return ScenarioStatus.Running; // she holds the stand while the corpses come and are looted
    }

    /// <summary>A walk the fight would start during the pull's fight (a bolt on the ledge, misses in a row) would take her off the stand on the tour: the pull ends by name first.</summary>
    private void PullWalkAsked(IGameSessionView session, string why)
    {
        if (_pull is not null && _pullStage == PullStage.Fight)
        {
            EndPull(session, "shot-blocked", why);
        }
    }

    /// <summary>Ends the pull: the keep-out back, the end logged, a failure remembered by name.</summary>
    private void EndPull(IGameSessionView session, string? failure, string detail)
    {
        var (pull, stand) = (_pull!, _pullStand!);
        (_pull, _pullStand, _pullStage) = (null, null, PullStage.None);
        _keptOut = _ground.Hunt?.KeepOut ?? CellSet.Empty;
        if (failure is null)
        {
            _log.Info(LogCategory, $"pull: done: {pull.Name}: {detail}; the keep-out is back");
            return;
        }

        TrophyPullMemory.For(session.State).Fail(stand.Quest, failure);
        _log.Warn(LogCategory, $"pull: failed:{failure}: {pull.Name}: {detail}; the keep-out is back, the job is not called again this session, the hunt goes on");
    }
}
