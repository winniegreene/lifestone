using System.Diagnostics.CodeAnalysis;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Dat;

/// <summary>
/// The user's retail end-of-retail DAT files, opened strictly read-only.
/// </summary>
/// <remarks>
/// <para>
/// Portal + cell are required; <c>client_highres.dat</c> and <c>client_local_English.dat</c> are optional. We
/// deliberately do not use <see cref="DatCollection"/>: it always opens <c>client_highres.dat</c>, and in read mode a
/// missing file is an error.
/// </para>
/// <para>
/// <b>Threading.</b> DatReaderWriter databases are not safe for concurrent reads. Every read goes through the
/// <c>TryGet*</c> helpers (or <see cref="Read{TResult}"/>), which serialise on <see cref="SyncRoot"/>, so callers may
/// decode on worker threads. Do not use <see cref="Portal"/>/<see cref="Cell"/>/<see cref="HighRes"/>/
/// <see cref="Local"/> directly from more than one thread.
/// </para>
/// <para>
/// Every database is opened with <see cref="DatAccessType.Read"/>, which makes DatReaderWriter open the file with
/// <c>FileMode.Open, FileAccess.Read</c> and map it read-only. Never change this to ReadWrite: in that mode
/// DatReaderWriter opens with <c>FileMode.OpenOrCreate</c> and may resize the file.
/// </para>
/// </remarks>
public sealed class DatSet : IDisposable
{
    /// <summary>
    /// The folder, at the root of a Lifestone checkout, that holds the user-supplied retail client files by default
    /// (<c>&lt;repo&gt;\client</c>; gitignored).
    /// </summary>
    public const string DefaultDirectoryName = "client";

    /// <summary>The file that marks the root of a Lifestone checkout, the folder <see cref="DefaultDirectoryName"/> sits in.</summary>
    public const string CheckoutMarkerFileName = "Lifestone.slnx";

    /// <summary>Environment variable that overrides the default folder (<see cref="ResolveDirectory(string?)"/>).</summary>
    public const string DirectoryEnvironmentVariable = "LIFESTONE_DAT_DIR";

    public const string PortalFileName = "client_portal.dat";
    public const string CellFileName = "client_cell_1.dat";
    public const string LocalFileName = "client_local_English.dat";
    public const string HighResFileName = "client_highres.dat";

    /// <summary>File id of the one Region record (Dereth).</summary>
    public const uint RegionId = 0x13000000u;

    private readonly Lazy<Region> _region;
    private readonly Lazy<TerrainRegion> _terrainRegion;
    private readonly Lazy<TerrainAtlasManifest> _terrainAtlas;
    private readonly Lazy<DerethSky> _sky;

    private DatSet(string directory, PortalDatabase portal, CellDatabase cell, LocalDatabase? local, PortalDatabase? highRes)
    {
        Directory = directory;
        Portal = portal;
        Cell = cell;
        Local = local;
        HighRes = highRes;
        Iterations = new DatIterations(
            portal.Iteration.CurrentIteration,
            cell.Iteration.CurrentIteration,
            local?.Iteration.CurrentIteration,
            highRes?.Iteration.CurrentIteration);

        // Shared, immutable, built once on first use from any thread.
        _region = new Lazy<Region>(
            () => TryGetPortal<Region>(RegionId, out var region)
                ? region
                : throw new InvalidDataException($"Region 0x{RegionId:X8} not found in {PortalFileName}."),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _terrainRegion = new Lazy<TerrainRegion>(() => TerrainRegion.FromRegion(Region), LazyThreadSafetyMode.ExecutionAndPublication);
        _terrainAtlas = new Lazy<TerrainAtlasManifest>(() => TerrainAtlasManifest.FromRegion(Region), LazyThreadSafetyMode.ExecutionAndPublication);
        _sky = new Lazy<DerethSky>(() => DerethSky.FromRegion(Region), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Lock that serialises every read from the underlying databases.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Iteration (version) of each opened database; part of every derived-data cache key.</summary>
    public DatIterations Iterations { get; }

    /// <summary>Directory the dats were opened from.</summary>
    public string Directory { get; }

    /// <summary><c>client_portal.dat</c> (models, textures, Region, tables).</summary>
    public PortalDatabase Portal { get; }

    /// <summary><c>client_cell_1.dat</c> (LandBlocks, LandBlockInfos, EnvCells).</summary>
    public CellDatabase Cell { get; }

    /// <summary><c>client_local_English.dat</c>, or null when the file is absent.</summary>
    public LocalDatabase? Local { get; }

    /// <summary>
    /// <c>client_highres.dat</c> (2,294 RenderSurfaces at end of retail), or null when the file is absent. Many
    /// SurfaceTextures list a high-res RenderSurface first; try those here before falling back to the portal.
    /// </summary>
    public PortalDatabase? HighRes { get; }

    /// <summary>
    /// Resolves the dat directory: the explicit argument (the game's <c>--dat-dir</c>), then <c>LIFESTONE_DAT_DIR</c>,
    /// then the <c>client</c> folder of the Lifestone checkout the process runs in, found by walking up from the current
    /// directory and from the program's folder (<see cref="FindDefaultDirectory"/>). Outside any checkout (a packaged
    /// build) it is the <c>client</c> folder next to the program. The folder is not required to exist:
    /// <see cref="Open"/> names it, and the remedy, when the dats are missing.
    /// </summary>
    public static string ResolveDirectory(string? directory = null) =>
        ResolveDirectory(
            directory,
            System.Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable),
            [System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory]);

    /// <summary>
    /// <see cref="ResolveDirectory(string?)"/> with its inputs given: <paramref name="directory"/>, else
    /// <paramref name="environmentValue"/>, else <see cref="FindDefaultDirectory"/> over <paramref name="searchRoots"/>,
    /// else the <c>client</c> folder in the last search root (the program's folder).
    /// </summary>
    public static string ResolveDirectory(string? directory, string? environmentValue, IReadOnlyList<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            return Path.GetFullPath(directory);
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return Path.GetFullPath(environmentValue);
        }

        if (searchRoots.Count == 0)
        {
            throw new ArgumentException("At least one search root is needed.", nameof(searchRoots));
        }

        return FindDefaultDirectory(searchRoots) ?? Path.Combine(Path.GetFullPath(searchRoots[^1]), DefaultDirectoryName);
    }

    /// <summary>
    /// The default dat folder: <c>&lt;checkout&gt;\client</c>, where a checkout is a folder holding
    /// <see cref="CheckoutMarkerFileName"/>, found by walking up from each of <paramref name="searchRoots"/> in turn. The
    /// first checkout whose <c>client</c> folder holds the required dats wins, so a git worktree created inside a checkout
    /// uses the enclosing checkout's dats; when none holds them, the nearest checkout's <c>client</c> folder (where they
    /// belong). Null when no search root lies inside a checkout.
    /// </summary>
    public static string? FindDefaultDirectory(IEnumerable<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        string? nearest = null;
        foreach (var root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            for (var dir = new DirectoryInfo(Path.GetFullPath(root)); dir is not null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, CheckoutMarkerFileName)))
                {
                    continue;
                }

                var candidate = Path.Combine(dir.FullName, DefaultDirectoryName);
                if (HasRequiredFiles(candidate))
                {
                    return candidate;
                }

                nearest ??= candidate;
            }
        }

        return nearest;
    }

    /// <summary>True when the required dat files (portal + cell) exist in <paramref name="directory"/>.</summary>
    public static bool Exists(string? directory = null) => HasRequiredFiles(ResolveDirectory(directory));

    private static bool HasRequiredFiles(string directory) =>
        File.Exists(Path.Combine(directory, PortalFileName)) && File.Exists(Path.Combine(directory, CellFileName));

    /// <summary>Opens the dat set read-only.</summary>
    /// <param name="directory">Directory holding the retail dats; see <see cref="ResolveDirectory(string?)"/>.</param>
    /// <exception cref="FileNotFoundException">A required dat file is missing.</exception>
    public static DatSet Open(string? directory = null)
    {
        var dir = ResolveDirectory(directory);
        var portalPath = RequireFile(dir, PortalFileName);
        var cellPath = RequireFile(dir, CellFileName);
        var localPath = Path.Combine(dir, LocalFileName);
        var highResPath = Path.Combine(dir, HighResFileName);

        PortalDatabase? portal = null;
        CellDatabase? cell = null;
        LocalDatabase? local = null;
        try
        {
            portal = new PortalDatabase(o => ReadOnlyUncached(o, portalPath));
            cell = new CellDatabase(o => ReadOnlyUncached(o, cellPath));
            local = File.Exists(localPath) ? new LocalDatabase(o => ReadOnlyUncached(o, localPath)) : null;
            var highRes = File.Exists(highResPath) ? new PortalDatabase(o => ReadOnlyUncached(o, highResPath)) : null;
            return new DatSet(dir, portal, cell, local, highRes);
        }
        catch
        {
            local?.Dispose();
            cell?.Dispose();
            portal?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Read-only, and no record cache: DatReaderWriter's default (<see cref="FileCachingStrategy.OnDemand"/>) keeps every
    /// record ever read, which would pin hundreds of MB while streaming the world. Callers keep their own bounded caches.
    /// The B-tree index is still cached on demand (small, and makes repeated seeks cheap).
    /// </summary>
    private static void ReadOnlyUncached(DatDatabaseOptions options, string path)
    {
        options.FilePath = path;
        options.AccessType = DatAccessType.Read;
        options.FileCachingStrategy = FileCachingStrategy.Never;
        options.IndexCachingStrategy = IndexCachingStrategy.OnDemand;
    }

    /// <summary>Raw record bytes from <c>client_portal.dat</c> (never cached).</summary>
    public byte[]? TryGetPortalBytes(uint fileId) => Read(d => d.Portal.TryGetFileBytes(fileId, out var bytes) ? bytes : null);

    /// <summary>Raw record bytes from <c>client_cell_1.dat</c> (never cached).</summary>
    public byte[]? TryGetCellBytes(uint fileId) => Read(d => d.Cell.TryGetFileBytes(fileId, out var bytes) ? bytes : null);

    /// <summary>Raw record bytes from <c>client_highres.dat</c>, or null when absent (never cached).</summary>
    public byte[]? TryGetHighResBytes(uint fileId) =>
        Read(d => d.HighRes is not null && d.HighRes.TryGetFileBytes(fileId, out var bytes) ? bytes : null);

    /// <summary>Every file id of type <typeparamref name="T"/> in <c>client_portal.dat</c>.</summary>
    public IReadOnlyList<uint> GetAllPortalIds<T>() where T : IDBObj => Read(d => d.Portal.GetAllIdsOfType<T>().ToArray());

    /// <summary>Every file id of type <typeparamref name="T"/> in <c>client_highres.dat</c> (empty when absent).</summary>
    public IReadOnlyList<uint> GetAllHighResIds<T>() where T : IDBObj =>
        Read(d => d.HighRes?.GetAllIdsOfType<T>().ToArray() ?? []);

    /// <summary>Every file id of type <typeparamref name="T"/> in <c>client_cell_1.dat</c>.</summary>
    public IReadOnlyList<uint> GetAllCellIds<T>() where T : IDBObj => Read(d => d.Cell.GetAllIdsOfType<T>().ToArray());

    /// <summary>Runs <paramref name="read"/> while holding <see cref="SyncRoot"/>.</summary>
    public TResult Read<TResult>(Func<DatSet, TResult> read)
    {
        lock (SyncRoot)
        {
            return read(this);
        }
    }

    /// <summary>Reads a record from <c>client_portal.dat</c>.</summary>
    public bool TryGetPortal<T>(uint fileId, [NotNullWhen(true)] out T? value) where T : IDBObj
    {
        lock (SyncRoot)
        {
            var found = Portal.TryGet<T>(fileId, out var result);
            value = found ? result : default;
            return found;
        }
    }

    /// <summary>Reads a record from <c>client_highres.dat</c>; false when the record or the file is absent.</summary>
    public bool TryGetHighRes<T>(uint fileId, [NotNullWhen(true)] out T? value) where T : IDBObj
    {
        lock (SyncRoot)
        {
            if (HighRes is not null)
            {
                var found = HighRes.TryGet<T>(fileId, out var result);
                value = found ? result : default;
                return found;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Reads a portal-type record from <c>client_highres.dat</c> if present there, else from the portal.</summary>
    public bool TryGetHighResOrPortal<T>(uint fileId, [NotNullWhen(true)] out T? value) where T : IDBObj =>
        TryGetHighRes(fileId, out value) || TryGetPortal(fileId, out value);

    /// <summary>Reads a record from <c>client_cell_1.dat</c>.</summary>
    public bool TryGetCell<T>(uint fileId, [NotNullWhen(true)] out T? value) where T : IDBObj
    {
        lock (SyncRoot)
        {
            var found = Cell.TryGet<T>(fileId, out var result);
            value = found ? result : default;
            return found;
        }
    }

    /// <summary>Reads a record from <c>client_local_English.dat</c>; false when the record or the file is absent.</summary>
    public bool TryGetLocal<T>(uint fileId, [NotNullWhen(true)] out T? value) where T : IDBObj
    {
        lock (SyncRoot)
        {
            if (Local is not null)
            {
                var found = Local.TryGet<T>(fileId, out var result);
                value = found ? result : default;
                return found;
            }
        }

        value = default;
        return false;
    }

    /// <summary>The Region record (0x13000000), read once and cached.</summary>
    public Region Region => _region.Value;

    /// <summary>Engine-agnostic view of the Region's terrain tables (LandHeightTable, terrain types).</summary>
    public TerrainRegion TerrainRegion => _terrainRegion.Value;

    /// <summary>Terrain textures, alpha maps and road maps in shader layer order (shared by every streaming worker).</summary>
    public TerrainAtlasManifest TerrainAtlas => _terrainAtlas.Value;

    /// <summary>Region sky description, calendar and lighting evaluator (shared instance).</summary>
    public DerethSky Sky => _sky.Value;

    /// <summary>Reads a LandBlock record by full file id (0xXXYYFFFF).</summary>
    public LandBlock? GetLandBlock(uint landBlockId) =>
        TryGetCell<LandBlock>(landBlockId, out var landBlock) ? landBlock : null;

    /// <summary>Reads a LandBlock by landblock coordinate.</summary>
    public LandBlock? GetLandBlock(LandblockCoord coord) => GetLandBlock(coord.LandBlockId);

    /// <summary>Reads the terrain (heights + terrain words) of a landblock, or null when it does not exist.</summary>
    public LandblockTerrain? GetLandblockTerrain(LandblockCoord coord)
    {
        var landBlock = GetLandBlock(coord);
        return landBlock is null ? null : LandblockTerrain.FromLandBlock(coord, landBlock);
    }

    public void Dispose()
    {
        HighRes?.Dispose();
        Local?.Dispose();
        Cell.Dispose();
        Portal.Dispose();
    }

    private static string RequireFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required retail data file '{fileName}' not found in '{directory}'. " +
                $"Copy your own Asheron's Call client files ({PortalFileName}, {CellFileName}) there, or set " +
                $"{DirectoryEnvironmentVariable} to the folder that holds them (the default is the '{DefaultDirectoryName}' " +
                $"folder of the Lifestone checkout, next to {CheckoutMarkerFileName}).", path);
        }

        return path;
    }
}
