using System.Numerics;

namespace Lifestone.Data.World.Sky;

/// <summary>
/// Retail sky, light and fog evaluation for a day group at a time of day.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed (not copied) from the retail decompile, Mag-nus/acclient-AI-RE 2013-09 11.4186 with PDB names,
/// PORTAL/cregion/CSkyDesc.c: <c>DayGroup::GetTimeOfDay</c> (acclient.c:301423), <c>SkyDesc::GetLighting</c> (301484),
/// <c>SkyDesc::GetWorldFog</c> (301601), <c>SkyDesc::GetSky</c> (302961); PORTAL/lscape/Sky.c: <c>GameSky::UseTime</c>
/// (308825), <c>GameSky::CalcFrame</c> (308458); PORTAL/lscape/LScape.c: <c>LScape::UseTime</c> (307221),
/// <c>LScape::set_landscape_lighting</c> (307002); PORTAL/cgeometry/CFrame.c: <c>Frame::set_heading</c> (357691),
/// <c>Frame::set_vector_heading</c> (357667), <c>Frame::grotate</c> (357421). GetSky's visibility test and
/// GetTimeOfDay's wrap ratio were checked unchanged in 11.6096 (acclient.c:299142, 297616), the user's client build.
/// </para>
/// <para>
/// Deliberate differences from OpenAC (docs/research/foundation-openac.md "Other deviations"): the sun heading is lerped
/// linearly, never by shortest arc (the dat's 0.610 -> 0.611 keyframe pair flips 90° -> 270° at the zenith on purpose),
/// and sky object visibility is the strict <c>Begin &lt; t &lt; End</c> with no wrap at midnight. After the last
/// keyframe retail interpolates toward keyframe 0 over <c>(t - b) / (1 - b)</c>, i.e. as if keyframe 0 sat at t = 1.0,
/// not at 1 + keyframe0.Begin.
/// </para>
/// <para>
/// Precision: retail runs the x87 FPU at 53-bit precision (<c>ClientMain::ConfigureFPU</c>, acclient.c:60885; the
/// D3D device is created with D3DCREATE_FPU_PRESERVE), so intermediates are computed here in double in the same order
/// and rounded to float wherever retail stores a float. sin/cos/atan2 come from .NET instead of x87 fsin/fcos/fpatan
/// and can differ in the last bit before the float rounding.
/// </para>
/// </remarks>
public static class SkyEvaluator
{
    /// <summary>The degrees-to-radians literal retail uses (0.0174532925199433).</summary>
    public const double DegToRad = 0.0174532925199433;

    /// <summary>Retail <c>GetLighting</c> fallback ambient level when a day group has no keyframes (0x3E99999A).</summary>
    public const float FallbackAmbientLevel = 0.3f;

    /// <summary>Retail <c>GetLighting</c> fallback sun vector when a day group has no keyframes ((0.5, 0, 0x3F4CCCCD)).</summary>
    public static readonly Vector3 FallbackSunVector = new(0.5f, 0f, 0.8f);

    /// <summary>Retail <c>Frame::grotate</c> ignores rotation vectors shorter than this (float literal 0.00019999999).</summary>
    private const float GRotateEpsilon = 0.00019999999f;

    /// <summary>
    /// <c>DayGroup::GetTimeOfDay</c>: before = last keyframe whose Begin &lt;= t (scan from keyframe 1; keyframe 0 is the
    /// default), after = the next one, or keyframe 0 after the last with ratio <c>(t - b) / (1 - b)</c>.
    /// Null when the day group has no keyframes.
    /// </summary>
    public static SkyKeyframeSpan? GetTimeOfDay(SkyDayGroup dayGroup, float timeOfDay)
    {
        ArgumentNullException.ThrowIfNull(dayGroup);
        var keys = dayGroup.Keyframes;
        if (keys.Count == 0)
        {
            return null;
        }

        var before = 0;
        while (before < keys.Count - 1 && !(keys[before + 1].Begin > timeOfDay))
        {
            before++;
        }

        double beforeBegin = keys[before].Begin;
        if (before == keys.Count - 1)
        {
            return new SkyKeyframeSpan(before, 0, (float)((timeOfDay - beforeBegin) / (1.0 - beforeBegin)));
        }

        return new SkyKeyframeSpan(before, before + 1, (float)((timeOfDay - beforeBegin) / (keys[before + 1].Begin - beforeBegin)));
    }

    /// <summary>
    /// <c>SkyDesc::GetLighting</c> followed by <c>LScape::UseTime</c>'s clamp of the ambient level to
    /// <see cref="SkyDescription.MinAmbientLevel"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// ambient  = lerp(AmbBright)                      (float)
    /// ambColor = per-byte truncating lerp(AmbColor)
    /// bright   = lerp(DirBright)                      (double)
    /// pitch    = (float)(lerp(DirPitch) * DEG2RAD)
    /// heading  = lerp(DirHeading) * DEG2RAD          (linear, double)
    /// sun      = ((float)(sin h * bright * cos p), (float)(cos h * bright * cos p), (float)(bright * sin p))
    /// dirColor = per-byte truncating lerp(DirColor)
    /// </code>
    /// Heading 0 = +Y (north), 90 = +X (east); pitch is elevation, so the vector points toward the sun.
    /// Retail's <c>LScape::m_fAlwaysDaylight</c> ("always day" option) re-queries <c>GetLighting(0.5)</c> for terrain and
    /// objects while the sky keeps moving; callers can pass 0.5 here for that.
    /// </remarks>
    public static SkyLighting GetLighting(SkyDayGroup dayGroup, float timeOfDay)
    {
        var span = GetTimeOfDay(dayGroup, timeOfDay);
        if (span is not { } s)
        {
            return new SkyLighting(
                FallbackAmbientLevel,
                Math.Max(FallbackAmbientLevel, SkyDescription.MinAmbientLevel),
                AcColor.White,
                FallbackSunVector,
                AcColor.White);
        }

        var b = dayGroup.Keyframes[s.BeforeIndex];
        var a = dayGroup.Keyframes[s.AfterIndex];
        double r = s.Ratio;

        var ambient = (float)((((double)a.AmbBright - b.AmbBright) * r) + b.AmbBright);
        var ambColor = AcColor.Lerp(b.AmbColor, a.AmbColor, s.Ratio);
        var bright = b.DirBright + (((double)a.DirBright - b.DirBright) * r);
        var pitch = (float)(((((double)a.DirPitch - b.DirPitch) * r) + b.DirPitch) * DegToRad);
        var heading = (b.DirHeading + (((double)a.DirHeading - b.DirHeading) * r)) * DegToRad;
        var cosPitch = Math.Cos(pitch);
        var sun = new Vector3(
            (float)(Math.Sin(heading) * bright * cosPitch),
            (float)(Math.Cos(heading) * bright * cosPitch),
            (float)(bright * Math.Sin(pitch)));
        var dirColor = AcColor.Lerp(b.DirColor, a.DirColor, s.Ratio);

        // LScape::UseTime: if (ambient < (double)LScape::min_ambient) ambient = LScape::min_ambient.
        var clamped = ambient < SkyDescription.MinAmbientLevel ? SkyDescription.MinAmbientLevel : ambient;
        return new SkyLighting(ambient, clamped, ambColor, sun, dirColor);
    }

    /// <summary>
    /// <c>SkyDesc::GetWorldFog</c>: linear lerp of MinWorldFog/MaxWorldFog and a truncating per-byte lerp of the colour,
    /// only when both bracketing keyframes have a non-zero <c>world_fog</c>.
    /// </summary>
    public static SkyFog GetWorldFog(SkyDayGroup dayGroup, float timeOfDay)
    {
        var span = GetTimeOfDay(dayGroup, timeOfDay);
        if (span is not { } s)
        {
            return default;
        }

        var b = dayGroup.Keyframes[s.BeforeIndex];
        var a = dayGroup.Keyframes[s.AfterIndex];
        if (b.WorldFogMode == 0 || a.WorldFogMode == 0)
        {
            return new SkyFog(false, 0f, 0f, default, b.WorldFogMode);
        }

        double r = s.Ratio;
        var start = (float)((((double)a.MinWorldFog - b.MinWorldFog) * r) + b.MinWorldFog);
        var end = (float)((((double)a.MaxWorldFog - b.MaxWorldFog) * r) + b.MaxWorldFog);
        return new SkyFog(true, start, end, AcColor.Lerp(b.WorldFogColor, a.WorldFogColor, s.Ratio), b.WorldFogMode);
    }

    /// <summary>
    /// <c>SkyDesc::GetSky</c> plus the per-object frame and material values <c>GameSky::UseTime</c> applies.
    /// </summary>
    /// <remarks>
    /// <para>Per object: <c>gfx = (Begin == End || (Begin &lt; t &amp;&amp; t &lt; End)) ? DefaultGfxObj : 0</c>;
    /// <c>rotation = Begin == End ? BeginAngle : (EndAngle - BeginAngle) * (t - Begin) / (End - Begin) + BeginAngle</c>
    /// (also outside the window); heading = 0; luminosity = max_bright = transparent = -1.</para>
    /// <para>Then for each replacement of the <i>before</i> keyframe, in order: a non-zero GfxObjId replaces gfx (even when
    /// the object is outside its window, so it becomes drawn); a non-zero Rotate sets heading; and if the replacement has
    /// luminosity &gt; 0, max_bright &gt; 0 or transparent &gt;= 0, the first replacement for the same object in the
    /// <i>after</i> keyframe is found and each property set in both is lerped with the keyframe ratio. A property set in
    /// only one of them stays -1 (unset), as does everything when the after keyframe has no replacement for the object.</para>
    /// </remarks>
    public static IReadOnlyList<SkyObjectState> GetSky(SkyDayGroup dayGroup, float timeOfDay)
    {
        ArgumentNullException.ThrowIfNull(dayGroup);
        var objects = dayGroup.SkyObjects;
        var count = objects.Count;
        var gfx = new uint[count];
        var heading = new float[count];
        var rotation = new float[count];
        var luminosity = new float[count];
        var maxBright = new float[count];
        var transparent = new float[count];

        for (var i = 0; i < count; i++)
        {
            var o = objects[i];
            gfx[i] = o.IsInWindow(timeOfDay) ? o.DefaultGfxObjId : 0u;
            rotation[i] = o.BeginTime == o.EndTime
                ? o.BeginAngle
                : (float)(((((double)o.EndAngle - o.BeginAngle) * ((double)timeOfDay - o.BeginTime)) / ((double)o.EndTime - o.BeginTime)) + o.BeginAngle);
            luminosity[i] = -1f;
            maxBright[i] = -1f;
            transparent[i] = -1f;
        }

        if (GetTimeOfDay(dayGroup, timeOfDay) is { } span)
        {
            var before = dayGroup.Keyframes[span.BeforeIndex];
            var after = dayGroup.Keyframes[span.AfterIndex];
            double r = span.Ratio;
            foreach (var rep in before.Replacements)
            {
                var i = (int)rep.ObjectIndex;
                if (rep.GfxObjId != 0)
                {
                    gfx[i] = rep.GfxObjId;
                }

                if (rep.Rotate != 0f)
                {
                    heading[i] = rep.Rotate;
                }

                if (rep.Luminosity > 0f || rep.MaxBright > 0f || rep.Transparent >= 0f)
                {
                    SkyObjectReplacement? next = null;
                    foreach (var candidate in after.Replacements)
                    {
                        if (candidate.ObjectIndex == rep.ObjectIndex)
                        {
                            next = candidate;
                            break;
                        }
                    }

                    if (next is not null)
                    {
                        if (rep.Luminosity > 0f && next.Luminosity > 0f)
                        {
                            luminosity[i] = (float)((((double)next.Luminosity - rep.Luminosity) * r) + rep.Luminosity);
                        }

                        if (rep.MaxBright > 0f && next.MaxBright > 0f)
                        {
                            maxBright[i] = (float)((((double)next.MaxBright - rep.MaxBright) * r) + rep.MaxBright);
                        }

                        if (rep.Transparent >= 0f && next.Transparent >= 0f)
                        {
                            transparent[i] = (float)((((double)next.Transparent - rep.Transparent) * r) + rep.Transparent);
                        }
                    }
                }
            }
        }

        var result = new SkyObjectState[count];
        for (var i = 0; i < count; i++)
        {
            var o = objects[i];
            result[i] = new SkyObjectState(
                i,
                o,
                gfx[i],
                o.DefaultPesObjId,
                o.Properties,
                heading[i],
                rotation[i],
                o.TexVelocity,
                luminosity[i],
                maxBright[i],
                transparent[i],
                CalcFrame(heading[i], rotation[i]));
        }

        return result;
    }

    /// <summary>
    /// <c>GameSky::CalcFrame</c> on the identity frame <c>GameSky::UseTime</c> builds: <c>Frame::set_heading(heading)</c>
    /// if heading != 0 (a rotation about +Z by -heading: local +Y turns to (sin h, cos h, 0)), then
    /// <c>Frame::grotate((0, -rotation * DEG2RAD, 0))</c> if rotation != 0 (a world-space rotation about +Y, the
    /// north axis, by -rotation, applied after the heading). Returns the object-to-world rotation in the AC frame.
    /// </summary>
    /// <remarks>Objects sit at the camera (sky cell origin); weather objects (<see cref="SkyObjectProperties.Weather"/>)
    /// are moved to the player's position by <c>GameSky::UpdatePosition</c>, with z forced to -120 unless
    /// <see cref="SkyObjectProperties.WeatherKeepsPlayerZ"/>.</remarks>
    public static Quaternion CalcFrame(float heading, float rotation)
    {
        var q = Quaternion.Identity;
        if (heading != 0f)
        {
            q = SetHeading(heading);
        }

        if (rotation != 0f)
        {
            q = GRotate(q, new Vector3(0f, (float)(-rotation * DegToRad), 0f));
        }

        return q;
    }

    /// <summary>Evaluates everything for a day group at a float time of day.</summary>
    public static SkyState Evaluate(SkyDayGroup dayGroup, float timeOfDay, DerethDate? date = null)
    {
        ArgumentNullException.ThrowIfNull(dayGroup);
        return new SkyState(
            timeOfDay,
            dayGroup,
            GetTimeOfDay(dayGroup, timeOfDay),
            GetLighting(dayGroup, timeOfDay),
            GetWorldFog(dayGroup, timeOfDay),
            GetSky(dayGroup, timeOfDay),
            date);
    }

    /// <summary>
    /// <c>Frame::set_heading</c> applied to an identity frame: direction (sin h, cos h, 0) through
    /// <c>Frame::set_vector_heading</c>, i.e. <c>euler_set_rotate(asin(0), 0, -fmod(450 - atan2deg(dir.y, dir.x), 360) * DEG2RAD)</c>
    /// with the static-XYZ Euler order 0, which is a rotation about +Z. (Hex-Rays prints the atan2 operands of
    /// <c>set_vector_heading</c>/<c>get_heading</c> swapped; the operand order here is ACE AFrame.set_vector_heading's,
    /// the only one under which set_heading(90) faces east.)
    /// </summary>
    private static Quaternion SetHeading(float degrees)
    {
        var radians = degrees * DegToRad;
        double dirX = (float)Math.Sin(radians);
        double dirY = (float)Math.Cos(radians);
        var zDegrees = (450.0 - (Math.Atan2(dirY, dirX) * 57.29577951308232)) % 360.0;
        var zAngle = (float)(-zDegrees * DegToRad);
        var half = zAngle * 0.5;
        return Normalize(Math.Cos(half), 0.0, 0.0, Math.Sin(half));
    }

    /// <summary><c>Frame::grotate(w)</c>: pre-multiply by the rotation of angle |w| about w (world space), then
    /// <c>Frame::set_rotate</c>'s normalisation.</summary>
    private static Quaternion GRotate(Quaternion q, Vector3 w)
    {
        double lengthSquared = (w.X * (double)w.X) + (w.Y * (double)w.Y) + (w.Z * (double)w.Z);
        if (!(lengthSquared >= GRotateEpsilon * (double)GRotateEpsilon))
        {
            return q;
        }

        var theta = Math.Sqrt(lengthSquared);
        var thetaInv = (float)(1.0 / theta);
        var s = Math.Sin(theta * 0.5);
        var c = (float)Math.Cos(theta * 0.5);
        var f = (float)(s * w.X * thetaInv);
        var g = (float)(thetaInv * s * w.Y);
        var h = s * thetaInv * w.Z;
        var qz = (float)((f * (double)q.Y) + (c * (double)q.Z) - (g * (double)q.X) + (h * q.W));
        var qy = (float)((c * (double)q.Y) - (f * (double)q.Z) + (h * q.X) + (g * (double)q.W));
        var qx = (float)((c * (double)q.X) + (g * (double)q.Z) + (f * (double)q.W) - (h * q.Y));
        var qw = (float)((c * (double)q.W) - (f * (double)q.X) - (g * (double)q.Y) - (h * q.Z));
        return Normalize(qw, qx, qy, qz);
    }

    private static Quaternion Normalize(double w, double x, double y, double z)
    {
        var inv = 1.0 / Math.Sqrt((w * w) + (x * x) + (y * y) + (z * z));
        return new Quaternion((float)(x * inv), (float)(y * inv), (float)(z * inv), (float)(w * inv));
    }
}
