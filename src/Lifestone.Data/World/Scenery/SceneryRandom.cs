// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/Landblock.cs (get_land_scenes) and
// Source/ACE.Server/Physics/Common/ObjectDesc.cs (AGPL-3.0)
// with changes: one function per hash, explicit uint32 wraparound, and the exact retail 2^-32 scale (see UnitScale).

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// The retail pseudo-random hashes that drive procedural scenery. Every hash is a polynomial in the global vertex
/// coordinates evaluated in wrapping uint32 arithmetic, then scaled to [0, 1) by <see cref="UnitScale"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sources: retail <c>CLandBlock::get_land_scenes</c>, <c>ObjectDesc::Place</c>, <c>ObjectDesc::GetObjFrame</c> and
/// <c>ObjectDesc::ScaleObj</c> (acclient 11.4186 decompile, Mag-nus/acclient-AI-RE); ACE <c>Landblock.get_land_scenes</c>
/// and <c>ObjectDesc.Displace/RotateObj/ScaleObj</c>. The constants are cross-checked in docs/research/rendering.md §4
/// and docs/research/foundation-openac.md §3c.
/// </para>
/// <para>
/// <c>gx</c>/<c>gy</c> are global vertex coordinates (<c>landblockX * 8 + vertexX</c>, <c>landblockY * 8 + vertexY</c>,
/// vertex 0..8) and <c>k</c> is the index of the ObjectDesc inside its Scene.
/// </para>
/// </remarks>
public static class SceneryRandom
{
    /// <summary>
    /// 2^-32, the scale retail applies to every hash. Retail multiplies by the <b>float</b> constant 0x2F800000
    /// (2.3283064e-10f == exactly 2^-32): in the user's acclient.exe 11.6096 it sits at VA 0x7C6F10 and is referenced
    /// by 14 <c>fmul dword ptr</c> (D8 0D) instructions; the image holds no double encoding of 2.3283064e-10.
    /// ACE uses the double literal 2.3283064e-10, which is about 1.6e-8 (relative) smaller than 2^-32.
    /// </summary>
    public const double UnitScale = 1.0 / 4294967296.0;

    /// <summary>Salt for the X displacement (ObjectDesc::Place).</summary>
    public const uint DisplaceXSalt = 45773;

    /// <summary>Salt for the Y displacement (ObjectDesc::Place).</summary>
    public const uint DisplaceYSalt = 72719;

    /// <summary>Salt for the heading (ObjectDesc::GetObjFrame).</summary>
    public const uint RotationSalt = 63127;

    /// <summary>Salt for the scale (ObjectDesc::ScaleObj).</summary>
    public const uint ScaleSalt = 32593;

    /// <summary>Salt for the frequency test (CLandBlock::get_land_scenes: <c>23399 * m</c>, then <c>+ m</c> per object).</summary>
    public const uint FrequencySalt = 23399;

    /// <summary>Scene pick hash: <c>gy * (712977289 * gx + 1813693831) - 1109124029 * gx + 2139937281</c>.</summary>
    public static uint SceneHash(uint gx, uint gy)
    {
        unchecked
        {
            return (gy * ((712977289u * gx) + 1813693831u)) - (1109124029u * gx) + 2139937281u;
        }
    }

    /// <summary>
    /// Index of the scene picked from a SceneType with <paramref name="sceneCount"/> scenes
    /// (retail: 0 when the count is 1, else <c>floor(SceneHash * 2^-32 * count)</c>, wrapped to 0 if out of range).
    /// </summary>
    public static int PickSceneIndex(uint gx, uint gy, int sceneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sceneCount);
        if (sceneCount == 1)
        {
            return 0;
        }

        var index = Math.Floor((SceneHash(gx, gy) * UnitScale) * sceneCount);
        return index >= sceneCount ? 0 : (int)index;
    }

    /// <summary>Per-vertex multiplier <c>m = 1360117743 * gx * gy + 1888038839</c> shared by the per-object hashes.</summary>
    public static uint VertexMultiplier(uint gx, uint gy)
    {
        unchecked
        {
            return (1360117743u * gx * gy) + 1888038839u;
        }
    }

    /// <summary>Frequency hash of object <paramref name="k"/>: <c>-1109124029 * gx + 1813693831 * gy - (23399 + k) * m</c>.</summary>
    public static uint FrequencyHash(uint gx, uint gy, uint k)
    {
        unchecked
        {
            return (0u - (1109124029u * gx)) + (1813693831u * gy) - ((FrequencySalt + k) * VertexMultiplier(gx, gy));
        }
    }

    /// <summary>Frequency noise in [0, 1); the object is a candidate when this is below ObjectDesc.Freq.</summary>
    public static double FrequencyNoise(uint gx, uint gy, uint k) => FrequencyHash(gx, gy, k) * UnitScale;

    /// <summary>
    /// Per-object hash used for displacement, heading and scale:
    /// <c>1813693831 * gy - (k + salt) * (1360117743 * gy * gx + 1888038839) - 1109124029 * gx</c>.
    /// </summary>
    public static uint ObjectHash(uint gx, uint gy, uint k, uint salt)
    {
        unchecked
        {
            return (1813693831u * gy) - ((k + salt) * VertexMultiplier(gx, gy)) - (1109124029u * gx);
        }
    }

    /// <summary><see cref="ObjectHash"/> scaled to [0, 1).</summary>
    public static double ObjectNoise(uint gx, uint gy, uint k, uint salt) => ObjectHash(gx, gy, k, salt) * UnitScale;

    /// <summary>Quadrant hash: <c>1813693831 * gy - gx * (1870387557 * gy + 1109124029) - 402451965</c>.</summary>
    public static uint QuadrantHash(uint gx, uint gy)
    {
        unchecked
        {
            return (1813693831u * gy) - (gx * ((1870387557u * gy) + 1109124029u)) - 402451965u;
        }
    }

    /// <summary>
    /// Quadrant noise in [0, 1), computed in double as retail does (OpenAC's float version is a documented deviation).
    /// </summary>
    public static double QuadrantNoise(uint gx, uint gy) => QuadrantHash(gx, gy) * UnitScale;
}
