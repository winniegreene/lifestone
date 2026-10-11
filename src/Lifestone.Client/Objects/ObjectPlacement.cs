using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Objects;

/// <summary>A part placed in its object's parent space (landblock-local AC coordinates for world objects).</summary>
/// <param name="Origin">Part frame origin: <c>obj.origin + R(obj)·(frame.origin × s)</c>.</param>
/// <param name="Rotation">Part frame rotation: <c>obj.q ⊗ frame.q</c> (normalised).</param>
/// <param name="Scale">The part's <c>gfxobj_scale</c>: <c>DefaultScale × s</c>.</param>
/// <param name="Shape">The non-uniform part of <see cref="Scale"/> baked into meshes (<see cref="PartShapes"/>).</param>
/// <param name="SortCenter">The level-0 GfxObj's sort centre, scaled and placed by the part frame (per-part LOD point).</param>
public readonly record struct PartPlacement(Vector3 Origin, Quaternion Rotation, Vector3 Scale, Vector3 Shape, Vector3 SortCenter);

/// <summary>
/// THE static posing rule, shared by every path that draws objects at rest or animated: the landscape objects
/// (<see cref="LandblockObjectSet"/>), the statics inside EnvCells and the spell viewer's Holtburg (docs/objects.md §1.3).
/// </summary>
/// <remarks>
/// <para>Retail (acclient 11.4186, by function name): <c>CPartArray::UpdateParts</c> places every part with
/// <c>Frame::combine(object frame, part frame, scale)</c>: flat, not hierarchical; the part frame is the placement frame
/// (<c>CPartArray::SetPlacementFrame(0x65)</c>: Resting, else Default) or the current animation frame
/// (<c>CSequence::get_curr_animframe</c>). The object scale <c>s</c> multiplies the part origins and the part scales
/// (<c>CPartArray::SetScaleInternal</c>: <c>gfxobj_scale = DefaultScale × s</c>).</para>
/// <para>The arithmetic is exactly <see cref="AcFrame.Combine(in AcFrame, in AcFrame, Vector3)"/> with a uniform scale,
/// which <c>Part_placement_is_Frame_combine</c> pins.</para>
/// </remarks>
public static class ObjectPlacement
{
    /// <summary>A part at its rest pose under an object frame and uniform scale.</summary>
    public static PartPlacement Place(ObjectModelPart part, Vector3 objectOrigin, Quaternion objectRotation, float scale)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Place(part, objectOrigin, objectRotation, scale, part.RestOrigin, part.RestOrientation);
    }

    /// <summary>
    /// A part under an object frame and uniform scale, with its frame relative to the object given explicitly (an
    /// animation frame instead of the rest pose).
    /// </summary>
    public static PartPlacement Place(ObjectModelPart part, Vector3 objectOrigin, Quaternion objectRotation, float scale, Vector3 frameOrigin, Quaternion frameRotation)
    {
        ArgumentNullException.ThrowIfNull(part);
        var origin = objectOrigin + Vector3.Transform(frameOrigin * scale, objectRotation);
        var rotation = Quaternion.Normalize(objectRotation * frameRotation);
        var partScale = part.DefaultScale * scale;
        var sortCenter = origin + Vector3.Transform(part.SortCenter * partScale, rotation);
        return new PartPlacement(origin, rotation, partScale, PartShapes.ShapeOf(part.DefaultScale), sortCenter);
    }

    /// <summary>The rest frame of a part relative to its object (for animation fallbacks and posing).</summary>
    public static AcFrame RestFrame(ObjectModelPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return new AcFrame(part.RestOrigin, part.RestOrientation);
    }
}
