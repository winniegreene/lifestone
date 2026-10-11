using Lifestone.Client.Objects;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Objects;

/// <summary>One read-only DatSet, model library and scenery generator shared by the objects tests (user's dats).</summary>
public sealed class ObjectsFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());
    private readonly Lazy<ObjectModelLibrary> _library;
    private readonly Lazy<SceneryGenerator> _scenery;

    public ObjectsFixture()
    {
        _library = new Lazy<ObjectModelLibrary>(() => new ObjectModelLibrary(Dats, new GfxObjMeshCache(Dats)));
        _scenery = new Lazy<SceneryGenerator>(() => SceneryGenerator.FromDats(Dats));
    }

    public DatSet Dats => _dats.Value;

    public ObjectModelLibrary Library => _library.Value;

    public SceneryGenerator Scenery => _scenery.Value;

    /// <summary>The landblock's full object set, as the game builds it.</summary>
    public LandblockObjectSet Build(LandblockCoord coord, WorldObjectKinds kinds = WorldObjectKinds.All) =>
        LandblockObjectSet.Build(coord, LandblockStatics.Read(Dats, coord), Scenery.Generate(coord).Placements, Library, kinds);

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}
