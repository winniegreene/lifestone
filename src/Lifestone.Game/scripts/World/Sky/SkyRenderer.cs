using Godot;
using Lifestone.Client.Sky;
using Lifestone.Client.World;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Sky;

/// <summary>
/// Draws the sky objects of <see cref="SkyObjectTracker"/>: one node per live slot, one material per drawn surface
/// (each slot has its own luminosity, diffusion, translucency and scroll), placed where retail's
/// <c>GameSky::UpdatePosition</c> puts them and ordered as retail draws them.
/// </summary>
/// <remarks>
/// <para><b>Placement.</b> Non-weather objects sit at their anchor (retail: the viewer's landblock south-west corner,
/// height 0) rotated by <c>GameSky::CalcFrame</c>; weather objects at the viewer's x, y with z = -120 m unless they keep
/// the viewer's z (<see cref="SkyObjectTracker.LocalOrigin"/>).</para>
/// <para><b>Passes.</b> Before-pass objects live in the <see cref="SkyView"/> (sky-local space, the anchor at the
/// origin), whose image is the main view's background; after-pass (weather) objects live in the main world (placed from
/// global coordinates every frame: floating origin) and draw over the landscape.</para>
/// <para><b>Order.</b> <c>render_priority</c> from -128 upwards in slot, part and subset order: the before pass in the sky
/// view, then the after pass in the main view below the world's translucent surfaces (priority 0), as retail draws the
/// weather cell before it flushes its alpha list; see <see cref="SkyShaders"/>.</para>
/// </remarks>
public partial class SkyRenderer : Node3D
{
    private const int FirstPriority = -128; // RenderingServer.MATERIAL_RENDER_PRIORITY_MIN
    private const int LastPriority = -1;

    private readonly Dictionary<int, SlotInstance> _instances = new();
    private readonly List<int> _retired = new();
    private SkyView _view = null!;
    private SkyModels _models = null!;
    private SkyShaders _shaders = null!;
    private MaterialFactory _materials = null!;
    private bool _orderDirty = true;
    private bool _warnedPriority;
    private readonly List<Node3D> _pipelineWarmers = new();
    private int _warmFrames = -1;

    /// <summary>Surfaces drawn last frame (diagnostics).</summary>
    public int DrawnSurfaces { get; private set; }

    /// <summary>Live slot nodes.</summary>
    public int InstanceCount => _instances.Count;

    /// <summary>Slot nodes created by the last <see cref="Apply"/> (diagnostics).</summary>
    public int CreatedThisFrame { get; private set; }

    /// <summary>Surface shader changes made by the last <see cref="Apply"/> (diagnostics).</summary>
    public int ShaderSwapsThisFrame { get; private set; }

    /// <summary>The last <see cref="Apply"/> created or freed the start-up pipeline warmers (diagnostics).</summary>
    public bool WarmingThisFrame { get; private set; }

    public SkyModels Models => _models;

    /// <summary>True when every live slot's model is built.</summary>
    public bool AllReady { get; private set; }

    /// <summary>The background for the main view's environment: the sky view's image.</summary>
    public Godot.Sky Background { get; private set; } = null!;

    /// <summary>The off-screen view of the before pass.</summary>
    public SkyView View => _view;

    public void Initialize(SkyModels models, SkyShaders shaders, MaterialFactory materials)
    {
        _models = models;
        _shaders = shaders;
        _materials = materials;
        Name = "Sky";
        _view = SkyView.Create();
        AddChild(_view);
        var background = new ShaderMaterial { Shader = new Shader { Code = SkyShaders.BackgroundCode() }, ResourceName = "lifestone_sky_background" };
        background.SetShaderParameter(SkyShaders.ViewTextureParameter, _view.GetTexture());
        Background = new Godot.Sky
        {
            SkyMaterial = background,
            RadianceSize = Godot.Sky.RadianceSizeEnum.Size32,
            ProcessMode = Godot.Sky.ProcessModeEnum.Quality,
        };
    }

    /// <summary>Requests every model a day group can show (keyframe replacements included).</summary>
    public void Preload(SkyDirector director, int dayGroup)
    {
        foreach (var id in SkyDirector.ModelIds(director.Sky.Sky.DayGroups[dayGroup]))
        {
            _models.Request(id);
        }
    }

    /// <summary>Main thread, once per frame after the director's update.</summary>
    /// <param name="director">The sky state.</param>
    /// <param name="toRender">Global position to render space (the world's floating origin).</param>
    /// <param name="anchor">Where non-weather objects sit (retail: the viewer's landblock corner, z = 0).</param>
    /// <param name="viewer">The viewer (camera) position.</param>
    /// <param name="camera">The main camera (the sky view copies it).</param>
    /// <param name="viewSize">The main view's size in pixels.</param>
    public void Apply(SkyDirector director, Func<GlobalPosition, Vector3> toRender, GlobalPosition anchor, GlobalPosition viewer, Camera3D camera, Vector2I viewSize)
    {
        ArgumentNullException.ThrowIfNull(director);
        ArgumentNullException.ThrowIfNull(camera);
        WarmPipelines(camera);
        CreatedThisFrame = 0;
        ShaderSwapsThisFrame = 0;
        var slots = director.Objects.Slots;

        // A standing clock writes exact parameter values; a running one skips sub-byte changes (SurfaceInstance).
        var exact = director.Last?.Unchanged ?? true;
        var allReady = true;

        // Retire instances whose slot no longer holds the same object.
        _retired.Clear();
        foreach (var (index, instance) in _instances)
        {
            if (index >= slots.Count || slots[index].Generation != instance.Generation || !slots[index].Exists)
            {
                _retired.Add(index);
            }
        }

        foreach (var index in _retired)
        {
            _instances.Remove(index, out var instance);
            instance!.Free();
            _orderDirty = true;
        }

        var drawn = 0;
        var viewerLocal = new System.Numerics.Vector3((float)(viewer.X - anchor.X), (float)(viewer.Y - anchor.Y), (float)(viewer.Z - anchor.Z));
        _view.Follow(camera, AcToGodot.Position(viewerLocal), viewSize);
        for (var i = 0; i < slots.Count; i++) // an index loop: foreach over the interface would box an enumerator
        {
            var slot = slots[i];
            if (!slot.Exists)
            {
                continue;
            }

            if (!_instances.TryGetValue(slot.Index, out var instance))
            {
                if (_models.TryGet(slot.GfxObjId) is not { } model)
                {
                    allReady &= _models.IsReady(slot.GfxObjId);
                    continue;
                }

                instance = Create(slot, model);
                _instances.Add(slot.Index, instance);
                CreatedThisFrame++;
                _orderDirty = true;
            }

            var local = SkyObjectTracker.LocalOrigin(slot, viewerLocal);
            instance.Node.Position = slot.Pass == SkyPass.Before
                ? AcToGodot.Position(local)
                : toRender(new GlobalPosition(anchor.X + local.X, anchor.Y + local.Y, anchor.Z + local.Z));
            instance.Node.Quaternion = AcToGodot.Rotation(slot.Orientation);
            instance.Node.Visible = slot.Drawn;
            if (slot.Drawn)
            {
                drawn += instance.Update(slot, director, _shaders, exact, ref _orderDirty, out var swaps);
                ShaderSwapsThisFrame += swaps;
            }
        }

        if (_orderDirty)
        {
            AssignOrder(slots);
            _orderDirty = false;
        }

        DrawnSurfaces = drawn;
        AllReady = allReady;
    }

    /// <summary>
    /// Draws one sub-pixel triangle with every warmed-up sky material for a few frames, in the view it belongs to, so
    /// the GPU pipelines for the sky view's float target and the main view exist before a sky object first needs them
    /// (a day-group change or a keyframe that makes a part translucent would otherwise build them mid-frame).
    /// </summary>
    private void WarmPipelines(Camera3D mainCamera)
    {
        WarmingThisFrame = false;
        if (_warmFrames < 0)
        {
            WarmingThisFrame = true;
            var triangle = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(0f, 0f, -0.5f), new(0.0001f, 0f, -0.5f), new(0f, 0.0001f, -0.5f) };
            arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back };
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { Vector2.Zero, Vector2.Right, Vector2.Down };
            arrays[(int)Mesh.ArrayType.Custom0] = new float[] { 0f, 0f, 0f };
            arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 2, 1 };
            triangle.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: MeshFactory.ZeroNormalFormat);
            foreach (var (variant, material) in _shaders.WarmMaterials)
            {
                var warmer = new MeshInstance3D { Name = $"warm_{variant.Name}", Mesh = triangle, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                warmer.SetSurfaceOverrideMaterial(0, material);
                (variant.Pass == SkyPass.Before ? _view.Camera : mainCamera).AddChild(warmer);
                _pipelineWarmers.Add(warmer);
            }

            _warmFrames = 0;
            return;
        }

        if (_pipelineWarmers.Count > 0 && ++_warmFrames >= 3)
        {
            WarmingThisFrame = true;
            foreach (var warmer in _pipelineWarmers)
            {
                warmer.QueueFree();
            }

            _pipelineWarmers.Clear();
        }
    }

    private SlotInstance Create(SkySlot slot, SkyModel model)
    {
        var node = new Node3D { Name = $"Sky{slot.Index:D2}_0x{slot.GfxObjId:X8}" };
        if (slot.Pass == SkyPass.Before)
        {
            _view.Objects.AddChild(node);
        }
        else
        {
            AddChild(node);
        }
        var instance = new SlotInstance(slot.Generation, node);
        foreach (var part in model.Parts)
        {
            var mi = new MeshInstance3D
            {
                Name = $"0x{part.GfxObjId:X8}",
                Mesh = part.Mesh,
                Transform = part.Transform,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                CustomAabb = SkyModels.CullBounds,
            };
            node.AddChild(mi);
            for (var s = 0; s < part.Surfaces.Count; s++)
            {
                var surface = part.Surfaces[s];
                ShaderMaterial material;
                if (surface.IsError)
                {
                    material = _materials.ErrorMaterial($"sky object 0x{slot.GfxObjId:X8} part 0x{part.GfxObjId:X8} surface {s}");
                }
                else
                {
                    material = new ShaderMaterial { ResourceName = $"sky{slot.Index:D2}_0x{part.GfxObjId:X8}_{s}" };
                    if (surface.Texture is { } texture)
                    {
                        material.SetShaderParameter(MaterialFactory.TextureParameter, texture);
                    }
                    else
                    {
                        material.SetShaderParameter("solid_color", surface.SolidColor);
                    }

                    material.SetShaderParameter("alpha_ref", (float)surface.Material!.RenderState.AlphaTestReference);
                }

                mi.SetSurfaceOverrideMaterial(s, material);
                instance.Surfaces.Add(new SurfaceInstance(part.GfxObjId, surface, material));
            }
        }

        return instance;
    }

    private void AssignOrder(IReadOnlyList<SkySlot> slots)
    {
        foreach (var pass in (ReadOnlySpan<SkyPass>)[SkyPass.Before, SkyPass.After])
        {
            var priority = FirstPriority;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Pass != pass || !_instances.TryGetValue(slot.Index, out var instance))
                {
                    continue;
                }

                foreach (var surface in instance.Surfaces)
                {
                    if (priority > LastPriority && !_warnedPriority)
                    {
                        _warnedPriority = true;
                        Log.Warn("sky", "more sky surfaces than render priorities below the world's; the last ones share one", ("surfaces", instance.Surfaces.Count));
                    }

                    surface.Material.RenderPriority = Math.Min(priority++, LastPriority);
                }
            }
        }
    }

    private sealed class SlotInstance(int generation, Node3D node)
    {
        public int Generation { get; } = generation;

        public Node3D Node { get; } = node;

        public List<SurfaceInstance> Surfaces { get; } = new();

        /// <summary>Updates per-surface parameters; returns the surfaces drawn.</summary>
        /// <param name="exact">Write every change, however small (the clock stands still, so this is the last write).</param>
        public int Update(SkySlot slot, SkyDirector director, SkyShaders shaders, bool exact, ref bool orderDirty, out int swaps)
        {
            var drawn = 0;
            swaps = 0;
            foreach (var s in Surfaces)
            {
                if (s.Surface.IsError || s.Surface.Material is not { } surface)
                {
                    continue;
                }

                var variant = SkySurfaceVariant.For(surface, s.Surface.Addressing, slot.Material, slot.Pass);
                if (variant != s.Variant)
                {
                    s.Variant = variant;
                    s.Material.Shader = shaders.Get(variant);
                    swaps++;
                    orderDirty = true; // keep priorities explicit after a swap
                }

                s.SetLight(slot.Material.VertexAlpha(surface), slot.Material.Emissive(surface), slot.Material.DiffuseFactor, exact);
                var offset = director.ScrollOffset(s.PartGfxObjId, s.Surface.Addressing == TextureAddressMode.Wrap);
                s.SetUvOffset(new Vector2(offset.X, offset.Y), exact);
                drawn++;
            }

            return drawn;
        }

        public void Free()
        {
            if (GodotObject.IsInstanceValid(Node))
            {
                Node.QueueFree();
            }
        }
    }

    /// <summary>
    /// One drawn surface's material and the parameter values last written to it.
    /// </summary>
    /// <remarks>
    /// <para>Parameter names are cached <see cref="StringName"/>s: a string name converts to a new finalizable StringName
    /// (plus a weak-reference registration) on every call, which while the clock runs was dozens per frame and made GC
    /// stalls of 20-50 ms (docs/sky.md §7).</para>
    /// <para>While the clock runs, a light value is only re-written once it has moved by half a byte step (0.5 / 255:
    /// the lit colour and the blend are byte-quantised in retail, so the picture differs by at most one byte level
    /// meanwhile) and a scroll offset by 1/4096 of the texture. A standing clock (<c>exact</c>) writes any change, so a
    /// paused or captured picture holds the exact values.</para>
    /// </remarks>
    private sealed class SurfaceInstance(uint partGfxObjId, SkyModelSurface surface, ShaderMaterial material)
    {
        private const float LightStep = 0.5f / 255f;
        private const float UvStep = 1f / 4096f;

        private static readonly StringName VertexAlphaName = "vertex_alpha";
        private static readonly StringName EmissiveName = "emissive";
        private static readonly StringName DiffuseName = "diffuse";
        private static readonly StringName UvOffsetName = "uv_offset";

        // NaN = never written: the first comparison is always "changed".
        private float _vertexAlpha = float.NaN;
        private float _emissive = float.NaN;
        private float _diffuse = float.NaN;
        private Vector2 _uvOffset = new(float.NaN, float.NaN);

        public uint PartGfxObjId { get; } = partGfxObjId;

        public SkyModelSurface Surface { get; } = surface;

        public ShaderMaterial Material { get; } = material;

        public SkySurfaceVariant? Variant { get; set; }

        /// <summary>Writes the lighting parameters that moved.</summary>
        public void SetLight(float vertexAlpha, float emissive, float diffuse, bool exact)
        {
            Write(VertexAlphaName, ref _vertexAlpha, vertexAlpha, exact);
            Write(EmissiveName, ref _emissive, emissive, exact);
            Write(DiffuseName, ref _diffuse, diffuse, exact);
        }

        /// <summary>Writes the scroll offset when it moved.</summary>
        public void SetUvOffset(Vector2 offset, bool exact)
        {
            if (offset == _uvOffset || (!exact && MathF.Abs(offset.X - _uvOffset.X) < UvStep && MathF.Abs(offset.Y - _uvOffset.Y) < UvStep))
            {
                return;
            }

            _uvOffset = offset;
            Material.SetShaderParameter(UvOffsetName, offset);
        }

        private void Write(StringName name, ref float last, float value, bool exact)
        {
            // NaN compares false both ways, so a never-written parameter always falls through.
            if (value == last || (!exact && MathF.Abs(value - last) < LightStep))
            {
                return;
            }

            last = value;
            Material.SetShaderParameter(name, value);
        }
    }
}
