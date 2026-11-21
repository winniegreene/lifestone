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
    /// M6 16 Eperf: a number that moves whenever the table's objects change: one added, replaced or removed, the table cleared, or an object given a new description by the server (an update of its CreateObject, which
    /// alone can change what kind of object it is). It does not move for a change inside an object (its position, its motion, its health, the inventory fields the reducer edits). A reader that keeps a list drawn from the
    /// table (the threat board's creatures) draws it again when this moved, so the list is never one the table has left behind.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>The server described an object of the table anew (its CreateObject was replaced in place): <see cref="Version"/> moves.</summary>
    internal void Described() => Version++;

    /// <summary>
    /// M6 16 E1f: which awareness range this session's walks keep off a hostile creature (<c>--walk-awareness</c>) and the shadow's ledger of what the other range would have changed
    /// (<see cref="Movement.Wayfinding.WalkAwareness"/>). Not the server's state: the host's setting and a report, kept here because every creature feed (<see cref="Movement.Wayfinding.HostileCreatureFeed"/>) is built over this
    /// table, so one a session reaches every runner without a host passing it. It stays across a reconnect.
    /// </summary>
    public Movement.Wayfinding.WalkAwareness WalkAwareness { get; } = new();

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

    /// <summary>
    /// <see cref="NearestNamed"/> over the living creatures only (M4 1.3 s2; the 4.2 review's R10): the object's ItemType has the creature bit, its last health fraction is not zero or below, and its forward
    /// motion is not Dead (<see cref="DeadMotionLowWord"/>), as <see cref="NearestCreature(in CellFrame, float, uint, IReadOnlyList{string}?, int, int, Func{uint, int?}?, out uint)"/> filters. Unlike that
    /// one it keeps other players: a player can be the attacker the auto-target is asked for. A corpse, a statue or an NPC of the name is skipped. Allocates nothing.
    /// </summary>
    /// <param name="name">The name to match (ordinal).</param>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="exclude">An id to skip (the player), or 0.</param>
    /// <param name="guid">The nearest living creature's id, or 0.</param>
    /// <param name="live">Where a creature stands now (<see cref="Where"/>), or null for the server's last report.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestLivingCreatureNamed(string name, in CellFrame centre, uint exclude, out uint guid, LivePositions? live = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var origin = centre.ToGlobal();
        var best = double.MaxValue;
        guid = 0;
        foreach (var o in _objects.Values)
        {
            if (o.Guid == exclude || o.Position is null || !string.Equals(o.Name, name, StringComparison.Ordinal)
                || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: DeadMotionLowWord } || Where(o, live) is not { } p)
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

    /// <summary>
    /// M6 fight targeting (<c>m6-blow-unpinned</c>): how many living creatures of <paramref name="name"/> with a position the table holds, by the filter of
    /// <see cref="NearestLivingCreatureNamed"/>. One: the name is an identity (the server's notice names its attacker, and only one bears the name). Allocates nothing.
    /// </summary>
    /// <param name="name">The name to match (ordinal).</param>
    /// <param name="exclude">An id to skip (the player), or 0.</param>
    /// <param name="live">Where a creature stands now (<see cref="Where"/>), or null for the server's last report.</param>
    public int LivingCreaturesNamed(string name, uint exclude, LivePositions? live = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var count = 0;
        foreach (var o in _objects.Values)
        {
            if (o.Guid == exclude || o.Position is null || !string.Equals(o.Name, name, StringComparison.Ordinal)
                || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: DeadMotionLowWord } || Where(o, live) is null)
            {
                continue;
            }

            count++;
        }

        return count;
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

    /// <summary>The ObjectDescriptionFlags bit of an object that may be attacked (<c>BF_ATTACKABLE</c>, 0x10; ACE <c>ObjectDescriptionFlag.Attackable</c>).</summary>
    public const uint AttackableFlag = 0x10;

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
    /// <param name="live">Where a creature stands now (<see cref="Where"/>; its distance and the cell <paramref name="cellFilter"/> sees), or null for the server's last report.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestCreature(IReadOnlyList<uint> wcids, in CellFrame centre, float maxMetres, Func<uint, bool>? cellFilter, IReadOnlyList<uint>? excluded, out uint guid, LivePositions? live = null)
    {
        ArgumentNullException.ThrowIfNull(wcids);
        guid = 0;
        var origin = centre.ToGlobal();
        var best = (double)maxMetres;
        var found = false;
        var count = wcids.Count;
        foreach (var o in _objects.Values)
        {
            if (o.Position is null || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: DeadMotionLowWord } || IsExcluded(excluded, o.Guid))
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

            if (!listed || Where(o, live) is not { } p || (cellFilter is not null && !cellFilter(p.CellId)))
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

    /// <summary>The ObjectDescriptionFlags bit of a player (<c>BF_PLAYER</c>, 0x8; ACE <c>ObjectDescriptionFlag.Player</c>), as <c>WorldContentCensus</c> reads it.</summary>
    public const uint PlayerFlag = 0x8;

    /// <summary>
    /// Finds the creature nearest to <paramref name="centre"/>, as <see cref="NearestNamed"/> does but over what a hunt may attack (M4 1.2 s1; the 4.2 review's R10): the object's ItemType has the creature
    /// bit, it is not the player or another player (<see cref="PlayerFlag"/>), it has a position, it is not known to be dead (its last health fraction is above zero, or it has none yet; its forward motion is not
    /// Dead, <see cref="DeadMotionLowWord"/>), it is within <paramref name="maxMetres"/> (straight line in global metres), and, when asked, its name is one of <paramref name="names"/> (ordinal) and its level is
    /// within <paramref name="minLevel"/> to <paramref name="maxLevel"/>. Non-creatures (a corpse, a lifestone, an NPC without the creature bit) and the dead are skipped. Retail's rule for what may be
    /// attacked is <c>ClientCombatSystem::ObjectIsAttackable</c> (11.6096 0x0056B340); the server stays the judge. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// A creature's level is not in its CreateObject (only an appraisal carries it): <paramref name="levelOf"/> supplies it (null when unknown). When a band is asked (<paramref name="minLevel"/> above 0 or
    /// <paramref name="maxLevel"/> below <see cref="int.MaxValue"/>) a creature whose level is unknown is <b>skipped</b>, never guessed in: <c>TODO(m4-creature-level)</c> until the appraisal is decoded.
    /// </remarks>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="exclude">An id to skip (the player), or 0.</param>
    /// <param name="names">The names to accept, or null for any name. Scanned linearly: keep it short.</param>
    /// <param name="minLevel">The lowest level accepted (0 for no lower bound).</param>
    /// <param name="maxLevel">The highest level accepted (<see cref="int.MaxValue"/> for no upper bound).</param>
    /// <param name="levelOf">The level of a creature by id, or null when no band is asked.</param>
    /// <param name="guid">The nearest creature's id, or 0.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestCreature(in CellFrame centre, float maxMetres, uint exclude, IReadOnlyList<string>? names, int minLevel, int maxLevel, Func<uint, int?>? levelOf, out uint guid) =>
        NearestCreature(in centre, maxMetres, exclude, names, minLevel, maxLevel, levelOf, 0, out guid);

    /// <summary>
    /// M6 7 AO (live, stretch 4: "0x7A9B301B is within 24 m: she rests standing", the Scrivener of Item Magic, a town NPC): the nearest living creature that may be attacked (<see cref="AttackableFlag"/>): what can come at her. An NPC is a creature with the bit clear (ACE <c>PropertyBool.Attackable</c> false; world row: weenie 20214, bool 19 = 0). Allocates nothing.
    /// </summary>
    /// <param name="centre">Where to measure from.</param>
    /// <param name="maxMetres">The largest distance accepted.</param>
    /// <param name="exclude">An id to skip (the player), or 0.</param>
    /// <param name="guid">The nearest such creature's id, or 0.</param>
    /// <param name="live">Where a creature stands now (<see cref="Where"/>), or null for the server's last report.</param>
    /// <returns>True when one was found.</returns>
    public bool NearestAttackableCreature(in CellFrame centre, float maxMetres, uint exclude, out uint guid, LivePositions? live = null) =>
        NearestCreature(in centre, maxMetres, exclude, null, 0, int.MaxValue, null, AttackableFlag, out guid, live);

    /// <summary>
    /// M6 item 14 (<c>m6-live-position</c>): where a creature query takes <paramref name="o"/> to stand: by <paramref name="live"/> when the caller passes the session's (<see cref="IGameSessionView.Live"/>: the body the
    /// screen draws when it can be trusted, else the server's report), else the server's last report as before. A caller that passes none keeps its meaning.
    /// </summary>
    private static CellFrame? Where(WorldObject o, LivePositions? live) => live is null ? o.Position : live.At(o);

    private bool NearestCreature(in CellFrame centre, float maxMetres, uint exclude, IReadOnlyList<string>? names, int minLevel, int maxLevel, Func<uint, int?>? levelOf, uint needFlags, out uint guid, LivePositions? live = null)
    {
        guid = 0;
        var origin = centre.ToGlobal();
        var best = (double)maxMetres;
        var found = false;
        var banded = minLevel > 0 || maxLevel < int.MaxValue;
        foreach (var o in _objects.Values)
        {
            if (o.Guid == exclude || o.IsSelf || o.Position is null || (o.ItemType & CreatureItemType) == 0 || (o.DescriptionFlags & PlayerFlag) != 0 || (o.DescriptionFlags & needFlags) != needFlags
                || o.Health is <= 0f || o.Motion is { Forward: DeadMotionLowWord } || !NameListed(names, o.Name) || Where(o, live) is not { } p)
            {
                continue;
            }

            if (banded && (levelOf?.Invoke(o.Guid) is not { } level || level < minLevel || level > maxLevel))
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

    private static bool NameListed(IReadOnlyList<string>? names, string name)
    {
        if (names is null)
        {
            return true;
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
    internal void Set(WorldObject value)
    {
        _objects[value.Guid] = value;
        Version++;
    }

    /// <summary>Removes an object; false when it was not there.</summary>
    internal bool Remove(uint guid)
    {
        if (!_objects.Remove(guid))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>Removes every object.</summary>
    internal void Clear()
    {
        _objects.Clear();
        Version++;
    }
}
