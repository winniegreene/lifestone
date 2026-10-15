using Godot;
using Lifestone.Client.Effects;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// The draw of one mesh variant of moving objects: a <see cref="MultiMeshInstance3D"/> whose instances are the objects' parts,
/// each a transform plus the four floats of <c>INSTANCE_CUSTOM</c> that the dynamic lighting rules read (x the cell slot, y one
/// plus the light-set row, z the luminosity override, w the translucency; <see cref="Lifestone.Client.Rendering.SurfaceLighting.DynamicObject"/>).
/// The whole buffer is handed to the engine in one call per frame (<see cref="End"/>), the way the particle renderer does.
/// </summary>
/// <remarks>
/// <para>The player draws one instance per batch: each of its parts is a different mesh. The server-objects layer will batch
/// every part that shares a mesh variant and a layer, the same class with more instances.</para>
/// <para>Main thread only.</para>
/// </remarks>
public sealed class DynamicObjectBatch : IDisposable
{
    private readonly MultiMeshInstance3D _node;
    private readonly MultiMesh _multimesh;
    private readonly InstanceBuffer _buffer = new();
    private Aabb _bounds;
    private bool _disposed;

    /// <summary>A hidden batch under <paramref name="parent"/>.</summary>
    /// <param name="parent">The node the batch hangs from (at the render origin: instance transforms are render space).</param>
    /// <param name="name">The node's name.</param>
    /// <param name="layers">The visual layers (the default layer outdoors, the interiors' cells layer in cells).</param>
    public DynamicObjectBatch(Node parent, string name, uint layers)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = true,
        };
        _node = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = _multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Layers = layers,
            Visible = false,
        };
        parent.AddChild(_node);
    }

    /// <summary>The mesh variant the instances draw (null draws nothing).</summary>
    public ArrayMesh? Mesh
    {
        get => _multimesh.Mesh as ArrayMesh;
        set
        {
            if (!ReferenceEquals(_multimesh.Mesh, value))
            {
                _multimesh.Mesh = value;
            }
        }
    }

    /// <summary>Instances written by the last <see cref="Begin"/>/<see cref="End"/>.</summary>
    public int Count => _buffer.Count;

    /// <summary>True while the batch is drawn.</summary>
    public bool Drawn => _node.Visible;

    /// <summary>The visual layers.</summary>
    public uint Layers
    {
        get => _node.Layers;
        set
        {
            if (_node.Layers != value)
            {
                _node.Layers = value;
            }
        }
    }

    /// <summary>Starts a frame's instances: room for <paramref name="needed"/> of them.</summary>
    public void Begin(int needed)
    {
        if (_buffer.Reserve(needed))
        {
            _multimesh.InstanceCount = _buffer.Capacity;
        }

        _bounds = default;
    }

    /// <summary>
    /// Adds an instance: <paramref name="transform"/> in render space, its <paramref name="custom"/> floats, and the instance's
    /// bounds for culling (the mesh's local bounds carried by the transform).
    /// </summary>
    public void Add(in Transform3D transform, Vector4 custom, Aabb localBounds)
    {
        var x = transform.Basis.Column0;
        var y = transform.Basis.Column1;
        var z = transform.Basis.Column2;
        var rows = AffineRows.FromColumns(
            new AcVector3(x.X, x.Y, x.Z),
            new AcVector3(y.X, y.Y, y.Z),
            new AcVector3(z.X, z.Y, z.Z),
            new AcVector3(transform.Origin.X, transform.Origin.Y, transform.Origin.Z));
        _buffer.Add(rows, new System.Numerics.Vector4(custom.X, custom.Y, custom.Z, custom.W));
        var world = transform * localBounds;
        _bounds = _buffer.Count == 1 ? world : _bounds.Merge(world);
    }

    /// <summary>Hands the frame's instances to the engine and shows the batch (hides it when none were added).</summary>
    public void End()
    {
        if (_buffer.Count == 0)
        {
            Hide();
            return;
        }

        RenderingServer.MultimeshSetBuffer(_multimesh.GetRid(), _buffer.Data);
        _multimesh.VisibleInstanceCount = _buffer.Count;
        _multimesh.CustomAabb = _bounds.Grow(0.05f);
        _node.Visible = true;
    }

    /// <summary>Stops drawing the batch.</summary>
    public void Hide()
    {
        if (_node.Visible)
        {
            _node.Visible = false;
            _multimesh.VisibleInstanceCount = 0;
        }
    }

    /// <summary>Frees the node and the MultiMesh now.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (GodotObject.IsInstanceValid(_node))
        {
            _node.Multimesh = null;
            _node.QueueFree();
        }

        _multimesh.Dispose();
    }
}
