namespace Lifestone.Data.Ui;

/// <summary>
/// A mode's root: the UILAYOUT enum of its layout and the top-level element of that layout it creates, the two arguments
/// of <c>UIMainFramework::CreateAndAddRootElement</c> (<see cref="UiLayouts.GetRootDesc"/>).
/// </summary>
/// <param name="LayoutEnum">The layout's UILAYOUT enum (group 5).</param>
/// <param name="ElementId">The root element's id, a top-level element of that layout.</param>
public readonly record struct UiRootRef(uint LayoutEnum, uint ElementId)
{
    /// <summary>For logs: <c>0xLLLLLLLL/0xEEEEEEEE</c>.</summary>
    public override string ToString() => $"0x{LayoutEnum:X8}/0x{ElementId:X8}";
}

/// <summary>
/// The UILAYOUT enums (master group 5, map 0x2500000E) and mode roots that retail code names, each pinned by
/// LayoutCatalogueTests to the layout it resolves to and to the dat's name for it.
/// </summary>
/// <remarks>
/// <para>
/// The roots are the arguments of each mode's <c>CreateAndAddRootElement</c> call (11.4186 decompile, by function):
/// <c>gmDataPatchUI::gmDataPatchUI</c> (11.4186 0x004EE2B0, TODO(retailui-data): 11.6096),
/// <c>gmDisconnectedUI::gmDisconnectedUI</c> (11.4186 0x004E9A10, TODO(retailui-data): 11.6096),
/// <c>gmCharacterManagementUI::gmCharacterManagementUI</c> (11.4186 0x004EC8F0, TODO(retailui-data): 11.6096),
/// <c>gmCharGenMainUI::gmCharGenMainUI</c> (11.4186 0x004E7EB0, 11.6096 0x004E8B40) and
/// <c>gmGamePlayUI::SetupChildren</c> (11.4186 0x004E9EC0, 11.6096 0x004EAB50). The symbol table has no 11.6096 match for
/// the first three constructors.
/// </para>
/// <para>
/// <b>The gameplay root is the floating-window shell.</b> <see cref="ClassicGameplay"/> (named <c>classic_gameplay</c>)
/// resolves to LayoutDesc 0x21000005, whose root <see cref="GameplayRoot"/> 0x10000495 holds 18 windows, each a partial
/// desc inheriting from another layout: the SmartBox from <c>classic_smartbox</c>, the keyboard and admin windows, four
/// floating chat windows from the unnamed layout 0x2100005B, and eleven <c>classic_floaty*</c> windows (vitals, side
/// vitals, radar, main chat, toolbar, panel, environment panel, indicators, power bar, combat panel, examination). The
/// <c>classic_chat</c>, <c>classic_radar</c>, <c>classic_vitals</c>, <c>classic_toolbar</c> and <c>classic_panel</c>
/// layouts that m2-log-in.md §5.3 names are not children of that root. Through inheritance the windows' contents reuse
/// templates of 75 layouts, <c>classic_chat</c> and <c>classic_toolbar</c> among them, but never <c>classic_radar</c>,
/// <c>classic_vitals</c> or <c>classic_panel</c> (input to decision D36; m2-plan.md R2; ResolveAllElementsTests).
/// </para>
/// </remarks>
public static class UiLayoutEnums
{
    /// <summary><c>ContextMenu</c> (layout 0x2100003B).</summary>
    public const uint ContextMenu = 0x00000001;

    /// <summary><c>Dialog</c> (layout 0x2100003C): the message, confirmation, menu, text-input and wait dialogs.</summary>
    public const uint Dialog = 0x00000002;

    /// <summary><c>patch</c> (layout 0x21000000): the data check.</summary>
    public const uint Patch = 0x10000001;

    /// <summary><c>disconnected</c> (layout 0x21000002).</summary>
    public const uint Disconnected = 0x10000003;

    /// <summary><c>charactermanagement</c> (layout 0x21000004).</summary>
    public const uint CharacterManagement = 0x10000005;

    /// <summary><c>classic_gameplay</c> (layout 0x21000005): the in-game shell.</summary>
    public const uint ClassicGameplay = 0x10000006;

    /// <summary><c>classic_smartbox</c> (layout 0x2100000F): the SmartBox frame's template.</summary>
    public const uint ClassicSmartBox = 0x10000010;

    /// <summary><c>chargen_master</c> (layout 0x21000038): character creation.</summary>
    public const uint ChargenMaster = 0x10000039;

    /// <summary>The data-check screen's root (<c>gmDataPatchUI::gmDataPatchUI</c>).</summary>
    public static readonly UiRootRef DataPatchRoot = new(Patch, 0x1000041A);

    /// <summary>The disconnected screen's root (<c>gmDisconnectedUI::gmDisconnectedUI</c>).</summary>
    public static readonly UiRootRef DisconnectedRoot = new(Disconnected, 0x10000416);

    /// <summary>Character management's root (<c>gmCharacterManagementUI::gmCharacterManagementUI</c>).</summary>
    public static readonly UiRootRef CharacterManagementRoot = new(CharacterManagement, 0x1000039A);

    /// <summary>Character creation's root (<c>gmCharGenMainUI::gmCharGenMainUI</c>).</summary>
    public static readonly UiRootRef CharGenRoot = new(ChargenMaster, 0x100003CC);

    /// <summary>The in-game shell's root (<c>gmGamePlayUI::SetupChildren</c>).</summary>
    public static readonly UiRootRef GameplayRoot = new(ClassicGameplay, 0x10000495);

    /// <summary>
    /// The children of <see cref="GameplayRoot"/> that <c>gmGamePlayUI</c> finds by id (<c>GetChildRecursive</c> in
    /// <c>gmGamePlayUI::SetupChildren</c>).
    /// </summary>
    public static class Gameplay
    {
        /// <summary>The SmartBox frame (<c>m_pSmartBoxUI</c>), from <c>classic_smartbox</c>.</summary>
        public const uint SmartBox = 0x1000049A;

        /// <summary>The radar (<c>m_pSmartBoxRadar</c>), from <c>classic_floatyradar</c>.</summary>
        public const uint Radar = 0x100006D2;

        /// <summary>The stacked vitals (<c>m_pStackedVitals</c>), from <c>classic_floatyvitals</c>.</summary>
        public const uint StackedVitals = 0x100005FA;

        /// <summary>The side-by-side vitals (<c>m_pSideBySideVitals</c>), from <c>classic_floatysidevitals</c>.</summary>
        public const uint SideBySideVitals = 0x100006D5;

        /// <summary>The four floating chat windows, in the order <c>SetupChildren</c> titles them (Chat 1 to Chat 4).</summary>
        public static IReadOnlyList<uint> FloatingChats { get; } = [0x10000505, 0x1000050E, 0x1000050F, 0x10000510];
    }
}
