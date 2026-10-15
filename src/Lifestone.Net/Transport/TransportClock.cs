// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/TransportClock.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Transport;
// the timestamp source is a System.TimeProvider (Lifestone.Net reads no Stopwatch) instead of a Func<long> and a
// frequency defaulting to Stopwatch's.

namespace Lifestone.Net.Transport;

internal sealed class TransportClock
{
    private readonly TimeProvider _time;
    private readonly long _ticksPerInterval;
    private long _intervalBaseTimestamp;

    /// <summary>Timestamp ticks per second of the injected source.</summary>
    public long Frequency { get; }

    public ushort IntervalId { get; private set; }

    public TransportClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        Frequency = time.TimestampFrequency;
        if (Frequency < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                "the interval clock needs at least 2 ticks per second");
        }

        _ticksPerInterval = Frequency / 2;
        _intervalBaseTimestamp = _time.GetTimestamp();
        IntervalId = 1;
    }

    public long GetTimestamp() => _time.GetTimestamp();

    public void Update()
    {
        long elapsed = _time.GetTimestamp() - _intervalBaseTimestamp;
        if (elapsed < _ticksPerInterval)
            return;

        long steps = elapsed / _ticksPerInterval;
        IntervalId = unchecked((ushort)(IntervalId + steps));
        _intervalBaseTimestamp += steps * _ticksPerInterval;
    }
}
