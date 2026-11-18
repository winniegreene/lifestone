using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Autopilot.Commands;

/// <summary>What the command runner reads and asks of the dial. Every act is a call the dial already has (a stop, a hand-back, a re-seat, a task, an order to the scenario); nothing here sends to the server.</summary>
public interface ICommandDial
{
    /// <summary>The dial's mode.</summary>
    AutopilotMode Mode { get; }

    /// <summary>True while a person's stop holds the dial.</summary>
    bool PersonHold { get; }

    /// <summary>True while an order paused the dial.</summary>
    bool InterruptedByOrder { get; }

    /// <summary>True while the kill switch is on.</summary>
    bool KillSwitch { get; }

    /// <summary>The scenario running, or null.</summary>
    AutopilotScenario? Scenario { get; }

    /// <summary>The task the dial runs.</summary>
    AutopilotTask Task { get; }

    /// <summary>The re-seats made.</summary>
    int Seats { get; }

    /// <summary>The session clock, in seconds.</summary>
    double Now { get; }

    /// <summary>The character's level (0 before it is read).</summary>
    int Level { get; }

    /// <summary>The dial's last decision, in words.</summary>
    string LastDecision { get; }

    /// <summary>Chooses the task the next start or hand-back runs; the refusal in words, or null.</summary>
    string? UseTask(AutopilotTask task);

    /// <summary>Starts the dial from off or done, or hands it back from a pause: a person's press as their /auto, else the desk's. Returns the line.</summary>
    string Start(bool person);

    /// <summary>A person's stop where she stands (their /stop). Returns the line.</summary>
    string StopHere(string via);

    /// <summary>An order's pause: stops where she stands, no hold; an order may resume. False when not running.</summary>
    bool PauseByOrder(string via);

    /// <summary>A clean stop at the next safe point that recalls home.</summary>
    void StopHome(string via);

    /// <summary>Asks a re-seat with <paramref name="task"/>; the refusal's token, or null.</summary>
    string? AskSeat(AutopilotTask task, string why);
}

/// <summary>The dial as the runner sees it: <see cref="AutopilotHostLogic"/> over the session, with the host's own press and stop (the play host's <c>/auto</c> and <c>/stop</c>).</summary>
/// <param name="logic">The dial.</param>
/// <param name="session">The session now, or null.</param>
/// <param name="start">The host's press: true for a person's (/auto), false for the desk's. Returns the line.</param>
/// <param name="stopHere">The host's /stop. Returns the line.</param>
public sealed class DialCommandTarget(AutopilotHostLogic logic, Func<IGameSessionView?> session, Func<bool, string> start, Func<string, string> stopHere) : ICommandDial
{
    private readonly AutopilotHostLogic _logic = logic ?? throw new ArgumentNullException(nameof(logic));
    private readonly Func<IGameSessionView?> _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<bool, string> _start = start ?? throw new ArgumentNullException(nameof(start));
    private readonly Func<string, string> _stopHere = stopHere ?? throw new ArgumentNullException(nameof(stopHere));

    /// <inheritdoc/>
    public AutopilotMode Mode => _logic.Mode;

    /// <inheritdoc/>
    public bool PersonHold => _logic.PersonHold;

    /// <inheritdoc/>
    public bool InterruptedByOrder => _logic.InterruptedByOrder;

    /// <inheritdoc/>
    public bool KillSwitch => _session()?.Actions.KillSwitch == true || _logic.Mode == AutopilotMode.Halted;

    /// <inheritdoc/>
    public AutopilotScenario? Scenario => _logic.Scenario;

    /// <inheritdoc/>
    public AutopilotTask Task => _logic.Task;

    /// <inheritdoc/>
    public int Seats => _logic.Seats;

    /// <inheritdoc/>
    public double Now => _session()?.Clock.Now ?? 0;

    /// <inheritdoc/>
    public int Level => _session() is { } s ? AutopilotHostLogic.LevelOf(s) : 0;

    /// <inheritdoc/>
    public string LastDecision => _logic.Status.LastDecision;

    /// <inheritdoc/>
    public string? UseTask(AutopilotTask task) => _logic.UseTask(task, "order");

    /// <inheritdoc/>
    public string Start(bool person)
    {
        if (!person && _logic.Mode == AutopilotMode.Interrupted && _logic.InterruptedByOrder && _session() is { } s)
        {
            return _logic.HandBack(s) ? "the autopilot has the controls again" : "the hand-back was refused (" + _logic.Status.LastDecision + ")";
        }

        return _start(person);
    }

    /// <inheritdoc/>
    public string StopHere(string via) => _stopHere(via);

    /// <inheritdoc/>
    public bool PauseByOrder(string via) => _session() is { } s && _logic.StopHereByOrder(s, via);

    /// <inheritdoc/>
    public void StopHome(string via) => _logic.Stop(via);

    /// <inheritdoc/>
    public string? AskSeat(AutopilotTask task, string why) => _logic.AskSeat(task, why);
}
