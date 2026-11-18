using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.WorldWalk;

// The world walk (docs/world-walk.md). Exit codes: 0 done, 1 a compare found differences, 2 a bad command line, 3 stopped by name (a cap, or a landblock's failure that ends the run).
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("the world walk's Light mode is written for this project's Windows machine");
    return 2;
}

WorldWalkOptions options;
try
{
    options = WorldWalkOptions.Parse(args);
}
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

return options.Verb switch
{
    "compare" => Compare.Run(options),
    "map" => MapPage.Run(options),
    _ => Driver.Run(options),
};

/// <summary>Runs a verb's landblocks on the walking threads.</summary>
[SupportedOSPlatform("windows")]
internal static class Driver
{
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromMinutes(1);

    public static int Run(WorldWalkOptions options)
    {
        using var store = new RunStore(options.Out);
        using var limits = new MachineLimits(store.Folder, options.IgnoreClimb, store.Note);
        using var dats = DatSet.Open(options.DatDir);
        var context = new WalkContext(dats) { PredictiveSteering = options.PredictiveSteering, TraceLeg = options.TraceLeg, TraceFolder = store.Folder };
        if (options.Verb == "graph")
        {
            return WorldGraph.Run(options, store, limits, context);
        }

        var clock = Stopwatch.StartNew();
        var detours = options.Verb == "detour" ? Detour.ReadEdges(options.Other ?? throw new ArgumentException("detour needs --other=DIR")) : null;
        if (options.Verb == "rooms" && options.CensusRun is null && options.Landblocks is null)
        {
            store.Note("rooms needs --census=DIR");
            return 2;
        }

        if (options.Verb == "long" && options.GraphRun is null)
        {
            store.Note("long needs --graph=DIR");
            return 2;
        }

        if (options.Verb == "places" && options.WorldSql is null)
        {
            store.Note("places needs --world-sql=ZIP");
            return 2;
        }

        var book = options.Verb == "places" ? WorldPlaces.Read(options.WorldSql!) : null;
        if (book is not null)
        {
            store.Note($"places: {book.Places.Values.Sum(l => l.Count)} objects, {book.Drops.Values.Sum(l => l.Count)} drops, {book.PortalsWithNoDestination} portals with no destination, {book.NoSetup} objects with no model; " + string.Join(", ", book.Places.Values.SelectMany(l => l).GroupBy(p => p.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")));
        }

        if (options.Verb == "moves" && (options.GraphRun is null || options.CensusRun is null))
        {
            store.Note("moves needs --graph=DIR and --census=DIR");
            return 2;
        }

        var mainLand = options.Verb is "long" or "moves" ? LongWalks.MainLand(options.GraphRun!) : null;
        IReadOnlyList<LandblockCoord> work = options.Landblocks ?? (options.Verb == "moves" ? HardMoves.Pick(options.CensusRun!, mainLand!, options.Seed, options.Sample) : options.Verb == "places" ? book!.Landblocks() : options.Verb == "pilot" ? Pilot.Pick(context, options.Seed, options.Sample) : options.Verb == "long" ? LongWalks.Pick(mainLand!, options.Seed, options.Sample) : options.Verb == "rooms" ? RoomWalks.Pick(options.CensusRun!) : detours is not null ? [.. AllInRows().Where(c => detours.ContainsKey(Census.IdOf(c)))] : AllInRows());
        store.Note(string.Create(CultureInfo.InvariantCulture, $"{options.Verb}: {work.Count} landblocks, {store.DoneCount} already done, {options.Threads} threads, picked in {clock.Elapsed.TotalSeconds:F1} s"));

        if (options.Verb is "walks" or "oracle" && options.GraphRun is null)
        {
            store.Note(options.Verb + " needs --graph=DIR");
            return 2;
        }

        var areas = options.Verb is "walks" or "oracle" or "long" ? new AreaIndex(options.GraphRun!) : null;
        WorldRoutes? routes = null;
        if (options.Routes is not null)
        {
            using var routesFile = File.OpenRead(options.Routes);
            routes = WorldRoutes.Load(routesFile);
        }

        using var trace = options.Trace is null ? null : new TraceLog(options.Trace);
        var next = -1;
        var failures = 0;
        string? fatal = null;
        void Work()
        {
            var rig = context.NewRig(trace);
            var edgeMap = new NavMap();
            while (Volatile.Read(ref fatal) is null)
            {
                limits.Gate();
                var at = Interlocked.Increment(ref next);
                if (at >= work.Count)
                {
                    return;
                }

                var coord = work[at];
                var id = Census.IdOf(coord);
                if (store.IsDone(id))
                {
                    continue;
                }

                try
                {
                    if (options.Verb == "detour")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = Detour.Run(context, coord, detours![id], edgeMap);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "oracle")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = OracleScan.Run(context, areas!, coord, edgeMap);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "long")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = LongWalks.Run(context, rig, areas!, mainLand!, coord, options.Seed, edgeMap, routes);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "moves")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = HardMoves.Run(context, coord, options.Seed);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "places")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = PlaceWalks.Run(context, rig, book!, coord);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "rooms")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = RoomWalks.Run(context, rig, coord);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "walks")
                    {
                        var start = Stopwatch.GetTimestamp();
                        var result = EdgeWalks.Run(context, rig, areas!, coord, edgeMap, routes);
                        store.Record(id, result, new { id, ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1) });
                    }
                    else if (options.Verb == "pilot")
                    {
                        var (result, timing) = Pilot.Run(context, rig, coord);
                        store.Record(id, result, timing);
                    }
                    else
                    {
                        var (row, _, collisionMs, tileMs) = Census.Scan(context, coord);
                        store.Record(id, new { id, row }, new { id, collision_ms = Math.Round(collisionMs, 2), tile_ms = Math.Round(tileMs, 2) });
                    }
                }
                catch (Exception e) when (e is OutOfMemoryException || (e is InvalidOperationException && e.Message.StartsWith("disk-cap-reached", StringComparison.Ordinal)))
                {
                    Interlocked.CompareExchange(ref fatal, e.GetType().Name + ": " + e.Message, null);
                }
                catch (Exception e)
                {
                    // A landblock that throws is a finding, on the record by name; the run goes on.
                    Interlocked.Increment(ref failures);
                    var where = string.Join(" | ", (e.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4));
                    store.Record(id, new { id, error = e.GetType().Name, message = e.Message, where }, new { id });
                    store.Note($"{id} threw {e.GetType().Name}: {e.Message} at {where}");
                }
            }
        }

        var threads = Enumerable.Range(0, options.Threads).Select(i => new Thread(Work, 16 * 1024 * 1024) { IsBackground = true, Name = "walk-" + i }).ToArray();
        foreach (var thread in threads)
        {
            thread.Start();
        }

        var lastNote = clock.Elapsed;
        while (threads.Any(t => !t.Join(TimeSpan.FromSeconds(5))))
        {
            if (clock.Elapsed - lastNote >= ProgressEvery)
            {
                lastNote = clock.Elapsed;
                store.Note(string.Create(CultureInfo.InvariantCulture, $"progress: {store.DoneCount} of {work.Count} at {clock.Elapsed.TotalMinutes:F1} min, {failures} threw, store {context.Store.Count} landblocks, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB"));
            }
        }

        using var self = Process.GetCurrentProcess();
        store.Note(string.Create(CultureInfo.InvariantCulture, $"{options.Verb} ended: {store.DoneCount} of {work.Count} in {clock.Elapsed.TotalMinutes:F1} min, {failures} threw, peak memory {self.PeakWorkingSet64 / (1024 * 1024)} MB, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB{(fatal is null ? string.Empty : ", STOPPED: " + fatal)}"));
        return fatal is null ? 0 : 3;
    }

    /// <summary>Every id on the map, row by row, so neighbours share the collision store.</summary>
    private static List<LandblockCoord> AllInRows()
    {
        var all = new List<LandblockCoord>(LandDefs.BlocksPerSide * LandDefs.BlocksPerSide);
        for (var x = 0; x < LandDefs.BlocksPerSide; x++)
        {
            for (var y = 0; y < LandDefs.BlocksPerSide; y++)
            {
                all.Add(new LandblockCoord((byte)x, (byte)y));
            }
        }

        return all;
    }
}

/// <summary>Compares two runs' verdicts line by line, leaving out timing: the proof that a run repeats.</summary>
internal static class Compare
{
    public static int Run(WorldWalkOptions options)
    {
        if (options.Other is null)
        {
            Console.Error.WriteLine("compare needs --other=DIR");
            return 2;
        }

        var a = Read(options.Out);
        var b = Read(options.Other);
        var same = a.Keys.Intersect(b.Keys, StringComparer.Ordinal).Count(k => a[k] == b[k]);
        var differ = a.Keys.Intersect(b.Keys, StringComparer.Ordinal).Where(k => a[k] != b[k]).Order(StringComparer.Ordinal).ToArray();
        var onlyA = a.Keys.Except(b.Keys, StringComparer.Ordinal).Count();
        var onlyB = b.Keys.Except(a.Keys, StringComparer.Ordinal).Count();
        Console.WriteLine($"same={same} differ={differ.Length} only_first={onlyA} only_second={onlyB}");
        foreach (var id in differ.Take(10))
        {
            Console.WriteLine($"  {id}\n    {a[id]}\n    {b[id]}");
        }

        return differ.Length == 0 && onlyA == 0 && onlyB == 0 ? 0 : 1;
    }

    private static Dictionary<string, string> Read(string folder)
    {
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(folder, "results.jsonl")))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            lines[doc.RootElement.GetProperty("id").GetString()!] = line;
        }

        return lines;
    }
}
