using Lifestone.Client.Objects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Objects;

/// <summary>A texture an attach needs that is neither resident nor an image in hand: it is decoded again on a worker.</summary>
/// <param name="Key">The texture the surface's material names.</param>
/// <param name="Material">The Surface (textured).</param>
/// <param name="Palette">The surface request's palette (null: the texture's default).</param>
public readonly record struct MissingTexture(TextureKey Key, SurfaceMaterial Material, PaletteData? Palette);

/// <summary>
/// The textures of one landblock's attach: pins on the resident ones (<see cref="TexturePinSet"/>), images decoded again
/// on a worker for the ones evicted since the prepare, and the upload of images in hand. With it no attach step decodes
/// a texture on the main thread (<c>MaterialFactory</c>'s fallback). Main thread, except <see cref="Decode"/>.
/// </summary>
/// <remarks>
/// <para><b>Lifetime.</b> <see cref="ObjectsSystem"/> creates it when a prepared landblock's attach is decided
/// (<see cref="PinResident"/>); if something is missing, the attach waits until <see cref="Decode"/> has run on a worker.
/// <see cref="LandblockObjectBatches"/> then owns it: each attach unit <see cref="Ensure"/>s one surface's texture, and the
/// pins are released once every mesh holds its materials (or when the landblock leaves early).</para>
/// <para>Pinned textures cannot be evicted, so the texture cache may sit above its budget by one landblock's textures
/// while that landblock attaches; the landblock holds them anyway once it is active.</para>
/// </remarks>
public sealed class LandblockAttachTextures
{
    private readonly RenderResources _render;
    private readonly TexturePinSet _pins = new();
    private readonly Dictionary<TextureKey, PreparedTexture> _decoded = new();
    private readonly Func<TextureKey, bool> _tryAcquire;
    private readonly Action<TextureKey> _release;

    public LandblockAttachTextures(RenderResources render)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _tryAcquire = key => _render.Textures.TryAcquire(key, out _);
        _release = _render.Textures.Release;
    }

    /// <summary>Textures pinned now.</summary>
    public int PinnedCount => _pins.Count;

    /// <summary>Decoded images not uploaded yet.</summary>
    public int DecodedCount => _decoded.Count;

    /// <summary>
    /// Main thread: pins every texture the drawable meshes of <paramref name="prepared"/> name that is resident now (an
    /// image in hand for it is then redundant and freed), and returns the ones that are neither resident, nor an image
    /// in hand, nor already decoded again. Call again after a decode to pick up anything that became resident.
    /// </summary>
    public List<MissingTexture> PinResident(PreparedLandblockObjects prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var missing = new List<MissingTexture>();
        HashSet<TextureKey>? listed = null;
        foreach (var mesh in prepared.Meshes)
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            foreach (var surface in mesh.Surfaces)
            {
                if (surface.Key?.Texture is not { } key || surface.Material is not { } material)
                {
                    continue;
                }

                if (_pins.TryPin(key, _tryAcquire))
                {
                    surface.Texture?.Dispose();
                    continue;
                }

                if (ImageInHand(surface, key) is not null || _decoded.ContainsKey(key))
                {
                    continue;
                }

                if ((listed ??= []).Add(key))
                {
                    missing.Add(new MissingTexture(key, material, surface.Request.Palette));
                }
            }
        }

        return missing;
    }

    /// <summary>
    /// Worker: decodes the missing textures (what <c>MaterialFactory</c> would otherwise do on the main thread). Returns
    /// each request with its decode; on cancellation or failure every decode made so far is freed.
    /// </summary>
    public static (TextureKey Requested, PreparedTexture Texture)[] Decode(RenderResources render, IReadOnlyList<MissingTexture> missing, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(missing);
        var decoded = new (TextureKey, PreparedTexture)[missing.Count];
        var done = 0;
        try
        {
            for (; done < decoded.Length; done++)
            {
                cancellation.ThrowIfCancellationRequested();
                var m = missing[done];
                decoded[done] = (m.Key, render.Textures.PrepareShared(render.Loader, m.Material, m.Key.RenderSurfaceId, m.Palette, render.DxtPolicy)); // wave-3 perf: shared decode
            }
        }
        catch
        {
            for (var i = 0; i < done; i++)
            {
                decoded[i].Item2.Dispose();
            }

            throw;
        }

        return decoded;
    }

    /// <summary>Main thread: takes over the worker's decodes (a decode that is no longer needed is freed).</summary>
    public void AddDecoded(IEnumerable<(TextureKey Requested, PreparedTexture Texture)> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        foreach (var (requested, texture) in decoded)
        {
            if (texture.Key != requested)
            {
                // Not expected (the key comes from the same decode); the material path then reports it.
                Log.Error("objects", "a texture decoded again under another key", ("requested", requested), ("decoded", texture.Key));
                texture.Dispose();
            }
            else if (_pins.Contains(requested) || !_decoded.TryAdd(requested, texture))
            {
                texture.Dispose();
            }
        }
    }

    /// <summary>
    /// Main thread, one attach unit: makes sure <paramref name="surface"/>'s texture is pinned, uploading its image (in
    /// hand or decoded again) when it is not resident. Returns true when it uploaded.
    /// </summary>
    public bool Ensure(PreparedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.Key?.Texture is not { } key || _pins.Contains(key))
        {
            return false;
        }

        var image = ImageInHand(surface, key);
        if (image is null && _decoded.Remove(key, out var decoded))
        {
            image = decoded;
        }

        if (image is not null)
        {
            var uploads = _render.Textures.Uploads;
            if (_render.Textures.TryAcquire(image, out _))
            {
                _pins.Adopt(key, _release);
                return _render.Textures.Uploads != uploads;
            }
        }

        // Resident by now (another landblock uploaded it), else the material path falls back (logged there).
        _pins.TryPin(key, _tryAcquire);
        return false;
    }

    /// <summary>Main thread: drops every pin and frees the decodes not uploaded (idempotent).</summary>
    public void Release()
    {
        _pins.ReleaseAll(_release);
        foreach (var texture in _decoded.Values)
        {
            texture.Dispose();
        }

        _decoded.Clear();
    }

    /// <summary>The surface's own decoded image when it is still there and is the texture its material names.</summary>
    private static PreparedTexture? ImageInHand(PreparedSurface surface, TextureKey key) =>
        surface.Texture is { IsDisposed: false } texture && texture.Key == key ? texture : null;
}
