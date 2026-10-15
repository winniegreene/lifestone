using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session;

/// <summary>
/// The session layer's loud stubs (A0's R7; CLAUDE.md "Loud stubs"): a member whose step has not landed throws
/// <see cref="NotSupportedException"/> with a <c>TODO(state)</c> message naming the step, and logs it as an ERROR when a
/// sink is at hand. Nothing fakes success.
/// </summary>
internal static class SessionTodo
{
    /// <summary>The log category of the session layer.</summary>
    internal const string Category = SessionLog.Category;

    /// <summary>
    /// The exception for a member that step <paramref name="step"/> (an m2-plan state step id, e.g. <c>ST13</c>) builds,
    /// after logging it to <paramref name="log"/> when one is given.
    /// </summary>
    /// <param name="step">The step that builds it.</param>
    /// <param name="what">What is missing, e.g. <c>GameSession.Tick</c>.</param>
    /// <param name="log">Where to log the ERROR line, or null.</param>
    internal static NotSupportedException NotBuilt(string step, string what, ILogSink? log = null)
    {
        var message = $"TODO(state): {what} is not built yet ({step}).";
        log?.Error(Category, message);
        return new NotSupportedException(message);
    }
}
