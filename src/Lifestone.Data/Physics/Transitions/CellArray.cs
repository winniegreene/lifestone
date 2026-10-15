// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/CellArray.cs (AGPL-3.0)
// with changes: retail's ordered array of (cell id, cell) pairs instead of a dictionary, so iteration follows insertion
// order as retail's does and nothing allocates once grown; a pair may hold a null cell (a cell whose landblock is not
// attached), as retail's add_cell stores one; remove_cell moves the last pair into the hole, as retail does.

using Lifestone.Data.Physics.World;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// The cells a sphere path or an object touches (retail <c>CELLARRAY</c>): ids with their cells, in the order they were
/// found, without duplicate ids.
/// </summary>
/// <remarks>Reused by its transition; grows by 8 like retail's <c>DArray</c> and never shrinks. Not thread-safe.</remarks>
internal sealed class CellArray
{
    private uint[] _ids = new uint[8];
    private PhysCell?[] _cells = new PhysCell?[8];

    /// <summary>How many cells the array holds (retail <c>num_cells</c>).</summary>
    public int Count { get; private set; }

    /// <summary>True once the outdoor cells around the path were added (retail <c>added_outside</c>).</summary>
    public bool AddedOutside;

    /// <summary>
    /// Retail <c>do_not_load_cells</c>: set while a static object's cells are found
    /// (<c>CPhysicsObj::calc_cross_cells_static</c>), which keeps an indoor object to its cell's visible cells.
    /// </summary>
    public bool DoNotLoadCells;

    /// <summary>The id at <paramref name="index"/>.</summary>
    public uint IdAt(int index) => _ids[index];

    /// <summary>The cell at <paramref name="index"/>, or null when its landblock is not attached.</summary>
    public PhysCell? CellAt(int index) => _cells[index];

    /// <summary>Empties the array and clears <see cref="AddedOutside"/> (retail sets <c>num_cells</c> and <c>added_outside</c> to 0).</summary>
    public void Reset()
    {
        Array.Clear(_cells, 0, Count);
        Count = 0;
        AddedOutside = false;
    }

    /// <summary>
    /// Retail <c>CELLARRAY::add_cell</c> (11.4186 0x006B4FF0, 11.6096 0x006B5F30): appends the pair unless the id is
    /// already in the array.
    /// </summary>
    public void Add(uint cellId, PhysCell? cell)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_ids[i] == cellId)
            {
                return;
            }
        }

        if (Count == _ids.Length)
        {
            Array.Resize(ref _ids, Count + 8);
            Array.Resize(ref _cells, Count + 8);
        }

        _ids[Count] = cellId;
        _cells[Count] = cell;
        Count++;
    }

    /// <summary>
    /// Retail <c>CELLARRAY::remove_cell</c> (11.4186 0x006B4E80, 11.6096 0x006B5DC0): the last pair takes the place of the
    /// one at <paramref name="index"/>.
    /// </summary>
    public void RemoveAt(int index)
    {
        if (Count == 0)
        {
            return;
        }

        Count--;
        _ids[index] = _ids[Count];
        _cells[index] = _cells[Count];
        _cells[Count] = null;
    }

    /// <summary>True when <paramref name="cellId"/> is in the array.</summary>
    public bool Contains(uint cellId)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_ids[i] == cellId)
            {
                return true;
            }
        }

        return false;
    }
}
