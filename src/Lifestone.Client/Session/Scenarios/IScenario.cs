using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Where a scenario stands after a step.</summary>
public enum ScenarioStatus
{
    /// <summary>Still running: step it again next frame.</summary>
    Running,

    /// <summary>Finished as intended.</summary>
    Succeeded,

    /// <summary>Stopped on a failure (<see cref="IScenario.Failure"/> says why).</summary>
    Failed,
}

/// <summary>
/// A scripted run of a game session for a host without a player (the headless host, ST15; the login checks): it acts
/// only through <see cref="IGameSessionView"/>, as any automation does (A0), and the host steps it once per frame after
/// the session ticked and portal space stepped.
/// </summary>
/// <remarks>A scenario never blocks and never sleeps: it issues actions and looks at their handles and the state on the
/// next steps. Game thread only.</remarks>
public interface IScenario
{
    /// <summary>The scenario's token (the recording header's <c>scenario</c>, the summary's <c>scenario=</c>), e.g. <c>login</c>.</summary>
    string Name { get; }

    /// <summary>What the scenario is doing now (for the summary and a timeout's message), e.g. <c>entering</c>.</summary>
    string Stage { get; }

    /// <summary>Why the scenario failed, or null.</summary>
    string? Failure { get; }

    /// <summary>
    /// The largest distance, in metres, between the server's view of the player (the tools' "where am I") and the
    /// client's, over every such check the scenario made (<c>Tools.PositionAgreement</c>); null before the first. The
    /// summary's <c>where_max_m=</c> (m2-plan §4.14).
    /// </summary>
    double? WhereMaxMetres { get; }

    /// <summary>
    /// The client's movement messages the server never matched (physics' <c>MovementAudit</c>, ST16); a scenario that
    /// sends no movement reports 0. The summary's <c>unmatched=</c>.
    /// </summary>
    long Unmatched { get; }

    /// <summary>Runs one frame of the scenario.</summary>
    /// <param name="session">The session (ticked this frame).</param>
    /// <param name="now">The frame time (the value the session's <c>Tick</c> got).</param>
    /// <returns>Whether it goes on.</returns>
    ScenarioStatus Step(IGameSessionView session, double now);
}
