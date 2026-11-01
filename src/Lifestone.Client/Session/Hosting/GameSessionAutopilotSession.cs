using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// The hosts' <see cref="IAutopilotSession"/> (M5 item 4 h3, D57): <see cref="Reconnect"/> is <see cref="GameSession.Reconnect"/> (a new network session for the same account, the bus and its subscribers kept) and a fresh
/// <see cref="LoginScenario"/> of the character with <see cref="LoginScenario.StayInWorld"/>, which <see cref="StepLogin"/> steps to the world (the login, the character choice, the entry, the arrival and the server's agreement on where the player
/// is) and which never logs off. Engine-free, so the game's play host, the <c>--login-check=autopilot</c> runner and the headless <c>--scenario=autopilot</c> share it. Game thread only.
/// </summary>
public sealed class GameSessionAutopilotSession : IAutopilotSession
{
    private readonly GameSession _session;
    private readonly string _character;
    private LoginScenario? _login;

    /// <summary>An adapter over <paramref name="session"/> that re-enters <paramref name="character"/>.</summary>
    /// <param name="session">The game session.</param>
    /// <param name="character">The character to enter again.</param>
    public GameSessionAutopilotSession(GameSession session, string character)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(character);
        _character = character;
    }

    /// <summary>How many tries began.</summary>
    public int Tries { get; private set; }

    /// <inheritdoc/>
    public string? LoginFailure { get; private set; }

    /// <inheritdoc/>
    public void Reconnect()
    {
        Tries++;
        LoginFailure = null;
        _session.Reconnect();
        _login = new LoginScenario(_character) { StayInWorld = true };
    }

    /// <inheritdoc/>
    public ScenarioStatus StepLogin(double now)
    {
        if (_login is not { } login)
        {
            LoginFailure = "StepLogin was called before Reconnect";
            return ScenarioStatus.Failed;
        }

        var status = login.Step(_session, now);
        if (status == ScenarioStatus.Failed)
        {
            LoginFailure = login.Failure ?? "the login failed without saying why";
        }

        return status;
    }
}
