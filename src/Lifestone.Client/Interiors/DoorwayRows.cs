namespace Lifestone.Client.Interiors;

/// <summary>
/// The doorway-plane table the cell shaders clip with (docs/interiors.md §3.4): one row per building doorway looked
/// into this frame, holding its plane and the cone from the eye through it.
/// </summary>
public static class DoorwayRows
{
    /// <summary>
    /// Cone planes per row: the most edges a building doorway polygon has in the dats. Of the 16,937 doorways, 100 have 8
    /// corners, 39 have 9 and 4 have 10 (e.g. building model 0x01003624); a cone has at most one
    /// plane per edge (<c>ViewVolume.Through</c>). Pinned by <c>Every_building_doorway_cone_fits_the_doorway_table</c>.
    /// </summary>
    public const int MaxConePlanes = 10;

    /// <summary>Rows in the table: the nearest doorways get them (the Arwic area shows 41-47 doorways at once).</summary>
    public const int Capacity = 128;

    /// <summary>
    /// Gives rows to the doorways nearest the eye first: <paramref name="rowOfDoorway"/>[i] is the row of
    /// <paramref name="doorways"/>[i], or -1 when the table is full, and <paramref name="doorwayOfRow"/> lists the
    /// doorway index of each row in order.
    /// </summary>
    /// <returns>Rows used.</returns>
    public static int Assign(IReadOnlyList<DoorwayView> doorways, int capacity, Span<int> rowOfDoorway, List<int> doorwayOfRow)
    {
        ArgumentNullException.ThrowIfNull(doorways);
        ArgumentNullException.ThrowIfNull(doorwayOfRow);
        if (rowOfDoorway.Length < doorways.Count)
        {
            throw new ArgumentException($"{rowOfDoorway.Length} slots for {doorways.Count} doorways.", nameof(rowOfDoorway));
        }

        doorwayOfRow.Clear();
        for (var i = 0; i < doorways.Count; i++)
        {
            rowOfDoorway[i] = -1;
            doorwayOfRow.Add(i);
        }

        if (doorways.Count > capacity)
        {
            // Nearest first; ties by index so the result is deterministic.
            doorwayOfRow.Sort((a, b) =>
            {
                var c = doorways[a].DistanceSquared.CompareTo(doorways[b].DistanceSquared);
                return c != 0 ? c : a.CompareTo(b);
            });
            doorwayOfRow.RemoveRange(capacity, doorwayOfRow.Count - capacity);
        }

        for (var row = 0; row < doorwayOfRow.Count; row++)
        {
            rowOfDoorway[doorwayOfRow[row]] = row;
        }

        return doorwayOfRow.Count;
    }
}
