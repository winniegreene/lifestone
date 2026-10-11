using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests.Models;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;
using DatEnvironment = DatReaderWriter.DBObjs.Environment;

namespace Lifestone.Data.Tests.Cells;

/// <summary>Shares one full scan of the cell dat between the exhaustive tests.</summary>
public sealed class ExhaustiveCellsFixture : IDisposable
{
    private readonly CellsFixture _inner = new();
    private readonly Lazy<WorldCellScan> _scan;

    public ExhaustiveCellsFixture()
    {
        _scan = new Lazy<WorldCellScan>(() => WorldCellScan.Run(Structures));
    }

    public DatSet Dats => _inner.Dats;

    public CellStructureLibrary Structures => _inner.Structures;

    public WorldCellScan Scan => _scan.Value;

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Whole-dat sweeps: every LandBlockInfo (5,346), every EnvCell (734,976), every Environment (772), a mesh for every
/// distinct (Environment, struct, surface list), every building doorway, and portal walks from a sample of cells.
/// Expected counts are measurements of the end-of-retail dats (cell iteration 982, portal 2072).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class CellExhaustiveTests(ExhaustiveCellsFixture fixture, ITestOutputHelper output) : IClassFixture<ExhaustiveCellsFixture>
{
    [RequiresDatsFact]
    public void Every_LandBlockInfo_and_EnvCell_decodes_byte_exact_and_matches_the_independent_decoder()
    {
        var scan = fixture.Scan;
        output.WriteLine($"scan: {scan.LandBlockInfos} LandBlockInfos, {scan.EnvCells} EnvCells in {scan.Elapsed.TotalSeconds:F1} s");
        Assert.True(scan.InvariantFailures == 0, string.Join(Environment.NewLine, scan.Mismatches));
        Assert.Equal(5346, scan.LandBlockInfos);
        Assert.Equal(734976, scan.EnvCells);
        Assert.Equal(1293, scan.RestrictionTables);

        // Restriction-table keys: retail CLandBlockInfo::GetRestrictionIID is only asked for the block's land cells.
        var keys = scan.Histograms.Where(kv => kv.Key.StartsWith("restrictionkey:", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[15..], kv => kv.Value);
        output.WriteLine("restriction keys: " + string.Join(", ", keys.OrderBy(k => k.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Equal(3490, keys.GetValueOrDefault("landcell"));
        Assert.Equal(3000, keys.GetValueOrDefault("envcell"));
        Assert.Equal(650, keys.GetValueOrDefault("zero"));
        Assert.Equal(0, keys.GetValueOrDefault("other"));
    }

    [RequiresDatsFact]
    public void Flags_take_only_the_known_values()
    {
        var scan = fixture.Scan;
        var cellFlags = scan.Histograms.Where(kv => kv.Key.StartsWith("cellflags:", StringComparison.Ordinal))
            .ToDictionary(kv => Convert.ToUInt32(kv.Key[10..], 16), kv => kv.Value);
        output.WriteLine("EnvCell flags: " + string.Join(", ", cellFlags.OrderBy(k => k.Key).Select(kv => $"0x{kv.Key:X}:{kv.Value}")));
        Assert.Equal(new uint[] { 0x0, 0x1, 0x2, 0x3, 0x8, 0x9, 0xA, 0xB }, cellFlags.Keys.Order());
        Assert.Equal(734976, cellFlags.Values.Sum());
        Assert.DoesNotContain(cellFlags.Keys, f => (f & (uint)EnvCellFlags.HasWeenieObjs) != 0);

        var portalFlags = scan.Histograms.Where(kv => kv.Key.StartsWith("portalflags:", StringComparison.Ordinal))
            .ToDictionary(kv => int.Parse(kv.Key[12..]), kv => kv.Value);
        output.WriteLine("CellPortal flags: " + string.Join(", ", portalFlags.OrderBy(k => k.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.Equal(new[] { 0, 1, 2, 3, 5, 7 }, portalFlags.Keys.Order());
        Assert.Equal(scan.Portals, portalFlags.Values.Sum());

        // Every cell with an outside portal is SeenOutside; 46,620 more see out through other rooms.
        Assert.Equal(0, scan.Histogram("notseenoutside:withoutsideportal"));
        Assert.Equal(15082, scan.Histogram("seenoutside:withoutsideportal"));
        Assert.Equal(46620, scan.Histogram("seenoutside:nooutsideportal"));
        Assert.Equal(8236, scan.Histogram("nodrawingbsp:cells"));

        // EnvCell statics are placed in landblock space: every one sits inside its cell's bounds (+2 m).
        var near = scan.Histogram("cellstatic:near");
        var far = scan.Histogram("cellstatic:far");
        output.WriteLine($"EnvCell statics within 2 m of their cell: {near}, farther: {far}");
        Assert.Equal(597863, near);
        Assert.Equal(0, far);

        Assert.Equal(new[] { "buildingmodeltype:01" }, scan.Histograms.Keys.Where(k => k.StartsWith("buildingmodeltype:", StringComparison.Ordinal)));
        Assert.Equal(
            new[] { "buildingportalflags:1", "buildingportalflags:3" },
            scan.Histograms.Keys.Where(k => k.StartsWith("buildingportalflags:", StringComparison.Ordinal)).Order());
        output.WriteLine($"building portal flags 1 (PortalSide): {scan.Histogram("buildingportalflags:1")}, 3: {scan.Histogram("buildingportalflags:3")}");
        Assert.Equal(5729, scan.Histogram("buildingportalflags:1"));
        Assert.Equal(11208, scan.Histogram("buildingportalflags:3"));
    }

    [RequiresDatsFact]
    public void Num_cells_field_disagrees_in_38_landblocks_and_never_overshoots()
    {
        Assert.Equal(38, fixture.Scan.NumCellsMismatch);
        Assert.Equal(0, fixture.Scan.NumCellsOvershoot);
    }

    [RequiresDatsFact]
    public void Portal_graph_is_closed_and_almost_reciprocal()
    {
        var scan = fixture.Scan;
        output.WriteLine($"portals {scan.Portals}, inside {scan.InsidePortals}, other-portal index out of range {scan.OtherPortalOutOfRange}, " +
                         $"not reciprocal {scan.NotReciprocal}, of these in cells beyond num_cells {scan.AnomaliesBeyondNumCells}");
        Assert.Equal(1867699, scan.Portals);
        Assert.Equal(1850702, scan.InsidePortals);
        Assert.Equal(697, scan.OtherPortalOutOfRange);
        Assert.Equal(1081, scan.NotReciprocal);
        Assert.Equal(1662, scan.AnomaliesBeyondNumCells);
    }

    [RequiresDatsFact]
    public void Exact_match_portal_pairs_coincide_in_landblock_space()
    {
        // Validates frames (origin + quaternion w,x,y,z, active rotation), polygon ids and other-portal indices at once.
        var scan = fixture.Scan;
        output.WriteLine($"exact pairs {scan.ExactPairs}, apart {scan.ExactPairsApart}, worst {scan.WorstExactPairDistance * 1000:F2} mm; non-exact pairs {scan.NonExactPairs}");
        Assert.Equal(1706795, scan.ExactPairs);
        Assert.Equal(0, scan.ExactPairsApart);
        Assert.True(scan.WorstExactPairDistance < 0.005f);
    }

    [RequiresDatsFact]
    public void Portal_side_flag_names_the_cell_interior_side()
    {
        var scan = fixture.Scan;
        output.WriteLine($"portal side decided by the cell BSP for {scan.PortalSideDecided} portals, wrong {scan.PortalSideWrong}");
        Assert.Equal(1867699, scan.PortalSideDecided);
        Assert.Equal(0, scan.PortalSideWrong);
    }

    [RequiresDatsFact]
    public void Building_portals_enter_cells_through_outside_portals()
    {
        var scan = fixture.Scan;
        output.WriteLine($"buildings {scan.Buildings}, doorways {scan.BuildingPortals}, bad {scan.BuildingPortalsBad}, entry cell missing from its stab list {scan.BuildingEntryNotInStabList}");
        Assert.Equal(6979, scan.Buildings);
        Assert.Equal(1515, scan.Histogram("buildings:nodoorways"));
        Assert.Equal(16937, scan.BuildingPortals);
        Assert.Equal(12783, scan.Histogram("statictype:01"));
        Assert.Equal(30159, scan.Histogram("statictype:02"));
        Assert.Equal(2, scan.Histograms.Keys.Count(k => k.StartsWith("statictype:", StringComparison.Ordinal)));
        Assert.Equal(0, scan.BuildingPortalsBad);
        Assert.Equal(0, scan.BuildingEntryNotInStabList);
    }

    [RequiresDatsFact]
    public void Every_Environment_decodes_byte_exact_with_ids_at_their_positions()
    {
        var ids = fixture.Dats.Read(d => d.Portal.GetAllIdsOfType<DatEnvironment>().ToList());
        Assert.Equal(772, ids.Count);
        var structs = 0;
        var withoutDrawingBsp = new List<string>();
        foreach (var id in ids)
        {
            var bytes = fixture.Dats.Read(d => d.Portal.TryGetFileBytes(id, out var b) ? b : null)!;
            foreach (var s in CellStructureLibrary.Decode(id, bytes))
            {
                structs++;
                if (!s.HasDrawingBsp)
                {
                    withoutDrawingBsp.Add(s.ToString());
                }
            }
        }

        output.WriteLine($"{ids.Count} Environments, {structs} cell structs; without drawing BSP: {string.Join(", ", withoutDrawingBsp)}");
        Assert.Equal(
            new[] { "CellStruct 0x0D00006B[0]", "CellStruct 0x0D00027F[0]", "CellStruct 0x0D0002F1[0]" },
            withoutDrawingBsp);
    }

    [RequiresDatsFact]
    public void Every_distinct_struct_and_surface_list_builds_a_well_formed_mesh()
    {
        var keys = fixture.Scan.MeshKeys;
        var pairs = keys.Select(k => (k.EnvironmentId, k.StructIndex)).Distinct().Count();
        var cache = new CellMeshCache(fixture.Structures, capacity: keys.Count + 1);
        var timer = Stopwatch.StartNew();
        var triangles = 0L;
        var drawn = 0L;
        var untextured = 0L;
        var degenerate = 0L;
        var splitByAddressing = 0;
        var inside = 0L;
        var outside = 0L;
        var noPosDrawn = 0;
        var normals = 0L;
        var nonUnitNormals = 0L;
        var skippedNoPos = 0;
        var skippedVisibleSolid = 0;
        var texturedPortals = 0;
        var failures = new ConcurrentQueue<string>();
        var surfaceTypes = new SurfaceTypeLookup(fixture.Dats);
        Parallel.ForEach(keys, key =>
        {
            try
            {
                var mesh = cache.Get(key.EnvironmentId, key.StructIndex, key.SurfaceIds);
                CellMeshAssert.WellFormed(mesh, key.SurfaceIds);
                Assert.Equal(mesh.PolygonCount, mesh.DrawnPolygonCount + mesh.UntexturedPolygonCount + mesh.DegeneratePolygonCount);
                Interlocked.Add(ref triangles, mesh.Mesh.TriangleCount);
                Interlocked.Add(ref drawn, mesh.DrawnPolygonCount);
                Interlocked.Add(ref untextured, mesh.UntexturedPolygonCount);
                Interlocked.Add(ref degenerate, mesh.DegeneratePolygonCount);
                if (mesh.Mesh.SubMeshes.Select(s => (s.SurfaceId, s.Side)).Distinct().Count() != mesh.Mesh.SubMeshes.Count)
                {
                    Interlocked.Increment(ref splitByAddressing);
                }

                var structure = fixture.Structures.Get(key.EnvironmentId, key.StructIndex);
                foreach (var normal in mesh.Mesh.SubMeshes.SelectMany(s => s.Normals))
                {
                    if (MathF.Abs(normal.Length() - 1f) > 0.01f)
                    {
                        Interlocked.Increment(ref nonUnitNormals);
                    }

                    Interlocked.Increment(ref normals);
                }

                var (i, o) = FrontProbes(mesh, structure);
                Interlocked.Add(ref inside, i);
                Interlocked.Add(ref outside, o);

                // Invisible portal faces (NoPos) are never part of the drawn mesh; count what the texture rule keeps.
                var portalIds = structure.Portals.Select(p => p.PolygonId).ToHashSet();
                foreach (var polygon in structure.Polygons)
                {
                    var textured = CellMeshBuilder.IsDrawnInCells(surfaceTypes.TypeOf(key.SurfaceIds[polygon.PosSurface]));
                    if (polygon.NoPos && textured)
                    {
                        Interlocked.Increment(ref noPosDrawn);
                    }

                    if (!polygon.NoPos && !textured)
                    {
                        Interlocked.Increment(ref skippedVisibleSolid);
                    }

                    if (polygon.NoPos && !textured)
                    {
                        Interlocked.Increment(ref skippedNoPos);
                    }

                    if (portalIds.Contains(polygon.Id) && textured)
                    {
                        Interlocked.Increment(ref texturedPortals);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Enqueue($"0x{key.EnvironmentId:X8}[{key.StructIndex}]: {ex.Message}");
            }
        });

        output.WriteLine($"{keys.Count} meshes ({pairs} distinct structs) in {timer.Elapsed.TotalSeconds:F1} s: {triangles} triangles; polygons drawn {drawn}, untextured (skipped) {untextured}, degenerate {degenerate}; " +
                         $"meshes split by texture addressing {splitByAddressing}; front probes inside {inside} / outside {outside} ({100.0 * inside / (inside + outside):F1}% inside); " +
                         $"skipped: NoPos {skippedNoPos}, other untextured {skippedVisibleSolid}; textured portal polygons drawn {texturedPortals}; " +
                         $"vertex normals not unit length (+-1%) {nonUnitNormals} of {normals}");
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
        Assert.Equal(3145, pairs);
        Assert.Equal(0, degenerate);
        Assert.Equal(0, noPosDrawn);
        Assert.Equal(untextured, skippedNoPos + skippedVisibleSolid);
        Assert.True(inside > 0.9 * (inside + outside), "drawn front faces face into their cell");
    }

    /// <summary>
    /// The ConstructMesh unification (Models/RetailConstructMesh.cs): for every distinct cell mesh of the dats, the
    /// cells adapter (<see cref="CellMeshBuilder"/>, our CellStructure types, untextured sides dropped at build time)
    /// and the GfxObj adapter (<see cref="GfxObjMeshBuilder.BuildPolygons"/>, DatReaderWriter's CellStruct) give the same
    /// mesh; and the GfxObj path's unfiltered build, with retail's draw-time rule applied afterwards
    /// (<see cref="SurfaceDrawRules"/>, EnvCell context), keeps exactly the same sub-meshes.
    /// </summary>
    [RequiresDatsFact]
    public void Every_distinct_cell_mesh_is_identical_through_the_cell_and_gfxobj_paths()
    {
        var keys = fixture.Scan.MeshKeys;
        var surfaceTypes = new SurfaceTypeLookup(fixture.Dats);
        var failures = new ConcurrentQueue<string>();
        var compared = 0;
        var subMeshes = 0L;
        Parallel.ForEach(keys, key =>
        {
            try
            {
                var structure = fixture.Structures.Get(key.EnvironmentId, key.StructIndex);
                var cell = CellMeshBuilder.Build(structure, key.SurfaceIds, surfaceTypes.TypeOf);
                Assert.True(fixture.Dats.TryGetPortal<DatEnvironment>(key.EnvironmentId, out var environment));
                var cellStruct = environment.Cells[key.StructIndex];
                bool Textured(uint id) => CellMeshBuilder.IsDrawnInCells(surfaceTypes.TypeOf(id));

                var filtered = GfxObjMeshBuilder.BuildPolygons(key.EnvironmentId, key.SurfaceIds, cellStruct.VertexArray.Vertices, cellStruct.Polygons, Textured);
                MeshAssert.Identical(cell.Mesh, filtered.Mesh);
                Assert.Equal(cell.SubMeshInfo.Select(i => (int)i.SurfaceIndex), filtered.SubMeshInfo.Select(i => i.FirstSurfaceIndex));
                Assert.Equal(cell.DegeneratePolygonCount, filtered.Stats.DegeneratePolygonCount);

                var unfiltered = GfxObjMeshBuilder.BuildPolygons(key.EnvironmentId, key.SurfaceIds, cellStruct.VertexArray.Vertices, cellStruct.Polygons);
                var drawn = unfiltered.Mesh.SubMeshes.Where(s => Textured(s.SurfaceId)).ToArray();
                MeshAssert.Identical(cell.Mesh, new MeshData(drawn));
                Interlocked.Increment(ref compared);
                Interlocked.Add(ref subMeshes, cell.Mesh.SubMeshes.Count);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Enqueue($"0x{key.EnvironmentId:X8}[{key.StructIndex}]: {ex.Message}");
            }
        });

        output.WriteLine($"{compared} cell meshes ({subMeshes} sub-meshes) identical through both ConstructMesh adapters");
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
        Assert.Equal(keys.Count, compared);
    }

    [RequiresDatsFact]
    public void Every_building_doorway_coincides_with_its_entry_cells_outside_portal()
    {
        var timer = Stopwatch.StartNew();
        var doorways = 0;
        var failures = new ConcurrentQueue<string>();
        Parallel.ForEach(CellDiscovery.LandblocksWithInfo(fixture.Dats), coord =>
        {
            var statics = LandblockStatics.Read(fixture.Dats, coord)!;
            if (statics.Buildings.All(b => b.Portals.Count == 0))
            {
                return;
            }

            var set = LandblockCellSet.Load(fixture.Structures, coord);
            foreach (var building in statics.Buildings)
            {
                var geometry = BuildingPortalGeometry.Load(fixture.Dats, building);
                foreach (var doorway in geometry.Doorways)
                {
                    Interlocked.Increment(ref doorways);
                    var cell = set.GetCell(doorway.Portal.OtherCellId)!;
                    var portal = cell.Portals[doorway.Portal.OtherPortalIndex];
                    var polygon = set.GetStructure(cell).GetPortal(portal.PolygonId)!;
                    var cellPolygon = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
                    if (CellGeometryMath.SetDistance(cellPolygon, doorway.Vertices) > 0.01f)
                    {
                        failures.Enqueue($"{coord} building 0x{building.ModelId:X8} doorway {doorway.PortalIndex}: polygons apart");
                    }

                    // A point just inside the doorway is in the entry cell; the viewer must stand on the other side.
                    var centre = doorway.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v) / doorway.Vertices.Length;
                    var inwards = doorway.Portal.PortalSide ? doorway.Plane.Normal : -doorway.Plane.Normal;
                    if (!set.Contains(cell, centre + (inwards * 0.05f)) || doorway.CanLookIn(centre + (inwards * 0.05f)) || !doorway.CanLookIn(centre - (inwards * 0.05f)))
                    {
                        failures.Enqueue($"{coord} building 0x{building.ModelId:X8} doorway {doorway.PortalIndex}: side test disagrees with the entry cell");
                    }
                }
            }
        });

        output.WriteLine($"{doorways} doorways in {timer.Elapsed.TotalSeconds:F1} s");
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
        Assert.Equal(16937, doorways);
    }

    [RequiresDatsFact]
    public void Landblock_classification_and_dungeon_entry_points()
    {
        var summaries = fixture.Scan.Summaries;
        var byKind = summaries.GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Count());
        var withCells = summaries.Where(s => s.EnvCellCount > 0).ToList();
        output.WriteLine($"landblocks with cells {withCells.Count}: " + string.Join(", ", byKind.OrderBy(k => k.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        output.WriteLine($"ACE IsDungeon {summaries.Count(s => s.AceIsDungeon)}; heights all zero with cells {withCells.Count(s => s.AllTerrainHeightsZero)}; " +
                         $"underground cells {summaries.Sum(s => s.UndergroundCellCount)}");
        Assert.Equal(3409, withCells.Count);
        Assert.Equal(5346 - 3409, byKind.GetValueOrDefault(LandblockCellKind.NoCells));
        Assert.Equal(1773, byKind.GetValueOrDefault(LandblockCellKind.Dungeon));
        Assert.Equal(1083, byKind.GetValueOrDefault(LandblockCellKind.BuildingInteriors));
        Assert.Equal(553, byKind.GetValueOrDefault(LandblockCellKind.Mixed));
        Assert.Equal(1718, summaries.Count(s => s.AceIsDungeon));
        Assert.All(summaries.Where(s => s.AceIsDungeon), s => Assert.Equal(LandblockCellKind.Dungeon, s.Kind));
        Assert.All(summaries.Where(s => s.Kind == LandblockCellKind.BuildingInteriors), s => Assert.True(s.BuildingCount > 0));

        var timer = Stopwatch.StartNew();
        var dungeons = summaries.Where(s => s.Kind == LandblockCellKind.Dungeon).ToList();
        var missing = new ConcurrentQueue<string>();
        Parallel.ForEach(dungeons, s =>
        {
            var set = LandblockCellSet.Load(fixture.Structures, s.Coord);
            var entry = CellDiscovery.FindDungeonEntry(set, LandblockStatics.Read(fixture.Dats, s.Coord));
            if (entry is null)
            {
                missing.Enqueue(s.Coord.ToString());
                return;
            }

            Assert.True(set.Contains(set.GetCell(entry.CellId)!, entry.Position + new Vector3(0, 0, 0.01f)));
        });

        output.WriteLine($"entry points for {dungeons.Count - missing.Count} of {dungeons.Count} dungeon landblocks in {timer.Elapsed.TotalSeconds:F1} s; none for: {string.Join(" ", missing.Take(30))}");
        Assert.Empty(missing);
    }

    [RequiresDatsFact]
    public void Largest_landblocks_by_cell_count_are_the_README_anchors()
    {
        var top = fixture.Scan.Summaries
            .OrderByDescending(s => s.EnvCellCount).ThenBy(s => s.Coord.X).ThenBy(s => s.Coord.Y)
            .Take(6)
            .Select(s => $"{s.Coord.X:X2}{s.Coord.Y:X2} {s.EnvCellCount} {s.Kind}")
            .ToArray();
        output.WriteLine(string.Join(", ", top));
        Assert.Equal(
            new[]
            {
                "00D1 4213 Dungeon", "200F 2468 Mixed", "8C03 2295 Dungeon", "7E03 2260 Dungeon", "8603 2163 Dungeon",
                "8803 2163 Dungeon",
            },
            top);
    }

    [RequiresDatsFact]
    public void FindDungeons_lists_dungeon_and_mixed_landblocks_largest_first()
    {
        var timer = Stopwatch.StartNew();
        var found = CellDiscovery.FindDungeons(fixture.Structures);
        output.WriteLine($"FindDungeons: {found.Count} landblocks in {timer.Elapsed.TotalSeconds:F1} s; first: " +
                         string.Join(", ", found.Take(5).Select(s => $"{s.Coord} {s.Kind} {s.EnvCellCount} cells")));
        Assert.Equal(1773 + 553, found.Count);
        Assert.Equal(new LandblockCoord(0x00, 0xD1), found[0].Coord);
        Assert.Equal(4213, found[0].EnvCellCount);
        Assert.Contains(found, s => s.Coord == new LandblockCoord(0x86, 0x03) && s.Kind == LandblockCellKind.Dungeon);
        Assert.Contains(found, s => s.Coord == new LandblockCoord(0x00, 0x07) && s.Kind == LandblockCellKind.Dungeon);
    }

    /// <summary>
    /// Sanity bounds only (PVS, start cell, SeenOutside, no truncation); the walk filters by the PVS itself, so this
    /// cannot catch missing cells. Completeness is checked against the ray oracle in
    /// <see cref="CellVisibilityExhaustiveTests"/>.
    /// </summary>
    [RequiresDatsFact]
    public void Portal_walks_from_sampled_cells_stay_inside_their_PVS()
    {
        var sample = fixture.Scan.SampleCells.Order().ToList();
        var timer = Stopwatch.StartNew();
        var walks = 0;
        var cellsSeen = 0L;
        var maxCells = 0;
        var truncated = 0;
        var saturated = 0L;
        var failures = new ConcurrentQueue<string>();
        Parallel.ForEach(sample.GroupBy(id => id >> 16), group =>
        {
            var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.FromId(group.Key << 16));
            foreach (var id in group)
            {
                var cell = set.GetCell(id)!;
                var floor = CellDiscovery.FloorPointInCell(set, cell);
                if (floor is null)
                {
                    continue;
                }

                var eye = floor.Position + new Vector3(0, 0, 1f);
                if (!set.Contains(cell, eye))
                {
                    eye = floor.Position + new Vector3(0, 0, 0.1f);
                }

                var result = PortalVisibility.FromInside(set, id, eye);
                Interlocked.Increment(ref walks);
                Interlocked.Add(ref cellsSeen, result.Cells.Count);
                Interlocked.Add(ref saturated, result.SaturatedCells);
                InterlockedMax(ref maxCells, result.Cells.Count);
                if (result.Truncated)
                {
                    Interlocked.Increment(ref truncated);
                }

                var pvs = PortalVisibility.PotentiallyVisibleSet(cell).ToHashSet();
                if (result.Cells[0].Cell.Id != id || result.Cells.Any(c => !pvs.Contains(c.Cell.Id)))
                {
                    failures.Enqueue($"0x{id:X8}: walk left the PVS or did not start at the viewer");
                }

                if (result.OutsideVisible && !cell.SeenOutside)
                {
                    failures.Enqueue($"0x{id:X8}: sees outside but is not SeenOutside");
                }
            }
        });

        output.WriteLine($"{walks} walks in {timer.Elapsed.TotalSeconds:F1} s: mean {(double)cellsSeen / Math.Max(1, walks):F1} cells, max {maxCells}, truncated {truncated}, saturated cells {saturated}");
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
        Assert.True(walks > 7000);
        Assert.Equal(0, truncated);
    }


    private static (long Inside, long Outside) FrontProbes(CellMesh mesh, CellStructure structure)
    {
        long inside = 0;
        long outside = 0;
        foreach (var sub in mesh.Mesh.SubMeshes.Where(s => s.Side == MeshSide.Positive))
        {
            for (var t = 0; t < sub.Indices.Length; t += 3)
            {
                var a = sub.Positions[sub.Indices[t]];
                var b = sub.Positions[sub.Indices[t + 1]];
                var c = sub.Positions[sub.Indices[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.Length() < 1e-6f)
                {
                    continue;
                }

                if (structure.Bsp.Contains(((a + b + c) / 3f) + (Vector3.Normalize(n) * 0.05f)))
                {
                    inside++;
                }
                else
                {
                    outside++;
                }
            }
        }

        return (inside, outside);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
