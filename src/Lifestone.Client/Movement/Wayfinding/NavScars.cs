using System.Numerics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// A run's keep-out spots (P5a, design 3.2, docs/wayfinding.md): where the body stuck or a physics probe failed, a disc
/// the planner treats as blocked until it expires (<see cref="DefaultLife"/>). Per run, never written into the shared
/// <see cref="NavTile"/>. The planner lets a route leave a scar the start stands in (each step moving outward) but never
/// enter one. The clock is injectable (<see cref="TimeProvider"/>). Not thread-safe: one per run. Not retail: Lifestone's own.
/// </summary>
public sealed class NavScars
{
    /// <summary>A scar's default life (design 3.1: 30 s). Not retail: Lifestone's own.</summary>
    public static readonly TimeSpan DefaultLife = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock;
    private readonly long _origin;
    private readonly List<NavScar> _scars = [];

    /// <summary>A scar set on <paramref name="clock"/> (the system clock by default).</summary>
    public NavScars(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _origin = _clock.GetTimestamp();
    }

    /// <summary>The scar set's clock: time since it was created.</summary>
    public TimeSpan Now => _clock.GetElapsedTime(_origin);

    /// <summary>The scars added and not yet removed by <see cref="Expire()"/> (some may have outlived their life).</summary>
    public IReadOnlyList<NavScar> All => _scars;

    /// <summary>The number of live scars (not past their life now).</summary>
    public int LiveCount
    {
        get
        {
            var now = Now;
            int n = 0;
            foreach (var s in _scars)
            {
                if (s.Until > now)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Scars a disc of <paramref name="radiusMetres"/> round <paramref name="centre"/> for <see cref="DefaultLife"/>.</summary>
    public void Add(NavPoint centre, float radiusMetres) => Add(centre, radiusMetres, DefaultLife);

    /// <summary>Scars a disc of <paramref name="radiusMetres"/> round <paramref name="centre"/> for <paramref name="life"/>.</summary>
    public void Add(NavPoint centre, float radiusMetres, TimeSpan life)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusMetres);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(life, TimeSpan.Zero);
        _scars.Add(new NavScar(centre.Global, radiusMetres, Now + life));
    }

    /// <summary>Whether a live scar covers <paramref name="point"/>.</summary>
    public bool IsScarred(NavPoint point) => IsScarred(point.Global, Now);

    /// <summary>Removes the scars past their life now; returns how many went.</summary>
    public int Expire() => Expire(Now);

    /// <summary>Removes the scars past their life at <paramref name="now"/> (this set's clock); returns how many went.</summary>
    public int Expire(TimeSpan now) => _scars.RemoveAll(s => s.Until <= now);

    /// <summary>Removes every scar (a new run).</summary>
    public void Clear() => _scars.Clear();

    internal bool IsScarred(Vector2 global, TimeSpan now)
    {
        foreach (var s in _scars)
        {
            if (s.Until > now && Vector2.DistanceSquared(global, s.Centre) <= s.Radius * s.Radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Copies the live scars into <paramref name="buffer"/> (grown if needed, so a warm planner allocates nothing); returns the count.</summary>
    internal int CopyLive(ref NavScar[] buffer)
    {
        var now = Now;
        int n = 0;
        foreach (var s in _scars)
        {
            if (s.Until <= now)
            {
                continue;
            }

            if (n == buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(8, buffer.Length * 2));
            }

            buffer[n++] = s;
        }

        return n;
    }
}

/// <summary>One scar: a disc in global metres (<see cref="NavPoint.Global"/>) live until <see cref="Until"/> on its set's clock.</summary>
/// <param name="Centre">The disc's centre, global metres.</param>
/// <param name="Radius">The disc's radius in metres.</param>
/// <param name="Until">When it expires, on the set's clock (<see cref="NavScars.Now"/>).</param>
public readonly record struct NavScar(Vector2 Centre, float Radius, TimeSpan Until);
