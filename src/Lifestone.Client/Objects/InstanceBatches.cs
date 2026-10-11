namespace Lifestone.Client.Objects;

/// <summary>
/// Which instances (object parts) each batch (one mesh, drawn instanced) holds right now, with O(1) moves and a dirty
/// set, so a renderer only rewrites the instance buffers that changed. Single-threaded.
/// </summary>
/// <remarks>
/// Each part is in at most one batch: the mesh of its current LOD level, or none when that level draws nothing.
/// Members are kept densely (swap-remove), in no particular order.
/// </remarks>
public sealed class InstanceBatches
{
    private readonly List<int>[] _members;
    private readonly int[] _batchOf;
    private readonly int[] _slotOf;
    private readonly bool[] _dirty;
    private readonly List<int> _dirtyList = [];

    /// <summary>Creates empty batches.</summary>
    /// <param name="batchCount">Number of batches (meshes).</param>
    /// <param name="instanceCount">Number of instances (parts).</param>
    public InstanceBatches(int batchCount, int instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceCount);
        _members = new List<int>[batchCount];
        for (var i = 0; i < batchCount; i++)
        {
            _members[i] = [];
        }

        _batchOf = new int[instanceCount];
        _slotOf = new int[instanceCount];
        Array.Fill(_batchOf, -1);
        _dirty = new bool[batchCount];
    }

    public int BatchCount => _members.Length;

    public int InstanceCount => _batchOf.Length;

    /// <summary>Batches changed since the dirty set was last taken.</summary>
    public int DirtyCount => _dirtyList.Count;

    /// <summary>The batch an instance is in, or -1.</summary>
    public int BatchOf(int instance) => _batchOf[instance];

    /// <summary>The instances of a batch (live view; do not modify).</summary>
    public IReadOnlyList<int> Members(int batch) => _members[batch];

    /// <summary>Moves an instance into <paramref name="batch"/> (-1 = none). Marks both batches dirty when it moved.</summary>
    public void Move(int instance, int batch)
    {
        var from = _batchOf[instance];
        if (from == batch)
        {
            return;
        }

        if (from >= 0)
        {
            var list = _members[from];
            var slot = _slotOf[instance];
            var last = list[^1];
            list[slot] = last;
            _slotOf[last] = slot;
            list.RemoveAt(list.Count - 1);
            MarkDirty(from);
        }

        _batchOf[instance] = batch;
        if (batch >= 0)
        {
            _slotOf[instance] = _members[batch].Count;
            _members[batch].Add(instance);
            MarkDirty(batch);
        }
    }

    /// <summary>Marks a batch for rewriting (e.g. a camera-facing member turned).</summary>
    public void MarkDirty(int batch)
    {
        if (!_dirty[batch])
        {
            _dirty[batch] = true;
            _dirtyList.Add(batch);
        }
    }

    /// <summary>Moves the dirty batches into <paramref name="into"/> (cleared first) and clears them; no allocation.</summary>
    public void TakeDirty(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        into.AddRange(_dirtyList);
        foreach (var batch in _dirtyList)
        {
            _dirty[batch] = false;
        }

        _dirtyList.Clear();
    }

    /// <summary>Returns and clears the dirty batches.</summary>
    public int[] TakeDirty()
    {
        var result = _dirtyList.ToArray();
        foreach (var batch in result)
        {
            _dirty[batch] = false;
        }

        _dirtyList.Clear();
        return result;
    }
}
