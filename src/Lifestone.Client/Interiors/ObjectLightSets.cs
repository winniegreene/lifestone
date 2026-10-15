using System.Numerics;

namespace Lifestone.Client.Interiors;

/// <summary>One hardware point light as a moving object's shader reads it.</summary>
/// <param name="Position">The light's position, in the space of the sphere it was selected for.</param>
/// <param name="Radiance">Colour times intensity: the factor of <c>max(0, N.L) / d</c> (<see cref="CellObjectLighting"/>).</param>
/// <param name="Reach">The light's range in metres (<c>falloff x rangeAdjust</c>); a vertex farther away gets nothing.</param>
public readonly record struct ObjectLight(Vector3 Position, Vector3 Radiance, float Reach);

/// <summary>
/// The light set of a moving object in a cell (the player, a server's creature): the lights retail enables for it, at most
/// eight (M2 owner decision D46, <c>Render::minimize_object_lighting</c>, acclient.c:380678), and the layout the shaders read
/// them in (<c>ac_object_lights</c>, <c>SurfaceLightingCode.ObjectLights</c>).
/// </summary>
/// <remarks>
/// <para><b>Selection.</b> Statics are lit once, per part, on a worker (<see cref="CellObjectLighting"/>); an object that moves
/// is lit every frame, per vertex, by the shader, and gets one set for the whole object: the first eight of the cell's lights
/// (<see cref="CellStaticLighting.SelectFor"/>: nearest to the viewer's cell first, as retail keeps them) that reach a sphere
/// round the object (<c>Render::viewconeCheck</c> sets the sphere; retail does it per part, a deviation that costs one row per
/// object instead of one per part; for the human at the Training Academy's start the union of the parts' sets is this set,
/// <c>MovingObjectLightEvidenceTests</c>). The reach test is retail's as far as the decompile
/// shows it (<c>TODO(interiors-light-reach)</c>, <see cref="CellObjectLighting"/>). Retail enables dynamic lights (spell effects) first; none exist here yet, so the static
/// lights are all there is (<c>TODO(g5-dynamic-lights)</c>).</para>
/// <para><b>Layout</b> of one row of the table, <see cref="RowWidth"/> RGBA32F texels: for light <c>i</c>, texel <c>2i</c> holds
/// the position (xyz, in Godot world space) and the reach (w; 0 ends the list), texel <c>2i + 1</c> the radiance (rgb). The
/// converting of positions to Godot axes and the render origin belongs to the Godot side.</para>
/// </remarks>
public static class ObjectLightSets
{
    /// <summary>Most lights enabled for one object (<c>Render::minimize_object_lighting</c>).</summary>
    public const int MaxLights = CellObjectLighting.MaxLights;

    /// <summary>Texels one light takes in a row.</summary>
    public const int TexelsPerLight = 2;

    /// <summary>Texels in a row of the table.</summary>
    public const int RowWidth = MaxLights * TexelsPerLight;

    /// <summary>
    /// The lights that reach the sphere, in the order of <paramref name="candidates"/>, at most <see cref="MaxLights"/>, written
    /// to <paramref name="into"/>. Allocation-free.
    /// </summary>
    /// <param name="sphereCenter">The object's sphere centre, in the landblock space the candidates are in.</param>
    /// <param name="sphereRadius">Its radius in metres.</param>
    /// <param name="candidates">The cell's static lights, nearest to the viewer's cell first.</param>
    /// <param name="into">Room for <see cref="MaxLights"/> lights.</param>
    /// <returns>How many lights were written.</returns>
    public static int Select(Vector3 sphereCenter, float sphereRadius, IReadOnlyList<CellLight> candidates, Span<ObjectLight> into)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (into.Length < MaxLights)
        {
            throw new ArgumentException($"Room for {MaxLights} lights is needed; got {into.Length}.", nameof(into));
        }

        var count = 0;
        for (var i = 0; i < candidates.Count && count < MaxLights; i++)
        {
            var light = candidates[i];
            var reach = light.Falloff + sphereRadius;
            if (Vector3.DistanceSquared(light.Position, sphereCenter) <= reach * reach)
            {
                into[count++] = new ObjectLight(light.Position, light.Color * light.Intensity, light.Falloff * CellObjectLighting.RangeAdjust);
            }
        }

        return count;
    }
}
