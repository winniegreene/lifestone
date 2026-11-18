using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Contracts;

/// <summary>Which of the game's two records answered "where does it stand now" (<see cref="LivePositions"/>).</summary>
public enum LiveSource : byte
{
    /// <summary>Neither: the server's record gives the object no position (held, in a pack, picked up), so it stands nowhere.</summary>
    None,

    /// <summary>The body the screen draws (<see cref="ILiveBodies"/>): retail's client physics moved it since the server's last report.</summary>
    Body,

    /// <summary>The server's last report (<see cref="WorldObject.Position"/>: the CreateObject's position, then each F748 UpdatePosition).</summary>
    ServerReport,

    /// <summary>M6 14: the place the server named in a movement order aimed at the object (<see cref="WorldObject.OrderPosition"/>), newer than its last position report.</summary>
    ServerOrder,
}

/// <summary>Why <see cref="LivePositions"/> answered from the server's report though the session draws bodies (<see cref="LiveReading.Fallback"/>).</summary>
public enum LiveFallback : byte
{
    /// <summary>No fall back: the body answered, or the object has no position at all.</summary>
    None,

    /// <summary>The session draws no bodies (a headless host, a unit-test rig): the server's report is the only record. Neither logged nor counted.</summary>
    NoBodies,

    /// <summary>The bodies hold none for the object (its create failed, or still waits in the adapter's queue).</summary>
    NoBody,

    /// <summary>The body is in no cell of its own (not placed yet, or held by another object).</summary>
    NotInWorld,

    /// <summary>The body is Hidden: the screen draws nothing there (an object between leaving and arriving is hidden).</summary>
    Hidden,

    /// <summary>The body has not taken the server's last position: a placement refused, a position without contact ignored, or a blip due (<see cref="LiveBody.ServerPositionPending"/>).</summary>
    ServerPositionPending,

    /// <summary>The body stands farther from the server's report than retail walks a body to one (<see cref="LivePositions.BoundMetres"/>).</summary>
    TooFar,
}

/// <summary>One body as the screen draws it, for <see cref="LivePositions"/> (<see cref="ILiveBodies.TryGetBody"/>). A value: no allocation.</summary>
/// <param name="Position">Where the body stands (its cell 0 while it is in none).</param>
/// <param name="InWorld">True when it stands in a cell on its own (not held by another object).</param>
/// <param name="Hidden">True while it is Hidden (not drawn).</param>
/// <param name="ServerPositionPending">True while it is known not to stand where the server last put it: a teleport's or a far position's placement was refused, a position without contact was ignored, or a blip is due.</param>
public readonly record struct LiveBody(CellFrame Position, bool InWorld, bool Hidden, bool ServerPositionPending);

/// <summary>
/// The bodies the screen draws, by object id: the route by which play mode's <c>AvatarSet</c> reaches the session's <see cref="LivePositions"/> with no engine type and no reference from the session layer to the avatars.
/// Game thread only; a lookup allocates nothing.
/// </summary>
public interface ILiveBodies
{
    /// <summary>The body of <paramref name="guid"/>.</summary>
    /// <param name="guid">The object id.</param>
    /// <param name="body">The body, when one is kept.</param>
    /// <returns>False when no body is kept for the object.</returns>
    bool TryGetBody(uint guid, out LiveBody body);
}

/// <summary>One answer of <see cref="LivePositions.Read"/>: where the object stands now, which record said so, and both records for a caller that compares them.</summary>
/// <param name="At">Where it stands now, or null when the server's record gives it no position.</param>
/// <param name="Source">Which record answered.</param>
/// <param name="Fallback">Why the server's report answered though bodies are drawn; <see cref="LiveFallback.None"/> otherwise.</param>
/// <param name="Body">The body's position when a body stands in the world (whether or not it answered), else null.</param>
/// <param name="Report">The server's last report, or null.</param>
public readonly record struct LiveReading(CellFrame? At, LiveSource Source, LiveFallback Fallback, CellFrame? Body, CellFrame? Report);

/// <summary>
/// M6 item 14 (<c>m6-live-position</c>, closing <c>TODO(m6-sticky-position)</c>): the one answer to "where does this creature stand now". The game keeps two records. The body the screen draws
/// (<c>AvatarBody.Position</c>) is retail's client physics: between the server's reports it moves a creature itself (interpolation, MoveTo, and the sticky follow of a monster in melee). The session's
/// <see cref="WorldObject.Position"/> is only the server's last report, and ACE sends none for a monster that sticks to the player (<c>Monster_Tick.cs:118-119</c>: the update runs with the broadcast off). The Cave's
/// run of 2026-11-12 (UTC) judged tuskers by the report alone: 85 times in two and a half hours an attacker beside her "had no clear line" from a spot it had left, and she walked while it went on hitting her.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The body answers when it can be trusted, else the server's report, and the reading says which (<see cref="LiveReading.Source"/>, <see cref="LiveReading.Fallback"/>). The server's record
/// decides whether the object is in the world at all: with no report there is no answer, whatever a body shows. The body does not answer when the session draws none (a headless host, a unit-test rig: the report is
/// then the only record, so existing checks keep their meaning), when the bodies keep none for the object, when the body stands in no cell of its own, when it is Hidden, when it is known not to have taken the
/// server's last position (<see cref="LiveBody.ServerPositionPending"/>), or when it stands farther than <see cref="BoundMetres"/> from the report. Nothing is guessed or blended: the answer is one record or the other.</para>
/// <para><b>The bound</b> is retail's own: <c>CPhysicsObj::GetAutonomyBlipDistance</c> (11.6096 0x0050F640; <see cref="PositionManager.GetAutonomyBlipDistance"/>), 20 m in a dungeon cell and 100 m outdoors for an
/// object that is not the player. Beyond it retail's <c>InterpolationManager::InterpolateTo</c> (11.6096 0x00556740) refuses to walk a body to a report and places it there instead, so a body that far from the
/// report is one retail itself is about to move.</para>
/// <para><b>What the body is not.</b> It is retail's client physics, not the server's truth. A stick lasts one second from each swing or arrival (<see cref="StickyManager.StickySeconds"/>); when it ends with a server
/// position still queued in the interpolation (ACE sends one with the first swing after a chase, <c>Monster_Melee.cs:342-346</c>), the body walks back to that position while the server's monster stays beside the
/// player, until the next swing sticks it again (<c>TODO(m6-sticky-walk-back)</c>; <c>AvatarStickyFollowTests</c> measures it offline). In those seconds body and report agree on a place the creature has left, and no
/// rule here can tell: a blow is the witness (the fight's struck proof and its far-blow line).</para>
/// <para><b>Logging.</b> A fall back with bodies drawn is counted (<see cref="Fallbacks"/>) and logged, at most once per object every <see cref="FallbackLogSeconds"/>; an object whose create is younger than
/// <see cref="CreateGraceSeconds"/> and has no body yet is counted, not logged (its create waits in the adapter's queue, which is no fault).</para>
/// <para>Game thread only. A read allocates nothing (a fall back's log line allocates, at the rate above).</para>
/// </remarks>
public sealed class LivePositions
{
    /// <summary>A fall back is logged at most once per object in this many session-clock seconds.</summary>
    public const double FallbackLogSeconds = 10.0;

    /// <summary>An object created less than this many seconds ago with no body yet is not logged: the avatar adapter applies creates inside a frame budget.</summary>
    public const double CreateGraceSeconds = 2.0;

    private const string LogCategory = "live-position";
    private const int MaxLoggedObjects = 256;

    private readonly ILiveBodies? _bodies;
    private readonly SessionClock? _clock;
    private readonly ILogSink _log;
    private readonly Dictionary<uint, double>? _loggedAt;
    private readonly long[] _fallbacks = new long[(int)LiveFallback.TooFar + 1];

    private LivePositions()
    {
        _log = NullLogSink.Instance;
    }

    /// <summary>The live positions of a session that draws <paramref name="bodies"/>.</summary>
    /// <param name="bodies">The bodies the screen draws.</param>
    /// <param name="clock">The session clock (the fall back log's rate, the create grace).</param>
    /// <param name="log">The log, or none.</param>
    public LivePositions(ILiveBodies bodies, SessionClock clock, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(clock);
        _bodies = bodies;
        _clock = clock;
        _log = log ?? NullLogSink.Instance;
        _loggedAt = new Dictionary<uint, double>(64);
    }

    /// <summary>The answer of a session that draws no bodies: always the server's report. It holds no state, so every such view shares it.</summary>
    public static LivePositions ReportsOnly { get; } = new();

    /// <summary>True when the session draws bodies (play mode wired its avatar set in).</summary>
    public bool HasBodies => _bodies is not null;

    /// <summary>The bodies this answers from, or null (a host takes its own set away again only while it is still the one wired).</summary>
    public ILiveBodies? Bodies => _bodies;

    /// <summary>The reads a body answered.</summary>
    public long BodyAnswers { get; private set; }

    /// <summary>The reads the server's report answered though bodies are drawn, all reasons together.</summary>
    public long Fallbacks { get; private set; }

    /// <summary>The reads the server's report answered for <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    public long FallbacksFor(LiveFallback reason) => _fallbacks[(int)reason];

    /// <summary>
    /// The farthest a body may stand from the server's report and still answer, in metres, for a body in cell <paramref name="bodyCellId"/>: retail's blip distance for an object that is not the player (see the class
    /// remarks).
    /// </summary>
    /// <param name="bodyCellId">The body's cell, as retail reads the moving object's.</param>
    public static float BoundMetres(uint bodyCellId) => PositionManager.GetAutonomyBlipDistance(bodyCellId, isThePlayer: false);

    /// <summary>Where <paramref name="o"/> stands now (<see cref="Read"/>'s <see cref="LiveReading.At"/>), or null when the server's record gives it no position.</summary>
    /// <param name="o">The object.</param>
    public CellFrame? At(WorldObject o) => Read(o).At;

    /// <summary>
    /// M6 14: how long a place named in a movement order stands as the server's newest word when no position report has come since (Lifestone's own: a creature the server placed five seconds ago has had
    /// time to move a room; after that the body, or the report, answers as before).
    /// </summary>
    public const double OrderWordSeconds = 5;

    /// <summary>The answers a movement order's place gave (<see cref="LiveSource.ServerOrder"/>; report-only count).</summary>
    public int OrderAnswers { get; private set; }

    /// <summary>Where <paramref name="o"/> stands now and which record said so (the class remarks give the rule).</summary>
    /// <param name="o">The object.</param>
    public LiveReading Read(WorldObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var report = o.Position;
        if (report is not { } reported)
        {
            return default; // the server's record decides whether it is in the world at all
        }

        if (_bodies is null)
        {
            return new LiveReading(report, LiveSource.ServerReport, LiveFallback.NoBodies, null, report);
        }

        if (o.IsSelf)
        {
            return new LiveReading(report, LiveSource.ServerReport, LiveFallback.None, null, report); // the player's own place is SelfState.Position, the player body's; the bodies are never asked for it
        }

        // M6 14 (use the information the server gives): a movement order aimed at this object named its place after the last position report: the server's newest word, for OrderWordSeconds.
        if (o.OrderPosition is { } ordered && !double.IsNaN(o.OrderPositionAt) && !(o.OrderPositionAt <= o.PositionAt) && _clock!.Now - o.OrderPositionAt <= OrderWordSeconds)
        {
            OrderAnswers++;
            return new LiveReading(ordered, LiveSource.ServerOrder, LiveFallback.None, _bodies.TryGetBody(o.Guid, out var drawn) && drawn.InWorld ? drawn.Position : null, report);
        }

        if (!_bodies.TryGetBody(o.Guid, out var body))
        {
            return Fall(o, LiveFallback.NoBody, null, in reported, 0f);
        }

        if (!body.InWorld)
        {
            return Fall(o, LiveFallback.NotInWorld, null, in reported, 0f);
        }

        var stands = body.Position;
        if (body.Hidden)
        {
            return Fall(o, LiveFallback.Hidden, stands, in reported, 0f);
        }

        if (body.ServerPositionPending)
        {
            return Fall(o, LiveFallback.ServerPositionPending, stands, in reported, PositionFrames.Distance(in stands, in reported));
        }

        var apart = PositionFrames.Distance(in stands, in reported);
        if (!(apart <= BoundMetres(stands.CellId)))
        {
            return Fall(o, LiveFallback.TooFar, stands, in reported, apart); // a NaN distance falls back too
        }

        BodyAnswers++;
        return new LiveReading(stands, LiveSource.Body, LiveFallback.None, stands, report);
    }

    private LiveReading Fall(WorldObject o, LiveFallback reason, CellFrame? body, in CellFrame report, float apart)
    {
        Fallbacks++;
        _fallbacks[(int)reason]++;
        var now = _clock!.Now;
        if (!(reason == LiveFallback.NoBody && now - o.CreatedAt < CreateGraceSeconds) && (!_loggedAt!.TryGetValue(o.Guid, out var last) || now - last >= FallbackLogSeconds))
        {
            if (_loggedAt.Count >= MaxLoggedObjects)
            {
                _loggedAt.Clear(); // bounded: the rate limit starts again for all of them
            }

            _loggedAt[o.Guid] = now;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{o.Name} 0x{o.Guid:X8} is taken to stand where the server last reported it, 0x{report.CellId:X8} [{report.Origin.X:0.#} {report.Origin.Y:0.#} {report.Origin.Z:0.#}], not where its body is drawn: {Why(reason, body, apart)} (m6-live-position; fall backs so far {Fallbacks}, this reason {_fallbacks[(int)reason]})"));
        }

        return new LiveReading(report, LiveSource.ServerReport, reason, body, report);
    }

    private static string Why(LiveFallback reason, CellFrame? body, float apart) => reason switch
    {
        LiveFallback.NoBody => "no body is kept for it (its create failed, or waits in the adapter's queue)",
        LiveFallback.NotInWorld => "its body stands in no cell of its own (not placed, or held)",
        LiveFallback.Hidden => "its body is Hidden, the screen draws nothing there",
        LiveFallback.ServerPositionPending when body is { } b => string.Create(CultureInfo.InvariantCulture, $"its body at 0x{b.CellId:X8} [{b.Origin.X:0.#} {b.Origin.Y:0.#} {b.Origin.Z:0.#}], {apart:0.#} m off, has not taken the server's last position (a placement refused, a position without contact, or a blip due)"),
        LiveFallback.TooFar when body is { } b => string.Create(CultureInfo.InvariantCulture, $"its body at 0x{b.CellId:X8} [{b.Origin.X:0.#} {b.Origin.Y:0.#} {b.Origin.Z:0.#}] is {apart:0.#} m from the report, over retail's blip distance of {BoundMetres(b.CellId):0} m there"),
        _ => reason.ToString(),
    };
}
