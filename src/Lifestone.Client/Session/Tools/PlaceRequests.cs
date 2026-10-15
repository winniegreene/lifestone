using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Data.World.Data;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Tools;

/// <summary>
/// The server-mod request that teleports the player to a <see cref="TeleportDestination"/> (step ST12; DoD 9 and 10):
/// one of ACE's points of interest goes by its first ACE name (<c>place poi "&lt;name&gt;"</c>, so ACE teleports to its
/// own Destination for it), and every other destination (the curated dungeons and building interiors, or a location the
/// player typed) by its exact location (<c>place loc &lt;cell&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; [&lt;qw&gt; &lt;qx&gt;
/// &lt;qy&gt; &lt;qz&gt;]</c>, invariant culture, written by the mod's own ToolsWire).
/// </summary>
/// <remarks>
/// A destination is an ACE row when the embedded ACE extract (<see cref="AcePointsOfInterest"/>) has an entry with its
/// name at its place (the same cell, within <see cref="TeleportCatalog.SamePlaceMetres"/>): the rows
/// <see cref="TeleportCatalog.FromAce(AcePointOfInterest)"/> builds, including those a curated interior was merged into.
/// The location of any other destination carries a rotation about Z only, built from its heading (the inverse of
/// <see cref="World.WorldLocations.HeadingFromQuaternion"/>).
/// </remarks>
public static class PlaceRequests
{
    /// <summary>
    /// The request for <paramref name="destination"/>, with the correlation id <paramref name="id"/> (or none).
    /// </summary>
    /// <param name="destination">Where to go.</param>
    /// <param name="id">The request id the reply will echo, or null.</param>
    /// <returns>A <see cref="PlacePoiRequest"/> or a <see cref="PlaceLocRequest"/>.</returns>
    /// <exception cref="ArgumentException">The request would be refused by the mod (a location off the map, a name the
    /// wire cannot carry); ToolsWire's own sentence says why.</exception>
    public static ToolRequest For(TeleportDestination destination, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ToolRequest request = TryFindAcePoint(destination, out var poi) && poi.PoiNames.Count > 0
            ? new PlacePoiRequest(poi.PoiNames[0], id)
            : new PlaceLocRequest(LocationOf(destination), id);

        // Validates it the way the mod parses it (ArgumentException with the reason).
        _ = ToolsWire.Request(request);
        return request;
    }

    /// <summary>
    /// The ACE point of interest <paramref name="destination"/> stands for, when it is an ACE row: an entry of the
    /// embedded extract with the destination's name (ordinal) at the destination's cell, within
    /// <see cref="TeleportCatalog.SamePlaceMetres"/>.
    /// </summary>
    public static bool TryFindAcePoint(TeleportDestination destination, [NotNullWhen(true)] out AcePointOfInterest? poi)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var at = destination.Location.Position;
        foreach (var candidate in AcePointsOfInterest.All)
        {
            if (string.Equals(candidate.DisplayName, destination.Name, StringComparison.Ordinal) &&
                candidate.CellId == at.CellId &&
                Vector3.Distance(candidate.Origin, at.Local) <= TeleportCatalog.SamePlaceMetres)
            {
                poi = candidate;
                return true;
            }
        }

        poi = null;
        return false;
    }

    /// <summary>
    /// The exact location of <paramref name="destination"/>: its cell and origin, with an ACE row's own rotation, or a
    /// rotation about Z for its heading (retail: heading = -2 atan2(z, w), so w = cos(-h/2), z = sin(-h/2)).
    /// </summary>
    public static ToolLocation LocationOf(TeleportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var at = destination.Location.Position;
        if (TryFindAcePoint(destination, out var poi))
        {
            var q = poi.Rotation;
            return new ToolLocation(at.CellId, at.Local.X, at.Local.Y, at.Local.Z, q.W, q.X, q.Y, q.Z);
        }

        var half = -destination.Location.HeadingDegrees * (Math.PI / 180.0) / 2.0;
        var w = (float)Math.Cos(half);
        var z = (float)Math.Sin(half);
        return new ToolLocation(at.CellId, at.Local.X, at.Local.Y, at.Local.Z, w, 0f, 0f, z);
    }
}
