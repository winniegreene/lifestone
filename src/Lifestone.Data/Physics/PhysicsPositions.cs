// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/Position.cs (GetOffset, LocalToGlobal,
// LocalToLocal, Distance) and Source/ACE.Server/Physics/Animation/AFrame.cs (GlobalToLocal) (AGPL-3.0)
// with changes: extension methods on the shared CellFrame (m2-plan §4.2) instead of a Position class, so nothing
// allocates; block offsets come from Lifestone.Data.World.LandDefs.

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics;

/// <summary>
/// Retail <c>Position</c> arithmetic on <see cref="CellFrame"/>: moving points and offsets between the landblock frames
/// of two positions. A position's frame lives in its cell's landblock space, so a point of one position is expressed in
/// another's space by adding the block offset between their landblocks (<see cref="LandDefs.GetBlockOffset"/>).
/// </summary>
public static class PhysicsPositions
{
    /// <summary>
    /// Retail <c>Position::get_offset</c> (11.4186 0x00509F60, 11.6096 0x0050AA30): the vector from
    /// <paramref name="from"/>'s origin to <paramref name="to"/>'s, in <paramref name="from"/>'s landblock space.
    /// </summary>
    public static Vector3 GetOffset(this in CellFrame from, in CellFrame to) =>
        LandDefs.GetBlockOffset(from.CellId, to.CellId) + to.Frame.Origin - from.Frame.Origin;

    /// <summary>
    /// The length of <see cref="GetOffset"/> (retail <c>Position::distance</c>, 11.4186 0x005A94B0, 11.6096 0x005AA560).
    /// </summary>
    public static float Distance(this in CellFrame from, in CellFrame to) => from.GetOffset(to).Length();

    /// <summary>
    /// Retail <c>Position::localtoglobal(to, from, point)</c> (11.4186 0x00456180, 11.6096 0x004562A0):
    /// <paramref name="point"/>, local to <paramref name="from"/>'s frame, in <paramref name="to"/>'s landblock space.
    /// </summary>
    public static Vector3 LocalToGlobal(this in CellFrame to, in CellFrame from, Vector3 point) =>
        LandDefs.GetBlockOffset(to.CellId, from.CellId) + from.Frame.LocalToGlobal(point);

    /// <summary>
    /// Retail <c>Position::localtolocal(to, from, point)</c> (11.4186 0x004527F0, 11.6096 0x00452830):
    /// <paramref name="point"/>, local to <paramref name="from"/>'s frame, in <paramref name="to"/>'s frame.
    /// </summary>
    public static Vector3 LocalToLocal(this in CellFrame to, in CellFrame from, Vector3 point) =>
        to.Frame.GlobalToLocal(LandDefs.GetBlockOffset(to.CellId, from.CellId) + from.Frame.LocalToGlobal(point));

    /// <summary>
    /// Retail <c>Frame::globaltolocal</c> (11.4186 0x00452680, 11.6096 0x004526C0): a point of the parent space in the
    /// frame's local space (the inverse of <see cref="AcFrame.LocalToGlobal"/>).
    /// </summary>
    public static Vector3 GlobalToLocal(this in AcFrame frame, Vector3 point) => frame.GlobalToLocalVec(point - frame.Origin);

    /// <summary>The same position with its origin moved by <paramref name="offset"/> (retail <c>Position::add_offset</c>).</summary>
    public static CellFrame AddOffset(this in CellFrame position, Vector3 offset) =>
        new(position.CellId, new AcFrame(position.Frame.Origin + offset, position.Frame.Orientation));
}
