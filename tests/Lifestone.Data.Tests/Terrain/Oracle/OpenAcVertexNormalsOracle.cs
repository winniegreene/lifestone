// Ported from eriknihlen/OpenAC@1ee5a08581a7f4b0b22effdf911171a76cf4391c src/AcDream.Core/Terrain/LandblockMesh.cs
// (BuildRetailVertexNormals, AccumulateFaceNormal) and src/AcDream.Core/Terrain/TerrainBlending.cs (CalculateSplitDirection)
// (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors)
// with changes: test oracle only; reads a Lifestone LandblockTerrain and height table instead of a DatReaderWriter LandBlock;
// returns a flat array indexed x * 9 + y instead of a [9, 9] array; the split is a bool (true = SW-NE). The geometry,
// the loop order (cell y outer, cell x inner) and the float arithmetic are kept as in the source.

using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.Tests.Terrain.Oracle;

/// <summary>
/// OpenAC's retail-style landscape vertex normals, used to check <c>TerrainVertexLighting.ComputeNormals</c> and the
/// lighting reference. It has its own split formula and triangle lists, so it does not share <c>TerrainSplit</c> or the
/// product's triangles.
/// </summary>
internal static class OpenAcVertexNormalsOracle
{
    private const int HeightmapSide = 9;
    private const int CellsPerSide = 8;
    private const float CellSize = 24.0f;

    /// <summary>OpenAC <c>TerrainBlending.CalculateSplitDirection</c>: true when the cell is cut SW to NE.</summary>
    public static bool IsSWtoNE(uint landblockX, uint cellX, uint landblockY, uint cellY)
    {
        var x = (landblockX * 8) + cellX;
        var y = (landblockY * 8) + cellY;
        var dw = unchecked((x * y * 0x0CCAC033u) - (x * 0x421BE3BDu) + (y * 0x6C1AC587u) - 0x519B8F25u);
        return (dw & 0x80000000u) != 0;
    }

    /// <summary>OpenAC <c>LandblockMesh.BuildRetailVertexNormals</c>: 81 normals, index x * 9 + y.</summary>
    public static Vector3[] BuildRetailVertexNormals(LandblockTerrain terrain, IReadOnlyList<float> heightTable)
    {
        var heights = new float[HeightmapSide, HeightmapSide];
        for (var x = 0; x < HeightmapSide; x++)
        {
            for (var y = 0; y < HeightmapSide; y++)
            {
                heights[x, y] = heightTable[terrain.HeightIndex(x, y)];
            }
        }

        var normalSums = new Vector3[HeightmapSide, HeightmapSide];
        for (var cy = 0; cy < CellsPerSide; cy++)
        {
            for (var cx = 0; cx < CellsPerSide; cx++)
            {
                var posBL = new Vector3(cx * CellSize, cy * CellSize, heights[cx, cy]);
                var posBR = new Vector3((cx + 1) * CellSize, cy * CellSize, heights[cx + 1, cy]);
                var posTR = new Vector3((cx + 1) * CellSize, (cy + 1) * CellSize, heights[cx + 1, cy + 1]);
                var posTL = new Vector3(cx * CellSize, (cy + 1) * CellSize, heights[cx, cy + 1]);

                if (IsSWtoNE(terrain.Coord.X, (uint)cx, terrain.Coord.Y, (uint)cy))
                {
                    AccumulateFaceNormal(normalSums, posBL, cx, cy, posBR, cx + 1, cy, posTR, cx + 1, cy + 1);
                    AccumulateFaceNormal(normalSums, posBL, cx, cy, posTR, cx + 1, cy + 1, posTL, cx, cy + 1);
                }
                else
                {
                    AccumulateFaceNormal(normalSums, posBL, cx, cy, posBR, cx + 1, cy, posTL, cx, cy + 1);
                    AccumulateFaceNormal(normalSums, posBR, cx + 1, cy, posTR, cx + 1, cy + 1, posTL, cx, cy + 1);
                }
            }
        }

        var normals = new Vector3[HeightmapSide * HeightmapSide];
        for (var x = 0; x < HeightmapSide; x++)
        {
            for (var y = 0; y < HeightmapSide; y++)
            {
                var sum = normalSums[x, y];
                normals[(x * HeightmapSide) + y] = sum.LengthSquared() > 0f ? Vector3.Normalize(sum) : Vector3.UnitZ;
            }
        }

        return normals;
    }

    private static void AccumulateFaceNormal(
        Vector3[,] normalSums,
        Vector3 p0,
        int x0,
        int y0,
        Vector3 p1,
        int x1,
        int y1,
        Vector3 p2,
        int x2,
        int y2)
    {
        var cross = Vector3.Cross(p1 - p0, p2 - p0);
        if (cross.LengthSquared() <= 0f)
        {
            return;
        }

        var faceNormal = Vector3.Normalize(cross);
        normalSums[x0, y0] += faceNormal;
        normalSums[x1, y1] += faceNormal;
        normalSums[x2, y2] += faceNormal;
    }
}
