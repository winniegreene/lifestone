using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Rendering;

/// <summary>
/// M5 7 P7d (D139 row e): trims the render caches each time the player arrives from portal space, so a long run's texture and mesh memory stays
/// that of the place it is in and the one it came from, not everything it ever saw up to the budgets.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Round 13's soak: the working set stayed flat in the den and stepped up 80 to 105 MB at each town trip (about 2.08 GB after seven
/// trips). The caches keep idle entries up to their least-recently-used budgets (768 MB of textures, 256 MB of meshes,
/// <c>GameOptions.TextureBudgetBytes</c> and <c>MeshBudgetBytes</c>), and ACE re-creates Holtburg's townsfolk with new ids and new dress every
/// visit, so each trip added textures the budget had room to keep; the cache's idle trim had no caller.</para>
/// <para><b>The rule.</b> On every arrival (the portal-space machine's state 6 ending, the world shown a second earlier), the idle entries not
/// used since the previous arrival leave and are freed; then a new mark is taken. What the place being left used was released during the trip, after
/// that mark, so it stays for a quick way back; what an earlier place used goes. Entries in use (the place arrived at) never go. The first arrival
/// only marks. One INFO line per trim (category <c>render</c>) with the counts and bytes before and after; <see cref="Trims"/> and
/// <see cref="BytesFreed"/> feed the soak line.</para>
/// <para>Engine-free; the game calls <see cref="OnArrival"/> on its main thread (the presenter's state change).</para>
/// </remarks>
public sealed class ArrivalCacheTrim
{
    /// <summary>The log category of the trim's line.</summary>
    public const string Category = "render";

    private static long s_trims;
    private static long s_bytesFreed;

    private readonly IRenderCacheTrim _caches;
    private readonly ILogSink _log;
    private RenderCacheMark? _mark;

    /// <summary>Creates the policy over <paramref name="caches"/>.</summary>
    public ArrivalCacheTrim(IRenderCacheTrim caches, ILogSink log)
    {
        _caches = caches ?? throw new ArgumentNullException(nameof(caches));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Trims run in this process (every instance; the soak line's <c>cache_trims</c>).</summary>
    public static long Trims => Interlocked.Read(ref s_trims);

    /// <summary>Texture and mesh bytes the trims freed in this process.</summary>
    public static long BytesFreed => Interlocked.Read(ref s_bytesFreed);

    /// <summary>The player arrived: trims what was not used since the previous arrival (none on the first), then marks.</summary>
    /// <returns>The trim's counts, or null on the first arrival.</returns>
    public RenderCacheTrimResult? OnArrival()
    {
        RenderCacheTrimResult? result = null;
        if (_mark is { } mark)
        {
            var r = _caches.TrimUnusedSince(mark);
            result = r;
            Interlocked.Increment(ref s_trims);
            Interlocked.Add(ref s_bytesFreed, r.Textures.BytesFreed + r.Meshes.BytesFreed);
            _log.Info(
                Category,
                "render caches trimmed on arrival: idle entries unused since the last arrival freed",
                new LogField("tex_n", $"{r.Textures.CountBefore}->{r.Textures.CountAfter}"),
                new LogField("tex_mb", $"{Mb(r.Textures.BytesBefore)}->{Mb(r.Textures.BytesAfter)}"),
                new LogField("mesh_n", $"{r.Meshes.CountBefore}->{r.Meshes.CountAfter}"),
                new LogField("mesh_mb", $"{Mb(r.Meshes.BytesBefore)}->{Mb(r.Meshes.BytesAfter)}"),
                new LogField("materials", $"{r.Materials.CountBefore}->{r.Materials.CountAfter}"),
                new LogField("freed_mb", Mb(r.Textures.BytesFreed + r.Meshes.BytesFreed)),
                new LogField("trims", Trims));
        }
        else
        {
            _log.Info(Category, "render caches: first arrival marked; the next arrival trims what is unused since");
        }

        _mark = _caches.Mark();
        return result;
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}
