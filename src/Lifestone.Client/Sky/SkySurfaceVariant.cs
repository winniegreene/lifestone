using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Sky;

/// <summary>
/// The shader variant of one sky object surface: the surface's own retail blend (<see cref="SurfaceShaderVariant"/>),
/// possibly overridden by the part material's alpha, drawn in sky mode.
/// </summary>
/// <remarks>
/// <para>Sky mode (<c>GameSky::Draw</c>, acclient.c:308475, 11.4186) differs from the world's surfaces:</para>
/// <list type="bullet">
/// <item>Depth test ALWAYS with depth writes off for the whole draw (<c>SetDepthBufferMode(DEPTHTEST_ALWAYS, 0)</c>;
/// <c>D3DPolyRender::SetSurface</c> leaves the depth mode alone while the sky flag is set, acclient.c:454549).</item>
/// <item>Fog off unless the server fog override is on (<c>SetFFFogEnable(m_override_enabled != 0)</c>).</item>
/// <item>Nothing is deferred to the alpha list (<c>D3DPolyRender::DrawMesh</c> skips <c>AddMeshToAlphaList</c> in sky
/// mode, acclient.c:455183): every subset draws at once, in object, part and subset order.</item>
/// <item>Fixed-function lighting is forced on (<c>RenderMeshSubset</c>, acclient.c:454684).</item>
/// <item><c>zfar</c> is multiplied by 4 (16,000 m with retail's 4,000 m <c>Render::zfar</c>, acclient.c:45527).</item>
/// </list>
/// <para>Engines that cannot draw "first, depth ALWAYS" emulate it: before-pass objects are drawn at the far plane
/// behind every world surface, after-pass objects with the depth test off, both before the world's translucent
/// surfaces (docs/sky.md).</para>
/// </remarks>
/// <param name="Blend">Blend after the material override.</param>
/// <param name="AlphaTest">The surface's alpha test (clip maps, reference 200).</param>
/// <param name="Addressing">Sampler addressing.</param>
/// <param name="Textured">Textured or solid colour.</param>
/// <param name="Pass">Before or after the landscape.</param>
public readonly record struct SkySurfaceVariant(
    SurfaceBlend Blend,
    bool AlphaTest,
    TextureAddressMode Addressing,
    bool Textured,
    SkyPass Pass)
{
    /// <summary>Retail <c>Render::zfar</c> (acclient.c:45527) times the sky's factor of 4 (<c>GameSky::Draw</c>).</summary>
    public const float FarClip = 4000f * 4f;

    /// <summary>A short stable name for shader caching and logs.</summary>
    public string Name =>
        $"sky.{Pass.ToString().ToLowerInvariant()}.{Blend.ToString().ToLowerInvariant()}{(AlphaTest ? "+test" : string.Empty)}." +
        $"{(Addressing == TextureAddressMode.Wrap ? "wrap" : "clamp")}.{(Textured ? "tex" : "solid")}";

    /// <summary>The variant for a surface drawn by a sky object part.</summary>
    /// <param name="surface">The Surface.</param>
    /// <param name="addressing">The sub-mesh addressing.</param>
    /// <param name="material">The part's lighting state (an alpha below 1 can force SRCALPHA/INVSRCALPHA).</param>
    /// <param name="pass">The pass.</param>
    public static SkySurfaceVariant For(SurfaceMaterial surface, TextureAddressMode addressing, SkyPartMaterial material, SkyPass pass)
    {
        ArgumentNullException.ThrowIfNull(surface);

        // Sky surfaces always take their exact retail blend: the opaque cut-out shortcut (render-contract.md §2) only
        // saves sorting, and sky surfaces are drawn in a fixed order anyway.
        var variant = SurfaceShaderVariant.For(surface, passingTexelsBlend: true, addressing);

        // D3DPolyRender::SetSurface (acclient.c:454531): a part material with alpha switches to SRCALPHA/INVSRCALPHA
        // and turns the alpha test off (singlePassDetailing = 0 feeds SetAlphaTestEnable) unless the subset already
        // blends without an alpha test.
        return material.ForcesMixBlend(variant)
            ? new SkySurfaceVariant(SurfaceBlend.Mix, false, addressing, variant.Textured, pass)
            : new SkySurfaceVariant(variant.Blend, variant.AlphaTest, addressing, variant.Textured, pass);
    }

    /// <summary>
    /// Every variant the given surface uses can take: without a part material and with a translucent one (the only
    /// part state that changes the variant), for compiling shaders ahead of use. Surfaces no variant expresses are
    /// skipped (they draw with the error material).
    /// </summary>
    public static IReadOnlySet<SkySurfaceVariant> Reachable(IEnumerable<(SurfaceMaterial Surface, TextureAddressMode Addressing, SkyPass Pass)> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        var translucent = SkyPartMaterial.Default.WithTranslucency(0.5f);
        var variants = new HashSet<SkySurfaceVariant>();
        foreach (var (surface, addressing, pass) in uses)
        {
            foreach (var material in (ReadOnlySpan<SkyPartMaterial>)[SkyPartMaterial.Default, translucent])
            {
                try
                {
                    variants.Add(For(surface, addressing, material, pass));
                }
                catch (NotSupportedException)
                {
                    // Drawn with the error material; nothing to compile.
                }
            }
        }

        return variants;
    }
}
