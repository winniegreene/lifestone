using System.Collections.Concurrent;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Game.World;

/// <summary>
/// Exact retail ground height under a global position, from <see cref="TerrainSampler"/> (retail
/// <c>CLandCell::find_terrain_poly</c> + <c>Plane::set_height</c>, bit-identical planes). Thread-safe.
/// </summary>
/// <remarks>
/// Samplers come from the terrain layer as landblocks stream in (<see cref="Register"/>, on a worker) and are dropped
/// when they stream out; a query outside the loaded set builds the landblock's sampler on demand (one LandBlock read)
/// and keeps it in a small overflow set.
/// </remarks>
public sealed class GroundHeights
{
    private const int MaxOnDemand = 32;

    private readonly DatSet _dats;
    private readonly ConcurrentDictionary<LandblockCoord, TerrainSampler> _streamed = new();
    private readonly ConcurrentDictionary<LandblockCoord, TerrainSampler?> _onDemand = new();

    public GroundHeights(DatSet dats) => _dats = dats;

    /// <summary>Samplers of streamed-in landblocks.</summary>
    public int Count => _streamed.Count;

    /// <summary>Adds a streamed landblock's sampler (any thread).</summary>
    public void Register(TerrainSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        _streamed[sampler.Coord] = sampler;
    }

    /// <summary>Drops a landblock's sampler when it streams out.</summary>
    public void Unregister(LandblockCoord coord) => _streamed.TryRemove(coord, out _);

    /// <summary>The ground height at a global position, or false off the map or where the dat has no landblock.</summary>
    public bool TryGetHeight(GlobalPosition position, out float height)
    {
        height = 0f;
        if (AcCoordinates.LandblockAt(position) is not { } coord || SamplerFor(coord) is not { } sampler)
        {
            return false;
        }

        var x = (float)(position.X - (coord.X * AcCoordinates.LandblockLength));
        var y = (float)(position.Y - (coord.Y * AcCoordinates.LandblockLength));

        // GetHeight wants 0 <= x, y < 192; float rounding of a position a hair below the next landblock can give 192.
        x = Math.Clamp(x, 0f, MathF.BitDecrement(192f));
        y = Math.Clamp(y, 0f, MathF.BitDecrement(192f));
        height = sampler.GetHeight(x, y);
        return true;
    }

    private TerrainSampler? SamplerFor(LandblockCoord coord)
    {
        if (_streamed.TryGetValue(coord, out var sampler))
        {
            return sampler;
        }

        if (_onDemand.Count > MaxOnDemand)
        {
            _onDemand.Clear();
        }

        return _onDemand.GetOrAdd(coord, c => _dats.GetLandblockTerrain(c) is { } terrain
            ? new TerrainSampler(terrain, _dats.TerrainRegion.LandHeightTable)
            : null);
    }
}
