using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Tests.Cells;

public sealed class CellMathTests
{
    [Fact]
    public void Placement_matrix_point_and_inverse_agree()
    {
        // Heading 90 degrees (AC turns clockwise from north): a rotation of -90 degrees about +Z, stored w,x,y,z =
        // (0.7071, 0, 0, -0.7071) like the Holtburg inn.
        var frame = new Placement(new Vector3(84f, 131.5f, 66f), new Quaternion(0f, 0f, -MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)));
        var local = new Vector3(1f, 2f, 3f);
        var parent = frame.LocalToParent(local);
        Assert.True(Vector3.Distance(new Vector3(84f + 2f, 131.5f - 1f, 69f), parent) < 1e-4f, parent.ToString());
        Assert.True(Vector3.Distance(parent, Vector3.Transform(local, frame.ToMatrix())) < 1e-4f);
        Assert.True(Vector3.Distance(local, frame.ParentToLocal(parent)) < 1e-4f);
    }

    [Fact]
    public void Plane_follows_retail_make_plane_and_moves_with_its_frame()
    {
        // CCW seen from +Z: normal +Z.
        var square = new[] { new Vector3(0, 0, 2), new Vector3(1, 0, 2), new Vector3(1, 1, 2), new Vector3(0, 1, 2) };
        var plane = CellPlane.FromPolygon(square);
        Assert.Equal(Vector3.UnitZ, plane.Normal);
        Assert.Equal(-2f, plane.D);
        Assert.Equal(PlaneSide.Positive, plane.SideOf(new Vector3(0, 0, 3)));
        Assert.Equal(PlaneSide.Negative, plane.SideOf(new Vector3(0, 0, 1)));
        Assert.Equal(PlaneSide.OnPlane, plane.SideOf(new Vector3(5, 5, 2.0001f)));

        var frame = new Placement(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        var moved = plane.ToParent(frame);
        foreach (var v in square)
        {
            Assert.Equal(0f, moved.Distance(frame.LocalToParent(v)), 4);
        }

        Assert.False(CellPlane.TryFromPolygon([Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2], out _));
        Assert.Throws<InvalidDataException>(() => CellPlane.FromPolygon([Vector3.Zero, Vector3.UnitX]));
    }

    [Fact]
    public void Cell_ids_follow_the_dat_layout()
    {
        var holtburg = LandblockCoord.Holtburg;
        Assert.Equal(0xA9B40100u, CellIds.Make(holtburg, CellIds.FirstEnvCell));
        Assert.True(CellIds.IsEnvCell(0xA9B40100u));
        Assert.False(CellIds.IsEnvCell(0xA9B4FFFEu));
        Assert.True(CellIds.IsLandCell(0xA9B40001u));
        Assert.Equal(0xA9B40000u, CellIds.BlockMask(0xA9B40123u));
        Assert.Equal(0xA9B40001u, CellIds.LandCellAt(holtburg, 0f, 0f));
        Assert.Equal(0xA9B40040u, CellIds.LandCellAt(holtburg, 191.9f, 191.9f));
        Assert.Equal(0xA9B40019u, CellIds.LandCellAt(holtburg, 84f, 7.1f)); // ACE PlayerFactory fallback 0xA9B40019 at (84, 7.1)
        Assert.Null(CellIds.LandCellAt(holtburg, 192f, 0f));
    }
}
