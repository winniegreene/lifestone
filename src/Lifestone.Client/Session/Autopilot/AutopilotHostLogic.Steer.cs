using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The dial's side of the steer desk (the steer contract, sections 5 to 7): the person hold, a stop made by an order, the way to leave, the claim on a run's end and the re-seat. None of it runs without the desk, except the hold, which only records.
/// </summary>
public sealed partial class AutopilotHostLogic
{
    /// <summary>The fewest seconds between two re-seats (the contract's <c>seat-rate</c>, a first guess): a seat runs the whole start audit.</summary>
    public const double SeatFloorSeconds = 60;

    private (AutopilotTask Task, string Why)? _seat;
    private double _lastSeat = double.NegativeInfinity;
    private bool _leaving;

    /// <summary>True from any person's stop (a typed /stop, the panel's button, the menu's stop row), in any mode, until a person's own press: no order may start or resume the dial.</summary>
    public bool PersonHold { get; private set; }

    /// <summary>True while the dial is interrupted by an order (<see cref="StopHereByOrder"/>), so an order may resume it; a person's interruption never sets it.</summary>
    public bool InterruptedByOrder { get; private set; }

    /// <summary>Asked when a run has ended by itself, before the unattended end is decided: true claims the end, so the game does not quit for it and <see cref="Stopped"/> is not raised. Null: every end is as before.</summary>
    public Func<AutopilotScenario, bool>? AfterRun { get; set; }

    /// <summary>How many re-seats the dial has made.</summary>
    public int Seats { get; private set; }

    /// <summary>True while a re-seat is waiting for its safe point.</summary>
    public bool SeatAsked => _seat is not null;

    /// <summary>A person's own press (/auto, the panel's button, the menu's rows): the only thing that clears <see cref="PersonHold"/>.</summary>
    public void PersonPressed() => PersonHold = false;

    /// <summary>A person's stop: the hold, and whatever an order had under way gives way to it: a way out in progress, and a hand-back an order is waiting to make.</summary>
    private void PersonStopped()
    {
        PersonHold = true;
        if (_leaving)
        {
            _leaving = false;
            if (_stopAsked == StopFileReason)
            {
                _stopAsked = null;
            }
        }

        if (InterruptedByOrder)
        {
            (_handBackWaiting, _handBackWhy) = (false, null);
        }
    }

    /// <summary>Stops where it stands, as /stop does, for an order: no hold is recorded, and an order may resume it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="via">Who asked, for the log.</param>
    /// <returns>False when the dial was not running or stopping.</returns>
    public bool StopHereByOrder(IGameSessionView session, string via)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) || _scenario is not { } scenario)
        {
            return false;
        }

        _quietUnlock = via;
        Interrupt(scenario, ChatStopReason, via);
        _quietUnlock = null;
        InterruptedByOrder = true;
        return true;
    }

    /// <summary>
    /// Leaves the game (to yield to a gate): while running, the stop file's path, which recalls home, logs off and quits, whether or not the run is unattended; while off or done, logs off where the character stands and quits. A repeat changes nothing.
    /// </summary>
    /// <param name="why">Who asked, for the log.</param>
    /// <returns>Null when asked; the mode's name when the dial cannot leave from it.</returns>
    public string? Leave(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        switch (_mode)
        {
            case AutopilotMode.Running:
                _leaving = true;
                _log.Info(LogCategory, $"autopilot host: asked to leave ({why}): the run stops at its next safe point, recalls home, and the game logs off and quits");
                Stop(StopFileReason);
                return null;
            case AutopilotMode.Off or AutopilotMode.Done:
                if (_unattendedEnd is null)
                {
                    _unattendedEnd = new AutopilotUnattendedEnd(0, $"autopilot: asked to leave ({why}) while {_mode.ToString().ToLowerInvariant()}: the game logs off where the character stands and quits with exit code 0");
                    _log.Info(LogCategory, _unattendedEnd.Decision);
                    Raise();
                }

                return null;
            default:
                return _mode.ToString().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Asks for a re-seat: at the next safe point the run in flight is dropped and a fresh scenario takes over on the same ground with the task given, like /stop then /auto in one frame, except that it never counts as a person, never unlocks the keys and carries
    /// the run's guards (<see cref="AutopilotSeatCarry"/>).
    /// </summary>
    /// <param name="task">The task the fresh scenario runs.</param>
    /// <param name="why">Why, for the log.</param>
    /// <returns>Null when asked; else the refusal's token.</returns>
    public string? AskSeat(AutopilotTask task, string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        if (_mode != AutopilotMode.Running || _scenario is not { } scenario)
        {
            return "not-running";
        }

        if (_session is { } session && session.Clock.Now - _lastSeat < SeatFloorSeconds)
        {
            return "seat-rate";
        }

        _seat = (task, why);
        scenario.AskSeat();
        _log.Info(LogCategory, $"autopilot host: a re-seat is asked ({why}): it waits for the next safe point");
        return null;
    }

    /// <summary>Straight after a step: makes the re-seat when the scenario says this is its safe point. A failure to build the fresh scenario leaves here as a host fault, the existing fault exit.</summary>
    private bool SeatIfReady(IGameSessionView session, AutopilotScenario old)
    {
        if (_seat is not { } seat || _mode != AutopilotMode.Running || !old.SeatReady)
        {
            return false; // a run already stopping is never cut: its stop stands
        }

        var why = JourneyToken(seat.Why);
        Drop(old, "seat-" + why);
        StandDown("seat");
        _standDownCheck = false; // the dial keeps the controls: the check is for a stand-down that hands them over
        var seed = (_abandonedSeed ?? old.Seed) with { Seat = old.SeatCarry };
        var carry = new AutopilotCarry(old.Cycles, old.Totals, old.CycleTally, seed.SecondsUsed, _forcedTripDone, _testDeathDone, seed, seat.Task);
        (_abandonedSeed, _task, _lastSeat) = (null, seat.Task, session.Clock.Now);
        Install(_create(carry), ResumeReason);
        LogTaskStart("re-seat");
        Seats++;
        _decision = $"re-seated ({why}): the loop reads the state it finds (cycle {_scenario!.Cycle})";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot host: {_decision}; seats={Seats} interrupted={Interrupted} resumed={Resumed}"));
        Raise();
        return true;
    }

    private static string JourneyToken(string text) => Scenarios.Journey.JourneyVerdict.Token(text);

    private void ResetSteer()
    {
        InterruptedByOrder = false;
        _seat = null;
    }
}
