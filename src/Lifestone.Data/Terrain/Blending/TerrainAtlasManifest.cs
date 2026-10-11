using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>Which TexMerge alpha-map list an alpha layer comes from.</summary>
public enum TerrainAlphaKind
{
    /// <summary><c>TexMerge.corner_terrain_maps</c>: transition into one corner (TCode 1, 2, 4 or 8 after rotation).</summary>
    Corner = 0,

    /// <summary><c>TexMerge.side_terrain_maps</c>: transition along one side (TCode 3, 6, 9 or 12 after rotation).</summary>
    Side = 1,

    /// <summary><c>TexMerge.road_maps</c>: road coverage (RCode family of the map, see <see cref="TerrainTexMerge.GetRoadCodes"/>).</summary>
    Road = 2,
}

/// <summary>
/// Which global detail surface a <see cref="TerrainDetailTexture"/> feeds. Retail <c>LScape::SetDetailTexturing</c>
/// (acclient.c:308209) reads the detail tiling and texture of TMTerrainDesc[0..3] by position and hands them to
/// <c>Render::Set{Landscape,Building,Environment,Object}Detail{Tiling,Surface}</c>.
/// </summary>
public enum TerrainDetailKind
{
    /// <summary>TMTerrainDesc[0]: landscape detail. Never generated in retail (<see cref="TerrainDetailRetailUse.Never"/>).</summary>
    Landscape = 0,

    /// <summary>TMTerrainDesc[1]: building detail. Generated only while the "Environment Detail Textures" preference is on.</summary>
    Building = 1,

    /// <summary>TMTerrainDesc[2]: environment (EnvCell) detail. Generated only while the "Environment Detail Textures" preference is on.</summary>
    Environment = 2,

    /// <summary>TMTerrainDesc[3]: object detail. Never generated in retail (<see cref="TerrainDetailRetailUse.Never"/>).</summary>
    Object = 3,
}

/// <summary>When retail generates a detail surface (<c>LScape::SetDetailTexturing</c>, acclient.c:308209).</summary>
/// <remarks>
/// <para>
/// <c>LScape::SetDetailTexturing(landscape, building, environment, object)</c> always sets all four detail tilings, but it
/// only calls <c>LScape::GenerateDetailSurface</c> (acclient.c:307691) and <c>Render::Set*DetailSurface</c> for the flags
/// that are non-zero. Every caller passes <c>landscape = 0</c>, <c>object = 0</c> and <c>building = environment</c>:
/// </para>
/// <list type="bullet">
/// <item><c>SmartBox::SetDetailTexturing(landscape, environment)</c> (acclient.c:143221) forwards
/// <c>(landscape, environment, environment, 0)</c>. Its only caller, <c>Render::UpdateFromPreferences</c>
/// (acclient.c:381026), passes <c>landscape = 0</c>.</item>
/// <item><c>SmartBox::ResetDetailTexturing</c> (acclient.c:144205) and <c>LScape::ChangeRegion</c> (acclient.c:308276)
/// pass <c>(0, pref, pref, 0)</c>.</item>
/// </list>
/// <para>
/// <c>pref</c> is the "Environment Detail Textures" user preference (<c>byte_81EF95</c>, registered at acclient.c:382015
/// under the key <c>Render_BuildingDetailTextures</c>). It defaults to on (acclient.c:45603). The overall-quality presets set
/// it to 0/0/0/1/1 (<c>Render::SetOverallGraphicsQuality</c>, acclient.c:378743), and safe render settings turn it off
/// (acclient.c:382204). <c>Render::SetObjectDetailSurface</c> is otherwise only called with 0, by
/// <c>LScape::CleanupDetailSurfaces</c> (acclient.c:306372).
/// </para>
/// </remarks>
public enum TerrainDetailRetailUse
{
    /// <summary>Retail never generates this detail surface (landscape and object detail), so do not draw it.</summary>
    Never = 0,

    /// <summary>Generated only while the "Environment Detail Textures" preference is on (building and environment detail).</summary>
    EnvironmentDetailPreference = 1,
}

/// <summary>
/// One TMTerrainDesc entry of the Region's TexMerge, exactly as stored (dat order). <see cref="Layer"/> is the terrain
/// atlas layer that holds <see cref="SurfaceTextureId"/>.
/// </summary>
/// <remarks>
/// The vertex brightness / saturation / hue ranges are carried for completeness only: in the 11.4186 decompile they are
/// read by <c>TerrainTex::UnPack</c> and written by <c>TerrainTex::Pack</c> and referenced nowhere else, so retail does
/// not use them. Only the detail fields of entries 0-3 are ever read (see <see cref="TerrainDetailTexture"/>).
/// </remarks>
public sealed record TerrainTextureDesc(
    int Index,
    uint TerrainType,
    uint SurfaceTextureId,
    uint TexTiling,
    uint MaxVertBright,
    uint MinVertBright,
    uint MaxVertSaturate,
    uint MinVertSaturate,
    uint MaxVertHue,
    uint MinVertHue,
    uint DetailTexTiling,
    uint DetailSurfaceTextureId,
    int Layer);

/// <summary>
/// One layer of the terrain colour atlas (a Texture2DArray layer in Godot): a unique (SurfaceTexture, TexTiling) pair.
/// </summary>
/// <param name="Layer">Layer index in the atlas.</param>
/// <param name="SurfaceTextureId">SurfaceTexture (0x05) id; the textures track resolves it to a RenderSurface.</param>
/// <param name="TexTiling">Repeats of the texture across one 24 m cell (retail <c>TerrainTex::tex_tiling</c>; 2 in Dereth).</param>
/// <param name="TerrainTypes">Terrain types (0-31, 32 = road) whose <c>TexMerge::GetTerrainTex</c> resolves to this layer.</param>
public sealed record TerrainTextureLayer(int Layer, uint SurfaceTextureId, uint TexTiling, IReadOnlyList<int> TerrainTypes);

/// <summary>One layer of the terrain alpha atlas: an 8-bit TexMerge alpha map.</summary>
/// <param name="Layer">Layer index in the alpha atlas (corner maps first, then side maps, then road maps, each in dat order).</param>
/// <param name="Kind">The TexMerge list this map comes from.</param>
/// <param name="IndexInKind">Index within that list (the value retail's pseudo-random pick produces).</param>
/// <param name="Code">The map's TCode (corner/side) or RCode (road) as stored, i.e. its rotation-0 coverage.</param>
/// <param name="SurfaceTextureId">SurfaceTexture (0x05) id of the alpha map.</param>
public sealed record TerrainAlphaLayer(int Layer, TerrainAlphaKind Kind, int IndexInKind, uint Code, uint SurfaceTextureId);

/// <summary>
/// A global detail texture taken from TMTerrainDesc[0..3]: retail <c>TexMerge::GetDetailTex</c> (acclient.c:304939) and
/// <c>TexMerge::GetDetailTiling</c> (acclient.c:304957), read by <c>LScape::SetDetailTexturing</c> (acclient.c:308209).
/// </summary>
/// <param name="Kind">Which detail surface this is.</param>
/// <param name="DescIndex">TMTerrainDesc position it comes from (0-3).</param>
/// <param name="SurfaceTextureId">SurfaceTexture (0x05) id of the detail texture.</param>
/// <param name="Tiling">Detail repeats (retail <c>detail_tex_tiling</c>, passed to <c>Render::Set*DetailTiling</c>).</param>
/// <param name="RetailUse">
/// When retail generates this surface: <see cref="TerrainDetailRetailUse.Never"/> for landscape and object detail,
/// <see cref="TerrainDetailRetailUse.EnvironmentDetailPreference"/> for building and environment detail.
/// </param>
public sealed record TerrainDetailTexture(TerrainDetailKind Kind, int DescIndex, uint SurfaceTextureId, uint Tiling, TerrainDetailRetailUse RetailUse);

/// <summary>
/// Everything the presentation layer needs to build the terrain texture arrays: the Region's TexMerge resolved into
/// a deduplicated colour atlas, an alpha atlas, the terrain-type → layer table and the detail textures. Reference ids
/// only; decoding the SurfaceTextures belongs to the textures track.
/// </summary>
/// <remarks>
/// <para>Source: Region 0x13000000 <c>TerrainInfo.LandSurfaces.TexMerge</c> (retail <c>TexMerge::UnPack</c>, CTexMerge.c).</para>
/// <para>
/// Terrain-type resolution follows retail <c>TexMerge::GetTerrainTex</c> (CTexMerge.c, acclient.c:304869): the first
/// TMTerrainDesc whose <c>terrain_type</c> equals the requested type, else TMTerrainDesc[0].
/// </para>
/// </remarks>
public sealed class TerrainAtlasManifest
{
    /// <summary>Terrain type of the road texture (retail <c>TexMerge::GetTerrainTex(0x20)</c> in <c>FillTempTexBuffer</c>).</summary>
    public const int RoadTerrainType = 0x20;

    /// <summary>Number of terrain types addressable from a terrain word (5 bits) plus the road type.</summary>
    public const int TerrainTypeCount = RoadTerrainType + 1;

    /// <summary>
    /// Retail never draws landscape detail textures: <c>Render::UpdateFromPreferences</c> forces the preference off and
    /// calls <c>SmartBox::SetDetailTexturing(landscape = 0, ...)</c> (acclient.c:381020-381026; Mag-nus acclient-AI-RE
    /// docs/06-rendering.md §5). Every other caller of <c>LScape::SetDetailTexturing</c> passes landscape = 0 too (see
    /// <see cref="TerrainDetailRetailUse"/>).
    /// </summary>
    public const bool LandscapeDetailTexturingEnabled = false;

    private readonly int[] _layerByTerrainType;
    private readonly int[] _descByTerrainType;

    private TerrainAtlasManifest(
        uint baseTexSize,
        IReadOnlyList<TerrainTextureDesc> descs,
        IReadOnlyList<TerrainTextureLayer> layers,
        IReadOnlyList<TerrainAlphaLayer> alphaLayers,
        IReadOnlyList<TerrainDetailTexture> detailTextures,
        int[] layerByTerrainType,
        int[] descByTerrainType)
    {
        BaseTexSize = baseTexSize;
        TerrainDescs = descs;
        TerrainLayers = layers;
        AlphaLayers = alphaLayers;
        DetailTextures = detailTextures;
        _layerByTerrainType = layerByTerrainType;
        _descByTerrainType = descByTerrainType;
        CornerAlphaLayers = alphaLayers.Where(a => a.Kind == TerrainAlphaKind.Corner).ToArray();
        SideAlphaLayers = alphaLayers.Where(a => a.Kind == TerrainAlphaKind.Side).ToArray();
        RoadAlphaLayers = alphaLayers.Where(a => a.Kind == TerrainAlphaKind.Road).ToArray();
        RoadLayer = layerByTerrainType[RoadTerrainType];
    }

    /// <summary>Edge length in texels of retail's merged per-cell texture (<c>TexMerge::base_tex_size</c>; 1024 in Dereth).</summary>
    public uint BaseTexSize { get; }

    /// <summary>The TMTerrainDesc entries in dat order (33 in Dereth: terrain types 0-31 and the road, 32).</summary>
    public IReadOnlyList<TerrainTextureDesc> TerrainDescs { get; }

    /// <summary>Colour atlas layers, deduplicated by (SurfaceTexture id, TexTiling) in order of first use.</summary>
    public IReadOnlyList<TerrainTextureLayer> TerrainLayers { get; }

    /// <summary>All alpha maps: corner maps, then side maps, then road maps (each in dat order).</summary>
    public IReadOnlyList<TerrainAlphaLayer> AlphaLayers { get; }

    /// <summary>The corner alpha maps (a contiguous run of <see cref="AlphaLayers"/>).</summary>
    public IReadOnlyList<TerrainAlphaLayer> CornerAlphaLayers { get; }

    /// <summary>The side alpha maps (a contiguous run of <see cref="AlphaLayers"/>).</summary>
    public IReadOnlyList<TerrainAlphaLayer> SideAlphaLayers { get; }

    /// <summary>The road alpha maps (a contiguous run of <see cref="AlphaLayers"/>).</summary>
    public IReadOnlyList<TerrainAlphaLayer> RoadAlphaLayers { get; }

    /// <summary>Colour atlas layer of the road texture (terrain type 32).</summary>
    public int RoadLayer { get; }

    /// <summary>
    /// Global detail textures from TMTerrainDesc[0..3] (only those entries exist). Check
    /// <see cref="TerrainDetailTexture.RetailUse"/> before drawing one: retail never generates the landscape or object
    /// detail surface.
    /// </summary>
    public IReadOnlyList<TerrainDetailTexture> DetailTextures { get; }

    /// <summary>Colour atlas layer used for a terrain type (0-31, or 32 for the road), per <c>TexMerge::GetTerrainTex</c>.</summary>
    public int LayerForTerrainType(int terrainType)
    {
        if ((uint)terrainType >= TerrainTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(terrainType), terrainType, "Terrain types are 0-32.");
        }

        return _layerByTerrainType[terrainType];
    }

    /// <summary>The TMTerrainDesc index <c>TexMerge::GetTerrainTex</c> returns for a terrain type (0-32).</summary>
    public int DescIndexForTerrainType(int terrainType)
    {
        if ((uint)terrainType >= TerrainTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(terrainType), terrainType, "Terrain types are 0-32.");
        }

        return _descByTerrainType[terrainType];
    }

    /// <summary>Repeats per cell of a colour atlas layer.</summary>
    public uint TilingOfLayer(int layer) => TerrainLayers[layer].TexTiling;

    /// <summary>Builds the manifest from the dats' Region record.</summary>
    public static TerrainAtlasManifest FromDatSet(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return FromRegion(dats.Region);
    }

    /// <summary>Builds the manifest from a Region record.</summary>
    /// <exception cref="InvalidDataException">The Region has no usable TexMerge.</exception>
    /// <exception cref="NotSupportedException">The Region uses PalShift landscape surfaces (not Dereth).</exception>
    public static TerrainAtlasManifest FromRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var landSurf = region.TerrainInfo?.LandSurfaces
            ?? throw new InvalidDataException("Region has no TerrainInfo.LandSurfaces.");

        // LandSurf::UnPack: a non-zero first word selects PalShift instead of TexMerge. Dereth uses TexMerge (0).
        if (landSurf.Type != 0)
        {
            throw new NotSupportedException(
                $"Region LandSurf type {landSurf.Type} (PalShift) is not implemented; only TexMerge (0) is supported.");
        }

        return FromTexMerge(landSurf.TexMerge ?? throw new InvalidDataException("Region LandSurf has no TexMerge."));
    }

    /// <summary>Builds the manifest from a decoded TexMerge.</summary>
    public static TerrainAtlasManifest FromTexMerge(TexMerge texMerge)
    {
        ArgumentNullException.ThrowIfNull(texMerge);
        var terrainDesc = texMerge.TerrainDesc ?? [];
        if (terrainDesc.Count == 0)
        {
            // Retail GetTerrainTex returns null and CopyCSI paints the cell green; refuse instead of guessing.
            throw new InvalidDataException("TexMerge has no TMTerrainDesc entries.");
        }

        if ((texMerge.CornerTerrainMaps?.Count ?? 0) == 0 || (texMerge.SideTerrainMaps?.Count ?? 0) == 0)
        {
            // Retail FindTerrainAlpha indexes element 0 of an empty list (undefined behaviour).
            throw new InvalidDataException("TexMerge needs at least one corner and one side alpha map.");
        }

        // Colour layers, deduplicated by (SurfaceTexture, tiling) in dat order.
        var layerKeys = new List<(uint Id, uint Tiling)>();
        var descLayer = new int[terrainDesc.Count];
        for (var i = 0; i < terrainDesc.Count; i++)
        {
            var tex = terrainDesc[i].TerrainTex ?? throw new InvalidDataException($"TMTerrainDesc[{i}] has no TerrainTex.");
            var key = (tex.TextureId?.DataId ?? 0u, tex.TexTiling);
            var layer = layerKeys.IndexOf(key);
            if (layer < 0)
            {
                layer = layerKeys.Count;
                layerKeys.Add(key);
            }

            descLayer[i] = layer;
        }

        // TexMerge::GetTerrainTex: first desc with a matching terrain_type, else desc 0.
        var descByType = new int[TerrainTypeCount];
        var layerByType = new int[TerrainTypeCount];
        for (var t = 0; t < TerrainTypeCount; t++)
        {
            var match = 0;
            for (var i = 0; i < terrainDesc.Count; i++)
            {
                if ((uint)terrainDesc[i].TerrainType == (uint)t)
                {
                    match = i;
                    break;
                }
            }

            descByType[t] = match;
            layerByType[t] = descLayer[match];
        }

        var layers = new List<TerrainTextureLayer>(layerKeys.Count);
        for (var l = 0; l < layerKeys.Count; l++)
        {
            var types = Enumerable.Range(0, TerrainTypeCount).Where(t => layerByType[t] == l).ToArray();
            layers.Add(new TerrainTextureLayer(l, layerKeys[l].Id, layerKeys[l].Tiling, types));
        }

        var descs = new List<TerrainTextureDesc>(terrainDesc.Count);
        for (var i = 0; i < terrainDesc.Count; i++)
        {
            var d = terrainDesc[i];
            var tex = d.TerrainTex;
            descs.Add(new TerrainTextureDesc(
                i,
                (uint)d.TerrainType,
                tex.TextureId?.DataId ?? 0u,
                tex.TexTiling,
                tex.MaxVertBright,
                tex.MinVertBright,
                tex.MaxVertSaturate,
                tex.MinVertSaturate,
                tex.MaxVertHue,
                tex.MinVertHue,
                tex.DetailTexTiling,
                tex.DetailTextureId?.DataId ?? 0u,
                descLayer[i]));
        }

        var alpha = new List<TerrainAlphaLayer>();
        AddAlpha(alpha, TerrainAlphaKind.Corner, texMerge.CornerTerrainMaps!.Select(m => (m.TCode, m.TextureId?.DataId ?? 0u)));
        AddAlpha(alpha, TerrainAlphaKind.Side, texMerge.SideTerrainMaps!.Select(m => (m.TCode, m.TextureId?.DataId ?? 0u)));
        AddAlpha(alpha, TerrainAlphaKind.Road, (texMerge.RoadMaps ?? []).Select(m => (m.RCode, m.TextureId?.DataId ?? 0u)));
        if (alpha.Count > 0xFE)
        {
            throw new InvalidDataException($"{alpha.Count} alpha maps do not fit the byte-sized layer index.");
        }

        if (layers.Count > 0xFE)
        {
            throw new InvalidDataException($"{layers.Count} terrain layers do not fit the byte-sized layer index.");
        }

        // LScape::SetDetailTexturing (acclient.c:308209): detail tiling/texture of desc 0 = landscape, 1 = building,
        // 2 = environment, 3 = object (via LandSurf::GetDetailTiling/GetDetailTex -> TexMerge::GetDetail*, which index
        // terrain_desc by position). Every caller passes landscape = 0, object = 0 and building = environment = the
        // "Environment Detail Textures" preference (see TerrainDetailRetailUse).
        var details = new List<TerrainDetailTexture>();
        for (var i = 0; i < Math.Min(4, descs.Count); i++)
        {
            var kind = (TerrainDetailKind)i;
            details.Add(new TerrainDetailTexture(
                kind,
                i,
                descs[i].DetailSurfaceTextureId,
                descs[i].DetailTexTiling,
                kind is TerrainDetailKind.Building or TerrainDetailKind.Environment
                    ? TerrainDetailRetailUse.EnvironmentDetailPreference
                    : TerrainDetailRetailUse.Never));
        }

        return new TerrainAtlasManifest(texMerge.BaseTexSize, descs, layers, alpha, details, layerByType, descByType);
    }

    private static void AddAlpha(List<TerrainAlphaLayer> into, TerrainAlphaKind kind, IEnumerable<(uint Code, uint Id)> maps)
    {
        var index = 0;
        foreach (var (code, id) in maps)
        {
            into.Add(new TerrainAlphaLayer(into.Count, kind, index++, code, id));
        }
    }
}
