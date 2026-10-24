using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Retail bitmap fonts (m2-plan.md R5): the FONT enum chain and its gap, the glyph lookup of <c>Font::GetCharDesc</c>,
/// the byte arithmetic of <c>Font::GetCharWidthA</c>, the fallback fonts of <c>Glyph::SetFont</c> and the atlases.
/// </summary>
public sealed class FontTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_portal_holds_49_fonts_and_FONT_names_48_enums_for_39_DIDs_one_of_them_absent()
    {
        var fonts = new UiFonts(Dats);
        var entries = Dats.EnumMaps.Entries(EnumGroup.Font);

        Assert.Equal(49, fonts.FontIds.Count);
        Assert.Equal(48, entries.Count);
        Assert.Equal(39, entries.Values.Distinct().Count());
        Assert.Equal("FONT", Dats.EnumMaps.GroupName(EnumGroup.Font));
        Assert.Equal(0x25000012u, Dats.EnumMaps.GroupMapId(EnumGroup.Font));

        // Every enum but FixedSys_15 names a font the portal dat holds.
        var missing = entries.Where(e => !fonts.FontIds.Contains(e.Value)).ToArray();
        var gap = Assert.Single(missing);
        Assert.Equal((UiFonts.FixedSys15Enum, 0x40000033u), (gap.Key, gap.Value));
        Assert.Equal("FixedSys_15", Dats.EnumMaps.NameOf(EnumGroup.Font, UiFonts.FixedSys15Enum));
        Assert.All(entries.Keys.Where(k => k != UiFonts.FixedSys15Enum), e => Assert.True(fonts.TryGetFontByEnum(e, out _)));

        // 11 fonts are reached by no enum: layouts name them by DID.
        Assert.Equal(11, fonts.FontIds.Except(entries.Values).Count());
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void FixedSys_15_is_a_loud_named_gap()
    {
        var fonts = new UiFonts(Dats);

        Assert.True(fonts.TryResolveFontEnum(UiFonts.FixedSys15Enum, out var did));
        Assert.Equal(0x40000033u, did);
        Assert.False(fonts.TryGetFontByEnum(UiFonts.FixedSys15Enum, out _));
        var error = Assert.Throws<InvalidDataException>(() => fonts.GetFontByEnum(UiFonts.FixedSys15Enum));
        Assert.Equal(
            "TODO(retailui-data): FONT enum 0x00000012 resolves to Font 0x40000033, which is not in client_portal.dat (a gap in the end-of-retail dats).",
            error.Message);
        error = Assert.Throws<InvalidDataException>(() => fonts.GetFontByEnum(0x10000FFFu));
        Assert.Equal("TODO(retailui-data): FONT enum 0x10000FFF does not resolve to a Font.", error.Message);
        error = Assert.Throws<InvalidDataException>(() => fonts.GetFont(0x40000033u));
        Assert.Equal("TODO(retailui-data): Font 0x40000033 is not in client_portal.dat.", error.Message);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [InlineData(0x4u, 0x40000000u, "Palatino_Linotype_16")]
    [InlineData(UiFonts.DefaultEnum, 0x40000002u, "Default")]
    [InlineData(UiFonts.FallbackEnumKorean, 0x40000018u, "Default_Korean")]
    [InlineData(UiFonts.FallbackEnumJapanese, 0x40000018u, "Default_Japanese")]
    [InlineData(0x10000001u, 0x40000016u, "Stonehenge_18")]
    [InlineData(0x10000014u, 0x40000009u, "Chat_Tahoma_Small")]
    public void FONT_enums_resolve_as_retail_names_them(uint fontEnum, uint fontId, string name)
    {
        var fonts = new UiFonts(Dats);

        Assert.Equal(name, Dats.EnumMaps.NameOf(EnumGroup.Font, fontEnum));
        var font = fonts.GetFontByEnum(fontEnum);
        Assert.Equal(fontId, font.Id);
        Assert.Same(font, fonts.GetFont(fontId)); // converted once
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Font_0x40000000_has_1050_glyphs_of_at_most_16_by_16_and_baseline_12()
    {
        var font = new UiFonts(Dats).GetFont(0x40000000u);

        Assert.Equal(1050, font.Glyphs.Count);
        Assert.Equal((16, 16, 12), (font.MaxCharHeight, font.MaxCharWidth, font.BaselineOffset));
        Assert.Equal((4, 4), (font.HorizontalBorderPixels, font.VerticalBorderPixels));
        Assert.All(font.Glyphs, g => Assert.True(g.Width <= 16 && g.Height <= 16, $"glyph U+{(int)g.Unicode:X4} is {g.Width}x{g.Height}"));
        Assert.Equal((' ', '￼'), (font.MinChar, font.MaxChar));
        Assert.Equal((0x06005EE5u, 0x06005EE6u), (font.ForegroundSurfaceId, font.BackgroundSurfaceId));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Glyph_lookup_follows_GetCharDesc_glyph_0_inside_the_range_and_question_mark_outside_it()
    {
        var font = new UiFonts(Dats).GetFont(0x40000000u);
        var question = font.GetCharDescIndex('?');

        // A held character finds its own glyph.
        var a = font.GetCharDescIndex('A');
        Assert.Equal('A', font.Glyphs[a].Unicode);
        Assert.Equal(font.Glyphs[a].Advance, font.GetCharWidth('A'));

        // Below the range (a control character): the '?' glyph.
        Assert.Equal(question, font.GetCharDescIndex('\u0001'));
        Assert.Equal(font.GetCharWidth('?'), font.GetCharWidth('\t'));
        Assert.False(font.ContainsChar('\t'));

        // Inside the range but not held: glyph 0, the space, as retail's zeroed character map gives.
        Assert.DoesNotContain(font.Glyphs, g => g.Unicode == '一');
        Assert.True(font.ContainsChar('一'));
        Assert.Equal(0, font.GetCharDescIndex('一'));
        Assert.Equal(' ', font.Glyphs[0].Unicode);
        Assert.True(font.TryGetCharDesc('一', out var glyph));
        Assert.Equal(' ', glyph.Unicode);

        // Above the range: '?'.
        Assert.Equal(question, font.GetCharDescIndex('￿'));
    }

    [Fact]
    public void GetCharWidthA_adds_unsigned_bytes_and_wraps()
    {
        Assert.Equal(4, new UiGlyph('a', 0, 0, 5, 9, -1, 0, 0).Advance); // 5 + 0xFF + 0 = 0x104
        Assert.Equal(7, new UiGlyph('a', 0, 0, 5, 9, 1, 1, 0).Advance);
        Assert.Equal(4, new UiGlyph('a', 0, 0, 250, 9, 10, 0, 0).Advance); // 260 keeps its low byte
        Assert.Equal(253, new UiGlyph('a', 0, 0, 0, 9, -1, -2, 0).Advance); // 0xFF + 0xFE = 0x1FD
    }

    [Fact]
    public void Synthetic_fonts_cover_the_edges_of_GetCharDesc()
    {
        // No '?' and a range that excludes it: a character outside the range has no glyph.
        var font = UiFont.FromRecord(SyntheticFont(0x40000F00u, 'a', 'c', 'e'));
        Assert.Equal(-1, font.GetCharDescIndex('z'));
        Assert.Equal(-1, font.GetCharDescIndex('?'));
        Assert.Equal(0, font.GetCharWidth('z'));
        Assert.False(font.TryGetCharDesc('z', out _));
        Assert.Equal(0, font.GetCharDescIndex('b')); // inside the range, not held: glyph 0
        Assert.Equal(2, font.GetCharDescIndex('e'));

        // '?' inside the range: an outside character falls back to it.
        font = UiFont.FromRecord(SyntheticFont(0x40000F01u, '?', 'A'));
        Assert.Equal(0, font.GetCharDescIndex('~'));
        Assert.Equal(1, font.GetCharDescIndex('A'));

        // A repeated character: the later glyph wins, as GenerateCharacterMap's loop overwrites.
        font = UiFont.FromRecord(SyntheticFont(0x40000F02u, 'x', 'x'));
        Assert.Equal(1, font.GetCharDescIndex('x'));

        var error = Assert.Throws<InvalidDataException>(() => UiFont.FromRecord(SyntheticFont(0x40000F03u)));
        Assert.StartsWith("TODO(retailui-data): Font 0x40000F03 holds no glyph", error.Message, StringComparison.Ordinal);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Glyph_SetFont_falls_back_to_the_Gulim_font_for_characters_below_a_fonts_range()
    {
        var fonts = new UiFonts(Dats);
        var palatino = fonts.GetFont(0x40000000u);
        var gulim = fonts.GetFont(0x40000018u);

        Assert.Same(palatino, fonts.ResolveGlyphFont('A', palatino));
        Assert.Same(palatino, fonts.ResolveGlyphFont('一', palatino)); // inside the range: kept (ContainsChar)
        Assert.Same(palatino, fonts.ResolveGlyphFont('\n', palatino)); // a newline always keeps its font
        Assert.Same(gulim, fonts.ResolveGlyphFont('\t', palatino));
        Assert.Null(fonts.ResolveGlyphFont('￿', palatino)); // outside every font
        Assert.Null(fonts.ResolveGlyphFont('A', null));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_font_converts_and_every_glyph_lies_inside_its_atlases()
    {
        var fonts = new UiFonts(Dats);
        var textures = new TextureLoader(Dats);
        var withBackground = 0;
        foreach (var id in fonts.FontIds)
        {
            var font = fonts.GetFont(id);
            var foreground = textures.ReadRenderSurface(font.ForegroundSurfaceId, out var source);
            Assert.NotNull(foreground);
            Assert.Equal(TextureDat.Portal, source);
            Assert.Equal(PixelFormat.PFID_A8, foreground.Format);
            if (font.BackgroundSurfaceId != 0)
            {
                withBackground++;
                var background = textures.ReadRenderSurface(font.BackgroundSurfaceId, out _);
                Assert.NotNull(background);
                Assert.Equal(PixelFormat.PFID_A8, background.Format);
                Assert.Equal((foreground.Width, foreground.Height), (background.Width, background.Height));
            }

            Assert.All(font.Glyphs, g => Assert.True(
                g.OffsetX + g.Width <= foreground.Width && g.OffsetY + g.Height <= foreground.Height,
                $"font 0x{id:X8} glyph U+{(int)g.Unicode:X4} lies outside its {foreground.Width}x{foreground.Height} atlas"));
            Assert.True(font.Glyphs.Max(g => g.Height) <= font.MaxCharHeight);

            var decoded = font.DecodeForeground(textures);
            Assert.Equal((DecodedTextureFormat.Alpha8, foreground.Width, foreground.Height), (decoded.Format, decoded.Width, decoded.Height));
            Assert.Equal(font.BackgroundSurfaceId == 0, font.DecodeBackground(textures) is null);
        }

        Assert.Equal(37, withBackground);
    }

    private static Font SyntheticFont(uint id, params char[] characters)
    {
        var font = new Font { Id = id, MaxCharHeight = 10, MaxCharWidth = 8, BaselineOffset = 8 };
        foreach (var c in characters)
        {
            font.CharDescs.Add(new FontCharDesc { Unicode = c, Width = 6, Height = 10 });
        }

        return font;
    }
}
