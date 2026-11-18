using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Dat;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 body (live ladder49 2026-11-17 01:58:53 UTC, <c>m6-body-apart</c>): which walk a station's fight gets, and why, logged once a fight. One world picture: a host that draws the player's body
/// (<see cref="IJourneyWalks.DrawsBody"/>, the game) gives its own walk over that body for ANY ground the player stands on (a quest's hunt-until ground included), and when it cannot, the fight has no walk and fails by name: the offline
/// <see cref="GroundWalk"/> would bind a second, undrawn walker over the session's movement and move the server player 40 m from the body on screen (the Crater Caves' mote hunt). The offline walker is the headless and scripted hosts' only:
/// a host with no walks of its own, or a headless host that gave none.
/// </summary>
public static class FightWalkChoice
{
    /// <summary>The log category of the choice's line.</summary>
    public const string LogCategory = "fight-walk";

    /// <summary>The TODO a drawn-body host's missing walk names.</summary>
    public const string NoDrawnWalkTodo = "m6-fight-walk-drawn-body";

    /// <summary>The walks a choice can make.</summary>
    public enum Kind
    {
        /// <summary>No walk: the fight fails its first leg by name.</summary>
        None,

        /// <summary>The outdoor ground's cells through the journey's travel seam (the host's runner, the drawn body in the game).</summary>
        Outdoor,

        /// <summary>The host's own walk over its body (the game's <c>GameFightWalk</c> over the drawn player, or the headless body's).</summary>
        Host,

        /// <summary>The offline ground walker (<see cref="GroundWalk"/>), for a host that draws no body.</summary>
        Offline,
    }

    /// <summary>The station's fight walk for <paramref name="ground"/>, logged with its kind and its reason.</summary>
    /// <param name="context">The journey context (its runners host and its travel).</param>
    /// <param name="ground">The ground the fight runs in.</param>
    /// <param name="dats">The dats (the offline and outdoor walks read them), or null.</param>
    /// <param name="station">The station's name, for the log line.</param>
    public static IFightWalk? For(JourneyContext context, HuntingGround ground, DatSet? dats, string station)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ground);
        if (FightTargets.IsOutdoorFight(ground))
        {
            var outdoor = dats is null ? null : OutdoorFightWalk.Create(dats, ground, context.Travel, context.Log);
            Log(context.Log, station, ground, outdoor is null ? Kind.None : Kind.Outdoor, outdoor is null ? "an outdoor ground and no dats to read its terrain" : "an outdoor ground: its cells walked as point goals through the journey's travel seam (the host's runner)");
            return outdoor;
        }

        var (kind, why) = Choose(context.Runners as IJourneyWalks, out var walk, () => (context.Runners as IJourneyWalks)?.FightWalk(context, ground), () => dats is null ? null : GroundWalk.Create(dats, ground, context.Log));
        Log(context.Log, station, ground, kind, why);
        return walk;
    }

    /// <summary>
    /// The rule (engine-free and testable): the host's walk when it gives one; none when the host draws the body and gave none (never the offline walker under a drawn body); the offline walker for a host with no walks or a headless host that gave none.
    /// </summary>
    /// <param name="walks">The host's walks, or null (a scripted host).</param>
    /// <param name="walk">The walk chosen, or null.</param>
    /// <param name="host">Makes the host's walk (null when it has none for the ground).</param>
    /// <param name="offline">Makes the offline walker (null without dats).</param>
    /// <returns>The kind and the reason, for the log.</returns>
    public static (Kind Kind, string Why) Choose(IJourneyWalks? walks, out IFightWalk? walk, Func<IFightWalk?> host, Func<IFightWalk?> offline)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(offline);
        if (walks is not null && host() is { } own)
        {
            walk = own;
            return (Kind.Host, walks.DrawsBody ? "the host's walk over the drawn player (the body the screen draws is the one that walks)" : "the host's walk over its own body");
        }

        if (walks is { DrawsBody: true })
        {
            walk = null;
            return (Kind.None, $"the host draws the player and made no walk for this ground; the offline walker is not used under a drawn body (it would move the server player apart from the screen's) (TODO({NoDrawnWalkTodo}))");
        }

        walk = offline();
        return walk is null
            ? (Kind.None, walks is null ? "a host with no walks and no dats for the offline walker" : "the headless host made no walk and there are no dats for the offline walker")
            : (Kind.Offline, walks is null ? "a host with no walks of its own (a scripted host): the offline ground walker" : "the headless host made no walk: the offline ground walker");
    }

    private static void Log(ILogSink log, string station, HuntingGround ground, Kind kind, string why)
    {
        var line = $"fight walk: station {station} ground={ground.Key} walk={kind.ToString().ToLowerInvariant()}: {why}";
        if (kind == Kind.None)
        {
            log.Warn(LogCategory, line);
        }
        else
        {
            log.Info(LogCategory, line);
        }
    }
}
