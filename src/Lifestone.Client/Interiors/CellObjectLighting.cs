using System.Numerics;

namespace Lifestone.Client.Interiors;

/// <summary>
/// Retail lighting of the static objects inside EnvCells by the cells' static lights: D3D9 hardware point lights
/// (docs/interiors.md §4.4).
/// </summary>
/// <remarks>
/// <para><b>Retail.</b> Objects in cells are drawn with the sun off (<c>PView::DrawCells</c>), so
/// <c>RenderDeviceD3D::DrawMeshInternal</c> calls <c>Render::minimize_object_lighting</c> for every part: it enables at
/// most 8 lights (acclient.c:380678), dynamic lights first, then the static lights of
/// <c>Render::world_lights.sorted_static_lights</c> (nearest to the viewer's cell first) that reach the part's drawing
/// sphere (<c>Render::viewconeCheck(gfxobj-&gt;drawing_sphere)</c> sets the sphere). Each is a D3D point light
/// (<c>PrimD3DRender::config_hardware_light</c>): diffuse = colour × intensity, range = falloff × <c>rangeAdjust</c>
/// (1.5, acclient.c:45742), attenuation (0, 1, 0), i.e. <c>1 / d</c>. D3D9's fixed-function lighting then adds, per
/// vertex, <c>diffuse × max(0, N·L) / d</c> for every enabled light within range to the emissive and ambient terms and
/// clamps the sum (material diffuse is the white vertex colour, <c>copyVert</c>).</para>
/// <para>With intensities of 20-100 that saturates almost everywhere a light reaches: the sides of an object that face a
/// torch show their full texture colour, the other sides only the ambient.</para>
/// <para><b>Uncertain detail, named: <c>TODO(interiors-light-reach)</c>.</b> The decompiler lost the comparison in
/// <c>minimize_object_lighting</c>'s static-light test (and in <c>Render::remove_object_light</c> for dynamic lights). Both builds'
/// decompiles (11.4186 and 11.6096, <c>Render::minimize_object_lighting</c> 0x0054E090, <c>Render::remove_object_light</c>
/// 0x0054CDC0) show the same thing: the sum <c>falloff + radius</c> is formed, and the test's result is only the x87 condition flags
/// of a comparison whose operands are not shown. Lifestone keeps a light when the distance from the sphere centre is at most
/// <c>falloff + radius</c> (a light whose reach does not touch the sphere could not light it anyway). Note what that rule does:
/// the hardware light's own range is <c>falloff × 1.5</c>, so a light between <c>falloff + radius</c> and
/// <c>falloff × 1.5 + radius</c> away would light the object if it were enabled, and under this rule it is not. A byte read of the
/// function in the owner's <c>acclient.exe</c> (docs/research/retail-addresses-m2.md §3.1) would settle the operands.</para>
/// <para><b>Lifestone</b> computes this on the CPU once per object part (the objects never move) and hands the clamped
/// sum to the shader as a vertex attribute; the light list is the one <see cref="CellStaticLighting.SelectFor"/> gives
/// for the object's cell (see there for how that approximates retail's per-viewer list).</para>
/// </remarks>
public static class CellObjectLighting
{
    /// <summary><c>rangeAdjust</c> (acclient.c:45742): a hardware light reaches <c>falloff × 1.5</c>.</summary>
    public const float RangeAdjust = 1.5f;

    /// <summary>Most lights enabled for one part (<c>Render::minimize_object_lighting</c>, acclient.c:380678).</summary>
    public const int MaxLights = 8;

    /// <summary>
    /// The lights retail enables for a part: the first <see cref="MaxLights"/> of <paramref name="candidates"/> (in their
    /// order) whose distance from the sphere centre is at most <c>falloff + radius</c>.
    /// </summary>
    /// <param name="sphereCenter">The part's drawing sphere centre, landblock space.</param>
    /// <param name="sphereRadius">Its radius (metres).</param>
    /// <param name="candidates">The static lights, nearest to the viewer's cell first.</param>
    public static IReadOnlyList<CellLight> SelectFor(Vector3 sphereCenter, float sphereRadius, IReadOnlyList<CellLight> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<CellLight>? result = null;
        foreach (var light in candidates)
        {
            var reach = light.Falloff + sphereRadius;
            if (Vector3.DistanceSquared(light.Position, sphereCenter) <= reach * reach)
            {
                (result ??= new List<CellLight>(MaxLights)).Add(light);
                if (result.Count == MaxLights)
                {
                    break;
                }
            }
        }

        return result ?? (IReadOnlyList<CellLight>)[];
    }

    /// <summary>
    /// The clamped hardware-light sum per vertex: <c>min(1, Σ colour × intensity × max(0, N·L̂) / d)</c> per channel over
    /// the lights within range. Positions and normals are already in landblock space.
    /// </summary>
    /// <returns>True when some vertex receives light.</returns>
    public static bool Light(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> normals, IReadOnlyList<CellLight> lights, Span<Vector3> rgb)
    {
        ArgumentNullException.ThrowIfNull(lights);
        if (normals.Length != positions.Length || rgb.Length < positions.Length)
        {
            throw new ArgumentException("positions, normals and rgb disagree in length.");
        }

        var any = false;
        for (var v = 0; v < positions.Length; v++)
        {
            var sum = Vector3.Zero;
            var n = normals[v];
            var nLength = n.Length();
            if (nLength > 1e-6f)
            {
                n /= nLength;
                foreach (var light in lights)
                {
                    var toLight = light.Position - positions[v];
                    var d = toLight.Length();
                    if (!(d > 1e-6f) || d > light.Falloff * RangeAdjust)
                    {
                        continue;
                    }

                    var cos = Vector3.Dot(n, toLight / d);
                    if (cos > 0f)
                    {
                        sum += light.Color * (light.Intensity * cos / d);
                    }
                }
            }

            // A zero normal gets no diffuse term (D3D: N·L = 0).
            sum = Vector3.Clamp(sum, Vector3.Zero, Vector3.One);
            rgb[v] = sum;
            any |= sum != Vector3.Zero;
        }

        return any;
    }
}
