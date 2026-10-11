using System.Globalization;
using System.Text;
using Lifestone.Client.Interiors;
using Lifestone.Client.World;
using Lifestone.Data.World.Data;

namespace Lifestone.Client.Navigation;

/// <summary>Whether a destination is on the landscape or inside an EnvCell (a building interior or a dungeon).</summary>
public enum DestinationKind
{
    Outdoor,
    Indoor,
}

/// <summary>A named place the camera can jump to.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Aliases">Other names it is found by (ACE's point-of-interest names, curated interior keys).</param>
/// <param name="Location">Cell position, height and facing.</param>
/// <param name="Source">Where the entry comes from (for the panel's tooltip and the docs).</param>
public sealed record TeleportDestination(string Name, IReadOnlyList<string> Aliases, WorldLocation Location, string Source)
{
    /// <summary>Outdoor (landscape cell) or indoor (EnvCell).</summary>
    public DestinationKind Kind => AcCoordinates.IsOutdoorCell(Location.Position.CellId) ? DestinationKind.Outdoor : DestinationKind.Indoor;

    /// <summary>
    /// For an indoor place: a dungeon or a building interior, from the curated interiors (<see cref="InteriorLocations"/>)
    /// or from the cell's seen_outside flag (<see cref="TeleportCatalog.ClassifyIndoor"/>); null outdoors and for an
    /// indoor place not classified (no dats).
    /// </summary>
    public InteriorKind? Interior { get; init; }

    /// <summary>Retail map coordinates of the destination's outdoor cell; null indoors (retail shows none there).</summary>
    public MapCoordinates? Map =>
        AcCoordinates.TryGetMapCoordinates(Location.Position.CellId, out var map) ? map : null;

    /// <summary>What the panel shows beside the name: retail map coordinates outdoors, else what kind of indoor place.</summary>
    public string Where => Map is { } map
        ? map.ToString()
        : Interior switch
        {
            InteriorKind.Dungeon => "dungeon",
            InteriorKind.BuildingInterior => "building interior",
            _ => "indoors",
        };

    /// <summary>
    /// The name for messages such as "Jumped to ...": indoors with the kind of place in brackets, unless the name already
    /// says it ("Glenden Wood Dungeon", "Holtburg shop (interior)"); outdoors the name alone.
    /// </summary>
    public string Label
    {
        get
        {
            if (Kind == DestinationKind.Outdoor)
            {
                return Name;
            }

            var name = TeleportCatalog.Normalize(Name);
            return Where.Split(' ').Any(word => name.Contains(TeleportCatalog.Normalize(word), StringComparison.Ordinal)) ? Name : $"{Name} ({Where})";
        }
    }

    /// <summary>
    /// What a search matches as the kind of place: the words of <see cref="Where"/> and "indoors" for every indoor place
    /// (normalised); none outdoors.
    /// </summary>
    public IReadOnlyList<string> KindWords => Kind == DestinationKind.Outdoor
        ? []
        : Where.Split(' ').Append("indoors").Select(TeleportCatalog.Normalize).Distinct().ToArray();

    public override string ToString() => $"{Name} {Location.Position}";
}

/// <summary>
/// The teleport panel's destinations and its search: ACE's points of interest (towns and landmarks; see
/// <see cref="AcePointsOfInterest"/>) plus the interiors track's curated dungeons and building interiors
/// (<see cref="InteriorLocations"/>; <see cref="Default"/>), outdoor first, each group by name.
/// </summary>
public sealed class TeleportCatalog
{
    /// <summary>Two places within this distance (metres) in the same cell are the same place (merged, see <see cref="WithInteriors"/>).</summary>
    public const double SamePlaceMetres = 0.5;

    public TeleportCatalog(IEnumerable<TeleportDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        Destinations = destinations
            .OrderBy(d => d.Kind)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>All destinations: outdoor first, then indoor, each by name.</summary>
    public IReadOnlyList<TeleportDestination> Destinations { get; }

    /// <summary>The catalogue from the embedded ACE extract alone.</summary>
    public static TeleportCatalog FromAce() => new(AcePointsOfInterest.All.Select(FromAce));

    /// <summary>
    /// The teleport panel's catalogue: ACE's points of interest plus every curated dungeon and building interior of
    /// <see cref="InteriorLocations"/> (M1 definition of done item 2 asks for at least three dungeons and a building
    /// interior; ACE's list has neither a classic dungeon nor a building).
    /// </summary>
    public static TeleportCatalog Default() => WithInteriors(AcePointsOfInterest.All.Select(FromAce), InteriorLocations.All);

    /// <summary>
    /// <paramref name="destinations"/> plus <paramref name="interiors"/>. An interior at the same place as a destination
    /// (same cell, origins within <see cref="SamePlaceMetres"/>; ACE's Town Network and Marketplace portals drop exactly
    /// where the curated entries stand) is merged into it: the destination keeps its name and position and gains the
    /// interior's name and key as aliases and its kind. Every other interior becomes a destination of its own.
    /// </summary>
    public static TeleportCatalog WithInteriors(IEnumerable<TeleportDestination> destinations, IEnumerable<InteriorLocation> interiors)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(interiors);
        var list = destinations.ToList();
        foreach (var interior in interiors)
        {
            var index = list.FindIndex(d => SamePlace(d.Location.Position, interior.Location.Position));
            if (index < 0)
            {
                list.Add(FromInterior(interior));
                continue;
            }

            var existing = list[index];
            var aliases = existing.Aliases.Concat(InteriorNames(interior))
                .Where(a => Normalize(a) != Normalize(existing.Name))
                .DistinctBy(Normalize)
                .ToArray();
            list[index] = existing with
            {
                Aliases = aliases,
                Interior = interior.Kind,
                Source = $"{existing.Source}; also the curated interior '{interior.Key}' ({interior.Source})",
            };
        }

        return new TeleportCatalog(list);
    }

    /// <summary>A destination for a curated interior: its name, its key as an alias, its floor position and facing.</summary>
    public static TeleportDestination FromInterior(InteriorLocation interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        var aliases = InteriorNames(interior).Where(a => Normalize(a) != Normalize(interior.Name)).DistinctBy(Normalize).ToArray();
        return new TeleportDestination(interior.Name, aliases, interior.Location, $"curated interior '{interior.Key}' ({interior.Source})")
        {
            Interior = interior.Kind,
        };
    }

    /// <summary>
    /// A copy in which every indoor destination nobody classified (ACE's indoor points of interest: the Underground City,
    /// AC Storage, Hotel Swank!, the Night Club) gets its kind from its cell's seen_outside flag, as the position HUD
    /// names it ("indoors (dungeon)" or "indoors (building)"): not seen_outside is a dungeon, seen_outside a building
    /// interior. <paramref name="seenOutside"/> reads the flag (null: unknown, the destination stays unclassified).
    /// </summary>
    public TeleportCatalog ClassifyIndoor(Func<uint, bool?> seenOutside)
    {
        ArgumentNullException.ThrowIfNull(seenOutside);
        return new TeleportCatalog(Destinations.Select(d =>
            d.Kind != DestinationKind.Indoor || d.Interior is not null || seenOutside(d.Location.Position.CellId) is not { } seen
                ? d
                : d with
                {
                    Interior = seen ? InteriorKind.BuildingInterior : InteriorKind.Dungeon,
                    Source = $"{d.Source}; kind from the cell's seen_outside flag ({(seen ? "set" : "clear")})",
                }));
    }

    private static IEnumerable<string> InteriorNames(InteriorLocation interior) => [interior.Name, interior.Key];

    private static bool SamePlace(CellPosition a, CellPosition b) =>
        a.CellId == b.CellId && System.Numerics.Vector3.Distance(a.Local, b.Local) <= SamePlaceMetres;

    /// <summary>A destination from an ACE point of interest: its portal's Destination cell, origin and heading.</summary>
    public static TeleportDestination FromAce(AcePointOfInterest poi)
    {
        ArgumentNullException.ThrowIfNull(poi);

        // Destination rotations are about Z only (x = y = 0 in every entry); retail heading = -2 atan2(qz, qw).
        var heading = WorldLocations.HeadingFromQuaternion(poi.Rotation.W, poi.Rotation.Z);
        var location = new WorldLocation(poi.DisplayName, new CellPosition(poi.CellId, poi.Origin), true, heading);
        var aliases = poi.PoiNames.Where(n => !string.Equals(n, poi.DisplayName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new TeleportDestination(
            poi.DisplayName,
            aliases,
            location,
            string.Create(CultureInfo.InvariantCulture, $"ACE point of interest -> weenie {poi.Wcid} ({poi.WeenieName}) Destination"));
    }

    /// <summary>
    /// Destinations matching <paramref name="query"/>, best first: an exact name or alias, then a name or alias that
    /// starts with the query, then one that contains it, and last the kind of place (<see cref="TeleportDestination.KindWords"/>:
    /// "dungeon", "building", "interior", "indoors"), so typing "dungeon" lists every dungeon. A kind matches only as a
    /// whole word or a prefix of at least <see cref="MinKindPrefix"/> letters, so a short query such as "in" or "do" does
    /// not append every indoor place. Matching ignores case, spaces and punctuation ("hebian to" finds Hebian-To,
    /// "qalabar" finds Qalaba'r). An empty query returns everything.
    /// </summary>
    public IReadOnlyList<TeleportDestination> Search(string? query)
    {
        var q = Normalize(query);
        if (q.Length == 0)
        {
            return Destinations;
        }

        return Destinations
            .Select(d => (Destination: d, Rank: Rank(d, q)))
            .Where(x => x.Rank < int.MaxValue)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Destination.Kind)
            .ThenBy(x => x.Destination.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Destination)
            .ToArray();
    }

    /// <summary>The single best match, or null.</summary>
    public TeleportDestination? Find(string? query) => Normalize(query).Length == 0 ? null : Search(query).FirstOrDefault();

    /// <summary>Lower-case letters and digits only.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>The shortest query that matches a kind of place by prefix ("dun" finds dungeons, "in" finds nothing by kind).</summary>
    public const int MinKindPrefix = 3;

    private static int Rank(TeleportDestination d, string q)
    {
        var best = int.MaxValue;
        foreach (var (name, penalty) in d.Aliases.Select(a => (a, 1)).Prepend((d.Name, 0)))
        {
            var n = Normalize(name);
            var rank = n == q ? 0 : n.StartsWith(q, StringComparison.Ordinal) ? 10 : n.Contains(q, StringComparison.Ordinal) ? 20 : int.MaxValue;
            if (rank != int.MaxValue)
            {
                best = Math.Min(best, rank + penalty);
            }
        }

        // The kind of an indoor place ranks after every name or alias match (penalty above the substring rank).
        foreach (var word in d.KindWords)
        {
            var rank = word == q ? 0 : q.Length >= MinKindPrefix && word.StartsWith(q, StringComparison.Ordinal) ? 10 : int.MaxValue;
            if (rank != int.MaxValue)
            {
                best = Math.Min(best, rank + 25);
            }
        }

        return best;
    }
}
