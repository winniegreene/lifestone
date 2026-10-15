using System.Globalization;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>One row of ACE's <c>points_of_interest</c> table: a name and the portal weenie it teleports through.</summary>
/// <param name="Name">The point of interest's name, as ACE spells it.</param>
/// <param name="Wcid">The portal weenie's class id.</param>
internal readonly record struct PoiRow(string Name, uint Wcid);

/// <summary>A portal weenie left out of a <c>places</c> reply because ACE has no destination for it.</summary>
/// <param name="Wcid">The portal weenie's class id.</param>
/// <param name="Names">Its names, in the reply's order.</param>
internal sealed record PlacesSkip(uint Wcid, IReadOnlyList<string> Names);

/// <summary>What <see cref="PlacesBuilder.Build"/> made: the reply's data and what it left out.</summary>
/// <param name="Data">The reply's data.</param>
/// <param name="Skipped">The portal weenies left out, in the reply's order.</param>
internal sealed record PlacesBuild(PlacesData Data, IReadOnlyList<PlacesSkip> Skipped)
{
    /// <summary>The reply's message: the names left out, or null when none were.</summary>
    public string? Note => Skipped.Count == 0 ? null : "left out, with no destination in ACE: " + string.Join(", ", Skipped.SelectMany(s => s.Names));

    /// <summary>The reply's chat sentence.</summary>
    public string Sentence => Note is { } note ? Data.Describe() + "; " + note : Data.Describe();
}

/// <summary>
/// Builds the <c>places</c> reply (<see cref="PlacesData"/>) from ACE's point-of-interest rows, with nothing from ACE in
/// it, so Lifestone.ServerMod.Tests runs it (PlacesBuilderTests). PlacesVerb feeds it ACE's rows and destinations.
/// </summary>
/// <remarks>
/// One entry per portal weenie, with all its names. The rows are taken in ordinal name order (stable, so rows with the
/// same name keep the caller's order), and the entries come in the order of their first name, each with its names in
/// order. A weenie without a destination is left out and reported in <see cref="PlacesBuild.Skipped"/>; the
/// destinations are written raw, as ACE's weenie stores them (WeenieExtensions.cs:104-113).
/// </remarks>
internal static class PlacesBuilder
{
    /// <summary>Builds the reply from <paramref name="rows"/>.</summary>
    /// <param name="rows">ACE's point-of-interest rows; every name must be non-empty.</param>
    /// <param name="destinationOf">A portal weenie's Destination position, or null when the weenie or its Destination is missing.</param>
    public static PlacesBuild Build(IEnumerable<PoiRow> rows, Func<uint, ToolLocation?> destinationOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(destinationOf);
        var ordered = rows.OrderBy(r => r.Name, StringComparer.Ordinal).ToArray();
        foreach (var row in ordered)
        {
            if (string.IsNullOrEmpty(row.Name))
            {
                throw new ArgumentException("a point-of-interest row has no name (portal weenie " + row.Wcid.ToString(CultureInfo.InvariantCulture) + ")", nameof(rows));
            }
        }

        var entries = new List<PoiEntry>();
        var skipped = new List<PlacesSkip>();
        foreach (var group in ordered.GroupBy(r => r.Wcid))
        {
            var names = group.Select(r => r.Name).ToArray();
            if (destinationOf(group.Key) is not { } at)
            {
                skipped.Add(new PlacesSkip(group.Key, names));
                continue;
            }

            entries.Add(new PoiEntry(names, group.Key, ToolsWire.FormatCell(at.Cell), at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz));
        }

        return new PlacesBuild(new PlacesData(entries.Count, entries.Sum(e => e.Names.Count), entries), skipped);
    }
}
