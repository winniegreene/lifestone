using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.WorldWalk;

/// <summary>
/// The world graph (W2): the planner's own coarse graph over the whole map, read a column of landblocks at a time because one <see cref="NavMap"/> cannot hold the world. It answers what connects to what
/// on foot. A landmass is land joined by any step, in either direction. An area is land a body can walk out of and back into: drops link one way, so a landmass can hold several areas, and a body in one
/// may never get back to another.
/// </summary>
/// <remarks>
/// Each column's tiles are attached with the columns either side, so the links out of a column are complete when it is read. The tiles are read through <see cref="NavMap.RegionsOf"/> and
/// <see cref="NavMap.LinksOf"/>, so this is the planner's view of the world, not a copy of its rules.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WorldGraph
{
    /// <summary>The file that names each region's area, for the walks (W4).</summary>
    public const string RegionsFile = "regions.bin";

    /// <summary>The file a walk reads the world's land from (<see cref="Lifestone.Client.Movement.Wayfinding.WorldRoutes"/>).</summary>
    public const string RoutesFile = "world-routes.bin";

    private const int Columns = LandDefs.BlocksPerSide;

    public static int Run(WorldWalkOptions options, RunStore store, MachineLimits limits, WalkContext context)
    {
        var clock = Stopwatch.StartNew();
        var map = new NavMap();
        var baseOf = new int[Columns * Columns];
        Array.Fill(baseOf, -1);
        var nodes = new List<int>(3_000_000);
        var blocks = new List<ushort>(3_000_000);
        var cells = new List<byte>(3_000_000);
        var centres = new List<(float X, float Y)>(3_000_000);
        var edges = new List<long>(16_000_000);
        var links = new List<(uint Landblock, int Region)>();

        void Attach(int x)
        {
            var tiles = new NavTile?[Columns];
            Parallel.For(0, Columns, new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, y =>
            {
                var coord = new LandblockCoord((byte)x, (byte)y);
                if (context.Dats.GetLandblockTerrain(coord) is null)
                {
                    return;
                }

                var tile = context.Tile(coord, out _);
                tiles[y] = Array.Exists(tile.Flags, f => (f & NavFlags.Blocked) == 0) ? tile : null;
            });

            for (var y = 0; y < Columns; y++)
            {
                if (tiles[y] is not { } tile)
                {
                    continue;
                }

                map.Attach(tile);
                baseOf[(x * Columns) + y] = nodes.Count;
                foreach (var region in map.RegionsOf(tile.LandblockId))
                {
                    nodes.Add(region.Nodes);
                    blocks.Add((ushort)((x * Columns) + y));
                    cells.Add((byte)region.Cell);
                    centres.Add((region.Centroid.X, region.Centroid.Y));
                }
            }
        }

        void Read(int x)
        {
            for (var y = 0; y < Columns; y++)
            {
                var first = baseOf[(x * Columns) + y];
                if (first < 0)
                {
                    continue;
                }

                var id = WalkContext.BlockId(new LandblockCoord((byte)x, (byte)y));
                var count = map.RegionsOf(id).Count;
                for (var r = 0; r < count; r++)
                {
                    links.Clear();
                    map.LinksOf(id, r, links);
                    foreach (var (landblock, region) in links)
                    {
                        var target = baseOf[((int)(landblock >> 24) * Columns) + (int)((landblock >> 16) & 0xFF)] + region;
                        edges.Add(((long)(first + r) << 32) | (uint)target);
                    }
                }
            }
        }

        void Drop(int x)
        {
            for (var y = 0; y < Columns; y++)
            {
                map.Detach(WalkContext.BlockId(new LandblockCoord((byte)x, (byte)y)));
            }
        }

        Attach(0);
        for (var x = 0; x < Columns; x++)
        {
            limits.Gate();
            if (x + 1 < Columns)
            {
                Attach(x + 1);
            }

            Read(x);
            if (x > 0)
            {
                Drop(x - 1);
            }

            if (x % 25 == 0)
            {
                store.Note(string.Create(CultureInfo.InvariantCulture, $"graph: column {x} of {Columns}, {nodes.Count} regions, {edges.Count} links, {clock.Elapsed.TotalMinutes:F1} min, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB"));
            }
        }

        store.Note(string.Create(CultureInfo.InvariantCulture, $"graph: read {nodes.Count} regions and {edges.Count} links in {clock.Elapsed.TotalMinutes:F1} min"));
        var areas = Areas(nodes.Count, edges);
        var landmasses = Landmasses(nodes.Count, edges);
        Report(store, nodes, blocks, areas, landmasses, clock);
        WriteRoutes(store, nodes, blocks, cells, centres, edges);
        return 0;
    }

    /// <summary>The world routes file: each landblock's regions gathered into the components a body can walk round both ways inside that landblock, and the links between components.</summary>
    private static void WriteRoutes(RunStore store, List<int> nodes, List<ushort> blocks, List<byte> cells, List<(float X, float Y)> centres, List<long> edges)
    {
        var n = nodes.Count;
        var offset = new int[n + 1];
        foreach (var e in edges)
        {
            offset[(int)(e >> 32) + 1]++;
        }

        for (var i = 0; i < n; i++)
        {
            offset[i + 1] += offset[i];
        }

        var targets = new int[edges.Count];
        var fill = (int[])offset.Clone();
        foreach (var e in edges)
        {
            targets[fill[(int)(e >> 32)]++] = (int)(uint)e;
        }

        var component = StronglyJoinedInsideBlocks(n, offset, targets, blocks, out var count);
        var nodesOf = new int[count];
        var xOf = new double[count];
        var yOf = new double[count];
        var blockOf = new ushort[count];
        for (var r = 0; r < n; r++)
        {
            var c = component[r];
            nodesOf[c] += nodes[r];
            xOf[c] += (double)centres[r].X * nodes[r];
            yOf[c] += (double)centres[r].Y * nodes[r];
            blockOf[c] = blocks[r];
        }

        var linkOffset = new int[count + 1];
        var links = new List<int>(n);
        var bySource = new List<int>[count];
        for (var r = 0; r < n; r++)
        {
            var c = component[r];
            for (var e = offset[r]; e < offset[r + 1]; e++)
            {
                var to = component[targets[e]];
                if (to != c)
                {
                    (bySource[c] ??= []).Add(to);
                }
            }
        }

        for (var c = 0; c < count; c++)
        {
            linkOffset[c] = links.Count;
            if (bySource[c] is { } out_)
            {
                links.AddRange(out_.Distinct().Order());
            }
        }

        linkOffset[count] = links.Count;
        var blockIds = new List<ushort>();
        var regionBase = new List<int>();
        var componentBase = new List<int>();
        var regionComponent = new ushort[n];
        var baseNow = 0;
        for (var r = 0; r < n; r++)
        {
            if (r == 0 || blocks[r] != blocks[r - 1])
            {
                blockIds.Add((ushort)((blocks[r] / Columns << 8) | (blocks[r] % Columns)));
                regionBase.Add(r);

                // A block's components are contiguous, but they finish in reverse order of discovery, so its first region need not hold the lowest.
                baseNow = component[r];
                for (var q = r + 1; q < n && blocks[q] == blocks[r]; q++)
                {
                    baseNow = Math.Min(baseNow, component[q]);
                }

                componentBase.Add(baseNow);
            }

            regionComponent[r] = checked((ushort)(component[r] - baseNow));
        }

        regionBase.Add(n);
        componentBase.Add(count);
        var ids = new ushort[count];
        for (var c = 0; c < count; c++)
        {
            ids[c] = (ushort)((blockOf[c] / Columns << 8) | (blockOf[c] % Columns));
        }

        using var routesFile = File.Create(Path.Combine(store.Folder, RoutesFile));
        WorldRoutes.Write(
            routesFile,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(blockIds),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(regionBase),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(componentBase),
            regionComponent,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells),
            ids,
            nodesOf,
            xOf.Select((x, i) => (float)(x / nodesOf[i])).ToArray(),
            yOf.Select((y, i) => (float)(y / nodesOf[i])).ToArray(),
            linkOffset,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(links));
        store.Note($"world routes: {blockIds.Count} landblocks, {n} regions, {count} components, {links.Count} links");
    }

    /// <summary>Tarjan's strongly connected components over the links that stay inside a landblock, without recursion. Regions come block by block, so a block's components come out together and in order.</summary>
    private static int[] StronglyJoinedInsideBlocks(int n, int[] offset, int[] targets, List<ushort> blocks, out int count)
    {
        var index = new int[n];
        var low = new int[n];
        var component = new int[n];
        Array.Fill(component, -1);
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var work = new Stack<(int Node, int Edge)>();
        var next = 0;
        var found = 0;
        for (var root = 0; root < n; root++)
        {
            if (index[root] != 0)
            {
                continue;
            }

            work.Push((root, offset[root]));
            while (work.TryPop(out var at))
            {
                var (v, e) = at;
                if (index[v] == 0)
                {
                    index[v] = low[v] = ++next;
                    stack.Push(v);
                    onStack[v] = true;
                }

                var descended = false;
                for (; e < offset[v + 1]; e++)
                {
                    var w = targets[e];
                    if (blocks[w] != blocks[v])
                    {
                        continue;
                    }

                    if (index[w] == 0)
                    {
                        work.Push((v, e + 1));
                        work.Push((w, offset[w]));
                        descended = true;
                        break;
                    }

                    if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }
                }

                if (descended)
                {
                    continue;
                }

                if (low[v] == index[v])
                {
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        component[w] = found;
                    }
                    while (w != v);
                    found++;
                }

                if (work.TryPeek(out var parent))
                {
                    low[parent.Node] = Math.Min(low[parent.Node], low[v]);
                }
            }
        }

        count = found;
        return component;
    }

    private static void Report(RunStore store, List<int> nodes, List<ushort> blocks, int[] areas, int[] landmasses, Stopwatch clock)
    {
        var (massNames, massSizes) = Rank(landmasses, nodes);
        var (areaNames, areaSizes) = Rank(areas, nodes);
        var perBlock = new Dictionary<ushort, Dictionary<(int Mass, int Area), int>>();
        for (var r = 0; r < nodes.Count; r++)
        {
            if (!perBlock.TryGetValue(blocks[r], out var held))
            {
                perBlock[blocks[r]] = held = [];
            }

            var key = (massNames[landmasses[r]], areaNames[areas[r]]);
            held[key] = held.GetValueOrDefault(key) + nodes[r];
        }

        foreach (var (block, held) in perBlock.OrderBy(x => x.Key))
        {
            var coord = new LandblockCoord((byte)(block / Columns), (byte)(block % Columns));
            var id = Census.IdOf(coord);
            var main = held.MaxBy(x => x.Value);
            store.Record(id, new
            {
                id,
                landmass = main.Key.Mass,
                area = main.Key.Area,
                nodes = held.Values.Sum(),
                landmasses = held.Keys.Select(k => k.Mass).Distinct().Count(),
                areas = held.Count,
            }, new { id });
        }

        var massBlocks = new Dictionary<int, HashSet<ushort>>();
        var massAreas = new Dictionary<int, HashSet<int>>();
        for (var r = 0; r < nodes.Count; r++)
        {
            var mass = massNames[landmasses[r]];
            if (!massBlocks.TryGetValue(mass, out var set))
            {
                massBlocks[mass] = set = [];
                massAreas[mass] = [];
            }

            set.Add(blocks[r]);
            massAreas[mass].Add(areaNames[areas[r]]);
        }

        var table = massSizes.Select((size, i) =>
        {
            var name = i + 1;
            var held = massBlocks[name];
            return new
            {
                landmass = name,
                km2 = Math.Round(size / 1e6, 3),
                landblocks = held.Count,
                areas = massAreas[name].Count,
                west = held.Min(b => b / Columns),
                east = held.Max(b => b / Columns),
                south = held.Min(b => b % Columns),
                north = held.Max(b => b % Columns),
            };
        }).ToArray();
        File.WriteAllText(Path.Combine(store.Folder, "landmasses.json"), JsonSerializer.Serialize(table));
        WriteRegions(Path.Combine(store.Folder, RegionsFile), blocks, areas, areaNames, areaSizes);
        (string Name, long Min)[] bands = [("1 km² or more", 1_000_000L), ("10,000 m² to 1 km²", 10_000L), ("100 to 10,000 m²", 100L), ("under 100 m²", 0L)];
        var from = long.MaxValue;
        foreach (var (name, min) in bands)
        {
            store.Note($"landmasses of {name}: {massSizes.Count(s => s >= min && s < from)}; areas: {areaSizes.Count(s => s >= min && s < from)}");
            from = min;
        }

        store.Note(string.Create(CultureInfo.InvariantCulture, $"graph ended in {clock.Elapsed.TotalMinutes:F1} min: {massSizes.Count} landmasses, {areaSizes.Count} areas; largest landmass {massSizes[0] / 1e6:F1} km², largest area {areaSizes[0] / 1e6:F1} km²"));
    }

    /// <summary>Each landblock's regions in order, with the number of the area each belongs to.</summary>
    private static void WriteRegions(string path, List<ushort> blocks, int[] areas, Dictionary<int, int> areaNames, List<long> areaSizes)
    {
        var runs = new List<(ushort Block, int First, int Count)>();
        for (var r = 0; r < blocks.Count; r++)
        {
            if (runs.Count > 0 && runs[^1].Block == blocks[r])
            {
                runs[^1] = (blocks[r], runs[^1].First, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((blocks[r], r, 1));
            }
        }

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(areaSizes.Count);
        foreach (var size in areaSizes)
        {
            writer.Write((int)Math.Min(size, int.MaxValue));
        }

        writer.Write(runs.Count);
        foreach (var (block, first, count) in runs)
        {
            writer.Write(block);
            writer.Write(count);
            for (var r = first; r < first + count; r++)
            {
                writer.Write(areaNames[areas[r]]);
            }
        }
    }

    /// <summary>Numbers groups from 1 by total nodes, largest first.</summary>
    private static (Dictionary<int, int> Names, List<long> Sizes) Rank(int[] groups, List<int> nodes)
    {
        var sizes = new Dictionary<int, long>();
        for (var r = 0; r < groups.Length; r++)
        {
            sizes[groups[r]] = sizes.GetValueOrDefault(groups[r]) + nodes[r];
        }

        var ranked = sizes.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToArray();
        var names = new Dictionary<int, int>(ranked.Length);
        for (var i = 0; i < ranked.Length; i++)
        {
            names[ranked[i].Key] = i + 1;
        }

        return (names, ranked.Select(x => x.Value).ToList());
    }

    /// <summary>Land joined by any step in either direction.</summary>
    private static int[] Landmasses(int count, List<long> edges)
    {
        var parent = new int[count];
        for (var i = 0; i < count; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        foreach (var edge in edges)
        {
            var a = Find((int)(edge >> 32));
            var b = Find((int)(uint)edge);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        var group = new int[count];
        for (var i = 0; i < count; i++)
        {
            group[i] = Find(i);
        }

        return group;
    }

    /// <summary>The strongly connected components: land a body can walk out of and back into. Tarjan's algorithm, on an explicit stack because the world's graph is far deeper than a thread's.</summary>
    private static int[] Areas(int count, List<long> edges)
    {
        var start = new int[count + 1];
        foreach (var edge in edges)
        {
            start[(int)(edge >> 32) + 1]++;
        }

        for (var i = 0; i < count; i++)
        {
            start[i + 1] += start[i];
        }

        var targets = new int[edges.Count];
        var fill = (int[])start.Clone();
        foreach (var edge in edges)
        {
            targets[fill[(int)(edge >> 32)]++] = (int)(uint)edge;
        }

        var order = new int[count];
        var low = new int[count];
        var group = new int[count];
        var next = new int[count];
        Array.Fill(group, -1);
        var held = new int[count];
        var heldCount = 0;
        var calls = new int[count];
        var seen = 0;
        for (var root = 0; root < count; root++)
        {
            if (order[root] != 0)
            {
                continue;
            }

            var depth = 0;
            calls[depth++] = root;
            order[root] = low[root] = ++seen;
            next[root] = start[root];
            held[heldCount++] = root;
            while (depth > 0)
            {
                var v = calls[depth - 1];
                if (next[v] < start[v + 1])
                {
                    var w = targets[next[v]++];
                    if (order[w] == 0)
                    {
                        order[w] = low[w] = ++seen;
                        next[w] = start[w];
                        held[heldCount++] = w;
                        calls[depth++] = w;
                    }
                    else if (group[w] < 0)
                    {
                        low[v] = Math.Min(low[v], order[w]);
                    }

                    continue;
                }

                depth--;
                if (depth > 0)
                {
                    var parent = calls[depth - 1];
                    low[parent] = Math.Min(low[parent], low[v]);
                }

                if (low[v] == order[v])
                {
                    int w;
                    do
                    {
                        w = held[--heldCount];
                        group[w] = v;
                    }
                    while (w != v);
                }
            }
        }

        return group;
    }
}
