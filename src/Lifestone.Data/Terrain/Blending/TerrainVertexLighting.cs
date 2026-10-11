using System.Numerics;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// The landscape light state retail keeps in <c>LScape::sunlight</c>, <c>sunlight_color</c>, <c>ambient_level</c> and
/// <c>ambient_color</c> (written by <c>LScape::set_landscape_lighting</c>, acclient.c:307003, from
/// <c>SkyDesc::GetLighting</c>; the sky track computes these values).
/// </summary>
/// <remarks>
/// <see cref="AmbientLevel"/> is <c>LScape::ambient_level</c>, <b>not</b> the raw keyframe AmbBright: retail floors it at
/// <c>LScape::min_ambient</c> = <c>LSCAPE_LIGHT_MINIMUM</c> = 0.2 (<see cref="TerrainVertexLighting.MinAmbientLevel"/>)
/// before calling <c>set_landscape_lighting</c>. Use <see cref="FromSkyLighting"/> to build the value from sky output.
/// </remarks>
/// <param name="Sunlight">
/// Sun vector in AC world axes (X east, Y north, Z up), pointing towards the sun. It is <b>not</b> normalised: its length
/// is the keyframe's DirBright, and calc_lighting uses it as is.
/// </param>
/// <param name="SunlightColor">Sun colour as 0xAARRGGBB (alpha ignored).</param>
/// <param name="AmbientLevel">
/// Ambient brightness as retail stores it: <c>max(AmbBright, 0.2)</c> (see the remarks and <see cref="FromSkyLighting"/>).
/// </param>
/// <param name="AmbientColor">Ambient colour as 0xAARRGGBB (alpha ignored).</param>
public readonly record struct TerrainLightingEnvironment(Vector3 Sunlight, uint SunlightColor, float AmbientLevel, uint AmbientColor)
{
    /// <summary>
    /// Builds the landscape light state from the sky's lighting output (<c>CRegionDesc::GetLighting</c> → <c>SkyDesc::GetLighting</c>),
    /// applying retail's ambient floor: <c>if (ambBright &lt; LScape::min_ambient) ambBright = LScape::min_ambient</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The floor is applied in <c>LScape::UseTime</c> (acclient.c:307261) before it calls <c>set_landscape_lighting</c>, and
    /// on the always-daylight path inside <c>LScape::set_landscape_lighting</c> (acclient.c:307024).
    /// <c>LScape::min_ambient</c> has one writer, which sets it to <c>LSCAPE_LIGHT_MINIMUM</c> = 0.2 (acclient.c:783184).
    /// </para>
    /// <para>
    /// Not modelled here: after the floor, <c>UseTime</c> can replace or blend the ambient level and colour towards a
    /// server-sent override (<c>LScape::m_override_*</c>, set by <c>CPlayerSystem::Handle_Admin__Environs</c>,
    /// acclient.c:396298). That belongs to whoever owns the sky and environs state.
    /// </para>
    /// </remarks>
    /// <param name="sunlight">Unnormalised sun vector in AC axes (length = DirBright).</param>
    /// <param name="sunlightColor">Sun colour as 0xAARRGGBB.</param>
    /// <param name="ambientBrightness">The keyframe-interpolated AmbBright.</param>
    /// <param name="ambientColor">Ambient colour as 0xAARRGGBB.</param>
    public static TerrainLightingEnvironment FromSkyLighting(Vector3 sunlight, uint sunlightColor, float ambientBrightness, uint ambientColor) =>
        new(
            sunlight,
            sunlightColor,
            ambientBrightness < TerrainVertexLighting.MinAmbientLevel ? TerrainVertexLighting.MinAmbientLevel : ambientBrightness,
            ambientColor);
}

/// <summary>Retail per-vertex landscape light (<c>CLandBlockStruct::vertex_lighting</c>), each channel in [0, 1].</summary>
public readonly record struct TerrainVertexLight(float R, float G, float B)
{
    /// <summary>Red as the D3D vertex-colour byte: <c>(uint8)(uint64)(r * 255.0)</c> (<c>ACRender::landPolyDraw</c>, acclient.c:719994).</summary>
    public byte R8 => ToByte(R);

    /// <summary>Green as the D3D vertex-colour byte.</summary>
    public byte G8 => ToByte(G);

    /// <summary>Blue as the D3D vertex-colour byte.</summary>
    public byte B8 => ToByte(B);

    /// <summary>The D3DCOLOR retail submits for the vertex: 0xFF alpha, truncated channels (0xAARRGGBB).</summary>
    public uint ToArgb() => 0xFF000000u | ((uint)R8 << 16) | ((uint)G8 << 8) | B8;

    private static byte ToByte(float channel)
    {
        var scaled = channel * 255.0;
        return scaled <= 0.0 ? (byte)0 : (byte)(ulong)scaled;
    }
}

/// <summary>
/// Retail per-vertex terrain lighting, <c>CLandBlockStruct::calc_lighting</c> (CLandBlockStruct.c, acclient.c:353713).
/// </summary>
/// <remarks>
/// <para>Re-expressed from the 11.4186 decompile:</para>
/// <list type="number">
/// <item>Vertex normal = normalised sum of the plane normals (<c>CPolygon::make_plane</c>, acclient.c:359628: unit
/// <c>cross(v1 - v0, v2 - v0)</c>) of the block's own 128 triangles that use the vertex, accumulated in polygon order
/// (cell x outer, cell y inner, first triangle then second; triangles as in <c>ConstructPolygons</c>, acclient.c:354001).
/// Neighbouring blocks are not included, so lighting is discontinuous across block edges in retail too.</item>
/// <item>A sum shorter than 0.00019999999 (the float 2e-4) becomes (0, 0, 1).</item>
/// <item><c>d = max(0, dot(sunlight, n))</c> with the unnormalised sun vector.</item>
/// <item>Per channel: <c>min(1, sunColor/255 * d + ambientColor/255 * ambientLevel)</c> (1/255 is the float 0.0039215689;
/// <c>ambientLevel</c> is <c>LScape::ambient_level</c>, already floored at <see cref="MinAmbientLevel"/>).
/// There is no lower clamp and no per-terrain brightness: the TerrainTex vertex bright/saturation/hue fields are unused.</item>
/// </list>
/// <para>
/// The result multiplies the merged texture (fixed-function MODULATE with lighting off; <c>ACRender::landPolyDraw</c> sends
/// the channels as truncated bytes, see <see cref="TerrainVertexLight.ToArgb"/>). Retail re-runs this on every loaded
/// block each <c>LightTickSize</c> (<c>LScape::set_landscape_lighting</c>). Arithmetic is done in double and stored as
/// float; retail's x87 extended precision can differ in the last float bit.
/// </para>
/// </remarks>
public static class TerrainVertexLighting
{
    /// <summary>Minimum normal-sum length before falling back to straight up (float 2e-4 in calc_lighting).</summary>
    public const float MinNormalLength = 0.00019999999f;

    /// <summary>The float constant 1/255 calc_lighting uses to convert colour bytes.</summary>
    public const float ColorByteScale = 0.0039215689f;

    /// <summary>
    /// Retail's floor for the landscape ambient level: <c>LScape::min_ambient</c> = <c>LSCAPE_LIGHT_MINIMUM</c> = 0.2
    /// (acclient.c:783184), applied in <c>LScape::UseTime</c> (acclient.c:307261). See
    /// <see cref="TerrainLightingEnvironment.FromSkyLighting"/>.
    /// </summary>
    public const float MinAmbientLevel = 0.2f;

    /// <summary>Per-vertex normals of a landblock (81, index x*9+y), exactly as calc_lighting builds them.</summary>
    public static Vector3[] ComputeNormals(LandblockTerrain terrain, IReadOnlyList<float> landHeightTable, float squareLength = TerrainConstants.CellSize)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(landHeightTable);
        if (landHeightTable.Count != 256)
        {
            throw new ArgumentException("LandHeightTable must have 256 entries.", nameof(landHeightTable));
        }

        const int side = TerrainConstants.VerticesPerSide;
        var positions = new Vector3[TerrainConstants.VertexCount];
        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
            {
                // CLandBlockStruct::ConstructVertices (acclient.c:354621): (ix * poly_size, iy * poly_size, Land_Height_Table[h]).
                positions[TerrainConstants.VertexIndex(x, y)] =
                    new Vector3(x * squareLength, y * squareLength, landHeightTable[terrain.HeightIndex(x, y)]);
            }
        }

        var sums = new Vector3[TerrainConstants.VertexCount];
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var sw = TerrainConstants.VertexIndex(cx, cy);
                var se = TerrainConstants.VertexIndex(cx + 1, cy);
                var ne = TerrainConstants.VertexIndex(cx + 1, cy + 1);
                var nw = TerrainConstants.VertexIndex(cx, cy + 1);
                if (TerrainSplit.IsSWtoNE(terrain.Coord, cx, cy))
                {
                    Accumulate(sums, positions, sw, se, ne);
                    Accumulate(sums, positions, sw, ne, nw);
                }
                else
                {
                    Accumulate(sums, positions, sw, se, nw);
                    Accumulate(sums, positions, ne, nw, se);
                }
            }
        }

        var normals = new Vector3[TerrainConstants.VertexCount];
        for (var i = 0; i < sums.Length; i++)
        {
            var s = sums[i];
            var length = Math.Sqrt(((double)s.X * s.X) + ((double)s.Y * s.Y) + ((double)s.Z * s.Z));
            if (length < MinNormalLength)
            {
                normals[i] = Vector3.UnitZ;
            }
            else
            {
                var inv = 1.0 / length;
                normals[i] = new Vector3((float)(inv * s.X), (float)(inv * s.Y), (float)(inv * s.Z));
            }
        }

        return normals;
    }

    /// <summary>Lights 81 (or any number of) vertex normals with the landscape light state.</summary>
    public static TerrainVertexLight[] Light(ReadOnlySpan<Vector3> normals, in TerrainLightingEnvironment environment)
    {
        var lights = new TerrainVertexLight[normals.Length];
        for (var i = 0; i < normals.Length; i++)
        {
            lights[i] = LightVertex(normals[i], environment);
        }

        return lights;
    }

    /// <summary>Normals plus lighting for a landblock (81 values, index x*9+y).</summary>
    public static TerrainVertexLight[] Compute(LandblockTerrain terrain, TerrainRegion region, in TerrainLightingEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Light(ComputeNormals(terrain, region.LandHeightTable, region.SquareLength), environment);
    }

    /// <summary>calc_lighting's per-vertex formula for one unit normal.</summary>
    public static TerrainVertexLight LightVertex(Vector3 normal, in TerrainLightingEnvironment environment)
    {
        var sun = environment.Sunlight;
        var d = ((double)sun.Z * normal.Z) + ((double)sun.Y * normal.Y) + ((double)sun.X * normal.X);
        if (d < 0.0)
        {
            d = 0.0;
        }

        double level = environment.AmbientLevel;
        return new TerrainVertexLight(
            Channel(environment.SunlightColor >> 16, environment.AmbientColor >> 16, d, level),
            Channel(environment.SunlightColor >> 8, environment.AmbientColor >> 8, d, level),
            Channel(environment.SunlightColor, environment.AmbientColor, d, level));
    }

    private static float Channel(uint sunByte, uint ambientByte, double d, double level)
    {
        var sun = (float)((sunByte & 0xFF) * (double)ColorByteScale);
        var ambient = (ambientByte & 0xFF) * (double)ColorByteScale;
        var value = (sun * d) + (ambient * level);
        return value > 1.0 ? 1f : (float)value;
    }

    private static void Accumulate(Vector3[] sums, Vector3[] positions, int i0, int i1, int i2)
    {
        var n = PlaneNormal(positions[i0], positions[i1], positions[i2]);
        sums[i0] += n;
        sums[i1] += n;
        sums[i2] += n;
    }

    /// <summary><c>CPolygon::make_plane</c> for a triangle: unit <c>cross(v1 - v0, v2 - v0)</c>.</summary>
    internal static Vector3 PlaneNormal(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        // e1 = v1 - v0 (stored as float in make_plane), e2 = v2 - v0.
        var e1 = v1 - v0;
        double e2x = (double)v2.X - v0.X, e2y = (double)v2.Y - v0.Y, e2z = (double)v2.Z - v0.Z;
        var nx = (float)((e1.Y * e2z) - (e1.Z * e2y));
        var ny = (float)((e1.Z * e2x) - (e2z * e1.X));
        var nz = (float)((e2y * e1.X) - (e1.Y * e2x));
        var inv = 1.0 / Math.Sqrt(((double)nz * nz) + ((double)ny * ny) + ((double)nx * nx));
        var invF = (float)inv;
        return new Vector3((float)(inv * nx), (float)((double)invF * ny), (float)((double)invF * nz));
    }
}
