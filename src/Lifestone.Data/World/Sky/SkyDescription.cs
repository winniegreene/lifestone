using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace Lifestone.Data.World.Sky;

/// <summary>Bits of <see cref="SkyObjectDesc.Properties"/> (retail <c>CelestialPosition::properties</c>).</summary>
/// <remarks>
/// Meanings from <c>GameSky::MakeObject</c>, <c>GameSky::Draw</c>, <c>GameSky::UpdatePosition</c> and
/// <c>GameSky::UseTime</c> (lscape/Sky.c, acclient.c:308366-308904, 11.4186). The user's Region uses 0, 2, 4, 5 and 0xD.
/// </remarks>
[Flags]
public enum SkyObjectProperties : uint
{
    None = 0,

    /// <summary>Lives in the "after" sky cell, drawn after the landscape and only when weather is enabled.
    /// Retail <c>GameSky::Draw(0)</c> skips these; <c>GameSky::Draw(1)</c> draws the after cell.</summary>
    AfterPass = 1,

    /// <summary>Not drawn while the server environment-fog override (<c>LScape::m_override_enabled</c>) is active
    /// and fog is on. Clouds carry this bit.</summary>
    HiddenUnderFogOverride = 2,

    /// <summary>Weather (rain): only created when <c>LScape::weather_enabled</c> (default true), pinned to the
    /// player's position each <c>GameSky::UpdatePosition</c>.</summary>
    Weather = 4,

    /// <summary>With <see cref="Weather"/>: keep the player's z. Without it a weather object's z is forced to
    /// -120 m (<c>GameSky::UpdatePosition</c>, acclient.c:308415).</summary>
    WeatherKeepsPlayerZ = 8,
}

/// <summary>Region SkyDesc (<c>SkyDesc::UnPack</c>, CSkyDesc.c acclient.c:302821).</summary>
public sealed class SkyDescription
{
    /// <summary>Retail <c>LScape::min_ambient</c> = <c>LSCAPE_LIGHT_MINIMUM</c> = 0.2f (static initialiser at
    /// acclient.c:783180, constant at acclient.c:40344). <c>LScape::UseTime</c> clamps the ambient level up to it.</summary>
    public const float MinAmbientLevel = 0.2f;

    public SkyDescription(double tickSize, double lightTickSize, IReadOnlyList<SkyDayGroup> dayGroups)
    {
        ArgumentNullException.ThrowIfNull(dayGroups);
        if (dayGroups.Count == 0)
        {
            // Retail would dereference day_groups.m_data[0] on an empty array (SkyDesc::GetSky).
            throw new InvalidDataException("SkyDesc has no day groups; retail indexes day group 0 unconditionally.");
        }

        TickSize = tickSize;
        LightTickSize = lightTickSize;
        DayGroups = dayGroups;
    }

    /// <summary>Seconds between retail fog updates (<c>LScape::UseTime</c> re-reads fog when
    /// <c>Timer::cur_time &gt;= next_tick</c>, then <c>next_tick = cur_time + tick_size</c>). 0.8 s in the user's dat.</summary>
    public double TickSize { get; }

    /// <summary>Seconds between retail light updates (<c>LScape::UseTime</c>: when <c>cur_time &gt; next_light_tick</c>,
    /// re-light and set <c>next_light_tick = cur_time + light_tick_size</c>). 15 s in the user's dat.</summary>
    public double LightTickSize { get; }

    public IReadOnlyList<SkyDayGroup> DayGroups { get; }

    /// <summary>Parses <see cref="Region.SkyInfo"/>. Throws when the Region has no sky.</summary>
    public static SkyDescription FromRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (!region.PartsMask.HasFlag(PartsMask.HasSkyInfo) || region.SkyInfo is null)
        {
            throw new InvalidDataException($"Region 0x{region.Id:X8} has no SkyDesc (PartsMask {region.PartsMask}).");
        }

        var sky = region.SkyInfo;
        var groups = new SkyDayGroup[sky.DayGroups.Count];
        for (var g = 0; g < groups.Length; g++)
        {
            groups[g] = SkyDayGroup.FromDat(g, sky.DayGroups[g]);
        }

        return new SkyDescription(sky.TickSize, sky.LightTickSize, groups);
    }

    /// <summary>
    /// Retail day-group choice for a calendar day (<c>SkyDesc::CalcPresentDayGroup</c>, CSkyDesc.c acclient.c:301663;
    /// identical in 11.6096 at acclient.c:295047):
    /// <code>
    /// idx = floor((double)(uint32)(1782775218 * (day + daysPerYear * year) - 1967253934) * 2^-32 * numGroups)
    /// if (idx >= numGroups) idx = 0
    /// </code>
    /// with <c>day</c> = <c>GameTime::current_day</c> (0-based day of year), <c>year</c> = <c>current_year</c> (absolute
    /// P.Y.) and int32 wrapping arithmetic. The literal printed as 2.3283064e-10 is a float constant, i.e. exactly
    /// 2^-32; the FPU runs at 53-bit precision (<c>ClientMain::ConfigureFPU</c>, device created with
    /// D3DCREATE_FPU_PRESERVE), so the product is exact and equals <c>(hash * numGroups) &gt;&gt; 32</c>.
    /// <see cref="SkyDayGroup.ChanceOfOccur"/> is not used: no retail function reads <c>chance_of_occur</c> except
    /// Pack/UnPack.
    /// </summary>
    public static int CalcPresentDayGroup(int currentDay, int daysPerYear, int currentYear, int numGroups)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numGroups);
        var index = ((ulong)DayGroupHash(currentDay, daysPerYear, currentYear) * (ulong)numGroups) >> 32;
        return index < (ulong)numGroups ? (int)index : 0;
    }

    /// <summary>The uint32 hash inside <see cref="CalcPresentDayGroup"/>.</summary>
    public static uint DayGroupHash(int currentDay, int daysPerYear, int currentYear) =>
        unchecked((uint)((1782775218 * (currentDay + (daysPerYear * currentYear))) - 1967253934));
}

/// <summary>One weather pattern for a whole Dereth day (<c>DayGroup::UnPack</c>, acclient.c:302641).</summary>
public sealed class SkyDayGroup
{
    public SkyDayGroup(int index, float chanceOfOccur, string name, IReadOnlyList<SkyObjectDesc> skyObjects, IReadOnlyList<SkyKeyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(skyObjects);
        ArgumentNullException.ThrowIfNull(keyframes);
        for (var k = 0; k < keyframes.Count; k++)
        {
            foreach (var replacement in keyframes[k].Replacements)
            {
                // DayGroup::UnPack resolves object_index to sky_objects.m_data[object_index] with no bounds check;
                // an out-of-range index would read garbage in retail, so refuse the data instead of guessing.
                if (replacement.ObjectIndex >= (uint)skyObjects.Count)
                {
                    throw new InvalidDataException(
                        $"Day group {index} keyframe {k}: SkyObjectReplace.ObjectIndex {replacement.ObjectIndex} >= {skyObjects.Count} sky objects.");
                }
            }

            if (k > 0 && keyframes[k].Begin < keyframes[k - 1].Begin)
            {
                // DayGroup::GetTimeOfDay is a linear scan that assumes ascending Begin; it never sorts.
                throw new InvalidDataException($"Day group {index}: keyframe {k} Begin {keyframes[k].Begin} < previous {keyframes[k - 1].Begin}.");
            }
        }

        Index = index;
        ChanceOfOccur = chanceOfOccur;
        Name = name ?? string.Empty;
        SkyObjects = skyObjects;
        Keyframes = keyframes;
    }

    /// <summary>Position in <see cref="SkyDescription.DayGroups"/>.</summary>
    public int Index { get; }

    /// <summary>Parsed but unused by retail (see <see cref="SkyDescription.CalcPresentDayGroup(int,int,int,int)"/>).</summary>
    public float ChanceOfOccur { get; }

    /// <summary>"Sunny", "Rainy", "Clear" or "Cloudy" in the user's dat.</summary>
    public string Name { get; }

    public IReadOnlyList<SkyObjectDesc> SkyObjects { get; }

    /// <summary>SkyTimeOfDay keyframes in dat order (ascending <see cref="SkyKeyframe.Begin"/>).</summary>
    public IReadOnlyList<SkyKeyframe> Keyframes { get; }

    public static SkyDayGroup FromDat(int index, DayGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var objects = new SkyObjectDesc[group.SkyObjects.Count];
        for (var i = 0; i < objects.Length; i++)
        {
            objects[i] = SkyObjectDesc.FromDat(i, group.SkyObjects[i]);
        }

        var keyframes = new SkyKeyframe[group.SkyTime.Count];
        for (var k = 0; k < keyframes.Length; k++)
        {
            keyframes[k] = SkyKeyframe.FromDat(group.SkyTime[k]);
        }

        return new SkyDayGroup(index, group.ChanceOfOccur, group.DayName?.Value ?? string.Empty, objects, keyframes);
    }

    public override string ToString() => $"DayGroup[{Index}] {Name} ({SkyObjects.Count} objects, {Keyframes.Count} keyframes)";
}

/// <summary>A celestial object: dome, stars, moons, clouds, sun, rain (<c>SkyObject::UnPack</c>, acclient.c:301788).</summary>
/// <param name="Index">Position in the day group's object list (what <see cref="SkyObjectReplacement.ObjectIndex"/> refers to).</param>
/// <param name="BeginTime">Day fraction the object appears (exclusive); equal to <paramref name="EndTime"/> means always visible.</param>
/// <param name="EndTime">Day fraction the object disappears (exclusive). Windows never wrap past midnight.</param>
/// <param name="BeginAngle">Arc angle in degrees at <paramref name="BeginTime"/>.</param>
/// <param name="EndAngle">Arc angle in degrees at <paramref name="EndTime"/>.</param>
/// <param name="TexVelocity">UV scroll in texture units per second (<c>CPhysicsObj::SetTextureVelocity</c>).</param>
/// <param name="DefaultGfxObjId">GfxObj (0x01) or Setup (0x02) drawn by default; 0 = nothing.</param>
/// <param name="DefaultPesObjId">PhysicsScript id. Retail copies it into <c>CelestialPosition::pes_id</c> but never plays it
/// (docs/research mag 06-rendering.md §9).</param>
/// <param name="Properties">Draw flags.</param>
public sealed record SkyObjectDesc(
    int Index,
    float BeginTime,
    float EndTime,
    float BeginAngle,
    float EndAngle,
    Vector2 TexVelocity,
    uint DefaultGfxObjId,
    uint DefaultPesObjId,
    SkyObjectProperties Properties)
{
    public static SkyObjectDesc FromDat(int index, SkyObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new SkyObjectDesc(
            index,
            o.BeginTime,
            o.EndTime,
            o.BeginAngle,
            o.EndAngle,
            new Vector2(o.TexVelocityX, o.TexVelocityY),
            o.DefaultGfxObjectId?.DataId ?? 0u,
            o.DefaultPesObjectId?.DataId ?? 0u,
            (SkyObjectProperties)o.Properties);
    }

    /// <summary>Retail visibility window test from <c>SkyDesc::GetSky</c>: <c>Begin == End || (Begin &lt; t &amp;&amp; t &lt; End)</c>
    /// (strict, no midnight wrap). A keyframe replacement with a non-zero GfxObj can still make an object drawn
    /// outside this window; see <see cref="SkyEvaluator.GetSky"/>.</summary>
    public bool IsInWindow(float timeOfDay) =>
        BeginTime == EndTime || (timeOfDay > BeginTime && timeOfDay < EndTime);
}

/// <summary>One SkyTimeOfDay keyframe (<c>SkyTimeOfDay::UnPack</c>, acclient.c:302256).</summary>
/// <param name="Begin">Day fraction where this keyframe starts.</param>
/// <param name="DirBright">Sun brightness: the length of the retail sun vector.</param>
/// <param name="DirHeading">Sun heading in degrees (0 = north/+Y, 90 = east/+X).</param>
/// <param name="DirPitch">Sun elevation in degrees above the horizon.</param>
/// <param name="DirColor">Directional (sun) colour.</param>
/// <param name="AmbBright">Ambient level.</param>
/// <param name="AmbColor">Ambient colour.</param>
/// <param name="MinWorldFog">Fog start distance in metres.</param>
/// <param name="MaxWorldFog">Fog end distance in metres.</param>
/// <param name="WorldFogColor">Fog colour.</param>
/// <param name="WorldFogMode">Retail <c>world_fog</c>: only tested for non-zero (fog on); the device always uses linear
/// vertex range fog. 1 in every keyframe of the user's dat.</param>
/// <param name="Replacements">Sky object overrides active from this keyframe.</param>
public sealed record SkyKeyframe(
    float Begin,
    float DirBright,
    float DirHeading,
    float DirPitch,
    AcColor DirColor,
    float AmbBright,
    AcColor AmbColor,
    float MinWorldFog,
    float MaxWorldFog,
    AcColor WorldFogColor,
    uint WorldFogMode,
    IReadOnlyList<SkyObjectReplacement> Replacements)
{
    public static SkyKeyframe FromDat(SkyTimeOfDay s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var replacements = new SkyObjectReplacement[s.SkyObjReplace.Count];
        for (var i = 0; i < replacements.Length; i++)
        {
            replacements[i] = SkyObjectReplacement.FromDat(s.SkyObjReplace[i]);
        }

        return new SkyKeyframe(
            s.Begin,
            s.DirBright,
            s.DirHeading,
            s.DirPitch,
            AcColor.FromDat(s.DirColor),
            s.AmbBright,
            AcColor.FromDat(s.AmbColor),
            s.MinWorldFog,
            s.MaxWorldFog,
            AcColor.FromDat(s.WorldFogColor),
            s.WorldFog,
            replacements);
    }
}

/// <summary>Per-keyframe override of one sky object (<c>SkyObjectReplace::UnPack</c>, acclient.c:301922).</summary>
/// <param name="ObjectIndex">Index into the day group's sky objects.</param>
/// <param name="GfxObjId">Replacement GfxObj/Setup; 0 = keep the default.</param>
/// <param name="Rotate">Heading in degrees applied with <c>Frame::set_heading</c>; 0 = none.</param>
/// <param name="Transparent">Percent; negative = not set. Retail applies <c>SetTranslucency(x * 0.01)</c>.</param>
/// <param name="Luminosity">Percent; &lt;= 0 = not set. Retail applies <c>SetLuminosity(x * 0.01)</c>.</param>
/// <param name="MaxBright">Percent; &lt;= 0 = not set. Retail applies <c>SetDiffusion(x * 0.01)</c>.</param>
public sealed record SkyObjectReplacement(uint ObjectIndex, uint GfxObjId, float Rotate, float Transparent, float Luminosity, float MaxBright)
{
    public static SkyObjectReplacement FromDat(SkyObjectReplace r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new SkyObjectReplacement(r.ObjectIndex, r.GfxObjId?.DataId ?? 0u, r.Rotate, r.Transparent, r.Luminosity, r.MaxBright);
    }
}
