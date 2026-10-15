using System.Collections.Concurrent;

namespace Lifestone.Data.Audio;

/// <summary>
/// The decode cache behind <see cref="DatAudioSource"/>: one load per id, its outcome kept whether it is a record, an
/// absence (null) or an <see cref="InvalidDataException"/>. Thread-safe (two threads asking for a new id at once may both
/// load it; one outcome is kept). A hit allocates nothing, apart from the new exception a cached failure throws.
/// </summary>
/// <typeparam name="T">The decoded record.</typeparam>
internal sealed class AudioRecordCache<T>
    where T : class
{
    private readonly ConcurrentDictionary<uint, Entry> _entries = new();

    /// <summary>
    /// The record <paramref name="id"/>: cached, or loaded with <paramref name="load"/> (which returns null for an absent
    /// record and throws <see cref="InvalidDataException"/> for a malformed one) and cached.
    /// </summary>
    /// <exception cref="InvalidDataException">The record is malformed (now or when it was first loaded).</exception>
    public T? Get<TState>(uint id, Func<uint, TState, T?> load, TState state)
    {
        var entry = _entries.GetOrAdd(id, static (key, args) => Load(key, args.load, args.state), (load, state));
        if (entry.Error is { } error)
        {
            throw new InvalidDataException(error.Message, error);
        }

        return entry.Value;
    }

    private static Entry Load<TState>(uint id, Func<uint, TState, T?> load, TState state)
    {
        try
        {
            return new Entry(load(id, state), null);
        }
        catch (InvalidDataException e)
        {
            return new Entry(null, e);
        }
    }

    /// <summary>A load's outcome: the record (null when absent) or the failure.</summary>
    private readonly record struct Entry(T? Value, InvalidDataException? Error);
}
