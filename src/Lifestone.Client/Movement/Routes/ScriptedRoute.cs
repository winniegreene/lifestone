using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Routes;

/// <summary>One goal of a <see cref="ScriptedRoute"/>: a place to walk to, and what to do on arrival.</summary>
/// <param name="Target">Where to go (the frame's rotation is ignored; only the cell and origin count).</param>
/// <param name="Tolerance">Arrival radius in metres, measured in the horizontal plane across landblocks.</param>
/// <param name="Stop">True to stop on arrival (release every movement key) rather than walk on to the next goal.</param>
/// <param name="DwellSeconds">How long to stand after stopping (the scenarios wait 1.5 s, so the 1.0 s cadence has sent the rest position).</param>
public readonly record struct RouteGoal(CellFrame Target, float Tolerance, bool Stop, double DwellSeconds);

/// <summary>
/// A walk for <see cref="RouteDriver"/>: goals in order, repeated <paramref name="Laps"/> times. The DoD 8 scenarios
/// (three Holtburg laps, the Academy exit) come from <see cref="RouteLibrary"/>.
/// </summary>
/// <param name="Name">The route's name, as the movement summary line prints it (<c>route=</c>).</param>
/// <param name="Goals">The goals of one lap.</param>
/// <param name="Laps">How many times the goals are walked; at least 1.</param>
public sealed record ScriptedRoute(string Name, IReadOnlyList<RouteGoal> Goals, int Laps = 1)
{
    /// <summary>
    /// Where the player should stand before the walk starts (the scenarios teleport there with the server mod's
    /// <c>place loc</c>), or null when the route starts wherever the player is.
    /// </summary>
    public CellFrame? Start { get; init; }
}

/// <summary>What <see cref="RouteDriver.Tick"/> reports.</summary>
public enum RouteStatus
{
    /// <summary>Still walking or dwelling.</summary>
    Running,

    /// <summary>Every goal of every lap was reached.</summary>
    Done,

    /// <summary>No progress toward the current goal for 3 s; <see cref="RouteDriver.FailureReason"/> names the cell and position.</summary>
    Stuck,

    /// <summary>The route cannot continue (for example the player was teleported away); see <see cref="RouteDriver.FailureReason"/>.</summary>
    Failed,
}
