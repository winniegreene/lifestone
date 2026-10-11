using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Data.Tests.Terrain.Oracle;

/// <summary>Compares a product <see cref="TerrainCellBlend"/> with the ACViewer oracle's result.</summary>
internal static class OracleComparison
{
    /// <summary>Null when equal, else a description of the first difference.</summary>
    /// <param name="checkAcViewerAtlasIndex">
    /// Also require ACViewer's hard-coded alpha atlas index (corners 0-3, side 4 + i, roads 5 + i) to equal ours; only
    /// meaningful for a TexMerge with 4 corner maps and 1 side map (Dereth).
    /// </param>
    public static string? Difference(in TerrainCellBlend product, OracleBlend oracle, TerrainAtlasManifest manifest, bool checkAcViewerAtlasIndex)
    {
        if (oracle.Failed)
        {
            return "oracle failed to find a terrain alpha map but the product produced a blend";
        }

        if (product.PalCode != oracle.PalCode)
        {
            return $"palCode {product.PalCode:X8} vs {oracle.PalCode:X8}";
        }

        if (product.IsAllRoad != oracle.AllRoad)
        {
            return $"allRoad {product.IsAllRoad} vs {oracle.AllRoad}";
        }

        if (product.BaseTerrainType != oracle.BaseTerrainType)
        {
            return $"base type {product.BaseTerrainType} vs {oracle.BaseTerrainType}";
        }

        var baseLayer = manifest.TerrainDescs[oracle.BaseDescIndex].Layer;
        if (product.BaseTextureLayer != baseLayer)
        {
            return $"base layer {product.BaseTextureLayer} vs {baseLayer}";
        }

        if (product.OverlayCount != oracle.Overlays.Count)
        {
            return $"overlay count {product.OverlayCount} vs {oracle.Overlays.Count}";
        }

        for (var i = 0; i < product.OverlayCount; i++)
        {
            var p = product.GetOverlay(i);
            var o = oracle.Overlays[i];
            var alphaLayer = (o.IsCorner ? manifest.CornerAlphaLayers : manifest.SideAlphaLayers)[o.Prng].Layer;
            var layer = manifest.TerrainDescs[o.DescIndex].Layer;
            if (p.TerrainType != o.TerrainType || p.TextureLayer != layer || p.TCode != o.TCode
                || (int)p.Rotation != o.Rotation || p.AlphaLayer != alphaLayer
                || (checkAcViewerAtlasIndex && p.AlphaLayer != o.AlphaIdx))
            {
                return $"overlay {i}: product {p} vs oracle {o} (alpha layer {alphaLayer}, texture layer {layer})";
            }
        }

        if (product.RoadCount != oracle.Roads.Count)
        {
            return $"road count {product.RoadCount} vs {oracle.Roads.Count}";
        }

        if (product.RoadCount > 0 && product.RoadTextureLayer != manifest.RoadLayer)
        {
            return $"road texture layer {product.RoadTextureLayer} vs {manifest.RoadLayer}";
        }

        for (var i = 0; i < product.RoadCount; i++)
        {
            var p = product.GetRoad(i);
            var o = oracle.Roads[i];
            var alphaLayer = manifest.RoadAlphaLayers[o.MapIndex].Layer;
            if (p.RCode != o.RCode || (int)p.Rotation != o.Rotation || p.AlphaLayer != alphaLayer
                || (checkAcViewerAtlasIndex && p.AlphaLayer != o.AlphaIdx))
            {
                return $"road {i}: product {p} vs oracle {o} (alpha layer {alphaLayer})";
            }
        }

        return null;
    }
}
