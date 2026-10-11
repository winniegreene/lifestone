using System.Collections.Concurrent;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using DatSurface = DatReaderWriter.DBObjs.Surface;

namespace Lifestone.Client.Effects;

/// <summary>
/// <see cref="IParticleModelSource"/> over the dats: a particle GfxObj's LOD chain (<see cref="LodChain.Load"/>), the
/// sort centre of its level-0 GfxObj, and whether a part material switches any of its surfaces' blend state. Thread-safe,
/// cached per GfxObj; a GfxObj that retail could not load (<see cref="LodChain.IsLoadable"/>) or that has no mesh yields
/// null, and its particles are not drawn.
/// </summary>
public sealed class DatParticleModels(DatSet dats, GfxObjMeshCache meshes) : IParticleModelSource
{
    private readonly ConcurrentDictionary<uint, ParticleModel?> _models = new();

    /// <summary>Called once for each GfxObj that cannot be drawn (for logging).</summary>
    public Action<uint, string>? Missing { get; set; }

    public ParticleModel? Get(uint gfxObjId) => _models.GetOrAdd(gfxObjId, id =>
    {
        var chain = LodChain.Load(dats, id);
        if (!chain.IsLoadable)
        {
            Missing?.Invoke(id, "retail CPhysicsPart::SetPart would refuse it (missing GfxObj or level 0)");
            return null;
        }

        if (meshes.Get(chain.NearestGfxObjId) is not { } nearest)
        {
            Missing?.Invoke(id, $"level-0 GfxObj 0x{chain.NearestGfxObjId:X8} has no mesh");
            return null;
        }

        // D3DPolyRender::SetSurface keeps the factors of a surface that blends without an alpha test; any other surface
        // switches to SRCALPHA/INVSRCALPHA under a part material (PartTranslucencyRules). A missing Surface counts as
        // switching (it draws with the error material either way).
        var switches = chain.GfxObjIds.Where(g => g != 0).Distinct()
            .SelectMany(g => meshes.Get(g)?.Mesh.SubMeshes ?? [])
            .Any(s => !dats.TryGetPortal<DatSurface>(s.SurfaceId, out var surface)
                      || SurfaceMaterial.FromDat(s.SurfaceId, surface).RenderState is var rs && !(rs.AlphaBlendEnabled && !rs.AlphaTestEnabled));
        return new ParticleModel(id, chain, nearest.SortCenter, switches);
    });
}
