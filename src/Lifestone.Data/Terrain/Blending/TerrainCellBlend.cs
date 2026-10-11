namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// Retail <c>LandDefs::Rotation</c> of an alpha map inside a cell. The alpha map is sampled at the rotated cell UV
/// (u east, v south, (0,0) = north-west corner), exactly as <c>ImgTex::MergeTexture</c> walks it (ImgTex.c:353):
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Rot0"/>: (u, v)</item>
/// <item><see cref="Rot1"/>: (1 - v, u)</item>
/// <item><see cref="Rot2"/>: (1 - u, 1 - v)</item>
/// <item><see cref="Rot3"/>: (v, 1 - u)</item>
/// </list>
/// Each step also maps a coverage code c to <see cref="TerrainTexMerge.RotateCode"/>(c), e.g. corner NW (8) → SW (1).
/// </remarks>
public enum TerrainRotation : byte
{
    Rot0 = 0,
    Rot1 = 1,
    Rot2 = 2,
    Rot3 = 3,
}

/// <summary>
/// One terrain overlay: the overlay terrain texture is merged over what is underneath through an alpha map.
/// </summary>
/// <param name="TerrainType">Overlay terrain type (0-31).</param>
/// <param name="TextureLayer">Colour atlas layer (<see cref="TerrainAtlasManifest.TerrainLayers"/>).</param>
/// <param name="AlphaLayer">Alpha atlas layer (<see cref="TerrainAtlasManifest.AlphaLayers"/>).</param>
/// <param name="Rotation">Rotation applied to the alpha map.</param>
/// <param name="TCode">Coverage code: corners where the overlay terrain sits (SW = 1, SE = 2, NE = 4, NW = 8).</param>
public readonly record struct TerrainOverlay(byte TerrainType, byte TextureLayer, byte AlphaLayer, TerrainRotation Rotation, byte TCode);

/// <summary>One road layer: the road texture merged through a road alpha map.</summary>
/// <param name="AlphaLayer">Alpha atlas layer (<see cref="TerrainAtlasManifest.AlphaLayers"/>).</param>
/// <param name="Rotation">Rotation applied to the alpha map.</param>
/// <param name="RCode">Road coverage code (corners with road; SW = 1, SE = 2, NE = 4, NW = 8).</param>
public readonly record struct TerrainRoadOverlay(byte AlphaLayer, TerrainRotation Rotation, byte RCode);

/// <summary>
/// The retail TexMerge recipe for one landscape cell, in the exact order retail composites it
/// (<c>TexMerge::FillTempTexBuffer</c>, CTexMerge.c, acclient.c:305908):
/// base (tiled) → <see cref="Overlay0"/> → <see cref="Overlay1"/> → <see cref="Overlay2"/> → <see cref="Road0"/> →
/// <see cref="Road1"/>. Only the first <see cref="OverlayCount"/> overlays and <see cref="RoadCount"/> roads are used;
/// unused slots are <c>default</c>.
/// </summary>
/// <param name="PalCode">The cell's palette code (see <see cref="TerrainPalCode"/>); the recipe is a pure function of it.</param>
/// <param name="BaseTerrainType">Terrain type of the base layer (0-31, or 32 when <see cref="IsAllRoad"/>).</param>
/// <param name="BaseTextureLayer">Colour atlas layer of the base.</param>
/// <param name="IsAllRoad">All four corners carry road bits: the base is the road texture and nothing is merged over it.</param>
/// <param name="IsSingleTexture">
/// Retail <c>bSingleTextureCell</c> from <c>CLandBlockStruct::GetCellRotation</c>: the cell's polygons get stippling 3, which
/// makes <c>D3DPolyRender::SetSurface</c> sample the merged texture with WRAP addressing instead of CLAMP.
/// </param>
/// <param name="OverlayCount">Number of terrain overlays in use (0-3).</param>
/// <param name="Overlay0">First terrain overlay (if <see cref="OverlayCount"/> &gt; 0).</param>
/// <param name="Overlay1">Second terrain overlay (if <see cref="OverlayCount"/> &gt; 1).</param>
/// <param name="Overlay2">Third terrain overlay (if <see cref="OverlayCount"/> &gt; 2; only when all four corners differ).</param>
/// <param name="RoadCount">Number of road layers in use (0-2).</param>
/// <param name="RoadTextureLayer">Colour atlas layer of the road texture when <see cref="RoadCount"/> &gt; 0, else <see cref="None"/>.</param>
/// <param name="Road0">First road layer (if <see cref="RoadCount"/> &gt; 0).</param>
/// <param name="Road1">Second road layer (if <see cref="RoadCount"/> &gt; 1).</param>
public readonly record struct TerrainCellBlend(
    uint PalCode,
    byte BaseTerrainType,
    byte BaseTextureLayer,
    bool IsAllRoad,
    bool IsSingleTexture,
    byte OverlayCount,
    TerrainOverlay Overlay0,
    TerrainOverlay Overlay1,
    TerrainOverlay Overlay2,
    byte RoadCount,
    byte RoadTextureLayer,
    TerrainRoadOverlay Road0,
    TerrainRoadOverlay Road1)
{
    /// <summary>Marker for "no layer" in byte-sized layer fields and packed data.</summary>
    public const byte None = 0xFF;

    /// <summary>Overlay <paramref name="index"/> (0 .. <see cref="OverlayCount"/> - 1).</summary>
    public TerrainOverlay GetOverlay(int index) =>
        (uint)index < OverlayCount
            ? index switch { 0 => Overlay0, 1 => Overlay1, _ => Overlay2 }
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"Cell has {OverlayCount} overlays.");

    /// <summary>Road layer <paramref name="index"/> (0 .. <see cref="RoadCount"/> - 1).</summary>
    public TerrainRoadOverlay GetRoad(int index) =>
        (uint)index < RoadCount
            ? index == 0 ? Road0 : Road1
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"Cell has {RoadCount} road layers.");

    public override string ToString()
    {
        var parts = new List<string> { $"pal=0x{PalCode:X8}", $"base t{BaseTerrainType}/L{BaseTextureLayer}" };
        if (IsAllRoad)
        {
            parts.Add("all-road");
        }

        for (var i = 0; i < OverlayCount; i++)
        {
            var o = GetOverlay(i);
            parts.Add($"ovl{i} t{o.TerrainType}/L{o.TextureLayer} a{o.AlphaLayer} r{(int)o.Rotation} tc{o.TCode}");
        }

        for (var i = 0; i < RoadCount; i++)
        {
            var r = GetRoad(i);
            parts.Add($"road{i} L{RoadTextureLayer} a{r.AlphaLayer} r{(int)r.Rotation} rc{r.RCode}");
        }

        return string.Join(", ", parts);
    }
}
