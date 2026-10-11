using System.Collections.Concurrent;
using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.World;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>Shared dats, cell structures and loaded landblock interiors for the interiors tests (real dats).</summary>
public sealed class InteriorsFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());
    private readonly Lazy<CellStructureLibrary> _structures;
    private readonly Lazy<GfxObjMeshCache> _meshes;
    private readonly ConcurrentDictionary<LandblockCoord, Lazy<LandblockInteriors>> _blocks = new();
    private readonly ConcurrentDictionary<uint, Lazy<IReadOnlyList<ModelLight>>> _lights = new();

    public InteriorsFixture()
    {
        _structures = new Lazy<CellStructureLibrary>(() => new CellStructureLibrary(Dats));
        _meshes = new Lazy<GfxObjMeshCache>(() => new GfxObjMeshCache(Dats));
    }

    public static readonly LandblockCoord Holtburg = new(0xA9, 0xB4);

    public DatSet Dats => _dats.Value;

    public CellStructureLibrary Structures => _structures.Value;

    /// <summary>A landblock's interiors as the game loads them (cells and building doorways).</summary>
    public LandblockInteriors Block(LandblockCoord coord) =>
        _blocks.GetOrAdd(coord, c => new Lazy<LandblockInteriors>(() =>
        {
            var cells = LandblockCellSet.Load(Structures, c);
            var statics = LandblockStatics.Read(Dats, c);
            var buildings = statics?.Buildings.Where(b => b.Portals.Count > 0).Select(b => BuildingPortalGeometry.Load(Dats, b)).ToArray()
                ?? [];
            return new LandblockInteriors(cells, buildings);
        })).Value;

    /// <summary>A registry with the given landblocks loaded.</summary>
    public InteriorRegistry Registry(params LandblockCoord[] coords)
    {
        var registry = new InteriorRegistry();
        foreach (var coord in coords)
        {
            registry.Add(Block(coord));
        }

        return registry;
    }

    /// <summary>The Setup lights of a model (cached).</summary>
    public IReadOnlyList<ModelLight> LightsOfModel(uint modelId) =>
        _lights.GetOrAdd(modelId, id => new Lazy<IReadOnlyList<ModelLight>>(() =>
            ModelAssembly.Load(Dats, id, _meshes.Value)?.Lights ?? [])).Value;

    /// <summary>The static lights of every cell of a landblock, by cell id.</summary>
    public Dictionary<uint, IReadOnlyList<CellLight>> CellLights(LandblockCoord coord) =>
        Block(coord).Cells.Cells.ToDictionary(c => c.Id, c => CellStaticLighting.LightsOf(c, LightsOfModel));

    /// <summary>The exact terrain height of a landblock-local point.</summary>
    public float TerrainHeight(LandblockCoord coord, float x, float y) =>
        new TerrainSampler(Dats.GetLandblockTerrain(coord)!, Dats.TerrainRegion.LandHeightTable).GetHeight(x, y);

    /// <summary>A landblock-local point as a global position.</summary>
    public static GlobalPosition Global(LandblockCoord coord, Vector3 local)
    {
        var origin = AcCoordinates.LandblockOrigin(coord);
        return new GlobalPosition(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
    }

    /// <summary>A level camera looking along a compass heading.</summary>
    public static ViewFrustum Looking(double headingDegrees, double pitchDegrees = 0.0)
    {
        var h = headingDegrees * Math.PI / 180.0;
        var p = pitchDegrees * Math.PI / 180.0;
        var forward = new Vector3((float)(Math.Sin(h) * Math.Cos(p)), (float)(Math.Cos(h) * Math.Cos(p)), (float)Math.Sin(p));
        return ViewFrustum.FromCamera(forward, Vector3.UnitZ, 60f, 16f / 9f);
    }

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}
