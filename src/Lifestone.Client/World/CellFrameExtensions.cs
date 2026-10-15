using Lifestone.Data.World;

namespace Lifestone.Client.World;

/// <summary>
/// Conversions from the data layer's <see cref="CellFrame"/> (a full retail <c>Position</c>) to the Client's coordinate
/// types: the rotation-free <see cref="CellPosition"/> and global metres (<see cref="AcCoordinates.ToGlobal"/>).
/// </summary>
public static class CellFrameExtensions
{
    /// <summary>The cell id and origin of <paramref name="frame"/>, without the rotation.</summary>
    public static CellPosition ToCellPosition(this CellFrame frame) => new(frame.CellId, frame.Frame.Origin);

    /// <summary>
    /// Global metres of the origin: <c>landblock * 192 + local</c> for any cell kind (<see cref="AcCoordinates.ToGlobal"/>,
    /// ACE <c>PositionExtensions.ToGlobal</c> with <c>skipIndoors = false</c>).
    /// </summary>
    public static GlobalPosition ToGlobal(this CellFrame frame) => AcCoordinates.ToGlobal(frame.ToCellPosition());
}
