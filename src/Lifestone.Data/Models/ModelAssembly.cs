using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Models;

/// <summary>Setup flags (retail <c>CSetup::UnPack</c>; ACE/DatReaderWriter <c>SetupFlags</c>).</summary>
[Flags]
public enum ModelAssemblyFlags : uint
{
    None = 0,

    /// <summary>A <c>ParentIndex</c> array follows the part ids.</summary>
    HasParent = 0x1,

    /// <summary>A <c>DefaultScale</c> array follows.</summary>
    HasDefaultScale = 0x2,

    /// <summary><c>CSetup::allow_free_heading</c>.</summary>
    AllowFreeHeading = 0x4,

    /// <summary><c>CSetup::has_physics_bsp</c>: collide against the parts' physics BSPs.</summary>
    HasPhysicsBsp = 0x8,
}

/// <summary>Well-known placement-frame ids (retail <c>Placement</c> enum; DatReaderWriter <c>Enums.Placement</c>).</summary>
public static class PlacementIds
{
    /// <summary>Default placement; present in every end-of-retail Setup.</summary>
    public const uint Default = 0x00;

    /// <summary>Resting placement; present in 2,629 of 5,935 Setups.</summary>
    public const uint Resting = 0x65;
}

/// <summary>One part of a <see cref="ModelAssembly"/>.</summary>
/// <param name="Index">Index of the part in the Setup.</param>
/// <param name="GfxObjId">
/// Part model id. Always a GfxObj (<c>0x01xxxxxx</c>) in the end-of-retail dats: retail
/// <c>CPhysicsPart::makePhysicsPart</c> only loads GfxObjs (no Setup nests another Setup), so any other id makes
/// the part, and the object, fail to load. Many Setups use the invisible anchor GfxObj 0x010001EC as a part; it is
/// hidden by its degrade table, not by an id check (see <see cref="LodChain"/>).
/// </param>
/// <param name="ParentIndex">
/// <c>CSetup::parent_index</c>, or -1 (stored 0xFFFFFFFF) for none, or when the Setup has no parent array. Retail
/// does <b>not</b> use it to pose parts: every part frame is relative to the object (<see cref="ModelAssembly.ComputePose"/>).
/// </param>
/// <param name="DefaultScale">
/// <c>CSetup::default_scale[i]</c>, (1, 1, 1) without a scale array; copied into the part's <c>gfxobj_scale</c> by
/// <c>CPartArray::InitParts</c> and multiplied by the object scale in <c>CPartArray::SetScaleInternal</c>.
/// </param>
public sealed record ModelPart(int Index, uint GfxObjId, int ParentIndex, Vector3 DefaultScale)
{
    public bool IsGfxObj => GfxObjId >> 24 == 0x01;
}

/// <summary>A named pose: one frame per part (retail <c>PlacementType</c> / <c>AnimFrame</c>).</summary>
public sealed record PlacementFrame(uint PlacementId, IReadOnlyList<PartFrame> Frames, int HookCount);

/// <summary>A holding location or connection point: a frame relative to one part.</summary>
public readonly record struct LocationFrame(int PartIndex, PartFrame Frame);

/// <summary>
/// A light carried by a Setup (retail <c>LIGHTINFO</c>, 48 bytes on disk:
/// <c>i32 type, Frame offset, u32 color, f32 intensity, f32 falloff, f32 cone_angle</c>).
/// </summary>
/// <param name="Type">
/// <c>LIGHTINFO::type</c>. DatReaderWriter reads it as a dictionary key, which only works because no end-of-retail
/// Setup has more than one light (608 Setups have exactly one, all of type 0).
/// </param>
/// <param name="Offset">
/// <c>LIGHTINFO::offset</c> (DatReaderWriter calls it <c>ViewSpaceLocation</c>): the light frame relative to the
/// <b>object</b> frame. <c>CPartArray::SetFrame</c> → <c>LIGHTLIST::set_frame</c> stores the object frame as each
/// light's <c>global_offset</c>; no part frame is involved.
/// </param>
/// <param name="ColorArgb">Packed colour as stored: bytes B, G, R, A (DatReaderWriter <c>ColorARGB</c>).</param>
/// <param name="Intensity"><c>LIGHTINFO::intensity</c> (100, 75, 50 or 20 on end-of-retail data).</param>
/// <param name="Falloff">
/// <c>LIGHTINFO::falloff</c> (1 up to FLT_MAX on end-of-retail data). Retail's CPU vertex lighting
/// (<c>calc_point_light</c> in D3DPolyRender) lights vertices within <c>falloff × static_light_factor</c> (1.3) metres.
/// </param>
/// <param name="ConeAngle">
/// Raw value. Every end-of-retail light stores the MSVC debug fill 0xCDCDCDCD here (≈ −4.3e8), i.e. the field was
/// never set by Turbine's tools; treat these lights as point lights.
/// </param>
public sealed record ModelLight(int Type, PartFrame Offset, uint ColorArgb, float Intensity, float Falloff, float ConeAngle)
{
    /// <summary>Colour as 0-1 RGB (each byte / 255); alpha is ignored (retail unpacks it into an <c>RGBColor</c>).</summary>
    public Vector3 ColorRgb => new(((ColorArgb >> 16) & 0xFF) / 255f, ((ColorArgb >> 8) & 0xFF) / 255f, (ColorArgb & 0xFF) / 255f);
}

/// <summary>
/// A Setup (<c>0x02xxxxxx</c>), or a bare GfxObj wrapped the way retail <c>CSetup::makeSimpleSetup</c> wraps it: the
/// parts of an object and how they are posed, plus the physics/selection shapes and default resource ids.
/// </summary>
/// <remarks>
/// <para><b>Posing</b> is flat, not hierarchical (retail <c>CPartArray::UpdateParts</c> → <c>Frame::combine</c>;
/// ACE <c>Physics/PartArray.cs UpdateParts</c> and <c>Animation/AFrame.cs Combine</c>; ACViewer
/// <c>Model/Setup.cs</c>): for part <c>i</c> with frame <c>f_i</c> of the current placement or animation frame,
/// <c>part.origin = obj.origin + R(obj.q)·(f_i.origin ⊙ objectScale)</c>,
/// <c>part.q = obj.q ⊗ f_i.q</c> (Hamilton product), and the part's mesh is scaled by
/// <c>DefaultScale[i] ⊙ objectScale</c> in part space. <see cref="ModelPart.ParentIndex"/> plays no part.</para>
/// <para><b>Rest pose.</b> Both part-array constructors, <c>CPartArray::CreateSetup</c> (Setup ids) and
/// <c>CPartArray::CreateMesh</c> (bare GfxObj ids), call <c>CPartArray::SetPlacementFrame(0x65)</c>, with no
/// distinction between statics, scenery and creatures: Resting if present, else Default (0); if neither exists no
/// frame is set. When <see cref="DefaultAnimationId"/> is non-zero, <c>CPartArray::InitDefaults</c>
/// also appends that animation to the part array's sequence (<c>AnimData</c> low frame 0, high frame −1 = last,
/// 30 fps); while it plays, its frames replace the placement pose (134 end-of-retail Setups have one; playback is a
/// later milestone).</para>
/// </remarks>
public sealed class ModelAssembly
{
    private ModelAssembly(
        uint id,
        bool isSimpleGfxObjSetup,
        ModelAssemblyFlags flags,
        IReadOnlyList<ModelPart> parts,
        IReadOnlyDictionary<uint, PlacementFrame> placementFrames,
        IReadOnlyDictionary<int, LocationFrame> holdingLocations,
        IReadOnlyDictionary<int, LocationFrame> connectionPoints,
        IReadOnlyList<ModelCylSphere> cylSpheres,
        IReadOnlyList<ModelSphere> spheres,
        float height,
        float radius,
        float stepUpHeight,
        float stepDownHeight,
        ModelSphere sortingSphere,
        ModelSphere selectionSphere,
        IReadOnlyList<ModelLight> lights,
        uint defaultAnimationId,
        uint defaultScriptId,
        uint defaultMotionTableId,
        uint defaultSoundTableId,
        uint defaultScriptTableId)
    {
        Id = id;
        IsSimpleGfxObjSetup = isSimpleGfxObjSetup;
        Flags = flags;
        Parts = parts;
        PlacementFrames = placementFrames;
        HoldingLocations = holdingLocations;
        ConnectionPoints = connectionPoints;
        CylSpheres = cylSpheres;
        Spheres = spheres;
        Height = height;
        Radius = radius;
        StepUpHeight = stepUpHeight;
        StepDownHeight = stepDownHeight;
        SortingSphere = sortingSphere;
        SelectionSphere = selectionSphere;
        Lights = lights;
        DefaultAnimationId = defaultAnimationId;
        DefaultScriptId = defaultScriptId;
        DefaultMotionTableId = defaultMotionTableId;
        DefaultSoundTableId = defaultSoundTableId;
        DefaultScriptTableId = defaultScriptTableId;
    }

    /// <summary>The Setup id, or the GfxObj id for a simple setup.</summary>
    public uint Id { get; }

    /// <summary>True when built from a bare GfxObj by <see cref="FromGfxObj"/> (<c>CSetup::makeSimpleSetup</c>).</summary>
    public bool IsSimpleGfxObjSetup { get; }

    public ModelAssemblyFlags Flags { get; }

    public IReadOnlyList<ModelPart> Parts { get; }

    /// <summary>Placement frames by placement id (see <see cref="PlacementIds"/>); 0 always present on EoR data.</summary>
    public IReadOnlyDictionary<uint, PlacementFrame> PlacementFrames { get; }

    /// <summary>Where wielded/held objects attach, keyed by retail <c>ParentLocation</c>.</summary>
    public IReadOnlyDictionary<int, LocationFrame> HoldingLocations { get; }

    /// <summary>Connection points, keyed by retail <c>ParentLocation</c>.</summary>
    public IReadOnlyDictionary<int, LocationFrame> ConnectionPoints { get; }

    /// <summary>Collision cylinder-spheres (object space).</summary>
    public IReadOnlyList<ModelCylSphere> CylSpheres { get; }

    /// <summary>Collision spheres (object space).</summary>
    public IReadOnlyList<ModelSphere> Spheres { get; }

    public float Height { get; }

    public float Radius { get; }

    public float StepUpHeight { get; }

    public float StepDownHeight { get; }

    /// <summary>
    /// <c>CSetup::sorting_sphere</c> (returned unscaled by <c>CPartArray::GetSortingSphere</c>). For a simple setup it is
    /// the GfxObj's physics sphere, else its drawing sphere, else zero.
    /// </summary>
    public ModelSphere SortingSphere { get; }

    /// <summary><c>CSetup::selection_sphere</c>; <c>CPartArray::GetSelectionSphere</c> scales centre and radius by the object scale.</summary>
    public ModelSphere SelectionSphere { get; }

    public IReadOnlyList<ModelLight> Lights { get; }

    /// <summary>Animation (<c>0x03</c>) started by <c>CPartArray::InitDefaults</c>, or 0.</summary>
    public uint DefaultAnimationId { get; }

    /// <summary>PhysicsScript (<c>0x33</c>) played by <c>CPhysicsObj::InitDefaults</c>, or 0.</summary>
    public uint DefaultScriptId { get; }

    /// <summary>MotionTable (<c>0x09</c>), or 0.</summary>
    public uint DefaultMotionTableId { get; }

    /// <summary>SoundTable (<c>0x20</c>), or 0.</summary>
    public uint DefaultSoundTableId { get; }

    /// <summary>PhysicsScriptTable (<c>0x34</c>), or 0.</summary>
    public uint DefaultScriptTableId { get; }

    /// <summary>
    /// Retail <c>CPartArray::SetPlacementFrame</c>: the requested placement if present, else Default (0), else null.
    /// </summary>
    public PlacementFrame? ResolvePlacement(uint placementId = PlacementIds.Resting)
    {
        if (PlacementFrames.TryGetValue(placementId, out var frame))
        {
            return frame;
        }

        return PlacementFrames.TryGetValue(PlacementIds.Default, out var fallback) ? fallback : null;
    }

    /// <summary>
    /// Part poses relative to the object frame for <paramref name="frames"/> (a placement or an animation frame), with
    /// the retail <c>Frame::combine</c> + <c>gfxobj_scale</c> rules (see the class remarks).
    /// </summary>
    /// <param name="frames">One frame per part; parts beyond its length keep <see cref="PartFrame.Identity"/>
    /// (retail <c>UpdateParts</c> only updates <c>min(num_parts, frame.num_parts)</c> parts).</param>
    /// <param name="objectScale">The object's scale (<c>CPartArray::scale</c>); (1, 1, 1) when null.</param>
    public PartPose[] ComputePose(IReadOnlyList<PartFrame> frames, Vector3? objectScale = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var scale = objectScale ?? Vector3.One;
        var poses = new PartPose[Parts.Count];
        for (var i = 0; i < Parts.Count; i++)
        {
            var frame = i < frames.Count ? frames[i] : PartFrame.Identity;
            poses[i] = new PartPose(frame.Origin * scale, frame.Orientation, Parts[i].DefaultScale * scale);
        }

        return poses;
    }

    /// <summary>
    /// The pose retail shows before any animation: <see cref="ResolvePlacement"/>(<see cref="PlacementIds.Resting"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">The Setup has neither a Resting nor a Default placement (none on EoR data;
    /// retail would leave every part at the identity frame).</exception>
    public PartPose[] ComputeRestPose(Vector3? objectScale = null)
    {
        var placement = ResolvePlacement(PlacementIds.Resting)
            ?? throw new InvalidDataException($"Setup 0x{Id:X8} has neither a Resting nor a Default placement frame.");
        return ComputePose(placement.Frames, objectScale);
    }

    /// <summary>Builds the assembly of a decoded Setup.</summary>
    public static ModelAssembly FromSetup(uint setupId, Setup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var flags = (ModelAssemblyFlags)(uint)setup.Flags;
        var count = setup.Parts.Count;
        var parts = new ModelPart[count];
        for (var i = 0; i < count; i++)
        {
            var parent = i < setup.ParentIndex.Count ? setup.ParentIndex[i] : uint.MaxValue;
            parts[i] = new ModelPart(
                i,
                setup.Parts[i].DataId,
                parent == uint.MaxValue ? -1 : checked((int)parent),
                i < setup.DefaultScale.Count ? setup.DefaultScale[i] : Vector3.One);
        }

        var placements = new Dictionary<uint, PlacementFrame>();
        foreach (var (key, animationFrame) in setup.PlacementFrames)
        {
            var frames = animationFrame.Frames.Select(ToPartFrame).ToArray();
            placements.Add((uint)key, new PlacementFrame((uint)key, frames, animationFrame.Hooks.Count));
        }

        return new ModelAssembly(
            setupId,
            isSimpleGfxObjSetup: false,
            flags,
            parts,
            placements,
            ToLocations(setup.HoldingLocations),
            ToLocations(setup.ConnectionPoints),
            setup.CylSpheres.Select(c => new ModelCylSphere(c.Origin, c.Radius, c.Height)).ToArray(),
            setup.Spheres.Select(ToSphere).ToArray(),
            setup.Height,
            setup.Radius,
            setup.StepUpHeight,
            setup.StepDownHeight,
            ToSphere(setup.SortingSphere),
            ToSphere(setup.SelectionSphere),
            setup.Lights
                .Select(l => new ModelLight(
                    l.Key,
                    ToPartFrame(l.Value.ViewSpaceLocation),
                    PackColor(l.Value.Color),
                    l.Value.Intensity,
                    l.Value.Falloff,
                    l.Value.ConeAngle))
                .ToArray(),
            setup.DefaultAnimation.DataId,
            setup.DefaultScript.DataId,
            setup.DefaultMotionTable.DataId,
            setup.DefaultSoundTable.DataId,
            setup.DefaultScriptTable.DataId);
    }

    /// <summary>
    /// Retail <c>CSetup::makeSimpleSetup</c>: one part (the GfxObj), one Default placement with the identity frame,
    /// unit scale, sorting sphere = the GfxObj's physics sphere (<c>CGfxObj</c> offset 116), else its drawing sphere
    /// (offset 144). Everything else is the
    /// <c>CSetup</c> constructor's default (no spheres, zero sizes, <c>allow_free_heading = 1</c>, no ids).
    /// </summary>
    public static ModelAssembly FromGfxObj(uint gfxObjId, GfxObjMesh gfxObj)
    {
        ArgumentNullException.ThrowIfNull(gfxObj);
        var sorting = gfxObj.PhysicsSphere ?? gfxObj.DrawingSphere ?? ModelSphere.Zero;
        return new ModelAssembly(
            gfxObjId,
            isSimpleGfxObjSetup: true,
            ModelAssemblyFlags.AllowFreeHeading,
            [new ModelPart(0, gfxObjId, -1, Vector3.One)],
            new Dictionary<uint, PlacementFrame>
            {
                [PlacementIds.Default] = new(PlacementIds.Default, [PartFrame.Identity], 0),
            },
            new Dictionary<int, LocationFrame>(),
            new Dictionary<int, LocationFrame>(),
            [],
            [],
            0f,
            0f,
            0f,
            0f,
            sorting,
            ModelSphere.Zero,
            [],
            0,
            0,
            0,
            0,
            0);
    }

    /// <summary>
    /// Loads a Setup (<c>0x02…</c>) or wraps a GfxObj (<c>0x01…</c>, as LandBlockInfo statics and Scene objects may
    /// reference either). Returns null when the record does not exist.
    /// </summary>
    /// <param name="dats">The dats.</param>
    /// <param name="id">Setup or GfxObj id.</param>
    /// <param name="meshCache">Optional cache used to obtain a GfxObj's spheres for a simple setup.</param>
    public static ModelAssembly? Load(DatSet dats, uint id, GfxObjMeshCache? meshCache = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        switch (id >> 24)
        {
            case 0x01:
                var mesh = meshCache is null ? GfxObjMeshBuilder.Load(dats, id) : meshCache.Get(id);
                return mesh is null ? null : FromGfxObj(id, mesh);
            case 0x02:
                return dats.TryGetPortal<Setup>(id, out var setup) ? FromSetup(id, setup) : null;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), $"0x{id:X8} is neither a GfxObj nor a Setup id.");
        }
    }

    /// <summary>
    /// Parts for which retail <c>CPhysicsPart::makePhysicsPart</c> fails (not a GfxObj, missing, or unloadable LOD
    /// chain). Retail creates the object only when this is empty (<c>CPartArray::InitParts</c>); on end-of-retail data
    /// only Setups 0x02001C4F and 0x02001C50 fail (part 0x01004E29 does not exist).
    /// </summary>
    public IReadOnlyList<ModelPart> FindUnloadableParts(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var failing = new List<ModelPart>();
        var cache = new Dictionary<uint, bool>();
        foreach (var part in Parts)
        {
            if (!cache.TryGetValue(part.GfxObjId, out var loadable))
            {
                loadable = part.IsGfxObj && LodChain.Load(dats, part.GfxObjId).IsLoadable;
                cache.Add(part.GfxObjId, loadable);
            }

            if (!loadable)
            {
                failing.Add(part);
            }
        }

        return failing;
    }

    // DatReaderWriter leaves reference-type fields null on objects it did not unpack (e.g. built in code).
    private static PartFrame ToPartFrame(Frame? frame) => frame is null ? PartFrame.Identity : new(frame.Origin, frame.Orientation);

    private static ModelSphere ToSphere(Sphere? sphere) => sphere is null ? ModelSphere.Zero : new(sphere.Origin, sphere.Radius);

    private static uint PackColor(ColorARGB color) =>
        ((uint)color.Alpha << 24) | ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;

    private static Dictionary<int, LocationFrame> ToLocations<TKey>(Dictionary<TKey, LocationType> source)
        where TKey : struct, Enum
    {
        var result = new Dictionary<int, LocationFrame>(source.Count);
        foreach (var (key, location) in source)
        {
            result.Add(Convert.ToInt32(key, System.Globalization.CultureInfo.InvariantCulture), new LocationFrame(location.PartId, ToPartFrame(location.Frame)));
        }

        return result;
    }
}
