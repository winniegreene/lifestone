namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// How an action ended (<see cref="ActionHandle.Outcome"/>). An action is never reported done because it was queued or
/// sent: it ends on the server's confirmation, the server's error, a timeout, or a refusal (A0's §1.3).
/// </summary>
/// <param name="Kind">The kind of ending.</param>
/// <param name="Code">The server's code for <see cref="ActionOutcomeKind.ServerError"/> (a CharGenResult, a CharacterError or a
/// WeenieError), else 0.</param>
/// <param name="Detail">One sentence for the log and the screen (why it was refused, the TODO it hit, what the server
/// said), or null. Never holds a secret.</param>
public readonly record struct ActionOutcome(ActionOutcomeKind Kind, uint Code = 0, string? Detail = null)
{
    /// <summary>True for the endings that mean the action did what was asked: confirmed, local or sent.</summary>
    public bool Succeeded => Kind is ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local or ActionOutcomeKind.Sent;
}

/// <summary>The kinds of <see cref="ActionOutcome"/>.</summary>
public enum ActionOutcomeKind
{
    /// <summary>The server confirmed it (the action's confirmation arrived: a message, an echo, a state change).</summary>
    Confirmed,

    /// <summary>Done without a server round trip, because none exists (selecting a character).</summary>
    Local,

    /// <summary>Sent, and the protocol has no reply to wait for (the Disconnect that ends a quit).</summary>
    Sent,

    /// <summary>The server answered with an error; <see cref="ActionOutcome.Code"/> holds its code.</summary>
    ServerError,

    /// <summary>No confirmation within the action's timeout (on the session clock).</summary>
    TimedOut,

    /// <summary>
    /// Not sent: a precondition failed (wrong phase, invalid arguments, an offline session, a non-loopback server, the
    /// kill switch). <see cref="ActionOutcome.Detail"/> says which.
    /// </summary>
    Refused,

    /// <summary>The action is not built yet: a loud stub (A0's R7), logged as an ERROR with its <c>TODO(state)</c>.</summary>
    NotImplemented,

    /// <summary>Withdrawn before it completed: the kill switch cancelled a queued automation action, or the session ended.</summary>
    Cancelled,

    /// <summary>An automation action under dry run: logged with what it would have sent, never sent.</summary>
    DryRun,
}
