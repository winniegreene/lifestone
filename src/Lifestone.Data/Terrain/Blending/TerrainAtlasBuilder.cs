using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>A decoded SurfaceTexture handed to <see cref="TerrainAtlasBuilder.Build"/>.</summary>
/// <param name="Texture">The decoded RenderSurface: <see cref="DecodedTextureFormat.Rgba8"/> for colour layers,
/// <see cref="DecodedTextureFormat.Alpha8"/> for alpha maps.</param>
/// <param name="RenderSurfaceId">The RenderSurface that was decoded.</param>
/// <param name="Dat">The dat it was read from.</param>
public sealed record TerrainAtlasSourceTexture(DecodedTexture Texture, uint RenderSurfaceId, TextureDat Dat);

/// <summary>
/// Builds <see cref="TerrainAtlasPixels"/> from a <see cref="TerrainAtlasManifest"/>: decodes every colour layer and alpha
/// map, brings each array to one size, keeps the manifest's layer order and generates the mip chains. Thread-safe and
/// engine-agnostic; run it on a worker thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which level of a SurfaceTexture.</b> Retail resolves a SurfaceTexture through <c>ImgTex::GetSurfaceDID</c>
/// (acclient.c:366232): level [0], or level [1] of a two-level list when <c>Render::ShouldDropHighDetail()</c>.
/// <see cref="FromDats"/> uses <see cref="TextureLoader.ResolveSurfaceTexture"/> with <see cref="TextureDetail.High"/> by
/// default: level [0], read from <c>client_highres.dat</c> first (docs/textures.md §4). For Dereth's terrain this choice
/// changes nothing: all 30 colour layers are single-level SurfaceTextures (portal A8R8G8B8 512²), and the 8 alpha maps
/// list a <c>client_highres.dat</c> level [0] whose bytes are identical to their portal level [1]
/// (<c>TerrainAtlasBuilderTests</c> pins both facts).
/// </para>
/// <para>
/// <b>Decoding.</b> Colour layers use <see cref="TextureDecodeMode.Rgba8"/> (A8R8G8B8 is stored B,G,R,A and comes out
/// R,G,B,A; docs/textures.md §1). Alpha maps (PFID_CUSTOM_LSCAPE_ALPHA) use <see cref="TextureDecodeMode.Native"/>, which
/// keeps the raw byte <c>ImgTex::MergeTexture</c> reads. An alpha map in any other format is outside what retail's merge
/// reads and throws <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// <b>Sizes.</b> A <c>Texture2DArray</c> needs one size per array, and every Dereth layer is 512², so nothing is ever
/// resized. A region whose colour layers (or whose alpha maps) differ in size <b>fails loudly</b> with a
/// <c>TODO(terrain)</c> <see cref="NotSupportedException"/>: retail's <c>ImgTex::TileCSI</c>/<c>ImgTex::MergeTexture</c>
/// address each layer by its own size and only support layer × tiling = BaseTexSize (see
/// <see cref="TerrainCompositor.ComposeRetailTexture"/>), so resampling a layer to share an array would be invented
/// behaviour. Non-square layers throw too. The colour array and the alpha array may differ from each other.
/// </para>
/// </remarks>
public static class TerrainAtlasBuilder
{
    /// <summary>Builds the arrays from the user's dats.</summary>
    /// <param name="dats">The opened dats; <see cref="DatSet.TerrainAtlas"/> supplies the manifest.</param>
    /// <param name="loader">Texture loader to use (a new one over <paramref name="dats"/> when null).</param>
    /// <param name="detail">SurfaceTexture level preference (see the remarks).</param>
    /// <exception cref="InvalidDataException">A SurfaceTexture of the manifest does not resolve to a RenderSurface.</exception>
    public static TerrainAtlasPixels FromDats(DatSet dats, TextureLoader? loader = null, TextureDetail detail = TextureDetail.High)
    {
        ArgumentNullException.ThrowIfNull(dats);
        loader ??= new TextureLoader(dats);
        return Build(dats.TerrainAtlas, (surfaceTextureId, isAlpha) =>
        {
            var resolution = loader.ResolveSurfaceTexture(surfaceTextureId, detail)
                ?? throw new InvalidDataException($"Terrain SurfaceTexture 0x{surfaceTextureId:X8} does not exist.");
            if (!resolution.IsResolved)
            {
                throw new InvalidDataException($"Terrain SurfaceTexture 0x{surfaceTextureId:X8}: {resolution}.");
            }

            var mode = isAlpha ? TextureDecodeMode.Native : TextureDecodeMode.Rgba8;
            var texture = loader.DecodeRenderSurface(resolution.RenderSurfaceId, mode);
            return new TerrainAtlasSourceTexture(texture, resolution.RenderSurfaceId, resolution.Source);
        });
    }

    /// <summary>Builds the arrays from any texture source (tests use synthetic ones).</summary>
    /// <param name="manifest">Layer order and ids.</param>
    /// <param name="decode">Decodes a SurfaceTexture id; the flag is true for alpha maps. Called concurrently only with
    /// <paramref name="parallelDecode"/>.</param>
    /// <param name="parallel">Build the mip chains in parallel (default).</param>
    /// <param name="parallelDecode">
    /// Also call <paramref name="decode"/> in parallel. Off by default (wave-3 performance pass): the dat reads serialise
    /// on the dat lock anyway, and every thread that reads a 512² layer leaves its 1-2 MB read buffers cached in
    /// <c>ArrayPool&lt;byte&gt;.Shared</c> (DatReaderWriter rents one per read), so a parallel decode kept tens of MB
    /// alive for minutes after the atlas was uploaded. Measured (build from the dats in a fresh process, drop the atlas,
    /// full compacting GC; 32 hardware threads, Debug, three runs each): 80-82 MB retained in parallel against 5 MB
    /// sequentially, for 46-49 ms against 111-114 ms of wall time on the worker, which the main thread's start-up
    /// warm-ups hide (docs/terrain-renderer.md §1).
    /// </param>
    public static TerrainAtlasPixels Build(
        TerrainAtlasManifest manifest,
        Func<uint, bool, TerrainAtlasSourceTexture> decode,
        bool parallel = true,
        bool parallelDecode = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(decode);

        var colorCount = manifest.TerrainLayers.Count;
        var alphaCount = manifest.AlphaLayers.Count;
        if (colorCount == 0 || alphaCount == 0)
        {
            throw new InvalidDataException($"The manifest has {colorCount} colour and {alphaCount} alpha layers; both arrays need at least one.");
        }

        var sources = new TerrainAtlasSourceTexture[colorCount + alphaCount];
        void DecodeOne(int i)
        {
            var isAlpha = i >= colorCount;
            var id = isAlpha ? manifest.AlphaLayers[i - colorCount].SurfaceTextureId : manifest.TerrainLayers[i].SurfaceTextureId;
            var source = decode(id, isAlpha) ?? throw new InvalidDataException($"No texture for SurfaceTexture 0x{id:X8}.");
            var expected = isAlpha ? DecodedTextureFormat.Alpha8 : DecodedTextureFormat.Rgba8;
            if (source.Texture.Format != expected)
            {
                // TODO(terrain): an alpha map that is not CUSTOM_LSCAPE_ALPHA / A8 would need retail's MergeTexture byte
                // addressing for that format; Dereth has none.
                throw new NotSupportedException(
                    $"Terrain {(isAlpha ? "alpha map" : "colour layer")} 0x{id:X8} (RenderSurface 0x{source.RenderSurfaceId:X8}, " +
                    $"{source.Texture.SourceFormat}) decoded to {source.Texture.Format}; expected {expected}.");
            }

            if (source.Texture.Width != source.Texture.Height)
            {
                throw new NotSupportedException(
                    $"Terrain texture 0x{id:X8} is {source.Texture.Width}x{source.Texture.Height}; only square textures are supported.");
            }

            sources[i] = source;
        }

        if (parallel && parallelDecode)
        {
            Parallel.For(0, sources.Length, DecodeOne);
        }
        else
        {
            for (var i = 0; i < sources.Length; i++)
            {
                DecodeOne(i);
            }
        }

        var colorSize = CommonSize(manifest, sources, 0, colorCount, isAlpha: false);
        var alphaSize = CommonSize(manifest, sources, colorCount, alphaCount, isAlpha: true);
        var colorLayers = new byte[colorCount][];
        var alphaLayers = new byte[alphaCount][];
        var colorSources = new TerrainAtlasLayerSource[colorCount];
        var alphaSources = new TerrainAtlasLayerSource[alphaCount];

        void Finish(int i)
        {
            var isAlpha = i >= colorCount;
            var layer = isAlpha ? i - colorCount : i;
            var source = sources[i];
            var size = isAlpha ? alphaSize : colorSize;
            var bytesPerTexel = isAlpha ? 1 : 4;
            var chain = TerrainMipChain.Build(source.Texture.Data, size, bytesPerTexel);
            var id = isAlpha ? manifest.AlphaLayers[layer].SurfaceTextureId : manifest.TerrainLayers[layer].SurfaceTextureId;
            var info = new TerrainAtlasLayerSource(layer, id, source.RenderSurfaceId, source.Dat, source.Texture.SourceFormat, source.Texture.Width);
            if (isAlpha)
            {
                alphaLayers[layer] = chain;
                alphaSources[layer] = info;
            }
            else
            {
                colorLayers[layer] = chain;
                colorSources[layer] = info;
            }
        }

        if (parallel)
        {
            Parallel.For(0, sources.Length, Finish);
        }
        else
        {
            for (var i = 0; i < sources.Length; i++)
            {
                Finish(i);
            }
        }

        return new TerrainAtlasPixels(manifest, colorSize, alphaSize, colorLayers, alphaLayers, colorSources, alphaSources);
    }

    /// <summary>The one edge length shared by the <paramref name="count"/> layers of one array, or a loud failure.</summary>
    /// <exception cref="NotSupportedException">The layers do not all have the same size (<c>TODO(terrain)</c>).</exception>
    private static int CommonSize(TerrainAtlasManifest manifest, TerrainAtlasSourceTexture[] sources, int first, int count, bool isAlpha)
    {
        var size = sources[first].Texture.Width;
        if (Enumerable.Range(first, count).All(i => sources[i].Texture.Width == size))
        {
            return size;
        }

        // TODO(terrain): retail addresses every layer by its own size (ImgTex::TileCSI, ImgTex::MergeTexture) and only
        // supports layer size x TexTiling = BaseTexSize. Drawing such a region faithfully would need one texture array
        // per size (or per-layer UV scales). Dereth has no such region, so it is not modelled, and resampling a layer
        // to share an array would be a guess.
        var layers = string.Join(", ", Enumerable.Range(first, count).Select(i =>
        {
            var id = isAlpha ? manifest.AlphaLayers[i - first].SurfaceTextureId : manifest.TerrainLayers[i - first].SurfaceTextureId;
            return $"0x{id:X8} {sources[i].Texture.Width}²";
        }));
        throw new NotSupportedException(
            $"TODO(terrain): the terrain {(isAlpha ? "alpha maps" : "colour layers")} differ in size ({layers}). A texture array " +
            "needs one size, and retail addresses each layer by its own size (ImgTex::TileCSI/MergeTexture); not modelled.");
    }
}
