using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// One parallel pass over every LandBlockInfo and EnvCell in the cell dat, decoding each record twice (Lifestone.Data
/// and <see cref="IndependentCellDecoder"/>) and gathering the counters the exhaustive tests assert on. Only small
/// aggregates are kept, never the decoded records.
/// </summary>
public sealed class WorldCellScan
{
    private readonly ConcurrentQueue<string> _mismatches = new();
    private readonly ConcurrentDictionary<string, int> _histograms = new();
    private readonly ConcurrentDictionary<CellMeshKey, byte> _meshKeys = new();
    private readonly ConcurrentBag<LandblockCellSummary> _summaries = new();
    private readonly ConcurrentBag<uint> _sampleCells = new();
    private long _lbis;
    private long _envCells;
    private long _numCellsMismatch;
    private long _numCellsOvershoot;
    private long _portals;
    private long _insidePortals;
    private long _otherPortalOutOfRange;
    private long _notReciprocal;
    private long _anomaliesBeyondNumCells;
    private long _exactPairs;
    private long _exactPairsApart;
    private long _nonExactPairs;
    private long _portalSideDecided;
    private long _portalSideWrong;
    private long _buildings;
    private long _buildingPortals;
    private long _buildingPortalsBad;
    private long _buildingEntryNotInStabList;
    private long _restrictionTables;
    private long _invariantFailures;
    private float _worstExactPairDistance;
    private readonly object _worstGate = new();

    public static WorldCellScan Run(CellStructureLibrary structures, int sampleEvery = 97)
    {
        var scan = new WorldCellScan();
        var dats = structures.Dats;
        var surfaceTypes = new SurfaceTypeLookup(dats);
        var heightTable = dats.TerrainRegion.LandHeightTable;
        var landblocks = CellDiscovery.LandblocksWithInfo(dats);
        var timer = Stopwatch.StartNew();
        Parallel.ForEach(landblocks, coord => scan.ScanLandblock(structures, surfaceTypes, heightTable, coord, sampleEvery));
        scan.Elapsed = timer.Elapsed;
        return scan;
    }

    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyCollection<string> Mismatches => _mismatches;

    public IReadOnlyDictionary<string, int> Histograms => _histograms;

    public IReadOnlyList<CellMeshKey> MeshKeys => _meshKeys.Keys.ToArray();

    public IReadOnlyCollection<LandblockCellSummary> Summaries => _summaries;

    public IReadOnlyCollection<uint> SampleCells => _sampleCells;

    public long LandBlockInfos => _lbis;

    public long EnvCells => _envCells;

    public long NumCellsMismatch => _numCellsMismatch;

    public long NumCellsOvershoot => _numCellsOvershoot;

    public long Portals => _portals;

    public long InsidePortals => _insidePortals;

    public long OtherPortalOutOfRange => _otherPortalOutOfRange;

    public long NotReciprocal => _notReciprocal;

    public long AnomaliesBeyondNumCells => _anomaliesBeyondNumCells;

    public long ExactPairs => _exactPairs;

    public long ExactPairsApart => _exactPairsApart;

    public long NonExactPairs => _nonExactPairs;

    public float WorstExactPairDistance => _worstExactPairDistance;

    public long PortalSideDecided => _portalSideDecided;

    public long PortalSideWrong => _portalSideWrong;

    public long Buildings => _buildings;

    public long BuildingPortals => _buildingPortals;

    public long BuildingPortalsBad => _buildingPortalsBad;

    public long BuildingEntryNotInStabList => _buildingEntryNotInStabList;

    public long RestrictionTables => _restrictionTables;

    public long InvariantFailures => _invariantFailures;

    public int Histogram(string key) => _histograms.GetValueOrDefault(key);

    private void Count(string key) => _histograms.AddOrUpdate(key, 1, (_, v) => v + 1);

    private void Fail(string message)
    {
        Interlocked.Increment(ref _invariantFailures);
        if (_mismatches.Count < 50)
        {
            _mismatches.Enqueue(message);
        }
    }

    private void ScanLandblock(CellStructureLibrary structures, SurfaceTypeLookup surfaceTypes, IReadOnlyList<float> heightTable, LandblockCoord coord, int sampleEvery)
    {
        var dats = structures.Dats;
        Interlocked.Increment(ref _lbis);

        // ---- LandBlockInfo: two decoders
        var lbiBytes = CellRecordReader.ReadCellBytes(dats, coord.LandBlockInfoId)!;
        var statics = LandblockStatics.Decode(coord, lbiBytes);
        var lbi = IndependentCellDecoder.DecodeLandBlockInfo(lbiBytes);
        CompareLbi(coord, statics, lbi, lbiBytes.Length);
        foreach (var obj in statics.Objects)
        {
            Count($"statictype:{obj.ModelId >> 24:X2}");
        }

        if (statics.RestrictionTable.Count > 0)
        {
            Interlocked.Increment(ref _restrictionTables);
        }

        foreach (var key in statics.RestrictionTable.Keys)
        {
            var own = CellIds.BlockMask(key) == CellIds.Make(coord, 0);
            Count(key == 0 ? "restrictionkey:zero"
                : own && CellIds.IsLandCell(key) ? "restrictionkey:landcell"
                : own && CellIds.IsEnvCell(key) ? "restrictionkey:envcell"
                : "restrictionkey:other");
        }

        // ---- EnvCells: two decoders
        var ids = CellRecordReader.EnvCellIds(dats, coord);
        var cells = new List<EnvCellData>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (id != CellIds.Make(coord, (ushort)(CellIds.FirstEnvCell + i)))
            {
                Fail($"{coord}: EnvCell ids not contiguous at {id:X8}");
            }

            var bytes = CellRecordReader.ReadCellBytes(dats, id)!;
            var mine = EnvCellData.Decode(id, bytes);
            var theirs = IndependentCellDecoder.DecodeEnvCell(bytes);
            CompareCell(mine, theirs, bytes.Length);
            cells.Add(mine);
            Count($"cellflags:{(uint)mine.Flags:X}");
            if (i % sampleEvery == 0)
            {
                _sampleCells.Add(id);
            }
        }

        Interlocked.Add(ref _envCells, cells.Count);
        if (cells.Count != statics.NumCells)
        {
            Interlocked.Increment(ref _numCellsMismatch);
            if (statics.NumCells > cells.Count)
            {
                Interlocked.Increment(ref _numCellsOvershoot);
            }
        }

        var set = LandblockCellSet.FromCells(structures, coord, cells);
        CheckBuildings(set, statics);
        CheckCells(set, statics, surfaceTypes);
        _summaries.Add(CellDiscovery.Summarize(set, statics, dats.GetLandblockTerrain(coord), heightTable));
    }

    private void CompareLbi(LandblockCoord coord, LandblockStatics mine, IndependentCellDecoder.LandBlockInfo theirs, int length)
    {
        var ok = theirs.Consumed == length
            && theirs.Id == coord.LandBlockInfoId
            && theirs.NumCells == mine.NumCells
            && theirs.Objects.Length == mine.Objects.Count
            && theirs.Objects.Zip(mine.Objects).All(p => p.First.Id == p.Second.ModelId && SameFrame(p.First.Frame, p.Second.Frame))
            && theirs.Buildings.Length == mine.Buildings.Count
            && theirs.Restrictions.Count == mine.RestrictionTable.Count
            && theirs.Restrictions.All(kv => mine.RestrictionTable.TryGetValue(kv.Key, out var v) && v == kv.Value);
        for (var b = 0; ok && b < theirs.Buildings.Length; b++)
        {
            var t = theirs.Buildings[b];
            var m = mine.Buildings[b];
            ok = t.ModelId == m.ModelId && t.NumLeaves == m.NumLeaves && SameFrame(t.Frame, m.Frame) && t.Portals.Length == m.Portals.Count
                && t.Portals.Zip(m.Portals).All(p =>
                    p.First.Flags == p.Second.RawFlags && p.First.OtherCellId == p.Second.OtherCellId
                    && p.First.OtherPortalId == p.Second.OtherPortalIndex && p.First.Stabs.SequenceEqual(p.Second.StabList));
        }

        if (!ok)
        {
            Fail($"{coord}: LandBlockInfo decoders disagree");
        }
    }

    private void CompareCell(EnvCellData mine, IndependentCellDecoder.EnvCell theirs, int length)
    {
        var ok = theirs.Consumed == length
            && theirs.Id == mine.Id
            && theirs.Flags == (uint)mine.Flags
            && theirs.Surfaces.SequenceEqual(mine.SurfaceIds)
            && theirs.EnvironmentId == mine.EnvironmentId
            && theirs.CellStruct == mine.CellStructureIndex
            && SameFrame(theirs.Frame, mine.Frame)
            && theirs.Portals.Length == mine.Portals.Count
            && theirs.Portals.Zip(mine.Portals).All(p =>
                p.First.Flags == p.Second.RawFlags && p.First.PolygonId == p.Second.PolygonId
                && p.First.OtherCellId == p.Second.OtherCellId && p.First.OtherPortalId == p.Second.OtherPortalIndex)
            && theirs.Visible.SequenceEqual(mine.VisibleCellIds)
            && theirs.Statics.Length == mine.StaticObjects.Count
            && theirs.Statics.Zip(mine.StaticObjects).All(p => p.First.Id == p.Second.ModelId && SameFrame(p.First.Frame, p.Second.Frame))
            && theirs.RestrictionObj == mine.RestrictionObjectId;
        if (!ok)
        {
            Fail($"{mine}: EnvCell decoders disagree");
        }

        foreach (var p in theirs.Portals)
        {
            Count($"portalflags:{p.Flags}");
            if (((p.Flags & 4) != 0) != (p.RawOther == CellIds.OutsideLow))
            {
                Fail($"{mine}: outside flag and other-cell 0xFFFF disagree");
            }
        }
    }

    private static bool SameFrame(IndependentCellDecoder.Frame f, Placement p) =>
        Bits(f.Origin.X) == Bits(p.Origin.X) && Bits(f.Origin.Y) == Bits(p.Origin.Y) && Bits(f.Origin.Z) == Bits(p.Origin.Z)
        && Bits(f.Qw) == Bits(p.Orientation.W) && Bits(f.Qx) == Bits(p.Orientation.X)
        && Bits(f.Qy) == Bits(p.Orientation.Y) && Bits(f.Qz) == Bits(p.Orientation.Z);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private void CheckBuildings(LandblockCellSet set, LandblockStatics statics)
    {
        foreach (var building in statics.Buildings)
        {
            Interlocked.Increment(ref _buildings);
            if (building.Portals.Count == 0)
            {
                Count("buildings:nodoorways");
            }

            Count($"buildingmodeltype:{building.ModelId >> 24:X2}");
            foreach (var portal in building.Portals)
            {
                Interlocked.Increment(ref _buildingPortals);
                Count($"buildingportalflags:{portal.RawFlags}");
                var cell = set.GetCell(portal.OtherCellId);
                var ok = cell is not null
                    && portal.OtherPortalIndex >= 0 && portal.OtherPortalIndex < cell.Portals.Count
                    && cell.Portals[portal.OtherPortalIndex].LeadsOutside
                    && portal.StabList.All(id => set.GetCell(id) is not null);
                if (!ok)
                {
                    Interlocked.Increment(ref _buildingPortalsBad);
                }

                if (!portal.StabList.Contains(portal.OtherCellId))
                {
                    Interlocked.Increment(ref _buildingEntryNotInStabList);
                }
            }
        }
    }

    private void CheckCells(LandblockCellSet set, LandblockStatics statics, SurfaceTypeLookup surfaceTypes)
    {
        foreach (var cell in set.Cells)
        {
            var beyondNumCells = (cell.Id & 0xFFFF) - CellIds.FirstEnvCell >= statics.NumCells;
            CellStructure structure;
            try
            {
                structure = set.GetStructure(cell);
            }
            catch (InvalidDataException ex)
            {
                Fail($"{cell}: {ex.Message}");
                continue;
            }

            foreach (var surface in cell.SurfaceIds)
            {
                surfaceTypes.TypeOf(surface); // throws for a missing or malformed Surface
            }

            // Static objects carry landblock-local frames: their origins sit in or next to the cell.
            if (cell.StaticObjects.Count > 0)
            {
                var b = structure.Bounds;
                var corners = Enumerable.Range(0, 8).Select(k => cell.Frame.LocalToParent(new Vector3(
                    (k & 1) == 0 ? b.Min.X : b.Max.X, (k & 2) == 0 ? b.Min.Y : b.Max.Y, (k & 4) == 0 ? b.Min.Z : b.Max.Z))).ToArray();
                var min = corners.Aggregate(Vector3.Min) - new Vector3(2f);
                var max = corners.Aggregate(Vector3.Max) + new Vector3(2f);
                foreach (var obj in cell.StaticObjects)
                {
                    var o = obj.Frame.Origin;
                    var near = o.X >= min.X && o.Y >= min.Y && o.Z >= min.Z && o.X <= max.X && o.Y <= max.Y && o.Z <= max.Z;
                    Count(near ? "cellstatic:near" : "cellstatic:far");
                }
            }

            var hasOutsidePortal = cell.Portals.Any(p => p.LeadsOutside);
            Count(hasOutsidePortal
                ? (cell.SeenOutside ? "seenoutside:withoutsideportal" : "notseenoutside:withoutsideportal")
                : (cell.SeenOutside ? "seenoutside:nooutsideportal" : "notseenoutside:nooutsideportal"));
            if (!structure.HasDrawingBsp)
            {
                Count("nodrawingbsp:cells");
            }

            _meshKeys.TryAdd(new CellMeshKey(cell.EnvironmentId, cell.CellStructureIndex, cell.SurfaceIds.ToArray()), 0);

            foreach (var visible in cell.VisibleCellIds)
            {
                if (visible == cell.Id || set.GetCell(visible) is null)
                {
                    Fail($"{cell}: visible cell 0x{visible:X8} is itself or missing");
                }
            }

            foreach (var portal in cell.Portals)
            {
                Interlocked.Increment(ref _portals);
                var polygon = structure.GetPortal(portal.PolygonId);
                if (polygon is null)
                {
                    Fail($"{cell}: portal polygon {portal.PolygonId} is not a struct portal");
                    continue;
                }

                CheckPortalSide(cell, structure, polygon, portal);
                if (portal.LeadsOutside)
                {
                    continue;
                }

                Interlocked.Increment(ref _insidePortals);
                var other = set.GetCell(portal.OtherCellId);
                if (other is null)
                {
                    Fail($"{cell}: portal to missing cell 0x{portal.OtherCellId:X8}");
                    continue;
                }

                if (portal.OtherPortalIndex < 0 || portal.OtherPortalIndex >= other.Portals.Count)
                {
                    Interlocked.Increment(ref _otherPortalOutOfRange);
                    if (beyondNumCells)
                    {
                        Interlocked.Increment(ref _anomaliesBeyondNumCells);
                    }

                    continue;
                }

                var back = other.Portals[portal.OtherPortalIndex];
                if (back.OtherCellId != cell.Id)
                {
                    Interlocked.Increment(ref _notReciprocal);
                    if (beyondNumCells)
                    {
                        Interlocked.Increment(ref _anomaliesBeyondNumCells);
                    }

                    continue;
                }

                var otherPolygon = set.GetStructure(other).GetPortal(back.PolygonId);
                if (otherPolygon is null)
                {
                    continue;
                }

                var mineWorld = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
                var otherWorld = otherPolygon.Vertices.Select(other.Frame.LocalToParent).ToArray();
                var distance = CellGeometryMath.SetDistance(mineWorld, otherWorld);
                if (portal.ExactMatch)
                {
                    Interlocked.Increment(ref _exactPairs);
                    if (distance > 0.01f)
                    {
                        Interlocked.Increment(ref _exactPairsApart);
                    }

                    lock (_worstGate)
                    {
                        _worstExactPairDistance = MathF.Max(_worstExactPairDistance, distance);
                    }
                }
                else
                {
                    Interlocked.Increment(ref _nonExactPairs);
                }
            }
        }
    }

    private void CheckPortalSide(EnvCellData cell, CellStructure structure, PortalPolygon polygon, CellPortalData portal)
    {
        // Probe 5 cm to each side of the portal's centre: exactly one should be inside the cell, on the interior side.
        var centre = polygon.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v) / polygon.Vertices.Length;
        var front = structure.Bsp.Contains(centre + (polygon.Plane.Normal * 0.05f));
        var behind = structure.Bsp.Contains(centre - (polygon.Plane.Normal * 0.05f));
        if (front == behind)
        {
            return;
        }

        Interlocked.Increment(ref _portalSideDecided);
        var interiorNegative = behind;
        if (interiorNegative != portal.PortalSide)
        {
            Interlocked.Increment(ref _portalSideWrong);
        }
    }
}
