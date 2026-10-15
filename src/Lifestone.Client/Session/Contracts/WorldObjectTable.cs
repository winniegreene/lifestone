using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The objects the server created and has not deleted (<see cref="GameState.Objects"/>), by id. Lookups and enumeration
/// allocate nothing.
/// </summary>
public sealed class WorldObjectTable
{
    private readonly Dictionary<uint, WorldObject> _objects = new(1024);

    /// <summary>An empty table. Only the session creates one.</summary>
    internal WorldObjectTable()
    {
    }

    /// <summary>The number of objects.</summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Every object, in no particular order. Its enumerator is a struct, so <c>foreach</c> allocates nothing; do not keep
    /// it across a tick.
    /// </summary>
    public Dictionary<uint, WorldObject>.ValueCollection All => _objects.Values;

    /// <summary>The object with id <paramref name="guid"/>.</summary>
    /// <param name="guid">The object id.</param>
    /// <param name="value">The object, or null.</param>
    /// <returns>True when it exists.</returns>
    public bool TryGet(uint guid, [NotNullWhen(true)] out WorldObject? value) => _objects.TryGetValue(guid, out value);

    /// <summary>True when the object exists.</summary>
    public bool Contains(uint guid) => _objects.ContainsKey(guid);

    /// <summary>
    /// Appends to <paramref name="into"/> the ids of the objects with a position within <paramref name="metres"/> of
    /// <paramref name="centre"/> (straight-line distance in global metres, as <see cref="CellFrameExtensions.ToGlobal"/>),
    /// and returns how many it appended.
    /// </summary>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="metres">The radius.</param>
    /// <param name="into">Receives the ids; not cleared.</param>
    /// <returns>The number of ids appended.</returns>
    public int Within(in CellFrame centre, float metres, List<uint> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var origin = centre.ToGlobal();
        var added = 0;
        foreach (var o in _objects.Values)
        {
            if (o.Position is { } p && p.ToGlobal().DistanceTo(origin) <= metres)
            {
                into.Add(o.Guid);
                added++;
            }
        }

        return added;
    }

    /// <summary>Adds or replaces an object.</summary>
    internal void Set(WorldObject value) => _objects[value.Guid] = value;

    /// <summary>Removes an object; false when it was not there.</summary>
    internal bool Remove(uint guid) => _objects.Remove(guid);

    /// <summary>Removes every object.</summary>
    internal void Clear() => _objects.Clear();
}
