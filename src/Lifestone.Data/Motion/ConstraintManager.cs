using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>ConstraintManager</c>, re-expressed from the 11.4186 decompile (PORTAL/cpmanager/CConstraintManager.c;
/// names and addresses cited, nothing copied): keeps the player near the position the server last confirmed. After
/// <see cref="ConstrainTo"/>, each frame's offset is slowed once the distance moved since passes the start distance,
/// and stopped at the maximum. Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para><b>The distance accumulates, as in retail.</b> <see cref="ConstrainTo"/> seeds <see cref="Offset"/> with the
/// distance from the confirmed position to the object; every <see cref="AdjustOffset"/> then adds the length of the
/// (scaled) step, in contact or not. It is a path length, not a distance from the confirmed position, so only the next
/// <see cref="ConstrainTo"/> lowers it. ACE assigns the step's length instead of adding it (ConstraintManager.cs:76), which
/// keeps the factor near 1 forever.</para>
/// <para>The physics track's player controller calls <see cref="ConstrainTo"/> for every newer server position
/// (<c>SmartBox::HandleReceivedPosition</c>) with <see cref="PositionManager.GetStartConstraintDistance"/> and
/// <see cref="PositionManager.GetMaxConstraintDistance"/>.</para>
/// </remarks>
public sealed class ConstraintManager
{
    /// <summary>Retail <c>is_constrained</c>.</summary>
    public bool IsConstrained { get; private set; }

    /// <summary>Retail <c>constraint_pos</c>: the confirmed position (kept, never read by the adjustment).</summary>
    public CellFrame ConstraintPosition { get; private set; } = new(0, AcFrame.Identity);

    /// <summary>Retail <c>constraint_distance_start</c>: the distance where the slow-down begins.</summary>
    public float StartDistance { get; private set; }

    /// <summary>Retail <c>constraint_distance_max</c>: the distance where movement stops.</summary>
    public float MaxDistance { get; private set; }

    /// <summary>Retail <c>constraint_pos_offset</c>: the distance accumulated since <see cref="ConstrainTo"/>.</summary>
    public float Offset { get; private set; }

    /// <summary>
    /// Retail <c>IsFullyConstrained</c> (0x005560D0; TODO(avatars): 11.6096): the accumulated distance is beyond 90% of the
    /// maximum. The motion interpreter then refuses a jump.
    /// </summary>
    public bool IsFullyConstrained => MaxDistance * 0.9 < Offset;

    /// <summary>
    /// Retail <c>ConstrainTo</c> (0x00556240; TODO(avatars): 11.6096, unmapped): constrains to <paramref name="position"/>
    /// with the given distances; the accumulated distance starts at the distance from it to <paramref name="current"/>.
    /// </summary>
    public void ConstrainTo(in CellFrame position, float startDistance, float maxDistance, in CellFrame current)
    {
        IsConstrained = true;
        ConstraintPosition = position;
        StartDistance = startDistance;
        MaxDistance = maxDistance;
        Offset = PositionFrames.Distance(position, current);
    }

    /// <summary>Retail <c>UnConstrain</c> (0x005560C0; TODO(avatars): 11.6096): stops constraining; the distances stay.</summary>
    public void UnConstrain() => IsConstrained = false;

    /// <summary>
    /// Retail <c>adjust_offset</c> (0x00556180 / 0x00556DA0): while constrained and in contact, the offset's origin is
    /// scaled by <c>(max − accumulated) / (max − start)</c> once the accumulated distance passes the start, and zeroed
    /// at the maximum; then, in contact or not, the length of the resulting origin is added to the accumulated
    /// distance. The rotation is never changed, and <paramref name="quantum"/> is not used (retail passes it too).
    /// </summary>
    public void AdjustOffset(ref AcFrame offset, double quantum, in PositionContext ctx)
    {
        if (!IsConstrained)
        {
            return;
        }

        if (ctx.HasContact)
        {
            if (Offset < (double)MaxDistance)
            {
                if (Offset > (double)StartDistance)
                {
                    var factor = (MaxDistance - Offset) / (MaxDistance - StartDistance);
                    offset = offset with { Origin = offset.Origin * factor };
                }
            }
            else
            {
                offset = offset with { Origin = default };
            }
        }

        var o = offset.Origin;
        Offset = (float)(Math.Sqrt(((double)o.X * o.X) + ((double)o.Y * o.Y) + ((double)o.Z * o.Z)) + Offset);
    }
}
