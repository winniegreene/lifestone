using DatReaderWriter.DBObjs;

namespace Lifestone.Data.Terrain;

/// <summary>One of the Region's 32 terrain types (name + map colour).</summary>
/// <param name="Index">Terrain type index 0-31 as stored in bits 2-6 of a terrain word.</param>
/// <param name="Name">Retail name, e.g. "LushGrass".</param>
/// <param name="ColorArgb">Map colour as 0xAARRGGBB.</param>
public readonly record struct TerrainTypeInfo(int Index, string Name, uint ColorArgb);

/// <summary>Engine-agnostic subset of Region 0x13000000 needed to build terrain.</summary>
public sealed class TerrainRegion
{
    public TerrainRegion(float[] landHeightTable, IReadOnlyList<TerrainTypeInfo> terrainTypes, float squareLength = TerrainConstants.CellSize)
    {
        ArgumentNullException.ThrowIfNull(landHeightTable);
        ArgumentNullException.ThrowIfNull(terrainTypes);
        if (landHeightTable.Length != 256)
        {
            throw new ArgumentException("LandHeightTable must have 256 entries.", nameof(landHeightTable));
        }

        LandHeightTable = landHeightTable;
        TerrainTypes = terrainTypes;
        SquareLength = squareLength;
    }

    /// <summary>Height in meters for each of the 256 height indices stored in LandBlocks.</summary>
    public IReadOnlyList<float> LandHeightTable { get; }

    /// <summary>The Region's terrain types, indexed by terrain type.</summary>
    public IReadOnlyList<TerrainTypeInfo> TerrainTypes { get; }

    /// <summary>Cell edge length from LandDefs (24 in retail Dereth).</summary>
    public float SquareLength { get; }

    public static TerrainRegion FromRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var landDefs = region.LandDefs ?? throw new InvalidDataException("Region has no LandDefs.");
        var heights = landDefs.LandHeightTable?.ToArray() ?? [];

        var types = new List<TerrainTypeInfo>();
        var terrainTypes = region.TerrainInfo?.TerrainTypes;
        if (terrainTypes is not null)
        {
            for (var i = 0; i < terrainTypes.Count; i++)
            {
                var t = terrainTypes[i];
                var c = t.TerrainColor;
                var argb = c is null ? 0xFF808080u : ((uint)c.Alpha << 24) | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
                types.Add(new TerrainTypeInfo(i, t.TerrainName?.Value ?? string.Empty, argb));
            }
        }

        return new TerrainRegion(heights, types, landDefs.SquareLength);
    }

    /// <summary>Map colour of a terrain type, or mid grey if the type is unknown.</summary>
    public uint ColorOf(int terrainType) =>
        (uint)terrainType < (uint)TerrainTypes.Count ? TerrainTypes[terrainType].ColorArgb : 0xFF808080u;
}
