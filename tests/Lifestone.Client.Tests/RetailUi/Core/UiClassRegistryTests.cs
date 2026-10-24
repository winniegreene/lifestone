using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// The class registry (m2-plan.md R10): retail's 105 registered classes pinned by id and name (regenerated from each
/// class's Register method: 21 in UIElementManager::Init, 84 in gmUIHelper::RegisterElements), and what creation does
/// for an implemented, a stand-in and an unknown class.
/// </summary>
public sealed class UiClassRegistryTests
{
    private static readonly (uint Id, string Name)[] Pinned =
    [
        (0x00000001, "UIElement_Button"),
        (0x00000002, "UIElement_Dragbar"),
        (0x00000003, "UIElement_Field"),
        (0x00000005, "UIElement_ListBox"),
        (0x00000006, "UIElement_Menu"),
        (0x00000007, "UIElement_Meter"),
        (0x00000008, "UIElement_Panel"),
        (0x00000009, "UIElement_Resizebar"),
        (0x0000000B, "UIElement_Scrollbar"),
        (0x0000000C, "UIElement_Text"),
        (0x0000000D, "UIElement_Viewport"),
        (0x0000000E, "UIElement_Browser"),
        (0x00000010, "UIElement_ColorPicker"),
        (0x00000011, "UIElement_GroupBox"),
        (0x00000013, "ConfirmationDialog"),
        (0x00000014, "ConfirmationMenuDialog"),
        (0x00000015, "ConfirmationTextInputDialog"),
        (0x00000016, "MenuDialog"),
        (0x00000017, "MessageDialog"),
        (0x00000018, "TextInputDialog"),
        (0x00000019, "WaitDialog"),
        (0x10000001, "gmUIElement_BurdenIndicator"),
        (0x10000002, "gmUIElement_EffectsIndicator"),
        (0x10000003, "gmUIElement_LinkStatusIndicator"),
        (0x10000004, "gmUIElement_MiniGameIndicator"),
        (0x10000005, "gmUIElement_PortalStormIndicator"),
        (0x10000006, "gmUIElement_VitaeIndicator"),
        (0x10000007, "gmToolbarUI"),
        (0x10000008, "gmPanelUI"),
        (0x10000009, "gmVitalsUI"),
        (0x1000000A, "gmIndicatorsUI"),
        (0x1000000C, "gmCombatUI"),
        (0x1000000D, "gmExternalContainerUI"),
        (0x1000000E, "gmKeyboardUI"),
        (0x1000000F, "gmPowerbarUI"),
        (0x10000010, "gmRadarUI"),
        (0x10000011, "gmSalvageUI"),
        (0x10000012, "gmSecureTradeUI"),
        (0x10000013, "gmSlumlordUI"),
        (0x10000014, "gmSmartBoxUI"),
        (0x10000015, "gmSpellcastingUI"),
        (0x10000016, "gmSpewBoxUI"),
        (0x10000017, "gmVendorUI"),
        (0x10000018, "gmAbuseUI"),
        (0x10000019, "gmBookUI"),
        (0x1000001A, "gmCharacterInfoUI"),
        (0x1000001B, "gmEffectsUI"),
        (0x1000001C, "gmExaminationUI"),
        (0x1000001D, "gmLinkStatusUI"),
        (0x1000001E, "gmMiniGameUI"),
        (0x1000001F, "gmUrgentAssistanceUI"),
        (0x10000020, "gmVitaeUI"),
        (0x10000021, "gm3DItemsUI"),
        (0x10000022, "gmBackpackUI"),
        (0x10000023, "gmInventoryUI"),
        (0x10000024, "gmPaperDollUI"),
        (0x10000025, "gmHouseUI"),
        (0x10000026, "gmMapUI"),
        (0x10000027, "gmCharacterSettingsUI"),
        (0x10000028, "gmConfigUI"),
        (0x10000029, "gmGameplayOptionsUI"),
        (0x1000002A, "gmAttributeUI"),
        (0x1000002B, "gmSkillUI"),
        (0x1000002C, "gmAllegianceUI"),
        (0x1000002D, "gmFellowshipUI"),
        (0x1000002E, "gmSpellbookUI"),
        (0x1000002F, "gmSpellComponentUI"),
        (0x10000030, "UIElement_SmartBoxWrapper"),
        (0x10000031, "UIElement_ItemList"),
        (0x10000032, "UIElement_UIItem"),
        (0x10000033, "gmEnvPanelUI"),
        (0x10000034, "UIOption_ActionKeyMap"),
        (0x10000035, "UIOption_Checkbox"),
        (0x10000036, "UIOption_CheckboxSlider"),
        (0x10000037, "UIOption_Slider"),
        (0x10000038, "UIOption_Menu"),
        (0x10000039, "gmCGHeritagePage"),
        (0x1000003A, "gmCGProfessionPage"),
        (0x1000003B, "gmCGSkillsPage"),
        (0x1000003C, "gmCGAppearancePage"),
        (0x1000003D, "gmCGTownPage"),
        (0x1000003E, "gmCGSummaryPage"),
        (0x1000003F, "gmAdminQualitiesUI"),
        (0x10000040, "gmFloatyChatUI"),
        (0x10000041, "gmMainChatUI"),
        (0x10000042, "gmChatOptionsUI"),
        (0x10000043, "UIOption_CheckboxBitfield"),
        (0x10000044, "UIOption_CheckboxBitfield64"),
        (0x10000045, "gmFriendsUI"),
        (0x10000046, "gmCharacterTitleUI"),
        (0x10000047, "gmSquelchUI"),
        (0x10000048, "gmJournalUI"),
        (0x10000049, "gmPageListUI"),
        (0x1000004A, "gmBarberUI"),
        (0x1000004B, "gmContractsUI"),
        (0x1000004C, "gmFloatyExaminationUI"),
        (0x1000004D, "gmFloatyVitalsUI"),
        (0x1000004E, "gmFloatyEnvPanelUI"),
        (0x1000004F, "gmFloatyPanelUI"),
        (0x10000050, "gmFloatyMainChatUI"),
        (0x10000051, "gmFloatyToolbarUI"),
        (0x10000052, "gmFloatyIndicatorsUI"),
        (0x10000053, "gmFloatyPowerBarUI"),
        (0x10000054, "gmFloatyCombatPanelUI"),
        (0x10000056, "gmFloatySideVitalsUI"),
    ];

    [Fact]
    public void The_retail_class_table_is_pinned_id_for_id()
    {
        Assert.Equal(105, UiClassRegistry.RetailClassNames.Count);
        Assert.Equal(Pinned.Length, UiClassRegistry.RetailClassNames.Count);
        foreach (var (id, name) in Pinned)
        {
            Assert.True(UiClassRegistry.RetailClassNames.TryGetValue(id, out var actual), $"class 0x{id:X8} missing");
            Assert.Equal(name, actual);
        }

        Assert.Equal(21, UiClassRegistry.RetailClassNames.Keys.Count(id => id < 0x20));
        Assert.Equal(84, UiClassRegistry.RetailClassNames.Keys.Count(id => id >= 0x10000000));
    }

    [Fact]
    public void Ids_retail_never_registered_are_named_as_such()
    {
        Assert.Equal("class 0x00000004", UiClassRegistry.NameOf(4));
        Assert.Equal("class 0x10000055", UiClassRegistry.NameOf(0x10000055));
    }

    [Fact]
    public void The_default_registry_implements_every_engine_class_and_stands_in_for_game_classes_once_loudly()
    {
        var log = new RecordingLogSink();
        var root = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, children:
        [
            UiDescs.Element(2, UiClassIds.Text, 0, 0, 10, 10),
            UiDescs.Element(3, UiClassIds.SpewBox, 0, 0, 10, 10),
            UiDescs.Element(4, UiClassIds.SpewBox, 0, 0, 10, 10),
            UiDescs.Element(5, 4, 0, 0, 10, 10),
            UiDescs.Element(6, UiClassIds.Button, 0, 0, 10, 10),
        ]);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(0x21000001, 800, 600, root)]), Log = log });

        var created = manager.CreateRootElementByDataId(0x21000001, 1)!;

        Assert.False(created.IsStandIn);
        Assert.IsType<UiField>(created);
        Assert.IsType<UiTextElement>(created.GetChild(2));
        Assert.True(created.GetChild(3)!.IsStandIn);
        Assert.True(created.GetChild(4)!.IsStandIn);
        Assert.Null(created.GetChild(5));
        Assert.IsType<UiButton>(created.GetChild(6));
        Assert.Single(log.Lines, l => l.Contains("class 0x10000016 (gmSpewBoxUI) has no Lifestone runtime yet", StringComparison.Ordinal));
        Assert.Single(log.Lines, l => l.Contains("class 0x00000004, which retail never registers", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_registry_implements_all_21_engine_classes_and_no_game_class()
    {
        var registry = UiClassRegistry.CreateDefault();

        foreach (var (id, name) in UiClassRegistry.RetailClassNames)
        {
            Assert.True(registry.IsImplemented(id) == id < 0x20, $"{name} 0x{id:X8}");
        }

        Assert.Equal(21, UiClassRegistry.RetailClassNames.Keys.Count(id => id < 0x20));
    }

    [Theory]
    [InlineData(UiClassIds.Button, typeof(UiButton))]
    [InlineData(UiClassIds.Dragbar, typeof(UiDragbar))]
    [InlineData(UiClassIds.Field, typeof(UiField))]
    [InlineData(UiClassIds.ListBox, typeof(UiListBox))]
    [InlineData(UiClassIds.Menu, typeof(UiMenu))]
    [InlineData(UiClassIds.Meter, typeof(UiMeter))]
    [InlineData(UiClassIds.Panel, typeof(UiPanel))]
    [InlineData(UiClassIds.ResizeBar, typeof(UiResizeBar))]
    [InlineData(UiClassIds.Scrollbar, typeof(UiScrollbar))]
    [InlineData(UiClassIds.Text, typeof(UiTextElement))]
    [InlineData(UiClassIds.Viewport, typeof(UiViewport))]
    [InlineData(UiClassIds.Browser, typeof(UiBrowser))]
    [InlineData(UiClassIds.ColorPicker, typeof(UiColorPicker))]
    [InlineData(UiClassIds.GroupBox, typeof(UiGroupBox))]
    [InlineData(UiClassIds.ConfirmationDialog, typeof(UiConfirmationDialog))]
    [InlineData(UiClassIds.ConfirmationMenuDialog, typeof(UiConfirmationMenuDialog))]
    [InlineData(UiClassIds.ConfirmationTextInputDialog, typeof(UiConfirmationTextInputDialog))]
    [InlineData(UiClassIds.MenuDialog, typeof(UiMenuDialog))]
    [InlineData(UiClassIds.MessageDialog, typeof(UiMessageDialog))]
    [InlineData(UiClassIds.TextInputDialog, typeof(UiTextInputDialog))]
    [InlineData(UiClassIds.WaitDialog, typeof(UiWaitDialog))]
    public void Each_engine_class_creates_its_runtime(uint classId, Type expected)
    {
        var root = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, children: [UiDescs.Element(2, classId, 0, 0, 10, 10)]);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(0x21000001, 800, 600, root)]) });

        var created = manager.CreateRootElementByDataId(0x21000001, 1)!;

        Assert.IsType(expected, created.GetChild(2));
        Assert.False(created.GetChild(2)!.IsStandIn);
    }

    [Fact]
    public void A_registered_factory_replaces_the_stand_in()
    {
        var registry = UiClassRegistry.CreateDefault();
        registry.Register(UiClassIds.Button, static (in UiElementInit init) => new MarkerElement(init));
        var root = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, children: [UiDescs.Element(2, UiClassIds.Button, 0, 0, 10, 10)]);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(0x21000001, 800, 600, root)]) }, registry: registry);

        var created = manager.CreateRootElementByDataId(0x21000001, 1)!;

        Assert.IsType<MarkerElement>(created.GetChild(2));
        Assert.True(registry.IsImplemented(UiClassIds.Button));
    }

    private sealed class MarkerElement(in UiElementInit init) : UiElement(init);
}

/// <summary>Every class the retail layouts use is a class retail registers (R10, against the user's dats).</summary>
public sealed class UiClassRegistryDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_class_the_resolved_layouts_create_is_registered_in_retail()
    {
        var layouts = new UiLayouts(fixture.Dats);
        var used = new SortedSet<uint>();

        void Visit(UiElementDesc desc)
        {
            var result = layouts.InqFullDesc(desc);
            if (!result.Succeeded)
            {
                return;
            }

            used.Add(result.Full!.Type);
            foreach (var child in result.Full.Children.Values)
            {
                Visit(child);
            }
        }

        foreach (var id in layouts.LayoutIds)
        {
            foreach (var top in layouts.GetLayout(id).Elements.Values)
            {
                Visit(top);
            }
        }

        var unknown = used.Where(c => !UiClassRegistry.RetailClassNames.ContainsKey(c)).Select(c => $"0x{c:X8}").ToList();
        Assert.Empty(unknown);
        Assert.Equal(100, used.Count);
    }
}
