using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>Which way a rung change goes (M6 item 4 t2).</summary>
public enum RungDirection
{
    /// <summary>Out to the ground's entry (the rung's ground).</summary>
    Out,

    /// <summary>Home to the travel file's start (Holtburg's lifestone): the trips' way home.</summary>
    Home,
}

/// <summary>Where the character stands when the rung change starts (the station reads it from the body's landblock).</summary>
public enum RungWhere
{
    /// <summary>In a landblock of the travel file's start place (Holtburg).</summary>
    Home,

    /// <summary>Anywhere else (a known travel place, or an unknown landblock).</summary>
    Elsewhere,

    /// <summary>In one of the ground's own landblocks.</summary>
    AtGround,
}

/// <summary>The kinds of a rung change's legs, in the order a plan may hold them.</summary>
public enum RungLegKind
{
    /// <summary>Lifestone Recall to the start's lifestone (the way home when the spellbook holds it).</summary>
    LifestoneRecall,

    /// <summary>The ground's Primary Portal Recall (the ground is known tied): lands at the ground's arrival.</summary>
    PortalRecall,

    /// <summary>One portal of the travel file (or the ground's exit portal): walk to its front, Use, arrive in its destination landblock.</summary>
    Hop,

    /// <summary>On foot to the ground entry's front point (the profile's <see cref="GroundEntry.FrontCell"/> and <see cref="GroundEntry.Front"/>).</summary>
    Run,

    /// <summary>The entry's level gate, read again at run time (<see cref="GroundEntry.Admits"/>).</summary>
    Gate,

    /// <summary>Primary Portal Tie on the entry portal, "linked with the portal" read before the Use.</summary>
    Tie,

    /// <summary>Use the ground's entry portal and arrive in the ground's arrival landblock.</summary>
    Enter,

    /// <summary>On foot to the home lifestone's point (an outdoor ground hunted from home on foot).</summary>
    WalkHome,
}

/// <summary>A portal a leg uses: where it stands, where it drops, its gate (every value from the travel file or the ground profile; none is a constant).</summary>
/// <param name="Key">The edge's key, or <c>entry:&lt;ground&gt;</c> / <c>exit:&lt;ground&gt;</c>.</param>
/// <param name="Name">The portal's name for the log.</param>
/// <param name="Wcid">The portal weenie.</param>
/// <param name="Guid">The instance guid, or 0 when unknown (found by weenie near <paramref name="Origin"/>).</param>
/// <param name="Cell">The stored cell (an outdoor row may carry 0xLLLL0000).</param>
/// <param name="Origin">The landblock-local point.</param>
/// <param name="DestLandblock">The landblock the Use must arrive in.</param>
/// <param name="MinLevel">The lowest level it admits, or null.</param>
/// <param name="MaxLevel">The highest level it admits, or null.</param>
/// <param name="FromLandblocks">The landblocks the body must stand in before the walk (the edge's From place), or empty when not checked.</param>
/// <param name="Facing">The unit vector the portal faces on the ground plane (the placement's rotation, <see cref="PortalFacing.Forward"/>), or null when the file gives none: the hop's front point is then on the approach side (M6 4 T3a).</param>
/// <param name="WalkOut">M6 8 B1: the edge's walk out of the drop's room (<see cref="TravelEdge.WalkOut"/>), or null.</param>
/// <param name="WalkIn">M6 8 B1: the edge's way to its portal's room (<see cref="TravelEdge.WalkIn"/>), or null.</param>
public sealed record RungPortal(string Key, string Name, uint Wcid, uint Guid, uint Cell, Vector3 Origin, uint DestLandblock, int? MinLevel, int? MaxLevel, IReadOnlyList<ushort> FromLandblocks, Vector2? Facing = null, IReadOnlyList<TravelVia>? WalkOut = null, IReadOnlyList<TravelVia>? WalkIn = null)
{
    /// <summary>True when <paramref name="level"/> passes the gate (ACE <c>Portal.CheckUseRequirements</c>, @95ef426d WorldObjects/Portal.cs:166-176).</summary>
    /// <param name="level">The character's level.</param>
    public bool Admits(int level) => (MinLevel is not { } min || level >= min) && (MaxLevel is not { } max || level <= max);

    /// <summary>The refusal's name for <paramref name="level"/>: <c>gate:&lt;key&gt;:&lt;level&gt;&lt;&lt;min&gt;</c> or <c>gate:&lt;key&gt;:&lt;level&gt;&gt;&lt;max&gt;</c>, or null when admitted.</summary>
    /// <param name="level">The character's level.</param>
    public string? Refusal(int level) => RungRoute.GateRefusal(Key, level, MinLevel, MaxLevel);

    /// <summary>How near two records of a portal stand, in metres: the same object named twice (an edge, a route's run target, a profile's entry). Not retail: Lifestone's own.</summary>
    private const float SamePortalMetres = 0.5f;

    private static readonly Lazy<KeptPortal[]> Known = new(KnownPortals);

    /// <summary>Every portal the travel data knows: the travel file's edges and its routes' run targets, the ground profiles' entries, portals and exit portals, and every portal placed in the world (<see cref="WorldPortals"/>, M6 14 m6-14-plaza), each once.</summary>
    internal static IReadOnlyList<KeptPortal> AllKnown => Known.Value;

    /// <summary>
    /// M6 7 AI: what a walk to this portal keeps clear of: every other portal the travel data knows (<see cref="PortalKeepOff"/>). This one is left out, so the walk's end is never refused: found by its place, since an exit's record has no guid.
    /// </summary>
    public PortalKeepOff KeepOffTheOthers()
    {
        var self = Kept(Name, Cell, Origin).Global;
        return new PortalKeepOff(Known.Value.Where(p => Vector2.Distance(p.Global, self) > SamePortalMetres));
    }

    /// <summary>
    /// M6 14 (portal overshoot; live 2026-11-13 19:20 UTC, the Tusker Barracks' front): what the walk to this portal's front point keeps clear of: every other portal (<see cref="KeepOffTheOthers"/>) and this one too, as the walk's own
    /// (<see cref="KeptPortal.Own"/>): kept off by its touch and her stop (the runner sets the reach from the goal, <see cref="NavRunner.StopMetres"/>), so the front is reached without crossing the portal and without stopping in its touch.
    /// The seek to the portal keeps <see cref="KeepOffTheOthers"/>: it goes to the portal's use range, which is inside that reach.
    /// </summary>
    public PortalKeepOff KeepOffForFront()
    {
        var self = Kept(Name, Cell, Origin);
        return new PortalKeepOff(Known.Value.Where(p => Vector2.Distance(p.Global, self.Global) > SamePortalMetres).Append(self with { Own = true }));
    }

    /// <summary>M6 14 (m6-14-plaza): this portal's point in global metres (<see cref="NavPoint.Global"/>).</summary>
    public Vector2 Global() => Kept(Name, Cell, Origin).Global;

    private static KeptPortal Kept(string name, uint cell, Vector3 origin)
    {
        var at = new NavPoint(cell & 0xFFFF0000u, origin);
        return new KeptPortal(name, at.Global, origin.Z, (cell & 0xFFFF) < 0x100);
    }

    private static KeptPortal[] KnownPortals()
    {
        var routes = TravelRoutes.Default;
        var all = new List<KeptPortal>(routes.Edges.Select(e => Kept(e.Name, e.Cell, e.Origin)));
        foreach (var key in routes.GroundKeys)
        {
            all.AddRange(routes.RoutesFor(key).Where(r => r.Run is not null).Select(r => Kept(r.Run!.EntryName, r.Run.EntryCell, r.Run.EntryOrigin)));
        }

        foreach (var g in GroundCatalog.All)
        {
            if (g.Entry is { } entry)
            {
                all.Add(Kept(g.Name + " entry", entry.Cell, entry.Origin));
            }

            if (g.Portal is { } portal)
            {
                all.Add(Kept(g.Name + " portal", portal.Cell, portal.Origin));
            }

            if (g.Exit is { Kind: GroundExitKind.Portal, Origin: { } exit } section)
            {
                all.Add(Kept(g.Name + " exit", section.Cell, exit));
            }
        }

        // M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:29:41 UTC: the walk to the Yinar Portal's front kept off its own portal alone and touched Sand's Edge Portal beside it): and every portal placed in the world (WorldPortals), after the
        // travel data's records so a portal both name keeps the travel data's name.
        all.AddRange(WorldPortals.All.Select(p => Kept(p.Name, p.Cell, p.Origin)));

        var once = new List<KeptPortal>();
        var grid = new Dictionary<(int X, int Y), List<int>>();
        foreach (var p in all)
        {
            var key = ((int)MathF.Floor(p.Global.X), (int)MathF.Floor(p.Global.Y));
            var same = false;
            for (var dx = -1; dx <= 1 && !same; dx++)
            {
                for (var dy = -1; dy <= 1 && !same; dy++)
                {
                    same = grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var near) && near.Exists(i => Vector2.Distance(once[i].Global, p.Global) <= SamePortalMetres);
                }
            }

            if (!same)
            {
                if (!grid.TryGetValue(key, out var cell))
                {
                    grid[key] = cell = [];
                }

                cell.Add(once.Count);
                once.Add(p);
            }
        }

        return [.. once];
    }

    /// <summary>The portal of a travel edge.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="routes">The travel file (its places give the From landblocks).</param>
    public static RungPortal Of(TravelEdge edge, TravelRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(routes);
        return new(edge.Key, edge.Name, edge.Wcid, edge.Guid, edge.Cell, edge.Origin, edge.DestCell >> 16, edge.MinLevel, edge.MaxLevel, routes.PlaceOf(edge.From)?.Landblocks ?? [], edge.Facing?.Forward, edge.WalkOut, edge.WalkIn);
    }
}

/// <summary>One leg of a rung change.</summary>
/// <param name="Kind">What the leg does.</param>
/// <param name="Label">The edge's key or the leg's word (the <c>edge=</c> field).</param>
/// <param name="Portal">The portal a Hop, Tie or Enter uses, else null.</param>
/// <param name="Spell">The spell a recall or a tie casts, else 0.</param>
/// <param name="Cell">The cell a Run or WalkHome walks to, or the landblock (as <c>0xLLLL0000</c>) a recall lands in.</param>
/// <param name="At">The point a Run or WalkHome walks to.</param>
/// <param name="Via">M6 7 Z: on a Run, the reason it is a via point of the travel file (<see cref="TravelViaPlan"/>): a bend on the way to the entry's front, never a stop; null on the run to the front.</param>
public sealed record RungLeg(RungLegKind Kind, string Label, RungPortal? Portal = null, uint Spell = 0, uint Cell = 0, Vector3 At = default, string? Via = null)
{
    /// <summary>The kind as the log writes it (<c>lifestone-recall</c>, <c>portal-recall</c>, <c>hop</c>, <c>run</c>, <c>gate</c>, <c>tie</c>, <c>enter</c>, <c>walk-home</c>).</summary>
    public string KindName => Kind switch
    {
        RungLegKind.LifestoneRecall => "lifestone-recall",
        RungLegKind.PortalRecall => "portal-recall",
        RungLegKind.Hop => "hop",
        RungLegKind.Run => "run",
        RungLegKind.Gate => "gate",
        RungLegKind.Tie => "tie",
        RungLegKind.Enter => "enter",
        RungLegKind.WalkHome => "walk-home",
        _ => Kind.ToString(),
    };
}

/// <summary>What <see cref="RungRoute.Decide"/> reads: where the character stands, its level and spellbook, the ground's tie state and the gem.</summary>
/// <param name="Ground">The rung's ground.</param>
/// <param name="Direction">Out to the ground, or home.</param>
/// <param name="Where">Where the body stands.</param>
/// <param name="Place">The travel place the body stands in (a place key, or the landblock in hex when no place holds it).</param>
/// <param name="Level">The character's level.</param>
/// <param name="KnowsLifestoneRecall">The spellbook holds Lifestone Recall 1635.</param>
/// <param name="KnowsPortalRecall">The spellbook holds the ground's Primary Portal Recall (the tie record's <see cref="GroundPortal.RecallSpell"/>, else 48).</param>
/// <param name="KnowsPortalTie">The spellbook holds Primary Portal Tie (the tie record's <see cref="GroundPortal.TieSpell"/>, else 47).</param>
/// <param name="Tie">What the journey knows of the ground's portal tie (<see cref="JourneyFacts.PortalTieOf"/>).</param>
/// <param name="HoldsGem">The pack holds the travel file's gem (<see cref="TravelRoutes.Gems"/>).</param>
/// <param name="BodyGround">M6 4 F1 (4-R3): the ground whose landblocks hold the body when it is not <paramref name="Ground"/> (another rung's dungeon, the outskirts; <see cref="RungRoute.BodyGroundOf"/>), else null: the way home is planned
/// from it (its exit portal or its walk home), not from the run's ground.</param>
/// <param name="StopAtPlace">M6 6 C2: a travel place's key (<see cref="TravelPlace.Key"/>: the island, Holtburg, the hub) the rung change stops in, or null (today: the whole plan). Given, <see cref="RungRoute.Decide"/> keeps the legs up to and including the
/// first that arrives in one of the place's landblocks (a hop or an enter by its <see cref="RungPortal.DestLandblock"/>, a recall by its landing) and drops the rest (the run, the gate, the tie, the entry's Use): the corpse trip of a death on a leg.</param>
/// <param name="FailedRoutes">M6 7 M: the keys of the ground's routes that failed on a hop this run (<see cref="JourneyFacts.FailedRoutesOf"/>): planned last, <see cref="TravelRoutes.FirstAdmitted"/>'s <c>avoid</c>; null or empty: none.</param>
public sealed record RungRouteInput(HuntingGround Ground, RungDirection Direction, RungWhere Where, string Place, int Level, bool KnowsLifestoneRecall, bool KnowsPortalRecall, bool KnowsPortalTie, PortalTie Tie, bool HoldsGem, HuntingGround? BodyGround = null, string? StopAtPlace = null, IReadOnlyCollection<string>? FailedRoutes = null);

/// <summary>M6 6 C2: the travel place that holds a corpse lost on a leg, or none, and why (<see cref="RungRoute.PlaceHolding"/>).</summary>
/// <param name="Key">The place's key, or null when no place holds it (the corpse is off the route: <c>trip_abort=corpse-off-route</c>).</param>
/// <param name="Why">The decision and its inputs in one sentence (logged).</param>
public sealed record LegPlace(string? Key, string Why);

/// <summary>A rung change's plan: the legs and the verdict's decided fields.</summary>
/// <param name="Direction">Out or home.</param>
/// <param name="GroundKey">The ground.</param>
/// <param name="RouteKey">The route's key (a travel route's, a way home's, <c>portal-recall</c>, <c>lifestone-recall</c>, <c>here</c>, or <c>none</c>).</param>
/// <param name="Legs">The legs in order.</param>
/// <param name="Tied">The verdict's <c>tied=</c>: <c>known</c> (recalled by the tie), <c>yes</c> (the plan ties), <c>no</c> (tieable, not tied), <c>untieable</c> (no tieable entry).</param>
/// <param name="Gem">The verdict's <c>gem=</c> as planned: <c>none</c> or <c>held</c> (the station says <c>used</c>).</param>
/// <param name="Gate">The verdict's <c>gate=</c>: <c>pass</c> or <c>refused:&lt;n&gt;</c> (n the bound the level failed).</param>
/// <param name="Stop">The named stop when the plan cannot run (a gate refusal, <c>TODO(m6-ground-route)</c>, <c>TODO(m6-way-home)</c>), else null.</param>
/// <param name="Why">The decision in one sentence (logged: the purpose, M5 D47).</param>
public sealed record RungPlan(RungDirection Direction, string GroundKey, string RouteKey, IReadOnlyList<RungLeg> Legs, string Tied, string Gem, string Gate, string? Stop, string Why)
{
    /// <summary>The portals the plan uses (its Hop and Enter legs).</summary>
    public int Hops => Legs.Count(l => l.Kind is RungLegKind.Hop or RungLegKind.Enter);
}

/// <summary>
/// The rung change's route as a pure rule (M6 item 4 t2, D50, D51): from where the character stands, its level, its spellbook, the ground's tie state and the gem, and the travel file's routes, the legs in order:
/// <list type="number">
/// <item>Out, at the ground already: no legs (<c>here</c>).</item>
/// <item>Out, the ground known tied (<see cref="PortalTie.Tied"/>) and its Primary Portal Recall known: the recall alone (<c>portal-recall</c>, <c>tied=known</c>), from anywhere.</item>
/// <item>Out, elsewhere: home first (the <see cref="RungDirection.Home"/> rule's legs), then the route.</item>
/// <item>The route: <see cref="TravelRoutes.FirstAdmitted"/> with the entry's own gate (a pending profile with no entry passes the portals' gate alone: the entry's is the server's, read at the Use); its edges as Hop legs; then, when the profile has an
/// entry, the run to its front point, its gate, the tie when <see cref="GroundEntry.Tieable"/>, Primary Portal Tie is known and the ground is not known tied, and the Use of the entry. A level no route admits is the named gate refusal
/// <c>gate:&lt;edge&gt;:&lt;level&gt;&lt;&lt;min&gt;</c> before any walk; a ground with no route is <c>TODO(m6-ground-route)</c>; a route with no steps and no run (the outskirts on foot, the den's own recall or tie) hands over to the ground's
/// own station (<c>den-travel</c>) with no legs.</item>
/// <item>Home: none at home; else Lifestone Recall when known (the climber learns it with Item Enchantment at 16, D43); else from the ground that holds the body (the run's ground, or another ground: <see cref="RungRouteInput.BodyGround"/>,
/// M6 4 F1, 4-R3) its exit portal (the profile's <c>exit</c>) or the walk home of an outdoor ground hunted from home on foot, then <see cref="TravelRoutes.WayHomeFrom"/> by portals (the Refuge's 42852, the hub's 42820); nothing known is
/// <c>TODO(m6-way-home)</c>; a way home whose first portal stands behind a lever-only door (<see cref="LeverDoors"/>, refused as <see cref="TownNetworkTrip"/> refuses it) is <c>TODO(door-lever)</c> (M6 4 F1, 4-R8).</item>
/// </list>
/// The gem is never planned: the station uses it only when a hop to its place failed by name and it is held (<c>gem=used</c>). No ground, town or gate fact lives here (D26): each comes from the profile or the travel file.
/// </summary>
public static class RungRoute
{
    /// <summary>The route key when the body already stands where the direction leads.</summary>
    public const string Here = "here";

    /// <summary>The route key of a tied ground's Primary Portal Recall.</summary>
    public const string PortalRecallRoute = "portal-recall";

    /// <summary>The route key of the way home by Lifestone Recall.</summary>
    public const string LifestoneRecallRoute = "lifestone-recall";

    /// <summary>The route key when nothing can be planned.</summary>
    public const string NoRoute = "none";

    /// <summary>The station a route with no steps and no run hands over to (the den's recall or tie, the outskirts' walk).</summary>
    public const string Handover = "den-travel";

    /// <summary>The gate refusal's name, or null when <paramref name="level"/> is admitted.</summary>
    /// <param name="key">The portal's key.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="min">The lowest level admitted, or null.</param>
    /// <param name="max">The highest level admitted, or null.</param>
    public static string? GateRefusal(string key, int level, int? min, int? max) =>
        min is { } lo && level < lo ? string.Create(CultureInfo.InvariantCulture, $"gate:{key}:{level}<{lo}")
        : max is { } hi && level > hi ? string.Create(CultureInfo.InvariantCulture, $"gate:{key}:{level}>{hi}")
        : null;

    /// <summary>The ground's entry as a portal (the profile's <c>entry</c>, else the route's run target with no gate: the server's is read at the Use), or null when neither is known.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="run">The route's run, or null.</param>
    public static RungPortal? EntryPortal(HuntingGround ground, TravelRun? run)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var dest = ground.ArrivalLandblock();
        if (ground.Entry is { } e)
        {
            // The profile's entry carries no rotation: the route's run row for the same instance does (the travel file, M6 4 T3a).
            var facing = run is not null && run.EntryGuid == e.Guid ? run.EntryFacing?.Forward : null;
            return new RungPortal("entry:" + ground.Key, ground.Name + " entry", e.Wcid, e.Guid, e.Cell, e.Origin, dest, e.MinLevel, e.MaxLevel, [], facing);
        }

        return run is null ? null : new RungPortal("entry:" + ground.Key, run.EntryName, run.EntryWcid, run.EntryGuid, run.EntryCell, run.EntryOrigin, dest, null, null, [], run.EntryFacing?.Forward);
    }

    /// <summary>Decides the legs (see the class).</summary>
    /// <param name="input">Where, who and what the character holds.</param>
    /// <param name="routes">The travel file.</param>
    /// <param name="doors">The lever-only doors (<see cref="LeverDoors.Default"/> when null).</param>
    public static RungPlan Decide(RungRouteInput input, TravelRoutes routes, LeverDoors? doors = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(routes);
        doors ??= LeverDoors.Default;
        if (input.StopAtPlace is { } stopAt)
        {
            return StopAt(input, routes, doors, stopAt);
        }

        return input.Direction == RungDirection.Home ? Home(input, routes, doors) : Out(input, routes, doors);
    }

    /// <summary>The route key's suffix of a plan cut at a place (<c>&lt;route&gt;@&lt;place&gt;</c>).</summary>
    public const string StopAtSeparator = "@";

    /// <summary>
    /// M6 6 C2: the plan without <see cref="RungRouteInput.StopAtPlace"/>, cut after its first leg that arrives in the place (a hop or an enter in one of the place's landblocks, a recall landing there); the run, the gate, the tie and the entry's Use
    /// after it are dropped. The tied ground's Primary Portal Recall is not planned (it lands in the ground, not in the place: the route is). The body already in the place: no legs (<see cref="Here"/>). A place the travel file does not know, or a
    /// plan none of whose legs arrives in it, is the named stop <c>rung-change:stop-at-unknown:&lt;key&gt;</c> or <c>rung-change:stop-at-off-route:&lt;key&gt;</c>; the uncut plan's own stop (a gate, <c>TODO(m6-ground-route)</c>) stays its own.
    /// </summary>
    private static RungPlan StopAt(RungRouteInput input, TravelRoutes routes, LeverDoors doors, string stopAt)
    {
        var whole = input with { StopAtPlace = null, Tie = input.Tie == PortalTie.Tied && input.Direction == RungDirection.Out ? PortalTie.Unknown : input.Tie };
        var tiedKnown = input.Tie == PortalTie.Tied;
        if (routes.PlaceOf(stopAt) is not { } place || place.Key != stopAt)
        {
            var unknown = $"rung-change:stop-at-unknown:{stopAt}";
            return new(input.Direction, input.Ground.Key, NoRoute, [], TiedOf(input), GemOf(input), "pass", unknown, $"{unknown}: the travel file ({TravelRoutes.FileName}) has no place '{stopAt}'");
        }

        if (input.Place == place.Key)
        {
            return new(input.Direction, input.Ground.Key, Here + StopAtSeparator + place.Key, [], tiedKnown ? "known" : TiedOf(input), GemOf(input), "pass", null, $"the body already stands in {place.Key} (stop_at={place.Key}): no travel");
        }

        var plan = whole.Direction == RungDirection.Home ? Home(whole, routes, doors) : Out(whole, routes, doors);
        if (plan.Stop is not null)
        {
            return plan with { Why = $"{plan.Why} (stop_at={place.Key}: the uncut plan stops first)" };
        }

        for (var i = 0; i < plan.Legs.Count; i++)
        {
            if (ArrivesIn(plan.Legs[i], place))
            {
                var dropped = plan.Legs.Skip(i + 1).Select(l => l.KindName + ":" + l.Label).ToArray();
                var tied = tiedKnown ? "known" : plan.Tied == "yes" ? "no" : plan.Tied; // the tie leg, if planned, is dropped with the entry
                return plan with
                {
                    RouteKey = plan.RouteKey + StopAtSeparator + place.Key,
                    Legs = [.. plan.Legs.Take(i + 1)],
                    Tied = tied,
                    Why = $"{plan.Why}; stop_at={place.Key}: the plan stops after leg {i + 1} ({plan.Legs[i].KindName}:{plan.Legs[i].Label}), which arrives in {place.Key} (dropped: [{string.Join(",", dropped)}])",
                };
            }
        }

        var off = $"rung-change:stop-at-off-route:{place.Key}";
        return plan with
        {
            RouteKey = NoRoute,
            Legs = [],
            Stop = off,
            Why = $"{off}: no leg of the plan {plan.RouteKey} [{string.Join(",", plan.Legs.Select(l => l.KindName + ":" + l.Label))}] arrives in {place.Key} ({string.Join(", ", place.Landblocks.Select(l => Hex(l)))})",
        };
    }

    /// <summary>True when <paramref name="leg"/> ends in one of <paramref name="place"/>'s landblocks (a hop or an enter by its destination, a recall by its landing).</summary>
    private static bool ArrivesIn(RungLeg leg, TravelPlace place) => leg.Kind switch
    {
        RungLegKind.Hop or RungLegKind.Enter => leg.Portal is { } p && place.Landblocks.Contains((ushort)p.DestLandblock),
        RungLegKind.LifestoneRecall or RungLegKind.PortalRecall => place.Landblocks.Contains((ushort)(leg.Cell >> 16)),
        _ => false,
    };

    /// <summary>
    /// M6 6 C2: the travel place that holds a corpse lost on a leg (the island run, a mainland walk): the place whose landblocks hold the corpse's (<see cref="TravelRoutes.PlaceOf"/>); else, on the ground's first admitted route
    /// (<see cref="TravelRoutes.FirstAdmitted"/>), the on-foot leg the body was walking: each step's walk (from the route's start lifestone, or the previous hop's drop, its <see cref="TravelStep.WalkMetres"/>) and the run (from the last hop's drop,
    /// its <see cref="TravelRun.Metres"/>), outdoors, whose start lies within the leg's own length of the corpse: the nearest such start's place (the corpse leg goes on foot from there). None is a null key, named by the caller
    /// <c>corpse-off-route</c>. Every value is the travel file's (D26); nothing is guessed: the inputs are in <see cref="LegPlace.Why"/>.
    /// </summary>
    /// <param name="routes">The travel file.</param>
    /// <param name="ground">The rung's ground (whose route the leg was on).</param>
    /// <param name="level">The character's level (the route the level admits).</param>
    /// <param name="cell">The corpse's cell.</param>
    /// <param name="local">The corpse's landblock-local point.</param>
    public static LegPlace PlaceHolding(TravelRoutes routes, HuntingGround ground, int level, uint cell, Vector3 local)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(ground);
        var landblock = Hex(cell >> 16);
        if (routes.PlaceOf(landblock) is { } held)
        {
            return new(held.Key, $"the travel file's place {held.Key} holds the corpse's landblock {landblock}");
        }

        var choice = routes.FirstAdmitted(ground.Key, level, ground.Entry?.MinLevel, ground.Entry?.MaxLevel);
        if (choice.Route is not { } route)
        {
            return new(null, $"no travel place holds landblock {landblock}, and {ground.Key} has no route level {level} passes ({choice.Stop})");
        }

        var corpse = AcCoordinates.ToGlobal(new CellPosition(cell, local));
        var legs = new List<(string Place, string Leg, uint Cell, Vector3 At, double? Metres)>();
        var (from, fromCell, fromAt) = (route.Start, routes.StartLifestone.Cell, routes.StartLifestone.Origin);
        if (route.Start != routes.StartPlace)
        {
            (fromCell, fromAt) = (0, default); // a route that starts elsewhere has no known first point: its first walk is not read
        }

        foreach (var step in route.Steps)
        {
            legs.Add((from, "walk to " + step.Edge.Key, fromCell, fromAt, step.WalkMetres));
            (from, fromCell, fromAt) = (step.Edge.To, step.Edge.DestCell, step.Edge.DestOrigin);
        }

        if (route.Run is { } run)
        {
            legs.Add((from, "run to " + run.EntryName, fromCell, fromAt, run.Metres));
        }

        (string Place, string Leg, double Metres, double Length)? best = null;
        var read = new List<string>();
        foreach (var (place, leg, start, at, metres) in legs)
        {
            if (start == 0 || metres is not { } length || (start & 0xFFFF) >= 0x100)
            {
                continue; // no start point, no length, or an indoor start (a Town Network room): not an outdoor walk
            }

            var d = AcCoordinates.ToGlobal(new CellPosition(start, at)).DistanceTo(corpse);
            read.Add(string.Create(CultureInfo.InvariantCulture, $"{leg} from {place} {d:0} m of {length:0} m"));
            if (d <= length && (best is null || d < best.Value.Metres))
            {
                best = (place, leg, d, length);
            }
        }

        if (best is { } b)
        {
            return new(b.Place, string.Create(CultureInfo.InvariantCulture, $"no travel place holds landblock {landblock}; on route {route.Key} the corpse lies {b.Metres:0} m from the start of the {b.Leg} leg in {b.Place}, within the leg's {b.Length:0} m: the corpse leg goes on foot from {b.Place} (read: {string.Join("; ", read)})"));
        }

        return new(null, $"no travel place holds landblock {landblock}, and no on-foot leg of route {route.Key} reaches it from its start (read: {(read.Count == 0 ? "none" : string.Join("; ", read))})");
    }

    /// <summary>
    /// M6 4 F1 (4-R3): the ground whose landblocks hold <paramref name="cell"/> when <paramref name="ground"/> does not: the first of <paramref name="grounds"/> (the catalog, <see cref="GroundCatalog.All"/>, or a test's own) that holds it;
    /// null when <paramref name="ground"/> holds it or none does.
    /// </summary>
    /// <param name="cell">The body's cell.</param>
    /// <param name="ground">The run's ground.</param>
    /// <param name="grounds">The known grounds.</param>
    public static HuntingGround? BodyGroundOf(uint cell, HuntingGround ground, IReadOnlyList<HuntingGround> grounds)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(grounds);
        if (ground.InLandblocks(cell))
        {
            return null;
        }

        for (var i = 0; i < grounds.Count; i++)
        {
            if (!string.Equals(grounds[i].Key, ground.Key, StringComparison.Ordinal) && grounds[i].InLandblocks(cell))
            {
                return grounds[i];
            }
        }

        return null;
    }

    private static string GemOf(RungRouteInput input) => input.HoldsGem ? "held" : "none";

    private static string TiedOf(RungRouteInput input) =>
        input.Ground.Entry is { Tieable: true } ? (input.KnowsPortalTie ? "yes" : "no") : "untieable";

    private static RungPlan Out(RungRouteInput input, TravelRoutes routes, LeverDoors doors)
    {
        var g = input.Ground;
        var gem = GemOf(input);
        if (input.Where == RungWhere.AtGround)
        {
            return new(RungDirection.Out, g.Key, Here, [], input.Tie == PortalTie.Tied ? "known" : TiedOf(input), gem, "pass", null, $"the body already stands in {g.Key}'s landblocks: no travel");
        }

        if (input.Tie == PortalTie.Tied && input.KnowsPortalRecall)
        {
            var spell = g.Portal?.RecallSpell ?? DenTravelStation.PortalRecallSpell;
            return new(
                RungDirection.Out,
                g.Key,
                PortalRecallRoute,
                [new RungLeg(RungLegKind.PortalRecall, "portal-recall", Spell: spell, Cell: g.ArrivalLandblock() << 16)],
                "known",
                gem,
                "pass",
                null,
                string.Create(CultureInfo.InvariantCulture, $"{g.Key} is known tied and the spellbook holds Primary Portal Recall {spell}: the recall lands at the ground's arrival (landblock 0x{g.ArrivalLandblock():X4})"));
        }

        var entry = g.Entry;
        var choice = routes.FirstAdmitted(g.Key, input.Level, entry?.MinLevel, entry?.MaxLevel, input.FailedRoutes, input.Place); // M6 8 B1: a route that starts in the body's place first
        if (choice.Outcome == TravelOutcome.NoRoute)
        {
            return new(RungDirection.Out, g.Key, NoRoute, [], TiedOf(input), gem, "pass", choice.Stop, choice.Stop!);
        }

        if (choice.Outcome == TravelOutcome.Refused)
        {
            var (name, bound) = Refusal(routes.RoutesFor(g.Key), input.Level, g);
            return new(RungDirection.Out, g.Key, NoRoute, [], TiedOf(input), gem, "refused:" + bound.ToString(CultureInfo.InvariantCulture), name, $"{name}: {choice.Stop} (refused before any walk)");
        }

        var route = choice.Route!;
        if (route.Steps.Count == 0 && route.Run is null)
        {
            return new(RungDirection.Out, g.Key, route.Key, [], input.Tie == PortalTie.Tied ? "known" : TiedOf(input), gem, "pass", null, $"route {route.Key} has no portal and no run ({route.Note ?? "no note"}): the ground's own station ({Handover}) takes the climber");
        }

        var legs = new List<RungLeg>();
        var why = new List<string>();
        var firstStep = 0;
        if (input.Where == RungWhere.Elsewhere && route.Start == routes.StartPlace && input.Place != route.Start)
        {
            var joins = input.FailedRoutes is { Count: > 0 }; // only after a route failed this run (the fallback): a station started elsewhere still goes home first
            if (joins && StepDroppingIn(route, input.Place) is var standing and >= 0)
            {
                // M6 7 M: the body already stands where a step of the route drops (a hop of the failed route that dropped it in the Network): the route goes on from there, no walk home.
                firstStep = standing + 1;
                why.Add($"joins route {route.Key} where step {route.Steps[standing].Edge.Key} drops ({input.Place})");
            }
            else
            {
                var home = Home(input with { Direction = RungDirection.Home }, routes, doors);
                if (home.Stop is not null)
                {
                    return home with { Direction = RungDirection.Out, RouteKey = NoRoute, Tied = TiedOf(input), Why = $"the way home before route {route.Key} has no way: {home.Why}" };
                }

                // M6 7 M: the way home is cut at its first hop that drops where a step of the route drops (the hub's way home reaches the Network before Holtburg), and the route goes on from there.
                var cut = -1;
                for (var i = 0; i < home.Legs.Count && cut < 0; i++)
                {
                    if (joins && home.Legs[i] is { Kind: RungLegKind.Hop, Portal: { } hop } && routes.PlaceOf(Hex(hop.DestLandblock))?.Key is { } dropped && StepDroppingIn(route, dropped) >= 0)
                    {
                        cut = i;
                        firstStep = StepDroppingIn(route, dropped) + 1;
                    }
                }

                legs.AddRange(cut >= 0 ? home.Legs.Take(cut + 1) : home.Legs);
                why.Add(cut >= 0 ? $"home first ({home.RouteKey}) as far as {legs[^1].Label}, then route {route.Key} from step {firstStep + 1}" : $"home first ({home.RouteKey})");
            }
        }

        for (var i = firstStep; i < route.Steps.Count; i++)
        {
            legs.Add(new RungLeg(RungLegKind.Hop, route.Steps[i].Edge.Key, RungPortal.Of(route.Steps[i].Edge, routes)));
        }

        var portal = EntryPortal(g, route.Run);
        if (portal is null)
        {
            return new(RungDirection.Out, g.Key, route.Key, legs, TiedOf(input), gem, "pass", $"TODO(m6-ground-route): route {route.Key} of '{g.Key}' names no entry and the profile has none", $"route {route.Key} names no entry portal");
        }

        var tied = TiedOf(input);
        if (entry is not null)
        {
            if (route.Run?.Via is { } via && input.Level < via.BelowLevel)
            {
                // M6 7 Z: the run bends round the outdoor encounter cells for a climber under their creatures' levels; a stronger one runs straight.
                legs.AddRange(via.Points.Select((p, i) => new RungLeg(RungLegKind.Run, "via-" + (i + 1).ToString(CultureInfo.InvariantCulture), Cell: p.Cell, At: p.Origin, Via: p.Why)));
                why.Add(string.Create(CultureInfo.InvariantCulture, $"the run takes {via.Points.Count} via points round the encounter cells (level {input.Level} under {via.BelowLevel})"));
            }

            legs.Add(new RungLeg(RungLegKind.Run, "entry-front", Cell: entry.FrontCell, At: entry.Front));
            legs.Add(new RungLeg(RungLegKind.Gate, portal.Key, portal));
            if (entry.Tieable && input.KnowsPortalTie && input.Tie != PortalTie.Tied)
            {
                legs.Add(new RungLeg(RungLegKind.Tie, portal.Key, portal, Spell: g.Portal?.TieSpell ?? DenTravelStation.PortalTieSpell));
            }
            else if (entry.Tieable && input.Tie == PortalTie.Tied)
            {
                tied = "known";
            }
        }
        else
        {
            why.Add("a pending profile with no entry: the entry's gate is the server's, read at the Use, and nothing is tied");
            tied = "untieable";
        }

        legs.Add(new RungLeg(RungLegKind.Enter, portal.Key, portal));
        why.Insert(0, string.Create(CultureInfo.InvariantCulture, $"route {route.Key} admits level {input.Level} (gate {route.Gate.With(entry?.MinLevel, entry?.MaxLevel)})"));
        return new(RungDirection.Out, g.Key, route.Key, legs, tied, gem, "pass", null, string.Join("; ", why));
    }

    /// <summary>
    /// The index of the first step of <paramref name="route"/> whose edge drops in <paramref name="place"/>, or -1. The last step is never one: its drop is the ground's landing (the island), where a body stands anywhere, not at the route's own drop,
    /// and the way home from there stays the way home.
    /// </summary>
    private static int StepDroppingIn(TravelRoute route, string place)
    {
        for (var i = 0; i < route.Steps.Count - 1; i++)
        {
            if (string.Equals(route.Steps[i].Edge.To, place, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The refusal's name for a level no route admits: the most permissive route's first portal that refuses it (or the entry's gate), and its bound.</summary>
    private static (string Name, int Bound) Refusal(IReadOnlyList<TravelRoute> routes, int level, HuntingGround ground)
    {
        var best = routes.OrderBy(r => r.Gate.MinLevel).First();
        foreach (var step in best.Steps)
        {
            if (!step.Edge.Admits(level))
            {
                var name = GateRefusal(step.Edge.Key, level, step.Edge.MinLevel, step.Edge.MaxLevel)!;
                return (name, step.Edge.MinLevel is { } min && level < min ? min : step.Edge.MaxLevel ?? 0);
            }
        }

        var e = ground.Entry;
        var entryName = GateRefusal("entry:" + ground.Key, level, e?.MinLevel, e?.MaxLevel) ?? $"gate:{best.Key}:{level}";
        return (entryName, e?.MinLevel is { } lo && level < lo ? lo : e?.MaxLevel ?? 0);
    }

    private static RungPlan Home(RungRouteInput input, TravelRoutes routes, LeverDoors doors)
    {
        var g = input.Ground;
        var gem = GemOf(input);
        var tied = input.Tie == PortalTie.Tied ? "known" : TiedOf(input);
        if (input.Where == RungWhere.Home)
        {
            return new(RungDirection.Home, g.Key, Here, [], tied, gem, "pass", null, $"the body already stands in {routes.StartPlace}: no travel");
        }

        if (input.KnowsLifestoneRecall)
        {
            var start = routes.StartLifestone.Cell >> 16;
            return new(RungDirection.Home, g.Key, LifestoneRecallRoute, [new RungLeg(RungLegKind.LifestoneRecall, "lifestone-recall", Spell: Fighter.RecallHome.LifestoneRecallSpell, Cell: start << 16)], tied, gem, "pass", null, $"the spellbook holds Lifestone Recall: home to {routes.StartPlace}'s lifestone (landblock 0x{start:X4})");
        }

        var legs = new List<RungLeg>();
        var place = input.Place;
        var why = new List<string> { "the spellbook lacks Lifestone Recall" };
        var holder = input.Where == RungWhere.AtGround ? g : input.Where == RungWhere.Elsewhere ? input.BodyGround : null; // M6 4 F1 (4-R3): the ground the body stands in, not the run's
        if (holder is not null)
        {
            if (!ReferenceEquals(holder, g))
            {
                why.Add($"the body stands in {holder.Key}'s landblocks, not {g.Key}'s: the way home is {holder.Key}'s");
            }

            if (WayHomeRule.HomeOnFoot(holder))
            {
                return new(RungDirection.Home, g.Key, "walk-home", [new RungLeg(RungLegKind.WalkHome, "home-lifestone-point", Cell: holder.Home.LifestoneCell, At: holder.Home.LifestonePoint)], tied, gem, "pass", null, $"{string.Join("; ", why)}; {holder.Key} is hunted from {holder.Home.Town} on foot: the walk home");
            }

            if (holder.Exit is not { Kind: GroundExitKind.Portal, Origin: { } origin } exit)
            {
                var stop = $"TODO(m6-way-home): {holder.Key} is left by {(holder.Exit?.Kind.ToString().ToLowerInvariant() ?? "an unknown way")} and the spellbook lacks Lifestone Recall";
                return new(RungDirection.Home, g.Key, NoRoute, [], tied, gem, "pass", stop, stop);
            }

            legs.Add(new RungLeg(RungLegKind.Hop, "exit:" + holder.Key, new RungPortal("exit:" + holder.Key, holder.Name + " exit", exit.Wcid, 0, exit.Cell, origin, exit.Destination >> 16, null, null, holder.Landblocks)));
            place = routes.PlaceOf(Hex(exit.Destination >> 16))?.Key ?? Hex(exit.Destination >> 16);
            why.Add($"the exit portal {exit.Wcid} to 0x{exit.Destination:X8}");
        }

        var way = routes.WayHomeFrom(place, input.Level);
        if (way.Outcome != TravelOutcome.Admitted)
        {
            var from = routes.PlaceOf(place)?.Key ?? place;
            var bound = routes.WaysHome.Where(w => w.From == from).Select(w => w.Gate.MinLevel).DefaultIfEmpty(0).Min();
            return new(RungDirection.Home, g.Key, NoRoute, [], tied, gem, way.Outcome == TravelOutcome.Refused ? "refused:" + bound.ToString(CultureInfo.InvariantCulture) : "pass", way.Stop, $"{string.Join("; ", why)}; {way.Stop}");
        }

        if (way.WayHome!.Steps.Count > 0 && doors.Before(way.WayHome.Steps[0].Edge.Guid) is { } door)
        {
            // M6 4 F1 (4-R8): the walk to the way's first portal passes a door the walker cannot open (TownNetworkTrip refuses the same).
            var first = way.WayHome.Steps[0].Edge;
            var stop = string.Create(CultureInfo.InvariantCulture, $"TODO(door-lever): the way home {way.WayHome.Key}'s first portal {first.Name} 0x{first.Guid:X8} is behind {door.Town}'s door 0x{door.DoorGuid:X8}, which opens only by its lever 0x{door.LeverGuid:X8} (lever-door:{door.Key})");
            return new(RungDirection.Home, g.Key, NoRoute, [], tied, gem, "pass", stop, $"{string.Join("; ", why)}; {stop}");
        }

        foreach (var step in way.WayHome.Steps)
        {
            legs.Add(new RungLeg(RungLegKind.Hop, step.Edge.Key, RungPortal.Of(step.Edge, routes)));
        }

        why.Add($"the way home {way.WayHome.Key} from {place} by portals");
        return new(RungDirection.Home, g.Key, way.WayHome.Key, legs, tied, gem, "pass", null, string.Join("; ", why));
    }

    /// <summary>A landblock in the travel file's hex form (<c>0xF682</c>).</summary>
    /// <param name="landblock">The landblock.</param>
    public static string Hex(uint landblock) => "0x" + landblock.ToString("X4", CultureInfo.InvariantCulture);
}
