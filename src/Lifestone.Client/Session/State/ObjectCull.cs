using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Streaming;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// Retail's client-side cull (M5 item 0 c1, D16; <c>m5-object-cull</c>): an object the server has stopped sending leaves the
/// object table <see cref="CullSeconds"/> after it left what the client treats as visible, through the same path as the
/// server's F747 (<see cref="ObjectDeleted"/>), so the avatar set, the light table, the fight's tables and the wayfinder's
/// creature feed follow. Engine-free; the reducer owns one and the session ticks it (<c>GameSession.Tick</c>, both hosts).
/// </summary>
/// <remarks>
/// <para><b>Retail's rule</b> (<c>CObjectMaint</c>, PORTAL/cobject/CObjMaint.obj; 11.6096 addresses, re-expressed from the
/// 11.4186 decompile, nothing copied). <c>CObjectMaint::AddObjectToBeDestroyed</c> (0x00509A40) stamps an object for
/// destruction at the current time plus 25 seconds, replacing any earlier stamp, and queues it.
/// <c>CObjectMaint::RemoveObjectToBeDestroyed</c> (0x00508F10) forgets the stamp. <c>CObjectMaint::UseTime</c> (0x00509480)
/// pops every due entry each frame and deletes the object only when its stamp is still the one queued (a re-stamp
/// supersedes). An object is queued when the cell it stands in is released from the client's loaded cells
/// (<c>CObjectMaint::ReleaseObjCell</c>, 0x005086E0: every object in the cell that is not static and has no parent leaves
/// visibility, and its children are queued with it), when it is created or moved into a cell the client has not loaded
/// (the lost cell, <c>CObjectMaint::GotoLostCell</c> 0x00508CE0), or when it moves into a container that is neither the
/// player's, the open ground container nor the trade partner's (with its contents). The queue is cancelled when the object
/// enters the world again (a create or a position into a loaded cell, with its children) or leaves it (a delete).
/// <c>CObjectMaint::DestroyObjects</c> (0x00509700) is the log-off clear. ACE relies on the same 25 s
/// (<c>Physics/Common/ObjectMaint.cs:21,41</c> @95ef426d, its port of the queue at :550-640) and sends no delete.</para>
/// <para><b>The mapping.</b> The Lifestone reducer knows the player's cell as the server last placed it (ACE echoes the
/// player's own position to the player), not the renderer's loaded cells, so the cull uses ACE's own visible set
/// (<c>Physics/Common/ObjectMaint.cs:337-385</c> @95ef426d) and never drops an object ACE still keeps up to date. Outdoors
/// and in a building cell (an EnvCell seen from outside) an object is visible while its cell's landblock is the player's or
/// one of the eight around it (ACE: the landblock and its neighbours; a superset of ACE's set for a building). In a dungeon
/// cell (an EnvCell not seen from outside) it is visible while its cell is the player's cell or in that cell's
/// visible-cell list from the dat (ACE's <c>envCell.VisibleCells</c>, M5 finding 0-R1), so the set moves with the player
/// from cell to cell. The hosts supply the lists through <see cref="Cells"/> (the game: the interior registry the streamer
/// loads; the headless host: <c>DataOnlyAttachments</c>, which decodes the player's cell and its visible cells as the
/// player moves); while a list is not known yet the landblock rule holds for that tick. A host that supplies no lists keeps
/// the landblock rule inside every EnvCell, and the reducer logs <c>TODO(m5-object-cull-cells)</c> once.
/// The visible set is re-read once a second (retail refreshes its visible list on the same period,
/// <c>CObjectMaint::UpdateVisibleObjectList</c> 0x00508E60); due entries are culled on the first tick at or after their
/// time. Any message the reducer applies for a pending object cancels its entry (<see cref="Seen"/>), and the next scan
/// queues it again with a fresh 25 s if it is still outside, as retail re-stamps an object moved into a lost cell.</para>
/// <para><b>Never culled.</b> The player; an object with no world position (contained or wielded: it follows its holder,
/// culled with it); a static object (retail's <c>ReleaseObjCell</c> skips them); an object the player holds (pack, side
/// pack or equipped) or wields; the ground container the player has open and its contents.</para>
/// <para>Allocation-free per tick in the steady state (no growth past the initial capacities, no culls).</para>
/// </remarks>
public sealed class ObjectCull
{
    /// <summary>How long an object stays after it left the visible set, in session-clock seconds (retail's <c>AddObjectToBeDestroyed</c>; ACE's <c>DestructionTime</c>).</summary>
    public const double CullSeconds = 25.0;

    /// <summary>How often the visible set is re-read, in session-clock seconds (<c>UpdateVisibleObjectList</c>'s period).</summary>
    public const double ScanSeconds = 1.0;

    private readonly Dictionary<uint, double> _due = new(1024);
    private readonly List<uint> _expired = new(256);
    private readonly HashSet<uint> _pvs = new(256);
    private double _nextScan = double.NegativeInfinity;
    private double _earliestDue = double.PositiveInfinity;
    private uint _viewCell;
    private bool _dungeon;

    /// <summary>
    /// Where the cull reads a dungeon cell's visible cells and its seen-outside flag (the host's world attachments: cheap
    /// reads of data it already loaded, never a load on the frame path), or null (the landblock rule inside every EnvCell).
    /// </summary>
    public IWorldAttachments? Cells { get; set; }

    /// <summary>True once the player stood in an EnvCell while no <see cref="Cells"/> was supplied (the reducer logs the TODO once).</summary>
    public bool CellsMissingSeen { get; private set; }

    /// <summary>True while the visible set is the player's dungeon cell and its visible cells (read at the last scan).</summary>
    public bool DungeonView => _dungeon;

    /// <summary>Objects waiting out their cull time now.</summary>
    public int Pending => _due.Count;

    /// <summary>Objects culled since the session started (followers included).</summary>
    public long Culled { get; private set; }

    /// <summary>Cull batches since the session started (one log line each).</summary>
    public long Batches { get; private set; }

    /// <summary>True while <paramref name="guid"/> waits out its cull time.</summary>
    public bool IsPending(uint guid) => _due.ContainsKey(guid);

    /// <summary>The server sent something for <paramref name="guid"/>: its pending cull is cancelled (<c>RemoveObjectToBeDestroyed</c>).</summary>
    internal void Seen(uint guid)
    {
        if (_due.Count != 0)
        {
            _due.Remove(guid);
        }
    }

    /// <summary>Forgets everything (the log off: <c>DestroyObjects</c>).</summary>
    internal void Clear()
    {
        _due.Clear();
        _dungeon = false;
        _viewCell = 0;
        _nextScan = double.NegativeInfinity;
        _earliestDue = double.PositiveInfinity;
    }

    /// <summary>
    /// One tick: re-reads the visible set when its period is up, then appends to <paramref name="into"/> every due object
    /// still outside it, followed by what it holds (contents, wielded and parented objects). Returns how many it appended.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="now">The session clock, in seconds.</param>
    /// <param name="into">Receives the ids to remove, holders before what they hold; not cleared.</param>
    internal int Collect(GameState state, double now, List<uint> into)
    {
        if (now < _nextScan && now < _earliestDue)
        {
            return 0;
        }

        var playerCell = PlayerCell(state);
        RefreshView(playerCell);
        if (now >= _nextScan)
        {
            _nextScan = now + ScanSeconds;
            Scan(state, playerCell, now);
        }

        if (now < _earliestDue)
        {
            return 0;
        }

        _expired.Clear();
        var earliest = double.PositiveInfinity;
        foreach (var (guid, due) in _due)
        {
            if (due <= now)
            {
                _expired.Add(guid);
            }
            else if (due < earliest)
            {
                earliest = due;
            }
        }

        _earliestDue = earliest;
        var start = into.Count;
        foreach (var guid in _expired)
        {
            _due.Remove(guid);
            if (state.Objects.TryGet(guid, out var o) && Placed(o, state) && (playerCell == 0 || !IsVisible(o.Position!.Value.CellId, playerCell)) && !Exempt(o, state))
            {
                into.Add(guid);
            }
        }

        if (into.Count == start)
        {
            return 0;
        }

        AddFollowers(state, into, start);
        var added = into.Count - start;
        Culled += added;
        Batches++;
        return added;
    }

    /// <summary>True when the landblock of <paramref name="cell"/> is the landblock of <paramref name="playerCell"/> or one of its eight neighbours.</summary>
    public static bool Visible(uint cell, uint playerCell)
    {
        var dx = (int)(cell >> 24) - (int)(playerCell >> 24);
        var dy = (int)((cell >> 16) & 0xFF) - (int)((playerCell >> 16) & 0xFF);
        return dx is >= -1 and <= 1 && dy is >= -1 and <= 1;
    }

    /// <summary>The visible-set rule for this tick: the dungeon cell's own list when the player stands in a dungeon cell whose list is known, else the landblock rule.</summary>
    private bool IsVisible(uint cell, uint playerCell) => _dungeon ? _pvs.Contains(cell) : Visible(cell, playerCell);

    /// <summary>Reads the player's cell's visible set: a dungeon cell's list into <c>_pvs</c> (rebuilt only when the cell or the list's availability changes).</summary>
    private void RefreshView(uint playerCell)
    {
        if (playerCell == 0 || !LandDefs.IsEnvCell(playerCell))
        {
            _dungeon = false;
            _viewCell = playerCell;
            return;
        }

        if (Cells is not { } cells)
        {
            CellsMissingSeen = true;
            _dungeon = false;
            _viewCell = playerCell;
            return;
        }

        if (_dungeon && _viewCell == playerCell)
        {
            return;
        }

        _viewCell = playerCell;
        _dungeon = false;
        var visible = cells.VisibleCells(playerCell);
        if (visible is null || visible.Count == 0 || cells.SeenOutside(playerCell))
        {
            return; // not known yet (or a failed read: an empty list), or a building cell: the landblock rule
        }

        _pvs.Clear();
        _pvs.Add(playerCell);
        for (var i = 0; i < visible.Count; i++)
        {
            _pvs.Add(visible[i]);
        }

        _dungeon = true;
    }

    private void Scan(GameState state, uint playerCell, double now)
    {
        if (playerCell == 0)
        {
            return; // no player cell (before the player, between logins): nothing new is queued; the entries already queued keep their time
        }

        foreach (var o in state.Objects.All)
        {
            if (!Placed(o, state) || IsVisible(o.Position!.Value.CellId, playerCell) || Exempt(o, state))
            {
                Seen(o.Guid);
                continue;
            }

            if (!_due.ContainsKey(o.Guid))
            {
                var due = now + CullSeconds;
                _due[o.Guid] = due;
                if (due < _earliestDue)
                {
                    _earliestDue = due;
                }
            }
        }
    }

    /// <summary>An object the cull may queue on its own: in the world by a position of its own, not the player, not static.</summary>
    private static bool Placed(WorldObject o, GameState state) =>
        o.Guid != state.Self.Guid && o.Parent is null && o.Position is { CellId: not 0 } && (o.State & PhysicsState.Static) == 0;

    /// <summary>The player's own holdings and the open ground container with its contents.</summary>
    private static bool Exempt(WorldObject o, GameState state)
    {
        var self = state.Self.Guid;
        var viewed = state.Containers.ViewedGuid;
        var guid = o.Guid;
        if (guid == self || (viewed != 0 && guid == viewed))
        {
            return true;
        }

        if (o.ContainerId is { } container && (container == self || (viewed != 0 && container == viewed)))
        {
            return true;
        }

        if (o.WielderId is { } wielder && wielder == self)
        {
            return true;
        }

        if (o.Parent is { } parent && parent.ParentGuid == self)
        {
            return true;
        }

        return state.Self.Inventory.Contains(guid);
    }

    /// <summary>Appends what the culled objects from <paramref name="start"/> on hold (contents, wielded and parented objects), at any depth, except what the player holds.</summary>
    private static void AddFollowers(GameState state, List<uint> into, int start)
    {
        for (var i = start; i < into.Count; i++)
        {
            var holder = into[i];
            foreach (var o in state.Objects.All)
            {
                var held = o.ContainerId == holder || o.WielderId == holder || (o.Parent is { } parent && parent.ParentGuid == holder);
                if (held && o.Guid != holder && !Exempt(o, state) && !Contains(into, start, o.Guid))
                {
                    into.Add(o.Guid);
                }
            }
        }
    }

    private static bool Contains(List<uint> list, int start, uint guid)
    {
        for (var i = start; i < list.Count; i++)
        {
            if (list[i] == guid)
            {
                return true;
            }
        }

        return false;
    }

    private static uint PlayerCell(GameState state)
    {
        if (state.Self.Guid == 0)
        {
            return 0;
        }

        if (state.Self.Position is { } p && p.CellId != 0)
        {
            return p.CellId;
        }

        return state.Objects.TryGet(state.Self.Guid, out var self) && self.Position is { } q ? q.CellId : 0;
    }
}
