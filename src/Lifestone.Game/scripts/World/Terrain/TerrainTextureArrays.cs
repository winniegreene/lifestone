using Godot;
using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// The terrain atlas as Godot <see cref="Image"/>s, built off the main thread (<see cref="Prepare"/>). Creating an
/// <see cref="Image"/> touches no rendering resource, so workers may do it (docs/render-contract.md §0); the textures
/// are created by <see cref="TerrainTextureArrays.Create"/> on the main thread.
/// </summary>
/// <remarks>
/// Whoever owns it disposes it: <see cref="TerrainRenderer"/>'s constructor does so right after the upload, which frees
/// the images (~44 MB native) and drops the reference to the managed <see cref="TerrainAtlasPixels"/> (~44 MB).
/// </remarks>
public sealed class PreparedTerrainAtlas : IDisposable
{
    private readonly List<Image> _images;
    private TerrainAtlasPixels? _pixels;
    private int _disposed;

    private PreparedTerrainAtlas(
        TerrainAtlasPixels pixels, Godot.Collections.Array<Image> color, Godot.Collections.Array<Image> alpha, List<Image> images, double decodeMilliseconds)
    {
        _pixels = pixels;
        _images = images;
        ColorImages = color;
        AlphaImages = alpha;
        DecodeMilliseconds = decodeMilliseconds;
    }

    /// <summary>The decoded CPU arrays.</summary>
    /// <exception cref="ObjectDisposedException">After <see cref="Dispose"/>.</exception>
    public TerrainAtlasPixels Pixels => _pixels ?? throw new ObjectDisposedException(nameof(PreparedTerrainAtlas));

    /// <summary>One RGBA8 image with its full mip chain per colour layer, manifest order.</summary>
    public Godot.Collections.Array<Image> ColorImages { get; }

    /// <summary>One R8 image with its full mip chain per alpha map, manifest order.</summary>
    public Godot.Collections.Array<Image> AlphaImages { get; }

    /// <summary>Wall time of decoding + mip generation + image creation.</summary>
    public double DecodeMilliseconds { get; }

    /// <summary>Worker thread: wraps decoded atlas pixels in Godot images (no copies beyond Godot's own).</summary>
    public static PreparedTerrainAtlas Prepare(TerrainAtlasPixels pixels, double decodeMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var images = new List<Image>(pixels.ColorLayers.Count + pixels.AlphaLayers.Count);
        var color = new Godot.Collections.Array<Image>();
        foreach (var layer in pixels.ColorLayers)
        {
            var image = Image.CreateFromData(pixels.ColorSize, pixels.ColorSize, true, Image.Format.Rgba8, layer);
            images.Add(image);
            color.Add(image);
        }

        var alpha = new Godot.Collections.Array<Image>();
        foreach (var layer in pixels.AlphaLayers)
        {
            var image = Image.CreateFromData(pixels.AlphaSize, pixels.AlphaSize, true, Image.Format.R8, layer);
            images.Add(image);
            alpha.Add(image);
        }

        return new PreparedTerrainAtlas(pixels, color, alpha, images, decodeMilliseconds + timer.Elapsed.TotalMilliseconds);
    }

    /// <summary>Frees the Godot images and drops the CPU arrays (any thread; idempotent).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pixels = null;
        ColorImages.Clear();
        AlphaImages.Clear();
        foreach (var image in _images)
        {
            image.Dispose();
        }

        _images.Clear();
    }
}

/// <summary>
/// The two <see cref="Texture2DArray"/>s the terrain shader samples (docs/terrain-shader.md §3.1): RGBA8 colour layers and
/// R8 alpha maps, both with full mip chains, in <see cref="TerrainAtlasManifest"/> layer order. Main thread only.
/// </summary>
public sealed class TerrainTextureArrays : IDisposable
{
    private TerrainTextureArrays(Texture2DArray color, Texture2DArray alpha, long colorBytes, long alphaBytes, double uploadMilliseconds)
    {
        Color = color;
        Alpha = alpha;
        ColorBytes = colorBytes;
        AlphaBytes = alphaBytes;
        UploadMilliseconds = uploadMilliseconds;
    }

    public Texture2DArray Color { get; }

    public Texture2DArray Alpha { get; }

    /// <summary>GPU bytes of the colour array (RGBA8, all levels).</summary>
    public long ColorBytes { get; }

    /// <summary>GPU bytes of the alpha array (R8, all levels).</summary>
    public long AlphaBytes { get; }

    /// <summary>Main-thread time spent creating both arrays.</summary>
    public double UploadMilliseconds { get; }

    /// <summary>Main thread: uploads the prepared images.</summary>
    /// <exception cref="InvalidOperationException">Godot rejected the images.</exception>
    public static TerrainTextureArrays Create(PreparedTerrainAtlas prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var color = new Texture2DArray();
        var error = color.CreateFromImages(prepared.ColorImages);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Terrain colour Texture2DArray creation failed: {error}.");
        }

        var alpha = new Texture2DArray();
        error = alpha.CreateFromImages(prepared.AlphaImages);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Terrain alpha Texture2DArray creation failed: {error}.");
        }

        return new TerrainTextureArrays(color, alpha, prepared.Pixels.ColorBytes, prepared.Pixels.AlphaBytes, timer.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        Color.Dispose();
        Alpha.Dispose();
    }
}
