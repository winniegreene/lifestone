using Lifestone.Client.Movement;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What <see cref="BodyApartWatch"/> found: the gap, the limit it passed, how long the server has said nothing new, and the two positions.</summary>
/// <param name="Metres">The metres between the body and the server's last position.</param>
/// <param name="Limit">The limit for the body's cell (<see cref="BodyApartWatch.Limit"/>).</param>
/// <param name="HeldSeconds">The seconds the gap held with no new position from the server.</param>
/// <param name="Server">Where the session last heard the server has the player.</param>
/// <param name="Body">Where the client's own body stands.</param>
public readonly record struct BodyApart(double Metres, double Limit, double HeldSeconds, CellFrame Server, CellFrame Body);

/// <summary>
/// M6 7 AJ (<c>m6-body-apart</c>; gate A's climb, 2026-11-06): the check that the client's body and the server still move together. The server echoes each position the client sends, and retail's constraint holds the body to the last
/// echo: it starts slowing the body at <see cref="ServerPositionPolicy.StartConstraintDistance"/> and stops it at <see cref="ServerPositionPolicy.MaxConstraintDistance"/>. So when the echoes stop, the body runs out a leash of 50 m (20 m in
/// a room), reads as stuck against nothing, and every later leg waits on <see cref="Scenarios.Journey.JourneySettleGate"/> for a server position that never comes. The named distance is the constraint's start for the body's cell (10 m
/// outdoors, 5 m in a room): past it the body is already being held back. A running body passes it only between two echoes, so the rule asks for <see cref="HoldSeconds"/> with the gap past the limit and no new position from the server at
/// all. Pure: no log, no session.
/// </summary>
public sealed class BodyApartWatch
{
    /// <summary>The seconds the gap must hold with no new position from the server (Lifestone's own: ACE echoes each position, about one a second on the run, so this is many echoes missed; live, the body met the leash's end 27 s in).</summary>
    public const double HoldSeconds = 15;

    private CellFrame? _lastServer;
    private double _since;

    /// <summary>True while the last read found the body past the limit from the server's position.</summary>
    public bool Apart { get; private set; }

    /// <summary>The metres of the last read with both positions, else NaN.</summary>
    public double LastMetres { get; private set; } = double.NaN;

    /// <summary>The named distance for a body in <paramref name="cellId"/>: where the constraint starts slowing it (10 m outdoors, 5 m in a room).</summary>
    /// <param name="cellId">The body's cell.</param>
    public static double Limit(uint cellId) => ServerPositionPolicy.StartConstraintDistance(cellId);

    /// <summary>
    /// One read. Null while all is well or the hold is not over; the finding once the gap has held <see cref="HoldSeconds"/> with no new position from the server (and then again each <see cref="HoldSeconds"/> while it lasts). No server
    /// position or no placed body (a login, portal space, a placement pending) is no reading: the watch starts again.
    /// </summary>
    /// <param name="server">The session's position for the player, or null when it is not present.</param>
    /// <param name="body">The client's body, or null when it is not placed or the host cannot say.</param>
    /// <param name="now">The session clock's seconds.</param>
    public BodyApart? Step(CellFrame? server, CellFrame? body, double now)
    {
        if (server is not { } at || body is not { } own)
        {
            Reset();
            return null;
        }

        var metres = at.ToGlobal().DistanceTo(own.ToGlobal());
        LastMetres = metres;
        var limit = Limit(own.CellId);
        if (metres <= limit)
        {
            Reset(keepMetres: true);
            return null;
        }

        Apart = true;
        if (_lastServer != at)
        {
            _lastServer = at; // the first sight of the gap, or a new position from the server: the server is talking, so the hold starts again
            _since = now;
            return null;
        }

        var held = now - _since;
        if (held < HoldSeconds)
        {
            return null;
        }

        _since = now;
        return new BodyApart(metres, limit, held, at, own);
    }

    /// <summary>Forgets the gap (the player left the world, or a re-login put the body where the server has it).</summary>
    /// <param name="keepMetres">True to keep <see cref="LastMetres"/>.</param>
    public void Reset(bool keepMetres = false)
    {
        Apart = false;
        _lastServer = null;
        if (!keepMetres)
        {
            LastMetres = double.NaN;
        }
    }
}
