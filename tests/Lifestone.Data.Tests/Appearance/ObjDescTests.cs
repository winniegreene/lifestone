using DatReaderWriter.DBObjs;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// <see cref="ObjDesc"/> and <see cref="ObjDescBuilder"/>: retail's <c>ObjDesc::UnPack</c>, <c>Add*</c> de-duplication
/// and 255 cap, and <c>operator+=</c>.
/// </summary>
public sealed class ObjDescTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Fact]
    public void FromWire_keeps_wire_order_in_colours_and_reads_the_palette_only_with_sub_palettes()
    {
        var od = ObjDesc.FromWire(
            0x0400007E,
            [new WireSubPalette(0x04000001, 0x00, 0x18), new WireSubPalette(0x04000002, 0x18, 0x08)],
            [new TextureMapChange(16, 0x05000001, 0x05000002)],
            [new AnimPartChange(16, 0x01000001), new AnimPartChange(0, 0x01000002)]);
        Assert.Equal(0x0400007Eu, od.PaletteId);
        Assert.Equal(new[] { new SubPalette(0x04000001, 0, 192), new SubPalette(0x04000002, 192, 64) }, od.SubPalettes);
        Assert.Equal(new[] { new AnimPartChange(16, 0x01000001), new AnimPartChange(0, 0x01000002) }, od.PartChanges);
        Assert.Equal(0x0400007Eu, od.Palette!.BasePaletteId);

        // ObjDesc::UnPack reads the base palette only when the sub-palette count is non-zero.
        var noSubs = ObjDesc.FromWire(0x0400007E, [], [new TextureMapChange(1, 0x05000001, 0x05000002)], []);
        Assert.Equal(0u, noSubs.PaletteId);
        Assert.Null(noSubs.Palette);
        Assert.Same(ObjDesc.Empty, ObjDesc.FromWire(0x0400007E, [], [], []));
    }

    [Fact]
    public void A_texture_change_replaces_the_earlier_one_for_the_same_part_and_old_texture()
    {
        var b = new ObjDescBuilder();
        b.AddTextureMapChange(new TextureMapChange(1, 0x05000001, 0x05000002));
        b.AddTextureMapChange(new TextureMapChange(2, 0x05000001, 0x05000003)); // other part: kept
        b.AddTextureMapChange(new TextureMapChange(1, 0x05000009, 0x05000003)); // other old texture: kept
        b.AddTextureMapChange(new TextureMapChange(1, 0x05000001, 0x05000004)); // replaces the first, appended last
        Assert.Equal(
            new[]
            {
                new TextureMapChange(2, 0x05000001, 0x05000003),
                new TextureMapChange(1, 0x05000009, 0x05000003),
                new TextureMapChange(1, 0x05000001, 0x05000004),
            },
            b.TextureChanges);
    }

    [Fact]
    public void An_equal_part_change_moves_to_the_end_and_a_different_GfxObj_for_the_same_part_is_kept()
    {
        var b = new ObjDescBuilder();
        b.AddAnimPartChange(new AnimPartChange(16, 0x01000001));
        b.AddAnimPartChange(new AnimPartChange(3, 0x01000002));
        b.AddAnimPartChange(new AnimPartChange(16, 0x01000001));
        Assert.Equal(new[] { new AnimPartChange(3, 0x01000002), new AnimPartChange(16, 0x01000001) }, b.PartChanges);

        // The comparison is not pinned (TODO(avatars), ContentProfile::operator==): both fields are compared, so a
        // different GfxObj for the same part stays in the list. The last change for a part still wins when applied.
        b.AddAnimPartChange(new AnimPartChange(16, 0x01000005));
        Assert.Equal(3, b.PartChanges.Count);

        var byIndex = new ObjDescBuilder { DeduplicatePartsByIndexOnly = true };
        byIndex.AddAnimPartChange(new AnimPartChange(16, 0x01000001));
        byIndex.AddAnimPartChange(new AnimPartChange(16, 0x01000005));
        Assert.Equal(new[] { new AnimPartChange(16, 0x01000005) }, byIndex.PartChanges);
    }

    [Fact]
    public void Each_list_holds_at_most_255_entries()
    {
        var b = new ObjDescBuilder();
        for (var i = 0; i < ObjDesc.MaxEntries; i++)
        {
            Assert.True(b.AddTextureMapChange(new TextureMapChange((byte)(i % 32), 0x05000000u + (uint)i, 0x05001000)));
            Assert.True(b.AddAnimPartChange(new AnimPartChange((byte)i, 0x01000000u + (uint)i)));
            Assert.True(b.AddSubPalette(new SubPalette(0x04000001, i * 8, 8)));
        }

        Assert.False(b.AddTextureMapChange(new TextureMapChange(0, 0x05009999, 0x05001000)));
        Assert.False(b.AddAnimPartChange(new AnimPartChange(255, 0x01009999)));
        Assert.False(b.AddSubPalette(new SubPalette(0x04000001, 2040, 8)));
        Assert.Equal(255, b.TextureChanges.Count);
        Assert.Equal(255, b.PartChanges.Count);
        Assert.Equal(255, b.SubPalettes.Count);

        // A replacing entry still fits: the duplicate is removed before the cap is checked.
        Assert.True(b.AddTextureMapChange(new TextureMapChange(0, 0x05000000, 0x05002000)));
        Assert.Equal(new TextureMapChange(0, 0x05000000, 0x05002000), b.TextureChanges[^1]);
        Assert.Equal(255, b.TextureChanges.Count);
    }

    [Fact]
    public void A_whole_palette_swap_replaces_every_swap_and_supercedes_later_partial_ones()
    {
        var b = new ObjDescBuilder();
        b.AddSubPalette(new SubPalette(0x04000001, 0, 192));
        b.AddSubPalette(new SubPalette(0x04000002, 192, 64));
        Assert.True(b.AddSubPalette(new SubPalette(0x04000003, 0, 2048)));
        Assert.True(b.AddSubPalette(new SubPalette(0x04000004, 0, 192))); // dropped, but AddSubpalette returns 1
        Assert.Equal(new[] { new SubPalette(0x04000003, 0, 2048) }, b.SubPalettes);

        // A second whole-palette swap is not superceded (supercedes needs a partial incoming swap): it replaces.
        b.AddSubPalette(new SubPalette(0x04000005, 0, 2048));
        Assert.Equal(new[] { new SubPalette(0x04000005, 0, 2048) }, b.SubPalettes);
    }

    [Fact]
    public void Add_appends_through_the_rules_and_keeps_the_left_palette_id()
    {
        var left = ObjDesc.FromWire(0x04000010, [new WireSubPalette(0x04000001, 0, 0x18)], [], [new AnimPartChange(1, 0x01000001)]);
        var right = ObjDesc.FromWire(
            0x04000020, [new WireSubPalette(0x04000002, 0, 0x18)], [new TextureMapChange(1, 0x05000001, 0x05000002)], [new AnimPartChange(2, 0x01000002)]);
        var sum = left.Add(right);
        Assert.Equal(0x04000010u, sum.PaletteId); // operator+= never copies the right-hand palette id
        Assert.Equal(new[] { new SubPalette(0x04000002, 0, 192) }, sum.SubPalettes);
        Assert.Equal(new[] { new AnimPartChange(1, 0x01000001), new AnimPartChange(2, 0x01000002) }, sum.PartChanges);
        Assert.Single(sum.TextureChanges);
        Assert.Same(left, left.Add(ObjDesc.Empty));
        Assert.Equal(0x04000099u, left.WithPaletteId(0x04000099).PaletteId);
        Assert.NotEqual(left.CacheKey, sum.CacheKey);
        Assert.Equal(sum.CacheKey, left.ToBuilder().Add(right).Build().CacheKey);
    }

    [Fact]
    public void TryUnpack_rejects_a_header_other_than_0x11_and_truncated_data()
    {
        Assert.False(ObjDesc.TryUnpack([0x10, 0, 0, 0], out _, out _, out var failure));
        Assert.Contains("0x10", failure, StringComparison.Ordinal);
        Assert.False(ObjDesc.TryUnpack([0x11, 0, 0], out _, out _, out _));
        Assert.False(ObjDesc.TryUnpack([0x11, 0, 0, 1, 0x05], out _, out _, out failure)); // part change cut short
        Assert.Contains("part change", failure, StringComparison.Ordinal);

        Assert.True(ObjDesc.TryUnpack([0x11, 0, 0, 0, 0xAA], out var empty, out var consumed, out _));
        Assert.Same(ObjDesc.Empty, empty);
        Assert.Equal(4, consumed);

        var bad = new DatReaderWriter.Types.ObjDesc { UnknownByte = 0x12 };
        Assert.Throws<InvalidDataException>(() => ObjDesc.FromDat(bad));
    }

    [Fact]
    public void TryUnpack_reads_known_type_ids_and_pads_to_four_bytes()
    {
        // One sub-palette, one part change: header 4 + palette 2 + sub 2+2 + part 1+4 = 15 bytes, padded to 16.
        byte[] data =
        [
            0x11, 1, 0, 1,
            0x7E, 0x00,             // base palette 0x0400007E (short form)
            0x34, 0x12, 0x18, 0x08, // sub-palette 0x04001234 at 0x18 x 0x08
            0x10, 0x01, 0x80, 0x87, 0x17, // part 16 -> 0x01011787 (long form: 0x8001, 0x1787)
            0x00,                   // padding
            0xFF,                   // not part of the ObjDesc
        ];
        Assert.True(ObjDesc.TryUnpack(data, out var od, out var consumed, out var failure), failure);
        Assert.Equal(16, consumed);
        Assert.Equal(0x0400007Eu, od.PaletteId);
        Assert.Equal(new[] { new SubPalette(0x04001234, 192, 64) }, od.SubPalettes);
        Assert.Equal(new[] { new AnimPartChange(16, 0x01011787) }, od.PartChanges);

        // Unpack_AsDataIDOfKnownType adds the type to every value, so a null id on the wire reads as the type itself.
        Assert.True(ObjDesc.TryUnpack([0x11, 0, 1, 0, 3, 0, 0, 0, 0, 0, 0, 0], out var zero, out _, out _));
        Assert.Equal(new TextureMapChange(3, 0x05000000, 0x05000000), zero.TextureChanges[0]);
    }

    [Fact]
    public void ACE_serialised_ModelData_unpacks_to_the_lists_retail_would_hold()
    {
        var ace = new AceObjDescOracle.AceObjDesc { PaletteID = 0x0400007E };
        ace.SubPalettes.Add((0x04001071, 0x18, 0x08));
        ace.SubPalettes.Add((0x040002BB, 0x00, 0x18));
        ace.SubPalettes.Add((0x040002BC, 0x00, 0x18)); // same range: replaces the previous one on receipt
        ace.TextureChanges.Add((0x10, 0x050003CE, 0x05001234));
        ace.AnimPartChanges.Add((0x10, 0x01001787));
        ace.AnimPartChanges.Add((0x00, 0x010000A0));
        var bytes = AceObjDescOracle.SerializeModelData(ace);
        Assert.Equal(0, bytes.Length % 4);

        Assert.True(ObjDesc.TryUnpack(bytes, out var od, out var consumed, out var failure), failure);
        Assert.Equal(bytes.Length, consumed);
        var expected = ObjDesc.FromWire(
            0x0400007E,
            [new(0x04001071, 0x18, 0x08), new(0x040002BB, 0x00, 0x18), new(0x040002BC, 0x00, 0x18)],
            [new TextureMapChange(0x10, 0x050003CE, 0x05001234)],
            [new AnimPartChange(0x10, 0x01001787), new AnimPartChange(0, 0x010000A0)]);
        Assert.Equal(expected.CacheKey, od.CacheKey);
        Assert.Equal(new[] { new SubPalette(0x04001071, 192, 64), new SubPalette(0x040002BC, 0, 192) }, od.SubPalettes);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_CharGen_ObjDesc_converts()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(CharGenId, out var cg));
        int count = 0, entries = 0, deduplicated = 0;
        foreach (var record in AllCharGenObjDescs(cg))
        {
            var od = ObjDesc.FromDat(record); // throws unless the header is 0x11
            count++;
            var raw = record.SubPalettes.Count + record.TextureChanges.Count + record.AnimPartChanges.Count;
            var kept = od.SubPalettes.Count + od.TextureChanges.Count + od.PartChanges.Count;
            entries += raw;
            deduplicated += raw - kept;
            Assert.Equal(record.SubPalettes.Count == 0 ? 0u : record.PaletteId.DataId, od.PaletteId);
        }

        // 13 heritages x 2 sexes: base ObjDescs, hair styles, eye strips (two each), nose and mouth strips.
        Assert.Equal(2404, count);
        Assert.Equal(PinnedCharGenEntries, entries);
        Assert.Equal(0, deduplicated); // no CharGen ObjDesc repeats an entry
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Both_part_duplicate_rules_draw_the_same_parts()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(CharGenId, out var cg));
        var source = new DatAppearanceSource(new TextureLoader(Dats));
        var compared = 0;
        foreach (var look in CharGenAppearanceTests.FirstChoiceLooks(cg, source).Concat(CharGenAppearanceTests.EveryHairStyleLooks(cg, source)))
        {
            var bothFields = CharGenAppearance.Compose(cg, look, source, new ObjDescBuilder());
            var indexOnly = CharGenAppearance.Compose(cg, look, source, new ObjDescBuilder { DeduplicatePartsByIndexOnly = true });
            var setup = Lifestone.Data.Models.ModelAssembly.Load(Dats, bothFields.SetupId)!;
            var a = PartArrayAppearance.Create(setup, source);
            var b = PartArrayAppearance.Create(setup, source);
            a.DoObjDescChangesFromDefault(bothFields.ObjDesc);
            b.DoObjDescChangesFromDefault(indexOnly.ObjDesc);
            Assert.Equal(Describe(a), Describe(b));
            compared++;
        }

        Assert.True(compared > 26, $"only {compared} looks compared");
    }

    internal const uint CharGenId = 0x0E000002;

    /// <summary>Entries in all CharGen ObjDescs (sub-palettes, texture and part changes), pinned by the sweep.</summary>
    private const int PinnedCharGenEntries = 4479;

    internal static IEnumerable<DatReaderWriter.Types.ObjDesc> AllCharGenObjDescs(CharGen cg)
    {
        foreach (var group in cg.HeritageGroups.Values)
        {
            foreach (var sex in group.Genders.Values)
            {
                yield return sex.BaseObjDesc;
                foreach (var h in sex.HairStyles)
                {
                    yield return h.ObjDesc;
                }

                foreach (var e in sex.EyeStrips)
                {
                    yield return e.ObjDesc;
                    yield return e.BaldObjDesc;
                }

                foreach (var n in sex.NoseStrips)
                {
                    yield return n.ObjDesc;
                }

                foreach (var m in sex.MouthStrips)
                {
                    yield return m.ObjDesc;
                }
            }
        }
    }

    internal static string Describe(PartArrayAppearance appearance) =>
        string.Join('\n', appearance.Parts.Select(p => $"{p.Index}: 0x{p.Chain.RootGfxObjId:X8} base 0x{p.BasePaletteId:X8}{p.VariantKey}"));
}
