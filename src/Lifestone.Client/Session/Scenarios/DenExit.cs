using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The way out of the Black Spawn Den before a run ends (the fight and death scenarios, on success and on failure): a <c>PlayerTools.Place</c> to Holtburg's point of interest, the teleport's arrival with the player present,
/// then <see cref="SettleSeconds"/>. Best effort and bounded: a place that is not confirmed, or an arrival later than <see cref="TeleportSeconds"/>, is a warning and the run goes on to its end where the player stands.
/// </summary>
/// <remarks>
/// <para>A mage logged off in landblock 0x0105 meets the Tusker Slaves at its next login, unbuffed when a death loop left it so (the r3 live failure of M3 3.2, m3-plan section 8: a gear run after a failed death run began among
/// the tuskers); BSD Mage is never reset, so every such login costs vitae and dropped items. Both scenarios run this before their log off, and before they return Failed or <c>done</c> when the host owns the session
/// (<c>endSession: false</c>: the game's legs log off right after).</para>
/// <para>One <see cref="Begin"/> a run (a second call does nothing), <see cref="Tick"/> every frame until it says the leave is over. The tick reads the place's handle, the caller's count of the player's own teleport positions and
/// the travel state, and allocates nothing: the log lines are on the transitions.</para>
/// </remarks>
public sealed class DenExit
{
    /// <summary>How long the player is present at Holtburg before the run goes on, in seconds (the scenarios' settle).</summary>
    public const double SettleSeconds = 3;

    /// <summary>The limit on the place and its teleport, in seconds (portal space takes 5 s or more).</summary>
    public const double TeleportSeconds = RoutePlay.TeleportSeconds;

    private readonly ILogSink _log;
    private readonly string _category;
    private readonly TeleportDestination _holtburg;
    private ActionHandle? _pending;
    private int _teleportMark;
    private double _start;
    private double _settleAt = double.NaN;
    private bool _begun;
    private bool _done;

    /// <summary>The way out, logging as <paramref name="category"/>.</summary>
    /// <param name="log">Where it logs.</param>
    /// <param name="category">The scenario's log category.</param>
    public DenExit(ILogSink log, string category)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(category);
        (_log, _category) = (log, category);
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
    }

    /// <summary>Where the leave goes: Holtburg's point of interest (the death scenario's bind goes to the same place).</summary>
    public TeleportDestination Destination => _holtburg;

    /// <summary>True once <see cref="Begin"/> sent the place: the leave is attempted once a run.</summary>
    public bool Begun => _begun;

    /// <summary>True once the leave is over (arrived and settled, refused, or out of time).</summary>
    public bool Done => _done;

    /// <summary>True when the player is in the world in the den's landblock (0x0105).</summary>
    /// <param name="session">The session.</param>
    public static bool InDen(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.State.Phase == SessionPhase.InWorld && session.State.Self.Position is { } here && (here.CellId >> 16) == BlackSpawnDen.Landblock;
    }

    /// <summary>Sends the place when the player is in the den and no leave was begun; the caller then ticks until <see cref="Tick"/> says the leave is over.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock now.</param>
    /// <param name="selfTeleports">The caller's count of the player's own teleport positions (<c>PositionVerdict.SelfTeleport</c>); the arrival is a count above this one.</param>
    /// <returns>True when a place went out; false when the player is not in the den or a leave was begun already (nothing to tick).</returns>
    public bool Begin(IGameSessionView session, double clock, int selfTeleports)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_begun || !InDen(session))
        {
            return false;
        }

        _begun = true;
        _start = clock;
        _teleportMark = selfTeleports;
        _settleAt = double.NaN;
        _log.Info(_category, "leaving the den before the log off (the next login must not start among the tuskers)");
        _pending = session.Tools.Place(_holtburg, ActionSource.Host);
        return true;
    }

    /// <summary>One frame of the leave.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock now.</param>
    /// <param name="selfTeleports">The caller's count of the player's own teleport positions.</param>
    /// <returns>True once the leave is over: the player arrived and <see cref="SettleSeconds"/> passed, the place was not confirmed, or <see cref="TeleportSeconds"/> ran out (the last two logged as warnings); also true when no leave was begun.</returns>
    public bool Tick(IGameSessionView session, double clock, int selfTeleports)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_done || !_begun)
        {
            return true;
        }

        if (_pending is { Outcome: { } placed })
        {
            _pending = null;
            if (placed.Kind != ActionOutcomeKind.Confirmed)
            {
                _log.Warn(_category, "leaving the den: the place was not confirmed (" + Describe(placed) + "); the run ends where it stands");
                return Finish();
            }
        }

        if (_pending is null && selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)
        {
            if (double.IsNaN(_settleAt))
            {
                _settleAt = clock + SettleSeconds;
            }

            if (clock < _settleAt)
            {
                return false;
            }

            var cell = session.State.Self.Position?.CellId ?? 0;
            if (InDen(session))
            {
                _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"leaving the den: the place landed the player in cell 0x{cell:X8}, still in landblock 0x0105; the run ends where it stands"));
            }
            else
            {
                _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"left the den: the player is present in cell 0x{cell:X8} after {clock - _start:0.0} s"));
            }

            return Finish();
        }

        if (clock - _start > TeleportSeconds)
        {
            _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"leaving the den: the player did not arrive within {TeleportSeconds:0.#} s (travel {session.State.Self.Travel}); the run ends where it stands"));
            return Finish();
        }

        return false;
    }

    private bool Finish()
    {
        _done = true;
        _pending = null;
        return true;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
}
