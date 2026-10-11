using System.Diagnostics;
using Godot;
using Lifestone.Client.Effects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Effects;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Render.Particles;

/// <summary>
/// Draws the live particles of effect objects every frame: one <see cref="MultiMeshInstance3D"/> per
/// (drawn particle GfxObj, part-material state), each instance a 3×4 transform (billboarded, scaled) plus the part
/// material alpha in <c>INSTANCE_CUSTOM</c>. All selection rules (billboards, LOD, translucency) are the engine-agnostic
/// <see cref="ParticleDrawList"/>; this node only uploads its batches.
/// </summary>
/// <remarks>
/// <para>Place it at the origin of the space the effect objects live in (AC coordinates converted with
/// <see cref="AcToGodot"/>). Batches are created on first use and kept (hidden when empty); their instance buffers
/// grow to the next power of two and never shrink, so steady state allocates nothing on the Godot side.</para>
/// <para>Draw order: within a batch instances keep retail order (object, emitter creation, slot); Godot sorts the
/// transparent batches back to front by their bounds (retail drew its alpha lists unsorted; render-contract.md §2).</para>
/// </remarks>
public partial class ParticleRenderer : Node3D
{
    private readonly Dictionary<ParticleBatchKey, BatchView> _views = new();
    private ParticleMeshLibrary? _library;

    /// <summary>The draw list of the last update (statistics).</summary>
    public ParticleDrawList DrawList { get; } = new();

    /// <summary>Milliseconds the last <see cref="UpdateParticles"/> took (collect + upload).</summary>
    public double LastUpdateMs { get; private set; }

    /// <summary>Batches drawn last frame.</summary>
    public int DrawnBatches { get; private set; }

    /// <summary>
    /// Godot visual layers of the batch nodes (default 1). The world's EnvCell statics draw theirs on the interiors'
    /// cells layer, so they show (and hide) with the cells when the landscape is culled (objects track, living statics).
    /// </summary>
    public uint Layers
    {
        get => _layers;
        set
        {
            _layers = value;
            foreach (var view in _views.Values)
            {
                view.SetLayers(value);
            }
        }
    }

    /// <summary>
    /// The lighting rule of the batches' materials (set before the first update): <see cref="SurfaceLighting.Object"/>
    /// (default), or <see cref="SurfaceLighting.CellObject"/> for particles of statics inside EnvCells, which then take the
    /// depth rules of the cell whose draw-state slot each object passes as its tag (<see cref="UpdateParticles(IReadOnlyList{EffectObject}, AcVector3, IReadOnlyList{int}?)"/>;
    /// <see cref="ParticleShaders.GenerateCode"/>).
    /// </summary>
    public SurfaceLighting Lighting { get; set; } = SurfaceLighting.Object;

    /// <summary>
    /// True (the spell viewer): a mesh not built yet is built on first use, on the main thread. False (the world): such a
    /// batch is skipped and its mesh queued (<see cref="ParticleMeshLibrary.TryGetMesh"/>) until it is ready, so a frame
    /// never decodes a texture.
    /// </summary>
    public bool BuildMissingMeshes { get; set; } = true;

    private uint _layers = 1;

    public void Initialize(ParticleMeshLibrary library) => _library = library;

    /// <summary>Main thread: rebuilds every batch for the given effect objects as seen from <paramref name="viewer"/> (AC).</summary>
    public void UpdateParticles(IEnumerable<EffectObject> objects, AcVector3 viewer)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var library = _library ?? throw new InvalidOperationException("ParticleRenderer.Initialize was not called.");
        var start = Stopwatch.GetTimestamp();
        DrawList.Clear();
        foreach (var fx in objects)
        {
            DrawList.Add(fx, viewer, library);
        }

        Upload(library, start);
    }

    /// <summary>Main thread: <see cref="UpdateParticles(IEnumerable{EffectObject}, AcVector3)"/> over a list, without an enumerator allocation.</summary>
    /// <param name="objects">The effect objects.</param>
    /// <param name="viewer">The viewer (AC, the objects' space).</param>
    /// <param name="tags">Parallel to <paramref name="objects"/>, or null (all 0): each object's tag, carried to its
    /// particles' instance data (<see cref="ParticleDraw.Tag"/>; the cell's draw-state slot under <see cref="SurfaceLighting.CellObject"/>).</param>
    public void UpdateParticles(IReadOnlyList<EffectObject> objects, AcVector3 viewer, IReadOnlyList<int>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var library = _library ?? throw new InvalidOperationException("ParticleRenderer.Initialize was not called.");
        if (tags is not null && tags.Count != objects.Count)
        {
            throw new ArgumentException("One tag per effect object.", nameof(tags));
        }

        var start = Stopwatch.GetTimestamp();
        DrawList.Clear();
        for (var i = 0; i < objects.Count; i++)
        {
            DrawList.Add(objects[i], viewer, library, tags?[i] ?? 0);
        }

        Upload(library, start);
    }

    private void Upload(ParticleMeshLibrary library, long start)
    {
        foreach (var view in _views.Values)
        {
            view.Used = false;
        }

        var drawn = 0;
        var batches = DrawList.Batches;
        for (var b = 0; b < batches.Count; b++)
        {
            var batch = batches[b];
            if (GetView(batch.Key, library) is not { } view)
            {
                continue;
            }

            view.Upload(batch);
            view.Used = true;
            drawn++;
        }

        foreach (var view in _views.Values)
        {
            if (!view.Used)
            {
                view.Hide();
            }
        }

        DrawnBatches = drawn;
        LastUpdateMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Hides every batch (e.g. when particles are switched off).</summary>
    public void HideAll()
    {
        foreach (var view in _views.Values)
        {
            view.Hide();
        }

        DrawnBatches = 0;
    }

    /// <summary>
    /// Main thread: frees every batch node and its MultiMesh now (instance buffers included), rather than at the wrappers'
    /// finalisers; the renderer can be reused or freed afterwards.
    /// </summary>
    public void ReleaseBatches()
    {
        foreach (var view in _views.Values)
        {
            view.Release();
        }

        _views.Clear();
        DrawnBatches = 0;
    }

    private BatchView? GetView(ParticleBatchKey key, ParticleMeshLibrary library)
    {
        if (_views.TryGetValue(key, out var view))
        {
            return view;
        }

        // A GfxObj without geometry is logged once by the library and drawn as nothing. In the world a mesh still being
        // prepared skips the batch for now (BuildMissingMeshes).
        ArrayMesh? mesh;
        if (BuildMissingMeshes)
        {
            mesh = library.GetMesh(key, Lighting);
        }
        else if (!library.TryGetMesh(key, out mesh, Lighting))
        {
            return null;
        }

        if (mesh is null)
        {
            return null;
        }

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = true,
            Mesh = mesh,
            ResourceName = mesh.ResourceName,
        };
        var node = new MultiMeshInstance3D
        {
            Name = $"Particles_0x{key.GfxObjId:X8}{(key.PartMaterial ? "_part" : string.Empty)}",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Layers = _layers,
        };
        AddChild(node);
        // Largest distance of the mesh from its origin (any orientation), to pad the batch bounds.
        var aabb = mesh.GetAabb();
        var radius = Math.Max(aabb.Position.Length(), aabb.End.Length());
        view = new BatchView(node, multimesh, radius);
        _views.Add(key, view);
        return view;
    }

    private sealed class BatchView(MultiMeshInstance3D node, MultiMesh multimesh, float meshRadius)
    {
        private readonly InstanceBuffer _buffer = new();

        public bool Used { get; set; }

        public void Upload(ParticleBatch batch)
        {
            var draws = batch.Draws;
            if (_buffer.Reserve(draws.Count))
            {
                multimesh.InstanceCount = _buffer.Capacity;
            }

            foreach (var d in draws)
            {
                // The one AC -> Godot conversion; AffineRows then just carries Godot-axis numbers into the buffer layout.
                var t = AcToGodot.Transform(d.Origin, d.Orientation, new AcVector3(d.Scale));
                var x = t.Basis.Column0;
                var y = t.Basis.Column1;
                var z = t.Basis.Column2;
                var rows = AffineRows.FromColumns(
                    new AcVector3(x.X, x.Y, x.Z),
                    new AcVector3(y.X, y.Y, y.Z),
                    new AcVector3(z.X, z.Y, z.Z),
                    new AcVector3(t.Origin.X, t.Origin.Y, t.Origin.Z));
                _buffer.Add(rows, InstanceBuffer.ParticleCustom(d.Alpha, d.PartMaterial, d.Tag));
            }

            RenderingServer.MultimeshSetBuffer(multimesh.GetRid(), _buffer.Data);
            multimesh.VisibleInstanceCount = draws.Count;
            var (min, max) = batch.Bounds(meshRadius);
            multimesh.CustomAabb = AcToGodot.Bounds(min, max);
            node.Visible = true;
        }

        public void Hide()
        {
            if (node.Visible)
            {
                node.Visible = false;
                multimesh.VisibleInstanceCount = 0;
            }
        }

        public void SetLayers(uint layers) => node.Layers = layers;

        public void Release()
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.Multimesh = null;
                node.QueueFree();
            }

            multimesh.Dispose();
        }
    }
}
