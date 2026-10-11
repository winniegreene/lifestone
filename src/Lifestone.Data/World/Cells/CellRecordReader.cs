using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Reads cell-dat records as raw bytes under the <see cref="DatSet"/> lock and unpacks them outside it.
/// </summary>
/// <remarks>
/// Going through raw bytes (<c>DatDatabase.TryGetFileBytes</c>, never cached) instead of <c>DatSet.TryGetCell</c>
/// keeps 734,976 EnvCells out of DatReaderWriter's unbounded OnDemand object cache, lets decoding run in parallel,
/// and lets every record be checked for byte-exact consumption.
/// </remarks>
internal static class CellRecordReader
{
    public static byte[]? ReadCellBytes(DatSet dats, uint fileId) =>
        dats.TryGetCellBytes(fileId);

    public static byte[]? ReadPortalBytes(DatSet dats, uint fileId) =>
        dats.TryGetPortalBytes(fileId);

    /// <summary>
    /// Unpacks <paramref name="bytes"/> into a DatReaderWriter record and requires it to consume every byte and to carry
    /// <paramref name="expectedId"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The record is malformed, has trailing bytes or the wrong id.</exception>
    public static T UnpackExact<T>(byte[] bytes, uint expectedId) where T : DBObj, new()
    {
        var reader = new DatBinReader(bytes);
        var record = new T();
        bool ok;
        try
        {
            ok = record.Unpack(reader);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or EndOfStreamException or ArgumentException)
        {
            throw new InvalidDataException($"{typeof(T).Name} 0x{expectedId:X8}: truncated or malformed record ({ex.Message}).", ex);
        }

        if (!ok)
        {
            throw new InvalidDataException($"{typeof(T).Name} 0x{expectedId:X8}: unpack failed.");
        }

        if (reader.Offset != bytes.Length)
        {
            throw new InvalidDataException(
                $"{typeof(T).Name} 0x{expectedId:X8}: consumed {reader.Offset} of {bytes.Length} bytes.");
        }

        if (record.HeaderFlags.HasFlag(DatReaderWriter.Enums.DBObjHeaderFlags.HasId) && record.Id != expectedId)
        {
            throw new InvalidDataException($"{typeof(T).Name} 0x{expectedId:X8}: record carries id 0x{record.Id:X8}.");
        }

        return record;
    }

    /// <summary>
    /// EnvCell ids that exist in the cell dat for a landblock, ascending, found by a B-tree range walk
    /// (<c>0xXXYY0100-0xXXYYFFFD</c>).
    /// </summary>
    public static List<uint> EnvCellIds(DatSet dats, LandblockCoord coord)
    {
        var first = CellIds.Make(coord, CellIds.FirstEnvCell);
        var last = CellIds.Make(coord, CellIds.LastEnvCell);
        return dats.Read(d => d.Cell.Tree.GetFilesInRange(first, last).Select(f => f.Id).OrderBy(id => id).ToList());
    }
}
