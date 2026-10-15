using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.World;

/// <summary>
/// <see cref="CellFrameExtensions"/>: a <see cref="CellFrame"/> converts to the Client's <see cref="CellPosition"/>
/// and global metres exactly as <see cref="AcCoordinates"/> does, and AcCoordinates still answers through
/// <see cref="LandDefs"/>.
/// </summary>
public sealed class CellFrameExtensionsTests
{
    [Theory]
    [InlineData(0xA9B40019u, 84f, 7.1f, 94f)] // Holtburg, outdoors
    [InlineData(0x01D90100u, 3f, -49.9f, 0f)] // an EnvCell: same formula, the cell's landblock
    [InlineData(0xFEFE0040u, 191.9f, 191.9f, 0f)] // the north-east corner of the map
    public void To_global_agrees_with_AcCoordinates(uint cellId, float x, float y, float z)
    {
        var origin = new Vector3(x, y, z);
        var frame = CellFrame.FromWire(cellId, origin, 0.70710677f, 0f, 0f, -0.70710677f);

        var position = frame.ToCellPosition();
        Assert.Equal(new CellPosition(cellId, origin), position);
        Assert.Equal(AcCoordinates.ToGlobal(position), frame.ToGlobal());
    }

    [Fact]
    public void AcCoordinates_and_LandDefs_agree_on_adjust_to_outside()
    {
        foreach (var (cellId, local) in new[]
        {
            (0xA9B40001u, new Vector3(192.5f, 7f, 3f)),
            (0xA9B40001u, new Vector3(-0.5f, -0.5f, 0f)),
            (0x00000001u, new Vector3(-0.5f, 10f, 0f)),
            (0xFEFE0001u, new Vector3(191.9f, 191.9f, 0f)),
            (0xA9B40100u, new Vector3(84f, 7.1f, 94f)),
        })
        {
            var (aCell, aLocal) = (cellId, local);
            var (bCell, bLocal) = (cellId, local);
            var a = AcCoordinates.AdjustToOutside(ref aCell, ref aLocal);
            var b = LandDefs.AdjustToOutside(ref bCell, ref bLocal);
            Assert.Equal((b, bCell, bLocal), (a, aCell, aLocal));
        }
    }

    [Fact]
    public void A_frame_s_heading_matches_AcCoordinates_heading_of_its_forward_axis()
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-110.9 * Math.PI / 180.0));
        var frame = new CellFrame(0xA9B40019, new AcFrame(Vector3.Zero, q));
        var forward = Vector3.Transform(Vector3.UnitY, q);
        Assert.Equal(AcCoordinates.HeadingOf(forward.X, forward.Y), frame.HeadingDegrees, 9);
        Assert.Equal(110.9, frame.HeadingDegrees, 3);
    }
}
