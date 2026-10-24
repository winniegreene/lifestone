using DatReaderWriter.DBObjs;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Tests.Textures;
using Lifestone.Data.Textures;
using TextureMapChange = Lifestone.Data.Appearance.TextureMapChange;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>Exhaustive appearance sweeps over the user's dats, with pinned counts.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class AppearanceSweepTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_ClothingTable_applies_to_every_Setup_it_lists()
    {
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var setups = new Dictionary<uint, ModelAssembly?>();
        var setupIds = Dats.GetAllPortalIds<Setup>().ToHashSet();
        var tableIds = Dats.GetAllPortalIds<ClothingTable>();
        Assert.Equal(1917, tableIds.Count);

        int pairs = 0, missingSetups = 0, noPalettedPart = 0, failedBuilds = 0, failedApplies = 0, partsOutside = 0, unmatchedTextures = 0;
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tableId in tableIds)
        {
            Assert.True(Dats.TryGetPortal<ClothingTable>(tableId, out var table));
            var template = table.ClothingSubPalEffects.Keys.Order().FirstOrDefault();
            foreach (var key in table.ClothingBaseEffects.Keys)
            {
                pairs++;
                if (!setups.TryGetValue(key.DataId, out var setup))
                {
                    setups[key.DataId] = setup = setupIds.Contains(key.DataId) ? ModelAssembly.Load(Dats, key.DataId) : null;
                }

                if (setup is null)
                {
                    missingSetups++;
                    continue;
                }

                var into = new ObjDescBuilder();
                var built = ClothingObjDesc.Build(table, key.DataId, template, new ShadePackage(0.5), source, into);
                if (!built.AllApplied)
                {
                    failedBuilds++;
                    failures.UnionWith(built.Problems);
                }

                var od = into.Build();
                partsOutside += od.PartChanges.Count(p => p.PartIndex >= setup.Parts.Count) + od.TextureChanges.Count(t => t.PartIndex >= setup.Parts.Count);
                var parts = PartArrayAppearance.Create(setup, source);
                var paletteId = PaletteOf(parts);
                if (paletteId == 0 && od.SubPalettes.Count > 0)
                {
                    // No part of this Setup has a paletted surface, so no palette can reach it (and the null id fails).
                    noPalettedPart++;
                    od = ObjDesc.FromWire(0, [], [.. od.TextureChanges], [.. od.PartChanges]);
                }

                var applied = parts.DoObjDescChangesFromDefault(od.WithPaletteId(paletteId));
                if (!applied.AllApplied)
                {
                    failedApplies++;
                    failures.UnionWith(applied.Problems.Select(p => $"0x{tableId:X8} on 0x{key.DataId:X8}: {p}"));
                }

                unmatchedTextures += od.TextureChanges.Count(t => !Matches(parts, source, t));
            }
        }

        var summary = $"pairs {pairs}, missing Setups {missingSetups}, without a paletted part {noPalettedPart}, failed builds {failedBuilds}, failed applies {failedApplies}, "
            + $"part indices outside the Setup {partsOutside}, texture changes matching no surface {unmatchedTextures}";
        Assert.True(PinnedClothingSweep == summary, summary + "\n" + string.Join('\n', failures.Take(40)));

        // Every failure is a part index the table gives for a Setup with fewer parts (retail's CPartArray::SetPart and
        // SetTextureMap refuse those too); nothing else fails.
        Assert.All(failures, f => Assert.Contains("parts (CPartArray::Set", f, StringComparison.Ordinal));
    }

    [RequiresHighResDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void A_surfaces_original_palette_depends_on_the_texture_level_for_two_surfaces_only()
    {
        // TextureDetail picks level 0 (high-res) or level 1 of a two-level SurfaceTexture; if the two levels had different
        // default palettes, whether an ObjDesc palette reaches a part (CPhysicsPart::DetermineBasePal) would depend on it.
        var loader = new TextureLoader(Dats);
        Assert.True(loader.HighResInstalled);
        int surfaces = 0, paletted = 0, twoLevel = 0;
        var differing = new List<string>();
        foreach (var id in Dats.GetAllPortalIds<Surface>())
        {
            surfaces++;
            var high = loader.GetOriginalPaletteId(id, TextureDetail.High);
            var low = loader.GetOriginalPaletteId(id, TextureDetail.Low);
            paletted += high != 0 ? 1 : 0;
            var material = loader.GetSurface(id)!;
            if (material.IsTextured && loader.ResolveSurfaceTexture(material.SurfaceTextureId)?.RenderSurfaceIds.Count == 2)
            {
                twoLevel++;
            }

            if (high != low)
            {
                differing.Add($"0x{id:X8}: 0x{high:X8} / 0x{low:X8}");
            }
        }

        Assert.Equal(6152, surfaces);
        Assert.True(PinnedPalettedSurfaces == paletted, $"{paletted} paletted surfaces");
        Assert.True(twoLevel > 0);
        // Two Surfaces share SurfaceTexture levels 0x0600628F (high-res, not indexed) and 0x060045B4 (indexed, palette
        // 0x040008B4). Only GfxObjs 0x01000C0F and 0x01002C11 use them, and in both the base palette differs by level
        // (docs/avatars.md); DatAppearanceSource therefore takes the level the renderer draws.
        Assert.Equal("0x080009B1: 0x00000000 / 0x040008B4\n0x0800129A: 0x00000000 / 0x040008B4", string.Join('\n', differing));
    }

    /// <summary>The palette whose parts a clothing palette reaches: the base palette of the Setup's first paletted part.</summary>
    private static uint PaletteOf(PartArrayAppearance parts) => parts.Parts.FirstOrDefault(p => p.BasePaletteId != 0)?.BasePaletteId ?? 0;

    /// <summary>True when the part (after its swaps) has a surface whose original texture is the change's old texture.</summary>
    private static bool Matches(PartArrayAppearance parts, IAppearanceSource source, TextureMapChange change) =>
        change.PartIndex < parts.Parts.Count
        && source.SurfaceIds(parts.Parts[change.PartIndex].Chain.NearestGfxObjId).Any(s => source.OriginalTextureId(s) == change.OldSurfaceTextureId);

    /// <summary>
    /// 15,803 (ClothingTable, Setup) pairs. 101 Setups have no paletted surface, so no clothing palette reaches them. Ten
    /// pairs fail only because the table names parts beyond the Setup's (28 changes). 3,639 texture changes match no
    /// surface, which retail treats as success.
    /// </summary>
    private const string PinnedClothingSweep =
        "pairs 15803, missing Setups 0, without a paletted part 101, failed builds 0, failed applies 10, "
        + "part indices outside the Setup 28, texture changes matching no surface 3639";

    private const int PinnedPalettedSurfaces = 2981; // docs/textures.md §3: every paletted Surface
}
