using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The host's control of the session, which the autopilot cannot reach through <see cref="Lifestone.Client.Session.Contracts.IGameSessionView"/> (M5 item 4 a2, D57; the spec's DoD 8 and risk 4). When the session leaves the world
/// (a failure, a boot, the 20 s silence watchdog: <c>m5-reconnect-auto</c>) the autopilot calls <see cref="Reconnect"/> and then steps <see cref="StepLogin"/> every frame until it succeeds or fails, in a bounded retry with growing waits
/// (<see cref="AutopilotOptions.ReconnectWaits"/>). The hosts implement it (slice h1: <c>GameSession.Reconnect</c> and a <see cref="LoginScenario"/> or equivalent that stops once the character is in the world, never one that logs off
/// at its end); the tests use a double.
/// <para><c>m5-reconnect-auto</c> (closed): the 20 s silence watchdog (<c>SessionTimeouts.Silence</c>, D18; retail 140 s via <c>--retail-net</c>) stays. It does not break the reconnect: it is what turns a dead link into the failed phase the autopilot
/// reacts to within 20 s instead of 140 s, and every try starts a new network session with its own silence timer. ACE drops the client it still holds after 60 s of silence (<c>NetworkSession.cs:336-338</c> @95ef426d), so the retry's waits
/// sum to 230 s: the early tries may meet the old session, the later ones do not.</para>
/// </summary>
public interface IAutopilotSession
{
    /// <summary>
    /// Starts one try: a new network session for the same account (<c>GameSession.Reconnect</c>: pending actions cancelled, the state reset to idle, the bus and its subscribers kept) and a fresh login of the character, which
    /// <see cref="StepLogin"/> then steps. Called on the game thread, once per try.
    /// </summary>
    void Reconnect();

    /// <summary>
    /// Steps the login begun by <see cref="Reconnect"/>: <see cref="ScenarioStatus.Running"/> while it goes on, <see cref="ScenarioStatus.Succeeded"/> once the character is in the world (the session's phase in the world and the player's own
    /// object known), <see cref="ScenarioStatus.Failed"/> when it cannot (then <see cref="LoginFailure"/> says why). The autopilot also fails a try that takes over <see cref="AutopilotScenario.ReconnectLoginSeconds"/> of the session clock.
    /// </summary>
    /// <param name="now">The session clock's seconds.</param>
    ScenarioStatus StepLogin(double now);

    /// <summary>Why the last login failed (a short phrase for the log), or null.</summary>
    string? LoginFailure { get; }

    /// <summary>M6 14 body: how the last login ended in the world when it did not enter itself (another owner's entry it adopted), or null. The reconnect's success line carries it.</summary>
    string? LoginNote => null;
}
