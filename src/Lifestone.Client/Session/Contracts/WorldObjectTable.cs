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

    /// <summary>
    /// Finds the object named <paramref name="name"/> (ordinal) with a position nearest to <paramref name="centre"/>, skipping <paramref name="exclude"/>. Allocates nothing.
    /// The auto-target uses it because the wire's attacker notices carry only a name (<c>TODO(m3-autotarget-attacker-id)</c>).
    /// </summary>
    /// <param name="name">The name to match.</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="exclude">An id to skip (the player), or 0.</param>
    /// <param name="guid">The nearest match's id, or 0.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestNamed(string name, in CellFrame centre, uint exclude, out uint guid)
    {
        ArgumentNullException.ThrowIfNull(name);
        var origin = centre.ToGlobal();
        var best = double.MaxValue;
        guid = 0;
        foreach (var o in _objects.Values)
        {
            if (o.Guid == exclude || o.Position is not { } p || !string.Equals(o.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d < best)
            {
                best = d;
                guid = o.Guid;
            }
        }

        return guid != 0;
    }

    /// <summary>The CreateObject description flag of a lifestone (<c>BF_LIFESTONE</c>, 0x4000; ACE <c>ObjectDescriptionFlag.LifeStone</c>), as <c>WorldContentCensus</c> reads it.</summary>
    public const uint LifestoneFlag = 0x4000;

    /// <summary>The CreateObject description flag of a corpse (<c>BF_CORPSE</c>, 0x2000; ACE <c>ObjectDescriptionFlag.Corpse</c>), as <c>WorldContentCensus</c> reads it.</summary>
    public const uint CorpseFlag = 0x2000;

    /// <summary>
    /// Finds the object nearest to <paramref name="centre"/> whose CreateObject description flags (<see cref="WorldObject.DescriptionFlags"/>, the <c>ObjectDescriptionFlags</c> bitfield
    /// <c>WorldContentCensus</c> reads) contain <b>any</b> of the bits in <paramref name="descriptionFlags"/> (<see cref="LifestoneFlag"/>, <see cref="CorpseFlag"/>). Only objects with a position count (a held
    /// or carried object has none). Distance is the straight line in global metres (<see cref="CellFrameExtensions.ToGlobal"/>, the frame <see cref="Within"/> and
    /// <see cref="NearestCreature(IReadOnlyList{uint}, in CellFrame, float, Func{uint, bool}, out uint)"/> use), at most <paramref name="maxMetres"/>. Allocates nothing.
    /// </summary>
    /// <param name="descriptionFlags">The bits to look for; 0 matches nothing.</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="guid">The nearest matching object's id, or 0.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestWithFlags(uint descriptionFlags, in CellFrame centre, float maxMetres, out uint guid)
    {
        guid = 0;
        if (descriptionFlags == 0)
        {
            return false;
        }

        var origin = centre.ToGlobal();
        var best = (double)maxMetres;
        var found = false;
        foreach (var o in _objects.Values)
        {
            if ((o.DescriptionFlags & descriptionFlags) == 0 || o.Position is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= best)
            {
                best = d;
                guid = o.Guid;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The weenie description's ItemType bit for a creature (<c>ITEM_TYPE::TYPE_CREATURE</c>, 0x10), as <c>AvatarWire</c> reads it.</summary>
    private const uint CreatureItemType = 0x10;

    /// <summary>The low word of <c>MotionCommand.Dead</c> (0x40000011), as the wire carries it in <c>ServerMotionState.Forward</c>: ACE sends it a moment after the kill notice, and keeps the body for its death animation.</summary>
    public const ushort DeadMotionLowWord = 0x0011;

    /// <summary>
    /// Finds the creature nearest to <paramref name="centre"/> whose weenie class is in <paramref name="wcids"/>: its
    /// CreateObject's ItemType has the creature bit, it has a position, it is not known to be dead (its last health
    /// fraction is above zero, or it has none yet), its forward motion is not Dead (<see cref="DeadMotionLowWord"/>) and its full cell id passes <paramref name="cellFilter"/>. Retail's
    /// rule for what may be attacked is <c>ClientCombatSystem::ObjectIsAttackable</c> (11.6096 0x0056B340) and its choice of target is <c>ClientCombatSystem::GetAttackTarget</c> (11.6096 0x0056B630, 11.4186 0x0056A8F0); the server
    /// stays the judge, this only narrows the candidates. Distance is the straight line in global metres
    /// (<see cref="CellFrameExtensions.ToGlobal"/>, the frame <see cref="Within"/> uses), so objects in other
    /// landblocks are compared correctly (not skipped). It allocates nothing: the caller owns the cached filter
    /// delegate and the list.
    /// </summary>
    /// <param name="wcids">The weenie class ids to accept.</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="cellFilter">Takes an object's full cell id (for example 0x01050375); null accepts every cell.</param>
    /// <param name="guid">The nearest creature's id, or 0.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestCreature(IReadOnlyList<uint> wcids, in CellFrame centre, float maxMetres, Func<uint, bool>? cellFilter, out uint guid) =>
        NearestCreature(wcids, in centre, maxMetres, cellFilter, null, out guid);

    /// <summary>
    /// <see cref="NearestCreature(IReadOnlyList{uint}, in CellFrame, float, Func{uint, bool}, out uint)"/> that also skips the ids in <paramref name="excluded"/> (the creatures the caller has
    /// seen killed: ACE sends the kill notice before the Dead motion and keeps the body for a moment, M3 3.1 fix A, review 3.1-R2).
    /// </summary>
    /// <param name="wcids">The weenie class ids to accept.</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="cellFilter">Takes an object's full cell id; null accepts every cell.</param>
    /// <param name="excluded">Ids never returned, or null. Scanned linearly: keep it short.</param>
    /// <param name="guid">The nearest creature's id, or 0.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestCreature(IReadOnlyList<uint> wcids, in CellFrame centre, float maxMetres, Func<uint, bool>? cellFilter, IReadOnlyList<uint>? excluded, out uint guid)
    {
        ArgumentNullException.ThrowIfNull(wcids);
        guid = 0;
        var origin = centre.ToGlobal();
        var best = (double)maxMetres;
        var found = false;
        var count = wcids.Count;
        foreach (var o in _objects.Values)
        {
            if (o.Position is not { } p || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: DeadMotionLowWord } || IsExcluded(excluded, o.Guid))
            {
                continue;
            }

            var wcid = o.Weenie.WeenieClassId;
            var listed = false;
            for (var i = 0; i < count; i++)
            {
                if (wcids[i] == wcid)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed || (cellFilter is not null && !cellFilter(p.CellId)))
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= best)
            {
                best = d;
                guid = o.Guid;
                found = true;
            }
        }

        return found;
    }

    private static bool IsExcluded(IReadOnlyList<uint>? excluded, uint guid)
    {
        if (excluded is null)
        {
            return false;
        }

        for (var i = 0; i < excluded.Count; i++)
        {
            if (excluded[i] == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The object's health fraction (0 to 1) from the last UpdateHealth for it (retail
    /// <c>Handle_Combat__QueryHealthResponse</c>, 11.6096 0x0056B2A0).
    /// </summary>
    /// <param name="guid">The object id.</param>
    /// <param name="fraction">The fraction, or 0.</param>
    /// <returns>False when the object is unknown or no health has been reported for it.</returns>
    public bool HealthOf(uint guid, out float fraction)
    {
        if (_objects.TryGetValue(guid, out var o) && o.Health is { } h)
        {
            fraction = h;
            return true;
        }

        fraction = 0f;
        return false;
    }

    /// <summary>Adds or replaces an object.</summary>
    internal void Set(WorldObject value) => _objects[value.Guid] = value;

    /// <summary>Removes an object; false when it was not there.</summary>
    internal bool Remove(uint guid) => _objects.Remove(guid);

    /// <summary>Removes every object.</summary>
    internal void Clear() => _objects.Clear();
}
