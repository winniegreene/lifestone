using System.Numerics;

namespace Lifestone.Data.World.Sky;

/// <summary>The two keyframes bracketing a time of day and the lerp ratio (<c>DayGroup::GetTimeOfDay</c>).</summary>
/// <param name="BeforeIndex">Last keyframe with Begin &lt;= t (keyframe 0 if t is before every keyframe).</param>
/// <param name="AfterIndex">The next keyframe, or keyframe 0 after the last one.</param>
/// <param name="Ratio">Float lerp ratio. Not clamped: it extrapolates if t precedes keyframe 0's Begin.</param>
public readonly record struct SkyKeyframeSpan(int BeforeIndex, int AfterIndex, float Ratio);

/// <summary>
/// Sun and ambient light at a time of day: retail <c>SkyDesc::GetLighting</c> plus the <c>LScape::UseTime</c> /
/// <c>LScape::set_landscape_lighting</c> post-processing that turns it into the renderer's light state.
/// </summary>
/// <param name="AmbientLevelRaw"><c>GetLighting</c>'s <c>ambient</c>: lerped AmbBright.</param>
/// <param name="AmbientLevel"><c>LScape::ambient_level</c>: <see cref="AmbientLevelRaw"/> clamped up to
/// <see cref="SkyDescription.MinAmbientLevel"/>. Terrain lighting uses this.</param>
/// <param name="AmbientColor"><c>LScape::ambient_color</c>.</param>
/// <param name="SunVector"><c>LScape::sunlight</c>: unnormalised, pointing from the ground toward the sun, length = lerped
/// DirBright. AC frame (x east, y north, z up).</param>
/// <param name="SunColor"><c>LScape::sunlight_color</c>.</param>
public readonly record struct SkyLighting(
    float AmbientLevelRaw,
    float AmbientLevel,
    AcColor AmbientColor,
    Vector3 SunVector,
    AcColor SunColor)
{
    /// <summary>|<see cref="SunVector"/>|, the sun brightness.</summary>
    public float SunIntensity => SunVector.Length();

    /// <summary>Unit vector toward the sun (AC frame), or +Z if the sun vector is ~0.</summary>
    public Vector3 SunDirection
    {
        get
        {
            var length = SunVector.Length();
            return length > 1e-6f ? SunVector / length : Vector3.UnitZ;
        }
    }

    /// <summary>Unit direction the sunlight travels (AC frame) = -<see cref="SunDirection"/>. Retail's D3D light uses
    /// <c>Direction = -sunlight</c> (D3D swizzle (x, z, y)) in <c>PrimD3DRender::UpdateLightsInternal</c> (acclient.c:453498-453515).</summary>
    public Vector3 LightTravelDirection => -SunDirection;

    /// <summary>D3D sunlight diffuse colour: <c>sunlight_color * |sunlight|</c> (<c>PrimD3DRender::UpdateLightsInternal</c>, acclient.c:453508). Objects use this with
    /// fixed-function lighting; terrain uses <see cref="SunVector"/> directly (CLandBlockStruct::calc_lighting).</summary>
    public Vector3 SunDiffuse => SunColor.ToVector3() * SunIntensity;

    /// <summary>Landscape ambient term: <c>ambient_level * ambient_color / 255</c> per channel, as
    /// <c>CLandBlockStruct::calc_lighting</c> (acclient.c:353712) adds it to <c>sunlight_color * max(0, n . sunlight)</c>
    /// per vertex (the terrain track owns the exact per-vertex formula and its precision).</summary>
    public Vector3 LandscapeAmbient => AmbientColor.ToVector3() * AmbientLevel;

    /// <summary>World ambient intensity for objects: <c>LScape::calc_object_light</c> =
    /// <c>|sunlight| * 0.2 + ambient_level</c> (acclient.c:146452; same formula inline in set_landscape_lighting).</summary>
    public float ObjectAmbientIntensity => (float)((SunIntensity * 0.2) + AmbientLevel);

    /// <summary>World ambient colour for objects: <c>SmartBox::SetWorldAmbientLight</c> scales all three channels of
    /// the 0..1 ambient colour by <see cref="ObjectAmbientIntensity"/> (verified in 11.6096 at 0x004530E0; the 11.4186
    /// Hex-Rays output drops the g/b multiplies).</summary>
    public Vector3 ObjectAmbient => AmbientColor.ToVector3() * ObjectAmbientIntensity;
}

/// <summary>World fog at a time of day (<c>SkyDesc::GetWorldFog</c>).</summary>
/// <param name="Enabled">False when retail's GetWorldFog returns 0 (no keyframes, or either bracketing keyframe has
/// <c>world_fog == 0</c>); retail then leaves the previous fog state untouched.</param>
/// <param name="Start">D3DRS_FOGSTART in metres.</param>
/// <param name="End">D3DRS_FOGEND in metres.</param>
/// <param name="Color">D3DRS_FOGCOLOR.</param>
/// <param name="Mode">The before keyframe's raw <c>world_fog</c> (only tested for non-zero). The device always uses
/// linear per-vertex range fog: <c>RenderDeviceD3D::SetDefaultD3DStates</c> sets FOGTABLEMODE NONE, FOGVERTEXMODE
/// LINEAR and RANGEFOGENABLE TRUE (acclient.c:460787-460812), i.e. f = (End - d) / (End - Start) with d the
/// eye-to-vertex distance, interpolated across the triangle.</param>
public readonly record struct SkyFog(bool Enabled, float Start, float End, AcColor Color, uint Mode);

/// <summary>One sky object as retail's <c>SkyDesc::GetSky</c> + <c>GameSky::UseTime</c> would place it.</summary>
/// <param name="Index">Index in the day group's object list.</param>
/// <param name="Desc">The dat description.</param>
/// <param name="GfxObjId">What to draw (<c>CelestialPosition::gfx_id</c>): the default GfxObj inside the visibility
/// window, else 0; a non-zero keyframe replacement overrides it even outside the window. 0 = not drawn
/// (<c>GameSky::MakeObject</c> creates nothing for id 0).</param>
/// <param name="PesObjId">Copied from the dat; retail never plays it.</param>
/// <param name="Properties">Draw flags.</param>
/// <param name="Heading">Replacement <c>Rotate</c> in degrees (0 = none).</param>
/// <param name="Rotation">Arc angle in degrees: BeginAngle, or the lerp across the visibility window. Computed even
/// when the object is outside its window.</param>
/// <param name="TexVelocity">UV scroll per second, see <see cref="SkyTextureScroll"/>.</param>
/// <param name="Luminosity">Raw percent, or -1 when not set this frame.</param>
/// <param name="MaxBright">Raw percent, or -1 when not set this frame.</param>
/// <param name="Transparent">Raw percent, or -1 when not set this frame.</param>
/// <param name="Orientation">Object-to-world rotation (AC frame) from <c>GameSky::CalcFrame</c>.</param>
public sealed record SkyObjectState(
    int Index,
    SkyObjectDesc Desc,
    uint GfxObjId,
    uint PesObjId,
    SkyObjectProperties Properties,
    float Heading,
    float Rotation,
    Vector2 TexVelocity,
    float Luminosity,
    float MaxBright,
    float Transparent,
    Quaternion Orientation)
{
    /// <summary>The 0.01 retail multiplies percentages by in <c>GameSky::UseTime</c> (a float constant).</summary>
    public const float PercentScale = 0.0099999998f;

    /// <summary>True when retail would create and draw a physics object for this slot.</summary>
    public bool IsDrawn => GfxObjId != 0;

    /// <summary><c>CPhysicsObj::SetLuminosity(luminosity * 0.01)</c> when luminosity &gt; 0, else null (material
    /// keeps its previous/default luminosity).</summary>
    public float? LuminosityScale => Luminosity > 0f ? Luminosity * PercentScale : null;

    /// <summary><c>CPhysicsObj::SetDiffusion(max_bright * 0.01)</c> when max_bright &gt; 0, else null.</summary>
    public float? DiffusionScale => MaxBright > 0f ? MaxBright * PercentScale : null;

    /// <summary><c>CPhysicsObj::SetTranslucency(transparent * 0.01)</c> when transparent &gt;= 0, else null.
    /// 0 = opaque, 1 = fully transparent.</summary>
    public float? Translucency => Transparent >= 0f ? Transparent * PercentScale : null;
}

/// <summary>Everything the presentation layer needs to draw the sky and light the world at one moment.</summary>
/// <param name="TimeOfDay">The float day fraction everything was evaluated at (<c>present_time_of_day</c>).</param>
/// <param name="DayGroup">The day group used.</param>
/// <param name="Keyframes">Bracketing keyframes, or null when the day group has none.</param>
/// <param name="Lighting">Sun and ambient.</param>
/// <param name="Fog">World fog.</param>
/// <param name="Objects">One entry per sky object of the day group, in dat order (draw order).</param>
/// <param name="Date">Calendar date, when evaluated from a game time.</param>
public sealed record SkyState(
    float TimeOfDay,
    SkyDayGroup DayGroup,
    SkyKeyframeSpan? Keyframes,
    SkyLighting Lighting,
    SkyFog Fog,
    IReadOnlyList<SkyObjectState> Objects,
    DerethDate? Date);
