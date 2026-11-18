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
    /// <summary>The owner name this adapter holds a play's auto-enter for (M6 14 body).</summary>
    public const string EntryOwner = "the autopilot's reconnect";

    private readonly GameSession _session;
    private readonly string _character;
    private readonly Func<string, IDisposable>? _holdEntry;
    private LoginScenario? _login;
    private IDisposable? _hold;

    /// <summary>An adapter over <paramref name="session"/> that re-enters <paramref name="character"/>.</summary>
    /// <param name="session">The game session.</param>
    /// <param name="character">The character to enter again.</param>
    /// <param name="holdEntry">
    /// M6 14 body: holds the host's own auto-enter (the game's <c>--character</c> entry, <c>PlayPresentationFlow.HoldAutoEnter</c>) for each try, so the try's login is the one owner of the entry; null for a host with none (headless).
    /// </param>
    public GameSessionAutopilotSession(GameSession session, string character, Func<string, IDisposable>? holdEntry = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(character);
        _character = character;
        _holdEntry = holdEntry;
    }

    /// <inheritdoc/>
    public string? LoginNote => _login?.Adopted;

    /// <summary>How many tries began.</summary>
    public int Tries { get; private set; }

    /// <inheritdoc/>
    public string? LoginFailure { get; private set; }

    /// <inheritdoc/>
    public void Reconnect()
    {
        Tries++;
        LoginFailure = null;
        _hold?.Dispose();
        _hold = _holdEntry?.Invoke(EntryOwner); // before the new network session: its character list must find the entry held
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

        if (status != ScenarioStatus.Running)
        {
            _hold?.Dispose(); // the try is over: the host's own entry is its own again
            _hold = null;
        }

        return status;
    }
}
