using System.Numerics;

namespace Lifestone.Client.Interiors;

/// <summary>
/// The layout of the global dynamic-light table the cell shaders read (<c>ac_dynamic_lights</c>, M3 4.1 r2, the cell glow): one row of
/// <see cref="Width"/> RGBA32F texels for up to <see cref="MaxLights"/> lights, rewritten every frame from a <see cref="DynamicLightFrame"/>.
/// For light <c>i</c>, texel <c>2i</c> holds the position (xyz, in the space the shader's vertices are in: Godot world space) and the reach
/// (w, metres; 0 ends the list), texel <c>2i + 1</c> the radiance (rgb, colour times intensity). Pure and allocation-free: the
/// Godot side converts positions (AC to Godot axes and the floating origin belong to <c>AcToGodot</c> and the world root) and uploads.
/// </summary>
/// <remarks>
/// The units are the object rows' (<see cref="ObjectLightSets"/>): radiance is the factor of <c>max(0, N.L) / d</c>, reach is
/// <c>falloff x 1.5</c> (<c>PrimD3DRender::config_hardware_light</c> 0x0059BD40). Retail enables every dynamic light as a hardware
/// light on a cell's polygons (<c>Render::minimize_envcell_lighting</c> 0x0054CD80), and there are at most seven of them
/// (<see cref="DynamicLightList.MaxLights"/>); the table has room for eight.
/// </remarks>
public static class DynamicLightTexels
{
    /// <summary>Most lights the table holds.</summary>
    public const int MaxLights = 8;

    /// <summary>Texels one light takes.</summary>
    public const int TexelsPerLight = 2;

    /// <summary>Texels in the table (it is one row).</summary>
    public const int Width = MaxLights * TexelsPerLight;

    /// <summary>Floats in the table (four per texel).</summary>
    public const int Floats = Width * 4;

    /// <summary>
    /// Writes <paramref name="lights"/> into <paramref name="texels"/>, in the order given (nearest the viewer first), at most
    /// <see cref="MaxLights"/>; the rest of the table is cleared (a reach of 0 ends the list in the shader). A light with no reach
    /// (zero or negative, or not finite) reaches nothing and is left out, so it cannot end the list early.
    /// </summary>
    /// <param name="lights">The frame's lights.</param>
    /// <param name="positions">Each light's position in the shader's space, one for each of <paramref name="lights"/> (the caller converts them).</param>
    /// <param name="texels">Room for <see cref="Floats"/> floats.</param>
    /// <returns>How many lights were written.</returns>
    public static int Pack(ReadOnlySpan<DynamicFrameLight> lights, ReadOnlySpan<Vector3> positions, Span<float> texels)
    {
        if (texels.Length < Floats)
        {
            throw new ArgumentException($"Room for {Floats} floats is needed; got {texels.Length}.", nameof(texels));
        }

        if (positions.Length < lights.Length)
        {
            throw new ArgumentException("A position is needed for each light.", nameof(positions));
        }

        texels[..Floats].Clear();
        var written = 0;
        for (var i = 0; i < lights.Length && written < MaxLights; i++)
        {
            ref readonly var light = ref lights[i];
            if (!(light.Reach > 0f) || !float.IsFinite(light.Reach))
            {
                continue;
            }

            var at = written * TexelsPerLight * 4;
            var position = positions[i];
            texels[at] = position.X;
            texels[at + 1] = position.Y;
            texels[at + 2] = position.Z;
            texels[at + 3] = light.Reach;
            texels[at + 4] = light.Radiance.X;
            texels[at + 5] = light.Radiance.Y;
            texels[at + 6] = light.Radiance.Z;
            written++;
        }

        return written;
    }

    /// <summary>
    /// True when a light reaches a box: the distance from the light to the nearest point of the axis-aligned box
    /// (<paramref name="min"/>, <paramref name="max"/>) is at most its reach. The engine check's and the cell counter's test of "a cell with a
    /// dynamic light in reach" (the cell's bounding box, in the light's space).
    /// </summary>
    public static bool ReachesBox(Vector3 position, float reach, Vector3 min, Vector3 max)
    {
        var nearest = Vector3.Clamp(position, min, max);
        return Vector3.DistanceSquared(position, nearest) <= reach * reach;
    }
}
