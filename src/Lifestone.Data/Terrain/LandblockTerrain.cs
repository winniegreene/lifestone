using DatReaderWriter.DBObjs;

namespace Lifestone.Data.Terrain;

/// <summary>
/// Engine-agnostic copy of a LandBlock record: 81 terrain words and 81 height indices on a 9 x 9 grid,
/// indexed <c>x * 9 + y</c> (x = east, y = north, index 0 = south-west corner).
/// </summary>
public sealed class LandblockTerrain
{
    public LandblockTerrain(LandblockCoord coord, ushort[] terrainWords, byte[] heightIndices)
    {
        ArgumentNullException.ThrowIfNull(terrainWords);
        ArgumentNullException.ThrowIfNull(heightIndices);
        if (terrainWords.Length != TerrainConstants.VertexCount)
        {
            throw new ArgumentException($"Expected {TerrainConstants.VertexCount} terrain words.", nameof(terrainWords));
        }

        if (heightIndices.Length != TerrainConstants.VertexCount)
        {
            throw new ArgumentException($"Expected {TerrainConstants.VertexCount} height indices.", nameof(heightIndices));
        }

        Coord = coord;
        TerrainWords = terrainWords;
        HeightIndices = heightIndices;
    }

    public LandblockCoord Coord { get; }

    /// <summary>Raw terrain words: bits 0-1 road, bits 2-6 terrain type, bits 11-15 scene slot.</summary>
    public IReadOnlyList<ushort> TerrainWords { get; }

    /// <summary>Indices into Region.LandDefs.LandHeightTable.</summary>
    public IReadOnlyList<byte> HeightIndices { get; }

    public static LandblockTerrain FromLandBlock(LandblockCoord coord, LandBlock landBlock)
    {
        ArgumentNullException.ThrowIfNull(landBlock);
        var words = new ushort[TerrainConstants.VertexCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = landBlock.Terrain[i]; // TerrainInfo converts implicitly to its raw ushort
        }

        return new LandblockTerrain(coord, words, (byte[])landBlock.Height.Clone());
    }

    public byte HeightIndex(int x, int y) => HeightIndices[TerrainConstants.VertexIndex(x, y)];

    /// <summary>Terrain type 0-31 (index into Region TerrainTypes).</summary>
    public int TerrainType(int x, int y) => (TerrainWords[TerrainConstants.VertexIndex(x, y)] >> 2) & 0x1F;

    /// <summary>Road bits 0-3 (non-zero means road at this vertex).</summary>
    public int Road(int x, int y) => TerrainWords[TerrainConstants.VertexIndex(x, y)] & 0x3;

    /// <summary>Scene slot 0-31 (procedural scenery selector).</summary>
    public int SceneSlot(int x, int y) => TerrainWords[TerrainConstants.VertexIndex(x, y)] >> 11;
}
