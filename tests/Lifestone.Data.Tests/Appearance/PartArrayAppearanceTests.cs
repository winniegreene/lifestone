using DatReaderWriter.DBObjs;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// <see cref="PartArrayAppearance"/>: retail <c>CPartArray::DoObjDescChanges</c> / <c>DoObjDescChangesFromDefault</c> and
/// the <c>CPhysicsPart</c> part, texture and palette rules, on a synthetic source and on the user's dats.
/// </summary>
public sealed class PartArrayAppearanceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    // Synthetic data: GfxObj 1 (surfaces 0x81, 0x82), GfxObj 2 (surfaces 0x83, 0x81), GfxObj 3 (0x84); Surface 0x81 has
    // texture 0x51 and no palette, 0x82 texture 0x52 with palette 0x40, 0x83 texture 0x51 with palette 0x41, 0x84 is
    // untextured. Palettes 0x40, 0x41, 0x42, 0x43 are 2,048 entries of their id.
    private static FakeSource NewSource() => new FakeSource()
        .GfxObj(0x01000001, 0x08000081, 0x08000082)
        .GfxObj(0x01000002, 0x08000083, 0x08000081)
        .GfxObj(0x01000003, 0x08000084)
        .Surface(0x08000081, 0x05000051, 0)
        .Surface(0x08000082, 0x05000052, 0x04000040)
        .Surface(0x08000083, 0x05000051, 0x04000041)
        .Surface(0x08000084, 0, 0)
        .Palette(0x04000040).Palette(0x04000041).Palette(0x04000042).Palette(0x04000043);

    private static ModelAssembly NewSetup(params uint[] parts) =>
        ModelAssembly.FromSetup(0x02000099, new Setup { Parts = [.. parts.Select(p => new DatReaderWriter.Types.QualifiedDataId<GfxObj> { DataId = p })] });

    [Fact]
    public void A_new_part_array_draws_the_Setup_with_base_palettes_from_its_first_paletted_surface()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000002, 0x01000003), NewSource());
        Assert.True(a.IsLoadable);
        Assert.Equal(new[] { 0x04000040u, 0x04000041u, 0u }, a.Parts.Select(p => p.BasePaletteId));
        Assert.All(a.Parts, p => Assert.Equal("", p.VariantKey));
        Assert.All(a.Parts, p => Assert.Null(p.Palette));

        var missing = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000077), NewSource());
        Assert.False(missing.IsLoadable); // CPartArray::InitParts refuses the object
    }

    [Fact]
    public void A_texture_change_targets_the_swapped_in_GfxObjs_surfaces()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001), NewSource());
        // Part swap first (to GfxObj 2: surfaces 0x83, 0x81), then texture 0x51 -> 0x59: both 0x83 and 0x81 have it.
        var r = a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000051, 0x05000059)], [new AnimPartChange(0, 0x01000002)]));
        Assert.True(r.AllApplied, r.ToString());
        var part = a.Parts[0];
        Assert.Equal(0x01000002u, part.Chain.RootGfxObjId);
        Assert.Equal(0x05000059u, part.TextureOverrideFor(0x08000083));
        Assert.Equal(0x05000059u, part.TextureOverrideFor(0x08000081));
        Assert.Equal("|tm:08000081=05000059,08000083=05000059", part.VariantKey);
        // The base palette comes from the swapped-in surfaces before any texture change: 0x83's palette.
        Assert.Equal(0x04000041u, part.BasePaletteId);
    }

    [Fact]
    public void Texture_changes_match_original_textures_and_report_nulls_and_untextured_surfaces()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000003), NewSource());
        Assert.True(a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000052, 0x05000060)], [])).AllApplied);
        // A second change for the same surface matches its original texture again, not the first swap's result.
        Assert.True(a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000060, 0x05000061)], [])).AllApplied);
        Assert.Equal(0x05000060u, a.Parts[0].TextureOverrideFor(0x08000082));
        Assert.True(a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000052, 0x05000062)], [])).AllApplied);
        Assert.Equal(0x05000062u, a.Parts[0].TextureOverrideFor(0x08000082));

        // A change back to the original texture undoes the swap: the surface draws its own texture, with no variant key.
        Assert.True(a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000052, 0x05000052)], [])).AllApplied);
        Assert.Null(a.Parts[0].TextureOverrideFor(0x08000082));
        Assert.Equal("", a.Parts[0].VariantKey);

        var nullNew = a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000051, 0)], []));
        Assert.False(nullNew.AllApplied);
        Assert.Contains("null new texture", Assert.Single(nullNew.Problems), StringComparison.Ordinal);

        // Old id 0 matches the untextured surface 0x84, which CSurface::UseTextureMap refuses.
        var untextured = a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(1, 0, 0x05000063)], []));
        Assert.False(untextured.AllApplied);
        Assert.Null(a.Parts[1].TextureOverrideFor(0x08000084));
    }

    [Fact]
    public void A_bad_part_change_fails_alone_and_keeps_the_old_part()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000003), NewSource());
        var r = a.DoObjDescChanges(ObjDesc.FromWire(
            0,
            [],
            [],
            [new AnimPartChange(7, 0x01000002), new AnimPartChange(1, 0x01000077), new AnimPartChange(0, 0), new AnimPartChange(1, 0x01000002)]));
        Assert.False(r.AllApplied);
        Assert.Equal(3, r.Problems.Count);
        Assert.Contains("2 parts", r.Problems[0], StringComparison.Ordinal);
        Assert.Contains("does not load", r.Problems[1], StringComparison.Ordinal);
        Assert.Contains("null GfxObj", r.Problems[2], StringComparison.Ordinal);
        Assert.Equal(0x01000001u, a.Parts[0].Chain.RootGfxObjId);
        Assert.Equal(0x01000002u, a.Parts[1].Chain.RootGfxObjId); // the good change after the bad ones still applies
    }

    [Fact]
    public void The_palette_goes_only_to_parts_whose_base_palette_is_the_ObjDesc_palette()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000002, 0x01000003), NewSource());
        var r = a.DoObjDescChanges(ObjDesc.FromWire(0x04000040, [new WireSubPalette(0x04000042, 0, 0x18)], [], []));
        Assert.True(r.AllApplied, r.ToString());
        var palette = a.Parts[0].Palette!;
        Assert.Null(a.Parts[1].Palette); // base 0x41
        Assert.Null(a.Parts[2].Palette); // base 0
        Assert.Equal(0x42u, palette[0] & 0xFF);     // swapped range
        Assert.Equal(0x40u, palette[192] & 0xFF);   // copied base
        Assert.Equal("|pal:pal04000040+04000042@0x192", a.Parts[0].VariantKey);

        var nullId = a.DoObjDescChanges(ObjDesc.FromWire(0, [new WireSubPalette(0x04000042, 0, 0x18)], [], []));
        Assert.False(nullId.AllApplied); // CPartArray::SetPalette refuses the null palette id
    }

    [Fact]
    public void A_second_ObjDesc_modifies_the_palette_in_place_for_every_part_that_holds_it()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000001), NewSource());
        a.DoObjDescChanges(ObjDesc.FromWire(0x04000040, [new WireSubPalette(0x04000042, 0, 0x18)], [], []));
        // Part 1 swaps to GfxObj 2 (base 0x41) but keeps the palette it holds (CPhysicsPart::SetGfxObjArray re-applies it).
        a.DoObjDescChanges(ObjDesc.FromWire(0, [], [], [new AnimPartChange(1, 0x01000002)]));
        Assert.Equal(0x04000041u, a.Parts[1].BasePaletteId);
        Assert.Same(a.Parts[0].Palette, a.Parts[1].Palette);

        // Palette::Modify on the existing palette: the new swap lands on top of the old one, and both parts see it.
        var r = a.DoObjDescChanges(ObjDesc.FromWire(0x04000040, [new WireSubPalette(0x04000043, 0x18, 0x08)], [], []));
        Assert.True(r.AllApplied, r.ToString());
        var p = a.Parts[0].Palette!;
        Assert.Same(p, a.Parts[1].Palette);
        Assert.Equal(0x42u, p[0] & 0xFF);
        Assert.Equal(0x43u, p[192] & 0xFF);
        Assert.Equal("pal04000040+04000042@0x192+04000043@192x64", p.CacheKey);
    }

    [Fact]
    public void FromDefault_restores_palettes_but_keeps_part_and_texture_swaps()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001), NewSource());
        a.DoObjDescChanges(ObjDesc.FromWire(
            0x04000041,
            [new WireSubPalette(0x04000042, 0, 0x18)],
            [new TextureMapChange(0, 0x05000051, 0x05000059)],
            [new AnimPartChange(0, 0x01000002)]));
        Assert.NotNull(a.Parts[0].Palette);
        var version = a.Version;

        Assert.True(a.DoObjDescChangesFromDefault(null).AllApplied);
        Assert.True(a.Version > version);
        Assert.Null(a.Parts[0].Palette);
        Assert.Equal(0x01000002u, a.Parts[0].Chain.RootGfxObjId);
        Assert.Equal(0x05000059u, a.Parts[0].TextureOverrideFor(0x08000083));

        // From default, a palette is built afresh from the base (no earlier swaps underneath).
        a.DoObjDescChangesFromDefault(ObjDesc.FromWire(0x04000041, [new WireSubPalette(0x04000043, 0x18, 0x08)], [], []));
        Assert.Equal("pal04000041+04000043@192x64", a.Parts[0].Palette!.CacheKey);
    }

    [Fact]
    public void A_lone_whole_palette_swap_uses_the_dat_palette_and_a_missing_sub_palette_fails()
    {
        var source = NewSource();
        var a = PartArrayAppearance.Create(NewSetup(0x01000001), source);
        Assert.True(a.DoObjDescChanges(ObjDesc.FromWire(0x04000040, [new WireSubPalette(0x04000043, 0, 0)], [], [])).AllApplied);
        Assert.Same(source.GetPalette(0x04000043), a.Parts[0].Palette);

        var b = PartArrayAppearance.Create(NewSetup(0x01000001), source);
        var r = b.DoObjDescChanges(ObjDesc.FromWire(0x04000040, [new WireSubPalette(0x04000077, 0, 0x18)], [], []));
        Assert.False(r.AllApplied);
        Assert.Contains("0x04000077 not found", Assert.Single(r.Problems), StringComparison.Ordinal);
        Assert.Null(b.Parts[0].Palette);
    }

    [Fact]
    public void Unchanged_parts_keep_their_appearance_object()
    {
        var a = PartArrayAppearance.Create(NewSetup(0x01000001, 0x01000003), NewSource());
        var before = a.Parts[1];
        a.DoObjDescChanges(ObjDesc.FromWire(0, [], [new TextureMapChange(0, 0x05000051, 0x05000059)], []));
        Assert.Same(before, a.Parts[1]);
        Assert.NotSame(before, a.Parts[0]);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_CharGen_sex_Setup_applies_its_base_ObjDesc()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(ObjDescTests.CharGenId, out var cg));
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var applied = 0;
        foreach (var group in cg.HeritageGroups.Values)
        {
            foreach (var sex in group.Genders.Values)
            {
                var setup = ModelAssembly.Load(Dats, sex.SetupId.DataId)!;
                var a = PartArrayAppearance.Create(setup, source);
                Assert.True(a.IsLoadable);
                var r = a.DoObjDescChangesFromDefault(ObjDesc.FromDat(sex.BaseObjDesc).WithPaletteId(sex.BasePalette.DataId));
                Assert.True(r.AllApplied, $"Setup 0x{setup.Id:X8}: {r}");
                applied++;
            }
        }

        Assert.Equal(26, applied);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void ACE_naked_human_ObjDesc_draws_the_Setup_parts_with_the_hair_style_head()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(ObjDescTests.CharGenId, out var cg));
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var dats = new DatAppearanceDats(Dats);
        var player = AceObjDescOracle.CreatePlayer(cg, 1, 1, new AceObjDescOracle.AceAppearance(0, 0, 0, 0, 0, 0, 0.5, 0.5), dats.GetPalSet);
        var ace = AceObjDescOracle.CalculateObjDesc(player, [], cg, dats.GetClothingTable, dats.GetPalSet, SetupParts);
        Assert.True(ObjDesc.TryUnpack(AceObjDescOracle.SerializeModelData(ace), out var od, out _, out var failure), failure);

        var setup = ModelAssembly.Load(Dats, player.SetupTableId)!;
        var a = PartArrayAppearance.Create(setup, source);
        var r = a.DoObjDescChangesFromDefault(od);
        Assert.True(r.AllApplied, r.ToString());
        for (var i = 0; i < setup.Parts.Count; i++)
        {
            var expected = i == 16 ? player.HeadObjectDID!.Value : setup.Parts[i].GfxObjId;
            Assert.Equal(expected, a.Parts[i].Chain.RootGfxObjId);
        }
    }

    internal IReadOnlyList<uint> SetupParts(uint setupId) => ModelAssembly.Load(Dats, setupId)!.Parts.Select(p => p.GfxObjId).ToArray();

    /// <summary>An in-memory <see cref="IAppearanceSource"/>.</summary>
    private sealed class FakeSource : IAppearanceSource
    {
        private readonly Dictionary<uint, uint[]> _gfxObjs = [];
        private readonly Dictionary<uint, (uint Texture, uint Palette)> _surfaces = [];
        private readonly Dictionary<uint, PaletteData> _palettes = [];

        public FakeSource GfxObj(uint id, params uint[] surfaces)
        {
            _gfxObjs[id] = surfaces;
            return this;
        }

        public FakeSource Surface(uint id, uint texture, uint palette)
        {
            _surfaces[id] = (texture, palette);
            return this;
        }

        public FakeSource Palette(uint id)
        {
            _palettes[id] = new PaletteData(id, Enumerable.Repeat(0xFF000000u | (id & 0xFF), PaletteData.RetailColorCount).ToArray());
            return this;
        }

        public LodChain GetChain(uint gfxObjId) =>
            _gfxObjs.ContainsKey(gfxObjId)
                ? LodChain.Create(gfxObjId, new DatReaderWriter.DBObjs.GfxObj(), null, _ => true)
                : LodChain.Create(gfxObjId, null, null, _ => false);

        public IReadOnlyList<uint> SurfaceIds(uint gfxObjId) => _gfxObjs.GetValueOrDefault(gfxObjId) ?? [];

        public uint OriginalTextureId(uint surfaceId) => _surfaces.GetValueOrDefault(surfaceId).Texture;

        public uint OriginalPaletteId(uint surfaceId) => _surfaces.GetValueOrDefault(surfaceId).Palette;

        public PaletteData? GetPalette(uint paletteId) => _palettes.GetValueOrDefault(paletteId);
    }
}
