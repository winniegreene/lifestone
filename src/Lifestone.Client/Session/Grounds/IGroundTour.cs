using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// The one seam every walk of a hunting ground takes its next waypoint from (M5 item 2 g5; the headless fight's <c>GroundWalk</c>, the game's <c>GameFightWalk</c> and the journey host's den walk). A walk asks
/// for a waypoint when it begins a leg, plans a walk to it and, when none joins the player to it, asks again; it never decides the order itself. <see cref="ProfileTour"/> is the first implementation (the profile's
/// tour order, kept for the tests and the arena run); <see cref="GroundTour"/> (g6) is the ground's tour over its respawn schedule, which the walks take for a dungeon (<see cref="GroundTour.For"/>). Nothing here is one dungeon's: the walkable cells, the order and the timing all come from the <see cref="HuntingGround"/>.
/// </summary>
/// <remarks>Game thread only. An implementation may keep state (a cursor, the slots it has seen); one instance serves one walk.</remarks>
public interface IGroundTour
{
    /// <summary>The most distinct waypoints <see cref="Next"/> can offer for one leg: a walk that planned no walk to this many in a row has none to go to.</summary>
    int Waypoints { get; }

    /// <summary>
    /// The cell of the waypoint the player goes to next from <paramref name="from"/>, or null when the ground has no waypoint besides <paramref name="from"/>. Each call is a new offer: a walk that finds no walk to it asks again.
    /// A tour that means the player to wait where it stands (2-R3: the cell it stands in holds a slot about to come back) offers <paramref name="from"/> itself, a hold (<see cref="Purpose"/> <see cref="TourPurpose.Hold"/>): the runner's walk (<c>RunnerLegs</c>) makes no leg and asks again after a short wait (F5); a walk that does not read the purpose plans a leg to that room's middle, which is short.
    /// </summary>
    /// <param name="from">The cell the player stands in (never offered back).</param>
    /// <param name="now">The session clock in seconds.</param>
    uint? Next(uint from, double now);

    /// <summary>A leg to <paramref name="cell"/> was abandoned (the runner blocked, stalled, or ran over its time) and the walk goes on; a tour that schedules by waypoint may leave it out for a while. The default does nothing.</summary>
    /// <param name="cell">The waypoint the failed leg was going to.</param>
    /// <param name="now">The session clock in seconds.</param>
    void LegFailed(uint cell, double now)
    {
    }

    /// <summary>The leg to <paramref name="cell"/> arrived (before the dwell); a tour that measures the run speed takes the leg's time. The default does nothing.</summary>
    /// <param name="cell">The waypoint the leg arrived at.</param>
    /// <param name="now">The session clock in seconds.</param>
    void LegArrived(uint cell, double now)
    {
    }

    /// <summary>
    /// F5: the leg to <paramref name="cell"/> arrived after <paramref name="walkSeconds"/> of the runner's own walking (from the leg's start of movement to its arrival, the time the runner was paused or yielded to the server's
    /// MoveTo left out); a tour that measures the run speed takes this time rather than the offer-to-arrival time. The default calls <see cref="LegArrived"/>.
    /// </summary>
    /// <param name="cell">The waypoint the leg arrived at.</param>
    /// <param name="now">The session clock in seconds.</param>
    /// <param name="walkSeconds">The runner's walking seconds of this leg.</param>
    void LegWalked(uint cell, double now, double walkSeconds) => LegArrived(cell, now);

    /// <summary>The purpose of the last offer of <see cref="Next"/> (F5, D47: every walk has a named reason). The default is <see cref="TourPurpose.None"/>, a tour that names none.</summary>
    TourPurpose Purpose => TourPurpose.None;

    /// <summary>What the last offer is for, for the leg's log line (the target, the slot and its due time, the hold's slot); empty when the tour names none.</summary>
    string PurposeNote => string.Empty;

    /// <summary>M5 7 P7b (D139 g): the creature the last offer walks toward (purpose <see cref="TourPurpose.Seen"/>), or 0 for none. The default is 0.</summary>
    uint PurposeGuid => 0;

    /// <summary>
    /// M5 7 P7b (D139 g): the fight gave up a walk toward creature <paramref name="guid"/> (the walk stuck with no way past); a tour that walks toward seen creatures leaves it out for a while. The default does nothing.
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="now">The session clock in seconds.</param>
    void Avoid(uint guid, double now)
    {
    }

    /// <summary>
    /// F5: what the player sees, every frame of the fight: the cell the player stands in and the session's object table. A tour with a schedule takes the slots in view with no creature of their own as seen empty
    /// (<see cref="RespawnSchedule.Look"/>) and walks toward the living targets the table holds. The default does nothing.
    /// </summary>
    /// <param name="cell">The cell the player stands in.</param>
    /// <param name="objects">The session's object table.</param>
    /// <param name="now">The session clock in seconds.</param>
    void Look(uint cell, WorldObjectTable objects, double now)
    {
    }

    /// <summary>The fight killed a creature (its 0x01AD notice): its weenie class id, the cell it died in and its guid (g6, 2-R1: the respawn schedule pairs it with its slot by the spawn it remembers for the guid, else by the cell). The default does nothing.</summary>
    /// <param name="wcid">The creature's weenie class id.</param>
    /// <param name="cell">The cell it died in.</param>
    /// <param name="now">The session clock in seconds.</param>
    /// <param name="guid">The creature's guid, or 0 when unknown.</param>
    void Killed(uint wcid, uint cell, double now, uint guid = 0)
    {
    }

    /// <summary>A creature of the ground was created or came into sight (a CreateObject): its weenie class id, cell and guid (g6: a slot seen again stands; 2-R1: a create in a slot's cell is remembered for that slot). The default does nothing.</summary>
    /// <param name="wcid">The creature's weenie class id.</param>
    /// <param name="cell">The cell it was created in.</param>
    /// <param name="now">The session clock in seconds.</param>
    /// <param name="guid">The creature's guid, or 0 when unknown.</param>
    void Created(uint wcid, uint cell, double now, uint guid = 0)
    {
    }

    /// <summary>F4: the fight starts, and the creatures already in the session's object table were created before the tour existed, so no create reached it: a tour with a schedule takes each one that stands in a slot's cell as that slot's creature (so its kill pairs by its spawn). The default does nothing.</summary>
    /// <param name="objects">The session's object table.</param>
    /// <param name="now">The session clock in seconds.</param>
    void Seed(WorldObjectTable objects, double now)
    {
    }
}

/// <summary>Why the tour offers a waypoint (F5, D47: the character always moves with purpose), logged at the leg's start and counted on the fight's line (<c>walks_by_purpose=</c>).</summary>
public enum TourPurpose
{
    /// <summary>The tour names no purpose (a tour of the tests that offers cells by hand).</summary>
    None,

    /// <summary>A living target the object table holds: walk toward it (ranked before any presumed slot).</summary>
    Seen,

    /// <summary>A slot standing by the schedule but not yet seen: walk to look.</summary>
    Presumed,

    /// <summary>A slot due within the walk plus the margin, or the one walk to where the earliest due slot is in view before a hold.</summary>
    Due,

    /// <summary>The timed return to the arrival's storey (D25).</summary>
    Return,

    /// <summary>No purpose to walk: the tour holds where it stands (the offer is the player's own cell, and the walk makes no leg).</summary>
    Hold,

    /// <summary>The profile's tour order (<see cref="ProfileTour"/>: the arena run and scripted walks, no schedule).</summary>
    Order,
}

/// <summary>The first <see cref="IGroundTour"/>: the profile's tour (<see cref="HuntingGround.Tour"/>) in its own order, round and round, skipping the cell the player stands in. Any ground's; no schedule.</summary>
public sealed class ProfileTour : IGroundTour
{
    private readonly IReadOnlyList<uint> _tour;
    private int _next;

    /// <summary>A tour over <paramref name="ground"/>'s waypoints, from the first.</summary>
    /// <param name="ground">The ground.</param>
    public ProfileTour(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        _tour = ground.Tour;
    }

    /// <inheritdoc/>
    public int Waypoints => _tour.Count;

    /// <inheritdoc/>
    public TourPurpose Purpose => TourPurpose.Order;

    /// <inheritdoc/>
    public string PurposeNote => "the profile's tour order (no schedule)";

    /// <inheritdoc/>
    public uint? Next(uint from, double now)
    {
        for (var tries = 0; tries < _tour.Count; tries++)
        {
            var cell = _tour[_next++ % _tour.Count];
            if (cell != from)
            {
                return cell;
            }
        }

        return null;
    }
}
