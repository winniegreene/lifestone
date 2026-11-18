using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>A door that opens only by its linked lever (M6 4 t4a, <c>TODO(door-lever)</c>): the walker cannot open it, so no Town Network trip plans a walk through it.</summary>
/// <param name="Key">The door's key.</param>
/// <param name="Town">The town it stands in.</param>
/// <param name="DoorGuid">The door's landblock_instance guid.</param>
/// <param name="DoorWcid">The door's weenie.</param>
/// <param name="DoorCell">The door's stored cell.</param>
/// <param name="DoorOrigin">The door's landblock-local point.</param>
/// <param name="LeverGuid">The lever's landblock_instance guid (the link's child).</param>
/// <param name="LeverWcid">The lever's weenie.</param>
/// <param name="Link">The landblock_instance_link row's id.</param>
/// <param name="Behind">The instances (vendors, portals) a walk from the town's Network drop reaches only through the door.</param>
/// <param name="Source">The SQL rows.</param>
public sealed record LeverDoor(string Key, string Town, uint DoorGuid, uint DoorWcid, uint DoorCell, Vector3 DoorOrigin, uint LeverGuid, uint LeverWcid, int Link, IReadOnlyList<uint> Behind, string Source);

/// <summary>A lever-door file error: the JSON path and the problem.</summary>
public sealed class LeverDoorsException(string path, string problem) : Exception($"{LeverDoors.FileName} {path}: {problem}")
{
    /// <summary>The JSON path of the field at fault.</summary>
    public string JsonPath { get; } = path;
}

/// <summary>
/// The lever-only doors as data (M6 4 t4a, D26, D28: <c>Session/Scenarios/Journey/lever-doors.json</c>, each with its landblock_instance and landblock_instance_link rows). The only one known is Fort Tethana's gate (door 0x72581042, lever 0x72581044,
/// link 298488): the walker would Use the door, which ACE refuses from outside ("This door cannot be activated from here."), so the Grand Master Scriveners and the town's own Portal to Town Network inside the walls are out of a trip's
/// reach until <c>TODO(door-lever)</c>. A missing or unknown field fails at load with its path.
/// </summary>
public sealed class LeverDoors
{
    /// <summary>The file's name.</summary>
    public const string FileName = "lever-doors.json";

    /// <summary>The embedded file's logical name (Lifestone.Client.csproj).</summary>
    internal const string ResourceName = "Lifestone.Client.Session.Scenarios.Journey." + FileName;

    private static readonly Lazy<LeverDoors> Loaded = new(() =>
    {
        using var stream = typeof(LeverDoors).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"the lever doors are not embedded ({ResourceName}; Lifestone.Client.csproj)");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    private LeverDoors(IReadOnlyList<LeverDoor> doors) => Doors = doors;

    /// <summary>The embedded file.</summary>
    public static LeverDoors Default => Loaded.Value;

    /// <summary>No door (a test's world without one).</summary>
    public static LeverDoors None { get; } = new([]);

    /// <summary>The doors.</summary>
    public IReadOnlyList<LeverDoor> Doors { get; }

    /// <summary>The door a walk to the instance <paramref name="guid"/> passes, or null.</summary>
    /// <param name="guid">A vendor's or a portal's guid.</param>
    public LeverDoor? Before(uint guid) => Doors.FirstOrDefault(d => d.Behind.Contains(guid));

    /// <summary>Reads a file; throws <see cref="LeverDoorsException"/> on any error.</summary>
    /// <param name="json">The text.</param>
    internal static LeverDoors Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new LeverDoorsException("$", "is not JSON: " + e.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            Only(root, "$", "key", "name", "source", "doors");
            Text(root, "$", "source");
            var doors = new List<LeverDoor>();
            var i = 0;
            foreach (var d in Get(root, "$", "doors").EnumerateArray())
            {
                var p = $"$.doors[{i++}]";
                Only(d, p, "key", "town", "door", "lever", "link", "behind", "source");
                var door = Get(d, p, "door");
                var lever = Get(d, p, "lever");
                Only(door, p + ".door", "guid", "wcid", "cell", "origin");
                Only(lever, p + ".lever", "guid", "wcid", "cell", "origin");
                var behind = new List<uint>();
                var j = 0;
                foreach (var b in Get(d, p, "behind").EnumerateArray())
                {
                    Only(b, $"{p}.behind[{j}]", "guid", "what");
                    Text(b, $"{p}.behind[{j}]", "what");
                    behind.Add(Hex(b, $"{p}.behind[{j++}]", "guid"));
                }

                if (behind.Count == 0)
                {
                    throw new LeverDoorsException(p + ".behind", "is empty: a door with nothing behind it changes no trip");
                }

                var o = Get(door, p + ".door", "origin");
                doors.Add(new LeverDoor(
                    Text(d, p, "key"),
                    Text(d, p, "town"),
                    Hex(door, p + ".door", "guid"),
                    Get(door, p + ".door", "wcid").GetUInt32(),
                    Hex(door, p + ".door", "cell"),
                    new Vector3(Get(o, p + ".door.origin", "x").GetSingle(), Get(o, p + ".door.origin", "y").GetSingle(), Get(o, p + ".door.origin", "z").GetSingle()),
                    Hex(lever, p + ".lever", "guid"),
                    Get(lever, p + ".lever", "wcid").GetUInt32(),
                    Get(d, p, "link").GetInt32(),
                    behind,
                    Text(d, p, "source")));
            }

            return new LeverDoors(doors);
        }
    }

    private static void Only(JsonElement e, string path, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw new LeverDoorsException(path, "is not an object");
        }

        foreach (var p in e.EnumerateObject())
        {
            if (!names.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new LeverDoorsException(path + "." + p.Name, "is not a known field");
            }
        }
    }

    private static JsonElement Get(JsonElement e, string path, string name) =>
        e.TryGetProperty(name, out var v) ? v : throw new LeverDoorsException(path + "." + name, "is missing");

    private static string Text(JsonElement e, string path, string name) =>
        Get(e, path, name) is { ValueKind: JsonValueKind.String } v && v.GetString() is { Length: > 0 } s ? s : throw new LeverDoorsException(path + "." + name, "is not a non-empty string");

    private static uint Hex(JsonElement e, string path, string name)
    {
        var s = Text(e, path, name);
        return s.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : throw new LeverDoorsException(path + "." + name, $"'{s}' is not 0x-hex");
    }
}

/// <summary>A place a trip may go to: the town, the vendor (a scrivener or a scarab seller) and his catalog place.</summary>
/// <param name="Town">The catalog's town.</param>
/// <param name="Name">The vendor's name.</param>
/// <param name="Wcid">The vendor's weenie.</param>
/// <param name="Vendor">His catalog place.</param>
public sealed record TownTripCandidate(string Town, string Name, uint Wcid, ScrivenerPlace Vendor);

/// <summary>What the trip's rule reads of the character.</summary>
/// <param name="StartPlace">The travel place the trip starts from (the home lifestone's), or null when no place holds it.</param>
/// <param name="Level">The character's level.</param>
/// <param name="KnowsLifestoneRecall">The spellbook holds Lifestone Recall (the way home).</param>
/// <param name="HomeIsTravelStart">The ground's home town is the travel file's start (the portals' way home ends there).</param>
public sealed record TownTripInput(string? StartPlace, int Level, bool KnowsLifestoneRecall, bool HomeIsTravelStart);

/// <summary>A trip by the Town Network: the town's travel place, the portals out in order, the drop, the straight metres from the drop to the vendor, and the way home.</summary>
/// <param name="Candidate">The town and the vendor.</param>
/// <param name="Place">The town's travel place.</param>
/// <param name="Hops">The portals out, in order (empty when the trip starts in the town).</param>
/// <param name="DropCell">The last hop's drop cell.</param>
/// <param name="DropOrigin">The last hop's drop point.</param>
/// <param name="DropMetres">The straight metres from the drop to the vendor's point (the choice's measure).</param>
/// <param name="Home"><c>lifestone-recall</c>, or <c>portals:&lt;way&gt;</c> (the rung change's <c>direction=home</c>).</param>
public sealed record TownTripPlan(TownTripCandidate Candidate, TravelPlace Place, IReadOnlyList<RungPortal> Hops, uint DropCell, Vector3 DropOrigin, double DropMetres, string Home)
{
    /// <summary>True when the way home is Lifestone Recall.</summary>
    public bool HomeByRecall => Home == TownNetworkTrip.HomeByRecall;

    /// <summary>The hops as the log writes them.</summary>
    public string HopKeys => Hops.Count == 0 ? "none" : string.Join(",", Hops.Select(h => h.Key));
}

/// <summary>The rule's answer for one candidate: the plan, or the refusal's token and why.</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="Plan">The plan, or null.</param>
/// <param name="Refusal">The refusal's token (<c>off-network</c>, <c>no-route</c>, <c>gate:...</c>, <c>no-way-back</c>, <c>no-way-home</c>, <c>lever-door:&lt;key&gt;</c>), or null.</param>
/// <param name="Why">The reason in words.</param>
public sealed record TownTripVerdict(TownTripCandidate Candidate, TownTripPlan? Plan, string? Refusal, string Why);

/// <summary>The choice among the candidates: the chosen plan (or the named stop) and every verdict.</summary>
/// <param name="Chosen">The chosen plan, or null.</param>
/// <param name="Verdicts">Each candidate's verdict, in the candidates' order.</param>
/// <param name="Stop">The named stop when none is chosen (<c>TODO(door-lever)</c> when a lever door refused one), else null.</param>
/// <param name="Why">The choice and every refusal in one line (logged).</param>
public sealed record TownTripChoice(TownTripPlan? Chosen, IReadOnlyList<TownTripVerdict> Verdicts, string? Stop, string Why);

/// <summary>
/// M6 4 t4a (D40, D43, D44; closes <c>TODO(m6-scrivener-town-network)</c> and <c>TODO(m6-buy-trip-town-network)</c>): the town a trip by the Town Network goes to, as a pure rule over the travel file
/// (<see cref="TravelRoutes"/>) and the lever doors (<see cref="LeverDoors"/>). A candidate (a town and its vendor) is admitted when:
/// <list type="number">
/// <item>the vendor's town is a travel place (the place whose landblocks hold his landblock, else the place named as the town): else <c>off-network</c>;</item>
/// <item>the file leads there: an edge into the place, reached from the start place directly, or by the start's own way home up to that edge's place (the Network): else <c>no-route</c>; every portal's gate admits the level (else the gate's
/// refusal by name, as the rung change's);</item>
/// <item>the town has its own way back in the file (<see cref="TravelRoutes.WayHomeFrom"/>), else <c>no-way-back</c>; and the way home is Lifestone Recall when the book holds it, else the portals (the rung change's <c>direction=home</c>), which
/// lead to the travel file's start, so a ground whose home is another town is <c>no-way-home</c>;</item>
/// <item>no walk of the trip passes a lever-only door: the vendor, a portal out, or (home by portals) the way back's first portal is not behind one (<c>lever-door:&lt;key&gt;</c>; Fort Tethana's Grand Masters and its Network portal, so
/// another Grand Master town on the Network is chosen).</item>
/// </list>
/// Of the admitted, the first in the caller's order is chosen (the learn: the towns by name, as <see cref="ScrivenerCatalog.TownsSelling"/>; the buy-trip: the catalog's sellers, Zisaha of Fort Tethana first, D40); the log gives every
/// other admitted town with its straight metres from the drop. No town or door fact lives here (D26): each comes from the travel file, the catalog or the lever doors.
/// </summary>
public static class TownNetworkTrip
{
    /// <summary>The journey's <c>travel=</c> word for a trip by the hop.</summary>
    public const string TravelWord = "town-network";

    /// <summary>The way home by Lifestone Recall.</summary>
    public const string HomeByRecall = "lifestone-recall";

    /// <summary>The travel place of the vendor's town: the place whose landblocks hold his landblock, else the place whose name is his town's; null when the town is not on the travel file.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="routes">The travel file.</param>
    public static TravelPlace? PlaceOf(TownTripCandidate candidate, TravelRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(routes);
        var landblock = (ushort)(candidate.Vendor.Cell >> 16);
        return routes.Places.FirstOrDefault(p => p.Landblocks.Contains(landblock)) ?? routes.Places.FirstOrDefault(p => string.Equals(p.Name, candidate.Town, StringComparison.Ordinal));
    }

    /// <summary>The travel place's key that holds <paramref name="landblock"/>, or null.</summary>
    /// <param name="landblock">The landblock.</param>
    /// <param name="routes">The travel file.</param>
    public static string? PlaceKeyOf(uint landblock, TravelRoutes routes) => (routes ?? throw new ArgumentNullException(nameof(routes))).PlaceOf(RungRoute.Hex(landblock))?.Key;

    /// <summary>The verdict for one candidate (see the class).</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="input">The character.</param>
    /// <param name="routes">The travel file.</param>
    /// <param name="doors">The lever doors.</param>
    public static TownTripVerdict Judge(TownTripCandidate candidate, TownTripInput input, TravelRoutes routes, LeverDoors doors)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(doors);
        var c = CultureInfo.InvariantCulture;
        if (PlaceOf(candidate, routes) is not { } place)
        {
            return new(candidate, null, "off-network", $"{candidate.Town} is no place of {TravelRoutes.FileName}");
        }

        if (input.StartPlace is not { } start)
        {
            return new(candidate, null, "no-route", "the trip's start (the home lifestone's landblock) is in no travel place");
        }

        var hops = HopsTo(place, start, input.Level, routes);
        if (hops is null)
        {
            return new(candidate, null, "no-route", $"no edge of {TravelRoutes.FileName} leads from {start} to {place.Key}");
        }

        foreach (var hop in hops)
        {
            if (hop.Refusal(input.Level) is { } refused)
            {
                return new(candidate, null, refused, string.Create(c, $"level {input.Level} does not pass {hop.Name}"));
            }
        }

        var back = routes.WayHomeFrom(place.Key, input.Level);
        var toStart = place.Key == routes.StartPlace; // M6 8 B2c: a trip to the start place needs no way back from it (a low-level fetch's last go home, TownNetworkWalkDatTests.Fetch)
        if (back.Outcome != TravelOutcome.Admitted && !input.KnowsLifestoneRecall && !toStart)
        {
            // M6 8 B1: a place with no portal way back (the stone cave's run from Yinar) is still a trip for a book that holds Lifestone Recall, the way home the plan names below.
            return new(candidate, null, "no-way-back", $"{place.Key} has no way back in {TravelRoutes.FileName} ({back.Stop})");
        }

        if (!input.KnowsLifestoneRecall && !input.HomeIsTravelStart)
        {
            return new(candidate, null, "no-way-home", $"the book lacks Lifestone Recall and the portals' way home leads to {routes.StartPlace}, not the ground's home");
        }

        var walked = new List<(uint Guid, string What)> { (candidate.Vendor.Guid, candidate.Name) };
        walked.AddRange(hops.Select(h => (h.Guid, h.Name)));
        if (!input.KnowsLifestoneRecall && !toStart)
        {
            walked.Add((back.WayHome!.Steps[0].Edge.Guid, "the way back's first portal, " + back.WayHome.Steps[0].Edge.Name));
        }

        foreach (var (guid, what) in walked)
        {
            if (guid != 0 && doors.Before(guid) is { } door)
            {
                return new(candidate, null, "lever-door:" + door.Key, string.Create(c, $"{what} 0x{guid:X8} is behind {door.Town}'s door 0x{door.DoorGuid:X8}, which opens only by its lever 0x{door.LeverGuid:X8} (TODO(door-lever))"));
            }
        }

        var (dropCell, dropOrigin) = hops.Count == 0 ? (candidate.Vendor.Cell, candidate.Vendor.Point) : (routes.Edges.First(e => e.Key == hops[^1].Key).DestCell, routes.Edges.First(e => e.Key == hops[^1].Key).DestOrigin);
        var metres = Vector2.Distance(ResupplyPlanner.WorldMetres(dropCell, dropOrigin), ResupplyPlanner.WorldMetres(candidate.Vendor.Cell, candidate.Vendor.Point));
        var home = input.KnowsLifestoneRecall ? HomeByRecall : toStart ? "here" : "portals:" + back.WayHome!.Key;
        var plan = new TownTripPlan(candidate, place, hops, dropCell, dropOrigin, metres, home);
        return new(candidate, plan, null, string.Create(c, $"{candidate.Name} in {candidate.Town}: hops {plan.HopKeys}, the drop 0x{dropCell:X8} {metres:0} m from him, home by {home}"));
    }

    /// <summary>The choice among <paramref name="candidates"/> (see the class): the first admitted in the given order, else the named stop.</summary>
    /// <param name="candidates">The towns and vendors.</param>
    /// <param name="input">The character.</param>
    /// <param name="routes">The travel file.</param>
    /// <param name="doors">The lever doors.</param>
    public static TownTripChoice Choose(IEnumerable<TownTripCandidate> candidates, TownTripInput input, TravelRoutes routes, LeverDoors doors)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var verdicts = candidates.Select(k => Judge(k, input, routes, doors)).ToList();
        var chosen = verdicts.FirstOrDefault(v => v.Plan is not null);
        var refused = verdicts.Where(v => v.Plan is null).Select(v => $"{v.Candidate.Town} refused {v.Refusal}: {v.Why}").ToList();
        var others = verdicts.Where(v => v.Plan is not null && v != chosen).Select(v => string.Create(CultureInfo.InvariantCulture, $"{v.Candidate.Town} {v.Plan!.DropMetres:0} m"));
        var tail = (refused.Count == 0 ? string.Empty : "; " + string.Join("; ", refused)) + (others.Any() ? "; also admitted: " + string.Join(", ", others) : string.Empty);
        if (chosen is null)
        {
            var stop = verdicts.Any(v => v.Refusal?.StartsWith("lever-door:", StringComparison.Ordinal) == true) ? "TODO(door-lever)" : "town-network:no-town";
            return new(null, verdicts, stop, $"no town is admitted ({stop}){tail}");
        }

        return new(chosen.Plan, verdicts, null, $"chose {chosen.Why} (the first admitted){tail}");
    }

    /// <summary>The portals from <paramref name="start"/> into <paramref name="place"/>: an edge into the place reached from the start directly or by the start's way home up to that edge's place; null when none.</summary>
    private static List<RungPortal>? HopsTo(TravelPlace place, string start, int level, TravelRoutes routes)
    {
        if (start == place.Key)
        {
            return [];
        }

        foreach (var into in routes.Edges.Where(e => e.To == place.Key))
        {
            List<TravelEdge>? before = null;
            if (into.From == start)
            {
                before = [];
            }
            else if (routes.Edges.FirstOrDefault(e => e.From == start && e.To == into.From) is { } direct)
            {
                before = [direct];
            }
            else if (routes.WayHomeFrom(start, level) is { Outcome: TravelOutcome.Admitted, WayHome: { } way } && way.Steps.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Edge.To == into.From) is { s: not null } hit)
            {
                before = [.. way.Steps.Take(hit.i + 1).Select(s => s.Edge)];
            }

            if (before is not null)
            {
                return [.. before.Append(into).Select(e => RungPortal.Of(e, routes))];
            }
        }

        return ChainTo(place, start, routes);
    }

    /// <summary>M6 8 B1: the fewest hops of the file's edges from <paramref name="start"/> into <paramref name="place"/>, when the rule above finds none (the chains of outdoor portals to Crater Lake and to the stone cave); the file's order breaks a tie; null when none leads there.</summary>
    private static List<RungPortal>? ChainTo(TravelPlace place, string start, TravelRoutes routes)
    {
        var cameBy = new Dictionary<string, TravelEdge>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var frontier = new Queue<string>();
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            var at = frontier.Dequeue();
            foreach (var e in routes.Edges.Where(e => e.From == at))
            {
                if (!seen.Add(e.To))
                {
                    continue;
                }

                cameBy[e.To] = e;
                if (e.To == place.Key)
                {
                    var chain = new List<TravelEdge>();
                    for (var hop = e; ; hop = cameBy[hop.From])
                    {
                        chain.Insert(0, hop);
                        if (hop.From == start)
                        {
                            return [.. chain.Select(c => RungPortal.Of(c, routes))];
                        }
                    }
                }

                frontier.Enqueue(e.To);
            }
        }

        return null;
    }
}
