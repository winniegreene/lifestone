using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// DoD 8's Holtburg scenario (step ST16): log in as <see cref="LoginScenario"/> does, send the character to the start of a
/// ring around Holtburg's arrival point with the server mod's <c>place loc</c>, walk three laps of it with the
/// <see cref="RouteDriver"/> and the character's own run skill (<see cref="QualitiesWeenie"/>), and require ACE to accept
/// every move: no force-position correction, and where-am-I within 0.5 m at every stop (<see cref="RoutePlay"/>,
/// <see cref="MovementAudit"/>). Then it logs off and quits.
/// </summary>
/// <remarks>
/// <para>The physics world is built in the constructor, before the session exists: the collision of Holtburg's landblock and
/// the eight around it from the dats (as the offline route tests do), and the ring, the smallest radius of
/// <see cref="RouteLibrary.RingRadii"/> the human can walk on that collision (owner decision D47). Building takes a few
/// seconds, so the host constructs the scenario before it connects. <see cref="Route"/> is the ring.</para>
/// <para>The character must be one whose Run and Jump skills are usable (<see cref="HeadlessCharacter.RetailSkillClasses"/>
/// gives that to a created one); the run rate comes from its qualities. The scenario leaves the character at the ring's
/// start in Holtburg: the loopback test and the live runs put it back at its own start with <c>place loc</c> (the character
/// is not the server's to restore).</para>
/// </remarks>
public sealed class HoltburgLapsScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=holtburg-laps</c>, the verdict line's <c>scenario=</c>).</summary>
    public const string ScenarioName = RouteLibrary.HoltburgLapsName;

    private readonly LoginScenario _login;
    private readonly RoutePlay _play;

    /// <summary>The Holtburg scenario.</summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="character">The character to enter (<c>--character</c>), or null.</param>
    /// <param name="createCharacter">The character to create when missing and enter, or null.</param>
    /// <param name="skillClasses">The skill classes of a created character (required with <paramref name="createCharacter"/>).</param>
    /// <param name="laps">The laps to walk.</param>
    /// <param name="arrivalSeconds">The limit on the login's arrival.</param>
    /// <param name="host">The game's player as the walker (<see cref="RoutePlayHost"/>; the login check), or null for the scenario's own walker.</param>
    /// <param name="jumps">True to add the jump leg after the laps (a standing and a running jump, then where-am-I; <see cref="JumpLeg"/>).</param>
    /// <exception cref="InvalidOperationException">No clear ring around Holtburg (the message names the obstacle).</exception>
    public HoltburgLapsScenario(
        DatSet dats,
        ILogSink log,
        string? character = null,
        string? createCharacter = null,
        IReadOnlyList<uint>? skillClasses = null,
        int laps = RouteLibrary.DefaultLaps,
        double arrivalSeconds = LoginScenario.DefaultArrivalSeconds,
        RoutePlayHost? host = null,
        bool jumps = false)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(log);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = AttachAround(dats, geometry, LandblockCoord.Holtburg);
        var body = PhysicsSetup.Load(dats, OfflineWalker.HumanSetupId, geometry)
            ?? throw new InvalidDataException($"The human Setup 0x{OfflineWalker.HumanSetupId:X8} is missing or does not load.");
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg");
        var centre = new CellFrame(poi.CellId, new AcFrame(poi.Origin, Quaternion.Identity));
        Route = RouteLibrary.HoltburgLaps(centre, world, body, laps);
        _play = new RoutePlay(dats, world, geometry, Route, log, host: host, jumps: jumps);
        _login = new LoginScenario(character, createCharacter, skillClasses, arrivalSeconds, _play);
    }

    /// <summary>The ring the scenario walks (its <see cref="ScriptedRoute.Start"/> is where the character is sent first).</summary>
    public ScriptedRoute Route { get; }

    /// <summary>The walk's part of the scenario (its audit, summary line and walker).</summary>
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

    /// <summary>
    /// A physics world with the collision of <paramref name="centre"/> and the eight landblocks around it attached, built
    /// from the dats (the cost is the builds', a few seconds).
    /// </summary>
    internal static PhysicsWorld AttachAround(DatSet dats, PhysicsGeometryLibrary geometry, LandblockCoord centre) =>
        HeadlessStreamer.AttachAround(dats, geometry, centre); // P5a S6: the headless streamer (the collision streamer over two workers) builds the nine in parallel
}
