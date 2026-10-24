using System.Buffers.Binary;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>Sub-palette units, ObjDesc de-duplication and palette building (retail <c>Palette::makeModifiedPalette</c>).</summary>
public sealed class PaletteOverrideTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Fact]
    public void Wire_units_are_groups_of_8_colours_and_length_0_means_2048()
    {
        // ACE WorldObject_Networking.CalculateObjDesc: skin (0x00, 0x18), hair (0x18, 0x08), eyes (0x20, 0x08).
        Assert.Equal(new SubPalette(0x04000001, 0, 192), SubPalette.FromWire(0x04000001, 0x00, 0x18));
        Assert.Equal(new SubPalette(0x04000001, 192, 64), SubPalette.FromWire(0x04000001, 0x18, 0x08));
        Assert.Equal(new SubPalette(0x04000001, 256, 64), SubPalette.FromWire(0x04000001, 0x20, 0x08));
        // Subpalette::UnPack: a length byte of 0 is 256 groups.
        var whole = SubPalette.FromWire(0x04000002, 0, 0);
        Assert.Equal(2048, whole.Length);
        Assert.True(whole.IsWholePalette);
    }

    [Fact]
    public void ObjDesc_add_rules_drop_superseded_and_replace_duplicates()
    {
        var a = new SubPalette(0x04000001, 0, 192);
        var b = new SubPalette(0x04000002, 192, 64);
        var a2 = new SubPalette(0x04000003, 0, 192);
        var whole = new SubPalette(0x04000004, 0, 2048);

        // Same range replaces the earlier entry; order is insertion order.
        Assert.Equal(new[] { b, a2 }, PaletteOverride.FromObjDesc(0x04000010, [a, b, a2]).SubPalettes);
        // A whole-palette swap removes everything before it and blocks later partial swaps.
        Assert.Equal(new[] { whole }, PaletteOverride.FromObjDesc(0x04000010, [a, b, whole, a2]).SubPalettes);
        // Without de-duplication the list is kept as given.
        Assert.Equal(new[] { a, b, a2 }, new PaletteOverride(0x04000010, [a, b, a2]).SubPalettes);
    }

    [Fact]
    public void Build_copies_the_base_then_overwrites_ranges_in_order()
    {
        var palettes = new Dictionary<uint, PaletteData>
        {
            [1] = new(1, Enumerable.Repeat(0xFF000001u, 2048).ToArray()),
            [2] = new(2, Enumerable.Range(0, 2048).Select(i => 0xFF000000u | (uint)i).ToArray()),
            [3] = new(3, Enumerable.Repeat(0xFF333333u, 2048).ToArray()),
        };
        var o = new PaletteOverride(1, [new SubPalette(2, 8, 16), new SubPalette(3, 16, 16)]);
        Assert.True(o.TryBuild(id => palettes.GetValueOrDefault(id), out var p, out var failure), failure);
        Assert.Equal(0u, p!.Id);
        Assert.Equal(o.CacheKey, p.CacheKey);
        Assert.Equal(0xFF000001u, p[7]);
        Assert.Equal(0xFF000008u, p[8]);     // same index from palette 2
        Assert.Equal(0xFF00000Fu, p[15]);
        Assert.Equal(0xFF333333u, p[16]);    // later swap wins on overlap
        Assert.Equal(0xFF333333u, p[31]);
        Assert.Equal(0xFF000001u, p[32]);

        // A lone whole-palette swap means "use that palette".
        var w = new PaletteOverride(1, [new SubPalette(3, 0, 2048)]);
        Assert.True(w.TryBuild(id => palettes.GetValueOrDefault(id), out var wp, out _));
        Assert.Equal(palettes[3].Argb.ToArray(), wp!.Argb.ToArray());

        // Failures leave the part palettes alone in retail; here they are reported.
        Assert.False(new PaletteOverride(1, [new SubPalette(2, 2040, 16)]).TryBuild(id => palettes.GetValueOrDefault(id), out _, out var f1));
        Assert.Contains("exceeds", f1);
        Assert.False(new PaletteOverride(1, [new SubPalette(9, 0, 8)]).TryBuild(id => palettes.GetValueOrDefault(id), out _, out var f2));
        Assert.Contains("not found", f2);
        Assert.False(new PaletteOverride(0, [new SubPalette(2, 0, 8)]).TryBuild(id => palettes.GetValueOrDefault(id), out _, out _));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Clothing_table_ranges_are_whole_wire_units_inside_2048_colours()
    {
        // ACE re-encodes CloSubPaletteRange as (Offset / 8, NumColors / 8) bytes for the wire; that is lossless only
        // if every range is a multiple of 8 within the palette. Check every ClothingTable in the portal.
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<ClothingTable>().ToArray());
        Assert.Equal(1917, ids.Length);
        var ranges = 0;
        foreach (var id in ids)
        {
            var table = TextureTestSupport.ReadUncached<ClothingTable>(Dats, id)!;
            foreach (var effect in table.ClothingSubPalEffects.Values)
            {
                foreach (var sub in effect.CloSubPalettes)
                {
                    foreach (var r in sub.Ranges)
                    {
                        Assert.True(r.Offset % 8 == 0 && r.NumColors % 8 == 0 && r.NumColors > 0 && r.Offset + r.NumColors <= 2048,
                            $"0x{id:X8}: range {r.Offset}+{r.NumColors}");
                        ranges++;
                    }
                }
            }
        }

        Assert.True(ranges > 1000, $"only {ranges} ranges");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_clothing_palette_swap_recolours_exactly_the_indices_in_its_ranges()
    {
        // Real data end to end: the first ClothingTable sub-palette effect, its PaletteSet at shade 0.5, applied over
        // the INDEX16 armour texture 0x06005747 (default palette 0x0400007E, checked visually in local-scout.md).
        var loader = new TextureLoader(Dats);
        var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, 0x06005747u)!;
        Assert.Equal(PixelFormat.PFID_INDEX16, rs.Format);
        Assert.Equal(0x0400007Eu, rs.DefaultPaletteId);

        var (table, sub) = FirstClothingSubPalette();
        var set = TextureTestSupport.ReadUncached<PalSet>(Dats, sub.PaletteSet.DataId)!;
        var swapId = PaletteSets.GetPaletteId(set, 0.5);
        Assert.NotEqual(0u, swapId);
        var swaps = sub.Ranges.Select(r => SubPalette.FromColorRange(swapId, r.Offset, r.NumColors)).ToArray();
        var o = PaletteOverride.FromObjDesc(rs.DefaultPaletteId, swaps);
        Assert.True(loader.TryBuildPalette(o, out var modified, out var failure), $"ClothingTable 0x{table:X8}: {failure}");

        var basePalette = loader.GetPalette(rs.DefaultPaletteId)!;
        var swapPalette = loader.GetPalette(swapId)!;
        var tex = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8, modified!.Argb);
        var recoloured = 0;
        for (var i = 0; i < rs.Width * rs.Height; i++)
        {
            int index = BinaryPrimitives.ReadUInt16LittleEndian(rs.SourceData.AsSpan(i * 2));
            var inRange = swaps.Any(s => index >= s.Offset && index < s.Offset + s.Length);
            var c = inRange ? swapPalette[index] : basePalette[index];
            Assert.Equal(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)255 }, tex.Data.AsSpan(i * 4, 4).ToArray());
            recoloured += inRange ? 1 : 0;
        }

        Assert.True(recoloured > 0, "no texel fell in the swapped ranges; pick another sample");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void PaletteSet_shade_lookup_matches_retail_formula()
    {
        var (_, sub) = FirstClothingSubPalette();
        var set = TextureTestSupport.ReadUncached<PalSet>(Dats, sub.PaletteSet.DataId)!;
        var n = set.Palettes.Count;
        Assert.True(n > 0);
        Assert.Equal((uint)set.Palettes[0], PaletteSets.GetPaletteId(set, 0.0));
        Assert.Equal((uint)set.Palettes[n - 1], PaletteSets.GetPaletteId(set, 1.0)); // (n - 1e-6) * 1 truncates to n - 1
        Assert.Equal(0u, PaletteSets.GetPaletteId(set, 1.5));
        Assert.Equal(0u, PaletteSets.GetPaletteId(set, -0.1));
        Assert.Equal(0u, PaletteSets.GetPaletteId(set, double.NaN));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Dat_palettes_have_2048_opaque_entries()
    {
        var loader = new TextureLoader(Dats);
        var p = loader.GetPalette(0x0400007Eu)!;
        Assert.Equal(PaletteData.RetailColorCount, p.Count);
        Assert.All(p.Argb.ToArray(), c => Assert.Equal(0xFFu, c >> 24));
        Assert.Same(p, loader.GetPalette(0x0400007Eu)); // cached
        Assert.Null(loader.GetPalette(0x0400FFFFu));
    }

    private (uint TableId, DatReaderWriter.Types.CloSubPalette Sub) FirstClothingSubPalette()
    {
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<ClothingTable>().ToArray());
        foreach (var id in ids.Order())
        {
            var table = TextureTestSupport.ReadUncached<ClothingTable>(Dats, id)!;
            foreach (var effect in table.ClothingSubPalEffects.OrderBy(kv => kv.Key).Select(kv => kv.Value))
            {
                var sub = effect.CloSubPalettes.FirstOrDefault(s => s.Ranges.Count > 0 && s.PaletteSet.DataId >> 24 == 0x0F);
                if (sub is not null)
                {
                    return (id, sub);
                }
            }
        }

        throw new Xunit.Sdk.XunitException("no ClothingTable sub-palette found");
    }
}
