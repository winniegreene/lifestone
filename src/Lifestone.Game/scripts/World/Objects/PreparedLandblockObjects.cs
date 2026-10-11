using System.Diagnostics;
using Godot;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Objects;

/// <summary>One mesh an object part can draw, prepared on a worker: Godot arrays and resolved surfaces, or nothing.</summary>
public sealed class PreparedObjectMesh
{
    internal PreparedObjectMesh(ObjectMeshKey key, PreparedMesh? mesh, PreparedSurface[] surfaces, float radius, string? problem)
    {
        Key = key;
        Mesh = mesh;
        Surfaces = surfaces;
        Radius = radius;
        Problem = problem;
    }

    public ObjectMeshKey Key { get; }

    /// <summary>The converted geometry, or null when retail draws nothing for it (see <see cref="Problem"/>).</summary>
    public PreparedMesh? Mesh { get; }

    /// <summary>One prepared surface per sub-mesh of <see cref="Mesh"/>.</summary>
    public PreparedSurface[] Surfaces { get; }

    /// <summary>Distance from the GfxObj's local origin to the farthest corner of its bounds (unscaled), for culling bounds.</summary>
    public float Radius { get; }

    /// <summary>Why nothing is drawn: missing GfxObj, or no sub-mesh survives the draw rules (e.g. untextured building LOD).</summary>
    public string? Problem { get; }
}

/// <summary>
/// A landblock's objects ready to attach: the engine-agnostic <see cref="LandblockObjectSet"/>, every mesh its parts can
/// draw (with surfaces and decoded textures), each part's instance transform in Godot axes and each batch's culling
/// bounds. Built on a worker by <see cref="Prepare"/>; disposing frees decoded images that were never uploaded.
/// </summary>
/// <remarks>
/// <b>Reuse.</b> Disposing only frees the images; everything else stays valid. A landblock that leaves the 3×3 but stays
/// in the prefetch ring keeps this object (with its LOD state and instance buffers) and attaches from it again if it
/// comes back: only textures evicted in the meantime are decoded again (<see cref="LandblockAttachTextures"/>).
/// </remarks>
public sealed class PreparedLandblockObjects : IDisposable
{
    /// <summary>Floats per instance in a Transform3D MultiMesh buffer (3×4: basis rows with the origin column).</summary>
    public const int TransformStride = 12;

    private int _imagesFreed;

    private PreparedLandblockObjects(LandblockObjectSet set, PreparedObjectMesh[] meshes, float[] baseTransforms, Aabb[] batchBounds, double prepareMs, LivingStaticPlan? living)
    {
        Set = set;
        Meshes = meshes;
        BaseTransforms = baseTransforms;
        BatchBounds = batchBounds;
        PrepareMs = prepareMs;
        Living = living;
        InstanceBuffers = new float[]?[meshes.Length];
    }

    public LandblockCoord Coord => Set.Coord;

    public LandblockObjectSet Set { get; }

    /// <summary>
    /// The landblock's living statics (default scripts and animations, docs/objects.md §1.7), or null when it has none or
    /// they are off. Created when the objects attach, destroyed with them.
    /// </summary>
    public LivingStaticPlan? Living { get; }

    /// <summary>Parallel to <see cref="LandblockObjectSet.Meshes"/>.</summary>
    public PreparedObjectMesh[] Meshes { get; }

    /// <summary>
    /// Per part, <see cref="TransformStride"/> floats: its frame (origin, rotation, <c>gfxobj_scale</c> less the shape baked
    /// into its meshes, <see cref="ObjectPartPlacement.InstanceScale"/>) relative to the landblock root, as
    /// <see cref="WriteTransform"/> lays it out. Used while the part's level is drawn with its own frame.
    /// </summary>
    public float[] BaseTransforms { get; }

    /// <summary>Per mesh, bounds (landblock-local, Godot axes) containing every part that can draw it, in any orientation.</summary>
    public Aabb[] BatchBounds { get; }

    /// <summary>Worker time spent preparing.</summary>
    public double PrepareMs { get; }

    /// <summary>
    /// Per mesh, its batch's instance buffer (<see cref="TransformStride"/> floats per instance of capacity), allocated by
    /// the first attach and kept for the next one. Main thread.
    /// </summary>
    internal float[]?[] InstanceBuffers { get; }

    /// <summary>The landblock's LOD state, created by the first attach and kept for the next one. Main thread.</summary>
    internal LandblockObjectLod? Lod { get; set; }

    /// <summary>
    /// Worker thread: builds the landblock's object set (LandBlockInfo statics and buildings, generated scenery) and
    /// prepares everything its parts can draw. Returns null when the landblock has no objects.
    /// </summary>
    public static PreparedLandblockObjects? Prepare(
        GameServices services,
        ObjectModelLibrary library,
        PreparedGeometryCache geometry,
        LandblockCoord coord,
        WorldObjectKinds kinds,
        CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew();
        var dats = services.Dats ?? throw new InvalidOperationException("dats not open");
        var statics = kinds.HasFlag(WorldObjectKinds.Statics) || kinds.HasFlag(WorldObjectKinds.Buildings) ? LandblockStatics.Read(dats, coord) : null;
        cancellation.ThrowIfCancellationRequested();
        var scenery = kinds.HasFlag(WorldObjectKinds.Scenery) ? services.Scenery.Generate(coord).Placements : null;
        cancellation.ThrowIfCancellationRequested();
        var set = LandblockObjectSet.Build(coord, statics, scenery, library, kinds);
        if (set.Objects.Count == 0)
        {
            return null;
        }

        // LOD levels of one part share their surfaces (LodChain): resolve and decode each surface once per landblock.
        var surfaces = new Dictionary<SurfaceRequest, PreparedSurface>();
        var meshes = new PreparedObjectMesh[set.Meshes.Count];
        try
        {
            for (var i = 0; i < meshes.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                meshes[i] = PrepareMesh(services, geometry, set.Meshes[i], surfaces);
            }
        }
        catch
        {
            foreach (var mesh in meshes)
            {
                DisposeSurfaces(mesh);
            }

            throw;
        }

        var transforms = new float[set.Parts.Count * TransformStride];
        for (var p = 0; p < set.Parts.Count; p++)
        {
            var part = set.Parts[p];
            WriteTransform(transforms, p, AcToGodot.Transform(part.Origin, part.Rotation, part.InstanceScale));
        }

        // Living statics: which objects run a default script or animation (their records decoded here, on the worker).
        var living = services.Options.StaticEffects ? LivingStaticPlan.Create(set, library, ObjectsShared.Sources(services)) : null;
        LivingStaticsSystem.WarmParticleModels(living);
        return new PreparedLandblockObjects(set, meshes, transforms, ComputeBatchBounds(set, meshes, living), timer.Elapsed.TotalMilliseconds, living);
    }

    /// <summary>An empty prepared landblock (warm-up of the attach path).</summary>
    internal static PreparedLandblockObjects Empty(LandblockObjectSet set) => new(set, [], [], [], 0, null);

    /// <summary>Writes a transform into instance <paramref name="index"/> of a Transform3D MultiMesh buffer.</summary>
    /// <remarks>Godot's layout (<c>multimesh_instance_set_transform</c>): basis row 0 then origin.x, row 1 then origin.y, row 2 then origin.z.</remarks>
    public static void WriteTransform(float[] buffer, int index, in Transform3D transform)
    {
        var o = index * TransformStride;
        var b = transform.Basis;
        buffer[o] = b.Row0.X;
        buffer[o + 1] = b.Row0.Y;
        buffer[o + 2] = b.Row0.Z;
        buffer[o + 3] = transform.Origin.X;
        buffer[o + 4] = b.Row1.X;
        buffer[o + 5] = b.Row1.Y;
        buffer[o + 6] = b.Row1.Z;
        buffer[o + 7] = transform.Origin.Y;
        buffer[o + 8] = b.Row2.X;
        buffer[o + 9] = b.Row2.Y;
        buffer[o + 10] = b.Row2.Z;
        buffer[o + 11] = transform.Origin.Z;
    }

    /// <summary>
    /// Frees decoded texture images that were not uploaded (idempotent, any thread). The rest stays valid for a later
    /// attach (see the remarks).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _imagesFreed, 1) != 0)
        {
            return;
        }

        foreach (var mesh in Meshes)
        {
            DisposeSurfaces(mesh);
        }
    }

    private static PreparedObjectMesh PrepareMesh(
        GameServices services,
        PreparedGeometryCache geometryCache,
        ObjectMeshKey key,
        Dictionary<SurfaceRequest, PreparedSurface> shared)
    {
        var geometry = geometryCache.GetOrAdd(key, k => BuildGeometry(services, k));
        if (geometry.Mesh is not { } mesh)
        {
            return new PreparedObjectMesh(key, null, [], geometry.Radius, geometry.Problem);
        }

        var surfaces = new PreparedSurface[mesh.SubMeshes.Count];
        for (var i = 0; i < surfaces.Length; i++)
        {
            var request = SurfaceRequest.For(mesh.SubMeshes[i].Source);
            if (key.Context == MeshDrawContext.Building)
            {
                // Render-fidelity fix: DrawBuilding makes the building detail surface current (docs/render-contract.md §2.4).
                request = request with { Detail = Lifestone.Client.Rendering.SurfaceDetail.Building };
            }
            if (!shared.TryGetValue(request, out var surface))
            {
                surface = services.Render.PrepareSurface(request);
                shared.Add(request, surface);
            }

            surfaces[i] = surface;
        }

        return new PreparedObjectMesh(key, mesh, surfaces, geometry.Radius, null);
    }

    /// <summary>
    /// Worker: a GfxObj's drawn sub-meshes in Godot arrays, with the key's part shape baked in (<see cref="PartShapes"/>;
    /// cached across landblocks).
    /// </summary>
    private static PreparedGeometry BuildGeometry(GameServices services, ObjectMeshKey key)
    {
        if (services.GfxObjMeshes.Get(key.GfxObjId) is not { } gfxObj)
        {
            // A missing level GfxObj leaves a null CPhysicsPart::gfxobj[] entry: nothing drawn at that level.
            return new PreparedGeometry(null, 0f, $"GfxObj 0x{key.GfxObjId:X8} does not exist");
        }

        var shaped = PartShapes.Apply(gfxObj.Mesh, key.Shape);
        var bounds = shaped.Bounds;
        var radius = MathF.Sqrt(Math.Max(Corner2(bounds.Min, bounds.Max), Corner2(bounds.Max, bounds.Min)));
        if (gfxObj.Mesh.SubMeshes.Count == 0)
        {
            return new PreparedGeometry(null, radius, "no drawing polygons");
        }

        // Building LODs skip untextured sub-meshes (D3DPolyRender::DrawMesh with ObjBuildingOrBuildingPart).
        var drawn = SurfaceDrawRules.DrawnSubMeshes(services.Dats!, gfxObj.Mesh, key.Context);
        if (!drawn.Any(d => d))
        {
            return new PreparedGeometry(null, radius, $"no sub-mesh drawn as {key.Context}");
        }

        return new PreparedGeometry(MeshFactory.Prepare(shaped, s => drawn[s]), radius, null);
    }

    /// <summary>Largest squared distance from the origin to a corner of the box (per axis the larger magnitude).</summary>
    private static float Corner2(AcVector3 a, AcVector3 b)
    {
        var x = Math.Max(Math.Abs(a.X), Math.Abs(b.X));
        var y = Math.Max(Math.Abs(a.Y), Math.Abs(b.Y));
        var z = Math.Max(Math.Abs(a.Z), Math.Abs(b.Z));
        return (x * x) + (y * y) + (z * z);
    }

    /// <summary>
    /// Per mesh: the union of spheres (part origin, mesh radius × largest instance scale) of every part that can draw it
    /// (the radius is the baked mesh's, so it includes the part's shape). A part of an animated object can be anywhere
    /// within its object's pose reach (<see cref="LivingStaticPlan.Reach"/>): its sphere is centred on the object's origin
    /// and grows by that reach.
    /// </summary>
    private static Aabb[] ComputeBatchBounds(LandblockObjectSet set, PreparedObjectMesh[] meshes, LivingStaticPlan? living)
    {
        var min = new AcVector3[meshes.Length];
        var max = new AcVector3[meshes.Length];
        var any = new bool[meshes.Length];
        for (var p = 0; p < set.Parts.Count; p++)
        {
            var part = set.Parts[p];
            var instanceScale = part.InstanceScale;
            var scale = Math.Max(Math.Abs(instanceScale.X), Math.Max(Math.Abs(instanceScale.Y), Math.Abs(instanceScale.Z)));
            var center = part.Origin;
            var extra = 0f;
            if (living?.ReachOf(part.ObjectIndex) is { } reach)
            {
                center = set.Objects[part.ObjectIndex].Origin;
                extra = reach;
            }

            foreach (var mesh in set.LevelMeshes(p))
            {
                if (mesh < 0)
                {
                    continue;
                }

                var r = new AcVector3((meshes[mesh].Radius * scale) + extra);
                var lo = center - r;
                var hi = center + r;
                min[mesh] = any[mesh] ? AcVector3.Min(min[mesh], lo) : lo;
                max[mesh] = any[mesh] ? AcVector3.Max(max[mesh], hi) : hi;
                any[mesh] = true;
            }
        }

        var result = new Aabb[meshes.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = any[i] ? AcToGodot.Bounds(min[i], max[i]) : default;
        }

        return result;
    }

    private static void DisposeSurfaces(PreparedObjectMesh? mesh)
    {
        if (mesh is null)
        {
            return;
        }

        // Surfaces are shared between meshes; PreparedSurface.Dispose is idempotent.
        foreach (var surface in mesh.Surfaces)
        {
            surface.Dispose();
        }
    }
}
