using Lifestone.Client.Rendering;

namespace Lifestone.Client.Objects;

/// <summary>
/// The textures one landblock attach holds a reference to ("pins") from the moment it is decided until its meshes hold
/// them through their materials, so the texture cache cannot evict them in between. Main thread only; engine-agnostic
/// (the cache's acquire and release are passed in).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A worker skips decoding a texture that is resident when it prepares a landblock
/// (<c>RenderResources.PrepareSurface</c>). Prepared landblocks wait in the ring around the 3×3 for an unbounded time, so
/// such a texture can be evicted before the attach; the material path would then decode it inside an attach step on
/// the main thread (tens of milliseconds). With pins, every texture the attach needs is either pinned resident, or an
/// image in hand, or decoded again on a worker before the attach starts (docs/objects.md §2.2).</para>
/// <para>Each key is pinned at most once, with exactly one cache reference; <see cref="ReleaseAll"/> drops them.</para>
/// </remarks>
public sealed class TexturePinSet
{
    private readonly HashSet<TextureKey> _pinned = [];

    /// <summary>Textures pinned now.</summary>
    public int Count => _pinned.Count;

    /// <summary>True when <paramref name="key"/> is pinned.</summary>
    public bool Contains(TextureKey key) => _pinned.Contains(key);

    /// <summary>
    /// Pins <paramref name="key"/> if it is resident: <paramref name="tryAcquire"/> takes one cache reference and returns
    /// false when the texture is not cached. Returns true when the key is pinned (now or already).
    /// </summary>
    public bool TryPin(TextureKey key, Func<TextureKey, bool> tryAcquire)
    {
        ArgumentNullException.ThrowIfNull(tryAcquire);
        if (_pinned.Contains(key))
        {
            return true;
        }

        if (!tryAcquire(key))
        {
            return false;
        }

        _pinned.Add(key);
        return true;
    }

    /// <summary>
    /// Takes over a reference the caller already holds (e.g. from an upload). A key that is already pinned keeps its one
    /// pin and the extra reference is given back through <paramref name="release"/>.
    /// </summary>
    public void Adopt(TextureKey key, Action<TextureKey> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!_pinned.Add(key))
        {
            release(key);
        }
    }

    /// <summary>Drops every pin (idempotent).</summary>
    public void ReleaseAll(Action<TextureKey> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        foreach (var key in _pinned)
        {
            release(key);
        }

        _pinned.Clear();
    }
}
