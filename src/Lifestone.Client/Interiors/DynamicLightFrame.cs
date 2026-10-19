using System.Numerics;

namespace Lifestone.Client.Interiors;

/// <summary>One light of the per-frame snapshot (<see cref="DynamicLightFrame"/>).</summary>
/// <param name="OwnerId">The owning guid; 0 is the viewer light.</param>
/// <param name="CellId">The cell the light is in; its landblock fixes the space of <paramref name="Position"/>.</param>
/// <param name="Position">AC axes, metres, landblock-local to <paramref name="CellId"/>'s landblock (the space <c>CellLight.Position</c> and a <c>CellFrame.Origin</c> are in).</param>
/// <param name="Radiance">Colour times intensity: the factor of <c>max(0, N.L) / d</c> (the same as <see cref="ObjectLight.Radiance"/>).</param>
/// <param name="Falloff">The light's falloff in metres.</param>
/// <param name="Reach">The hardware light's range in metres, <c>falloff x 1.5</c> (<c>PrimD3DRender::config_hardware_light</c> 0x0059BD40); a vertex farther away gets nothing.</param>
public readonly record struct DynamicFrameLight(uint OwnerId, uint CellId, Vector3 Position, Vector3 Radiance, float Falloff, float Reach);

/// <summary>
/// The dynamic lights of one frame, as the Godot layer reads them: the up to seven lights of the <see cref="DynamicLightList"/>, nearest the viewer first,
/// copied into fixed storage. Filled in place by <see cref="Fill"/> (one call per frame, from <c>AvatarSet.Advance</c>); reading allocates nothing.
/// </summary>
public sealed class DynamicLightFrame
{
    /// <summary>Most lights in a frame.</summary>
    public const int MaxLights = DynamicLightList.MaxLights;

    private readonly DynamicFrameLight[] _lights = new DynamicFrameLight[MaxLights];
    private int _count;

    /// <summary>Lights in the frame.</summary>
    public int Count => _count;

    /// <summary>Frames filled so far (a reader can tell a new frame from the last).</summary>
    public long Version { get; private set; }

    /// <summary>The <paramref name="index"/>th light, nearest first.</summary>
    public ref readonly DynamicFrameLight this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _lights[index];
        }
    }

    /// <summary>The lights, nearest first.</summary>
    public ReadOnlySpan<DynamicFrameLight> Lights => new(_lights, 0, _count);

    /// <summary>Copies <paramref name="list"/>'s current lights in.</summary>
    public void Fill(DynamicLightList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _count = list.Count;
        for (var i = 0; i < _count; i++)
        {
            ref readonly var e = ref list[i];
            _lights[i] = new DynamicFrameLight(e.OwnerId, e.CellId, e.Position, e.Color * e.Intensity, e.Falloff, e.Falloff * CellObjectLighting.RangeAdjust);
        }

        Version++;
    }

    /// <summary>Empties the frame.</summary>
    public void Clear() => _count = 0;
}
