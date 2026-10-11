using Lifestone.Client.Rendering;

namespace Lifestone.Client.Effects;

/// <summary>
/// What a part's own translucency (a particle's, or a Transparent hook's) does to the way its surfaces are drawn.
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail client (acclient 11.4186, by function name):</para>
/// <list type="bullet">
/// <item><c>CPhysicsPart::SetTranslucency(t)</c>: t == 1.0 hides the part (<c>draw_state |= 1</c>, so
/// <c>CPhysicsPart::Draw</c> skips it); a t equal to the GfxObj's default (0 without a GfxObj material) restores the
/// default (no part material, <c>RestoreMaterial</c>); any other t copies a <c>CMaterial</c> and calls
/// <c>CMaterial::SetTranslucencySimple(t)</c>, which sets the material's ambient/diffuse/specular/emissive alpha to
/// 1 − t, and <c>CMaterial::CheckAlphaValues</c> then flags it <c>has_alpha</c>.</item>
/// <item><c>RenderDeviceD3D::SetCurrentMaterial</c>: with a part material the D3D diffuse and ambient colour sources
/// become the material (white, alpha 1 − t) instead of the vertex colour, so the vertex alpha that
/// <c>copyVert</c> baked from the surface's own translucency is replaced by 1 − t; the stage-0 alpha is still
/// texel alpha × that alpha (<c>TEXOP_MODULATE</c>). Lighting is unchanged (white material, the surface's luminosity as
/// emissive).</item>
/// <item><c>D3DPolyRender::SetSurface</c>: with a current material that <c>has_alpha</c>, a surface that does not already
/// blend without an alpha test is switched to SRCALPHA/INVSRCALPHA with the alpha test off and no depth write; surfaces
/// that already blend without a test (Additive, Alpha, Translucent) keep their factors. ONE/ONE surfaces ignore alpha,
/// so a translucency fade does not show on them, as in retail.</item>
/// </list>
/// </remarks>
public static class PartTranslucencyRules
{
    /// <summary>True when a part with translucency <paramref name="translucency"/> is not drawn at all.</summary>
    public static bool IsHidden(float translucency) => translucency == 1f;

    /// <summary>
    /// True when the part carries its own material alpha (1 − t) instead of the surface's (any t other than the default
    /// 0 and other than the hidden 1).
    /// </summary>
    public static bool HasPartMaterial(float translucency) => translucency != 0f && translucency != 1f;

    /// <summary>The alpha the part material multiplies texel alpha by: 1 − t.</summary>
    public static float MaterialAlpha(float translucency) => 1f - translucency;

    /// <summary>The variant a surface is drawn with when its part carries a translucency material.</summary>
    public static SurfaceShaderVariant WithPartMaterial(SurfaceShaderVariant surface)
    {
        var blendsWithoutTest = surface.Blend != SurfaceBlend.Opaque && !surface.AlphaTest;
        return blendsWithoutTest
            ? surface
            : surface with { Blend = SurfaceBlend.Mix, AlphaTest = false, DepthWrite = false };
    }
}
