using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// What retail's <c>CompositeSpellIcon</c> reads of a spell (<c>CSpellTable::InqSpellBase</c>): its id (the cache key), the SpellTable icon, the <c>CSpellBase::_bitfield</c> flags and the base formula's slot 0
/// (<c>CSpellBase::InqSpellFormula</c>, the scarab whose power level picks the tile).
/// </summary>
/// <param name="SpellId">The spell; the composed picture is kept under this id.</param>
/// <param name="IconId">The SpellTable icon (a RenderSurface DID; 0 draws none).</param>
/// <param name="Flags">The spell's flags (<see cref="SpellFlags"/>).</param>
/// <param name="PowerComponent">Slot 0 of the base formula (a scarab for every spell of the retail table).</param>
public readonly record struct UiSpellIcon(uint SpellId, uint IconId, SpellFlags Flags, uint PowerComponent)
{
    /// <summary>The fields of <paramref name="spell"/>.</summary>
    public static UiSpellIcon Of(SpellInfo spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return new UiSpellIcon(spell.Id, spell.IconId, spell.Flags, spell.Formula.Count > 0 ? spell.Formula[0] : 0);
    }
}

/// <summary>
/// The composed spell and spell-component icons, made once per id and kept, as retail's <c>ClientMagicSystem::GetSpellIcon</c> (11.6096 0x00569990) and <c>GetSpellComponentIcon</c> (0x00569A50) keep theirs
/// (M5 D147, <c>m5-8-spell-icon-composite</c>). Retail never draws a raw spell icon: every spell cell, the spell bars, the spellbook, the effects page and a dragged spell draw this picture, with three-channel alpha.
/// </summary>
/// <remarks>
/// <para>
/// <b>A spell</b> (<c>ClientMagicSystem::CompositeSpellIcon</c>, 11.6096 0x005682F0), on a 32 by 32 surface, every layer blitted over the whole surface from its top-left corner:
/// (1) the power tile, copied: entry <i>n</i> of enum group <see cref="PowerTiles"/>, <i>n</i> the power level of the formula's slot 0 (<c>SpellFormula::GetPowerLevelOfPowerComponent</c> 0x005BDA10 over
/// <c>MagicSystem::DeterminePowerLevelOfComponent</c> 0x005BE310: Lead to Pyreal Scarab 1 to 6, Diamond 7, Platinum 8, Dark 9, Mana 10; <see cref="SpellFormulaRules.PowerLevelOfComponent"/>);
/// (2) the SpellTable icon with four-channel alpha; (3) every pure white pixel of the result (the icon's outline) replaced by the pixel of entry 1 of group <see cref="OutlinesAndBadges"/> (a red texture) when
/// the spell has flag 0x10 (ACE's <c>Reversed</c>), else of entry 2 (black) (<c>SurfaceWindow::ReplaceColor</c>, 11.6096 0x00441750); (4) with four-channel alpha, entry 4 of that group when the spell has flag
/// 0x2000 (ACE's <c>FellowshipSpell</c>), else entry 3 when it has flag 0x8 (ACE's <c>SelfTargeted</c>), else nothing. In the end-of-retail dats the tiles are 0x060013F4 to 0x060013F9 for levels 1 to 6,
/// 0x060013F6 again for 7 and 9, 0x06001F63 for 8 and 0x060067A6 for 10 (entry 0 is absent); the outlines 0x06004C3E and 0x06004C3F; the badges 0x060013F3 and 0x060030D7, each a 32 by 32 image whose
/// mark fills its top-right ten by ten pixels.
/// </para>
/// <para>
/// <b>A component</b> (<c>ClientMagicSystem::CompositeSpellComponentIcon</c>, 11.6096 0x005684C0): the SpellComponentTable icon copied onto a 32 by 32 surface, then every pure white pixel made opaque black
/// (<c>SurfaceWindow::ReplaceColor</c>'s colour overload, 0x004416D0). A component without an image is filled with <c>ClientMagicSystem::s_NullColor</c>, whose value is not read: <c>TODO(m5-8-component-null-color)</c>.
/// </para>
/// <para>
/// A layer whose image is still decoding leaves the whole picture undone (<see cref="UiImageLookup.Pending"/>; the caller asks again on a later frame); a layer the dats lack is left out and logged once
/// (retail would fail on a missing tile or outline: every spell of the table has a scarab in slot 0). <c>TODO(m5-8-spell-icon-badge)</c>: what the two badges mean to a player beyond ACE's flag names
/// (<c>SelfTargeted</c>, <c>FellowshipSpell</c>) is not established; they are drawn as retail draws them.
/// </para>
/// </remarks>
public sealed class UiSpellIcons
{
    /// <summary>The power tiles' enum group (<c>DBObj::GetByEnum(level, 0x10000006, RenderSurface)</c>).</summary>
    public const uint PowerTiles = 0x10000006;

    /// <summary>The outlines' and badges' enum group (<c>DBObj::GetByEnum(entry, 0x10000007, RenderSurface)</c>).</summary>
    public const uint OutlinesAndBadges = 0x10000007;

    /// <summary>The entries of <see cref="OutlinesAndBadges"/>.</summary>
    public const uint RedOutlineEntry = 1, BlackOutlineEntry = 2, SelfTargetedBadgeEntry = 3, FellowshipBadgeEntry = 4;

    private const int Size = 32;
    private readonly UiElementManager _manager;
    private readonly Dictionary<uint, UiImage> _spells = [];
    private readonly Dictionary<uint, UiImage> _components = [];

    internal UiSpellIcons(UiElementManager manager) => _manager = manager;

    /// <summary>How many spell pictures were composed (each spell once).</summary>
    public int SpellCompositions { get; private set; }

    /// <summary>How many component pictures were composed (each icon once).</summary>
    public int ComponentCompositions { get; private set; }

    /// <summary>The outline entry of a spell: red for flag 0x10, else black.</summary>
    public static uint OutlineEntry(SpellFlags flags) => (flags & SpellFlags.Reversed) != 0 ? RedOutlineEntry : BlackOutlineEntry;

    /// <summary>The badge entry of a spell: 4 for flag 0x2000, else 3 for flag 0x8, else 0 (none).</summary>
    public static uint BadgeEntry(SpellFlags flags) =>
        (flags & SpellFlags.FellowshipSpell) != 0 ? FellowshipBadgeEntry : (flags & SpellFlags.SelfTargeted) != 0 ? SelfTargetedBadgeEntry : 0;

    /// <summary>
    /// The picture of <paramref name="spell"/>, composed on the first ask and kept under its spell id (<c>GetSpellIcon</c>). <see cref="UiImageLookup.Pending"/> while a layer is decoding (each ask also starts
    /// the decoding of every layer); <see cref="UiImageLookup.Missing"/> with no image services or for spell 0.
    /// </summary>
    public UiImageLookup TryGetSpell(in UiSpellIcon spell, out UiImage? picture)
    {
        if (_spells.TryGetValue(spell.SpellId, out picture))
        {
            return UiImageLookup.Ready;
        }

        var images = _manager.Services.Images;
        if (spell.SpellId == 0 || images is null)
        {
            picture = null;
            return UiImageLookup.Missing;
        }

        var maps = _manager.Services.EnumMaps;
        var level = SpellFormulaRules.PowerLevelOfComponent(spell.PowerComponent);
        var outlineEntry = OutlineEntry(spell.Flags);
        var badgeEntry = BadgeEntry(spell.Flags);
        var pending = false;
        var tileDid = Resolve(maps, PowerTiles, (uint)level, spell.SpellId, "tile");
        var outlineDid = Resolve(maps, OutlinesAndBadges, outlineEntry, spell.SpellId, "outline");
        var badgeDid = badgeEntry == 0 ? 0 : Resolve(maps, OutlinesAndBadges, badgeEntry, spell.SpellId, "badge");
        var tile = Get(images, tileDid, ref pending);
        var icon = Get(images, spell.IconId, ref pending);
        var outline = Get(images, outlineDid, ref pending);
        var badge = Get(images, badgeDid, ref pending);
        if (pending)
        {
            picture = null;
            return UiImageLookup.Pending;
        }

        var surface = new UiSurface(Size, Size);
        if (tile is not null)
        {
            Blit(tile, surface, UiBlitMode.SrcCopy);
        }

        if (icon is not null)
        {
            Blit(icon, surface, UiBlitMode.FourChannelAlpha);
        }

        if (outline is not null)
        {
            UiItemIcons.ReplaceWhite(surface, outline);
        }

        if (badge is not null)
        {
            Blit(badge, surface, UiBlitMode.FourChannelAlpha);
        }

        picture = new UiImage(spell.IconId, Size, Size, UiPixelFormat.Argb, surface.Pixels);
        _spells[spell.SpellId] = picture;
        SpellCompositions++;
        if (badgeEntry != 0)
        {
            _manager.LogOnce($"spellicon:badge:{badgeEntry}", LogSeverity.Warning, string.Create(CultureInfo.InvariantCulture,
                $"TODO(m5-8-spell-icon-badge): spell {spell.SpellId} draws badge entry {badgeEntry} of group 0x{OutlinesAndBadges:X8} (flag {(badgeEntry == FellowshipBadgeEntry ? "0x2000, ACE FellowshipSpell" : "0x8, ACE SelfTargeted")}) as CompositeSpellIcon (0x005682F0) does; what the badge means to a player beyond ACE's flag name is not established"));
        }

        if (_manager.Services.Log.IsEnabled(LogSeverity.Info))
        {
            _manager.Services.Log.Write(LogSeverity.Info, UiElementManager.Category, string.Create(CultureInfo.InvariantCulture,
                $"spell icon {spell.SpellId}: tile level {level} (slot 0 component {spell.PowerComponent}) 0x{tileDid:X8}, icon 0x{spell.IconId:X8}, outline entry {outlineEntry} 0x{outlineDid:X8}, badge entry {badgeEntry} 0x{badgeDid:X8}"), []);
        }

        return UiImageLookup.Ready;
    }

    /// <summary>The picture of the component icon <paramref name="iconDid"/> (<c>GetSpellComponentIcon</c>: composed once per icon and kept); results as <see cref="TryGetSpell"/>.</summary>
    public UiImageLookup TryGetComponent(uint iconDid, out UiImage? picture)
    {
        if (_components.TryGetValue(iconDid, out picture))
        {
            return UiImageLookup.Ready;
        }

        var images = _manager.Services.Images;
        if (iconDid == 0 || images is null)
        {
            picture = null;
            return UiImageLookup.Missing;
        }

        var pending = false;
        var icon = Get(images, iconDid, ref pending);
        if (pending)
        {
            picture = null;
            return UiImageLookup.Pending;
        }

        var surface = new UiSurface(Size, Size);
        if (icon is not null)
        {
            Blit(icon, surface, UiBlitMode.SrcCopy);
            ReplaceWhiteWithBlack(surface);
        }
        else
        {
            _manager.LogOnce("spellicon:component-null-color", LogSeverity.Warning, "TODO(m5-8-component-null-color): a component icon is missing; retail fills the picture with ClientMagicSystem::s_NullColor (value not read); Lifestone leaves it transparent");
        }

        picture = new UiImage(iconDid, Size, Size, UiPixelFormat.Argb, surface.Pixels);
        _components[iconDid] = picture;
        ComponentCompositions++;
        return UiImageLookup.Ready;
    }

    /// <summary><c>SurfaceWindow::ReplaceColor</c>'s colour overload as <c>CompositeSpellComponentIcon</c> calls it: every opaque white pixel becomes opaque black.</summary>
    internal static void ReplaceWhiteWithBlack(UiSurface target)
    {
        var p = target.Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i] == 255 && p[i + 1] == 255 && p[i + 2] == 255 && p[i + 3] == 255)
            {
                p[i] = p[i + 1] = p[i + 2] = 0;
            }
        }
    }

    private uint Resolve(EnumIdMaps? maps, uint group, uint entry, uint spellId, string layer)
    {
        if (maps is not null && maps.TryResolve(new EnumGroup(group), entry, out var did))
        {
            return did;
        }

        _manager.LogOnce($"spellicon:enum:{group:X8}:{entry}", LogSeverity.Warning, string.Create(CultureInfo.InvariantCulture,
            $"CompositeSpellIcon: entry {entry} of enum group 0x{group:X8} does not resolve (first for spell {spellId}); the {layer} is left out of the spell icon"));
        return 0;
    }

    private UiImage? Get(IUiImageSource images, uint did, ref bool pending)
    {
        if (did == 0)
        {
            return null;
        }

        switch (images.TryGetImage(did, out var image))
        {
            case UiImageLookup.Ready:
                return image;
            case UiImageLookup.Pending:
                pending = true;
                return null;
            default:
                _manager.LogOnce($"spellicon:missing:{did:X8}", LogSeverity.Warning, $"CompositeSpellIcon: image 0x{did:X8} is missing; its layer is left out of the icon");
                return null;
        }
    }

    private static void Blit(UiImage source, UiSurface target, UiBlitMode mode) =>
        UiBlitter.Blit(source, 0, 0, target, 0, 0, Math.Min(Size, source.Width), Math.Min(Size, source.Height), mode, 1f);
}
