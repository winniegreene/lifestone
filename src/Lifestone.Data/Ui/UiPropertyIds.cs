namespace Lifestone.Data.Ui;

/// <summary>
/// The UI property ids the retail UI runtime reads: every property of the core widget classes in the master property table
/// (0x39000001, <see cref="MasterPropertyTable"/>), plus the chat, spew-box and radar properties of the M2 shell. Retail's
/// class handlers switch on these ids when a desc or a state sets them (<c>OnSetAttribute</c>, one per class).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pinned to the dats.</b> MasterPropertyTests checks every constant against the table: the constant's name is the
/// table's name for the id without its <c>UICore_</c> or <c>UI_</c> prefix and its underscores (compared ignoring case,
/// so <see cref="TextEntry"/> is the Text class's <c>entry</c> property), and the <see cref="PropertyTypeAttribute"/> on
/// it is the table's type for the id. A wrong id fails that test, not a screen.
/// </para>
/// <para>
/// The retail layouts set 155 property ids (UiLayoutCensusTests); the runtime reads others that only code sets (a
/// button's highlighted flag, a meter's position, a dialog's response). Read values through <see cref="UiPropertyLookup"/>,
/// which throws on a type mismatch.
/// </para>
/// </remarks>
public static class UiPropertyIds
{
    // Menu (class Menu).
    [PropertyType(UiPropertyType.Enum)] public const uint MenuDefaultSelectedItem = 0x1;
    [PropertyType(UiPropertyType.Enum)] public const uint MenuListbox = 0x2;
    [PropertyType(UiPropertyType.Bool)] public const uint MenuOpenCenter = 0x3;
    [PropertyType(UiPropertyType.Bool)] public const uint MenuOpenDown = 0x4;
    [PropertyType(UiPropertyType.Bool)] public const uint MenuOpenUp = 0x5;
    [PropertyType(UiPropertyType.Enum)] public const uint MenuPopup = 0x6;
    [PropertyType(UiPropertyType.DataId)] public const uint MenuPopupLayout = 0x7;
    [PropertyType(UiPropertyType.Enum)] public const uint MenuSelectionDisplay = 0x8;
    [PropertyType(UiPropertyType.Enum)] public const uint MenuTextItem = 0x9;
    [PropertyType(UiPropertyType.DataId)] public const uint MenuTextItemLayout = 0xA;

    // Button (<c>UIElement_Button::OnSetAttribute</c>, 11.4186 0x00471F40, 11.6096 0x00472300).
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonBooleanButton = 0xB;
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonBroadcastWhenGhosted = 0xC;
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonGhosted = 0xD;
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonHighlighted = 0xE;
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonHotButton = 0xF;
    [PropertyType(UiPropertyType.Float)] public const uint ButtonHotClickFirstInterval = 0x10;
    [PropertyType(UiPropertyType.Float)] public const uint ButtonHotClickInterval = 0x11;
    [PropertyType(UiPropertyType.Enum)] public const uint ButtonClickAction = 0x12;
    [PropertyType(UiPropertyType.Bool)] public const uint ButtonRollover = 0x13;

    // Text (<c>UIElement_Text::OnSetAttribute</c>, 11.4186 0x0046A640, 11.6096 0x0046A8D0).
    [PropertyType(UiPropertyType.Enum)] public const uint TextHorizontalJustification = 0x14;
    [PropertyType(UiPropertyType.Enum)] public const uint TextVerticalJustification = 0x15;
    [PropertyType(UiPropertyType.Bool)] public const uint TextEditable = 0x16;
    [PropertyType(UiPropertyType.StringInfo)] public const uint TextEntry = 0x17;
    [PropertyType(UiPropertyType.DataId)] public const uint TextFont = 0x18;
    [PropertyType(UiPropertyType.Color)] public const uint TextFontColor = 0x19;
    [PropertyType(UiPropertyType.Array)] public const uint TextFonts = 0x1A;
    [PropertyType(UiPropertyType.Array)] public const uint TextFontColors = 0x1B;
    [PropertyType(UiPropertyType.Array)] public const uint TextTagFonts = 0x1C;
    [PropertyType(UiPropertyType.Array)] public const uint TextTagFontColors = 0x1D;
    [PropertyType(UiPropertyType.Integer)] public const uint TextMaxChars = 0x1E;
    [PropertyType(UiPropertyType.Bool)] public const uint TextNoIme = 0x1F;
    [PropertyType(UiPropertyType.Bool)] public const uint TextOneLine = 0x20;
    [PropertyType(UiPropertyType.Bool)] public const uint TextOutline = 0x21;
    [PropertyType(UiPropertyType.Color)] public const uint TextFontOutlineColor = 0x22;
    [PropertyType(UiPropertyType.Integer)] public const uint TextLeftMargin = 0x23;
    [PropertyType(UiPropertyType.Integer)] public const uint TextRightMargin = 0x24;
    [PropertyType(UiPropertyType.Integer)] public const uint TextTopMargin = 0x25;
    [PropertyType(UiPropertyType.Integer)] public const uint TextBottomMargin = 0x26;
    [PropertyType(UiPropertyType.Bool)] public const uint TextSelectable = 0x27;
    [PropertyType(UiPropertyType.Bool)] public const uint TextTrimFromTop = 0x28;
    [PropertyType(UiPropertyType.Bool)] public const uint TextFitToText = 0x29;
    [PropertyType(UiPropertyType.StringInfo)] public const uint TextTruncateTextToFit = 0xC7;
    [PropertyType(UiPropertyType.Bool)] public const uint TextLoseFocusOnEscape = 0xCB;
    [PropertyType(UiPropertyType.Bool)] public const uint TextLoseFocusOnAcceptInput = 0xCC;
    [PropertyType(UiPropertyType.Bool)] public const uint TextAutoTooltipTruncatedText = 0xD0;
    [PropertyType(UiPropertyType.Bool)] public const uint TextSelectAllOnGainFocusMouseDown = 0xD1;

    // Resize bar.
    [PropertyType(UiPropertyType.Bool)] public const uint ResizebarBorderBottom = 0x2A;
    [PropertyType(UiPropertyType.Bool)] public const uint ResizebarBorderLeft = 0x2B;
    [PropertyType(UiPropertyType.Bool)] public const uint ResizebarBorderRight = 0x2C;
    [PropertyType(UiPropertyType.Bool)] public const uint ResizebarBorderTop = 0x2D;

    // Panel (tabbed pages).
    [PropertyType(UiPropertyType.Array)] public const uint PanelPages = 0x2E;
    [PropertyType(UiPropertyType.Struct)] public const uint PanelPageData = 0x2F;
    [PropertyType(UiPropertyType.Enum)] public const uint PanelTabElement = 0x30;
    [PropertyType(UiPropertyType.Enum)] public const uint PanelPageElement = 0x31;
    [PropertyType(UiPropertyType.Bool)] public const uint PanelPageOpen = 0x32;

    // Every element (<c>UIElement::OnSetAttribute</c>, 11.4186 0x00462D80, 11.6096 0x00462E60).
    [PropertyType(UiPropertyType.Bool)] public const uint ElementActivatable = 0x33;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementActivateOnShow = 0x34;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementFocusOnShow = 0x35;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementContainer = 0x36;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementContextMenu = 0x37;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementDisallowDragIn = 0x38;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementDisallowDragOut = 0x39;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementDragable = 0x3A;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementHide = 0x3B;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementMaxHeight = 0x3C;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementMaxWidth = 0x3D;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementMinHeight = 0x3E;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementMinWidth = 0x3F;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementBlocksClicks = 0x40;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementNotifyOnResize = 0x41;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementNotifyOnMove = 0x42;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementNotifyOnCreate = 0x43;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementResizeLine = 0x44;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementSaveLoc = 0x45;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementSaveSize = 0x46;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementTooltipId = 0x47;
    [PropertyType(UiPropertyType.DataId)] public const uint ElementTooltipLayout = 0x48;
    [PropertyType(UiPropertyType.StringInfo)] public const uint ElementTooltipEntry = 0x49;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementTooltipTextId = 0x4A;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementTooltipsOn = 0x4B;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementViewport = 0x4C;
    [PropertyType(UiPropertyType.Float)] public const uint ElementAlphaBlendMod = 0x4D;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementInputMapId = 0x4E;
    [PropertyType(UiPropertyType.Float)] public const uint ElementHoverDelay = 0x50;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementShouldEraseBackground = 0x51;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementClampGameViewEdgeToObject = 0x52;
    [PropertyType(UiPropertyType.Bool)] public const uint ElementDrawAfterChildren = 0x53;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementImageTileOffsetX = 0x54;
    [PropertyType(UiPropertyType.Integer)] public const uint ElementImageTileOffsetY = 0x55;
    [PropertyType(UiPropertyType.Struct)] public const uint ElementImageTileOffset = 0x56;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementVisibilityToggleInputAction = 0x57;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementVisibilityToggleToggleType = 0x58;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementObjectMode = 0xCD;
    [PropertyType(UiPropertyType.Enum)] public const uint ElementPanelId = 0x10000029;

    // List box.
    [PropertyType(UiPropertyType.Bool)] public const uint ListBoxClickSelect = 0x59;
    [PropertyType(UiPropertyType.Bool)] public const uint ListBoxDragRollover = 0x5A;
    [PropertyType(UiPropertyType.Bool)] public const uint ListBoxDragSelect = 0x5B;
    [PropertyType(UiPropertyType.Bool)] public const uint ListBoxHorizontal = 0x5C;
    [PropertyType(UiPropertyType.Enum)] public const uint ListBoxItemNormalState = 0x5D;
    [PropertyType(UiPropertyType.Enum)] public const uint ListBoxItemSelectedState = 0x5E;
    [PropertyType(UiPropertyType.Integer)] public const uint ListBoxMaxColumns = 0x5F;
    [PropertyType(UiPropertyType.Integer)] public const uint ListBoxMaxRows = 0x60;
    [PropertyType(UiPropertyType.Bool)] public const uint ListBoxSelectedItemStateChange = 0x61;
    [PropertyType(UiPropertyType.Enum)] public const uint ListBoxEntryTemplateElement = 0x62;
    [PropertyType(UiPropertyType.DataId)] public const uint ListBoxEntryTemplateLayout = 0x63;
    [PropertyType(UiPropertyType.Array)] public const uint ListBoxEntryTemplates = 0x64;
    [PropertyType(UiPropertyType.Struct)] public const uint ListBoxEntryTemplate = 0x65;

    // Meter.
    [PropertyType(UiPropertyType.Float)] public const uint MeterGoalPosition = 0x66;
    [PropertyType(UiPropertyType.Bool)] public const uint MeterFrameMeter = 0x67;
    [PropertyType(UiPropertyType.Bool)] public const uint MeterMoveFill = 0x68;
    [PropertyType(UiPropertyType.Float)] public const uint MeterPosition = 0x69;
    [PropertyType(UiPropertyType.Bool)] public const uint MeterSmoothMovement = 0x6A;
    [PropertyType(UiPropertyType.Float)] public const uint MeterSmoothMovementDuration = 0x6B;
    [PropertyType(UiPropertyType.DataId)] public const uint MeterFrame = 0x6C;
    [PropertyType(UiPropertyType.Array)] public const uint MeterFrameArray = 0x6D;
    [PropertyType(UiPropertyType.Enum)] public const uint MeterFrameArrayDrawMode = 0x6E;
    [PropertyType(UiPropertyType.Enum)] public const uint MeterChildDirection = 0x6F;

    // Field (the container and drop target).
    [PropertyType(UiPropertyType.Bool)] public const uint FieldDragRollover = 0x70;

    // Scrollable.
    [PropertyType(UiPropertyType.Enum)] public const uint ScrollableHorizontalScrollbar = 0x71;
    [PropertyType(UiPropertyType.Enum)] public const uint ScrollableVerticalScrollbar = 0x72;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollableKeepInBounds = 0x73;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollablePreservePercentageOnResize = 0x74;

    // Scroll bar.
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarDisallowUpdating = 0x75;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarDisabled = 0x76;
    [PropertyType(UiPropertyType.Enum)] public const uint ScrollbarIncrementButton = 0x77;
    [PropertyType(UiPropertyType.Enum)] public const uint ScrollbarDecrementButton = 0x78;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarHideWhenDisabled = 0x79;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarGhostWhenDisabled = 0x7A;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarHorizontal = 0x7B;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarMoveToTouchedPosition = 0x7C;
    [PropertyType(UiPropertyType.Integer)] public const uint ScrollbarNumStopLocations = 0x7D;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarStopsDontIncludeEndpoints = 0x7E;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarStopLocations = 0x7F;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarPageFirstInterval = 0x80;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarPageInterval = 0x81;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarProportional = 0x82;
    [PropertyType(UiPropertyType.Bool)] public const uint ScrollbarSmoothMovement = 0x83;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarSmoothMovementDuration = 0x84;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarGoalPosition = 0x85;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarPosition = 0x86;
    [PropertyType(UiPropertyType.Integer)] public const uint ScrollbarCurrentStop = 0x87;
    [PropertyType(UiPropertyType.Float)] public const uint ScrollbarWidgetSize = 0x88;
    [PropertyType(UiPropertyType.Integer)] public const uint ScrollbarMinWidgetSize = 0x89;

    // Context menu.
    [PropertyType(UiPropertyType.Float)] public const uint ContextMenuOpenDelay = 0x8A;
    [PropertyType(UiPropertyType.Integer)] public const uint ContextMenuLvl = 0x8B;
    [PropertyType(UiPropertyType.Integer)] public const uint ContextMenuIndex = 0x8C;

    // Dialogs.
    [PropertyType(UiPropertyType.Bool)] public const uint DialogTopmost = 0x8D;
    [PropertyType(UiPropertyType.Enum)] public const uint DialogType = 0x8E;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationYesButtonText = 0x90;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationNoButtonText = 0x91;
    [PropertyType(UiPropertyType.Bool)] public const uint DialogConfirmationResponse = 0x92;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogMessageOkButtonText = 0x95;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogTextInputDoneButtonText = 0x97;
    [PropertyType(UiPropertyType.String)] public const uint DialogTextInputResponse = 0x98;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationTextInputDoneButtonText = 0x9A;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationTextInputCancelButtonText = 0x9B;
    [PropertyType(UiPropertyType.String)] public const uint DialogConfirmationTextInputResponse = 0x9C;
    [PropertyType(UiPropertyType.Enum)] public const uint DialogWaitKey = 0x9E;
    [PropertyType(UiPropertyType.Array)] public const uint DialogMenuItemArray = 0xA0;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogMenuItemText = 0xA1;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogMenuOkButtonText = 0xA2;
    [PropertyType(UiPropertyType.Integer)] public const uint DialogMenuChoice = 0xA4;
    [PropertyType(UiPropertyType.Array)] public const uint DialogConfirmationMenuItemArray = 0xA6;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationMenuItemText = 0xA7;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationMenuOkButtonText = 0xA8;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogConfirmationMenuCancelButtonText = 0xA9;
    [PropertyType(UiPropertyType.Integer)] public const uint DialogConfirmationMenuChoice = 0xAB;
    [PropertyType(UiPropertyType.Bool)] public const uint DialogModal = 0xAC;
    [PropertyType(UiPropertyType.Enum)] public const uint DialogQueue = 0xC3;
    [PropertyType(UiPropertyType.StringInfo)] public const uint DialogText = 0xC5;
    [PropertyType(UiPropertyType.Float)] public const uint DialogDuration = 0xC6;

    // Browser (a loud stub in M2).
    [PropertyType(UiPropertyType.String)] public const uint BrowserUrl = 0xAD;

    // Colour picker.
    [PropertyType(UiPropertyType.Bool)] public const uint ColorPickerShowSelection = 0xAE;
    [PropertyType(UiPropertyType.Color)] public const uint ColorPickerCurrentSelection = 0xAF;

    // Group box.
    [PropertyType(UiPropertyType.Enum)] public const uint GroupBoxDefaultButton = 0xB0;
    [PropertyType(UiPropertyType.Enum)] public const uint GroupBoxSelectedButton = 0xB1;
    [PropertyType(UiPropertyType.Bool)] public const uint GroupBoxAllowButtonClickOnSelectedButton = 0xC1;

    // Chat windows (game UI).
    [PropertyType(UiPropertyType.Enum)] public const uint ChatTalkFocus = 0x1000000B;
    [PropertyType(UiPropertyType.Enum)] public const uint ChatWindowId = 0x1000007E;

    // Spew box (game UI; the portal-space notice).
    [PropertyType(UiPropertyType.Integer)] public const uint SpewBoxMaxMessagesToDisplay = 0x10000028;

    // Radar (game UI).
    [PropertyType(UiPropertyType.Integer)] public const uint RadarRadius = 0x1000002D;
    [PropertyType(UiPropertyType.Struct)] public const uint RadarCenterPoint = 0x1000002E;
    [PropertyType(UiPropertyType.Integer)] public const uint RadarX = 0x1000002F;
    [PropertyType(UiPropertyType.Integer)] public const uint RadarY = 0x10000030;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarNorthToken = 0x10000031;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarSouthToken = 0x10000032;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarEastToken = 0x10000033;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarWestToken = 0x10000034;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarCoordinatesContainer = 0x10000035;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarCombinedCoordsField = 0x10000036;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarXCoordField = 0x10000037;
    [PropertyType(UiPropertyType.Enum)] public const uint RadarYCoordField = 0x10000038;
}

/// <summary>The value type the master property table gives a <see cref="UiPropertyIds"/> constant (checked by MasterPropertyTests).</summary>
/// <param name="type">The property's value type.</param>
[AttributeUsage(AttributeTargets.Field)]
internal sealed class PropertyTypeAttribute(UiPropertyType type) : Attribute
{
    /// <summary>The property's value type.</summary>
    public UiPropertyType Type { get; } = type;
}
