using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using SubPalette = Lifestone.Data.Textures.SubPalette;
using TextureMapChange = Lifestone.Data.Appearance.TextureMapChange;

namespace Lifestone.Data.Tests.Appearance;

/// <summary><see cref="ClothingObjDesc.Build"/> (retail <c>ClothingTable::BuildObjDesc</c>) and <see cref="ShadePackage"/>.</summary>
public sealed class ClothingObjDescTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    private const uint Setup = 0x02000001;

    [Fact]
    public void ShadePackage_fills_every_slot_and_repeats_the_last_one()
    {
        var one = new ShadePackage(0.3);
        Assert.Equal(0.3, one.GetVal(0));
        Assert.Equal(0.3, one.GetVal(3));
        var four = new ShadePackage(0.1, 0.2, 0.3, 0.4);
        Assert.Equal(0.2, four.GetVal(1));
        Assert.Equal(0.4, four.GetVal(3));
        Assert.Equal(0.4, four.GetVal(7)); // ShadePackage::GetVal: index 4 and up read slot 3
    }

    [Fact]
    public void Parts_then_textures_then_one_sub_palette_per_range_in_colours()
    {
        var dats = new FakeDats().WithPalSet(0x0F000001, 0x04000011, 0x04000012, 0x04000013).WithPalSet(0x0F000002, 0x04000021);
        var table = Table(
            Effect(Setup, (3, 0x01000003, [(0x05000001, 0x05000002)]), (4, 0x01000004, [])),
            (7, [(0x0F000001, [(0u, 24u), (512u, 64u)]), (0x0F000002, [(800u, 8u)])]));
        var into = new ObjDescBuilder();
        var r = ClothingObjDesc.Build(table, Setup, 7, new ShadePackage(1.0, 0.0, 0.0, 0.0), dats, into);
        Assert.True(r.AllApplied, r.ToString());

        Assert.Equal(new[] { new AnimPartChange(3, 0x01000003), new AnimPartChange(4, 0x01000004) }, into.PartChanges);
        Assert.Equal(new[] { new TextureMapChange(3, 0x05000001, 0x05000002) }, into.TextureChanges);
        // Effect 0 uses shade GetVal(0) = 1.0 (the last palette), effect 1 GetVal(1) = 0.0.
        Assert.Equal(
            new[] { new SubPalette(0x04000013, 0, 24), new SubPalette(0x04000013, 512, 64), new SubPalette(0x04000021, 800, 8) },
            into.SubPalettes);
    }

    [Fact]
    public void Shades_outside_0_to_1_give_the_null_palette_as_PalSet_GetPaletteID_does()
    {
        var dats = new FakeDats().WithPalSet(0x0F000001, 0x04000011, 0x04000012, 0x04000013);
        var table = Table(Effect(Setup), (7, [(0x0F000001, [(0u, 8u)])]));
        foreach (var (shade, expected) in new[] { (0.0, 0x04000011u), (0.5, 0x04000012u), (1.0, 0x04000013u), (double.NaN, 0u), (1.5, 0u) })
        {
            var into = new ObjDescBuilder();
            Assert.True(ClothingObjDesc.Build(table, Setup, 7, new ShadePackage(shade), dats, into).AllApplied);
            Assert.Equal(expected, Assert.Single(into.SubPalettes).PaletteId);
        }
    }

    [Fact]
    public void Template_0_adds_no_palette_and_an_unknown_template_or_PaletteSet_fails()
    {
        var dats = new FakeDats();
        var table = Table(Effect(Setup, (1, 0x01000001, [])), (7, [(0x0F000009, [(0u, 8u)])]));

        var none = new ObjDescBuilder();
        Assert.True(ClothingObjDesc.Build(table, Setup, 0, new ShadePackage(0.5), dats, none).AllApplied);
        Assert.Empty(none.SubPalettes);
        Assert.Single(none.PartChanges);

        var unknown = ClothingObjDesc.Build(table, Setup, 8, new ShadePackage(0.5), dats, new ObjDescBuilder());
        Assert.False(unknown.AllApplied); // ACE would use the table's first template instead
        Assert.Contains("palette template 8", Assert.Single(unknown.Problems), StringComparison.Ordinal);

        var missingSet = ClothingObjDesc.Build(table, Setup, 7, new ShadePackage(0.5), dats, new ObjDescBuilder());
        Assert.False(missingSet.AllApplied);
        Assert.Contains("0x0F000009 not found", Assert.Single(missingSet.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_texture_is_still_added_but_fails_before_the_palettes()
    {
        var dats = new FakeDats().WithPalSet(0x0F000001, 0x04000011);
        var table = Table(Effect(Setup, (2, 0x01000002, [(0x05000001, 0u)])), (7, [(0x0F000001, [(0u, 8u)])]));
        var into = new ObjDescBuilder();
        var r = ClothingObjDesc.Build(table, Setup, 7, new ShadePackage(0.5), dats, into);
        Assert.False(r.AllApplied);
        Assert.Equal(new[] { new TextureMapChange(2, 0x05000001, 0) }, into.TextureChanges);
        Assert.Empty(into.SubPalettes);
    }

    [Theory]
    [InlineData(0x02001A9Cu, 0x02001A0Eu)] // Undead male skeleton
    [InlineData(0x02001A0Fu, 0x02001A0Eu)] // Undead male gen
    [InlineData(0x02001AA2u, 0x02001A0Cu)] // Undead female zombie without flame
    [InlineData(0x02001A6Fu, 0x0200196Fu)] // Umbraen male void
    [InlineData(0x02001A6Eu, 0x02001970u)] // Umbraen female void (ACE spells it 0x2001A6EE)
    [InlineData(0x02001971u, 0x0200196Eu)] // Penumbraen male crown gen
    [InlineData(0x02001A71u, 0x0200196Du)] // Penumbraen female void
    [InlineData(0x02001AA3u, 0x02000001u)] // Anakshay male → HUMAN_MALE_CLOTHING_DEFAULT
    [InlineData(0x02001AA4u, 0x0200004Eu)] // Anakshay female → HUMAN_FEMALE_CLOTHING_DEFAULT
    public void A_Setup_the_table_lacks_takes_its_heritage_groups_entry(uint setup, uint fallback)
    {
        Assert.True(ClothingObjDesc.HeritageFallbacksKnown);
        Assert.Equal(fallback, ClothingObjDesc.GetHeritageFallbackSetupId(setup));
        var table = Table(Effect(fallback, (1, 0x01000001, [])), (7, []));
        var into = new ObjDescBuilder();
        var r = ClothingObjDesc.Build(table, setup, 7, new ShadePackage(0.5), new FakeDats(), into);
        Assert.True(r.AllApplied, r.ToString());
        Assert.Equal(new[] { new AnimPartChange(1, 0x01000001) }, into.PartChanges);
    }

    [Fact]
    public void The_Setups_own_entry_wins_over_its_fallback()
    {
        var table = Table(Effect(0x02001A0E, (1, 0x01000001, [])), (7, []));
        table.ClothingBaseEffects.Add(new QualifiedDataId<DatReaderWriter.DBObjs.Setup> { DataId = 0x02001A9C }, Effect(0x02001A9C, (2, 0x01000002, [])).Item2);
        var into = new ObjDescBuilder();
        Assert.True(ClothingObjDesc.Build(table, 0x02001A9C, 7, new ShadePackage(0.5), new FakeDats(), into).AllApplied);
        Assert.Equal(new[] { new AnimPartChange(2, 0x01000002) }, into.PartChanges);
    }

    [Theory]
    [InlineData(0x02001907u)] // a Gear Knight body: in no group
    [InlineData(0x02001A20u)] // an Olthoi body: in no group
    [InlineData(0x02001A9Cu)] // in a group whose fallback the table lacks too
    [InlineData(0x0200196Fu)] // a group's own fallback, missing: it maps to itself
    public void A_Setup_without_an_entry_or_a_fallback_entry_applies_nothing_and_succeeds(uint setup)
    {
        var table = Table(Effect(Setup, (1, 0x01000001, [])), (7, [(0x0F000009, [(0u, 8u)])]));
        var into = new ObjDescBuilder();
        var r = ClothingObjDesc.Build(table, setup, 7, new ShadePackage(0.5), new FakeDats(), into);
        Assert.True(r.AllApplied, r.ToString());
        Assert.Empty(into.PartChanges);
        Assert.Empty(into.SubPalettes); // retail returns before the palettes too
        Assert.Equal(setup is 0x02001907 or 0x02001A20, ClothingObjDesc.GetHeritageFallbackSetupId(setup) == 0);
    }

    [RequiresDatsFact]
    public void The_CharGen_Setups_that_need_the_heritage_fallback_are_listed()
    {
        // Every (Setup, ClothingTable) pair CharGen can produce where the table has no entry for the Setup. Retail maps
        // the Undead skeleton and zombie bodies to the Undead default clothing Setups, which every such table has; the
        // others (Gear Knight, Olthoi) are in no fallback group and get nothing.
        Assert.True(Dats.TryGetPortal<CharGen>(ObjDescTests.CharGenId, out var cg));
        var misses = new SortedSet<string>(StringComparer.Ordinal);
        var unclothed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var group in cg.HeritageGroups.Values)
        {
            foreach (var sex in group.Genders.Values)
            {
                var setups = new SortedSet<uint> { sex.SetupId.DataId };
                foreach (var h in sex.HairStyles.Where(h => h.AlternateSetup != 0))
                {
                    setups.Add(h.AlternateSetup);
                }

                foreach (var gear in sex.Headgears.Concat(sex.Shirts).Concat(sex.Pants).Concat(sex.Footwear))
                {
                    Assert.True(Dats.TryGetPortal<ClothingTable>(gear.ClothingTable.DataId, out var table));
                    foreach (var setup in setups.Where(s => !Has(table, s)))
                    {
                        misses.Add($"0x{setup:X8}");
                        var fallback = ClothingObjDesc.GetHeritageFallbackSetupId(setup);
                        if (fallback == 0 || !Has(table, fallback))
                        {
                            unclothed.Add($"0x{setup:X8}");
                        }
                    }
                }
            }
        }

        Assert.Equal(
            "0x02001907 0x020019ED 0x02001A20 0x02001A21 0x02001A9C 0x02001A9D 0x02001AA0 0x02001AA1",
            string.Join(' ', misses));
        Assert.Equal("0x02001907 0x020019ED 0x02001A20 0x02001A21", string.Join(' ', unclothed));

        static bool Has(ClothingTable table, uint setup) => table.ClothingBaseEffects.Keys.Any(k => k.DataId == setup);
    }

    private static ClothingTable Table(
        (uint Setup, ClothingBaseEffect Effect) baseEffect, params (uint Template, (uint PalSet, (uint Offset, uint Count)[] Ranges)[] Subs)[] templates)
    {
        var table = new ClothingTable { Id = 0x10000099 };
        table.ClothingBaseEffects.Add(new QualifiedDataId<DatReaderWriter.DBObjs.Setup> { DataId = baseEffect.Setup }, baseEffect.Effect);
        foreach (var (template, subs) in templates)
        {
            var effect = new CloSubPalEffect();
            foreach (var (palSet, ranges) in subs)
            {
                var sub = new CloSubPalette { PaletteSet = palSet };
                foreach (var (offset, count) in ranges)
                {
                    sub.Ranges.Add(new CloSubPaletteRange { Offset = offset, NumColors = count });
                }

                effect.CloSubPalettes.Add(sub);
            }

            table.ClothingSubPalEffects.Add(template, effect);
        }

        return table;
    }

    private static (uint, ClothingBaseEffect) Effect(uint setup, params (uint Part, uint GfxObj, (uint Old, uint New)[] Textures)[] objects)
    {
        var effect = new ClothingBaseEffect();
        foreach (var (part, gfxObj, textures) in objects)
        {
            var o = new CloObjectEffect { Index = part, ModelId = gfxObj };
            foreach (var (oldTexture, newTexture) in textures)
            {
                o.CloTextureEffects.Add(new CloTextureEffect { OldTexture = oldTexture, NewTexture = newTexture });
            }

            effect.CloObjectEffects.Add(o);
        }

        return (setup, effect);
    }

    /// <summary>An in-memory <see cref="IAppearanceDats"/>.</summary>
    private sealed class FakeDats : IAppearanceDats
    {
        private readonly Dictionary<uint, PalSet> _palSets = [];

        public FakeDats WithPalSet(uint id, params uint[] palettes)
        {
            var set = new PalSet { Id = id };
            foreach (var p in palettes)
            {
                set.Palettes.Add(p);
            }

            _palSets[id] = set;
            return this;
        }

        public ClothingTable? GetClothingTable(uint clothingTableId) => null;

        public PalSet? GetPalSet(uint palSetId) => _palSets.GetValueOrDefault(palSetId);
    }
}
