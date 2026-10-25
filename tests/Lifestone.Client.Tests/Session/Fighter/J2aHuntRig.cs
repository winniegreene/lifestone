using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.State;
using Lifestone.Client.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b lane J2a: what the hunt's travel-seam rows add to <see cref="HuntScriptRig"/> (a new partial; nothing of the rig's existing behaviour changes): the player walked by a position update (no teleport, no place), the
/// walk's arrival outside Holtburg placing the cell's creatures as a place's arrival does (<see cref="HuntScriptRig.OnBelt"/>), and the stamina set at once or later.
/// </summary>
internal sealed partial class HuntScriptRig
{
    private int _j2aWalkArrivals;

    /// <summary>The walks that arrived outside Holtburg's landblock (the run travel's arrivals; <see cref="BeltArrivals"/> counts the place-loc teleports only).</summary>
    public int J2aWalkArrivals => _j2aWalkArrivals;

    /// <summary>The player moved to <paramref name="at"/> by a position update, as the runner's keys and ACE's echo of them end: no teleport, no place.</summary>
    public void J2aWalkTo(CellPosition at)
    {
        var stamps = Session.State.Self.Stamps;
        _where = new AcePosition(at.CellId, at.Local.X, at.Local.Y, at.Local.Z);
        Fighter.InHoltburg = (at.CellId >> 16) == 0xA9B4;
        Sink.OnUpdatePosition(Ctx, new UpdatePositionMsg(Self, PositionFlags.Grounded, new AcPosition(at.CellId, at.Local, Quaternion.Identity), null, null, stamps.Instance, unchecked((ushort)(stamps.Position + 1)), stamps.Teleport, stamps.ForcePosition));
    }

    /// <summary>The walk to <paramref name="cell"/> arrived: outside Holtburg the cell's creatures are placed (<see cref="OnBelt"/> with the walk's number).</summary>
    public void J2aArrived(uint cell)
    {
        if ((cell >> 16) != 0xA9B4)
        {
            OnBelt?.Invoke(this, ++_j2aWalkArrivals);
        }
    }

    /// <summary>The hunter's stamina set to <paramref name="value"/> now.</summary>
    public void J2aStamina(uint value) => Vital(Stamina, value);

    /// <summary>The hunter's stamina set to <paramref name="value"/> after <paramref name="after"/> session seconds.</summary>
    public void J2aStaminaLater(double after, uint value) => At(after, () => Vital(Stamina, value));

    /// <summary>The places the session's action log holds (<see cref="ActionKind.ToolPlace"/>): the "no silent place" count.</summary>
    public int J2aPlaces()
    {
        var log = Session.ActionLog;
        var count = 0;
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Kind == ActionKind.ToolPlace)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// M4 P5b lane J2a: a recording travel seam. Each request is recorded; with <see cref="Inner"/> set the leg is that seam's (the default <see cref="PlaceTravel"/>: today's place), else a run leg that walks the player to the
/// destination through <see cref="HuntScriptRig.J2aWalkTo"/> and arrives (authoritatively, the seam's arrival contract) once the session holds the player in the destination's cell; <see cref="FailWhen"/> fails a leg.
/// </summary>
internal sealed class J2aRecordingTravel : IJourneyTravel
{
    private ITravelLeg? _leg;

    /// <summary>The rig whose player the run legs walk (set after the rig is made: the scenario is built inside it).</summary>
    public HuntScriptRig? Rig { get; set; }

    /// <summary>A seam to hand the requests to (a <see cref="PlaceTravel"/>), or null for the run legs.</summary>
    public IJourneyTravel? Inner { get; init; }

    /// <summary>The means of the run legs (<see cref="TravelMeans.Run"/>; <see cref="TravelMeans.None"/> arrives at once without moving).</summary>
    public TravelMeans Means { get; init; } = TravelMeans.Run;

    /// <summary>Fails the leg of a request it holds for (with <c>travel:run:test-refusal</c>).</summary>
    public Func<TravelRequest, bool>? FailWhen { get; init; }

    /// <summary>The failure text <see cref="FailWhen"/> gives (F3: a run that ended ArrivedWithoutSight).</summary>
    public string FailureText { get; init; } = "travel:run:test-refusal";

    /// <summary>A failed leg's typed end (<see cref="ITravelLeg.ArrivedWithoutSight"/>, M4 P5b fix FA): true makes each <see cref="FailWhen"/> failure a sightless arrival whatever its text.</summary>
    public bool Sightless { get; init; }

    /// <summary>Called with each request as its leg starts (fix FA: what the hunt had sent before the travel), or null.</summary>
    public Action<TravelRequest>? OnRequest { get; init; }

    /// <summary>The requests, in order.</summary>
    public List<TravelRequest> Requests { get; } = [];

    /// <summary>The means of each leg, in order.</summary>
    public List<TravelMeans> LegMeans { get; } = [];

    /// <inheritdoc />
    public bool Busy => Inner?.Busy ?? _leg is RunLeg { Done: false };

    /// <inheritdoc />
    public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
    {
        Requests.Add(request);
        OnRequest?.Invoke(request);
        _leg = Inner is { } inner ? inner.GoTo(session, request, now) : new RunLeg(this, request);
        LegMeans.Add(_leg.Means);
        return _leg;
    }

    private sealed class RunLeg(J2aRecordingTravel travel, TravelRequest request) : ITravelLeg
    {
        private bool _walked;

        public bool Done { get; private set; }

        public TravelMeans Means => travel.Means;

        public string? Failure { get; private set; }

        public string Fields => "route_m=0 run_s=0 rungs=0 blocked=no";

        public bool ArrivedWithoutSight => travel.Sightless && Failure is not null;

        public TravelStatus Step(IGameSessionView session, double now)
        {
            if (travel.FailWhen?.Invoke(request) == true)
            {
                Done = true;
                Failure = travel.FailureText;
                return TravelStatus.Failed;
            }

            if (Means == TravelMeans.None)
            {
                Done = true;
                return TravelStatus.Arrived;
            }

            var at = request.Destination.Location.Position;
            if (!_walked && request.ObjectGuid != 0 && session.State.Self.Position is { } from && session.State.Objects.TryGet(request.ObjectGuid, out var goal) && goal.Position is { } there
                && from.ToGlobal().DistanceTo(there.ToGlobal()) <= request.Range)
            {
                Done = true; // the object goal is already within its range (the runner's no-move arrival)
                return TravelStatus.Arrived;
            }

            if (!_walked)
            {
                _walked = true;
                travel.Rig!.J2aWalkTo(at);
                return TravelStatus.Running;
            }

            if (session.State.Self.Position is { } here && here.CellId == at.CellId)
            {
                Done = true;
                travel.Rig!.J2aArrived(at.CellId);
                return TravelStatus.Arrived;
            }

            return TravelStatus.Running;
        }

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose() => Done = true;
    }
}

/// <summary>M4 P5b lane J2a: a session view that forwards to the rig's session once it exists (the keeper is built with the hunt's options, before the rig's runner makes the session).</summary>
internal sealed class J2aLateView(Func<IGameSessionView> view) : IGameSessionView
{
    public GameState State => view().State;

    public IEventBus Events => view().Events;

    public IActionApi Actions => view().Actions;

    public PlayerTools Tools => view().Tools;

    public SessionClock Clock => view().Clock;

    public ActionLog ActionLog => view().ActionLog;

    public AttackSequence Attack => view().Attack;

    public LinkStats Link => view().Link;
}
