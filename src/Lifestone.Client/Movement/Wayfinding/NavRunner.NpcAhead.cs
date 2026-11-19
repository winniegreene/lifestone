using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// M6 sprint (the owner's decision of 2026-11-19: she must not run headfirst into the Scrivener of Item Magic): an NPC body the walk comes to see on the route ahead after its plan. The plan reads the
/// bodies from where the walk starts (<see cref="NpcBodies.Refresh"/>), so one beyond the object table's reach then is not on it: live, run 5 (cache/m6/sprint/run5/step3-levelup.log) planned the walk to the
/// Apprentice Scrivener of Life Magic 265 m away at 14:24:58 UTC, ran into the Scrivener of Item Magic outside that building's door at 14:25:55, climbed four rungs between him and the wall, and replanned round him
/// only at 14:26:09 (why=stuck, his body stamped at 1.0 m); run 3 met the same scrivener (2026-11-19 01:52 UTC). Each <see cref="CreatureRefreshSeconds"/> the bodies near the player are read again: one the
/// last plan did not stamp whose disc the route's next <see cref="CreatureLookAheadMetres"/> passes through is a replan (<c>why=npc</c>, its body stamped by the plan's own rule), at most one each
/// <see cref="CreatureReplanSeconds"/> and once a body a run (<c>m6-npc-ahead</c>). Not retail: the player walked round an NPC himself; the constants are the creature layer's.
/// </summary>
public sealed partial class NavRunner
{
    private readonly NpcBodies _npcAhead = new();
    private readonly HashSet<uint> _npcReplannedFor = [];
    private double _nextNpcAheadCheck;
    private double _lastNpcReplan = double.NegativeInfinity;

    /// <summary>M6 sprint: the replans this run made for an NPC body seen on the route ahead after the plan (<c>m6-npc-ahead</c>).</summary>
    public int NpcReplans { get; private set; }

    private void ResetNpcAhead(double now)
    {
        _npcReplannedFor.Clear();
        _nextNpcAheadCheck = now;
        _lastNpcReplan = double.NegativeInfinity;
        NpcReplans = 0;
    }

    /// <summary>The step's read of the bodies on the route ahead; true when it replanned (the caller takes the new route).</summary>
    private bool StepNpcBodies(double now, in CellFrame position, NavPoint here, NavRoute route, float s)
    {
        if (BodyFeed is not { } feed || _paths.Planner is null || _goal is not { } goal || now < _nextNpcAheadCheck || now - _lastNpcReplan < CreatureReplanSeconds)
        {
            return false;
        }

        _nextNpcAheadCheck = now + CreatureRefreshSeconds;
        _npcAhead.Refresh(feed, here.Global, CreatureLookAheadMetres + NpcBodies.RouteMetres, goal.Guid ?? 0, goal.Point.Global, _paths.Map);
        var seen = _npcAhead.Seen;
        for (var i = 0; i < seen.Count; i++)
        {
            var body = seen[i];
            if (_npcs.IsStamped(body.Guid) || _npcReplannedFor.Contains(body.Guid) || !RouteMeetsBody(route, s, body))
            {
                continue;
            }

            _npcReplannedFor.Add(body.Guid);
            _lastNpcReplan = now;
            NpcReplans++;
            _log.Info(
                LogCategory,
                "an NPC on the route ahead that the plan did not know: replan round it (m6-npc-ahead)",
                new LogField("name", Options.Name),
                new LogField("npc", string.Create(CultureInfo.InvariantCulture, $"0x{body.Guid:X8}:{body.Name}")),
                new LogField("metres", Vector2.Distance(here.Global, body.Centre)),
                new LogField("disc_m", NpcBodies.DiscRadius(body.Radius)));
            Plan(now, position, replan: true, "npc");
            return true;
        }

        return false;
    }

    /// <summary>Whether the route's next <see cref="CreatureLookAheadMetres"/> from <paramref name="s"/> passes within <paramref name="body"/>'s disc, less the graze <see cref="NpcBodies.MeetSlackMetres"/> the plan allows.</summary>
    private static bool RouteMeetsBody(NavRoute route, float s, in NavBody body)
    {
        var reach = NpcBodies.DiscRadius(body.Radius) - NpcBodies.MeetSlackMetres;
        var end = s + CreatureLookAheadMetres;
        var from = route.PointAt(s);
        for (var i = 1; i < route.Vertices.Count; i++)
        {
            if (route.ArcLengthAt(i) <= s)
            {
                continue;
            }

            var to = route.ArcLengthAt(i) > end ? route.PointAt(end) : route.Vertices[i];
            if (NpcBodies.SegmentDistance(body.Centre, from.Global, to.Global) <= reach)
            {
                return true;
            }

            if (route.ArcLengthAt(i) > end)
            {
                break;
            }

            from = to;
        }

        return false;
    }
}
