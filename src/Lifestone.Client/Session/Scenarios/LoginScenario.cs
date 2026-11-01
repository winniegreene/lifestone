using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The headless host's first scenario (step ST15; DoD 13): log in, choose (or create) a character, enter the world, wait
/// until portal space has sent LoginComplete and the server has unhidden the player, ask the server mod for its status,
/// check that the server's view of the player agrees with the client's, log off, and quit. Every step goes through the
/// Action API (<see cref="ActionSource.Host"/>) and waits for its handle.
/// </summary>
/// <remarks>
/// <para><b>Steps</b> (<see cref="Stage"/>):</para>
/// <list type="number">
/// <item><c>login</c>: <see cref="IActionApi.Login"/>, confirmed at the character list.</item>
/// <item><c>choosing</c>: the character named by <c>--character</c> (case-insensitive, as ACE compares names), else the
/// one <c>--create-character</c> names, else the first not pending deletion. A missing <c>--create-character</c> character
/// is created once (<c>creating</c>, <see cref="HeadlessCharacter.Intent"/> with the skill classes the host passed) and
/// chosen when the server confirms it.</item>
/// <item><c>entering</c>: <see cref="IActionApi.EnterWorld"/>, confirmed when the player's own object arrived.</item>
/// <item><c>arriving</c>: portal space plays the arrival and its director sends LoginComplete through the session bridge;
/// the step ends when the player is <see cref="SelfTravel.Present"/>, or fails after <see cref="ArrivalSeconds"/> on the
/// session clock.</item>
/// <item><c>status</c>: <see cref="PlayerTools.Status"/>; the account's access must be <see cref="PlayerTools.PlayerAccess"/>
/// (owner decision D11).</item>
/// <item><c>where</c>: <see cref="PlayerTools.WhereAmI"/>; the server's position of the player must agree with the
/// client's (<see cref="GameState.Self"/>) within <see cref="PositionAgreement.ToleranceMetres"/>
/// (<see cref="WhereMaxMetres"/>). Nothing on the client has moved the player, so a disagreement is a defect of the read model, or ACE's own placement
/// after the player's CreateObject (ACE Player_Networking.cs 224, then WorldObject.cs 209-220) moved a character saved inside a solid server object, which
/// Lifestone's client did not collide with before the server-object physics host existed (<c>TODO(d25-server-object-host)</c>, closed by M5 item 5 h1 and h2: the headless body now feeds the host from the object table, so the lifestone is solid in its world; M4 4.2 FS: 2.22 m out of Holtburg's lifestone).</item>
/// <item><c>playing</c> (only with a <c>play</c> scenario, ST16): the player is in the world and the server agrees where it
/// is; <c>play</c> is stepped each frame until it succeeds (then the scenario goes on) or fails (so does this one).</item>
/// <item><c>logging-off</c>: <see cref="IActionApi.LogOff"/>, confirmed back at the character list.</item>
/// <item><c>quitting</c>: <see cref="IActionApi.Quit"/> (<see cref="ActionOutcomeKind.Sent"/>); then <c>done</c>.</item>
/// </list>
/// <para>Any other outcome fails the scenario with the step and the outcome (<see cref="Failure"/>); the host then disposes
/// the session, which quits the network session. Character names appear in the log, never in the summary.</para>
/// </remarks>
public sealed class LoginScenario : IScenario
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "login";

    /// <summary>The default limit on the arrival, in seconds on the session clock (portal space takes 5 s or more).</summary>
    public const double DefaultArrivalSeconds = 90;

    private readonly IReadOnlyList<uint>? _skillClasses;
    private readonly IScenario? _play;
    private ActionHandle? _pending;
    private ToolHandle<ToolStatus>? _status;
    private ToolHandle<WhereResult>? _where;
    private bool _createTried;
    private double _arrivalStart = double.NaN;

    /// <summary>A login scenario.</summary>
    /// <param name="character">The character to enter (<c>--character</c>), or null.</param>
    /// <param name="createCharacter">The character to create when missing and enter (<c>--create-character</c>), or null.</param>
    /// <param name="skillClasses">The skill advancement classes of a created character
    /// (<see cref="HeadlessCharacter.RetailSkillClasses"/>); required with <paramref name="createCharacter"/>.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="play">What to do in the world once the server's view of the player agrees (the movement scenarios,
    /// ST16), stepped in the <c>playing</c> stage; null logs off right away.</param>
    /// <exception cref="ArgumentException"><paramref name="createCharacter"/> without <paramref name="skillClasses"/>.</exception>
    public LoginScenario(string? character = null, string? createCharacter = null, IReadOnlyList<uint>? skillClasses = null, double arrivalSeconds = DefaultArrivalSeconds, IScenario? play = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        Character = string.IsNullOrWhiteSpace(character) ? null : character.Trim();
        CreateCharacter = string.IsNullOrWhiteSpace(createCharacter) ? null : createCharacter.Trim();
        if (CreateCharacter is not null && skillClasses is null)
        {
            throw new ArgumentException("creating a character needs its skill classes (HeadlessCharacter.RetailSkillClasses)", nameof(skillClasses));
        }

        _skillClasses = skillClasses is null ? null : [.. skillClasses];
        _play = play;
        ArrivalSeconds = arrivalSeconds;
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The character to enter, or null.</summary>
    public string? Character { get; }

    /// <summary>The character to create when missing, or null.</summary>
    public string? CreateCharacter { get; }

    /// <summary>The limit on the arrival, in seconds.</summary>
    public double ArrivalSeconds { get; }

    /// <summary>
    /// True when the scenario ends in the world, logged in, once the server's view of the player agrees (M4 P4: the entry of a
    /// <see cref="Hosting.HeadlessSession"/>, which runs the next scenarios over the same login); no log off, no quit. Default false.
    /// </summary>
    public bool StayInWorld { get; init; }

    /// <summary>The character entered (its guid), or 0.</summary>
    public uint EnteredGuid { get; private set; }

    /// <summary>The status the server mod reported, or null.</summary>
    public ToolStatus? Status { get; private set; }

    /// <summary>Where the server said the player is (the <c>where</c> step), or null.</summary>
    public WhereResult? Where { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Always 0: the login scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null)
        {
            return ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        switch (Stage)
        {
            case "start":
                _pending = actions.Login(ActionSource.Host);
                Stage = "login";
                break;

            case "login":
                if (Settled(out var login))
                {
                    return login.Kind == ActionOutcomeKind.Confirmed ? Go("choosing") : Fail("login", login);
                }

                break;

            case "choosing":
                return Choose(session);

            case "creating":
                if (Settled(out var created))
                {
                    return created.Kind == ActionOutcomeKind.Confirmed ? Go("choosing") : Fail("creating the character", created);
                }

                break;

            case "entering":
                if (Settled(out var entered))
                {
                    if (entered.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail("entering the world", entered);
                    }

                    _arrivalStart = session.Clock.Now;
                    return Go("arriving");
                }

                break;

            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present)
                {
                    _status = session.Tools.Status(ActionSource.Host);
                    _pending = _status;
                    return Go("status");
                }

                if (session.Clock.Now - _arrivalStart > ArrivalSeconds)
                {
                    return Fail(string.Create(
                        CultureInfo.InvariantCulture,
                        $"arriving: the player was not unhidden within {ArrivalSeconds:0.#} s (travel {session.State.Self.Travel}); portal space sends LoginComplete when the destination is ready"));
                }

                break;

            case "status":
                if (Settled(out var status))
                {
                    if (status.Kind != ActionOutcomeKind.Confirmed || _status?.Result is not { } reported)
                    {
                        return Fail("asking the server mod for its status", status);
                    }

                    Status = reported;
                    if (!string.Equals(reported.Access, PlayerTools.PlayerAccess, StringComparison.Ordinal))
                    {
                        return Fail($"status: the account's access level is {reported.Access}, above {PlayerTools.PlayerAccess} (owner decision D11)");
                    }

                    _where = session.Tools.WhereAmI(ActionSource.Host);
                    _pending = _where;
                    return Go("where");
                }

                break;

            case "where":
                if (Settled(out var where))
                {
                    return CheckWhere(session, where);
                }

                break;

            case "playing":
                switch (_play!.Step(session, now))
                {
                    case ScenarioStatus.Succeeded when StayInWorld:
                        return Go("done", ScenarioStatus.Succeeded);
                    case ScenarioStatus.Succeeded:
                        _pending = actions.LogOff(ActionSource.Host);
                        return Go("logging-off");
                    case ScenarioStatus.Failed:
                        return Fail(_play.Failure ?? $"the {_play.Name} scenario failed without saying why");
                }

                break;

            case "logging-off":
                if (Settled(out var loggedOff))
                {
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail("logging off", loggedOff);
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    return Go("quitting");
                }

                break;

            case "quitting":
                if (Settled(out var quit))
                {
                    return quit.Kind == ActionOutcomeKind.Sent ? Go("done", ScenarioStatus.Succeeded) : Fail("quitting", quit);
                }

                break;

            default:
                return Fail($"TODO(state): unknown login-scenario stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus Choose(IGameSessionView session)
    {
        var list = session.State.Characters.Entries;
        var wanted = Character ?? CreateCharacter;
        if (wanted is not null)
        {
            foreach (var c in list)
            {
                if (string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return c.PendingDeletion ? Fail("choosing: the named character is pending deletion (restore it first)") : Enter(session, c.Guid);
                }
            }

            if (CreateCharacter is not null && string.Equals(wanted, CreateCharacter, StringComparison.OrdinalIgnoreCase))
            {
                if (_createTried)
                {
                    return Fail("choosing: the character the server confirmed is not in the character list");
                }

                _createTried = true;
                _pending = session.Actions.CreateCharacter(HeadlessCharacter.Intent(CreateCharacter, _skillClasses!), ActionSource.Host);
                return Go("creating");
            }

            return Fail("choosing: the account has no character of that name (--create-character=NAME creates one)");
        }

        foreach (var c in list)
        {
            if (!c.PendingDeletion)
            {
                return Enter(session, c.Guid);
            }
        }

        return Fail("choosing: the account has no character to enter (--create-character=NAME creates one)");
    }

    private ScenarioStatus CheckWhere(IGameSessionView session, in ActionOutcome outcome)
    {
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail("asking the server where the player is", outcome);
        }

        Where = server;
        if (session.State.Self.Position is not { } client)
        {
            return Fail("where: the client has no position for the player");
        }

        var agrees = PositionAgreement.Agrees(server.Position, client, out var metres);
        WhereMaxMetres = Math.Max(WhereMaxMetres ?? 0, metres);
        if (!agrees)
        {
            return Fail("where: " + PositionAgreement.Describe(server.Position, client));
        }

        if (_play is not null)
        {
            return Go("playing");
        }

        if (StayInWorld)
        {
            return Go("done", ScenarioStatus.Succeeded);
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    private ScenarioStatus Enter(IGameSessionView session, uint guid)
    {
        EnteredGuid = guid;
        _pending = session.Actions.EnterWorld(guid, ActionSource.Host);
        return Go("entering");
    }

    private bool Settled(out ActionOutcome outcome)
    {
        if (_pending is { Outcome: { } done })
        {
            _pending = null;
            outcome = done;
            return true;
        }

        outcome = default;
        return false;
    }

    private ScenarioStatus Go(string stage, ScenarioStatus status = ScenarioStatus.Running)
    {
        Stage = stage;
        return status;
    }

    private ScenarioStatus Fail(string step, in ActionOutcome outcome) =>
        Fail(step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) +
             (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    private ScenarioStatus Fail(string why)
    {
        Failure = why;
        return ScenarioStatus.Failed;
    }
}
