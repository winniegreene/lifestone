using System.Numerics;
using Lifestone.Data.Models;

namespace Lifestone.Client.Objects;

/// <summary>How one LOD part of an EnvCell static is drawn now: its level and its draw frame (landblock-local AC).</summary>
/// <param name="Level">The LOD level drawn (vertices of the other levels are hidden); a level whose GfxObj is 0 draws nothing.</param>
/// <param name="Origin">The part's frame origin now (its rest origin unless a default animation moves it).</param>
/// <param name="Rotation">The rotation it is drawn with now (camera-facing levels and animations change it).</param>
public readonly record struct CellPartDrawState(int Level, Vector3 Origin, Quaternion Rotation);

/// <summary>
/// Retail LOD for the statics inside some EnvCells of a landblock (docs/objects.md §2.6): which of their parts need per-frame
/// state (a degrade table, or a pose that moves), and that state for the cells the viewer can see. Engine-agnostic.
/// </summary>
/// <remarks>
/// <para><b>Why a state per part.</b> A cell's statics are merged with the cell into one mesh, lit per vertex by the cell's
/// static lights (docs/interiors.md §4.4). A part with a degrade table keeps every level's geometry in that mesh, tagged
/// with its slot and level; the renderer hides the levels not drawn now and turns camera-facing levels (and animated parts)
/// to their draw frame (<see cref="CellPartDrawState"/>). Parts without a table that never move need no state: retail draws
/// their only level at any distance.</para>
/// <para><b>Rules</b> are <see cref="LandblockObjectLod"/>'s for <see cref="WorldObjectKind.CellStatic"/> objects (the object
/// rule at 50 m horizontally, else the part rule; no far-cell rule), evaluated only for the cells drawn this frame, as retail
/// updates viewer distances only from <c>PView::DrawCells</c>.</para>
/// </remarks>
public sealed class CellObjectLod
{
    private readonly int[] _slotOfPart;
    private readonly int[] _partOfSlot;
    private readonly CellPartDrawState[] _state;
    private readonly bool[] _written;
    private readonly Dictionary<uint, int[]> _objectsByCell;
    private readonly List<int> _scratch = [];

    /// <param name="set">The cells' statics (<see cref="LandblockObjectSet.BuildCellObjects"/>).</param>
    /// <param name="posedObjects">Objects whose parts move or take a non-rest pose (default animation or motion table).</param>
    /// <param name="settings">Retail LOD parameters.</param>
    public CellObjectLod(LandblockObjectSet set, IReadOnlySet<int>? posedObjects = null, ObjectLodSettings? settings = null)
    {
        Set = set ?? throw new ArgumentNullException(nameof(set));
        Lod = new LandblockObjectLod(set, settings);
        _slotOfPart = new int[set.Parts.Count];
        var slots = new List<int>();
        for (var p = 0; p < set.Parts.Count; p++)
        {
            var part = set.Parts[p];
            var posed = posedObjects?.Contains(part.ObjectIndex) == true;
            _slotOfPart[p] = NeedsState(part, posed) ? slots.Count : -1;
            if (_slotOfPart[p] >= 0)
            {
                slots.Add(p);
            }
        }

        _partOfSlot = [.. slots];
        _state = new CellPartDrawState[_partOfSlot.Length];
        _written = new bool[_partOfSlot.Length];
        _objectsByCell = set.Objects
            .Select((o, i) => (o.CellId, i))
            .GroupBy(x => x.CellId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.i).ToArray());
    }

    public LandblockObjectSet Set { get; }

    public LandblockObjectLod Lod { get; }

    /// <summary>Parts that need a per-frame state.</summary>
    public int SlotCount => _partOfSlot.Length;

    /// <summary>The cells whose statics this set holds.</summary>
    public IEnumerable<uint> Cells => _objectsByCell.Keys;

    /// <summary>
    /// A part needs a state when it has a degrade table (its level changes with distance, and some levels face the camera)
    /// or when its object is posed by an animation or motion table.
    /// </summary>
    public static bool NeedsState(in ObjectPartPlacement part, bool objectPosed) => objectPosed || part.Chain.Table is not null;

    /// <summary>The part's slot (0-based, local to this set), or −1 when it is drawn as merged, at level 0, always.</summary>
    public int SlotOf(int part) => _slotOfPart[part];

    /// <summary>The part of a slot.</summary>
    public int PartOf(int slot) => _partOfSlot[slot];

    /// <summary>The objects in a cell (indices into <see cref="Set"/>).</summary>
    public ReadOnlySpan<int> ObjectsIn(uint cellId) => _objectsByCell.TryGetValue(cellId, out var objects) ? objects : [];

    /// <summary>The state of a slot after the last evaluation.</summary>
    public CellPartDrawState StateOf(int slot) => _state[slot];

    /// <summary>
    /// Selects the levels and draw frames of the parts in the given cells for a viewer at <paramref name="viewer"/>
    /// (landblock-local AC) and lists the slots whose state changed (or was never written).
    /// </summary>
    public void Evaluate(Vector3 viewer, IEnumerable<uint> cells, List<int> changedSlots)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(changedSlots);
        _scratch.Clear();
        foreach (var cell in cells)
        {
            if (_objectsByCell.TryGetValue(cell, out var objects))
            {
                _scratch.AddRange(objects);
            }
        }

        if (_scratch.Count == 0)
        {
            return;
        }

        Lod.EvaluateObjects(viewer, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_scratch));
        foreach (var o in _scratch)
        {
            var obj = Set.Objects[o];
            for (var p = obj.FirstPart; p < obj.FirstPart + obj.PartCount; p++)
            {
                var slot = _slotOfPart[p];
                if (slot < 0)
                {
                    continue;
                }

                var state = new CellPartDrawState(Lod.LevelOf(p), Lod.PartOrigin(p), Lod.DrawRotation(p));
                if (!_written[slot] || !Same(state, _state[slot]))
                {
                    _state[slot] = state;
                    _written[slot] = true;
                    changedSlots.Add(slot);
                }
            }
        }
    }

    /// <summary>Every cell's parts (registration: the first state of every slot).</summary>
    public void EvaluateAll(Vector3 viewer, List<int> changedSlots) => Evaluate(viewer, _objectsByCell.Keys, changedSlots);

    private static bool Same(in CellPartDrawState a, in CellPartDrawState b) =>
        a.Level == b.Level
        && Vector3.DistanceSquared(a.Origin, b.Origin) < 1e-8f
        && MathF.Abs(Quaternion.Dot(a.Rotation, b.Rotation)) > 1f - 1e-7f;
}
