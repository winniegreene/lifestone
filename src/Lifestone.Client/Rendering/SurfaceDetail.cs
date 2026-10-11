namespace Lifestone.Client.Rendering;

/// <summary>
/// Retail's detail-texture pass for a draw (render-fidelity extension of the surface variants; docs/render-contract.md
/// §2.4): which of the Region's detail surfaces is current while it draws.
/// </summary>
/// <remarks>
/// <para><b>Retail</b> (acclient 11.4186, re-expressed): <c>LScape::SetDetailTexturing</c> (acclient.c:308209) turns
/// TMTerrainDesc[1] and [2]'s detail textures into <c>Render::building_detail_surface</c> and
/// <c>Render::environment_detail_surface</c> (<c>LScape::GenerateDetailSurface</c>, a custom Surface with the Detail type
/// bit 0x20000) and their <c>detail_tex_tiling</c> into <c>Render::*_detail_tiling</c>, while the "Environment Detail
/// Textures" preference is on (its default, acclient.c:45603; landscape and object detail are never generated).
/// <c>RenderDeviceD3D::DrawEnvCell</c> makes the environment surface current for a cell's own mesh and
/// <c>RenderDeviceD3D::DrawBuilding</c> the building surface for a building's shell, both with blend
/// DESTCOLOR/INVSRCALPHA, and clear it afterwards.</para>
/// <para><c>D3DPolyRender::RenderMeshSubset</c> then draws every subset a second time (single-pass detailing is off:
/// <c>trysinglepass_2</c> is never set): stage 0 samples the detail texture (WRAP, linear filtering;
/// <c>ACRender::SetDetailSurfaceInternal(0)</c>) at the second UV set, which <c>D3DPolyRender::SetDetailTiling</c> makes
/// <c>tiling x</c> the base UV (the 3.0 that <c>CEnvCell::UnPack</c> and the 1.0 that GfxObjs pass to
/// <c>ConstructMesh</c> are only the initial factor, replaced at the first draw), MODULATEs it with the lit vertex colour,
/// and blends <c>dst x (src.rgb + 1 - src.a)</c> with depth LESSEQUAL; fog, when on for the surface, applies to that
/// second pass as well. The detail texture's colour and alpha are close (rgb mean 42, alpha mean 34 of 255), so the
/// factor is 1.03 ± 0.10 at full light: grain, not a tint.</para>
/// <para>Distance fades (<c>ACRender::get_alpha_for_z</c>, 10-50 m) belong to retail's software polygon path
/// (<c>D3DPolyRender::DrawPolyInternal</c>, <c>ACRender::landPolyDraw</c>), not to the mesh path that draws built
/// cells and GfxObjs, so they do not apply here.</para>
/// </remarks>
public enum SurfaceDetail
{
    /// <summary>No detail pass.</summary>
    None,

    /// <summary>A landblock building's shell (<c>Render::building_detail_surface</c>, TMTerrainDesc[1]).</summary>
    Building,

    /// <summary>An EnvCell's own mesh (<c>Render::environment_detail_surface</c>, TMTerrainDesc[2]).</summary>
    Environment,
}
