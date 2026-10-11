using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Sky;

/// <summary>
/// One sky object at one moment, as <c>SkyDesc::GetSky</c> + <c>GameSky::UseTime</c> leave it: the value-type twin of
/// <see cref="SkyObjectState"/> that <see cref="SkyObjectEvaluator"/> fills every frame without allocating.
/// </summary>
/// <param name="Index">Index in the day group's object list.</param>
/// <param name="GfxObjId">What to draw (<c>CelestialPosition::gfx_id</c>); 0 = nothing.</param>
/// <param name="Properties">Draw flags.</param>
/// <param name="Heading">Replacement <c>Rotate</c> in degrees (0 = none).</param>
/// <param name="Rotation">Arc angle in degrees.</param>
/// <param name="TexVelocity">UV scroll per second.</param>
/// <param name="Luminosity">Raw percent, or -1 when not set this frame.</param>
/// <param name="MaxBright">Raw percent, or -1 when not set this frame.</param>
/// <param name="Transparent">Raw percent, or -1 when not set this frame.</param>
/// <param name="Orientation">Object-to-world rotation (AC frame) from <c>GameSky::CalcFrame</c>.</param>
public readonly record struct SkyObjectFrame(
    int Index,
    uint GfxObjId,
    SkyObjectProperties Properties,
    float Heading,
    float Rotation,
    Vector2 TexVelocity,
    float Luminosity,
    float MaxBright,
    float Transparent,
    Quaternion Orientation)
{
    /// <summary>True when retail would create and draw a physics object for this slot.</summary>
    public bool IsDrawn => GfxObjId != 0;

    /// <summary><c>SetLuminosity(luminosity x 0.01)</c> when luminosity &gt; 0, else null (see <see cref="SkyObjectState.LuminosityScale"/>).</summary>
    public float? LuminosityScale => Luminosity > 0f ? Luminosity * SkyObjectState.PercentScale : null;

    /// <summary><c>SetDiffusion(max_bright x 0.01)</c> when max_bright &gt; 0, else null.</summary>
    public float? DiffusionScale => MaxBright > 0f ? MaxBright * SkyObjectState.PercentScale : null;

    /// <summary><c>SetTranslucency(transparent x 0.01)</c> when transparent &gt;= 0, else null.</summary>
    public float? Translucency => Transparent >= 0f ? Transparent * SkyObjectState.PercentScale : null;

    /// <summary>The same values from a <see cref="SkyObjectState"/> (tests, diagnostics).</summary>
    public static SkyObjectFrame From(SkyObjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SkyObjectFrame(
            state.Index,
            state.GfxObjId,
            state.Properties,
            state.Heading,
            state.Rotation,
            state.TexVelocity,
            state.Luminosity,
            state.MaxBright,
            state.Transparent,
            state.Orientation);
    }

    /// <summary>Every object of a <see cref="SkyState"/> as frames (allocates; tests and diagnostics).</summary>
    public static SkyObjectFrame[] From(SkyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var frames = new SkyObjectFrame[state.Objects.Count];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = From(state.Objects[i]);
        }

        return frames;
    }
}

/// <summary>
/// <see cref="SkyEvaluator.GetSky"/> without allocations: evaluates a day group's sky objects at a time of day into
/// buffers it reuses, so the per-frame sky update produces no garbage (docs/sky.md §7).
/// </summary>
/// <remarks>
/// The arithmetic and order are <see cref="SkyEvaluator.GetSky"/>'s (re-expressed from <c>SkyDesc::GetSky</c>,
/// acclient.c:302961, and <c>GameSky::UseTime</c>, 308826; see that method for the retail rules), with index loops
/// instead of enumerators and reused arrays instead of new ones. <c>SkyObjectEvaluatorTests</c> pins the two to the
/// same bits for every day group of the user's Region at every keyframe, window edge and a dense grid of times.
/// </remarks>
public sealed class SkyObjectEvaluator
{
    private SkyObjectFrame[] _objects = [];
    private uint[] _gfx = [];
    private float[] _heading = [];
    private float[] _rotation = [];
    private float[] _luminosity = [];
    private float[] _maxBright = [];
    private float[] _transparent = [];

    /// <summary>Objects evaluated by the last <see cref="Evaluate"/>.</summary>
    public int Count { get; private set; }

    /// <summary>The last evaluation, in dat (draw) order. Valid until the next <see cref="Evaluate"/>.</summary>
    public ReadOnlySpan<SkyObjectFrame> Objects => _objects.AsSpan(0, Count);

    /// <summary>Evaluates every object of <paramref name="dayGroup"/> at <paramref name="timeOfDay"/>.</summary>
    /// <param name="dayGroup">The day group.</param>
    /// <param name="timeOfDay">Retail <c>present_time_of_day</c>.</param>
    public ReadOnlySpan<SkyObjectFrame> Evaluate(SkyDayGroup dayGroup, float timeOfDay)
    {
        ArgumentNullException.ThrowIfNull(dayGroup);
        var objects = dayGroup.SkyObjects;
        var count = objects.Count;
        Reserve(count);
        Count = count;

        for (var i = 0; i < count; i++)
        {
            var o = objects[i];
            _gfx[i] = o.IsInWindow(timeOfDay) ? o.DefaultGfxObjId : 0u;
            _heading[i] = 0f;
            _rotation[i] = o.BeginTime == o.EndTime
                ? o.BeginAngle
                : (float)(((((double)o.EndAngle - o.BeginAngle) * ((double)timeOfDay - o.BeginTime)) / ((double)o.EndTime - o.BeginTime)) + o.BeginAngle);
            _luminosity[i] = -1f;
            _maxBright[i] = -1f;
            _transparent[i] = -1f;
        }

        if (SkyEvaluator.GetTimeOfDay(dayGroup, timeOfDay) is { } s)
        {
            var before = dayGroup.Keyframes[s.BeforeIndex].Replacements;
            var after = dayGroup.Keyframes[s.AfterIndex].Replacements;
            double r = s.Ratio;
            for (var k = 0; k < before.Count; k++)
            {
                var rep = before[k];
                var i = (int)rep.ObjectIndex;
                if (rep.GfxObjId != 0)
                {
                    _gfx[i] = rep.GfxObjId;
                }

                if (rep.Rotate != 0f)
                {
                    _heading[i] = rep.Rotate;
                }

                if (!(rep.Luminosity > 0f || rep.MaxBright > 0f || rep.Transparent >= 0f))
                {
                    continue;
                }

                SkyObjectReplacement? next = null;
                for (var n = 0; n < after.Count; n++)
                {
                    if (after[n].ObjectIndex == rep.ObjectIndex)
                    {
                        next = after[n];
                        break;
                    }
                }

                if (next is null)
                {
                    continue;
                }

                if (rep.Luminosity > 0f && next.Luminosity > 0f)
                {
                    _luminosity[i] = (float)((((double)next.Luminosity - rep.Luminosity) * r) + rep.Luminosity);
                }

                if (rep.MaxBright > 0f && next.MaxBright > 0f)
                {
                    _maxBright[i] = (float)((((double)next.MaxBright - rep.MaxBright) * r) + rep.MaxBright);
                }

                if (rep.Transparent >= 0f && next.Transparent >= 0f)
                {
                    _transparent[i] = (float)((((double)next.Transparent - rep.Transparent) * r) + rep.Transparent);
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            var o = objects[i];
            _objects[i] = new SkyObjectFrame(
                i,
                _gfx[i],
                o.Properties,
                _heading[i],
                _rotation[i],
                o.TexVelocity,
                _luminosity[i],
                _maxBright[i],
                _transparent[i],
                SkyEvaluator.CalcFrame(_heading[i], _rotation[i]));
        }

        return Objects;
    }

    private void Reserve(int count)
    {
        if (_objects.Length >= count)
        {
            return;
        }

        _objects = new SkyObjectFrame[count];
        _gfx = new uint[count];
        _heading = new float[count];
        _rotation = new float[count];
        _luminosity = new float[count];
        _maxBright = new float[count];
        _transparent = new float[count];
    }
}
