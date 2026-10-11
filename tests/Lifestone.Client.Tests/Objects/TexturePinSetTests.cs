using Lifestone.Client.Caching;
using Lifestone.Client.Objects;
using Lifestone.Client.Rendering;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// The attach-time texture pins: a texture a prepared landblock found resident must stay resident through the attach,
/// and one evicted since the prepare must be detected before the attach (so it is decoded on a worker, not in an
/// attach step).
/// </summary>
public sealed class TexturePinSetTests
{
    private static TextureKey Key(int i) => new((uint)(0x06000000 + i), TextureKey.DefaultPalette, false, false);

    [Fact]
    public void Pinned_textures_survive_eviction_and_evicted_ones_are_reported_missing()
    {
        var evicted = new List<TextureKey>();
        var cache = new ResourceCache<TextureKey, object>("textures", budgetBytes: 100, (key, _) => evicted.Add(key));

        // A prepare saw textures 1 and 2 resident (idle, held by nobody) and skipped decoding them.
        foreach (var i in new[] { 1, 2 })
        {
            cache.Add(Key(i), new object(), 40);
            cache.Release(Key(i));
        }

        // Before the attach another landblock uploads texture 3: over budget, the least recent idle texture (1) goes.
        cache.Add(Key(3), new object(), 40);
        Assert.Equal(new[] { Key(1) }, evicted);

        // The attach is decided: texture 2 is pinned, texture 1 is missing (decoded again on a worker).
        var pins = new TexturePinSet();
        bool TryAcquire(TextureKey key) => cache.TryAcquire(key, out _);
        Assert.False(pins.TryPin(Key(1), TryAcquire));
        Assert.True(pins.TryPin(Key(2), TryAcquire));
        Assert.True(pins.TryPin(Key(2), TryAcquire)); // pinned once
        Assert.Equal(1, pins.Count);

        // While the attach runs, more uploads cannot evict the pinned texture.
        cache.Add(Key(4), new object(), 40);
        cache.Release(Key(3));
        cache.Release(Key(4));
        Assert.True(cache.Contains(Key(2)));
        Assert.DoesNotContain(Key(2), evicted);

        // The worker's decode of texture 1 is uploaded by an attach unit, which takes over its reference.
        cache.Add(Key(1), new object(), 40);
        pins.Adopt(Key(1), key => cache.Release(key));
        Assert.True(pins.Contains(Key(1)));

        // A duplicate reference is handed back, so each pin is exactly one reference.
        Assert.True(cache.TryAcquire(Key(2), out _));
        pins.Adopt(Key(2), key => cache.Release(key));
        Assert.Equal(2, pins.Count);

        // Once the meshes hold their textures the pins go; nobody holds them now, so the budget applies again.
        pins.ReleaseAll(key => cache.Release(key));
        Assert.Equal(0, pins.Count);
        Assert.Equal(cache.Count, cache.IdleCount);
        Assert.True(cache.TotalBytes <= cache.BudgetBytes);
        pins.ReleaseAll(key => throw new InvalidOperationException("released twice"));
    }
}
