using System.Collections.Concurrent;
using Lifestone.Data.Dat;
using DatAnimation = DatReaderWriter.DBObjs.Animation;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;

namespace Lifestone.Data.Animation;

/// <summary>Raw record access through <see cref="DatSet"/> (holds the dat lock for the read).</summary>
internal static class DatRaw
{
    /// <summary>The raw bytes of a <c>client_portal.dat</c> record (starting with its id), or null when absent.</summary>
    public static byte[]? Portal(DatSet dats, uint id) =>
        dats.Read(d => d.Portal.TryGetFileBytes(id, out byte[]? bytes, true) ? bytes : null);

    /// <summary>All portal ids of the DatReaderWriter type <typeparamref name="T"/> (used for the id range only).</summary>
    public static IReadOnlyList<uint> PortalIds<T>(DatSet dats) where T : DatReaderWriter.Lib.IO.IDBObj =>
        dats.Read(d => d.Portal.GetAllIdsOfType<T>().OrderBy(i => i).ToArray());
}

/// <summary>
/// Thread-safe cache of decoded Animations (0x03) and MotionTables (0x09) read from the user's dats.
/// Animations are decoded with <see cref="AnimationData.Decode"/> (not DatReaderWriter, see <see cref="AnimHookDecoder"/>).
/// </summary>
public sealed class DatAnimationSource(DatSet dats) : IAnimationSource
{
    private readonly ConcurrentDictionary<uint, AnimationData?> _animations = new();
    private readonly ConcurrentDictionary<uint, MotionTableData?> _motionTables = new();

    public DatSet Dats { get; } = dats;

    public AnimationData? GetAnimation(uint animId) =>
        _animations.GetOrAdd(animId, id => DatRaw.Portal(Dats, id) is { } raw ? AnimationData.Decode(id, raw) : null);

    /// <summary>The MotionTable, or null when absent.</summary>
    public MotionTableData? GetMotionTable(uint motionTableId) =>
        _motionTables.GetOrAdd(motionTableId, id => Dats.TryGetPortal<DatMotionTable>(id, out var table) ? MotionTableData.FromDat(table) : null);

    /// <summary>All Animation ids in the portal.</summary>
    public IReadOnlyList<uint> AllAnimationIds() => DatRaw.PortalIds<DatAnimation>(Dats);

    /// <summary>All MotionTable ids in the portal.</summary>
    public IReadOnlyList<uint> AllMotionTableIds() => DatRaw.PortalIds<DatMotionTable>(Dats);
}
