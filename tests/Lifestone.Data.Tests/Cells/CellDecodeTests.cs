using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// Known records checked against an independent path: values printed by the research Python reader
/// (scratchpad research/scout acformats.py parse_envcell / parse_landblock_info, run read-only on the same dats) and
/// ACE facts (character start position, Town Network landblock).
/// </summary>
public sealed class CellDecodeTests(CellsFixture fixture) : IClassFixture<CellsFixture>
{
    private const float Tol = 1e-4f;

    /// <summary>ACE <c>ACE.Entity/CharacterPositionExtensions.cs StartingPosition</c> (Holtburg start, 2248343981).</summary>
    private const uint AceStartCell = 0x860301ADu;

    private static readonly Vector3 AceStartOrigin = new(12.3199f, -28.482f, 0.0049999995f);

    /// <summary>ACE <c>ACE.Common/GameConfiguration.cs</c> PreloadedLandblocks: "0007FFFF" is the Town Network.</summary>
    private static readonly LandblockCoord TownNetwork = new(0x00, 0x07);

    private static readonly LandblockCoord Academy = new(0x86, 0x03);

    [RequiresDatsFact]
    public void Holtburg_building_entry_cell_A9B40100_matches_python_reader()
    {
        var cell = EnvCellData.Read(fixture.Dats, 0xA9B40100u)!;
        Assert.Equal(EnvCellFlags.SeenOutside | EnvCellFlags.HasStaticObjs, cell.Flags);
        Assert.True(cell.SeenOutside);
        Assert.Equal(0x0D00034Fu, cell.EnvironmentId);
        Assert.Equal(0, cell.CellStructureIndex);
        Assert.Equal(11, cell.SurfaceIds.Count);
        Assert.Equal(0x08000371u, cell.SurfaceIds[0]);
        Assert.Equal(0x080006E8u, cell.SurfaceIds[^1]);
        AssertVector(new Vector3(84.0929f, 131.542f, 66f), cell.Frame.Origin);
        AssertQuaternionWxyz(0.707107f, 0f, 0f, -0.707107f, cell.Frame.Orientation);

        // [f7 poly25 ocFFFF op65535] [f7 poly26 ...] [f1 poly30 oc0110 op0] [f1 poly28 oc0102 op0] [f1 poly29 oc0103 op1] [f7 poly27 ...]
        var expected = new (ushort Flags, ushort Poly, uint Other, int OtherPortal)[]
        {
            (7, 25, CellIds.Outside, -1), (7, 26, CellIds.Outside, -1), (1, 30, 0xA9B40110u, 0), (1, 28, 0xA9B40102u, 0),
            (1, 29, 0xA9B40103u, 1), (7, 27, CellIds.Outside, -1),
        };
        Assert.Equal(expected, cell.Portals.Select(p => (p.RawFlags, p.PolygonId, p.OtherCellId, p.OtherPortalIndex)).ToArray());
        Assert.All(cell.Portals.Where(p => p.RawFlags == 7), p => Assert.True(p.LeadsOutside && p.ExactMatch && !p.PortalSide));
        Assert.All(cell.Portals.Where(p => p.RawFlags == 1), p => Assert.True(!p.LeadsOutside && p.ExactMatch && p.PortalSide));

        Assert.Equal(Enumerable.Range(0x101, 16).Select(i => 0xA9B40000u | (uint)i), cell.VisibleCellIds);
        Assert.Equal(22, cell.StaticObjects.Count);
        Assert.Equal(0x01000C20u, cell.StaticObjects[0].ModelId);
        AssertVector(new Vector3(92.0929f, 131.542f, 68f), cell.StaticObjects[0].Frame.Origin);
        Assert.Equal(0u, cell.RestrictionObjectId);
    }

    [RequiresDatsFact]
    public void Holtburg_cell_A9B4010F_matches_python_reader()
    {
        var cell = EnvCellData.Read(fixture.Dats, 0xA9B4010Fu)!;
        Assert.Equal(EnvCellFlags.SeenOutside, cell.Flags);
        Assert.Equal(15, cell.CellStructureIndex);
        Assert.Equal(new[] { (3, (ushort)2, 0xA9B40105u, 1), (3, (ushort)3, 0xA9B40101u, 3) },
            cell.Portals.Select(p => ((int)p.RawFlags, p.PolygonId, p.OtherCellId, p.OtherPortalIndex)).ToArray());
        Assert.All(cell.Portals, p => Assert.False(p.PortalSide)); // flags 3: bit 1 set
        Assert.Empty(cell.StaticObjects);
        Assert.DoesNotContain(cell.Id, cell.VisibleCellIds);
        Assert.Equal(16, cell.VisibleCellIds.Count);
    }

    [RequiresDatsFact]
    public void Holtburg_LandBlockInfo_matches_python_reader()
    {
        var lbi = LandblockStatics.Read(fixture.Dats, LandblockCoord.Holtburg)!;
        Assert.Equal(123u, lbi.NumCells);
        Assert.Equal(114, lbi.Objects.Count);
        Assert.Equal(28, lbi.Objects.Count(o => o.IsGfxObj));
        Assert.Equal(86, lbi.Objects.Count(o => o.IsSetup));
        Assert.Equal(12, lbi.Buildings.Count);
        Assert.Empty(lbi.RestrictionTable);

        var inn = lbi.Buildings[0];
        Assert.Equal(0x01000C1Eu, inn.ModelId);
        AssertVector(new Vector3(84.0929f, 131.542f, 66f), inn.Frame.Origin);
        Assert.Equal(64u, inn.NumLeaves);
        var expected = new (uint Cell, int Portal)[]
        {
            (0x100, 0), (0x100, 1), (0x100, 5), (0x103, 0), (0x102, 1), (0x106, 0), (0x107, 0), (0x109, 1), (0x10A, 1), (0x10B, 0),
        };
        Assert.Equal(expected.Select(e => (0xA9B40000u | e.Cell, e.Portal)), inn.Portals.Select(p => (p.OtherCellId, p.OtherPortalIndex)));
        Assert.All(inn.Portals, p =>
        {
            Assert.Equal(1, p.RawFlags);
            Assert.True(p.ExactMatch && p.PortalSide);
            Assert.Equal(17, p.StabList.Count);
        });

        var second = lbi.Buildings[1];
        Assert.Equal(0x01000BC3u, second.ModelId);
        AssertVector(new Vector3(31.5f, 159.5f, 66f), second.Frame.Origin);
        Assert.Equal(36u, second.NumLeaves);
        Assert.Equal(new uint[] { 0xA9B40113, 0xA9B40114, 0xA9B40115 }, second.Portals.Select(p => p.OtherCellId));
        Assert.All(second.Portals, p => Assert.Equal(5, p.StabList.Count));
    }

    [RequiresDatsFact]
    public void Ace_start_cell_860301AD_matches_python_reader()
    {
        var cell = EnvCellData.Read(fixture.Dats, AceStartCell)!;
        Assert.Equal(EnvCellFlags.HasStaticObjs, cell.Flags);
        Assert.False(cell.SeenOutside);
        Assert.Equal(0x0D0001C9u, cell.EnvironmentId);
        Assert.Equal(new uint[] { 0x08000374, 0x08000034 }, new[] { cell.SurfaceIds[0], cell.SurfaceIds[^1] });
        Assert.Equal(3, cell.SurfaceIds.Count);
        AssertVector(new Vector3(10f, -30f, 0f), cell.Frame.Origin);
        AssertQuaternionWxyz(-0f, 0f, 0f, -1f, cell.Frame.Orientation);
        Assert.Equal(new[] { (2, (ushort)12, 0x860301B4u, 0), (3, (ushort)13, 0x860302E2u, 0) },
            cell.Portals.Select(p => ((int)p.RawFlags, p.PolygonId, p.OtherCellId, p.OtherPortalIndex)).ToArray());
        Assert.Equal(58, cell.VisibleCellIds.Count);
        Assert.Equal(30, cell.StaticObjects.Count);
        Assert.Equal(0x0100022Bu, cell.StaticObjects[0].ModelId);
        AssertVector(new Vector3(10.388f, -25.102f, 3.857f), cell.StaticObjects[0].Frame.Origin);
    }

    [RequiresDatsFact]
    public void Town_network_cells_match_python_reader()
    {
        var first = EnvCellData.Read(fixture.Dats, 0x00070100u)!;
        Assert.Equal(0x0D000103u, first.EnvironmentId);
        Assert.Equal(9, first.SurfaceIds.Count);
        Assert.Equal(0x0800010Bu, first.SurfaceIds[0]);
        Assert.Equal(0x080004BFu, first.SurfaceIds[^1]);
        AssertVector(new Vector3(60f, -160f, -6f), first.Frame.Origin);
        Assert.Equal(72, first.VisibleCellIds.Count);
        Assert.Equal(15, first.StaticObjects.Count);
        Assert.Equal(0x0200129Du, first.StaticObjects[0].ModelId);

        var hub = EnvCellData.Read(fixture.Dats, 0x00070145u)!;
        Assert.Equal(0x0D0000CBu, hub.EnvironmentId);
        Assert.Equal(5, hub.Portals.Count);
        Assert.Equal(204, hub.VisibleCellIds.Count);
        Assert.Equal(7, hub.StaticObjects.Count);
    }

    [RequiresDatsTheory]
    [InlineData(0x86, 0x03, 2163, 0x86030972u, 568u, 5089, 0, 123531, 975, 0, 155, 0, 0)]
    [InlineData(0x00, 0x07, 498, 0x000702F1u, 205u, 1299, 0, 60360, 1218, 0, 103, 0, 0)]
    [InlineData(0xA9, 0xB4, 138, 0xA9B40189u, 123u, 319, 53, 1770, 435, 138, 123, 114, 12)]
    public void Landblock_aggregates_match_python_reader(
        int x, int y, int envCells, uint lastId, uint numCellsField, int portals, int outsidePortals, int visible, int statics,
        int seenOutside, int distinctStructs, int lbiObjects, int buildings)
    {
        var coord = new LandblockCoord((byte)x, (byte)y);
        var set = LandblockCellSet.Load(fixture.Structures, coord);
        var lbi = LandblockStatics.Read(fixture.Dats, coord)!;
        Assert.Equal(envCells, set.Count);
        Assert.Equal(lastId, set.Ids[^1]);
        Assert.Equal(CellIds.Make(coord, CellIds.FirstEnvCell), set.Ids[0]);
        Assert.Equal(numCellsField, lbi.NumCells);
        Assert.Equal(portals, set.Cells.Sum(c => c.Portals.Count));
        Assert.Equal(outsidePortals, set.Cells.Sum(c => c.Portals.Count(p => p.LeadsOutside)));
        Assert.Equal(visible, set.Cells.Sum(c => c.VisibleCellIds.Count));
        Assert.Equal(statics, set.Cells.Sum(c => c.StaticObjects.Count));
        Assert.Equal(seenOutside, set.Cells.Count(c => c.SeenOutside));
        Assert.Equal(distinctStructs, set.Cells.Select(c => (c.EnvironmentId, c.CellStructureIndex)).Distinct().Count());
        Assert.Equal(lbiObjects, lbi.Objects.Count);
        Assert.Equal(buildings, lbi.Buildings.Count);
    }

    [RequiresDatsFact]
    public void Ace_start_position_lies_inside_its_cell_on_the_floor()
    {
        var set = LandblockCellSet.Load(fixture.Structures, Academy);
        var cell = set.GetCell(AceStartCell)!;
        Assert.True(set.Contains(cell, AceStartOrigin), "ACE's start position must be inside cell 0x860301AD");
        Assert.Equal(AceStartCell, set.FindCell(AceStartOrigin)!.Id);
        Assert.Equal(AceStartCell, set.FindCell(AceStartOrigin, hintCellId: 0x860301B4u)!.Id);

        // ACE places characters 5 mm above the floor: the cell has a floor polygon at z = 0 under the point.
        var structure = set.GetStructure(cell);
        var local = cell.Frame.ParentToLocal(AceStartOrigin);
        Assert.Contains(structure.Floors, f => MathF.Abs(f.Plane.Distance(local) - 0.005f) < 1e-3f && f.Plane.Normal.Z > 0.99f);
        Assert.False(set.Contains(cell, AceStartOrigin - new Vector3(0, 0, 0.5f)), "half a metre below the floor is outside the cell");
    }

    [RequiresDatsFact]
    public void Ace_dungeon_landblocks_are_classified_as_dungeons()
    {
        foreach (var coord in new[] { Academy, TownNetwork })
        {
            var summary = CellDiscovery.Summarize(fixture.Structures, coord);
            Assert.True(summary.AceIsDungeon, $"{coord}: ACE Landblock.IsDungeon");
            Assert.Equal(LandblockCellKind.Dungeon, summary.Kind);
            Assert.Equal(0, summary.BuildingCellCount);
            Assert.Equal(summary.EnvCellCount, summary.DetachedCellCount);
            Assert.True(summary.AllTerrainHeightsZero);

            var set = LandblockCellSet.Load(fixture.Structures, coord);
            var entry = CellDiscovery.FindDungeonEntry(set, LandblockStatics.Read(fixture.Dats, coord));
            Assert.NotNull(entry);
            Assert.True(set.Contains(set.GetCell(entry.CellId)!, entry.Position + new Vector3(0, 0, 0.01f)));
            Assert.True(entry.FloorNormal.Z >= CellStructure.FloorZ);
        }

        var holtburg = CellDiscovery.Summarize(fixture.Structures, LandblockCoord.Holtburg);
        Assert.False(holtburg.AceIsDungeon);
        Assert.Equal(138, holtburg.EnvCellCount);
        Assert.Equal(12, holtburg.BuildingCount);
        Assert.Equal(138, holtburg.SeenOutsideCellCount);
    }

    [RequiresDatsFact]
    public void Holtburg_building_entry_point_is_in_an_entry_cell()
    {
        var set = LandblockCellSet.Load(fixture.Structures, LandblockCoord.Holtburg);
        var inn = LandblockStatics.Read(fixture.Dats, LandblockCoord.Holtburg)!.Buildings[0];
        var entry = CellDiscovery.FindBuildingEntry(set, inn);
        Assert.NotNull(entry);
        Assert.Contains(entry.CellId, inn.Portals.Select(p => p.OtherCellId));
        Assert.Equal(entry.CellId, set.FindCell(entry.Position + new Vector3(0, 0, 0.01f), entry.CellId)!.Id);
        // The inn floor is on the town plateau (66 m), within a metre.
        Assert.InRange(entry.Position.Z, 65f, 67.5f);
    }

    [RequiresDatsFact]
    public void Enumerating_cells_uses_the_btree_not_num_cells()
    {
        var ids = CellDiscovery.EnumerateEnvCellIds(fixture.Dats, LandblockCoord.Holtburg);
        Assert.Equal(138, ids.Count);
        Assert.Equal(Enumerable.Range(0x100, 138).Select(i => 0xA9B40000u | (uint)i), ids);
        var fromNumCells = CellDiscovery.EnvCellIdsFromNumCells(LandblockCoord.Holtburg, 123);
        Assert.Equal(ids.Take(123), fromNumCells);
        Assert.Empty(CellDiscovery.EnumerateEnvCellIds(fixture.Dats, new LandblockCoord(0x00, 0x00)));
    }

    [RequiresDatsFact]
    public void Missing_records_return_null()
    {
        Assert.Null(EnvCellData.Read(fixture.Dats, 0xA9B40FFFu));
        Assert.Null(LandblockStatics.Read(fixture.Dats, new LandblockCoord(0x00, 0x00)));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvCellData.Read(fixture.Dats, 0xA9B40001u));
    }

    [Fact]
    public void Decode_rejects_trailing_bytes_and_wrong_ids()
    {
        // A minimal valid EnvCell: id, flags 0, id, 0 surfaces, 0 portals, 0 visible, env 1, struct 0, identity frame.
        var bytes = new List<byte>();
        void U32(uint v) => bytes.AddRange(BitConverter.GetBytes(v));
        void U16(ushort v) => bytes.AddRange(BitConverter.GetBytes(v));
        void F(float v) => bytes.AddRange(BitConverter.GetBytes(v));
        U32(0x12340100); U32(0); U32(0x12340100); bytes.Add(0); bytes.Add(0); U16(0); U16(1); U16(0);
        F(1); F(2); F(3); F(1); F(0); F(0); F(0);
        var ok = EnvCellData.Decode(0x12340100, bytes.ToArray());
        Assert.Equal(new Vector3(1, 2, 3), ok.Frame.Origin);
        Assert.Equal(Quaternion.Identity, ok.Frame.Orientation);
        Assert.Equal(0x0D000001u, ok.EnvironmentId);

        Assert.Throws<InvalidDataException>(() => EnvCellData.Decode(0x12340100, [.. bytes, 0]));
        Assert.Throws<InvalidDataException>(() => EnvCellData.Decode(0x12340101, bytes.ToArray()));
        Assert.Throws<InvalidDataException>(() => EnvCellData.Decode(0x12340100, bytes.Take(20).ToArray()));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tol, $"expected {expected}, got {actual}");
    }

    private static void AssertQuaternionWxyz(float w, float x, float y, float z, Quaternion actual)
    {
        Assert.True(
            MathF.Abs(actual.W - w) < Tol && MathF.Abs(actual.X - x) < Tol && MathF.Abs(actual.Y - y) < Tol && MathF.Abs(actual.Z - z) < Tol,
            $"expected wxyz ({w}, {x}, {y}, {z}), got {actual}");
    }
}
