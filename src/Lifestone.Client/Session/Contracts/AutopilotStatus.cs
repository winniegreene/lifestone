namespace Lifestone.Client.Session.Contracts;

/// <summary>Where the dial is (M5 item 4 h2, D20; DoD 9).</summary>
public enum AutopilotMode
{
    /// <summary>Never started, or switched off and finished: the key starts it.</summary>
    Off,

    /// <summary>The loop is stepping.</summary>
    Running,

    /// <summary>A stop was asked (the key, the button): the loop ends at its next safe point and writes its lines.</summary>
    Stopping,

    /// <summary>A person pressed a movement key: the loop is paused, the keys released; the key hands the controls back.</summary>
    Interrupted,

    /// <summary>The kill switch was thrown: the loop is paused and sends nothing; the key hands back once the switch is off.</summary>
    Halted,

    /// <summary>The autopilot ended by itself (a stop rule, a failure, a stop): its done line is written; the key starts a new run.</summary>
    Done,
}

/// <summary>What the inspector shows of the autopilot: its mode, state, last decision and next trigger (engine-free text).</summary>
/// <param name="Mode">The dial's mode.</param>
/// <param name="State">The state machine's stage (<c>fight:den-fight</c>), or empty when off.</param>
/// <param name="LastDecision">Why the last arrow was taken, or what the person or the switch did.</param>
/// <param name="NextTrigger">The nearest thing that will change the state (<c>trip at 200 tapers: 412 held</c>, <c>stop at 30 min: 12 left</c>); empty when none is known.</param>
/// <param name="Cycle">The cycle in progress.</param>
/// <param name="Interrupted">How many times a person took the controls this run.</param>
/// <param name="Resumed">How many times the controls were handed back.</param>
public readonly record struct AutopilotStatus(AutopilotMode Mode, string State, string LastDecision, string NextTrigger, int Cycle, int Interrupted, int Resumed);

/// <summary>Something that reports the autopilot's status and says when it changed (the inspector reads it).</summary>
public interface IAutopilotStatusSource
{
    /// <summary>The status now.</summary>
    AutopilotStatus Status { get; }

    /// <summary>Raised when the mode, the state or a decision changed (and now and then while running, for the trigger's counts).</summary>
    event Action? Changed;
}
