using System.Numerics;

namespace Lifestone.Data.Animation;

/// <summary>
/// Places animated parts in the world, re-expressing retail <c>CPartArray::UpdateParts</c> (acclient 11.4186
/// PORTAL/cphysobj/CPartArray.c): <c>part[i].pos.frame = Frame::combine(objectFrame, animFrame[i], scale)</c>, i.e.
/// <c>origin = obj.origin + R(obj.q)·(anim.origin ∘ scale)</c>, <c>q = obj.q ⊗ anim.q</c>. Flat, not hierarchical.
/// Only <c>min(numParts, animParts)</c> parts are updated; the others keep their previous frame.
/// Mesh scaling (<c>DefaultScale[i] · objScale</c>) is the model layer's job (docs/research/rendering.md §6).
/// </summary>
public static class PartPose
{
    /// <summary>Writes the world frames of the posed parts into <paramref name="partFrames"/> (length = Setup part count).</summary>
    public static void Update(AcFrame objectFrame, IReadOnlyList<AcFrame>? pose, Vector3 objectScale, Span<AcFrame> partFrames)
    {
        if (pose is null)
        {
            return;
        }

        var n = Math.Min(partFrames.Length, pose.Count);
        for (var i = 0; i < n; i++)
        {
            partFrames[i] = AcFrame.Combine(objectFrame, pose[i], objectScale);
        }
    }

    /// <summary>Convenience overload returning a new array of <paramref name="partCount"/> frames (unposed parts = object frame).</summary>
    public static AcFrame[] Compose(AcFrame objectFrame, IReadOnlyList<AcFrame>? pose, float objectScale, int partCount)
    {
        var frames = new AcFrame[partCount];
        Array.Fill(frames, objectFrame);
        Update(objectFrame, pose, new Vector3(objectScale), frames);
        return frames;
    }
}
