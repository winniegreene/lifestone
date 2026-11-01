using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.RetailUi.Widgets;

/// <summary>
/// The composed spell and component icons (M5 D147, <c>m5-8-spell-icon-composite</c>; <see cref="UiSpellIcons"/>): retail's <c>ClientMagicSystem::CompositeSpellIcon</c> (11.6096 0x005682F0) puts the power tile behind the
/// SpellTable icon, replaces the icon's pure white outline from the spell's flags and adds the badge; <c>CompositeSpellComponentIcon</c> (0x005684C0) turns a component icon's white black. The real-dat tests pin the enum
/// groups' entries and check each layer in the composed pixels.
/// </summary>
public sealed class UiSpellIconsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    // The end-of-retail dats' entries of the two enum groups (read from the portal dat's EnumIDMaps, M5 D147).
    private const uint RedOutline = 0x06004C3E, BlackOutline = 0x06004C3F, SelfTargetedBadge = 0x060013F3, FellowshipBadge = 0x060030D7;

    private SpellTableData Table => _table ??= SpellTableData.Load(fixture.Dats);

    private SpellTableData? _table;

    [RequiresLocalDatsFact]
    public void The_enum_groups_hold_the_power_tiles_by_level_and_the_outlines_and_badges()
    {
        var maps = new EnumIdMaps(fixture.Dats);
        uint Tile(uint level) => maps.TryResolve(new EnumGroup(UiSpellIcons.PowerTiles), level, out var did) ? did : 0;
        uint Entry(uint entry) => maps.TryResolve(new EnumGroup(UiSpellIcons.OutlinesAndBadges), entry, out var did) ? did : 0;
        Assert.Equal(
            [0u, 0x060013F4, 0x060013F5, 0x060013F6, 0x060013F7, 0x060013F8, 0x060013F9, 0x060013F6, 0x06001F63, 0x060013F6, 0x060067A6, 0u],
            Enumerable.Range(0, 12).Select(l => Tile((uint)l)).ToArray());
        Assert.Equal([0u, RedOutline, BlackOutline, SelfTargetedBadge, FellowshipBadge, 0u], Enumerable.Range(0, 6).Select(e => Entry((uint)e)).ToArray());

        // Every spell of the table has a scarab in slot 0, so every spell has a tile (retail would fail on level 0).
        Assert.All(Table.Spells.Values, static s => Assert.InRange(SpellFormulaRules.PowerLevelOfComponent(s.Formula[0]), 1, 10));
        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            new uint[] { 1, 2, 3, 4, 5, 6, 0x6E, 0x70, 0xC0, 0xC1 }.Select(SpellFormulaRules.PowerLevelOfComponent).ToArray()); // Lead to Pyreal, Diamond, Platinum, Dark, Mana Scarab
    }

    [RequiresLocalDatsTheory]
    [InlineData(2u, 0x060013F4u, BlackOutline, SelfTargetedBadge)] // Strength Self I: Lead Scarab (level 1), flag 0x8 (SelfTargeted)
    [InlineData(3u, 0x060013F4u, RedOutline, 0u)] // Weakness Other I: level 1, flag 0x10 (Reversed): the red outline, no badge
    [InlineData(27u, 0x060013F4u, BlackOutline, 0u)] // Flame Bolt I: level 1, neither flag
    [InlineData(1240u, 0x060013F7u, RedOutline, 0u)] // Drain Health Other IV: Silver Scarab (level 4), reversed
    [InlineData(1783u, 0x060013F6u, BlackOutline, 0u)] // Searing Disc: Diamond Scarab (level 7, the level-3 tile again)
    [InlineData(2128u, 0x06001F63u, BlackOutline, 0u)] // Ilservian's Flame: Platinum Scarab (level 8)
    [InlineData(4422u, 0x060067A6u, BlackOutline, 0u)] // Incantation of Blade Arc: Mana Scarab (level 10)
    [InlineData(2981u, 0x060013F4u, BlackOutline, FellowshipBadge)] // Fellowship Heal I: flags 0x2000 and 0x8: the fellowship badge wins
    public void A_spells_picture_is_its_power_tile_behind_the_icon_with_the_outline_replaced_and_the_badge_of_its_flags(uint spellId, uint tileDid, uint outlineDid, uint badgeDid)
    {
        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), NullLogSink.Instance);
        var manager = Manager(images);
        var spell = Table.Spells[spellId];
        var fields = UiSpellIcon.Of(spell);
        Assert.Equal(outlineDid == RedOutline ? UiSpellIcons.RedOutlineEntry : UiSpellIcons.BlackOutlineEntry, UiSpellIcons.OutlineEntry(spell.Flags));
        Assert.Equal(badgeDid == 0 ? 0u : badgeDid == FellowshipBadge ? UiSpellIcons.FellowshipBadgeEntry : UiSpellIcons.SelfTargetedBadgeEntry, UiSpellIcons.BadgeEntry(spell.Flags));

        Assert.Equal(UiImageLookup.Ready, manager.SpellIcons.TryGetSpell(fields, out var picture));
        Assert.NotNull(picture);
        Assert.Equal((32, 32, UiPixelFormat.Argb), (picture.Width, picture.Height, picture.Format));
        var tile = Image(images, tileDid);
        var icon = Image(images, spell.IconId);
        var outline = Image(images, outlineDid);
        var badge = badgeDid == 0 ? null : Image(images, badgeDid);

        // The icon over the tile, before the outline and the badge: what each layer is checked against.
        var under = new UiSurface(32, 32);
        UiBlitter.Blit(tile, 0, 0, under, 0, 0, 32, 32, UiBlitMode.SrcCopy, 1f);
        UiBlitter.Blit(icon, 0, 0, under, 0, 0, 32, 32, UiBlitMode.FourChannelAlpha, 1f);
        int tileShown = 0, outlined = 0, badged = 0;
        for (var i = 0; i < 32 * 32 * 4; i += 4)
        {
            var badgeAlpha = badge?.Pixels[i + 3] ?? 0;
            if (badgeAlpha == 255)
            {
                Assert.Equal(Px(badge!.Pixels, i), Px(picture.Pixels, i)); // the badge's opaque pixels are on top
                badged++;
                continue;
            }

            if (badgeAlpha != 0)
            {
                continue; // a blended badge edge
            }

            Assert.False(IsWhite(picture.Pixels, i), "no pure white pixel is left: the outline replaced it");
            if (IsWhite(under.Pixels, i))
            {
                Assert.Equal(Px(outline.Pixels, i) | 0xFF000000, Px(picture.Pixels, i)); // the outline texture's pixel (an RGB texture: opaque)
                outlined++;
            }
            else if (icon.Pixels[i + 3] == 0 && tile.Pixels[i + 3] == 255)
            {
                Assert.Equal(Px(tile.Pixels, i), Px(picture.Pixels, i)); // the tile behind where the icon is clear
                tileShown++;
            }
            else
            {
                Assert.Equal(Px(under.Pixels, i), Px(picture.Pixels, i));
            }
        }

        Assert.True(outlined > 0, "the icon has a white outline to replace");
        Assert.True(tileShown > 0 || Enumerable.Range(0, 1024).All(p => icon.Pixels[(p * 4) + 3] != 0), "the tile shows behind the icon's clear pixels");
        if (badge is null)
        {
            Assert.Equal(0, badged);
        }
        else
        {
            Assert.Equal(74, Enumerable.Range(0, 1024).Count(p => badge.Pixels[(p * 4) + 3] != 0)); // the badge's mark, in its image's top-right ten by ten pixels
            Assert.True(badged > 0);
        }

        // Kept per spell id: the second ask composes nothing.
        var compositions = manager.SpellIcons.SpellCompositions;
        Assert.Equal(UiImageLookup.Ready, manager.SpellIcons.TryGetSpell(fields, out var again));
        Assert.Same(picture, again);
        Assert.Equal(compositions, manager.SpellIcons.SpellCompositions);
    }

    [RequiresLocalDatsFact]
    public void A_layer_still_decoding_leaves_the_picture_undone_until_it_is_ready()
    {
        var images = new HoldingImages(new DatUiImageSource(new TextureLoader(fixture.Dats), NullLogSink.Instance));
        images.Held.Add(BlackOutline);
        var manager = Manager(images);
        var fields = UiSpellIcon.Of(Table.Spells[27]);
        Assert.Equal(UiImageLookup.Pending, manager.SpellIcons.TryGetSpell(fields, out var picture));
        Assert.Null(picture);
        Assert.Equal(0, manager.SpellIcons.SpellCompositions);
        images.Held.Clear();
        Assert.Equal(UiImageLookup.Ready, manager.SpellIcons.TryGetSpell(fields, out picture));
        Assert.NotNull(picture);
        Assert.Equal(1, manager.SpellIcons.SpellCompositions);
    }

    [RequiresLocalDatsFact]
    public void A_component_picture_is_its_icon_with_the_pure_white_pixels_made_black()
    {
        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), NullLogSink.Instance);
        var manager = Manager(images);
        var lead = SpellComponentTableData.Load(fixture.Dats).Components[1]; // Lead Scarab
        var icon = Image(images, lead.IconId);
        var whites = Enumerable.Range(0, 1024).Count(p => IsWhite(icon.Pixels, p * 4));
        Assert.True(whites > 0, "the scarab's icon has a white outline");
        Assert.Equal(UiImageLookup.Ready, manager.SpellIcons.TryGetComponent(lead.IconId, out var picture));
        Assert.NotNull(picture);
        for (var i = 0; i < 32 * 32 * 4; i += 4)
        {
            Assert.Equal(IsWhite(icon.Pixels, i) ? 0xFF000000 : Px(icon.Pixels, i), Px(picture.Pixels, i));
        }

        Assert.Equal(UiImageLookup.Ready, manager.SpellIcons.TryGetComponent(lead.IconId, out var again));
        Assert.Same(picture, again);
        Assert.Equal(1, manager.SpellIcons.ComponentCompositions);
    }

    [Fact]
    public void Only_opaque_pure_white_becomes_black_and_the_flags_pick_the_outline_and_the_badge()
    {
        var surface = new UiSurface(3, 1);
        byte[] pixels = [255, 255, 255, 255, 255, 255, 255, 254, 254, 255, 255, 255];
        pixels.CopyTo(surface.Pixels, 0);
        UiSpellIcons.ReplaceWhiteWithBlack(surface);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 254, 254, 255, 255, 255 }, surface.Pixels);

        Assert.Equal(UiSpellIcons.BlackOutlineEntry, UiSpellIcons.OutlineEntry(SpellFlags.SelfTargeted | SpellFlags.FellowshipSpell));
        Assert.Equal(UiSpellIcons.RedOutlineEntry, UiSpellIcons.OutlineEntry(SpellFlags.Reversed));
        Assert.Equal(0u, UiSpellIcons.BadgeEntry(SpellFlags.Reversed | SpellFlags.Beneficial));
        Assert.Equal(UiSpellIcons.SelfTargetedBadgeEntry, UiSpellIcons.BadgeEntry(SpellFlags.SelfTargeted));
        Assert.Equal(UiSpellIcons.FellowshipBadgeEntry, UiSpellIcons.BadgeEntry(SpellFlags.FellowshipSpell));
        Assert.Equal(UiSpellIcons.FellowshipBadgeEntry, UiSpellIcons.BadgeEntry(SpellFlags.FellowshipSpell | SpellFlags.SelfTargeted));
    }

    [Fact]
    public void Without_image_services_nothing_is_composed()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(0x21000001, 800, 600)]) });
        Assert.Equal(UiImageLookup.Missing, manager.SpellIcons.TryGetSpell(new UiSpellIcon(1, 0x06001374, SpellFlags.None, 1), out var picture));
        Assert.Null(picture);
        Assert.Equal(UiImageLookup.Missing, manager.SpellIcons.TryGetComponent(0x060013E7, out picture));
        Assert.Null(picture);
    }

    private UiElementManager Manager(IUiImageSource images) =>
        new(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(0x21000001, 800, 600)]), Images = images, EnumMaps = new EnumIdMaps(fixture.Dats) });

    private static UiImage Image(IUiImageSource images, uint did)
    {
        Assert.Equal(UiImageLookup.Ready, images.TryGetImage(did, out var image));
        Assert.Equal((32, 32), (image!.Width, image.Height));
        return image;
    }

    private static uint Px(byte[] p, int i) => (uint)((p[i + 3] << 24) | (p[i] << 16) | (p[i + 1] << 8) | p[i + 2]);

    private static bool IsWhite(byte[] p, int i) => p[i] == 255 && p[i + 1] == 255 && p[i + 2] == 255 && p[i + 3] == 255;

    /// <summary>A source whose <see cref="Held"/> DIDs answer pending (the game's source decodes on a worker).</summary>
    private sealed class HoldingImages(IUiImageSource inner) : IUiImageSource
    {
        public HashSet<uint> Held { get; } = [];

        public UiImageLookup TryGetImage(uint did, [NotNullWhen(true)] out UiImage? image)
        {
            if (Held.Contains(did))
            {
                image = null;
                return UiImageLookup.Pending;
            }

            return inner.TryGetImage(did, out image);
        }
    }
}
