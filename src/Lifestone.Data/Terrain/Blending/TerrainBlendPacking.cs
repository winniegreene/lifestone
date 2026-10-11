namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// Suggested per-vertex packing of a <see cref="TerrainCellBlend"/> into three RGBA8 vertex attributes (Godot
/// <c>ARRAY_CUSTOM0..2</c> with <c>ARRAY_CUSTOM_RGBA8_UNORM</c>), repeated on every vertex of the cell. The shader reads a
/// byte as <c>int(round(CUSTOMn.c * 255.0))</c>; <see cref="TerrainCellBlend.None"/> (255) marks an unused slot.
/// See docs/terrain-shader.md.
/// </summary>
/// <remarks>
/// <code>
/// CUSTOM0 = (base layer, overlay0 layer, overlay1 layer, overlay2 layer)
/// CUSTOM1 = (overlay0 alpha, overlay1 alpha, overlay2 alpha, road0 alpha)
/// CUSTOM2 = (road1 alpha,
///            rot(overlay0) | rot(overlay1) &lt;&lt; 2 | rot(overlay2) &lt;&lt; 4 | rot(road0) &lt;&lt; 6,
///            rot(road1) | flags &lt;&lt; 2   (flag 1 = single texture / wrap addressing, flag 2 = all road),
///            road texture layer)
/// </code>
/// </remarks>
public static class TerrainBlendPacking
{
    /// <summary>Bytes per vertex (three RGBA8 attributes).</summary>
    public const int BytesPerVertex = 12;

    /// <summary>Flag bit (in byte 10 after the 2-bit road1 rotation) for <see cref="TerrainCellBlend.IsSingleTexture"/>.</summary>
    public const int SingleTextureFlag = 1;

    /// <summary>Flag bit for <see cref="TerrainCellBlend.IsAllRoad"/>.</summary>
    public const int AllRoadFlag = 2;

    /// <summary>Writes the 12 packed bytes of a cell (CUSTOM0 RGBA, CUSTOM1 RGBA, CUSTOM2 RGBA).</summary>
    public static void Pack(in TerrainCellBlend blend, Span<byte> destination)
    {
        if (destination.Length < BytesPerVertex)
        {
            throw new ArgumentException($"Need {BytesPerVertex} bytes.", nameof(destination));
        }

        const byte none = TerrainCellBlend.None;
        var o0 = blend.OverlayCount > 0;
        var o1 = blend.OverlayCount > 1;
        var o2 = blend.OverlayCount > 2;
        var r0 = blend.RoadCount > 0;
        var r1 = blend.RoadCount > 1;

        destination[0] = blend.BaseTextureLayer;
        destination[1] = o0 ? blend.Overlay0.TextureLayer : none;
        destination[2] = o1 ? blend.Overlay1.TextureLayer : none;
        destination[3] = o2 ? blend.Overlay2.TextureLayer : none;

        destination[4] = o0 ? blend.Overlay0.AlphaLayer : none;
        destination[5] = o1 ? blend.Overlay1.AlphaLayer : none;
        destination[6] = o2 ? blend.Overlay2.AlphaLayer : none;
        destination[7] = r0 ? blend.Road0.AlphaLayer : none;

        destination[8] = r1 ? blend.Road1.AlphaLayer : none;
        destination[9] = (byte)(Rot(o0, blend.Overlay0.Rotation)
            | (Rot(o1, blend.Overlay1.Rotation) << 2)
            | (Rot(o2, blend.Overlay2.Rotation) << 4)
            | (Rot(r0, blend.Road0.Rotation) << 6));
        var flags = (blend.IsSingleTexture ? SingleTextureFlag : 0) | (blend.IsAllRoad ? AllRoadFlag : 0);
        destination[10] = (byte)(Rot(r1, blend.Road1.Rotation) | (flags << 2));
        destination[11] = r0 ? blend.RoadTextureLayer : none;
    }

    /// <summary>The packed bytes of a cell as a new array.</summary>
    public static byte[] Pack(in TerrainCellBlend blend)
    {
        var bytes = new byte[BytesPerVertex];
        Pack(blend, bytes);
        return bytes;
    }

    /// <summary>
    /// Decodes packed bytes the way the shader does, into layer indices (-1 = unused) and rotations; terrain types,
    /// coverage codes and the palette code are not carried.
    /// </summary>
    public static TerrainPackedCell Unpack(ReadOnlySpan<byte> packed)
    {
        if (packed.Length < BytesPerVertex)
        {
            throw new ArgumentException($"Need {BytesPerVertex} bytes.", nameof(packed));
        }

        static int L(byte b) => b == TerrainCellBlend.None ? -1 : b;
        return new TerrainPackedCell(
            L(packed[0]),
            [L(packed[1]), L(packed[2]), L(packed[3])],
            [L(packed[4]), L(packed[5]), L(packed[6])],
            [(TerrainRotation)(packed[9] & 3), (TerrainRotation)((packed[9] >> 2) & 3), (TerrainRotation)((packed[9] >> 4) & 3)],
            L(packed[11]),
            [L(packed[7]), L(packed[8])],
            [(TerrainRotation)((packed[9] >> 6) & 3), (TerrainRotation)(packed[10] & 3)],
            ((packed[10] >> 2) & SingleTextureFlag) != 0,
            ((packed[10] >> 2) & AllRoadFlag) != 0);
    }

    private static int Rot(bool used, TerrainRotation rotation) => used ? (int)rotation & 3 : 0;
}

/// <summary>A cell decoded from <see cref="TerrainBlendPacking"/> bytes (-1 = unused).</summary>
public sealed record TerrainPackedCell(
    int BaseLayer,
    int[] OverlayLayers,
    int[] OverlayAlphas,
    TerrainRotation[] OverlayRotations,
    int RoadLayer,
    int[] RoadAlphas,
    TerrainRotation[] RoadRotations,
    bool IsSingleTexture,
    bool IsAllRoad);
