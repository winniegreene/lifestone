using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>Why a scenario travels (the travel seam decides the means from it: M4 P5b 3.4).</summary>
public enum TravelPurpose
{
    /// <summary>To the hunting ground or the den (the journey's one budgeted teleport, or a recall).</summary>
    Ground,

    /// <summary>A hunt's seek: next to a creature it saw (on foot in a journey: closes <c>m4-seek-terrain-only</c>).</summary>
    Seek,

    /// <summary>A hunt's next cell (on foot in a journey).</summary>
    Cell,

    /// <summary>Home to Holtburg (Lifestone Recall in a journey).</summary>
    Home,

    /// <summary>To a shop (on foot from the lifestone in a journey).</summary>
    Vendor,

    /// <summary>To the player's corpse (on foot over the room graph in a journey).</summary>
    Corpse,

    /// <summary>To a fight spot inside the den (on foot in a journey).</summary>
    Spot,

    /// <summary>To Holtburg's lifestone to bind (on foot in a journey).</summary>
    Bind,
}

/// <summary>How a travel leg moved the player (the verdict's <c>teleports=</c> and <c>recalls=</c> count by it).</summary>
public enum TravelMeans
{
    /// <summary>The mod's <c>place</c> (a teleport; budgeted in a journey).</summary>
    Place,

    /// <summary>On foot: the wayfinder's runner through the player's keys.</summary>
    Run,

    /// <summary>A recall spell (Lifestone Recall, Primary Portal Recall).</summary>
    Recall,

    /// <summary>Nothing to do: the player already stands at the destination.</summary>
    None,
}

/// <summary>Where a travel leg stands after a step.</summary>
public enum TravelStatus
{
    /// <summary>Still moving: step it again next frame.</summary>
    Running,

    /// <summary>Arrived (see <see cref="ITravelLeg"/> for what arrival means for each means).</summary>
    Arrived,

    /// <summary>Stopped on a failure (<see cref="ITravelLeg.Failure"/> says why); a refusal of the teleport budget is one.</summary>
    Failed,
}

/// <summary>
/// A travel request at one of the scenarios' teleport choke points (M4 P5b 3.4: <c>HuntScenario.BeginPlace</c>,
/// <c>DeathScenario</c>'s places, <c>FightScenario</c>'s place, <c>GroundExit</c>).
/// </summary>
/// <param name="Destination">Where to: the same destination the scenario places at today.</param>
/// <param name="Purpose">Why (the seam picks the means from it).</param>
/// <param name="ObjectGuid">An object goal (a creature, a vendor, a portal), or 0 for a point goal.</param>
/// <param name="Range">For an object goal: arrive within this range, in metres.</param>
/// <param name="NeedSight">For an object goal: arrive with a clear line of sight to it (the missile pick: closes <c>m4-missile-clear-line</c>).</param>
public sealed record TravelRequest(TeleportDestination Destination, TravelPurpose Purpose, uint ObjectGuid = 0, double Range = 0, bool NeedSight = false);

/// <summary>
/// One travel in progress. The scenario steps it every frame until it ends.
/// <para><b>The arrival contract</b> (the scenarios keep today's checks for a place): for <see cref="TravelMeans.Place"/>,
/// <see cref="TravelStatus.Arrived"/> means only that the place action was Confirmed; the scenario's own existing check
/// (the teleport seen, travel Present in the destination's landblock, the settle) still decides the arrival, exactly as
/// before the seam. For <see cref="TravelMeans.Run"/>, <see cref="TravelMeans.Recall"/> and <see cref="TravelMeans.None"/>,
/// Arrived is authoritative: the player stands at the destination (a run within its tolerance or object range; a recall
/// present in the destination's landblock) and the scenario goes on without waiting for a teleport.</para>
/// </summary>
public interface ITravelLeg : IDisposable
{
    /// <summary>How this leg moves the player.</summary>
    TravelMeans Means { get; }

    /// <summary>Runs one frame of the leg.</summary>
    TravelStatus Step(IGameSessionView session, double now);

    /// <summary>Why the leg failed (<c>travel:&lt;means&gt;:&lt;reason&gt;</c>, e.g. <c>travel:place:over-budget</c>), or null.</summary>
    string? Failure { get; }

    /// <summary>The leg's verdict fields (a run's <c>route_m= run_s= rungs= blocked=</c>; a recall's <c>recalls= recall_s=</c>), or empty.</summary>
    string Fields { get; }

    /// <summary>
    /// True when the leg failed because a run reached its object range without the clear line of sight the request needs (the runner's <c>NavRunState.ArrivedWithoutSight</c>):
    /// the typed end the hunt's sightless seek reads (M4 P5b fix FA, P5b-R5; never the failure's text). False for every other end and for the legs that do not run.
    /// </summary>
    bool ArrivedWithoutSight => false;

    /// <summary>Releases the keys and holds the route (a station's interruption); a run only.</summary>
    void Pause();

    /// <summary>Resumes after <see cref="Pause"/> (a run replans when 3 m off).</summary>
    void Resume(double now);
}

/// <summary>
/// The travel seam (M4 P5b 3.4): injected at the scenarios' teleport choke points. The default, <see cref="PlaceTravel"/>,
/// is today's <c>Tools.Place</c>, so <c>--scenario=hunt</c> and the leg runners behave as before; a journey injects its own
/// (the runner on foot, a recall, the budgeted teleport; over budget is refused loudly).
/// </summary>
public interface IJourneyTravel
{
    /// <summary>Starts a travel; the caller steps the returned leg until it ends and disposes it.</summary>
    ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now);

    /// <summary>True while a leg this seam started is running.</summary>
    bool Busy { get; }
}

/// <summary>The default travel seam: today's <c>Tools.Place</c> for every request (no budget, no runner). One per scenario (<see cref="Busy"/> is its own leg's).</summary>
public sealed class PlaceTravel : IJourneyTravel
{
    private PlaceLeg? _leg;

    /// <inheritdoc />
    public bool Busy => _leg is { Done: false };

    /// <inheritdoc />
    public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        _leg = new PlaceLeg(session.Tools.Place(request.Destination, ActionSource.Host), request.Destination.Name);
        return _leg;
    }

    private sealed class PlaceLeg(ActionHandle handle, string name) : ITravelLeg
    {
        public bool Done { get; private set; }

        public TravelMeans Means => TravelMeans.Place;

        public string? Failure { get; private set; }

        public string Fields => string.Empty;

        public TravelStatus Step(IGameSessionView session, double now)
        {
            if (handle.Outcome is not { } outcome)
            {
                return TravelStatus.Running;
            }

            Done = true;
            if (outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                return TravelStatus.Arrived;
            }

            Failure ??= $"travel:place:{name}: {outcome.Kind}" + (outcome.Code == 0 ? string.Empty : $" (code 0x{outcome.Code:X})") + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
            return TravelStatus.Failed;
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
