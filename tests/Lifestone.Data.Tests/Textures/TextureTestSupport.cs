using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>Shared helpers for the texture tests: uncached reads from one specific dat.</summary>
internal static class TextureTestSupport
{
    /// <summary>All RenderSurface ids in the portal (or highres when <paramref name="highRes"/>).</summary>
    public static uint[] RenderSurfaceIds(DatSet dats, bool highRes) =>
        dats.Read(d => Db(d, highRes)?.GetAllIdsOfType<RenderSurface>().ToArray() ?? []);

    /// <summary>Reads a record from one dat without touching DatReaderWriter's file cache.</summary>
    public static T? ReadUncached<T>(DatSet dats, uint id, bool highRes = false)
        where T : class, IDBObj =>
        dats.Read(d => Db(d, highRes) is { } db && db.TryGet<T, T>(id, static x => x, out var v) ? v : null);

    /// <summary>Default palette as ARGB for an indexed RenderSurface.</summary>
    public static PaletteData Palette(TextureLoader loader, RenderSurface rs) =>
        loader.GetPalette(rs.DefaultPaletteId) ?? throw new Xunit.Sdk.XunitException($"palette 0x{rs.DefaultPaletteId:X8} missing");

    private static DatDatabase? Db(DatSet d, bool highRes) => highRes ? d.HighRes : d.Portal;
}
