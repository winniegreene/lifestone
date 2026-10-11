using System.Runtime.InteropServices;
using Godot;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>Where the main view blends (docs/render-contract.md §2.3).</summary>
public enum BlendSpace
{
    /// <summary>
    /// Retail's: every blend is arithmetic on the raw bytes / 255 the shaders compute, as D3D9 blended into retail's
    /// 8-bit framebuffer. The default (<c>--blend-space=byte</c>).
    /// </summary>
    Byte,

    /// <summary>
    /// Godot's: shaders hand over sRGB-decoded colours and the blends run in linear light (the M1 wave-2 pipeline,
    /// <c>--blend-space=linear</c>, kept for comparison).
    /// </summary>
    Linear,
}

/// <summary>
/// Byte-space blending for the main view: the surface, terrain and sky shaders write raw retail values (bytes / 255)
/// into Godot's float colour buffer, so every blend between draws is retail's byte arithmetic; the tonemap pass's 1D
/// colour-correction LUT then undoes the sRGB encode it applies, so exactly those bytes reach the screen.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Retail blended in its 8-bit framebuffer (D3D9 fixed function, no sRGB blending:
/// <c>RenderDeviceD3D::SetBlendFunction</c>, D3DRS_SRGBWRITEENABLE never set). Lifestone's shaders used to convert each
/// draw to linear (<c>ac_srgb_to_linear</c>) and let Godot blend in linear light, so additive glows were strong over dark
/// surfaces and faint over bright ones, and half-transparent surfaces let more light through than retail
/// (docs/render-contract.md §2.3, docs/spell-viewer.md §7). The sky's own view already blended in byte space
/// (docs/sky.md §4.3); this does the same for the main view.</para>
/// <para><b>How (Godot 4.7, Forward+; every step pinned on the engine by <c>--blend-checks</c>):</b></para>
/// <list type="number">
/// <item>The global shader uniform <see cref="GlobalName"/> tells the shaders to output raw values
/// (<see cref="RenderGlobals.OutputFunctionCode"/>; the terrain shader and the sky background have the same switch).</item>
/// <item>The main view's colour buffer is floating point (RGBA16F), so blends are float arithmetic on those raw values.
/// Nothing clamps between draws: retail's 8-bit buffer saturated after every blend; the checks pin this deviation, as
/// the sky's do for the sky view.</item>
/// <item>The tonemap pass (Linear, exposure 1, white 1: an identity) sRGB-encodes the buffer for the non-HDR window and
/// then, with <see cref="Godot.Environment.AdjustmentEnabled"/>, applies brightness/contrast/saturation (left at 1, an
/// exact identity) and the 1D colour-correction LUT. The LUT is the exact sRGB decode (<see cref="LutSize"/> float texels
/// sampled at their centres), so encode then decode gives back the raw value, saturated at 1 as retail's framebuffer was.
/// No per-frame code and no extra pass: one texture fetch per channel in a pass Godot runs anyway.</item>
/// <item>The clear colour is linearised by Godot, so in byte space the fog background is handed over sRGB-encoded
/// (<see cref="BackgroundColor"/>) to land in the buffer as its raw value.</item>
/// <item>Built-in Godot 3D materials (Label3D, Sprite3D, StandardMaterial3D, ...) decode their colours the same way, so
/// they get them through <see cref="BuiltinColor"/> (review fix; guarded by <c>--blend-checks</c> and
/// <c>BuiltinMaterialColourRuleTests</c>).</item>
/// </list>
/// <para>A viewport outside the main <see cref="WorldEnvironment"/> (its own World3D or camera environment) does not get
/// the LUT: it must run in linear space or be given the LUT with <see cref="Attach(Godot.Environment)"/>. Scenes that
/// never call <see cref="Select"/> (the terrain preview and its golden test) keep the project default, linear; the sky
/// view outputs raw values by design and is converted by the main view's background shader.</para>
/// </remarks>
public static class ByteSpaceBlending
{
    /// <summary>bool global shader uniform: shaders output raw byte-space values (declared in <c>project.godot</c>).</summary>
    public const string GlobalName = "ac_byte_space";

    /// <summary>Texels of the decode LUT (the interpolation error is below 1e-7 with 4,096).</summary>
    public const int LutSize = 4096;

    private static readonly List<Godot.Environment> Attached = new();
    private static ImageTexture? _lut;

    /// <summary>The blend space shaders are writing for (the project default, linear, until <see cref="Select"/>).</summary>
    public static BlendSpace Current { get; private set; } = BlendSpace.Linear;

    /// <summary>
    /// Main thread: selects the blend space for every Lifestone shader (the global uniform) and turns the decode LUT of
    /// every attached environment on or off to match. Call before computing background colours (<see cref="BackgroundColor"/>).
    /// </summary>
    public static void Select(BlendSpace space)
    {
        Current = space;
        RenderingServer.GlobalShaderParameterSet(GlobalName, space == BlendSpace.Byte);
        foreach (var environment in Attached)
        {
            Apply(environment);
        }
    }

    /// <summary>
    /// Main thread: gives the scene's main <see cref="WorldEnvironment"/> the decode LUT while <see cref="Current"/> is
    /// <see cref="BlendSpace.Byte"/>, and follows later <see cref="Select"/> calls until the node leaves the tree.
    /// </summary>
    public static void Attach(WorldEnvironment node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Environment is not { } environment)
        {
            throw new InvalidOperationException("ByteSpaceBlending.Attach: the WorldEnvironment has no Environment.");
        }

        Attach(environment);
        node.TreeExiting += () => Attached.Remove(environment);
        Log.Info("render", "blend space", ("space", Current), ("decode_lut", environment.AdjustmentEnabled));
    }

    /// <summary>Main thread: as <see cref="Attach(WorldEnvironment)"/> for an environment of one's own (call Select to switch).</summary>
    public static void Attach(Godot.Environment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!Attached.Contains(environment))
        {
            Attached.Add(environment);
        }

        Apply(environment);
    }

    /// <summary>
    /// The <see cref="Godot.Environment.BackgroundColor"/> that lands in the colour buffer as the raw retail colour
    /// <paramref name="raw"/> (bytes / 255) in the current blend space. Godot linearises the clear colour; in byte space
    /// the buffer must hold the raw value, so it is handed over sRGB-encoded.
    /// </summary>
    public static Color BackgroundColor(Color raw) => BuiltinColor(raw);

    /// <summary>
    /// The colour to hand a <b>built-in</b> Godot material in the main view (a <see cref="Label3D"/> or
    /// <see cref="Sprite3D"/> <c>Modulate</c>, a <see cref="StandardMaterial3D"/> <c>AlbedoColor</c>, ...) so that it lands
    /// in the colour buffer as the raw value <paramref name="raw"/> (bytes / 255, or a designer's sRGB colour) and reaches
    /// the screen unchanged in the current blend space.
    /// </summary>
    /// <remarks>
    /// Built-in materials sRGB-decode the colours they are given (Godot treats them as sRGB). In linear space the tonemap
    /// re-encodes, so <paramref name="raw"/> is returned as is; in byte space the decode LUT decodes the frame once more, so
    /// a colour handed over unchanged would be decoded twice (darker and more saturated: the spell-viewer words' cream
    /// (1, 0.95, 0.75) showed as golden (237, 214, 133)). It is therefore handed over sRGB-encoded. Alpha is unchanged.
    /// Pinned on the engine by <c>--blend-checks</c> (a StandardMaterial3D probe and a Label3D probe). Call after
    /// <see cref="Select"/>, and again if the space changes. Rule: docs/render-contract.md §2.3.
    /// </remarks>
    public static Color BuiltinColor(Color raw) => Current == BlendSpace.Byte ? raw.LinearToSrgb() : raw;

    private static void Apply(Godot.Environment environment)
    {
        var on = Current == BlendSpace.Byte;
        environment.AdjustmentEnabled = on;
        environment.AdjustmentBrightness = 1f;
        environment.AdjustmentContrast = 1f;
        environment.AdjustmentSaturation = 1f;
        environment.AdjustmentColorCorrection = on ? Lut : null;
    }

    /// <summary>The exact sRGB decode as a 1D LUT: texel i holds srgb_to_linear((i + 0.5) / n) in every channel (RGB float).</summary>
    private static ImageTexture Lut => _lut ??= CreateLut();

    private static ImageTexture CreateLut()
    {
        var texels = new float[LutSize * 3];
        for (var i = 0; i < LutSize; i++)
        {
            var s = (i + 0.5) / LutSize;
            var linear = (float)(s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4));
            texels[i * 3] = texels[(i * 3) + 1] = texels[(i * 3) + 2] = linear;
        }

        using var image = Image.CreateFromData(LutSize, 1, false, Image.Format.Rgbf, MemoryMarshal.AsBytes(texels.AsSpan()).ToArray());
        var texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName = "lifestone_byte_space_decode_lut";
        return texture;
    }
}
