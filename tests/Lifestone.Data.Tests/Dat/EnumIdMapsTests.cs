using System.Collections.Concurrent;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests.Dat;

/// <summary>
/// The enum-map chain (<c>DBObj::GetDIDByEnum</c> → <c>DBCache::GetDIDFromEnum</c>) against the user's portal dat, with
/// the expected values of m2-log-in.md §4.2 and §5.1, and its rules on synthetic maps.
/// </summary>
public sealed class EnumIdMapsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private EnumIdMaps Maps => fixture.Dats.EnumMaps;

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_master_map_id_is_the_portal_headers()
    {
        Assert.Equal(EnumIdMaps.MasterMapId, (uint)fixture.Dats.Portal.Header.MasterMapId);
        Assert.Same(Maps, fixture.Dats.EnumMaps);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Each_group_constant_is_pinned_to_its_master_map_name_and_map()
    {
        var expected = new (EnumGroup Group, uint Value, string Name, uint MapId)[]
        {
            (EnumGroup.StringTables, 4, "STRINGTABLE", 0x25000004),
            (EnumGroup.UiLayout, 5, "UILAYOUT", 0x2500000E),
            (EnumGroup.UiCursor, 6, "UICURSOR", 0x2500000F),
            (EnumGroup.UiAsset, 7, "UIASSET", 0x25000010),
            (EnumGroup.Font, 9, "FONT", 0x25000012),
            (EnumGroup.KeyMap, 10, "KEYMAP", 0x25000013),
        };
        foreach (var (group, value, name, mapId) in expected)
        {
            Assert.Equal(value, group.Value);
            Assert.Equal(name, Maps.GroupName(group));
            Assert.Equal(mapId, Maps.GroupMapId(group));
            Assert.Equal($"{value} ({name})", group.ToString());
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_key_map_group_is_the_one_the_master_map_names_KEYMAP()
    {
        Assert.True(fixture.Dats.TryGetPortal<EnumIDMap>(EnumIdMaps.MasterMapId, out var master));
        Assert.Equal(22, master.ClientEnumToID.Count); // one entry per map, 0x25000000 Undef included as group 0
        var keyMap = Assert.Single(master.ClientEnumToName, kv => kv.Value.Value == "KEYMAP");
        Assert.Equal(EnumGroup.KeyMap.Value, keyMap.Key);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [InlineData(0x10000001u, 0x02000306u, "portalspace_background")] // the tunnel Setup
    [InlineData(0x10000002u, 0x030005ACu, "portalspace_animation")] // its spin
    [InlineData(0x10000003u, 0x2000004Bu, "UISoundTable")]
    [InlineData(0x10000004u, 0x0600127Eu, "PlayerIcon")]
    [InlineData(0x10000005u, 0x030003C0u, "PaperDollAnimation")]
    [InlineData(0x10000006u, 0x03000001u, "CharGenAnimation")]
    [InlineData(0x10000007u, 0x030000ACu, "CharGenAnimationWave")]
    [InlineData(0x10000008u, 0x03000540u, "CharGenAnimationYawn")]
    [InlineData(0x10000009u, 0x03000508u, "CharGenAnimationShrug")]
    [InlineData(0x1000000Au, 0x03000523u, "CharGenAnimationAkimbo")]
    [InlineData(0x1000000Bu, 0x0300052Eu, "CharGenAnimationTapFoot")]
    public void Ui_assets_resolve_as_m2_section_4_2_lists(uint enumValue, uint did, string name)
    {
        Assert.Equal(did, Maps.Resolve(EnumGroup.UiAsset, enumValue));
        Assert.True(Maps.TryResolve(EnumGroup.UiAsset, enumValue, out var tried));
        Assert.Equal(did, tried);
        Assert.Equal(name, Maps.NameOf(EnumGroup.UiAsset, enumValue));
        Assert.Equal(did, Maps.Entries(EnumGroup.UiAsset)[enumValue]);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [InlineData("patch", 0x21000000u)]
    [InlineData("disconnected", 0x21000002u)]
    [InlineData("charactermanagement", 0x21000004u)]
    [InlineData("chargen_master", 0x21000038u)]
    [InlineData("classic_smartbox", 0x2100000Fu)]
    public void Layouts_resolve_by_name(string name, uint layoutId)
    {
        Assert.True(Maps.TryFindByName(EnumGroup.UiLayout, name, out var enumValue));
        Assert.Equal(layoutId, Maps.Resolve(EnumGroup.UiLayout, enumValue));
        Assert.Equal(name, Maps.NameOf(EnumGroup.UiLayout, enumValue));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresLocalDatsFact]
    public void The_layout_group_names_81_layouts_all_in_the_language_dat()
    {
        // m2-log-in.md §5.1 counts 82 names: the map's 82 entries include Undef (enum 0 → DID 0), so 81 layouts.
        var entries = Maps.Entries(EnumGroup.UiLayout);
        Assert.Equal(81, entries.Count);
        Assert.Equal(81, entries.Values.Distinct().Count());
        Assert.Subset(fixture.Dats.GetAllLocalIds<LayoutDesc>().ToHashSet(), entries.Values.ToHashSet());
        Assert.False(Maps.TryResolve(EnumGroup.UiLayout, 0, out _));
        Assert.Equal("Undef", Maps.NameOf(EnumGroup.UiLayout, 0));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Cursors_fonts_string_tables_and_key_maps_resolve()
    {
        var cursors = Maps.Entries(EnumGroup.UiCursor);
        Assert.Equal(Enumerable.Range(1, 41).Select(i => (uint)i), cursors.Keys);
        Assert.Equal("Default", Maps.NameOf(EnumGroup.UiCursor, 1)); // gmUIFlow's default pointer
        Assert.Equal(0x06004D68u, cursors[1]);
        Assert.All(cursors.Values, did => Assert.Equal(0x06000000u, did & 0xFF000000u));

        // 48 font names for 39 fonts. All but one are among the portal's 49 Font records, 11 of which no name reaches; the
        // exception is the internal-only FixedSys_15 (The_dats_use_the_internal_fallback), whose Font is not in the dats.
        var fonts = Maps.Entries(EnumGroup.Font);
        Assert.Equal(48, fonts.Count);
        Assert.Equal(39, fonts.Values.Distinct().Count());
        var portalFonts = fixture.Dats.GetAllPortalIds<DatReaderWriter.DBObjs.Font>().ToHashSet();
        Assert.Equal(49, portalFonts.Count);
        Assert.Equal([0x40000033u], fonts.Values.Distinct().Where(did => !portalFonts.Contains(did)));
        Assert.Equal(11, portalFonts.Except(fonts.Values).Count());
        Assert.True(Maps.TryFindByName(EnumGroup.Font, "Chat_Tahoma_Small", out var chat));
        Assert.Equal(0x40000009u, Maps.Resolve(EnumGroup.Font, chat));

        var tables = Maps.Entries(EnumGroup.StringTables);
        Assert.Equal(14, tables.Count);
        Assert.True(Maps.TryFindByName(EnumGroup.StringTables, "UI", out var ui));
        Assert.Equal(0x23000001u, Maps.Resolve(EnumGroup.StringTables, ui));
        Assert.DoesNotContain(0x2300000Fu, tables.Values);

        // m2-log-in.md §5.1: gmDefaultMap is KEYMAP enum 0x10000001, MasterInputMap 0x14000000.
        Assert.Equal(0x14000000u, Maps.Resolve(EnumGroup.KeyMap, 0x10000001u));
        Assert.Equal("gmDefault", Maps.NameOf(EnumGroup.KeyMap, 0x10000001u));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_dats_use_the_internal_fallback()
    {
        // Only in m_EnumToIDInternal (DatReaderWriter's ServerEnumToID): found through the fallback.
        Assert.True(fixture.Dats.TryGetPortal<EnumIDMap>(0x25000013u, out var keyMaps));
        Assert.False(keyMaps.ClientEnumToID.ContainsKey(0x10000002u));
        Assert.Equal(0x14000001u, Maps.Resolve(EnumGroup.KeyMap, 0x10000002u));
        Assert.Equal("gmInternal", Maps.NameOf(EnumGroup.KeyMap, 0x10000002u));

        Assert.True(Maps.TryFindByName(EnumGroup.Font, "FixedSys_15", out var fixedSys));
        Assert.Equal(0x12u, fixedSys);
        Assert.Equal(0x40000033u, Maps.Resolve(EnumGroup.Font, fixedSys)); // a DID only: that Font is not in the portal
        Assert.False(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.Font>(0x40000033u, out _));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void An_unknown_enum_throws_naming_the_enum_and_the_group()
    {
        Assert.False(Maps.TryResolve(EnumGroup.UiAsset, 0x1000FFFFu, out var did));
        Assert.Equal(0u, did);
        var error = Assert.Throws<InvalidDataException>(() => Maps.Resolve(EnumGroup.UiAsset, 0x1000FFFFu));
        Assert.Contains("0x1000FFFF", error.Message, StringComparison.Ordinal);
        Assert.Contains("group 7 (UIASSET)", error.Message, StringComparison.Ordinal);
        Assert.Contains("0x25000010", error.Message, StringComparison.Ordinal);

        var noGroup = Assert.Throws<InvalidDataException>(() => Maps.Resolve(new EnumGroup(99), 1));
        Assert.Contains("0x00000001", noGroup.Message, StringComparison.Ordinal);
        Assert.Contains("group 99", noGroup.Message, StringComparison.Ordinal);
        Assert.Null(Maps.GroupMapId(new EnumGroup(99)));
        Assert.Empty(Maps.Entries(new EnumGroup(99)));
        Assert.Null(Maps.NameOf(new EnumGroup(99), 1));
    }

    [Fact]
    public void Lookups_read_the_enum_to_id_table_first_then_the_internal_one()
    {
        var maps = new EnumIdMaps(Synthetic().GetValueOrDefault);

        // The group itself is only in the master map's internal table.
        Assert.Equal(0x25000010u, maps.GroupMapId(EnumGroup.UiAsset));
        Assert.Equal("UIASSET", maps.GroupName(EnumGroup.UiAsset));

        Assert.Equal(0x02000001u, maps.Resolve(EnumGroup.UiAsset, 1)); // m_EnumToID shadows the internal entry
        Assert.Equal(0x02000002u, maps.Resolve(EnumGroup.UiAsset, 2)); // internal only
        Assert.False(maps.TryResolve(EnumGroup.UiAsset, 0, out _)); // Undef maps to INVALID_DID
        Assert.False(maps.TryResolve(EnumGroup.UiAsset, 3, out _));

        Assert.Equal(new Dictionary<uint, uint> { [1] = 0x02000001, [2] = 0x02000002 }, maps.Entries(EnumGroup.UiAsset));
        Assert.Equal("one", maps.NameOf(EnumGroup.UiAsset, 1));
        Assert.Equal("two", maps.NameOf(EnumGroup.UiAsset, 2));
        Assert.True(maps.TryFindByName(EnumGroup.UiAsset, "two", out var two));
        Assert.Equal(2u, two);
        Assert.False(maps.TryFindByName(EnumGroup.UiAsset, "TWO", out _)); // ordinal
    }

    [Fact]
    public void A_group_whose_map_is_missing_throws_naming_the_map()
    {
        var maps = new EnumIdMaps(Synthetic().GetValueOrDefault);
        Assert.Equal(0x2500000Eu, maps.GroupMapId(EnumGroup.UiLayout)); // named by the master map, absent from the "dat"
        Assert.False(maps.TryResolve(EnumGroup.UiLayout, 1, out _));
        var error = Assert.Throws<InvalidDataException>(() => maps.Resolve(EnumGroup.UiLayout, 1));
        Assert.Contains("0x2500000E is not in the portal dat", error.Message, StringComparison.Ordinal);
        Assert.Contains("group 5 (UILAYOUT)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_map_is_read_once_from_any_thread()
    {
        var source = Synthetic();
        var reads = new ConcurrentDictionary<uint, int>();
        var maps = new EnumIdMaps(did =>
        {
            reads.AddOrUpdate(did, 1, (_, n) => n + 1);
            return source.TryGetValue(did, out var map) ? map : null;
        });

        var results = new uint[400];
        Parallel.For(0, results.Length, i => results[i] = maps.TryResolve(i % 2 == 0 ? EnumGroup.UiAsset : EnumGroup.UiLayout, 1, out var d) ? d : 0);
        Assert.All(results.Where((_, i) => i % 2 == 0), d => Assert.Equal(0x02000001u, d));
        Assert.All(results.Where((_, i) => i % 2 == 1), d => Assert.Equal(0u, d));

        // 400 racing lookups read each DID exactly once, the absent UILAYOUT map (0x2500000E) included.
        Assert.Equal(new uint[] { 0x25000000, 0x2500000E, 0x25000010 }, reads.Keys.Order());
        Assert.All(reads.Values, n => Assert.Equal(1, n));

        _ = maps.Resolve(EnumGroup.UiAsset, 2);
        _ = maps.Entries(EnumGroup.UiAsset);
        _ = maps.GroupName(EnumGroup.UiLayout);
        Assert.False(maps.TryResolve(EnumGroup.UiLayout, 1, out _));
        Assert.All(reads.Values, n => Assert.Equal(1, n));
    }

    [Fact]
    public void Without_a_master_map_nothing_resolves()
    {
        var maps = new EnumIdMaps(_ => null);
        Assert.Null(maps.GroupMapId(EnumGroup.UiAsset));
        Assert.Null(maps.GroupName(EnumGroup.UiAsset));
        Assert.False(maps.TryResolve(EnumGroup.UiAsset, 1, out _));
        Assert.Contains("master EnumIDMap 0x25000000 has no group 7 (UIASSET)", Assert.Throws<InvalidDataException>(() => maps.Resolve(EnumGroup.UiAsset, 1)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A master map naming UILAYOUT (whose map is absent) and, in its internal table only, UIASSET, whose map has an
    /// Undef entry, an entry in both tables and one only in the internal table.
    /// </summary>
    private static Dictionary<uint, EnumIDMap> Synthetic() => new()
    {
        [EnumIdMaps.MasterMapId] = new EnumIDMap
        {
            Id = EnumIdMaps.MasterMapId,
            ClientEnumToID = { [0] = 0, [5] = 0x2500000E },
            ClientEnumToName = { [0] = "UNDEFINED", [5] = "UILAYOUT" },
            ServerEnumToID = { [7] = 0x25000010 },
            ServerEnumToName = { [7] = "UIASSET" },
        },
        [0x25000010] = new EnumIDMap
        {
            Id = 0x25000010,
            ClientEnumToID = { [0] = 0, [1] = 0x02000001 },
            ClientEnumToName = { [0] = "Undef", [1] = "one" },
            ServerEnumToID = { [1] = 0x0200FFFF, [2] = 0x02000002 },
            ServerEnumToName = { [1] = "shadowed", [2] = "two" },
        },
    };
}
