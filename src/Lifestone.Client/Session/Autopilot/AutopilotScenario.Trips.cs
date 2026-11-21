using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 16 E5d (docs/automation-engine.md section 2.5; m6-plan.md D139): the outer loop and the session's trip desk (<see cref="TripDesk"/>).
/// <para><b>The one set of counters.</b> A scenario is built before its first step, so it states its counters first (<see cref="TripDesk.Stated"/>: its ground, its floor-only lines, its order lines) and the
/// desk adopts them at the first step of a session's read model (<see cref="BindTrips"/>): on a ground the session has not counted they become the session's; on one it has (a scenario rebuilt on the same ground, at a
/// hand-back) the session's are taken and the scenario states its own order lines and trip mark on them, which is what its own new counters held before this slice. A reconnect's new read model adopts the
/// counters the scenario holds, so the schedule's mark survives it as it did.</para>
/// <para><b>The verdict a trip carries.</b> Where the trigger calls a trip (the counters' verdict, the restockable stop, the hour, the buff bot's lines, a lapsed line) it states what it decided to the desk
/// (<see cref="CallTrip(IGameSessionView, LogisticsVerdict)"/>); the logistics station takes it. A frame of the fight that asks nothing, and a death, void a call no station took.</para>
/// <para><b>The read that moves nothing.</b> The audit's kit read, the buy-trip's decision that follows it and the panel ask the desk's reading twin (<see cref="TripReader"/>); the trigger alone checks the counters that
/// decide.</para>
/// </summary>
public sealed partial class AutopilotScenario
{
    private GameState? _tripState;
    private TripDesk? _trips;

    /// <summary>Takes the session's one set of counters for the job's ground, once a session's read model.</summary>
    /// <param name="session">The session.</param>
    private void BindTrips(IGameSessionView session)
    {
        if (ReferenceEquals(_tripState, session.State))
        {
            return;
        }

        _tripState = session.State;
        SaleDesk.For(session.State).StateJob(_context.Character, _context.Spells, _context.Facts); // M6 16 E5c2: who she is, the SpellTable and the job's facts, for the rule book's verdict outside a trip (the sale estimate, the audit's gate, the field's make-room rule)
        _trips = TripDesk.For(session.State);
        var stated = _counters;
        _counters = _trips.Adopt(_ground, stated, _floorOnly.Keys, m => _log.Info(LogCategory, m));
        if (!ReferenceEquals(stated, _counters))
        {
            _counters.OrderLines = stated.OrderLines;
            _counters.MarkTrip(stated.CyclesAtTrip); // the schedule's mark is the scenario's own count of buff cycles: a rebuilt scenario counts from its own start, as its own counters did
        }
    }

    /// <summary>
    /// The trigger calls a trip on the counters' verdict or the hour's: what it decided is made into the order the trip carries (<see cref="TripDesk.Call(Grounds.HuntingGround, LogisticsVerdict, double)"/>), once, and
    /// said. Allocates: only where a trip is called, never in a check that calls none.
    /// </summary>
    private void CallTrip(IGameSessionView session, LogisticsVerdict verdict) => SayCall((_trips ?? TripDesk.For(session.State)).Call(_ground, verdict, _clock));

    /// <summary>The trigger calls a trip no counters call (the buff bot's, a lapsed line's): the order carries the caller's name with the counts and the purse as they stand.</summary>
    private void CallTrip(IGameSessionView session, string trigger, string reason) => SayCall((_trips ?? TripDesk.For(session.State)).Call(_ground, trigger, reason, _clock));

    private void SayCall(TripOrder order)
    {
        _botHourWent = null; // the next trip's reason is logged afresh
        _log.Info(LogCategory, $"autopilot fight: trip: the verdict the trip carries: {order.Said}");
    }

    /// <summary>
    /// The reading twin of the session's counters over the job's ground (<see cref="TripDesk.Reading"/>): a check through it moves nothing the trigger reads. Before the first step, the counters the job states (nothing
    /// decides yet).
    /// </summary>
    internal LogisticsCounters TripReader => _trips?.Reading(_ground) ?? _counters;
}
