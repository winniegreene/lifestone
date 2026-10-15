using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// One part of a <see cref="PhysicsSetup"/> as collision sees it: the level-0 GfxObj's shape (<c>gfxobj[0]</c>), the
/// part's frame in the object at rest, its default scale and whether its first degrade level faces the camera.
/// </summary>
/// <param name="Shape">The level-0 GfxObj's bounds, spheres and physics shape.</param>
/// <param name="RestFrame">The part's frame in the object, from the rest placement (Resting, else Default).</param>
/// <param name="DefaultScale">The Setup's default scale of the part.</param>
/// <param name="Always2D">Retail <c>CPhysicsPart::Always2D</c>.</param>
public sealed record PhysicsSetupPart(GfxObjShape Shape, AcFrame RestFrame, Vector3 DefaultScale, bool Always2D);

/// <summary>
/// What a moving <see cref="PhysicsObj"/> takes from its Setup (retail <c>CPartArray</c> over a <c>CSetup</c>): the
/// collision spheres and cylinder-spheres, the step heights, the sorting sphere, the free-heading flag and the parts with
/// a physics BSP. Unscaled: the object applies its own scale where retail does.
/// </summary>
/// <remarks>Immutable; shared by every object made from the Setup.</remarks>
public sealed class PhysicsSetup
{
    /// <summary>
    /// Retail <c>CPartArray::GetStepUpHeight</c> and <c>GetStepDownHeight</c>'s answer without a Setup (the float
    /// 0.0099999998; ACE <c>DefaultStepHeight</c>).
    /// </summary>
    public const float NoSetupStepHeight = 0.01f;

    private readonly PhysSphere[] _spheres;
    private readonly PhysCylSphere[] _cylSpheres;
    private readonly PhysicsSetupPart[] _parts;

    private PhysicsSetup(
        uint id,
        PhysSphere[] spheres,
        PhysCylSphere[] cylSpheres,
        float height,
        float radius,
        float stepUpHeight,
        float stepDownHeight,
        PhysSphere sortingSphere,
        bool allowsFreeHeading,
        PhysicsSetupPart[] parts,
        uint defaultMotionTableId)
    {
        Id = id;
        _spheres = spheres;
        _cylSpheres = cylSpheres;
        Height = height;
        Radius = radius;
        StepUpHeight = stepUpHeight;
        StepDownHeight = stepDownHeight;
        SortingSphere = sortingSphere;
        AllowsFreeHeading = allowsFreeHeading;
        _parts = parts;
        DefaultMotionTableId = defaultMotionTableId;
        foreach (var part in parts)
        {
            HasPhysicsBsp |= part.Shape.Physics is not null;
        }
    }

    /// <summary>The Setup id (0 for a Setup built in code).</summary>
    public uint Id { get; }

    /// <summary>The collision spheres in the object's frame (retail <c>CSetup::sphere</c>; a transition takes the first two).</summary>
    public IReadOnlyList<PhysSphere> Spheres => _spheres;

    /// <summary>The collision cylinder-spheres in the object's frame (retail <c>CSetup::cylsphere</c>).</summary>
    public IReadOnlyList<PhysCylSphere> CylSpheres => _cylSpheres;

    /// <summary>Retail <c>CSetup::height</c>.</summary>
    public float Height { get; }

    /// <summary>Retail <c>CSetup::radius</c>.</summary>
    public float Radius { get; }

    /// <summary>Retail <c>CSetup::step_up_height</c>.</summary>
    public float StepUpHeight { get; }

    /// <summary>Retail <c>CSetup::step_down_height</c>.</summary>
    public float StepDownHeight { get; }

    /// <summary>Retail <c>CSetup::sorting_sphere</c>: the cell search of an object without cylinder-spheres.</summary>
    public PhysSphere SortingSphere { get; }

    /// <summary>Retail <c>CSetup::allow_free_heading</c>: the object's transitions carry FreeRotate.</summary>
    public bool AllowsFreeHeading { get; }

    /// <summary>The parts, at rest.</summary>
    public IReadOnlyList<PhysicsSetupPart> Parts => _parts;

    /// <summary>True when some part's level-0 GfxObj has a physics BSP (retail <c>CPhysicsObj::CacheHasPhysicsBSP</c>).</summary>
    public bool HasPhysicsBsp { get; }

    /// <summary>The Setup's motion table (retail <c>CSetup::default_mtable_id</c>), or 0.</summary>
    public uint DefaultMotionTableId { get; }

    /// <summary>The spheres as a span (the transition's <c>init_sphere</c>).</summary>
    internal ReadOnlySpan<PhysSphere> SphereSpan => _spheres;

    /// <summary>The cylinder-spheres as a span.</summary>
    internal ReadOnlySpan<PhysCylSphere> CylSphereSpan => _cylSpheres;

    /// <summary>The parts as a span.</summary>
    internal ReadOnlySpan<PhysicsSetupPart> PartSpan => _parts;

    /// <inheritdoc/>
    public override string ToString() => $"PhysicsSetup 0x{Id:X8}";

    /// <summary>
    /// A Setup built in code (tests, and objects whose shape is known without the dats).
    /// </summary>
    public static PhysicsSetup FromParts(
        IReadOnlyList<PhysSphere> spheres,
        IReadOnlyList<PhysCylSphere>? cylSpheres = null,
        float stepUpHeight = NoSetupStepHeight,
        float stepDownHeight = NoSetupStepHeight,
        float height = 0f,
        float radius = 0f,
        PhysSphere? sortingSphere = null,
        bool allowsFreeHeading = false,
        IReadOnlyList<PhysicsSetupPart>? parts = null,
        uint id = 0,
        uint defaultMotionTableId = 0)
    {
        ArgumentNullException.ThrowIfNull(spheres);
        return new PhysicsSetup(
            id,
            spheres.ToArray(),
            cylSpheres?.ToArray() ?? [],
            height,
            radius,
            stepUpHeight,
            stepDownHeight,
            sortingSphere ?? default,
            allowsFreeHeading,
            parts?.ToArray() ?? [],
            defaultMotionTableId);
    }

    /// <summary>
    /// The Setup <paramref name="setupId"/> from the dats, as <c>CPartArray::CreateSetup</c> makes a part array of it:
    /// each part's level-0 GfxObj at the rest placement (<c>SetPlacementFrame(0x65)</c>: Resting, else Default).
    /// </summary>
    /// <returns>Null when the Setup does not exist or one of its parts does not load (retail <c>InitParts</c> then
    /// fails the whole object).</returns>
    /// <exception cref="InvalidDataException">A part's GfxObj is malformed.</exception>
    public static PhysicsSetup? Load(DatSet dats, uint setupId, PhysicsGeometryLibrary? geometry = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (!dats.TryGetPortal<Setup>(setupId, out var setup))
        {
            return null;
        }

        geometry ??= new PhysicsGeometryLibrary(dats);
        var assembly = ModelAssembly.FromSetup(setupId, setup);
        var placement = assembly.ResolvePlacement(PlacementIds.Resting);
        var parts = new PhysicsSetupPart[assembly.Parts.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var chain = LodChain.Load(dats, assembly.Parts[i].GfxObjId);
            var shape = chain.IsLoadable ? geometry.GetGfxObjShape(chain.NearestGfxObjId) : null;
            if (shape is null)
            {
                return null;
            }

            var frame = placement is not null && i < placement.Frames.Count ? placement.Frames[i] : PartFrame.Identity;
            parts[i] = new PhysicsSetupPart(shape, new AcFrame(frame.Origin, frame.Orientation), assembly.Parts[i].DefaultScale, chain.Table?.Always2D ?? false);
        }

        var sorting = assembly.SortingSphere;
        return new PhysicsSetup(
            setupId,
            assembly.Spheres.Select(s => new PhysSphere(s.Center, s.Radius)).ToArray(),
            assembly.CylSpheres.Select(c => new PhysCylSphere(c.LowPoint, c.Height, c.Radius)).ToArray(),
            assembly.Height,
            assembly.Radius,
            assembly.StepUpHeight,
            assembly.StepDownHeight,
            new PhysSphere(sorting.Center, sorting.Radius),
            (assembly.Flags & ModelAssemblyFlags.AllowFreeHeading) != 0,
            parts,
            assembly.DefaultMotionTableId);
    }
}
