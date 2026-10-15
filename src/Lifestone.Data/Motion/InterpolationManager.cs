using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>InterpolationManager</c>, re-expressed from the 11.4186 decompile (PORTAL/cpmanager/CInterpolationManager.c;
/// names and addresses cited, nothing copied): moves a server object smoothly towards the positions the server sends,
/// instead of snapping it. Each frame <see cref="AdjustOffset"/> replaces the object's animation offset with a step
/// towards the oldest queued position; <see cref="UseTime"/> blips the object when it keeps failing to get there.
/// Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para><b>The offset is overwritten, as in retail.</b> While a node is queued and the object is in contact,
/// <see cref="AdjustOffset"/> writes the whole offset frame: the animation's root motion is ignored until the node
/// completes. ACE's InterpolationManager computes the same step and then assigns it to its local parameter
/// (InterpolationManager.cs:249), so the step never reaches the caller and ACE does not interpolate at all; it is not
/// used here, not even as a test oracle. OpenAC's MIT version was read as a cross-check only.</para>
/// <para><b>Only position nodes.</b> Retail's queue also has node types 2 and 3 (a velocity is applied when one
/// completes); <c>InterpolateTo</c>, the only producer in the module, makes position nodes only, so this queue holds
/// <see cref="CellFrame"/>s.</para>
/// <para><b>Unpinned data.</b> <c>MAX_INTERPOLATED_VELOCITY</c>, <c>BIG_DISTANCE</c> and the initial value of
/// <c>fUseAdjustedSpeed_</c> are data the decompile names but the symbol table does not map. The values are ACE's
/// (7.5, 999,999, true; OpenAC agrees on the first two). TODO(avatars): read them from the 11.6096 data (a D4-style
/// read by the lead). <see cref="MaxInterpolatedVelocity"/> matters only for an object whose run rate is 0,
/// <see cref="BigDistance"/> only as "far", and for a server object both speeds are the same (docs/avatars.md §7).</para>
/// <para>Not thread-safe. Allocation-free once the queue has grown.</para>
/// </remarks>
public sealed class InterpolationManager
{
    /// <summary>The queue keeps at most this many nodes when a new one is added (<c>InterpolateTo</c>).</summary>
    public const int MaxQueuedNodes = 20;

    /// <summary>A node is reached within this distance (0.050000001).</summary>
    public const float DesiredDistance = 0.05f;

    /// <summary>A stalled node still counts as reached within this distance.</summary>
    public const float MinDistanceToReachPosition = 0.2f;

    /// <summary>Progress is checked every this many frames.</summary>
    public const int ProgressCheckFrames = 5;

    /// <summary>The progress a check needs, as a fraction of the maximum speed (0.30000001).</summary>
    public const float MinProgressRatio = 0.3f;

    /// <summary>After more than this many failed nodes, <see cref="UseTime"/> blips.</summary>
    public const int MaxNodeFailures = 3;

    /// <summary>The steps go at this multiple of the motion interpreter's maximum speed.</summary>
    public const float MaxSpeedFactor = 2f;

    /// <summary>Retail <c>MAX_INTERPOLATED_VELOCITY</c>: the step speed when the interpreter allows none (ACE's value).</summary>
    public const float MaxInterpolatedVelocity = 7.5f;

    /// <summary>Retail <c>BIG_DISTANCE</c>: the "no progress measured yet" distance (ACE's value).</summary>
    public const float BigDistance = 999999f;

    /// <summary>The failure count <c>InterpolateTo</c> sets for a position beyond the blip distance.</summary>
    public const int ArmedBlipFailures = 4;

    private const double Epsilon = 0.00019999999;

    private readonly List<CellFrame> _queue = new(MaxQueuedNodes + 1);

    /// <summary>
    /// Retail <c>InterpolationManager::fUseAdjustedSpeed_</c>: step at <see cref="PositionContext.AdjustedMaxSpeed"/>
    /// rather than <see cref="PositionContext.MaxSpeed"/>. True as in ACE; see the remarks.
    /// </summary>
    public bool UseAdjustedSpeed { get; set; } = true;

    /// <summary>Retail <c>keep_heading</c>: the steps keep the object's heading (set by <see cref="InterpolateTo"/>).</summary>
    public bool KeepHeading { get; private set; }

    /// <summary>Retail <c>frame_counter</c>: frames since the last progress check.</summary>
    public int FrameCounter { get; private set; }

    /// <summary>Retail <c>progress_quantum</c>: seconds since the last progress check.</summary>
    public float ProgressQuantum { get; private set; }

    /// <summary>Retail <c>original_distance</c>: the distance to the node at the last progress check.</summary>
    public float OriginalDistance { get; private set; } = BigDistance;

    /// <summary>Retail <c>node_fail_counter</c>.</summary>
    public int NodeFailCounter { get; private set; }

    /// <summary>Retail <c>blipto_position</c>: the last node that failed when nothing followed it.</summary>
    public CellFrame BlipToPosition { get; private set; } = new(0, AcFrame.Identity);

    /// <summary>Retail <c>PositionManager::IsInterpolating</c> (0x005553B0; TODO(avatars): 11.6096): a node is queued.</summary>
    public bool IsInterpolating => _queue.Count > 0;

    /// <summary>The queued positions, oldest first.</summary>
    public IReadOnlyList<CellFrame> Queue => _queue;

    /// <summary>
    /// Retail <c>InterpolateTo</c> (0x00555B20 / 0x00556740): a new server position.
    /// <list type="bullet">
    /// <item>Beyond <see cref="PositionManager.GetAutonomyBlipDistance"/> of the newest queued node (or the object):
    /// the position is queued without trimming, with the object's heading when <see cref="KeepHeading"/> (the previous
    /// call's value: retail does not update it here), and <see cref="NodeFailCounter"/> becomes 4, so the next
    /// <see cref="UseTime"/> blips.</item>
    /// <item>Within 0.05 m of the object: interpolation stops, and without <paramref name="keepHeading"/> the object
    /// takes the position's heading (<see cref="InterpolateToResult.SetHeading"/>).</item>
    /// <item>Otherwise: queued nodes at the end within 0.05 m of the position are dropped, the oldest nodes are dropped
    /// down to 19, <see cref="KeepHeading"/> takes <paramref name="keepHeading"/> and the position is queued (with the
    /// object's heading when keeping it).</item>
    /// </list>
    /// </summary>
    public InterpolateToResult InterpolateTo(in CellFrame position, bool keepHeading, in PositionContext ctx)
    {
        var reference = _queue.Count > 0 ? _queue[^1] : ctx.Position;
        if (PositionManager.GetAutonomyBlipDistance(ctx.Position.CellId, ctx.IsThePlayer) < PositionFrames.Distance(reference, position))
        {
            _queue.Add(KeepHeading ? WithHeadingOf(position, ctx.Position) : position);
            NodeFailCounter = ArmedBlipFailures;
            return new InterpolateToResult(InterpolateToOutcome.BlipArmed);
        }

        if (PositionFrames.Distance(ctx.Position, position) <= DesiredDistance)
        {
            float? heading = keepHeading ? null : PositionFrames.Heading(position.Orientation);
            StopInterpolating();
            return new InterpolateToResult(InterpolateToOutcome.Arrived, heading);
        }

        while (_queue.Count > 0 && PositionFrames.Distance(_queue[^1], position) < DesiredDistance)
        {
            _queue.RemoveAt(_queue.Count - 1);
        }

        while (_queue.Count >= MaxQueuedNodes)
        {
            _queue.RemoveAt(0);
        }

        KeepHeading = keepHeading;
        _queue.Add(KeepHeading ? WithHeadingOf(position, ctx.Position) : position);
        return new InterpolateToResult(InterpolateToOutcome.Queued);
    }

    /// <summary>
    /// Retail <c>adjust_offset</c> (0x00555D30 / 0x00556950): with a node queued and the object in contact, replaces
    /// <paramref name="offset"/> with a step towards the oldest node.
    /// <list type="number">
    /// <item>Within 0.05 m the node completes and the offset is left alone.</item>
    /// <item>The step speed is <see cref="MaxSpeedFactor"/> × the (adjusted) maximum speed, or
    /// <see cref="MaxInterpolatedVelocity"/> when that is below 0.0002.</item>
    /// <item>Every <see cref="ProgressCheckFrames"/> frames the progress since the last check must reach
    /// <see cref="MinProgressRatio"/> of that speed (or the object sticks to something); if not, the node completes, as
    /// reached within 0.2 m and as a failure beyond (counted in <see cref="NodeFailCounter"/>), and the offset is left
    /// alone.</item>
    /// <item>The step is the frame from the object to the node (<see cref="PositionFrames.Subtract2"/>), its origin
    /// shortened to speed × <paramref name="quantum"/>; within 0.05 m it also completes the node. With
    /// <see cref="KeepHeading"/> its rotation is set to heading 0 (no turn). It overwrites the offset.</item>
    /// </list>
    /// </summary>
    public void AdjustOffset(ref AcFrame offset, double quantum, in PositionContext ctx)
    {
        if (_queue.Count == 0 || !ctx.HasContact)
        {
            return;
        }

        var node = _queue[0];
        var currentDistance = PositionFrames.Distance(ctx.Position, node);
        if (currentDistance < DesiredDistance)
        {
            NodeCompleted(success: true, ctx);
            return;
        }

        var maxSpeed = (float)((UseAdjustedSpeed ? ctx.AdjustedMaxSpeed : ctx.MaxSpeed) * (double)MaxSpeedFactor);
        if (maxSpeed < Epsilon)
        {
            maxSpeed = MaxInterpolatedVelocity;
        }

        FrameCounter++;
        ProgressQuantum = (float)(quantum + ProgressQuantum);
        if (FrameCounter >= ProgressCheckFrames)
        {
            var progress = OriginalDistance - currentDistance;
            if (ctx.StickyObjectId == 0
                && !(progress >= Epsilon && (double)progress / ProgressQuantum / maxSpeed >= MinProgressRatio))
            {
                if (currentDistance < MinDistanceToReachPosition)
                {
                    NodeCompleted(success: true, ctx);
                }
                else
                {
                    NodeFailCounter++;
                    NodeCompleted(success: false, ctx);
                }

                return;
            }

            FrameCounter = 0;
            ProgressQuantum = 0f;
            OriginalDistance = currentDistance;
        }

        var adjustment = PositionFrames.Subtract2(node, ctx.Position);
        var maxStep = (float)(maxSpeed * quantum);
        var o = adjustment.Origin;
        var stepLength = (float)Math.Sqrt(((double)o.X * o.X) + ((double)o.Y * o.Y) + ((double)o.Z * o.Z));
        if (stepLength <= DesiredDistance)
        {
            NodeCompleted(success: true, ctx);
        }

        if (stepLength > maxStep)
        {
            adjustment = adjustment with { Origin = o * (maxStep / stepLength) };
        }

        if (KeepHeading)
        {
            adjustment = adjustment with { Orientation = PositionFrames.WithHeading(adjustment.Orientation, 0f) };
        }

        offset = adjustment;
    }

    /// <summary>
    /// Retail <c>UseTime</c> (0x00555F20 / 0x00556B40): after more than <see cref="MaxNodeFailures"/> failed nodes, or
    /// with the queue empty after a failure, the object must blip: to the newest queued node, else to
    /// <see cref="BlipToPosition"/>. The host places it (<c>SetPositionSimple</c>) and calls
    /// <see cref="CompleteBlip"/>; until it reports success, every call asks again, as retail retries a placement
    /// that failed. Null when no blip is due.
    /// </summary>
    public InterpolationBlip? UseTime()
    {
        if (NodeFailCounter > MaxNodeFailures || (_queue.Count == 0 && NodeFailCounter > 0))
        {
            return new InterpolationBlip(_queue.Count > 0 ? _queue[^1] : BlipToPosition);
        }

        return null;
    }

    /// <summary>
    /// The host's answer to a <see cref="UseTime"/> blip: when the object was placed (retail <c>SetPositionSimple</c>
    /// returned no error), interpolation stops; otherwise nothing changes and the blip is asked for again.
    /// </summary>
    public void CompleteBlip(bool placed)
    {
        if (placed)
        {
            StopInterpolating();
        }
    }

    /// <summary>
    /// Retail <c>StopInterpolating</c> (0x00555950 / 0x00556570): empties the queue and resets the progress, the
    /// failure count and the original distance. <see cref="KeepHeading"/> and <see cref="BlipToPosition"/> stay.
    /// </summary>
    public void StopInterpolating()
    {
        _queue.Clear();
        FrameCounter = 0;
        ProgressQuantum = 0f;
        NodeFailCounter = 0;
        OriginalDistance = BigDistance;
    }

    /// <summary>
    /// Retail <c>NodeCompleted</c> (0x005559A0 / 0x005565C0): drops the oldest node. With another queued, the original
    /// distance is measured to it. With none left, a success stops interpolating and a failure remembers the node as
    /// <see cref="BlipToPosition"/>.
    /// </summary>
    private void NodeCompleted(bool success, in PositionContext ctx)
    {
        FrameCounter = 0;
        ProgressQuantum = 0f;
        var completed = _queue[0];
        _queue.RemoveAt(0);
        if (_queue.Count > 0)
        {
            OriginalDistance = PositionFrames.Distance(ctx.Position, _queue[0]);
            return;
        }

        OriginalDistance = BigDistance;
        if (success)
        {
            StopInterpolating();
        }
        else
        {
            BlipToPosition = completed;
        }
    }

    private static CellFrame WithHeadingOf(in CellFrame position, in CellFrame current) =>
        position with
        {
            Frame = position.Frame with
            {
                Orientation = PositionFrames.WithHeading(position.Orientation, PositionFrames.Heading(current.Orientation)),
            },
        };
}
