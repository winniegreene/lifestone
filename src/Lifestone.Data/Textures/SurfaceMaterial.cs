using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;

namespace Lifestone.Data.Textures;

/// <summary>D3D blend factors, numbered as retail's <c>BlendMode</c> enum (acclient.h; equal to <c>D3DBLEND</c>).</summary>
public enum BlendFactor : byte
{
    /// <summary>BLEND_ZERO.</summary>
    Zero = 1,

    /// <summary>BLEND_ONE.</summary>
    One = 2,

    /// <summary>BLEND_SRCALPHA.</summary>
    SourceAlpha = 5,

    /// <summary>BLEND_INVSRCALPHA.</summary>
    InverseSourceAlpha = 6,
}

/// <summary>
/// Classification of <see cref="SurfaceRenderState"/> that keeps every distinction retail draws: alpha test or not,
/// and whether the destination factor is ONE. The exact factors in the state stay authoritative.
/// </summary>
/// <remarks>
/// Retail never alpha-tests without blending: <c>D3DPolyRender::SetSurface</c> enables blending on every Base1ClipMap
/// surface. So both alpha-test modes still blend the texels that pass the test. Never map
/// <see cref="AlphaTestAdditive"/> to an opaque alpha-scissor material, and map <see cref="AlphaTestBlend"/> to one
/// only when the texture's passing texels are all fully opaque (see <see cref="AlphaTestBlend"/>).
/// </remarks>
public enum SurfaceBlendMode
{
    /// <summary>No blending, no alpha test, depth write on.</summary>
    Opaque,

    /// <summary>
    /// Alpha test (GREATEREQUAL <see cref="SurfaceRenderState.AlphaTestReference"/>) with depth write on, then
    /// blending with a non-ONE destination factor: ONE/INVSRCALPHA (plain Base1ClipMap, premultiplied-style "over") or
    /// SRCALPHA/INVSRCALPHA (ClipMap|Alpha). It equals a pure cut-out only for textures whose texels are all alpha 0
    /// or 255 (every palette-expanded INDEX16/P8 clip map). Texels with alpha in [reference, 254] pass the test and are
    /// then blended: <see cref="DecodedTexture.AnyAlphaInRange"/> with (reference, 254) tells which textures have
    /// them. On the end-of-retail dats, 90 non-paletted textures on plain ClipMap surfaces do.
    /// </summary>
    AlphaTestBlend,

    /// <summary>
    /// Alpha test with depth write on, blended additively: destination factor ONE (ONE/ONE for ClipMap|Additive,
    /// SRCALPHA/ONE for ClipMap|Alpha|Additive). Fog is off (<see cref="SurfaceRenderState.FogEnabled"/>).
    /// </summary>
    AlphaTestAdditive,

    /// <summary>Alpha blending with a non-ONE destination factor, no alpha test, depth write off.</summary>
    AlphaBlend,

    /// <summary>Destination factor ONE (ONE/ONE, SRCALPHA/ONE or INVSRCALPHA/ONE), no alpha test, depth write off.</summary>
    Additive,
}

/// <summary>Where the alpha of a drawn texel comes from.</summary>
public enum SurfaceAlphaSource
{
    /// <summary>
    /// Stage 0 alpha = texture alpha x diffuse alpha (<c>TEXOP_MODULATE</c>); the diffuse alpha is
    /// <see cref="SurfaceRenderState.MaterialAlpha"/>.
    /// </summary>
    TextureTimesMaterial,

    /// <summary>
    /// Untextured surface: retail binds a 1x1 solid-colour texture (<see cref="SurfaceMaterial.SolidColorTexel"/>, alpha
    /// = (1 - translucency) x 255) and modulates it with <see cref="SurfaceRenderState.MaterialAlpha"/>.
    /// </summary>
    SolidColorTimesMaterial,
}

/// <summary>Fixed-function render state retail sets for a surface.</summary>
/// <param name="SourceBlend">D3DRS_SRCBLEND.</param>
/// <param name="DestinationBlend">D3DRS_DESTBLEND (blend op is always ADD).</param>
/// <param name="AlphaBlendEnabled">D3DRS_ALPHABLENDENABLE.</param>
/// <param name="AlphaTestEnabled">D3DRS_ALPHATESTENABLE (function GREATEREQUAL).</param>
/// <param name="AlphaTestReference">D3DRS_ALPHAREF (0-255) used while the alpha test is on.</param>
/// <param name="DepthWrite">D3DRS_ZWRITEENABLE (depth test LESS stays on).</param>
/// <param name="MaterialAlpha">Per-vertex diffuse alpha, 0-255 (<c>copyVert</c>).</param>
/// <param name="FogEnabled">False for Additive surfaces (retail turns D3DRS_FOGENABLE off for them).</param>
/// <param name="AlphaSource">How the texel alpha is formed.</param>
public readonly record struct SurfaceRenderState(
    BlendFactor SourceBlend,
    BlendFactor DestinationBlend,
    bool AlphaBlendEnabled,
    bool AlphaTestEnabled,
    byte AlphaTestReference,
    bool DepthWrite,
    byte MaterialAlpha,
    bool FogEnabled,
    SurfaceAlphaSource AlphaSource)
{
    /// <summary>
    /// Classification: alpha test (then additive or not), else no blending (opaque), else additive (destination
    /// ONE), else alpha blend. Alpha-tested states always blend as well (see <see cref="SurfaceBlendMode"/>).
    /// </summary>
    public SurfaceBlendMode BlendMode =>
        AlphaTestEnabled
            ? (AlphaBlendEnabled && DestinationBlend == BlendFactor.One ? SurfaceBlendMode.AlphaTestAdditive : SurfaceBlendMode.AlphaTestBlend)
        : !AlphaBlendEnabled ? SurfaceBlendMode.Opaque
        : DestinationBlend == BlendFactor.One ? SurfaceBlendMode.Additive
        : SurfaceBlendMode.AlphaBlend;

    /// <summary>
    /// Retail draws alpha-tested and blended subsets from deferred lists (<c>D3DPolyRender::AddMeshToAlphaList</c>,
    /// <c>s_AlphaDelayMask</c> = Alpha/InvAlpha/Additive, Translucent, ClipMap), unsorted, after the opaque ones.
    /// </summary>
    public bool IsDeferred => AlphaBlendEnabled || AlphaTestEnabled;
}

/// <summary>A Surface (0x08) record plus the render state retail derives from it.</summary>
/// <remarks>
/// <para><b>Flags.</b> <see cref="Type"/> uses DatReaderWriter's <see cref="SurfaceType"/>, whose values equal retail's
/// <c>SurfaceType</c> enum (acclient.h): Base1Solid 0x1, Base1Image 0x2, Base1ClipMap 0x4, Translucent 0x10,
/// Diffuse 0x20, Luminous 0x40, Alpha 0x100, InvAlpha 0x200, Additive 0x10000, Detail 0x20000, Gouraud 0x10000000,
/// Stippled 0x40000000, Perspective 0x80000000. A record carries a SurfaceTexture id and palette id when
/// <c>type &amp; 6</c>, else a solid ARGB colour (<c>CSurface::Serialize</c>).</para>
/// <para><b>Load-time fix-ups</b> (<c>CSurface::InitEnd</c>): translucency &gt; 0.0002 forces the Translucent flag
/// (<see cref="EffectiveType"/>); the palette used for INDEX16/P8 is the RenderSurface's default palette, and the
/// stored <see cref="OriginalPaletteId"/> is replaced by it (every textured end-of-retail surface stores 0 there).</para>
/// <para><b>Render state</b> (<c>D3DPolyRender::SetSurface</c>), see <see cref="RenderState"/>:</para>
/// <list type="bullet">
/// <item>Alpha: SRCALPHA / (Additive ? ONE : INVSRCALPHA), blended. Else InvAlpha: INVSRCALPHA / (Additive ? ONE :
/// SRCALPHA), blended. Else Additive: ONE/ONE, blended. Else ONE/ZERO, not blended.</item>
/// <item>Base1ClipMap: alpha test on, GREATEREQUAL, reference <see cref="ClipMapAlphaTestReference"/>; if nothing
/// above enabled blending, blend ONE/INVSRCALPHA; blending stays on either way, and Alpha/Additive factors set above
/// are kept. For palette-expanded (INDEX16/P8) clip maps every texel is alpha 0 or 255, so the result equals plain
/// alpha testing. Non-paletted clip maps (DXT3/DXT5/A8R8G8B8/A4R4G4B4) can have texels with alpha in [200, 254]:
/// they pass the test and are then blended (src x 1 + dst x (1 - a) for ONE/INVSRCALPHA). See
/// <see cref="SurfaceBlendMode.AlphaTestBlend"/> and <see cref="SurfaceBlendMode.AlphaTestAdditive"/>.</item>
/// <item>Translucent: if not yet blending, or alpha-testing, switch to SRCALPHA/INVSRCALPHA with the alpha test off;
/// material alpha = (1 - translucency) x 255, truncated. Otherwise material alpha 255.</item>
/// <item>Depth write = alpha test on, or blending off. Fog off for Additive.</item>
/// </list>
/// <para><b>Not surface properties</b> (handled by the model/cell builders): culling and two-sidedness come from the
/// polygon (<c>SidesType</c>; retail always culls CW, <c>D3DPolyRender::RenderMeshSubset</c>, and draws SidesType 1
/// polygons with culling off). Texture addressing (WRAP or CLAMP) comes from the polygons' stippling byte, by two
/// different rules (docs/textures.md §5): GfxObj and EnvCell meshes (<c>D3DPolyRender::ConstructMesh</c> then
/// <c>D3DPolyRender::RenderMeshSubset</c>) use WRAP for a whole surface subset when any polygon whose
/// <i>pos</i> surface is that surface has a non-zero stippling byte, else CLAMP; the per-polygon path
/// (<c>D3DPolyRender::SetSurface(CPolygon*, ...)</c>, used by <c>ACRender::landPolyDraw</c> and the deferred poly
/// list) uses WRAP when the side's bit is set (pos 0x1, neg 0x2). A part-level translucency (<c>CMaterial</c>
/// alpha, from PhysicsDesc) forces SRCALPHA/INVSRCALPHA, no alpha test and no depth write unless the subset is
/// already blended without alpha test. Lighting: emissive = <see cref="Luminosity"/> (grey), diffuse =
/// sunlight colour x <see cref="Diffuse"/>.</para>
/// </remarks>
public sealed class SurfaceMaterial
{
    /// <summary>
    /// Retail <c>CSurface::InitEnd</c>: a translucency above this sets the Translucent flag. The constant is the float
    /// <c>F_EPSILON</c> = 0.0002f (ACE <c>PhysicsGlobals.EPSILON</c>). The decompile prints it as "0.00019999999",
    /// exactly as it prints the declared <c>const float F_EPSILON_37</c>: floats get 8 significant digits there
    /// (<c>CAMERA_MOUSELOOK_LIMIT = 0.80000001</c>), doubles 16 (<c>0.0174532925199433</c>). So the comparison is
    /// float against float, and a translucency of exactly 0.0002f does <b>not</b> set the flag.
    /// </summary>
    public const float TranslucencyEpsilon = 0.0002f;

    /// <summary>
    /// Alpha reference for clip maps: <c>D3DPolyRender::s_ddsAlphaTestRef</c> = 200. Retail uses
    /// <c>s_256AlphaTestRef</c> = 100 only when the bound texture still carries a palette (<c>ImgTex::m_pPalette</c>);
    /// palette-expanded textures from <c>ImgTex::CreateCombinedTexture</c> never do (only
    /// <c>ImgTex::CreateFromRenderSurface_Internal</c> sets it), so 200 applies to every textured clip map. Clip-map
    /// palette texels are alpha 0 or 255, so the distinction cannot change a palette-expanded result anyway.
    /// </summary>
    public const byte ClipMapAlphaTestReference = 200;

    /// <summary>Surface file id (0x08xxxxxx).</summary>
    public uint Id { get; init; }

    /// <summary>Flags exactly as stored in the dat.</summary>
    public SurfaceType Type { get; init; }

    /// <summary>Flags after <c>CSurface::InitEnd</c> (Translucent added when <see cref="Translucency"/> &gt; 0.0002).</summary>
    public SurfaceType EffectiveType { get; init; }

    /// <summary>SurfaceTexture (0x05) id for Base1Image/Base1ClipMap surfaces, else 0.</summary>
    public uint SurfaceTextureId { get; init; }

    /// <summary>Palette id stored in the record (0 for every textured end-of-retail surface; retail ignores it).</summary>
    public uint OriginalPaletteId { get; init; }

    /// <summary>Solid colour 0xAARRGGBB for untextured surfaces, else 0.</summary>
    public uint SolidColorArgb { get; init; }

    /// <summary>0 = opaque, 1 = invisible.</summary>
    public float Translucency { get; init; }

    /// <summary>Emissive level (D3D material emissive = luminosity in r, g and b).</summary>
    public float Luminosity { get; init; }

    /// <summary>Diffuse reflectance multiplier.</summary>
    public float Diffuse { get; init; }

    /// <summary>The fixed-function state retail sets for this surface.</summary>
    public SurfaceRenderState RenderState { get; init; }

    /// <summary>True when the surface samples a texture (Base1Image or Base1ClipMap).</summary>
    public bool IsTextured => (Type & (SurfaceType.Base1Image | SurfaceType.Base1ClipMap)) != 0;

    /// <summary>True for Base1ClipMap: INDEX16/P8 indices below 8 become transparent and the alpha test is used.</summary>
    public bool IsClipMap => (Type & SurfaceType.Base1ClipMap) != 0;

    /// <summary>
    /// The 1x1 texel retail binds for an untextured surface (<c>RenderDeviceD3D::SetSolidColorTextureColor</c>):
    /// RGB of <see cref="SolidColorArgb"/> with alpha (1 - translucency) x 255 truncated, as 0xAARRGGBB. The stored
    /// alpha byte is discarded.
    /// </summary>
    public uint SolidColorTexel => (SolidColorArgb & 0x00FFFFFFu) | ((uint)TranslucencyToAlpha(Translucency) << 24);

    /// <summary>Builds the material from a DatReaderWriter Surface record.</summary>
    public static SurfaceMaterial FromDat(uint surfaceId, Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var textured = (surface.Type & (SurfaceType.Base1Image | SurfaceType.Base1ClipMap)) != 0;
        var effective = surface.Translucency > TranslucencyEpsilon ? surface.Type | SurfaceType.Translucent : surface.Type;
        var color = surface.ColorValue;
        return new SurfaceMaterial
        {
            Id = surfaceId,
            Type = surface.Type,
            EffectiveType = effective,
            SurfaceTextureId = textured ? surface.OrigTextureId.DataId : 0,
            OriginalPaletteId = textured ? surface.OrigPaletteId.DataId : 0,
            SolidColorArgb = textured || color is null
                ? 0
                : ((uint)color.Alpha << 24) | ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue,
            Translucency = surface.Translucency,
            Luminosity = surface.Luminosity,
            Diffuse = surface.Diffuse,
            RenderState = DeriveRenderState(effective, surface.Translucency, textured),
        };
    }

    /// <summary>
    /// Re-expresses <c>D3DPolyRender::SetSurface</c> for a surface drawn with no clip-map override, no single-pass
    /// detail texture and no part-level material alpha (see the class remarks for those).
    /// </summary>
    /// <param name="effectiveType">Flags after the <c>CSurface::InitEnd</c> translucency fix-up.</param>
    /// <param name="translucency">Surface translucency.</param>
    /// <param name="textured">Whether a texture is bound (affects only <see cref="SurfaceRenderState.AlphaSource"/>).</param>
    public static SurfaceRenderState DeriveRenderState(SurfaceType effectiveType, float translucency, bool textured)
    {
        var additive = (effectiveType & SurfaceType.Additive) != 0;
        BlendFactor src, dst;
        bool blend;
        if ((effectiveType & SurfaceType.Alpha) != 0)
        {
            src = BlendFactor.SourceAlpha;
            dst = additive ? BlendFactor.One : BlendFactor.InverseSourceAlpha;
            blend = true;
        }
        else if ((effectiveType & SurfaceType.InvAlpha) != 0)
        {
            src = BlendFactor.InverseSourceAlpha;
            dst = additive ? BlendFactor.One : BlendFactor.SourceAlpha;
            blend = true;
        }
        else if (additive)
        {
            src = BlendFactor.One;
            dst = BlendFactor.One;
            blend = true;
        }
        else
        {
            src = BlendFactor.One;
            dst = BlendFactor.Zero;
            blend = false;
        }

        var alphaTest = false;
        if ((effectiveType & SurfaceType.Base1ClipMap) != 0)
        {
            if (!blend)
            {
                src = BlendFactor.One;
                dst = BlendFactor.InverseSourceAlpha;
            }

            blend = true;
            alphaTest = true;
        }

        byte materialAlpha = 255;
        if ((effectiveType & SurfaceType.Translucent) != 0)
        {
            // Retail also takes this branch when the global skipChk is set; it is never set (zero-initialised).
            if (!blend || alphaTest)
            {
                blend = true;
                src = BlendFactor.SourceAlpha;
                dst = BlendFactor.InverseSourceAlpha;
                alphaTest = false;
            }

            materialAlpha = TranslucencyToAlpha(translucency);
        }

        return new SurfaceRenderState(
            src,
            dst,
            blend,
            alphaTest,
            ClipMapAlphaTestReference,
            DepthWrite: alphaTest || !blend,
            materialAlpha,
            FogEnabled: !additive,
            textured ? SurfaceAlphaSource.TextureTimesMaterial : SurfaceAlphaSource.SolidColorTimesMaterial);
    }

    /// <summary>Retail <c>(unsigned __int64)((1.0 - translucency) * 255.0)</c>, clamped to a byte.</summary>
    public static byte TranslucencyToAlpha(float translucency)
    {
        var v = (1.0 - translucency) * 255.0;
        return v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(long)v;
    }
}
