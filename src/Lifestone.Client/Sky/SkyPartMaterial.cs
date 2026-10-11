using Lifestone.Client.Rendering;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Sky;

/// <summary>
/// The per-object lighting state retail keeps on a sky object's parts: what <c>CPhysicsObj::SetLuminosity</c>,
/// <c>SetDiffusion</c> and <c>SetTranslucency</c> leave behind (<c>CPhysicsPart::curLuminosity</c>, <c>curDiffuse</c>,
/// <c>curTranslucency</c>, the hidden bit of <c>draw_state</c>).
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail decompile (Mag-nus/acclient-AI-RE 11.4186): <c>CPhysicsPart::SetLuminosity</c>
/// (acclient.c:315454), <c>SetDiffusion</c> (315420), <c>SetTranslucency</c> (315488), <c>CurSettingsAreDefault</c>
/// (314711), <c>CopyMaterial</c> (314472), <c>CMaterial::SetLuminositySimple/SetDiffuseSimple/SetTranslucencySimple</c>
/// (360594-360633), <c>CMaterial::CMaterial</c> (360635). <c>CGfxObj::CGfxObj</c> (356358) leaves the GfxObj's own
/// material null and nothing else sets it, so a fresh part starts at luminosity 0, diffuse 1, translucency 0.</para>
/// <list type="bullet">
/// <item>While all three are at their defaults the part has <b>no material</b>: D3D takes ambient and diffuse colour
/// from the vertex (white, alpha = the Surface's (1 - translucency), <c>copyVert</c>) and the emissive from the default
/// material (0).</item>
/// <item>Otherwise the part has a <c>CMaterial</c> (<c>RenderDeviceD3D::SetCurrentMaterial</c>, acclient.c:457648, then
/// sets the diffuse and ambient colour sources to the material): ambient (1, 1, 1), diffuse (d, d, d), emissive
/// (l, l, l), every alpha 1 - t. D3D's lit alpha is the diffuse alpha, so the vertex alpha no longer applies.</item>
/// <item><c>SetTranslucency(1.0)</c> only hides the part (<c>draw_state |= 1</c>, skipped by <c>CPhysicsPart::Draw</c>);
/// it does not touch <c>curTranslucency</c> or the material.</item>
/// <item>A Surface whose own luminosity is &gt; 0 overrides the emissive while it draws
/// (<c>D3DPolyRender::RenderMeshSubset</c>, acclient.c:454688): <see cref="Emissive"/>.</item>
/// <item>A material with an alpha below 1 (<c>CMaterial::has_alpha</c>) turns blending on with SRCALPHA/INVSRCALPHA, the
/// alpha test off, for surfaces that do not already blend without an alpha test (<c>D3DPolyRender::SetSurface</c>,
/// acclient.c:454531): <see cref="ForcesMixBlend"/>.</item>
/// </list>
/// </remarks>
/// <param name="Luminosity"><c>curLuminosity</c> (material emissive).</param>
/// <param name="Diffusion"><c>curDiffuse</c> (material diffuse RGB).</param>
/// <param name="Translucency"><c>curTranslucency</c> (material alpha = 1 - t).</param>
/// <param name="Hidden">The part was given translucency 1.0 and is not drawn.</param>
public readonly record struct SkyPartMaterial(float Luminosity, float Diffusion, float Translucency, bool Hidden)
{
    /// <summary>A fresh part: no material, drawn.</summary>
    public static SkyPartMaterial Default => new(0f, 1f, 0f, false);

    /// <summary><c>!CPhysicsPart::CurSettingsAreDefault</c>: the part draws with its own <c>CMaterial</c>.</summary>
    public bool HasMaterial => !(Translucency == 0f && Diffusion == 1f && Luminosity == 0f);

    /// <summary><c>CMaterial::has_alpha</c> (every alpha is 1 - t, so: t &gt; 0).</summary>
    public bool HasAlpha => HasMaterial && 1f - Translucency < 1f;

    /// <summary><c>CPhysicsPart::SetLuminosity</c>: a changed value is stored (and copied into the material).</summary>
    public SkyPartMaterial WithLuminosity(float luminosity) => this with { Luminosity = luminosity };

    /// <summary><c>CPhysicsPart::SetDiffusion</c>.</summary>
    public SkyPartMaterial WithDiffusion(float diffusion) => this with { Diffusion = diffusion };

    /// <summary>
    /// <c>CPhysicsObj::SetTranslucency(t, 0)</c> (acclient.c:318694; t is raised to <c>translucencyOriginal</c>, 0 for sky
    /// objects) then <c>CPhysicsPart::SetTranslucency</c>: exactly 1.0 hides the part and changes nothing else; any
    /// other value shows it and becomes <c>curTranslucency</c>.
    /// </summary>
    public SkyPartMaterial WithTranslucency(float translucency)
    {
        var t = translucency < 0f ? 0f : translucency;
        return t == 1.0f ? this with { Hidden = true } : this with { Hidden = false, Translucency = t };
    }

    /// <summary>D3D emissive while drawing <paramref name="surface"/>: its own luminosity when &gt; 0, else the
    /// material's, else 0.</summary>
    public float Emissive(SurfaceMaterial surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Luminosity > 0f ? surface.Luminosity : HasMaterial ? Luminosity : 0f;
    }

    /// <summary>The diffuse factor on the sun term: the material's diffuse, or 1 (white vertex colour).</summary>
    public float DiffuseFactor => HasMaterial ? Diffusion : 1f;

    /// <summary>The lit alpha before the texture: the material's 1 - t, or the Surface's vertex alpha (bytes / 255).</summary>
    public float VertexAlpha(SurfaceMaterial surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return HasMaterial ? 1f - Translucency : surface.RenderState.MaterialAlpha / 255f;
    }

    /// <summary>
    /// True when the material's alpha forces SRCALPHA/INVSRCALPHA blending: it has alpha and the surface does not
    /// already blend without an alpha test.
    /// </summary>
    public bool ForcesMixBlend(SurfaceShaderVariant surfaceVariant) =>
        HasAlpha && !(surfaceVariant.Blend != SurfaceBlend.Opaque && !surfaceVariant.AlphaTest);
}
