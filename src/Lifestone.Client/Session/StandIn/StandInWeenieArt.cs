using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>The art one weenie carries: its Setup and the icon fields of its CreateObject (DIDs only).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Setup">The weenie's Setup (0x02xxxxxx).</param>
/// <param name="Icon">The icon RenderSurface (0x06xxxxxx).</param>
/// <param name="Underlay">The icon underlay RenderSurface, or 0.</param>
/// <param name="Overlay">The icon overlay RenderSurface, or 0.</param>
/// <param name="Overlay2">The secondary icon overlay (a weenie property that retail draws on the appraisal, not a CreateObject field: the WeenieDesc has no field for it), or 0.</param>
internal readonly record struct StandInArt(uint Wcid, uint Setup, uint Icon, uint Underlay, uint Overlay, uint Overlay2);

/// <summary>
/// The art of the weenies the stand-in serves (M4 4.1 lane v4, D27): the Setup, Icon, IconUnderlay, IconOverlay and IconOverlaySecondary of each class id, so a stand-in item looks as the server's does instead of wearing the placeholder
/// icon 0x06000001. The rows are the world database's (the lead's read-only SELECT of the pinned ACE world, <c>cache/m4/4-1/weenie-icons.tsv</c>); they are identifiers only, no dat content is committed: the icon and Setup are looked up in the
/// user's own dats when drawn. A class id the table does not hold keeps the placeholder icon and logs <c>TODO(m4-standin-art-missing)</c> once with the class id (the stand-in never guesses an icon).
/// </summary>
internal static class StandInWeenieArt
{
    /// <summary>The placeholder icon every stand-in item wore before the table (a 1x1 surface of the dats).</summary>
    internal const uint PlaceholderIcon = 0x06000001;

    private static readonly ConcurrentDictionary<uint, byte> MissingLogged = new();

    private static readonly FrozenDictionary<uint, StandInArt> Table = new StandInArt[]
    {
        // the stand-in player (weenie 1, human)
        new(1, 0x02000001, 0x06001036, 0, 0, 0), // human
        // the wand and the leather set (GearKit)
        new(8552, 0x020009AD, 0x06001EED, 0, 0, 0), // orbhealer
        // M5 3 m1: the mage kit's vendor items and the purse's notes (GearKit.SupplyWeenies; the pinned world SQL v0.9.296 weenie_properties_d_i_d type 1 Setup and type 8 Icon)
        new(2472, 0x0200017C, 0x06001578, 0, 0, 0), // wand
        new(6071, 0x020001A6, 0x06001B98, 0, 0, 0), // robesuckwaraluvian
        new(5910, 0x020001A6, 0x06001BA3, 0, 0, 0), // robewargharundim
        new(5851, 0x020001A6, 0x06001B92, 0, 0, 0), // robealuvianhood (M5 7 F13: the hooded robe of the kit)
        new(56, 0x020000D8, 0x06000FCC, 0, 0, 0), // gauntletsleather (M5 7 F13: the gloves of the kit)
        new(2625, 0x02000155, 0x060016C9, 0, 0, 0), // tradenote10000
        new(2626, 0x02000155, 0x060016CA, 0, 0, 0), // tradenote50000
        new(20630, 0x02000155, 0x06002761, 0, 0, 0), // tradenote250000
        new(3715, 0x020000DA, 0x06000FCF, 0, 0, 0), // helmolthoi
        new(6600, 0x020001A6, 0x06001BE3, 0, 0, 0), // coatamullianshadowgreater
        new(6606, 0x020001A8, 0x06001BEB, 0, 0, 0), // leggingsamullianshadowgreater
        new(57, 0x020000D8, 0x06000FCD, 0, 0, 0), // gauntletsplatemail
        new(107, 0x020000DE, 0x06000FAD, 0, 0, 0), // sollerets
        // the foci and the taper and scarabs the kit and the death model name
        new(15268, 0x02000151, 0x0600207C, 0, 0x0600207C, 0), // packcreatureessence
        new(15269, 0x02000151, 0x06001F97, 0, 0x06001F97, 0), // packitemessence
        new(15270, 0x02000151, 0x060020C3, 0, 0x060020C3, 0), // packlifeessence
        new(15271, 0x02000151, 0x06001F64, 0, 0x06001F64, 0), // packwaressence
        new(43173, 0x02000151, 0x06001F64, 0, 0x06006E7A, 0), // packvoidessence
        new(20631, 0x020003F5, 0x0600262A, 0, 0, 0), // taperprismatic
        new(37155, 0x0200030B, 0x060067A5, 0, 0, 0), // scarabmana
        new(8897, 0x0200030B, 0x06001F61, 0, 0, 0), // scarabplatinum
        // the fighter kit and the vendors' catalogue
        new(331, 0x0200013A, 0x0600161B, 0, 0, 0), // mace
        new(307, 0x02000129, 0x06001599, 0, 0, 0), // bowshort
        new(300, 0x02000124, 0x060010E6, 0, 0, 0), // arrow
        new(628, 0x020002FA, 0x060032EF, 0, 0, 0), // healingkitcrude
        new(377, 0x020000AB, 0x060032D6, 0, 0, 0), // healthpotion
        new(2457, 0x020000AB, 0x060032D5, 0, 0, 0), // healthdraught
        new(2460, 0x020000AB, 0x060032E1, 0, 0, 0), // manadraught
        new(379, 0x020000AB, 0x060032E2, 0, 0, 0), // manapotion
        new(378, 0x020000AB, 0x060032DB, 0, 0, 0), // staminapotion
        new(27326, 0x020000AB, 0x060032DC, 0, 0, 0), // staminatincture
        new(629, 0x020002FA, 0x060032F0, 0, 0, 0), // healingkitplain
        new(2621, 0x02000155, 0x060016CB, 0, 0, 0), // tradenote100
        new(2622, 0x02000155, 0x060016CD, 0, 0, 0), // tradenote500
        new(309, 0x0200012B, 0x060015B7, 0, 0, 0), // club
        new(352, 0x02000148, 0x0600166B, 0, 0, 0), // swordshort
        new(36, 0x020000D1, 0x06000FE4, 0, 0, 0), // bracersleather
        new(115, 0x020000D0, 0x06000FAE, 0, 0, 0), // bootsleather
        new(130, 0x020000D4, 0x06000FF0, 0, 0, 0), // shirt
        new(258, 0x020000EB, 0x06001049, 0, 0, 0), // apple
        new(273, 0x02000B77, 0x0600229F, 0, 0, 0), // coinstack
        // the corpse's contents, the pack, the chest, the lifestone and the two vendors
        new(141, 0x020001F1, 0x060014C4, 0, 0, 0), // bowl
        new(2589, 0x020000D4, 0x06000FF0, 0, 0, 0), // smock
        new(136, 0x02000151, 0x06001BAF, 0, 0, 0), // backpack
        new(143, 0x0200007C, 0x06001022, 0, 0, 0), // chest
        new(715, 0x02000001, 0x06001036, 0, 0, 0), // holtburghealer
        new(718, 0x02000001, 0x06001036, 0, 0, 0), // holtburgshopkeep
        // the spell components (ComponentWeenies.All)
        new(691, 0x0200030B, 0x060013E7, 0, 0, 0), // scarablead
        new(689, 0x0200030B, 0x060013E6, 0, 0, 0), // scarabiron
        new(686, 0x0200030B, 0x060013E4, 0, 0, 0), // scarabcopper
        new(688, 0x0200030B, 0x060013E9, 0, 0, 0), // scarabsilver
        new(687, 0x0200030B, 0x060013E5, 0, 0, 0), // scarabgold
        new(690, 0x0200030B, 0x060013E8, 0, 0, 0), // scarabpyreal
        new(774, 0x02000181, 0x0600140A, 0, 0, 0), // hyssop
        new(775, 0x02000181, 0x0600140B, 0, 0, 0), // mandrake
        new(778, 0x02000181, 0x0600140F, 0, 0, 0), // saffron
        new(768, 0x02000181, 0x06001403, 0, 0, 0), // damiana
        new(776, 0x02000181, 0x0600140C, 0, 0, 0), // mugwort
        new(766, 0x02000181, 0x06001007, 0, 0, 0), // bistort
        new(780, 0x02000181, 0x06001410, 0, 0, 0), // wormwood
        new(625, 0x02000181, 0x06001407, 0, 0, 0), // ginseng
        new(772, 0x02000181, 0x06001408, 0, 0, 0), // hawthorn
        new(765, 0x02000181, 0x06001401, 0, 0, 0), // amaranth
        new(779, 0x02000181, 0x0600140E, 0, 0, 0), // vervain
        new(777, 0x02000181, 0x0600140D, 0, 0, 0), // myrrh
        new(770, 0x02000181, 0x06001405, 0, 0, 0), // eyebright
        new(771, 0x02000181, 0x06001406, 0, 0, 0), // frankincense
        new(769, 0x02000181, 0x06001404, 0, 0, 0), // dragonsblood
        new(773, 0x02000181, 0x06001409, 0, 0, 0), // henbane
        new(767, 0x02000181, 0x06001402, 0, 0, 0), // comfrey
        new(781, 0x02000181, 0x06001411, 0, 0, 0), // yarrow
        new(782, 0x02000308, 0x060013D9, 0, 0, 0), // agate
        new(783, 0x02000308, 0x060013DF, 0, 0, 0), // amber
        new(784, 0x02000308, 0x06001907, 0, 0, 0), // azurite
        new(785, 0x02000308, 0x060013DB, 0, 0, 0), // bloodstone
        new(786, 0x02000308, 0x060013E1, 0, 0, 0), // carnelian
        new(626, 0x02000308, 0x060013DD, 0, 0, 0), // hematite
        new(787, 0x02000308, 0x06001906, 0, 0, 0), // lapislazul
        new(788, 0x02000308, 0x06001908, 0, 0, 0), // malachite
        new(789, 0x02000308, 0x060013E2, 0, 0, 0), // moonstone
        new(790, 0x02000308, 0x060013D8, 0, 0, 0), // onyx
        new(791, 0x02000308, 0x06001909, 0, 0, 0), // quartz
        new(792, 0x02000308, 0x060013DA, 0, 0, 0), // turquoise
        new(753, 0x02000309, 0x060013D7, 0, 0, 0), // alchembrimstone
        new(754, 0x02000309, 0x060013D6, 0, 0, 0), // alchemcadmia
        new(755, 0x02000309, 0x060013D5, 0, 0, 0), // alchemcinnabar
        new(756, 0x02000309, 0x060013D0, 0, 0, 0), // alchemcobalt
        new(757, 0x02000309, 0x06001905, 0, 0, 0), // alchemcolcothar
        new(758, 0x02000309, 0x06001902, 0, 0, 0), // alchemgypsum
        new(759, 0x02000309, 0x060013D2, 0, 0, 0), // alchemquicksilver
        new(760, 0x02000309, 0x06001911, 0, 0, 0), // alchemrealgar
        new(761, 0x02000309, 0x06001904, 0, 0, 0), // alchemstibnite
        new(762, 0x02000309, 0x06001903, 0, 0, 0), // alchemturpeth
        new(763, 0x02000309, 0x060013D1, 0, 0, 0), // alchemverdigris
        new(764, 0x02000309, 0x06001912, 0, 0, 0), // alchemvitriol
        new(749, 0x02000307, 0x0600190C, 0, 0, 0), // poplartalisman
        new(742, 0x02000307, 0x060013EA, 0, 0, 0), // blackthorntalisman
        new(752, 0x02000307, 0x0600190B, 0, 0, 0), // yewtalisman
        new(747, 0x02000307, 0x0600190E, 0, 0, 0), // hemlocktalisman
        new(627, 0x02000307, 0x060013EC, 0, 0, 0), // aldertalisman
        new(744, 0x02000307, 0x0600190D, 0, 0, 0), // ebonytalisman
        new(741, 0x02000307, 0x06001910, 0, 0, 0), // birchtalisman
        new(748, 0x02000307, 0x060013EE, 0, 0, 0), // oaktalisman
        new(740, 0x02000307, 0x060013EF, 0, 0, 0), // ashwoodtalisman
        new(745, 0x02000307, 0x060013ED, 0, 0, 0), // eldertalisman
        new(746, 0x02000307, 0x060013EB, 0, 0, 0), // hazeltalisman
        new(750, 0x02000307, 0x0600190A, 0, 0, 0), // rowantalisman
        new(751, 0x02000307, 0x060013F0, 0, 0, 0), // willowtalisman
        new(743, 0x02000307, 0x0600190F, 0, 0, 0), // cedartalisman
        new(1650, 0x020003F5, 0x060013A6, 0, 0, 0), // taperred
        new(1649, 0x020003F5, 0x060013A5, 0, 0, 0), // taperpink
        new(1648, 0x020003F5, 0x060013A4, 0, 0, 0), // taperorange
        new(1653, 0x020003F5, 0x060013A9, 0, 0, 0), // taperyellow
        new(1645, 0x020003F5, 0x060013A1, 0, 0, 0), // tapergreen
        new(1654, 0x020003F5, 0x0600139F, 0, 0, 0), // taperturquoise
        new(1643, 0x020003F5, 0x0600139E, 0, 0, 0), // taperblue
        new(1647, 0x020003F5, 0x060013A3, 0, 0, 0), // taperindigo
        new(1651, 0x020003F5, 0x060013A7, 0, 0, 0), // taperviolet
        new(1644, 0x020003F5, 0x060013A0, 0, 0, 0), // taperbrown
        new(1652, 0x020003F5, 0x060013A8, 0, 0, 0), // taperwhite
        new(1646, 0x020003F5, 0x060013A2, 0, 0, 0), // tapergrey
        new(7299, 0x0200030B, 0x06001CE9, 0, 0, 0), // scarabdiamond
        new(7581, 0x02000309, 0x06001D0F, 0, 0, 0), // alchemchorizite
        new(9148, 0x02000309, 0x0600144D, 0, 0, 0), // alchemmoojuice
        new(25730, 0x02000307, 0x06003161, 0, 0, 0), // banyantalisman
    }.ToFrozenDictionary(static a => a.Wcid);

    /// <summary>The class ids the table holds.</summary>
    internal static IReadOnlyCollection<uint> Wcids => Table.Keys;

    /// <summary>The art of <paramref name="wcid"/>, or false when the table does not hold it.</summary>
    internal static bool TryGet(uint wcid, out StandInArt art) => Table.TryGetValue(wcid, out art);

    /// <summary>The weenie's own Setup, or <paramref name="fallback"/> when the table does not hold the class id.</summary>
    internal static uint SetupOr(uint wcid, uint fallback) => Table.TryGetValue(wcid, out var art) && art.Setup != 0 ? art.Setup : fallback;

    /// <summary>
    /// <paramref name="weenie"/> with the icon, icon underlay and icon overlay of its class id (and the header bits that announce them: IconOverlay in the first header; IconUnderlay in the second, announced by the object description flags). A class id
    /// the table does not hold keeps the description as it is (the placeholder icon) and logs <c>TODO(m4-standin-art-missing)</c> once per class id to <paramref name="log"/>.
    /// </summary>
    /// <param name="weenie">The stand-in item's description.</param>
    /// <param name="log">Where the missing-art TODO goes, or null.</param>
    internal static WeenieDesc Apply(WeenieDesc weenie, ILogSink? log)
    {
        ArgumentNullException.ThrowIfNull(weenie);
        if (!Table.TryGetValue(weenie.WeenieClassId, out var art) || art.Icon == 0)
        {
            if (MissingLogged.TryAdd(weenie.WeenieClassId, 0))
            {
                log?.Info(SessionLog.Category, "TODO(m4-standin-art-missing): weenie " + weenie.WeenieClassId.ToString(CultureInfo.InvariantCulture) + " (" + weenie.Name + ") is not in StandInWeenieArt; it keeps the placeholder icon 0x" + weenie.IconId.ToString("X8", CultureInfo.InvariantCulture));
            }

            return weenie;
        }

        var header = weenie.Header;
        var header2 = weenie.Header2;
        var flags = weenie.ObjectDescriptionFlags;
        if (art.Overlay != 0)
        {
            header |= WeenieHeaderFlags.IconOverlay;
        }

        if (art.Underlay != 0)
        {
            header2 |= WeenieHeaderFlags2.IconUnderlay;
            flags |= WeenieDesc.IncludesSecondHeader;
        }

        return weenie with
        {
            Header = header,
            Header2 = header2,
            ObjectDescriptionFlags = flags,
            IconId = art.Icon,
            IconOverlayId = art.Overlay != 0 ? art.Overlay : weenie.IconOverlayId,
            IconUnderlayId = art.Underlay != 0 ? art.Underlay : weenie.IconUnderlayId,
        };
    }
}
