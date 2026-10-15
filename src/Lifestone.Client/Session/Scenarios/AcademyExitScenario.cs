using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// DoD 8's Academy scenario (step ST16 part 2): log in as <see cref="LoginScenario"/> does, send the character to the
/// Training Academy's start (cell 0x860201AD, where new characters begin) with the server mod's <c>place loc</c>, walk
/// through the Academy's rooms to the exit portal the server created, use the portal from range (the server walks the
/// player the rest of the way with a MoveToObject that the walker carries out) and arrive outside the Academy through the
/// portal's teleport (F751), with ACE accepting every move: no force-position correction, and where-am-I within 0.5 m
/// at every check (<see cref="RoutePlay"/>, <see cref="MovementAudit"/>). Before the Use the character is made to hold the Sentry's quest
/// the portal needs (<see cref="ExitQuestMode"/>), and after it the character is sent back to where the login found it (the Academy start) and
/// the quest registry is asked and brought back to what the scenario found; then the scenario logs off and quits.
/// </summary>
/// <remarks>
/// <para>The physics world is built in the constructor, before the session exists: the collision of the Academy's
/// landblock only (the dungeon has no neighbours that matter), from the dats. The walk to the portal is planned in
/// <see cref="RoutePlay"/> once the portal's CreateObject is in the session: the exit portal is a world-database object
/// (<c>TODO(st16-which-portal)</c> if the landblock holds more than one that cannot be told apart).</para>
/// <para><b>What is not left as found.</b> The portal runs its own emote set after the teleport (weenie 29338, EmoteCategory 4, emote 86996; ACE's
/// <c>Portal.ActOnUse</c> to <c>EmoteManager.OnPortal</c>, Portal.cs:287): it erases ten Academy quests (the Sentry's among them), moves the character's sanctuary
/// to Holtburg, enables recalls (<c>RecallsDisabled</c> false; ACE creates characters with it true, PlayerFactory.cs:393-394), shows a pop-up and sets the last
/// portal. So the exit graduates the character for good, and the mod has no verb to undo that: graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25). What this
/// scenario leaves as found is the character's position (the Academy start) and the Sentry's quest (<see cref="RoutePlay.QuestVerdict"/>).</para>
/// <para>The character must be one whose Run and Jump skills are usable (<see cref="HeadlessCharacter.RetailSkillClasses"/>
/// gives that to a created one).</para>
/// </remarks>
public sealed class AcademyExitScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=academy-exit</c>, the verdict line's <c>route=</c>).</summary>
    public const string ScenarioName = RouteLibrary.AcademyExitName;

    /// <summary>The Training Academy's start cell, where new characters begin (<c>CharGen</c> start area 0, location 0).</summary>
    public const uint AcademyStartCell = 0x860201AD;

    private readonly LoginScenario _login;
    private readonly RoutePlay _play;

    /// <summary>The Academy scenario.</summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="character">The character to enter (<c>--character</c>), or null.</param>
    /// <param name="createCharacter">The character to create when missing and enter, or null.</param>
    /// <param name="skillClasses">The skill classes of a created character (required with <paramref name="createCharacter"/>).</param>
    /// <param name="portalName">The portal to leave through (<see cref="PortalExit.PortalName"/>).</param>
    /// <param name="stopWithinMetres">How far from the portal the walk stops and the player uses it.</param>
    /// <param name="arrivalSeconds">The limit on the login's arrival.</param>
    /// <param name="portalCell">The cell the portal is in (<see cref="PortalExit.PortalCell"/>; the live server's is
    /// <see cref="PortalExit.ExitToHoltburgCell"/>), or null to start at the Academy start: its rooms and the portal's wing are not
    /// connected, so a live run is sent to a room of the wing.</param>
    /// <param name="host">The game's player as the walker (<see cref="RoutePlayHost"/>; the login check), or null for the scenario's own walker.</param>
    /// <param name="exitQuest">What to do about the Sentry's quest the portal needs (<see cref="ExitQuestMode"/>): by default it is stamped when the character lacks it,
    /// the portal teleports the character (and erases the quest with its own emote set), and the registry is asked after the way home and the quest stamped again when it was found held;
    /// <see cref="ExitQuestMode.Erase"/> takes the portal's refusal as the outcome instead.</param>
    /// <exception cref="InvalidDataException">The dats hold no start location in the Academy's start cell, or <paramref name="portalCell"/>
    /// is not one of the Academy's rooms.</exception>
    public AcademyExitScenario(
        DatSet dats,
        ILogSink log,
        string? character = null,
        string? createCharacter = null,
        IReadOnlyList<uint>? skillClasses = null,
        string portalName = PortalExit.DefaultPortalName,
        float stopWithinMetres = PortalExit.DefaultStopWithinMetres,
        double arrivalSeconds = LoginScenario.DefaultArrivalSeconds,
        uint? portalCell = null,
        RoutePlayHost? host = null,
        ExitQuestMode exitQuest = ExitQuestMode.Stamp)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        var spawn = CharGenData.Load(dats).StartAreas.SelectMany(a => a.Locations).Where(l => l.CellId == AcademyStartCell).Select(l => (CellFrame?)l.Normalized()).FirstOrDefault()
            ?? throw new InvalidDataException($"TODO(st16-academy-start): the CharGen start areas hold no location in cell 0x{AcademyStartCell:X8}.");
        var coord = LandblockCoord.FromId(AcademyStartCell);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(dats), coord);
        var exit = new PortalExit(cells, portalName, stopWithinMetres, portalCell, spawn);
        var start = portalCell is null
            ? spawn
            : exit.WingStart() ?? throw new InvalidDataException($"TODO(st16-exit-wing): cell 0x{portalCell:X8} is not one of the Academy's rooms, so no room of the portal's wing can be walked from.");
        Route = new ScriptedRoute(RouteLibrary.AcademyExitName, []) { Start = start };
        _play = new RoutePlay(dats, world, geometry, Route, log, exit: exit, host: host, exitQuest: exitQuest);
        _login = new LoginScenario(character, createCharacter, skillClasses, arrivalSeconds, _play);
    }

    /// <summary>The route as given to the walk: where it starts (the Academy start, or a room of the portal's wing) and no goals (the walk to the portal is planned once the portal is known).</summary>
    public ScriptedRoute Route { get; }

    /// <summary>The walk's part of the scenario (its audit, summary line, walker, portal and exit).</summary>
    public RoutePlay Play => _play;

    /// <summary>The login's part of the scenario.</summary>
    public LoginScenario Login => _login;

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage => _login.Stage == "playing" ? "playing:" + _play.Stage : _login.Stage;

    /// <inheritdoc/>
    public string? Failure => _login.Failure;

    /// <inheritdoc/>
    public double? WhereMaxMetres
    {
        get
        {
            var (a, b) = (_login.WhereMaxMetres, _play.WhereMaxMetres);
            return a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
        }
    }

    /// <inheritdoc/>
    public long Unmatched => _play.Unmatched;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now) => _login.Step(session, now);
}
