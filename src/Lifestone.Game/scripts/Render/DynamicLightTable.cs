using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.Render;

/// <summary>
/// The global dynamic-light table of the cell glow (M3 4.1 r2): the sampler <c>ac_dynamic_lights</c>
/// (<see cref="SurfaceLightingCode.DynamicLights"/>), one row of <see cref="DynamicLightTexels.Width"/> RGBA32F texels for up to eight lights
/// (position in Godot world space, reach; colour times intensity), rewritten every frame from the frame's <see cref="DynamicLightFrame"/> and
/// read per vertex by the EnvCell and CellObject variants (retail's hardware lights on a cell's polygons, <c>Render::minimize_envcell_lighting</c>
/// 0x0054CD80; <c>PrimD3DRender::config_hardware_light</c> 0x0059BD40). The packing is <see cref="DynamicLightTexels.Pack"/>.
/// </summary>
/// <remarks>
/// <para>The table refreshes itself in <see cref="RenderingServer.FramePreDraw"/>, after the frame's process calls (the avatars' tick fills the
/// frame in <c>AvatarSet.Advance</c>) and before the draw, so positions are converted with the render origin the frame draws with. It touches
/// the texture only when the packed lights changed (a standing bolt-less room costs a compare). Allocation-free after construction. Main thread.</para>
/// <para>The global is declared at run time by <see cref="Declare"/> (the shader generator calls it before it makes any shader that names the
/// sampler), so no project file has to carry it; until a table binds a texture the sampler is a zero table, which has no lights.</para>
/// </remarks>
public sealed class DynamicLightTable : IDisposable
{
    private static bool _declared;
    private static ImageTexture? _emptyTexture;

    private readonly Func<GlobalPosition, Vector3> _toRender;
    private readonly Image _image;
    private readonly ImageTexture _texture;
    private readonly float[] _next = new float[DynamicLightTexels.Floats];
    private readonly float[] _shadow = new float[DynamicLightTexels.Floats];
    private readonly AcVector3[] _positions = new AcVector3[DynamicLightFrame.MaxLights];
    private readonly Vector3[] _godotPositions = new Vector3[DynamicLightTexels.MaxLights];
    private readonly float[] _reaches = new float[DynamicLightTexels.MaxLights];
    private bool _disposed;

    /// <summary>A table that converts positions with <paramref name="toRender"/> (AC global metres to Godot render space: the world root's).</summary>
    public DynamicLightTable(Func<GlobalPosition, Vector3> toRender)
    {
        ArgumentNullException.ThrowIfNull(toRender);
        _toRender = toRender;
        Declare();
        _image = Image.CreateEmpty(DynamicLightTexels.Width, 1, false, Image.Format.Rgbaf);
        _image.Fill(new Color(0f, 0f, 0f, 0f));
        _texture = ImageTexture.CreateFromImage(_image);
        _texture.ResourceName = "lifestone_dynamic_lights";
        RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.DynamicLights, _texture);
        RenderingServer.FramePreDraw += Refresh;
    }

    /// <summary>The frame to read (<c>AvatarSet.DynamicLightFrame</c>); null is no lights. Set again after a log off makes a new set.</summary>
    public DynamicLightFrame? Source { get; set; }

    /// <summary>Lights in the table as of the last refresh.</summary>
    public int Count { get; private set; }

    /// <summary>Times the texture was uploaded (a refresh in which the packed lights changed).</summary>
    public long Uploads { get; private set; }

    /// <summary>The <paramref name="index"/>th light's position in Godot render space, as of the last refresh.</summary>
    public Vector3 PositionOf(int index) => _godotPositions[index];

    /// <summary>The <paramref name="index"/>th light's reach in metres, as of the last refresh.</summary>
    public float ReachOf(int index) => _reaches[index];

    /// <summary>
    /// Declares the global sampler (once) and binds a zero table to it. Safe to call from anywhere on the main thread, and cheap after the first
    /// call; shaders that name <see cref="SurfaceLightingCode.DynamicLights"/> compile against it. The project does not declare it, so this adds it; the server's parameter list is never read (editor-only engine call).
    /// </summary>
    public static void Declare()
    {
        if (_declared)
        {
            return;
        }

        _declared = true;
        var name = new StringName(SurfaceLightingCode.DynamicLights);
        // RenderingServer.GlobalShaderParameterGetList() logs an engine ERROR outside the editor, so the server is not asked: the project's
        // shader_globals do not declare this sampler (project.godot), and _declared makes this the only place that adds it.
        RenderingServer.GlobalShaderParameterAdd(name, RenderingServer.GlobalShaderParameterType.Sampler2D, Variant.From(string.Empty));

        var empty = Image.CreateEmpty(DynamicLightTexels.Width, 1, false, Image.Format.Rgbaf);
        empty.Fill(new Color(0f, 0f, 0f, 0f));
        _emptyTexture = ImageTexture.CreateFromImage(empty);
        _emptyTexture.ResourceName = "lifestone_dynamic_lights_empty";
        RenderingServer.GlobalShaderParameterSet(name, _emptyTexture);
    }

    /// <summary>Packs the source's lights and uploads them when they changed. The table calls it before every draw.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var lights = Source is { } frame ? frame.Lights : default;
        for (var i = 0; i < lights.Length; i++)
        {
            ref readonly var light = ref lights[i];
            var block = AcCoordinates.LandblockOrigin(LandblockCoord.FromId(light.CellId));
            var p = _toRender(block + light.Position);
            _positions[i] = new AcVector3(p.X, p.Y, p.Z);
        }

        Count = DynamicLightTexels.Pack(lights, _positions, _next);
        for (var i = 0; i < Count; i++)
        {
            var at = i * DynamicLightTexels.TexelsPerLight * 4;
            _godotPositions[i] = new Vector3(_next[at], _next[at + 1], _next[at + 2]);
            _reaches[i] = _next[at + 3];
        }

        if (_next.AsSpan().SequenceEqual(_shadow))
        {
            return;
        }

        _next.CopyTo(_shadow, 0);
        for (var t = 0; t < DynamicLightTexels.Width; t++)
        {
            var at = t * 4;
            _image.SetPixel(t, 0, new Color(_next[at], _next[at + 1], _next[at + 2], _next[at + 3]));
        }

        _texture.Update(_image);
        Uploads++;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RenderingServer.FramePreDraw -= Refresh;
        if (_emptyTexture is not null)
        {
            RenderingServer.GlobalShaderParameterSet(SurfaceLightingCode.DynamicLights, _emptyTexture);
        }

        _texture.Dispose();
        _image.Dispose();
    }
}
