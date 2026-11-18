using System.Globalization;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// A journey on a scripted ACE (M4 P5b J1): BSD Mage (the wand, the magic mode, known vitals: <see cref="ScriptedCaster"/>) standing in Holtburg in
/// peace, who knows Lifestone Recall (1635) and Primary Portal Recall (48). The scripted ACE answers as ACE @95ef426d does: a recall cast ends (UseDone) and
/// 2 s later teleports to Holtburg or the den (<c>WorldObject_Magic.cs</c> 1087-1111), or sends 0x049E / 0x04A3 when the tie is missing; the mod's place
/// is answered Success and teleports to the destination's cell; a suicide is answered with the death (health 0, NumDeaths up: <c>Player_Death.cs</c>) and
/// the respawn in Holtburg. Every teleport is PlayerTeleport, the position, then the SetState that unhides the player.
/// </summary>
internal sealed class J1JourneyRig
{
    public const uint HoltburgCell = 0xA9B40019;
    public const uint DenCell = 0x01050375;
    public const uint BeltCell = 0xA5B00032;

    private readonly List<(ActionHandle Handle, TeleportDestination To, double At, bool Answered)> _places = [];
    private ushort _teleportStamp = 2;
    private ushort _positionStamp = 2;
    private ushort _stateStamp = 2;
    private byte _vital = 60;
    private int _deaths;
    private int _suicidesSeen;
    private int _toolsSeen;
    private double? _castAt;
    private uint _castSpell;
    private double? _teleportAt;
    private uint _teleportCell;
    private double? _unhideAt;

    /// <summary>A rig with the player at <paramref name="startCell"/>, in peace unless <paramref name="peace"/> is false (the magic mode).</summary>
    public J1JourneyRig(uint startCell = HoltburgCell, bool peace = true)
    {
        R = new WorldActionRig();
        Caster = new ScriptedCaster(R, 426, 275);
        View = new J1View(R);
        R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 1635));
        R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 48));
        R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 47));
        Caster.CastHook = spell =>
        {
            if (spell is not (1635 or 48))
            {
                return false;
            }

            Casts.Add(spell);
            _castAt = Now;
            _castSpell = spell;
            return true;
        };
        Caster.TargetedCastHook = (spell, _) =>
        {
            if (spell != 47)
            {
                return false;
            }

            Casts.Add(spell);
            _castAt = Now;
            _castSpell = spell;
            return true;
        };
        TeleportNow(startCell);
        if (peace)
        {
            R.CombatModeIs(CombatModes.NonCombat);
        }

        R.Tick();
    }

    private sealed class J1View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public IGameSessionView View { get; }

    public double Now => R.H.R.Clock.Now;

    public RecordingLogSink Log => R.H.Log;

    /// <summary>False: ACE answers Lifestone Recall with 0x049E (no LinkedLifestone).</summary>
    public bool LifestoneTied { get; set; } = true;

    /// <summary>False: ACE answers Primary Portal Recall with 0x04A3 (no portal tied).</summary>
    public bool PortalTied { get; set; } = true;

    /// <summary>Nonzero: ACE answers Primary Portal Tie with this WeenieError instead of the link text.</summary>
    public uint TieRefusal { get; set; }

    /// <summary>Creates the portal object <paramref name="guid"/> (weenie class <paramref name="wcid"/>) in view, in <paramref name="cell"/>.</summary>
    public void CreatePortal(uint guid, uint wcid, uint cell)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(cell, 122, 87, 30), name: "Portal");
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = "Portal", WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>The recall spells cast, in order.</summary>
    public List<uint> Casts { get; } = [];

    /// <summary>The cells of the places made straight through the session's tools (not the journey's seam), in order (M5 3 p1: the build's tie leg).</summary>
    public List<uint> PlacedCells { get; } = [];

    /// <summary>The places' destinations, in order.</summary>
    public List<string> PlacesTo { get; } = [];

    /// <summary>The facts the context and the travel share.</summary>
    public JourneyFacts Facts { get; } = new();

    /// <summary>The travel the last <see cref="Context"/> built.</summary>
    public JourneyTravel? Travel { get; private set; }

    /// <summary>The scripted place verb: the session's own <c>Tools.Place</c>, recorded so the scripted ACE answers it.</summary>
    public ActionHandle Place(IGameSessionView session, TeleportDestination destination)
    {
        var handle = session.Tools.Place(destination, ActionSource.Host);
        _places.Add((handle, destination, Now, false));
        PlacesTo.Add(destination.Name);
        return handle;
    }

    /// <summary>A context for <paramref name="character"/> over a <see cref="JourneyTravel"/> with the scripted place.</summary>
    public JourneyContext Context(string character = "BSD Mage", IJourneyRunnerHost? runners = null, IJourneyObserver? observer = null, int budget = 1)
    {
        Facts.TeleportBudget = budget;
        Travel = new JourneyTravel(character, Facts, Log, runners, Place);
        return new JourneyContext
        {
            Character = character,
            Account = "test",
            Log = Log,
            Travel = Travel,
            Spells = Caster.Table,
            Runners = runners,
            Observer = observer ?? NoJourneyObserver.Instance,
            Facts = Facts,
            // M6 7 AV: the way home reads the plan, so a body on the belt (an outdoor ground hunted from home on foot) would walk; these tests prove the recall, so the context knows the dungeons' grounds only
            Grounds = [.. Lifestone.Client.Session.Grounds.GroundCatalog.All.Where(g => g.Kind != Lifestone.Client.Session.Grounds.GroundKind.Outdoor)],
        };
    }

    /// <summary>A journey over the rig: the scripted place, the terrain 10 m everywhere, no log off.</summary>
    public JourneyScenario Journey(JourneyPlan plan, IStationFactory factory, JourneyContext context) =>
        new(plan, factory, context, endSession: false, place: Place, groundHeight: (_, _, _) => 10f);

    /// <summary>One frame: the clock, the Action API, the scripted ACE.</summary>
    public void Frame(double dt = 0.25)
    {
        R.Tick(dt);
        Caster.Pump();
        Script();
        R.Tick();
    }

    /// <summary>Steps <paramref name="scenario"/> every frame until it ends (or the time runs out).</summary>
    public ScenarioStatus Run(IScenario scenario, double maxSeconds = 4000)
    {
        var end = Now + maxSeconds;
        ScenarioStatus status;
        while ((status = scenario.Step(View, Now)) == ScenarioStatus.Running && Now < end)
        {
            Frame();
        }

        return status;
    }

    /// <summary>Steps <paramref name="leg"/> every frame until it ends (or the time runs out).</summary>
    public TravelStatus RunLeg(ITravelLeg leg, double maxSeconds = 300)
    {
        var end = Now + maxSeconds;
        TravelStatus status;
        while ((status = leg.Step(View, Now)) == TravelStatus.Running && Now < end)
        {
            Frame();
        }

        return status;
    }

    /// <summary>An object standing at (<paramref name="x"/>, 20, 30) in <paramref name="cell"/> (the player stands at (84, 20, 30)).</summary>
    public void CreateAt(uint guid, uint cell, float x, float y = 20, float z = 30) =>
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(cell, x, y, z), state: Lifestone.Net.Types.PhysicsState.Gravity), isUpdate: false);

    /// <summary>The player's landblock (the high word of its cell), or 0.</summary>
    public uint Landblock => R.H.State.Self.Position is { } at ? at.CellId >> 16 : 0;

    /// <summary>Teleports the player at once (PlayerTeleport, the position, unhidden).</summary>
    public void TeleportNow(uint cell, Vector3? at = null)
    {
        Teleport(cell, at);
        R.H.Unhide(++_stateStamp);
    }

    private void Teleport(uint cell, Vector3? at = null)
    {
        R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleportStamp));
        var move = ReducerHarness.Move(WorldActionRig.Player, 1, ++_positionStamp, _teleportStamp, 1, cell: cell, x: 84);
        R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, at is { } origin ? move with { Position = ReducerHarness.Position(cell, origin.X, origin.Y, origin.Z) } : move);
    }

    private void Script()
    {
        if (_castAt is { } cast && Now >= cast + 1.0)
        {
            _castAt = null;
            if (_castSpell == 1635 && !LifestoneTied)
            {
                R.WeenieError(0x049E);
            }
            else if (_castSpell == 48 && !PortalTied)
            {
                R.WeenieError(0x04A3);
            }
            else if (_castSpell == 47)
            {
                // Primary Portal Tie at a portal (M5 3 p1): ACE's text, or the scripted refusal; the player does not move.
                if (TieRefusal != 0)
                {
                    R.WeenieError(TieRefusal);
                }
                else
                {
                    R.Raw(AceWireWriters.ServerMessage("You have successfully linked with the portal.", 0));
                }
            }
            else
            {
                _teleportAt = Now + 2.0;
                _teleportCell = _castSpell == 48 ? DenCell : HoltburgCell;
            }

            R.UseDone();
        }

        for (var i = 0; i < _places.Count; i++)
        {
            var (handle, to, at, answered) = _places[i];
            if (answered || Now < at + 0.5)
            {
                continue;
            }

            _places[i] = (handle, to, at, true);
            var p = to.Location.Position;
            var data = new PlaceData("loc", to.Name, "0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture), p.Local.X, p.Local.Y, p.Local.Z, 1, 0, 0, 0);
            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success("a" + handle.Id.ToString(CultureInfo.InvariantCulture), ToolVerbs.Place, data), data.Describe()));
            _teleportAt = Now + 1.0;
            _teleportCell = p.CellId;
        }

        // M5 3 p1: places made straight through the session's Tools (the build's tie leg, not the journey's seam) are answered the same way; the seam's own were recorded above.
        var requests = R.H.Net.ToolRequests();
        while (_toolsSeen < requests.Count)
        {
            var text = requests[_toolsSeen++];
            if (text is null || !text.StartsWith("@lifestone place ", StringComparison.Ordinal) || !ToolsWire.TryParseRequest(text["@lifestone ".Length..], out var request, out _) || request is not PlaceLocRequest loc
                || _places.Exists(p => "a" + p.Handle.Id.ToString(CultureInfo.InvariantCulture) == request.Id))
            {
                continue;
            }

            var at = loc.At;
            PlacedCells.Add(at.Cell);
            var placed = new PlaceData("loc", null, ToolsWire.FormatCell(at.Cell), at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz);
            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Place, placed), placed.Describe()));
            _teleportAt = Now + 1.0;
            _teleportCell = at.Cell;
        }

        if (R.H.Net.Of(FakeSendKind.Suicide).Count > _suicidesSeen)
        {
            _suicidesSeen++;
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_vital, 2, 0));
            R.PrivateInt(43, ++_deaths);
            _teleportAt = Now + 3.0;
            _teleportCell = HoltburgCell;
        }

        if (_teleportAt is { } tele && Now >= tele)
        {
            _teleportAt = null;
            Teleport(_teleportCell);
            _unhideAt = Now + 1.0;
        }

        if (_unhideAt is { } unhide && Now >= unhide)
        {
            _unhideAt = null;
            R.H.Unhide(++_stateStamp);
        }
    }
}

/// <summary>What a scripted station does: ends after <paramref name="Steps"/> steps, Succeeded or Failed, judged <paramref name="Ok"/>, with <paramref name="Fields"/>; <paramref name="OnEnd"/> runs at its end (a station that leaves the player elsewhere).</summary>
internal sealed record J1StationScript(int Steps = 3, bool Succeed = true, bool Ok = true, string Fields = "kills=1", string? Failure = null, int Granted = 0, Action<IGameSessionView>? OnEnd = null);

/// <summary>A scripted station (M4 P5b J1).</summary>
internal sealed class J1Station(string name, StationNeeds needs, J1StationScript script, List<string> started) : IStation
{
    public string Name => name;

    public StationNeeds Needs => needs;

    public IStationRun Start(JourneyContext context)
    {
        started.Add(name);
        return new J1Run(name, script, context);
    }

    private sealed class J1Run(string name, J1StationScript script, JourneyContext context) : IStationRun
    {
        private int _steps;
        private ScenarioStatus _status = ScenarioStatus.Running;

        public string Name => name;

        public string Stage => _status == ScenarioStatus.Running ? "running" : "done";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => 0.5;

        public long Unmatched => 0;

        public bool Ok => _status == ScenarioStatus.Succeeded && script.Ok;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_status != ScenarioStatus.Running)
            {
                return _status;
            }

            if (++_steps < script.Steps)
            {
                return ScenarioStatus.Running;
            }

            context.Facts.Granted += script.Granted;
            script.OnEnd?.Invoke(session);
            Failure = script.Succeed ? null : script.Failure ?? "scripted failure";
            return _status = script.Succeed ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
        }

        public string VerdictFields() => script.Fields;
    }
}

/// <summary>A scripted station factory: every kind but <c>unknown</c> makes a <see cref="J1Station"/> with the script named after the station (default: passes).</summary>
internal sealed class J1Factory(Dictionary<string, J1StationScript>? scripts = null) : IStationFactory
{
    public List<string> Created { get; } = [];

    public List<string> Started { get; } = [];

    public IStation Create(StationSpec spec)
    {
        if (spec.Kind == "unknown")
        {
            throw new NotSupportedException("TODO(m4-journey-catalog): no station for kind 'unknown'");
        }

        Created.Add(spec.Name);
        return new J1Station(spec.Name, spec.NeedsOverride ?? StationNeeds.None, scripts is not null && scripts.TryGetValue(spec.Name, out var s) ? s : new J1StationScript(), Started);
    }
}

/// <summary>An observer that records its calls (<c>starting:&lt;name&gt;</c>, <c>ended:&lt;name&gt;:&lt;ok&gt;</c>).</summary>
internal sealed class J1Observer : IJourneyObserver
{
    public List<string> Calls { get; } = [];

    public void StationStarting(string station, double now) => Calls.Add("starting:" + station);

    public void StationEnded(string station, bool ok, double now) => Calls.Add($"ended:{station}:{(ok ? "true" : "false")}");

    public void StationEvent(string station, string name, double now) => Calls.Add($"event:{station}:{name}");
}

/// <summary>A scripted runner host: each run arrives after <see cref="Steps"/> steps (or fails when <see cref="Fail"/>) with route fields.</summary>
internal sealed class J1Runner : IJourneyRunnerHost
{
    public List<TravelRequest> Requests { get; } = [];

    public int Steps { get; set; } = 4;

    public bool Fail { get; set; }

    public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now)
    {
        Requests.Add(request);
        return new Leg(this);
    }

    private sealed class Leg(J1Runner host) : ITravelLeg
    {
        private int _steps;

        public TravelMeans Means => TravelMeans.Run;

        public string? Failure { get; private set; }

        public string Fields => "route_m=120.5 run_s=30.0 rungs=0 blocked=0";

        public TravelStatus Step(IGameSessionView session, double now)
        {
            if (++_steps < host.Steps)
            {
                return TravelStatus.Running;
            }

            if (host.Fail)
            {
                Failure = "travel:run:blocked";
                return TravelStatus.Failed;
            }

            return TravelStatus.Arrived;
        }

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose()
        {
        }
    }
}
