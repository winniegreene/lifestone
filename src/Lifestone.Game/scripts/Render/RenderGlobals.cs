using Godot;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>
/// The global shader uniforms every Lifestone shader reads for lighting and fog (docs/render-contract.md §1). They are
/// declared in <c>project.godot</c> (<c>[shader_globals]</c>) so shaders compile against them, and driven at run time:
/// noon defaults from <see cref="DerethSky"/> now, the sky track's time of day later.
/// </summary>
/// <remarks>
/// All colours are raw retail values (bytes / 255, the float <c>0.0039215689</c> retail uses), not sRGB-decoded; all
/// directions are in Godot world axes (<see cref="AcToGodot.Direction"/>). Shaders do their arithmetic on raw values
/// and convert to linear at the end (docs/terrain-shader.md §6).
/// </remarks>
public static class RenderGlobals
{
    /// <summary>vec3: unit vector towards the sun, Godot axes (<c>LScape::sunlight</c> normalised).</summary>
    public const string SunDirection = "ac_sun_dir";

    /// <summary>
    /// vec3: the D3D sun light's diffuse colour, <c>sunlight_color x |sunlight|</c> (<see cref="SkyLighting.SunDiffuse"/>;
    /// <c>PrimD3DRender::UpdateLightsInternal</c>). Terrain's <c>sunColor/255 x max(0, n · sunVec)</c> equals
    /// <c>ac_sun_color x max(0, n · ac_sun_dir)</c>, so terrain uses it too.
    /// </summary>
    public const string SunColor = "ac_sun_color";

    /// <summary>
    /// vec3: world ambient for objects, <c>ambient_color x (ambient_level + 0.2 |sunlight|)</c>
    /// (<see cref="SkyLighting.ObjectAmbient"/>; <c>SmartBox::SetWorldAmbientLight</c>, <c>LScape::calc_object_light</c>).
    /// </summary>
    public const string Ambient = "ac_ambient";

    /// <summary>
    /// vec3: landscape ambient, <c>ambient_color x ambient_level</c> (<see cref="SkyLighting.LandscapeAmbient"/>;
    /// <c>CLandBlockStruct::calc_lighting</c>). Terrain only.
    /// </summary>
    public const string LandAmbient = "ac_land_ambient";

    /// <summary>vec3: fog colour (D3DRS_FOGCOLOR, <see cref="SkyFog.Color"/>).</summary>
    public const string FogColor = "ac_fog_color";

    /// <summary>float: fog start in metres (D3DRS_FOGSTART).</summary>
    public const string FogStart = "ac_fog_start";

    /// <summary>float: fog end in metres (D3DRS_FOGEND).</summary>
    public const string FogEnd = "ac_fog_end";

    /// <summary>bool: world fog on (<see cref="SkyFog.Enabled"/>). Additive surfaces never fog, whatever this says.</summary>
    public const string FogEnabled = "ac_fog_enabled";

    /// <summary>float: highest mip level sampled (3 = retail's at most 4 levels; raise to see Godot's full chain).</summary>
    public const string MaxMipLevel = "ac_max_mip_level";

    /// <summary>The <c>global uniform</c> declarations to paste into a shader.</summary>
    public const string ShaderDeclarations =
        "global uniform vec3 ac_sun_dir;\n" +
        "global uniform vec3 ac_sun_color;\n" +
        "global uniform vec3 ac_ambient;\n" +
        "global uniform vec3 ac_land_ambient;\n" +
        "global uniform vec3 ac_fog_color;\n" +
        "global uniform float ac_fog_start;\n" +
        "global uniform float ac_fog_end;\n" +
        "global uniform bool ac_fog_enabled;\n" +
        "global uniform float ac_max_mip_level;\n" +
        "global uniform bool ac_byte_space;   // render-fidelity fix: blend in retail's byte space (ByteSpaceBlending)\n";

    /// <summary>
    /// The one place a shader turns its raw retail colour (bytes / 255) into what it writes: unchanged in byte space
    /// (<see cref="ByteSpaceBlending"/> converts the whole frame once, after every blend), else the exact sRGB decode
    /// the Linear tonemapper re-encodes (docs/terrain-shader.md §6). Paste after <see cref="ShaderDeclarations"/>; write
    /// <c>ALBEDO = ac_output(c);</c>.
    /// </summary>
    public const string OutputFunctionCode =
        "vec3 ac_srgb_to_linear(vec3 c) {\n" +
        "\treturn mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));\n" +
        "}\n\n" +
        "// docs/render-contract.md §2.3: raw byte-space values when the frame is decoded once (ac_byte_space), else linear.\n" +
        "vec3 ac_output(vec3 c) {\n" +
        "\treturn ac_byte_space ? c : ac_srgb_to_linear(c);\n" +
        "}\n";

    /// <summary>
    /// The day fraction Lifestone uses for its default lighting: 0.5, the start of Midsong, retail's midday time of day
    /// (<c>DerethCalendarTests</c>: the next event after day start + 210 s is Midsong at 0.5 day).
    /// </summary>
    public const float NoonDayFraction = 0.5f;

    /// <summary>The last state applied (for the HUD and screenshots' captions).</summary>
    public static SkyState? Current { get; private set; }

    /// <summary>Sets the uniforms from a sky state (main thread).</summary>
    public static void Apply(SkyState sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        var lighting = sky.Lighting;
        Set(SunDirection, AcToGodot.Direction(lighting.SunDirection));
        Set(SunColor, ToVector(lighting.SunDiffuse));
        Set(Ambient, ToVector(lighting.ObjectAmbient));
        Set(LandAmbient, ToVector(lighting.LandscapeAmbient));
        Set(FogColor, ToVector(sky.Fog.Color.ToVector3()));
        Set(FogStart, sky.Fog.Start);
        Set(FogEnd, sky.Fog.End);
        Set(FogEnabled, sky.Fog.Enabled);
        Current = sky;
    }

    /// <summary>Applies retail noon lighting for day group 0 (a deterministic default until the sky track drives it).</summary>
    public static SkyState ApplyDefaults(DerethSky sky, float dayFraction = NoonDayFraction, float maxMipLevel = 3f)
    {
        ArgumentNullException.ThrowIfNull(sky);
        var state = sky.EvaluateAtTimeOfDay(0, dayFraction);
        Apply(state);
        Set(MaxMipLevel, maxMipLevel);
        return state;
    }

    /// <summary>The fog colour as a Godot colour for the clear/background colour (raw bytes shown as-is).</summary>
    /// <remarks>Depends on the blend space (<see cref="ByteSpaceBlending.BackgroundColor"/>): select it first.</remarks>
    public static Color FogBackground(SkyState sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        return FogBackground(sky.Fog);
    }

    /// <summary>The fog colour of <paramref name="fog"/> as the clear/background colour that shows its raw bytes.</summary>
    public static Color FogBackground(SkyFog fog)
    {
        var c = fog.Color;
        return ByteSpaceBlending.BackgroundColor(new Color(c.R / 255f, c.G / 255f, c.B / 255f));
    }

    // --- M1 sky track (scripts/World/Sky/SkyController.cs): retail refreshes light and fog on separate cadences
    // (LScape::UseTime: fog every tick_size, light every light_tick_size; docs/sky.md), so they are set separately. ---

    // They take cached StringNames: a string name converts to a new finalizable StringName on every call, and at a
    // time-lapse these run nearly every frame (docs/sky.md §7).

    /// <summary>Sets the light uniforms (sun direction and colour, object and landscape ambient). Main thread; allocates nothing.</summary>
    public static void ApplyLighting(SkyLighting lighting)
    {
        Set(SkyNames.SunDirection, AcToGodot.Direction(lighting.SunDirection));
        Set(SkyNames.SunColor, ToVector(lighting.SunDiffuse));
        Set(SkyNames.Ambient, ToVector(lighting.ObjectAmbient));
        Set(SkyNames.LandAmbient, ToVector(lighting.LandscapeAmbient));
    }

    /// <summary>Sets the fog uniforms. Main thread; allocates nothing.</summary>
    public static void ApplyFog(SkyFog fog)
    {
        Set(SkyNames.FogColor, ToVector(fog.Color.ToVector3()));
        Set(SkyNames.FogStart, fog.Start);
        Set(SkyNames.FogEnd, fog.End);
        Set(SkyNames.FogEnabled, fog.Enabled);
    }

    private static void Set(StringName name, Variant value) => RenderingServer.GlobalShaderParameterSet(name, value);

    /// <summary>The uniform names as StringNames, created on the sky's first call (main thread).</summary>
    private static class SkyNames
    {
        public static readonly StringName SunDirection = RenderGlobals.SunDirection;
        public static readonly StringName SunColor = RenderGlobals.SunColor;
        public static readonly StringName Ambient = RenderGlobals.Ambient;
        public static readonly StringName LandAmbient = RenderGlobals.LandAmbient;
        public static readonly StringName FogColor = RenderGlobals.FogColor;
        public static readonly StringName FogStart = RenderGlobals.FogStart;
        public static readonly StringName FogEnd = RenderGlobals.FogEnd;
        public static readonly StringName FogEnabled = RenderGlobals.FogEnabled;
    }

    // --- end of the M1 sky track block ---

    private static void Set(string name, Variant value) => RenderingServer.GlobalShaderParameterSet(name, value);

    private static Vector3 ToVector(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
