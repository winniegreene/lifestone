using System.Collections.Concurrent;
using Lifestone.Data.Dat;
using Lifestone.Data.World.Cells;
using DatEnvironment = DatReaderWriter.DBObjs.Environment;
using DatGfxObj = DatReaderWriter.DBObjs.GfxObj;

namespace Lifestone.Data.Physics.Geometry;

/// <summary>
/// Thread-safe, lazily filled store of collision shapes: GfxObj physics BSPs and the room shapes' physics BSPs.
/// Replaces ACE's static <c>BSPCache</c>, <c>GfxObjCache</c>, <c>PolygonCache</c> and <c>VertexCache</c>.
/// </summary>
/// <remarks>
/// <para>Records are read as raw bytes under the <see cref="DatSet"/> lock and decoded outside it, byte-exact
/// (<c>CellRecordReader</c>), bypassing DatReaderWriter's object cache. Shapes are kept for the library's lifetime:
/// 772 Environments, and GfxObjs on demand.</para>
/// <para>Concurrent first requests for the same record decode it once (<see cref="Lazy{T}"/>).</para>
/// </remarks>
public sealed class PhysicsGeometryLibrary
{
    private readonly DatSet _dats;
    private readonly ConcurrentDictionary<uint, Lazy<GfxObjShape?>> _gfxObjs = new();
    private readonly ConcurrentDictionary<uint, Lazy<CellStructPhysics[]?>> _environments = new();

    /// <summary>Creates an empty library over <paramref name="dats"/>.</summary>
    public PhysicsGeometryLibrary(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        _dats = dats;
    }

    /// <summary>
    /// The GfxObj's collision shape, or null when the GfxObj does not exist or has no physics (draw-only).
    /// </summary>
    /// <exception cref="InvalidDataException">The GfxObj is malformed.</exception>
    public GfxObjPhysics? GetGfxObj(uint gfxObjId) => GetGfxObjShape(gfxObjId)?.Physics;

    /// <summary>
    /// The GfxObj's bounds, spheres and (when it has one) collision shape, or null when the GfxObj does not exist. Static
    /// objects find their cells with the bounds of parts that only draw too.
    /// </summary>
    /// <exception cref="InvalidDataException">The GfxObj is malformed.</exception>
    public GfxObjShape? GetGfxObjShape(uint gfxObjId) =>
        _gfxObjs.GetOrAdd(
            gfxObjId,
            id => new Lazy<GfxObjShape?>(() => LoadGfxObj(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Every struct's collision shape in an Environment, in array order, or null when it does not exist.</summary>
    /// <exception cref="InvalidDataException">The Environment is malformed.</exception>
    public IReadOnlyList<CellStructPhysics>? GetEnvironment(uint environmentId) =>
        _environments.GetOrAdd(
            environmentId,
            id => new Lazy<CellStructPhysics[]?>(() => LoadEnvironment(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>The collision shape of the struct an EnvCell uses (retail <c>CEnvironment::get_cellstruct</c>).</summary>
    /// <exception cref="InvalidDataException">The Environment is missing or malformed, or the index is out of range.</exception>
    public CellStructPhysics GetCellStruct(uint environmentId, ushort structIndex)
    {
        var structs = GetEnvironment(environmentId)
            ?? throw new InvalidDataException($"Environment 0x{environmentId:X8} not found in {DatSet.PortalFileName}.");
        return structIndex < structs.Count
            ? structs[structIndex]
            : throw new InvalidDataException($"Environment 0x{environmentId:X8} has {structs.Count} cell structs; index {structIndex} requested.");
    }

    /// <summary>Decodes a GfxObj record's collision shape from its raw bytes (null when it has no physics).</summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or the wrong id.</exception>
    public static GfxObjPhysics? DecodeGfxObj(uint gfxObjId, byte[] bytes) =>
        GfxObjPhysics.FromDat(gfxObjId, CellRecordReader.UnpackExact<DatGfxObj>(bytes, gfxObjId));

    /// <summary>Decodes an Environment record's struct shapes from its raw bytes.</summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or the wrong id.</exception>
    public static CellStructPhysics[] DecodeEnvironment(uint environmentId, byte[] bytes)
    {
        var environment = CellRecordReader.UnpackExact<DatEnvironment>(bytes, environmentId);
        var result = new CellStructPhysics[environment.Cells.Count];
        var position = 0;
        foreach (var (key, cellStruct) in environment.Cells)
        {
            if (key != position)
            {
                // Retail CEnvironment::get_cellstruct indexes the array; DatReaderWriter keys by the stored id.
                throw new InvalidDataException($"Environment 0x{environmentId:X8}: struct stored with id {key} at position {position}.");
            }

            result[position] = CellStructPhysics.FromDat(environmentId, (ushort)position, cellStruct);
            position++;
        }

        return result;
    }

    private GfxObjShape? LoadGfxObj(uint id)
    {
        var bytes = CellRecordReader.ReadPortalBytes(_dats, id);
        return bytes is null ? null : GfxObjShape.FromDat(id, CellRecordReader.UnpackExact<DatGfxObj>(bytes, id));
    }

    private CellStructPhysics[]? LoadEnvironment(uint id)
    {
        var bytes = CellRecordReader.ReadPortalBytes(_dats, id);
        return bytes is null ? null : DecodeEnvironment(id, bytes);
    }
}
