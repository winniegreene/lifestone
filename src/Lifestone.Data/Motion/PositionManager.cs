using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>PositionManager</c>, re-expressed from the 11.4186 decompile (PORTAL/cpmanager/CPositionManager.c; names
/// and addresses cited, nothing copied): an object's interpolation, sticky and constraint managers, applied in that
/// order to the offset of each update (<c>CPhysicsObj::UpdatePositionInternal</c>, 0x00512C30 / 0x00513730: the part
/// array's root motion, scaled by the object's scale on walkable ground and zeroed elsewhere, then
/// <see cref="AdjustOffset"/>, then combined onto the position). Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para>The interpolation manager is created up front (retail makes it on the first <c>InterpolateTo</c>; an empty
/// one changes nothing). The constraint manager is made on the first <see cref="ConstrainTo"/>, as in retail, because
/// <see cref="IsFullyConstrained"/> depends on whether it exists.</para>
/// <para>The sticky manager (<see cref="StickyManager"/>, between the other two) is made by the first
/// <see cref="StickTo"/>, as in retail. Hosts pass <see cref="GetStickyObjectId"/> as
/// <see cref="PositionContext.StickyObjectId"/>: the interpolation manager skips its progress check while stuck.</para>
/// </remarks>
public sealed class PositionManager
{
    /// <summary>The interpolation manager.</summary>
    public InterpolationManager Interpolation { get; } = new();

    /// <summary>The constraint manager, or null before the first <see cref="ConstrainTo"/>.</summary>
    public ConstraintManager? Constraint { get; private set; }

    /// <summary>The sticky manager, or null before the first <see cref="StickTo"/>.</summary>
    public StickyManager? Sticky { get; private set; }

    /// <summary>Retail <c>IsInterpolating</c> (0x005553B0; TODO(avatars): 11.6096).</summary>
    public bool IsInterpolating => Interpolation.IsInterpolating;

    /// <summary>Retail <c>IsFullyConstrained</c> (0x005552C0; TODO(avatars): 11.6096): false without a constraint manager.</summary>
    public bool IsFullyConstrained => Constraint?.IsFullyConstrained ?? false;

    /// <summary>
    /// Retail <c>CPhysicsObj::GetAutonomyBlipDistance</c> (0x0050EB70 / 0x0050F640): beyond this distance a new
    /// position blips instead of interpolating. Outdoors 100 m; in an EnvCell 25 m for the player and 20 m for others.
    /// </summary>
    public static float GetAutonomyBlipDistance(uint cellId, bool isThePlayer) =>
        !IsIndoors(cellId) ? 100f : isThePlayer ? 25f : 20f;

    /// <summary>
    /// Retail <c>CPhysicsObj::GetStartConstraintDistance</c> (0x0050EBC0 / 0x0050F690): outdoors 10 m, in an EnvCell
    /// 5 m, for the player and for others alike. (ACE has them the other way round.)
    /// </summary>
    public static float GetStartConstraintDistance(uint cellId) => IsIndoors(cellId) ? 5f : 10f;

    /// <summary>
    /// Retail <c>CPhysicsObj::GetMaxConstraintDistance</c> (0x0050EC10 / 0x0050F6E0): outdoors 50 m, in an EnvCell 20 m.
    /// </summary>
    public static float GetMaxConstraintDistance(uint cellId) => IsIndoors(cellId) ? 20f : 50f;

    /// <summary>
    /// Retail <c>adjust_offset</c> (0x00555190 / 0x00555DB0): interpolation (which may overwrite the offset), the sticky
    /// follow (which replaces it while stuck), then the constraint (which scales what is left).
    /// </summary>
    public void AdjustOffset(ref AcFrame offset, double quantum, in PositionContext ctx)
    {
        Interpolation.AdjustOffset(ref offset, quantum, ctx);
        Sticky?.AdjustOffset(ref offset, quantum, ctx);
        Constraint?.AdjustOffset(ref offset, quantum, ctx);
    }

    /// <summary>
    /// Retail <c>UseTime</c> (0x00555160 / 0x00555D80): the interpolation manager's blip check, then the sticky
    /// manager's timeout (<see cref="StickyManager.UseTime"/>; the constraint's is empty). Report the placement with
    /// <see cref="InterpolationManager.CompleteBlip"/>.
    /// </summary>
    /// <remarks><paramref name="ctx"/> is not read: retail's blip check looks at the queue and the failure count only.</remarks>
    public InterpolationBlip? UseTime(in PositionContext ctx)
    {
        var blip = Interpolation.UseTime();
        Sticky?.UseTime();
        return blip;
    }

    /// <summary>
    /// Retail <c>StickTo</c> (0x00555230 / 0x00555E50): makes the sticky manager for <paramref name="host"/> on first use
    /// and sticks to <paramref name="objectId"/> (<see cref="StickyManager.StickTo"/>).
    /// </summary>
    public void StickTo(uint objectId, float radius, float height, IMoveToHost host)
    {
        Sticky ??= new StickyManager(host);
        Sticky.StickTo(objectId, radius, height);
    }

    /// <summary>
    /// Retail <c>UnStick</c> (0x005551E0 / 0x00555E00, which the 11.6096 table names <c>HandleExitWorld</c>): ends a stick
    /// (<see cref="StickyManager.Unstick"/>).
    /// </summary>
    public void UnStick() => Sticky?.Unstick();

    /// <summary>Retail <c>GetStickyObjectID</c> (0x00555270; TODO(avatars): 11.6096): the object stuck to, or 0.</summary>
    public uint GetStickyObjectId() => Sticky?.TargetId ?? 0;

    /// <summary>Retail <c>HandleUpdateTarget</c> (0x005553D0 / 0x00555FF0): a target report for the sticky manager.</summary>
    public void HandleUpdateTarget(in TargetInfo info) => Sticky?.HandleUpdateTarget(info);

    /// <summary>Retail <c>InterpolateTo</c> (0x005551F0 / 0x00555E10).</summary>
    public InterpolateToResult InterpolateTo(in CellFrame position, bool keepHeading, in PositionContext ctx) =>
        Interpolation.InterpolateTo(position, keepHeading, ctx);

    /// <summary>Retail <c>StopInterpolating</c> (0x00555220 / 0x00555E40).</summary>
    public void StopInterpolating() => Interpolation.StopInterpolating();

    /// <summary>
    /// Retail <c>ConstrainTo</c> (0x00555280; TODO(avatars): 11.6096): makes the constraint manager on first use and
    /// constrains to <paramref name="position"/>, measuring from <paramref name="ctx"/>'s position.
    /// </summary>
    public void ConstrainTo(in CellFrame position, float startDistance, float maxDistance, in PositionContext ctx)
    {
        Constraint ??= new ConstraintManager();
        Constraint.ConstrainTo(position, startDistance, maxDistance, ctx.Position);
    }

    /// <summary>Retail <c>UnConstrain</c> (0x005552B0 / 0x00555ED0).</summary>
    public void UnConstrain() => Constraint?.UnConstrain();

    /// <summary>Retail's indoor test in these functions: a cell low word of 0x100 or more (an EnvCell, or the landblock).</summary>
    private static bool IsIndoors(uint cellId) => (cellId & 0xFFFF) >= 0x100;
}
