using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Travel;

/// <summary>
/// A travel-routes file that does not load (M6 item 4 D50): names the file and the JSON path of the field, as <c>LadderException</c> does for the ladders.
/// </summary>
public sealed class TravelRoutesException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="file">The file name.</param>
    /// <param name="path">The JSON path of the field.</param>
    /// <param name="problem">What is wrong.</param>
    public TravelRoutesException(string file, string path, string problem)
        : base($"travel routes {file} {path}: {problem}")
    {
        File = file;
        JsonPath = path;
    }

    /// <summary>The file name.</summary>
    public string File { get; }

    /// <summary>The JSON path of the field.</summary>
    public string JsonPath { get; }
}

/// <summary>A place the travel file knows: a town, the Town Network, the island surface, the Facility Hub.</summary>
/// <param name="Key">The place's key.</param>
/// <param name="Name">The place's name for logs.</param>
/// <param name="Landblocks">The landblocks that belong to the place (the high word of a cell).</param>
/// <param name="Source">The rows the landblocks come from (D28; M6 4 F2): the edges' and the start's landblock_instance rows, their destination positions, and <c>TravelRoutesSqlTests</c>, which checks every landblock of every place against those rows.</param>
public sealed record TravelPlace(string Key, string Name, IReadOnlyList<ushort> Landblocks, string Source = "");

/// <summary>
/// The rotation of a placed portal, the angles of its <c>landblock_instance</c> row (<c>angles_W/X/Y/Z</c>, a quaternion). An object faces local +Y at the identity; <see cref="Forward"/> is that direction turned by the rotation, on the ground plane. The side a
/// portal faces is where the client stands to use it (ACE uses the player's approach; the client walks to 3 m in front: <c>TODO(m6-portal-front-facing)</c> closed by M6 4 T3a).
/// </summary>
/// <param name="W">The quaternion's W.</param>
/// <param name="X">The quaternion's X.</param>
/// <param name="Y">The quaternion's Y.</param>
/// <param name="Z">The quaternion's Z.</param>
public readonly record struct PortalFacing(double W, double X, double Y, double Z)
{
    /// <summary>The unit vector the portal faces on the ground plane (landblock-local x east, y north), or null when the rotation tips it flat (no horizontal facing).</summary>
    public Vector2? Forward
    {
        get
        {
            var x = 2 * ((X * Y) - (W * Z));
            var y = 1 - (2 * ((X * X) + (Z * Z)));
            var length = Math.Sqrt((x * x) + (y * y));
            return length < 0.3 ? null : new Vector2((float)(x / length), (float)(y / length));
        }
    }
}

/// <summary>
/// A portal gem a hop into a place may be replaced by (M6 item 4 T3a, closing <c>TODO(m6-gem-in-travel-file)</c>): the Aphus Lassel Portal Gem. Every value is a world SQL row named in <see cref="Source"/>: the weenie, its spell (PropertyDataId 28)
/// and the spell's destination position.
/// </summary>
/// <param name="Key">The gem's key.</param>
/// <param name="Name">The gem's name (<c>weenie_properties_string</c> type 1).</param>
/// <param name="Wcid">The gem's weenie.</param>
/// <param name="Spell">The spell it casts (<c>weenie_properties_d_i_d</c> type 28).</param>
/// <param name="SpellName">The spell's name.</param>
/// <param name="DestCell">The spell's destination cell.</param>
/// <param name="DestOrigin">The spell's destination point.</param>
/// <param name="ReplacesHopsInto">The place whose portal hops the gem replaces.</param>
/// <param name="Source">The SQL tables and rows the values come from.</param>
public sealed record TravelGem(string Key, string Name, uint Wcid, uint Spell, string SpellName, uint DestCell, Vector3 DestOrigin, string ReplacesHopsInto, string Source)
{
    /// <summary>The landblock the gem drops in.</summary>
    public uint DestLandblock => DestCell >> 16;
}

/// <summary>
/// One portal of the world as a travel edge: where it stands, where it drops and what it admits (every value a world SQL row, named in <see cref="Source"/>).
/// </summary>
/// <param name="Key">The edge's key.</param>
/// <param name="Name">The portal's name.</param>
/// <param name="From">The place the portal stands in.</param>
/// <param name="To">The place it drops in.</param>
/// <param name="Wcid">The portal weenie.</param>
/// <param name="Guid">The landblock_instance guid.</param>
/// <param name="Cell">The stored cell of the instance (an outdoor row carries 0xLLLL0000).</param>
/// <param name="Origin">The instance's landblock-local point.</param>
/// <param name="DestCell">The destination cell (weenie_properties_position type 2).</param>
/// <param name="DestOrigin">The destination point.</param>
/// <param name="MinLevel">PropertyInt 86, or null (no limit).</param>
/// <param name="MaxLevel">PropertyInt 87, or null (no limit).</param>
/// <param name="Bitmask">PropertyInt 111 (PortalBitmask).</param>
/// <param name="Source">The SQL tables and rows the values come from.</param>
/// <param name="Note">A named note, or null.</param>
/// <param name="Facing">The placement's rotation (<c>landblock_instance</c> angles), or null when the file gives none.</param>
/// <param name="WalkIn">M6 8 B1: the way the hop walks before its portal's front when the room graph has no way to the portal's room (the Crater Pathway's door and stair): the first point by the ordinary walk (the door's front), the rest as an authored way (<c>AuthoredWaySource</c>), or null.</param>
/// <param name="WalkOut">M6 8 B1: the points the body walks to in turn after it arrives (the room graph has no way out of the drop's room: the Crater Pathway's top building), or null; each point a <see cref="TravelVia"/> checked by <c>WalkToPoint</c>.</param>
public sealed record TravelEdge(
    string Key,
    string Name,
    string From,
    string To,
    uint Wcid,
    uint Guid,
    uint Cell,
    Vector3 Origin,
    uint DestCell,
    Vector3 DestOrigin,
    int? MinLevel,
    int? MaxLevel,
    int Bitmask,
    string Source,
    string? Note,
    PortalFacing? Facing = null,
    IReadOnlyList<TravelVia>? WalkOut = null,
    IReadOnlyList<TravelVia>? WalkIn = null)
{
    /// <summary>True when a character of <paramref name="level"/> may use the portal (ACE's MinLevel and MaxLevel).</summary>
    /// <param name="level">The character's level.</param>
    public bool Admits(int level) => (MinLevel is not { } min || level >= min) && (MaxLevel is not { } max || level <= max);
}

/// <summary>The levels a route or way home admits: the highest MinLevel and the lowest MaxLevel on its portals (and the ground entry's own).</summary>
/// <param name="MinLevel">The lowest admitted level (0 when nothing limits it).</param>
/// <param name="MaxLevel">The highest admitted level, or null.</param>
public readonly record struct TravelGate(int MinLevel, int? MaxLevel)
{
    /// <summary>True when <paramref name="level"/> is inside the gate.</summary>
    /// <param name="level">The character's level.</param>
    public bool Admits(int level) => level >= MinLevel && (MaxLevel is not { } max || level <= max);

    /// <summary>The gate of both (the higher minimum, the lower maximum).</summary>
    /// <param name="minLevel">Another minimum, or null.</param>
    /// <param name="maxLevel">Another maximum, or null.</param>
    public TravelGate With(int? minLevel, int? maxLevel) => new(
        Math.Max(MinLevel, minLevel ?? 0),
        (MaxLevel, maxLevel) switch { (null, var b) => b, (var a, null) => a, var (a, b) => Math.Min(a!.Value, b!.Value) });

    /// <summary>The gate of a list of edges.</summary>
    /// <param name="edges">The portals.</param>
    public static TravelGate Of(IEnumerable<TravelEdge> edges)
    {
        var gate = new TravelGate(0, null);
        foreach (var e in edges)
        {
            gate = gate.With(e.MinLevel, e.MaxLevel);
        }

        return gate;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{MinLevel}..{(MaxLevel is { } m ? m.ToString(CultureInfo.InvariantCulture) : "-")}";
}

/// <summary>One portal use of a route: the edge and the walk to it.</summary>
/// <param name="Edge">The portal.</param>
/// <param name="WalkMetres">The straight-line metres from where the previous step left the climber (the start, or the previous drop) to the portal; null for a way home's first step (see <see cref="WayHome.FirstWalkHints"/>).</param>
public sealed record TravelStep(TravelEdge Edge, double? WalkMetres);

/// <summary>The run from a route's last drop to the ground's entry portal (a hint; the run is planned at run time from the profile's entry front point).</summary>
/// <param name="EntryWcid">The entry portal's weenie.</param>
/// <param name="EntryName">The entry portal's name.</param>
/// <param name="EntryGuid">The entry portal's landblock_instance guid.</param>
/// <param name="EntryCell">The stored cell of that row.</param>
/// <param name="EntryOrigin">That row's landblock-local point.</param>
/// <param name="Metres">The straight-line metres from the last drop (or the start) to the entry portal.</param>
/// <param name="Source">How the number is derived.</param>
/// <param name="EntryFacing">The entry row's rotation (<c>landblock_instance</c> angles), or null when the file gives none.</param>
/// <param name="Via">M6 7 Z: the points the run walks to in turn before the entry's front, or null: straight to it.</param>
public sealed record TravelRun(uint EntryWcid, string EntryName, uint EntryGuid, uint EntryCell, Vector3 EntryOrigin, double Metres, string Source, PortalFacing? EntryFacing = null, TravelViaPlan? Via = null);

/// <summary>
/// One point a run walks to on its way (M6 7 Z): the run's legs are the via points in turn, then the entry's front, so the walk keeps off the outdoor encounter cells (<see cref="TravelDangerTable"/>). A via that cannot be walked is skipped by name, never a stop
/// (<c>RungChangeStation</c>): the points are a cost, not a wall.
/// </summary>
/// <param name="Cell">The land cell.</param>
/// <param name="Origin">The landblock-local point (z the terrain's).</param>
/// <param name="Why">The reason the walk bends here (the danger cells it keeps off), for the log.</param>
public sealed record TravelVia(uint Cell, Vector3 Origin, string Why);

/// <summary>The via points of a run (M6 7 Z).</summary>
/// <param name="BelowLevel">The run walks them only for a level below this (one above the highest level of the encounter tables it keeps off); a stronger climber runs straight.</param>
/// <param name="Points">The points in order.</param>
/// <param name="Source">How they were computed.</param>
public sealed record TravelViaPlan(int BelowLevel, IReadOnlyList<TravelVia> Points, string Source);

/// <summary>One outdoor encounter generator's cell (M6 7 Z): ACE puts the generator at (<see cref="CellX"/> x 24, <see cref="CellY"/> x 24), clamped to 0.5..191.5 (<c>Landblock.SpawnEncounters</c>, Landblock.cs:282-283 @95ef426d).</summary>
/// <param name="Landblock">The landblock (the high word of a cell).</param>
/// <param name="CellX">The row's cell_X, 0..7.</param>
/// <param name="CellY">The row's cell_Y, 0..7.</param>
public readonly record struct TravelDangerCell(ushort Landblock, int CellX, int CellY)
{
    /// <summary>The generator's landblock-local point.</summary>
    public Vector2 Local => new(Math.Clamp(CellX * 24f, 0.5f, 191.5f), Math.Clamp(CellY * 24f, 0.5f, 191.5f));

    /// <summary>The generator's global metres (landblock index x 192 plus the local point).</summary>
    public Vector2 Global => new(((Landblock >> 8) * 192f) + Local.X, ((Landblock & 0xFF) * 192f) + Local.Y);
}

/// <summary>
/// One encounter generator's cells, a table of the danger map (M6 7 Z; D28: <see cref="Source"/>, checked by <c>TravelRoutesSqlTests</c>): the levels it spawns, how near they notice a player, and how far from the generator they stand.
/// </summary>
/// <param name="Weenie">The generator weenie.</param>
/// <param name="Name">Its name.</param>
/// <param name="MinLevel">The lowest creature level among its outcomes.</param>
/// <param name="MaxLevel">The highest.</param>
/// <param name="AwarenessMetres">The largest VisualAwarenessRange among its outcomes (PropertyFloat 31; ACE wakes a creature within it, Monster_Awareness.cs @95ef426d).</param>
/// <param name="ScatterMetres">How far from the generator an outcome stands (PropertyFloat 43, the generator's and a camp's).</param>
/// <param name="Source">The rows.</param>
/// <param name="Cells">The cells.</param>
public sealed record TravelDangerTable(uint Weenie, string Name, int MinLevel, int MaxLevel, double AwarenessMetres, double ScatterMetres, string Source, IReadOnlyList<TravelDangerCell> Cells)
{
    /// <summary>The disc round a cell a walk stays out of: the awareness, the scatter and the two bodies' radii (<see cref="Movement.Wayfinding.NavCreatures.AggroRadius(float, float)"/>).</summary>
    public float ClearMetres => Movement.Wayfinding.NavCreatures.AggroRadius((float)(AwarenessMetres + ScatterMetres));
}

/// <summary>One way to a ground: a start place, the portals in order, then the run to the entry.</summary>
/// <param name="Key">The route's key (unique in the ground).</param>
/// <param name="GroundKey">The ground it leads to.</param>
/// <param name="Start">The place the climber starts from (the home town's lifestone).</param>
/// <param name="Steps">The portal uses in order.</param>
/// <param name="Run">The run to the entry portal, or null when no travel leg is needed.</param>
/// <param name="Note">A named note, or null.</param>
public sealed record TravelRoute(string Key, string GroundKey, string Start, IReadOnlyList<TravelStep> Steps, TravelRun? Run, string? Note)
{
    /// <summary>The gate of the route's portals (the ground entry's own gate is added by <see cref="Admits"/>).</summary>
    public TravelGate Gate { get; } = TravelGate.Of(Steps.Select(s => s.Edge));

    /// <summary>True when <paramref name="level"/> passes every portal of the route and the entry's own gate.</summary>
    /// <param name="level">The character's level.</param>
    /// <param name="entryMinLevel">The ground entry's MinLevel, or null.</param>
    /// <param name="entryMaxLevel">The ground entry's MaxLevel, or null.</param>
    public bool Admits(int level, int? entryMinLevel = null, int? entryMaxLevel = null) => Gate.With(entryMinLevel, entryMaxLevel).Admits(level);
}

/// <summary>The walk from where a character arrived to the first portal of a way home, given per arriving edge.</summary>
/// <param name="ArrivedBy">The edge the character arrived by.</param>
/// <param name="WalkMetres">The straight-line metres from that drop to the way home's first portal.</param>
public sealed record WalkHint(string ArrivedBy, double WalkMetres);

/// <summary>The way back to Holtburg's Town Network portal side: the portals in order from a place.</summary>
/// <param name="Key">The way's key.</param>
/// <param name="From">The place it leads from.</param>
/// <param name="Steps">The portal uses; the first step's walk is in <paramref name="FirstWalkHints"/>.</param>
/// <param name="FirstWalkHints">The walk to the first portal, by the edge arrived by.</param>
/// <param name="Note">A named note, or null.</param>
public sealed record WayHome(string Key, string From, IReadOnlyList<TravelStep> Steps, IReadOnlyList<WalkHint> FirstWalkHints, string? Note)
{
    /// <summary>The gate of the way's portals.</summary>
    public TravelGate Gate { get; } = TravelGate.Of(Steps.Select(s => s.Edge));

    /// <summary>True when <paramref name="level"/> passes every portal of the way.</summary>
    /// <param name="level">The character's level.</param>
    public bool Admits(int level) => Gate.Admits(level);
}

/// <summary>What <see cref="TravelRoutes.FirstAdmitted"/> and <see cref="TravelRoutes.WayHomeFrom"/> decided.</summary>
public enum TravelOutcome
{
    /// <summary>A route was admitted.</summary>
    Admitted,

    /// <summary>The file has no route for the ground: the named stop <c>TODO(m6-ground-route)</c>.</summary>
    NoRoute,

    /// <summary>The ground has routes but the level fails every gate.</summary>
    Refused,
}

/// <summary>The decision of <see cref="TravelRoutes.FirstAdmitted"/>: a route, or a named stop (never an exception).</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="Route">The first admitted route, or null.</param>
/// <param name="Stop">The named stop for the log, or null when admitted.</param>
public sealed record RouteChoice(TravelOutcome Outcome, TravelRoute? Route, string? Stop);

/// <summary>The decision of <see cref="TravelRoutes.WayHomeFrom"/>.</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="WayHome">The first admitted way home, or null.</param>
/// <param name="Stop">The named stop for the log, or null when admitted.</param>
public sealed record WayHomeChoice(TravelOutcome Outcome, WayHome? WayHome, string? Stop);

/// <summary>
/// The travel routes as data (M6 item 4 D50, <c>Session/Travel/travel-routes.json</c>): the portal edges (each with its wcid, guid, cell, origin, destination, gates and world SQL row), per ground key its routes in order of preference
/// (each a start place, portal steps and the run to the entry) and the ways home. A ground's travel is a tied tie record, else <see cref="FirstAdmitted"/>, else the named stop <c>TODO(m6-ground-route)</c>. Loaded once; a missing or
/// unknown field fails at load naming the file and the JSON path. No gate or distance lives in code (D26, D28).
/// </summary>
public sealed class TravelRoutes
{
    /// <summary>The embedded file's name.</summary>
    public const string FileName = "travel-routes.json";

    /// <summary>The logical name of the embedded file (Lifestone.Client.csproj).</summary>
    internal const string ResourceName = "Lifestone.Client.Session.Travel." + FileName;

    private static readonly Lazy<TravelRoutes> Loaded = new(() => Parse(FileName, ResourceText() ?? throw new FileNotFoundException($"the travel routes are not embedded (resource {ResourceName}; Lifestone.Client.csproj)")));

    private readonly Dictionary<string, TravelEdge> _edges;
    private readonly Dictionary<string, IReadOnlyList<TravelRoute>> _routes;

    private TravelRoutes(
        string name,
        IReadOnlyDictionary<string, string> sources,
        TravelEdge startLifestone,
        string startPlace,
        IReadOnlyList<TravelPlace> places,
        IReadOnlyList<TravelEdge> edges,
        Dictionary<string, IReadOnlyList<TravelRoute>> routes,
        IReadOnlyList<WayHome> waysHome,
        IReadOnlyList<TravelGem> gems,
        IReadOnlyList<TravelDangerTable> dangerCells,
        IReadOnlyList<RoomWayOut> waysOut)
    {
        Name = name;
        Sources = sources;
        StartLifestone = startLifestone;
        StartPlace = startPlace;
        Places = places;
        Edges = edges;
        _edges = edges.ToDictionary(e => e.Key, StringComparer.Ordinal);
        _routes = routes;
        WaysHome = waysHome;
        Gems = gems;
        DangerCells = dangerCells;
        WaysOut = waysOut;
    }

    /// <summary>The embedded travel routes.</summary>
    public static TravelRoutes Default => Loaded.Value;

    /// <summary>The file's name field.</summary>
    public string Name { get; }

    /// <summary>The file's sources by name.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>The start place every route begins from (Holtburg).</summary>
    public string StartPlace { get; }

    /// <summary>The start's lifestone as a pseudo-edge-free record: only its cell, origin, wcid and guid are meaningful (a <see cref="TravelEdge"/> with the destination equal to the stone).</summary>
    public TravelEdge StartLifestone { get; }

    /// <summary>The places.</summary>
    public IReadOnlyList<TravelPlace> Places { get; }

    /// <summary>Every portal edge.</summary>
    public IReadOnlyList<TravelEdge> Edges { get; }

    /// <summary>The ways home.</summary>
    public IReadOnlyList<WayHome> WaysHome { get; }

    /// <summary>The portal gems that replace a hop (M6 4 T3a).</summary>
    public IReadOnlyList<TravelGem> Gems { get; }

    /// <summary>The danger map (M6 7 Z): the outdoor encounter generators of the island, by table.</summary>
    public IReadOnlyList<TravelDangerTable> DangerCells { get; }

    /// <summary>M6 8 M: the named ways out of rooms the room graph cannot leave (<see cref="IndoorOutdoorPathSource.WaysOut"/>).</summary>
    public IReadOnlyList<RoomWayOut> WaysOut { get; }

    /// <summary>The gem that replaces hops into <paramref name="place"/>, or null.</summary>
    /// <param name="place">A place key.</param>
    public TravelGem? GemInto(string place) => Gems.FirstOrDefault(g => g.ReplacesHopsInto == place);

    /// <summary>The ground keys that have routes.</summary>
    public IEnumerable<string> GroundKeys => _routes.Keys;

    /// <summary>The edge of <paramref name="key"/>.</summary>
    /// <param name="key">The edge's key.</param>
    /// <exception cref="KeyNotFoundException">No such edge.</exception>
    public TravelEdge Edge(string key) => _edges.TryGetValue(key, out var e) ? e : throw new KeyNotFoundException($"no travel edge '{key}'");

    /// <summary>The routes of a ground in order of preference; empty when the file has none (<see cref="FirstAdmitted"/> then names <c>TODO(m6-ground-route)</c>).</summary>
    /// <param name="groundKey">The ground's key.</param>
    public IReadOnlyList<TravelRoute> RoutesFor(string groundKey) => _routes.TryGetValue(groundKey, out var r) ? r : [];

    /// <summary>
    /// The first route of the ground whose gate (the highest MinLevel and lowest MaxLevel on its portals, and the entry's own gate) admits <paramref name="level"/>; a ground with no route is the named stop <c>TODO(m6-ground-route)</c>, a level
    /// that fails every gate is <see cref="TravelOutcome.Refused"/>; neither throws.
    /// </summary>
    /// <param name="groundKey">The ground's key.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="entryMinLevel">The ground entry's MinLevel (the profile's entry, or the world's), or null.</param>
    /// <param name="entryMaxLevel">The ground entry's MaxLevel, or null.</param>
    /// <param name="avoid">M6 7 M: route keys that failed this run: the first admitted route not among them; when every admitted route is among them, the first admitted one (an avoided route is last, never removed).</param>
    /// <param name="bodyPlace">M6 8 B1: the place the body stands in, or null: a route that starts there comes first (a body in Crater Lake Village walks the crater, it does not go home first); a route that starts neither there nor at the file's start is skipped (no walk is known from the body to its start).</param>
    public RouteChoice FirstAdmitted(string groundKey, int level, int? entryMinLevel = null, int? entryMaxLevel = null, IReadOnlyCollection<string>? avoid = null, string? bodyPlace = null)
    {
        var all = RoutesFor(groundKey);
        var routes = (IReadOnlyList<TravelRoute>)[.. all.Where(r => r.Start == bodyPlace), .. all.Where(r => r.Start != bodyPlace && r.Start == StartPlace)];
        if (all.Count == 0)
        {
            return new RouteChoice(TravelOutcome.NoRoute, null, $"TODO(m6-ground-route): ground '{groundKey}' has no route in {FileName}");
        }

        TravelRoute? avoided = null;
        foreach (var r in routes)
        {
            if (r.Admits(level, entryMinLevel, entryMaxLevel))
            {
                if (avoid is null || !avoid.Contains(r.Key))
                {
                    return new RouteChoice(TravelOutcome.Admitted, r, null);
                }

                avoided ??= r;
            }
        }

        if (avoided is not null)
        {
            return new RouteChoice(TravelOutcome.Admitted, avoided, null);
        }

        var gates = string.Join(", ", routes.Select(r => $"{r.Key} {r.Gate.With(entryMinLevel, entryMaxLevel)}"));
        return new RouteChoice(TravelOutcome.Refused, null, $"travel:route-refused: level {level} passes no route of '{groundKey}' ({gates})");
    }

    /// <summary>
    /// The first way home from a place, a way's key or a landblock (a place's landblocks, <c>0xF682</c> or a number) that admits <paramref name="level"/>; nothing known is the named stop <c>TODO(m6-way-home)</c>, a level that fails every
    /// gate is <see cref="TravelOutcome.Refused"/>; neither throws.
    /// </summary>
    /// <param name="placeOrLandblock">A place key, a way home's key or a landblock in hex.</param>
    /// <param name="level">The character's level.</param>
    public WayHomeChoice WayHomeFrom(string placeOrLandblock, int level)
    {
        var place = PlaceOf(placeOrLandblock);
        var ways = WaysHome.Where(w => string.Equals(w.Key, placeOrLandblock, StringComparison.Ordinal) || string.Equals(w.From, placeOrLandblock, StringComparison.Ordinal) || (place is not null && w.From == place.Key)).ToArray();
        if (ways.Length == 0)
        {
            return new WayHomeChoice(TravelOutcome.NoRoute, null, $"TODO(m6-way-home): no way home from '{placeOrLandblock}' in {FileName}");
        }

        foreach (var w in ways)
        {
            if (w.Admits(level))
            {
                return new WayHomeChoice(TravelOutcome.Admitted, w, null);
            }
        }

        return new WayHomeChoice(TravelOutcome.Refused, null, $"travel:way-home-refused: level {level} passes no way home from '{placeOrLandblock}' ({string.Join(", ", ways.Select(w => $"{w.Key} {w.Gate}"))})");
    }

    /// <summary>The place whose key or landblock is <paramref name="placeOrLandblock"/>, or null.</summary>
    /// <param name="placeOrLandblock">A place key or a landblock in hex.</param>
    public TravelPlace? PlaceOf(string placeOrLandblock)
    {
        var byKey = Places.FirstOrDefault(p => p.Key == placeOrLandblock);
        if (byKey is not null)
        {
            return byKey;
        }

        if (placeOrLandblock.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(placeOrLandblock.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lb))
        {
            return Places.FirstOrDefault(p => p.Landblocks.Contains(lb));
        }

        return null;
    }

    /// <summary>The embedded file's text, or null.</summary>
    internal static string? ResourceText()
    {
        using var stream = typeof(TravelRoutes).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a travel-routes file; throws <see cref="TravelRoutesException"/> naming the file and the JSON path of the first error.</summary>
    /// <param name="file">The file name, for the messages.</param>
    /// <param name="json">The text.</param>
    internal static TravelRoutes Parse(string file, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new TravelRoutesException(file, "$", $"is not JSON: {e.Message}");
        }

        using (doc)
        {
            return new Reader(file).Read(doc.RootElement);
        }
    }

    private sealed class Reader(string file)
    {
        public TravelRoutesException Fail(string path, string problem) => new(file, path, problem);

        public TravelRoutes Read(JsonElement root)
        {
            var top = new Obj(this, root, "$");
            var key = top.Text("key");
            if (key != Path.GetFileNameWithoutExtension(file))
            {
                throw Fail("$.key", $"'{key}' does not match the file name {file}");
            }

            var name = top.Text("name");
            var sources = top.Object("sources", s =>
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in s.Names())
                {
                    d[p] = s.Text(p);
                }

                return d;
            });
            var startObj = top.Object("start", o => o);
            var places = top.List("places", ReadPlace);
            var placeKeys = places.ToDictionary(p => p.Key, StringComparer.Ordinal);
            if (placeKeys.Count != places.Count)
            {
                throw Fail("$.places", "has a duplicated key");
            }

            var edges = top.List("edges", (o, p) => ReadEdge(o, p, placeKeys));
            var byKey = new Dictionary<string, TravelEdge>(StringComparer.Ordinal);
            for (var i = 0; i < edges.Count; i++)
            {
                if (!byKey.TryAdd(edges[i].Key, edges[i]))
                {
                    throw Fail($"$.edges[{i}].key", $"'{edges[i].Key}' is duplicated");
                }
            }

            var startPlace = startObj.Text("place");
            if (!placeKeys.ContainsKey(startPlace))
            {
                throw Fail("$.start.place", $"'{startPlace}' is not a place");
            }

            var lifestone = new TravelEdge("start-lifestone", "Holtburg's lifestone", startPlace, startPlace, startObj.Uint("wcid"), startObj.Hex("guid"), startObj.Hex("cell"), startObj.Vector("origin"), 0, Vector3.Zero, null, null, 0, startObj.Text("source"), null);
            startObj.End();
            lifestone = lifestone with { DestCell = lifestone.Cell, DestOrigin = lifestone.Origin };

            var groundsObj = top.Object("grounds", o => o);
            var routes = new Dictionary<string, IReadOnlyList<TravelRoute>>(StringComparer.Ordinal);
            foreach (var g in groundsObj.Names())
            {
                var gObj = groundsObj.Object(g, o => o);
                var list = gObj.List("routes", (o, p) => ReadRoute(o, p, g, byKey, placeKeys));
                gObj.End();
                if (list.Count == 0)
                {
                    throw Fail($"$.grounds.{g}.routes", "is empty (a ground in the file has at least one route)");
                }

                if (list.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != list.Count)
                {
                    throw Fail($"$.grounds.{g}.routes", "has a duplicated route key");
                }

                routes[g] = list;
            }

            groundsObj.End();
            var ways = top.List("ways_home", (o, p) => ReadWay(o, p, byKey, placeKeys));
            if (ways.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count() != ways.Count)
            {
                throw Fail("$.ways_home", "has a duplicated key");
            }

            var gems = top.List("gems", (o, p) => ReadGem(o, p, placeKeys));
            if (gems.Select(g => g.Key).Distinct(StringComparer.Ordinal).Count() != gems.Count)
            {
                throw Fail("$.gems", "has a duplicated key");
            }

            var danger = top.List("danger_cells", ReadDanger);
            var waysOut = (IReadOnlyList<RoomWayOut>?)top.OptionalList("ways_out", ReadWayOut) ?? [];
            top.End();
            if (waysOut.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count() != waysOut.Count)
            {
                throw Fail("$.ways_out", "has a duplicated key");
            }

            if (waysOut.FirstOrDefault(w => waysOut.Any(o => o.Rooms.Contains(w.Way[^1].CellId))) is { } chained)
            {
                throw Fail("$.ways_out", $"'{chained.Key}' ends in a room a way out starts from (a way out must end where the planner joins)");
            }

            return new TravelRoutes(name, sources, lifestone, startPlace, places, edges, routes, ways, gems, danger, waysOut);
        }

        private TravelPlace ReadPlace(Obj o, string path)
        {
            var key = o.Text("key");
            var name = o.Text("name");
            var lbs = o.Strings("landblocks").Select((t, i) => ParseLandblock(t, $"{path}.landblocks[{i}]")).ToList();
            var source = o.OptionalText("source") ?? string.Empty;
            o.End();
            if (lbs.Count == 0)
            {
                throw Fail(path + ".landblocks", "is empty");
            }

            return new TravelPlace(key, name, lbs, source);
        }

        private ushort ParseLandblock(string text, string path) =>
            text.StartsWith("0x", StringComparison.Ordinal) && ushort.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : throw Fail(path, $"'{text}' is not a 0x hex landblock");

        private TravelDangerTable ReadDanger(Obj o, string path)
        {
            var weenie = o.Uint("weenie");
            var name = o.Text("name");
            var min = o.Int("min_level", 1, 275);
            var max = o.Int("max_level", 1, 275);
            var awareness = o.Number("awareness_m", 1, 100);
            var scatter = o.Number("scatter_m", 0, 100);
            var source = o.Text("source");
            var cells = o.Strings("cells").Select((t, i) => ParseDangerCell(t, $"{path}.cells[{i}]")).ToList();
            o.End();
            if (min > max)
            {
                throw Fail(path + ".min_level", $"{min} is above max_level {max}");
            }

            if (cells.Count == 0)
            {
                throw Fail(path + ".cells", "is empty");
            }

            return new TravelDangerTable(weenie, name, min, max, awareness, scatter, source, cells);
        }

        // "0xF882:4,0": a landblock, then cell_X and cell_Y.
        private TravelDangerCell ParseDangerCell(string text, string path)
        {
            var parts = text.Split(':', ',');
            return parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var x) && x <= 7 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y <= 7
                ? new TravelDangerCell(ParseLandblock(parts[0], path), x, y)
                : throw Fail(path, $"'{text}' is not 0xLLLL:x,y with x and y in 0..7");
        }

        private TravelGem ReadGem(Obj o, string path, Dictionary<string, TravelPlace> places)
        {
            var key = o.Text("key");
            var name = o.Text("name");
            var wcid = o.Uint("wcid");
            var spell = o.Uint("spell");
            var spellName = o.Text("spell_name");
            var destCell = o.Hex("dest_cell");
            var destOrigin = o.Vector("dest_origin");
            var into = o.Text("replaces_hops_into");
            var source = o.Text("source");
            o.End();
            if (!places.TryGetValue(into, out var place))
            {
                throw Fail(path + ".replaces_hops_into", $"'{into}' is not a place");
            }

            if (!place.Landblocks.Contains((ushort)(destCell >> 16)))
            {
                throw Fail(path + ".dest_cell", $"landblock 0x{destCell >> 16:X4} is not a landblock of place '{into}'");
            }

            return new TravelGem(key, name, wcid, spell, spellName, destCell, destOrigin, into, source);
        }

        private TravelEdge ReadEdge(Obj o, string path, Dictionary<string, TravelPlace> places)
        {
            var key = o.Text("key");
            var name = o.Text("name");
            var from = o.Text("from");
            var to = o.Text("to");
            var wcid = o.Uint("wcid");
            var guid = o.Hex("guid");
            var cell = o.Hex("cell");
            var origin = o.Vector("origin");
            var facing = o.Facing("facing");
            var destCell = o.Hex("dest_cell");
            var destOrigin = o.Vector("dest_origin");
            var min = o.IntOrNull("min_level", 1, 275);
            var max = o.IntOrNull("max_level", 1, 275);
            var bitmask = o.Int("bitmask", 0, 255);
            var source = o.Text("source");
            var note = o.OptionalText("note");
            var walkOut = o.OptionalList("walk_out", ReadVia);
            var walkIn = o.OptionalList("walk_in", ReadVia);
            o.End();
            if (walkOut is { Count: 0 })
            {
                throw Fail(path + ".walk_out", "is empty");
            }

            if (walkIn is { Count: < 2 })
            {
                throw Fail(path + ".walk_in", "needs the door's front and at least one point of the way");
            }

            if (!places.TryGetValue(from, out var fromPlace))
            {
                throw Fail(path + ".from", $"'{from}' is not a place");
            }

            if (!places.TryGetValue(to, out var toPlace))
            {
                throw Fail(path + ".to", $"'{to}' is not a place");
            }

            if (!fromPlace.Landblocks.Contains((ushort)(cell >> 16)))
            {
                throw Fail(path + ".cell", $"landblock 0x{cell >> 16:X4} is not a landblock of place '{from}'");
            }

            if (!toPlace.Landblocks.Contains((ushort)(destCell >> 16)))
            {
                throw Fail(path + ".dest_cell", $"landblock 0x{destCell >> 16:X4} is not a landblock of place '{to}'");
            }

            if (min is { } a && max is { } b && a > b)
            {
                throw Fail(path + ".min_level", $"{a} is above max_level {b}");
            }

            return new TravelEdge(key, name, from, to, wcid, guid, cell, origin, destCell, destOrigin, min, max, bitmask, source, note, facing, walkOut, walkIn);
        }

        private RoomWayOut ReadWayOut(Obj o, string path)
        {
            var key = o.Text("key");
            var rooms = o.Strings("rooms").Select((t, i) => ParseRoom(t, $"{path}.rooms[{i}]")).ToHashSet();
            var way = o.List("way", ReadVia);
            _ = o.Text("source");
            o.End();
            if (rooms.Count == 0)
            {
                throw Fail(path + ".rooms", "is empty");
            }

            if (way.Count < 2)
            {
                throw Fail(path + ".way", "needs at least two points");
            }

            return new RoomWayOut(key, rooms, way.Select(p => new CellFrame(p.Cell, new AcFrame(p.Origin, Quaternion.Identity))).ToList());
        }

        private uint ParseRoom(string text, string path) =>
            text.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) && (v & 0xFFFF) >= 0x100 ? v : throw Fail(path, $"'{text}' is not a 0x hex room (an env cell)");

        private static TravelVia ReadVia(Obj a, string path)
        {
            var viaCell = a.Hex("cell");
            var viaOrigin = a.Vector("origin");
            var why = a.Text("why");
            a.End();
            return new TravelVia(viaCell, viaOrigin, why);
        }

        private List<TravelStep> ReadSteps(Obj parent, string name, string path, Dictionary<string, TravelEdge> edges, string fromPlace, bool firstWalkKnown)
        {
            var steps = parent.List(name, (o, p) =>
            {
                var edge = o.Text("edge");
                var walk = o.NumberOrNull("walk_m", 0, 100000);
                o.End();
                if (!edges.TryGetValue(edge, out var e))
                {
                    throw Fail(p + ".edge", $"'{edge}' is not an edge");
                }

                return new TravelStep(e, walk);
            });
            var at = fromPlace;
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Edge.From != at)
                {
                    throw Fail($"{path}.{name}[{i}].edge", $"'{steps[i].Edge.Key}' stands in '{steps[i].Edge.From}', but the climber is in '{at}'");
                }

                if ((i > 0 || firstWalkKnown) != steps[i].WalkMetres.HasValue)
                {
                    throw Fail($"{path}.{name}[{i}].walk_m", i > 0 || firstWalkKnown ? "is missing" : "must be null (the first walk is first_walk_hints)");
                }

                at = steps[i].Edge.To;
            }

            return steps;
        }

        private TravelRoute ReadRoute(Obj o, string path, string ground, Dictionary<string, TravelEdge> edges, Dictionary<string, TravelPlace> places)
        {
            var key = o.Text("key");
            var start = o.Text("start");
            if (!places.ContainsKey(start))
            {
                throw Fail(path + ".start", $"'{start}' is not a place");
            }

            var steps = ReadSteps(o, "steps", path, edges, start, firstWalkKnown: true);
            var run = o.ObjectOrNull("run", (r, p) =>
            {
                var to = r.Object("to", t =>
                {
                    var res = (Wcid: t.Uint("wcid"), Name: t.Text("name"), Guid: t.Hex("guid"), Cell: t.Hex("cell"), Origin: t.Vector("origin"), Facing: t.Facing("facing"));
                    t.End();
                    return res;
                });
                var metres = r.Number("metres", 0, 100000);
                var source = r.Text("source");
                var via = r.OptionalObject("via", (v, q) =>
                {
                    var below = v.Int("below_level", 1, 275);
                    var points = v.List("points", (a, ap) =>
                    {
                        var cell = a.Hex("cell");
                        var origin = a.Vector("origin");
                        var why = a.Text("why");
                        a.End();
                        return new TravelVia(cell, origin, why);
                    });
                    var viaSource = v.Text("source");
                    v.End();
                    return points.Count == 0 ? throw Fail(q + ".points", "is empty") : new TravelViaPlan(below, points, viaSource);
                });
                r.End();
                return new TravelRun(to.Wcid, to.Name, to.Guid, to.Cell, to.Origin, metres, source, to.Facing, via);
            });
            var note = o.OptionalText("note");
            o.End();
            return new TravelRoute(key, ground, start, steps, run, note);
        }

        private WayHome ReadWay(Obj o, string path, Dictionary<string, TravelEdge> edges, Dictionary<string, TravelPlace> places)
        {
            var key = o.Text("key");
            var from = o.Text("from");
            if (!places.ContainsKey(from))
            {
                throw Fail(path + ".from", $"'{from}' is not a place");
            }

            var steps = ReadSteps(o, "steps", path, edges, from, firstWalkKnown: false);
            if (steps.Count == 0)
            {
                throw Fail(path + ".steps", "is empty");
            }

            var hints = o.List("first_walk_hints", (h, p) =>
            {
                var by = h.Text("arrived_by");
                var walk = h.Number("walk_m", 0, 100000);
                h.End();
                if (!edges.TryGetValue(by, out var e) || e.To != from)
                {
                    throw Fail(p + ".arrived_by", $"'{by}' is not an edge that drops in '{from}'");
                }

                return new WalkHint(by, walk);
            });
            var note = o.OptionalText("note");
            o.End();
            return new WayHome(key, from, steps, hints, note);
        }
    }

    /// <summary>A JSON object read field by field: a missing field, an unknown field or a field of the wrong type fails naming the path.</summary>
    private sealed class Obj
    {
        private readonly Reader _reader;
        private readonly JsonElement _element;
        private readonly string _path;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public Obj(Reader reader, JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw reader.Fail(path, $"is {element.ValueKind}, not an object");
            }

            _reader = reader;
            _element = element;
            _path = path;
        }

        public IEnumerable<string> Names() => _element.EnumerateObject().Select(p => p.Name).ToArray();

        public string Text(string name)
        {
            var e = Take(name);
            return e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString()! : throw _reader.Fail(Path(name), $"is {e.ValueKind}, not a non-empty string");
        }

        public string? OptionalText(string name)
        {
            if (!_element.TryGetProperty(name, out _))
            {
                return null;
            }

            return Text(name);
        }

        public int Int(string name, int min, int max)
        {
            var e = Take(name);
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v))
            {
                throw _reader.Fail(Path(name), $"is {e.ValueKind}, not an integer");
            }

            return v >= min && v <= max ? v : throw _reader.Fail(Path(name), $"{v} is outside {min}..{max}");
        }

        public int? IntOrNull(string name, int min, int max)
        {
            if (_element.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Null)
            {
                Take(name);
                return null;
            }

            return Int(name, min, max);
        }

        public uint Uint(string name)
        {
            var e = Take(name);
            return e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out var v) ? v : throw _reader.Fail(Path(name), $"is {e.ValueKind}, not an unsigned integer");
        }

        public double Number(string name, double min, double max)
        {
            var e = Take(name);
            if (e.ValueKind != JsonValueKind.Number)
            {
                throw _reader.Fail(Path(name), $"is {e.ValueKind}, not a number");
            }

            var v = e.GetDouble();
            return v >= min && v <= max ? v : throw _reader.Fail(Path(name), $"{v} is outside {min}..{max}");
        }

        public double? NumberOrNull(string name, double min, double max)
        {
            if (_element.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Null)
            {
                Take(name);
                return null;
            }

            return Number(name, min, max);
        }

        public uint Hex(string name)
        {
            var text = Text(name);
            return text.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : throw _reader.Fail(Path(name), $"'{text}' is not a 0x hex number");
        }

        public List<string> Strings(string name)
        {
            var e = Take(name);
            if (e.ValueKind != JsonValueKind.Array)
            {
                throw _reader.Fail(Path(name), $"is {e.ValueKind}, not an array");
            }

            var list = new List<string>();
            foreach (var item in e.EnumerateArray())
            {
                list.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : throw _reader.Fail($"{Path(name)}[{list.Count}]", $"is {item.ValueKind}, not a string"));
            }

            return list;
        }

        public PortalFacing Facing(string name)
        {
            var o = Object(name, x => x);
            var f = new PortalFacing(o.Number("w", -1.001, 1.001), o.Number("x", -1.001, 1.001), o.Number("y", -1.001, 1.001), o.Number("z", -1.001, 1.001));
            o.End();
            var norm = (f.W * f.W) + (f.X * f.X) + (f.Y * f.Y) + (f.Z * f.Z);
            return Math.Abs(norm - 1) < 0.01 ? f : throw _reader.Fail(Path(name), $"is not a unit quaternion (squared length {norm:0.###})");
        }

        public Vector3 Vector(string name)
        {
            var o = Object(name, x => x);
            var v = new Vector3((float)o.Number("x", -1e6, 1e6), (float)o.Number("y", -1e6, 1e6), (float)o.Number("z", -1e6, 1e6));
            o.End();
            return v;
        }

        public T Object<T>(string name, Func<Obj, T> read) => read(new Obj(_reader, Take(name), Path(name)));

        public T? ObjectOrNull<T>(string name, Func<Obj, string, T> read)
            where T : class
        {
            var e = Take(name);
            return e.ValueKind == JsonValueKind.Null ? null : read(new Obj(_reader, e, Path(name)), Path(name));
        }

        public T? OptionalObject<T>(string name, Func<Obj, string, T> read)
            where T : class => _element.TryGetProperty(name, out _) ? ObjectOrNull(name, read) : null;

        public List<T> List<T>(string name, Func<Obj, string, T> read)
        {
            var e = Take(name);
            if (e.ValueKind != JsonValueKind.Array)
            {
                throw _reader.Fail(Path(name), $"is {e.ValueKind}, not an array");
            }

            var list = new List<T>();
            var i = 0;
            foreach (var item in e.EnumerateArray())
            {
                var p = $"{Path(name)}[{i++}]";
                list.Add(read(new Obj(_reader, item, p), p));
            }

            return list;
        }

        public List<T> List<T>(string name, Func<Obj, T> read) => List(name, (o, _) => read(o));

        public List<T>? OptionalList<T>(string name, Func<Obj, string, T> read) => _element.TryGetProperty(name, out _) ? List(name, read) : null;

        public void End()
        {
            foreach (var p in _element.EnumerateObject())
            {
                if (!_seen.Contains(p.Name))
                {
                    throw _reader.Fail(Path(p.Name), "is not a field of this section");
                }
            }
        }

        private JsonElement Take(string name)
        {
            if (!_element.TryGetProperty(name, out var e))
            {
                throw _reader.Fail(Path(name), "is missing");
            }

            _seen.Add(name);
            return e;
        }

        private string Path(string name) => _path == "$" ? "$." + name : _path + "." + name;
    }
}
