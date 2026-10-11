using System.Numerics;
using Lifestone.Data.Models;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// A static light inside a cell: a light of the Setup of one of the cell's static objects (retail <c>LIGHTOBJ</c> whose
/// state has the static bit), in landblock space.
/// </summary>
/// <param name="CellId">The EnvCell the object belongs to.</param>
/// <param name="ModelId">The object's Setup.</param>
/// <param name="ObjectOrigin">The object's frame origin (landblock space). Retail sorts lights by this point, not by
/// the light's own position (<c>Render::insert_light</c> measures <c>frame-&gt;m_fOrigin</c>, the object frame passed
/// in as the light's <c>global_offset</c>).</param>
/// <param name="Position">The light's position: object frame ∘ light offset (<c>Frame::combine</c> in
/// <c>Render::insert_light</c>), landblock space.</param>
/// <param name="Color">Colour, each channel byte / 255 (<c>insert_light</c> rounds the colour through bytes).</param>
/// <param name="Intensity"><c>LIGHTINFO::intensity</c>.</param>
/// <param name="Falloff"><c>LIGHTINFO::falloff</c>.</param>
public sealed record CellLight(uint CellId, uint ModelId, Vector3 ObjectOrigin, Vector3 Position, Vector3 Color, float Intensity, float Falloff);

/// <summary>
/// Retail's burned-in EnvCell lighting: which static lights a cell is lit by and the vertex colours they give
/// (docs/interiors.md §4.3).
/// </summary>
/// <remarks>
/// <para><b>Which lights</b> (retail):</para>
/// <list type="number">
/// <item>Every object with a Setup light gets a <c>LIGHTLIST</c> (<c>CPartArray::InitLights</c>); its lights are static
/// when the object is (state bit 1, which the static objects of cells have) and are added to the object's cell
/// (<c>CPartArray::AddLightsToCell</c>). Each light's <c>global_offset</c> is the object frame
/// (<c>LIGHTLIST::set_frame</c>). All 608 end-of-retail Setups with a light have exactly one, of type 0 (point).</item>
/// <item>When the viewer changes cell, <c>CellManager::ChangePosition</c> empties the light list and
/// <c>CEnvCell::flush_cells</c> calls <c>CObjCell::add_static_to_global_lights</c> for every cell of
/// <c>CEnvCell::visible_cell_table</c>, i.e. the viewer's cell and its stab list (<c>CEnvCell::grab_visible_cells</c>).
/// <c>Render::insert_light</c> keeps them sorted by the squared distance from the object origin to the viewer cell's
/// origin and keeps the nearest <c>Render::max_static_lights</c> (40 at start-up, acclient.c:45530; the degrade
/// controller moves it between 40 and 60, <c>Render::SetDegradeLevelInternal</c>). A light at the same distance as one
/// already kept goes after it.</item>
/// <item><c>RenderDeviceD3D::DrawEnvCell</c> burns that list into each drawn cell's vertex colours
/// (<c>D3DPolyRender::SetStaticLightingVertexColors</c>), redoing it only when the number of lights changed since that
/// cell was last burned (<c>onlyOnce</c> = 1 and the <c>burnedInStaticLights</c> count).</item>
/// </list>
/// <para><b>Lifestone's approximation</b> (deterministic, computed once per cell on a worker): each cell is burned with
/// the list it would get with the viewer in that cell (<see cref="SelectFor"/>). For the viewer's own cell this is
/// exactly retail's list; for the other visible cells retail uses the viewer cell's list, and because of the count
/// cache it keeps whatever list it first burned a cell with as long as the count does not change, which depends on the
/// path the player walked. Lights only reach <c>falloff × 1.3</c> metres, so the two differ only where a light outside the
/// viewer cell's stab list (or beyond its nearest 40) shines on geometry that is visible from it.</para>
/// <para><b>The formula</b> (<c>calc_point_light</c>, acclient.c:454579), per vertex and light, in cell-local space:
/// <c>d = light - vertex</c>, <c>dist = |d|</c>, <c>range = falloff × 1.3</c> (<c>static_light_factor</c>,
/// acclient.c:45774); if <c>dist &lt; range</c> and <c>s = (0.5 dist + n·d) / 1.5 &gt; 0</c>:
/// <c>k = s / dist</c> when <c>dist² ≤ 1</c>, else <c>s / (dist² · dist)</c>; <c>k ×= (1 - dist / range) × intensity</c>;
/// each channel adds <c>min(k × colour, colour)</c>. The sums are clamped to [0, 1] and truncated to bytes
/// (<c>(unsigned __int8)(r × 255.0)</c>). Directional lights (type 1) add <c>intensity × colour × n·dir</c> when
/// positive; no end-of-retail light has that type, so it fails loudly here instead.</para>
/// </remarks>
public static class CellStaticLighting
{
    /// <summary><c>static_light_factor</c> (acclient.c:45774): the reach of a static light is <c>falloff × 1.3</c>.</summary>
    public const float StaticLightFactor = 1.3f;

    /// <summary><c>Render::max_static_lights</c> at start-up (acclient.c:45530).</summary>
    public const int MaxStaticLights = 40;

    /// <summary>
    /// The static lights of a cell's static objects, in stored object order (retail adds each object's lights to its
    /// cell when it is placed).
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="lightsOfModel">The lights of a static object's Setup (null or empty when it has none).</param>
    /// <exception cref="NotSupportedException">A light of a type other than point (0).</exception>
    public static IReadOnlyList<CellLight> LightsOf(EnvCellData cell, Func<uint, IReadOnlyList<ModelLight>?> lightsOfModel)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(lightsOfModel);
        List<CellLight>? result = null;
        foreach (var obj in cell.StaticObjects)
        {
            if (lightsOfModel(obj.ModelId) is not { Count: > 0 } lights)
            {
                continue;
            }

            foreach (var light in lights)
            {
                if (light.Type != 0)
                {
                    throw new NotSupportedException(
                        $"TODO(interiors): Setup 0x{obj.ModelId:X8} in cell 0x{cell.Id:X8} has a light of type {light.Type}; only point lights (0) occur in end-of-retail data and only they are implemented.");
                }

                // Frame::combine(info.offset, global_offset, light.offset): the light frame relative to the object frame.
                var position = obj.Frame.LocalToParent(light.Offset.Origin);
                var color = light.ColorRgb;
                (result ??= new List<CellLight>()).Add(new CellLight(cell.Id, obj.ModelId, obj.Frame.Origin, position, color, light.Intensity, light.Falloff));
            }
        }

        return result ?? (IReadOnlyList<CellLight>)[];
    }

    /// <summary>
    /// The static lights retail uses while the viewer is in <paramref name="viewerCell"/>: the lights of that cell and of
    /// its stab list, nearest <paramref name="max"/> by object origin to the cell origin (see the class remarks).
    /// </summary>
    /// <param name="viewerCell">The viewer's cell.</param>
    /// <param name="lightsOfCell">The lights of a cell by full id (empty when it has none or is not loaded).</param>
    /// <param name="max">Light limit (<see cref="MaxStaticLights"/>).</param>
    public static IReadOnlyList<CellLight> SelectFor(EnvCellData viewerCell, Func<uint, IReadOnlyList<CellLight>> lightsOfCell, int max = MaxStaticLights)
    {
        ArgumentNullException.ThrowIfNull(viewerCell);
        ArgumentNullException.ThrowIfNull(lightsOfCell);
        var origin = viewerCell.Frame.Origin;
        var candidates = new List<(CellLight Light, float DistanceSq)>();
        Add(viewerCell.Id);
        foreach (var id in viewerCell.VisibleCellIds)
        {
            Add(id);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        // Render::insert_light: sorted by distance, a light equal to a kept one goes after it, the farthest drop out
        // once the list is full. A stable sort followed by a cut gives the same list.
        var kept = candidates
            .Select((c, i) => (c.Light, c.DistanceSq, Index: i))
            .OrderBy(c => c.DistanceSq)
            .ThenBy(c => c.Index)
            .Take(Math.Max(0, max))
            .Select(c => c.Light)
            .ToArray();
        return kept;

        void Add(uint cellId)
        {
            foreach (var light in lightsOfCell(cellId))
            {
                candidates.Add((light, Vector3.DistanceSquared(light.ObjectOrigin, origin)));
            }
        }
    }

    /// <summary>
    /// <c>D3DPolyRender::SetStaticLightingVertexColors</c> for one set of vertices of a cell: the burned-in colour of
    /// every vertex as bytes (R, G, B), from cell-local positions and normals.
    /// </summary>
    /// <param name="positions">Vertex positions, cell-local.</param>
    /// <param name="normals">Vertex normals, cell-local (as stored; a zero normal gets only the wrap term).</param>
    /// <param name="cellFrame">The cell's frame (cell-local to landblock).</param>
    /// <param name="lights">The lights (landblock space).</param>
    /// <param name="rgb">Output, three bytes per vertex.</param>
    public static void Burn(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> normals, Placement cellFrame, IReadOnlyList<CellLight> lights, Span<byte> rgb)
    {
        ArgumentNullException.ThrowIfNull(lights);
        if (normals.Length != positions.Length || rgb.Length < positions.Length * 3)
        {
            throw new ArgumentException("positions, normals and rgb disagree in length.");
        }

        // LIGHTINFO::convert_to_local: point lights move into the cell frame.
        Span<Vector3> local = lights.Count <= 64 ? stackalloc Vector3[lights.Count] : new Vector3[lights.Count];
        for (var l = 0; l < lights.Count; l++)
        {
            local[l] = cellFrame.ParentToLocal(lights[l].Position);
        }

        for (var v = 0; v < positions.Length; v++)
        {
            float r = 0f, g = 0f, b = 0f;
            for (var l = 0; l < lights.Count; l++)
            {
                AddPointLight(positions[v], normals[v], local[l], lights[l], ref r, ref g, ref b);
            }

            rgb[v * 3] = ToByte(r);
            rgb[(v * 3) + 1] = ToByte(g);
            rgb[(v * 3) + 2] = ToByte(b);
        }
    }

    /// <summary><c>calc_point_light</c> (acclient.c:454579) for one vertex and one light (light position cell-local).</summary>
    public static void AddPointLight(Vector3 vertex, Vector3 normal, Vector3 lightLocal, CellLight light, ref float r, ref float g, ref float b)
    {
        ArgumentNullException.ThrowIfNull(light);
        double dx = lightLocal.X - vertex.X;
        double dy = lightLocal.Y - vertex.Y;
        double dz = lightLocal.Z - vertex.Z;
        var distSq = (float)((dz * dz) + (dy * dy) + (dx * dx));
        var dist = MathF.Sqrt(distSq);
        var range = light.Falloff * StaticLightFactor;
        if (!(dist < range))
        {
            return;
        }

        // ((0.75 + 0.75 - 1.0) * dist + n·d) * (1 / (0.75 + 0.75)): a wrapped cosine, positive up to ~109° off the normal.
        var s = ((0.5 * dist) + (normal.Z * dz) + (normal.Y * dy) + (normal.X * dx)) * (1.0 / 1.5);
        if (!(s > 0.0))
        {
            return;
        }

        var k = distSq <= 1f ? s / dist : s / (distSq * dist);
        k *= (1.0 - (dist / range)) * light.Intensity;
        r += (float)Math.Min(k * light.Color.X, light.Color.X);
        g += (float)Math.Min(k * light.Color.Y, light.Color.Y);
        b += (float)Math.Min(k * light.Color.Z, light.Color.Z);
    }

    /// <summary>Clamp to [0, 1] and truncate to a byte, as retail stores the vertex colour.</summary>
    public static byte ToByte(float value) => value <= 0f || float.IsNaN(value) ? (byte)0 : value >= 1f ? (byte)255 : (byte)(value * 255.0);
}
