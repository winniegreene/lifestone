using System.Collections.Concurrent;
using System.Collections.Frozen;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>What a class factory gets to build an element: the manager, the layout the desc came from, and the full desc.</summary>
/// <param name="Manager">The element manager creating the element.</param>
/// <param name="Layout">The layout the element is created from (retail's <c>m_layout</c>); its design size anchors roots.</param>
/// <param name="Desc">The element's full desc (<c>LayoutDesc::InqFullDesc</c> already applied).</param>
public readonly record struct UiElementInit(UiElementManager Manager, UiLayout Layout, UiElementDesc Desc);

/// <summary>Creates the element of one UI class (retail's per-class <c>Create</c> method).</summary>
public delegate UiElement UiElementFactory(in UiElementInit init);

/// <summary>Well-known retail UI class ids (an ElementDesc's <c>m_type</c>).</summary>
public static class UiClassIds
{
    /// <summary><c>UIElement_Button</c>.</summary>
    public const uint Button = 0x01;

    /// <summary><c>UIElement_Dragbar</c>.</summary>
    public const uint Dragbar = 0x02;

    /// <summary><c>UIElement_Field</c>: the plain container every mode root is.</summary>
    public const uint Field = 0x03;

    /// <summary><c>UIElement_ListBox</c>.</summary>
    public const uint ListBox = 0x05;

    /// <summary><c>UIElement_Menu</c>.</summary>
    public const uint Menu = 0x06;

    /// <summary><c>UIElement_Meter</c>.</summary>
    public const uint Meter = 0x07;

    /// <summary><c>UIElement_Panel</c>.</summary>
    public const uint Panel = 0x08;

    /// <summary><c>UIElement_Resizebar</c>.</summary>
    public const uint ResizeBar = 0x09;

    /// <summary><c>UIElement_Scrollbar</c>.</summary>
    public const uint Scrollbar = 0x0B;

    /// <summary><c>UIElement_Text</c>.</summary>
    public const uint Text = 0x0C;

    /// <summary><c>UIElement_Viewport</c>: a hole the host fills with a 3D view.</summary>
    public const uint Viewport = 0x0D;

    /// <summary><c>UIElement_Browser</c> (a loud stub).</summary>
    public const uint Browser = 0x0E;

    /// <summary><c>UIElement_ColorPicker</c>.</summary>
    public const uint ColorPicker = 0x10;

    /// <summary><c>UIElement_GroupBox</c>.</summary>
    public const uint GroupBox = 0x11;

    /// <summary><c>ConfirmationDialog</c>.</summary>
    public const uint ConfirmationDialog = 0x13;

    /// <summary><c>ConfirmationMenuDialog</c>.</summary>
    public const uint ConfirmationMenuDialog = 0x14;

    /// <summary><c>ConfirmationTextInputDialog</c>.</summary>
    public const uint ConfirmationTextInputDialog = 0x15;

    /// <summary><c>MenuDialog</c>.</summary>
    public const uint MenuDialog = 0x16;

    /// <summary><c>MessageDialog</c>.</summary>
    public const uint MessageDialog = 0x17;

    /// <summary><c>TextInputDialog</c>.</summary>
    public const uint TextInputDialog = 0x18;

    /// <summary><c>WaitDialog</c>.</summary>
    public const uint WaitDialog = 0x19;

    /// <summary><c>gmUIElement_LinkStatusIndicator</c> (<c>Register</c>, 11.6096 0x004E73A0).</summary>
    public const uint LinkStatusIndicator = 0x10000003;

    /// <summary><c>gmRadarUI</c>.</summary>
    public const uint Radar = 0x10000010;

    /// <summary><c>gmSmartBoxUI</c>.</summary>
    public const uint SmartBox = 0x10000014;

    /// <summary><c>gmSpewBoxUI</c>.</summary>
    public const uint SpewBox = 0x10000016;
}

/// <summary>
/// The UI classes an element can be: retail's class-create table, filled by <c>UIElement::RegisterElementClass</c>
/// (11.4186 0x00460190, 11.6096 0x00460270) from the 21 engine registrations in <c>UIElementManager::Init</c>
/// (11.4186 0x0045EE10, 11.6096 0x0045EEF0) and the 84 game registrations in <c>gmUIHelper::RegisterElements</c>
/// (11.4186 0x0047A460, 11.6096 0x0047A860). Each registration's class id comes from that class's own
/// <c>Register</c> method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Creation</b> follows <c>UIElementManager::CreateElementRecursiveFromFullDesc</c> (11.4186 0x0045B0C0, 11.6096
/// 0x0045B1A0): the desc's class picks the create method; a class with none (retail falls back to the desc's engine
/// type, which the serializer never sets) creates nothing.
/// </para>
/// <para>
/// Lifestone implements the retail classes one by one. A retail class with no Lifestone runtime yet gets a generic
/// <see cref="UiElement"/> (it still lays out, takes states and draws its media, which is what most container classes
/// do) and one loud log line per class (<see cref="UiElement.IsStandIn"/>); a class id retail never registered creates
/// nothing, with an error, as retail would.
/// </para>
/// </remarks>
public sealed class UiClassRegistry
{
    /// <summary>
    /// Every class retail registers, by id: the 21 engine classes (ids below 0x20) and the 84 game classes
    /// (0x10000001-0x10000056). Pinned by UiClassRegistryTests.
    /// </summary>
    public static readonly FrozenDictionary<uint, string> RetailClassNames = new Dictionary<uint, string>
    {
        // UIElementManager::Init.
        [0x00000001] = "UIElement_Button",
        [0x00000002] = "UIElement_Dragbar",
        [0x00000003] = "UIElement_Field",
        [0x00000005] = "UIElement_ListBox",
        [0x00000006] = "UIElement_Menu",
        [0x00000007] = "UIElement_Meter",
        [0x00000008] = "UIElement_Panel",
        [0x00000009] = "UIElement_Resizebar",
        [0x0000000B] = "UIElement_Scrollbar",
        [0x0000000C] = "UIElement_Text",
        [0x0000000D] = "UIElement_Viewport",
        [0x0000000E] = "UIElement_Browser",
        [0x00000010] = "UIElement_ColorPicker",
        [0x00000011] = "UIElement_GroupBox",
        [0x00000013] = "ConfirmationDialog",
        [0x00000014] = "ConfirmationMenuDialog",
        [0x00000015] = "ConfirmationTextInputDialog",
        [0x00000016] = "MenuDialog",
        [0x00000017] = "MessageDialog",
        [0x00000018] = "TextInputDialog",
        [0x00000019] = "WaitDialog",

        // gmUIHelper::RegisterElements.
        [0x10000001] = "gmUIElement_BurdenIndicator",
        [0x10000002] = "gmUIElement_EffectsIndicator",
        [0x10000003] = "gmUIElement_LinkStatusIndicator",
        [0x10000004] = "gmUIElement_MiniGameIndicator",
        [0x10000005] = "gmUIElement_PortalStormIndicator",
        [0x10000006] = "gmUIElement_VitaeIndicator",
        [0x10000007] = "gmToolbarUI",
        [0x10000008] = "gmPanelUI",
        [0x10000009] = "gmVitalsUI",
        [0x1000000A] = "gmIndicatorsUI",
        [0x1000000C] = "gmCombatUI",
        [0x1000000D] = "gmExternalContainerUI",
        [0x1000000E] = "gmKeyboardUI",
        [0x1000000F] = "gmPowerbarUI",
        [0x10000010] = "gmRadarUI",
        [0x10000011] = "gmSalvageUI",
        [0x10000012] = "gmSecureTradeUI",
        [0x10000013] = "gmSlumlordUI",
        [0x10000014] = "gmSmartBoxUI",
        [0x10000015] = "gmSpellcastingUI",
        [0x10000016] = "gmSpewBoxUI",
        [0x10000017] = "gmVendorUI",
        [0x10000018] = "gmAbuseUI",
        [0x10000019] = "gmBookUI",
        [0x1000001A] = "gmCharacterInfoUI",
        [0x1000001B] = "gmEffectsUI",
        [0x1000001C] = "gmExaminationUI",
        [0x1000001D] = "gmLinkStatusUI",
        [0x1000001E] = "gmMiniGameUI",
        [0x1000001F] = "gmUrgentAssistanceUI",
        [0x10000020] = "gmVitaeUI",
        [0x10000021] = "gm3DItemsUI",
        [0x10000022] = "gmBackpackUI",
        [0x10000023] = "gmInventoryUI",
        [0x10000024] = "gmPaperDollUI",
        [0x10000025] = "gmHouseUI",
        [0x10000026] = "gmMapUI",
        [0x10000027] = "gmCharacterSettingsUI",
        [0x10000028] = "gmConfigUI",
        [0x10000029] = "gmGameplayOptionsUI",
        [0x1000002A] = "gmAttributeUI",
        [0x1000002B] = "gmSkillUI",
        [0x1000002C] = "gmAllegianceUI",
        [0x1000002D] = "gmFellowshipUI",
        [0x1000002E] = "gmSpellbookUI",
        [0x1000002F] = "gmSpellComponentUI",
        [0x10000030] = "UIElement_SmartBoxWrapper",
        [0x10000031] = "UIElement_ItemList",
        [0x10000032] = "UIElement_UIItem",
        [0x10000033] = "gmEnvPanelUI",
        [0x10000034] = "UIOption_ActionKeyMap",
        [0x10000035] = "UIOption_Checkbox",
        [0x10000036] = "UIOption_CheckboxSlider",
        [0x10000037] = "UIOption_Slider",
        [0x10000038] = "UIOption_Menu",
        [0x10000039] = "gmCGHeritagePage",
        [0x1000003A] = "gmCGProfessionPage",
        [0x1000003B] = "gmCGSkillsPage",
        [0x1000003C] = "gmCGAppearancePage",
        [0x1000003D] = "gmCGTownPage",
        [0x1000003E] = "gmCGSummaryPage",
        [0x1000003F] = "gmAdminQualitiesUI",
        [0x10000040] = "gmFloatyChatUI",
        [0x10000041] = "gmMainChatUI",
        [0x10000042] = "gmChatOptionsUI",
        [0x10000043] = "UIOption_CheckboxBitfield",
        [0x10000044] = "UIOption_CheckboxBitfield64",
        [0x10000045] = "gmFriendsUI",
        [0x10000046] = "gmCharacterTitleUI",
        [0x10000047] = "gmSquelchUI",
        [0x10000048] = "gmJournalUI",
        [0x10000049] = "gmPageListUI",
        [0x1000004A] = "gmBarberUI",
        [0x1000004B] = "gmContractsUI",
        [0x1000004C] = "gmFloatyExaminationUI",
        [0x1000004D] = "gmFloatyVitalsUI",
        [0x1000004E] = "gmFloatyEnvPanelUI",
        [0x1000004F] = "gmFloatyPanelUI",
        [0x10000050] = "gmFloatyMainChatUI",
        [0x10000051] = "gmFloatyToolbarUI",
        [0x10000052] = "gmFloatyIndicatorsUI",
        [0x10000053] = "gmFloatyPowerBarUI",
        [0x10000054] = "gmFloatyCombatPanelUI",
        [0x10000056] = "gmFloatySideVitalsUI",
    }.ToFrozenDictionary();

    private const string Category = "retailui";

    private readonly ConcurrentDictionary<uint, UiElementFactory> _factories = new();
    private readonly ConcurrentDictionary<uint, bool> _reported = new();

    /// <summary>
    /// A registry with every engine class (the 21 of <c>UIElementManager::Init</c>): the widgets, the Browser's loud stub and
    /// the seven dialogs. The game classes (0x10000001 and up) are the screen tracks' to register.
    /// </summary>
    public static UiClassRegistry CreateDefault()
    {
        var registry = new UiClassRegistry();
        registry.Register(UiClassIds.Button, static (in UiElementInit init) => new UiButton(init));
        registry.Register(UiClassIds.Dragbar, static (in UiElementInit init) => new UiDragbar(init));
        registry.Register(UiClassIds.Field, static (in UiElementInit init) => new UiField(init));
        registry.Register(UiClassIds.ListBox, static (in UiElementInit init) => new UiListBox(init));
        registry.Register(UiClassIds.Menu, static (in UiElementInit init) => new UiMenu(init));
        registry.Register(UiClassIds.Meter, static (in UiElementInit init) => new UiMeter(init));
        registry.Register(UiClassIds.Panel, static (in UiElementInit init) => new UiPanel(init));
        registry.Register(UiClassIds.ResizeBar, static (in UiElementInit init) => new UiResizeBar(init));
        registry.Register(UiClassIds.Scrollbar, static (in UiElementInit init) => new UiScrollbar(init));
        registry.Register(UiClassIds.Text, static (in UiElementInit init) => new UiTextElement(init));
        registry.Register(UiClassIds.Viewport, static (in UiElementInit init) => new UiViewport(init));
        registry.Register(UiClassIds.Browser, static (in UiElementInit init) => new UiBrowser(init));
        registry.Register(UiClassIds.ColorPicker, static (in UiElementInit init) => new UiColorPicker(init));
        registry.Register(UiClassIds.GroupBox, static (in UiElementInit init) => new UiGroupBox(init));
        registry.Register(UiClassIds.ConfirmationDialog, static (in UiElementInit init) => new UiConfirmationDialog(init));
        registry.Register(UiClassIds.ConfirmationMenuDialog, static (in UiElementInit init) => new UiConfirmationMenuDialog(init));
        registry.Register(UiClassIds.ConfirmationTextInputDialog, static (in UiElementInit init) => new UiConfirmationTextInputDialog(init));
        registry.Register(UiClassIds.MenuDialog, static (in UiElementInit init) => new UiMenuDialog(init));
        registry.Register(UiClassIds.MessageDialog, static (in UiElementInit init) => new UiMessageDialog(init));
        registry.Register(UiClassIds.TextInputDialog, static (in UiElementInit init) => new UiTextInputDialog(init));
        registry.Register(UiClassIds.WaitDialog, static (in UiElementInit init) => new UiWaitDialog(init));
        return registry;
    }

    /// <summary>Retail's name for class <paramref name="classId"/>, or <c>class 0x........</c> for one retail never registered.</summary>
    public static string NameOf(uint classId) =>
        RetailClassNames.TryGetValue(classId, out var name) ? name : $"class 0x{classId:X8}";

    /// <summary>
    /// Registers the runtime of class <paramref name="classId"/> (the widget stages add theirs; Lifestone's own panels may
    /// add ids retail never used). A later registration replaces an earlier one.
    /// </summary>
    public void Register(uint classId, UiElementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[classId] = factory;
    }

    /// <summary>True when class <paramref name="classId"/> has a Lifestone runtime.</summary>
    public bool IsImplemented(uint classId) => _factories.ContainsKey(classId);

    /// <summary>
    /// Creates the element for <paramref name="init"/>'s desc: its class's runtime; a generic stand-in for a retail class
    /// with no runtime yet (one warning per class); null for a class retail never registered (one error per class).
    /// </summary>
    internal UiElement? Create(in UiElementInit init, ILogSink log)
    {
        var classId = init.Desc.Type;
        if (_factories.TryGetValue(classId, out var factory))
        {
            return factory(init);
        }

        if (RetailClassNames.TryGetValue(classId, out var name))
        {
            if (_reported.TryAdd(classId, true))
            {
                log.Warn(Category, $"TODO(retailui-runtime): class 0x{classId:X8} ({name}) has no Lifestone runtime yet; a generic element stands in (placeholder behaviour, retail art)",
                    new LogField("element", $"0x{init.Desc.ElementId:X8}"), new LogField("layout", $"0x{init.Layout.Id:X8}"));
            }

            return new UiElement(init) { IsStandIn = true };
        }

        if (_reported.TryAdd(classId, true))
        {
            log.Error(Category, $"{init.Desc.Describe()} has class 0x{classId:X8}, which retail never registers; nothing is created (as retail)");
        }

        return null;
    }
}
