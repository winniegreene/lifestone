using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Play;

/// <summary>
/// D26: the player is drawn at positions interpolated between retail's 30 Hz physics steps, not at the step positions
/// (<c>--motion-stepping=retail</c> restores stepping). Retail draws the last step's position until the next step; at 60 frames a second
/// that is a stutter of every other frame. The poses stay retail keyframes: only the body's frame is smoothed.
/// </summary>
/// <remarks>
/// <para>The player is shown as it was one step ago plus the time since the last step: between the step at <c>T1</c> (position
/// <c>P1</c>) and the one before it (<c>T0</c>, <c>P0</c>), at time <c>t</c> in [T1, T1 + (T1 - T0)) the draw position is
/// <c>P0 + (P1 - P0) * (t - T1) / (T1 - T0)</c>. That is continuous when the next step arrives, never extrapolates, and costs up to one step (33 ms)
/// of latency. A move longer than <see cref="MaxStepMetres"/> in one step (a teleport, a blip, a placement) is not interpolated.</para>
/// <para>The same interpolated frame is the pivot of the chase camera, so the character does not shake against the view.</para>
/// </remarks>
public static class DrawInterpolation
{
    /// <summary>A step longer than this many metres is a jump (a placement, a teleport, a server blip), shown as it is.</summary>
    public const double MaxStepMetres = 4.0;

    /// <summary>
    /// The frame to draw the player in, in the landblock space of <paramref name="current"/>'s cell. With <paramref name="stepping"/> (or no
    /// previous step, or a jump) it is <paramref name="current"/>.
    /// </summary>
    /// <param name="previous">The position before the last step.</param>
    /// <param name="current">The position after the last step.</param>
    /// <param name="previousStepTime">When the step before the last ran (seconds; NaN if none).</param>
    /// <param name="lastStepTime">When the last step ran (seconds; NaN if none).</param>
    /// <param name="now">The host's clock now (seconds).</param>
    /// <param name="stepping">True for retail's stepping (<c>--motion-stepping=retail</c>).</param>
    public static CellFrame Interpolate(in CellFrame previous, in CellFrame current, double previousStepTime, double lastStepTime, double now, bool stepping)
    {
        if (stepping || double.IsNaN(previousStepTime) || double.IsNaN(lastStepTime) || previous.CellId == 0)
        {
            return current;
        }

        var interval = lastStepTime - previousStepTime;
        if (!(interval > 1e-6))
        {
            return current;
        }

        var alpha = Math.Clamp((now - lastStepTime) / interval, 0.0, 1.0);
        if (alpha >= 1.0)
        {
            return current;
        }

        var from = InSpaceOf(previous, current.CellId);
        if (Vector3.DistanceSquared(from.Origin, current.Origin) > MaxStepMetres * MaxStepMetres)
        {
            return current;
        }

        var t = (float)alpha;
        var origin = Vector3.Lerp(from.Origin, current.Origin, t);
        var orientation = Quaternion.Normalize(Quaternion.Slerp(from.Orientation, current.Orientation, t));
        return new CellFrame(current.CellId, new AcFrame(origin, orientation));
    }

    /// <summary><paramref name="position"/>'s frame in the landblock space of <paramref name="cellId"/> (the origin moved by the difference of the landblocks' corners).</summary>
    public static CellFrame InSpaceOf(in CellFrame position, uint cellId)
    {
        var from = LandblockCoord.FromId(position.CellId);
        var to = LandblockCoord.FromId(cellId);
        if (from == to)
        {
            return position;
        }

        var a = AcCoordinates.LandblockOrigin(from);
        var b = AcCoordinates.LandblockOrigin(to);
        var shift = new Vector3((float)(a.X - b.X), (float)(a.Y - b.Y), (float)(a.Z - b.Z));
        return new CellFrame(position.CellId, position.Frame with { Origin = position.Origin + shift });
    }

    /// <summary>
    /// A rigid move of the drawn character from where the physics step left it to where it is drawn: the part frames of the avatars
    /// (in the anchor landblock's space) are moved by its translation and turned about the body's origin by
    /// its rotation.
    /// </summary>
    /// <param name="Translation">The move of the body's origin.</param>
    /// <param name="Rotation">The turn from the stepped orientation to the drawn one.</param>
    public readonly record struct Offset(Vector3 Translation, Quaternion Rotation)
    {
        /// <summary>No move.</summary>
        public static Offset None => new(Vector3.Zero, Quaternion.Identity);

        /// <summary>True when applying it changes nothing.</summary>
        public bool IsNone => Translation == Vector3.Zero && Rotation == Quaternion.Identity;

        /// <summary>The offset that carries <paramref name="stepped"/> (a physics position) to <paramref name="drawn"/> (same cell).</summary>
        public static Offset Between(in CellFrame stepped, in CellFrame drawn)
        {
            var delta = drawn.Origin - stepped.Origin;
            var turn = AcFrame.Hamilton(drawn.Orientation, Quaternion.Conjugate(stepped.Orientation));
            return delta == Vector3.Zero && turn == Quaternion.Identity ? None : new Offset(delta, Quaternion.Normalize(turn));
        }

        /// <summary>A part's frame moved with the body, whose origin (in the same space as the part) is <paramref name="bodyOrigin"/>.</summary>
        public AcFrame Apply(in AcFrame part, Vector3 bodyOrigin)
        {
            if (IsNone)
            {
                return part;
            }

            var origin = bodyOrigin + Translation + Vector3.Transform(part.Origin - bodyOrigin, Rotation);
            return new AcFrame(origin, AcFrame.SetRotate(AcFrame.Hamilton(Rotation, part.Orientation), part.Orientation));
        }
    }
}
