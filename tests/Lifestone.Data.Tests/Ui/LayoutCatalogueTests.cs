using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// The layout catalogue (m2-plan.md R2): the UILAYOUT chains retail code follows, the mode roots, the dialog layout, and
/// what the gameplay root holds (input to decision D36).
/// </summary>
public sealed class LayoutCatalogueTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    private UiLayouts Layouts => new(Dats);

    [RequiresLocalDatsFact]
    public void The_UILAYOUT_group_names_81_layouts_and_20_are_templates_only()
    {
        var layouts = Layouts;
        var entries = Dats.EnumMaps.Entries(EnumGroup.UiLayout);

        Assert.Equal(101, layouts.LayoutIds.Count);
        Assert.Equal(81, entries.Count);
        Assert.Equal(81, entries.Values.Distinct().Count());
        Assert.Subset(layouts.LayoutIds.ToHashSet(), entries.Values.ToHashSet());
        Assert.Equal(20, layouts.LayoutIds.Except(entries.Values).Count()); // reached only as base layouts

        // The map also names its Undef enum 0, which resolves to nothing: 82 names for 81 layouts (m2-log-in.md §5.1).
        Assert.True(Dats.TryGetPortal<EnumIDMap>(0x2500000Eu, out var map));
        Assert.Equal(82, map.ClientEnumToName.Count + map.ServerEnumToName.Count);
        Assert.False(layouts.TryResolveLayoutEnum(0, out _));
    }

    [RequiresLocalDatsTheory]
    [InlineData(UiLayoutEnums.ContextMenu, 0x2100003Bu, "ContextMenu")]
    [InlineData(UiLayoutEnums.Dialog, 0x2100003Cu, "Dialog")]
    [InlineData(UiLayoutEnums.Patch, 0x21000000u, "patch")]
    [InlineData(UiLayoutEnums.Disconnected, 0x21000002u, "disconnected")]
    [InlineData(UiLayoutEnums.CharacterManagement, 0x21000004u, "charactermanagement")]
    [InlineData(UiLayoutEnums.ClassicGameplay, 0x21000005u, "classic_gameplay")]
    [InlineData(UiLayoutEnums.ClassicSmartBox, 0x2100000Fu, "classic_smartbox")]
    [InlineData(UiLayoutEnums.ChargenMaster, 0x21000038u, "chargen_master")]
    [InlineData(0x10000007u, 0x21000006u, "classic_chat")]
    [InlineData(0x1000000Cu, 0x2100000Bu, "classic_radar")]
    [InlineData(0x10000015u, 0x21000014u, "classic_vitals")]
    [InlineData(0x10000017u, 0x21000016u, "classic_toolbar")]
    [InlineData(0x10000018u, 0x21000017u, "classic_panel")]
    [InlineData(0x1000001Bu, 0x2100001Au, "classic_characterinfo")]
    public void Layout_enums_resolve_as_retail_names_them(uint layoutEnum, uint layoutId, string name)
    {
        var layouts = Layouts;

        Assert.Equal(layoutId, layouts.ResolveLayoutEnum(layoutEnum));
        Assert.Equal(name, Dats.EnumMaps.NameOf(EnumGroup.UiLayout, layoutEnum));
        var layout = layouts.GetLayout(layoutId);
        Assert.Same(layout, layouts.GetLayout(layoutId)); // converted once
        Assert.Equal((layoutId, 800, 600), (layout.Id, layout.Width, layout.Height));
    }

    [RequiresLocalDatsFact]
    public void Unknown_enums_layouts_and_elements_fail_loudly()
    {
        var layouts = Layouts;

        var error = Assert.Throws<InvalidDataException>(() => layouts.ResolveLayoutEnum(0x10000FFFu));
        Assert.Equal("TODO(retailui-data): UILAYOUT enum 0x10000FFF does not resolve to a layout.", error.Message);
        Assert.False(layouts.TryGetLayout(0x21FFFFFFu, out _));
        error = Assert.Throws<InvalidDataException>(() => layouts.GetLayout(0x21FFFFFFu));
        Assert.Equal("TODO(retailui-data): LayoutDesc 0x21FFFFFF is not in client_local_English.dat.", error.Message);
        Assert.Null(layouts.AccessElementDesc(0x21FFFFFFu, 1));
        Assert.Null(layouts.AccessElementDesc(0x21000004u, 0x1000039Du)); // a child of the root, not top-level
        error = Assert.Throws<InvalidDataException>(() => layouts.GetRootDesc(new UiRootRef(UiLayoutEnums.CharacterManagement, 0x1000039Du)));
        Assert.Contains("layout 0x21000004 (UILAYOUT enum 0x10000005) has no top-level element 0x1000039D", error.Message, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void Every_mode_root_is_a_top_level_element_of_its_layout()
    {
        var layouts = Layouts;
        var roots = new[]
        {
            (UiLayoutEnums.DataPatchRoot, 0x21000000u),
            (UiLayoutEnums.DisconnectedRoot, 0x21000002u),
            (UiLayoutEnums.CharacterManagementRoot, 0x21000004u),
            (UiLayoutEnums.CharGenRoot, 0x21000038u),
            (UiLayoutEnums.GameplayRoot, 0x21000005u),
        };
        foreach (var (root, layoutId) in roots)
        {
            // Each a full-screen Field (class 3), the container every mode's screen is built in.
            var desc = layouts.GetRootDesc(root);
            Assert.Same(desc, layouts.AccessElementDesc(layoutId, root.ElementId));
            Assert.True((3u, layoutId) == (desc.Type, desc.SourceLayout), $"{root}: class 0x{desc.Type:X}");
            Assert.Equal(new UiBox(0, 0, 799, 599), desc.Box);
        }

        // Character management (retail-exact, no 3D view; D35): a full-screen Field with eight children.
        var characterManagement = layouts.GetRootDesc(UiLayoutEnums.CharacterManagementRoot);
        Assert.Equal(8, characterManagement.Children.Count);
        Assert.Equal(2, layouts.GetLayout(0x21000004u).Elements.Count);
    }

    [RequiresLocalDatsFact]
    public void The_dialog_layout_holds_one_full_screen_element_per_dialog_class()
    {
        var layouts = Layouts;
        var dialog = layouts.GetLayout(layouts.ResolveLayoutEnum(UiLayoutEnums.Dialog));

        Assert.Equal(0x2100003Cu, dialog.Id);
        Assert.Equal(
            "0x15:0x13 0x1B:0x16 0x1F:0x14 0x24:0x17 0x2C:0x15 0x31:0x19",
            string.Join(" ", dialog.Elements.Values.OrderBy(e => e.ElementId).Select(e => $"0x{e.ElementId:X}:0x{e.Type:X}")));
        Assert.All(dialog.Elements.Values, e =>
        {
            Assert.Equal(new UiBox(0, 0, 799, 599), e.Box);
            Assert.Equal(new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide), e.Edges);
            var frame = Assert.Single(e.Children.Values); // the dialog's frame, a partial desc of a shared template
            Assert.Equal((0x3Du, 0u, 0x21000042u, 0x100002AFu), (frame.ElementId, frame.Type, frame.BaseLayout, frame.BaseElement));
        });
    }

    [RequiresLocalDatsFact]
    public void The_gameplay_root_holds_the_floating_window_shell()
    {
        var layouts = Layouts;
        var root = layouts.GetRootDesc(UiLayoutEnums.GameplayRoot);

        Assert.Equal((3u, 1u), (root.Type, root.BaseState.ZLevel));
        Assert.Equal([0x10000063u, 0x10000064u], root.States.Keys);
        Assert.Equal(18, root.Children.Count);
        Assert.All(root.Children.Values, c => Assert.Equal(0u, c.Type)); // every window is a partial desc

        string BaseName(UiElementDesc c) =>
            Dats.EnumMaps.Entries(EnumGroup.UiLayout).Where(kv => kv.Value == c.BaseLayout)
                .Select(kv => Dats.EnumMaps.NameOf(EnumGroup.UiLayout, kv.Key)).SingleOrDefault() ?? $"0x{c.BaseLayout:X8}";
        var bases = root.Children.Values.ToDictionary(c => c.ElementId, BaseName);

        Assert.Equal("classic_smartbox", bases[UiLayoutEnums.Gameplay.SmartBox]);
        Assert.Equal("classic_floatyradar", bases[UiLayoutEnums.Gameplay.Radar]);
        Assert.Equal("classic_floatyvitals", bases[UiLayoutEnums.Gameplay.StackedVitals]);
        Assert.Equal("classic_floatysidevitals", bases[UiLayoutEnums.Gameplay.SideBySideVitals]);
        Assert.All(UiLayoutEnums.Gameplay.FloatingChats, id => Assert.Equal("0x2100005B", bases[id]));
        Assert.Equal(
            ["0x2100005B", "classic_admin", "classic_floatycombatpanel", "classic_floatyenvpanel", "classic_floatyexamination",
             "classic_floatyindicators", "classic_floatymainchat", "classic_floatypanel", "classic_floatypowerbar",
             "classic_floatyradar", "classic_floatysidevitals", "classic_floatytoolbar", "classic_floatyvitals",
             "classic_keyboard", "classic_smartbox"],
            bases.Values.Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(11, bases.Values.Count(n => n.StartsWith("classic_floaty", StringComparison.Ordinal)));
        Assert.DoesNotContain(bases.Values, n => n is "classic_chat" or "classic_radar" or "classic_vitals" or "classic_toolbar" or "classic_panel");
    }

    [RequiresDatsFact] // models the machine without the language dat, so it needs only portal and cell
    public void Without_the_language_dat_the_layouts_name_the_missing_file()
    {
        var dir = Dats.Directory;
        using var dats = DatSet.Open(new DatSetPaths(
            dir, Path.Combine(dir, DatSet.PortalFileName), Path.Combine(dir, DatSet.CellFileName), Local: null, HighRes: null));

        var error = Assert.Throws<InvalidOperationException>(() => new UiLayouts(dats));
        Assert.StartsWith("The retail interface needs the retail language dat 'client_local_English.dat'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Layouts_from_memory_resolve_enums_and_top_level_elements()
    {
        var child = new UiElementDesc { ElementId = 2, Type = 5, SourceLayout = 0x21000001u };
        var top = new UiElementDesc
        {
            ElementId = 1,
            Type = 3,
            SourceLayout = 0x21000001u,
            Children = UiOrdered.Dictionary([(2u, child)], k => new InvalidDataException()),
        };
        var layouts = UiLayouts.FromLayouts(
            [new UiLayout(0x21000001u, 800, 600, UiOrdered.Dictionary([(1u, top)], k => new InvalidDataException()))],
            new Dictionary<uint, uint> { [0x10000001u] = 0x21000001u });

        Assert.Equal([0x21000001u], layouts.LayoutIds);
        Assert.Same(top, layouts.GetRootDesc(new UiRootRef(0x10000001u, 1)));
        Assert.Null(layouts.AccessElementDesc(0x21000001u, 2));
        Assert.Equal("0x10000001/0x00000001", new UiRootRef(0x10000001u, 1).ToString());
    }
}
