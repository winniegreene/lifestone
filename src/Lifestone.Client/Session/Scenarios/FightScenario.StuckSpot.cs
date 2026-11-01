using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7i (D139 g, the rehearsal of 2026-10-30): the walk out of a stuck spot. Legs begun from about the same spot (<see cref="StuckSpotMetres"/>) that are given up twice in a row mean the spot itself holds the mage: the next move
/// is out of it, not another leg from it. The floor round it is made costly for the walk's planner for <see cref="StuckSpotAvoidSeconds"/> (<see cref="IFightWalk.AvoidSpot"/>), and the mage walks back along the way it came in by
/// (the newest of the walk's own breadcrumbs at least <see cref="StuckSpotEscapeMetres"/> off); with no breadcrumb, no leg to it, or an escape that ends short, the fight fails named and the autopilot's recovery recalls (home, then
/// the den travel to the ground's start). Every decision is logged.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>M5 7 P7i: legs begun within this many metres of each other start from "the same spot". Lifestone's own.</summary>
    public const double StuckSpotMetres = 4;

    /// <summary>M5 7 P7i: the give-ups in a row from the same spot after which the mage leaves it first. Two, the owner's brief.</summary>
    public const int StuckSpotGiveUps = 2;

    /// <summary>M5 7 P7i: the seconds the stuck spot's floor stays costly for the planner (ten minutes). Lifestone's own.</summary>
    public const double StuckSpotAvoidSeconds = 600;

    /// <summary>M5 7 P7i: the escape walks back to a breadcrumb at least this far from the spot. Lifestone's own.</summary>
    public const double StuckSpotEscapeMetres = 6;

    /// <summary>M5 7 P7i: the walk drops a breadcrumb each time the mage has moved this far from the last one. Lifestone's own.</summary>
    public const double BreadcrumbMetres = 2;

    /// <summary>M5 7 P7i: the breadcrumbs kept (the newest; about 60 m of walk). Lifestone's own.</summary>
    public const int BreadcrumbsKept = 32;

    /// <summary>M5 7 P7i: the reason the fight fails when the mage cannot walk out of a stuck spot (the autopilot's recovery then recalls).</summary>
    public const string StuckSpotFailure = "walk: stuck spot";

    private readonly List<CellFrame> _breadcrumbs = [];
    private CellFrame? _legFrom;       // where the leg under way began
    private CellFrame? _spotFrom;      // where the last given-up leg began
    private int _spotGiveUps;          // the give-ups in a row from about _spotFrom
    private bool _escaping;            // the leg under way is the walk out of a stuck spot
    private string? _spotRecall;       // set: the fight fails with it (the autopilot recalls)

    /// <summary>The walks out of a stuck spot begun this fight (report-only).</summary>
    public int StuckSpotEscapes { get; private set; }

    /// <summary>M5 7 P7i: a leg of the fight's own began (<see cref="StartWalk"/>, <see cref="StartWalkBack"/>): where it began; an escape the fight dropped (an attacker, a heal) is over.</summary>
    private void NoteLegFrom(IGameSessionView session)
    {
        _legFrom = session.State.Self.Position;
        _escaping = false;
    }

    /// <summary>M5 7 P7i: the fight fails with the stuck spot's reason; the autopilot's recovery recalls.</summary>
    private ScenarioStatus FailStuckSpot(IGameSessionView session, string why)
    {
        _spotRecall = null;
        return Fail(session, why);
    }

    /// <summary>M5 7 P7i: in the Walk phase, each frame: a breadcrumb every <see cref="BreadcrumbMetres"/> of the mage's way.</summary>
    private void NoteBreadcrumb(IGameSessionView session)
    {
        if (session.State.Self.Position is not { } here || _escaping)
        {
            return;
        }

        if (_breadcrumbs.Count > 0 && Metres(_breadcrumbs[^1], here) < BreadcrumbMetres)
        {
            return;
        }

        if (_breadcrumbs.Count >= BreadcrumbsKept)
        {
            _breadcrumbs.RemoveAt(0);
        }

        _breadcrumbs.Add(here);
    }

    /// <summary>
    /// M5 7 P7i: a leg was given up (<see cref="WalkStuckGivesUp"/>). Counts the give-ups in a row from about the same spot; at <see cref="StuckSpotGiveUps"/> the spot is patched and the walk out of it begins (true), or the recall is
    /// asked (<see cref="_spotRecall"/>, true). An escape leg given up asks the recall at once.
    /// </summary>
    private bool StuckSpotAfterGiveUp(IGameSessionView session, double clock)
    {
        var here = session.State.Self.Position;
        if (_escaping)
        {
            _escaping = false;
            _spotRecall = $"{StuckSpotFailure}: the walk out of the spot near {Where(here)} stuck too; the autopilot's recall takes the mage out (P7i)";
            _log.Warn(LogCategory, "walk: " + _spotRecall);
            return true;
        }

        var from = _legFrom ?? here;
        if (from is not { } start)
        {
            return false;
        }

        _spotGiveUps = _spotFrom is { } last && Metres(last, start) <= StuckSpotMetres ? _spotGiveUps + 1 : 1;
        _spotFrom = start;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walk: stuck spot: give-up {_spotGiveUps} in a row of legs begun near {Where(start)} (within {StuckSpotMetres:0} m; out of the spot first at {StuckSpotGiveUps}) (P7i)"));
        if (_spotGiveUps < StuckSpotGiveUps || here is not { } at)
        {
            return false;
        }

        _spotGiveUps = 0;
        if (_walk is null)
        {
            return false;
        }

        if (!_walk.AvoidSpot(in at, clock + StuckSpotAvoidSeconds, out var patch))
        {
            _log.Warn(LogCategory, "walk: stuck spot: the floor is not patched: " + patch);
        }

        CellFrame? crumb = null;
        for (var i = _breadcrumbs.Count - 1; i >= 0; i--)
        {
            if (Metres(_breadcrumbs[i], at) >= StuckSpotEscapeMetres)
            {
                crumb = _breadcrumbs[i];
                break;
            }
        }

        var why = string.Create(CultureInfo.InvariantCulture, $"{StuckSpotGiveUps} legs begun near {Where(start)} were given up in a row");
        if (crumb is not { } back)
        {
            _spotRecall = $"{StuckSpotFailure}: {why} and no breadcrumb lies {StuckSpotEscapeMetres:0} m or more from {Where(at)}; the autopilot's recall takes the mage out (P7i)";
            _log.Warn(LogCategory, "walk: " + _spotRecall);
            return true;
        }

        if (!_walk.BeginLegTo(session, clock, in back, out var leg))
        {
            _spotRecall = $"{StuckSpotFailure}: {why} and no walk back to {Where(back)} could be planned ({leg}); the autopilot's recall takes the mage out (P7i)";
            _log.Warn(LogCategory, "walk: " + _spotRecall);
            return true;
        }

        _escaping = true;
        StuckSpotEscapes++;
        _walks++;
        (_legPurpose, _legNote, _legGuid, _walkBackCorpse) = (TourPurpose.None, "out of a stuck spot", 0u, 0u);
        _legFrom = at;
        _target = 0;
        _phase = Phase.Walk;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walking: purpose=escape ({why}; out of the spot first: back along the way in to {Where(back)}, {Metres(back, at):0.#} m off) (P7i); {leg}"));
        return true;
    }

    /// <summary>M5 7 P7i: in the Walk phase of an escape, each frame: the leg's end says whether the mage is out (true when it ended the walk; <see cref="_spotRecall"/> set when it ended short).</summary>
    private bool EscapeStep(IGameSessionView session, FightWalkStatus status)
    {
        if (!_escaping || status != FightWalkStatus.Arrived)
        {
            return false;
        }

        _escaping = false;
        _missesInARow = 0;
        _phase = Phase.Choose;
        var here = session.State.Self.Position;
        var away = here is { } h && _spotFrom is { } spot ? Metres(h, spot) : double.NaN;
        if (away >= StuckSpotMetres)
        {
            _spotFrom = null;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walk: out of the stuck spot: {away:0.#} m from it at {Where(here)}; the fight picks again (P7i)"));
            return true;
        }

        _spotRecall = string.Create(CultureInfo.InvariantCulture, $"{StuckSpotFailure}: the walk out of the spot ended {away:0.#} m from it at {Where(here)}; the autopilot's recall takes the mage out (P7i)");
        _log.Warn(LogCategory, "walk: " + _spotRecall);
        return true;
    }

    /// <summary>M5 7 P7i: a leg arrived away from the stuck spot: the give-ups in a row start again.</summary>
    private void SpotLegArrived(IGameSessionView session)
    {
        if (_spotFrom is { } spot && session.State.Self.Position is { } here && Metres(here, spot) > StuckSpotMetres)
        {
            _spotFrom = null;
            _spotGiveUps = 0;
        }
    }

    private static double Metres(in CellFrame a, in CellFrame b) => a.ToGlobal().DistanceTo(b.ToGlobal());

    private static string Where(CellFrame? at) => at is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"0x{p.CellId:X8} [{p.Origin.X:0.#} {p.Origin.Y:0.#} {p.Origin.Z:0.#}]")
        : "an unknown position";
}
