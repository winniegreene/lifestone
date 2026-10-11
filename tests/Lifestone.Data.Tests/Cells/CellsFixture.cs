using Lifestone.Data.Dat;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

/// <summary>One read-only <see cref="DatSet"/> plus a shared <see cref="CellStructureLibrary"/> per test class.</summary>
public sealed class CellsFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());
    private readonly Lazy<CellStructureLibrary> _structures;

    public CellsFixture()
    {
        _structures = new Lazy<CellStructureLibrary>(() => new CellStructureLibrary(Dats));
    }

    public DatSet Dats => _dats.Value;

    public CellStructureLibrary Structures => _structures.Value;

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}
