using Lifestone.Data.Geometry;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Rendering;

/// <summary>How a surface shader variant blends (docs/render-contract.md §2).</summary>
/// <remarks>Names describe retail's D3D factors; the Godot render mode each maps to is in the contract.</remarks>
public enum SurfaceBlend
{
    /// <summary>ONE/ZERO, blending off: drawn in the opaque pass (an alpha test, if any, discards).</summary>
    Opaque,

    /// <summary>SRCALPHA/INVSRCALPHA ("mix").</summary>
    Mix,

    /// <summary>ONE/INVSRCALPHA (premultiplied-style "over").</summary>
    Premultiplied,

    /// <summary>ONE/ONE.</summary>
    AddOne,

    /// <summary>SRCALPHA/ONE.</summary>
    AddSourceAlpha,

    /// <summary>INVSRCALPHA/ONE. No end-of-retail surface uses it (InvAlpha is unused).</summary>
    AddInverseSourceAlpha,
}

/// <summary>Face culling of a variant. MeshData consumers always use <see cref="Back"/> (builders emit back faces).</summary>
public enum SurfaceCull
{
    Back,
    None,
}

/// <summary>
/// One surface shader variant: everything an engine can only fix when it compiles the shader (blend and depth state,
/// culling, sampler addressing, textured or solid, fog). Per-surface values (texture, colour, material alpha,
/// luminosity) are material parameters.
/// </summary>
/// <param name="Blend">Blend mode.</param>
/// <param name="AlphaTest">Discard texels whose alpha is below the reference (retail GREATEREQUAL 200).</param>
/// <param name="DepthWrite">Write depth (retail: alpha test on, or blending off).</param>
/// <param name="Cull">Face culling.</param>
/// <param name="Addressing">Sampler addressing (WRAP = repeat, CLAMP = clamp to edge).</param>
/// <param name="Textured">Samples the texture; otherwise the solid colour (retail's 1x1 solid texture).</param>
/// <param name="Fog">Apply retail linear fog (off for Additive surfaces).</param>
/// <param name="Lighting">Retail lighting rule (interiors extension, <see cref="SurfaceLighting"/>); <see cref="For"/>
/// always gives <see cref="SurfaceLighting.Object"/>, callers that draw cells set it with <c>with</c>.</param>
/// <param name="Detail">Retail's detail-texture pass (render-fidelity extension, <see cref="SurfaceDetail"/>); set only
/// through <see cref="WithDetail"/>, which applies retail's rules.</param>
public readonly record struct SurfaceShaderVariant(
    SurfaceBlend Blend,
    bool AlphaTest,
    bool DepthWrite,
    SurfaceCull Cull,
    TextureAddressMode Addressing,
    bool Textured,
    bool Fog,
    SurfaceLighting Lighting = SurfaceLighting.Object,
    SurfaceDetail Detail = SurfaceDetail.None)
{
    /// <summary>
    /// A short stable name, e.g. <c>opaque+test.z.back.wrap.tex.fog</c> (non-default lighting appends <c>.envcell</c> /
    /// <c>.cellobject</c>, a detail pass <c>.detail-building</c> / <c>.detail-environment</c>).
    /// </summary>
    public string Name =>
        $"{Blend.ToString().ToLowerInvariant()}{(AlphaTest ? "+test" : string.Empty)}.{(DepthWrite ? "z" : "noz")}." +
        $"{Cull.ToString().ToLowerInvariant()}.{(Addressing == TextureAddressMode.Wrap ? "wrap" : "clamp")}." +
        $"{(Textured ? "tex" : "solid")}.{(Fog ? "fog" : "nofog")}" +
        (Lighting == SurfaceLighting.Object ? string.Empty : "." + Lighting.ToString().ToLowerInvariant()) +
        (Detail == SurfaceDetail.None ? string.Empty : ".detail-" + Detail.ToString().ToLowerInvariant());

    /// <summary>
    /// Whether retail's detail pass shows on this variant (<see cref="SurfaceDetail"/>): a textured, opaque surface
    /// without an alpha test, drawn with the object or EnvCell lighting rule.
    /// </summary>
    /// <remarks>
    /// <para>The pass draws the subset again with blend DESTCOLOR/INVSRCALPHA (<c>D3DPolyRender::RenderMeshSubset</c>,
    /// <c>ACRender::SetDetailSurfaceInternal(0)</c>). Under an alpha test (clip maps, the cut-out shortcut) it never
    /// passes: the detail texture's alpha is 24-46 of 255, below the reference 200. Untextured subsets are not drawn in
    /// buildings and cells at all. Objects inside cells are drawn after <c>RenderDeviceD3D::DrawEnvCell</c> has cleared
    /// the detail surface again.</para>
    /// <para>Blended subsets would need the pass to scale what shows through them too; the dats have no textured blended
    /// building surface and one textured blended cell surface (12 cells), so they are left out: a documented
    /// deviation (docs/render-contract.md §2.4).</para>
    /// </remarks>
    public bool CanCarryDetail =>
        Blend == SurfaceBlend.Opaque && !AlphaTest && Textured && Lighting is SurfaceLighting.Object or SurfaceLighting.EnvCell;

    /// <summary>
    /// This variant with retail's detail pass for <paramref name="detail"/> when it applies (<see cref="CanCarryDetail"/>,
    /// EnvCell lighting draws environment detail and object lighting building detail), else with none.
    /// </summary>
    public SurfaceShaderVariant WithDetail(SurfaceDetail detail)
    {
        var applies = detail switch
        {
            SurfaceDetail.Environment => Lighting == SurfaceLighting.EnvCell,
            SurfaceDetail.Building => Lighting == SurfaceLighting.Object,
            _ => false,
        };
        return this with { Detail = applies && CanCarryDetail ? detail : SurfaceDetail.None };
    }

    /// <summary>
    /// The variant retail's fixed-function state for <paramref name="material"/> maps to (docs/textures.md §5,
    /// <c>D3DPolyRender::SetSurface</c> as re-expressed by <see cref="SurfaceMaterial.DeriveRenderState"/>).
    /// </summary>
    /// <param name="material">The surface.</param>
    /// <param name="passingTexelsBlend">
    /// For alpha-tested surfaces: whether the texture has texels with alpha in [reference, 254], i.e. texels that pass
    /// the test and are then blended (<see cref="DecodedTexture.AnyAlphaInRange"/>). When false, and the material alpha
    /// is 255, a <see cref="SurfaceBlendMode.AlphaTestBlend"/> surface draws exactly as an opaque cut-out (only alpha
    /// 0 or 255 texels reach the blender; docs/textures.md §7) and gets the cheaper opaque variant with the alpha test.
    /// Additive alpha-tested surfaces always blend. Ignored for surfaces without an alpha test.
    /// </param>
    /// <param name="addressing">The sub-mesh's retail texture addressing (<see cref="SubMesh.Addressing"/>).</param>
    /// <param name="cull">Face culling.</param>
    /// <exception cref="NotSupportedException">INVSRCALPHA/SRCALPHA, which no common engine blend mode expresses and no
    /// end-of-retail surface uses (InvAlpha is unused).</exception>
    public static SurfaceShaderVariant For(SurfaceMaterial material, bool passingTexelsBlend, TextureAddressMode addressing, SurfaceCull cull = SurfaceCull.Back)
    {
        ArgumentNullException.ThrowIfNull(material);
        var rs = material.RenderState;
        var blend = !rs.AlphaBlendEnabled
            ? SurfaceBlend.Opaque
            : (rs.SourceBlend, rs.DestinationBlend) switch
            {
                (BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha) => SurfaceBlend.Mix,
                (BlendFactor.One, BlendFactor.InverseSourceAlpha) => SurfaceBlend.Premultiplied,
                (BlendFactor.One, BlendFactor.One) => SurfaceBlend.AddOne,
                (BlendFactor.SourceAlpha, BlendFactor.One) => SurfaceBlend.AddSourceAlpha,
                (BlendFactor.InverseSourceAlpha, BlendFactor.One) => SurfaceBlend.AddInverseSourceAlpha,
                var pair => throw new NotSupportedException(
                    $"TODO(render): surface 0x{material.Id:X8} blends {pair.SourceBlend}/{pair.DestinationBlend}; no variant " +
                    "expresses it (no end-of-retail surface should; InvAlpha is unused)."),
            };

        if (rs.AlphaTestEnabled && blend is SurfaceBlend.Mix or SurfaceBlend.Premultiplied && !passingTexelsBlend && rs.MaterialAlpha == 255)
        {
            blend = SurfaceBlend.Opaque;
        }

        return new SurfaceShaderVariant(
            blend,
            rs.AlphaTestEnabled,
            rs.DepthWrite,
            cull,
            addressing,
            material.IsTextured,
            rs.FogEnabled);
    }

    /// <summary>
    /// Every variant <paramref name="materials"/> can map to on the mesh path (both addressing modes, with and without
    /// the cut-out shortcut unless <paramref name="exactCutouts"/>), for compiling shaders ahead of use. Surfaces no
    /// variant expresses are skipped (they draw with the error material).
    /// </summary>
    public static IReadOnlySet<SurfaceShaderVariant> Reachable(IEnumerable<SurfaceMaterial> materials, bool exactCutouts = false, SurfaceCull cull = SurfaceCull.Back)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var variants = new HashSet<SurfaceShaderVariant>();
        foreach (var material in materials)
        {
            foreach (var addressing in (ReadOnlySpan<TextureAddressMode>)[TextureAddressMode.Wrap, TextureAddressMode.Clamp])
            {
                foreach (var passing in exactCutouts ? (ReadOnlySpan<bool>)[true] : [false, true])
                {
                    try
                    {
                        variants.Add(For(material, passing, addressing, cull));
                    }
                    catch (NotSupportedException)
                    {
                        // Drawn with the error material; nothing to compile.
                    }
                }
            }
        }

        return variants;
    }
}
