using System.Collections.Concurrent;
using Lifestone.Data.Dat;
using DatEnvironment = DatReaderWriter.DBObjs.Environment;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Thread-safe, lazily filled store of decoded Environments (0x0D room-shape libraries).
/// </summary>
/// <remarks>
/// <para>
/// There are only 772 Environments (769 referenced by EnvCells, 3,145 distinct structs in use), so they are kept for
/// the lifetime of the library. Records are read as raw bytes through <see cref="DatSet.Read{TResult}"/> and must
/// decode byte-exact; DatReaderWriter's own object cache is bypassed.
/// </para>
/// <para>Concurrent first requests for the same Environment decode it once (<see cref="Lazy{T}"/>).</para>
/// </remarks>
public sealed class CellStructureLibrary
{
    private readonly DatSet _dats;
    private readonly ConcurrentDictionary<uint, Lazy<CellStructure[]?>> _environments = new();

    private CellStructureLibrary()
    {
        _dats = null!;
    }

    public CellStructureLibrary(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        _dats = dats;
    }

    /// <summary>A library holding only the given structs (synthetic unit tests: <see cref="Dats"/> is null, so no environment outside <paramref name="structs"/> loads).</summary>
    internal static CellStructureLibrary FromStructures(IEnumerable<CellStructure> structs)
    {
        var library = new CellStructureLibrary();
        foreach (var group in structs.GroupBy(c => c.EnvironmentId))
        {
            var ordered = group.OrderBy(c => c.Index).ToArray();
            library._environments[group.Key] = new Lazy<CellStructure[]?>(ordered);
        }

        return library;
    }

    public DatSet Dats => _dats;

    /// <summary>Number of Environments decoded so far.</summary>
    public int LoadedEnvironmentCount => _environments.Count(kv => kv.Value.IsValueCreated);

    /// <summary>All structs of an Environment in array order, or null when the Environment does not exist.</summary>
    /// <exception cref="InvalidDataException">The Environment is malformed.</exception>
    public IReadOnlyList<CellStructure>? GetEnvironment(uint environmentId) =>
        _environments.GetOrAdd(
            environmentId,
            id => new Lazy<CellStructure[]?>(() => Load(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>
    /// The struct an EnvCell uses (retail <c>CEnvironment::get_cellstruct(index)</c>).
    /// </summary>
    /// <exception cref="InvalidDataException">The Environment is missing or malformed, or the index is out of range.</exception>
    public CellStructure Get(uint environmentId, ushort structIndex)
    {
        var structs = GetEnvironment(environmentId)
            ?? throw new InvalidDataException($"Environment 0x{environmentId:X8} not found in {DatSet.PortalFileName}.");
        return structIndex < structs.Count
            ? structs[structIndex]
            : throw new InvalidDataException($"Environment 0x{environmentId:X8} has {structs.Count} cell structs; index {structIndex} requested.");
    }

    /// <summary>The struct of an EnvCell.</summary>
    public CellStructure Get(EnvCellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Get(cell.EnvironmentId, cell.CellStructureIndex);
    }

    private CellStructure[]? Load(uint environmentId)
    {
        var bytes = CellRecordReader.ReadPortalBytes(_dats, environmentId);
        return bytes is null ? null : Decode(environmentId, bytes);
    }

    /// <summary>Decodes raw Environment bytes into its structs.</summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or the wrong id.</exception>
    public static CellStructure[] Decode(uint environmentId, byte[] bytes)
    {
        var env = CellRecordReader.UnpackExact<DatEnvironment>(bytes, environmentId);
        var result = new CellStructure[env.Cells.Count];
        var position = 0;
        foreach (var (key, cellStruct) in env.Cells)
        {
            if (key != position)
            {
                // Retail CEnvironment::get_cellstruct indexes the array; DatReaderWriter keys by the stored id.
                throw new InvalidDataException($"Environment 0x{environmentId:X8}: struct stored with id {key} at position {position}.");
            }

            result[position] = CellStructure.FromDat(environmentId, (ushort)position, cellStruct);
            position++;
        }

        return result;
    }
}
