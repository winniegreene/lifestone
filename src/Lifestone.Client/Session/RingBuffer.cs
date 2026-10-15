namespace Lifestone.Client.Session;

/// <summary>
/// A fixed-capacity ring of values, oldest first, that drops the oldest when full: the chat log's and the action log's
/// storage. Adding allocates nothing. Not thread-safe (game thread only).
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
internal sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _start;

    /// <summary>An empty ring of <paramref name="capacity"/> values.</summary>
    internal RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    /// <summary>The most values kept.</summary>
    internal int Capacity => _items.Length;

    /// <summary>The number of values kept.</summary>
    internal int Count { get; private set; }

    /// <summary>Every value ever added, including those dropped.</summary>
    internal long Total { get; private set; }

    /// <summary>The value at <paramref name="index"/>, 0 being the oldest kept.</summary>
    internal T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _items[(_start + index) % _items.Length];
        }
    }

    /// <summary>Adds a value, dropping the oldest when the ring is full.</summary>
    internal void Add(in T value)
    {
        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = value;
            Count++;
        }
        else
        {
            _items[_start] = value;
            _start = (_start + 1) % _items.Length;
        }

        Total++;
    }

    /// <summary>Removes every value (<see cref="Total"/> is kept).</summary>
    internal void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        Count = 0;
    }
}
