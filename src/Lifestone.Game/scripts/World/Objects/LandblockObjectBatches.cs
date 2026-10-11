using System.Diagnostics;
using Godot;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// The drawn objects of one active landblock: one <see cref="MultiMeshInstance3D"/> per mesh its parts can draw (under an
/// <c>Objects</c> node of the landblock root), whose instances are the parts currently at a LOD level drawing that mesh.
/// Main thread only.
/// </summary>
/// <remarks>
/// <para><b>Attach</b> is stepwise (<see cref="AttachStep"/>): the first step evaluates the LOD for the current viewer
/// and orders the meshes (those drawn now first, nearest member first, so objects appear nearest-first). Then each step
/// runs small units until its time slice is spent: one texture upload (<see cref="LandblockAttachTextures.Ensure"/>), or
/// one shared mesh (<see cref="RenderResources.AcquireMesh"/>) with its batch node and first fill. The last step
/// re-evaluates and flushes. A step therefore costs the slice plus at most one unit.</para>
/// <para><b>No decode in an attach step.</b> Every texture the landblock needs is pinned resident, an image in hand, or
/// was decoded again on a worker before the attach began (<see cref="LandblockAttachTextures"/>, owned by this object), so
/// <see cref="RenderResources.AcquireMesh"/> always finds its textures; the pins go when the meshes hold them.</para>
/// <para><b>Per frame</b> (<see cref="Update"/>): the LOD is re-evaluated when the viewer moved, and only the batches whose
/// membership changed, or that hold camera-facing parts, rewrite their instance buffer.</para>
/// <para><b>Memory.</b> Shared meshes are held while the landblock is active and released on <see cref="Release"/>, which
/// also frees the batch nodes' MultiMeshes at once; nothing pins them afterwards (render-contract.md §4.1). The prepared
/// images are disposed when the attach ends; the rest of the prepared data (LOD state, instance buffers) stays with
/// <see cref="Prepared"/> for a later attach.</para>
/// </remarks>
public sealed class LandblockObjectBatches
{
    /// <summary>Batch creations (mesh acquisition, node, first fill) slower than this are logged (debug).</summary>
    private const double SlowBatchMs = 1.5;

    private readonly RenderResources _render;
    private readonly PreparedLandblockObjects _prepared;
    private readonly LandblockAttachTextures _textures;
    private readonly LandblockObjectLod _lod;
    private readonly Node3D _container;
    private readonly MultiMesh?[] _multiMeshes;
    private readonly MultiMeshInstance3D?[] _nodes;
    private readonly float[]?[] _buffers;
    private readonly List<string> _acquired = [];
    private readonly List<int> _dirty = [];
    private readonly List<int> _movedObjects = [];
    private LivingStaticsSystem.Registration? _living;
    private int[]? _order;
    private int _next;
    private int _nextSurface;
    private bool _released;

    /// <param name="render">The shared render resources.</param>
    /// <param name="prepared">The landblock's prepared objects (kept, not owned: the caller may attach them again later).</param>
    /// <param name="lodSettings">Retail LOD parameters.</param>
    /// <param name="landblockRoot">The landblock's root node.</param>
    /// <param name="textures">The attach's texture pins and decodes (owned from here on; released when the attach ends).</param>
    public LandblockObjectBatches(RenderResources render, PreparedLandblockObjects prepared, ObjectLodSettings lodSettings, Node3D landblockRoot, LandblockAttachTextures textures)
    {
        _render = render;
        _prepared = prepared;
        _textures = textures;
        _lod = prepared.Lod ??= new LandblockObjectLod(prepared.Set, lodSettings);
        var count = prepared.Set.Meshes.Count;
        _multiMeshes = new MultiMesh?[count];
        _nodes = new MultiMeshInstance3D?[count];
        _buffers = prepared.InstanceBuffers;
        _container = new Node3D { Name = "Objects" };
        landblockRoot.AddChild(_container);
    }

    public LandblockCoord Coord => _prepared.Coord;

    /// <summary>The prepared data this landblock draws (reusable after <see cref="Release"/>).</summary>
    public PreparedLandblockObjects Prepared => _prepared;

    public LandblockObjectSet Set => _prepared.Set;

    public LandblockObjectLod Lod => _lod;

    /// <summary>True once every mesh is acquired and the batches are filled.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>Batch nodes created.</summary>
    public int NodeCount { get; private set; }

    /// <summary>Batch nodes shown now.</summary>
    public int VisibleNodeCount { get; private set; }

    /// <summary>Instances in shown batches.</summary>
    public int VisibleInstanceCount { get; private set; }

    /// <summary>Meshes acquired from the shared cache.</summary>
    public int AcquiredMeshCount => _acquired.Count;

    /// <summary>
    /// One budgeted attach step: runs until <paramref name="sliceMs"/> is spent. Returns true when the attach is complete.
    /// </summary>
    public bool AttachStep(Func<Vector3> viewer, double sliceMs)
    {
        if (_released || IsAttached)
        {
            return true;
        }

        var start = Stopwatch.GetTimestamp();
        if (_order is null)
        {
            _lod.Evaluate(ToLocal(viewer()), force: true);
            _lod.Batches.TakeDirty(_dirty);
            _order = AttachOrder(viewer());
            return false;
        }

        // Units: each surface's texture (pinned already, or uploaded from its image now), then the mesh with its batch
        // node; the slice is checked after every unit, so one step is the slice plus at most one upload or mesh.
        while (_next < _order.Length)
        {
            var mesh = _order[_next];
            var surfaces = _prepared.Meshes[mesh].Surfaces;
            if (_prepared.Meshes[mesh].Mesh is not null && _nextSurface < surfaces.Length)
            {
                _textures.Ensure(surfaces[_nextSurface++]);
            }
            else
            {
                _next++;
                _nextSurface = 0;
                CreateBatch(mesh);
            }

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= sliceMs)
            {
                return false;
            }
        }

        // The viewer may have moved while the meshes were acquired.
        _lod.Evaluate(ToLocal(viewer()), force: true);
        Flush();
        IsAttached = true;
        _textures.Release(); // every mesh now holds its materials, and they their textures
        _prepared.Dispose();

        // Living statics (docs/objects.md §1.7): retail creates them with their objects, which are all there now.
        if (_prepared.Living is { } living && LivingStaticsSystem.Instance is { } system)
        {
            _living = system.Register(Coord, _container, living, layers: 1, cellVisible: null, moved: ApplyMoved);
        }

        return true;
    }

    /// <summary>
    /// The living statics' parts moved (default animations, <c>animate_static_object</c> → <c>UpdatePartsInternal</c>):
    /// their LOD is re-selected from the moved frames and their batches rewritten.
    /// </summary>
    private void ApplyMoved(LivingStaticSet set, System.Numerics.Vector3 viewerLocal)
    {
        if (_released || !IsAttached)
        {
            return;
        }

        _movedObjects.Clear();
        var objects = set.Set.Objects;
        foreach (var i in set.Moved)
        {
            var s = set.Statics[i];
            var obj = objects[s.ObjectIndex];
            var frames = s.PartFrames;
            for (var k = 0; k < obj.PartCount && k < frames.Length; k++)
            {
                _lod.SetPartFrame(obj.FirstPart + k, frames[k].Origin, frames[k].Orientation);
            }

            _movedObjects.Add(s.ObjectIndex);
        }

        _lod.EvaluateObjects(viewerLocal, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_movedObjects));
        Flush();
    }

    /// <summary>Per frame: re-selects LOD levels for the viewer and rewrites the batches that changed.</summary>
    /// <returns>Batches rewritten.</returns>
    public int Update(Vector3 viewer)
    {
        if (!IsAttached || _released)
        {
            return 0;
        }

        _lod.Evaluate(ToLocal(viewer));
        return Flush();
    }

    /// <summary>
    /// Frees the nodes and their MultiMeshes and releases every shared mesh and texture pin acquired (idempotent). The
    /// prepared data stays usable for a later attach.
    /// </summary>
    public void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;

        // Retail destroys a landblock's living statics with its objects (CPhysicsObj::Destroy → RemoveStaticAnimatingObject).
        LivingStaticsSystem.Instance?.Unregister(_living);
        _living = null;

        // Hide first: meshes that go idle below are unbound from their materials while the nodes live until the frame ends.
        if (GodotObject.IsInstanceValid(_container))
        {
            _container.Visible = false;
            _container.QueueFree();
        }

        // Free each MultiMesh now (its instance buffer and its reference to the shared mesh) rather than at the wrapper's
        // finaliser, so a shared mesh the cache evicts below is freed at once (render-contract.md §4.1). Unbound from its
        // node, the C# wrapper holds the last reference. (Clearing MultiMesh.Mesh first is not needed and makes Godot
        // log "multimesh->mesh.is_null()" while it recomputes the bounds of a MultiMesh with instances.)
        for (var i = 0; i < _multiMeshes.Length; i++)
        {
            if (_nodes[i] is { } node && GodotObject.IsInstanceValid(node))
            {
                node.Multimesh = null;
            }

            _multiMeshes[i]?.Dispose();
        }

        Array.Clear(_nodes);
        Array.Clear(_multiMeshes);
        foreach (var key in _acquired)
        {
            _render.ReleaseMesh(key);
        }

        _acquired.Clear();
        _textures.Release();
        _prepared.Dispose();
    }

    /// <summary>Viewer position (render space) to landblock-local AC coordinates.</summary>
    private System.Numerics.Vector3 ToLocal(Vector3 viewer)
    {
        if (!GodotObject.IsInstanceValid(_container) || _container.GetParent() is not Node3D root)
        {
            return AcToGodot.ToAc(viewer);
        }

        // Landblock roots are unrotated children of the world; outside the tree (warm-up) the local position is it.
        return AcToGodot.ToAc(viewer - (root.IsInsideTree() ? root.GlobalPosition : root.Position));
    }

    /// <summary>Meshes drawn now first (nearest member first), then the other LOD levels.</summary>
    private int[] AttachOrder(Vector3 viewer)
    {
        var local = ToLocal(viewer);
        var parts = _prepared.Set.Parts;
        var count = _prepared.Set.Meshes.Count;
        var drawnNow = new float[count];
        Array.Fill(drawnNow, float.MaxValue);
        for (var p = 0; p < parts.Count; p++)
        {
            var mesh = _lod.Batches.BatchOf(p);
            if (mesh >= 0)
            {
                drawnNow[mesh] = Math.Min(drawnNow[mesh], System.Numerics.Vector3.DistanceSquared(parts[p].Origin, local));
            }
        }

        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
        }

        Array.Sort(drawnNow, order); // stable enough: ties keep no particular order, which does not matter
        return order;
    }

    private void CreateBatch(int mesh)
    {
        var prepared = _prepared.Meshes[mesh];
        if (prepared.Mesh is null)
        {
            return;
        }

        var key = prepared.Key.CacheKey;
        ArrayMesh arrayMesh;
        var start = Stopwatch.GetTimestamp();
        try
        {
            arrayMesh = _render.AcquireMesh(key, prepared.Mesh, prepared.Surfaces);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Log.Error("objects", "mesh creation failed; its LOD level draws nothing", ("mesh", key), ("error", ex.Message));
            return;
        }

        _acquired.Add(key);
        var capacity = _prepared.Set.MeshCapacity[mesh];
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = false,
            Mesh = arrayMesh,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
            CustomAabb = _prepared.BatchBounds[mesh],
        };
        var node = new MultiMeshInstance3D
        {
            Name = $"0x{prepared.Key.GfxObjId:X8}_{prepared.Key.Context}",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = false,
        };
        _container.AddChild(node);
        _multiMeshes[mesh] = multiMesh;
        _nodes[mesh] = node;
        _buffers[mesh] ??= new float[capacity * PreparedLandblockObjects.TransformStride]; // kept for a later attach
        NodeCount++;
        Write(mesh);
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (ms > SlowBatchMs)
        {
            Log.Debug("objects", "slow batch", ("mesh", key), ("ms", ms), ("capacity", capacity), ("vertices", prepared.Mesh.VertexCount));
        }
    }

    /// <summary>
    /// Main thread, at start-up: runs the batch path once on throw-away objects (MultiMesh and node creation, a buffer
    /// upload, the LOD and camera-facing maths, the release), so the first real attach step does not pay the one-time
    /// JIT and interop set-up (measured: 15 ms for the first MultiMesh node, 4-8 ms for the first LOD evaluation).
    /// </summary>
    /// <returns>
    /// Whether <see cref="Release"/>'s way of freeing a MultiMesh (unbind it from its node, dispose the wrapper) dropped its
    /// reference to the mesh at once, as render-contract.md §4.1 needs; logged at start-up.
    /// </returns>
    public static bool WarmUp()
    {
        using var probeMesh = new ArrayMesh();
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = false,
            Mesh = probeMesh,
            InstanceCount = 1,
            VisibleInstanceCount = 0,
            CustomAabb = new Aabb(Vector3.Zero, Vector3.One),
        };
        var buffer = new float[PreparedLandblockObjects.TransformStride];
        var rotation = DegradeFrames.DrawRotation(DegradeMode.AxisAlignedZ, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.UnitX);
        PreparedLandblockObjects.WriteTransform(buffer, 0, AcToGodot.Transform(System.Numerics.Vector3.Zero, rotation, System.Numerics.Vector3.One));
        multiMesh.Buffer = buffer;
        multiMesh.VisibleInstanceCount = 1;
        var node = new MultiMeshInstance3D
        {
            Name = "warm-up",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = false,
        };
        var heldByMultiMesh = probeMesh.GetReferenceCount();
        node.Multimesh = null;
        multiMesh.Dispose();
        var released = probeMesh.GetReferenceCount() == heldByMultiMesh - 1;
        node.Free();
        _ = DegradeFrames.DrawRotation(DegradeMode.Billboard, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.UnitX);
        var batches = new InstanceBatches(1, 1);
        batches.Move(0, 0);
        _ = batches.TakeDirty();
        return released;
    }

    private int Flush()
    {
        _lod.Batches.TakeDirty(_dirty);
        foreach (var mesh in _dirty)
        {
            Write(mesh);
        }

        return _dirty.Count;
    }

    /// <summary>Rewrites one batch's instances from the current LOD membership.</summary>
    private void Write(int mesh)
    {
        if (_multiMeshes[mesh] is not { } multiMesh || _nodes[mesh] is not { } node)
        {
            return;
        }

        var members = _lod.Batches.Members(mesh);
        var wasVisible = node.Visible;
        var wasCount = wasVisible ? multiMesh.VisibleInstanceCount : 0;
        if (members.Count == 0)
        {
            multiMesh.VisibleInstanceCount = 0;
            node.Visible = false;
        }
        else
        {
            var buffer = _buffers[mesh]!;
            var baseTransforms = _prepared.BaseTransforms;
            var parts = _prepared.Set.Parts;
            for (var i = 0; i < members.Count; i++)
            {
                var p = members[i];
                if (_lod.ModeOf(p) == DegradeMode.Normal && !_lod.IsDynamic(p))
                {
                    Array.Copy(baseTransforms, p * PreparedLandblockObjects.TransformStride, buffer, i * PreparedLandblockObjects.TransformStride, PreparedLandblockObjects.TransformStride);
                }
                else
                {
                    // Camera-facing (DegradeFrames) and/or moved by a default animation (LandblockObjectLod.SetPartFrame).
                    var part = parts[p];
                    PreparedLandblockObjects.WriteTransform(buffer, i, AcToGodot.Transform(_lod.PartOrigin(p), _lod.DrawRotation(p), part.InstanceScale));
                }
            }

            multiMesh.Buffer = buffer;
            multiMesh.VisibleInstanceCount = members.Count;
            node.Visible = true;
        }

        VisibleNodeCount += (node.Visible ? 1 : 0) - (wasVisible ? 1 : 0);
        VisibleInstanceCount += (node.Visible ? members.Count : 0) - wasCount;
    }
}
