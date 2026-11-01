using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The portal exit of a <see cref="RoutePlay"/> (<see cref="PortalExit"/>, ST16 part 2): after the walk the player uses the
/// portal from range, the server walks it the rest of the way with a MoveToObject that the walker carries out (AV14), and
/// the portal's teleport (F751) brings it outside.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> after <c>walking</c>:</para>
/// <list type="number">
/// <item><c>using</c>: <see cref="IActionApi.Use"/> of the portal (GameAction 0x0036); the distance to it is logged.</item>
/// <item><c>exiting</c>: the player's server movements are applied to the walker (a MoveToObject or MoveToPosition: the
/// controller loses control to the server and the physics object's <c>MoveToManager</c> walks, while position messages keep
/// going out on retail's cadence); when the server announces the teleport (F751) movement stops being sent, as the walker
/// stays in the dungeon; the stage ends when the player has arrived in another landblock and is present, and the use
/// completed without a server error.</item>
/// <item><c>exit-where</c>: where-am-I until the server no longer has the player teleporting; its position must be outside
/// the dungeon and agree with the arrival position within the audit's 0.5 m.</item>
/// </list>
/// </remarks>
public sealed partial class RoutePlay
{
    /// <summary>The limit on the portal exit (the server's walk to the portal, the use, portal space), in seconds on the session clock.</summary>
    public const double ExitSeconds = 90;

    /// <summary>How long after the use completes (UseDone) the portal's teleport may take to be announced before the use is called refused, in seconds.</summary>
    public const double PostUseSeconds = 12;

    /// <summary>How many times where-am-I is asked at a quest-refused portal before a disagreement with the walker is recorded (when the scenario takes the refusal as the outcome).</summary>
    public const int MaxRefusedAsks = 4;

    /// <summary>The pause between those asks, in seconds.</summary>
    public const double RefusedAskSpacingSeconds = 1.5;

    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _motionSubscription;
    private IDisposable? _teleportSubscription;
    private SessionTargetWorld? _targets;
    private WorldObject? _portal;
    private ActionHandle? _use;
    private CellFrame? _exitArrival;
    private bool _exitAnnounced;
    private bool _movementHeld;
    private long _chatMark;
    private string? _lastServerText;
    private double? _useDoneAt;
    private string? _refusal;
    private int _searchLegs;
    private readonly HashSet<uint> _visited = [];
    private readonly HashSet<uint> _portalsSeen = [];
    private int _questRefusals;
    private int _refusedAsks;
    private double _refusedNext;

    /// <summary>
    /// The WeenieError ACE sends (once a second, while the player stands at the portal) when an Academy portal's quest requirement is not met: 0x0474,
    /// as the live runs saw it (a character without the Sentry's quest).
    /// </summary>
    public const uint QuestRefusalWeenieError = 0x0474;

    /// <summary>True when the portal's Use was answered by the quest refusal (<see cref="QuestRefusalWeenieError"/>) and the scenario took that as the exit's outcome (it was built with <see cref="ExitQuestMode.Erase"/>).</summary>
    public bool ExitRefusedByQuest { get; private set; }

    /// <summary>The server movements of the player (MoveToObject, MoveToPosition, TurnToObject, TurnToHeading) given to the walker.</summary>
    public int ServerMovements { get; private set; }

    /// <summary>The player's other server movements (an emote, an action), which the walker does not follow.</summary>
    public int ServerMovementsIgnored { get; private set; }

    /// <summary>The portal the walk ends at, or null before it is planned.</summary>
    public WorldObject? Portal => _portal;

    /// <summary>Where the portal's teleport put the player (the first self teleport outside the dungeon), or null.</summary>
    public CellFrame? ExitArrival => _exitArrival;

    /// <summary>True when the server announced a teleport (F751) while the player was using the portal.</summary>
    public bool ExitAnnounced => _exitAnnounced;

    /// <summary>The outcome of the portal's Use, or null while it is in flight (or before it was sent).</summary>
    public ActionOutcome? UseOutcome => _use?.Outcome;

    private void BeginServerMovement(IGameSessionView session)
    {
        if (_host is null)
        {
            _targets = new SessionTargetWorld(session.State, _dats, _geometry, () => _world.CurrentTime, _walker!.Physics);
            _walker.Physics.TargetWorld = _targets;
        }

        _motionSubscription = session.Events.Subscribe<ObjectMotionChanged>((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && !e.Autonomous)
            {
                _serverMotions.Add(e);
            }
        });
        _teleportSubscription = session.Events.Subscribe<PlayerTeleportAnnounced>((in PlayerTeleportAnnounced e) =>
        {
            if (Stage == "exiting")
            {
                // The walker stays in the dungeon, so from here on its positions would pull the player back.
                _exitAnnounced = true;
                _movementHeld = true;
            }
        });
    }

    /// <summary>A self teleport (F751 then its position): records the one that leaves the dungeon while the player exits.</summary>
    private void NoteSelfTeleport(in ObjectMoved e)
    {
        if (_exit is not null && Stage == "exiting" && LandblockCoord.FromId(e.Position.CellId) != _exit.Cells.Coord)
        {
            _exitArrival ??= e.Position;
        }
    }

    /// <summary>
    /// Gives the walker the player's server movements: a MoveTo or TurnTo makes it walk or turn (the controller loses control
    /// to the server first: <c>ACSmartBox::DispatchSmartBoxEvent</c> (11.4186 0x005595D0, 11.6096 0x0055A210) calls
    /// <c>CommandInterpreter::LoseControlToServer</c> (11.4186 0x006B3170, 11.6096 0x006B40B0)); the rest (an emote, an
    /// action) is counted and left alone. The target of a MoveToObject is found in <see cref="SessionTargetWorld"/>.
    /// </summary>
    private void ApplyServerMotions()
    {
        if (_serverMotions.Count == 0)
        {
            return;
        }

        foreach (var e in _serverMotions)
        {
            var type = (byte)e.Motion.MovementType;
            if (_host is not null)
            {
                // The game's avatar adapter gives the player's body its server moves (a MoveTo takes control from the keys); the scenario only counts them.
                if (type is >= 6 and <= 9)
                {
                    ServerMovements++;
                    _log.Info(LogCategory, "the server moves the player", new LogField("type", type), new LogField("target", e.Motion.MoveTo?.TargetGuid is { } hostedTarget ? $"0x{hostedTarget:X8}" : "none"), new LogField("moving_to", _walker!.Physics.IsMovingTo));
                }
                else
                {
                    ServerMovementsIgnored++;
                }

                continue;
            }

            if (type is < 6 or > 9 || !_placed || _walker!.Body is not IPlayerMovementBody body)
            {
                ServerMovementsIgnored++;
                _log.Debug(LogCategory, "a server movement of the player is not followed", new LogField("type", type), new LogField("placed", _placed));
                continue;
            }

            var count = AvatarWire.CopyActions(e.Motion, _actions);
            if (count > _actions.Length)
            {
                _actions = new WireMotionItem[count];
                AvatarWire.CopyActions(e.Motion, _actions);
            }

            var result = body.ApplyServerMovement(AvatarWire.ToMovement(e.Motion), _actions.AsSpan(0, count), e.Autonomous, out var failure);
            ServerMovements++;
            _log.Info(
                LogCategory,
                "the server moves the player",
                new LogField("type", type),
                new LogField("target", e.Motion.MoveTo?.TargetGuid is { } target ? $"0x{target:X8}" : "none"),
                new LogField("result", result.ToString()),
                new LogField("moving_to", _walker.Physics.IsMovingTo),
                new LogField("initialized", _walker.Physics.Movement?.MoveTo?.Initialized == true),
                new LogField("failure", result == MovementUnpackResult.Applied ? "none" : failure.Message));
        }

        _serverMotions.Clear();
    }

    /// <summary>
    /// The exit portal among the session objects in the dungeon: the only one named <see cref="PortalExit.PortalName"/>.
    /// <paramref name="found"/> is null when none is known yet (the server tells the client only of the objects the player
    /// cell can see); the result is a reason when several cannot be told apart.
    /// </summary>
    private string? FindExitPortal(IGameSessionView session, out WorldObject? found)
    {
        var exit = _exit!;
        found = null;
        var portals = session.State.Objects.All
            .Where(o => !o.IsSelf && o.Position is { } p && LandblockCoord.FromId(p.CellId) == exit.Cells.Coord
                && WorldContentCensus.Classify(o.Weenie) == WorldContentKind.Portal)
            .OrderBy(o => o.Guid)
            .ToList();
        if (portals.Count == 0)
        {
            return null;
        }

        foreach (var portal in portals)
        {
            if (_portalsSeen.Add(portal.Guid))
            {
                _log.Info(
                    LogCategory,
                    "a portal is in view",
                    new LogField("portal", $"0x{portal.Guid:X8}"),
                    new LogField("name", portal.Name),
                    new LogField("wcid", portal.Weenie.WeenieClassId),
                    new LogField("flags", $"0x{portal.Weenie.ObjectDescriptionFlags:X8}"),
                    new LogField("use_radius", portal.Weenie.UseRadius),
                    new LogField("cell", $"0x{portal.Position!.Value.CellId:X8}"),
                    new LogField("at", portal.Position!.Value.ToString()));
            }
        }

        var named = portals.Where(o => o.Name.Contains(exit.PortalName, StringComparison.OrdinalIgnoreCase)).ToList();
        found = named.Count == 1 ? named[0] : null;
        return named.Count <= 1
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"planning: {portals.Count} portals in landblock {exit.Cells.Coord} ({string.Join(", ", portals.Select(o => $"0x{o.Guid:X8} '{o.Name}'"))}) and {named.Count} named '{exit.PortalName}'; TODO(st16-which-portal) the exit portal cannot be told apart");
    }

    /// <summary>
    /// At the start of the walk: plans the walk to the exit portal when the session knows it, else a search walk to the far end
    /// of the dungeon, which is replaced by the walk to the portal as soon as it comes into view (<see cref="WatchForPortal"/>).
    /// A failure status, or null when planned.
    /// </summary>
    private ScenarioStatus? PlanExitRoute(IGameSessionView session)
    {
        var exit = _exit!;
        if (FindExitPortal(session, out var found) is { } ambiguous)
        {
            return Fail(ambiguous);
        }

        var spawn = _route.Start!.Value;
        if (found is not null)
        {
            return PlanToPortal(found, spawn);
        }

        var far = exit.FarthestRoom(spawn.CellId);
        if (far is null)
        {
            return Fail($"planning: no exit portal is in view in landblock {exit.Cells.Coord} and no other room can be walked to from cell 0x{spawn.CellId:X8}; TODO(st16-which-portal)");
        }

        try
        {
            _route = RouteLibrary.AcademyExit(spawn, far.Value, exit.Cells, PortalExit.SearchStopWithinMetres);
        }
        catch (InvalidOperationException e)
        {
            return Fail("planning the search for the exit portal: " + e.Message);
        }

        BeginDriver();
        _log.Info(
            LogCategory,
            "no exit portal is in view; walking to the far end of the dungeon to find it",
            new LogField("room", $"0x{far.Value.CellId:X8}"),
            new LogField("goals", _route.Goals.Count));
        return null;
    }

    /// <summary>Plans the walk from <paramref name="from"/> to <paramref name="portal"/>, stopping short of it; a failure status, or null.</summary>
    private ScenarioStatus? PlanToPortal(WorldObject portal, in CellFrame from)
    {
        var exit = _exit!;
        try
        {
            _route = RouteLibrary.AcademyExit(from, portal.Position!.Value, exit.Cells, exit.StopWithinMetres);
        }
        catch (InvalidOperationException e)
        {
            return Fail("planning the walk to the exit portal: " + e.Message);
        }

        _portal = portal;
        BeginDriver();
        _log.Info(
            LogCategory,
            "the exit portal and the walk to it",
            new LogField("portal", $"0x{portal.Guid:X8}"),
            new LogField("name", portal.Name),
            new LogField("cell", $"0x{portal.Position!.Value.CellId:X8}"),
            new LogField("from_cell", $"0x{from.CellId:X8}"),
            new LogField("goals", _route.Goals.Count),
            new LogField("stop_within_m", exit.StopWithinMetres),
            new LogField("setup", portal.Physics.SetupId is { } setup ? $"0x{setup:X8}" : "none"));
        return null;
    }

    /// <summary>
    /// The search walk ended at its room and the portal is still not in view: walks to the room farthest from here that the player
    /// has not been in, up to <see cref="PortalExit.MaxSearchLegs"/> times. Running when a leg began, else a failure.
    /// </summary>
    private ScenarioStatus NextSearchLeg()
    {
        var exit = _exit!;
        var from = _walker!.Position;
        if (++_searchLegs > PortalExit.MaxSearchLegs || exit.FarthestRoom(from.CellId, _visited) is not { } far)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"walking: no portal named '{exit.PortalName}' came into view after {_searchLegs - 1} searches through {_visited.Count} rooms ({_portalsSeen.Count} other portals were); TODO(st16-which-portal)"));
        }

        try
        {
            _route = RouteLibrary.AcademyExit(from, far, exit.Cells, PortalExit.SearchStopWithinMetres);
        }
        catch (InvalidOperationException e)
        {
            return Fail("planning the search for the exit portal: " + e.Message);
        }

        BeginDriver();
        _wasDwelling = false;
        _log.Info(
            LogCategory,
            "no exit portal yet; walking to the farthest room not visited",
            new LogField("leg", _searchLegs),
            new LogField("room", $"0x{far.CellId:X8}"),
            new LogField("visited", _visited.Count),
            new LogField("goals", _route.Goals.Count));
        return ScenarioStatus.Running;
    }

    /// <summary>A new driver for the current route; every stop it completes is a lap of the audit.</summary>
    private void BeginDriver()
    {
        var audit = _audit!;
        _driver = new RouteDriver(_walker!.Controller, _route);
        _driver.Stopped += _ => audit.CompleteLap();
        _driver.KeysPressedAgain += line => _log.Warn(LogCategory, line); // M5 7 D153 (D141): a held key let go by something else was pressed again
    }

    /// <summary>
    /// While the walk searches for the portal: when it comes into view, the search walk is dropped and the walk to the portal
    /// planned from where the player stands. A failure status, or null.
    /// </summary>
    private ScenarioStatus? WatchForPortal(IGameSessionView session)
    {
        if (FindExitPortal(session, out var found) is { } ambiguous)
        {
            return Fail(ambiguous);
        }

        if (found is null)
        {
            return null;
        }

        _driver!.Abandon();
        _wasDwelling = false;
        return PlanToPortal(found, _walker!.Position);
    }

    /// <summary>Uses the portal from where the walk stopped.</summary>
    private ScenarioStatus UsePortal(IGameSessionView session)
    {
        var portal = _portal!;
        var metres = PositionAgreement.DistanceMetres(_walker!.Position, portal.Position!.Value);
        _log.Info(
            LogCategory,
            "using the exit portal",
            new LogField("portal", $"0x{portal.Guid:X8}"),
            new LogField("distance_m", metres),
            new LogField("at", _walker.Position.ToString()),
            new LogField("portal_at", portal.Position!.Value.ToString()));
        _chatMark = session.State.Chat.Total;
        _use = session.Actions.Use(portal.Guid, ActionSource.Host);
        return Go("exiting", session);
    }

    /// <summary>Waits for the portal's teleport to bring the player outside, and for the use to complete.</summary>
    private ScenarioStatus Exiting(IGameSessionView session)
    {
        var chat = session.State.Chat;
        for (; _chatMark < chat.Total; _chatMark++)
        {
            var index = chat.Count - (int)(chat.Total - _chatMark);
            if (index >= 0)
            {
                var line = chat[index];
                var said = line.Kind == ChatKind.WeenieError
                    ? string.Create(CultureInfo.InvariantCulture, $"WeenieError 0x{line.ChatType:X4}{(string.IsNullOrEmpty(line.Text) ? string.Empty : ": " + line.Text)}")
                    : line.Text;
                if (line.Kind == ChatKind.WeenieError && line.ChatType == QuestRefusalWeenieError)
                {
                    _questRefusals++;
                }

                _log.Info(LogCategory, "the server said", new LogField("text", said), new LogField("kind", line.Kind.ToString()), new LogField("chat_type", line.ChatType));
                _lastServerText = said;
            }
        }

        if (_use?.Outcome is { } outcome && outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            return Fail("using the exit portal", outcome);
        }

        if (_use?.Outcome is not null && !_exitAnnounced)
        {
            _useDoneAt ??= session.Clock.Now;
            if (session.Clock.Now - _useDoneAt > PostUseSeconds)
            {
                _refusal = string.Create(
                    CultureInfo.InvariantCulture,
                    $"exiting: the use completed {PostUseSeconds:0.#} s ago and no teleport (F751) followed; the server last said \"{_lastServerText ?? "nothing"}\"{(_questRefusals > 0 && _exitQuest == ExitQuestMode.Stamp ? $" (ACE refused the portal for its quest although the character held {_exit!.QuestName})" : string.Empty)}");
                _pending = null;
                return Go("refused-where", session);
            }
        }

        if (_exitAnnounced && _exitArrival is { } arrival && session.State.Self.Travel == SelfTravel.Present && _use?.Outcome is not null)
        {
            if (_exitQuest == ExitQuestMode.Erase)
            {
                return Fail($"exiting: the portal teleported the character (to cell 0x{arrival.CellId:X8}) although the scenario had taken quest {_exit!.QuestName} from it: the quest gate did not hold");
            }

            _log.Info(
                LogCategory,
                "the portal's teleport brought the player outside",
                new LogField("cell", $"0x{arrival.CellId:X8}"),
                new LogField("server_movements", ServerMovements));
            _pending = null;
            _nextWhere = 0;
            return Go("exit-where", session);
        }

        return Within(
            session,
            ExitSeconds,
            string.Create(
                CultureInfo.InvariantCulture,
                $"exiting: the player did not arrive outside the dungeon and become present (announced {_exitAnnounced}, arrived {_exitArrival is not null}, travel {session.State.Self.Travel}, use {(_use?.Outcome is { } o ? o.Kind.ToString() : "in flight")}, the server last said \"{_lastServerText ?? "nothing"}\")"));
    }

    /// <summary>
    /// After a use that ACE answered with UseDone and no teleport: the server's MoveTo walked the player to the portal, so the server and
    /// the walker must agree where that is (the check the arrival gets); then the run fails with the refusal, which names what the
    /// server said.
    /// </summary>
    private ScenarioStatus RefusedWhere(IGameSessionView session)
    {
        if (_pending is null)
        {
            if (session.Clock.Now < _refusedNext)
            {
                return Within(session, SettleSeconds, _refusal + "; and the server's view of the player did not catch up");
            }

            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Within(session, SettleSeconds, _refusal + "; and no answer from the server about where the player is");
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail(_refusal + "; and asking the server where the player is", outcome);
        }

        var apart = PositionAgreement.DistanceMetres(_walker!.Position, server.Position);
        if (apart > MovementAuditSummary.WhereToleranceMetres && (_walker.Physics.IsMovingTo || ++_refusedAsks < MaxRefusedAsks))
        {
            // The server's view of the walker comes from the position messages it has had; one that is still on its way (or was rate-limited) leaves it behind for a moment. Ask
            // again before the disagreement is recorded or called a failure (TODO(st16-refused-where): the cause of the 2.3 to 2.4 m is not pinned). This settle does not depend on
            // whether the refusal is the expected outcome: a refusal in Stamp mode (which fails) needs it as much as Erase mode does. The walker's own MoveTo to the
            // portal can still be under way 12 s after UseDone (live: it moved 3 m between two asks 1.5 s apart, moving_to true, TODO(st16-moveto-settle): it circles
            // the portal before it arrives), and the server cannot agree with a walker that is moving; so a walker that is moving does not use up an ask, and only the
            // stage's SettleSeconds limit bounds the wait.
            _log.Warn(LogCategory, "the server's view of the player at the refused portal is behind the walker; asking again", new LogField("metres", apart), new LogField("ask", _refusedAsks), new LogField("walker", _walker.Position.Frame.Origin), new LogField("server", server.Position.Frame.Origin), new LogField("moving_to", _walker.Physics.IsMovingTo), new LogField("server_controlled", _walker.Controller.ControlledByServer));
            _refusedNext = session.Clock.Now + RefusedAskSpacingSeconds;
            return Within(session, SettleSeconds, _refusal + "; and the server's view of the player did not catch up");
        }

        var metres = _audit!.RecordWhere(_walker!.Position, server.Position, server.ForcePositionSequence);
        if (_exitQuest == ExitQuestMode.Erase && _questRefusals > 0 && metres <= MovementAuditSummary.WhereToleranceMetres)
        {
            ExitRefusedByQuest = true;
            _log.Info(
                LogCategory,
                "the exit portal refused the character without its quest, as ACE does (WeenieError 0x0474 and no teleport); the walk to the portal, the server's MoveTo and where-am-I were checked",
                new LogField("refusals", _questRefusals),
                new LogField("metres", metres));
            return Go("verdict", session);
        }

        return Fail(string.Create(CultureInfo.InvariantCulture, $"{_refusal}; the server has the player {metres:0.00} m from the walker at the portal"));
    }

    /// <summary>After the arrival: the server must have the player where the teleport put it, outside the dungeon.</summary>
    private ScenarioStatus ExitWhere(IGameSessionView session)
    {
        if (_pending is null)
        {
            if (session.Clock.Now < _nextWhere)
            {
                return Within(session, SettleSeconds, "exit-where: the server still had the player teleporting");
            }

            if (_host is not null && (_walker!.Physics.PlacementPending || _walker.Physics.Cell is null || LandblockCoord.FromId(_walker.Position.CellId) == _exit!.Cells.Coord))
            {
                // Hosted, the game's body is the client's position: it is placed where the portal put the player once the destination has streamed in (as Settle waits for after a teleport).
                return Within(session, SettleSeconds, "exit-where: the game's player body was not placed where the portal put it");
            }

            if (_host is not null)
            {
                _whereClient = _walker!.Position;
            }

            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Within(session, SettleSeconds, "exit-where: no answer from the server about where the player is");
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail("exit-where: asking the server where the player is", outcome);
        }

        if (server.Teleporting)
        {
            _nextWhere = session.Clock.Now + 0.25;
            return Within(session, SettleSeconds, "exit-where: the server still had the player teleporting");
        }

        // Hosted: the client's position is the game's physics body (it left the dungeon with the player); unhosted the walker stays in the dungeon, so the position the teleport announced stands in.
        var client = _host is not null ? _whereClient : _exitArrival!.Value;
        var metres = _audit!.RecordWhere(client, server.Position, server.ForcePositionSequence);
        _log.Info(
            LogCategory,
            "the server has the player outside the dungeon",
            new LogField("metres", metres),
            new LogField("client", _host is not null ? "the game's body" : "the teleport's announced position"),
            new LogField("cell", $"0x{server.Position.CellId:X8}"));
        if (LandblockCoord.FromId(server.Position.CellId) == _exit!.Cells.Coord)
        {
            return Fail($"exit-where: the server still has the player in the dungeon, cell 0x{server.Position.CellId:X8}");
        }

        return Go("verdict", session);
    }
}
