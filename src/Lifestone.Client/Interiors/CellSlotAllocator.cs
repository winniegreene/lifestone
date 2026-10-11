namespace Lifestone.Client.Interiors;

/// <summary>
/// Slots of the cell-state table (docs/interiors.md §3.4): every attached cell mesh carries its slot in its vertex
/// data and its shaders read their <see cref="CellDrawState"/> from that texel. Slot 0 is reserved for "no state" (a
/// mesh without the attribute reads 0). Main thread only.
/// </summary>
/// <remarks>
/// This replaces Godot instance uniforms, which reserve a block of the global shader-uniform buffer for every instance
/// whose material declares one, hidden or not (about 4,000 instances with the default buffer size), shared with every
/// other track. A slot costs one texel.
/// </remarks>
public sealed class CellSlotAllocator
{
    private readonly Stack<int> _free = new();
    private int _next = 1;

    /// <param name="capacity">Slots in the table, including the reserved slot 0.</param>
    public CellSlotAllocator(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>Slots handed out and not released.</summary>
    public int InUse { get; private set; }

    /// <summary>Most slots in use at once.</summary>
    public int Peak { get; private set; }

    /// <summary>Allocations that found the table full (they got slot 0).</summary>
    public long Exhausted { get; private set; }

    /// <summary>A free slot (1 to <see cref="Capacity"/> − 1), or 0 when the table is full.</summary>
    public int Allocate()
    {
        int slot;
        if (_free.Count > 0)
        {
            slot = _free.Pop();
        }
        else if (_next < Capacity)
        {
            slot = _next++;
        }
        else
        {
            Exhausted++;
            return 0;
        }

        InUse++;
        Peak = Math.Max(Peak, InUse);
        return slot;
    }

    /// <summary>Returns a slot (0 is ignored).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The slot was never handed out by this allocator.</exception>
    public void Release(int slot)
    {
        if (slot == 0)
        {
            return;
        }

        if (slot < 0 || slot >= _next)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a slot of this table.");
        }

        _free.Push(slot);
        InUse--;
    }
}
