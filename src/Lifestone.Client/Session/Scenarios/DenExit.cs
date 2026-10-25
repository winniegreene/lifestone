using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The way out of the Black Spawn Den before a run ends (the fight and death scenarios, on success and on failure): a <c>PlayerTools.Place</c> to Holtburg's point of interest (since M4 P5b, through the travel seam with
/// <see cref="TravelPurpose.Home"/>: <see cref="PlaceTravel"/> by default, Lifestone Recall in a journey), the teleport's arrival with the player present, then <see cref="SettleSeconds"/>. Best effort and bounded: a place that is not confirmed, or an arrival later than <see cref="TeleportSeconds"/>, is a warning and the run goes on to its end where the player stands.
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
    private readonly SeamLeg _seam;
    private int _teleportMark;
    private double _start;
    private double _settleAt = double.NaN;
    private bool _begun;
    private bool _done;

    /// <summary>The way out, logging as <paramref name="category"/>.</summary>
    /// <param name="log">Where it logs.</param>
    /// <param name="category">The scenario's log category.</param>
    /// <param name="travel">M4 P5b: the travel seam the leave goes through (<see cref="TravelPurpose.Home"/>), or null for today's place (<see cref="PlaceTravel"/>).</param>
    public DenExit(ILogSink log, string category, IJourneyTravel? travel = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(category);
        (_log, _category) = (log, category);
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
        _seam = new SeamLeg(travel ?? new PlaceTravel());
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
        _seam.Start(session, new TravelRequest(_holtburg, TravelPurpose.Home), clock);
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

        var travel = _seam.Step(session, clock);
        if (travel == TravelStatus.Failed)
        {
            _log.Warn(_category, (_seam.Means == TravelMeans.Place ? "leaving the den: the place was not confirmed (" : "leaving the den: the travel failed (") + _seam.Failure + "); the run ends where it stands");
            return Finish();
        }

        if (travel == TravelStatus.Arrived && (_seam.Authoritative || (selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)))
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

        if (_seam.Means == TravelMeans.Place && clock - _start > TeleportSeconds)
        {
            _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"leaving the den: the player did not arrive within {TeleportSeconds:0.#} s (travel {session.State.Self.Travel}); the run ends where it stands"));
            return Finish();
        }

        return false;
    }

    private bool Finish()
    {
        _done = true;
        _seam.Abandon();
        return true;
    }
}

/// <summary>
/// M4 P5b: one travel through the seam (<see cref="IJourneyTravel"/>) at a scenario's choke point, stepped every frame: the death and fight scenarios' places, the corpse trip and <see cref="DenExit"/>. It keeps the
/// arrival contract of <see cref="ITravelLeg"/>: for <see cref="TravelMeans.Place"/> an <see cref="TravelStatus.Arrived"/> means only the place was confirmed and the caller's existing teleport check decides; for
/// Run, Recall and None (<see cref="Authoritative"/>) the arrival is the player at the destination and the caller waits for no teleport. Once the leg ends, <see cref="Step"/> keeps answering its last status.
/// </summary>
/// <param name="travel">The seam.</param>
internal sealed class SeamLeg(IJourneyTravel travel)
{
    private ITravelLeg? _leg;
    private TravelStatus _last = TravelStatus.Running;

    /// <summary>The seam this leg goes through.</summary>
    public IJourneyTravel Travel { get; } = travel ?? throw new ArgumentNullException(nameof(travel));

    /// <summary>How the current (or last) leg moves the player; <see cref="TravelMeans.Place"/> before the first.</summary>
    public TravelMeans Means { get; private set; } = TravelMeans.Place;

    /// <summary>The last request started, or null.</summary>
    public TravelRequest? Request { get; private set; }

    /// <summary>Why the last leg failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The last leg's verdict fields (a run's or a recall's), or empty.</summary>
    public string Fields { get; private set; } = string.Empty;

    /// <summary>True when the last leg's arrival is authoritative (Run, Recall, None): no teleport is waited for.</summary>
    public bool Authoritative => Means is TravelMeans.Run or TravelMeans.Recall or TravelMeans.None;

    /// <summary>Starts a travel (a leg still running is disposed first).</summary>
    public void Start(IGameSessionView session, TravelRequest request, double now)
    {
        Abandon();
        Request = request;
        Failure = null;
        Fields = string.Empty;
        _last = TravelStatus.Running;
        _leg = Travel.GoTo(session, request, now);
        Means = _leg.Means;
    }

    /// <summary>One frame of the leg; after it ended, its last status.</summary>
    public TravelStatus Step(IGameSessionView session, double now)
    {
        if (_leg is null)
        {
            return _last;
        }

        _last = _leg.Step(session, now);
        if (_last != TravelStatus.Running)
        {
            Failure = _last == TravelStatus.Failed ? _leg.Failure ?? "travel: the leg failed without a reason (TODO(m4-travel-no-reason))" : null;
            Fields = _leg.Fields;
            _leg.Dispose();
            _leg = null;
        }

        return _last;
    }

    /// <summary>Stops a leg still running (its keys released) and forgets it.</summary>
    public void Abandon()
    {
        _leg?.Dispose();
        _leg = null;
    }
}
