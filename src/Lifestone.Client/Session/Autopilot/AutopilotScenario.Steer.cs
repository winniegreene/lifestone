using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What a re-seat carries that a hand-back does not, so a seat never looks like a new run (the steer contract, section 5).</summary>
/// <param name="LadderChecked">The rung was already checked: a fresh scenario's first check treats a due move-on as <c>failed:rung-ground-mismatch</c>.</param>
/// <param name="LadderSpentLevel">The level the experience was last spent for: each seat would spend again.</param>
/// <param name="LadderSpendOwed">A spend that failed or was cut is owed to the next cycle's start.</param>
/// <param name="PassBook">The spellbook's size when the last whole pass ended: without it the known state runs a whole start pass again.</param>
/// <param name="PassHomeWalked">She walked home for a pass that has not run yet.</param>
/// <param name="RetreatCycle">The cycle of the retreats counted.</param>
/// <param name="RetreatGround">The ground of the retreats counted.</param>
/// <param name="RetreatsHere">The retreats counted, for <c>retreat:again:&lt;ground&gt;</c>.</param>
/// <param name="WatchdogRecoveries">The watchdog recoveries this cycle.</param>
/// <param name="TripLoop">The trip loop guard's state.</param>
/// <param name="QuestFailed">The quests whose turn-in failed this run.</param>
/// <param name="BuyTripTried">The buy-trip was queued once this run.</param>
public sealed record AutopilotSeatCarry(
    bool LadderChecked,
    int LadderSpentLevel,
    bool LadderSpendOwed,
    int? PassBook,
    bool PassHomeWalked,
    int RetreatCycle,
    string? RetreatGround,
    int RetreatsHere,
    int WatchdogRecoveries,
    (int Idle, long HoldUntilKills, int Holds) TripLoop,
    IReadOnlyDictionary<string, string> QuestFailed,
    bool BuyTripTried);

/// <summary>The scenario's side of a re-seat: the two safe points, and what the fresh scenario must carry.</summary>
public sealed partial class AutopilotScenario
{
    private bool _seatAsked;
    private double _seatPointAt = double.NaN; // the frame a safe point was reached: a later frame must reach its own

    /// <summary>Asks for a re-seat at the next safe point.</summary>
    public void AskSeat() => _seatAsked = true;

    /// <summary>True when this frame reached a safe point and nothing has since made it unsafe: the dial re-seats straight after the step.</summary>
    public bool SeatReady => _seatPointAt == _clock && _seatAsked && _end == ScenarioStatus.Running && !_tripPending && !_deathPending && _ladderSpend is null && _state is AutopilotState.KnownState or AutopilotState.Fight;

    /// <summary>True when someone asked this run to stop: a stop that was asked must not be outrun by a move-on, which would restart the run and lose it.</summary>
    private bool StopRequested => _stopAsked?.StartsWith("request:", StringComparison.Ordinal) == true;

    /// <summary>What a fresh scenario must carry.</summary>
    public AutopilotSeatCarry SeatCarry => new(
        _ladderChecked,
        _ladderSpentLevel,
        _ladderSpendOwed,
        _passBook,
        _passHomeWalked,
        _retreatCycle,
        _retreatGround,
        _retreatsHere,
        _watchdogs.Recoveries,
        _tripLoop.State,
        new Dictionary<string, string>(_questFailed, StringComparer.Ordinal),
        _buyTripTried);

    private void ApplySeat(AutopilotSeatCarry carry)
    {
        (_ladderChecked, _ladderSpentLevel, _ladderSpendOwed) = (carry.LadderChecked, carry.LadderSpentLevel, carry.LadderSpendOwed);
        (_passBook, _passHomeWalked) = (carry.PassBook, carry.PassHomeWalked);
        (_retreatCycle, _retreatGround, _retreatsHere) = (carry.RetreatCycle, carry.RetreatGround, carry.RetreatsHere);
        _watchdogs.RestoreRecoveries(carry.WatchdogRecoveries);
        _tripLoop.Restore(carry.TripLoop);
        foreach (var (key, stop) in carry.QuestFailed)
        {
            _questFailed[key] = stop;
        }

        _buyTripTried = carry.BuyTripTried;
    }

    /// <summary>The known state's safe point: present, the audit finished, no corpse owed, no trip or spend in flight, and, as the task arrow's own guards, not dead, at home or on the ground, and no hostile in reach.</summary>
    private bool SeatAtKnownState(IGameSessionView session)
    {
        if (!_seatAsked || _stopAsked is not null || _audit is not null || _tripPending || _deathPending || _ladderSpend is not null)
        {
            return false;
        }

        if (Classify(session.State, _ground) is not (AutopilotPlace.Home or AutopilotPlace.Ground) || _hostiles?.InReach(_clock, out _) == true)
        {
            return false;
        }

        _seatPointAt = _clock;
        return true;
    }

    /// <summary>The fight's safe point, read only when nothing else asked to stop the fight: no hostile in reach, no trip, death, spend or trophy pull in flight, and no keeper's fight-back.</summary>
    private void SeatAtLull(IStoppableFight fight)
    {
        if (!_seatAsked || _stopAsked is not null || _tripPending || _deathPending || _ladderSpend is not null || _keeperBack is not null || fight is FightScenario { PullRunning: true })
        {
            return;
        }

        if (_hostiles is { } watch && !watch.InReach(_clock, out _))
        {
            _seatPointAt = _clock;
        }
    }
}
